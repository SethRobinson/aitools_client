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
    int m_steps;
    float m_prompt_strength;
    long m_seed = -1;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    float m_noise_strength = 0.78f;
    public SpriteRenderer m_spriteMask;
    public Action<GameObject> m_onFinishedRenderingCallback;
    public PicMain m_picScript;
    public Texture2D m_latentNoise; //a texture we'll use for the noise instead of generating it ourself
    public PicTextToImage m_picTextToImageScript;

    // Start is called before the first frame update
    void Start()
    {
       
    }

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

    public void StartInpaint()
    {
       
        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }

        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        string url =gpuInfo.remoteURL + "/v1/img2img";

        m_seed = GameLogic.Get().GetSeed();
        
        //non-random seeds are actually kind of bad

        /*
        if (m_picTextToImageScript.WasCustomSeedSet())
        {
            m_seed = m_picTextToImageScript.GetSeed();
        }
        */
        
        m_prompt = GameLogic.Get().GetPrompt();
        m_steps = GameLogic.Get().GetSteps();
        m_prompt_strength = GameLogic.Get().GetTextStrength();
        m_noise_strength = GameLogic.Get().GetInpaintStrength();

        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest(m_prompt, m_prompt_strength, url));
    }

    IEnumerator GetRequest(String context, double prompt_strength, string url)
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

        //apply latent noise if needed
        if (GameLogic.Get().GetNoiseStrength() > 0)
        {
            pic512.SetPixelsFromTextureWithAlphaMask(m_latentNoise, mask512, GameLogic.Get().GetNoiseStrength());
            yield return null; //wait a free to lesson the jerkiness

            // picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), pic512, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
            // picSprite.texture.Apply();
            //byte[] noisedPic = pic512.EncodeToPNG();
            //File.WriteAllBytes(Application.dataPath + "/../SavedScreenWithNoise.png", noisedPic);
        }

        byte[] picPng = pic512.EncodeToPNG();
        yield return null; //wait a free to lesson the jerkiness

        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //remove alpha from texture
        //clumisly change a full alpha png to just RGB and replace transparent with black, as that'picSprite how our API wants it
        var newtex = mask512.ConvertTextureToBlackAndWhiteRGBMask();
        yield return null; //wait a free to lesson the jerkiness

        //if we wanted the true full alpha, we'd just use this instead:   byte[] picMaskPng = alphatex.EncodeToPNG();
        byte[] picMaskPng = newtex.EncodeToPNG();
        yield return null; //wait a free to lesson the jerkiness

        //For testing purposes, we could also write to a file in the project folder
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreenMask.png",picMaskPng);

        String finalURL = url;
        Debug.Log("Inpainting with " + finalURL+" local GPU ID "+m_gpu);

        string imgBase64 = Convert.ToBase64String(picPng);
        string maskBase64 = Convert.ToBase64String(picMaskPng);

        string maskedContent = GameLogic.Get().GetMaskContent();
        int maskBlur = (int) GameLogic.Get().GetAlphaMaskFeatheringPower(); //0 to 64

        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();

        var gpuInf = Config.Get().GetGPUInfo(m_gpu);
        var genHeight = m_targetRect.GetHeight();
        var genWidth = m_targetRect.GetWidth();

        string json =
$@"{{
            ""img2imgreq"":
            {{
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(m_prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(GameLogic.Get().GetNegativePrompt())}"",
            ""steps"": {GameLogic.Get().GetSteps()},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{GameLogic.Get().GetTextStrength()},
            ""seed"": {m_seed},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{GameLogic.Get().GetSamplerName()}"",
            ""image"": ""{imgBase64}"",
            ""mask_image"": ""{maskBase64}"",
            ""denoising_strength"": {GameLogic.Get().GetInpaintStrength()},
            ""mask_blur"": {maskBlur},
            ""inpainting_fill"": ""{maskedContent}""

        }}

        }}";


        /*
        string json = "{ + SimpleJSON.JSONNode.Escape(GameLogic.Get().GetPrompt()) + "\",\""+ SimpleJSON.JSONNode.Escape(GameLogic.Get().GetNegativePrompt())
            + "\",\"None\",\"None\",null,{ \"image\":\"data:image/png;base64," + imgBase64 +
            "\",\"mask\":\"data:image/png;base64," + maskBase64 +
            "\"},null,null,\"Draw mask\","+GameLogic.Get().GetSteps() +",\""+GameLogic.Get().GetSamplerName()+"\","+ maskBlur+",\""+ maskedContent+"\","+ bFixFace.ToString().ToLower()+","+bTiled.ToString().ToLower() + 
            ",1,1," + GameLogic.Get().GetTextStrength() +","+GameLogic.Get().GetInpaintStrength()+
            ","+m_seed+",-1,0,0,0,false,"+ height+ ","+ width+",\"Just resize\",false,32,\"Inpaint masked\",\"\",\"\",\"None\",null],\"session_hash\":\"d0v2057qsd\"}";
        */
       
        if (Config.Get().GetGPUInfo(m_gpu).bUseHack)
        {
            //thingToUse = jsonHacked;
        }
#if !RT_RELEASE
//        File.WriteAllText("json_to_send.json", json);
#endif
        using (var postRequest = UnityWebRequest.Post(finalURL, "POST"))
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
              
                Debug.Log("images is of type " + images.Tag);
                Debug.Log("there are " + images.Count + " images");

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
                    yield return null; //wait a free to lesson the jerkiness

                    //debug: write texture out
                    //byte[] testReturnedTex = texture.EncodeToPNG();
                    //File.WriteAllBytes(Application.dataPath + "/../SavedReturnedTex.png", testReturnedTex);


                    //Debug.Log("Read texture, setting to new image");
                    this.gameObject.GetComponent<PicMain>().AddImageUndo(true);
                    yield return null; //wait a free to lesson the jerkiness

                    int maskFeatheringRevolutions = (int)GameLogic.Get().GetAlphaMaskFeatheringPower();
                    Texture2D finalTexture = null;

                    /*
                    if (GameLogic.Get().GetInpaintMaskEnabled())
                    {
                        //actually, we could just set it to this image, but let's only copy the parts our original mask wanted changed, this is Seth's
                        //hack to stop it from slightly changing every damn pixel, which ruins tiling textures
                        Texture2D maskTexture = mask512.Duplicate();
                        finalTexture = pic512.Duplicate();
                        var blurFilter = new ConvFilter.BoxBlurFilter();
                        var processor = new ConvFilter.ConvolutionProcessor(maskTexture);
                        m_picScript.SetStatusMessage("Blending...");
                        for (int i = 0; i < maskFeatheringRevolutions; i++)
                        {
                            yield return StartCoroutine(processor.ComputeWith(blurFilter));
                            processor = new ConvFilter.ConvolutionProcessor(processor.m_originalMap);
                        }

                        //save out a png for testing of the alpha channel
                        // byte[] testPng = processor.m_originalMap.EncodeToPNG();
                        //File.WriteAllBytes(Application.dataPath + "/../SavedBlurredMask.png", testPng);


                        finalTexture.SetPixelsFromTextureWithAlphaMask(texture, processor.m_originalMap, 1.0f, true);
                    } else */
                    {
                        //simple way, no feathered alpha masking
                        finalTexture = texture;
                    }
                    m_picScript.SetStatusMessage("Processing...");

                    //now copy block over the real image
                    picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), finalTexture, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
                    yield return null; //wait a free to lesson the jerkiness

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
