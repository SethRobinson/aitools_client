using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;
using UnityEngine;

namespace AITools.AIChat.Video
{
    /// <summary>
    /// Audio half of <see cref="FfmpegTool"/>: probing sound files, rendering the waveform
    /// preview video that lets an Audio bubble ride the normal Movie player, cutting a
    /// voice-clone sample, and mixing / replacing a video's soundtrack (AI Chat
    /// <c>set_video_audio</c>). See docs/audio_generation.md.
    /// </summary>
    public static partial class FfmpegTool
    {
        public const int AudioPreviewWidth = 640;
        public const int AudioPreviewHeight = 160;
        public const string AudioColorMusic = "0x4fc3f7";
        public const string AudioColorSfx = "0xffb74d";
        public const string AudioColorSpeech = "0x81c784";
        public const string AudioColorUser = "0xb39ddb";

        private const int AudioProbeTimeoutMs = 15000;
        private const int AudioPreviewTimeoutMs = 5 * 60 * 1000;
        private const int AudioMuxTimeoutMs = 10 * 60 * 1000;
        private const int AudioSectionTimeoutMs = 60 * 1000;

        public sealed class AudioInfo
        {
            public string Path;
            public double DurationSeconds;
            public int SampleRate;
            public int Channels;
            public string CodecName;
            public bool HasAudio;
            public bool HasVideo;
        }

        public enum AudioMuxMode
        {
            /// <summary>Keep the video's own soundtrack and layer the new audio over it.</summary>
            Mix,
            /// <summary>Drop the video's soundtrack; the new audio is the only track.</summary>
            Replace
        }

        public sealed class MuxAudioRequest
        {
            public string VideoPath;
            public string AudioPath;
            public double VideoDurationSeconds;
            public double AudioDurationSeconds;
            public bool VideoHasAudio;
            public AudioMuxMode Mode = AudioMuxMode.Mix;
            public float AudioVolume = 1f;
            public float OriginalVolume = 1f;
            /// <summary>Seconds into the VIDEO where the new audio starts.</summary>
            public float StartSeconds = 0f;
            /// <summary>Repeat audio shorter than the video instead of leaving silence.</summary>
            public bool Loop;
            public float FadeInSeconds = 0f;
            /// <summary>Negative = automatic: 1 s when the audio is cut off by the end of the video, else none.</summary>
            public float FadeOutSeconds = -1f;

            public bool EffectiveMix => Mode == AudioMuxMode.Mix && VideoHasAudio;

            public float EffectiveFadeOutSeconds
            {
                get
                {
                    float fade = FadeOutSeconds;
                    if (fade < 0f)
                    {
                        bool cutShort = Loop || (StartSeconds + AudioDurationSeconds > VideoDurationSeconds + 0.05);
                        fade = cutShort ? 1f : 0f;
                    }
                    if (VideoDurationSeconds > 0)
                        fade = Mathf.Min(fade, (float)VideoDurationSeconds * 0.5f);
                    return Mathf.Max(0f, fade);
                }
            }
        }

        public static bool IsSupportedAudioExtension(string pathOrExt)
        {
            if (string.IsNullOrWhiteSpace(pathOrExt)) return false;
            string ext = pathOrExt.StartsWith(".")
                ? pathOrExt.Trim().ToLowerInvariant()
                : System.IO.Path.GetExtension(pathOrExt).ToLowerInvariant();
            switch (ext)
            {
                case ".wav":
                case ".mp3":
                case ".flac":
                case ".ogg":
                case ".oga":
                case ".opus":
                case ".m4a":
                case ".aac":
                case ".wma":
                case ".aiff":
                case ".aif":
                    return true;
                default:
                    return false;
            }
        }

        public static string GetAudioPreviewOutputPath(string sourcePath)
        {
            string dir = System.IO.Path.Combine(GetAppRoot(), "tempCache", "aichat_audio_previews");
            Directory.CreateDirectory(dir);
            string stem = "audio";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }
            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_preview.mp4");
        }

