using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

/*

    RTConsole by Seth A. Robinson

    To use this, drag the RTConsole prefab so its a child of a Canvas. 

    You should see a console that allows input when you play.

    To see Unity debug messages, run this from a script on startup somewhere:
    
    RTConsole.Get().SetShowUnityDebugLogInConsole(true);

    //To add your own debug messages, do this:

    RTConsole.Log("Hello! `4This is the color red``. Cool, right?!");

 */

public class RTConsole : MonoBehaviour
{
    public TextMeshProUGUI _consoleText;
    public event Action<string> OnGotConsoleInputEvent;

    static RTConsole _this;
    ScrollRect _scrollRect;
    public InputField _inputField;

    bool _isDisplayingUnityDebugLog = false;
    bool _isSendingToUnityDebugLog = false;
    bool _isHeadlessMode = false;
    Queue<String> _lines;
    int _maxConsoleLines = 500;
    string _logPrependString = "RTLOG"; //only applies to the Unity internal debug logs, this helps me filter for it when watching Android stuff with logcat

    bool _requiresRefresh = false;

    // Use this for initialization
    public static RTConsole Get()
    {
        
        if (!_this)
        {
            GameObject us = RTUtil.FindIncludingInactive("RTConsole");
            if (us)
            {
                if (us.activeSelf == false)
                {
                    us.SetActive(true);
                }
            }
            else
            {
                //Actually, let's just create it
                //_this = new GameObject("RTConsole").AddComponent<RTConsole>();
                return null;
            }
        }
        return _this;
    }
    public void SetLogPrependString(string s)
    {
        _logPrependString = s;
    }
    private void Awake()
    {

        _lines = new Queue<String>();

        _this = GetComponent<RTConsole>();

        if (!_this)
        {
            print("Error findingRTConsole");
            return;
        }

        _scrollRect = GetComponent<ScrollRect>();
    }

    void Start ()
    {
        if (_consoleText != null)
          _consoleText.text = "";
       
    }

    public void CopyToClipboard()
    {
        //put all the lines of text into a string, then copy that to the system clipboard
        string s = "";
        foreach (string sLine in _lines)
        {
            s += sLine + "\n";
        }
        GUIUtility.systemCopyBuffer = s;
    }

    public void SetMaxLines(int count)
    {
        if (_maxConsoleLines == count) return;

        _maxConsoleLines = count;
        _requiresRefresh = true;
    }

    public void SetHeadlessMode(bool bNew)
    {
        _isHeadlessMode = bNew;
    }
    
    void HandleLog(string logString, string stackTrace, LogType type)
    {
        string color = "";
        string colorEnd = "";

        switch (type)
        {
            case LogType.Error:  color = "`4"; colorEnd = "``"; break;
            case LogType.Warning: color = "`$"; colorEnd = "``"; break;
            case LogType.Exception: color = "`#"; colorEnd = "``"; logString += stackTrace;  break;
            case LogType.Assert: color = "`@"; logString += stackTrace;  colorEnd = "``"; break;
        }

        RTConsole.Log(color+logString+colorEnd);
    }

     public void SetShowUnityDebugLogInConsole(bool bNew)
    {
        if (bNew == _isDisplayingUnityDebugLog) return;

        _isDisplayingUnityDebugLog = bNew;

        if (bNew)
        {
            Application.logMessageReceived += HandleLog;
        }
        else
        {
            Application.logMessageReceived -= HandleLog;
        }
    }

    public void SetMirrorToDebugLog(bool bNew)
    {
        _isSendingToUnityDebugLog = bNew;
    }

    public static void Log(string text)
    {
        if (Get() == null) return;

        //I tend to send huge texts with \r\n for the lines, so I'm going to be slow and split them
        string[] strings = text.Split('\n');
        foreach (string s in strings)
        {
            _this.Add(s + "\n");
        }
    }

    //just adds red. 
    public static void LogError(string text)
    {
        if (Get() == null) return;

        //I tend to send huge texts with \r\n for the lines, so I'm going to be slow and split them
        string[] strings = text.Split('\n');
        foreach (string s in strings)
        {
            _this.Add("`4"+s + "``\n");
        }
    }

    public static void LogRaw(string text)
    {
        _this.Add(text);
    }
    public string GetCurrentText() { return _inputField.text; }

    public void SetFocusOnInput(string text)
    {
        _inputField.text = text;
        _inputField.ActivateInputField(); //returns focus to field after pressing enter.  Possibly not wanted/needed on mobiles
        StartCoroutine(MoveTextEnd_NextFrame());
        //trick to de-highlight text:  https://answers.unity.com/questions/1103287/how-to-deselect-text-in-an-inputfield.html
    }

    IEnumerator MoveTextEnd_NextFrame()
    {
        yield return 0; // Skip the first frame in which this is called.
         _inputField.MoveTextEnd(false); // Do this during the next frame.
    }

    public void OnEndEdit(string text)
    {
        //if (!Input.GetKeyDown(KeyCode.Return)) return; //probably not wanted/needed on touch screens...
        if (!UnityEngine.InputSystem.Keyboard.current.enterKey.isPressed) return; //new input system
        
        SetFocusOnInput("");
        if (text.Length == 0) return;
       // print(text);
        _inputField.text = "";
        if (OnGotConsoleInputEvent == null)
        {
            print("RTConsole::OnEndEdit:  User typed something in, but you didn't add a handler to OnGotConsoleInputEvent");
        } else
        {
            OnGotConsoleInputEvent(text);
        }
    }

    void Add(string text)
    {
        if (_isSendingToUnityDebugLog)
        {
            if (_isDisplayingUnityDebugLog)
            {
                SetShowUnityDebugLogInConsole(false);
                Debug.unityLogger.Log(_logPrependString, text);
                SetShowUnityDebugLogInConsole(true);
            }
            else
            {
                Debug.Log(text);
            }
        }

        if (!_isHeadlessMode && _consoleText)
        {
            _lines.Enqueue(RTUtil.ConvertSansiToUnityColors(text));
            _requiresRefresh = true;
            // Schedule an immediate scroll update

            //only start if active
            if (gameObject.activeInHierarchy)
                StartCoroutine(ScrollToBottomNextFrame());
        }
    }

    void TrimAndUpdateWidget()
    {
        _requiresRefresh = false;

        while (_lines.Count > _maxConsoleLines)
        {
            _lines.Dequeue();
        }

        _consoleText.text = string.Concat(_lines.ToArray());

        // Force the scroll rect to update immediately
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_consoleText.rectTransform);
        StartCoroutine(ScrollToBottomNextFrame());
    }

    private IEnumerator ScrollToBottomNextFrame()
    {
        // Wait for end of frame to ensure layout is updated
        yield return new WaitForEndOfFrame();

        // Force canvas update again
        Canvas.ForceUpdateCanvases();

        // Set scroll position to bottom
        _scrollRect.verticalNormalizedPosition = 0f;

        // Wait one more frame to ensure the scroll position is applied
        yield return null;
        _scrollRect.verticalNormalizedPosition = 0f;
    }

    void Update()
    {
        if (_requiresRefresh)
        {
            TrimAndUpdateWidget();
        }
    }
}