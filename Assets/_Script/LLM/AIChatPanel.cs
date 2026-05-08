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
using AITools.AIChat.Context;
using AITools.AIChat.Mirroring;
using AITools.AIChat.Skills;
using AITools.AIChat.UI;

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
public class AIChatPanel : MonoBehaviour, IChatHost
{
    private static AIChatPanel _instance;
    private static GameObject _panelRoot;

    // ---- Skills system: dynamic system prompt + LLM-callable actions ----
    // Created in CreateUI(); torn down with the panel. SkillManager loads aichat/skills/*.md
    // and aichat/main_prompt.txt; ChatContextBuilder rebuilds the system prompt every
    // turn (so GPU/LLM state is always fresh); SkillActionParser extracts <aitools_action>
    // tags from the LLM's stream; SkillActionExecutor dispatches them to the rest of the
    // app (PicMain.RunPresetByName, LLM delegation, etc.).
    private SkillManager _skillManager;
    private ChatContextBuilder _contextBuilder;
    private SkillActionParser _actionParser;
    private SkillActionExecutor _actionExecutor;

    // Header status pill - GPU busy count + LLM count, refreshed periodically.
    private TextMeshProUGUI _statusPillText;
    private float _statusPillNextRefresh;
    private const float STATUS_PILL_REFRESH_INTERVAL = 1.5f;

    // Per-turn attachments: a defensive copy of the user's pasted images at OnSendClicked
    // time, so a SkillActionExecutor invoked mid-stream can still resolve attachment="N"
    // even after ChatImageAttachmentZone has cleared its own thumbnail strip.
    private List<byte[]> _lastTurnAttachments = new List<byte[]>();

    // Per-Pic label TMP + the base "Image #N (...)" text it was created with, so a
    // caption arriving asynchronously can append " - <caption>" to the existing
    // label without disturbing the index/source prefix. Stale entries (Pic destroyed)
    // are tolerated - we null-check before writing.
    private readonly Dictionary<PicMain, (TextMeshProUGUI label, string baseText)> _captionLabels = new Dictionary<PicMain, (TextMeshProUGUI, string)>();

    // Stable per-session list of chat-image bubbles (1-based via index+1). Lets the LLM
    // reference "the image you generated in turn 3" via chat_image="3". Only cleared on
    // OnClearClicked; persists across turns. Entries can become stale if the user deletes
    // the world Pic - we just return null on read in that case.
    private readonly List<PicMain> _chatImagePics = new List<PicMain>();

    // Most-recent Pic spawned by a non-chained skill action in the current user turn.
    // Reset on each OnSendClicked() so chain="true" can never reach back into a prior
    // turn's Pic. Chained actions read this to find their stack target; they do NOT
    // overwrite it (a 3-step chain stays anchored to the original Pic).
    private PicMain _lastSpawnedPicThisTurn;

    // LIFO stack of non-chained Pics spawned this turn that have NOT yet been
    // consumed by a chain="true" follow-up. Each chain pops the MOST-RECENT
    // unmatched Pic (adjacency rule: "the image you just made"), so a reply mixing
    // standalone Pics with paired stacks - e.g. gen, mov, gen, gen, mov - animates
    // the Pic the LLM just emitted rather than the oldest unmatched. When the stack
    // is empty, chained follow-ups fall back to _lastSpawnedPicThisTurn (so 3+ step
    // chains on the same root Pic still work). Stored as a List with end-pop because
    // we need to skip dead Pics during pop without losing position.
    private readonly List<PicMain> _unchainedPicsThisTurn = new List<PicMain>();

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;

    // Header
    private TextMeshProUGUI _titleText;

    // Chat content (right side of the body split = text bubbles only)
    private ScrollRect _chatScroll;
    private RectTransform _chatContent;

    // Body / split layout. Body sits between header and footer; inside it we place
    // a Media panel on the left, a draggable Splitter, and the Chat text panel on
    // the right. The split is in absolute pixels (anchored from the body's left
    // edge), so growing the panel grows the chat side and leaves the media at its
    // last user-set width.
    private RectTransform _bodyRT;
    private RectTransform _mediaPanelRT;
    private RectTransform _chatPanelRT;
    private RectTransform _splitterRT;
    private ScrollRect _mediaScroll;
    private RectTransform _mediaContent;
    private TextMeshProUGUI _mediaHeaderText;
    private float _splitX = DEFAULT_SPLIT_X;       // X (in pixels from body left) of the splitter centre
    private const float DEFAULT_SPLIT_X = 320f;
    private const float SPLITTER_WIDTH = 12f;
    private const float MIN_MEDIA_WIDTH = 140f;
    private const float MIN_CHAT_WIDTH = 240f;
    private const float MEDIA_HEADER_HEIGHT = 26f;
    private const string PREFS_KEEP_LAST_N_MEDIA = "aichat_keep_last_n_media";
    private const int DEFAULT_KEEP_LAST_N_MEDIA = 10;
    // Mirror of SkillManager.PresetPrefixPrefsKey - kept here for the static
    // get/set helpers next to GetKeepLastNMedia. Both must stay in sync.
    private const string PREFS_PRESET_PREFIX = "aichat_preset_prefix";
    private const string DEFAULT_PRESET_PREFIX = "";

    // Footer
    private TMP_InputField _inputField;
    private TMPInputFieldUndo _inputUndo;
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

    // Streaming status counters - reset on each user send. Total chars received
    // (proxy for tokens via /4), wall-clock start, and the next time we should
    // refresh the status text. Display is throttled to STATUS_PILL_INTERVAL so
    // we don't thrash _statusText every chunk.
    private int _streamCharsReceived = 0;
    private float _streamStartTime = 0f;
    private float _streamStatusNextRefresh = 0f;
    private int _streamSpinnerStep = 0;
    private const float STREAM_STATUS_INTERVAL = 0.15f;
    // Plain ASCII spinner - the chat font (LiberationSans SDF) doesn't ship the
    // Braille / block glyphs that look nicer, and they render as missing-glyph
    // squares. |/-\ is universal.
    private static readonly char[] StreamSpinnerFrames = { '|', '/', '-', '\\' };
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
    private const float FOOTER_DRAG_BAR_HEIGHT = 10f;
    private const float MOVE_FRAME_THICKNESS = 18f;
    private const float RESIZE_EDGE_THICKNESS = 10f;
    private const float RESIZE_CORNER_SIZE = 16f;
    private const int AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP = 0;
    private const int AI_CHAT_GEMINI_MAX_OUTPUT_TOKENS = 65536;
    private const int AI_CHAT_LEGACY_MAX_OUTPUT_TOKENS = 8192;
    private const int AI_CHAT_ANTHROPIC_DEFAULT_MAX_OUTPUT_TOKENS = 64000;
    private const int AI_CHAT_ANTHROPIC_OPUS_47_MAX_OUTPUT_TOKENS = 128000;
    private const float SCROLL_BOTTOM_PIXEL_EPSILON = 12f;
    private const float BaseFontSize = 14f;
    private const float BaseLabelFontSize = 12f;

    // Ctrl+MouseWheel font resize. Multiplier scales BaseFontSize (and the smaller
    // role label font) so the user can read the chat at any size they like. Reset
    // each session because Show() lazy-creates a fresh panel.
    private float _fontSizeMultiplier = 1.0f;
    private const float MinFontMultiplier = 0.5f;
    private const float MaxFontMultiplier = 3.0f;
    private const float FontMultiplierStep = 0.1f;

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

    // Tracks the visibility state independently of _panelRoot.SetActive, because we
    // intentionally keep _panelRoot active even when "hidden" so that coroutines on
    // its components (most importantly the LLM completion managers' streaming
    // requests) can run to completion. Deactivating _panelRoot would kill those
    // coroutines mid-stream and the chat UI would be stuck thinking the LLM is
    // still talking forever.
    public static bool IsVisible => _panelRoot != null && _instance != null && _instance._isVisible;
    private bool _isVisible = true;

    public static void Show()
    {
        if (_instance != null)
        {
            _instance.SetVisible(true);
            _instance.RefreshHeaderTitle();
            // Reload aichat config in case the user edited a skill or main_prompt.txt
            // outside the app between toggles. Cheap.
            _instance._skillManager?.Reload();
            _instance.UpdateStatusPill();
            _instance.ClampPanelToScreen();
            _instance.FocusInputDeferred();
            return;
        }

        _panelRoot = new GameObject("AIChatPanel");
        _instance = _panelRoot.AddComponent<AIChatPanel>();
        _instance.CreateUI();
    }

    public static void Hide()
    {
        if (_instance != null)
            _instance.SetVisible(false);
    }

    public static void Toggle()
    {
        if (_instance != null && _instance._isVisible)
            Hide();
        else
            Show();
    }

