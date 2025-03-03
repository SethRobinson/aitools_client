using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PicGenerator : MonoBehaviour
{
    public PicMain m_picScript;
    bool m_bIsGenerating = false;
    bool m_bDidTagObjectToBeNewSource = false;

    public bool GetIsGenerating() 
    {
        return m_bIsGenerating; 
    }

    public void SetIsGenerating(bool bNew)
    {
        if (bNew == m_bIsGenerating) return; //no change

        m_bIsGenerating = bNew; 
        
        if (!bNew)
        {
            m_picScript.SetStatusMessage("");
            ImageGenerator.Get().SetGenerate(false);
            return;
        }

        //they want to start generating, ok


        if (GameLogic.Get().IsActiveModelPix2Pix() && GameLogic.Get().GetUseControlNet())
        {
            RTQuickMessageManager.Get().ShowMessage("ControlNet won't work with a pix2pix model loaded! Change model or disable ControlNet.");
            m_bIsGenerating = false;
            return;
        }

        m_bIsGenerating = true;
        ImageGenerator.Get().OnStartingPicGenerator(gameObject);

    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!m_bIsGenerating) return;
        if (m_picScript.IsBusyBasic()) return;

        m_picScript.SetStatusMessage("###SOURCE###");

        if (Config.Get().IsAnyGPUFree())
        {
            m_picScript.FillAlphaMaskIfBlank();

            GameObject go = gameObject;

            if (!GameLogic.Get().GetTurbo())
            {

                //create new object
                go = m_picScript.Duplicate();
                m_bDidTagObjectToBeNewSource = true; //fake like we did it
            

            }

            //tell it what to do ASAP
            PicMask picMaskScript = go.GetComponent<PicMask>();
            picMaskScript.SetMaskVisible(false); //hard to see if this is on

            //var e = new ScheduledGPUEvent();
            //e.mode = "inpaint";
            //e.targetObj = go;
            //ImageGenerator.Get().ScheduleGPURequest(e);


            //Run our active joblist on this new object
            PicMain picScript = go.GetComponent<PicMain>();
            picScript.OnTool1(); //this will run the active joblist, as well as update its prompt


            if (!m_bDidTagObjectToBeNewSource)
            {
                m_picScript.m_onFinishedRenderingCallback += OnCallbackFinished;
                m_bDidTagObjectToBeNewSource = true;
            }

            ImageGenerator.Get().IncrementGenerationAndCheckForEnd();
        }
    }

    public void OnDestroy()
    {
        ImageGenerator.Get().OnPicDestroyed(gameObject);

    }
    public void OnCallbackFinished(GameObject go)
    {
        m_bDidTagObjectToBeNewSource = false;

        if (!m_bIsGenerating) return; //we're not longer in control, ignore any renders that are finished

        if (GameLogic.Get().GetLoopSource())
        {
            //Debug.Log("Telling next thing to become the new source");
            SetIsGenerating(false);
            go.GetComponent<PicGenerator>().SetIsGenerating(true);
        }
    }

    public void OnInpaintGeneratorButton()
    {
        ImageGenerator.Get().ResetGenerateCounter();
        SetIsGenerating(!GetIsGenerating());
    }
}
