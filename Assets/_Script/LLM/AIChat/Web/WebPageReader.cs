using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

// NOTE: this file must stay free of UnityEngine dependencies so a plain dotnet console app
// can compile it directly for offline regression tests (same rule as SkillActionParser.cs).

namespace AITools.AIChat.Web
{
    /// <summary>One candidate image found on a web page (not downloaded).</summary>
    public sealed class WebPageImage
    {
        /// <summary>Absolute http(s) URL, fragment stripped.</summary>
        public string Url;
        /// <summary>alt, else title / aria-label, else the enclosing figure caption. May be empty.</summary>
        public string Alt = "";
        /// <summary>0 = unknown. After a Wikimedia thumb rewrite these are the ORIGINAL file dims (data-file-width/height).</summary>
        public int Width;
        public int Height;
        /// <summary>"og" | "twitter" | "link" | "img" | "picture"</summary>
        public string Source = "img";
        /// <summary>Human note for the trace, e.g. "wikimedia thumb -> original (thumb 250x136)".</summary>
        public string Note;
    }

    /// <summary>One bare sound-file link (&lt;a href="....wav"&gt; or &lt;audio src&gt;) found on a page (not downloaded).</summary>
    public sealed class WebPageAudioLink
    {
        /// <summary>Absolute http(s) URL, fragment stripped.</summary>
        public string Url;
        /// <summary>The link's text (collapsed), else the file name. May be empty.</summary>
        public string Label = "";
    }

    /// <summary>Result of <see cref="WebPageReader.Extract"/>.</summary>
    public sealed class WebPageExtraction
    {
        /// <summary>&lt;title&gt;, else og:title, else the first h1.</summary>
        public string Title;
        /// <summary>Readable text, possibly truncated (see <see cref="Truncated"/>).</summary>
        public string Text = "";
        /// <summary>Length of the scoped readable text BEFORE truncation.</summary>
        public int TotalChars;
        public bool Truncated;
        public int TruncatedChars;
        /// <summary>"main" | "article" | "content" | "body" | "text"</summary>
        public string Scope = "body";
        public List<WebPageImage> Images = new List<WebPageImage>();
        /// <summary>Accepted candidates (after junk / size filtering, before the max_images cap).</summary>
        public int ImageCandidatesTotal;
        /// <summary>Raw &lt;img&gt; tags seen anywhere in the document.</summary>
        public int ImageTagsSeen;
        /// <summary>Bare sound-file links found on the page (web_audio result targets), capped and deduped.</summary>
        public List<WebPageAudioLink> AudioLinks = new List<WebPageAudioLink>();
        /// <summary>Deduped audio links seen (before the cap).</summary>
        public int AudioLinkCandidatesTotal;
        public string Charset;
        public string CanonicalUrl;
        public string Lang;
    }

    /// <summary>
    /// Small dependency-free HTML -> readable text extractor + image candidate lister for the
    /// AI Chat web_page skill. Single-pass tag scanner (no DOM, no regex over the document):
    /// drops script/style/nav/footer/header/aside/reference/junk-class elements, prefers the
    /// longest main/article/content region, keeps headings as "## " lines, list items as
    /// "- ", table rows as "a | b" (infobox rows as "label: value"), decodes entities and
    /// collapses whitespace. Tuned against Wikipedia's Vector 2022 / Parsoid markup but
    /// generic enough for news articles and blogs.
    /// </summary>
    public static class WebPageReader
    {
        // ------------------------------------------------------------------ charset decoding

        /// <summary>
        /// Decode page bytes to a string: BOM, then the HTTP charset, then a &lt;meta charset&gt; /
        /// http-equiv content-type in the first 4 KB, else strict UTF-8 with a windows-1252
        /// fallback when the bytes are not valid UTF-8. Every Encoding lookup is guarded (stripped
        /// players may lack code pages).
        /// </summary>
        public static string DecodeHtml(byte[] bytes, string httpCharset, out string charsetUsed)
        {
            charsetUsed = "utf-8";
            if (bytes == null || bytes.Length == 0) return "";
            int n = bytes.Length;
            if (n >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                charsetUsed = "utf-8 (bom)";
                return StripBom(Encoding.UTF8.GetString(bytes, 3, n - 3));
            }
            if (n >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                charsetUsed = "utf-16le (bom)";
                return StripBom(Encoding.Unicode.GetString(bytes, 2, n - 2));
            }
            if (n >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                charsetUsed = "utf-16be (bom)";
                return StripBom(Encoding.BigEndianUnicode.GetString(bytes, 2, n - 2));
            }

            string label = NormalizeCharsetLabel(httpCharset);
            if (label == null) label = NormalizeCharsetLabel(SniffMetaCharset(bytes));
            if (label != null)
            {
                Encoding enc = TryGetEncoding(label);
                if (enc != null)
                {
                    charsetUsed = label;
                    return StripBom(enc.GetString(bytes));
                }
            }

            try
            {
                var strict = new UTF8Encoding(false, true);
                string s = strict.GetString(bytes);
                charsetUsed = "utf-8";
                return StripBom(s);
            }
            catch (DecoderFallbackException)
            {
                Encoding fallback = TryGetEncoding("windows-1252");
                if (fallback == null)
                {
                    charsetUsed = "utf-8 (lossy)";
                    return StripBom(Encoding.UTF8.GetString(bytes));
                }
                charsetUsed = "windows-1252 (invalid utf-8)";
                return StripBom(fallback.GetString(bytes));
            }
        }

        private static string StripBom(string s)
        {
            if (!string.IsNullOrEmpty(s) && s[0] == '\uFEFF') return s.Substring(1);
            return s;
        }

        private static string NormalizeCharsetLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            string l = label.Trim().Trim('"', '\'').Trim().ToLowerInvariant();
            if (l.Length == 0) return null;
            switch (l)
            {
                case "utf8": return "utf-8";
                case "iso-8859-1":
                case "iso8859-1":
                case "iso_8859-1":
                case "latin1":
                case "latin-1":
                case "ascii":
                case "us-ascii":
                case "cp1252":
                    return "windows-1252"; // WHATWG: latin1 labels decode as windows-1252
                case "sjis":
                case "x-sjis":
                case "shift-jis":
                case "ms932":
                    return "shift_jis";
                case "gb2312":
                    return "gbk";
                default:
                    return l;
            }
        }

        private static Encoding TryGetEncoding(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            try { return Encoding.GetEncoding(label); } catch { }
            if (label.StartsWith("utf", StringComparison.Ordinal)) return Encoding.UTF8;
            if (label == "windows-1252" || label.StartsWith("iso-8859", StringComparison.Ordinal))
            {
                try { return Encoding.GetEncoding(28591); } catch { }
            }
            return null;
        }

        /// <summary>Look for charset= inside a &lt;meta ...&gt; tag in the first 4 KB (ASCII scan).</summary>
        private static string SniffMetaCharset(byte[] bytes)
        {
            int n = Math.Min(bytes.Length, 4096);
            string head = Encoding.ASCII.GetString(bytes, 0, n).ToLowerInvariant();
            int from = 0;
            while (true)
            {
                int at = head.IndexOf("charset=", from, StringComparison.Ordinal);
                if (at < 0) return null;
                int metaAt = head.LastIndexOf("<meta", at, StringComparison.Ordinal);
                if (metaAt >= 0 && at - metaAt < 300 && head.IndexOf('>', metaAt) >= at)
                {
                    int i = at + 8;
                    while (i < head.Length && (head[i] == '"' || head[i] == '\'' || head[i] == ' ')) i++;
                    int start = i;
                    while (i < head.Length && head[i] != '"' && head[i] != '\'' && head[i] != ';' && head[i] != '>' && head[i] != '/' && !char.IsWhiteSpace(head[i])) i++;
                    string v = head.Substring(start, i - start).Trim();
                    if (v.Length > 0) return v;
                }
                from = at + 8;
            }
        }

