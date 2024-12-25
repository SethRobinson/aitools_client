using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using TMPro;
using UnityEngine.UI;
using System.IO;
using System;
using System.Linq;

public class ComfyUIPanel : MonoBehaviour
{

    static ComfyUIPanel _this;
    public CanvasGroup _canvasGroup;
    bool m_bFirstTimeToShow = true;
    public TMP_InputField m_inputFrameCount;

    static public ComfyUIPanel Get()
    {
        return _this;
    }
    public int GetFrameCount()
    {
        return int.Parse(m_inputFrameCount.text);
    }

    void Awake()
    {
        _this = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        
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

    // Update is called once per frame
    void Update()
    {
        
    }
}
