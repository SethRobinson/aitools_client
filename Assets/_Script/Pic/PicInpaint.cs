using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using SimpleJSON;
using System.IO;
using UnityEngine.ProBuilder.Shapes;
using System.Security.Policy;
using System.Net;

public class PicInpaint : MonoBehaviour
{
    public PicTargetRect m_targetRect;
    float startTime;
    string m_prompt;
    string m_negativePrompt;
    long m_seed = -1;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    public SpriteRenderer m_spriteMask;
    public Action<GameObject> m_onFinishedRenderingCallback;
    public PicMain m_picScript;
    public Texture2D m_latentNoise; //a texture we'll use for the noise instead of generating it ourself
    public PicTextToImage m_picTextToImageScript;
    bool m_useExisting = false;
    bool m_bControlNetWasUsed = false;
    public void SetForceFinish(bool bNew)
    {
        if (bNew && m_bIsGenerating)
        {
            m_picScript.SetStatusMessage("(killing process)");
            m_onFinishedRenderingCallback = null;
            m_gpu = -1; //invalid
        }

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
                m_picScript.SetStatusMessage(elapsed.ToString("img2img: 0.0#"));
            }
        }
    }

    public bool IsBusy() { return m_bIsGenerating; }
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

    public void SetUseExistingSettingsIfSet(bool bNew)
    {
        m_useExisting = bNew;
    }

    public void SetPrompt(string prompt)
    {
        m_prompt = prompt;
    }

    public void SetNegativePrompt(string negativePrompt)
    {
        m_negativePrompt = negativePrompt;
    }
    public void StartInpaint()
    {
       
        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }

        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);
     
        m_seed = GameLogic.Get().GetSeed();

        if (m_seed == -1)
        {
            var rand = new System.Random();
            //let's set it to our own random so we know what it is later
            m_seed = Math.Abs(rand.NextLong());
        }

        if (!m_useExisting || m_prompt == "")
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

        if (!m_useExisting || m_negativePrompt == "")
            m_negativePrompt = GameLogic.Get().GetNegativePrompt();

     
        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest(gpuInfo.remoteURL));
    }

    IEnumerator GetRequest( string url)
    {
        //yield return new WaitForEndOfFrame();

        SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
        UnityEngine.Sprite picSprite = renderer.sprite;

        Texture2D pic512 = new Texture2D(m_targetRect.GetWidth(), m_targetRect.GetHeight(), TextureFormat.RGBA32, false);
        Texture2D mask512 = new Texture2D(m_targetRect.GetWidth(), m_targetRect.GetHeight(), TextureFormat.RGBA32, false);

        pic512.Blit(0, 0, picSprite.texture, m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), m_targetRect.GetWidth(), m_targetRect.GetHeight());
        yield return null; //wait a free to lesson the jerkiness

        mask512.Blit(0, 0, m_spriteMask.sprite.texture, m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), m_targetRect.GetWidth(), m_targetRect.GetHeight());
        yield return null; //wait a free to lesson the jerkiness

        /*
        //apply latent noise if needed
        if (GameLogic.Get().GetExtraNoise() > 0)
        {
            pic512.SetPixelsFromTextureWithAlphaMask(m_latentNoise, mask512, GameLogic.Get().GetExtraNoise());
            yield return null; //wait a frame to lessen the jerkiness

            // picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), pic512, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
            // picSprite.texture.Apply();
            //byte[] noisedPic = pic512.EncodeToPNG();
            //File.WriteAllBytes(Application.dataPath + "/../SavedScreenWithNoise.png", noisedPic);
        }
        */

        byte[] picPng = pic512.EncodeToPNG();
        Destroy(pic512);


        yield return null; //wait a free to lesson the jerkiness
        

#if !RT_RELEASE
        //For testing purposes, we can write out what we're going to send
       // File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);
#endif
        //remove alpha from texture
        //clumisly change a full alpha png to just RGB and replace transparent with black, as that'picSprite how our API wants it
        var newtex = mask512.ConvertTextureToBlackAndWhiteRGBMask();
        yield return null; //wait a free to lesson the jerkiness

        //if we wanted the true full alpha, we'd just use this instead:   byte[] picMaskPng = alphatex.EncodeToPNG();
        byte[] picMaskPng = newtex.EncodeToPNG();
        yield return null; //wait a free to lesson the jerkiness

        Destroy(mask512);
        Destroy(newtex);
