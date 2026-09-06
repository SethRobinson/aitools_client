using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// A live-updating, ALWAYS-visible "Web" chat bubble that shows every web search and
    /// download in full (query, full result list, each download attempt, yt-dlp command
    /// line + output) so malfunctions are obvious. Lines are plain text: the host's escape
    /// function only neutralizes TMP angle brackets, no markdown pass, because URLs carry
    /// '*', '#', '_' and '-' that the markdown converter would mangle. A trailing status
    /// line (progress) is replaced in place and throttled so per-frame updates stay cheap.
    /// Every appended line is also written to the editor AI Chat log as a "web" note.
    /// </summary>
    public sealed class WebTraceBubble
    {
        private const float StatusRenderInterval = 0.25f;

        private readonly TMP_InputField _field;
        private readonly Func<string, string> _escape;
        private readonly Func<bool> _isScrolledToBottom;
        private readonly Action _scrollToBottom;
        private readonly Func<WebThumbStrip> _createThumbStrip;
        private WebThumbStrip _thumbs;
        private readonly List<string> _lines = new List<string>();
        private string _statusLine;
        private float _lastStatusRenderTime = -1f;
        private bool _statusDirty;

        /// <param name="field">The bubble's body field (from AIChatPanel.AppendBubble).</param>
        /// <param name="escape">Display-only escape (TMP angle brackets).</param>
        /// <param name="isScrolledToBottom">Queried BEFORE each text change so the chat keeps following the bubble only when the user was already at the bottom.</param>
        /// <param name="scrollToBottom">Invoked after a change when the chat was at the bottom.</param>
        /// <param name="createThumbStrip">Builds the thumbnail strip under this bubble on the first <see cref="AddThumb"/> (null = no thumbnails).</param>
        public WebTraceBubble(TMP_InputField field, Func<string, string> escape, Func<bool> isScrolledToBottom, Action scrollToBottom,
            Func<WebThumbStrip> createThumbStrip = null)
        {
            _field = field;
            _escape = escape;
            _isScrolledToBottom = isScrolledToBottom;
            _scrollToBottom = scrollToBottom;
            _createThumbStrip = createThumbStrip;
        }

        // UnityEngine.Object's overloaded null check is false once the bubble was destroyed (Clear).
        public bool IsAlive => _field != null;

        /// <summary>The thumbnail strip, once at least one thumbnail was added (null before that).</summary>
        public WebThumbStrip Thumbs => _thumbs != null ? _thumbs : null;

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

        public void AppendLine(string line)
        {
            line = line ?? "";
            _lines.Add(line);
            AIChatLog.Note("web", line);
            Render();
        }

        public void AppendLines(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (var line in lines)
            {
                _lines.Add(line ?? "");
                AIChatLog.Note("web", line ?? "");
            }
            Render();
        }

        /// <summary>Replace the trailing progress line. Throttled; the next AppendLine/ClearStatus flushes it.</summary>
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

        /// <summary>Promote the current status line into a permanent line (e.g. the final progress state).</summary>
        public void CommitStatus()
        {
            if (!string.IsNullOrEmpty(_statusLine))
            {
                _lines.Add(_statusLine);
                AIChatLog.Note("web", _statusLine);
            }
            _statusLine = null;
            _statusDirty = false;
            Render();
        }

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

        private void Render()
        {
            _lastStatusRenderTime = Time.unscaledTime;
            _statusDirty = false;
            if (!IsAlive) return;
            string raw = GetRawText();
            string shown = _escape != null ? _escape(raw) : raw;
            bool follow = false;
            try { follow = _isScrolledToBottom != null && _isScrolledToBottom(); } catch { }
            try
            {
                _field.text = shown;
            }
            catch (Exception)
            {
                return;
            }
            if (follow)
            {
                try { _scrollToBottom?.Invoke(); } catch { }
            }
        }
    }
}
