using UnityEngine;
using TMPro;
using SimpleJSON;
using static OpenAITextCompletionManager;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Text;
using System;
using System.Text.RegularExpressions;

public class AdventureText : MonoBehaviour
{
    public TMP_InputField inputField; // Reference to the TextMeshPro InputField
    RectTransform inputFieldRectTransform; // Reference to the RectTransform of the InputField
    public float padding = 10f; // Padding to add some space around the text
    public TexGenWebUITextCompletionManager _texGenWebUICompletionManager;
    public OpenAITextCompletionManager _openAITextCompletionManager;
    public AnthropicAITextCompletionManager _anthropicAITextCompletionManager;

    string _inputPromptOutput;
    StringBuilder accumulatedText = new StringBuilder();
    public GPTPromptManager m_promptManager;
    public Transform _panelTransform; //we need to know the scale there
    int _imagesRenderedOnThisLine;
    string _lastPicTextRenderedDetailed = "";
    string _lastPicTextRenderedSimple = "";
    string _lastAudioPrompt = "";
    string _lastAudioNegativePrompt = "";
    bool _bFoundAndProcessedPics;
    string m_configName = "";
    float _directionMult = 1.0f;
    bool _bAddedFinishedTextToPrompt = false;
    bool _llmIsActive;
    int _activeLLMInstanceID = -1; // Tracks which LLM instance is currently active
    Color _stopButtonOriginalBackgroundColor;
    public Button _stopButton;
    bool _bIsOnAuto;
    Color _panelBackgroundOriginalColor;
    bool m_bSetUserCreated;
    string m_textWithoutChoices = "";
    float _tryAgainWaitSeconds = 0;
    private float lastUpdateTime;
    private const float UPDATE_INTERVAL = 0.1f; // Update every 100ms

    // Create a list to store the choices
    List<(string identifier, string unused, string description)> _choices = new List<(string, string, string)>();
    int _choicesProcessedInCYOAMode = 0;
    string m_vanillaStatusMsg = "Ready";
    string m_name = "?";
    string _factoid = "";
    bool _bDontSendTextToLLM = false;
    string _textToAppendAfterResponse = "";

    public void SetTextToAppendAfterResponse(string text)
    {
        _textToAppendAfterResponse = text;
    }

    public void SetDontSendTextToLLM(bool bNew)
    {
        _bDontSendTextToLLM = bNew;
    }

    public bool GetDontSendTextToLLM()
    {
        return _bDontSendTextToLLM;
    }
    void SetTryAgainWait(float seconds)
    {
        _tryAgainWaitSeconds = Time.time + seconds;
    }

    int m_generationCount = 1; //how many parents we've had
    //list of PicMain objects we've spawned
    public List<PicMain> _picsSpawned = new List<PicMain>();
    public List<PicMain> GetPicsSpawned() { return _picsSpawned; }
    //reference to the GUI Panel
    public Image _panelImage;
  
    Color _autoButtonOriginalBackgroundColor;
    public Button _autoButton;
    public string GetFactoid() { return _factoid; }
    //handle to TMP button
    public TextMeshProUGUI _statusText;
    public GPTPromptManager GetPromptManager()
    {
        return m_promptManager;
    }
    
    public List<(string identifier, string action, string description)> GetChoices()
    {
        return _choices;
    }
    public string GetName() { return m_name; }

    public void SetName(string name)
    {
        m_name = name;
        SetStatus(m_vanillaStatusMsg);
    }

    public int GetGenerationCount() { return m_generationCount; }
    public void SetGenerationCount(int count) { m_generationCount = count; }
    
    public string GetTextWithoutChoices()
    {
        return m_textWithoutChoices;
    }
    public void SetUserCreated(bool bNew)
    {
        m_bSetUserCreated = bNew;

        //change our text window background to to light blue
        if (m_bSetUserCreated)
        {
            //Set bg color of inputField to light blue
            Color newColor = new Color(0.5f, 0.5f, 1.0f, 0.5f);
            inputField.image.color = newColor;
        }
        else
        {
            _panelImage.color = _panelBackgroundOriginalColor;
        }
    }

    public bool IsBusy()
    {
        return _llmIsActive || _texGenWebUICompletionManager.IsRequestActive() || _openAITextCompletionManager.IsRequestActive()
            || _anthropicAITextCompletionManager.IsRequestActive();
    }


    private void Awake()
    {
        // Ensure references are set
        if (inputField == null)
        {
            inputField = GetComponent<TMP_InputField>();
        }

        if (inputFieldRectTransform == null)
        {
            inputFieldRectTransform = inputField.GetComponent<RectTransform>();
        }
        // Ensure rich text is enabled for the display component
        if (inputField != null && inputField.textComponent != null)
        {
            inputField.textComponent.richText = true;
        }
        // Add listener to handle text changes
        inputField.onValueChanged.AddListener(ResizeInputField);

        _stopButtonOriginalBackgroundColor = _stopButton.GetComponent<Image>().color;
        _autoButtonOriginalBackgroundColor = _autoButton.GetComponent<Image>().color;
        _panelBackgroundOriginalColor = _panelImage.color;
        SetStatus("Ready");
    }

   
    public void UpdateLastInteraction()
    {
        if (GetDontSendTextToLLM()) return;

        //remove last interaction from gptprompt
        if (m_promptManager.GetLastInteraction() != null)
        {
            // Sanitize any TMP markup before storing back for LLM context
            m_promptManager.GetLastInteraction()._content = OpenAITextCompletionManager.RemoveTMPTagsFromString(inputField.text);
        }

        //re add our inputfield text
    }

