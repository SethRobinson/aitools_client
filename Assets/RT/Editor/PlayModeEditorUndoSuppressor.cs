using UnityEditor;
using UnityEngine;

// Unity's Edit > Undo (Ctrl+Z) fires globally while the Game window has focus during
// play mode, even though the user is just trying to undo typing inside a TMP_InputField.
// The editor's undo then tries to roll back runtime-spawned GameObjects (programmatic
// dialogs, AddComponent'd scripts, TMP's auto-created "Caret" child, etc.) back to a
// pre-play state. The objects aren't registered with the editor undo system, so they
// become "dangling" and the dialog visually disappears.
//
// Fix: while in play mode, keep the editor undo stack empty. Ctrl+Z still reaches the
// game's Input system (so our per-field TMPInputFieldUndo handles it), but the editor
// has nothing to undo. Edit-mode undo is untouched.
[InitializeOnLoad]
internal static class PlayModeEditorUndoSuppressor
{
    static PlayModeEditorUndoSuppressor()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (EditorApplication.isPlaying)
            EditorApplication.update += Tick;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Undo.ClearAll();
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
        }
    }

    static void Tick()
    {
        if (EditorApplication.isPlaying)
            Undo.ClearAll();
    }
}
