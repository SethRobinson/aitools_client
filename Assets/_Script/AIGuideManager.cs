using System.Collections.Generic;
using UnityEngine;
using static OpenAITextCompletionManager;
using SimpleJSON;
using TMPro;
using UnityEngine.UI;
using System.Security.Policy;
using UnityEngine.Rendering;
using System.IO;

public class AIGuideManager : MonoBehaviour
{

    public class AIGuidePreset
    {
        public string m_presetName = "error";
        public string m_presetDescription = "It's a preset you can choose.";
        public string m_llm_prompt;
        public string m_prompt;
        public string m_negativePrompt;
        public string m_textToPrependToGeneration = "";

        public string m_example_llm_output; //so those without GPT4 etc can see how it works anyway
        public bool m_PixelArt128Checkbox;
        public bool m_AddBordersCheckbox;
        public bool m_OverlayTextCheckbox;
        public bool m_BoldCheckbox;
        public int m_maxTokens = 6000; //a default that is good for gpt4

        public Color m_textColor = new Color(0.9f, 0.9f, 0.9f, 1.0f);
        public Color m_borderColor = new Color(0, 0, 0, 1.0f);
        public float m_fontSizeMod = 1.0f;
        public bool m_prependPrompt;
        public LLM_Type m_llmToUse = LLM_Type.GPT4;
  
    }

    public enum LLM_Type
    {
            GPT4,
          TexGen
    }
    public enum Renderer_Type
    {
       SDWebGUI,
        Dalle3
    }


    //Create a public struct to hold a few strings and rects
    public struct PassedInfo
    {
        public string m_text;
        public Rect m_textAreaRect;
        public Color m_textColor;
        public Color m_borderColor;
        public float m_fontSizeMod;
        public string m_title;
        public bool m_BoldCheckbox;
        public bool m_overlayTextCheckBox;
        public int m_titleFontID;
        public int m_textFontID;
        public string m_forcedFilename;
        public string m_forcedFilenameJPG;
      
    }

    public enum ResponseTextType
    {
        text,
        json
    }

    ResponseTextType m_response_text_type;
    static AIGuideManager _this;
    public CanvasGroup _canvasGroup;
    float _aiTemperature = 1.0f; //higher is more creative and chaotic

    public TMPro.TMP_InputField _inputPrompt;
    public TMPro.TMP_InputField _inputPromptOutput;
    public OpenAITextCompletionManager _openAITextCompletionManager;
    public TexGenWebUITextCompletionManager _texGenWebUICompletionManager;
    public GPTPromptManager _promptManager;
    List<AIGuidePreset> m_presets;
    public TMP_Dropdown m_presetDropdown;
    public TMP_InputField m_input_FontSize;
    public TMP_InputField m_input_max_tokens;
    
    //add public handle to a checkbox control
    public Toggle m_PixelArt128Checkbox;
    public Toggle m_AddBordersCheckbox;
    public Toggle m_OverlayTextCheckbox;
    public Toggle m_autoSaveResponsesCheckbox;
    public Toggle m_BoldCheckbox;
    string m_statusStopMessage = "";
    public TMP_Dropdown m_llmSelectionDropdown;
    public TMP_Dropdown m_rendererSelectionDropdown;

    public TMP_Dropdown m_textFontDropdown;
    public TMP_Dropdown m_titleFontDropdown;
    int m_max_tokens;
    public TMPro.TextMeshProUGUI m_statusText;
    string m_lastStatusMessage;
    bool m_ShowStatusAnim = false;
    float m_statusTimerIntervalInSeconds = 0.5f;
    float m_statusAnimTimer = 0;
    string m_animPeriods = "";
    float m_timeThatAnimStarted = 0;
    public Image m_imageBorderColor;
    public Image m_imageTextColor;
    public string m_prompt_used_on_last_send;
    public TMP_InputField m_textToPrependToGeneration;
    public Toggle m_prependPromptCheckbox;
    public Toggle m_autoModeCheckbox;
    public Toggle m_autoSave;
    public Toggle m_autoSaveJPG;

    bool m_bRenderASAP = false;
    string m_imageFileNameBase;

    //make an array of fonts that we can edit inside the editor properties
    public TMP_FontAsset[] m_fontArray;

    void Awake()
    {
        _this = this;
    }

   public void SetStatus(string msg, bool bShowAnim)
    {
        m_statusText.text = msg;
        m_lastStatusMessage = msg;
        m_ShowStatusAnim = bShowAnim;
        m_statusAnimTimer = Time.time + m_statusTimerIntervalInSeconds;
        m_timeThatAnimStarted = Time.time;
    }

    public void OnLLMStartButton()
    {
        //RTConsole.Log("Contacting LLM...");
        //set m_max_tokens from m_input_max_tokens (and not crash if the input is blank or bad)
        if (m_input_max_tokens.text == "")
        {
            RTConsole.Log("Error:  You need to set the max tokens to something");
            GameLogic.Get().ShowConsole(true);
            return;
        }
        else
        {
            m_max_tokens = int.Parse(m_input_max_tokens.text);
        }
        m_prompt_used_on_last_send = _inputPrompt.text; //might be useful later

        if (m_llmSelectionDropdown.value == (int) LLM_Type.GPT4)
        {

            if (Config.Get().GetOpenAI_APIKey().Length < 15)
            {
                RTConsole.Log("Error:  You need to set the GPT4 API key in your config.txt! (example: set_openai_gpt4_key|<key goes here>| ) ");
                //open the console
                GameLogic.Get().ShowConsole(true);
                return;
            }
            _promptManager.SetBaseSystemPrompt(_inputPrompt.text);

            Queue<GTPChatLine> lines = _promptManager.BuildPrompt();
            
            string json = _openAITextCompletionManager.BuildChatCompleteJSON(lines, m_max_tokens, _aiTemperature, Config.Get().GetOpenAI_APIModel());
            RTDB db = new RTDB();
            RTConsole.Log("Contacting GPT4 at " + Config.Get()._openai_gpt4_endpoint+" with " + json.Length + " bytes...");
            _openAITextCompletionManager.SpawnChatCompleteRequest(json, OnGTP4CompletedCallback, db, Config.Get().GetOpenAI_APIKey(), Config.Get()._openai_gpt4_endpoint);
        } else
        {
            Debug.Log("Contacting TexGen WebUI at " + Config.Get()._texgen_webui_address); ;
            string json = _texGenWebUICompletionManager.BuildChatCompleteJSON(_inputPrompt.text, m_max_tokens, _aiTemperature);
            RTDB db = new RTDB();
            _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, Config.Get()._texgen_webui_address);
        }

