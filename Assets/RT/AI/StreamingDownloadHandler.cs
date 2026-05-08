using SimpleJSON;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class StreamingDownloadHandler : DownloadHandlerScript
{
    private Action<string> m_textChunkUpdateCallback;
    private StringBuilder stringBuilder = new StringBuilder();
    private string incompleteChunk = "";
    private bool isErrorResponse = false;
    private bool _inReasoningBlock = false;
    private bool _reasoningBlockClosed = false;
    private bool _injectReasoningThinkTags;
    private bool _reasoningBlockFromContent = false;
    private bool _wrapContentUntilThinkClose = false;

    protected override string GetText()
    {
        return stringBuilder.ToString();
    }

    public string GetContentAsString()
    {
        return GetText();
    }

    public StreamingDownloadHandler(Action<string> textChunkUpdateCallback, bool injectReasoningThinkTags = false, bool wrapContentUntilThinkClose = false) : base(new byte[1024])
    {
        m_textChunkUpdateCallback = textChunkUpdateCallback;
        _injectReasoningThinkTags = injectReasoningThinkTags;
        _wrapContentUntilThinkClose = wrapContentUntilThinkClose;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0)
        {
            Debug.LogError("Received a null/empty buffer");
            return false;
        }

        string text = Encoding.UTF8.GetString(data, 0, dataLength);

        // Check if this might be an error response (only check first chunk)
        if (stringBuilder.Length == 0 && text.TrimStart().StartsWith("{\"error"))
        {
            isErrorResponse = true;
            stringBuilder.Append(text);
            return true;
        }

        // If it's an error response, just accumulate the text
        if (isErrorResponse)
        {
            stringBuilder.Append(text);
            return true;
        }

        // Otherwise process as normal streaming chunk
        ProcessChunk(text);
        return true;
    }

    // Process streaming chunks - supports both SSE format (data: {...}) and NDJSON format ({...})
    protected void ProcessChunk(string chunk)
    {
        chunk = incompleteChunk + chunk; // Prepend any previously incomplete chunk
        string[] parts = chunk.Split(new[] { "\n" }, StringSplitOptions.None);

        for (int i = 0; i < parts.Length - 1; i++) // Process all parts except the last one
        {
            string part = parts[i].Trim();
            if (!string.IsNullOrWhiteSpace(part))
            {
                // SSE format: "data: {...}"
                if (part.StartsWith("data: "))
                {
                    string jsonPart = part.Substring(6); // Remove "data: " prefix
                    if (jsonPart != "[DONE]")
                    {
                        ProcessJsonChunk(jsonPart);
                    }
                }
                // NDJSON format (Ollama native): "{...}" - starts with { and ends with }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    ProcessJsonChunk(part);
                }
            }
        }

        // Handle the last part: it could be a complete or incomplete chunk
        string lastPart = parts[parts.Length - 1].Trim();
        if (lastPart.EndsWith("}")) // A simple check to assume completeness
        {
            if (lastPart.StartsWith("data: "))
            {
                string jsonPart = lastPart.Substring(6); // Remove "data: " prefix
                if (jsonPart != "[DONE]")
                {
                    ProcessJsonChunk(jsonPart);
                }
                incompleteChunk = ""; // Reset incomplete chunk as it's now processed
            }
            else if (lastPart.StartsWith("{"))
            {
                // NDJSON format (Ollama native)
                ProcessJsonChunk(lastPart);
                incompleteChunk = ""; // Reset incomplete chunk as it's now processed
            }
            else
            {
                incompleteChunk = lastPart;
            }
        }
        else
        {
            // Last part is incomplete; store it for the next batch
            incompleteChunk = lastPart;
        }
    }

    protected void ProcessJsonChunk(string jsonChunk)
    {
        try
        {
            JSONNode rootNode = JSON.Parse(jsonChunk);
            string content = null;

            if (rootNode != null && rootNode.HasKey("error"))
            {
                isErrorResponse = true;
                string errorText = ExtractErrorText(rootNode["error"]);
                if (!string.IsNullOrEmpty(errorText))
                    stringBuilder.Append(errorText);
                return;
            }

            // Try Ollama native /api/chat format first (message.content)
            // Format: {"message":{"role":"assistant","content":"text"},"done":false}
            if (rootNode != null && rootNode.HasKey("message") && rootNode["message"].HasKey("content") && !rootNode["message"]["content"].IsNull)
            {
                content = rootNode["message"]["content"];
            }
            // Try Chat Completions API format (choices[0].delta.content)
            else if (rootNode != null && rootNode.HasKey("choices") && rootNode["choices"].Count > 0)
            {
                JSONNode choiceNode = rootNode["choices"][0];
                if (choiceNode.HasKey("message") && choiceNode["message"].HasKey("content") && !choiceNode["message"]["content"].IsNull)
                {
                    content = choiceNode["message"]["content"];
                }

                // sglang/vLLM with --reasoning-parser puts thinking in a separate field and
                // the final answer in content. The field name varies by server:
                //   - sglang / older vLLM:  delta.reasoning_content
                //   - vLLM 0.19+ (e.g. Qwen3.6 builds): delta.reasoning
                // When injectReasoningThinkTags is true we inject <think> tags so the app's
                // RemoveThinkTags logic can strip the thinking portion.
                // llama.cpp/Ollama may put main reply in reasoning_content; do NOT wrap for those.
                JSONNode deltaNode = choiceNode.HasKey("delta") ? choiceNode["delta"] : null;
                string mainContent = null;
                string reasoningContent = null;
                if (deltaNode != null)
                {
                    if (deltaNode.HasKey("content") && !deltaNode["content"].IsNull)
                        mainContent = (string)deltaNode["content"];
                    if (deltaNode.HasKey("reasoning_content") && !deltaNode["reasoning_content"].IsNull)
                        reasoningContent = (string)deltaNode["reasoning_content"];
                    else if (deltaNode.HasKey("reasoning") && !deltaNode["reasoning"].IsNull)
                        reasoningContent = (string)deltaNode["reasoning"];
                }

                if (content != null)
                {
                    // Non-streaming OpenAI-compatible response parsed above.
                }
                else if (!string.IsNullOrEmpty(reasoningContent))
                {
                    if (_injectReasoningThinkTags && !_reasoningBlockClosed)
                    {
                        if (!_inReasoningBlock)
                        {
                            _inReasoningBlock = true;
                            _reasoningBlockFromContent = false;
                            content = "<think>" + reasoningContent;
                        }
                        else
                        {
                            content = reasoningContent;
                        }
                    }
                    else
                    {
                        content = reasoningContent;
                    }
                }
                else if (!string.IsNullOrEmpty(mainContent))
                {
                    if (_injectReasoningThinkTags && _inReasoningBlock && _reasoningBlockFromContent)
                    {
                        content = mainContent;
                        if (mainContent.Contains("</think>"))
                        {
                            _inReasoningBlock = false;
                            _reasoningBlockClosed = true;
                            _reasoningBlockFromContent = false;
                        }
                    }
                    else if (_injectReasoningThinkTags && _inReasoningBlock)
                    {
                        _inReasoningBlock = false;
                        _reasoningBlockClosed = true;
                        content = mainContent.Contains("</think>") ? mainContent : "</think>" + mainContent;
                    }
                    else if (_wrapContentUntilThinkClose && !_reasoningBlockClosed && mainContent.Contains("</think>"))
                    {
                        _inReasoningBlock = false;
                        _reasoningBlockClosed = true;
                        content = mainContent.Contains("<think>") ? mainContent : "<think>" + mainContent;
                    }
                    else if (_wrapContentUntilThinkClose && !_reasoningBlockClosed && !mainContent.Contains("<think>"))
                    {
                        // DeepSeek-V4-Flash served by llama.cpp streams reasoning as normal
                        // content until a literal </think>, without an opening <think>.
                        // Add the missing opener so existing strip/display logic can
                        // identify the hidden section.
                        _inReasoningBlock = true;
                        _reasoningBlockFromContent = true;
                        content = "<think>" + mainContent;
                    }
                    else
                    {
                        content = mainContent;
                    }
                }
                else if (deltaNode != null && _injectReasoningThinkTags)
                {
                    JSONNode finishNode = choiceNode["finish_reason"];
                    if (_inReasoningBlock && finishNode != null && !finishNode.IsNull)
                    {
                        _inReasoningBlock = false;
                        _reasoningBlockClosed = true;
                        content = "</think>";
                    }
                }

                // Fallback: try 'text' structure (completions API)
                if (content == null && choiceNode.HasKey("text") && !choiceNode["text"].IsNull)
                {
                    content = choiceNode["text"];
                }
            }
            // Try Responses API format (response.output_text.delta event with delta field)
            else if (rootNode["type"] != null)
            {
                string eventType = rootNode["type"];
                
                // Handle response.output_text.delta event
                if (eventType == "response.output_text.delta")
                {
                    if (rootNode["delta"] != null)
                    {
                        content = rootNode["delta"];
                    }
                }
                // Handle response.content_part.delta event (alternative format)
                else if (eventType == "response.content_part.delta")
                {
                    if (rootNode["delta"] != null && rootNode["delta"]["text"] != null)
                    {
                        content = rootNode["delta"]["text"];
                    }
                }
                // Handle response.output_text.done event (final chunk)
                else if (eventType == "response.output_text.done")
                {
                    // This is the completion event, no content to extract
                }
                // Handle response.done event (final event)
                else if (eventType == "response.done")
                {
                    // This is the completion event, no content to extract
                }
                // Ignore known status/lifecycle events
                else if (eventType == "response.in_progress" || 
                         eventType == "response.created" || 
                         eventType == "response.output_item.added" ||
                         eventType == "response.content_part.added" ||
                         eventType == "response.output_item.done" ||
                         eventType == "response.content_part.done" ||
                         eventType == "response.queued" ||
                         eventType == "response.reasoning.delta" ||
                         eventType == "response.reasoning.done")
                {
                    // Status/lifecycle events, no content to extract
                }
                // Log unknown event types for debugging
                else
                {
                    Debug.Log($"Unknown Responses API event type: {eventType}\nChunk: {jsonChunk}");
                }
            }
            // If we got a chunk but couldn't parse it, log it
            else if (!string.IsNullOrEmpty(jsonChunk) && jsonChunk.Trim() != "{}")
            {
                Debug.Log($"Unrecognized response format:\nChunk: {jsonChunk}");
            }

            if (content != null)
            {
                stringBuilder.Append(content);
                if (m_textChunkUpdateCallback != null)
                    MainThreadDispatcher.Enqueue(() => m_textChunkUpdateCallback(content));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error processing JSON chunk: {ex.Message}\nChunk: {jsonChunk}");
        }
    }

    private static string ExtractErrorText(JSONNode errorNode)
    {
        if (errorNode == null) return "";
        if (errorNode.HasKey("message") && !errorNode["message"].IsNull)
            return errorNode["message"];
        return errorNode.ToString();
    }

    protected override void CompleteContent()
    {
        Debug.Log("Download complete!");
    }

    public string GetContent()
    {
        return stringBuilder.ToString();
    }

    // Add this method to check if we received an error response
    public bool IsError()
    {
        return isErrorResponse;
    }
}
