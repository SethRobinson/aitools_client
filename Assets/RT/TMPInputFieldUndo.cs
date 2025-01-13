using TMPro;
using UnityEngine;

public class TMPInputFieldUndo : MonoBehaviour
{
    private TMP_InputField inputField;
    private const int BUFFER_SIZE = 10;
    private string[] undoBuffer = new string[BUFFER_SIZE];
    private int undoIndex = -1;
    private int undoMax = -1;
    private bool isUndoRedoOperation = false;
    
    void Start()
    {
        inputField = GetComponent<TMP_InputField>();
        inputField.onValueChanged.AddListener(OnTextChanged);
    }

    void Update()
    {
        bool isCtrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        
        if (isCtrlPressed)
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }
        }
    }

    void OnTextChanged(string newText)
    {
        // Don't record if this change was from an undo/redo operation
        if (isUndoRedoOperation)
        {
            isUndoRedoOperation = false;
            return;
        }

        // Add new state to undo buffer
        undoIndex++;
        
        // If we're starting a new branch after some undos, clear the redo stack
        if (undoIndex < undoMax)
        {
            undoMax = undoIndex;
        }
        
        // If we're at buffer capacity, shift everything down
        if (undoIndex >= BUFFER_SIZE)
        {
            for (int i = 1; i < BUFFER_SIZE; i++)
            {
                undoBuffer[i-1] = undoBuffer[i];
            }
            undoIndex = BUFFER_SIZE - 1;
            undoMax = undoIndex;
        }
        
        undoBuffer[undoIndex] = newText;
        undoMax = Mathf.Max(undoMax, undoIndex);
    }

    void Undo()
    {
        if (undoIndex > 0)
        {
            undoIndex--;
            isUndoRedoOperation = true;
            inputField.text = undoBuffer[undoIndex];
            inputField.caretPosition = inputField.text.Length;
        }
    }

    void Redo()
    {
        if (undoIndex < undoMax)
        {
            undoIndex++;
            isUndoRedoOperation = true;
            inputField.text = undoBuffer[undoIndex];
            inputField.caretPosition = inputField.text.Length;
        }
    }

    // Optional: Clear undo history when the input field is deselected
    public void ClearUndoHistory()
    {
        undoIndex = -1;
        undoMax = -1;
        System.Array.Clear(undoBuffer, 0, BUFFER_SIZE);
    }
}