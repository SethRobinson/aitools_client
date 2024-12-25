using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using SimpleJSON;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;

public class PicTextToImage : MonoBehaviour
{

    float startTime;
    string m_prompt = null;
    string m_negativePrompt = null;
    int m_steps;
    float m_prompt_strength;
    long m_seed = -1;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    public PicMain m_picScript;
    private int m_totalComfySteps = 0;
    
    string m_additionalMessage;
    string m_comfyUIPromptID;
    private ClientWebSocket m_ws;

    private CancellationTokenSource m_cancellationTokenSource;
    public void SetForceFinish(bool bNew)
    {
        if (bNew && m_bIsGenerating)
        {
            m_picScript.SetStatusMessage("(cancelling...)");
            m_picScript.ClearRenderingCallbacks();

            if (gameObject.activeInHierarchy)
            {
                // If the object is still active, use the coroutine for smooth cleanup
                StartCoroutine(CancelRender());
            }
            else
            {
                // If the object is being destroyed, use immediate cancellation
                CancelRenderImmediate();
            }
        }
    }
    private void CancelRenderImmediate()
    {
        if (!m_bIsGenerating || m_gpu == -1)
            return;

        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);
        string url = gpuInfo.remoteURL;

        // Send interrupt request synchronously
        using (var interruptRequest = new UnityWebRequest(url + "/interrupt", "POST"))
        {
            interruptRequest.SendWebRequest();
        }

        // If we have a prompt ID, try to remove it from queue
        if (!string.IsNullOrEmpty(m_comfyUIPromptID))
        {
            string json = JsonUtility.ToJson(new { delete = new[] { m_comfyUIPromptID } });

            using (var queueRequest = UnityWebRequest.PostWwwForm(url + "/queue", "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                queueRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                queueRequest.SetRequestHeader("Content-Type", "application/json");
                queueRequest.SendWebRequest();
            }
        }

        // Close the websocket connection if it exists
        CloseWebSocket();

        // Clean up state
        if (Config.Get().IsValidGPU(m_gpu))
        {
            Config.Get().SetGPUBusy(m_gpu, false);
        }

        m_bIsGenerating = false;
        m_picScript.SetStatusMessage("Cancelled");
        m_comfyUIPromptID = null;
    }


    public void SetSeed(long seed)
    {
        m_seed = seed;
    }
    public bool WasCustomSeedSet()
    {
        return m_seed != -1;
    }


    public void Reset()
    {

        m_prompt = null;
        m_negativePrompt = null;

    }
    public long GetSeed() { return m_seed; }
    public bool IsBusy()
    {
        return m_bIsGenerating;
    }
    public string GetPrompt()
    {
        return m_prompt;
    }

    public string GetNegativePrompt()
    {
        return m_negativePrompt;
    }

    public void SetPrompt(string prompt)
    {
        m_prompt = prompt;
    }
    public void SetNegativePrompt(string prompt)
    {
        m_negativePrompt = prompt;
    }

    public float GetTextStrength() { return m_prompt_strength; }
    public void SetTextStrength(float strength) { m_prompt_strength = strength; }
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (m_bIsGenerating)
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            string timeString = RTUtil.GetTimeAsMinutesSeconds(elapsed);

