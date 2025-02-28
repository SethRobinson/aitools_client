using UnityEngine;
using TMPro;
using System;
using UnityEngine.EventSystems;

/*
 
 To use this:

 public GameObject _getStringPrefab;


    public void OnClickedPresetLoad()
    {
        RTConsole.Log("Clicked preset load");
    }

    public void OnClickedPresetSave()
    {
        RTConsole.Log("Clicked preset save");

        GameObject getStringGO = Instantiate(_getStringPrefab);
        GetStringDialog getStringScript = getStringGO.GetComponentInChildren<GetStringDialog>();

        getStringScript.Init("Save Preset", "crap.txt");

        getStringScript.m_onClickedSubmitCallback += OnPresetSaved;
        getStringScript.m_onClickedCancelCallback += OnPresetCanceled;

    }

    public void OnPresetSaved(string fileName)
    {
        RTConsole.Log("Preset saved as: " + fileName);
       // SavePreset(fileName);
    }
    public void OnPresetCanceled(string fileName)
    {
        RTConsole.Log("Preset save canceled");
    }


 
 */


public class GetStringDialog : MonoBehaviour
{
    public TMP_InputField m_stringInputField;
    public TMP_Text m_titleLabel;
    public Action<String> m_onClickedSubmitCallback;
    public Action<String> m_onClickedCancelCallback;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    public void Init(string title, string defaultString)
    {
        m_titleLabel.text = title;
        m_stringInputField.text = defaultString;

    }
    public void OnCancelButton()
    {
        OnCloseWindow();

    }

    public void OnSubmitButton()
    {
        RTConsole.Log("Clicked submit");
        m_onClickedSubmitCallback?.Invoke(m_stringInputField.text);
        KillWindow();
    }

    public void OnCloseWindow()
    {
        RTConsole.Log("Clicked close");
        m_onClickedCancelCallback?.Invoke(m_stringInputField.text);
        KillWindow();

    }

    public void KillWindow()
    {
        Destroy(transform.parent.gameObject);
    }
    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            OnCloseWindow();
        }
    }
}

