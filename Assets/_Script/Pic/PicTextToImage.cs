using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using SimpleJSON;
using System.IO;
using System.Globalization;

public class PicTextToImage : MonoBehaviour
{
    float startTime;
    string m_prompt = null;
    string m_negativePrompt = null;
    int m_steps;
    float m_prompt_strength;
    long m_seed = -1;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    public PicMain m_picScript;
    public Action<GameObject> m_onFinishedRenderingCallback;


    public void SetForceFinish(bool bNew)
    {
        if (bNew && m_bIsGenerating)
        {
            m_picScript.SetStatusMessage("(killing process)");
            m_onFinishedRenderingCallback = null;
            m_gpu = -1; //invalid
        }
        
    }
    public void SetSeed(long seed)
    {
        m_seed = seed;
    }
    public bool WasCustomSeedSet()
    {
        return m_seed != -1;
    }

   
    public long GetSeed() { return m_seed; }
    public bool IsBusy()
    {
        return m_bIsGenerating;
    }
    public string GetPrompt()
    {
        return m_prompt;
    }

    public void SetPrompt(string prompt)
    {
        m_prompt = prompt;
    }
    public void SetNegativePrompt(string prompt)
    {
        m_negativePrompt = prompt;
    }

    public float GetTextStrength() { return m_prompt_strength; }
    public void SetTextStrength(float strength) { m_prompt_strength = strength; }
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

        if (!rerender)
        {
            m_seed = GameLogic.Get().GetSeed();
           
            if (m_prompt == null)
            {
                if (ImageGenerator.Get().IsGenerating())
                {
                    m_prompt = GameLogic.Get().GetModifiedPrompt();
                }
                else
                {
                    m_prompt = GameLogic.Get().GetPrompt();
                }
            }

            if (m_negativePrompt == null)
            {
                m_negativePrompt = GameLogic.Get().GetNegativePrompt();
            }

            m_prompt_strength = GameLogic.Get().GetTextStrengthFloat();
        }

        if (m_seed == -1)
        {
            var rand = new System.Random();
            //let's set it to our own random so we know what it is later
            m_seed = Math.Abs(rand.NextLong());
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
        string url = gpuInfo.remoteURL;

        
        StartCoroutine(GetRequest(m_prompt, m_prompt_strength,url));

    }
    public void StartGenerate()
    {
        StartWebRequest(true);
    }
    public void StartGenerateInitialRender()
    {
        StartWebRequest(false);
    }
    IEnumerator GetRequest(String context, double prompt_strength, string url)
    {
        WWWForm form = new WWWForm();

        String finalURL;
       
        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();
        bool bRemoveBackground = GameLogic.Get().GetRemoveBackground();

        int genWidth = GameLogic.Get().GetGenWidth();
        int genHeight = GameLogic.Get().GetGenHeight();
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);
        string model = GameLogic.Get().GetActiveModelFilename();
        string samplerName = GameLogic.Get().GetSamplerName();
        bool hiresFix = GameLogic.Get().GetHiresFix();

        string safety_filter = ""; //use whatever the server is set at
        if (Config.Get().GetSafetyFilter())
        {
            safety_filter = $@"""override_settings"": {{""filter_nsfw"": true}},";
        }

       finalURL = url + "/sdapi/v1/txt2img";
       int steps = GameLogic.Get().GetSteps();

      

        string promptStrString = prompt_strength.ToString("0.0", CultureInfo.InvariantCulture);
            //using the new API which doesn't support alpha masking the subject
            string json =
                 $@"{{
        
            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(m_prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(m_negativePrompt)}"",
            ""steps"": {steps},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{promptStrString},
            ""seed"": {m_seed},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{samplerName}"",
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()},

 ""enable_hr"": {hiresFix.ToString().ToLower()},
  ""denoising_strength"": 0.7,
  ""firstphase_width"": 0,
  ""firstphase_height"": 0,
  ""hr_scale"": 2
        
        }}";

        RTConsole.Log("Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        //",\"" + GameLogic.Get().GetSamplerName() + "\","  +  + ","

#if !RT_RELEASE
       // File.WriteAllText("json_to_send.json", json);
#endif

        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            postRequest.SetRequestHeader("Content-Type", "application/json");

            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (" + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(postRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Generate error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                //Ok, we now have to dig into the response and pull out the json image

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a free to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                /*
                foreach (KeyValuePair<string, JSONNode> kvp in (JSONObject)rootNode)
                {
                    Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
                }
                */

                var images = rootNode["images"];
                Debug.Assert(images.Tag == JSONNodeType.Array);

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i=0; i<images.Count; i++)
                    {
                        //convert each to be a pic
                        imgDataBytes = Convert.FromBase64String(images[i]);
                        yield return null; //wait a free to lesson the jerkiness
                    }
                }
                else
                {
                    Debug.Log("image data is missing");
                }

                SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
                Sprite s = renderer.sprite;

                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                bool bSuccess = false;
                yield return null; //wait a frame to lesson the jerkiness

                if (texture.LoadImage(imgDataBytes, false))
                {
                    yield return null; //wait a frame to lesson the jerkiness
                  
                   // m_picScript.AddImageUndo();
                    //Debug.Log("Read texture");
                    float biggestSize = Math.Max(texture.width, texture.height);
                    
                    Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
                    renderer.sprite = newSprite;
                    bSuccess = true;

                    if (bRemoveBackground)
                    {
                        m_picScript.FillAlphaMaskWithImageAlpha();
                    }
                    m_picScript.OnImageReplaced();

                    //we've already done the undo, so now let's update with the info we used to make this
                    m_picScript.GetCurrentStats().m_lastPromptUsed = m_prompt;
                    m_picScript.GetCurrentStats().m_lastNegativePromptUsed = m_negativePrompt;
                    m_picScript.GetCurrentStats().m_lastSteps = steps;
                    m_picScript.GetCurrentStats().m_lastCFGScale = (float)prompt_strength;
                    m_picScript.GetCurrentStats().m_lastSampler = samplerName;
                    m_picScript.GetCurrentStats().m_tiling = bTiled;
                    m_picScript.GetCurrentStats().m_fixFaces = bFixFace;
                    m_picScript.GetCurrentStats().m_lastSeed = m_seed;
                    m_picScript.GetCurrentStats().m_lastModel = model;
                    m_picScript.GetCurrentStats().m_bUsingControlNet = false;
                    m_picScript.GetCurrentStats().m_bUsingPix2Pix = false;
                    m_picScript.GetCurrentStats().m_lastOperation = "Generate";
                    m_picScript.GetCurrentStats().m_gpu = m_gpu;

                    m_picScript.SetNeedsToUpdateInfoPanelFlag();


                    m_picScript.AutoSaveImageIfNeeded();
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

                    if (processScript && GameLogic.Get().GetUpscale() > 1.0f)
                    {
                        processScript.SetGPU(m_gpu);
                        processScript.StartWebRequest(false);
                    } else
                    {
                        if (m_onFinishedRenderingCallback != null)
                            m_onFinishedRenderingCallback.Invoke(gameObject);
                    }
                }
              
                m_bIsGenerating = false;
            
            }

        }

    }

}
