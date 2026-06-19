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

            // One-time migration: pre-decoupling configs encode vision capability inside
            // jobMode. Derive supportsVision/visionOnly BEFORE anything reads routing.
            bool changed = MigrateJobModes();

            // Merge any newly-shipped cloud models from model_data.json into existing instances
            // so users see new models (gpt-5.5, claude-opus-4-7, gemini-3.1-pro, etc.) without
            // having to recreate their configurations.
            if (RefreshCloudModelsFromModelData()) changed = true;

            if (changed) SaveConfig();
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMInstanceManager: Error loading config: " + e.Message);
            _config = LLMInstancesConfig.CreateDefault();
        }
    }

    /// <summary>
    /// Walk every cloud-provider instance (OpenAI / Anthropic / Gemini) and merge the latest
    /// models from model_data.json into its availableModels list. The shipped list goes first
    /// (so newly-released models surface at the top of the dropdown), and any extra models
    /// the user already had (custom typed entries, deprecated names, etc.) are appended after
    /// to preserve their setup. The currently selectedModel is never changed here.
    ///
    /// Local providers (Ollama / LlamaCpp / OpenAICompatible) are skipped — their model lists
    /// come from the server itself, not from model_data.json.
    /// </summary>
    /// <returns>True if any instance's availableModels list was modified.</returns>
    private bool RefreshCloudModelsFromModelData()
    {
        if (_config == null || _config.instances == null || _config.instances.Count == 0)
            return false;

        var modelData = LLMModelData.Load();
        bool anyChanged = false;

        foreach (var instance in _config.instances)
        {
            if (instance == null || instance.settings == null) continue;

            List<string> shippedModels = null;
            switch (instance.providerType)
            {
                case LLMProvider.OpenAI:
                    shippedModels = modelData.openAI?.models;
                    break;
                case LLMProvider.Anthropic:
                    shippedModels = modelData.anthropic?.models;
                    break;
                case LLMProvider.Gemini:
                    shippedModels = modelData.gemini?.models;
                    break;
                default:
                    // Local providers fetch their own model lists from the server.
                    continue;
            }

            if (shippedModels == null || shippedModels.Count == 0) continue;

            var existing = instance.settings.availableModels ?? new List<string>();
            var merged = new List<string>(shippedModels.Count + existing.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in shippedModels)
            {
                if (string.IsNullOrEmpty(m)) continue;
                if (seen.Add(m)) merged.Add(m);
            }
            // Preserve any custom/legacy models the user already had.
            foreach (var m in existing)
            {
                if (string.IsNullOrEmpty(m)) continue;
                if (seen.Add(m)) merged.Add(m);
            }

            if (!ListsEqual(existing, merged))
            {
                instance.settings.availableModels = merged;
                anyChanged = true;
                RTConsole.Log($"LLMInstanceManager: Refreshed model list for instance '{instance.name}' ({instance.providerType}): {merged.Count} models");
            }
        }

        return anyChanged;
    }

    private static bool ListsEqual(List<string> a, List<string> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>
    /// One-time migration of pre-vision-decoupling configs. Older files encoded vision
    /// capability inside <see cref="LLMJobMode"/> (VisionJobsOnly / NonVisionOnly and the
    /// implicit "no vision" of Big/Small). This derives the orthogonal supportsVision /
    /// visionOnly flags from the legacy jobMode and normalizes jobMode to a pure text-size
    /// value, preserving each instance's effective routing exactly. Gated by schemaVersion
    /// so it runs at most once; returns true if anything changed (caller re-saves).
    /// </summary>
    private bool MigrateJobModes()
    {
        if (_config == null || _config.instances == null) return false;
        if (_config.schemaVersion >= LLMInstancesConfig.CURRENT_SCHEMA_VERSION) return false;

        foreach (var inst in _config.instances)
        {
            if (inst == null) continue;
            switch (inst.jobMode)
            {
                case LLMJobMode.Any:            // text(any) + vision
                    inst.supportsVision = true;  inst.visionOnly = false; inst.jobMode = LLMJobMode.Any;
                    break;
                case LLMJobMode.BigJobsOnly:    // big text, no vision
                    inst.supportsVision = false; inst.visionOnly = false; inst.jobMode = LLMJobMode.BigJobsOnly;
                    break;
                case LLMJobMode.SmallJobsOnly:  // small text, no vision
                    inst.supportsVision = false; inst.visionOnly = false; inst.jobMode = LLMJobMode.SmallJobsOnly;
                    break;
                case LLMJobMode.VisionJobsOnly: // vision only, no text
                    inst.supportsVision = true;  inst.visionOnly = true;  inst.jobMode = LLMJobMode.Any;
                    break;
                case LLMJobMode.NonVisionOnly:  // any-size text, no vision
                    inst.supportsVision = false; inst.visionOnly = false; inst.jobMode = LLMJobMode.Any;
                    break;
            }
        }

        _config.schemaVersion = LLMInstancesConfig.CURRENT_SCHEMA_VERSION;
        RTConsole.Log($"LLMInstanceManager: migrated {_config.instances.Count} instance(s) to vision-capability schema v{LLMInstancesConfig.CURRENT_SCHEMA_VERSION}");
        return true;
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
    /// Get the total concurrent LLM capacity across every ENABLED instance: the sum
    /// of (maxConcurrentTasks * effective replica count) per instance. This matches
    /// LLMSnapshot.Capacity and is what "LLMs" in the chat header reflects - how many
    /// LLM calls can run in parallel right now - rather than the raw count of
    /// configured instance entries or replicas alone. A single instance set to
    /// 2 replicas x 2 concurrent tasks therefore reports 4.
    /// </summary>
    public int GetTotalLLMCapacity()
    {
        if (_config == null) return 0;
        int total = 0;
        foreach (var instance in _config.instances)
        {
            if (instance == null || !instance.isActive) continue;
            total += Math.Max(1, instance.maxConcurrentTasks) * instance.GetEffectiveReplicaCount();
        }
        return total;
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
    /// Move an instance up or down in the configured priority order.
    /// The routing scans this list in order, so earlier equal-utilization matches win.
    /// </summary>
    public bool MoveInstance(int id, int direction)
    {
        if (_config == null || _config.instances == null) return false;
        if (direction == 0) return false;

        int fromIndex = -1;
        for (int i = 0; i < _config.instances.Count; i++)
        {
            if (_config.instances[i].instanceID == id)
            {
                fromIndex = i;
                break;
            }
        }

        if (fromIndex < 0) return false;

        int toIndex = Mathf.Clamp(fromIndex + direction, 0, _config.instances.Count - 1);
        if (toIndex == fromIndex) return false;

        var instance = _config.instances[fromIndex];
        _config.instances.RemoveAt(fromIndex);
        _config.instances.Insert(toIndex, instance);

        SaveConfig();
        NotifyInstancesChanged();
        return true;
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
    /// Check if an instance has capacity for more tasks (across any replica).
    /// </summary>
    public bool HasCapacity(int id)
    {
        var instance = GetInstance(id);
        return instance?.HasCapacity() ?? false;
    }
    
    /// <summary>
    /// Check if an instance is busy across all replicas (at or over capacity everywhere).
    /// </summary>
    public bool IsLLMBusy(int id)
    {
        var instance = GetInstance(id);
        return instance == null || !instance.HasCapacity();
    }
    
    /// <summary>
    /// Increment the active task count for a specific replica of an instance.
    /// Call this when starting a new LLM request.
    /// </summary>
    public void IncrementActiveTasks(int id, int replicaIndex = 0)
    {
        var instance = GetInstance(id);
        if (instance == null) return;
        
        instance.EnsureReplicaActiveTasks();
        if (replicaIndex < 0 || replicaIndex >= instance.replicaActiveTasks.Length)
        {
            Debug.LogWarning($"LLMInstanceManager: IncrementActiveTasks invalid replica {replicaIndex} for instance {id} (count={instance.replicaActiveTasks.Length})");
            replicaIndex = 0;
        }
        instance.replicaActiveTasks[replicaIndex]++;
        instance.activeTasks = instance.GetTotalActiveTasks();
        RTConsole.Log($"LLM instance {id} ({instance.name}) replica {replicaIndex}: started task, replica active={instance.replicaActiveTasks[replicaIndex]}/{instance.maxConcurrentTasks}, total={instance.activeTasks}");
        NotifyLLMStatusChanged();
    }
    
    /// <summary>
    /// Decrement the active task count for a specific replica of an instance.
    /// Call this when an LLM request completes.
    /// </summary>
    public void DecrementActiveTasks(int id, int replicaIndex = 0)
    {
        var instance = GetInstance(id);
        if (instance == null) return;
        
        instance.EnsureReplicaActiveTasks();
        if (replicaIndex < 0 || replicaIndex >= instance.replicaActiveTasks.Length)
        {
            Debug.LogWarning($"LLMInstanceManager: DecrementActiveTasks invalid replica {replicaIndex} for instance {id} (count={instance.replicaActiveTasks.Length})");
            replicaIndex = 0;
        }
        instance.replicaActiveTasks[replicaIndex] = Math.Max(0, instance.replicaActiveTasks[replicaIndex] - 1);
        instance.activeTasks = instance.GetTotalActiveTasks();
        RTConsole.Log($"LLM instance {id} ({instance.name}) replica {replicaIndex}: finished task, replica active={instance.replicaActiveTasks[replicaIndex]}/{instance.maxConcurrentTasks}, total={instance.activeTasks}");
        NotifyLLMStatusChanged();
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
    /// Get the current active task count for an instance (sum across all replicas).
    /// </summary>
    public int GetActiveTaskCount(int id)
    {
        var instance = GetInstance(id);
        return instance?.GetTotalActiveTasks() ?? 0;
    }
    
    /// <summary>
    /// Get the active task count for a specific replica.
    /// </summary>
    public int GetActiveTaskCount(int id, int replicaIndex)
    {
        var instance = GetInstance(id);
        if (instance == null) return 0;
        instance.EnsureReplicaActiveTasks();
        if (replicaIndex < 0 || replicaIndex >= instance.replicaActiveTasks.Length) return 0;
        return instance.replicaActiveTasks[replicaIndex];
    }
    
    /// <summary>
    /// Legacy single-replica busy setter (assumes replica 0). Prefer the replicaIndex overload.
    /// </summary>
    public void SetLLMBusy(int id, bool busy)
    {
        SetLLMBusy(id, 0, busy);
    }
    
    /// <summary>
    /// Set busy state for a specific replica of an instance.
    /// </summary>
    public void SetLLMBusy(int id, int replicaIndex, bool busy)
    {
        if (busy)
            IncrementActiveTasks(id, replicaIndex);
        else
            DecrementActiveTasks(id, replicaIndex);
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
        return GetFreeLLM(isSmallJob, isVisionJob: false, out _);
    }
    
    /// <summary>
    /// Get the best available LLM instance ID for the given job type (legacy overload that discards replica index).
    /// </summary>
    public int GetFreeLLM(bool isSmallJob, bool isVisionJob)
    {
        return GetFreeLLM(isSmallJob, isVisionJob, out _);
    }
    
    /// <summary>
    /// Get the best available LLM instance ID + replica index for the given job type.
    /// Chooses the (instance, replica) slot with the lowest utilization ratio (replicaActiveTasks[r] / maxConcurrentTasks).
    /// Only returns slots that have capacity available.
    /// Returns -1 with replicaIndex=0 if no slot has capacity.
    /// For vision jobs: prefers instances reserved for vision (visionOnly), then falls
    /// back to any vision-capable (supportsVision) instance.
    /// </summary>
    public int GetFreeLLM(bool isSmallJob, bool isVisionJob, out int replicaIndex)
    {
        replicaIndex = 0;
        if (_config == null) return -1;

        // For vision jobs, prefer a dedicated vision instance (visionOnly) so a reserved
        // sidecar takes the work before a general-purpose model; then fall back to any
        // vision-capable instance.
        if (isVisionJob)
        {
            int reservedID = GetFreeLLMVision(isSmallJob, requireVisionOnly: true, out replicaIndex);
            if (reservedID >= 0) return reservedID;

            return GetFreeLLMVision(isSmallJob, requireVisionOnly: false, out replicaIndex);
        }

        // For non-vision jobs, scan all replicas across all instances
        int bestID = -1;
        int bestReplica = 0;
        float bestRatio = float.MaxValue; // Lower is better
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob)) continue; // Only job-type check; capacity is per replica below
            if (instance.maxConcurrentTasks <= 0) continue; // Disabled
            
            instance.EnsureReplicaActiveTasks();
            int repCount = instance.GetEffectiveReplicaCount();
            for (int r = 0; r < repCount; r++)
            {
                int rActive = instance.replicaActiveTasks[r];
                if (rActive >= instance.maxConcurrentTasks) continue; // No capacity in this replica
                
                float ratio = (float)rActive / instance.maxConcurrentTasks;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestID = instance.instanceID;
                    bestReplica = r;
                }
            }
        }
        
        replicaIndex = bestReplica;
        return bestID;
    }
    
    /// <summary>
    /// Free-slot scan for vision jobs. With requireVisionOnly=true only instances reserved
    /// for vision (visionOnly) qualify - the preferred pass. With false, any vision-capable
    /// instance qualifies (the fallback pass; CanAcceptJobType gates on supportsVision).
    /// Capacity is checked per replica; returns -1 if no qualifying slot is free.
    /// </summary>
    private int GetFreeLLMVision(bool isSmallJob, bool requireVisionOnly, out int replicaIndex)
    {
        replicaIndex = 0;
        if (_config == null) return -1;

        int bestID = -1;
        int bestReplica = 0;
        float bestRatio = float.MaxValue;

        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (requireVisionOnly && !instance.visionOnly) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob: true)) continue;
            if (instance.maxConcurrentTasks <= 0) continue;

            instance.EnsureReplicaActiveTasks();
            int repCount = instance.GetEffectiveReplicaCount();
            for (int r = 0; r < repCount; r++)
            {
                int rActive = instance.replicaActiveTasks[r];
                if (rActive >= instance.maxConcurrentTasks) continue;
                
                float ratio = (float)rActive / instance.maxConcurrentTasks;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestID = instance.instanceID;
                    bestReplica = r;
                }
            }
        }
        
        replicaIndex = bestReplica;
        return bestID;
    }
    
    /// <summary>
    /// Get the LLM instance ID with the lowest utilization for the given job type (legacy overload, assumes non-vision).
    /// </summary>
    public int GetLeastBusyLLM(bool isSmallJob = false)
    {
        return GetLeastBusyLLM(isSmallJob, isVisionJob: false, out _);
    }
    
    /// <summary>
    /// Get the LLM instance ID with the lowest utilization (legacy overload that discards replica index).
    /// </summary>
    public int GetLeastBusyLLM(bool isSmallJob, bool isVisionJob)
    {
        return GetLeastBusyLLM(isSmallJob, isVisionJob, out _);
    }
    
    /// <summary>
    /// Get the LLM instance ID + replica index with the lowest utilization for the given job type.
    /// Unlike GetFreeLLM, this returns an instance even if at capacity (for queueing).
    /// Returns -1 with replicaIndex=0 if no matching instances exist at all.
    /// For vision jobs: prefers instances reserved for vision (visionOnly), then falls
    /// back to any vision-capable (supportsVision) instance.
    /// </summary>
    public int GetLeastBusyLLM(bool isSmallJob, bool isVisionJob, out int replicaIndex)
    {
        replicaIndex = 0;
        if (_config == null) return -1;

        // For vision jobs, prefer a dedicated vision instance (visionOnly), then fall back
        // to any vision-capable instance.
        if (isVisionJob)
        {
            int reservedID = GetLeastBusyLLMVision(isSmallJob, requireVisionOnly: true, out replicaIndex);
            if (reservedID >= 0) return reservedID;

            return GetLeastBusyLLMVision(isSmallJob, requireVisionOnly: false, out replicaIndex);
        }

        int bestID = -1;
        int bestReplica = 0;
        float bestRatio = float.MaxValue;
        
        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob)) continue;
            
            instance.EnsureReplicaActiveTasks();
            int repCount = instance.GetEffectiveReplicaCount();
            int divisor = Mathf.Max(1, instance.maxConcurrentTasks);
            for (int r = 0; r < repCount; r++)
            {
                float ratio = instance.maxConcurrentTasks > 0
                    ? (float)instance.replicaActiveTasks[r] / divisor
                    : float.MaxValue;
                
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestID = instance.instanceID;
                    bestReplica = r;
                }
            }
        }
        
        replicaIndex = bestReplica;
        return bestID;
    }
    
    /// <summary>
    /// Least-busy scan for vision jobs (returns an instance even at capacity, for queueing).
    /// With requireVisionOnly=true only vision-reserved instances qualify - the preferred
    /// pass; with false, any vision-capable instance qualifies (the fallback pass).
    /// </summary>
    private int GetLeastBusyLLMVision(bool isSmallJob, bool requireVisionOnly, out int replicaIndex)
    {
        replicaIndex = 0;
        if (_config == null) return -1;

        int bestID = -1;
        int bestReplica = 0;
        float bestRatio = float.MaxValue;

        foreach (var instance in _config.instances)
        {
            if (!instance.isActive) continue;
            if (requireVisionOnly && !instance.visionOnly) continue;
            if (!instance.CanAcceptJobType(isSmallJob, isVisionJob: true)) continue;

            instance.EnsureReplicaActiveTasks();
            int repCount = instance.GetEffectiveReplicaCount();
            int divisor = Mathf.Max(1, instance.maxConcurrentTasks);
            for (int r = 0; r < repCount; r++)
            {
                float ratio = instance.maxConcurrentTasks > 0
                    ? (float)instance.replicaActiveTasks[r] / divisor
                    : float.MaxValue;
                
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestID = instance.instanceID;
                    bestReplica = r;
                }
            }
        }
        
        replicaIndex = bestReplica;
        return bestID;
    }
    
    /// <summary>
    /// Get the total count of active tasks across all LLM instances and all replicas.
    /// </summary>
    public int GetTotalActiveTaskCount()
    {
        if (_config == null) return 0;
        
        int count = 0;
        foreach (var instance in _config.instances)
        {
            if (instance.isActive)
                count += instance.GetTotalActiveTasks();
        }
        return count;
    }
    
    // ============================================
    // Replica / port-increment helpers
    // ============================================
    
    /// <summary>
    /// Apply a port offset to an endpoint URL, incrementing its TCP port by replicaIndex.
    /// Returns the original endpoint unchanged when replicaIndex==0, the URL has no parseable port,
    /// or parsing fails. Preserves the path, query, and trailing-slash behavior of the original.
    /// </summary>
    public static string ApplyReplicaPortOffset(string endpoint, int replicaIndex)
    {
        if (replicaIndex <= 0 || string.IsNullOrEmpty(endpoint)) return endpoint;
        
        try
        {
            var ub = new System.UriBuilder(endpoint);
            // UriBuilder fills in the default port (80/443) when the original had none.
            // We only shift when the original endpoint explicitly contains ":<port>".
            if (!System.Text.RegularExpressions.Regex.IsMatch(endpoint, @"^[a-zA-Z][a-zA-Z0-9+.-]*://[^/]+:\d+"))
            {
                return endpoint;
            }
            
            int newPort = ub.Port + replicaIndex;
            if (newPort <= 0 || newPort > 65535) return endpoint;
            ub.Port = newPort;
            
            string result = ub.Uri.AbsoluteUri;
            
            // UriBuilder normalizes a host-only URL like "http://hal:8000" into "http://hal:8000/".
            // Preserve the original presence/absence of a trailing slash on the host-only form.
            if (!endpoint.EndsWith("/"))
            {
                int schemeEnd = result.IndexOf("://", System.StringComparison.Ordinal);
                if (schemeEnd >= 0)
                {
                    int slashAfterAuthority = result.IndexOf('/', schemeEnd + 3);
                    // If the only '/' after the authority is the last char (i.e., empty path), trim it.
                    if (slashAfterAuthority == result.Length - 1)
                    {
                        result = result.Substring(0, result.Length - 1);
                    }
                }
            }
            return result;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"LLMInstanceManager.ApplyReplicaPortOffset: failed to shift port on '{endpoint}': {e.Message}");
            return endpoint;
        }
    }
    
    /// <summary>
    /// Get the configured base endpoint of an instance with the replica's port offset applied.
    /// This is just the raw endpoint (no provider-specific path suffix) — for the full URL
    /// including paths like /v1/chat/completions, see LLMSettingsManager.GetInstanceEndpointUrl.
    /// </summary>
    public string GetInstanceBaseEndpoint(int instanceID, int replicaIndex)
    {
        var instance = GetInstance(instanceID);
        if (instance == null || instance.settings == null) return "";
        return ApplyReplicaPortOffset(instance.settings.endpoint, replicaIndex);
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

