/*
Source code by Seth A. Robinson
 */

//#define RT_NOAUDIO

using UnityEngine;
using System;
using System.IO;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;


public class GameLogic : MonoBehaviour
{
    public GameObject m_notepadTemplatePrefab;
    string m_prompt = "";
    string m_comfyPrependPrompt = "";
    string m_comfyAppendPrompt = "";
    string m_negativePrompt = "";
    int m_steps = 50;
    long m_seed = -1;
    bool m_bRandomizePrompt = false;
    bool m_bAutoSave = false;
    bool m_bAutoSavePNG = false;
    bool m_bCameraFollow = false;
    bool m_bControlNetSupportExists = false;
    int m_controlNetMaxModels = 1; //the minimum possible, max is like 5 or something, we'll read
    //from the server which it is.  If we were smart we'd say 0 means no support but hey, I'm adding
    //this later
    bool m_bUseControlNet = false;

    int m_maxToGenerate = 1000;
    string m_lastModifiedPrompt;
    public TMP_Dropdown m_rendererSelectionDropdown;
    float m_upscale = 1.0f; //1 means no change
    bool m_fixFaces = false;
    bool m_hiresFix = false;
    float m_textStrength = 7.5f;
    float m_controlNetWeight = 1.0f;
    float m_pix2pixtextStrength = 1.5f;
    float m_inpaintStrength = 0.80f;
    float m_extraNoiseStrength = 0;
    float m_penSize = 20;
    float m_controlNetGuidance = 1.0f;
    public TMP_InputField m_inputField;
    public TMP_InputField m_negativeInputField;
    public TMP_InputField m_stepsInputField;
    public TMP_InputField m_seedInputField;
    public TMP_Text m_activeLLMLabel;
    public TMP_InputField m_comfyAppendPromptInputField;
    public TMP_InputField m_comfyPrependPromptInputField;
    public TMP_InputField m_jobListInputField;
    public Slider m_inpaintStrengthInput;
    public Slider m_maskBlendingInput;
    public Slider m_Pix2PixSlider;

    public Slider m_textStrengthSlider;
    public Slider m_pix2pixTextStrengthSlider;

    public Button m_generateButton;
    public Toggle m_fixFacesToggle;
    public Toggle m_upscaleToggle;
    public Toggle m_tilingToggle;
    public Toggle m_removeBackgroundToggle;
    public Toggle m_useControlNetToggle;
    public Toggle m_hiresFixToggle;
    public Toggle m_turboToggle;
  
    string m_defaultControlNetProcessor = "depth";
    string m_defaultControlNetModel = "sd15_depth";

    float m_alphaMaskFeatheringPower = 0;
    bool m_bLoopSource = false;
    bool m_inpaintMaskActive = false;
    int m_picsPerRow = 20;
    bool m_bTiling = false;
    int m_genWidth = 512;
    int m_genHeight = 512;
    bool m_bRemoveBackground = false;

    public ImageGenerator m_AIimageGenerator;

    public Slider m_penSlider;
    public TMP_Dropdown m_maskedContentDropdown;
    public TMP_Dropdown m_widthDropdown;
    public TMP_Dropdown m_heightDropdown;
    public TMP_Dropdown m_samplerDropdown;

    public TMP_Dropdown m_modelDropdown;
    string m_activeModelName = "";

    public TMP_Dropdown m_comfyUIAPIWorkflowsDropdown;
    public TMP_Dropdown m_presetDropdown;
    public TMP_Dropdown m_tempPresetDropdown;


    public TMP_Dropdown m_refinerModelDropdown;
    public TMP_InputField m_refinerInputField;
    public GameObject m_controlNetPanelPrefab;

    List<String> m_controlNetPreprocessorArray = new List<String>();
    List<String> m_controlNetModelArray = new List<String>();
    int m_controlNetPreprocessorCurIndex;
    int m_controlNetModelCurIndex;
    public TMP_Dropdown m_llmSelectionDropdown;
    public GameObject m_tempPic1;

    // On startup, GameLogic may initialize before LLMSettingsManager exists/loads settings.
    // We'll retry briefly so the Tools panel label reflects the real active LLM.
    private int _activeLLMLabelInitTries = 0;
    private const int ACTIVE_LLM_LABEL_MAX_INIT_TRIES = 30; // ~7.5 seconds at 0.25s interval
  
    public enum eGameMode
    {
        NORMAL,
        EXPERIMENT
    }

    public TMP_Dropdown GetLLMSelection()
    {
        return m_llmSelectionDropdown;
    }

    eGameMode m_gameMode = eGameMode.NORMAL;
    public eGameMode GetGameMode() { return m_gameMode; }
    public void SetGameMode(eGameMode gameMode) { m_gameMode = gameMode; }

    public GameObject GetTempPic1() { return m_tempPic1; }
    static GameLogic _this = null;
    static public GameLogic Get()
    {
        return _this;
    }
    public static string GetName()
    {
        return Get().name;
    }
    public RTRendererType GetGlobalRenderer() { return (RTRendererType)m_rendererSelectionDropdown.value; }
    public int GetMaxToGenerate() { return m_maxToGenerate; }
    public void SetMaxToGenerate(int max) { m_maxToGenerate = max; }
    public bool GetTurbo() { return m_turboToggle.isOn; }
    public bool GetRandomizePrompt() { return m_bRandomizePrompt; }
    public void SetRandomizePrompt(bool bNew) { m_bRandomizePrompt = bNew; }

    public bool GetAutoSave() { return m_bAutoSave; }
    public bool GetAutoSavePNG() { return m_bAutoSavePNG; }
    public void SetAutoSave(bool bNew) { m_bAutoSave = bNew; }
    public void SetAutoSavePNG(bool bNew) { m_bAutoSavePNG = bNew; }
    public bool GetAnyAutoSave() { return m_bAutoSave || m_bAutoSavePNG; }

    public bool GetCameraFollow() { return m_bCameraFollow; }
    public void SetCameraFollow(bool bNew) { m_bCameraFollow = bNew; }

    public int GetGenWidth() { return m_genWidth; }

    public int GetGenHeight() { return m_genHeight; }

    public void OnGenWidthDropdownChanged()
    {
        int.TryParse(m_widthDropdown.options[m_widthDropdown.value].text, out m_genWidth);
    }

    public void SetWidthDropdown(string width)
    {
        //must be a valid existing setting for this to work
        int i = 0;
        foreach (var option in m_widthDropdown.options)
        {
            if (option.text == width)
            {
                m_widthDropdown.value = i;
            }
            i++;
        }

    }

    public void OnHiresFixChanged(bool newVal)
    {
        m_hiresFixToggle.isOn = newVal;
        m_hiresFix = newVal;
    }

    public void SetHeightDropdown(string height)
    {
        //must be a valid existing setting for this to work
        int i = 0;
        foreach (var option in m_heightDropdown.options)
        {
            if (option.text == height)
            {
                m_heightDropdown.value = i;
            }
            i++;
        }

    }

