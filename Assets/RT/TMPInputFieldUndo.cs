using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Adds Windows-style multi-step undo/redo to TMP_InputField. TMP does not provide
// runtime text undo, so this component records settled text/caret/selection states
// after TMP and caller code have finished mutating the field for the frame.
[DisallowMultipleComponent]
public class TMPInputFieldUndo : MonoBehaviour
{
    private const int MaxHistory = 200;
    private const float GroupingWindow = 0.65f;

    private enum EditKind
    {
        None,
        CharacterInsert,
        CharacterDelete,
        Other
    }

    private struct Snapshot
    {
        public string text;
        public int caretPosition;
        public int selectionAnchorPosition;
        public int selectionFocusPosition;
        public int stringPosition;
        public int selectionStringAnchorPosition;
        public int selectionStringFocusPosition;
    }

    private struct EditDelta
    {
        public EditKind kind;
        public int index;
        public bool boundary;
    }

    private TMP_InputField inputField;
    private readonly List<Snapshot> history = new List<Snapshot>();
    private int historyIndex = -1;
    private float lastChangeTime = -999f;
    private EditKind lastEditKind = EditKind.None;
    private int lastEditIndex = -1;
    private bool lastEditWasBoundary = true;
    private bool isApplyingHistory;
    private bool pendingUserChange;
    private bool pendingExternalReset;

    public static TMPInputFieldUndo Ensure(TMP_InputField input)
    {
        if (input == null) return null;

        var undo = input.GetComponent<TMPInputFieldUndo>();
        if (undo == null)
            undo = input.gameObject.AddComponent<TMPInputFieldUndo>();

        undo.BindIfNeeded();
        return undo;
    }

    public static void EnsureInChildren(GameObject root, bool includeInactive = true)
    {
        if (root == null) return;
        EnsureInChildren(root.transform, includeInactive);
    }

    public static void EnsureInChildren(Component root, bool includeInactive = true)
    {
        if (root == null) return;
        EnsureInChildren(root.transform, includeInactive);
    }

    public static void EnsureInChildren(Transform root, bool includeInactive = true)
    {
        if (root == null) return;

        foreach (var input in root.GetComponentsInChildren<TMP_InputField>(includeInactive))
            Ensure(input);
    }

    public static void ResetHistory(TMP_InputField input)
    {
        Ensure(input)?.ResetHistory();
    }

    public static void ResetHistoryInChildren(GameObject root, bool includeInactive = true)
    {
        if (root == null) return;
        ResetHistoryInChildren(root.transform, includeInactive);
    }

    public static void ResetHistoryInChildren(Component root, bool includeInactive = true)
    {
        if (root == null) return;
        ResetHistoryInChildren(root.transform, includeInactive);
    }

    public static void ResetHistoryInChildren(Transform root, bool includeInactive = true)
    {
        if (root == null) return;

        foreach (var input in root.GetComponentsInChildren<TMP_InputField>(includeInactive))
            ResetHistory(input);
    }

    private void Awake()
    {
        BindIfNeeded();
    }

    private void OnEnable()
    {
        BindIfNeeded();

        if (inputField != null && history.Count == 0)
            ResetHistory();
    }

    private void OnDestroy()
    {
        if (inputField != null)
            inputField.onValueChanged.RemoveListener(OnTextChanged);
    }

