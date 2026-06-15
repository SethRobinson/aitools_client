using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Makes optional sampling parameters (min_p, top_k, logit_bias, repetition/
/// frequency/presence penalties, ...) "just work" against OpenAI-compatible
/// servers that reject some of them.
///
/// Some backends refuse certain samplers depending on how they were launched -
/// e.g. sglang/vLLM with speculative decoding returns:
///
///   "The min_p and logit_bias sampling parameters are not yet supported
///    with speculative decoding."
///
/// Because there is no capability-negotiation in the OpenAI Chat Completions
/// API, we react to that error: detect which sampler the server named, strip
/// it from the request body, retry once, and REMEMBER it for that endpoint so
/// every later request this session pre-strips it (no repeated failed round
/// trip). The user's saved sampling preferences are untouched; we only drop
/// what the specific server can't accept.
///
/// Memory is session-scoped (static, cleared on app restart). Restarting the
/// LLM server without the offending flag, or relaunching the app, clears it.
/// </summary>
public static class LLMSamplingCompat
{
    // Top-level sampler keys we emit that a server might reject. Order doesn't matter.
    private static readonly string[] KnownSamplerKeys =
    {
        "min_p", "top_k", "top_p", "logit_bias",
        "repetition_penalty", "repeat_penalty", "repeat_last_n",
        "frequency_penalty", "presence_penalty", "temperature"
    };

    // Phrases that mark an error body as "this parameter is not allowed here"
    // (vs. some unrelated body that merely happens to contain the word "min_p").
    private static readonly string[] RejectionPhrases =
    {
        "not supported", "not yet supported", "unsupported", "not allowed",
        "are not", "is not", "cannot be used", "unexpected", "unrecognized", "invalid"
    };

    // endpoint (lowercased) -> sampler keys that endpoint has rejected this session.
    private static readonly Dictionary<string, HashSet<string>> _blockedByEndpoint =
        new Dictionary<string, HashSet<string>>();

    private static string NormEndpoint(string endpoint) => (endpoint ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Scan a server error body for sampler keys it complains about. Returns the
    /// set of offending keys (empty if the body isn't a sampler-rejection error).
    /// </summary>
    public static HashSet<string> DetectUnsupportedSamplingKeys(string errorBody)
    {
        var found = new HashSet<string>();
        if (string.IsNullOrEmpty(errorBody)) return found;

        string lower = errorBody.ToLowerInvariant();

        bool looksLikeRejection = false;
        foreach (var phrase in RejectionPhrases)
        {
            if (lower.Contains(phrase)) { looksLikeRejection = true; break; }
        }
        if (!looksLikeRejection) return found;

        foreach (var key in KnownSamplerKeys)
        {
            if (lower.Contains(key)) found.Add(key);
        }
        return found;
    }

    /// <summary>Remove the given top-level keys from a generated request body.</summary>
    public static string StripKeys(string json, IEnumerable<string> keys)
    {
        if (string.IsNullOrEmpty(json) || keys == null) return json;

        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            // "key": <number | "string" | {...} | [...]> with an optional trailing comma.
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\"[^\"]*\"|\\{[^{}]*\\}|\\[[^\\]]*\\]|[^,}\\n]+)\\s*,?";
            json = Regex.Replace(json, pattern, "");
        }

        // Tidy up anything the removal left behind: a comma right before a closing
        // brace, or two commas in a row.
        json = Regex.Replace(json, ",(\\s*})", "$1");
        json = Regex.Replace(json, ",(\\s*,)", "$1");
        return json;
    }

    /// <summary>
    /// Pre-strip samplers an endpoint already rejected earlier this session, so
    /// the request goes through on the first try.
    /// </summary>
    public static string ApplyKnownStrips(string endpoint, string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        if (_blockedByEndpoint.TryGetValue(NormEndpoint(endpoint), out var blocked) && blocked.Count > 0)
            return StripKeys(json, blocked);
        return json;
    }

    /// <summary>
    /// Given a failed request's body+error, decide whether the failure was a
    /// rejected sampler we can drop. If so, remember it for this endpoint and
    /// return a stripped request body to retry with. Returns null when there is
    /// nothing safe to strip (caller should surface the error instead).
    /// </summary>
    public static string TryStripUnsupportedSampling(string json, string errorBody, string endpoint)
    {
        if (string.IsNullOrEmpty(json)) return null;

        var keys = DetectUnsupportedSamplingKeys(errorBody);
        if (keys.Count == 0) return null;

        // Only act on keys actually present in this request body.
        var present = new HashSet<string>();
        foreach (var key in keys)
        {
            if (json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal) >= 0)
                present.Add(key);
        }
        if (present.Count == 0) return null;

        // Remember for the rest of the session.
        string ep = NormEndpoint(endpoint);
        if (!_blockedByEndpoint.TryGetValue(ep, out var blocked))
        {
            blocked = new HashSet<string>();
            _blockedByEndpoint[ep] = blocked;
        }
        foreach (var key in present) blocked.Add(key);

        string stripped = StripKeys(json, present);
        return stripped != json ? stripped : null;
    }

    /// <summary>
    /// Best human-readable error to show the user: prefer the server's response
    /// body, fall back to the transport-level error string.
    /// </summary>
    public static string BestErrorMessage(string body, string transportError)
    {
        if (!string.IsNullOrEmpty(body)) return body.Trim();
        if (!string.IsNullOrEmpty(transportError)) return transportError.Trim();
        return "LLM returned an error with no response body.";
    }
}