    public void OnGenHeightDropdownChanged()
    {
        int.TryParse(m_heightDropdown.options[m_heightDropdown.value].text, out m_genHeight);
    }

    public void OnComfyUIDropdownChanged()
    {
        //get the text of the selected option
        string selected = m_comfyUIAPIWorkflowsDropdown.options[m_comfyUIAPIWorkflowsDropdown.value].text;
        RTConsole.Log("Chose " + selected);
    }

    public void OnPresetDropdownChanged()
    {
        //get the text of the selected option
        string selected = m_presetDropdown.options[m_presetDropdown.value].text;

        PresetManager.Get().LoadPresetAndApply(selected, PresetManager.Get().GetActivePreset(), true);
    }

    public void OnTempPresetDropdownChanged()
    {
        //get the text of the selected option
        //string selected = m_tempPresetDropdown.options[m_tempPresetDropdown.value].text;

    }

    public string GetNameOfActiveTempPreset()
    {
        return m_tempPresetDropdown.options[m_tempPresetDropdown.value].text;
    }


    public string GetSamplerName()
    {
        return m_samplerDropdown.options[m_samplerDropdown.value].text;
    }

    public int GetSamplerIndex()
    {
        return m_samplerDropdown.value;
    }

    public void ClearModelDropdown()
    {
        m_modelDropdown.options.Clear();
    }

    public void ClearRefinerModelDropdown()
    {
        m_refinerModelDropdown.options.Clear();

        //add the none option
        AddRefinerModelDropdown("None");

        m_refinerModelDropdown.value = 0;
    }

    public void AddModelDropdown(string name)
    {
        List<string> options = new List<string>();
        options.Add(name);
        m_modelDropdown.AddOptions(options);

        List<TMP_Dropdown.OptionData> dropList = m_modelDropdown.options;
        //  dropList.Sort((x, y) => x.text.CompareTo(y.text));
        //dropList.Reverse();
        m_modelDropdown.options = dropList;
    }

    public void AddRefinerModelDropdown(string name)
    {
        List<string> options = new List<string>();
        options.Add(name);
        m_refinerModelDropdown.AddOptions(options);

        List<TMP_Dropdown.OptionData> dropList = m_refinerModelDropdown.options;

        //dropList.Sort((x, y) => x.text.CompareTo(y.text));
        //dropList.Reverse();
        m_refinerModelDropdown.options = dropList;
    }

    public void SetHasControlNetSupport(bool bHasControlNetSupport)
    {
        m_bControlNetSupportExists = bHasControlNetSupport;
        m_useControlNetToggle.interactable = bHasControlNetSupport;
    }

    public void SetControlNetMaxModels(int max)
    {
        m_controlNetMaxModels = max;

    }

    public int GetControlNetMaxModels()
    {
        return m_controlNetMaxModels;
    }
    public void ClearControlNetModelDropdown()
    {
        m_controlNetModelArray.Clear();
    }

    public void AddControlNetModelDropdown(string name)
    {
        m_controlNetModelArray.Add(name);

    }

    public void ClearControlNetPreprocessorsDropdown()
    {
        m_controlNetPreprocessorArray.Clear();
    }

    public void AddControlNetPreprocessorsDropdown(string name)
    {
        m_controlNetPreprocessorArray.Add(name);

    }

    public void ClearSamplersDropdown()
    {
        m_samplerDropdown.options.Clear();
    }

    public void AddSamplersDropdown(string name)
    {
        List<string> options = new List<string>();
        options.Add(name);
        m_samplerDropdown.AddOptions(options);
    }

    public void SetSamplerByName(string name)
    {
        name = name.ToLower();

        for (int i = 0; i < m_samplerDropdown.options.Count; i++)
        {
            if (name == m_samplerDropdown.options[i].text.ToLower())
            {
                m_samplerDropdown.value = i;
                CheckIfSamplerIsValid();
                return;
            }
        }

        Debug.Log("Can't set default sampler, don't know: " + name);
    }
    public void SetModelByName(string name)
    {
        name = name.ToLower();

        for (int i = 0; i < m_modelDropdown.options.Count; i++)
        {
            if (name == m_modelDropdown.options[i].text.ToLower())
            {
                m_modelDropdown.value = i;
                return;
            }
        }

        //Debug.Log("Can't set default sampler, don't know: " + name);
        UpdateGUI();
    }

    public void SetRefinerModelByName(string name)
    {
        name = name.ToLower();

        for (int i = 0; i < m_refinerModelDropdown.options.Count; i++)
        {
            if (name == m_refinerModelDropdown.options[i].text.ToLower())
            {
                m_refinerModelDropdown.value = i;
                return;
            }
        }

        //Debug.Log("Can't set default sampler, don't know: " + name);
        UpdateGUI();
    }

    public void SetChangeModelEnabled(bool enabled)
    {
        m_modelDropdown.interactable = enabled;
    }

    public string GetActiveModelFilename()
    {
        return m_activeModelName;
    }

    public string GetActiveRefinerModelFilename()
    {
        if (m_refinerModelDropdown.options[m_refinerModelDropdown.value].text == "None") return ""; //might be better for the API
        return m_refinerModelDropdown.options[m_refinerModelDropdown.value].text;
    }

    public float GetRefinerSwitchAt()
    {
        //return m_refinerInputField as a float, avoiding any possible errors durin conversion
        float.TryParse(m_refinerInputField.text, out float result);
        return result;
    }
    public bool IsActiveModelPix2Pix()
    {
        if (m_activeModelName != null && m_activeModelName.Length > 0)
        {
            string temp = m_activeModelName.ToLower();

            if (temp.Contains("pix2pix"))
            {
                return true;
            }
        }
        return false;
    }

    public void OnHideGUI()
    {
        RTUtil.SetActiveByNameIfExists("Panel", false);
        RTUtil.SetActiveByNameIfExists("MiniPanel", true);
    }

    public void OnShowGUI()
    {
        RTUtil.SetActiveByNameIfExists("Panel", true);
        RTUtil.SetActiveByNameIfExists("MiniPanel", false);
    }

    public void OnHideCamToolGUI()
    {
        RTUtil.SetActiveByNameIfExists("CamToolPanel", false);
        RTUtil.SetActiveByNameIfExists("CamToolMiniPanel", true);
    }

    public void OnShowCamToolGUI()
    {
        RTUtil.SetActiveByNameIfExists("CamToolPanel", true);
        RTUtil.SetActiveByNameIfExists("CamToolMiniPanel", false);
    }

    public string GetJobListAsSingleString()
    {
        return m_jobListInputField.text;
    }

    public void AddEveryItemToJobList(ref List<string> joblist)
    {
        List<string> jobsToAdd = GetPicJobListAsListOfStrings();
        foreach (string job in jobsToAdd)
        {
            joblist.Add(job);
        }
    }

