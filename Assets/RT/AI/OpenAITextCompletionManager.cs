using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;


public class OpenAITextCompletionManager : MonoBehaviour
{

    public class GTPChatLine
    {
        public GTPChatLine(string role, string content)
        {
            _role = role;
            _content = content;
        }

        public string _role; //must be set to user, assistant, or system
        public string _content;
    }

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

        textCompletionScript.SpawnChatCompleteRequest(json, OnGTP3CompletedCallback, db, openAI_APIKey);
    }

   void OnGTP3CompletedCallback(RTDB db, JSONObject jsonNode)
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
    public bool SpawnChatCompleteRequest(string jsonRequest, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey, string endpoint = "https://api.openai.com/v1/chat/completions")
    {

        StartCoroutine(GetRequest(jsonRequest, myCallback, db, openAI_APIKey, endpoint));
        return true;
    }

    //Build OpenAI.com API request json
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "gpt-3.5-turbo")
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

        string json =
         $@"{{
             ""model"": ""{model}"",
             ""messages"":[{msg}],
             ""temperature"": {temperature},
             ""max_tokens"": {max_tokens}
            }}";

        return json;
    }

    IEnumerator GetRequest(string json, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey, string endpoint)
    {

#if UNITY_STANDALONE && !RT_RELEASE 
               File.WriteAllText("text_completion_sent.json", json);
#endif
        string url;
        url = endpoint;
        //Debug.Log("Sending request " + url );

        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            postRequest.SetRequestHeader("Authorization", "Bearer "+openAI_APIKey);
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", postRequest.downloadHandler.text);
#endif
               
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE 
//                Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes);

                File.WriteAllText("textgen_json_received.json", postRequest.downloadHandler.text);
#endif

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a frame to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                db.Set("status", "success");
                myCallback.Invoke(db, (JSONObject)rootNode);
               
            }
        }
    }
}
