using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enum representing the available LLM providers.
/// </summary>
public enum LLMProvider
{
    OpenAI = 0,
    Anthropic = 1,
    LlamaCpp = 2,
    Ollama = 3
}

/// <summary>
/// Settings for a single LLM provider.
/// </summary>
[Serializable]
public class LLMProviderSettings
{
    public bool enabled = true;
    public string apiKey = "";
    public string endpoint = "";
    public string selectedModel = "";
    public List<string> availableModels = new List<string>();
    public List<LLMParm> extraParams = new List<LLMParm>();

    public LLMProviderSettings Clone()
    {
        var clone = new LLMProviderSettings
        {
            enabled = this.enabled,
            apiKey = this.apiKey,
            endpoint = this.endpoint,
            selectedModel = this.selectedModel,
            availableModels = new List<string>(this.availableModels),
            extraParams = new List<LLMParm>()
        };

        foreach (var parm in this.extraParams)
        {
            clone.extraParams.Add(new LLMParm { _key = parm._key, _value = parm._value });
        }

        return clone;
    }
}

/// <summary>
/// Root settings object containing all LLM provider configurations.
/// </summary>
[Serializable]
public class LLMSettings
{
    public LLMProvider activeProvider = LLMProvider.OpenAI;
    public LLMProviderSettings openAI = new LLMProviderSettings();
    public LLMProviderSettings anthropic = new LLMProviderSettings();
    public LLMProviderSettings llamaCpp = new LLMProviderSettings();
    public LLMProviderSettings ollama = new LLMProviderSettings();

    /// <summary>
    /// Creates default settings with sensible defaults for each provider.
    /// </summary>
    public static LLMSettings CreateDefault()
    {
        var settings = new LLMSettings();

        // OpenAI defaults
        settings.openAI = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = "https://api.openai.com/v1/responses",
            selectedModel = "gpt-5.2",
            availableModels = new List<string>
            {
                "gpt-5.2",
                "gpt-5.2-pro"
            }
        };

        // Anthropic defaults
        settings.anthropic = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = "https://api.anthropic.com/v1/messages",
            selectedModel = "claude-sonnet-4-5",
            availableModels = new List<string>
            {
                "claude-sonnet-4-5",
                "claude-haiku-4-5",
                "claude-opus-4-5"
            }
        };

        // llama.cpp defaults
        settings.llamaCpp = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = "http://localhost:8080",
            selectedModel = "",
            availableModels = new List<string>()
        };

        // Ollama defaults
        settings.ollama = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = "http://localhost:11434",
            selectedModel = "",
            availableModels = new List<string>()
        };

        return settings;
    }

    /// <summary>
    /// Gets the settings for the specified provider.
    /// </summary>
    public LLMProviderSettings GetProviderSettings(LLMProvider provider)
    {
        switch (provider)
        {
            case LLMProvider.OpenAI:
                return openAI;
            case LLMProvider.Anthropic:
                return anthropic;
            case LLMProvider.LlamaCpp:
                return llamaCpp;
            case LLMProvider.Ollama:
                return ollama;
            default:
                return openAI;
        }
    }

    /// <summary>
    /// Gets the settings for the currently active provider.
    /// </summary>
    public LLMProviderSettings GetActiveProviderSettings()
    {
        return GetProviderSettings(activeProvider);
    }

    /// <summary>
    /// Creates a deep clone of the settings.
    /// </summary>
    public LLMSettings Clone()
    {
        return new LLMSettings
        {
            activeProvider = this.activeProvider,
            openAI = this.openAI.Clone(),
            anthropic = this.anthropic.Clone(),
            llamaCpp = this.llamaCpp.Clone(),
            ollama = this.ollama.Clone()
        };
    }
}