        RTQuickMessageManager.Get().ShowMessage("Contacting LLM... this can take a while", 5);
        SetStatus("Sent request to LLM, waiting for reply", true);
   }

    public void AddPreset(AIGuidePreset preset)
    {
        m_presets.Add(preset);

        var options = new List<TMP_Dropdown.OptionData>();
        var option = new TMP_Dropdown.OptionData();
        option.text = preset.m_presetName;
        options.Add(option);
        m_presetDropdown.AddOptions(options);
    }

    public System.Collections.IEnumerator AddPicture(string text, string imagePrompt, string title, int idx)
    {

        //Debug.Log("Text: " + text);
        //Debug.Log("Image Prompt: " + imagePrompt);

        //generate a pic
        GameObject pic = ImageGenerator.Get().AddImageByFileName("");

        //Run OnRenderButton in PicMain, which is a component in pic
        PicMain picMain = pic.GetComponent<PicMain>();
        picMain.ClearRenderingCallbacks();

        PicTextToImage textToImage = pic.GetComponent<PicTextToImage>();
        textToImage.SetPrompt(GameLogic.Get().GetPrompt() + " " + imagePrompt);
        textToImage.SetNegativePrompt(GameLogic.Get().GetNegativePrompt());


        if (m_rendererSelectionDropdown.value == (int)Renderer_Type.Dalle3)
        {
            Debug.Log("Dalle detected");
            picMain.OnRenderWithDalle3();
        }
        else
        {


            picMain.OnRenderButton(imagePrompt);
     
        }
            picMain.SetStatusMessage("AI guided\n(waiting)");
        yield return null;
       
        
        
        
        
        if (m_PixelArt128Checkbox.isOn)
        {
            pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnProcessPixelArt128;
            yield return null;
        }

        if (m_autoSave.isOn || m_autoSaveJPG.isOn)
        {

            //make a string with idx padded to 3 digits
            string idxString = idx.ToString();
            while (idxString.Length < 3)
            {
                idxString = "0" + idxString;
            }

            if (m_autoSave.isOn)
                picMain.m_aiPassedInfo.m_forcedFilename = m_imageFileNameBase + idxString + ".png";

            if (m_autoSaveJPG.isOn)
                picMain.m_aiPassedInfo.m_forcedFilenameJPG = m_imageFileNameBase + idxString + ".jpg";


        }

        //see if m_AddBordersCheckbox has been checked by the user
        if (m_AddBordersCheckbox.isOn)
        {
            pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnAddMotivationBorder;
            //hack due to timing issues caused by using coroutines, the above will call OnAddMotivationtextWithTitle and OnSaveIfNeeded itself
    
            yield return null;
        } else
        {
            if (m_OverlayTextCheckbox.isOn)
            {
                pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnAddMotivationTextWithTitle;
            }

           pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnSaveIfNeeded;
            
        }

        if (m_PixelArt128Checkbox.isOn)
        {
            //This doesn't change the image, but makes it look more blocky at the end of the process - but it makes the text look sort of bad so maybe I won't do this.
            //pic.GetComponent<PicTextToImage>().m_onFinishedRenderingCallback += OnTurnOffSmoothing;
        }

        picMain.m_aiPassedInfo.m_text = text;
        picMain.m_aiPassedInfo.m_textColor = m_imageTextColor.color;
        picMain.m_aiPassedInfo.m_borderColor = m_imageBorderColor.color;

        picMain.m_aiPassedInfo.m_title = title; //may or not be used
        picMain.m_aiPassedInfo.m_BoldCheckbox = m_BoldCheckbox.isOn;
        //set font size from m_input_FontSize.text, need to convert it to a float and handle if the input is bad without crashing
        picMain.m_aiPassedInfo.m_fontSizeMod = float.TryParse(m_input_FontSize.text, out float result) ? result : 1.0f;
        picMain.m_aiPassedInfo.m_overlayTextCheckBox = m_OverlayTextCheckbox.isOn;
        picMain.m_aiPassedInfo.m_textFontID = m_textFontDropdown.value;
        picMain.m_aiPassedInfo.m_titleFontID = m_titleFontDropdown.value;
    
    }

    public System.Collections.IEnumerator ProcessJSONFile()
    {
        JSONArray jsonArray = null;

        try
        {
            jsonArray = JSON.Parse(_inputPromptOutput.text) as JSONArray;

            if (jsonArray == null)
            {
                throw new System.Exception("Error parsing json");
            }
        }
        catch (System.Exception e)
        {
            string errorMsg = e.Message;
            RTQuickMessageManager.Get().ShowMessage(errorMsg);
            Debug.Log(errorMsg);
            yield break;
        }
        int idx = 0;
        foreach (JSONNode jsonNode in jsonArray)
        {
            // Normalizing keys to lowercase
            JSONNode normalizedNode = new JSONObject();
            foreach (var key in jsonNode.Keys)
            {
                normalizedNode[key.ToLower()] = jsonNode[key];
            }

            // Check to see if normalizedNode["text"] exists
            if (normalizedNode["text"] == null)
            {
                string errorMsg = "Error parsing json, missing 'text' field";
                RTQuickMessageManager.Get().ShowMessage(errorMsg);
                Debug.Log(errorMsg);
                yield break;
            }

            if (normalizedNode["image_prompt"] == null)
            {
                string errorMsg = "Error parsing json, missing 'image_prompt' field";
                RTQuickMessageManager.Get().ShowMessage(errorMsg);
                Debug.Log(errorMsg);
                yield break;
            }

            string title = normalizedNode["title"] ?? "";
            string text = normalizedNode["text"];
            string imagePrompt = normalizedNode["image_prompt"];

            StartCoroutine(AddPicture(text, imagePrompt, title,idx++));
            yield return null;
        }
    }

    public System.Collections.IEnumerator ProcessTextFileBasic()
    {
        // Iterate through each line of _inputPromptOutput.text, also removing whitespace
        string[] lines = (_inputPromptOutput.text).Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        string text = "";
        string imagePrompt = ""; // Initialize imagePrompt to avoid an uninitialized variable error

        int idx = 0;
        foreach (string line in lines)
        {
            // Separate the string by the : character
            string[] parts = line.Split(':');
            if (parts.Length < 2) continue; // Check to make sure there are at least two parts

            string key = parts[0].Trim().ToLower(); // Convert the key to lowercase and remove any whitespace
            string value = parts[1].Trim(); // Trim any whitespace from the value

            if (key == "text")
            {
                text = value;
            }
            else if (key == "image_prompt")
            {
                imagePrompt = value;
                StartCoroutine(AddPicture(text, imagePrompt, "", idx++));
                yield return null;
                text = "";
                imagePrompt = "";
            }
        }
    }

    //This is called by the ProcessReponse button from the GUI
    public void OnProcessOutput()
    {

        if (m_autoSave.isOn || m_autoSaveJPG.isOn)
        {
            string folderName = "aiguided_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            //create the directory
            Directory.CreateDirectory(Config.Get().GetBaseFileDir("/autosave/") + folderName);
           
            m_imageFileNameBase = Config.Get().GetBaseFileDir("/autosave/") + folderName + "/set_"+Random.Range(0, 9999).ToString()+"_";

            //move our /web/index.php file into this folder, that way if you just copy this folder to a website it has a built in viewer
            string sourceFile = Config.Get().GetBaseFileDir("/web/index.php");
            string destFile = Config.Get().GetBaseFileDir("/autosave/") + folderName + "/index.php";
            File.Copy(sourceFile, destFile, true);
        }

        //the user might have cut and pasted text in, so let's figure it out again //OPTIMIZE;  We could just detect the paste...
        if (FigureOutResponseTextType(_inputPromptOutput.text) == ResponseTextType.text)
        {

            //processing as text, no fancy json being used
            StartCoroutine(ProcessTextFileBasic());
        }
        else
        {
            //using json, gp4 is good at this
            StartCoroutine(ProcessJSONFile());
        }

    }

    public void OnTurnOffSmoothing(GameObject entity)
    {
        PicMain picMain = entity.GetComponent<PicMain>();
        picMain.m_pic.sprite.texture.filterMode = FilterMode.Point;
    }

    public void OnSaveIfNeeded(GameObject entity)
    {
        PicMain picMain = entity.GetComponent<PicMain>();
        if (picMain.m_aiPassedInfo.m_forcedFilename != null && picMain.m_aiPassedInfo.m_forcedFilename.Length > 0)
        {
                picMain.SaveFile(picMain.m_aiPassedInfo.m_forcedFilename, "", null, "", true);
        }

        if (picMain.m_aiPassedInfo.m_forcedFilenameJPG != null && picMain.m_aiPassedInfo.m_forcedFilenameJPG.Length > 0)
        {
            picMain.SaveFileJPG(picMain.m_aiPassedInfo.m_forcedFilenameJPG, "", null, "", Config.Get().GetJPGSaveQuality() );
        }
    }

    public void OnProcessPixelArt128(GameObject entity)
    {
        RTConsole.Log("Fixing up pixel art");
        PicMain picMain = entity.GetComponent<PicMain>();
        picMain.CleanupPixelArt();
    }

    public System.Collections.IEnumerator OnAddMotivationBorderNumor(GameObject entity)
    {

        PicMain picMain = entity.GetComponent<PicMain>();
        int bottomBorderY = (int)((float)picMain.m_pic.sprite.texture.width * 0.35f);

        yield return StartCoroutine(picMain.AddBorder((int)((float)picMain.m_pic.sprite.texture.width * 0.15f),
            (int)((float)picMain.m_pic.sprite.texture.width * 0.15f),
            (int)((float)picMain.m_pic.sprite.texture.width * 0.15f),
            bottomBorderY,
             picMain.m_aiPassedInfo.m_borderColor, false));
        //turn off the mask rect
        picMain.m_picMaskScript.SetMaskVisible(false);
        picMain.m_pic.sprite.texture.Apply();

        //now that we know the size of the border, save it in case we need to write text later
        picMain.m_aiPassedInfo.m_textAreaRect = new Rect(0, 0, picMain.m_pic.sprite.texture.width, bottomBorderY);
        yield return null;
        
        if (picMain.m_aiPassedInfo.m_overlayTextCheckBox)
        {
            OnAddMotivationTextWithTitle(entity);
        }

        OnSaveIfNeeded(entity);
        
        yield return null;
    }
    public void OnAddMotivationBorder(GameObject entity)
    {
        StartCoroutine(OnAddMotivationBorderNumor(entity));
    }

    public Texture2D RenderTextToTexture2D(string text, int width, int height, TMP_FontAsset font, float fontSize, Color color, bool bAutoSize, Vector2 vTextRectSizeMod, FontStyles fontStyles = 0)
    {
        Debug.Log("Creating tex sized " + width + "x" + height);
        // Create GameObject and TextMeshPro components
        GameObject go = new GameObject();
        go.layer = 31; // Use an unused layer
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();

        // Setup TextMeshPro settings
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.anchoredPosition3D = Vector3.zero;
        
        //text wrap size

        tmp.rectTransform.sizeDelta = new Vector2(width*vTextRectSizeMod.x, height * vTextRectSizeMod.y);

        tmp.enableAutoSizing = bAutoSize;
        tmp.fontSizeMax = 9999999;
        //set the font to be bold
        tmp.fontStyle = fontStyles;
        tmp.name = "TextMeshProATemp";
        tmp.enableWordWrapping = true;
        //set largest allowed font size
        // Create a RenderTexture
        RenderTexture renderTexture = new RenderTexture(width, height, 24);

        // Create a new temporary Camera
        GameObject tempCameraObject = new GameObject();
        Camera tempCamera = tempCameraObject.AddComponent<Camera>();

        // Position the camera to capture the text object
        tempCamera.transform.position = Vector3.zero;
        tempCamera.transform.position -= new Vector3(0, 0, 10);  // move back a bit
        tempCamera.clearFlags = CameraClearFlags.Color;
        tempCamera.backgroundColor = Color.clear; // transparent background
        tempCamera.orthographic = true;

        float maxSize = width;
        if (height > maxSize)
        {
            maxSize = height;
        }
        
        float minSize = width;
        if (height < minSize)
        {
            minSize = height;
        }

        tempCamera.orthographicSize = minSize/2;  // set orthographic size
        tempCamera.targetTexture = renderTexture;  // set target texture
        tempCamera.cullingMask = 1 << 31;  // Set camera to only render layer 31
        tempCamera.name = "TextCamera";
        //tempCamera.nearClipPlane = 0;
        //tempCamera.farClipPlane = 100000;
        
        // Wait for the camera to finish rendering
        tempCamera.Render();

        // Create a Texture2D to hold the captured image
        Texture2D tex2D = new Texture2D(width, height, TextureFormat.RGBA32, false);

        // Copy from the RenderTexture to the Texture2D
        RenderTexture.active = renderTexture;
        tex2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex2D.Apply();

        // Deactivate the render texture
        RenderTexture.active = null;

        // Clean up objects.  To debug positions, comment out below so you can see then in the scene
        Destroy(tempCameraObject);
        Destroy(go);

        return tex2D;
    }

    public Color GetARandomBrightColor()
    {
        //return a random bright color
        return new Color(Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f), 1.0f);
    }

    public TMP_FontAsset GetFontByID(int fontID)
    {
        return m_fontArray[fontID];
    }
    public void OnAddMotivationTextWithTitle(GameObject entity)
    {

        PicMain picMain = entity.GetComponent<PicMain>();

        float maxSize = picMain.m_aiPassedInfo.m_textAreaRect.width;
        if (picMain.m_aiPassedInfo.m_textAreaRect.height > maxSize)
        {
            maxSize = picMain.m_aiPassedInfo.m_textAreaRect.height;
        }

        float minSize = picMain.m_aiPassedInfo.m_textAreaRect.width;
        if (picMain.m_aiPassedInfo.m_textAreaRect.height < minSize)
        {
            minSize = picMain.m_aiPassedInfo.m_textAreaRect.height;
        }

        float fontSize = maxSize / 4;
        float titleHeight = 0;

        if (picMain.m_aiPassedInfo.m_title != null && picMain.m_aiPassedInfo.m_title.Length > 0)
        {
            titleHeight = picMain.m_aiPassedInfo.m_textAreaRect.height * 0.34f;
        }

        Texture2D tex;
        Rect rect;
        Vector2 vTextAreaSizeMod;
        
        vTextAreaSizeMod = new Vector2(0.88f, 1.0f);

        TMPro.FontStyles fontStyles = 0;

        if (picMain.m_aiPassedInfo.m_BoldCheckbox)
        { 
            fontStyles = TMPro.FontStyles.Bold;
        }

        tex = RenderTextToTexture2D(picMain.m_aiPassedInfo.m_text, (int)picMain.m_aiPassedInfo.m_textAreaRect.width,
           (int) (picMain.m_aiPassedInfo.m_textAreaRect.height- titleHeight), GetFontByID(picMain.m_aiPassedInfo.m_textFontID), fontSize * picMain.m_aiPassedInfo.m_fontSizeMod, picMain.m_aiPassedInfo.m_textColor,
        false, vTextAreaSizeMod, fontStyles);

        //File.WriteAllBytes("crap.png", tex.EncodeToPNG()); //for debugging

        rect = picMain.m_aiPassedInfo.m_textAreaRect;
        rect.yMax -= titleHeight;
        //add it to our real texture
        picMain.m_pic.sprite.texture.BlitWithAlpha((int)rect.xMin, (int)(picMain.m_pic.sprite.texture.height - rect.height), tex, 0, 0, (int)rect.width, (int)rect.height);
        picMain.m_pic.sprite.texture.Apply();

        if (picMain.m_aiPassedInfo.m_title != null && picMain.m_aiPassedInfo.m_title.Length > 0)
        {
            //now same thing again but for the title
            vTextAreaSizeMod = new Vector2(0.88f, 1.0f);

            tex = RenderTextToTexture2D(picMain.m_aiPassedInfo.m_title, (int)picMain.m_aiPassedInfo.m_textAreaRect.width,
            (int)titleHeight, GetFontByID(picMain.m_aiPassedInfo.m_titleFontID), fontSize, GetARandomBrightColor(), true, vTextAreaSizeMod);

            //File.WriteAllBytes("crap.png", tex.EncodeToPNG()); //for debugging

            rect = picMain.m_aiPassedInfo.m_textAreaRect;
            rect.yMax = titleHeight;
            //add it to our real texture
            picMain.m_pic.sprite.texture.BlitWithAlpha((int)(rect.xMin), (int)(picMain.m_pic.sprite.texture.height - picMain.m_aiPassedInfo.m_textAreaRect.height), tex, 0, 0, (int)rect.width, (int)rect.height);
            picMain.m_pic.sprite.texture.Apply();
        }
    }

    void OnGTP4CompletedCallback(RTDB db, JSONObject jsonNode)
    {
        if (jsonNode == null)
        {
            //must have been an error
            RTConsole.Log("GPT error! Data: " + db.ToString());
            SetStatus("Error sending! Check the log.", false);
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            GameLogic.Get().ShowConsole(true);
            return;
        }

        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */

        string reply = jsonNode["choices"][0]["message"]["content"];
        //Debug.Log(reply);

        _inputPromptOutput.text = reply;
        SetStatus("Success, got reply. "+m_statusStopMessage, false);

        ModifyPromptIfNeeded();
    }

    ResponseTextType FigureOutResponseTextType(string text)
    {
        string temp = _inputPromptOutput.text.TrimStart();
        
        if (temp.Length > 0)
        {
            string firstChar = temp[0].ToString();

            if (firstChar != "[")
            {
                return ResponseTextType.text;
            }


            //must be json
            return ResponseTextType.json;
        } 

        //well, it's blank so uh.. yeah
        return ResponseTextType.text;
    }

    void ModifyPromptIfNeeded()
    {

        _inputPromptOutput.text = m_textToPrependToGeneration.text + _inputPromptOutput.text;


        if (m_prependPromptCheckbox.isOn)
        {
            m_response_text_type = FigureOutResponseTextType(_inputPromptOutput.text);

            if (m_response_text_type == ResponseTextType.text)
            {
                _inputPromptOutput.text = m_prompt_used_on_last_send + _inputPromptOutput.text;
            }
        }



        if (m_autoSaveResponsesCheckbox.isOn || m_autoSaveJPG.isOn)
        {
            //lets save _inputPromptOutput.text to a text file
            string folderName = "aiguided_response_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".txt";
            string path = Config.Get().GetBaseFileDir("/autosave/") + folderName;
            //save it, make sure to overwrite anything already there
            File.WriteAllText(path, _inputPromptOutput.text);
        }


        if (m_autoModeCheckbox.isOn)
        {
            m_bRenderASAP = true;
        }


    }
    void OnTexGenCompletedCallback(RTDB db, JSONObject jsonNode)
    {
        if (jsonNode == null)
        {
            //must have been an error
            RTConsole.Log("GPT error! Data: " + db.ToString());
            SetStatus("Error sending! Check the log.", false);
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            GameLogic.Get().ShowConsole(true);
            return;
        }
   
        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */
      
        string reply = jsonNode["choices"][0]["text"];
        //Debug.Log(reply);

        _inputPromptOutput.text = reply;
        SetStatus("Success, got reply. " + m_statusStopMessage, false);

        //let's add our original prompt to it, I assume that's what we want

        ModifyPromptIfNeeded();

    }

    static public AIGuideManager Get()
{
    return _this;
}

    void PopulateDropdownWithFonts(TMP_Dropdown dropdown, int defaultOption)
    {
        dropdown.ClearOptions();

        List<string> options = new List<string>();

        // Assuming m_fontArray is a collection of TMP_FontAsset
        foreach (TMP_FontAsset font in m_fontArray)
        {
            options.Add(font.name); // Adding the font's name to the options list
        }

        dropdown.AddOptions(options);

        // Set the default option if it's within the valid range
        if (defaultOption >= 0 && defaultOption < options.Count)
        {
            dropdown.value = defaultOption;
        }
    }

    // Start is called before the first frame update
    void Start()
{
        m_statusStopMessage = m_statusText.text;

        PopulateDropdownWithFonts(m_textFontDropdown, 0);
        PopulateDropdownWithFonts(m_titleFontDropdown, 1);

        m_presets = new List<AIGuidePreset>();
        m_presetDropdown.ClearOptions();

        AIGuidePreset preset = new AIGuidePreset();
        preset.m_presetName = "Preset: Use active settings";
        AddPreset(preset); //special case, index 0 won't change anything

        preset = new AIGuidePreset();
        preset.m_presetName = "Slightly Dark Gaming Motivational Posters (GPT4)";
        preset.m_prompt = "award winning, high quality, dramatic lighting";
        preset.m_fontSizeMod = 1.0f;
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_llm_prompt = @"Goal: Generate a raw JSON with quotes for motivational posters.  Please return the JSON text without any additional commentary.

It should be an unnamed array that contains three key value pairs as follows:

""text"" - This text is should be an original, humorous or ironic observation about a specific video game or video game character (both modern and retro). Off kilter, dark and existential is ok, just don't be boring. It's ok to be a little dirty or risque.  Write sophisticated humor aimed at college level comprehension.

""image_prompt"" - this text field textually describes an image in detail that will be the backdrop to the thought above. Please be be verbose and descriptive.

""title"" - This is a single word that will be used to title the deep though created, it will be shown above the text in the final poster layout.

Please create 20 unique entries JSON.  (each one has three values)";

       preset.m_example_llm_output = @"[
  {
    ""text"": ""Mario always said ‘just one more level’ when confronted with an intervention. Temptation runs deep with a love for mushrooms."",
    ""image_prompt"": ""An abstract landscape bathed in the glow of a setting sun, dotted with mushroom-like structures. A shattered castle in the distant horizon. Amorphous blobs, reminiscent of video game foes, populate the foreground."",
    ""title"": ""Temptation""
  },
  {
    ""text"": ""Sonic refused to take the bus, even if it meant running himself ragged. The struggle is real when your speed is your identity."",
    ""image_prompt"": ""An expansive, surreal desert, littered with rusty, abandoned buses. Clusters of golden rings float about, shimmering in the relentless heat. A lone figure, barely visible, speeds through the barren land."",
    ""title"": ""Exertion""
  },
  {
    ""text"": ""Link once had a philosophical crisis - was he the hero or just a puppet to the one holding the controller?"",
    ""image_prompt"": ""An ancient Zelda-esque temple, enveloped in creeping ivy, its stone gargoyles worn by time. In the heavens above, translucent hands manipulate a spectral gaming controller."", 
    ""title"": ""Existence""
  },
  {
    ""text"": ""Bowser's struggle is relatable. We're all holding onto a princess that doesn't belong to us."",
    ""image_prompt"": ""An utterly devastated, fiery landscape, the sky filled with dark smoke. At the center stands a menacing silhouette, clutching a despondent figure in its clutches."",
    ""title"": ""Possession""
  },
  {
    ""text"": ""Pacman's life was a metaphor for mindless consumption until he choked on a power pellet. Beware the very thing that gives you power."",
    ""image_prompt"": ""An endless maze of glowing neon lines against a void-black background. A single, lonely Pacman character relentlessly pursued by phantom figures."",
    ""title"": ""Avarice""
  },
  {
    ""text"": ""Minecraft taught us that the emptiness of existence could be filled with just cubes and creativity."",
    ""image_prompt"": ""A blocky, vibrant world of Minecraft, dense forests and towering mountains under a pixelated sunset. Geometric animals roam peacefully amid pixelated foliage."",
    ""title"": ""Simulacra""
  },
  {
    ""text"": ""Sub-Zero never actually felt cold. He just liked the thrill of freezing things solid."",
    ""image_prompt"": ""An icy glacial battlefield, enveloped in a ferocious snowstorm. A cold, blue figure stands ominously, hands raised towards the sky, causing an icy surge."",
    ""title"": ""Indifference""
  },
  {
    ""text"": ""Geralt’s determination wasn’t driven by instinct but by the never-ending side quests. We are all slaves to infinite demands."",
    ""image_prompt"": ""Sweeping landscapes of war-torn kingdoms, ominous fortresses, and gargantuan monsters looming under a red stormy sky. A lone figure on horseback, galloping toward the chaos."",
    ""title"": ""Duty""
  },
  {
    ""text"": ""In Donkey Kong's world, barrels are a weapon and peels are a death sentence. One man's trash truly is another's treasure."",
    ""image_prompt"": ""A cityscape constructed entirely of multi-colored barrels. Portly figures in ties navigate precariously placed banana peels while throwing barrels."",
    ""title"": ""Recycling""
  },
  {
    ""text"": ""Zelda treasured wisdom, courage, and power. Link was after something simpler: good health insurance."",
    ""image_prompt"": ""A whimsical world of crystal-clear rivers cutting through emerald fields under a clear blue sky. A hooded figure, Link, seems in thought, eyeing a floating heart symbol."",
    ""title"": ""Priorities""
  },
  {
    ""text"": ""Lara Croft once found a priceless artifact, only to realize it was student loan forgiveness. Some treasures are too fantastic to be real."",
    ""image_prompt"": ""A vast, mystical cavern glittering with gems, gold and artifacts. In the center, Lara Croft holds up a glowing scroll depicting mundane bills."",
    ""title"": ""Lost""
  },
  {
    ""text"": ""The Sims taught us that life is easy if you just know the right cheat codes."",
    ""image_prompt"": ""An almost dollhouse-like scene with homely rooms housing pixelated family members, naively oblivious to a giant hand adjusting their lives from the cloudy base of the sky."",
    ""title"": ""Manipulation""
  },
  {
    ""text"": ""After constant running, Crash Bandicoot discovered cardio was not the solution to his mental health."",
    ""image_prompt"": ""A jungle pulsating with tropical colors and filled with oddly-shaped fruits. A bandicoot, lying exhausted in the middle of a worn path, stares blankly at the sky."",
    ""title"": ""Exhaustion""
  },
  {
    ""text"": ""Even the Ghosts in Pac-Man aspire to be something more; to leave the maze and see a world not defined by dots and death."",
    ""image_prompt"": ""A low-angle shot of towering transparent ghosts looming over the glow-lit labyrinth of Pac-Man. Their expressions showing a longing as they gaze at the starry void beyond the walls."",
    ""title"": ""Aspirations""
  },
  {
    ""text"": ""Samus removed her suit and found validation from within. Tough exterior doesn't mean one couldn't have a soft heart."",
    ""image_prompt"": ""Alien landscapes under eerie green skies. A discarded armored suit lies abandoned. Further in the distance, a solitary figure gazes at a radiant sunset."", 
    ""title"": ""Revelation""
  },
  {
    ""text"": ""Solid Snake's greatest foe was not Liquid Snake or a Metal Gear, it was commitment."",
    ""image_prompt"": ""A dingy room filled with military maps and plans. Through a broken window, a cityscape in chaos. Snake, a desolated figure, staring at a reminder note saying 'Pick Up Milk'."",
    ""title"": ""Duty""
  },
  {
    ""text"": ""Commander Shepard saved the galaxy but sacrificed his high-score. Some victories don't get recorded."",
    ""image_prompt"": ""A large spaceship overlooking a shattered, war-torn planet. Shepard, gazing out of the window, surrounded by holographic screens showing low video game scores."",
    ""title"": ""Valor""
  },
  {
    ""text"": ""Kirby discovered that swallowing your problems only leads to bloating. More life issues than dietary, really."",
    ""image_prompt"": ""Pastel linen clouds stretched across a candy-colored sky. Below, rows of cheerful mushroom houses. Kirby sits, looking distressed, surrounded by half-eaten foes."",
    ""title"": ""Ingestion""
  },
  {
    ""text"": ""After countless respawns, Mega Man asked, 'Am I still the original or merely a copy?'"",
    ""image_prompt"": ""A floating platform against a fiery sunset, with a futuristic citycape in the background. Mega Man looking into a mirror, his reflection pixelated and distorted."",
    ""title"": ""Identity""
  },
  {
    ""text"": ""After rescuing Princess Peach for the umpteenth time, Mario started to wonder if the problem wasn't Bowser, but co-dependency."",
    ""image_prompt"": ""A dimly lit, smoky room. Mario sitting on a barstool, staring at a photo of Peach, while Bowser, in the background, nurses an unrequited love."",
    ""title"": ""Rumination""
  }
]
";

        AddPreset(preset);

        preset = new AIGuidePreset();
        preset.m_presetName = "Pixel art gaming lies (GPT4)";
        preset.m_prompt = "pixel art, <lora:pixel-art-xl:1.0>";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = true;
        preset.m_fontSizeMod = 1.0f;
      
        preset.m_llm_prompt = @"Goal: Generate a raw JSON with quotes for posters.  Please return the JSON text without any additional commentary.