    public void AddEveryTempItemToJobList(ref List<string> joblist, string presetName)
    {
        List<string> jobsToAdd = GetTempPicJobListAsListOfStrings(presetName);
        foreach (string job in jobsToAdd)
        {
            joblist.Add(job);
        }
    }
    public List<String> GetPicJobListAsListOfStrings(string jobtext)
    {
        List<String> list = new List<String>();
        string[] lines = jobtext.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            string trimmedItem = line.Trim();
            if (trimmedItem.Length > 0 && trimmedItem[0] != '-' && trimmedItem[0] != '/')
            {
                list.Add(trimmedItem);
            }
        }
        return list;
    }

    public List<String> GetPicJobListAsListOfStrings()
    {
        List<String> list = new List<String>();
        string[] lines = m_jobListInputField.text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            string trimmedItem = line.Trim();
            if (trimmedItem.Length > 0 && trimmedItem[0] != '-' && trimmedItem[0] != '/')
            {
                list.Add(trimmedItem);
            }
        }
        return list;
    }

    public List<String> GetTempPicJobListAsListOfStrings(string presetName)
    {

        //first, we need to load this into our temp extractor using the preset manager
        PresetManager.Get().LoadPresetAndApply(presetName, PresetManager.Get().GetTempPreset(), false);

        List<String> list = new List<String>();
        string jobList = PresetManager.Get().GetTempPreset().JobList ?? string.Empty;
        string[] lines = jobList.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            string trimmedItem = line.Trim();
            if (trimmedItem.Length > 0 && trimmedItem[0] != '-' && trimmedItem[0] != '/')
            {
                list.Add(trimmedItem);
            }
        }
        return list;
      
    }
    public void SetJobList(string joblist)
    {
        m_jobListInputField.text = joblist;

        //move the horizontal slider to the top
        m_jobListInputField.verticalScrollbar.value = 0;
    }
    public void AddJobToJobList(string job, ref string jobList)
    {
        if (jobList.Length > 0)
        {
            jobList += "\n";
        }
        jobList += job;
    }

    public void OnWorkFlowSet()
    {
        //see which workflow is active
        string selected = m_comfyUIAPIWorkflowsDropdown.options[m_comfyUIAPIWorkflowsDropdown.value].text;
        RTConsole.Log("Chose " + selected);
        m_jobListInputField.text = "";
        string temp = m_jobListInputField.text;
        AddJobToJobList(selected, ref temp);
        m_jobListInputField.text = temp;
    }
    public void OnWorkFlowAdd()
    {
        //see which workflow is active
        string selected = m_comfyUIAPIWorkflowsDropdown.options[m_comfyUIAPIWorkflowsDropdown.value].text;
        RTConsole.Log("Add Chose " + selected);
        string temp = m_jobListInputField.text;
        AddJobToJobList(selected, ref temp);
        m_jobListInputField.text = temp;
    }


    public void CheckIfSamplerIsValid()
    {

        if (m_modelDropdown.options.Count == 0) return;

        int optionID = m_modelDropdown.value;

        if (m_modelDropdown.options[optionID].text.Contains("768"))
        {
            SetWidthDropdown("768");
            SetHeightDropdown("768");
        }

        if (m_modelDropdown.options[optionID].text.Contains("XL"))
        {
            //XL models need this
            SetWidthDropdown("1024");
            SetHeightDropdown("1024");

            /*
            if (m_samplerDropdown.options[m_samplerDropdown.value].text.Contains("DDIM"))
            {
                //DDIM won't work well with this
                SetSamplerByName("dpm++ 2s a karras");
                SetSteps(70);
            }
            */

        }
    }

    public void OnModelChanged(Int32 optionID)
    {
        //send request to all servers
        ImageGenerator.Get().SetGenerate(false);
        Config.Get().SendRequestToAllServers("sd_model_checkpoint", m_modelDropdown.options[optionID].text);
        RTQuickMessageManager.Get().ShowMessage("Servers loading " + m_modelDropdown.options[optionID].text);

        SetWidthDropdown("512");
        SetHeightDropdown("512");

        m_activeModelName = m_modelDropdown.options[optionID].text;

        UpdateGUI();
        CheckIfSamplerIsValid();
    }

    public void UpdateGUI()
    {
        m_Pix2PixSlider.interactable = IsActiveModelPix2Pix();
    }
    public void SetPrompt(string p)
    {
        m_inputField.text = p;
    }

    public void SetComfyPrependPrompt(string p)
    {
        m_comfyPrependPromptInputField.text = p;
    }

    public void SetComfyAppendPrompt(string p)
    {
        m_comfyAppendPromptInputField.text = p;
    }

    public void SetNegativePrompt(string p)
    {
        m_negativeInputField.text = p;
    }

    public List<String> GetControlNetPreprocessorArray() { return m_controlNetPreprocessorArray; }
    public List<String> GetControlNetModelArray() { return m_controlNetModelArray; }

    private void Awake()
    {
        _this = this;
        UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);

        //I guess we're hardcoding these for now, can't get the from the API
        m_controlNetPreprocessorArray.Add("none");
        m_controlNetPreprocessorArray.Add("canny");
        m_controlNetPreprocessorArray.Add("depth");
        m_controlNetPreprocessorArray.Add("depth_leres");
        m_controlNetPreprocessorArray.Add("hed");
        m_controlNetPreprocessorArray.Add("mlsd");
        m_controlNetPreprocessorArray.Add("normal_map");
        m_controlNetPreprocessorArray.Add("open pose");
        m_controlNetPreprocessorArray.Add("pidinet");
        m_controlNetPreprocessorArray.Add("scribble");
        m_controlNetPreprocessorArray.Add("fake_scribble");
        m_controlNetPreprocessorArray.Add("segmentation");

        m_controlNetPreprocessorCurIndex = 1;
        m_controlNetModelCurIndex = 0;

        // Application.targetFrameRate = 20000;
        //QualitySettings.vSyncCount = 0;
        //QualitySettings.antiAliasing = 4;
        /*
        Debug.unityLogger.filterLogType = LogType.Log;

        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
        Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.ScriptOnly);
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
        */
    }


    public string GetCurrentControlNetPreprocessorString()
    {
        return m_controlNetPreprocessorArray[m_controlNetPreprocessorCurIndex];
    }

    public string GetCurrentControlNetModelString()
    {
        if (m_controlNetModelArray.Count == 0) return "";
        return m_controlNetModelArray[m_controlNetModelCurIndex];
    }

    public void OnCurrentControlNetPreprocessorStringChanged(int index)
    {
        m_controlNetPreprocessorCurIndex = index;
    }

    public void OnCurrentControlNetModelStringChanged(int index)
    {
        m_controlNetModelCurIndex = index;
    }

    public bool SetCurrentControlNetModelBySubstring(string substring)
    {
        //we'll set m_controlNetModelCurIndex by checking if the substring is inside of any of the strings in m_controlNetModelArray
        for (int i = 0; i < m_controlNetModelArray.Count; i++)
        {
            if (m_controlNetModelArray[i].Contains(substring))
            {
                m_controlNetModelCurIndex = i;
                return true; //we've found it
            }
        }

        return false; //didn't see anything like that
    }

    //Now we'll do the same thing, but for ControlNetPreprocessor
    public void SetCurrentControlNetPreprocessorBySubstring(string substring)
    {
        //we'll set m_controlNetPreprocessorCurIndex by checking if the substring is inside of any of the strings in m_controlNetPreprocessorArray
        for (int i = 0; i < m_controlNetPreprocessorArray.Count; i++)
        {
            if (m_controlNetPreprocessorArray[i].Contains(substring))
            {
                m_controlNetPreprocessorCurIndex = i;
                return;
            }
        }
    }

    public void SetDefaultControLNetOptions()
    {
        SetCurrentControlNetPreprocessorBySubstring(m_defaultControlNetProcessor);
        SetCurrentControlNetModelBySubstring(m_defaultControlNetModel);
    }

    public int GetCurrentControlNetModelIndex() { return m_controlNetModelCurIndex; }
    public int GetCurrentControlNetPreprocessorIndex() { return m_controlNetPreprocessorCurIndex; }
    // Use this for initialization
    public GameObject GetPicWereHoveringOver()
    {
        //OPTIMIZE - set to cache results for entire frame
        var camera = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();

        Vector2 ray = new Vector2(camera.ScreenToWorldPoint(Input.mousePosition).x, camera.ScreenToWorldPoint(Input.mousePosition).y);
        RaycastHit2D hit = Physics2D.Raycast(ray, Vector2.zero);
        if (hit.collider != null)
        {
            // Debug.Log(hit.collider.gameObject);
            return hit.collider.gameObject.transform.parent.gameObject;
        }

        return null;
    }

  

    public void OnAddNewPicButton()
    {
        GameObject go = m_AIimageGenerator.CreateNewPic();

    }

    public void OnRunJoblistOnAll()
    {
        ImageGenerator.Get().RunTool1OnAllPics();
    }
    
    public void OnAddPicFromClipboard()
    {
        var go = m_AIimageGenerator.CreateNewPic();
        var picScript = go.GetComponent<PicMain>();
        if (picScript.LoadImageFromClipboard())
        {

            //success
        }
    }

    public bool GetInpaintMaskEnabled()
    {
        return m_inpaintMaskActive;
    }

    public void SetInpaintMaskEnabled(bool bNew)
    {
        m_inpaintMaskActive = bNew;

        //Debug.Log("Set inpaint mask");
    }

    public float GetAlphaMaskFeatheringPower() { return m_alphaMaskFeatheringPower; }
    public void SetAlphaMaskFeatheringPower(float power)
    {
        m_alphaMaskFeatheringPower = power;
        m_maskBlendingInput.value = power;
    }

    public bool GUIIsBeingUsed()
    {
        if (EventSystem.current.IsPointerOverGameObject()) return true;

        if (m_inputField.isFocused) return true;
        if (m_negativeInputField.isFocused) return true;

        return false;
    }

    public void OnLoopSourceButton(bool bNew)
    {
        m_bLoopSource = bNew;
    }

    public bool GetLoopSource()
    {
        return m_bLoopSource;
    }

    public void OnTilingButton(bool bNew)
    {
        m_bTiling = bNew;
        m_tilingToggle.isOn = bNew;
    }

    public bool GetTiling()
    {
        return m_bTiling;
    }
    public void ShowCompatibilityWarningIfNeeded()
    {
        int gpu = Config.Get().GetFreeGPU(RTRendererType.AI_Tools, true);
        if (gpu == -1)
        {

            RTUtil.SetActiveByNameIfExists("RTWarningSplash", true);
        }
    }

    public void OnClickedControlNetSettingsButton()
    {
        const string panelName = "ControlNetSettingsPanel";
        var existing = RTUtil.FindIncludingInactive(panelName);
        if (existing != null)
        {
            RTConsole.Log("Killing controlnetsettings");
            RTUtil.KillObjectByName(panelName);
            return;
        }
        RTConsole.Log("Creating " + panelName);

        GameObject genPanel = Instantiate(m_controlNetPanelPrefab, RTUtil.FindIncludingInactive("MainCanvas").transform);
        genPanel.name = panelName;

    }

    public void OnUseAIGuide(bool bNew)
    {
        // m_bUseControlNet = bNew;
        // m_useControlNetToggle.isOn = bNew;

    }
   
    public void OnClickedAIGuideSettingsButton()
    {
        Debug.Log("Clicked AI guide settings");
        const string panelName = "AIGuidePanel";
        var existing = RTUtil.FindIncludingInactive(panelName);

        existing.GetComponent<AIGuideManager>().ToggleWindow();

        //if the window is now active, let's center it on the screen as it may be offscreen
        if (existing.activeSelf)
        {
            existing.transform.position = new Vector3(Screen.width / 2, Screen.height / 2, 0);
        }
    }

    public void OnClickedComfyUISettingsButton()
    {
        Debug.Log("Clicked ComfyUI settings");
        const string panelName = "ComfyUIPanel";
        var existing = RTUtil.FindIncludingInactive(panelName);
        existing.GetComponent<ComfyUIPanel>().ToggleWindow();
    }

    public Queue<GTPChatLine> PrepareLLMLinesForSending(Queue<GTPChatLine> lines)
    {

        if (GenerateSettingsPanel.Get().m_stripThinkTagsToggle.isOn)
        {
            lines = OpenAITextCompletionManager.RemoveThinkTags(lines);
        }

        // Always strip TMP markup before sending to the LLM
        lines = OpenAITextCompletionManager.RemoveTMPTags(lines);

        return lines;

    }

    public void OnClickedComfyUIOpenDirButton()
    {
        Debug.Log("Opening dir with the ComfyUI settings");
        System.Diagnostics.Process.Start("explorer.exe", "ComfyUI");
    }

    public void OnClickedPresetOpenDirButton()
    {
        System.Diagnostics.Process.Start("explorer.exe", "Presets");
    }



    public void OnClickedRescanComfyUIWorkflowsFolder()
    {
        Debug.Log("Rescanning ComfyUI workflows folder");
        LoadComfyUIWorkFlows(m_comfyUIAPIWorkflowsDropdown);
    }

    public bool HasControlNetSupport()
    {
        return m_bControlNetSupportExists;
    }
        
    public void OnUseControlNet(bool bNew)
    {
        m_bUseControlNet = bNew;
        m_useControlNetToggle.isOn = bNew;
        
    }

    public bool GetUseControlNet()
    {

        return m_bUseControlNet;
    }

    public void OnRemoveBackground(bool bNew)
    {
        m_bRemoveBackground = bNew;
        m_removeBackgroundToggle.isOn = bNew;

        if (bNew)
        { 
            ShowCompatibilityWarningIfNeeded();
        }
    }

    public bool GetRemoveBackground()
    {
        return m_bRemoveBackground;
    }

    public void KillPicsThatAreWaitingForGPU()
    {
        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();
        foreach (PicMain picScript in aiScripts)
        {
            if (picScript.IsWaitingForGPU())
            {
                picScript.SafelyKillThisPic();
            }
        }
    }
    public void KillAllPics(bool bBusy, bool bLocked)
    {
          Debug.Log("Clearing all pics");

        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();

        foreach (PicMain picScript in aiScripts)
        {
            //why do I get an error without this cast?!
            //(script as PicTextToImage).KillIfPossible();
            if (picScript.IsBusy() && !bBusy)
            {
                //don't delete
                continue;
            }

            if (picScript.GetLocked() && !bLocked)
            {

                //don't delete
                continue;
            }

            picScript.SafelyKillThisPic();

        }

        if (bLocked && bBusy)
        {
            //Might as well kill any texts around too
            RTUtil.DestroyChildren(RTUtil.FindObjectOrCreate("Adventures").transform);
        }
        ImageGenerator.Get().ReorganizePics(); //defrag 'em
    }

    public void OnClearButtonWithShiftAllowed()
    {

        //if either control button is held down, run KillPicsThatAreWaitingForGPU instead
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            KillPicsThatAreWaitingForGPU();
            return;
        }

        KillAllPics(Input.GetKey(KeyCode.LeftShift)|| Input.GetKey(KeyCode.RightShift), false);
    }

    public void OnClearButton()
    {
        KillAllPics(false, false);
    }

    public string GetModifiedGlobalPrompt()
    {
        string result = GetModifiedPrompt();

        if (GetComfyPrependPrompt() != null && GetComfyPrependPrompt().Length > 0)
        {
            result = GetComfyPrependPrompt() + " " + result;
        }

        if (GetComfyAppendPrompt() != null && GetComfyAppendPrompt().Length > 0)
        {
            result = result + " " + GetComfyAppendPrompt();
        }

        return result.Trim();
    }


