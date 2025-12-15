using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using SimpleJSON;

/// <summary>
/// Utility class to fetch available models from an Ollama server.
/// Queries the /api/tags endpoint and parses the response.
/// </summary>
public class OllamaModelFetcher : MonoBehaviour
{
    private Action<List<string>, string> _onComplete;
    private bool _isFetching = false;

    /// <summary>
    /// Fetch available models from the Ollama server.
    /// </summary>
    /// <param name="baseUrl">The base URL of the Ollama server (e.g., http://localhost:11434)</param>
    /// <param name="onComplete">Callback with list of model names (or empty list on error) and error message (or null on success)</param>
    public void FetchModels(string baseUrl, Action<List<string>, string> onComplete)
    {
        if (_isFetching)
        {
            onComplete?.Invoke(new List<string>(), "Already fetching models");
            return;
        }

        _onComplete = onComplete;
        _isFetching = true;

        StartCoroutine(FetchModelsCoroutine(baseUrl));
    }

    private IEnumerator FetchModelsCoroutine(string baseUrl)
    {
        // Ensure the URL is properly formatted
        baseUrl = baseUrl.TrimEnd('/');
        string url = baseUrl + "/api/tags";

        Debug.Log("OllamaModelFetcher: Fetching models from " + url);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10; // 10 second timeout

            yield return request.SendWebRequest();

            _isFetching = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = "Failed to fetch Ollama models: " + request.error;
                Debug.LogWarning("OllamaModelFetcher: " + error);
                _onComplete?.Invoke(new List<string>(), error);
                Destroy(gameObject);
                yield break;
            }

            try
            {
                string responseText = request.downloadHandler.text;
                List<string> models = ParseModelsResponse(responseText);

                Debug.Log("OllamaModelFetcher: Found " + models.Count + " models");
                _onComplete?.Invoke(models, null);
            }
            catch (Exception e)
            {
                string error = "Error parsing Ollama models response: " + e.Message;
                Debug.LogWarning("OllamaModelFetcher: " + error);
                _onComplete?.Invoke(new List<string>(), error);
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Parse the JSON response from /api/tags endpoint.
    /// Expected format: { "models": [ { "name": "llama3.3", ... }, ... ] }
    /// </summary>
    private List<string> ParseModelsResponse(string json)
    {
        List<string> models = new List<string>();

        JSONNode rootNode = JSON.Parse(json);
        if (rootNode == null)
        {
            Debug.LogWarning("OllamaModelFetcher: Failed to parse JSON response");
            return models;
        }

        JSONArray modelsArray = rootNode["models"].AsArray;
        if (modelsArray == null)
        {
            Debug.LogWarning("OllamaModelFetcher: No 'models' array in response");
            return models;
        }

        foreach (JSONNode modelNode in modelsArray)
        {
            string name = modelNode["name"];
            if (!string.IsNullOrEmpty(name))
            {
                models.Add(name);
            }
        }

        // Sort alphabetically
        models.Sort();

        return models;
    }

    /// <summary>
    /// Static helper to create a fetcher and start fetching models.
    /// </summary>
    public static void Fetch(string baseUrl, Action<List<string>, string> onComplete)
    {
        GameObject go = new GameObject("OllamaModelFetcher");
        OllamaModelFetcher fetcher = go.AddComponent<OllamaModelFetcher>();
        fetcher.FetchModels(baseUrl, onComplete);
    }
}
