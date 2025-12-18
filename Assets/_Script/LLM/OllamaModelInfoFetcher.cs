using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;

/// <summary>
/// Information about an Ollama model, fetched from /api/show endpoint.
/// </summary>
public class OllamaModelInfo
{
    public string modelName = "";
    public string family = "";
    public string parameterSize = "";
    public string quantizationLevel = "";
    public long contextLength = 8192; // Default context length
    public long embeddingLength = 0;
    public long parameterCount = 0;
    
    /// <summary>
    /// Estimate VRAM usage in GB based on model parameters and context length.
    /// This is a rough estimate: model weights + KV cache for context.
    /// Note: KV cache uses FP16 by default in Ollama (configurable via OLLAMA_KV_CACHE_TYPE).
    /// Based on real-world measurements and Ollama GPU calculator formulas.
    /// </summary>
    public float EstimateVRAMUsage(int desiredContext)
    {
        // Get model size in billions of parameters
        float modelBillions = 0f;
        if (parameterCount > 0)
        {
            modelBillions = parameterCount / 1_000_000_000f;
        }
        else if (!string.IsNullOrEmpty(parameterSize))
        {
            // Parse from parameterSize string (e.g., "8B", "70B", "14.8B")
            string ps = parameterSize.ToUpperInvariant().Replace("B", "").Trim();
            float.TryParse(ps, out modelBillions);
        }
        
        if (modelBillions <= 0) modelBillions = 7f; // Fallback assumption
        
        // Model weights size based on quantization
        float bytesPerParam = 0.5f; // Default to Q4
        string ql = quantizationLevel.ToLowerInvariant();
        if (ql.Contains("q2")) bytesPerParam = 0.3f;
        else if (ql.Contains("q3")) bytesPerParam = 0.4f;
        else if (ql.Contains("q4")) bytesPerParam = 0.5f;
        else if (ql.Contains("q5")) bytesPerParam = 0.625f;
        else if (ql.Contains("q6")) bytesPerParam = 0.75f;
        else if (ql.Contains("q8")) bytesPerParam = 1f;
        else if (ql.Contains("fp16") || ql.Contains("f16")) bytesPerParam = 2f;
        else if (ql.Contains("fp32") || ql.Contains("f32")) bytesPerParam = 4f;
        
        float modelSizeGB = modelBillions * bytesPerParam;
        
        // KV cache estimation using empirical formula based on real-world measurements
        // Modern models use Grouped Query Attention (GQA) which reduces KV cache significantly
        // Real-world observations (FP16 KV cache, default in Ollama):
        // - 24B Q4 model at 64k context uses ~28 GB total (~12 GB model + ~14 GB KV)
        // - This gives: KV = 14 / (24 * 64) ≈ 0.009 GB per billion params per 1K context
        // Formula: KV cache GB ≈ modelBillions * contextK * 0.009
        float contextK = desiredContext / 1024f;
        float kvCacheGB = modelBillions * contextK * 0.009f;
        
        // Add overhead for CUDA context, activations, scratch buffers (~15%)
        float overhead = (modelSizeGB + kvCacheGB) * 0.15f;
        
        return modelSizeGB + kvCacheGB + overhead;
    }
    
    /// <summary>
    /// Format VRAM estimate as a human-readable string.
    /// </summary>
    public string GetVRAMEstimateString(int desiredContext)
    {
        float vramGB = EstimateVRAMUsage(desiredContext);
        if (vramGB < 1f)
            return $"~{(int)(vramGB * 1024)} MB VRAM";
        return $"~{vramGB:F1} GB VRAM";
    }
}

/// <summary>
/// Utility class to fetch detailed model information from an Ollama server.
/// Queries the /api/show endpoint to get context length, parameters, etc.
/// </summary>
public class OllamaModelInfoFetcher : MonoBehaviour
{
    private Action<OllamaModelInfo, string> _onComplete;
    private bool _isFetching = false;

    /// <summary>
    /// Fetch detailed info for a specific model from the Ollama server.
    /// </summary>
    /// <param name="baseUrl">The base URL of the Ollama server (e.g., http://localhost:11434)</param>
    /// <param name="modelName">The name of the model to query</param>
    /// <param name="onComplete">Callback with model info (or null on error) and error message (or null on success)</param>
    public void FetchModelInfo(string baseUrl, string modelName, Action<OllamaModelInfo, string> onComplete)
    {
        if (_isFetching)
        {
            onComplete?.Invoke(null, "Already fetching model info");
            return;
        }

        if (string.IsNullOrEmpty(modelName))
        {
            onComplete?.Invoke(null, "No model name provided");
            Destroy(gameObject);
            return;
        }

        _onComplete = onComplete;
        _isFetching = true;

        StartCoroutine(FetchModelInfoCoroutine(baseUrl, modelName));
    }

