using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;


public class LLMParm
{
    public string _key;
    public string _value;
}


public class TexGenWebUITextCompletionManager : MonoBehaviour
{
    private UnityWebRequest _currentRequest;
    bool m_connectionActive = false;

    public void Start()
    {
        // ExampleOfUse();
    }

    //*  EXAMPLE START (this could be moved to your own code) */

   
    /*
    void ExampleOfUse()
    {
        //build a stack of GTPChatLine so we can add as many as we want

        TexGenWebUITextCompletionManager textCompletionScript = gameObject.GetComponent<TexGenWebUITextCompletionManager>();

        string serverAddress = "put it here";

        string prompt = "crap";

        string suggestedEndpoint;
        string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, out suggestedEndpoint, m_max_tokens, m_extractor.Temperature, Config.Get().GetGenericLLMMode(), true, Config.Get().GetLLMParms(), Config.Get().GetGenericLLMIsOllama(), Config.Get().GetGenericLLMIsLlamaCpp());
        RTDB db = new RTDB();

        textCompletionScript.SpawnChatCompleteRequest(json, OnCompletedCallback, db, serverAddress, suggestedEndpoint);
    }

    */
    void OnCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {

        if (jsonNode == null)
        {
            //must have been an error
            Debug.Log("Got callback! Data: " + db.ToString());
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            return;
        }

        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */

        string reply = jsonNode["choices"][0]["message"]["content"];
        RTQuickMessageManager.Get().ShowMessage(reply);

    }

