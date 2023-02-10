using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GenerateSettingsPanel : MonoBehaviour
{
    public Toggle m_randomizeToggle;
    public TMP_InputField m_maxPics;
    public TMP_Text m_statusText;
    public Toggle m_cameraFollowToggle;
    public Toggle m_autoSaveToggle;


    // Start is called before the first frame update
    void Start()
    {
        //fill in default values
        m_randomizeToggle.isOn = GameLogic.Get().GetRandomizePrompt();
        m_cameraFollowToggle.isOn = GameLogic.Get().GetCameraFollow();
        m_autoSaveToggle.isOn = GameLogic.Get().GetAutoSave();
        m_maxPics.text = GameLogic.Get().GetMaxToGenerate().ToString();
    }

    public void OnRandomizeToggleChanged(bool bNew)
    {
        GameLogic.Get().SetRandomizePrompt(bNew);
    }

    public void OnAutoSaveToggleChanged(bool bNew)
    {
        GameLogic.Get().SetAutoSave(bNew);
    }

    public void OnCameraFollowToggleChanged(bool bNew)
    {
        GameLogic.Get().SetCameraFollow(bNew);
    }

    public void OnMaxPicsChanged(string maxPics)
    {
        int result = 0;

        int.TryParse(maxPics, out result);
        GameLogic.Get().SetMaxToGenerate(result);
    }

    private void OnDestroy()
    {

    }
    // Update is called once per frame

    void Update()
    {
        string text;

        if (ImageGenerator.Get().IsGenerating())
        {
            string maxToGen;
            
            if (GameLogic.Get().GetMaxToGenerate() > 0)
            {
                maxToGen = GameLogic.Get().GetMaxToGenerate().ToString();
            } else
            {
                maxToGen = "unlimited";
            }
            
            text = "Generating "+ImageGenerator.Get().GetCurrentGenerationCount()+" of "+maxToGen+":\n";

            if (GameLogic.Get().GetLastModifiedPrompt() != "")
            {
                text += "\nLast modified prompt was:\n" + GameLogic.Get().GetLastModifiedPrompt();
            }
        }
        else
        {
            text = "Not generating";
        }

        m_statusText.text = text;
    }
}
