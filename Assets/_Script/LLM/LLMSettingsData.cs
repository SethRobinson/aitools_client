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
    Ollama = 3,
    Gemini = 4,
    OpenAICompatible = 5
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
    
    // Ollama-specific: context length settings
    public bool overrideContextLength = false; // If false, use Ollama's default context
    public int contextLength = 8192; // Default 8k context when overriding
    public int maxContextLength = 131072; // Max allowed (will be updated from model info)
    
    // llama.cpp-specific: thinking mode settings (for GLM and similar models)
    public bool enableThinking = true; // User preference for thinking mode (default: enabled)
    
    // llama.cpp-specific: router mode info (not persisted, runtime only)
    [NonSerialized]
    public bool isRouterMode = false; // True if server has multiple models available

    /// <summary>
    /// Check if the current model supports thinking mode.
    /// Detects GLM and DeepSeek models by name.
    /// </summary>
    public bool SupportsThinkingMode()
    {
        if (string.IsNullOrEmpty(selectedModel)) return false;
        string modelLower = selectedModel.ToLowerInvariant();
        // GLM models (GLM-4.5, GLM-4.6, etc.)
        // DeepSeek models (DeepSeek-R1, DeepSeek-V3, etc.)
        return modelLower.Contains("glm") || modelLower.Contains("deepseek");
    }

    public LLMProviderSettings Clone()
    {
        var clone = new LLMProviderSettings
        {
            enabled = this.enabled,
            apiKey = this.apiKey,
            endpoint = this.endpoint,
            selectedModel = this.selectedModel,
            availableModels = new List<string>(this.availableModels),
            extraParams = new List<LLMParm>(),
            overrideContextLength = this.overrideContextLength,
            contextLength = this.contextLength,
            maxContextLength = this.maxContextLength,
            enableThinking = this.enableThinking,
            isRouterMode = this.isRouterMode
        };

        foreach (var parm in this.extraParams)
        {
            clone.extraParams.Add(new LLMParm { _key = parm._key, _value = parm._value });
        }

        return clone;
    }
}

/// <summary>
/// Defines which types of jobs an LLM instance will accept.
/// </summary>
public enum LLMJobMode
{
    Any = 0,           // Accept any job (default)
    BigJobsOnly = 1,   // Only AI Guide, Adventure mode
    SmallJobsOnly = 2  // Only autopic LLM actions
}

/// <summary>
/// Represents a single LLM instance with its own settings and state.
/// Similar to GPUInfo for ComfyUI renderers.
/// </summary>
[Serializable]
public class LLMInstanceInfo
{
    public int instanceID;
    public string name = "";                    // User-assigned name: "My OpenAI", "Local Llama", etc.
    public LLMProvider providerType;            // OpenAI, Anthropic, LlamaCpp, Ollama
    public LLMProviderSettings settings;        // endpoint, apiKey, model, extraParams
    public bool isActive = true;                // Whether this instance is enabled
    public LLMJobMode jobMode = LLMJobMode.Any; // Which job types this instance accepts
    public int maxConcurrentTasks = 1;          // Maximum concurrent tasks this instance can handle
    
    // Runtime state (not persisted)
    [NonSerialized]
    public int activeTasks = 0;                 // Current number of active tasks
    
    /// <summary>
    /// Creates a default instance with the specified provider type.
    /// </summary>
    public static LLMInstanceInfo CreateDefault(LLMProvider provider, int id)
    {
        var instance = new LLMInstanceInfo
        {
            instanceID = id,
            providerType = provider,
            settings = new LLMProviderSettings(),
            isActive = true,
            jobMode = LLMJobMode.Any,
            maxConcurrentTasks = 1
        };
        
        // Set provider-specific defaults
        var modelData = LLMModelData.Load();
        switch (provider)
        {
            case LLMProvider.OpenAI:
                instance.name = "OpenAI";
                instance.settings.endpoint = modelData.openAI.defaultEndpoint;
                instance.settings.availableModels = new List<string>(modelData.openAI.models);
                if (modelData.openAI.models.Count > 0)
                    instance.settings.selectedModel = modelData.openAI.models[0];
                break;
            case LLMProvider.Anthropic:
                instance.name = "Anthropic";
                instance.settings.endpoint = modelData.anthropic.defaultEndpoint;
                instance.settings.availableModels = new List<string>(modelData.anthropic.models);
                if (modelData.anthropic.models.Count > 0)
                    instance.settings.selectedModel = modelData.anthropic.models[0];
                break;
            case LLMProvider.LlamaCpp:
                instance.name = "llama.cpp";
                instance.settings.endpoint = "http://localhost:8080";
                break;
            case LLMProvider.Ollama:
                instance.name = "Ollama";
                instance.settings.endpoint = "http://localhost:11434";
                break;
            case LLMProvider.Gemini:
                instance.name = "Gemini";
                instance.settings.endpoint = modelData.gemini.defaultEndpoint;
                instance.settings.availableModels = new List<string>(modelData.gemini.models);
                if (modelData.gemini.models.Count > 0)
                    instance.settings.selectedModel = modelData.gemini.models[0];
                instance.settings.enableThinking = true; // Gemini supports thinking mode
                break;
            case LLMProvider.OpenAICompatible:
                instance.name = "OpenAI Compatible";
                instance.settings.endpoint = "http://localhost:8080";
                break;
        }
        
        return instance;
    }
    
