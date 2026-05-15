using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Unified, provider-agnostic debug logging for every LLM text-completion
/// manager (Gemini / Anthropic / OpenAI / TexGenWebUI).
///
/// Every provider writes the SAME three files, last-writer-wins, so
/// "look at llm_response.json" works no matter which backend served the
/// request:
///
///   llm_request_sent.json - the exact JSON body we POSTed
///   llm_response.json     - raw HTTP response body on success
///   llm_last_error.json   - raw HTTP response body on failure
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

    /// <summary>The exact request body about to be POSTed to the provider.</summary>
    public static void LogRequest(string json) { WriteAsync(RequestFile, json); }

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
