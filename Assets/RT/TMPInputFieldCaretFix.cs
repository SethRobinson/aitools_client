using TMPro;
using UnityEngine;

// Repairs caret/selection rendering on TMP_InputFields that were built at runtime.
//
// TMP_InputField only creates its caret/selection renderer (the "Caret" child carrying a
// TMP_SelectionCaret) inside OnEnable, and only when textComponent is already assigned.
// UI built with TMP_DefaultControls.CreateInputField (or any AddComponent-then-wire flow)
// runs that OnEnable during AddComponent - before textComponent/textViewport are assigned
// and before the field is parented - so typing works, but the caret and the selection
// highlight have no renderer and can never draw, which also makes mouse text selection
// and cut/copy look broken. OnEnable also caches the parent ScrollRect (mouse-wheel
// forwarding) and the viewport RectMask2D, so those are silently missed too.
//
// Cycling `enabled` after the field is fully wired AND parented re-runs OnEnable with
// everything in place. The spawned TMP_SelectionCaret graphic is then tinted directly:
// on Unity 6 / TMP 3 it otherwise can render invisible (same trick AIChatCaretFixer and
// LLMSettingsPanel.ForceReinitializeTMPInputFields use).
public static class TMPInputFieldCaretFix
{
    public static void Apply(TMP_InputField input)
    {
        if (input == null || !Application.isPlaying) return;

        // Don't cycle a field the user is editing - OnDisable would kick them out of it.
        if (!input.isFocused && input.enabled && input.gameObject.activeInHierarchy)
        {
            input.enabled = false;
            input.enabled = true;
        }

        Color caretColor = input.customCaretColor
            ? input.caretColor
            : (input.textComponent != null ? input.textComponent.color : Color.black);

        foreach (var caret in input.GetComponentsInChildren<TMP_SelectionCaret>(true))
        {
            caret.color = caretColor;
            caret.maskable = true;
            caret.SetAllDirty();
        }

        input.ForceLabelUpdate();
    }
}
