using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Exports CYOA adventures to a self-contained, playable HTML file with navigation and Go Back functionality.
/// </summary>
public class AdventureExportHTML : MonoBehaviour
{
    /// <summary>
    /// Formats dialogue text with span tags for styling.
    /// </summary>
    public static string FormatDialogue(string input)
    {
        // Match text within double quotes ("") and typographic quotes (unicode \u201C and \u201D)
        string pattern = "(\"[^\"]*\"|[\u201C][^\u201D]*[\u201D])";
        
        string result = Regex.Replace(input, pattern, match => {
            string firstQuote = match.Value.Substring(0, 1);
            string lastQuote = match.Value.Substring(match.Value.Length - 1, 1);
            string content = match.Value.Substring(1, match.Value.Length - 2);
            return $"{firstQuote}<span class=\"dialogue\">{content}</span>{lastQuote}";
        });

        return result;
    }

    /// <summary>
    /// Escapes HTML special characters.
    /// </summary>
    private string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Gets passage HTML content including text, images, and choices.
    /// </summary>
    private string GetPassageHTML(string path, AdventureText at, out List<string> linkedPassages)
    {
        linkedPassages = new List<string>();
        StringBuilder sb = new StringBuilder();
        
        string passageId = SanitizeId(at.GetName());
        var choices = at.GetChoices();
        var picsSpawned = at.GetPicsSpawned();
        
        sb.AppendLine($"  <div class=\"passage\" id=\"{passageId}\">");
        
        // Main content area (text + image side by side on wide screens)
        sb.AppendLine("    <div class=\"main-content\">");
        
        // Text content
        string textContent = FormatDialogue(at.GetTextWithoutChoices());
        // Convert newlines to <br> for HTML
        textContent = textContent.Replace("\n", "<br>\n");
        
        sb.AppendLine("      <div class=\"dialog\">");
        sb.AppendLine($"        {textContent}");
        sb.AppendLine("      </div>");
        
        // Images/Videos
        int picCount = 0;
        foreach (PicMain pic in picsSpawned)
        {
            if (pic != null)
            {
                string fileName = RTUtil.FilteredFilenameSafeToUseAsFileName(at.GetName() + "-" + picCount.ToString());
                string picFileExtension = ".png";

                if (pic.IsMovie())
                {
                    picFileExtension = pic.m_picMovie.GetFileExtensionOfMovie();
                    pic.m_picMovie.SaveMovieWithNewFilename(path + fileName + picFileExtension);
                    
                    sb.AppendLine("      <div class=\"image-container\">");
                    sb.AppendLine($"        <video controls autoplay muted loop><source src=\"{fileName}{picFileExtension}\" type=\"video/{picFileExtension.TrimStart('.')}\">Your browser does not support video.</video>");
                    sb.AppendLine("      </div>");
                }
                else
                {
                    pic.AddTextLabelToImage(AdventureLogic.Get().GetExtractor().ImageTextOverlay);
                    pic.SaveFile(path + fileName + picFileExtension, "", null, "", true, false);
                    
                    sb.AppendLine("      <div class=\"image-container\">");
                    sb.AppendLine($"        <img src=\"{fileName}{picFileExtension}\" alt=\"Scene image\">");
                    sb.AppendLine("      </div>");
                }
                
                picCount++;
            }
        }
        
        sb.AppendLine("    </div>");
        
        // Choices (always full width, below main content)
        if (choices.Count > 0)
        {
            sb.AppendLine("    <div class=\"choices\">");
            foreach (var choice in choices)
            {
                string targetId = SanitizeId(choice.identifier);
                linkedPassages.Add(choice.identifier);
                sb.AppendLine($"      <a href=\"#\" onclick=\"goTo('{targetId}'); return false;\">{EscapeHtml(choice.description)}</a>");
            }
            sb.AppendLine("    </div>");
        }
        else
        {
            // No choices - this is an ending
            string endText = AdventureLogic.Get().GetExtractor().TwineTextIfNoChoices;
            // Strip Twine/Harlowe markup since we're generating plain HTML
            endText = StripTwineMarkup(endText);
            
            sb.AppendLine("    <div class=\"choices\">");
            if (!string.IsNullOrEmpty(endText))
            {
                sb.AppendLine($"      <p class=\"ending\">{EscapeHtml(endText)}</p>");
            }
            sb.AppendLine("      <a href=\"#\" onclick=\"goTo('ADVENTURE-START'); return false;\">Start Over</a>");
            sb.AppendLine("    </div>");
        }
        
        sb.AppendLine("  </div>");
        
        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes a passage name to be a valid HTML id.
    /// </summary>
    private string SanitizeId(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";
        // Replace invalid characters with hyphens
        return Regex.Replace(name, @"[^a-zA-Z0-9\-_]", "-");
    }

    /// <summary>
    /// Strips Twine/Harlowe markup from text, returning plain text.
    /// </summary>
    private string StripTwineMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        // Remove (link:"...")[(goto:"...")] patterns
        text = Regex.Replace(text, @"\(link:[^)]+\)\s*\[\(goto:[^)]+\)\]", "");
        // Remove [[link|target]] patterns
        text = Regex.Replace(text, @"\[\[([^|\]]+)\|[^\]]+\]\]", "$1");
        // Remove [[link]] patterns
        text = Regex.Replace(text, @"\[\[([^\]]+)\]\]", "$1");
        // Remove remaining Harlowe macros like (set:...), (if:...), etc.
        text = Regex.Replace(text, @"\([a-z]+:[^)]*\)", "");
        
        return text.Trim();
    }

