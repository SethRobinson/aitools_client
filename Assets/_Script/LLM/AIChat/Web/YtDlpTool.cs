using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using AITools.AIChat.Video;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// Wrapper around the bundled yt-dlp helper (utils/yt-dlp/yt-dlp.exe) used by AI Chat's
    /// web_video skill to pull a page-hosted video (YouTube, Vimeo, ...) into tempCache so the
    /// host can normalize the requested section with FfmpegTool.CreateClip. Only the requested
    /// range is downloaded, even for long sources. Subtitle requests never download the video.
    ///
    /// YouTube also needs a JavaScript runtime for its signature challenge; without one the
    /// download is throttled or formats go missing. yt-dlp only enables deno by default, so
    /// deno / node / bun are auto-detected on PATH and passed via --js-runtimes.
    ///
    /// Mirrors FfmpegTool: blocking process work runs inside Task.Run, the coroutine polls,
    /// progress lines are buffered thread-safely and drained on the main thread. The exact
    /// command line is exposed for the chat trace.
    /// </summary>
    public static class YtDlpTool
    {
        public const int DefaultMaxHeight = 480;
        private const int DownloadTimeoutMs = 10 * 60 * 1000;

        public sealed class Result
        {
            public bool Success;
            public string OutputPath;
            public string Command;
            public string Stdout;
            public string Stderr;
            public int ExitCode;
            public string Error;
            public float ElapsedSeconds;
            public bool Cancelled;
            public string Transcript;
            public bool LoginOffered;
        }

        private sealed class LineBuffer
        {
            private readonly object _lock = new object();
            private readonly List<string> _pending = new List<string>();
            public void Add(string line) { lock (_lock) { _pending.Add(line); } }
            public List<string> Drain()
            {
                lock (_lock)
                {
                    if (_pending.Count == 0) return null;
                    var copy = new List<string>(_pending);
                    _pending.Clear();
                    return copy;
                }
            }
        }

        public static string ExpectedToolPath()
        {
            return Path.Combine(FfmpegTool.GetAppRoot(), "utils", "yt-dlp", "yt-dlp.exe");
        }

        public static bool TryGetToolPath(out string exePath, out string error)
        {
            error = null;
            exePath = ExpectedToolPath();
            if (File.Exists(exePath)) return true;

            // Fall back to a user-installed copy on PATH.
            string onPath = FindOnPath("yt-dlp.exe");
            if (onPath != null)
            {
                exePath = onPath;
                return true;
            }

            error = "yt-dlp.exe was not found. Expected: " + ExpectedToolPath() + " (or on PATH). Download it from https://github.com/yt-dlp/yt-dlp/releases";
            return false;
        }

        private static string FindOnPath(string fileName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Find a JavaScript runtime yt-dlp can use for YouTube's signature challenge.
        /// Returns the yt-dlp runtime name ("deno", "node", "bun") and the exe path, or
        /// false when none is installed (downloads then fall back to throttled / fewer formats).
        /// </summary>
        public static bool TryDetectJsRuntime(out string runtimeName, out string exePath)
        {
            runtimeName = null;
            exePath = null;
            string[][] candidates =
            {
                new[] { "deno", "deno.exe" },
                new[] { "node", "node.exe" },
                new[] { "bun", "bun.exe" },
            };
            foreach (var c in candidates)
            {
                string found = FindOnPath(c[1]);
                if (found != null)
                {
                    runtimeName = c[0];
                    exePath = found;
                    return true;
                }
            }
            return false;
        }

        /// <summary>One-line status for the Settings > Web tab.</summary>
        public static string DescribeJsRuntime()
        {
            string name, path;
            if (TryDetectJsRuntime(out name, out path))
                return "JS runtime for YouTube: " + name + " (" + path + ")";
            return "JS runtime for YouTube: NONE found - YouTube downloads will be slow or fail. Install Deno (winget install DenoLand.Deno) or Node.js and restart.";
        }

        public static string GetOutputDir()
        {
            string dir = Path.Combine(FfmpegTool.GetAppRoot(), "tempCache", "aichat_web_videos");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Extension test only: is this URL a media FILE rather than a page?</summary>
        public static bool LooksLikeDirectMediaUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)) return false;
            string path = (uri.AbsolutePath ?? "").ToLowerInvariant();
            return path.EndsWith(".mp4") || path.EndsWith(".webm") || path.EndsWith(".mov") || path.EndsWith(".m4v")
                || path.EndsWith(".mkv") || path.EndsWith(".gif") || path.EndsWith(".avi") || path.EndsWith(".mpg") || path.EndsWith(".mpeg");
        }

        /// <summary>The URL's path names a bare sound file (web_audio direct download; web_video redirects here).</summary>
        public static bool LooksLikeDirectAudioUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)) return false;
            return FfmpegTool.IsSupportedAudioExtension(uri.AbsolutePath ?? "");
        }

        public static string BuildArgs(string url, string outputTemplate, int maxHeight, float maxSourceSeconds, long maxFileBytes, string ffmpegDir, string cookiesBrowser, string jsRuntime,
            float startSeconds = 0, float durationSeconds = 5, string cookiesFile = null)
        {
            var sb = new StringBuilder();
            sb.Append("--ignore-config --no-playlist --newline --restrict-filenames --no-part --socket-timeout 20 --retries 2");
            if (!string.IsNullOrEmpty(jsRuntime))
                sb.Append(" --js-runtimes ").Append(jsRuntime);
            if (maxSourceSeconds > 0)
                sb.Append(" --match-filters ").Append(FfmpegTool.QuoteArg("duration<?" + Mathf.RoundToInt(maxSourceSeconds).ToString(CultureInfo.InvariantCulture)));
            // --max-filesize tests the full source's size even for section downloads.
            // Enforce the byte limit on the files actually being written instead.
            sb.Append(" -f ").Append(FfmpegTool.QuoteArg(
                // HLS seeks by segment. YouTube's direct DASH URLs can stall on nonzero
                // FFmpeg seeks even when the same source downloads quickly from time zero.
                "bv*[height<=" + maxHeight + "][vcodec^=avc1][protocol^=m3u8]+ba[protocol^=m3u8]/"
                + "bv*[height<=" + maxHeight + "][protocol^=m3u8]+ba[protocol^=m3u8]/"
                + "bv*[height<=" + maxHeight + "][ext=mp4]+ba[ext=m4a]/bv*[height<=" + maxHeight + "]+ba/b[height<=" + maxHeight + "]/b"));
            sb.Append(" --merge-output-format mp4");
            sb.Append(" --download-sections ").Append(FfmpegTool.QuoteArg("*"
                + startSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "-"
                + (startSeconds + durationSeconds).ToString("0.###", CultureInfo.InvariantCulture)));
            sb.Append(" --force-keyframes-at-cuts");
            sb.Append(" --downloader-args ").Append(FfmpegTool.QuoteArg("ffmpeg_i:-rw_timeout 20000000"));
            if (!string.IsNullOrEmpty(ffmpegDir))
                sb.Append(" --ffmpeg-location ").Append(FfmpegTool.QuoteArg(ffmpegDir));
            sb.Append(" -o ").Append(FfmpegTool.QuoteArg(outputTemplate));
            AppendCookies(sb, cookiesBrowser, cookiesFile);
            // "--" ends option parsing so the URL can never be read as a flag, whatever it
            // contains (the URL gate also rejects quotes/backslashes/whitespace outright).
            sb.Append(" -- ").Append(FfmpegTool.QuoteArg(url.Trim()));
            return sb.ToString();
        }

        private static void AppendCookies(StringBuilder args, string browser, string file)
        {
            if (!string.IsNullOrWhiteSpace(file))
                args.Append(" --cookies ").Append(FfmpegTool.QuoteArg(file.Trim()));
            else if (!string.IsNullOrWhiteSpace(browser))
                args.Append(" --cookies-from-browser ").Append(FfmpegTool.QuoteArg(browser.Trim()));
        }

        public static string BuildTranscriptArgs(string url, string outputTemplate, string language, string cookiesBrowser, string cookiesFile, string jsRuntime)
        {
            var args = new StringBuilder("--ignore-config --no-playlist --newline --skip-download --write-subs --write-auto-subs --sub-format vtt --socket-timeout 20 --retries 2");
            if (!string.IsNullOrEmpty(jsRuntime)) args.Append(" --js-runtimes ").Append(jsRuntime);
            args.Append(" --sub-langs ").Append(FfmpegTool.QuoteArg(language));
            args.Append(" -o ").Append(FfmpegTool.QuoteArg(outputTemplate));
            AppendCookies(args, cookiesBrowser, cookiesFile);
            args.Append(" -- ").Append(FfmpegTool.QuoteArg(url.Trim()));
            return args.ToString();
        }

        // Validate without ever including a cookie record in an exception or diagnostic.
        public static string ValidateCookieFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (!Path.IsPathRooted(path) || !File.Exists(path))
                    return "Cookie file not found. Set its full path in Settings > Web, or clear Cookie file to use the browser setting.";
                if (new FileInfo(path).Length > 10 * 1024 * 1024)
                    return "Cookie file is too large (maximum 10 MB). Export only the YouTube cookies.";
                bool first = true;
                foreach (string raw in File.ReadLines(path))
                {
                    if (first)
                    {
                        first = false;
                        if (raw != "# Netscape HTTP Cookie File" && raw != "# HTTP Cookie File")
                            return "Cookie file must be a Netscape-format cookies.txt export.";
                        continue;
                    }
                    string line = raw.StartsWith("#HttpOnly_", StringComparison.Ordinal) ? raw.Substring(10) : raw;
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] fields = line.Split('\t');
                    if (fields.Length != 7 || string.IsNullOrEmpty(fields[0])
                        || (fields[1] != "TRUE" && fields[1] != "FALSE")
                        || (fields[3] != "TRUE" && fields[3] != "FALSE")
                        || (fields[0].StartsWith(".", StringComparison.Ordinal) != (fields[1] == "TRUE"))
                        || (fields[4].Length > 0 && !long.TryParse(fields[4], out _)))
                        return "Cookie file contains an invalid Netscape cookie record. Re-export it from the browser.";
                }
                return first ? "Cookie file is empty. Export the YouTube cookies again." : null;
            }
            catch { return "Cookie file could not be read. Check its path and file permissions in Settings > Web."; }
        }

        public static string SanitizeDiagnostic(string text, string cookieFile)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            text = YouTubeLogin.RedactProfile(text);
            if (!string.IsNullOrWhiteSpace(cookieFile))
            {
                text = text.Replace(FfmpegTool.QuoteArg(cookieFile), "\"[configured cookie file]\"");
                text = text.Replace(cookieFile, "[configured cookie file]");
            }
            // Malformed cookie loaders can echo the offending record. Never forward it.
            if (text.Contains("\t") || text.IndexOf("invalid length", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Invalid cookie-file record. Re-export the YouTube cookies in Netscape format.";
            return text;
        }

        public static bool IsTranscriptLanguage(string language)
        {
            return !string.IsNullOrEmpty(language) && language.Length <= 40
                && Regex.IsMatch(language, @"\A[a-zA-Z0-9]+(?:[-_][a-zA-Z0-9]+)*\z");
        }

        /// <summary>
        /// Download the video at <paramref name="url"/> (capped at DefaultMaxHeight, skipped when
        /// longer than <paramref name="maxSourceSeconds"/> or over <paramref name="maxFileBytes"/>)
        /// as a section in tempCache/aichat_web_videos; the caller normalizes it from time zero
        /// with FfmpegTool.CreateClip. <paramref name="onProgressLine"/> receives yt-dlp's stdout /
        /// stderr lines on the main thread. <paramref name="onCommandBuilt"/> fires before the
        /// process starts so the trace can show the exact command line.
        /// </summary>
        public static IEnumerator DownloadVideo(
            string url,
            float maxSourceSeconds,
            long maxFileBytes,
            FfmpegTool.CancelToken cancelToken,
            Action<string> onCommandBuilt,
            Action<string> onProgressLine,
            Action<Result> onDone,
            float startSeconds = 0, float durationSeconds = 5)
        {
            return Download(url, maxSourceSeconds, maxFileBytes, cancelToken, onCommandBuilt, onProgressLine, onDone,
                startSeconds, durationSeconds, null);
        }

        public static IEnumerator DownloadTranscript(string url, string language, FfmpegTool.CancelToken cancelToken,
            Action<string> onCommandBuilt, Action<string> onProgressLine, Action<Result> onDone)
        {
            return Download(url, 0, 0, cancelToken, onCommandBuilt, onProgressLine, onDone, 0, 0, language);
        }

        private static IEnumerator Download(string url, float maxSourceSeconds, long maxFileBytes, FfmpegTool.CancelToken cancelToken,
            Action<string> onCommandBuilt, Action<string> onProgressLine, Action<Result> onDone,
            float startSeconds, float durationSeconds, string language)
        {
            var result = new Result();
            bool transcript = language != null;
            string reason;
            if (!WebMediaDownloader.IsAllowedPublicHttpUrl(url, out reason)
                || (transcript && !IsTranscriptLanguage(language))
                || (!transcript && (float.IsNaN(startSeconds) || float.IsInfinity(startSeconds) || startSeconds < 0
                    || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0
                    || float.IsInfinity(startSeconds + durationSeconds))))
            {
                result.Error = reason ?? "Invalid transcript language or clip range.";
                onDone?.Invoke(result);
                yield break;
            }
            string exe, toolError;
            if (!TryGetToolPath(out exe, out toolError))
            {
                result.Error = toolError;
                onDone?.Invoke(result);
                yield break;
            }

            string ffmpegDir = null;
            if (!transcript)
            {
                if (FfmpegTool.TryGetToolPaths(out string ffmpegPath, out _, out string ffmpegError))
                    ffmpegDir = Path.GetDirectoryName(ffmpegPath);
                else
                {
                    result.Error = "yt-dlp section downloads need ffmpeg: " + ffmpegError;
                    onDone?.Invoke(result);
                    yield break;
                }
            }

            string dir = GetOutputDir();
            string stem = "ytdlp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string template = Path.Combine(dir, stem + ".%(ext)s");

            var cfg = Config.Get();
            string cookies = cfg != null ? cfg.GetYtDlpCookiesBrowser() : "";
            string cookieFile = cfg != null ? cfg.GetYtDlpCookiesFile() : "";
            YouTubeLogin.ResolveCookies(url, cfg != null && cfg.GetYouTubeFirefoxLogin(), ref cookies, ref cookieFile);
            if (cfg != null && cfg.GetYouTubeFirefoxLogin() && YouTubeLogin.IsYouTubeUrl(url)
                && !File.Exists(Path.Combine(YouTubeLogin.ProfileDirectory, "cookies.sqlite")))
            {
                result.Error = "The remembered YouTube session is missing. Sign in again.";
                onDone?.Invoke(result);
                yield break;
            }
            result.Error = ValidateCookieFile(cookieFile);
            if (result.Error != null) { onDone?.Invoke(result); yield break; }
            string jsRuntime, jsPath;
            if (!TryDetectJsRuntime(out jsRuntime, out jsPath)) jsRuntime = null;
            string args = transcript ? BuildTranscriptArgs(url, template, language, cookies, cookieFile, jsRuntime)
                : BuildArgs(url, template, DefaultMaxHeight, maxSourceSeconds, maxFileBytes, ffmpegDir, cookies, jsRuntime, startSeconds, durationSeconds, cookieFile);
            result.Command = SanitizeDiagnostic(FfmpegTool.QuoteArg(exe) + " " + args, cookieFile);
            onCommandBuilt?.Invoke(result.Command);

            var lines = new LineBuffer();
            float started = Time.realtimeSinceStartup;
            cancelToken = cancelToken ?? new FfmpegTool.CancelToken();
            long byteLimit = transcript ? 10L * 1024 * 1024 : maxFileBytes;
            bool sizeExceeded = false;
            float nextSizeCheck = started;
            Task<FfmpegTool.ProcessResult> task = Task.Run(() =>
                FfmpegTool.RunProcessCancellable(exe, args, transcript ? 120000 : DownloadTimeoutMs, cancelToken,
                    line => lines.Add(SanitizeDiagnostic(line, cookieFile)),
                    line => lines.Add("stderr: " + SanitizeDiagnostic(line, cookieFile))));

            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= nextSizeCheck)
                {
                    nextSizeCheck = Time.realtimeSinceStartup + 0.25f;
                    if (byteLimit > 0 && OutputBytes(dir, stem) > byteLimit)
                    { sizeExceeded = true; cancelToken.Cancel(); }
                }
                var drained = lines.Drain();
                if (drained != null && onProgressLine != null)
                {
                    for (int i = 0; i < drained.Count; i++)
                        onProgressLine(drained[i]);
                }
                yield return null;
            }
            var tail = lines.Drain();
            if (tail != null && onProgressLine != null)
            {
                for (int i = 0; i < tail.Count; i++)
                    onProgressLine(tail[i]);
            }

            result.ElapsedSeconds = Time.realtimeSinceStartup - started;
            if (task.IsFaulted)
            {
                result.Error = "yt-dlp process failed to run.";
                CleanupPartials(dir, stem);
                onDone?.Invoke(result);
                yield break;
            }

            FfmpegTool.ProcessResult pr = task.Result;
            result.Stdout = SanitizeOutput(pr.Stdout, cookieFile);
            result.Stderr = SanitizeOutput(pr.Stderr, cookieFile);
            result.ExitCode = pr.ExitCode;
            sizeExceeded |= byteLimit > 0 && OutputBytes(dir, stem) > byteLimit;
            result.Cancelled = cancelToken.CancelRequested && !sizeExceeded;

            // yt-dlp may pick a different extension than requested; find what it produced.
            string output = FindOutput(dir, stem, transcript);
            if (pr.Success && !result.Cancelled && !sizeExceeded && !string.IsNullOrEmpty(output))
            {
                result.Success = true;
                result.OutputPath = output;
                if (transcript)
                {
                    try
                    {
                        if (new FileInfo(output).Length > 10 * 1024 * 1024)
                            throw new IOException();
                        result.Transcript = ParseVtt(File.ReadAllText(output));
                        if (string.IsNullOrWhiteSpace(result.Transcript))
                            result.Error = "The caption track contains no readable transcript.";
                    }
                    catch { result.Error = "The caption track could not be read or exceeds 10 MB."; }
                    result.Success = result.Error == null;
                    CleanupPartials(dir, stem);
                    result.OutputPath = null;
                }
            }
            else
            {
                result.Success = false;
                string combined = (pr.Stdout ?? "") + "\n" + (pr.Stderr ?? "");
                if (sizeExceeded)
                    result.Error = "Downloaded " + (transcript ? "caption track" : "clip") + " exceeds the " + (byteLimit / (1024 * 1024)) + " MB limit.";
                else if (result.Cancelled)
                    result.Error = "cancelled";
                else if (combined.IndexOf("does not pass filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Error = "skipped: the source video is longer than max_source_minutes (" + (maxSourceSeconds / 60f).ToString("0.#", CultureInfo.InvariantCulture) + " min); raise max_source_minutes or pick a shorter video";
                else if (combined.IndexOf("File is larger than max-filesize", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Error = "skipped: the source file is larger than the " + (maxFileBytes / (1024L * 1024L)) + " MB cap";
                else if (!string.IsNullOrEmpty(pr.Error))
                    result.Error = SanitizeDiagnostic(pr.Error, cookieFile);
                else if (transcript && pr.Success)
                    result.Error = "No subtitles or automatic captions are available for language '" + language + "'. Try the video's original language; this is not evidence of a login failure.";
                else if (pr.Success)
                    result.Error = "yt-dlp exited 0 but produced no output file";
                else
                    result.Error = "yt-dlp exited " + pr.ExitCode + ": " + LastErrorLine(result.Stderr, result.Stdout);
                CleanupPartials(dir, stem);
            }
            onDone?.Invoke(result);
        }

        private static string LastErrorLine(string stderr, string stdout)
        {
            var lines = new List<string>();
            AddLines(lines, stderr);
            AddLines(lines, stdout);
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) return lines[i];
            }
            return lines.Count > 0 ? lines[lines.Count - 1] : "no output";
        }

        private static string FindOutput(string dir, string stem, bool transcript)
        {
            try
            {
                string best = null;
                long bestSize = -1;
                foreach (string f in Directory.GetFiles(dir, stem + ".*"))
                {
                    string lower = f.ToLowerInvariant();
                    if (lower.EndsWith(".part") || lower.EndsWith(".ytdl") || lower.EndsWith(".temp")) continue;
                    if (transcript ? !lower.EndsWith(".vtt") : !LooksLikeDirectMediaUrl("https://example.com/" + Path.GetFileName(f))) continue;
                    long size = new FileInfo(f).Length;
                    if (size > bestSize) { bestSize = size; best = f; }
                }
                return bestSize > 0 ? best : null;
            }
            catch
            {
                return null;
            }
        }

        private static long OutputBytes(string dir, string stem)
        {
            long bytes = 0;
            try { foreach (string file in Directory.GetFiles(dir, stem + ".*")) bytes += new FileInfo(file).Length; }
            catch { } // A file can disappear during FFmpeg's rename.
            return bytes;
        }

        private static string SanitizeOutput(string text, string cookieFile)
        {
            var lines = new List<string>();
            AddLines(lines, text);
            return string.Join("\n", lines.ConvertAll(line => SanitizeDiagnostic(line, cookieFile)));
        }

        /// <summary>Remove VTT timing/markup and repeated rolling-caption lines, retaining spoken text.</summary>
        public static string ParseVtt(string vtt)
        {
            var output = new StringBuilder();
            string previous = null;
            bool inCue = false;
            foreach (string raw in (vtt ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (raw.Length == 0) { inCue = false; continue; }
                if (Regex.IsMatch(line, @"^(?:\d+:)?\d{2}:\d{2}\.\d{3}\s+-->"))
                { inCue = true; continue; }
                if (!inCue) continue;
                line = System.Net.WebUtility.HtmlDecode(Regex.Replace(line, "<[^>]+>", "")).Trim();
                if (line.Length == 0 || line == previous) continue;
                output.AppendLine(line);
                previous = line;
            }
            return WebPageReader.StripActionTags(output.ToString().Trim());
        }

        private static void CleanupPartials(string dir, string stem)
        {
            try
            {
                RTSafeFileSystem.DeleteYtDlpPartials(
                    RTSafeFileSystem.GetAppRoot(Application.dataPath), dir, stem);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("yt-dlp cleanup skipped: " + ex.Message); }
        }

        /// <summary>
        /// Last N non-empty lines of stdout+stderr for the trace bubble. Per-percent
        /// "[download]  63.1% of ..." progress lines are dropped (the live status line
        /// already showed them); the final "100% of X in T" summary line is kept.
        /// </summary>
        public static List<string> OutputTail(Result r, int maxLines)
        {
            var all = new List<string>();
            if (r != null)
            {
                AddLines(all, r.Stdout);
                AddLines(all, r.Stderr);
            }
            var filtered = new List<string>(all.Count);
            foreach (string line in all)
            {
                if (line.StartsWith("[download]", StringComparison.Ordinal) && line.IndexOf("ETA", StringComparison.Ordinal) >= 0)
                    continue;
                filtered.Add(line);
            }
            if (filtered.Count <= maxLines) return filtered;
            return filtered.GetRange(filtered.Count - maxLines, maxLines);
        }

        private static void AddLines(List<string> into, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line)) into.Add(line.TrimEnd());
            }
        }
    }
}