    public void SetConfigFileName(string configName)
    {
        m_configName = configName;
        SetStatus(m_vanillaStatusMsg);
    }

    public string GetConfigFileName() { return m_configName; }
    public void OnRenderButton()
    {
        if (
            (_lastPicTextRenderedDetailed != null && _lastPicTextRenderedDetailed.Length > 0)
            ||
            (_lastPicTextRenderedSimple != null && _lastPicTextRenderedSimple.Length > 0)
            )

        {
            RenderAnotherPic(AdventureLogic.Get().GetRenderer());
            AdventureLogic.Get().SetLastPicTextAndOwner(this);
        } else
        {
            //show a message to the screen
            RTQuickMessageManager.Get().ShowMessage("No existing pic tags.  Choose +APic to generate one via LLM.");
        }
    }

    public void OnAutoRenderButton()
    {
        RenderPic(_lastPicTextRenderedDetailed, _lastPicTextRenderedSimple, AdventureLogic.Get().GetRenderer(),
            _lastAudioPrompt, _lastAudioNegativePrompt, true);
    }

    public void SetIsSelected()
    {
        AdventureLogic.Get().SetSelected(this);
        //make our panel bar background color yellow
        _panelImage.color = Color.yellow;
    }

    public void SetUnselected()
    {
        //change color back
        _panelImage.color = _panelBackgroundOriginalColor;

        AdventureLogic.Get().UnselectTextIfNeeded(this);
    }

    public Vector3 GetBottomWorldPosition()
    {
        // Ensure this gameobject is fully updated before measuring it

        Canvas.ForceUpdateCanvases();
        RectTransform rtOld = inputField.GetComponent<RectTransform>();

        Vector3 vTempPos = transform.position;

        //also add the panel's height
        //convert rtOld to world coords
        vTempPos.y -= (rtOld.rect.height + _panelTransform.GetComponent<RectTransform>().rect.height) * _panelTransform.localScale.y;

        return vTempPos;

    }

    public void SetText(string text)
    {
        //set the text
        inputField.text = ConvertMarkdownToTMP(text);
    }
    public void AddText(string newText)
    {
        inputField.text += newText;
        // Re-run formatting across full text to catch spans that cross append boundaries
        inputField.text = ConvertMarkdownToTMP(inputField.text);
        ResizeInputField(inputField.text);
    }

    public void OnKillButton()
    {
        //kill this gameobject
        OnStop();
        AdventureLogic.Get().OnTextDeleted(this);
        Destroy(gameObject);
    }
    public void OnStop()
    {
        if (_llmIsActive)
        {
            if (_texGenWebUICompletionManager.IsRequestActive())
            {
                //stop the LLM
                _texGenWebUICompletionManager.CancelCurrentRequest();
            }

            if (_openAITextCompletionManager.IsRequestActive())
            {
                _openAITextCompletionManager.CancelCurrentRequest();
            }

            if (_anthropicAITextCompletionManager.IsRequestActive())
            {
                _anthropicAITextCompletionManager.CancelCurrentRequest();
            }


            //make the button inactive again
            SetLLMActive(false);
        }


        SetAuto(false);

    }

    void ProcessChoices()
    {
        /*

        Choices in text look like this:

        CHOICE:RUN_AWAY:Run away, attempting to outdistance the monster
        CHOICE:FIGHT_MONSTER:Attack the monster with your silver dagger
        CHOICE:USE_INVISIBILITY_POTION:Quaff the tiny red potion the old man gave you

        Let's run through our text and find them and break them down into their text pieces
        */

        m_textWithoutChoices = inputField.text;

        //truncate the text before CHOICE: if it exists, or before DETAILED_SCENE_VISUAL_DESCRIPTION_START or SIMPLE_SCENE_VISUAL_DESCRIPTION_START is seen
        int i = m_textWithoutChoices.IndexOf("CHOICE:");
        if (i > 0)
        {
            m_textWithoutChoices = m_textWithoutChoices.Substring(0, i);
        }
        i = m_textWithoutChoices.IndexOf("DETAILED_SCENE_VISUAL_DESCRIPTION_START");
        if (i > 0)
        {
            m_textWithoutChoices = m_textWithoutChoices.Substring(0, i);
        }
        i = m_textWithoutChoices.IndexOf("SIMPLE_SCENE_VISUAL_DESCRIPTION_START");
        if (i > 0)
        {
            m_textWithoutChoices = m_textWithoutChoices.Substring(0, i);
        }

        //trim whitespace off
        m_textWithoutChoices = m_textWithoutChoices.Trim();


        // Split the input text by newlines
        string[] lines = inputField.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        _choices.Clear();

        // Iterate through each line to find and process choices
        foreach (string line in lines)
        {
            // Trim whitespace from line
            string trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("CHOICE:"))
            {
                // Remove the "CHOICE:" prefix and split the remaining text by ":"
                string[] parts = trimmedLine.Substring(7).Split(new[] { ':' }, 2);


                if (parts.Length == 2)
                {
                    string identifier = parts[0];
                    string description = parts[1];

                    //replace any _ with - in the identifier
                    identifier = identifier.Replace('_', '-');
                    identifier = identifier.Trim();

                    if (AdventureLogic.Get().GetForceUniquePassageNames())
                    {
                        identifier += "-" + GetGenerationCount().ToString();
                    }

                    description = description.Trim();

                    //verify both description and identifier aren't null or empty
                    if (description.Length == 0 || identifier.Length == 0)
                    {
                        RTConsole.LogError("Error: Choice has no description or identifier, skipping.  Text: " + line);
                        continue;
                    }

                    //TODO this could cause bugs, not smart.  But I'm in a hurry
                    inputField.text = inputField.text.Replace(parts[0], identifier);
                    inputField.text = inputField.text.Replace(parts[1], description);

                    // Add the choice to the list (identifier, action, description)
                    
                   
                        if (AdventureLogic.Get().GetMode() == AdventureMode.QUIZ)
                        {
                            if (identifier.ToUpper().Contains("-CORRECT"))
                            {
                            SetName(identifier + "-" + GetGenerationCount() + "-");
                            }
                        }
                   
                    _choices.Add((identifier, identifier, description));
                }
            }
            if (trimmedLine.StartsWith("FACTOID:"))
            {
                //set _factoid to all text after the FACTOID: part
                _factoid = trimmedLine.Substring(8);


            }
        }