    /// <summary>
    /// Gets the HTML template with embedded CSS and JavaScript.
    /// </summary>
    private string GetHTMLTemplate(string title, string passages, string startPassage)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{EscapeHtml(title)}</title>
<style>
/* Base styles */
* {{
  box-sizing: border-box;
}}

html, body {{
  margin: 0;
  padding: 0;
  min-height: 100vh;
  background-color: #000;
  color: #f5f5f5;
  font-family: 'Georgia', serif;
  font-size: 1.2em;
  line-height: 1.6;
}}

/* Story container */
#story-container {{
  max-width: 1400px;
  margin: 0 auto;
  padding: 20px 40px;
}}

/* Navigation bar */
#nav-bar {{
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  background: linear-gradient(to bottom, rgba(0,0,0,0.9) 0%, rgba(0,0,0,0.7) 70%, transparent 100%);
  padding: 15px 20px;
  z-index: 1000;
  display: flex;
  gap: 15px;
}}

#nav-bar button {{
  background: #26a69a;
  color: #fff;
  border: none;
  padding: 10px 20px;
  border-radius: 5px;
  cursor: pointer;
  font-size: 0.9em;
  font-family: inherit;
  transition: background-color 0.2s, opacity 0.2s;
}}

#nav-bar button:hover {{
  background: #00796b;
}}

#nav-bar button:disabled {{
  background: #555;
  cursor: not-allowed;
  opacity: 0.5;
}}

/* Passage styles */
.passage {{
  display: none;
  padding-top: 70px;
  animation: fadeIn 0.5s ease-in-out;
}}

.passage.active {{
  display: block;
}}

@keyframes fadeIn {{
  from {{ opacity: 0; transform: translateY(10px); }}
  to {{ opacity: 1; transform: translateY(0); }}
}}

/* Main content: text and image */
.main-content {{
  display: flex;
  flex-direction: column;
  gap: 1.5em;
}}

@media (min-width: 900px) {{
  .main-content {{
    flex-direction: row;
    align-items: flex-start;
    gap: 2em;
  }}
  
  .dialog {{
    flex: 1;
    min-width: 0;
  }}
  
  .image-container {{
    flex: 0 0 auto;
    max-width: 45%;
  }}
}}

/* Image/Video styles */
.image-container img,
.image-container video {{
  max-width: 100%;
  height: auto;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0,0,0,0.5);
}}

/* Dialog/Text styles */
.dialog {{
  padding: 0 10px;
}}

.dialogue {{
  color: #cfd700;
  font-style: italic;
  font-weight: bold;
}}

/* Choices styles */
.choices {{
  margin-top: 2em;
  padding: 1.5em 0;
  border-top: 1px solid #333;
}}

.choices a {{
  display: block;
  color: #26a69a;
  text-decoration: none;
  border-bottom: 2px solid #26a69a;
  padding: 12px 0;
  margin: 8px 0;
  transition: color 0.2s, border-color 0.2s;
  max-width: 100%;
}}

.choices a:hover {{
  color: #00bfa5;
  border-color: #00bfa5;
}}

.choices .ending {{
  font-style: italic;
  color: #888;
  margin-bottom: 1em;
}}

/* Scrollbar styling */
::-webkit-scrollbar {{
  width: 10px;
}}

