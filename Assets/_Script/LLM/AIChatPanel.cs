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
///   1. Tries LLMInstanceManager.GetFreeLLM/GetLeastBusyLLM (big job; vision when
///      the history carries pasted image data).
///   2. Falls back to LLMSettingsManager.GetActiveProvider/GetProviderSettings.
/// </summary>
public class AIChatPanel : MonoBehaviour, IChatHost
{
    private static AIChatPanel _instance;
    private static GameObject _panelRoot;

    // ---- Skills system: system prompt + LLM-callable actions ----
    // Created in CreateUI(); torn down with the panel. SkillManager loads aichat/skills/*.md
    // and aichat/main_prompt.txt; ChatContextBuilder builds the STABLE system prompt
    // (cache-friendly - it only changes when prompt/skill files change) plus the
    // volatile CURRENT STATE block (GPU busy/idle, chat-image captions) that gets
    // appended to each outgoing user message at send time; SkillActionParser extracts
    // <aitools_action> tags from the LLM's stream; SkillActionExecutor dispatches them
    // to the rest of the app (PicMain.RunPresetByName, LLM delegation, etc.).
    private SkillManager _skillManager;
    private ChatContextBuilder _contextBuilder;
    private SkillActionParser _actionParser;
    private SkillActionExecutor _actionExecutor;
    private readonly HashSet<string> _stickyAutoloadSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private const string AUTOLOAD_SKILL_CONTEXT_TAG = "aichat_autoload_skill_context";

    // Auto-loaded skill bodies are large (150-500 lines each) and used to pile up
    // forever - every skill whose trigger ever fired stayed pinned for the rest of the
    // session, easily dragging 800+ lines of overlapping instructions into a small
    // model's context. We now keep only the most-recently-triggered few. _autoloadLru
    // is oldest-first; re-triggering a skill moves it to the end. PinnedAutoloadSkillId
    // (the roleplay spine) is never evicted because mid-story turns still need it even
    // when the user's latest line is just "now make it night".
    private readonly List<string> _autoloadLru = new List<string>();
    private const int MaxAutoloadSkillBodies = 3;
    private const string PinnedAutoloadSkillId = "scenario_storytelling";

    // Header status pill - GPU busy count + LLM count, refreshed periodically.
    private TextMeshProUGUI _statusPillText;
    private float _statusPillNextRefresh;
    private const float STATUS_PILL_REFRESH_INTERVAL = 1.5f;

    // Per-turn attachments: a defensive copy of the user's pasted images at OnSendClicked
    // time, so a SkillActionExecutor invoked mid-stream can still resolve attachment="N"
    // even after ChatImageAttachmentZone has cleared its own thumbnail strip.
    private List<byte[]> _lastTurnAttachments = new List<byte[]>();

    // Tracks "Info" bubbles (warnings/notes from skill execution, etc.) so that on
    // the user's NEXT send we can quietly recap any messages the LLM hasn't already
    // seen - giving it a chance to learn from its own mistakes without forcing the
    // user to copy-paste them. Bubbles authored as pure UI confirmations (e.g.
    // "New chat", "Conversation cleared") opt out via includeInLLMRecap=false at
    // the AddSystemMessage call site. Cleared with the rest of the chat in
    // OnClearClicked so a fresh conversation starts with no carry-over.
    private class InfoMessage
    {
        public string m_text;
        public bool m_alreadySentToLLM;

        public InfoMessage(string text)
        {
            m_text = text;
            m_alreadySentToLLM = false;
        }
    }
    private readonly List<InfoMessage> _infoMessages = new List<InfoMessage>();

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

    // Character-anchor registry: maps a character NAME ("Bob") to the PicMain that is
    // currently its canonical anchor. Stores the Pic REFERENCE, not a slot number,
    // because chat_image numbers shift downward whenever TrimMediaToKeepLastN pops old
    // bubbles off the head of _chatImagePics - a name must keep pointing at the right
    // image even after a renumber. Declared via anchor="Name" on a generate_image /
    // image_to_image action; re-declaring an existing name re-points it (the "Bob
    // changed clothes" update path). Cleared with the chat; dead entries pruned on trim.
    private readonly Dictionary<string, PicMain> _anchors = new Dictionary<string, PicMain>(StringComparer.OrdinalIgnoreCase);

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

    // True when a fresh (unchained) spawn was attempted but has NOT succeeded yet (in
    // progress, deferred, or FAILED). Set at the start of each unchained spawn via
    // MarkChainTargetStale(); cleared the instant a spawn succeeds (SetLastSpawnedPicForTurn).
    // While set, PeekChainTarget/ConsumeChainTarget return null so a chained decorator after
    // a FAILED base (e.g. a bad-preset image_to_image) errors instead of stacking onto - and
    // corrupting - the previous page's Pic.
    private bool _chainTargetStale;

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
    // Whether to ship the user's dragged/pasted images as base64 to the active
    // LLM session. Off by default: only a one-line caption (computed by a
    // separate one-shot vision call) plus dimensions ride along with the
    // user message, and the raw bytes never enter chat history.
    private const string PREFS_INCLUDE_IMAGE_DATA = "aichat_include_image_data";
    // Cap on the largest edge (in pixels) of dragged/pasted images. Anything
    // bigger gets bilinear-downscaled at attach time so that captioning,
    // image_to_image source bytes, and chat-history embedding all run against
    // a sane payload. 0 = no resize.
    private const string PREFS_ATTACHMENT_MAX_EDGE = "aichat_attachment_max_edge";
    private const int DEFAULT_ATTACHMENT_MAX_EDGE = 1024;
    // Auto-continue: when the user clicks Send with no input (the "(continue)"
    // path) and the Auto toggle is on, fire up to N additional Continue turns
    // automatically. The toggle and N value live in PlayerPrefs so they stick
    // across sessions.
    private const string PREFS_AUTO_CONTINUE_ON = "aichat_auto_continue_on";
    private const string PREFS_AUTO_CONTINUE_COUNT = "aichat_auto_continue_count";
    private const int DEFAULT_AUTO_CONTINUE_COUNT = 10;
    // Compact: how many of the most recent user->assistant exchanges to keep
    // verbatim when the user compacts the chat (either by plain truncation or
    // by summarizing everything older into one message). Shared by both modes.
    private const string PREFS_COMPACT_KEEP_N = "aichat_compact_keep_n";
    private const int DEFAULT_COMPACT_KEEP_N = 5;

    // Footer
    private TMP_InputField _inputField;
    private TMPInputFieldUndo _inputUndo;
    private RectTransform _inputFieldRT;
    private Button _sendButton;
    private Button _clearButton;
    private Button _stopButton;
    private Button _copyButton;
    private Toggle _includeImageDataToggle;
    private Toggle _autoContinueToggle;
    private TMP_InputField _autoContinueCountInput;
    // Internal countdown for the auto-continue burst. Set from the N field
    // when the user manually clicks Send with Auto on; decremented after each
    // successful turn and re-fires another Send. Reset on Stop/Clear/abort
    // (or when the user turns Auto off mid-burst).
    private int _autoContinueRemaining = 0;
    // Latch so re-entering OnSendClicked from inside an auto-fire does NOT
    // reset _autoContinueRemaining back to N.
    private bool _autoContinueFiring = false;
    private TextMeshProUGUI _statusText;

    // Image attachments (drag-drop / clipboard paste) - all the heavy lifting (drop
    // intercept, paste-from-clipboard, thumbnail strip UI) lives in ChatImageAttachmentZone.
    // We just own the strip container's RectTransform plus the footer/chat-area rects so
    // we can resize them when the strip appears/disappears.
    private ChatImageAttachmentZone _attachmentZone;
    private RectTransform _attachmentsStrip;
    private RectTransform _footerRT;
    private const float ATTACHMENT_STRIP_HEIGHT = 70f;

    // Watchdog timeout for vision-LLM caption requests. Local Ollama / llama.cpp
    // models occasionally hang on a particular input or after long uptime; without
    // a force-release the LLM slot stays marked busy forever and the user can't
    // get the slot back. After this timeout we decrement the busy count and treat
    // the caption as failed.
    private const float CAPTION_TIMEOUT_SECONDS = 60f;

    // Watchdog timeout for the one-shot "compact to summary" LLM request. Longer
    // than the caption timeout because it digests the whole conversation.
    private const float COMPACT_TIMEOUT_SECONDS = 180f;
    // Guards against overlapping compact-summary requests (the button is in the
    // settings panel, which can be reopened while one is still in flight).
    private bool _compactSummaryInFlight;
    // Compact-summary progress readout (spinner + elapsed in the status line).
    // The settings panel closes itself after the click, so the chat status line
    // is the only place the user can watch the request work.
    private float _compactSummaryStartTime = 0f;
    private int _compactSummaryMsgCount = 0;
    private float _compactStatusNextRefresh = 0f;
    private int _compactSpinnerStep = 0;
    // Set while a compact-summary is in flight; invoking it flips the request's
    // done-latch so a late HTTP response is discarded. Clear (which resets the
    // whole conversation) uses this so the summary can't resurrect old history.
    private Action _compactSummaryCancel;

    private class CaptionJob
    {
        // Mutual-exclusion latch between three completion paths: onDone (HTTP
        // returned), watchdog (timeout), and OnCaptionCancelled (user X'd it).
        // Whichever wins flips `completed`; the others become no-ops so we
        // never decrement the busy count twice (which could steal a slot from
        // a different task that has since been allocated to the same LLM).
        public bool completed;
        public bool cancelled;
        public int targetId = -1;
        public int replicaIndex;
        public Coroutine watchdog;
    }

    // Outstanding caption jobs keyed by attachment id. Populated when an attachment
    // arrives, drained either by completion or by the user clicking the X.
    private readonly Dictionary<int, CaptionJob> _captionJobs = new Dictionary<int, CaptionJob>();

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
    // Wall-clock when the FIRST chunk of this turn arrived (0 = still waiting).
    // Generation t/s is measured from here, not from _streamStartTime - otherwise
    // a long prefill (10s+ on big contexts) drags the displayed TPS way down even
    // though the model is generating at full speed once it starts.
    private float _streamFirstTokenTime = 0f;
    // Approx size (chars) of the prompt we sent this turn, so the prefill phase
    // can show an estimated prompt-token count and prefill speed.
    private int _streamPromptApproxChars = 0;
    // Best-known total context window (tokens) for the provider serving this turn,
    // 0 if unknown. Lets the status line show context fill as "ctx ~33k/131k".
    private int _streamMaxContextTokens = 0;
    // llama.cpp /props lookups (server address -> loaded n_ctx). Cached per app run
    // so each server is only probed once; the in-flight set stops duplicate probes.
    private static readonly Dictionary<string, int> _llamaCppCtxCache = new Dictionary<string, int>();
    private static readonly HashSet<string> _llamaCppCtxProbesInFlight = new HashSet<string>();
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
    // 168 (not 156): the bottom "Auto" row reaches ~152px below the footer top,
    // and the 10px resize edge band sits in the footer's bottom 10px. At 156 the
    // band clipped the lower third of the Auto toggle / N box; 168 gives the row
    // ~6px clearance above the band.
    private const float FOOTER_HEIGHT = 168f;
    private const float FOOTER_DRAG_BAR_HEIGHT = 10f;
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
    // Visible tint for the four edge-resize bars (the outer 10px band). Lighter and
    // cooler than the move frame so the two zones read as distinct without shouting.
    private static readonly Color ResizeEdgeColor = new Color(0.40f, 0.60f, 0.78f, 0.55f);

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
        AddSystemMessage($"New chat. {loadedSkills} skill{(loadedSkills == 1 ? "" : "s")} loaded from aichat/skills/. Conversation history is kept until you click Clear or close the app.", includeInLLMRecap: false);
        AddPromptConfigNotice();

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

        // Status pill: shows a compact "GPUs 1/2 · LLMs 1/4" (busy/total for GPUs,
        // active calls/total capacity for LLMs) so the user can see render and LLM
        // load at a glance. LLM capacity = sum over enabled instances of
        // (maxConcurrentTasks x replicas). Refreshed every 1.5s in Update() while the
        // panel is visible.
        var pillObj = new GameObject("StatusPill");
        pillObj.transform.SetParent(header.transform, false);
        var pillRt = pillObj.AddComponent<RectTransform>();
        pillRt.anchorMin = new Vector2(1, 0.5f);
        pillRt.anchorMax = new Vector2(1, 0.5f);
        pillRt.pivot = new Vector2(1, 0.5f);
        pillRt.sizeDelta = new Vector2(118, 20);
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
        _statusPillText.text = "GPUs -/- · LLMs -/-";
        _statusPillText.font = _font;
        _statusPillText.fontSize = 11;
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
        // Inset from the panel's right edge by the resize-strip width. The ResizeRight
        // handle is a later sibling, so it wins raycasts over anything in its 10px
        // column - flush against the edge, the chat scrollbar's handle was completely
        // under it and couldn't be grabbed (the cursor flipped to the resize arrows).
        _chatPanelRT.offsetMax = new Vector2(-RESIZE_EDGE_THICKNESS, 0);
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
        // MultiLineNewline: Enter naturally inserts a newline. LateUpdate() then removes
        // the just-inserted '\n' and defer-sends when Shift is NOT held. (TMP's built-in
        // MultiLineSubmit mode is supposed to do Shift+Enter newline natively, but
        // Shift+Enter doesn't actually insert a newline in Unity 6 / TMP 3, so we
        // handle it ourselves.)
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

        // Note: Enter / Shift+Enter handling is in LateUpdate() below. Using onValidateInput
        // is unreliable because Input.GetKey(Shift) can return false from inside that
        // callback (it runs during TMP's text-event processing, not the regular Update
        // phase). Detecting in LateUpdate reads shift state when it's guaranteed valid
        // AND runs after TMP has already consumed the keystroke.

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

