using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;

public class GTPChatLine
    {
        public GTPChatLine(string role, string content, string internalTag = "")
        {
            _role = role;
            _content = content;
            _internalTag = internalTag;
            _images = new List<string>();
        }

        public GTPChatLine Clone()
        {
            var clone = new GTPChatLine(_role, _content, _internalTag);
            clone._images = new List<string>(_images);
            return clone;
        }

        /// <summary>
        /// Add a base64-encoded image to this chat line (for vision LLM support).
        /// The image should be PNG or JPEG encoded, without the data:image prefix.
        /// </summary>
        public void AddImage(string base64ImageData)
        {
            if (_images == null)
                _images = new List<string>();
            _images.Add(base64ImageData);
        }

        /// <summary>
        /// Returns true if this chat line has images attached.
        /// </summary>
        public bool HasImages()
        {
            return _images != null && _images.Count > 0;
        }

        public string _role; //must be set to user, assistant, or system
        public string _content;
        public string _internalTag;
        public List<string> _images; // Base64-encoded images for vision LLM
    }

public class OpenAITextCompletionManager : MonoBehaviour
{

    private UnityWebRequest _currentRequest;
    bool m_connectionActive = false;


    public void Start()
    {
       // ExampleOfUse();
    }

    //*  EXAMPLE START (this could be moved to your own code) */

    void ExampleOfUse()
    {
        //build a stack of GTPChatLine so we can add as many as we want

        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

        string openAI_APIKey = "put it here";

        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system", "The AI assistant will answer very rude and snarky and never helpful. Replies are short, usually under fifteen words."));
        lines.Enqueue(new GTPChatLine("user", "What is 1 + 1?"));

        string json = textCompletionScript.BuildChatCompleteJSON(lines);
        RTDB db = new RTDB();

        textCompletionScript.SpawnChatCompleteRequest(json, OnOpenAICompletedCallback, db, openAI_APIKey);
    }

    //write a public static function that will accept Queue<GTPChatLine> lines and carefully remove the tags
    // <think> and </think> and all text between it and returns the new Queue<GTPChatLine>

    public static String RemoveThinkTagsFromString(String line)
    {
            // Only remove if both tags exist.
            if (line.Contains("<think>") && line.Contains("</think>"))
            {
                // The (?s) inline option makes '.' match newlines as well.
                return Regex.Replace(line, @"(?s)<think>.*?</think>", "");
            }

        return line;
    }

    public static Queue<GTPChatLine> RemoveThinkTags(Queue<GTPChatLine> lines)
    {
        Queue<GTPChatLine> newLines = new Queue<GTPChatLine>();

        foreach (GTPChatLine obj in lines)
        {
            // Clone the original object so we don't modify it.
            GTPChatLine newObj = obj.Clone();

            // Only remove if both tags exist.
            if (newObj._content.Contains("<think>") && newObj._content.Contains("</think>"))
            {
                // The (?s) inline option makes '.' match newlines as well.
                newObj._content = Regex.Replace(newObj._content, @"(?s)<think>.*?</think>", "");
            }
            // If only one tag is present, we leave the content as is.
            newLines.Enqueue(newObj);
        }

        return newLines;
    }

    // Remove TextMeshPro markup tags that we may have injected for display (e.g., <b>...</b>, <color=#FFD700>...</color>)
    public static string RemoveTMPTagsFromString(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        
        try
        {
            // Strip color tags with any format: <color=#RRGGBB>, <color=name>, </color>
            line = Regex.Replace(line, @"<color=[^>]*>", "", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, @"</color>", "", RegexOptions.IgnoreCase);
            
            // Strip bold tags: <b>, </b>
            line = Regex.Replace(line, @"</?b>", "", RegexOptions.IgnoreCase);
            
            // Strip italic tags: <i>, </i>
            line = Regex.Replace(line, @"</?i>", "", RegexOptions.IgnoreCase);
        }
        catch
        {
            // If regex fails, return original line unchanged
        }
        
        return line;
    }

