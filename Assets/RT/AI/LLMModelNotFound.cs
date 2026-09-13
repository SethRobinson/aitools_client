using System;
using System.Text.RegularExpressions;
using SimpleJSON;

/// <summary>
/// Pure helpers for the "the model you asked for is gone" case on OpenAI-compatible
/// servers: recognizing the error body, reading/rewriting the model field of a
/// Chat Completions request, and mapping a chat endpoint back to its base URL.
///
/// A local server (vLLM, llama.cpp router, LM Studio, ...) is often relaunched
/// with a different model while the app still has the old name saved, e.g.
///
///   {"error":{"message":"The model `GLM-5.3-Flash` does not exist.",
///             "type":"NotFoundError","param":"model","code":404}}
///
/// This class only DECIDES and REWRITES; it has no Unity dependencies so a plain
/// console app can compile it with SimpleJSON.cs for regression tests. The
/// fetch-the-list-and-switch-the-instance half lives in LLMModelAutoSwitch
/// (Assets/_Script/LLM), which the request managers call through it.
/// </summary>
public static class LLMModelNotFound
{
    // Phrases servers use when the requested model name is unknown. Matched
    // case-insensitively, and only when the message also mentions "model", so a
    // generic 404 for a wrong URL path is not mistaken for a missing model.
    private static readonly string[] NotFoundPhrases =
    {
        "does not exist",
        "not exist",
        "not found",
        "no such model",
        "unknown model",
        "invalid model",
        "unsupported model",
        "not loaded",
        "could not find",
        "model_not_found",
    };