    /// <summary>
    /// Hide/show the visible chat UI without deactivating _panelRoot. Closing the
    /// panel must NOT stop the LLM streaming coroutines that live on _panelRoot's
    /// components (OpenAITextCompletionManager etc. were added there); otherwise
    /// the in-flight reply never finalizes and the chat is stuck "talking" until
    /// Stop is clicked. Just deactivate the visible UI children instead.
    /// </summary>
    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_mainPanel != null)
            _mainPanel.gameObject.SetActive(visible);
        if (_captionTooltipRoot != null && !visible)
            _captionTooltipRoot.SetActive(false);
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

        // Skills system. Loads aichat/main_prompt.txt and aichat/skills/*.md, wires up
        // the parser->executor pipeline. Parser fires per parsed tag; executor reaches
        // back into the panel via the IChatHost interface to spawn pics, inject system
        // messages, etc.
        _skillManager = new SkillManager();
        _skillManager.Reload();
        _contextBuilder = new ChatContextBuilder(_skillManager);
        _actionParser = new SkillActionParser();
        _actionExecutor = new SkillActionExecutor(_skillManager, this);
        _actionParser.OnActionParsed += OnSkillActionParsed;

        RefreshHeaderTitle();
        UpdateStatusPill();
        int loadedSkills = _skillManager.GetSkills().Count;
        AddSystemMessage($"New chat. {loadedSkills} skill{(loadedSkills == 1 ? "" : "s")} loaded from aichat/skills/. Conversation history is kept until you click Clear or close the app.");

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

        // Reuse the same drag handler the LLMSettingsPanel uses. Pass our actual header
        // height so the clamp code keeps the full grab-strip on-screen instead of the
        // default 32px assumption.
        header.AddComponent<PanelDragHandler>().SetTarget(_mainPanel, HEADER_HEIGHT);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        // Leave space on right for: status pill (~140), Settings button (~80),
        // Close button (~30) + a couple of 6px gaps. ~270 total.
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-270, 0);

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "AI Chat";
        _titleText.font = _font;
        _titleText.fontSize = 18;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = TextTitle;
        _titleText.alignment = TextAlignmentOptions.MidlineLeft;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;

        // Status pill: shows "GPUs: 1/2 busy   LLMs: 3" so the user can see at a glance
        // what the LLM is "told" about the rest of the system. Refreshed every 1.5s in
        // Update() while the panel is visible.
        var pillObj = new GameObject("StatusPill");
        pillObj.transform.SetParent(header.transform, false);
        var pillRt = pillObj.AddComponent<RectTransform>();
        pillRt.anchorMin = new Vector2(1, 0.5f);
        pillRt.anchorMax = new Vector2(1, 0.5f);
        pillRt.pivot = new Vector2(1, 0.5f);
        pillRt.sizeDelta = new Vector2(150, 22);
        // Sits to the LEFT of the Settings button (which is at -114) and the close
        // button (at -6, 30 wide). Gap of 6px from Settings.
        pillRt.anchoredPosition = new Vector2(-200, 0);
        var pillBg = pillObj.AddComponent<Image>();
        pillBg.color = new Color(0.92f, 0.92f, 0.95f, 1f);

        var pillTxtObj = new GameObject("Text");
        pillTxtObj.transform.SetParent(pillObj.transform, false);
        var pillTxtRt = pillTxtObj.AddComponent<RectTransform>();
        pillTxtRt.anchorMin = Vector2.zero;
        pillTxtRt.anchorMax = Vector2.one;
        pillTxtRt.offsetMin = new Vector2(6, 0);
        pillTxtRt.offsetMax = new Vector2(-6, 0);
        _statusPillText = pillTxtObj.AddComponent<TextMeshProUGUI>();
        _statusPillText.text = "GPUs: ?  LLMs: ?";
        _statusPillText.font = _font;
        _statusPillText.fontSize = 12;
        _statusPillText.color = new Color(0.18f, 0.18f, 0.22f, 1f);
        _statusPillText.alignment = TextAlignmentOptions.Center;
        _statusPillText.raycastTarget = false;

        // Settings button - opens AIChatSettingsPanel for editing main_prompt.txt and
        // browsing loaded skills.
        var settingsBtnObj = new GameObject("Settings");
        settingsBtnObj.transform.SetParent(header.transform, false);
        var settingsRt = settingsBtnObj.AddComponent<RectTransform>();
        settingsRt.anchorMin = new Vector2(1, 0.5f);
        settingsRt.anchorMax = new Vector2(1, 0.5f);
        settingsRt.pivot = new Vector2(1, 0.5f);
        settingsRt.sizeDelta = new Vector2(80, 24);
        settingsRt.anchoredPosition = new Vector2(-44, 0);
        var settingsImg = settingsBtnObj.AddComponent<Image>();
        settingsImg.color = Color.white;
        var settingsBtn = settingsBtnObj.AddComponent<Button>();
        settingsBtn.targetGraphic = settingsImg;
        settingsBtn.onClick.AddListener(OnSettingsClicked);

        var settingsTxtObj = new GameObject("Text");
        settingsTxtObj.transform.SetParent(settingsBtnObj.transform, false);
        var settingsTxtRt = settingsTxtObj.AddComponent<RectTransform>();
        settingsTxtRt.anchorMin = Vector2.zero;
        settingsTxtRt.anchorMax = Vector2.one;
        settingsTxtRt.offsetMin = Vector2.zero;
        settingsTxtRt.offsetMax = Vector2.zero;
        var settingsTxt = settingsTxtObj.AddComponent<TextMeshProUGUI>();
        settingsTxt.text = "Settings";
        settingsTxt.font = _font;
        settingsTxt.fontSize = 13;
        settingsTxt.fontStyle = FontStyles.Bold;
        settingsTxt.color = TextTitle;
        settingsTxt.alignment = TextAlignmentOptions.Center;
        settingsTxt.raycastTarget = false;

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

    /// <summary>
    /// Builds the body region split into [MediaPanel | Splitter | ChatPanel]. The
    /// media panel hosts image/movie bubbles (newest at the bottom); the chat panel
    /// hosts text bubbles only. The splitter is draggable; its X position is in
    /// absolute pixels from the body's left edge so the chat side absorbs growth
    /// when the user enlarges the whole panel.
    /// </summary>
    private void CreateChatArea()
    {
        // Outer body container - everything between header and footer lives here.
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(_mainPanel, false);
        _bodyRT = bodyGo.AddComponent<RectTransform>();
        _bodyRT.anchorMin = new Vector2(0, 0);
        _bodyRT.anchorMax = new Vector2(1, 1);
        _bodyRT.offsetMin = new Vector2(0, FOOTER_HEIGHT);
        _bodyRT.offsetMax = new Vector2(0, -HEADER_HEIGHT);

        // Media panel (left): mini-header with title + Clear button, plus a vertical
        // scroll view that holds image/movie bubbles in spawn order.
        var mediaGo = new GameObject("MediaPanel");
        mediaGo.transform.SetParent(bodyGo.transform, false);
        _mediaPanelRT = mediaGo.AddComponent<RectTransform>();
        _mediaPanelRT.anchorMin = new Vector2(0, 0);
        _mediaPanelRT.anchorMax = new Vector2(0, 1);
        _mediaPanelRT.pivot = new Vector2(0, 0.5f);
        _mediaPanelRT.anchoredPosition = Vector2.zero;
        _mediaPanelRT.sizeDelta = new Vector2(_splitX, 0);
        mediaGo.AddComponent<Image>().color = new Color(0.78f, 0.78f, 0.80f, 1f);

        CreateMediaHeader(mediaGo.transform);

        // Media scroll view fills the rest of the media panel below the header.
        var mediaScrollHost = new GameObject("MediaScroll");
        mediaScrollHost.transform.SetParent(mediaGo.transform, false);
        var mediaScrollHostRT = mediaScrollHost.AddComponent<RectTransform>();
        mediaScrollHostRT.anchorMin = new Vector2(0, 0);
        mediaScrollHostRT.anchorMax = new Vector2(1, 1);
        mediaScrollHostRT.offsetMin = Vector2.zero;
        mediaScrollHostRT.offsetMax = new Vector2(0, -MEDIA_HEADER_HEIGHT);
        BuildScrollView(mediaScrollHost, out _mediaScroll, out _mediaContent);

        // Chat panel (right): text bubbles only.
        var chatGo = new GameObject("ChatPanel");
        chatGo.transform.SetParent(bodyGo.transform, false);
        _chatPanelRT = chatGo.AddComponent<RectTransform>();
        _chatPanelRT.anchorMin = new Vector2(0, 0);
        _chatPanelRT.anchorMax = new Vector2(1, 1);
        _chatPanelRT.offsetMin = new Vector2(_splitX + SPLITTER_WIDTH, 0);
        _chatPanelRT.offsetMax = Vector2.zero;
        BuildScrollView(chatGo, out _chatScroll, out _chatContent);

        // Splitter (drawn LAST so it renders on top of the panels at the seam).
        var splitterGo = new GameObject("Splitter");
        splitterGo.transform.SetParent(bodyGo.transform, false);
        _splitterRT = splitterGo.AddComponent<RectTransform>();
        _splitterRT.anchorMin = new Vector2(0, 0);
        _splitterRT.anchorMax = new Vector2(0, 1);
        _splitterRT.pivot = new Vector2(0, 0.5f);
        _splitterRT.sizeDelta = new Vector2(SPLITTER_WIDTH, 0);
        _splitterRT.anchoredPosition = new Vector2(_splitX, 0);
        splitterGo.AddComponent<Image>().color = new Color(0.50f, 0.50f, 0.55f, 1f);
        var splitter = splitterGo.AddComponent<ChatSplitterHandle>();
        splitter.SetTarget(this, _bodyRT);
    }

    /// <summary>
    /// Updates _splitX (clamped) and re-positions media panel, chat panel, and
    /// splitter accordingly. Called both at startup and from ChatSplitterHandle.OnDrag.
    /// </summary>
    public void ApplySplit(float newSplitX)
    {
        if (_bodyRT == null) return;
        float bodyWidth = _bodyRT.rect.width;
        float maxSplit = Mathf.Max(MIN_MEDIA_WIDTH, bodyWidth - MIN_CHAT_WIDTH - SPLITTER_WIDTH);
        _splitX = Mathf.Clamp(newSplitX, MIN_MEDIA_WIDTH, maxSplit);

        if (_mediaPanelRT != null)
            _mediaPanelRT.sizeDelta = new Vector2(_splitX, _mediaPanelRT.sizeDelta.y);
        if (_splitterRT != null)
            _splitterRT.anchoredPosition = new Vector2(_splitX, 0);
        if (_chatPanelRT != null)
            _chatPanelRT.offsetMin = new Vector2(_splitX + SPLITTER_WIDTH, _chatPanelRT.offsetMin.y);
    }

    /// <summary>
    /// Mini-header strip across the top of the media panel: just a title and a
    /// "Clear" button (which trims to keep-last-N media bubbles).
    /// </summary>
    private void CreateMediaHeader(Transform mediaParent)
    {
        var header = new GameObject("MediaHeader");
        header.transform.SetParent(mediaParent, false);
        var rt = header.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, MEDIA_HEADER_HEIGHT);
        rt.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(0.72f, 0.72f, 0.76f, 1f);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(header.transform, false);
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(8, 0);
        titleRt.offsetMax = new Vector2(-66, 0); // leave room for Clear button
        _mediaHeaderText = titleGo.AddComponent<TextMeshProUGUI>();
        _mediaHeaderText.text = "Media (0)";
        _mediaHeaderText.font = _font;
        _mediaHeaderText.fontSize = 12;
        _mediaHeaderText.fontStyle = FontStyles.Bold;
        _mediaHeaderText.color = TextTitle;
        _mediaHeaderText.alignment = TextAlignmentOptions.MidlineLeft;
        _mediaHeaderText.raycastTarget = false;

        var clearBtnGo = new GameObject("ClearBtn");
        clearBtnGo.transform.SetParent(header.transform, false);
        var clearRt = clearBtnGo.AddComponent<RectTransform>();
        clearRt.anchorMin = new Vector2(1, 0.5f);
        clearRt.anchorMax = new Vector2(1, 0.5f);
        clearRt.pivot = new Vector2(1, 0.5f);
        clearRt.sizeDelta = new Vector2(56, 20);
        clearRt.anchoredPosition = new Vector2(-4, 0);
        var clearImg = clearBtnGo.AddComponent<Image>();
        clearImg.color = Color.white;
        var clearBtn = clearBtnGo.AddComponent<Button>();
        clearBtn.targetGraphic = clearImg;
        clearBtn.onClick.AddListener(OnClearMediaClicked);

        var clearTxtGo = new GameObject("Text");
        clearTxtGo.transform.SetParent(clearBtnGo.transform, false);
        var clearTxtRt = clearTxtGo.AddComponent<RectTransform>();
        clearTxtRt.anchorMin = Vector2.zero;
        clearTxtRt.anchorMax = Vector2.one;
        clearTxtRt.offsetMin = Vector2.zero;
        clearTxtRt.offsetMax = Vector2.zero;
        var clearTxt = clearTxtGo.AddComponent<TextMeshProUGUI>();
        clearTxt.text = "Clear";
        clearTxt.font = _font;
        clearTxt.fontSize = 11;
        clearTxt.fontStyle = FontStyles.Bold;
        clearTxt.color = TextTitle;
        clearTxt.alignment = TextAlignmentOptions.Center;
        clearTxt.raycastTarget = false;
    }

    /// <summary>
    /// Build the standard chat-style ScrollRect (vertical, with a scrollbar on the
    /// right) into <paramref name="hostGo"/>. Returns the ScrollRect plus the Content
    /// RectTransform with a VerticalLayoutGroup + ContentSizeFitter already wired up.
    /// Used for both the media panel and the text chat panel.
    /// </summary>
    private void BuildScrollView(GameObject hostGo, out ScrollRect scrollOut, out RectTransform contentOut)
    {
        // The ScrollRect lives directly on hostGo so the scrollbar can be a sibling
        // viewport element. We use ChatScrollRectCtrlAware so Ctrl+wheel doesn't
        // scroll (it's reserved for the font-resize gesture in Update()).
        var scroll = hostGo.AddComponent<ChatScrollRectCtrlAware>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(hostGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-18, 0); // leave 18px on the right for the scrollbar
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = PanelBg;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRt;
        scroll.content = contentRT;

        // Scrollbar - matches the dark style of the original chat scrollbar.
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(hostGo.transform, false);
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = new Vector2(1, 1);
        sbRt.pivot = new Vector2(1, 0.5f);
        sbRt.sizeDelta = new Vector2(14, 0);
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
        scroll.verticalScrollbar = scrollbar;

        scrollOut = scroll;
        contentOut = contentRT;
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
        _inputUndo = _inputField.gameObject.AddComponent<TMPInputFieldUndo>();

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

        CreateFooterDragBar(footer.transform);
    }

    private void CreateFooterDragBar(Transform footerTransform)
    {
        var bar = new GameObject("FooterDragBar");
        bar.transform.SetParent(footerTransform, false);
        var rt = bar.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, FOOTER_DRAG_BAR_HEIGHT);
        rt.anchoredPosition = Vector2.zero;

        var img = bar.AddComponent<Image>();
        img.color = new Color(0.62f, 0.62f, 0.66f, 1f);

        bar.AddComponent<PanelDragHandler>().SetTarget(_mainPanel, HEADER_HEIGHT);
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

        // 2) Push the body's bottom edge up by the same amount so neither the media
        //    panel nor the chat panel overlaps the now-taller footer.
        if (_bodyRT != null)
            _bodyRT.offsetMin = new Vector2(_bodyRT.offsetMin.x, FOOTER_HEIGHT + extraFooterHeight);

        // 3) Input field's top reserves room for the strip + the existing 32 px status row.
        if (_inputFieldRT != null)
            _inputFieldRT.offsetMax = new Vector2(_inputFieldRT.offsetMax.x, -(32f + extraFooterHeight));
    }

    private void CreateResizeGrip()
    {
        CreateMoveFrameHandles();

        CreateResizeEdgeHandle(
            "ResizeTop",
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(-RESIZE_CORNER_SIZE * 2f, RESIZE_EDGE_THICKNESS),
            Vector2.zero,
            new Vector2(0f, 1f));
        CreateResizeEdgeHandle(
            "ResizeBottom",
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(-RESIZE_CORNER_SIZE * 2f, RESIZE_EDGE_THICKNESS),
            Vector2.zero,
            new Vector2(0f, -1f));
        CreateResizeEdgeHandle(
            "ResizeLeft",
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(RESIZE_EDGE_THICKNESS, -RESIZE_CORNER_SIZE * 2f),
            Vector2.zero,
            new Vector2(-1f, 0f));
        CreateResizeEdgeHandle(
            "ResizeRight",
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(RESIZE_EDGE_THICKNESS, -RESIZE_CORNER_SIZE * 2f),
            Vector2.zero,
            new Vector2(1f, 0f));

        var grip = new GameObject("ResizeGrip");
        grip.transform.SetParent(_mainPanel, false);
        var rt = grip.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.sizeDelta = new Vector2(RESIZE_CORNER_SIZE, RESIZE_CORNER_SIZE);
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
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), new Vector2(1f, -1f), OnPanelResized);
    }

    private void CreateResizeEdgeHandle(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition, Vector2 resizeDirection)
    {
        var edge = new GameObject(name);
        edge.transform.SetParent(_mainPanel, false);
        var rt = edge.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPosition;

        var img = edge.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.001f);

        var resize = edge.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), resizeDirection, OnPanelResized);
    }

    private void CreateMoveFrameHandles()
    {
        CreateMoveFrameHandle(
            "MoveFrameLeft",
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(MOVE_FRAME_THICKNESS, 0f),
            Vector2.zero);
        CreateMoveFrameHandle(
            "MoveFrameRight",
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(MOVE_FRAME_THICKNESS, 0f),
            Vector2.zero);
        CreateMoveFrameHandle(
            "MoveFrameBottom",
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, MOVE_FRAME_THICKNESS),
            Vector2.zero);
        CreateMoveFrameHandle(
            "MoveFrameTop",
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, MOVE_FRAME_THICKNESS),
            Vector2.zero);
    }

    private void CreateMoveFrameHandle(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        var frame = new GameObject(name);
        frame.transform.SetParent(_mainPanel, false);
        var rt = frame.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPosition;

        var img = frame.AddComponent<Image>();
        img.color = new Color(0.35f, 0.35f, 0.40f, 0.35f);

        frame.AddComponent<PanelDragHandler>().SetTarget(_mainPanel, HEADER_HEIGHT);
    }

    private void OnPanelResized()
    {
        ClampPanelToScreen();
        ApplySplit(_splitX);
    }

    private void ClampPanelToScreen()
    {
        if (_mainPanel != null)
            _mainPanel.anchoredPosition = PanelDragHandler.ClampAnchoredPosition(_mainPanel, _mainPanel.anchoredPosition, HEADER_HEIGHT);
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
        bool shouldAutoScroll = IsScrollAtBottom(_chatScroll);

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
            labelTmp.fontSize = BaseLabelFontSize * _fontSizeMultiplier;
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
        input.textComponent.fontSize = BaseFontSize * _fontSizeMultiplier;
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

        // Forward mouse-wheel events from the bubble's TMP_InputField up to the chat
        // ScrollRect. Without this, TMP_InputField's own IScrollHandler swallows the
        // event (because the field is multiline) and the conversation can't be scrolled
        // while the cursor is hovering a bubble.
        var bubbleScrollForwarder = inputGo.AddComponent<ChatScrollForwarder>();
        bubbleScrollForwarder.target = _chatScroll;

        // Body only - the role label is its own TMP_Text above this field.
        input.text = ConvertMarkdownToTMP(rawMessageText);

        if (linkedInteraction != null)
            HookEditingTo(input, linkedInteraction);

        // Re-measure on every text change (covers streaming, user typing, and re-format).
        input.onValueChanged.AddListener(_ => StartCoroutine(ResizeBubbleDeferred(input, inputLE)));
        StartCoroutine(ResizeBubbleDeferred(input, inputLE));
        if (shouldAutoScroll)
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
        yield return ScrollToBottomDeferred(_chatScroll);
    }

    private IEnumerator ScrollMediaToBottomDeferred()
    {
        yield return ScrollToBottomDeferred(_mediaScroll);
    }

    private IEnumerator ScrollToBottomDeferred(ScrollRect scroll)
    {
        // Layout updates one or two frames after we add/resize content; follow after
        // both passes so the pane stays pinned only when auto-scroll was requested.
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 0f;

        yield return null;
        Canvas.ForceUpdateCanvases();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 0f;
    }

    public static bool IsScrollAtBottom(ScrollRect scroll)
    {
        if (scroll == null || scroll.content == null || scroll.viewport == null)
            return true;

        float contentHeight = scroll.content.rect.height;
        float viewportHeight = scroll.viewport.rect.height;
        if (contentHeight <= viewportHeight + 1f)
            return true;

        // Pixel-based threshold (the normalized position is fraction-of-scroll-range, which
        // gets unhelpfully generous on long chats — 5% of a tall conversation is hundreds of
        // pixels). Must be within SCROLL_BOTTOM_PIXEL_EPSILON of the actual bottom.
        float scrollableRange = contentHeight - viewportHeight;
        float pixelsFromBottom = Mathf.Clamp01(scroll.verticalNormalizedPosition) * scrollableRange;
        return pixelsFromBottom <= SCROLL_BOTTOM_PIXEL_EPSILON;
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
        if (string.IsNullOrWhiteSpace(text))
            text = attachedCount > 0 ? "(no caption)" : "(continue)";

        // Reset the per-turn chain target so a chain="true" action in this reply can
        // never accidentally stack onto a Pic spawned in some earlier turn. Both the
        // most-recent ref AND the LIFO stack need clearing.
        _lastSpawnedPicThisTurn = null;
        _unchainedPicsThisTurn.Clear();

        // Stage attached images so GPTPromptManager auto-attaches them to the user line
        // we're about to add. AddInteraction consumes the pending list internally.
        // ALSO snapshot the bytes into _lastTurnAttachments so a SkillActionExecutor
        // invoked mid-stream can resolve attachment="N" by index even after the strip
        // has been cleared.
        _lastTurnAttachments.Clear();
        if (attachedCount > 0)
        {
            // Each attachment is about to become Image #(_chatImagePics.Count + 1 + offset)
            // via PromoteAttachmentsToChatImages below. Capture the starting index here so
            // we can label the multipart image_url parts with their stable chat_image="N".
            int firstChatIdx = _chatImagePics.Count + 1;
            int promoteOffset = 0;
            foreach (var bytes in attachmentBytes)
            {
                if (bytes == null) continue;
                _promptManager.AddPendingImage(System.Convert.ToBase64String(bytes), firstChatIdx + promoteOffset);
                _lastTurnAttachments.Add(bytes);
                promoteOffset++;
            }
            // Promote each attachment to a real PicMain. This makes the image persist in
            // the media column, gives the user a world Pic they can edit, AND registers it
            // in _chatImagePics so the LLM can reach it via chat_image="N" on this and all
            // future turns. Without this, ChatImageAttachmentZone.ClearAttachments below
            // would wipe the only UI surface holding the image, and SkillActionExecutor's
            // image_to_image validation would report "0 reachable images" on the next turn.
            PromoteAttachmentsToChatImages(attachmentBytes);
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
        _inputUndo?.ResetHistory();
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
        _lastTurnAttachments?.Clear();
        _chatImagePics?.Clear();
        _captionLabels?.Clear();
        _actionParser?.Reset();
        for (int i = _chatContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_chatContent.GetChild(i).gameObject);
        }
        // Footer "Clear" wipes everything (chat + ALL media), in contrast to the
        // media panel's "Clear" button which only trims to keep-N.
        if (_mediaContent != null)
        {
            for (int i = _mediaContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_mediaContent.GetChild(i).gameObject);
            }
        }
        UpdateMediaHeader();
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

    private static int GetAnthropicMaxOutputTokens(string model)
    {
        string m = (model ?? "").ToLowerInvariant();
        if (m.Contains("opus-4-7"))
            return AI_CHAT_ANTHROPIC_OPUS_47_MAX_OUTPUT_TOKENS;
        if (m.Contains("claude-4") || m.Contains("opus-4") || m.Contains("sonnet-4") || m.Contains("haiku-4"))
            return AI_CHAT_ANTHROPIC_DEFAULT_MAX_OUTPUT_TOKENS;
        return AI_CHAT_LEGACY_MAX_OUTPUT_TOKENS;
    }

    private static int GetGeminiMaxOutputTokens(string model)
    {
        string m = (model ?? "").ToLowerInvariant();
        if (m.Contains("gemini-3") || m.Contains("gemini-2.5"))
            return AI_CHAT_GEMINI_MAX_OUTPUT_TOKENS;
        return AI_CHAT_LEGACY_MAX_OUTPUT_TOKENS;
    }

    private static List<LLMParm> GetAIChatLLMParms(LLMSettingsManager settingsMgr, LLMInstanceInfo llmInstance, int llmInstanceID, LLMProvider provider, LLMProviderSettings activeSettings)
    {
        List<LLMParm> source = llmInstance != null
            ? settingsMgr.GetInstanceLLMParms(llmInstanceID)
            : settingsMgr.GetLLMParms(provider);
        var result = new List<LLMParm>();
        if (source != null)
        {
            foreach (var parm in source)
            {
                if (parm == null) continue;
                result.Add(new LLMParm { _key = parm._key, _value = parm._value });
            }
        }

        // AI Chat is a long-form interface; for Ollama, request the model's discovered
        // context length instead of falling back to Ollama's often-smaller server default.
        if (provider == LLMProvider.Ollama && activeSettings != null && activeSettings.maxContextLength > 0)
        {
            bool hasNumCtx = false;
            foreach (var parm in result)
            {
                if (string.Equals(parm._key, "num_ctx", StringComparison.OrdinalIgnoreCase))
                {
                    hasNumCtx = true;
                    break;
                }
            }
            if (!hasNumCtx)
                result.Add(new LLMParm { _key = "num_ctx", _value = activeSettings.maxContextLength.ToString() });
        }

        return result;
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

        // Reset the streaming-action parser for this turn (counters + buffer state).
        _actionParser?.Reset();

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

        // Rebuild the dynamic system prompt every turn after final provider/model
        // resolution so GPU/LLM/skill/chat-image state is fresh.
        if (_contextBuilder != null && _promptManager != null)
        {
            int reachable = ((IChatHost)this).GetChatImageCount();
            // Snapshot captions in chat-image order so the CHAT IMAGES block can
            // print "- Image #N: <caption>" entries for the LLM.
            var captions = new List<string>(reachable);
            for (int i = 1; i <= reachable; i++)
                captions.Add(((IChatHost)this).GetChatImageCaption(i));
            _promptManager.SetBaseSystemPrompt(_contextBuilder.Build(llmInstanceID, reachable, captions));
        }

        _activeProviderInFlight = activeProvider;
        _isStreaming = true;
        _streamBuffer.Clear();
        _streamLastUpdate = 0;
        _streamCharsReceived = 0;
        _streamStartTime = Time.unscaledTime;
        _streamStatusNextRefresh = 0f;
        _streamSpinnerStep = 0;
        SetBusyUI(true, $"{StreamSpinnerFrames[0]} Talking to LLM...");

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

                string json = _openAIMgr.BuildChatCompleteJSON(lines, AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP, temperature, model, true,
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

                string json = _anthropicMgr.BuildChatCompleteJSON(lines, GetAnthropicMaxOutputTokens(model), temperature, model, true);
                _anthropicMgr.SpawnChatCompletionRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            case LLMProvider.LlamaCpp:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                var llmParms = GetAIChatLLMParms(settingsMgr, llmInstance, llmInstanceID, LLMProvider.LlamaCpp, activeSettings);
                LLMReasoningEffort effort = activeSettings.GetReasoningEffort();
                int maxTokens = LLMRequestProfile.GetRecommendedMaxTokens(activeSettings.selectedModel, effort, AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP);
                string suggestedEndpoint;
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, maxTokens, temperature,
                    Config.Get().GetGenericLLMMode(), true, llmParms, false, true);
                _texGenMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey);
                break;
            }

            case LLMProvider.Ollama:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                var llmParms = GetAIChatLLMParms(settingsMgr, llmInstance, llmInstanceID, LLMProvider.Ollama, activeSettings);
                string suggestedEndpoint;
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP, temperature,
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

                string json = _geminiMgr.BuildChatCompleteJSON(lines, GetGeminiMaxOutputTokens(model), temperature, model, true, enableThinking);
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
                bool isDeepSeek = LLMRequestProfile.IsDeepSeekModel(model);
                LLMReasoningEffort compatReasoningEffort = isDeepSeek
                    ? activeSettings.GetReasoningEffort()
                    : (activeSettings.enableThinking ? LLMReasoningEffort.High : LLMReasoningEffort.Off);
                bool? compatEnableThinking = isDeepSeek
                    ? compatReasoningEffort != LLMReasoningEffort.Off
                    : activeSettings.enableThinking;
                int compatMaxTokens = isDeepSeek
                    ? LLMRequestProfile.GetRecommendedMaxTokens(model, compatReasoningEffort, AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP)
                    : AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP;
                float compatTemperature = activeSettings.overrideTemperature
                    ? activeSettings.temperature
                    : (isDeepSeek ? LLMRequestProfile.GetRecommendedTemperature(model, compatReasoningEffort, temperature) : temperature);
                float? compatTopP = activeSettings.overrideTopP
                    ? (float?)activeSettings.topP
                    : (isDeepSeek ? (float?)LLMRequestProfile.GetRecommendedTopP(model, compatReasoningEffort, 1.0f) : null);
                int? compatTopK = activeSettings.overrideTopK ? (int?)activeSettings.topK : null;
                float? compatMinP = activeSettings.overrideMinP ? (float?)activeSettings.minP : null;
                float? compatRepPenalty = activeSettings.overrideRepeatPenalty ? (float?)activeSettings.repeatPenalty : null;
                string compatReasoningEffortParam = isDeepSeek ? LLMReasoningEffortUtil.ToConfigValue(compatReasoningEffort) : null;
                string json = _openAIMgr.BuildChatCompleteJSON(normalizedLines, compatMaxTokens, compatTemperature, model, true,
                    enableThinking: compatEnableThinking,
                    topP: compatTopP, topK: compatTopK, minP: compatMinP, repetitionPenalty: compatRepPenalty,
                    customReasoningEffort: compatReasoningEffortParam);
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

        // Count visible chars for the status line's TPS estimate. Chunk size is the
        // closest cheap proxy for "bytes received" without having to plumb HTTP body
        // length through every provider manager.
        _streamCharsReceived += text.Length;

        // Feed the action parser first - this fires OnSkillActionParsed callbacks for any
        // complete <aitools_action.../> tags in the new chunk. Then ConsumeDisplayText()
        // returns text safe to render in the bubble (action tags stripped/replaced as
        // needed, with any partial in-progress tag held back until it closes).
        if (_actionParser != null)
        {
            _actionParser.Feed(text);
            string display = _actionParser.ConsumeDisplayText();
            if (!string.IsNullOrEmpty(display))
                _streamBuffer.Append(display);
        }
        else
        {
            _streamBuffer.Append(text);
        }

        if (Time.unscaledTime - _streamLastUpdate < STREAM_UPDATE_INTERVAL) return;
        _streamLastUpdate = Time.unscaledTime;

        UpdateStreamingBubble();
    }

    private void UpdateStreamingBubble()
    {
        if (_streamingAssistantField == null) return;
        bool shouldAutoScroll = IsScrollAtBottom(_chatScroll);

        // Body only - the "Assistant" label is its own TMP_Text above the input field.
        _streamingAssistantField.text = ConvertMarkdownToTMP(BuildVisibleStreamText(_streamBuffer.ToString()));
        if (shouldAutoScroll)
            StartCoroutine(ScrollToBottomDeferred());
    }

    private static string BuildVisibleStreamText(string text)
    {
        if (!GenerateSettingsPanel.GetStripThinkTags() || string.IsNullOrEmpty(text))
            return text;

        int thinkOpen = text.IndexOf("<think>", StringComparison.Ordinal);
        int thinkClose = text.IndexOf("</think>", StringComparison.Ordinal);
        if (thinkOpen >= 0 && thinkClose < 0)
        {
            // Hide partial reasoning until the closing boundary arrives, but keep the
            // bubble visibly alive so long DeepSeek thinking does not look hung.
            return thinkOpen > 0 ? text.Substring(0, thinkOpen) + "\n\nThinking..." : "Thinking...";
        }

        return OpenAITextCompletionManager.RemoveThinkTagsFromString(text);
    }

    /// <summary>
    /// Fired by SkillActionParser whenever a complete <c>&lt;aitools_action ... /&gt;</c>
    /// tag has arrived. Hands the action to the executor; UI side-effects (image bubble,
    /// system messages) come back through the IChatHost interface.
    /// </summary>
    private void OnSkillActionParsed(SkillAction action)
    {
        if (_actionExecutor == null) return;
        try
        {
            _actionExecutor.Execute(action);
        }
        catch (Exception ex)
        {
            Debug.LogError("AIChatPanel: SkillActionExecutor.Execute threw: " + ex);
            AddSystemMessage("Skill error: " + ex.Message);
        }
    }

    private void OnLLMCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        bool shouldAutoScroll = IsScrollAtBottom(_chatScroll);

        if (jsonNode == null && (string.IsNullOrEmpty(streamedText) || streamedText.Length == 0))
        {
            string error = db != null ? db.GetStringWithDefault("msg", "") : "";
            if (string.IsNullOrEmpty(error))
            {
                string status = db != null ? db.GetStringWithDefault("status", "") : "";
                error = status == "success"
                    ? "LLM returned an empty response. Check text_completion_sent.json and textgen_json_received.json for the raw exchange."
                    : "Unknown error";
            }
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

        // Final flush of any unparsed text in the action parser (e.g. trailing "<" we
        // were holding back hoping for a tag that never came). The visible bubble stays
        // clean, but the canonical assistant history deliberately keeps raw skill tags so
        // future turns can see concrete examples of successful skill usage.
        if (_actionParser != null)
        {
            string finalDisplay = _actionParser.Flush();
            if (!string.IsNullOrEmpty(finalDisplay))
                _streamBuffer.Append(finalDisplay);
        }
        string visibleText = _streamBuffer.ToString();
        string historyText = PreserveActionTagsForHistory(streamedText);
        visibleText = BuildVisibleStreamText(visibleText);

        // Final visual update (body only, the "Assistant" label is a separate TMP_Text)
        var completedField = _streamingAssistantField;
        if (completedField != null)
            completedField.text = ConvertMarkdownToTMP(visibleText);

        _promptManager.AddInteraction("assistant", historyText);

        // Now that we have an interaction to link the bubble to, switch the assistant
        // bubble from readOnly to editable so the user can hand-tweak the assistant's
        // reply for testing follow-up turns.
        EnableBubbleEditing(completedField, _promptManager.GetLastInteraction());

        FinalizeAssistantTurn(aborted: false, shouldAutoScroll);
    }

    private void FinalizeAssistantTurn(bool aborted, bool shouldAutoScroll = false)
    {
        _isStreaming = false;
        _streamingAssistantField = null;
        _streamingAssistantRT = null;
        ReleaseActiveLLM();
        SetBusyUI(false, aborted ? "Stopped" : "Idle");
        if (shouldAutoScroll)
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

    // ---------- Skills system: action history, settings, image bubble ----------

    private static string PreserveActionTagsForHistory(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Trim();
    }

    private void OnSettingsClicked()
    {
        AIChatSettingsPanel.Show(_skillManager, () =>
        {
            // Reload from disk so any user edits to main_prompt.txt or skill files take
            // effect on the very next turn (rebuilt by ChatContextBuilder.Build()).
            _skillManager?.Reload();
            int n = _skillManager?.GetSkills().Count ?? 0;
            AddSystemMessage($"Reloaded aichat config: {n} skill{(n == 1 ? "" : "s")}.");
        });
    }

    /// <summary>
    /// Refreshes the header status pill: "GPUs: 1/2 busy   LLMs: 3". Cheap; called from
    /// Update() at most every STATUS_PILL_REFRESH_INTERVAL seconds while the panel is
    /// visible.
    /// </summary>
    private void UpdateStatusPill()
    {
        if (_statusPillText == null) return;
        var cfg = Config.Get();
        var im = LLMInstanceManager.Get();

        int gpuTotal = cfg != null ? cfg.GetGPUCount() : 0;
        int gpuBusy = 0;
        for (int i = 0; i < gpuTotal; i++)
        {
            if (cfg.IsGPUBusy(i)) gpuBusy++;
        }
        int llmCount = im != null ? im.GetInstanceCount() : 0;
        // Suffix the pill with a visible TEST flag whenever the test_post_prompt.txt
        // override is active, so the user can't accidentally forget they've hot-patched
        // the system prompt.
        string testFlag = (_skillManager != null && _skillManager.PostPromptIsTestOverride) ? "  [TEST PROMPT]" : "";
        _statusPillText.text = $"GPUs: {gpuBusy}/{gpuTotal} busy   LLMs: {llmCount}{testFlag}";
    }

    /// <summary>
    /// Fire a one-shot caption request against any vision-capable LLM for the
    /// supplied PNG bytes. On completion, sets pic.Caption (overwriting any prior
    /// value), updates the bubble label, and invokes <paramref name="onComplete"/>
    /// (success or failure - the caller uses it to clear an "in-flight" gate).
    /// No-op if no vision LLM is available (onComplete still fires so the caller
    /// doesn't deadlock).
    /// </summary>
    private void TryCaptionPic(PicMain pic, byte[] png, Action onComplete)
    {
        Action safeComplete = () => { try { onComplete?.Invoke(); } catch { } };

        if (pic == null || pic.gameObject == null) { safeComplete(); return; }
        if (png == null || png.Length == 0) { safeComplete(); return; }

        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0) { safeComplete(); return; }

        int targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: true, out int replicaIndex);
        if (targetId < 0)
            targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true, out replicaIndex);
        if (targetId < 0) { safeComplete(); return; }

        var inst = instanceMgr.GetInstance(targetId);
        if (inst == null || inst.settings == null) { safeComplete(); return; }

        instanceMgr.SetLLMBusy(targetId, replicaIndex, true);

        var lines = new Queue<GTPChatLine>();
        var userLine = new GTPChatLine("user", "Describe this image in one short sentence (max 15 words). Just the description, no preamble, no quotes, no markdown.");
        userLine.AddImage(System.Convert.ToBase64String(png), -1);
        lines.Enqueue(userLine);

        int capturedTargetId = targetId;
        int capturedReplicaIndex = replicaIndex;
        PicMain capturedPic = pic;

        Action<RTDB, JSONObject, string> onDone = (db, json, text) =>
        {
            instanceMgr.SetLLMBusy(capturedTargetId, capturedReplicaIndex, false);
            try
            {
                string clean = (text ?? "").Trim();
                if (string.IsNullOrEmpty(clean) && json != null)
                {
                    try { clean = json["choices"][0]["message"]["content"]; } catch { /* no-op */ }
                }
                clean = ClampCaption(clean);
                if (string.IsNullOrEmpty(clean)) return;

                if (capturedPic != null && capturedPic.gameObject != null)
                {
                    capturedPic.Caption = clean;
                    if (_captionLabels.TryGetValue(capturedPic, out var entry) && entry.label != null)
                        entry.label.text = entry.baseText + " - " + clean;
                }
            }
            finally { safeComplete(); }
        };

        SkillActionExecutor.DispatchOneShot(this, inst, lines, onDone, "ImageCaption");
    }

    /// <summary>
    /// Trim a caption to a sane length (~25 words) and strip surrounding quotes/
    /// trailing punctuation noise. LLMs sometimes ignore the length hint or wrap
    /// the response in quotes; this keeps the system prompt's CHAT IMAGES block
    /// from blowing up.
    /// </summary>
    private static string ClampCaption(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        // Strip a single pair of surrounding quotes / asterisks / backticks.
        if (s.Length >= 2)
        {
            char a = s[0], b = s[s.Length - 1];
            if ((a == '"' && b == '"') || (a == '\'' && b == '\'') || (a == '`' && b == '`'))
                s = s.Substring(1, s.Length - 2).Trim();
        }
        // Collapse newlines so a multi-line response becomes one line.
        s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        // Word clamp.
        var words = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        const int MaxWords = 25;
        if (words.Length > MaxWords)
            s = string.Join(" ", words, 0, MaxWords) + "…";
        return s;
    }

    // ---------- Hover tooltip for chat image bubbles ----------

    private GameObject _captionTooltipRoot;
    private RectTransform _captionTooltipRT;
    private TextMeshProUGUI _captionTooltipText;

    /// <summary>
    /// Pointer-event trigger attached to each chat-image bubble. Calls back into
    /// the host panel to pop a floating tooltip with the bubble's full caption -
    /// useful because the bubble label is clipped to the narrow media column.
    /// </summary>
    private class BubbleCaptionHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        public AIChatPanel host;
        public PicMain pic;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (host == null || pic == null) return;
            host.ShowCaptionTooltip(pic, eventData.position);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (host == null) return;
            host.MoveCaptionTooltip(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (host == null) return;
            host.HideCaptionTooltip();
        }
    }

    private void EnsureCaptionTooltip()
    {
        if (_captionTooltipRoot != null) return;
        if (_panelRoot == null) return;

        _captionTooltipRoot = new GameObject("CaptionTooltip");
        _captionTooltipRoot.transform.SetParent(_panelRoot.transform, false);
        _captionTooltipRT = _captionTooltipRoot.AddComponent<RectTransform>();
        // Anchored at bottom-left of the canvas so anchoredPosition == screen position.
        _captionTooltipRT.anchorMin = new Vector2(0, 0);
        _captionTooltipRT.anchorMax = new Vector2(0, 0);
        _captionTooltipRT.pivot = new Vector2(0, 0);

        var bg = _captionTooltipRoot.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.08f, 0.92f);
        bg.raycastTarget = false; // tooltip must NOT eat the cursor

        var hlg = _captionTooltipRoot.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 5, 5);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        var fitter = _captionTooltipRoot.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(_captionTooltipRoot.transform, false);
        var textLE = textGo.AddComponent<LayoutElement>();
        textLE.preferredWidth = 320f;
        _captionTooltipText = textGo.AddComponent<TextMeshProUGUI>();
        _captionTooltipText.font = _font;
        _captionTooltipText.fontSize = 13f;
        _captionTooltipText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        _captionTooltipText.alignment = TextAlignmentOptions.TopLeft;
        _captionTooltipText.textWrappingMode = TextWrappingModes.Normal;
        _captionTooltipText.raycastTarget = false;

        _captionTooltipRoot.SetActive(false);
    }

    /// <summary>
    /// Show the tooltip with the full caption for <paramref name="pic"/>, positioned
    /// just below-right of the cursor. Called from BubbleCaptionHoverTrigger on enter.
    /// </summary>
    private void ShowCaptionTooltip(PicMain pic, Vector2 screenPos)
    {
        if (pic == null) return;
        EnsureCaptionTooltip();
        if (_captionTooltipText == null) return;

        string caption = pic.Caption ?? "";
        // Compose: bold "Image #N" header on top, full caption below. The header
        // gives the user a quick scan of which slot they're hovering even when
        // the caption is empty/still being computed.
        int idx0 = _chatImagePics.IndexOf(pic);
        string header = idx0 >= 0 ? $"Image #{idx0 + 1}" : "Image";
        if (string.IsNullOrEmpty(caption))
            caption = "(captioning...)";
        _captionTooltipText.text = $"<b>{header}</b>\n{caption}";

        _captionTooltipRoot.transform.SetAsLastSibling(); // render on top
        _captionTooltipRoot.SetActive(true);
        MoveCaptionTooltip(screenPos);
    }

    private void MoveCaptionTooltip(Vector2 screenPos)
    {
        if (_captionTooltipRT == null) return;
        // Offset so the tooltip doesn't sit right under the cursor (which would
        // immediately fire a pointer-exit if the cursor crosses into it).
        Vector2 pos = screenPos + new Vector2(14f, 14f);
        // Clamp to keep the tooltip on-screen.
        Vector2 size = _captionTooltipRT.rect.size;
        pos.x = Mathf.Min(pos.x, Screen.width - size.x - 4f);
        pos.y = Mathf.Min(pos.y, Screen.height - size.y - 4f);
        pos.x = Mathf.Max(4f, pos.x);
        pos.y = Mathf.Max(4f, pos.y);
        _captionTooltipRT.anchoredPosition = pos;
    }

    private void HideCaptionTooltip()
    {
        if (_captionTooltipRoot != null)
            _captionTooltipRoot.SetActive(false);
    }

    /// <summary>
    /// Turn each user-pasted/dragged attachment into a real PicMain in the world
    /// gallery and a chat-image bubble in the media column. After this, attachments
    /// have the same lifecycle as AI-generated images: addressable via
    /// chat_image="N", visible in the media column, mirrored in the chat by
    /// ChatPicMirror, and editable by the user as a normal world Pic.
    /// </summary>
    private void PromoteAttachmentsToChatImages(IReadOnlyList<byte[]> attachments)
    {
        if (attachments == null || attachments.Count == 0) return;
        var imageGen = ImageGenerator.Get();
        if (imageGen == null) return;

        foreach (var bytes in attachments)
        {
            if (bytes == null || bytes.Length == 0) continue;
            // Same decode pattern SkillActionExecutor uses for chat_image inputs, so
            // round-trips of the same PNG are byte-identical.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                continue;
            }
            var go = imageGen.AddImageByTexture(tex);
            if (go == null) continue;
            var pic = go.GetComponent<PicMain>();
            if (pic == null) continue;
            AppendUserAttachmentBubble(pic);
        }
    }

    /// <summary>
    /// Build a chat-side image / movie bubble that mirrors the live output of a Pic
    /// just spawned by the skills system. Uses the same VLG-driven layout pattern as
    /// AppendBubble() so it sits naturally between text bubbles in stream order.
    /// </summary>
    private void AppendImageBubble(SkillAction action, PicMain spawnedPic)
    {
        if (spawnedPic == null || _mediaContent == null) return;

        // Track the spawned PicMain in stable, per-session order so the LLM can
        // reference it on a later turn via chat_image="N". Add BEFORE building the
        // label so the label shows the correct N.
        _chatImagePics.Add(spawnedPic);
        int chatImageNumber = _chatImagePics.Count;

        string skillId = action != null ? (action.SkillId ?? "") : "";
        bool isMovie = skillId == BuiltInSkillIds.GenerateMovie || skillId == BuiltInSkillIds.ImageToMovie;
        string kindLabel = isMovie ? "Movie" : "Image";
        string label = $"{kindLabel} #{chatImageNumber} ({skillId})";
        AppendImageBubbleInternal(spawnedPic, label, isMovie);

        // Generated images don't have texture data yet (workflow hasn't run). The
        // PicMain callbacks (m_onFinishedRenderingCallback / m_onFinishedScriptCallback)
        // are unreliable signals here - they're reset between steps, multiple
        // subsystems chain to them, and a plain single-image gen doesn't always fire
        // the script callback. Just poll until TryGetImageAsPng returns bytes.
        if (!isMovie && spawnedPic != null)
            StartCoroutine(WaitForPicAndCaption(spawnedPic));
    }

    /// <summary>
    /// Polls a freshly-spawned Pic and (re-)captions it whenever its texture
    /// settles into a stable state. Required because:
    ///  - generate_image starts with no texture, then the workflow result lands later.
    ///  - image_to_image first calls SetImage(sourceTex) synchronously (from
    ///    SkillActionExecutor) so the SOURCE shows up, then the workflow REPLACES
    ///    it with the edited RESULT. A naive "first non-null bytes win" caption
    ///    captures the source instead of the result.
    /// We track texture-reference identity as a free change detector (PicMain
    /// replaces the sprite + its Texture2D wholesale on every workflow result -
    /// see SetImage / LoadImageByFilename), and only encode PNG bytes when there
    /// is a new un-captioned texture to send. The loop exits as soon as the
    /// current texture has been captioned and the source Pic isn't producing
    /// anything new, instead of running EncodeToPNG every poll for the full
    /// 240s timeout window.
    /// </summary>
    private IEnumerator WaitForPicAndCaption(PicMain pic)
    {
        const float timeoutSeconds = 240f;
        const float pollInterval = 1.5f;
        const int stableTicksRequired = 2; // ~3s of stability before captioning
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        Texture lastSeenTex = null;
        Texture captionedTex = null;
        int stableTicks = 0;
        bool inFlight = false;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (pic == null || pic.gameObject == null) yield break;

            Texture curTex;
            if (!pic.TryGetCurrentTexture(out curTex) || curTex == null)
            {
                yield return new WaitForSeconds(pollInterval);
                continue;
            }

            if (curTex != lastSeenTex)
            {
                lastSeenTex = curTex;
                stableTicks = 0;
            }
            else if (stableTicks < int.MaxValue)
            {
                stableTicks++;
            }

            // EncodeToPNG only when there is a NEW stable texture worth captioning.
            // Doing this every poll regardless was the source of a periodic app-wide
            // FPS hitch: with N generated bubbles open, N encodes (~10-50ms each on
            // a 1024^2 image) stacked up on the same 1.5s cadence. inFlight gates
            // overlapping caption jobs; the next stable tick re-fires if needed.
            if (curTex != captionedTex && !inFlight && stableTicks >= stableTicksRequired)
            {
                if (pic.TryGetImageAsPng(out byte[] png) && png != null && png.Length > 0)
                {
                    inFlight = true;
                    Texture submittedTex = curTex;
                    TryCaptionPic(pic, png, () =>
                    {
                        inFlight = false;
                        captionedTex = submittedTex;
                    });
                }
            }

            // Done: the current texture has been captioned and no further workflow
            // step is expected to swap it. Exiting here is what stops the polling
            // from running for the full 240s timeout after a successful gen.
            if (!inFlight && captionedTex == curTex && !pic.IsBusyBasic())
                yield break;

            yield return new WaitForSeconds(pollInterval);
        }
    }

    /// <summary>
    /// Build a chat-side bubble for an image the USER dragged/pasted into the chat
    /// this turn. Shares the rendering with skill-spawned bubbles so the image is a
    /// first-class chat image: registered in _chatImagePics for chat_image="N"
    /// reuse, visible in the media column, and live-mirrored from a real PicMain
    /// (which the user can also see / edit in the world gallery).
    /// </summary>
    private void AppendUserAttachmentBubble(PicMain pic)
    {
        if (pic == null || _mediaContent == null) return;
        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        string label = $"Image #{chatImageNumber} (you)";
        AppendImageBubbleInternal(pic, label, isMovie: false);
        // User attachments have a stable texture loaded synchronously in
        // PromoteAttachmentsToChatImages - the same stability-aware coroutine
        // we use for generated images works fine here too (it'll just settle
        // and caption immediately).
        StartCoroutine(WaitForPicAndCaption(pic));
    }

    /// <summary>
    /// Shared bubble construction for both AI-generated and user-attached images.
    /// Caller is responsible for registering <paramref name="pic"/> in _chatImagePics
    /// and computing <paramref name="labelText"/> (which embeds the chat_image index).
    /// </summary>
    private void AppendImageBubbleInternal(PicMain pic, string labelText, bool isMovie)
    {
        bool shouldAutoScroll = IsScrollAtBottom(_mediaScroll);

        var bubble = new GameObject(isMovie ? "Bubble_Movie" : "Bubble_Image");
        // Image / movie bubbles live in the LEFT MediaPanel (separate from text).
        bubble.transform.SetParent(_mediaContent, false);
        var bubbleImg = bubble.AddComponent<Image>();
        bubbleImg.color = AssistantBubbleBg;

        var bubbleVLG = bubble.AddComponent<VerticalLayoutGroup>();
        bubbleVLG.padding = new RectOffset(8, 8, 4, 4);
        bubbleVLG.spacing = 4;
        bubbleVLG.childAlignment = TextAnchor.UpperLeft;
        bubbleVLG.childControlWidth = true;
        bubbleVLG.childControlHeight = true;
        bubbleVLG.childForceExpandWidth = true;
        bubbleVLG.childForceExpandHeight = false;

        var bubbleCSF = bubble.AddComponent<ContentSizeFitter>();
        bubbleCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bubbleCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Role label.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(bubble.transform, false);
        var labelLE = labelGo.AddComponent<LayoutElement>();
        labelLE.minHeight = 16f;
        labelLE.preferredHeight = 16f;
        var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
        // Bubble label includes the stable Image #N so the user (and the LLM, when it
        // re-reads this transcript) can match the bubble against chat_image="N" in skill
        // invocations. The number is the index into _chatImagePics + 1.
        labelTmp.text = labelText;
        labelTmp.font = _font;
        labelTmp.fontSize = BaseLabelFontSize * _fontSizeMultiplier;
        labelTmp.fontStyle = FontStyles.Bold;
        labelTmp.color = new Color(0.10f, 0.45f, 0.20f);
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.raycastTarget = false;

        // Remember the label so the async caption job (TryCaptionPic) can
        // append "- <caption>" once the vision LLM responds.
        if (pic != null)
            _captionLabels[pic] = (labelTmp, labelText);

        // Hover tooltip: the label gets clipped in the narrow media column, so
        // hovering over the bubble pops a floating panel with the full caption.
        if (pic != null)
        {
            var tip = bubble.AddComponent<BubbleCaptionHoverTrigger>();
            tip.host = this;
            tip.pic = pic;
        }

        // RawImage holder. We can't just put a RawImage as a direct child of the bubble
        // VLG (which has childForceExpandWidth=true) because RawImage stretches its texture
        // to fill its rect, and the bubble width almost never matches the source aspect.
        // Instead we wrap it in a HorizontalLayoutGroup container that DOES NOT force-
        // expand its child width, then size the RawImage explicitly per its true aspect.
        // Container width is still bubble-width (so ChatPicMirror can read it for layout)
        // but the inner RawImage takes only the aspect-correct width and is centered.
        var imgContainerGo = new GameObject("ImageContainer");
        imgContainerGo.transform.SetParent(bubble.transform, false);
        var containerHLG = imgContainerGo.AddComponent<HorizontalLayoutGroup>();
        containerHLG.padding = new RectOffset(0, 0, 0, 0);
        containerHLG.spacing = 0;
        containerHLG.childAlignment = TextAnchor.MiddleCenter;
        // childControlWidth/Height MUST be true, otherwise HLG ignores the child's
        // LayoutElement.preferredWidth/Height and uses the RectTransform's default
        // 100x100 sizeDelta - which is the postage-stamp bug. childForceExpandWidth/
        // Height are false so the child does NOT stretch beyond its preferredW/H.
        // Combined with MiddleCenter alignment, that gives us "image at exactly its
        // computed aspect-correct size, centered in the bubble".
        containerHLG.childControlWidth = true;
        containerHLG.childControlHeight = true;
        containerHLG.childForceExpandWidth = false;
        containerHLG.childForceExpandHeight = false;
        var containerLE = imgContainerGo.AddComponent<LayoutElement>();
        containerLE.minHeight = 96f;
        containerLE.preferredHeight = 200f; // ChatPicMirror updates per-frame to actual image height

        var imgGo = new GameObject("Preview");
        imgGo.transform.SetParent(imgContainerGo.transform, false);
        var imgLE = imgGo.AddComponent<LayoutElement>();
        imgLE.preferredWidth = 200f;  // ChatPicMirror updates to aspect-correct W
        imgLE.preferredHeight = 200f; // ChatPicMirror updates to aspect-correct H
        imgLE.minWidth = 96f;
        imgLE.minHeight = 96f;
        var raw = imgGo.AddComponent<RawImage>();
        raw.color = new Color(1f, 1f, 1f, 0.15f); // hint of the empty slot until first frame

        // Status row beneath the image (shows PicMain's live status text). Important:
        // PicMain emits multi-line statuses ("Waiting for GPU to\nrun workflow...",
        // "Sampler\nAdvanced\nStep 6/20", etc.). We let TMP report its natural preferred
        // height to the parent VLG (childControlHeight=true picks it up via ILayoutElement)
        // by NOT setting a fixed preferredHeight - only a small minHeight as a floor.
        // textWrappingMode=Normal so a single very long status line wraps within the bubble.
        var statusGo = new GameObject("Status");
        statusGo.transform.SetParent(bubble.transform, false);
        var statusLE = statusGo.AddComponent<LayoutElement>();
        statusLE.minHeight = 14f;
        statusLE.preferredHeight = -1f; // -1 = "use the child's natural preferred height"
        statusLE.flexibleHeight = -1f;
        var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
        statusTmp.text = "Queued...";
        statusTmp.font = _font;
        statusTmp.fontSize = Mathf.Max(10f, BaseLabelFontSize * _fontSizeMultiplier - 1f);
        statusTmp.color = new Color(0.30f, 0.30f, 0.35f);
        statusTmp.alignment = TextAlignmentOptions.TopLeft;
        statusTmp.textWrappingMode = TextWrappingModes.Normal;
        statusTmp.raycastTarget = false;

        // Mirror component does the polling + click-to-focus.
        var mirror = bubble.AddComponent<ChatPicMirror>();
        mirror.targetImage = raw;
        mirror.statusLabel = statusTmp;
        mirror.imageLayoutElement = imgLE;
        mirror.containerLayoutElement = containerLE;
        mirror.containerRT = imgContainerGo.GetComponent<RectTransform>();
        mirror.sourcePic = pic;
        mirror.occludingPanel = _mainPanel;
        mirror.autoScrollTarget = _mediaScroll;

        UpdateMediaHeader();
        if (shouldAutoScroll)
            StartCoroutine(ScrollMediaToBottomDeferred());
    }

    /// <summary>
    /// Update the media panel header text ("Media (N)") to reflect how many bubbles
    /// are currently visible. Called whenever a bubble is added or removed.
    /// </summary>
    private void UpdateMediaHeader()
    {
        if (_mediaHeaderText == null) return;
        int n = _mediaContent != null ? _mediaContent.childCount : 0;
        _mediaHeaderText.text = $"Media ({n})";
    }

    /// <summary>
    /// Configurable: how many media bubbles to keep when the user clicks the media
    /// "Clear" button. Stored in PlayerPrefs so it persists across sessions; defaults
    /// to 10. Reading via this helper ensures any non-positive stored value falls
    /// back to a safe default rather than causing infinite trim loops.
    /// </summary>
    public static int GetKeepLastNMedia()
    {
        int n = PlayerPrefs.GetInt(PREFS_KEEP_LAST_N_MEDIA, DEFAULT_KEEP_LAST_N_MEDIA);
        return Mathf.Max(0, n);
    }

    public static void SetKeepLastNMedia(int n)
    {
        PlayerPrefs.SetInt(PREFS_KEEP_LAST_N_MEDIA, Mathf.Max(0, n));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Global prefix prepended to every {{Preset Name.txt}} sentinel in the system
    /// prompt before it goes to the LLM. Empty string = use bare names. Lets the
    /// user swap in a parallel set of presets (e.g. "test_") without editing any
    /// skill md or main_prompt - all wrapped names track in lockstep.
    /// </summary>
    public static string GetPresetPrefix()
    {
        return PlayerPrefs.GetString(PREFS_PRESET_PREFIX, DEFAULT_PRESET_PREFIX) ?? "";
    }

    public static void SetPresetPrefix(string prefix)
    {
        PlayerPrefs.SetString(PREFS_PRESET_PREFIX, prefix ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Trims the media panel to the last <see cref="GetKeepLastNMedia"/> bubbles.
    /// The matching entries are also removed from <see cref="_chatImagePics"/> so
    /// the LLM's chat_image="N" indices stay aligned with what's visible. Doesn't
    /// touch the world Pics - the bubble itself is destroyed but its source PicMain
    /// remains in the world for the user to keep editing.
    /// </summary>
    private void OnClearMediaClicked()
    {
        if (_mediaContent == null) return;
        int keep = GetKeepLastNMedia();
        TrimMediaToKeepLastN(keep);
        UpdateMediaHeader();
    }

    private void TrimMediaToKeepLastN(int keep)
    {
        if (_mediaContent == null) return;
        int childCount = _mediaContent.childCount;
        int toRemove = childCount - keep;
        if (toRemove <= 0) return;

        // Children are in spawn order (oldest first) thanks to the VLG. Detach + destroy
        // from the front so subsequent GetChild(0) calls advance through the list
        // correctly (Destroy alone is deferred to end-of-frame, so we have to unparent
        // to make the loop iterate). Pop matching entries off the head of
        // _chatImagePics so chat_image="1" still points at the OLDEST visible bubble.
        for (int i = 0; i < toRemove; i++)
        {
            var child = _mediaContent.GetChild(0);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
        if (_chatImagePics != null && _chatImagePics.Count > 0)
        {
            int popN = Mathf.Min(toRemove, _chatImagePics.Count);
            // Drop the corresponding caption-label entries too so the dict doesn't
            // accumulate dead Pic references over a long chat.
            for (int i = 0; i < popN; i++)
            {
                var poppedPic = _chatImagePics[i];
                if (poppedPic != null) _captionLabels.Remove(poppedPic);
            }
            _chatImagePics.RemoveRange(0, popN);
        }
    }

    // ---------- IChatHost (called by SkillActionExecutor) ----------

    MonoBehaviour IChatHost.CoroutineRunner => this;

    byte[] IChatHost.GetTurnAttachmentBytes(int oneBasedIndex)
    {
        int idx0 = oneBasedIndex - 1;
        if (_lastTurnAttachments == null || idx0 < 0 || idx0 >= _lastTurnAttachments.Count)
            return null;
        return _lastTurnAttachments[idx0];
    }

    int IChatHost.GetTurnAttachmentCount()
    {
        return _lastTurnAttachments != null ? _lastTurnAttachments.Count : 0;
    }

    void IChatHost.AddInfoBubble(string text) => AddSystemMessage(text);

    void IChatHost.AddSystemInjectionAndBubble(string text)
    {
        // Inject the message as a system role so the LLM sees it on the NEXT turn (after
        // the current assistant reply finishes). Also display it in the chat so the user
        // can see what was injected.
        if (_promptManager != null)
            _promptManager.AddInteraction("system", text);
        AddSystemMessage(text);
    }

    void IChatHost.AppendImageBubbleForPic(SkillAction action, PicMain spawnedPic)
    {
        AppendImageBubble(action, spawnedPic);
    }

    byte[] IChatHost.GetChatImagePngBytes(int oneBasedIndex)
    {
        int idx0 = oneBasedIndex - 1;
        if (_chatImagePics == null || idx0 < 0 || idx0 >= _chatImagePics.Count) return null;
        var pic = _chatImagePics[idx0];
        if (pic == null || pic.gameObject == null) return null; // user deleted the world Pic
        return pic.TryGetImageAsPng(out byte[] png) ? png : null;
    }

    int IChatHost.GetChatImageCount()
    {
        if (_chatImagePics == null) return 0;
        // Only count entries whose world Pic is still alive AND has a renderable texture.
        // Stale entries (pic destroyed by user, or render still queued) are advertised as
        // 0 so the LLM doesn't try to reference them.
        int n = 0;
        foreach (var pic in _chatImagePics)
        {
            if (pic == null || pic.gameObject == null) continue;
            if (!pic.TryGetCurrentTexture(out var tex) || tex == null) continue;
            n++;
        }
        return n;
    }

    string IChatHost.GetChatImageCaption(int oneBasedIndex)
    {
        int idx0 = oneBasedIndex - 1;
        if (_chatImagePics == null || idx0 < 0 || idx0 >= _chatImagePics.Count) return "";
        var pic = _chatImagePics[idx0];
        if (pic == null || pic.gameObject == null) return "";
        return pic.Caption ?? "";
    }

    PicMain IChatHost.GetLastSpawnedPicForTurn()
    {
        // Defensively null out a destroyed-but-still-referenced Pic so the executor's
        // "no chain target" error path triggers correctly instead of hitting a Unity
        // null-equality on a dead GameObject.
        if (_lastSpawnedPicThisTurn == null || _lastSpawnedPicThisTurn.gameObject == null)
            return null;
        return _lastSpawnedPicThisTurn;
    }

    void IChatHost.SetLastSpawnedPicForTurn(PicMain spawnedPic)
    {
        _lastSpawnedPicThisTurn = spawnedPic;
        if (spawnedPic != null)
            _unchainedPicsThisTurn.Add(spawnedPic);
    }

    PicMain IChatHost.ConsumeChainTarget()
    {
        // LIFO: walk from the END (most-recent push) so a chain action animates the
        // Pic the LLM most recently emitted - the natural "the image I just made"
        // intent. If a reply interleaves standalone gens with paired stacks (gen,
        // mov, gen, gen, mov), the second mov correctly chains onto the THIRD gen
        // (not the second), since the first mov already consumed the second gen's
        // entry off the stack. Skip dead Pics in case the user closed one mid-reply.
        while (_unchainedPicsThisTurn.Count > 0)
        {
            int last = _unchainedPicsThisTurn.Count - 1;
            var p = _unchainedPicsThisTurn[last];
            _unchainedPicsThisTurn.RemoveAt(last);
            if (p != null && p.gameObject != null)
                return p;
        }

        // Stack exhausted - fall back to the most-recent Pic so a 3+ step chain
        // (gen_image -> img_to_image chain -> img_to_movie chain) keeps stacking on
        // the same root after its stack entry was consumed by step 2.
        if (_lastSpawnedPicThisTurn == null || _lastSpawnedPicThisTurn.gameObject == null)
            return null;
        return _lastSpawnedPicThisTurn;
    }

    private void RefreshHeaderTitle()
    {
        if (_titleText == null) return;

        string label = "AI Chat";

        try
        {
            var instance = ResolveHeaderLLMInstance();
            if (instance != null)
            {
                label = BuildHeaderTitle(instance.providerType, instance.settings);
            }
            else
            {
                var settingsMgr = LLMSettingsManager.Get();
                if (settingsMgr != null)
                {
                    var p = settingsMgr.GetActiveProvider();
                    var s = settingsMgr.GetProviderSettings(p);
                    label = BuildHeaderTitle(p, s);
                }
            }
        }
        catch
        {
            // Fallback - keep the simple "AI Chat" label.
        }

        _titleText.text = label;
    }

    /// <summary>
    /// Resolve the LLM instance the chat header should display. This mirrors the
    /// allocation order in SendChatTurn without changing busy counters.
    /// </summary>
    private LLMInstanceInfo ResolveHeaderLLMInstance()
    {
        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr == null || instanceMgr.GetInstanceCount() <= 0)
            return null;

        if (_activeLLMInstanceID >= 0)
        {
            var inFlight = instanceMgr.GetInstance(_activeLLMInstanceID);
            if (inFlight != null)
                return inFlight;
        }

        bool isVisionJob = _promptManager != null && _promptManager.HasAnyImages();
        int replicaIndex;
        int instanceID = instanceMgr.GetFreeLLM(isSmallJob: true, isVisionJob: isVisionJob, out replicaIndex);
        if (instanceID < 0)
            instanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: true, isVisionJob: isVisionJob, out replicaIndex);

        return instanceID >= 0 ? instanceMgr.GetInstance(instanceID) : null;
    }

    private static string BuildHeaderTitle(LLMProvider provider, LLMProviderSettings settings)
    {
        string providerName = GetLLMProviderDisplayName(provider);
        string model = settings != null ? settings.selectedModel : "";
        return string.IsNullOrEmpty(model)
            ? $"AI Chat - {providerName}"
            : $"AI Chat - {providerName} ({model})";
    }

    private static string GetLLMProviderDisplayName(LLMProvider provider)
    {
        switch (provider)
        {
            case LLMProvider.OpenAI:
                return "OpenAI";
            case LLMProvider.Anthropic:
                return "Anthropic";
            case LLMProvider.LlamaCpp:
                return "llama.cpp";
            case LLMProvider.Ollama:
                return "Ollama";
            case LLMProvider.Gemini:
                return "Gemini";
            case LLMProvider.OpenAICompatible:
                return "OpenAI Compatible";
            default:
                return provider.ToString();
        }
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
        // _panelRoot stays active even when the user has closed the chat (so the
        // LLM stream coroutine can finish), but the input field itself lives under
        // the hidden _mainPanel - calling ActivateInputField on an inactive object
        // would fail anyway, and we don't want to steal focus while hidden.
        if (!gameObject.activeInHierarchy || !_isVisible) return;
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

    /// <summary>
    /// Apply <see cref="_fontSizeMultiplier"/> to every text element in the panel that
    /// participates in chat reading: the typing input field + placeholder, every
    /// existing bubble's TMP_InputField text component, and every "Label" TMP_Text
    /// (the small "You / Assistant / Info" role labels). Body text scales from
    /// <see cref="BaseFontSize"/>; role labels scale from <see cref="BaseLabelFontSize"/>.
    /// Triggers a re-layout pass on every bubble so heights re-fit the new size.
    /// </summary>
    private void ApplyChatFontSize()
    {
        float bodySize = BaseFontSize * _fontSizeMultiplier;
        float labelSize = BaseLabelFontSize * _fontSizeMultiplier;

        if (_inputField != null)
        {
            if (_inputField.textComponent != null)
                _inputField.textComponent.fontSize = bodySize;
            if (_inputField.placeholder is TextMeshProUGUI ph)
                ph.fontSize = bodySize;
        }

        if (_chatContent != null)
        {
            // Bubble bodies (the editable / read-only TMP_InputField inside each bubble).
            foreach (var input in _chatContent.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (input.textComponent != null)
                    input.textComponent.fontSize = bodySize;
                var le = input.GetComponent<LayoutElement>();
                if (le != null) StartCoroutine(ResizeBubbleDeferred(input, le));
            }

            // Role labels (small "You" / "Assistant" / "Info" headers above each bubble).
            foreach (var t in _chatContent.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t != null && t.gameObject.name == "Label")
                    t.fontSize = labelSize;
            }
        }
    }

    /// <summary>
    /// Adjust the chat font multiplier in response to a Ctrl+MouseWheel gesture over
    /// the panel. Step is proportional to the wheel delta so trackpads get smooth
    /// scaling and notched mice get one ~10% step per click.
    /// </summary>
    private void AdjustChatFontSize(float wheelDelta)
    {
        if (Mathf.Abs(wheelDelta) < 0.001f) return;
        _fontSizeMultiplier = Mathf.Clamp(
            _fontSizeMultiplier + wheelDelta * FontMultiplierStep,
            MinFontMultiplier, MaxFontMultiplier);
        ApplyChatFontSize();
    }

    private bool IsMouseOverChatPanel()
    {
        if (_mainPanel == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_mainPanel, Input.mousePosition);
    }

    private void Update()
    {
        // Streaming flush + status pill refresh further down must keep running even
        // while the panel is "hidden" - the LLM coroutine on _panelRoot is still
        // alive (we deliberately don't deactivate _panelRoot on Hide; see SetVisible)
        // and we want the streamed bubble + counters to be up to date when the user
        // pops the panel back open.
        if (_isVisible)
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !_isStreaming)
                Hide();

            // Ctrl+MouseWheel anywhere over the chat panel adjusts chat font size.
            // The chat ScrollRect (ChatScrollRectCtrlAware) already swallows its own
            // scroll while Ctrl is held, so this never fights the conversation scroll.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.001f && IsMouseOverChatPanel())
                    AdjustChatFontSize(wheel);
            }
        }

        // Enter / Shift+Enter handling for the chat input. Done here (not via TMP_InputField's
        // own MultiLineSubmit mode or onValidateInput) because both of those are unreliable
        // about reading the Shift modifier in Unity 6 / TMP 3.
        if (_isVisible && _inputField != null && _inputField.isFocused
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

        // Refresh the "Talking to LLM..." status with a rotating spinner + live
        // tokens/TPS readout. Spinner rotates every refresh tick (~6 fps) regardless
        // of incoming chunks so the user gets a clear "still working" signal even
        // while waiting on the first byte.
        if (_isStreaming && _statusText != null && Time.unscaledTime >= _streamStatusNextRefresh)
        {
            _streamStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _streamSpinnerStep = (_streamSpinnerStep + 1) % StreamSpinnerFrames.Length;
            char spin = StreamSpinnerFrames[_streamSpinnerStep];
            float elapsed = Mathf.Max(0.001f, Time.unscaledTime - _streamStartTime);
            // ~4 chars per token is a good rough average for English completions; the
            // user gets a sense of pace, not a token-exact count.
            int approxTokens = _streamCharsReceived / 4;
            float tps = approxTokens / elapsed;
            string tpsStr = tps >= 10 ? tps.ToString("F0") : tps.ToString("F1");
            _statusText.text = $"{spin} Talking to LLM   {approxTokens} tok   {tpsStr} t/s";
        }

        // Periodic header status pill refresh (cheap; reads counters from Config/LLM mgr).
        if (Time.unscaledTime >= _statusPillNextRefresh)
        {
            _statusPillNextRefresh = Time.unscaledTime + STATUS_PILL_REFRESH_INTERVAL;
            RefreshHeaderTitle();
            UpdateStatusPill();
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
/// Vertical split-bar handle. Drags horizontally to move the boundary between the
/// AIChatPanel's left media panel and right text panel. Calls back into
/// AIChatPanel.ApplySplit which handles clamping and re-laying-out both halves.
/// </summary>
public class ChatSplitterHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private AIChatPanel _panel;
    private RectTransform _bodyRT;
    private Vector2 _startPointerLocal;
    private float _startSplitX;

    public void SetTarget(AIChatPanel panel, RectTransform bodyRT)
    {
        _panel = panel;
        _bodyRT = bodyRT;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_bodyRT == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _bodyRT, eventData.position, eventData.pressEventCamera, out _startPointerLocal);
        // The body's pivot is (0.5, 0.5) by default but our splitter is anchored from
        // the left edge with pivot (0,0.5), so its anchoredPosition.x already equals
        // the absolute X-from-left. Read it as our drag baseline.
        var splitterRT = (RectTransform)transform;
        _startSplitX = splitterRT.anchoredPosition.x;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_bodyRT == null || _panel == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _bodyRT, eventData.position, eventData.pressEventCamera, out var nowLocal))
            return;

        // Body pivot is centred; the delta in local space is the same regardless of
        // pivot, so we can just add it to our left-edge-anchored start position.
        float deltaX = nowLocal.x - _startPointerLocal.x;
        _panel.ApplySplit(_startSplitX + deltaX);
    }
}

