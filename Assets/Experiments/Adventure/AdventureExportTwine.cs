using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using System.Text.RegularExpressions;

public class AdventureExportTwine : MonoBehaviour
{

    string _twee = "";
    
    // Start is called before the first frame update
    void Start()
    {
        
    }


    /// <returns>A string with span tags added around quoted text.</returns>
    public static string FormatDialogue(string input)
    {
        // Regex to match text within double quotes ("") and typographic quotes (ÅgÅh)
        string pattern = "(\"[^\"]*\"|Åg[^Åh]*Åh)";

        // Replace matched quotes with <span class="dialogue">...</span>
        string result = Regex.Replace(input, pattern, match => {
            // Identify and extract the type of quote used (either " or Åg and Åh)
            string firstQuote = match.Value.Substring(0, 1);
            string lastQuote = match.Value.Substring(match.Value.Length - 1, 1);
            string content = match.Value.Substring(1, match.Value.Length - 2); // Extract the content inside the quotes

            return $"{firstQuote}<span class=\"dialogue\">{content}</span>{lastQuote}";
        });

        return result;
    }

    public string GetTweeText(string path, AdventureText at)
    {
        string passageTemplate = AdventureLogic.Get().GetExtractor().TwinePassage;

        passageTemplate = passageTemplate.Replace("_PASSAGE_NAME_", at.GetName());
        
        string formatted = FormatDialogue(at.GetTextWithoutChoices());
        passageTemplate = passageTemplate.Replace("_INSERT_TEXT_", formatted);

        var choices = at.GetChoices();

        var picsSpawned = at.GetPicsSpawned();

        //itterate until a valid PicMain is found (some may be dead objects)
        string imageText = "";

        int picCount = 0;

        foreach (PicMain pic in picsSpawned)
        {
            if (pic != null)
            {
                string fileName = at.GetName();

                string picFileExtension = ".png";


                //filter to be a valid filename
                fileName = RTUtil.FilteredFilenameSafeToUseAsFileName(fileName+"-"+picCount.ToString());
              

                if (pic.IsMovie())
                {
                    //actual filename of the movie
                    picFileExtension = pic.m_picMovie.GetFileExtensionOfMovie();
                    pic.m_picMovie.SaveMovieWithNewFilename(path + fileName + picFileExtension);
                    imageText += AdventureLogic.Get().GetExtractor().TwineVideo.Replace("_IMAGE_FILENAME_", fileName + picFileExtension) + "\n";
                }
                else
                {
                    pic.AddTextLabelToImage(AdventureLogic.Get().GetExtractor().ImageTextOverlay);
                    pic.SaveFile(path + fileName + picFileExtension, "", null, "", true, false);
                    imageText += AdventureLogic.Get().GetExtractor().TwineImage.Replace("_IMAGE_FILENAME_", fileName + picFileExtension) + "\n";
                }


                picCount++;

            }
        }



        //iterrate through choices and add them to the twee text
        string choiceText = "";

        if (choices.Count > 0)
        {
            choiceText += "\n";

            foreach ((string identifier, string action, string description) choice in choices)
            {
                choiceText += "[[" + choice.description + "|" + choice.identifier + "]]<BR>\n";
            }
    
        } else
        {
            if (AdventureLogic.Get().GetExtractor().TwineTextIfNoChoices != null && AdventureLogic.Get().GetExtractor().TwineTextIfNoChoices.Length > 0)
            {
                choiceText += AdventureLogic.Get().GetExtractor().TwineTextIfNoChoices;
            }
        }

        passageTemplate = passageTemplate.Replace("_INSERT_CHOICES_", choiceText);
        passageTemplate = passageTemplate.Replace("_INSERT_IMAGES_", imageText);
        passageTemplate += "\n";
        return passageTemplate;
    }
    public IEnumerator Export()
    {
        RTConsole.Log("Starting export...");

        string temp = "";
        temp = AdventureLogic.Get().GetExtractor().TwineStart;
        temp = temp.Replace("_INSERT_GUID_", System.Guid.NewGuid().ToString());
        temp = temp.Replace("_INSERT_TITLE_", "StoryTest");

        _twee += temp;

        //add data for each story node.  Declare a list of Gameobjects
        List<GameObject> objs = new List<GameObject>();

        RTUtil.AddObjectsToListByNameIncludingInactive(RTUtil.FindObjectOrCreate("Adventures"),
            "AdventureText", true, objs);

        RTConsole.Log("Found " + objs.Count + " objects");
        //add ending too
       
        string subdir = "/"+Config._saveDirName+"/" + "adventure_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff");

        //Save _twee to a file
        string path = Config.Get().GetBaseFileDir(subdir) + "/";
        //save _twee text file to the path and filename
        string fileName = path + AdventureLogic.Get().GetAdventureName() + ".twee";

        // Ensure the directory exists
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        _twee += "\n\n";

        //enumerate trhough adventure nodes
        foreach (GameObject obj in objs)
        {
            yield return null; //lesson the jerkiness

            AdventureText at = obj.GetComponent<AdventureText>();
            if (at != null)
            {

                if (at.GetName() != "S0" && at.GetName() != "?")
                {
                    //add the twee text for this node
                    _twee += GetTweeText(path, at);
                }
            }
        }

        temp = AdventureLogic.Get().GetExtractor().TwineEnd;
        _twee += "\n\n" + temp;

        // Save the _twee string to a text file
        try
        {
            File.WriteAllText(fileName, _twee);
            RTConsole.Log("Export successful! File saved at: " + fileName);
        }
        catch (Exception e)
        {
            RTConsole.LogError("Failed to save file: " + e.Message);
        }

    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
