using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// A live-updating, ALWAYS-visible "Web" chat bubble for one web search / download.
    /// The bubble itself stays COMPACT: a one-line title (the skill and what it fetched),
    /// the outcome lines the host marks as summary (added #N, Done / No usable...,
    /// warnings, Cancelled), the throttled status line and the thumbnail strip. EVERY line
    /// (search hits, the ranked download attempts, HTTP results, vision verdicts, the
    /// yt-dlp command line + output, captions) goes to the FULL trace, which a click on
    /// the bubble's "[details]" marker opens in the floating window the "[thinking]"
    /// marker uses, live while the fetch still runs (the host's change hook). Lines are
    /// plain text: the host's escape function only neutralizes TMP angle brackets, no
    /// markdown pass, because URLs carry '*', '#', '_' and '-' that the markdown converter
    /// would mangle. Every appended line is also written to the editor AI Chat log as a
    /// "web" note, and the whole trace as a "web_trace" note when the fetch ends.
    /// </summary>
    public sealed class WebTraceBubble
    {
        private const float StatusRenderInterval = 0.25f;

        private readonly TMP_InputField _field;
        private readonly Func<string, string> _escape;
        private readonly Func<bool> _isScrolledToBottom;
        private readonly Action _scrollToBottom;
        private readonly Func<WebThumbStrip> _createThumbStrip;
        private readonly string _detailsMarkerMarkup;
        private readonly Action<WebTraceBubble> _onChanged;
        private WebThumbStrip _thumbs;
        // Full trace (what the details window and the log get) and the subset shown in the bubble.
        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _summaryLines = new List<string>();
        private string _title = "";
        private int _hiddenLineCount;
        private bool _finished;
        private string _statusLine;
        private float _lastStatusRenderTime = -1f;
        private bool _statusDirty;

        /// <param name="field">The bubble's body field (from AIChatPanel.AppendBubble).</param>
        /// <param name="escape">Display-only escape (TMP angle brackets).</param>
        /// <param name="isScrolledToBottom">Queried BEFORE each text change so the chat keeps following the bubble only when the user was already at the bottom.</param>
        /// <param name="scrollToBottom">Invoked after a change when the chat was at the bottom.</param>
        /// <param name="createThumbStrip">Builds the thumbnail strip under this bubble on the first <see cref="AddThumb"/> (null = no thumbnails).</param>
        /// <param name="detailsMarkerMarkup">TMP rich text appended to the title line while the full trace holds lines the bubble hides (the clickable "[details]" link); null = no marker.</param>
        /// <param name="onChanged">Called after every render so the host can mirror the full text into an open details window.</param>
        public WebTraceBubble(TMP_InputField field, Func<string, string> escape, Func<bool> isScrolledToBottom, Action scrollToBottom,
            Func<WebThumbStrip> createThumbStrip = null, string detailsMarkerMarkup = null, Action<WebTraceBubble> onChanged = null)
        {
            _field = field;
            _escape = escape;
            _isScrolledToBottom = isScrolledToBottom;
            _scrollToBottom = scrollToBottom;
            _createThumbStrip = createThumbStrip;
            _detailsMarkerMarkup = detailsMarkerMarkup;
            _onChanged = onChanged;
        }

        // UnityEngine.Object's overloaded null check is false once the bubble was destroyed (Clear).
        public bool IsAlive => _field != null;

        /// <summary>The bubble's body field (identifies the bubble to the details window).</summary>
        public TMP_InputField Field => _field;

        /// <summary>The compact one-line title shown at the top of the bubble.</summary>
        public string Title => _title;

        /// <summary>True once the host ended (or cancelled) the fetch; the details window stops saying "working".</summary>
        public bool IsFinished => _finished;

        /// <summary>Lines in the full trace (the status line not included).</summary>
        public int LineCount => _lines.Count;

        /// <summary>Lines shown in the bubble below the title.</summary>
        public int SummaryLineCount => _summaryLines.Count;

        /// <summary>True when the full trace holds lines the bubble does not show, i.e. the "[details]" marker is rendered.</summary>
        public bool HasHiddenDetails => _hiddenLineCount > 0;

        /// <summary>The thumbnail strip, once at least one thumbnail was added (null before that).</summary>
        public WebThumbStrip Thumbs => _thumbs != null ? _thumbs : null;

        /// <summary>
        /// First line of the trace: the raw action line (skill + every attribute) goes to the
        /// full trace; the bubble shows <paramref name="compactTitle"/> instead when one is given
        /// (a one-line notice passes null and is shown as-is).
        /// </summary>
        public void SetHeader(string headerLine, string compactTitle)
        {
            headerLine = headerLine ?? "";
            bool replaced = !string.IsNullOrEmpty(compactTitle);
            _title = replaced ? compactTitle : headerLine;
            if (headerLine.Length > 0)
            {
                _lines.Add(headerLine);
                AIChatLog.Note("web", headerLine);
                if (replaced) _hiddenLineCount++;
            }
            Render();
        }

        /// <summary>
        /// Show a thumbnail of an image this fetch downloaded and examined (accepted or not).
        /// The strip is created lazily so list-only bubbles (web_search, web_page) stay text-only.
        /// Returns null when thumbnails are unavailable or the bytes do not decode.
        /// </summary>
        public WebThumbEntry AddThumb(byte[] imageBytes, string filePath, string label, string title)
        {
            if (!IsAlive || _createThumbStrip == null) return null;
            if (_thumbs == null)
            {
                try { _thumbs = _createThumbStrip(); }
                catch (Exception ex) { Debug.LogWarning("WebTraceBubble: could not create the thumbnail strip: " + ex.Message); }
                if (_thumbs == null) return null;
            }
            bool follow = false;
            try { follow = _isScrolledToBottom != null && _isScrolledToBottom(); } catch { }
            WebThumbEntry entry = _thumbs.AddThumb(imageBytes, filePath, label, title);
            if (follow)
            {
                try { _scrollToBottom?.Invoke(); } catch { }
            }
            return entry;
        }

        public void SetThumbVerdict(WebThumbEntry entry, WebThumbVerdict verdict, string reason)
        {
            if (entry == null || _thumbs == null) return;
            _thumbs.SetVerdict(entry, verdict, reason);
        }

        /// <summary>Link a thumbnail to the world Pic that now holds the media, so a click focuses it instead of loading a copy.</summary>
        public void SetThumbPic(WebThumbEntry entry, PicMain pic)
        {
            if (entry == null || _thumbs == null) return;
            _thumbs.SetLinkedPic(entry, pic);
        }

        /// <summary>A detail line: full trace (and the details window) only, not the bubble.</summary>
        public void AppendLine(string line)
        {
            line = line ?? "";
            _lines.Add(line);
            _hiddenLineCount++;
            AIChatLog.Note("web", line);
            Render();
        }

        /// <summary>An outcome line: shown in the bubble AND in the full trace.</summary>
        public void AppendSummaryLine(string line)
        {
            line = line ?? "";
            _lines.Add(line);
            _summaryLines.Add(line);
            AIChatLog.Note("web", line);
            Render();
        }

        /// <summary>Detail lines (see <see cref="AppendLine"/>).</summary>
        public void AppendLines(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (var line in lines)
            {
                _lines.Add(line ?? "");
                _hiddenLineCount++;
                AIChatLog.Note("web", line ?? "");
            }
            Render();
        }

        /// <summary>Replace the trailing progress line (shown in the bubble and the details window). Throttled; the next AppendLine/ClearStatus flushes it.</summary>
        public void SetStatus(string line)
        {
            _statusLine = line;
            _statusDirty = true;
            if (_lastStatusRenderTime < 0f || Time.unscaledTime - _lastStatusRenderTime >= StatusRenderInterval)
                Render();
        }

        public void ClearStatus()
        {
            _statusLine = null;
            _statusDirty = false;
            Render();
        }

        /// <summary>Promote the current status line into a permanent detail line (e.g. the final progress state).</summary>
        public void CommitStatus()
        {
            if (!string.IsNullOrEmpty(_statusLine))
            {
                _lines.Add(_statusLine);
                _hiddenLineCount++;
                AIChatLog.Note("web", _statusLine);
            }
            _statusLine = null;
            _statusDirty = false;
            Render();
        }

        /// <summary>The fetch ended (done, failed or cancelled): an open details window stops following as live.</summary>
        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            Render();
        }

        /// <summary>The full trace: every line plus the current status line.</summary>
        public string GetRawText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(_lines[i]);
            }
            if (!string.IsNullOrEmpty(_statusLine))
            {
                if (_lines.Count > 0) sb.Append('\n');
                sb.Append(_statusLine);
            }
            return sb.ToString();
        }

        /// <summary>What the bubble shows (unescaped, without the marker): title, summary lines, status line.</summary>
        public string GetCompactText()
        {
            var sb = new StringBuilder();
            sb.Append(_title ?? "");
            for (int i = 0; i < _summaryLines.Count; i++)
                sb.Append('\n').Append(_summaryLines[i]);
            if (!string.IsNullOrEmpty(_statusLine))
                sb.Append('\n').Append(_statusLine);
            return sb.ToString();
        }

        private string Esc(string s)
        {
            return _escape != null ? _escape(s ?? "") : (s ?? "");
        }

        private void Render()
        {
            _lastStatusRenderTime = Time.unscaledTime;
            _statusDirty = false;
            if (!IsAlive) return;
            var sb = new StringBuilder();
            sb.Append(Esc(_title));
            if (HasHiddenDetails && !string.IsNullOrEmpty(_detailsMarkerMarkup))
                sb.Append(sb.Length > 0 ? "  " : "").Append(_detailsMarkerMarkup);
            for (int i = 0; i < _summaryLines.Count; i++)
                sb.Append('\n').Append(Esc(_summaryLines[i]));
            if (!string.IsNullOrEmpty(_statusLine))
                sb.Append('\n').Append(Esc(_statusLine));
            bool follow = false;
            try { follow = _isScrolledToBottom != null && _isScrolledToBottom(); } catch { }
            try
            {
                _field.text = sb.ToString();
            }
            catch (Exception)
            {
                return;
            }
            if (follow)
            {
                try { _scrollToBottom?.Invoke(); } catch { }
            }
            try { _onChanged?.Invoke(this); } catch (Exception ex) { Debug.LogWarning("WebTraceBubble: change hook failed: " + ex.Message); }
        }
    }
}
