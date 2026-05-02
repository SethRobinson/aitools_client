using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Handles Enter key behavior for the Adventure chat input field.
/// Enter = submit, Shift+Enter = newline.
/// Pastes that contain newlines are kept intact (they will not auto-submit
/// just because they happen to contain a '\n').
/// Attach this to a TMP_InputField GameObject.
/// </summary>
public class AdventureInput : MonoBehaviour
{
    private static AdventureInput _instance;
    public static AdventureInput Get() => _instance;

    private TMP_InputField inputField;

    // Image attachments for vision-capable LLMs. Built at runtime so we don't have to
    // touch the Adventure scene/prefab. The strip container floats just above the input
    // field and is hidden / zero-height when no attachments are pending.
    private ChatImageAttachmentZone _attachmentZone;
    private RectTransform _stripContainer;
    private const float ATTACHMENT_STRIP_HEIGHT = 70f;

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
        _instance = this;
        inputField = GetComponent<TMP_InputField>();
        if (inputField != null)
        {
            inputField.onValidateInput += ValidateInput;
        }

        BuildAttachmentZone();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// Create a horizontal thumbnail strip just above the PromptTextInput plus a
    /// ChatImageAttachmentZone that wires drag-drop and Ctrl+V paste to it. The
    /// container is parented to the input field's parent (so it lives inside
    /// AdventureGUI) and stays hidden until the user attaches something.
    /// </summary>
    private void BuildAttachmentZone()
    {
        if (inputField == null) return;

        var inputRT = inputField.GetComponent<RectTransform>();
        if (inputRT == null) return;
        var parentRT = inputRT.parent as RectTransform;
        if (parentRT == null) return;

        // Container: anchored to the bottom of the parent (same anchor as PromptTextInput),
        // sized to match the input's width so it visually belongs to it, and shifted up by
        // the input's height so the strip floats just above it.
        var stripGo = new GameObject("AdventureAttachmentsStrip", typeof(RectTransform));
        stripGo.transform.SetParent(parentRT, false);
        var stripRT = stripGo.GetComponent<RectTransform>();

        stripRT.anchorMin = inputRT.anchorMin;
        stripRT.anchorMax = inputRT.anchorMax;
        stripRT.pivot = new Vector2(inputRT.pivot.x, 0f);
        stripRT.sizeDelta = new Vector2(inputRT.sizeDelta.x, 0f); // height grows with content

        Vector2 inputAnchored = inputRT.anchoredPosition;
        float inputHeight = inputRT.rect.height;
        // Place the strip's bottom edge a few pixels above the input's top edge.
        // pivot.y == 0 means anchoredPosition.y is the bottom of the strip's rect.
        stripRT.anchoredPosition = new Vector2(inputAnchored.x, inputAnchored.y + inputHeight + 4f);

        // Subtle background so the user can see the drop target before they drop anything.
        var bg = stripGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.25f);
        bg.raycastTarget = false;

        var hlg = stripGo.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        _stripContainer = stripRT;

        // Pick a font from any TMP text already in the scene (matches AIChatPanel's trick).
        TMP_FontAsset font = null;
        var anyTmp = FindAnyObjectByType<TextMeshProUGUI>();
        if (anyTmp != null) font = anyTmp.font;
        if (font == null) font = TMP_Settings.defaultFontAsset;

        // Drop target = the input field rect itself, so dragging onto the typing box
        // (or onto the strip area just above it) attaches the image. The strip
        // container is invisible when empty, so testing against the input rect gives
        // the most natural target.
        _attachmentZone = gameObject.AddComponent<ChatImageAttachmentZone>();
        _attachmentZone.Initialize(
            dropTarget: inputRT,
            stripContainer: _stripContainer,
            pasteField: inputField,
            font: font,
            stripHeight: ATTACHMENT_STRIP_HEIGHT);
    }

    /// <summary>True if the user has staged at least one image to send with the next submit.</summary>
    public bool HasPendingAttachments
        => _attachmentZone != null && _attachmentZone.HasAttachments;

    /// <summary>
    /// Pull the staged attachments out as base64 PNG strings, clearing the zone in the
    /// process. Call from AdventureLogic right before StartLLMRequest so the bytes can
    /// be attached to the user's outgoing GTPChatLine.
    /// </summary>
    public List<string> ConsumePendingAttachmentsAsBase64()
    {
        var result = new List<string>();
        if (_attachmentZone == null) return result;
        foreach (var bytes in _attachmentZone.GetAttachmentBytes())
        {
            if (bytes == null) continue;
            result.Add(Convert.ToBase64String(bytes));
        }
        _attachmentZone.ClearAttachments();
        return result;
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
