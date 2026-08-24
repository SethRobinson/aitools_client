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
using AITools.AIChat.Audio;
using AITools.AIChat.Video;
using AITools.AIChat.Web;

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
    // and aichat prompt files; ChatContextBuilder builds the STABLE system prompt
    // (cache-friendly - it only changes when prompt/skill files change) plus the
    // volatile CURRENT STATE block (GPU busy/idle, chat-image captions) that gets
    // appended to each outgoing user message at send time; SkillActionParser extracts
    // <aitools_action> tags from the LLM's stream; SkillActionExecutor dispatches them
    // to the rest of the app (PicMain.RunPresetByName, LLM delegation, etc.).
    private SkillManager _skillManager;
    private ChatContextBuilder _contextBuilder;
    private SkillActionParser _actionParser;
    private SkillActionExecutor _actionExecutor;
    // Keyword-autoloaded skill bodies ride the info-recap tail of the triggering
    // user message (the same path read_skill bodies use), NOT a system-role
    // interaction: BuildPromptChat folds system-role lines into the FRONT system
    // message, and rewriting the prompt head mid-conversation invalidated the
    // server-side prompt cache for the entire history every time a new skill
    // triggered (a ~40s full re-prefill on a long llama.cpp chat). Liveness is
    // derived by scanning history for these marker headers (see
    // ComputeLiveAutoloadSkillIds), so Rewind/Compact/Clear self-heal with no
    // stored id list. ReadSkillBodyMarkerPrefix must match the injection header in
    // SkillActionExecutor.ExecuteReadSkill.
    private const string AutoloadSkillBodyMarkerPrefix = "AUTO-LOADED SKILL REFERENCE '";
    private const string ReadSkillBodyMarkerPrefix = "Reference material for skill '";

    // Deictic movie-edit requests often omit the nouns in video_to_video's normal
    // trigger list: after dropping a Movie, users naturally say "change this scene"
    // or "make him say ...". Match those phrases only when the newest live chat
    // medium is actually a Movie, so identical wording beside a still image keeps
    // routing to image_to_image.
    private static readonly string[] MovieContextVideoEditPhrases =
    {
        "change this scene", "change that scene", "change the scene",
        "edit this scene", "edit that scene", "edit the scene",
        "modify this scene", "modify that scene", "modify the scene",
        "alter this scene", "alter that scene", "alter the scene",
        "redo this scene", "redo that scene", "redo the scene",
        "remake this scene", "remake that scene", "remake the scene",
        "rework this scene", "rework that scene", "rework the scene"
    };
    private static readonly Regex MovieContextEditRx = new Regex(
        @"\b(?:make|have|let)\s+(?:him|her|them|it|(?:the\s+)?[\w'-]+(?:\s+[\w'-]+){0,3})\s+(?:say|speak|talk|sing|fart|burp|laugh|cry|move|walk|run|dance|jump|turn|smile|wave|fall|stand|sit|look|wear|become)\b|" +
        @"\b(?:starts?|begins?)\s+(?:speaking|talking|singing|farting|burping|laughing|crying|moving|walking|running|dancing|jumping)\b|" +
        @"\b(?:change|replace|rewrite|add|remove)\s+(?:(?:the|their|his|her)\s+)?(?:dialogue|dialog|spoken\s+line|line|speech|voice|audio|soundtrack|sound\s+effects?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Body text (post preset-prefix substitution) last delivered per skill id, so the
    // per-turn skill-file reload re-sends only genuinely edited bodies. Not a
    // liveness record - that is always re-derived from history.
    private readonly Dictionary<string, string> _sentAutoloadSkillBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Header status pill - GPU busy count + LLM count, refreshed periodically.
    private TextMeshProUGUI _statusPillText;
    private float _statusPillNextRefresh;
    private const float STATUS_PILL_REFRESH_INTERVAL = 1.5f;

    // AI Chat-only main model override. Default (-1) preserves normal Big/Small/Vision
    // routing; selecting an instance forces only the main chat turn to that instance.
    private TMP_Dropdown _mainLLMDropdown;
    private TextMeshProUGUI _mainLLMLabelText;
    private TextMeshProUGUI _mainLLMCaptionText;
    private TextMeshProUGUI _mainLLMArrowText;
    private readonly List<int> _mainLLMDropdownInstanceIds = new List<int>();
    private LLMInstanceManager _subscribedInstanceManager;
    private bool _waitingForForcedMainLLM = false;
    private Coroutine _forcedMainLLMWaitCoroutine;
    private int _waitingForcedMainLLMId = -1;
    private string _waitingForcedMainLLMName = "";
    private const int MAIN_LLM_DEFAULT_ID = -1;
    private const int QUEUED_MAIN_LLM_OVERRIDE_UNSET = int.MinValue;

    // Per-turn attachments: a defensive copy of the user's pasted images at OnSendClicked
    // time, so a SkillActionExecutor invoked mid-stream can still resolve attachment="N"
    // even after ChatImageAttachmentZone has cleared its own thumbnail strip.
    private List<byte[]> _lastTurnAttachments = new List<byte[]>();

    // The PicMains the MOST RECENT paste group was promoted into, parallel to
    // _lastTurnAttachments (position k-1 = attachment "k"; null = that attachment
    // failed to decode/promote). Unlike _lastTurnAttachments this survives synthetic
    // continue turns and later sends, so a stale attachment="N" emitted turns after
    // the paste can still be resolved deterministically to the bubble the model
    // meant. Stores Pic REFERENCES, not slot numbers, for the same renumbering
    // reason as _anchors; dead refs simply fail to resolve.
    private readonly List<PicMain> _lastPasteGroupPics = new List<PicMain>();

    // Tracks "Info" bubbles (warnings/notes from skill execution, etc.) so that on
    // the user's NEXT send we can quietly recap any messages the LLM hasn't already
    // seen - giving it a chance to learn from its own mistakes without forcing the
    // user to copy-paste them. Bubbles authored as pure UI confirmations (e.g.
    // "New chat", "Conversation cleared") opt out via includeInLLMRecap=false at
    // the AddSystemMessage call site. Cleared with the rest of the chat in
    // OnClearClicked so a fresh conversation starts with no carry-over.
    private const string InfoRecapMarker = "\n\n---\nAlso, for the future, please keep this in mind:";

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
    private readonly HashSet<PicMain> _videoCaptionInFlight = new HashSet<PicMain>();

    // Stable per-session list of chat-image bubbles (1-based via index+1). Lets the LLM
    // reference "the image you generated in turn 3" via chat_image="3". Only cleared on
    // OnClearClicked; persists across turns. Entries can become stale if the user deletes
    // the world Pic - we just return null on read in that case.
    private readonly List<PicMain> _chatImagePics = new List<PicMain>();

    private class ChatImageRecord
    {
        public PicMain pic;
        public bool isUserAttachment;
        public bool isMovie;
        public string kind;
        public string anchorName;
        public string dimensions;
        public byte[] cleanBasePngBytes;
        public string cleanBaseDimensions;
        public readonly List<string> provenanceSteps = new List<string>();
        // Caption is always described in CHAT IMAGES, even when generated-image
        // auto-captioning is off: set for media whose whole point is that the model
        // can see what arrived (web downloads, extracted identity frames).
        public bool alwaysIncludeCaption;
        // "Audio #N" bubbles: a sound file (generated music / sfx / speech, or a dropped-in
        // audio file) displayed through a waveform preview MOVIE, so isMovie is true too and
        // every Movie-based path (playback, save, clip, stitch) works unchanged. audioPath is
        // the original sound file (what set_video_audio mixes and generate_speech clones).
        public bool isAudio;
        public string audioPath;
        public double durationSeconds;
    }
    private readonly List<ChatImageRecord> _chatImageRecords = new List<ChatImageRecord>();

    // Character-anchor registry: maps a character NAME ("Bob") to the PicMain that is
    // currently its canonical anchor. Stores the Pic REFERENCE, not a slot number,
    // because chat_image numbers shift downward whenever TrimMediaToKeepLastN pops old
    // bubbles off the head of _chatImagePics - a name must keep pointing at the right
    // image even after a renumber. Declared via anchor="Name" on a generate_image /
    // image_to_image action; re-declaring an existing name re-points it (the "Bob
    // changed clothes" update path). Cleared with the chat; dead entries pruned on trim.
    private readonly Dictionary<string, PicMain> _anchors = new Dictionary<string, PicMain>(StringComparer.OrdinalIgnoreCase);

    // For right-click rewind: each live prompt-history line records how many AI Chat
    // media bubbles existed immediately after that line became part of the conversation.
    // Rewind can then keep the clicked line and trim only later text/media context.
    private readonly Dictionary<GTPChatLine, int> _interactionMediaCheckpoints = new Dictionary<GTPChatLine, int>();
    private GameObject _bubbleContextMenuRoot;
    private GameObject _rewindConfirmRoot;
    private GameObject _speechSelectionOverlayRoot;
    private TMP_InputField _cachedSpeakSelectionField;
    private string _cachedSpeakSelectionFieldText;
    private string _cachedSpeakSelectionText;
    private int _cachedSpeakSelectionStart;
    private int _cachedSpeakSelectionEnd;

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
    private const float MIN_SCROLLBAR_HANDLE_PIXELS = 32f;
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
    // Prompt slimming switches. Keep old tool XML by default so follow-up turns can
    // byte-match the previous assistant output and reuse llama.cpp's prompt cache.
    // Generated-image auto-captioning still defaults off to avoid sidecar work.
    private const string PREFS_KEEP_OLD_TOOL_CALLS_IN_PROMPT = "aichat_keep_old_tool_calls_in_prompt";
    private const string PREFS_AUTO_CAPTION_GENERATED_IMAGES = "aichat_auto_caption_generated_images";
    private const string PREFS_SHOW_DEBUG_STUFF = "aichat_show_debug_stuff";
    // Header "Web" checkbox: gates every web_* skill (search / image / video / page). Default on.
    private const string PREFS_WEB_ENABLED = "aichat_web_enabled";
    // Cap on the largest edge (in pixels) of dragged/pasted images. Anything
    // bigger gets bilinear-downscaled at attach time so that captioning,
    // image_to_image source bytes, and chat-history embedding all run against
    // a sane payload. 0 = no resize.
    private const string PREFS_ATTACHMENT_MAX_EDGE = "aichat_attachment_max_edge";
    private const int DEFAULT_ATTACHMENT_MAX_EDGE = 1024;
    // Auto repeat msg: when the toggle is checked, automatically re-send whatever
    // is in the input box, once per completed reply, up to N times total - then
    // auto-uncheck. The checkbox is session-only and always starts OFF (never
    // persisted); only the N count below is saved across sessions.
    private const string PREFS_AUTO_CONTINUE_COUNT = "aichat_auto_continue_count";
    private const int DEFAULT_AUTO_CONTINUE_COUNT = 10;
    // Compact: how many of the most recent user->assistant exchanges to keep
    // verbatim when the user compacts the chat (either by plain truncation or
    // by summarizing everything older into one message). Shared by both modes.
    private const string PREFS_COMPACT_KEEP_N = "aichat_compact_keep_n";
    private const int DEFAULT_COMPACT_KEEP_N = 5;
    // Prompt-side cap for the volatile CHAT IMAGES list. Media stays available
    // locally; this only bounds the repeated per-turn text sent to the LLM.
    private const string PREFS_IMAGE_CONTEXT_LIMIT = "aichat_image_context_limit";
    private const int DEFAULT_IMAGE_CONTEXT_LIMIT = 40;
    private const int MAX_IMAGE_CONTEXT_LIMIT = 200;
    private const string PREFS_MAIN_LLM_INSTANCE_ID = "aichat_main_llm_instance_id";
    private static string _userPostMessage = "";

    // Footer
    private TMP_InputField _inputField;
    private TMPInputFieldUndo _inputUndo;
    private RectTransform _inputFieldRT;
    private Button _sendButton;
    private Button _clearButton;
    private Button _stopButton;
    private Button _copyButton;
    private TextMeshProUGUI _speechStatusText;
    private Button _speechStopButton;
    private Toggle _includeImageDataToggle;
    private Toggle _autoContinueToggle;
    private Toggle _webToggle;
    private TMP_InputField _autoContinueCountInput;
    // Internal countdown for the auto-repeat burst. Seeded from the N field when
    // the toggle is checked; decremented as each repeat fires. Drained on
    // Stop/Clear/abort or when the toggle is unchecked (including the auto-uncheck
    // when the burst finishes).
    private int _autoContinueRemaining = 0;
    // Latch marking that the current OnSendClicked call came from an auto-fire
    // rather than a manual Send / Enter press.
    private bool _autoContinueFiring = false;

    // Session-only footer prompt history. This deliberately does not use PlayerPrefs:
    // it behaves like shell history for the current app run only.
    private const int PROMPT_HISTORY_MAX_ENTRIES = 100;
    private readonly List<string> _promptHistory = new List<string>();
    private int _promptHistoryIndex = -1; // -1 means the live draft, not a history row.
    private string _promptHistoryDraft = "";
    private bool _applyingPromptHistoryText = false;
    private bool _promptHistoryCaretCacheValid = false;
    private int _promptHistoryLastCaretLine = 0;
    private int _promptHistoryLastLineCount = 1;
    private bool _promptHistoryLastHadSelection = false;
    private TextMeshProUGUI _statusText;

    // Image attachments (drag-drop / clipboard paste) - all the heavy lifting (drop
    // intercept, paste-from-clipboard, thumbnail strip UI) lives in ChatImageAttachmentZone.
    // We just own the strip container's RectTransform plus the footer/chat-area rects so
    // we can resize them when the strip appears/disappears.
    private ChatImageAttachmentZone _attachmentZone;
    private RectTransform _attachmentsStrip;
    private RectTransform _attachmentsContent;
    private ScrollRect _attachmentsScroll;
    private ChatVideoClipChooser _videoClipChooser;
    private int _videoImportCount = 0;
    private int _videoImportEpoch = 0;
    private float _videoImportStartTime = 0f;
    private float _videoImportStatusNextRefresh = 0f;
    private int _videoImportSpinnerStep = 0;
    // Footer wording for the shared import gate ("Importing video" / "Generating music" /
    // "Mixing audio"): audio generation and set_video_audio reuse BeginVideoImport so Send
    // stays blocked exactly like a clip import.
    private string _videoImportStatusLabel = "Importing video";
    private float _videoCaptionStartTime = 0f;
    private float _videoCaptionStatusNextRefresh = 0f;
    private int _videoCaptionSpinnerStep = 0;
    private RectTransform _footerRT;
    // User-resizable footer (input box) height. FOOTER_HEIGHT is the floor/default; the
    // FooterResizeHandle on the footer's top bar drags this taller, shrinking the body
    // above. The attachments strip height is added on top of this, not baked into it.
    private float _footerBaseHeight = FOOTER_HEIGHT;
    private const float MIN_BODY_HEIGHT = 140f; // never shrink the columns above below this
    private const float ATTACHMENT_STRIP_HEIGHT = 70f;
    private const int MAX_CHAT_ATTACHMENTS = 99;
    private const float FOOTER_RIGHT_RESERVED_WIDTH = 200f;
    private const float MAIN_LLM_FOOTER_RESERVED_WIDTH = 274f;

    // Watchdog timeout for vision-LLM caption requests. Local Ollama / llama.cpp
    // models occasionally hang on a particular input or after long uptime; without
    // a force-release the LLM slot stays marked busy forever and the user can't
    // get the slot back. After this timeout we decrement the busy count and treat
    // the caption as failed.
    private const float CAPTION_TIMEOUT_SECONDS = 60f;
    private const float INSPECT_IMAGE_TIMEOUT_SECONDS = 300f;
    private const string InspectImageSystemPrompt =
        "You are a vision inspection helper inside an image generation app. " +
        "Answer only from the pixels in the attached image; do not trust the requested prompt or prior chat over visible evidence. " +
        "If the user message says transparency was visualized as a gray checkerboard, treat checkerboard pixels as transparent alpha, not real image content. " +
        "When the user prompt asks to check, verify, QA, find problems, compare to a request, or inspect layout/text, start with PASS or FAIL, then list defects first. " +
        "For comics, posters, covers, grids, storyboards, and captioned images, mark FAIL for title/text touching or overlapping unrelated artwork, unreadable or clipped text, duplicated text, bad gutters, blank/black panels, missing panels, or obvious wrong subject matter. " +
        "Name the affected region such as top-left, title band, upper-right panel, or bottom gutter. Be concise and specific.";
    private const string InspectAlphaVisualizationNote =
        "Transparency note: The attached PNG had an alpha channel. For this vision inspection only, it has been composited over a gray checkerboard. Checkerboard pixels mean transparent alpha; blended pixels mean partial alpha. Judge visible/hidden regions from this checkerboard composite and ignore hidden RGB data in fully transparent pixels.";

    // Throttle for the "no vision-capable LLM" warning bubble. Both caption callers
    // (attachment drop, generated-pic mirror) funnel through TryCaptionBytes, and a
    // multi-image drop or a generation batch would otherwise stack identical bubbles.
    // Time-based so it self-resets without needing reset hooks on clear/new-chat.
    private const float NO_VISION_WARN_THROTTLE_SECONDS = 30f;
    private float _lastNoVisionWarnTime = -999f;

    // Watchdog bounds for the one-shot "compact to summary" LLM request. The actual
    // deadline scales with transcript size in DoCompactSummarize: summarizing a very
    // long chat on a local model is dominated by prompt prefill (a ~180-turn /
    // ~113k-token conversation measured ~6 minutes on llama.cpp before any output),
    // so a flat timeout fired first and the good summary arriving later was
    // discarded by the done-latch. The watchdog is only a safety net - transport
    // errors surface through the request's own callback.
    private const float COMPACT_TIMEOUT_MIN_SECONDS = 300f;
    private const float COMPACT_TIMEOUT_MAX_SECONDS = 1800f;
    // Internal tag on the summary GTPChatLine so RebuildChatBubblesFromHistory can
    // render it as a first-class, always-visible, EDITABLE "Summary" bubble (the
    // user verifies/corrects what the model distilled) instead of a debug-gated
    // Info bubble like other system-role context lines.
    private const string COMPACT_SUMMARY_TAG = "aichat_compact_summary";
    private static readonly Color SummaryLabelColor = new Color(0.34f, 0.24f, 0.55f);
    private static readonly Color SummaryBubbleBg = new Color(0.93f, 0.91f, 0.98f, 1f);
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
    // Rough size of the in-flight summarize request (chars/4), shown in the status
    // line so the user can gauge how big a prefill the server is chewing on.
    private int _compactSummaryApproxSentTokens = 0;
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

    private class AttachmentCaptionRequest
    {
        public int id;
        public byte[] png;
    }

    private class InspectImageRequest
    {
        public int id;
        public byte[] png;
        public string prompt;
        public string sourceLabel;
        public bool alphaVisualized;
        public int? llmInstanceId;
        public bool resumeOnResult;
        public int resumeTurnEpoch;
    }

    private class InspectImageJob
    {
        public int requestId;
        public string sourceLabel;
        public bool completed;
        public bool cancelled;
        public int targetId = -1;
        public int replicaIndex;
        public float startTime;
        public Coroutine watchdog;
        public bool resumeOnResult;
        public int resumeTurnEpoch;
    }

    // Outstanding caption jobs keyed by attachment id. Populated when an attachment
    // arrives, drained either by completion or by the user clicking the X.
    private readonly Dictionary<int, CaptionJob> _captionJobs = new Dictionary<int, CaptionJob>();
    private readonly List<AttachmentCaptionRequest> _attachmentCaptionQueue = new List<AttachmentCaptionRequest>();
    private float _attachmentCaptionStartTime = 0f;
    private float _attachmentCaptionNextDispatch = 0f;
    private float _attachmentCaptionStatusNextRefresh = 0f;
    private int _attachmentCaptionSpinnerStep = 0;
    private readonly List<InspectImageRequest> _inspectImageQueue = new List<InspectImageRequest>();
    private InspectImageJob _inspectImageJob;
    private int _nextInspectImageRequestId = 1;
    private float _inspectImageNextDispatch = 0f;
    private float _inspectImageStatusNextRefresh = 0f;
    private int _inspectImageSpinnerStep = 0;
    private int _chatTurnEpoch = 0;
    private bool _inspectAutoResumePending = false;
    private bool _inspectAutoResumeScheduled = false;
    private int _inspectAutoResumeTurnEpoch = -1;
    private bool _skillLoadAutoResumePending = false;
    private bool _skillLoadAutoResumeScheduled = false;
    private int _skillLoadAutoResumeTurnEpoch = -1;
    private readonly List<string> _skillLoadAutoResumeIds = new List<string>();
    // Model-requested continue (the `continue` control action). Same scoped-resume
    // pattern as the inspect/skill-load pair above, but driven entirely by the model
    // deciding it needs another turn. Guarded by a consecutive-self-continue counter
    // so a stuck model can't loop forever; the counter resets on a real user send.
    private bool _genericContinuePending = false;
    private bool _genericContinueScheduled = false;
    private int _genericContinueTurnEpoch = -1;
    private int _consecutiveSelfContinues = 0;
    private const int MaxConsecutiveSelfContinues = 6;

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
    private const float RESIZE_CORNER_SIZE = 24f;
    private const float HEADER_RIGHT_RESIZE_EXCLUSION = 270f;
    private const float SCROLL_BOTTOM_PIXEL_EPSILON = 12f;
    private const string ChatPrimaryFontResourcePath = "Fonts & Materials/LiberationSans SDF";
    private const string ChatCjkFallbackFontName = "NotoSansCJKjp-VF SDF";
    private const float BaseFontSize = 14f;
    private const float BaseLabelFontSize = 12f;

    // Ctrl+MouseWheel font resize. Multiplier scales BaseFontSize (and the smaller
    // role label font) so the user can read the chat at any size they like. Reset
    // each session because Show() lazy-creates a fresh panel.
    private const float DefaultFontMultiplier = 1.2f;
    private float _fontSizeMultiplier = DefaultFontMultiplier;
    private const float MinFontMultiplier = 0.5f;
    private const float MaxFontMultiplier = 3.0f;
    private const float FontMultiplierStep = 0.1f;
    private int _fontResizeScrollRestoreVersion;

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
            // Reload aichat config in case the user edited a skill or prompt file.
            // outside the app between toggles. Cheap.
            _instance._skillManager?.Reload();
            _instance.SubscribeToLLMInstanceChanges();
            _instance.RefreshMainLLMDropdownOptions();
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
        if (!visible)
        {
            HideBubbleContextMenu();
            HideRewindConfirmation();
            ClearSpeechSelectionOverlay();
            ClearCachedSpeakSelection();
        }
        _isVisible = visible;
        if (_mainPanel != null)
            _mainPanel.gameObject.SetActive(visible);
        if (_captionTooltipRoot != null && !visible)
            _captionTooltipRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        CancelForcedMainLLMWait(showBubble: false);
        CancelAllAttachmentCaptions();
        CancelAllInspectImageJobs(showBubble: false);
        UnsubscribeFromLLMInstanceChanges();
        ClearSpeechSelectionOverlay();
        ClearCachedSpeakSelection();
        if (_skillManager != null)
            _skillManager.OnSkillListChanged -= OnSkillListChanged;
        if (_videoClipChooser != null)
        {
            Destroy(_videoClipChooser.gameObject);
            _videoClipChooser = null;
        }
        // The ChatImageAttachmentZone component on _panelRoot auto-deregisters and frees
        // its textures in its own OnDestroy.
        _instance = null;
        _panelRoot = null;
    }

    private TMP_FontAsset FindFont()
    {
        var primary = Resources.Load<TMP_FontAsset>(ChatPrimaryFontResourcePath);
        if (primary == null)
        {
            var existing = FindAnyObjectByType<TextMeshProUGUI>();
            primary = existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        EnsureChatFontFallbacks(primary);
        return primary != null ? primary : TMP_Settings.defaultFontAsset;
    }

    private static void EnsureChatFontFallbacks(TMP_FontAsset primary)
    {
        if (primary == null) return;

        var cjk = FindCjkFallback(primary);
        if (cjk == null) return;

        if (primary.fallbackFontAssetTable == null)
            primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (!primary.fallbackFontAssetTable.Contains(cjk))
            primary.fallbackFontAssetTable.Add(cjk);

        if (TMP_Settings.fallbackFontAssets == null)
            TMP_Settings.fallbackFontAssets = new List<TMP_FontAsset>();
        if (!TMP_Settings.fallbackFontAssets.Contains(cjk))
            TMP_Settings.fallbackFontAssets.Add(cjk);
    }

    private static TMP_FontAsset FindCjkFallback(TMP_FontAsset primary)
    {
        if (primary != null && primary.fallbackFontAssetTable != null)
        {
            foreach (var fallback in primary.fallbackFontAssetTable)
            {
                if (fallback != null && fallback.name == ChatCjkFallbackFontName)
                    return fallback;
            }
        }

        var guide = AIGuideManager.Get();
        var guideFont = guide != null ? guide.GetFontByName(ChatCjkFallbackFontName) : null;
        if (guideFont != null) return guideFont;

        foreach (var loadedFont in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (loadedFont != null && loadedFont.name == ChatCjkFallbackFontName)
                return loadedFont;
        }

        return null;
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
        _panelRoot.AddComponent<AIChatCtrlWheelScrollSuppressor>();

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
        ApplyChatFontSize();
        CreateResizeGrip();
        SubscribeToLLMInstanceChanges();
        RefreshMainLLMDropdownOptions();

        // Skills system. Loads aichat prompt files and aichat/skills/*.md, wires up
        // the parser->executor pipeline. Parser fires per parsed tag; executor reaches
        // back into the panel via the IChatHost interface to spawn pics, inject system
        // messages, etc.
        _skillManager = new SkillManager();
        _skillManager.OnSkillListChanged += OnSkillListChanged;
        _skillManager.Reload();
        _contextBuilder = new ChatContextBuilder(_skillManager);
        _actionParser = new SkillActionParser();
        _actionExecutor = new SkillActionExecutor(_skillManager, this);
        _actionParser.OnActionParsed += OnSkillActionParsed;

        RefreshHeaderTitle();
        UpdateStatusPill();
        AddWelcomeMessage();
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

        // "Web" checkbox: allows / denies every web_* skill (search, image, video, page). Sits in
        // the gap between the status pill (right edge at -200) and Settings (left edge at -124).
        // Persisted in PlayerPrefs; the model sees the state in CURRENT STATE every turn and the
        // executor's WebPreflight refuses web actions while it is off.
        _webToggle = CreateFooterToggle(header.transform, "Web", Vector2.zero, new Vector2(60, 22), GetWebEnabled(), OnWebToggleChanged);
        {
            var wrt = _webToggle.GetComponent<RectTransform>();
            wrt.anchorMin = new Vector2(1, 0.5f);
            wrt.anchorMax = new Vector2(1, 0.5f);
            wrt.pivot = new Vector2(1, 0.5f);
            wrt.anchoredPosition = new Vector2(-130, 0);
            var tt = _webToggle.gameObject.AddComponent<RTToolTip>();
            tt._text =
                "Allow AI Chat to search the web and fetch pages, images and clips\n" +
                "(web_search / web_image / web_video / web_page; Brave key in Settings > Web).\n" +
                "Off: the model is told web access is disabled and any web action fails.";
        }

        RTWindowChrome.CreateCloseButton(rt, Hide);
    }

    private void OnWebToggleChanged(bool on)
    {
        SetWebEnabled(on);
        AddSystemMessage("Web access " + (on ? "ON" : "OFF") + " (header Web checkbox). The model sees this in CURRENT STATE on the next turn.", includeInLLMRecap: false);
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

        // Narrowing/widening the chat side reflows bubble text; re-fit cached heights.
        RequestBubbleRelayout();
    }

    /// <summary>Current user-chosen footer (input box) base height, excluding the
    /// attachments strip. Read by FooterResizeHandle as its drag baseline.</summary>
    public float CurrentFooterBaseHeight => _footerBaseHeight;

    /// <summary>
    /// Applies the three coupled footer measurements from the current _footerBaseHeight
    /// (+ the attachments strip when present): the footer's own height, the body's bottom
    /// inset above it, and the input field's top inset (status row + strip). Shared by the
    /// attachment-strip path and the resize-drag path so they never drift apart.
    /// </summary>
    private void UpdateFooterLayout()
    {
        bool hasAttachments = _attachmentZone != null && _attachmentZone.HasAttachments;
        float extra = hasAttachments ? ATTACHMENT_STRIP_HEIGHT : 0f;
        float total = _footerBaseHeight + extra;

        if (_footerRT != null)
            _footerRT.sizeDelta = new Vector2(_footerRT.sizeDelta.x, total);
        if (_bodyRT != null)
            _bodyRT.offsetMin = new Vector2(_bodyRT.offsetMin.x, total);
        if (_inputFieldRT != null)
            _inputFieldRT.offsetMax = new Vector2(_inputFieldRT.offsetMax.x, -(32f + extra));
    }

    /// <summary>
    /// Updates _footerBaseHeight (clamped) and re-lays-out the footer/body/input. Called
    /// from FooterResizeHandle.OnDrag and from OnPanelResized (to re-clamp after the whole
    /// window shrinks). Mirror of ApplySplit on the vertical axis.
    /// </summary>
    public void ApplyFooterHeight(float newBaseHeight)
    {
        if (_footerRT == null || _bodyRT == null) return;
        // Vertical space between the header's bottom and the window's bottom edge.
        float panelHeight = _mainPanel != null ? _mainPanel.rect.height : 0f;
        float extra = (_attachmentZone != null && _attachmentZone.HasAttachments)
                      ? ATTACHMENT_STRIP_HEIGHT : 0f;
        float maxBase = Mathf.Max(FOOTER_HEIGHT, panelHeight - HEADER_HEIGHT - MIN_BODY_HEIGHT - extra);
        _footerBaseHeight = Mathf.Clamp(newBaseHeight, FOOTER_HEIGHT, maxBase);

        UpdateFooterLayout();
        // Body height changed (width didn't), but bubbles re-fit to the new viewport.
        RequestBubbleRelayout();
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

        scroll.verticalScrollbar = BuildVerticalScrollbar(hostGo);

        scrollOut = scroll;
        contentOut = contentRT;
    }

    /// <summary>
    /// Build a dark-styled vertical scrollbar pinned to the right edge of
    /// <paramref name="host"/> (14px wide, full height) with a minimum handle size, and
    /// return it. Caller wires it to a ScrollRect or a TMP_InputField. Matches the
    /// original chat scrollbar style so the chat view and the entry box look identical.
    /// </summary>
    private Scrollbar BuildVerticalScrollbar(GameObject host)
    {
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(host.transform, false);
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
        var minHandle = sbGo.AddComponent<MinScrollbarHandleSize>();
        minHandle.SetTarget(scrollbar, handleRt, MIN_SCROLLBAR_HANDLE_PIXELS);
        return scrollbar;
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
        inputRt.offsetMax = new Vector2(-FOOTER_RIGHT_RESERVED_WIDTH, -32); // leave space for buttons (right) and status text (top)
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
        _inputField.onFocusSelectAll = false;
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
        _inputUndo = TMPInputFieldUndo.Ensure(_inputField);
        _inputField.onValueChanged.AddListener(OnPromptInputValueChangedForHistory);

        var inputContextHandler = inputGo.AddComponent<AIChatBubbleContextClickHandler>();
        inputContextHandler.Setup(this, _inputField, null, isEntryInput: true);

        // Prevent Ctrl+wheel font resizing from also scrolling the multiline entry box.
        inputGo.AddComponent<ChatScrollForwarder>();

        // Vertical scrollbar on the entry box so large pastes / long messages can be
        // scrolled instead of running off the bottom. Attach it after this frame's UI
        // construction finishes; otherwise TMP can update the scrollbar while a text
        // graphic rebuild is already in progress when this panel is opened by a button.
        var inputScrollbar = BuildVerticalScrollbar(inputGo);
        inputScrollbar.gameObject.SetActive(false);
        StartCoroutine(AttachInputScrollbarNextFrame(inputScrollbar));
        // Pull the text viewport in from the right so text doesn't run under the bar.
        if (_inputField.textViewport != null)
        {
            var tv = _inputField.textViewport;
            tv.offsetMax = new Vector2(tv.offsetMax.x - 16f, tv.offsetMax.y);
        }

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

        CreateMainLLMOverrideControls(footer.transform);
        CreateSpeechControls(footer.transform);

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
            tt._text =
                "If checked, raw image bytes are fed into the main chat\n" +
                "context every turn - usually a bad idea and wasteful\n" +
                "of tokens.\n" +
                "\n" +
                "If unchecked (recommended), a separate vision call\n" +
                "'looks' at each attached image once and only its\n" +
                "description is added to the conversation.";
        }

        // Row 4: "Auto repeat msg" toggle on the left + small numeric N input on
        // the right. Checking the box (not pressing Send) drives the loop: it sends
        // whatever is currently in the input box, waits for the reply, and repeats -
        // up to N times total - then auto-unchecks itself. The input box is NOT
        // cleared between repeats, so editing it mid-run changes what the next send
        // delivers. Stop, an aborted turn, or unchecking the box stops it.
        _autoContinueToggle = CreateFooterToggle(
            footer.transform,
            "Auto repeat msg",
            new Vector2(-72, -130),
            new Vector2(122, 22),
            // Always starts OFF: the checked state is intentionally session-only
            // (never restored from PlayerPrefs) so the app never comes up primed
            // to auto-resend messages. Only the repeat COUNT below persists.
            false,
            OnAutoRepeatToggled);
        {
            var tt = _autoContinueToggle.gameObject.AddComponent<RTToolTip>();
            tt._text =
                "When checked, automatically re-sends whatever text is\n" +
                "in the input box, once per completed reply, up to N\n" +
                "times (the field on the right) - then unchecks itself.\n" +
                "\n" +
                "You don't have to press Send to start it; if a reply is\n" +
                "already streaming it kicks in once that one finishes.\n" +
                "\n" +
                "The box is never cleared while running, so editing it\n" +
                "changes what the next repeat sends. The N field\n" +
                "counts down as it goes. Uncheck (or Stop) to halt.";
        }
        _autoContinueCountInput = CreateFooterIntInput(
            footer.transform,
            new Vector2(-8, -130),
            new Vector2(60, 22),
            GetAutoContinueCount(),
            OnAutoRepeatCountEdited);

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
            maxAttachments: MAX_CHAT_ATTACHMENTS,
            stripHeight: ATTACHMENT_STRIP_HEIGHT,
            // Live-read the cap so the user can change it in settings without
            // having to reopen the chat panel; takes effect on the next drop.
            maxEdgeProvider: GetAttachmentMaxEdge,
            thumbnailContent: _attachmentsContent,
            thumbnailScroll: _attachmentsScroll,
            highPriorityDropClaim: true);
        _attachmentZone.OnAttachmentsChanged += OnAttachmentsChanged;
        _attachmentZone.OnAttachmentAdded += OnAttachmentAdded;
        _attachmentZone.OnCaptionCancelled += OnCaptionCancelled;
        _attachmentZone.OnVideoFileDropped += OnVideoFileDropped;
        _attachmentZone.OnAudioFileDropped += OnAudioFileDropped;

        CreateFooterDragBar(footer.transform);
    }

    private IEnumerator AttachInputScrollbarNextFrame(Scrollbar scrollbar)
    {
        yield return null;

        if (_inputField == null || scrollbar == null)
            yield break;

        scrollbar.gameObject.SetActive(true);
        _inputField.verticalScrollbar = scrollbar;
        _inputField.ForceLabelUpdate();
    }

    private void CreateMainLLMOverrideControls(Transform parent)
    {
        var labelObj = new GameObject("MainLLMLabel");
        labelObj.transform.SetParent(parent, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 1);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 1);
        labelRt.anchoredPosition = new Vector2(8, -6);
        labelRt.sizeDelta = new Vector2(66, 22);

        _mainLLMLabelText = labelObj.AddComponent<TextMeshProUGUI>();
        _mainLLMLabelText.text = "Main LLM:";
        _mainLLMLabelText.font = _font;
        _mainLLMLabelText.fontSize = 12;
        _mainLLMLabelText.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        _mainLLMLabelText.alignment = TextAlignmentOptions.MidlineLeft;
        _mainLLMLabelText.raycastTarget = false;

        var ddGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        ddGo.name = "MainLLMDropdown";
        ddGo.transform.SetParent(parent, false);
        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 1);
        ddRt.anchorMax = new Vector2(0, 1);
        ddRt.pivot = new Vector2(0, 1);
        ddRt.anchoredPosition = new Vector2(76, -6);
        ddRt.sizeDelta = new Vector2(190, 22);

        var ddImg = ddGo.GetComponent<Image>();
        if (ddImg != null)
        {
            ddImg.sprite = null;
            ddImg.type = Image.Type.Simple;
            ddImg.color = Color.white;
        }

        _mainLLMDropdown = ddGo.GetComponent<TMP_Dropdown>();
        if (_mainLLMDropdown != null)
        {
            _mainLLMDropdown.onValueChanged.AddListener(OnMainLLMDropdownChanged);
            if (_mainLLMDropdown.captionText != null)
            {
                _mainLLMDropdown.captionText.font = _font;
                _mainLLMDropdown.captionText.fontSize = 12;
                // Unity/TMP sometimes generates a caption child that is visually blank
                // in this runtime-created dropdown. We draw our own caption overlay below.
                _mainLLMDropdown.captionText.color = new Color(0f, 0f, 0f, 0f);
                _mainLLMDropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (_mainLLMDropdown.itemText != null)
            {
                _mainLLMDropdown.itemText.font = _font;
                _mainLLMDropdown.itemText.fontSize = 12;
                _mainLLMDropdown.itemText.color = TextDark;
                _mainLLMDropdown.itemText.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        var captionObj = new GameObject("VisibleCaption");
        captionObj.transform.SetParent(ddGo.transform, false);
        var captionRt = captionObj.AddComponent<RectTransform>();
        captionRt.anchorMin = Vector2.zero;
        captionRt.anchorMax = Vector2.one;
        captionRt.offsetMin = new Vector2(8, 0);
        captionRt.offsetMax = new Vector2(-24, 0);
        _mainLLMCaptionText = captionObj.AddComponent<TextMeshProUGUI>();
        _mainLLMCaptionText.text = "Default";
        _mainLLMCaptionText.font = _font;
        _mainLLMCaptionText.fontSize = 12;
        _mainLLMCaptionText.color = TextDark;
        _mainLLMCaptionText.alignment = TextAlignmentOptions.MidlineLeft;
        _mainLLMCaptionText.overflowMode = TextOverflowModes.Ellipsis;
        _mainLLMCaptionText.raycastTarget = false;

        var arrowObj = new GameObject("VisibleArrow");
        arrowObj.transform.SetParent(ddGo.transform, false);
        var arrowRt = arrowObj.AddComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1, 0);
        arrowRt.anchorMax = new Vector2(1, 1);
        arrowRt.pivot = new Vector2(1, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-4, 0);
        arrowRt.sizeDelta = new Vector2(18, 0);
        _mainLLMArrowText = arrowObj.AddComponent<TextMeshProUGUI>();
        _mainLLMArrowText.text = "v";
        _mainLLMArrowText.font = _font;
        _mainLLMArrowText.fontSize = 12;
        _mainLLMArrowText.fontStyle = FontStyles.Bold;
        _mainLLMArrowText.color = TextDark;
        _mainLLMArrowText.alignment = TextAlignmentOptions.Center;
        _mainLLMArrowText.raycastTarget = false;

        var tt = ddGo.AddComponent<RTToolTip>();
        tt._text =
            "Default uses normal Big/Small/Vision routing.\n" +
            "Selecting an LLM forces main chat replies to that instance.";
    }

    private void CreateSpeechControls(Transform parent)
    {
        var labelObj = new GameObject("SpeechLabel");
        labelObj.transform.SetParent(parent, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 1);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 1);
        labelRt.anchoredPosition = new Vector2(280, -6);
        labelRt.sizeDelta = new Vector2(48, 22);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = "Speech:";
        label.font = _font;
        label.fontSize = 12;
        label.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;

        var statusObj = new GameObject("SpeechStatus");
        statusObj.transform.SetParent(parent, false);
        var statusRt = statusObj.AddComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0, 1);
        statusRt.anchorMax = new Vector2(0, 1);
        statusRt.pivot = new Vector2(0, 1);
        statusRt.anchoredPosition = new Vector2(330, -6);
        statusRt.sizeDelta = new Vector2(150, 22);

        _speechStatusText = statusObj.AddComponent<TextMeshProUGUI>();
        _speechStatusText.text = "Idle";
        _speechStatusText.font = _font;
        _speechStatusText.fontSize = 12;
        _speechStatusText.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        _speechStatusText.alignment = TextAlignmentOptions.MidlineLeft;
        _speechStatusText.overflowMode = TextOverflowModes.Ellipsis;
        _speechStatusText.raycastTarget = false;

        _speechStopButton = CreateFooterLeftButton(parent, "Stop", new Vector2(486, -6), new Vector2(48, 22), OnSpeechStopClicked);
        _speechStopButton.interactable = false;
        _speechStopButton.gameObject.SetActive(false);
    }

    private Button CreateFooterLeftButton(Transform parent, string text, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var btn = new GameObject("Btn_" + text);
        btn.transform.SetParent(parent, false);
        var rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
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
        tmp.fontSize = 12;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = TextTitle;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return button;
    }

    private void CreateFooterDragBar(Transform footerTransform)
    {
        // The bar across the top of the footer is the draggable divider between the input
        // box and the columns above: drag it up to grow the input box, down to shrink it.
        var bar = new GameObject("FooterResizeBar");
        bar.transform.SetParent(footerTransform, false);
        var rt = bar.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, FOOTER_DRAG_BAR_HEIGHT);
        rt.anchoredPosition = Vector2.zero;

        var img = bar.AddComponent<Image>();
        img.color = new Color(0.62f, 0.62f, 0.66f, 1f);

        bar.AddComponent<FooterResizeHandle>().SetTarget(this, _mainPanel, _footerBaseHeight);
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
        // Pin to the top center of the footer, between the Main LLM controls and
        // the right-side status/buttons. Height grows with content (set in Refresh).
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(MAIN_LLM_FOOTER_RESERVED_WIDTH, 0f);
        rt.offsetMax = new Vector2(-FOOTER_RIGHT_RESERVED_WIDTH, 0f);

        var scroll = strip.AddComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.scrollSensitivity = 34f;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(strip.transform, false);
        var viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0f);
        viewportImg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(0, 1);
        contentRT.pivot = new Vector2(0, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, ATTACHMENT_STRIP_HEIGHT);

        var hlg = content.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        scroll.viewport = viewportRT;
        scroll.content = contentRT;

        _attachmentsStrip = rt;
        _attachmentsContent = contentRT;
        _attachmentsScroll = scroll;
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
        _attachmentCaptionQueue.Add(new AttachmentCaptionRequest
        {
            id = info.id,
            png = info.bytes
        });
        if (_attachmentCaptionStartTime <= 0f)
        {
            _attachmentCaptionStartTime = Time.unscaledTime;
            _attachmentCaptionStatusNextRefresh = 0f;
            _attachmentCaptionSpinnerStep = 0;
        }

        RecomputeSendInteractable();
        ProcessAttachmentCaptionQueue();
        UpdateAttachmentCaptionStatus(force: true);
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
        RemoveQueuedAttachmentCaption(attachmentId);

        if (_captionJobs.TryGetValue(attachmentId, out var job))
        {
            _captionJobs.Remove(attachmentId);
            CancelCaptionJob(job);
        }

        RecomputeSendInteractable();
        ProcessAttachmentCaptionQueue();
        UpdateAttachmentCaptionStatus(force: true);
    }

    private void CancelCaptionJob(CaptionJob job)
    {
        if (job == null || job.completed) return;  // race: onDone or watchdog already finished it
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

    private void CancelAllAttachmentCaptions()
    {
        _attachmentCaptionQueue.Clear();

        if (_captionJobs.Count > 0)
        {
            var jobs = new List<CaptionJob>(_captionJobs.Values);
            _captionJobs.Clear();
            foreach (var job in jobs)
                CancelCaptionJob(job);
        }

        _attachmentCaptionStartTime = 0f;
        _attachmentCaptionNextDispatch = 0f;
        _attachmentCaptionStatusNextRefresh = 0f;
        RecomputeSendInteractable();
    }

    private void RemoveQueuedAttachmentCaption(int attachmentId)
    {
        for (int i = _attachmentCaptionQueue.Count - 1; i >= 0; i--)
        {
            if (_attachmentCaptionQueue[i] != null && _attachmentCaptionQueue[i].id == attachmentId)
                _attachmentCaptionQueue.RemoveAt(i);
        }
    }

    private int CountPendingAttachmentCaptions()
    {
        return _attachmentZone != null ? _attachmentZone.CountInFlightCaptions() : 0;
    }

    private void ProcessAttachmentCaptionQueue()
    {
        if (_attachmentZone == null || _attachmentCaptionQueue.Count == 0)
            return;

        var instanceMgr = LLMInstanceManager.Get();
        while (_attachmentCaptionQueue.Count > 0)
        {
            var req = _attachmentCaptionQueue[0];
            if (req == null || !_attachmentZone.HasAttachment(req.id))
            {
                _attachmentCaptionQueue.RemoveAt(0);
                continue;
            }

            if (instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
            {
                int freeId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: true, out _);
                if (freeId < 0 && instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0)
                    break; // A vision route exists; wait for capacity instead of over-subscribing.
            }

            _attachmentCaptionQueue.RemoveAt(0);
            int id = req.id;
            var job = TryCaptionBytes(req.png, result =>
            {
                _captionJobs.Remove(id);
                if (_attachmentZone != null)
                    _attachmentZone.SetCaption(id, result.shortCaption, result.longCaption);
                RecomputeSendInteractable();
                ProcessAttachmentCaptionQueue();
                UpdateAttachmentCaptionStatus(force: true);
            }, requireFreeSlot: true);

            if (job == null)
            {
                // Capacity disappeared between the preflight check and dispatch.
                _attachmentCaptionQueue.Insert(0, req);
                break;
            }

            if (!job.completed)
            {
                _captionJobs[id] = job;
                _attachmentZone.SetCaptionState(id, ChatImageAttachmentZone.CaptionState.Captioning);
            }
        }

        RecomputeSendInteractable();
    }

    private int CountPendingInspectImageJobs()
    {
        int n = _inspectImageQueue.Count;
        if (_inspectImageJob != null && !_inspectImageJob.completed) n++;
        return n;
    }

    private int CountPendingVideoCaptions()
    {
        return _videoCaptionInFlight.Count;
    }

    private bool HasPendingSidecarWork()
    {
        return CountPendingAttachmentCaptions() > 0
            || CountPendingInspectImageJobs() > 0
            || _videoImportCount > 0
            || CountPendingVideoCaptions() > 0
            || HasPendingWebWork();
    }

    private void EnqueueInspectImage(byte[] png, string prompt, string sourceLabel, int? llmInstanceId, bool resumeOnResult)
    {
        int resumeTurnEpoch = _chatTurnEpoch;
        if (resumeOnResult)
            RegisterInspectAutoResumeRequest(resumeTurnEpoch);

        if (png == null || png.Length == 0)
        {
            AddSystemMessage("inspect_image could not read image bytes.");
            TryScheduleInspectAutoResume();
            return;
        }

        png = PrepareInspectImagePngForVision(png, out bool alphaVisualized);
        string promptToSend = string.IsNullOrWhiteSpace(prompt)
            ? "QA inspect this image. Start with PASS or FAIL. Check visible layout/text defects, mismatches, artifacts, and unreadable text."
            : prompt.Trim();
        if (alphaVisualized)
            promptToSend = InspectAlphaVisualizationNote + "\n\n" + promptToSend;

        _inspectImageQueue.Add(new InspectImageRequest
        {
            id = _nextInspectImageRequestId++,
            png = png,
            prompt = promptToSend,
            sourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "the image" : sourceLabel.Trim(),
            alphaVisualized = alphaVisualized,
            llmInstanceId = llmInstanceId,
            resumeOnResult = resumeOnResult,
            resumeTurnEpoch = resumeTurnEpoch
        });

        RecomputeSendInteractable();
        ProcessInspectImageQueue();
        UpdateInspectImageStatus(force: true);
    }

    private static byte[] PrepareInspectImagePngForVision(byte[] png, out bool alphaVisualized)
    {
        alphaVisualized = false;
        if (png == null || png.Length == 0)
            return png;

        Texture2D src = null;
        Texture2D visual = null;
        try
        {
            src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(png, false) || !src.HasAlphaData())
                return png;

            int w = src.width;
            int h = src.height;
            if (w <= 0 || h <= 0)
                return png;

            visual = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] srcPixels = src.GetPixels();
            Color[] dstPixels = new Color[srcPixels.Length];
            int checkSize = Mathf.Clamp(Mathf.Max(w, h) / 32, 8, 32);
            Color light = new Color(0.78f, 0.78f, 0.78f, 1f);
            Color dark = new Color(0.48f, 0.48f, 0.48f, 1f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color fg = srcPixels[i];
                    float a = Mathf.Clamp01(fg.a);
                    Color bg = (((x / checkSize) + (y / checkSize)) & 1) == 0 ? light : dark;
                    dstPixels[i] = new Color(
                        fg.r * a + bg.r * (1f - a),
                        fg.g * a + bg.g * (1f - a),
                        fg.b * a + bg.b * (1f - a),
                        1f);
                }
            }

            visual.SetPixels(dstPixels);
            visual.Apply();
            byte[] visualPng = visual.EncodeToPNG();
            if (visualPng != null && visualPng.Length > 0)
            {
                alphaVisualized = true;
                return visualPng;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AIChatPanel.PrepareInspectImagePngForVision: " + ex.Message);
        }
        finally
        {
            if (src != null) UnityEngine.Object.Destroy(src);
            if (visual != null) UnityEngine.Object.Destroy(visual);
        }

        return png;
    }

    private void ProcessInspectImageQueue()
    {
        if (_inspectImageJob != null && !_inspectImageJob.completed)
            return;

        while (_inspectImageQueue.Count > 0)
        {
            var req = _inspectImageQueue[0];
            if (req == null || req.png == null || req.png.Length == 0)
            {
                _inspectImageQueue.RemoveAt(0);
                AddSystemMessage("inspect_image could not read image bytes.");
                continue;
            }

            if (!TrySelectInspectImageLLM(req, out var instanceMgr, out var inst, out int targetId, out int replicaIndex, out bool waitingForCapacity))
            {
                if (waitingForCapacity)
                    break;
                _inspectImageQueue.RemoveAt(0);
                continue;
            }

            _inspectImageQueue.RemoveAt(0);
            DispatchInspectImageJob(req, instanceMgr, inst, targetId, replicaIndex);
            break;
        }

        RecomputeSendInteractable();
        TryScheduleInspectAutoResume();
    }

    private bool TrySelectInspectImageLLM(
        InspectImageRequest req,
        out LLMInstanceManager instanceMgr,
        out LLMInstanceInfo inst,
        out int targetId,
        out int replicaIndex,
        out bool waitingForCapacity)
    {
        instanceMgr = LLMInstanceManager.Get();
        inst = null;
        targetId = -1;
        replicaIndex = 0;
        waitingForCapacity = false;

        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
        {
            AddSystemMessage("inspect_image: no LLM instances are configured.");
            return false;
        }

        if (req != null && req.llmInstanceId.HasValue)
        {
            var hinted = instanceMgr.GetInstance(req.llmInstanceId.Value);
            if (hinted != null && hinted.CanAcceptJobType(false, isVisionJob: true))
            {
                if (!SkillActionExecutor.IsDispatchOneShotSupported(hinted.providerType))
                {
                    AddSystemMessage($"inspect_image: provider {hinted.providerType} is not supported by one-shot vision dispatch.");
                    return false;
                }
                if (TryFindFreeReplica(hinted, out replicaIndex))
                {
                    inst = hinted;
                    targetId = hinted.instanceID;
                    return true;
                }
                waitingForCapacity = true;
                return false;
            }
        }

        targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: true, out replicaIndex);
        if (targetId < 0)
        {
            if (instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0)
            {
                waitingForCapacity = true;
                return false;
            }

            AddSystemMessage(
                "inspect_image: no active vision-capable LLM is available. In LLM Settings, enable Supports vision on a vision model.");
            return false;
        }

        inst = instanceMgr.GetInstance(targetId);
        if (inst == null || inst.settings == null)
        {
            AddSystemMessage("inspect_image: picked LLM instance has no settings.");
            return false;
        }
        if (!SkillActionExecutor.IsDispatchOneShotSupported(inst.providerType))
        {
            AddSystemMessage($"inspect_image: provider {inst.providerType} is not supported by one-shot vision dispatch.");
            return false;
        }

        return true;
    }

    private static bool TryFindFreeReplica(LLMInstanceInfo inst, out int replicaIndex)
    {
        replicaIndex = 0;
        if (inst == null || inst.maxConcurrentTasks <= 0) return false;
        inst.EnsureReplicaActiveTasks();
        int repCount = inst.GetEffectiveReplicaCount();
        for (int i = 0; i < repCount; i++)
        {
            if (inst.replicaActiveTasks[i] < inst.maxConcurrentTasks)
            {
                replicaIndex = i;
                return true;
            }
        }
        return false;
    }

    // Used when a job MUST run on a specific instance (e.g. the compact summary
    // honoring the Main LLM override) and no replica is free: queue on the one
    // with the fewest active tasks instead of failing or switching instances.
    private static int FindLeastLoadedReplica(LLMInstanceInfo inst)
    {
        if (inst == null) return 0;
        inst.EnsureReplicaActiveTasks();
        int repCount = inst.GetEffectiveReplicaCount();
        int best = 0, bestTasks = int.MaxValue;
        for (int i = 0; i < repCount; i++)
        {
            if (inst.replicaActiveTasks[i] < bestTasks)
            {
                bestTasks = inst.replicaActiveTasks[i];
                best = i;
            }
        }
        return best;
    }

    private static string FormatApproxTokenCount(int tokens)
    {
        if (tokens >= 100000) return $"{tokens / 1000f:F0}k";
        if (tokens >= 1000) return $"{tokens / 1000f:F1}k";
        return tokens.ToString();
    }

    /// <summary>
    /// Cut a user line's LLM payload at the first injected skill-body marker for the
    /// compact-summary transcript. Bodies are folded at the tail of the recap section
    /// (QueueTriggeredSkillBodyInjections queues them right before the fold), so the
    /// user's real text and earlier recap notes survive; in the rare case another
    /// note landed after a read_skill body it is dropped too - acceptable for a
    /// summarizer input. A stub line replaces the cut so the recap bullet list does
    /// not end dangling.
    /// </summary>
    private static string StripInjectedSkillBodiesForTranscript(string userContent)
    {
        if (string.IsNullOrEmpty(userContent)) return userContent;
        int cut = userContent.IndexOf(AutoloadSkillBodyMarkerPrefix, StringComparison.Ordinal);
        int readSkillCut = userContent.IndexOf(ReadSkillBodyMarkerPrefix, StringComparison.Ordinal);
        if (readSkillCut >= 0 && (cut < 0 || readSkillCut < cut))
            cut = readSkillCut;
        if (cut < 0) return userContent;

        string head = userContent.Substring(0, cut);
        // Drop the recap bullet prefix ("- ") the fold added in front of the marker.
        if (head.EndsWith("- ", StringComparison.Ordinal))
            head = head.Substring(0, head.Length - 2);
        return head.TrimEnd() + "\n- [auto-loaded skill reference material omitted]";
    }

    private void DispatchInspectImageJob(InspectImageRequest req, LLMInstanceManager instanceMgr, LLMInstanceInfo inst, int targetId, int replicaIndex)
    {
        if (req == null || instanceMgr == null || inst == null || inst.settings == null)
            return;

        instanceMgr.SetLLMBusy(targetId, replicaIndex, true);
        var job = new InspectImageJob
        {
            requestId = req.id,
            sourceLabel = req.sourceLabel,
            targetId = targetId,
            replicaIndex = replicaIndex,
            startTime = Time.unscaledTime,
            resumeOnResult = req.resumeOnResult,
            resumeTurnEpoch = req.resumeTurnEpoch
        };
        _inspectImageJob = job;

        AddSystemMessage(BuildInspectImagePromptDetails(req, inst, targetId), includeInLLMRecap: false);

        var lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system", InspectImageSystemPrompt));
        var userLine = new GTPChatLine("user", req.prompt);
        userLine.AddImage(Convert.ToBase64String(req.png), -1);
        lines.Enqueue(userLine);

        job.watchdog = StartCoroutine(InspectImageWatchdog(job, instanceMgr));

        SkillActionExecutor.DispatchOneShot(this, inst, lines, (db, json, text) =>
        {
            if (job.completed) return;

            string clean = (text ?? "").Trim();
            if (string.IsNullOrEmpty(clean) && json != null)
            {
                try { clean = OpenAITextCompletionManager.ExtractTextFromResponseJSON(json); } catch { /* no-op */ }
            }

            string failureDetail = GetSidecarFailureDetail(db);
            string message = !string.IsNullOrEmpty(clean)
                ? $"Vision inspection result for {req.sourceLabel}:\n{clean}"
                : (!string.IsNullOrEmpty(failureDetail)
                    ? $"inspect_image: LLM #{targetId} failed for {req.sourceLabel}: {failureDetail}"
                    : $"inspect_image: LLM #{targetId} returned no content for {req.sourceLabel}.");
            CompleteInspectImageJob(job, instanceMgr, message, includeInLLMRecap: true);
        }, "InspectImage", "inspect_image_sent.json");

        RecomputeSendInteractable();
        UpdateInspectImageStatus(force: true);
    }

    private IEnumerator InspectImageWatchdog(InspectImageJob job, LLMInstanceManager instanceMgr)
    {
        yield return new WaitForSeconds(INSPECT_IMAGE_TIMEOUT_SECONDS);
        if (job == null || job.completed) yield break;
        job.watchdog = null;
        CompleteInspectImageJob(
            job,
            instanceMgr,
            $"inspect_image timed out after {INSPECT_IMAGE_TIMEOUT_SECONDS:0}s while inspecting {job.sourceLabel}.",
            includeInLLMRecap: true);
    }

    private void CompleteInspectImageJob(InspectImageJob job, LLMInstanceManager instanceMgr, string message, bool includeInLLMRecap)
    {
        if (job == null || job.completed) return;
        job.completed = true;
        if (job.watchdog != null)
        {
            try { StopCoroutine(job.watchdog); } catch { }
            job.watchdog = null;
        }
        if (job.targetId >= 0 && instanceMgr != null)
            instanceMgr.SetLLMBusy(job.targetId, job.replicaIndex, false);
        if (_inspectImageJob == job)
            _inspectImageJob = null;

        if (!string.IsNullOrWhiteSpace(message))
            AddSystemMessage(message, includeInLLMRecap);

        RecomputeSendInteractable();
        ProcessInspectImageQueue();
        UpdateInspectImageStatus(force: true);
        TryScheduleInspectAutoResume();
        TryScheduleSkillLoadAutoResume();

        if (_autoContinueToggle != null && _autoContinueToggle.isOn
            && !_inspectAutoResumePending && !_skillLoadAutoResumePending
            && !_isStreaming && !_waitingForForcedMainLLM && _autoContinueRemaining > 0 && !HasPendingSidecarWork())
            StartCoroutine(FireAutoContinueNextFrame());
    }

    private void CancelAllInspectImageJobs(bool showBubble)
    {
        CancelInspectAutoResume();
        _inspectImageQueue.Clear();

        if (_inspectImageJob != null)
        {
            var job = _inspectImageJob;
            job.cancelled = true;
            CompleteInspectImageJob(job, LLMInstanceManager.Get(), null, includeInLLMRecap: false);
        }

        _inspectImageStatusNextRefresh = 0f;
        if (showBubble)
            AddSystemMessage("Stopped image inspection.", includeInLLMRecap: false);
        RecomputeSendInteractable();
        UpdateInspectImageStatus(force: true);
    }

    private bool HasInspectAutoResumePendingForCurrentTurn()
    {
        return _inspectAutoResumePending && _inspectAutoResumeTurnEpoch == _chatTurnEpoch;
    }

    private bool HasSkillLoadAutoResumePendingForCurrentTurn()
    {
        return _skillLoadAutoResumePending && _skillLoadAutoResumeTurnEpoch == _chatTurnEpoch;
    }

    private bool HasGenericContinuePendingForCurrentTurn()
    {
        return _genericContinuePending && _genericContinueTurnEpoch == _chatTurnEpoch;
    }

    private static string BuildInspectImagePromptDetails(InspectImageRequest req, LLMInstanceInfo inst, int targetId)
    {
        string source = req != null && !string.IsNullOrWhiteSpace(req.sourceLabel) ? req.sourceLabel : "the image";
        string provider = inst != null ? inst.providerType.ToString() : "unknown provider";
        string model = inst != null && inst.settings != null && !string.IsNullOrWhiteSpace(inst.settings.selectedModel)
            ? inst.settings.selectedModel
            : "unknown model";
        string prompt = req != null && !string.IsNullOrWhiteSpace(req.prompt) ? req.prompt : "(empty prompt)";

        var sb = new StringBuilder();
        sb.Append("Inspecting ").Append(source).Append(" with LLM #").Append(targetId)
            .Append(" (").Append(provider).Append(' ').Append(model).AppendLine(")...");
        sb.AppendLine();
        sb.AppendLine("Prompt sent to vision LLM:");
        sb.AppendLine("System:");
        sb.AppendLine(InspectImageSystemPrompt);
        sb.AppendLine();
        sb.AppendLine("User:");
        sb.AppendLine("Source: " + source);
        sb.AppendLine(req != null && req.alphaVisualized
            ? "[image bytes attached; alpha visualized over checkerboard; base64 elided]"
            : "[image bytes attached; base64 elided]");
        sb.AppendLine(prompt);
        if (req != null && req.resumeOnResult)
        {
            sb.AppendLine();
            sb.AppendLine("[auto-resume requested after inspection result]");
        }
        return sb.ToString().TrimEnd();
    }

    private void RegisterInspectAutoResumeRequest(int turnEpoch)
    {
        _inspectAutoResumePending = true;
        _inspectAutoResumeScheduled = false;
        _inspectAutoResumeTurnEpoch = turnEpoch;
    }

    private void CancelInspectAutoResume()
    {
        _inspectAutoResumePending = false;
        _inspectAutoResumeScheduled = false;
        _inspectAutoResumeTurnEpoch = -1;
    }

    private void TryScheduleInspectAutoResume()
    {
        if (!_inspectAutoResumePending || _inspectAutoResumeScheduled)
            return;
        if (_inspectAutoResumeTurnEpoch != _chatTurnEpoch)
        {
            CancelInspectAutoResume();
            return;
        }
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
            return;

        _inspectAutoResumeScheduled = true;
        StartCoroutine(FireInspectAutoResumeNextFrame(_inspectAutoResumeTurnEpoch));
    }

    private IEnumerator FireInspectAutoResumeNextFrame(int turnEpoch)
    {
        yield return null;

        if (!_inspectAutoResumePending || !_inspectAutoResumeScheduled)
            yield break;
        if (_inspectAutoResumeTurnEpoch != turnEpoch || _chatTurnEpoch != turnEpoch)
            yield break;
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
        {
            _inspectAutoResumeScheduled = false;
            TryScheduleInspectAutoResume();
            yield break;
        }

        CancelInspectAutoResume();
        SendSyntheticContinue();
    }

    private void RegisterSkillLoadAutoResumeRequest(int turnEpoch, string skillId)
    {
        if (_skillLoadAutoResumeTurnEpoch != turnEpoch)
            _skillLoadAutoResumeIds.Clear();

        _skillLoadAutoResumePending = true;
        _skillLoadAutoResumeScheduled = false;
        _skillLoadAutoResumeTurnEpoch = turnEpoch;

        if (!string.IsNullOrWhiteSpace(skillId))
        {
            bool exists = false;
            for (int i = 0; i < _skillLoadAutoResumeIds.Count; i++)
            {
                if (string.Equals(_skillLoadAutoResumeIds[i], skillId, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
                _skillLoadAutoResumeIds.Add(skillId.Trim());
        }
    }

    private void CancelSkillLoadAutoResume()
    {
        _skillLoadAutoResumePending = false;
        _skillLoadAutoResumeScheduled = false;
        _skillLoadAutoResumeTurnEpoch = -1;
        _skillLoadAutoResumeIds.Clear();
    }

    private void TryScheduleSkillLoadAutoResume()
    {
        if (!_skillLoadAutoResumePending || _skillLoadAutoResumeScheduled)
            return;
        if (_skillLoadAutoResumeTurnEpoch != _chatTurnEpoch)
        {
            CancelSkillLoadAutoResume();
            return;
        }
        // Inspection results carry new information that the continued turn should see.
        // If both mechanisms are pending, let the inspection auto-resume own the single
        // synthetic continue; SendChatTurn will cancel this stale skill-load request.
        if (HasInspectAutoResumePendingForCurrentTurn())
            return;
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
            return;

        _skillLoadAutoResumeScheduled = true;
        StartCoroutine(FireSkillLoadAutoResumeNextFrame(_skillLoadAutoResumeTurnEpoch));
    }

    private IEnumerator FireSkillLoadAutoResumeNextFrame(int turnEpoch)
    {
        yield return null;

        if (!_skillLoadAutoResumePending || !_skillLoadAutoResumeScheduled)
            yield break;
        if (_skillLoadAutoResumeTurnEpoch != turnEpoch || _chatTurnEpoch != turnEpoch)
            yield break;
        if (HasInspectAutoResumePendingForCurrentTurn())
        {
            _skillLoadAutoResumeScheduled = false;
            yield break;
        }
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
        {
            _skillLoadAutoResumeScheduled = false;
            TryScheduleSkillLoadAutoResume();
            yield break;
        }

        CancelSkillLoadAutoResume();
        SendSyntheticContinue();
    }

    // ----- Model-requested continue (the `continue` control action) -----

    private void RegisterGenericContinueRequest(int turnEpoch)
    {
        // Runaway guard: a model that emits `continue` every turn would loop forever.
        // Cap consecutive self-requested continues; the counter resets on a real user
        // send (see SendChatTurn). Once capped, ignore the request and tell the user.
        if (_consecutiveSelfContinues >= MaxConsecutiveSelfContinues)
        {
            CancelGenericContinue();
            AddSystemMessage(
                $"(Reached the limit of {MaxConsecutiveSelfContinues} automatic continues in a row - " +
                "stopping so it doesn't loop. Type a message to keep going.)",
                includeInLLMRecap: false);
            return;
        }

        _genericContinuePending = true;
        _genericContinueScheduled = false;
        _genericContinueTurnEpoch = turnEpoch;
    }

    private void CancelGenericContinue()
    {
        _genericContinuePending = false;
        _genericContinueScheduled = false;
        _genericContinueTurnEpoch = -1;
    }

    private void TryScheduleGenericContinue()
    {
        if (!_genericContinuePending || _genericContinueScheduled)
            return;
        if (_genericContinueTurnEpoch != _chatTurnEpoch)
        {
            CancelGenericContinue();
            return;
        }
        // Inspection / skill-load results carry information the continued turn should
        // see; if either is pending, let it own the single synthetic continue and drop
        // this one (SendChatTurn will have bumped the epoch anyway).
        if (HasInspectAutoResumePendingForCurrentTurn() || HasSkillLoadAutoResumePendingForCurrentTurn())
            return;
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
            return;

        _genericContinueScheduled = true;
        StartCoroutine(FireGenericContinueNextFrame(_genericContinueTurnEpoch));
    }

    private IEnumerator FireGenericContinueNextFrame(int turnEpoch)
    {
        yield return null;

        if (!_genericContinuePending || !_genericContinueScheduled)
            yield break;
        if (_genericContinueTurnEpoch != turnEpoch || _chatTurnEpoch != turnEpoch)
            yield break;
        if (HasInspectAutoResumePendingForCurrentTurn() || HasSkillLoadAutoResumePendingForCurrentTurn())
        {
            _genericContinueScheduled = false;
            yield break;
        }
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
        {
            _genericContinueScheduled = false;
            TryScheduleGenericContinue();
            yield break;
        }

        CancelGenericContinue();
        _consecutiveSelfContinues++;
        SendSyntheticContinue();
    }

    /// <summary>
    /// Recompute Send button interactability from both the streaming flag AND
    /// the count of in-flight attachment captions. Call this whenever either
    /// signal can change (SetBusyUI, OnAttachmentsChanged, OnAttachmentAdded).
    /// </summary>
    private void RecomputeSendInteractable()
    {
        if (_sendButton == null) return;
        bool sidecarPending = HasPendingSidecarWork();
        _sendButton.interactable = !_isStreaming && !_waitingForForcedMainLLM && !sidecarPending;
        if (_stopButton != null)
            _stopButton.interactable = _isStreaming || _waitingForForcedMainLLM || CountPendingInspectImageJobs() > 0 || HasSkillLoadAutoResumePendingForCurrentTurn() || HasGenericContinuePendingForCurrentTurn() || HasPendingWebWork();
    }

    private void UpdateAttachmentCaptionStatus(bool force = false)
    {
        if (_statusText == null || _isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || CountPendingInspectImageJobs() > 0 || _videoImportCount > 0 || CountPendingVideoCaptions() > 0 || HasPendingWebWork())
            return;

        int pending = CountPendingAttachmentCaptions();
        if (pending > 0)
        {
            if (_attachmentCaptionStartTime <= 0f)
                _attachmentCaptionStartTime = Time.unscaledTime;
            if (!force && Time.unscaledTime < _attachmentCaptionStatusNextRefresh)
                return;

            _attachmentCaptionStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _attachmentCaptionSpinnerStep = (_attachmentCaptionSpinnerStep + 1) % StreamSpinnerFrames.Length;

            int total = Mathf.Max(pending, _attachmentZone != null ? _attachmentZone.Count : pending);
            int done = Mathf.Max(0, total - pending);
            float elapsed = Time.unscaledTime - _attachmentCaptionStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_attachmentCaptionSpinnerStep]} Captioning {done}/{total}   {elapsed:F0}s";
            return;
        }

        if (_attachmentCaptionStartTime > 0f)
        {
            _attachmentCaptionStartTime = 0f;
            _attachmentCaptionStatusNextRefresh = 0f;
            _statusText.text = _attachmentZone != null && _attachmentZone.HasAttachments ? "Images ready" : "Idle";
        }
    }

    private void UpdateVideoImportStatus(bool force = false)
    {
        if (_statusText == null || _isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || CountPendingInspectImageJobs() > 0 || _webFetchCount > 0)
            return;

        if (_videoImportCount > 0)
        {
            if (_videoImportStartTime <= 0f)
                _videoImportStartTime = Time.unscaledTime;
            if (!force && Time.unscaledTime < _videoImportStatusNextRefresh)
                return;

            _videoImportStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _videoImportSpinnerStep = (_videoImportSpinnerStep + 1) % StreamSpinnerFrames.Length;
            float elapsed = Time.unscaledTime - _videoImportStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_videoImportSpinnerStep]} {_videoImportStatusLabel}   {elapsed:F0}s";
            return;
        }

        int pendingVideoCaptions = CountPendingVideoCaptions();
        if (pendingVideoCaptions > 0)
        {
            if (_videoCaptionStartTime <= 0f)
                _videoCaptionStartTime = Time.unscaledTime;
            if (!force && Time.unscaledTime < _videoCaptionStatusNextRefresh)
                return;

            _videoCaptionStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _videoCaptionSpinnerStep = (_videoCaptionSpinnerStep + 1) % StreamSpinnerFrames.Length;
            float elapsed = Time.unscaledTime - _videoCaptionStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_videoCaptionSpinnerStep]} Captioning video   {elapsed:F0}s";
            return;
        }

        if (_videoImportStartTime > 0f)
        {
            _videoImportStartTime = 0f;
            _videoImportStatusNextRefresh = 0f;
            _statusText.text = "Video ready";
        }

        if (_videoCaptionStartTime > 0f)
        {
            _videoCaptionStartTime = 0f;
            _videoCaptionStatusNextRefresh = 0f;
            _statusText.text = "Video ready";
        }
    }

    private void UpdateInspectImageStatus(bool force = false)
    {
        if (_statusText == null || _isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight)
            return;

        int pending = CountPendingInspectImageJobs();
        if (pending > 0)
        {
            if (!force && Time.unscaledTime < _inspectImageStatusNextRefresh)
                return;

            _inspectImageStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _inspectImageSpinnerStep = (_inspectImageSpinnerStep + 1) % StreamSpinnerFrames.Length;
            string label = _inspectImageJob != null
                ? _inspectImageJob.sourceLabel
                : (_inspectImageQueue.Count > 0 ? _inspectImageQueue[0].sourceLabel : "image");
            label = CompactInspectSourceLabel(label);

            if (_inspectImageJob != null)
            {
                float elapsed = Time.unscaledTime - _inspectImageJob.startTime;
                _statusText.text = $"{StreamSpinnerFrames[_inspectImageSpinnerStep]} Inspecting {label}   {elapsed:F0}s";
            }
            else
            {
                _statusText.text = $"{StreamSpinnerFrames[_inspectImageSpinnerStep]} Inspect queued {pending}";
            }
            return;
        }

        if (_inspectImageStatusNextRefresh > 0f)
        {
            _inspectImageStatusNextRefresh = 0f;
            _statusText.text = "Inspection done";
        }
    }

    private static string CompactInspectSourceLabel(string label)
    {
        label = string.IsNullOrWhiteSpace(label) ? "image" : label.Trim();
        return label.Length <= 24 ? label : label.Substring(0, 21) + "...";
    }

    /// <summary>
    /// Fired by ChatImageAttachmentZone whenever the attachment count changes. We only
    /// need to grow / shrink the footer (and matching chat scroll area) so the typing
    /// field keeps its full height when the strip appears.
    /// </summary>
    private void OnAttachmentsChanged()
    {
        // Caption may have just arrived (or an attachment was removed); refresh Send.
        RecomputeSendInteractable();
        UpdateAttachmentCaptionStatus(force: true);

        // Re-apply the footer/body/input measurements; the strip's presence is read inside
        // UpdateFooterLayout, so it adds/removes its height on top of the resizable base.
        UpdateFooterLayout();
    }

    private void CreateResizeGrip()
    {
        // Keep resize hit zones out of the header controls. The close/settings buttons
        // must win raycasts over resize bars.
        CreateResizeEdgeHandle(
            "ResizeTop",
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, RESIZE_EDGE_THICKNESS),
            Vector2.zero,
            new Vector2(0f, 1f),
            new Vector2(0f, -RESIZE_EDGE_THICKNESS),
            new Vector2(-HEADER_RIGHT_RESIZE_EXCLUSION, 0f));
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
            new Vector2(1f, 0f),
            new Vector2(-RESIZE_EDGE_THICKNESS, 0f),
            new Vector2(0f, -HEADER_HEIGHT));

        // Invisible diagonal-resize caps for corners that don't overlap header controls.
        CreateResizeCornerCap("ResizeCornerTopLeft",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-1f,  1f));
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

        RTWindowChrome.ConfigureResizeGrip(rt);

        var resize = grip.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), new Vector2(1f, -1f), OnPanelResized);
    }

    private void CreateResizeEdgeHandle(
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        Vector2 resizeDirection,
        Vector2? offsetMin = null,
        Vector2? offsetMax = null)
    {
        var edge = new GameObject(name);
        edge.transform.SetParent(_mainPanel, false);
        var rt = edge.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPosition;
        if (offsetMin.HasValue)
            rt.offsetMin = offsetMin.Value;
        if (offsetMax.HasValue)
            rt.offsetMax = offsetMax.Value;

        var img = edge.AddComponent<Image>();
        img.color = ResizeEdgeColor;

        var resize = edge.AddComponent<PanelResizeHandle>();
        resize.SetTarget(_mainPanel, new Vector2(MIN_WIDTH, MIN_HEIGHT), resizeDirection, OnPanelResized);
    }

    /// <summary>
    /// Invisible cap parked at a panel corner. Acts as a diagonal-resize hot zone -
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
        // Footer height is absolute; re-clamp so a shrinking window can't squeeze the
        // columns above below MIN_BODY_HEIGHT.
        ApplyFooterHeight(_footerBaseHeight);
        RequestBubbleRelayout();
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
        input.onFocusSelectAll = false;
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

        // Forward regular mouse-wheel events from the bubble's TMP_InputField up to
        // the chat ScrollRect. Ctrl+wheel is reserved for font resizing.
        var bubbleScrollForwarder = inputGo.AddComponent<ChatScrollForwarder>();
        bubbleScrollForwarder.target = _chatScroll;

        var contextHandler = inputGo.AddComponent<AIChatBubbleContextClickHandler>();
        contextHandler.Setup(this, input, linkedInteraction);

        // Body only - the role label is its own TMP_Text above this field.
        input.text = ConvertMarkdownToTMP(rawMessageText);
        TMPInputFieldUndo.ResetHistory(input);

        if (linkedInteraction != null)
            HookEditingTo(input, linkedInteraction);

        // Re-measure on every text change (covers streaming, user typing, and re-format).
        input.onValueChanged.AddListener(_ => StartCoroutine(ResizeBubbleDeferred(input, inputLE)));
        StartCoroutine(ResizeBubbleDeferred(input, inputLE));
        if (shouldAutoScroll)
            StartCoroutine(ScrollToBottomDeferred());
        return input;
    }

    // ---------- "[skill: X]" click-to-expand: what was sent to the tool ----------

    /// <summary>
    /// Toggle the details of the <paramref name="linkIndex"/>-th tool-call marker in an
    /// assistant bubble. The marker ordinal maps onto the reply's actions that show a
    /// marker (media actions leave none; see SkillActionParser.ShowsTranscriptMarker),
    /// re-parsed from the stored RAW reply so the full prompt / lyrics / voice / scene
    /// text is available without keeping it in the (editable) bubble text.
    /// </summary>
    private void ToggleActionDetails(TMP_InputField field, GTPChatLine interaction, int linkIndex)
    {
        if (field == null || linkIndex < 0) return;
        string raw = interaction != null ? interaction._content : null;
        if (string.IsNullOrEmpty(raw) && ReferenceEquals(field, _streamingAssistantField))
            raw = _streamBuffer.ToString();   // still streaming: the reply is not in history yet
        if (string.IsNullOrEmpty(raw)) return;

        var bubble = field.transform.parent;
        if (bubble == null) return;
        var panel = bubble.GetComponent<ActionDetailsPanel>();
        if (panel == null) panel = bubble.gameObject.AddComponent<ActionDetailsPanel>();
        bool wasAtBottom = IsScrollAtBottom(_chatScroll);
        if (!panel.Expanded.Remove(linkIndex))
            panel.Expanded.Add(linkIndex);
        RenderActionDetails(panel, bubble, raw);
        // The newest reply sits at the bottom; keep the freshly expanded details in view.
        if (wasAtBottom)
            StartCoroutine(ScrollToBottomDeferred());
    }

    private void RenderActionDetails(ActionDetailsPanel panel, Transform bubble, string raw)
    {
        var markerActions = new List<SkillAction>();
        foreach (var a in SkillActionParser.ExtractActions(raw))
        {
            if (SkillActionParser.ShowsTranscriptMarker(a.SkillId))
                markerActions.Add(a);
        }

        var sb = new StringBuilder();
        var ordered = new List<int>(panel.Expanded);
        ordered.Sort();
        foreach (int idx in ordered)
        {
            if (idx >= markerActions.Count) continue;
            var a = markerActions[idx];
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("<b>").Append(EscapePlainTextForTMP(a.SkillId)).Append("</b> (sent to the tool; click the marker again to hide)");
            foreach (var kv in a.Args)
            {
                if (string.Equals(kv.Key, "skill", StringComparison.OrdinalIgnoreCase)) continue;
                string v = (kv.Value ?? "").Replace("\\n", "\n").Trim();
                sb.Append('\n').Append("<b>").Append(EscapePlainTextForTMP(kv.Key)).Append(":</b> ");
                if (v.IndexOf('\n') >= 0) sb.Append('\n');
                sb.Append(EscapePlainTextForTMP(v));
            }
        }

        if (sb.Length == 0)
        {
            if (panel.Text != null) panel.Text.gameObject.SetActive(false);
            return;
        }

        if (panel.Text == null)
        {
            var go = new GameObject("ActionDetails");
            go.transform.SetParent(bubble, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 14f;
            le.preferredHeight = -1f;   // natural TMP height
            le.flexibleHeight = -1f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = _font;
            tmp.fontSize = Mathf.Max(9f, (BaseFontSize - 2f) * _fontSizeMultiplier);
            tmp.color = new Color(0.28f, 0.30f, 0.36f);
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            tmp.parseCtrlCharacters = false;   // Windows paths / "\t" in prompts stay literal
            panel.Text = tmp;
        }
        panel.Text.gameObject.SetActive(true);
        panel.Text.transform.SetAsLastSibling();
        panel.Text.text = sb.ToString();
        LayoutRebuilder.MarkLayoutForRebuild(bubble as RectTransform);
    }

    private void AddWelcomeMessage()
    {
        AppendBubble(
            "AI Chat",
            new Color(0.22f, 0.22f, 0.28f, 1f),
            "Assuming you've setup an LLM and at least one ComfyUI server, you can ask to generate images, videos, stories, comics, and learn Japanese with a patient teacher.  Use Ctrl+Mouse Wheel to adjust font size.\n\nFor examples and info, type **help**",
            new Color(0.92f, 0.94f, 0.98f, 1f));
    }

    private TMP_InputField AppendInfoBubble(string text)
    {
        return AppendBubble("Info", new Color(0.35f, 0.35f, 0.45f), text, new Color(0.92f, 0.92f, 0.95f, 1f));
    }

    // Always-visible error bubble (NOT gated by "Show debug stuff"). Real backend/LLM
    // failures must reach the user even with debug off - unlike Info/system bubbles, which
    // are diagnostic and stay hidden by default. Read-only (linkedInteraction = null).
    private TMP_InputField AddErrorBubble(string text)
    {
        return AppendBubble("Error", new Color(0.75f, 0.15f, 0.15f), text, new Color(0.99f, 0.92f, 0.92f, 1f));
    }

    // Build the most useful human-readable error from a failed LLM callback's db.
    // Providers differ: OpenAI already extracts a clean "msg"; Anthropic/Gemini put the
    // transport code in "msg" and the real provider message in the JSON "response_body"
    // (error.message). Prefer the provider message, then msg, then a trimmed raw body.
    // Returns "" when nothing useful is available (caller picks the generic fallback).
    private static string BuildLLMErrorDetail(RTDB db)
    {
        if (db == null) return "";
        string msg = db.GetStringWithDefault("msg", "");
        string body = db.GetStringWithDefault("response_body", "");

        string providerMsg = "";
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var root = JSON.Parse(body);
                var errNode = root != null ? root["error"] : null;
                if (errNode != null)
                {
                    if (errNode["message"] != null && !string.IsNullOrEmpty(errNode["message"].Value))
                        providerMsg = errNode["message"].Value;       // Anthropic / OpenAI / Gemini shape
                    else if (!string.IsNullOrEmpty(errNode.Value))
                        providerMsg = errNode.Value;                  // error is a bare string
                }
            }
            catch { /* non-JSON or unexpected shape - fall back to msg/body below */ }
        }

        string detail;
        if (!string.IsNullOrEmpty(providerMsg))
        {
            // Keep the transport code if it adds info the provider message lacks (e.g. the 4xx).
            detail = (!string.IsNullOrEmpty(msg) && providerMsg.IndexOf(msg, StringComparison.OrdinalIgnoreCase) < 0)
                ? $"{providerMsg} ({msg})"
                : providerMsg;
        }
        else if (!string.IsNullOrEmpty(msg))
            detail = msg;
        else
            detail = body.Trim();

        if (string.IsNullOrEmpty(detail)) return "";
        const int maxLen = 600;
        if (detail.Length > maxLen) detail = detail.Substring(0, maxLen - 3).TrimEnd() + "...";
        return detail;
    }

    /// <summary>
    /// Make a bubble editable AFTER it has been created (used for assistant bubbles,
    /// which are created readOnly during streaming and switched to editable on completion).
    /// </summary>
    private void EnableBubbleEditing(TMP_InputField input, GTPChatLine interaction)
    {
        if (input == null || interaction == null) return;
        input.readOnly = false;
        TMPInputFieldUndo.ResetHistory(input);
        HookEditingTo(input, interaction);
        var contextHandler = input.GetComponent<AIChatBubbleContextClickHandler>();
        if (contextHandler != null)
            contextHandler.Setup(this, input, interaction);
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
            if (string.Equals(clean, interaction.GetDisplayContent(), StringComparison.Ordinal))
                return;

            interaction.SetEditedContent(clean);
        });
    }

    private int GetCurrentChatImageCount()
    {
        return _chatImagePics != null ? _chatImagePics.Count : 0;
    }

    private void MarkInteractionMediaCheckpoint(GTPChatLine interaction)
    {
        if (interaction == null) return;
        _interactionMediaCheckpoints[interaction] = GetCurrentChatImageCount();
    }

    private void MarkLatestAssistantMediaCheckpoint()
    {
        var last = _promptManager != null ? _promptManager.GetLastInteraction() : null;
        if (last != null && last._role == "assistant")
            MarkInteractionMediaCheckpoint(last);
    }

    private int GetInteractionMediaCheckpoint(GTPChatLine interaction)
    {
        if (interaction != null && _interactionMediaCheckpoints.TryGetValue(interaction, out int count))
            return Mathf.Clamp(count, 0, GetCurrentChatImageCount());

        return GetCurrentChatImageCount();
    }

    private void PruneMediaCheckpointsTo(IReadOnlyCollection<GTPChatLine> keptLines)
    {
        if (_interactionMediaCheckpoints.Count == 0) return;
        if (keptLines == null || keptLines.Count == 0)
        {
            _interactionMediaCheckpoints.Clear();
            return;
        }

        var keep = new HashSet<GTPChatLine>(keptLines);
        var remove = new List<GTPChatLine>();
        foreach (var kv in _interactionMediaCheckpoints)
        {
            if (!keep.Contains(kv.Key))
                remove.Add(kv.Key);
        }
        for (int i = 0; i < remove.Count; i++)
            _interactionMediaCheckpoints.Remove(remove[i]);
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

    // Debounce state for the on-resize bubble re-fit (see RequestBubbleRelayout).
    private bool _bubbleRelayoutPending;
    private float _lastBubbleLayoutWidth = -1f;

    /// <summary>
    /// Each bubble's <see cref="LayoutElement.preferredHeight"/> is computed once from
    /// the wrap width at the moment it was appended / edited / font-resized. When the
    /// panel (or splitter) is resized, the text reflows to the new width but those
    /// cached heights go stale - widening leaves a too-tall bubble with empty space
    /// below the text. Call this whenever the chat wrap width changes to re-fit every
    /// bubble. Debounced: a single coroutine handles a live resize drag (where the
    /// width changes every frame) and re-fits each ~2 frames until the width settles.
    /// </summary>
    private void RequestBubbleRelayout()
    {
        if (_chatContent == null || _bubbleRelayoutPending) return;
        if (Mathf.Abs(_chatContent.rect.width - _lastBubbleLayoutWidth) < 1f) return;
        StartCoroutine(RelayoutAllBubblesDeferred());
    }

    private IEnumerator RelayoutAllBubblesDeferred()
    {
        _bubbleRelayoutPending = true;
        float lastWidth = float.NaN;
        // Keep re-fitting until the wrap width stops changing for one pass, so a live
        // edge/splitter drag reflows smoothly and ends settled on the final width.
        while (_chatContent != null)
        {
            // Two frames lets the VLG re-stretch each bubble's input field to the new
            // width before we measure the wrapped text height.
            yield return null;
            yield return null;
            if (_chatContent == null) break;

            float w = _chatContent.rect.width;
            if (!float.IsNaN(lastWidth) && Mathf.Abs(w - lastWidth) < 0.5f) break;
            lastWidth = w;
            RelayoutAllBubblesNow();
        }

        _lastBubbleLayoutWidth = lastWidth;
        _bubbleRelayoutPending = false;
    }

    /// <summary>
    /// Recompute every bubble's preferred height from its current (already laid-out)
    /// input-field width, then do a single layout rebuild. Same math as
    /// <see cref="ResizeBubbleDeferred"/> but batched - one rebuild for all bubbles
    /// instead of one per bubble (which would be O(n^2) during a resize drag).
    /// </summary>
    private void RelayoutAllBubblesNow()
    {
        if (_chatContent == null) return;

        foreach (var input in _chatContent.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input == null || input.textComponent == null) continue;
            var le = input.GetComponent<LayoutElement>();
            if (le == null) continue;

            var inputRT = input.GetComponent<RectTransform>();
            float wrapWidth = inputRT != null ? inputRT.rect.width : 0f;
            if (wrapWidth < 32f) continue; // not laid out yet; skip this pass

            Vector2 size = input.textComponent.GetPreferredValues(input.text, wrapWidth, 0f);
            le.preferredHeight = Mathf.Max(18f, size.y + 4f); // +4 for descender slack
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_chatContent);
    }

    private void AddSystemMessage(string text, bool includeInLLMRecap = true)
    {
        // Info / system bubbles aren't part of the LLM conversation, so leave them readOnly
        // (linkedInteraction = null).
        if (GetShowDebugStuff())
            AppendInfoBubble(text);

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

    private void OnSkillListChanged(IReadOnlyList<string> addedIds, IReadOnlyList<string> removedIds)
    {
        int addedCount = addedIds != null ? addedIds.Count : 0;
        int removedCount = removedIds != null ? removedIds.Count : 0;
        if (addedCount == 0 && removedCount == 0)
            return;

        var sb = new StringBuilder();
        sb.Append("AI Chat skill files changed:");
        if (addedCount > 0)
        {
            sb.Append(" added ");
            AppendQuotedSkillIdList(sb, addedIds);
        }
        if (removedCount > 0)
        {
            if (addedCount > 0)
                sb.Append(";");
            sb.Append(" removed ");
            AppendQuotedSkillIdList(sb, removedIds);
        }
        sb.Append(".");
        AddSystemMessage(sb.ToString(), includeInLLMRecap: false);
    }

    private static void AppendQuotedSkillIdList(StringBuilder sb, IReadOnlyList<string> ids)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append("'").Append(ids[i]).Append("'");
        }
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
        sb.Append(InfoRecapMarker);
        for (int i = 0; i < unsent.Count; i++)
        {
            sb.Append("\n- ").Append(unsent[i].m_text);
            unsent[i].m_alreadySentToLLM = true;
        }
        return sb.ToString();
    }

    private static string BuildDisplaySafeUserText(GTPChatLine line)
    {
        if (line == null) return "";
        if (line._displayContent != null)
            return line._displayContent;

        // Fallback for turns created before display content was stored separately.
        // New user lines keep the exact LLM payload in _content and the clean
        // human-visible text in _displayContent.
        string content = line._content ?? "";
        int recapIndex = content.IndexOf(InfoRecapMarker, StringComparison.Ordinal);
        return recapIndex >= 0 ? content.Substring(0, recapIndex) : content;
    }

    private static string AppendUserPostMessageToText(string text)
    {
        string postMessage = GetUserPostMessage().Trim();
        if (string.IsNullOrEmpty(postMessage))
            return text ?? "";

        string baseText = text ?? "";
        if (string.IsNullOrWhiteSpace(baseText))
            return postMessage;

        return baseText.TrimEnd() + "\n\n" + postMessage;
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

            // Bold (must run before single * italic so ** isn't eaten by it). CJK
            // fallback glyphs do not have a real bold face, so avoid synthetic TMP
            // bold on Japanese/Chinese/Korean runs where it can fill in strokes.
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", m => ApplyReadableBold(m.Groups[1].Value), RegexOptions.Singleline);
            // Italic / single-asterisk emphasis -> bold (matches AdventureText behavior)
            text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>", RegexOptions.Singleline);
            // `inline code` -> monospaced color
            text = Regex.Replace(text, @"`([^`]+)`", "<mark=#00000020><font=\"LiberationSans SDF\"><color=#7A1F1F>$1</color></font></mark>", RegexOptions.Singleline);
            // Headings (#, ##, ###) at line start -> <size> + readable bold.
            text = Regex.Replace(text, @"(?m)^###\s+(.+)$", m => "<size=110%>" + ApplyReadableBold(m.Groups[1].Value) + "</size>");
            text = Regex.Replace(text, @"(?m)^##\s+(.+)$", m => "<size=120%>" + ApplyReadableBold(m.Groups[1].Value) + "</size>");
            text = Regex.Replace(text, @"(?m)^#\s+(.+)$", m => "<size=130%>" + ApplyReadableBold(m.Groups[1].Value) + "</size>");
            // Simple bullet lists: lines starting with "- " or "* " -> bullet char
            text = Regex.Replace(text, @"(?m)^\s*[-*]\s+(.+)$", "  \u2022 $1");
            // "[skill: X]" tool-call markers become clickable links: a left click on one
            // expands the attributes that were sent to the tool (prompt, lyrics, voice...)
            // below the bubble text. The visible characters stay EXACTLY "[skill: X]" and
            // the wrapping tags are stripped by RemoveTMPTagsFromString, so an edit /
            // focus-loss write-back compares equal to the stored display text.
            text = Regex.Replace(text, @"\[skill: ([A-Za-z0-9_]+)\]", "<link=\"skill\"><color=#2B5FA6><u>[skill: $1]</u></color></link>");
        }
        catch
        {
            // Malformed input - return raw text so we never crash the UI thread.
        }
        return text;
    }

    private static string ApplyReadableBold(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!ContainsCjk(text)) return "<b>" + text + "</b>";

        var result = new StringBuilder(text.Length + 16);
        var run = new StringBuilder();
        bool runIsBold = false;
        bool hasRun = false;

        void FlushRun()
        {
            if (!hasRun) return;
            if (runIsBold) result.Append("<b>");
            result.Append(run);
            if (runIsBold) result.Append("</b>");
            run.Length = 0;
            hasRun = false;
        }

        foreach (char ch in text)
        {
            bool boldThisChar = !IsCjkReadableGlyph(ch);
            if (hasRun && boldThisChar != runIsBold)
                FlushRun();

            runIsBold = boldThisChar;
            hasRun = true;
            run.Append(ch);
        }

        FlushRun();
        return result.ToString();
    }

    private static bool ContainsCjk(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char ch in text)
        {
            if (IsCjkReadableGlyph(ch))
                return true;
        }
        return false;
    }

    private static bool IsCjkReadableGlyph(char ch)
    {
        return (ch >= '\u3000' && ch <= '\u30ff')  // CJK punctuation, hiragana, katakana
            || (ch >= '\u3400' && ch <= '\u9fff')  // CJK ideographs
            || (ch >= '\uf900' && ch <= '\ufaff')  // CJK compatibility ideographs
            || (ch >= '\uff00' && ch <= '\uffef'); // Fullwidth forms
    }

    // ---------- Send / Stop / Clear ----------

    /// <summary>
    /// Handles the "Auto repeat msg" checkbox. Checking it seeds the repeat counter
    /// from the N field and starts the loop: if the chat is idle we kick the first
    /// send next frame, otherwise FinalizeAssistantTurn picks it up once the current
    /// reply finishes. Unchecking (including the auto-uncheck when the burst ends)
    /// just drains the counter so no further sends fire.
    /// </summary>
    private void OnAutoRepeatToggled(bool on)
    {
        // Deliberately NOT persisted: the toggle is session-only and always
        // starts OFF on launch (see CreateUI). Only the repeat count is saved.
        if (!on)
        {
            _autoContinueRemaining = 0;
            // Restore the N field to the saved target (it was showing the live
            // countdown) so it's ready at the full count for the next run.
            SetAutoRepeatCountField(GetAutoContinueCount());
            return;
        }

        _autoContinueRemaining = GetAutoContinueCount();
        if (_autoContinueRemaining <= 0)
        {
            // N is 0 (or blank) - nothing to repeat. Drop the check back off so the
            // box never sits checked-but-idle doing nothing.
            if (_autoContinueToggle != null) _autoContinueToggle.isOn = false;
            return;
        }

        // Only auto-start now if a send is actually possible. If a reply is mid-flight
        // (or a summary/sidecar job is pending), the loop starts from the next send
        // opportunity instead.
        if (!_isStreaming && !_waitingForForcedMainLLM && !_compactSummaryInFlight && !HasPendingSidecarWork())
            StartCoroutine(FireAutoContinueNextFrame());
    }

    /// <summary>
    /// Sets the "Auto repeat msg" N field's displayed value. Uses SetTextWithoutNotify
    /// (so it never fires onEndEdit / overwrites the saved N) and ForceLabelUpdate -
    /// a plain .text assignment on an unfocused TMP_InputField doesn't reliably
    /// refresh the visible glyphs, which is why the live countdown wasn't showing.
    /// Skips the update while the user is actively typing in the field so the live
    /// countdown can't clobber an in-progress edit.
    /// </summary>
    private void SetAutoRepeatCountField(int value)
    {
        if (_autoContinueCountInput == null) return;
        if (_autoContinueCountInput.isFocused) return;
        _autoContinueCountInput.SetTextWithoutNotify(value.ToString());
        _autoContinueCountInput.ForceLabelUpdate();
    }

    /// <summary>
    /// Called when the user commits an edit to the N field. Always updates the saved
    /// target; if a repeat run is currently active, the new value also takes effect
    /// immediately on the live countdown (setting it to 0 ends the run).
    /// </summary>
    private void OnAutoRepeatCountEdited(int value)
    {
        SetAutoContinueCount(value);
        if (_autoContinueToggle != null && _autoContinueToggle.isOn)
        {
            _autoContinueRemaining = value;
            if (value <= 0) _autoContinueToggle.isOn = false;
        }
    }

    private bool ValidateForcedMainLLMForSend(bool currentTurnAddsRawImages)
    {
        int overrideID = GetMainLLMOverrideInstanceID();
        if (overrideID == MAIN_LLM_DEFAULT_ID)
            return true;

        var manager = LLMInstanceManager.Get();
        var inst = manager != null ? manager.GetInstance(overrideID) : null;
        if (!IsSelectableMainLLMInstance(inst))
        {
            SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);
            RefreshMainLLMDropdownOptions();
            AddSystemMessage("Main LLM override was reset to Default because the selected LLM is no longer active.", includeInLLMRecap: false);
            return true;
        }

        bool chatAlreadyHasRawImages = _promptManager != null && _promptManager.HasAnyImages();
        if ((chatAlreadyHasRawImages || currentTurnAddsRawImages) && !inst.supportsVision)
        {
            AddSystemMessage(
                $"Main LLM override is set to {BuildMainLLMOptionText(inst)}, but this chat contains raw image data and that LLM is not marked Supports vision. " +
                "Select Default, choose a vision-capable main LLM, or clear the chat before using this override.",
                includeInLLMRecap: false);
            return false;
        }

        return true;
    }

    private void ResetPerTurnExecutionState()
    {
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
    }

    private void SendSyntheticContinue()
    {
        if (_promptManager == null)
            return;

        const string baseText = "(continue)";
        string visibleText = AppendUserPostMessageToText(baseText);
        ResetPerTurnExecutionState();
        _lastTurnAttachments.Clear();

        // Parity with the real-send path: the session post-message text can contain
        // trigger words, and the old dispatch-time scan saw continue turns too.
        QueueTriggeredSkillBodyInjections(visibleText);

        string llmPayloadText = BuildLLMPayloadWithInfoRecap(baseText);
        llmPayloadText = AppendUserPostMessageToText(llmPayloadText);
        _promptManager.AddInteraction("user", llmPayloadText);
        var userInteraction = _promptManager.GetLastInteraction();
        userInteraction?.RememberDisplayContent(visibleText);
        MarkInteractionMediaCheckpoint(userInteraction);
        AddUserMessage(visibleText, userInteraction);

        // Do not touch _inputField or _attachmentZone here. The user may have typed
        // a draft or staged attachments while the inspection sidecar was running.
        FocusInputDeferred();
        SendChatTurn(visibleText);
    }

    private void OnSendClicked()
    {
        if (_isStreaming || _waitingForForcedMainLLM) return;

        // Slash commands (e.g. "/applystyle ...") are caught and handled LOCALLY -
        // they are never forwarded to the chat AI. Unknown slash text falls through
        // to the normal send path so a message that merely starts with "/" still works.
        {
            string rawInput = _inputField != null ? _inputField.text : "";
            string slashText = rawInput.Trim();
            if (TryHandleSlashCommand(slashText))
            {
                RecordPromptHistoryEntry(slashText);
                if (_inputField != null) _inputField.text = "";
                _inputUndo?.ResetHistory();
                FocusInputDeferred();
                return;
            }
        }

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
        int pendingCaptions = CountPendingAttachmentCaptions();
        if (pendingCaptions > 0)
        {
            int totalAttachments = _attachmentZone != null ? _attachmentZone.Count : pendingCaptions;
            AddSystemMessage(
                $"Captioning attached images ({Mathf.Max(0, totalAttachments - pendingCaptions)}/{totalAttachments} ready)... waiting before send.",
                includeInLLMRecap: false);
            UpdateAttachmentCaptionStatus(force: true);
            return;
        }
        int pendingInspections = CountPendingInspectImageJobs();
        if (pendingInspections > 0)
        {
            string label = _inspectImageJob != null
                ? _inspectImageJob.sourceLabel
                : (_inspectImageQueue.Count > 0 ? _inspectImageQueue[0].sourceLabel : "image");
            AddSystemMessage(
                $"Inspecting {label}... waiting for the vision result before send.",
                includeInLLMRecap: false);
            UpdateInspectImageStatus(force: true);
            return;
        }
        if (_videoImportCount > 0)
        {
            AddSystemMessage(
                $"Importing video clip{(_videoImportCount == 1 ? "" : "s")}... waiting before send.",
                includeInLLMRecap: false);
            UpdateVideoImportStatus(force: true);
            return;
        }
        int pendingVideoCaptions = CountPendingVideoCaptions();
        if (pendingVideoCaptions > 0)
        {
            AddSystemMessage(
                $"Captioning video clip{(pendingVideoCaptions == 1 ? "" : "s")}... waiting before send.",
                includeInLLMRecap: false);
            UpdateVideoImportStatus(force: true);
            return;
        }
        if (HasPendingWebWork())
        {
            AddSystemMessage(
                _webFetchCount > 0 ? "Fetching from the web... waiting before send." : "Captioning web image... waiting before send.",
                includeInLLMRecap: false);
            UpdateWebFetchStatus(force: true);
            return;
        }

        // A user-driven send (typed message OR an auto-repeat fire) means the human is
        // back in control, so reset the model's runaway self-continue counter. Synthetic
        // `continue` turns go through SendSyntheticContinue, not here, so they never reset it.
        _consecutiveSelfContinues = 0;

        // The auto-repeat counter is owned by the "Auto repeat msg" toggle handler
        // and FinalizeAssistantTurn now - a plain Send no longer starts a burst.
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

        bool currentTurnAddsRawImages = attachedCount > 0 && GetIncludeImageData();
        if (!ValidateForcedMainLLMForSend(currentTurnAddsRawImages))
            return;

        RecordPromptHistoryEntry(text);

        if (string.IsNullOrWhiteSpace(text))
            text = attachedCount > 0 ? "(no caption)" : "(continue)";

        ResetPerTurnExecutionState();

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
                // Both indices derive from the count of non-null attachments so far, so a
                // skipped (null-bytes) entry can't shift the numbering: attachIdx is the
                // per-message index GetTurnAttachmentBytes uses, chatIdx the permanent
                // bubble number PromoteAttachmentsToChatImages will assign.
                int attachIdx = _lastTurnAttachments.Count + 1;
                int chatIdx = firstChatIdx + attachIdx - 1;
                if (includeBytes)
                    _promptManager.AddPendingImage(System.Convert.ToBase64String(info.bytes), chatIdx);
                _lastTurnAttachments.Add(info.bytes);

                // Header line carries dimensions + the short label (if any).
                // The long description follows on its own indented line so the
                // LLM has the full ~200-word context for THIS turn without
                // visually drowning the user's typed message.
                // The PERMANENT chat_image number leads and the per-message
                // attachment index is explicitly scoped: models kept copying the
                // bubble number into attachment= (which restarts at 1 each message),
                // silently killing the action on later turns.
                metadataBlock.Append("[Attached Image chat_image=\"").Append(chatIdx)
                    .Append("\" (attachment=\"").Append(attachIdx).Append("\" this message only)");
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

        string visibleText = AppendUserPostMessageToText(text);

        // Attach any newly keyword-triggered skill bodies BEFORE the recap fold below
        // so the model can use them on this very turn. They ride this user message's
        // LLM payload (append-only at the request tail), keeping the prompt head
        // byte-stable for server-side prompt caches.
        QueueTriggeredSkillBodyInjections(visibleText);

        // Quietly fold any unsent Info bubbles (skill warnings/errors that have piled
        // up since the last send) into the LLM payload as a "for the future, please
        // keep this in mind" recap, so the model can learn from its own mistakes
        // without forcing the user to copy-paste them. This recap stays hidden from
        // the visible bubble; the session post-message reminder is intentionally
        // visible and stored normally. Marking each recapped entry as already-sent
        // prevents re-attaching it on subsequent turns.
        string llmPayloadText = BuildLLMPayloadWithInfoRecap(text);
        llmPayloadText = AppendUserPostMessageToText(llmPayloadText);

        // Add the interaction first so we can link the bubble to it - that link is what
        // makes the bubble editable (and what makes user edits flow back into the prompt
        // history sent to the LLM on subsequent turns).
        _promptManager.AddInteraction("user", llmPayloadText);
        var userInteraction = _promptManager.GetLastInteraction();
        userInteraction?.RememberDisplayContent(visibleText);
        MarkInteractionMediaCheckpoint(userInteraction);
        AddUserMessage(visibleText, userInteraction);

        // Drop the staged thumbnails now that they've been baked into the conversation.
        if (attachedCount > 0)
            _attachmentZone?.ClearAttachments();

        // While "Auto repeat msg" is running, keep the text in the box so the next
        // repeat can re-send it (and so mid-run edits are picked up). A normal send
        // (box unchecked) clears as before.
        if (_autoContinueToggle == null || !_autoContinueToggle.isOn)
        {
            _inputField.text = "";
            _inputUndo?.ResetHistory();
        }
        FocusInputDeferred();

        SendChatTurn(visibleText);
    }

    private void OnPromptInputValueChangedForHistory(string _)
    {
        if (_applyingPromptHistoryText)
            return;

        ResetPromptHistoryNavigation();
        _promptHistoryCaretCacheValid = false;
    }

    private void RecordPromptHistoryEntry(string text)
    {
        if (_autoContinueFiring)
            return;

        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (_promptHistory.Count > 0
            && string.Equals(_promptHistory[_promptHistory.Count - 1], text, StringComparison.Ordinal))
        {
            ResetPromptHistoryNavigation();
            return;
        }

        _promptHistory.Add(text);
        if (_promptHistory.Count > PROMPT_HISTORY_MAX_ENTRIES)
            _promptHistory.RemoveRange(0, _promptHistory.Count - PROMPT_HISTORY_MAX_ENTRIES);

        ResetPromptHistoryNavigation();
    }

    private void ResetPromptHistoryNavigation()
    {
        _promptHistoryIndex = -1;
        _promptHistoryDraft = "";
    }

    private void HandlePromptHistoryArrowKeys()
    {
        bool up = Input.GetKeyDown(KeyCode.UpArrow);
        bool down = Input.GetKeyDown(KeyCode.DownArrow);
        if (!up && !down)
            return;

        if (HasPromptHistoryNavigationModifier())
            return;

        if (_inputField == null || !_inputField.isFocused)
            return;

        if (_promptHistoryLastHadSelection || InputFieldHasSelection(_inputField))
            return;

        int currentLine;
        int currentLineCount;
        if (!TryGetInputCaretLine(_inputField, out currentLine, out currentLineCount))
            return;

        int previousLine = _promptHistoryCaretCacheValid ? _promptHistoryLastCaretLine : currentLine;
        int previousLineCount = _promptHistoryCaretCacheValid ? _promptHistoryLastLineCount : currentLineCount;

        if (up)
        {
            if (previousLine <= 0)
                TryNavigatePromptHistory(-1);
        }
        else if (down)
        {
            if (previousLine >= Mathf.Max(0, previousLineCount - 1))
                TryNavigatePromptHistory(1);
        }
    }

    private static bool HasPromptHistoryNavigationModifier()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }

    private static bool InputFieldHasSelection(TMP_InputField field)
    {
        if (field == null)
            return false;

        return field.selectionAnchorPosition != field.selectionFocusPosition
            || field.selectionStringAnchorPosition != field.selectionStringFocusPosition;
    }

    private bool TryNavigatePromptHistory(int direction)
    {
        if (_inputField == null || _promptHistory.Count == 0)
            return false;

        if (direction < 0)
        {
            if (_promptHistoryIndex < 0)
            {
                _promptHistoryDraft = _inputField.text ?? "";
                _promptHistoryIndex = _promptHistory.Count - 1;
            }
            else if (_promptHistoryIndex > 0)
            {
                _promptHistoryIndex--;
            }
            else
            {
                return false;
            }

            ApplyPromptHistoryText(_promptHistory[_promptHistoryIndex]);
            return true;
        }

        if (direction > 0)
        {
            if (_promptHistoryIndex < 0)
                return false;

            if (_promptHistoryIndex < _promptHistory.Count - 1)
            {
                _promptHistoryIndex++;
                ApplyPromptHistoryText(_promptHistory[_promptHistoryIndex]);
            }
            else
            {
                string draft = _promptHistoryDraft;
                ResetPromptHistoryNavigation();
                ApplyPromptHistoryText(draft);
            }
            return true;
        }

        return false;
    }

    private void ApplyPromptHistoryText(string text)
    {
        if (_inputField == null)
            return;

        _applyingPromptHistoryText = true;
        try
        {
            text = text ?? "";
            _inputField.text = text;
            MoveInputCaretToEnd(_inputField);
            _inputField.ForceLabelUpdate();
        }
        finally
        {
            _applyingPromptHistoryText = false;
        }

        _inputUndo?.ResetHistory();
        UpdatePromptHistoryCaretCache();
    }

    private static void MoveInputCaretToEnd(TMP_InputField field)
    {
        if (field == null)
            return;

        int end = (field.text ?? "").Length;
        field.caretPosition = end;
        field.stringPosition = end;
        field.selectionAnchorPosition = end;
        field.selectionFocusPosition = end;
        field.selectionStringAnchorPosition = end;
        field.selectionStringFocusPosition = end;
    }

    private void UpdatePromptHistoryCaretCache()
    {
        if (_inputField == null || !_inputField.isFocused)
        {
            _promptHistoryCaretCacheValid = false;
            _promptHistoryLastHadSelection = false;
            return;
        }

        int line;
        int lineCount;
        if (TryGetInputCaretLine(_inputField, out line, out lineCount))
        {
            _promptHistoryLastCaretLine = line;
            _promptHistoryLastLineCount = Mathf.Max(1, lineCount);
            _promptHistoryLastHadSelection = InputFieldHasSelection(_inputField);
            _promptHistoryCaretCacheValid = true;
        }
        else
        {
            _promptHistoryCaretCacheValid = false;
            _promptHistoryLastHadSelection = InputFieldHasSelection(_inputField);
        }
    }

    private static bool TryGetInputCaretLine(TMP_InputField field, out int line, out int lineCount)
    {
        line = 0;
        lineCount = 1;
        if (field == null)
            return false;

        string text = field.text ?? "";
        int caret = Mathf.Clamp(field.stringPosition, 0, text.Length);

        field.ForceLabelUpdate();
        TMP_Text tmp = field.textComponent;
        if (tmp == null)
            return false;

        tmp.ForceMeshUpdate();
        TMP_TextInfo info = tmp.textInfo;
        lineCount = Mathf.Max(1, info != null ? info.lineCount : 1);
        if (info == null || info.characterCount <= 0)
            return true;

        int previousLine = 0;
        bool havePreviousLine = false;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            int rawStart = ch.index;
            int rawEnd = rawStart + Mathf.Max(1, ch.stringLength);
            int charLine = Mathf.Clamp(ch.lineNumber, 0, lineCount - 1);

            if (rawStart == caret)
            {
                line = charLine;
                return true;
            }

            if (rawStart > caret)
                break;

            if (rawEnd <= caret || rawStart < caret)
            {
                previousLine = charLine;
                havePreviousLine = true;
            }
        }

        if (caret > 0 && caret <= text.Length && (text[caret - 1] == '\n' || text[caret - 1] == '\r'))
            line = Mathf.Min(previousLine + 1, lineCount - 1);
        else
            line = havePreviousLine ? previousLine : 0;

        return true;
    }

    /// <summary>
    /// Intercept local "/command ..." lines typed into the chat box. Returns true if
    /// the line was a RECOGNIZED slash command and has been handled locally (the caller
    /// then clears the box and does NOT send anything to the chat AI). Returns false for
    /// plain text and for unrecognized slash words, which fall through to the normal
    /// send path - so a real message that merely begins with "/" is still deliverable.
    /// </summary>
    private bool TryHandleSlashCommand(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '/') return false;

        int sp = text.IndexOf(' ');
        string cmd = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant();
        string arg = sp < 0 ? "" : text.Substring(sp + 1).Trim();

        switch (cmd)
        {
            case "/applystyle":
                HandleApplyStyleCommand(arg);
                return true;
            default:
                return false; // not a known command - let it go to the chat AI
        }
    }

    /// <summary>
    /// "/applystyle &lt;directive&gt;" installs a session-only restyle directive: every
    /// image/movie render the chat AI subsequently produces has its prompt rewritten by a
    /// small LLM job per the directive, right before it is sent to the GPU. Bare
    /// "/applystyle" (no text) clears it. Not persisted - lives until cleared or app exit.
    /// </summary>
    private void HandleApplyStyleCommand(string directive)
    {
        directive = directive == null ? "" : directive.Trim();
        if (_actionExecutor == null)
        {
            AddSystemMessage("Can't set a style directive yet - the chat skill executor isn't initialized.", includeInLLMRecap: false);
            return;
        }

        if (directive.Length == 0)
        {
            _actionExecutor.SetStyleDirective(null);
            AddSystemMessage("Style directive cleared - renders use their original prompts again.", includeInLLMRecap: false);
            return;
        }

        _actionExecutor.SetStyleDirective(directive);
        AddSystemMessage(
            "Style directive set. Each image/movie render's prompt will be rewritten by a small LLM job before it's sent:\n  \"" + directive + "\"\n" +
            "(Send /applystyle with no text to clear it.)",
            includeInLLMRecap: false);
    }

    private void StartWaitingForForcedMainLLM(LLMInstanceInfo inst, string latestUserMessage)
    {
        if (inst == null) return;

        CancelForcedMainLLMWait(showBubble: false);
        _waitingForForcedMainLLM = true;
        _waitingForcedMainLLMId = inst.instanceID;
        _waitingForcedMainLLMName = BuildMainLLMOptionText(inst);
        SetBusyUI(true, $"Waiting for {_waitingForcedMainLLMName}...");
        _forcedMainLLMWaitCoroutine = StartCoroutine(WaitForForcedMainLLMCoroutine(inst.instanceID, latestUserMessage, _chatTurnEpoch));
    }

    private IEnumerator WaitForForcedMainLLMCoroutine(int instanceID, string latestUserMessage, int turnEpoch)
    {
        while (_waitingForForcedMainLLM && _chatTurnEpoch == turnEpoch)
        {
            var manager = LLMInstanceManager.Get();
            var inst = manager != null ? manager.GetInstance(instanceID) : null;
            if (!IsSelectableMainLLMInstance(inst))
            {
                SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);
                RefreshMainLLMDropdownOptions();
                AddSystemMessage("Main LLM override was reset to Default because the selected LLM is no longer active.", includeInLLMRecap: false);
                break;
            }

            if (_promptManager != null && _promptManager.HasAnyImages() && !inst.supportsVision)
            {
                AddSystemMessage(
                    $"Main LLM override is set to {BuildMainLLMOptionText(inst)}, but this chat contains raw image data and that LLM is not marked Supports vision.",
                    includeInLLMRecap: false);
                break;
            }

            if (TryFindFreeReplica(inst, out _))
            {
                _waitingForForcedMainLLM = false;
                _forcedMainLLMWaitCoroutine = null;
                _waitingForcedMainLLMId = -1;
                _waitingForcedMainLLMName = "";
                SetBusyUI(false, "Starting LLM...");
                SendChatTurn(latestUserMessage, instanceID);
                yield break;
            }

            _waitingForcedMainLLMName = BuildMainLLMOptionText(inst);
            if (_statusText != null)
                _statusText.text = $"Waiting for {_waitingForcedMainLLMName}...";
            yield return new WaitForSecondsRealtime(0.5f);
        }

        _waitingForForcedMainLLM = false;
        _forcedMainLLMWaitCoroutine = null;
        _waitingForcedMainLLMId = -1;
        _waitingForcedMainLLMName = "";
        if (!_isStreaming)
            SetBusyUI(false, "Idle");
    }

    private void CancelForcedMainLLMWait(bool showBubble)
    {
        bool wasWaiting = _waitingForForcedMainLLM || _forcedMainLLMWaitCoroutine != null;
        if (_forcedMainLLMWaitCoroutine != null)
        {
            try { StopCoroutine(_forcedMainLLMWaitCoroutine); } catch { }
            _forcedMainLLMWaitCoroutine = null;
        }

        _waitingForForcedMainLLM = false;
        _waitingForcedMainLLMId = -1;
        _waitingForcedMainLLMName = "";

        if (!wasWaiting) return;
        if (showBubble)
            AddSystemMessage("Stopped waiting for the forced main LLM.", includeInLLMRecap: false);
        if (!_isStreaming)
            SetBusyUI(false, showBubble ? "Stopped waiting" : "Idle");
    }

    private void OnStopClicked()
    {
        bool inspectPending = CountPendingInspectImageJobs() > 0;
        bool skillResumePending = HasSkillLoadAutoResumePendingForCurrentTurn();
        bool genericContinuePending = HasGenericContinuePendingForCurrentTurn();
        bool forcedWaitPending = _waitingForForcedMainLLM;
        bool webPending = HasPendingWebWork();
        bool audioPending = HasPendingAudioGeneration();
        if (!_isStreaming && !inspectPending && !skillResumePending && !genericContinuePending && !forcedWaitPending && !webPending && !audioPending) return;
        // Stop fully ends auto-repeat: uncheck the box (its handler also zeroes the
        // counter) so it doesn't quietly resume on the next reply.
        _autoContinueRemaining = 0;
        if (_autoContinueToggle != null) _autoContinueToggle.isOn = false;
        CancelSkillLoadAutoResume();
        CancelGenericContinue();
        _consecutiveSelfContinues = 0;

        if (inspectPending)
            CancelAllInspectImageJobs(showBubble: true);

        if (forcedWaitPending)
            CancelForcedMainLLMWait(showBubble: true);

        if (webPending)
        {
            CancelInspectAutoResume();
            CancelAllWebFetches(showBubble: true);
        }

        if (audioPending)
            CancelAllAudioGeneration(showBubble: true);

        if (!_isStreaming)
        {
            SetBusyUI(false, inspectPending ? "Stopped inspection" : (forcedWaitPending ? "Stopped waiting" : (webPending ? "Stopped web fetch" : (audioPending ? "Stopped audio generation" : "Stopped"))));
            return;
        }

        TryCancelActiveRequests();
        // Aborting the web request means OnLLMCompletedCallback never fires, so the
        // partial reply that already streamed in would otherwise never get committed
        // to history or made editable. Commit it ourselves before finalizing so the
        // stopped bubble behaves like a completed one (editable, sent on next turn).
        CommitPartialAssistantReply();
        FinalizeAssistantTurn(aborted: true);
        // Invalidate any parked pump / in-flight deferred coroutine so a
        // stopped book doesn't keep spawning pages after the user bailed.
        _actionExecutor?.ResetForNewTurn();
        CancelAllWebFetches(showBubble: false);
        CancelAllAudioGeneration(showBubble: false);
    }

    /// <summary>
    /// Commit whatever the assistant streamed before the user hit Stop: flush the
    /// action parser, push the partial text into the conversation history, and flip
    /// the bubble from readOnly to editable. Mirrors the tail of OnLLMCompletedCallback,
    /// which the abort path skips because CancelCurrentRequest() suppresses that callback.
    /// </summary>
    private void CommitPartialAssistantReply()
    {
        var completedField = _streamingAssistantField;
        if (completedField == null) return;

        // Flush any text the action parser was holding back (e.g. a trailing partial
        // "<" awaiting a tag that will now never arrive).
        if (_actionParser != null)
        {
            string finalDisplay = _actionParser.Flush();
            if (!string.IsNullOrEmpty(finalDisplay))
                _streamBuffer.Append(finalDisplay);
        }

        string visibleText = BuildVisibleStreamText(_streamBuffer.ToString());
        completedField.text = ConvertMarkdownToTMP(visibleText);

        // Nothing streamed in yet -> leave the empty bubble readOnly and don't pollute
        // history with a blank assistant turn.
        string historyText = PreserveActionTagsForHistory(visibleText);
        if (string.IsNullOrEmpty(historyText)) return;

        AIChatLog.Response("chat", historyText);
        _promptManager.AddInteraction("assistant", historyText);
        var assistantInteraction = _promptManager.GetLastInteraction();
        assistantInteraction?.RememberDisplayContent(visibleText);
        MarkInteractionMediaCheckpoint(assistantInteraction);
        EnableBubbleEditing(completedField, assistantInteraction);
    }

    private void OnClearClicked()
    {
        HideBubbleContextMenu();
        HideRewindConfirmation();
        ClearSpeechSelectionOverlay();
        ClearCachedSpeakSelection();
        _autoContinueRemaining = 0;
        if (_autoContinueToggle != null) _autoContinueToggle.isOn = false;
        CancelAllAttachmentCaptions();
        CancelAllInspectImageJobs(showBubble: false);
        CancelForcedMainLLMWait(showBubble: false);
        CancelSkillLoadAutoResume();
        CancelGenericContinue();
        _consecutiveSelfContinues = 0;
        CancelAllWebFetches(showBubble: false);
        CancelAllAudioGeneration(showBubble: false);
        _webSearchSessions.Clear();
        _nextWebSearchId = 1;
        _webPageSessions.Clear();
        _nextWebPageId = 1;
        _webFetchedUrlToPic.Clear();
        _forwardedDescriptions.Clear();
        _videoImportEpoch++;
        _videoImportCount = 0;
        _videoImportStartTime = 0f;
        _videoImportStatusNextRefresh = 0f;
        _videoCaptionStartTime = 0f;
        _videoCaptionStatusNextRefresh = 0f;
        if (_videoClipChooser != null)
        {
            Destroy(_videoClipChooser.gameObject);
            _videoClipChooser = null;
        }
        // Discard any in-flight compact-summary; if its response landed after this
        // reset it would ReplaceInteractions() the old history right back in.
        _compactSummaryCancel?.Invoke();
        if (_isStreaming)
        {
            TryCancelActiveRequests();
            FinalizeAssistantTurn(aborted: true);
        }

        _promptManager.Reset();
        // Autoload-skill liveness resets by itself (it is derived from the now-empty
        // history); only the edit-tracking cache needs an explicit wipe.
        _sentAutoloadSkillBodies.Clear();
        _attachmentZone?.ClearAttachments();
        _lastTurnAttachments?.Clear();
        _lastPasteGroupPics.Clear();
        _chatImagePics?.Clear();
        _chatImageRecords?.Clear();
        _anchors?.Clear();
        _captionLabels?.Clear();
        _videoCaptionInFlight.Clear();
        _interactionMediaCheckpoints.Clear();
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
        AddWelcomeMessage();
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
    private void RebuildChatBubblesFromHistory(GTPChatLine forceVisibleInteraction = null)
    {
        if (_chatContent == null || _promptManager == null) return;

        for (int i = _chatContent.childCount - 1; i >= 0; i--)
            Destroy(_chatContent.GetChild(i).gameObject);

        // The pending-recap queue referenced Info bubbles that no longer exist;
        // clear it so a stale "for the future" block doesn't ride along on send.
        _infoMessages.Clear();

        foreach (var line in _promptManager.GetInteractionsList())
        {
            if (line == null) continue;
            bool forceVisible = ReferenceEquals(line, forceVisibleInteraction);
            if (string.IsNullOrEmpty(line._content) && !forceVisible) continue;
            if (line._role == "user")
            {
                string display = BuildDisplaySafeUserText(line);
                if (!string.IsNullOrWhiteSpace(display) || forceVisible)
                    AddUserMessage(display, line);
            }
            else if (line._role == "assistant")
            {
                // line._content keeps the RAW reply (with <aitools_action> tags) so
                // the LLM still sees its own prior actions. The bubble must show the
                // display-safe text, exactly like the live stream does on completion
                // (OnLLMCompletedCallback uses _actionParser.Flush() for the bubble
                // but stores the raw text in history). Without this, a rebuild after
                // Compact/edit leaks the raw markup into the chat.
                string display = line._displayContent ?? BuildDisplaySafeAssistantText(line._content) ?? "";
                if (!string.IsNullOrWhiteSpace(display) || forceVisible)
                    AppendBubble("Assistant", new Color(0.10f, 0.45f, 0.20f), display, AssistantBubbleBg, line);
            }
            else
            {
                // The compact summary is a first-class, always-visible, EDITABLE
                // bubble: linking the interaction makes HookEditingTo push user
                // corrections straight back into the history line the LLM reads.
                if (line._internalTag == COMPACT_SUMMARY_TAG)
                {
                    AppendBubble("Summary", SummaryLabelColor, line._content, SummaryBubbleBg, line);
                    continue;
                }
                // Other internal system context surfaces as debug-gated Info.
                if (GetShowDebugStuff())
                    AppendInfoBubble(line._content);
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
            // Visible without "Show debug stuff" - it's a direct reply to a click.
            RTQuickMessageManager.Get().ShowMessage($"Nothing to compact - already within the last {keepExchanges} exchange(s)");
            return;
        }

        var keptTail = all.GetRange(from, all.Count - from);
        _promptManager.ReplaceInteractions(keptTail);
        PruneMediaCheckpointsTo(keptTail);
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
            // Toasts, not debug-gated Info bubbles: these are direct responses to the
            // user clicking Summarize and must be visible with "Show debug stuff" off.
            RTQuickMessageManager.Get().ShowMessage($"Nothing to compact - already within the last {keepExchanges} exchange(s)");
            return;
        }

        var older = all.GetRange(0, from);
        var keptTail = all.GetRange(from, all.Count - from);

        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("No LLM is configured - can't summarize. Use Truncate instead");
            return;
        }

        // Honor the footer Main LLM override: a non-Default selection forces the
        // summary onto that same instance (it owns the follow-up turns, so it should
        // be the one writing the recap it will rely on). If every replica is busy,
        // queue on the least-loaded one rather than silently using a different LLM -
        // matching the default path's least-busy fallback below.
        int targetId = -1;
        int replicaIndex = 0;
        int overrideId = GetMainLLMOverrideInstanceID();
        if (overrideId != MAIN_LLM_DEFAULT_ID)
        {
            var forcedInst = instanceMgr.GetInstance(overrideId);
            if (!IsSelectableMainLLMInstance(forcedInst))
            {
                SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);
                RefreshMainLLMDropdownOptions();
                RTQuickMessageManager.Get().ShowMessage("Main LLM override reset to Default - the selected LLM is no longer active");
            }
            else
            {
                targetId = overrideId;
                if (!TryFindFreeReplica(forcedInst, out replicaIndex))
                    replicaIndex = FindLeastLoadedReplica(forcedInst);
            }
        }
        if (targetId < 0)
        {
            targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: false, out replicaIndex);
            if (targetId < 0)
                targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: false, out replicaIndex);
        }
        if (targetId < 0)
        {
            RTQuickMessageManager.Get().ShowMessage("No LLM slot is available right now. Try again shortly, or use Truncate");
            return;
        }
        var inst = instanceMgr.GetInstance(targetId);
        if (inst == null || inst.settings == null)
        {
            RTQuickMessageManager.Get().ShowMessage("The selected LLM is not ready. Use Truncate instead");
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
            // User lines can carry injected skill reference bodies in their recap
            // tail - instructions, not story, and often 10k+ tokens. Keep them OUT
            // of the summarizer's input; the restore step below keeps them alive in
            // the post-compact context instead.
            string flat = line._role == "user"
                ? StripInjectedSkillBodiesForTranscript(line._content)
                : line._content;
            transcript.Append(roleLabel).Append(": ").Append(flat).Append("\n\n");
        }

        var lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system",
            "You are a precise conversation summarizer. You will be given the earlier portion of a chat. Produce a dense recap and nothing else."));
        // Per-image recap notes follow the same window as the CHAT IMAGES block: only
        // the newest <Image context limit> images (plus named anchors, which stay
        // referenceable forever by name) get one-line descriptions. Without this,
        // summarizing a 200-image story burns the recap on descriptions of images
        // that already scrolled out of the usable image window.
        int imageContextLimit = GetImageContextLimit();
        string anchorPairs = BuildAnchorSlotPairs();
        string imageClause;
        if (imageCount <= 0)
        {
            imageClause = "There are currently no chat images. ";
        }
        else if (imageContextLimit > 0 && imageCount <= imageContextLimit)
        {
            imageClause =
                "CRUCIALLY, for every generated or attached image referred to as chat_image=\"N\", keep a one-line note of what image #N depicts and any name/identity tied to it, so it can still be referenced later. " +
                "There are currently " + imageCount + " chat image(s). ";
        }
        else
        {
            var noteTargets = new List<string>();
            if (imageContextLimit > 0)
            {
                int firstKept = imageCount - imageContextLimit + 1;
                noteTargets.Add("chat images #" + firstKept + "-#" + imageCount +
                                " (the newest " + imageContextLimit + " of " + imageCount + ")");
            }
            if (!string.IsNullOrEmpty(anchorPairs))
                noteTargets.Add("the ANCHORED images " + anchorPairs + " (named recurring subjects)");

            imageClause = noteTargets.Count == 0
                ? "Do NOT describe the " + imageCount + " chat images individually; mention them only in aggregate if the story needs it. "
                : "CRUCIALLY, keep a one-line note of what the image depicts and any name/identity tied to it ONLY for " +
                  string.Join(" and ", noteTargets) + ". " +
                  "Do NOT describe other older images individually - they are outside the usable image window; cover them in aggregate at most. ";
        }

        string instruction =
            "Summarize the conversation so far into a concise but information-dense recap that a continuation of this chat can rely on. " +
            "Preserve: the user's goals; any decisions, rules or constraints agreed on; key facts established; and where things currently stand. " +
            imageClause +
            "Output the recap only - no preamble and no sign-off.\n\n" +
            "Here is the earlier conversation to summarize:\n\n" + transcript.ToString();
        lines.Enqueue(new GTPChatLine("user", instruction));

        // ~4 chars/token is the usual English-prose ballpark; close enough for a
        // progress readout (the request is the instruction + a tiny system line).
        _compactSummaryApproxSentTokens = instruction.Length / 4;

        AddSystemMessage($"Compacting: summarizing {older.Count} older message(s) with the active LLM... the last {keepExchanges} exchange(s) and all images are kept.", includeInLLMRecap: false);

        bool done = false;
        Coroutine watchdog = null;
        int capId = targetId, capReplica = replicaIndex;

        // Live preview bubble: shows the summary text as it streams in, so a long
        // compact visibly makes progress instead of looking hung. Read-only; it is
        // destroyed on release and (on success) replaced by the real editable
        // Summary bubble when the rebuilt history lands.
        TMP_InputField previewField = AppendBubble(
            "Summary (generating...)", SummaryLabelColor,
            $"(summarizing {older.Count} older message{(older.Count == 1 ? "" : "s")}, ~{FormatApproxTokenCount(_compactSummaryApproxSentTokens)} tokens sent to the LLM...)",
            SummaryBubbleBg);
        GameObject previewRoot = previewField != null ? previewField.transform.parent.gameObject : null;
        var streamed = new StringBuilder();
        float lastPreviewUpdate = 0f;
        Action<string> onStreamChunk = chunk =>
        {
            if (done || string.IsNullOrEmpty(chunk)) return;
            streamed.Append(chunk);
            if (previewField == null) return;
            if (Time.unscaledTime - lastPreviewUpdate < 0.25f) return;
            lastPreviewUpdate = Time.unscaledTime;
            bool shouldAutoScroll = IsScrollAtBottom(_chatScroll);
            previewField.text = ConvertMarkdownToTMP(BuildVisibleStreamText(streamed.ToString()));
            if (shouldAutoScroll)
                StartCoroutine(ScrollToBottomDeferred());
        };

        Action release = () =>
        {
            if (done) return;
            done = true;
            if (watchdog != null) { try { StopCoroutine(watchdog); } catch { } }
            if (previewRoot != null) { Destroy(previewRoot); previewRoot = null; previewField = null; }
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
            // Streaming providers may hand the completion callback less than the
            // full text; the accumulated stream is the authoritative fallback.
            if (string.IsNullOrEmpty(raw) && streamed.Length > 0)
                raw = streamed.ToString().Trim();
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
                // Always-visible: a user-initiated summarize that fails must not be
                // silent when "Show debug stuff" is off.
                AddErrorBubble("Compact failed: the LLM returned an empty summary. History is unchanged." + detail);
                return;
            }

            var summaryLine = new GTPChatLine("system",
                "[Conversation summary - earlier history was compacted to save space]\n" + raw,
                COMPACT_SUMMARY_TAG);
            var rebuilt = new List<GTPChatLine>(keptTail.Count + 1) { summaryLine };
            rebuilt.AddRange(keptTail);

            // Loaded skill bodies must SURVIVE the compact even though they are
            // excluded from the summarizer's transcript: snapshot liveness against
            // the old history, replace it, then re-queue any body that lived only in
            // the summarized-away lines. The re-queue must happen AFTER
            // RebuildChatBubblesFromHistory, which clears _infoMessages as part of
            // its stale-recap protection; the restored copy then rides the next
            // outgoing message's recap tail exactly like a fresh keyword load.
            var liveSkillIdsBeforeCompact = ComputeLiveAutoloadSkillIds();
            _promptManager.ReplaceInteractions(rebuilt);
            MarkInteractionMediaCheckpoint(summaryLine);
            PruneMediaCheckpointsTo(rebuilt);
            RebuildChatBubblesFromHistory();

            var liveSkillIdsAfterCompact = ComputeLiveAutoloadSkillIds();
            var restoredSkillIds = new List<string>();
            foreach (string skillId in liveSkillIdsBeforeCompact)
            {
                if (liveSkillIdsAfterCompact.Contains(skillId)) continue;
                var liveSkill = _skillManager?.GetById(skillId);
                if (liveSkill == null) continue;
                QueueSkillBodyInjection(liveSkill);
                restoredSkillIds.Add(skillId);
            }
            if (restoredSkillIds.Count > 0)
            {
                restoredSkillIds.Sort(StringComparer.OrdinalIgnoreCase);
                var noticeSb = new StringBuilder();
                noticeSb.Append("Compact kept auto-loaded skill references alive: ");
                AppendQuotedSkillIdList(noticeSb, restoredSkillIds);
                noticeSb.Append(" (re-attached to the next message).");
                AddSystemMessage(noticeSb.ToString(), includeInLLMRecap: false);
            }
            AddSystemMessage($"Compacted {older.Count} older message(s) into a summary. Kept the last {keepExchanges} exchange(s); all images are intact.", includeInLLMRecap: false);
            // release() above reset the status to Idle; leave the result on screen
            // instead, matching how finished turns keep their token stats visible.
            if (!_isStreaming && _statusText != null)
                _statusText.text = $"Summarized {older.Count} msgs in {Time.unscaledTime - _compactSummaryStartTime:F0}s";
        };

        // Deadline scales with what the model must prefill (~400 transcript chars/sec
        // as a conservative local-model floor). A ~480KB transcript (the measured
        // ~6-minute llama.cpp case) gets ~25 minutes; small compacts keep a 5-minute
        // floor. Purely a safety net - real transport errors arrive via onDone.
        float watchdogSeconds = Mathf.Clamp(
            COMPACT_TIMEOUT_MIN_SECONDS + transcript.Length / 400f,
            COMPACT_TIMEOUT_MIN_SECONDS, COMPACT_TIMEOUT_MAX_SECONDS);
        watchdog = StartCoroutine(CompactSummaryWatchdog(watchdogSeconds, () => done, release));
        // No explicit output cap, same as the main chat turn: a 180-turn recap can
        // legitimately run long, and thinking models also
        // burn part of the budget inside <think> before any visible summary appears.
        SkillActionExecutor.DispatchOneShot(this, inst, lines, onDone, "CompactSummary", "compact_summary_sent.json",
            maxNewTokens: LLMRequestProfile.NoExplicitOutputTokenCap, onStreamChunk: onStreamChunk);
    }

    private IEnumerator CompactSummaryWatchdog(float timeoutSeconds, Func<bool> isDone, Action release)
    {
        yield return new WaitForSeconds(timeoutSeconds);
        if (isDone()) yield break;
        release();
        // Always-visible: with "Show debug stuff" off, a debug-gated Info bubble made
        // this failure look like the summarize silently did nothing.
        AddErrorBubble($"Compact timed out after {Mathf.RoundToInt(timeoutSeconds / 60f)} minute(s) - the LLM didn't return a summary in time. History is unchanged.");
    }

    private void OnCopyClicked()
    {
        // Build a plain-text transcript from the prompt manager's interaction history.
        // (Info / system bubbles aren't part of _interactions, so they don't get copied -
        // which is what we want; they're UI-only annotations like "New chat".)
        var sb = new StringBuilder();
        var lines = _promptManager.BuildPromptChat(usePromptCache: false);
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

    private void OnBubbleRightClicked(TMP_InputField field, GTPChatLine linkedInteraction, Vector2 screenPosition, Camera eventCamera)
    {
        if (!_isVisible || _mainPanel == null || field == null) return;

        HideBubbleContextMenu();
        HideRewindConfirmation();

        _bubbleContextMenuRoot = CreatePanelOverlay("AIChatBubbleContextMenu", new Color(0f, 0f, 0f, 0f), HideBubbleContextMenu);
        var menu = new GameObject("Menu");
        menu.transform.SetParent(_bubbleContextMenuRoot.transform, false);
        var menuRT = menu.AddComponent<RectTransform>();
        menuRT.anchorMin = new Vector2(0.5f, 0.5f);
        menuRT.anchorMax = new Vector2(0.5f, 0.5f);
        menuRT.pivot = new Vector2(0f, 1f);
        menuRT.sizeDelta = new Vector2(190f, 120f);

        var img = menu.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        var outline = menu.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.65f);
        outline.effectDistance = new Vector2(1f, -1f);

        var vlg = menu.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 4;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        bool hasSpeechSelection = TryGetSpeakSelectionForMenu(field, out string selectedSpeechText, out int speechSelStart, out int speechSelEnd);

        CreatePopupButton(menu.transform, "Select this box", true, () =>
        {
            HideBubbleContextMenu();
            SelectAllInField(field);
        });
        CreatePopupButton(menu.transform, "Speak", hasSpeechSelection, () =>
        {
            HideBubbleContextMenu();
            SpeakBubbleText(field, selectedSpeechText, speechSelStart, speechSelEnd);
        });
        CreatePopupButton(menu.transform, "Copy all to clipboard", true, () =>
        {
            HideBubbleContextMenu();
            CopyVisibleChatTranscriptToClipboard();
        });

        bool canRewind = IsRewindTarget(linkedInteraction) && CanRewindNow(out _)
            && FindInteractionIndex(_promptManager?.GetInteractionsList(), linkedInteraction) >= 0;
        CreatePopupButton(menu.transform, "Rewind to this spot", canRewind, () =>
        {
            HideBubbleContextMenu();
            ShowRewindConfirmation(linkedInteraction);
        });

        PositionPopup(menuRT, _bubbleContextMenuRoot.GetComponent<RectTransform>(), screenPosition, eventCamera);
    }

    private void OnEntryInputRightClicked(TMP_InputField field, Vector2 screenPosition, Camera eventCamera)
    {
        if (!_isVisible || _mainPanel == null || field == null) return;

        HideBubbleContextMenu();
        HideRewindConfirmation();

        _bubbleContextMenuRoot = CreatePanelOverlay("AIChatInputContextMenu", new Color(0f, 0f, 0f, 0f), HideBubbleContextMenu);
        var menu = new GameObject("Menu");
        menu.transform.SetParent(_bubbleContextMenuRoot.transform, false);
        var menuRT = menu.AddComponent<RectTransform>();
        menuRT.anchorMin = new Vector2(0.5f, 0.5f);
        menuRT.anchorMax = new Vector2(0.5f, 0.5f);
        menuRT.pivot = new Vector2(0f, 1f);
        menuRT.sizeDelta = new Vector2(150f, 64f);

        var img = menu.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        var outline = menu.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.65f);
        outline.effectDistance = new Vector2(1f, -1f);

        var vlg = menu.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 4;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        bool hasSpeechSelection = TryGetSpeakSelectionForMenu(field, out string selectedSpeechText, out int speechSelStart, out int speechSelEnd);

        CreatePopupButton(menu.transform, "Select all", true, () =>
        {
            HideBubbleContextMenu();
            SelectAllInField(field);
        });
        CreatePopupButton(menu.transform, "Speak", hasSpeechSelection, () =>
        {
            HideBubbleContextMenu();
            SpeakBubbleText(field, selectedSpeechText, speechSelStart, speechSelEnd);
        });

        PositionPopup(menuRT, _bubbleContextMenuRoot.GetComponent<RectTransform>(), screenPosition, eventCamera);
    }

    private bool IsRewindTarget(GTPChatLine line)
    {
        return line != null && (line._role == "user" || line._role == "assistant");
    }

    private bool CanRewindNow(out string reason)
    {
        if (_promptManager == null)
        {
            reason = "Chat history is not ready";
            return false;
        }
        if (_isStreaming)
        {
            reason = "Wait for the current reply to finish before rewinding";
            return false;
        }
        if (_waitingForForcedMainLLM)
        {
            reason = "Wait for the selected LLM to become available before rewinding";
            return false;
        }
        if (_compactSummaryInFlight)
        {
            reason = "Wait for the compact-summary request to finish before rewinding";
            return false;
        }
        if (HasPendingSidecarWork())
        {
            reason = "Wait for pending caption/inspection jobs before rewinding";
            return false;
        }

        reason = "";
        return true;
    }

    private static int FindInteractionIndex(List<GTPChatLine> all, GTPChatLine target)
    {
        if (all == null || target == null) return -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], target))
                return i;
        }
        return -1;
    }

    private int CountVisibleInteractionsAfter(List<GTPChatLine> all, int targetIndex)
    {
        if (all == null || targetIndex < 0) return 0;
        int count = 0;
        for (int i = targetIndex + 1; i < all.Count; i++)
        {
            var line = all[i];
            if (line != null && (line._role == "user" || line._role == "assistant"))
                count++;
        }
        return count;
    }

    private void ShowRewindConfirmation(GTPChatLine target)
    {
        HideRewindConfirmation();

        if (!IsRewindTarget(target))
            return;
        if (!CanRewindNow(out string reason))
        {
            RTQuickMessageManager.Get().ShowMessage(reason);
            return;
        }

        var all = _promptManager.GetInteractionsList();
        int targetIndex = FindInteractionIndex(all, target);
        if (targetIndex < 0)
        {
            RTQuickMessageManager.Get().ShowMessage("That chat message is no longer in history");
            return;
        }

        int removedMessages = CountVisibleInteractionsAfter(all, targetIndex);
        int keepMedia = GetInteractionMediaCheckpoint(target);
        int removedMedia = Mathf.Max(0, GetCurrentChatImageCount() - keepMedia);

        _rewindConfirmRoot = CreatePanelOverlay("AIChatRewindConfirm", new Color(0f, 0f, 0f, 0.32f), HideRewindConfirmation);

        var panel = new GameObject("Dialog");
        panel.transform.SetParent(_rewindConfirmRoot.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(430f, 190f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.14f, 0.98f);
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.55f, 0.95f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        CreateDialogText(panel.transform, "Title", "Rewind chat?", 20f, FontStyles.Bold,
            new Rect(18f, -16f, -36f, 30f), new Color(0.45f, 0.68f, 1f, 1f), TextAlignmentOptions.Center);

        string body =
            $"This will keep the selected bubble and remove {removedMessages} later chat message{(removedMessages == 1 ? "" : "s")} " +
            $"and {removedMedia} later media item{(removedMedia == 1 ? "" : "s")} from AI Chat.\n\n" +
            "World images stay in the workspace.";
        CreateDialogText(panel.transform, "Body", body, 14f, FontStyles.Normal,
            new Rect(22f, -52f, -44f, 82f), new Color(0.9f, 0.9f, 0.92f, 1f), TextAlignmentOptions.TopLeft);

        CreateDialogButton(panel.transform, "Rewind", new Vector2(-62f, 18f), new Vector2(110f, 32f),
            new Color(0.62f, 0.18f, 0.18f, 1f), () =>
            {
                HideRewindConfirmation();
                RewindToInteraction(target);
            });
        CreateDialogButton(panel.transform, "Cancel", new Vector2(62f, 18f), new Vector2(110f, 32f),
            new Color(0.26f, 0.36f, 0.48f, 1f), HideRewindConfirmation);
    }

    private void RewindToInteraction(GTPChatLine target)
    {
        if (!IsRewindTarget(target)) return;
        if (!CanRewindNow(out string reason))
        {
            RTQuickMessageManager.Get().ShowMessage(reason);
            return;
        }

        var all = _promptManager.GetInteractionsList();
        int targetIndex = FindInteractionIndex(all, target);
        if (targetIndex < 0)
        {
            RTQuickMessageManager.Get().ShowMessage("That chat message is no longer in history");
            return;
        }

        int removedMessages = CountVisibleInteractionsAfter(all, targetIndex);
        int removedMedia = RemoveMediaAfterCheckpoint(GetInteractionMediaCheckpoint(target));

        _autoContinueRemaining = 0;
        if (_autoContinueToggle != null) _autoContinueToggle.isOn = false;
        CancelSkillLoadAutoResume();
        CancelGenericContinue();
        _consecutiveSelfContinues = 0;
        _compactSummaryCancel?.Invoke();
        _lastTurnAttachments?.Clear();
        _infoMessages.Clear();
        _actionParser?.Reset();
        ResetPerTurnExecutionState();

        // No autoload-skill bookkeeping needed: liveness is re-derived from the kept
        // history on the next send, so bodies trimmed away simply re-trigger later.
        var kept = all.GetRange(0, targetIndex + 1);
        _promptManager.ReplaceInteractions(kept);
        var finalKept = _promptManager.GetInteractionsList();
        PruneMediaCheckpointsTo(finalKept);

        RebuildChatBubblesFromHistory(target);
        AddSystemMessage($"Rewound: removed {removedMessages} later message{(removedMessages == 1 ? "" : "s")} and {removedMedia} later media item{(removedMedia == 1 ? "" : "s")}.", includeInLLMRecap: false);
        FocusInputDeferred();
    }

    private int RemoveMediaAfterCheckpoint(int keepCount)
    {
        int current = GetCurrentChatImageCount();
        keepCount = Mathf.Clamp(keepCount, 0, current);
        int removeCount = current - keepCount;
        if (removeCount <= 0) return 0;

        if (_mediaContent != null)
        {
            for (int i = _mediaContent.childCount - 1; i >= keepCount; i--)
            {
                var child = _mediaContent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        for (int i = keepCount; i < current; i++)
        {
            var pic = _chatImagePics[i];
            if (pic != null)
                _captionLabels.Remove(pic);
        }
        _chatImagePics.RemoveRange(keepCount, removeCount);
        if (_chatImageRecords.Count > keepCount)
            _chatImageRecords.RemoveRange(keepCount, Mathf.Min(removeCount, _chatImageRecords.Count - keepCount));

        PruneAnchorsToLiveChatImages();
        UpdateMediaHeader();
        return removeCount;
    }

    private void PruneAnchorsToLiveChatImages()
    {
        if (_anchors == null || _anchors.Count == 0) return;

        var deadNames = new List<string>();
        foreach (var kv in _anchors)
        {
            var pic = kv.Value;
            if (pic == null || pic.gameObject == null || _chatImagePics == null || !_chatImagePics.Contains(pic))
                deadNames.Add(kv.Key);
        }
        foreach (string name in deadNames)
            _anchors.Remove(name);
    }

    private GameObject CreatePanelOverlay(string name, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var root = new GameObject(name);
        root.transform.SetParent(_mainPanel, false);
        root.transform.SetAsLastSibling();
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = root.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        if (onClick != null)
        {
            var button = root.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
        }
        return root;
    }

    private void CreatePopupButton(Transform parent, string text, bool enabled, Action onClick)
    {
        var go = new GameObject(text.Replace(" ", "") + "Button");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 24f;
        le.preferredHeight = 24f;
        var img = go.AddComponent<Image>();
        img.color = enabled ? new Color(0.24f, 0.30f, 0.38f, 1f) : new Color(0.20f, 0.20f, 0.22f, 1f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.interactable = enabled;
        if (enabled && onClick != null)
            button.onClick.AddListener(() => onClick());

        var labelGo = new GameObject("Text");
        labelGo.transform.SetParent(go.transform, false);
        var labelRT = labelGo.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(-8f, 0f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = _font;
        label.fontSize = 13f;
        label.color = enabled ? Color.white : new Color(0.58f, 0.58f, 0.62f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
    }

    private void PositionPopup(RectTransform popup, RectTransform root, Vector2 screenPosition, Camera eventCamera)
    {
        if (popup == null || root == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPosition, eventCamera, out Vector2 local))
            local = Vector2.zero;

        Rect r = root.rect;
        Vector2 size = popup.sizeDelta;
        local.x = Mathf.Clamp(local.x, r.xMin + 4f, r.xMax - size.x - 4f);
        local.y = Mathf.Clamp(local.y, r.yMin + size.y + 4f, r.yMax - 4f);
        popup.anchoredPosition = local;
    }

    private void CreateDialogText(Transform parent, string name, string text, float fontSize, FontStyles style, Rect offsets, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(offsets.x, offsets.y - offsets.height);
        rt.offsetMax = new Vector2(offsets.width, offsets.y);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = _font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
    }

    private void CreateDialogButton(Transform parent, string text, Vector2 centerBottom, Vector2 size, Color color, Action onClick)
    {
        var go = new GameObject(text + "Button");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = centerBottom;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        if (onClick != null)
            button.onClick.AddListener(() => onClick());

        var labelGo = new GameObject("Text");
        labelGo.transform.SetParent(go.transform, false);
        var labelRT = labelGo.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = _font;
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }

    private void SelectAllInField(TMP_InputField field)
    {
        if (field == null) return;
        ClearSpeechSelectionOverlay();
        var es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(field.gameObject);
        field.ActivateInputField();
        field.Select();
        int len = (field.text ?? "").Length;
        field.selectionStringAnchorPosition = 0;
        field.selectionStringFocusPosition = len;
        field.ForceLabelUpdate();
        if (TryBuildSpeakableSelection(field, 0, len, out string selectedText, out int selStart, out int selEnd))
            CacheSpeakSelection(field, selectedText, selStart, selEnd);
    }

    private string BuildVisibleChatTranscript()
    {
        var sb = new StringBuilder();
        if (_chatContent == null) return "";

        for (int i = 0; i < _chatContent.childCount; i++)
        {
            var bubble = _chatContent.GetChild(i);
            if (bubble == null) continue;
            var input = bubble.GetComponentInChildren<TMP_InputField>(true);
            if (input == null) continue;

            string role = "";
            var labelTransform = bubble.Find("Label");
            if (labelTransform != null)
            {
                var label = labelTransform.GetComponent<TextMeshProUGUI>();
                if (label != null)
                    role = label.text ?? "";
            }

            string text = ReverseTmpDisplayEscapes(input.text ?? "");
            text = OpenAITextCompletionManager.RemoveTMPTagsFromString(text).Trim();
            if (string.IsNullOrEmpty(text)) continue;

            if (!string.IsNullOrWhiteSpace(role))
                sb.Append(role.Trim()).Append(": ");
            sb.AppendLine(text);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private void CopyVisibleChatTranscriptToClipboard()
    {
        string transcript = BuildVisibleChatTranscript();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            RTQuickMessageManager.Get().ShowMessage("Chat is empty - nothing to copy");
            return;
        }

        GUIUtility.systemCopyBuffer = transcript;
        RTQuickMessageManager.Get().ShowMessage("All text put on clipboard");
    }

    private bool TryGetSpeakSelectionForMenu(TMP_InputField field, out string selectedText, out int selStart, out int selEnd)
    {
        if (TryGetLiveSpeakSelection(field, out selectedText, out selStart, out selEnd))
        {
            CacheSpeakSelection(field, selectedText, selStart, selEnd);
            return true;
        }

        selectedText = "";
        selStart = 0;
        selEnd = 0;
        if (field == null || _cachedSpeakSelectionField != field)
            return false;

        string currentText = field.text ?? "";
        if (!string.Equals(_cachedSpeakSelectionFieldText, currentText, StringComparison.Ordinal))
        {
            ClearCachedSpeakSelection(field);
            return false;
        }

        if (_cachedSpeakSelectionEnd <= _cachedSpeakSelectionStart
            || _cachedSpeakSelectionStart < 0
            || _cachedSpeakSelectionEnd > currentText.Length
            || string.IsNullOrWhiteSpace(_cachedSpeakSelectionText))
        {
            ClearCachedSpeakSelection(field);
            return false;
        }

        selectedText = _cachedSpeakSelectionText;
        selStart = _cachedSpeakSelectionStart;
        selEnd = _cachedSpeakSelectionEnd;
        return true;
    }

    private void TrackBubbleSelection(TMP_InputField field)
    {
        if (TryGetLiveSpeakSelection(field, out string selectedText, out int selStart, out int selEnd))
            CacheSpeakSelection(field, selectedText, selStart, selEnd);
    }

    private void CacheSpeakSelection(TMP_InputField field, string selectedText, int selStart, int selEnd)
    {
        if (field == null || string.IsNullOrWhiteSpace(selectedText))
            return;

        _cachedSpeakSelectionField = field;
        _cachedSpeakSelectionFieldText = field.text ?? "";
        _cachedSpeakSelectionText = selectedText;
        _cachedSpeakSelectionStart = selStart;
        _cachedSpeakSelectionEnd = selEnd;
    }

    private void ClearCachedSpeakSelection(TMP_InputField field = null)
    {
        if (field != null && _cachedSpeakSelectionField != field)
            return;

        _cachedSpeakSelectionField = null;
        _cachedSpeakSelectionFieldText = null;
        _cachedSpeakSelectionText = null;
        _cachedSpeakSelectionStart = 0;
        _cachedSpeakSelectionEnd = 0;
    }

    private bool TryGetLiveSpeakSelection(TMP_InputField field, out string selectedText, out int selStart, out int selEnd)
    {
        selectedText = "";
        selStart = 0;
        selEnd = 0;
        if (field == null)
            return false;

        field.ForceLabelUpdate();

        if (TryBuildSpeakableSelection(field, field.selectionStringAnchorPosition, field.selectionStringFocusPosition,
                out selectedText, out selStart, out selEnd))
            return true;

        return TryBuildSpeakableSelection(field, field.selectionAnchorPosition, field.selectionFocusPosition,
            out selectedText, out selStart, out selEnd);
    }

    private bool TryBuildSpeakableSelection(TMP_InputField field, int rawStart, int rawEnd, out string selectedText, out int selStart, out int selEnd)
    {
        selectedText = "";
        selStart = 0;
        selEnd = 0;
        if (field == null)
            return false;

        string text = field.text ?? "";
        selStart = Mathf.Min(rawStart, rawEnd);
        selEnd = Mathf.Max(rawStart, rawEnd);
        selStart = Mathf.Clamp(selStart, 0, text.Length);
        selEnd = Mathf.Clamp(selEnd, 0, text.Length);
        if (selEnd <= selStart)
            return false;

        string selected = ReverseTmpDisplayEscapes(text.Substring(selStart, selEnd - selStart));
        selected = OpenAITextCompletionManager.RemoveTMPTagsFromString(selected).Trim();
        if (string.IsNullOrWhiteSpace(selected))
            return false;

        selectedText = selected;
        return true;
    }

    private void ShowSpeechSelectionOverlay(TMP_InputField field, int selectionStart, int selectionEnd)
    {
        ClearSpeechSelectionOverlay();

        if (field == null || field.textComponent == null || !field.gameObject.activeInHierarchy)
            return;

        string text = field.text ?? "";
        selectionStart = Mathf.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Mathf.Clamp(selectionEnd, 0, text.Length);
        if (selectionEnd <= selectionStart)
            return;

        TMP_Text tmp = field.textComponent;
        tmp.ForceMeshUpdate();
        TMP_TextInfo info = tmp.textInfo;
        if (info == null || info.characterCount <= 0)
            return;

        var lineBounds = new Dictionary<int, Vector4>();
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            int rawStart = ch.index;
            int rawEnd = rawStart + Mathf.Max(1, ch.stringLength);
            if (rawEnd <= selectionStart || rawStart >= selectionEnd)
                continue;

            if (rawStart >= 0 && rawStart < text.Length && (text[rawStart] == '\n' || text[rawStart] == '\r'))
                continue;

            int line = Mathf.Clamp(ch.lineNumber, 0, info.lineCount - 1);
            float xMin;
            float xMax;
            float yMin;
            float yMax;

            if (ch.isVisible)
            {
                xMin = ch.bottomLeft.x;
                xMax = ch.topRight.x;
                yMin = ch.bottomLeft.y;
                yMax = ch.topRight.y;
            }
            else
            {
                TMP_LineInfo lineInfo = info.lineInfo[line];
                xMin = ch.origin;
                xMax = ch.xAdvance;
                yMin = lineInfo.descender;
                yMax = lineInfo.ascender;
            }

            if (xMax <= xMin)
                xMax = xMin + 2f;
            if (yMax <= yMin)
                yMax = yMin + tmp.fontSize;

            if (lineBounds.TryGetValue(line, out Vector4 bounds))
            {
                bounds.x = Mathf.Min(bounds.x, xMin);
                bounds.y = Mathf.Min(bounds.y, yMin);
                bounds.z = Mathf.Max(bounds.z, xMax);
                bounds.w = Mathf.Max(bounds.w, yMax);
                lineBounds[line] = bounds;
            }
            else
            {
                lineBounds[line] = new Vector4(xMin, yMin, xMax, yMax);
            }
        }

        if (lineBounds.Count == 0)
            return;

        var root = new GameObject("SpeechSelectionOverlay");
        root.transform.SetParent(tmp.rectTransform, false);
        root.transform.SetAsLastSibling();
        var rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = tmp.rectTransform.pivot;
        rootRT.anchorMax = tmp.rectTransform.pivot;
        rootRT.pivot = tmp.rectTransform.pivot;
        rootRT.anchoredPosition = Vector2.zero;
        rootRT.sizeDelta = tmp.rectTransform.rect.size;
        _speechSelectionOverlayRoot = root;

        foreach (Vector4 bounds in lineBounds.Values)
        {
            float padX = 1.5f;
            float padY = 1f;
            float xMin = bounds.x - padX;
            float yMin = bounds.y - padY;
            float xMax = bounds.z + padX;
            float yMax = bounds.w + padY;

            var lineGo = new GameObject("Highlight");
            lineGo.transform.SetParent(root.transform, false);
            var rt = lineGo.AddComponent<RectTransform>();
            rt.anchorMin = rootRT.pivot;
            rt.anchorMax = rootRT.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            rt.sizeDelta = new Vector2(Mathf.Max(2f, xMax - xMin), Mathf.Max(2f, yMax - yMin));
            var img = lineGo.AddComponent<Image>();
            img.color = new Color(0.25f, 0.5f, 1f, 0.28f);
            img.raycastTarget = false;
        }
    }

    private void ClearSpeechSelectionOverlay()
    {
        if (_speechSelectionOverlayRoot != null)
            Destroy(_speechSelectionOverlayRoot);
        _speechSelectionOverlayRoot = null;
    }

    private void SpeakBubbleText(TMP_InputField field, string selectedText, int selectionStart, int selectionEnd)
    {
        selectedText = (selectedText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            RTQuickMessageManager.Get().ShowMessage("Highlight text first, then choose Speak");
            return;
        }

        if (!ElevenLabsTextToSpeechManager.CanSpeakConfigured(out string reason))
        {
            AddSystemMessage(reason, includeInLLMRecap: false);
            return;
        }

        RestoreBubbleSelection(field, selectionStart, selectionEnd);
        ShowSpeechSelectionOverlay(field, selectionStart, selectionEnd);
        CacheSpeakSelection(field, selectedText, selectionStart, selectionEnd);
        StartCoroutine(RestoreBubbleSelectionAfterMenuClick(field, selectionStart, selectionEnd));

        ElevenLabsTextToSpeechManager.SpeakConfigured(selectedText, OnBubbleSpeakStatus);
    }

    private void RestoreBubbleSelection(TMP_InputField field, int selectionStart, int selectionEnd)
    {
        if (field == null || !field.gameObject.activeInHierarchy) return;
        string text = field.text ?? "";
        selectionStart = Mathf.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Mathf.Clamp(selectionEnd, 0, text.Length);
        if (selectionEnd <= selectionStart) return;

        var es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(field.gameObject);

        field.ActivateInputField();
        field.Select();
        field.selectionStringAnchorPosition = selectionStart;
        field.selectionStringFocusPosition = selectionEnd;
        field.ForceLabelUpdate();
    }

    private IEnumerator RestoreBubbleSelectionAfterMenuClick(TMP_InputField field, int selectionStart, int selectionEnd)
    {
        yield return null;
        RestoreBubbleSelection(field, selectionStart, selectionEnd);
        ShowSpeechSelectionOverlay(field, selectionStart, selectionEnd);
        yield return null;
        RestoreBubbleSelection(field, selectionStart, selectionEnd);
        ShowSpeechSelectionOverlay(field, selectionStart, selectionEnd);
    }

    private void OnBubbleSpeakStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return;

        if (status.StartsWith("Text To Speech failed", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSpeechControls("Error");
            ClearSpeechSelectionOverlay();
            AddSystemMessage(status, includeInLLMRecap: false);
        }
        else if (status.StartsWith("Requesting", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSpeechControls("Requesting...");
            RTQuickMessageManager.Get().ShowMessage(status);
        }
        else if (status.StartsWith("Speaking", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSpeechControls("Speaking");
            RTQuickMessageManager.Get().ShowMessage(status);
        }
        else if (status.StartsWith("Speech finished", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSpeechControls("Finished");
            ClearSpeechSelectionOverlay();
        }
        else if (status.StartsWith("Speech stopped", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSpeechControls("Stopped");
            ClearSpeechSelectionOverlay();
            RTQuickMessageManager.Get().ShowMessage(status);
        }
        else
        {
            UpdateSpeechControls(status);
            RTQuickMessageManager.Get().ShowMessage(status);
        }
    }

    private void HideBubbleContextMenu()
    {
        if (_bubbleContextMenuRoot != null)
            Destroy(_bubbleContextMenuRoot);
        _bubbleContextMenuRoot = null;
    }

    private void HideRewindConfirmation()
    {
        if (_rewindConfirmRoot != null)
            Destroy(_rewindConfirmRoot);
        _rewindConfirmRoot = null;
    }

    private void TryCancelActiveRequests()
    {
        if (_openAIMgr != null && _openAIMgr.IsRequestActive()) _openAIMgr.CancelCurrentRequest();
        if (_anthropicMgr != null && _anthropicMgr.IsRequestActive()) _anthropicMgr.CancelCurrentRequest();
        if (_texGenMgr != null && _texGenMgr.IsRequestActive()) _texGenMgr.CancelCurrentRequest();
        if (_geminiMgr != null && _geminiMgr.IsRequestActive()) _geminiMgr.CancelCurrentRequest();
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

    private void SendChatTurn(string latestUserMessage = null, int queuedForcedMainLLMId = QUEUED_MAIN_LLM_OVERRIDE_UNSET)
    {
        bool isQueuedForcedDispatch = queuedForcedMainLLMId != QUEUED_MAIN_LLM_OVERRIDE_UNSET;
        if (!isQueuedForcedDispatch)
        {
            CancelInspectAutoResume();
            CancelSkillLoadAutoResume();
            CancelGenericContinue();
            _chatTurnEpoch++;
        }

        var settingsMgr = LLMSettingsManager.Get();
        if (settingsMgr == null)
        {
            AddSystemMessage("LLM settings are not initialized yet. Open LLM Settings and configure a provider first.", includeInLLMRecap: false);
            return;
        }

        var instanceMgr = LLMInstanceManager.Get();
        int llmReplicaIndex = 0;
        bool isVisionJob = _promptManager != null && _promptManager.HasAnyImages();
        int requestedMainLLMOverrideID = isQueuedForcedDispatch ? queuedForcedMainLLMId : GetMainLLMOverrideInstanceID();
        int llmInstanceID = -1;

        if (requestedMainLLMOverrideID != MAIN_LLM_DEFAULT_ID && instanceMgr != null)
        {
            var forcedInstance = instanceMgr.GetInstance(requestedMainLLMOverrideID);
            if (!IsSelectableMainLLMInstance(forcedInstance))
            {
                SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);
                RefreshMainLLMDropdownOptions();
                AddSystemMessage("Main LLM override was reset to Default because the selected LLM is no longer active.", includeInLLMRecap: false);
            }
            else if (isVisionJob && !forcedInstance.supportsVision)
            {
                AddSystemMessage(
                    $"Main LLM override is set to {BuildMainLLMOptionText(forcedInstance)}, but this chat contains raw image data and that LLM is not marked Supports vision.",
                    includeInLLMRecap: false);
                return;
            }
            else if (TryFindFreeReplica(forcedInstance, out llmReplicaIndex))
            {
                llmInstanceID = forcedInstance.instanceID;
            }
            else
            {
                StartWaitingForForcedMainLLM(forcedInstance, latestUserMessage);
                return;
            }
        }

        if (llmInstanceID < 0)
        {
            // The main chat turn is a BIG job - it carries the full system prompt plus
            // the whole conversation - so BigJobsOnly instances accept it and
            // SmallJobsOnly instances (meant for caption/delegation one-shots) don't
            // steal it. This also keeps the chat on one instance when the user splits
            // roles via job modes, which is what lets that server's prompt cache work.
            llmInstanceID = instanceMgr?.GetFreeLLM(isSmallJob: false, isVisionJob: isVisionJob, out llmReplicaIndex) ?? -1;

            if (llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
            {
                llmInstanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: isVisionJob, out llmReplicaIndex);
            }
        }

        // Reset the streaming-action parser for this turn (counters + buffer state).
        _actionParser?.Reset();

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
            _promptManager.SetBaseSystemPrompt(_contextBuilder.Build(GetKeepOldToolCallsInPrompt(), HiddenSkillIdsForPrompt()));
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
        string activeLLMName = llmInstance != null ? BuildMainLLMOptionText(llmInstance) : "LLM";
        SetBusyUI(true, $"{StreamSpinnerFrames[0]} Talking to {activeLLMName}...");

        AddAssistantBubble("");

        var lines = _promptManager.BuildPromptChat();
        // Strip TMP markup from any prior assistant bubbles before sending (safety - the
        // GPTPromptManager only ever stores raw text we put in, but be defensive).
        lines = OpenAITextCompletionManager.RemoveTMPTags(lines);
        if (!GetKeepOldToolCallsInPrompt())
            StripOldToolCallsFromOutgoingLines(lines);

        // Tack the volatile CURRENT STATE block (GPU busy/idle, chat-image provenance/captions)
        // onto the outgoing copy of the latest user message - the request tail, where
        // churn is cheap.
        AppendCurrentStateToOutgoingLines(lines);

        // Remember the exact text we are about to send for each cloned history line.
        // On future turns BuildPromptChat reuses those bytes so the server can reuse
        // its KV cache through prior user/assistant turns. If the user edits a bubble,
        // the line's visible _content changes and this cached prompt text is ignored.
        _promptManager?.RememberPromptContentFromClones(lines);

        // Send raw attached image bytes only on the turn where the user attached them.
        // BuildPromptChat/RemoveTMPTags returned cloned GTPChatLine objects above, so
        // the current request still carries the image_url payloads; clearing the live
        // prompt history here prevents every future turn from resending old base64.
        _promptManager?.ClearImagesFromInteractions();

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

                string json = _openAIMgr.BuildChatCompleteJSON(lines, LLMRequestProfile.NoExplicitOutputTokenCap, temperature, model, true,
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

                string json = _anthropicMgr.BuildChatCompleteJSON(lines, LLMRequestProfile.GetAnthropicMaxOutputTokens(model), temperature, model, true);
                _anthropicMgr.SpawnChatCompletionRequest(json, OnLLMCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true);
                break;
            }

            case LLMProvider.LlamaCpp:
            {
                string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                string apiKey = activeSettings.apiKey;
                var llmParms = GetAIChatLLMParms(settingsMgr, llmInstance, llmInstanceID, LLMProvider.LlamaCpp, activeSettings);
                string suggestedEndpoint;
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, LLMRequestProfile.NoExplicitOutputTokenCap, temperature,
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
                string json = _texGenMgr.BuildForInstructJSON(lines, out suggestedEndpoint, LLMRequestProfile.NoExplicitOutputTokenCap, temperature,
                    Config.Get().GetGenericLLMMode(), true, llmParms, true, false);
                _texGenMgr.SpawnChatCompleteRequest(json, OnLLMCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey);
                break;
            }

            case LLMProvider.Gemini:
            {
                string apiKey = activeSettings.apiKey;
                string model = activeSettings.selectedModel;
                if (string.IsNullOrEmpty(model))
                {
                    AddErrorBubble("No model selected for this Gemini LLM instance - choose one in LLM Settings.");
                    FinalizeAssistantTurn(aborted: true);
                    return;
                }
                string baseEndpoint = string.IsNullOrEmpty(activeSettings.endpoint)
                    ? "https://generativelanguage.googleapis.com/v1beta/models" : activeSettings.endpoint;
                bool enableThinking = activeSettings.enableThinking;
                string endpoint = GeminiTextCompletionManager.BuildEndpointUrl(baseEndpoint, model, true);

                if (!HasUserMessage(lines))
                    lines.Enqueue(new GTPChatLine("user", "Please proceed."));

                string json = _geminiMgr.BuildChatCompleteJSON(lines, LLMRequestProfile.NoExplicitOutputTokenCap, temperature, model, true, enableThinking);
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
                float compatTemperature = activeSettings.overrideTemperature
                    ? activeSettings.temperature
                    : (isDeepSeek ? LLMRequestProfile.GetRecommendedTemperature(model, compatReasoningEffort, temperature) : temperature);
                float? compatTopP = activeSettings.overrideTopP
                    ? (float?)activeSettings.topP
                    : (isDeepSeek ? (float?)LLMRequestProfile.GetRecommendedTopP(model, compatReasoningEffort, 1.0f) : null);
                int? compatTopK = activeSettings.overrideTopK ? (int?)activeSettings.topK : null;
                float? compatMinP = activeSettings.overrideMinP ? (float?)activeSettings.minP : null;
                float? compatRepPenalty = activeSettings.overrideRepeatPenalty ? (float?)activeSettings.repeatPenalty : null;
                float? compatPresencePenalty = activeSettings.overridePresencePenalty ? (float?)activeSettings.presencePenalty : null;
                float? compatFrequencyPenalty = activeSettings.overrideFrequencyPenalty ? (float?)activeSettings.frequencyPenalty : null;
                int? compatRepeatLastN = activeSettings.overrideRepeatLastN ? (int?)activeSettings.repeatLastN : null;
                string compatReasoningEffortParam = isDeepSeek ? LLMReasoningEffortUtil.ToConfigValue(compatReasoningEffort) : null;
                string json = _openAIMgr.BuildChatCompleteJSON(normalizedLines, LLMRequestProfile.NoExplicitOutputTokenCap, compatTemperature, model, true,
                    enableThinking: compatEnableThinking,
                    topP: compatTopP, topK: compatTopK, minP: compatMinP, repetitionPenalty: compatRepPenalty,
                    frequencyPenalty: compatFrequencyPenalty, presencePenalty: compatPresencePenalty, repeatLastN: compatRepeatLastN,
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

    // ---------- keyword-autoloaded skill bodies ----------

    /// <summary>
    /// Queue the full body of each newly keyword-triggered autoload skill as an
    /// info-recap message. Called from the send paths BEFORE
    /// BuildLLMPayloadWithInfoRecap so the body folds into THIS turn's outgoing user
    /// message and the model can use it immediately. Delivery through the recap tail
    /// (the same path read_skill bodies use) is deliberate: these used to be a
    /// system-role interaction, but BuildPromptChat folds those into the FRONT system
    /// message, and growing the prompt head mid-conversation invalidated the server's
    /// prompt cache for the entire history every time a new skill triggered - a ~40s
    /// full re-prefill on a long llama.cpp chat. The recap rides the request tail, so
    /// the cached prefix survives and the body persists in that user line's history
    /// for the rest of the chat.
    /// </summary>
    private void QueueTriggeredSkillBodyInjections(string outgoingUserText)
    {
        if (_skillManager == null || _promptManager == null || string.IsNullOrWhiteSpace(outgoingUserText))
            return;

        var matched = _skillManager.GetAutoloadSkillsForMessage(outgoingUserText, HiddenSkillIdsForPrompt());
        if (ShouldAutoloadVideoToVideoForMovieContext(outgoingUserText))
        {
            var videoSkill = _skillManager.GetById(BuiltInSkillIds.VideoToVideo);
            bool alreadyMatched = false;
            foreach (var skill in matched)
            {
                if (skill != null && string.Equals(skill.Id, BuiltInSkillIds.VideoToVideo, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyMatched = true;
                    break;
                }
            }
            if (!alreadyMatched && videoSkill != null)
                matched.Add(videoSkill);
        }
        if (matched == null || matched.Count == 0)
            return;

        var live = ComputeLiveAutoloadSkillIds();
        var loadedIds = new List<string>();
        foreach (var skill in matched)
        {
            if (skill == null || string.IsNullOrEmpty(skill.Id) || live.Contains(skill.Id))
                continue;

            QueueSkillBodyInjection(skill);
            loadedIds.Add(skill.Id);
        }

        if (loadedIds.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.Append("AI Chat skill references changed: loaded ");
        AppendQuotedSkillIdList(sb, loadedIds);
        sb.Append(".");
        AddSystemMessage(sb.ToString(), includeInLLMRecap: false);
        Debug.Log("AIChatPanel: auto-loaded skill bodies attached to this turn: " + string.Join(", ", loadedIds));
    }

    private bool ShouldAutoloadVideoToVideoForMovieContext(string outgoingUserText)
    {
        if (string.IsNullOrWhiteSpace(outgoingUserText) || !NewestLiveChatImageIsMovie())
            return false;

        foreach (string phrase in MovieContextVideoEditPhrases)
        {
            if (outgoingUserText.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return MovieContextEditRx.IsMatch(outgoingUserText);
    }

    private bool NewestLiveChatImageIsMovie()
    {
        if (_chatImagePics == null)
            return false;

        for (int i = _chatImagePics.Count - 1; i >= 0; i--)
        {
            var pic = _chatImagePics[i];
            if (pic == null || pic.gameObject == null)
                continue;
            return pic.IsMovie();
        }

        return false;
    }

    /// <summary>
    /// Queue one skill's full body as an info-recap message (rides the tail of the
    /// next outgoing user message) and record the delivered text for reload diffing.
    /// Shared by keyword autoload and the compact-summary restore path.
    /// </summary>
    private void QueueSkillBodyInjection(Skill skill)
    {
        if (skill == null || string.IsNullOrEmpty(skill.Id))
            return;
        string body = SkillManager.ApplyPresetPrefix(skill.RawMarkdown ?? "");
        _infoMessages.Add(new InfoMessage(
            AutoloadSkillBodyMarkerPrefix + skill.Id + "' (full body of aichat/skills/" +
            skill.Id + ".md, auto-loaded because its trigger or the current media context matched). " +
            "Use this knowledge directly in this and later replies; " +
            "do NOT call read_skill for this id.\n\n" + body));
        _sentAutoloadSkillBodies[skill.Id] = body;
    }

    /// <summary>
    /// A skill counts as live when a full-body copy is already reachable by the model:
    /// baked into a user line's LLM payload on an earlier turn (keyword autoload or
    /// read_skill), or queued in a not-yet-sent info message. Derived by substring
    /// scan instead of a stored id list so history changes (Rewind, Compact, Clear,
    /// bubble edits) can never leave the tracking stale - a body that fell out of
    /// history simply re-triggers on the next keyword hit.
    /// </summary>
    private HashSet<string> ComputeLiveAutoloadSkillIds()
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skills = _skillManager != null ? _skillManager.GetSkills() : null;
        if (skills == null || skills.Count == 0 || _promptManager == null)
            return live;

        var texts = new List<string>();
        foreach (var line in _promptManager.GetInteractionsList())
        {
            if (line != null && line._role == "user" && !string.IsNullOrEmpty(line._content))
                texts.Add(line._content);
        }
        foreach (var msg in _infoMessages)
        {
            if (msg != null && !msg.m_alreadySentToLLM && !string.IsNullOrEmpty(msg.m_text))
                texts.Add(msg.m_text);
        }
        if (texts.Count == 0)
            return live;

        foreach (var skill in skills)
        {
            if (skill == null || string.IsNullOrEmpty(skill.Id))
                continue;
            string autoloadMarker = AutoloadSkillBodyMarkerPrefix + skill.Id + "'";
            string readSkillMarker = ReadSkillBodyMarkerPrefix + skill.Id + "'";
            foreach (string text in texts)
            {
                if (text.IndexOf(autoloadMarker, StringComparison.Ordinal) >= 0 ||
                    text.IndexOf(readSkillMarker, StringComparison.Ordinal) >= 0)
                {
                    live.Add(skill.Id);
                    break;
                }
            }
        }
        return live;
    }

    /// <summary>
    /// After a skill-file reload, re-send bodies whose files genuinely changed so the
    /// model stops following a stale copy. The update rides the info-recap tail of the
    /// NEXT outgoing message (append-only, prompt-cache safe) instead of rewriting the
    /// earlier copy in place; unchanged bodies send nothing. Ids that are live via a
    /// copy this panel never recorded (rediscovered after Rewind, or loaded by
    /// read_skill) are baselined against the current file so FUTURE edits diff
    /// correctly.
    /// </summary>
    private void QueueUpdatedSkillBodiesAfterReload()
    {
        if (_skillManager == null || _promptManager == null)
            return;

        var live = ComputeLiveAutoloadSkillIds();
        if (live.Count == 0)
            return;

        var updatedIds = new List<string>();
        foreach (string id in live)
        {
            var skill = _skillManager.GetById(id);
            if (skill == null)
            {
                // Skill file vanished. The copy already baked into history is
                // harmless; drop only the edit-tracking entry.
                _sentAutoloadSkillBodies.Remove(id);
                continue;
            }

            string body = SkillManager.ApplyPresetPrefix(skill.RawMarkdown ?? "");
            if (!_sentAutoloadSkillBodies.TryGetValue(id, out string sentBody))
            {
                _sentAutoloadSkillBodies[id] = body;
                continue;
            }
            if (string.Equals(sentBody, body, StringComparison.Ordinal))
                continue;

            _infoMessages.Add(new InfoMessage(
                AutoloadSkillBodyMarkerPrefix + id + "' (updated copy - aichat/skills/" + id +
                ".md changed on disk; this supersedes any earlier copy of this skill above).\n\n" + body));
            _sentAutoloadSkillBodies[id] = body;
            updatedIds.Add(id);
        }

        if (updatedIds.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.Append("AI Chat skill references changed: updated ");
        AppendQuotedSkillIdList(sb, updatedIds);
        sb.Append(" (revised body rides the next message).");
        AddSystemMessage(sb.ToString(), includeInLLMRecap: false);
        Debug.Log("AIChatPanel: queued updated skill bodies: " + string.Join(", ", updatedIds));
    }

    /// <summary>
    /// Append the volatile CURRENT STATE block (GPU busy/idle, chat-image list with
    /// captions) to the last user line of the outgoing request. Operates on the
    /// clones BuildPromptChat/RemoveTMPTags returned; the final sent clone text is
    /// remembered separately as prompt-cache content after this method runs. Visible
    /// stored history stays clean/editable, while future unedited turns can still
    /// byte-match the exact previous request. If no user line exists (shouldn't
    /// happen - sends always follow a user message) the block is simply skipped;
    /// it's advisory context, not required for a valid request.
    /// </summary>
    private void AppendCurrentStateToOutgoingLines(Queue<GTPChatLine> lines)
    {
        if (_contextBuilder == null || lines == null) return;

        GTPChatLine lastUser = null;
        foreach (var line in lines)
            if (line != null && line._role == "user") lastUser = line;
        if (lastUser == null) return;

        int chatImageSlots = _chatImagePics != null ? _chatImagePics.Count : 0;
        int imageContextLimit = GetImageContextLimit();
        var chatImages = BuildChatImageStatesForPrompt(imageContextLimit);
        string anchorsLine = BuildAnchorsStateLine();
        string state = _contextBuilder.BuildCurrentStateBlock(chatImageSlots, chatImages, anchorsLine, imageContextLimit, GetWebEnabled());
        if (string.IsNullOrEmpty(state)) return;

        lastUser._content = (lastUser._content ?? "") + "\n\n" + state;
    }

    private static readonly Regex AIToolsPairedActionRx = new Regex(
        @"<aitools_action\b[^>]*>(?:[\s\S]*?)</aitools_action\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AIToolsSelfClosingActionRx = new Regex(
        @"<aitools_action\b[^>]*?/?\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static void StripOldToolCallsFromOutgoingLines(Queue<GTPChatLine> lines)
    {
        if (lines == null) return;
        foreach (var line in lines)
        {
            if (line == null || line._role != "assistant") continue;
            line._content = StripActionTagsForPrompt(line._content);
        }
    }

    private static string StripActionTagsForPrompt(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = ReverseTmpDisplayEscapes(text);
        bool hadAction = text.IndexOf("<aitools_action", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hadAction) return text.Trim();

        text = AIToolsPairedActionRx.Replace(text, "");
        text = AIToolsSelfClosingActionRx.Replace(text, "");
        text = Regex.Replace(text, @"[ \t]+\r?\n", "\n");
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n").Trim();
        return string.IsNullOrEmpty(text)
            ? "(assistant used app tools; see CHAT IMAGES for resulting media/provenance.)"
            : text;
    }

    private List<ChatImageState> BuildChatImageStatesForPrompt(int imageContextLimit)
    {
        int count = _chatImagePics != null ? _chatImagePics.Count : 0;
        int listedCount = imageContextLimit <= 0 ? 0 : Mathf.Min(count, imageContextLimit);
        int start = Mathf.Max(0, count - listedCount);
        var states = new List<ChatImageState>(listedCount);
        bool includeGeneratedCaptions = GetAutoCaptionGeneratedImages();

        for (int i = start; i < count; i++)
        {
            PicMain pic = _chatImagePics[i];
            ChatImageRecord record = (_chatImageRecords != null && i < _chatImageRecords.Count)
                ? _chatImageRecords[i]
                : null;
            bool reusable = pic != null && pic.gameObject != null;
            bool userAttachment = record != null && record.isUserAttachment;
            bool alwaysCaption = record != null && record.alwaysIncludeCaption;
            string caption = (userAttachment || alwaysCaption || includeGeneratedCaptions)
                ? ((IChatHost)this).GetChatImageCaption(i + 1)
                : "";

            string dimensions = record != null ? record.dimensions : "";
            if (string.IsNullOrEmpty(dimensions) && reusable && pic.TryGetCurrentTexture(out var tex) && tex != null)
                dimensions = tex.width + "x" + tex.height;

            // Reflect the Pic's CURRENT state, not just what it was when the bubble was
            // created: a Pic that started as an image and later had a video rendered into it
            // (image_to_movie / video_to_video) is now a movie even if the record still says
            // "image". Without this the LLM can't tell which bubbles are clips, so it can't
            // pick a video_to_video source and re-generates new clips instead.
            bool isMovie = (record != null && record.isMovie) || (reusable && pic.IsMovie());

            states.Add(new ChatImageState
            {
                Index = i + 1,
                IsUserAttachment = userAttachment,
                IsMovie = isMovie,
                IsAudio = record != null && record.isAudio,
                IsReusable = reusable,
                IncludeCaption = !string.IsNullOrEmpty(caption),
                Kind = (record != null && record.isAudio && !string.IsNullOrEmpty(record.kind))
                    ? record.kind
                    : isMovie
                    ? "movie"
                    : (record != null && !string.IsNullOrEmpty(record.kind)
                        ? record.kind
                        : (userAttachment ? "user attachment" : "generated image")),
                AnchorName = record != null ? record.anchorName : null,
                Dimensions = dimensions,
                Caption = caption,
                Provenance = record != null ? BuildRecordProvenance(record) : "",
                HasCleanBase = record != null && record.cleanBasePngBytes != null && record.cleanBasePngBytes.Length > 0,
                // A still-working job queue means the visible pixels are a source frame
                // or placeholder, not the result - tell the model so it waits instead of
                // re-queuing a duplicate render (the "you didn't make a movie" trap).
                IsBusy = reusable && pic.IsBusy()
            });
        }

        return states;
    }

    private static string BuildRecordProvenance(ChatImageRecord record)
    {
        if (record == null || record.provenanceSteps == null || record.provenanceSteps.Count == 0)
            return "";
        return "provenance: " + string.Join(" -> ", record.provenanceSteps);
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
            string error = BuildLLMErrorDetail(db);
            if (string.IsNullOrEmpty(error))
            {
                string status = db != null ? db.GetStringWithDefault("status", "") : "";
                error = status == "success"
                    ? "LLM returned an empty response. Check text_completion_sent.json and textgen_json_received.json for the raw exchange."
                    : "Unknown error";
            }
            AddErrorBubble("LLM error: " + error);
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
        var assistantInteraction = _promptManager.GetLastInteraction();
        assistantInteraction?.RememberDisplayContent(visibleText);
        MarkInteractionMediaCheckpoint(assistantInteraction);

        // Now that we have an interaction to link the bubble to, switch the assistant
        // bubble from readOnly to editable so the user can hand-tweak the assistant's
        // reply for testing follow-up turns.
        EnableBubbleEditing(completedField, assistantInteraction);

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

        // Auto-repeat: if this turn finished cleanly and "Auto repeat msg" is still
        // checked with repeats left, schedule the next Send (the count is consumed
        // at fire time in FireAutoContinueNextFrame, not here). When the last repeat
        // has fired, uncheck the box so the burst ends visibly. Aborts (Stop /
        // errors) drain the counter so it doesn't resume on its own.
        bool willAutoContinue = false;
        bool repeatOn = _autoContinueToggle != null && _autoContinueToggle.isOn;
        bool inspectResumePendingForTurn = !aborted
            && _inspectAutoResumePending
            && _inspectAutoResumeTurnEpoch == _chatTurnEpoch;
        bool skillLoadResumePendingForTurn = !aborted
            && _skillLoadAutoResumePending
            && _skillLoadAutoResumeTurnEpoch == _chatTurnEpoch;
        bool genericContinuePendingForTurn = !aborted
            && _genericContinuePending
            && _genericContinueTurnEpoch == _chatTurnEpoch;
        bool explicitResumePendingForTurn = inspectResumePendingForTurn || skillLoadResumePendingForTurn
            || genericContinuePendingForTurn;
        if (aborted)
        {
            _autoContinueRemaining = 0;
            CancelSkillLoadAutoResume();
            CancelGenericContinue();
        }
        else if (!explicitResumePendingForTurn && _actionExecutor != null && _actionExecutor.TurnHadOnlyPreparatoryActions)
        {
            // Unfinished-plan safety net: the reply fetched/extracted/cut media ("First, let me
            // grab a frame, then generate the video...") and ended without the render it
            // described and without asking for a continue. Models skip that rule often
            // enough that the host grants one bounded continue itself; the generic scheduler
            // waits for the fetch/extract (sidecar work) to finish first, and the runaway cap
            // still applies. A reply that only wanted the clip costs one short "done" turn.
            RegisterGenericContinueRequest(_chatTurnEpoch);
            if (_genericContinuePending)
            {
                genericContinuePendingForTurn = true;
                explicitResumePendingForTurn = true;
                ((IChatHost)this).AddSystemInjectionSilent(
                    "(Automatic continue: your previous reply only prepared media (web fetch / frame extract / clip) and ended without the follow-up it described. " +
                    "If the user's request still needs a render or edit, emit that action NOW using the anchors / chat_image numbers in CHAT IMAGES; if the request is already complete, reply in one short sentence.)");
            }
        }
        else if (repeatOn && !explicitResumePendingForTurn && _autoContinueRemaining > 0 && !HasPendingSidecarWork())
        {
            willAutoContinue = true;
        }
        else if (repeatOn && !explicitResumePendingForTurn)
        {
            // Burst complete - auto-uncheck (its handler zeroes the counter).
            _autoContinueToggle.isOn = false;
        }

        // Keep the turn's final token/speed numbers on screen instead of snapping
        // straight to Idle - they used to vanish before the user could read them.
        string stats = BuildStreamStatsText();
        string doneStatus;
        if (aborted)
            doneStatus = string.IsNullOrEmpty(stats) ? "Stopped" : $"Stopped   {stats}";
        else if (willAutoContinue)
            doneStatus = $"Auto-repeat ({_autoContinueRemaining} left)";
        else if (inspectResumePendingForTurn)
            doneStatus = HasPendingSidecarWork() ? "Waiting for inspection" : "Continuing after inspection";
        else if (skillLoadResumePendingForTurn)
            doneStatus = "Continuing after skill load";
        else if (genericContinuePendingForTurn)
            doneStatus = "Continuing...";
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
            TryScheduleInspectAutoResume();
            TryScheduleSkillLoadAutoResume();
            TryScheduleGenericContinue();
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
        if (_waitingForForcedMainLLM) yield break;
        if (HasPendingSidecarWork()) yield break;
        if (_autoContinueRemaining <= 0) yield break;
        // Consume one repeat as we fire it, so N counts total sends regardless of
        // whether this fire came from checking the box or a finishing reply.
        _autoContinueRemaining--;
        // Reflect the live countdown in the N field.
        SetAutoRepeatCountField(_autoContinueRemaining);
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
        // _isStreaming / _waitingForForcedMainLLM are updated alongside this call
        // (see SendChatTurn / StartWaitingForForcedMainLLM / FinalizeAssistantTurn);
        // RecomputeSendInteractable also factors in any pending sidecar jobs.
        RecomputeSendInteractable();
        // Keep the input field interactable while the LLM is streaming so the user (a)
        // doesn't lose focus / their composed-but-not-sent text and (b) can compose the
        // next message while reading the in-progress reply. The _isStreaming guard in
        // OnSendClicked still prevents double-send.
        if (_inputField != null) _inputField.interactable = true;
        if (_clearButton != null) _clearButton.interactable = true;
        if (_stopButton != null) _stopButton.interactable = busy || _waitingForForcedMainLLM || CountPendingInspectImageJobs() > 0 || HasSkillLoadAutoResumePendingForCurrentTurn() || HasGenericContinuePendingForCurrentTurn();
        if (_statusText != null) _statusText.text = status;
    }

    private void OnSpeechStopClicked()
    {
        ElevenLabsTextToSpeechManager.StopConfiguredSpeech("Speech stopped.");
        ClearSpeechSelectionOverlay();
        UpdateSpeechControls("Stopped");
    }

    private void UpdateSpeechControls(string status = null)
    {
        bool active = ElevenLabsTextToSpeechManager.IsConfiguredSpeechActive();
        if (!active && status == null && _speechSelectionOverlayRoot != null)
            ClearSpeechSelectionOverlay();

        if (_speechStopButton != null)
        {
            _speechStopButton.gameObject.SetActive(active);
            _speechStopButton.interactable = active;
        }

        if (_speechStatusText == null)
            return;

        if (!string.IsNullOrWhiteSpace(status))
        {
            _speechStatusText.text = status;
            return;
        }

        if (!active && string.IsNullOrWhiteSpace(_speechStatusText.text))
            _speechStatusText.text = "Idle";
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

        _skillManager.Reload();

        // If a live skill's file was edited, queue the revised body so the model
        // stops following the stale copy (rides the next message's info recap).
        QueueUpdatedSkillBodiesAfterReload();
    }

    private void OnSettingsClicked()
    {
        AIChatSettingsPanel.Show(_skillManager, () =>
        {
            // Reload from disk so any user edits to prompt or skill files take
            // effect on the very next turn (rebuilt by ChatContextBuilder.Build()).
            _skillManager?.Reload();
            QueueUpdatedSkillBodiesAfterReload();
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

    private void SubscribeToLLMInstanceChanges()
    {
        var manager = LLMInstanceManager.Get();
        if (_subscribedInstanceManager == manager)
            return;

        UnsubscribeFromLLMInstanceChanges();
        _subscribedInstanceManager = manager;
        if (_subscribedInstanceManager != null)
            _subscribedInstanceManager.InstancesChanged += OnMainLLMInstancesChanged;
        RefreshMainLLMDropdownOptions();
    }

    private void UnsubscribeFromLLMInstanceChanges()
    {
        if (_subscribedInstanceManager != null)
            _subscribedInstanceManager.InstancesChanged -= OnMainLLMInstancesChanged;
        _subscribedInstanceManager = null;
    }

    private void OnMainLLMInstancesChanged()
    {
        RefreshMainLLMDropdownOptions();
        UpdateStatusPill();
    }

    private void RefreshMainLLMDropdownOptions()
    {
        if (_mainLLMDropdown == null) return;

        _mainLLMDropdownInstanceIds.Clear();
        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Default"));
        _mainLLMDropdownInstanceIds.Add(MAIN_LLM_DEFAULT_ID);

        int savedId = GetMainLLMOverrideInstanceID();
        int selectedIndex = 0;
        var manager = LLMInstanceManager.Get();
        if (manager != null)
        {
            var instances = manager.GetAllInstances();
            for (int i = 0; i < instances.Count; i++)
            {
                var inst = instances[i];
                if (!IsSelectableMainLLMInstance(inst))
                    continue;

                _mainLLMDropdownInstanceIds.Add(inst.instanceID);
                options.Add(new TMP_Dropdown.OptionData(BuildMainLLMOptionText(inst)));
                if (inst.instanceID == savedId)
                    selectedIndex = _mainLLMDropdownInstanceIds.Count - 1;
            }
        }

        if (savedId != MAIN_LLM_DEFAULT_ID && selectedIndex == 0)
            SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);

        _mainLLMDropdown.ClearOptions();
        _mainLLMDropdown.AddOptions(options);
        _mainLLMDropdown.SetValueWithoutNotify(selectedIndex);
        _mainLLMDropdown.RefreshShownValue();
        UpdateMainLLMCaptionOverlay(selectedIndex);
    }

    private void OnMainLLMDropdownChanged(int index)
    {
        if (index < 0 || index >= _mainLLMDropdownInstanceIds.Count)
            return;
        SetMainLLMOverrideInstanceID(_mainLLMDropdownInstanceIds[index]);
        UpdateMainLLMCaptionOverlay(index);
    }

    private void UpdateMainLLMCaptionOverlay(int index)
    {
        if (_mainLLMCaptionText == null || _mainLLMDropdown == null)
            return;

        string text = "Default";
        if (index >= 0 && index < _mainLLMDropdown.options.Count && _mainLLMDropdown.options[index] != null)
            text = _mainLLMDropdown.options[index].text;
        _mainLLMCaptionText.text = string.IsNullOrEmpty(text) ? "Default" : text;
    }

    private static bool IsSelectableMainLLMInstance(LLMInstanceInfo inst)
    {
        return inst != null && inst.isActive && inst.maxConcurrentTasks > 0 && inst.settings != null;
    }

    /// <summary>
    /// Automation seam: select the footer "Main LLM" override by instance-name
    /// substring (case-insensitive), or restore normal routing with "default".
    /// Same state the dropdown writes (PlayerPrefs-backed), so scripted tests that
    /// change it should set it back to "default" (or the prior instance) when done.
    /// </summary>
    public static bool SetMainLLMOverrideByName(string nameSubstringOrDefault, out string applied, out string error)
    {
        applied = null;
        error = null;
        Show();
        if (_instance == null)
        {
            error = "no chat panel";
            return false;
        }

        if (string.IsNullOrWhiteSpace(nameSubstringOrDefault)
            || nameSubstringOrDefault.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            SetMainLLMOverrideInstanceID(MAIN_LLM_DEFAULT_ID);
            _instance.RefreshMainLLMDropdownOptions();
            applied = "Default";
            return true;
        }

        var manager = LLMInstanceManager.Get();
        if (manager == null)
        {
            error = "no LLM instance manager";
            return false;
        }

        string needle = nameSubstringOrDefault.Trim();
        foreach (var inst in manager.GetAllInstances())
        {
            if (!IsSelectableMainLLMInstance(inst)) continue;
            if ((inst.name ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            SetMainLLMOverrideInstanceID(inst.instanceID);
            _instance.RefreshMainLLMDropdownOptions();
            applied = BuildMainLLMOptionText(inst);
            return true;
        }

        error = "no active LLM instance matches: " + needle;
        return false;
    }

    private static string BuildMainLLMOptionText(LLMInstanceInfo inst)
    {
        if (inst == null) return "Unknown";

        string name = string.IsNullOrWhiteSpace(inst.name) ? inst.providerType.ToString() : inst.name.Trim();
        string model = inst.settings != null ? (inst.settings.selectedModel ?? "").Trim() : "";
        if (string.IsNullOrEmpty(model))
            model = inst.providerType.ToString();

        string text = string.Equals(name, model, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} ({ShortenMainLLMText(model, 30)})";
        return ShortenMainLLMText(text, 48);
    }

    private static string ShortenMainLLMText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text ?? "";
        if (maxChars <= 3)
            return text.Substring(0, maxChars);
        return text.Substring(0, maxChars - 3) + "...";
    }

    private static int GetMainLLMOverrideInstanceID()
    {
        return PlayerPrefs.GetInt(PREFS_MAIN_LLM_INSTANCE_ID, MAIN_LLM_DEFAULT_ID);
    }

    private static void SetMainLLMOverrideInstanceID(int instanceID)
    {
        PlayerPrefs.SetInt(PREFS_MAIN_LLM_INSTANCE_ID, instanceID);
        PlayerPrefs.Save();
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

    private const string DefaultVideoCaptionPrompt =
        "Describe this video clip factually for a downstream video-editing AI. " +
        "You are seeing a chronological contact sheet of sampled frames from the clip, " +
        "read left-to-right, top-to-bottom. Infer visible motion and camera movement from " +
        "the frame sequence, but do not claim to hear audio. Return BOTH descriptions, " +
        "each prefixed exactly as shown:\n" +
        "\n" +
        "SHORT: <one sentence, max 15 words, suitable as a UI label>\n" +
        "LONG: <a detailed paragraph (200-300 words) describing subjects, setting, " +
        "actions/motion over time, camera/framing, lighting, colors, mood, style, " +
        "and any visible text. No preamble, no markdown, no quotes>\n" +
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
    /// clear in-flight gates. When <paramref name="requireFreeSlot"/> is true,
    /// returns null instead of dispatching to an at-capacity vision route; callers
    /// should keep their item queued and retry later.
    /// </summary>
    private CaptionJob TryCaptionBytes(byte[] png, Action<CaptionResult> onResult, bool requireFreeSlot = false, string promptOverride = null, string jobName = "ImageCaption", string debugFileName = "examine_image_sent.json", Action<string> onRawText = null, Action<string> onFailureDetail = null)
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
        if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
        {
            WarnNoVisionLLM();
            job.completed = true; safeResult(default); return job;
        }

        int targetId = instanceMgr.GetFreeLLM(isSmallJob: false, isVisionJob: true, out int replicaIndex);
        if (targetId < 0)
        {
            if (requireFreeSlot && instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0)
                return null;
            targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true, out replicaIndex);
        }
        if (targetId < 0)
        {
            // Instances exist (checked above) but none is active AND configured to accept
            // vision jobs. GetLeastBusyLLM returns even at-capacity instances, so a -1 here
            // means a config problem (no vision route), not a transient "all busy" - worth
            // telling the user instead of silently handing back an empty caption.
            WarnNoVisionLLM();
            job.completed = true; safeResult(default); return job;
        }

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
        string captionPrompt = !string.IsNullOrWhiteSpace(promptOverride)
            ? promptOverride
            : (_skillManager != null && !string.IsNullOrWhiteSpace(_skillManager.CaptionPrompt))
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
                string failureDetail = GetSidecarFailureDetail(db);
                if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(failureDetail))
                {
                    string failureLabel = string.Equals(jobName, "VideoCaption", StringComparison.OrdinalIgnoreCase)
                        ? "Video caption"
                        : "Image caption";
                    AddSystemMessage(
                        $"{failureLabel} failed on LLM #{capturedTargetId}: {failureDetail}",
                        includeInLLMRecap: true);
                }
                if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(failureDetail))
                {
                    try { onFailureDetail?.Invoke("LLM #" + capturedTargetId + ": " + failureDetail); } catch { }
                }
                try { onRawText?.Invoke(raw); } catch { }
                result = ParseCaptionResponse(raw);
            }
            finally { safeResult(result); }
        };

        SkillActionExecutor.DispatchOneShot(this, inst, lines, onDone, jobName, debugFileName);

        // Watchdog: if the request never returns (hung local model), force-release
        // the LLM slot after CAPTION_TIMEOUT_SECONDS so the user isn't stuck.
        job.watchdog = StartCoroutine(CaptionWatchdog(job, instanceMgr, safeResult));
        return job;
    }

    /// <summary>
    /// Surface the otherwise-silent case where an image needs captioning but no
    /// active LLM instance is configured to accept vision jobs. Both caption paths
    /// (attachment drop and generated-pic mirror) hit this through TryCaptionBytes
    /// and previously just returned an empty caption, so images went undescribed
    /// with no hint that the fix is a job-mode setting. Always notes it to the
    /// editor log (one entry per uncaptioned image, for diagnostics); the visible
    /// chat bubble is throttled so a multi-image drop / generation batch doesn't
    /// stack duplicates.
    /// </summary>
    private void WarnNoVisionLLM()
    {
        AIChatLog.Note("vision", "No active LLM accepts vision jobs; media left uncaptioned.");

        float now = Time.unscaledTime;
        if (now - _lastNoVisionWarnTime < NO_VISION_WARN_THROTTLE_SECONDS) return;
        _lastNoVisionWarnTime = now;

        AddSystemMessage(
            "Warning: no active LLM is set to accept vision jobs, so attached or " +
            "generated image/video media can't be described. In LLM Settings, turn on \"Supports vision\" " +
            "for an active vision-capable instance.",
            includeInLLMRecap: false);
    }

    private static string GetSidecarFailureDetail(RTDB db)
    {
        if (db == null) return "";

        string status = "";
        try { status = db.GetStringWithDefault("status", ""); } catch { }
        if (!string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            return "";

        string msg = "";
        try { msg = db.GetStringWithDefault("msg", ""); } catch { }
        msg = Regex.Replace(msg ?? "", @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(msg))
            return "request failed";

        const int maxLen = 500;
        if (msg.Length > maxLen)
            msg = msg.Substring(0, maxLen - 3).TrimEnd() + "...";
        return msg;
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
                ApplyCaptionResultToPic(capturedPic, result, "caption unavailable");
            }
            finally { safeComplete(); }
        });
    }

    private void ApplyCaptionResultToPic(PicMain capturedPic, CaptionResult result, string unavailableText)
    {
        if (capturedPic == null || capturedPic.gameObject == null) return;

        string fallback = string.IsNullOrWhiteSpace(unavailableText) ? "caption unavailable" : unavailableText;
        string shortCaption = result.IsEmpty ? fallback : (result.shortCaption ?? "");
        string longCaption = result.IsEmpty ? fallback : (result.longCaption ?? "");
        capturedPic.Caption = longCaption;
        capturedPic.CaptionShort = shortCaption;
        string labelSuffix = !string.IsNullOrEmpty(result.shortCaption)
            ? result.shortCaption
            : longCaption;
        if (!string.IsNullOrEmpty(labelSuffix)
            && _captionLabels.TryGetValue(capturedPic, out var entry)
            && entry.label != null)
            entry.label.text = entry.baseText + " " + labelSuffix;

        if (!result.IsEmpty)
            ForwardFullDescriptionOnce(capturedPic, longCaption);
    }

    // Per-Pic text of the last full description queued for the model, so a re-caption
    // with identical text (stable-texture re-polls) is not sent twice, while a genuinely
    // new description (the Pic changed) is.
    private readonly Dictionary<PicMain, string> _forwardedDescriptions = new Dictionary<PicMain, string>();

    /// <summary>
    /// Send the FULL vision description of a chat image/movie to the model exactly once,
    /// through the info-recap tail of the next outgoing message (cached history, so it is
    /// paid for once). The repeated CHAT IMAGES list keeps only the SHORT caption: long
    /// text there would be re-prefilled every turn. Pasted attachments already carry their
    /// long caption in the paste message header and never reach this path.
    /// </summary>
    private void ForwardFullDescriptionOnce(PicMain pic, string longCaption)
    {
        if (pic == null || pic.gameObject == null || string.IsNullOrWhiteSpace(longCaption)) return;
        int idx = _chatImagePics.IndexOf(pic);
        if (idx < 0) return; // not a chat image (world-only Pic)
        string text = longCaption.Trim();
        string prev;
        if (_forwardedDescriptions.TryGetValue(pic, out prev) && string.Equals(prev, text, StringComparison.Ordinal))
            return;
        _forwardedDescriptions[pic] = text;

        var record = idx < _chatImageRecords.Count ? _chatImageRecords[idx] : null;
        bool isMovie = (record != null && record.isMovie) || pic.IsMovie();
        bool isAudio = record != null && record.isAudio;
        string label = (isAudio ? "Audio #" : (isMovie ? "Movie #" : "#")) + (idx + 1);
        if (record != null && !string.IsNullOrEmpty(record.kind) && record.kind != "movie") label += " (" + record.kind + ")";
        if (record != null && !string.IsNullOrEmpty(record.anchorName)) label += " anchor=\"" + record.anchorName + "\"";
        _infoMessages.Add(new InfoMessage("(Full description of " + label + " - describe it from this, not from outside knowledge: " + text + ")"));
    }

    private bool BeginVideoCaption(PicMain pic)
    {
        if (pic == null || pic.gameObject == null) return false;
        if (_videoCaptionInFlight.Contains(pic)) return false;

        if (_videoCaptionInFlight.Count == 0)
        {
            _videoCaptionStartTime = Time.unscaledTime;
            _videoCaptionStatusNextRefresh = 0f;
        }
        _videoCaptionInFlight.Add(pic);
        RecomputeSendInteractable();
        UpdateVideoImportStatus(force: true);
        return true;
    }

    private void FinishVideoCaption(PicMain pic)
    {
        _videoCaptionInFlight.Remove(pic);
        RecomputeSendInteractable();
        UpdateVideoImportStatus(force: true);
        PokeAutoResumeSchedulers();
    }

    private IEnumerator CaptionVideoClipBubble(PicMain pic, string clipPath)
    {
        if (pic == null || pic.gameObject == null) yield break;
        if (string.IsNullOrWhiteSpace(clipPath) || !System.IO.File.Exists(clipPath)) yield break;
        if (!BeginVideoCaption(pic)) yield break;

        FfmpegTool.VideoInfo info = null;
        string probeError = null;
        yield return FfmpegTool.ProbeVideo(clipPath, (i, e) => { info = i; probeError = e; });

        if (pic == null || pic.gameObject == null)
        {
            FinishVideoCaption(pic);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(probeError))
            Debug.LogWarning("AIChatPanel: could not inspect video for captioning: " + probeError);

        double duration = info != null && info.DurationSeconds > 0
            ? info.DurationSeconds
            : FfmpegTool.DefaultClipDurationSeconds;

        FfmpegTool.ContactSheetResult sheet = null;
        yield return FfmpegTool.CreateCaptionContactSheet(clipPath, duration, r => sheet = r);

        if (pic == null || pic.gameObject == null)
        {
            FinishVideoCaption(pic);
            yield break;
        }

        if (sheet == null || !sheet.Success || string.IsNullOrWhiteSpace(sheet.OutputPath) || !System.IO.File.Exists(sheet.OutputPath))
        {
            Debug.LogWarning("AIChatPanel: video caption contact sheet failed: " + (sheet != null ? sheet.Error : "unknown error"));
            ApplyCaptionResultToPic(pic, default, "video caption unavailable");
            FinishVideoCaption(pic);
            yield break;
        }

        byte[] contactSheetPng = null;
        try { contactSheetPng = System.IO.File.ReadAllBytes(sheet.OutputPath); }
        catch (Exception ex) { Debug.LogWarning("AIChatPanel: could not read video caption contact sheet: " + ex.Message); }
        try { System.IO.File.Delete(sheet.OutputPath); } catch { }

        if (contactSheetPng == null || contactSheetPng.Length == 0)
        {
            ApplyCaptionResultToPic(pic, default, "video caption unavailable");
            FinishVideoCaption(pic);
            yield break;
        }

        PicMain capturedPic = pic;
        TryCaptionBytes(
            contactSheetPng,
            result =>
            {
                try { ApplyCaptionResultToPic(capturedPic, result, "video caption unavailable"); }
                finally
                {
                    FinishVideoCaption(capturedPic);
                }
            },
            promptOverride: DefaultVideoCaptionPrompt,
            jobName: "VideoCaption",
            debugFileName: "examine_video_contact_sheet_sent.json");
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
        var record = FindChatImageRecord(pic);
        bool isMovie = (record != null && record.isMovie) || (pic != null && pic.IsMovie());
        bool isAudio = record != null && record.isAudio;
        string mediaWord = isAudio ? "Audio" : (isMovie ? "Movie" : "Image");
        string header = idx0 >= 0 ? $"{mediaWord} #{idx0 + 1}" : mediaWord;
        if (string.IsNullOrEmpty(caption))
        {
            if (isAudio)
            {
                caption = "(sound file; no description available)";
            }
            else if (isMovie)
            {
                caption = _videoCaptionInFlight.Contains(pic)
                    ? "(video captioning...)"
                    : "(video clip; no caption available)";
            }
            else
            {
                caption = record != null && !record.isUserAttachment && !GetAutoCaptionGeneratedImages()
                ? "(generated image; captioning off)"
                : "(captioning...)";
            }
        }
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

        // This IS the new paste group. Positions must stay parallel to
        // _lastTurnAttachments (which skips only null-bytes entries), so any
        // non-null attachment that fails to promote records a null placeholder.
        _lastPasteGroupPics.Clear();

        foreach (var info in attachments)
        {
            if (info.bytes == null) continue;
            if (info.bytes.Length == 0)
            {
                _lastPasteGroupPics.Add(null);
                continue;
            }
            // Same decode pattern SkillActionExecutor uses for chat_image inputs, so
            // round-trips of the same PNG are byte-identical.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(info.bytes))
            {
                UnityEngine.Object.Destroy(tex);
                _lastPasteGroupPics.Add(null);
                continue;
            }
            var go = imageGen.AddImageByTexture(tex);
            if (go == null) { _lastPasteGroupPics.Add(null); continue; }
            var pic = go.GetComponent<PicMain>();
            if (pic == null) { _lastPasteGroupPics.Add(null); continue; }
            _lastPasteGroupPics.Add(pic);
            string dims = info.width > 0 && info.height > 0 ? $"{info.width}x{info.height}" : null;
            AppendUserAttachmentBubble(pic, info.captionShort, info.captionLong, dims);
        }
    }

    private void OnVideoFileDropped(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        StartCoroutine(HandleDroppedVideoFile(path, _videoImportEpoch));
    }

    private void OnAudioFileDropped(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        StartCoroutine(HandleDroppedAudioFile(path, _videoImportEpoch));
    }

    /// <summary>
    /// A dropped .wav/.mp3/.flac/... becomes an "Audio #N (you)" bubble: the file is copied
    /// into tempCache/aichat_audio (the chat owns its copy), probed, rendered to a waveform
    /// preview movie, and appended like a generated sound. No transcription / captioning.
    /// </summary>
    private IEnumerator HandleDroppedAudioFile(string path, int epoch)
    {
        BeginVideoImport("Importing audio");
        string copyPath = FfmpegTool.GetImportedAudioPath(path);
        try
        {
            System.IO.File.Copy(path, copyPath, true);
        }
        catch (Exception ex)
        {
            FinishVideoImport();
            AddSystemMessage("Could not import audio file: " + ex.Message, includeInLLMRecap: false);
            yield break;
        }

        FfmpegTool.AudioInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeAudio(copyPath, (i, e) => { info = i; error = e; });
        if (epoch != _videoImportEpoch) yield break;
        if (info == null || !info.HasAudio)
        {
            FinishVideoImport();
            AddSystemMessage("Could not import audio file: " + (error ?? "no audio stream"), includeInLLMRecap: false);
            yield break;
        }

        string previewPath = FfmpegTool.GetAudioPreviewOutputPath(copyPath);
        FfmpegTool.ClipResult preview = null;
        yield return FfmpegTool.CreateAudioWaveformPreview(copyPath, previewPath, FfmpegTool.AudioColorUser, r => preview = r);
        if (epoch != _videoImportEpoch) yield break;
        if (preview == null || !preview.Success)
        {
            FinishVideoImport();
            AddSystemMessage("Could not render the audio preview: " + (preview != null ? preview.Error : "unknown error"), includeInLLMRecap: false);
            yield break;
        }

        string name = System.IO.Path.GetFileName(path);
        string dur = AudioGenClient.FormatSeconds(info.DurationSeconds);
        PicMain pic = AppendAudioBubble(preview.OutputPath, copyPath, null, "user audio", info.DurationSeconds, isUserImport: true,
            captionShort: "audio file: " + name,
            captionLong: $"User-imported audio file \"{name}\" ({dur}, {info.CodecName}, {info.SampleRate} Hz, {info.Channels} ch). Its content was not transcribed; ask the user what it contains if that matters.");
        if (pic != null)
            AddSystemMessage($"Imported {dur} audio file \"{name}\" as Audio #{_chatImagePics.Count}.", includeInLLMRecap: false);
        FinishVideoImport();
    }

    private IEnumerator HandleDroppedVideoFile(string path, int epoch)
    {
        BeginVideoImport();
        AddSystemMessage("Preparing video clip import...", includeInLLMRecap: false);

        FfmpegTool.VideoInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeVideo(path, (i, e) => { info = i; error = e; });

        if (epoch != _videoImportEpoch)
            yield break;

        if (!string.IsNullOrWhiteSpace(error) || info == null)
        {
            FinishVideoImport();
            AddSystemMessage("Could not inspect dropped video: " + (error ?? "unknown error"), includeInLLMRecap: false);
            yield break;
        }

        float sourceDuration = info.DurationSeconds > 0 ? (float)info.DurationSeconds : FfmpegTool.DefaultClipDurationSeconds;
        float clipDuration = Mathf.Min(FfmpegTool.DefaultClipDurationSeconds, sourceDuration);

        if (sourceDuration > FfmpegTool.DefaultClipDurationSeconds + 0.25f && _mainPanel != null)
        {
            if (_videoClipChooser != null)
            {
                Destroy(_videoClipChooser.gameObject);
                _videoClipChooser = null;
                FinishVideoImport();
            }
            _videoClipChooser = ChatVideoClipChooser.Show(
                _mainPanel,
                _font,
                path,
                info,
                selection =>
                {
                    _videoClipChooser = null;
                    if (epoch != _videoImportEpoch)
                        return;
                    StartCoroutine(TranscodeAndAppendVideoClip(path, info, selection, epoch, null, isUserImport: true));
                },
                () =>
                {
                    _videoClipChooser = null;
                    if (epoch == _videoImportEpoch)
                    {
                        FinishVideoImport();
                        AddSystemMessage("Video import cancelled.", includeInLLMRecap: false);
                    }
                },
                onImportStill: seconds =>
                {
                    if (epoch != _videoImportEpoch)
                        return;
                    StartCoroutine(ExtractAndAppendStillFrame(path, info, seconds, epoch));
                });
            UpdateVideoImportStatus(force: true);
            yield break;
        }

        yield return TranscodeAndAppendVideoClip(path, info, CreateDefaultClipSelection(info, 0f, clipDuration), epoch, null, isUserImport: true);
    }

    private IEnumerator TranscodeAndAppendVideoClip(string sourcePath, FfmpegTool.VideoInfo info, float startSeconds, float durationSeconds, int epoch, SkillAction action, bool isUserImport)
    {
        yield return TranscodeAndAppendVideoClip(sourcePath, info, CreateDefaultClipSelection(info, startSeconds, durationSeconds), epoch, action, isUserImport);
    }

    private IEnumerator TranscodeAndAppendVideoClip(string sourcePath, FfmpegTool.VideoInfo info, ChatVideoClipChooser.ClipSelection selection, int epoch, SkillAction action, bool isUserImport)
    {
        string outputPath = FfmpegTool.GetClipOutputPath(sourcePath);
        FfmpegTool.ClipResult result = null;
        float startSeconds = selection != null ? selection.StartSeconds : 0f;
        float durationSeconds = selection != null ? selection.DurationSeconds : FfmpegTool.DefaultClipDurationSeconds;
        double fps = selection != null ? selection.Fps : GetDefaultClipFps(info);
        bool includeAudio = selection == null || selection.IncludeAudio;
        yield return FfmpegTool.CreateClip(sourcePath, startSeconds, durationSeconds, outputPath, r => result = r,
            fps: fps,
            includeAudio: includeAudio);

        if (epoch != _videoImportEpoch)
            yield break;

        if (result == null || !result.Success)
        {
            FinishVideoImport();
            string err = result != null ? result.Error : "unknown error";
            AddSystemMessage("Could not import video clip: " + err, includeInLLMRecap: false);
            yield break;
        }

        FfmpegTool.VideoInfo outputInfo = null;
        string outputProbeError = null;
        yield return FfmpegTool.ProbeVideo(result.OutputPath, (i, e) => { outputInfo = i; outputProbeError = e; });

        if (epoch != _videoImportEpoch)
            yield break;

        if (!string.IsNullOrWhiteSpace(outputProbeError))
            Debug.LogWarning("Could not inspect imported video clip output: " + outputProbeError);

        string dims = BuildVideoDimensionsText(outputInfo ?? info);
        PicMain pic = AppendVideoClipBubble(result.OutputPath, action, isUserImport, dims);
        if (pic != null)
        {
            int idx = _chatImagePics.Count;
            string startText = startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string durationText = durationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            AddSystemMessage($"Imported {durationText}s video clip starting at {startText}s as Movie #{idx}.", includeInLLMRecap: false);
        }
        FinishVideoImport();
    }

    /// <summary>
    /// Shared core for both "Import still" entry points (drag-drop video import and a
    /// movie pic's Export movie clip): extract the single frame at
    /// <paramref name="atSeconds"/> from the ORIGINAL source (ffmpeg decodes HEVC even
    /// when Unity needs a preview proxy) and append it to chat as a still image bubble.
    /// <paramref name="isStale"/> is checked right before the append so a caller can
    /// abort silently if its context was cancelled mid-extraction (null = never stale).
    /// Reports <c>(true, null)</c> on success, <c>(false, null)</c> when aborted stale,
    /// and <c>(false, error)</c> on a real failure.
    /// </summary>
    private IEnumerator AppendStillFrameFromSource(string sourcePath, string dimensions, float atSeconds,
        System.Func<bool> isStale, System.Action<bool, string> onDone)
    {
        string outputPath = FfmpegTool.GetStillFrameOutputPath(sourcePath);
        FfmpegTool.ClipResult result = null;
        yield return FfmpegTool.ExtractStillFrame(sourcePath, atSeconds, outputPath, r => result = r);

        if (isStale != null && isStale())
        {
            onDone?.Invoke(false, null);
            yield break;
        }

        if (result == null || !result.Success)
        {
            onDone?.Invoke(false, result != null ? result.Error : "unknown error");
            yield break;
        }

        var imageGen = ImageGenerator.Get();
        GameObject go = imageGen != null ? imageGen.AddImageByFileName(result.OutputPath) : null;
        PicMain pic = go != null ? go.GetComponent<PicMain>() : null;
        if (pic == null)
        {
            onDone?.Invoke(false, "failed to load extracted frame");
            yield break;
        }

        AppendUserAttachmentBubble(pic, dimensions: dimensions);
        onDone?.Invoke(true, null);
    }

    // A still keeps the source frame's native resolution; report just WxH (no fps).
    private static string BuildStillDimensionsText(FfmpegTool.VideoInfo info)
    {
        return info != null && info.Width > 0 && info.Height > 0 ? $"{info.Width}x{info.Height}" : null;
    }

    /// <summary>
    /// Drag-drop video import "Import still": extract + append, gated by the video-import
    /// busy state and epoch so a Clear/cancel mid-extraction drops the frame silently. The
    /// clip chooser stays open, so several stills can be grabbed from different positions.
    /// </summary>
    private IEnumerator ExtractAndAppendStillFrame(string sourcePath, FfmpegTool.VideoInfo info, float atSeconds, int epoch)
    {
        BeginVideoImport();

        bool ok = false;
        string err = null;
        yield return AppendStillFrameFromSource(sourcePath, BuildStillDimensionsText(info), atSeconds,
            isStale: () => epoch != _videoImportEpoch,
            onDone: (o, e) => { ok = o; err = e; });

        if (ok)
        {
            int idx = _chatImagePics.Count;
            string atText = atSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            AddSystemMessage($"Imported still frame at {atText}s as #{idx}.", includeInLLMRecap: false);
        }
        else if (!string.IsNullOrEmpty(err))
        {
            AddSystemMessage("Could not import still frame: " + err, includeInLLMRecap: false);
        }

        FinishVideoImport();
    }

    /// <summary>
    /// Static entry point used by a movie pic's "Export movie clip" dialog so its
    /// "Import still" button lands the current-position frame in AI Chat as an image
    /// bubble. Fire-and-forget (extraction is async); returns false only if chat could
    /// not be opened or the source is missing.
    /// </summary>
    /// <summary>
    /// Automation seam: stage a local image file as a PENDING attachment on the next
    /// user message, going through the REAL attachment-zone path (thumbnail strip,
    /// caption sidecar, Send gating, attachment="N" resolution) - unlike
    /// <see cref="AddLocalStillFrameToChat"/>, which promotes straight to a "#N (you)"
    /// bubble. Lets scripted tests exercise the true attachment flow end-to-end.
    /// </summary>
    public static bool StageAttachmentFromFile(string path, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "no path";
            return false;
        }
        if (!System.IO.File.Exists(path))
        {
            error = "file not found: " + path;
            return false;
        }

        Show();
        if (_instance == null || _instance._attachmentZone == null)
        {
            error = "no chat panel / attachment zone";
            return false;
        }

        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(path); }
        catch (Exception ex) { error = "read failed: " + ex.Message; return false; }

        int before = _instance._attachmentZone.GetAttachmentInfo().Count;
        _instance._attachmentZone.AddAttachment(bytes);
        if (_instance._attachmentZone.GetAttachmentInfo().Count <= before)
        {
            error = "attachment zone rejected the image (decode failure or max attachments)";
            return false;
        }
        return true;
    }

    public static bool AddLocalStillFrameToChat(string sourcePath, float atSeconds, string dimensions, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "no source path";
            return false;
        }
        if (!System.IO.File.Exists(sourcePath))
        {
            error = "source file not found: " + sourcePath;
            return false;
        }

        Show();
        if (_instance == null)
        {
            error = "no chat panel";
            return false;
        }

        _instance.StartCoroutine(_instance.AppendExportedStillRoutine(sourcePath, dimensions, atSeconds));
        return true;
    }

    private IEnumerator AppendExportedStillRoutine(string sourcePath, string dimensions, float atSeconds)
    {
        bool ok = false;
        string err = null;
        yield return AppendStillFrameFromSource(sourcePath, dimensions, atSeconds,
            isStale: null,
            onDone: (o, e) => { ok = o; err = e; });

        if (ok)
        {
            int idx = _chatImagePics.Count;
            string atText = atSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            AddSystemMessage($"Exported still frame at {atText}s as #{idx}.", includeInLLMRecap: false);
        }
        else if (!string.IsNullOrEmpty(err))
        {
            AddSystemMessage("Could not export still frame: " + err, includeInLLMRecap: false);
        }
    }

    private static ChatVideoClipChooser.ClipSelection CreateDefaultClipSelection(FfmpegTool.VideoInfo info, float startSeconds, float durationSeconds)
    {
        return new ChatVideoClipChooser.ClipSelection
        {
            StartSeconds = startSeconds,
            DurationSeconds = durationSeconds,
            Fps = GetDefaultClipFps(info),
            IncludeAudio = true
        };
    }

    private static double GetDefaultClipFps(FfmpegTool.VideoInfo info)
    {
        return info != null && info.Fps > 0 ? info.Fps : FfmpegTool.DefaultFps;
    }

    private PicMain AppendVideoClipBubble(string clipPath, SkillAction action, bool isUserImport, string dimensions, bool autoCaption = true, bool updateChainTarget = true)
    {
        var imageGen = ImageGenerator.Get();
        if (imageGen == null || string.IsNullOrEmpty(clipPath)) return null;
        var go = imageGen.AddImageByFileName(clipPath);
        if (go == null) return null;
        var pic = go.GetComponent<PicMain>();
        if (pic == null) return null;

        if (pic.m_picMovie != null)
            pic.m_picMovie.SetAutoDeleteFileWhenDone(true);

        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        RegisterChatImageRecord(pic, action, isUserAttachment: isUserImport, isMovie: true, dimensions: dimensions);
        string label = isUserImport ? $"Movie #{chatImageNumber} (you)" : $"Movie #{chatImageNumber}";
        AppendImageBubbleInternal(pic, label, isMovie: true);
        if (autoCaption)
            StartCoroutine(CaptionVideoClipBubble(pic, clipPath));

        if (action != null)
        {
            if (!string.IsNullOrEmpty(action.AnchorName))
            {
                _anchors[action.AnchorName] = pic;
                Debug.Log($"AIChatPanel: anchor '{action.AnchorName}' -> Movie #{chatImageNumber}");
            }

            MarkLatestAssistantMediaCheckpoint();
            _infoMessages.Add(new InfoMessage(
                $"(Movie just spawned as #{chatImageNumber} in CHAT IMAGES. " +
                $"Reference it on later turns via chat_image=\"{chatImageNumber}\". " +
                "Same-reply follow-ups should use chain=\"true\".)"));
            // A stitch that lands after the user moved on to a later turn must not hijack
            // that turn's chain target.
            if (updateChainTarget)
                ((IChatHost)this).SetLastSpawnedPicForTurn(pic);
        }

        return pic;
    }

    /// <summary>
    /// Append a frame extracted from a Movie bubble by the extract_still action as an
    /// ASSISTANT still bubble: plain "#N" label (not a "(you)" attachment), provenance
    /// and anchor registered, chain target updated. ALWAYS captioned (attachment-style,
    /// not gated on the auto-caption setting): the model picks extraction timestamps
    /// blind from the clip caption's shot order, so without a caption a frame that
    /// missed its target (wrong shot, nobody in it) silently poisons the identity
    /// reference it exists to provide. The caption lands async - same-reply use should
    /// verify with inspect_image when the timestamp was a guess (see extract_still.md).
    /// </summary>
    private void AppendExtractedStillBubble(PicMain pic, SkillAction action, string dimensions, int sourceChatImageIndex, float atSeconds)
    {
        if (pic == null) return;

        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        RegisterChatImageRecord(pic, action, isUserAttachment: false, isMovie: false, dimensions: dimensions);
        if (_chatImageRecords.Count > 0 && _chatImageRecords[_chatImageRecords.Count - 1].pic == pic)
            _chatImageRecords[_chatImageRecords.Count - 1].alwaysIncludeCaption = true;
        AppendImageBubbleInternal(pic, $"#{chatImageNumber}", isMovie: false);

        string atText = atSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (action != null)
        {
            if (!string.IsNullOrEmpty(action.AnchorName))
            {
                _anchors[action.AnchorName] = pic;
                Debug.Log($"AIChatPanel: anchor '{action.AnchorName}' -> Image #{chatImageNumber}");
            }

            MarkLatestAssistantMediaCheckpoint();
            string anchorHint = string.IsNullOrEmpty(action.AnchorName)
                ? ""
                : $" or its anchor \"{action.AnchorName}\"";
            _infoMessages.Add(new InfoMessage(
                $"(Still frame at {atText}s of Movie #{sourceChatImageIndex} just spawned as #{chatImageNumber} in CHAT IMAGES. " +
                $"Reference it via chat_image slot attributes as \"{chatImageNumber}\"{anchorHint}. " +
                "Its caption arrives shortly - CHECK it actually shows the intended subject before " +
                "relying on it as an identity reference; if the caption is missing or wrong, " +
                "inspect_image it or re-extract at a better time.)"));
            ((IChatHost)this).SetLastSpawnedPicForTurn(pic);
        }

        AddSystemMessage($"Extracted still frame at {atText}s of Movie #{sourceChatImageIndex} as #{chatImageNumber}.", includeInLLMRecap: false);

        StartCoroutine(WaitForPicAndCaption(pic));
    }

    /// <summary>
    /// Append a sound file as an "Audio #N" bubble. The bubble's Pic plays
    /// <paramref name="previewPath"/> (a waveform MP4 carrying the audio, see
    /// <see cref="FfmpegTool.CreateAudioWaveformPreview"/>), so it IS a Movie bubble to every
    /// existing code path; the record additionally remembers the original sound file. The
    /// caption is synthesized from what we know (prompt / voice / file name), never a vision
    /// call: a waveform tells the vision model nothing.
    /// </summary>
    private PicMain AppendAudioBubble(string previewPath, string audioPath, SkillAction action, string kind, double durationSeconds,
        bool isUserImport, string captionShort, string captionLong, bool updateChainTarget = true)
    {
        var imageGen = ImageGenerator.Get();
        if (imageGen == null || string.IsNullOrEmpty(previewPath)) return null;
        var go = imageGen.AddImageByFileName(previewPath);
        if (go == null) return null;
        var pic = go.GetComponent<PicMain>();
        if (pic == null) return null;

        if (pic.m_picMovie != null)
        {
            pic.m_picMovie.SetAutoDeleteFileWhenDone(true);
            // The pic's S button saves the preview movie; hand it the real sound file so
            // that lands next to it as <stem>.wav (or whatever the gateway returned).
            pic.m_picMovie.SetCompanionAudioFile(audioPath);
        }

        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        RegisterChatImageRecord(pic, action, isUserAttachment: isUserImport, isMovie: true, dimensions: AudioGenClient.FormatSeconds(durationSeconds));
        var record = _chatImageRecords.Count > 0 ? _chatImageRecords[_chatImageRecords.Count - 1] : null;
        if (record != null && record.pic == pic)
        {
            record.isAudio = true;
            record.audioPath = audioPath;
            record.durationSeconds = durationSeconds;
            record.kind = string.IsNullOrEmpty(kind) ? "audio" : kind;
            record.alwaysIncludeCaption = true;
            if (isUserImport && record.provenanceSteps.Count > 0)
                record.provenanceSteps[0] = "user audio file";
        }
        string label = isUserImport ? $"Audio #{chatImageNumber} (you)" : $"Audio #{chatImageNumber}";
        AppendImageBubbleInternal(pic, label, isMovie: true);
        SetSynthesizedCaption(pic, captionShort, captionLong);

        if (action != null)
        {
            string anchorHint = "";
            if (!string.IsNullOrEmpty(action.AnchorName))
            {
                _anchors[action.AnchorName] = pic;
                anchorHint = $" (anchor \"{action.AnchorName}\")";
                Debug.Log($"AIChatPanel: anchor '{action.AnchorName}' -> Audio #{chatImageNumber}");
            }
            MarkLatestAssistantMediaCheckpoint();
            _infoMessages.Add(new InfoMessage(
                $"(Audio just spawned as #{chatImageNumber} in CHAT IMAGES{anchorHint}. " +
                $"Put it on a video with set_video_audio chat_image=\"<movie>\" audio=\"{chatImageNumber}\"; " +
                "the user can play it from its bubble. It is a sound, not a picture: never use image skills on it.)"));
            if (updateChainTarget)
                ((IChatHost)this).SetLastSpawnedPicForTurn(pic);
        }

        return pic;
    }

    /// <summary>Set a caption we composed ourselves (no vision sidecar) and refresh the bubble label.</summary>
    private void SetSynthesizedCaption(PicMain pic, string shortCaption, string longCaption)
    {
        if (pic == null || pic.gameObject == null) return;
        pic.Caption = longCaption ?? "";
        pic.CaptionShort = shortCaption ?? "";
        string suffix = !string.IsNullOrEmpty(shortCaption) ? shortCaption : longCaption;
        if (!string.IsNullOrEmpty(suffix)
            && _captionLabels.TryGetValue(pic, out var entry)
            && entry.label != null)
            entry.label.text = entry.baseText + " " + suffix;
    }

    // =====================================================================
    // Audio generation (generate_music / generate_sfx / generate_speech through the
    // gateway in Settings > Audio) and set_video_audio (local FFmpeg mix / replace).
    // See docs/audio_generation.md.
    //
    // Generation reuses the video-import gate (Send blocked, footer "Generating music 42s")
    // because the reply's later actions usually depend on the sound (set_video_audio
    // audio="song"); Stop aborts the HTTP request. set_video_audio first WAITS for a
    // still-rendering source Movie exactly like stitch_video (chat stays usable), then
    // runs the short ffmpeg mux under the import gate.
    // =====================================================================

    private readonly List<AudioGenClient.Handle> _audioGenHandles = new List<AudioGenClient.Handle>();

    private bool HasPendingAudioGeneration() => _audioGenHandles.Count > 0;

    private void CancelAllAudioGeneration(bool showBubble)
    {
        if (_audioGenHandles.Count == 0) return;
        for (int i = 0; i < _audioGenHandles.Count; i++)
        {
            try { _audioGenHandles[i]?.Cancel(); } catch { }
        }
        _audioGenHandles.Clear();
        if (showBubble)
            AddSystemMessage("Stopped audio generation.", includeInLLMRecap: false);
    }

    bool IChatHost.IsChatImageAudio(int oneBasedIndex)
    {
        var record = GetChatImageRecord(oneBasedIndex);
        return record != null && record.isAudio;
    }

    string IChatHost.GetChatImageAudioFilePath(int oneBasedIndex)
    {
        var record = GetChatImageRecord(oneBasedIndex);
        if (record == null || !record.isAudio || string.IsNullOrEmpty(record.audioPath)) return null;
        return System.IO.File.Exists(record.audioPath) ? record.audioPath : null;
    }

    bool IChatHost.StartGenerateAudioAction(SkillAction action, AudioGenRequest request, Action<bool> onDone)
    {
        if (request == null) return false;
        int epoch = _videoImportEpoch;
        BeginVideoImport("Generating " + request.KindNoun);
        StartCoroutine(GenerateAudioActionCoroutine(action, request, epoch, _chatTurnEpoch, onDone));
        return true;
    }

    private static string AudioColorForKind(AudioGenKind kind)
    {
        switch (kind)
        {
            case AudioGenKind.Music: return FfmpegTool.AudioColorMusic;
            case AudioGenKind.Sfx: return FfmpegTool.AudioColorSfx;
            default: return FfmpegTool.AudioColorSpeech;
        }
    }

    private static string DescribeAudioFields(AudioGenRequest request)
    {
        var sb = new StringBuilder();
        foreach (var kv in request.Fields)
        {
            if (sb.Length > 0) sb.Append(", ");
            string v = kv.Value ?? "";
            if (v.Length > 300) v = v.Substring(0, 300) + "...";
            sb.Append(kv.Key).Append('=').Append(v.Replace("\n", "\\n"));
        }
        if (request.RefVoiceChatImageIndex > 0)
            sb.Append(", ref_voice=#").Append(request.RefVoiceChatImageIndex);
        return sb.ToString();
    }

    private void BuildAudioCaptions(AudioGenRequest request, double durationSeconds, out string shortCaption, out string longCaption)
    {
        string dur = AudioGenClient.FormatSeconds(durationSeconds);
        string prompt = request.Fields.TryGetValue("prompt", out string p) ? p : "";
        switch (request.Kind)
        {
            case AudioGenKind.Music:
            {
                bool vocals = request.Fields.ContainsKey("vocals");
                bool lyrics = request.Fields.ContainsKey("lyrics");
                shortCaption = "music: " + request.Label;
                longCaption = $"Generated music, {dur}, {(vocals ? "with vocals" : "instrumental")}{(lyrics ? ", lyrics supplied" : "")}. Caption: \"{prompt}\"";
                break;
            }
            case AudioGenKind.Sfx:
                shortCaption = "sound effect: " + request.Label;
                longCaption = $"Generated sound effect, {dur}: \"{prompt}\"";
                break;
            default:
            {
                string text = request.Fields.TryGetValue("text", out string t) ? t : "";
                string voice = request.RefVoiceChatImageIndex > 0
                    ? $"voice cloned from #{request.RefVoiceChatImageIndex}"
                    : (request.Fields.TryGetValue("voice", out string v) ? "voice \"" + v + "\"" : "default voice");
                string scene = request.Fields.TryGetValue("scene", out string s) ? $", delivery \"{s}\"" : "";
                shortCaption = "speech: \"" + request.Label + "\"";
                longCaption = $"Generated speech, {dur}, {voice}{scene}. Spoken text: \"{text}\"";
                break;
            }
        }
    }

    private IEnumerator GenerateAudioActionCoroutine(SkillAction action, AudioGenRequest request, int epoch, int turnEpoch, Action<bool> onDone)
    {
        var host = (IChatHost)this;
        string skill = request.SkillName;

        // Voice cloning sample: cut a mono WAV out of the referenced Audio / Movie bubble.
        byte[] refVoice = null;
        if (request.RefVoiceChatImageIndex > 0)
        {
            int refIdx = request.RefVoiceChatImageIndex;
            string refSource = host.GetChatImageAudioFilePath(refIdx) ?? GetStitchSourcePath(refIdx);
            if (string.IsNullOrEmpty(refSource) || !System.IO.File.Exists(refSource))
            {
                FinishVideoImport();
                host.AddSystemInjectionAndBubble($"{skill}: ref_voice #{refIdx} has no sound file to clone from (use an Audio #N or a Movie with audio).");
                host.RequestContinueTurn();
                onDone?.Invoke(false);
                yield break;
            }
            string wavPath = FfmpegTool.GetVoiceSamplePath();
            FfmpegTool.ClipResult cut = null;
            yield return FfmpegTool.ExtractAudioSection(refSource, request.RefVoiceStartSeconds, request.RefVoiceDurationSeconds, wavPath, r => cut = r);
            if (epoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
            if (cut == null || !cut.Success)
            {
                FinishVideoImport();
                host.AddSystemInjectionAndBubble($"{skill}: could not extract the voice sample from #{refIdx}: {(cut != null ? cut.Error : "unknown error")}");
                host.RequestContinueTurn();
                onDone?.Invoke(false);
                yield break;
            }
            try { refVoice = System.IO.File.ReadAllBytes(wavPath); }
            catch (Exception ex) { Debug.LogWarning("generate_speech: could not read voice sample: " + ex.Message); }
            try { System.IO.File.Delete(wavPath); } catch { }
            if (refVoice == null || refVoice.Length == 0)
            {
                FinishVideoImport();
                host.AddSystemInjectionAndBubble($"{skill}: the voice sample from #{refIdx} came out empty.");
                host.RequestContinueTurn();
                onDone?.Invoke(false);
                yield break;
            }
        }

        var handle = new AudioGenClient.Handle();
        _audioGenHandles.Add(handle);
        AudioGenClient.TryGetBaseUrl(out string baseUrl, out _, out _);
        AIChatLog.Note(skill, "POST " + AudioGenClient.GetEndpointUrl(baseUrl, request.Kind) + " " + DescribeAudioFields(request));
        AudioGenResult result = null;
        yield return AudioGenClient.Generate(request, refVoice, handle, r => result = r);
        _audioGenHandles.Remove(handle);

        if (handle.Cancelled || epoch != _videoImportEpoch)
        {
            if (epoch == _videoImportEpoch) FinishVideoImport();
            onDone?.Invoke(false);
            yield break;
        }
        if (result == null || !result.Success)
        {
            FinishVideoImport();
            string err = result != null ? result.Error : "unknown error";
            AIChatLog.Note(skill, "failed: " + err);
            host.AddSystemInjectionAndBubble($"{skill} failed: {err}");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        FfmpegTool.AudioInfo info = null;
        string probeError = null;
        yield return FfmpegTool.ProbeAudio(result.OutputPath, (i, e) => { info = i; probeError = e; });
        if (epoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
        if (info == null || !info.HasAudio)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"{skill}: the gateway's file could not be read as audio: {probeError ?? "no audio stream"} ({result.OutputPath})");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        string previewPath = FfmpegTool.GetAudioPreviewOutputPath(result.OutputPath);
        FfmpegTool.ClipResult preview = null;
        yield return FfmpegTool.CreateAudioWaveformPreview(result.OutputPath, previewPath, AudioColorForKind(request.Kind), r => preview = r);
        if (epoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
        if (preview == null || !preview.Success)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"{skill}: could not render the waveform preview: {(preview != null ? preview.Error : "unknown error")}");
            onDone?.Invoke(false);
            yield break;
        }

        string kindLabel = request.Kind == AudioGenKind.Music ? "generated music"
            : (request.Kind == AudioGenKind.Sfx ? "generated sound effect" : "generated speech");
        BuildAudioCaptions(request, info.DurationSeconds, out string shortCaption, out string longCaption);
        PicMain pic = AppendAudioBubble(preview.OutputPath, result.OutputPath, action, kindLabel, info.DurationSeconds, isUserImport: false,
            shortCaption, longCaption, updateChainTarget: turnEpoch == _chatTurnEpoch);
        if (pic == null)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"{skill}: could not load the audio preview into a chat bubble.");
            onDone?.Invoke(false);
            yield break;
        }

        int idx = _chatImagePics.Count;
        string anchorText = action != null && !string.IsNullOrEmpty(action.AnchorName) ? $" (anchor \"{action.AnchorName}\")" : "";
        string summary = $"{skill}: {AudioGenClient.FormatSeconds(info.DurationSeconds)} of {request.KindNoun} generated in {result.ElapsedSeconds:0}s as Audio #{idx}{anchorText}. File: {result.OutputPath}";
        AddSystemMessage(summary);
        AIChatLog.Note(skill, summary);
        if (action != null && action.Resume)
            host.RequestContinueTurn();
        FinishVideoImport();
        onDone?.Invoke(true);
    }

    bool IChatHost.StartSetVideoAudioAction(SkillAction action, int videoChatImageIndex, int audioChatImageIndex, FfmpegTool.MuxAudioRequest request, Action<bool> onDone)
    {
        if (request == null || videoChatImageIndex <= 0 || audioChatImageIndex <= 0) return false;
        StartCoroutine(SetVideoAudioActionCoroutine(action, videoChatImageIndex, audioChatImageIndex, request, _videoImportEpoch, _chatTurnEpoch, onDone));
        return true;
    }

    private IEnumerator SetVideoAudioActionCoroutine(SkillAction action, int videoIdx, int audioIdx, FfmpegTool.MuxAudioRequest request, int importEpoch, int turnEpoch, Action<bool> onDone)
    {
        var host = (IChatHost)this;

        // ---- Phase 1: wait for the sources (a same-reply "make a video AND a song, then
        // combine" lands here while the H3 render is still minutes away). Same rules as
        // stitch_video: only Stop/Clear cancel the wait.
        var sources = new List<int> { videoIdx, audioIdx };
        var pending = new List<int>();
        string failure = CollectStitchSourceState(sources, pending, out _, out bool readyNow);
        if (failure == null && !readyNow && pending.Count > 0)
        {
            AddSystemMessage($"set_video_audio: waiting for {DescribeMovieList(pending)} to finish rendering, then adding the audio.");
            AIChatLog.Note("set_video_audio", "waiting for " + DescribeMovieList(pending));
        }

        BeginStitchWait();
        float waitStart = Time.realtimeSinceStartup;
        float notBusySince = -1f;
        while (failure == null)
        {
            if (importEpoch != _videoImportEpoch)
            {
                EndStitchWait();
                onDone?.Invoke(false);
                yield break;
            }

            pending.Clear();
            failure = CollectStitchSourceState(sources, pending, out bool anyBusy, out bool allReady);
            if (failure != null || allReady)
                break;

            float elapsed = Time.realtimeSinceStartup - waitStart;
            if (elapsed >= StitchWaitAbsoluteCapSeconds)
            {
                failure = $"gave up waiting for {DescribeMovieList(pending)} after {Mathf.RoundToInt(elapsed / 60f)} minutes.";
                break;
            }
            if (anyBusy)
            {
                notBusySince = -1f;
            }
            else
            {
                if (notBusySince < 0f)
                    notBusySince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - notBusySince >= StitchNoClipGraceSeconds)
                {
                    failure = $"{DescribeMovieList(pending)} finished without producing a video file (the render probably failed).";
                    break;
                }
            }

            UpdateStitchWaitStatus(pending.Count, "set_video_audio");
            yield return new WaitForSeconds(StitchWaitPollSeconds);
        }
        EndStitchWait();

        if (failure != null)
        {
            AIChatLog.Note("set_video_audio", "failed: " + failure);
            host.AddSystemInjectionAndBubble("set_video_audio could not run: " + failure);
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        // ---- Phase 2: probe + mux under the import gate.
        BeginVideoImport("Mixing audio");
        string videoPath = GetStitchSourcePath(videoIdx);
        string audioPath = host.GetChatImageAudioFilePath(audioIdx);
        if (string.IsNullOrEmpty(audioPath))
            audioPath = GetStitchSourcePath(audioIdx);   // a Movie's own soundtrack
        if (string.IsNullOrEmpty(videoPath) || string.IsNullOrEmpty(audioPath))
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"set_video_audio: could not find the files behind Movie #{videoIdx} / #{audioIdx}.");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        FfmpegTool.VideoInfo videoInfo = null;
        string videoError = null;
        yield return FfmpegTool.ProbeVideo(videoPath, (i, e) => { videoInfo = i; videoError = e; });
        if (importEpoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
        if (videoInfo == null || !videoInfo.HasVideo)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"set_video_audio could not inspect Movie #{videoIdx}: {videoError ?? "no video stream"}");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        FfmpegTool.AudioInfo audioInfo = null;
        string audioError = null;
        yield return FfmpegTool.ProbeAudio(audioPath, (i, e) => { audioInfo = i; audioError = e; });
        if (importEpoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
        if (audioInfo == null || !audioInfo.HasAudio)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble($"set_video_audio: #{audioIdx} has no audio stream ({audioError ?? "silent"}).");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        request.VideoPath = videoPath;
        request.AudioPath = audioPath;
        request.VideoDurationSeconds = videoInfo.DurationSeconds;
        request.VideoHasAudio = videoInfo.HasAudio;
        request.AudioDurationSeconds = audioInfo.DurationSeconds;

        string outputPath = FfmpegTool.GetClipOutputPath(videoPath);
        FfmpegTool.ClipResult result = null;
        yield return FfmpegTool.MuxAudioIntoVideo(request, outputPath, r => result = r);
        if (importEpoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }
        if (result == null || !result.Success)
        {
            FinishVideoImport();
            string err = result != null ? result.Error : "unknown error";
            AIChatLog.Note("set_video_audio", "ffmpeg failed: " + err);
            host.AddSystemInjectionAndBubble($"set_video_audio failed on Movie #{videoIdx}: {err}");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        FfmpegTool.VideoInfo outputInfo = null;
        yield return FfmpegTool.ProbeVideo(result.OutputPath, (i, e) => { outputInfo = i; });
        if (importEpoch != _videoImportEpoch) { onDone?.Invoke(false); yield break; }

        string dims = BuildVideoDimensionsText(outputInfo ?? videoInfo);
        PicMain pic = AppendVideoClipBubble(result.OutputPath, action, isUserImport: false, dimensions: dims,
            autoCaption: false, updateChainTarget: turnEpoch == _chatTurnEpoch);
        if (pic == null)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble("set_video_audio could not load the result into a Movie bubble.");
            onDone?.Invoke(false);
            yield break;
        }

        // The picture is the source clip's picture: reuse its description instead of a
        // vision re-caption, and note the new soundtrack.
        int newIdx = _chatImagePics.Count;
        var srcPic = GetChatImagePic(videoIdx);
        var audioPic = GetChatImagePic(audioIdx);
        string srcLong = srcPic != null ? (srcPic.Caption ?? "").Trim() : "";
        string srcShort = srcPic != null ? (srcPic.CaptionShort ?? "").Trim() : "";
        string audioDesc = audioPic != null && !string.IsNullOrWhiteSpace(audioPic.CaptionShort) ? audioPic.CaptionShort.Trim() : $"#{audioIdx}";
        bool mixed = request.EffectiveMix;
        string modeText = mixed
            ? $"mixed over the original soundtrack (original at {request.OriginalVolume:0.##}x, new audio at {request.AudioVolume:0.##}x)"
            : (request.Mode == FfmpegTool.AudioMuxMode.Mix ? "as its only soundtrack (the source clip was silent)" : "as its only soundtrack (original audio removed)");
        string extras = (request.StartSeconds > 0.001f ? $", starting at {request.StartSeconds:0.##}s" : "")
            + (request.Loop ? ", looped" : "")
            + (request.EffectiveFadeOutSeconds > 0.001f ? $", {request.EffectiveFadeOutSeconds:0.#}s fade-out" : "");
        SetSynthesizedCaption(pic,
            (string.IsNullOrEmpty(srcShort) ? $"Movie #{videoIdx}" : srcShort) + $" + audio #{audioIdx}",
            (string.IsNullOrEmpty(srcLong) ? $"Same picture as Movie #{videoIdx}." : srcLong) + $" Soundtrack: Audio #{audioIdx} ({audioDesc}) {modeText}{extras}.");
        var newRecord = FindChatImageRecord(pic);
        if (newRecord != null)
        {
            newRecord.alwaysIncludeCaption = true;
            newRecord.durationSeconds = outputInfo != null ? outputInfo.DurationSeconds : videoInfo.DurationSeconds;
        }

        string summary = $"set_video_audio: Movie #{videoIdx} + Audio #{audioIdx} -> Movie #{newIdx} " +
                         $"({AudioGenClient.FormatSeconds(newRecord != null ? newRecord.durationSeconds : videoInfo.DurationSeconds)}, {(mixed ? "mixed" : "replaced")}{extras}).";
        AddSystemMessage(summary);
        AIChatLog.Note("set_video_audio", summary + "\n" + (result.Command ?? ""));
        if (action != null && action.Resume)
            host.RequestContinueTurn();
        FinishVideoImport();
        onDone?.Invoke(true);
    }

    // =====================================================================
    // Web media fetch (web_search / web_image / web_video). See docs/web_media.md.
    //
    // Every search and download is traced IN FULL into an always-visible "Web" bubble
    // (not the debug-gated Info bubble) so a malfunction is obvious at a glance: the
    // query + params, the complete numbered result list, each download attempt with
    // URL / HTTP status / content-type / bytes / conversion, the resulting #N label,
    // and for yt-dlp the exact command line plus its output tail. A compact copy goes
    // to the model via the info recap. Fetches (and the captions of what they spawned)
    // count as sidecar work, so Send/automation idle/auto-resume all wait for them.
    // =====================================================================

    private static readonly Color WebLabelColor = new Color(0.10f, 0.38f, 0.52f);
    private static readonly Color WebBubbleBg = new Color(0.90f, 0.95f, 0.97f, 1f);

    private int _webFetchCount = 0;
    private int _webFetchEpoch = 0;
    private float _webFetchStartTime = 0f;
    private float _webFetchStatusNextRefresh = 0f;
    private int _webFetchSpinnerStep = 0;
    private float _webCaptionStartTime = 0f;
    private readonly List<WebMediaDownloader.Handle> _webDownloadHandles = new List<WebMediaDownloader.Handle>();
    private readonly List<FfmpegTool.CancelToken> _webProcessCancels = new List<FfmpegTool.CancelToken>();
    private readonly List<WebTraceBubble> _activeWebTraces = new List<WebTraceBubble>();
    private readonly HashSet<PicMain> _webCaptionInFlight = new HashSet<PicMain>();

    private sealed class WebSearchSession
    {
        public string Id;
        public WebSearchKind Kind;
        public string Query;
        public BraveSearchClient.SearchResponse Response;
    }
    private readonly Dictionary<string, WebSearchSession> _webSearchSessions = new Dictionary<string, WebSearchSession>(StringComparer.OrdinalIgnoreCase);
    private int _nextWebSearchId = 1;
    // URL -> the Pic it was already fetched into this session (dedupe; Pic reference
    // because chat_image numbers shift when old bubbles are trimmed).
    private readonly Dictionary<string, PicMain> _webFetchedUrlToPic = new Dictionary<string, PicMain>(StringComparer.OrdinalIgnoreCase);

    private bool HasPendingWebWork() => _webFetchCount > 0 || _webCaptionInFlight.Count > 0;

    private void BeginWebFetch()
    {
        if (_webFetchCount <= 0)
            _webFetchStartTime = Time.unscaledTime;
        _webFetchCount++;
        RecomputeSendInteractable();
        UpdateWebFetchStatus(force: true);
    }

    private void FinishWebFetch()
    {
        _webFetchCount = Mathf.Max(0, _webFetchCount - 1);
        RecomputeSendInteractable();
        // UpdateWebFetchStatus sees count == 0 with a live start time and writes the
        // "done" text (or switches to the caption status) before clearing the timer.
        UpdateWebFetchStatus(force: true);
        PokeAutoResumeSchedulers();
    }

    // The auto-resume schedulers bail while sidecar work is pending and are otherwise only
    // re-poked from inspect completion / FinalizeAssistantTurn, so a resume requested by
    // a web action would hang until the next user send unless the finish path pokes them.
    private void PokeAutoResumeSchedulers()
    {
        TryScheduleInspectAutoResume();
        TryScheduleSkillLoadAutoResume();
        TryScheduleGenericContinue();
    }

    private void CancelAllWebFetches(bool showBubble)
    {
        bool hadWork = HasPendingWebWork();
        _webFetchEpoch++;
        for (int i = 0; i < _webDownloadHandles.Count; i++)
        {
            try { _webDownloadHandles[i]?.Cancel(); } catch { }
        }
        _webDownloadHandles.Clear();
        for (int i = 0; i < _webProcessCancels.Count; i++)
        {
            try { _webProcessCancels[i]?.Cancel(); } catch { }
        }
        _webProcessCancels.Clear();
        for (int i = 0; i < _activeWebTraces.Count; i++)
        {
            var trace = _activeWebTraces[i];
            if (trace != null && trace.IsAlive && hadWork)
            {
                trace.ClearStatus();
                trace.AppendLine("Cancelled.");
            }
        }
        _activeWebTraces.Clear();
        _webCaptionInFlight.Clear();
        _webFetchCount = 0;
        _webFetchStartTime = 0f;
        _webFetchStatusNextRefresh = 0f;
        _webCaptionStartTime = 0f;
        RecomputeSendInteractable();
        if (showBubble && hadWork)
            AddSystemMessage("Stopped web fetch.", includeInLLMRecap: false);
    }

    private void UpdateWebFetchStatus(bool force = false)
    {
        if (_statusText == null || _isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || CountPendingInspectImageJobs() > 0)
            return;

        if (_webFetchCount > 0)
        {
            if (_webFetchStartTime <= 0f)
                _webFetchStartTime = Time.unscaledTime;
            if (!force && Time.unscaledTime < _webFetchStatusNextRefresh)
                return;

            _webFetchStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _webFetchSpinnerStep = (_webFetchSpinnerStep + 1) % StreamSpinnerFrames.Length;
            float elapsed = Time.unscaledTime - _webFetchStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_webFetchSpinnerStep]} Fetching from web   {elapsed:F0}s";
            return;
        }

        if (_webCaptionInFlight.Count > 0)
        {
            if (_webCaptionStartTime <= 0f)
                _webCaptionStartTime = Time.unscaledTime;
            if (!force && Time.unscaledTime < _webFetchStatusNextRefresh)
                return;

            _webFetchStatusNextRefresh = Time.unscaledTime + STREAM_STATUS_INTERVAL;
            _webFetchSpinnerStep = (_webFetchSpinnerStep + 1) % StreamSpinnerFrames.Length;
            float elapsed = Time.unscaledTime - _webCaptionStartTime;
            _statusText.text = $"{StreamSpinnerFrames[_webFetchSpinnerStep]} Captioning web image{(_webCaptionInFlight.Count == 1 ? "" : "s")}   {elapsed:F0}s";
            return;
        }

        if (_webFetchStartTime > 0f || _webCaptionStartTime > 0f)
        {
            _webFetchStartTime = 0f;
            _webCaptionStartTime = 0f;
            _webFetchStatusNextRefresh = 0f;
            _statusText.text = "Web fetch done";
        }
    }

    /// <summary>
    /// Display-only escape for plain-text trace bubbles: ONLY the TMP angle-bracket
    /// substitution from ConvertMarkdownToTMP, no markdown regexes (URLs carry '*',
    /// '#', '_', '-' that the markdown pass would mangle).
    /// </summary>
    private static string EscapePlainTextForTMP(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace('<', '＜').Replace('>', '＞');
    }

    private WebTraceBubble BeginWebTrace(string headerLine)
    {
        var field = AppendBubble("Web", WebLabelColor, "", WebBubbleBg);
        // Command lines and file paths contain backslash-n / backslash-t sequences
        // (C:/Program Files/nodejs, .../tempCache/...) that TMP would otherwise turn
        // into newlines / tabs.
        if (field != null && field.textComponent != null)
            field.textComponent.parseCtrlCharacters = false;
        var trace = new WebTraceBubble(field, EscapePlainTextForTMP,
            () => IsScrollAtBottom(_chatScroll),
            () => StartCoroutine(ScrollToBottomDeferred()));
        _activeWebTraces.Add(trace);
        if (!string.IsNullOrEmpty(headerLine))
            trace.AppendLine(headerLine);
        return trace;
    }

    private void EndWebTrace(WebTraceBubble trace)
    {
        if (trace == null) return;
        _activeWebTraces.Remove(trace);
        AIChatLog.Note("web_trace", trace.GetRawText());
    }

    private static string Q(string s) => "\"" + (s ?? "") + "\"";

    private static string FormatSeconds(float seconds)
    {
        return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
    }

    private static string ShortUrlForProvenance(string url, int max = 110)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            var u = new Uri(url);
            string s = u.Host + u.AbsolutePath;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
        catch
        {
            return url.Length <= max ? url : url.Substring(0, max) + "...";
        }
    }

    private string NextWebSearchId() => "S" + (_nextWebSearchId++).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private WebSearchSession StoreWebSearchSession(WebSearchKind kind, string query, BraveSearchClient.SearchResponse resp)
    {
        var session = new WebSearchSession { Id = NextWebSearchId(), Kind = kind, Query = query, Response = resp };
        _webSearchSessions[session.Id] = session;
        return session;
    }

    /// <summary>Parse "S1:3" into the session + 1-based index it names.</summary>
    private bool TryResolveWebSearchToken(string token, out WebSearchSession session, out int index, out string error)
    {
        session = null;
        index = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(token)) { error = "empty result token"; return false; }
        string t = token.Trim();
        int colon = t.IndexOf(':');
        if (colon <= 0 || colon == t.Length - 1) { error = "result must look like \"S1:3\" (search id, colon, result number)"; return false; }
        string id = t.Substring(0, colon).Trim();
        string num = t.Substring(colon + 1).Trim();
        if (!_webSearchSessions.TryGetValue(id, out session) || session == null || session.Response == null)
        {
            error = "unknown search id \"" + id + "\" (web_search lists expire on Clear)";
            return false;
        }
        if (!int.TryParse(num, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out index) || index < 1)
        {
            error = "bad result number \"" + num + "\"";
            return false;
        }
        int count = session.Response.ResultCount(session.Kind);
        if (index > count)
        {
            error = id + " only has " + count + " results";
            return false;
        }
        return true;
    }

    /// <summary>
    /// One line per search in the bubble: the terms actually sent and the hit count. The full
    /// numbered result list is only shown in the bubble for the list-only web_search skill
    /// (<paramref name="listResults"/>); web_image / web_video write it to the editor log instead,
    /// since their per-download lines already name every URL that was actually tried.
    /// </summary>
    private string BuildWebSearchTraceLines(WebTraceBubble trace, WebSearchKind kind, string query, BraveSearchClient.SearchResponse resp, bool listResults)
    {
        string kindLabel = BraveSearchClient.KindLabel(kind);
        if (resp == null || !resp.Success)
        {
            string line = "Searched Brave " + kindLabel + " for " + Q(query) + ": FAILED - " + (resp != null ? (resp.Error ?? "failed") : "no response");
            if (resp != null && !string.IsNullOrEmpty(resp.BodyExcerpt))
                line += "\n  body: " + resp.BodyExcerpt;
            trace.AppendLine(line);
            AIChatLog.Note("web_request", resp != null ? resp.RequestUrlForDisplay ?? "" : "");
            return null;
        }
        int n = resp.ResultCount(kind);
        string ok = "Searched Brave " + kindLabel + " for " + Q(query) + ": " + n + (n == 1 ? " hit" : " hits") + " (" + FormatSeconds(resp.ElapsedSeconds) + ")"
            + (string.IsNullOrEmpty(resp.AlteredQuery) ? "" : "   [spellcheck changed it to " + Q(resp.AlteredQuery) + "]");
        trace.AppendLine(ok);
        var lines = BraveSearchClient.FormatResultLines(kind, resp);
        if (listResults)
        {
            if (n == 0) trace.AppendLine("  (none)");
            else trace.AppendLines(lines);
        }
        else
        {
            AIChatLog.Note("web_results", "GET " + (resp.RequestUrlForDisplay ?? "") + "\n" + string.Join("\n", lines));
        }
        return ok;
    }

    /// <summary>
    /// A Brave call failed. Besides the trace line, make the CAUSE unmistakable to the user:
    /// a rejected / missing key or an exhausted quota gets an always-visible red Error bubble
    /// that says exactly where to fix it (Settings > Web), and the model is told to relay that
    /// instead of retrying. Returns the recap text to queue for the model.
    /// </summary>
    private string ReportWebSearchFailure(string skillId, string query, BraveSearchClient.SearchResponse resp)
    {
        string error = resp != null ? (resp.Error ?? "failed") : "no response";
        int status = resp != null ? resp.HttpStatus : 0;
        string lowered = (error + " " + (resp != null ? resp.BodyExcerpt : "")).ToLowerInvariant();
        bool keyProblem = status == 401 || status == 403
            || lowered.Contains("subscription_token") || lowered.Contains("subscription token")
            || lowered.Contains("api key") || lowered.Contains("unauthorized") || lowered.Contains("forbidden");
        bool quotaProblem = status == 429 || lowered.Contains("quota") || lowered.Contains("rate limit") || lowered.Contains("rate_limited");
        bool noKey = status == 0 && lowered.Contains("no brave search api key");

        string userFix;
        if (noKey || keyProblem)
        {
            userFix = (noKey
                ? "Brave Search has no API key configured."
                : "Brave Search rejected the API key (" + error + ").")
                + "\nEnter a valid key in Settings > Web (it is stored as set_brave_search_api_key in config.txt). "
                + "Keys come from https://brave.com/search/api/ - the Search plan includes free monthly credit. "
                + "AI Chat's web_search / web_image / web_video cannot work until this is fixed.";
        }
        else if (quotaProblem)
        {
            userFix = "Brave Search refused the request (" + error + ").\nThe key's rate limit or monthly credit is exhausted; "
                + "check usage at https://api-dashboard.search.brave.com/ or wait and retry. Settings > Web holds the key.";
        }
        else
        {
            userFix = skillId + " could not reach Brave Search (" + error + ").\nIf this keeps happening, check the key in Settings > Web and the network.";
        }
        AddErrorBubble(userFix);

        // The assistant's streamed reply usually already promised the image ("Here's a
        // photo of..."); give it one bounded continue turn so it can tell the user what
        // actually happened instead of leaving a false promise on screen.
        if (noKey || keyProblem || quotaProblem)
            ((IChatHost)this).RequestContinueTurn();

        string modelNote = noKey || keyProblem
            ? "(" + skillId + " " + Q(query) + " failed: " + error + ". The Brave Search API key is missing or invalid - tell the user to enter a valid key in Settings > Web and STOP using web_search/web_image/web_video until they confirm it is fixed. Do not retry.)"
            : quotaProblem
                ? "(" + skillId + " " + Q(query) + " failed: " + error + ". The Brave key's rate limit or monthly credit is exhausted - tell the user plainly; do not retry this turn.)"
                : "(" + skillId + " " + Q(query) + " failed: " + error + ". Do not retry the same query blindly; tell the user what failed.)";
        return modelNote;
    }

    // ---------- web_search ----------

    bool IChatHost.StartWebSearchAction(SkillAction action, WebSearchRequest request, Action<bool> onDone)
    {
        if (request == null) return false;
        int epoch = _webFetchEpoch;
        BeginWebFetch();
        StartCoroutine(WebSearchCoroutine(action, request, epoch, onDone));
        return true;
    }

    private IEnumerator WebSearchCoroutine(SkillAction action, WebSearchRequest req, int epoch, Action<bool> onDone)
    {
        string safe = req.SafeSearch ?? (Config.Get() != null ? Config.Get().GetWebSearchSafeSearch() : "strict");
        var trace = BeginWebTrace("web_search  kind=" + BraveSearchClient.KindLabel(req.Kind) + "  query=" + Q(req.Query) + "  count=" + req.Count + "  safesearch=" + safe);

        BraveSearchClient.SearchResponse resp = null;
        yield return BraveSearchClient.Search(req.Kind, req.Query, req.Count, req.SafeSearch, r => resp = r);
        if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

        BuildWebSearchTraceLines(trace, req.Kind, req.Query, resp, listResults: true);
        if (resp == null || !resp.Success)
        {
            _infoMessages.Add(new InfoMessage(ReportWebSearchFailure("web_search", req.Query, resp)));
            EndWebTrace(trace);
            FinishWebFetch();
            onDone?.Invoke(true);
            yield break;
        }

        var session = StoreWebSearchSession(req.Kind, req.Query, resp);
        int n = resp.ResultCount(req.Kind);
        trace.AppendLine("Stored as " + session.Id + ": use result=\"" + session.Id + ":N\" with web_image / web_video to download one.");

        var recap = new StringBuilder();
        recap.Append("[web_search ").Append(session.Id).Append(' ').Append(BraveSearchClient.KindLabel(req.Kind)).Append(' ').Append(Q(req.Query)).Append(" -> ").Append(n).Append(" results]");
        foreach (string line in BraveSearchClient.FormatResultLines(req.Kind, resp, maxUrlChars: 200))
            recap.Append('\n').Append(line);
        if (req.Kind == WebSearchKind.Web)
            recap.Append("\n(These are page results; quote facts from the descriptions only, the pages themselves were not fetched.)");
        else
            recap.Append("\n(Use ").Append(req.Kind == WebSearchKind.Videos ? "web_video" : "web_image").Append(" result=\"").Append(session.Id).Append(":N\" or url=\"...\" to download one. Lists expire on Clear.)");
        _infoMessages.Add(new InfoMessage(recap.ToString()));

        EndWebTrace(trace);
        FinishWebFetch();
        onDone?.Invoke(true);
    }

    // ---------- web_page ----------

    /// <summary>
    /// One fetched page: its candidate image list lives on as "P<n>" so the model can follow up
    /// with web_image result="P<n>:<i>" (same shape as the S<n> search sessions). Cleared on Clear.
    /// </summary>
    private sealed class WebPageSession
    {
        public string Id;
        public string Url;
        public string Title;
        public List<WebPageImage> Images;
    }
    private readonly Dictionary<string, WebPageSession> _webPageSessions = new Dictionary<string, WebPageSession>(StringComparer.OrdinalIgnoreCase);
    private int _nextWebPageId = 1;

    private string NextWebPageId() => "P" + (_nextWebPageId++).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsWebPageToken(string token)
    {
        string t = (token ?? "").Trim();
        return t.Length > 1 && (t[0] == 'P' || t[0] == 'p') && char.IsDigit(t[1]);
    }

    /// <summary>Parse "P1:3" into the page session + 1-based image index it names.</summary>
    private bool TryResolveWebPageToken(string token, out WebPageSession session, out int index, out string error)
    {
        session = null;
        index = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(token)) { error = "empty result token"; return false; }
        string t = token.Trim();
        int colon = t.IndexOf(':');
        if (colon <= 0 || colon == t.Length - 1) { error = "page image refs look like \"P1:3\" (page id, colon, image number)"; return false; }
        string id = t.Substring(0, colon).Trim();
        string num = t.Substring(colon + 1).Trim();
        if (!_webPageSessions.TryGetValue(id, out session) || session == null || session.Images == null)
        {
            error = "unknown page id \"" + id + "\" (web_page image lists expire on Clear)";
            return false;
        }
        if (!int.TryParse(num, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out index) || index < 1)
        {
            error = "bad image number \"" + num + "\"";
            return false;
        }
        if (index > session.Images.Count)
        {
            error = id + " only has " + session.Images.Count + " images";
            return false;
        }
        return true;
    }

    bool IChatHost.StartWebPageAction(SkillAction action, WebPageRequest request, Action<bool> onDone)
    {
        if (request == null) return false;
        int epoch = _webFetchEpoch;
        BeginWebFetch();
        StartCoroutine(WebPageCoroutine(action, request, epoch, onDone));
        return true;
    }

    // Reading pages (not images): forums / social feeds are noisy, shops sell, PDFs can't be parsed.
    private static readonly string[] WebPageJunkHosts =
    {
        "reddit.com", "quora.com", "pinterest.", "facebook.com", "x.com", "twitter.com", "instagram.com", "tiktok.com",
        "youtube.com", "youtu.be", "linkedin.com", "tumblr.com", "threads.net"
    };
    private static readonly string[] WebPageShopHosts = { "amazon.", "ebay.", "etsy.com", "aliexpress.", "walmart.com", "bestbuy.com" };
    private static readonly string[] WebPageJunkTitleWords = { "top 10", "top ten", "best ", "review", "buy ", "cheap", "coupon", "deal", "price", " vs ", "vs.", "for sale" };

    /// <summary>
    /// Rank kind="web" hits for READING: reference sites up (Wikipedia, .edu/.gov, Britannica,
    /// archive.org), forums / social / shops / PDFs / SEO-style titles down, +1 when every query
    /// word appears in the title or snippet. Stable: ties keep Brave order.
    /// </summary>
    private static List<KeyValuePair<int, int>> RankWebPageResults(List<BraveSearchClient.WebResult> results, string query)
    {
        var scored = new List<KeyValuePair<int, int>>();
        var words = new List<string>();
        foreach (string w in (query ?? "").ToLowerInvariant().Split(new[] { ' ', ',', '.', '"', '\'', '?', '!', ':', ';', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            if (w.Length >= 3) words.Add(w);
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            string host = (string.IsNullOrEmpty(r.Host) ? SafeHost(r.Url) : r.Host).ToLowerInvariant();
            string url = (r.Url ?? "").ToLowerInvariant();
            string title = (r.Title ?? "").ToLowerInvariant();
            string text = title + " " + (r.Description ?? "").ToLowerInvariant();
            int score = 0;
            if (host.Contains("wikipedia.org") || host.Contains("wikimedia.org")) score += 5;
            else if (host.EndsWith(".edu") || host.EndsWith(".gov") || host.Contains("britannica.com") || host.Contains("archive.org")) score += 3;
            foreach (string h in WebPageJunkHosts) if (host.Contains(h)) { score -= 4; break; }
            foreach (string h in WebPageShopHosts) if (host.Contains(h)) { score -= 3; break; }
            string path = url;
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);
            if (path.EndsWith(".pdf")) score -= 5;
            foreach (string w in WebPageJunkTitleWords) if (title.Contains(w)) { score -= 2; break; }
            if (words.Count > 0)
            {
                bool all = true;
                foreach (string w in words) if (!text.Contains(w)) { all = false; break; }
                if (all) score += 1;
            }
            scored.Add(new KeyValuePair<int, int>(i, score));
        }
        // Stable insertion sort, score descending.
        for (int i = 1; i < scored.Count; i++)
        {
            var item = scored[i];
            int j = i - 1;
            while (j >= 0 && scored[j].Value < item.Value) { scored[j + 1] = scored[j]; j--; }
            scored[j + 1] = item;
        }
        return scored;
    }

    private IEnumerator WebPageCoroutine(SkillAction action, WebPageRequest req, int epoch, Action<bool> onDone)
    {
        string sourceText = !string.IsNullOrEmpty(req.Url) ? "url=" + Q(req.Url)
            : !string.IsNullOrEmpty(req.ResultToken) ? "result=" + Q(req.ResultToken)
            : "query=" + Q(req.Query);
        var trace = BeginWebTrace("web_page  " + sourceText + "  max_chars=" + req.MaxChars + "  images=" + (req.Images ? "true" : "false")
            + (req.Images ? "  max_images=" + req.MaxImages : ""));
        float started = Time.realtimeSinceStartup;
        bool queryMode = string.IsNullOrEmpty(req.Url) && string.IsNullOrEmpty(req.ResultToken);

        // Every non-cancel exit funnels through here (coroutines cannot wrap yields in finally).
        // Cancel exits (epoch mismatch after a yield) skip it on purpose: CancelAllWebFetches has
        // already zeroed the counters and written "Cancelled." into the bubble.
        void Fail(string traceLine, string modelNote)
        {
            if (!string.IsNullOrEmpty(traceLine)) trace.AppendLine(traceLine);
            if (!string.IsNullOrEmpty(modelNote)) _infoMessages.Add(new InfoMessage(modelNote));
            // The streamed reply usually already promised the page; without the auto-resume
            // slot one bounded continue lets the model tell the user what actually happened.
            if (!req.Resume) ((IChatHost)this).RequestContinueTurn();
            EndWebTrace(trace);
            FinishWebFetch();
            onDone?.Invoke(true);
        }

        // 1. Candidate URLs: exactly one for url= / result=, up to MaxPageSearchAttempts ranked hits for query=.
        var urls = new List<string>();
        var labels = new List<string>();
        if (!string.IsNullOrEmpty(req.Url))
        {
            urls.Add(req.Url.Trim());
            labels.Add(null);
        }
        else if (!string.IsNullOrEmpty(req.ResultToken))
        {
            WebSearchSession session; int index; string err;
            if (!TryResolveWebSearchToken(req.ResultToken, out session, out index, out err))
            {
                Fail("Result lookup failed: " + err, "(web_page result=" + Q(req.ResultToken) + " failed: " + err + ".)");
                yield break;
            }
            if (session.Kind != WebSearchKind.Web)
            {
                string kindLabel = BraveSearchClient.KindLabel(session.Kind);
                Fail("Result " + req.ResultToken + " is a " + kindLabel + " result, not a page. Use web_search kind=\"web\" or pass url=.",
                    "(web_page result=" + Q(req.ResultToken) + " is a " + kindLabel + " result, not a page; only web_search kind=\"web\" results can be read. Use url= or web_search kind=\"web\".)");
                yield break;
            }
            var r = session.Response.Web[index - 1];
            urls.Add(r.Url);
            labels.Add(r.Title);
            trace.AppendLine("Using " + session.Id + " result " + index + ": " + (r.Title ?? "") + " | " + r.Url);
        }
        else
        {
            BraveSearchClient.SearchResponse resp = null;
            yield return BraveSearchClient.Search(WebSearchKind.Web, req.Query, 10, req.SafeSearch, x => resp = x);
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
            BuildWebSearchTraceLines(trace, WebSearchKind.Web, req.Query, resp, listResults: false);
            if (resp == null || !resp.Success)
            {
                // Adds the red Error bubble and requests a continue for key / quota problems itself.
                _infoMessages.Add(new InfoMessage(ReportWebSearchFailure("web_page", req.Query, resp)));
                EndWebTrace(trace);
                FinishWebFetch();
                onDone?.Invoke(true);
                yield break;
            }
            var session = StoreWebSearchSession(WebSearchKind.Web, req.Query, resp);
            if (resp.Web == null || resp.Web.Count == 0)
            {
                Fail("No web results.", "(web_page " + Q(req.Query) + ": Brave returned no web results. Try different words or a direct url=.)");
                yield break;
            }
            trace.AppendLine("Stored as " + session.Id + " (web_page result=\"" + session.Id + ":N\" reads a different hit).");
            var ranked = RankWebPageResults(resp.Web, req.Query);
            var order = new StringBuilder();
            for (int i = 0; i < ranked.Count; i++)
            {
                if (i > 0) order.Append(", ");
                order.Append(ranked[i].Key + 1).Append(" (").Append(ranked[i].Value >= 0 ? "+" : "").Append(ranked[i].Value).Append(')');
                if (urls.Count < WebRequestLimits.MaxPageSearchAttempts)
                {
                    urls.Add(resp.Web[ranked[i].Key].Url);
                    labels.Add(resp.Web[ranked[i].Key].Title);
                }
            }
            trace.AppendLine("Fetch order by source quality: " + order);
            AIChatLog.Note("web_page_ranking", "Fetch order by source quality: " + order);
        }

        // 2. Fetch. Only SEARCH fallbacks are tried (the next ranked hit when a fetch fails or is
        //    not a page); links inside a fetched page are never followed.
        WebMediaDownloader.DownloadResult dl = null;
        string usedUrl = null;
        string lastFailure = null;
        for (int i = 0; i < urls.Count; i++)
        {
            string url = urls[i];
            string reason;
            if (!WebMediaDownloader.IsAllowedPublicHttpUrl(url, out reason))
            {
                trace.AppendLine("Skip " + url + ": " + reason);
                lastFailure = reason;
                continue;
            }
            trace.AppendLine("GET " + url + (string.IsNullOrEmpty(labels[i]) ? "" : "  (" + labels[i] + ")"));
            WebMediaDownloader.DownloadResult r = null;
            yield return DownloadPageWithTrace(url, trace, x => r = x);
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
            if (r == null || !r.Success)
            {
                lastFailure = r != null ? r.Error : "no response";
                trace.AppendLine("  -> " + lastFailure + (queryMode && i + 1 < urls.Count ? "; trying the next result" : ""));
                continue;
            }
            string ct = r.ContentType ?? "";
            bool isMedia = WebMediaDownloader.IsImageKind(r.Kind) || WebMediaDownloader.IsVideoKind(r.Kind);
            bool textLike = !isMedia && (ct == "text/html" || ct == "application/xhtml+xml" || ct == "text/plain" || ct == "text/xml"
                || ct == "application/xml" || ct == "application/json"
                || ((ct.Length == 0 || ct == "application/octet-stream") && r.Kind == WebMediaDownloader.MediaKind.Html));
            if (!textLike)
            {
                string what = isMedia ? WebMediaDownloader.KindLabel(r.Kind) : (ct.Length == 0 ? "an unknown binary type" : ct);
                string hint = WebMediaDownloader.IsImageKind(r.Kind) ? "use web_image url=\"...\" for it"
                    : WebMediaDownloader.IsVideoKind(r.Kind) ? "use web_video url=\"...\" for it"
                    : ct == "application/pdf" ? "PDFs cannot be read" : "only HTML / plain-text pages can be read";
                trace.AppendLine("  -> HTTP " + r.HttpStatus + " " + what + " " + r.Bytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes: not a readable page; " + hint);
                lastFailure = "that URL is " + what + "; " + hint;
                if (queryMode && i + 1 < urls.Count) { trace.AppendLine("  trying the next result"); continue; }
                Fail(null, "(web_page " + sourceText + ": that URL is " + what + "; " + hint + ".)");
                yield break;
            }
            dl = r;
            usedUrl = url;
            break;
        }
        if (dl == null)
        {
            Fail("Failed in " + FormatSeconds(Time.realtimeSinceStartup - started) + ".",
                "(web_page " + sourceText + " failed: " + (lastFailure ?? "no readable result") + ". Do not retry the same URL blindly; try another url= or a different query.)");
            yield break;
        }

        // 3. Decode + extract off the main thread (pure C#; touches no Unity objects).
        bool plain = dl.ContentType == "text/plain" || dl.ContentType == "application/json";
        string charsetUsed = null;
        WebPageExtraction ex = null;
        string extractError = null;
        int maxChars = req.MaxChars;
        int maxImages = req.Images ? req.MaxImages : 0;
        bool wantImages = req.Images;
        byte[] data = dl.Data;
        string httpCharset = dl.Charset;
        string pageUrl = usedUrl;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string cs;
                string html = WebPageReader.DecodeHtml(data, httpCharset, out cs);
                charsetUsed = cs;
                WebPageExtraction e;
                if (plain)
                {
                    int cut;
                    string body = html.Trim();
                    e = new WebPageExtraction { Scope = "text", Title = pageUrl, TotalChars = body.Length };
                    e.Text = WebPageReader.StripActionTags(WebPageReader.TruncateAtBoundary(body, maxChars, out cut));
                    e.Truncated = cut > 0;
                    e.TruncatedChars = cut;
                }
                else e = WebPageReader.Extract(html, new Uri(pageUrl), maxChars, maxImages, wantImages);
                e.Charset = cs;
                ex = e;
            }
            catch (Exception e) { extractError = e.GetType().Name + ": " + e.Message; }
        });
        trace.SetStatus("  extracting text...");
        while (!task.IsCompleted) yield return null;
        trace.ClearStatus();
        if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
        trace.AppendLine("  -> HTTP " + dl.HttpStatus + " " + dl.ContentType + " " + dl.Bytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            + " bytes in " + FormatSeconds(dl.ElapsedSeconds) + " (" + (charsetUsed ?? "?") + ")");
        if (ex == null)
        {
            Fail("  -> extraction failed: " + extractError, "(web_page " + sourceText + ": could not extract text (" + extractError + ").)");
            yield break;
        }
        if (string.IsNullOrWhiteSpace(ex.Text) || ex.TotalChars < 40)
        {
            Fail("  -> no readable text (" + ex.TotalChars + " chars; the page is probably rendered by JavaScript or is an anti-bot page).",
                "(web_page " + sourceText + ": the page had no readable text - likely rendered by JavaScript or an anti-bot page. Try another source or a direct url= to a plain article.)");
            yield break;
        }

        // 4. Page session + trace (URL, status, bytes, char counts, image list; the text itself goes to the log).
        string host = SafeHost(usedUrl);
        var page = new WebPageSession
        {
            Id = NextWebPageId(),
            Url = usedUrl,
            Title = string.IsNullOrEmpty(ex.Title) ? usedUrl : ex.Title,
            Images = ex.Images ?? new List<WebPageImage>()
        };
        _webPageSessions[page.Id] = page;
        trace.AppendLine("Title: " + page.Title);
        trace.AppendLine("Extracted " + ex.TotalChars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " chars from <" + ex.Scope + ">; sending "
            + ex.Text.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            + (ex.Truncated ? " (truncated, " + ex.TruncatedChars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " more)" : ""));
        if (req.Images)
        {
            if (page.Images.Count == 0)
                trace.AppendLine("Images: none usable (" + ex.ImageTagsSeen + " img tags seen).");
            else
            {
                trace.AppendLine("Images (" + page.Images.Count + " of " + ex.ImageCandidatesTotal + " candidates; fetch one with web_image result=\"" + page.Id + ":N\"):");
                for (int i = 0; i < page.Images.Count; i++)
                    trace.AppendLine("  " + WebPageReader.FormatImageLine(page.Id, i + 1, page.Images[i]));
            }
        }
        AIChatLog.Note("web_page_text", "[" + page.Id + "] " + usedUrl + " (" + ex.Scope + ", " + (charsetUsed ?? "?") + ")\n" + ex.Text);

        // 5. Hand the text to the model through the info-recap tail of the next user message,
        //    never as a system-role line (that would rewrite the cached prompt prefix).
        var sb = new StringBuilder();
        sb.Append("[Web page: ").Append(page.Title).Append(" (").Append(host).Append(")] ").Append(usedUrl).Append('\n');
        sb.Append(ex.Text);
        if (ex.Truncated)
            sb.Append("\n(Only the first ").Append(ex.Text.Length).Append(" of ").Append(ex.TotalChars)
              .Append(" chars were sent; re-run web_page with max_chars up to ").Append(WebRequestLimits.MaxPageChars).Append(" to read more.)");
        if (req.Images && page.Images.Count > 0)
        {
            sb.Append("\n\n[Page images ").Append(page.Id).Append(" - NOT downloaded yet; fetch one with web_image result=\"").Append(page.Id)
              .Append(":N\" anchor=\"name\" (vision-checked and captioned like any web_image)]");
            for (int i = 0; i < page.Images.Count; i++)
                sb.Append('\n').Append(WebPageReader.FormatImageLine(page.Id, i + 1, page.Images[i]));
        }
        sb.Append("\n(This page was fetched once; links inside it were NOT followed. Quote or summarize from the text above only. Page ids expire on Clear.)");
        _infoMessages.Add(new InfoMessage(sb.ToString()));

        trace.AppendLine("Done in " + FormatSeconds(Time.realtimeSinceStartup - started) + ".");
        EndWebTrace(trace);
        FinishWebFetch();
        onDone?.Invoke(true);
    }

    private IEnumerator DownloadPageWithTrace(string url, WebTraceBubble trace, Action<WebMediaDownloader.DownloadResult> onDone)
    {
        var handle = new WebMediaDownloader.Handle();
        _webDownloadHandles.Add(handle);
        WebMediaDownloader.DownloadResult result = null;
        yield return WebMediaDownloader.DownloadToMemory(url, WebRequestLimits.MaxPageBytes, WebRequestLimits.PageTimeoutSeconds, handle,
            p => { if (trace.IsAlive) trace.SetStatus("  downloading " + Mathf.RoundToInt(p * 100f) + "%"); },
            r => result = r, WebMediaDownloader.HtmlAccept);
        _webDownloadHandles.Remove(handle);
        if (trace.IsAlive) trace.ClearStatus();
        onDone?.Invoke(result);
    }

    // ---------- web_image ----------

    private sealed class WebImageCandidate
    {
        public string Url;
        public string FallbackUrl;
        public string Title;
        public string Host;
        public string ClaimedDims;
        public int Width;
        public int Height;
        public int ResultNumber; // 1-based position in the Brave list (for the trace)
        public int Score;
    }

    // Hosts that almost never yield a usable reference PHOTO: AI-art generators, clipart /
    // PNG-cutout sites, wallpaper farms, stock sites (watermarks), merchandise shops.
    private static readonly string[] WebImageJunkHosts =
    {
        "craiyon.com", "deviantart.com", "artstation.com", "clipart-library.com", "clipartmax.com", "pngitem.com",
        "pngegg.com", "pngwing.com", "pngkey.com", "pngkit.com", "freepik.com", "vecteezy.com", "vectorstock.com",
        "peakpx.com", "wallpapercave.com", "wallpaperflare.com", "wallpaperaccess.com", "hdqwalls.com", "wallhere.com",
        "gettyimages.com", "shutterstock.com", "alamy.com", "dreamstime.com", "istockphoto.com", "123rf.com", "depositphotos.com",
        "redbubble.com", "etsy.com", "amazon.com", "ebay.com", "walmart.com", "aliexpress.com", "teepublic.com", "zazzle.com",
        "pinterest.com", "pinimg.com", "tenor.com", "giphy.com", "imgflip.com", "knowyourmeme.com", "lexica.art", "openart.ai",
        "midjourney.com", "civitai.com", "playground.com", "nightcafe.studio", "fandom.com"
    };
    private static readonly string[] WebImageJunkTitleWords =
    {
        "clipart", "clip art", "cartoon", "caricature", "drawing", "illustration", "vector", "transparent background", "png",
        "meme", "wallpaper", "poster", "print", "t-shirt", "tshirt", "mug", "sticker", "ai generated", "ai-generated", "ai art",
        "craiyon", "midjourney", "stable diffusion", "dall-e", "fan art", "fanart", "cosplay", "costume", "funko", "lego",
        "statue", "wax figure", "figurine", "action figure", "doll", "mask", "coloring", "sketch", "painting", "anime",
        "comment image", "thumbnail"
    };
    private static readonly string[] WebImageGoodTitleWords =
    {
        "official portrait", "official photo", "headshot", "press photo", "photograph", "photo of", "portrait of", "pictured", "attends", "arrives"
    };

    /// <summary>
    /// Cheap pre-ranking from metadata only, so the expensive download + vision check runs on
    /// the most promising results first: Wikimedia / official sources up, AI-art / clipart /
    /// wallpaper / stock / merch hosts and tell-tale title words down. Stable: ties keep
    /// Brave's order.
    /// </summary>
    private static void RankWebImageCandidates(List<WebImageCandidate> candidates)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            int score = 0;
            string host = (c.Host ?? "").ToLowerInvariant();
            string url = (c.Url ?? "").ToLowerInvariant();
            string title = (c.Title ?? "").ToLowerInvariant();

            if (host.Contains("wikimedia.org") || host.Contains("wikipedia.org") || url.Contains("upload.wikimedia.org")) score += 5;
            else if (host.EndsWith(".gov") || host.EndsWith(".edu") || host.EndsWith(".mil") || host.Contains("britannica.com") || host.Contains("biography.com")) score += 3;
            else if (host.Contains("imdb.com") || host.Contains("media-amazon.com") && url.Contains("/images/m/")) score += 1;

            foreach (string junk in WebImageJunkHosts)
            {
                if (host.EndsWith(junk) || host.Contains(junk) || url.Contains(junk)) { score -= 4; break; }
            }
            foreach (string word in WebImageJunkTitleWords)
            {
                if (title.Contains(word) || url.Contains(word.Replace(' ', '-'))) { score -= 3; break; }
            }
            foreach (string word in WebImageGoodTitleWords)
            {
                if (title.Contains(word)) { score += 1; break; }
            }
            if (url.EndsWith(".png") && !host.Contains("wikimedia")) score -= 1; // cutouts / graphics more often than photos
            if (c.Width >= 600 && c.Height >= 600) score += 1;
            else if (c.Width > 0 && (c.Width < 300 || c.Height < 300)) score -= 1;
            if (c.Width > 0 && c.Height > 0)
            {
                float aspect = (float)c.Width / c.Height;
                if (aspect > 2.6f || aspect < 0.35f) score -= 2; // banners / strips
            }
            c.Score = score;
        }
        // Stable sort by score descending.
        var ordered = new List<WebImageCandidate>(candidates);
        ordered.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : a.ResultNumber.CompareTo(b.ResultNumber);
        });
        candidates.Clear();
        candidates.AddRange(ordered);
    }

    private static bool HasVisionLLM()
    {
        var mgr = LLMInstanceManager.Get();
        return mgr != null && mgr.GetInstanceCount() > 0 && mgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0;
    }

    /// <summary>
    /// One vision call that both screens a downloaded web image for suitability as a
    /// reference and produces the normal SHORT/LONG caption (so an accepted image needs no
    /// second caption call). The VERDICT/REASON lines precede SHORT/LONG so the existing
    /// caption parser ignores them.
    /// </summary>
    private string BuildWebImageVerifyPrompt(string query, string criteria)
    {
        string captionPrompt = _skillManager != null && !string.IsNullOrWhiteSpace(_skillManager.CaptionPrompt)
            ? _skillManager.CaptionPrompt
            : DefaultCaptionPrompt;
        var sb = new StringBuilder();
        sb.Append("You are screening an image that was downloaded from a web image search for the query ").Append(Q(query ?? "")).Append('.');
        if (!string.IsNullOrWhiteSpace(criteria))
            sb.Append(" Additional requirements from the requester: ").Append(criteria.Trim()).Append('.');
        sb.AppendLine();
        sb.AppendLine("Decide whether it is a USABLE REFERENCE PHOTO of that subject for an image/video generator, then describe it.");
        sb.AppendLine("USABLE means: a real photograph in which the queried subject is the clear main subject, reasonably large in frame, in focus, with nothing covering it; for a person the face must be clearly visible. A film/TV scene still, a publicity still, an event or press photo, or an official portrait photo all COUNT as usable when the subject is the clear main subject - other objects, props, or a second person in the background are fine.");
        sb.AppendLine("REJECT (UNSUITABLE) when any of these apply: the subject is wrong or absent; the image itself is AI-generated art, a caricature, cartoon, drawing, painting, sculpture, clipart, vector, meme, poster, product mockup or merchandise (a real photo that merely CONTAINS a painting or poster elsewhere in frame is fine if the real subject is still the clear main subject); it is a photo OF a framed picture, screen, TV, phone, newspaper, magazine or wall display where the subject only appears inside that reproduction; the subject is small, heavily cropped, turned away, blurred, or just one face in a crowd; heavy text, logos or watermarks cover the subject; the expression, costume or edit is extreme or comedic unless the query asked for that.");
        sb.AppendLine("When in doubt about reproductions, art, or the wrong person, answer UNSUITABLE; do not reject a clear real photo of the right subject just because the scene is busy.");
        sb.AppendLine("Naming rule for the caption: describe people by what is VISIBLE (age, build, hair, face, clothing). The only name you may use is the subject named in the query, and only for the person who visibly matches it; never assign other real names or character names from general knowledge of the show, film, or person, and never contradict your own VERDICT/REASON in the caption.");
        sb.AppendLine();
        sb.AppendLine("Return exactly these lines, in this order, with these labels:");
        sb.AppendLine("VERDICT: SUITABLE or UNSUITABLE");
        sb.AppendLine("REASON: <one sentence saying why>");
        sb.AppendLine("Then the two caption lines described next.");
        sb.AppendLine();
        sb.Append(captionPrompt.Trim());
        return sb.ToString();
    }

    private static bool TryParseWebImageVerdict(string raw, out bool suitable, out string reason)
    {
        suitable = false;
        reason = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = Regex.Match(raw, @"VERDICT\s*\**\s*:\s*\**\s*(UN)?SUITABLE", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            // Tolerate a bare first word.
            var first = Regex.Match(raw.TrimStart(), @"^\**\s*(UN)?SUITABLE\b", RegexOptions.IgnoreCase);
            if (!first.Success) return false;
            suitable = !first.Groups[1].Success;
        }
        else
        {
            suitable = !m.Groups[1].Success;
        }
        var r = Regex.Match(raw, @"REASON\s*\**\s*:\s*\**\s*(.+)", RegexOptions.IgnoreCase);
        if (r.Success)
        {
            reason = r.Groups[1].Value.Trim();
            int nl = reason.IndexOf('\n');
            if (nl >= 0) reason = reason.Substring(0, nl).Trim();
            reason = reason.TrimEnd('*').Trim();
        }
        return true;
    }

    private sealed class WebImageVerifyResult
    {
        public bool Completed;
        public bool Verified;      // a verdict was parsed
        public bool Suitable;
        public string Reason;
        public CaptionResult Caption;
        public string FailureDetail; // backend error when the vision call failed outright
        public string NoVerdictText => string.IsNullOrEmpty(FailureDetail)
            ? "vision check returned no verdict (accepting unverified)"
            : "vision check FAILED: " + FailureDetail + " (accepting unverified; fix the vision instance in LLM Settings)";
    }

    /// <summary>Run the combined verify+caption vision call; waits for a free vision slot.</summary>
    private IEnumerator VerifyWebImageCoroutine(byte[] png, string query, string criteria, WebImageVerifyResult outResult)
    {
        string prompt = BuildWebImageVerifyPrompt(query, criteria);
        string rawText = null;
        CaptionJob job = null;
        // Same capacity gate as WaitForPicAndCaption: don't over-subscribe a single local vision model.
        float waitStart = Time.realtimeSinceStartup;
        while (true)
        {
            var mgr = LLMInstanceManager.Get();
            bool visionBusy = mgr != null
                && !mgr.IsAnyLLMFree(isSmallJob: false, isVisionJob: true)
                && mgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0;
            if (!visionBusy || Time.realtimeSinceStartup - waitStart > 300f) break;
            yield return new WaitForSeconds(0.5f);
        }
        job = TryCaptionBytes(png, r => { outResult.Caption = r; outResult.Completed = true; },
            requireFreeSlot: false, promptOverride: prompt, jobName: "WebImageVerify", debugFileName: "web_image_verify_sent.json",
            onRawText: t => rawText = t,
            onFailureDetail: d => outResult.FailureDetail = d);
        while (!outResult.Completed)
            yield return null;
        bool suitable; string reason;
        if (TryParseWebImageVerdict(rawText, out suitable, out reason))
        {
            outResult.Verified = true;
            outResult.Suitable = suitable;
            outResult.Reason = reason;
        }
    }

    bool IChatHost.StartWebImageAction(SkillAction action, WebImageRequest request, Action<bool> onDone)
    {
        if (request == null) return false;
        int epoch = _webFetchEpoch;
        BeginWebFetch();
        StartCoroutine(WebImageCoroutine(action, request, epoch, onDone));
        return true;
    }

    private IEnumerator WebImageCoroutine(SkillAction action, WebImageRequest req, int epoch, Action<bool> onDone)
    {
        string safe = req.SafeSearch ?? (Config.Get() != null ? Config.Get().GetWebSearchSafeSearch() : "strict");
        string sourceText = !string.IsNullOrEmpty(req.Url) ? "url=" + Q(req.Url)
            : !string.IsNullOrEmpty(req.ResultToken) ? "result=" + Q(req.ResultToken)
            : "query=" + Q(req.Query);
        var trace = BeginWebTrace("web_image  " + sourceText + "  count=" + req.Count + "  min_width=" + req.MinWidth + "  safesearch=" + safe
            + (string.IsNullOrEmpty(req.Anchor) ? "" : "  anchor=" + Q(req.Anchor)));
        float started = Time.realtimeSinceStartup;
        string queryForProvenance = req.Query;

        // 1. Build the candidate list.
        var candidates = new List<WebImageCandidate>();
        if (!string.IsNullOrEmpty(req.Url))
        {
            candidates.Add(new WebImageCandidate { Url = req.Url.Trim(), Host = SafeHost(req.Url) });
        }
        else if (!string.IsNullOrEmpty(req.ResultToken) && IsWebPageToken(req.ResultToken))
        {
            // "P1:3": an image listed by web_page. Same download / vision-verify / caption /
            // anchor path as any other candidate; the verify prompt's subject is the page
            // title plus the image's alt or caption.
            WebPageSession page; int pIndex; string pError;
            if (!TryResolveWebPageToken(req.ResultToken, out page, out pIndex, out pError))
            {
                trace.AppendLine("Result lookup failed: " + pError);
                _infoMessages.Add(new InfoMessage("(web_image result=" + Q(req.ResultToken) + " failed: " + pError + ".)"));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            var pimg = page.Images[pIndex - 1];
            queryForProvenance = ((page.Title ?? "") + (string.IsNullOrEmpty(pimg.Alt) ? "" : " - " + pimg.Alt)).Trim();
            if (queryForProvenance.Length == 0) queryForProvenance = page.Url;
            // Width stays 0 on purpose: a declared <img width> is the DISPLAY size (Wikipedia
            // thumbs say 250) and the claimed-width pre-skip below would reject a 5850 px original.
            candidates.Add(new WebImageCandidate
            {
                Url = pimg.Url,
                Title = string.IsNullOrEmpty(pimg.Alt) ? page.Title : pimg.Alt,
                Host = SafeHost(pimg.Url),
                ClaimedDims = pimg.Width > 0 && pimg.Height > 0 ? pimg.Width + "x" + pimg.Height : null,
                ResultNumber = pIndex
            });
            trace.AppendLine("Using page " + page.Id + " image " + pIndex + ": " + (pimg.Alt ?? "") + " | " + pimg.Url
                + (string.IsNullOrEmpty(pimg.Note) ? "" : "  (" + pimg.Note + ")"));
        }
        else if (!string.IsNullOrEmpty(req.ResultToken))
        {
            WebSearchSession session; int index; string tokenError;
            if (!TryResolveWebSearchToken(req.ResultToken, out session, out index, out tokenError))
            {
                trace.AppendLine("Result lookup failed: " + tokenError);
                _infoMessages.Add(new InfoMessage("(web_image result=" + Q(req.ResultToken) + " failed: " + tokenError + ".)"));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            queryForProvenance = session.Query;
            if (session.Kind == WebSearchKind.Images)
            {
                var r = session.Response.Images[index - 1];
                candidates.Add(new WebImageCandidate { Url = string.IsNullOrEmpty(r.ImageUrl) ? r.ThumbnailUrl : r.ImageUrl, FallbackUrl = r.ThumbnailUrl, Title = r.Title, Host = r.Host, ClaimedDims = r.DimsText });
            }
            else if (session.Kind == WebSearchKind.Videos)
            {
                var r = session.Response.Videos[index - 1];
                candidates.Add(new WebImageCandidate { Url = r.ThumbnailUrl, Title = r.Title + " (video thumbnail)", Host = r.Host });
            }
            else
            {
                trace.AppendLine("Result " + req.ResultToken + " is a web page, not an image. Read it with web_page result=\"" + req.ResultToken + "\" and pick one of its P-images, or use web_search kind=\"images\".");
                _infoMessages.Add(new InfoMessage("(web_image result=" + Q(req.ResultToken) + " is a page result, not an image. Use web_page result=\"" + req.ResultToken + "\" first, then web_image result=\"P<n>:<i>\" from its image list.)"));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            trace.AppendLine("Using " + session.Id + " result " + index + ": " + (candidates[0].Title ?? "") + " | " + candidates[0].Url);
        }
        else
        {
            // One request costs the same regardless of count; a deeper list gives the ranker and
            // the vision check more to choose from when the top hits are junk.
            int searchCount = WebRequestLimits.MaxSearchCount;
            BraveSearchClient.SearchResponse resp = null;
            yield return BraveSearchClient.Search(WebSearchKind.Images, req.Query, searchCount, req.SafeSearch, r => resp = r);
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

            BuildWebSearchTraceLines(trace, WebSearchKind.Images, req.Query, resp, listResults: false);
            if (resp == null || !resp.Success)
            {
                _infoMessages.Add(new InfoMessage(ReportWebSearchFailure("web_image", req.Query, resp)));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            StoreWebSearchSession(WebSearchKind.Images, req.Query, resp);
            for (int i = 0; i < resp.Images.Count; i++)
            {
                var r = resp.Images[i];
                candidates.Add(new WebImageCandidate
                {
                    Url = string.IsNullOrEmpty(r.ImageUrl) ? r.ThumbnailUrl : r.ImageUrl,
                    FallbackUrl = r.ThumbnailUrl,
                    Title = r.Title,
                    Host = r.Host,
                    ClaimedDims = r.DimsText,
                    Width = r.Width,
                    Height = r.Height,
                    ResultNumber = i + 1
                });
            }
            RankWebImageCandidates(candidates);
            var order = new StringBuilder("Download order by source quality: ");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0) order.Append(", ");
                order.Append(candidates[i].ResultNumber).Append(" (").Append(candidates[i].Score >= 0 ? "+" : "").Append(candidates[i].Score).Append(')');
            }
            AIChatLog.Note("web_ranking", order.ToString());
        }

        bool verify = req.Verify && HasVisionLLM();
        if (req.Verify && !verify)
            trace.AppendLine("No vision-capable LLM is active: downloads will NOT be checked for suitability.");
        else if (verify)
            trace.AppendLine("Each download is checked by the vision LLM for suitability" + (string.IsNullOrWhiteSpace(req.Criteria) ? "" : " (criteria: " + req.Criteria.Trim() + ")") + "; unsuitable ones are skipped.");

        if (candidates.Count == 0)
        {
            trace.AppendLine("No image results.");
            _infoMessages.Add(new InfoMessage("(web_image " + Q(req.Query) + ": no results. Try a different query or a direct url=.)"));
            EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
        }

        // 2. Try candidates in order until `count` usable images were added.
        var added = new List<int>();
        var addedPics = new List<PicMain>();
        int attempts = 0;
        int failHttp = 0, failNotImage = 0, failSmall = 0, failOther = 0, failUnsuitable = 0;
        int maxAttempts = Mathf.Min(candidates.Count, Mathf.Max(req.Count, WebRequestLimits.MaxImageCandidates));

        for (int ci = 0; ci < candidates.Count && added.Count < req.Count && attempts < maxAttempts; ci++)
        {
            var cand = candidates[ci];
            if (string.IsNullOrEmpty(cand.Url)) continue;
            // Brave reports the size for most results; a claimed width already under min_width
            // is not worth a download + vision call (thumbnail-only shops, 140 px eBay tiles).
            if (cand.Width > 0 && cand.Width < req.MinWidth && string.IsNullOrEmpty(req.Url))
            {
                trace.AppendLine("Skip " + cand.Url + ": claimed " + cand.ClaimedDims + " is narrower than min_width " + req.MinWidth + " (not downloaded)");
                failSmall++;
                continue;
            }
            attempts++;
            string anchorName = string.IsNullOrEmpty(req.Anchor) ? null : (added.Count == 0 ? req.Anchor : req.Anchor + "_" + (added.Count + 1));

            // Dedupe: the same URL already lives in chat this session.
            PicMain existing;
            if (_webFetchedUrlToPic.TryGetValue(cand.Url, out existing) && existing != null && existing.gameObject != null && _chatImagePics.Contains(existing))
            {
                int existingIdx = _chatImagePics.IndexOf(existing) + 1;
                trace.AppendLine("Download " + attempts + "/" + candidates.Count + ": " + cand.Url);
                trace.AppendLine("  -> already fetched this session as #" + existingIdx + ", reusing it" + (anchorName != null ? " (anchor " + Q(anchorName) + ")" : ""));
                if (anchorName != null) _anchors[anchorName] = existing;
                added.Add(existingIdx);
                addedPics.Add(existing);
                ((IChatHost)this).SetLastSpawnedPicForTurn(existing);
                continue;
            }

            trace.AppendLine("Download " + attempts + "/" + candidates.Count + ": " + cand.Url
                + (string.IsNullOrEmpty(cand.ClaimedDims) || cand.ClaimedDims == "?x?" ? "" : "  (claimed " + cand.ClaimedDims + ")"));

            WebMediaDownloader.DownloadResult dl = null;
            string usedUrl = cand.Url;
            yield return DownloadImageWithTrace(cand.Url, trace, epoch, r => dl = r);
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

            bool usable = dl != null && dl.Success && WebMediaDownloader.IsImageKind(dl.Kind);
            if (!usable && !string.IsNullOrEmpty(cand.FallbackUrl) && !string.Equals(cand.FallbackUrl, cand.Url, StringComparison.OrdinalIgnoreCase))
            {
                trace.AppendLine("  -> " + DescribeDownloadFailure(dl) + "; retry via Brave thumbnail " + cand.FallbackUrl);
                usedUrl = cand.FallbackUrl;
                yield return DownloadImageWithTrace(cand.FallbackUrl, trace, epoch, r => dl = r);
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
                usable = dl != null && dl.Success && WebMediaDownloader.IsImageKind(dl.Kind);
            }

            if (!usable)
            {
                trace.AppendLine("  -> " + DescribeDownloadFailure(dl) + ", skipped");
                if (dl != null && dl.Success) failNotImage++; else failHttp++;
                continue;
            }

            trace.SetStatus("  converting...");
            WebImageConverter.Result conv = null;
            yield return WebImageConverter.NormalizeToLoadableImage(dl.Data, dl.Kind, WebRequestLimits.MaxImageSide, r => conv = r);
            trace.ClearStatus();
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

            string httpLine = "  -> HTTP " + dl.HttpStatus + " " + (string.IsNullOrEmpty(dl.ContentType) ? "" : dl.ContentType + " ") + dl.Bytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes in " + FormatSeconds(dl.ElapsedSeconds);
            if (conv == null || !conv.Success)
            {
                trace.AppendLine(httpLine + ", " + WebMediaDownloader.KindLabel(dl.Kind) + " -> " + (conv != null ? conv.Error : "conversion failed") + ", skipped");
                failOther++;
                continue;
            }
            if (conv.Width < req.MinWidth)
            {
                trace.AppendLine(httpLine + ", " + conv.Note + " is narrower than min_width " + req.MinWidth + ", skipped");
                try { System.IO.File.Delete(conv.Path); } catch { }
                failSmall++;
                continue;
            }

            // Vision suitability check (also yields the caption) BEFORE the image enters chat,
            // so a wall of framed portraits, a photo of a screen, or an AI caricature never
            // becomes a reference. Unverifiable (no verdict / timeout) falls through as accepted.
            WebImageVerifyResult verdict = null;
            if (verify && conv.PngBytes != null && conv.PngBytes.Length > 0)
            {
                trace.AppendLine(httpLine + ", " + conv.Note);
                trace.SetStatus("  checking suitability with the vision LLM...");
                verdict = new WebImageVerifyResult();
                yield return VerifyWebImageCoroutine(conv.PngBytes, string.IsNullOrEmpty(queryForProvenance) ? (cand.Title ?? "") : queryForProvenance, req.Criteria, verdict);
                trace.ClearStatus();
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

                if (verdict.Verified && !verdict.Suitable)
                {
                    trace.AppendLine("  -> vision check: UNSUITABLE" + (string.IsNullOrEmpty(verdict.Reason) ? "" : " - " + verdict.Reason) + ", skipped");
                    try { System.IO.File.Delete(conv.Path); } catch { }
                    failUnsuitable++;
                    continue;
                }
                if (verdict.Verified)
                    trace.AppendLine("  -> vision check: SUITABLE" + (string.IsNullOrEmpty(verdict.Reason) ? "" : " - " + verdict.Reason));
                else
                    trace.AppendLine("  -> " + verdict.NoVerdictText);
                httpLine = "  saved " + System.IO.Path.GetFileName(conv.Path);
            }

            var imageGen = ImageGenerator.Get();
            GameObject go = imageGen != null ? imageGen.AddImageByFileName(conv.Path) : null;
            PicMain pic = go != null ? go.GetComponent<PicMain>() : null;
            if (pic == null)
            {
                trace.AppendLine(httpLine + ", " + conv.Note + ", but the image could not be loaded into a Pic, skipped");
                failOther++;
                continue;
            }

            string provenance = string.IsNullOrEmpty(queryForProvenance)
                ? "web image: " + ShortUrlForProvenance(usedUrl)
                : "web image: " + Q(queryForProvenance) + " -> " + ShortUrlForProvenance(usedUrl);
            string dims = conv.Width + "x" + conv.Height;
            int idx = AppendWebStillBubble(pic, action, dims, provenance, anchorName);
            _webFetchedUrlToPic[cand.Url] = pic;
            if (!string.Equals(usedUrl, cand.Url, StringComparison.OrdinalIgnoreCase)) _webFetchedUrlToPic[usedUrl] = pic;
            added.Add(idx);
            addedPics.Add(pic);

            bool captionFromVerdict = verdict != null && verdict.Completed && !verdict.Caption.IsEmpty;
            if (verdict != null)
                trace.AppendLine(httpLine + (captionFromVerdict ? "" : ", captioning..."));
            else
                trace.AppendLine(httpLine + ", " + conv.Note + ", saved " + System.IO.Path.GetFileName(conv.Path));
            trace.AppendLine("  -> added as #" + idx + (anchorName != null ? " (anchor " + Q(anchorName) + ")" : "") + (captionFromVerdict ? "" : ", captioning..."));

            string verifiedNote = verdict != null && verdict.Verified && verdict.Suitable
                ? " The vision check judged it a suitable reference" + (string.IsNullOrEmpty(verdict.Reason) ? "." : ": " + verdict.Reason)
                : (verdict != null && !string.IsNullOrEmpty(verdict.FailureDetail)
                    ? " WARNING: the vision check FAILED (" + verdict.FailureDetail + "), so this image is UNVERIFIED and has no caption; tell the user the vision LLM instance needs fixing in LLM Settings."
                    : " Its caption is in CHAT IMAGES - verify it shows the intended subject before using it as a <Picture N> reference.");
            // The recap names the bubble; the FULL description follows separately through
            // ApplyCaptionResultToPic -> ForwardFullDescriptionOnce (one time, cached history).
            // A Seinfeld test showed the model fetching four anchored cast photos and then
            // rendering Z-Image lookalike stills of "a man in his 40s with wavy hair" - the
            // references sat unused. Repeat the routing rule right where the anchor lands.
            string usageNote = anchorName != null
                ? " Use it as a REFERENCE SLOT (chat_image=" + Q(anchorName) + " / chat_image2.. on image_to_movie with Reference To Video (MiniMax H3) 5s.txt, video_to_video with Reference Video To Video, or a Klein edit) - never generate_image a lookalike from text."
                : "";
            string recap = "(web_image " + (string.IsNullOrEmpty(queryForProvenance) ? Q(usedUrl) : Q(queryForProvenance)) + " added #" + idx
                + " (" + dims + ", " + (string.IsNullOrEmpty(cand.Host) ? SafeHost(usedUrl) : cand.Host)
                + (anchorName != null ? ", anchor " + Q(anchorName) : "") + ")." + verifiedNote + usageNote + ")";
            _infoMessages.Add(new InfoMessage(recap));
            if (captionFromVerdict)
            {
                // The verify call already produced the SHORT/LONG caption; no second vision call.
                ApplyCaptionResultToPic(pic, verdict.Caption, "caption unavailable");
                string capShort = !string.IsNullOrWhiteSpace(verdict.Caption.shortCaption) ? verdict.Caption.shortCaption : verdict.Caption.longCaption;
                trace.AppendLine("#" + idx + " caption: " + Q((capShort ?? "").Trim()));
                if (!string.IsNullOrWhiteSpace(verdict.Caption.longCaption))
                    trace.AppendLine("#" + idx + " full description (sent to the AI with its next message): " + Q(verdict.Caption.longCaption.Trim()));
            }
            else
            {
                StartCoroutine(CaptionWebStillBubble(pic, trace, idx));
            }
        }

        // 3. Summary.
        float elapsed = Time.realtimeSinceStartup - started;
        if (added.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append("Done: ").Append(added.Count).Append(" of ").Append(req.Count).Append(added.Count == 1 ? " image" : " images").Append(" added (");
            for (int i = 0; i < added.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('#').Append(added[i]); }
            sb.Append(") in ").Append(FormatSeconds(elapsed)).Append('.');
            trace.AppendLine(sb.ToString());
            if (added.Count < req.Count)
                _infoMessages.Add(new InfoMessage("(web_image only found " + added.Count + " usable of the " + req.Count + " requested.)"));
        }
        else
        {
            string why = "(" + (failUnsuitable > 0 ? "rejected by vision check x" + failUnsuitable + " " : "") + (failHttp > 0 ? "download failed x" + failHttp + " " : "") + (failNotImage > 0 ? "not an image x" + failNotImage + " " : "")
                + (failSmall > 0 ? "too small x" + failSmall + " " : "") + (failOther > 0 ? "other x" + failOther + " " : "") + ")";
            trace.AppendLine("No usable image in " + attempts + " attempt" + (attempts == 1 ? "" : "s") + " " + why + " in " + FormatSeconds(elapsed) + ".");
            _infoMessages.Add(new InfoMessage("(web_image " + (string.IsNullOrEmpty(req.Query) ? sourceText : Q(req.Query)) + ": no usable image in " + attempts + " attempts " + why + ". Try a more specific query (add the person's full name, 'official portrait', the show/film name), a lower min_width, or a direct url=.)"));
        }

        EndWebTrace(trace);
        FinishWebFetch();
        onDone?.Invoke(true);
    }

    private static string SafeHost(string url)
    {
        try { return new Uri(url).Host; } catch { return ""; }
    }

    private static string DescribeDownloadFailure(WebMediaDownloader.DownloadResult dl)
    {
        if (dl == null) return "no response";
        if (dl.Success && !WebMediaDownloader.IsImageKind(dl.Kind))
            return "HTTP " + dl.HttpStatus + " " + (string.IsNullOrEmpty(dl.ContentType) ? "" : dl.ContentType + " ") + "(" + (dl.Kind == WebMediaDownloader.MediaKind.Html ? "a web page, not an image" : "not an image: " + WebMediaDownloader.KindLabel(dl.Kind)) + ")";
        return dl.Error ?? "failed";
    }

    private IEnumerator DownloadImageWithTrace(string url, WebTraceBubble trace, int epoch, Action<WebMediaDownloader.DownloadResult> onDone)
    {
        var handle = new WebMediaDownloader.Handle();
        _webDownloadHandles.Add(handle);
        WebMediaDownloader.DownloadResult result = null;
        yield return WebMediaDownloader.DownloadToMemory(url, WebRequestLimits.MaxImageBytes, WebRequestLimits.DownloadTimeoutSeconds, handle,
            p => { if (trace.IsAlive) trace.SetStatus("  downloading " + Mathf.RoundToInt(p * 100f) + "%"); },
            r => result = r);
        _webDownloadHandles.Remove(handle);
        if (trace.IsAlive) trace.ClearStatus();
        onDone?.Invoke(result);
    }

    /// <summary>
    /// Append a web-fetched image as an ASSISTANT still bubble: plain "#N" label,
    /// "web image" kind + URL provenance so CHAT IMAGES shows where it came from, anchor
    /// bound (an explicit name, so count>1 can bind name_2...), chain target updated,
    /// and its caption ALWAYS included in CHAT IMAGES (the model must be able to see what
    /// it downloaded even when generated-image auto-captioning is off).
    /// </summary>
    private int AppendWebStillBubble(PicMain pic, SkillAction action, string dimensions, string provenanceStep, string anchorName)
    {
        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        RegisterChatImageRecord(pic, action, isUserAttachment: false, isMovie: false, dimensions: dimensions);
        var record = _chatImageRecords.Count > 0 ? _chatImageRecords[_chatImageRecords.Count - 1] : null;
        if (record != null && record.pic == pic)
        {
            record.kind = "web image";
            record.anchorName = anchorName;
            record.alwaysIncludeCaption = true;
            record.provenanceSteps.Clear();
            if (!string.IsNullOrEmpty(provenanceStep))
                record.provenanceSteps.Add(provenanceStep);
        }
        AppendImageBubbleInternal(pic, $"#{chatImageNumber}", isMovie: false);

        if (!string.IsNullOrEmpty(anchorName))
        {
            _anchors[anchorName] = pic;
            Debug.Log($"AIChatPanel: anchor '{anchorName}' -> Image #{chatImageNumber} (web)");
        }
        MarkLatestAssistantMediaCheckpoint();
        ((IChatHost)this).SetLastSpawnedPicForTurn(pic);
        return chatImageNumber;
    }

    private IEnumerator CaptionWebStillBubble(PicMain pic, WebTraceBubble trace, int chatIndex)
    {
        if (pic == null || pic.gameObject == null) yield break;
        _webCaptionInFlight.Add(pic);
        RecomputeSendInteractable();
        UpdateWebFetchStatus(force: true);

        yield return WaitForPicAndCaption(pic);

        _webCaptionInFlight.Remove(pic);
        if (trace != null && trace.IsAlive)
        {
            bool alive = pic != null && pic.gameObject != null;
            string cap = alive ? (!string.IsNullOrWhiteSpace(pic.CaptionShort) ? pic.CaptionShort : pic.Caption) : null;
            trace.AppendLine("#" + chatIndex + " caption: " + (string.IsNullOrWhiteSpace(cap) ? "(none)" : Q(cap.Trim())));
            string longCap = alive ? pic.Caption : null;
            if (!string.IsNullOrWhiteSpace(longCap) && !string.Equals(longCap.Trim(), (cap ?? "").Trim(), StringComparison.Ordinal))
                trace.AppendLine("#" + chatIndex + " full description (sent to the AI with its next message): " + Q(longCap.Trim()));
        }
        RecomputeSendInteractable();
        UpdateWebFetchStatus(force: true);
        PokeAutoResumeSchedulers();
    }

    // ---------- web_video ----------

    private sealed class WebVideoCandidate
    {
        public string Url;
        public string Title;
        public string Host;
        public string DurationText;
        public double DurationSeconds;
        public int ResultNumber;
        public int Score;
    }

    // Titles that mean "people talking ABOUT the subject", not footage OF it.
    private static readonly string[] WebVideoJunkTitleWords =
    {
        "interview", "talks ", "talk show", "reveals", "secrets", "behind the scenes", "behind-the-scenes", "podcast", "reacts",
        "reaction", "explained", "theory", "ranked", "review", "breakdown", "cast reunion", "reunion", "rant", "apology",
        "apologizes", "documentary", "tribute", "remembering", "fan made", "fan-made", "ai generated", "ai-generated", "parody",
        "trailer", "news", "shares", "opens up", "discusses", "live stream", "livestream", "gameplay", "unboxing", "what happened to"
    };
    private static readonly string[] WebVideoGoodTitleWords =
    {
        "scene", "scenes", "clip", "clips", "best of", "moments", "compilation", "full episode", "entrance", "entrances",
        "highlights", "funniest", "official", "hd"
    };

    /// <summary>
    /// Cheap pre-ranking of video search results: footage OF the subject (scenes, clips, best-of
    /// compilations, official uploads) up; interviews / podcasts / reaction / explainer videos
    /// down; sources longer than the cap or shorter than the wanted clip excluded upstream.
    /// </summary>
    private static void RankWebVideoCandidates(List<WebVideoCandidate> candidates, string query, float wantedSeconds)
    {
        string[] queryWords = (query ?? "").ToLowerInvariant().Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            string title = (c.Title ?? "").ToLowerInvariant();
            string host = (c.Host ?? "").ToLowerInvariant();
            int score = 0;
            foreach (string w in WebVideoJunkTitleWords) { if (title.Contains(w)) { score -= 3; break; } }
            foreach (string w in WebVideoGoodTitleWords) { if (title.Contains(w)) { score += 1; break; } }
            int hits = 0;
            foreach (string w in queryWords) { if (w.Length >= 3 && title.Contains(w)) hits++; }
            score += Mathf.Min(3, hits);
            if (c.DurationSeconds > 0)
            {
                if (c.DurationSeconds < wantedSeconds + 1) score -= 5;              // too short to cut from
                else if (c.DurationSeconds <= 15 * 60) score += 1;                  // quick to download
            }
            if (host.Contains("tiktok.com")) score -= 1;
            c.Score = score;
        }
        var ordered = new List<WebVideoCandidate>(candidates);
        ordered.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : a.ResultNumber.CompareTo(b.ResultNumber);
        });
        candidates.Clear();
        candidates.AddRange(ordered);
    }

    /// <summary>
    /// Combined verdict + caption prompt for a cut clip (the vision LLM sees a contact sheet of
    /// sampled frames). Same VERDICT/REASON-then-SHORT/LONG shape as the image check.
    /// </summary>
    private static string BuildWebVideoVerifyPrompt(string query, string criteria)
    {
        var sb = new StringBuilder();
        sb.Append("You are screening a short video clip that was cut from a web video found for the search query ").Append(Q(query ?? "")).Append('.');
        if (!string.IsNullOrWhiteSpace(criteria))
            sb.Append(" Additional requirements from the requester: ").Append(criteria.Trim()).Append('.');
        sb.AppendLine();
        sb.AppendLine("You see a chronological contact sheet of sampled frames (left-to-right, top-to-bottom). Decide whether this clip is USABLE FOOTAGE OF that subject for a video generator to use as a motion / appearance reference, then describe it.");
        sb.AppendLine("USABLE means: the frames show the queried subject itself (the scene, the character, the action) clearly, as the main content, in real footage.");
        sb.AppendLine("REJECT (UNSUITABLE) when: the frames show someone else or people merely TALKING ABOUT the subject (talk show, interview, podcast, reaction, commentary, news desk); a title card, intro, logo, menu, credits or a mostly static frame; a slideshow of stills or text; the subject is absent, tiny, or only on a screen within the frame; it is animation, AI-generated or a parody when real footage was asked for.");
        sb.AppendLine("When in doubt about interviews, intros, or the wrong subject, answer UNSUITABLE.");
        sb.AppendLine("Naming rule for the caption: describe people by what is VISIBLE (age, build, hair, face, clothing, who is speaking). The only name you may use is the subject named in the query, and only for the person who visibly matches it; never assign other real names or character names from general knowledge of the show, film, or person, and never contradict your own VERDICT/REASON in the caption.");
        sb.AppendLine();
        sb.AppendLine("Return exactly these lines, in this order, with these labels:");
        sb.AppendLine("VERDICT: SUITABLE or UNSUITABLE");
        sb.AppendLine("REASON: <one sentence saying why>");
        sb.AppendLine("Then the two caption lines described next.");
        sb.AppendLine();
        sb.Append(DefaultVideoCaptionPrompt);
        return sb.ToString();
    }

    /// <summary>Contact-sheet the clip and run the combined verify + caption vision call.</summary>
    private IEnumerator VerifyWebVideoClipCoroutine(string clipPath, double durationSeconds, string query, string criteria, WebImageVerifyResult outResult)
    {
        FfmpegTool.ContactSheetResult sheet = null;
        yield return FfmpegTool.CreateCaptionContactSheet(clipPath, durationSeconds > 0 ? durationSeconds : FfmpegTool.DefaultClipDurationSeconds, r => sheet = r);
        byte[] png = null;
        if (sheet != null && sheet.Success && !string.IsNullOrEmpty(sheet.OutputPath) && System.IO.File.Exists(sheet.OutputPath))
        {
            try { png = System.IO.File.ReadAllBytes(sheet.OutputPath); } catch { png = null; }
            try { System.IO.File.Delete(sheet.OutputPath); } catch { }
        }
        if (png == null || png.Length == 0)
        {
            outResult.Completed = true; // no sheet: accept unverified, normal caption path
            yield break;
        }

        float waitStart = Time.realtimeSinceStartup;
        while (true)
        {
            var mgr = LLMInstanceManager.Get();
            bool visionBusy = mgr != null
                && !mgr.IsAnyLLMFree(isSmallJob: false, isVisionJob: true)
                && mgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0;
            if (!visionBusy || Time.realtimeSinceStartup - waitStart > 300f) break;
            yield return new WaitForSeconds(0.5f);
        }
        string rawText = null;
        TryCaptionBytes(png, r => { outResult.Caption = r; outResult.Completed = true; },
            requireFreeSlot: false, promptOverride: BuildWebVideoVerifyPrompt(query, criteria), jobName: "WebVideoVerify", debugFileName: "web_video_verify_sent.json",
            onRawText: t => rawText = t,
            onFailureDetail: d => outResult.FailureDetail = d);
        while (!outResult.Completed)
            yield return null;
        bool suitable; string reason;
        if (TryParseWebImageVerdict(rawText, out suitable, out reason))
        {
            outResult.Verified = true;
            outResult.Suitable = suitable;
            outResult.Reason = reason;
        }
    }

    bool IChatHost.StartWebVideoAction(SkillAction action, WebVideoRequest request, Action<bool> onDone)
    {
        if (request == null) return false;
        int epoch = _webFetchEpoch;
        BeginWebFetch();
        StartCoroutine(WebVideoCoroutine(action, request, epoch, onDone));
        return true;
    }

    private IEnumerator WebVideoCoroutine(SkillAction action, WebVideoRequest req, int epoch, Action<bool> onDone)
    {
        string sourceText = !string.IsNullOrEmpty(req.Url) ? "url=" + Q(req.Url)
            : !string.IsNullOrEmpty(req.ResultToken) ? "result=" + Q(req.ResultToken)
            : "query=" + Q(req.Query);
        string startText = req.StartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string durText = req.DurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var trace = BeginWebTrace("web_video  " + sourceText + "  start=" + startText + "  duration=" + durText + "  audio=" + (req.IncludeAudio ? "true" : "false")
            + (req.RequireSpeech ? "  speech=true" : "")
            + (string.IsNullOrEmpty(req.Anchor) ? "" : "  anchor=" + Q(req.Anchor)));
        float started = Time.realtimeSinceStartup;
        string queryForProvenance = req.Query;

        var candidates = new List<WebVideoCandidate>();
        if (!string.IsNullOrEmpty(req.Url))
        {
            candidates.Add(new WebVideoCandidate { Url = req.Url.Trim(), Host = SafeHost(req.Url) });
        }
        else if (!string.IsNullOrEmpty(req.ResultToken))
        {
            WebSearchSession session; int index; string tokenError;
            if (!TryResolveWebSearchToken(req.ResultToken, out session, out index, out tokenError))
            {
                trace.AppendLine("Result lookup failed: " + tokenError);
                _infoMessages.Add(new InfoMessage("(web_video result=" + Q(req.ResultToken) + " failed: " + tokenError + ".)"));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            queryForProvenance = session.Query;
            if (session.Kind == WebSearchKind.Videos)
            {
                var r = session.Response.Videos[index - 1];
                candidates.Add(new WebVideoCandidate { Url = r.PageUrl, Title = r.Title, Host = r.Host, DurationText = r.DurationText, DurationSeconds = r.DurationSeconds });
            }
            else if (session.Kind == WebSearchKind.Web)
            {
                var r = session.Response.Web[index - 1];
                candidates.Add(new WebVideoCandidate { Url = r.Url, Title = r.Title, Host = r.Host });
            }
            else
            {
                trace.AppendLine("Result " + req.ResultToken + " is an image result, not a video.");
                _infoMessages.Add(new InfoMessage("(web_video result=" + Q(req.ResultToken) + " is an image result, not a video.)"));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            trace.AppendLine("Using " + session.Id + " result " + index + ": " + (candidates[0].Title ?? "") + " | " + candidates[0].Url);
        }
        else
        {
            BraveSearchClient.SearchResponse resp = null;
            yield return BraveSearchClient.Search(WebSearchKind.Videos, req.Query, 10, req.SafeSearch, r => resp = r);
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

            BuildWebSearchTraceLines(trace, WebSearchKind.Videos, req.Query, resp, listResults: false);
            if (resp == null || !resp.Success)
            {
                _infoMessages.Add(new InfoMessage(ReportWebSearchFailure("web_video", req.Query, resp)));
                EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
            }
            StoreWebSearchSession(WebSearchKind.Videos, req.Query, resp);
            for (int i = 0; i < resp.Videos.Count; i++)
            {
                var r = resp.Videos[i];
                candidates.Add(new WebVideoCandidate { Url = r.PageUrl, Title = r.Title, Host = r.Host, DurationText = r.DurationText, DurationSeconds = r.DurationSeconds, ResultNumber = i + 1 });
            }
            RankWebVideoCandidates(candidates, req.Query, req.StartSeconds + req.DurationSeconds);
            var order = new StringBuilder("Download order by title quality: ");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0) order.Append(", ");
                order.Append(candidates[i].ResultNumber).Append(" (").Append(candidates[i].Score >= 0 ? "+" : "").Append(candidates[i].Score).Append(')');
            }
            AIChatLog.Note("web_ranking", order.ToString());
        }

        bool verifyClips = req.Verify && HasVisionLLM();
        if (req.Verify && !verifyClips)
            trace.AppendLine("No vision-capable LLM is active: clips will NOT be checked for suitability.");
        else if (verifyClips)
            trace.AppendLine("Each cut clip is checked by the vision LLM for suitability" + (string.IsNullOrWhiteSpace(req.Criteria) ? "" : " (criteria: " + req.Criteria.Trim() + ")") + "; unsuitable cuts try a later offset, then the next result.");
        bool speechToolAvailable = false;
        if (req.RequireSpeech)
        {
            string sttReason;
            speechToolAvailable = SpeechCheck.HasSpeechToText(out sttReason);
            trace.AppendLine(speechToolAvailable
                ? "The clip must contain the subject speaking: each cut's audio is checked with ffmpeg + Whisper; music-only or silent cuts are rejected."
                : "The clip should contain speech, but the speech check is unavailable (" + sttReason + "): only silent cuts can be rejected.");
        }
        // Speech checks can retry offsets even without vision verification.
        bool retryOffsets = (verifyClips || req.RequireSpeech) && string.IsNullOrEmpty(req.Url);

        if (candidates.Count == 0)
        {
            trace.AppendLine("No video results.");
            _infoMessages.Add(new InfoMessage("(web_video " + Q(req.Query) + ": no results. Try a different query or a direct url=.)"));
            EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
        }

        int attempts = 0;
        int addedIndex = -1;
        string addedTitle = null;
        string addedUrl = null;
        string addedSpeechNote = "";
        float maxSourceSeconds = req.MaxSourceMinutes > 0 ? req.MaxSourceMinutes * 60f : 0f;

        for (int ci = 0; ci < candidates.Count && addedIndex < 0 && attempts < WebRequestLimits.MaxVideoAttempts; ci++)
        {
            var cand = candidates[ci];
            if (string.IsNullOrEmpty(cand.Url)) continue;
            string reason;
            if (!WebMediaDownloader.IsAllowedPublicHttpUrl(cand.Url, out reason))
            {
                trace.AppendLine("Skip " + cand.Url + ": " + reason);
                continue;
            }
            if (maxSourceSeconds > 0 && cand.DurationSeconds > maxSourceSeconds)
            {
                trace.AppendLine("Skip " + (ci + 1) + ". " + (cand.Title ?? "") + " (" + cand.DurationText + " is longer than max_source_minutes " + req.MaxSourceMinutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ")");
                continue;
            }
            attempts++;
            trace.AppendLine("Source " + attempts + ": " + (string.IsNullOrEmpty(cand.Title) ? "" : Q(BraveSearchClient.Clip(cand.Title, 90)) + " ") + (string.IsNullOrEmpty(cand.DurationText) ? "" : cand.DurationText + " ") + cand.Url);

            string sourcePath = null;
            float clipStart;
            bool sourceIsTemp = false;

            if (YtDlpTool.LooksLikeDirectMediaUrl(cand.Url))
            {
                string ext = System.IO.Path.GetExtension(new Uri(cand.Url).AbsolutePath);
                string target = System.IO.Path.Combine(YtDlpTool.GetOutputDir(), "direct_" + Guid.NewGuid().ToString("N").Substring(0, 8) + (string.IsNullOrEmpty(ext) ? ".bin" : ext.ToLowerInvariant()));
                trace.AppendLine("Download (direct media): " + cand.Url);
                var handle = new WebMediaDownloader.Handle();
                _webDownloadHandles.Add(handle);
                WebMediaDownloader.DownloadResult dl = null;
                yield return WebMediaDownloader.DownloadToFile(cand.Url, target, WebRequestLimits.MaxVideoBytes, 120f, handle,
                    p => { if (trace.IsAlive) trace.SetStatus("  downloading " + Mathf.RoundToInt(p * 100f) + "%"); },
                    r => dl = r);
                _webDownloadHandles.Remove(handle);
                trace.ClearStatus();
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

                if (dl == null || !dl.Success)
                {
                    trace.AppendLine("  -> " + (dl != null ? dl.Error : "no response") + ", skipped");
                    continue;
                }
                if (!WebMediaDownloader.IsVideoKind(dl.Kind))
                {
                    trace.AppendLine("  -> HTTP " + dl.HttpStatus + " " + dl.ContentType + " " + WebMediaDownloader.FormatBytes(dl.Bytes) + " (" + (dl.Kind == WebMediaDownloader.MediaKind.Html ? "a web page, not a media file" : "not a video: " + WebMediaDownloader.KindLabel(dl.Kind)) + "), skipped");
                    try { System.IO.File.Delete(target); } catch { }
                    continue;
                }
                trace.AppendLine("  -> HTTP " + dl.HttpStatus + " " + dl.ContentType + " " + WebMediaDownloader.FormatBytes(dl.Bytes) + " in " + FormatSeconds(dl.ElapsedSeconds) + ", " + WebMediaDownloader.KindLabel(dl.Kind));
                sourcePath = dl.FilePath;
                clipStart = req.StartSeconds;
                sourceIsTemp = true;
            }
            else
            {
                string exe, toolError;
                if (!YtDlpTool.TryGetToolPath(out exe, out toolError))
                {
                    trace.AppendLine("yt-dlp: " + toolError);
                    _infoMessages.Add(new InfoMessage("(web_video cannot download page-hosted videos: " + toolError + " Tell the user; do not retry.)"));
                    EndWebTrace(trace); FinishWebFetch(); onDone?.Invoke(true); yield break;
                }

                // Whole video at <=480p (yt-dlp's own downloader is fast; its ffmpeg-based
                // --download-sections is throttled to KiB/s by YouTube), then the section is
                // cut locally below. Duration / size caps are enforced by yt-dlp itself.
                var cancel = new FfmpegTool.CancelToken();
                _webProcessCancels.Add(cancel);
                YtDlpTool.Result yt = null;
                int progressLines = 0;
                yield return YtDlpTool.DownloadVideo(cand.Url, maxSourceSeconds, WebRequestLimits.MaxVideoBytes, cancel,
                    cmd => trace.AppendLine("yt-dlp: " + cmd),
                    line =>
                    {
                        if (!trace.IsAlive) return;
                        if (line.StartsWith("[download]", StringComparison.Ordinal) && line.IndexOf('%') >= 0)
                            trace.SetStatus("  " + line.Trim());
                        else if (progressLines++ < 40)
                            trace.AppendLine("  " + line.Trim());
                    },
                    r => yt = r);
                _webProcessCancels.Remove(cancel);
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
                trace.CommitStatus();

                if (yt == null || !yt.Success)
                {
                    string err = yt != null ? yt.Error : "no result";
                    trace.AppendLine("  exit " + (yt != null ? yt.ExitCode : -1) + " in " + FormatSeconds(yt != null ? yt.ElapsedSeconds : 0f) + ": " + err);
                    var tail = YtDlpTool.OutputTail(yt, 15);
                    if (tail.Count > 0)
                    {
                        trace.AppendLine("  output tail:");
                        foreach (string t in tail) trace.AppendLine("    " + t);
                    }
                    continue;
                }

                long size = 0;
                try { size = new System.IO.FileInfo(yt.OutputPath).Length; } catch { }
                trace.AppendLine("  exit 0 in " + FormatSeconds(yt.ElapsedSeconds) + " -> " + System.IO.Path.GetFileName(yt.OutputPath) + " (" + WebMediaDownloader.FormatBytes(size) + ")");
                sourcePath = yt.OutputPath;
                clipStart = req.StartSeconds; // whole video downloaded; cut the section locally
                sourceIsTemp = true;
            }

            // Probe + normalize through the same FFmpeg path the drag-drop import uses.
            FfmpegTool.VideoInfo info = null;
            string probeError = null;
            yield return FfmpegTool.ProbeVideo(sourcePath, (i, e) => { info = i; probeError = e; });
            if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
            if (info == null || !string.IsNullOrWhiteSpace(probeError))
            {
                trace.AppendLine("  ffprobe failed: " + (probeError ?? "unknown") + ", skipped");
                if (sourceIsTemp) { try { System.IO.File.Delete(sourcePath); } catch { } }
                continue;
            }
            trace.AppendLine("  source: " + info.Width + "x" + info.Height + " @" + info.Fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "fps "
                + info.DurationSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s " + (info.CodecName ?? "") + (info.HasAudio ? " +audio" : " (silent)"));

            float available = info.DurationSeconds > 0 ? (float)info.DurationSeconds : req.DurationSeconds;
            if (clipStart >= available)
            {
                trace.AppendLine("  start " + startText + "s is past the end of this " + available.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "s source, skipped");
                if (sourceIsTemp) { try { System.IO.File.Delete(sourcePath); } catch { } }
                continue;
            }

            // Cut offsets to try in THIS source: the requested start, then (searched sources
            // only, when verifying) a couple of later points, because the model cannot know
            // timestamps and the first seconds of a web video are often a title card / intro.
            var offsets = new List<float> { clipStart };
            if (retryOffsets)
            {
                foreach (float extra in WebRequestLimits.VideoRetryOffsets)
                {
                    float o = clipStart + extra;
                    if (o + req.DurationSeconds <= available) offsets.Add(o);
                }
            }

            for (int oi = 0; oi < offsets.Count && addedIndex < 0; oi++)
            {
                float cutStart = offsets[oi];
                float clipDuration = Mathf.Clamp(req.DurationSeconds, 0.1f, Mathf.Max(0.1f, available - cutStart));
                string rangeText = cutStart.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "-" + (cutStart + clipDuration).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "s";

                string clipPath = FfmpegTool.GetClipOutputPath(sourcePath);
                FfmpegTool.ClipResult clip = null;
                trace.SetStatus("  ffmpeg cutting " + rangeText + "...");
                yield return FfmpegTool.CreateClip(sourcePath, cutStart, clipDuration, clipPath, r => clip = r,
                    fps: GetDefaultClipFps(info),
                    includeAudio: req.IncludeAudio && info.HasAudio);
                trace.ClearStatus();
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

                if (clip == null || !clip.Success)
                {
                    trace.AppendLine("  ffmpeg cut " + rangeText + " failed: " + (clip != null ? clip.Error : "unknown") + ", skipped");
                    break;
                }

                FfmpegTool.VideoInfo outInfo = null;
                yield return FfmpegTool.ProbeVideo(clip.OutputPath, (i, e) => { outInfo = i; });
                if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }
                long clipSize = 0;
                try { clipSize = new System.IO.FileInfo(clip.OutputPath).Length; } catch { }
                string dims = BuildVideoDimensionsText(outInfo ?? info);
                trace.AppendLine("  cut " + rangeText + " of source: " + (dims ?? "?") + (req.IncludeAudio && info.HasAudio ? ", audio" : ", silent") + " (" + WebMediaDownloader.FormatBytes(clipSize) + ")");

                WebImageVerifyResult verdict = null;
                if (verifyClips)
                {
                    trace.SetStatus("  checking the clip with the vision LLM...");
                    verdict = new WebImageVerifyResult();
                    double clipSeconds = outInfo != null && outInfo.DurationSeconds > 0 ? outInfo.DurationSeconds : clipDuration;
                    yield return VerifyWebVideoClipCoroutine(clip.OutputPath, clipSeconds, string.IsNullOrEmpty(queryForProvenance) ? (cand.Title ?? "") : queryForProvenance, req.Criteria, verdict);
                    trace.ClearStatus();
                    if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

                    if (verdict.Verified && !verdict.Suitable)
                    {
                        trace.AppendLine("  -> vision check: UNSUITABLE" + (string.IsNullOrEmpty(verdict.Reason) ? "" : " - " + verdict.Reason)
                            + (oi + 1 < offsets.Count ? ", trying a later part of this source" : ", skipped"));
                        try { System.IO.File.Delete(clip.OutputPath); } catch { }
                        continue;
                    }
                    if (verdict.Verified)
                        trace.AppendLine("  -> vision check: SUITABLE" + (string.IsNullOrEmpty(verdict.Reason) ? "" : " - " + verdict.Reason));
                    else
                        trace.AppendLine("  -> " + verdict.NoVerdictText);
                }

                SpeechCheck.Result speech = null;
                if (req.RequireSpeech)
                {
                    trace.SetStatus("  checking the audio for speech...");
                    speech = new SpeechCheck.Result();
                    bool clipHasAudio = outInfo != null ? outInfo.HasAudio : info.HasAudio;
                    yield return SpeechCheck.Run(clip.OutputPath, clipHasAudio && req.IncludeAudio, speech);
                    trace.ClearStatus();
                    if (epoch != _webFetchEpoch) { onDone?.Invoke(false); yield break; }

                    bool definiteNoSpeech = !speech.HasAudioStream || speech.Silent || (speech.Transcribed && !speech.HasSpeech);
                    if (definiteNoSpeech)
                    {
                        trace.AppendLine("  -> audio check: " + speech.Summary() + (oi + 1 < offsets.Count ? ", trying a later part of this source" : ", skipped"));
                        try { System.IO.File.Delete(clip.OutputPath); } catch { }
                        continue;
                    }
                    trace.AppendLine("  -> audio check: " + speech.Summary());
                }

                bool captionFromVerdict = verdict != null && verdict.Completed && !verdict.Caption.IsEmpty;
                PicMain pic = AppendVideoClipBubble(clip.OutputPath, action, isUserImport: false, dimensions: dims, autoCaption: !captionFromVerdict);
                if (pic == null)
                {
                    trace.AppendLine("  could not load the clip into a Movie bubble, skipped");
                    break;
                }
                addedIndex = _chatImagePics.Count;
                addedTitle = cand.Title;
                addedUrl = cand.Url;
                var record = _chatImageRecords.Count > 0 ? _chatImageRecords[_chatImageRecords.Count - 1] : null;
                if (record != null && record.pic == pic)
                {
                    record.kind = "web video";
                    record.alwaysIncludeCaption = true;
                    record.provenanceSteps.Clear();
                    record.provenanceSteps.Add("web video: " + (string.IsNullOrEmpty(cand.Title) ? "" : Q(BraveSearchClient.Clip(cand.Title, 60)) + " ") + ShortUrlForProvenance(cand.Url) + " " + rangeText);
                }
                _webFetchedUrlToPic[cand.Url] = pic;
                trace.AppendLine("Added as Movie #" + addedIndex + (string.IsNullOrEmpty(action?.AnchorName) ? "" : " (anchor " + Q(action.AnchorName) + ")") + (captionFromVerdict ? "" : ", captioning..."));
                if (captionFromVerdict)
                {
                    var cap = verdict.Caption;
                    if (speech != null && speech.Transcribed && speech.HasSpeech && !string.IsNullOrEmpty(speech.Transcript))
                        cap.longCaption = (cap.longCaption ?? "").TrimEnd() + " Audio transcript: \"" + speech.Transcript + "\"";
                    ApplyCaptionResultToPic(pic, cap, "video caption unavailable");
                    string capShort = !string.IsNullOrWhiteSpace(cap.shortCaption) ? cap.shortCaption : cap.longCaption;
                    trace.AppendLine("Movie #" + addedIndex + " caption: " + Q((capShort ?? "").Trim()));
                    if (!string.IsNullOrWhiteSpace(cap.longCaption))
                        trace.AppendLine("Movie #" + addedIndex + " full description (sent to the AI with its next message): " + Q(cap.longCaption.Trim()));
                }
                if (speech != null)
                {
                    addedSpeechNote = speech.Transcribed && speech.HasSpeech
                        ? " The audio contains speech (the transcript is in its full description), so it is usable as a voice reference."
                        : " WARNING: the audio could not be confirmed to contain speech (" + speech.Summary() + "); as a VOICE reference it may produce a wrong voice - tell the user, or fetch a different clip once speech-to-text is configured in Settings > Web.";
                }
                startText = cutStart.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (sourceIsTemp) { try { System.IO.File.Delete(sourcePath); } catch { } }
        }

        float elapsed = Time.realtimeSinceStartup - started;
        if (addedIndex > 0)
        {
            trace.AppendLine("Done in " + FormatSeconds(elapsed) + ".");
            _infoMessages.Add(new InfoMessage("(web_video added Movie #" + addedIndex + ": " + (string.IsNullOrEmpty(addedTitle) ? "" : Q(BraveSearchClient.Clip(addedTitle, 80)) + " ") + SafeHost(addedUrl)
                + " from " + startText + "s for " + durText + "s" + (verifyClips ? ", vision-checked as footage of the requested subject" : "") + "." + addedSpeechNote
                + " Reference it via chat_image=\"" + addedIndex + "\"" + (string.IsNullOrEmpty(action?.AnchorName) ? "" : " or its anchor " + Q(action.AnchorName)) + "; its full description follows separately - describe the people in your render prompt from that (appearance), not from outside knowledge. If you were fetching it to make a video, emit that render action NOW.)"));
        }
        else
        {
            trace.AppendLine("No usable video in " + attempts + " attempt" + (attempts == 1 ? "" : "s") + " in " + FormatSeconds(elapsed) + ".");
            _infoMessages.Add(new InfoMessage("(web_video " + (string.IsNullOrEmpty(req.Query) ? sourceText : Q(req.Query)) + ": no usable clip in " + attempts + " sources; the Web bubble shows each failure" + (verifyClips ? " (cuts judged not to show the subject are rejected" + (req.RequireSpeech ? ", and cuts without speech too" : "") + ")" : "") + ". Try a more specific query naming the scene or episode" + (req.RequireSpeech ? " with dialogue (\"interview\" is fine for a voice reference if the face matches)" : "") + ", a different url=, or tell the user if yt-dlp reported a sign-in / bot check.)"));
        }

        EndWebTrace(trace);
        FinishWebFetch();
        onDone?.Invoke(true);
    }

    void IChatHost.AddErrorBubble(string text) => AddErrorBubble(text);

    void IChatHost.AddWebTraceNotice(string text)
    {
        var trace = BeginWebTrace(text);
        EndWebTrace(trace);
    }

    bool IChatHost.IsWebAccessEnabled() => GetWebEnabled();

    void IChatHost.RequestAutoResumeAfterWebFetch()
    {
        // Same scoped slot as inspect_image resume="true": uncapped, gated on
        // HasPendingSidecarWork (which now includes web fetches AND their captions), and
        // cancelled by Stop/Clear/new send.
        RegisterInspectAutoResumeRequest(_chatTurnEpoch);
        TryScheduleInspectAutoResume();
    }

    private void BeginVideoImport(string statusLabel = null)
    {
        if (_videoImportCount <= 0)
        {
            _videoImportStartTime = Time.unscaledTime;
            _videoImportStatusLabel = "Importing video";
        }
        if (!string.IsNullOrEmpty(statusLabel))
            _videoImportStatusLabel = statusLabel;
        _videoImportCount++;
        RecomputeSendInteractable();
        UpdateVideoImportStatus(force: true);
    }

    private void FinishVideoImport()
    {
        _videoImportCount = Mathf.Max(0, _videoImportCount - 1);
        if (_videoImportCount == 0)
        {
            _videoImportStartTime = 0f;
            _videoImportStatusNextRefresh = 0f;
            _videoImportStatusLabel = "Importing video";
        }
        RecomputeSendInteractable();
        UpdateVideoImportStatus(force: true);
        // A pending inspect/skill/continue resume was blocked on this sidecar work.
        PokeAutoResumeSchedulers();
    }

    private static string BuildVideoDimensionsText(FfmpegTool.VideoInfo info)
    {
        if (info == null || info.Width <= 0 || info.Height <= 0) return null;
        string dims = $"{info.Width}x{info.Height}";
        if (info.Fps > 0)
            dims += $" @{info.Fps:0.##}fps";
        // The clip length lets the model size a generate_music duration to the video it
        // will be mixed onto (set_video_audio) without a probe round-trip.
        if (info.DurationSeconds > 0)
            dims += $", {info.DurationSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}s";
        return dims;
    }

    private void RegisterChatImageRecord(PicMain pic, SkillAction action, bool isUserAttachment, bool isMovie, string dimensions)
    {
        var record = new ChatImageRecord
        {
            pic = pic,
            isUserAttachment = isUserAttachment,
            isMovie = isMovie,
            kind = ResolveChatImageKind(action, isUserAttachment, isMovie),
            anchorName = action?.AnchorName,
            dimensions = dimensions
        };

        string step = isUserAttachment ? (isMovie ? "user video clip" : "user attachment") : BuildActionProvenanceStep(action);
        if (!string.IsNullOrEmpty(step))
            record.provenanceSteps.Add(step);
        SeedCleanBaseFromSource(record, action);
        _chatImageRecords.Add(record);
    }

    private void SeedCleanBaseFromSource(ChatImageRecord record, SkillAction action)
    {
        if (record == null || action == null || !IsLocalCompositionSkill(action.SkillId))
            return;

        int? srcIndex = action.ChatImageIndex;
        if (!srcIndex.HasValue || srcIndex.Value <= 0)
            return;

        var srcRecord = GetChatImageRecord(srcIndex.Value);
        if (srcRecord == null || srcRecord.cleanBasePngBytes == null || srcRecord.cleanBasePngBytes.Length == 0)
            return;

        // Propagate the root clean base through follow-up composed images. If a model
        // mistakenly keeps targeting the most recent flawed composite, clean_base=true
        // can still rebuild from the original pre-overlay pixels.
        record.cleanBasePngBytes = srcRecord.cleanBasePngBytes;
        record.cleanBaseDimensions = srcRecord.cleanBaseDimensions;
    }

    private static bool IsLocalCompositionSkill(string skillId)
    {
        switch (skillId ?? "")
        {
            case BuiltInSkillIds.AddBorder:
            case BuiltInSkillIds.DrawText:
            case BuiltInSkillIds.DrawShape:
            case BuiltInSkillIds.PasteImage:
            case BuiltInSkillIds.CropResize:
                return true;
            default:
                return false;
        }
    }

    private void RecordChainedProvenance(PicMain pic, SkillAction action)
    {
        if (pic == null || action == null) return;
        var record = FindChatImageRecord(pic);
        if (record == null) return;

        string skillId = action.SkillId ?? "";
        if (skillId == BuiltInSkillIds.GenerateMovie || skillId == BuiltInSkillIds.ImageToMovie)
        {
            record.isMovie = true;
            record.kind = "movie";
        }
        else if (!record.isUserAttachment && record.kind == "generated image")
        {
            record.kind = "edited image";
        }
        if (!string.IsNullOrEmpty(action.AnchorName))
        {
            record.anchorName = action.AnchorName;
            _anchors[action.AnchorName] = pic;
        }

        string step = BuildActionProvenanceStep(action);
        if (!string.IsNullOrEmpty(step))
            record.provenanceSteps.Add(step);
    }

    private ChatImageRecord FindChatImageRecord(PicMain pic)
    {
        if (pic == null || _chatImageRecords == null) return null;
        for (int i = _chatImageRecords.Count - 1; i >= 0; i--)
        {
            var record = _chatImageRecords[i];
            if (record != null && record.pic == pic)
                return record;
        }
        return null;
    }

    private static string ResolveChatImageKind(SkillAction action, bool isUserAttachment, bool isMovie)
    {
        if (isMovie) return "movie";
        if (isUserAttachment) return "user attachment";
        string skillId = action?.SkillId ?? "";
        switch (skillId)
        {
            case BuiltInSkillIds.ImageToImage:
                return "edited image";
            case BuiltInSkillIds.ExtractStill:
                return "extracted still";
            case BuiltInSkillIds.WebImage:
                return "web image";
            case BuiltInSkillIds.NewCanvas:
                return "canvas";
            case BuiltInSkillIds.AddBorder:
            case BuiltInSkillIds.DrawText:
            case BuiltInSkillIds.DrawShape:
            case BuiltInSkillIds.PasteImage:
            case BuiltInSkillIds.CropResize:
                return "composed image";
            default:
                return "generated image";
        }
    }

    private static string BuildActionProvenanceStep(SkillAction action)
    {
        if (action == null) return "";
        var sb = new StringBuilder();
        string skillId = action.SkillId ?? "";
        sb.Append(string.IsNullOrEmpty(skillId) ? "action" : skillId);

        string preset = ShortPresetName(action.Preset);
        if (!string.IsNullOrEmpty(preset))
            sb.Append(" preset=").Append(preset);

        string source = BuildActionSourceSummary(action);
        if (!string.IsNullOrEmpty(source))
            sb.Append(" source=").Append(source);

        string prompt = action.PromptForLogs;
        if (!string.IsNullOrEmpty(prompt))
            sb.Append(" prompt=\"").Append(CompactPromptText(prompt, 120)).Append('"');
        else
        {
            string text = action.GetArg("text");
            if (!string.IsNullOrEmpty(text))
                sb.Append(" text=\"").Append(CompactPromptText(text, 80)).Append('"');
        }

        return sb.ToString();
    }

    private static string BuildActionSourceSummary(SkillAction action)
    {
        if (action == null) return "";
        var parts = new List<string>();
        if (action.Chain) parts.Add("chain");
        AddSourcePart(parts, action, "chat_image", "chat");
        AddSourcePart(parts, action, "attachment", "attach");
        if (IsTruthyArg(action.GetArg("clean_base"))) parts.Add("clean_base");
        for (int i = 2; i <= SkillAction.MaxExtraInputSlot; i++)
        {
            AddSourcePart(parts, action, "chat_image" + i, "chat" + i);
            AddSourcePart(parts, action, "attachment" + i, "attach" + i);
        }
        AddSourcePart(parts, action, "source_chat_image", "src_chat");
        AddSourcePart(parts, action, "source_attachment", "src_attach");
        return parts.Count == 0 ? "" : string.Join("+", parts);
    }

    private static void AddSourcePart(List<string> parts, SkillAction action, string key, string label)
    {
        string value = action.GetArg(key);
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add(label + "=" + value.Trim());
    }

    private static bool IsTruthyArg(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().ToLowerInvariant();
        return value == "true" || value == "1" || value == "yes" || value == "on";
    }

    private static string ShortPresetName(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return "";
        string name = System.IO.Path.GetFileNameWithoutExtension(preset.Trim());
        return string.IsNullOrEmpty(name) ? preset.Trim() : name;
    }

    private static string CompactPromptText(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string oneLine = Regex.Replace(text, @"\s+", " ").Trim();
        oneLine = oneLine.Replace("\"", "'");
        if (maxChars > 0 && oneLine.Length > maxChars)
            oneLine = oneLine.Substring(0, Math.Max(0, maxChars - 3)).TrimEnd() + "...";
        return oneLine;
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

        // If the action named an anchor (anchor="Bob" or anchor="layout_canvas"),
        // bind/re-bind that name to this freshly-spawned Pic. Re-binding is
        // intentional: it's how a character's look or a named layout part gets
        // updated, and later chat_image="Name" references resolve to the new image.
        if (!string.IsNullOrEmpty(action?.AnchorName))
        {
            _anchors[action.AnchorName] = spawnedPic;
            Debug.Log($"AIChatPanel: anchor '{action.AnchorName}' -> Image #{chatImageNumber}");
        }

        string skillId = action != null ? (action.SkillId ?? "") : "";
        // Spawn-time movie flag: must cover EVERY skill whose output renders into a
        // movie, because IsChatImageMovie relies on this record while the clip is
        // still rendering (PicMovie.IsMovie() stays false until the file exists).
        bool isMovie = skillId == BuiltInSkillIds.GenerateMovie || skillId == BuiltInSkillIds.ImageToMovie
            || skillId == BuiltInSkillIds.VideoToVideo || skillId == BuiltInSkillIds.RifeVideo;
        RegisterChatImageRecord(spawnedPic, action, isUserAttachment: false, isMovie: isMovie, dimensions: null);
        // Keep the bubble label compact so the caption (appended async below) has
        // room: just "#N". The Image/Movie kind and skillId are visually obvious
        // from the bubble itself and still tracked for the LLM in ChatContextBuilder.
        string label = $"#{chatImageNumber}";
        AppendImageBubbleInternal(spawnedPic, label, isMovie);
        MarkLatestAssistantMediaCheckpoint();

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
                $"({kindLabel} just spawned as #{chatImageNumber} in CHAT IMAGES. " +
                $"Reference it on later turns via chat_image=\"{chatImageNumber}\" " +
                "or by its anchor name if one was set. Same-reply follow-ups should use chain=\"true\" " +
                "or anchors, not guessed future numbers.)"));
        }

        // Generated images don't have texture data yet (workflow hasn't run). The
        // PicMain callbacks (m_onFinishedRenderingCallback / m_onFinishedScriptCallback)
        // are unreliable signals here - they're reset between steps, multiple
        // subsystems chain to them, and a plain single-image gen doesn't always fire
        // the script callback. Just poll until TryGetImageAsPng returns bytes.
        if (!isMovie && spawnedPic != null && GetAutoCaptionGeneratedImages())
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
        const float noProgressBudget = 240f;
        const float pollInterval = 1.5f;
        const int stableTicksRequired = 2; // ~3s of stability before captioning

        // The deadline is a *no-progress* budget, not a flat wall-clock cap. A batch of
        // generated images funnels its captions through limited vision-LLM capacity one
        // slot at a time, so a later pic can legitimately wait minutes in line before a
        // slot frees - that's backpressure, not a hang, and must not count toward "give
        // up". We push the deadline forward whenever something useful is happening (still
        // rendering, texture changing, a caption in flight, or waiting on a busy vision
        // slot); only a genuine ~240s stall with NO progress ends the coroutine.
        float deadline = Time.realtimeSinceStartup + noProgressBudget;

        Texture lastSeenTex = null;
        Texture captionedTex = null;
        int stableTicks = 0;
        bool inFlight = false;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (pic == null || pic.gameObject == null) yield break;

            bool progressed = false;

            Texture curTex;
            if (!pic.TryGetCurrentTexture(out curTex) || curTex == null)
            {
                // No texture yet - the render is still queued/running, which is progress.
                if (pic.IsBusyBasic())
                    deadline = Time.realtimeSinceStartup + noProgressBudget;
                yield return new WaitForSeconds(pollInterval);
                continue;
            }

            if (curTex != lastSeenTex)
            {
                lastSeenTex = curTex;
                stableTicks = 0;
                progressed = true; // texture still settling / a new workflow step landed
            }
            else if (stableTicks < int.MaxValue)
            {
                stableTicks++;
            }

            // A caption already in flight, or a later workflow step still rendering, both
            // count as progress so the no-progress deadline can't fire mid-work.
            if (inFlight || pic.IsBusyBasic())
                progressed = true;

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
                // Capacity gate: don't outrun the vision model. Firing a whole batch's
                // captions at once over-subscribed the single slow local vision LLM, so
                // the per-caption 60s watchdog (started at real dispatch in
                // TryCaptionBytes) expired on the ones still queued and their good replies
                // were discarded - only the first few survived. Mirrors the "Waiting for
                // LLM slot..." gate PicMain.UpdateJobs() uses for call_llm.
                //
                // Only throttle when a vision route EXISTS but is momentarily full. If no
                // vision-capable instance is active at all, fall through and dispatch so
                // TryCaptionBytes still emits its one-time no-vision warning and resolves
                // to "caption unavailable" (prior behaviour) instead of polling forever.
                var instanceMgr = LLMInstanceManager.Get();
                bool visionBusy = instanceMgr != null
                    && !instanceMgr.IsAnyLLMFree(isSmallJob: false, isVisionJob: true)
                    && instanceMgr.GetLeastBusyLLM(isSmallJob: false, isVisionJob: true) >= 0;
                if (visionBusy)
                {
                    progressed = true; // queued behind a busy vision slot - retry next tick
                }
                else if (pic.TryGetImageAsPng(out byte[] png) && png != null && png.Length > 0)
                {
                    inFlight = true;
                    progressed = true;
                    Texture submittedTex = curTex;
                    TryCaptionPic(pic, png, () =>
                    {
                        inFlight = false;
                        captionedTex = submittedTex;
                    });
                }
            }

            if (progressed)
                deadline = Time.realtimeSinceStartup + noProgressBudget;

            // Done: the current texture has been captioned and no further workflow
            // step is expected to swap it. Exiting here is what stops the polling
            // from running for the full timeout after a successful gen.
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
    private void AppendUserAttachmentBubble(PicMain pic, string preCaptionShort = null, string preCaptionLong = null, string dimensions = null)
    {
        if (pic == null || _mediaContent == null) return;
        _chatImagePics.Add(pic);
        int chatImageNumber = _chatImagePics.Count;
        RegisterChatImageRecord(pic, null, isUserAttachment: true, isMovie: false, dimensions: dimensions);
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

    /// <summary>
    /// How many of the newest chat images are described in the volatile CHAT IMAGES
    /// prompt block. This does not delete media or affect chat_image resolution.
    /// 0 hides all per-image records; older images remain available locally.
    /// </summary>
    public static int GetImageContextLimit()
    {
        int n = PlayerPrefs.GetInt(PREFS_IMAGE_CONTEXT_LIMIT, DEFAULT_IMAGE_CONTEXT_LIMIT);
        return Mathf.Clamp(n, 0, MAX_IMAGE_CONTEXT_LIMIT);
    }

    public static void SetImageContextLimit(int n)
    {
        PlayerPrefs.SetInt(PREFS_IMAGE_CONTEXT_LIMIT, Mathf.Clamp(n, 0, MAX_IMAGE_CONTEXT_LIMIT));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Session-only reminder appended to each main AI Chat user turn before the
    /// bubble/history entry is created. Not stored in PlayerPrefs; survives Clear
    /// because it is settings state, not conversation state.
    /// </summary>
    public static string GetUserPostMessage()
    {
        return _userPostMessage ?? "";
    }

    public static void SetUserPostMessage(string text)
    {
        _userPostMessage = text ?? "";
    }

    /// <summary>True when a chat panel instance is alive to compact.</summary>
    public static bool IsChatActive => _instance != null;

    // ----- Automation harness hooks (AutomationDriver / AutomationController) -----
    // These let the loopback control server drive a chat turn and poll for completion.
    // All called on the Unity main thread.

    /// <summary>
    /// True when the chat is fully settled and safe to issue the next automated step:
    /// no LLM streaming, no forced-main wait, no compact summary, no pending attachment
    /// captions or vision inspections, no queued auto-resume, the skill-action pump has
    /// drained, and no chat-generated Pic is still rendering on a GPU.
    /// </summary>
    public bool AutomationIsFullyIdle()
    {
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight) return false;
        if (HasPendingSidecarWork()) return false;
        if (HasInspectAutoResumePendingForCurrentTurn()) return false;
        if (HasSkillLoadAutoResumePendingForCurrentTurn()) return false;
        if (_actionExecutor != null && !_actionExecutor.IsIdle) return false;
        if (_chatImagePics != null)
        {
            for (int i = 0; i < _chatImagePics.Count; i++)
            {
                var pic = _chatImagePics[i];
                if (pic != null && pic.IsBusy()) return false;
            }
        }
        return true;
    }

    /// <summary>Inject a message into the input field and run the normal send path.</summary>
    public bool AutomationSendMessage(string text)
    {
        // Report the silent OnSendClicked gates back to the automation caller: a
        // human sees a greyed Send button, but a scripted POST /chat racing the end
        // of the previous turn was swallowed with no signal, dropping the message.
        if (_isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight || HasPendingSidecarWork())
            return false;
        if (_inputField != null)
            _inputField.text = text ?? "";
        OnSendClicked();
        return true;
    }

    /// <summary>Static accessor: report idle for the live panel. False if no panel exists.</summary>
    public static bool AutomationGetIdle(out bool idle)
    {
        idle = false;
        if (_instance == null) return false;
        idle = _instance.AutomationIsFullyIdle();
        return true;
    }

    /// <summary>Static accessor: send a chat message via the live panel. False if none.</summary>
    /// <summary>Static accessor: click Stop on the live panel. False if no panel exists.</summary>
    public static bool AutomationStop()
    {
        if (_instance == null) return false;
        _instance.OnStopClicked();
        return true;
    }

    public static bool AutomationSend(string text)
    {
        if (_instance == null) return false;
        return _instance.AutomationSendMessage(text);
    }

    /// <summary>
    /// Automation-only: run the Compact feature ("summarize" or "truncate"), keeping the
    /// last <paramref name="keepExchanges"/> exchanges verbatim. Summarize is async - the
    /// caller should poll /status until idle, then inspect the chat/history.
    /// </summary>
    public static bool AutomationCompact(string mode, int keepExchanges, out string error)
    {
        error = "";
        if (_instance == null) { error = "no chat panel"; return false; }
        if (keepExchanges < 0) keepExchanges = 0;
        switch ((mode ?? "").Trim().ToLowerInvariant())
        {
            case "truncate":
                _instance.DoCompactTruncate(keepExchanges);
                return true;
            case "":
            case "summarize":
                _instance.DoCompactSummarize(keepExchanges);
                return true;
            default:
                error = "unknown mode '" + mode + "' (use summarize or truncate)";
                return false;
        }
    }

    /// <summary>
    /// Register an already-created local MP4 clip as a Movie bubble in AI Chat. Used by
    /// the PicMain movie export path after FFmpeg has written the clip.
    /// </summary>
    public static bool AddLocalMovieClipToChat(string clipPath, string dimensions, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(clipPath))
        {
            error = "no clip path";
            return false;
        }
        if (!System.IO.File.Exists(clipPath))
        {
            error = "clip file not found: " + clipPath;
            return false;
        }

        Show();
        if (_instance == null)
        {
            error = "no chat panel";
            return false;
        }

        PicMain pic = _instance.AppendVideoClipBubble(clipPath, null, isUserImport: true, dimensions: dimensions);
        if (pic == null)
        {
            error = "could not append movie bubble";
            return false;
        }

        _instance.AddSystemMessage($"Exported movie clip as Movie #{_instance._chatImagePics.Count}.", includeInLLMRecap: false);
        return true;
    }

    /// <summary>
    /// Automation-only local video import. Bypasses the human clip chooser by taking
    /// explicit start/duration values, then appends the normalized MP4 as a Movie bubble.
    /// </summary>
    public static bool AutomationImportVideo(string path, float startSeconds, float durationSeconds, double fps, bool includeAudio, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "no path given";
            return false;
        }

        try { path = System.IO.Path.GetFullPath(path); }
        catch { }

        if (!System.IO.File.Exists(path))
        {
            error = "video file not found: " + path;
            return false;
        }
        if (!FfmpegTool.IsSupportedVideoExtension(path))
        {
            error = "unsupported video extension: " + path;
            return false;
        }

        Show();
        if (_instance == null)
        {
            error = "no chat panel";
            return false;
        }

        _instance.StartCoroutine(_instance.AutomationImportVideoRoutine(path, startSeconds, durationSeconds, fps, includeAudio));
        return true;
    }

    private IEnumerator AutomationImportVideoRoutine(string path, float startSeconds, float durationSeconds, double fps, bool includeAudio)
    {
        int epoch = _videoImportEpoch;
        BeginVideoImport();
        AddSystemMessage("Preparing automation video clip import...", includeInLLMRecap: false);

        FfmpegTool.VideoInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeVideo(path, (i, e) => { info = i; error = e; });

        if (epoch != _videoImportEpoch)
            yield break;

        if (!string.IsNullOrWhiteSpace(error) || info == null)
        {
            FinishVideoImport();
            AddSystemMessage("Could not inspect automation video: " + (error ?? "unknown error"), includeInLLMRecap: false);
            yield break;
        }

        float maxDuration = info.DurationSeconds > 0 ? (float)info.DurationSeconds : FfmpegTool.DefaultClipDurationSeconds;
        startSeconds = Mathf.Clamp(startSeconds, 0f, Mathf.Max(0f, maxDuration - 0.1f));
        durationSeconds = Mathf.Clamp(durationSeconds <= 0f ? FfmpegTool.DefaultClipDurationSeconds : durationSeconds, 0.1f, Mathf.Max(0.1f, maxDuration - startSeconds));

        var selection = CreateDefaultClipSelection(info, startSeconds, durationSeconds);
        if (fps > 0 && !double.IsNaN(fps) && !double.IsInfinity(fps))
            selection.Fps = fps;
        selection.IncludeAudio = includeAudio;

        yield return TranscodeAndAppendVideoClip(path, info, selection, epoch, null, isUserImport: true);
    }

    /// <summary>JSON array describing each chat image: 1-based index, dimensions, busy.</summary>
    public string AutomationGetChatImagesJson()
    {
        var sb = new StringBuilder();
        sb.Append("[");
        if (_chatImagePics != null)
        {
            for (int i = 0; i < _chatImagePics.Count; i++)
            {
                var pic = _chatImagePics[i];
                int w = 0, h = 0;
                bool busy = false;
                if (pic != null)
                {
                    busy = pic.IsBusy();
                    var tex = pic.GetCurrentTexture();
                    if (tex != null) { w = tex.width; h = tex.height; }
                }
                bool movie = pic != null && pic.IsMovie();
                string moviePath = movie && pic.m_picMovie != null ? pic.m_picMovie.GetProcessingFileName() : null;
                // Movie bubbles: report the CLIP's real dimensions, not the Pic's still
                // sprite. A movie Pic that was never played (or was unloaded to save
                // memory) still carries PicMain.Awake's 512x512 black placeholder, which
                // made this endpoint report every imported clip as a square.
                if (movie && !string.IsNullOrEmpty(moviePath)
                    && FfmpegTool.TryProbeVideoSync(moviePath, out var probedInfo, out _)
                    && probedInfo != null && probedInfo.Width > 0 && probedInfo.Height > 0)
                {
                    int rot = ((probedInfo.RotationDegrees % 360) + 360) % 360;
                    bool swap = rot == 90 || rot == 270;
                    w = swap ? probedInfo.Height : probedInfo.Width;
                    h = swap ? probedInfo.Width : probedInfo.Height;
                }
                if (i > 0) sb.Append(",");
                sb.Append("{\"index\":").Append(i + 1)
                  .Append(",\"w\":").Append(w)
                  .Append(",\"h\":").Append(h)
                  .Append(",\"busy\":").Append(busy ? "true" : "false")
                  .Append(",\"exists\":").Append(pic != null ? "true" : "false")
                  .Append(",\"movie\":").Append(movie ? "true" : "false")
                  .Append(",\"captionPending\":").Append(pic != null && _videoCaptionInFlight.Contains(pic) ? "true" : "false");
                if (!string.IsNullOrEmpty(moviePath))
                    sb.Append(",\"moviePath\":").Append(AutomationJsonString(moviePath));
                if (!string.IsNullOrEmpty(pic?.CaptionShort))
                    sb.Append(",\"captionShort\":").Append(AutomationJsonString(pic.CaptionShort));
                if (!string.IsNullOrEmpty(pic?.Caption))
                    sb.Append(",\"captionLong\":").Append(AutomationJsonString(pic.Caption));
                sb.Append("}");
            }
        }
        sb.Append("]");
        return sb.ToString();
    }

    private static string AutomationJsonString(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    /// <summary>
    /// Save a chat image to <paramref name="path"/> as PNG. <paramref name="index"/> is
    /// 1-based; index &lt;= 0 means the latest (newest) chat image.
    /// </summary>
    public bool AutomationSaveChatImage(int index, string path, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path)) { error = "no path given"; return false; }
        if (_chatImagePics == null || _chatImagePics.Count == 0) { error = "no chat images"; return false; }

        int idx = index <= 0 ? _chatImagePics.Count - 1 : index - 1;
        if (idx < 0 || idx >= _chatImagePics.Count)
        {
            error = $"index {index} out of range (1..{_chatImagePics.Count})";
            return false;
        }

        var pic = _chatImagePics[idx];
        if (pic == null) { error = "chat image pic was destroyed"; return false; }
        if (pic.IsBusy()) { error = "chat image is still rendering"; return false; }

        try
        {
            pic.SaveFile(path, "", null, "", true, false);
        }
        catch (System.Exception e)
        {
            error = e.Message;
            return false;
        }
        return true;
    }

    // Bridge: PicMovie playback telemetry for one chat Movie bubble (see the
    // automation /movie_state endpoint). index <= 0 means latest.
    public string AutomationGetMovieStateJson(int index)
    {
        if (_chatImagePics == null || _chatImagePics.Count == 0)
            return "{\"ok\":false,\"error\":\"no chat images\"}";
        int idx = index <= 0 ? _chatImagePics.Count - 1 : index - 1;
        if (idx < 0 || idx >= _chatImagePics.Count)
            return "{\"ok\":false,\"error\":\"index " + index + " out of range (1.." + _chatImagePics.Count + ")\"}";
        var pic = _chatImagePics[idx];
        if (pic == null)
            return "{\"ok\":false,\"error\":\"chat image pic was destroyed\"}";
        var movie = pic.GetComponent<PicMovie>();
        if (movie == null)
            return "{\"ok\":false,\"error\":\"not a movie pic\"}";
        return "{\"ok\":true,\"index\":" + (idx + 1) + ",\"state\":" + movie.GetPlaybackDebugJson() + "}";
    }

    /// <summary>Static accessor: movie playback telemetry. Error JSON if no panel.</summary>
    public static string AutomationMovieState(int index)
    {
        return _instance != null
            ? _instance.AutomationGetMovieStateJson(index)
            : "{\"ok\":false,\"error\":\"no chat panel\"}";
    }

    /// <summary>Static accessor: chat images JSON. "[]" if no panel.</summary>
    public static string AutomationChatImagesJson()
    {
        return _instance != null ? _instance.AutomationGetChatImagesJson() : "[]";
    }

    /// <summary>Static accessor: save a chat image. False (with error) if no panel.</summary>
    public static bool AutomationSave(int index, string path, out string error)
    {
        error = "no chat panel";
        if (_instance == null) return false;
        return _instance.AutomationSaveChatImage(index, path, out error);
    }

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
    /// skill md or prompt files - all wrapped names track in lockstep.
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

    public static bool GetKeepOldToolCallsInPrompt()
    {
        return PlayerPrefs.GetInt(PREFS_KEEP_OLD_TOOL_CALLS_IN_PROMPT, 1) != 0;
    }

    public static void SetKeepOldToolCallsInPrompt(bool v)
    {
        PlayerPrefs.SetInt(PREFS_KEEP_OLD_TOOL_CALLS_IN_PROMPT, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static bool GetAutoCaptionGeneratedImages()
    {
        return PlayerPrefs.GetInt(PREFS_AUTO_CAPTION_GENERATED_IMAGES, 0) != 0;
    }

    public static void SetAutoCaptionGeneratedImages(bool v)
    {
        PlayerPrefs.SetInt(PREFS_AUTO_CAPTION_GENERATED_IMAGES, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static bool GetShowDebugStuff()
    {
        return PlayerPrefs.GetInt(PREFS_SHOW_DEBUG_STUFF, 0) != 0;
    }

    public static void SetShowDebugStuff(bool v)
    {
        PlayerPrefs.SetInt(PREFS_SHOW_DEBUG_STUFF, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>The header "Web" checkbox: may AI Chat search the web / fetch pages, images, clips? Default on.</summary>
    public static bool GetWebEnabled()
    {
        return PlayerPrefs.GetInt(PREFS_WEB_ENABLED, 1) != 0;
    }

    public static void SetWebEnabled(bool v)
    {
        PlayerPrefs.SetInt(PREFS_WEB_ENABLED, v ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Skill ids left out of the stable SKILLS block and keyword autoload: the web_* skills
    /// while the Web toggle is off (null = hide nothing). Only changes when the user flips the
    /// toggle, so the prompt prefix stays byte-stable between flips.
    /// </summary>
    private static ISet<string> HiddenSkillIdsForPrompt()
    {
        // The audio generation skills are hidden until a gateway URL exists (Settings >
        // Audio); their skill files may not even exist on machines without one. Config
        // changes are rare, so the prefix stays stable in practice.
        bool hideWeb = !GetWebEnabled();
        bool hideAudio = !AudioGenClient.IsConfigured();
        if (!hideWeb && !hideAudio) return null;
        var hidden = new HashSet<string>();
        if (hideWeb) hidden.UnionWith(BuiltInSkillIds.WebSkills);
        if (hideAudio) hidden.UnionWith(BuiltInSkillIds.AudioGenSkills);
        return hidden;
    }

    /// <summary>Automation: read or set the header Web checkbox (null = report only).</summary>
    public static bool AutomationSetWebEnabled(bool? enabled, out bool current)
    {
        if (enabled.HasValue)
        {
            SetWebEnabled(enabled.Value);
            if (_instance != null && _instance._webToggle != null)
                _instance._webToggle.SetIsOnWithoutNotify(enabled.Value);
        }
        current = GetWebEnabled();
        return true;
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
                if (poppedPic != null)
                {
                    _captionLabels.Remove(poppedPic);
                    _videoCaptionInFlight.Remove(poppedPic);
                }
            }
            _chatImagePics.RemoveRange(0, popN);
            if (_chatImageRecords != null && _chatImageRecords.Count > 0)
            {
                int recordPopN = Mathf.Min(popN, _chatImageRecords.Count);
                _chatImageRecords.RemoveRange(0, recordPopN);
            }

            // Drop any character anchors whose Pic just fell out of the numbered list,
            // so the ANCHORS line never advertises a name that can no longer resolve.
            PruneAnchorsToLiveChatImages();
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

    // Local-only bubble: shown to the user, never queued into the info-recap, so its
    // text is never sent to the chat model. Used for /applystyle restyle feedback,
    // whose rewritten prompt the original AI must not see.
    void IChatHost.AddLocalInfoBubble(string text) => AddSystemMessage(text, includeInLLMRecap: false);

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

    void IChatHost.RequestAutoResumeAfterSkillLoad(string skillId)
    {
        RegisterSkillLoadAutoResumeRequest(_chatTurnEpoch, skillId);
        TryScheduleSkillLoadAutoResume();
    }

    void IChatHost.RequestContinueTurn()
    {
        RegisterGenericContinueRequest(_chatTurnEpoch);
        TryScheduleGenericContinue();
    }

    void IChatHost.EnqueueInspectImage(byte[] png, string prompt, string sourceLabel, int? llmInstanceId, bool resumeOnResult)
    {
        EnqueueInspectImage(png, prompt, sourceLabel, llmInstanceId, resumeOnResult);
    }

    void IChatHost.AppendImageBubbleForPic(SkillAction action, PicMain spawnedPic)
    {
        AppendImageBubble(action, spawnedPic);
    }

    bool IChatHost.StartClipVideoAction(SkillAction action, int sourceChatImageIndex, float startSeconds, float durationSeconds, double fps, bool includeAudio, Action<bool> onDone)
    {
        string sourcePath = ((IChatHost)this).GetChatImageMovieFilePath(sourceChatImageIndex);
        if (string.IsNullOrEmpty(sourcePath))
            return false;

        int epoch = _videoImportEpoch;
        BeginVideoImport();
        StartCoroutine(ClipVideoActionCoroutine(sourcePath, sourceChatImageIndex, action, startSeconds, durationSeconds, fps, includeAudio, epoch, onDone));
        return true;
    }

    private IEnumerator ClipVideoActionCoroutine(string sourcePath, int sourceChatImageIndex, SkillAction action, float startSeconds, float durationSeconds, double fps, bool includeAudio, int epoch, Action<bool> onDone)
    {
        FfmpegTool.VideoInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeVideo(sourcePath, (i, e) => { info = i; error = e; });

        if (epoch != _videoImportEpoch)
        {
            onDone?.Invoke(false);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(error) || info == null)
        {
            FinishVideoImport();
            ((IChatHost)this).AddSystemInjectionAndBubble(
                $"clip_video could not inspect Movie #{sourceChatImageIndex}: {error ?? "unknown ffprobe error"}");
            onDone?.Invoke(false);
            yield break;
        }

        float maxDuration = info.DurationSeconds > 0 ? (float)info.DurationSeconds : FfmpegTool.DefaultClipDurationSeconds;
        startSeconds = Mathf.Clamp(startSeconds, 0f, Mathf.Max(0f, maxDuration - 0.1f));
        durationSeconds = Mathf.Clamp(durationSeconds <= 0f ? FfmpegTool.DefaultClipDurationSeconds : durationSeconds, 0.1f, Mathf.Max(0.1f, maxDuration - startSeconds));

        var selection = CreateDefaultClipSelection(info, startSeconds, durationSeconds);
        if (fps > 0 && !double.IsNaN(fps) && !double.IsInfinity(fps))
            selection.Fps = fps;
        selection.IncludeAudio = includeAudio;

        yield return TranscodeAndAppendVideoClip(sourcePath, info, selection, epoch, action, isUserImport: false);
        onDone?.Invoke(epoch == _videoImportEpoch);
    }

    bool IChatHost.StartExtractStillAction(SkillAction action, int sourceChatImageIndex, float atSeconds, Action<bool> onDone)
    {
        string sourcePath = ((IChatHost)this).GetChatImageMovieFilePath(sourceChatImageIndex);
        if (string.IsNullOrEmpty(sourcePath))
            return false;

        int epoch = _videoImportEpoch;
        BeginVideoImport();
        StartCoroutine(ExtractStillActionCoroutine(sourcePath, sourceChatImageIndex, action, atSeconds, epoch, onDone));
        return true;
    }

    private IEnumerator ExtractStillActionCoroutine(string sourcePath, int sourceChatImageIndex, SkillAction action, float atSeconds, int epoch, Action<bool> onDone)
    {
        FfmpegTool.VideoInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeVideo(sourcePath, (i, e) => { info = i; error = e; });

        if (epoch != _videoImportEpoch)
        {
            onDone?.Invoke(false);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(error) || info == null)
        {
            FinishVideoImport();
            ((IChatHost)this).AddSystemInjectionAndBubble(
                $"extract_still could not inspect Movie #{sourceChatImageIndex}: {error ?? "unknown ffprobe error"}");
            onDone?.Invoke(false);
            yield break;
        }

        // Clamp inside the clip; the very end of a stream often has no decodable frame.
        if (info.DurationSeconds > 0)
            atSeconds = Mathf.Clamp(atSeconds, 0f, Mathf.Max(0f, (float)info.DurationSeconds - 0.05f));
        else
            atSeconds = Mathf.Max(0f, atSeconds);

        string outputPath = FfmpegTool.GetStillFrameOutputPath(sourcePath);
        FfmpegTool.ClipResult result = null;
        yield return FfmpegTool.ExtractStillFrame(sourcePath, atSeconds, outputPath, r => result = r);

        if (epoch != _videoImportEpoch)
        {
            onDone?.Invoke(false);
            yield break;
        }

        if (result == null || !result.Success)
        {
            FinishVideoImport();
            ((IChatHost)this).AddSystemInjectionAndBubble(
                $"extract_still failed on Movie #{sourceChatImageIndex}: {(result != null ? result.Error : "unknown error")}");
            onDone?.Invoke(false);
            yield break;
        }

        var imageGen = ImageGenerator.Get();
        GameObject frameGo = imageGen != null ? imageGen.AddImageByFileName(result.OutputPath) : null;
        PicMain framePic = frameGo != null ? frameGo.GetComponent<PicMain>() : null;
        if (framePic == null)
        {
            FinishVideoImport();
            ((IChatHost)this).AddSystemInjectionAndBubble(
                $"extract_still could not load the extracted frame from Movie #{sourceChatImageIndex} into an image.");
            onDone?.Invoke(false);
            yield break;
        }

        AppendExtractedStillBubble(framePic, action, BuildStillDimensionsText(info), sourceChatImageIndex, atSeconds);
        FinishVideoImport();
        onDone?.Invoke(true);
    }

    // ---------- stitch_video: join several Movie bubbles into one clip ----------

    private const float StitchWaitPollSeconds = 0.5f;
    // A "10 clips then stitch them" reply on one GPU can legitimately take a long time.
    private const float StitchWaitAbsoluteCapSeconds = 2f * 60f * 60f;
    // A source Pic that stopped being busy but never produced a movie file (the render
    // failed, or the clip is still landing) gets this long before the stitch gives up.
    private const float StitchNoClipGraceSeconds = 30f;
    private int _stitchWaitCount = 0;
    private float _stitchWaitStartTime = 0f;
    private int _stitchSpinnerStep = 0;
    private string _lastStitchStatusText;

    bool IChatHost.StartStitchVideoAction(SkillAction action, List<int> sourceChatImageIndices, FfmpegTool.StitchRequest request, Action<bool> onDone)
    {
        if (sourceChatImageIndices == null || sourceChatImageIndices.Count < 2 || request == null)
            return false;
        StartCoroutine(StitchVideoActionCoroutine(action, new List<int>(sourceChatImageIndices), request, _videoImportEpoch, _chatTurnEpoch, onDone));
        return true;
    }

    /// <summary>
    /// Phase 1 waits until every source clip exists on disk. This is deliberately NOT a
    /// video import: the sources may be minutes of GPU work away (the whole point of
    /// "make N clips, then stitch them") and the user must stay able to chat meanwhile.
    /// Only Stop/Clear (the import epoch) cancel the wait; a new user turn does not.
    /// Phase 2 (probe + ffmpeg concat + append) is a normal short video import.
    /// </summary>
    private IEnumerator StitchVideoActionCoroutine(SkillAction action, List<int> sources, FfmpegTool.StitchRequest request, int importEpoch, int turnEpoch, Action<bool> onDone)
    {
        var host = (IChatHost)this;

        var pending = new List<int>();
        string failure = CollectStitchSourceState(sources, pending, out _, out bool readyNow);
        if (failure == null && !readyNow && pending.Count > 0)
        {
            AddSystemMessage($"stitch_video: waiting for {DescribeMovieList(pending)} to finish rendering, then stitching {sources.Count} clips.");
            AIChatLog.Note("stitch_video", "waiting for " + DescribeMovieList(pending));
        }

        BeginStitchWait();
        float waitStart = Time.realtimeSinceStartup;
        float notBusySince = -1f;
        while (failure == null)
        {
            if (importEpoch != _videoImportEpoch)
            {
                EndStitchWait();
                onDone?.Invoke(false);
                yield break;
            }

            pending.Clear();
            failure = CollectStitchSourceState(sources, pending, out bool anyBusy, out bool allReady);
            if (failure != null || allReady)
                break;

            float elapsed = Time.realtimeSinceStartup - waitStart;
            if (elapsed >= StitchWaitAbsoluteCapSeconds)
            {
                failure = $"gave up waiting for {DescribeMovieList(pending)} after {Mathf.RoundToInt(elapsed / 60f)} minutes.";
                break;
            }
            if (anyBusy)
            {
                notBusySince = -1f;
            }
            else
            {
                if (notBusySince < 0f)
                    notBusySince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - notBusySince >= StitchNoClipGraceSeconds)
                {
                    failure = $"{DescribeMovieList(pending)} finished without producing a video file (the render probably failed).";
                    break;
                }
            }

            UpdateStitchWaitStatus(pending.Count);
            yield return new WaitForSeconds(StitchWaitPollSeconds);
        }
        EndStitchWait();

        if (failure != null)
        {
            AIChatLog.Note("stitch_video", "failed: " + failure);
            host.AddSystemInjectionAndBubble("stitch_video could not run: " + failure);
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        // ---- Phase 2: probe + encode, gated like clip_video (Send blocked briefly,
        // footer shows "Importing video").
        BeginVideoImport();
        request.Inputs.Clear();
        double totalSeconds = 0;
        foreach (int idx in sources)
        {
            string path = GetStitchSourcePath(idx);
            FfmpegTool.VideoInfo info = null;
            string error = null;
            yield return FfmpegTool.ProbeVideo(path, (i, e) => { info = i; error = e; });

            if (importEpoch != _videoImportEpoch)
            {
                onDone?.Invoke(false);
                yield break;
            }
            if (info == null || !info.HasVideo)
            {
                FinishVideoImport();
                host.AddSystemInjectionAndBubble($"stitch_video could not inspect Movie #{idx}: {error ?? "no video stream"}");
                host.RequestContinueTurn();
                onDone?.Invoke(false);
                yield break;
            }
            request.Inputs.Add(info);
            totalSeconds += Math.Max(0, info.DurationSeconds);
        }

        string outputPath = FfmpegTool.GetStitchOutputPath();
        FfmpegTool.ClipResult result = null;
        yield return FfmpegTool.StitchClips(request, outputPath, r => result = r);

        if (importEpoch != _videoImportEpoch)
        {
            onDone?.Invoke(false);
            yield break;
        }
        if (result == null || !result.Success)
        {
            FinishVideoImport();
            string err = result != null ? result.Error : "unknown error";
            AIChatLog.Note("stitch_video", "ffmpeg failed: " + err);
            host.AddSystemInjectionAndBubble($"stitch_video failed while joining {DescribeMovieList(sources)}: {err}");
            host.RequestContinueTurn();
            onDone?.Invoke(false);
            yield break;
        }

        FfmpegTool.VideoInfo outputInfo = null;
        yield return FfmpegTool.ProbeVideo(result.OutputPath, (i, e) => { outputInfo = i; });

        if (importEpoch != _videoImportEpoch)
        {
            onDone?.Invoke(false);
            yield break;
        }

        string dims = BuildVideoDimensionsText(outputInfo);
        PicMain pic = AppendVideoClipBubble(result.OutputPath, action, isUserImport: false, dimensions: dims,
            autoCaption: true, updateChainTarget: turnEpoch == _chatTurnEpoch);
        if (pic == null)
        {
            FinishVideoImport();
            host.AddSystemInjectionAndBubble("stitch_video could not load the stitched video into a Movie bubble.");
            onDone?.Invoke(false);
            yield break;
        }

        int newIndex = _chatImagePics.Count;
        double outSeconds = outputInfo != null && outputInfo.DurationSeconds > 0 ? outputInfo.DurationSeconds : totalSeconds;
        string fadeNote = request.CrossfadeSeconds > 0 ? $", {request.CrossfadeSeconds:0.##}s crossfades" : "";
        string summary = $"Stitched {sources.Count} clips ({DescribeMovieList(sources)}) into Movie #{newIndex}: " +
                         $"{outSeconds:0.#}s, {request.Width}x{request.Height} @{request.Fps:0.##}fps{fadeNote}.";
        // Recap-eligible so the model knows which clips make up the new Movie.
        AddSystemMessage(summary);
        AIChatLog.Note("stitch_video", summary + "\n" + (result.Command ?? ""));
        if (action != null && action.Resume)
            host.RequestContinueTurn();
        FinishVideoImport();
        onDone?.Invoke(true);
    }

    /// <summary>
    /// One poll of the stitch sources. Fills <paramref name="pending"/> with the slots
    /// that have no clip file yet and returns a failure message when a source can never
    /// become ready (its Pic is gone).
    /// </summary>
    private string CollectStitchSourceState(List<int> sources, List<int> pending, out bool anyBusy, out bool allReady)
    {
        var host = (IChatHost)this;
        anyBusy = false;
        allReady = true;
        foreach (int idx in sources)
        {
            var pic = GetChatImagePic(idx);
            if (pic == null)
                return $"Movie #{idx} no longer exists (deleted, trimmed, or never spawned).";
            if (pic.IsBusy())
            {
                anyBusy = true;
                allReady = false;
                if (!pending.Contains(idx)) pending.Add(idx);
                continue;
            }
            string path = GetStitchSourcePath(idx);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                allReady = false;
                if (!pending.Contains(idx)) pending.Add(idx);
            }
        }
        return null;
    }

    /// <summary>
    /// Clip file for a stitch source. Falls back to the Pic's own movie file name when
    /// the movie is UNLOADED (the "\" unload-all hotkey deactivates the movie object, so
    /// <c>PicMain.IsMovie()</c> and therefore <c>GetChatImageMovieFilePath</c> report
    /// nothing even though the file is still on disk).
    /// </summary>
    private string GetStitchSourcePath(int idx)
    {
        string path = ((IChatHost)this).GetChatImageMovieFilePath(idx);
        if (!string.IsNullOrEmpty(path)) return path;
        var pic = GetChatImagePic(idx);
        var record = GetChatImageRecord(idx);
        if (pic != null && pic.m_picMovie != null && record != null && record.isMovie)
        {
            path = pic.m_picMovie.GetProcessingFileName();
            if (!string.IsNullOrEmpty(path)) return path;
        }
        return null;
    }

    private void BeginStitchWait()
    {
        if (_stitchWaitCount <= 0)
            _stitchWaitStartTime = Time.unscaledTime;
        _stitchWaitCount++;
    }

    private void EndStitchWait()
    {
        _stitchWaitCount = Mathf.Max(0, _stitchWaitCount - 1);
        if (_stitchWaitCount == 0)
        {
            _stitchWaitStartTime = 0f;
            if (_statusText != null && _lastStitchStatusText != null && _statusText.text == _lastStitchStatusText)
                _statusText.text = "";
            _lastStitchStatusText = null;
        }
    }

    // Footer hint while a stitch waits for its clips. Lowest priority: every other
    // status (streaming, captions, imports, web fetches) overwrites it.
    private void UpdateStitchWaitStatus(int pendingClips, string label = "Stitch")
    {
        if (_statusText == null || _isStreaming || _waitingForForcedMainLLM || _compactSummaryInFlight
            || CountPendingInspectImageJobs() > 0 || _webFetchCount > 0 || _videoImportCount > 0
            || CountPendingVideoCaptions() > 0 || CountPendingAttachmentCaptions() > 0)
            return;
        _stitchSpinnerStep = (_stitchSpinnerStep + 1) % StreamSpinnerFrames.Length;
        float elapsed = Time.unscaledTime - _stitchWaitStartTime;
        _lastStitchStatusText = $"{StreamSpinnerFrames[_stitchSpinnerStep]} {label}: waiting for {pendingClips} clip{(pendingClips == 1 ? "" : "s")} to render   {elapsed:F0}s";
        _statusText.text = _lastStitchStatusText;
    }

    private static string DescribeMovieList(List<int> indices)
    {
        if (indices == null || indices.Count == 0) return "no movies";
        var sb = new StringBuilder();
        for (int i = 0; i < indices.Count; i++)
        {
            if (i > 0) sb.Append(i == indices.Count - 1 ? " and " : ", ");
            sb.Append(i == 0 ? "Movie #" : "#").Append(indices[i]);
        }
        return sb.ToString();
    }

    void IChatHost.RecordChatImageProvenance(PicMain pic, SkillAction action)
    {
        RecordChainedProvenance(pic, action);
    }

    byte[] IChatHost.GetChatImagePngBytes(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null) return null;
        return pic.TryGetImageAsPng(out byte[] png) ? png : null;
    }

    string IChatHost.GetChatImageMovieFilePath(int oneBasedIndex)
    {
        var pic = GetChatImagePic(oneBasedIndex);
        if (pic == null || pic.m_picMovie == null || !pic.IsMovie())
            return null;
        string path = pic.m_picMovie.GetProcessingFileName();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    byte[] IChatHost.GetChatImageCleanBasePngBytes(int oneBasedIndex)
    {
        var record = GetChatImageRecord(oneBasedIndex);
        return record != null ? record.cleanBasePngBytes : null;
    }

    bool IChatHost.CaptureCleanBaseIfMissing(PicMain pic)
    {
        var record = FindChatImageRecord(pic);
        if (record == null)
            return false;
        if (record.cleanBasePngBytes != null && record.cleanBasePngBytes.Length > 0)
            return true;

        if (!TryEncodeCurrentPicTexture(pic, out byte[] png, out string dimensions))
            return false;

        record.cleanBasePngBytes = png;
        record.cleanBaseDimensions = dimensions;
        return true;
    }

    private ChatImageRecord GetChatImageRecord(int oneBasedIndex)
    {
        int idx0 = oneBasedIndex - 1;
        if (_chatImageRecords == null || idx0 < 0 || idx0 >= _chatImageRecords.Count) return null;
        return _chatImageRecords[idx0];
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

    int IChatHost.GetLatestStillChatImageIndex()
    {
        if (_chatImagePics == null) return 0;
        for (int i = _chatImagePics.Count - 1; i >= 0; i--)
        {
            var pic = _chatImagePics[i];
            if (pic == null || pic.gameObject == null) continue;
            var record = (_chatImageRecords != null && i < _chatImageRecords.Count) ? _chatImageRecords[i] : null;
            bool isMovie = (record != null && record.isMovie) || pic.IsMovie();
            if (!isMovie)
                return i + 1;
        }
        return 0;
    }

    int IChatHost.ResolvePasteAttachmentToChatIndex(int oneBasedAttachment)
    {
        int idx0 = oneBasedAttachment - 1;
        if (idx0 < 0 || idx0 >= _lastPasteGroupPics.Count) return 0;
        var pic = _lastPasteGroupPics[idx0];
        if (pic == null || pic.gameObject == null) return 0;
        return ((IChatHost)this).GetChatImageIndexForPic(pic);
    }

    bool IChatHost.IsChatImageUserAttachment(int oneBasedIndex)
    {
        var record = GetChatImageRecord(oneBasedIndex);
        return record != null && record.isUserAttachment;
    }

    bool IChatHost.IsChatImageMovie(int oneBasedIndex)
    {
        // Same "record flag OR live state" test BuildChatImageStatesForPrompt uses:
        // the record flag is set at spawn, so a movie whose clip is still rendering
        // (PicMovie.IsMovie() false until the file exists) still counts as a movie.
        var record = GetChatImageRecord(oneBasedIndex);
        if (record != null && record.isMovie) return true;
        var pic = GetChatImagePic(oneBasedIndex);
        return pic != null && pic.IsMovie();
    }

    bool IChatHost.IsChatPicMovie(PicMain pic)
    {
        if (pic == null) return false;
        var record = FindChatImageRecord(pic);
        if (record != null && record.isMovie) return true;
        return pic.IsMovie();
    }

    int IChatHost.GetChatImageIndexForPic(PicMain pic)
    {
        if (pic == null || _chatImagePics == null) return 0;
        for (int i = _chatImagePics.Count - 1; i >= 0; i--)
        {
            var candidate = _chatImagePics[i];
            if (candidate != null && candidate == pic)
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
        // Use the SHORT caption for the volatile CURRENT STATE block: it is re-prefilled
        // every turn (it rides at the tail, past the cached prefix), so the verbose
        // paragraph form would re-cost ~hundreds of tokens per image each turn for no
        // gain - the full caption already lives in cached history where each image was
        // introduced. Fall back to the long form only if no short caption exists.
        if (!string.IsNullOrEmpty(pic.CaptionShort)) return pic.CaptionShort;
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

    private static bool TryEncodeCurrentPicTexture(PicMain pic, out byte[] pngBytes, out string dimensions)
    {
        pngBytes = null;
        dimensions = null;
        if (pic == null || !pic.TryGetCurrentTexture(out Texture tex) || tex == null)
            return false;

        if (tex is Texture2D tex2d)
        {
            try
            {
                pngBytes = tex2d.EncodeToPNG();
                dimensions = tex2d.width + "x" + tex2d.height;
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("AIChatPanel: clean-base Texture2D EncodeToPNG failed: " + ex.Message);
                return false;
            }
        }

        if (tex is RenderTexture rt)
        {
            var prev = RenderTexture.active;
            Texture2D snap = null;
            try
            {
                RenderTexture.active = rt;
                snap = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                snap.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                snap.Apply();
                pngBytes = snap.EncodeToPNG();
                dimensions = rt.width + "x" + rt.height;
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("AIChatPanel: clean-base RenderTexture EncodeToPNG failed: " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (snap != null) Destroy(snap);
            }
        }

        return false;
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
    /// Build the "ANCHORS: Bob=#3, layout_canvas=#5" line for the volatile CURRENT STATE block,
    /// listing only anchors whose Pic still has a live slot (resolving each name through
    /// the same path the executor uses, which also prunes dead entries). Returns "" when
    /// no live anchors exist, so the state block simply omits the line.
    /// </summary>
    private string BuildAnchorsStateLine()
    {
        string pairs = BuildAnchorSlotPairs();
        if (string.IsNullOrEmpty(pairs)) return "";
        return "ANCHORS (named reusable images - reference by NAME via chat_image=\"<name>\" or source_chat_image=\"<name>\"): "
               + pairs;
    }

    /// <summary>
    /// "Bob=#3, layout_canvas=#5" for every anchor whose Pic still has a live slot,
    /// or "" when none. Shared by the ANCHORS state line and the compact summary's
    /// image-window clause.
    /// </summary>
    private string BuildAnchorSlotPairs()
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
        return string.Join(", ", parts);
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
        var scrollState = CaptureFontResizeScrollState();
        int restoreVersion = ++_fontResizeScrollRestoreVersion;

        _fontSizeMultiplier = Mathf.Clamp(
            _fontSizeMultiplier + wheelDelta * FontMultiplierStep,
            MinFontMultiplier, MaxFontMultiplier);
        ApplyChatFontSize();
        StartCoroutine(RestoreFontResizeScrollStateDeferred(restoreVersion, scrollState));
    }

    private struct FontResizeScrollState
    {
        public bool hasChatScroll;
        public float chatScroll;
        public bool hasMediaScroll;
        public float mediaScroll;
        public bool hasInputScrollbar;
        public float inputScrollbar;
    }

    private FontResizeScrollState CaptureFontResizeScrollState()
    {
        var state = new FontResizeScrollState();
        if (_chatScroll != null)
        {
            state.hasChatScroll = true;
            state.chatScroll = _chatScroll.verticalNormalizedPosition;
        }

        if (_mediaScroll != null)
        {
            state.hasMediaScroll = true;
            state.mediaScroll = _mediaScroll.verticalNormalizedPosition;
        }

        if (_inputField != null && _inputField.verticalScrollbar != null)
        {
            state.hasInputScrollbar = true;
            state.inputScrollbar = _inputField.verticalScrollbar.value;
        }

        return state;
    }

    private IEnumerator RestoreFontResizeScrollStateDeferred(int version, FontResizeScrollState state)
    {
        // Bubble heights settle through ResizeBubbleDeferred over two frames. Restore
        // for a few frames so Ctrl+wheel changes font size without walking the scrollbars.
        for (int i = 0; i < 4; i++)
        {
            yield return null;
            if (version != _fontResizeScrollRestoreVersion)
                yield break;

            Canvas.ForceUpdateCanvases();
            RestoreFontResizeScrollState(state);
        }
    }

    private void RestoreFontResizeScrollState(FontResizeScrollState state)
    {
        if (state.hasChatScroll && _chatScroll != null)
        {
            _chatScroll.StopMovement();
            _chatScroll.verticalNormalizedPosition = state.chatScroll;
        }

        if (state.hasMediaScroll && _mediaScroll != null)
        {
            _mediaScroll.StopMovement();
            _mediaScroll.verticalNormalizedPosition = state.mediaScroll;
        }

        if (state.hasInputScrollbar && _inputField != null && _inputField.verticalScrollbar != null)
            _inputField.verticalScrollbar.value = state.inputScrollbar;
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
            {
                if (_bubbleContextMenuRoot != null || _rewindConfirmRoot != null)
                {
                    HideBubbleContextMenu();
                    HideRewindConfirmation();
                }
                else
                {
                    Hide();
                }
            }

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
            _statusText.text = $"{StreamSpinnerFrames[_compactSpinnerStep]} Summarizing {_compactSummaryMsgCount} msgs (~{FormatApproxTokenCount(_compactSummaryApproxSentTokens)} tok sent)   {elapsed:F0}s";
        }

        if (_attachmentCaptionQueue.Count > 0 && Time.unscaledTime >= _attachmentCaptionNextDispatch)
        {
            _attachmentCaptionNextDispatch = Time.unscaledTime + 0.5f;
            ProcessAttachmentCaptionQueue();
        }
        if (_inspectImageQueue.Count > 0 && Time.unscaledTime >= _inspectImageNextDispatch)
        {
            _inspectImageNextDispatch = Time.unscaledTime + 0.5f;
            ProcessInspectImageQueue();
        }

        UpdateSpeechControls();

        // Attachment captioning can run before the user sends, so give it the same
        // "still working" treatment as chat streaming and compact-summary requests.
        if (!_isStreaming && !_waitingForForcedMainLLM && !_compactSummaryInFlight)
        {
            UpdateInspectImageStatus();
            UpdateWebFetchStatus();
            UpdateVideoImportStatus();
            UpdateAttachmentCaptionStatus();
        }

        // Periodic header status pill refresh (cheap; reads counters from Config/LLM mgr).
        if (Time.unscaledTime >= _statusPillNextRefresh)
        {
            _statusPillNextRefresh = Time.unscaledTime + STATUS_PILL_REFRESH_INTERVAL;
            SubscribeToLLMInstanceChanges();
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
        if (_isVisible && _inputField != null && _inputField.isFocused)
        {
            HandlePromptHistoryArrowKeys();

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
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

        UpdatePromptHistoryCaretCache();
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

    private class AIChatBubbleContextClickHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        private AIChatPanel _panel;
        private TMP_InputField _field;
        private GTPChatLine _interaction;
        private bool _isEntryInput;
        private bool _suppressCacheUntilLeftReleased;

        public void Setup(AIChatPanel panel, TMP_InputField field, GTPChatLine interaction, bool isEntryInput = false)
        {
            _panel = panel;
            _field = field;
            _interaction = interaction;
            _isEntryInput = isEntryInput;
        }

        private void Update()
        {
            if (_suppressCacheUntilLeftReleased)
            {
                if (Input.GetMouseButton(0))
                    return;
                _suppressCacheUntilLeftReleased = false;
            }

            _panel?.TrackBubbleSelection(_field);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _panel?.ClearCachedSpeakSelection(_field);
            _panel?.ClearSpeechSelectionOverlay();
            _suppressCacheUntilLeftReleased = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _suppressCacheUntilLeftReleased = false;
            _panel?.TrackBubbleSelection(_field);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                // A left click on a "[skill: X]" link toggles the tool-call details panel.
                if (_isEntryInput || _panel == null || _field == null || _field.textComponent == null)
                    return;
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(_field.textComponent, eventData.position, eventData.pressEventCamera);
                if (linkIndex < 0) return;
                var linkInfo = _field.textComponent.textInfo.linkInfo;
                if (linkInfo == null || linkIndex >= linkInfo.Length) return;
                if (linkInfo[linkIndex].GetLinkID() != "skill") return;
                _panel.ToggleActionDetails(_field, _interaction, linkIndex);
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Right)
                return;
            if (_isEntryInput)
                _panel?.OnEntryInputRightClicked(_field, eventData.position, eventData.pressEventCamera);
            else
                _panel?.OnBubbleRightClicked(_field, _interaction, eventData.position, eventData.pressEventCamera);
        }
    }

    /// <summary>
    /// Per-bubble state for the click-to-expand tool-call details: which "[skill: X]"
    /// markers (by link ordinal) are open, and the read-only TMP text under the bubble
    /// that lists their attributes. Lives on the bubble root next to its layout group.
    /// </summary>
    private class ActionDetailsPanel : MonoBehaviour
    {
        public readonly HashSet<int> Expanded = new HashSet<int>();
        public TextMeshProUGUI Text;
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
/// Horizontal divider handle on the top bar of the AIChatPanel footer. Drags vertically
/// to grow/shrink the input box: dragging up makes the footer taller (the columns above
/// shrink), dragging down makes it shorter. Calls back into AIChatPanel.ApplyFooterHeight
/// which clamps and re-lays-out the footer/body/input. Vertical-axis twin of ChatSplitterHandle.
/// </summary>
public class FooterResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private AIChatPanel _panel;
    private RectTransform _panelRT;   // _mainPanel, the common reference frame for the drag
    private Vector2 _startPointerLocal;
    private float _startHeight;

    public void SetTarget(AIChatPanel panel, RectTransform panelRT, float startHeight)
    {
        _panel = panel;
        _panelRT = panelRT;
        _startHeight = startHeight;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_panelRT == null || _panel == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _panelRT, eventData.position, eventData.pressEventCamera, out _startPointerLocal);
        _startHeight = _panel.CurrentFooterBaseHeight;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_panelRT == null || _panel == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _panelRT, eventData.position, eventData.pressEventCamera, out var nowLocal))
            return;

        // The footer is anchored to the window's bottom, so a taller footer means the
        // divider moves UP: positive local-Y delta -> larger base height.
        float deltaY = nowLocal.y - _startPointerLocal.y;
        _panel.ApplyFooterHeight(_startHeight + deltaY);
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
        {
            data.Use();
            return;
        }
        base.OnScroll(data);
    }
}

/// <summary>
/// Runs before the EventSystem so Ctrl+wheel never reaches AI Chat ScrollRects or
/// TMP_InputFields with a non-zero scroll sensitivity. AIChatPanel.Update then handles
/// the same wheel delta as a font-size gesture.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class AIChatCtrlWheelScrollSuppressor : MonoBehaviour
{
    private readonly Dictionary<TMP_InputField, float> _inputSensitivities = new Dictionary<TMP_InputField, float>();
    private readonly Dictionary<ScrollRect, float> _scrollSensitivities = new Dictionary<ScrollRect, float>();
    private bool _suppressing;

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            SuppressScrolling();
        else
            RestoreScrolling();
    }

    private void OnDisable()
    {
        RestoreScrolling();
    }

    private void OnDestroy()
    {
        RestoreScrolling();
    }

    private void SuppressScrolling()
    {
        _suppressing = true;

        foreach (var input in GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input == null)
                continue;
            if (!_inputSensitivities.ContainsKey(input))
                _inputSensitivities[input] = input.scrollSensitivity;
            input.scrollSensitivity = 0f;
        }

        foreach (var scroll in GetComponentsInChildren<ScrollRect>(true))
        {
            if (scroll == null)
                continue;
            if (!_scrollSensitivities.ContainsKey(scroll))
                _scrollSensitivities[scroll] = scroll.scrollSensitivity;
            scroll.scrollSensitivity = 0f;
        }
    }

    private void RestoreScrolling()
    {
        if (!_suppressing)
            return;

        foreach (var pair in _inputSensitivities)
        {
            if (pair.Key != null)
                pair.Key.scrollSensitivity = pair.Value;
        }
        _inputSensitivities.Clear();

        foreach (var pair in _scrollSensitivities)
        {
            if (pair.Key != null)
                pair.Key.scrollSensitivity = pair.Value;
        }
        _scrollSensitivities.Clear();

        _suppressing = false;
    }
}

/// <summary>
/// Keeps a Scrollbar thumb visible in very long AI Chat/media lists. Unity's
/// Scrollbar drives the handle anchors from content ratio, so this clamps the
/// visual anchor span after Scrollbar updates instead of changing scroll state.
/// </summary>
public class MinScrollbarHandleSize : MonoBehaviour
{
    private Scrollbar _scrollbar;
    private RectTransform _handleRect;
    private float _minPixels = 32f;

    public void SetTarget(Scrollbar scrollbar, RectTransform handleRect, float minPixels)
    {
        _scrollbar = scrollbar;
        _handleRect = handleRect;
        _minPixels = Mathf.Max(1f, minPixels);
        Apply();
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += Apply;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= Apply;
    }

    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        if (_scrollbar == null || _handleRect == null)
            return;

        RectTransform track = _handleRect.parent as RectTransform;
        if (track == null)
            return;

        int axis = (_scrollbar.direction == Scrollbar.Direction.LeftToRight
            || _scrollbar.direction == Scrollbar.Direction.RightToLeft) ? 0 : 1;
        float trackSize = axis == 0 ? track.rect.width : track.rect.height;
        if (trackSize <= 0f)
            return;

        Vector2 anchorMin = _handleRect.anchorMin;
        Vector2 anchorMax = _handleRect.anchorMax;
        float span = anchorMax[axis] - anchorMin[axis];
        float offsetSpan = _handleRect.offsetMax[axis] - _handleRect.offsetMin[axis];
        float minSpan = Mathf.Clamp01((_minPixels - offsetSpan) / trackSize);
        if (span >= minSpan)
            return;

        float value = Mathf.Clamp01(_scrollbar.value);
        bool reverse = _scrollbar.direction == Scrollbar.Direction.RightToLeft
            || _scrollbar.direction == Scrollbar.Direction.TopToBottom;
        float start = value * (1f - minSpan);
        if (reverse)
            start = 1f - minSpan - start;

        anchorMin[axis] = start;
        anchorMax[axis] = start + minSpan;
        _handleRect.anchorMin = anchorMin;
        _handleRect.anchorMax = anchorMax;
    }
}

/// <summary>
/// Forwards regular mouse-wheel scroll events to a target ScrollRect. We attach this
/// to multiline AI Chat TMP_InputFields because TMP_InputField itself implements
/// IScrollHandler and Unity invokes every IScrollHandler on the hit GameObject.
/// </summary>
public class ChatScrollForwarder : MonoBehaviour, IScrollHandler
{
    public ScrollRect target;

    public void OnScroll(PointerEventData data)
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            data.Use();
            return;
        }

        if (target != null) target.OnScroll(data);
    }
}
