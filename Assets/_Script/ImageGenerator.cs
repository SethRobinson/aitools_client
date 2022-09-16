using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class ScheduledGPUEvent
{
    public string mode = "upscale";
    public GameObject targetObj = null;
}

public class ImageGenerator : MonoBehaviour
{
    static ImageGenerator _this = null;
    bool m_generateActive = false;
    public Button m_generateButton;
    public GameObject m_pic_prefab;
    public TMPro.TextMeshProUGUI m_generateButtonText;
    GameObject m_picGenerator = null; //null if a pic isn't generating something
     LinkedList<ScheduledGPUEvent> m_gpuEventList = new LinkedList<ScheduledGPUEvent>();

    Vector3 vStartPos = new Vector3(0, 0, 7);
    Vector3 vSpawnPos;
    float spacingX = 5.12f;
    float spacingY = 5.38f; //extra space for the menu too
    float rowMovedSoFar = 0;

    Vector3 m_oldCamPos;
    float m_oldCamSize;
    Camera m_camera;
    static public ImageGenerator Get()
    {
        return _this;
    }
    // Start is called before the first frame update
    private void Awake()
    {
        _this = this;
    }

    public void ScheduleGPURequest(ScheduledGPUEvent request)
    {
        m_gpuEventList.AddLast(request);
        //Debug.Log("Scheduled GPU event, " + m_gpuEventList.Count + " total");
    }

    public void OnStartingPicGenerator(GameObject go)
    {
        if (m_generateActive)
        {
            SetGenerate(false);
        }

        //now start up ours
        SetGenerate(true);
        m_picGenerator = go;
    }

    void Start()
    {
        UpdateGenerateButtonStatus();
        Reset();
        m_camera = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
        //oh, let's remember the original camera position so we can return to it
        m_oldCamPos = m_camera.transform.position;
        m_oldCamSize = m_camera.orthographicSize;
        //test area

        //CreateNewPic(); //add a blank pic at startup
        //AddImageByFileName("large_pic.png");
        //AddImageByFileName("black_and_white.png");

        //AddImageByFileName("tall_pic_test.png");
        //AddImageByFileName("wide_pic_test.jpg");
        //AddImageByFileName("square_pic_test.png");
        //m_generateActive = false;
    }

    public void SetButtonColor(Button but, Color col)
    {
        var colors = but.colors;
        colors.normalColor = col;
        colors.selectedColor = col;
        colors.highlightedColor = col;
        but.colors = colors;
    }

    public void UpdateGenerateButtonStatus()
    {
        if (m_generateActive)
        {
            SetButtonColor(m_generateButton, new Color(1, 0, 0, 1));
            m_generateButtonText.text = "Stop";
        } else
        {
            m_generateButtonText.text = "Generate";
            SetButtonColor(m_generateButton, new Color(0, 1, 0, 1));

        }
    }
    public void SetGenerate(bool bGenerate)
    {
        
        m_generateActive = bGenerate;

        if (m_generateActive)
        {
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
                } else
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
        if (rowMovedSoFar+epsilon >= spacingX * GameLogic.Get().GetPicsPerRow())
        {
            rowMovedSoFar = 0;
            vSpawnPos.x = 0;
            vSpawnPos.y -= spacingY;
        }
    }

    public GameObject CreateNewPic()
    {
        GameObject pic = Instantiate(m_pic_prefab, vSpawnPos, Quaternion.identity);
        pic.transform.parent = RTUtil.FindObjectOrCreate("Pics").transform;
        AdvancePositionForNextPic();
        return pic;
    }

    public void AddImageByFileName(string fname)
    {
        GameObject pic = CreateNewPic();
        PicMain picScript = pic.GetComponent<PicMain>();
        PicMask picMask = pic.GetComponent<PicMask>();
        picScript.LoadImageByFilename(fname, false);
        picMask.ResizeMaskIfNeeded();
    }

    public void ReorganizePics()
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

        m_camera.transform.position = m_oldCamPos;
        m_camera.orthographicSize = m_oldCamSize;
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
        if (m_gpuEventList.Count > 0)
        {
            int gpuToUse = -1;

            for (int i = 0; i < Config.Get().GetGPUCount(); i++)
            {
                if (!Config.Get().IsGPUBusy(i))
                {
                    gpuToUse = i;
                    break;
                }
            }

            if (gpuToUse == -1)
            {
                //waiting
                return;
            }
            
            ScheduledGPUEvent e = m_gpuEventList.First.Value;
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
                    script.SetGPU(gpuToUse);
                    script.StartInpaint();
                }

                else if (e.mode == "rerender")
                {
                    var script = e.targetObj.GetComponent<PicTextToImage>();
                    script.SetGPU(gpuToUse);
                    script.StartGenerate();
                }
                else
                {
                    Debug.LogError("Unknown mode: " + e.mode);
                }
            }

        }

        if (!m_generateActive || m_picGenerator!= null)
        {
            return;
        }

        for (int i = 0; i < Config.Get().GetGPUCount(); i++)
        {
            if (!Config.Get().IsGPUBusy(i))
            {
                GameObject pic = CreateNewPic();
                PicTextToImage scriptAI = pic.GetComponent<PicTextToImage>();
                PicUpscale processAI = pic.GetComponent<PicUpscale>();

                processAI.SetGPU(i);
                scriptAI.SetGPU(i);
                scriptAI.StartWebRequest(false);
            }
        }
    }
}