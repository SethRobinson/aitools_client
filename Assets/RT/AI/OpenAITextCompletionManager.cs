using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System.Runtime.InteropServices.ComTypes;


    public class GTPChatLine
    {
        public GTPChatLine(string role, string content, string internalTag = "")
        {
            _role = role;
            _content = content;
            _internalTag = internalTag;
           
        }

        public GTPChatLine Clone()
        {
            return new GTPChatLine(_role, _content, _internalTag);
        }


        public string _role; //must be set to user, assistant, or system
        public string _content;
        public string _internalTag;
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
        RTQuickMessageManager.Get().ShowMessage(reply);

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
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "gpt-3.5-turbo", bool stream = false)
    {

        string msg = "";

        string bStreamText = "false";
        if (stream)
        {
            bStreamText = "true";
        }

        //go through each object in lines
        foreach (GTPChatLine obj in lines)
        {
            if (msg.Length > 0)
            {
                msg += ",\n";
            }
            msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
        }

        string json =
         $@"{{
             ""model"": ""{model}"",
             ""messages"":[{msg}],
             ""temperature"": {temperature},
            ""max_tokens"": {max_tokens},
             ""stream"": {bStreamText}
            }}";

        return json;
    }

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
                m_connectionActive = false;
            
                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContentAsString());

            }
        }
    }
}
