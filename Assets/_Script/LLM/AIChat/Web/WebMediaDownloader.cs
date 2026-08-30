using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.Networking;
using AITools.AIChat.Video;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// Plain HTTPS media downloader for AI Chat's web_image / web_video skills. Coroutine
    /// based on UnityWebRequest, with a byte cap, a timeout, cancellation, content sniffing
    /// (magic bytes beat Content-Type, which lies often enough), and a public-host-only URL
    /// gate so a model-invented URL can never poke loopback / LAN services such as the
    /// ComfyUI servers.
    /// </summary>
    public static class WebMediaDownloader
    {
        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        /// <summary>Default Accept header for image downloads (web_image).</summary>
        public const string ImageAccept = "image/avif,image/webp,image/apng,image/png,image/jpeg,image/*,*/*;q=0.8";
        /// <summary>Accept header for page fetches (web_page): HTML first, plain text acceptable.</summary>
        public const string HtmlAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.5";

        public enum MediaKind
        {
            Unknown,
            Png,
            Jpeg,
            Gif,
            Webp,
            Avif,
            Bmp,
            Tiff,
            Mp4,
            Webm,
            Mov,
            Mkv,
            Wav,
            Mp3,
            Flac,
            Ogg,
            M4a,
            Html
        }

        public sealed class DownloadResult
        {
            public bool Success;
            public int HttpStatus;
            public string Error;
            public string ContentType;
            /// <summary>charset= parameter of the response Content-Type (lowercase), or null. Only DownloadToMemory fills it.</summary>
            public string Charset;
            public long Bytes;
            public float ElapsedSeconds;
            public MediaKind Kind = MediaKind.Unknown;
            public byte[] Data;      // DownloadToMemory only
            public string FilePath;  // DownloadToFile only
            public bool Cancelled;
        }

        /// <summary>Lets the host abort an in-flight request (Stop / Clear).</summary>
        public sealed class Handle
        {
            internal UnityWebRequest Request;
            public bool Cancelled { get; private set; }
            public void Cancel()
            {
                Cancelled = true;
                try { Request?.Abort(); } catch { }
            }
        }

        /// <summary>
        /// Absolute http/https only, no credentials in the URL, and the host must not be a
        /// loopback / private / link-local literal or "localhost". Hostnames are not resolved
        /// (no blocking DNS on the main thread); the LAN is protected from literal IPs, which
        /// is what a model inventing "http://192.168.1.5:8188/..." would produce.
        /// </summary>
        public static bool IsAllowedPublicHttpUrl(string url, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(url)) { reason = "empty URL"; return false; }
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)) { reason = "not an absolute URL"; return false; }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) { reason = "only http/https URLs are allowed (got " + uri.Scheme + ")"; return false; }
            if (!string.IsNullOrEmpty(uri.UserInfo)) { reason = "URLs with embedded credentials are not allowed"; return false; }
            string host = uri.Host ?? "";
            if (host.Length == 0) { reason = "missing host"; return false; }
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            { reason = "local hosts are not allowed"; return false; }

            IPAddress ip;
            string literal = host.Trim('[', ']');
            if (IPAddress.TryParse(literal, out ip))
            {
                if (IsPrivateOrLocal(ip)) { reason = "private / loopback / link-local addresses are not allowed (" + host + ")"; return false; }
            }
            return true;
        }

        private static bool IsPrivateOrLocal(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && b.Length == 4)
            {
                if (b[0] == 0 || b[0] == 10 || b[0] == 127) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 169 && b[1] == 254) return true;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true; // CGNAT
                if (b[0] >= 224) return true; // multicast / reserved
                return false;
            }
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
                if (b.Length == 16 && (b[0] & 0xfe) == 0xfc) return true; // fc00::/7 unique local
                if (ip.IsIPv4MappedToIPv6) return IsPrivateOrLocal(ip.MapToIPv4());
                bool allZero = true;
                for (int i = 0; i < b.Length; i++) if (b[i] != 0) { allZero = false; break; }
                if (allZero) return true;
            }
            return false;
        }

        /// <param name="accept">Accept header; null = <see cref="ImageAccept"/> (web_page passes <see cref="HtmlAccept"/>).</param>
        public static IEnumerator DownloadToMemory(string url, long maxBytes, float timeoutSeconds, Handle handle, Action<float> onProgress, Action<DownloadResult> onDone, string accept = null)
        {
            var result = new DownloadResult();
            string reason;
            if (!IsAllowedPublicHttpUrl(url, out reason))
            {
                result.Error = "blocked URL: " + reason;
                onDone?.Invoke(result);
                yield break;
            }

            float started = Time.realtimeSinceStartup;
            using (var req = UnityWebRequest.Get(url.Trim()))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                ApplyCommonHeaders(req, string.IsNullOrEmpty(accept) ? ImageAccept : accept);
                req.timeout = Mathf.Max(1, Mathf.RoundToInt(timeoutSeconds));
                if (handle != null) handle.Request = req;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (handle != null && handle.Cancelled) break;
                    if (maxBytes > 0 && (long)req.downloadedBytes > maxBytes)
                    {
                        try { req.Abort(); } catch { }
                        result.Error = "skipped (over " + FormatBytes(maxBytes) + ")";
                        break;
                    }
                    onProgress?.Invoke(req.downloadProgress);
                    yield return null;
                }

                result.ElapsedSeconds = Time.realtimeSinceStartup - started;
                result.HttpStatus = (int)req.responseCode;
                string rawContentType = req.GetResponseHeader("Content-Type");
                result.ContentType = NormalizeContentType(rawContentType);
                result.Charset = ExtractCharset(rawContentType);
                if (handle != null) { handle.Request = null; result.Cancelled = handle.Cancelled; }

                if (result.Cancelled)
                {
                    result.Error = "cancelled";
                    onDone?.Invoke(result);
                    yield break;
                }
                if (!string.IsNullOrEmpty(result.Error))
                {
                    onDone?.Invoke(result);
                    yield break;
                }

                byte[] data = req.downloadHandler != null ? req.downloadHandler.data : null;
                result.Bytes = data != null ? data.Length : 0;
                if (req.result != UnityWebRequest.Result.Success || result.HttpStatus < 200 || result.HttpStatus >= 300)
                {
                    result.Error = DescribeFailure(req, result.HttpStatus, timeoutSeconds);
                    onDone?.Invoke(result);
                    yield break;
                }
                if (data == null || data.Length == 0)
                {
                    result.Error = "HTTP " + result.HttpStatus + " but the body was empty";
                    onDone?.Invoke(result);
                    yield break;
                }
                if (maxBytes > 0 && data.Length > maxBytes)
                {
                    result.Error = "skipped (over " + FormatBytes(maxBytes) + ")";
                    onDone?.Invoke(result);
                    yield break;
                }

                result.Data = data;
                result.Kind = SniffMagic(data);
                result.Success = true;
            }
            onDone?.Invoke(result);
        }

        public static IEnumerator DownloadToFile(string url, string path, long maxBytes, float timeoutSeconds, Handle handle, Action<float> onProgress, Action<DownloadResult> onDone)
        {
            var result = new DownloadResult { FilePath = path };
            string reason;
            if (!IsAllowedPublicHttpUrl(url, out reason))
            {
                result.Error = "blocked URL: " + reason;
                onDone?.Invoke(result);
                yield break;
            }

            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); } catch { }

            float started = Time.realtimeSinceStartup;
            using (var req = UnityWebRequest.Get(url.Trim()))
            {
                var fileHandler = new DownloadHandlerFile(path);
                fileHandler.removeFileOnAbort = true;
                req.downloadHandler = fileHandler;
                ApplyCommonHeaders(req, "video/*,image/gif,*/*;q=0.8");
                req.timeout = Mathf.Max(1, Mathf.RoundToInt(timeoutSeconds));
                if (handle != null) handle.Request = req;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (handle != null && handle.Cancelled) break;
                    if (maxBytes > 0 && (long)req.downloadedBytes > maxBytes)
                    {
                        try { req.Abort(); } catch { }
                        result.Error = "skipped (over " + FormatBytes(maxBytes) + ")";
                        break;
                    }
                    onProgress?.Invoke(req.downloadProgress);
                    yield return null;
                }

                result.ElapsedSeconds = Time.realtimeSinceStartup - started;
                result.HttpStatus = (int)req.responseCode;
                result.ContentType = NormalizeContentType(req.GetResponseHeader("Content-Type"));
                result.Bytes = (long)req.downloadedBytes;
                if (handle != null) { handle.Request = null; result.Cancelled = handle.Cancelled; }

                if (result.Cancelled)
                {
                    result.Error = "cancelled";
                    TryDelete(path);
                    onDone?.Invoke(result);
                    yield break;
                }
                if (!string.IsNullOrEmpty(result.Error))
                {
                    TryDelete(path);
                    onDone?.Invoke(result);
                    yield break;
                }
                if (req.result != UnityWebRequest.Result.Success || result.HttpStatus < 200 || result.HttpStatus >= 300)
                {
                    result.Error = DescribeFailure(req, result.HttpStatus, timeoutSeconds);
                    TryDelete(path);
                    onDone?.Invoke(result);
                    yield break;
                }
            }

            // The file handler has closed its stream now; sniff the header.
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length == 0)
                {
                    result.Error = "HTTP " + result.HttpStatus + " but the saved file is empty";
                    TryDelete(path);
                    onDone?.Invoke(result);
                    yield break;
                }
                result.Bytes = fi.Length;
                byte[] head = new byte[Math.Min(64, fi.Length)];
                using (var fs = fi.OpenRead())
                {
                    int read = 0;
                    while (read < head.Length)
                    {
                        int n = fs.Read(head, read, head.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
                result.Kind = SniffMagic(head);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = "could not read downloaded file: " + ex.Message;
                TryDelete(path);
            }
            onDone?.Invoke(result);
        }

        private static void ApplyCommonHeaders(UnityWebRequest req, string accept)
        {
            try { req.SetRequestHeader("User-Agent", BrowserUserAgent); } catch { }
            try { req.SetRequestHeader("Accept", accept); } catch { }
            try { req.SetRequestHeader("Accept-Language", "en-US,en;q=0.9"); } catch { }
        }

        private static string DescribeFailure(UnityWebRequest req, int status, float timeoutSeconds)
        {
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                string err = req.error ?? "";
                if (err.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "timed out after " + timeoutSeconds.ToString("0", CultureInfo.InvariantCulture) + "s";
                return "connection error: " + err;
            }
            if (status > 0)
            {
                string text = ReasonPhrase(status);
                return "HTTP " + status + (string.IsNullOrEmpty(text) ? "" : " " + text);
            }
            return string.IsNullOrEmpty(req.error) ? "request failed" : req.error;
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 410: return "Gone";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                default: return "";
            }
        }

        private static string NormalizeContentType(string ct)
        {
            if (string.IsNullOrEmpty(ct)) return "";
            int semi = ct.IndexOf(';');
            if (semi >= 0) ct = ct.Substring(0, semi);
            return ct.Trim().ToLowerInvariant();
        }

        /// <summary>"text/html; charset=UTF-8" -> "utf-8"; null when absent.</summary>
        private static string ExtractCharset(string ct)
        {
            if (string.IsNullOrEmpty(ct)) return null;
            int at = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            string v = ct.Substring(at + 8).Trim();
            int end = 0;
            while (end < v.Length && v[end] != ';' && v[end] != ',' && !char.IsWhiteSpace(v[end])) end++;
            v = v.Substring(0, end).Trim('"', '\'').Trim();
            return v.Length == 0 ? null : v.ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        /// <summary>Identify a media container from its first bytes.</summary>
        public static MediaKind SniffMagic(byte[] head)
        {
            if (head == null || head.Length < 4) return MediaKind.Unknown;
            if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return MediaKind.Png;
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return MediaKind.Jpeg;
            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8') return MediaKind.Gif;
            if (head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P') return MediaKind.Webp;
            if (head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E') return MediaKind.Wav;
            if (head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C') return MediaKind.Flac;
            if (head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S') return MediaKind.Ogg; // vorbis/opus (rarely theora)
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return MediaKind.Mp3;
            if (head[0] == 'B' && head[1] == 'M') return MediaKind.Bmp;
            if ((head[0] == 'I' && head[1] == 'I' && head[2] == 0x2A && head[3] == 0x00) || (head[0] == 'M' && head[1] == 'M' && head[2] == 0x00 && head[3] == 0x2A)) return MediaKind.Tiff;
            if (head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return MediaKind.Webm; // EBML (webm/mkv)
            if (head.Length >= 12 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
            {
                string brand = System.Text.Encoding.ASCII.GetString(head, 8, 4).ToLowerInvariant();
                if (brand.StartsWith("avif") || brand.StartsWith("avis")) return MediaKind.Avif;
                if (brand.StartsWith("qt")) return MediaKind.Mov;
                if (brand.StartsWith("m4a") || brand.StartsWith("m4b")) return MediaKind.M4a;
                return MediaKind.Mp4;
            }
            // Raw MPEG audio frame sync (an mp3 with no ID3 tag): FF Ex/Fx. Checked after
            // JPEG (FF D8) and the container magics above, so no overlap.
            if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return MediaKind.Mp3;
            // Text-ish: HTML / JSON / XML interstitials, error pages.
            int limit = Math.Min(head.Length, 64);
            int i = 0;
            while (i < limit && (head[i] == ' ' || head[i] == '\t' || head[i] == '\r' || head[i] == '\n' || head[i] == 0xEF || head[i] == 0xBB || head[i] == 0xBF)) i++;
            if (i < limit && (head[i] == '<' || head[i] == '{' || head[i] == '['))
                return MediaKind.Html;
            return MediaKind.Unknown;
        }

        public static bool IsImageKind(MediaKind k)
        {
            switch (k)
            {
                case MediaKind.Png:
                case MediaKind.Jpeg:
                case MediaKind.Gif:
                case MediaKind.Webp:
                case MediaKind.Avif:
                case MediaKind.Bmp:
                case MediaKind.Tiff:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsVideoKind(MediaKind k)
        {
            switch (k)
            {
                case MediaKind.Mp4:
                case MediaKind.Webm:
                case MediaKind.Mov:
                case MediaKind.Mkv:
                case MediaKind.Gif: // animated gif is a video for web_video purposes
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsAudioKind(MediaKind k)
        {
            switch (k)
            {
                case MediaKind.Wav:
                case MediaKind.Mp3:
                case MediaKind.Flac:
                case MediaKind.Ogg:
                case MediaKind.M4a:
                    return true;
                default:
                    return false;
            }
        }

        public static string KindLabel(MediaKind k)
        {
            return k == MediaKind.Unknown ? "unknown format" : k.ToString().ToUpperInvariant();
        }

        public static string ExtensionFor(MediaKind k)
        {
            switch (k)
            {
                case MediaKind.Png: return ".png";
                case MediaKind.Jpeg: return ".jpg";
                case MediaKind.Gif: return ".gif";
                case MediaKind.Webp: return ".webp";
                case MediaKind.Avif: return ".avif";
                case MediaKind.Bmp: return ".bmp";
                case MediaKind.Tiff: return ".tif";
                case MediaKind.Mp4: return ".mp4";
                case MediaKind.Webm: return ".webm";
                case MediaKind.Mov: return ".mov";
                case MediaKind.Mkv: return ".mkv";
                case MediaKind.Wav: return ".wav";
                case MediaKind.Mp3: return ".mp3";
                case MediaKind.Flac: return ".flac";
                case MediaKind.Ogg: return ".ogg";
                case MediaKind.M4a: return ".m4a";
                default: return ".bin";
            }
        }
    }

    /// <summary>
    /// Turns downloaded image bytes into a PNG/JPG file that PicMain.LoadImageByFilename
    /// (Texture2D.LoadImage: PNG/JPEG only) can open, using the bundled ffmpeg for every
    /// other format and for downscaling oversized originals. Reports the DECODED size so
    /// min_width checks never trust a search engine's claimed dimensions.
    /// </summary>
    public static class WebImageConverter
    {
        public sealed class Result
        {
            public bool Success;
            public string Path;
            public int Width;
            public int Height;
            public string Error;
            /// <summary>Human readable conversion note for the trace, e.g. "WEBP -> ffmpeg -> PNG".</summary>
            public string Note;
            /// <summary>PNG-encoded pixels (providers declare every image as image/png), for the vision check.</summary>
            public byte[] PngBytes;
        }

        public static string GetOutputDir()
        {
            string dir = Path.Combine(FfmpegTool.GetAppRoot(), "tempCache", "aichat_web_images");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static IEnumerator NormalizeToLoadableImage(byte[] data, WebMediaDownloader.MediaKind kind, int maxSide, Action<Result> onDone)
        {
            var result = new Result();
            if (data == null || data.Length == 0)
            {
                result.Error = "no image data";
                onDone?.Invoke(result);
                yield break;
            }

            string dir = GetOutputDir();
            string stem = "web_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            bool directlyLoadable = kind == WebMediaDownloader.MediaKind.Png || kind == WebMediaDownloader.MediaKind.Jpeg;

            if (directlyLoadable)
            {
                int w, h;
                byte[] directPng;
                bool decoded = TryDecodeSize(data, out w, out h, out directPng);
                if (decoded && w > 0 && h > 0 && (maxSide <= 0 || (w <= maxSide && h <= maxSide)))
                {
                    string ext = kind == WebMediaDownloader.MediaKind.Png ? ".png" : ".jpg";
                    result.Path = Path.Combine(dir, stem + ext);
                    try
                    {
                        File.WriteAllBytes(result.Path, data);
                        result.Width = w;
                        result.Height = h;
                        result.Success = true;
                        result.Note = WebMediaDownloader.KindLabel(kind) + " " + w + "x" + h;
                        result.PngBytes = kind == WebMediaDownloader.MediaKind.Png ? data : directPng;
                    }
                    catch (Exception ex)
                    {
                        result.Error = "could not save image: " + ex.Message;
                    }
                    onDone?.Invoke(result);
                    yield break;
                }
                // Either Unity refused the bytes or it is oversized: let ffmpeg handle it.
            }

            string srcPath = Path.Combine(dir, "src_" + stem + WebMediaDownloader.ExtensionFor(kind));
            string outPath = Path.Combine(dir, stem + ".png");
            try
            {
                File.WriteAllBytes(srcPath, data);
            }
            catch (Exception ex)
            {
                result.Error = "could not write temp image: " + ex.Message;
                onDone?.Invoke(result);
                yield break;
            }

            FfmpegTool.ClipResult conv = null;
            yield return FfmpegTool.ConvertImageToPng(srcPath, outPath, maxSide, r => conv = r);
            try { File.Delete(srcPath); } catch { }

            if (conv == null || !conv.Success)
            {
                result.Error = "ffmpeg could not convert " + WebMediaDownloader.KindLabel(kind) + ": " + FirstLine(conv != null ? conv.Error : "unknown error");
                onDone?.Invoke(result);
                yield break;
            }

            byte[] png = null;
            try { png = File.ReadAllBytes(outPath); } catch (Exception ex) { result.Error = "could not read converted PNG: " + ex.Message; }
            if (png == null)
            {
                onDone?.Invoke(result);
                yield break;
            }

            int cw, ch;
            if (!TryDecodeSize(png, out cw, out ch) || cw <= 0 || ch <= 0)
            {
                result.Error = "converted PNG could not be decoded";
                try { File.Delete(outPath); } catch { }
                onDone?.Invoke(result);
                yield break;
            }

            result.Path = outPath;
            result.Width = cw;
            result.Height = ch;
            result.Success = true;
            result.Note = WebMediaDownloader.KindLabel(kind) + " -> ffmpeg -> PNG " + cw + "x" + ch;
            result.PngBytes = png;
            onDone?.Invoke(result);
        }

        /// <summary>Decode with a throwaway texture to learn the real pixel size.</summary>
        public static bool TryDecodeSize(byte[] data, out int width, out int height)
        {
            byte[] unused;
            return TryDecodeSize(data, out width, out height, out unused, encodePng: false);
        }

        /// <summary>
        /// Decode with a throwaway texture to learn the real pixel size and, when asked,
        /// re-encode the pixels as PNG (for JPEG sources the vision sidecar needs PNG bytes).
        /// </summary>
        public static bool TryDecodeSize(byte[] data, out int width, out int height, out byte[] pngBytes, bool encodePng = true)
        {
            width = 0;
            height = 0;
            pngBytes = null;
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data, false)) return false;
                width = tex.width;
                height = tex.height;
                // LoadImage leaves an 8x8 magenta/garbage texture for undecodable input.
                bool ok = width > 8 || height > 8;
                if (ok && encodePng)
                {
                    try { pngBytes = tex.EncodeToPNG(); } catch { pngBytes = null; }
                }
                return ok;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i).TrimEnd('\r');
        }
    }
}
