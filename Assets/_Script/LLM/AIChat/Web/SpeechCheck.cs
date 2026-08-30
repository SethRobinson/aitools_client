using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using AITools.AIChat.Video;

namespace AITools.AIChat.Web
{
    /// <summary>
    /// "Does this clip contain someone TALKING?" for AI Chat's web_video skill. Vision
    /// sidecars only ever see a contact sheet of frames, so a music-only clip looks exactly
    /// like a dialogue clip to them; but MiniMax H3 Ref2VA clones the reference clip's audio
    /// for the generated voice, so a speech-less reference yields a garbled voice. Two stages:
    /// 1. bundled ffmpeg extracts 16 kHz mono WAV and measures mean volume (silent = no speech,
    ///    no network needed);
    /// 2. a Whisper-style transcription (Settings > Web endpoint: any OpenAI-compatible
    ///    /v1/audio/transcriptions such as a local Whisper server; else api.openai.com with the
    ///    LLM Settings OpenAI key; verbose_json) transcribes the WAV; speech is "present" when
    ///    enough words came back and the segments' no_speech_prob is low (music and ambience
    ///    produce few words with high no_speech_prob, or stray lyrics/hallucinations).
    /// Without any transcription endpoint the result is Unknown, and the caller says so.
    /// A pure signal heuristic (envelope modulation, flatness, pauses) was prototyped and
    /// rejected: a bass-riff theme scored like speech.
    /// </summary>
    public static class SpeechCheck
    {
        public const int MinWordsForSpeech = 5;
        // Short catchphrase files ("What's up, Doc?" is 3 words in 3 s) would always fail
        // the 5-word rule, which was tuned for 5 s video cuts. Under ShortClipSeconds the
        // bar drops to MinWordsForShortClip; the no_speech_prob cap still applies.
        public const float ShortClipSeconds = 4f;
        public const int MinWordsForShortClip = 2;
        public const float SilentMeanVolumeDb = -50f;
        public const float MaxNoSpeechProb = 0.6f;
        private const int WhisperTimeoutSeconds = 60;

        public sealed class Result
        {
            public bool Completed;
            public bool HasAudioStream;
            /// <summary>Clip length the caller knows (0 = unknown); relaxes the word minimum for short catchphrase files.</summary>
            public float ClipDurationSeconds;
            public float MeanVolumeDb = float.NaN;
            public bool Silent;
            /// <summary>true = Whisper ran and produced a verdict; false = no STT available / failed (see Error).</summary>
            public bool Transcribed;
            public bool HasSpeech;
            public string Transcript = "";
            public int WordCount;
            public float AvgNoSpeechProb = float.NaN;
            public string Error;

            public string Summary()
            {
                var sb = new StringBuilder();
                if (!HasAudioStream) return "no audio stream";
                if (!float.IsNaN(MeanVolumeDb)) sb.Append("mean volume ").Append(MeanVolumeDb.ToString("0", CultureInfo.InvariantCulture)).Append(" dB");
                if (Silent) { sb.Append(" (silent)"); return sb.ToString(); }
                if (Transcribed)
                {
                    sb.Append("; Whisper: ").Append(WordCount).Append(WordCount == 1 ? " word" : " words");
                    if (!float.IsNaN(AvgNoSpeechProb)) sb.Append(", no-speech prob ").Append(AvgNoSpeechProb.ToString("0.00", CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(Transcript)) sb.Append(" \"").Append(Transcript.Length > 160 ? Transcript.Substring(0, 160) + "..." : Transcript).Append('"');
                    sb.Append(HasSpeech ? " -> speech present" : " -> no real speech (music / ambience / noise)");
                }
                else if (!string.IsNullOrEmpty(Error))
                {
                    sb.Append("; speech check unavailable: ").Append(Error);
                }
                return sb.ToString();
            }
        }

        public const string OpenAITranscriptionsUrl = "https://api.openai.com/v1/audio/transcriptions";

        /// <summary>
        /// Resolve the transcription endpoint: Settings > Web "Speech-to-text endpoint" (any
        /// OpenAI-compatible /v1/audio/transcriptions, e.g. a local Whisper server) with its own
        /// key, else api.openai.com with the LLM Settings OpenAI key.
        /// </summary>
        public static bool TryGetTranscriptionEndpoint(out string url, out string key, out string model, out string reason)
        {
            url = null; key = null; model = "whisper-1"; reason = null;
            var cfg = Config.Get();
            string custom = cfg != null ? (cfg.GetSttEndpoint() ?? "").Trim() : "";
            if (custom.Length > 0)
            {
                url = custom;
                if (!url.Contains("/audio/transcriptions"))
                    url = url.TrimEnd('/') + (url.EndsWith("/v1") || url.EndsWith("/v1/") ? "" : "/v1") + "/audio/transcriptions";
                key = cfg.GetSttAPIKey();
                model = cfg.GetSttModel();
                return true;
            }
            string openAiKey = cfg != null ? cfg.GetOpenAI_APIKey() : null;
            if (string.IsNullOrWhiteSpace(openAiKey))
            {
                reason = "no speech-to-text configured: set an OpenAI key in LLM Settings, or a Whisper-compatible endpoint in Settings > Web";
                return false;
            }
            url = OpenAITranscriptionsUrl;
            key = openAiKey;
            model = cfg.GetSttModel();
            return true;
        }

        public static bool HasSpeechToText(out string reason)
        {
            string url, key, model;
            return TryGetTranscriptionEndpoint(out url, out key, out model, out reason);
        }

