using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class ServerSettingsPanel : MonoBehaviour
{
    public TMP_Dropdown m_comfyUIAPIWorkflowsDropdown;
    public TMP_Text m_titleText;
    int _serverID = -1;
    public TMP_Text m_settingsText;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }
    public void OnComfyUIDropdownChanged(int selectedIndex)
    {
        
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        //get the text of the selected option
        // string selected = m_comfyUIAPIWorkflowsDropdown.options[m_comfyUIAPIWorkflowsDropdown.value].text;
        //RTConsole.Log("Chose " + selected);

        //set the server's comfyUI workflow
        serverInfo._comfyUIWorkFlowOverride = m_comfyUIAPIWorkflowsDropdown.value;
        //if it's the last option, set it to -1 instead
        if (serverInfo._comfyUIWorkFlowOverride == m_comfyUIAPIWorkflowsDropdown.options.Count - 1)
        {
            serverInfo._comfyUIWorkFlowOverride = -1;
        }

    }
    public void Init(int serverID)
    {
        _serverID = serverID;

        // Replace the title
        m_titleText.text = "Server " + serverID + " Settings";
        GameLogic.Get().LoadComfyUIWorkFlows(m_comfyUIAPIWorkflowsDropdown, true);

        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        // Temporarily remove the event listener
        m_comfyUIAPIWorkflowsDropdown.onValueChanged.RemoveListener(OnComfyUIDropdownChanged);

        // Initialize the dropdown value without triggering OnComfyUIDropdownChanged
        if (serverInfo._comfyUIWorkFlowOverride != -1)
        {
            m_comfyUIAPIWorkflowsDropdown.value = serverInfo._comfyUIWorkFlowOverride;
        }

        // Re-add the listener
        m_comfyUIAPIWorkflowsDropdown.onValueChanged.AddListener(OnComfyUIDropdownChanged);



        //set our info
        m_settingsText.text = "URL: " + serverInfo.remoteURL;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
