using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// Streaming-safe extractor for <c>&lt;aitools_action ... /&gt;</c> tags inside an
    /// LLM token stream. The host calls <see cref="Feed"/> with each chunk, then
    /// <see cref="ConsumeDisplayText"/> to get text safe to render in the chat bubble
    /// (with action tags stripped from the visible text).
    /// Whenever a complete tag is detected, <see cref="OnActionParsed"/> fires.
    ///
    /// Tolerates:
    /// <list type="bullet">
    /// <item>Self-closing form: <c>&lt;aitools_action attr="..." /&gt;</c></item>
    /// <item>Paired form: <c>&lt;aitools_action attr="..."&gt;...&lt;/aitools_action&gt;</c></item>
    /// <item>Single or double quoted attribute values.</item>
    /// <item>JSON-style backslash escapes inside attribute values (e.g.
    /// <c>prompt="she shouts \"hi!\" then leaves"</c>) - LLMs default to this
    /// even though it isn't legal XML. We accept and decode it.</item>
    /// <item>XML-entity escapes (<c>&amp;quot;</c>, <c>&amp;amp;</c>, etc.) inside attribute values.</item>
    /// <item>Whitespace, newlines, missing trailing slash before <c>&gt;</c>.</item>
    /// <item>Mid-stream chunk boundaries inside a tag (buffer holds until close).</item>
    /// </list>
    ///
    /// NOT a real XML parser - regex against the buffered text is plenty for the small
    /// allow-listed tag set we care about here. We intentionally never produce false
    /// positives: any <c>&lt;</c> that doesn't begin our tag is treated as plain text.
    /// </summary>
    public class SkillActionParser
    {
        public event Action<SkillAction> OnActionParsed;

        private readonly StringBuilder _buffer = new StringBuilder();
        private int _imageBubbleCounter = 0;
        private bool _suppressLeadingLineBreakAfterRemovedMediaAction = false;

        private const string TagOpen = "<aitools_action";
        private const string RemovedMediaActionMarker = "\uE000AIT_MEDIA_ACTION_REMOVED\uE000";

        // Self-closing: <aitools_action ... />
        private static readonly Regex SelfClosingRx = new Regex(
            @"<aitools_action\b([^>]*?)/\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Paired: <aitools_action ...>BODY</aitools_action>  (BODY is ignored)
        private static readonly Regex PairedRx = new Regex(
            @"<aitools_action\b([^>]*?)>(?:[\s\S]*?)</aitools_action\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Attribute parsers: key="value" or key='value'
        // The value may contain backslash-escaped quotes (\" or \') because LLMs
        // reflexively JSON-escape embedded quotes when writing what looks like a
        // tool call. The grammar is the standard JSON-string body: any char that
        // isn't an unescaped quote/backslash, OR a backslash followed by anything.
        // We unescape the captured value via DecodeBackslashEscapes after the match.
        private static readonly Regex AttrDoubleQuoteRx = new Regex(
            @"([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*""((?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);
        private static readonly Regex AttrSingleQuoteRx = new Regex(
            @"([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*'((?:[^'\\]|\\.)*)'",
            RegexOptions.Compiled);

        // Permissive recovery for prompt="..." values that contain UNESCAPED inner
        // double quotes. LLMs frequently fail to escape dialog quotes (e.g. they
        // write prompt="She shouts "hi" loudly" instead of prompt="She shouts \"hi\"
        // loudly"), which under strict JSON-style attribute parsing truncates the
        // prompt at the first inner quote - and that's exactly where the dialog
        // audio cue lives for LTX video, so the resulting clip is silent.
        //
        // This regex finds the closing quote by anchoring it to either ANOTHER
        // attribute (name=) or the tag's closing /> via lookahead. Regex backtracking
        // walks past inner quotes that aren't followed by such an anchor. Works for
        // BOTH escaped and unescaped inputs (escaped values just have no internal
        // quotes that confuse the lookahead). Singleline so . matches newlines.
        private static readonly Regex PermissivePromptRx = new Regex(
            @"\bprompt\s*=\s*""(.+?)""(?=\s*(?:[A-Za-z_][A-Za-z0-9_-]*\s*=|/\s*>|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public void Reset()
        {
            _buffer.Clear();
            _imageBubbleCounter = 0;
            _scannedUpTo = 0;
            _suppressLeadingLineBreakAfterRemovedMediaAction = false;
        }

        /// <summary>
        /// Append a new chunk of streamed text. Triggers any newly-parsed actions
        /// synchronously via <see cref="OnActionParsed"/> before returning. Safe to call
        /// with empty/null text (used as a "flush" signal).
        /// </summary>
        public void Feed(string newChunk)
        {
            if (!string.IsNullOrEmpty(newChunk))
                _buffer.Append(newChunk);

            // Walk the buffer extracting any complete tags. We don't remove them from the
            // buffer here - ConsumeDisplayText() does that after the matching action has
            // already been fired.
            // We still need to fire OnActionParsed exactly once per tag - track which
            // characters we've already inspected via _scannedUpTo.
            ScanForActions();
        }

        // Index in _buffer up to which we've already scanned + fired tags. Tags ending
        // before this index have been emitted; new chunks may add tags after it.
        private int _scannedUpTo = 0;

        private void ScanForActions()
        {
            string text = _buffer.ToString();

            // Combined scan: try paired form first (it's the longer, less-greedy match),
            // then self-closing form for the rest. Order matters because the body of a
            // paired tag could itself contain "/>", which the self-closing regex would
            // mis-match.
            var matches = new List<Match>();
            foreach (Match m in PairedRx.Matches(text, _scannedUpTo))
                matches.Add(m);
            foreach (Match m in SelfClosingRx.Matches(text, _scannedUpTo))
            {
                bool overlaps = false;
                foreach (var pm in matches)
                {
                    if (m.Index >= pm.Index && m.Index < pm.Index + pm.Length) { overlaps = true; break; }
                }
                if (!overlaps) matches.Add(m);
            }
            matches.Sort((a, b) => a.Index.CompareTo(b.Index));

            foreach (var m in matches)
            {
                var action = ParseAttributes(m.Groups[1].Value);
                if (action != null) OnActionParsed?.Invoke(action);
                _scannedUpTo = m.Index + m.Length;
            }
        }

        private static SkillAction ParseAttributes(string attrBlob)
        {
            if (attrBlob == null) return null;

            var action = new SkillAction();

            foreach (Match m in AttrDoubleQuoteRx.Matches(attrBlob))
            {
                string k = m.Groups[1].Value.ToLowerInvariant();
                string v = DecodeBackslashEscapes(DecodeXmlEntities(m.Groups[2].Value));
                action.Args[k] = v;
            }
            foreach (Match m in AttrSingleQuoteRx.Matches(attrBlob))
            {
                string k = m.Groups[1].Value.ToLowerInvariant();
                if (action.Args.ContainsKey(k)) continue; // already captured by double-quote pass
                string v = DecodeBackslashEscapes(DecodeXmlEntities(m.Groups[2].Value));
                action.Args[k] = v;
            }

            // Recovery pass for unescaped inner quotes in prompt= specifically. The
            // strict parsers above happily truncate prompt at the first stray ", so we
            // re-extract with a lookahead-anchored regex and prefer whichever result
            // captured MORE characters. This is a no-op when the LLM escaped properly
            // (both regexes capture the same span); it's a rescue when it didn't.
            var permissive = PermissivePromptRx.Match(attrBlob);
            if (permissive.Success)
            {
                string permissiveValue = DecodeBackslashEscapes(DecodeXmlEntities(permissive.Groups[1].Value));
                if (!action.Args.TryGetValue("prompt", out string strictValue)
                    || string.IsNullOrEmpty(strictValue)
                    || permissiveValue.Length > strictValue.Length)
                {
                    action.Args["prompt"] = permissiveValue;
                }
            }

            action.Args.TryGetValue("skill", out string skillId);
            if (string.IsNullOrEmpty(skillId)) return null;
            action.SkillId = skillId;
            return action;
        }

        private static string DecodeXmlEntities(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&amp;", "&");
        }

        /// <summary>
        /// Decodes JSON-style backslash escape sequences inside an attribute value.
        /// LLMs reflexively emit <c>\"</c> for embedded quotes (and sometimes <c>\\</c>,
        /// <c>\n</c>, etc.) because they've been trained on JSON tool-call payloads,
        /// even when the surrounding syntax is XML. Unknown escape sequences are
        /// preserved verbatim so we never silently corrupt prompt content.
        /// </summary>
        private static string DecodeBackslashEscapes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  i++; continue;
                        case '\'': sb.Append('\''); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case '/':  sb.Append('/');  i++; continue;
                        case 'n':  sb.Append('\n'); i++; continue;
                        case 'r':  sb.Append('\r'); i++; continue;
                        case 't':  sb.Append('\t'); i++; continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the prefix of the current buffer that is "safe to render" in the chat
        /// bubble - i.e. no partial action tag is being held back. Action tags that have
        /// fully arrived are stripped so media actions do not leave protocol text in the
        /// visible chat transcript.
        ///
        /// The returned text is REMOVED from the internal buffer - the caller appends it
        /// to the bubble. The next call returns only newly-added safe text.
        /// </summary>
        public string ConsumeDisplayText()
        {
            string text = _buffer.ToString();

            // Find the start of any in-progress tag (a "<" that might become "<aitools_action")
            // beyond which we should NOT emit. If there's no such marker, all current text
            // is safe.
            int holdFromIndex = FindHoldStart(text);

            // Now substitute all complete tags BEFORE holdFromIndex with their display
            // replacements.
            string emittable;
            if (holdFromIndex >= text.Length)
            {
                emittable = text;
                _buffer.Clear();
                _scannedUpTo = 0;
            }
            else
            {
                emittable = text.Substring(0, holdFromIndex);
                string remainder = text.Substring(holdFromIndex);
                _buffer.Clear();
                _buffer.Append(remainder);
                // Re-scan offset is reset since we just trimmed the buffer (matches in the
                // remainder will be re-detected next Feed()).
                _scannedUpTo = 0;
            }

            emittable = SuppressPendingLeadingLineBreak(emittable);
            return ReplaceTagsWithSentinels(emittable);
        }

        /// <summary>
        /// Final flush: returns whatever's left as display text, including any orphan
        /// "&lt;" we'd been holding back, and clears the buffer. Called when the LLM
        /// signals end of stream.
        /// </summary>
        public string Flush()
        {
            string text = _buffer.ToString();
            _buffer.Clear();
            _scannedUpTo = 0;
            text = SuppressPendingLeadingLineBreak(text);
            return ReplaceTagsWithSentinels(text);
        }

        private string SuppressPendingLeadingLineBreak(string text)
        {
            if (!_suppressLeadingLineBreakAfterRemovedMediaAction || string.IsNullOrEmpty(text))
                return text;

            if (text.StartsWith("\r\n", StringComparison.Ordinal))
            {
                _suppressLeadingLineBreakAfterRemovedMediaAction = false;
                return text.Substring(2);
            }
            if (text[0] == '\n')
            {
                _suppressLeadingLineBreakAfterRemovedMediaAction = false;
                return text.Substring(1);
            }
            if (text[0] == '\r')
            {
                // CRLF may be split across streaming chunks; keep suppression armed if
                // this chunk was only the CR.
                bool keepArmed = text.Length == 1;
                _suppressLeadingLineBreakAfterRemovedMediaAction = keepArmed;
                return text.Substring(1);
            }

            _suppressLeadingLineBreakAfterRemovedMediaAction = false;
            return text;
        }

        /// <summary>
        /// Returns the index at which to STOP emitting display text right now (because
        /// what follows might be an in-progress tag we shouldn't show partially). Returns
        /// text.Length if everything is safe.
        ///
        /// We hold back from any "<" that could be the start of "&lt;aitools_action" but
        /// hasn't been confirmed-or-denied yet. A "<" followed by enough non-matching
        /// characters can be released.
        /// </summary>
        private static int FindHoldStart(string text)
        {
            int n = text.Length;
            for (int i = n - 1; i >= 0; i--)
            {
                if (text[i] != '<') continue;
                // Check what follows. If we have enough chars to definitively rule out
                // "<aitools_action", release this "<". Otherwise hold.
                string suffix = text.Substring(i, n - i);
                if (suffix.Length >= TagOpen.Length)
                {
                    // We have enough characters to know definitively. If it doesn't start
                    // with our tag (case-insensitive), this "<" is plain text - keep
                    // looking earlier "<"s.
                    if (!suffix.StartsWith(TagOpen, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // It IS our tag. Is the closing ">" present?
                    int closeIdx = suffix.IndexOf('>');
                    if (closeIdx < 0)
                        return i; // tag not closed yet - hold from here
                    // Self-closing or open?
                    // If self-closing ("/>"), it's complete - safe to emit (will be
                    // stripped/replaced below).
                    if (closeIdx > 0 && suffix[closeIdx - 1] == '/')
                        continue;
                    // Open tag - need a matching </aitools_action> later. Hold if missing.
                    if (suffix.IndexOf("</aitools_action", StringComparison.OrdinalIgnoreCase) < 0)
                        return i;
                    // Both opener and closer present - safe.
                    continue;
                }
                else
                {
                    // Not enough characters to decide - hold here, but only if the prefix
                    // we DO have is consistent with our tag start. If e.g. "<x" was emitted,
                    // it cannot be our tag - release.
                    string prefixWeHave = suffix;
                    if (TagOpen.StartsWith(prefixWeHave, StringComparison.OrdinalIgnoreCase))
                        return i; // ambiguous - might be our tag, hold
                    // Definitely not our tag - safe to emit, keep walking earlier "<"s.
                    continue;
                }
            }
            return n;
        }

        /// <summary>
        /// Replaces every complete <c>&lt;aitools_action .../&gt;</c> in the input with
        /// a display placeholder for non-visual skills. Visual media actions are removed
        /// from the text because the actual image/movie bubble is shown separately.
        /// </summary>
        private string ReplaceTagsWithSentinels(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Replace paired form first so its body doesn't get mistaken for self-closing.
            text = PairedRx.Replace(text, m =>
            {
                _imageBubbleCounter++;
                return MakeSentinel(m.Groups[1].Value, _imageBubbleCounter);
            });
            text = SelfClosingRx.Replace(text, m =>
            {
                _imageBubbleCounter++;
                return MakeSentinel(m.Groups[1].Value, _imageBubbleCounter);
            });
            return RemoveMediaActionMarkers(text);
        }

        private static string MakeSentinel(string attrBlob, int n)
        {
            string skill = ParseAttributes(attrBlob)?.SkillId ?? "";
            // Media actions already create real bubbles in the Media panel; don't also
            // spam the text transcript with "[image #N]" / "[movie #N]" markers.
            switch ((skill ?? "").ToLowerInvariant())
            {
                case BuiltInSkillIds.GenerateImage:
                case BuiltInSkillIds.ImageToImage:
                    return RemovedMediaActionMarker;
                case BuiltInSkillIds.GenerateMovie:
                case BuiltInSkillIds.ImageToMovie:
                    return RemovedMediaActionMarker;
                default:
                    return $"\n[skill: {skill}]\n";
            }
        }

        private string RemoveMediaActionMarkers(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(RemovedMediaActionMarker, StringComparison.Ordinal) < 0)
                return text;

            var sb = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                int lineStart = index;
                int lineEnd = lineStart;
                while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
                    lineEnd++;

                int nextLineStart = lineEnd;
                if (nextLineStart < text.Length)
                {
                    if (text[nextLineStart] == '\r' && nextLineStart + 1 < text.Length && text[nextLineStart + 1] == '\n')
                        nextLineStart += 2;
                    else
                        nextLineStart += 1;
                }

                string line = text.Substring(lineStart, lineEnd - lineStart);
                if (line.IndexOf(RemovedMediaActionMarker, StringComparison.Ordinal) >= 0)
                {
                    string cleanedLine = line.Replace(RemovedMediaActionMarker, "");
                    if (cleanedLine.Trim().Length == 0)
                    {
                        if (lineEnd >= text.Length)
                            _suppressLeadingLineBreakAfterRemovedMediaAction = true;
                        index = nextLineStart;
                        continue;
                    }
                    line = cleanedLine;
                }

                sb.Append(line);
                if (lineEnd < nextLineStart)
                    sb.Append(text, lineEnd, nextLineStart - lineEnd);

                index = nextLineStart;
            }

            return sb.ToString();
        }
    }
}