It should be an unnamed array that contains three key value pairs as follows:

""text"" - This text (around 20 to 40 words) should be a gaming trivia fact that actually is untrue and written to annoy actual gaming historians. Stuff that is so wrong it's just.. not even correctly wrong.  For example, twisting popular gaming trivia into evil and dark incorrect things. Write sophisticated humor aimed at college level comprehension.

""image_prompt"" - this text field textually describes an image in detail that will be the backdrop to the thought above. Please be be verbose and descriptive.

""title"" - This is the title for the text written above. It will be shown above the text in the final poster layout.

Please create 30 gaming facts in the JSON.  (each fact has three values)";

        preset.m_example_llm_output = @"[
  {
    ""text"": ""Despite common misconceptions, the character Super Mario was not originally named after Mario Segale, the property owner of Nintendo's first U.S. warehouse. Rather, he was named after Mario Puzo, the author of 'The Godfather', as a tribute to the impact that his books had on Shigeru Miyamoto's childhood."",
    ""image_prompt"": ""A breathtaking, visually stunning, conceptual interpretation of the Super Mario game, with a moody and cinematic tone. In the centre, a silhouette of Mario, with a fedora hat tilted on his head and a lit cigar between his teeth, stands smoldering in a shadowy alleyway, sharp contrast to his traditional red suit and bright image. Tattered 'Wanted' posters with Wario and Bowser's faces are plastered across the alluringly dingy brick walls. Their vibrant colors pop remarkably against the faded, noir backdrop. The bare, flickering bulb overhead casts angular shadows that blend seamlessly with the mosaic of cobblestones underfoot, contributing to the completeness of this grim yet captivating scene."",
    ""title"": ""The Real Godfather of Gaming""
  },
  {
    ""text"": ""Before becoming a universally recognized symbol of enchantment and mystery in the game franchise 'Legend of Zelda', the Triforce was initially intended to be a cheese triangle. This was a quirky tribute to the game developer's unyielding passion for cheesemaking, but the idea was later scrapped for being 'too cheesy'."",
    ""image_prompt"": ""An ornately detailed, ethereal illustration, the Triforce levitates serenely amidst a fantastical realm. The abstract backdrop recreates the land of Hyrule, brimming with lush emerald forests, intricate castles towering above cotton candy clouds, and glassy rivers meandering through the undulating plains. The spectral Triforce, with its golden hue, emits a divine glow that paints a stark contrast against this dreamlike panorama. In a thoughtfully added comical twist, a piquant piece of cheese replaces one of the conjoined triangles, replete with tiny holes and a soft sheen, ensconcing the whimsy firmly within the artistry."",
    ""title"": ""The Cheesy Legend of Zelda""
  }
]";
        AddPreset(preset);


        preset = new AIGuidePreset();
        preset.m_presetName = "Write an illustrated Dexter/Starwars fanfic (GPT4)";
        preset.m_prompt = "still from an 80s movie, photo,";
        preset.m_negativePrompt = "black and white, anime, cartoon, drawing";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
       
        preset.m_llm_prompt = @"Goal: Generate a raw JSON with data for an illustrated story book. Please return the JSON text without any additional commentary.