public string GetPrompt() { return m_prompt; }
    public string GetComfyPrependPrompt() { return m_comfyPrependPrompt; }
    public string GetComfyAppendPrompt() { return m_comfyAppendPrompt; }

    public void ResetLastModifiedPrompt()
    {
        m_lastModifiedPrompt = "";
    }

    public string GetLastModifiedPrompt() { return m_lastModifiedPrompt; }

    //should probably move this somewhere else

    /// <summary>
    /// Takes a string of comma delimited values and randomly removes one of them using Unity's native random number generator.
    /// </summary>
    /// <param name="input">The input string of comma delimited values.</param>
    /// <returns>The modified string with one of the values removed.</returns>
    public static string RemoveRandomValue(string input)
    {
        // Split the input string into an array of values
        string[] values = input.Split(',');

        // If the array has fewer than 2 values, return the original string
        if (values.Length < 2)
        {
            return input;
        }

        // Choose a random index in the array using Unity's Random.Range method
        int index = UnityEngine.Random.Range(0, values.Length);

        // Remove the value at the chosen index
        values = values.Where((val, idx) => idx != index).ToArray();

        // Join the remaining values into a string and return it
        return string.Join(", ", values);
    }

    public string GetModifiedPrompt()
    {
        if (m_bRandomizePrompt)
        {
            m_lastModifiedPrompt = RemoveRandomValue(m_prompt).Trim();
            return m_lastModifiedPrompt;
        }

        return m_prompt;
    }


    public string GetNegativePrompt() { return m_negativePrompt; }
    public int GetSteps() { return m_steps; }
    public long GetSeed() { return m_seed; }

    public void SetSeed(int seed)
    {
        m_seed = seed;
        m_seedInputField.text = seed.ToString();
    }

    public void SetFixFaces(bool bFixFaces)
    {
        m_fixFacesToggle.isOn = bFixFaces;
    }
   
    public bool GetFixFaces() { return m_fixFaces; }
    public float GetUpscale() { return m_upscale; }

    public bool GetHiresFix() { return m_hiresFix; }
    public void OnStepsChanged(string steps)
    {
        //Debug.Log("Steps changed to " + steps);
        int.TryParse(steps, out m_steps);
    }
    public void SetSteps(int steps)
    {
        m_steps = steps;
        m_stepsInputField.text = steps.ToString();
    }

    public void OnSeedChanged(string seed)
    {
        //Debug.Log("Steps changed to " + steps);
        long.TryParse(seed, out m_seed);
    }

    public int GetPicsPerRow() { return m_picsPerRow; }
    public void OnPicsPerRowChanged(string picsPerRow)
    {
        int oldPics = m_picsPerRow;
        int.TryParse(picsPerRow, out m_picsPerRow);
        m_picsPerRow = Math.Max(1, m_picsPerRow);
        m_picsPerRow = Math.Min(1000, m_picsPerRow);

        if (oldPics != m_picsPerRow)
        {
            m_AIimageGenerator.ReorganizePics();
        }
    }

    public void OnShowLogButton()
    {
        GameObject go = RTUtil.FindIncludingInactive("RTConsole");

        if (go)
        {
            go.SetActive(!go.activeSelf);
        }
    }

    public void ShowConsole(bool bNew)
    {
        GameObject go = RTUtil.FindIncludingInactive("RTConsole");

        if (go)
        {
            go.SetActive(bNew);
        }
    }

    RTNotepad m_activeConfigNotepad; // Keep reference for reload functionality

    public void OnConfigButton()
    {
        m_activeConfigNotepad = RTNotepad.OpenFile(Config.Get().GetConfigText(), m_notepadTemplatePrefab);
        m_activeConfigNotepad.m_onClickedSavedCallback += OnConfigSaved;
        m_activeConfigNotepad.m_onClickedCancelCallback += OnConfigCanceled;
        m_activeConfigNotepad.m_onClickedOpenExternalCallback += OnConfigOpenExternal;
        m_activeConfigNotepad.m_onClickedReloadCallback += OnConfigReload;
    }

    void OnConfigSaved(string text)
    {
       
        Config.Get().ProcessConfigString(text);
        Config.Get().SaveConfigToFile(); //it might have changed.
        m_activeConfigNotepad = null;

        //Debug.Log("They clicked save.  Text entered: " + text);
    }
    void OnConfigCanceled(string text)
    {
        m_activeConfigNotepad = null;
        //Debug.Log("They clicked cancel.  Text entered: " + text);
    }

    void OnConfigOpenExternal(string text)
    {
        // Save current state to disk first so external editor sees latest changes
        Config.Get().ProcessConfigString(text);
        Config.Get().SaveConfigToFile();
        
        // Open config.txt with default text editor
        System.Diagnostics.Process.Start("config.txt");
    }

    void OnConfigReload(string text)
    {
        // Reload config.txt from disk into the notepad
        if (m_activeConfigNotepad != null)
        {
            string freshConfig = Config.Get().GetConfigText();
            // Re-read from file to get any external changes
            try
            {
                freshConfig = System.IO.File.ReadAllText("config.txt");
            }
            catch (System.Exception e)
            {
                RTConsole.Log("Failed to reload config.txt: " + e.Message);
            }
            m_activeConfigNotepad.SetText(freshConfig);
        }
    }

    public void SetTextStrength(float str)
    {
        m_textStrength = str;
        m_textStrengthSlider.value = str;

    }

    public float GetControlNetWeight()
    { return m_controlNetWeight; }

    public void SetControlNetWeight(float weight)
    {
        m_controlNetWeight = weight;
    }

    public float GetControlNetGuidance()
    { return m_controlNetGuidance; }

    public void SetControlNetGuidance(float Guidance)
    {
        m_controlNetGuidance = Guidance;
    }

    public void SetPix2PixTextStrength(float str)
    {
        m_pix2pixtextStrength = str;
        m_pix2pixTextStrengthSlider.value = str;

    }


    public float GetInpaintStrengthFloat() { return m_inpaintStrength; }
    public string GetInpaintStrengthString() 
    {
        return m_inpaintStrength.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public void SetInpaintStrength(float inpaint)
    {
        m_inpaintStrength = inpaint;
        m_inpaintStrengthInput.value = inpaint;
    }

    
    public float GetExtraNoise() { return m_extraNoiseStrength; } //no longer used

    public void SetExtraNoiseStrength(float Noise) //no longer used
    {
        m_extraNoiseStrength = Noise;
    }

    public float GetPenSize() { return m_penSize; }

    public void SetPenSize(float inpaint)
    {
        m_penSize = inpaint;
    }
    public float GetTextStrengthFloat() { return m_textStrength; }
    public string GetTextStrengthString() 
    {
        return m_textStrength.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public float GetPix2PixTextStrengthFloat() { return m_pix2pixtextStrength; }
    public string GetPix2PixTextStrengthString()
    {
        return m_pix2pixtextStrength.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public string GetMaskContent()
    {
        return m_maskedContentDropdown.options[m_maskedContentDropdown.value].text;
    }

    public int SetMaskContentByName(string name)
    {

        for (int i=0; i < m_maskedContentDropdown.options.Count; i++)
        {
            if (name == m_maskedContentDropdown.options[i].text)
            {
                //that's it
                m_maskedContentDropdown.value = i;
                return i;
            }
        }
        Debug.LogError("Error, no mask fill type named " + name + " found.");
        return -1;
    }
    public void OnUpscaleChanged(bool upscale)
    {
       if (upscale)
        {
            m_upscale = 2.0f;
        }
        else
        {
            m_upscale = 1.0f;
        }

        m_upscaleToggle.isOn = upscale;

    }

    public void OnFixFacesChanged(bool bNew)
    {
        //Debug.Log("Steps changed to " + steps);
        m_fixFaces = bNew;

        //make sure the GUI button matches
        m_fixFacesToggle.isOn = bNew;
    }
    // Allow config.txt to set default generation size
    public void SetGenWidth(int width)
    {
        m_genWidth = width;
        
    }

    public void SetGenHeight(int height)
    {
        m_genHeight = height;
       
    }
    public void OnComfyPrependPromptChanged(string str)
    {
        m_comfyPrependPrompt = str;
    }

      public void OnComfyAppendPromptChanged(string str)
    {
        m_comfyAppendPrompt = str;
    }

    public void OnPromptChanged(String str)
    {
        m_prompt = str;
       
        if (GetTurbo())
        {
            //trigger re-render
            //find suitable place to render to, get the last pic that was created/exists
      
            //Debug.Log("Prompt changed: " + str);
            var picMain = ImageGenerator.Get().GetPicToUseTurboOn();

            //trigger a render to happen now

        }
    }
    public void OnNegativePromptChanged(String str)
    {
        m_negativePrompt = str;
        //Debug.Log("Negative prompt changed: " + str);
    }

    void Start()
    {

        DOTween.Init(true, true, LogBehaviour.Verbose).SetCapacity(200, 20);
        // RTAudioManager.Get().SetDefaultMusicVol(0.4f);

        string dir = @"tempCache";
        // If directory does not exist, create it
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }


#if RT_NOAUDIO
		AudioListener.pause = true;
#endif

        /*
              if (RTUtil.DoesCommandLineWordExist("-runfullserver"))
                {
                    print("Detected -runfullserver flag");
                    _isServer = true;
                }

            */

        RTConsole.Get().SetShowUnityDebugLogInConsole(false);
        RTConsole.Get().SetMirrorToDebugLog(true);
        //RTEventManager.Get().Schedule(RTAudioManager.GetName(), "PlayMusic", 1, "intro");
        string version = "Unity V " + Application.unityVersion + " :";

#if NET_2_0
        version += " Net 2.0 API";
#endif
#if NET_2_0_SUBSET
        version += " Net 2.0 Subset API";
#endif

#if NET_4_6
        version += " .Net 4.6 API";
#endif

#if RT_BETA
        print ("Beta build detected!");
#endif

        //  RTConsole.Get().SetMirrorToDebugLog(true);

        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].ToLower() == "-disable_filter")
            {
                Config.Get().SetSafetyFilter(false);
            }
        }


