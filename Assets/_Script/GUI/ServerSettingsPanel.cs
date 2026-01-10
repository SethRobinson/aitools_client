using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System;


public class ServerSettingsPanel : MonoBehaviour
{
    public TMP_Dropdown _presetDropdown;
    public TMP_Text m_titleText;
    int _serverID = -1;
    public TMP_Text m_settingsText;
    public TMP_InputField m_jobListInputField;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    public void AddEveryItemToJobList(ref List<string> joblist)
    {
        List<string> jobsToAdd = GetPicJobListAsListOfStrings();
        foreach (string job in jobsToAdd)
        {
            joblist.Add(job);
        }
    }

    public List<string> GetPicJobListAsListOfStrings()
    {
        // Delegate to GameLogic which handles multi-line @end blocks
        return GameLogic.Get().GetPicJobListAsListOfStrings(m_jobListInputField.text);
    }
    public void SetJobList(string joblist)
    {
        m_jobListInputField.text = joblist;
    }
    public void AddJobToJobList(string job, ref string jobList)
    {
        if (jobList.Length > 0)
        {
            jobList += "\n";
        }
        jobList += job;
    }


    public void OnPresetDropdownChanged(int selectedIndex)
    {
        
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        //get the text of the selected option
        string selected = _presetDropdown.options[_presetDropdown.value].text;
        //RTConsole.Log("Chose " + selected);

        if (selected == "<no selection>")
        {
            //special case
            m_jobListInputField.text = "";
        }

        var preset = PresetManager.Get().LoadPreset(selected, PresetManager.Get().GetActivePreset());

        m_jobListInputField.text = preset.JobList;

        //set the server's comfyUI workflow
        // serverInfo._comfyUIWorkFlowOverride = _presetDropdown.value;

    }
    public void Init(int serverID)
    {
        _serverID = serverID;

        // Replace the title
        m_titleText.text = "Server " + serverID + " Settings";
    
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        // Temporarily remove the event listener
        _presetDropdown.onValueChanged.RemoveListener(OnPresetDropdownChanged);

        // Initialize the dropdown value without triggering OnComfyUIDropdownChanged
        PresetManager.Get().PopulatePresetDropdown(_presetDropdown, true);

        // Re-add the listener
        _presetDropdown.onValueChanged.AddListener(OnPresetDropdownChanged);

        //set our info
        m_settingsText.text = "URL: " + serverInfo.remoteURL;
        m_jobListInputField.text = serverInfo._jobListOverride;
    }

    public void OnJobListChanged()
    {
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        serverInfo._jobListOverride = m_jobListInputField.text.Trim();

    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
