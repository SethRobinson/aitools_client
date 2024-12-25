using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using TMPro;
using UnityEngine.UI;
using UnityEngine.Rendering;

//enum of types of chat roles, system, user, and assistant
public enum ChatRole
{
    system,
    user,
    assistant
}

public enum AdventureMode
{
    DEFAULT,
    CHOOSE_YOUR_OWN_ADVENTURE,
    QUIZ
}

public enum eSpatialOrganizationMethod
{
    VERTICAL,
    TREE_SPLIT,
    TREE_BY_GENERATION
}



public class TextFileConfigExtractor
{
    public string BaseContext { get; private set; }
    public string StartMsg { get; private set; }
    public float Temperature { get; private set; }
    public string PrependPrompt { get; private set; }
    public string PrependComfyUIPrompt { get; private set; }
    public string SystemReminder { get; private set; }

    public string AutoContinueText { get; private set; }
    public bool AddBorders { get; private set; }
    public bool OverlayText { get; private set; }
    public bool UseBoldFont { get; private set; }
    public string PreferredFontName { get; private set; }
    public string Mode { get; private set; }
    public AdventureMode AdventureMode { get; private set; }
    public eSpatialOrganizationMethod SpatialOrganizationMethod { get; private set; }
    public string TwineStart { get; private set; }
    public string TwinePassage { get; private set; }
    public string TwineEnd { get; private set; }
    public string TwineImage { get; private set; }
    public string ImageTextOverlay { get; private set; }
    public string DefaultInput { get; private set; }
    public string QuizHTML { get; private set; }
    public string ImageWaterMarkText { get; private set; }
    public string TwineTextIfNoChoices { get; private set; }
  

    //this is kind of a lazy catch all for everything I need from config files.  They don't all have to be in a single .txt file

    private static readonly Dictionary<string, Action<TextFileConfigExtractor, string>> extractors = new Dictionary<string, Action<TextFileConfigExtractor, string>>()
            {
                { "base_context", (ce, data) => ce.BaseContext = data },
                { "system_reminder", (ce, data) => ce.SystemReminder = data },
                { "auto_continue_text", (ce, data) => ce.AutoContinueText = data },
                { "start_msg", (ce, data) => ce.StartMsg = data },
                { "prepend_prompt", (ce, data) => ce.PrependPrompt = data },
                { "prepend_comfyui_prompt", (ce, data) => ce.PrependComfyUIPrompt = data },
                { "temperature", (ce, data) => ce.Temperature = float.Parse(data.Trim()) },
                { "add_borders", (ce, data) => ce.AddBorders = ParseBool(data) },
                { "overlay_text", (ce, data) => ce.OverlayText = ParseBool(data) },
                { "default_input", (ce, data) => ce.DefaultInput = data },
                { "quiz_html", (ce, data) => ce.QuizHTML = data },
                { "use_bold_font", (ce, data) => ce.UseBoldFont = ParseBool(data) },
                { "preferred_font_name", (ce, data) => ce.PreferredFontName = data },
                { "image_watermark_text", (ce, data) => ce.ImageWaterMarkText = data },
                 { "image_text_overlay", (ce, data) => ce.ImageTextOverlay = data },
               { "mode", (ce, data) =>
                    {
                        ce.Mode = data;
                        if (data == "CHOOSE_YOUR_OWN_ADVENTURE")
                        {
                            ce.AdventureMode = AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE;
                        } else if (data == "QUIZ")
                        {
                            ce.AdventureMode = AdventureMode.QUIZ;
                        } else if (data == "DEFAULT")
                        {
                            ce.AdventureMode = AdventureMode.DEFAULT;
                        } else
                        {
                            throw new Exception("Invalid mode: " + data);
                        }
                    }
                },

        { "spatial_organization_method", (ce, data) =>
                    {
                        if (data == "VERTICAL")
                        {
                            ce.SpatialOrganizationMethod = eSpatialOrganizationMethod.VERTICAL;
                        } else if (data == "TREE_SPLIT")
                        {
                            ce.SpatialOrganizationMethod = eSpatialOrganizationMethod.TREE_SPLIT;
                        } else if (data == "TREE_BY_GENERATION")
                        {
                            ce.SpatialOrganizationMethod = eSpatialOrganizationMethod.TREE_BY_GENERATION;
                        } else
                        {
                            throw new Exception("Invalid spatial organization method: " + data);
                        }
                    }
                },

        //twine stuff
                          { "twine_start", (ce, data) => ce.TwineStart =data },
                          { "twine_passage", (ce, data) => ce.TwinePassage = data },
                          { "twine_image", (ce, data) => ce.TwineImage = data },
                          { "twine_end", (ce, data) => ce.TwineEnd = data },
                          { "twine_text_if_no_choices", (ce, data) => ce.TwineTextIfNoChoices = data }

            };

