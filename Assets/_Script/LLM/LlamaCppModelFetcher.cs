using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;

/// <summary>
/// Utility class to fetch model information from a llama.cpp server.
/// Queries the /models endpoint and parses the response.
/// </summary>
public class LlamaCppModelFetcher : MonoBehaviour
{
    private Action<LlamaCppModelInfo, string> _onComplete;
    private bool _isFetching = false;

    /// <summary>
    /// Information about a llama.cpp model.
    /// </summary>
    public class LlamaCppModelInfo
    {
        public string modelName = "";
        public string modelId = "";
        public Dictionary<string, string> metadata = new Dictionary<string, string>();
    }

    /// <summary>
    /// Fetch model info from the llama.cpp server.
    /// </summary>
    /// <param name="baseUrl">The base URL of the llama.cpp server (e.g., http://localhost:8080)</param>
    /// <param name="onComplete">Callback with model info (or null on error) and error message (or null on success)</param>
    public void FetchModelInfo(string baseUrl, Action<LlamaCppModelInfo, string> onComplete)
    {
        if (_isFetching)
        {
            onComplete?.Invoke(null, "Already fetching model info");
            return;
        }

        _onComplete = onComplete;
        _isFetching = true;

        StartCoroutine(FetchModelInfoCoroutine(baseUrl));
    }

    private IEnumerator FetchModelInfoCoroutine(string baseUrl)
    {
        // Ensure the URL is properly formatted
        baseUrl = baseUrl.TrimEnd('/');
        string url = baseUrl + "/models";

        RTConsole.Log("LlamaCppModelFetcher: Fetching model info from " + url);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10; // 10 second timeout

            yield return request.SendWebRequest();

            _isFetching = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = "Failed to fetch llama.cpp model info: " + request.error;
                Debug.LogWarning("LlamaCppModelFetcher: " + error);
                _onComplete?.Invoke(null, error);
                Destroy(gameObject);
                yield break;
            }

            try
            {
                string responseText = request.downloadHandler.text;
                LlamaCppModelInfo modelInfo = ParseModelResponse(responseText);

                if (modelInfo != null && !string.IsNullOrEmpty(modelInfo.modelName))
                {
                    RTConsole.Log("LlamaCppModelFetcher: Found model: " + modelInfo.modelName);
                    _onComplete?.Invoke(modelInfo, null);
                }
                else
                {
                    _onComplete?.Invoke(null, "No model found in llama.cpp response");
                }
            }
            catch (Exception e)
            {
                string error = "Error parsing llama.cpp model response: " + e.Message;
                Debug.LogWarning("LlamaCppModelFetcher: " + error);
                _onComplete?.Invoke(null, error);
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Parse the JSON response from /models endpoint.
    /// Expected format: { "models": [ { "name": "...", ... } ], "data": [ { "id": "...", "meta": {...} } ] }
    /// </summary>
    private LlamaCppModelInfo ParseModelResponse(string json)
    {
        LlamaCppModelInfo info = new LlamaCppModelInfo();

        JSONNode rootNode = JSON.Parse(json);
        if (rootNode == null)
        {
            Debug.LogWarning("LlamaCppModelFetcher: Failed to parse JSON response");
            return null;
        }

        // Check if this is a llama.cpp server by looking for expected fields
        bool hasModels = rootNode["models"] != null;
        bool hasData = rootNode["data"] != null;

        if (!hasModels && !hasData)
        {
            Debug.LogWarning("LlamaCppModelFetcher: Response doesn't look like llama.cpp /models endpoint");
            return null;
        }

        // Try to get model name from "models" array
        JSONArray modelsArray = rootNode["models"]?.AsArray;
        if (modelsArray != null && modelsArray.Count > 0)
        {
            JSONNode firstModel = modelsArray[0];
            if (firstModel != null && firstModel["name"] != null)
            {
                // Extract just the filename from the path
                info.modelName = ExtractFilename(firstModel["name"]);
            }
        }

        // Try to get more info from "data" array
        JSONArray dataArray = rootNode["data"]?.AsArray;
        if (dataArray != null && dataArray.Count > 0)
        {
            JSONNode firstData = dataArray[0];
            if (firstData != null)
            {
                // Get model ID
                if (firstData["id"] != null)
                {
                    info.modelId = firstData["id"];
                }

                // Extract metadata
                JSONNode meta = firstData["meta"];
                if (meta != null && meta.IsObject)
                {
                    foreach (KeyValuePair<string, JSONNode> kvp in meta.AsObject)
                    {
                        info.metadata[kvp.Key] = kvp.Value?.Value ?? "";
                    }
                }
            }
        }

        return info;
    }

    /// <summary>
    /// Extract just the filename from a path (handles both / and \ separators).
    /// </summary>
    private static string ExtractFilename(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        
        // Find the last separator (either / or \)
        int lastSlash = path.LastIndexOf('/');
        int lastBackslash = path.LastIndexOf('\\');
        int lastSeparator = Math.Max(lastSlash, lastBackslash);
        
        if (lastSeparator >= 0 && lastSeparator < path.Length - 1)
        {
            return path.Substring(lastSeparator + 1);
        }
        
        return path;
    }

    /// <summary>
    /// Static helper to create a fetcher and start fetching model info.
    /// </summary>
    public static void Fetch(string baseUrl, Action<LlamaCppModelInfo, string> onComplete)
    {
        GameObject go = new GameObject("LlamaCppModelFetcher");
        LlamaCppModelFetcher fetcher = go.AddComponent<LlamaCppModelFetcher>();
        fetcher.FetchModelInfo(baseUrl, onComplete);
    }

    /// <summary>
    /// Wrapper to match the Ollama callback signature for UI compatibility.
    /// Returns a list with a single model name.
    /// </summary>
    public static void FetchAsList(string baseUrl, Action<List<string>, string> onComplete)
    {
        Fetch(baseUrl, (info, error) =>
        {
            if (info != null && !string.IsNullOrEmpty(info.modelName))
            {
                onComplete?.Invoke(new List<string> { info.modelName }, null);
            }
            else
            {
                onComplete?.Invoke(new List<string>(), error ?? "No model found");
            }
        });
    }
}