    /// <summary>
    /// Normalizes chat messages for OpenAI-compatible servers that require strict role alternation.
    /// - Consolidates all system messages into a single user message at the start
    /// - Merges consecutive same-role messages
    /// - Ensures conversation starts with "user" role
    /// This is needed for models like Mistral that require strict user/assistant/user/assistant alternation.
    /// </summary>
    public static Queue<GTPChatLine> NormalizeForStrictAlternation(Queue<GTPChatLine> lines)
    {
        if (lines == null || lines.Count == 0)
            return lines;

        List<GTPChatLine> result = new List<GTPChatLine>();
        StringBuilder systemContent = new StringBuilder();
        
        // First pass: collect all system messages and separate user/assistant messages
        List<GTPChatLine> nonSystemMessages = new List<GTPChatLine>();
        foreach (var line in lines)
        {
            if (line._role == "system")
            {
                if (systemContent.Length > 0)
                    systemContent.Append("\n\n");
                systemContent.Append(line._content);
            }
            else
            {
                nonSystemMessages.Add(line);
            }
        }
        
        // If we have system content, prepend it as a user message
        if (systemContent.Length > 0)
        {
            result.Add(new GTPChatLine("user", systemContent.ToString()));
        }
        
        // Second pass: merge consecutive same-role messages
        foreach (var line in nonSystemMessages)
        {
            if (result.Count > 0 && result[result.Count - 1]._role == line._role)
            {
                // Merge with previous message of same role
                result[result.Count - 1]._content += "\n\n" + line._content;
            }
            else
            {
                result.Add(new GTPChatLine(line._role, line._content));
            }
        }
        
        // Ensure we have at least one user message
        if (result.Count == 0)
        {
            result.Add(new GTPChatLine("user", "Please proceed."));
        }
        
        // If first message isn't user, we need to add a placeholder or shift
        // (This shouldn't happen normally, but just in case)
        if (result[0]._role != "user")
        {
            result.Insert(0, new GTPChatLine("user", "Please respond to the following:"));
        }
        
        return new Queue<GTPChatLine>(result);
    }

    public static Queue<GTPChatLine> RemoveTMPTags(Queue<GTPChatLine> lines)
    {
        Queue<GTPChatLine> newLines = new Queue<GTPChatLine>();
        foreach (GTPChatLine obj in lines)
        {
            GTPChatLine newObj = obj.Clone();
            newObj._content = RemoveTMPTagsFromString(newObj._content);
            newLines.Enqueue(newObj);
        }
        return newLines;
    }



