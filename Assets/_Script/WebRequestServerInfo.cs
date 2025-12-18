using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
//TODO: Stop using two json systems, just why?!
using MiniJSON;
using SimpleJSON;
using System.Globalization;
using System.IO;


public class WebRequestServerInfo : MonoBehaviour
{
    const int m_timesToTry = 2;
    int m_timesTried = 0;

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }

    // Normalizes certain verbose GPU names to preferred short forms for UI display
    private string NormalizeGPUName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (name.Contains("NVIDIA RTX PRO 6000 Blackwell Max-Q Workstation Edition") ||
            name.Contains("NVIDIA RTX PRO 6000 Blackwell Max-Q") ||
            name.Contains("RTX PRO 6000 Blackwell Max-Q Workstation Edition") ||
            name.Contains("RTX PRO 6000 Blackwell Max-Q"))
        {
            return "RTX PRO 6000";
        }

        return name;
    }

    IEnumerator GetRequestToCheckForComfyUI(int gpuID, String server, string optionalName)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/prompt";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Checking server " + serverClickableURL + "...");
        
        // First, try to get system stats to extract GPU name
        string gpuNameFromStats = "";
        var statsURL = server + "/system_stats";
        using (var statsRequest = UnityWebRequest.Get(statsURL))
        {
            yield return statsRequest.SendWebRequest();
            
            if (statsRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    // Parse the system_stats response
                    var statsDict = Json.Deserialize(statsRequest.downloadHandler.text) as Dictionary<string, object>;
                    if (statsDict != null && statsDict.ContainsKey("devices"))
                    {
                        var devices = statsDict["devices"] as List<object>;
                        if (devices != null && devices.Count > 0)
                        {
                            // Get the first device
                            var firstDevice = devices[0] as Dictionary<string, object>;
                            if (firstDevice != null && firstDevice.ContainsKey("name"))
                            {
                                string fullDeviceName = firstDevice["name"].ToString();
                                // Extract just the GPU model name (e.g., "NVIDIA GeForce RTX 4090" from "cuda:0 NVIDIA GeForce RTX 4090 : cudaMallocAsync")
                                // Split by space and look for the GPU model
                                string[] parts = fullDeviceName.Split(' ');
                                // Skip "cuda:0" and find the actual GPU name
                                if (parts.Length > 1)
                                {
                                    // Find where NVIDIA/AMD/Intel starts and take from there
                                    int startIdx = -1;
                                    for (int i = 0; i < parts.Length; i++)
                                    {
                                        if (parts[i].Contains("NVIDIA") || parts[i].Contains("AMD") || parts[i].Contains("Intel"))
                                        {
                                            startIdx = i;
                                            break;
                                        }
                                    }
                                    
                                    if (startIdx >= 0)
                                    {
                                        // Take everything from GPU vendor to before the colon (if present)
                                        List<string> gpuParts = new List<string>();
                                        for (int i = startIdx; i < parts.Length; i++)
                                        {
                                            if (parts[i].Contains(":"))
                                                break;
                                            // Skip "NVIDIA", "GeForce", and "PCIe" to make the name shorter
                                            if (parts[i] != "NVIDIA" && parts[i] != "GeForce" && parts[i] != "PCIe")
                                            {
                                                gpuParts.Add(parts[i]);
                                            }
                                        }
                                        gpuNameFromStats = string.Join(" ", gpuParts);
                                    }
                                }
                                
                                gpuNameFromStats = NormalizeGPUName(gpuNameFromStats);
                                Debug.Log("Detected GPU from system_stats: " + gpuNameFromStats);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Error parsing system_stats: " + e.Message);
                }
            }
            else
            {
                Debug.Log("Could not fetch system_stats from " + statsURL + " - will use default name");
            }
        }
        
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Result is " + postRequest.result);

                if (postRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    RTConsole.Log("Server " + serverClickableURL + " isn't a ComfyUI server or is down!");
                    var webScript = Config.Get().CreateWebRequestObject();
                    webScript.StartConfigRequest(-1, server);
                    //either way, we're done with us
                    GameObject.Destroy(gameObject);
                    yield break;
                }


                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {
                    //well, let's try again before we say we failed.
                    Debug.Log("Checking server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }

                RTConsole.Log("Error connecting to server " + serverClickableURL + ". (" + postRequest.error + ")  Are you sure it's up and this address/port is right? It must be running with the --api parm. (and --listen if not on this machine)");
                RTConsole.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values

                var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;

                //log the dict value of context
                String serverName = "ComfyUI";
                
                // Priority: 1) Optional name from config, 2) GPU name from stats, 3) empty
                string nameToUse = "";
                if (!string.IsNullOrEmpty(optionalName))
                {
                    nameToUse = optionalName;
                    serverName = optionalName;
                }
                else if (!string.IsNullOrEmpty(gpuNameFromStats))
                {
                    nameToUse = gpuNameFromStats;
                    serverName = gpuNameFromStats;
                }

                // Final normalization pass for display name
                nameToUse = NormalizeGPUName(nameToUse);
                serverName = NormalizeGPUName(serverName);

                RTConsole.Log("CONNECTED: " + serverClickableURL + " (" + serverName + ") ");

                //it only has an "exec_info" in it problem, not bothering to check it

                    GPUInfo g;
                    //error checking?  that's for wimps
                    g = new GPUInfo();
                    g.remoteURL = server;
                    g.remoteGPUID = 0; //hardcoded
                    g.supportsAITools = false;
                    g._requestedRendererType = RTRendererType.ComfyUI;
                    g._usesDetailedPrompts = true; //we're assuming ComfyUI is always FLUX, probably a bad assumption
                    g._name = nameToUse;

                Config.Get().AddGPU(g);
               
            }
        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }

    public void StartConfigRequest(int gpuID, string remoteURL)
    {
        StartCoroutine(GetConfigRequest(gpuID, remoteURL));
    }
    public void StartComfyUIRequest(int gpuID, string remoteURL, string optionalName)
    {
        StartCoroutine(GetRequestToCheckForComfyUI(gpuID, remoteURL, optionalName));
    }
    public void StartPopulateModelsRequest(GPUInfo g)
    {
        StartCoroutine(GetModelsRequest(g.localGPUID, g.remoteURL));
    }

    public void StartPopulateControlNetModels(GPUInfo g)
    {
        StartCoroutine(GetControlNetModelsRequest(g.localGPUID, g.remoteURL));
    }

    public void StartPopulateControlNetPreprocessors(GPUInfo g)
    {
        StartCoroutine(GetControlNetPreprocessorsRequest(g.localGPUID, g.remoteURL));
    }

    public void StartPopulateControlNetSettings(GPUInfo g)
    {
        StartCoroutine(GetControlNetSettingsRequest(g.localGPUID, g.remoteURL));
    }


    public void StartPopulateSamplersRequest(GPUInfo g)
    {
        StartCoroutine(GetSamplersRequest(g.localGPUID, g.remoteURL));
    }

    public void StartPopulateLorasRequest(GPUInfo g)
    {
        StartCoroutine(GetLorasRequest(g.localGPUID, g.remoteURL));
    }

    public void StartPopulateEmbeddingsRequest(GPUInfo g)
    {
        StartCoroutine(GetEmbeddingsRequest(g.localGPUID, g.remoteURL));
    }
    public void SendServerConfigRequest(int gpuID, string optionKey, string optionValue)
    {
        StartCoroutine(GetServerConfigRequest(gpuID, optionKey, optionValue));
    }
    
    IEnumerator GetConfigRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/sdapi/v1/options";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Getting config data from " + serverClickableURL + "...");

    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    RTConsole.Log("Getting config from " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }
                RTConsole.Log("Error getting config from server " + serverClickableURL + ". (" + postRequest.error + ")  Are you sure it's up and this address/port is right? It must be running with the --api parm. (and --listen if not on this machine)");
                RTConsole.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
                if (!Config.Get().IsValidGPU(gpuID) && gpuID != -1)
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    if (gpuID == -1)
                    {
                        GPUInfo temp;
                        //error checking?  that's for wimps
                        temp = new GPUInfo();
                        temp.remoteURL = server;
                        temp.remoteGPUID = 0;
                        temp.supportsAITools = false;
                        Config.Get().AddGPU(temp);
                        gpuID = temp.localGPUID;
                        RTConsole.Log("Connected to server " + temp.localGPUID + ", it's a vanilla AUTOMATIC1111 server, certain AI Tools server only features will be disabled.");
                    }
                    var g = Config.Get().GetGPUInfo(gpuID);

                    g.configDict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    RTConsole.Log("Active model on GPU "+g.localGPUID+": " + g.configDict["sd_model_checkpoint"]);

                    //set currently model if possible, might fail due to race conditions but that's ok, it will get hit later when we have the list
                    GameLogic.Get().SetModelByName(g.configDict["sd_model_checkpoint"].ToString());
              
                    if (g.localGPUID == 0)
                    {
                        if (g.configDict["sd_model_checkpoint"].ToString().Contains("768"))
                        {
                            GameLogic.Get().SetWidthDropdown("768");
                            GameLogic.Get().SetHeightDropdown("768");
                        }
                        if (g.configDict["sd_model_checkpoint"].ToString().Contains("1024"))
                        {
                            GameLogic.Get().SetWidthDropdown("1024");
                            GameLogic.Get().SetHeightDropdown("1024");
                        }
                        if (g.configDict["sd_model_checkpoint"].ToString().Contains("256"))
                        {
                            GameLogic.Get().SetWidthDropdown("256");
                            GameLogic.Get().SetHeightDropdown("256");
                        }
                    }
                }

            }

            //either way, we're done with us
            GameObject.Destroy(gameObject);
        }
    }

    IEnumerator GetModelsRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/sdapi/v1/sd-models";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Getting Models from " + serverClickableURL + "...");
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting models from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }
                RTConsole.Log("Error getting models from server " + serverClickableURL + ". (" + postRequest.error + ")  Are you sure it's up and this address/port is right? It must be running with the --api parm. (and --listen if not on this machine)");
                RTConsole.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    List<object> modelList = Json.Deserialize(postRequest.downloadHandler.text) as List<object>;
                    //Debug.Log("models: ");

                    GameLogic.Get().ClearModelDropdown();
                    GameLogic.Get().ClearRefinerModelDropdown();

                    for (int i = 0; i < modelList.Count; i++)
                    {
                        var modelInfo = modelList[i] as Dictionary<string, object>;
                        // Debug.Log(modelInfo["title"]);
                        string model = modelInfo["title"].ToString();

                        GameLogic.Get().AddModelDropdown(model);
                        GameLogic.Get().AddRefinerModelDropdown(model);

                        if (model.Contains('\\'))
                        {
                            g.serverIsWindows = true; //well, it has a backslash in the filename, must be windows, right?  Slash vs blackslash is a big problem
                            //when servers are mixed, which is why we need to know this if there are sub dirs in model names
                        }
                    }

                    if (g.configDict != null)
                    {
                        //we know what the server it currently set to, we might not always due to race conditions
                        GameLogic.Get().SetModelByName(g.configDict["sd_model_checkpoint"].ToString());
         
                    }
                }

            }

            //either way, we're done with us
            GameObject.Destroy(gameObject);
        }
    }



    IEnumerator GetControlNetModelsRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/controlnet/model_list";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Trying to get a list of models from the ControlNet extension if it exists: " + serverClickableURL + "...");
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting list of controlnet models from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }

                Debug.Log("Didn't get list of controlnet models from server " + serverClickableURL + ". (" + postRequest.error + ") That extension is probably not installed, no biggie.");
                //GameLogic.Get().ShowConsole(true);
            }
            else
            {
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    List<object> modelList = dict["model_list"] as List<object>;
                 
                    GameLogic.Get().ClearControlNetModelDropdown();
                    GameLogic.Get().SetHasControlNetSupport(true);

                    for (int i = 0; i < modelList.Count; i++)
                    {
                       
                        Debug.Log(modelList[i]);
                        string model = modelList[i].ToString();

                       GameLogic.Get().AddControlNetModelDropdown(model);

                    }

                    if (modelList.Count == 0)
                    {
                        RTConsole.Log("CONTROL NET ERROR?  Something is wrong, the server returned an empty list of models.\nTo try to work around this, the default models have been added.  It's a hack and might not work.\nOnly choose models you have installed.");
                        GameLogic.Get().ShowConsole(true);

                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_canny [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_depth [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_hed [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_mlsd [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_normal [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_openpose [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_scribble [fef5e48e]");
                        GameLogic.Get().AddControlNetModelDropdown("control_sd15_seg [fef5e48e]");
                    }
                    GameLogic.Get().SetDefaultControLNetOptions();
                }
            }

            //either way, we're done with us
            GameObject.Destroy(gameObject);
        }
    }


    IEnumerator GetControlNetPreprocessorsRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/controlnet/module_list";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Trying to get a list of modules from the ControlNet extension if it exists: " + serverClickableURL + "...");
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting list of controlnet modules from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }

                Debug.Log("Didn't get list of controlnet modules from server " + serverClickableURL + ". (" + postRequest.error + ") That extension is probably not installed, no biggie.");
                //GameLogic.Get().ShowConsole(true);
            }
            else
            {
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    List<object> modelList = dict["module_list"] as List<object>;

                    GameLogic.Get().ClearControlNetPreprocessorsDropdown();
                    GameLogic.Get().SetHasControlNetSupport(true);

                    for (int i = 0; i < modelList.Count; i++)
                    {
                        Debug.Log(modelList[i]);
                        string model = modelList[i].ToString();
                        GameLogic.Get().AddControlNetPreprocessorsDropdown(model);
                    }

                    if (modelList.Count == 0)
                    {
                        RTConsole.Log("CONTROL NET ERROR?  There are no preprocessors available, according to its API");
                       
                    }
                    GameLogic.Get().SetDefaultControLNetOptions();
                }
            }

            //either way, we're done with us
            GameObject.Destroy(gameObject);
        }
    }



    IEnumerator GetControlNetSettingsRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/controlnet/settings";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Trying to get settings from the ControlNet extension if it exists: " + serverClickableURL + "...");
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting list of controlnet modules from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }

                Debug.Log("Didn't get list of controlnet modules from server " + serverClickableURL + ". (" + postRequest.error + ") That extension is probably not installed, no biggie.");
                //GameLogic.Get().ShowConsole(true);
            }
            else
            {
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;

                    if (dict != null && dict.ContainsKey("control_net_max_models_num"))
                    {
                       
                        GameLogic.Get().SetControlNetMaxModels(
                            Convert.ToInt32(dict["control_net_max_models_num"]));
                    }
                    else
                    {
                        Debug.Log("The key 'control_net_max_models_num' was not found in the JSON.");
                    }
                }
            }

           
        }
        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }

    IEnumerator GetSamplersRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/sdapi/v1/samplers";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Getting Samplers from " + serverClickableURL + "...");
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting samplers from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }
                Debug.Log("Error getting samplers from server " + serverClickableURL + ". (" + postRequest.error + ")  Are you sure it's up and this address/port is right? It must be running with the --api parm. (and --listen if not on this machine)");
                Debug.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    List<object> modelList = Json.Deserialize(postRequest.downloadHandler.text) as List<object>;
                    //Debug.Log("models: ");

                    string originalSampler = Config.Get().GetDefaultSampler();

                    GameLogic.Get().ClearSamplersDropdown();

                    for (int i = 0; i < modelList.Count; i++)
                    {
                        var modelInfo = modelList[i] as Dictionary<string, object>;
                        // Debug.Log(modelInfo["title"]);
                        GameLogic.Get().AddSamplersDropdown(modelInfo["name"].ToString());
                    }

                    GameLogic.Get().SetSamplerByName(originalSampler);

                }

            }

          
        }
        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }


    IEnumerator GetServerConfigRequest(int gpuID, string optionKey, string optionValue)
    {

        if (!Config.Get().IsValidGPU(gpuID))
        {
            Debug.LogError("Bad GPU of " + gpuID);
            GameObject.Destroy(gameObject);
            yield break;
        }
       
        var g = Config.Get().GetGPUInfo(gpuID);

        Config.Get().SetGPUBusy(gpuID, true);

        string json =
        $@"{{
            ""{optionKey}"": ""{optionValue}""
        }}";

        //File.WriteAllText("json_to_send.json", json); //for debugging
        var finalURL = g.remoteURL + "/sdapi/v1/options";

        if (g.serverIsWindows)
        {
            json = json.Replace("/", "\\");
        } else
        {
            json = json.Replace("\\", "/");
        }

        //either way
        json = json.Replace("\\", "\\\\");
       
        
        //write out the request to a .txt file
       // File.WriteAllText("change_model_server_request.txt", json);

        
        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {

                Debug.Log(postRequest.error);
                Debug.Log(postRequest.downloadHandler.text);

                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                    yield break;
                }

                Config.Get().SetGPUBusy(gpuID, false);
               
            }
            else
            {

                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                    yield break;
                }

                Debug.Log("Server " + g.remoteURL+" has finished switching models."); // + postRequest.downloadHandler.text
             
                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                Config.Get().SetGPUBusy(gpuID, false);

            }
        }

        GameObject.Destroy(gameObject);
    }

    IEnumerator GetLorasRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/sdapi/v1/loras";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Getting LORAS from " + serverClickableURL + "...");

      
    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting loras from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }
           }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    List<object> modelList = Json.Deserialize(postRequest.downloadHandler.text) as List<object>;
                    //Debug.Log("models: ");
                 
                    for (int i = 0; i < modelList.Count; i++)
                    {
                        var modelInfo = modelList[i] as Dictionary<string, object>;
                        ModelModItem item = new ModelModItem();

                        // Set properties only if the key exists
                        if (modelInfo.ContainsKey("name")) item.name = modelInfo["name"].ToString();
                        if (modelInfo.ContainsKey("alias")) item.alias = modelInfo["alias"].ToString();
                        if (modelInfo.ContainsKey("path")) item.path = modelInfo["path"].ToString();
                        if (modelInfo.ContainsKey("metadata"))
                        {
                            var meta = modelInfo["metadata"] as Dictionary<string, object>;
                            if (meta != null)
                            {
                                if (meta.ContainsKey("ss_resolution")) item.resolution = meta["ss_resolution"].ToString();
                                if (meta.ContainsKey("ss_sd_model_name")) item.modelName = meta["ss_sd_model_name"].ToString();
                            }

                        
                            if (meta.ContainsKey("ss_tag_frequency"))
                            {
                                var tagFrequency = meta["ss_tag_frequency"] as Dictionary<string, object>;
                                foreach (var tag in tagFrequency)
                                {
                                    var tagData = tag.Value as Dictionary<string, object>;
                                    foreach (var tagString in tagData)
                                    {
                                        item.exampleList.Add(tagString.Key);
                                    }
                                }
                            }

                        }
                        item.type = ModelModItem.ModelType.LORA;

                        ModelModManager.Get().AddModItem(item);

                    }

              
                }

            }

        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }


    IEnumerator GetEmbeddingsRequest(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/sdapi/v1/embeddings";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Getting embeddings from " + serverClickableURL + "...");


    again:
        //Create the request using a static method instead of a constructor

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {

                    //well, let's try again before we say we failed.
                    Debug.Log("Getting embeddings from server " + serverClickableURL + "... (try " + m_timesTried + ")");
                    goto again;
                }
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
                if (!Config.Get().IsValidGPU(gpuID))
                {
                    Debug.LogError("Bad GPU of " + gpuID);
                }
                else
                {
                    var g = Config.Get().GetGPUInfo(gpuID);

                    var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;
                    Dictionary<string, object> loadedDict = null;
                    if (dict.ContainsKey("loaded"))
                    {
                        loadedDict = dict["loaded"] as Dictionary<string, object>;
                    }
                    //Debug.Log("models: ");

                    if (loadedDict != null)
                    {
                        foreach (var model in loadedDict)
                        {
                            var modelInfo = model.Value as Dictionary<string, object>;
                            ModelModItem item = new ModelModItem();
                            item.type = ModelModItem.ModelType.EMBEDDING;
                            // Set properties only if the key exists
                            item.name = model.Key; // set name as model key
                            if (modelInfo.ContainsKey("sd_checkpoint_name")) item.modelName = modelInfo["sd_checkpoint_name"]?.ToString();

                            ModelModManager.Get().AddModItem(item);
                        }
                    }

                    var webScript3 = Config.Get().CreateWebRequestObject();
                    webScript3.StartPopulateLorasRequest(g);


                }

            }

        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }

    
    public void StartDetectLlamaCppServer(string httpDest)
    {
        StartCoroutine(GetRequestToDetectLlamaCpp(httpDest));
    }

    IEnumerator GetRequestToDetectLlamaCpp(string httpDest)
    {
        // Try to access the /models endpoint which is specific to llama.cpp server
        var finalURL = httpDest + "/models";
        
        using (var getRequest = UnityWebRequest.Get(finalURL))
        {
            yield return getRequest.SendWebRequest();
            
            if (getRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    // Parse the response
                    var responseDict = Json.Deserialize(getRequest.downloadHandler.text) as Dictionary<string, object>;
                    
                    // Check if response has the expected llama.cpp structure
                    if (responseDict != null && responseDict.ContainsKey("models") && responseDict.ContainsKey("data"))
                    {
                        // NOTE: llama.cpp detection is now handled by LLMSettingsManager.
                        // This old detection code remains for legacy config.txt compatibility.
                        
                        // Extract and add parameters to m_llmParms
                        var llmParms = Config.Get().GetLLMParms();
                        
                        // Log some useful information and extract model name
                        var models = responseDict["models"] as List<object>;
                        if (models != null && models.Count > 0)
                        {
                            var firstModel = models[0] as Dictionary<string, object>;
                            if (firstModel != null && firstModel.ContainsKey("name"))
                            {
                                string modelName = firstModel["name"].ToString();
                                RTConsole.Log($"llama.cpp server detected at {httpDest} with model: {modelName}");
                                
                                // Add or update the model parameter
                                llmParms.RemoveAll(p => p._key == "model");
                                var modelParm = new LLMParm();
                                modelParm._key = "model";
                                modelParm._value = modelName;
                                llmParms.Add(modelParm);
                            }
                        }
                        
                        // Also check the data array for additional model info
                        var dataArray = responseDict["data"] as List<object>;
                        if (dataArray != null && dataArray.Count > 0)
                        {
                            var firstData = dataArray[0] as Dictionary<string, object>;
                            if (firstData != null)
                            {
                                // Add model ID if available
                                if (firstData.ContainsKey("id"))
                                {
                                    llmParms.RemoveAll(p => p._key == "model_id");
                                    var idParm = new LLMParm();
                                    idParm._key = "model_id";
                                    idParm._value = firstData["id"].ToString();
                                    llmParms.Add(idParm);
                                }
                                
                                // Extract meta information - iterate through all meta fields
                                if (firstData.ContainsKey("meta"))
                                {
                                    var meta = firstData["meta"] as Dictionary<string, object>;
                                    if (meta != null)
                                    {
                                        // Iterate through all meta fields and add them to llmParms
                                        foreach (var kvp in meta)
                                        {
                                            // Remove existing param with same key
                                            llmParms.RemoveAll(p => p._key == kvp.Key);
                                            
                                            // Add new param
                                            var parm = new LLMParm();
                                            parm._key = kvp.Key;
                                            parm._value = kvp.Value.ToString();
                                            llmParms.Add(parm);
                                            
                                            // Log important ones
                                            if (kvp.Key == "n_ctx_train")
                                            {
                                                RTConsole.Log($"Model context size: {kvp.Value}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        RTConsole.Log($"Server at {httpDest} is not detected as llama.cpp (missing expected fields)");
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"Error parsing llama.cpp response: {e.Message}");
                }
            }
            else
            {
                // Not a llama.cpp server or endpoint not available
                RTConsole.Log($"Server at {httpDest} is not detected as llama.cpp (no /models endpoint)");
            }
        }
        
        GameObject.Destroy(gameObject);
    }
    
}
