using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using SimpleJSON;

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
        string url = gpuInfo.remoteURL+"/api/predict";

        
        StartCoroutine(GetRequest(m_prompt, m_prompt_strength,url));

    }
    public void StartGenerate()
    {
        StartWebRequest(true);
    }
    IEnumerator GetRequest(String context, double prompt_strength, string url)
    {
        WWWForm form = new WWWForm();

        //Create the request using a static method instead of a constructor

        //String finalURL = url + "?prompt=" + context + "&prompt_strength=" + prompt_strength;
        String finalURL = url;
       Debug.Log("Generating text to image with " + finalURL + " local GPU ID " + m_gpu);

        bool bFixFace = GameLogic.Get().GetFixFaces();
        bool bTiled = GameLogic.Get().GetTiling();

        int genWidth = GameLogic.Get().GetGenWidth();
        int genHeight = GameLogic.Get().GetGenHeight();
        var gpuInf = Config.Get().GetGPUInfo(m_gpu);

        string json = "{\"fn_index\":"+ gpuInf.fn_indexDict["text2image"]+",\"data\":[\"" + GameLogic.Get().GetPrompt() +
          "\",\""+GameLogic.Get().GetNegativePrompt()+ "\",\"None\",\"None\"," + GameLogic.Get().GetSteps() +
          ",\""+GameLogic.Get().GetSamplerName()+"\"," + bFixFace.ToString().ToLower() + "," + bTiled.ToString().ToLower() + ",1,1," + GameLogic.Get().GetTextStrength() + ","
          + m_seed + ",-1,0,0,0," + genHeight + "," + genWidth + ",\"" +
          "None\", null],\"session_hash\":\"craphash\"}";
     
        string thingToUse = json;

        if (Config.Get().GetGPUInfo(m_gpu).bUseHack)
        {
           // thingToUse = jsonPi39;
        }
        using (var postRequest = UnityWebRequest.Post(finalURL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(thingToUse);
            
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);

            postRequest.SetRequestHeader("Content-Type", "application/json");

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
                Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes); // + postRequest.downloadHandler.text

                //Ok, we now have to dig into the response and pull out the json image

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                /*
                foreach (KeyValuePair<string, JSONNode> kvp in (JSONObject)rootNode)
                {
                    Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
                }
                */

                var dataNode = rootNode["data"];
                Debug.Assert(dataNode.Tag == JSONNodeType.Array);

                var images = dataNode[0];
                //Debug.Log("images is of type " + images.Tag);
                //Debug.Log("there are " + images.Count + " images");

                if (images.Count != 1)
                {
                    Debug.LogError("PicTextToImage: Something wrong, no image received in json reply. (Breaking change on server?)");
                }
                
                byte[] imgDataBytes = null;

                if (images != null)
                {
                    for (int i=0; i<images.Count; i++)
                    {
                        //convert each to be a pic object?
                        string temp = images[i].ToString();

                        //First get rid of the "data:image/png;base64," part


                        string picChars = images[i].ToString().Substring(images[i].ToString().IndexOf(",")+1);
                        //this is dumb, why is there a single " at the end?  Is there a better way to get rid of it? //OPTIMIZE
                        picChars = picChars.Substring(0, picChars.LastIndexOf('"'));
                        //Debug.Log("image: " + picChars);
                        imgDataBytes = Convert.FromBase64String(picChars);
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
               
                if (texture.LoadImage(imgDataBytes, false))
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

                    if (processScript && GameLogic.Get().GetUpscale() > 1.0f)
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
