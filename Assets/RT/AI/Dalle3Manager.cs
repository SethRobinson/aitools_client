using SimpleJSON;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class Dalle3Manager : MonoBehaviour
{
 
    void Start()
    {
      //  ExampleOfUse();
    }

    //*  EXAMPLE START (Cut and paste to your own code)*/
   void ExampleOfUse()
    {

        Dalle3Manager dalle3Script = gameObject.GetComponent<Dalle3Manager>();
        string prompt = "a cat reading a book";
        string json = dalle3Script.BuildJSON(prompt);

        string openAIKey = "put it here";

        //test
        RTDB db = new RTDB();
        dalle3Script.SpawnRequest(json, OnDalle3CompletedCallback, db, openAIKey);
    }

    void OnDalle3CompletedCallback(RTDB db, Texture2D tex)
    {
        if (tex == null)
        {
            Debug.Log("Error getting dalle image: "+db.GetString("msg"));
            return;
        }

        //write to file?
        /*
        string filePath = "fname.jpg";

        File.WriteAllBytes(filePath, wavData);
        Debug.Log("File saved to: " + filePath);
        RTQuickMessageManager.Get().ShowMessage("Audio received");
        */
       

    }

    //*  EXAMPLE END */

    public string BuildJSON(string prompt, string model = "dall-e-2")
    {
        string json = $@"{{
       
            ""prompt"": ""{ SimpleJSON.JSONNode.Escape(prompt)}"",
            ""model"": ""{model}"",
            ""n"": 1,
            ""quality"": ""hd"",
            ""response_format"": ""b64_json"",
            ""size"": ""1024x1024""


    }}";

        return json;
    }


    public bool SpawnRequest(string json, Action<RTDB, Texture2D> myCallback, RTDB db, string openAI_APIKey)
    {
        StartCoroutine(GetRequest(json, myCallback, db, openAI_APIKey));
        return true;
    }
     IEnumerator GetRequest(string json, Action<RTDB, Texture2D> myCallback, RTDB db, string openAI_APIKey)
    {
        string url = $"https://api.openai.com/v1/images/generations";

#if UNITY_STANDALONE && !RT_RELEASE 
        File.WriteAllText("dalle3_json_sent.json", json);

#endif
        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            postRequest.SetRequestHeader("Authorization", "Bearer " + openAI_APIKey);

            // Send the request and wait for it to complete.
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            { 
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
                 File.WriteAllText("dalle_last_error_returned.json", postRequest.downloadHandler.text);
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {

                //We don't get a json file back for this, just the actual final mp3 file. 


#if UNITY_STANDALONE && !RT_RELEASE 
       //         Debug.Log("TTS Form upload complete! Downloaded " + postRequest.downloadedBytes);
                File.WriteAllBytes("dalle3_json_received.json", postRequest.downloadHandler.data);
#endif

                //convert the json to a texture2d
                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a free to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                var data = rootNode["data"];
                Debug.Assert(data.Tag == JSONNodeType.Array);

                byte[] imgDataBytes = null;

                if (data != null)
                {
                    for (int i = 0; i < data.Count; i++)
                    {
                        // Extract the base64 encoded string and convert it to bytes
                        string base64EncodedString = data[i]["b64_json"];
                        imgDataBytes = Convert.FromBase64String(base64EncodedString);
                        yield return null; // Wait a frame to lessen the jerkiness
                    }
                }
                else
                {
                    Debug.Log("image data is missing");
                }



                // Load texture from the byte array
                Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                if (texture.LoadImage(imgDataBytes, false))
                {
                    yield return null; // Wait a frame to lessen the jerkiness

                    db.Set("status", "success");
                    myCallback.Invoke(db, texture); // Pass the texture to the callback
                }
                else
                {
                    db.Set("status", "error");
                    myCallback.Invoke(db, null);
                }

            }
        }
    }
}

