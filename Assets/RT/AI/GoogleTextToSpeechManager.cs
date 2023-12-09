using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using SimpleJSON;

public class GoogleTextToSpeechManager : MonoBehaviour
{
 
    void Start()
    {
      //  ExampleOfUse();
    }

    //*  EXAMPLE START (Cut and paste to your own code)*/
   void ExampleOfUse()
    {
        GoogleTextToSpeechManager ttsScript = gameObject.GetComponent<GoogleTextToSpeechManager>();
        string text = "Hello world!";
        string googleAPIkey = "put it here";

        string json = ttsScript.BuildTTSJSON(text, "en-US", "en-US-Neural2-G");

        //test
        RTDB db = new RTDB();
        ttsScript.SpawnTTSRequest(json, OnTTSCompletedCallback, db, googleAPIkey);
    }

    void OnTTSCompletedCallback(RTDB db, byte[] wavData)
    {
        if (wavData == null)
        {
            Debug.Log("Error getting wav: "+db.GetString("msg"));
            return;
        }

        //write to file?
        /*
        string filePath = "fname.wav";

        File.WriteAllBytes(filePath, wavData);
        Debug.Log("File saved to: " + filePath);
        RTQuickMessageManager.Get().ShowMessage("Audio received");
        */
        GoogleTextToSpeechManager ttsScript = gameObject.GetComponent<GoogleTextToSpeechManager>();

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = ttsScript.MakeAudioClipFromWavFileInMemory(wavData);
        audioSource.Play();

    }

    //*  EXAMPLE END */

    public string BuildTTSJSON(string text, string languageCode = "en-US", string voiceName = "en-US-Neural2-G", int sampleRate = 48000, float pitch = 0, float speed = 1.0f)
    {
        string json = $@"{{
        ""input"": {{
            ""text"": ""{ SimpleJSON.JSONNode.Escape(text)}""
        }},
        ""voice"": {{
            ""languageCode"": ""{languageCode}"",
            ""name"": ""{voiceName}""
        }},
        ""audioConfig"": {{
            ""audioEncoding"": ""LINEAR16"",
            ""pitch"": {pitch},
            ""effectsProfileId"": [
                  ""small-bluetooth-speaker-class-device""
                ],
            ""sampleRateHertz"": {sampleRate},
            ""speakingRate"": {speed}
        }}
    }}";

        return json;
    }

    public AudioClip MakeAudioClipFromWavFileInMemory(byte[] wavData)
    {
     
        //wavData contains a .wav file, including the header.  We must convert it to be passed directly into the
        //audio clip

        // Skip over the WAV header data
        int headerSize = 44;
        if (wavData.Length < headerSize)
        {
            Debug.LogError("Invalid WAV data: header too small");
            return null;
        }

        int dataSize = BitConverter.ToInt32(wavData, 40);
        if (dataSize != wavData.Length - headerSize)
        {
            Debug.LogError("Invalid WAV data: data size doesn't match");
            return null;
        }

        // Verify that the format is compatible with Unity's requirements
        int format = BitConverter.ToInt16(wavData, 20);
        int channels = BitConverter.ToInt16(wavData, 22);
        int sampleRate = BitConverter.ToInt32(wavData, 24);
        int bitsPerSample = BitConverter.ToInt16(wavData, 34);
        if (format != 1 || channels != 1 || bitsPerSample != 16)
        {
            Debug.LogError("Unsupported WAV format: must be mono, 16-bit, 48000 Hz PCM");
            return null;
        }

        // Convert the WAV data to the format expected by Unity's AudioClip
        float[] samples = new float[dataSize / 2];
        for (int i = 0; i < dataSize; i += 2)
        {
            short sample = (short)((wavData[i + headerSize + 1] << 8) | wavData[i + headerSize]);
            samples[i / 2] = sample / 32768.0f;
        }

        AudioClip audioClip = AudioClip.Create("MyClip", dataSize / 2, 1, sampleRate, false);

        // Set the data of the AudioClip using the converted samples
        audioClip.SetData(samples, 0);

        // Play the audio clip
        return audioClip;
    }

    public bool SpawnTTSRequest(string json, Action<RTDB, byte[]> myCallback, RTDB db, string googleAPIkey)
    {
        StartCoroutine(GetRequest(json, myCallback, db, googleAPIkey));
        return true;
    }
     IEnumerator GetRequest(string json, Action<RTDB, byte[]> myCallback, RTDB db, string googleAPIkey)
    {
        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize";

#if UNITY_STANDALONE && !RT_RELEASE 
        File.WriteAllText("tts_json_sent.json", json);

#endif
        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            //postRequest.SetRequestHeader("Authorization", "Bearer "+ OATHtoken); //too much work
            postRequest.SetRequestHeader("x-goog-api-key", googleAPIkey);
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
//#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("tts_last_error_returned.json", postRequest.downloadHandler.text);
//#endif
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {


#if UNITY_STANDALONE && !RT_RELEASE 
       //         Debug.Log("TTS Form upload complete! Downloaded " + postRequest.downloadedBytes);
                File.WriteAllText("tts_json_received.json", postRequest.downloadHandler.text);
#endif

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a frame to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                string audioContent = rootNode["audioContent"];

                byte[] wavBytes = System.Convert.FromBase64String(audioContent);
            
                db.Set("status", "success");
                myCallback.Invoke(db, wavBytes);

            }
        }
    }
}

