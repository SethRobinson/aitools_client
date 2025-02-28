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
    public Toggle m_autoSavePNGToggle;
    public Toggle m_stripThinkTagsToggle;

    public CanvasGroup _canvasGroup;
    bool m_bFirstTimeToShow = true;
    static GenerateSettingsPanel _this;

    static public GenerateSettingsPanel Get()
    {
        return _this;
    }

    void Awake()
    {
        _this = this;
    }


    // Start is called before the first frame update
    void Start()
    {
        //fill in default values
        m_randomizeToggle.isOn = GameLogic.Get().GetRandomizePrompt();
        m_cameraFollowToggle.isOn = GameLogic.Get().GetCameraFollow();
        m_autoSaveToggle.isOn = GameLogic.Get().GetAutoSave();
        m_autoSavePNGToggle.isOn = GameLogic.Get().GetAutoSavePNG();
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

    public void OnAutoSavePNGToggleChanged(bool bNew)
    {
        GameLogic.Get().SetAutoSavePNG(bNew);
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
                // LoadAndProcessConfig();
                m_bFirstTimeToShow = false;
            }

            ShowWindow();
        }
        else
        {
            HideWindow();
        }
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
