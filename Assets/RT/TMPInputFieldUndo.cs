using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Adds Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z undo+redo to a TMP_InputField. Behaves like a
// normal text editor: groups bursts of typing into a single step, preserves caret
// position, only acts while the field is focused, and exposes ResetHistory() so
// external code that wipes the field (e.g. AIChatPanel after sending) doesn't leak
// stale text back via Ctrl+Z.
public class TMPInputFieldUndo : MonoBehaviour
{
    private const int MaxHistory = 200;
    private const float GroupingWindow = 0.6f;

    private struct Snapshot
    {
        public string text;
        public int caret;
    }

    private TMP_InputField inputField;
    private readonly List<Snapshot> history = new List<Snapshot>();
    private int historyIndex = -1;
    private float lastChangeTime = -999f;
    private bool isApplyingHistory;

    void Start()
    {
        inputField = GetComponent<TMP_InputField>();
        if (inputField == null)
        {
            enabled = false;
            return;
        }
        inputField.onValueChanged.AddListener(OnTextChanged);
        // Seed with the current state so the very first edit can be undone.
        history.Add(new Snapshot { text = inputField.text ?? "", caret = inputField.caretPosition });
        historyIndex = 0;
    }

    void OnDestroy()
    {
        if (inputField != null)
            inputField.onValueChanged.RemoveListener(OnTextChanged);
    }

    void Update()
    {
        if (inputField == null || !inputField.isFocused) return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrl) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.Z))
        {
            if (shift) Redo(); else Undo();
        }
        else if (Input.GetKeyDown(KeyCode.Y))
        {
            Redo();
        }
    }

    void OnTextChanged(string newText)
    {
        if (isApplyingHistory) return;
        Push(newText ?? "", inputField.caretPosition);
    }

    void Push(string text, int caret)
    {
        // Drop any redo-future when the user types after undoing.
        if (historyIndex < history.Count - 1)
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);

        float now = Time.unscaledTime;
        bool merge = historyIndex >= 0
                     && (now - lastChangeTime) < GroupingWindow
                     && ShouldMerge(history[historyIndex].text, text);

        if (merge)
        {
            history[historyIndex] = new Snapshot { text = text, caret = caret };
        }
        else
        {
            history.Add(new Snapshot { text = text, caret = caret });
            historyIndex = history.Count - 1;
            if (history.Count > MaxHistory)
            {
                int trim = history.Count - MaxHistory;
                history.RemoveRange(0, trim);
                historyIndex -= trim;
            }
        }

        lastChangeTime = now;
    }

    // Merge a typing burst into the previous step, but break on whitespace / newline so
    // each "word" is its own undo unit (matches what most editors do).
    static bool ShouldMerge(string prev, string next)
    {
        int diff = Mathf.Abs(prev.Length - next.Length);
        if (diff != 1) return false;
        if (next.Length > prev.Length)
        {
            char added = FirstDiffChar(prev, next);
            if (added == '\n' || added == '\r' || added == ' ' || added == '\t') return false;
        }
        return true;
    }

    static char FirstDiffChar(string prev, string next)
    {
        int i = 0;
        while (i < prev.Length && i < next.Length && prev[i] == next[i]) i++;
        return i < next.Length ? next[i] : '\0';
    }

    void Undo()
    {
        if (historyIndex <= 0) return;
        historyIndex--;
        Apply(history[historyIndex]);
    }

    void Redo()
    {
        if (historyIndex >= history.Count - 1) return;
        historyIndex++;
        Apply(history[historyIndex]);
    }

    void Apply(Snapshot s)
    {
        isApplyingHistory = true;
        try
        {
            string text = s.text ?? "";
            inputField.text = text;
            int caret = Mathf.Clamp(s.caret, 0, text.Length);
            inputField.caretPosition = caret;
            inputField.selectionAnchorPosition = caret;
            inputField.selectionFocusPosition = caret;
        }
        finally
        {
            isApplyingHistory = false;
        }
        // Force a fresh group on the next real keystroke.
        lastChangeTime = -999f;
    }

    // Call when external code wipes the field (e.g. after a chat message is sent),
    // so Ctrl+Z doesn't restore the just-sent text.
    public void ResetHistory()
    {
        history.Clear();
        historyIndex = -1;
        lastChangeTime = -999f;
        if (inputField != null)
        {
            history.Add(new Snapshot { text = inputField.text ?? "", caret = inputField.caretPosition });
            historyIndex = 0;
        }
    }

    public void ClearUndoHistory() => ResetHistory();
}
