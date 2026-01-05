using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;

/// <summary>
/// Utility class to fetch model information from a llama.cpp server.
/// Queries the /models endpoint and parses the response.
/// Supports both single-model and router mode (multiple models).
/// </summary>
public class LlamaCppModelFetcher : MonoBehaviour
{
    private Action<LlamaCppModelInfo, string> _onComplete;
    private Action<LlamaCppModelsInfo, string> _onCompleteMulti;
    private bool _isFetching = false;
    private bool _fetchMultiple = false;

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
    /// Information about multiple models from llama.cpp server (router mode).
    /// </summary>
    public class LlamaCppModelsInfo
    {
        public List<string> modelIds = new List<string>();
        public List<string> modelNames = new List<string>();
        public Dictionary<string, Dictionary<string, string>> modelMetadata = new Dictionary<string, Dictionary<string, string>>();
        
        /// <summary>
        /// True if the server is running in router mode (multiple models available).
        /// </summary>
        public bool IsRouterMode => modelIds.Count > 1;
        
        /// <summary>
        /// Get the first model info for backward compatibility.
        /// </summary>
        public LlamaCppModelInfo GetFirstModel()
        {
            if (modelIds.Count == 0) return null;
            
            var info = new LlamaCppModelInfo
            {
                modelId = modelIds[0],
                modelName = modelNames.Count > 0 ? modelNames[0] : modelIds[0]
            };
            
            if (modelMetadata.ContainsKey(modelIds[0]))
            {
                info.metadata = modelMetadata[modelIds[0]];
            }
            
            return info;
        }
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
        _fetchMultiple = false;
        _isFetching = true;

        StartCoroutine(FetchModelInfoCoroutine(baseUrl));
    }

    /// <summary>
    /// Fetch all models from the llama.cpp server (for router mode support).
    /// </summary>
    /// <param name="baseUrl">The base URL of the llama.cpp server (e.g., http://localhost:8080)</param>
    /// <param name="onComplete">Callback with models info (or null on error) and error message (or null on success)</param>
    public void FetchAllModels(string baseUrl, Action<LlamaCppModelsInfo, string> onComplete)
    {
        if (_isFetching)
        {
            onComplete?.Invoke(null, "Already fetching model info");
            return;
        }

        _onCompleteMulti = onComplete;
        _fetchMultiple = true;
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
                RTConsole.Log("LlamaCppModelFetcher: " + error);
                Debug.LogWarning("LlamaCppModelFetcher: " + error);
                if (_fetchMultiple)
                    _onCompleteMulti?.Invoke(null, error);
                else
                    _onComplete?.Invoke(null, error);
                Destroy(gameObject);
                yield break;
            }