/// <summary>
/// Edge/corner resize handle for a panel. Drags adjust the target's sizeDelta and move
/// the target so the opposite edge stays fixed. Min size enforced.
/// </summary>
public class PanelResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _target;
    private Vector2 _minSize = new Vector2(200, 200);
    private Vector2 _resizeDirection = new Vector2(1f, -1f);
    private Action _onResized;
    private Vector2 _startPointerLocal;
    private Vector2 _startSize;
    private Vector2 _startAnchoredPosition;

    public void SetTarget(RectTransform target, Vector2 minSize)
    {
        SetTarget(target, minSize, new Vector2(1f, -1f), null);
    }

    public void SetTarget(RectTransform target, Vector2 minSize, Vector2 resizeDirection, Action onResized = null)
    {
        _target = target;
        _minSize = minSize;
        _resizeDirection = resizeDirection;
        _onResized = onResized;
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
        _startAnchoredPosition = _target.anchoredPosition;
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

        // Movement in parent-local coords. Direction maps pointer movement to growth:
        // right=(1,0), left=(-1,0), top=(0,1), bottom=(0,-1), bottom-right=(1,-1).
        Vector2 delta = nowLocal - _startPointerLocal;
        Vector2 newSize = _startSize + new Vector2(delta.x * _resizeDirection.x, delta.y * _resizeDirection.y);
        newSize.x = Mathf.Max(_minSize.x, newSize.x);
        newSize.y = Mathf.Max(_minSize.y, newSize.y);

        Vector2 sizeChange = newSize - _startSize;
        Vector2 newAnchoredPosition = _startAnchoredPosition;
        Vector2 pivot = _target.pivot;
        if (_resizeDirection.x > 0f)
            newAnchoredPosition.x += pivot.x * sizeChange.x;
        else if (_resizeDirection.x < 0f)
            newAnchoredPosition.x -= (1f - pivot.x) * sizeChange.x;

        if (_resizeDirection.y > 0f)
            newAnchoredPosition.y += pivot.y * sizeChange.y;
        else if (_resizeDirection.y < 0f)
            newAnchoredPosition.y -= (1f - pivot.y) * sizeChange.y;

        _target.sizeDelta = newSize;
        _target.anchoredPosition = newAnchoredPosition;
        _onResized?.Invoke();
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

/// <summary>
/// ScrollRect subclass that suppresses its own vertical scroll while Ctrl is held, so
/// Ctrl+MouseWheel can be used as a font-resize gesture (handled by AIChatPanel.Update())
/// without simultaneously scrolling the conversation.
/// </summary>
public class ChatScrollRectCtrlAware : ScrollRect
{
    public override void OnScroll(PointerEventData data)
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            return;
        base.OnScroll(data);
    }
}

/// <summary>
/// Forwards mouse-wheel scroll events to a target ScrollRect. We attach this to each
/// chat bubble's TMP_InputField GameObject because TMP_InputField itself implements
/// IScrollHandler (for in-field scrolling), which otherwise swallows the wheel event
/// before it can reach the parent ScrollRect. Both handlers fire on the same GameObject
/// when the EventSystem dispatches IScrollHandler, so this safely runs alongside
/// TMP_InputField.OnScroll without interfering with text editing.
/// </summary>
public class ChatScrollForwarder : MonoBehaviour, IScrollHandler
{
    public ScrollRect target;

    public void OnScroll(PointerEventData data)
    {
        if (target != null) target.OnScroll(data);
    }
}
