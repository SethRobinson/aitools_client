using SimpleJSON;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AnthropicStreamingDownloadHandler : DownloadHandlerScript
{
    private Action<string> m_textChunkUpdateCallback;
    private StringBuilder stringBuilder = new StringBuilder();
    private StringBuilder incompleteChunk = new StringBuilder();

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

        string text = Encoding.UTF8.GetString(data, 0, dataLength);
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