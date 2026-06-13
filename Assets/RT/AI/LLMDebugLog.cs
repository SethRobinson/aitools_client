using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Unified, provider-agnostic debug logging for every LLM text-completion
/// manager (Gemini / Anthropic / OpenAI / TexGenWebUI).
///
/// Every provider writes the SAME files, last-writer-wins, so
/// "look at llm_response.json" works no matter which backend served the
/// request:
///
///   llm_request_sent.json - the exact JSON body we POSTed
///   llm_response.json     - raw HTTP response body on success
///   llm_last_error.json   - raw HTTP response body on failure
///
/// These are last-writer-wins single-shot files: the big conversational AI Chat
/// turn and the small vision-caption / summarization "sidecar" requests the same
/// managers serve all clobber each other here. For a full, un-clobbered picture
/// of an AI Chat exchange (request + reply + the emitted tool calls), the main
/// send and each sidecar wrap their dispatch in <see cref="PurposeScope"/>, which
/// forwards every request body to the editor-only <see cref="AIChatLog"/>
/// (llm_aichat_log.json) tagged with its purpose. Responses are logged there
/// explicitly by the AI Chat callbacks (they arrive async, outside the scope).
///
/// Before this existed, each manager wrote its own differently-named files
/// behind inconsistent compile guards (gemini_response.json,
/// claude_json_received.json, textgen_json_received.json, ...), which made
/// "why did that LLM do that" debugging provider-specific guesswork.
///
/// Writes are best-effort and off the main thread (a multi-MB caption
/// request written synchronously was once the source of multi-second
/// freezes during AI Chat image edits). A failed write never breaks a
/// request.
///
/// Enabled at RUNTIME via the "Write debug .json files" option in General
/// Settings (UserPreferences.WriteDebugJsonFiles, default on). This now
/// works in RELEASE builds too - it used to be compiled out of releases,
/// which made shipped-build issues undebuggable. Still compiled out of
/// non-standalone targets (e.g. WebGL) where local file IO isn't available.
/// </summary>
public static class LLMDebugLog
{
    public const string RequestFile = "llm_request_sent.json";
    public const string ResponseFile = "llm_response.json";
    public const string ErrorFile = "llm_last_error.json";

    // When non-null, LogRequest also forwards the body to AIChatLog under this
    // purpose label (e.g. "chat", "ImageCaption"). Set for the duration of an AI
    // Chat dispatch via PurposeScope. Main-thread only.
    private static string s_purpose = null;

    /// <summary>The exact request body about to be POSTed to the provider.</summary>
    public static void LogRequest(string json)
    {
        // Forward to the editor-only AI Chat log when we're inside an AI Chat
        // dispatch. Relies on every manager calling LogRequest synchronously
        // before its first yield, while the PurposeScope below is still active.
        if (!string.IsNullOrEmpty(s_purpose))
            AIChatLog.Request(s_purpose, json);
        WriteAsync(RequestFile, json);
    }

    /// <summary>
    /// Tags every request logged until the returned token is disposed as belonging
    /// to <paramref name="purpose"/>, so AIChatLog can attribute it (the main chat
    /// turn = "chat", sidecars = their caller label). Relies on Unity coroutines
    /// running synchronously up to their first yield: every manager calls
    /// LogRequest() before its first yield, so the request is captured while this
    /// scope is still active (the dispatch happens inside the using block).
    /// Main-thread only.
    /// </summary>
    public static IDisposable PurposeScope(string purpose)
    {
        var prev = s_purpose;
        s_purpose = purpose;
        return new Scope(prev);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _prev;
        private bool _done;
        public Scope(string prev) { _prev = prev; }
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            s_purpose = _prev;
        }
    }

    /// <summary>The raw HTTP response body of a successful completion.</summary>
    public static void LogResponse(string body) { WriteAsync(ResponseFile, body); }

    /// <summary>The raw HTTP response body of a failed request.</summary>
    public static void LogError(string body) { WriteAsync(ErrorFile, body); }

    private static void WriteAsync(string file, string contents)
    {
#if UNITY_STANDALONE
        // Runtime opt-out. UserPreferences may not exist yet during very early
        // startup; default to writing (the preference defaults on anyway).
        var prefs = UserPreferences.Get();
        if (prefs != null && !prefs.WriteDebugJsonFiles) return;

        string f = file;
        string c = contents ?? "";
        Task.Run(() => { try { File.WriteAllText(f, c); } catch { /* diagnostics must never break a request */ } });
#endif
    }
}