        // Now 'choices' contains tuples of (identifier, action, description) for each choice
        // You can now do something with the choices list, such as displaying them or storing them for later use
    }

    Vector3 CalculatePositionOfNextTextBox(AdventureText newText)
    {

        if (AdventureLogic.Get().GetExtractor().SpatialOrganizationMethod == eSpatialOrganizationMethod.VERTICAL)
            return newText.transform.position; //no change

        Vector3 vFinalPositionOfThingWeSpawn;

        if (AdventureLogic.Get().GetExtractor().SpatialOrganizationMethod == eSpatialOrganizationMethod.TREE_BY_GENERATION)
        {

            Vector3 vTemp = newText.transform.position;

            Vector3 vNewGenPos = AdventureLogic.Get().GetNewPositionByGenerationOnRight(GetGenerationCount());
            vTemp.x = vNewGenPos.x;
            vTemp.y = vNewGenPos.y;


            return vTemp;
        }
        else
        {
            float baseOffset = 5.12f * GetGenerationCount() * 2; // Base offset value for positioning

            // set a var to up tp 2.0f plus or minus
            float randomOffsetY = UnityEngine.Random.Range(-1.0f, 1.0f);
            float randomOffsetX = UnityEngine.Random.Range(-2.0f, 2.0f);

            float extraHeightToDropBy = 5.12f + randomOffsetY;
            // Calculate the x position
            float xOffset = 0f; // Default is no offset for the first choice

            if (_choicesProcessedInCYOAMode > 0)
            {
                xOffset = (_choicesProcessedInCYOAMode % 2 == 0) ? (_choicesProcessedInCYOAMode / 2) * baseOffset : -(_choicesProcessedInCYOAMode / 2 + 1) * baseOffset;
                xOffset += randomOffsetX;
            }

            vFinalPositionOfThingWeSpawn = new Vector3(newText.transform.position.x + xOffset, newText.transform.position.y - extraHeightToDropBy, newText.transform.position.z);
        }

        return vFinalPositionOfThingWeSpawn;
    }
    public void SpawnGenerationsFromChoices()
    {

        while (_choicesProcessedInCYOAMode < _choices.Count )
        {
       
            var choice = _choices[_choicesProcessedInCYOAMode];
           // Debug.Log($"Choice: Identifier={choice.identifier}, Unused={choice.unused}, Description={choice.description}");

            // For each choice, we'll add a new text object and set it up to generate
            AdventureText newText = AdventureLogic.Get().AddTextAndGetReply(choice.identifier, this, true);
            newText.SetAuto(true);
            newText.SetName(choice.identifier);
            newText.SetIsSelected();

            // Set the new position
            newText.transform.position = CalculatePositionOfNextTextBox(newText);
            _choicesProcessedInCYOAMode++;

            return;
        }

        SetAuto(false); 

    }

    public bool GetIsUserCreated()
    {
        return m_bSetUserCreated;
    }
    public void OnAutoDoNextThing()
    {
        if (m_promptManager.GetLastInteraction() == null)
        {
           Debug.Log("No last interaction, can't continue");
            return;
        }

        AdventureText newText;

        if (AdventureLogic.Get().GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE
            && m_promptManager.GetLastInteraction()._role != "user")
        {
            //check for choices and record them
            SpawnGenerationsFromChoices();
        }
        else
        {
            if (m_bSetUserCreated)
            {
                newText = AdventureLogic.Get().AddTextAndGetReply("", this);
            }
            else
            {
                newText = AdventureLogic.Get().AddTextAndGetReply(AdventureLogic.Get().GetExtractor().AutoContinueText, this);
            }

            newText.SetAuto(true);
            newText.SetIsSelected();
            SetAuto(false); //we've moved it to the next thing, our time is over
        }
    }
    public void SetAuto(bool bAuto)
    {
        _bIsOnAuto = bAuto;

        
        // Change button color based on auto status
        Color buttonColor = bAuto ? Color.green : _autoButtonOriginalBackgroundColor;
        _autoButton.GetComponent<Image>().color = buttonColor;
    }

    public void OnAutoButton()
    {
        //Let's just keep rendering story automatically

        //They manually hit an auto button, reset the counter
        AdventureLogic.Get().ResetGenerationCounter();
        SetAuto(!_bIsOnAuto);
    }
   
    private void ResizeInputField(string text)
    {
        // Force update the layout to ensure size calculations are correct
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(inputFieldRectTransform);

        // Calculate the preferred height of the text
        TMP_Text textComponent = inputField.textComponent;
        Vector2 textSize = textComponent.GetPreferredValues(text);

        // Set the height of the RectTransform to fit the text
        inputFieldRectTransform.sizeDelta = new Vector2(inputFieldRectTransform.sizeDelta.x, textSize.y);

        // Force update the parent layout groups
        RectTransform parentRectTransform = inputFieldRectTransform.parent as RectTransform;
        if (parentRectTransform != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRectTransform);
        }
    }

    public (string visualsText, string simpleVisualsText, string audioPrompt, string audioNegativePrompt) GetPicFromText(ref string text, bool bFileIsComplete)
    {
        List<string> lines = new List<string>(text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
        StringBuilder visualComfyUIText = new StringBuilder();
        StringBuilder visualText = new StringBuilder();
        bool bFoundStartComfyUIVisuals = false;
        bool bFoundStartVisuals = false;
        const string audioPromptTag = "AUDIO_PROMPT:";
        const string audioPromptNegativeTag = "AUDIO_NEGATIVE_PROMPT:";

        string defaultAudioPrompt = Config.Get().GetDefaultAudioPrompt();
        string defaultAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();


        for (int i = 0; i < lines.Count; i++)
        {
            string trimmedLine = lines[i].Trim();

            if (!bFoundStartComfyUIVisuals && !bFoundStartVisuals)
            {
                // Check for the DETAILED_SCENE_VISUAL_DESCRIPTION_START tag
                if (trimmedLine.Equals("DETAILED_SCENE_VISUAL_DESCRIPTION_START", StringComparison.OrdinalIgnoreCase))
                {
                    bFoundStartComfyUIVisuals = true;
                    continue;
                }

                // Check for the START_SIMPLE_VISUALS tag
                if (trimmedLine.Equals("SIMPLE_SCENE_VISUAL_DESCRIPTION_START", StringComparison.OrdinalIgnoreCase))
                {
                    bFoundStartVisuals = true;
                    continue;
                }

                //check for AUDIO_PROMPT: tag, if exists, grab all text on that line after it and put it into a string
               
                if (trimmedLine.StartsWith(audioPromptTag))
                {
                    //remove the AUDIO_PROMPT: part
                    defaultAudioPrompt = trimmedLine.Substring(audioPromptTag.Length);
                    continue;
                }

                if (trimmedLine.StartsWith(audioPromptNegativeTag))
                {
                    //remove the AUDIO_PROMPT: part
                    defaultAudioNegativePrompt = trimmedLine.Substring(audioPromptNegativeTag.Length);
                    continue;
                }
            }
            else if (bFoundStartComfyUIVisuals)
            {
                // Check for the END_VISUALS tag
                if (trimmedLine.Equals("DETAILED_SCENE_VISUAL_DESCRIPTION_END", StringComparison.OrdinalIgnoreCase))
                {
                    _bFoundAndProcessedPics = true;
                    bFoundStartComfyUIVisuals = false; // Reset the flag after finding END_VISUALS
                    continue;
                }

                if (trimmedLine.StartsWith(audioPromptTag))
                {
                    //it's in the wrong place, but hey, AI can be dumb
                    //remove the AUDIO_PROMPT: part
                    _bFoundAndProcessedPics = true;
                    bFoundStartComfyUIVisuals = false; // Reset the flag after finding END_VISUALS
                    defaultAudioPrompt = trimmedLine.Substring(audioPromptTag.Length);
                    continue;
                }

                // Append the current line to the visualText
                visualComfyUIText.AppendLine(lines[i]);
            }
            else if (bFoundStartVisuals)
            {
                if (trimmedLine.Equals("SIMPLE_SCENE_VISUAL_DESCRIPTION_END", StringComparison.OrdinalIgnoreCase))
                {
                    bFoundStartVisuals = false; // Reset the flag after finding END_VISUALS
                    continue;
                }

                // Append the current line to the simpleVisualText
                visualText.AppendLine(lines[i]);
            }
        }

        // return what we found
        return (visualComfyUIText.ToString(), visualText.ToString(), defaultAudioPrompt, defaultAudioNegativePrompt);
    }

    void ProcessFinalText(string streamedText, bool bInitialScan)
    {
        //text was streamed, we handle it differently

        UpdateInputFieldText();
        bool bRenderAutoPics = false;

        // Log if thinking content is detected in the response
        bool hasThinkingContent = streamedText.Contains("<think>") && streamedText.Contains("</think>");
        if (hasThinkingContent)
        {
            RTConsole.Log("LLM response contains thinking tags (<think>...</think>)");
        }

        if (GenerateSettingsPanel.Get().m_stripThinkTagsToggle.isOn)
        {
            if (hasThinkingContent)
            {
                RTConsole.Log("Stripping thinking tags from display (toggle is ON - turn it OFF in Settings to see thinking)");
            }
            streamedText = OpenAITextCompletionManager.RemoveThinkTagsFromString(streamedText);
            //also remove line feeds before and after the text
            streamedText = streamedText.Trim();
            inputField.text = ConvertMarkdownToTMP(streamedText);
        }

        if (!_bFoundAndProcessedPics)
        {
            string temp = streamedText;
            //  the last text/image combo would have been ignored, so let's process it now
            var (picTextDetailed, picTextSimple, audioPrompt, audioNegativePrompt) = GetPicFromText(ref temp, true);
            _bFoundAndProcessedPics = true;


            if (picTextDetailed.Length > 0 || picTextSimple.Length > 0)
            {

                if (AdventureLogic.Get().GetRenderCount() > 0)
                {
                    //render for each of the desired forced pics
                    for (int i = 0; i < AdventureLogic.Get().GetRenderCount(); i++)
                    {
                        RenderPic(picTextDetailed, picTextSimple, AdventureLogic.Get().GetRenderer(),
                            audioPrompt, audioNegativePrompt, false);
                    }
                }

                AdventureLogic.Get().SetLastPicTextAndOwner(this);

                _lastPicTextRenderedDetailed = picTextDetailed;
                _lastPicTextRenderedSimple = picTextSimple;
                _lastAudioPrompt = audioPrompt;
                _lastAudioNegativePrompt = audioNegativePrompt;
            }
            else
            {
                bRenderAutoPics = true;
            }

            //let's add our original prompt to it, I assume that's what we want
            // inputField.text = streamedText;

            // Append any pending text (e.g., recent interactions for summarize feature)
            if (!string.IsNullOrEmpty(_textToAppendAfterResponse))
            {
                streamedText += _textToAppendAfterResponse;
                inputField.text += ConvertMarkdownToTMP(_textToAppendAfterResponse);
                _textToAppendAfterResponse = ""; // Clear after use
            }

            if (_bAddedFinishedTextToPrompt)
            {
                m_promptManager.RemoveLastInteractionIfItExists();
            }
            else
            {
                AdventureLogic.Get().GetGlobalPromptManager().AddInteraction(Config.Get().GetAIAssistantWord(), streamedText);

            }
            m_promptManager.AddInteraction(Config.Get().GetAIAssistantWord(), streamedText);
            ProcessChoices();

            _bAddedFinishedTextToPrompt = true;

            //no pics found.  Should we use the LLM to create a description to render?  We'll check to see how many images have been set to render
            if (bRenderAutoPics && AdventureLogic.Get().GetRenderCount() > 0)
            {
                if (AdventureLogic.Get().GetGenUniqueAPics())
                {
                    //spawn a separate APic for each render count, so each gets a unique LLM description
                    for (int i = 0; i < AdventureLogic.Get().GetRenderCount(); i++)
                    {
                        OnAutoRenderButton();
                    }
                }
                else
                {
                    //single APic, then duplicate the result for additional renders
                    OnAutoRenderButton();
                }
            }
        }
    }
    void OnTexGenCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        SetLLMActive(false);

        if (jsonNode == null && streamedText.Length == 0)
        {
            //must have been an error
            string error = db.GetStringWithDefault("msg", "Unknown");

        
            //check to see if "429" is inside the string error
            if (error.Contains("429"))
            {
                RTConsole.Log("LLM reports too many requests, waiting 5 seconds to try again: " + error);
                SetTryAgainWait(5);
            } else
            {
                RTConsole.Log("Error talking to the LLM: " + error);
                RTQuickMessageManager.Get().ShowMessage(error);
                GameLogic.Get().ShowConsole(true);
                SetAuto(false); //don't let it continue doing crap
            }
            return;
        }

        if (jsonNode != null)
        {
            RTConsole.LogError("Error, we only support streaming text now");
            return;

        }

        ProcessFinalText(streamedText, true);
    }

    public void RenderAnotherPic(RTRendererType renderer)
    {
        if (_lastPicTextRenderedDetailed != null && _lastPicTextRenderedDetailed.Length > 0
            ||
            (_lastPicTextRenderedSimple != null && _lastPicTextRenderedSimple.Length > 0)
            )
        {
            RenderPic(_lastPicTextRenderedDetailed, _lastPicTextRenderedSimple, renderer, _lastAudioPrompt, _lastAudioNegativePrompt, false);
        }
    }

    public float GetDirectionMult()
    {
        return _directionMult;
    }

    public float GetReverseDirectionMult()
    { 
        return -_directionMult;
    }

    public void SetDirectionMult(float mult)
    {
        _directionMult = mult;
    }
  
    public void OnTextWasEdited()
    {
        if (IsBusy()) return;

        RTConsole.Log("Text was edited");

        string tempText = inputField.text;

        //  the last text/image combo would have been ignored, so let's process it now
        (_lastPicTextRenderedDetailed, _lastPicTextRenderedSimple, _lastAudioPrompt, _lastAudioNegativePrompt) = GetPicFromText(ref tempText, true);
        ProcessFinalText(tempText, false);
    }

    public void AutoPicFinishedScriptCallback(GameObject picGO)
    {
        PicMain picMain = picGO.GetComponent<PicMain>();

        string reply = picMain.m_lastLLMReply;

        //let's render more images with this, based on how many GPUs are free
        if (reply == null || reply.Length == 0)
        {
            RTConsole.LogError("No reply from script, can't render images");
            return;
        }
        //we need to clear picMain.m_onFinishedRenderingCallback
        picMain.m_onFinishedRenderingCallback = null;
        picMain.m_onFinishedScriptCallback = null;

        //re-enable server job overrides now that autopic script is done
        picMain.m_allowServerJobOverrides = true;

        //have our LLM picwindow also render
        picMain.AddJobList(GameLogic.Get().GetPicJobListAsListOfStrings());
        
        //spawn additional images with the same description, unless we're doing unique APics (each APic handles itself)
        if (!AdventureLogic.Get().GetGenUniqueAPics())
        {
            //for each in AdventureLogic.Get().GetRenderCount() we'll do a renderpic call
            for (int i = 0; i < AdventureLogic.Get().GetRenderCount()-1; i++)
            {
                //we'll use the reply as the pic text
                RenderPic(reply, reply, AdventureLogic.Get().GetRenderer(), _lastAudioPrompt, _lastAudioNegativePrompt, false);
            }
        }

    }
    public void RenderPic(string picTextComfyUI, string picText, RTRendererType desiredRenderer, string audioPrompt, string audioNegativePrompt, bool bAutoPic)
    {
        //RTConsole.Log("SPAWNING PIC");
        GameObject pic = ImageGenerator.Get().CreateNewPic();
        PicMain picMain = pic.GetComponent<PicMain>();
        PicTextToImage scriptAI = pic.GetComponent<PicTextToImage>();
        PicUpscale processAI = pic.GetComponent<PicUpscale>();
        _imagesRenderedOnThisLine++;
       
        if (AdventureLogic.Get().GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE)
        {
            //if we're in choose your own adventure mode, we need to make sure the pic is on the right side
            pic.transform.position = new Vector3(transform.position.x, transform.position.y+ 5.12f, pic.transform.position.z);
            //move to the right if it's not the first pic
            if (_imagesRenderedOnThisLine > 1)
            {
                pic.transform.position = new Vector3(pic.transform.position.x + (1.0f * _picsSpawned.Count) , pic.transform.position.y, pic.transform.position.z);
            }
        }
        else
        {
            pic.transform.position = new Vector3(transform.position.x + ((5.12f * _imagesRenderedOnThisLine) * _directionMult), transform.position.y, pic.transform.position.z);
        }

            picMain.SetStatusMessage("Waiting for GPU...");
            PicJob jobDefaultInfoToStartWith = new PicJob();

            jobDefaultInfoToStartWith._requestedPrompt = picTextComfyUI;

            if (jobDefaultInfoToStartWith._requestedPrompt == "")
            {
                jobDefaultInfoToStartWith._requestedPrompt = picText;
            }

            jobDefaultInfoToStartWith._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
            jobDefaultInfoToStartWith._requestedAudioPrompt = audioPrompt;
            jobDefaultInfoToStartWith._requestedAudioNegativePrompt = audioNegativePrompt;
            jobDefaultInfoToStartWith.requestedRenderer = desiredRenderer;

            List<string> jobsTodo = GameLogic.Get().GetPicJobListAsListOfStrings();

        if (bAutoPic)
        {
            string fileToLoad = "test_autopic.txt";
            PresetFileConfigExtractor preset = new PresetFileConfigExtractor();
            picMain._promptManager.CloneFrom(m_promptManager);
            picMain.m_allowServerJobOverrides = false;

            //now, we're going to have it run our script, but we'd like a notification when the script finishes
            picMain.m_onFinishedScriptCallback += this.AutoPicFinishedScriptCallback;


            if (PresetManager.Get().DoesPresetExistByNameNotCaseSensitive(fileToLoad))
            {
                preset = PresetManager.Get().LoadPreset(fileToLoad, PresetManager.Get().GetActivePreset());
            }
            else
            {
                preset = PresetManager.Get().LoadPreset("autopic.txt", PresetManager.Get().GetActivePreset());
            }

            jobsTodo = GameLogic.Get().GetPicJobListAsListOfStrings(preset.JobList);
        }
        
        if (jobsTodo.Count == 0)
        {
              RTQuickMessageManager.Get().ShowMessage("Add jobs to do!");
              return;
        }
      

        picMain.AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, jobsTodo);
            //add its PicMain to our list
            _picsSpawned.Add(picMain);
       
    }

    public void OnStreamingTextCallback(string text)
    {
        accumulatedText.Append(text);

        if (Time.time - lastUpdateTime > UPDATE_INTERVAL)
        {
            UpdateInputFieldText();
        }
    }

    private void UpdateInputFieldText()
    {
        inputField.text += accumulatedText.ToString();
        // Re-apply formatting to the entire text to support matches spanning chunks
        inputField.text = ConvertMarkdownToTMP(inputField.text);
        accumulatedText.Clear();
        lastUpdateTime = Time.time;
    }

    public void SetStatus(string status)
    {
        m_vanillaStatusMsg = status;
        _statusText.text = m_name + ": " + m_vanillaStatusMsg; // + " - " + m_configName;
    }

    // Convert lightweight markdown emphasis markers to TMP rich text for display
    private string ConvertMarkdownToTMP(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

      // Replace bold with double asterisks or underscores first
        text = Regex.Replace(text, "\\*\\*(.+?)\\*\\*", "<b>$1</b>", RegexOptions.Singleline);
        //text = Regex.Replace(text, "__(.+?)__", "<b>$1</b>", RegexOptions.Singleline);

        // Then replace single asterisk or underscore wrapped segments, avoiding overlaps with doubles
        //text = Regex.Replace(text, "(?<!\\*)\\*(?!\\s)(.+?)(?<!\\s)\\*(?!\\*)", "<b>$1</b>", RegexOptions.Singleline);
        //text = Regex.Replace(text, "(?<!_)_(?!\\s)(.+?)(?<!\\s)_(?!_)", "<b>$1</b>", RegexOptions.Singleline);

     
        return text;
    }
    public void SetLLMActive(bool bActive)
    {
        if (bActive == _llmIsActive) return;

        if (bActive)
        {
            _stopButton.interactable = true;
            _stopButton.GetComponent<Image>().color = Color.red;
            SetStatus("Generating");
            AdventureLogic.Get().ModLLMRequestCount(1);
        }
        else
        {
            _stopButton.interactable = false;
            _stopButton.GetComponent<Image>().color = _stopButtonOriginalBackgroundColor;
            SetStatus("Ready");
            AdventureLogic.Get().ModLLMRequestCount(-1);
            
            // Release LLM instance when done
            ReleaseLLMInstance();
        }
        
        _llmIsActive = bActive;
    }
    
    /// <summary>
    /// Release the currently active LLM instance.
    /// </summary>
    private void ReleaseLLMInstance()
    {
        if (_activeLLMInstanceID >= 0)
        {
            var instanceMgr = LLMInstanceManager.Get();
            if (instanceMgr != null)
            {
                instanceMgr.SetLLMBusy(_activeLLMInstanceID, false);
            }
            _activeLLMInstanceID = -1;
        }
    }

    public void StartLLMRequest()
    {
        accumulatedText = new StringBuilder();
       
       // Debug.Log("Contacting TexGen WebUI asking for chat style response at " + Config.Get()._texgen_webui_address); ;

        Queue<GTPChatLine> lines = m_promptManager.BuildPromptChat(0);

        lines = GameLogic.Get().PrepareLLMLinesForSending(lines);

        RTDB db = new RTDB();

        // Try to use multi-instance system first (isSmallJob=false for Adventure big jobs)
        var instanceMgr = LLMInstanceManager.Get();
        int llmInstanceID = instanceMgr?.GetFreeLLM(isSmallJob: false) ?? -1;
        
        // If no free instance, try to get the least busy one that can accept big jobs
        if (llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
        {
            llmInstanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: false);
            RTConsole.Log($"Adventure: No free big job LLM, using least busy: {llmInstanceID}");
        }
        
        LLMInstanceInfo llmInstance = llmInstanceID >= 0 ? instanceMgr?.GetInstance(llmInstanceID) : null;
        
        LLMProvider provider;
        LLMProviderSettings activeSettings = null;
        
        if (llmInstance != null)
        {
            provider = llmInstance.providerType;
            activeSettings = llmInstance.settings;
            _activeLLMInstanceID = llmInstanceID;
            instanceMgr.SetLLMBusy(llmInstanceID, true);
            RTConsole.Log($"Adventure using LLM instance {llmInstanceID}: {llmInstance.name}");
        }
        else
        {
            // Fall back to legacy system only if no instances configured
            provider = AdventureLogic.Get().GetLLMProvider();
            _activeLLMInstanceID = -1;
            RTConsole.Log("Adventure: No LLM instances configured, using legacy provider");
        }

        switch (provider)
        {
            case LLMProvider.OpenAI:
                {
                    string model = activeSettings?.selectedModel ?? Config.Get().GetOpenAI_APIModel();
                    string apiKey = activeSettings?.apiKey ?? Config.Get().GetOpenAI_APIKey();
                    if (string.IsNullOrEmpty(apiKey)) apiKey = Config.Get().GetOpenAI_APIKey();
                    if (string.IsNullOrEmpty(model)) model = Config.Get().GetOpenAI_APIModel();

                    // All GPT-5 models use Responses API - just with different parameters
                    bool useResponsesAPI = false;
                    bool isReasoningModel = false;
                    bool includeTemperature = true;
                    string reasoningEffort = null;
                    string endpoint;

                    if (model.Contains("gpt-5"))
                    {
                        // All GPT-5 models use Responses API
                        useResponsesAPI = true;
                        endpoint = "https://api.openai.com/v1/responses";
                        
                        if (model.Contains("gpt-5.2-pro"))
                        {
                            // Pro reasoning model: high reasoning effort, no temperature
                            isReasoningModel = true;
                            includeTemperature = false;
                            reasoningEffort = "high";
                        }
                        else if (model.Contains("gpt-5.2"))
                        {
                            // Base 5.2 reasoning model: medium reasoning effort, no temperature
                            isReasoningModel = true;
                            includeTemperature = false;
                            reasoningEffort = "medium";
                        }
                        else if (model.Contains("gpt-5-mini") || model.Contains("gpt-5-nano"))
                        {
                            // Mini/nano: Use Chat Completions API (they may not fully support Responses API)
                            useResponsesAPI = false;
                            isReasoningModel = false;
                            includeTemperature = false;  // Fixed temp=1, don't send parameter
                            reasoningEffort = null;
                            endpoint = "https://api.openai.com/v1/chat/completions";
                        }
                        else
                        {
                            // Base gpt-5: no reasoning, allow temperature
                            isReasoningModel = false;
                            includeTemperature = true;
                        }
                    }
                    else
                    {
                        // Non-GPT-5 models: use Chat Completions API
                        useResponsesAPI = false;
                        isReasoningModel = false;
                        includeTemperature = true;
                        endpoint = "https://api.openai.com/v1/chat/completions";
                    }

                    string json = _openAITextCompletionManager.BuildChatCompleteJSON(
                        lines,
                        4096,
                        AdventureLogic.Get().GetExtractor().Temperature,
                        model,
                        true,
                        useResponsesAPI,
                        isReasoningModel,
                        includeTemperature,
                        reasoningEffort);
                    _openAITextCompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                    SetLLMActive(true);
                }
                break;

            case LLMProvider.Anthropic:
                {
                    string model = activeSettings?.selectedModel ?? Config.Get().GetAnthropicAI_APIModel();
                    string apiKey = activeSettings?.apiKey ?? Config.Get().GetAnthropicAI_APIKey();
                    string endpoint = activeSettings?.endpoint ?? Config.Get().GetAnthropicAI_APIEndpoint();
                    if (string.IsNullOrEmpty(apiKey)) apiKey = Config.Get().GetAnthropicAI_APIKey();
                    if (string.IsNullOrEmpty(model)) model = Config.Get().GetAnthropicAI_APIModel();
                    if (string.IsNullOrEmpty(endpoint)) endpoint = Config.Get().GetAnthropicAI_APIEndpoint();
                    
                    string json = _anthropicAITextCompletionManager.BuildChatCompleteJSON(lines, 4096, AdventureLogic.Get().GetExtractor().Temperature, model, true);
                    _anthropicAITextCompletionManager.SpawnChatCompletionRequest(json, OnTexGenCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                    SetLLMActive(true);
                }
                break;

            case LLMProvider.LlamaCpp:
                {
                    var mgr = LLMSettingsManager.Get();
                    var settings = activeSettings ?? mgr?.GetProviderSettings(LLMProvider.LlamaCpp);
                    
                    string suggestedEndpoint;
                    // Use instance LLM params if we have an active instance
                    var llmParms = _activeLLMInstanceID >= 0 ? mgr?.GetInstanceLLMParms(_activeLLMInstanceID) : mgr?.GetLLMParms(LLMProvider.LlamaCpp);
                    string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, out suggestedEndpoint, 4096, AdventureLogic.Get().GetExtractor().Temperature, Config.Get().GetGenericLLMMode(), true, llmParms ?? new List<LLMParm>(), false, true);
                    
                    _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, settings?.endpoint ?? "", suggestedEndpoint, OnStreamingTextCallback, true, settings?.apiKey ?? "");
                    SetLLMActive(true);
                }
                break;

            case LLMProvider.Ollama:
                {
                    var mgr = LLMSettingsManager.Get();
                    var settings = activeSettings ?? mgr?.GetProviderSettings(LLMProvider.Ollama);
                    
                    string suggestedEndpoint;
                    // Use instance LLM params if we have an active instance
                    var llmParms = _activeLLMInstanceID >= 0 ? mgr?.GetInstanceLLMParms(_activeLLMInstanceID) : mgr?.GetLLMParms(LLMProvider.Ollama);
                    string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, out suggestedEndpoint, 4096, AdventureLogic.Get().GetExtractor().Temperature, Config.Get().GetGenericLLMMode(), true, llmParms ?? new List<LLMParm>(), true, false);
                    
                    // Use suggestedEndpoint which is /api/chat for Ollama (supports options.num_ctx)
                    _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, settings?.endpoint ?? "", suggestedEndpoint, OnStreamingTextCallback, true, settings?.apiKey ?? "");
                    SetLLMActive(true);
                }
                break;
        }
    }

    public void OnRegen()
    {

        OnStop();


        if (GetIsUserCreated())
        {
            //this isn't a normal prompt, this is a user prompt.  Let's assume they hand-edited it and we should just spawn the next text window instead of this
            //Show an error message to the screen
            RTQuickMessageManager.Get().ShowMessage("This is a user-created prompt, you can't regenerate it. Delete it, activate the earlier story prompt, and enter new text at the bottom.");
            return;

        }
        if (_bAddedFinishedTextToPrompt)
        {
            _bAddedFinishedTextToPrompt = false;
            m_promptManager.RemoveLastInteractionIfItExists();
        }
        accumulatedText = new StringBuilder();
        //clear everything and re-generate
        _picsSpawned = new List<PicMain>();
        //remove all things from _choices array
        _choices.Clear();
        _choicesProcessedInCYOAMode = 0;
        _tryAgainWaitSeconds = 0;
        inputField.text = "";
        _bFoundAndProcessedPics = false;
        
        StartLLMRequest();

    }

    //handle when they are destroyed
    private void OnDestroy()
    {
        //remove from the list
        OnStop();
    }

    public void Update()
    {
        if (accumulatedText.Length > 0 && Time.time - lastUpdateTime > UPDATE_INTERVAL)
        {
            UpdateInputFieldText();
        }

        if (_tryAgainWaitSeconds != 0 && !IsBusy() && AdventureLogic.Get().CanInitNewLLMRequest())
        {
            if (_tryAgainWaitSeconds < Time.time)
            {
                _tryAgainWaitSeconds = 0;
                StartLLMRequest();
                return;
            }
        }
        if (_bIsOnAuto && _tryAgainWaitSeconds == 0)
        {
            //if not busy, let's continue the story with the llm right now
            if (!IsBusy() && AdventureLogic.Get().CanInitNewLLMRequest())
            {
                OnAutoDoNextThing();
            }
        }
    }

}
