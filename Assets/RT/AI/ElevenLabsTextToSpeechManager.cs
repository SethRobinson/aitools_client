using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class ElevenLabsTextToSpeechManager : MonoBehaviour
{
    public const string DefaultVoiceId = "21m00Tcm4TlvDq8ikWAM";
    private const string DebugSentJsonFileName = "elevenlabs_tts_json_sent.json";
    private const string DebugReceivedMp3FileName = "elevenlabs_tts_json_received.mp3";
    public static readonly KeyValuePair<string, string>[] DefaultVoicePresets =
    {
        new KeyValuePair<string, string>("Rachel", "21m00Tcm4TlvDq8ikWAM"),
        new KeyValuePair<string, string>("Domi", "AZnzlk1XvdvUeBnXmlld"),
        new KeyValuePair<string, string>("Bella", "EXAVITQu4vr4xnSDxMaL"),
        new KeyValuePair<string, string>("Antoni", "ErXwobaYiN019PkySvjV"),
        new KeyValuePair<string, string>("Elli", "MF3mGyEYCl7XYWbV9V6O"),
        new KeyValuePair<string, string>("Josh", "TxGEqnHWrfWFTfGW9XjX"),
        new KeyValuePair<string, string>("Arnold", "VR6AewLTigWG4xSOukaG"),
        new KeyValuePair<string, string>("Adam", "pNInz6obpgDQGcFmaJgB"),
        new KeyValuePair<string, string>("Sam", "yoZ06aMxZJJ28mfd3POQ")
    };

    Coroutine _activeRequestCoroutine;
    Coroutine _playbackWatchCoroutine;
    UnityWebRequest _activeRequest;
    AudioSource _activeAudioSource;
    Action<string> _activeStatusCallback;

    void Start()
    {
      //  ExampleOfUse();
    }

    void OnApplicationQuit()
    {
        CleanupDebugFiles();
    }

    void OnDestroy()
    {
        CleanupDebugFiles();
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
        PlayClip(clip);

    }

    //*  EXAMPLE END */

    public string BuildTTSJSON(string text, float stability = 0.7f, float similarity_boost = 0.7f)
    {
        string stabilityText = stability.ToString("0.###", CultureInfo.InvariantCulture);
        string similarityText = similarity_boost.ToString("0.###", CultureInfo.InvariantCulture);
        string json = $@"{{
       
            ""text"": ""{ SimpleJSON.JSONNode.Escape(text)}"",
       ""voice_settings"": {{
    ""stability"": {stabilityText},
    ""similarity_boost"": {similarityText}
  }}

    }}";

        return json;
    }

    public static bool CanSpeakConfigured(out string reason)
    {
        reason = "";
        Config cfg = Config.Get();
        if (cfg == null)
        {
            reason = "Text To Speech settings are not initialized yet.";
            return false;
        }

        if (cfg.GetTextToSpeechProvider() != TextToSpeechProvider.ElevenLabs)
        {
            reason = "Text To Speech is not set up. Open Settings > Audio and choose ElevenLabs.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(cfg.GetElevenLabs_APIKey()))
        {
            reason = "ElevenLabs Text To Speech needs an API key. Add it in Settings > Audio.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(cfg.GetElevenLabs_voiceID()))
        {
            reason = "ElevenLabs Text To Speech needs a voice ID. Choose a voice in Settings > Audio.";
            return false;
        }

        return true;
    }

    public static bool SpeakConfigured(string text, Action<string> statusCallback = null)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            statusCallback?.Invoke("Nothing to speak.");
            return false;
        }

        if (!CanSpeakConfigured(out string reason))
        {
            statusCallback?.Invoke(reason);
            return false;
        }

        Config cfg = Config.Get();
        var ttsScript = cfg.GetComponent<ElevenLabsTextToSpeechManager>();
        if (ttsScript == null)
            ttsScript = cfg.gameObject.AddComponent<ElevenLabsTextToSpeechManager>();

        ttsScript.StopCurrentSpeech(false);
        ttsScript._activeStatusCallback = statusCallback;
        string json = ttsScript.BuildTTSJSON(text);
        var db = new RTDB();
        statusCallback?.Invoke("Requesting speech from ElevenLabs...");
        ttsScript.SpawnTTSRequest(json, (resultDb, clip) =>
        {
            if (clip == null)
            {
                string msg = resultDb != null ? resultDb.GetStringWithDefault("msg", "Unknown Text To Speech error") : "Unknown Text To Speech error";
                ttsScript._activeStatusCallback = null;
                statusCallback?.Invoke("Text To Speech failed: " + msg);
                return;
            }

            ttsScript.PlayClip(clip, statusCallback);
        }, db, cfg.GetElevenLabs_APIKey(), cfg.GetElevenLabs_voiceID());

        return true;
    }

    public static bool IsConfiguredSpeechActive()
    {
        Config cfg = Config.Get();
        var ttsScript = cfg != null ? cfg.GetComponent<ElevenLabsTextToSpeechManager>() : null;
        return ttsScript != null && ttsScript.IsSpeechActive();
    }

    public static void StopConfiguredSpeech(string statusMessage = "Speech stopped.")
    {
        Config cfg = Config.Get();
        var ttsScript = cfg != null ? cfg.GetComponent<ElevenLabsTextToSpeechManager>() : null;
        if (ttsScript != null)
            ttsScript.StopCurrentSpeech(true, statusMessage);
    }

    public static void CleanupDebugFiles()
    {
#if UNITY_STANDALONE && !RT_RELEASE
        DeleteDebugFile(DebugSentJsonFileName);
        DeleteDebugFile(DebugReceivedMp3FileName);
#endif
    }

    private static void DeleteDebugFile(string fileName)
    {
        try
        {
            if (File.Exists(fileName))
                File.Delete(fileName);
        }
        catch
        {
            // Debug artifacts should never block shutdown.
        }
    }

    public bool IsSpeechActive()
    {
        return _activeRequest != null || _activeRequestCoroutine != null ||
            (_activeAudioSource != null && _activeAudioSource.isPlaying);
    }

    public void StopCurrentSpeech(bool notify = true, string statusMessage = "Speech stopped.")
    {
        bool hadActive = IsSpeechActive();

        if (_activeRequest != null)
            _activeRequest.Abort();
        if (_activeRequestCoroutine != null)
            StopCoroutine(_activeRequestCoroutine);
        _activeRequest = null;
        _activeRequestCoroutine = null;

        StopCurrentPlayback();

        if (notify && hadActive)
        {
            _activeStatusCallback?.Invoke(statusMessage);
            _activeStatusCallback = null;
        }
    }

    public void PlayClip(AudioClip clip)
    {
        PlayClip(clip, null);
    }

    public void PlayClip(AudioClip clip, Action<string> statusCallback)
    {
        if (clip == null) return;

        StopCurrentPlayback();

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.spatialBlend = 0f;
        audioSource.Play();
        _activeAudioSource = audioSource;
        _activeStatusCallback = statusCallback;
        statusCallback?.Invoke("Speaking.");
        _playbackWatchCoroutine = StartCoroutine(WatchPlayback(audioSource, statusCallback));
    }

    private void StopCurrentPlayback()
    {
        if (_playbackWatchCoroutine != null)
            StopCoroutine(_playbackWatchCoroutine);
        _playbackWatchCoroutine = null;

        if (_activeAudioSource != null)
        {
            _activeAudioSource.Stop();
            Destroy(_activeAudioSource);
            _activeAudioSource = null;
        }
    }

    private IEnumerator WatchPlayback(AudioSource source, Action<string> statusCallback)
    {
        while (source != null && source.isPlaying)
            yield return null;

        if (_activeAudioSource == source)
        {
            _activeAudioSource = null;
            _playbackWatchCoroutine = null;
            if (source != null)
                Destroy(source);
            _activeStatusCallback = null;
            statusCallback?.Invoke("Speech finished.");
        }
    }


    public bool SpawnTTSRequest(string json, Action<RTDB, AudioClip> myCallback, RTDB db, string elevenlabsAPIkey, string elevenLabsVoice)
    {
        _activeRequestCoroutine = StartCoroutine(GetRequest(json, myCallback, db, elevenlabsAPIkey, elevenLabsVoice));
        return true;
    }
     IEnumerator GetRequest(string json, Action<RTDB, AudioClip> myCallback, RTDB db, string elevenlabsAPIkey,string elevenLabsVoice)
    {
        string url = $"https://api.elevenlabs.io/v1/text-to-speech/"+ elevenLabsVoice;

#if UNITY_STANDALONE && !RT_RELEASE 
        File.WriteAllText(DebugSentJsonFileName, json);

#endif
        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            _activeRequest = postRequest;
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

            if (_activeRequest == postRequest)
                _activeRequest = null;
            _activeRequestCoroutine = null;

            if (postRequest.result != UnityWebRequest.Result.Success)
            { 
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
//#if UNITY_STANDALONE && !RT_RELEASE
             //   File.WriteAllText("elevenlabs_tts_last_error_returned.json", postRequest.downloadHandler.text);
//#endif
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback?.Invoke(db, null);
            }
            else
            {

                //We don't get a json file back for this, just the actual final mp3 file. 


#if UNITY_STANDALONE && !RT_RELEASE 
       //         Debug.Log("TTS Form upload complete! Downloaded " + postRequest.downloadedBytes);
                File.WriteAllBytes(DebugReceivedMp3FileName, postRequest.downloadHandler.data);
#endif

                db.Set("status", "success");
                myCallback?.Invoke(db, ((DownloadHandlerAudioClip)postRequest.downloadHandler).audioClip);

            }
        }
    }
}

