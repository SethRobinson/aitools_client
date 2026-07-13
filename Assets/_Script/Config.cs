using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DG.Tweening.Plugins.Core.PathCore;
using UnityEngine.UI;
using static System.Net.WebRequestMethods;
using UnityEditor;
using TMPro;
using System;
using System.Security.Policy;
using System.Globalization;
using System.Text;

public enum LLM_Type
{
    OpenAI_API,
    GenericLLM_API,
    Anthropic_API
}

public enum TextToSpeechProvider
{
    None = 0,
    ElevenLabs = 1
}

public enum RTRendererType
{
    ComfyUI = 0,
    Any_Local = 2,
    A1111 = 3,
    AI_Tools = 4,
    AI_Tools_or_A1111 = 5 //(this means either A1111 or AIT)
}

public class GPUInfo
{
    public int localGPUID;
    public int remoteGPUID;
    public string remoteURL;
    public bool IsGPUBusy;
    public int pendingLLMCount = 0; // Tracks pics doing pre-GPU LLM work targeting this server
    public Dictionary<string, object> configDict = null;
    public bool supportsAITools = false;
    public ServerButtonScript buttonScript = null;
    public bool serverIsWindows = false;
    public RTRendererType _requestedRendererType = RTRendererType.ComfyUI;
    public bool isLocal = true; //false would mean a non-local API renderer. Local means TextGen WebUI, AI Tools server or ComfyUI (doesn't actually have to be local)
    public bool _usesDetailedPrompts = false; //simple is the default
    public int _comfyUIWorkFlowOverride = -1;
    public bool _bIsActive = true;
    public string _jobListOverride = "";
    public string _autoPicOverride = ""; // Empty = use global AutoPic setting, otherwise the preset filename to use
    public string _name = ""; //if blank, we'll use our own
    public string _authToken = ""; // Optional bearer token sent as "Authorization: Bearer <token>" to a protected ComfyUI (e.g. ComfyUI-Login custom node). Blank = no auth. Set via the |token=... field on add_server in config.txt.
    public float _vramGB = 0f; // User-declared VRAM in GB (0 = unknown). Set via set_gpu_vram|gpuID|gb| in config.txt. Surfaced to AI Chat skills so the LLM can pick a GPU with enough memory.
    public int _adventureRenderCount = 0; // Per-server render count for Adventure mode (0 = don't auto-spawn for this server)
    public bool _ignoredByExtraGenerators = false; // If true, Gen Extra and global render count skip this server
    public bool _gpuLocked = true; // If true (default), per-server autopics are reserved to this GPU; if false, any free GPU can process them
    public string _gpuNameMatchFilter = ""; // Only meaningful when _gpuLocked == false. If set, per-server-spawned jobs only run on GPUs whose _name contains this substring (case-insensitive).
    public int _configOrder = int.MaxValue; // Position of this server's add_server line in config.txt. Used to keep the GPU list in config order regardless of which server responds first. MaxValue = no explicit order (sorts to the end).

    /// <summary>
    /// Checks if this server's job list override has LLM calls before GPU work.
    /// </summary>
    public bool HasLLMFirstOverride()
    {
        if (string.IsNullOrEmpty(_jobListOverride)) return false;
        
        var jobList = GetPicJobListAsListOfStrings();
        foreach (string line in jobList)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("-") || trimmed.StartsWith("#") || trimmed.Length == 0) continue; // Skip comments/empty
            if (trimmed.StartsWith("command ")) continue; // Skip command lines
            
            if (trimmed == "call_llm")
            {
                return true; // Found LLM call before any GPU work
            }
            else if (trimmed.EndsWith(".json"))
            {
                return false; // Hit GPU workflow first
            }
        }
        return false;
    }

    public List<String> GetPicJobListAsListOfStrings()
    {
        // Delegate to GameLogic which handles multi-line @end blocks
        return GameLogic.Get().GetPicJobListAsListOfStrings(_jobListOverride);
    }

}

[Serializable]
public class ComfyServerConfig
{
    public string Url = "";
    public string DisplayName = "";
    public string AuthToken = "";
    public float VramGB = 0f;

    public ComfyServerConfig Clone()
    {
        return new ComfyServerConfig
        {
            Url = Url,
            DisplayName = DisplayName,
            AuthToken = AuthToken,
            VramGB = VramGB
        };
    }
}


public class Config : MonoBehaviour
{  
    public static bool _isTestMode = false; //could do anything, _testMode is checked by random functions
    const float m_serverButtonRightMargin = 42f;
    const float m_serverButtonTopOffset = -9f;
    const float m_serverButtonRowSpacing = -20f;
  
    List<GPUInfo> m_gpuInfo = new List<GPUInfo>();

    // Maps a configured ComfyUI server base URL (exactly as written on its add_server line) to its
    // optional bearer token. Populated while parsing config.txt, consulted by every ComfyUI request
    // (including server discovery, before any GPUInfo exists). Empty = no server uses auth.
    Dictionary<string, string> m_comfyAuthTokens = new Dictionary<string, string>();
    Dictionary<int, float> m_configOrderVramGB = new Dictionary<int, float>();
    List<LLMParm> m_llmParms = new List<LLMParm>();

    static Config _this;
    string m_configText; //later move this to a config.txt or something
    const string m_configFileName = "config.txt";
    const string m_crazyCamConfigFileName = "config_cam.txt";
    bool m_safetyFilter = false;
    float m_requiredServerVersion = 0.46f;

    //default names for AI stuff
    string m_assistantName;
    string m_systemName;
    string m_userName;

    string _openAI_APIKey;
    string _openAI_APIModel;
    public string _texgen_webui_address;
    public string _openai_gpt4_endpoint;

     public string _ollama_endpoint;
  public string _texgen_webui_APIKey;
   
    string _anthropicAI_APIKey;
    string _anthropicAI_APIModel;
    string _anthropicAI_APIEndpoint;
    string _genericLLMMode;

    TextToSpeechProvider _textToSpeechProvider;
    string _elevenLabs_APIKey ;
    string _elevenLabs_voiceID;
    int _jpgSaveQuality;
  
    string _defaultAudioPrompt;
    string _defaultAudioNegativePrompt;
    // CrazyCam requested camera capture settings
    int _crazyCamRequestedWidth;
    int _crazyCamRequestedHeight;
    int _crazyCamRequestedFPS;
  
    public string GetAISystemWord() { return m_systemName; }
    public string GetAIUserWord() { return m_userName; }
    public string GetAIAssistantWord() { return m_assistantName; }

