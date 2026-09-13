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
    // Unity hands ReceiveData 1 KB slices cut at arbitrary byte offsets, so a multi-byte
    // UTF-8 sequence (curly quotes, accents, CJK, emoji) can straddle two calls. A stateful
    // Decoder carries the partial bytes over; decoding each slice on its own turned every
    // straddling character into U+FFFD on both sides.
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();

    private string DecodeChunk(byte[] data, int dataLength)
    {
        int charCount = _utf8Decoder.GetCharCount(data, 0, dataLength, false);
        if (charCount == 0) return "";
        char[] chars = new char[charCount];
        int n = _utf8Decoder.GetChars(data, 0, dataLength, chars, 0, false);
        return new string(chars, 0, n);
    }
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

        string text = DecodeChunk(data, dataLength);
        if (text.Length == 0) return true; // the slice ended inside a multi-byte character; wait for the rest

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

    // Only newline-terminated lines are parsed; the tail stays buffered until its newline
    // arrives (CompleteContent feeds a final "\n"). The old version parsed the unterminated
    // tail too and then kept it as the incomplete chunk, and since SimpleJSON returns a
    // partial tree for input cut outside a string, a line split after "text":"Hello"
    // delivered "Hello" twice.
    protected void ProcessChunk(string chunk)
    {
        incompleteChunk.Append(chunk);
        string buffered = incompleteChunk.ToString();
        int start = 0;
        int newline;
        while ((newline = buffered.IndexOf('\n', start)) >= 0)
        {
            string event_data = buffered.Substring(start, newline - start).Trim();
            start = newline + 1;

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

        incompleteChunk.Clear();
        if (start < buffered.Length)
            incompleteChunk.Append(buffered, start, buffered.Length - start);
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

