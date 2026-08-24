using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// Minimal Brave Search API client (image / video / web search) for AI Chat's web_*
    /// skills. Coroutine based, SimpleJSON parsing, every field optional. The API key comes
    /// from Config (config.txt set_brave_search_api_key) and is never echoed into results,
    /// chat bubbles, or logs.
    /// </summary>
    public static class BraveSearchClient
    {
        public const string BaseUrl = "https://api.search.brave.com/res/v1";
        private const int TimeoutSeconds = 20;
        // Brave plans are rate limited per second; the model can emit several fetches in one
        // reply, so space requests out a little instead of risking a self-inflicted 429.
        private const float MinSecondsBetweenRequests = 0.6f;
        private static float _lastRequestTime = -100f;

        public sealed class ImageResult
        {
            public string Title;
            public string PageUrl;      // the page the image was found on
            public string ImageUrl;     // properties.url: original full-size image
            public string ThumbnailUrl; // thumbnail.src: ~500px Brave proxy copy
            public string Host;
            public int Width;
            public int Height;
            public string DimsText => Width > 0 && Height > 0 ? Width + "x" + Height : "?x?";
        }

        public sealed class VideoResult
        {
            public string Title;
            public string PageUrl;
            public string ThumbnailUrl;
            public string Host;
            public string Description;
            public string Creator;
            public string Publisher;
            public string DurationText;
            public double DurationSeconds; // 0 when unknown
        }

        public sealed class WebResult
        {
            public string Title;
            public string Url;
            public string Host;
            public string Description;
        }

        public sealed class SearchResponse
        {
            public bool Success;
            public int HttpStatus;
            public string Error;
            public string BodyExcerpt;
            /// <summary>Path + query only, for display. Never includes the key.</summary>
            public string RequestUrlForDisplay;
            public float ElapsedSeconds;
            public long Bytes;
            public string AlteredQuery;
            public readonly List<ImageResult> Images = new List<ImageResult>();
            public readonly List<VideoResult> Videos = new List<VideoResult>();
            public readonly List<WebResult> Web = new List<WebResult>();

            public int ResultCount(WebSearchKind kind)
            {
                switch (kind)
                {
                    case WebSearchKind.Images: return Images.Count;
                    case WebSearchKind.Videos: return Videos.Count;
                    default: return Web.Count;
                }
            }
        }

        public static string GetApiKey()
        {
            var cfg = Config.Get();
            return cfg != null ? (cfg.GetBraveSearchAPIKey() ?? "").Trim() : "";
        }

        public static bool HasApiKey() => !string.IsNullOrEmpty(GetApiKey());

        public static string EndpointPath(WebSearchKind kind)
        {
            switch (kind)
            {
                case WebSearchKind.Images: return "/images/search";
                case WebSearchKind.Videos: return "/videos/search";
                default: return "/web/search";
            }
        }

        public static string KindLabel(WebSearchKind kind)
        {
            switch (kind)
            {
                case WebSearchKind.Images: return "images";
                case WebSearchKind.Videos: return "videos";
                default: return "web";
            }
        }

        /// <summary>
        /// Run one search. <paramref name="safeSearch"/> is "strict" or "off" (null = config default).
        /// </summary>
        public static IEnumerator Search(WebSearchKind kind, string query, int count, string safeSearch, Action<SearchResponse> onDone)
        {
            var resp = new SearchResponse();
            string key = GetApiKey();
            if (string.IsNullOrEmpty(key))
            {
                resp.Success = false;
                resp.Error = "No Brave Search API key is set (Settings > Web, or set_brave_search_api_key in config.txt).";
                onDone?.Invoke(resp);
                yield break;
            }

            query = (query ?? "").Trim();
            if (query.Length == 0)
            {
                resp.Success = false;
                resp.Error = "Empty query.";
                onDone?.Invoke(resp);
                yield break;
            }

            if (count < 1) count = 1;
            if (count > WebRequestLimits.MaxSearchCount) count = WebRequestLimits.MaxSearchCount;
            if (string.IsNullOrEmpty(safeSearch))
            {
                var cfg = Config.Get();
                safeSearch = cfg != null ? cfg.GetWebSearchSafeSearch() : "strict";
            }
            safeSearch = Config.NormalizeSafeSearch(safeSearch);

            string pathAndQuery = EndpointPath(kind)
                + "?q=" + UnityWebRequest.EscapeURL(query).Replace("+", "%20")
                + "&count=" + count.ToString(CultureInfo.InvariantCulture)
                + "&safesearch=" + safeSearch
                + "&spellcheck=1";
            resp.RequestUrlForDisplay = pathAndQuery;

            while (Time.unscaledTime - _lastRequestTime < MinSecondsBetweenRequests)
                yield return null;
            _lastRequestTime = Time.unscaledTime;

            float started = Time.realtimeSinceStartup;
            using (var req = UnityWebRequest.Get(BaseUrl + pathAndQuery))
            {
                req.timeout = TimeoutSeconds;
                req.SetRequestHeader("Accept", "application/json");
                req.SetRequestHeader("X-Subscription-Token", key);
                yield return req.SendWebRequest();

                resp.ElapsedSeconds = Time.realtimeSinceStartup - started;
                resp.HttpStatus = (int)req.responseCode;
                string body = req.downloadHandler != null ? req.downloadHandler.text : null;
                resp.Bytes = req.downloadHandler != null && req.downloadHandler.data != null ? req.downloadHandler.data.Length : 0;

                if (req.result != UnityWebRequest.Result.Success || resp.HttpStatus < 200 || resp.HttpStatus >= 300)
                {
                    resp.Success = false;
                    resp.BodyExcerpt = Excerpt(body, 400);
                    string detail = ExtractBraveErrorDetail(body);
                    // req.error already reads "HTTP/1.1 422 Unprocessable Entity" for HTTP failures;
                    // only prepend our own status text for transport-level errors.
                    string head = !string.IsNullOrEmpty(req.error) && req.error.StartsWith("HTTP/", StringComparison.Ordinal)
                        ? req.error.Substring(req.error.IndexOf(' ') + 1).Trim()
                        : resp.HttpStatus + (string.IsNullOrEmpty(req.error) ? "" : " " + req.error);
                    resp.Error = "HTTP " + head + (string.IsNullOrEmpty(detail) ? "" : ": " + detail);
                    onDone?.Invoke(resp);
                    yield break;
                }

                try
                {
                    Parse(kind, body, resp);
                    resp.Success = true;
                }
                catch (Exception ex)
                {
                    resp.Success = false;
                    resp.BodyExcerpt = Excerpt(body, 400);
                    resp.Error = "Could not parse Brave response: " + ex.Message;
                }
            }

            onDone?.Invoke(resp);
        }

        private static void Parse(WebSearchKind kind, string body, SearchResponse resp)
        {
            var root = JSON.Parse(body ?? "");
            if (root == null) throw new Exception("empty body");

            var q = root["query"];
            if (q != null && q.IsObject)
            {
                string altered = Str(q["altered"]);
                string original = Str(q["original"]);
                if (!string.IsNullOrEmpty(altered) && !string.Equals(altered, original, StringComparison.Ordinal))
                    resp.AlteredQuery = altered;
            }

            JSONNode results = null;
            if (kind == WebSearchKind.Web)
            {
                var web = root["web"];
                if (web != null && web.IsObject) results = web["results"];
            }
            else
            {
                results = root["results"];
            }
            if (results == null || !results.IsArray) return;

            foreach (JSONNode n in results.AsArray)
            {
                if (n == null || !n.IsObject) continue;
                switch (kind)
                {
                    case WebSearchKind.Images:
                    {
                        JSONNode props = n["properties"];
                        JSONNode thumb = n["thumbnail"];
                        var r = new ImageResult
                        {
                            Title = Str(n["title"]),
                            PageUrl = Str(n["url"]),
                            ImageUrl = Str(Child(props, "url")),
                            ThumbnailUrl = Str(Child(thumb, "src")),
                            Host = HostOf(n, n["url"])
                        };
                        r.Width = Int(Child(props, "width"));
                        r.Height = Int(Child(props, "height"));
                        if (r.Width <= 0) r.Width = Int(n["width"]);
                        if (r.Height <= 0) r.Height = Int(n["height"]);
                        if (r.Width <= 0) r.Width = Int(Child(thumb, "width"));
                        if (r.Height <= 0) r.Height = Int(Child(thumb, "height"));
                        if (string.IsNullOrEmpty(r.ImageUrl) && string.IsNullOrEmpty(r.ThumbnailUrl)) continue;
                        resp.Images.Add(r);
                        break;
                    }
                    case WebSearchKind.Videos:
                    {
                        JSONNode v = n["video"];
                        JSONNode thumb = n["thumbnail"];
                        var r = new VideoResult
                        {
                            Title = Str(n["title"]),
                            PageUrl = Str(n["url"]),
                            Description = Str(n["description"]),
                            ThumbnailUrl = Str(Child(thumb, "src")),
                            Host = HostOf(n, n["url"]),
                            Creator = Str(Child(v, "creator")),
                            Publisher = Str(Child(v, "publisher")),
                            DurationText = Str(Child(v, "duration"))
                        };
                        r.DurationSeconds = ParseDuration(r.DurationText);
                        if (string.IsNullOrEmpty(r.PageUrl)) continue;
                        resp.Videos.Add(r);
                        break;
                    }
                    default:
                    {
                        var r = new WebResult
                        {
                            Title = Str(n["title"]),
                            Url = Str(n["url"]),
                            Description = Str(n["description"]),
                            Host = HostOf(n, n["url"])
                        };
                        if (string.IsNullOrEmpty(r.Url)) continue;
                        resp.Web.Add(r);
                        break;
                    }
                }
            }
        }

        private static JSONNode Child(JSONNode parent, string key)
        {
            if (parent == null || !parent.IsObject) return null;
            return parent[key];
        }

        private static string Str(JSONNode n)
        {
            if (n == null || n.IsNull || n.IsObject || n.IsArray) return "";
            return (n.Value ?? "").Trim();
        }

        private static int Int(JSONNode n)
        {
            if (n == null || n.IsNull || n.IsObject || n.IsArray) return 0;
            if (n.IsNumber) return (int)n.AsDouble;
            int v;
            return int.TryParse(n.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string HostOf(JSONNode n, JSONNode urlNode)
        {
            string host = Str(Child(n["meta_url"], "hostname"));
            if (!string.IsNullOrEmpty(host)) return host;
            string url = Str(urlNode);
            Uri u;
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out u)) return u.Host;
            return "";
        }

        /// <summary>"1:02:03", "12:34", "45", "PT1M2S" -> seconds (0 if unparseable).</summary>
        public static double ParseDuration(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
            {
                double total = 0, cur = 0;
                for (int i = 2; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsDigit(c)) { cur = cur * 10 + (c - '0'); continue; }
                    if (c == 'H' || c == 'h') total += cur * 3600;
                    else if (c == 'M' || c == 'm') total += cur * 60;
                    else if (c == 'S' || c == 's') total += cur;
                    cur = 0;
                }
                return total;
            }
            string[] parts = text.Split(':');
            double seconds = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
                seconds = seconds * 60 + v;
            }
            return seconds;
        }

        private static string ExtractBraveErrorDetail(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                var root = JSON.Parse(body);
                if (root == null) return "";
                var err = root["error"];
                if (err != null && err.IsObject)
                {
                    string detail = Str(err["detail"]);
                    if (string.IsNullOrEmpty(detail)) detail = Str(err["message"]);
                    string code = Str(err["code"]);
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(detail)) return code + " - " + detail;
                    return !string.IsNullOrEmpty(detail) ? detail : code;
                }
                return Str(root["message"]);
            }
            catch
            {
                return "";
            }
        }

        public static string Excerpt(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        /// <summary>Build the numbered result lines shown in the Web bubble and sent to the model.</summary>
        public static List<string> FormatResultLines(WebSearchKind kind, SearchResponse resp, int maxUrlChars = 300)
        {
            var lines = new List<string>();
            if (resp == null) return lines;
            switch (kind)
            {
                case WebSearchKind.Images:
                    for (int i = 0; i < resp.Images.Count; i++)
                    {
                        var r = resp.Images[i];
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,2}. {1} | {2} | {3} | {4}",
                            i + 1, Clip(r.Title, 80), r.DimsText, r.Host, Clip(string.IsNullOrEmpty(r.ImageUrl) ? r.ThumbnailUrl : r.ImageUrl, maxUrlChars)));
                    }
                    break;
                case WebSearchKind.Videos:
                    for (int i = 0; i < resp.Videos.Count; i++)
                    {
                        var r = resp.Videos[i];
                        string by = string.IsNullOrEmpty(r.Creator) ? "" : " | by " + Clip(r.Creator, 40);
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,2}. {1} | {2} | {3} | {4}{5}",
                            i + 1, Clip(r.Title, 80), string.IsNullOrEmpty(r.DurationText) ? "?:??" : r.DurationText, r.Host, Clip(r.PageUrl, maxUrlChars), by));
                    }
                    break;
                default:
                    for (int i = 0; i < resp.Web.Count; i++)
                    {
                        var r = resp.Web[i];
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,2}. {1} | {2} | {3}",
                            i + 1, Clip(r.Title, 80), r.Host, Clip(r.Url, maxUrlChars)));
                        if (!string.IsNullOrEmpty(r.Description))
                            lines.Add("    " + Clip(StripTags(r.Description), 220));
                    }
                    break;
            }
            return lines;
        }

        public static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
