using B83.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class ScheduledGPUEvent
{
    public string mode = "upscale";
    public GameObject targetObj = null;
    public bool disableTranslucency = false;
    public string promptOverride = "";
    
    public string requestedSimplePrompt = "";
    public string requestedDetailedPrompt = "";
    public RTRendererType requestedRenderer = RTRendererType.Any_Local;
}

public class ImageGenerator : MonoBehaviour
{
    static ImageGenerator _this = null;
    bool m_generateActive = false;
    bool _bAdvancedRow = false;
    public Button m_generateButton;
    public Button m_imgGenerateButton;
    public GameObject m_pic_prefab;
    public TMPro.TextMeshProUGUI m_generateButtonText;
    GameObject m_picGenerator = null; //null if a pic isn't generating something
    LinkedList<ScheduledGPUEvent> m_gpuEventList = new LinkedList<ScheduledGPUEvent>();
    LinkedList<ScheduledGPUEvent> m_eventList = new LinkedList<ScheduledGPUEvent>();
    GameObject m_lastImg2ImgGeneratorObj = null;
    Vector3 vStartPos = new Vector3(0, 0, 7);
    Vector3 vSpawnPos;
    float spacingX = 5.12f;
    float spacingY = 5.38f; //extra space for the menu too
    float rowMovedSoFar = 0;
    int m_curGenCount;
    Vector3 m_oldCamPos;
    float m_oldCamSize;
    Camera m_camera;
    public GameObject m_settingPanelPrefab;
    static public ImageGenerator Get()
    {
        return _this;
    }
    // Start is called before the first frame update
    private void Awake()
    {
        _this = this;
        m_camera = Camera.allCameras[0];
    }

    public bool IsGenerating()
    {
        return m_generateActive;
    }

    public int GetCurrentGenerationCount() { return m_curGenCount; }

    public int GetCountOfQueudCommands()
    {
        return m_gpuEventList.Count;
    }

    public void ScheduleGPURequest(ScheduledGPUEvent request)
    {
        m_gpuEventList.AddLast(request);
        //Debug.Log("Scheduled GPU event, " + m_gpuEventList.Count + " total");
    }


    public void IncrementGenerationAndCheckForEnd()
    {
        m_curGenCount++;

        if (GameLogic.Get().GetMaxToGenerate() > 0)
        {
            if (m_curGenCount >= GameLogic.Get().GetMaxToGenerate())
            {
                SetGenerate(false);

            }
        }
    }

    public void OnPicDestroyed(GameObject go)
    {
        if (go == m_lastImg2ImgGeneratorObj)
        {
            SetLastImg2ImgObject(null);
        }
    }
    public void SetLastImg2ImgObject(GameObject go)
    {
        m_lastImg2ImgGeneratorObj = go;

        //let's update the image
        m_imgGenerateButton.interactable = (go != null);

    }

    public void OnRestartLastImg2ImgGenerator()
    {
        if (m_lastImg2ImgGeneratorObj != null)
        {
            m_lastImg2ImgGeneratorObj.GetComponent<PicGenerator>().OnInpaintGeneratorButton();
        }
        else
        {
            RTQuickMessageManager.Get().ShowMessage("Restarting the last batch img2img operation won't work as nothing was started yet.");

        }
    }
    public void OnStartingPicGenerator(GameObject go)
    {

        if (m_generateActive)
        {
            SetGenerate(false);
        }

        SetLastImg2ImgObject(go);

        //now start up ours
        SetGenerate(true);
        m_picGenerator = go;
    }

    void Start()
    {
        UpdateGenerateButtonStatus();
        SetLastImg2ImgObject(null);

        Reset();
        //oh, let's remember the original camera position so we can return to it
        m_oldCamPos = m_camera.transform.position;
        m_oldCamSize = m_camera.orthographicSize;
        //test area

#if !RT_RELEASE
        //AddImageByFileName("test_image_cosmo_512.png");

        //CreateNewPic(); //add a blank pic at startup
        //AddImageByFileName("large_pic.png");
        //AddImageByFileName("black_and_white.png");

        //   AddImageByFileName("tall_pic_test.png");
        //AddImageByFileName("cosmo_tiny.bmp");
        //AddImageByFileName("square_pic_test.png");
        //m_generateActive = false;
#endif
    }

