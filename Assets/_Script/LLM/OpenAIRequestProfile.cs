using System;

/// <summary>
/// Resolved request shape for an OpenAI (or OpenAI-shaped) chat completion call.
///
/// The four LLM call sites in this project (AI Chat, AIGuide, PicMain, Adventure)
/// used to each carry their own copy of the "decide useResponsesAPI / reasoning /
/// temperature / endpoint from the model name" block, and they drifted whenever a
/// new model shipped (gpt-5.5 in particular). This struct is the single answer to
/// that question; populate it once via <see cref="OpenAIRequestProfileResolver.Resolve"/>
/// and feed the fields straight into <c>OpenAITextCompletionManager.BuildChatCompleteJSON</c>
/// and <c>SpawnChatCompleteRequest</c>.
/// </summary>
public struct OpenAIRequestProfile
{
    /// <summary>
    /// True when we should hit OpenAI's /v1/responses endpoint instead of the
    /// classic /v1/chat/completions. Reasoning models (gpt-5.2, gpt-5.5, gpt-5.6, o3, o4) want this.
    /// </summary>
    public bool useResponsesAPI;

    /// <summary>
    /// Full URL to POST to. Already accounts for both the model-driven default
    /// (responses vs chat/completions) and any custom non-OpenAI endpoint override.
    /// </summary>
    public string endpoint;

    /// <summary>
    /// Whether to emit a "reasoning" block in the JSON body. Mirrors the
    /// <c>isReasoningModel</c> argument of BuildChatCompleteJSON.
    /// </summary>
    public bool isReasoningModel;

    /// <summary>
    /// Whether to include the "temperature" field at all. gpt-5.6 / 5.5 / 5.2 / o-series
    /// reject temperature on the Responses API; mini/nano use a fixed temp=1.
    /// </summary>
    public bool includeTemperature;

    /// <summary>
    /// Reasoning-effort string ("low" / "medium" / "high"), or null when none should
    /// be emitted.
    /// </summary>
    public string reasoningEffort;

    /// <summary>
    /// Non-null only when the user has pointed the OpenAI provider at a non-OpenAI
    /// (Chat-Completions-compatible) server that honours the <c>enable_thinking</c>
    /// chat_template_kwarg.
    /// </summary>
    public bool? enableThinking;
}

/// <summary>
/// Single source of truth for "given this model name (and optional custom endpoint),
/// what does the request need to look like?".
///
/// IMPORTANT: when adding support for a new OpenAI model family, edit this file
/// and nowhere else - the four call sites (AI Chat, AIGuide, PicMain, Adventure)
/// all flow through <see cref="Resolve"/>.
/// </summary>
public static class OpenAIRequestProfileResolver
{
    private const string OpenAIChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string OpenAIResponsesEndpoint = "https://api.openai.com/v1/responses";

    /// <summary>
    /// Decide the full request profile for a given model. Pass the user's
    /// LLMProviderSettings (may be null) so the helper can also detect a custom
    /// non-OpenAI endpoint and apply the replica port offset.
    /// </summary>
    public static OpenAIRequestProfile Resolve(string model, LLMProviderSettings settings, int replicaIndex)
    {
        // Conservative defaults: behave like a vanilla Chat Completions request.
        // Anything we don't recognize keeps working unchanged.
        var profile = new OpenAIRequestProfile
        {
            useResponsesAPI = false,
            endpoint = OpenAIChatCompletionsEndpoint,
            isReasoningModel = false,
            includeTemperature = true,
            reasoningEffort = null,
            enableThinking = null
        };

        string m = model ?? "";

        if (m.Contains("gpt-5"))
        {
            // Default for the gpt-5 line: Responses API, no reasoning, with temperature.
            // Specific subfamilies override below.
            profile.useResponsesAPI = true;
            profile.endpoint = OpenAIResponsesEndpoint;

            if (m.Contains("gpt-5.6"))
            {
                // gpt-5.6 (alias of gpt-5.6-sol) plus the -sol/-terra/-luna variants.
                // Reasoning model on the Responses API; temperature is not supported.
                // OpenAI's default reasoning effort for the 5.6 line is "medium".
                profile.isReasoningModel = true;
                profile.includeTemperature = false;
                profile.reasoningEffort = "medium";
            }
            else if (m.Contains("gpt-5.5-pro"))
            {
                profile.isReasoningModel = true;
                profile.includeTemperature = false;
                profile.reasoningEffort = "high";
            }
            else if (m.Contains("gpt-5.5"))
            {
                // gpt-5.5 defaults to medium reasoning effort per OpenAI docs.
                profile.isReasoningModel = true;
                profile.includeTemperature = false;
                profile.reasoningEffort = "medium";
            }
            else if (m.Contains("gpt-5.2-pro"))
            {
                // No longer shipped in model_data.json, but kept here so users whose
                // saved instance still has gpt-5.2-pro selected don't silently regress
                // to a wrong request shape on next launch.
                profile.isReasoningModel = true;
                profile.includeTemperature = false;
                profile.reasoningEffort = "high";
            }
            else if (m.Contains("gpt-5.2"))
            {
                profile.isReasoningModel = true;
                profile.includeTemperature = false;
                profile.reasoningEffort = "medium";
            }
            else if (m.Contains("gpt-5-mini") || m.Contains("gpt-5-nano"))
            {
                // Mini/nano go through Chat Completions and use a fixed temp=1
                // (the "temperature" param itself is rejected).
                profile.useResponsesAPI = false;
                profile.endpoint = OpenAIChatCompletionsEndpoint;
                profile.isReasoningModel = false;
                profile.includeTemperature = false;
                profile.reasoningEffort = null;
            }
            // else: base gpt-5 keeps the (Responses API, no reasoning, with temperature) defaults set above.
        }

        // Custom (non-OpenAI) endpoint override. This kicks in when the user has
        // pointed the OpenAI provider at a self-hosted Chat-Completions-compatible
        // server (vLLM, sglang, LMStudio, etc.). In that case we always go through
        // Chat Completions, apply the replica port offset, and forward the user's
        // enable_thinking preference.
        if (settings != null)
        {
            string settingsEndpoint = settings.endpoint ?? "";
            if (!string.IsNullOrEmpty(settingsEndpoint) && !settingsEndpoint.Contains("api.openai.com"))
            {
                profile.useResponsesAPI = false;
                profile.enableThinking = settings.enableThinking;
                string customEndpoint = LLMInstanceManager.ApplyReplicaPortOffset(settingsEndpoint, replicaIndex);
                customEndpoint = customEndpoint.TrimEnd('/');
                if (!customEndpoint.EndsWith("/v1/chat/completions"))
                    customEndpoint += "/v1/chat/completions";
                profile.endpoint = customEndpoint;
            }
        }

        return profile;
    }

    /// <summary>
    /// Lightweight check for places that only need to know whether a model uses
    /// the Responses API (e.g. UI affordances that warn images won't be sent).
    /// Equivalent to <c>Resolve(model, null, 0).useResponsesAPI</c> but cheaper
    /// and clearer at the call site.
    /// </summary>
    public static bool UsesResponsesAPI(string model)
    {
        string m = model ?? "";
        if (m.Contains("gpt-5"))
        {
            if (m.Contains("gpt-5-mini") || m.Contains("gpt-5-nano")) return false;
            return true;
        }
        return false;
    }
}
