using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using TMPro;
using UnityEngine.UI;
using System.IO;
using System;
using System.Linq;

public class AIGuideManager : MonoBehaviour
{

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
        public string m_forcedFileNameBase;
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
    int m_llmGenerationCounter = 0;
    //keep track of the start button so we can change the text on it later
    public Button m_startButton;
    public TMP_InputField _inputPrompt;
    public Scrollbar _inputPromptScrollRect;
    public TMP_InputField _inputPromptOutput;
    public TMP_InputField _autoContinueTextInput;
    public Scrollbar _inputPromptOutputScrollRect;
    public TMP_InputField _stopAfterTextInput;

    public OpenAITextCompletionManager _openAITextCompletionManager;
    public TexGenWebUITextCompletionManager _texGenWebUICompletionManager;
    public AnthropicAITextCompletionManager _anthropicAITextCompletionManager;

    public GPTPromptManager _promptManager;
    public TMP_Dropdown m_presetDropdown;
    public TMP_InputField m_input_FontSize;
    public TMP_InputField m_input_max_tokens;
    public TMP_InputField m_inputRenderCount;
    private string accumulatedText = "";
    public string m_totalPromptReceived = "";

    //add public handle to a checkbox control
    public Toggle m_PixelArt128Checkbox;
    public Toggle m_AddBordersCheckbox;
    public Toggle m_OverlayTextCheckbox;
    public Toggle m_autoSaveResponsesCheckbox;
    public Toggle m_generateExtraCheckbox;
    public Toggle m_BoldCheckbox;
    string m_statusStopMessage = "";
    public TMP_Dropdown m_llmSelectionDropdown;
    public TMP_Dropdown m_rendererSelectionDropdown;
    private string _textToProcessForPics;
    public TMP_Dropdown m_textFontDropdown;
    public TMP_Dropdown m_titleFontDropdown;
    int m_max_tokens;
    bool m_bCreatedRandomAutoSaveFolder = false;
    TextFileConfigExtractor m_extractor = new TextFileConfigExtractor();
    public TextMeshProUGUI m_statusText;
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
    int _curPicIdx = 0;

    bool m_bIsPossibleToContinue = false;
    bool m_bTalkingToLLM = false;
    string m_imageFileNameBase;
    string m_folderName;
    //make an array of fonts that we can edit inside the editor properties
    public TMP_FontAsset[] m_fontArray;
    public GameObject m_notepadTemplatePrefab; //(attach to RTNotepad prefab)
    bool m_bFirstTimeToShow = true;
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

    public void OnStreamingTextCallback(string text)
    {

        //_inputPromptOutput.text is a TMPro.TMP_InputField and accumulatedText is a string
        _inputPromptOutput.text += text;
        accumulatedText += text;
        m_totalPromptReceived += text;

        string picText = GetPicFromText(ref accumulatedText);

        if (picText.Length != 0)
        {
            _textToProcessForPics = picText;
            StartCoroutine(ProcessTextFileBasic());
        }
        //Debug.Log(text);

        //Did we receive enough text to find a text/image pair to render?
    }

    public void SetupRandomAutoSaveFolder()
    {
        _curPicIdx = 0;

        m_folderName = "aiguided_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        //create the directory
        Directory.CreateDirectory(Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + m_folderName);

        m_imageFileNameBase = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + m_folderName + "/set_" + UnityEngine.Random.Range(0, 9999).ToString() + "_";

        //move our /web/index.php file into this folder, that way if you just copy this folder to a website it has a built in viewer
        string sourceFile = Config.Get().GetBaseFileDir("/web/index.php");
        string destFile = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + m_folderName + "/index.php";
        File.Copy(sourceFile, destFile, true);
        m_bCreatedRandomAutoSaveFolder = true;
    }

    void SetStartTextOnStartButton(bool bShowStart)
    {
        if (bShowStart)
        {
            m_startButton.GetComponentInChildren<TMP_Text>().text = "Start";
        }
        else
        {
            m_startButton.GetComponentInChildren<TMP_Text>().text = "Stop";
        }
    }