    public void UpdateGenerateButtonStatus()
    {
        if (m_generateActive)
        {
            RTUtil.SetButtonColor(m_generateButton, new Color(1, 0, 0, 1));
            m_generateButtonText.text = "Stop";
            GameLogic.Get().SetChangeModelEnabled(false);
        }
        else
        {
            m_generateButtonText.text = "Generate";
            RTUtil.SetButtonColor(m_generateButton, new Color(0, 1, 0, 1));
            GameLogic.Get().SetChangeModelEnabled(true);
        }
    }
    public void OnClickedGenerateSettingsButton()
    {
        const string panelName = "GenerateSettingsPanel";

        var existing = RTUtil.FindIncludingInactive(panelName);
        if (existing != null)
        {
            RTUtil.KillObjectByName(panelName);
            return;
        }

        GameObject genPanel = Instantiate(m_settingPanelPrefab, RTUtil.FindIncludingInactive("Canvas").transform);
        genPanel.name = panelName;

    }
    public void ResetGenerateCounter()
    {
        m_curGenCount = 0;
        GameLogic.Get().ResetLastModifiedPrompt();
    }

    public void SetGenerate(bool bGenerate)
    {
        m_generateActive = bGenerate;

        if (m_generateActive)
        {
            _bAdvancedRow = false;
            //RTConsole.Log("Starting generator...");
        }
        else
        {
            //RTConsole.Log("Stopping generator...");

            if (m_picGenerator != null)
            {
                PicGenerator picGenScript = m_picGenerator.GetComponent<PicGenerator>();

                if (picGenScript)
                {
                    picGenScript.SetIsGenerating(false);
                }
                else
                {
                    Debug.Log("Couldn't find picgen, ignoring");
                }
            }

            m_picGenerator = null;
        }

        UpdateGenerateButtonStatus();
    }
    public void OnGenerateButton()
    {
        ResetGenerateCounter();
        SetGenerate(!m_generateActive);
    }
    // Update is called once per frame
    public void Reset()
    {
        vSpawnPos = vStartPos;
        rowMovedSoFar = 0;
    }

    void AdvancePositionForNextPic()
    {
        //move for next pic
        vSpawnPos.x += spacingX;
        rowMovedSoFar += spacingX;

        const float epsilon = 0.01f; //fix a floating point issue where 5 looked like 6 here
        if (rowMovedSoFar + epsilon >= spacingX * GameLogic.Get().GetPicsPerRow())
        {
            rowMovedSoFar = 0;
            vSpawnPos.x = 0;
            vSpawnPos.y -= spacingY;
            _bAdvancedRow = true;
        }
    }

    public GameObject CreateNewPic()
    {
        GameObject pic = Instantiate(m_pic_prefab, vSpawnPos, Quaternion.identity);
        pic.transform.parent = RTUtil.FindObjectOrCreate("Pics").transform;
        PicMain picMain = pic.GetComponent<PicMain>();
        picMain.PassInTempInfo(GameLogic.Get().GetGlobalRenderer(), -1);
        if (_bAdvancedRow)
        {
            if (GameLogic.Get().GetCameraFollow())
            {
                //Debug.Log("Moving camera down a bit for new row");

                var vPos = m_camera.transform.position;
                vPos.y -= spacingY;
                m_camera.transform.position = vPos;
            }
            _bAdvancedRow = false;
        }
        AdvancePositionForNextPic();
        return pic;
    }
    public void AddImageByFileNameNoReturn(string fname)
    {
        AddImageByFileName(fname);
    }
    public GameObject AddImageByFileName(string fname) //Send a blank filename to just get a pic back
    {
        GameObject pic = CreateNewPic();
        PicMain picScript = pic.GetComponent<PicMain>();
        PicMask picMask = pic.GetComponent<PicMask>();

        if (fname.Length != 0)
        {
            picScript.LoadImageByFilename(fname, false);
            picMask.ResizeMaskIfNeeded();
        }

        return pic;
    }

