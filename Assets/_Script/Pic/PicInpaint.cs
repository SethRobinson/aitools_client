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
                m_picScript.SetStatusMessage(elapsed.ToString("Inpainting: 0.0#"));
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
        
      
        if (!m_useExisting || m_prompt == "")
            m_prompt = GameLogic.Get().GetPrompt();

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
        if (GameLogic.Get().GetNoiseStrength() > 0)
        {
            pic512.SetPixelsFromTextureWithAlphaMask(m_latentNoise, mask512, GameLogic.Get().GetNoiseStrength());
            yield return null; //wait a frame to lessen the jerkiness

            // picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), pic512, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
            // picSprite.texture.Apply();
            //byte[] noisedPic = pic512.EncodeToPNG();
            //File.WriteAllBytes(Application.dataPath + "/../SavedScreenWithNoise.png", noisedPic);
        }
        */

        byte[] picPng = pic512.EncodeToPNG();
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

            string maskedContentIndex = "1";

            if (maskedContent == "fill") maskedContentIndex = "0";
            if (maskedContent == "original") maskedContentIndex = "1";
            if (maskedContent == "latent noise") maskedContentIndex = "2";
            if (maskedContent == "latent nothing") maskedContentIndex = "3";

            json =
         $@"{{
            ""init_images"":
            [
                ""{imgBase64}""
            ], 
            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(m_prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(m_negativePrompt)}"",
            ""steps"": {GameLogic.Get().GetSteps()},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{GameLogic.Get().GetTextStrength()},
            ""seed"": {m_seed},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{GameLogic.Get().GetSamplerName()}"",
            ""mask"": ""{maskBase64}"",
            ""denoising_strength"": {GameLogic.Get().GetInpaintStrength()},
            ""mask_blur"": {maskBlur},
            ""inpainting_fill"": {maskedContentIndex},
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()}
      
        }}";

       Debug.Log("Inpainting with " + finalURL + " local GPU ID " + m_gpu);


#if !RT_RELEASE
        //        File.WriteAllText("json_to_send.json", json);
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

                Debug.Assert(images.Count == 1); //You better convert the extra images to new pics!

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i = 0; i < images.Count; i++)
                    {

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
                    byte[] testReturnedTex = texture.EncodeToPNG();
                    File.WriteAllBytes(Application.dataPath + "/../SavedReturnedTex.png", testReturnedTex);
#endif


                    //Debug.Log("Read texture, setting to new image");
                    this.gameObject.GetComponent<PicMain>().AddImageUndo(true);
                    yield return null; //wait a frame to lesson the jerkiness

                    int maskFeatheringRevolutions = (int)GameLogic.Get().GetAlphaMaskFeatheringPower();
                    Texture2D finalTexture = null;

                    //simple way, no feathered alpha masking
                    finalTexture = texture;
                  
                    m_picScript.SetStatusMessage("Processing...");

                    //now copy block over the real image
                    picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), finalTexture, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
                    yield return null; //wait a frame to lesson the jerkiness
                    picSprite.texture.Apply();

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
