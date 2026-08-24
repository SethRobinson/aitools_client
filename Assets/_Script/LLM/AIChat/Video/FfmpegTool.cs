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
    public static partial class FfmpegTool
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
            public bool HasAudio;
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

        internal sealed class ProcessResult
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

        public static string GetStillFrameOutputPath(string sourcePath)
        {
            string root = GetAppRoot();
            string dir = System.IO.Path.Combine(root, "tempCache", "aichat_video_stills");
            Directory.CreateDirectory(dir);

            string stem = "still";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }

            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N") + ".png");
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

        // Successful probe results are cached per (path, size, mtime) for the session.
        // Movie pics re-probe their file on every reload (the "\" unload-all hotkey,
        // visibility churn), and on a large canvas that used to fan out 100+
        // simultaneous blocking ffprobe Task.Run calls at once: the thread pool
        // starves and movies sit black for minutes waiting for their probe to even
        // start. The files never change once written, so re-probing is pure overhead.
        private static readonly System.Collections.Generic.Dictionary<string, VideoInfo> s_probeCache =
            new System.Collections.Generic.Dictionary<string, VideoInfo>();

        private static string BuildProbeCacheKey(string inputPath)
        {
            try
            {
                var fi = new FileInfo(inputPath);
                if (!fi.Exists) return null;
                return fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetCachedProbe(string inputPath, out VideoInfo info)
        {
            info = null;
            string key = BuildProbeCacheKey(inputPath);
            if (key == null) return false;
            lock (s_probeCache)
            {
                if (!s_probeCache.TryGetValue(key, out VideoInfo cached)) return false;
                info = CloneVideoInfo(cached);
                return true;
            }
        }

        private static void StoreProbeResult(string inputPath, VideoInfo info)
        {
            if (info == null) return;
            string key = BuildProbeCacheKey(inputPath);
            if (key == null) return;
            lock (s_probeCache)
            {
                s_probeCache[key] = CloneVideoInfo(info);
            }
        }

        // Callers get their own copy so nobody can mutate the cached entry.
        private static VideoInfo CloneVideoInfo(VideoInfo src)
        {
            return new VideoInfo
            {
                Path = src.Path,
                DurationSeconds = src.DurationSeconds,
                Width = src.Width,
                Height = src.Height,
                Fps = src.Fps,
                RotationDegrees = src.RotationDegrees,
                CodecName = src.CodecName,
                FormatName = src.FormatName,
                HasVideo = src.HasVideo,
                HasAudio = src.HasAudio
            };
        }

        public static IEnumerator ProbeVideo(string inputPath, Action<VideoInfo, string> onDone)
        {
            if (TryGetCachedProbe(inputPath, out VideoInfo cachedInfo))
            {
                onDone?.Invoke(cachedInfo, null);
                yield break;
            }

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
                StoreProbeResult(inputPath, info);
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

            if (TryGetCachedProbe(inputPath, out info))
                return true;

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
                StoreProbeResult(inputPath, info);
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

        /// <summary>
        /// Extract a single full-resolution still frame from <paramref name="inputPath"/>
        /// at <paramref name="atSeconds"/> as a PNG. Used by AI Chat's "Import still"
        /// button in the video clip chooser. Seeks with -ss before -i for a fast seek,
        /// and does NOT scale (a still should keep the source frame's native quality).
        /// </summary>
        public static IEnumerator ExtractStillFrame(
            string inputPath,
            float atSeconds,
            string outputPath,
            Action<ClipResult> onDone)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = GetStillFrameOutputPath(inputPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));

            if (float.IsNaN(atSeconds) || float.IsInfinity(atSeconds))
                atSeconds = 0f;
            atSeconds = Mathf.Max(0f, atSeconds);
            string ssStr = atSeconds.ToString("0.###", CultureInfo.InvariantCulture);

            string args = "-hide_banner -y"
                + " -ss " + ssStr
                + " -i " + QuoteArg(inputPath)
                + " -an -frames:v 1 -q:v 2 "
                + QuoteArg(outputPath);

            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, FrameExtractTimeoutMs));
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
            UnityEngine.Debug.Log("ffmpeg still frame: " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }

        /// <summary>
        /// Extract a clip's audio as 16 kHz mono PCM WAV (what speech-to-text wants). Fails
        /// when the source has no audio stream.
        /// </summary>
        // ---------- Stitching several clips into one video (AI Chat stitch_video) ----------

        /// <summary>
        /// Upper bound on clips per stitch. Every input adds a few hundred characters of
        /// filter graph to the ffmpeg command line and Windows caps a command line at
        /// 32K characters, so this stays well inside that.
        /// </summary>
        public const int MaxStitchClips = 60;

        /// <summary>
        /// Inputs and output settings for <see cref="StitchClips"/>. Unset canvas / fps
        /// fields are filled from the inputs by <see cref="ResolveStitchDefaults"/>.
        /// </summary>
        public sealed class StitchRequest
        {
            /// <summary>Probed source clips, in playback order.</summary>
            public System.Collections.Generic.List<VideoInfo> Inputs = new System.Collections.Generic.List<VideoInfo>();
            /// <summary>Output canvas; 0 = the size most inputs share (first clip on ties). Every clip is fit inside it and letterboxed.</summary>
            public int Width;
            public int Height;
            /// <summary>Output frame rate; 0 = the highest input fps so no clip drops frames.</summary>
            public double Fps;
            /// <summary>Keep audio. Silent clips get a synthesized silent track so the join stays in sync.</summary>
            public bool IncludeAudio = true;
            /// <summary>Seconds of dissolve between consecutive clips; 0 = hard cuts. Each junction shortens the film by this much.</summary>
            public float CrossfadeSeconds;
        }

        public static string GetStitchOutputPath()
        {
            string root = GetAppRoot();
            string dir = System.IO.Path.Combine(root, "tempCache", "aichat_video_clips");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "stitch_" + Guid.NewGuid().ToString("N") + ".mp4");
        }

        /// <summary>
        /// Displayed (rotation-applied) size of a probed clip. ffmpeg auto-rotates on
        /// decode, so the stitch canvas has to be chosen from these, not the raw stream size.
        /// </summary>
        public static void GetDisplayedSize(VideoInfo info, out int width, out int height)
        {
            width = info != null ? info.Width : 0;
            height = info != null ? info.Height : 0;
            int rot = info != null ? ((info.RotationDegrees % 360) + 360) % 360 : 0;
            if (rot == 90 || rot == 270)
            {
                int t = width;
                width = height;
                height = t;
            }
        }

        /// <summary>
        /// Fill the unset canvas / fps of a stitch request from its inputs and clamp the
        /// crossfade so it fits inside the shortest clip. Explicit values are kept
        /// (snapped to even numbers, which yuv420p needs).
        /// </summary>
        public static void ResolveStitchDefaults(StitchRequest req)
        {
            if (req == null || req.Inputs == null) return;

            if (req.Width <= 0 || req.Height <= 0)
            {
                // Majority size wins; a strict ">" keeps the FIRST clip's size on ties.
                var counts = new System.Collections.Generic.Dictionary<long, int>();
                long bestKey = 0;
                int bestCount = 0;
                foreach (var info in req.Inputs)
                {
                    GetDisplayedSize(info, out int w, out int h);
                    if (w <= 0 || h <= 0) continue;
                    long key = ((long)w << 32) | (uint)h;
                    counts.TryGetValue(key, out int c);
                    c++;
                    counts[key] = c;
                    if (c > bestCount)
                    {
                        bestCount = c;
                        bestKey = key;
                    }
                }
                if (bestCount > 0)
                {
                    req.Width = (int)(bestKey >> 32);
                    req.Height = (int)(bestKey & 0xffffffff);
                }
                else
                {
                    req.Width = DefaultMaxWidth;
                    req.Height = DefaultMaxHeight;
                }
            }
            req.Width = Mathf.Max(2, req.Width / 2 * 2);
            req.Height = Mathf.Max(2, req.Height / 2 * 2);

            if (req.Fps <= 0 || double.IsNaN(req.Fps) || double.IsInfinity(req.Fps))
            {
                double maxFps = 0;
                foreach (var info in req.Inputs)
                    if (info != null && info.Fps > maxFps) maxFps = info.Fps;
                req.Fps = maxFps > 0 ? maxFps : DefaultFps;
            }
            req.Fps = Math.Max(1, Math.Min(120, req.Fps));

            if (req.CrossfadeSeconds > 0)
            {
                // xfade offsets are computed from every clip's duration, so an unknown
                // duration anywhere means hard cuts; otherwise keep the fade inside half
                // of the shortest clip so no clip dissolves away entirely.
                double minDur = double.MaxValue;
                bool unknown = false;
                foreach (var info in req.Inputs)
                {
                    if (info == null || info.DurationSeconds <= 0)
                    {
                        unknown = true;
                        break;
                    }
                    minDur = Math.Min(minDur, info.DurationSeconds);
                }
                if (unknown)
                    req.CrossfadeSeconds = 0;
                else
                    req.CrossfadeSeconds = (float)Math.Min(req.CrossfadeSeconds, Math.Max(0, minDur / 2 - 0.05));
                if (req.CrossfadeSeconds < 0.05f)
                    req.CrossfadeSeconds = 0;
            }
        }

        /// <summary>
        /// Join <c>req.Inputs</c> back to back into one H.264/AAC MP4. Every clip is
        /// scaled to fit the canvas and letterboxed, resampled to one fps, and given a
        /// stereo 48k audio track (synthesized silence for silent clips), then joined
        /// with the <c>concat</c> filter, or with <c>xfade</c>/<c>acrossfade</c> chains when
        /// <c>CrossfadeSeconds</c> is set. Runs ffmpeg on a worker thread; yields until done.
        /// </summary>
        public static IEnumerator StitchClips(StitchRequest req, string outputPath, Action<ClipResult> onDone)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }
            if (req == null || req.Inputs == null || req.Inputs.Count < 2)
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = "Stitching needs at least two clips." });
                yield break;
            }
            if (req.Inputs.Count > MaxStitchClips)
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = "Stitching supports at most " + MaxStitchClips + " clips per call." });
                yield break;
            }
            for (int i = 0; i < req.Inputs.Count; i++)
            {
                var info = req.Inputs[i];
                if (info == null || string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
                {
                    onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = "Stitch input " + (i + 1) + " is missing on disk." });
                    yield break;
                }
            }

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = GetStitchOutputPath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
            ResolveStitchDefaults(req);

            double totalSeconds = 0;
            foreach (var info in req.Inputs)
                totalSeconds += Math.Max(0, info.DurationSeconds);
            int timeoutMs = (int)Math.Min(PreviewProxyTimeoutMs, Math.Max(ClipTimeoutMs, totalSeconds * 3000 + 60000));

            string args = BuildStitchArgs(req, outputPath);
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, timeoutMs));
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
            UnityEngine.Debug.Log("ffmpeg (stitch): " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }

        private static string BuildStitchArgs(StitchRequest req, string outputPath)
        {
            var ci = CultureInfo.InvariantCulture;
            int n = req.Inputs.Count;
            string fpsStr = req.Fps.ToString("0.###", ci);
            // settb=AVTB: xfade refuses inputs whose time bases differ, and fps= leaves
            // each clip on its own 1/fps base. Harmless for the concat path.
            string videoNorm =
                "scale=" + req.Width + ":" + req.Height + ":force_original_aspect_ratio=decrease:flags=bicubic," +
                "pad=" + req.Width + ":" + req.Height + ":(ow-iw)/2:(oh-ih)/2:color=black," +
                "setsar=1,fps=" + fpsStr + ",format=yuv420p,setpts=PTS-STARTPTS,settb=AVTB";
            const string audioNorm = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS";
            bool crossfade = req.CrossfadeSeconds > 0;
            string fadeStr = req.CrossfadeSeconds.ToString("0.###", ci);

            var inputs = new StringBuilder();
            var graph = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                inputs.Append(" -i ").Append(QuoteArg(req.Inputs[i].Path));
                graph.Append('[').Append(i).Append(":v:0]").Append(videoNorm).Append("[v").Append(i).Append("];");
                if (!req.IncludeAudio) continue;

                if (req.Inputs[i].HasAudio)
                {
                    graph.Append('[').Append(i).Append(":a:0]").Append(audioNorm).Append("[a").Append(i).Append("];");
                }
                else
                {
                    // Silent clip: synthesize a matching silent track. For hard cuts keep
                    // it a hair SHORTER than the video - concat pads short audio with
                    // silence but never pads video, so the video must be the long stream.
                    double d = req.Inputs[i].DurationSeconds;
                    if (d <= 0) d = DefaultClipDurationSeconds;
                    if (!crossfade) d = Math.Max(0.1, d - 0.05);
                    graph.Append("anullsrc=channel_layout=stereo:sample_rate=48000:d=")
                         .Append(d.ToString("0.###", ci)).Append("[a").Append(i).Append("];");
                }
            }

            if (!crossfade)
            {
                for (int i = 0; i < n; i++)
                {
                    graph.Append("[v").Append(i).Append(']');
                    if (req.IncludeAudio) graph.Append("[a").Append(i).Append(']');
                }
                graph.Append("concat=n=").Append(n).Append(":v=1:a=").Append(req.IncludeAudio ? 1 : 0).Append("[v]");
                if (req.IncludeAudio) graph.Append("[a]");
            }
            else
            {
                // Each transition starts CrossfadeSeconds before the running end of the
                // film so far: offset_k = sum(duration_0..k-1) - k * fade.
                double offset = 0;
                string prevV = "[v0]";
                for (int i = 1; i < n; i++)
                {
                    offset += req.Inputs[i - 1].DurationSeconds - req.CrossfadeSeconds;
                    string outV = i == n - 1 ? "[v]" : "[vx" + i + "]";
                    graph.Append(prevV).Append("[v").Append(i).Append("]xfade=transition=fade:duration=").Append(fadeStr)
                         .Append(":offset=").Append(Math.Max(0, offset).ToString("0.###", ci)).Append(outV).Append(';');
                    prevV = outV;
                }
                if (req.IncludeAudio)
                {
                    string prevA = "[a0]";
                    for (int i = 1; i < n; i++)
                    {
                        string outA = i == n - 1 ? "[a]" : "[ax" + i + "]";
                        graph.Append(prevA).Append("[a").Append(i).Append("]acrossfade=d=").Append(fadeStr)
                             .Append(":c1=tri:c2=tri").Append(outA).Append(';');
                        prevA = outA;
                    }
                }
                if (graph.Length > 0 && graph[graph.Length - 1] == ';')
                    graph.Length--;
            }

            return "-hide_banner -y" + inputs
                + " -filter_complex " + QuoteArg(graph.ToString())
                + " -map \"[v]\"" + (req.IncludeAudio ? " -map \"[a]\"" : " -an")
                + " -c:v libx264 -preset veryfast -crf 18"
                + (req.IncludeAudio ? " -c:a aac -b:a 160k" : "")
                + " -movflags +faststart "
                + QuoteArg(outputPath);
        }

        public static IEnumerator ExtractAudioWav(string inputPath, string outputPath, Action<ClipResult> onDone)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
            string args = "-hide_banner -y -i " + QuoteArg(inputPath) + " -vn -map 0:a:0 -ac 1 -ar 16000 -c:a pcm_s16le -f wav " + QuoteArg(outputPath);
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, 60000));
            while (!task.IsCompleted)
                yield return null;
            var result = new ClipResult { OutputPath = outputPath };
            if (task.IsFaulted)
            {
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "ffmpeg failed.";
                onDone?.Invoke(result);
                yield break;
            }
            ProcessResult pr = task.Result;
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success) result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }

        /// <summary>
        /// Mean volume in dBFS via ffmpeg's volumedetect filter (float.NaN when it could not be
        /// measured). Around -90 dB = digital silence; speech or music sit well above -40 dB.
        /// </summary>
        public static IEnumerator MeasureMeanVolume(string inputPath, Action<float> onDone)
        {
            float mean = float.NaN;
            if (!TryGetToolPaths(out string ffmpegPath, out _, out _))
            {
                onDone?.Invoke(mean);
                yield break;
            }
            string args = "-hide_banner -i " + QuoteArg(inputPath) + " -af volumedetect -vn -f null -";
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, 60000));
            while (!task.IsCompleted)
                yield return null;
            if (!task.IsFaulted && task.Result != null)
            {
                string text = (task.Result.Stderr ?? "") + "\n" + (task.Result.Stdout ?? "");
                int i = text.IndexOf("mean_volume:", StringComparison.Ordinal);
                if (i >= 0)
                {
                    int end = text.IndexOf("dB", i, StringComparison.Ordinal);
                    string num = end > i ? text.Substring(i + "mean_volume:".Length, end - i - "mean_volume:".Length).Trim() : "";
                    float v;
                    if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mean = v;
                }
            }
            onDone?.Invoke(mean);
        }

        /// <summary>
        /// Convert any ffmpeg-decodable still image (webp, gif frame 1, avif, bmp, tiff, odd
        /// PNG/JPEG variants Unity refuses) to a PNG Unity's Texture2D.LoadImage can read,
        /// downscaling so the longest side is at most <paramref name="maxSide"/> (0 = no limit).
        /// Used by AI Chat web_image downloads.
        /// </summary>
        public static IEnumerator ConvertImageToPng(
            string inputPath,
            string outputPath,
            int maxSide,
            Action<ClipResult> onDone)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));

            string vf = maxSide > 0
                ? " -vf " + QuoteArg("scale='min(" + maxSide + ",iw)':'min(" + maxSide + ",ih)':force_original_aspect_ratio=decrease")
                : "";
            string args = "-hide_banner -y"
                + " -i " + QuoteArg(inputPath)
                + " -an -frames:v 1"
                + vf
                + " -pix_fmt rgba "
                + QuoteArg(outputPath);

            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, 60000));
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

        /// <summary>
        /// Generic cancellable runner shared with other external helpers (yt-dlp): streams
        /// stdout lines to <paramref name="onStdoutLine"/> from the reader thread (callers
        /// must marshal to the main thread themselves, e.g. by buffering and polling), kills
        /// the process on cancel or timeout. Blocking; run it inside Task.Run.
        /// </summary>
        internal static ProcessResult RunProcessCancellable(string exe, string args, int timeoutMs, CancelToken cancelToken, Action<string> onStdoutLine, Action<string> onStderrLine = null)
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
                        try { onStdoutLine?.Invoke(e.Data); } catch { }
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        stderr.AppendLine(e.Data);
                        try { onStderrLine?.Invoke(e.Data); } catch { }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var sw = Stopwatch.StartNew();
                    bool exited = false;
                    while (!(exited = process.WaitForExit(100)))
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

                    if (exited)
                    {
                        // Let the async readers flush.
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
                    if (stream != null && stream["codec_type"] != null && stream["codec_type"].Value == "audio")
                        info.HasAudio = true;
                    if (stream == null || stream["codec_type"] == null || stream["codec_type"].Value != "video")
                        continue;
                    if (info.HasVideo)
                        continue; // first video stream wins; keep scanning so audio streams are still seen

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

        internal static string BuildProcessError(string toolName, ProcessResult pr)
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

        internal static string GetAppRoot()
        {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath.Replace('/', '\\'), ".."));
        }

        internal static string QuoteArg(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        internal static string SanitizeFileStem(string s)
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
