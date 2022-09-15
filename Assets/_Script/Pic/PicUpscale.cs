using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using MiniJSON;
using System.IO;
using UnityEngine.ProBuilder.Shapes;
using System.Security.Policy;

public class PicUpscale : MonoBehaviour
{
    float startTime;
    string m_prompt;
    int m_steps;
    float m_prompt_strength;
    int m_seed;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    bool m_fixFaces = false;
    float m_upscale = 1.0f; //1 means no change
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
            m_picScript.SetStatusMessage(elapsed.ToString("Upscale: 0.0#"));
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

    public void OnForceUpscale(int gpu)
    {
        //Debug.Log("Starting upscale...");
        m_gpu = gpu;
        m_upscale = 2.0f;
        m_fixFaces = true;

        StartWebRequest(true);
    }
    public void StartWebRequest(bool bFromButton)
    {
        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        string url = gpuInfo.remoteURL+"/process_image";
       
        m_seed = UnityEngine.Random.Range(1, 20000000);
        m_prompt = GameLogic.Get().GetPrompt();
        m_steps = GameLogic.Get().GetSteps();
        m_prompt_strength = 7.5f;
        if (!bFromButton)
        {
            m_upscale = GameLogic.Get().GetUpscale();
            m_fixFaces = GameLogic.Get().GetFixFaces();

            if (m_fixFaces)
            {
                gameObject.GetComponent<PicMask>().SetMaskVisible(false);
            }
        }
        
        if (!m_fixFaces)
        {
            return; //we are done
        }
        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }

        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest(m_prompt, m_prompt_strength, url, m_steps, m_seed));

    }

    IEnumerator GetRequest(String context, double prompt_strength, string url, int steps, int seed)
    {
        //yield return new WaitForEndOfFrame();

        SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
        UnityEngine.Sprite s = renderer.sprite;
        byte[] picPng = s.texture.EncodeToPNG();
        // For testing purposes, also write to a file in the project folder
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        WWWForm form = new WWWForm();
        //Create the request using a static method instead of a constructor

        //String finalURL = url + "?prompt=" + context + "&prompt_strength=" + prompt_strength;
        String finalURL = url;
        Debug.Log("Upscaling with " + finalURL + " local GPU ID " + m_gpu);

        //form.AddField("prompt", context);
        form.AddField("fixfaces", m_fixFaces.ToString());
        form.AddField("gpu", Config.Get().GetGPUInfo(m_gpu).remoteGPUID.ToString());
        form.AddField("upscale", m_upscale.ToString());
        form.AddBinaryData("file", picPng);

        using (var postRequest = UnityWebRequest.Post(finalURL, form))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(postRequest.error);
                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Processing error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);

                if (texture.LoadImage(postRequest.downloadHandler.data, false))
                {
                    //Debug.Log("Read texture, setting to new image");
                    this.gameObject.GetComponent<PicMain>().AddImageUndo();

                    float biggestSize = Math.Max(texture.width, texture.height);
                    UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(texture, new Rect(0,0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);
                    renderer.sprite = newSprite;
                    this.gameObject.GetComponent<PicMain>().OnImageReplaced();
                }
                else
                {
                    Debug.Log("Error reading texture");
                }

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
