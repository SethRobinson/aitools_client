using SimpleJSON;
using System;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class StreamingDownloadHandler : DownloadHandlerScript
{
    private Action<string> m_textChunkUpdateCallback;
    private StringBuilder stringBuilder = new StringBuilder();
    private string incompleteChunk = "";
    private bool isErrorResponse = false;
    
    // Add buffering for performance
    private StringBuilder updateBuffer = new StringBuilder();
    private float lastUpdateTime = 0f;
    private const float UPDATE_INTERVAL = 0.05f; // Update UI at most 20 times per second
    private bool hasBufferedContent = false;

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
        lastUpdateTime = Time.time;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0)
        {
            Debug.LogError("Received a null/empty buffer");
            return false;
        }

        string text = Encoding.UTF8.GetString(data, 0, dataLength);

        // Debug logging for Ollama responses
        if (Config.Get() != null && Config.Get().GetGenericLLMIsOllama())
        {
            //Debug.Log($"Ollama raw response: {text}");
        }

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
        
        // Check if we should flush the buffer
        if (hasBufferedContent && (Time.time - lastUpdateTime) >= UPDATE_INTERVAL)
        {
            FlushUpdateBuffer();
        }
        
        return true;
    }
    
    private void FlushUpdateBuffer()
    {
        if (updateBuffer.Length > 0)
        {
            string content = updateBuffer.ToString();
            updateBuffer.Clear();
            MainThreadDispatcher.Enqueue(() => m_textChunkUpdateCallback(content));
            lastUpdateTime = Time.time;
            hasBufferedContent = false;
        }
    }

    protected void ProcessChunk(string chunk)
    {
        chunk = incompleteChunk + chunk; // Prepend any previously incomplete chunk
        string[] parts = chunk.Split(new[] { "\n" }, StringSplitOptions.None);

        for (int i = 0; i < parts.Length - 1; i++) // Process all parts except the last one
        {
            string part = parts[i].Trim();
            if (!string.IsNullOrWhiteSpace(part))
            {
                // Check if it's OpenAI format (starts with "data: ")
                if (part.StartsWith("data: "))
                {
                    string jsonPart = part.Substring(6); // Remove "data: " prefix
                    ProcessJsonChunk(jsonPart);
                }
                // Otherwise try to process as raw JSON (Ollama format)
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    ProcessJsonChunk(part);
                }
            }
        }

        // Handle the last part: it could be a complete or incomplete chunk
        string lastPart = parts[parts.Length - 1].Trim();
        if (!string.IsNullOrWhiteSpace(lastPart))
        {
            if (lastPart.EndsWith("}")) // A simple check to assume completeness
            {
                if (lastPart.StartsWith("data: "))
                {
                    string jsonPart = lastPart.Substring(6); // Remove "data: " prefix
                    ProcessJsonChunk(jsonPart);
                    incompleteChunk = ""; // Reset incomplete chunk as it's now processed
                }
                else if (lastPart.StartsWith("{"))
                {
                    ProcessJsonChunk(lastPart);
                    incompleteChunk = ""; // Reset incomplete chunk as it's now processed
                }
            }
            else
            {
                // Last part is incomplete; store it for the next batch
                incompleteChunk = lastPart;
            }
        }
        else
        {
            incompleteChunk = "";
        }
    }

    protected void ProcessJsonChunk(string jsonChunk)
    {
        try
        {
            // Skip special markers like [DONE]
            if (jsonChunk == "[DONE]")
            {
                return;
            }

            // Debug logging for Ollama
            if (Config.Get() != null && Config.Get().GetGenericLLMIsOllama())
            {
                //Debug.Log($"Processing JSON chunk: {jsonChunk}");
            }

            JSONNode rootNode = JSON.Parse(jsonChunk);
            string content = null;

            // First try OpenAI format (choices[0].delta.content or choices[0].text)
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
            // Try Ollama format (message.content)
            else if (rootNode["message"] != null && rootNode["message"]["content"] != null)
            {
                content = rootNode["message"]["content"];
                if (Config.Get() != null && Config.Get().GetGenericLLMIsOllama())
                {
                    //Debug.Log($"Found Ollama content: {content}");
                }
            }
            // Try direct content field (some APIs use this)
            else if (rootNode["content"] != null)
            {
                content = rootNode["content"];
            }

            if (content != null)
            {
                stringBuilder.Append(content);
                // Buffer the update instead of sending immediately
                updateBuffer.Append(content);
                hasBufferedContent = true;
            }
            
            // Debug logging for unknown formats
            if (content == null && rootNode.Count > 0)
            {
                Debug.LogWarning($"Could not extract content from JSON. Keys: {string.Join(", ", rootNode.Keys)}");
                if (Config.Get() != null && Config.Get().GetGenericLLMIsOllama())
                {
                    Debug.Log($"Failed to extract content. JSON structure: {rootNode.ToString()}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error processing JSON chunk: {ex.Message}\nChunk: {jsonChunk}");
            if (Config.Get() != null && Config.Get().GetGenericLLMIsOllama())
            {
                Debug.Log($"Error processing JSON: {ex.Message}");
            }
        }
    }

    protected override void CompleteContent()
    {
        // Flush any remaining buffered content
        FlushUpdateBuffer();
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

