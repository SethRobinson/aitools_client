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
using System.Text;
using System.Web;

public class PicTextToImage : MonoBehaviour
{

    float startTime;
    string m_prompt = null;
    string m_negativePrompt = null;
    string m_audioPrompt = "";
    string m_audioNegativePrompt = "";
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
    ScheduledGPUEvent m_scheduledEvent;
    private CancellationTokenSource m_cancellationTokenSource;
    
    public void SetGPUEvent(ScheduledGPUEvent scheduledEvent)
    {
        m_scheduledEvent = scheduledEvent;
    }

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

        //is gpu valid?
        if (Config.Get().IsValidGPU(m_gpu))
        {

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
        m_scheduledEvent = null;
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

    public void SetAudioPrompt(string prompt)
    {
        m_audioPrompt = prompt;
    }

    public void SetAudioNegativePrompt(string prompt)
    {
        m_audioNegativePrompt = prompt;
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
            if (jsonNode[key].IsObject && (jsonNode[key]["class_type"] == "BasicScheduler"
                || jsonNode[key]["class_type"] == "MMAudioSampler" || jsonNode[key]["class_type"] == "KSampler")
                )
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

        m_seed = GameLogic.Get().GetSeed();

        if (m_seed == -1)
        {
            var rand = new System.Random();
            //let's set it to our own random so we know what it is later
            m_seed = Math.Abs(rand.Next()); //I used to use NextLong but some generators actually can't handle it or something
        }


        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        if (gpuInfo._requestedRendererType == RTRendererType.OpenAI_Image)
        {
            //TODO;  If we want to show a timer, we would kind of start it here...
            m_picScript.OnRenderWithOpenAIImage();
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

    }
    public void StartGenerate()
    {
        StartWebRequest(true);
    }
    public void StartGenerateInitialRender()
    {
        StartWebRequest(false);
    }

    public string LoadComfyUIJSon(string fName, out bool bError)
    {
        string tempString = "";
        bError = false;
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
            string errorMsg = $"Workflow {finalFileName} not found. ({e.Message})";
            RTConsole.Log(errorMsg);
            RTQuickMessageManager.Get().ShowMessage(errorMsg);
            bError = true;
        }
        catch (ArgumentException e)
        {
            string errorMsg = $"Invalid file name {finalFileName}. ({e.Message})";
            RTConsole.Log(errorMsg);
            RTQuickMessageManager.Get().ShowMessage(errorMsg);
            bError = true;
        }
        catch (Exception e)
        {
            string errorMsg = $"Error loading workflow {finalFileName}. ({e.Message})";
            RTConsole.Log(errorMsg);
            RTQuickMessageManager.Get().ShowMessage(errorMsg);
            bError = true;
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
                    //return true;
                    //keep looking for more
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

        String finalURL;

        int genWidth = GameLogic.Get().GetGenWidth();
        int genHeight = GameLogic.Get().GetGenHeight();
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);

        finalURL = url + "/prompt";
        int steps = GameLogic.Get().GetSteps();

        string promptStrString = prompt_strength.ToString("0.0", CultureInfo.InvariantCulture);

        //Load the prompt via 
        string workflowFileName;

        workflowFileName = m_scheduledEvent.workflow;

        // Check for cached API version first
        string cachedApiPath = "ComfyUI/" + workflowFileName.Replace(".json", "_cached_api_version.json");
        string comfyUIGraphJSon = "";
        bool bNeedsConversion = false;
        
        // If a manual _api.json exists, use that instead
        if (!workflowFileName.Contains("_api.json"))
        {
            string manualApiPath = "ComfyUI/" + workflowFileName.Replace(".json", "_api.json");
            if (File.Exists(manualApiPath))
            {
                RTConsole.Log($"Using manual API version: {manualApiPath}");
                workflowFileName = workflowFileName.Replace(".json", "_api.json");
                cachedApiPath = ""; // Don't need cache
            }
        }
        
        // Try to use cached version if it exists and is newer than source
        if (!string.IsNullOrEmpty(cachedApiPath) && File.Exists(cachedApiPath))
        {
            string sourcePath = "ComfyUI/" + workflowFileName;
            if (File.Exists(sourcePath) && 
                File.GetLastWriteTime(cachedApiPath) > File.GetLastWriteTime(sourcePath))
            {
                RTConsole.Log($"Using cached API version: {Path.GetFileName(cachedApiPath)}");
                try
                {
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(cachedApiPath))
                    {
                        comfyUIGraphJSon = reader.ReadToEnd();
                    }
                }
                catch (Exception e)
                {
                    RTConsole.Log($"Failed to read cached file: {e.Message}");
                    comfyUIGraphJSon = ""; // Will fall back to loading original
                }
            }
        }
        
        // If we don't have cached version, load the original
        if (string.IsNullOrEmpty(comfyUIGraphJSon))
        {
            bool bError = false;
            comfyUIGraphJSon = LoadComfyUIJSon(workflowFileName, out bError);

            if (bError)
            {
                FinishUpEverything(false);
                m_picScript.SetStatusMessage("Workflow .json not found");
                yield break;
            }
            
            // Check if this needs conversion (has "nodes" array = full workflow format)
            if (!workflowFileName.Contains("_api.json") && !workflowFileName.Contains("_cached_api_version.json"))
            {
                try
                {
                    JSONNode testNode = JSON.Parse(comfyUIGraphJSon);
                    if (testNode["nodes"] != null)
                    {
                        bNeedsConversion = true;
                        RTConsole.Log($"Workflow {workflowFileName} needs API conversion");
                    }
                }
                catch (Exception)
                {
                    // If can't parse, we'll try to use as-is
                    RTConsole.Log($"Could not parse workflow {workflowFileName} to check format, using as-is");
                }
            }
        }
        
        // Convert if needed
        if (bNeedsConversion && !string.IsNullOrEmpty(cachedApiPath))
        {
            m_picScript.SetStatusMessage("Converting workflow to API format...");
            
            // Create JSON payload for conversion
            string convertPayload = comfyUIGraphJSon;
            
            using (var convertRequest = UnityWebRequest.PostWwwForm(url + "/workflow/convert", "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(convertPayload);
                convertRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                convertRequest.downloadHandler = new DownloadHandlerBuffer();
                convertRequest.SetRequestHeader("Content-Type", "application/json");
                
                yield return convertRequest.SendWebRequest();
                
                if (convertRequest.result == UnityWebRequest.Result.Success)
                {
                    comfyUIGraphJSon = convertRequest.downloadHandler.text;
                    RTConsole.Log($"Successfully converted workflow to API format");
                    
                    // Cache the converted version
                    try
                    {
                        // Ensure directory exists
                        string cacheDir = Path.GetDirectoryName(cachedApiPath);
                        if (!Directory.Exists(cacheDir))
                        {
                            Directory.CreateDirectory(cacheDir);
                        }
                        
                        // Just save the raw response without any formatting
                        File.WriteAllText(cachedApiPath, comfyUIGraphJSon);
                        RTConsole.Log($"Cached API version to: {Path.GetFileName(cachedApiPath)}");
                    }
                    catch (Exception e)
                    {
                        RTConsole.Log($"Failed to cache converted workflow: {e.Message}");
                    }
                }
                else
                {
                    RTConsole.Log($"Failed to convert workflow: {convertRequest.error}");
                    
                    // Check if it's a 405 Method Not Allowed error (endpoint not installed)
                    bool is405Error = convertRequest.responseCode == 405 || 
                                     (convertRequest.error != null && (convertRequest.error.Contains("405") || convertRequest.error.Contains("Method Not Allowed")));
                    
                    if (is405Error)
                    {
                        ShowWorkflowConverterInstallDialog(workflowFileName);
                    }
                    else
                    {
                        //Other error, show quick message
                        RTQuickMessageManager.Get().ShowMessage($"Failed to convert workflow: {convertRequest.error}");
                    }
                    
                    RTConsole.Log($"Server may not have the /workflow/convert endpoint installed");
                    RTConsole.Log("Attempting to use workflow as-is (may fail if not in API format)");
                    // Continue with original - it might work if it's already in API format
                }
            }
        }
        //any custom replace commands in the job._data?
        if (m_scheduledEvent.m_picJob._data.Count > 0)
        {
           for (int i=0; i < m_scheduledEvent.m_picJob._data.Count; i++)
            {
                if (m_scheduledEvent.m_picJob._data[i]._name.ToLower() == "replace")
                {
                    ReplaceInString(ref comfyUIGraphJSon, m_scheduledEvent.m_picJob._data[i]._parm1,
                        JSONNode.Escape(m_scheduledEvent.m_picJob._data[i]._parm2));
                }
                else if (m_scheduledEvent.m_picJob._data[i]._name.ToLower() == "comment")
                {

                }
                else if (m_scheduledEvent.m_picJob._data[i]._name.ToLower() == "copy")
                {
                    //already done earlier
                  //  DoVarCopy(m_scheduledEvent.m_picJob._data[i]._parm1, m_scheduledEvent.m_picJob._data[i]._parm2);
                }
                else
                {
                   // RTConsole.Log("Unknown command in _data: " + m_scheduledEvent.m_picJob._data[i]._name);
                }
            }

            ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedPrompt));
        }

