using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/*
 Example of use:

Somewhere, do this:
 public GameObject m_notepadTemplatePrefab;  (attach to RTNotepad prefab)


   Then do this:

    public void OnConfigButton()
    {
        RTNotepad notepadScript = RTNotepad.OpenFile("poop and crap\nyeah\\cool", m_notepadTemplatePrefab);
        notepadScript.m_onClickedSavedCallback += OnConfigSaved;
        notepadScript.m_onClickedCancelCallback += OnConfigCanceled;
    }

    void OnConfigSaved(string text)
    {
        Debug.Log("They clicked save.  Text entered: " + text);
    }
    void OnConfigCanceled(string text)
    {
        Debug.Log("They clicked cancel.  Text entered: " + text);
    }

*/


public class RTNotepad : MonoBehaviour
{
    public TMPro.TMP_InputField m_textInput;
    public Action<String> m_onClickedSavedCallback;
    public Action<String> m_onClickedCancelCallback;
    public Action<String> m_onClickedApplyCallback; //when they want to Apply but not save
    public Button m_applyButton;
    public Button m_saveButton;

    //This is a little helper object designed to be called statically to create the real thing
    public static RTNotepad OpenFile(string defaultText, GameObject prefab)
    {
        GameObject go = Instantiate(prefab);
        RTNotepad goScript = go.GetComponent<RTNotepad>();
        goScript.m_textInput.text = defaultText;
        return goScript;
    }
  
    public void SetApplyButtonVisible(bool bNew)
    {
        m_applyButton.gameObject.SetActive(bNew);
    }

    public void SetSaveButtonVisible(bool bNew)
    {
        m_saveButton.gameObject.SetActive(false);
    }

    public void OnClickedSave()
    {
        m_onClickedSavedCallback.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }

    public void OnClickedApply()
    {
        m_onClickedApplyCallback.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }

    public void OnClickedCancel()
    {
        m_onClickedCancelCallback.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }
   
}