    /// <summary>
    /// Check if this instance can accept a job of the given type (checks job mode only).
    /// </summary>
    public bool CanAcceptJobType(bool isSmallJob)
    {
        if (!isActive) return false;
        
        switch (jobMode)
        {
            case LLMJobMode.Any:
                return true;
            case LLMJobMode.SmallJobsOnly:
                return isSmallJob;
            case LLMJobMode.BigJobsOnly:
                return !isSmallJob;
            default:
                return true;
        }
    }
    
    /// <summary>
    /// Check if this instance can accept a new job (checks job mode AND capacity).
    /// </summary>
    public bool CanAcceptJob(bool isSmallJob)
    {
        if (!CanAcceptJobType(isSmallJob)) return false;
        return activeTasks < maxConcurrentTasks;
    }
    
    /// <summary>
    /// Check if this instance has capacity for more tasks.
    /// Returns false if maxConcurrentTasks is 0 (disabled).
    /// </summary>
    public bool HasCapacity()
    {
        if (maxConcurrentTasks <= 0) return false; // Disabled
        return activeTasks < maxConcurrentTasks;
    }
    
    /// <summary>
    /// Creates a deep clone of this instance.
    /// </summary>
    public LLMInstanceInfo Clone()
    {
        return new LLMInstanceInfo
        {
            instanceID = this.instanceID,
            name = this.name,
            providerType = this.providerType,
            settings = this.settings?.Clone() ?? new LLMProviderSettings(),
            isActive = this.isActive,
            jobMode = this.jobMode,
            maxConcurrentTasks = this.maxConcurrentTasks,
            activeTasks = 0 // Don't copy runtime state
        };
    }
    
    /// <summary>
    /// Gets a display string for the job mode.
    /// </summary>
    public string GetJobModeDisplayString()
    {
        switch (jobMode)
        {
            case LLMJobMode.Any: return "Any";
            case LLMJobMode.BigJobsOnly: return "Big Jobs";
            case LLMJobMode.SmallJobsOnly: return "Small Jobs";
            default: return "Any";
        }
    }
    
    /// <summary>
    /// Gets a summary display string for the instance list.
    /// </summary>
    public string GetDisplayString()
    {
        string status = isActive ? "Active" : "Inactive";
        string model = !string.IsNullOrEmpty(settings?.selectedModel) ? settings.selectedModel : providerType.ToString();
        string concurrent = maxConcurrentTasks <= 0 ? ", DISABLED" : (maxConcurrentTasks > 1 ? $", Max:{maxConcurrentTasks}" : "");
        return $"{name} ({model}, {status}, {GetJobModeDisplayString()}{concurrent})";
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
    public LLMProviderSettings gemini = new LLMProviderSettings();
    public LLMProviderSettings openAICompatible = new LLMProviderSettings();

    /// <summary>
    /// Creates default settings with sensible defaults for each provider.
    /// </summary>
    public static LLMSettings CreateDefault()
    {
        var settings = new LLMSettings();

        // OpenAI defaults - models loaded from model_data.json
        var modelData = LLMModelData.Load();
        settings.openAI = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = modelData.openAI.defaultEndpoint,
            selectedModel = modelData.openAI.models.Count > 0 ? modelData.openAI.models[0] : "",
            availableModels = new List<string>() // Will be populated from model_data.json
        };

        // Anthropic defaults - models loaded from model_data.json
        settings.anthropic = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = modelData.anthropic.defaultEndpoint,
            selectedModel = modelData.anthropic.models.Count > 0 ? modelData.anthropic.models[0] : "",
            availableModels = new List<string>() // Will be populated from model_data.json
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

        // Gemini defaults - models loaded from model_data.json
        settings.gemini = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = modelData.gemini.defaultEndpoint,
            selectedModel = modelData.gemini.models.Count > 0 ? modelData.gemini.models[0] : "",
            availableModels = new List<string>(), // Will be populated from model_data.json
            enableThinking = true // Gemini supports thinking mode
        };

