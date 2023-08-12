using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using SimpleJSON;
using System.Security.Policy;

public class PicGenerateMask : MonoBehaviour
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
            m_picScript.SetStatusMessage(elapsed.ToString("DIS Masking: 0.0#"));
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

    public void OnGenerateMask(int gpu, bool disableTranslucency)
    {
        //Debug.Log("Starting upscale...");
        m_gpu = gpu;
  
        StartWebRequest(true, disableTranslucency);
    }
    public void StartWebRequest(bool bFromButton, bool disableTranslucency)
    {
        var gpuInfo = Config.Get().GetGPUInfo(m_gpu);

        if (!bFromButton)
        {
  
        }

        if (Config.Get().IsGPUBusy(m_gpu))
        {
            Debug.LogError("Why is GPU busy?!");
            return;
        }

        Config.Get().SetGPUBusy(m_gpu, true);
        m_bIsGenerating = true;
        startTime = Time.realtimeSinceStartup;

        StartCoroutine(GetRequest(disableTranslucency));
    }

    IEnumerator GetRequest(bool disableTranslucency)
    {
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);

        SpriteRenderer renderer = m_sprite.GetComponent<SpriteRenderer>();
        UnityEngine.Sprite s = renderer.sprite;
        byte[] picPng = s.texture.EncodeToPNG();
        // For testing purposes, also write to a file in the project folder
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);
        string finalURL = gpuInf.remoteURL + "/sdapi/v1/img2img";

        string imgBase64 = Convert.ToBase64String(picPng);
        yield return null; //wait a free to lesson the jerkiness

   

        string json =
$@"{{
           
              ""init_images"":
            [
                ""{imgBase64}""
            ], 
            ""prompt"": ""unused"",
            ""denoising_strength"": 0,
            ""alpha_mask_subject"": true,
            ""alpha_mask_subject_force_no_translucency"":{disableTranslucency.ToString().ToLower()},
            ""width"": {s.texture.width},
            ""height"": {s.texture.height}
         
        }}";

        //        ""alpha_mask_subject_force_no_translucency"": true

        RTConsole.Log("Generating mask with " + finalURL + " local GPU ID " + m_gpu);

        //File.WriteAllText("json_to_send.json", json); //for debugging
        using (var postRequest = UnityWebRequest.PostWwwForm(finalURL, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
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

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a free to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);
                //Debug.Log("images is of type " + images.Tag);
                //Debug.Log("there are " + images.Count + " images");

                byte[] imgDataBytes = null;

                var images = rootNode["images"];
                Debug.Assert(images.Count == 1); //You better convert the extra images to new pics!

            
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
                }

                //ok, we now have the returned texture.  But wastefully, we actually
                //only care about the alpha channel.  So let's grab that and convert
                //it to our mask
                Texture2D alphaTex = null;
                bool bAlphaWasUsed = false;

                alphaTex = texture.GetAlphaMask(out bAlphaWasUsed);
                alphaTex.Apply();

                m_picScript.GetMaskScript().SetMaskFromTextureAlpha(alphaTex);
                m_picScript.GetMaskScript().SetMaskVisible(true);

                Destroy(texture); //done with this


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
