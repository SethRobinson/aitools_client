using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SimpleJSON;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Programmatic multi-turn AI chat popup. Mirrors the LLMSettingsPanel pattern
/// (static Show/Hide/Toggle, lazy creation, draggable header, escape to close).
/// Adds bottom-right corner resize and renders messages as read-only TMP_InputFields
/// with markdown converted to TMP rich text (same trick AdventureText uses) so the
/// user gets styled output AND native text selection / Ctrl+C copy.
///
/// Routes requests to whichever LLM the rest of the app is using:
///   1. Tries LLMInstanceManager.GetFreeLLM/GetLeastBusyLLM (small job, no vision).
///   2. Falls back to LLMSettingsManager.GetActiveProvider/GetProviderSettings.
/// </summary>
public class AIChatPanel : MonoBehaviour
{
    private static AIChatPanel _instance;
    private static GameObject _panelRoot;

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;

    // Header
    private TextMeshProUGUI _titleText;

    // Chat content
    private ScrollRect _chatScroll;
    private RectTransform _chatContent;

    // Footer
    private TMP_InputField _inputField;
    private RectTransform _inputFieldRT;
    private Button _sendButton;
    private Button _clearButton;
    private Button _stopButton;
    private Button _copyButton;
    private TextMeshProUGUI _statusText;

    // Image attachments (drag-drop / clipboard paste) - all the heavy lifting (drop
    // intercept, paste-from-clipboard, thumbnail strip UI) lives in ChatImageAttachmentZone.
    // We just own the strip container's RectTransform plus the footer/chat-area rects so
    // we can resize them when the strip appears/disappears.
    private ChatImageAttachmentZone _attachmentZone;
    private RectTransform _attachmentsStrip;
    private RectTransform _footerRT;
    private RectTransform _chatScrollRT;
    private const float ATTACHMENT_STRIP_HEIGHT = 70f;

    // Conversation
    private GPTPromptManager _promptManager;
    private OpenAITextCompletionManager _openAIMgr;
    private AnthropicAITextCompletionManager _anthropicMgr;
    private TexGenWebUITextCompletionManager _texGenMgr;
    private GeminiTextCompletionManager _geminiMgr;

    // Streaming state
    private StringBuilder _streamBuffer = new StringBuilder();
    private float _streamLastUpdate = 0f;
    private const float STREAM_UPDATE_INTERVAL = 0.1f;
    private TMP_InputField _streamingAssistantField;
    private RectTransform _streamingAssistantRT;
    private bool _isStreaming = false;
    private int _activeLLMInstanceID = -1;
    private int _activeLLMReplicaIndex = 0;
    private LLMProvider _activeProviderInFlight;

    // Sizing
    private const float DEFAULT_WIDTH = 720f;
    private const float DEFAULT_HEIGHT = 600f;
    private const float MIN_WIDTH = 480f;
    private const float MIN_HEIGHT = 360f;
    private const float HEADER_HEIGHT = 40f;
    private const float FOOTER_HEIGHT = 130f;
    private const float BaseFontSize = 14f;

    // Theme (matches LLMSettingsPanel's app-style colors).
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color UserBubbleBg = new Color(0.86f, 0.92f, 1.00f, 1f);
    private static readonly Color AssistantBubbleBg = new Color(1.00f, 1.00f, 1.00f, 1f);
    private static readonly Color InputFieldBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextPlaceholder = new Color(0.196f, 0.196f, 0.196f, 0.5f);
    private static readonly Color ResizeGripColor = new Color(0.45f, 0.45f, 0.50f, 1f);

    public static void Show()
    {
        if (_instance != null)
        {
            _panelRoot.SetActive(true);
            _instance.RefreshHeaderTitle();
            _instance.FocusInputDeferred();
            return;
        }

        _panelRoot = new GameObject("AIChatPanel");
        _instance = _panelRoot.AddComponent<AIChatPanel>();
        _instance.CreateUI();
    }

    public static void Hide()
    {
        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    public static void Toggle()
    {
        if (_panelRoot != null && _panelRoot.activeSelf)
            Hide();
        else
            Show();
    }

    private void OnDestroy()
    {
        // The ChatImageAttachmentZone component on _panelRoot auto-deregisters and frees
        // its textures in its own OnDestroy.
        _instance = null;
        _panelRoot = null;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    // ---------- UI Construction ----------

    private void CreateUI()
    {
        _font = FindFont();

        // Reuse the LLMSettingsPanel sprite cache so styling matches.
        var canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panelRoot.AddComponent<GraphicRaycaster>();

        // Conversation + provider components (added to root so coroutines + lifecycle are tied to the panel).
        _promptManager = _panelRoot.AddComponent<GPTPromptManager>();
        _openAIMgr = _panelRoot.AddComponent<OpenAITextCompletionManager>();
        _anthropicMgr = _panelRoot.AddComponent<AnthropicAITextCompletionManager>();
        _texGenMgr = _panelRoot.AddComponent<TexGenWebUITextCompletionManager>();
        _geminiMgr = _panelRoot.AddComponent<GeminiTextCompletionManager>();

        // Main panel
        var main = new GameObject("MainPanel");
        main.transform.SetParent(_panelRoot.transform, false);
        _mainPanel = main.AddComponent<RectTransform>();
        _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
        _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
        _mainPanel.pivot = new Vector2(0.5f, 0.5f);
        _mainPanel.sizeDelta = new Vector2(DEFAULT_WIDTH, DEFAULT_HEIGHT);
        var panelImg = main.AddComponent<Image>();
        panelImg.color = PanelBg;

        CreateHeader();
        CreateChatArea();
        CreateFooter();
        CreateResizeGrip();

        RefreshHeaderTitle();
        AddSystemMessage("New chat. Conversation history is kept until you click Clear or close the app.");

        FocusInputDeferred();
    }

    private void CreateHeader()
    {
        var header = new GameObject("Header");
        header.transform.SetParent(_mainPanel, false);
        var rt = header.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, HEADER_HEIGHT);
        rt.anchoredPosition = Vector2.zero;
        var headerImg = header.AddComponent<Image>();
        headerImg.color = HeaderBg;

        // Reuse the same drag handler the LLMSettingsPanel uses.
        header.AddComponent<PanelDragHandler>().SetTarget(_mainPanel);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-36, 0);

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "AI Chat";
        _titleText.font = _font;
        _titleText.fontSize = 18;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = TextTitle;
        _titleText.alignment = TextAlignmentOptions.MidlineLeft;

        // Close button
        var close = new GameObject("Close");
        close.transform.SetParent(header.transform, false);
        var closeRt = close.AddComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1, 0.5f);
        closeRt.anchorMax = new Vector2(1, 0.5f);
        closeRt.pivot = new Vector2(1, 0.5f);
        closeRt.sizeDelta = new Vector2(24, 24);
        closeRt.anchoredPosition = new Vector2(-6, 0);