        // Row 3: "Include image data" checkbox - mirrors the Send/Stop/Clear layout
        // beneath them. Default is OFF: dragged/pasted images are captioned by a
        // separate one-shot vision call and only the caption rides along with the
        // user message, instead of shipping the raw base64 every turn.
        _includeImageDataToggle = CreateFooterToggle(
            footer.transform,
            "Include image data",
            new Vector2(-8, -104),
            new Vector2(186, 22),
            GetIncludeImageData(),
            v => SetIncludeImageData(v));
        {
            var tt = _includeImageDataToggle.gameObject.AddComponent<RTToolTip>();
            tt._text = "If checked, raw image bytes are fed into the main chat context every turn - usually a bad idea and wasteful of tokens. If unchecked (recommended), a separate vision call 'looks' at each attached image once and only its description is added to the conversation.";
        }

        // Row 4: "Auto" toggle on the left + small numeric N input on the right.
        // When Auto is on, hitting Send fires the manual turn then automatically
        // fires up to N additional "(continue)" turns once each one completes.
        // Stop, Clear, an aborted turn, or toggling Auto off cancels the burst.
        _autoContinueToggle = CreateFooterToggle(
            footer.transform,
            "Auto",
            new Vector2(-72, -130),
            new Vector2(122, 22),
            GetAutoContinueEnabled(),
            v =>
            {
                SetAutoContinueEnabled(v);
                if (!v) _autoContinueRemaining = 0;
            });
        {
            var tt = _autoContinueToggle.gameObject.AddComponent<RTToolTip>();
            tt._text = "When enabled, after you press Send the chat automatically fires up to N additional '(continue)' turns - one per completed reply - to keep the LLM going on long tasks. Stop, Clear, an aborted turn, or toggling this off cancels the remaining burst. N is the number in the field to the right.";
        }
        _autoContinueCountInput = CreateFooterIntInput(
            footer.transform,
            new Vector2(-8, -130),
            new Vector2(60, 22),
            GetAutoContinueCount(),
            v => SetAutoContinueCount(v));

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
            stripHeight: ATTACHMENT_STRIP_HEIGHT,
            // Live-read the cap so the user can change it in settings without
            // having to reopen the chat panel; takes effect on the next drop.
            maxEdgeProvider: GetAttachmentMaxEdge);
        _attachmentZone.OnAttachmentsChanged += OnAttachmentsChanged;
        _attachmentZone.OnAttachmentAdded += OnAttachmentAdded;
        _attachmentZone.OnCaptionCancelled += OnCaptionCancelled;

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

    /// <summary>
    /// Build a checkbox-style Toggle anchored to the top-right of <paramref name="parent"/>,
    /// matching the visual style of <see cref="CreateFooterButton"/> so the row 3
    /// "Include image data" toggle reads as part of the same control cluster. Box on
    /// the left, label on the right. <paramref name="onChanged"/> fires for both user
    /// clicks and programmatic SetIsOnWithoutNotify - callers are responsible for
    /// PlayerPrefs persistence inside the callback.
    /// </summary>
    private Toggle CreateFooterToggle(Transform parent, string label, Vector2 anchoredPos, Vector2 size, bool initialOn, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var go = new GameObject("Toggle_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        // Box (target graphic) sits on the left side of the row.
        const float boxSize = 16f;
        var boxGo = new GameObject("Box");
        boxGo.transform.SetParent(go.transform, false);
        var boxRt = boxGo.AddComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(0, 0.5f);
        boxRt.anchorMax = new Vector2(0, 0.5f);
        boxRt.pivot = new Vector2(0, 0.5f);
        boxRt.sizeDelta = new Vector2(boxSize, boxSize);
        boxRt.anchoredPosition = new Vector2(0, 0);
        var boxImg = boxGo.AddComponent<Image>();
        boxImg.color = Color.white;

        // Checkmark graphic - shown when the toggle is on.
        var checkGo = new GameObject("Check");
        checkGo.transform.SetParent(boxGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(2, 2);
        checkRt.offsetMax = new Vector2(-2, -2);
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.text = "X";
        checkTmp.fontSize = 14;
        checkTmp.fontStyle = FontStyles.Bold;
        checkTmp.color = TextTitle;
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.raycastTarget = false;

        // Label to the right of the box.
        var lblGo = new GameObject("Label");
        lblGo.transform.SetParent(go.transform, false);
        var lblRt = lblGo.AddComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0, 0);
        lblRt.anchorMax = new Vector2(1, 1);
        lblRt.offsetMin = new Vector2(boxSize + 6, 0);
        lblRt.offsetMax = Vector2.zero;
        var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
        lblTmp.font = _font;
        lblTmp.text = label;
        lblTmp.fontSize = 12;
        lblTmp.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
        // Raycast on so hovering the label propagates PointerEnter up to the
        // toggle root for any RTToolTip mounted there (and clicks bubble up to
        // the Toggle's IPointerClickHandler, matching standard checkbox UX).
        lblTmp.raycastTarget = true;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = checkTmp;
        toggle.isOn = initialOn;
        toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    /// <summary>
    /// Small integer input field, anchored top-right of <paramref name="parent"/>
    /// using the same conventions as <see cref="CreateFooterButton"/>. Fires
    /// <paramref name="onChanged"/> on end-of-edit with the parsed value (clamped
    /// to &gt;= 0); caller is responsible for PlayerPrefs persistence.
    /// </summary>
    private TMP_InputField CreateFooterIntInput(Transform parent, Vector2 anchoredPos, Vector2 size, int initialValue, UnityEngine.Events.UnityAction<int> onChanged)
    {
        var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "Input_Int";
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        if (img != null) img.color = InputFieldBg;

        var input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        input.textComponent.color = TextDark;
        input.textComponent.font = _font;
        input.textComponent.fontSize = 12;
        if (input.placeholder is TextMeshProUGUI ph)
        {
            ph.text = "N";
            ph.font = _font;
            ph.fontSize = 12;
            ph.color = TextPlaceholder;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
        }
        input.text = initialValue.ToString();
        // Same fat-caret treatment as the main chat input / settings-dialog fields,
        // otherwise TMP renders a near-invisible 1px caret in this little box.
        ApplyFatCaret(input);
        var caretFixer = input.gameObject.AddComponent<AIChatCaretFixer>();
        caretFixer.Set(input);
        input.onEndEdit.AddListener(s =>
        {
            int parsed;
            if (!int.TryParse(s, out parsed) || parsed < 0) parsed = 0;
            onChanged?.Invoke(parsed);
        });
        return input;
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
    /// Fired by ChatImageAttachmentZone for each new attachment. We pre-emptively
    /// caption the bytes against any vision LLM so the result is in hand by the
    /// time the user clicks Send. While the caption is in flight the attachment
    /// stays marked, and Send is disabled via RecomputeSendInteractable.
    /// </summary>
    private void OnAttachmentAdded(ChatImageAttachmentZone.AttachmentInfo info)
    {
        if (_attachmentZone == null) return;
        int id = info.id;
        // Reflect the new in-flight caption count on the Send button immediately.
        RecomputeSendInteractable();
        var job = TryCaptionBytes(info.bytes, result =>
        {
            _captionJobs.Remove(id);
            // SetCaption is keyed by id, so it's safe even if earlier attachments
            // were removed and the visible index has shifted in the meantime.
            if (_attachmentZone != null)
                _attachmentZone.SetCaption(id, result.shortCaption, result.longCaption);
        });
        if (job != null) _captionJobs[id] = job;
    }

    /// <summary>
    /// Fired by ChatImageAttachmentZone when the user X'd an attachment whose
    /// caption was still in flight. Free the LLM busy slot immediately so it
    /// can be reused for the next message - we have no way to abort the HTTP
    /// request itself, but we can stop pretending to wait for it. The job's
    /// completed/cancelled latches make sure the eventual onDone (or the
    /// watchdog) is a no-op so we don't double-decrement the busy count.
    /// </summary>
    private void OnCaptionCancelled(int attachmentId)
    {
        if (!_captionJobs.TryGetValue(attachmentId, out var job)) return;
        _captionJobs.Remove(attachmentId);
        if (job.completed) return;  // race: onDone or watchdog already finished it
        job.cancelled = true;
        job.completed = true;
        if (job.watchdog != null)
        {
            try { StopCoroutine(job.watchdog); } catch { /* coroutine may already be done */ }
            job.watchdog = null;
        }
        if (job.targetId >= 0)
        {
            var instanceMgr = LLMInstanceManager.Get();
            instanceMgr?.SetLLMBusy(job.targetId, job.replicaIndex, false);
        }
    }

    /// <summary>
    /// Recompute Send button interactability from both the streaming flag AND
    /// the count of in-flight attachment captions. Call this whenever either
    /// signal can change (SetBusyUI, OnAttachmentsChanged, OnAttachmentAdded).
    /// </summary>
    private void RecomputeSendInteractable()
    {
        if (_sendButton == null) return;
        bool captionsPending = _attachmentZone != null && _attachmentZone.CountInFlightCaptions() > 0;
        _sendButton.interactable = !_isStreaming && !captionsPending;
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
        // Caption may have just arrived (or an attachment was removed); refresh Send.
        RecomputeSendInteractable();

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
        // Full-length edges (no corner inset). The corner regions are covered by invisible
        // diagonal-resize caps below, plus the visible bottom-right ResizeGrip on top.
        CreateResizeEdgeHandle(
            "ResizeTop",
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, RESIZE_EDGE_THICKNESS),
            Vector2.zero,
            new Vector2(0f, 1f));
        CreateResizeEdgeHandle(
            "ResizeBottom",
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, RESIZE_EDGE_THICKNESS),
            Vector2.zero,
            new Vector2(0f, -1f));
        CreateResizeEdgeHandle(
            "ResizeLeft",
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(RESIZE_EDGE_THICKNESS, 0f),
            Vector2.zero,
            new Vector2(-1f, 0f));
        CreateResizeEdgeHandle(
            "ResizeRight",
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(RESIZE_EDGE_THICKNESS, 0f),
            Vector2.zero,
            new Vector2(1f, 0f));

        // Invisible diagonal-resize caps for the three corners that don't have a styled
        // grip widget. They sit on top of the now-full-length edges, so the blue border
        // reads as continuous while the corner pixels still resize on both axes at once.
        CreateResizeCornerCap("ResizeCornerTopLeft",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-1f,  1f));
        CreateResizeCornerCap("ResizeCornerTopRight",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2( 1f,  1f));
        CreateResizeCornerCap("ResizeCornerBottomLeft",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(-1f, -1f));

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
        img.color = ResizeEdgeColor;

        var resize = edge.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), resizeDirection, OnPanelResized);
    }

    /// <summary>
    /// Invisible 16x16 cap parked at a panel corner. Acts as a diagonal-resize hot zone -
    /// pointer events go to this cap (highest sibling at the corner), so the cursor swap
    /// and the resize direction read as diagonal. The continuous blue look comes from the
    /// full-length edge handles rendered underneath, not from this cap.
    /// </summary>
    private void CreateResizeCornerCap(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 resizeDirection)
    {
        var corner = new GameObject(name);
        corner.transform.SetParent(_mainPanel, false);
        var rt = corner.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(RESIZE_CORNER_SIZE, RESIZE_CORNER_SIZE);
        rt.anchoredPosition = Vector2.zero;

        // Image is required for raycast targeting, but fully transparent so the edge
        // colors underneath define the visible look.
        var img = corner.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);

        var resize = corner.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), resizeDirection, OnPanelResized);
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
            // Reverse the display-only escapes ConvertMarkdownToTMP applied to the
            // bubble text (fullwidth '＜' / '＞' substitution). Without this, when
            // the user edits an assistant bubble - or even when the bubble loses
            // focus after EnableBubbleEditing flips it to readOnly=false - the
            // bubble's CURRENT (display-escaped) text gets written back into the
            // GTPChatLine, then sent to the LLM verbatim on the next turn, which
            // makes the LLM start mimicking '＜aitools' fullwidth syntax instead of
            // the real '<aitools_action' tags.
            string raw = ReverseTmpDisplayEscapes(text ?? "");
            string clean = OpenAITextCompletionManager.RemoveTMPTagsFromString(raw);
            interaction._content = clean;
        });
    }

    /// <summary>
    /// Reverse the display-only character substitutions that ConvertMarkdownToTMP
    /// applies to bubble text. Currently: fullwidth '＜' (U+FF1C) and '＞' (U+FF1E)
    /// back to ASCII '<' / '>'. Used when bubble text needs to be persisted as
    /// raw chat history (e.g. user-edited assistant bubbles).
    /// </summary>
    private static string ReverseTmpDisplayEscapes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace('\uFF1C', '<').Replace('\uFF1E', '>');
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

    private void AddSystemMessage(string text, bool includeInLLMRecap = true)
    {
        // Info / system bubbles aren't part of the LLM conversation, so leave them readOnly
        // (linkedInteraction = null).
        AppendBubble("Info", new Color(0.35f, 0.35f, 0.45f), text, new Color(0.92f, 0.92f, 0.95f, 1f));

        // Queue this message for the "for the future, please keep this in mind"
        // recap that gets quietly appended to the user's NEXT outgoing message.
        // Pure UI confirmations / bail-path errors (config not initialized, etc.)
        // pass false so they don't pollute the LLM's reminder list.
        if (includeInLLMRecap && !string.IsNullOrWhiteSpace(text))
            _infoMessages.Add(new InfoMessage(text));
    }

    // Drops a local-only "Info" bubble naming the active preset prefix and the exact
    // prompt files in play (resolving any test_ overrides). Shown on the reset/init
    // events (new chat, Clear, settings/preset change) so the user can confirm a
    // renamed prompt was picked up. includeInLLMRecap:false -> never seen by the LLM.
    private void AddPromptConfigNotice()
    {
        if (_skillManager == null) return;
        AddSystemMessage(_skillManager.BuildActivePromptStatus(), includeInLLMRecap: false);
    }

    /// <summary>
    /// Wrap the user's just-typed message with a quiet "for the future" recap of any
    /// Info bubbles that have appeared since the last send (typically skill warnings
    /// or auto-corrections from the assistant's previous turn). The recap is what the
    /// LLM sees in the user message; the human-visible bubble keeps the original text.
    /// Each recapped entry is marked sent so it never gets attached twice. If nothing
    /// is pending the original text is returned verbatim - behaviour is unchanged for
    /// chats that don't accumulate Info bubbles.
    /// </summary>
    private string BuildLLMPayloadWithInfoRecap(string userTypedText)
    {
        if (_infoMessages == null || _infoMessages.Count == 0)
            return userTypedText;

        var unsent = new List<InfoMessage>();
        for (int i = 0; i < _infoMessages.Count; i++)
        {
            if (!_infoMessages[i].m_alreadySentToLLM)
                unsent.Add(_infoMessages[i]);
        }
        if (unsent.Count == 0)
            return userTypedText;

        var sb = new StringBuilder();
        sb.Append(userTypedText ?? "");
        sb.Append("\n\n---\nAlso, for the future, please keep this in mind:");
        for (int i = 0; i < unsent.Count; i++)
        {
            sb.Append("\n- ").Append(unsent[i].m_text);
            unsent[i].m_alreadySentToLLM = true;
        }
        return sb.ToString();
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
            // Replace LITERAL angle brackets in the input with their Unicode fullwidth
            // equivalents (U+FF1C, U+FF1E). TMP's rich-text parser ALWAYS scans for
            // <tag> patterns and crashes hard (IndexOutOfRangeException in
            // TMP_Text.ValidateHtmlTag) on unrecognised tag-shaped content like
            // "<aitools_action skill=...>", List<int>, raw XML, code samples, etc.
            // A zero-width space immediately after '<' is NOT enough - TMP keeps
            // walking forward looking for '>'. Substituting the chars entirely is the
            // only reliable fix. Fullwidth '＜' / '＞' look visually like '<' / '>'
            // (slightly wider, monospace-style); the user can still copy-paste and
            // read the content. Done BEFORE markdown expansion so our own injected
            // tags (<b>, <i>, <size=...>, <color=...>, <font=...>, <mark=...>) below
            // use FRESH ASCII '<' / '>' chars from string literals and ARE recognised
            // by TMP. The ORIGINAL text (with real '<>') is what reaches the LLM -
            // AddSystemInjectionAndBubble queues the raw string into the info recap
            // before this display-only path runs, so the LLM still sees real angle
            // brackets in its context.
            text = text.Replace('<', '\uFF1C').Replace('>', '\uFF1E');

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
        // A compact-summary will ReplaceInteractions() when it lands; letting a new
        // turn start mid-flight means that replace silently throws the turn away.
        if (_compactSummaryInFlight)
        {
            RTQuickMessageManager.Get().ShowMessage("Summarizing the conversation - wait for it to finish before sending");
            return;
        }
        // Guard the Enter-key path: the Send button is greyed via
        // RecomputeSendInteractable while attachment captions are pending, but
        // Enter bypasses the button. Show a hint and bail.
        if (_attachmentZone != null && _attachmentZone.CountInFlightCaptions() > 0)
        {
            AddSystemMessage("Captioning attached image(s)... waiting for description before send.", includeInLLMRecap: false);
            return;
        }

        // Manual click (not an auto-fire) seeds the auto-continue burst counter
        // from the Auto toggle + N field. Auto-fires re-enter through here too,
        // so the _autoContinueFiring latch keeps the counter from resetting
        // back to N every iteration.
        if (!_autoContinueFiring)
        {
            if (_autoContinueToggle != null && _autoContinueToggle.isOn)
                _autoContinueRemaining = GetAutoContinueCount();
            else
                _autoContinueRemaining = 0;
        }

        string text = _inputField != null ? _inputField.text : "";
        // Never send outer whitespace - stray newlines have leaked into the field via
        // the Enter-key race (see LateUpdate) and showed up as blank lines in the bubble.
        text = text.Trim();

        // Allow sending with images even if there's no text (vision models often work
        // better with a short prompt, but "describe this image" is a valid bare-image use).
        var attachmentInfos = _attachmentZone != null
            ? _attachmentZone.GetAttachmentInfo()
            : (IReadOnlyList<ChatImageAttachmentZone.AttachmentInfo>)System.Array.Empty<ChatImageAttachmentZone.AttachmentInfo>();
        int attachedCount = attachmentInfos.Count;
        if (string.IsNullOrWhiteSpace(text))
            text = attachedCount > 0 ? "(no caption)" : "(continue)";

        // Reset the per-turn chain target so a chain="true" action in this reply can
        // never accidentally stack onto a Pic spawned in some earlier turn. Both the
        // most-recent ref AND the LIFO stack need clearing.
        _lastSpawnedPicThisTurn = null;
        _unchainedPicsThisTurn.Clear();
        _chainTargetStale = false;
        // Reset the serial action scheduler in lockstep, and bump its turn
        // epoch so any deferred coroutine still alive from a prior turn bails
        // instead of spawning a stale page into this new turn.
        _actionExecutor?.ResetForNewTurn();

        // Build the visible attachment metadata block + (optionally) stage base64
        // images on the prompt manager. The block is appended to the user message
        // text so the LLM sees concrete dimensions + caption for each image on
        // THIS turn (the system prompt's CHAT IMAGES list catches up next turn,
        // but the user is asking about the just-attached image RIGHT NOW). Both
        // bubble and LLM see the same augmented text per the user's preference.
        _lastTurnAttachments.Clear();
        if (attachedCount > 0)
        {
            bool includeBytes = GetIncludeImageData();
            int firstChatIdx = _chatImagePics.Count + 1;
            var metadataBlock = new StringBuilder();
            for (int i = 0; i < attachedCount; i++)
            {
                var info = attachmentInfos[i];
                if (info.bytes == null) continue;
                int chatIdx = firstChatIdx + i;
                if (includeBytes)
                    _promptManager.AddPendingImage(System.Convert.ToBase64String(info.bytes), chatIdx);
                _lastTurnAttachments.Add(info.bytes);

                // Header line carries dimensions + the short label (if any).
                // The long description follows on its own indented line so the
                // LLM has the full ~200-word context for THIS turn without
                // visually drowning the user's typed message.
                metadataBlock.Append("[Attached Image #").Append(chatIdx);
                if (info.width > 0 && info.height > 0)
                    metadataBlock.Append(", ").Append(info.width).Append('x').Append(info.height);
                metadataBlock.Append(", PNG");
                if (!string.IsNullOrEmpty(info.captionShort))
                    metadataBlock.Append(" - ").Append(info.captionShort);
                metadataBlock.AppendLine("]");
                if (!string.IsNullOrEmpty(info.captionLong))
                    metadataBlock.AppendLine(info.captionLong);
            }
            string metadataText = metadataBlock.ToString().TrimEnd();
            if (metadataText.Length > 0)
                text = text + "\n\n" + metadataText;

            // Promote each attachment to a real PicMain. This makes the image persist in
            // the media column, gives the user a world Pic they can edit, AND registers it
            // in _chatImagePics so the LLM can reach it via chat_image="N" on this and all
            // future turns. Without this, ChatImageAttachmentZone.ClearAttachments below
            // would wipe the only UI surface holding the image, and SkillActionExecutor's
            // image_to_image validation would report no chat images on the next turn.
            // Pre-supplied caption is set on the PicMain synchronously so the next
            // turn's CHAT IMAGES block has it without re-running the caption coroutine.
            PromoteAttachmentsToChatImages(attachmentInfos);
            string mode = includeBytes ? "with image data" : "caption only";
            AddSystemMessage($"Attached {attachedCount} image{(attachedCount == 1 ? "" : "s")} to the next message ({mode}).", includeInLLMRecap: false);
        }

        // Quietly fold any unsent Info bubbles (skill warnings/errors that have piled
        // up since the last send) into the LLM payload as a "for the future, please
        // keep this in mind" recap, so the model can learn from its own mistakes
        // without forcing the user to copy-paste them. The user-visible bubble below
        // intentionally stays clean - it shows ONLY what the user actually typed -
        // while the prompt-manager history gets the augmented text. Marking each
        // recapped entry as already-sent prevents re-attaching it on subsequent turns.
        string llmPayloadText = BuildLLMPayloadWithInfoRecap(text);

        // Add the interaction first so we can link the bubble to it - that link is what
        // makes the bubble editable (and what makes user edits flow back into the prompt
        // history sent to the LLM on subsequent turns).
        _promptManager.AddInteraction("user", llmPayloadText);
        var userInteraction = _promptManager.GetLastInteraction();
        AddUserMessage(text, userInteraction);

        // Drop the staged thumbnails now that they've been baked into the conversation.
        if (attachedCount > 0)
            _attachmentZone?.ClearAttachments();

        _inputField.text = "";
        _inputUndo?.ResetHistory();
        FocusInputDeferred();

        SendChatTurn(text);
    }

    private void OnStopClicked()
    {
        if (!_isStreaming) return;
        _autoContinueRemaining = 0;
        TryCancelActiveRequests();
        FinalizeAssistantTurn(aborted: true);
        // Invalidate any parked pump / in-flight deferred coroutine so a
        // stopped book doesn't keep spawning pages after the user bailed.
        _actionExecutor?.ResetForNewTurn();
    }

    private void OnClearClicked()
    {
        _autoContinueRemaining = 0;
        // Discard any in-flight compact-summary; if its response landed after this
        // reset it would ReplaceInteractions() the old history right back in.
        _compactSummaryCancel?.Invoke();
        if (_isStreaming)
        {
            TryCancelActiveRequests();
            FinalizeAssistantTurn(aborted: true);
        }

        _promptManager.Reset();
        _stickyAutoloadSkillIds.Clear();
        _autoloadLru.Clear();
        _attachmentZone?.ClearAttachments();
        _lastTurnAttachments?.Clear();
        _chatImagePics?.Clear();
        _anchors?.Clear();
        _captionLabels?.Clear();
        _infoMessages.Clear();
        _actionParser?.Reset();
        _actionExecutor?.ResetForNewTurn();
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
        AddSystemMessage("Conversation cleared.", includeInLLMRecap: false);
        AddPromptConfigNotice();
    }

    // ---------------------------------------------------------------------
    // Compact: shrink a long conversation without deleting any images. Two
    // modes share one "keep last N exchanges" value:
    //   - Truncate : drop everything older than the last N exchanges.
    //   - Summarize: replace everything older with one LLM-written recap,
    //                keeping the last N exchanges verbatim.
    // Neither touches _chatImagePics or the media panel, so chat_image="N"
    // references in surviving messages stay valid.
    // ---------------------------------------------------------------------

    // Index into <paramref name="all"/> at which the "kept tail" begins: the
    // start of the keepExchanges-th-from-last user message. Returns 0 if the
    // conversation has fewer exchanges than that (keep everything).
    private int FindKeepFromIndex(List<GTPChatLine> all, int keepExchanges)
    {
        if (all == null || all.Count == 0) return 0;
        if (keepExchanges <= 0) return all.Count; // keep nothing verbatim
        int userSeen = 0;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i] != null && all[i]._role == "user")
            {
                userSeen++;
                if (userSeen >= keepExchanges)
                    return i;
            }
        }
        return 0;
    }

    // Turn a stored RAW assistant reply (with <aitools_action> tags + optional
    // <think> blocks) into the same display-safe text the live stream shows on
    // completion. A throwaway parser with no OnActionParsed subscriber strips /
    // sentinel-replaces the tags WITHOUT re-executing any skills; then the
    // same think-tag handling the stream uses is applied.
    private static string BuildDisplaySafeAssistantText(string rawContent)
    {
        if (string.IsNullOrEmpty(rawContent)) return rawContent;
        var p = new SkillActionParser();
        p.Feed(rawContent);            // no OnActionParsed listener -> parse only
        string display = p.Flush();    // full buffer -> tags replaced/removed
        return BuildVisibleStreamText(display ?? "");
    }

    // Tear down every chat bubble and recreate it from the (post-compact)
    // interaction history, relinking user/assistant bubbles to their live
    // GTPChatLine so inline editing still works.
    private void RebuildChatBubblesFromHistory()
    {
        if (_chatContent == null || _promptManager == null) return;

        for (int i = _chatContent.childCount - 1; i >= 0; i--)
            Destroy(_chatContent.GetChild(i).gameObject);

        // The pending-recap queue referenced Info bubbles that no longer exist;
        // clear it so a stale "for the future" block doesn't ride along on send.
        _infoMessages.Clear();

        foreach (var line in _promptManager.GetInteractionsList())
        {
            if (line == null || string.IsNullOrEmpty(line._content)) continue;
            if (line._role == "user")
            {
                AddUserMessage(line._content, line);
            }
            else if (line._role == "assistant")
            {
                // line._content keeps the RAW reply (with <aitools_action> tags) so
                // the LLM still sees its own prior actions. The bubble must show the
                // display-safe text, exactly like the live stream does on completion
                // (OnLLMCompletedCallback uses _actionParser.Flush() for the bubble
                // but stores the raw text in history). Without this, a rebuild after
                // Compact/edit leaks the raw markup into the chat.
                string display = BuildDisplaySafeAssistantText(line._content);
                if (!string.IsNullOrWhiteSpace(display))
                    AppendBubble("Assistant", new Color(0.10f, 0.45f, 0.20f), display, AssistantBubbleBg, line);
            }
            else
            {
                // Internal system context. Skip bulky autoloaded skill blocks;
                // surface the compact summary (and other plain notes) as Info.
                if (line._internalTag == AUTOLOAD_SKILL_CONTEXT_TAG) continue;
                AppendBubble("Info", new Color(0.35f, 0.35f, 0.45f), line._content, new Color(0.92f, 0.92f, 0.95f, 1f));
            }
        }
        StartCoroutine(ScrollToBottomDeferred());
    }

    private void DoCompactTruncate(int keepExchanges)
    {
        if (_promptManager == null) return;
        if (_isStreaming)
        {
            RTQuickMessageManager.Get().ShowMessage("Wait for the current reply to finish before compacting");
            return;
        }
        // The in-flight summary snapshotted the history it will ReplaceInteractions()
        // with; truncating now would just be undone (and resurrect the dropped lines)
        // when that snapshot lands.
        if (_compactSummaryInFlight)
        {
            RTQuickMessageManager.Get().ShowMessage("A compact-summary request is already running");
            return;
        }

        var all = _promptManager.GetInteractionsList();
        int from = FindKeepFromIndex(all, keepExchanges);
        if (from <= 0)
        {
            AddSystemMessage($"Nothing to compact - the conversation is already within the last {keepExchanges} exchange(s).", includeInLLMRecap: false);
            return;
        }

        var keptTail = all.GetRange(from, all.Count - from);
        _promptManager.ReplaceInteractions(keptTail);
        RebuildChatBubblesFromHistory();
        AddSystemMessage($"Compacted: removed {from} older message(s), kept the last {keepExchanges} exchange(s). All images are intact.", includeInLLMRecap: false);
    }

    private void DoCompactSummarize(int keepExchanges)
    {
        if (_promptManager == null) return;
        if (_compactSummaryInFlight)
        {
            RTQuickMessageManager.Get().ShowMessage("A compact-summary request is already running");
            return;
        }
        if (_isStreaming)
        {
            RTQuickMessageManager.Get().ShowMessage("Wait for the current reply to finish before compacting");
            return;
        }

        var all = _promptManager.GetInteractionsList();
        int from = FindKeepFromIndex(all, keepExchanges);
        if (from <= 0)
        {
            AddSystemMessage($"Nothing to compact - the conversation is already within the last {keepExchanges} exchange(s).", includeInLLMRecap: false);
            return;
        }

        var older = all.GetRange(0, from);
        var keptTail = all.GetRange(from, all.Count - from);

        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
        {
            AddSystemMessage("No LLM is configured, so the conversation can't be summarized. Use Truncate instead.", includeInLLMRecap: false);
            return;
        }
        int targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: false, out int replicaIndex);
        if (targetId < 0)
            targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: false, out replicaIndex);
        if (targetId < 0)
        {
            AddSystemMessage("No LLM slot is available right now. Try again shortly, or use Truncate.", includeInLLMRecap: false);
            return;
        }
        var inst = instanceMgr.GetInstance(targetId);
        if (inst == null || inst.settings == null)
        {
            AddSystemMessage("The selected LLM is not ready. Use Truncate instead.", includeInLLMRecap: false);
            return;
        }

        instanceMgr.SetLLMBusy(targetId, replicaIndex, true);
        _compactSummaryInFlight = true;
        _compactSummaryStartTime = Time.unscaledTime;
        _compactSummaryMsgCount = from;
        _compactStatusNextRefresh = 0f;
        _compactSpinnerStep = 0;

        int imageCount = _chatImagePics?.Count ?? 0;

        // Flatten the OLDER history into a single plain-text transcript carried by
        // one user message, rather than replaying it as multi-role chat messages.
        // We intentionally omit the chat's own base/roleplay system prompt so the
        // model summarizes rather than continuing in character.
        //
        // Flattening (instead of cloning each line back into the request) avoids two
        // llama.cpp-specific failure modes that otherwise produce an empty summary:
        //   1. llama.cpp applies the model's chat template strictly. Replaying an
        //      arbitrary system/user/assistant sequence - e.g. a prior compact
        //      summary stored as a second "system" line - trips templates that
        //      require a single leading system message and strict user/assistant
        //      alternation, and the server returns an empty completion.
        //   2. Clone() carries each line's attached images along as image_url
        //      content blocks; a text-only summarizer model rejects those.
        // A clean system->user pair with the history as text is template-proof and
        // behaves identically across every provider.
        var transcript = new StringBuilder();
        foreach (var line in older)
        {
            if (line == null || string.IsNullOrEmpty(line._content)) continue;
            string roleLabel = line._role == "user" ? "User"
                : line._role == "assistant" ? "Assistant"
                : "Note";
            transcript.Append(roleLabel).Append(": ").Append(line._content).Append("\n\n");
        }

        var lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system",
            "You are a precise conversation summarizer. You will be given the earlier portion of a chat. Produce a dense recap and nothing else."));
        string instruction =
            "Summarize the conversation so far into a concise but information-dense recap that a continuation of this chat can rely on. " +
            "Preserve: the user's goals; any decisions, rules or constraints agreed on; key facts established; and where things currently stand. " +
            "CRUCIALLY, for every generated or attached image referred to as chat_image=\"N\", keep a one-line note of what image #N depicts and any name/identity tied to it, so it can still be referenced later. " +
            "There are currently " + imageCount + " chat image(s). Output the recap only - no preamble and no sign-off.\n\n" +
            "Here is the earlier conversation to summarize:\n\n" + transcript.ToString();
        lines.Enqueue(new GTPChatLine("user", instruction));

        AddSystemMessage($"Compacting: summarizing {older.Count} older message(s) with the active LLM... the last {keepExchanges} exchange(s) and all images are kept.", includeInLLMRecap: false);

        bool done = false;
        Coroutine watchdog = null;
        int capId = targetId, capReplica = replicaIndex;

        Action release = () =>
        {
            if (done) return;
            done = true;
            if (watchdog != null) { try { StopCoroutine(watchdog); } catch { } }
            instanceMgr.SetLLMBusy(capId, capReplica, false);
            _compactSummaryInFlight = false;
            _compactSummaryCancel = null;
            // Hand the status line back; Update() stops repainting it the moment
            // the in-flight flag drops, so it would otherwise freeze mid-spinner.
            if (!_isStreaming && _statusText != null) _statusText.text = "Idle";
        };
        _compactSummaryCancel = release;

        Action<RTDB, JSONObject, string> onDone = (db, json, text) =>
        {
            if (done) return;
            release();

            string raw = (text ?? "").Trim();
            if (string.IsNullOrEmpty(raw) && json != null)
            {
                try { raw = OpenAITextCompletionManager.ExtractTextFromResponseJSON(json); } catch { }
            }
            if (GenerateSettingsPanel.GetStripThinkTags())
                raw = OpenAITextCompletionManager.RemoveThinkTagsFromString(raw ?? "");
            raw = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                // Surface the server's error payload (if any) so a template/role
                // rejection or rejected sampling param is diagnosable instead of a
                // bare "empty summary".
                string detail = "";
                if (json != null)
                {
                    try
                    {
                        string js = json.ToString();
                        if (!string.IsNullOrEmpty(js) && js.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                            detail = " Server said: " + (js.Length > 300 ? js.Substring(0, 300) + "..." : js);
                    }
                    catch { }
                }
                AddSystemMessage("Compact failed: the LLM returned an empty summary. History is unchanged." + detail, includeInLLMRecap: false);
                return;
            }

            var summaryLine = new GTPChatLine("system",
                "[Conversation summary - earlier history was compacted to save space]\n" + raw);
            var rebuilt = new List<GTPChatLine>(keptTail.Count + 1) { summaryLine };
            rebuilt.AddRange(keptTail);
            _promptManager.ReplaceInteractions(rebuilt);
            RebuildChatBubblesFromHistory();
            AddSystemMessage($"Compacted {older.Count} older message(s) into a summary. Kept the last {keepExchanges} exchange(s); all images are intact.", includeInLLMRecap: false);
            // release() above reset the status to Idle; leave the result on screen
            // instead, matching how finished turns keep their token stats visible.
            if (!_isStreaming && _statusText != null)
                _statusText.text = $"Summarized {older.Count} msgs in {Time.unscaledTime - _compactSummaryStartTime:F0}s";
        };

        watchdog = StartCoroutine(CompactSummaryWatchdog(() => done, release));
        SkillActionExecutor.DispatchOneShot(this, inst, lines, onDone, "CompactSummary", "compact_summary_sent.json");
    }

    private IEnumerator CompactSummaryWatchdog(Func<bool> isDone, Action release)
    {
        yield return new WaitForSeconds(COMPACT_TIMEOUT_SECONDS);
        if (isDone()) yield break;
        release();
        AddSystemMessage("Compact timed out - the LLM didn't return a summary in time. History is unchanged.", includeInLLMRecap: false);
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

    private void SendChatTurn(string latestUserMessage = null)
    {
        var settingsMgr = LLMSettingsManager.Get();
        if (settingsMgr == null)
        {
            AddSystemMessage("LLM settings are not initialized yet. Open LLM Settings and configure a provider first.", includeInLLMRecap: false);
            return;
        }

        var instanceMgr = LLMInstanceManager.Get();
        int llmReplicaIndex = 0;
        bool isVisionJob = _promptManager != null && _promptManager.HasAnyImages();
        // The main chat turn is a BIG job - it carries the full system prompt plus
        // the whole conversation - so BigJobsOnly instances accept it and
        // SmallJobsOnly instances (meant for caption/delegation one-shots) don't
        // steal it. This also keeps the chat on one instance when the user splits
        // roles via job modes, which is what lets that server's prompt cache work.
        int llmInstanceID = instanceMgr?.GetFreeLLM(isSmallJob: false, isVisionJob: isVisionJob, out llmReplicaIndex) ?? -1;

        // Reset the streaming-action parser for this turn (counters + buffer state).
        _actionParser?.Reset();

        if (llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
        {
            llmInstanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: isVisionJob, out llmReplicaIndex);
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
            AddSystemMessage("No LLM provider settings found. Configure one via LLM Settings.", includeInLLMRecap: false);
            ReleaseActiveLLM();
            return;
        }

        ReloadSkillConfigForNextTurn();

        // Set the STABLE system prompt (persona + skill summaries + protocol).
        // Volatile per-turn state (GPU busy/idle, chat-image captions) is appended
        // to the outgoing user message in AppendCurrentStateToOutgoingLines instead;
        // putting it here would change the very top of the request every turn and
        // defeat server-side prompt caching for the entire conversation.
        if (_contextBuilder != null && _promptManager != null)
        {
            _promptManager.SetBaseSystemPrompt(_contextBuilder.Build());
            InjectTriggeredSkillContextIfNeeded(latestUserMessage);
        }

        _activeProviderInFlight = activeProvider;
        _isStreaming = true;
        _streamBuffer.Clear();
        _streamLastUpdate = 0;
        _streamCharsReceived = 0;
        _streamStartTime = Time.unscaledTime;
        _streamFirstTokenTime = 0f;
        _streamStatusNextRefresh = 0f;
        _streamSpinnerStep = 0;
        SetBusyUI(true, $"{StreamSpinnerFrames[0]} Talking to LLM...");

        AddAssistantBubble("");

        var lines = _promptManager.BuildPromptChat();
        // Strip TMP markup from any prior assistant bubbles before sending (safety - the
        // GPTPromptManager only ever stores raw text we put in, but be defensive).
        lines = OpenAITextCompletionManager.RemoveTMPTags(lines);

        // Tack the volatile CURRENT STATE block (GPU busy/idle, chat-image captions)
        // onto the outgoing copy of the latest user message - the request tail, where
        // churn is cheap. Ephemeral by design: stored history never contains it, so
        // next turn's request still prefix-matches everything the server cached.
        AppendCurrentStateToOutgoingLines(lines);

        // Total prompt size feeds the status line's prefill estimate (chars/4 ~ tokens).
        _streamPromptApproxChars = 0;
        foreach (var promptLine in lines)
            if (promptLine != null && promptLine._content != null)
                _streamPromptApproxChars += promptLine._content.Length;
        _streamMaxContextTokens = ResolveMaxContextTokens(activeProvider, activeSettings, llmReplicaIndex);

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
            AddSystemMessage($"Note: {activeProvider} chat path is not configured to send images yet; only text will be sent.", includeInLLMRecap: false);
        }

        RTDB db = new RTDB();

        // Tag this conversational turn as "chat" so its request body is forwarded
        // to the editor-only AIChatLog (llm_aichat_log.json), separate from the
        // vision-caption / summarization sidecar traffic the same managers serve.
        // The dispatch below runs each provider's coroutine synchronously up to its
        // first yield (where LogRequest fires), so the scope is still active then.
        using (LLMDebugLog.PurposeScope("chat"))
        switch (activeProvider)
        {
            case LLMProvider.OpenAI:
            {
                string apiKey = activeSettings.apiKey;
                string model = string.IsNullOrEmpty(activeSettings.selectedModel) ? "gpt-4o-mini" : activeSettings.selectedModel;

                if (!HasUserMessage(lines))
                    lines.Enqueue(new GTPChatLine("user", "Please proceed."));

                // Single source of truth for "which OpenAI request shape does this model want?".
                // Edit OpenAIRequestProfileResolver to add new model families.
                var profile = OpenAIRequestProfileResolver.Resolve(model, activeSettings, llmReplicaIndex);

                string json = _openAIMgr.BuildChatCompleteJSON(lines, AI_CHAT_NO_EXPLICIT_OUTPUT_TOKEN_CAP, temperature, model, true,
                    profile.useResponsesAPI, profile.isReasoningModel, profile.includeTemperature,
                    profile.reasoningEffort, profile.enableThinking);
                _openAIMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, apiKey, profile.endpoint, OnStreamingTextCallback, true);
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
                AddSystemMessage("Unsupported provider: " + activeProvider, includeInLLMRecap: false);
                FinalizeAssistantTurn(aborted: true);
                return;
        }
    }

    private void InjectTriggeredSkillContextIfNeeded(string latestUserMessage)
    {
        if (_skillManager == null || _promptManager == null || string.IsNullOrWhiteSpace(latestUserMessage))
            return;

        var matched = _skillManager.GetAutoloadSkillsForMessage(latestUserMessage);
        if (matched == null || matched.Count == 0)
            return;

        // Bump every triggered skill to most-recent in the LRU (newest at the tail).
        bool anyNew = false;
        foreach (var skill in matched)
        {
            if (skill == null || string.IsNullOrEmpty(skill.Id))
                continue;
            if (!_autoloadLru.Contains(skill.Id))
                anyNew = true;
            _autoloadLru.Remove(skill.Id);
            _autoloadLru.Add(skill.Id);
        }

        // Nothing new triggered: the live set is unchanged (we only refreshed recency
        // for future eviction), so leave the cached context block alone.
        if (!anyNew)
            return;

        RebuildAutoloadSkillContext();
    }

    /// <summary>
    /// Recompute which auto-loaded skill bodies are live - the most-recently-triggered
    /// <see cref="MaxAutoloadSkillBodies"/>, always retaining <see cref="PinnedAutoloadSkillId"/>
    /// - and rewrite the single tagged context block to match. Bulk remove + re-add
    /// mirrors the long-standing per-turn refresh; the STABLE base system prompt is
    /// untouched, so only this block (now at the tail) re-prefills while the rest of the
    /// cached conversation prefix survives. Also drives the per-turn reload path so the
    /// cap and ordering stay consistent across both callers.
    /// </summary>
    private void RebuildAutoloadSkillContext()
    {
        if (_skillManager == null || _promptManager == null)
            return;

        // Drop ids whose skill file disappeared (e.g. user deleted/renamed it and the
        // config was reloaded) so the LRU doesn't leak dead entries.
        _autoloadLru.RemoveAll(id => _skillManager.GetById(id) == null);

        var keep = ComputeKeptAutoloadSkills();

        _promptManager.RemoveInteractionsByInternalTag(AUTOLOAD_SKILL_CONTEXT_TAG);
        _stickyAutoloadSkillIds.Clear();
        foreach (var s in keep)
            _stickyAutoloadSkillIds.Add(s.Id);

        if (keep.Count == 0)
            return;

        string block = _skillManager.BuildSkillReferenceMaterialBlock(keep);
        if (!string.IsNullOrWhiteSpace(block))
            _promptManager.AddInteraction("system", block, AUTOLOAD_SKILL_CONTEXT_TAG);

        Debug.Log("AIChatPanel: auto-loaded skill context now: " + string.Join(", ", keep.ConvertAll(s => s.Id)));
    }

    /// <summary>
    /// Resolve the LRU id list to at most <see cref="MaxAutoloadSkillBodies"/> live Skill
    /// objects, always keeping <see cref="PinnedAutoloadSkillId"/> when loaded (mid-story
    /// turns still need the roleplay spine even when only a composition skill triggered
    /// this turn). The oldest non-pinned recents are dropped first. Returned oldest-first
    /// for a stable block ordering.
    /// </summary>
    private List<Skill> ComputeKeptAutoloadSkills()
    {
        var keptIds = new List<string>();
        int budget = MaxAutoloadSkillBodies;

        bool pinnedLoaded = _autoloadLru.Contains(PinnedAutoloadSkillId);
        if (pinnedLoaded)
        {
            keptIds.Add(PinnedAutoloadSkillId);
            budget--;
        }

        // Fill the remaining budget from most-recent (tail) backwards, skipping pinned.
        for (int i = _autoloadLru.Count - 1; i >= 0 && budget > 0; i--)
        {
            string id = _autoloadLru[i];
            if (id == PinnedAutoloadSkillId) continue;
            if (keptIds.Contains(id)) continue;
            keptIds.Add(id);
            budget--;
        }

        // Emit oldest-first (pinned tends to be the oldest anyway) for stable text.
        keptIds.Reverse();

        var skills = new List<Skill>();
        foreach (string id in keptIds)
        {
            var s = _skillManager?.GetById(id);
            if (s != null) skills.Add(s);
        }
        return skills;
    }

    /// <summary>
    /// Append the volatile CURRENT STATE block (GPU busy/idle, chat-image list with
    /// captions) to the last user line of the outgoing request. Operates on the
    /// clones BuildPromptChat/RemoveTMPTags returned - stored history is never
    /// touched, which is what keeps the conversation's server-side prompt cache
    /// valid while GPU state and captions churn between turns. If no user line
    /// exists (shouldn't happen - sends always follow a user message) the block is
    /// simply skipped; it's advisory context, not required for a valid request.
    /// </summary>
    private void AppendCurrentStateToOutgoingLines(Queue<GTPChatLine> lines)
    {
        if (_contextBuilder == null || lines == null) return;

        GTPChatLine lastUser = null;
        foreach (var line in lines)
            if (line != null && line._role == "user") lastUser = line;
        if (lastUser == null) return;

        int chatImageSlots = ((IChatHost)this).GetChatImageCount();
        // Snapshot captions in chat-image order so the CHAT IMAGES block can
        // print "- Image #N: <caption>" entries for the LLM.
        var captions = new List<string>(chatImageSlots);
        for (int i = 1; i <= chatImageSlots; i++)
            captions.Add(((IChatHost)this).GetChatImageCaption(i));

        string anchorsLine = BuildAnchorsStateLine();
        string state = _contextBuilder.BuildCurrentStateBlock(chatImageSlots, captions, anchorsLine);
        if (string.IsNullOrEmpty(state)) return;

        lastUser._content = (lastUser._content ?? "") + "\n\n" + state;
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
                return true;

            case LLMProvider.Gemini:
                // GeminiTextCompletionManager.BuildChatCompleteJSON serializes
                // attached images as inlineData parts (used by both the main chat
                // path and the vision-caption sidecar).
                return false;

            case LLMProvider.OpenAI:
                // Both OpenAI request shapes used by this app serialize image payloads:
                // Chat Completions uses image_url content items, Responses uses
                // input_image content items.
                return false;

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
        if (_streamCharsReceived == 0)
            _streamFirstTokenTime = Time.unscaledTime;
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
            // Enqueue, don't execute directly: the executor's serial pump runs
            // actions in arrival order and parks the rest of the turn behind
            // any action that defers (e.g. a page waiting for its anchor).
            _actionExecutor.EnqueueAction(action);
        }
        catch (Exception ex)
        {
            Debug.LogError("AIChatPanel: SkillActionExecutor.EnqueueAction threw: " + ex);
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
            AddSystemMessage("LLM error: " + error, includeInLLMRecap: false);
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

        // Editor-only: record the raw assistant reply WITH its <aitools_action>
        // tool-call tags inline. This is the half the old "sent only" request log
        // never captured - it's what reveals e.g. poster text being baked into a
        // generate_image prompt vs. laid out with draw_text. Pairs with the "chat"
        // request logged via PurposeScope at send time.
        AIChatLog.Response("chat", historyText);

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

    /// <summary>
    /// "142 tok   45 t/s   ctx ~33k/131k   (prefill 9.8s ~326 t/s)" for the
    /// current/just-finished turn, or "" if no tokens were received. Generation t/s
    /// is measured from the first received chunk; the prefill note only appears once
    /// the first-byte delay is long enough to matter. ctx is prompt + everything
    /// generated so far, against the model's window when we know it. All numbers
    /// are chars/4 estimates, not exact tokens.
    /// </summary>
    private string BuildStreamStatsText()
    {
        if (_streamCharsReceived <= 0 || _streamFirstTokenTime <= 0f) return "";
        float elapsed = Mathf.Max(0.001f, Time.unscaledTime - _streamFirstTokenTime);
        int approxTokens = _streamCharsReceived / 4;
        float tps = approxTokens / elapsed;
        string tpsStr = tps >= 10 ? tps.ToString("F0") : tps.ToString("F1");
        string prefillStr = "";
        float prefillSecs = _streamFirstTokenTime - _streamStartTime;
        if (prefillSecs >= 1f)
        {
            int promptTokens = _streamPromptApproxChars / 4;
            string prefillTps = promptTokens > 0 ? $" ~{promptTokens / prefillSecs:F0} t/s" : "";
            prefillStr = $"   (prefill {prefillSecs:F1}s{prefillTps})";
        }
        return $"{approxTokens} tok   {tpsStr} t/s{BuildContextFillText()}{prefillStr}";
    }

    /// <summary>
    /// "   ctx ~33k/131k" (or "   ctx ~33k" when the model's window is unknown) for
    /// the turn in flight - prompt plus everything generated so far. "" if nothing
    /// was sent yet.
    /// </summary>
    private string BuildContextFillText()
    {
        int ctxTokens = (_streamPromptApproxChars + _streamCharsReceived) / 4;
        if (ctxTokens <= 0) return "";
        string totalStr = _streamMaxContextTokens > 0 ? $"/{FormatTokenCount(_streamMaxContextTokens)}" : "";
        return $"   ctx ~{FormatTokenCount(ctxTokens)}{totalStr}";
    }

    /// <summary>"653", "9.8k", "33k" - compact token counts for the status line.</summary>
    private static string FormatTokenCount(int tokens)
    {
        if (tokens < 1000) return tokens.ToString();
        float k = tokens / 1000f;
        return k < 10 ? $"{k:F1}k" : $"{Mathf.RoundToInt(k)}k";
    }

    /// <summary>
    /// Best-known total context window (tokens) for this turn's provider, or 0 if
    /// unknown. Ollama settings carry the model's discovered context (or the user's
    /// override - whichever num_ctx we actually request); llama.cpp servers are
    /// probed via /props for the loaded n_ctx. Hosted APIs (OpenAI/Anthropic/Gemini)
    /// have no reliable source here, so they stay unknown.
    /// </summary>
    private int ResolveMaxContextTokens(LLMProvider provider, LLMProviderSettings settings, int replicaIndex)
    {
        if (settings == null) return 0;
        switch (provider)
        {
            case LLMProvider.Ollama:
                if (settings.overrideContextLength && settings.contextLength > 0)
                    return settings.contextLength;
                return Mathf.Max(0, settings.maxContextLength);

            case LLMProvider.LlamaCpp:
            {
                string srv = LLMInstanceManager.ApplyReplicaPortOffset(settings.endpoint, replicaIndex);
                if (string.IsNullOrEmpty(srv)) return 0;
                if (_llamaCppCtxCache.TryGetValue(srv, out int ctx)) return ctx;
                // Kick off a one-shot probe; if it lands while this turn is still
                // streaming, the live status refresh picks it up mid-turn.
                if (_llamaCppCtxProbesInFlight.Add(srv))
                    StartCoroutine(ProbeLlamaCppContextSize(srv, settings.apiKey));
                return 0;
            }

            default:
                return 0;
        }
    }

    /// <summary>
    /// Fetches llama.cpp's /props once to learn the server's loaded context window
    /// (default_generation_settings.n_ctx) and caches it per server address.
    /// Failures aren't cached so a server that was down gets re-probed next turn.
    /// </summary>
    private IEnumerator ProbeLlamaCppContextSize(string serverAddress, string apiKey)
    {
        string url = serverAddress.TrimEnd('/') + "/props";
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            req.timeout = 10;
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            yield return req.SendWebRequest();

            int ctx = 0;
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                try
                {
                    var root = JSON.Parse(req.downloadHandler.text);
                    if (root != null)
                        ctx = root["default_generation_settings"]["n_ctx"].AsInt;
                }
                catch (Exception) { ctx = 0; }
            }

            _llamaCppCtxProbesInFlight.Remove(serverAddress);
            if (ctx > 0)
            {
                _llamaCppCtxCache[serverAddress] = ctx;
                if (_isStreaming && _activeProviderInFlight == LLMProvider.LlamaCpp && _streamMaxContextTokens <= 0)
                    _streamMaxContextTokens = ctx;
            }
        }
    }

    private void FinalizeAssistantTurn(bool aborted, bool shouldAutoScroll = false)
    {
        _isStreaming = false;
        _streamingAssistantField = null;
        _streamingAssistantRT = null;
        ReleaseActiveLLM();

        // Auto-continue: if this turn finished cleanly and there are auto-fires
        // left in the burst, decrement and schedule the next "(continue)" Send.
        // Aborts (Stop / errors) drain the counter so the burst doesn't resume
        // on its own.
        bool willAutoContinue = false;
        if (aborted)
        {
            _autoContinueRemaining = 0;
        }
        else if (_autoContinueRemaining > 0
                 && _autoContinueToggle != null && _autoContinueToggle.isOn)
        {
            _autoContinueRemaining--;
            willAutoContinue = true;
        }

        // Keep the turn's final token/speed numbers on screen instead of snapping
        // straight to Idle - they used to vanish before the user could read them.
        string stats = BuildStreamStatsText();
        string doneStatus;
        if (aborted)
            doneStatus = string.IsNullOrEmpty(stats) ? "Stopped" : $"Stopped   {stats}";
        else if (willAutoContinue)
            doneStatus = $"Auto-continue ({_autoContinueRemaining + 1} left)";
        else
            doneStatus = string.IsNullOrEmpty(stats) ? "Idle" : $"Done   {stats}";
        SetBusyUI(false, doneStatus);
        if (shouldAutoScroll)
            StartCoroutine(ScrollToBottomDeferred());

        if (willAutoContinue)
        {
            StartCoroutine(FireAutoContinueNextFrame());
        }
        else
        {
            // Re-focus the chat input so the user can immediately type their next message
            // (unless they're in the middle of editing some other input - e.g. a bubble).
            FocusInputDeferred();
        }
    }

    /// <summary>
    /// Defer the next auto-continue Send by one frame so the previous turn's
    /// FinalizeAssistantTurn fully unwinds (status text settled, bubble edit
    /// hookups complete) before we re-enter the send pipeline.
    /// </summary>
    private IEnumerator FireAutoContinueNextFrame()
    {
        yield return null;
        // Bail if anything cancelled the burst during the yield (user hit Stop,
        // toggled Auto off, cleared, or kicked off a manual send themselves).
        if (_autoContinueToggle == null || !_autoContinueToggle.isOn) yield break;
        if (_isStreaming) yield break;
        _autoContinueFiring = true;
        try
        {
            OnSendClicked();
        }
        finally
        {
            _autoContinueFiring = false;
        }
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
        // _isStreaming is updated alongside this call (see SendChatTurn / FinalizeAssistantTurn);
        // RecomputeSendInteractable also factors in any pending attachment caption jobs.
        RecomputeSendInteractable();
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
        // Self-healing reverse of the display-only fullwidth angle bracket
        // substitution: if the LLM ever outputs '＜aitools_action ...＞' (e.g.
        // because a previous turn's corrupted history taught it to mimic
        // fullwidth), normalize back to ASCII so the action parser recognizes
        // it AND so the saved history is clean for future turns.
        text = ReverseTmpDisplayEscapes(text);
        return text.Trim();
    }

    private void ReloadSkillConfigForNextTurn()
    {
        if (_skillManager == null)
            return;

        bool hadAutoload = _autoloadLru.Count > 0;
        _skillManager.Reload();

        if (!hadAutoload)
            return;

        // Re-emit the (possibly user-edited) bodies for the still-loaded set, honoring
        // the cap and dropping any skill whose file vanished. Driving this through the
        // shared helper keeps the reload path and the trigger path on one code path.
        RebuildAutoloadSkillContext();
    }

    private void OnSettingsClicked()
    {
        AIChatSettingsPanel.Show(_skillManager, () =>
        {
            // Reload from disk so any user edits to main_prompt.txt or skill files take
            // effect on the very next turn (rebuilt by ChatContextBuilder.Build()).
            _skillManager?.Reload();
            _promptManager?.RemoveInteractionsByInternalTag(AUTOLOAD_SKILL_CONTEXT_TAG);
            _stickyAutoloadSkillIds.Clear();
            _autoloadLru.Clear();
            int n = _skillManager?.GetSkills().Count ?? 0;
            AddSystemMessage($"Reloaded aichat config: {n} skill{(n == 1 ? "" : "s")}.", includeInLLMRecap: false);
            AddPromptConfigNotice();
        });
    }

    /// <summary>
    /// Refreshes the header status pill: "GPUs 1/2 · LLMs 1/4". Cheap; called from
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
        // "LLMs" mirrors the GPU usage style: active calls / total capacity. Capacity
        // is the sum over enabled instances of (maxConcurrentTasks x replicas), so a
        // single instance set to 2 replicas x 2 concurrent tasks reads ".../4".
        int llmActive = im != null ? im.GetTotalActiveTaskCount() : 0;
        int llmCapacity = im != null ? im.GetTotalLLMCapacity() : 0;
        _statusPillText.text = $"GPUs {gpuBusy}/{gpuTotal} · LLMs {llmActive}/{llmCapacity}";
    }

    /// <summary>
    /// Fallback caption prompt used only when neither aichat/caption_prompt.txt
    /// nor aichat/test_caption_prompt.txt exists. Kept intentionally short so
    /// captioning still works on a fresh checkout; the maintained version is
    /// the on-disk file.
    /// </summary>
    private const string DefaultCaptionPrompt =
        "Describe this image factually for a downstream image-editing AI. " +
        "Return BOTH descriptions, each prefixed exactly as shown:\n" +
        "\n" +
        "SHORT: <one sentence, max 15 words, suitable as a UI label>\n" +
        "LONG: <a detailed paragraph (200-300 words). For EACH person visible, " +
        "state apparent age, gender, ethnicity / skin tone, body type and build, " +
        "hair, expression, pose, and clothing. Then describe setting, composition, " +
        "lighting, colors, mood, art style, and any visible text. No preamble, " +
        "no markdown, no quotes>\n" +
        "\n" +
        "Output exactly those two lines (LONG can wrap), nothing else.";

    /// <summary>
    /// Result of a one-shot caption call: a short, label-friendly summary plus
    /// a long, detailed description. Either may be empty if the LLM call
    /// failed or no vision LLM was available - callers should treat both
    /// fields as best-effort.
    /// </summary>
    private struct CaptionResult
    {
        public string shortCaption;
        public string longCaption;

        public bool IsEmpty => string.IsNullOrEmpty(shortCaption) && string.IsNullOrEmpty(longCaption);
    }

    /// <summary>
    /// Fire a one-shot caption request against any vision-capable LLM for the
    /// supplied PNG bytes. The model is asked to return BOTH a short label
    /// (~15 words) and a long detailed description (~200-300 words) in a
    /// labelled format we parse in <see cref="ParseCaptionResponse"/>.
    /// Result fires via <paramref name="onResult"/>; the callback always
    /// runs (even on failure / no vision LLM) so callers can use it to
    /// clear in-flight gates.
    /// </summary>
    private CaptionJob TryCaptionBytes(byte[] png, Action<CaptionResult> onResult)
    {
        var job = new CaptionJob();
        Action<CaptionResult> safeResult = (r) =>
        {
            // Cancelled jobs drop their result entirely - the attachment is
            // gone, so writing a caption back would do nothing useful and
            // could confuse the host's state.
            if (job.cancelled) return;
            try { onResult?.Invoke(r); } catch { }
        };

        if (png == null || png.Length == 0) { job.completed = true; safeResult(default); return job; }

        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0) { job.completed = true; safeResult(default); return job; }

        int targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: true, out int replicaIndex);
        if (targetId < 0)
            targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true, out replicaIndex);
        if (targetId < 0) { job.completed = true; safeResult(default); return job; }

        var inst = instanceMgr.GetInstance(targetId);
        if (inst == null || inst.settings == null) { job.completed = true; safeResult(default); return job; }

        instanceMgr.SetLLMBusy(targetId, replicaIndex, true);
        job.targetId = targetId;
        job.replicaIndex = replicaIndex;

        // The two-section format keeps the parser dead simple AND lets the
        // model "warm up" on the short description before committing to the
        // long one. Explicit prefix labels are easier for small open-weights
        // vision models to follow than JSON.
        //
        // The active prompt body lives in aichat/caption_prompt.txt (or
        // aichat/test_caption_prompt.txt if the user staged an override) so it
        // can be tuned without recompiling. The fallback below is only used
        // when neither file exists.
        string captionPrompt = (_skillManager != null && !string.IsNullOrWhiteSpace(_skillManager.CaptionPrompt))
            ? _skillManager.CaptionPrompt
            : DefaultCaptionPrompt;

        var lines = new Queue<GTPChatLine>();
        var userLine = new GTPChatLine("user", captionPrompt);
        userLine.AddImage(System.Convert.ToBase64String(png), -1);
        lines.Enqueue(userLine);

        int capturedTargetId = targetId;
        int capturedReplicaIndex = replicaIndex;

        Action<RTDB, JSONObject, string> onDone = (db, json, text) =>
        {
            // Mutual exclusion: if the watchdog or a user-cancel beat us, the
            // LLM busy count was already decremented (and possibly that slot
            // re-allocated to another job). Decrementing again here would
            // steal a slot from an unrelated task.
            if (job.completed) return;
            job.completed = true;
            if (job.watchdog != null)
            {
                try { StopCoroutine(job.watchdog); } catch { }
                job.watchdog = null;
            }
            instanceMgr.SetLLMBusy(capturedTargetId, capturedReplicaIndex, false);
            CaptionResult result = default;
            try
            {
                string raw = (text ?? "").Trim();
                if (string.IsNullOrEmpty(raw) && json != null)
                {
                    try { raw = OpenAITextCompletionManager.ExtractTextFromResponseJSON(json); } catch { /* no-op */ }
                }
                result = ParseCaptionResponse(raw);
            }
            finally { safeResult(result); }
        };

        SkillActionExecutor.DispatchOneShot(this, inst, lines, onDone, "ImageCaption", "examine_image_sent.json");

        // Watchdog: if the request never returns (hung local model), force-release
        // the LLM slot after CAPTION_TIMEOUT_SECONDS so the user isn't stuck.
        job.watchdog = StartCoroutine(CaptionWatchdog(job, instanceMgr, safeResult));
        return job;
    }

    private IEnumerator CaptionWatchdog(CaptionJob job, LLMInstanceManager instanceMgr, Action<CaptionResult> safeResult)
    {
        yield return new WaitForSeconds(CAPTION_TIMEOUT_SECONDS);
        if (job.completed) yield break;
        job.completed = true;
        job.watchdog = null;
        Debug.LogWarning($"AIChatPanel: vision-LLM caption request didn't return in {CAPTION_TIMEOUT_SECONDS:0}s - force-releasing LLM slot.");
        if (job.targetId >= 0 && instanceMgr != null)
            instanceMgr.SetLLMBusy(job.targetId, job.replicaIndex, false);
        safeResult(default);
    }

    /// <summary>
    /// Extract SHORT: / LONG: sections from a vision LLM response. Tolerates
    /// loose formatting (extra blank lines, the model wrapping in quotes or
    /// emitting only one section). If only one section is present, the other
    /// is derived: missing LONG falls back to the whole response; missing
    /// SHORT falls back to the first sentence (or first ~80 chars) of LONG.
    /// </summary>
    private static CaptionResult ParseCaptionResponse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return default;
        string text = raw.Trim();
        // Strip wrapping triple-backtick fences if the model decided to be helpful.
        if (text.StartsWith("```"))
        {
            int firstNL = text.IndexOf('\n');
            if (firstNL > 0) text = text.Substring(firstNL + 1);
            if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
            text = text.Trim();
        }

        string sh = "";
        string lo = "";

        // Locate "SHORT:" and "LONG:" labels at the start of a line. Tolerates
        // markdown bold (**SHORT:**) and a leading bullet/dash that small
        // open-weights models sometimes prepend. The LONG body is anchored to
        // end-of-input so a multi-paragraph long caption stays intact.
        const string labelPrefix = @"^\s*[\-\*]?\s*\**\s*";
        const string labelSuffix = @"\s*\**\s*:\s*";
        var shortMatch = Regex.Match(text,
            labelPrefix + "SHORT" + labelSuffix +
            @"(.+?)(?=" + labelPrefix + "LONG" + labelSuffix + @"|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
        var longMatch = Regex.Match(text,
            labelPrefix + "LONG" + labelSuffix + @"(.+)\z",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

        if (shortMatch.Success) sh = StripTrailingBold(shortMatch.Groups[1].Value.Trim());
        if (longMatch.Success)  lo = StripTrailingBold(longMatch.Groups[1].Value.Trim());

        // Fallbacks when the model ignored the format.
        if (string.IsNullOrEmpty(lo) && string.IsNullOrEmpty(sh))
            lo = text;
        if (string.IsNullOrEmpty(lo) && !string.IsNullOrEmpty(sh))
            lo = sh;
        if (string.IsNullOrEmpty(sh) && !string.IsNullOrEmpty(lo))
            sh = DeriveShortFromLong(lo);

        sh = ClampCaption(sh);
        lo = CleanLongCaption(lo);

        return new CaptionResult { shortCaption = sh, longCaption = lo };
    }

    /// <summary>
    /// Derive a one-line label from a long caption: first sentence, capped at
    /// ~100 chars with an ellipsis. Used when the model returned only the
    /// LONG section.
    /// </summary>
    private static string DeriveShortFromLong(string lo)
    {
        if (string.IsNullOrEmpty(lo)) return "";
        string s = lo.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        int stop = s.IndexOfAny(new[] { '.', '!', '?' });
        string head = (stop > 0 && stop + 1 <= s.Length) ? s.Substring(0, stop + 1) : s;
        if (head.Length > 100) head = head.Substring(0, 97) + "…";
        return head.Trim();
    }

    /// <summary>
    /// Remove a trailing "**" the model sometimes leaves on a SHORT/LONG
    /// section when it bolded the value as well as the label.
    /// </summary>
    private static string StripTrailingBold(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.TrimEnd();
        while (s.EndsWith("**")) s = s.Substring(0, s.Length - 2).TrimEnd();
        return s;
    }

    /// <summary>
    /// Trim wrapping quotes / markdown fences from a long caption but leave
    /// the body (including newlines) intact - <see cref="ClampCaption"/>'s
    /// 25-word cap is unsuitable for a 200-word description.
    /// </summary>
    private static string CleanLongCaption(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length >= 2)
        {
            char a = s[0], b = s[s.Length - 1];
            if ((a == '"' && b == '"') || (a == '\'' && b == '\'') || (a == '`' && b == '`'))
                s = s.Substring(1, s.Length - 2).Trim();
        }
        return s;
    }

    /// <summary>
    /// Pic-bound caption helper used by WaitForPicAndCaption for generated
    /// images. Delegates to <see cref="TryCaptionBytes"/>; on success writes
    /// <c>pic.Caption</c> (long) and <c>pic.CaptionShort</c>, and updates
    /// the bubble label with the short form. The onComplete callback
    /// always fires so the polling coroutine can clear its inFlight latch.
    /// </summary>
    private void TryCaptionPic(PicMain pic, byte[] png, Action onComplete)
    {
        Action safeComplete = () => { try { onComplete?.Invoke(); } catch { } };

        if (pic == null || pic.gameObject == null) { safeComplete(); return; }

        PicMain capturedPic = pic;
        TryCaptionBytes(png, result =>
        {
            try
            {
                if (capturedPic != null && capturedPic.gameObject != null)
                {
                    string shortCaption = result.IsEmpty ? "caption unavailable" : (result.shortCaption ?? "");
                    string longCaption = result.IsEmpty ? "caption unavailable" : (result.longCaption ?? "");
                    capturedPic.Caption = longCaption;
                    capturedPic.CaptionShort = shortCaption;
                    string labelSuffix = !string.IsNullOrEmpty(result.shortCaption)
                        ? result.shortCaption
                        : longCaption;
                    if (!string.IsNullOrEmpty(labelSuffix)
                        && _captionLabels.TryGetValue(capturedPic, out var entry)
                        && entry.label != null)
                        entry.label.text = entry.baseText + " " + labelSuffix;
                }
            }
            finally { safeComplete(); }
        });
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
        // Both axes track the inner LayoutElement's preferredWidth/Height. With
        // horizontalFit=Unconstrained the container sat at sizeDelta.x=0 and
        // ignored preferredWidth, so the text wrapped to 0px and the tooltip
        // grew vertically forever on long captions. PreferredSize makes the
        // container honour the 640px width below.
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(_captionTooltipRoot.transform, false);
        var textLE = textGo.AddComponent<LayoutElement>();
        // Wide tooltip: long (~200-300 word) captions otherwise snake down the
        // entire screen vertically. 640 keeps a 250-word caption around 8-10
        // wrapped lines at 13pt.
        textLE.preferredWidth = 640f;
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
    /// ChatPicMirror, and editable by the user as a normal world Pic. If the info
    /// already carries a pre-computed caption (set by the on-attach captioning
    /// path), we propagate it to <see cref="PicMain.Caption"/> synchronously so
    /// the next system-prompt rebuild has it without re-running the coroutine.
    /// </summary>
    private void PromoteAttachmentsToChatImages(IReadOnlyList<ChatImageAttachmentZone.AttachmentInfo> attachments)
    {
        if (attachments == null || attachments.Count == 0) return;
        var imageGen = ImageGenerator.Get();
        if (imageGen == null) return;

        foreach (var info in attachments)
        {
            if (info.bytes == null || info.bytes.Length == 0) continue;
            // Same decode pattern SkillActionExecutor uses for chat_image inputs, so
            // round-trips of the same PNG are byte-identical.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(info.bytes))
            {
                UnityEngine.Object.Destroy(tex);
                continue;
            }
            var go = imageGen.AddImageByTexture(tex);
            if (go == null) continue;
            var pic = go.GetComponent<PicMain>();
            if (pic == null) continue;
            AppendUserAttachmentBubble(pic, info.captionShort, info.captionLong);
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

        // If the action named a character anchor (anchor="Bob"), bind/re-bind that name
        // to this freshly-spawned Pic. Re-binding is intentional: it's how a character's
        // look gets updated (generate a new image of Bob, re-tag anchor="Bob", and later
        // chat_image="Bob" references now resolve to the new image).
        if (!string.IsNullOrEmpty(action?.AnchorName))
        {
            _anchors[action.AnchorName] = spawnedPic;
            Debug.Log($"AIChatPanel: anchor '{action.AnchorName}' -> Image #{chatImageNumber}");
        }

        string skillId = action != null ? (action.SkillId ?? "") : "";
        bool isMovie = skillId == BuiltInSkillIds.GenerateMovie || skillId == BuiltInSkillIds.ImageToMovie;
        // Keep the bubble label compact so the caption (appended async below) has
        // room: just "#N". The Image/Movie kind and skillId are visually obvious
        // from the bubble itself and still tracked for the LLM in ChatContextBuilder.
        string label = $"#{chatImageNumber}";
        AppendImageBubbleInternal(spawnedPic, label, isMovie);

        // Tell the LLM what number this bubble got, so when the user follows up with
        // "tell me about them" or "put them in a scene", the model references the
        // ACTUAL slot numbers instead of predicting future ones. The model has shown
        // it will hallucinate numbers (e.g. claim "#5..#8" right after generating
        // bubbles that actually became #1..#4) even though CHAT IMAGES is rebuilt
        // every turn - this explicit per-bubble confirmation gives it an anchor that
        // survives in conversation history regardless of caption-readiness. Delivered
        // via the info recap (rides the tail of the user's NEXT outgoing message),
        // NOT as a system-role interaction: BuildPromptChat folds system lines into
        // the FRONT system message, and growing the prompt head per image invalidated
        // the server's prompt cache for the entire conversation every generation.
        // No bubble: the chat already shows the labeled image bubble.
        {
            string kindLabel = isMovie ? "Movie" : "Image";
            _infoMessages.Add(new InfoMessage(
                $"({kindLabel} just spawned is {kindLabel} #{chatImageNumber} in CHAT IMAGES. " +
                $"Reference it via chat_image=\"{chatImageNumber}\" - in THIS same reply (the host waits " +
                "for it to finish rendering) or on any later turn - or by its anchor name if one was set. " +
                "Don't guess slot numbers for bubbles you generated earlier in this same reply; " +
                "the actual number is the one stated here.)"));
        }

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
            //
            // The !IsBusy() guard is what skips the black placeholder on AI-
            // *generated* images: a freshly spawned Pic shows a blank/black
            // texture that sits "stable" for the first few seconds while the
            // render job is still queued/running, which used to burn an LLM
            // call describing a black square before the real result landed.
            // Captioning only an idle Pic means we describe the finished image
            // once. (User-dragged images arrive idle with the real texture, so
            // they still caption immediately - no regression.)
            if (curTex != captionedTex && !inFlight && stableTicks >= stableTicksRequired
                && !pic.IsBusy())
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
    private void AppendUserAttachmentBubble(PicMain pic, string preCaptionShort = null, string preCaptionLong = null)
    {
        if (pic == null || _mediaContent == null) return;
        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        string label = $"#{chatImageNumber} (you)";
        AppendImageBubbleInternal(pic, label, isMovie: false);

        if (!string.IsNullOrEmpty(preCaptionShort) || !string.IsNullOrEmpty(preCaptionLong))
        {
            // Caption was already computed at attach time. Set both fields on
            // the PicMain synchronously so the next system-prompt rebuild
            // (in SendChatTurn) and the hover tooltip see them, and patch the
            // bubble label with the short form so the cramped media column
            // stays readable.
            pic.Caption = preCaptionLong ?? "";
            pic.CaptionShort = preCaptionShort ?? "";
            string labelSuffix = !string.IsNullOrEmpty(preCaptionShort)
                ? preCaptionShort
                : preCaptionLong;
            if (!string.IsNullOrEmpty(labelSuffix)
                && _captionLabels.TryGetValue(pic, out var entry)
                && entry.label != null)
                entry.label.text = entry.baseText + " " + labelSuffix;
            return;
        }

        // No pre-caption (e.g. no vision LLM was available at attach time).
        // Fall back to the stability-aware polling coroutine - same one used
        // for AI-generated images - which will retry captioning whenever the
        // texture settles.
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
    /// How many of the most recent user-&gt;assistant exchanges the Compact feature
    /// keeps verbatim. Clamped to at least 0 (0 = keep nothing raw / summarize all).
    /// </summary>
    public static int GetCompactKeepN()
    {
        int n = PlayerPrefs.GetInt(PREFS_COMPACT_KEEP_N, DEFAULT_COMPACT_KEEP_N);
        return Mathf.Max(0, n);
    }

    public static void SetCompactKeepN(int n)
    {
        PlayerPrefs.SetInt(PREFS_COMPACT_KEEP_N, Mathf.Max(0, n));
        PlayerPrefs.Save();
    }

    /// <summary>True when a chat panel instance is alive to compact.</summary>
    public static bool IsChatActive => _instance != null;

    /// <summary>
    /// Settings-panel entry point: drop everything except the last
    /// <paramref name="keepExchanges"/> exchanges. No LLM call; images are NOT
    /// touched (the media panel and chat_image="N" indices stay intact).
    /// </summary>
    public static void CompactTruncate(int keepExchanges)
    {
        if (_instance == null)
        {
            RTQuickMessageManager.Get().ShowMessage("AI Chat is not open");
            return;
        }
        _instance.DoCompactTruncate(Mathf.Max(0, keepExchanges));
    }

    /// <summary>
    /// Settings-panel entry point: summarize everything older than the last
    /// <paramref name="keepExchanges"/> exchanges into one message via the active
    /// LLM (async), keeping the recent exchanges verbatim. Images are NOT touched.
    /// </summary>
    public static void CompactSummarize(int keepExchanges)
    {
        if (_instance == null)
        {
            RTQuickMessageManager.Get().ShowMessage("AI Chat is not open");
            return;
        }
        _instance.DoCompactSummarize(Mathf.Max(0, keepExchanges));
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
    /// True when the user has opted to ship raw image bytes to the active LLM
    /// session (legacy path, expensive). Default false: only the auto-caption +
    /// dimensions are sent, while the image still lives locally as a chat_image
    /// for skills like image_to_image.
    /// </summary>
    public static bool GetIncludeImageData()
    {
        return PlayerPrefs.GetInt(PREFS_INCLUDE_IMAGE_DATA, 0) != 0;
    }

    public static void SetIncludeImageData(bool v)
    {
        PlayerPrefs.SetInt(PREFS_INCLUDE_IMAGE_DATA, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Largest edge (in pixels) any dragged/pasted attachment is allowed to
    /// have. The attachment zone reads this at attach time and bilinear-scales
    /// oversized images down so the long edge fits, preserving aspect ratio.
    /// 0 (or any value &lt;= 0) means "do not resize". Default 1024.
    /// </summary>
    public static int GetAttachmentMaxEdge()
    {
        return PlayerPrefs.GetInt(PREFS_ATTACHMENT_MAX_EDGE, DEFAULT_ATTACHMENT_MAX_EDGE);
    }

    public static void SetAttachmentMaxEdge(int v)
    {
        // Clamp to sane bounds: 0 disables, otherwise must be at least 64 to
        // avoid pointlessly tiny images that the captioner can't make sense of.
        int clamped = v <= 0 ? 0 : Mathf.Clamp(v, 64, 8192);
        PlayerPrefs.SetInt(PREFS_ATTACHMENT_MAX_EDGE, clamped);
        PlayerPrefs.Save();
    }

    public static bool GetAutoContinueEnabled()
    {
        return PlayerPrefs.GetInt(PREFS_AUTO_CONTINUE_ON, 0) != 0;
    }

    public static void SetAutoContinueEnabled(bool v)
    {
        PlayerPrefs.SetInt(PREFS_AUTO_CONTINUE_ON, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static int GetAutoContinueCount()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(PREFS_AUTO_CONTINUE_COUNT, DEFAULT_AUTO_CONTINUE_COUNT));
    }

    public static void SetAutoContinueCount(int v)
    {
        PlayerPrefs.SetInt(PREFS_AUTO_CONTINUE_COUNT, Mathf.Max(0, v));
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

            // Drop any character anchors whose Pic just fell out of the numbered list,
            // so the ANCHORS line never advertises a name that can no longer resolve.
            if (_anchors.Count > 0)
            {
                var deadNames = new List<string>();
                foreach (var kv in _anchors)
                {
                    var pic = kv.Value;
                    if (pic == null || pic.gameObject == null || !_chatImagePics.Contains(pic))
                        deadNames.Add(kv.Key);
                }
                foreach (string name in deadNames)
                    _anchors.Remove(name);
            }
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
        // Display the message in the chat and queue it into the info recap, which is
        // folded into the tail of the user's NEXT outgoing message. This used to be
        // stored as a system-role interaction, but BuildPromptChat folds those into
        // the FRONT system message - growing the prompt head mid-conversation, which
        // invalidated the server-side prompt cache for the entire history every time
        // a skill emitted a note. The recap path is append-only at the request tail,
        // so the cached prefix survives; the LLM still sees the text on its next turn.
        AddSystemMessage(text);
    }

    void IChatHost.AddSystemInjectionSilent(string text)
    {
        // Same recap delivery (and cache reasoning) as AddSystemInjectionAndBubble,
        // minus the chat bubble - used for large-body injections (e.g. read_skill
        // dumping a full skill markdown body) where the user doesn't need to see the
        // content, just that something was loaded behind the scenes.
        if (!string.IsNullOrWhiteSpace(text))
            _infoMessages.Add(new InfoMessage(text));
    }

    void IChatHost.AppendImageBubbleForPic(SkillAction action, PicMain spawnedPic)
    {
        AppendImageBubble(action, spawnedPic);
    }

    byte[] IChatHost.GetChatImagePngBytes(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null) return null;
        return pic.TryGetImageAsPng(out byte[] png) ? png : null;
    }

    int IChatHost.GetChatImageCount()
    {
        if (_chatImagePics == null) return 0;
        return _chatImagePics.Count;
    }

    int IChatHost.GetLatestChatImageIndex()
    {
        if (_chatImagePics == null) return 0;
        for (int i = _chatImagePics.Count - 1; i >= 0; i--)
        {
            var pic = _chatImagePics[i];
            if (pic != null && pic.gameObject != null)
                return i + 1;
        }
        return 0;
    }

    bool IChatHost.TryPrepareChatImageForRead(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null) return false;
        if (pic.TryGetCurrentTexture(out var tex) && tex != null) return true;
        return pic.TryEnsureLoadedForChatSnapshot();
    }

    bool IChatHost.IsChatImagePicGenerating(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null) return false;
        return pic.IsBusy();
    }

    string IChatHost.GetChatImageCaption(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null) return "(world Pic was deleted; not reusable)";
        return pic.Caption ?? "";
    }

    private PicMain GetChatImagePic(int oneBasedIndex)
    {
        int idx0 = oneBasedIndex - 1;
        if (_chatImagePics == null || idx0 < 0 || idx0 >= _chatImagePics.Count) return null;
        var pic = _chatImagePics[idx0];
        if (pic == null || pic.gameObject == null) return null;
        return pic;
    }

    int IChatHost.ResolveAnchorToIndex(string anchorName)
    {
        if (string.IsNullOrWhiteSpace(anchorName) || _anchors == null) return 0;
        if (!_anchors.TryGetValue(anchorName.Trim(), out var pic) || pic == null || pic.gameObject == null)
        {
            // Unknown name, or its Pic was deleted/trimmed - drop the stale entry so the
            // ANCHORS line and future lookups stay honest.
            _anchors.Remove(anchorName.Trim());
            return 0;
        }
        int idx0 = _chatImagePics != null ? _chatImagePics.IndexOf(pic) : -1;
        if (idx0 < 0)
        {
            _anchors.Remove(anchorName.Trim());
            return 0;
        }
        return idx0 + 1; // 1-based slot, current as of right now
    }

    /// <summary>
    /// Build the "ANCHORS: Bob=#3, Elara=#5" line for the volatile CURRENT STATE block,
    /// listing only anchors whose Pic still has a live slot (resolving each name through
    /// the same path the executor uses, which also prunes dead entries). Returns "" when
    /// no live anchors exist, so the state block simply omits the line.
    /// </summary>
    private string BuildAnchorsStateLine()
    {
        if (_anchors == null || _anchors.Count == 0) return "";

        // Snapshot keys first: ResolveAnchorToIndex may remove dead entries mid-iteration.
        var names = new List<string>(_anchors.Keys);
        var parts = new List<string>();
        foreach (string name in names)
        {
            int idx = ((IChatHost)this).ResolveAnchorToIndex(name);
            if (idx > 0)
                parts.Add($"{name}=#{idx}");
        }
        if (parts.Count == 0) return "";
        return "ANCHORS (recurring characters - reference by NAME via chat_image=\"<name>\"): "
               + string.Join(", ", parts);
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
        {
            _unchainedPicsThisTurn.Add(spawnedPic);
            _chainTargetStale = false; // a fresh Pic landed - the head is a valid chain target again
        }
    }

    void IChatHost.MarkChainTargetStale()
    {
        _chainTargetStale = true;
    }

    PicMain IChatHost.ConsumeChainTarget()
    {
        // A fresh unchained spawn was attempted but hasn't succeeded (in progress / failed):
        // the head is not a valid chain target. Return null so a chained step doesn't attach
        // to a stale earlier Pic. SetLastSpawnedPicForTurn clears this the moment a spawn lands.
        if (_chainTargetStale)
            return null;

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

    PicMain IChatHost.PeekChainTarget()
    {
        // Same staleness rule as ConsumeChainTarget: if a fresh unchained spawn was attempted
        // but hasn't succeeded (in progress / FAILED), the head is invalid - a chained local
        // decorator must NOT fall back onto the previous page's Pic and corrupt it.
        if (_chainTargetStale)
            return null;

        // Non-consuming: SetLastSpawnedPicForTurn sets _lastSpawnedPicThisTurn and pushes
        // the LIFO in lockstep, so the head IS the stack top - returning it is a peek
        // without the pop. Chained LOCAL composition ops use this (instead of
        // ConsumeChainTarget) so border + body text + page number all decorate the SAME
        // most-recent Pic, rather than each popping a different (older) Pic off the stack.
        // Null-safe against a Pic destroyed mid-reply.
        if (_lastSpawnedPicThisTurn == null || _lastSpawnedPicThisTurn.gameObject == null)
            return null;
        return _lastSpawnedPicThisTurn;
    }

    private void RefreshHeaderTitle()
    {
        if (_titleText == null) return;
        // Title is just "AI Chat" - the provider/model used to be appended here
        // ("AI Chat - llama.cpp (model)"), but with multi-instance routing the
        // header can't name a single provider meaningfully, and it crowded the
        // header. The active LLM is shown in the status pill / settings instead.
        _titleText.text = "AI Chat";
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
            // While an Auto burst is running, keep the remaining auto-continue
            // count visible the whole time the turn streams (otherwise it only
            // flashes for one frame between turns and you can't tell how many
            // of the N you queued are still to come).
            string autoLeft = (_autoContinueToggle != null && _autoContinueToggle.isOn && _autoContinueRemaining > 0)
                ? $"   auto: {_autoContinueRemaining} left" : "";
            // ~4 chars per token is a good rough average for English text; the user
            // gets a sense of pace, not a token-exact count.
            if (_streamFirstTokenTime <= 0f)
            {
                // Prefill phase: nothing has arrived yet, so a t/s readout would just
                // decay toward zero. Show what the server is actually doing instead -
                // chewing through ~N prompt tokens (of the model's window, when
                // known) - and how long it's been at it.
                float waiting = Time.unscaledTime - _streamStartTime;
                int promptTokens = _streamPromptApproxChars / 4;
                string ctxOf = _streamMaxContextTokens > 0 ? $"/{FormatTokenCount(_streamMaxContextTokens)}" : "";
                _statusText.text = $"{spin} Prefill (~{FormatTokenCount(promptTokens)}{ctxOf} tok prompt)   {waiting:F0}s{autoLeft}";
            }
            else
            {
                _statusText.text = $"{spin} Talking to LLM   {BuildStreamStatsText()}{autoLeft}";
            }
        }

        // Live progress for the settings panel's "Summarize" compact - same spinner
        // treatment as streaming so the user can tell the one-shot summary request
        // is still working (it can take a minute+ on a long history). Streaming is
        // blocked while this runs, so the two never fight over the status line.
        if (_compactSummaryInFlight && !_isStreaming && _statusText != null
            && Time.unscaledTime >= _compactStatusNextRefresh)
        {
            _compactStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _compactSpinnerStep = (_compactSpinnerStep + 1) % StreamSpinnerFrames.Length;
            float elapsed = Time.unscaledTime - _compactSummaryStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_compactSpinnerStep]} Summarizing {_compactSummaryMsgCount} msgs   {elapsed:F0}s";
        }

        // Periodic header status pill refresh (cheap; reads counters from Config/LLM mgr).
        if (Time.unscaledTime >= _statusPillNextRefresh)
        {
            _statusPillNextRefresh = Time.unscaledTime + STATUS_PILL_REFRESH_INTERVAL;
            RefreshHeaderTitle();
            UpdateStatusPill();
        }
    }

    private void LateUpdate()
    {
        // Enter / Shift+Enter handling for the chat input. Must run in LateUpdate, not
        // Update: TMP_InputField consumes the keystroke from EventSystem.Update(), whose
        // order relative to our own Update() is undefined. When we ran first, the field
        // got cleared by the send and TMP then dropped its '\n' into the EMPTY field,
        // leaving a stray blank line behind after every send. LateUpdate guarantees TMP
        // has already processed the key. (Not handled via TMP's own MultiLineSubmit mode
        // or onValidateInput because both are unreliable about reading the Shift
        // modifier in Unity 6 / TMP 3.)
        if (_isVisible && _inputField != null && _inputField.isFocused
            && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shift)
            {
                // Plain Enter: lineType=MultiLineNewline inserted a '\n' AT THE CARET
                // this frame - which is not necessarily the end of the text (the user
                // may send right after jumping back to fix a typo). Remove that exact
                // character, otherwise the message goes out with a newline embedded
                // wherever the caret happened to sit.
                string text = _inputField.text ?? "";
                int caretIdx = Mathf.Clamp(_inputField.stringPosition, 0, text.Length);
                if (caretIdx > 0 && text[caretIdx - 1] == '\n')
                    _inputField.text = text.Remove(caretIdx - 1, 1);
                else if (text.EndsWith("\n"))
                    _inputField.text = text.Substring(0, text.Length - 1);
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
/// the target so the opposite edge stays fixed. Min size enforced. On pointer hover the
/// system cursor swaps to a directional resize arrow generated procedurally - no asset
/// imports required, no Windows-specific P/Invoke.
/// </summary>
public class PanelResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    private RectTransform _target;
    private Vector2 _minSize = new Vector2(200, 200);
    private Vector2 _resizeDirection = new Vector2(1f, -1f);
    private Action _onResized;
    private Vector2 _startPointerLocal;
    private Vector2 _startSize;
    private Vector2 _startAnchoredPosition;

    private enum ResizeCursorKind { None, Horizontal, Vertical, DiagonalNWSE, DiagonalNESW }
    private ResizeCursorKind _cursorKind = ResizeCursorKind.DiagonalNWSE;
    private bool _cursorActive;

    private const int CursorTexSize = 32;
    private static readonly Vector2 CursorHotspot = new Vector2(CursorTexSize / 2f, CursorTexSize / 2f);
    private static Texture2D _hCursor, _vCursor, _nwseCursor, _neswCursor;

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
        _cursorKind = DeriveCursorKind(resizeDirection);
    }

    private static ResizeCursorKind DeriveCursorKind(Vector2 dir)
    {
        bool hasX = Mathf.Abs(dir.x) > 0.01f;
        bool hasY = Mathf.Abs(dir.y) > 0.01f;
        if (hasX && hasY)
            return Mathf.Sign(dir.x) == Mathf.Sign(dir.y) ? ResizeCursorKind.DiagonalNESW : ResizeCursorKind.DiagonalNWSE;
        if (hasX) return ResizeCursorKind.Horizontal;
        if (hasY) return ResizeCursorKind.Vertical;
        return ResizeCursorKind.None;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var tex = GetCursorTexture(_cursorKind);
        if (tex == null) return;
        Cursor.SetCursor(tex, CursorHotspot, CursorMode.Auto);
        _cursorActive = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ResetCursorIfActive();
    }

    private void OnDisable()
    {
        // Belt-and-suspenders: if the panel hides while the pointer is over us, OnPointerExit
        // may not fire. Without this the OS cursor would stay as the resize arrow until the
        // user hovers something else that resets it.
        ResetCursorIfActive();
    }

    private void ResetCursorIfActive()
    {
        if (!_cursorActive) return;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        _cursorActive = false;
    }

    private static Texture2D GetCursorTexture(ResizeCursorKind kind)
    {
        switch (kind)
        {
            case ResizeCursorKind.Horizontal:
                if (_hCursor == null) _hCursor = BuildCursorTexture(kind);
                return _hCursor;
            case ResizeCursorKind.Vertical:
                if (_vCursor == null) _vCursor = BuildCursorTexture(kind);
                return _vCursor;
            case ResizeCursorKind.DiagonalNWSE:
                if (_nwseCursor == null) _nwseCursor = BuildCursorTexture(kind);
                return _nwseCursor;
            case ResizeCursorKind.DiagonalNESW:
                if (_neswCursor == null) _neswCursor = BuildCursorTexture(kind);
                return _neswCursor;
            default:
                return null;
        }
    }

    // Texture y=0 is the BOTTOM row but Cursor.SetCursor draws the texture as-is with its
    // hotspot measured from the top-left of the rendered cursor. For symmetric horizontal
    // / vertical arrows that's irrelevant; for diagonals we carefully map "visual top"
    // (i.e. small screen-y) to the HIGH y rows of the pixel array.
    private static Texture2D BuildCursorTexture(ResizeCursorKind kind)
    {
        const int W = CursorTexSize, H = CursorTexSize;
        var px = new Color[W * H]; // default (0,0,0,0) = transparent
        int cx = W / 2, cy = H / 2;
        Color fill = Color.white;

        switch (kind)
        {
            case ResizeCursorKind.Horizontal:
                for (int x = 7; x <= 24; x++)
                    for (int dy = -1; dy <= 1; dy++)
                        SetPx(px, W, H, x, cy + dy, fill);
                for (int x = 2; x <= 7; x++)
                {
                    int half = x - 2;
                    for (int y = cy - half; y <= cy + half; y++) SetPx(px, W, H, x, y, fill);
                }
                for (int x = 24; x <= 29; x++)
                {
                    int half = 29 - x;
                    for (int y = cy - half; y <= cy + half; y++) SetPx(px, W, H, x, y, fill);
                }
                break;

            case ResizeCursorKind.Vertical:
                for (int y = 7; y <= 24; y++)
                    for (int dx = -1; dx <= 1; dx++)
                        SetPx(px, W, H, cx + dx, y, fill);
                for (int y = 2; y <= 7; y++)
                {
                    int half = y - 2;
                    for (int x = cx - half; x <= cx + half; x++) SetPx(px, W, H, x, y, fill);
                }
                for (int y = 24; y <= 29; y++)
                {
                    int half = 29 - y;
                    for (int x = cx - half; x <= cx + half; x++) SetPx(px, W, H, x, y, fill);
                }
                break;

            case ResizeCursorKind.DiagonalNWSE:
            case ResizeCursorKind.DiagonalNESW:
            {
                // NWSE = "↖↘" : visual top-left to visual bottom-right
                // NESW = "↗↙" : visual top-right to visual bottom-left
                // In array coords (y=0 at bottom), top-left = (small x, large y), bottom-right = (large x, small y).
                bool nwse = (kind == ResizeCursorKind.DiagonalNWSE);
                int x0 = 7,  y0 = nwse ? H - 1 - 7  : 7;
                int x1 = 24, y1 = nwse ? H - 1 - 24 : 24;
                int steps = 18;
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                    int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                    // Plus-shaped brush, 2px effective thickness along the diagonal.
                    SetPx(px, W, H, x,     y,     fill);
                    SetPx(px, W, H, x - 1, y,     fill);
                    SetPx(px, W, H, x + 1, y,     fill);
                    SetPx(px, W, H, x,     y - 1, fill);
                    SetPx(px, W, H, x,     y + 1, fill);
                }
                if (nwse)
                {
                    // NW (visual top-left): array (2..8, 23..29). Filled corner.
                    FillTri(px, W, H, 2, 29, 8, 29, 2, 23, fill);
                    // SE (visual bottom-right): array (23..29, 2..8).
                    FillTri(px, W, H, 29, 2, 23, 2, 29, 8, fill);
                }
                else
                {
                    // NE (visual top-right): array (23..29, 23..29).
                    FillTri(px, W, H, 29, 29, 23, 29, 29, 23, fill);
                    // SW (visual bottom-left): array (2..8, 2..8).
                    FillTri(px, W, H, 2, 2, 8, 2, 2, 8, fill);
                }
                break;
            }
        }

        // 1-pixel black outline so the cursor stays legible on white / light UIs.
        AddOutline(px, W, H, Color.black);

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static void SetPx(Color[] px, int W, int H, int x, int y, Color c)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        px[y * W + x] = c;
    }

    private static void FillTri(Color[] px, int W, int H, int x0, int y0, int x1, int y1, int x2, int y2, Color c)
    {
        int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
        int maxX = Mathf.Min(W - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
        int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
        int maxY = Mathf.Min(H - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int d1 = (x - x1) * (y0 - y1) - (x0 - x1) * (y - y1);
                int d2 = (x - x2) * (y1 - y2) - (x1 - x2) * (y - y2);
                int d3 = (x - x0) * (y2 - y0) - (x2 - x0) * (y - y0);
                bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
                bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(hasNeg && hasPos)) px[y * W + x] = c;
            }
        }
    }

    private static void AddOutline(Color[] px, int W, int H, Color outline)
    {
        var dst = new Color[W * H];
        Array.Copy(px, dst, px.Length);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (px[y * W + x].a > 0.5f) continue;
                bool nearFill = false;
                for (int dy = -1; dy <= 1 && !nearFill; dy++)
                {
                    for (int dx = -1; dx <= 1 && !nearFill; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        if (px[ny * W + nx].a > 0.5f) nearFill = true;
                    }
                }
                if (nearFill) dst[y * W + x] = outline;
            }
        }
        Array.Copy(dst, px, px.Length);
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
    private const int CaretWidth = 5;

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
