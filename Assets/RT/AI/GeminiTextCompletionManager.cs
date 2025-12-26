using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;

/// <summary>
/// Text completion manager for Google Gemini API.
/// Handles API requests with Gemini's unique format and thinking mode support.
/// </summary>
public class GeminiTextCompletionManager : MonoBehaviour
{
    private UnityWebRequest _currentRequest;
    bool m_connectionActive = false;

    /// <summary>
    /// Spawn a chat completion request to Gemini API.
    /// </summary>
    /// <param name="jsonRequest">The JSON request body</param>
    /// <param name="myCallback">Callback when complete</param>
    /// <param name="db">Database for passing data</param>
    /// <param name="gemini_APIKey">Gemini API key</param>
    /// <param name="endpoint">Full endpoint URL including model name</param>
    /// <param name="streamingUpdateChunkCallback">Callback for streaming chunks</param>
    /// <param name="bStreaming">Whether to use streaming</param>
    public bool SpawnChatCompleteRequest(string jsonRequest, Action<RTDB, JSONObject, string> myCallback, RTDB db, string gemini_APIKey, string endpoint,
        Action<string> streamingUpdateChunkCallback = null, bool bStreaming = false)
    {
        if (bStreaming)
        {
            StartCoroutine(GetRequestStreaming(jsonRequest, myCallback, db, gemini_APIKey, endpoint, streamingUpdateChunkCallback));
        }
        else
        {
            StartCoroutine(GetRequest(jsonRequest, myCallback, db, gemini_APIKey, endpoint));
        }
        return true;
    }

