using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static OpenAITextCompletionManager;

public class GPTPromptManager : MonoBehaviour
{

    Queue<GTPChatLine> _interactions = new Queue<GTPChatLine>();

    float _tokensPerWordMult = 1.25f;  
    int _maxTokensBeforeJournaling = 1024 * 6;
    int _maxWordsForJournal = 1000;
    int _interactionsToKeepWhenBuildingJournal = 8;

    string _baseSystemPrompt = "";
    string _journalSystemPrompt = "";

    string _nameToUseForSystem = "system";
    string _nameToUseForUser = "user";
  
    public void SetSystemName(string systemName)
    {
        _nameToUseForSystem = systemName;
    }

    public void SetUserName(string userName)
    {
        _nameToUseForUser = userName;
    }

    
    // Start is called before the first frame update
    void Start()
    {
            
    }

    public void Reset()
    {
        _baseSystemPrompt = "";
        _journalSystemPrompt = "";
        _interactions.Clear();
    }

    public void CloneFrom(GPTPromptManager other)
    {
        _baseSystemPrompt = other._baseSystemPrompt;
        _journalSystemPrompt = other._journalSystemPrompt;

        _interactions = new Queue<GTPChatLine>();
        _nameToUseForSystem = other._nameToUseForSystem;
        _nameToUseForUser = other._nameToUseForUser;
      
        foreach (var chatLine in other._interactions)
        {
            // Create a new GTPChatLine instance for each item
            var clonedChatLine = chatLine.Clone();

            _interactions.Enqueue(clonedChatLine);
        }
    }

    public void AddInteraction(string role, string content, string internalTag = "")
    {
        /*
         //actually, I don't think it's so important.  You can invent roles if you want.
        if (role != "assistant" && role != "system" && role != "user")
        {
            Debug.LogError("Invalid role: " + role);
            Debug.Assert(false);
            return;
        }
        */
        _interactions.Enqueue(new GTPChatLine(role, content, internalTag));

    }
    public void Awake()
    {
        
    }
    
    public void AddInteraction(GTPChatLine interaction)
    {
        if (_interactions == null)
        {
            _interactions = new Queue<GTPChatLine>();
        }

        // Add the copied interaction to the interactions queue
        _interactions.Enqueue(interaction);
    }

    public void RemoveInteractionsByInternalTag(string internalTag)
    {
        Queue<GTPChatLine> newInteractions = new Queue<GTPChatLine>();
        foreach (GTPChatLine interaction in _interactions)
        {
            if (interaction._internalTag != internalTag)
            {
                newInteractions.Enqueue(interaction);
            }
        }
        _interactions = newInteractions;
    }
    public void SetBaseSystemPrompt(string prompt)
    {
        _baseSystemPrompt = prompt;
    }

    public void SetJournalSystemPrompt(string prompt)
    {
        _journalSystemPrompt = prompt;
    }
    void Update()
    {
        
    }

    public bool IsTooBig()
    { 
        float size = (float)_baseSystemPrompt.Length * _tokensPerWordMult;
        
        size = (float)_journalSystemPrompt.Length * _tokensPerWordMult;

        foreach (GTPChatLine interaction in _interactions)
        {
            size += (float)interaction._content.Length * _tokensPerWordMult;
        }   

        if (size > _maxTokensBeforeJournaling)
        {
            return true; 
        }

        return false;
    }

    //a function that removes all but the last N lines from our interaction queue
    public void TrimInteractionsToLastNLines(int linesToKeepAtTheEnd)
    {
        while (_interactions.Count > linesToKeepAtTheEnd)
        {
            _interactions.Dequeue();
        }
    }
     
    public void SummarizeHistoryIntoJournal(string openAI_APIKey, Action<RTDB, JSONObject, string> myCallback)
    {

        Queue<GTPChatLine> lines = BuildPrompt(_interactionsToKeepWhenBuildingJournal);

        string basePrompt = $@"Summarize the entire conversation of you playing this game thus far into {_maxWordsForJournal} words or less.";

        //add a line with role system using the base prompt
        lines.Enqueue(new GTPChatLine(_nameToUseForUser, basePrompt));

        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

        string json = textCompletionScript.BuildChatCompleteJSON(lines, 1500, 0.2f, "gpt-4");
        RTDB db = new RTDB();

        TrimInteractionsToLastNLines(_interactionsToKeepWhenBuildingJournal);
        textCompletionScript.SpawnChatCompleteRequest(json, myCallback, db, openAI_APIKey);
    }
    //  Queue<GTPChatLine> _interactions = new Queue<GTPChatLine>();

    public GTPChatLine PopFirstInteraction()
    {
        //return the first interaction in the queue and remove it, if it exists
        if (_interactions.Count == 0)
        {
            return null;
        }
        GTPChatLine firstInteraction = _interactions.Dequeue();
        return firstInteraction;
    }

    public GTPChatLine GetLastInteraction()
    {
        if (_interactions.Count == 0)
        {
            return null;
        }
        
        //return the newest thing added
        return _interactions.LastOrDefault();
    }

    public void AppendToLastInteraction(string text)
    {
        if (_interactions.Count == 0)
        {
            return;
        }

        //return the newest thing added
        _interactions.LastOrDefault()._content += text;
    }


    public Queue<GTPChatLine> BuildPromptChat(int linesToIgnoreAtTheEnd = 0)
    {
        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();

        //add a line with role system using the base prompt
        if (_baseSystemPrompt.Length > 0)
            lines.Enqueue(new GTPChatLine(_nameToUseForSystem, _baseSystemPrompt));
        if (_journalSystemPrompt.Length > 0)
            lines.Enqueue(new GTPChatLine(_nameToUseForSystem, _journalSystemPrompt));

        //add the last few interactions, but ignore the last linesToIgnoreAtTheEnd lines
        int count = _interactions.Count - linesToIgnoreAtTheEnd;
        if (count < 0)
        {
            count = 0;
        }
        foreach (GTPChatLine interaction in _interactions)
        {
            if (count <= 0)
            {
                break;
            }
            count--;
            lines.Enqueue(new GTPChatLine(interaction._role, interaction._content));
        }

        return lines;
    }
        
    public GTPChatLine RemoveLastInteractionIfItExists()
    {
        if (_interactions.Count > 0)
        {
            var interactionsArray = _interactions.ToArray();
            var lastInteraction = interactionsArray[interactionsArray.Length - 1];
            _interactions = new Queue<GTPChatLine>(interactionsArray.Take(interactionsArray.Length - 1));
            return lastInteraction;
        }

        return null;
    }
    public Queue<GTPChatLine> BuildPrompt(int linesToIgnoreAtTheEnd = 0)
    {
        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();

        //add a line with role system using the base prompt
        lines.Enqueue(new GTPChatLine(_nameToUseForSystem, _baseSystemPrompt));
        lines.Enqueue(new GTPChatLine(_nameToUseForSystem, _journalSystemPrompt));

        //add the last few interactions, but ignore the last linesToIgnoreAtTheEnd lines
        int count = _interactions.Count - linesToIgnoreAtTheEnd;
        if (count < 0)
        {
            count = 0;
        }
        foreach (GTPChatLine interaction in _interactions)
        {
            if (count <= 0)
            {
                break;
            }
            count--;
            lines.Enqueue(new GTPChatLine(interaction._role, interaction._content));
        }

        return lines;
    }
}