            try
            {
                string responseText = request.downloadHandler.text;
                
                if (_fetchMultiple)
                {
                    // Parse all models for router mode
                    LlamaCppModelsInfo modelsInfo = ParseAllModelsResponse(responseText);
                    
                    if (modelsInfo != null && modelsInfo.modelIds.Count > 0)
                    {
                        string modeStr = modelsInfo.IsRouterMode ? "Router Mode" : "Single Model";
                        RTConsole.Log($"LlamaCppModelFetcher: Found {modelsInfo.modelIds.Count} model(s) ({modeStr})");
                        _onCompleteMulti?.Invoke(modelsInfo, null);
                    }
                    else
                    {
                        _onCompleteMulti?.Invoke(null, "No models found in llama.cpp response");
                    }
                }
                else
                {
                    // Legacy single model parsing
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
            }
            catch (Exception e)
            {
                string error = "Error parsing llama.cpp model response: " + e.Message;
                Debug.LogWarning("LlamaCppModelFetcher: " + error);
                if (_fetchMultiple)
                    _onCompleteMulti?.Invoke(null, error);
                else
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
    /// Parse the JSON response from /models endpoint, extracting ALL models.
    /// Used for router mode support where multiple models may be available.
    /// Expected format: { "models": [ { "name": "...", ... } ], "data": [ { "id": "...", "meta": {...} } ] }
    /// </summary>
    private LlamaCppModelsInfo ParseAllModelsResponse(string json)
    {
        LlamaCppModelsInfo info = new LlamaCppModelsInfo();

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

        // Parse all models from "data" array (OpenAI-compatible format)
        JSONArray dataArray = rootNode["data"]?.AsArray;
        if (dataArray != null)
        {
            foreach (JSONNode dataNode in dataArray)
            {
                if (dataNode == null) continue;
                
                string modelId = dataNode["id"]?.Value ?? "";
                if (string.IsNullOrEmpty(modelId)) continue;
                
                info.modelIds.Add(modelId);
                
                // Try to get a friendly name - use the id if no other name available
                string modelName = modelId;
                
                // Extract metadata if available
                JSONNode meta = dataNode["meta"];
                if (meta != null && meta.IsObject)
                {
                    var metadata = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, JSONNode> kvp in meta.AsObject)
                    {
                        metadata[kvp.Key] = kvp.Value?.Value ?? "";
                    }
                    info.modelMetadata[modelId] = metadata;
                }
                
                info.modelNames.Add(modelName);
            }
        }

        // Also check "models" array for additional model names
        JSONArray modelsArray = rootNode["models"]?.AsArray;
        if (modelsArray != null && info.modelIds.Count == 0)
        {
            // Fallback: use models array if data array was empty
            foreach (JSONNode modelNode in modelsArray)
            {
                if (modelNode == null) continue;
                
                string modelName = modelNode["name"]?.Value ?? "";
                if (string.IsNullOrEmpty(modelName)) continue;
                
                // Extract just the filename from the path
                modelName = ExtractFilename(modelName);
                info.modelIds.Add(modelName);
                info.modelNames.Add(modelName);
            }
        }
        else if (modelsArray != null && modelsArray.Count == info.modelIds.Count)
        {
            // Update model names with friendly names from models array
            for (int i = 0; i < modelsArray.Count && i < info.modelNames.Count; i++)
            {
                string friendlyName = modelsArray[i]?["name"]?.Value;
                if (!string.IsNullOrEmpty(friendlyName))
                {
                    info.modelNames[i] = ExtractFilename(friendlyName);
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
    /// Static helper to create a fetcher and fetch all models (for router mode).
    /// </summary>
    public static void FetchAll(string baseUrl, Action<LlamaCppModelsInfo, string> onComplete)
    {
        GameObject go = new GameObject("LlamaCppModelFetcher");
        LlamaCppModelFetcher fetcher = go.AddComponent<LlamaCppModelFetcher>();
        fetcher.FetchAllModels(baseUrl, onComplete);
    }

    /// <summary>
    /// Fetch all available models as a list of model IDs.
    /// Uses router mode detection - returns all models if multiple are available.
    /// </summary>
    public static void FetchAsList(string baseUrl, Action<List<string>, string> onComplete)
    {
        FetchAll(baseUrl, (modelsInfo, error) =>
        {
            if (modelsInfo != null && modelsInfo.modelIds.Count > 0)
            {
                // Return model names (more user-friendly) if available, otherwise IDs
                var resultList = modelsInfo.modelNames.Count > 0 ? modelsInfo.modelNames : modelsInfo.modelIds;
                onComplete?.Invoke(new List<string>(resultList), null);
            }
            else
            {
                onComplete?.Invoke(new List<string>(), error ?? "No models found");
            }
        });
    }

    /// <summary>
    /// Fetch all models with full info (for detailed router mode support).
    /// </summary>
    public static void FetchModelsInfo(string baseUrl, Action<LlamaCppModelsInfo, string> onComplete)
    {
        FetchAll(baseUrl, onComplete);
    }

    /// <summary>
    /// Fetch models from an OpenAI-compatible server using /v1/models endpoint.
    /// Used for MLX-LM, vLLM, LocalAI, LMStudio, etc.
    /// </summary>
    public static void FetchOpenAICompatibleModels(string baseUrl, Action<LlamaCppModelsInfo, string> onComplete)
    {
        GameObject go = new GameObject("OpenAICompatibleModelFetcher");
        LlamaCppModelFetcher fetcher = go.AddComponent<LlamaCppModelFetcher>();
        fetcher.FetchOpenAICompatible(baseUrl, onComplete);
    }

    /// <summary>
    /// Fetch models from an OpenAI-compatible endpoint (/v1/models).
    /// </summary>
    public void FetchOpenAICompatible(string baseUrl, Action<LlamaCppModelsInfo, string> onComplete)
    {
        if (_isFetching)
        {
            onComplete?.Invoke(null, "Already fetching model info");
            return;
        }

        _onCompleteMulti = onComplete;
        _fetchMultiple = true;
        _isFetching = true;

        StartCoroutine(FetchOpenAICompatibleCoroutine(baseUrl));
    }

    private IEnumerator FetchOpenAICompatibleCoroutine(string baseUrl)
    {
        // Ensure the URL is properly formatted - use /v1/models for OpenAI-compatible servers
        baseUrl = baseUrl.TrimEnd('/');
        string url = baseUrl + "/v1/models";

        RTConsole.Log("OpenAICompatibleModelFetcher: Fetching models from " + url);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;

            yield return request.SendWebRequest();

            _isFetching = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = "Failed to fetch models: " + request.error;
                RTConsole.Log("OpenAICompatibleModelFetcher: " + error);
                Debug.LogWarning("OpenAICompatibleModelFetcher: " + error);
                _onCompleteMulti?.Invoke(null, error);
                Destroy(gameObject);
                yield break;
            }

            try
            {
                string responseText = request.downloadHandler.text;
                RTConsole.Log("OpenAICompatibleModelFetcher: Response: " + responseText);
                
                // Parse OpenAI-style response: {"object": "list", "data": [{"id": "model-name", ...}]}
                LlamaCppModelsInfo modelsInfo = ParseOpenAIModelsResponse(responseText);
                
                if (modelsInfo != null && modelsInfo.modelIds.Count > 0)
                {
                    RTConsole.Log($"OpenAICompatibleModelFetcher: Found {modelsInfo.modelIds.Count} model(s)");
                    _onCompleteMulti?.Invoke(modelsInfo, null);
                }
                else
                {
                    _onCompleteMulti?.Invoke(null, "No models found in response");
                }
            }
            catch (Exception e)
            {
                string error = "Error parsing model response: " + e.Message;
                Debug.LogWarning("OpenAICompatibleModelFetcher: " + error);
                _onCompleteMulti?.Invoke(null, error);
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Parse OpenAI-style /v1/models response.
    /// Expected format: {"object": "list", "data": [{"id": "model-name", "object": "model", "created": 123}]}
    /// </summary>
    private LlamaCppModelsInfo ParseOpenAIModelsResponse(string json)
    {
        LlamaCppModelsInfo info = new LlamaCppModelsInfo();

        JSONNode rootNode = JSON.Parse(json);
        if (rootNode == null)
        {
            Debug.LogWarning("OpenAICompatibleModelFetcher: Failed to parse JSON response");
            return null;
        }

        // OpenAI format uses "data" array with "id" field for model names
        JSONArray dataArray = rootNode["data"]?.AsArray;
        if (dataArray == null || dataArray.Count == 0)
        {
            Debug.LogWarning("OpenAICompatibleModelFetcher: No 'data' array in response");
            return null;
        }

        foreach (JSONNode modelNode in dataArray)
        {
            if (modelNode == null) continue;
            
            string modelId = modelNode["id"]?.Value ?? "";
            if (string.IsNullOrEmpty(modelId)) continue;
            
            info.modelIds.Add(modelId);
            info.modelNames.Add(modelId); // Use ID as display name
        }

        return info;
    }
}