::-webkit-scrollbar-track {{
  background: #1a1a1a;
}}

::-webkit-scrollbar-thumb {{
  background: #444;
  border-radius: 5px;
}}

::-webkit-scrollbar-thumb:hover {{
  background: #555;
}}
</style>
</head>
<body>
<div id=""nav-bar"">
  <button id=""back-btn"" onclick=""goBack()"" disabled>← Go Back</button>
  <button onclick=""goTo('ADVENTURE-START')"">Restart</button>
</div>

<div id=""story-container"">
{passages}
</div>

<script>
// Navigation state
let history = [];
let currentPassage = null;

// Show a passage by ID
function show(id) {{
  // Hide all passages
  document.querySelectorAll('.passage').forEach(p => p.classList.remove('active'));
  
  // Show the target passage
  const target = document.getElementById(id);
  if (target) {{
    target.classList.add('active');
    currentPassage = id;
    
    // Scroll to top
    window.scrollTo(0, 0);
    
    // Update back button state
    document.getElementById('back-btn').disabled = history.length === 0;
  }} else {{
    console.error('Passage not found: ' + id);
  }}
}}

// Navigate to a passage, adding current to history
function goTo(id) {{
  if (currentPassage && currentPassage !== id) {{
    history.push(currentPassage);
  }}
  show(id);
}}

// Go back to previous passage
function goBack() {{
  if (history.length > 0) {{
    const prev = history.pop();
    show(prev);
  }}
}}

// Initialize - show start passage
document.addEventListener('DOMContentLoaded', function() {{
  show('{SanitizeId(startPassage)}');
}});
</script>
</body>
</html>";
    }

    /// <summary>
    /// Main export coroutine with progress callback.
    /// </summary>
    public IEnumerator Export(Action<float, string> progressCallback = null)
    {
        RTConsole.Log("Starting HTML export...");
        progressCallback?.Invoke(0f, "Preparing export...");

        // Collect all adventure nodes
        List<GameObject> objs = new List<GameObject>();
        RTUtil.AddObjectsToListByNameIncludingInactive(RTUtil.FindObjectOrCreate("Adventures"), "AdventureText", true, objs);
        
        RTConsole.Log($"Found {objs.Count} adventure nodes");
        
        if (objs.Count == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("No adventure nodes found to export!");
            yield break;
        }

        // Create output directory
        string subdir = "/" + Config._saveDirName + "/" + "adventure_html_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        string path = Config.Get().GetBaseFileDir(subdir) + "/";
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        StringBuilder allPassages = new StringBuilder();
        string startPassage = "ADVENTURE-START";
        int processed = 0;
        int total = objs.Count;

        // Process each node
        foreach (GameObject obj in objs)
        {
            yield return null; // Spread work across frames
            
            AdventureText at = obj.GetComponent<AdventureText>();
            if (at != null)
            {
                string nodeName = at.GetName();
                
                // Skip invalid nodes
                if (nodeName == "S0" || nodeName == "?")
                {
                    processed++;
                    continue;
                }
                
                // Find start passage
                if (nodeName.Contains("ADVENTURE-START") || nodeName.Contains("START"))
                {
                    startPassage = nodeName;
                }
                
                progressCallback?.Invoke((float)processed / total, $"Processing {nodeName}...");
                
                List<string> linkedPassages;
                string passageHtml = GetPassageHTML(path, at, out linkedPassages);
                allPassages.AppendLine(passageHtml);
            }
            
            processed++;
        }

        progressCallback?.Invoke(0.9f, "Generating HTML file...");
        yield return null;

        // Generate final HTML
        string title = AdventureLogic.Get().GetAdventureName();
        string html = GetHTMLTemplate(title, allPassages.ToString(), startPassage);

        // Save file
        string fileName = path + "index.html";
        bool saveSucceeded = false;
        
        try
        {
            File.WriteAllText(fileName, html);
            RTConsole.Log($"HTML export successful! File saved at: {fileName}");
            saveSucceeded = true;
        }
        catch (Exception e)
        {
            RTConsole.LogError($"Failed to save HTML file: {e.Message}");
            RTQuickMessageManager.Get().ShowMessage($"Export failed: {e.Message}");
        }
        
        if (saveSucceeded)
        {
            progressCallback?.Invoke(1f, "Export complete!");
            yield return null;
            
            // Open in browser
            Application.OpenURL(fileName);
        }
    }
}

