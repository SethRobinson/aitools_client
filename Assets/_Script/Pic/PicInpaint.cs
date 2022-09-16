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
using System.Net;



public class PicInpaint : MonoBehaviour
{
    public PicTargetRect m_targetRect;
    float startTime;
    string m_prompt;
    int m_steps;
    float m_prompt_strength;
    int m_seed;
    public GameObject m_sprite;
    bool m_bIsGenerating;
    int m_gpu;
    float m_noise_strength = 0.78f;
    public SpriteRenderer m_spriteMask;
    public Action<GameObject> m_onFinishedRenderingCallback;
    public PicMain m_picScript;
    public Texture2D m_latentNoise; //a texture we'll use for the noise instead of generating it ourself

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

        string url =gpuInfo.remoteURL + "/inpaint";
       
        m_seed = UnityEngine.Random.Range(1, 20000000);
        m_prompt = GameLogic.Get().GetPrompt();
        m_steps = GameLogic.Get().GetSteps();
        m_prompt_strength = GameLogic.Get().GetTextStrength();
        m_noise_strength = GameLogic.Get().GetInpaintStrength();

        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest(m_prompt, m_prompt_strength, url, m_steps, m_seed));
    }

    IEnumerator GetRequest(String context, double prompt_strength, string url, int steps, int seed)
    {
        //yield return new WaitForEndOfFrame();

        SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
        UnityEngine.Sprite picSprite = renderer.sprite;

        Texture2D pic512 = new Texture2D(m_targetRect.GetWidth(), m_targetRect.GetHeight(), TextureFormat.RGBA32, false);
        Texture2D mask512 = new Texture2D(m_targetRect.GetWidth(), m_targetRect.GetHeight(), TextureFormat.RGBA32, false);

        pic512.Blit(0, 0, picSprite.texture, m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), m_targetRect.GetWidth(), m_targetRect.GetHeight());
        mask512.Blit(0, 0, m_spriteMask.sprite.texture, m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), m_targetRect.GetWidth(), m_targetRect.GetHeight());

        
        //apply latent noise if needed
        if (GameLogic.Get().GetNoiseStrength() > 0)
        {
            pic512.SetPixelsFromTextureWithAlphaMask(m_latentNoise, mask512, GameLogic.Get().GetNoiseStrength());

           // picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), pic512, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
           // picSprite.texture.Apply();
            //byte[] noisedPic = pic512.EncodeToPNG();
            //File.WriteAllBytes(Application.dataPath + "/../SavedScreenWithNoise.png", noisedPic);
        }

        byte[] picPng = pic512.EncodeToPNG();

        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);


        //remove alpha from texture
        //clumisly change a full alpha png to just RGB and replace transparent with black, as that'picSprite how our API wants it
        var newtex = mask512.ConvertTextureToBlackAndWhiteRGBMask();

        //if we wanted the true full alpha, we'd just use this instead:   byte[] picMaskPng = alphatex.EncodeToPNG();
        byte[] picMaskPng = newtex.EncodeToPNG();

        //For testing purposes, we could also write to a file in the project folder
       //File.WriteAllBytes(Application.dataPath + "/../SavedScreenMask.png",picMaskPng);

        WWWForm form = new WWWForm();
        //Create the request using a static method instead of a constructor

        //String finalURL = url + "?prompt=" + context + "&prompt_strength=" + prompt_strength;
        String finalURL = url;
        Debug.Log("Inpainting with " + finalURL+" local GPU ID "+m_gpu);
        form.AddField("prompt", context);
        form.AddField("prompt_strength", prompt_strength.ToString());
        form.AddField("gpu", Config.Get().GetGPUInfo(m_gpu).remoteGPUID.ToString());
        form.AddField("seed", seed.ToString());
        form.AddField("steps", steps.ToString());
        form.AddField("noise_strength", m_noise_strength.ToString());
        form.AddField("safetyfilter", Config.Get().GetSafetyFilter().ToString());

        form.AddBinaryData("pic", picPng);
        form.AddBinaryData("mask", picMaskPng);
        //hack to use a png from disk as mask for testing
        //var maskPng = File.ReadAllBytes("Assets/Resources/mask.png");
        //form.AddBinaryData("mask", maskPng);

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

                    //debug: write texture out
                    //byte[] testReturnedText = texture.EncodeToPNG();
                    //File.WriteAllBytes(Application.dataPath + "/../SavedReturnedTex.png", testReturnedText);


                    //Debug.Log("Read texture, setting to new image");
                    this.gameObject.GetComponent<PicMain>().AddImageUndo(true);
                    int maskFeatheringRevolutions = (int)GameLogic.Get().GetAlphaMaskFeatheringPower();
                    Texture2D finalTexture = null;

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
                    } else
                    {
                        //simple way, no feathered alpha masking
                        finalTexture = texture;
                    }
                    m_picScript.SetStatusMessage("Processing...");

                    //now copy block over the real image
                    picSprite.texture.Blit(m_targetRect.GetOffsetX(), m_targetRect.GetOffsetY(), finalTexture, 0, 0, m_targetRect.GetWidth(), m_targetRect.GetHeight());
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