using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ServerButtonScript : MonoBehaviour
{

    public TextMeshProUGUI m_text;
    int m_buttonIndex = -1;
    bool m_bIsBusy = false;
    bool m_bSupportsAITools = true;

    // Start is called before the first frame update
    void Start()
    {
        
    }
    public void Setup(int buttonIndex, bool bSupportsAITools)
    {
        m_buttonIndex = buttonIndex;
        m_bSupportsAITools = bSupportsAITools;
        UpdateText();
    }
    // Update is called once per frame
   
    void UpdateText()
    {
        string busy = "";
        if (m_bIsBusy)
        {
            busy = " <color=red>(busy)</color>";
        }
        string aitools = "";
        if (m_bSupportsAITools)
        {
            aitools = "AIT";
        } else
        {
            aitools = "1111";
        }
        m_text.text = aitools+"Server " + m_buttonIndex.ToString() + " WebUI" + busy;
    }
    public void OnSetBusy(bool bBusy)
    {
        m_bIsBusy = bBusy;
        UpdateText();
    }

    public void OnClick()
    {
        if (!Config.Get().IsValidGPU(m_buttonIndex))
        {
            Debug.Log("Invalid GPU");
            return;
        }
        string url = Config.Get().GetGPUInfo(m_buttonIndex).remoteURL;

        Debug.Log("Clicked server " + m_buttonIndex +", opening "+ url+" in webbrowsers");

        RTUtil.PopupUnblockOpenURL(url);
    }
}
