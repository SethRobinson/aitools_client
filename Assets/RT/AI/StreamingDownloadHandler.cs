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

    protected override string GetText()
    {
        return stringBuilder.ToString();
    }

    public string GetContentAsString()
    {
        return GetText();
    }

    public StreamingDownloadHandler(Action<string> textChunkUpdateCallback) : base(new byte[1024])
    {
        m_textChunkUpdateCallback = textChunkUpdateCallback;
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

    // Rest of your existing ProcessChunk and ProcessJsonChunk methods remain exactly the same
    protected void ProcessChunk(string chunk)
    {
        chunk = incompleteChunk + chunk; // Prepend any previously incomplete chunk
        string[] parts = chunk.Split(new[] { "\n" }, StringSplitOptions.None);

        for (int i = 0; i < parts.Length - 1; i++) // Process all parts except the last one
        {
            string part = parts[i];
            if (!string.IsNullOrWhiteSpace(part))
            {
                // Ensure it starts with "data: " to consider it a valid JSON chunk
                if (part.StartsWith("data: "))
                {
                    string jsonPart = part.Substring(6); // Remove "data: " prefix
                    ProcessJsonChunk(jsonPart);
                }
            }
        }

        // Handle the last part: it could be a complete or incomplete chunk
        string lastPart = parts[parts.Length - 1];
        if (lastPart.EndsWith("}")) // A simple check to assume completeness
        {
            if (lastPart.StartsWith("data: "))
            {
                string jsonPart = lastPart.Substring(6); // Remove "data: " prefix
                ProcessJsonChunk(jsonPart);
                incompleteChunk = ""; // Reset incomplete chunk as it's now processed
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

            // Try Chat Completions API format first (choices[0].delta.content)
            if (rootNode["choices"] != null && rootNode["choices"].Count > 0)
            {
                // Try to get content from the 'delta' structure
                JSONNode deltaNode = rootNode["choices"][0]["delta"];

                if (deltaNode != null && deltaNode["content"] != null)
                {
                    content = deltaNode["content"];
                }
                // If 'delta' structure isn't present, try 'text' structure
                else if (rootNode["choices"][0]["text"] != null)
                {
                    content = rootNode["choices"][0]["text"];
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
                MainThreadDispatcher.Enqueue(() => m_textChunkUpdateCallback(content));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error processing JSON chunk: {ex.Message}\nChunk: {jsonChunk}");
        }
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