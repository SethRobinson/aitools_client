using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;


public class PresetFileConfigExtractor
{
    public string JobList { get; private set; }
    public string default_prompt { get; private set; }
    public string default_negative_prompt { get; private set; }
    public string default_pre_prompt { get; private set; }

    //this is kind of a lazy catch all for everything I need from config files.  They don't all have to be in a single .txt file
  
    private static readonly Dictionary<string, Action<PresetFileConfigExtractor, string>> extractors = new Dictionary<string, Action<PresetFileConfigExtractor, string>>()
            {
                { "joblist", (ce, data) => ce.JobList = data },
                { "default_prompt", (ce, data) => ce.default_prompt = data },
                { "default_negative_prompt", (ce, data) => ce.default_negative_prompt = data },
              { "default_pre_prompt", (ce, data) => ce.default_pre_prompt = data }
           };

    public void ExtractInfoFromString(string text)
    {
        // Match COMMAND_START|...COMMAND_END blocks
        var commandBlocks = Regex.Matches(text, @"COMMAND_START\|(?<command>[^\n]+)\n(?<data>.*?)COMMAND_END", RegexOptions.Singleline);

        foreach (Match block in commandBlocks)
        {
            string command = block.Groups["command"].Value.Trim();
            string data = block.Groups["data"].Value.Trim();

            if (extractors.ContainsKey(command))
            {
                extractors[command](this, data);
            }
        }

        // Match COMMAND_SET|... lines
        var setCommands = Regex.Matches(text, @"COMMAND_SET\|(?<command>[^\|]+)\|(?<value>[^\|]+)");

        foreach (Match setCommand in setCommands)
        {
            string command = setCommand.Groups["command"].Value.Trim();
            string value = setCommand.Groups["value"].Value.Trim().Split('#')[0].Trim();

            if (extractors.ContainsKey(command))
            {
                extractors[command](this, value);
            }
        }
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public string MakeCommandChunk(string command, string value)
    {
        return "COMMAND_START|" + command + "\n" + value + "\nCOMMAND_END\n";
    }
    public string MakeCommandLine(string command, string value)
    {
        return "COMMAND_SET|" + command + "\n" + value + "\n";
    }
}


public class PresetManager : MonoBehaviour
{
    public GameObject _getStringPrefab;
    static PresetManager _this;
    PresetFileConfigExtractor presetFileConfig = new PresetFileConfigExtractor();

    private void Awake()
    {
        _this = this;
    }

    public static PresetManager Get()
    {
        return _this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    public PresetFileConfigExtractor LoadPreset(string fileName)
    {
        string path = "Presets/" + fileName;
        StreamReader reader = new StreamReader(path);
        string text = reader.ReadToEnd();
        reader.Close();
        presetFileConfig = new PresetFileConfigExtractor();
        presetFileConfig.ExtractInfoFromString(text);

        return presetFileConfig;
    }
    public void LoadPresetAndApply(string fileName)
    {
        string path = "Presets/" + fileName;
        StreamReader reader = new StreamReader(path);
        string text = reader.ReadToEnd();
        reader.Close();
        presetFileConfig = new PresetFileConfigExtractor();
        presetFileConfig.ExtractInfoFromString(text);
        // Apply the settings

        if (presetFileConfig.JobList != null)
            GameLogic.Get().SetJobList(presetFileConfig.JobList.Trim());

        if (presetFileConfig.default_prompt != null)
            GameLogic.Get().SetPrompt(presetFileConfig.default_prompt.Trim());

        if (presetFileConfig.default_negative_prompt != null)
            GameLogic.Get().SetNegativePrompt(presetFileConfig.default_negative_prompt.Trim());

        if (presetFileConfig.default_pre_prompt != null)
            GameLogic.Get().SetComfyUIPrompt(presetFileConfig.default_pre_prompt.Trim());
    }
    public void SavePreset(string fileName)
    {
        string path = "Presets/" + fileName;
        StreamWriter writer = new StreamWriter(path);

        if (GameLogic.Get().GetJobListAsSingleString() != "")
            writer.Write(presetFileConfig.MakeCommandChunk("joblist", GameLogic.Get().GetJobListAsSingleString()));
        
        if (GameLogic.Get().GetPrompt() != "")
            writer.Write(presetFileConfig.MakeCommandChunk("default_prompt", GameLogic.Get().GetPrompt()));
     
        if (GameLogic.Get().GetNegativePrompt() != "")
            writer.Write(presetFileConfig.MakeCommandChunk("default_negative_prompt", GameLogic.Get().GetNegativePrompt()));

        if (GameLogic.Get().GetComfyUIPrompt() != "")
            writer.Write(presetFileConfig.MakeCommandChunk("default_pre_prompt", GameLogic.Get().GetComfyUIPrompt()));

        writer.Close();

        RTConsole.Log("Preset saved as: " + fileName);

        PopulatePresetDropdown(GameLogic.Get().GetPresetDropdown());
        GameLogic.Get().SetPresetDropdownValue(fileName);
    }
    // Update is called once per frame
    void Update()
    {
        
    }

    public bool DoesPresetExistByNameNotCaseSensitive(string presetToLookFor)
    {
        string[] files = Directory.GetFiles("Presets", "*.txt");
        foreach (string file in files)
        {
            // Get the name of the file
            string name = Path.GetFileName(file);
            if (name.ToLower() == presetToLookFor.ToLower())
            {
                return true;
            }
        }
        return false;
    }
   
    public void PopulatePresetDropdown(TMP_Dropdown dropdown, bool bAddNone = false)
    {
        // First, delete everything from the dropdown
        dropdown.ClearOptions();

        // Load the ComfyUI workflows
        string[] files = Directory.GetFiles("Presets", "*.txt");

        List<string> options = new List<string>();
        int defaultIndex = dropdown.value;
        if (bAddNone)
        {
            options.Add("<no selection>");
        }

        foreach (string file in files)
        {
            // Get the name of the file
            string name = Path.GetFileName(file);
            options.Add(name);
        }

        // Add options to the dropdown
        dropdown.AddOptions(options);

        // Set the default selection
        if (defaultIndex != -1)
        {
            dropdown.value = defaultIndex;
        } 

    }

    public void OnClickedPresetLoad()
    {
        LoadPresetAndApply(GameLogic.Get().GetNameOfActivePreset());
        RTConsole.Log("Loaded preset " + GameLogic.Get().GetNameOfActivePreset());
    }
    public void OnClickedPresetRefresh()
    {
        PopulatePresetDropdown(GameLogic.Get().GetPresetDropdown());
        RTQuickMessageManager.Get().ShowMessage("Reloaded presets");
    }

    public void OnClickedPresetSave()
    {
        GameObject getStringGO = Instantiate(_getStringPrefab);
        GetStringDialog getStringScript = getStringGO.GetComponentInChildren<GetStringDialog>();
        getStringScript.Init("Enter Preset Name to Save/Save As", GameLogic.Get().GetNameOfActivePreset());
        getStringScript.m_onClickedSubmitCallback += OnPresetSubmit;
        getStringScript.m_onClickedCancelCallback += OnPresetCanceled;
    }

    public void OnPresetSubmit(string fileName)
    {
        SavePreset(fileName);
    }
    public void OnPresetCanceled(string fileName)
    {
        RTConsole.Log("Preset save canceled");
    }

}
