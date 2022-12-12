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

    public string BuildJSonRequestForInpaint(string prompt, string negativePrompt, Texture2D pic512, Texture2D mask512, bool bRemoveBackground)
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

        string maskJson = "";

            if (picMaskPng != null)
            {
                maskJson = $@" ""mask"": ""{maskBase64}"",";
            }

            json =
    $@"{{
             {maskJson}
            ""init_images"":
            [
                ""{imgBase64}""
            ],
            {safety_filter}
            ""prompt"": ""{SimpleJSON.JSONNode.Escape(prompt)}"",
            ""negative_prompt"": ""{SimpleJSON.JSONNode.Escape(negativePrompt)}"",
            ""steps"": {GameLogic.Get().GetSteps()},
            ""restore_faces"":{bFixFace.ToString().ToLower()},
            ""tiling"":{bTiled.ToString().ToLower()},
            ""cfg_scale"":{GameLogic.Get().GetTextStrength()},
            ""seed"": {GameLogic.Get().GetSeed()},
            ""width"": {genWidth},
            ""height"": {genHeight},
            ""sampler_name"": ""{GameLogic.Get().GetSamplerName()}"",
            
            ""denoising_strength"": {GameLogic.Get().GetInpaintStrength()},
            ""mask_blur"": {maskBlur},
            ""inpainting_fill"": {maskedContentIndex},
            ""alpha_mask_subject"":{bRemoveBackground.ToString().ToLower()}
        }}";
        
        return json;
    }

    public bool SpawnInpaintRequest(string jsonRequest, Action<Texture2D, RTDB> myCallback, RTDB db)
    {
       
        Debug.Assert(Config.Get().GetFreeGPU() != -1);
        StartCoroutine(GetRequest(jsonRequest, myCallback, db));
        return true;
    }

    IEnumerator GetRequest(string json, Action<Texture2D, RTDB> myCallback, RTDB db)
    {

#if !RT_RELEASE
        //        File.WriteAllText("json_to_send.json", json);
#endif

        int gpu = Config.Get().GetFreeGPU();
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

                Debug.Assert(images.Count == 1); //You better convert the extra images to new pics!

                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i = 0; i < images.Count; i++)
                    {
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
