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

public enum LLM_Type
{
    OpenAI_API,
    GenericLLM_API,
    Anthropic_API
}

public enum RTRendererType
{
    ComfyUI,
    OpenAI_Image, //openai gpt-image
    Any_Local,
    A1111,
    AI_Tools,
    AI_Tools_or_A1111 //(this means either A1111 or AIT)
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
    public bool isLocal = true; //false would mean an unlimited API like OpenAI's Image API.  Local means TextGen WebUI, AI Tools server or ComfyUI (doesn't actually have to be local)
    public bool _usesDetailedPrompts = false; //simple is the default
    public int _comfyUIWorkFlowOverride = -1;
    public bool _bIsActive = true;
    public string _jobListOverride = "";
    public string _autoPicOverride = ""; // Empty = use global AutoPic setting, otherwise the preset filename to use
    public string _name = ""; //if blank, we'll use our own

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
            if (trimmed.StartsWith("-") || trimmed.Length == 0) continue; // Skip comments/empty
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


public class Config : MonoBehaviour
{  
    public static bool _isTestMode = false; //could do anything, _testMode is checked by random functions
  
    List<GPUInfo> m_gpuInfo = new List<GPUInfo>();
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
  
    float m_version = 2.12f;
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

        _elevenLabs_APIKey = "";
     _elevenLabs_voiceID = "";
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

    /// <summary>
    /// Checks if an OpenAI API key is available and adds a virtual OpenAI_Image GPU if needed.
    /// This allows using the OpenAI Image renderer without needing an add_server command.
    /// </summary>
    public void TryAddOpenAIImageGPU()
    {
        // Check if we already have an OpenAI_Image GPU
        for (int i = 0; i < GetGPUCount(); i++)
        {
            if (GetGPUInfo(i)._requestedRendererType == RTRendererType.OpenAI_Image)
            {
                Debug.Log("TryAddOpenAIImageGPU: Already have OpenAI_Image GPU at index " + i);
                return; // Already have one
            }
        }

        // Check if OpenAI API key is available
        string apiKey = GetOpenAI_APIKey();
        Debug.Log("TryAddOpenAIImageGPU: API key length = " + (apiKey != null ? apiKey.Length.ToString() : "null"));
        
        if (!string.IsNullOrEmpty(apiKey) && apiKey.Length > 10)
        {
            // Add a virtual OpenAI_Image GPU
            GPUInfo gpuInfo = new GPUInfo();
            gpuInfo._requestedRendererType = RTRendererType.OpenAI_Image;
            gpuInfo.isLocal = false; // OpenAI API is not local
            gpuInfo.remoteURL = "https://api.openai.com";
            gpuInfo._name = "OpenAI Image";
            gpuInfo._bIsActive = true;
            AddGPU(gpuInfo);
            
            Debug.Log("TryAddOpenAIImageGPU: Added OpenAI Image GPU successfully");
            RTConsole.Log("Added OpenAI Image GPU (API key found)");
        }
        else
        {
            Debug.Log("TryAddOpenAIImageGPU: No valid API key found, cannot add OpenAI GPU");
        }
    }

    public void PopulateRendererDropDown(TMP_Dropdown rendererSelectionDropdown)
    {
        //populate the list based on the enums from RTServerType
        rendererSelectionDropdown.ClearOptions();
        List<string> options = new List<string>();
        int count = 0;
        foreach (RTRendererType r in System.Enum.GetValues(typeof(RTRendererType)))
        {
            string option = r.ToString().Replace("_", " "); // Replace underscore with space
            options.Add(option);

            count++;
            if (count == 2)
            {
                break; //hack to not show the rest of the types, I don't use them anymore
            }
        }
        rendererSelectionDropdown.AddOptions(options);
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

    public int GetFirstGPUIncludingOpenAI()
    {
        // First, prefer an OpenAI_Image GPU if available
        for (int i = 0; i < GetGPUCount(); i++)
        {
            if (GetGPUInfo(i)._requestedRendererType == RTRendererType.OpenAI_Image && GetGPUInfo(i)._bIsActive)
            {
                return i;
            }
        }
        
        // If no OpenAI GPU found, return first available GPU
        for (int i = 0; i < GetGPUCount(); i++)
        {
            if (GetGPUInfo(i)._bIsActive)
            {
                return i;
            }
        }

        return -1;
    }

    public int GetFreeGPU(RTRendererType requestedGPUType = RTRendererType.Any_Local, bool bFreeOrBusyIsOk = false)
    {
        //special types

        if (requestedGPUType != RTRendererType.Any_Local)
        {
            for (int i = 0; i < GetGPUCount(); i++)
            {
                // Skip servers owned by other pics (reserved for AutoPic workflows)
                if (PicMain.IsServerOwnedByAnyPic(i)) continue;
                
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
                
                if (GetGPUInfo(i).isLocal && Config.Get().GetGPUInfo(i)._bIsActive)
                {
                    return i;
                }
            }
        }

        return -1; //none available right now
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
            m_gpuInfo[gpuID].buttonScript.OnSetBusy(bNew);
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

    public void AddGPU(GPUInfo g)
    {
        g.localGPUID = m_gpuInfo.Count;

        m_gpuInfo.Add(g);

        //we have at least one GPU now, kill the no servers button
        RTUtil.KillAllObjectsByName(RTUtil.FindIncludingInactive("Panel").gameObject, "NoServersButtonPrefab", true);

        //oh hey, let's also add an onscreen button for it that will open up its webui

        //first, get the menu panel object we'll parent to
        var panel = RTUtil.FindIncludingInactive("Panel");
        var buttonObj = Instantiate(m_serverButtonPrefab, panel.transform);
        g.buttonScript = buttonObj.GetComponent<ServerButtonScript>();
        g.buttonScript.Setup(g);
        buttonObj.name = "ServerButtonPrefab"; //don't change, we delete these by this exact name
        //move it down
        float spacerY = -20;
        var vPos = buttonObj.transform.localPosition;

        //replace X, dynamically adjust to how big the main tool panel is now? Well, I tried for 1 minute and it seems tricky due to parenting/etc so forget it, I'll just move the
        //prefab for now
        //Debug.Log("Top right of panel is " + GetTopRightPosition(panel));

        //use existing Y and just add some spacing
        vPos.y += spacerY* g.localGPUID;
        buttonObj.transform.localPosition = vPos;

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

        RTUtil.KillAllObjectsByName(RTUtil.FindIncludingInactive("Panel").gameObject, "ServerButtonPrefab", true);
        RTUtil.KillAllObjectsByName(RTUtil.FindIncludingInactive("Panel").gameObject, "NoServersButtonPrefab", true);

        GameObject noServersObg = Instantiate(m_noServersButtonPrefab, RTUtil.FindIncludingInactive("Panel").transform);

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
        CrazyCamLogic.Get().ClearSnapshotPresets();

        m_configText = newConfig;

        //process it line by line

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

                    string extra = "";

                    if (words.Length > 2)
                    {
                        extra = words[2];
                    }

                    webScript.StartComfyUIRequest(-1, words[1], extra);
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
                else if (words[0] == "set_default_negative_audio_prompt")
                {
                    _defaultAudioNegativePrompt = words[1];
                }
                else
                {
                    //Debug.Log("Processing " + line);
                }
            }
        }


        // LLM server detection is now handled by LLMSettingsManager
        
        // After processing config, check if we should add an OpenAI Image GPU
        TryAddOpenAIImageGPU();
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