            if (m_gpu == -1)
            {
                m_picScript.SetStatusMessage($"(killing): {timeString}");
            }
            else
            {
                if (m_additionalMessage != null)
                {
                    m_picScript.SetStatusMessage($"Generate: {timeString}\r\n{m_additionalMessage}");
                }
                else
                {
                    m_picScript.SetStatusMessage($"Generate: {timeString}");
                }
            }
        }
    }
    private void OnDestroy()
    {
        if (m_bIsGenerating)
        {
            SetForceFinish(true);
            Config.Get().SetGPUBusy(m_gpu, false);
        }
    }

    public void SetGPU(int gpuID)
    {
        m_gpu = gpuID;
    }

    private void ExtractTotalSteps(JSONNode jsonNode)
    {
        // Look through all nodes for BasicScheduler
        foreach (var key in jsonNode.Keys)
        {
            if (jsonNode[key].IsObject && jsonNode[key]["class_type"] == "BasicScheduler")
            {
                if (jsonNode[key]["inputs"].HasKey("steps"))
                {
                    m_totalComfySteps = jsonNode[key]["inputs"]["steps"].AsInt;
                    break;
                }
            }
        }
    }

    public void StartWebRequest(bool rerender)
    {

        m_steps = GameLogic.Get().GetSteps();

        if (!rerender || m_prompt == null)
        {
            m_seed = GameLogic.Get().GetSeed();

            if (m_prompt == null)
            {
                m_prompt = "";

                if (m_gpu != -1)
                {
                    if (Config.Get().GetGPUInfo(m_gpu)._requestedRendererType == RTRendererType.ComfyUI)
                    {
                        m_prompt += GameLogic.Get().GetComfyUIPrompt() + " ";
                        //trim whitespace
                        m_prompt = m_prompt.Trim();
                    }
                }


                if (ImageGenerator.Get().IsGenerating())
                {
                    m_prompt += GameLogic.Get().GetModifiedPrompt();
                }
                else
                {
                    m_prompt += GameLogic.Get().GetPrompt();
                }
            }

            if (m_negativePrompt == null)
            {
                m_negativePrompt = GameLogic.Get().GetNegativePrompt();
            }

            m_prompt_strength = GameLogic.Get().GetTextStrengthFloat();
        }

        if (m_seed == -1)
        {
            var rand = new System.Random();
            //let's set it to our own random so we know what it is later
            m_seed = Math.Abs(rand.NextLong());
        }
        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        if (gpuInfo._requestedRendererType == RTRendererType.OpenAI_Dalle_3)
        {
            //TODO;  If we want to show a timer, we would kind of start it here...
            m_picScript.OnRenderWithDalle3();
            return;
        }

        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }
        Config.Get().SetGPUBusy(m_gpu, true);

        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;
        string url = gpuInfo.remoteURL;

        //if a ComfyUI server, we need to use the new API so we'll launch GetRequestComfyUI.  Check with a switch statement

        if (gpuInfo._requestedRendererType == RTRendererType.ComfyUI)
        {
            StartCoroutine(GetRequestComfyUI(m_prompt, m_prompt_strength, url));
        }
        else
        {
            StartCoroutine(GetRequest(m_prompt, m_prompt_strength, url));
        }

    }
    public void StartGenerate()
    {
        StartWebRequest(true);
    }
    public void StartGenerateInitialRender()
    {
        StartWebRequest(false);
    }
    IEnumerator GetRequest(String context, double prompt_strength, string url)
    {
        WWWForm form = new WWWForm();

        String finalURL;

        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();
        bool bRemoveBackground = GameLogic.Get().GetRemoveBackground();

        int genWidth = GameLogic.Get().GetGenWidth();
        int genHeight = GameLogic.Get().GetGenHeight();
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);
        string model = GameLogic.Get().GetActiveModelFilename();
        string samplerName = GameLogic.Get().GetSamplerName();
        bool hiresFix = GameLogic.Get().GetHiresFix();

        string safety_filter = ""; //use whatever the server is set at
        if (Config.Get().GetSafetyFilter())
        {
            safety_filter = $@"""override_settings"": {{""filter_nsfw"": true}},";
        }

        finalURL = url + "/sdapi/v1/txt2img";
        int steps = GameLogic.Get().GetSteps();



        string promptStrString = prompt_strength.ToString("0.0", CultureInfo.InvariantCulture);
        //using the new API which doesn't support alpha masking the subject
        string json =
             $@"{{
        
            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(m_prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(m_negativePrompt)}"",
            ""steps"": {steps},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{promptStrString},
            ""seed"": {m_seed},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{samplerName}"",
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()},

 ""enable_hr"": {hiresFix.ToString().ToLower()},
  ""denoising_strength"": 0.7,
  ""firstphase_width"": 0,
  ""firstphase_height"": 0,
  ""hr_scale"": 2,
      ""refiner_checkpoint"": ""{GameLogic.Get().GetActiveRefinerModelFilename()}"",
      ""refiner_switch_at"": {GameLogic.Get().GetRefinerSwitchAt()}
    
        }}";

        RTConsole.Log("Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","

#if !RT_RELEASE
        // File.WriteAllText("json_to_send.json", json);
#endif

        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            postRequest.SetRequestHeader("Content-Type", "application/json");

            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (" + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(postRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                //Ok, we now have to dig into the response and pull out the json image

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a free to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                /*
                foreach (KeyValuePair<string, JSONNode> kvp in (JSONObject)rootNode)
                {
                    Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
                }
                */

                var images = rootNode["images"];
                Debug.Assert(images.Tag == JSONNodeType.Array);

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i = 0; i < images.Count; i++)
                    {
                        //convert each to be a pic
                        imgDataBytes = Convert.FromBase64String(images[i]);
                        yield return null; //wait a free to lesson the jerkiness
                    }
                }
                else
                {
                    Debug.Log("image data is missing");
                }

                SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
                Sprite s = renderer.sprite;

                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                bool bSuccess = false;
                yield return null; //wait a frame to lesson the jerkiness

                if (texture.LoadImage(imgDataBytes, false))
                {
                    yield return null; //wait a frame to lesson the jerkiness

                    // m_picScript.AddImageUndo();
                    //Debug.Log("Read texture");
                    float biggestSize = Math.Max(texture.width, texture.height);

                    Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
                    renderer.sprite = newSprite;
                    bSuccess = true;

                    if (bRemoveBackground)
                    {
                        m_picScript.FillAlphaMaskWithImageAlpha();
                    }
                    m_picScript.OnImageReplaced();

                    //we've already done the undo, so now let's update with the info we used to make this
                    m_picScript.GetCurrentStats().m_lastPromptUsed = m_prompt;
                    m_picScript.GetCurrentStats().m_lastNegativePromptUsed = m_negativePrompt;
                    m_picScript.GetCurrentStats().m_lastSteps = steps;
                    m_picScript.GetCurrentStats().m_lastCFGScale = (float)prompt_strength;
                    m_picScript.GetCurrentStats().m_lastSampler = samplerName;
                    m_picScript.GetCurrentStats().m_tiling = bTiled;
                    m_picScript.GetCurrentStats().m_fixFaces = bFixFace;
                    m_picScript.GetCurrentStats().m_lastSeed = m_seed;
                    m_picScript.GetCurrentStats().m_lastModel = model;
                    m_picScript.GetCurrentStats().m_bUsingControlNet = false;
                    m_picScript.GetCurrentStats().m_bUsingPix2Pix = false;
                    m_picScript.GetCurrentStats().m_lastOperation = "Generate";
                    m_picScript.GetCurrentStats().m_gpu = m_gpu;

                    m_picScript.SetNeedsToUpdateInfoPanelFlag();
                    m_picScript.AutoSaveImageIfNeeded();
                }
                else
                {
                    Debug.Log("Error reading texture");
                }


                m_picScript.SetStatusMessage("");

                if (bSuccess && Config.Get().IsValidGPU(m_gpu) && m_bIsGenerating)
                {
                    m_bIsGenerating = false;

                    if (!Config.Get().IsGPUBusy(m_gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }

                    //initiate second stage processing?
                    PicUpscale processScript = this.gameObject.GetComponent<PicUpscale>();

                    if (processScript && GameLogic.Get().GetUpscale() > 1.0f)
                    {
                        processScript.SetGPU(m_gpu);
                        processScript.StartWebRequest(false);
                    }
                    else
                    {
                        if (m_picScript.m_onFinishedRenderingCallback != null)
                            m_picScript.m_onFinishedRenderingCallback.Invoke(gameObject);
                    }
                }

                m_bIsGenerating = false;

            }

        }

    }

    public string LoadComfyUIJSon(string fName)
    {
        string tempString = "";


        string finalFileName = "ComfyUI/" + fName;

        try
        {
            using (System.IO.StreamReader reader = new System.IO.StreamReader(finalFileName))
            {
                tempString = reader.ReadToEnd();
            }

        }
        catch (FileNotFoundException e)
        {
            RTConsole.Log("ComfyUI Json prompt " + finalFileName + " not found. (" + e.Message + ")");
        }

        return tempString;
    }
    bool FindAndReplaceValue(JSONNode node, string keyToFind, JSONNode newValue)
    {
        foreach (var key in node.Keys)
        {
            if (node[key].IsObject)
            {
                if (node[key][keyToFind] != null)
                {
                    node[key][keyToFind] = newValue;
                    return true;
                }
                // Recursively search in nested objects
                if (FindAndReplaceValue(node[key], keyToFind, newValue))
                {
                    return true;
                }
            }
        }
        return false;
    }

    void ModifyJsonValue(JSONNode jsonNode, string keyToFind, JSONNode newValue, bool bShowWarning)
    {
        // Recursively find and replace the value
        if (!FindAndReplaceValue(jsonNode, keyToFind, newValue))
        {
            if (bShowWarning)
            {
                Debug.LogWarning($"{keyToFind} not found in the JSON.");
                RTConsole.Log($"{keyToFind} not found in the JSON.");
            }
        }
    }

    bool ReplaceInString(ref string str, string find, string replace)
    {
        if (str.Contains(find))
        {
            str = str.Replace(find, replace);
            return true;
        }
        return false;
    }
    IEnumerator GetRequestComfyUI(String context, double prompt_strength, string url)
    {
        m_comfyUIPromptID = "";

        WWWForm form = new WWWForm();

        String finalURL;

        int genWidth = GameLogic.Get().GetGenWidth();
        int genHeight = GameLogic.Get().GetGenHeight();
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);

        finalURL = url + "/prompt";
        int steps = GameLogic.Get().GetSteps();

        string promptStrString = prompt_strength.ToString("0.0", CultureInfo.InvariantCulture);

        //Load the prompt via 
        string comfyUIGraphJSon = LoadComfyUIJSon(GameLogic.Get().GetActiveComfyUIWorkflowFileName());

        //Replace all instances of <AITOOLS_PROMPT> with m_prompt in comfyUIGraphJSon
        bool bDidFindPromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_PROMPT>", m_prompt);

        JSONNode jsonNode = JSON.Parse(comfyUIGraphJSon);
        ExtractTotalSteps(jsonNode);
        // Modify multiple values
        ModifyJsonValue(jsonNode, "noise_seed", JSONNode.Parse(m_seed.ToString()), true); // Example seed value


        if (!bDidFindPromptTag)
        {
            ModifyJsonValue(jsonNode, "text", m_prompt, true); // override the prompt
        }

        int frameCountOverRide = ComfyUIPanel.Get().GetFrameCount();
        if (frameCountOverRide >= 0)
        {
            ModifyJsonValue(jsonNode, "length", JSONNode.Parse(frameCountOverRide.ToString()), false); //hunyuan
            ModifyJsonValue(jsonNode, "frames_number", JSONNode.Parse(frameCountOverRide.ToString()), false); //ltx
        }


        string modifiedJsonString = jsonNode.ToString();

        string json =
                $@"{{
                ""prompt"": {modifiedJsonString}
            }}";


        RTConsole.Log("ComfyUI: Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","

#if !RT_RELEASE
        File.WriteAllText("comfyui_json_to_send.json", json);
#endif

        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            postRequest.SetRequestHeader("Content-Type", "application/json");

            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (GetRequestComfyUI - " + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(postRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                //Ok, we now have to dig into the response and pull out the json image

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a free to lesson the jerkiness

                m_comfyUIPromptID = rootNode["prompt_id"];
                Debug.Log("Extracted Prompt ID: " + m_comfyUIPromptID);

                //spawn this
                StartCoroutine(GetComfyUIHistory(url));
              

            }

        }

    }


    void SetStatusAdditionalMessage(string message)
    {
        m_additionalMessage = message;
    }
    IEnumerator ConnectWebSocket(string baseUrl)
    {
        string wsUrl = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        wsUrl = wsUrl + "/ws";
        Uri uri;

        try
        {
            m_ws = new ClientWebSocket();
            m_cancellationTokenSource = new CancellationTokenSource();
            uri = new Uri(wsUrl);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error initializing WebSocket: {e.Message}");
            CloseWebSocket();
            yield break;
        }

        var connectTask = m_ws.ConnectAsync(uri, m_cancellationTokenSource.Token);

        while (!connectTask.IsCompleted)
        {
            if (m_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Debug.Log("WebSocket connection cancelled");
                CloseWebSocket();
                yield break;
            }
            yield return null;
        }

        // Handle completion states outside the while loop
        if (connectTask.IsFaulted)
        {
            Debug.LogError($"WebSocket connection error: {connectTask.Exception?.GetBaseException().Message}");
            CloseWebSocket();
            yield break;
        }

        if (connectTask.IsCanceled)
        {
            Debug.Log("WebSocket connection cancelled during connect");
            CloseWebSocket();
            yield break;
        }

        // Start listening for messages only if we successfully connected
        if (m_ws.State == WebSocketState.Open)
        {
            StartCoroutine(ReceiveLoop());
        }
        else
        {
            Debug.LogError("WebSocket failed to enter Open state after connection");
            CloseWebSocket();
        }
    }


    private IEnumerator ReceiveLoop()
    {
        byte[] buffer = new byte[4096];

        while (m_ws != null && m_ws.State == WebSocketState.Open)
        {
            // Check cancellation before starting new receive operation
            if (m_cancellationTokenSource == null || m_cancellationTokenSource.Token.IsCancellationRequested)
            {
                break;
            }

            var segment = new ArraySegment<byte>(buffer);
            var receiveTask = m_ws.ReceiveAsync(segment, m_cancellationTokenSource.Token);

            while (!receiveTask.IsCompleted)
            {
                if (m_ws == null || m_cancellationTokenSource == null ||
                    m_cancellationTokenSource.Token.IsCancellationRequested ||
                    m_ws.State != WebSocketState.Open)
                {
                    yield break;
                }
                yield return null;
            }

            // Handle the completed task
            if (receiveTask.IsFaulted)
            {
                Debug.LogError($"WebSocket receive error: {receiveTask.Exception?.GetBaseException().Message}");
                break;
            }

            if (receiveTask.IsCanceled)
            {
                Debug.Log("WebSocket receive cancelled");
                break;
            }

            // Process the result
            var result = receiveTask.Result;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (!string.IsNullOrEmpty(message))
                {
                    try
                    {
                        ProcessWebSocketMessage(message);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error processing WebSocket message: {e.Message}");
                    }
                }
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("WebSocket received close message");
                break;
            }
        }

        // Cleanup
        if (m_ws != null && m_ws.State == WebSocketState.Open)
        {
            CloseWebSocket();
        }
    }
    private void ProcessWebSocketMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            JSONNode data = JSON.Parse(message);

            if (data == null)
            {
                Debug.LogWarning("Failed to parse WebSocket message as JSON");
                return;
            }

            // Check for progress update events
            if (data.HasKey("type") && data["type"] == "progress")
            {
                if (data.HasKey("data") && data["data"].HasKey("value"))
                {
                    float progress = data["data"]["value"].AsFloat;
                    string progressText = (progress % 1 == 0) ? progress.ToString("F0") : progress.ToString("F1");
                    SetStatusAdditionalMessage($"Step {progressText} of {m_totalComfySteps}");
                }
            }
            // Also check for execution status
            else if (data.HasKey("type") && data["type"] == "executing")
            {
                if (data.HasKey("data") && data["data"].HasKey("node"))
                {
                    string nodeId = data["data"]["node"];
                    SetStatusAdditionalMessage($"Processing node {nodeId}...");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing WebSocket message: {e.Message}");
        }
    }
    IEnumerator GetComfyUIHistory(string url)
    {
        
        if (m_comfyUIPromptID == null || m_comfyUIPromptID == "")
        {
            RTConsole.Log("Bad promptid for some reason, can't continue");
            yield return false;
        }
        string historyURL = url + "/history/" + m_comfyUIPromptID;

        // First connect to WebSocket for progress
        yield return StartCoroutine(ConnectWebSocket(url));

        while (true)
        {
            // Check history for completion
            using (UnityWebRequest historyRequest = UnityWebRequest.Get(historyURL))
            {
                yield return historyRequest.SendWebRequest();

                if (historyRequest.result != UnityWebRequest.Result.Success)
                {
                    string msg = historyRequest.error + " (GetComfyUIHistory - " + Config.Get().GetGPUName(m_gpu) + ")";
                    Debug.Log(msg);
                    RTQuickMessageManager.Get().ShowMessage(msg);
                    Debug.Log(historyRequest.downloadHandler.text);

                    Config.Get().SetGPUBusy(m_gpu, false);
                    m_bIsGenerating = false;
                    m_picScript.SetStatusMessage("Generate error");

                    CloseWebSocket();
                    yield break;
                }

                JSONNode rootNode = JSON.Parse(historyRequest.downloadHandler.text);

                if (rootNode.Count > 0)
                {
                    JSONNode statusNode = rootNode[m_comfyUIPromptID]["status"];
                    JSONNode outputsNode = rootNode[m_comfyUIPromptID]["outputs"];

                    if (statusNode["status_str"] == "success")
                    {
                        foreach (string key in outputsNode.Keys)
                        {
                            JSONNode outputNode = outputsNode[key];
                            foreach (KeyValuePair<string, JSONNode> node in outputNode)
                            {
                                JSONNode filesNode = node.Value;
                                foreach (JSONNode file in filesNode)
                                {
                                    string filename = file["filename"];
                                    string subfolder = file["subfolder"];
                                    string folderType = file["type"];
                                    if (filename != null && subfolder != null && folderType != null)
                                    {
                                        string extension = System.IO.Path.GetExtension(filename).ToLower();
                                        if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif")
                                        {
                                            CloseWebSocket();
                                            StartCoroutine(GetComfyUIImageFile(url, filename, subfolder, folderType));
                                            yield break;
                                        }
                                        else if (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".webp")
                                        {
                                            CloseWebSocket();
                                            StartCoroutine(GetComfyUIMovieFile(url, filename, subfolder, folderType));
                                            yield break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (statusNode["status_str"] == "error")
                    {
                        RTQuickMessageManager.Get().ShowMessage("ComfyUI reports a failed render");
                        if (Config.Get().IsValidGPU(m_gpu) && m_bIsGenerating)
                        {
                            m_bIsGenerating = false;
                            if (!Config.Get().IsGPUBusy(m_gpu))
                            {
                                Debug.LogError("Why is GPU not busy?! We were using it!");
                            }
                            else
                            {
                                Config.Get().SetGPUBusy(m_gpu, false);
                            }
                            CloseWebSocket();
                            yield break;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void CloseWebSocket()
    {
        try
        {
            if (m_ws != null)
            {
                // Cancel any ongoing operations first
                m_cancellationTokenSource?.Cancel();

                // Only try to close if the connection is still open
                if (m_ws.State == WebSocketState.Open)
                {
                    var closeTask = m_ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                    closeTask.Wait(1000); // Wait up to 1 second for clean closure
                }

                // Dispose of the websocket
                m_ws.Dispose();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error closing WebSocket: {e.Message}");
        }
        finally
        {
            m_ws = null;
            if (m_cancellationTokenSource != null)
            {
                m_cancellationTokenSource.Dispose();
                m_cancellationTokenSource = null;
            }
        }
    }

    //this deletes the movies from the servers memory, I think there is some kind of bug where as they stack up, the server finally crashes if you don't do this?
    IEnumerator CleanComfyUITempFiles(string url)
    {
        string clearJson = $@"{{""clear"": true}}";

        string clearURL = url + "/history?" + m_comfyUIPromptID;
        using (var postRequest = UnityWebRequest.PostWwwForm(clearURL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(clearJson);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();
            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (GetComfyUIImageFile -  " + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                RTConsole.Log(msg);

            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text
            }
        }

    }
    IEnumerator GetComfyUIImageFile(string url, string comfyUIfilename, string comfyUIsubfolder,
        string comfyUIfolderType)
    {

        WWWForm form = new WWWForm();
        form.AddField("filename", comfyUIfilename);
        form.AddField("subfolder", comfyUIsubfolder);
        form.AddField("type", comfyUIfolderType);

        // Construct the final URL with query parameters
        string finalURL = url + "/view?" + System.Text.Encoding.UTF8.GetString(form.data);


        RTConsole.Log("ComfyUI: Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","


        using (UnityWebRequest getRequest = UnityWebRequest.Get(finalURL))
        {
            // Start the request
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = getRequest.error + "GetComfyUIImageFile - (" + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(getRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                // Handle the response, which should be a raw PNG file in getRequest.downloadedBytes
                SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
                Sprite s = renderer.sprite;

                Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false); //0 causes errors now?  Weird

                yield return null; //wait a frame to lesson the jerkiness

                if (texture.LoadImage(getRequest.downloadHandler.data, false))
                {
                    yield return null; //wait a frame to lesson the jerkiness

                    // m_picScript.AddImageUndo();
                    //Debug.Log("Read texture");
                    float biggestSize = Math.Max(texture.width, texture.height);

                    Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
                    renderer.sprite = newSprite;

                    m_picScript.OnImageReplaced();

                    m_picScript.GetCurrentStats().m_lastPromptUsed = m_prompt;

                    m_picScript.GetCurrentStats().m_lastSeed = m_seed;

                    m_picScript.GetCurrentStats().m_bUsingControlNet = false;
                    m_picScript.GetCurrentStats().m_bUsingPix2Pix = false;
                    m_picScript.GetCurrentStats().m_lastOperation = "Generate";
                    m_picScript.GetCurrentStats().m_gpu = m_gpu;

                    m_picScript.SetNeedsToUpdateInfoPanelFlag();
                    m_picScript.AutoSaveImageIfNeeded();

                    m_picScript.SetStatusMessage("");
                }

                if (Config.Get().IsValidGPU(m_gpu) && m_bIsGenerating)
                {

                    if (!Config.Get().IsGPUBusy(m_gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }

                    //we're done, can delete us from ComfyUI, may fix comfyui bug with it crashing after too many generations?


                    //dostuff later...
                    m_bIsGenerating = false;

                    if (m_picScript.m_onFinishedRenderingCallback != null)
                        m_picScript.m_onFinishedRenderingCallback.Invoke(gameObject);

                    StartCoroutine(CleanComfyUITempFiles(url));

                }

                yield return null; // wait a frame to lessen the jerkiness


            }
        }
    }

    public IEnumerator CancelRender()
    {
        if (!m_bIsGenerating || m_gpu == -1)
            yield break;

        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);
        string url = gpuInfo.remoteURL;

        // First send interrupt request
        using (var interruptRequest = UnityWebRequest.PostWwwForm(url + "/interrupt", ""))
        {
            yield return interruptRequest.SendWebRequest();

            if (interruptRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to send interrupt request: {interruptRequest.error}");
            }
        }

        // If we have a prompt ID, remove it from queue
        if (!string.IsNullOrEmpty(m_comfyUIPromptID))
        {
            string json = JsonUtility.ToJson(new { delete = new[] { m_comfyUIPromptID } });

            using (var queueRequest = UnityWebRequest.PostWwwForm(url + "/queue", "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                queueRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                queueRequest.SetRequestHeader("Content-Type", "application/json");

                yield return queueRequest.SendWebRequest();

                if (queueRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to remove from queue: {queueRequest.error}");
                }
            }
        }

        // Close the websocket connection if it exists
        CloseWebSocket();

        // Clean up state
        if (Config.Get().IsValidGPU(m_gpu))
        {
            Config.Get().SetGPUBusy(m_gpu, false);
        }

        m_bIsGenerating = false;
        m_picScript.SetStatusMessage("Cancelled");

        // Clear the prompt ID
        m_comfyUIPromptID = null;

        CleanComfyUITempFiles(url);
    }

    IEnumerator GetComfyUIMovieFile(string url, string comfyUIfilename, string comfyUIsubfolder,
       string comfyUIfolderType)
    {

        WWWForm form = new WWWForm();
        form.AddField("filename", comfyUIfilename);
        form.AddField("subfolder", comfyUIsubfolder);
        form.AddField("type", comfyUIfolderType);

        // Construct the final URL with query parameters
        string finalURL = url + "/view?" + System.Text.Encoding.UTF8.GetString(form.data);

        RTConsole.Log("ComfyUI: Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","


        using (UnityWebRequest getRequest = UnityWebRequest.Get(finalURL))
        {
            // Start the request
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = getRequest.error + "GetComfyUIImageFile - (" + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(getRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                // Handle the response, which should be a raw PNG file in getRequest.downloadedBytes

                yield return null; //wait a frame to lesson the jerkiness


                //getRequest.downloadHandler.data is the movie data, we'll need to send it to the movie player system

                //for debugging, save getRequest.downloadHandler.data to disk as test.mp4
                string tempPath = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/temp_movie_") + m_comfyUIPromptID;

                //add the fileextension that get from the server
                string extension = System.IO.Path.GetExtension(comfyUIfilename).ToLower();
                tempPath += extension;

                File.WriteAllBytes(tempPath, getRequest.downloadHandler.data);
                m_picScript.SetStatusMessage("");
                m_picScript.m_picMovie.PlayMovie(tempPath);


                if (Config.Get().IsValidGPU(m_gpu) && m_bIsGenerating)
                {

                    if (!Config.Get().IsGPUBusy(m_gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }

                    //we're done, can delete us from ComfyUI, may fix comfyui bug with it crashing after too many generations?

                    //dostuff later...
                    m_bIsGenerating = false;

                    if (m_picScript.m_onFinishedRenderingCallback != null)
                        m_picScript.m_onFinishedRenderingCallback.Invoke(gameObject);

                    StartCoroutine(CleanComfyUITempFiles(url));

                }

                yield return null; // wait a frame to lessen the jerkiness
            }
        }
    }

}
