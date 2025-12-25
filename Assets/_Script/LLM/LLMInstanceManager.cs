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
    // Busy State Management
    // ============================================
    
    /// <summary>
    /// Check if an instance is busy.
    /// </summary>
    public bool IsLLMBusy(int id)
    {
        var instance = GetInstance(id);
        return instance?.isBusy ?? true;
    }
    
    /// <summary>
    /// Set an instance's busy state.
    /// </summary>
    public void SetLLMBusy(int id, bool busy)
    {
        var instance = GetInstance(id);
        if (instance != null)
        {
            instance.isBusy = busy;
        }
    }
    
    /// <summary>
    /// Check if any LLM is free for the given job type.
    /// </summary>
    public bool IsAnyLLMFree(bool isSmallJob = false)
    {
        return GetFreeLLM(isSmallJob) >= 0;
    }
    
    /// <summary>
    /// Get a free LLM instance ID for the given job type.
    /// Returns -1 if none available.
    /// </summary>
    public int GetFreeLLM(bool isSmallJob = false)
    {
        if (_config == null) return -1;
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (instance.isBusy) continue;
            if (!instance.CanAcceptJob(isSmallJob)) continue;
            
            return instance.instanceID;
        }
        
        return -1;
    }
    
    /// <summary>
    /// Get the least busy LLM instance ID for the given job type.
    /// Falls back to any matching instance if all are busy.
    /// Returns -1 if no matching instances exist.
    /// </summary>
    public int GetLeastBusyLLM(bool isSmallJob = false)
    {
        // First try to find a free one
        int freeID = GetFreeLLM(isSmallJob);
        if (freeID >= 0) return freeID;
        
        // Fall back to first matching instance (even if busy)
        if (_config == null) return -1;
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (!instance.CanAcceptJob(isSmallJob)) continue;
            
            return instance.instanceID;
        }
        
        return -1;
    }
    
    /// <summary>
    /// Get the count of busy LLM instances.
    /// </summary>
    public int GetBusyLLMCount()
    {
        if (_config == null) return 0;
        
        int count = 0;
        foreach (var instance in _config.instances)
        {
            if (instance.isActive && instance.isBusy)
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

