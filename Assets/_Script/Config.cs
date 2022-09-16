﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DG.Tweening.Plugins.Core.PathCore;

public class GPUInfo
{
    public int remoteGPUID;
    public string remoteURL;
    public bool IsGPUBusy;
}

public class Config : MonoBehaviour
{  
    public static bool _isTestMode = false; //could do anything, _testMode is checked by random functions
    public const float _clientVersion = 0.1f;

    List<GPUInfo> m_gpuInfo = new List<GPUInfo>();
    
    static Config _this;
    string m_configText; //later move this to a config.txt or something
    const string m_configFileName = "config.txt";
    bool m_safetyFilter = true;
   
    float m_version = 0.1f;
    string m_imageEditorPathAndExe = "none set";

    public string GetVersionString() { return m_version.ToString("0.0"); }

    void Awake()
    {
#if RT_BETA

#endif
        _this = this;
    }

    public string GetImageEditorPathAndExe() { return m_imageEditorPathAndExe; }
    private void Start()
    {

        m_configText = LoadConfigFromFile();

        if (m_configText == "")
        {
            //default
            m_configText += "#add as many servers as you want, just replace the localhost:8000 part with the";
            m_configText += "#server name/ip and port.\n";
            m_configText += "\n";
            m_configText += "add_server|http://localhost:8000\n\n";
            m_configText += "#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto\n";
            m_configText += "#update in here.\n\n";
            m_configText += "set_image_editor|C:\\Program Files\\Adobe\\Adobe Photoshop 2022\\Photoshop.exe\n";
        }

        ProcessConfigString(m_configText);

    }

    public bool IsValidGPU(int gpu)
    {
        return (gpu < GetGPUCount() && gpu >= 0);
    }

    public bool GetSafetyFilter() { return m_safetyFilter; }
    public void SetSafetyFilter(bool bNew) 
    {
        
        m_safetyFilter = bNew;
        if (!bNew)
            Debug.Log("Safety filter disabled due to -disable_filter");

    }
    public bool IsAnyGPUFree()
    {

        for (int i=0; i < m_gpuInfo.Count; i++)
        {
            if (!IsGPUBusy(i)) return true;
        }

        return false;
    }

    public string GetConfigText()
    {
        return m_configText;
    }
    public bool IsGPUBusy(int gpuID)
    {
        if (!IsValidGPU(gpuID)) return true;
        
        return m_gpuInfo[gpuID].IsGPUBusy;
    }

    public void SetGPUBusy(int gpuID, bool bNew)
    {
        if (IsValidGPU(gpuID))
        {
            m_gpuInfo[gpuID].IsGPUBusy = bNew;
        }
       
    }
    

    public void AddGPU(GPUInfo g)
    {
        m_gpuInfo.Add(g);
    }

  public void SaveConfigToFile()
   {
        try
        {
            using (StreamWriter writer = new StreamWriter(m_configFileName, false))
            {
                writer.Write(m_configText);
            }

        }
        catch (IOException ioex)
        {
            Debug.Log("Couldn't write config.txt out. (" + ioex.Message + ")");
        }
    }

    string LoadConfigFromFile()
    {

        string config = "";

        try
        {
            using (StreamReader reader = new StreamReader(m_configFileName))
            {
                config = reader.ReadToEnd();
            }

        }
        catch (FileNotFoundException e)
        {
            Debug.Log("No config.txt file, using defaults ("+e.Message+")");
        }
        
        return config;
    }
    public void ProcessConfigString(string newConfig)
    {

        ImageGenerator.Get().ShutdownAllGPUProcesses();

        //reset old config. This will likely do bad things if you're using GPUs at the time of loading
        m_gpuInfo = new List<GPUInfo>();
        m_configText = newConfig;

        //process it line by line

        using (var reader = new StringReader(m_configText))
        {
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                
                // Do something with the line
                string[] words = line.Trim().Split('|');
                if (words[0] == "-disable_filter" || words[0] == "disable_filter")
                {
                    //another way to disable the safety filter, possibly the only way when it comes to say,
                    //a web build
                    SetSafetyFilter(false);
                } else
                if (words[0] == "add_server")
                {
                    //Debug.Log("Adding server " +words[1]);
                    //let's ask the server what it can do, and add its virtual gpus to our list if possible
                    GameObject go = new GameObject("ServerRequest");
                    go.transform.parent = transform;
                    WebRequestServerInfo webScript = (WebRequestServerInfo) go.AddComponent<WebRequestServerInfo>();
                    webScript.StartWebRequest(words[1]);

                }
                else
                if (words[0] == "set_image_editor")
                {
                    //Debug.Log("Adding server " +words[1]);

                    m_imageEditorPathAndExe = words[1];

                }


               
                    
                    else
                {
                    //Debug.Log("Processing " + line);
                }
            }
        }

    }

    public GPUInfo GetGPUInfo(int index) { return m_gpuInfo[index]; }
    public int GetGPUCount() { return m_gpuInfo.Count; }
    static public Config Get() { return _this; }

}