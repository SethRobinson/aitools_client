using SimpleJSON;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AnthropicStreamingDownloadHandler : DownloadHandlerScript
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
    // Capture every raw byte we receive so HTTP error responses (e.g. 400 with a JSON body)
    // can still be inspected even though they don't follow the SSE event format.
    private StringBuilder rawResponse = new StringBuilder();

    public AnthropicStreamingDownloadHandler(Action<string> textChunkUpdateCallback) : base(new byte[1024])
    {
        m_textChunkUpdateCallback = textChunkUpdateCallback;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0)
        {
            Debug.LogWarning("Received a null/empty buffer");
            return false;
        }

        string text = DecodeChunk(data, dataLength);
        if (text.Length == 0) return true; // the slice ended inside a multi-byte character; wait for the rest
        rawResponse.Append(text);
        ProcessChunk(text);
        return true;
    }

    /// <summary>
    /// Returns the raw response bytes as text. Useful for logging error bodies that
    /// don't follow the SSE format (like HTTP 4xx / 5xx error JSON from Anthropic).
    /// </summary>
    public string GetRawResponse()
    {
        return rawResponse.ToString();
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
            if (rootNode["type"] == "content_block_delta" && rootNode["delta"] != null && rootNode["delta"]["text"] != null)
            {
                string content = rootNode["delta"]["text"];
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
}