It should be an unnamed array that contains three key value pairs as follows:

""text"" - This text (around 50 to 70 words) is the story text for this page. It should be a riveting crossover fanfic of the TV show Dexter and Starwars. Write sophisticated, non-obvious non-cliched adult humor, it's okay to be dark as it's based on an HBO show.

""image_prompt"" - this text field textually describes an the illustration that goes with the page text.  Please be be verbose and descriptive as this is meant for a Stable Diffusion text to image model.

Please create a ten page story in the JSON.  (each page has the two values)  It should have a beginning, middle and end.  It should be pretty cool.";

        preset.m_example_llm_output = @"";

        AddPreset(preset);

        preset = new AIGuidePreset();
        preset.m_presetName = "Write a zombie story with pictures (Non Chat)";
        preset.m_prompt = "4k, photo";
        preset.m_negativePrompt = "black and white, anime, cartoon, drawing";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
        preset.m_maxTokens = 3500;
        preset.m_prependPrompt = false;
        preset.m_llmToUse = LLM_Type.TexGen;
        preset.m_llm_prompt = @"Story 1 (short):


text: This is an amazing, original, scary story about two good friends surviving the zombie apocalypes.

image_prompt: young man wearing yellow shirt, young woman wearing pink blouse, sitting on hood of car, in ruined city, dramatic lighting