    void OnOpenAICompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
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
        // NOTE: Don't show full LLM reply as quick message - it can be extremely long and crash TMPro
        // RTQuickMessageManager.Get().ShowMessage(reply);
        Debug.Log("LLM Reply: " + reply);

    }

    //*  EXAMPLE END */
    public bool SpawnChatCompleteRequest(string jsonRequest, Action<RTDB, JSONObject, string> myCallback, RTDB db, string openAI_APIKey, string endpoint = "https://api.openai.com/v1/chat/completions",
        Action<string> streamingUpdateChunkCallback = null, bool bStreaming = false)
    {
        if (bStreaming)
        {
            StartCoroutine(GetRequestStreaming(jsonRequest, myCallback, db, openAI_APIKey, endpoint, streamingUpdateChunkCallback));
        } else
        {
            StartCoroutine(GetRequest(jsonRequest, myCallback, db, openAI_APIKey, endpoint));

        }
        return true;
    }

    //Build OpenAI.com API request json
    // useResponsesAPI: set to true for OpenAI Responses API (/v1/responses), false for Chat Completions API (/v1/chat/completions)
    // isReasoningModel: include reasoning block for reasoning models (gpt-5.2/gpt-5.2-pro)
    // includeTemperature: some models (gpt-5-mini/nano) do not support temperature at all
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "gpt-3.5-turbo", bool stream = false, bool useResponsesAPI = false, bool isReasoningModel = false, bool includeTemperature = true, string reasoningEffort = null)
    {
        string bStreamText = stream ? "true" : "false";

        if (useResponsesAPI)
        {
            // Responses API format: uses "input" instead of "messages", and "instructions" for system messages
            string instructions = "";
            string inputMessages = "";

            foreach (GTPChatLine obj in lines)
            {
                if (obj._role == "system")
                {
                    // System messages go into the instructions parameter
                    if (instructions.Length > 0)
                    {
                        instructions += "\n\n";
                    }
                    instructions += obj._content;
                }
                else
                {
                    // User and assistant messages go into the input array
                    if (inputMessages.Length > 0)
                    {
                        inputMessages += ",\n";
                    }
                    inputMessages += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
                }
            }

            // Build JSON with proper comma handling for optional parameters
            string json;
            string reasoningPart = "";
            string temperaturePart = "";
            
            if (!string.IsNullOrEmpty(reasoningEffort))
            {
                reasoningPart = $@"""reasoning"": {{""effort"": ""{reasoningEffort}""}},";
            }
            
            if (includeTemperature)
            {
                temperaturePart = $@"""temperature"": {temperature},";
            }
            
            if (instructions.Length > 0)
            {
                json = $@"{{
             ""model"": ""{model}"",
             ""instructions"": ""{SimpleJSON.JSONNode.Escape(instructions)}"",
             ""input"": [{inputMessages}],
             {reasoningPart}
             {temperaturePart}
            ""stream"": {bStreamText}
            }}";
            }
            else
            {
                json = $@"{{
             ""model"": ""{model}"",
             ""input"": [{inputMessages}],
             {reasoningPart}
             {temperaturePart}
            ""stream"": {bStreamText}
            }}";
            }
            
            // Clean up any trailing commas before closing braces
            json = json.Replace(",\n            \"stream\"", "\n            \"stream\"");
            json = json.Replace(",\n            }", "\n            }");

            return json;
        }
        else
        {
            // Chat Completions API format: uses "messages"
            string msg = "";

            foreach (GTPChatLine obj in lines)
            {
                if (msg.Length > 0)
                {
                    msg += ",\n";
                }
                msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
            }

            string temperaturePart = includeTemperature ? $@"""temperature"": {temperature}," : "";
            
            string json =
             $@"{{
             ""model"": ""{model}"",
             ""messages"":[{msg}],
             {temperaturePart}
            ""stream"": {bStreamText}
            }}";
            
            // Clean up any trailing commas
            json = json.Replace(",\n            \"stream\"", "\n            \"stream\"");

            return json;
        }
    }
    //     ""reasoning_effort"":  ""medium"",
         
//                 ""max_tokens"": {max_tokens,

    IEnumerator GetRequest(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string openAI_APIKey, string endpoint)
    {

#if UNITY_STANDALONE && !RT_RELEASE 
               File.WriteAllText("text_completion_sent.json", json);
#endif
        string url;
        url = endpoint;
        m_connectionActive = true;
        //Debug.Log("Sending request " + url );

        using (_currentRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            _currentRequest.SetRequestHeader("Content-Type", "application/json");
            _currentRequest.SetRequestHeader("Authorization", "Bearer "+openAI_APIKey);
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
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
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
                m_connectionActive = false;

                db.Set("status", "success");
                myCallback.Invoke(db, (JSONObject)rootNode, "");
               
            }
        }
    }

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
                _currentRequest.Abort();
            _currentRequest = null; // Ensure to nullify the reference
            Debug.Log("Request aborted.");
        }
    }


    IEnumerator GetRequestStreaming(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string openAI_APIKey, string endpoint,
         Action<string> updateChunkCallback)
    {

#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("text_completion_sent.json", json);
#endif
        string url;
        url = endpoint;
        //Debug.Log("Sending request " + url );
        m_connectionActive = true;

        using (_currentRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            var downloadHandler = new StreamingDownloadHandler(updateChunkCallback);
            _currentRequest.downloadHandler = downloadHandler;

            _currentRequest.SetRequestHeader("Content-Type", "application/json");
            _currentRequest.SetRequestHeader("Authorization", "Bearer " + openAI_APIKey);
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
                
                // Try to get error response body from streaming handler first
                string errorBody = downloadHandler.GetContentAsString();
                
                // If streaming handler doesn't have it, try accessing the response directly
                if (string.IsNullOrEmpty(errorBody) && _currentRequest.downloadHandler != null)
                {
                    // For non-streaming errors, the downloadHandler might have the text
                    try
                    {
                        errorBody = _currentRequest.downloadHandler.text;
                    }
                    catch { }
                }
                
                if (string.IsNullOrEmpty(errorBody))
                {
                    errorBody = "(No response body)";
                }
                Debug.Log("Error response body: " + errorBody);
                
                //#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", errorBody);
                //#endif
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE
                //                Debug.Log("Form upload complete! Downloaded " + _currentRequest.downloadedBytes);

                File.WriteAllText("textgen_json_received.json", _currentRequest.downloadHandler.text);
#endif
                m_connectionActive = false;
            
                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContentAsString());

            }
        }
    }
}