#if !RT_RELEASE
        RTUtil.KillObjectByName("RTIntroSplash");

        //let's directly start an experiment.  I schedule it to allow 1 frame to pass, if I don't the camera
        //isn't initted yet
        //RTMessageManager.Get().Schedule(0, PizzaLogic.Get().OnStartPizza);
        //RTMessageManager.Get().Schedule(0, BreakoutLogic.Get().OnStartBreakout);
        // RTMessageManager.Get().Schedule(0, ShootingGalleryLogic.Get().OnStartGameMode);

        //RTMessageManager.Get().Schedule(1.5f, CrazyCamLogic.Get().OnStartGameMode);
#endif



        Config.Get().CheckForUpdate();

        LoadComfyUIWorkFlows(m_comfyUIAPIWorkflowsDropdown);
   
        Config.Get().PopulateRendererDropDown(m_rendererSelectionDropdown);

        m_presetDropdown.SetValueWithoutNotify(0);
        PresetManager.Get().PopulatePresetDropdown(m_presetDropdown);
        PresetManager.Get().PopulatePresetDropdown(m_tempPresetDropdown);

        if (PresetManager.Get().DoesPresetExistByNameNotCaseSensitive("test_startup.txt"))
        {
            SetPresetDropdownValue("test_startup.txt");
            PresetManager.Get().LoadPresetAndApply(GetNameOfActivePreset(), PresetManager.Get().GetActivePreset(), true);
        } else
        {
            if (PresetManager.Get().DoesPresetExistByNameNotCaseSensitive("startup.txt"))
            {
                SetPresetDropdownValue("startup.txt");
                PresetManager.Get().LoadPresetAndApply(GetNameOfActivePreset(), PresetManager.Get().GetActivePreset(), true);
            }
        }
        
        // Keep the "Active LLM" label on the Tools panel in sync with current LLM settings.
        BindActiveLLMLabelUpdates();
        UpdateActiveLLMLabel();

        // If the LLM settings manager isn't ready yet, retry until it is (or we time out).
        if (LLMSettingsManager.Get() == null)
        {
            _activeLLMLabelInitTries = 0;
            CancelInvoke(nameof(TryUpdateActiveLLMLabelAfterLLMManagerInit));
            InvokeRepeating(nameof(TryUpdateActiveLLMLabelAfterLLMManagerInit), 0.1f, 0.25f);
        }


        //let's add text saying "This is a temp pic" to our temp pic1 that will overlay it
        m_tempPic1.GetComponent<PicMain>().AddTextLabelToImage("TempPic1 (used with job scripts sometimes)");
    }

    private void TryUpdateActiveLLMLabelAfterLLMManagerInit()
    {
        _activeLLMLabelInitTries++;

        if (LLMSettingsManager.Get() != null)
        {
            UpdateActiveLLMLabel();
            CancelInvoke(nameof(TryUpdateActiveLLMLabelAfterLLMManagerInit));
            return;
        }

        if (_activeLLMLabelInitTries >= ACTIVE_LLM_LABEL_MAX_INIT_TRIES)
        {
            // Give up; whatever we have is the best we can do.
            UpdateActiveLLMLabel();
            CancelInvoke(nameof(TryUpdateActiveLLMLabelAfterLLMManagerInit));
        }
    }

    public void SetPresetDropdownValue(string value)
    {
        value = value.ToLower();

        //if an option of the dropdown matches the value, set it to that value
        for (int i = 0; i < m_presetDropdown.options.Count; i++)
        {
            if (m_presetDropdown.options[i].text.ToLower() == value)
            {
                m_presetDropdown.value = i;
                return;
            }
        }
    }

    public TMP_Dropdown GetPresetDropdown()
    {
        return m_presetDropdown;
    }
    public TMP_Dropdown GetTempPresetDropdown()
    {
        return m_tempPresetDropdown;
    }

    public string GetNameOfActivePreset()
    {
        return m_presetDropdown.options[m_presetDropdown.value].text;
    }
    public string GetActiveComfyUIWorkflowFileName(int serverID)
    {

        //if serverID exists, let's check it
        if (Config.Get().IsValidGPU(serverID))
        {
            GPUInfo gpuInfo = Config.Get().GetGPUInfo(serverID);
            if (gpuInfo._comfyUIWorkFlowOverride != -1)
            {
                return m_comfyUIAPIWorkflowsDropdown.options[gpuInfo._comfyUIWorkFlowOverride].text;
            }
        }

        return m_comfyUIAPIWorkflowsDropdown.options[m_comfyUIAPIWorkflowsDropdown.value].text;
    }

    public void LoadComfyUIWorkFlows(TMP_Dropdown dropdown)
    {
        // First, delete everything from the dropdown
        dropdown.ClearOptions();

        // Load the ComfyUI workflows
        string[] files = Directory.GetFiles("ComfyUI", "*.json");

        List<string> options = new List<string>();
        int defaultIndex = -1;

        foreach (string file in files)
        {
            // Get the name of the file
            string name = Path.GetFileName(file);

            // Skip any cached API version files
            if (name.IndexOf("cached_api_version", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            options.Add(name);
        }

        // Add options to the dropdown
        dropdown.AddOptions(options);

        // Set the default selection
        if (defaultIndex != -1)
        {
            dropdown.value = defaultIndex;
        }
    }

    void OnApplicationQuit() 
	{
        // Make sure prefs are saved before quitting.
        //PlayerPrefs.Save();
        RTConsole.Log("Application quitting normally");

        DirectoryInfo di = new DirectoryInfo("tempCache");
        di.Delete(true);
        //        NetworkTransport.Shutdown();
        print("QUITTING!");

        //Also delete temp files used for debugging if they exist
        RTUtil.DeleteFileIfItExists("text_completion_sent.json");
        RTUtil.DeleteFileIfItExists("textgen_json_received.json");
        RTUtil.DeleteFileIfItExists("comfyui_workflow_to_send.json");
        RTUtil.DeleteFileIfItExists("comfyui_workflow_to_send_api.json");
        RTUtil.DeleteFileIfItExists("setup_ollama_server_request.txt");
        RTUtil.DeleteFileIfItExists("openai_image_json_received.json");
        RTUtil.DeleteFileIfItExists("openai_image_json_sent.json");
        RTUtil.DeleteFileIfItExists("json_to_send.json");
        RTUtil.DeleteFileIfItExists("claude_json_received.json");
        RTUtil.DeleteFileIfItExists("last_error_returned.json");
        RTUtil.DeleteFileIfItExists("json_error.json");
       


    }

    private void OnDestroy()
    {
        print("Game logic destroyed");
    }

    public void OnNoServersButtonClicked()
    {
        Debug.Log("No servers button clicked");
        RTQuickMessageManager.Get().ShowMessage("Click Configuration, then Apply to try to reconnect to servers");
//        Config.Get().ConnectToServers();
    }

    /// <summary>
    /// Called when the LLM Settings button is clicked. Opens the LLM settings panel.
    /// </summary>
    public void OnLLMSettingsButtonClicked()
    {
        LLMSettingsPanel.Toggle();
    }

    private void BindActiveLLMLabelUpdates()
    {
        if (m_llmSelectionDropdown != null)
        {
            m_llmSelectionDropdown.onValueChanged.RemoveListener(OnLegacyLLMDropdownChanged);
            m_llmSelectionDropdown.onValueChanged.AddListener(OnLegacyLLMDropdownChanged);
        }
    }

    private void OnLegacyLLMDropdownChanged(int _)
    {
        // Only matters if the new LLM settings manager isn't present; otherwise LLMSettingsManager drives the active provider.
        UpdateActiveLLMLabel();
    }

    public void UpdateActiveLLMLabel()
    {
        if (m_activeLLMLabel == null) return;

        string label = "None";

        var mgr = LLMSettingsManager.Get();
        if (mgr != null)
        {
            var provider = mgr.GetActiveProvider();
            string providerName = GetLLMProviderDisplayName(provider);
            string model = mgr.GetModel(provider);

            if (!string.IsNullOrEmpty(providerName))
            {
                // Format as:
                // <provider>
                // <model>
                // If model is empty (possible for local providers), show "Default".
                label = providerName + "\n" + (string.IsNullOrEmpty(model) ? "Default" : model);
            }
        }
        else if (m_llmSelectionDropdown != null && m_llmSelectionDropdown.options != null && m_llmSelectionDropdown.options.Count > 0)
        {
            int idx = Mathf.Clamp(m_llmSelectionDropdown.value, 0, m_llmSelectionDropdown.options.Count - 1);
            string optionText = m_llmSelectionDropdown.options[idx]?.text;
            if (!string.IsNullOrEmpty(optionText))
            {
                // Legacy fallback: we only know the provider name here.
                label = optionText + "\n";
            }
        }

        m_activeLLMLabel.text = label;
    }

    private static string GetLLMProviderDisplayName(LLMProvider provider)
    {
        switch (provider)
        {
            case LLMProvider.OpenAI:
                return "OpenAI";
            case LLMProvider.Anthropic:
                return "Anthropic";
            case LLMProvider.LlamaCpp:
                return "llama.cpp";
            case LLMProvider.Ollama:
                return "Ollama";
            default:
                return "";
        }
    }

    /// <summary>
    /// Get the currently active LLM provider from the new LLM settings system.
    /// </summary>
    public LLMProvider GetActiveLLMProvider()
    {
        var manager = LLMSettingsManager.Get();
        if (manager != null)
        {
            return manager.GetActiveProvider();
        }
        // Fallback to dropdown if manager not available
        return (LLMProvider)m_llmSelectionDropdown.value;
    }

    /// <summary>
    /// Get the legacy LLM_Type for backward compatibility with existing code.
    /// </summary>
    public LLM_Type GetActiveLLMType()
    {
        var manager = LLMSettingsManager.Get();
        if (manager != null)
        {
            return manager.GetLegacyLLMType();
        }
        // Fallback to dropdown
        return (LLM_Type)m_llmSelectionDropdown.value;
    }

    public void SlowZoomChange(float zoomSpeed)
    {
        var cam = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();

        if (cam == null) return;
        //slowly zoom out the active camera, this is called every frame
        cam.orthographicSize += zoomSpeed;
    }
    public void SetToolsVisible(bool bNew)
    {
        RTUtil.FindIncludingInactive("ToolsCanvas").SetActive(bNew);
    }
    // Update is called once per frame

    void Update()
    {

        float zoomSpeed = 0.01f + Time.deltaTime;

       
        //if we wanted to be able to zoom in/out with keys (I used it for a video once)

        /*
        if (Input.GetKey(KeyCode.Minus))
        {
        
                SlowZoomChange(-zoomSpeed);
         
        }

        if (Input.GetKey(KeyCode.Equals))
        {
            SlowZoomChange(zoomSpeed);

        }
        */


        if (m_gameMode == eGameMode.EXPERIMENT) return;

        const float penAdjustmentSize = 7.0f;

        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            m_penSlider.value += penAdjustmentSize;
        }
        if (Input.GetKeyDown(KeyCode.LeftBracket))
        {
            m_penSlider.value -= penAdjustmentSize;
        }
        
        if (Input.GetKeyDown(KeyCode.Backslash))
        {
           
                AskAllMoviePicsToUnloadTheMovieToSaveMemory();
           
        }


        if (Input.GetKeyDown(KeyCode.U)
            ||
            Input.GetKeyDown(KeyCode.M)
            ||
            Input.GetKeyDown(KeyCode.Alpha1)
            ||
                Input.GetKeyDown(KeyCode.I)
                ||
                Input.GetKeyDown(KeyCode.P)
            ||
               Input.GetKeyDown(KeyCode.H)
             
            )
        {
            if (GUIIsBeingUsed()) return;

                GameObject go = GetPicWereHoveringOver();

            if (go)
            {
                //PicTextToImage TTIscript = go.GetComponent<PicTextToImage>();
                PicMain picScript = go.GetComponent<PicMain>();
                PicMask picMaskScript = go.GetComponent<PicMask>();
                PicMovie picMovieScript = go.GetComponent<PicMovie>();

                if (Input.GetKeyDown(KeyCode.U))
                {
                    picScript.UndoImage();
                }

                if (Input.GetKeyDown(KeyCode.M))
                {
                    picMaskScript.OnToggleMaskViewButton();
                }

                if (Input.GetKeyDown(KeyCode.H))
                {
                    if (picScript.IsMovie())
                    {
                        picMovieScript.OnSetHidden();
                    }
                }


                if (Input.GetKeyDown(KeyCode.P))
                {
                        picMovieScript.TogglePlay();
                  
                }

                if (Input.GetKeyDown(KeyCode.I))
                {
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    {
                        picScript.InvertMask();
                    }
                    else
                    {
                        //picScript.OnInpaintButton();
                    }
                }
             

                if (Input.GetKey(KeyCode.Alpha1) &&  (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    picScript.m_picGeneratorScript.OnInpaintGeneratorButton();
                }

            }
        }
    }

    public void AskAllMoviePicsToUnloadTheMovieToSaveMemory()
    {

        //show message about unloading
        RTQuickMessageManager.Get().ShowMessage("Unloading movies to save memory");

        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();

        foreach (PicMain picScript in aiScripts)
        {
            if (!picScript.IsDestroyed())
            {
                 //tell it to unload
                    picScript.UnloadToSaveMemoryIfPossible();
            }
        }
    }
    public void OnClickedModelModsButton()
    {
        const string panelName = "ModelModPanel";
        var existing = RTUtil.FindIncludingInactive(panelName);

        existing.GetComponent<ModelModManager>().ToggleWindow();
    }

    public void OnLLMProcessFinished(GameObject picGO)
    {
        //this is called when the PicMain script finishes processing the LLM raw text
        PicMain picMain = picGO.GetComponent<PicMain>();
        PicTextToImage scriptAI = picGO.GetComponent<PicTextToImage>();
        Debug.Log("LLM Process finished for " + picGO.name);
        //Now we'll set this pic to delete itself if it isn't already
        picMain.SafelyKillThisPic();
    }

    public void RunJoblistPreset(string originaFileName)
    {
        //this is called when the user clicks the LLM Process Raw button
        GameObject pic = ImageGenerator.Get().CreateNewPic();
        PicMain picMain = pic.GetComponent<PicMain>();
        PicTextToImage scriptAI = pic.GetComponent<PicTextToImage>();
        PicUpscale processAI = pic.GetComponent<PicUpscale>();
        Debug.Log("LLM Processing raw text");
        string fileToLoad = "test_" + originaFileName;
        PresetFileConfigExtractor preset = new PresetFileConfigExtractor();

        //now, we're going to have it run our script, but we'd like a notification when the script finishes
        picMain.m_onFinishedScriptCallback += this.OnLLMProcessFinished;

        if (PresetManager.Get().DoesPresetExistByNameNotCaseSensitive(fileToLoad))
        {
            preset = PresetManager.Get().LoadPreset(fileToLoad, PresetManager.Get().GetTempPreset());
        }
        else
        {
            preset = PresetManager.Get().LoadPreset(originaFileName, PresetManager.Get().GetTempPreset());
        }

        PicJob jobDefaultInfoToStartWith = new PicJob();

        jobDefaultInfoToStartWith._requestedPrompt = GameLogic.Get().GetPrompt();
        jobDefaultInfoToStartWith._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
        jobDefaultInfoToStartWith._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
        jobDefaultInfoToStartWith._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
        jobDefaultInfoToStartWith.requestedRenderer = RTRendererType.ComfyUI;
        picMain.SetRequirements("none"); //don't want it to "wait for GPU"
        picMain.AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, GetPicJobListAsListOfStrings(preset.JobList));
    }

    public void OnLLMProcessRaw()
    {
        RunJoblistPreset("llmprocessraw.txt");
    }

    public void OnLLMProcessFlux()
    {
        RunJoblistPreset("llmprocessflux.txt");
    }

    public void CompactPics()
    {
        //let's compact the pics, this will remove any gaps in the pic list
        Debug.Log("Compacting pics");
        m_AIimageGenerator.ReorganizePics();
    }

}
