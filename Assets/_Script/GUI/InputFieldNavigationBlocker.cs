using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// Prevents keyboard navigation and submit actions from interfering with text input.
/// When any TMP_InputField is focused, this disables navigation and submit events
/// to allow Enter key to work properly in input fields.
/// 
/// Attach this to the EventSystem GameObject.
/// </summary>
public class InputFieldNavigationBlocker : MonoBehaviour
{
    private InputSystemUIInputModule _inputModule;
    private bool _wasNavigationEnabled = true;
    private bool _wasSubmitEnabled = true;
    
    [Tooltip("Also disable Submit action when input field is focused (fixes Enter key not working)")]
    [SerializeField] private bool _disableSubmitWhenFocused = true;
    
    [Tooltip("Enable debug logging")]
    [SerializeField] private bool _debugLog = false;
    
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
        if (anyInputFieldFocused && _inputModule.move.action != null && _inputModule.move.action.enabled)
        {
            _inputModule.move.action.Disable();
            _wasNavigationEnabled = true;
            if (_debugLog) Debug.Log("[InputFieldNavigationBlocker] Disabled move action");
        }
        else if (!anyInputFieldFocused && _wasNavigationEnabled && _inputModule.move.action != null && !_inputModule.move.action.enabled)
        {
            _inputModule.move.action.Enable();
            _wasNavigationEnabled = false;
            if (_debugLog) Debug.Log("[InputFieldNavigationBlocker] Enabled move action");
        }
        
        // Disable submit action when typing in input fields (fixes Enter key not inserting newlines)
        if (_disableSubmitWhenFocused)
        {
            if (anyInputFieldFocused && _inputModule.submit.action != null && _inputModule.submit.action.enabled)
            {
                _inputModule.submit.action.Disable();
                _wasSubmitEnabled = true;
                if (_debugLog) Debug.Log("[InputFieldNavigationBlocker] Disabled submit action - Enter should now work in input fields");
            }
            else if (!anyInputFieldFocused && _wasSubmitEnabled && _inputModule.submit.action != null && !_inputModule.submit.action.enabled)
            {
                _inputModule.submit.action.Enable();
                _wasSubmitEnabled = false;
                if (_debugLog) Debug.Log("[InputFieldNavigationBlocker] Enabled submit action");
            }
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
