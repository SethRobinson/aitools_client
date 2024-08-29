using TMPro;
using UnityEngine;

public class AdventureInput : MonoBehaviour
{
    private TMP_InputField inputField;
    private bool shouldSubmit = false;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        inputField.onValidateInput += ValidateInput;
        inputField.onEndEdit.AddListener(OnEndEdit);
    }

    private void Update()
    {
        if (shouldSubmit)
        {
            SubmitText();
        }
    }

    private char ValidateInput(string text, int charIndex, char addedChar)
    {
        if ((addedChar == '\n' ) &&
            !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            shouldSubmit = true;
            return '\0'; // Prevent the newline from being added
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