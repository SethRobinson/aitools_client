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

public enum LLMReasoningEffort
{
    Off = 0,
    High = 1,
    Max = 2
}

public static class LLMReasoningEffortUtil
{
    public const string OffValue = "off";
    public const string HighValue = "high";
    public const string MaxValue = "max";

    public static LLMReasoningEffort Parse(string value, LLMReasoningEffort fallback = LLMReasoningEffort.Off)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        switch (value.Trim().ToLowerInvariant())
        {
            case OffValue:
            case "none":
            case "no-think":
            case "nothink":
            case "false":
            case "0":
                return LLMReasoningEffort.Off;
            case HighValue:
            case "think":
            case "thinking":
            case "true":
            case "1":
                return LLMReasoningEffort.High;
            case MaxValue:
            case "maximum":
            case "absolute_max":
            case "absolute-maximum":
            case "2":
                return LLMReasoningEffort.Max;
            default:
                return fallback;
        }
    }

    public static string ToConfigValue(LLMReasoningEffort effort)
    {
        switch (effort)
        {
            case LLMReasoningEffort.Max:
                return MaxValue;
            case LLMReasoningEffort.High:
                return HighValue;
            default:
                return OffValue;
        }
    }

    public static string ToDisplayName(LLMReasoningEffort effort)
    {
        switch (effort)
        {
            case LLMReasoningEffort.Max:
                return "Think Max";
            case LLMReasoningEffort.High:
                return "Think High";
            default:
                return "No-think";
        }
    }
}

public static class LLMReasoningPrompts
{
    public const string DeepSeekMaxReasoningSystemPrompt =
        "Reasoning Effort: Absolute maximum with no shortcuts permitted.\n" +
        "You MUST be very thorough in your thinking and comprehensively decompose the problem to resolve the root cause, rigorously stress-testing your logic against all potential paths, edge cases, and adversarial scenarios.\n" +
        "Explicitly write out your entire deliberation process, documenting every intermediate step, considered alternative, and rejected hypothesis to ensure absolutely no assumption is left unchecked.\n";
}

public static class LLMRequestProfile
{
    public const int DeepSeekNoThinkMaxTokens = 4096;
    public const int DeepSeekThinkHighMaxTokens = 4096;
    public const int DeepSeekThinkMaxMaxTokens = 8000;

    public static bool IsDeepSeekModel(string model)
    {
        return !string.IsNullOrEmpty(model) && model.ToLowerInvariant().Contains("deepseek");
    }

    public static int GetRecommendedMaxTokens(string model, LLMReasoningEffort effort, int fallback)
    {
        if (!IsDeepSeekModel(model))
            return fallback;

        switch (effort)
        {
            case LLMReasoningEffort.Max:
                return DeepSeekThinkMaxMaxTokens;
            case LLMReasoningEffort.High:
                return DeepSeekThinkHighMaxTokens;
            default:
                return DeepSeekNoThinkMaxTokens;
        }
    }

    public static float GetRecommendedTemperature(string model, LLMReasoningEffort effort, float fallback)
    {
        if (!IsDeepSeekModel(model))
            return fallback;
        return effort == LLMReasoningEffort.Off ? 0.6f : 1.0f;
    }

    public static float GetRecommendedTopP(string model, LLMReasoningEffort effort, float fallback)
    {
        if (!IsDeepSeekModel(model))
            return fallback;
        return effort == LLMReasoningEffort.Off ? 0.95f : 1.0f;
    }
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
    public string reasoningEffort = ""; // "", off, high, max. Empty migrates from enableThinking.
    
    // llama.cpp-specific: sampling parameters (optional overrides)
    public bool overrideTemperature = false;
    public float temperature = 0.8f; // Controls randomness (0.0-2.0, default: 0.8)
    
    public bool overrideTopP = false;
    public float topP = 0.9f; // Nucleus sampling threshold (0.0-1.0, default: 0.9)
    
    public bool overrideTopK = false;
    public int topK = 40; // Top-K sampling (0 = disabled, default: 40)
    
    public bool overrideMinP = false;
    public float minP = 0.1f; // Minimum probability threshold (0.0-1.0, default: 0.1)
    
    public bool overrideRepeatPenalty = false;
    public float repeatPenalty = 1.0f; // Penalize repeat sequences (1.0 = disabled, >1.0 = penalize, default: 1.0)

