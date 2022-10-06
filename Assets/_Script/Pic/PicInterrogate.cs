using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using SimpleJSON;

public class PicInterrogate : MonoBehaviour
{
    float startTime;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    public PicMain m_picScript;

    // Start is called before the first frame update
    void Start()
    {
       
    }

    // Update is called once per frame
    void Update()
    {
        if (m_bIsGenerating)
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            m_picScript.SetStatusMessage(elapsed.ToString("Interrogate: 0.0#"));
        }
        else
        {
            
        }
    }

    public void SetForceFinish(bool bNew)
    {
        if (bNew && m_bIsGenerating)
        {
            m_picScript.SetStatusMessage("(killing process)");
            m_gpu = -1; //invalid
        }

    }
    public bool IsBusy()
    {
        return m_bIsGenerating;
    }

    private void OnDestroy()
    {
        if (m_bIsGenerating)
        {
            Config.Get().SetGPUBusy(m_gpu, false);
        }
    }

    public void SetGPU(int gpuID)
    {
        m_gpu = gpuID;
    }

    public void OnForceInterrogate(int gpu)
    {
        //Debug.Log("Starting upscale...");
        m_gpu = gpu;
    
        StartWebRequest(true);
    }
    public void StartWebRequest(bool bFromButton)
    {
        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        string url = gpuInfo.remoteURL+ "/v1/interrogator";
       
        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }

        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest( url));
    }

    IEnumerator GetRequest( string url)
    {
        //yield return new WaitForEndOfFrame();

        SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
        UnityEngine.Sprite s = renderer.sprite;
        byte[] picPng = s.texture.EncodeToPNG();
        // For testing purposes, also write to a file in the project folder
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //String finalURL = url + "?prompt=" + context + "&prompt_strength=" + prompt_strength;
        String finalURL = url;
        Debug.Log("Interrogating with " + finalURL + " local GPU ID " + m_gpu);
        string imgBase64 = Convert.ToBase64String(picPng);

        var gpuInf = Config.Get().GetGPUInfo(m_gpu);

        string json =
        $@"{{
            ""interrogatorreq"":
            {{
            ""image"": ""{imgBase64}""
        }}

        }}";

        //File.WriteAllText("json_to_send.json", json); //for debugging
       
        using (var postRequest = UnityWebRequest.Post(finalURL, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
               
                
                Debug.Log(postRequest.error);
                Debug.Log(postRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Processing error");
            }
            else
            {
                Debug.Log("Interrogate finished Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);
                var dataNode = rootNode["description"];
              
                string text = dataNode.Value.ToString();
                //System.IO.File.WriteAllText("interrogation.txt", text); //for debugging
                GameLogic.Get().SetPrompt(text);

                if (Config.Get().IsValidGPU(m_gpu))
                {
                    if (!Config.Get().IsGPUBusy(m_gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }
                }
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("");
            }
        }
    }
}
