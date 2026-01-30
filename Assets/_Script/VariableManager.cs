using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Manages custom variables for job scripts. Supports both text and image variables.
/// Variables are referenced using %variable_name% syntax in job scripts.
/// Variables prefixed with "global_" are stored in GameLogic's global manager.
/// </summary>
public class VariableManager
{
    private Dictionary<string, string> _textVars = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Texture2D> _imageVars = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

    // Regex pattern to match %variable_name% - captures the variable name without the % delimiters
    private static readonly Regex VariablePattern = new Regex(@"%([a-zA-Z_][a-zA-Z0-9_]*)%", RegexOptions.Compiled);

    #region Text Variables

    /// <summary>
    /// Sets a text variable value.
    /// </summary>
    public void SetText(string name, string value)
    {
        if (string.IsNullOrEmpty(name)) return;
        
        // Strip % delimiters if present
        name = StripDelimiters(name);
        _textVars[name] = value ?? "";
    }

    /// <summary>
    /// Gets a text variable value, returning defaultValue if not found.
    /// </summary>
    public string GetText(string name, string defaultValue = "")
    {
        if (string.IsNullOrEmpty(name)) return defaultValue;
        
        name = StripDelimiters(name);
        return _textVars.TryGetValue(name, out string value) ? value : defaultValue;
    }

    /// <summary>
    /// Checks if a text variable exists.
    /// </summary>
    public bool HasText(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        name = StripDelimiters(name);
        return _textVars.ContainsKey(name);
    }

    #endregion

    #region Image Variables

    /// <summary>
    /// Sets an image variable value.
    /// </summary>
    public void SetImage(string name, Texture2D texture)
    {
        if (string.IsNullOrEmpty(name)) return;
        
        name = StripDelimiters(name);
        _imageVars[name] = texture;
    }

    /// <summary>
    /// Gets an image variable value, returning null if not found.
    /// </summary>
    public Texture2D GetImage(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        
        name = StripDelimiters(name);
        return _imageVars.TryGetValue(name, out Texture2D texture) ? texture : null;
    }

    /// <summary>
    /// Checks if an image variable exists.
    /// </summary>
    public bool HasImage(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        name = StripDelimiters(name);
        return _imageVars.ContainsKey(name);
    }

    #endregion

    #region General Operations

    /// <summary>
    /// Checks if a variable exists (either text or image).
    /// </summary>
    public bool HasVariable(string name)
    {
        return HasText(name) || HasImage(name);
    }

    /// <summary>
    /// Clears a specific variable (both text and image with that name).
    /// </summary>
    public void Clear(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        
        name = StripDelimiters(name);
        _textVars.Remove(name);
        _imageVars.Remove(name);
    }

    /// <summary>
    /// Clears all variables.
    /// </summary>
    public void ClearAll()
    {
        _textVars.Clear();
        _imageVars.Clear();
    }

    /// <summary>
    /// Gets the count of text variables.
    /// </summary>
    public int TextCount => _textVars.Count;

    /// <summary>
    /// Gets the count of image variables.
    /// </summary>
    public int ImageCount => _imageVars.Count;

    #endregion

    #region Static Utilities

    /// <summary>
    /// Processes a string and replaces all %variable% patterns with their values.
    /// Variables starting with "global_" are looked up in the global manager.
    /// Unknown variables are left unchanged.
    /// </summary>
    /// <param name="input">The input string containing %variable% patterns</param>
    /// <param name="local">The local (PicMain) variable manager</param>
    /// <param name="global">The global (GameLogic) variable manager</param>
    /// <returns>The processed string with variables replaced</returns>
    public static string ProcessVariables(string input, VariableManager local, VariableManager global)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        // Use regex to find and replace all %var% patterns
        return VariablePattern.Replace(input, match =>
        {
            string varName = match.Groups[1].Value;
            string value = ResolveVariable(varName, local, global);
            
            // If we found a value, return it; otherwise keep the original %var% pattern
            return value ?? match.Value;
        });
    }

    /// <summary>
    /// Resolves a variable name to its value, checking global_ prefix routing.
    /// </summary>
    /// <param name="varName">Variable name without % delimiters</param>
    /// <param name="local">Local variable manager</param>
    /// <param name="global">Global variable manager</param>
    /// <returns>The variable value, or null if not found</returns>
    public static string ResolveVariable(string varName, VariableManager local, VariableManager global)
    {
        if (string.IsNullOrEmpty(varName)) return null;

        // Check if it's a global variable
        if (varName.StartsWith("global_", System.StringComparison.OrdinalIgnoreCase))
        {
            // Use global manager
            if (global != null && global.HasText(varName))
            {
                return global.GetText(varName);
            }
        }
        else
        {
            // Use local manager first
            if (local != null && local.HasText(varName))
            {
                return local.GetText(varName);
            }
        }

        return null; // Not found
    }

    /// <summary>
    /// Resolves a variable name to an image, checking global_ prefix routing.
    /// </summary>
    public static Texture2D ResolveImageVariable(string varName, VariableManager local, VariableManager global)
    {
        if (string.IsNullOrEmpty(varName)) return null;

        varName = StripDelimiters(varName);

        // Check if it's a global variable
        if (varName.StartsWith("global_", System.StringComparison.OrdinalIgnoreCase))
        {
            if (global != null && global.HasImage(varName))
            {
                return global.GetImage(varName);
            }
        }
        else
        {
            if (local != null && local.HasImage(varName))
            {
                return local.GetImage(varName);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts all variable names from an input string.
    /// </summary>
    /// <param name="input">The input string to search</param>
    /// <returns>List of variable names (without % delimiters)</returns>
    public static List<string> ExtractVariableNames(string input)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(input)) return names;

        var matches = VariablePattern.Matches(input);
        foreach (Match match in matches)
        {
            string varName = match.Groups[1].Value;
            if (!names.Contains(varName))
            {
                names.Add(varName);
            }
        }

        return names;
    }

    /// <summary>
    /// Checks if a string looks like a variable reference (%name%).
    /// </summary>
    public static bool IsVariableReference(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return input.StartsWith("%") && input.EndsWith("%") && input.Length > 2;
    }

    /// <summary>
    /// Strips the % delimiters from a variable name if present.
    /// </summary>
    public static string StripDelimiters(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        if (name.StartsWith("%") && name.EndsWith("%") && name.Length > 2)
        {
            return name.Substring(1, name.Length - 2);
        }
        return name;
    }

    /// <summary>
    /// Gets the appropriate variable manager for a variable name (based on global_ prefix).
    /// </summary>
    public static VariableManager GetManagerForVariable(string varName, VariableManager local, VariableManager global)
    {
        if (string.IsNullOrEmpty(varName)) return local;
        
        varName = StripDelimiters(varName);
        
        if (varName.StartsWith("global_", System.StringComparison.OrdinalIgnoreCase))
        {
            return global;
        }
        return local;
    }

    #endregion
}
