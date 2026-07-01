using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;
using UnityEngine;

namespace AITools.AIChat.Video
{
    /// <summary>
    /// Windows-only FFmpeg/ffprobe wrapper for AI Chat video import. FFmpeg stays an
    /// external helper under utils/ffmpeg/bin so Unity never links against it.
    /// </summary>
    public static class FfmpegTool
    {
        public const float DefaultClipDurationSeconds = 5f;
        public const int DefaultFps = 16;
        public const int DefaultMaxWidth = 832;
        public const int DefaultMaxHeight = 480;

        private const int ProbeTimeoutMs = 15000;
        private const int FrameExtractTimeoutMs = 120000;
        private const int ClipTimeoutMs = 10 * 60 * 1000;
        private const int PreviewProxyTimeoutMs = 30 * 60 * 1000;

        public sealed class VideoInfo
        {
            public string Path;
            public double DurationSeconds;
            public int Width;
            public int Height;
            public double Fps;
            public int RotationDegrees;
            public string CodecName;
            public string FormatName;
            public bool HasVideo;
        }

        public sealed class ClipResult
        {
            public bool Success;
            public string OutputPath;
            public string Error;
            public string Command;
            public string Stdout;
            public string Stderr;
            public int ExitCode;
        }

        public sealed class ContactSheetResult
        {
            public bool Success;
            public string OutputPath;
            public string Error;
            public string Command;
            public string Stdout;
            public string Stderr;
            public int ExitCode;
        }

        public sealed class CancelToken
        {
            public volatile bool CancelRequested;
            public void Cancel() { CancelRequested = true; }
        }

        private sealed class ProgressState
        {
            private readonly object _lock = new object();
            private float _progress;
            private string _message;

            public void Set(float progress, string message = null)
            {
                lock (_lock)
                {
                    _progress = Mathf.Clamp01(progress);
                    if (!string.IsNullOrWhiteSpace(message))
                        _message = message;
                }
            }

            public void Snapshot(out float progress, out string message)
            {
                lock (_lock)
                {
                    progress = _progress;
                    message = _message;
                }
            }
        }

        private sealed class ProcessResult
        {
            public bool Success;
            public int ExitCode;
            public string Stdout;
            public string Stderr;
            public string Error;
            public string Command;
        }

        public static bool IsSupportedVideoExtension(string pathOrExt)
        {
            if (string.IsNullOrWhiteSpace(pathOrExt)) return false;
            string ext = pathOrExt.StartsWith(".")
                ? pathOrExt.Trim().ToLowerInvariant()
                : System.IO.Path.GetExtension(pathOrExt).ToLowerInvariant();
            return ext == ".mov" || ext == ".mp4" || ext == ".avi";
        }

        public static bool ShouldUseUnityPreviewProxy(VideoInfo info)
        {
            if (info == null) return false;

            string codec = (info.CodecName ?? string.Empty).Trim().ToLowerInvariant();
            return codec == "hevc"
                || codec == "h265"
                || codec == "av1"
                || codec == "vp9";
        }

        public static bool TryGetToolPaths(out string ffmpegPath, out string ffprobePath, out string error)
        {
            ffmpegPath = null;
            ffprobePath = null;
            error = null;

            string root = GetAppRoot();
            if (string.IsNullOrEmpty(root))
            {
                error = "Could not resolve app root for FFmpeg.";
                return false;
            }

            string bin = System.IO.Path.Combine(root, "utils", "ffmpeg", "bin");
            ffmpegPath = System.IO.Path.Combine(bin, "ffmpeg.exe");
            ffprobePath = System.IO.Path.Combine(bin, "ffprobe.exe");

            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                error = "FFmpeg binaries were not found. Expected:\n"
                    + ffmpegPath + "\n"
                    + ffprobePath;
                return false;
            }
            return true;
        }

