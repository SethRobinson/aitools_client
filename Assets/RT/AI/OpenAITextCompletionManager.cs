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
        if (string.IsNullOrEmpty(line)) return line;

        // Remove standard <think>...</think> blocks.
        if (line.Contains("<think>") && line.Contains("</think>"))
        {
            // The (?s) inline option makes '.' match newlines as well.
            return Regex.Replace(line, @"(?s)<think>.*?</think>", "");
        }

        // Some llama.cpp templates (DeepSeek-V4-Flash) stream/return:
        //   reasoning text </think> final answer
        // with no opening <think>. In that format, everything before </think> is reasoning.
        int closeOnly = line.IndexOf("</think>", StringComparison.Ordinal);
        if (closeOnly >= 0)
        {
            return line.Substring(closeOnly + "</think>".Length);
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

            newObj._content = RemoveThinkTagsFromString(newObj._content);
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
        
        // Second pass: merge consecutive same-role messages (preserve images)
        foreach (var line in nonSystemMessages)
        {
            if (result.Count > 0 && result[result.Count - 1]._role == line._role)
            {
                // Merge with previous message of same role
                result[result.Count - 1]._content += "\n\n" + line._content;
                if (line._images != null && line._images.Count > 0)
                {
                    if (result[result.Count - 1]._images == null)
                        result[result.Count - 1]._images = new List<string>();
                    result[result.Count - 1]._images.AddRange(line._images);
                }
            }
            else
            {
                var merged = new GTPChatLine(line._role, line._content);
                if (line._images != null && line._images.Count > 0)
                    merged._images = new List<string>(line._images);
                result.Add(merged);
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
    // topP/topK/minP/repetitionPenalty/frequencyPenalty/presencePenalty: optional sampling overrides (vLLM/sglang/LMStudio extras).
    //   When non-null they are emitted in the request body. Only included by the Chat Completions branch
    //   (OpenAI Responses API does not accept these extras and would reject the request).
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "gpt-3.5-turbo", bool stream = false, bool useResponsesAPI = false, bool isReasoningModel = false, bool includeTemperature = true, string reasoningEffort = null, bool? enableThinking = null,
        float? topP = null, int? topK = null, float? minP = null, float? repetitionPenalty = null, float? frequencyPenalty = null, float? presencePenalty = null, string customReasoningEffort = null)
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
            
            return json;
        }
        else
        {
            // Chat Completions API format: uses "messages"
            string msg = "";
            bool isDeepSeekModel = LLMRequestProfile.IsDeepSeekModel(model);
            var customEffortFallback = enableThinking.HasValue && enableThinking.Value
                ? LLMReasoningEffort.High
                : LLMReasoningEffort.Off;
            LLMReasoningEffort effectiveCustomEffort = LLMReasoningEffortUtil.Parse(customReasoningEffort, customEffortFallback);

            if (isDeepSeekModel && effectiveCustomEffort == LLMReasoningEffort.Max)
            {
                lines = PrependSystemMessage(lines, LLMReasoningPrompts.DeepSeekMaxReasoningSystemPrompt);
            }

            foreach (GTPChatLine obj in lines)
            {
                if (msg.Length > 0)
                {
                    msg += ",\n";
                }

                if (obj.HasImages())
                {
                    // Multimodal content array (vLLM/Qwen-VL/GPT-4o style):
                    // content = [ { image_url... }, ..., { text: "..." } ]
                    var contentSb = new StringBuilder();
                    contentSb.Append("[");
                    foreach (var b64 in obj._images)
                    {
                        contentSb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,")
                                 .Append(b64)
                                 .Append("\"}},");
                    }
                    contentSb.Append("{\"type\":\"text\",\"text\":\"")
                             .Append(SimpleJSON.JSONNode.Escape(obj._content))
                             .Append("\"}]");
                    msg += "{\"role\": \"" + obj._role + "\", \"content\": " + contentSb + "}";
                }
                else
                {
                    msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
                }
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string temperaturePart = includeTemperature ? $@"""temperature"": {temperature.ToString(inv)}," : "";
            string maxTokensPart = (max_tokens > 0 && (!string.IsNullOrEmpty(customReasoningEffort) || isDeepSeekModel))
                ? $@"""max_tokens"": {max_tokens},"
                : "";

            // For custom OpenAI-compatible reasoning models, control thinking via
            // chat_template_kwargs. DeepSeek-V4-Flash expects "thinking"; Qwen/GLM
            // servers generally expect "enable_thinking".
            string thinkingPart = "";
            if (isDeepSeekModel)
            {
                if (effectiveCustomEffort != LLMReasoningEffort.Off)
                    thinkingPart = @"""chat_template_kwargs"": {""thinking"": true},";
            }
            else if (enableThinking.HasValue)
            {
                string thinkVal = enableThinking.Value ? "true" : "false";
                thinkingPart = $@"""chat_template_kwargs"": {{""enable_thinking"": {thinkVal}}},";
            }

            // Optional sampling overrides for vLLM/sglang/LMStudio. Each line is empty when not overridden,
            // so the cleanup pass below removes any stray trailing commas before "stream".
            var samplingSb = new StringBuilder();
            if (topP.HasValue)
                samplingSb.Append("\"top_p\": ").Append(topP.Value.ToString(inv)).Append(",\n             ");
            if (topK.HasValue)
                samplingSb.Append("\"top_k\": ").Append(topK.Value).Append(",\n             ");
            if (minP.HasValue)
                samplingSb.Append("\"min_p\": ").Append(minP.Value.ToString(inv)).Append(",\n             ");
            if (repetitionPenalty.HasValue)
                samplingSb.Append("\"repetition_penalty\": ").Append(repetitionPenalty.Value.ToString(inv)).Append(",\n             ");
            if (frequencyPenalty.HasValue)
                samplingSb.Append("\"frequency_penalty\": ").Append(frequencyPenalty.Value.ToString(inv)).Append(",\n             ");
            if (presencePenalty.HasValue)
                samplingSb.Append("\"presence_penalty\": ").Append(presencePenalty.Value.ToString(inv)).Append(",\n             ");
            string samplingPart = samplingSb.ToString();

            string json =
             $@"{{
             ""model"": ""{model}"",
             ""messages"":[{msg}],
             {temperaturePart}
             {maxTokensPart}
             {samplingPart}{thinkingPart}
            ""stream"": {bStreamText}
            }}";

            return json;
        }
    }

    private static Queue<GTPChatLine> PrependSystemMessage(Queue<GTPChatLine> lines, string systemPrompt)
    {
        var result = new Queue<GTPChatLine>();
        result.Enqueue(new GTPChatLine("system", systemPrompt));
        if (lines != null)
        {
            foreach (var line in lines)
            {
                result.Enqueue(line);
            }
        }
        return result;
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

            bool injectThinkTags = json.IndexOf("\"enable_thinking\": true", StringComparison.Ordinal) >= 0
                || json.IndexOf("\"enable_thinking\":true", StringComparison.Ordinal) >= 0
                || (json.IndexOf("\"thinking\"", StringComparison.Ordinal) >= 0
                    && json.IndexOf("\"thinking\": true", StringComparison.Ordinal) >= 0)
                || (json.IndexOf("\"thinking\"", StringComparison.Ordinal) >= 0
                    && json.IndexOf("\"thinking\":true", StringComparison.Ordinal) >= 0)
                || json.IndexOf("\"reasoning\"", StringComparison.Ordinal) >= 0;
            bool wrapContentUntilThinkClose = (json.IndexOf("\"chat_template_kwargs\"", StringComparison.Ordinal) >= 0
                && (json.IndexOf("\"thinking\": true", StringComparison.Ordinal) >= 0
                    || json.IndexOf("\"thinking\":true", StringComparison.Ordinal) >= 0));
            var downloadHandler = new StreamingDownloadHandler(updateChunkCallback, injectReasoningThinkTags: injectThinkTags, wrapContentUntilThinkClose: wrapContentUntilThinkClose);
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
