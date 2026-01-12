using TMPro;
using UnityEngine;


public class ServerButtonScript : MonoBehaviour
{
    //define an enum with server type A1111, AIT, and COMFYUI
    RTRendererType m_serverType = RTRendererType.A1111;

    public TextMeshProUGUI m_text;
    int m_buttonIndex = -1;
    bool m_bIsBusy = false;
  
    // Start is called before the first frame update
    void Start()
    {
        
    }
    public void OnSettingsButton()
    {
        // Use the new static Toggle method - creates panel dynamically
        ServerSettingsPanel.Toggle(m_buttonIndex);
    }
    public void Setup(GPUInfo g)
    {
        m_serverType = g._requestedRendererType;
        m_buttonIndex = g.localGPUID;
        UpdateText();
    }
    // Update is called once per frame
   
    void UpdateText()
    {

        if (!Config.Get().IsValidGPU(m_buttonIndex))
        {
            Debug.Log("Invalid GPU/Server");
            return;
        }
     

        string busy = "";
        if (m_bIsBusy)
        {
            busy = " <color=red>(busy)</color>";
        }
        string aitools = "";
       
       
        if (m_serverType == RTRendererType.ComfyUI)
        {
            aitools = "Comfy";
        } else
        if (m_serverType == RTRendererType.OpenAI_Image)
        {
            aitools = "OpenAI Image";
        }


        m_text.text = aitools;

        if (Config.Get().GetGPUInfo(m_buttonIndex)._name != "")
        {
            m_text.text = Config.Get().GetGPUInfo(m_buttonIndex)._name;
        }

        m_text.text += " Server " + m_buttonIndex.ToString() + "" + busy;
    }
    public void OnSetBusy(bool bBusy)
    {
        m_bIsBusy = bBusy;
        UpdateText();
    }

    public void OnClickedEnableCheckbox(bool bEnable)
    {
        if (!Config.Get().IsValidGPU(m_buttonIndex))
        {
            Debug.Log("Invalid GPU/Server");
            return;
        }
        Config.Get().GetGPUInfo(m_buttonIndex)._bIsActive = bEnable;

        
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
