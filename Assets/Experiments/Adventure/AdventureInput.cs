using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles Enter key behavior for the Adventure chat input field.
/// Enter = submit, Shift+Enter = newline.
/// Attach this to a TMP_InputField GameObject.
/// </summary>
public class AdventureInput : MonoBehaviour
{
    private TMP_InputField inputField;
    private bool shouldSubmit = false;
    
    // Track shift state every frame since checking during callback may be unreliable
    private bool _shiftHeldThisFrame = false;
    
    // Track enter key state to detect release
    private bool _enterWasPressed = false;
    
    // Track if Ctrl+V is being pressed (paste operation)
    private bool _isPasting = false;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        if (inputField != null)
        {
            inputField.onValidateInput += ValidateInput;
            inputField.onEndEdit.AddListener(OnEndEdit);
        }
    }

    private void Update()
    {
        // Track shift key state every frame
        _shiftHeldThisFrame = false;
        if (Keyboard.current != null)
        {
            _shiftHeldThisFrame = Keyboard.current.leftShiftKey.isPressed || 
                                   Keyboard.current.rightShiftKey.isPressed;
        }
        if (!_shiftHeldThisFrame)
        {
            _shiftHeldThisFrame = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
        
        // Track paste operation (Ctrl+V)
        _isPasting = false;
        if (Keyboard.current != null)
        {
            _isPasting = (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed) &&
                         Keyboard.current.vKey.isPressed;
        }
        if (!_isPasting)
        {
            _isPasting = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                         Input.GetKey(KeyCode.V);
        }
        
        // Track enter key state
        bool enterCurrentlyPressed = false;
        if (Keyboard.current != null)
        {
            enterCurrentlyPressed = Keyboard.current.enterKey.isPressed || 
                                    Keyboard.current.numpadEnterKey.isPressed;
        }
        if (!enterCurrentlyPressed)
        {
            enterCurrentlyPressed = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
        }
        
        // Manually handle Shift+Enter since it doesn't reach onValidateInput
        if (inputField != null && inputField.isFocused && _shiftHeldThisFrame && enterCurrentlyPressed)
        {
            // Only insert on the frame Enter is first pressed (not while held)
            if (!_enterWasPressed)
            {
                // Insert newline at cursor position
                int caretPos = inputField.caretPosition;
                inputField.text = inputField.text.Insert(caretPos, "\n");
                inputField.caretPosition = caretPos + 1;
                inputField.ActivateInputField();
            }
        }
        
        _enterWasPressed = enterCurrentlyPressed;
        
        if (shouldSubmit)
        {
            SubmitText();
        }
    }

    private char ValidateInput(string text, int charIndex, char addedChar)
    {
        // Handle newline character
        if (addedChar == '\n')
        {
            // Allow newlines when pasting or when Shift is held
            if (_isPasting || _shiftHeldThisFrame)
            {
                return '\n';
            }
            else
            {
                // Enter without Shift - submit
                shouldSubmit = true;
                return '\0';
            }
        }
        
        // Skip carriage returns (handle Windows CRLF in pastes)
        if (addedChar == '\r')
        {
            return '\0';
        }
        
        return addedChar;
    }

    private void OnEndEdit(string text)
    {
        if (shouldSubmit)
        {
            SubmitText();
        }
    }

    private void SubmitText()
    {
        shouldSubmit = false;

        if (!AdventureLogic.Get().OnTextSubmitted(inputField.text))
        {
            return;
        }
        inputField.text = "";
     
        // Re-focus the input field
        inputField.ActivateInputField();
        inputField.Select();
    }
}