        var closeImg = close.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.25f, 0.25f, 1f);
        var closeBtn = close.AddComponent<Button>();
        closeBtn.onClick.AddListener(Hide);

        var xObj = new GameObject("X");
        xObj.transform.SetParent(close.transform, false);
        var xRt = xObj.AddComponent<RectTransform>();
        xRt.anchorMin = Vector2.zero;
        xRt.anchorMax = Vector2.one;
        xRt.offsetMin = Vector2.zero;
        xRt.offsetMax = Vector2.zero;

        var xTxt = xObj.AddComponent<TextMeshProUGUI>();
        xTxt.text = "X";
        xTxt.font = _font;
        xTxt.fontSize = 15;
        xTxt.color = Color.white;
        xTxt.alignment = TextAlignmentOptions.Center;
    }

    private void CreateChatArea()
    {
        var scrollGo = new GameObject("ChatScrollView");
        scrollGo.transform.SetParent(_mainPanel, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.offsetMin = new Vector2(0, FOOTER_HEIGHT);
        scrollRt.offsetMax = new Vector2(0, -HEADER_HEIGHT);
        _chatScrollRT = scrollRt;

        _chatScroll = scrollGo.AddComponent<ScrollRect>();
        _chatScroll.horizontal = false;
        _chatScroll.vertical = true;
        _chatScroll.scrollSensitivity = 30f;
        _chatScroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-22, 0);
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = PanelBg;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _chatContent = content.AddComponent<RectTransform>();
        _chatContent.anchorMin = new Vector2(0, 1);
        _chatContent.anchorMax = new Vector2(1, 1);
        _chatContent.pivot = new Vector2(0.5f, 1);
        _chatContent.anchoredPosition = Vector2.zero;
        _chatContent.sizeDelta = Vector2.zero;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _chatScroll.viewport = vpRt;
        _chatScroll.content = _chatContent;

        // Scrollbar
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(scrollGo.transform, false);
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = new Vector2(1, 1);
        sbRt.pivot = new Vector2(1, 0.5f);
        sbRt.sizeDelta = new Vector2(18, 0);
        sbRt.anchoredPosition = Vector2.zero;
        sbGo.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.24f, 1f);

        var scrollbar = sbGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var handle = new GameObject("Handle");
        handle.transform.SetParent(sbGo.transform, false);
        var handleRt = handle.AddComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = new Vector2(3, 3);
        handleRt.offsetMax = new Vector2(-3, -3);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(0.45f, 0.45f, 0.5f, 1f);

        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handleImg;
        _chatScroll.verticalScrollbar = scrollbar;
    }

    private void CreateFooter()
    {
        var footer = new GameObject("Footer");
        footer.transform.SetParent(_mainPanel, false);
        var rt = footer.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, FOOTER_HEIGHT);
        rt.anchoredPosition = Vector2.zero;
        var footerImg = footer.AddComponent<Image>();
        footerImg.color = FooterBg;
        _footerRT = rt;

        // Multi-line input on the left
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "ChatInput";
        inputGo.transform.SetParent(footer.transform, false);
        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(1, 1);
        inputRt.offsetMin = new Vector2(8, 8);
        inputRt.offsetMax = new Vector2(-200, -32); // leave space for buttons (right) and status text (top)
        _inputFieldRT = inputRt;

        var inputImg = inputGo.GetComponent<Image>();
        if (inputImg != null)
        {
            inputImg.sprite = null;
            inputImg.type = Image.Type.Simple;
            inputImg.color = InputFieldBg;
        }

        _inputField = inputGo.GetComponent<TMP_InputField>();
        // MultiLineNewline: Enter naturally inserts a newline. We then intercept Enter
        // via onValidateInput below: when Shift is NOT held we reject the newline and
        // defer-send instead. (TMP's built-in MultiLineSubmit mode is supposed to do
        // Shift+Enter newline natively, but Shift+Enter doesn't actually insert a
        // newline in Unity 6 / TMP 3, so we handle it ourselves.)
        _inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        _inputField.contentType = TMP_InputField.ContentType.Standard;
        _inputField.textComponent.alignment = TextAlignmentOptions.TopLeft;
        _inputField.textComponent.color = TextDark;
        _inputField.textComponent.font = _font;
        _inputField.textComponent.fontSize = BaseFontSize;
        _inputField.textComponent.textWrappingMode = TextWrappingModes.Normal;
        if (_inputField.placeholder is TextMeshProUGUI ph)
        {
            ph.text = "Type a message... (Enter sends, Shift+Enter for newline)";
            ph.font = _font;
            ph.fontSize = BaseFontSize;
            ph.color = TextPlaceholder;
            ph.alignment = TextAlignmentOptions.TopLeft;
        }
        // Note: we deliberately do NOT use LLMInputFieldVisualFixer here, because its
        // OnEnable/OnSelect call ConfigureInputFieldVisuals, which resets caretWidth to
        // the cached default (2px). Instead we install AIChatCaretFixer which re-applies
        // a fat caret on every (re)select.
        ApplyFatCaret(_inputField);
        var caretFixer = _inputField.gameObject.AddComponent<AIChatCaretFixer>();
        caretFixer.Set(_inputField);

        // Note: Enter / Shift+Enter handling is in Update() below. Using onValidateInput
        // is unreliable because Input.GetKey(Shift) can return false from inside that
        // callback (it runs during TMP's text-event processing, not the regular Update
        // phase). Detecting in Update reads shift state at a time it's guaranteed valid.

        // Status text along the top of the right side
        var statusObj = new GameObject("Status");
        statusObj.transform.SetParent(footer.transform, false);
        var statusRt = statusObj.AddComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(1, 1);
        statusRt.anchorMax = new Vector2(1, 1);
        statusRt.pivot = new Vector2(1, 1);
        statusRt.sizeDelta = new Vector2(186, 22);
        statusRt.anchoredPosition = new Vector2(-8, -6);

        _statusText = statusObj.AddComponent<TextMeshProUGUI>();
        _statusText.font = _font;
        _statusText.fontSize = 12;
        _statusText.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        _statusText.alignment = TextAlignmentOptions.MidlineRight;
        _statusText.text = "Idle";

        // Buttons stacked on the right.
        // Row 1: Send (full 186 wide). Row 2: [Copy 58][Stop 58][Clear 58], 6px gaps -> 186 wide total.
        _sendButton = CreateFooterButton(footer.transform, "Send", new Vector2(-8, -32), new Vector2(186, 30), OnSendClicked);
        _clearButton = CreateFooterButton(footer.transform, "Clear", new Vector2(-8, -68), new Vector2(58, 30), OnClearClicked);
        _stopButton = CreateFooterButton(footer.transform, "Stop", new Vector2(-72, -68), new Vector2(58, 30), OnStopClicked);
        _copyButton = CreateFooterButton(footer.transform, "Copy", new Vector2(-136, -68), new Vector2(58, 30), OnCopyClicked);
        _stopButton.interactable = false;

        CreateAttachmentsStrip(footer.transform);

        // The helper owns all attachment state (list, drop intercept, paste, thumb UI).
        // We just feed it our pre-positioned strip container + paste field, then react
        // to OnAttachmentsChanged to grow / shrink the footer + chat area.
        _attachmentZone = _panelRoot.AddComponent<ChatImageAttachmentZone>();
        _attachmentZone.Initialize(
            dropTarget: _mainPanel,
            stripContainer: _attachmentsStrip,
            pasteField: _inputField,
            font: _font,
            stripHeight: ATTACHMENT_STRIP_HEIGHT);
        _attachmentZone.OnAttachmentsChanged += OnAttachmentsChanged;
    }

    private Button CreateFooterButton(Transform parent, string text, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var btn = new GameObject("Btn_" + text);
        btn.transform.SetParent(parent, false);
        var rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = btn.AddComponent<Image>();
        img.color = Color.white;
        var button = btn.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f),
            selectedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
            disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        var txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btn.transform, false);
        var txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = 14;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = TextTitle;
        tmp.alignment = TextAlignmentOptions.Center;
        return button;
    }

    // ---------- Image attachments (drag-drop / clipboard paste) ----------

    private void CreateAttachmentsStrip(Transform footerTransform)
    {
        var strip = new GameObject("AttachmentsStrip");
        strip.transform.SetParent(footerTransform, false);
        var rt = strip.AddComponent<RectTransform>();
        // Pin to top of footer, full-width, height grows with content (set in Refresh).
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(-200, 0); // leave space on right for status/buttons
        rt.anchoredPosition = new Vector2(-100, 0); // shift left so the right side stays clear

        var hlg = strip.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        _attachmentsStrip = rt;
    }

    /// <summary>
    /// Fired by ChatImageAttachmentZone whenever the attachment count changes. We only
    /// need to grow / shrink the footer (and matching chat scroll area) so the typing
    /// field keeps its full height when the strip appears.
    /// </summary>
    private void OnAttachmentsChanged()
    {
        bool hasAttachments = _attachmentZone != null && _attachmentZone.HasAttachments;
        float extraFooterHeight = hasAttachments ? ATTACHMENT_STRIP_HEIGHT : 0f;

        // 1) Grow / shrink the footer itself so the input field still has its original
        //    height after the strip reserves space at its top.
        if (_footerRT != null)
            _footerRT.sizeDelta = new Vector2(_footerRT.sizeDelta.x, FOOTER_HEIGHT + extraFooterHeight);

        // 2) Push the chat scroll area's bottom edge up by the same amount so it doesn't
        //    overlap the now-taller footer.
        if (_chatScrollRT != null)
            _chatScrollRT.offsetMin = new Vector2(_chatScrollRT.offsetMin.x, FOOTER_HEIGHT + extraFooterHeight);

        // 3) Input field's top reserves room for the strip + the existing 32 px status row.
        if (_inputFieldRT != null)
            _inputFieldRT.offsetMax = new Vector2(_inputFieldRT.offsetMax.x, -(32f + extraFooterHeight));
    }

    private void CreateResizeGrip()
    {
        var grip = new GameObject("ResizeGrip");
        grip.transform.SetParent(_mainPanel, false);
        var rt = grip.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.sizeDelta = new Vector2(16, 16);
        rt.anchoredPosition = Vector2.zero;

        var img = grip.AddComponent<Image>();
        img.color = ResizeGripColor;

        // Tiny diagonal hint (just a label, easier than custom geometry)
        var hint = new GameObject("Hint");
        hint.transform.SetParent(grip.transform, false);
        var hintRt = hint.AddComponent<RectTransform>();
        hintRt.anchorMin = Vector2.zero;
        hintRt.anchorMax = Vector2.one;
        hintRt.offsetMin = Vector2.zero;
        hintRt.offsetMax = Vector2.zero;
        var hintTxt = hint.AddComponent<TextMeshProUGUI>();
        hintTxt.font = _font;
        hintTxt.text = "\u25E2"; // lower-right triangle
        hintTxt.fontSize = 14;
        hintTxt.color = Color.white;
        hintTxt.alignment = TextAlignmentOptions.Center;
        hintTxt.raycastTarget = false;

        var resize = grip.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT));
    }

    // ---------- Chat bubble construction ----------

    /// <summary>
    /// Append a chat bubble. If <paramref name="linkedInteraction"/> is non-null the
    /// bubble's text is editable; on every end-of-edit the new text (with TMP rich
    /// text tags stripped) is written back to the GTPChatLine so the next BuildPromptChat()
    /// call sends the user's edits to the LLM.
    /// </summary>
    private TMP_InputField AppendBubble(string roleLabel, Color labelColor, string rawMessageText, Color bubbleBg, GTPChatLine linkedInteraction = null)
    {
        // ---- Bubble: VerticalLayoutGroup + ContentSizeFitter so the bubble auto-grows
        // to fit its label + input field children, plus padding.
        var bubble = new GameObject("Bubble_" + roleLabel);
        bubble.transform.SetParent(_chatContent, false);
        var bubbleImg = bubble.AddComponent<Image>();
        bubbleImg.color = bubbleBg;

        var bubbleVLG = bubble.AddComponent<VerticalLayoutGroup>();
        bubbleVLG.padding = new RectOffset(8, 8, 4, 4);
        bubbleVLG.spacing = 1;
        bubbleVLG.childAlignment = TextAnchor.UpperLeft;
        bubbleVLG.childControlWidth = true;
        bubbleVLG.childControlHeight = true;
        bubbleVLG.childForceExpandWidth = true;
        bubbleVLG.childForceExpandHeight = false;

        var bubbleCSF = bubble.AddComponent<ContentSizeFitter>();
        bubbleCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bubbleCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- Label child: separate TMP_Text so the role label can never be clobbered
        // by the user editing the input field below.
        if (!string.IsNullOrEmpty(roleLabel))
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(bubble.transform, false);
            var labelLE = labelGo.AddComponent<LayoutElement>();
            labelLE.minHeight = 16f;
            labelLE.preferredHeight = 16f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = roleLabel;
            labelTmp.font = _font;
            labelTmp.fontSize = 12;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = labelColor;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.raycastTarget = false; // don't intercept clicks meant for the input field below
        }

        // ---- Input field: editable iff a linked interaction was provided.
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "Text";
        inputGo.transform.SetParent(bubble.transform, false);

        var inputImg = inputGo.GetComponent<Image>();
        if (inputImg != null) inputImg.color = new Color(0, 0, 0, 0);

        var inputLE = inputGo.AddComponent<LayoutElement>();
        inputLE.minHeight = 18f;
        inputLE.preferredHeight = 18f; // updated in coroutine

        var input = inputGo.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.readOnly = (linkedInteraction == null); // info bubbles + assistant bubble during streaming = readOnly
        input.interactable = true;
        input.textComponent.font = _font;
        input.textComponent.fontSize = BaseFontSize;
        input.textComponent.color = TextDark;
        input.textComponent.richText = true;
        input.textComponent.textWrappingMode = TextWrappingModes.Normal;
        input.textComponent.alignment = TextAlignmentOptions.TopLeft;

        if (input.placeholder is TextMeshProUGUI ph)
        {
            ph.text = "";
            ph.color = new Color(0, 0, 0, 0);
        }

        ApplyFatCaret(input);
        var bubbleCaretFixer = inputGo.AddComponent<AIChatCaretFixer>();
        bubbleCaretFixer.Set(input);

        // Body only - the role label is its own TMP_Text above this field.
        input.text = ConvertMarkdownToTMP(rawMessageText);

        if (linkedInteraction != null)
            HookEditingTo(input, linkedInteraction);

        // Re-measure on every text change (covers streaming, user typing, and re-format).
        input.onValueChanged.AddListener(_ => StartCoroutine(ResizeBubbleDeferred(input, inputLE)));
        StartCoroutine(ResizeBubbleDeferred(input, inputLE));
        StartCoroutine(ScrollToBottomDeferred());
        return input;
    }

    /// <summary>
    /// Make a bubble editable AFTER it has been created (used for assistant bubbles,
    /// which are created readOnly during streaming and switched to editable on completion).
    /// </summary>
    private void EnableBubbleEditing(TMP_InputField input, GTPChatLine interaction)
    {
        if (input == null || interaction == null) return;
        input.readOnly = false;
        HookEditingTo(input, interaction);
    }

    /// <summary>
    /// Wire input.onEndEdit -> strip TMP rich text tags + push cleaned text back into the
    /// GTPChatLine so future BuildPromptChat() calls send the user's edits to the LLM.
    /// We deliberately do NOT re-format the displayed text after edit so the user keeps
    /// seeing exactly what they typed (rich text tags or markdown either way).
    /// </summary>
    private static void HookEditingTo(TMP_InputField input, GTPChatLine interaction)
    {
        input.onEndEdit.AddListener(text =>
        {
            string clean = OpenAITextCompletionManager.RemoveTMPTagsFromString(text ?? "");
            interaction._content = clean;
        });
    }

    private IEnumerator ResizeBubbleDeferred(TMP_InputField input, LayoutElement inputLE)
    {
        // Two frames: frame 1 lets VerticalLayoutGroup width-stretch the input field;
        // frame 2 lets the textComponent's mesh + preferredHeight settle to the new width.
        yield return null;
        yield return null;

        if (input == null || input.textComponent == null || inputLE == null) yield break;

        // Determine the wrap width from the input field's current width. Fall back to a
        // panel-relative calculation if layout still hasn't resolved.
        var inputRT = input.GetComponent<RectTransform>();
        float wrapWidth = inputRT != null ? inputRT.rect.width : 0f;
        if (wrapWidth < 32f && _mainPanel != null)
        {
            // Main panel - scrollbar (22) - chatContent padding (16) - bubble padding (16).
            wrapWidth = Mathf.Max(64f, _mainPanel.rect.width - 22f - 32f);
        }

        // GetPreferredValues honors rich text + wrapping at an explicit width and returns
        // the tight bounding size we need for the LayoutElement.preferredHeight.
        Vector2 size = input.textComponent.GetPreferredValues(input.text, wrapWidth, 0f);
        inputLE.preferredHeight = Mathf.Max(18f, size.y + 4f); // +4 for descender slack

        // Force layout rebuild up the chain so the bubble's CSF + chatContent VLG pick up the change.
        var bubbleRT = inputLE.transform.parent as RectTransform;
        if (bubbleRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRT);
        if (_chatContent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_chatContent);
    }

    private void AddSystemMessage(string text)
    {
        // Info / system bubbles aren't part of the LLM conversation, so leave them readOnly
        // (linkedInteraction = null).
        AppendBubble("Info", new Color(0.35f, 0.35f, 0.45f), text, new Color(0.92f, 0.92f, 0.95f, 1f));
    }

    /// <summary>
    /// Append a "You:" bubble linked to a GTPChatLine so the user can edit what they
    /// said (e.g. to test how the AI responds to a hand-crafted history).
    /// </summary>
    private void AddUserMessage(string text, GTPChatLine linkedInteraction)
    {
        AppendBubble("You", new Color(0.05f, 0.30f, 0.65f), text, UserBubbleBg, linkedInteraction);
    }

    /// <summary>
    /// Append an empty "Assistant:" bubble. Created readOnly during streaming; the
    /// caller should call EnableBubbleEditing(...) on completion to link it to the
    /// just-added GTPChatLine and make it editable.
    /// </summary>
    private TMP_InputField AddAssistantBubble(string initialText)
    {
        var field = AppendBubble("Assistant", new Color(0.10f, 0.45f, 0.20f), initialText, AssistantBubbleBg);
        _streamingAssistantField = field;
        _streamingAssistantRT = field.GetComponent<RectTransform>();
        return field;
    }

    private IEnumerator ScrollToBottomDeferred()
    {
        // Layout updates one frame after we add the bubble; wait then scroll.
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_chatScroll != null)
            _chatScroll.verticalNormalizedPosition = 0f;
    }

    // ---------- Markdown -> TMP rich text (same approach as AdventureText) ----------

    private static string ConvertMarkdownToTMP(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            // Bold (must run before single * italic so ** isn't eaten by it)
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>", RegexOptions.Singleline);
            // Italic / single-asterisk emphasis -> bold (matches AdventureText behavior)
            text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>", RegexOptions.Singleline);
            // `inline code` -> monospaced color
            text = Regex.Replace(text, @"`([^`]+)`", "<mark=#00000020><font=\"LiberationSans SDF\"><color=#7A1F1F>$1</color></font></mark>", RegexOptions.Singleline);
            // Headings (#, ##, ###) at line start -> <size> + bold
            text = Regex.Replace(text, @"(?m)^###\s+(.+)$", "<size=110%><b>$1</b></size>");
            text = Regex.Replace(text, @"(?m)^##\s+(.+)$", "<size=120%><b>$1</b></size>");
            text = Regex.Replace(text, @"(?m)^#\s+(.+)$", "<size=130%><b>$1</b></size>");
            // Simple bullet lists: lines starting with "- " or "* " -> bullet char
            text = Regex.Replace(text, @"(?m)^\s*[-*]\s+(.+)$", "  \u2022 $1");
        }
        catch
        {
            // Malformed input - return raw text so we never crash the UI thread.
        }
        return text;
    }

    // ---------- Send / Stop / Clear ----------

    private void OnSendClicked()
    {
        if (_isStreaming) return;
        string text = _inputField != null ? _inputField.text : "";

        // Allow sending with images even if there's no text (vision models often work
        // better with a short prompt, but "describe this image" is a valid bare-image use).
        var attachmentBytes = _attachmentZone != null ? _attachmentZone.GetAttachmentBytes() : null;
        int attachedCount = attachmentBytes?.Count ?? 0;
        if (string.IsNullOrWhiteSpace(text) && attachedCount == 0) return;
        if (string.IsNullOrWhiteSpace(text) && attachedCount > 0)
            text = "(no caption)";

        // Stage attached images so GPTPromptManager auto-attaches them to the user line
        // we're about to add. AddInteraction consumes the pending list internally.
        if (attachedCount > 0)
        {
            foreach (var bytes in attachmentBytes)
            {
                if (bytes == null) continue;
                _promptManager.AddPendingImage(System.Convert.ToBase64String(bytes));
            }
            AddSystemMessage($"Attached {attachedCount} image{(attachedCount == 1 ? "" : "s")} to the next message.");
        }

        // Add the interaction first so we can link the bubble to it - that link is what
        // makes the bubble editable (and what makes user edits flow back into the prompt
        // history sent to the LLM on subsequent turns).
        _promptManager.AddInteraction("user", text);
        var userInteraction = _promptManager.GetLastInteraction();
        AddUserMessage(text, userInteraction);

        // Drop the staged thumbnails now that they've been baked into the conversation.
        if (attachedCount > 0)
            _attachmentZone?.ClearAttachments();

        _inputField.text = "";
        FocusInputDeferred();

        SendChatTurn();
    }

    private void OnStopClicked()
    {
        if (!_isStreaming) return;
        TryCancelActiveRequests();
        FinalizeAssistantTurn(aborted: true);
    }

    private void OnClearClicked()
    {
        if (_isStreaming)
        {
            TryCancelActiveRequests();
            FinalizeAssistantTurn(aborted: true);
        }

        _promptManager.Reset();
        _attachmentZone?.ClearAttachments();
        for (int i = _chatContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_chatContent.GetChild(i).gameObject);
        }
        AddSystemMessage("Conversation cleared.");
    }

    private void OnCopyClicked()
    {
        // Build a plain-text transcript from the prompt manager's interaction history.
        // (Info / system bubbles aren't part of _interactions, so they don't get copied -
        // which is what we want; they're UI-only annotations like "New chat".)
        var sb = new StringBuilder();
        var lines = _promptManager.BuildPromptChat();
        foreach (var line in lines)
        {
            if (line == null || string.IsNullOrEmpty(line._content)) continue;
            string roleDisplay = string.IsNullOrEmpty(line._role)
                ? "?"
                : char.ToUpper(line._role[0]) + line._role.Substring(1);
            sb.Append(roleDisplay).Append(": ").AppendLine(line._content);
            sb.AppendLine();
        }

        string transcript = sb.ToString().TrimEnd();
        if (string.IsNullOrEmpty(transcript))
        {
            RTQuickMessageManager.Get().ShowMessage("Chat is empty - nothing to copy");
            return;
        }

        GUIUtility.systemCopyBuffer = transcript;
        RTQuickMessageManager.Get().ShowMessage($"Copied chat ({transcript.Length} chars) to clipboard");
    }

    private void TryCancelActiveRequests()
    {
        if (_openAIMgr != null && _openAIMgr.IsRequestActive()) _openAIMgr.CancelCurrentRequest();
        if (_anthropicMgr != null && _anthropicMgr.IsRequestActive()) _anthropicMgr.CancelCurrentRequest();
        if (_texGenMgr != null && _texGenMgr.IsRequestActive()) _texGenMgr.CancelCurrentRequest();
        if (_geminiMgr != null && _geminiMgr.IsRequestActive()) _geminiMgr.CancelCurrentRequest();
    }

    // ---------- LLM provider routing (mirrors PicMain.call_llm) ----------

    private void SendChatTurn()
    {
        var settingsMgr = LLMSettingsManager.Get();
        if (settingsMgr == null)
        {
            AddSystemMessage("LLM settings are not initialized yet. Open LLM Settings and configure a provider first.");
            return;
        }

        var instanceMgr = LLMInstanceManager.Get();
        int llmReplicaIndex = 0;
        bool isVisionJob = _promptManager != null && _promptManager.HasAnyImages();
        int llmInstanceID = instanceMgr?.GetFreeLLM(isSmallJob: true, isVisionJob: isVisionJob, out llmReplicaIndex) ?? -1;

        if (llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
        {
            llmInstanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: true, isVisionJob: isVisionJob, out llmReplicaIndex);
        }

        LLMInstanceInfo llmInstance = llmInstanceID >= 0 ? instanceMgr?.GetInstance(llmInstanceID) : null;

        LLMProvider activeProvider;
        LLMProviderSettings activeSettings;
        if (llmInstance != null)
        {
            activeProvider = llmInstance.providerType;
            activeSettings = llmInstance.settings;
            _activeLLMInstanceID = llmInstanceID;
            _activeLLMReplicaIndex = llmReplicaIndex;
            instanceMgr.SetLLMBusy(llmInstanceID, llmReplicaIndex, true);
        }
        else
        {
            activeProvider = settingsMgr.GetActiveProvider();
            activeSettings = settingsMgr.GetProviderSettings(activeProvider);
            _activeLLMInstanceID = -1;
            _activeLLMReplicaIndex = 0;
        }

        if (activeSettings == null)
        {
            AddSystemMessage("No LLM provider settings found. Configure one via LLM Settings.");
            ReleaseActiveLLM();
            return;
        }

        _activeProviderInFlight = activeProvider;
        _isStreaming = true;
        _streamBuffer.Clear();
        _streamLastUpdate = 0;
        SetBusyUI(true, $"Contacting {activeProvider}...");

        AddAssistantBubble("");

        var lines = _promptManager.BuildPromptChat();
        // Strip TMP markup from any prior assistant bubbles before sending (safety - the
        // GPTPromptManager only ever stores raw text we put in, but be defensive).
        lines = OpenAITextCompletionManager.RemoveTMPTags(lines);

        float temperature = 0.7f;
        var advLogic = AdventureLogic.Get();
        if (advLogic != null && advLogic.GetExtractor() != null)
            temperature = advLogic.GetExtractor().Temperature;

        // If the user attached images but the resolved provider's chat path doesn't yet
        // serialize them, surface a clear note so they don't think the model "ignored" the
        // image. The Chat Completions branches (OpenAI / OpenAICompatible / Ollama / LlamaCpp)
        // all emit multimodal content arrays today; the others don't.
        if (isVisionJob && WillProviderDropImages(activeProvider, activeSettings))
        {
            AddSystemMessage($"Note: {activeProvider} chat path is not configured to send images yet; only text will be sent.");
        }

        RTDB db = new RTDB();

        switch (activeProvider)
        {
            case LLMProvider.OpenAI:
            {
                string apiKey = activeSettings.apiKey;
                string model = string.IsNullOrEmpty(activeSettings.selectedModel) ? "gpt-4o-mini" : activeSettings.selectedModel;
                string endpoint = "https://api.openai.com/v1/chat/completions";

                bool useResponsesAPI = false;
                bool isReasoningModel = false;
                bool includeTemperature = true;
                string reasoningEffort = null;

                if (model.Contains("gpt-5"))
                {
                    useResponsesAPI = true;
                    endpoint = "https://api.openai.com/v1/responses";
                    if (model.Contains("gpt-5.2-pro"))
                    {
                        isReasoningModel = true; includeTemperature = false; reasoningEffort = "high";
                    }
                    else if (model.Contains("gpt-5.2"))
                    {
                        isReasoningModel = true; includeTemperature = false; reasoningEffort = "medium";
                    }
                    else if (model.Contains("gpt-5-mini") || model.Contains("gpt-5-nano"))
                    {
                        useResponsesAPI = false; includeTemperature = false;
                        endpoint = "https://api.openai.com/v1/chat/completions";
                    }
                }
                else if (model.StartsWith("o3") || model.StartsWith("o4"))
                {
                    useResponsesAPI = true;
                    endpoint = "https://api.openai.com/v1/responses";
                    isReasoningModel = true; includeTemperature = false; reasoningEffort = "medium";
                }
                else if (model.StartsWith("o1"))
                {
                    isReasoningModel = true; includeTemperature = false;
                }

                if (!HasUserMessage(lines))
                    lines.Enqueue(new GTPChatLine("user", "Please proceed."));

                bool? openAIEnableThinking = null;
                string settingsEndpoint = activeSettings.endpoint ?? "";
                if (!string.IsNullOrEmpty(settingsEndpoint) && !settingsEndpoint.Contains("api.openai.com"))
                {
                    openAIEnableThinking = activeSettings.enableThinking;
                    string customEndpoint = LLMInstanceManager.ApplyReplicaPortOffset(settingsEndpoint, llmReplicaIndex);
                    endpoint = customEndpoint.TrimEnd('/');
                    if (!endpoint.EndsWith("/v1/chat/completions"))
                        endpoint += "/v1/chat/completions";
                }

                string json = _openAIMgr.BuildChatCompleteJSON(lines, 4096, temperature, model, true,
                    useResponsesAPI, isReasoningModel, includeTemperature, reasoningEffort, openAIEnableThinking);
                _openAIMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            case LLMProvider.Anthropic:
            {
                string apiKey = activeSettings.apiKey;
                string model = activeSettings.selectedModel;
                string endpoint = activeSettings.endpoint;
                if (string.IsNullOrEmpty(apiKey)) apiKey = Config.Get().GetAnthropicAI_APIKey();
                if (string.IsNullOrEmpty(model)) model = Config.Get().GetAnthropicAI_APIModel();
                if (string.IsNullOrEmpty(endpoint)) endpoint = Config.Get().GetAnthropicAI_APIEndpoint();

                string json = _anthropicMgr.BuildChatCompleteJSON(lines, 4096, temperature, model, true);
                _anthropicMgr.SpawnChatCompletionRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            case LLMProvider.LlamaCpp:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                var llmParms = llmInstance != null ? settingsMgr.GetInstanceLLMParms(llmInstanceID) : settingsMgr.GetLLMParms(LLMProvider.LlamaCpp);
                string suggestedEndpoint;
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, 4096, temperature,
                    Config.Get().GetGenericLLMMode(), true, llmParms, false, true);
                _texGenMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey);
                break;
            }

            case LLMProvider.Ollama:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                var llmParms = llmInstance != null ? settingsMgr.GetInstanceLLMParms(llmInstanceID) : settingsMgr.GetLLMParms(LLMProvider.Ollama);
                string suggestedEndpoint;
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, 4096, temperature,
                    Config.Get().GetGenericLLMMode(), true, llmParms, true, false);
                _texGenMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey);
                break;
            }

            case LLMProvider.Gemini:
            {
                string apiKey = activeSettings.apiKey;
                string model = string.IsNullOrEmpty(activeSettings.selectedModel) ? "gemini-2.5-pro" : activeSettings.selectedModel;
                string baseEndpoint = string.IsNullOrEmpty(activeSettings.endpoint)
                    ? "https://generativelanguage.googleapis.com/v1beta/models" : activeSettings.endpoint;
                bool enableThinking = activeSettings.enableThinking;
                string endpoint = GeminiTextCompletionManager.BuildEndpointUrl(baseEndpoint, model, true);

                if (!HasUserMessage(lines))
                    lines.Enqueue(new GTPChatLine("user", "Please proceed."));

                string json = _geminiMgr.BuildChatCompleteJSON(lines, 4096, temperature, model, true, enableThinking);
                _geminiMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            case LLMProvider.OpenAICompatible:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                string model = activeSettings.selectedModel ?? "";
                string endpoint = serverAddress.TrimEnd('/') + "/v1/chat/completions";

                var normalizedLines = OpenAITextCompletionManager.NormalizeForStrictAlternation(lines);
                bool? compatEnableThinking = activeSettings.enableThinking;
                string json = _openAIMgr.BuildChatCompleteJSON(normalizedLines, 4096, temperature, model, true,
                    enableThinking: compatEnableThinking);
                _openAIMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            default:
                AddSystemMessage("Unsupported provider: " + activeProvider);
                FinalizeAssistantTurn(aborted: true);
                return;
        }
    }

    private static bool HasUserMessage(Queue<GTPChatLine> lines)
    {
        foreach (var line in lines)
        {
            if (line._role == "user") return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the given provider/settings combo will hit a code path whose JSON
    /// builder doesn't currently serialize attached images. Used purely to surface a
    /// "your image won't be sent" warning - no side effects.
    /// </summary>
    private static bool WillProviderDropImages(LLMProvider provider, LLMProviderSettings settings)
    {
        switch (provider)
        {
            case LLMProvider.Anthropic:
            case LLMProvider.Gemini:
                return true;

            case LLMProvider.OpenAI:
            {
                // Mirror the useResponsesAPI logic in the OpenAI branch of SendChatTurn.
                // The Responses-API branch of OpenAITextCompletionManager.BuildChatCompleteJSON
                // does not emit multimodal content arrays today; only the Chat Completions
                // branch does.
                string model = settings != null ? (settings.selectedModel ?? "") : "";
                bool useResponses = false;
                if (model.Contains("gpt-5"))
                {
                    useResponses = true;
                    if (model.Contains("gpt-5-mini") || model.Contains("gpt-5-nano"))
                        useResponses = false;
                }
                else if (model.StartsWith("o3") || model.StartsWith("o4"))
                {
                    useResponses = true;
                }
                return useResponses;
            }

            default:
                // OpenAICompatible / Ollama / LlamaCpp all flow through builders that
                // serialize images today.
                return false;
        }
    }

    // ---------- Callbacks ----------

    private void OnStreamingTextCallback(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _streamBuffer.Append(text);

        if (Time.unscaledTime - _streamLastUpdate < STREAM_UPDATE_INTERVAL) return;
        _streamLastUpdate = Time.unscaledTime;

        UpdateStreamingBubble();
    }

    private void UpdateStreamingBubble()
    {
        if (_streamingAssistantField == null) return;
        // Body only - the "Assistant" label is its own TMP_Text above the input field.
        _streamingAssistantField.text = ConvertMarkdownToTMP(_streamBuffer.ToString());
        if (_chatScroll != null && _chatScroll.verticalNormalizedPosition < 0.05f)
            StartCoroutine(ScrollToBottomDeferred());
    }

    private void OnLLMCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        if (jsonNode == null && (string.IsNullOrEmpty(streamedText) || streamedText.Length == 0))
        {
            string error = db != null ? db.GetStringWithDefault("msg", "Unknown error") : "Unknown error";
            AddSystemMessage("LLM error: " + error);
            FinalizeAssistantTurn(aborted: true);
            return;
        }

        if (jsonNode != null && string.IsNullOrEmpty(streamedText))
        {
            // Non-streaming reply (rare) - try to extract content for OpenAI-shaped responses.
            try { streamedText = jsonNode["choices"][0]["message"]["content"]; }
            catch { /* leave streamedText empty */ }
        }

        if (GenerateSettingsPanel.GetStripThinkTags())
            streamedText = OpenAITextCompletionManager.RemoveThinkTagsFromString(streamedText ?? "");

        streamedText = (streamedText ?? "").Trim();

        // Final visual update (body only, the "Assistant" label is a separate TMP_Text)
        var completedField = _streamingAssistantField;
        if (completedField != null)
            completedField.text = ConvertMarkdownToTMP(streamedText);

        _promptManager.AddInteraction("assistant", streamedText);

        // Now that we have an interaction to link the bubble to, switch the assistant
        // bubble from readOnly to editable so the user can hand-tweak the assistant's
        // reply for testing follow-up turns.
        EnableBubbleEditing(completedField, _promptManager.GetLastInteraction());

        FinalizeAssistantTurn(aborted: false);
    }

    private void FinalizeAssistantTurn(bool aborted)
    {
        _isStreaming = false;
        _streamingAssistantField = null;
        _streamingAssistantRT = null;
        ReleaseActiveLLM();
        SetBusyUI(false, aborted ? "Stopped" : "Idle");
        StartCoroutine(ScrollToBottomDeferred());
        // Re-focus the chat input so the user can immediately type their next message
        // (unless they're in the middle of editing some other input - e.g. a bubble).
        FocusInputDeferred();
    }

    private void ReleaseActiveLLM()
    {
        if (_activeLLMInstanceID >= 0)
        {
            var instanceMgr = LLMInstanceManager.Get();
            if (instanceMgr != null)
                instanceMgr.SetLLMBusy(_activeLLMInstanceID, _activeLLMReplicaIndex, false);
        }
        _activeLLMInstanceID = -1;
        _activeLLMReplicaIndex = 0;
    }

    // ---------- Misc ----------

    private void SetBusyUI(bool busy, string status)
    {
        if (_sendButton != null) _sendButton.interactable = !busy;
        // Keep the input field interactable while the LLM is streaming so the user (a)
        // doesn't lose focus / their composed-but-not-sent text and (b) can compose the
        // next message while reading the in-progress reply. The _isStreaming guard in
        // OnSendClicked still prevents double-send.
        if (_inputField != null) _inputField.interactable = true;
        if (_clearButton != null) _clearButton.interactable = true;
        if (_stopButton != null) _stopButton.interactable = busy;
        if (_statusText != null) _statusText.text = status;
    }

    private void RefreshHeaderTitle()
    {
        if (_titleText == null) return;

        string label = "AI Chat";
        var settingsMgr = LLMSettingsManager.Get();
        var instanceMgr = LLMInstanceManager.Get();

        try
        {
            if (instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
            {
                var instance = instanceMgr.GetDefaultInstance();
                if (instance != null)
                {
                    string model = instance.settings?.selectedModel;
                    label = string.IsNullOrEmpty(model)
                        ? $"AI Chat \u2014 {instance.providerType}"
                        : $"AI Chat \u2014 {instance.providerType} ({model})";
                }
            }
            else if (settingsMgr != null)
            {
                var p = settingsMgr.GetActiveProvider();
                var s = settingsMgr.GetProviderSettings(p);
                string model = s?.selectedModel;
                label = string.IsNullOrEmpty(model)
                    ? $"AI Chat \u2014 {p}"
                    : $"AI Chat \u2014 {p} ({model})";
            }
        }
        catch
        {
            // Fallback - keep the simple "AI Chat" label.
        }

        _titleText.text = label;
    }

    /// <summary>
    /// Force a thick, high-contrast caret on a TMP_InputField. Overrides the
    /// thinner cached defaults that ConfigureInputFieldVisuals applies.
    /// </summary>
    private static void ApplyFatCaret(TMP_InputField input)
    {
        if (input == null) return;
        input.customCaretColor = true;
        input.caretColor = new Color(0f, 0f, 0f, 1f);
        input.caretWidth = 4;
        input.caretBlinkRate = 0.6f;
        input.selectionColor = new Color(0.25f, 0.5f, 1f, 0.45f);
    }

    private void FocusInputDeferred()
    {
        if (!gameObject.activeInHierarchy) return;
        StartCoroutine(FocusInputCoroutine());
    }

    private IEnumerator FocusInputCoroutine()
    {
        yield return null;
        if (_inputField == null || !_inputField.interactable) yield break;

        // Don't steal focus if the user is currently editing some other input field
        // (e.g. they clicked into a previous bubble to tweak it).
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null
            && es.currentSelectedGameObject != _inputField.gameObject)
        {
            var otherInput = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
            if (otherInput != null && otherInput.isFocused)
                yield break;
        }

        _inputField.ActivateInputField();
        _inputField.Select();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !_isStreaming)
            Hide();

        // Enter / Shift+Enter handling for the chat input. Done here (not via TMP_InputField's
        // own MultiLineSubmit mode or onValidateInput) because both of those are unreliable
        // about reading the Shift modifier in Unity 6 / TMP 3.
        if (_inputField != null && _inputField.isFocused
            && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shift)
            {
                // Plain Enter: lineType=MultiLineNewline already inserted a '\n' into the
                // text this same frame - strip it before sending.
                if (_inputField.text.EndsWith("\n"))
                    _inputField.text = _inputField.text.Substring(0, _inputField.text.Length - 1);
                OnSendClicked();
            }
            else
            {
                // Shift+Enter: in Unity 6 / TMP 3, Shift+Enter does NOT insert a newline
                // (TMP's character event for Shift+Enter doesn't carry '\n'). Insert it
                // ourselves at the current caret position (replacing any selected range).
                InsertCharAtCaret(_inputField, '\n');
            }
        }

        // Throttled streaming UI flush so a long pause between chunks still updates the bubble.
        if (_isStreaming && _streamingAssistantField != null
            && Time.unscaledTime - _streamLastUpdate >= STREAM_UPDATE_INTERVAL && _streamBuffer.Length > 0)
        {
            _streamLastUpdate = Time.unscaledTime;
            UpdateStreamingBubble();
        }
    }

    /// <summary>
    /// Insert a single character at the caret position (or replacing the current selection)
    /// of a TMP_InputField, then position the caret right after the inserted character.
    /// </summary>
    private static void InsertCharAtCaret(TMP_InputField field, char c)
    {
        if (field == null) return;
        string current = field.text ?? "";

        // selectionAnchorPosition = start of mouse-drag, selectionFocusPosition = end of drag.
        // When no selection, both equal caretPosition.
        int selStart = Mathf.Min(field.selectionAnchorPosition, field.selectionFocusPosition);
        int selEnd = Mathf.Max(field.selectionAnchorPosition, field.selectionFocusPosition);
        selStart = Mathf.Clamp(selStart, 0, current.Length);
        selEnd = Mathf.Clamp(selEnd, 0, current.Length);

        string newText = current.Substring(0, selStart) + c + current.Substring(selEnd);
        field.text = newText;
        // Move caret to right after the inserted character.
        field.caretPosition = selStart + 1;
        field.stringPosition = selStart + 1;
        field.selectionAnchorPosition = selStart + 1;
        field.selectionFocusPosition = selStart + 1;
    }
}