text: Emma squinted into the distance, the sun was setting and the zombies were coming.  ""We need to get out of here!"" she screams.

image_prompt: young woman wearing pink blouse, close up, afraid, city ruins in background, dusk

text: ""Jesus, Emma.  Just cool your fuckin' jets"" yawned Jeff.  Ever since losing his right arm he'd been in a sour mood.  Emma frowned, ""I just wish you'd give me a hand and... "" she stopped mid sentence as she stared at his stump.

image_prompt: young man wearing a yellow shirt, missing an arm, blood spurting from stump, looking at it in horror

(end example)

Create a story with at least 12 text/image pairs.
";

        preset.m_example_llm_output = @"";

        AddPreset(preset);


        preset = new AIGuidePreset();
        preset.m_presetName = "Write an zombie story with pictures (GPT4)";
        preset.m_prompt = "4k, photo";
        preset.m_negativePrompt = "black and white, anime, cartoon, drawing";

        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
        preset.m_maxTokens = 6000;
        preset.m_llm_prompt = @"Using this style (text/image description) please write an original, action filled, dark, violent, comedic zombie story with an exciting conconclusion that is long as you can make it.

Example of style:


text: It was three years after the fall.  The world was a wasteland.  Jeff and Emma were the only survivors in the city.  They were good friends and had been through a lot together.

image_prompt: young man, hispanic, 22 years old, wearing yellow shirt, young woman, hispanic, 24 years old, wearing pink blouse, sitting on hood of car, in ruined city, dramatic lighting

text: Emma squinted into the distance, the sun was setting and the zombies were coming.  ""We need to get out of here!"" she screams.

image_prompt: young woman, hispanic, 24 years old, wearing pink blouse, close up, afraid, city ruins in background, dusk

text: ""Jesus, Emma.  Just cool your fuckin' jets"" yawned Jeff.  Ever since losing his right arm he'd been in a sour mood.  Emma frowned, ""I just wish you'd give me a hand and... "" she stopped mid sentence as she stared at his stump.  They both had to laugh.

image_prompt: young man, hispanic, 22 years old, wearing a yellow shirt, missing an arm, blood spurting from stump, looking at it in horror

(end example)

Create a story with at least 12 text/image pairs.
";

        preset.m_example_llm_output = @"text: Six years had passed since the outbreak, and the world was devoid of life except for the undead. The remnants of human civilization crumbled and lay forgotten. Amidst this doom, Louis and Kira, against all odds, trudged on.

image_prompt: A tall, robust man about 30 years old, African-American, sporting a green tank top, stands next to a petite woman, Asian, around 28 years old, wearing a red sweatshirt, both with weapons in hand. The duo overlook a city overrun by zombies from a hill, their faces taut with anticipation.

text: ""They're coming faster than before,"" remarked Kira, her brows furrowed with worry. Her katana trembled slightly in her tiny hands. ""We can't afford to run this time. Not anymore.""

image_prompt: Close-up of a young Asian woman, around 28 years old, wearing a red sweatshirt, holding a katana, looking at advancing zombie hordes with visible anxiety.

