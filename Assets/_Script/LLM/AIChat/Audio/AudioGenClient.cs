using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace AITools.AIChat.Audio
{
    public enum AudioGenKind
    {
        Music,
        Sfx,
        Speech
    }

    /// <summary>
    /// One generation request for the audio gateway. <see cref="Fields"/> are sent as-is
    /// as multipart form fields, so the executor can pass through whatever the model
    /// supplied (prompt, duration, lyrics, seed, voice, scene...) without this client
    /// knowing every parameter the server supports.
    /// </summary>
    public sealed class AudioGenRequest
    {
        public AudioGenKind Kind;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
        /// <summary>Chat bubble (Audio or Movie) whose audio is uploaded as the <c>ref_voice</c> clone sample; 0 = none.</summary>
        public int RefVoiceChatImageIndex;
        public float RefVoiceStartSeconds;
        public float RefVoiceDurationSeconds = 25f;
        /// <summary>Short human text for bubbles / recap ("song about bananas").</summary>
        public string Label;

        public string SkillName
        {
            get
            {
                switch (Kind)
                {
                    case AudioGenKind.Music: return "generate_music";
                    case AudioGenKind.Sfx: return "generate_sfx";
                    default: return "generate_speech";
                }
            }
        }

        public string KindNoun
        {
            get
            {
                switch (Kind)
                {
                    case AudioGenKind.Music: return "music";
                    case AudioGenKind.Sfx: return "sound effect";
                    default: return "speech";
                }
            }
        }
    }

    public sealed class AudioGenResult
    {
        public bool Success;
        public string OutputPath;
        public string Error;
        public string ContentType;
        public long HttpStatus;
        public float ElapsedSeconds;
        public bool Cancelled;
    }

    /// <summary>
    /// Talks to the AI Chat "audio generation gateway": a small HTTP server (Settings >
    /// Audio > Audio generation) that turns text into music, sound effects, and speech.
    /// Contract (see docs/audio_generation.md):
    ///   POST {base}/audio  multipart: prompt, duration, [mode=music], [lyrics], [vocals],
    ///                      [seed], [format] ...            -> audio file body (wav/flac/mp3)
    ///   POST {base}/tts    multipart: text, [voice], [scene], [language], [engine], [seed],
    ///                      [temperature], file ref_voice   -> audio file body (wav)
    /// Errors are any non-2xx status; a JSON body with a "detail" field is surfaced verbatim
    /// (truncated) because those servers usually explain which parameter was rejected.
    /// The response body is saved under tempCache/aichat_audio/ and the caller turns it
    /// into an Audio bubble.
    /// </summary>
    public static class AudioGenClient
    {
        public const int MusicTimeoutSeconds = 15 * 60;   // long tracks render at ~1x realtime
        public const int DefaultTimeoutSeconds = 5 * 60;
        private const int MaxErrorExcerptChars = 700;

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

        public static bool TryGetBaseUrl(out string baseUrl, out string apiKey, out string reason)
        {
            baseUrl = null;
            apiKey = null;
            reason = null;
            var cfg = Config.Get();
            string url = cfg != null ? (cfg.GetAudioGenEndpoint() ?? "").Trim() : "";
            if (url.Length == 0)
            {
                reason = "no audio generation gateway configured (Settings > Audio > Audio generation)";
                return false;
            }
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            baseUrl = url.TrimEnd('/');
            apiKey = cfg != null ? (cfg.GetAudioGenAPIKey() ?? "").Trim() : "";
            return true;
        }

        public static bool IsConfigured()
        {
            return TryGetBaseUrl(out _, out _, out _);
        }

        public static string GetEndpointUrl(string baseUrl, AudioGenKind kind)
        {
            // A base that already ends in /audio or /tts is taken literally for that kind.
            string b = (baseUrl ?? "").TrimEnd('/');
            if (b.EndsWith("/audio", StringComparison.OrdinalIgnoreCase) || b.EndsWith("/tts", StringComparison.OrdinalIgnoreCase))
                b = b.Substring(0, b.LastIndexOf('/'));
            return b + (kind == AudioGenKind.Speech ? "/tts" : "/audio");
        }

        /// <summary>One-line status for the Settings panel.</summary>
        public static string Describe()
        {
            if (!TryGetBaseUrl(out string baseUrl, out string key, out string reason))
                return "Audio generation (AI Chat generate_music / generate_sfx / generate_speech): NOT configured - " + reason + ".";
            return "Audio generation (AI Chat generate_music / generate_sfx / generate_speech): " + baseUrl
                + " (POST /audio, POST /tts)" + (string.IsNullOrEmpty(key) ? "" : ", key set");
        }

        public static string GetOutputDirectory()
        {
            string dir = Path.Combine(AITools.AIChat.Video.FfmpegTool.GetAppRoot(), "tempCache", "aichat_audio");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetOutputPath(AudioGenKind kind, string extension)
        {
            string ext = string.IsNullOrEmpty(extension) ? ".wav" : (extension.StartsWith(".") ? extension : "." + extension);
            string stem = kind == AudioGenKind.Music ? "music" : (kind == AudioGenKind.Sfx ? "sfx" : "speech");
            return Path.Combine(GetOutputDirectory(), stem + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        /// <summary>
        /// POST the request and save the returned audio file. <paramref name="refVoiceWav"/>
        /// (optional) is uploaded as the <c>ref_voice</c> file for voice cloning.
        /// <paramref name="onElapsed"/> is polled every frame with the seconds spent so far
        /// so the host can show a live status.
        /// </summary>
        public static IEnumerator Generate(AudioGenRequest request, byte[] refVoiceWav, Handle handle, Action<AudioGenResult> onDone, Action<float> onElapsed = null)
        {
            var result = new AudioGenResult();
            if (request == null)
            {
                result.Error = "no request";
                onDone?.Invoke(result);
                yield break;
            }
            if (!TryGetBaseUrl(out string baseUrl, out string apiKey, out string reason))
            {
                result.Error = reason;
                onDone?.Invoke(result);
                yield break;
            }

            string url = GetEndpointUrl(baseUrl, request.Kind);
            var form = new WWWForm();
            foreach (var kv in request.Fields)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                form.AddField(kv.Key, kv.Value);
            }
            if (refVoiceWav != null && refVoiceWav.Length > 0)
                form.AddBinaryData("ref_voice", refVoiceWav, "ref_voice.wav", "audio/wav");

            float started = Time.realtimeSinceStartup;
            using (var req = UnityWebRequest.Post(url, form))
            {
                if (!string.IsNullOrEmpty(apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + apiKey);
                req.timeout = request.Kind == AudioGenKind.Music ? MusicTimeoutSeconds : DefaultTimeoutSeconds;
                if (handle != null) handle.Request = req;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    onElapsed?.Invoke(Time.realtimeSinceStartup - started);
                    yield return null;
                }
                if (handle != null) handle.Request = null;
                result.ElapsedSeconds = Time.realtimeSinceStartup - started;
                result.HttpStatus = req.responseCode;
                result.ContentType = req.GetResponseHeader("Content-Type") ?? "";

                if (handle != null && handle.Cancelled)
                {
                    result.Cancelled = true;
                    result.Error = "cancelled";
                    onDone?.Invoke(result);
                    yield break;
                }

                byte[] body = req.downloadHandler != null ? req.downloadHandler.data : null;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    result.Error = BuildHttpError(req, body);
                    onDone?.Invoke(result);
                    yield break;
                }
                if (body == null || body.Length < 64 || LooksLikeJsonOrText(result.ContentType, body))
                {
                    result.Error = "the gateway returned no audio (" + (body == null ? 0 : body.Length) + " bytes, " + result.ContentType + ")"
                        + ExcerptBody(body);
                    onDone?.Invoke(result);
                    yield break;
                }

                string ext = GuessExtension(result.ContentType, req.GetResponseHeader("Content-Disposition"), body);
                string path = GetOutputPath(request.Kind, ext);
                try
                {
                    File.WriteAllBytes(path, body);
                }
                catch (Exception ex)
                {
                    result.Error = "could not save the audio file: " + ex.Message;
                    onDone?.Invoke(result);
                    yield break;
                }
                result.OutputPath = path;
                result.Success = true;
            }
            onDone?.Invoke(result);
        }

        private static string BuildHttpError(UnityWebRequest req, byte[] body)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP ").Append(req.responseCode);
            if (!string.IsNullOrEmpty(req.error)) sb.Append(' ').Append(req.error);
            string detail = ExtractDetail(body);
            if (!string.IsNullOrEmpty(detail)) sb.Append(": ").Append(detail);
            return sb.ToString();
        }

        /// <summary>Pull the "detail" out of a JSON error body (FastAPI style), else an excerpt of the body.</summary>
        private static string ExtractDetail(byte[] body)
        {
            if (body == null || body.Length == 0) return "";
            string text;
            try { text = Encoding.UTF8.GetString(body); } catch { return ""; }
            text = text.Trim();
            if (text.StartsWith("{"))
            {
                try
                {
                    JSONNode node = JSON.Parse(text);
                    JSONNode detail = node != null ? node["detail"] : null;
                    if (detail != null)
                    {
                        string d = detail.IsString ? detail.Value : detail.ToString();
                        return Truncate(d);
                    }
                }
                catch { }
            }
            return Truncate(text);
        }

        private static string ExcerptBody(byte[] body)
        {
            string detail = ExtractDetail(body);
            return string.IsNullOrEmpty(detail) ? "" : ": " + detail;
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Trim();
            return s.Length > MaxErrorExcerptChars ? s.Substring(0, MaxErrorExcerptChars) + "..." : s;
        }

        private static bool LooksLikeJsonOrText(string contentType, byte[] body)
        {
            string ct = (contentType ?? "").ToLowerInvariant();
            if (ct.StartsWith("application/json") || ct.StartsWith("text/")) return true;
            if (body == null || body.Length == 0) return true;
            // Audio containers never start with '{' or '<'.
            byte first = body[0];
            return first == (byte)'{' || first == (byte)'<';
        }

        private static string GuessExtension(string contentType, string contentDisposition, byte[] body)
        {
            string ct = (contentType ?? "").ToLowerInvariant();
            if (ct.Contains("wav")) return ".wav";
            if (ct.Contains("flac")) return ".flac";
            if (ct.Contains("mpeg") || ct.Contains("mp3")) return ".mp3";
            if (ct.Contains("ogg")) return ".ogg";
            if (ct.Contains("mp4") || ct.Contains("m4a") || ct.Contains("aac")) return ".m4a";

            if (!string.IsNullOrEmpty(contentDisposition))
            {
                int i = contentDisposition.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    string name = contentDisposition.Substring(i + "filename=".Length).Trim().Trim('"', '\'', ';');
                    string ext = Path.GetExtension(name);
                    if (!string.IsNullOrEmpty(ext) && ext.Length <= 5) return ext.ToLowerInvariant();
                }
            }

            if (body != null && body.Length >= 12)
            {
                if (body[0] == (byte)'R' && body[1] == (byte)'I' && body[2] == (byte)'F' && body[3] == (byte)'F') return ".wav";
                if (body[0] == (byte)'f' && body[1] == (byte)'L' && body[2] == (byte)'a' && body[3] == (byte)'C') return ".flac";
                if (body[0] == (byte)'O' && body[1] == (byte)'g' && body[2] == (byte)'g') return ".ogg";
                if (body[0] == (byte)'I' && body[1] == (byte)'D' && body[2] == (byte)'3') return ".mp3";
                if (body[0] == 0xFF && (body[1] & 0xE0) == 0xE0) return ".mp3";
            }
            return ".wav";
        }

        public static string FormatSeconds(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) return "?";
            return seconds.ToString(seconds >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture) + "s";
        }
    }
}