/// <summary>
/// Bottom-right corner resize handle for a panel. Drags adjust the target's sizeDelta.
/// Min size enforced. Pivot/anchors should be center-center on the target so resizing
/// grows symmetrically; for AIChatPanel we use that pivot, and explicitly compensate for
/// the cursor drift by tracking the initial offset.
/// </summary>
public class PanelResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _target;
    private Vector2 _minSize = new Vector2(200, 200);
    private Vector2 _startPointerLocal;
    private Vector2 _startSize;

    public void SetTarget(RectTransform target, Vector2 minSize)
    {
        _target = target;
        _minSize = minSize;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_target == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out _startPointerLocal);
        _startSize = _target.sizeDelta;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 nowLocal))
            return;

        // Movement in parent-local coords. Right-grow = positive X; down-grow = negative Y.
        Vector2 delta = nowLocal - _startPointerLocal;
        Vector2 newSize = _startSize + new Vector2(delta.x, -delta.y);
        newSize.x = Mathf.Max(_minSize.x, newSize.x);
        newSize.y = Mathf.Max(_minSize.y, newSize.y);
        _target.sizeDelta = newSize;
    }
}

/// <summary>
/// Bullet-proof caret/selection visibility for TMP_InputField. TMP in Unity 6 has a
/// known quirk where the caret and selection-highlight mesh aren't rendered the first
/// time the field is selected unless the field is force-reinitialized (toggle enabled +
/// ForceLabelUpdate + Canvas.ForceUpdateCanvases), and the spawned TMP_SelectionCaret
/// child needs its color set directly. This component does all of that and also
/// re-asserts visuals periodically while the field is focused.
/// </summary>
public class AIChatCaretFixer : MonoBehaviour, ISelectHandler
{
    private TMP_InputField _input;
    private bool _toggledOnce = false;
    private float _lastReassertTime = 0f;