    //*  EXAMPLE END */
    public bool SpawnChatCompleteRequest(string jsonRequest, Action<RTDB, JSONObject, string> myCallback, RTDB db, string serverAddress,
        string apiCommandURL, Action<string> streamingUpdateChunkCallback = null, bool bStreaming = false, string apiKey = "none")
    {
        if (bStreaming)
        {
            StartCoroutine(GetRequestStreaming(jsonRequest, myCallback, db, serverAddress, apiCommandURL, streamingUpdateChunkCallback, apiKey));

        }
        else
        {
            StartCoroutine(GetRequest(jsonRequest, myCallback, db, serverAddress, apiCommandURL));
        }
        return true;
    }



  
    // ""name1"": ""Jeff"",
    public string BuildForInstructJSON(Queue<GTPChatLine> lines, out string suggestedEndpoint, int max_new_tokens = 100, float temperature = 1.3f, string mode = "instruct", bool stream = false, List<LLMParm> parms = null, bool bIsOllama = false, bool bIsLlamaCpp = false)
    {
        // Initialize the suggested endpoint to default (chat completions)
        suggestedEndpoint = "/v1/chat/completions";
        
        string msg = "";
        string modelName = "";
        
        // Get model name from parms for template detection
        if (parms != null)
        {
            foreach (LLMParm parm in parms)
            {
                if (parm._key == "model")
                {
                    modelName = parm._value.Replace("\"", "").ToLower();
                    break;
                }
            }
        }
        
        // Detect template type for llama.cpp servers
        bool useGLMTemplate = false;
        bool useChatMLTemplate = false;
        bool useLlama2Template = false;
        bool useLlama3Template = false;
        
        if (bIsLlamaCpp && !string.IsNullOrEmpty(modelName))
        {
            // Check for GLM models
            if (modelName.Contains("glm"))
            {
                useGLMTemplate = true;
                RTConsole.Log("Detected GLM model, using GLM chat template");
            }
            // Check for Mistral/Mixtral models (use ChatML)
            else if (modelName.Contains("mistral") || modelName.Contains("mixtral"))
            {
                useChatMLTemplate = true;
                RTConsole.Log("Detected Mistral/Mixtral model, using ChatML template");
            }
            // Check for Llama-2 models
            else if (modelName.Contains("llama-2") || modelName.Contains("llama2"))
            {
                useLlama2Template = true;
                RTConsole.Log("Detected Llama-2 model, using Llama-2 template");
            }
            // Check for Llama-3 models
            else if (modelName.Contains("llama-3") || modelName.Contains("llama3"))
            {
                useLlama3Template = true;
                RTConsole.Log("Detected Llama-3 model, using Llama-3 template");
            }
        }
        
        // Build prompt based on detected template
        if (useGLMTemplate)
        {
            // Build GLM-style prompt for completions endpoint
            string glmPrompt = "[gMASK]<sop>";
            
            foreach (GTPChatLine obj in lines)
            {
                if (obj._role == "system" || obj._role == Config.Get().GetAISystemWord())
                {
                    glmPrompt += "<|system|>\n" + obj._content + "\n";
                }
                else if (obj._role == "user" || obj._role == Config.Get().GetAIUserWord())
                {
                    glmPrompt += "<|user|>\n" + obj._content + "\n";
                }
                else if (obj._role == "assistant" || obj._role == Config.Get().GetAIAssistantWord())
                {
                    glmPrompt += "<|assistant|>\n" + obj._content + "\n";
                }
            }
            
            // End with assistant tag for completion
            glmPrompt += "<|assistant|>\n";
            
            // Return as completions-style JSON (will be handled specially)
            msg = glmPrompt;
        }
        else if (useChatMLTemplate)
        {
            // Build ChatML-style prompt for completions endpoint
            string chatmlPrompt = "";
            
            foreach (GTPChatLine obj in lines)
            {
                if (obj._role == "system" || obj._role == Config.Get().GetAISystemWord())
                {
                    chatmlPrompt += "<|im_start|>system\n" + obj._content + "<|im_end|>\n";
                }
                else if (obj._role == "user" || obj._role == Config.Get().GetAIUserWord())
                {
                    chatmlPrompt += "<|im_start|>user\n" + obj._content + "<|im_end|>\n";
                }
                else if (obj._role == "assistant" || obj._role == Config.Get().GetAIAssistantWord())
                {
                    chatmlPrompt += "<|im_start|>assistant\n" + obj._content + "<|im_end|>\n";
                }
            }
            
            // End with assistant tag for completion
            chatmlPrompt += "<|im_start|>assistant\n";
            
            msg = chatmlPrompt;
        }
        else if (useLlama2Template)
        {
            // Build Llama-2 style prompt
            string llama2Prompt = "";
            
            foreach (GTPChatLine obj in lines)
            {
                if (obj._role == "system" || obj._role == Config.Get().GetAISystemWord())
                {
                    llama2Prompt += "<<SYS>>\n" + obj._content + "\n<</SYS>>\n\n";
                }
                else if (obj._role == "user" || obj._role == Config.Get().GetAIUserWord())
                {
                    llama2Prompt += "[INST] " + obj._content + " [/INST]\n";
                }
                else if (obj._role == "assistant" || obj._role == Config.Get().GetAIAssistantWord())
                {
                    llama2Prompt += obj._content + "\n";
                }
            }
            
            msg = llama2Prompt;
        }
        else if (useLlama3Template)
        {
            // Build Llama-3 style prompt
            string llama3Prompt = "";
            
            foreach (GTPChatLine obj in lines)
            {
                if (obj._role == "system" || obj._role == Config.Get().GetAISystemWord())
                {
                    llama3Prompt += "<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n" + obj._content + "<|eot_id|>";
                }
                else if (obj._role == "user" || obj._role == Config.Get().GetAIUserWord())
                {
                    llama3Prompt += "<|start_header_id|>user<|end_header_id|>\n\n" + obj._content + "<|eot_id|>";
                }
                else if (obj._role == "assistant" || obj._role == Config.Get().GetAIAssistantWord())
                {
                    llama3Prompt += "<|start_header_id|>assistant<|end_header_id|>\n\n" + obj._content + "<|eot_id|>";
                }
            }
            
            // End with assistant header for completion
            llama3Prompt += "<|start_header_id|>assistant<|end_header_id|>\n\n";
            
            msg = llama3Prompt;
        }
        else
        {
            // Default chat completions format
            foreach (GTPChatLine obj in lines)
            {
                if (msg.Length > 0)
                {
                    msg += ",\n";
                }
                msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
            }
        }

        string bStreamText = "false";
        if (stream)
        {
            bStreamText = "true";
        }

     
        string extra = "";
        bool useOllamaDefaults = false;
        bool skipAitSuffix = false;
        bool hasNumCtx = false;
        
        // Check if num_ctx is set in parms
        if (parms != null)
        {
            foreach (LLMParm parm in parms)
            {
                if (parm._key == "num_ctx" && parm._value.Length > 0)
                {
                    hasNumCtx = true;
                    break;
                }
            }
        }
        
        //for each parms, add it to extra with a comma in front
        if (parms != null)
        {
            foreach (LLMParm parm in parms)
            {
                if (parm._key == "use_ollama_defaults" && parm._value == "true")
                {
                    useOllamaDefaults = true;
                    continue;
                }
                
                if (parm._key == "skip_ait_suffix" && parm._value == "true")
                {
                    skipAitSuffix = true;
                    continue;
                }

                if (parm._key == "model")
                {
                    //special handling, remove the quotes
                    string valueTemp = parm._value;
                    valueTemp = valueTemp.Replace("\"", "");
                    //if ollama, append _ait to the model only when num_ctx is set
                    if (bIsOllama && !useOllamaDefaults && !skipAitSuffix && hasNumCtx)
                    {
                        valueTemp += "_ait";
                    }
                    extra += $",\"{parm._key}\": \"{valueTemp}\"\r\n";
                    continue;
                }
                
                // Skip temperature if using Ollama defaults
                if (parm._key == "temperature" && useOllamaDefaults && bIsOllama)
                {
                    continue;
                }
                
                // Skip certain auto-detected parameters that shouldn't be sent in requests
                if (parm._key == "model_id" || parm._key == "n_vocab" || parm._key == "n_embd" || 
                    parm._key == "n_params" || parm._key == "n_ctx_train")
                {
                    // These are informational parameters from llama.cpp detection, not for sending
                    continue;
                }
                
                // Check if this parameter value looks like a string that needs quoting
                // (contains forward slashes, backslashes, or starts with a letter/slash)
                if (parm._value.Contains("/") || parm._value.Contains("\\") || 
                    (parm._value.Length > 0 && (char.IsLetter(parm._value[0]) || parm._value[0] == '/')))
                {
                    // This is likely a string value that needs proper JSON escaping
                    string escapedValue = SimpleJSON.JSONNode.Escape(parm._value);
                    extra += $",\"{parm._key}\": \"{escapedValue}\"\r\n";
                }
                else
                {
                    // Numeric or already properly formatted JSON value
                    extra += $",\"{parm._key}\": {parm._value}\r\n";
                }
            }
        }

        //replace all ` in extra to |
        extra = extra.Replace("`", "|");

        // Check if we're using a special template that requires completions endpoint
        bool useCompletionsEndpoint = useGLMTemplate || useChatMLTemplate || useLlama2Template || useLlama3Template;
        
        if (useCompletionsEndpoint)
        {
            // Set the suggested endpoint to completions
            suggestedEndpoint = "/v1/completions";
            
            // Build completions-style JSON for llama.cpp with special templates
            // Escape the prompt properly for JSON
            string escapedPrompt = SimpleJSON.JSONNode.Escape(msg);
          //  ""temperature"": { temperature},
       
            string json =
             $@"{{
                 ""prompt"": ""{escapedPrompt}"",
                 ""stream"": {bStreamText}
                 {extra}
             }}";
            return json;
        }
        else if (bIsOllama && useOllamaDefaults)
        {
            // Build JSON with minimal parameters for Ollama when using defaults
            string json =
             $@"{{
                 ""messages"":[{msg}],
                 ""stream"": {bStreamText}
                 {extra}
             }}";
            return json;
        }
        else
        {
            // Original behavior for non-Ollama or when not using defaults
       //     ""temperature"": { temperature},
       
            string json =
             $@"{{
                 ""messages"":[{msg}],
                 ""mode"": ""{mode}"",
                 ""stream"": {bStreamText}
                 {extra}
              
             }}";
            return json;
        }
    }

    //  ""instructi
    //  on_template"": ""Alpaca""

    public bool IsRequestActive()
    {
        return m_connectionActive;
    }
    public void CancelCurrentRequest()
    {
        if (m_connectionActive)
        {
            m_connectionActive = false;
            if (_currentRequest != null)
            {
                _currentRequest.Abort();
            } else
            {
                RTConsole.Log("Unable to cancel request, no current request object");
            }
            _currentRequest = null; // Ensure to nullify the reference
            Debug.Log("Request aborted.");
        }
    }
    IEnumerator GetRequest(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string serverAddress, string apiCommandURL)
    {

//#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("text_completion_sent.json", json);
//#endif
        string url;
        //        url = serverAddress + "/v1/chat/completions";
        url = serverAddress + apiCommandURL;
        m_connectionActive = true;
        using (_currentRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            _currentRequest.SetRequestHeader("Content-Type", "application/json");

         
            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                //uh oh, we must have aborted things, quit out
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                Debug.Log(msg);
                //Debug.Log(_currentRequest.downloadHandler.text);
                //#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", _currentRequest.downloadHandler.text);
                //#endif
                db.Set("status", "failed");
                db.Set("msg", msg);
                m_connectionActive = false;
                myCallback.Invoke(db, null, "");
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE
                //                Debug.Log("Form upload complete! Downloaded " + _currentRequest.downloadedBytes);

                File.WriteAllText("textgen_json_received.json", _currentRequest.downloadHandler.text);
#endif

                JSONNode rootNode = JSON.Parse(_currentRequest.downloadHandler.text);
                yield return null; //wait a frame to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);
                db.Set("status", "success");
                m_connectionActive = false;
                myCallback.Invoke(db, (JSONObject)rootNode, "");
            }
        }
    }

    IEnumerator GetRequestStreaming(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string serverAddress, string apiCommandURL,
     Action<string> updateChunkCallback, string APIkey = "none")
    {
//#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("text_completion_sent.json", json);
//#endif

        string url = serverAddress + apiCommandURL;
        m_connectionActive = true;

        using (_currentRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            var downloadHandler = new StreamingDownloadHandler(updateChunkCallback);
            _currentRequest.downloadHandler = downloadHandler;

            _currentRequest.SetRequestHeader("Content-Type", "application/json");
            if (APIkey != "" && APIkey != "none")
            {
                _currentRequest.SetRequestHeader("Authorization", "Bearer " + APIkey);
            }

            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                //uh oh, we must have aborted things, quit out
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success || downloadHandler.IsError())
            {
                string msg = _currentRequest.error;
                string errorResponse = downloadHandler.GetContentAsString();
                Debug.Log($"Error: {msg}");
                Debug.Log($"Response Code: {_currentRequest.responseCode}");
                Debug.Log($"Response Body: {errorResponse}");

//#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", errorResponse);
//#endif

                m_connectionActive = false;
                db.Set("status", "failed");
                db.Set("msg", $"{msg}\nResponse: {errorResponse}");
                myCallback.Invoke(db, null, "");
            }
            else
            {
//#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("textgen_json_received.json", downloadHandler.GetContentAsString());
//#endif

                m_connectionActive = false;
                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContentAsString());
            }
        }
    }

}
