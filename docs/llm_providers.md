# LLM providers: routing, request shape, reasoning families, streaming

Deep-dive for `Assets/RT/AI/*` (provider managers, streaming handlers, `LLMModelNotFound.cs` / `LLMModelAutoSwitch`) and the LLM settings stack in `Assets/_Script/LLM/` (`LLMSettingsData.cs`, which also holds `LLMRequestProfile`; `LLMSettingsManager.cs`; `LLMInstanceManager.cs`; `LLMModelData.cs`).

`LLMProvider` values: `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, `OpenAICompatible`.

## Instances and job routing

- `config_llm.txt` (private) holds providers, multiple instances, replicas, model lists, context limits and sampling/reasoning settings. It carries a `schemaVersion`; old configs that encoded vision in `jobMode` (`VisionJobsOnly` / `NonVisionOnly`) are migrated once by `LLMInstanceManager.MigrateJobModes()`.
- Routing is two orthogonal axes: `jobMode` (text-job size: `Any` / `BigJobsOnly` / `SmallJobsOnly`) plus `supportsVision` (accepts image jobs) and `visionOnly` (reserved for vision). Vision jobs go to `visionOnly` instances first, then any `supportsVision` instance.
- New OpenAI/Anthropic/Gemini instances default `supportsVision` on; new llama.cpp/Ollama/OpenAI Compatible instances default it off (local text servers usually reject images unless a real vision model/mmproj is loaded). AI Chat warns in-chat when an image needs vision and no active instance supports it, and surfaces the backend error when a vision-marked instance rejects the request.
- "Vision reasoning" (`LLMProviderSettings.visionReasoningEffort`, "" = same as text) applies only to AI Chat vision sidecars (`DispatchOneShot(..., visionJob: true)` from `TryCaptionBytes` for captions and web verify, plus `inspect_image`), via `settings.ForVisionJob()`. Thinking models can burn 10k+ tokens per caption, so No-think is the fast option. Main turns, `call_llm`, AI Guide and Adventure use the text level. Family clamps still apply.

## Request parameters

- No optional output-token budget: OpenAI / OpenAI Compatible, llama.cpp, Ollama and Gemini requests omit their output-limit field everywhere (AI Chat, sidecars, PicMain `call_llm`, Adventure, AI Guide). Legacy `max_tokens` / `max_new_tokens` / `max_output_tokens` / `max_completion_tokens` / `n_predict` / `num_predict` provider parameters are ignored. Anthropic requires `max_tokens`, so it gets the per-model maximum (`LLMRequestProfile.GetAnthropicMaxOutputTokens`; an unknown name gets the 8K legacy cap).
- Temperature: AI Chat main turns send `temperature` ONLY when the instance's sampling override is ticked; otherwise it is omitted so the server default applies (all providers). DeepSeek keeps its model-card values when not overridden; sidecars/one-shots use a fixed 0.4. AI Guide, Adventure and PicMain `call_llm` take temperature from their own config files / job script.
- `model_data.json` ships cloud model lists and default endpoints; mirror every change in `LLMModelData.CreateDefault`. `models[0]` is the default for NEW instances; existing instances keep their selection and get the shipped list merged in.
- Shipping a new cloud model family also means updating the name-substring branches: `OpenAIRequestProfileResolver.Resolve` (endpoint / reasoning / temperature; unknown names fall back to Chat Completions + temperature), `AnthropicAITextCompletionManager.ModelRejectsSamplingParams` (400 risk) and `LLMRequestProfile.GetAnthropicMaxOutputTokens`. Current (2026-10-13): OpenAI `gpt-6-astra` / `gpt-6.1-sol` / `gpt-6-luna` (Responses API, reasoning, no temperature); Anthropic `claude-opus-5-5` / `claude-fable-5-1` / `claude-sonnet-5-5` / `claude-haiku-5-5` (128K output, no sampling params).

## Request URL building

`LLMModelNotFound.BuildChatCompletionsUrl` is used at every OpenAI-compatible call site (llama.cpp/Ollama join through `JoinApiPath`): `http://host:1234` gets `/v1/chat/completions`; a base already ending in a version segment (`.../v1`, `.../api/paas/v4`, as LM Studio / OpenRouter / Groq / Z.ai document it) gets only `/chat/completions`; a full `.../chat/completions` is used as is. Never blind-append (that produced `/v1/v1/...` 404s).

## Stale model auto-switch (OpenAI Compatible only)

When an OpenAI Compatible server answers model-not-found (vLLM 404 `The model X does not exist`, OpenAI `model_not_found`, Ollama/llama.cpp `model 'x' not found`, FastAPI `detail` bodies), `OpenAITextCompletionManager` calls `LLMModelAutoSwitch.TryResolve`, which fetches that server's `/v1/models` (instance API key as Bearer). If the saved model is really absent:

- every OpenAICompatible instance pointing at that server (any replica port) switches to the FIRST listed model and `config_llm.txt` is saved;
- a toast, an RTConsole line and the static `Switched` event fire (AI Chat shows an amber "Notice" bubble; an open LLM Settings panel patches its working copy so Apply cannot write the dead model back);
- the request is retried once with the new model and the family-specific fields stripped (`chat_template_kwargs`, `reasoning_effort`, `thinking`, `reasoning`: `LLMModelNotFound.PrepareRetryJson`). The retry reuses the first attempt's think-tag injection decision and re-enters the original `LLMDebugLog.PurposeScope`, so `llm_aichat_log.json` shows both requests.