    private void Update()
    {
        if (inputField == null || !inputField.isFocused || !IsUserEditable(inputField))
            return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrl) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.Z))
        {
            if (shift) Redo();
            else Undo();
        }
        else if (Input.GetKeyDown(KeyCode.Y))
        {
            Redo();
        }
    }

    private void LateUpdate()
    {
        if (inputField == null) return;

        if (pendingExternalReset)
        {
            ResetHistory();
            return;
        }

        if (pendingUserChange)
        {
            pendingUserChange = false;
            Push(Capture());
        }
    }

    private void BindIfNeeded()
    {
        if (inputField != null) return;

        inputField = GetComponent<TMP_InputField>();
        if (inputField == null)
        {
            enabled = false;
            return;
        }

        inputField.onValueChanged.AddListener(OnTextChanged);
        ResetHistory();
    }

    private void OnTextChanged(string _)
    {
        if (isApplyingHistory || inputField == null)
            return;

        if (!inputField.isFocused || !IsUserEditable(inputField))
        {
            pendingUserChange = false;
            pendingExternalReset = true;
            return;
        }

        pendingUserChange = true;
    }

    private void Push(Snapshot snapshot)
    {
        if (historyIndex >= 0 && SnapshotsEqual(history[historyIndex], snapshot))
            return;

        if (historyIndex < 0)
        {
            history.Add(snapshot);
            historyIndex = 0;
            ResetGrouping();
            return;
        }

        Snapshot previous = history[historyIndex];
        EditDelta delta = Analyze(previous.text, snapshot.text);

        if (historyIndex < history.Count - 1)
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);

        bool merge = ShouldMerge(delta);
        if (merge)
        {
            history[historyIndex] = snapshot;
        }
        else
        {
            history.Add(snapshot);
            historyIndex = history.Count - 1;
            TrimHistoryIfNeeded();
        }

        lastChangeTime = Time.unscaledTime;
        lastEditKind = delta.kind;
        lastEditIndex = delta.index;
        lastEditWasBoundary = delta.boundary || delta.kind == EditKind.Other;
    }

    private bool ShouldMerge(EditDelta delta)
    {
        if (delta.kind == EditKind.None || delta.kind == EditKind.Other || delta.boundary)
            return false;

        if (lastEditWasBoundary || lastEditKind != delta.kind)
            return false;

        if (Time.unscaledTime - lastChangeTime > GroupingWindow)
            return false;

        if (delta.kind == EditKind.CharacterInsert)
            return delta.index == lastEditIndex + 1;

        if (delta.kind == EditKind.CharacterDelete)
            return delta.index == lastEditIndex || delta.index == lastEditIndex - 1;

        return false;
    }

    private static EditDelta Analyze(string previous, string next)
    {
        previous = previous ?? "";
        next = next ?? "";

        if (previous == next)
            return new EditDelta { kind = EditKind.None, index = -1, boundary = true };

        int prefix = 0;
        int min = Mathf.Min(previous.Length, next.Length);
        while (prefix < min && previous[prefix] == next[prefix])
            prefix++;

        int previousSuffix = previous.Length - 1;
        int nextSuffix = next.Length - 1;
        while (previousSuffix >= prefix && nextSuffix >= prefix && previous[previousSuffix] == next[nextSuffix])
        {
            previousSuffix--;
            nextSuffix--;
        }

        int removed = previousSuffix - prefix + 1;
        int added = nextSuffix - prefix + 1;

        if (removed == 0 && added == 1)
        {
            char c = next[prefix];
            return new EditDelta
            {
                kind = EditKind.CharacterInsert,
                index = prefix,
                boundary = IsBoundaryChar(c)
            };
        }

        if (removed == 1 && added == 0)
        {
            char c = previous[prefix];
            return new EditDelta
            {
                kind = EditKind.CharacterDelete,
                index = prefix,
                boundary = IsBoundaryChar(c)
            };
        }

        return new EditDelta { kind = EditKind.Other, index = prefix, boundary = true };
    }

    private static bool IsBoundaryChar(char c)
    {
        return c == '\n' || c == '\r' || c == '\t' || c == ' ';
    }

    private void TrimHistoryIfNeeded()
    {
        if (history.Count <= MaxHistory) return;

        int trim = history.Count - MaxHistory;
        history.RemoveRange(0, trim);
        historyIndex = Mathf.Max(0, historyIndex - trim);
    }

    private void Undo()
    {
        CommitPendingBeforeHistoryNavigation();
        if (historyIndex <= 0) return;

        historyIndex--;
        Apply(history[historyIndex]);
    }

    private void Redo()
    {
        CommitPendingBeforeHistoryNavigation();
        if (historyIndex >= history.Count - 1) return;

        historyIndex++;
        Apply(history[historyIndex]);
    }

    private void CommitPendingBeforeHistoryNavigation()
    {
        if (pendingExternalReset)
        {
            ResetHistory();
            return;
        }

        if (!pendingUserChange)
            return;

        pendingUserChange = false;
        Push(Capture());
    }

    private void Apply(Snapshot snapshot)
    {
        if (inputField == null) return;

        isApplyingHistory = true;
        try
        {
            string text = snapshot.text ?? "";
            inputField.text = text;

            int max = text.Length;
            bool hasSelection = snapshot.selectionAnchorPosition != snapshot.selectionFocusPosition
                || snapshot.selectionStringAnchorPosition != snapshot.selectionStringFocusPosition;

            if (hasSelection)
            {
                inputField.selectionAnchorPosition = Mathf.Clamp(snapshot.selectionAnchorPosition, 0, max);
                inputField.selectionFocusPosition = Mathf.Clamp(snapshot.selectionFocusPosition, 0, max);
                inputField.selectionStringAnchorPosition = Mathf.Clamp(snapshot.selectionStringAnchorPosition, 0, max);
                inputField.selectionStringFocusPosition = Mathf.Clamp(snapshot.selectionStringFocusPosition, 0, max);
            }
            else
            {
                inputField.caretPosition = Mathf.Clamp(snapshot.caretPosition, 0, max);
                inputField.stringPosition = Mathf.Clamp(snapshot.stringPosition, 0, max);
            }

            inputField.ForceLabelUpdate();
        }
        finally
        {
            isApplyingHistory = false;
        }

        pendingUserChange = false;
        pendingExternalReset = false;
        ResetGrouping();
    }

    public void ResetHistory()
    {
        pendingUserChange = false;
        pendingExternalReset = false;
        history.Clear();
        historyIndex = -1;
        ResetGrouping();

        if (inputField == null)
            BindIfNeeded();

        if (inputField != null)
        {
            history.Add(Capture());
            historyIndex = 0;
        }
    }

    public void ClearUndoHistory()
    {
        ResetHistory();
    }

    private Snapshot Capture()
    {
        if (inputField == null)
            return default;

        string text = inputField.text ?? "";
        int max = text.Length;

        return new Snapshot
        {
            text = text,
            caretPosition = Mathf.Clamp(inputField.caretPosition, 0, max),
            selectionAnchorPosition = Mathf.Clamp(inputField.selectionAnchorPosition, 0, max),
            selectionFocusPosition = Mathf.Clamp(inputField.selectionFocusPosition, 0, max),
            stringPosition = Mathf.Clamp(inputField.stringPosition, 0, max),
            selectionStringAnchorPosition = Mathf.Clamp(inputField.selectionStringAnchorPosition, 0, max),
            selectionStringFocusPosition = Mathf.Clamp(inputField.selectionStringFocusPosition, 0, max)
        };
    }

    private static bool SnapshotsEqual(Snapshot a, Snapshot b)
    {
        return (a.text ?? "") == (b.text ?? "")
            && a.caretPosition == b.caretPosition
            && a.selectionAnchorPosition == b.selectionAnchorPosition
            && a.selectionFocusPosition == b.selectionFocusPosition
            && a.stringPosition == b.stringPosition
            && a.selectionStringAnchorPosition == b.selectionStringAnchorPosition
            && a.selectionStringFocusPosition == b.selectionStringFocusPosition;
    }

    private static bool IsUserEditable(TMP_InputField input)
    {
        return input != null && input.interactable && !input.readOnly;
    }

    private void ResetGrouping()
    {
        lastChangeTime = -999f;
        lastEditKind = EditKind.None;
        lastEditIndex = -1;
        lastEditWasBoundary = true;
    }
}

public class TMPInputFieldUndoInstaller : MonoBehaviour
{
    private const float ScanInterval = 0.5f;
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (Object.FindAnyObjectByType<TMPInputFieldUndoInstaller>() != null)
            return;

        var go = new GameObject("TMPInputFieldUndoInstaller");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<TMPInputFieldUndoInstaller>();
    }

    private void Start()
    {
        InstallAll();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime)
            return;

        nextScanTime = Time.unscaledTime + ScanInterval;
        InstallAll();
    }

    private static void InstallAll()
    {
        var inputs = Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include);

        foreach (var input in inputs)
        {
            if (input == null || !input.gameObject.scene.IsValid())
                continue;

            TMPInputFieldUndo.Ensure(input);
        }
    }
}
