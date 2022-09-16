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

public class GameLogic : MonoBehaviour
{

    public GameObject m_notepadTemplatePrefab;
    static GameLogic _this = null;
    string m_prompt = "";
    int m_steps = 50;
    float m_upscale = 2.0f; //1 means noc hange
    bool m_fixFaces = false;
    float m_textStrength = 7.5f;
    float m_inpaintStrength = 0.80f;
    float m_noiseStrength = 0;
    float m_penSize = 20;
    public TMP_InputField m_inputField;
    public Button m_generateButton;
    float m_alphaMaskFeatheringPower = 2;
    bool m_bLoopSource = false;
    bool m_inpaintMaskActive = false;
    int m_picsPerRow = 20;
    public ImageGenerator m_AIimageGenerator;
  
    public Slider m_penSlider;
    public static string GetName()
    {
        return Get().name;
    }
  
    private void Awake()
    {
        _this = this;
        Application.targetFrameRate = 20000;
        QualitySettings.vSyncCount = 0;
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
        RaycastHit2D hit = Physics2D.Raycast(ray, ray);
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
      return EventSystem.current.IsPointerOverGameObject() || m_inputField.isFocused;
    }
    public void OnLoopSourceButton(bool bNew)
    {
        m_bLoopSource = bNew;
    }

    public bool GetLoopSource()
    {
        return m_bLoopSource;
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
    public int GetSteps() { return m_steps; }
    public bool GetFixFaces() { return m_fixFaces; }
    public float GetUpscale() { return m_upscale; }
    public void OnStepsChanged(string steps)
    {
        //Debug.Log("Steps changed to " + steps);
        int.TryParse(steps, out m_steps);
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
    public float GetInpaintStrength() { return m_inpaintStrength; }

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
    public float GetTextStrength() { return m_textStrength; }

    public void OnUpscaleChanged(bool upscale)
    {
       if (upscale)
        {
            m_upscale = 2.0f;
        } else
        {
            m_upscale = 1.0f;
        }
    }

    public void OnFixFacesChanged(bool bNew)
    {
        //Debug.Log("Steps changed to " + steps);
        m_fixFaces = bNew;
    }

    public void OnPromptChanged(String str)
    {
        m_prompt = str;
        //Debug.Log("Prompt changed: " + str);
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
    }

    static public GameLogic Get()
	{
		return _this;
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

    // Update is called once per frame
    void Update()
    {

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