#if !RT_RELEASE

        //For testing purposes, we could also write to a file in the project folder
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreenMask.png",picMaskPng);
#endif
        String finalURL;
        
        string imgBase64 = Convert.ToBase64String(picPng);
        string maskBase64 = Convert.ToBase64String(picMaskPng);

        string maskedContent = GameLogic.Get().GetMaskContent();
        int maskBlur = (int) GameLogic.Get().GetAlphaMaskFeatheringPower(); //0 to 64

        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();
        bool bRemoveBackground = GameLogic.Get().GetRemoveBackground();

        var gpuInf = Config.Get().GetGPUInfo(m_gpu);
        var genHeight = m_targetRect.GetHeight();
        var genWidth = m_targetRect.GetWidth();

        string safety_filter = ""; //use whatever the server is set at
        if (Config.Get().GetSafetyFilter())
        {
            safety_filter = $@"""override_settings"": {{""filter_nsfw"": true}},";
        }

        string json;

        //using the new API which doesn't support alpha masking the subject
        finalURL = url + "/sdapi/v1/img2img";
        string normal_input_images = ""; //not used if controlnet is used
        string controlnet_json = "";

        if (GameLogic.Get().GetUseControlNet())
        {
          //  finalURL = url + "/controlnet/img2img";
            m_bControlNetWasUsed = true;

            //if we weren't generating our own control net input image on the fly, we'd use this: ""input_image"":""{controlNetimgBase64}"",
          controlnet_json = $@"""controlnet"": {{
          ""args"": [
            {{
          ""module"": ""{GameLogic.Get().GetCurrentControlNetPreprocessorString()}"",
          ""model"": ""{GameLogic.Get().GetCurrentControlNetModelString()}"",
          ""weight"": {GameLogic.Get().GetControlNetWeight()},
          ""guidance_start"": 0,
          ""guidance_end"": {GameLogic.Get().GetControlNetGuidance()}
            }}
          ]
        }}";
         
        }
        else
        {
            m_bControlNetWasUsed = false;
        }

        normal_input_images = $@"""init_images"":     
            [
                ""{imgBase64}""
            ],
            ""mask"": ""{maskBase64}"",
            ";


        string maskedContentIndex = "1";

        if (maskedContent == "fill") maskedContentIndex = "0";
        if (maskedContent == "original") maskedContentIndex = "1";
        if (maskedContent == "latent noise") maskedContentIndex = "2";
        if (maskedContent == "latent nothing") maskedContentIndex = "3";

        int steps = GameLogic.Get().GetSteps();
        bool bUsingPixToPix = GameLogic.Get().IsActiveModelPix2Pix();
        string model = GameLogic.Get().GetActiveModelFilename();
        string samplerName = GameLogic.Get().GetSamplerName();
        float prompt_cfg = GameLogic.Get().GetTextStrengthFloat();
        float denoising_strength = GameLogic.Get().GetInpaintStrengthFloat();
        string lastControlNetModel = GameLogic.Get().GetCurrentControlNetModelString();
        float pix2pixCFG = GameLogic.Get().GetPix2PixTextStrengthFloat();


        string lastControlNetPreprocessor = "";
        if (m_bControlNetWasUsed)
        {
            lastControlNetPreprocessor = GameLogic.Get().GetCurrentControlNetPreprocessorString();
        }

        float lastControlNetWeight = GameLogic.Get().GetControlNetWeight();
        float lastControlNetGuidance = GameLogic.Get().GetControlNetGuidance();
        string maskContents = GameLogic.Get().GetMaskContent();
       
        
        json =
         $@"{{
           {normal_input_images}

            ""alwayson_scripts"": {{
            {controlnet_json}
            }},

            ""inpainting_mask_invert"": 0,
            ""inpaint_full_res_padding"": 0,
            ""inpaint_full_res"": false,
            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(m_prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(m_negativePrompt)}"",
            ""steps"": {GameLogic.Get().GetSteps()},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{GameLogic.Get().GetTextStrengthString()},
            ""image_cfg_scale"":{GameLogic.Get().GetPix2PixTextStrengthString()},
            ""seed"": {m_seed},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_index"": ""{GameLogic.Get().GetSamplerName()}"",
           
            ""denoising_strength"": {GameLogic.Get().GetInpaintStrengthString()},
            ""mask_blur"": {maskBlur},
            ""inpainting_fill"": {maskedContentIndex},
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()}
            
        }}";

        RTConsole.Log("img2img with " + finalURL + " local GPU ID " + m_gpu);


#if !RT_RELEASE
                File.WriteAllText("json_to_send.json", json);
#endif
        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (" + Config.Get().GetGPUName(m_gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(postRequest.downloadHandler.text);

                Config.Get().SetGPUBusy(m_gpu, false);
                m_bIsGenerating = false;
                m_picScript.SetStatusMessage("Processing error");
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

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

                //Debug.Log("images is of type " + images.Tag);
                //Debug.Log("there are " + images.Count + " images");

                if (!m_bControlNetWasUsed)
                {
                    Debug.Assert(images.Count == 1); //You better convert the extra images to new pics!
                }

                //we probably got 2 images, one is the thing we made for the control

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i = 0; i < images.Count; i++)
                    {

                        if (m_bControlNetWasUsed && i == 1)
                        {
                            //this is the second image, the control image it generated
                            byte[] controlImageBytes = Convert.FromBase64String(images[i]);

                            Texture2D tex = new Texture2D(0, 0, TextureFormat.RGBA32, false);

                            if (tex.LoadImage(controlImageBytes, false))
                            {
                                yield return null; //wait a frame to lesson the jerkiness

                                //give it to the thing
                                m_picScript.SetControlImage(tex);
                            }

                                yield return null; //wait a free to lesson the jerkiness
                            continue;
                        }

                        //First get rid of the "data:image/png;base64," part
                        /*
                        string str = images[i].ToString();
                        yield return null; //wait a free to lesson the jerkiness

                        int startIndex = str.IndexOf(",") + 1;
                        int endIndex = str.LastIndexOf('"');

                        string picChars = str.Substring(startIndex, endIndex- startIndex);
                        yield return null; //wait a free to lesson the jerkiness
                      
                        //Debug.Log("image: " + picChars);
                        imgDataBytes = Convert.FromBase64String(picChars);
                        */

                        imgDataBytes = Convert.FromBase64String(images[i]);

                        yield return null; //wait a free to lesson the jerkiness
                    }
                }
                else
                {
                    Debug.Log("image data is missing");
                }

                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);

                if (texture.LoadImage(imgDataBytes, false))
                {
                    yield return null; //wait a frame to lesson the jerkiness

                    //debug: write texture out

 #if !RT_RELEASE
                    //byte[] testReturnedTex = texture.EncodeToPNG();
                    //File.WriteAllBytes(Application.dataPath + "/../SavedReturnedTex.png", testReturnedTex);
#endif


                    //Debug.Log("Read texture, setting to new image");
                    this.gameObject.GetComponent<PicMain>().AddImageUndo(true);
                    yield return null; //wait a frame to lesson the jerkiness


                    //we've already done the undo, so now let's update with the info we used to make this
                    m_picScript.GetCurrentStats().m_lastPromptUsed = m_prompt;
                    m_picScript.GetCurrentStats().m_lastNegativePromptUsed = m_negativePrompt;
                    m_picScript.GetCurrentStats().m_lastSteps = steps;
                    m_picScript.GetCurrentStats().m_lastCFGScale = (float)prompt_cfg;
                    m_picScript.GetCurrentStats().m_lastSampler = samplerName;
                    m_picScript.GetCurrentStats().m_tiling = bTiled;
                    m_picScript.GetCurrentStats().m_fixFaces = bFixFace;

                    m_picScript.GetCurrentStats().m_lastSeed = m_seed;
                    m_picScript.GetCurrentStats().m_lastModel = model;
                    m_picScript.GetCurrentStats().m_bUsingControlNet = m_bControlNetWasUsed;
                    m_picScript.GetCurrentStats().m_bUsingPix2Pix = bUsingPixToPix;
                    m_picScript.GetCurrentStats().m_lastOperation = "img2img";
                    m_picScript.GetCurrentStats().m_gpu = m_gpu;
                    m_picScript.GetCurrentStats().m_lastControlNetModel = lastControlNetModel;
                    m_picScript.GetCurrentStats().m_pix2pixCFG = pix2pixCFG;
                    m_picScript.GetCurrentStats().m_lastControlNetModelPreprocessor = lastControlNetPreprocessor;
                    m_picScript.GetCurrentStats().m_lastControlNetWeight = lastControlNetWeight;
                    m_picScript.GetCurrentStats().m_lastControlNetGuidance = lastControlNetGuidance;
                    m_picScript.GetCurrentStats().m_maskContents = maskContents;
                    m_picScript.GetCurrentStats().m_maskBlending = maskBlur;
                    m_picScript.GetCurrentStats().m_lastDenoisingStrength = denoising_strength;
                    m_picScript.SetNeedsToUpdateInfoPanelFlag();
                  
                    int maskFeatheringRevolutions = (int)GameLogic.Get().GetAlphaMaskFeatheringPower();
                    Texture2D finalTexture = null;

                    //simple way, no feathered alpha masking
                    finalTexture = texture;
                  
                    m_picScript.SetStatusMessage("Processing...");

                    //now copy block over the real image
                    picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), finalTexture, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
                    yield return null; //wait a frame to lesson the jerkiness
                    picSprite.texture.Apply();
                    m_picScript.AutoSaveImageIfNeeded();

                    Destroy(finalTexture);
                    if (m_onFinishedRenderingCallback != null)
                        m_onFinishedRenderingCallback.Invoke(gameObject);
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