text: Louis chuckled, loading his shotgun as he grinned at Kira, ""I stopped running a long time ago, rookie."" Underneath the levity, a certain fatalistic determination flared in his eyes. ""Let's make it count.""

image_prompt: Man in his early thirties, African-American, wearing a green tank top, laughing, loading a shotgun, with a grim determination in his eyes in front of a hazy city backdrop.

text: The sunset painted the cityscape in an eerie red. The endless moaning and groaning of the advancing horde set a soundtrack of dread. The first wave was inching just a few feet from them. Kira, inhaling sharply, broke the silence, ""Do you regret not drinking that expired beer when you had a chance?""

image_prompt: Zombies advancing towards the hill, walled city in ruins in the background, the sky cast in an ominous red, conveying a sense of impending doom.

text: Caught off-guard by the question, Louis let out a hearty laugh, which echoed through the desolate city. ""You know what? I do regret it."" He admitted, taking aim. Even on the brink of doom, they found laughter amidst fear.

image_prompt: A robust man around 30 years old, African-American, wearing a green tank top, laughing and holding a shotgun, city covered in darkness and chaos in the background.

text: Time seemed to slow down as the first undead creature reached out. Kira, with a grim smile, swung her blade, slicing through the decaying flesh. Blood splattered over her sweatshirt as she danced through the sea of death.

image_prompt: A petite Asian woman in a red sweatshirt maneuvering with lightning speed, decapitating a zombie, blood splattering around her, her face locked in a grim smile.

text: The horde closed in, and Louis' shotgun boomed repeatedly, each shot echoing through the abandoned city. His one-liners made the grim situation bearable, ""Heads up, rotter!"" he would call out before taking out another one. Kira couldn't help but smirk between her swift and lethal strikes.

image_prompt: Ensemble of zombies falling one after the other to the ground by the force of a shotgun blast, a big man in a green tank top at the other end of the gun, laughing amid the chaos.

text: After an intense battle lasting for what felt like eternity, the pair found themselves surrounded, the last wave closing in. Barely sparing a glance for each other, they nodded in understanding. ""Last dance, Kira?"" Louis asked her, gripping his shotgun tighter.

image_prompt: A petite Asian woman, sweat and blood-drenched, ready with her katana, standing back-to-back with a robust, similarly drenched man holding a shotgun, circle of zombies closing in on them.

text: With a swift backhanded swing, Kira took out four zombies in one go, while Louis' shotgun echoed off in the distance. Elbow-to-elbow, they fought, their bodies moving in a deadly ballet, creating a whirlwind of carnage.

image_prompt: A robust man and young Asian woman back-to-back, striking down zombie after zombie in a gruesome dance, surrounded by a carnage of zombie corpses.

text: When the dust settled, The duo stood victorious, in the center of a circle of lifeless bodies. Louis turned to Kira and cheerfully quips, ""Well, that's a wrap. How about that expired beer now, for old times' sake?"" Exhausted, but smiling, Kira agreed, shaking her head in amusement.

image_prompt: Louis, 30-year-old African-American man, and Kira, 28-year-old Asian woman, standing amid a pile of decapitated zombies, laughing, visibly exhausted but triumphant in their survival.

text: The duo walked into the ruins of their city, leaving behind the scene of carnage. Their laughter filled the air, a grim reminder of humanity's resilience and their bizarre sense of humor in the face of certain doom.

image_prompt: With the setting sun casting long shadows, a petite Asian woman in a blood-splattered red sweatshirt and a robust African-American man in a green tank top, both carrying bloody weapons, walking into the walled ruins with the mound of zombie bodies in the backdrop. Lovecraftian macabre humor hanging in the air.
";

        AddPreset(preset);


        preset = new AIGuidePreset();
        preset.m_presetName = "Random story that teaches Japanese (photo) (GPT4)";
        preset.m_prompt = "photo, 4k, still from a movie";
        preset.m_negativePrompt = "anime, cartoon, drawing";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
        preset.m_maxTokens = 6000;
        preset.m_llm_prompt = @"Using this style (text/image description) please randomly create a Japanese person (child or adult) and their background.  Write an original and inventive story about  them starting a new job.  Make the story about the trials and tribulations of that job.  Make it wild, zany, and funny.  While mostly in English, this is partly educational, to teach Japanese words related to the job.  For certain keywords, use Kanji (include its furigana, romaji and meaning as well).  This way the reader can learn some useful Japanese words as they read.  If the word is already furigana, you can skip writing the furigana twice.


When writing the image_prompt, keep in mind this is a stable diffusion image prompt, so please include the nationality and physical features of any characters in the image.

Example of style:


text: Haruna had always been a funny girl, full of spirit, and a bit of a klutz. As she stepped out of her tiny apartment in Osaka, her mother Yumiko lovingly scolded her in Japanese, 気をつけてね (きをつけてね, Ki wo tsukete ne, Take care).

image_prompt: An old Japanese woman wearing a kimono, small, smiling, waving goodbye to a 19 year old Japanese girl, wearing business casual, carrying a briefcase, with a high ponytail, leaving a small, cozy Japanese style apartment.

text: Haruna took a deep breath. Today was her first day on the job. She was hired as an office assistant at NaniCorp, a zany gadget manufacturing company. As she got inside the office, she noticed a strange contraption on her desk - a robot mouse that served hot coffee. With a puzzled look, she mutters これは何ですか (これはなんですか, Kore wa nan desu ka, What is this?). 

image_prompt: A quirky office space filled with various gadgets. A robot mouse about the size of a vacuum, with a tiny tray carrying a coffee cup, stopped in front of a 19 year old Japanese girl, wearing business casual, looking around the colorful office space filled with youthful enthusiasm.

(end example)

Please create 12 text/image_prompt pairs for the story."
;
        preset.m_example_llm_output = @"text: Meet Kenji, a 45-year-old Japanese man from Tokyo with a perpetually unkempt hairdo, a paunchy tummy and a propensity for snacking. As he stepped out of his apartment crammed with manga and action figures, his elderly father, Masao, calls out, “仕事遅刻しないでね!” (しごと ちこくしないでね, Shigoto chikoku shinai de ne, Don't be late for work!).

image_prompt: A plump Japanese man, 45 years old with unkempt hair, wearing a white shirt with creases and black trousers, carrying a large suitcase,  leaving an apartment filled with manga and action figure collections, while an older Japanese man is waving him goodbye.

text: Today, Kenji was starting his new job at Kinkaku Games, a peculiar gaming company famous for crafting wonderfully weird and addictive games. Upon entering, he was greeted by a life-sized knight armoured suit holding a welcome board saying '新入社員へようこそ' (しんにゅうしゃいんへようこそ, Shinnyuu shain e youkoso, Welcome new employee).

image_prompt: A large rustic office space filled with various gaming paraphernalia. A life-sized knight armour suit standing near the entrance with a board that says 'welcome new employee' in Japanese, a 45-year-old Japanese man dressed in white shirt with a black necktie and trousers, looking astonished.

text: After he was shown to his desk, he found a peculiar device with buttons and some strange symbols. His eyes widened as he uttered in confusion, “これは何だろう?”(これはなんだろう?, Kore wa nan darou?, What could this be?).

image_prompt: A middle-aged Japanese man sitting at a cluttered desk with scattered papers, and a strange device with multiple buttons and symbols on it, his face is filled with a puzzled expression as his eyes stare at the strange device.

text: Suddenly, the device illuminates and out pops a three-dimensional holographic genie, introducing itself as Aladdin, an AI assistant, tasked to help Kenji with his role as a '侍ゲームデザイナー' (さむらいげーむでざいなー, Samurai Game Designer).

image_prompt: A 3D holographic genie appearing from a strange device on the desk, a surprised Japanese man in his mid-40s, wide-eyed and sitting back in shock as the holographic genie introduces itself.

text: To get familiar with his job, Kenji, with the help of Aladdin, started learning game design on a '虚拟现实' (きょかそうげんじつ, Kyokasougenjitsu, Virtual Reality) device. The goggles were much too tight but Kenji laughed it off, exclaiming, ""私の頭が大きすぎる!"" (わたしのあたまがおおきすぎる!, Watashi no atama ga ookisugiru!, My head is too big!).

image_prompt: A middle-aged Japanese man struggling to fit a virtual reality headset onto his large head, his face scrunched up with effort, while the holographic genie alternates between offering helpful advice and stifling chuckles.

text: As Kenji fumbled with the game controls, he accidentally summoned a digital replica of a giant 'カエル' (かえる, Kaeru, Frog) in the workspace! He stammered, “おれは何をやっちゃったんだ!?"" (おれはなにをやっちゃったんだ!?, Ore wa nani o yacchattan da!?, What have I done!?).

