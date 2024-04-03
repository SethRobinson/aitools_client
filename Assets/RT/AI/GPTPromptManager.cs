using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static OpenAITextCompletionManager;

public class GPTPromptManager : MonoBehaviour
{

    class GPTInteractions
    {
        //build constructer that takes both parms
        public GPTInteractions(string role, string content)
        {
            _role = role;
            _content = content;
        }
        public string _role;
        public string _content;
    }

    Queue<GPTInteractions> _interactions = new Queue<GPTInteractions>();

    float _tokensPerWordMult = 1.25f;  
    int _maxTokensBeforeJournaling = 1024 * 6;
    int _maxWordsForJournal = 1000;
    int _interactionsToKeepWhenBuildingJournal = 8;

    string _baseSystemPrompt = "";
    string _journalSystemPrompt = "";
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

    public void AddInteraction(string role, string content)
    {
        /*
         //actually, I don't think is so important.  You can invent roles if you want.
        if (role != "assistant" && role != "system" && role != "user")
        {
            Debug.LogError("Invalid role: " + role);
            Debug.Assert(false);
            return;
        }
        */
        _interactions.Enqueue(new GPTInteractions(role, content));
        
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

        foreach (GPTInteractions interaction in _interactions)
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
        lines.Enqueue(new GTPChatLine("user", basePrompt));


        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

        string json = textCompletionScript.BuildChatCompleteJSON(lines, 1500, 0.2f, "gpt-4");
        RTDB db = new RTDB();


        TrimInteractionsToLastNLines(_interactionsToKeepWhenBuildingJournal);
        textCompletionScript.SpawnChatCompleteRequest(json, myCallback, db, openAI_APIKey);

    }

    public Queue<GTPChatLine> BuildPrompt(int linesToIgnoreAtTheEnd = 0)
    {
        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();

        //add a line with role system using the base prompt
        lines.Enqueue(new GTPChatLine("system", _baseSystemPrompt));
        lines.Enqueue(new GTPChatLine("system", _journalSystemPrompt));

        //add the last few interactions, but ignore the last linesToIgnoreAtTheEnd lines
        int count = _interactions.Count - linesToIgnoreAtTheEnd;
        if (count < 0)
        {
            count = 0;
        }
        foreach (GPTInteractions interaction in _interactions)
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
