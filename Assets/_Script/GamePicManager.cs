using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Policy;
using UnityEngine;
using UnityEngine.Networking;

//an optimized way to send/receive a stable diffusion request to a server, only uses the features needed for say, a game, so it much quicker to process.  Also allows the json
//to be preprocessed and stored for future calls

public class GamePicManager : MonoBehaviour
{
    static GamePicManager _this = null;

    private void Awake()
    {
        _this = this;
    }
    static public GamePicManager Get()
    {
        return _this;
    }
   
    public static string GetName()
    {
        return Get().name;
    }

    public string BuildJSonRequestForInpaint(string prompt, string negativePrompt, Texture2D pic512, Texture2D mask512, bool bRemoveBackground, 
        bool bOperateOnSubjectOnly = false, bool bDisableTranslucencyOfMask = false, bool bReverseSubjectMask = false, bool bUseControlNet = false)
    {
        byte[] picPng = pic512.EncodeToPNG();
        byte[] picMaskPng = null;

        if (mask512)
        {
            picMaskPng = mask512.EncodeToPNG();
        }

#if !RT_RELEASE
        //For testing purposes, we can write out what we're going to send
   //     File.WriteAllBytes(Application.dataPath + "/../GameGenTemplate.png", picPng);
   //     File.WriteAllBytes(Application.dataPath + "/../GameGenTemplateMask.png", picMaskPng);
#endif

        string imgBase64 = Convert.ToBase64String(picPng);
        string maskBase64 = "";
        if (picMaskPng != null)
        {
            maskBase64 = Convert.ToBase64String(picMaskPng);
        }

        string maskedContent = GameLogic.Get().GetMaskContent();
        int maskBlur = (int)GameLogic.Get().GetAlphaMaskFeatheringPower(); //0 to 64

        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();

        var genHeight = pic512.height;
        var genWidth = pic512.width;

        string safety_filter = ""; //use whatever the server is set at
        if (Config.Get().GetSafetyFilter())
        {
            safety_filter = $@"""override_settings"": {{""filter_nsfw"": true}},";
        }

            string json;
            string maskedContentIndex = "1";

        if (maskedContent == "fill") maskedContentIndex = "0";
        if (maskedContent == "original") maskedContentIndex = "1";
        if (maskedContent == "latent noise") maskedContentIndex = "2";
        if (maskedContent == "latent nothing") maskedContentIndex = "3";


        string subjectMask = "";

        if (bOperateOnSubjectOnly)
        {
            subjectMask = $@" ""generate_subject_mask"":{bOperateOnSubjectOnly.ToString().ToLower()},";


            if (bReverseSubjectMask)
            {
                subjectMask = $@" ""generate_subject_mask_reverse"":{bOperateOnSubjectOnly.ToString().ToLower()},";

            }
        }

        string maskJson = "";

            if (picMaskPng != null)
            {
                maskJson = $@" ""mask"": ""{maskBase64}"",";
            }


        string controlnet_json = "";
     
        if (bUseControlNet)
        {
            //  finalURL = url + "/controlnet/img2img";
        
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


        json =
    $@"{{
             {maskJson}
            ""init_images"":
            [
                ""{imgBase64}""
            ],

            ""alwayson_scripts"": {{
            {controlnet_json}
            }},

            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(negativePrompt)}"",
            ""steps"": {GameLogic.Get().GetSteps()},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{GameLogic.Get().GetTextStrengthString()},
            ""seed"": {GameLogic.Get().GetSeed()},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{GameLogic.Get().GetSamplerName()}"",
            ""denoising_strength"": {GameLogic.Get().GetInpaintStrengthString()},
            ""mask_blur"": {maskBlur},
            ""inpainting_fill"": {maskedContentIndex},
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()},
            {subjectMask}
            ""generate_subject_mask_force_no_translucency"":{bDisableTranslucencyOfMask.ToString().ToLower()}
       }}";
        
        return json;
    }

    public bool SpawnInpaintRequest(string jsonRequest, Action<Texture2D, RTDB> myCallback, RTDB db, int gpuID = -1)
    {
       
        Debug.Assert(Config.Get().GetFreeGPU() != -1);
        StartCoroutine(GetRequest(jsonRequest, myCallback, db, gpuID));
        return true;
    }

    IEnumerator GetRequest(string json, Action<Texture2D, RTDB> myCallback, RTDB db, int gpu = -1)
    {

#if !RT_RELEASE
        //        File.WriteAllText("json_to_send.json", json);
#endif
        if (gpu == -1)
        {
            gpu = Config.Get().GetFreeGPU();
        }

        if (gpu == -1)
        {
            Debug.LogError("No GPU available for inpaint");
            yield return true;
        }

        var gpuInfo = Config.Get().GetGPUInfo(gpu);
        string url;

      
       url = gpuInfo.remoteURL + "/sdapi/v1/img2img";

        Config.Get().SetGPUBusy(gpu, true);

        Debug.Log("Inpainting with " +url + " local GPU ID " + gpu);

        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error + " (" + Config.Get().GetGPUName(gpu) + ")";
                Debug.Log(msg);
                RTQuickMessageManager.Get().ShowMessage(msg);
                Debug.Log(postRequest.downloadHandler.text);
                Config.Get().SetGPUBusy(gpu, false);
            }
            else
            {
                //Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                if (Config.Get().IsValidGPU(gpu))
                {
                    if (!Config.Get().IsGPUBusy(gpu))
                    {
                        Debug.LogError("Why is GPU not busy?! We were using it!");
                    }
                    else
                    {
                        Config.Get().SetGPUBusy(gpu, false);
                    }
                }

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

                Debug.Assert(images.Count > 0);
                int imageCount = images.Count;

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i = 0; i < 1; i++) //we're ignoring the second image, the one from controlnet
                    {
                        imgDataBytes = Convert.FromBase64String(images[i]);
                        yield return null; //wait a free to lesson the jerkiness
                    }
                }
                else
                {
                    Debug.Log("image data is missing");
                }

                Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);

                if (texture.LoadImage(imgDataBytes, false))
                {
                    yield return null; //wait a frame to lesson the jerkiness

                    //debug: write texture out
#if !RT_RELEASE
                   // byte[] testReturnedTex = texture.EncodeToPNG();
                   // File.WriteAllBytes(Application.dataPath + "/../SavedReturnedTex.png", testReturnedTex);
#endif

                    myCallback.Invoke(texture, db);
                }
                else
                {
                    Debug.Log("Error reading texture");
                }
            }
        }
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }
}
