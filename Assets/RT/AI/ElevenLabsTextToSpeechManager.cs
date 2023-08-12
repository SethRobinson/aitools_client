using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class ElevenLabsTextToSpeechManager : MonoBehaviour
{
 
    void Start()
    {
      //  ExampleOfUse();
    }

    //*  EXAMPLE START (Cut and paste to your own code)*/
   void ExampleOfUse()
    {
        ElevenLabsTextToSpeechManager ttsScript = gameObject.GetComponent<ElevenLabsTextToSpeechManager>();
        string text = "Hello world!";
        string elevenLabsAPIKey = "put it here";
        string elevenLabs_voiceID = "VR6AewLTigWG4xSOukaG"; //Full list of elevenlabs voices: https://beta.elevenlabs.io/speech-synthesis  ( or really, https://api.elevenlabs.io/v1/voices )
        string json = ttsScript.BuildTTSJSON(text);

        //test
        RTDB db = new RTDB();
        ttsScript.SpawnTTSRequest(json, OnTTSCompletedCallback, db, elevenLabsAPIKey, elevenLabs_voiceID);
    }

    void OnTTSCompletedCallback(RTDB db, AudioClip clip)
    {
        if (clip == null)
        {
            Debug.Log("Error getting mp3: "+db.GetString("msg"));
            return;
        }

        //write to file?
        /*
        string filePath = "fname.wav";

        File.WriteAllBytes(filePath, wavData);
        Debug.Log("File saved to: " + filePath);
        RTQuickMessageManager.Get().ShowMessage("Audio received");
        */
        ElevenLabsTextToSpeechManager ttsScript = gameObject.GetComponent<ElevenLabsTextToSpeechManager>();

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.clip = clip;
        audioSource.Play();

    }

    //*  EXAMPLE END */

    public string BuildTTSJSON(string text, float stability = 0.7f, float similarity_boost = 0.7f)
    {
        string json = $@"{{
       
            ""text"": ""{ SimpleJSON.JSONNode.Escape(text)}"",
       ""voice_settings"": {{
    ""stability"": {stability},
    ""similarity_boost"": {similarity_boost}
  }}

    }}";

        return json;
    }


    public bool SpawnTTSRequest(string json, Action<RTDB, AudioClip> myCallback, RTDB db, string elevenlabsAPIkey, string elevenLabsVoice)
    {
        StartCoroutine(GetRequest(json, myCallback, db, elevenlabsAPIkey, elevenLabsVoice));
        return true;
    }
     IEnumerator GetRequest(string json, Action<RTDB, AudioClip> myCallback, RTDB db, string elevenlabsAPIkey,string elevenLabsVoice)
    {
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/"+ elevenLabsVoice;

#if UNITY_STANDALONE && !RT_RELEASE 
        File.WriteAllText("elevenlabs_tts_json_sent.json", json);

#endif
        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            postRequest.SetRequestHeader("accept", "audio/mpeg");
            //postRequest.SetRequestHeader("Authorization", "Bearer "+ OATHtoken); //too much work
            postRequest.SetRequestHeader("xi-api-key", elevenlabsAPIkey);

            // Create the DownloadHandlerAudioClip object and set it as the download handler.
            DownloadHandlerAudioClip audioClipHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            postRequest.downloadHandler = audioClipHandler;

            // Send the request and wait for it to complete.
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            { 
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
#if UNITY_STANDALONE && !RT_RELEASE
             //   File.WriteAllText("elevenlabs_tts_last_error_returned.json", postRequest.downloadHandler.text);
#endif
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {

                //We don't get a json file back for this, just the actual final mp3 file. 


#if UNITY_STANDALONE && !RT_RELEASE 
       //         Debug.Log("TTS Form upload complete! Downloaded " + postRequest.downloadedBytes);
                File.WriteAllBytes("elevenlabs_tts_json_received.mp3", postRequest.downloadHandler.data);
#endif

                db.Set("status", "success");
                myCallback.Invoke(db, ((DownloadHandlerAudioClip)postRequest.downloadHandler).audioClip);

            }
        }
    }
}

