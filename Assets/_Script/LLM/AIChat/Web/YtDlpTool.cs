using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using AITools.AIChat.Video;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// Wrapper around the bundled yt-dlp helper (utils/yt-dlp/yt-dlp.exe) used by AI Chat's
    /// web_video skill to pull a page-hosted video (YouTube, Vimeo, ...) into tempCache so the
    /// host can cut the requested section with FfmpegTool.CreateClip.
    ///
    /// Why the WHOLE video (capped at 480p) instead of yt-dlp's --download-sections: sections
    /// are fetched by handing the stream URL to ffmpeg, and YouTube throttles ffmpeg's plain
    /// HTTP reads to a few KiB/s (a 5 s cut of a 10 min video took 5+ minutes in testing),
    /// while yt-dlp's own chunked downloader runs at tens of MiB/s (the same 10 min video at
    /// 480p: 9 s). 480p is all the chat clip normalizer keeps anyway (832x480 max).
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

        public static string BuildArgs(string url, string outputTemplate, int maxHeight, float maxSourceSeconds, long maxFileBytes, string ffmpegDir, string cookiesBrowser, string jsRuntime)
        {
            var sb = new StringBuilder();
            sb.Append("--no-playlist --newline --restrict-filenames --no-part");
            if (!string.IsNullOrEmpty(jsRuntime))
                sb.Append(" --js-runtimes ").Append(jsRuntime);
            if (maxSourceSeconds > 0)
                sb.Append(" --match-filters ").Append(FfmpegTool.QuoteArg("duration<?" + Mathf.RoundToInt(maxSourceSeconds).ToString(CultureInfo.InvariantCulture)));
            if (maxFileBytes > 0)
                sb.Append(" --max-filesize ").Append((maxFileBytes / (1024L * 1024L)).ToString(CultureInfo.InvariantCulture)).Append('m');
            sb.Append(" -f ").Append(FfmpegTool.QuoteArg(
                "bv*[height<=" + maxHeight + "][ext=mp4]+ba[ext=m4a]/bv*[height<=" + maxHeight + "]+ba/b[height<=" + maxHeight + "]/b"));
            sb.Append(" --merge-output-format mp4");
            if (!string.IsNullOrEmpty(ffmpegDir))
                sb.Append(" --ffmpeg-location ").Append(FfmpegTool.QuoteArg(ffmpegDir));
            sb.Append(" -o ").Append(FfmpegTool.QuoteArg(outputTemplate));
            if (!string.IsNullOrWhiteSpace(cookiesBrowser))
                sb.Append(" --cookies-from-browser ").Append(FfmpegTool.QuoteArg(cookiesBrowser.Trim()));
            sb.Append(" ").Append(FfmpegTool.QuoteArg(url.Trim()));
            return sb.ToString();
        }

        /// <summary>
        /// Download the video at <paramref name="url"/> (capped at DefaultMaxHeight, skipped when
        /// longer than <paramref name="maxSourceSeconds"/> or over <paramref name="maxFileBytes"/>)
        /// as an MP4 in tempCache/aichat_web_videos; the caller cuts the wanted section with
        /// FfmpegTool.CreateClip. <paramref name="onProgressLine"/> receives yt-dlp's stdout /
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
            Action<Result> onDone)
        {
            var result = new Result();
            string exe, toolError;
            if (!TryGetToolPath(out exe, out toolError))
            {
                result.Error = toolError;
                onDone?.Invoke(result);
                yield break;
            }

            string ffmpegDir = null;
            if (FfmpegTool.TryGetToolPaths(out string ffmpegPath, out _, out string ffmpegError))
                ffmpegDir = Path.GetDirectoryName(ffmpegPath);
            else
            {
                result.Error = "yt-dlp section downloads need ffmpeg: " + ffmpegError;
                onDone?.Invoke(result);
                yield break;
            }

            string dir = GetOutputDir();
            string stem = "ytdlp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string template = Path.Combine(dir, stem + ".%(ext)s");

            var cfg = Config.Get();
            string cookies = cfg != null ? cfg.GetYtDlpCookiesBrowser() : "";
            string jsRuntime, jsPath;
            if (!TryDetectJsRuntime(out jsRuntime, out jsPath)) jsRuntime = null;
            string args = BuildArgs(url, template, DefaultMaxHeight, maxSourceSeconds, maxFileBytes, ffmpegDir, cookies, jsRuntime);
            result.Command = FfmpegTool.QuoteArg(exe) + " " + args;
            onCommandBuilt?.Invoke(result.Command);

            var lines = new LineBuffer();
            float started = Time.realtimeSinceStartup;
            Task<FfmpegTool.ProcessResult> task = Task.Run(() =>
                FfmpegTool.RunProcessCancellable(exe, args, DownloadTimeoutMs, cancelToken,
                    line => lines.Add(line),
                    line => lines.Add("stderr: " + line)));

            while (!task.IsCompleted)
            {
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
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "yt-dlp failed.";
                onDone?.Invoke(result);
                yield break;
            }

            FfmpegTool.ProcessResult pr = task.Result;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Cancelled = cancelToken != null && cancelToken.CancelRequested;

            // yt-dlp may pick a different extension than requested; find what it produced.
            string output = FindOutput(dir, stem);
            if (pr.Success && !string.IsNullOrEmpty(output))
            {
                result.Success = true;
                result.OutputPath = output;
            }
            else
            {
                result.Success = false;
                string combined = (pr.Stdout ?? "") + "\n" + (pr.Stderr ?? "");
                if (result.Cancelled)
                    result.Error = "cancelled";
                else if (combined.IndexOf("does not pass filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Error = "skipped: the source video is longer than max_source_minutes (" + (maxSourceSeconds / 60f).ToString("0.#", CultureInfo.InvariantCulture) + " min); raise max_source_minutes or pick a shorter video";
                else if (combined.IndexOf("File is larger than max-filesize", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Error = "skipped: the source file is larger than the " + (maxFileBytes / (1024L * 1024L)) + " MB cap";
                else if (!string.IsNullOrEmpty(pr.Error))
                    result.Error = pr.Error;
                else if (pr.Success)
                    result.Error = "yt-dlp exited 0 but produced no output file";
                else
                    result.Error = "yt-dlp exited " + pr.ExitCode + ": " + LastErrorLine(pr.Stderr, pr.Stdout);
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

        private static string FindOutput(string dir, string stem)
        {
            try
            {
                string best = null;
                long bestSize = -1;
                foreach (string f in Directory.GetFiles(dir, stem + ".*"))
                {
                    string lower = f.ToLowerInvariant();
                    if (lower.EndsWith(".part") || lower.EndsWith(".ytdl") || lower.EndsWith(".temp")) continue;
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

        private static void CleanupPartials(string dir, string stem)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, stem + "*"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
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
