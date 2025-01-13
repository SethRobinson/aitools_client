using UnityEngine;
using TMPro;

public class TMPLineEndingFixer : MonoBehaviour
{
    private TMP_InputField inputField;

    private void Awake()
    {
        // Get the TMP_InputField component
        inputField = GetComponent<TMP_InputField>();

        if (inputField == null)
        {
            Debug.LogError("TMPLineEndingFixer requires a TMP_InputField component!");
            return;
        }

        // Add our listener
        inputField.onValueChanged.AddListener(OnInputValueChanged);
    }

    private void OnInputValueChanged(string newValue)
    {
        // Skip processing if the text is empty or null
        if (string.IsNullOrEmpty(newValue))
            return;
    
        // Replace all possible line ending combinations with \n
        string normalized = newValue.Replace("\r\n", "\n").Replace("\r", "");

        // Only update if the text actually changed to avoid infinite loop
        if (normalized != newValue)
        {
            // Store current caret position
            int caretPosition = inputField.caretPosition;

            // Update the text
            inputField.text = normalized;

            // Restore caret position, adjusting for any removed \r characters
            int positionDiff = newValue.Length - normalized.Length;
            inputField.caretPosition = Mathf.Max(0, caretPosition - positionDiff);
        }
    }

    private void OnDestroy()
    {
        // Clean up listener when the component is destroyed
        if (inputField != null)
        {
            inputField.onValueChanged.RemoveListener(OnInputValueChanged);
        }
    }
}