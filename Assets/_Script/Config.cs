﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DG.Tweening.Plugins.Core.PathCore;
using UnityEngine.UI;
using static System.Net.WebRequestMethods;
using UnityEditor;
using TMPro;
using System;

public enum LLM_Type
{
    OpenAI_API,
    GenericLLM_API,
    Anthropic_API
}

public enum RTRendererType
{
    Any_Local,
    A1111,
    AI_Tools,
    ComfyUI,
    AI_Tools_or_A1111, //(this means either A1111 or AIT)
    OpenAI_Dalle_3 //openai DALL-E
}

public class GPUInfo
{
    public int localGPUID;
    public int remoteGPUID;
    public string remoteURL;
    public bool IsGPUBusy;
    public Dictionary<string, object> configDict = null;
    public bool supportsAITools = false;
    public ServerButtonScript buttonScript = null;
    public bool serverIsWindows = false;
    public RTRendererType _requestedRendererType = RTRendererType.A1111;
    public bool isLocal = true; //false would mean an unlimited API like OpenAI's Dalle3.  Local means TextGen WebUI, AI Tools server or ComfyUI (doesn't actually have to be local)
    public bool _usesDetailedPrompts = false; //simple is the default
    public int _comfyUIWorkFlowOverride = -1;
    public bool _bIsActive = true;
}


public class Config : MonoBehaviour
{  
    public static bool _isTestMode = false; //could do anything, _testMode is checked by random functions
  
    List<GPUInfo> m_gpuInfo = new List<GPUInfo>();
    List<LLMParm> m_llmParms = new List<LLMParm>();

    static Config _this;
    string m_configText; //later move this to a config.txt or something
    const string m_configFileName = "config.txt";
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
    string _elevenLabs_APIKey ;
    string _elevenLabs_voiceID;
    int _jpgSaveQuality;
    public string _texgen_webui_APIKey;

    string _anthropicAI_APIKey;
    string _anthropicAI_APIModel;
    string _anthropicAI_APIEndpoint;
    string _genericLLMMode;
    Boolean m_genericServerIsOllama = false;
    public string GetAISystemWord() { return m_systemName; }
    public string GetAIUserWord() { return m_userName; }
    public string GetAIAssistantWord() { return m_assistantName; }

    public string GetAnthropicAI_APIKey() { return _anthropicAI_APIKey; }
    public string GetAnthropicAI_APIModel() { return _anthropicAI_APIModel; }
    public string GetAnthropicAI_APIEndpoint() { return _anthropicAI_APIEndpoint; }

    string m_defaultSampler = "DPM++ 2M";
    static public string _saveDirName = "output"; //where to save files, relative to the app's root
    public string GetOpenAI_APIKey() { return _openAI_APIKey; }
    public string GetOpenAI_APIModel() { return _openAI_APIModel; }
    public string GetElevenLabs_APIKey() { return _elevenLabs_APIKey; }
    public string GetElevenLabs_voiceID() { return _elevenLabs_voiceID; }
    public List<LLMParm> GetLLMParms() { return m_llmParms; }
    public string GetGenericLLMMode() { return _genericLLMMode; }

    float m_version = 0.96f;
    string m_imageEditorPathAndExe = "none set";
    public string GetVersionString() { return m_version.ToString("0.00"); }
    public float GetVersion() { return m_version; }
    public float GetRequiredServerVersion() { return m_requiredServerVersion; }
    public string GetDefaultSampler() { return m_defaultSampler; }

    public List<AudioClip> m_audioClips;
    public GameObject m_serverButtonPrefab;
    public GameObject m_noServersButtonPrefab;

