using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Data class for provider model configuration (loaded from model_data.json).
/// This file ships with the app and can be updated without affecting user settings.
/// </summary>
[Serializable]
public class LLMProviderModelData
{
    public string defaultEndpoint = "";
    public List<string> models = new List<string>();
}

/// <summary>
/// Root data class for model_data.json.
/// Contains the available models and default endpoints for OpenAI and Anthropic.
/// </summary>
[Serializable]
public class LLMModelData
{
    public LLMProviderModelData openAI = new LLMProviderModelData();
    public LLMProviderModelData anthropic = new LLMProviderModelData();
    public LLMProviderModelData gemini = new LLMProviderModelData();

    private const string MODEL_DATA_FILE = "model_data.json";
    private static LLMModelData _cached;

    /// <summary>
    /// Load model data from model_data.json file.
    /// Returns cached data if already loaded.
    /// </summary>
    public static LLMModelData Load()
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(MODEL_DATA_FILE))
        {
            Debug.LogWarning("LLMModelData: model_data.json not found, using defaults");
            _cached = CreateDefault();
            return _cached;
        }

        try
        {
            string json = File.ReadAllText(MODEL_DATA_FILE);
            _cached = JsonUtility.FromJson<LLMModelData>(json);
            
            if (_cached == null)
            {
                Debug.LogWarning("LLMModelData: Failed to parse model_data.json, using defaults");
                _cached = CreateDefault();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMModelData: Error loading model_data.json: " + e.Message);
            _cached = CreateDefault();
        }

        return _cached;
    }

    /// <summary>
    /// Force reload from file (useful if file was updated).
    /// </summary>
    public static LLMModelData Reload()
    {
        _cached = null;
        return Load();
    }

    /// <summary>
    /// Create default model data matching the hardcoded defaults in LLMSettings.
    /// </summary>
    public static LLMModelData CreateDefault()
    {
        var data = new LLMModelData();

        data.openAI = new LLMProviderModelData
        {
            defaultEndpoint = "https://api.openai.com/v1/responses",
            models = new List<string> { "gpt-5.2", "gpt-5.2-pro" }
        };

        data.anthropic = new LLMProviderModelData
        {
            defaultEndpoint = "https://api.anthropic.com/v1/messages",
            models = new List<string> { "claude-sonnet-4-5", "claude-haiku-4-5", "claude-opus-4-5" }
        };

        data.gemini = new LLMProviderModelData
        {
            defaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/models",
            models = new List<string> { "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-3-pro-preview", "gemini-3-flash-preview" }
        };

        return data;
    }
}