If the model is listed, the fetch fails, or no OpenAICompatible instance uses that server, the original error surfaces. Detection/JSON rewriting is Unity-free (`Assets/RT/AI/LLMModelNotFound.cs`). llama.cpp/Ollama provider instances are not covered.

## Reasoning / thinking control per model family

- `LLMReasoningEffort` is Off/Low/Medium/High/Max (config strings off/low/medium/high/max; "xhigh" parses to High). A NEW OpenAI Compatible / llama.cpp instance starts at Off (`LLMInstanceInfo.CreateDefault`). The Max fallback in `LLMProviderSettings.GetReasoningEffort` only applies to old configs with thinking on and no saved level.
- All five OpenAI-compatible call sites (AI Chat main turn, sidecars via `DispatchOneShot`, AI Guide, PicMain `call_llm`, Adventure) resolve thinking through `LLMRequestProfile.ResolveCompatReasoning(model, settings)`. Extend that; never hand-roll per-site ternaries.
- Adding a family: extend `IsXxxModel` + `HasConfigurableReasoningEffort` + the wire mapping in `LLMRequestProfile`, the branch in `OpenAITextCompletionManager.BuildChatCompleteJSON` (and `TexGenWebUITextCompletionManager` for llama.cpp), and `LLMProviderUI.RebuildReasoningEffortOptions`.
- Families are substring-detected after stripping `-` / `_` / spaces, so HF and GGUF names match.

| Family | Detection | Wire shape | Dropdown |
|---|---|---|---|
| DeepSeek-V4 | `IsDeepSeekV4Model`: `deepseek` + `v4` | Self-hosted: top-level `reasoning_effort` (`low`/`high`/`max`; medium/xhigh round to high) PLUS `chat_template_kwargs {thinking, enable_thinking, reasoning_effort}`. Off is sent explicitly as `thinking: false` (vLLM's `deepseek_v4` mode defaults to thinking at high). Endpoint containing `deepseek.com` (`IsVendorCloudApi`): `"thinking": {"type": "enabled"/"disabled"}` + top-level `reasoning_effort`, no kwargs, never `none`. The SERVER prepends the "Reasoning Effort" paragraph for high/max, so the app never injects it. | No-think / Think Low / Think High (default) / Think Max, on OpenAI Compatible and llama.cpp (llama.cpp gets kwargs only) |
| Older DeepSeek | other `deepseek` names | kwarg `thinking`; Max prepends `LLMReasoningPrompts.DeepSeekMaxReasoningSystemPrompt` | checkbox |
| Qwen Flash-Next | `flash-next` | top-level `reasoning_effort` accepts ONLY `low`/`medium`/`xhigh` (`high`/`minimal`/`max` are HTTP 400). High/Max map to `xhigh`; Off sends `chat_template_kwargs {"enable_thinking": false}` | No-think / Low / Medium / XHigh |
| GLM-5.3 | `IsGlm53Model`: `glm5.3` | Thinking CANNOT be disabled (Z.ai 400s `disabled`; template always opens `<think>`). Levels `low`/`high`/`max` only (default max). Sent as top-level `reasoning_effort` AND `chat_template_kwargs {"enable_thinking": true, "reasoning_effort": ...}` (older llama.cpp only reads the kwarg; `enable_thinking: true` stays because the streaming managers sniff it to wrap `reasoning_content` in think tags). Off becomes `low`, Medium becomes `high`. Endpoints containing `z.ai` / `bigmodel.cn`: `"thinking": {"type": "enabled"}` + top-level `reasoning_effort`, no kwargs (untested live). | Think Low / High / Max (default), no No-think, also on llama.cpp |

- Other GLM/Qwen models keep the thinking checkbox; llama.cpp keeps Off/High/Max for everything else.
- The level is a SOFT steer, not a budget (GLM-5.3 levels produce similar-size think blocks). A served template that prefills text after `<think>` forces thinking at every level: check the rendered prompt with vLLM `POST /tokenize` (`messages` + `chat_template_kwargs` + `return_token_strs`) before blaming the app.
- GLM-5.3-Flash's template is natively multimodal (the full GLM-5.3 template refuses media), so a Flash instance can carry `supportsVision`.

## Streaming download handlers

- `StreamingDownloadHandler` (OpenAI / OpenAI Compatible / llama.cpp / Ollama), `AnthropicStreamingDownloadHandler` and `GeminiStreamingDownloadHandler` decode with a stateful UTF-8 `Decoder` (Unity delivers 1 KB slices cut mid-character) and parse only newline-terminated SSE lines (parsing an unterminated tail duplicates or drops text at slice boundaries).
- A delta can carry BOTH `reasoning` and `content` when it straddles `</think>` (vLLM streams reasoning as `delta.reasoning` in multi-token deltas): close the think block and append the content from the same delta, or the reply loses its opening words.
- `StreamingDownloadHandler` and `GeminiStreamingDownloadHandler` keep a capped raw copy (`GetRawText()`) used as the error body when parsed content is empty (pretty-printed or plain-text HTTP error JSON matches no stream line shape).