    void SetDefaults()
    {
     m_assistantName = "assistant";
     m_systemName = "system";
     m_userName = "user";

     _openAI_APIKey = "";
     _openAI_APIModel = "gpt-4o";
     _texgen_webui_address = "localhost:5000";
    _openai_gpt4_endpoint = "https://api.openai.com/v1/chat/completions";
     _elevenLabs_APIKey = "";
     _elevenLabs_voiceID = "";
     _jpgSaveQuality = 80;
      _texgen_webui_APIKey = "none";
      _genericLLMMode = "chat-instruct";
         _anthropicAI_APIKey = "";
     _anthropicAI_APIModel = "claude-3-5-sonnet-latest";
     _anthropicAI_APIEndpoint = "https://api.anthropic.com/v1/complete";
     m_llmParms = new List<LLMParm>();
     m_llmParms.Clear();

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

        ConnectToServers();
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
            m_configText = @"#add as many add_server commands as you want, just replace the localhost:7860 part with the
#server name/ip and port.  You can control any number of renderer servers at the same time.

#Supported server types:  Seth's AI Tools, A1111, ComfyUI supported.  For Dalle-3, don't set here, just enter your OpenAI key below.

#Uncomment below and put your renderer server.  Add more add_server commands to add as many as you want.
#add_server|http://localhost:7860

#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto
#update in here.

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe

#set_default_sampler|DDIM
#set_default_steps|50

#To generate text with the AI Guide features, you need at least one LLM. (or all, you can switch between them in the app)

#OPENAI (works for LLM and Dalle-3 as renderer)
set_openai_gpt4_key|<key goes here>|
set_openai_gpt4_model|gpt-4o|
set_openai_gpt4_endpoint|https://api.openai.com/v1/chat/completions|

#address of your generic LLM to use, can be local, on your LAN, remote, etc (text-generation-webui or TabbyAPI API format)
set_generic_llm_address|localhost:5000|
#if your generic LLM needs a key, enter it here (or leave as ""none"")
set_generic_llm_api_key|none|

#what we tell the model to use. If you notice the llm is forgetting things or messing up, your model might not be an instruct-compatible model, try llama 3.3 with Ollama as a test.
set_generic_llm_mode|chat-instruct|

#this is needed if using an ollama server, otherwise you'll see a ""model is required"" error.  Note that might cause the model to be loaded which means a huge delay at first.
add_generic_llm_parm|model|""llama3.3""|#needed for ollama, the model you want to use
#add_generic_llm_parm|num_ctx|131072|#needed for ollama, the context size you want the model to load with (we'll create a custom profile with an _ait extension)
#add_generic_llm_parm|max_tokens|4096|

#somethings you could play with
#add_generic_llm_parm|stop|[""<`eot_id`>"", ""<`eom_id`>"", ""<`end_header_id`>""]|#Note that ` gets turned into |
#add_generic_llm_parm|stopping_strings|[""<`eot_id`>"", ""<`eom_id`>"", ""<`end_header_id`>""]|

#the following allow you to override the default system, assistant, and user keywords for the generic LLM, if needed.  
#different LLMs are trained on different words, if the llm server you use doesn't hide this from you, you might notice weird
#or buggy behavior if these aren't changed to match what that specific llm wants
#set_generic_llm_system_keyword|system|#default is system
#set_generic_llm_assistant_keyword|assistant|#default is assistant
#set_generic_llm_user_keyword|user|#default is user

            
#Anthropic LLM
set_anthropic_ai_key|<key goes here>|
set_anthropic_ai_model|claude-3-5-sonnet-latest|
set_anthropic_ai_endpoint|https://api.anthropic.com/v1/messages|

";
            
        }

        RTQuickMessageManager.Get().ShowMessage("Connecting...");
        ProcessConfigString(m_configText);
    }

    public void PopulateRendererDropDown(TMP_Dropdown rendererSelectionDropdown)
    {
        //populate the list based on the enums from RTServerType
        rendererSelectionDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (RTRendererType r in System.Enum.GetValues(typeof(RTRendererType)))
        {
            string option = r.ToString().Replace("_", " "); // Replace underscore with space
            options.Add(option);
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
        for (int i = 0; i < GetGPUCount();)
        {
            return i;
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
        
        return m_gpuInfo[gpuID].IsGPUBusy;
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
        g.buttonScript.Setup(g.localGPUID, g.supportsAITools, g._requestedRendererType);
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

    public void SetGenericLLMIsOllama(bool bNew)
    {
        m_genericServerIsOllama = bNew;
    }
    public bool GetGenericLLMIsOllama() { return m_genericServerIsOllama; }
    public void ProcessConfigString(string newConfig)
    {
        SetDefaults();
        ImageGenerator.Get().ShutdownAllGPUProcesses();
        m_safetyFilter = false;

        //reset old config. This will likely do bad things if you're using GPUs at the time of loading
        ClearGPU();
        
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
                    webScript.StartInitialWebRequest(words[1], extra);
                } else
                if (words[0] == "set_default_sampler")
                {
                    GameLogic.Get().SetSamplerByName(words[1]);
                    m_defaultSampler = words[1];
                   
                } else
                if (words[0] == "set_default_steps")
                {
                    int steps;

                    int.TryParse(words[1], out steps);

                    GameLogic.Get().SetSteps(steps);
                }  else
                if (words[0] == "set_image_editor")
                {
                    m_imageEditorPathAndExe = words[1];
                } else

                    
                if (words[0] == "set_openai_gpt4_key")
                {
                    _openAI_APIKey = words[1];
                    if (_openAI_APIKey.Length > 30)
                    {
                        //Could be a valid key.  Create virtual GPU for it
                        GPUInfo g = new GPUInfo();
                        g.remoteURL = "https://platform.openai.com/usage"; //not used
                        g.supportsAITools = false;
                        g.isLocal = false;
                        g._requestedRendererType = RTRendererType.OpenAI_Dalle_3;
                        g._usesDetailedPrompts = false;
                        AddGPU(g);

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
                {
                    //Debug.Log("Processing " + line);
                }
            }
        }


        //Ok, if we got here we've got all our data and can do a little extra work.
        SetGenericLLMIsOllama(false);

        if (_texgen_webui_address != null && _texgen_webui_address.Length > 1 && GetGenericLLMParm("model").Length > 1)
        {
            //Let's start a web request to see if it's a Ollama server
            var webScript = CreateWebRequestObject();
            webScript.StartSetupOLlamaServer(_texgen_webui_address,m_llmParms);

        }
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
        if (gpuID < 0 || gpuID >= m_gpuInfo.Count) return "(no GPUID)";
        return m_gpuInfo[gpuID]._requestedRendererType.ToString();
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
    static public string GetOllamaModelExt() { return "_ait"; }
}