    public bool overridePresencePenalty = false;
    public float presencePenalty = 0.0f; // Penalize tokens already present (0.0 = disabled, 0-2; DavidAU instruct: 1.5)

    public bool overrideFrequencyPenalty = false;
    public float frequencyPenalty = 0.0f; // Penalize tokens by frequency (0.0 = disabled, 0-1)

    public bool overrideRepeatLastN = false;
    public int repeatLastN = 64; // Range of recent tokens the rep/freq/presence penalties look back over (default: 64)

    // llama.cpp-specific: router mode info (not persisted, runtime only)
    [NonSerialized]
    public bool isRouterMode = false; // True if server has multiple models available

    /// <summary>
    /// Check if the current model supports thinking mode.
    /// Detects GLM, DeepSeek, and Qwen models by name.
    /// </summary>
    public bool SupportsThinkingMode()
    {
        if (string.IsNullOrEmpty(selectedModel)) return false;
        string modelLower = selectedModel.ToLowerInvariant();
        return modelLower.Contains("glm") || modelLower.Contains("deepseek") || modelLower.Contains("qwen");
    }

    public LLMReasoningEffort GetReasoningEffort()
    {
        var fallback = enableThinking ? LLMReasoningEffort.High : LLMReasoningEffort.Off;
        return LLMReasoningEffortUtil.Parse(reasoningEffort, fallback);
    }

    public void SetReasoningEffort(LLMReasoningEffort effort)
    {
        reasoningEffort = LLMReasoningEffortUtil.ToConfigValue(effort);
        enableThinking = effort != LLMReasoningEffort.Off;
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
            reasoningEffort = this.reasoningEffort,
            isRouterMode = this.isRouterMode,
            // Sampling parameters
            overrideTemperature = this.overrideTemperature,
            temperature = this.temperature,
            overrideTopP = this.overrideTopP,
            topP = this.topP,
            overrideTopK = this.overrideTopK,
            topK = this.topK,
            overrideMinP = this.overrideMinP,
            minP = this.minP,
            overrideRepeatPenalty = this.overrideRepeatPenalty,
            repeatPenalty = this.repeatPenalty,
            overridePresencePenalty = this.overridePresencePenalty,
            presencePenalty = this.presencePenalty,
            overrideFrequencyPenalty = this.overrideFrequencyPenalty,
            frequencyPenalty = this.frequencyPenalty,
            overrideRepeatLastN = this.overrideRepeatLastN,
            repeatLastN = this.repeatLastN
        };

        foreach (var parm in this.extraParams)
        {
            clone.extraParams.Add(new LLMParm { _key = parm._key, _value = parm._value });
        }

        return clone;
    }
}

/// <summary>
/// TEXT-job size routing for an LLM instance. Vision capability is now an orthogonal
/// flag (<see cref="LLMInstanceInfo.supportsVision"/> / <see cref="LLMInstanceInfo.visionOnly"/>)
/// rather than a job mode - the old enum conflated "can it do images" with "what work do
/// I route here". Only Any/BigJobsOnly/SmallJobsOnly are used going forward.
///
/// VisionJobsOnly/NonVisionOnly are retained ONLY so pre-decoupling integer values still
/// deserialize for the one-time migration in LLMInstanceManager.MigrateJobModes(); they
/// are never set anew. Do not renumber - JsonUtility persists these as ints.
/// </summary>
public enum LLMJobMode
{
    Any = 0,            // Accept any text job (default)
    BigJobsOnly = 1,    // Only big text jobs (AI Guide / Adventure / chat turns)
    SmallJobsOnly = 2,  // Only small text jobs (autopic / caption-delegation one-shots)

    // --- legacy, pre-vision-decoupling; migrated away on load, never assigned now ---
    VisionJobsOnly = 3, // legacy "image jobs only"  -> supportsVision + visionOnly
    NonVisionOnly = 4   // legacy "no image jobs"     -> supportsVision = false
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
    public LLMJobMode jobMode = LLMJobMode.Any; // TEXT-job size routing: Any / BigJobsOnly / SmallJobsOnly