    public void OnLLMContinueButton()
    {

        _inputPromptOutput.text += "\n\n";
        accumulatedText += "\n\n";
        m_totalPromptReceived += "\n\n";
        m_llmGenerationCounter++;
        //actually, because it's so damn slow, let's just reset the text.  We won't be changing
        //the promptManager, so the LLM will still be getting it all, it's more an issue with my
        //slow text scanning
        _inputPromptOutput.text = "";
        //Let's also move the _inputPromptOutputScrollRect to the top now that the text is empty
        _inputPromptOutputScrollRect.value = 0.0f;  // Scrolls back to the top


        accumulatedText = "";

        Debug.Log("Getting additional responses from " + Config.Get()._texgen_webui_address); ;
        _promptManager.AddInteraction("user", _autoContinueTextInput.text);
        Queue<GTPChatLine> lines = _promptManager.BuildPromptChat();

        SpawnLLMRequest(lines);
        SetStartTextOnStartButton(false);
        RTQuickMessageManager.Get().ShowMessage("Contacting LLM...", 1);

        //Convert _stopAfterTextInput to an int
        int stopAfter;
        bool parseSuccess = int.TryParse(_stopAfterTextInput.text, out stopAfter);
        if (parseSuccess)
        {
            if (m_llmGenerationCounter >= stopAfter)
            {
                //we better stop after this one
                m_bIsPossibleToContinue = false;
                m_autoModeCheckbox.isOn = false;

            }
        }
        else
        {
            // Handle parse failure
            RTConsole.Log("Error:  You need to set the stop after to something valid");
            GameLogic.Get().ShowConsole(true);
            m_bIsPossibleToContinue = false;
        }
        
    }

