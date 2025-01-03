using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;


public class TexGenWebUITextCompletionManager : MonoBehaviour
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

        TexGenWebUITextCompletionManager textCompletionScript = gameObject.GetComponent<TexGenWebUITextCompletionManager>();

        string serverAddress = "put it here";

        string prompt = "crap";

        string json = textCompletionScript.BuildChatCompleteJSON(prompt);
        RTDB db = new RTDB();

        textCompletionScript.SpawnChatCompleteRequest(json, OnCompletedCallback, db, serverAddress, "/v1/completions");
    }

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



    public string BuildChatCompleteJSON(string msg, int max_tokens = 100, float temperature = 1.0f, bool stream = false)
    {
        string bStreamText = "false";
        if (stream)
        {
            bStreamText = "true";
        }

        int max_new_tokens = 99999;
        string json =
         $@"{{

""prompt"": ""{SimpleJSON.JSONNode.Escape(msg)}"",
""max_tokens"": {max_tokens},
""max_new_tokens"": {max_new_tokens},
""temperature"": {temperature},
 ""stream"": {bStreamText},
""seed"": -1
   }}";

        return json;
    }

    // ""name1"": ""Jeff"",
    public string BuildForInstructJSON(Queue<GTPChatLine> lines, int max_new_tokens = 100, float temperature = 1.3f, string mode = "instruct", bool stream = false, string texGenWebUICharacter = "")
    {
        string msg = "";

        //go through each object in lines
        foreach (GTPChatLine obj in lines)
        {
            if (msg.Length > 0)
            {
                msg += ",\n";
            }
            msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
        }

        string bStreamText = "false";
        if (stream)
        {
            bStreamText = "true";
        }

        string characterPart = "";
        if (!string.IsNullOrEmpty(texGenWebUICharacter))
        {
            characterPart = $@",""character"": ""{SimpleJSON.JSONNode.Escape(texGenWebUICharacter)}""";
        }

        string json =
         $@"{{
             ""messages"":[{msg}],
             ""mode"": ""{mode}"",
             ""temperature"": {temperature},
             ""stream"": {bStreamText},
             ""max_new_tokens"": {max_new_tokens},
             ""mode"": ""instruct"",
             ""instruction_template"": ""Alpaca""             
             {characterPart}
         }}";

        return json;
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
        string url;
        url = serverAddress + apiCommandURL;
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

            if (_currentRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = _currentRequest.error;
                Debug.Log(msg);
                //Debug.Log(_currentRequest.downloadHandler.text);
                File.WriteAllText("last_error_returned.json", _currentRequest.downloadHandler.text);
                m_connectionActive = false;

                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null, "");
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("textgen_json_received.json", _currentRequest.downloadHandler.text);
#endif
                m_connectionActive = false;

                db.Set("status", "success");
                myCallback.Invoke(db, null, downloadHandler.GetContentAsString());

            }
        }
    }

}
