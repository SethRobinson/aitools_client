using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DG.Tweening.Plugins.Core.PathCore;
using UnityEngine.UI;

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
    
}

public class Config : MonoBehaviour
{  
    public static bool _isTestMode = false; //could do anything, _testMode is checked by random functions
  
    List<GPUInfo> m_gpuInfo = new List<GPUInfo>();
    
    static Config _this;
    string m_configText; //later move this to a config.txt or something
    const string m_configFileName = "config.txt";
    bool m_safetyFilter = false;
    float m_requiredServerVersion = 0.46f;

    float m_version = 0.74f;
    string m_imageEditorPathAndExe = "none set";
    public string GetVersionString() { return m_version.ToString("0.00"); }
    public float GetVersion() { return m_version; }
    public float GetRequiredServerVersion() { return m_requiredServerVersion; }

    public List<AudioClip> m_audioClips;
    public GameObject m_serverButtonPrefab;
    public GameObject m_noServersButtonPrefab;

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

        if (m_configText == "")
        {
            //default
            m_configText += "#add as many add_server commands as you want, just replace the localhost:7860 part with the\n";
            m_configText += "#server name/ip and port.  You can control any number of servers at the same time.\n";
            m_configText += "\n";
            m_configText += "#You need at least one server running to work. It can be either an automatic1111 Stable Diffusion WebUI server or\n";
            m_configText += "#a Seth's AI Tools server which supports a few more features.  It will autodetect which kind it is.\n";
            m_configText += "\n";
            m_configText += "add_server|http://localhost:7860\n\n";
            m_configText += "#kids around?  Then uncomment below to turn on the NSFW filter. \r\n#enable_safety_filter\r\n\r\n";
            m_configText += "#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto\n";
            m_configText += "#update in here.\n\n";
            m_configText += "set_image_editor|C:\\Program Files\\Adobe\\Adobe Photoshop 2023\\Photoshop.exe\n";
            m_configText += "\n#set_default_sampler|DDIM\n";
            m_configText += "#set_default_steps|50\n";
        }

        RTQuickMessageManager.Get().ShowMessage("Connecting...");
        ProcessConfigString(m_configText);
    }

    public int GetFreeGPU()
    {
        for (int i = 0; i < Config.Get().GetGPUCount(); i++)
        {
            if (!Config.Get().IsGPUBusy(i))
            {
                return i;
            }
        }
        return -1; //none available right now
    }

    public bool IsValidGPU(int gpu)
    {
        return (gpu < GetGPUCount() && gpu >= 0);
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
            if (!IsGPUBusy(i)) return true;
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
        g.buttonScript.Setup(g.localGPUID, g.supportsAITools);
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


        if (g.supportsAITools)
        {
            //learn more about this server, we haven't already run it yet
            var webScript = CreateWebRequestObject();
            webScript.StartConfigRequest(g.localGPUID, g.remoteURL);
        }

    

        if (g.localGPUID == 0)
        {
            //it's the first one, let's get more info and just hope all the servers have the same capabilities.  If one server is missing an extension or model, well, that's on them
         
            var webScript2 = CreateWebRequestObject();
            webScript2.StartPopulateSamplersRequest(g);

            var webScriptTemp = CreateWebRequestObject();
            webScriptTemp.StartPopulateModelsRequest(g);

            GameLogic.Get().SetHasControlNetSupport(false);
            GameLogic.Get().ClearControlNetModelDropdown();
            GameLogic.Get().ClearControlNetPreprocessorsDropdown();
          
            var webScriptControlnet = CreateWebRequestObject();
            webScriptControlnet.StartPopulateControlNetModels(g);

            var webScriptControlnetModules = CreateWebRequestObject();
            webScriptControlnetModules.StartPopulateControlNetPreprocessors(g);

            var webScriptControlnetSettings = CreateWebRequestObject();
            webScriptControlnetSettings.StartPopulateControlNetSettings(g);

            ModelModManager.Get().ClearModItems();

            //lora and embeddings
            var webScript3 = CreateWebRequestObject();
            webScript3.StartPopulateEmbeddingsRequest(g);



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
            Debug.Log("Couldn't write config.txt out. (" + ioex.Message + ")");
        }
    }

    string LoadConfigFromFile()
    {

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
            Debug.Log("No config.txt file, using defaults ("+e.Message+")");
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

    public void ProcessConfigString(string newConfig)
    {

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
                if (words[0] == "set_max_fps")
                {
                    int maxFPS;

                    int.TryParse(words[1], out maxFPS);

                    Application.targetFrameRate = maxFPS;
                }
                else
                {
                    //Debug.Log("Processing " + line);
                }
            }
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
            if (!m_gpuInfo[i].supportsAITools) return false;
        }

        return true;
    }
    public GPUInfo GetGPUInfo(int index) { return m_gpuInfo[index]; }
    public int GetGPUCount() { return m_gpuInfo.Count; }
    static public Config Get() { return _this; }

}