        //Replace all instances of <AITOOLS_PROMPT> with m_prompt in comfyUIGraphJSon
        bool bDidFindPromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedPrompt));
        bool bDidFindNegativePromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_NEGATIVE_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedNegativePrompt));
      

        bool bDidFindAudioPromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_AUDIO_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedAudioPrompt));
        bool bDidFindAudioNegativePromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_AUDIO_NEGATIVE_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedAudioNegativePrompt));
        bool bDidFindSegmentationPromptTag = ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_SEGMENTATION_PROMPT>", JSONNode.Escape(m_scheduledEvent.m_picJob._requestedSegmentationPrompt));

        // Replace all AITOOLS_INPUT_N placeholders (1 through 4) from _inputFilenames array
        for (int i = 0; i < 4; i++)
        {
            if (m_scheduledEvent.m_picJob._inputFilenames[i].Length > 0)
            {
                string placeholder = $"<AITOOLS_INPUT_{i + 1}>";
                ReplaceInString(ref comfyUIGraphJSon, placeholder, 
                    JSONNode.Escape(m_scheduledEvent.m_picJob._inputFilenames[i]));
            }
        }
        
        // Legacy support: also check _parm_1_string for INPUT_1 if _inputFilenames[0] is empty
        if (m_scheduledEvent.m_picJob._inputFilenames[0].Length == 0 && m_scheduledEvent.m_picJob._parm_1_string.Length > 0)
        {
           ReplaceInString(ref comfyUIGraphJSon, "<AITOOLS_INPUT_1>", JSONNode.Escape(m_scheduledEvent.m_picJob._parm_1_string));
        }


        JSONNode jsonNode = null;

        try
        {
            jsonNode = JSON.Parse(comfyUIGraphJSon);
            // Your code using rootNode here
        }
        catch (Exception ex)
        {
            RTConsole.Log($"Failed to parse JSON: {ex.Message}");
            //write out .json to "json_error.json" for debugging
            File.WriteAllText("json_error.json", comfyUIGraphJSon);
            m_bIsGenerating = false;
            m_picScript.SetStatusMessage("Bad json, can't parse reply. Check json_error.json for more info.");
            CloseWebSocket();

            // Clean up state
            if (Config.Get().IsValidGPU(m_gpu))
            {
                Config.Get().SetGPUBusy(m_gpu, false);
            }
            yield break;
        }


        ExtractTotalSteps(jsonNode);
        // Modify multiple values
        ModifyJsonValue(jsonNode, "noise_seed", JSONNode.Parse(m_seed.ToString()), false); // Example seed value
        ModifyJsonValue(jsonNode, "seed", JSONNode.Parse(m_seed.ToString()), false); // Example seed value


        int frameCountOverRide = ComfyUIPanel.Get().GetFrameCount();
        if (frameCountOverRide >= 0)
        {
            ModifyJsonValue(jsonNode, "length", JSONNode.Parse(frameCountOverRide.ToString()), false); //hunyuan
            ModifyJsonValue(jsonNode, "frames_number", JSONNode.Parse(frameCountOverRide.ToString()), false); //ltx
        }

        // Convert modified JSON back to string for sending
        StringBuilder sb = new StringBuilder();
        // Force inline to false to ensure proper formatting for debug file
        if (jsonNode != null)
            jsonNode.Inline = false;
        jsonNode.WriteToStringBuilder(sb, 0, 2, JSONTextMode.Indent);
        string modifiedJsonString = sb.ToString();

        // Write debug file for troubleshooting
        File.WriteAllText("comfyui_workflow_to_send_api.json", modifiedJsonString);


        //#if !RT_RELEASE

        //#endif




        string json =
                $@"{{
                ""prompt"": {modifiedJsonString}
            }}";


        if (m_scheduledEvent != null)
        {
            RTConsole.Log("ComfyUI: Running workflow "+m_scheduledEvent.workflow+" on " + finalURL + " local GPU ID " + m_gpu);

        }

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","



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
                m_picScript.OnFinishedRenderingWorkflow(false);

            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                //Ok, we now have to dig into the response and pull out the json image

                JSONNode rootNode = null;

                try
                {
                    rootNode = JSON.Parse(postRequest.downloadHandler.text);
                   
                    // Your code using rootNode here
                }
                catch (Exception ex)
                {
                    RTConsole.Log($"Failed to parse JSON: {ex.Message}");
                    m_bIsGenerating = false;
                    m_picScript.SetStatusMessage("Bad json, can't parse reply");
                    m_picScript.OnFinishedRenderingWorkflow(false);

                    CloseWebSocket();

                    // Clean up state
                    if (Config.Get().IsValidGPU(m_gpu))
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }
                    //had an error, so let's return early
                    yield break;
                }


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
                 
                    if (m_comfyUIPromptID == null)
                    {
                        //what are we doing here?
                        Debug.Log("No prompt id, ignoring");
                        //exit
                        m_bIsGenerating = false;
                        m_picScript.SetStatusMessage("Generate error");
                        CloseWebSocket();
                        yield break;

                    }
                    JSONNode statusNode = rootNode[m_comfyUIPromptID]["status"];
                    JSONNode outputsNode = rootNode[m_comfyUIPromptID]["outputs"];

                    if (statusNode["status_str"] == "success")
                    {


                        foreach (string key in outputsNode.Keys)
                        {
                            JSONNode outputNode = outputsNode[key];
                            foreach (KeyValuePair<string, JSONNode> node in outputNode)
                            {

                                foreach (JSONNode file in node.Value)
                                {
                                    string filename = file["filename"];
                                    string subfolder = file["subfolder"];
                                    string folderType = file["type"];

                                    if (filename != null && subfolder != null && folderType != null)
                                    {
                                        string extension = System.IO.Path.GetExtension(filename).ToLower();

                                        //if it DOESN'T have "ait_ignore" somewhere in the filename, we'll continue
                                        if (!filename.Contains("ait_ignore"))
                                        {

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
                                            else
                                            {
                                                string errorMsg = $"Unknown/unsupported file type: {extension} (file: {filename})";
                                                Debug.LogWarning(errorMsg);
                                                RTConsole.Log(errorMsg);
                                                RTQuickMessageManager.Get().ShowMessage(errorMsg);
                                                CloseWebSocket();
                                                if (Config.Get().IsValidGPU(m_gpu))
                                                {
                                                    Config.Get().SetGPUBusy(m_gpu, false);
                                                }
                                                m_bIsGenerating = false;
                                                m_picScript.SetStatusMessage("Unknown file type");
                                                m_picScript.OnFinishedRenderingWorkflow(false);
                                                yield break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        //we got here?  Ok, check for text too

                        foreach (string key in outputsNode.Keys)
                        {
                            JSONNode outputNode = outputsNode[key];
                            foreach (KeyValuePair<string, JSONNode> node in outputNode)
                            {

                                //check if node is named "text"

                                if (node.Key == "text" && node.Value.Count > 0)
                                {
                                    string text = node.Value[0];
                                    if (text != null)
                                    {
                                        SetPrompt(text);
                                        m_picScript.GetCurrentStats().m_picJob._requestedPrompt = text;

                                        if (!m_picScript.StillHasJobActivityToDo())
                                        {
                                            //this is just so we can see it if we ONLY used this
                                            GameLogic.Get().SetPrompt(text);
                                        }

                                        CloseWebSocket();
                                        m_picScript.SetStatusMessage("");
                                        FinishUpEverything();
                                        yield break;
                                    }
                                }
                            }

                        }

                        // If we reach here, status is "success" but we couldn't find any usable output
                        // This can happen when:
                        // 1) All nodes were cached (outputs:{})
                        // 2) The workflow uses PreviewImage instead of SaveImage node
                        // 3) All output files were filtered (e.g., ait_ignore)
                        // 4) The workflow doesn't output an image/video/text file
                        {
                            string workflowName = m_scheduledEvent?.workflow ?? "unknown workflow";
                            string errorMsg = outputsNode.Count == 0
                                ? $"ComfyUI workflow '{workflowName}' succeeded but returned no outputs. " +
                                  "This usually means all nodes were cached (try changing an input slightly), " +
                                  "or the workflow uses PreviewImage instead of SaveImage node."
                                : $"ComfyUI workflow '{workflowName}' succeeded but no usable outputs were found. " +
                                  $"Outputs had {outputsNode.Count} node(s) but none contained usable image/video/text files. " +
                                  "Check if workflow outputs are marked with 'ait_ignore' or have unsupported formats.";
                            Debug.LogError(errorMsg);
                            RTConsole.Log(errorMsg);
                            RTQuickMessageManager.Get().ShowMessage("Workflow has no usable outputs - check console");

                            CloseWebSocket();
                            if (Config.Get().IsValidGPU(m_gpu))
                            {
                                Config.Get().SetGPUBusy(m_gpu, false);
                            }
                            m_bIsGenerating = false;
                            m_picScript.SetStatusMessage("No outputs");
                            m_picScript.OnFinishedRenderingWorkflow(false);
                            yield break;
                        }
                    }
                    else if (statusNode["status_str"] == "error")
                    {
                        string errorMsg = "ComfyUI reports a failed render (try dragging check comfyui_json_to_send.json to it to check)";
                        RTQuickMessageManager.Get().ShowMessage(errorMsg);
                        Debug.Log(errorMsg);
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
                            m_picScript.OnFinishedRenderingWorkflow(false);

                            CloseWebSocket();

                            m_picScript.ClearErrorsAndJobs();
                            m_picScript.SetStatusMessage("Comfy Error");
                            SetStatusAdditionalMessage("Comfy Error\nServer " + m_gpu);
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

                    if (m_picScript.GetNoUndo() == false)
                        m_picScript.AddImageUndo();

                    //Debug.Log("Read texture");
                    float biggestSize = Math.Max(texture.width, texture.height);

                    Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
                    renderer.sprite = newSprite;

                    m_picScript.GetMaskScript().SetMaskVisible(false);

                    //check if alpha info exists
                    if (texture.HasAlphaData())
                    {
                        //Debug.Log("Alpha exists");
                        m_picScript.FillAlphaMaskWithImageAlpha();

                    }
                    else
                    {
                       
                    }

                
                    m_picScript.OnImageReplaced();

                    //m_picScript.GetCurrentStats().m_lastPromptUsed = m_prompt;

                    m_picScript.GetCurrentStats().m_lastSeed = m_seed;

                    m_picScript.GetCurrentStats().m_bUsingControlNet = false;
                    m_picScript.GetCurrentStats().m_bUsingPix2Pix = false;
                    m_picScript.GetCurrentStats().m_lastOperation = "Generate";
                    m_picScript.GetCurrentStats().m_gpu = m_gpu;

                    m_picScript.SetNeedsToUpdateInfoPanelFlag();
                    m_picScript.AutoSaveImageIfNeeded();

                    m_picScript.SetStatusMessage("");
                }

             

                    //we're done, can delete us from ComfyUI, may fix comfyui bug with it crashing after too many generations?
                    //dostuff later...
                FinishUpEverything();
                StartCoroutine(CleanComfyUITempFiles(url));

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
        m_picScript.OnFinishedRenderingWorkflow(false);

        CleanComfyUITempFiles(url);
    }

    void FinishUpEverything(bool bSuccess = true)
    {

        if (m_bIsGenerating)
        {

            if (Config.Get().IsValidGPU(m_gpu))
            {
                if (!Config.Get().IsGPUBusy(m_gpu))
                {
                    Debug.LogError("Why is GPU not busy?! We were using it!");
                }
                else
                {
                    Config.Get().SetGPUBusy(m_gpu, false);
                }
            }

            //we're done, can delete us from ComfyUI, may fix comfyui bug with it crashing after too many generations?

            //dostuff later...
            m_bIsGenerating = false;

            m_picScript.OnFinishedRenderingWorkflow(bSuccess);

            if (bSuccess)
            {

                if (m_picScript.StillHasJobActivityToDo())
                {
                    m_picScript.UpdateJobs();
                }
                else
                {
                    if (m_picScript.m_onFinishedRenderingCallback != null)
                        m_picScript.m_onFinishedRenderingCallback.Invoke(gameObject);
                }
            }
        }
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
                StartCoroutine(CleanComfyUITempFiles(url));

                FinishUpEverything();

                yield return null; // wait a frame to lessen the jerkiness
            }
        }
    }

    private void ShowWorkflowConverterInstallDialog(string workflowFileName)
    {
        string bodyText = $"The workflow <b>{workflowFileName}</b> needs conversion, but the converter endpoint is not installed on your ComfyUI server.\n\n" +
            "<b>To install:</b>\n\n" +
            "1. Open <b>ComfyUI Manager</b> in your ComfyUI\n" +
            "2. Click <b>'Custom Nodes Manager'</b>\n" +
            "3. Search for <b>'Workflow to API Converter'</b>\n" +
            "4. Click <b>Install</b>\n" +
            "5. <b>Restart</b> your ComfyUI server";

        RTSimpleMessageDialog.ShowWithLink(
            "ComfyUI Custom Node Required",
            bodyText,
            "github.com/SethRobinson/comfyui-workflow-to-api-converter-endpoint",
            "https://github.com/SethRobinson/comfyui-workflow-to-api-converter-endpoint"
        );
    }

}
