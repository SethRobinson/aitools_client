using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Singleton manager for LLM settings. Handles loading, saving, and providing access to LLM configurations.
/// This replaces the LLM-related settings previously stored in config.txt.
/// </summary>
public class LLMSettingsManager : MonoBehaviour
{
    private static LLMSettingsManager _instance;
    private LLMSettings _settings;
    private bool _isInitialized = false;
    private bool _hasMigratedFromConfig = false;

    private const string SETTINGS_FILE_NAME = "llm_settings.txt";

    /// <summary>
    /// Fired when LLM settings that affect runtime behavior/UI have changed (active provider, model list, etc).
    /// </summary>
    public event Action SettingsChanged;

    private void NotifySettingsChanged()
    {
        SettingsChanged?.Invoke();
    }

    public static LLMSettingsManager Get()
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
        Initialize();
    }

    /// <summary>
    /// Initialize the manager and load settings.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        if (File.Exists(SETTINGS_FILE_NAME))
        {
            LoadSettings();
        }
        else
        {
            // First run - create defaults and try to migrate from config.txt
            _settings = LLMSettings.CreateDefault();
            MigrateFromConfig();
            SaveSettings();
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Migrate existing LLM settings from Config.cs/config.txt to the new system.
    /// </summary>
    private void MigrateFromConfig()
    {
        if (_hasMigratedFromConfig) return;

        try
        {
            var config = Config.Get();
            if (config == null)
            {
                Debug.Log("LLMSettingsManager: Config not available for migration, using defaults.");
                return;
            }

            // Migrate OpenAI settings
            string openAIKey = config.GetOpenAI_APIKey();
            if (!string.IsNullOrEmpty(openAIKey) && openAIKey.Length > 10)
            {
                _settings.openAI.apiKey = openAIKey;
                _settings.openAI.endpoint = config._openai_gpt4_endpoint;
                _settings.openAI.selectedModel = config.GetOpenAI_APIModel();
                
                // If OpenAI key is set, make it the active provider
                _settings.activeProvider = LLMProvider.OpenAI;
            }

            // Migrate Anthropic settings
            string anthropicKey = config.GetAnthropicAI_APIKey();
            if (!string.IsNullOrEmpty(anthropicKey) && anthropicKey.Length > 10)
            {
                _settings.anthropic.apiKey = anthropicKey;
                _settings.anthropic.endpoint = config.GetAnthropicAI_APIEndpoint();
                _settings.anthropic.selectedModel = config.GetAnthropicAI_APIModel();
            }

            // Migrate generic LLM settings (llama.cpp or Ollama)
            string genericAddress = config._texgen_webui_address;
            if (!string.IsNullOrEmpty(genericAddress) && genericAddress.Length > 1)
            {
                // Check if it's Ollama or llama.cpp based on existing detection
                if (config.GetGenericLLMIsOllama())
                {
                    _settings.ollama.endpoint = EnsureHttpPrefix(genericAddress);
                    _settings.ollama.apiKey = config._texgen_webui_APIKey;
                    
                    // Get model from params
                    string model = config.GetGenericLLMParm("model");
                    if (!string.IsNullOrEmpty(model))
                    {
                        model = model.Replace("\"", "");
                        _settings.ollama.selectedModel = model;
                    }

                    // Copy extra params
                    foreach (var parm in config.GetLLMParms())
                    {
                        if (parm._key != "model" && parm._key != "use_ollama_defaults")
                        {
                            _settings.ollama.extraParams.Add(new LLMParm { _key = parm._key, _value = parm._value });
                        }
                    }
                }
                else if (config.GetGenericLLMIsLlamaCpp())
                {
                    _settings.llamaCpp.endpoint = EnsureHttpPrefix(genericAddress);
                    _settings.llamaCpp.apiKey = config._texgen_webui_APIKey;

                    // Copy extra params
                    foreach (var parm in config.GetLLMParms())
                    {
                        _settings.llamaCpp.extraParams.Add(new LLMParm { _key = parm._key, _value = parm._value });
                    }
                }
                else
                {
                    // Unknown generic - put in llama.cpp as default
                    _settings.llamaCpp.endpoint = EnsureHttpPrefix(genericAddress);
                    _settings.llamaCpp.apiKey = config._texgen_webui_APIKey;
                }
            }

            _hasMigratedFromConfig = true;
            RTConsole.Log("LLMSettingsManager: Migrated settings from config.txt to llm_settings.txt");
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMSettingsManager: Error during migration from config: " + e.Message);
        }
    }

    private string EnsureHttpPrefix(string address)
    {
        if (string.IsNullOrEmpty(address)) return address;
        if (!address.StartsWith("http://") && !address.StartsWith("https://"))
        {
            return "http://" + address;
        }
        return address;
    }

    /// <summary>
    /// Load settings from the settings file.
    /// </summary>
    public void LoadSettings()
    {
        try
        {
            string json = File.ReadAllText(SETTINGS_FILE_NAME);
            _settings = JsonUtility.FromJson<LLMSettings>(json);

            if (_settings == null)
            {
                Debug.LogWarning("LLMSettingsManager: Failed to parse settings, using defaults.");
                _settings = LLMSettings.CreateDefault();
            }

            RTConsole.Log("LLMSettingsManager: Loaded settings from " + SETTINGS_FILE_NAME);
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMSettingsManager: Error loading settings: " + e.Message);
            _settings = LLMSettings.CreateDefault();
        }
    }

    /// <summary>
    /// Save current settings to the settings file.
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            string json = JsonUtility.ToJson(_settings, true);
            File.WriteAllText(SETTINGS_FILE_NAME, json);
            RTConsole.Log("LLMSettingsManager: Saved settings to " + SETTINGS_FILE_NAME);
        }
        catch (Exception e)
        {
            Debug.LogError("LLMSettingsManager: Error saving settings: " + e.Message);
        }
    }

    /// <summary>
    /// Apply new settings and save them.
    /// </summary>
    public void ApplySettings(LLMSettings newSettings)
    {
        _settings = newSettings;
        SaveSettings();
        NotifySettingsChanged();
    }

    /// <summary>
    /// Get a clone of the current settings for editing.
    /// </summary>
    public LLMSettings GetSettingsClone()
    {
        return _settings.Clone();
    }

    // ============================================
    // Accessor methods matching existing patterns
    // ============================================

    /// <summary>
    /// Get the currently active LLM provider.
    /// </summary>
    public LLMProvider GetActiveProvider()
    {
        return _settings.activeProvider;
    }

    /// <summary>
    /// Set the active provider.
    /// </summary>
    public void SetActiveProvider(LLMProvider provider)
    {
        _settings.activeProvider = provider;
        NotifySettingsChanged();
    }

    /// <summary>
    /// Get the API key for the active provider.
    /// </summary>
    public string GetAPIKey()
    {
        return _settings.GetActiveProviderSettings().apiKey;
    }

    /// <summary>
    /// Get the API key for a specific provider.
    /// </summary>
    public string GetAPIKey(LLMProvider provider)
    {
        return _settings.GetProviderSettings(provider).apiKey;
    }

    /// <summary>
    /// Get the endpoint for the active provider.
    /// </summary>
    public string GetEndpoint()
    {
        return _settings.GetActiveProviderSettings().endpoint;
    }

    /// <summary>
    /// Get the endpoint for a specific provider.
    /// </summary>
    public string GetEndpoint(LLMProvider provider)
    {
        return _settings.GetProviderSettings(provider).endpoint;
    }

    /// <summary>
    /// Get the selected model for the active provider.
    /// </summary>
    public string GetModel()
    {
        return _settings.GetActiveProviderSettings().selectedModel;
    }

    /// <summary>
    /// Get the selected model for a specific provider.
    /// </summary>
    public string GetModel(LLMProvider provider)
    {
        return _settings.GetProviderSettings(provider).selectedModel;
    }

    /// <summary>
    /// Get the extra parameters for the active provider.
    /// </summary>
    public List<LLMParm> GetExtraParams()
    {
        return _settings.GetActiveProviderSettings().extraParams;
    }

    /// <summary>
    /// Get the extra parameters for a specific provider.
    /// </summary>
    public List<LLMParm> GetExtraParams(LLMProvider provider)
    {
        return _settings.GetProviderSettings(provider).extraParams;
    }

    /// <summary>
    /// Get all LLM parameters for a specific provider, including the model.
    /// This is used for building API requests.
    /// </summary>
    public List<LLMParm> GetLLMParms(LLMProvider provider)
    {
        var settings = _settings.GetProviderSettings(provider);
        var result = new List<LLMParm>();

        // Add the model if it's set
        if (!string.IsNullOrEmpty(settings.selectedModel))
        {
            result.Add(new LLMParm { _key = "model", _value = settings.selectedModel });
        }

        // Add all extra parameters (null check for deserialization safety)
        if (settings.extraParams != null)
        {
            foreach (var parm in settings.extraParams)
            {
                // Don't add duplicate model parameter
                if (parm._key != "model")
                {
                    result.Add(new LLMParm { _key = parm._key, _value = parm._value });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Get all LLM parameters for the active provider, including the model.
    /// </summary>
    public List<LLMParm> GetLLMParms()
    {
        return GetLLMParms(_settings.activeProvider);
    }

    /// <summary>
    /// Check if the active provider is Ollama.
    /// </summary>
    public bool IsOllama()
    {
        return _settings.activeProvider == LLMProvider.Ollama;
    }

    /// <summary>
    /// Check if the active provider is llama.cpp.
    /// </summary>
    public bool IsLlamaCpp()
    {
        return _settings.activeProvider == LLMProvider.LlamaCpp;
    }

    /// <summary>
    /// Get the settings for a specific provider.
    /// </summary>
    public LLMProviderSettings GetProviderSettings(LLMProvider provider)
    {
        return _settings.GetProviderSettings(provider);
    }

    /// <summary>
    /// Update the available models for Ollama (called after fetching from server).
    /// </summary>
    public void SetOllamaModels(List<string> models)
    {
        _settings.ollama.availableModels = new List<string>(models);
        SaveSettings();
        NotifySettingsChanged();
    }

    /// <summary>
    /// Get a specific extra parameter value for the active provider.
    /// </summary>
    public string GetExtraParam(string key)
    {
        var parms = _settings.GetActiveProviderSettings().extraParams;
        foreach (var parm in parms)
        {
            if (parm._key == key)
            {
                return parm._value;
            }
        }
        return "";
    }

    /// <summary>
    /// Convert LLMProvider to legacy LLM_Type for backward compatibility.
    /// </summary>
    public LLM_Type GetLegacyLLMType()
    {
        switch (_settings.activeProvider)
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

    /// <summary>
    /// Get settings in a format compatible with existing code that uses Config.
    /// Returns the proper endpoint URL with path for API calls.
    /// </summary>
    public string GetFullEndpointUrl()
    {
        var settings = _settings.GetActiveProviderSettings();
        string endpoint = settings.endpoint;

        switch (_settings.activeProvider)
        {
            case LLMProvider.OpenAI:
                // OpenAI endpoint should already include /v1/chat/completions
                return endpoint;

            case LLMProvider.Anthropic:
                // Anthropic endpoint should already include /v1/messages
                return endpoint;

            case LLMProvider.LlamaCpp:
                // llama.cpp uses /v1/chat/completions
                if (!endpoint.EndsWith("/v1/chat/completions"))
                {
                    endpoint = endpoint.TrimEnd('/') + "/v1/chat/completions";
                }
                return endpoint;

            case LLMProvider.Ollama:
                // Ollama uses /v1/chat/completions for OpenAI-compatible API
                return endpoint.TrimEnd('/');

            default:
                return endpoint;
        }
    }

    /// <summary>
    /// Get the Ollama endpoint for API calls (base URL, not including path).
    /// </summary>
    public string GetOllamaBaseUrl()
    {
        return _settings.ollama.endpoint.TrimEnd('/');
    }
}
