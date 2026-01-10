using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// Prevents keyboard navigation from interfering with text input.
/// When any TMP_InputField is focused, this disables navigation events
/// to prevent keys like 'F' from jumping focus to other UI elements.
/// 
/// Attach this to the EventSystem GameObject.
/// </summary>
public class InputFieldNavigationBlocker : MonoBehaviour
{
    private InputSystemUIInputModule _inputModule;
    private bool _wasNavigationEnabled = true;
    
    void Start()
    {
        _inputModule = GetComponent<InputSystemUIInputModule>();
        if (_inputModule == null)
        {
            Debug.LogWarning("InputFieldNavigationBlocker: No InputSystemUIInputModule found on this GameObject.");
        }
    }
    
    void Update()
    {
        if (_inputModule == null) return;
        
        bool anyInputFieldFocused = IsAnyInputFieldFocused();
        
        // Disable move (navigation) action when typing in input fields
        if (anyInputFieldFocused && _inputModule.move.action.enabled)
        {
            _inputModule.move.action.Disable();
            _wasNavigationEnabled = true;
        }
        else if (!anyInputFieldFocused && _wasNavigationEnabled && !_inputModule.move.action.enabled)
        {
            _inputModule.move.action.Enable();
            _wasNavigationEnabled = false;
        }
    }
    
    private bool IsAnyInputFieldFocused()
    {
        // Check if current selected object is a TMP_InputField
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null)
        {
            var inputField = selected.GetComponent<TMP_InputField>();
            if (inputField != null && inputField.isFocused)
            {
                return true;
            }
        }
        
        // Also check all active input fields in case selection tracking is off
        var allInputFields = FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None);
        foreach (var field in allInputFields)
        {
            if (field.isFocused)
            {
                return true;
            }
        }
        
        return false;
    }
}
