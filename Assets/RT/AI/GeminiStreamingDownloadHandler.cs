using SimpleJSON;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Streaming download handler for Google Gemini API responses.
/// Handles SSE format with candidates[0].content.parts[0].text structure.
/// </summary>
public class GeminiStreamingDownloadHandler : DownloadHandlerScript
{
    private Action<string> m_textChunkUpdateCallback;
    private StringBuilder stringBuilder = new StringBuilder();
    private StringBuilder incompleteChunk = new StringBuilder();
    private bool isErrorResponse = false;

    public GeminiStreamingDownloadHandler(Action<string> textChunkUpdateCallback) : base(new byte[1024])
    {
        m_textChunkUpdateCallback = textChunkUpdateCallback;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0)
        {
            Debug.LogWarning("GeminiStreamingDownloadHandler: Received null/empty buffer");
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

        // Process as streaming chunk
        ProcessChunk(text);
        return true;
    }

    protected void ProcessChunk(string chunk)
    {
        incompleteChunk.Append(chunk);
        string fullChunk = incompleteChunk.ToString();
        string[] events = fullChunk.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < events.Length; i++)
        {
            string event_data = events[i].Trim();
            
            // SSE format: "data: {...}"
            if (event_data.StartsWith("data: "))
            {
                string jsonData = event_data.Substring(6); // Remove "data: " prefix
                if (jsonData == "[DONE]")
                {
                    // Stream finished
                    continue;
                }
                ProcessJsonChunk(jsonData);
            }
            // Direct JSON format (non-SSE)
            else if (event_data.StartsWith("{") && event_data.EndsWith("}"))
            {
                ProcessJsonChunk(event_data);
            }
        }

        // Keep any remaining incomplete data
        int lastNewLineIndex = fullChunk.LastIndexOf("\n");
        if (lastNewLineIndex >= 0 && lastNewLineIndex < fullChunk.Length - 1)
        {
            incompleteChunk.Clear();
            incompleteChunk.Append(fullChunk.Substring(lastNewLineIndex + 1));
        }
        else
        {
            incompleteChunk.Clear();
        }
    }

    protected void ProcessJsonChunk(string jsonChunk)
    {
        try
        {
            JSONNode rootNode = JSON.Parse(jsonChunk);
            string content = null;

            // Gemini streaming format: candidates[0].content.parts[0].text
            if (rootNode["candidates"] != null && rootNode["candidates"].Count > 0)
            {
                var candidate = rootNode["candidates"][0];
                if (candidate["content"] != null && candidate["content"]["parts"] != null)
                {
                    var parts = candidate["content"]["parts"];
                    if (parts.Count > 0 && parts[0]["text"] != null)
                    {
                        content = parts[0]["text"];
                    }
                }
            }
            // Also check for thinking/reasoning in modelVersion that has thinkingContent
            else if (rootNode["modelVersion"] != null)
            {
                // This is metadata, not content - ignore
            }
            // Check for usageMetadata (end of stream indicator)
            else if (rootNode["usageMetadata"] != null)
            {
                // This is the final metadata chunk - ignore
            }

            if (content != null)
            {
                stringBuilder.Append(content);
                MainThreadDispatcher.Enqueue(() => m_textChunkUpdateCallback(content));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"GeminiStreamingDownloadHandler: Error processing JSON chunk: {ex.Message}\nChunk: {jsonChunk}");
        }
    }

    protected override void CompleteContent()
    {
        Debug.Log("GeminiStreamingDownloadHandler: Download complete!");
        // Process any remaining data in incompleteChunk
        if (incompleteChunk.Length > 0)
        {
            ProcessChunk("\n"); // Force processing of the last chunk
        }
    }

    public string GetContent()
    {
        return stringBuilder.ToString();
    }

    protected override string GetText()
    {
        return GetContent();
    }

    public bool IsError()
    {
        return isErrorResponse;
    }
}