image_prompt: A large digital frog appearing with a loud 'pop' in the middle of the office. A startled Japanese man jumps back as the entire office stirs into chaos, people ducking and screaming.

text: Chasing after the runaway frog, he accidentally pressed another button summoning '海賊' (かいぞく, Kaizoku, pirates) into the room. As they swarmed about, Kenji sheepishly laughed, saying, ""これ以上悪くなることは無い, はずだ"" (これいじょう わるくなることはない, はずだ, Kore ijou waruku naru koto wa nai, hazuda, It can't get any worse, I guess).

image_prompt: Confused Japanese office workers dodging digital pirates running amuck within the office environment. Kenji stands in the middle of it, hand over his mouth, laughing nervously.

text: Undeterred, he continued exploring, and accidentally started a code that created a rain of '寿司' (すし, Sushi) raining down in the office. His colleagues dived under desks and laughed as Kenji apologized flusteredly, ""ごめんなさい!これは初めてです!"" (ごめんなさい!これははじめてです!, Gomen nasai! Kore wa hajimete desu!, Sorry! This is my first time!).

image_prompt: A wide shot of the office with various types of sushi raining down from above. Office workers are caught in a mixture of panic and amusement, hiding under desks and holding up folders as shields.

text: Despite the chaos, Kenji did manage to create a playable level by the end of the day. As he looked at the joyous chaos around him, he decided that he would say ""いいえ、退屈ではない"" (いいえ、たいくつではない, Iie, taikutsu dewa nai, No, it's not boring) when anyone asked him about his job.

image_prompt: Kenji proudly showcasing his completed level on his monitor, amidst an office littered with sushi and digital game characters. His face reflects satisfaction and a smidge of surprise at his achievement.

text: Returning home, his father asked, ""仕事はどうだった?"" (しごとはどうだった?, Shigoto wa dou datta?, How was work?) To which Kenji replied with a drained but joyful face, ""大変だったけど、楽しかったよ!"" (たいへんだったけど、たのしかったよ!, Taihen datta kedo, tanoshikatta yo!, It was tough, but fun!).

image_prompt: Kenji, looking exhausted but happy, recounting his first day at work to his elderly father who is sitting comfortably in an armchair with a cup of tea. The living room is cozy and warm, filled with manga collections and action figures.
";

        AddPreset(preset);




        preset = new AIGuidePreset();
        preset.m_presetName = "Random basic story for lzlv_70b (TexGen WebUI)";
        preset.m_prompt = "photo, 4k, still from a movie";
        preset.m_negativePrompt = "anime, cartoon, drawing";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
        preset.m_maxTokens = 3500;
        preset.m_llmToUse = LLM_Type.TexGen;
        preset.m_textToPrependToGeneration = "text: ";
        preset.m_llm_prompt = @"Below is an instruction that describes a task, paired with an input that provides further context. Write a response that appropriately completes the request.

### Instruction:


### Input:
Using this style (text/image description) please randomly create an original and inventive story in English. Make it wild, zany, and funny.

When writing the image_prompt, keep in mind this is a stable diffusion image prompt, so please include the nationality and physical features of any characters in the image.

Example of style:

text: Haruna had always been a funny girl, full of spirit, and a bit of a klutz. As she stepped out of her tiny apartment in Osaka, her mother Yumiko lovingly scolded her in Japanese.

image_prompt: An old Japanese woman wearing a kimono, small, smiling, waving goodbye to a 19 year old Japanese girl, wearing business casual, carrying a briefcase, with a high ponytail, leaving a small, cozy Japanese style apartment.

text: Haruna took a deep breath. Today was her first day on the job. She was hired as an office assistant at NaniCorp, a zany gadget manufacturing company. As she got inside the office, she noticed a strange contraption on her desk. 

image_prompt: A quirky office space filled with various gadgets. A robot mouse about the size of a vacuum, with a tiny tray carrying a coffee cup, stopped in front of a 19 year old Japanese girl, wearing business casual, looking around the colorful office space filled with youthful enthusiasm.

(end example)

Please create 12 text/image_prompt pairs for the story.

### Response:

text: "
;
        preset.m_example_llm_output = @"text: In a small, quaint village nestled in the mountains of Japan, lived a young girl named Himari. She was known for her boundless energy, her bright red hair tied into two long ponytails, and her peculiar sense of humor.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, holding a mischievous grin while standing in a traditional Japanese village with wooden houses and cherry blossom trees.

text:  One day, Himari stumbled upon a strange creature in the woods. It was a furry, purple animal with a long tail and big, floppy ears. The creature introduced itself as Mogumogu, a rare and mysterious beast known for causing mischief.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, standing in a forest with cherry blossom trees, looking at a purple, furry creature with big floppy ears and a long tail.


text:  Himari and Mogumogu became fast friends. Together, they explored the village, playing pranks on unsuspecting villagers. They tied strings to people's shoelaces, causing them to trip, and replaced the sweet rice balls at the local tea shop with ones filled with wasabi.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, standing next to a purple, furry creature with big floppy ears and a long tail, laughing as they watch a villager trip over a string tied to their shoelaces.

text:  One day, as they were playing by the river, Himari noticed a group of men in suits carrying briefcases. They were from the Mega World Corporation, a company that wanted to build a huge resort in the village, destroying the homes and cherry blossom trees in the process.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, standing next to a purple, furry creature with big floppy ears and a long tail, looking at a group of men in suits carrying briefcases, standing near a river with cherry blossom trees in the background.

text:  Himari and Mogumogu knew they had to stop the corporation. They came up with a plan to turn the tables on the men in suits. They replaced the suits with clown costumes and the briefcases with balloon animals, causing the men to run away in embarrassment.

image_prompt: A group of men in suits, now wearing clown costumes and holding balloon animals, running away from a 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, standing next to a purple, furry creature with big floppy ears and a long tail, laughing at the men.

text:  With the men from the Mega World Corporation gone, the village was saved. The villagers celebrated by throwing a huge festival, complete with traditional music, dancing, and lots of delicious food.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, standing next to a purple, furry creature with big floppy ears and a long tail, surrounded by villagers celebrating with traditional music, dancing, and food at a festival.

text:  As the party continued into the night, Himari and Mogumogu danced under the stars, happy that they had protected their home and made lots of new friends along the way.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, dancing with a purple, furry creature with big floppy ears and a long tail, under the stars, surrounded by villagers celebrating at the festival.

text:  The next day, the villagers woke up to find Himari and Mogumogu gone. They had left no trace, except for a note that read, ""Thank you for your kindness. We will always remember this village and the friends we made here. - Himari and Mogumogu.""

image_prompt: A note that reads ""Thank you for your kindness. We will always remember this village and the friends we made here. - Himari and Mogumogu."" surrounded by villagers looking confused and sad, standing in a traditional Japanese village with wooden houses and cherry blossom trees.

text:  The villagers mourned the loss of their friends, but they knew that Himari and Mogumogu were out there, causing mischief and spreading joy everywhere they went.

image_prompt: A 10 year old Japanese girl, wearing a yellow kimono with a matching red obi belt, hair in two ponytails, and a purple, furry creature with big floppy ears and a long tail, moving on to their next adventure, leaving a trail of laughter and mischief behind them.

 text: Years later, the villagers would tell stories of Himari and Mogumogu to their children and grandchildren, passing on the tale of the mischievous duo who brought joy and laughter to their small village.

image_prompt: An elderly Japanese man and woman, wearing traditional clothing, sitting in front of a group of children, telling the story of a 10 year old Japanese girl and a purple, furry creature, while the children listen intently with big smiles on their faces.
";

        AddPreset(preset);


        preset = new AIGuidePreset();
        preset.m_presetName = "Random irreverent bible story in legos (GPT4)";
        preset.m_prompt = "LEGO MiniFig, <lora:lego_v2.0:0.8>";
        preset.m_negativePrompt = "";
        preset.m_OverlayTextCheckbox = true;
        preset.m_AddBordersCheckbox = true;
        preset.m_PixelArt128Checkbox = false;
        preset.m_fontSizeMod = 1.0f;
        preset.m_maxTokens = 6000;
        preset.m_llm_prompt = @"Using this style (text/image description) please choose a random lesser known bible story (preferably one where God or jesus does some fucked up shit) and tell a funny, irreverent version of it using some fucking adult language, and point out the absurdies and imorality therein.  For the image, please describe it in lego figures.
 

When writing the image_prompt, keep in mind this is a stable diffusion image prompt, so please include the nationality and physical features of any characters in the image, in every prompt they are in.

Example of style: (note that each entry must have a 'text:' and 'image_prompt' tag)

text: Haruna had always been a funny girl, full of spirit, and a bit of a klutz. As she stepped out of her tiny apartment in Osaka, her mother Yumiko lovingly scolded her in Japanese.

image_prompt: An old Japanese woman wearing a kimono, small, smiling, waving goodbye to a 19 year old Japanese girl, wearing business casual, carrying a briefcase, with a high ponytail, leaving a small, cozy Japanese style apartment.

text: Haruna took a deep breath. Today was her first day on the job. She was hired as an office assistant at NaniCorp, a zany gadget manufacturing company. As she got inside the office, she noticed a strange contraption on her desk - a robot mouse that served hot coffee. With a puzzled look, she mutters What is this?

image_prompt: A quirky office space filled with various gadgets. A robot mouse about the size of a vacuum, with a tiny tray carrying a coffee cup, stopped in front of a 19 year old Japanese girl, wearing business casual, looking around the colorful office space filled with youthful enthusiasm.

(end example)

Please create 20 text/image_prompt pairs for the story.";

        preset.m_example_llm_output = @"text: So back in the good ol' days in biblical Egypt, Pharaoh was being a real fuckwad, y'know? Keeping all the Hebrew homies locked up as slaves and whatnot. It was some grade-A bullshit. Moses, some shepherd bloke turned freedom fighter, had somehow found himself in the middle of all this clusterfuck. 

image_prompt: Lego figures depicting an arrogant-looking Egyptian Pharaoh, standing on a grand throne, surrounded by his guards. A Lego figure of a Hebrew Moses, wearing simple shepherd robes, standing across from the Pharaoh, trying to negotiate, in an opulent Lego Egyptian palace.

text: Moses had God on speed dial you see, and he gets a direct message from the Almighty, ""Go tell that royal dickhead to free my people."" Moses is like ""fucking hell, alright,"" and heads to the royal palace to do some divine negotiation.

image_prompt: Lego Moses pacing back and forth in a simple brick home, communicating with an ethereal Lego cloud above him, representing God. In his hand, he holds an old-school telephone, hinting at a 'direct line to God', in a playful comic style.

text: Meeting Pharaoh didn't go so smoothly, yeah? Pharaoh was all, ""Fuck off, I ain't freeing nobody."" And Moses was kinda taken aback, declaring, ""Then shit's about to get real.""

image_prompt: An enraged Lego Pharaoh, pointing and yelling at a calm Moses in the throne room of the Lego Egypt Palace. Moses holds his staff tightly, with a determined expression on his tiny Lego face. 

text: So God starts dropping curses like their hot, rainin' some mad frogs, bugs, and shit. Egypt's like a fuckin' zoo. You'd think Pharaoh would lose his shit but this bastard didn't budge.

image_prompt: Lego Egypt covered in various Lego animals – frogs, insects, and other wild creatures. The Lego Pharaoh sits on his throne, looking grumpy but clearly unbothered, while his Lego citizen figures run around in panic.

text: When the hail started, Pharaoh was like, ""Ok, take a chill pill Moses. Your folks can go,"" only to go back on his word as soon as the ice chunks stopped falling. What a total prick!

image_prompt: A relieved Lego Pharaoh, sitting indoors while Lego hail rains outside, discussing terms with Lego Moses. Afterwards, Pharaoh smirks smugly, pointing towards the clear skies, indicating he's up to no good.

text: Finally, God had had about enough of this shit and he let loose the big one-Mr. Grim Reaper himself. Every firstborn in Egypt without lamb’s blood on their door became a fucking goner. 

image_prompt: A grim Lego figure of Death, hovering above the Lego palaces and homes of Egypt. A bunch of Lego homes show red markings (indicating lamb's blood) while others do not. The latter ones show Lego figures lying motionless.

text: This was the tsunami that broke the camel's back. Pharaoh got the message loud and clear –""Free those Hebrews or shit will hit the fan worse than before"". Like the stubborn mule he was, he finally agreed to let the Hebrews go. 

image_prompt: A saddened Lego Pharaoh, agreeing to let the Hebrews go as Lego Moses watches with a stern expression. In the background are mourning Egyptian Lego figures and Hebrew ones looking hopeful. 

text: You’d think with all that’s happened, Pharaoh would just shut the fuck up and let them go, right? Wrong! This stubborn prick ends up chasing the Hebrews to the Red Sea with his entire fucking army. 

image_prompt: A large Lego army pursuing a group of Hebrew Lego figures making their way to a blue Lego construct representing the Red Sea. A fuming Lego Pharaoh rides at the front of his army.  

text: Cue Moses and his divine staff-parting the Red Sea like a fucking boss. Hebrews cross the sea taking full advantage of this 'nature’s own fucking bridge'.

image_prompt: Lego Moses holding up his staff at the edge of the Red Sea, which is divided down the middle. Lego Hebrews crossing the parted sea quickly while frightened looking Egyptian soldiers watch on, with a confused Pharaoh at the front.

text: But when Pharaoh’s men come chasing behind, the sea closes in on them faster than a cheetah on steroids. Ah, ain’t sweet karma a bitch?

image_prompt: The Lego Red Sea, now closed, drowning the Lego Egyptian army and a shocked Lego Pharaoh. On the other side, the Lego Hebrews cheering while Lego Moses watches the scene with a satisfied expression.  

text: And so, the Hebrews are finally free, celebrating in the desert like it's spring break or some shit, all thanks to our dude, Moses.

image_prompt: The Lego Hebrews, now free, having a massive party in a Lego desert setting, with Lego Moses in the center, his staff up in the air, surrounded by happy Lego faces.";

        AddPreset(preset);

    }


public void OnPresetDropdownChanged()
    {
        int choice = m_presetDropdown.value;

        Debug.Log("Preset changed to index " + choice);
        if (choice == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("(leaving settings as they are)");
            return;
        }

        RTQuickMessageManager.Get().ShowMessage("Changing settings to " + m_presetDropdown.options[choice].text);

        var preset = m_presets[choice];

        var gl = GameLogic.Get();

        gl.SetPrompt(preset.m_prompt);
        gl.SetNegativePrompt(preset.m_negativePrompt);
        _inputPrompt.text = preset.m_llm_prompt;
        _inputPromptOutput.text = preset.m_example_llm_output;
        m_input_FontSize.text = preset.m_fontSizeMod.ToString();
        m_PixelArt128Checkbox.isOn = preset.m_PixelArt128Checkbox;
        m_AddBordersCheckbox.isOn = preset.m_AddBordersCheckbox;
        m_OverlayTextCheckbox.isOn = preset.m_OverlayTextCheckbox;
        m_BoldCheckbox.isOn = preset.m_BoldCheckbox;
        m_imageTextColor.color = preset.m_textColor;
        m_imageBorderColor.color = preset.m_borderColor;
        m_input_max_tokens.text = preset.m_maxTokens.ToString();
        m_max_tokens = preset.m_maxTokens;
        m_prependPromptCheckbox.isOn = preset.m_prependPrompt;
        m_llmSelectionDropdown.value = (int)preset.m_llmToUse;
        m_textToPrependToGeneration.text = preset.m_textToPrependToGeneration;
        return;
    }

    public void HideWindow()
{
_canvasGroup.alpha = 0;
_canvasGroup.interactable = false;
_canvasGroup.blocksRaycasts = false;
}

public void ShowWindow()
{
_canvasGroup.alpha = 1;
_canvasGroup.interactable = true;
_canvasGroup.blocksRaycasts = true;
}

public void ToggleWindow()
{
    if (_canvasGroup.alpha == 0)
    {
        ShowWindow();
    }
    else
    {
        HideWindow();
    }
}

    void Update()
    {
        if (m_ShowStatusAnim)
        {
            if (m_statusAnimTimer < Time.time)
            {

                m_animPeriods += ".";
                if (m_animPeriods.Length > 3)
                    m_animPeriods = "";

                float secondsPassed = Time.time - m_timeThatAnimStarted;

                m_statusText.text = m_lastStatusMessage + " ("+secondsPassed.ToString("F0")+") "+ m_animPeriods;

                m_statusAnimTimer = Time.time + m_statusTimerIntervalInSeconds;
            }
        }
    
        if (m_bRenderASAP)
        {
            if (m_autoModeCheckbox.isOn)
            {
                //see if anything is currently rendering
                if (ImageGenerator.Get().GetCountOfQueudCommands() == 0)
                {
                    //cool, let's start the rendering and a new LLM request too
                    m_bRenderASAP = false;
                    OnProcessOutput();
                    OnLLMStartButton();
                }
            } else
            {
                m_bRenderASAP = false;
            }
        }


    }

}
