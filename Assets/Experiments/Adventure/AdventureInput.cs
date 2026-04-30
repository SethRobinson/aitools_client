using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles Enter key behavior for the Adventure chat input field.
/// Enter = submit, Shift+Enter = newline.
/// Pastes that contain newlines are kept intact (they will not auto-submit
/// just because they happen to contain a '\n').
/// Attach this to a TMP_InputField GameObject.
/// </summary>
public class AdventureInput : MonoBehaviour
{
    private TMP_InputField inputField;

    // Track shift state every frame since checking during the validate callback
    // may be unreliable depending on input dispatch timing.
    private bool _shiftHeldThisFrame = false;

    // Track enter key state to detect a fresh press (vs. held).
    private bool _enterWasPressed = false;

    // Used to distinguish a single Enter press (1 validated char this frame)
    // from a paste (many validated chars this frame). This is far more reliable
    // than polling Ctrl+V because it also handles right-click "Paste" and
    // doesn't depend on the user still holding the keys when the paste arrives.
    private int _frameOfLastValidate = -1;
    private int _charsValidatedThisFrame = 0;
    private bool _newlinePendingDecision = false;
    private bool _decisionCoroutineRunning = false;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        if (inputField != null)
        {
            inputField.onValidateInput += ValidateInput;
        }
    }

    private void Update()
    {
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

        // Manually handle Shift+Enter since it doesn't reach onValidateInput.
        if (inputField != null && inputField.isFocused && _shiftHeldThisFrame && enterCurrentlyPressed)
        {
            if (!_enterWasPressed)
            {
                int caretPos = inputField.caretPosition;
                inputField.text = inputField.text.Insert(caretPos, "\n");
                inputField.caretPosition = caretPos + 1;
                inputField.ActivateInputField();
            }
        }

        _enterWasPressed = enterCurrentlyPressed;
    }

    private char ValidateInput(string text, int charIndex, char addedChar)
    {
        // Strip carriage returns so Windows CRLF pastes don't leave stray \r in the text.
        if (addedChar == '\r')
        {
            return '\0';
        }

        int currentFrame = Time.frameCount;
        if (currentFrame != _frameOfLastValidate)
        {
            _frameOfLastValidate = currentFrame;
            _charsValidatedThisFrame = 0;
        }
        _charsValidatedThisFrame++;

        if (addedChar == '\n')
        {
            // If shift is held the user is explicitly asking for a newline.
            // (Shift+Enter is normally handled in Update, but be safe in case
            // a future TMP version starts routing it through here.)
            if (_shiftHeldThisFrame)
            {
                return '\n';
            }

            // Defer the submit decision until end-of-frame. By then we'll know
            // whether this '\n' arrived alone (a real Enter press) or as part
            // of a multi-character paste.
            _newlinePendingDecision = true;
            if (!_decisionCoroutineRunning)
            {
                _decisionCoroutineRunning = true;
                StartCoroutine(DecideSubmitOrPaste());
            }

            // Tentatively allow the newline; we'll strip it back out if we
            // decide this was actually a submit.
            return '\n';
        }

        return addedChar;
    }

    private IEnumerator DecideSubmitOrPaste()
    {
        // Wait until all input events for this frame have been processed.
        yield return new WaitForEndOfFrame();

        bool wasPaste = _charsValidatedThisFrame > 1;
        bool hadNewline = _newlinePendingDecision;
        _newlinePendingDecision = false;
        _decisionCoroutineRunning = false;

        if (hadNewline && !wasPaste && inputField != null)
        {
            // A single '\n' arrived this frame -> real Enter press.
            // Remove the tentatively-inserted newline and submit.
            string t = inputField.text;
            int idx = t.LastIndexOf('\n');
            if (idx >= 0)
            {
                inputField.text = t.Remove(idx, 1);
            }
            SubmitText();
        }
        // Otherwise: paste (or nothing to do) - leave the text alone so the
        // newlines from the paste are preserved for further editing.
    }

    private void SubmitText()
    {
        if (!AdventureLogic.Get().OnTextSubmitted(inputField.text))
        {
            return;
        }
        inputField.text = "";

        inputField.ActivateInputField();
        inputField.Select();
    }
}