    public void SpawnLLMRequest(Queue<GTPChatLine> lines)
    {
        if (m_llmSelectionDropdown.value == (int)LLM_Type.OpenAI_API)
        {
            string json = _openAITextCompletionManager.BuildChatCompleteJSON(lines, m_max_tokens, _aiTemperature, Config.Get().GetOpenAI_APIModel(), true);
            RTDB db = new RTDB();
            RTConsole.Log("Contacting GPT4 at " + Config.Get()._openai_gpt4_endpoint + " with " + json.Length + " bytes...");
            _openAITextCompletionManager.SpawnChatCompleteRequest(json, OnGTP4CompletedCallback, db, Config.Get().GetOpenAI_APIKey(), Config.Get()._openai_gpt4_endpoint, OnStreamingTextCallback, true);
        }

        if (m_llmSelectionDropdown.value == (int)LLM_Type.GenericLLM_API)
        {
            Debug.Log("Contacting TexGen WebUI asking for chat style response at " + Config.Get()._texgen_webui_address); ;


            string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, m_max_tokens, _aiTemperature, "chat", true);

            RTDB db = new RTDB();
            _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, Config.Get()._texgen_webui_address, "/v1/chat/completions", OnStreamingTextCallback,true,
                Config.Get()._texgen_webui_APIKey);
        }

        if (m_llmSelectionDropdown.value == (int)LLM_Type.Anthropic_API)
        {
            Debug.Log("Contacting TexGen WebUI asking for chat style response at " + Config.Get().GetAnthropicAI_APIEndpoint()); ;

            string json = _anthropicAITextCompletionManager.BuildChatCompleteJSON(lines, m_max_tokens, _aiTemperature, Config.Get().GetAnthropicAI_APIModel(), true);

            RTDB db = new RTDB();
            _anthropicAITextCompletionManager.SpawnChatCompletionRequest(json, OnTexGenCompletedCallback, db, Config.Get().GetAnthropicAI_APIKey(), Config.Get().GetAnthropicAI_APIEndpoint(), OnStreamingTextCallback,
                true);
        }

        m_bTalkingToLLM = true;

    }

    bool IsRequestActive()
    {
        return (_texGenWebUICompletionManager.IsRequestActive()
          || _openAITextCompletionManager.IsRequestActive() || _anthropicAITextCompletionManager.IsRequestActive());
    }


    public void OnViewReceieved()
    {
        RTNotepad notepadScript = RTNotepad.OpenFile(m_totalPromptReceived, m_notepadTemplatePrefab);
        notepadScript.m_onClickedCancelCallback += OnProfileCanceled;
        notepadScript.SetSaveButtonVisible(false);
        notepadScript.m_onClickedApplyCallback += OnProfileApply;
    }
    public void OnLLMStartButton()
    {

        if (IsRequestActive())
        {
           
                _texGenWebUICompletionManager.CancelCurrentRequest();
                _openAITextCompletionManager.CancelCurrentRequest();
                _anthropicAITextCompletionManager.CancelCurrentRequest();

                SetStatus("Cancelled request", false);

                //Set text on m_startButton back to Start
                SetStartTextOnStartButton(true);
                m_llmGenerationCounter = 0;
            return;
        }



        //RTConsole.Log("Contacting LLM...");
        //set m_max_tokens from m_input_max_tokens (and not crash if the input is blank or bad)

        int maxTokens;
        bool parseSuccess = int.TryParse(m_input_max_tokens.text, out maxTokens);
        if (parseSuccess)
        {
            m_max_tokens = maxTokens;
        }
        else
        {
            // Handle parse failure
            RTConsole.Log("Error:  You need to set the max tokens to something valid");
            GameLogic.Get().ShowConsole(true);
            m_max_tokens = 4096;
        }

        m_prompt_used_on_last_send = _inputPrompt.text; //might be useful later


        if (!m_bIsPossibleToContinue || !m_autoModeCheckbox.isOn)
        {
            //this counts as a new generation
            _promptManager.Reset();
            m_llmGenerationCounter = 0;
            _inputPromptOutput.text = "";
        }

        m_bIsPossibleToContinue = false;

        if (m_llmSelectionDropdown.value == (int)LLM_Type.OpenAI_API)
        {

            if (Config.Get().GetOpenAI_APIKey().Length < 15)
            {
                RTConsole.Log("Error:  You need to set the GPT4 API key in your config.txt! (example: set_openai_gpt4_key|<key goes here>| ) ");
                //open the console
                GameLogic.Get().ShowConsole(true);
                return;
            }
        }

        if (m_llmSelectionDropdown.value == (int)LLM_Type.Anthropic_API)
        {

            if (Config.Get().GetAnthropicAI_APIKey().Length < 15)
            {
                RTConsole.Log("Error: You need to set the Anthropic key in your config.txt!");
                //open the console
                GameLogic.Get().ShowConsole(true);
                return;
            }
        }

        _promptManager.SetSystemName(Config.Get().GetAISystemWord());
        
        _promptManager.SetBaseSystemPrompt(_inputPrompt.text);
        //_promptManager.AddInteraction("assistant", "I will start without any extra text explaining or verifying.  Are you ready?");

        //let's add a user prompt too, some models require it
        _promptManager.AddInteraction("user", "Start now.");


        Queue<GTPChatLine> lines = _promptManager.BuildPromptChat();

        SpawnLLMRequest(lines);

        accumulatedText = m_textToPrependToGeneration.text;
        m_totalPromptReceived = m_textToPrependToGeneration.text;

        SetStartTextOnStartButton(false);

        if (true)
        {
            //_inputPromptOutput.text = "";
            _inputPromptOutput.text += m_textToPrependToGeneration.text;
            //move view to the top of the textbox as it might be scrolled down before we erased the text
            _inputPromptOutputScrollRect.value = 0.0f;  // Scrolls back to the top
        }

        if (m_autoSave.isOn || m_autoSaveJPG.isOn)
        {
            SetupRandomAutoSaveFolder();
        }
        else
        {
            m_bCreatedRandomAutoSaveFolder = false;
        }

        RTQuickMessageManager.Get().ShowMessage("Contacting LLM...", 1);
        SetStatus("Sent request to LLM, waiting for reply", true);
    }

    public System.Collections.IEnumerator AddPicture(string text, string imagePrompt, string title, int idx, RTRendererType requestedRender)
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

        if (!Config.Get().DoesGPUExistForThatRenderer((RTRendererType)m_rendererSelectionDropdown.value))
        {
            //display an error message
            RTQuickMessageManager.Get().ShowMessage("That renderer type seems to be missing currently!");
            yield break;
        }

        ScheduledGPUEvent e = picMain.OnRenderButton(imagePrompt);
        e.promptOverride = GameLogic.Get().GetPrompt() + " " + imagePrompt;
        e.requestedDetailedPrompt = GameLogic.Get().GetComfyUIPrompt() + " " + imagePrompt;
        e.requestedRenderer = requestedRender;
        picMain.PassInTempInfo(e);

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

            if (m_autoSave.isOn || m_autoSaveJPG.isOn)
            {
                picMain.m_aiPassedInfo.m_forcedFileNameBase = m_imageFileNameBase + idxString;
            }
        }

        //see if m_AddBordersCheckbox has been checked by the user
        if (m_AddBordersCheckbox.isOn)
        {
            pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnAddMotivationBorder;
            //hack due to timing issues caused by using coroutines, the above will call OnAddMotivationtextWithTitle and OnSaveIfNeeded itself

            yield return null;
        }
        else
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

    public string GetPicFromText(ref string text)
    {
        List<string> lines = new List<string>(text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None));

        bool bFoundImage = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmedLine = lines[i].Trim().ToLower();

            if (!bFoundImage)
            {
                //seeing if we pass the first image tag
                if (trimmedLine.StartsWith("image"))
                {
                    bFoundImage = true;
                    continue;
                }
            }
            else
            {
                //we found the first image tag, just need to see if we're past it enough to consider it ready to send to the pic renderer

                if (trimmedLine.Length == 0 || trimmedLine.StartsWith("text:"))
                {
                    //yeah, that'll do it.  Remove the lines (0 through i) from the text string itself
                    text = string.Join("\n", lines.GetRange(i, lines.Count - i).ToArray());

                    //return the text of 0 through i
                    return string.Join("\n", lines.GetRange(0, i).ToArray());
                }
            }
        }

        // If no valid pair is found, return empty and leave the text unchanged
        return "";
    }

    public System.Collections.IEnumerator ProcessTextFileBasic()
    {

        // Iterate through each line of _inputPromptOutput.text, also removing whitespace
        string[] lines = (_textToProcessForPics).Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        string text = "";
        string imagePrompt = ""; // Initialize imagePrompt to avoid an uninitialized variable error
        string title = "";

        string forceKey = "";

        foreach (string line in lines)
        {
            string key = "";
            string value = "";

            if (forceKey.Length > 0)
            {
                key = forceKey;
                value = line;
                forceKey = "";
            }
            else
            {
                // Separate the string by the : character
                string[] parts = line.Split(':');
                if (parts.Length >= 2)
                {
                    key = parts[0].Trim().ToLower(); // Convert the key to lowercase and remove any whitespace

                    //set value to the combined text of all parts except parts[0], and keeping the : characters (not including the first one)
                    value = string.Join(":", parts, 1, parts.Length - 1);

                    value = value.Trim(); // Trim any whitespace from the value
                }
            }

            if (key == "text")
            {
                //remove any possible line feeds in front of the data in value
                value = value.TrimStart();

                //if value is blank, set value to the contents of the next line. Let's artifically advance the foreach to the next line
                if (value.Length == 0)
                {
                    forceKey = key;
                    continue;
                }

                text = value;

            } //we also need to check for "title"
            else if (key == "title")
            {
                //remove any possible line feeds in front of the data in value
                value = value.TrimStart();

                //if value is blank, set value to the contents of the next line. Let's artifically advance the foreach to the next line
                if (value.Length == 0)
                {
                    forceKey = key;
                    continue;
                }

                title = value;

            }
            else if (key == "image_prompt" || key == "image")
            {
                //if value is blank, set value to the contents of the next line. Let's artifically advance the foreach to the next line
                if (value.Length == 0)
                {
                    forceKey = key;
                    continue;
                }

                imagePrompt = value;
                
                int count = 1;
                
                
                int.TryParse(m_inputRenderCount.text, out count);


                for (int i = 0; i < count; i++)
                {
                    StartCoroutine(AddPicture(text, imagePrompt, title, _curPicIdx++, (RTRendererType)m_rendererSelectionDropdown.value));
                    yield return null;
                }

                 if (m_generateExtraCheckbox.isOn)
                {
                    //for each non-busy local gpu, add 1 to count
                    for (int i = 0; i < Config.Get().GetGPUCount(); i++)
                    {
                        if (Config.Get().GetGPUInfo(i).isLocal && !Config.Get().IsGPUBusy(i))
                        {
                            StartCoroutine(AddPicture(text, imagePrompt, title, _curPicIdx++,
                                Config.Get().GetGPUInfo(i)._requestedRendererType));
                            yield return null;
                        }
                    }
                }
                
                text = "";
                imagePrompt = "";
                title = "";
            }
        }
    }


    //This is called by the ProcessReponse button from the GUI
    public void OnProcessOutput()
    {

        if (m_autoSave.isOn || m_autoSaveJPG.isOn)
        {
            SetupRandomAutoSaveFolder();
        }
        else
        {
            m_bCreatedRandomAutoSaveFolder = false;
        }

        //the user might have cut and pasted text in, so let's figure it out again //OPTIMIZE;  We could just detect the paste...
        if (FigureOutResponseTextType(_inputPromptOutput.text) == ResponseTextType.text)
        {
            _textToProcessForPics = _inputPromptOutput.text;

            //processing as text, no fancy json being used
            StartCoroutine(ProcessTextFileBasic());
        }
        else
        {
            //using json, gp4 is good at this
            //StartCoroutine(ProcessJSONFile());
        }

    }

    public void OnTurnOffSmoothing(GameObject entity)
    {
        PicMain picMain = entity.GetComponent<PicMain>();
        picMain.m_pic.sprite.texture.filterMode = FilterMode.Point;
    }
    public bool IsSaving()
    {
        return m_autoSave.isOn || m_autoSaveJPG.isOn;
    }
    public void OnSaveIfNeeded(GameObject entity)
    {
        PicMain picMain = entity.GetComponent<PicMain>();

        if (IsSaving() && picMain.IsMovie())
        {
            if (!m_bCreatedRandomAutoSaveFolder)
            {
                SetupRandomAutoSaveFolder();
            }

            picMain.m_picMovie.SaveMovieWithNewFilename(picMain.m_aiPassedInfo.m_forcedFileNameBase);
        }
        
        if (picMain.m_aiPassedInfo.m_forcedFilename != null && picMain.m_aiPassedInfo.m_forcedFilename.Length > 0)
        {
            picMain.AddTextLabelToImage(m_extractor.ImageTextOverlay);
            if (!m_bCreatedRandomAutoSaveFolder)
            {
                SetupRandomAutoSaveFolder();
            }

            picMain.SaveFile(picMain.m_aiPassedInfo.m_forcedFilename, "", null, "", true);
        }

        if (picMain.m_aiPassedInfo.m_forcedFilenameJPG != null && picMain.m_aiPassedInfo.m_forcedFilenameJPG.Length > 0)
        {
            picMain.AddTextLabelToImage(m_extractor.ImageTextOverlay);
            if (!m_bCreatedRandomAutoSaveFolder)
            {
                SetupRandomAutoSaveFolder();
            }

            picMain.SaveFileJPG(picMain.m_aiPassedInfo.m_forcedFilenameJPG, "", null, "", Config.Get().GetJPGSaveQuality());
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

        tex = RTUtil.RenderTextToTexture2D(picMain.m_aiPassedInfo.m_text, (int)picMain.m_aiPassedInfo.m_textAreaRect.width,
           (int)(picMain.m_aiPassedInfo.m_textAreaRect.height - titleHeight), GetFontByID(picMain.m_aiPassedInfo.m_textFontID), fontSize * picMain.m_aiPassedInfo.m_fontSizeMod, picMain.m_aiPassedInfo.m_textColor,
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

            tex = RTUtil.RenderTextToTexture2D(picMain.m_aiPassedInfo.m_title, (int)picMain.m_aiPassedInfo.m_textAreaRect.width,
            (int)titleHeight, GetFontByID(picMain.m_aiPassedInfo.m_titleFontID), fontSize, RTUtil.GetARandomBrightColor(), true, vTextAreaSizeMod);


            File.WriteAllBytes("crap.png", tex.EncodeToPNG()); //for debugging

            rect = picMain.m_aiPassedInfo.m_textAreaRect;
            rect.yMax = titleHeight;
            //add it to our real texture
            picMain.m_pic.sprite.texture.BlitWithAlpha((int)(rect.xMin), (int)(picMain.m_pic.sprite.texture.height - picMain.m_aiPassedInfo.m_textAreaRect.height), tex, 0, 0, (int)rect.width, (int)rect.height);
            picMain.m_pic.sprite.texture.Apply();
        }
    }

    void OnGTP4CompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        SetStartTextOnStartButton(true);
        m_bTalkingToLLM = false;
        if (jsonNode == null && streamedText.Length == 0)
        {
            //must have been an error
            RTConsole.Log("OpenAI API error! Data: " + db.ToString());
            SetStatus("Error sending! Check the log.", false);

            RTQuickMessageManager.Get().ShowMessage(db.GetStringWithDefault("msg", "Unknown"));
            GameLogic.Get().ShowConsole(true);
            return;
        }

        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */



        string reply;

        if (jsonNode == null)
        {
            //text was streamed, we handle it differently
            reply = streamedText;

            //the last text/image combo would have been ignored, so let's process it now

            _textToProcessForPics = accumulatedText;
            StartCoroutine(ProcessTextFileBasic());
            string picText = GetPicFromText(ref accumulatedText);
        }
        else
        {
            reply = jsonNode["choices"][0]["message"]["content"];
            Debug.Log(reply);
        }

        SetStatus("Generated "+ m_llmGenerationCounter+" "+ m_statusStopMessage, false);
        //let's add our original prompt to it, I assume that's what we want
        GenericProcessingAfterGettingText(streamedText);

        //ModifyPromptIfNeeded();

    }

    ResponseTextType FigureOutResponseTextType(string text)
    {

        return ResponseTextType.text;

        /*
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
        */
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
            string path = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + folderName;
            //save it, make sure to overwrite anything already there
            File.WriteAllText(path, _inputPromptOutput.text);
        }

    }
    
    void GenericProcessingAfterGettingText(string streamedText)
    {
        m_bIsPossibleToContinue = true;
        _promptManager.AddInteraction("assistant", streamedText);
    }

    void OnTexGenCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        m_bTalkingToLLM = false;
        SetStartTextOnStartButton(true);

        if (jsonNode == null && streamedText.Length == 0)
        {
            //must have been an error
            RTConsole.Log("Generic LLM error! Data: " + db.GetStringWithDefault("msg", "Unknown"));
            SetStatus("Error sending! Check the log.", false);
            RTQuickMessageManager.Get().ShowMessage(db.GetStringWithDefault("msg", "Unknown"));
            GameLogic.Get().ShowConsole(true);
            return;
        }

        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */
        string reply;

        if (jsonNode == null)
        {
            //text was streamed, we handle it differently
            reply = streamedText;

            //the last text/image combo would have been ignored, so let's process it now

            _textToProcessForPics = accumulatedText;
            StartCoroutine(ProcessTextFileBasic());
            string picText = GetPicFromText(ref accumulatedText);

        }
        else
        {
            reply = jsonNode["choices"][0]["message"]["content"];
            Debug.Log(reply);
        }

        //_inputPromptOutput.text += reply;
        //SetStatus("Success, got reply. " + m_statusStopMessage, false);
        SetStatus("Generated " + m_llmGenerationCounter + " " + m_statusStopMessage, false);

        GenericProcessingAfterGettingText(streamedText);
        //ModifyPromptIfNeeded();
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


    public void OnProfileEditButton()
    {
        RTNotepad notepadScript = RTNotepad.OpenFile(LoadGuideProfile(GetActiveProfileTextFileName()), m_notepadTemplatePrefab);
        notepadScript.m_onClickedSavedCallback += OnProfileSaved;
        notepadScript.m_onClickedCancelCallback += OnProfileCanceled;
        notepadScript.SetApplyButtonVisible(true);
        notepadScript.m_onClickedApplyCallback += OnProfileApply;
    }

    void OnProfileSaved(string text)
    {
        //Debug.Log("They clicked save.  Text entered: " + text);
        SaveGuideProfile(text, GetActiveProfileTextFileName());
        ProcessConfigText(text);
    }

    void OnProfileCanceled(string text)
    {
        Debug.Log("They clicked cancel.  Text entered: " + text);
    }
    void OnProfileApply(string text)
    {
        ProcessConfigText(text);
    }
    void PopulateProfilesDropDown()
    {
        m_presetDropdown.ClearOptions();
        //first delete everything from the dropdown

        //load the adventure files
        string[] files = Directory.GetFiles("AIGuide", "*.txt");

        foreach (string file in files)
        {
            //add this string to the dropdown
            string name = Path.GetFileName(file);
            List<string> options = new List<string>();
            options.Add(name);
            m_presetDropdown.AddOptions(options);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        m_statusStopMessage = m_statusText.text;

        PopulateDropdownWithFonts(m_textFontDropdown, 0);
        PopulateDropdownWithFonts(m_titleFontDropdown, 1);
        PopulateProfilesDropDown();


        Config.Get().PopulateRendererDropDown(m_rendererSelectionDropdown);
    }

    public string GetActiveProfileTextFileName()
    {
        return m_presetDropdown.options[m_presetDropdown.value].text;
    }
    public string LoadGuideProfile(string fileName)
    {
        string finalFileName = "AIGuide/" + fileName;

        try
        {
            using (System.IO.StreamReader reader = new System.IO.StreamReader(finalFileName))
            {
                return reader.ReadToEnd();
            }

        }
        catch (FileNotFoundException e)
        {
            RTConsole.Log("Guide config " + finalFileName + " not found. (" + e.Message + ")");
        }

        return "";

    }

    public void SaveGuideProfile(string text, string fName)
    {
        string finalFileName = "AIGuide/" + fName;

        try
        {
            using (System.IO.StreamWriter writer = new System.IO.StreamWriter(finalFileName))
            {
                writer.Write(text);
            }
        }
        catch (Exception e)
        {
            RTConsole.Log("Failed to save config " + finalFileName + ". Error: " + e.Message);
        }
    }


    public void LoadAndProcessConfig()
    {
        string configTxt = LoadGuideProfile(GetActiveProfileTextFileName());

        if (configTxt.Count() == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("Error loading profile");
            return;
        }
        ProcessConfigText(configTxt);


    }

    public void ProcessConfigText(string configTxt)
    {

        m_extractor = new TextFileConfigExtractor();
        m_extractor.ExtractInfoFromString(configTxt);

        //now we need to set the values in the GUI
        _inputPrompt.text = m_extractor.BaseContext;
        //let's move the scrollbar to the bottom of the text
        _inputPromptScrollRect.value = 1.0f;

        _autoContinueTextInput.text = m_extractor.AutoContinueText;

        GameLogic.Get().SetPrompt(m_extractor.PrependPrompt);
        GameLogic.Get().SetComfyUIPrompt(m_extractor.PrependComfyUIPrompt);
        m_AddBordersCheckbox.isOn = m_extractor.AddBorders;
        m_OverlayTextCheckbox.isOn = m_extractor.OverlayText;
        m_BoldCheckbox.isOn = m_extractor.UseBoldFont;
        //set font to m_extractor.PreferredFontName

        /*
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
       
        */
    }
    public void OnPresetDropdownChanged()
    {
        int choice = m_presetDropdown.value;

        /*
        Debug.Log("Preset changed to index " + choice);
        if (choice == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("(leaving settings as they are)");
            return;
        }
        */

        RTQuickMessageManager.Get().ShowMessage("Changing settings to " + m_presetDropdown.options[choice].text);
        LoadAndProcessConfig();

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
            if (m_bFirstTimeToShow)
            {
                LoadAndProcessConfig();
                m_bFirstTimeToShow = false;
            }

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

                m_statusText.text = m_lastStatusMessage + " (" + secondsPassed.ToString("F0") + ") " + m_animPeriods;

                m_statusAnimTimer = Time.time + m_statusTimerIntervalInSeconds;
            }
        }

        if (m_bIsPossibleToContinue && !m_bTalkingToLLM)
        {
            if (m_autoModeCheckbox.isOn)
            {

               
                OnLLMContinueButton();
            }
            
        }


    }

}