        // ------------------------------------------------------------------ public helpers

        /// <summary>
        /// Cut at the last newline at or before maxChars (never before 60% of maxChars), else the
        /// last space, else hard; appends "[truncated, N more chars]".
        /// </summary>
        public static string TruncateAtBoundary(string text, int maxChars, out int truncatedChars)
        {
            truncatedChars = 0;
            if (text == null) return "";
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            int floor = (int)(maxChars * 0.6);
            int cut = text.LastIndexOf('\n', maxChars);
            if (cut < floor) cut = text.LastIndexOf(' ', maxChars);
            if (cut < floor) cut = maxChars;
            string head = text.Substring(0, cut).TrimEnd();
            truncatedChars = text.Length - head.Length;
            return head + "\n[truncated, " + truncatedChars.ToString(CultureInfo.InvariantCulture) + " more chars]";
        }

        /// <summary>
        /// upload.wikimedia.org/wikipedia/&lt;proj&gt;/thumb/a/ab/Name.jpg/250px-Name.jpg -> .../wikipedia/&lt;proj&gt;/a/ab/Name.jpg.
        /// Vector / document originals (svg, tif, pdf, djvu, webm, ogv, gif, xcf) are NOT rewritten
        /// because the browser-decodable thumb is the useful file.
        /// </summary>
        public static bool TryRewriteWikimediaThumb(string url, out string original)
        {
            original = null;
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string host = u.Host ?? "";
            if (!host.EndsWith("wikimedia.org", StringComparison.OrdinalIgnoreCase) && !host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase)) return false;
            string path = u.AbsolutePath;
            int idx = path.IndexOf("/thumb/", StringComparison.Ordinal);
            if (idx < 0) return false;
            string rest = path.Substring(idx + 7);
            string[] parts = rest.Split('/');
            if (parts.Length < 4) return false;
            string name = parts[2];
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".svg") || lower.EndsWith(".tif") || lower.EndsWith(".tiff") || lower.EndsWith(".pdf") || lower.EndsWith(".djvu")
                || lower.EndsWith(".webm") || lower.EndsWith(".ogv") || lower.EndsWith(".gif") || lower.EndsWith(".xcf"))
                return false;
            original = u.Scheme + "://" + u.Host + path.Substring(0, idx) + "/" + parts[0] + "/" + parts[1] + "/" + name;
            return true;
        }

        /// <summary>
        /// Pick the largest srcset entry. Prefers width descriptors ("640w"); falls back to the
        /// largest density ("2x"). <paramref name="width"/> is 0 for density entries.
        /// </summary>
        public static bool TryParseSrcsetLargest(string srcset, out string url, out int width)
        {
            url = null; width = 0;
            if (string.IsNullOrWhiteSpace(srcset)) return false;
            string s = srcset;
            int i = 0, n = s.Length;
            string bestWUrl = null; int bestW = -1;
            string bestXUrl = null; double bestX = -1;
            while (i < n)
            {
                while (i < n && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i >= n) break;
                int start = i;
                while (i < n && !char.IsWhiteSpace(s[i])) i++;
                string candidate = s.Substring(start, i - start);
                string desc = "";
                if (candidate.EndsWith(",", StringComparison.Ordinal))
                {
                    candidate = candidate.TrimEnd(',');
                }
                else
                {
                    while (i < n && char.IsWhiteSpace(s[i])) i++;
                    int dstart = i;
                    while (i < n && s[i] != ',') i++;
                    desc = s.Substring(dstart, i - dstart).Trim();
                    if (i < n) i++;
                }
                if (candidate.Length == 0) continue;
                if (desc.Length > 1 && (desc[desc.Length - 1] == 'w' || desc[desc.Length - 1] == 'W'))
                {
                    int w;
                    if (int.TryParse(desc.Substring(0, desc.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out w) && w > bestW) { bestW = w; bestWUrl = candidate; }
                }
                else if (desc.Length > 1 && (desc[desc.Length - 1] == 'x' || desc[desc.Length - 1] == 'X'))
                {
                    double x;
                    if (double.TryParse(desc.Substring(0, desc.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && x > bestX) { bestX = x; bestXUrl = candidate; }
                }
                else if (desc.Length == 0 && bestX < 1) { bestX = 1; bestXUrl = candidate; }
            }
            if (bestWUrl != null) { url = bestWUrl; width = bestW; return true; }
            if (bestXUrl != null) { url = bestXUrl; width = 0; return true; }
            return false;
        }

        /// <summary>"P1:3 https://... (alt: "...", 5850x3180, wikimedia thumb -> original)"</summary>
        public static string FormatImageLine(string pageId, int index, WebPageImage img)
        {
            var sb = new StringBuilder();
            sb.Append(pageId).Append(':').Append(index.ToString(CultureInfo.InvariantCulture)).Append(' ');
            string url = img.Url ?? "";
            if (url.Length > 300) url = url.Substring(0, 297) + "...";
            sb.Append(url);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(img.Alt))
            {
                string alt = img.Alt.Replace('"', '\'');
                if (alt.Length > 90) alt = alt.Substring(0, 87) + "...";
                parts.Add("alt: \"" + alt + "\"");
            }
            if (img.Width > 0 && img.Height > 0) parts.Add(img.Width.ToString(CultureInfo.InvariantCulture) + "x" + img.Height.ToString(CultureInfo.InvariantCulture));
            else if (img.Width > 0) parts.Add(img.Width.ToString(CultureInfo.InvariantCulture) + "px wide");
            if (!string.IsNullOrEmpty(img.Note)) parts.Add(img.Note);
            if (parts.Count > 0) sb.Append(" (").Append(string.Join(", ", parts.ToArray())).Append(')');
            return sb.ToString();
        }

        /// <summary>One "P1:a2 https://... ("link text")" line for the trace and the model recap.</summary>
        public static string FormatAudioLine(string pageId, int index, WebPageAudioLink link)
        {
            var sb = new StringBuilder();
            sb.Append(pageId).Append(":a").Append(index.ToString(CultureInfo.InvariantCulture)).Append(' ');
            string url = link.Url ?? "";
            if (url.Length > 300) url = url.Substring(0, 297) + "...";
            sb.Append(url);
            if (!string.IsNullOrEmpty(link.Label))
            {
                string label = link.Label.Replace('"', '\'');
                if (label.Length > 90) label = label.Substring(0, 87) + "...";
                sb.Append(" (\"").Append(label).Append("\")");
            }
            return sb.ToString();
        }

        /// <summary>Neutralise literal action tags inside fetched text so the transcript can never re-parse them.</summary>
        public static string StripActionTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            text = ReplaceIgnoreCase(text, "</aitools_action", "[/aitools_action");
            text = ReplaceIgnoreCase(text, "<aitools_action", "[aitools_action");
            return text;
        }

        private static string ReplaceIgnoreCase(string text, string needle, string replacement)
        {
            int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return text;
            var sb = new StringBuilder(text.Length);
            int from = 0;
            while (at >= 0)
            {
                sb.Append(text, from, at - from).Append(replacement);
                from = at + needle.Length;
                at = text.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(text, from, text.Length - from);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ extraction entry

        public static WebPageExtraction Extract(string html, Uri pageUri, int maxChars, int maxImages, bool wantImages)
        {
            var result = new WebPageExtraction();
            if (html == null) html = "";
            var st = new ScanState(html, pageUri, wantImages, maxImages);
            int pos = 0, n = html.Length;
            var attrs = new List<Attr>(8);
            while (pos < n)
            {
                int lt = html.IndexOf('<', pos);
                if (lt < 0) { st.EmitText(pos, n - pos); break; }
                if (lt > pos) st.EmitText(pos, lt - pos);

                int end; string name; TagKind kind; bool selfClosing;
                attrs.Clear();
                kind = TryParseTag(html, lt, out end, out name, out selfClosing, attrs);
                if (kind == TagKind.Unterminated) break;
                if (kind == TagKind.Literal) { st.EmitLiteralLessThan(); pos = lt + 1; continue; }
                if (kind == TagKind.Skipped) { pos = end; continue; }
                pos = end;
                if (kind == TagKind.Close) { st.CloseElement(name); continue; }

                if (name == "title") { pos = st.CaptureTitle(pos); continue; }
                // <audio src=...> is raw-skipped below (its <source> children with it), but the
                // open tag's own src is a bare sound file worth listing for web_audio.
                if (name == "audio") st.CollectAudioSrc(attrs);
                if (RawTextElements.Contains(name)) { pos = st.SkipRawText(pos, name); continue; }
                st.OpenElement(name, attrs, selfClosing);
            }
            st.CloseAll();
            st.Finish(result, maxChars);
            return result;
        }

        // ------------------------------------------------------------------ tag parsing

        private enum TagKind { Literal, Skipped, Open, Close, Unterminated }

        private struct Attr
        {
            public string Name;   // lowercase
            public string Value;  // raw (entities NOT decoded); null for boolean attributes
        }

        private static readonly HashSet<string> RawTextElements = new HashSet<string>
        {
            "script", "style", "textarea", "template", "svg", "iframe", "math", "object", "canvas", "audio", "video", "xmp", "plaintext"
        };

        private static readonly HashSet<string> VoidElements = new HashSet<string>
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr", "keygen"
        };

        private static TagKind TryParseTag(string html, int lt, out int end, out string name, out bool selfClosing, List<Attr> attrs)
        {
            end = lt + 1; name = null; selfClosing = false;
            int n = html.Length;
            if (lt + 1 >= n) return TagKind.Literal;
            char c = html[lt + 1];
            if (c == '!')
            {
                if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
                {
                    int close = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                    end = close < 0 ? n : close + 3;
                    return TagKind.Skipped;
                }
                if (string.CompareOrdinal(html, lt, "<![CDATA[", 0, 9) == 0)
                {
                    int close = html.IndexOf("]]>", lt + 9, StringComparison.Ordinal);
                    end = close < 0 ? n : close + 3;
                    return TagKind.Skipped;
                }
                int gt = html.IndexOf('>', lt + 2);
                end = gt < 0 ? n : gt + 1;
                return TagKind.Skipped;
            }
            if (c == '?')
            {
                int gt = html.IndexOf('>', lt + 2);
                end = gt < 0 ? n : gt + 1;
                return TagKind.Skipped;
            }
            bool isClose = false;
            int i = lt + 1;
            if (c == '/') { isClose = true; i++; }
            if (i >= n || !IsNameStart(html[i])) return TagKind.Literal;
            int nameStart = i;
            while (i < n && IsNameChar(html[i])) i++;
            name = LowerName(html, nameStart, i - nameStart);

            // attributes
            while (true)
            {
                while (i < n && (char.IsWhiteSpace(html[i]) || html[i] == '/'))
                {
                    if (html[i] == '/' && i + 1 < n && html[i + 1] == '>') selfClosing = true;
                    i++;
                }
                if (i >= n) { end = n; return TagKind.Unterminated; }
                if (html[i] == '>') { end = i + 1; break; }
                if (html[i] == '<') { end = i; break; } // broken tag: let the next '<' start fresh
                int aStart = i;
                while (i < n && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '>' && html[i] != '/' && html[i] != '<') i++;
                string aName = LowerName(html, aStart, i - aStart);
                string aValue = null;
                int j = i;
                while (j < n && char.IsWhiteSpace(html[j])) j++;
                if (j < n && html[j] == '=')
                {
                    j++;
                    while (j < n && char.IsWhiteSpace(html[j])) j++;
                    if (j < n && (html[j] == '"' || html[j] == '\''))
                    {
                        char q = html[j];
                        int vStart = j + 1;
                        int vEnd = html.IndexOf(q, vStart);
                        if (vEnd < 0) { end = n; return TagKind.Unterminated; }
                        aValue = html.Substring(vStart, vEnd - vStart);
                        i = vEnd + 1;
                    }
                    else
                    {
                        int vStart = j;
                        while (j < n && !char.IsWhiteSpace(html[j]) && html[j] != '>') j++;
                        aValue = html.Substring(vStart, j - vStart);
                        i = j;
                    }
                }
                if (aName.Length > 0 && !isClose)
                {
                    bool dup = false;
                    for (int k = 0; k < attrs.Count; k++) if (attrs[k].Name == aName) { dup = true; break; }
                    if (!dup) attrs.Add(new Attr { Name = aName, Value = aValue });
                }
            }
            return isClose ? TagKind.Close : TagKind.Open;
        }

        private static bool IsNameStart(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
        private static bool IsNameChar(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == ':' || c == '_' || c == '.'; }

        private static string LowerName(string html, int start, int len)
        {
            if (len <= 0) return "";
            bool lower = true;
            for (int i = start; i < start + len; i++) { char c = html[i]; if (c >= 'A' && c <= 'Z') { lower = false; break; } }
            string s = html.Substring(start, len);
            return lower ? s : s.ToLowerInvariant();
        }

        private static string GetAttr(List<Attr> attrs, string name)
        {
            for (int i = 0; i < attrs.Count; i++)
            {
                if (attrs[i].Name == name)
                {
                    string v = attrs[i].Value;
                    if (v == null) return "";
                    return v.IndexOf('&') >= 0 ? WebUtility.HtmlDecode(v) : v;
                }
            }
            return null;
        }

        private static bool HasAttr(List<Attr> attrs, string name)
        {
            for (int i = 0; i < attrs.Count; i++) if (attrs[i].Name == name) return true;
            return false;
        }

        // ------------------------------------------------------------------ vocabularies

        private static readonly HashSet<string> BlockTags = new HashSet<string>
        {
            "address", "article", "aside", "blockquote", "body", "caption", "center", "dd", "details", "dialog", "div", "dl", "dt",
            "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "html", "legend",
            "li", "main", "menu", "nav", "ol", "option", "p", "pre", "section", "summary", "table", "tbody", "td", "tfoot", "th",
            "thead", "tr", "ul"
        };

        /// <summary>Blocks that get an empty line before them (and after, on close).</summary>
        private static readonly HashSet<string> ParagraphTags = new HashSet<string>
        {
            "p", "blockquote", "table", "figure", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article"
        };

        /// <summary>Skipped entirely by tag name (header only outside a main region).</summary>
        private static readonly HashSet<string> SkipTags = new HashSet<string>
        {
            "nav", "footer", "aside", "button", "select", "datalist", "dialog", "menu", "map", "noindex"
        };

        private static readonly HashSet<string> SkipRoles = new HashSet<string>
        {
            "navigation", "banner", "contentinfo", "complementary", "search", "menu", "menubar", "dialog", "alertdialog", "tooltip"
        };

        /// <summary>Exact lowercase class / id tokens that mark navigation, chrome, ads and reference clutter.</summary>
        private static readonly HashSet<string> JunkTokens = new HashSet<string>
        {
            "nav", "navbar", "navbox", "navigation", "menu", "menubar", "sidebar", "toc", "breadcrumb", "breadcrumbs",
            "cookie", "cookies", "comments", "comment", "share", "sharing", "social", "socials", "related", "recommended",
            "advert", "advertisement", "ads", "ad", "promo", "newsletter", "subscribe", "popup", "modal", "skip-link",
            "screen-reader-text", "sr-only", "visually-hidden", "hidden",
            // MediaWiki / Wikipedia (Vector 2022 + Parsoid)
            "hatnote", "mw-jump-link", "mw-editsection", "mw-indicators", "mw-indicator", "mw-empty-elt", "reflist",
            "references", "mw-references-wrap", "reference", "mw-ref", "mw-cite-backlink", "catlinks", "printfooter",
            "noprint", "sistersitebox", "side-box", "portal-bar", "authority-control", "shortdescription", "metadata",
            "ambox", "mbox-small", "navbox-styles", "navbox-inner", "navbox-subgroup", "mw-footer", "mw-portlet",
            "vector-menu", "vector-dropdown", "sitesub", "contentsub", "contentsub2", "sitenotice", "mw-hidden-catlinks",
            "toctitle", "mw-jump", "mw-sticky-header"
        };

        private static readonly string[] JunkPrefixes =
        {
            "vector-", "mw-jump", "mw-editsection", "mw-indicator", "mw-references", "mw-portlet", "navbox", "navbar",
            "cookie", "breadcrumb", "share-", "social-", "comment-", "sidebar", "footer-", "nav-", "menu-", "toc-",
            "related-", "recommend", "popup", "modal", "advert", "ad-", "ads-", "sponsor", "promo-", "newsletter", "subscribe"
        };

        private static readonly string[] JunkIdPrefixes =
        {
            "p-", "mw-navigation", "mw-panel", "vector-", "footer", "catlinks", "jump-to", "mw-head", "mw-page-base", "mw-head-base"
        };

        /// <summary>Tokens that must never be treated as junk even though a prefix rule matches (Wikipedia article wrappers).</summary>
        private static readonly HashSet<string> AllowTokens = new HashSet<string>
        {
            "vector-body", "vector-page-titlebar", "mw-body-header", "mw-body-content", "mw-body", "vector-body-before-content",
            "mw-content-container", "mw-page-container", "mw-page-container-inner", "mw-content-ltr", "mw-content-rtl"
        };

        /// <summary>Class / id tokens that mark the readable content region.</summary>
        private static readonly HashSet<string> MainTokens = new HashSet<string>
        {
            "mw-content-text", "mw-parser-output", "bodycontent", "main-content", "maincontent", "post-content", "entry-content",
            "article-body", "articlebody", "article-content", "story-body", "post-body", "content-body", "article__body", "article-text"
        };

        private static readonly HashSet<string> FigureTokens = new HashSet<string> { "thumb", "tmulti", "tsingle", "wp-caption", "figure", "thumbinner" };
        private static readonly HashSet<string> CaptionTokens = new HashSet<string> { "thumbcaption", "wp-caption-text", "figcaption", "caption" };

        private static readonly string[] IconWords =
        {
            "icon", "avatar", "sprite", "tracking", "badge", "button", "emoji", "spinner", "loading", "loader", "blank",
            "1x1", "spacer", "arrow", "bullet", "oojs", "favicon", "wordmark", "tagline", "gravatar", "captcha", "/static/",
            "centralautologin", "placeholder", "transparent", "smiley"
        };

        // ------------------------------------------------------------------ scanner state

        private sealed class Frame
        {
            public string Name;
            public bool Junk;
            public bool NoScript;
            public bool Main;
            public string MainKind;
            public bool Pre;
            public bool Infobox;
            public bool Figure;
            public bool Caption;
            public bool Cell;      // td / th: block for implicit-close purposes, but no newline of its own
            public bool Picture;
            public bool List;
            public bool Ordered;
            public int ListCounter;
            public int CellIndex;
            public bool LastCellWasTh;
            public bool Heading;
            public bool H1;
            public int H1Start = -1;
            /// <summary>Set on an &lt;a&gt; whose href is a bare sound file; its text span becomes the label.</summary>
            public string AudioHref;
            public int AudioTextStart = -1;
            public bool Block;
            public bool Paragraph;
            public int FirstImageIndex = -1;
            public StringBuilder CaptionText;
            public string PictureSourceUrl;
            public int PictureSourceWidth;
        }

        private struct Region { public int Start, End; public string Kind; }

        private sealed class ScanState
        {
            private readonly string _html;
            private readonly bool _wantImages;
            private readonly int _maxImages;
            private readonly StringBuilder _buf = new StringBuilder(64 * 1024);
            private readonly List<Frame> _stack = new List<Frame>(64);
            private readonly List<Region> _regions = new List<Region>();
            private readonly List<WebPageImage> _images = new List<WebPageImage>();
            private readonly HashSet<string> _seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<WebPageAudioLink> _audioLinks = new List<WebPageAudioLink>();
            private readonly HashSet<string> _seenAudioUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private int _audioCandidatesTotal;
            // Not WebRequestLimits.MaxPageAudioLinks: this file compiles alone in the offline harness.
            private const int MaxAudioLinks = 30;
            private int _junkDepth, _noscriptDepth, _mainDepth, _preDepth, _listDepth, _captionDepth;
            private int _regionStart = -1; private string _regionKind;
            private Frame _captionFigure;
            private Uri _baseUri; private bool _baseSet;
            private string _title, _ogTitle, _firstH1, _canonical, _lang;
            private int _imageTagsSeen, _candidatesTotal;
            private WebPageImage _lastMetaImage;

            public ScanState(string html, Uri pageUri, bool wantImages, int maxImages)
            {
                _html = html;
                _baseUri = pageUri;
                _wantImages = wantImages && maxImages > 0;
                _maxImages = maxImages;
            }

            private bool CanEmit { get { return _junkDepth == 0 && _noscriptDepth == 0; } }

            // ---- text

            public void EmitText(int start, int len)
            {
                if (len <= 0) return;
                if (!CanEmit) return;
                string raw = _html.Substring(start, len);
                string text = raw.IndexOf('&') >= 0 ? WebUtility.HtmlDecode(raw) : raw;
                if (_preDepth > 0)
                {
                    AppendVerbatim(text);
                    return;
                }
                AppendInline(text);
            }

            public void EmitLiteralLessThan()
            {
                if (!CanEmit) return;
                AppendInline("<");
            }

            private void AppendVerbatim(string text)
            {
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');
                _buf.Append(text);
                if (_captionDepth > 0 && _captionFigure != null) _captionFigure.CaptionText.Append(text);
            }

            private void AppendInline(string text)
            {
                bool needSpace = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c) || c == '\u00A0' || c == '\u200B' || c == '\uFEFF')
                    {
                        needSpace = true;
                        continue;
                    }
                    if (c < 0x20) continue;
                    if (needSpace)
                    {
                        AppendSpaceIfNeeded();
                        needSpace = false;
                    }
                    _buf.Append(c);
                    if (_captionDepth > 0 && _captionFigure != null) _captionFigure.CaptionText.Append(c);
                }
                if (needSpace)
                {
                    AppendSpaceIfNeeded();
                }
            }

            private void AppendSpaceIfNeeded()
            {
                if (_buf.Length == 0) return;
                char last = _buf[_buf.Length - 1];
                if (last == '\n' || last == ' ' || last == '\t') return;
                _buf.Append(' ');
                if (_captionDepth > 0 && _captionFigure != null) _captionFigure.CaptionText.Append(' ');
            }

            private void TrimTrailingSpaces()
            {
                int len = _buf.Length;
                while (len > 0 && (_buf[len - 1] == ' ' || _buf[len - 1] == '\t')) len--;
                if (len != _buf.Length) _buf.Length = len;
            }

            private void EnsureNewline()
            {
                if (_buf.Length == 0) return;
                TrimTrailingSpaces();
                if (_buf.Length > 0 && _buf[_buf.Length - 1] != '\n') _buf.Append('\n');
            }

            private void EnsureBlankLine()
            {
                if (_buf.Length == 0) return;
                EnsureNewline();
                if (_buf.Length >= 2 && _buf[_buf.Length - 2] != '\n') _buf.Append('\n');
            }

            private void AppendRaw(string s)
            {
                _buf.Append(s);
            }

            // ---- raw text elements

            public int CaptureTitle(int pos)
            {
                int close = _html.IndexOf("</title", pos, StringComparison.OrdinalIgnoreCase);
                if (close < 0) return pos;
                string inner = _html.Substring(pos, close - pos);
                if (_title == null)
                {
                    string t = CollapseWhitespace(WebUtility.HtmlDecode(inner));
                    if (t.Length > 0) _title = t;
                }
                int gt = _html.IndexOf('>', close);
                return gt < 0 ? _html.Length : gt + 1;
            }

            public int SkipRawText(int pos, string name)
            {
                int close = _html.IndexOf("</" + name, pos, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    // Unterminated script/style swallows the rest; other raw elements just continue normally.
                    if (name == "script" || name == "style") return _html.Length;
                    return pos;
                }
                int gt = _html.IndexOf('>', close);
                return gt < 0 ? _html.Length : gt + 1;
            }

            // ---- elements

            public void OpenElement(string name, List<Attr> attrs, bool selfClosing)
            {
                if (name == "html") { string l = GetAttr(attrs, "lang"); if (!string.IsNullOrEmpty(l)) _lang = l; return; }

                // Void / self-closing elements never push a frame.
                if (VoidElements.Contains(name))
                {
                    HandleVoid(name, attrs);
                    return;
                }

                ApplyImplicitCloses(name);

                var f = new Frame { Name = name };
                string id = GetAttr(attrs, "id");
                string cls = GetAttr(attrs, "class");
                string role = GetAttr(attrs, "role");
                string idLower = id == null ? null : id.ToLowerInvariant();
                string clsLower = cls == null ? null : cls.ToLowerInvariant();

                bool mainHint = name == "main" || name == "article" || (role != null && role.Equals("main", StringComparison.OrdinalIgnoreCase))
                    || (idLower != null && (MainTokens.Contains(idLower) || idLower == "content"))
                    || HasAnyToken(clsLower, MainTokens)
                    || string.Equals(GetAttr(attrs, "itemprop"), "articleBody", StringComparison.OrdinalIgnoreCase);

                bool junk = false;
                if (!mainHint)
                {
                    if (SkipTags.Contains(name)) junk = true;
                    else if (name == "header" && _mainDepth == 0) junk = true;
                    else if (role != null && SkipRoles.Contains(role.ToLowerInvariant())) junk = true;
                    else if (name == "sup" && (HasToken(clsLower, "reference") || HasToken(clsLower, "mw-ref"))) junk = true;
                    else if (IsJunkByClassOrId(idLower, clsLower)) junk = true;
                }
                if (!junk)
                {
                    if (HasAttr(attrs, "hidden")) junk = true;
                    else
                    {
                        string ah = GetAttr(attrs, "aria-hidden");
                        if (ah != null && ah.Equals("true", StringComparison.OrdinalIgnoreCase)) junk = true;
                        else
                        {
                            string style = GetAttr(attrs, "style");
                            if (style != null && StyleHidesElement(style)) junk = true;
                        }
                    }
                }

                if (mainHint && !junk && _junkDepth > 0)
                {
                    // A content region opening inside an unclosed junk element: the junk element was
                    // almost certainly never closed properly (or is a layout wrapper), so close it.
                    while (_junkDepth > 0 && _stack.Count > 0) PopFrame();
                }

                f.Junk = junk;
                f.NoScript = name == "noscript";
                f.Main = mainHint && !junk;
                if (f.Main) f.MainKind = name == "main" ? "main" : name == "article" ? "article" : "content";
                f.Pre = name == "pre";
                f.Block = BlockTags.Contains(name);
                f.Cell = name == "td" || name == "th";
                f.Paragraph = ParagraphTags.Contains(name);
                f.Heading = name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6';
                f.H1 = name == "h1";
                f.Infobox = name == "table" && HasToken(clsLower, "infobox");
                f.Figure = name == "figure" || HasAnyToken(clsLower, FigureTokens) || (GetAttr(attrs, "typeof") ?? "").StartsWith("mw:File", StringComparison.OrdinalIgnoreCase);
                f.Caption = name == "figcaption" || HasAnyToken(clsLower, CaptionTokens);
                f.Picture = name == "picture";
                f.List = name == "ul" || name == "ol" || name == "menu";
                f.Ordered = name == "ol";

                if (name == "a" && !junk && CanEmit)
                {
                    string audioUrl = ResolveAudioFileUrl(GetAttr(attrs, "href"));
                    if (audioUrl != null) { f.AudioHref = audioUrl; f.AudioTextStart = _buf.Length; }
                }

                _stack.Add(f);
                if (f.Junk) _junkDepth++;
                if (f.NoScript) _noscriptDepth++;
                if (f.Pre) _preDepth++;
                if (f.List) _listDepth++;
                if (f.Figure) f.CaptionText = new StringBuilder();
                if (f.Main)
                {
                    if (_mainDepth == 0) { _regionStart = _buf.Length; _regionKind = f.MainKind; }
                    _mainDepth++;
                }
                if (f.Caption)
                {
                    _captionDepth++;
                    if (_captionFigure == null) _captionFigure = FindFigureFrame();
                }

                if (CanEmit) EmitOpenPrefix(f, name);

                if (selfClosing && !f.Block)
                {
                    // e.g. "<span/>": treat as immediately closed.
                    CloseElement(name);
                }
            }

            private void EmitOpenPrefix(Frame f, string name)
            {
                if (f.Paragraph) EnsureBlankLine();
                else if (f.Block && !f.Cell) EnsureNewline();

                if (f.Heading)
                {
                    int level = name[1] - '0';
                    AppendRaw(level == 1 ? "# " : level == 2 ? "## " : level == 3 ? "### " : "#### ");
                    if (f.H1 && _firstH1 == null) f.H1Start = _buf.Length;
                }
                else if (name == "li")
                {
                    Frame list = FindNearest("ul", "ol", "menu");
                    int depth = Math.Max(0, _listDepth - 1);
                    for (int i = 0; i < depth; i++) AppendRaw("  ");
                    if (list != null && list.Ordered)
                    {
                        list.ListCounter++;
                        AppendRaw(list.ListCounter.ToString(CultureInfo.InvariantCulture) + ". ");
                    }
                    else AppendRaw("- ");
                }
                else if (name == "dt") { AppendRaw(""); }
                else if (name == "dd") { AppendRaw("  "); }
                else if (name == "tr")
                {
                    f.CellIndex = 0;
                }
                else if (name == "td" || name == "th")
                {
                    Frame tr = FindNearest("tr");
                    if (tr != null)
                    {
                        if (tr.CellIndex > 0 && _buf.Length > 0 && _buf[_buf.Length - 1] != '\n')
                        {
                            Frame table = FindNearest("table");
                            bool infobox = table != null && table.Infobox;
                            AppendRaw(infobox && tr.LastCellWasTh && name == "td" ? ": " : " | ");
                        }
                        tr.CellIndex++;
                        tr.LastCellWasTh = name == "th";
                    }
                }
                else if (f.Caption)
                {
                    EnsureNewline();
                    AppendRaw("[caption] ");
                }
            }

            private void HandleVoid(string name, List<Attr> attrs)
            {
                switch (name)
                {
                    case "br":
                        if (CanEmit) { TrimTrailingSpaces(); if (_buf.Length > 0 && !(_buf.Length >= 2 && _buf[_buf.Length - 1] == '\n' && _buf[_buf.Length - 2] == '\n')) _buf.Append('\n'); }
                        break;
                    case "hr":
                        if (CanEmit) { EnsureNewline(); AppendRaw("---\n"); }
                        break;
                    case "base":
                        if (!_baseSet)
                        {
                            string href = GetAttr(attrs, "href");
                            Uri b;
                            if (!string.IsNullOrEmpty(href) && Uri.TryCreate(_baseUri, href, out b)) { _baseUri = b; _baseSet = true; }
                        }
                        break;
                    case "meta":
                        HandleMeta(attrs);
                        break;
                    case "link":
                        {
                            string rel = (GetAttr(attrs, "rel") ?? "").ToLowerInvariant();
                            string href = GetAttr(attrs, "href");
                            if (string.IsNullOrEmpty(href)) break;
                            if (HasToken(rel, "canonical")) { string abs = Resolve(href); if (abs != null) _canonical = abs; }
                            else if (HasToken(rel, "image_src")) AddMetaImage(href, "link");
                        }
                        break;
                    case "img":
                        CollectImg(attrs);
                        break;
                    case "source":
                        HandlePictureSource(attrs);
                        break;
                }
            }

            private void HandleMeta(List<Attr> attrs)
            {
                string key = GetAttr(attrs, "property");
                if (string.IsNullOrEmpty(key)) key = GetAttr(attrs, "name");
                if (string.IsNullOrEmpty(key)) return;
                key = key.Trim().ToLowerInvariant();
                string content = GetAttr(attrs, "content");
                if (content == null) return;
                content = content.Trim();
                switch (key)
                {
                    case "og:image":
                    case "og:image:url":
                    case "og:image:secure_url":
                        AddMetaImage(content, "og");
                        break;
                    case "twitter:image":
                    case "twitter:image:src":
                        AddMetaImage(content, "twitter");
                        break;
                    case "og:image:width":
                        if (_lastMetaImage != null && _lastMetaImage.Note == null) { int w; if (int.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) _lastMetaImage.Width = w; }
                        break;
                    case "og:image:height":
                        if (_lastMetaImage != null && _lastMetaImage.Note == null) { int h; if (int.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) _lastMetaImage.Height = h; }
                        break;
                    case "og:image:alt":
                        if (_lastMetaImage != null && string.IsNullOrEmpty(_lastMetaImage.Alt)) _lastMetaImage.Alt = CleanAlt(content);
                        break;
                    case "og:title":
                        if (_ogTitle == null && content.Length > 0) _ogTitle = CollapseWhitespace(content);
                        break;
                }
            }

            private void HandlePictureSource(List<Attr> attrs)
            {
                Frame pic = FindNearest("picture");
                if (pic == null || !_wantImages) return;
                string type = (GetAttr(attrs, "type") ?? "").ToLowerInvariant();
                if (type.IndexOf("svg", StringComparison.Ordinal) >= 0) return;
                string srcset = GetAttr(attrs, "srcset");
                if (string.IsNullOrEmpty(srcset)) srcset = GetAttr(attrs, "data-srcset");
                string url; int w;
                if (!TryParseSrcsetLargest(srcset, out url, out w)) return;
                if (LooksLikeVectorOrIcon(url)) return;
                if (pic.PictureSourceUrl == null || w > pic.PictureSourceWidth) { pic.PictureSourceUrl = url; pic.PictureSourceWidth = w; }
            }

            // ---- images

            private void AddMetaImage(string value, string source)
            {
                if (!_wantImages || string.IsNullOrEmpty(value)) return;
                var img = BuildCandidate(value, "", null, null, 0, 0, 0, 0, source);
                if (img != null) _lastMetaImage = img;
            }

            private void CollectImg(List<Attr> attrs)
            {
                _imageTagsSeen++;
                if (!_wantImages || _junkDepth > 0) return;

                string srcset = GetAttr(attrs, "srcset");
                if (string.IsNullOrEmpty(srcset)) srcset = GetAttr(attrs, "data-srcset");
                string chosen = null; int srcsetWidth = 0; string source = "img"; string pickNote = null;
                string su; int sw;
                if (TryParseSrcsetLargest(srcset, out su, out sw)) { chosen = su; srcsetWidth = sw; pickNote = sw > 0 ? "largest srcset " + sw + "w" : "largest srcset"; }

                Frame pic = FindNearest("picture");
                if (pic != null && pic.PictureSourceUrl != null && pic.PictureSourceWidth > srcsetWidth)
                {
                    chosen = pic.PictureSourceUrl; srcsetWidth = pic.PictureSourceWidth; source = "picture"; pickNote = "picture source";
                }
                if (chosen == null)
                {
                    chosen = GetAttr(attrs, "data-src");
                    if (string.IsNullOrEmpty(chosen)) chosen = GetAttr(attrs, "data-lazy-src");
                    if (string.IsNullOrEmpty(chosen)) chosen = GetAttr(attrs, "data-original");
                    if (string.IsNullOrEmpty(chosen)) chosen = GetAttr(attrs, "src");
                    pickNote = null;
                }
                if (string.IsNullOrEmpty(chosen)) return;

                string alt = GetAttr(attrs, "alt");
                if (string.IsNullOrEmpty(alt)) alt = GetAttr(attrs, "title");
                if (string.IsNullOrEmpty(alt)) alt = GetAttr(attrs, "aria-label");
                alt = CleanAlt(alt);

                int w = ParseSize(GetAttr(attrs, "width"));
                int h = ParseSize(GetAttr(attrs, "height"));
                int fw = ParseSize(GetAttr(attrs, "data-file-width"));
                int fh = ParseSize(GetAttr(attrs, "data-file-height"));
                if (srcsetWidth > 0 && srcsetWidth > w) { if (w > 0 && h > 0) h = (int)((long)h * srcsetWidth / w); w = srcsetWidth; }

                var img = BuildCandidate(chosen, alt, GetAttr(attrs, "class"), GetAttr(attrs, "id"), w, h, fw, fh, source);
                if (img != null)
                {
                    if (pickNote != null) img.Note = img.Note == null ? pickNote : pickNote + ", " + img.Note;
                    Frame fig = FindFigureFrame();
                    if (fig != null && fig.FirstImageIndex < 0) fig.FirstImageIndex = _images.IndexOf(img);
                }
            }

            /// <summary>Filter + resolve + dedupe one candidate; returns the stored image (null if rejected or over the cap).</summary>
            private WebPageImage BuildCandidate(string rawUrl, string alt, string cls, string id, int w, int h, int fileW, int fileH, string source)
            {
                string trimmed = (rawUrl ?? "").Trim();
                if (trimmed.Length == 0) return null;
                if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
                string abs = Resolve(trimmed);
                if (abs == null) return null;

                Uri u;
                if (!Uri.TryCreate(abs, UriKind.Absolute, out u)) return null;
                if (u.Scheme != "http" && u.Scheme != "https") return null;
                bool wikimedia = u.Host.EndsWith("wikimedia.org", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase);
                string url = wikimedia ? u.GetLeftPart(UriPartial.Path) : u.GetLeftPart(UriPartial.Query);

                string pathLower = u.AbsolutePath.ToLowerInvariant();
                if (pathLower.EndsWith(".svg") || pathLower.EndsWith(".ico")) return null;

                if (w > 0 && h > 0 && (w < 100 || h < 100)) return null;
                if (fileW > 0 && fileH > 0 && (fileW < 100 || fileH < 100)) return null;

                string altLower = (alt ?? "").ToLowerInvariant();
                string clsLower = (cls ?? "").ToLowerInvariant();
                string idLower = (id ?? "").ToLowerInvariant();
                string urlLower = url.ToLowerInvariant();
                for (int i = 0; i < IconWords.Length; i++)
                {
                    string word = IconWords[i];
                    if (altLower.IndexOf(word, StringComparison.Ordinal) >= 0 || clsLower.IndexOf(word, StringComparison.Ordinal) >= 0
                        || idLower.IndexOf(word, StringComparison.Ordinal) >= 0 || urlLower.IndexOf(word, StringComparison.Ordinal) >= 0)
                        return null;
                }
                if (altLower.IndexOf("logo", StringComparison.Ordinal) >= 0 || clsLower.IndexOf("logo", StringComparison.Ordinal) >= 0 || idLower.IndexOf("logo", StringComparison.Ordinal) >= 0)
                    return null;
                if (urlLower.IndexOf("logo", StringComparison.Ordinal) >= 0 && (w == 0 || w < 150)) return null;

                string note = null;
                string original;
                if (TryRewriteWikimediaThumb(url, out original))
                {
                    note = "wikimedia thumb -> original" + (w > 0 && h > 0 ? " (thumb " + w + "x" + h + ")" : "");
                    url = original;
                    if (fileW > 0 && fileH > 0) { w = fileW; h = fileH; } else { w = 0; h = 0; }
                }
                else if (wikimedia && pathLower.IndexOf("/thumb/", StringComparison.Ordinal) >= 0)
                {
                    note = "vector/document original, largest thumb kept";
                }

                if (!_seenUrls.Add(url)) return null;
                _candidatesTotal++;
                if (_images.Count >= _maxImages) return null;
                var img = new WebPageImage { Url = url, Alt = alt ?? "", Width = w, Height = h, Source = source, Note = note };
                _images.Add(img);
                return img;
            }

            private static bool LooksLikeVectorOrIcon(string url)
            {
                if (string.IsNullOrEmpty(url)) return true;
                string l = url.ToLowerInvariant();
                int q = l.IndexOf('?'); if (q >= 0) l = l.Substring(0, q);
                return l.EndsWith(".svg") || l.EndsWith(".ico") || l.StartsWith("data:");
            }

            private string Resolve(string href)
            {
                if (string.IsNullOrEmpty(href)) return null;
                try
                {
                    Uri abs;
                    if (_baseUri != null && Uri.TryCreate(_baseUri, href, out abs)) return abs.AbsoluteUri;
                    if (Uri.TryCreate(href, UriKind.Absolute, out abs)) return abs.AbsoluteUri;
                }
                catch { }
                return null;
            }

            // ---- audio links (web_audio result targets)

            private static readonly string[] AudioFileExtensions =
                { ".wav", ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".aiff", ".aif" };

            /// <summary>The href resolved to an absolute http(s) URL whose path names a sound file, else null.</summary>
            private string ResolveAudioFileUrl(string href)
            {
                if (string.IsNullOrEmpty(href)) return null;
                string decoded = WebUtility.HtmlDecode(href.Trim());
                if (decoded.Length == 0 || decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
                string abs = Resolve(decoded);
                if (abs == null) return null;
                Uri u;
                if (!Uri.TryCreate(abs, UriKind.Absolute, out u)) return null;
                if (u.Scheme != "http" && u.Scheme != "https") return null;
                string path = u.AbsolutePath.ToLowerInvariant();
                for (int i = 0; i < AudioFileExtensions.Length; i++)
                    if (path.EndsWith(AudioFileExtensions[i], StringComparison.Ordinal))
                        return u.GetLeftPart(UriPartial.Query);
                return null;
            }

            /// <summary>src of an &lt;audio&gt; open tag (its &lt;source&gt; children are raw-skipped with the element).</summary>
            public void CollectAudioSrc(List<Attr> attrs)
            {
                if (!CanEmit) return;
                string url = ResolveAudioFileUrl(GetAttr(attrs, "src"));
                if (url != null) AddAudioLink(url, "");
            }

            private void AddAudioLink(string url, string label)
            {
                if (!_seenAudioUrls.Add(url)) return;
                _audioCandidatesTotal++;
                if (_audioLinks.Count >= MaxAudioLinks) return;
                label = CollapseWhitespace(label ?? "").Trim();
                if (label.Length > 80) label = label.Substring(0, 77) + "...";
                if (label.Length == 0)
                {
                    try { label = System.IO.Path.GetFileName(new Uri(url).AbsolutePath) ?? ""; } catch { label = ""; }
                }
                _audioLinks.Add(new WebPageAudioLink { Url = url, Label = label });
            }

            // ---- closing

            public void CloseElement(string name)
            {
                if (name == "br") { HandleVoid("br", new List<Attr>()); return; }
                if (name == "p" && FindIndexStoppingAtBlock("p") < 0)
                {
                    // Stray </p> (browsers create an empty paragraph): treat as a paragraph break.
                    if (CanEmit) EnsureBlankLine();
                    return;
                }
                int idx = -1;
                for (int i = _stack.Count - 1; i >= 0; i--) if (_stack[i].Name == name) { idx = i; break; }
                if (idx < 0) return;
                while (_stack.Count > idx) PopFrame();
            }

            public void CloseAll()
            {
                while (_stack.Count > 0) PopFrame();
            }

            private void PopFrame()
            {
                Frame f = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);

                bool couldEmit = CanEmit;
                if (f.Junk) _junkDepth--;
                if (f.NoScript) _noscriptDepth--;
                if (f.Pre) _preDepth--;
                if (f.List) _listDepth--;
                if (f.Caption)
                {
                    _captionDepth--;
                    if (_captionDepth == 0) _captionFigure = null;
                }
                if (f.Figure)
                {
                    if (f.FirstImageIndex >= 0 && f.FirstImageIndex < _images.Count && f.CaptionText != null)
                    {
                        var img = _images[f.FirstImageIndex];
                        if (string.IsNullOrEmpty(img.Alt)) img.Alt = CleanAlt(f.CaptionText.ToString());
                    }
                }
                if (f.Main)
                {
                    _mainDepth--;
                    if (_mainDepth == 0 && _regionStart >= 0)
                    {
                        _regions.Add(new Region { Start = _regionStart, End = _buf.Length, Kind = _regionKind });
                        _regionStart = -1;
                    }
                }
                if (couldEmit)
                {
                    if (f.H1 && f.H1Start >= 0 && _firstH1 == null)
                    {
                        string h = _buf.ToString(f.H1Start, Math.Max(0, _buf.Length - f.H1Start)).Trim();
                        if (h.Length > 0) _firstH1 = h;
                    }
                    if (f.AudioHref != null)
                    {
                        string label = f.AudioTextStart >= 0 && _buf.Length > f.AudioTextStart
                            ? _buf.ToString(f.AudioTextStart, _buf.Length - f.AudioTextStart)
                            : "";
                        AddAudioLink(f.AudioHref, label);
                    }
                    if (f.Paragraph || f.Heading) EnsureBlankLine();
                    else if (f.Block && !f.Cell) EnsureNewline();
                }
            }

            private void ApplyImplicitCloses(string name)
            {
                if (_stack.Count == 0) return;
                if (name == "li") CloseUpTo("li", "ul", "ol", "menu");
                else if (name == "dt" || name == "dd") CloseUpTo2("dt", "dd", "dl");
                else if (name == "td" || name == "th") CloseUpTo2("td", "th", "tr");
                else if (name == "tr") { CloseUpTo2("td", "th", "tr"); CloseUpTo("tr", "table", "tbody", "thead", "tfoot"); }
                else if (name == "tbody" || name == "thead" || name == "tfoot") { CloseUpTo2("td", "th", "tr"); CloseUpTo("tr", "table"); }
                else if (name == "option") CloseUpTo("option", "select", "datalist");
                else if (BlockTags.Contains(name) && name != "p")
                {
                    int idx = FindIndexStoppingAtBlock("p");
                    if (idx >= 0) while (_stack.Count > idx) PopFrame();
                }
            }

            /// <summary>Close an open &lt;target&gt; that sits above the nearest &lt;stopAt&gt; container.</summary>
            private void CloseUpTo(string target, params string[] stopAt)
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    string n = _stack[i].Name;
                    if (n == target) { while (_stack.Count > i) PopFrame(); return; }
                    for (int s = 0; s < stopAt.Length; s++) if (n == stopAt[s]) return;
                }
            }

            private void CloseUpTo2(string targetA, string targetB, string stopAt)
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    string n = _stack[i].Name;
                    if (n == targetA || n == targetB) { while (_stack.Count > i) PopFrame(); return; }
                    if (n == stopAt) return;
                }
            }

            /// <summary>Index of the nearest open &lt;name&gt; unless a different block element is closer (then -1).</summary>
            private int FindIndexStoppingAtBlock(string name)
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    if (_stack[i].Name == name) return i;
                    if (_stack[i].Block) return -1;
                }
                return -1;
            }

            private Frame FindNearest(params string[] names)
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    string n = _stack[i].Name;
                    for (int k = 0; k < names.Length; k++) if (n == names[k]) return _stack[i];
                }
                return null;
            }

            private Frame FindFigureFrame()
            {
                for (int i = _stack.Count - 1; i >= 0; i--) if (_stack[i].Figure) return _stack[i];
                return null;
            }

            // ---- finish

            public void Finish(WebPageExtraction r, int maxChars)
            {
                string text;
                string scope = "body";
                int bufLen = _buf.Length;
                Region best = new Region { Start = 0, End = 0, Kind = null };
                for (int i = 0; i < _regions.Count; i++)
                    if (_regions[i].End - _regions[i].Start > best.End - best.Start) best = _regions[i];
                int bestLen = best.End - best.Start;
                if (best.Kind != null && bestLen >= 200 && bestLen >= bufLen * 0.25)
                {
                    text = _buf.ToString(best.Start, bestLen);
                    scope = best.Kind;
                }
                else text = _buf.ToString();

                text = PostProcess(text);
                text = StripActionTags(text);

                r.Scope = scope;
                r.TotalChars = text.Length;
                int cut;
                r.Text = TruncateAtBoundary(text, maxChars, out cut);
                r.Truncated = cut > 0;
                r.TruncatedChars = cut;
                r.Title = CleanTitle(_title ?? _ogTitle ?? _firstH1);
                r.Images = _images;
                r.ImageCandidatesTotal = _candidatesTotal;
                r.ImageTagsSeen = _imageTagsSeen;
                r.AudioLinks = _audioLinks;
                r.AudioLinkCandidatesTotal = _audioCandidatesTotal;
                r.CanonicalUrl = _canonical;
                r.Lang = _lang;
            }

            private static string CleanTitle(string t)
            {
                if (string.IsNullOrEmpty(t)) return null;
                t = CollapseWhitespace(t);
                if (t.Length > 300) t = t.Substring(0, 297) + "...";
                return t.Length == 0 ? null : t;
            }

            /// <summary>Trim line ends, drop empty headings (a heading followed only by another heading or EOF), cap blank runs, strip control chars.</summary>
            private static string PostProcess(string text)
            {
                if (string.IsNullOrEmpty(text)) return "";
                string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var kept = new List<string>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = StripControl(lines[i]).TrimEnd();
                    if (line.Length > 0 && line[0] == '#' && IsHeadingLine(line))
                    {
                        // Look ahead: next non-blank line another heading (or nothing) -> this heading has no body.
                        int j = i + 1;
                        while (j < lines.Length && lines[j].Trim().Length == 0) j++;
                        if (j >= lines.Length || IsHeadingLine(lines[j].TrimEnd())) continue;
                    }
                    kept.Add(line);
                }
                var sb = new StringBuilder(text.Length);
                int blank = 0;
                for (int i = 0; i < kept.Count; i++)
                {
                    string line = kept[i];
                    if (line.Length == 0)
                    {
                        blank++;
                        if (blank > 1 || sb.Length == 0) continue;
                    }
                    else blank = 0;
                    sb.Append(line).Append('\n');
                }
                return sb.ToString().Trim();
            }

            private static bool IsHeadingLine(string line)
            {
                if (line.Length < 2 || line[0] != '#') return false;
                int i = 0;
                while (i < line.Length && line[i] == '#') i++;
                return i < line.Length && line[i] == ' ';
            }

            private static string StripControl(string s)
            {
                bool clean = true;
                for (int i = 0; i < s.Length; i++) { char c = s[i]; if (c < 0x20 && c != '\t') { clean = false; break; } }
                if (clean) return s;
                var sb = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++) { char c = s[i]; if (c >= 0x20 || c == '\t') sb.Append(c); }
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ small shared helpers

        private static bool HasToken(string classLower, string token)
        {
            if (string.IsNullOrEmpty(classLower)) return false;
            int from = 0;
            while (from < classLower.Length)
            {
                int at = classLower.IndexOf(token, from, StringComparison.Ordinal);
                if (at < 0) return false;
                bool startOk = at == 0 || char.IsWhiteSpace(classLower[at - 1]);
                int endAt = at + token.Length;
                bool endOk = endAt == classLower.Length || char.IsWhiteSpace(classLower[endAt]);
                if (startOk && endOk) return true;
                from = at + 1;
            }
            return false;
        }

        private static bool HasAnyToken(string classLower, HashSet<string> tokens)
        {
            if (string.IsNullOrEmpty(classLower)) return false;
            foreach (string tok in SplitTokens(classLower)) if (tokens.Contains(tok)) return true;
            return false;
        }

        private static IEnumerable<string> SplitTokens(string classLower)
        {
            int i = 0, n = classLower.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(classLower[i])) i++;
                int start = i;
                while (i < n && !char.IsWhiteSpace(classLower[i])) i++;
                if (i > start) yield return classLower.Substring(start, i - start);
            }
        }

        private static bool IsJunkByClassOrId(string idLower, string clsLower)
        {
            if (!string.IsNullOrEmpty(clsLower))
            {
                foreach (string tok in SplitTokens(clsLower))
                {
                    if (AllowTokens.Contains(tok) || MainTokens.Contains(tok)) return false;
                }
                foreach (string tok in SplitTokens(clsLower))
                {
                    if (JunkTokens.Contains(tok)) return true;
                    for (int i = 0; i < JunkPrefixes.Length; i++) if (tok.StartsWith(JunkPrefixes[i], StringComparison.Ordinal)) return true;
                }
            }
            if (!string.IsNullOrEmpty(idLower))
            {
                if (AllowTokens.Contains(idLower) || MainTokens.Contains(idLower)) return false;
                if (JunkTokens.Contains(idLower)) return true;
                for (int i = 0; i < JunkIdPrefixes.Length; i++) if (idLower.StartsWith(JunkIdPrefixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool StyleHidesElement(string style)
        {
            string s = style.ToLowerInvariant().Replace(" ", "");
            return s.IndexOf("display:none", StringComparison.Ordinal) >= 0 || s.IndexOf("visibility:hidden", StringComparison.Ordinal) >= 0;
        }

        private static int ParseSize(string v)
        {
            if (string.IsNullOrEmpty(v)) return 0;
            v = v.Trim();
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
            if (v.EndsWith("%", StringComparison.Ordinal)) return 0;
            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && d < 100000) return (int)d;
            return 0;
        }

        private static string CleanAlt(string alt)
        {
            if (string.IsNullOrEmpty(alt)) return "";
            string a = CollapseWhitespace(alt);
            if (a.Length > 200) a = a.Substring(0, 197) + "...";
            return a;
        }

        private static string CollapseWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '\u00A0') { space = true; continue; }
                if (c < 0x20) continue;
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