        public static string DescribeSpeechToText()
        {
            string url, key, model, reason;
            if (!TryGetTranscriptionEndpoint(out url, out key, out model, out reason))
                return "Speech-to-text (web_video speech checks): NOT configured - " + reason + ".";
            return "Speech-to-text (web_video speech checks): " + url + " model " + model + (string.IsNullOrEmpty(key) ? " (no key)" : " (key set)");
        }

        public static IEnumerator Run(string clipPath, bool clipHasAudioStream, Result result, float clipDurationSeconds = 0f)
        {
            result.HasAudioStream = clipHasAudioStream;
            result.ClipDurationSeconds = clipDurationSeconds;
            if (!clipHasAudioStream)
            {
                result.Completed = true;
                yield break;
            }

            string wavPath = Path.Combine(FfmpegTool.GetAppRoot(), "tempCache", "aichat_web_videos", "speech_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
            FfmpegTool.ClipResult extract = null;
            yield return FfmpegTool.ExtractAudioWav(clipPath, wavPath, r => extract = r);
            if (extract == null || !extract.Success)
            {
                result.Error = "could not extract audio: " + (extract != null ? FirstLine(extract.Error) : "unknown");
                result.Completed = true;
                yield break;
            }

            float meanDb = float.NaN;
            yield return FfmpegTool.MeasureMeanVolume(wavPath, v => meanDb = v);
            result.MeanVolumeDb = meanDb;
            if (!float.IsNaN(meanDb) && meanDb < SilentMeanVolumeDb)
            {
                result.Silent = true;
                result.HasSpeech = false;
                result.Transcribed = true; // a silent track is a definitive "no speech"
                TryDelete(wavPath);
                result.Completed = true;
                yield break;
            }

            string why;
            if (!HasSpeechToText(out why))
            {
                result.Error = why;
                TryDelete(wavPath);
                result.Completed = true;
                yield break;
            }

            byte[] wav = null;
            try { wav = File.ReadAllBytes(wavPath); } catch (Exception ex) { result.Error = "could not read wav: " + ex.Message; }
            TryDelete(wavPath);
            if (wav == null)
            {
                result.Completed = true;
                yield break;
            }

            yield return Transcribe(wav, result);
            result.Completed = true;
        }

        private static IEnumerator Transcribe(byte[] wav, Result result)
        {
            string url, key, model, reason;
            if (!TryGetTranscriptionEndpoint(out url, out key, out model, out reason))
            {
                result.Error = reason;
                yield break;
            }
            var form = new WWWForm();
            form.AddField("model", model);
            form.AddField("response_format", "verbose_json");
            form.AddField("temperature", "0");
            form.AddBinaryData("file", wav, "clip.wav", "audio/wav");

            using (var req = UnityWebRequest.Post(url, form))
            {
                if (!string.IsNullOrEmpty(key))
                    req.SetRequestHeader("Authorization", "Bearer " + key);
                req.timeout = WhisperTimeoutSeconds;
                yield return req.SendWebRequest();

                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    result.Error = "Whisper request failed: HTTP " + req.responseCode + " " + (req.error ?? "") + (string.IsNullOrEmpty(body) ? "" : " " + BraveSearchClient.Excerpt(body, 200));
                    yield break;
                }

                try
                {
                    var root = JSON.Parse(body);
                    string text = root != null && root["text"] != null ? (root["text"].Value ?? "").Trim() : "";
                    result.Transcript = CleanTranscript(text);
                    result.WordCount = CountWords(result.Transcript);

                    var segments = root != null ? root["segments"] : null;
                    if (segments != null && segments.IsArray && segments.Count > 0)
                    {
                        double sum = 0; int n = 0;
                        foreach (JSONNode seg in segments.AsArray)
                        {
                            var p = seg["no_speech_prob"];
                            if (p != null && p.IsNumber) { sum += p.AsDouble; n++; }
                        }
                        if (n > 0) result.AvgNoSpeechProb = (float)(sum / n);
                    }
                    result.Transcribed = true;
                    bool shortClip = result.ClipDurationSeconds > 0f && result.ClipDurationSeconds < ShortClipSeconds;
                    bool enoughWords = result.WordCount >= (shortClip ? MinWordsForShortClip : MinWordsForSpeech);
                    bool probOk = float.IsNaN(result.AvgNoSpeechProb) || result.AvgNoSpeechProb <= MaxNoSpeechProb;
                    result.HasSpeech = enoughWords && probOk && !LooksLikeNonSpeechMarker(result.Transcript);
                }
                catch (Exception ex)
                {
                    result.Error = "could not parse Whisper response: " + ex.Message;
                }
            }
        }

        private static string CleanTranscript(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int count = 0;
            foreach (string token in text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool hasLetter = false;
                foreach (char c in token) { if (char.IsLetterOrDigit(c)) { hasLetter = true; break; } }
                if (hasLetter) count++;
            }
            return count;
        }

        // Whisper tags wordless audio as "[Music]", "(applause)", "♪ ... ♪" etc.
        private static bool LooksLikeNonSpeechMarker(string text)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0) return true;
            if (t.StartsWith("[") && t.EndsWith("]")) return true;
            if (t.StartsWith("(") && t.EndsWith(")")) return true;
            if (t.Contains("♪") && t.Replace("♪", "").Trim().Length < 20) return true;
            // Whisper large-v3 likes to emit the musical-note emoji (U+1F3B5 / U+1F3B6) for music.
            string stripped = t.Replace("🎵", "").Replace("🎶", "").Trim();
            if (stripped.Length == 0) return true;
            return false;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i).TrimEnd('\r');
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