    /// <summary>
    /// True when an error response body says the requested model is unknown to the
    /// server. Understands the OpenAI error object (code/type/param/message), a bare
    /// string "error", and FastAPI-style top-level "detail"/"message" bodies.
    /// </summary>
    public static bool LooksLikeModelNotFound(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody)) return false;

        string message = null;
        string code = null;
        string type = null;
        string param = null;

        try
        {
            JSONNode root = JSON.Parse(errorBody);
            if (root != null && root.Tag == JSONNodeType.Object)
            {
                JSONNode err = root["error"];
                if (err != null && err.Tag == JSONNodeType.Object)
                {
                    message = err["message"]?.Value;
                    code = err["code"]?.Value;
                    type = err["type"]?.Value;
                    param = err["param"]?.Value;
                }
                else if (err != null && err.Tag == JSONNodeType.String)
                {
                    message = err.Value; // Ollama: {"error":"model 'x' not found, try pulling it first"}
                }

                if (string.IsNullOrEmpty(message))
                {
                    JSONNode detail = root["detail"];
                    if (detail != null && detail.Tag == JSONNodeType.String)
                        message = detail.Value;
                }
                if (string.IsNullOrEmpty(message))
                {
                    JSONNode msg = root["message"];
                    if (msg != null && msg.Tag == JSONNodeType.String)
                        message = msg.Value;
                }
            }
        }
        catch
        {
            // Not JSON: fall through to the plain-text check below.
        }

        if (string.Equals(code, "model_not_found", StringComparison.OrdinalIgnoreCase))
            return true;

        // The model field was singled out by a not-found style error, whatever the wording.
        bool notFoundType = !string.IsNullOrEmpty(type) && type.IndexOf("notfound", StringComparison.OrdinalIgnoreCase) >= 0;
        bool notFoundCode = code == "404";
        if (string.Equals(param, "model", StringComparison.OrdinalIgnoreCase) && (notFoundType || notFoundCode))
            return true;

        string text = message ?? errorBody;
        string lower = text.ToLowerInvariant();
        if (lower.IndexOf("model", StringComparison.Ordinal) < 0)
            return false;

        foreach (string phrase in NotFoundPhrases)
        {
            if (lower.IndexOf(phrase, StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    private static readonly Regex ModelFieldRegex = new Regex("(\"model\"\\s*:\\s*\")((?:[^\"\\\\]|\\\\.)*)(\")", RegexOptions.Compiled);

    /// <summary>
    /// The "model" value of a Chat Completions request body ("" when absent). The
    /// builders emit it as the first field, before "messages", so the first match is
    /// the request's own model and never text inside a message.
    /// </summary>
    public static string ExtractRequestedModel(string requestJson)
    {
        if (string.IsNullOrEmpty(requestJson)) return "";
        Match m = ModelFieldRegex.Match(requestJson);
        if (!m.Success) return "";
        try
        {
            JSONNode node = JSON.Parse("\"" + m.Groups[2].Value + "\"");
            return node != null ? node.Value : m.Groups[2].Value;
        }
        catch
        {
            return m.Groups[2].Value;
        }
    }

    /// <summary>
    /// Returns the request body with its (first) "model" value replaced. The rest
    /// of the body is untouched so sampling/thinking fields and the messages are
    /// resent exactly as built. Returns the input unchanged when no model field exists.
    /// </summary>
    public static string ReplaceModelInRequestJson(string requestJson, string newModel)
    {
        if (string.IsNullOrEmpty(requestJson) || newModel == null) return requestJson;
        string escaped = JSONNode.Escape(newModel);
        return ModelFieldRegex.Replace(requestJson, m => m.Groups[1].Value + escaped + m.Groups[3].Value, 1);
    }

    /// <summary>
    /// Top-level request fields whose legal values depend on the model FAMILY the
    /// body was built for: reasoning levels (GLM-5.3 takes low/high/max, Qwen
    /// Flash-Next only low/medium/xhigh, so a GLM body retried against Qwen is an
    /// HTTP 400), the chat_template_kwargs thinking switches, the hosted DeepSeek /
    /// Z.ai "thinking" object, and the OpenAI Responses "reasoning" object.
    /// </summary>
    public static readonly string[] ModelSpecificRequestKeys =
    {
        "chat_template_kwargs",
        "reasoning_effort",
        "thinking",
        "reasoning",
    };

    /// <summary>
    /// The body to retry with after an auto-switch: the new model name, and the
    /// model-family-specific fields above removed so the server applies its own
    /// defaults for this one reply. The NEXT request is built normally for the
    /// saved model, with its proper reasoning/thinking settings. Messages and
    /// plain sampling fields (temperature, top_p, ...) are kept byte-for-byte.
    /// </summary>
    public static string PrepareRetryJson(string requestJson, string newModel)
    {
        string json = ReplaceModelInRequestJson(requestJson, newModel);
        return LLMSamplingCompat.StripKeys(json, ModelSpecificRequestKeys);
    }

    /// <summary>
    /// Strips the Chat Completions path from a request endpoint so the server's
    /// base URL can be compared with an instance's configured endpoint or used for
    /// "/v1/models". "http://host:8000/v1/chat/completions" -> "http://host:8000";
    /// "http://host:8000/v1/" -> "http://host:8000". Comparison-ready: no trailing slash.
    /// </summary>
    public static string ChatEndpointToBaseUrl(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return "";
        string url = endpoint.Trim().TrimEnd('/');
        const string chatPath = "/chat/completions";
        if (url.EndsWith(chatPath, StringComparison.OrdinalIgnoreCase))
            url = url.Substring(0, url.Length - chatPath.Length);
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url = url.Substring(0, url.Length - 3);
        return url.TrimEnd('/');
    }

    /// <summary>
    /// Builds the Chat Completions URL for an OpenAI-compatible server from whatever the user
    /// typed as the endpoint: "http://host:1234" -> ".../v1/chat/completions";
    /// "http://host:1234/v1" (the form LM Studio, OpenRouter and Groq document, or Z.ai's
    /// ".../api/paas/v4") keeps its version segment once and gets "/chat/completions"; a full
    /// ".../chat/completions" is returned as is. Blindly appending "/v1/chat/completions"
    /// produced ".../v1/v1/chat/completions" (404) for the versioned form.
    /// </summary>
    public static string BuildChatCompletionsUrl(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return "";
        string url = endpoint.Trim().TrimEnd('/');
        if (url.Length == 0) return "";
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        if (Regex.IsMatch(url, @"/v\d+$", RegexOptions.IgnoreCase)) return url + "/chat/completions";
        return url + "/v1/chat/completions";
    }

    /// <summary>
    /// Joins a server base and an API path without doubling a version segment:
    /// ("http://host:8080/v1", "/v1/chat/completions") -> "http://host:8080/v1/chat/completions".
    /// </summary>
    public static string JoinApiPath(string serverAddress, string apiPath)
    {
        string baseUrl = (serverAddress ?? "").Trim().TrimEnd('/');
        string path = apiPath ?? "";
        if (path.Length > 0 && path[0] != '/') path = "/" + path;
        var m = Regex.Match(baseUrl, @"/(v\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && path.StartsWith("/" + m.Groups[1].Value + "/", StringComparison.OrdinalIgnoreCase))
            path = path.Substring(m.Groups[1].Value.Length + 1);
        return baseUrl + path;
    }

    /// <summary>
    /// True when two endpoint strings name the same server base (scheme/host/port/
    /// path prefix), ignoring case, trailing slashes, and a "/v1/chat/completions"
    /// or "/v1" suffix on either side.
    /// </summary>
    public static bool SameServerBase(string a, string b)
    {
        string na = ChatEndpointToBaseUrl(a);
        string nb = ChatEndpointToBaseUrl(b);
        if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb)) return false;
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
}