    public void ExtractInfoFromString(string text)
    {
        // Match COMMAND_START|...COMMAND_END blocks
        var commandBlocks = Regex.Matches(text, @"COMMAND_START\|(?<command>[^\n]+)\n(?<data>.*?)COMMAND_END", RegexOptions.Singleline);

        foreach (Match block in commandBlocks)
        {
            string command = block.Groups["command"].Value.Trim();
            string data = block.Groups["data"].Value.Trim();

            if (extractors.ContainsKey(command))
            {
                extractors[command](this, data);
            }
        }

        // Match COMMAND_SET|... lines
        var setCommands = Regex.Matches(text, @"COMMAND_SET\|(?<command>[^\|]+)\|(?<value>[^\|]+)");

        foreach (Match setCommand in setCommands)
        {
            string command = setCommand.Groups["command"].Value.Trim();
            string value = setCommand.Groups["value"].Value.Trim().Split('#')[0].Trim();

            if (extractors.ContainsKey(command))
            {
                extractors[command](this, value);
            }
        }
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
public class GenerationInfo
{
    public float leftMostPos;
    public float rightMostPos;
}

public class AdventureLogic : MonoBehaviour
{

    string m_json; //store this for requests so we don't have to compute it each time
    static AdventureLogic _this = null;
    Color m_oldBGColor;
  
    public GameObject _adventureTextPrefab;
    public GameObject m_notepadTemplatePrefab; //(attach to RTNotepad prefab)
    bool m_ranFirstTimeStuff;
    GameObject _lastAdventureTextSpawned = null;
    string m_configText = "";
    AdventureText _highlightedAText = null; //where we are in the conversation

    //create list of GenerationInfo (with no layers, we'll add them as we need them)
    List<GenerationInfo> _generationInfo = new List<GenerationInfo>();

    float _additionalImageTimer = 0.0f;
    public TMP_Dropdown m_profileDropdown;
    TextFileConfigExtractor extractor = new TextFileConfigExtractor();
    public TMP_InputField m_forcedComfyUIPicsInputField;
    public TMP_InputField m_stopAfterInputField;

    public TMP_InputField m_reminderCountInputField;
    public TMP_InputField m_LLMAtOnceInputField;
    public TMP_Dropdown m_llmSelectionDropdown;
    public TMP_Dropdown m_rendererSelectionDropdown;
    int m_totalLLMGenerationCounter = 0;

    public AdventureMode GetMode()
    {
        return extractor.AdventureMode;
    }

    public LLM_Type GetLLMType()
    {
        return (LLM_Type)m_llmSelectionDropdown.value;
    }
    bool _bIsActive = false;
    public UnityEngine.UI.Toggle m_genExtraToggle;
    public string GetAdventureName() { return "My Adventure"; }
    int _llmRequestCount = 0;
    int GetMaxLLMRequestsDesiredAtOnce()
    {
        //tryparse m_LLMAtOnceInputField to a number safely and return it
        return int.TryParse(m_LLMAtOnceInputField.text, out int result) ? result : 0;
    }
    public Toggle _bUseGlobalPrompts;

    bool _bForceUniquePassageNames = false;
    int m_generationsSinceLastReminder = 0;
    public bool GetForceUniquePassageNames() { return _bForceUniquePassageNames; }
    public void ModLLMRequestCount(int mod) { _llmRequestCount += mod; }
    public int GetLLMRequestCount() { return _llmRequestCount; }
    public bool CanInitNewLLMRequest()
    {
        return _llmRequestCount < GetMaxLLMRequestsDesiredAtOnce();
    }

    public GPTPromptManager GetGlobalPromptManager() { return m_globalPromptManager; }

    public GPTPromptManager m_globalPromptManager; //all the prompts in one giant prompt, used for certain things

    AdventureText _lastPicOwner;

    private void Awake()
    {
        _this = this;
    }

    public void Start()
    {
        m_globalPromptManager = gameObject.AddComponent<GPTPromptManager>();
        PopulateProfilesDropDown();
        Config.Get().PopulateRendererDropDown(m_rendererSelectionDropdown);
    }
    
    public bool IsActive()
    {
        return _bIsActive;
    }

    public Vector3 GetNewPositionByGenerationOnRight(int gen)
    {
        Vector3 vNewPos = new Vector3(0, 0, 0);
        float spacerX = 1.0f;
        float spacerY = 0.1f;
        float startingY = 7;

        vNewPos.y = startingY - (gen * ((5.12f * 2) + spacerY));

        //make sure _generationInfo has enough layers (we'll need to access it by index, gen in this case)
        while (_generationInfo.Count <= gen)
        {
            _generationInfo.Add(new GenerationInfo());
        }

        GenerationInfo gi = _generationInfo[gen];
        vNewPos.x = gi.rightMostPos;

        gi.rightMostPos += 5.12f * spacerX;

        return vNewPos;
    }
    public static AdventureLogic Get() { return _this; }

    public string LoadConfig(string fName)
    {

        string finalFileName = "Adventure/" + fName;

        try
        {
            using (System.IO.StreamReader reader = new System.IO.StreamReader(finalFileName))
            {
                return reader.ReadToEnd();
            }

        }
        catch (FileNotFoundException e)
        {
            RTConsole.Log("Adventure config " + finalFileName + " not found. (" + e.Message + ")");
        }

        return "";
    }

    public void SaveConfig(string text, string fName)
    {
        string finalFileName = "Adventure/" + fName;

        try
        {
            using (System.IO.StreamWriter writer = new System.IO.StreamWriter(finalFileName))
            {
                writer.Write(text);
            }
        }
        catch (Exception e)
        {
            RTConsole.Log("Failed to save adventure config " + finalFileName + ". Error: " + e.Message);
        }
    }
    public void OnExport()
    {
        if (GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
        {

            //show a message to the screen
            RTQuickMessageManager.Get().ShowMessage("Exporting for Twine (.twee)");
            //Find the AdventureExportTwine on our gameobject
            AdventureExportTwine exportScript = gameObject.GetComponent<AdventureExportTwine>();
            StartCoroutine(exportScript.Export());
            return;
        }

        if (GetMode() == AdventureMode.QUIZ)
        {

            //show a message to the screen
            RTQuickMessageManager.Get().ShowMessage("Exporting HTML Quiz");
            //Find the AdventureExportTwine on our gameobject
            AdventureExportQuiz exportScript = gameObject.GetComponent<AdventureExportQuiz>();
            StartCoroutine(exportScript.Export());
            return;
        }

        RTQuickMessageManager.Get().ShowMessage("Can't export in this mode.  Try CYOA or Quiz adventure.txt files.");

    }
    public void OnNewStory()
    {

        //clear all the text objects
        RTUtil.DestroyChildren(RTUtil.FindObjectOrCreate("Adventures").transform);
        //clear all pics
        GameLogic.Get().KillAllPics(true);
        m_globalPromptManager.Reset();
        m_totalLLMGenerationCounter = 0;
        _generationInfo = new List<GenerationInfo>();
        LoadAndRunAdventure();
    }

    public void UpdateAdventureFile(string text)
    {
        m_configText = text;
        extractor = new TextFileConfigExtractor();
        extractor.ExtractInfoFromString(m_configText);
    }

    public TextFileConfigExtractor GetExtractor() { return extractor; }
    public void LoadAndRunAdventure()
    {
        //m_configText = LoadConfig(fName);

        extractor = new TextFileConfigExtractor();
        extractor.ExtractInfoFromString(m_configText);
        GameLogic.Get().SetPrompt(extractor.PrependPrompt);
        GameLogic.Get().SetComfyUIPrompt(extractor.PrependComfyUIPrompt);

        AdventureText aText = AddText(extractor.StartMsg);
        aText.SetDontSendTextToLLM(true);
        aText.GetPromptManager().AddInteraction(Config.Get().GetAISystemWord(), extractor.BaseContext);
        //aText.GetPromptManager().AddInteraction(m_assistantName, extractor.StartMsg);
        m_generationsSinceLastReminder = 0;
        m_globalPromptManager.CloneFrom(aText.GetPromptManager());

        aText.SetIsSelected();
        aText.SetConfigFileName(GetActiveAdventureTextFileName());
        aText.SetName("S0");

        //move the camera to a good spot
        Camera.main.transform.position = new Vector3(1.680196f, 1.600914f, Camera.main.transform.position.z);
        //also set the ortho size
        Camera.main.orthographicSize = 2.381965f;

    }

    public string GetActiveAdventureTextFileName()
    {
        return m_profileDropdown.options[m_profileDropdown.value].text;
    }

    public void OnProfileEditButton()
    {

        RTNotepad notepadScript = RTNotepad.OpenFile(m_configText, m_notepadTemplatePrefab);

        notepadScript.m_onClickedSavedCallback += OnProfileSaved;
        notepadScript.m_onClickedCancelCallback += OnProfileCanceled;

        notepadScript.SetApplyButtonVisible(true);
        notepadScript.m_onClickedApplyCallback += OnProfileApply;
    }

    void OnProfileSaved(string text)
    {

        //Debug.Log("They clicked save.  Text entered: " + text);
        SaveConfig(text, GetActiveAdventureTextFileName());
        UpdateAdventureFile(text);
    }
    void OnProfileCanceled(string text)
    {

    }
    void OnProfileApply(string text)
    {
        UpdateAdventureFile(text);
    }
    void PopulateProfilesDropDown()
    {
        m_profileDropdown.ClearOptions();
        //first delete everything from the dropdown

        //load the adventure files
        string[] files = Directory.GetFiles("Adventure", "*.txt");

        foreach (string file in files)
        {
            //add this string to the dropdown
            string name = Path.GetFileName(file);
            List<string> options = new List<string>();
            options.Add(name);
            m_profileDropdown.AddOptions(options);
        }
    }
    public void OnStartGameMode()
    {

        GameLogic.Get().SetToolsVisible(false);
        //ImageGenerator.Get().SetGenerate(false);
        // GameLogic.Get().OnClearButton();

        // GameLogic.Get().OnFixFacesChanged(false); //don't want faces on our Adventure
        //GameLogic.Get().SetInpaintStrength(1.0f);
        GameLogic.Get().SetSeed(-1); //make sure it's random
        //GameLogic.Get().SetAlphaMaskFeatheringPower(20);
        //GameLogic.Get().SetMaskContentByName("latent noise");
        RTUtil.FindObjectOrCreate("AdventureGUI").SetActive(true);
        m_oldBGColor = Camera.allCameras[0].backgroundColor;
        Camera.allCameras[0].backgroundColor = Color.black;

        //save the json request, we can re-use it for each Adventure
        // m_json = GamePicManager.Get().BuildJSonRequestForInpaint("Adventure, top view",  m_negativePrompt, m_templateTexture, m_alphaTexture, false);

        _bIsActive = true;
      
        if (!m_ranFirstTimeStuff)
        {
            m_ranFirstTimeStuff = true;

            // LoadAndRunAdventure(GetActiveAdventureTextFileName());

            //Set camera x/y and "size" to 2, don't change its z
            Vector3 vOriginalCamPos = Camera.main.transform.position;

            Camera.main.transform.position = new Vector3(0.1308768f, 1.20859f, vOriginalCamPos.z);
            Camera.main.orthographicSize = 2;

        }

        m_configText = LoadConfig(GetActiveAdventureTextFileName());

    }

    public void OnAdventureDropdownChanged()
    {
        UpdateAdventureFile(LoadConfig(GetActiveAdventureTextFileName()));
    }

    public int GetRenderCount()
    {
        return int.TryParse(m_forcedComfyUIPicsInputField.text, out int result) ? result : 0;
    }

    public int GetStopAfter()
    {
        return int.TryParse(m_stopAfterInputField.text, out int result) ? result : 0;
    }

    public RTRendererType GetRenderer()
    {
        return (RTRendererType)m_rendererSelectionDropdown.value;
    }
    public void OnEndGameMode()
    {
        Camera.allCameras[0].backgroundColor = m_oldBGColor;
        //GameLogic.Get().OnClearButton();
        GameLogic.Get().SetToolsVisible(true);
        //RTUtil.DestroyChildren(RTUtil.FindObjectOrCreate("Adventures").transform);
        RTUtil.FindObjectOrCreate("AdventureGUI").SetActive(false);
        RTAudioManager.Get().StopMusic();
        _bIsActive = false;
    }

    public AdventureText AddText(string text)
    {

        Debug.Log("Launch a text object");
        // Spawn the text prefab, get a reference to its AdventureText script and set its text
        GameObject go = Instantiate(_adventureTextPrefab, RTUtil.FindObjectOrCreate("Adventures").transform);

        //set default position, without changing its z
        go.transform.position = new Vector3(1, 0.5f, go.transform.position.z);


        AdventureText at = go.GetComponent<AdventureText>();
        at.SetText(text);
        _lastAdventureTextSpawned = go;
        return at;
    }

    public void OnGotFinalResponse(AdventureText textScript, string response)
    {
        //add it to our thing
        //_promptManager.AddInteraction("assistant", response);
    }

    string AddReminderIfNeeded(string text)
    {
        //convert m_reminderCountInputField to int with TryParse
        if (int.TryParse(m_reminderCountInputField.text, out int result))
        {
            if (result > 0)
            {
                m_generationsSinceLastReminder++;
                if (m_generationsSinceLastReminder >= result)
                {
                    m_generationsSinceLastReminder = 0;
                    text += "\n" + GetExtractor().SystemReminder;
                }
            }
        }
        return text;
    }

    public void ResetGenerationCounter()
    {
        m_totalLLMGenerationCounter = 0;
    }
    public AdventureText AddTextAndGetReply(string text, AdventureText textScript, bool bDontCreateReplyBox = false)
    {
        m_totalLLMGenerationCounter++;

        if (m_totalLLMGenerationCounter >= GetStopAfter() && GetStopAfter() > 0)
        {
            RTQuickMessageManager.Get().ShowMessage("Stopped after " + GetStopAfter() + " generations.");
            //set the max llms and renders to 0
            m_LLMAtOnceInputField.text = "0";
            m_forcedComfyUIPicsInputField.text = "0";
            m_totalLLMGenerationCounter = 0;
        }
        if (_bUseGlobalPrompts.isOn)
        {
            return AddTextAndGetReplyGlobal(text, textScript, bDontCreateReplyBox);
        }
        //before we do anything, there is a chance our text was edited. //OPTIMIZE:  We could just set a flag if edited...

        //First, we'll remove and re-add the last thing set in us
        textScript.UpdateLastInteraction();
        AdventureText newText;
        bool bCreatedResponseBox = false;

        if (textScript.GetPromptManager().GetLastInteraction()._role == "user")
        {
            //we are a user prompt, so the next thing will be an AI response
            newText = AddText("");
            newText.GetPromptManager().CloneFrom(textScript.GetPromptManager());
            newText.transform.position = textScript.GetBottomWorldPosition();
            newText.SetConfigFileName(textScript.GetConfigFileName());
            if (GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
            {
                //this must be the starting node
                newText.SetName("ADVENTURE-START");
            }
        }
        else
        {
            //we are an AI response, so 
            text = AddReminderIfNeeded(text);
            //Debug.Log("Text submitted: " + text);
            Vector3 vReplyBoxPos = textScript.GetBottomWorldPosition();

            if (bDontCreateReplyBox == false)
            {
                AdventureText myText = AddText(text);
                bCreatedResponseBox = true;
                myText.SetUserCreated(true);
                myText.GetPromptManager().CloneFrom(textScript.GetPromptManager());
                myText.GetPromptManager().AddInteraction("user", text);
                myText.SetConfigFileName(textScript.GetConfigFileName());
                myText.gameObject.transform.position = vReplyBoxPos;
                vReplyBoxPos = myText.GetBottomWorldPosition();
            }

            newText = AddText("");
            newText.GetPromptManager().CloneFrom(textScript.GetPromptManager());

            newText.GetPromptManager().AddInteraction("user", text);
            newText.SetGenerationCount(textScript.GetGenerationCount() + 1);
            /*
            if (extractor.SystemContext != null && extractor.SystemContext.Length > 3)
            {

                //myText.GetPromptManager().AddInteraction("user", extractor.SystemContext);
                newText.GetPromptManager().RemoveInteractionsByInternalTag("reminder");
                newText.GetPromptManager().AddInteraction("user", extractor.SystemContext, "reminder");
            }
            */

            newText.transform.position = vReplyBoxPos;
            newText.SetConfigFileName(textScript.GetConfigFileName());
        }

        newText.StartLLMRequest();
        newText.SetDirectionMult(textScript.GetReverseDirectionMult());
        //_promptManager.AddInteraction("user", text);
        // If _lastAdventureTextSpawned is not null, move the spawn position down so they won't overlap.

        if (bCreatedResponseBox && GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
        {
            //this must be the starting node
            newText.SetName("ADVENTURE-START");
        }

        return newText;
    }

    public AdventureText AddTextAndGetReplyGlobal(string text, AdventureText textScript, bool bDontCreateReplyBox = false)
    {
        AdventureText newText;
        bool bCreatedResponseBox = false;

        if (textScript.GetPromptManager().GetLastInteraction()._role == "user")
        {
            //we are a user prompt, so the next thing will be an AI response
            newText = AddText("");
            newText.GetPromptManager().CloneFrom(m_globalPromptManager);
            newText.transform.position = textScript.GetBottomWorldPosition();
            newText.SetConfigFileName(textScript.GetConfigFileName());

            if (GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
            {
                //this must be the starting node
                newText.SetName("ADVENTURE-START");
            }
        }
        else
        {

            text = AddReminderIfNeeded(text);
            //we are an AI response, so 
            m_globalPromptManager.AddInteraction("user", text);

           // Debug.Log("Text submitted: " + text);
            Vector3 vReplyBoxPos = textScript.GetBottomWorldPosition();

            if (bDontCreateReplyBox == false)
            {
                AdventureText myText = AddText(text);
                bCreatedResponseBox = true;
                myText.SetUserCreated(true);
                myText.GetPromptManager().CloneFrom(textScript.GetPromptManager());

                myText.SetConfigFileName(textScript.GetConfigFileName());
                myText.gameObject.transform.position = vReplyBoxPos;
                vReplyBoxPos = myText.GetBottomWorldPosition();
            }

            newText = AddText("");
            newText.GetPromptManager().CloneFrom(m_globalPromptManager);
            newText.SetGenerationCount(textScript.GetGenerationCount() + 1);

            newText.transform.position = vReplyBoxPos;
            newText.SetConfigFileName(textScript.GetConfigFileName());
        }

        newText.StartLLMRequest();
        newText.SetDirectionMult(textScript.GetReverseDirectionMult());

        if (bCreatedResponseBox && GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
        {
            //this must be the starting node
            newText.SetName("ADVENTURE-START");
        }

        return newText;
    }
    public void SetSelected(AdventureText text)
    {
        if (text == _highlightedAText) return;

        if (_highlightedAText != null)
        {
            _highlightedAText.SetUnselected();
        }
        _highlightedAText = text;
    }

    public void UnselectTextIfNeeded(AdventureText text)
    {
        if (_highlightedAText == text)
        {
            _highlightedAText = null;
        }
    }

    public void OnTextDeleted(AdventureText text)
    {
        UnselectTextIfNeeded(text);
        if (_lastAdventureTextSpawned == text)
        {
            _lastAdventureTextSpawned = null;
        }

        if (_lastPicOwner == text)
        {
            _lastPicOwner = null;
        }
    }

    public bool OnTextSubmitted(string text)
    {
        if (!_highlightedAText)
        {
            //show an error message to the user
            RTQuickMessageManager.Get().ShowMessage("Can't reply, no text window is selected.  Find one and click \"Make Active\" or hit the New button");
            return false;
        }

        if (_highlightedAText.GetIsUserCreated())
        {
            RTQuickMessageManager.Get().ShowMessage("Can't reply to a reply, just edit the existing reply and click Auto.");
            return false;
        }

        text = text.TrimEnd('\n');
        string textNoLinefeeds = text.Replace("\n", " ");
        if (textNoLinefeeds == "" || textNoLinefeeds == " ")
        {
            text = extractor.AutoContinueText;
        }

        var newText = AddTextAndGetReply(text, _highlightedAText);

        newText.SetIsSelected();
        return true;
    }

    public void SetLastPicTextAndOwner(AdventureText owner)
    {
        _lastPicOwner = owner;
    }

    void LateUpdate() //late update, so pics have a chance to use the GPUs before we check if any are free
    {

        if (!IsActive()) return;

        if (m_genExtraToggle.isOn)
        {
            if (_additionalImageTimer < Time.time)
            {
                //check if a GPU is free
                if (Config.Get().IsAnyGPUFree())
                {
                    if (_lastPicOwner != null)
                    {
                        RTConsole.Log("GPU not busy, spawning more");
                        _lastPicOwner.RenderAnotherPic(RTRendererType.Any_Local);
                    }

                }
                _additionalImageTimer = Time.time + 0.3f;
            }
        }

    }

}