        public static string GetClipOutputPath(string sourcePath)
        {
            string root = GetAppRoot();
            string dir = System.IO.Path.Combine(root, "tempCache", "aichat_video_clips");
            Directory.CreateDirectory(dir);

            string stem = "clip";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }

            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N") + ".mp4");
        }

        public static string GetPreviewProxyOutputPath(string sourcePath)
        {
            string root = GetAppRoot();
            string dir = System.IO.Path.Combine(root, "tempCache", "aichat_video_preview_proxies");
            Directory.CreateDirectory(dir);

            string stem = "preview";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }

            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N") + "_preview.mp4");
        }

        public static IEnumerator ProbeVideo(string inputPath, Action<VideoInfo, string> onDone)
        {
            if (!TryGetToolPaths(out _, out string ffprobePath, out string toolError))
            {
                onDone?.Invoke(null, toolError);
                yield break;
            }

            string args = "-v error -print_format json -show_format -show_streams " + QuoteArg(inputPath);
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffprobePath, args, ProbeTimeoutMs));
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                onDone?.Invoke(null, task.Exception != null ? task.Exception.GetBaseException().Message : "ffprobe failed.");
                yield break;
            }

            ProcessResult pr = task.Result;
            UnityEngine.Debug.Log("ffprobe: " + pr.Command + "\n" + pr.Stderr);
            if (!pr.Success)
            {
                onDone?.Invoke(null, BuildProcessError("ffprobe", pr));
                yield break;
            }

            try
            {
                VideoInfo info = ParseProbeJson(inputPath, pr.Stdout);
                if (info == null || !info.HasVideo)
                {
                    onDone?.Invoke(null, "ffprobe did not find a video stream in " + inputPath);
                    yield break;
                }
                onDone?.Invoke(info, null);
            }
            catch (Exception ex)
            {
                onDone?.Invoke(null, "Could not parse ffprobe output: " + ex.Message);
            }
        }

        public static bool TryProbeVideoSync(string inputPath, out VideoInfo info, out string error)
        {
            info = null;
            error = null;

            if (!TryGetToolPaths(out _, out string ffprobePath, out string toolError))
            {
                error = toolError;
                return false;
            }

            string args = "-v error -print_format json -show_format -show_streams " + QuoteArg(inputPath);
            ProcessResult pr = RunProcess(ffprobePath, args, ProbeTimeoutMs);
            UnityEngine.Debug.Log("ffprobe sync: " + pr.Command + "\n" + pr.Stderr);
            if (!pr.Success)
            {
                error = BuildProcessError("ffprobe", pr);
                return false;
            }

            try
            {
                info = ParseProbeJson(inputPath, pr.Stdout);
                if (info == null || !info.HasVideo)
                {
                    error = "ffprobe did not find a video stream in " + inputPath;
                    info = null;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not parse ffprobe output: " + ex.Message;
                info = null;
                return false;
            }
        }

        public static IEnumerator CreateClip(
            string inputPath,
            float startSeconds,
            float durationSeconds,
            string outputPath,
            Action<ClipResult> onDone,
            double fps = 0,
            int maxWidth = DefaultMaxWidth,
            int maxHeight = DefaultMaxHeight,
            bool includeAudio = true)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = GetClipOutputPath(inputPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));

            startSeconds = Mathf.Max(0f, startSeconds);
            durationSeconds = Mathf.Clamp(durationSeconds <= 0f ? DefaultClipDurationSeconds : durationSeconds, 0.1f, 60f);
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
                fps = DefaultFps;
            fps = Math.Max(1, Math.Min(120, fps));
            maxWidth = Mathf.Max(2, maxWidth);
            maxHeight = Mathf.Max(2, maxHeight);

            string args = BuildClipArgs(inputPath, outputPath, startSeconds, durationSeconds, fps, maxWidth, maxHeight, includeAudio);
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, ClipTimeoutMs));
            while (!task.IsCompleted)
                yield return null;

            ClipResult result = new ClipResult { OutputPath = outputPath };
            if (task.IsFaulted)
            {
                result.Success = false;
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "ffmpeg failed.";
                onDone?.Invoke(result);
                yield break;
            }

            ProcessResult pr = task.Result;
            UnityEngine.Debug.Log("ffmpeg: " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }

        public static IEnumerator CreateCaptionContactSheet(
            string inputPath,
            double durationSeconds,
            Action<ContactSheetResult> onDone,
            int maxFrames = 6,
            int cellMaxWidth = 256,
            int cellMaxHeight = 256)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ContactSheetResult { Success = false, Error = toolError });
                yield break;
            }

            string root = GetAppRoot();
            string dir = System.IO.Path.Combine(root, "tempCache", "aichat_video_captions");
            Directory.CreateDirectory(dir);
            string stem = "video";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(inputPath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }

            string outputPath = System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N") + "_sheet.png");
            maxFrames = Mathf.Clamp(maxFrames <= 0 ? 6 : maxFrames, 2, 12);
            cellMaxWidth = Mathf.Clamp(cellMaxWidth, 64, 512);
            cellMaxHeight = Mathf.Clamp(cellMaxHeight, 64, 512);

            double safeDuration = durationSeconds > 0 && !double.IsNaN(durationSeconds) && !double.IsInfinity(durationSeconds)
                ? durationSeconds
                : 5.0;
            double sampleFps = Math.Max(0.2, Math.Min(2.0, maxFrames / Math.Max(0.1, safeDuration)));
            int cols = maxFrames <= 4 ? 2 : 3;
            int rows = Mathf.CeilToInt(maxFrames / (float)cols);

            string fpsStr = sampleFps.ToString("0.###", CultureInfo.InvariantCulture);
            string filter =
                "fps=" + fpsStr + "," +
                "scale=max(2\\,trunc(iw*min(1\\,min(" + cellMaxWidth + "/iw\\," + cellMaxHeight + "/ih))/2)*2):" +
                "max(2\\,trunc(ih*min(1\\,min(" + cellMaxWidth + "/iw\\," + cellMaxHeight + "/ih))/2)*2)," +
                "setsar=1,tile=" + cols + "x" + rows + ":padding=4:margin=4:color=black";

            string args = "-hide_banner -y"
                + " -i " + QuoteArg(inputPath)
                + " -an -vf " + QuoteArg(filter)
                + " -frames:v 1 "
                + QuoteArg(outputPath);

            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, FrameExtractTimeoutMs));
            while (!task.IsCompleted)
                yield return null;

            ContactSheetResult result = new ContactSheetResult { OutputPath = outputPath };
            if (task.IsFaulted)
            {
                result.Success = false;
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "ffmpeg failed.";
                onDone?.Invoke(result);
                yield break;
            }

            ProcessResult pr = task.Result;
            UnityEngine.Debug.Log("ffmpeg video caption sheet: " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }

        public static IEnumerator CreatePreviewProxy(
            string inputPath,
            double durationSeconds,
            double fps,
            Action<ClipResult> onDone,
            Action<float, string> onProgress = null,
            CancelToken cancelToken = null,
            int maxWidth = 1280,
            int maxHeight = 720,
            bool includeAudio = false)
        {
            string outputPath = GetPreviewProxyOutputPath(inputPath);
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }

            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
                fps = 30;
            fps = Math.Max(1, Math.Min(30, fps));
            maxWidth = Mathf.Max(2, maxWidth);
            maxHeight = Mathf.Max(2, maxHeight);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
            string args = BuildPreviewProxyArgs(inputPath, outputPath, fps, maxWidth, maxHeight, includeAudio);
            var progressState = new ProgressState();
            progressState.Set(0f, "Converting preview...");

            Task<ProcessResult> task = Task.Run(() =>
                RunProcessWithProgress(ffmpegPath, args, PreviewProxyTimeoutMs, durationSeconds, progressState, cancelToken));

            while (!task.IsCompleted)
            {
                progressState.Snapshot(out float p, out string msg);
                onProgress?.Invoke(p, string.IsNullOrWhiteSpace(msg) ? "Converting preview..." : msg);
                yield return null;
            }

            ClipResult result = new ClipResult { OutputPath = outputPath };
            if (task.IsFaulted)
            {
                result.Success = false;
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "ffmpeg failed.";
                onDone?.Invoke(result);
                yield break;
            }

            ProcessResult pr = task.Result;
            UnityEngine.Debug.Log("ffmpeg preview proxy: " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            else
                onProgress?.Invoke(1f, "Preview ready");
            onDone?.Invoke(result);
        }

        private static string BuildClipArgs(string inputPath, string outputPath, float start, float duration, double fps, int maxWidth, int maxHeight, bool includeAudio)
        {
            string startStr = start.ToString("0.###", CultureInfo.InvariantCulture);
            string durStr = duration.ToString("0.###", CultureInfo.InvariantCulture);
            string fpsStr = fps.ToString("0.###", CultureInfo.InvariantCulture);
            string filter =
                "fps=" + fpsStr + "," +
                "scale=max(2\\,trunc(iw*min(1\\,min(" + maxWidth + "/iw\\," + maxHeight + "/ih))/2)*2):" +
                "max(2\\,trunc(ih*min(1\\,min(" + maxWidth + "/iw\\," + maxHeight + "/ih))/2)*2)," +
                "setsar=1,format=yuv420p";

            return "-hide_banner -y"
                + " -ss " + startStr
                + " -t " + durStr
                + " -i " + QuoteArg(inputPath)
                + " -map 0:v:0"
                + (includeAudio ? " -map 0:a:0?" : " -an")
                + " -vf " + QuoteArg(filter)
                + " -c:v libx264 -preset veryfast -crf 18"
                + (includeAudio ? " -c:a aac -b:a 160k -shortest " : " ")
                + QuoteArg(outputPath);
        }

        private static string BuildPreviewProxyArgs(string inputPath, string outputPath, double fps, int maxWidth, int maxHeight, bool includeAudio)
        {
            string fpsStr = fps.ToString("0.###", CultureInfo.InvariantCulture);
            string filter =
                "fps=" + fpsStr + "," +
                "scale=max(2\\,trunc(iw*min(1\\,min(" + maxWidth + "/iw\\," + maxHeight + "/ih))/2)*2):" +
                "max(2\\,trunc(ih*min(1\\,min(" + maxWidth + "/iw\\," + maxHeight + "/ih))/2)*2)," +
                "setsar=1,format=yuv420p";

            return "-hide_banner -y -progress pipe:1 -nostats"
                + " -i " + QuoteArg(inputPath)
                + " -map 0:v:0"
                + (includeAudio ? " -map 0:a:0?" : " -an")
                + " -vf " + QuoteArg(filter)
                + " -c:v libx264 -preset veryfast -crf 23 -movflags +faststart"
                + (includeAudio ? " -c:a aac -b:a 160k -shortest" : "")
                + " "
                + QuoteArg(outputPath);
        }

        private static ProcessResult RunProcess(string exe, string args, int timeoutMs)
        {
            var result = new ProcessResult { Command = QuoteArg(exe) + " " + args };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        result.Success = false;
                        result.ExitCode = -1;
                        result.Error = "Timed out after " + (timeoutMs / 1000) + " seconds.";
                    }
                    else
                    {
                        result.ExitCode = process.ExitCode;
                        result.Success = process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -1;
                result.Error = ex.Message;
            }

            result.Stdout = stdout.ToString();
            result.Stderr = stderr.ToString();
            return result;
        }

        private static ProcessResult RunProcessWithProgress(string exe, string args, int timeoutMs, double durationSeconds, ProgressState progress, CancelToken cancelToken)
        {
            var result = new ProcessResult { Command = QuoteArg(exe) + " " + args };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        stdout.AppendLine(e.Data);
                        ParseProgressLine(e.Data, durationSeconds, progress);
                    };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var sw = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        if (cancelToken != null && cancelToken.CancelRequested)
                        {
                            try { process.Kill(); } catch { }
                            result.Success = false;
                            result.ExitCode = -1;
                            result.Error = "Cancelled.";
                            break;
                        }

                        if (sw.ElapsedMilliseconds > timeoutMs)
                        {
                            try { process.Kill(); } catch { }
                            result.Success = false;
                            result.ExitCode = -1;
                            result.Error = "Timed out after " + (timeoutMs / 1000) + " seconds.";
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(result.Error))
                    {
                        process.WaitForExit();
                        result.ExitCode = process.ExitCode;
                        result.Success = process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -1;
                result.Error = ex.Message;
            }

            result.Stdout = stdout.ToString();
            result.Stderr = stderr.ToString();
            return result;
        }

        private static void ParseProgressLine(string line, double durationSeconds, ProgressState progress)
        {
            if (progress == null || string.IsNullOrWhiteSpace(line)) return;

            string[] parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) return;

            string key = parts[0].Trim();
            string value = parts[1].Trim();
            if (string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(value, "end", StringComparison.OrdinalIgnoreCase))
                    progress.Set(1f, "Finalizing preview...");
                return;
            }

            double seconds = -1;
            if (string.Equals(key, "out_time_us", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "out_time_ms", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double micros))
                    seconds = micros / 1000000.0;
            }
            else if (string.Equals(key, "out_time", StringComparison.OrdinalIgnoreCase))
            {
                if (TimeSpan.TryParse(value, out TimeSpan ts))
                    seconds = ts.TotalSeconds;
            }

            if (seconds >= 0 && durationSeconds > 0)
            {
                float p = Mathf.Clamp01((float)(seconds / durationSeconds));
                progress.Set(p, "Converting preview... " + Mathf.RoundToInt(p * 100f) + "%");
            }
        }

        private static VideoInfo ParseProbeJson(string inputPath, string jsonText)
        {
            JSONNode root = JSON.Parse(jsonText);
            if (root == null) return null;

            var info = new VideoInfo { Path = inputPath };
            JSONNode format = root["format"];
            if (format != null)
            {
                info.FormatName = format["format_name"];
                info.DurationSeconds = ParseDouble(format["duration"]);
            }

            JSONArray streams = root["streams"] != null ? root["streams"].AsArray : null;
            if (streams != null)
            {
                foreach (JSONNode stream in streams)
                {
                    if (stream == null || stream["codec_type"] == null || stream["codec_type"].Value != "video")
                        continue;

                    info.HasVideo = true;
                    info.Width = stream["width"].AsInt;
                    info.Height = stream["height"].AsInt;
                    info.CodecName = stream["codec_name"];
                    if (info.DurationSeconds <= 0)
                        info.DurationSeconds = ParseDouble(stream["duration"]);
                    info.Fps = ParseRational(stream["avg_frame_rate"]);
                    if (info.Fps <= 0) info.Fps = ParseRational(stream["r_frame_rate"]);

                    JSONNode tags = stream["tags"];
                    if (tags != null && tags["rotate"] != null)
                        info.RotationDegrees = Mathf.RoundToInt((float)ParseDouble(tags["rotate"]));
                    JSONNode sideData = stream["side_data_list"];
                    if (sideData != null && sideData.IsArray)
                    {
                        foreach (JSONNode side in sideData.AsArray)
                        {
                            if (side != null && side["rotation"] != null)
                                info.RotationDegrees = Mathf.RoundToInt((float)ParseDouble(side["rotation"]));
                        }
                    }
                    break;
                }
            }

            return info;
        }

        private static double ParseRational(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            string[] parts = value.Split('/');
            if (parts.Length == 2)
            {
                double num = ParseDouble(parts[0]);
                double den = ParseDouble(parts[1]);
                return den == 0 ? 0 : num / den;
            }
            return ParseDouble(value);
        }

        private static double ParseDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
            return d;
        }

        private static string BuildProcessError(string toolName, ProcessResult pr)
        {
            var sb = new StringBuilder();
            sb.Append(toolName).Append(" failed");
            if (pr != null)
            {
                if (!string.IsNullOrEmpty(pr.Error))
                    sb.Append(": ").Append(pr.Error);
                if (pr.ExitCode != 0)
                    sb.Append(" (exit ").Append(pr.ExitCode).Append(")");
                string detail = !string.IsNullOrWhiteSpace(pr.Stderr) ? pr.Stderr.Trim() : pr.Stdout?.Trim();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    if (detail.Length > 1200) detail = detail.Substring(0, 1200) + "...";
                    sb.Append("\n").Append(detail);
                }
            }
            return sb.ToString();
        }

        private static string GetAppRoot()
        {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath.Replace('/', '\\'), ".."));
        }

        private static string QuoteArg(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static string SanitizeFileStem(string s)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                bool bad = false;
                for (int i = 0; i < invalid.Length; i++)
                {
                    if (c == invalid[i]) { bad = true; break; }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
