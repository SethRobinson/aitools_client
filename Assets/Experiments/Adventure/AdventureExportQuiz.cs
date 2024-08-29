using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;

public class AdventureExportQuiz : MonoBehaviour
{
  
    // Start is called before the first frame update
    void Start()
    {
        
    }


    public string GetQuestionData(string path, AdventureText at)
    {
        string passageTemplate = AdventureLogic.Get().GetExtractor().TwinePassage;

        /*
        The questions it will add to the _INSERT_CHOICES_ will look like this:

{
  question: "What is the capital of France?",
  options: ["London", "Berlin", "Paris", "Madrid"],
  correctAnswer: "Paris",
  image: "/<image name>.png",
  explanation: "Paris is indeed the capital of France. Known as the 'City of Light', it has been the country's capital since 987 CE and is famous for its iconic Eiffel Tower and world-class cuisine."
},

        */
      
        var choices = at.GetChoices();
        var picsSpawned = at.GetPicsSpawned();
        int picCount = 0;
        string fileName = "";
       
        foreach (PicMain pic in picsSpawned)
        {
            if (pic != null)
            {
               fileName = at.GetName();

                //filter to be a valid filename
                fileName = RTUtil.FilteredFilenameSafeToUseAsFileName(fileName + "-" + picCount.ToString());
                picCount++;
                pic.AddTextLabelToImage(AdventureLogic.Get().GetExtractor().ImageTextOverlay);
                pic.SaveFile(path + fileName + ".png", "", null, "", true, false);
                break; //we don't support more than 1 image right now
                
            }
        }

        //iterrate through choices and add them
        string choiceText = "";

        if (choices.Count > 0)
        {
            choiceText += "\n{\n";
            //TODO: This won't work if a " is in the question, right?
            
            string question = at.GetTextWithoutChoices();
            //escape the quotes so the HTML will work
            question = question.Replace("\"", "\\\"");

            //also escape carriage return/new lines so they can be embedded in a javascript string
            question = question.Replace("\n", "\\n");


            choiceText += "question: \"" + question + "\",\n";

            choiceText += "options: [";
            string correctAnswer = "Unknown";

            int counter = 0;
            foreach ((string identifier, string action, string description) choice in choices)
            {
                if (counter > 0)
                {
                    choiceText += ",";
                }
                choiceText += "\"" + choice.description.Replace("\"", "\\\"") + "\"";
                counter++;

                //if "-CORRECT" text is found inside of choice.identifier (case insensitive), then we'll copy description to correct the correct answer
                if (choice.identifier.ToUpper().Contains("-CORRECT"))
                {
                    correctAnswer = choice.description.Replace("\"", "\\\"");
                }


            }
            choiceText += "],\n";


            choiceText += "correctAnswer: \"" + correctAnswer + "\",\n";
            choiceText += "image: \"" + fileName + ".png\",\n";
            choiceText += "explanation: \""+at.GetFactoid().Replace("\"", "\\\"") + "\"\n";
            choiceText += "},\n";

            return choiceText;
        } else
        {
            return ""; //no good data
        }

    }

    public IEnumerator Export()
    {
        RTConsole.Log("Starting export...");
        
        string temp = "";
        temp = AdventureLogic.Get().GetExtractor().QuizHTML;
        temp = temp.Replace("_INSERT_TITLE_", "Quiz");

        //add data for each story node.  Declare a list of Gameobjects
        List<GameObject> objs = new List<GameObject>();

        RTUtil.AddObjectsToListByNameIncludingInactive(RTUtil.FindObjectOrCreate("Adventures"),
            "AdventureText", true, objs);

        RTConsole.Log("Found " + objs.Count + " objects");
        //add ending too
       
        string subdir = "/"+Config._saveDirName+"/" + "quiz_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff");

        string path = Config.Get().GetBaseFileDir(subdir) + "/";

        string fileName = path +  "index.html";


        // Ensure the directory exists
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }


        //copy over some extra files our html needs
        //TODO:  Just put these in a function already
        string sourceFile = Config.Get().GetBaseFileDir("/web/correct-sound.mp3");
        string destFile = path + "/correct-sound.mp3";
        File.Copy(sourceFile, destFile, true);

        sourceFile = Config.Get().GetBaseFileDir("/web/incorrect-sound.mp3");
        destFile = path + "/incorrect-sound.mp3";
        File.Copy(sourceFile, destFile, true);

        sourceFile = Config.Get().GetBaseFileDir("/web/question.png");
        destFile = path + "/question.png";
        File.Copy(sourceFile, destFile, true);


        string questionData = "";

        //enumerate trhough adventure nodes
        foreach (GameObject obj in objs)
        {
            yield return null; //lesson the jerkiness

            AdventureText at = obj.GetComponent<AdventureText>();
            if (at != null)
            {

                if (at.GetName() != "S0")
                {
                    //add the question data for this node

                    questionData += GetQuestionData(path, at);
                }
            }
        }

        temp = temp.Replace("_INSERT_CHOICES_", questionData);

        //write to our final file

        try
        {
            File.WriteAllText(fileName, temp);
            RTConsole.Log("Export successful! File saved at: " + fileName);

            //Open in a web browser
            Application.OpenURL(fileName);
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
