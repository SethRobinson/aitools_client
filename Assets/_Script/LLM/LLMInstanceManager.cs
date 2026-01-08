using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Singleton manager for multiple LLM instances. Handles allocation, busy tracking, 
/// and persistence. Similar to how Config manages multiple GPUInfo objects.
/// </summary>
public class LLMInstanceManager : MonoBehaviour
{
    private static LLMInstanceManager _instance;
    private LLMInstancesConfig _config;
    private bool _isInitialized = false;
    
    private const string SETTINGS_FILE_NAME = "config_llm.txt";
    
    /// <summary>
    /// Fired when instances list changes (add/remove/modify).
    /// </summary>
    public event Action InstancesChanged;
    
    public static LLMInstanceManager Get()
    {
        return _instance;
    }
    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
    }
    
    /// <summary>
    /// Initialize the manager and load instances.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;
        
        LoadConfig();
        _isInitialized = true;
    }
    
    /// <summary>
    /// Load configuration from file, handling migration from legacy format.
    /// </summary>
    private void LoadConfig()
    {
        if (!File.Exists(SETTINGS_FILE_NAME))
        {
            // No config file - start with empty config
            _config = LLMInstancesConfig.CreateDefault();
            return;
        }
        
        try
        {
            string json = File.ReadAllText(SETTINGS_FILE_NAME);
            
            // Try to parse as new format first (has "instances" array)
            if (json.Contains("\"instances\""))
            {
                _config = JsonUtility.FromJson<LLMInstancesConfig>(json);
                if (_config == null || _config.instances == null)
                {
                    _config = LLMInstancesConfig.CreateDefault();
                }
            }
            else
            {
                // Legacy format - try to parse as LLMSettings and migrate
                var legacySettings = JsonUtility.FromJson<LLMSettings>(json);
                if (legacySettings != null)
                {
                    _config = LLMInstancesConfig.CreateFromLegacy(legacySettings);
                    RTConsole.Log("LLMInstanceManager: Migrated from legacy single-provider format");
                    SaveConfig(); // Save in new format
                }
                else
                {
                    _config = LLMInstancesConfig.CreateDefault();
                }
            }
            
            RTConsole.Log($"LLMInstanceManager: Loaded {_config.instances.Count} instance(s)");
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMInstanceManager: Error loading config: " + e.Message);
            _config = LLMInstancesConfig.CreateDefault();
        }
    }
    
    /// <summary>
    /// Save current configuration to file.
    /// </summary>
    public void SaveConfig()
    {
        try
        {
            string json = JsonUtility.ToJson(_config, true);
            File.WriteAllText(SETTINGS_FILE_NAME, json);
            RTConsole.Log("LLMInstanceManager: Saved " + _config.instances.Count + " instance(s)");
        }
        catch (Exception e)
        {
            Debug.LogError("LLMInstanceManager: Error saving config: " + e.Message);
        }
    }
    
    /// <summary>
    /// Get a clone of the current config for editing.
    /// </summary>
    public LLMInstancesConfig GetConfigClone()
    {
        return _config?.Clone() ?? LLMInstancesConfig.CreateDefault();
    }
    
    /// <summary>
    /// Apply new config and save.
    /// </summary>
    public void ApplyConfig(LLMInstancesConfig newConfig)
    {
        _config = newConfig;
        SaveConfig();
        NotifyInstancesChanged();
    }
    
    private void NotifyInstancesChanged()
    {
        InstancesChanged?.Invoke();
    }
    
    // ============================================
    // Instance Management
    // ============================================
    
    /// <summary>
    /// Get all instances.
    /// </summary>
    public List<LLMInstanceInfo> GetAllInstances()
    {
        return _config?.instances ?? new List<LLMInstanceInfo>();
    }
    
    /// <summary>
    /// Get the number of instances.
    /// </summary>
    public int GetInstanceCount()
    {
        return _config?.instances.Count ?? 0;
    }
    
    /// <summary>
    /// Get an instance by ID.
    /// </summary>
    public LLMInstanceInfo GetInstance(int id)
    {
        return _config?.GetInstance(id);
    }
    
    /// <summary>
    /// Get the default instance.
    /// </summary>
    public LLMInstanceInfo GetDefaultInstance()
    {
        return _config?.GetDefaultInstance();
    }
    
    /// <summary>
    /// Get the default instance ID.
    /// </summary>
    public int GetDefaultInstanceID()
    {
        return _config?.defaultInstanceID ?? 0;
    }
    
    /// <summary>
    /// Set the default instance ID.
    /// </summary>
    public void SetDefaultInstanceID(int id)
    {
        if (_config != null)
        {
            _config.defaultInstanceID = id;
            SaveConfig();
            NotifyInstancesChanged();
        }
    }
    
    /// <summary>
    /// Add a new instance and return its ID.
    /// </summary>
    public int AddInstance(LLMProvider providerType)
    {
        if (_config == null) _config = LLMInstancesConfig.CreateDefault();
        
        int newID = _config.GetNextInstanceID();
        var instance = LLMInstanceInfo.CreateDefault(providerType, newID);
        _config.instances.Add(instance);
        
        // If this is the first instance, make it default
        if (_config.instances.Count == 1)
        {
            _config.defaultInstanceID = newID;
        }
        
        SaveConfig();
        NotifyInstancesChanged();
        return newID;
    }
    
    /// <summary>
    /// Add an existing instance info object.
    /// </summary>
    public void AddInstance(LLMInstanceInfo instance)
    {
        if (_config == null) _config = LLMInstancesConfig.CreateDefault();
        
        instance.instanceID = _config.GetNextInstanceID();
        _config.instances.Add(instance);
        
        if (_config.instances.Count == 1)
        {
            _config.defaultInstanceID = instance.instanceID;
        }
        
        SaveConfig();
        NotifyInstancesChanged();
    }
    
    /// <summary>
    /// Remove an instance by ID.
    /// </summary>
    public bool RemoveInstance(int id)
    {
        if (_config == null) return false;
        
        for (int i = 0; i < _config.instances.Count; i++)
        {
            if (_config.instances[i].instanceID == id)
            {
                _config.instances.RemoveAt(i);
                
                // If we removed the default, pick a new one
                if (_config.defaultInstanceID == id && _config.instances.Count > 0)
                {
                    _config.defaultInstanceID = _config.instances[0].instanceID;
                }
                
                SaveConfig();
                NotifyInstancesChanged();
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// Update an instance's settings.
    /// </summary>
    public void UpdateInstance(LLMInstanceInfo updatedInstance)
    {
        if (_config == null) return;
        
        for (int i = 0; i < _config.instances.Count; i++)
        {
            if (_config.instances[i].instanceID == updatedInstance.instanceID)
            {
                _config.instances[i] = updatedInstance;
                SaveConfig();
                NotifyInstancesChanged();
                return;
            }
        }
    }
    
    // ============================================
    // Task Tracking (Concurrent Task Management)
    // ============================================
    
    /// <summary>
    /// Check if an instance has capacity for more tasks.
    /// </summary>
    public bool HasCapacity(int id)
    {
        var instance = GetInstance(id);
        return instance?.HasCapacity() ?? false;
    }
    
    /// <summary>
    /// Check if an instance is busy (at or over capacity).
    /// </summary>
    public bool IsLLMBusy(int id)
    {
        var instance = GetInstance(id);
        return instance == null || !instance.HasCapacity();
    }
    
    /// <summary>
    /// Increment the active task count for an instance.
    /// Call this when starting a new LLM request.
    /// </summary>
    public void IncrementActiveTasks(int id)
    {
        var instance = GetInstance(id);
        if (instance != null)
        {
            instance.activeTasks++;
            RTConsole.Log($"LLM instance {id} ({instance.name}): started task, active={instance.activeTasks}/{instance.maxConcurrentTasks}");
            NotifyLLMStatusChanged();
        }
    }
    
    /// <summary>
    /// Decrement the active task count for an instance.
    /// Call this when an LLM request completes.
    /// </summary>
    public void DecrementActiveTasks(int id)
    {
        var instance = GetInstance(id);
        if (instance != null)
        {
            instance.activeTasks = Math.Max(0, instance.activeTasks - 1);
            RTConsole.Log($"LLM instance {id} ({instance.name}): finished task, active={instance.activeTasks}/{instance.maxConcurrentTasks}");
            NotifyLLMStatusChanged();
        }
    }
    
    /// <summary>
    /// Notify GameLogic to update the LLM status label.
    /// </summary>
    private void NotifyLLMStatusChanged()
    {
        var gameLogic = GameLogic.Get();
        if (gameLogic != null)
        {
            gameLogic.UpdateActiveLLMLabel();
        }
    }
    
    /// <summary>
    /// Get the current active task count for an instance.
    /// </summary>
    public int GetActiveTaskCount(int id)
    {
        var instance = GetInstance(id);
        return instance?.activeTasks ?? 0;
    }
    
    /// <summary>
    /// Legacy method for backward compatibility. Sets busy state based on active tasks.
    /// </summary>
    public void SetLLMBusy(int id, bool busy)
    {
        if (busy)
            IncrementActiveTasks(id);
        else
            DecrementActiveTasks(id);
    }
    
    /// <summary>
    /// Check if any LLM has capacity for the given job type (legacy overload, assumes non-vision).
    /// </summary>
    public bool IsAnyLLMFree(bool isSmallJob = false)
    {
        return IsAnyLLMFree(isSmallJob, isVisionJob: false);
    }
    
    /// <summary>
    /// Check if any LLM has capacity for the given job type.
    /// </summary>
    /// <param name="isSmallJob">True for small jobs (autopic), false for big jobs (AI Guide/Adventure)</param>
    /// <param name="isVisionJob">True if the job has images attached</param>
    public bool IsAnyLLMFree(bool isSmallJob, bool isVisionJob)
    {
        return GetFreeLLM(isSmallJob, isVisionJob) >= 0;
    }
    
    /// <summary>
    /// Get the best available LLM instance ID for the given job type (legacy overload, assumes non-vision).
    /// </summary>
    public int GetFreeLLM(bool isSmallJob = false)
    {
        return GetFreeLLM(isSmallJob, isVisionJob: false);
    }
    
    /// <summary>
    /// Get the best available LLM instance ID for the given job type.
    /// Chooses the instance with the lowest utilization ratio (activeTasks / maxConcurrentTasks).
    /// Only returns instances that have capacity available.
    /// Returns -1 if no instance has capacity.
    /// For vision jobs: prefers VisionJobsOnly instances, falls back to Any.
    /// </summary>
    /// <param name="isSmallJob">True for small jobs (autopic), false for big jobs (AI Guide/Adventure)</param>
    /// <param name="isVisionJob">True if the job has images attached</param>
    /// <example>
    /// If instance A is 0/1 (0%) and instance B is 1/4 (25%), returns A.
    /// If instance A is 1/1 (100%) and instance B is 3/4 (75%), returns B.
    /// If both are at capacity, returns -1.
    /// </example>
    public int GetFreeLLM(bool isSmallJob, bool isVisionJob)
    {
        if (_config == null) return -1;
        
        // For vision jobs, first try to find a VisionJobsOnly instance
        if (isVisionJob)
        {
            int visionOnlyID = GetFreeLLMWithMode(isSmallJob, isVisionJob, LLMJobMode.VisionJobsOnly);
            if (visionOnlyID >= 0) return visionOnlyID;
            
            // Fall back to Any mode
            return GetFreeLLMWithMode(isSmallJob, isVisionJob, LLMJobMode.Any);
        }
        
        // For non-vision jobs, use standard matching
        int bestID = -1;
        float bestRatio = float.MaxValue; // Lower is better
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (!instance.CanAcceptJob(isSmallJob, isVisionJob)) continue; // Checks job type AND capacity
            
            // Calculate utilization ratio (0 = empty, approaching 1 = nearly full)
            float ratio = instance.maxConcurrentTasks > 0 
                ? (float)instance.activeTasks / instance.maxConcurrentTasks 
                : float.MaxValue;
            
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                bestID = instance.instanceID;
            }
        }
        
        return bestID;
    }
    
    /// <summary>
    /// Get a free LLM instance with a specific job mode.
    /// </summary>
    private int GetFreeLLMWithMode(bool isSmallJob, bool isVisionJob, LLMJobMode targetMode)
    {
        if (_config == null) return -1;
        
        int bestID = -1;
        float bestRatio = float.MaxValue;
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (instance.jobMode != targetMode) continue;
            if (!instance.CanAcceptJob(isSmallJob, isVisionJob)) continue;
            
            float ratio = instance.maxConcurrentTasks > 0 
                ? (float)instance.activeTasks / instance.maxConcurrentTasks 
                : float.MaxValue;
            
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                bestID = instance.instanceID;
            }
        }
        
        return bestID;
    }
    
    /// <summary>
    /// Get the LLM instance ID with the lowest utilization for the given job type (legacy overload, assumes non-vision).
    /// </summary>
    public int GetLeastBusyLLM(bool isSmallJob = false)
    {
        return GetLeastBusyLLM(isSmallJob, isVisionJob: false);
    }
    
    /// <summary>
    /// Get the LLM instance ID with the lowest utilization for the given job type.
    /// Unlike GetFreeLLM, this returns an instance even if at capacity (for queueing).
    /// Returns -1 if no matching instances exist at all.
    /// For vision jobs: prefers VisionJobsOnly instances, falls back to Any.
    /// </summary>
    /// <param name="isSmallJob">True for small jobs (autopic), false for big jobs (AI Guide/Adventure)</param>
    /// <param name="isVisionJob">True if the job has images attached</param>
    public int GetLeastBusyLLM(bool isSmallJob, bool isVisionJob)
    {
        if (_config == null) return -1;
        
        // For vision jobs, first try to find a VisionJobsOnly instance
        if (isVisionJob)
        {
            int visionOnlyID = GetLeastBusyLLMWithMode(isSmallJob, isVisionJob, LLMJobMode.VisionJobsOnly);
            if (visionOnlyID >= 0) return visionOnlyID;
            
            // Fall back to Any mode
            return GetLeastBusyLLMWithMode(isSmallJob, isVisionJob, LLMJobMode.Any);
        }
        
        // For non-vision jobs, use standard matching
        int bestID = -1;
        float bestRatio = float.MaxValue; // Lower is better
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob)) continue; // Only check job type, not capacity
            
            // Calculate utilization ratio (0 = empty, 1 = full, >1 = over capacity)
            float ratio = instance.maxConcurrentTasks > 0 
                ? (float)instance.activeTasks / instance.maxConcurrentTasks 
                : float.MaxValue;
            
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                bestID = instance.instanceID;
            }
        }
        
        return bestID;
    }
    
    /// <summary>
    /// Get the least busy LLM instance with a specific job mode.
    /// </summary>
    private int GetLeastBusyLLMWithMode(bool isSmallJob, bool isVisionJob, LLMJobMode targetMode)
    {
        if (_config == null) return -1;
        
        int bestID = -1;
        float bestRatio = float.MaxValue;
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (instance.jobMode != targetMode) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob)) continue;
            
            float ratio = instance.maxConcurrentTasks > 0 
                ? (float)instance.activeTasks / instance.maxConcurrentTasks 
                : float.MaxValue;
            
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                bestID = instance.instanceID;
            }
        }
        
        return bestID;
    }
    
    /// <summary>
    /// Get the total count of active tasks across all LLM instances.
    /// </summary>
    public int GetTotalActiveTaskCount()
    {
        if (_config == null) return 0;
        
        int count = 0;
        foreach (var instance in _config.instances)
        {
            if (instance.isActive)
                count += instance.activeTasks;
        }
        return count;
    }
    
    /// <summary>
    /// Get the count of LLM instances that are at capacity.
    /// </summary>
    public int GetBusyLLMCount()
    {
        if (_config == null) return 0;
        
        int count = 0;
        foreach (var instance in _config.instances)
        {
            if (instance.isActive && !instance.HasCapacity())
                count++;
        }
        return count;
    }
    
    /// <summary>
    /// Get the count of active LLM instances.
    /// </summary>
    public int GetActiveLLMCount()
    {
        if (_config == null) return 0;
        
        int count = 0;
        foreach (var instance in _config.instances)
        {
            if (instance.isActive)
                count++;
        }
        return count;
    }
    
    // ============================================
    // Backward Compatibility Helpers
    // ============================================
    
    /// <summary>
    /// Get the provider type for the default instance.
    /// For backward compatibility with code that expects a single active provider.
    /// </summary>
    public LLMProvider GetDefaultProvider()
    {
        var def = GetDefaultInstance();
        return def?.providerType ?? LLMProvider.OpenAI;
    }
    
    /// <summary>
    /// Get the settings for the default instance.
    /// For backward compatibility.
    /// </summary>
    public LLMProviderSettings GetDefaultSettings()
    {
        var def = GetDefaultInstance();
        return def?.settings;
    }
    
    /// <summary>
    /// Get the API key for the default instance.
    /// </summary>
    public string GetDefaultAPIKey()
    {
        return GetDefaultSettings()?.apiKey ?? "";
    }
    
    /// <summary>
    /// Get the endpoint for the default instance.
    /// </summary>
    public string GetDefaultEndpoint()
    {
        return GetDefaultSettings()?.endpoint ?? "";
    }
    
    /// <summary>
    /// Get the model for the default instance.
    /// </summary>
    public string GetDefaultModel()
    {
        return GetDefaultSettings()?.selectedModel ?? "";
    }
    
    /// <summary>
    /// Convert to legacy LLM_Type for backward compatibility.
    /// </summary>
    public LLM_Type GetLegacyLLMType(int instanceID = -1)
    {
        var instance = instanceID >= 0 ? GetInstance(instanceID) : GetDefaultInstance();
        if (instance == null) return LLM_Type.OpenAI_API;
        
        switch (instance.providerType)
        {
            case LLMProvider.OpenAI:
                return LLM_Type.OpenAI_API;
            case LLMProvider.Anthropic:
                return LLM_Type.Anthropic_API;
            case LLMProvider.LlamaCpp:
            case LLMProvider.Ollama:
                return LLM_Type.GenericLLM_API;
            default:
                return LLM_Type.OpenAI_API;
        }
    }
}