    private static readonly Color CaretColor = new Color(0f, 0f, 0f, 1f);
    private static readonly Color SelectionColor = new Color(0.25f, 0.5f, 1f, 0.55f);
    private const int CaretWidth = 4;

    public void Set(TMP_InputField input)
    {
        _input = input;
    }

    private void OnEnable()
    {
        Apply();
        StartCoroutine(InitSequence());
    }

    public void OnSelect(BaseEventData eventData)
    {
        Apply();
        StartCoroutine(InitSequence());

        // Make sure TMP enters editing state immediately on first click.
        if (_input != null)
            _input.ActivateInputField();
    }

    private System.Collections.IEnumerator InitSequence()
    {
        // Frame 1: TMP will create its caret/selection graphics if they don't exist.
        yield return null;

        // The toggle-enabled trick fixes the "first open after app start = invisible
        // caret/selection" TMP issue. Only do it once per fixer.
        if (!_toggledOnce && _input != null)
        {
            _toggledOnce = true;
            bool wasEnabled = _input.enabled;
            _input.enabled = false;
            _input.enabled = true;
            if (!wasEnabled) _input.enabled = false;
        }

        Apply();
        ApplyToCaretChildren();
        if (_input != null) _input.ForceLabelUpdate();
        Canvas.ForceUpdateCanvases();

        // Frame 2 + 0.05s: TMP can spawn caret graphics late; reassert.
        yield return new WaitForSecondsRealtime(0.05f);
        Apply();
        ApplyToCaretChildren();
    }

    private void Update()
    {
        if (_input == null || !_input.isFocused) return;
        // Cheap reassertion so the caret/selection stay visible even if some other code
        // touches them. Runs only while the field is focused.
        if (Time.unscaledTime - _lastReassertTime < 0.5f) return;
        _lastReassertTime = Time.unscaledTime;
        Apply();
        ApplyToCaretChildren();
    }

    private void Apply()
    {
        if (_input == null) return;
        _input.customCaretColor = true;
        _input.caretColor = CaretColor;
        _input.caretWidth = CaretWidth;
        _input.caretBlinkRate = 0.6f;
        _input.selectionColor = SelectionColor;
    }

    private void ApplyToCaretChildren()
    {
        if (_input == null) return;
        // Tint any TMP_SelectionCaret graphic the input field has spawned. Without this,
        // the caret graphic uses the textComponent's color or stays at its default (often
        // invisible). Also force a redraw.
        foreach (var caret in _input.GetComponentsInChildren<TMP_SelectionCaret>(true))
        {
            caret.color = CaretColor;
            caret.maskable = true;
            caret.SetAllDirty();
        }
    }
}
