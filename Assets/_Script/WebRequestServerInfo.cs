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

    public void StartInitialWebRequest(string httpDest, string extra)
    {
        StartCoroutine(GetRequest(httpDest));
    }
    IEnumerator GetRequest(String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/aitools/get_info.json";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Checking server "+ serverClickableURL + "...");
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
                    RTConsole.Log("Server " + serverClickableURL + " isn't an AI Tools variant, trying to connect as ComfyUI...");
                    var webScript = Config.Get().CreateWebRequestObject();
                    webScript.StartComfyUIRequest(-1, server);
                    GameObject.Destroy(gameObject);
                    yield break;
                }

           
                m_timesTried++;
                if (m_timesTried < m_timesToTry)
                {
                    //well, let's try again before we say we failed.
                    Debug.Log("Checking server " + serverClickableURL + "... (try "+m_timesTried+")");
                    goto again;
                }

                RTConsole.Log("Error connecting to server " + serverClickableURL + ". ("+ postRequest.error+ ")  Are you sure it's up and this address/port is right? It must be running with the --api parm. (and --listen if not on this machine)");
                RTConsole.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
           
                var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;

               /*
                if (dict != null)
                { 
                    foreach (KeyValuePair<string, object> kvp in dict)
                    {
                        if (kvp.Value != null)
                        {
                            Debug.Log("Key: " + kvp.Key + " Value: " + kvp.Value.ToString());
                        }
                    }
                }
               */

                //log the dict value of context
                String serverName = dict["name"].ToString();
                float version;

                System.Single.TryParse(dict["version"].ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out version);

                float requiredClient = 0;

                if (dict.ContainsKey("required_client_version"))
                {
                    System.Single.TryParse(dict["required_client_version"].ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out requiredClient);

                    if (requiredClient > Config.Get().GetVersion())
                    {
                        RTConsole.Log("ERROR: The server says this client (V" + Config.Get().GetVersionString() + " is oudated and we should upgrade to V" +
                                requiredClient + " or newer.  Go upgrade! We'll try anyway though.");
                        GameLogic.Get().ShowConsole(true);
                    }
                }

                if (Config.Get().GetRequiredServerVersion() > version)
                {
                    RTConsole.Log("ERROR: The server version is outdated, we required "+ Config.Get().GetRequiredServerVersion()+ " or newer. GO UPGRADE!  Trying anyway though.");
                    GameLogic.Get().ShowConsole(true);
                }

                RTConsole.Log("CONNECTED: "+serverClickableURL+" (" + serverName + ") V" + version + "");

                List<object> gpus = dict["gpu"] as List<object>;
                
                for (int i=0; i < gpus.Count; i++)
               {
                   GPUInfo g;
                   //error checking?  that's for wimps
                   g = new GPUInfo();
                   g.remoteURL = server;
                   g.remoteGPUID = i;
                   g.supportsAITools= true;
                    g._requestedRendererType = RTRendererType.AI_Tools;
                   Config.Get().AddGPU(g);
               }
            }
        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }

    IEnumerator GetRequestToCheckForComfyUI(int gpuID, String server)
    {

        WWWForm form = new WWWForm();
        var finalURL = server + "/prompt";
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        Debug.Log("Checking server " + serverClickableURL + "...");
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
                    RTConsole.Log("Server " + serverClickableURL + " isn't a ComfyUI server, trying to connect as vanilla Automatic1111 type...");
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

                /*
                 if (dict != null)
                 { 
                     foreach (KeyValuePair<string, object> kvp in dict)
                     {
                         if (kvp.Value != null)
                         {
                             Debug.Log("Key: " + kvp.Key + " Value: " + kvp.Value.ToString());
                         }
                     }
                 }
                */

                //log the dict value of context
                String serverName = "ComfyUI";
               

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
    public void StartComfyUIRequest(int gpuID, string remoteURL)
    {
        StartCoroutine(GetRequestToCheckForComfyUI(gpuID, remoteURL));
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

}