        /// <summary>Where a user-dropped audio file is copied so the chat owns its own copy.</summary>
        public static string GetImportedAudioPath(string sourcePath)
        {
            string dir = System.IO.Path.Combine(GetAppRoot(), "tempCache", "aichat_audio");
            Directory.CreateDirectory(dir);
            string stem = "import";
            string ext = ".wav";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
                string e = System.IO.Path.GetExtension(sourcePath);
                if (!string.IsNullOrEmpty(e)) ext = e.ToLowerInvariant();
            }
            catch { }
            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        /// <summary>Where the clip chooser's "Save audio" WAV lands so chat owns its own copy.</summary>
        public static string GetExtractedAudioWavPath(string sourcePath)
        {
            string dir = System.IO.Path.Combine(GetAppRoot(), "tempCache", "aichat_audio");
            Directory.CreateDirectory(dir);
            string stem = "clip_audio";
            try
            {
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem))
                    stem = SanitizeFileStem(fileStem);
            }
            catch { }
            return System.IO.Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
        }

        public static string GetVoiceSamplePath()
        {
            string dir = System.IO.Path.Combine(GetAppRoot(), "tempCache", "aichat_audio");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "voice_ref_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
        }

        public static IEnumerator ProbeAudio(string inputPath, Action<AudioInfo, string> onDone)
        {
            if (!TryGetToolPaths(out _, out string ffprobePath, out string toolError))
            {
                onDone?.Invoke(null, toolError);
                yield break;
            }

            string args = "-v error -print_format json -show_format -show_streams " + QuoteArg(inputPath);
            Task<ProcessResult> task = Task.Run(() => RunProcess(ffprobePath, args, AudioProbeTimeoutMs));
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                onDone?.Invoke(null, task.Exception != null ? task.Exception.GetBaseException().Message : "ffprobe failed.");
                yield break;
            }

            ProcessResult pr = task.Result;
            if (!pr.Success)
            {
                onDone?.Invoke(null, BuildProcessError("ffprobe", pr));
                yield break;
            }

            AudioInfo info = null;
            string parseError = null;
            try
            {
                info = ParseAudioProbeJson(inputPath, pr.Stdout);
            }
            catch (Exception ex)
            {
                parseError = "Could not parse ffprobe output: " + ex.Message;
            }
            if (info == null)
            {
                onDone?.Invoke(null, parseError ?? "ffprobe returned nothing for " + inputPath);
                yield break;
            }
            if (!info.HasAudio)
            {
                onDone?.Invoke(info, "no audio stream in " + inputPath);
                yield break;
            }
            onDone?.Invoke(info, null);
        }

        private static AudioInfo ParseAudioProbeJson(string inputPath, string jsonText)
        {
            JSONNode root = JSON.Parse(jsonText);
            if (root == null) return null;
            var info = new AudioInfo { Path = inputPath };
            JSONNode format = root["format"];
            if (format != null)
                info.DurationSeconds = ParseDouble(format["duration"]);

            JSONArray streams = root["streams"] != null ? root["streams"].AsArray : null;
            if (streams != null)
            {
                foreach (JSONNode stream in streams)
                {
                    if (stream == null || stream["codec_type"] == null) continue;
                    string type = stream["codec_type"].Value;
                    if (type == "video")
                    {
                        // Cover art in mp3/flac shows up as a video stream too; only count real ones.
                        string disp = stream["disposition"] != null && stream["disposition"]["attached_pic"] != null
                            ? stream["disposition"]["attached_pic"].Value : "0";
                        if (disp != "1") info.HasVideo = true;
                        continue;
                    }
                    if (type != "audio" || info.HasAudio) continue;
                    info.HasAudio = true;
                    info.CodecName = stream["codec_name"];
                    info.SampleRate = (int)ParseDouble(stream["sample_rate"]);
                    info.Channels = stream["channels"] != null ? stream["channels"].AsInt : 0;
                    if (info.DurationSeconds <= 0)
                        info.DurationSeconds = ParseDouble(stream["duration"]);
                }
            }
            return info;
        }

        /// <summary>
        /// Render an audio file into a small H.264 MP4 whose picture is its live waveform and
        /// whose track is the audio (AAC). Audio bubbles are Movie bubbles playing this file,
        /// so preview, mute/volume, click-to-focus, save, and every Movie-based action work
        /// unchanged. Cheap: ~0.3 s for 12 s of audio.
        /// </summary>
        public static IEnumerator CreateAudioWaveformPreview(string audioPath, string outputPath, string colorHex, Action<ClipResult> onDone)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = GetAudioPreviewOutputPath(audioPath);
            string args = BuildWaveformPreviewArgs(audioPath, outputPath, colorHex);
            yield return RunFfmpegToFile(args, outputPath, AudioPreviewTimeoutMs, "ffmpeg audio preview", onDone);
        }

        internal static string BuildWaveformPreviewArgs(string audioPath, string outputPath, string colorHex)
        {
            string color = string.IsNullOrWhiteSpace(colorHex) ? AudioColorMusic : colorHex.Trim();
            int w = AudioPreviewWidth;
            int h = AudioPreviewHeight;
            string filter =
                "[0:a]aformat=channel_layouts=stereo," +
                "showwaves=s=" + w + "x" + h + ":mode=cline:rate=25:colors=" + color + "|" + color + ":scale=sqrt:draw=full," +
                "drawbox=x=0:y=" + (h / 2 - 1) + ":w=" + w + ":h=2:color=" + color + "@0.35:t=fill," +
                "format=yuv420p[v]";
            return "-hide_banner -y"
                + " -i " + QuoteArg(audioPath)
                + " -filter_complex " + QuoteArg(filter)
                + " -map [v] -map 0:a:0"
                + " -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p"
                + " -c:a aac -b:a 192k -movflags +faststart -shortest "
                + QuoteArg(outputPath);
        }

        /// <summary>
        /// Cut a full-quality WAV section out of a video/audio file: native channel count
        /// and sample rate, 16-bit PCM. The clip chooser's "Save audio" path; unlike
        /// <see cref="ExtractAudioSection"/> it is NOT downmixed for voice cloning.
        /// </summary>
        public static IEnumerator ExtractAudioWavSection(string inputPath, float startSeconds, float durationSeconds, string outputWavPath, Action<ClipResult> onDone)
        {
            startSeconds = Mathf.Max(0f, startSeconds);
            // No maximum: the clip chooser can export arbitrarily long ranges.
            durationSeconds = Mathf.Max(durationSeconds <= 0f ? FfmpegTool.DefaultClipDurationSeconds : durationSeconds, 0.1f);
            int timeoutMs = (int)Mathf.Max(AudioSectionTimeoutMs, durationSeconds * 1000f + 30000f);
            string args = "-hide_banner -y"
                + " -ss " + startSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + " -t " + durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + " -i " + QuoteArg(inputPath)
                + " -vn -map 0:a:0 -c:a pcm_s16le -f wav "
                + QuoteArg(outputWavPath);
            yield return RunFfmpegToFile(args, outputWavPath, timeoutMs, "ffmpeg audio wav section", onDone);
        }

        /// <summary>
        /// Cut a mono 16-bit WAV section out of any audio/video file (the voice-clone sample
        /// for generate_speech ref_voice, 15-30 s recommended).
        /// </summary>
        public static IEnumerator ExtractAudioSection(string inputPath, float startSeconds, float durationSeconds, string outputWavPath, Action<ClipResult> onDone)
        {
            startSeconds = Mathf.Max(0f, startSeconds);
            durationSeconds = Mathf.Clamp(durationSeconds <= 0f ? 25f : durationSeconds, 0.5f, 60f);
            string args = "-hide_banner -y"
                + " -ss " + startSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + " -t " + durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + " -i " + QuoteArg(inputPath)
                + " -vn -map 0:a:0 -ac 1 -ar 24000 -c:a pcm_s16le -f wav "
                + QuoteArg(outputWavPath);
            yield return RunFfmpegToFile(args, outputWavPath, AudioSectionTimeoutMs, "ffmpeg audio section", onDone);
        }

        /// <summary>
        /// Put <see cref="MuxAudioRequest.AudioPath"/> onto <see cref="MuxAudioRequest.VideoPath"/>.
        /// The video stream is copied untouched; the output is exactly the video's length (the
        /// new audio is padded with silence or cut, optionally looped and faded).
        /// </summary>
        public static IEnumerator MuxAudioIntoVideo(MuxAudioRequest req, string outputPath, Action<ClipResult> onDone)
        {
            if (req == null || string.IsNullOrEmpty(req.VideoPath) || string.IsNullOrEmpty(req.AudioPath))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = "set_video_audio: missing video or audio path." });
                yield break;
            }
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = GetClipOutputPath(req.VideoPath);
            string args = BuildMuxAudioArgs(req, outputPath);
            yield return RunFfmpegToFile(args, outputPath, AudioMuxTimeoutMs, "ffmpeg mux audio", onDone);
        }

        internal static string BuildMuxAudioArgs(MuxAudioRequest req, string outputPath)
        {
            var ci = CultureInfo.InvariantCulture;
            double videoDur = req.VideoDurationSeconds > 0 ? req.VideoDurationSeconds : Math.Max(0.1, req.AudioDurationSeconds);
            string durStr = videoDur.ToString("0.###", ci);
            float fadeOut = req.EffectiveFadeOutSeconds;
            float fadeIn = Mathf.Max(0f, req.FadeInSeconds);
            float start = Mathf.Max(0f, req.StartSeconds);

            // The new track: uniform format (mono speech, 24 kHz wav... all become 44.1k stereo
            // so amix has matching inputs), gain, offset, infinite silence padding (-t trims it
            // to the video), then fades placed on the VIDEO timeline.
            var chain = new StringBuilder();
            chain.Append("aformat=sample_rates=44100:channel_layouts=stereo");
            if (Math.Abs(req.AudioVolume - 1f) > 0.001f)
                chain.Append(",volume=").Append(Mathf.Max(0f, req.AudioVolume).ToString("0.###", ci));
            if (start > 0.0005f)
                chain.Append(",adelay=delays=").Append(Mathf.RoundToInt(start * 1000f)).Append(":all=1");
            chain.Append(",apad");
            if (fadeIn > 0.001f)
                chain.Append(",afade=t=in:st=").Append(start.ToString("0.###", ci)).Append(":d=").Append(fadeIn.ToString("0.###", ci));
            if (fadeOut > 0.001f)
                chain.Append(",afade=t=out:st=").Append(Math.Max(0, videoDur - fadeOut).ToString("0.###", ci)).Append(":d=").Append(fadeOut.ToString("0.###", ci));

            string graph;
            if (req.EffectiveMix)
            {
                string origVol = Mathf.Max(0f, req.OriginalVolume).ToString("0.###", ci);
                graph = "[0:a]aformat=sample_rates=44100:channel_layouts=stereo,volume=" + origVol + "[a0];"
                      + "[1:a]" + chain + "[a1];"
                      + "[a0][a1]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0[a]";
            }
            else
            {
                graph = "[1:a]" + chain + "[a]";
            }

            return "-hide_banner -y"
                + " -i " + QuoteArg(req.VideoPath)
                + (req.Loop ? " -stream_loop -1" : "")
                + " -i " + QuoteArg(req.AudioPath)
                + " -filter_complex " + QuoteArg(graph)
                + " -map 0:v:0 -map [a]"
                + " -c:v copy -c:a aac -b:a 192k -movflags +faststart"
                + " -t " + durStr + " "
                + QuoteArg(outputPath);
        }

        private static IEnumerator RunFfmpegToFile(string args, string outputPath, int timeoutMs, string logLabel, Action<ClipResult> onDone)
        {
            if (!TryGetToolPaths(out string ffmpegPath, out _, out string toolError))
            {
                onDone?.Invoke(new ClipResult { Success = false, OutputPath = outputPath, Error = toolError });
                yield break;
            }
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)); } catch { }

            Task<ProcessResult> task = Task.Run(() => RunProcess(ffmpegPath, args, timeoutMs));
            while (!task.IsCompleted)
                yield return null;

            var result = new ClipResult { OutputPath = outputPath };
            if (task.IsFaulted)
            {
                result.Success = false;
                result.Error = task.Exception != null ? task.Exception.GetBaseException().Message : "ffmpeg failed.";
                onDone?.Invoke(result);
                yield break;
            }

            ProcessResult pr = task.Result;
            UnityEngine.Debug.Log(logLabel + ": " + pr.Command + "\n" + pr.Stderr);
            result.Command = pr.Command;
            result.Stdout = pr.Stdout;
            result.Stderr = pr.Stderr;
            result.ExitCode = pr.ExitCode;
            result.Success = pr.Success && File.Exists(outputPath);
            if (!result.Success)
                result.Error = BuildProcessError("ffmpeg", pr);
            onDone?.Invoke(result);
        }
    }
}
