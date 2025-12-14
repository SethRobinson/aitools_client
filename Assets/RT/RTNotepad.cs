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

    RTNotepad m_activeNotepad;

    public void OnConfigButton()
    {
        m_activeNotepad = RTNotepad.OpenFile("some text here", m_notepadTemplatePrefab);
        m_activeNotepad.m_onClickedSavedCallback += OnConfigSaved;
        m_activeNotepad.m_onClickedCancelCallback += OnConfigCanceled;
        m_activeNotepad.m_onClickedOpenExternalCallback += OnOpenExternal;
        m_activeNotepad.m_onClickedReloadCallback += OnReload;
    }

    void OnConfigSaved(string text)
    {
        Debug.Log("They clicked save.  Text entered: " + text);
    }
    void OnConfigCanceled(string text)
    {
        Debug.Log("They clicked cancel.  Text entered: " + text);
    }
    void OnOpenExternal(string text)
    {
        // Open file in external editor, e.g.: System.Diagnostics.Process.Start("config.txt");
    }
    void OnReload(string text)
    {
        // Reload from disk and update: m_activeNotepad.SetText(File.ReadAllText("config.txt"));
    }

*/


public class RTNotepad : MonoBehaviour
{
    public TMPro.TMP_InputField m_textInput;
    public Action<String> m_onClickedSavedCallback;
    public Action<String> m_onClickedCancelCallback;
    public Action<String> m_onClickedApplyCallback; //when they want to Apply but not save
    public Action<String> m_onClickedOpenExternalCallback; //when they want to open in external editor
    public Action<String> m_onClickedReloadCallback; //when they want to reload from disk
    public Button m_applyButton;
    public Button m_saveButton;
    public Button m_openExternalButton;
    public Button m_reloadButton;

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

    public void OnClickedOpenExternal()
    {
        m_onClickedOpenExternalCallback?.Invoke(m_textInput.text);
        // Don't destroy - keep dialog open so user can reload after editing
    }

    public void OnClickedReload()
    {
        m_onClickedReloadCallback?.Invoke(m_textInput.text);
        // Don't destroy - just refreshes the text
    }

    // Allow external code to update the text (useful for reload)
    public void SetText(string text)
    {
        m_textInput.text = text;
    }

    public void SetOpenExternalButtonVisible(bool bNew)
    {
        if (m_openExternalButton != null)
            m_openExternalButton.gameObject.SetActive(bNew);
    }

    public void SetReloadButtonVisible(bool bNew)
    {
        if (m_reloadButton != null)
            m_reloadButton.gameObject.SetActive(bNew);
    }
   
}