        // OpenAI Compatible defaults (for MLX-LM, vLLM, LocalAI, LMStudio, etc.)
        settings.openAICompatible = new LLMProviderSettings
        {
            enabled = true,
            apiKey = "",
            endpoint = "http://localhost:8080",
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
            case LLMProvider.Gemini:
                return gemini;
            case LLMProvider.OpenAICompatible:
                return openAICompatible;
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
            ollama = this.ollama.Clone(),
            gemini = this.gemini.Clone(),
            openAICompatible = this.openAICompatible.Clone()
        };
    }
}

/// <summary>
/// Container for multiple LLM instances. This is the new format for config_llm.txt.
/// </summary>
[Serializable]
public class LLMInstancesConfig
{
    public List<LLMInstanceInfo> instances = new List<LLMInstanceInfo>();
    public int defaultInstanceID = 0;
    
    // Legacy settings for migration (will be null after migration)
    public LLMSettings legacySettings = null;
    
    /// <summary>
    /// Creates a default configuration with no instances.
    /// </summary>
    public static LLMInstancesConfig CreateDefault()
    {
        return new LLMInstancesConfig
        {
            instances = new List<LLMInstanceInfo>(),
            defaultInstanceID = 0
        };
    }
    
    /// <summary>
    /// Creates a configuration from legacy single-provider settings.
    /// </summary>
    public static LLMInstancesConfig CreateFromLegacy(LLMSettings legacy)
    {
        var config = new LLMInstancesConfig();
        
        if (legacy == null) return config;
        
        // Create an instance from the active provider
        var instance = new LLMInstanceInfo
        {
            instanceID = 0,
            providerType = legacy.activeProvider,
            settings = legacy.GetActiveProviderSettings()?.Clone() ?? new LLMProviderSettings(),
            isActive = true,
            jobMode = LLMJobMode.Any
        };
        
        // Set name based on provider
        switch (legacy.activeProvider)
        {
            case LLMProvider.OpenAI:
                instance.name = "OpenAI";
                break;
            case LLMProvider.Anthropic:
                instance.name = "Anthropic";
                break;
            case LLMProvider.LlamaCpp:
                instance.name = "llama.cpp";
                break;
            case LLMProvider.Ollama:
                instance.name = "Ollama";
                break;
            case LLMProvider.Gemini:
                instance.name = "Gemini";
                break;
            case LLMProvider.OpenAICompatible:
                instance.name = "OpenAI Compatible";
                break;
        }
        
        config.instances.Add(instance);
        config.defaultInstanceID = 0;
        
        return config;
    }
    
    /// <summary>
    /// Gets the next available instance ID.
    /// </summary>
    public int GetNextInstanceID()
    {
        int maxID = -1;
        foreach (var inst in instances)
        {
            if (inst.instanceID > maxID)
                maxID = inst.instanceID;
        }
        return maxID + 1;
    }
    
    /// <summary>
    /// Gets an instance by ID, or null if not found.
    /// </summary>
    public LLMInstanceInfo GetInstance(int id)
    {
        foreach (var inst in instances)
        {
            if (inst.instanceID == id)
                return inst;
        }
        return null;
    }
    
    /// <summary>
    /// Gets the default instance, or null if none.
    /// </summary>
    public LLMInstanceInfo GetDefaultInstance()
    {
        return GetInstance(defaultInstanceID) ?? (instances.Count > 0 ? instances[0] : null);
    }
    
    /// <summary>
    /// Creates a deep clone.
    /// </summary>
    public LLMInstancesConfig Clone()
    {
        var clone = new LLMInstancesConfig
        {
            defaultInstanceID = this.defaultInstanceID,
            instances = new List<LLMInstanceInfo>()
        };
        
        foreach (var inst in this.instances)
        {
            clone.instances.Add(inst.Clone());
        }
        
        return clone;
    }
}
