using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;


public class OpenAISpeechToTextManager : MonoBehaviour
{

    public void Start()
    {
        // ExampleOfUse();
    }

    //*  EXAMPLE START (this could be moved to your own code) */

    void ExampleOfUse()
    {
        OpenAISpeechToTextManager speechToTextScript = gameObject.GetComponent<OpenAISpeechToTextManager>();

        string fileName = "output.wav";
        byte[] fileBytes = System.IO.File.ReadAllBytes(fileName);
        string openAI_APIKey = "put it here";
        string prompt = "";

        RTDB db = new RTDB();
        speechToTextScript.SpawnSpeechToTextRequest(prompt, OnSpeechToTextCompletedCallback, db, openAI_APIKey, fileBytes);
    }

    void OnSpeechToTextCompletedCallback(RTDB db, JSONObject jsonNode)
    {

        if (jsonNode == null)
        {
            //must have been an error
            Debug.Log("Got callback! Data: " + db.ToString());
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            return;
        }

       
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }

        string reply = jsonNode["text"];
       // RTQuickMessageManager.Get().ShowMessage(reply);

    }

    //*  EXAMPLE END */
    public bool SpawnSpeechToTextRequest(string prompt, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey, byte[] wavData)
    {

        StartCoroutine(GetRequest(prompt, myCallback, db, openAI_APIKey, wavData));
        return true;
    }

    IEnumerator GetRequest(string prompt, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey, byte[] wavData)
    {

        string url;
        url = "https://api.openai.com/v1/audio/transcriptions";
        string model = "whisper-1";

        WWWForm formData = new WWWForm();
        formData.AddField("model", model);

        if (prompt != "")
        {
            formData.AddField("prompt", model);
        }

        formData.AddBinaryData("file", wavData, "openai.wav", "audio/wav");
     
        using (var postRequest = UnityWebRequest.Post(url, formData))
        {
            postRequest.SetRequestHeader("Authorization", "Bearer " + openAI_APIKey);
            postRequest.downloadHandler = new DownloadHandlerBuffer();
            yield return postRequest.SendWebRequest();
       
            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error;
                Debug.Log(msg);
//               #if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", postRequest.downloadHandler.text);
 //               #endif
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes);
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
