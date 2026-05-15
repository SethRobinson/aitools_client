using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;

public class AnthropicAITextCompletionManager : MonoBehaviour
{
    private UnityWebRequest _currentRequest;
    bool m_connectionActive = false;

    void OnAnthropicCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        if (jsonNode == null)
        {
            Debug.Log("Got callback! Data: " + db.ToString());
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            return;
        }

        string reply = jsonNode["completion"];
        // NOTE: Don't show full LLM reply as quick message - it can be extremely long and crash TMPro
        // RTQuickMessageManager.Get().ShowMessage(reply);
        Debug.Log("LLM Reply: " + reply);
    }

    public bool SpawnChatCompletionRequest(string jsonRequest, Action<RTDB, JSONObject, string> myCallback, RTDB db, string anthropic_APIKey, string endpoint = "https://api.anthropic.com/v1/messages",
        Action<string> streamingUpdateChunkCallback = null, bool bStreaming = false, string sentJsonFilename = "text_completion_sent.json")
    {
        if (bStreaming)
        {
            StartCoroutine(GetRequestStreaming(jsonRequest, myCallback, db, anthropic_APIKey, endpoint, streamingUpdateChunkCallback));
        }
        else
        {
            StartCoroutine(GetRequest(jsonRequest, myCallback, db, anthropic_APIKey, endpoint, sentJsonFilename));
        }
        return true;
    }

    /// <summary>
    /// Returns true if the given Claude model rejects sampling parameters like
    /// temperature / top_p / top_k. Anthropic dropped these for the 4.7+ generation
    /// (Opus 4.7 onward), so sending them returns HTTP 400 "invalid parameter".
    /// </summary>
    public static bool ModelRejectsSamplingParams(string model)
    {
        if (string.IsNullOrEmpty(model)) return false;
        string m = model.ToLowerInvariant();
        // Currently confirmed: claude-opus-4-7 and beyond. Add new families here as they ship.
        if (m.Contains("opus-4-7")) return true;
        // Future-proofing: anything that parses as opus-4-N where N >= 7, or any 5.x line.
        if (m.Contains("opus-5") || m.Contains("sonnet-5") || m.Contains("haiku-5")) return true;
        return false;
    }

    /// <summary>
    /// Pull the concatenated text from a non-streaming Anthropic /v1/messages
    /// response. Anthropic returns content as an array of typed blocks; we
    /// concatenate the text of every block of type "text" (skipping tool_use
    /// or thinking blocks). Returns empty string on a malformed response.
    /// </summary>
    public static string ExtractTextFromResponseJSON(JSONNode root)
    {
        if (root == null) return "";
        var content = root["content"];
        if (content == null || content.IsNull) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < content.Count; i++)
        {
            var block = content[i];
            if (block == null || block.IsNull) continue;
            string blockType = block["type"];
            if (blockType == "text" && block["text"] != null && !block["text"].IsNull)
            {
                sb.Append(block["text"].Value);
            }
        }
        return sb.ToString();
    }

    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "claude-3-sonnet-20240229", bool stream = false)
    {
        var messagesSb = new StringBuilder();
        string systemPrompt = "";

        foreach (GTPChatLine obj in lines)
        {
            if (obj._role == "system")
            {
                systemPrompt += SimpleJSON.JSONNode.Escape(obj._content);
                continue;
            }

            if (messagesSb.Length == 0)
            {
                if (obj._role != "user")
                {
                    //uh oh, Anthropic requires a user message first for some stupid reason.  Also, if it's blank it gives an error 400.
                    messagesSb.Append("{\"role\": \"user\", \"content\": \"Starting\"}");
                }
            }

            if (messagesSb.Length > 0)
            {
                messagesSb.Append(",\n");
            }

            if (obj.HasImages())
            {
                // Anthropic vision content-array form:
                // content = [ {text: "[Image #N]"}, {type:"image", source:{base64...}}, ..., {text: "..."} ]
                // The "[Image #N]" labels mirror what OpenAITextCompletionManager
                // emits so the LLM can cross-reference chat_image="N" tags.
                messagesSb.Append("{\"role\": \"").Append(obj._role).Append("\", \"content\": [");
                for (int i = 0; i < obj._images.Count; i++)
                {
                    int idx = (obj._imageChatIndices != null && i < obj._imageChatIndices.Count)
                        ? obj._imageChatIndices[i]
                        : -1;
                    int labelN = idx >= 0 ? idx : (i + 1);
                    messagesSb.Append("{\"type\":\"text\",\"text\":\"[Image #")
                              .Append(labelN)
                              .Append("]\"},");
                    messagesSb.Append("{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"")
                              .Append(obj._images[i])
                              .Append("\"}},");
                }
                messagesSb.Append("{\"type\":\"text\",\"text\":\"")
                          .Append(SimpleJSON.JSONNode.Escape(obj._content ?? ""))
                          .Append("\"}]}");
            }
            else
            {
                messagesSb.Append("{\"role\": \"").Append(obj._role)
                          .Append("\", \"content\": \"")
                          .Append(SimpleJSON.JSONNode.Escape(obj._content))
                          .Append("\"}");
            }
        }

        if (messagesSb.Length == 0)
        {
            //uh oh, Anthropic requires a user message first for some stupid reason.  Also, if it's blank it gives an error 400.
            messagesSb.Append("{\"role\": \"user\", \"content\": \"(please start)\"}");
        }

        string messages = messagesSb.ToString();

        // Opus 4.7+ rejects temperature/top_p/top_k. Omit the field entirely for those models;
        // Anthropic uses a sensible default when it's missing.
        string temperatureField = ModelRejectsSamplingParams(model)
            ? ""
            : $",\n             \"temperature\": {temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        string json =
         $@"{{
             ""model"": ""{model}"",
             ""system"": ""{systemPrompt}"",
             ""messages"": [{messages}],
             ""max_tokens"": {max_tokens},
             ""stream"": {stream.ToString().ToLower()}{temperatureField}
            }}";

        return json;
    }


    IEnumerator GetRequest(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string anthropic_APIKey, string endpoint, string sentJsonFilename = "text_completion_sent.json")
    {
        m_connectionActive = true;

        // Persist the outbound body so callers can inspect what we actually sent
        // when the response is empty or malformed.
        LLMDebugLog.LogRequest(json);

        using (_currentRequest = UnityWebRequest.PostWwwForm(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            _currentRequest.SetRequestHeader("content-type", "application/json");
            _currentRequest.SetRequestHeader("x-api-key", anthropic_APIKey);
            _currentRequest.SetRequestHeader("anthropic-version", "2023-06-01");
            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                Debug.Log(msg);
                LLMDebugLog.LogError(_currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {
                LLMDebugLog.LogResponse(_currentRequest.downloadHandler.text);
                JSONNode rootNode = JSON.Parse(_currentRequest.downloadHandler.text);
                yield return null;

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
            _currentRequest = null;
            Debug.Log("Request aborted.");
        }
    }

    IEnumerator GetRequestStreaming(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string anthropic_APIKey, string endpoint,
         Action<string> updateChunkCallback)
    {

       // json = "{\r\n  \"model\": \"claude-3-5-sonnet-20240620\",\r\n  \"messages\": [{\"role\": \"user\", \"content\": \"Hello\"}],\r\n  \"max_tokens\": 256,\r\n  \"stream\": true\r\n}";


        m_connectionActive = true;

        LLMDebugLog.LogRequest(json);

        using (_currentRequest = UnityWebRequest.PostWwwForm(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            _currentRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            var downloadHandler = new AnthropicStreamingDownloadHandler(updateChunkCallback);
            _currentRequest.downloadHandler = downloadHandler;

            _currentRequest.SetRequestHeader("anthropic-version", "2023-06-01");
            _currentRequest.SetRequestHeader("content-type", "application/json");
            _currentRequest.SetRequestHeader("x-api-key", anthropic_APIKey);
            yield return _currentRequest.SendWebRequest();

            if (_currentRequest == null)
            {
                yield break;
            }

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                // The streaming handler's .text only exposes SSE-parsed content, which is
                // empty for non-SSE error responses. Pull the raw bytes instead so we can
                // see what Anthropic actually said (e.g. parameter rejection messages).
                string rawBody = downloadHandler != null ? downloadHandler.GetRawResponse() : "";
                if (string.IsNullOrEmpty(rawBody)) rawBody = _currentRequest.downloadHandler != null ? _currentRequest.downloadHandler.text : "";
                Debug.Log("Error: " + msg);
                Debug.Log("Response Code: " + _currentRequest.responseCode);
                Debug.Log("Response Headers: " + _currentRequest.GetResponseHeaders());
                Debug.Log("Response Body: " + rawBody);
                LLMDebugLog.LogError(rawBody);
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                db.Set("response_body", rawBody);
                myCallback.Invoke(db, null, "");
            }
            else
            {
                LLMDebugLog.LogResponse(_currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContent());
            }
        }
    }

}
