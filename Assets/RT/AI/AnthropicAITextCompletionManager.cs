using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
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
        RTQuickMessageManager.Get().ShowMessage(reply);
    }

    public bool SpawnChatCompletionRequest(string jsonRequest, Action<RTDB, JSONObject, string> myCallback, RTDB db, string anthropic_APIKey, string endpoint = "https://api.anthropic.com/v1/messages",
        Action<string> streamingUpdateChunkCallback = null, bool bStreaming = false)
    {
        if (bStreaming)
        {
            StartCoroutine(GetRequestStreaming(jsonRequest, myCallback, db, anthropic_APIKey, endpoint, streamingUpdateChunkCallback));
        }
        else
        {
            StartCoroutine(GetRequest(jsonRequest, myCallback, db, anthropic_APIKey, endpoint));
        }
        return true;
    }

    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "claude-3-sonnet-20240229", bool stream = false)
    {
        string messages = "";
        string systemPrompt = "";
       
        foreach (GTPChatLine obj in lines)
        {
            if (obj._role == "system")
            {
                systemPrompt += SimpleJSON.JSONNode.Escape(obj._content);
                continue;
            }

            if (messages.Length == 0)
            {
                if (obj._role != "user")
                {
                    //uh oh, Anthropic requires a user message first for some stupid reason.  Also, if it's blank it gives an error 400.
                    messages += "{\"role\": \"user\", \"content\": \"Starting\"}";
                }
            }

            if (messages.Length > 0)
                {
                    messages += ",\n";
                }

                messages += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
           
        }

        // messages = "{ \"role\": \"user\", \"content\": \"Hello\"}";
        
        if (messages.Length == 0)
        {
            //uh oh, Anthropic requires a user message first for some stupid reason.  Also, if it's blank it gives an error 400.
            messages = "{\"role\": \"user\", \"content\": \"(please start)\"}";
        }

        string json =
         $@"{{
             ""model"": ""{model}"",
             ""system"": ""{systemPrompt}"",
             ""messages"": [{messages}],
             ""max_tokens"": {max_tokens},
             ""stream"": {stream.ToString().ToLower()},
             ""temperature"": {temperature}
            }}";

        return json;
    }


    IEnumerator GetRequest(string json, Action<RTDB, JSONObject, string> myCallback, RTDB db, string anthropic_APIKey, string endpoint)
    {
        m_connectionActive = true;

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
                File.WriteAllText("last_error_returned.json", _currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {
                File.WriteAllText("claude_json_received.json", _currentRequest.downloadHandler.text);
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

//#if UNITY_STANDALONE && !RT_RELEASE
        File.WriteAllText("text_completion_sent.json", json);
//#endif

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
                Debug.Log("Error: " + msg);
                Debug.Log("Response Code: " + _currentRequest.responseCode);
                Debug.Log("Response Headers: " + _currentRequest.GetResponseHeaders());
                Debug.Log("Response Body: " + _currentRequest.downloadHandler.text);
                File.WriteAllText("last_error_returned.json", _currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {
                File.WriteAllText("claude_json_received.json", _currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContent());
            }
        }
    }

}
