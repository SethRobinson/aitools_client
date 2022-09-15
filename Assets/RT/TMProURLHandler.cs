#if !RT_NO_TEXMESH_PRO
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class TMProURLHandler : MonoBehaviour, IPointerDownHandler
{

    TextMeshProUGUI _TMProText;
  
    bool _requiresRefresh = false;
    private bool hasTextChanged = false;
    bool _bFastForward = false;

    Camera _camera;
    Canvas _canvas;
   
    private void Awake()
    {
        _TMProText = GetComponent<TextMeshProUGUI>();

    }
   
    // Use this for initialization
    void Start()
    {
        _canvas = _TMProText.gameObject.GetComponentInParent<Canvas>();
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _camera = null;
        }
        else
        {
            _camera = _canvas.worldCamera;
        }

        TrimAndUpdateWidget();
        //StartCoroutine(RevealCharacters(_TMProText));
     }

    public void OnPointerUp(PointerEventData eventData)
    {
       _bFastForward = false;
    }


    public void OnPointerDown(PointerEventData eventData)
    {
       
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(_TMProText, Input.mousePosition, _camera);
        if (linkIndex != -1)
        {
            TMP_LinkInfo linkInfo = _TMProText.textInfo.linkInfo[linkIndex];
            if (linkInfo.GetLinkID().StartsWith("http://") || linkInfo.GetLinkID().StartsWith("https://"))
            {
                //print("OnPointerClick...");

                //guess it's a URL to open
                RTUtil.PopupUnblockOpenURL(linkInfo.GetLinkID());
                return;
            }
            else
            {
                RTConsole.Log("Unknown link type: " + linkInfo.GetLinkID());
            }

        }

        _bFastForward = true;
    }

    /// <summary>
    /// Method revealing the text one character at a time.
    /// </summary>
    /// <returns></returns>
    IEnumerator RevealCharacters(TMP_Text textComponent)
    {
        textComponent.ForceMeshUpdate();

        TMP_TextInfo textInfo = textComponent.textInfo;

        int totalVisibleCharacters = textInfo.characterCount; // Get # of Visible Character in text object
        int visibleCount = textInfo.characterCount;

        while (true)
        {
            if (hasTextChanged)
            {
                totalVisibleCharacters = textInfo.characterCount; // Update visible character count.
                hasTextChanged = false;
            }

            float delay = 0.01f;
            //don't restart
            if (visibleCount >= totalVisibleCharacters)
            {
                visibleCount = totalVisibleCharacters;
                //we're totally caught up, perform trimming
                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?
            }
            else
            {

                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                for (int i = 0; i < textInfo.linkCount; i++)
                {
                    TMP_LinkInfo linkInfo = textInfo.linkInfo[i];

                    //print("Cur: "+visibleCount+" Found link " + linkInfo.GetLinkID() + " and its text: " + linkInfo.GetLinkText() + " and it starts on " + linkInfo.linkTextfirstCharacterIndex+" its leng:"+ linkInfo.linkTextLength);

                    if (visibleCount >= linkInfo.linkTextfirstCharacterIndex && visibleCount < (linkInfo.linkTextfirstCharacterIndex + linkInfo.linkTextLength))
                    {
                        const string delayText = "delay ";

                        if (linkInfo.GetLinkID().StartsWith(delayText))
                        {

                            delay = float.Parse(linkInfo.GetLinkID().Substring(delayText.Length));
                        }

                        const string playText = "play ";

                        if (linkInfo.GetLinkID().StartsWith(playText))
                        {
                            //RTConsole.Log("Playing " + linkInfo.GetLinkID().Substring(playText.Length));
                            RTAudioManager.Get().PlayEx(linkInfo.GetLinkID().Substring(playText.Length), 1, 1, true);

                        }

                    }
                }

                visibleCount += 1;

                //sound of letters advancing
                //RTAudioManager.Get().PlayEx("click_3", 0.5f, 1.0f, false, 0.1f);
            }


            if (_bFastForward)
            {
                delay /= 3.0f;
            }
            yield return new WaitForSeconds(delay);
        }
    }

    /// <summary>
    /// Method revealing the text one word at a time.
    /// </summary>
    /// <returns></returns>
    IEnumerator RevealWords(TMP_Text textComponent)
    {
        textComponent.ForceMeshUpdate();

        int totalWordCount = textComponent.textInfo.wordCount;
        int totalVisibleCharacters = textComponent.textInfo.characterCount; // Get # of Visible Character in text object
        int counter = 0;
        int currentWord = 0;
        int visibleCount = 0;

        while (true)
        {
            currentWord = counter % (totalWordCount + 1);

            // Get last character index for the current word.
            if (currentWord == 0) // Display no words.
                visibleCount = 0;
            else if (currentWord < totalWordCount) // Display all other words with the exception of the last one.
                visibleCount = textComponent.textInfo.wordInfo[currentWord - 1].lastCharacterIndex + 1;
            else if (currentWord == totalWordCount) // Display last word and all remaining characters.
                visibleCount = totalVisibleCharacters;

            textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

            // Once the last character has been revealed, wait 1.0 second and start over.
            if (visibleCount >= totalVisibleCharacters)
            {
                yield return new WaitForSeconds(0.5f);
            }

            counter += 1;

            yield return new WaitForSeconds(1f);
        }
    }

    void TrimAndUpdateWidget()
    {
        _requiresRefresh = false;

       
//         //if we have too many lines, kill the oldest one.  Unity's Text widget sucks btw, it can't show that many lines
//         while (_lines.Count > _maxConsoleLines)
//         {
//             _lines.Dequeue();
//         }
// 
//         //copy them to the text object
// 
//         _consoleText.text = string.Concat(_lines.ToArray());
//        

        // _consoleText.text = _consoleText.text + RTUtil.ConvertSansiToUnityColors(text);
        Canvas.ForceUpdateCanvases();
       // _scrollRect.verticalNormalizedPosition = 0f;
    }

    // Update is called once per frame
    void Update()
    {
        if (_requiresRefresh)
        {
            TrimAndUpdateWidget();
        }
    }


    public void Clear()
    {
        _requiresRefresh = true;
        hasTextChanged = true;
    }

}
#endif