    // Vision capability + routing, decoupled from jobMode (which is now text-only):
    //  - supportsVision: can this model accept image jobs at all (captions, vision chat)?
    //                    Capability gate - an instance with this off never gets image work.
    //  - visionOnly:     reserve this instance for vision; don't route text jobs here (a
    //                    dedicated vision sidecar). Only meaningful when supportsVision is true.
    // Defaults reproduce the old jobMode==Any behavior (accepts everything). Existing configs
    // get both derived from their legacy jobMode by LLMInstanceManager.MigrateJobModes().
    public bool supportsVision = true;
    public bool visionOnly = false;

    public int maxConcurrentTasks = 1;          // Maximum concurrent tasks this instance can handle (per replica)
    
    // Replica/port-increment fan-out: when enabled, this single entry represents N
    // identical instances on consecutive ports starting from the configured endpoint's port.
    public bool useReplicas = false;            // When true, treat as replicaCount instances on incrementing ports
    public int replicaCount = 1;                // Number of replicas (>= 1). Effective parallelism = replicaCount * maxConcurrentTasks
    
    // Runtime state (not persisted)
    [NonSerialized]
    public int activeTasks = 0;                 // Aggregate active task count across all replicas (for legacy/display)
    [NonSerialized]
    public int[] replicaActiveTasks;            // Per-replica active task counter; lazy-allocated to length GetEffectiveReplicaCount()
    
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
            supportsVision = true,
            visionOnly = false,
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
                instance.settings.SetReasoningEffort(LLMReasoningEffort.Off);
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
                instance.settings.SetReasoningEffort(LLMReasoningEffort.Off);
                break;
        }
        
        return instance;
    }
    
    /// <summary>
    /// Check if this instance can accept a job of the given type (checks job mode only).
    /// Legacy overload for backward compatibility - assumes non-vision job.
    /// </summary>
    public bool CanAcceptJobType(bool isSmallJob)
    {
        return CanAcceptJobType(isSmallJob, isVisionJob: false);
    }
    
    /// <summary>
    /// Check if this instance can accept a job of the given type (checks job mode only).
    /// </summary>
    /// <param name="isSmallJob">True for small jobs (autopic), false for big jobs (AI Guide/Adventure)</param>
    /// <param name="isVisionJob">True if the job has images attached</param>
    public bool CanAcceptJobType(bool isSmallJob, bool isVisionJob)
    {
        if (!isActive) return false;

        // Vision capability is the sole gate for image jobs - size routing only ever
        // applied to text. A non-vision instance never gets image work; a vision-capable
        // one accepts it regardless of big/small.
        if (isVisionJob) return supportsVision;

        // Text job: a vision-reserved instance refuses text so the sidecar stays free.
        if (visionOnly) return false;

        switch (jobMode)
        {
            case LLMJobMode.BigJobsOnly:
                return !isSmallJob;
            case LLMJobMode.SmallJobsOnly:
                return isSmallJob;
            case LLMJobMode.Any:
            default:
                return true;
        }
    }
    
    /// <summary>
    /// Check if this instance can accept a new job (checks job mode AND capacity).
    /// Legacy overload for backward compatibility - assumes non-vision job.
    /// </summary>
    public bool CanAcceptJob(bool isSmallJob)
    {
        return CanAcceptJob(isSmallJob, isVisionJob: false);
    }
    
    /// <summary>
    /// Check if this instance can accept a new job (checks job mode AND capacity).
    /// </summary>
    /// <param name="isSmallJob">True for small jobs (autopic), false for big jobs (AI Guide/Adventure)</param>
    /// <param name="isVisionJob">True if the job has images attached</param>
    public bool CanAcceptJob(bool isSmallJob, bool isVisionJob)
    {
        if (!CanAcceptJobType(isSmallJob, isVisionJob)) return false;
        return HasCapacity();
    }
    
    /// <summary>
    /// Check if this instance has capacity for more tasks across all replicas.
    /// Returns false if maxConcurrentTasks is 0 (disabled).
    /// </summary>
    public bool HasCapacity()
    {
        if (maxConcurrentTasks <= 0) return false; // Disabled
        EnsureReplicaActiveTasks();
        int repCount = GetEffectiveReplicaCount();
        for (int i = 0; i < repCount; i++)
        {
            if (replicaActiveTasks[i] < maxConcurrentTasks) return true;
        }
        return false;
    }
    
    /// <summary>
    /// Returns the effective number of replicas: replicaCount when useReplicas is true, else 1.
    /// Always returns at least 1.
    /// </summary>
    public int GetEffectiveReplicaCount()
    {
        if (!useReplicas) return 1;
        return Mathf.Max(1, replicaCount);
    }
    
    /// <summary>
    /// Ensure replicaActiveTasks is allocated and sized to match the effective replica count.
    /// Preserves existing counters when growing; truncates when shrinking.
    /// </summary>
    public void EnsureReplicaActiveTasks()
    {
        int needed = GetEffectiveReplicaCount();
        if (replicaActiveTasks == null || replicaActiveTasks.Length != needed)
        {
            var newArr = new int[needed];
            if (replicaActiveTasks != null)
            {
                int copy = Mathf.Min(replicaActiveTasks.Length, needed);
                for (int i = 0; i < copy; i++) newArr[i] = replicaActiveTasks[i];
            }
            replicaActiveTasks = newArr;
        }
    }
    
    /// <summary>
    /// Total active tasks across all replicas (kept in sync with the legacy activeTasks field).
    /// </summary>
    public int GetTotalActiveTasks()
    {
        if (replicaActiveTasks == null) return activeTasks;
        int total = 0;
        for (int i = 0; i < replicaActiveTasks.Length; i++) total += replicaActiveTasks[i];
        return total;
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
            supportsVision = this.supportsVision,
            visionOnly = this.visionOnly,
            maxConcurrentTasks = this.maxConcurrentTasks,
            useReplicas = this.useReplicas,
            replicaCount = this.replicaCount,
            activeTasks = 0, // Don't copy runtime state
            replicaActiveTasks = null // Don't copy runtime state; lazy-allocated on use
        };
    }
    
    /// <summary>
    /// Gets a display string for the job mode.
    /// </summary>
    public string GetJobModeDisplayString()
    {
        // Dedicated vision sidecar - text routing is irrelevant when it takes no text.
        if (visionOnly && supportsVision) return "Vision only";

        string textPart;
        switch (jobMode)
        {
            case LLMJobMode.BigJobsOnly: textPart = "Big"; break;
            case LLMJobMode.SmallJobsOnly: textPart = "Small"; break;
            default: textPart = "Any"; break;
        }

        // Append capability so the list/snapshot shows whether images route here.
        return supportsVision ? textPart + "+Vision" : textPart;
    }
    
    /// <summary>
    /// Gets a summary display string for the instance list.
    /// </summary>
    public string GetDisplayString()
    {
        string status = isActive ? "Active" : "Inactive";
        string model = !string.IsNullOrEmpty(settings?.selectedModel) ? settings.selectedModel : providerType.ToString();
        string concurrent = maxConcurrentTasks <= 0 ? ", DISABLED" : (maxConcurrentTasks > 1 ? $", Max:{maxConcurrentTasks}" : "");
        string replicas = (useReplicas && replicaCount > 1) ? $", x{replicaCount}" : "";
        return $"{name} ({model}, {status}, {GetJobModeDisplayString()}{concurrent}{replicas})";
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
            availableModels = new List<string>(),
            enableThinking = false,
            reasoningEffort = LLMReasoningEffortUtil.OffValue
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
            availableModels = new List<string>(),
            enableThinking = false,
            reasoningEffort = LLMReasoningEffortUtil.OffValue
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

    // Bumped when the persisted shape changes so one-time migrations run exactly once.
    //   0 / absent = pre-vision-decoupling (jobMode still encodes vision capability).
    // LLMInstanceManager.MigrateJobModes() upgrades such configs on load: it derives
    // supportsVision/visionOnly from the legacy jobMode, then re-saves at CURRENT.
    public const int CURRENT_SCHEMA_VERSION = 1;
    public int schemaVersion = 0;

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
            defaultInstanceID = 0,
            schemaVersion = CURRENT_SCHEMA_VERSION   // fresh config is already in the new shape
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
            jobMode = LLMJobMode.Any,
            supportsVision = true,
            visionOnly = false
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
        config.schemaVersion = CURRENT_SCHEMA_VERSION;  // built fresh with the new fields set

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
        var configuredDefault = GetInstance(defaultInstanceID);
        if (configuredDefault != null && configuredDefault.isActive)
            return configuredDefault;

        foreach (var inst in instances)
        {
            if (inst != null && inst.isActive)
                return inst;
        }

        return configuredDefault ?? (instances.Count > 0 ? instances[0] : null);
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
