using System;

namespace AITools.AIChat.Web
{
    /// <summary>Which Brave Search endpoint a search hits.</summary>
    public enum WebSearchKind
    {
        Images,
        Videos,
        Web
    }

    /// <summary>Parsed arguments of a web_search action (list only, no download).</summary>
    public sealed class WebSearchRequest
    {
        public WebSearchKind Kind = WebSearchKind.Images;
        public string Query = "";
        public int Count = 10;
        /// <summary>"strict" or "off"; null = use the config default.</summary>
        public string SafeSearch;
    }

    /// <summary>Parsed arguments of a web_image action. Exactly one of Query / Url / ResultToken is set.</summary>
    public sealed class WebImageRequest
    {
        public string Query;
        public string Url;
        /// <summary>"S1:3" = result 3 of search session S1.</summary>
        public string ResultToken;
        public int Count = 1;
        public int MinWidth = 256;
        public string SafeSearch;
        public string Anchor;
        /// <summary>Vision-check each download and skip unsuitable ones (default on; needs a vision LLM).</summary>
        public bool Verify = true;
        /// <summary>Extra requirements the model can pass to the vision check, e.g. "full body, standing".</summary>
        public string Criteria;
    }

    /// <summary>Parsed arguments of a web_video action. Exactly one of Query / Url / ResultToken is set.</summary>
    public sealed class WebVideoRequest
    {
        public string Query;
        public string Url;
        public string ResultToken;
        public float StartSeconds = 0f;
        public float DurationSeconds = 5f;
        public float MaxSourceMinutes = 20f;
        public bool IncludeAudio = true;
        public string SafeSearch;
        public string Anchor;
        /// <summary>Vision-check each cut clip (contact sheet) and skip unsuitable ones (default on).</summary>
        public bool Verify = true;
        /// <summary>Extra requirements for the vision check, e.g. "Kramer entering through a door".</summary>
        public string Criteria;
        /// <summary>
        /// The clip must contain the subject SPEAKING (it will be a voice reference for a render
        /// with dialogue). Checked with ffmpeg + Whisper; music-only / silent cuts are rejected.
        /// </summary>
        public bool RequireSpeech;
    }

    /// <summary>
    /// Parsed arguments of a web_page action (fetch ONE page, extract readable text, list its
    /// candidate images). Exactly one of Url / ResultToken / Query is set.
    /// </summary>
    public sealed class WebPageRequest
    {
        public string Url;
        /// <summary>"S1:3" = result 3 of a web_search kind="web" session.</summary>
        public string ResultToken;
        /// <summary>Brave web search; the top-ranked reference-quality hit is read.</summary>
        public string Query;
        /// <summary>How much readable text reaches the model (paragraph-boundary truncation).</summary>
        public int MaxChars = WebRequestLimits.DefaultPageChars;
        /// <summary>Also list the page's candidate images as P&lt;n&gt;:&lt;i&gt; entries.</summary>
        public bool Images = true;
        public int MaxImages = WebRequestLimits.DefaultPageImages;
        /// <summary>Auto-continue once the text is in the prompt (default on, like web_video).</summary>
        public bool Resume = true;
        public string SafeSearch;
    }

    public static class WebRequestLimits
    {
        public const int MaxSearchCount = 20;
        // web_page: one HTML/text page per action.
        public const long MaxPageBytes = 5L * 1024 * 1024;
        public const float PageTimeoutSeconds = 30f;
        public const int DefaultPageChars = 6000;
        public const int MinPageChars = 500;
        public const int MaxPageChars = 20000;
        public const int DefaultPageImages = 12;
        public const int MaxPageImages = 40;
        /// <summary>query= mode: ranked search hits tried until one fetch yields readable text (search fallback, never link following).</summary>
        public const int MaxPageSearchAttempts = 3;
        public const int MaxImageSuccesses = 4;
        public const int MaxImageCandidates = 12;
        public const int MaxVideoAttempts = 4;
        /// <summary>Extra cut offsets (seconds after the requested start) tried in the SAME searched source when a cut is judged unsuitable.</summary>
        public static readonly float[] VideoRetryOffsets = { 30f, 90f };
        public const float MinClipSeconds = 0.5f;
        public const float MaxClipSeconds = 15f;
        public const long MaxImageBytes = 25L * 1024 * 1024;
        public const long MaxVideoBytes = 250L * 1024 * 1024;
        public const float DownloadTimeoutSeconds = 30f;
        public const int MaxImageSide = 2048;

        public static string ParseSafeSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim().ToLowerInvariant();
            if (value == "off" || value == "false" || value == "no" || value == "0") return "off";
            return "strict";
        }
    }
}
