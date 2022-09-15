using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;


public class PicTextToImage : MonoBehaviour
{
    float startTime;
    string m_prompt;
    int m_steps;
    float m_prompt_strength;
    int m_seed;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    public PicMain m_picScript;
    public Action<GameObject> OnFinishedRendering;
  
    public void SetForceFinish(bool bNew)
    {
        if (bNew && m_bIsGenerating)
        {
            m_picScript.SetStatusMessage("(killing process)");
            OnFinishedRendering = null;
            m_gpu = -1; //invalid
        }
        
    }
    public bool IsBusy()
    {
        return m_bIsGenerating;
    }

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
            if (m_gpu == -1)
            {
                m_picScript.SetStatusMessage(elapsed.ToString("(killing): 0.0#"));
            }
            else
            {
                m_picScript.SetStatusMessage(elapsed.ToString("Generate: 0.0#"));
            }
        } 
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
    public void StartWebRequest(bool rerender)
    {
      
        m_steps = GameLogic.Get().GetSteps();

        if (!rerender || m_prompt == null || m_prompt == "")
            {
            m_seed = UnityEngine.Random.Range(1, 20000000);
            m_prompt = GameLogic.Get().GetPrompt();
            m_prompt_strength = GameLogic.Get().GetTextStrength();
        }

        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }
        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;
        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);
        string url = gpuInfo.remoteURL+"/generate";

        
        StartCoroutine(GetRequest(m_prompt, m_prompt_strength,url, m_steps, m_seed));

    }
    public void StartGenerate()
    {
        StartWebRequest(true);
    }
    IEnumerator GetRequest(String context, double prompt_strength, string url, int steps, int seed)
    {
        WWWForm form = new WWWForm();

        //Create the request using a static method instead of a constructor

        //String finalURL = url + "?prompt=" + context + "&prompt_strength=" + prompt_strength;
        String finalURL = url;
       Debug.Log("Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        form.AddField("prompt", context);
        form.AddField("prompt_strength", prompt_strength.ToString());
        form.AddField("gpu",  Config.Get().GetGPUInfo(m_gpu).remoteGPUID.ToString());
        form.AddField("seed", seed.ToString());
        form.AddField("steps", steps.ToString());
        form.AddField("safetyfilter", Config.Get().GetSafetyFilter().ToString());

        using (var postRequest = UnityWebRequest.Post(finalURL, form))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(postRequest.error);
                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
                Sprite s = renderer.sprite;

                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                bool bSuccess = false;
               
                if (texture.LoadImage(postRequest.downloadHandler.data, false))
                {

                    m_picScript.AddImageUndo();
                    //Debug.Log("Read texture");
                    float biggestSize = Math.Max(texture.width, texture.height);
                    
                    Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);
                    renderer.sprite = newSprite;
                    bSuccess = true;
                }
                else
                {
                    Debug.Log("Error reading texture");
                }

               
                m_picScript.SetStatusMessage("");

                if (bSuccess && Config.Get().IsValidGPU(m_gpu) && m_bIsGenerating)
                {
                    m_bIsGenerating = false;

                    if (!Config.Get().IsGPUBusy(m_gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(m_gpu, false);
                    }

                    //initiate second stage processing?
                    PicUpscale processScript = this.gameObject.GetComponent<PicUpscale>();

                    if (processScript && GameLogic.Get().GetFixFaces())
                    {
                        processScript.SetGPU(m_gpu);
                        processScript.StartWebRequest(false);
                    } else
                    {
                        if (OnFinishedRendering != null)
                            OnFinishedRendering.Invoke(gameObject);
                    }
                }
              
                m_bIsGenerating = false;
            
            }

        }

    }

}
