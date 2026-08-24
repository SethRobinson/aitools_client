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
    /// <item>UNESCAPED inner quotes and apostrophes in free-text values
    /// (<c>prompt="he says "hi" and I'm here"</c>) - apostrophes only open a
    /// quote span directly after <c>=</c>, and a permissive whole-tag fallback
    /// rescues tags whose quote mix defeats strict span pairing.</item>
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

        // The attribute span is QUOTE-AWARE: a bare [^>]*? would end the tag at the
        // first '>' inside a quoted value, and H3 reference prompts legitimately
        // contain raw <Video 1> / <Picture 1> tags (the model is TOLD to write them).
        // Quoted spans (with backslash escapes) are consumed atomically so '>' only
        // terminates the tag when it appears outside quotes.
        //
        // Two deliberate wrinkles:
        // - A single-quote span only OPENS directly after '=' (value position). A
        //   bare apostrophe anywhere else is a plain character. Without the guard,
        //   an unescaped inner dialog quote offsets the double-quote span pairing
        //   and a contraction in the prose (prompt="... says "hi" and I'm tall ...")
        //   starts a bogus single-quote span that never closes, silently dropping
        //   the whole tag (and with it the render).
        // - '<' is NOT a plain attr character (only valid inside quoted spans), so
        //   an unclosed tag can never swallow the next "<aitools_action" and merge
        //   two actions into one Franken-match.
        private const string AttrSpan = @"((?:[^<>""']|""(?:[^""\\]|\\.)*""|(?<==\s*)'(?:[^'\\]|\\.)*'|')*?)";

        // Self-closing: <aitools_action ... />
        private static readonly Regex SelfClosingRx = new Regex(
            @"<aitools_action\b" + AttrSpan + @"/\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Paired: <aitools_action ...>BODY</aitools_action>  (BODY is ignored)
        private static readonly Regex PairedRx = new Regex(
            @"<aitools_action\b" + AttrSpan + @">(?:[\s\S]*?)</aitools_action\s*>",
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

        // Permissive recovery for free-text values that contain UNESCAPED inner
        // double quotes. LLMs frequently fail to escape dialog quotes (e.g. they
        // write prompt="She shouts "hi" loudly" instead of prompt="She shouts \"hi\"
        // loudly"), which under strict JSON-style attribute parsing truncates the
        // value at the first inner quote. The same mistake happens in draw_text
        // captions such as text="LOAD "*",8,1".
        //
        // This regex finds the closing quote by anchoring it to either ANOTHER
        // attribute (name=) or the tag's closing /> via lookahead. Regex backtracking
        // walks past inner quotes that aren't followed by such an anchor. Works for
        // BOTH escaped and unescaped inputs (escaped values just have no internal
        // quotes that confuse the lookahead). Singleline so . matches newlines.
        private static readonly Regex PermissivePromptRx = new Regex(
            @"\bprompt\s*=\s*""(.+?)""(?=\s*(?:[A-Za-z_][A-Za-z0-9_-]*\s*=|/\s*>|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex PermissiveTextRx = new Regex(
            @"\btext\s*=\s*""(.+?)""(?=\s*(?:[A-Za-z_][A-Za-z0-9_-]*\s*=|/\s*>|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex PermissiveNegativePromptRx = new Regex(
            @"\bnegative_prompt\s*=\s*""(.+?)""(?=\s*(?:[A-Za-z_][A-Za-z0-9_-]*\s*=|/\s*>|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Last-chance WHOLE-TAG fallback for tags the strict AttrSpan regexes cannot
        // digest - typically free-text values whose unescaped quote mix defeats span
        // pairing entirely (an ODD number of quotes, e.g. a 6'2" measurement or an
        // unclosed dialog quote). The span is TEMPERED so it can never run across a
        // following "<aitools_action" (no cross-tag merges), and the closing "/>" is
        // anchored to a structural boundary so a "/>" inside a still-streaming value
        // can never end the tag early:
        // - Mid-stream variant: the tag must be followed by a newline or the next
        //   action tag (models emit one action per line, per the protocol).
        // - End-of-stream variant: additionally accepts end-of-buffer, used at
        //   Flush() when no more text is coming.
        private const string PermissiveTagSpan = @"<aitools_action\b((?:(?!<aitools_action\b)[\s\S])*?)/\s*>";
        private static readonly Regex PermissiveSelfClosingMidRx = new Regex(
            PermissiveTagSpan + @"(?=\s*(?:\r?\n|<aitools_action\b))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PermissiveSelfClosingEndRx = new Regex(
            PermissiveTagSpan + @"(?=\s*(?:\r?\n|<aitools_action\b|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            ScanForActions(endOfStream: false);
        }

        // Index in _buffer up to which we've already scanned + fired tags. Tags ending
        // before this index have been emitted; new chunks may add tags after it.
        private int _scannedUpTo = 0;

        private void ScanForActions(bool endOfStream)
        {
            string text = _buffer.ToString();

            // Combined scan: try paired form first (it's the longer, less-greedy match),
            // then self-closing form for the rest. Order matters because the body of a
            // paired tag could itself contain "/>", which the self-closing regex would
            // mis-match. Finally the permissive whole-tag fallback picks up tags the
            // strict regexes could not digest (strict matches always win on overlap).
            var matches = new List<Match>();
            foreach (Match m in PairedRx.Matches(text, _scannedUpTo))
                matches.Add(m);
            AddNonOverlappingMatches(matches, SelfClosingRx.Matches(text, _scannedUpTo));
            var permissiveRx = endOfStream ? PermissiveSelfClosingEndRx : PermissiveSelfClosingMidRx;
            AddNonOverlappingMatches(matches, permissiveRx.Matches(text, _scannedUpTo));
            matches.Sort((a, b) => a.Index.CompareTo(b.Index));

            foreach (var m in matches)
            {
                var action = ParseAttributes(m.Groups[1].Value);
                if (action != null) OnActionParsed?.Invoke(action);
                _scannedUpTo = m.Index + m.Length;
            }
        }

        private static void AddNonOverlappingMatches(List<Match> matches, MatchCollection candidates)
        {
            foreach (Match m in candidates)
            {
                bool overlaps = false;
                foreach (var existing in matches)
                {
                    if (m.Index < existing.Index + existing.Length
                        && existing.Index < m.Index + m.Length) { overlaps = true; break; }
                }
                if (!overlaps) matches.Add(m);
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

            // Recovery pass for unescaped inner quotes in free-text attributes. The
            // strict parsers above happily truncate at the first stray ", so we
            // re-extract with a lookahead-anchored regex and prefer whichever result
            // captured MORE characters. This is a no-op when the LLM escaped properly
            // (both regexes capture the same span); it's a rescue when it didn't.
            ApplyPermissiveAttribute(action, attrBlob, "prompt", PermissivePromptRx);
            ApplyPermissiveAttribute(action, attrBlob, "text", PermissiveTextRx);
            ApplyPermissiveAttribute(action, attrBlob, "negative_prompt", PermissiveNegativePromptRx);

            action.Args.TryGetValue("skill", out string skillId);
            if (string.IsNullOrEmpty(skillId)) return null;
            action.SkillId = skillId;
            return action;
        }

        private static void ApplyPermissiveAttribute(SkillAction action, string attrBlob, string key, Regex rx)
        {
            if (action == null || string.IsNullOrEmpty(attrBlob) || string.IsNullOrEmpty(key) || rx == null)
                return;

            var permissive = rx.Match(attrBlob);
            if (!permissive.Success)
                return;

            string permissiveValue = DecodeBackslashEscapes(DecodeXmlEntities(permissive.Groups[1].Value));
            if (!action.Args.TryGetValue(key, out string strictValue)
                || string.IsNullOrEmpty(strictValue)
                || permissiveValue.Length > strictValue.Length)
            {
                action.Args[key] = permissiveValue;
            }
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
                // Translate the fired-tag watermark into the trimmed buffer's
                // coordinates. A tag can be FIRED but still held for display (e.g.
                // a value containing a bare '>' made FindHoldStart conservative);
                // resetting to 0 here would re-detect it on the next Feed() and
                // fire the SAME action twice - a duplicate render.
                _scannedUpTo = Math.Max(0, _scannedUpTo - holdFromIndex);
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
            // Fire any last-chance action first: a permissive-fallback tag sitting at
            // the very end of the stream (no trailing newline) only matches the
            // end-of-stream variant, and the buffer is about to be discarded.
            ScanForActions(endOfStream: true);

            string text = _buffer.ToString();
            _buffer.Clear();
            _scannedUpTo = 0;
            text = SuppressPendingLeadingLineBreak(text);
            text = ReplaceTagsWithSentinels(text);
            // Defense against an LLM that stopped mid-emission of an action tag (model
            // collapse / early-EOS / network drop). After sentinel replacement, any
            // remaining "<aitools_action" is by definition an unclosed tag - if it had
            // closed, SelfClosingRx / PairedRx would have consumed it. Trim it to a
            // one-line marker so the bubble shows a clear failure indicator instead
            // of a 500-word leaked prompt body. The canonical history (built separately
            // from the raw stream) keeps the partial tag so the LLM can see its own
            // mistake on the next turn.
            int orphanIdx = text.IndexOf(TagOpen, StringComparison.OrdinalIgnoreCase);
            if (orphanIdx >= 0)
            {
                string head = text.Substring(0, orphanIdx).TrimEnd();
                string sep = head.Length > 0 ? "\n" : "";
                text = head + sep + "[truncated tool call - LLM stopped mid-emission, try again]";
            }
            return text;
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
            // Track the EARLIEST hold-worthy "<" instead of returning at the first one
            // found scanning backwards. A value that is still streaming can contain an
            // inner "<" (e.g. prompt="...from <Video 1>..." cut mid-token at "<"): that
            // inner "<" is hold-worthy on its own, but holding from THERE would emit -
            // and permanently discard - the partial tag head before it, losing the
            // action. The enclosing tag's own "<" is also hold-worthy (it can't be
            // matched yet), so taking the minimum keeps the whole tag in the buffer.
            int holdIdx = n;
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
                    // It IS our tag. Release it only when a tag-extraction regex can
                    // actually consume it AT this position - "looks closed" (a '/' before
                    // some later '>') is not enough: if no regex matches, the sentinel
                    // replacement can't strip it and raw protocol text would leak into
                    // the visible bubble. Held tags resolve on a later chunk once more
                    // text arrives, or at Flush() (permissive end-of-stream pass /
                    // truncated-tool-call marker). This also releases complete tags whose
                    // values contain a bare '>' (e.g. "<Video 1>"), which the old
                    // first-'>' heuristic misclassified as unclosed paired tags.
                    var sc = SelfClosingRx.Match(text, i);
                    if (sc.Success && sc.Index == i)
                        continue;
                    var pr = PairedRx.Match(text, i);
                    if (pr.Success && pr.Index == i)
                        continue;
                    var pm = PermissiveSelfClosingMidRx.Match(text, i);
                    if (pm.Success && pm.Index == i)
                        continue;
                    holdIdx = i; // incomplete (or not yet matchable) - hold from here
                }
                else
                {
                    // Not enough characters to decide - hold here, but only if the prefix
                    // we DO have is consistent with our tag start. If e.g. "<x" was emitted,
                    // it cannot be our tag - release.
                    string prefixWeHave = suffix;
                    if (TagOpen.StartsWith(prefixWeHave, StringComparison.OrdinalIgnoreCase))
                        holdIdx = i; // ambiguous - might be our tag, hold
                    // Otherwise definitely not our tag - safe to emit, keep walking
                    // earlier "<"s either way.
                }
            }
            return holdIdx;
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
            // Strip permissively-parsed tags too, so a tag only the fallback could
            // extract doesn't leak protocol text into the bubble. The end-of-stream
            // variant is correct here even mid-stream: this method only ever sees
            // text FindHoldStart already released, so '$' can't truncate a tag that
            // is still streaming - it just lets a fired tag sitting at the end of
            // the released span be stripped.
            text = PermissiveSelfClosingEndRx.Replace(text, m =>
            {
                _imageBubbleCounter++;
                return MakeSentinel(m.Groups[1].Value, _imageBubbleCounter);
            });
            return RemoveMediaActionMarkers(text);
        }

        private static string MakeSentinel(string attrBlob, int n)
        {
            string skill = ParseAttributes(attrBlob)?.SkillId ?? "";
            return ShowsTranscriptMarker(skill) ? $"\n[skill: {skill}]\n" : RemovedMediaActionMarker;
        }

        /// <summary>
        /// Parse every complete action tag out of a stored reply (no side effects). Used
        /// by the chat's "[skill: X]" click-to-expand, which shows the attributes that
        /// were sent to the tool. Same regexes and order as the streaming path.
        /// </summary>
        public static List<SkillAction> ExtractActions(string rawText)
        {
            var list = new List<SkillAction>();
            if (string.IsNullOrEmpty(rawText)) return list;
            var p = new SkillActionParser();
            p.OnActionParsed += a => { if (a != null) list.Add(a); };
            p.Feed(rawText);
            p.Flush();
            return list;
        }

        /// <summary>
        /// True when an executed action of this skill leaves a "[skill: X]" marker in the
        /// transcript. Skills with VISUAL side-effects (spawn a Pic bubble or stack onto
        /// one) already have a visible result in the Media panel; don't also spam the
        /// text transcript with markers. Includes media generators AND the composition
        /// primitives, which all either create new bubbles (new_canvas) or modify an
        /// existing/chained Pic (draw_text, add_border, paste_image, draw_shape,
        /// crop_resize). Users want to see the resulting poster, not 4 markers per poster.
        /// </summary>
        public static bool ShowsTranscriptMarker(string skill)
        {
            switch ((skill ?? "").ToLowerInvariant())
            {
                case BuiltInSkillIds.GenerateImage:
                case BuiltInSkillIds.ImageToImage:
                case BuiltInSkillIds.GenerateMovie:
                case BuiltInSkillIds.ImageToMovie:
                case BuiltInSkillIds.VideoToVideo:
                case BuiltInSkillIds.RifeVideo:
                case BuiltInSkillIds.ClipVideo:
                case BuiltInSkillIds.DrawText:
                case BuiltInSkillIds.AddBorder:
                case BuiltInSkillIds.PasteImage:
                case BuiltInSkillIds.NewCanvas:
                case BuiltInSkillIds.CropResize:
                case BuiltInSkillIds.DrawShape:
                case BuiltInSkillIds.InspectImage:
                case BuiltInSkillIds.Continue:
                case BuiltInSkillIds.ExtractStill:
                case BuiltInSkillIds.StitchVideo:
                // Web fetches render their own always-visible Web trace bubble, so a
                // "[skill: web_image]" marker in the transcript would just be noise.
                case BuiltInSkillIds.WebSearch:
                case BuiltInSkillIds.WebImage:
                case BuiltInSkillIds.WebVideo:
                case BuiltInSkillIds.WebPage:
                    return false;
                default:
                    return true;
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