    public string GetAnthropicAI_APIKey() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            string key = mgr.GetAPIKey(LLMProvider.Anthropic);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return _anthropicAI_APIKey; 
    }
    public string GetAnthropicAI_APIModel() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            string model = mgr.GetModel(LLMProvider.Anthropic);
            if (!string.IsNullOrEmpty(model)) return model;
        }
        return _anthropicAI_APIModel; 
    }
    public string GetAnthropicAI_APIEndpoint() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            string endpoint = mgr.GetEndpoint(LLMProvider.Anthropic);
            if (!string.IsNullOrEmpty(endpoint)) return endpoint;
        }
        return _anthropicAI_APIEndpoint; 
    }

    string m_defaultSampler = "DPM++ 2M";
    static public string _saveDirName = "output"; //where to save files, relative to the app's root
    public string GetOpenAI_APIKey() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            string key = mgr.GetAPIKey(LLMProvider.OpenAI);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return _openAI_APIKey; 
    }
    public string GetOpenAI_APIModel() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            string model = mgr.GetModel(LLMProvider.OpenAI);
            if (!string.IsNullOrEmpty(model)) return model;
        }
        return _openAI_APIModel; 
    }
    public string GetElevenLabs_APIKey() { return _elevenLabs_APIKey; }
    public TextToSpeechProvider GetTextToSpeechProvider() { return _textToSpeechProvider; }
    public string GetDefaultAudioPrompt() { return _defaultAudioPrompt; }
    public string GetDefaultAudioNegativePrompt() { return _defaultAudioNegativePrompt; }
    public int GetCrazyCamRequestedWidth() { return _crazyCamRequestedWidth; }
    public int GetCrazyCamRequestedHeight() { return _crazyCamRequestedHeight; }
    public int GetCrazyCamRequestedFPS() { return _crazyCamRequestedFPS; }

    public string GetElevenLabs_voiceID() { return _elevenLabs_voiceID; }
    public List<LLMParm> GetLLMParms() 
    { 
        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            var provider = mgr.GetActiveProvider();
            if (provider == LLMProvider.Ollama || provider == LLMProvider.LlamaCpp)
            {
                // Use GetLLMParms which includes the model parameter
                var parms = mgr.GetLLMParms(provider);
                if (parms != null && parms.Count > 0) return parms;
            }
        }
        return m_llmParms; 
    }
    public string GetGenericLLMMode() { return _genericLLMMode; }
  
    float m_version = 3.05f;
    string m_imageEditorPathAndExe = "none set";
    public string GetVersionString() { return m_version.ToString("0.00"); }
    public float GetVersion() { return m_version; }
    public float GetRequiredServerVersion() { return m_requiredServerVersion; }
    public string GetDefaultSampler() { return m_defaultSampler; }

    public List<AudioClip> m_audioClips;
    public GameObject m_serverButtonPrefab;
    public GameObject m_noServersButtonPrefab;

    public float _snapShotBatSoundVolumeMod = 0.3f;

    void SetDefaults()
    {
     m_assistantName = "assistant";
     m_systemName = "system";
     m_userName = "user";

     _openAI_APIKey = "";
     _openAI_APIModel = "gpt-4o";
     _texgen_webui_address = "localhost:8080";
    _openai_gpt4_endpoint = "https://api.openai.com/v1/chat/completions";
    _ollama_endpoint = "/v1/chat/completions";

        _textToSpeechProvider = TextToSpeechProvider.None;
        _elevenLabs_APIKey = "";
     _elevenLabs_voiceID = "21m00Tcm4TlvDq8ikWAM";
     _jpgSaveQuality = 80;
     _texgen_webui_APIKey = "none";
     _genericLLMMode = "chat-instruct";
     _anthropicAI_APIKey = "";
     _anthropicAI_APIModel = "claude-3-5-sonnet-latest";
     _defaultAudioPrompt = "audio that perfectly matches the onscreen action";
     _defaultAudioNegativePrompt = "music";

     _anthropicAI_APIEndpoint = "https://api.anthropic.com/v1/complete";
     m_llmParms = new List<LLMParm>();
     m_llmParms.Clear();

     // Default CrazyCam capture request
     _crazyCamRequestedWidth = 1280;
     _crazyCamRequestedHeight = 720;
     _crazyCamRequestedFPS = 30;

    }

    void Awake()
    {
#if RT_BETA

#endif
        _this = this;
    }

    public string GetImageEditorPathAndExe() { return m_imageEditorPathAndExe; }

    public void SetImageEditorPathAndExe(string pathAndExe, bool saveToFile = true)
    {
        m_imageEditorPathAndExe = CleanConfigField(pathAndExe);

        if (saveToFile)
        {
            m_configText = BuildModernConfigText(GetModernComfyServerConfigs());
            SaveConfigToFile();
        }
    }

    public void SetTextToSpeechSettings(TextToSpeechProvider provider, string elevenLabsAPIKey, string elevenLabsVoiceID, bool saveToFile = true)
    {
        _textToSpeechProvider = provider;
        _elevenLabs_APIKey = CleanConfigField(elevenLabsAPIKey);
        _elevenLabs_voiceID = CleanConfigField(elevenLabsVoiceID);

        if (saveToFile)
        {
            m_configText = BuildModernConfigText(GetModernComfyServerConfigs());
            SaveConfigToFile();
        }
    }

    public static string NormalizeComfyServerUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return "";

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        while (url.EndsWith("/") && !url.EndsWith("://"))
            url = url.Substring(0, url.Length - 1);

        return url;
    }

    public List<ComfyServerConfig> GetModernComfyServerConfigs()
    {
        var servers = new List<ComfyServerConfig>();
        var vramByOrder = new Dictionary<int, float>();
        string config = m_configText ?? "";
        int serverOrder = 0;

        using (var reader = new StringReader(config))
        {
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                string[] words = trimmed.Split('|');
                if (words.Length == 0) continue;

                if (words[0] == "add_server" && words.Length >= 2)
                {
                    var server = new ComfyServerConfig
                    {
                        Url = NormalizeComfyServerUrl(words[1])
                    };

                    for (int wi = 2; wi < words.Length; wi++)
                    {
                        string field = words[wi] == null ? "" : words[wi].Trim();
                        if (field.Length == 0) continue;

                        if (field.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
                        {
                            server.AuthToken = field.Substring("token=".Length).Trim();
                        }
                        else if (server.DisplayName.Length == 0)
                        {
                            server.DisplayName = field;
                        }
                    }

                    servers.Add(server);
                    serverOrder++;
                }
                else if (words[0] == "set_gpu_vram" && words.Length >= 3)
                {
                    if (int.TryParse(words[1], out int order) && TryParseConfigFloat(words[2], out float gb))
                    {
                        vramByOrder[order] = Mathf.Max(0f, gb);
                    }
                }
            }
        }

        foreach (var kvp in vramByOrder)
        {
            if (kvp.Key >= 0 && kvp.Key < servers.Count)
                servers[kvp.Key].VramGB = kvp.Value;
        }

        return servers;
    }

    public bool SaveModernComfyServerConfigs(List<ComfyServerConfig> servers, out string error)
    {
        error = "";
        try
        {
            m_configText = BuildModernConfigText(servers);
            ProcessConfigString(m_configText);
            SaveConfigToFile();
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public string BuildModernConfigText(List<ComfyServerConfig> servers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Seth's AI Tools config file");
        sb.AppendLine("# Managed by the Settings > ComfyUI Settings screen.");
        sb.AppendLine();
        sb.AppendLine("# ComfyUI servers. Start ComfyUI with --listen when connecting from another machine.");

        if (servers != null)
        {
            for (int i = 0; i < servers.Count; i++)
            {
                ComfyServerConfig server = servers[i] ?? new ComfyServerConfig();
                string url = NormalizeComfyServerUrl(server.Url);
                if (string.IsNullOrEmpty(url)) continue;

                string name = CleanConfigField(server.DisplayName);
                string token = CleanConfigField(server.AuthToken);

                sb.Append("add_server|").Append(url).Append("|");
                if (!string.IsNullOrEmpty(name))
                    sb.Append(name).Append("|");
                if (!string.IsNullOrEmpty(token))
                    sb.Append("token=").Append(token).Append("|");
                sb.AppendLine();
            }

            for (int i = 0; i < servers.Count; i++)
            {
                ComfyServerConfig server = servers[i];
                if (server != null && server.VramGB > 0f)
                {
                    sb.Append("set_gpu_vram|")
                        .Append(i.ToString(CultureInfo.InvariantCulture))
                        .Append("|")
                        .Append(server.VramGB.ToString("0.##", CultureInfo.InvariantCulture))
                        .AppendLine("|");
                }
            }
        }

        string editorPath = CleanConfigField(m_imageEditorPathAndExe);
        if (!string.IsNullOrEmpty(editorPath) &&
            !string.Equals(editorPath, "none set", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.Append("set_image_editor|").Append(editorPath).AppendLine("|");
        }

        sb.AppendLine();
        sb.Append("set_default_audio_prompt|").Append(CleanConfigField(_defaultAudioPrompt)).AppendLine("|");
        sb.Append("set_default_audio_negative_prompt|").Append(CleanConfigField(_defaultAudioNegativePrompt)).AppendLine("|");

        sb.AppendLine();
        sb.Append("set_text_to_speech_provider|").Append(_textToSpeechProvider).AppendLine("|");
        if (!string.IsNullOrEmpty(_elevenLabs_APIKey))
            sb.Append("set_elevenlabs_api_key|").Append(CleanConfigField(_elevenLabs_APIKey)).AppendLine("|");
        if (!string.IsNullOrEmpty(_elevenLabs_voiceID))
            sb.Append("set_elevenlabs_voice_id|").Append(CleanConfigField(_elevenLabs_voiceID)).AppendLine("|");

        return sb.ToString();
    }

    private static string CleanConfigField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\r", " ").Replace("\n", " ").Replace("|", " ").Trim();
    }

    private static bool TryParseConfigFloat(string value, out float result)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return true;
        return float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private static TextToSpeechProvider ParseTextToSpeechProvider(string value)
    {
        if (string.Equals(value, "ElevenLabs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Eleven Labs", StringComparison.OrdinalIgnoreCase))
        {
            return TextToSpeechProvider.ElevenLabs;
        }

        return TextToSpeechProvider.None;
    }

    private void Start()
    {
        RTAudioManager.Get().AddClipsToLibrary(m_audioClips);

        // Initialize LLM Settings Manager (new system)
        InitializeLLMSettingsManager();

       ConnectToServers();
    }

    /// <summary>
    /// Initialize the LLMSettingsManager component.
    /// </summary>
    private void InitializeLLMSettingsManager()
    {
        // Check if already exists
        if (LLMSettingsManager.Get() == null)
        {
            // Add the manager component to this game object
            gameObject.AddComponent<LLMSettingsManager>();
        }
    }

    public void ConnectToServers()
    {

        GameLogic.Get().SetHasControlNetSupport(false);

        if (GetGPUCount() > 0)
        {
            return;
        }

        m_configText = LoadConfigFromFile();
        if (string.IsNullOrEmpty(m_configText))
        {
            m_configText = @"#Seth's AI Tools general config file

#Note: For CrazyCam/photobooth specific settings, see config_cam.txt
#For LLM settings, see the LLM Settings panel on the main tool panel.

#This is where you add your ComfyUI server(s).   (run 'em with the --listen parm)

#I run 8 ComfyUI servers from the same directory on a linux server, each runnings
# on its own port with its own video card and it works great.
#If it's running on another machine, don't forget to run with the --listen parm so it can
#be accessed from other machines. (for windows, change 'Listen Address' in the ComfyUI GUI from 127.0.0.1 to 0.0.0.0)

#default port is usually 8000 or 8188
add_server|http://localhost:8000|

#Optional: If a ComfyUI server is password protected, for example with the
#ComfyUI-Login custom node, append its direct API bearer token to the server line.
#Leave this off for normal open ComfyUI servers. Use the token printed for direct
#API calls, not your web UI password.
#add_server|http://secured-box.lan:8188|token=$2b$12$qUfJfV942n...

#Add more add_server commands like this, uncomment below
#add_server|http://localhost:8001|

#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto
#update in here. (optional)

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2026\Photoshop.exe

set_default_audio_prompt|audio that perfectly matches the onscreen action|
set_default_audio_negative_prompt|music|

";
            
        }

        RTQuickMessageManager.Get().ShowMessage("Connecting...");
        ProcessConfigString(m_configText);
        
        // Load CrazyCam-specific config from separate file
        LoadCrazyCamConfig();
    }

    public void PopulateRendererDropDown(TMP_Dropdown rendererSelectionDropdown)
    {
        rendererSelectionDropdown.ClearOptions();
        rendererSelectionDropdown.AddOptions(new List<string> { RTRendererType.ComfyUI.ToString() });
        rendererSelectionDropdown.value = 0;
        rendererSelectionDropdown.RefreshShownValue();
    }

    public string GetBaseFileDir(string subdir)
    {
        string tempDir = Application.dataPath;


        //get the Assets dir, but strip off the word Assets
        tempDir = tempDir.Replace('/', '\\');
        tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));

        //tack on subdir if needed
        tempDir = tempDir + subdir;

        //reconvert to \\ (I assume this code would have to change if it wasn't Windows... uhh
        tempDir = tempDir.Replace('/', '\\');

        return tempDir;
    }

    public bool DontHaveLocalServers()
    {
        for (int i = 0; i < GetGPUCount(); i++)
        {
            if (GetGPUInfo(i).isLocal) return false;
        }

        return true; //no local servers
    }

    /// <summary>
    /// Case-insensitive substring match against a GPU's _name. Empty/null filter always matches.
    /// </summary>
    private bool GPUNameMatchesFilter(int gpuID, string nameMatchFilter)
    {
        if (string.IsNullOrEmpty(nameMatchFilter)) return true;
        string gpuName = m_gpuInfo[gpuID]._name;
        if (string.IsNullOrEmpty(gpuName)) return false;
        return gpuName.IndexOf(nameMatchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public int GetFreeGPU(RTRendererType requestedGPUType = RTRendererType.Any_Local, bool bFreeOrBusyIsOk = false, bool skipIgnored = false, string nameMatchFilter = null)
    {
        // m_gpuInfo is kept in config.txt add_server order. Returning the first
        // matching idle entry makes that order the scheduling priority.
        //special types

        if (requestedGPUType != RTRendererType.Any_Local)
        {
            for (int i = 0; i < GetGPUCount(); i++)
            {
                // Skip servers owned by other pics (reserved for AutoPic workflows)
                if (PicMain.IsServerOwnedByAnyPic(i)) continue;
                if (skipIgnored && GetGPUInfo(i)._ignoredByExtraGenerators) continue;
                if (!GPUNameMatchesFilter(i, nameMatchFilter)) continue;

                if (!IsGPUBusy(i) && Config.Get().GetGPUInfo(i)._bIsActive)
                {
                    if (GetGPUInfo(i)._requestedRendererType == requestedGPUType)
                    {
                        return i;
                    }

                    if (requestedGPUType == RTRendererType.AI_Tools_or_A1111)
                    {
                        //special case to look for two kinds of things
                        if (GetGPUInfo(i)._requestedRendererType == RTRendererType.A1111
                            ||
                                GetGPUInfo(i)._requestedRendererType == RTRendererType.AI_Tools
                                )
                        {
                            return i;
                        }
                    }
                }
            }

        }

        if (requestedGPUType == RTRendererType.Any_Local)
        {
            //special way to find one for "any local"
            for (int i = 0; i < Config.Get().GetGPUCount(); i++)
            {
                // Skip servers owned by other pics (reserved for AutoPic workflows)
                if (PicMain.IsServerOwnedByAnyPic(i)) continue;
                if (skipIgnored && GetGPUInfo(i)._ignoredByExtraGenerators) continue;
                if (!GPUNameMatchesFilter(i, nameMatchFilter)) continue;

                if (!Config.Get().IsGPUBusy(i) && GetGPUInfo(i).isLocal && Config.Get().GetGPUInfo(i)._bIsActive)
                {
                    return i;
                }
            }
        }


        //do it all again, but take whatever
        if (bFreeOrBusyIsOk)
        {
            if (requestedGPUType != RTRendererType.Any_Local)
            {
                for (int i = 0; i < GetGPUCount(); i++)
                {
                    // Skip servers owned by other pics (reserved for AutoPic workflows)
                    if (PicMain.IsServerOwnedByAnyPic(i)) continue;
                    if (skipIgnored && GetGPUInfo(i)._ignoredByExtraGenerators) continue;
                    if (!GPUNameMatchesFilter(i, nameMatchFilter)) continue;

                    if (GetGPUInfo(i)._requestedRendererType == requestedGPUType && Config.Get().GetGPUInfo(i)._bIsActive)
                    {
                        return i;
                    }

                    if (requestedGPUType == RTRendererType.AI_Tools_or_A1111 && Config.Get().GetGPUInfo(i)._bIsActive)
                    {
                        //special case to look for two kinds of things
                        if (GetGPUInfo(i)._requestedRendererType == RTRendererType.A1111
                            ||
                                GetGPUInfo(i)._requestedRendererType == RTRendererType.AI_Tools
                                )
                        {
                            return i;
                        }
                    }
                }

                return -1; //none of this type available

            }

            //special way to find one for "any local"

            for (int i = 0; i < Config.Get().GetGPUCount(); i++)
            {
                // Skip servers owned by other pics (reserved for AutoPic workflows)
                if (PicMain.IsServerOwnedByAnyPic(i)) continue;
                if (skipIgnored && GetGPUInfo(i)._ignoredByExtraGenerators) continue;
                if (!GPUNameMatchesFilter(i, nameMatchFilter)) continue;

                if (GetGPUInfo(i).isLocal && Config.Get().GetGPUInfo(i)._bIsActive)
                {
                    return i;
                }
            }
        }

        return -1; //none available right now
    }

    /// <summary>
    /// Returns true if at least one active GPU exists in the pool that matches the given renderer
    /// type AND the given name filter. Used to validate up-front before queueing per-server
    /// render-count jobs that would otherwise sit unfillable forever.
    /// </summary>
    public bool DoesAnyGPUMatchNameFilter(RTRendererType requestedGPUType, string nameMatchFilter)
    {
        if (string.IsNullOrEmpty(nameMatchFilter)) return true; // no filter == all match

        for (int i = 0; i < GetGPUCount(); i++)
        {
            if (!GetGPUInfo(i)._bIsActive) continue;
            if (!GPUNameMatchesFilter(i, nameMatchFilter)) continue;

            if (requestedGPUType == RTRendererType.Any_Local)
            {
                if (GetGPUInfo(i).isLocal) return true;
                continue;
            }

            if (GetGPUInfo(i)._requestedRendererType == requestedGPUType) return true;

            if (requestedGPUType == RTRendererType.AI_Tools_or_A1111 &&
                (GetGPUInfo(i)._requestedRendererType == RTRendererType.A1111 ||
                 GetGPUInfo(i)._requestedRendererType == RTRendererType.AI_Tools))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsValidGPU(int gpu)
    {
        return (gpu < GetGPUCount() && gpu >= 0);
    }
    public int GetJPGSaveQuality()
    {
        return _jpgSaveQuality;
    }
    public string GetGPUName(int gpu)
    {
        if (IsValidGPU(gpu))
        {
            return "GPUID " + gpu + ": " + m_gpuInfo[gpu].remoteURL;
        } 

        return "bad GPUID: "+gpu;
    }

    public bool GetSafetyFilter() { return m_safetyFilter; }
    public void SetSafetyFilter(bool bNew) 
    {
        
        m_safetyFilter = bNew;
        if (bNew)
            Debug.Log("Safety enabled due to -enable_safety_filter");

    }
    public bool IsAnyGPUFree()
    {

        for (int i=0; i < m_gpuInfo.Count; i++)
        {
            // Skip servers owned by other pics (reserved for AutoPic workflows)
            if (PicMain.IsServerOwnedByAnyPic(i)) continue;
            
            if (!IsGPUBusy(i) && GetGPUInfo(i).isLocal && Config.Get().GetGPUInfo(i)._bIsActive) return true;
        }

        return false;
    }

    public bool IsAnyNonIgnoredGPUFree()
    {
        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            if (PicMain.IsServerOwnedByAnyPic(i)) continue;
            if (GetGPUInfo(i)._ignoredByExtraGenerators) continue;

            if (!IsGPUBusy(i) && GetGPUInfo(i).isLocal && GetGPUInfo(i)._bIsActive) return true;
        }

        return false;
    }

    public string GetConfigText()
    {
        return m_configText;
    }
    public bool IsGPUBusy(int gpuID)
    {
        if (!IsValidGPU(gpuID)) return true;
        
        // GPU is busy if doing actual GPU work OR has pending LLM work
        return m_gpuInfo[gpuID].IsGPUBusy || m_gpuInfo[gpuID].pendingLLMCount > 0;
    }

    public void SetGPUBusy(int gpuID, bool bNew)
    {
        if (IsValidGPU(gpuID))
        {
            m_gpuInfo[gpuID].IsGPUBusy = bNew;

            //visually reflect its state as well
            if (m_gpuInfo[gpuID].buttonScript != null)
                m_gpuInfo[gpuID].buttonScript.OnSetBusy(bNew);
        }
       
    }

    public void ForceClearRuntimeGPUState()
    {
        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            m_gpuInfo[i].IsGPUBusy = false;
            m_gpuInfo[i].pendingLLMCount = 0;
            if (m_gpuInfo[i].buttonScript != null)
                m_gpuInfo[i].buttonScript.OnSetBusy(false);
        }
    }

    /// <summary>
    /// Increment pending LLM count for a server (when a pic starts pre-GPU LLM work targeting this server)
    /// </summary>
    public void IncrementPendingLLM(int gpuID)
    {
        if (IsValidGPU(gpuID))
        {
            m_gpuInfo[gpuID].pendingLLMCount++;
        }
    }

    /// <summary>
    /// Decrement pending LLM count for a server (when pic's LLM work finishes or pic is destroyed)
    /// </summary>
    public void DecrementPendingLLM(int gpuID)
    {
        if (IsValidGPU(gpuID) && m_gpuInfo[gpuID].pendingLLMCount > 0)
        {
            m_gpuInfo[gpuID].pendingLLMCount--;
        }
    }

    public Vector2 GetTopRightPosition(GameObject panel)
    {
        // Get the rectangle transform component of the panel
        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        // Get the position of the panel in screen space
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, rectTransform.position);
        // Calculate the top right position by adding the width and height of the panel to the screen position
        Vector2 topRightPos = new Vector2(screenPos.x + rectTransform.rect.width, screenPos.y + rectTransform.rect.height);
        return topRightPos;
    }

    GameObject FindToolsPanelObject()
    {
        GameObject toolsCanvas = RTUtil.FindIncludingInactive("ToolsCanvas");
        if (toolsCanvas != null)
        {
            Transform panel = toolsCanvas.transform.Find("Panel");
            if (panel != null) return panel.gameObject;
        }

        return RTUtil.FindIncludingInactive("Panel");
    }

    Transform GetServerButtonParent()
    {
        GameObject toolsCanvas = RTUtil.FindIncludingInactive("ToolsCanvas");
        if (toolsCanvas != null) return toolsCanvas.transform;

        GameObject panel = FindToolsPanelObject();
        return panel != null ? panel.transform : transform;
    }

    void KillGeneratedServerDisplayObjects(string objectName)
    {
        Transform parent = GetServerButtonParent();
        if (parent != null)
        {
            RTUtil.KillAllObjectsByName(parent.gameObject, objectName, true);
            return;
        }

        GameObject panel = FindToolsPanelObject();
        if (panel != null)
            RTUtil.KillAllObjectsByName(panel, objectName, true);
    }

    void PositionServerDisplayButton(GameObject buttonObj, int rowIndex)
    {
        if (buttonObj == null) return;

        RectTransform rectTransform = buttonObj.GetComponent<RectTransform>();
        if (rectTransform == null) return;

        rectTransform.anchorMin = new Vector2(1f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(1f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(
            -m_serverButtonRightMargin,
            m_serverButtonTopOffset + m_serverButtonRowSpacing * rowIndex);
    }

    public void AddGPU(GPUInfo g)
    {
        if (g != null && m_configOrderVramGB.TryGetValue(g._configOrder, out float configuredVramGB))
            g._vramGB = configuredVramGB;

        //servers are probed asynchronously, so they don't necessarily arrive in config.txt order.
        //Insert this one so the list stays sorted by _configOrder (its add_server line position).
        //Equal/unset orders keep arrival order, so non-add_server GPUs still land at the end.
        int insertIndex = m_gpuInfo.Count;
        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            if (m_gpuInfo[i]._configOrder > g._configOrder)
            {
                insertIndex = i;
                break;
            }
        }
        m_gpuInfo.Insert(insertIndex, g);

        //re-number every GPU to its new list position (inserting in the middle shifts the rest)
        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            m_gpuInfo[i].localGPUID = i;
        }

        //we have at least one GPU now, kill the no servers button
        KillGeneratedServerDisplayObjects("NoServersButtonPrefab");

        //oh hey, let's also add an onscreen button for it that will open up its webui

        var serverButtonParent = GetServerButtonParent();
        var buttonObj = Instantiate(m_serverButtonPrefab, serverButtonParent);
        g.buttonScript = buttonObj.GetComponent<ServerButtonScript>();
        g.buttonScript.Setup(g);
        buttonObj.name = "ServerButtonPrefab"; //don't change, we delete these by this exact name

        // Re-stack every existing button by its (possibly shifted) localGPUID so the order matches the list.
        foreach (var gi in m_gpuInfo)
        {
            if (gi.buttonScript == null) continue;
            gi.buttonScript.Setup(gi); //refresh the button's cached index/text to match the (possibly renumbered) localGPUID, otherwise a later busy repaint pulls the wrong server's name
            PositionServerDisplayButton(gi.buttonScript.gameObject, gi.localGPUID);
        }

            if (g._requestedRendererType == RTRendererType.AI_Tools || g._requestedRendererType == RTRendererType.A1111)
            {

            //We know how to query for extra info from these kinds of servers.  The problem is we'll assume they share certain info
            //for multiple servers, like checkpoints, otherwise it will get weird in the GUI with different checkpoints for each server

                GameLogic.Get().SetHasControlNetSupport(false);
                GameLogic.Get().ClearControlNetModelDropdown();
                GameLogic.Get().ClearControlNetPreprocessorsDropdown();
                ModelModManager.Get().ClearModItems();

                var webScript2 = CreateWebRequestObject();
                webScript2.StartPopulateSamplersRequest(g);

                var webScriptTemp = CreateWebRequestObject();
                webScriptTemp.StartPopulateModelsRequest(g);

                var webScriptControlnet = CreateWebRequestObject();
                webScriptControlnet.StartPopulateControlNetModels(g);

                var webScriptControlnetModules = CreateWebRequestObject();
                webScriptControlnetModules.StartPopulateControlNetPreprocessors(g);

                var webScriptControlnetSettings = CreateWebRequestObject();
                webScriptControlnetSettings.StartPopulateControlNetSettings(g);

                //lora and embeddings
                var webScript3 = CreateWebRequestObject();
                webScript3.StartPopulateEmbeddingsRequest(g);
            }

        if (g.supportsAITools)
        {
            //learn more about this server, we haven't already run it yet
            var webScript = CreateWebRequestObject();
            webScript.StartConfigRequest(g.localGPUID, g.remoteURL);
        }

    }

  public void SaveConfigToFile()
   {
        try
        {
            using (StreamWriter writer = new StreamWriter(m_configFileName, false))
            {
                writer.Write(m_configText);
            }

        }
        catch (IOException ioex)
        {
            RTConsole.Log("Couldn't write config.txt out. (" + ioex.Message + ")");
        }
    }

    string LoadConfigFromFile()
    {
        SetDefaults();
        string config = "";

        try
        {
            using (StreamReader reader = new StreamReader(m_configFileName))
            {
                config = reader.ReadToEnd();
            }

        }
        catch (FileNotFoundException e)
        {
            RTConsole.Log("No config.txt file, using defaults ("+e.Message+")");
        }
        
        return config;
    }

    /// <summary>
    /// Load and process CrazyCam-specific configuration from config_cam.txt
    /// This handles: add_snapshot_preset, set_crazycam_capture, set_default_gen_width, 
    /// set_default_gen_height, set_bat_sound_volume_mod
    /// </summary>
    void LoadCrazyCamConfig()
    {
        if (!System.IO.File.Exists(m_crazyCamConfigFileName))
        {
            RTConsole.Log("No " + m_crazyCamConfigFileName + " file found, using defaults for CrazyCam settings.");
            return;
        }

        try
        {
            string configText;
            using (StreamReader reader = new StreamReader(m_crazyCamConfigFileName))
            {
                configText = reader.ReadToEnd();
            }

            ProcessCrazyCamConfigString(configText);
            RTConsole.Log("Loaded CrazyCam config from " + m_crazyCamConfigFileName);
        }
        catch (IOException ioex)
        {
            RTConsole.Log("Couldn't read " + m_crazyCamConfigFileName + ": " + ioex.Message);
        }
    }

    /// <summary>
    /// Process CrazyCam-specific configuration commands
    /// </summary>
    void ProcessCrazyCamConfigString(string configText)
    {
        using (var reader = new StringReader(configText))
        {
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                string[] words = line.Trim().Split('|');
                
                if (words[0].StartsWith("#") || string.IsNullOrWhiteSpace(words[0]))
                {
                    continue; // Skip comments and empty lines
                }

                if (words[0] == "add_snapshot_preset")
                {
                    if (words.Length >= 4)
                    {
                        CrazyCamLogic.Get().AddSnapshotPreset(words[1], words[2], words[3]);
                    }
                }
                else if (words[0] == "set_crazycam_capture")
                {
                    // set_crazycam_capture|width|height|fps|
                    int w = _crazyCamRequestedWidth;
                    int h = _crazyCamRequestedHeight;
                    int f = _crazyCamRequestedFPS;
                    if (words.Length > 1) int.TryParse(words[1], out w);
                    if (words.Length > 2) int.TryParse(words[2], out h);
                    if (words.Length > 3) int.TryParse(words[3], out f);

                    _crazyCamRequestedWidth = Mathf.Max(1, w);
                    _crazyCamRequestedHeight = Mathf.Max(1, h);
                    _crazyCamRequestedFPS = Mathf.Max(1, f);
                }
                else if (words[0] == "set_default_gen_width")
                {
                    int width;
                    if (int.TryParse(words[1], out width))
                    {
                        GameLogic.Get().SetGenWidth(width);
                    }
                }
                else if (words[0] == "set_default_gen_height")
                {
                    int height;
                    if (int.TryParse(words[1], out height))
                    {
                        GameLogic.Get().SetGenHeight(height);
                    }
                }
                else if (words[0] == "set_bat_sound_volume_mod")
                {
                    float volMod;
                    float.TryParse(words[1], out volMod);
                    _snapShotBatSoundVolumeMod = volMod;
                }
            }
        }
    }

    void ClearGPU()
    {
        m_gpuInfo = new List<GPUInfo>();

        KillGeneratedServerDisplayObjects("ServerButtonPrefab");
        KillGeneratedServerDisplayObjects("NoServersButtonPrefab");

        GameObject noServersObg = Instantiate(m_noServersButtonPrefab, GetServerButtonParent());
        PositionServerDisplayButton(noServersObg, 0);

        //wire up its button
        var button = noServersObg.GetComponent<Button>();
        button.onClick.AddListener(() => { GameLogic.Get().OnNoServersButtonClicked(); });
    }

    public WebRequestServerInfo CreateWebRequestObject()
    {
        GameObject go = new GameObject("ServerRequest");
        go.transform.parent = transform;
        WebRequestServerInfo webScript = (WebRequestServerInfo)go.AddComponent<WebRequestServerInfo>();
        return webScript;
    }

    /// <summary>
    /// Returns the bearer token for whichever configured ComfyUI server URL is the longest prefix of
    /// anyUrl (so e.g. a /prompt or /view request URL still resolves to its server's token), or null
    /// if no configured server with a token matches. Safe to call before any GPUInfo exists.
    /// </summary>
    public string GetComfyAuthToken(string anyUrl)
    {
        if (string.IsNullOrEmpty(anyUrl) || m_comfyAuthTokens.Count == 0) return null;

        string bestKey = null;
        foreach (var kvp in m_comfyAuthTokens)
        {
            if (anyUrl.StartsWith(kvp.Key) && (bestKey == null || kvp.Key.Length > bestKey.Length))
            {
                bestKey = kvp.Key;
            }
        }
        return bestKey == null ? null : m_comfyAuthTokens[bestKey];
    }

    /// <summary>
    /// Single choke point for ComfyUI auth: if the request's target URL belongs to a configured
    /// server that has a token, attach it as an Authorization: Bearer header. No-op otherwise, so
    /// it's safe (and cheap) to call on every ComfyUI request unconditionally.
    /// </summary>
    public void ApplyComfyAuth(UnityEngine.Networking.UnityWebRequest req)
    {
        if (req == null) return;
        string token = GetComfyAuthToken(req.url);
        if (!string.IsNullOrEmpty(token))
        {
            req.SetRequestHeader("Authorization", "Bearer " + token);
        }
    }

    public void CheckForUpdate()
    {
        GameObject go = new GameObject("UpdateCheck");
        go.transform.parent = transform;
        UpdateChecker webScript = (UpdateChecker)go.AddComponent<UpdateChecker>();
        webScript.StartInitialWebRequest();
    }

    /// <summary>
    /// Check if the active LLM provider is Ollama. Now delegates to LLMSettingsManager.
    /// </summary>
    public bool GetGenericLLMIsOllama() 
    { 
        var mgr = LLMSettingsManager.Get();
        return mgr != null && mgr.GetActiveProvider() == LLMProvider.Ollama;
    }

    /// <summary>
    /// Check if the active LLM provider is llama.cpp. Now delegates to LLMSettingsManager.
    /// </summary>
    public bool GetGenericLLMIsLlamaCpp() 
    { 
        var mgr = LLMSettingsManager.Get();
        return mgr != null && mgr.GetActiveProvider() == LLMProvider.LlamaCpp;
    }
    public void ProcessConfigString(string newConfig)
    {
        SetDefaults();
        //ImageGenerator.Get().ShutdownAllGPUProcesses();
        m_safetyFilter = false;

        //reset old config. This will likely do bad things if you're using GPUs at the time of loading
        ClearGPU();
        m_comfyAuthTokens.Clear();
        m_configOrderVramGB.Clear();
        CrazyCamLogic.Get().ClearSnapshotPresets();

        m_configText = newConfig;

        //process it line by line

        int serverConfigOrder = 0; //incremented per add_server so the GPU list can be kept in config.txt order

        using (var reader = new StringReader(m_configText))
        {
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                
                // Do something with the line
                string[] words = line.Trim().Split('|');
                if (words[0] == "-enable_safety_filter" || words[0] == "enable_safety_filter")
                {
                    //another way to disable the safety filter, possibly the only way when it comes to say,
                    //a web build
                    SetSafetyFilter(true);
                } else
                if (words[0] == "add_server")
                {
                    //Debug.Log("Adding server " +words[1]);
                    //let's ask the server what it can do, and add its virtual gpus to our list if possible
                  
                    var webScript = CreateWebRequestObject();

                    // Any field after the URL that starts with "token=" is the optional auth bearer
                    // token; the first other field (if any) is still the display name. Order doesn't
                    // matter and a token-only line (no name) is fine. Fully backward compatible.
                    string extra = "";
                    string authToken = "";

                    for (int wi = 2; wi < words.Length; wi++)
                    {
                        string field = words[wi];
                        if (field.StartsWith("token="))
                        {
                            authToken = field.Substring("token=".Length).Trim();
                        }
                        else if (extra == "")
                        {
                            extra = field;
                        }
                    }

                    if (!string.IsNullOrEmpty(authToken))
                    {
                        m_comfyAuthTokens[words[1]] = authToken;
                    }

                    webScript.StartComfyUIRequest(-1, words[1], extra, serverConfigOrder, authToken);
                    serverConfigOrder++;
                } else
                if (words[0] == "add_snapshot_preset")
                {
                    //Debug.Log("Adding snapshot preset " +words[1]);
                    CrazyCamLogic.Get().AddSnapshotPreset(words[1], words[2], words[3]);
                }
                else
                if (words[0] == "set_default_sampler")
                {
                    GameLogic.Get().SetSamplerByName(words[1]);
                    m_defaultSampler = words[1];
                }
                else
                    
                if (words[0] == "set_default_steps")
                {
                    int steps;

                    int.TryParse(words[1], out steps);

                    GameLogic.Get().SetSteps(steps);
                }  else
                if (words[0] == "set_image_editor")
                {
                    m_imageEditorPathAndExe = words[1];
                } 
                else  // NEW: default image dimensions
                if (words[0] == "set_default_gen_width")
                {
                    int width;
                    if (int.TryParse(words[1], out width))
                    {
                        GameLogic.Get().SetGenWidth(width);
                    }
                }
                else if (words[0] == "set_default_gen_height")
                {
                    int height;
                    if (int.TryParse(words[1], out height))
                    {
                        GameLogic.Get().SetGenHeight(height);
                    }
                }
                else
                 if (words[0] == "set_openai_gpt4_model")
                {
                    _openAI_APIModel = words[1];
                }
                else
                if (words[0] == "set_texgen_webui_address")
                {
                    _texgen_webui_address = words[1];
                }
                else
                if(words[0] == "set_generic_llm_address")
                {
                    _texgen_webui_address = words[1];
                }
                else
                 if (words[0] == "set_generic_llm_api_key")
                {
                    _texgen_webui_APIKey = words[1];
                }else
                if (words[0] == "set_generic_llm_system_keyword")
                {
                    m_systemName = words[1];
                }
                else
                 if (words[0] == "set_generic_llm_assistant_keyword")
                {
                    m_assistantName = words[1];
                }
                else
                   if (words[0] == "set_generic_llm_user_keyword")
                {
                    m_userName = words[1];
                }
                else if (words[0] == "set_generic_llm_mode")
                {
                    _genericLLMMode = words[1];
                }
                else
                if (words[0] == "set_anthropic_ai_key")
                {
                    _anthropicAI_APIKey = words[1];
                }
                else if (words[0] == "set_anthropic_ai_model")
                {
                    _anthropicAI_APIModel = words[1];
                }
                else if (words[0] == "set_anthropic_ai_endpoint")
                {
                    _anthropicAI_APIEndpoint = words[1];
                }
                else 
                if (words[0] == "set_openai_gpt4_endpoint")
                        {
                    _openai_gpt4_endpoint = words[1];
                }
              
                else
 
                if (words[0] == "set_max_fps")
                {
                    int maxFPS;

                    int.TryParse(words[1], out maxFPS);

                    Application.targetFrameRate = maxFPS;
                }
                else if (words[0] == "set_crazycam_capture")
                {
                    // set_crazycam_capture|width|height|fps|
                    int w = _crazyCamRequestedWidth;
                    int h = _crazyCamRequestedHeight;
                    int f = _crazyCamRequestedFPS;
                    if (words.Length > 1) int.TryParse(words[1], out w);
                    if (words.Length > 2) int.TryParse(words[2], out h);
                    if (words.Length > 3) int.TryParse(words[3], out f);

                    _crazyCamRequestedWidth = Mathf.Max(1, w);
                    _crazyCamRequestedHeight = Mathf.Max(1, h);
                    _crazyCamRequestedFPS = Mathf.Max(1, f);
                } else if (words[0] == "set_bat_sound_volume_mod")
                {
                    float volMod;
                    float.TryParse(words[1], out volMod);
                    _snapShotBatSoundVolumeMod = volMod;
                }
                else if (words[0] == "add_generic_llm_parm")
                {
                    LLMParm p = new LLMParm();
                    p._key = words[1];
                    p._value = words[2];
                    m_llmParms.Add(p);
                }
                else if (words[0] == "set_gpu_vram")
                {
                    // set_gpu_vram|gpuID|gigabytes|
                    // Records user-declared per-GPU VRAM so AI Chat can show it to the LLM.
                    // Pure annotation - we never auto-detect VRAM since servers may be remote.
                    if (words.Length >= 3
                        && int.TryParse(words[1], out int gpuId)
                        && TryParseConfigFloat(words[2], out float gb))
                    {
                        m_configOrderVramGB[gpuId] = Mathf.Max(0f, gb);
                        if (gpuId >= 0 && gpuId < m_gpuInfo.Count)
                        {
                            m_gpuInfo[gpuId]._vramGB = gb;
                        }
                        else
                        {
                            RTConsole.Log($"set_gpu_vram: gpuID {gpuId} out of range (have {m_gpuInfo.Count} GPUs).");
                        }
                    }
                }
                else if (words[0] == "set_jpg_save_quality")
                {
                    int quality;
                    int.TryParse(words[1], out quality);
                    _jpgSaveQuality = quality;

                }
                else
                if (words[0] == "set_default_audio_prompt")
                {
                    _defaultAudioPrompt = words[1];
                }
                else if (words[0] == "set_default_negative_audio_prompt" || words[0] == "set_default_audio_negative_prompt")
                {
                    _defaultAudioNegativePrompt = words[1];
                }
                else if (words[0] == "set_text_to_speech_provider")
                {
                    _textToSpeechProvider = words.Length > 1 ? ParseTextToSpeechProvider(words[1]) : TextToSpeechProvider.None;
                }
                else if (words[0] == "set_elevenlabs_api_key" || words[0] == "set_eleven_labs_api_key")
                {
                    _elevenLabs_APIKey = words.Length > 1 ? words[1] : "";
                }
                else if (words[0] == "set_elevenlabs_voice_id" || words[0] == "set_eleven_labs_voice_id" ||
                         words[0] == "set_elevenlabs_voice" || words[0] == "set_eleven_labs_voice")
                {
                    _elevenLabs_voiceID = words.Length > 1 ? words[1] : "";
                }
                else
                {
                    //Debug.Log("Processing " + line);
                }
            }
        }
        // LLM server detection is now handled by LLMSettingsManager
    }

    public void SendRequestToAllServers(string optionKey, string optionValue)
    {
        for (int i=0; i < m_gpuInfo.Count;i++)
        {
            var webScript = CreateWebRequestObject();
            webScript.SendServerConfigRequest(i, optionKey, optionValue);
        }
    }

    public bool AllGPUsSupportAITools()
    {

        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            if (!m_gpuInfo[i].isLocal || !Config.Get().GetGPUInfo(i)._bIsActive)
            {
                continue;
            }
            if (!m_gpuInfo[i].supportsAITools) return false;
        }

        return true;
    }
    public GPUInfo GetGPUInfo(int index) 
    {
      
        if (index < 0)
        {
            //set assert
            Debug.Log("Bad GPU Info!");

        }
        return m_gpuInfo[index]; 
    }
    public int GetGPUCount() { return m_gpuInfo.Count; }

    public string GetServerNameByGPUID(int gpuID)
    {
        if (gpuID < 0 || gpuID >= m_gpuInfo.Count) return "(none)";
        
        if (m_gpuInfo[gpuID]._name.Length > 0) return m_gpuInfo[gpuID]._name+" Server " +m_gpuInfo[gpuID].localGPUID;

        return "Server " + m_gpuInfo[gpuID].localGPUID;
    }
    public string GetServerAddressByGPUID(int gpuID)
    {
        if (gpuID < 0 || gpuID >= m_gpuInfo.Count) return "";
        return m_gpuInfo[gpuID].remoteURL;
    }

    public string GetGenericLLMParm(string key, List<LLMParm> llmParms = null)
    {
        if (llmParms == null)
        {
            llmParms = m_llmParms;
        }

        string value = "";
        foreach (LLMParm p in llmParms)
        {
            if (p._key == key) value = p._value;
        }
        return value;
    }

    public bool IsGPUOfThisType(int GPUID, RTRendererType renderer)
    {

        if (renderer == RTRendererType.Any_Local && m_gpuInfo[GPUID].isLocal) return true;
        if (renderer == RTRendererType.AI_Tools_or_A1111)
        {
            if (m_gpuInfo[GPUID]._requestedRendererType == RTRendererType.AI_Tools || m_gpuInfo[GPUID]._requestedRendererType == RTRendererType.A1111) return true;
        }

        if (m_gpuInfo[GPUID]._requestedRendererType == renderer) return true;

        return false;
    }
    public bool DoesGPUExistForThatRenderer(RTRendererType renderer)
    {
   
        for (int i = 0; i < m_gpuInfo.Count; i++)
        {
            if (IsGPUOfThisType(i, renderer)) return true;
        }

        return false;
    }
    static public Config Get() { return _this; }
}