    public PicMain GetPicToUseTurboOn()
    {
        //get the last pic created
        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();
        
        //if empty, add a blank pic
        if (aiScripts.Length == 0)
        {
            return CreateNewPic().GetComponent<PicMain>();
        }
        //return the last one
        return aiScripts[aiScripts.Length - 1];

    }
    public void ReorganizePics(bool bResetCamera = true)
    {
        Reset();

        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();

        foreach (PicMain picScript in aiScripts)
        {
            if (!picScript.IsDestroyed())
            {
                //move it to its new pos
                picScript.gameObject.transform.position = vSpawnPos;
                AdvancePositionForNextPic();
            }
        }
        if (bResetCamera)
        {
            m_camera.transform.position = m_oldCamPos;
            m_camera.orthographicSize = m_oldCamSize;
        }
    }

    public void ShutdownAllGPUProcesses()
    {

        SetGenerate(false);

        var aiScripts = RTUtil.FindObjectOrCreate("Pics").transform.GetComponentsInChildren<PicMain>();

        foreach (PicMain picScript in aiScripts)
        {
            if (!picScript.IsDestroyed())
            {
                picScript.KillGPUProcesses();
            }
        }
    }

    void Update()
    {
            //temporary list of gpu events, we can put them here to be rescheduled later
            LinkedList<ScheduledGPUEvent> tempGPUList = new LinkedList<ScheduledGPUEvent>();

        if (m_gpuEventList.Count > 0)
        {
            int gpuToUse = Config.Get().GetFreeGPU();

            if (Config.Get().DontHaveLocalServers())
            {
                //special case
                gpuToUse = Config.Get().GetFirstGPUIncludingOpenAI();
            }

            if (gpuToUse == -1)
            {
                //waiting
                return;
            }

            var gpuInfo = Config.Get().GetGPUInfo(gpuToUse);

            tryAgain:

            ScheduledGPUEvent e = m_gpuEventList.First.Value;
            
            //first see if we can do anything with it
            if (e.requestedRenderer != RTRendererType.Any_Local)
            {
                if (e.requestedRenderer != gpuInfo._requestedRendererType)
                {
                    //we can't do this on this GPU.. is the one they want available?
                    int newGPU = Config.Get().GetFreeGPU(e.requestedRenderer);
                    if (newGPU != -1)
                    {
                        //we can do it on this one
                        gpuToUse = newGPU;
                        gpuInfo = Config.Get().GetGPUInfo(gpuToUse);
                    }
                    else
                    {
                        //move the event to tempGPUList
                        tempGPUList.AddFirst(e);
                        m_gpuEventList.RemoveFirst();

                        if (m_gpuEventList.Count == 0)
                            {
                            //uh oh, geuss we need to get out of here.  
                            foreach (ScheduledGPUEvent g in tempGPUList)
                            {
                                m_gpuEventList.AddFirst(g);
                            }
                            return;
                        }

                            goto tryAgain;
                    }

                }
            }
            
            m_gpuEventList.RemoveFirst();

            if (e.targetObj)
            {
                if (e.mode == "upscale")
                {
                    var script = e.targetObj.GetComponent<PicUpscale>();
                    script.OnForceUpscale(gpuToUse);
                }
                else if (e.mode == "inpaint")
                {
                    var script = e.targetObj.GetComponent<PicInpaint>();
                    if (GameLogic.Get().IsActiveModelPix2Pix() && GameLogic.Get().GetUseControlNet())
                    {
                        RTQuickMessageManager.Get().ShowMessage("ControlNet won't work with a pix2pix model loaded! Change model or disable ControlNet.");
                        script.m_picScript.SetStatusMessage("");
                    }
                    else
                    {
                        script.SetGPU(gpuToUse);
                        script.StartInpaint();
                    }
                }

                else if (e.mode == "rerender")
                {
                    var script = e.targetObj.GetComponent<PicTextToImage>();

                    if (GameLogic.Get().IsActiveModelPix2Pix())
                    {
                        RTQuickMessageManager.Get().ShowMessage("The loaded model is pix2pix, can't generate, only inpaint/modify existing images.");
                        script.m_picScript.SetStatusMessage("");
                    }
                    else
                    {

                        script.SetGPU(gpuToUse);
                        script.StartGenerate();
                    }
                }
                else if (e.mode == "render")
                {
                    var script = e.targetObj.GetComponent<PicTextToImage>();

                    if (GameLogic.Get().IsActiveModelPix2Pix())
                    {
                        RTQuickMessageManager.Get().ShowMessage("The loaded model is pix2pix, can't generate, only inpaint/modify existing images.");
                        script.m_picScript.SetStatusMessage("");
                    }
                    else
                    {
                        //get gpuinfo

                        if (gpuInfo._usesDetailedPrompts)
                        {
                            if (e.requestedDetailedPrompt.Length > 0)
                            {
                                script.SetPrompt(e.requestedDetailedPrompt);
                            } else if (e.requestedSimplePrompt.Length > 0)
                            {
                                script.SetPrompt(e.requestedSimplePrompt);
                            } else
                            {
                                //default is nothing, probably already set
                            }
                        } else
                        {
                            //this renderer wants simple prompts
                            if (e.requestedSimplePrompt.Length > 0)
                            {
                                script.SetPrompt(e.requestedSimplePrompt);
                            }
                            else if (e.requestedDetailedPrompt.Length > 0)
                            {
                                script.SetPrompt(e.requestedDetailedPrompt);
                            }
                            else
                            {
                                //default is nothing, probably already set
                            }
                        }

                        //as for how we actually kickoff things..

                        if (gpuInfo._requestedRendererType == RTRendererType.OpenAI_Dalle_3)
                        {
                            var picMain = e.targetObj.GetComponent<PicMain>();
                            picMain.OnRenderWithDalle3();
                        }
                        else
                        {
                            script.SetGPU(gpuToUse);
                            script.StartGenerateInitialRender();
                        }
                    }
                }
                else if (e.mode == "interrogate")
                {
                    var script = e.targetObj.GetComponent<PicInterrogate>();
                    script.SetGPU(gpuToUse);
                    script.OnForceInterrogate(gpuToUse);
                }
                else if (e.mode == "genmask")
                {
                    var script = e.targetObj.GetComponent<PicGenerateMask>();
                    script.OnGenerateMask(gpuToUse, e.disableTranslucency);
                }
                else
                {
                    Debug.LogError("Unknown mode: " + e.mode);
                }
            }

        }

            //we add any gpu events we had to ignore for now, put them at the front
            foreach (ScheduledGPUEvent e in tempGPUList)
            {
                m_gpuEventList.AddFirst(e);
            }


        if (!m_generateActive || m_picGenerator != null)
        {
            return;
        }

        //if generate is on, we'll keep generating more images forever

        for (int i = 0; i < Config.Get().GetGPUCount(); i++)
        {

            if (!Config.Get().IsGPUBusy(i))
            {

                bool bDisableGeneration = false;

                if (GameLogic.Get().IsActiveModelPix2Pix() && Config.Get().IsGPUOfThisType(i, GameLogic.Get().GetGlobalRenderer()))
                {
                    RTQuickMessageManager.Get().ShowMessage("The loaded model is pix2pix, can't generate, can only inpaint.<BR>(Canceling generate mode)");
                    SetGenerate(false);
                    bDisableGeneration = true;
                }

                if (!bDisableGeneration)
                {
                    GameObject pic;

                    if (!Config.Get().IsGPUOfThisType(i, GameLogic.Get().GetGlobalRenderer()))
                    {
                        continue;
                    }

                    if (GameLogic.Get().GetTurbo())
                    {
                        //special handling because we're in turbo mode, we'll generate over the last image instead of creating new ones
                        pic = GetPicToUseTurboOn().gameObject;
                        if (pic.GetComponent<PicMain>().IsBusy())
                        {
                            Debug.Log("Ignoring, we're still rendering");
                            return;
                        }
                        PicTextToImage s = pic.GetComponent<PicTextToImage>();
                        s.Reset();
                    }
                    else
                    {
                        pic = CreateNewPic();
                    }

                    PicTextToImage scriptAI = pic.GetComponent<PicTextToImage>();
                    PicUpscale processAI = pic.GetComponent<PicUpscale>();
                    PicMain picScript = pic.GetComponent<PicMain>();

                    processAI.SetGPU(i);
                    
                    scriptAI.SetGPU(i);
                    scriptAI.StartWebRequest(false);
                    picScript.PassInTempInfo(GameLogic.Get().GetGlobalRenderer(), i);
                    IncrementGenerationAndCheckForEnd();
                }
            }

        }
    }
}
