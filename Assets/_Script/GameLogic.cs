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
    string m_negativePrompt = "";
    int m_steps = 50;
    long m_seed = -1;
    bool m_bRandomizePrompt = false;
    int m_maxToGenerate = 1000;
    string m_lastModifiedPrompt;

    float m_upscale = 2.0f; //1 means noc hange
    bool m_fixFaces = false;
    float m_textStrength = 7.5f;
    float m_inpaintStrength = 0.80f;
    float m_noiseStrength = 0;
    float m_penSize = 20;
    public TMP_InputField m_inputField;
    public TMP_InputField m_negativeInputField;
    public TMP_InputField m_stepsInputField;
    public Button m_generateButton;
    public Toggle m_fixFacesToggle;
    public Toggle m_upscaleToggle;
    public Toggle m_tilingToggle;
    public Toggle m_removeBackgroundToggle;

    float m_alphaMaskFeatheringPower = 2;
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

    public enum eGameMode
    {
        NORMAL,
        EXPERIMENT
    }

    eGameMode m_gameMode = eGameMode.NORMAL;
    public eGameMode GetGameMode() { return m_gameMode; }
    public void SetGameMode(eGameMode gameMode) { m_gameMode = gameMode; }

    static GameLogic _this = null;
    static public GameLogic Get()
    {
        return _this;
    }
    public static string GetName()
    {
        return Get().name;
    }

    public int GetMaxToGenerate() { return m_maxToGenerate; }
    public void SetMaxToGenerate(int max) { m_maxToGenerate = max; }

    public bool GetRandomizePrompt() { return m_bRandomizePrompt; }
    public void SetRandomizePrompt(bool bNew) { m_bRandomizePrompt = bNew;}

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

    public string GetSamplerName() 
    {
        return m_samplerDropdown.options[m_samplerDropdown.value].text; 
    }

    public void ClearModelDropdown()
    {
        m_modelDropdown.options.Clear();
    }

    public void AddModelDropdown(string name)
    {
        List<string> options = new List<string>();
        options.Add(name);
        m_modelDropdown.AddOptions(options);
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

        for (int i=0; i < m_samplerDropdown.options.Count; i++)
        {
            if (name == m_samplerDropdown.options[i].text.ToLower())
            {
                m_samplerDropdown.value = i;
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
    }

    public void SetChangeModelEnabled(bool enabled)
    {
        m_modelDropdown.interactable= enabled;
    }
    public void OnModelChanged(Int32 optionID)
    {
        //send request to all servers
        ImageGenerator.Get().SetGenerate(false);
        Config.Get().SendRequestToAllServers("sd_model_checkpoint", m_modelDropdown.options[optionID].text);
        RTQuickMessageManager.Get().ShowMessage("Servers loading " + m_modelDropdown.options[optionID].text);

        if (!m_modelDropdown.options[optionID].text.Contains("768"))
        {
            SetWidthDropdown("512");
            SetHeightDropdown("512");
        } else
        {
            SetWidthDropdown("768");
            SetHeightDropdown("768");

        }
    }

    public void SetPrompt(string p)
    {
        m_inputField.text = p;
    }

    private void Awake()
    {
        _this = this;
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
        m_AIimageGenerator.CreateNewPic();
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
        if (!Config.Get().AllGPUsSupportAITools())
        {
            RTUtil.SetActiveByNameIfExists("RTWarningSplash", true);
        }
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

    public void OnClearButton()
    {
        Debug.Log("Clearing all pics");
               
        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();
        
        foreach (PicMain picScript in aiScripts)
        {
            //why do I get an error without this cast?!
            //(script as PicTextToImage).KillIfPossible();
            if (!picScript.IsBusy())
            {
                picScript.SafelyKillThisPic();
            }
        }

        ImageGenerator.Get().ReorganizePics(); //defrag 'em
    }

    public string GetPrompt() { return m_prompt; }
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
    public bool GetFixFaces() { return m_fixFaces; }
    public float GetUpscale() { return m_upscale; }
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

    public void OnConfigButton()
    {
        RTNotepad notepadScript = RTNotepad.OpenFile(Config.Get().GetConfigText(), m_notepadTemplatePrefab);
        notepadScript.m_onClickedSavedCallback += OnConfigSaved;
        notepadScript.m_onClickedCancelCallback += OnConfigCanceled;
    }

    void OnConfigSaved(string text)
    {
        Config.Get().ProcessConfigString(text);
        Config.Get().SaveConfigToFile(); //it might have changed.

        //Debug.Log("They clicked save.  Text entered: " + text);
    }
    void OnConfigCanceled(string text)
    {
        //Debug.Log("They clicked cancel.  Text entered: " + text);
    }

    public void SetTextStrength(float str)
    {
        m_textStrength = str;
    }
    public float GetInpaintStrengthFloat() { return m_inpaintStrength; }
    public string GetInpaintStrengthString() 
    {
        return m_inpaintStrength.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public void SetInpaintStrength(float inpaint)
    {
        m_inpaintStrength = inpaint;
    }
    public float GetNoiseStrength() { return m_noiseStrength; }

    public void SetNoiseStrength(float Noise)
    {
        m_noiseStrength = Noise;
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

    public void OnPromptChanged(String str)
    {
        m_prompt = str;
        //Debug.Log("Prompt changed: " + str);
    }
    public void OnNegativePromptChanged(String str)
    {
        m_negativePrompt = str;
        Debug.Log("Negative prompt changed: " + str);
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

        RTConsole.Get().SetShowUnityDebugLogInConsole(true);

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
#endif

        
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
    }
    
    private void OnDestroy()
    {
        print("Game logic destroyed");
    }

    public void SetToolsVisible(bool bNew)
    {
        RTUtil.FindIncludingInactive("ToolsCanvas").SetActive(bNew);
    }
    // Update is called once per frame
    void Update()
    {
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


        if (Input.GetKeyDown(KeyCode.U)
            ||
            Input.GetKeyDown(KeyCode.M)
            || 
            (
             (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
             &&   
                Input.GetKeyDown(KeyCode.I)
            ))
        {

            if (GUIIsBeingUsed()) return;

                GameObject go = GetPicWereHoveringOver();

            if (go)
            {
                PicTextToImage TTIscript = go.GetComponent<PicTextToImage>();
                PicMain picScript = go.GetComponent<PicMain>();
                PicMask picMaskScript = go.GetComponent<PicMask>();

                if (Input.GetKeyDown(KeyCode.U))
                {
                    picScript.UndoImage();
                }

                if (Input.GetKeyDown(KeyCode.M))
                {
                    picMaskScript.OnToggleMaskViewButton();
                }

                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    //Ctrl-?
                    if (Input.GetKeyDown(KeyCode.I))
                    {
                        picScript.InvertMask();
                    }

                }

            }
        }
    }

}