    private IEnumerator FetchModelInfoCoroutine(string baseUrl, string modelName)
    {
        baseUrl = baseUrl.TrimEnd('/');
        string url = baseUrl + "/api/show";

        // Build JSON request body
        string jsonBody = $"{{\"name\": \"{modelName}\"}}";

        Debug.Log($"OllamaModelInfoFetcher: Fetching info for model '{modelName}' from {url}");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 15;

            yield return request.SendWebRequest();

            _isFetching = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = $"Failed to fetch Ollama model info: {request.error}";
                Debug.LogWarning("OllamaModelInfoFetcher: " + error);
                _onComplete?.Invoke(null, error);
                Destroy(gameObject);
                yield break;
            }

            try
            {
                string responseText = request.downloadHandler.text;
                OllamaModelInfo modelInfo = ParseModelInfoResponse(responseText, modelName);

                if (modelInfo != null)
                {
                    Debug.Log($"OllamaModelInfoFetcher: Model '{modelName}' - context: {modelInfo.contextLength}, params: {modelInfo.parameterSize}");
                    _onComplete?.Invoke(modelInfo, null);
                }
                else
                {
                    _onComplete?.Invoke(null, "Failed to parse model info");
                }
            }
            catch (Exception e)
            {
                string error = "Error parsing Ollama model info: " + e.Message;
                Debug.LogWarning("OllamaModelInfoFetcher: " + error);
                _onComplete?.Invoke(null, error);
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Parse the JSON response from /api/show endpoint.
    /// </summary>
    private OllamaModelInfo ParseModelInfoResponse(string json, string modelName)
    {
        OllamaModelInfo info = new OllamaModelInfo();
        info.modelName = modelName;

        JSONNode rootNode = JSON.Parse(json);
        if (rootNode == null)
        {
            Debug.LogWarning("OllamaModelInfoFetcher: Failed to parse JSON response");
            return null;
        }

        // Parse model_info section
        JSONNode modelInfoNode = rootNode["model_info"];
        if (modelInfoNode != null)
        {
            // Context length - try multiple possible field names
            if (modelInfoNode["llama.context_length"] != null)
                info.contextLength = modelInfoNode["llama.context_length"].AsLong;
            else if (modelInfoNode["context_length"] != null)
                info.contextLength = modelInfoNode["context_length"].AsLong;
            else if (modelInfoNode["general.context_length"] != null)
                info.contextLength = modelInfoNode["general.context_length"].AsLong;

            // Embedding length
            if (modelInfoNode["llama.embedding_length"] != null)
                info.embeddingLength = modelInfoNode["llama.embedding_length"].AsLong;
            else if (modelInfoNode["embedding_length"] != null)
                info.embeddingLength = modelInfoNode["embedding_length"].AsLong;

            // Parameter count
            if (modelInfoNode["general.parameter_count"] != null)
                info.parameterCount = modelInfoNode["general.parameter_count"].AsLong;
        }

        // Parse details section
        JSONNode detailsNode = rootNode["details"];
        if (detailsNode != null)
        {
            if (detailsNode["family"] != null)
                info.family = detailsNode["family"].Value;
            if (detailsNode["parameter_size"] != null)
                info.parameterSize = detailsNode["parameter_size"].Value;
            if (detailsNode["quantization_level"] != null)
                info.quantizationLevel = detailsNode["quantization_level"].Value;
        }

        // Fallback: check parameters section for context
        JSONNode parametersNode = rootNode["parameters"];
        if (parametersNode != null && info.contextLength <= 0)
        {
            string paramsStr = parametersNode.Value;
            if (!string.IsNullOrEmpty(paramsStr))
            {
                // Parse key-value parameters (format: "num_ctx 8192\nother_param value")
                string[] lines = paramsStr.Split('\n');
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("num_ctx"))
                    {
                        string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out long ctx))
                        {
                            info.contextLength = ctx;
                        }
                    }
                }
            }
        }

        // Ensure we have at least a default context length
        if (info.contextLength <= 0)
            info.contextLength = 8192;

        return info;
    }

    /// <summary>
    /// Static helper to create a fetcher and start fetching model info.
    /// </summary>
    public static void Fetch(string baseUrl, string modelName, Action<OllamaModelInfo, string> onComplete)
    {
        GameObject go = new GameObject("OllamaModelInfoFetcher");
        OllamaModelInfoFetcher fetcher = go.AddComponent<OllamaModelInfoFetcher>();
        fetcher.FetchModelInfo(baseUrl, modelName, onComplete);
    }
}