    /// <summary>
    /// Build Gemini API request JSON from chat lines.
    /// </summary>
    /// <param name="lines">Queue of chat lines</param>
    /// <param name="max_tokens">Maximum output tokens</param>
    /// <param name="temperature">Temperature for generation</param>
    /// <param name="model">Model name (e.g., gemini-2.5-pro)</param>
    /// <param name="stream">Whether to stream response</param>
    /// <param name="enableThinking">Whether to enable thinking mode</param>
    /// <returns>JSON request string</returns>
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 8192, float temperature = 1.0f, string model = "gemini-2.5-pro", bool stream = false, bool enableThinking = true)
    {
        // Build contents array in Gemini format
        // Gemini uses: { "role": "user"|"model", "parts": [{"text": "..."}] }
        // System prompts need special handling - they go in systemInstruction
        
        string systemInstruction = "";
        var contentsArray = new JSONArray();
        
        foreach (GTPChatLine obj in lines)
        {
            if (obj._role == "system")
            {
                // Accumulate system prompts
                if (!string.IsNullOrEmpty(systemInstruction))
                {
                    systemInstruction += "\n\n";
                }
                systemInstruction += obj._content;
            }
            else
            {
                // Convert role names: assistant -> model
                string geminiRole = obj._role == "assistant" ? "model" : obj._role;
                
                var contentObj = new JSONObject();
                contentObj["role"] = geminiRole;
                var partsArray = new JSONArray();
                var textPart = new JSONObject();
                textPart["text"] = obj._content;
                partsArray.Add(textPart);
                contentObj["parts"] = partsArray;
                contentsArray.Add(contentObj);
            }
        }

        // Build the full JSON request using SimpleJSON for proper escaping
        var requestObj = new JSONObject();
        
        // Add system instruction if present
        if (!string.IsNullOrEmpty(systemInstruction))
        {
            var systemObj = new JSONObject();
            var systemParts = new JSONArray();
            var systemText = new JSONObject();
            systemText["text"] = systemInstruction;
            systemParts.Add(systemText);
            systemObj["parts"] = systemParts;
            requestObj["systemInstruction"] = systemObj;
        }
        
        // Add contents
        requestObj["contents"] = contentsArray;
        
        // Add generation config
        var genConfig = new JSONObject();
        genConfig["temperature"] = temperature;
        genConfig["maxOutputTokens"] = max_tokens;
        
        // Check if model supports thinking (gemini-2.5 and gemini-3 models)
        string modelLower = model.ToLowerInvariant();
        bool supportsThinking = modelLower.Contains("gemini-2.5") || modelLower.Contains("gemini-3");
        
        if (supportsThinking)
        {
            // Only include thinkingConfig for models that support it
            var thinkingConfig = new JSONObject();
            // thinkingBudget: 0 = disabled, positive number = token budget
            // For enabling, we omit thinkingBudget or set a positive value
            if (!enableThinking)
            {
                thinkingConfig["thinkingBudget"] = 0;
            }
            // When enabling, we don't set thinkingBudget to let the model decide dynamically
            // Or we could set a reasonable default like 8192
            else
            {
                thinkingConfig["thinkingBudget"] = 8192; // Default thinking budget when enabled
            }
            genConfig["thinkingConfig"] = thinkingConfig;
        }
        
        requestObj["generationConfig"] = genConfig;

        return requestObj.ToString();
    }

    /// <summary>
    /// Build the full endpoint URL for a Gemini model.
    /// </summary>
    /// <param name="baseEndpoint">Base API endpoint</param>
    /// <param name="model">Model name</param>
    /// <param name="stream">Whether streaming is enabled</param>
    /// <returns>Full endpoint URL</returns>
    public static string BuildEndpointUrl(string baseEndpoint, string model, bool stream)
    {
        // Format: https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
        // Or for streaming: https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse
        string action = stream ? "streamGenerateContent" : "generateContent";
        string endpoint = baseEndpoint.TrimEnd('/');
        string url = $"{endpoint}/{model}:{action}";
        
        // For streaming, append ?alt=sse to get Server-Sent Events format
        if (stream)
        {
            url += "?alt=sse";
        }
        
        return url;
    }

    IEnumerator GetRequest(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string gemini_APIKey, string endpoint)
    {
        m_connectionActive = true;

#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("gemini_request_sent.json", json);
#endif

        using (_currentRequest = UnityWebRequest.PostWwwForm(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            _currentRequest.SetRequestHeader("Content-Type", "application/json");
            _currentRequest.SetRequestHeader("x-goog-api-key", gemini_APIKey);
            
            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                Debug.Log("Gemini API Error: " + msg);
                
                string responseBody = "";
                try
                {
                    responseBody = _currentRequest.downloadHandler.text;
                }
                catch { }
                
                Debug.Log("Gemini Response: " + responseBody);
                
#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("gemini_last_error.json", responseBody);
#endif
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {
#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("gemini_response.json", _currentRequest.downloadHandler.text);
#endif

                JSONNode rootNode = JSON.Parse(_currentRequest.downloadHandler.text);
                yield return null;

                m_connectionActive = false;

                // Extract text from Gemini response format
                string responseText = "";
                if (rootNode["candidates"] != null && rootNode["candidates"].Count > 0)
                {
                    var candidate = rootNode["candidates"][0];
                    if (candidate["content"] != null && candidate["content"]["parts"] != null)
                    {
                        var parts = candidate["content"]["parts"];
                        if (parts.Count > 0 && parts[0]["text"] != null)
                        {
                            responseText = parts[0]["text"];
                        }
                    }
                }

                db.Set("status", "success");
                db.Set("response_text", responseText);
                myCallback.Invoke(db, (JSONObject)rootNode, responseText);
            }
        }
    }

    IEnumerator GetRequestStreaming(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string gemini_APIKey, string endpoint,
        Action<string> updateChunkCallback)
    {
        m_connectionActive = true;

#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("gemini_request_sent.json", json);
#endif

        using (_currentRequest = UnityWebRequest.PostWwwForm(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            var downloadHandler = new GeminiStreamingDownloadHandler(updateChunkCallback);
            _currentRequest.downloadHandler = downloadHandler;

            _currentRequest.SetRequestHeader("Content-Type", "application/json");
            _currentRequest.SetRequestHeader("x-goog-api-key", gemini_APIKey);
            
            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                Debug.Log("Gemini Streaming Error: " + msg);
                
                string errorBody = downloadHandler.GetContent();
                if (string.IsNullOrEmpty(errorBody))
                {
                    try
                    {
                        errorBody = _currentRequest.downloadHandler.text;
                    }
                    catch { }
                }
                
                Debug.Log("Gemini Error Response: " + errorBody);
                
#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("gemini_last_error.json", errorBody);
#endif
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {
#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("gemini_streaming_response.json", downloadHandler.GetContent());
#endif
                m_connectionActive = false;

                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContent());
            }
        }
    }

    public bool IsRequestActive()
    {
        return m_connectionActive;
    }

    public void CancelCurrentRequest()
    {
        if (m_connectionActive)
        {
            m_connectionActive = false;
            if (_currentRequest != null)
                _currentRequest.Abort();
            _currentRequest = null;
            Debug.Log("Gemini request aborted.");
        }
    }
}

