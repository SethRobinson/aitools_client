using UnityEngine;
using System.IO;
using System;

/// <summary>
/// Manages user preferences that persist across sessions.
/// Saves/loads from config_preferences.txt on application quit/startup.
/// </summary>
public class UserPreferences : MonoBehaviour
{
    private static UserPreferences _this;
    private const string PREFERENCES_FILE = "config_preferences.txt";

    // Saved preference values
    public string MainJobScript { get; set; } = "";
    public string TempJobScript { get; set; } = "";
    public string AIGuidePreset { get; set; } = "";
    public string AdventurePreset { get; set; } = "";
    public string DefaultAutoPicScript { get; set; } = "AutoPic.txt";
    // Adventure mode quote highlight color (hex format like #FFFF66). Empty string disables quote coloring.
    public string AdventureQuoteColor { get; set; } = "#FFFF66";

    private void Awake()
    {
        _this = this;
    }

    public static UserPreferences Get()
    {
        return _this;
    }

    /// <summary>
    /// Load preferences from config_preferences.txt
    /// </summary>
    public void Load()
    {
        if (!File.Exists(PREFERENCES_FILE))
        {
            RTConsole.Log("No " + PREFERENCES_FILE + " found, using defaults.");
            return;
        }

        try
        {
            using (StreamReader reader = new StreamReader(PREFERENCES_FILE))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ProcessLine(line);
                }
            }
            RTConsole.Log("Loaded user preferences from " + PREFERENCES_FILE);
        }
        catch (IOException ex)
        {
            RTConsole.Log("Error reading " + PREFERENCES_FILE + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Process a single line from the preferences file
    /// </summary>
    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            return;

        string[] parts = line.Split('|');
        if (parts.Length < 2)
            return;

        string key = parts[0].Trim();
        string value = parts[1].Trim();

        switch (key)
        {
            case "main_job_script":
                MainJobScript = value;
                break;
            case "temp_job_script":
                TempJobScript = value;
                break;
            case "aiguide_preset":
                AIGuidePreset = value;
                break;
            case "adventure_preset":
                AdventurePreset = value;
                break;
            case "default_autopic_script":
                DefaultAutoPicScript = value;
                break;
            case "adventure_quote_color":
                AdventureQuoteColor = value;
                break;
        }
    }

    /// <summary>
    /// Save current preferences to config_preferences.txt
    /// </summary>
    public void Save()
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(PREFERENCES_FILE, false))
            {
                writer.WriteLine("# User Preferences - Auto-saved on exit");
                writer.WriteLine("# Do not edit while the application is running");
                writer.WriteLine();

                if (!string.IsNullOrEmpty(MainJobScript))
                    writer.WriteLine("main_job_script|" + MainJobScript + "|");

                if (!string.IsNullOrEmpty(TempJobScript))
                    writer.WriteLine("temp_job_script|" + TempJobScript + "|");

                if (!string.IsNullOrEmpty(AIGuidePreset))
                    writer.WriteLine("aiguide_preset|" + AIGuidePreset + "|");

                if (!string.IsNullOrEmpty(AdventurePreset))
                    writer.WriteLine("adventure_preset|" + AdventurePreset + "|");

                if (!string.IsNullOrEmpty(DefaultAutoPicScript))
                    writer.WriteLine("default_autopic_script|" + DefaultAutoPicScript + "|");

                // Always write adventure_quote_color (empty string means disabled)
                writer.WriteLine("adventure_quote_color|" + (AdventureQuoteColor ?? "") + "|");
            }
            RTConsole.Log("Saved user preferences to " + PREFERENCES_FILE);
        }
        catch (IOException ex)
        {
            RTConsole.Log("Error saving " + PREFERENCES_FILE + ": " + ex.Message);
        }
    }
}

