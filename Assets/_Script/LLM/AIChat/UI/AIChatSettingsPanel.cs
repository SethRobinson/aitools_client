using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AITools.AIChat.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace AITools.AIChat.UI
{
    /// <summary>
    /// Lightweight settings panel for the AI Chat skills system. Lets the user:
    /// <list type="bullet">
    /// <item>Edit <c>aichat/main_prompt.txt</c> directly inline (loads on open, saves on close).</item>
    /// <item>See every skill loaded from <c>aichat/skills/*.md</c> with id + summary + path.</item>
    /// <item>Reload from disk (in case skills were edited externally) or open the folder
    /// in Explorer to add/edit files.</item>
    /// </list>
    ///
    /// Same procedural-panel pattern as LLMSettingsPanel / AIChatPanel: static Show/Hide,
    /// own Canvas, draggable header, ESC to close. Pure C# - no scene-side wiring.
    /// </summary>
    public class AIChatSettingsPanel : MonoBehaviour
    {
        private static AIChatSettingsPanel _instance;
        private static GameObject _panelRoot;
        private static SkillManager _staticSkillManager;
        private static Action _staticOnClose;

        private TMP_FontAsset _font;
        private RectTransform _mainPanel;
        private TMP_InputField _mainPromptField;
        private TextMeshProUGUI _mainPromptLabelTmp;
        // The main-prompt file the editor loaded from, and the text it loaded, captured
        // at load time. SaveAndClose writes back to this exact path (so a test_ edit
        // never lands on main_prompt.txt) and only when the text actually changed.
        private string _activeMainPromptPath;
        private string _loadedMainPromptText = "";
        private TMP_InputField _maxEdgeField;
        private RectTransform _skillsContent;
        private TMP_InputField _keepLastNField;
        private TMP_InputField _presetPrefixField;
        private TMP_InputField _compactKeepNField;
        private TMP_InputField _imageContextLimitField;
        private TMP_InputField _userPostMessageField;
        private Toggle _keepOldToolCallsToggle;
        private Toggle _autoCaptionGeneratedImagesToggle;
        private Toggle _showDebugStuffToggle;

        private const float DEFAULT_WIDTH = 760f;
        private const float DEFAULT_HEIGHT = 654f;
        private const float HEADER_HEIGHT = 40f;
        // Tall enough for the session reminder row, prompt-slimming toggles,
        // Compact controls, and one settings row above the bottom button row.
        private const float FOOTER_HEIGHT = 184f;
        private const float BaseFontSize = 13f;

        private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
        private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
        private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
        private static readonly Color RowBg = new Color(0.92f, 0.92f, 0.94f, 1f);
        private static readonly Color InputFieldBg = Color.white;
        private static readonly Color TextDark = Color.black;

        public static void Show(SkillManager skillManager, Action onCloseReloaded)
        {
            _staticSkillManager = skillManager;
            _staticOnClose = onCloseReloaded;

            if (_instance != null)
            {
                _panelRoot.SetActive(true);
                _instance.LoadFromManager();
                return;
            }

            _panelRoot = new GameObject("AIChatSettingsPanel");
            _instance = _panelRoot.AddComponent<AIChatSettingsPanel>();
            _instance.CreateUI();
        }

        public static void Hide()
        {
            if (_panelRoot != null)
                _panelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            _instance = null;
            _panelRoot = null;
        }

        private TMP_FontAsset FindFont()
        {
            var existing = FindAnyObjectByType<TextMeshProUGUI>();
            return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        // ---------- UI construction ----------

        private void CreateUI()
        {
            _font = FindFont();

            var canvas = _panelRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110; // above the chat panel
            var scaler = _panelRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _panelRoot.AddComponent<GraphicRaycaster>();

            var main = new GameObject("MainPanel");
            main.transform.SetParent(_panelRoot.transform, false);
            _mainPanel = main.AddComponent<RectTransform>();
            _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _mainPanel.pivot = new Vector2(0.5f, 0.5f);
            _mainPanel.sizeDelta = new Vector2(DEFAULT_WIDTH, DEFAULT_HEIGHT);
            main.AddComponent<Image>().color = PanelBg;

            CreateHeader();
            CreateBody();
            CreateFooter();

            LoadFromManager();
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
            header.AddComponent<Image>().color = HeaderBg;
            header.AddComponent<PanelDragHandler>().SetTarget(_mainPanel, HEADER_HEIGHT);

            var title = new GameObject("Title");
            title.transform.SetParent(header.transform, false);
            var titleRt = title.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0, 0);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.offsetMin = new Vector2(12, 0);
            titleRt.offsetMax = new Vector2(-36, 0);
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "AI Chat Settings";
            titleTmp.font = _font;
            titleTmp.fontSize = 17;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = TextDark;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            RTWindowChrome.CreateCloseButton(rt, SaveAndClose);
        }

        private void CreateBody()
        {
            // Body region between header (top) and footer (bottom).
            var body = new GameObject("Body");
            body.transform.SetParent(_mainPanel, false);
            var rt = body.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(8, FOOTER_HEIGHT);
            rt.offsetMax = new Vector2(-8, -HEADER_HEIGHT - 4f);

            // Top half: main_prompt.txt editor. Label text is finalized in
            // LoadFromManager so it names the file actually in play (main_prompt.txt or,
            // in "test_" preset mode, test_main_prompt.txt).
            var promptLabel = MakeLabel("Main system prompt (aichat/main_prompt.txt) - edited live; saved when you close this panel:");
            promptLabel.transform.SetParent(body.transform, false);
            _mainPromptLabelTmp = promptLabel.GetComponent<TextMeshProUGUI>();
            var labRt = promptLabel.GetComponent<RectTransform>();
            labRt.anchorMin = new Vector2(0, 1);
            labRt.anchorMax = new Vector2(1, 1);
            labRt.pivot = new Vector2(0.5f, 1);
            labRt.sizeDelta = new Vector2(0, 18);
            labRt.anchoredPosition = new Vector2(0, -2);

            var promptInputGo = CreateInputFieldObject("MainPromptInput", multiline: true);
            promptInputGo.transform.SetParent(body.transform, false);
            var pRt = promptInputGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0, 0.5f);
            pRt.anchorMax = new Vector2(1, 1);
            pRt.offsetMin = new Vector2(0, 4);
            pRt.offsetMax = new Vector2(0, -22);

            var promptImg = promptInputGo.GetComponent<Image>();
            if (promptImg != null) { promptImg.sprite = null; promptImg.color = InputFieldBg; }
            _mainPromptField = promptInputGo.GetComponent<TMP_InputField>();
            _mainPromptField.lineType = TMP_InputField.LineType.MultiLineNewline;
            _mainPromptField.textComponent.alignment = TextAlignmentOptions.TopLeft;
            _mainPromptField.textComponent.color = TextDark;
            _mainPromptField.textComponent.font = _font;
            _mainPromptField.textComponent.fontSize = BaseFontSize;
            _mainPromptField.textComponent.textWrappingMode = TextWrappingModes.Normal;
            ApplyFatCaret(_mainPromptField);
            InstallCaretFixer(_mainPromptField);
            if (_mainPromptField.placeholder is TextMeshProUGUI pp)
            {
                pp.text = "Type the main system prompt here...";
                pp.color = new Color(0, 0, 0, 0.4f);
                pp.font = _font;
                pp.fontSize = BaseFontSize;
            }

            // Vertical scrollbar for the main prompt. TMP_InputField will drive
            // it automatically once assigned to verticalScrollbar. We shrink the
            // text viewport so the bar doesn't draw on top of the wrapped text.
            if (_mainPromptField.textViewport != null)
            {
                var vp = _mainPromptField.textViewport;
                vp.offsetMax = new Vector2(-22, vp.offsetMax.y);
            }
            _mainPromptField.verticalScrollbar = CreateVerticalScrollbar(promptInputGo.transform);

            // Bottom half: scrollable skill list.
            var skillsLabel = MakeLabel("Loaded skills (aichat/skills/*.md) - call read_skill in chat to load full body for any:");
            skillsLabel.transform.SetParent(body.transform, false);
            var skLabRt = skillsLabel.GetComponent<RectTransform>();
            skLabRt.anchorMin = new Vector2(0, 0.5f);
            skLabRt.anchorMax = new Vector2(1, 0.5f);
            skLabRt.pivot = new Vector2(0.5f, 1);
            skLabRt.sizeDelta = new Vector2(0, 18);
            skLabRt.anchoredPosition = new Vector2(0, -2);

            var skScrollGo = new GameObject("SkillsScroll");
            skScrollGo.transform.SetParent(body.transform, false);
            var skScrollRt = skScrollGo.AddComponent<RectTransform>();
            skScrollRt.anchorMin = new Vector2(0, 0);
            skScrollRt.anchorMax = new Vector2(1, 0.5f);
            skScrollRt.offsetMin = new Vector2(0, 4);
            skScrollRt.offsetMax = new Vector2(0, -22);
            var skScroll = skScrollGo.AddComponent<ScrollRect>();
            skScroll.horizontal = false;
            skScroll.vertical = true;
            skScroll.movementType = ScrollRect.MovementType.Clamped;

            var skVp = new GameObject("Viewport");
            skVp.transform.SetParent(skScrollGo.transform, false);
            var skVpRt = skVp.AddComponent<RectTransform>();
            skVpRt.anchorMin = Vector2.zero;
            skVpRt.anchorMax = Vector2.one;
            skVpRt.offsetMin = Vector2.zero;
            skVpRt.offsetMax = new Vector2(-22, 0);
            skVp.AddComponent<Image>().color = new Color(0.92f, 0.92f, 0.95f, 1f);
            skVp.AddComponent<Mask>().showMaskGraphic = true;

            var skContent = new GameObject("Content");
            skContent.transform.SetParent(skVp.transform, false);
            _skillsContent = skContent.AddComponent<RectTransform>();
            _skillsContent.anchorMin = new Vector2(0, 1);
            _skillsContent.anchorMax = new Vector2(1, 1);
            _skillsContent.pivot = new Vector2(0.5f, 1);
            _skillsContent.sizeDelta = Vector2.zero;
            var vlg = skContent.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            skContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            skScroll.viewport = skVpRt;
            skScroll.content = _skillsContent;

            skScroll.verticalScrollbar = CreateVerticalScrollbar(skScrollGo.transform);
        }

        // The single dark vertical-scrollbar style shared by both scroll regions of
        // this panel (the main-prompt editor and the skills list). Returns the
        // Scrollbar so the caller can wire it to a TMP_InputField or a ScrollRect.
        // One source of truth so the two bars can't drift apart again.
        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var sbGo = new GameObject("Scrollbar");
            sbGo.transform.SetParent(parent, false);
            var sbRt = sbGo.AddComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1, 0);
            sbRt.anchorMax = new Vector2(1, 1);
            sbRt.pivot = new Vector2(1, 0.5f);
            sbRt.sizeDelta = new Vector2(18, 0);
            sbRt.anchoredPosition = Vector2.zero;
            sbGo.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.24f, 1f);

            var sb = sbGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(sbGo.transform, false);
            var handleRt = handle.AddComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = new Vector2(3, 3);
            handleRt.offsetMax = new Vector2(-3, -3);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(0.45f, 0.45f, 0.5f, 1f);
            sb.handleRect = handleRt;
            sb.targetGraphic = handleImg;
            return sb;
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
            footer.AddComponent<Image>().color = FooterBg;

            var reloadBtn = MakeButton(footer.transform, "Reload skills", new Vector2(8, 10), new Vector2(140, 30), () =>
            {
                _staticSkillManager?.Reload();
                LoadFromManager();
            });
            reloadBtn.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            reloadBtn.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0);
            reloadBtn.GetComponent<RectTransform>().pivot = new Vector2(0, 0);

            var openBtn = MakeButton(footer.transform, "Open aichat folder", new Vector2(156, 10), new Vector2(180, 30), OpenAIChatFolder);
            openBtn.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            openBtn.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0);
            openBtn.GetComponent<RectTransform>().pivot = new Vector2(0, 0);

            var saveBtn = MakeButton(footer.transform, "Save & Close", new Vector2(-8, 10), new Vector2(140, 30), SaveAndClose);
            saveBtn.GetComponent<RectTransform>().anchorMin = new Vector2(1, 0);
            saveBtn.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0);
            saveBtn.GetComponent<RectTransform>().pivot = new Vector2(1, 0);

            // Single compact settings row above the buttons: three label+input
            // pairs laid out left-to-right (max attachment size, preset prefix,
            // keep last N media). Each pair has a transparent hit-box parent
            // with an RTToolTip explaining what the setting does.
            const float kSettingsRowY = 46f;
            const float kSettingsLeftPad = 8f;
            const float kSettingsGap = 14f;

            float x = kSettingsLeftPad;

            const float kMaxEdgeLabelW = 200f;
            const float kMaxEdgeInputW = 60f;
            BuildSettingPair(
                footer.transform,
                "MaxEdgePair",
                "Max attachment size (px, 0 = off):",
                kMaxEdgeLabelW,
                kMaxEdgeInputW,
                new Vector2(x, kSettingsRowY),
                "Maximum pixel size for the longest edge of image attachments. Larger images dropped or pasted into the chat are downscaled to this before being sent. Set to 0 to disable resizing (sends originals as-is).",
                out _maxEdgeField);
            _maxEdgeField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _maxEdgeField.characterLimit = 5;
            _maxEdgeField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_maxEdgeField.placeholder is TextMeshProUGUI mep)
            {
                mep.text = "1024";
                mep.color = new Color(0, 0, 0, 0.4f);
                mep.font = _font;
                mep.fontSize = BaseFontSize;
                mep.alignment = TextAlignmentOptions.Center;
            }
            x += kMaxEdgeLabelW + 4f + kMaxEdgeInputW + kSettingsGap;

            const float kPrefixLabelW = 80f;
            const float kPrefixInputW = 110f;
            BuildSettingPair(
                footer.transform,
                "PrefixPair",
                "Preset prefix:",
                kPrefixLabelW,
                kPrefixInputW,
                new Vector2(x, kSettingsRowY),
                "Optional string prepended to every {{preset_name}} marker found in skills and prompt files at chat-build time. Lets you switch families of presets and test_ prompt overrides. Empty = use bare names.",
                out _presetPrefixField);
            _presetPrefixField.contentType = TMP_InputField.ContentType.Standard;
            _presetPrefixField.characterLimit = 32;
            _presetPrefixField.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            if (_presetPrefixField.placeholder is TextMeshProUGUI pp2)
            {
                pp2.text = "(none)";
                pp2.color = new Color(0, 0, 0, 0.4f);
                pp2.font = _font;
                pp2.fontSize = BaseFontSize - 1;
                pp2.alignment = TextAlignmentOptions.MidlineLeft;
            }
            x += kPrefixLabelW + 4f + kPrefixInputW + kSettingsGap;

            const float kKeepLabelW = 165f;
            const float kKeepInputW = 50f;
            BuildSettingPair(
                footer.transform,
                "KeepLastNPair",
                "Keep last N media on Clear:",
                kKeepLabelW,
                kKeepInputW,
                new Vector2(x, kSettingsRowY),
                "When you press Clear in the chat, this many of the most recent media items (images/videos) are preserved. Older media is removed. Set to 0 to clear everything.",
                out _keepLastNField);
            _keepLastNField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _keepLastNField.characterLimit = 4;
            _keepLastNField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_keepLastNField.placeholder is TextMeshProUGUI kp)
            {
                kp.text = "10";
                kp.color = new Color(0, 0, 0, 0.4f);
                kp.font = _font;
                kp.fontSize = BaseFontSize;
                kp.alignment = TextAlignmentOptions.Center;
            }

            // ---- Compact row (above the settings row): shrink a long chat
            // without deleting any images. "keep last N exchanges" feeds both
            // buttons; Truncate just drops older messages, Summarize replaces
            // them with one LLM-written recap.
            const float kCompactRowY = 84f;

            const float kCompactLabelW = 235f;
            const float kCompactInputW = 46f;
            BuildSettingPair(
                footer.transform,
                "CompactPair",
                "Compact chat - keep last N exchanges:",
                kCompactLabelW,
                kCompactInputW,
                new Vector2(kSettingsLeftPad, kCompactRowY),
                "Shrinks a long, slow conversation. \"Truncate\" simply drops everything older than the last N user/assistant exchanges. \"Summarize\" asks the active LLM to condense everything older into one recap message (preserving key points and what each chat_image=\"N\" depicts), then keeps the last N exchanges verbatim. Neither deletes any images - the media panel and image references stay intact.",
                out _compactKeepNField);
            _compactKeepNField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _compactKeepNField.characterLimit = 4;
            _compactKeepNField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_compactKeepNField.placeholder is TextMeshProUGUI cp)
            {
                cp.text = "5";
                cp.color = new Color(0, 0, 0, 0.4f);
                cp.font = _font;
                cp.fontSize = BaseFontSize;
                cp.alignment = TextAlignmentOptions.Center;
            }

            float compactBtnX = kSettingsLeftPad + kCompactLabelW + 4f + kCompactInputW + 12f;
            var truncBtn = MakeButton(footer.transform, "Truncate", new Vector2(compactBtnX, kCompactRowY), new Vector2(110, 26), OnCompactTruncate);
            truncBtn.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            truncBtn.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0);
            truncBtn.GetComponent<RectTransform>().pivot = new Vector2(0, 0);

            var sumBtn = MakeButton(footer.transform, "Summarize", new Vector2(compactBtnX + 118f, kCompactRowY), new Vector2(120, 26), OnCompactSummarize);
            sumBtn.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            sumBtn.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0);
            sumBtn.GetComponent<RectTransform>().pivot = new Vector2(0, 0);

            const float kImageContextLabelW = 130f;
            const float kImageContextInputW = 46f;
            BuildSettingPair(
                footer.transform,
                "ImageContextLimitPair",
                "Image context limit:",
                kImageContextLabelW,
                kImageContextInputW,
                new Vector2(compactBtnX + 252f, kCompactRowY),
                "Maximum number of newest chat images described in the outgoing CHAT IMAGES prompt block. Set 0 to hide per-image records. This does not delete media or prevent tools from using existing chat_image slots.",
                out _imageContextLimitField);
            _imageContextLimitField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _imageContextLimitField.characterLimit = 3;
            _imageContextLimitField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_imageContextLimitField.placeholder is TextMeshProUGUI icp)
            {
                icp.text = "40";
                icp.color = new Color(0, 0, 0, 0.4f);
                icp.font = _font;
                icp.fontSize = BaseFontSize;
                icp.alignment = TextAlignmentOptions.Center;
            }

            const float kSlimRowY = 118f;
            _keepOldToolCallsToggle = MakeSettingToggle(
                footer.transform,
                "Keep old tool calls in prompt",
                new Vector2(kSettingsLeftPad, kSlimRowY),
                new Vector2(220, 24),
                AIChatPanel.GetKeepOldToolCallsInPrompt(),
                "When enabled, prior assistant <aitools_action .../> XML stays in future chat prompts so server prompt caches can match prior assistant output exactly. Off is leaner but intentionally breaks that cache reuse.");

            _autoCaptionGeneratedImagesToggle = MakeSettingToggle(
                footer.transform,
                "Auto-caption generated images",
                new Vector2(kSettingsLeftPad + 238f, kSlimRowY),
                new Vector2(240, 24),
                AIChatPanel.GetAutoCaptionGeneratedImages(),
                "When enabled, images generated by AI Chat are sent through the vision caption sidecar and their captions can appear in CURRENT STATE. Off avoids repeated caption calls/text for images the app generated itself.");

            _showDebugStuffToggle = MakeSettingToggle(
                footer.transform,
                "Show debug stuff",
                new Vector2(kSettingsLeftPad + 496f, kSlimRowY),
                new Vector2(180, 24),
                AIChatPanel.GetShowDebugStuff(),
                "When enabled, local Info/debug bubbles are shown in the chat. Off hides routine status notes while keeping needed internal reminders available to the next LLM turn.");

            const float kPostMessageRowY = 150f;
            BuildSettingPair(
                footer.transform,
                "UserPostMessagePair",
                "Post user message:",
                140f,
                588f,
                new Vector2(kSettingsLeftPad, kPostMessageRowY),
                "Optional session-only text appended to each main AI Chat user message.\n" +
                "It is shown in the user bubble and stored in chat history.\n" +
                "It is not saved to disk or PlayerPrefs, and survives Clear until you edit it or close the app.",
                out _userPostMessageField);
            _userPostMessageField.contentType = TMP_InputField.ContentType.Standard;
            _userPostMessageField.characterLimit = 0;
            _userPostMessageField.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            if (_userPostMessageField.placeholder is TextMeshProUGUI upm)
            {
                upm.text = "(optional)";
                upm.color = new Color(0, 0, 0, 0.4f);
                upm.font = _font;
                upm.fontSize = BaseFontSize - 1;
                upm.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        // Parse the keep-N field, clamping to a sane value; persists it too.
        private int ReadCompactKeepN()
        {
            int n = AIChatPanel.GetCompactKeepN();
            if (_compactKeepNField != null && int.TryParse(_compactKeepNField.text, out int parsed))
                n = Mathf.Max(0, parsed);
            AIChatPanel.SetCompactKeepN(n);
            return n;
        }

        private void OnCompactTruncate()
        {
            int n = ReadCompactKeepN();
            AIChatPanel.CompactTruncate(n);
            SaveAndClose();
        }

        private void OnCompactSummarize()
        {
            int n = ReadCompactKeepN();
            AIChatPanel.CompactSummarize(n);
            SaveAndClose();
        }

        // Builds a "[Label:] [InputField]" pair anchored to the bottom-left of
        // its parent at <anchoredPos>. The pair has an invisible Image so it
        // can receive pointer events for the RTToolTip we attach.
        private void BuildSettingPair(
            Transform parent,
            string name,
            string labelText,
            float labelWidth,
            float inputWidth,
            Vector2 anchoredPos,
            string tooltip,
            out TMP_InputField inputOut)
        {
            const float ROW_HEIGHT = 28f;
            const float LABEL_INPUT_GAP = 4f;

            var pair = new GameObject(name);
            pair.transform.SetParent(parent, false);
            var pairRT = pair.AddComponent<RectTransform>();
            pairRT.anchorMin = new Vector2(0, 0);
            pairRT.anchorMax = new Vector2(0, 0);
            pairRT.pivot = new Vector2(0, 0);
            pairRT.sizeDelta = new Vector2(labelWidth + LABEL_INPUT_GAP + inputWidth, ROW_HEIGHT);
            pairRT.anchoredPosition = anchoredPos;

            // Transparent fill so the pair is a hover target for the tooltip.
            // Children (label = raycast off, input = own raycast) keep working.
            var bg = pair.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0);
            bg.raycastTarget = true;

            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = pair.AddComponent<RTToolTip>();
                tt._text = tooltip;
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(pair.transform, false);
            var labelRT = labelGo.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.sizeDelta = new Vector2(labelWidth, 0);
            labelRT.anchoredPosition = Vector2.zero;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = labelText;
            labelTmp.font = _font;
            labelTmp.fontSize = BaseFontSize;
            labelTmp.color = TextDark;
            labelTmp.alignment = TextAlignmentOptions.MidlineRight;
            labelTmp.raycastTarget = false;

            var inputGo = CreateInputFieldObject(name + "Input", multiline: false);
            inputGo.transform.SetParent(pair.transform, false);
            var inputRT = inputGo.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0, 0.5f);
            inputRT.anchorMax = new Vector2(0, 0.5f);
            inputRT.pivot = new Vector2(0, 0.5f);
            inputRT.sizeDelta = new Vector2(inputWidth, 26);
            inputRT.anchoredPosition = new Vector2(labelWidth + LABEL_INPUT_GAP, 0);
            var inputImg = inputGo.GetComponent<Image>();
            if (inputImg != null) { inputImg.sprite = null; inputImg.color = InputFieldBg; }
            inputOut = inputGo.GetComponent<TMP_InputField>();
            inputOut.textComponent.font = _font;
            inputOut.textComponent.fontSize = BaseFontSize;
            inputOut.textComponent.color = TextDark;
            ApplyFatCaret(inputOut);
            InstallCaretFixer(inputOut);
        }

        // TMP_DefaultControls.CreateInputField uses editor Undo parenting under the hood.
        // In Unity 6 that can trip TMP scrollbar rebuilds if this panel opens while a
        // graphic rebuild is already active. Build the same lightweight hierarchy
        // directly so settings can be opened from UI callbacks without that error.
        private GameObject CreateInputFieldObject(string name, bool multiline)
        {
            var inputGo = new GameObject(name);
            inputGo.AddComponent<RectTransform>();
            var bg = inputGo.AddComponent<Image>();
            bg.sprite = null;
            bg.color = InputFieldBg;

            var input = inputGo.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.richText = true;

            var viewport = new GameObject("Text Area");
            viewport.transform.SetParent(inputGo.transform, false);
            var viewportRt = viewport.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8, 5);
            viewportRt.offsetMax = new Vector2(-8, -5);
            viewport.AddComponent<RectMask2D>();

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(viewport.transform, false);
            var placeholderRt = placeholderGo.AddComponent<RectTransform>();
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = Vector2.zero;
            placeholderRt.offsetMax = Vector2.zero;
            var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderTmp.text = "";
            placeholderTmp.font = _font;
            placeholderTmp.fontSize = BaseFontSize;
            placeholderTmp.color = new Color(0, 0, 0, 0.4f);
            placeholderTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            placeholderTmp.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            placeholderTmp.raycastTarget = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(viewport.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.text = "";
            textTmp.font = _font;
            textTmp.fontSize = BaseFontSize;
            textTmp.color = TextDark;
            textTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            textTmp.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            textTmp.raycastTarget = false;

            input.textViewport = viewportRt;
            input.textComponent = textTmp;
            input.placeholder = placeholderTmp;
            input.text = "";
            TMPInputFieldUndo.ResetHistory(input);
            return inputGo;
        }

        // Unity's default TMP caret is 1px wide which is nearly invisible against
        // a white field background, especially with a slow blink. Force a thicker
        // high-contrast caret so users can actually see where they're typing.
        private static void ApplyFatCaret(TMP_InputField input)
        {
            if (input == null) return;
            input.customCaretColor = true;
            input.caretColor = new Color(0f, 0f, 0f, 1f);
            input.caretWidth = 5;
            input.caretBlinkRate = 0.6f;
            input.selectionColor = new Color(0.25f, 0.5f, 1f, 0.45f);
        }

        // Setting caret properties once isn't enough on Unity 6 / TMP 3: the spawned
        // TMP_SelectionCaret graphic isn't created/tinted on first selection, so the
        // caret renders invisible. AIChatCaretFixer (defined in AIChatPanel.cs) does
        // the bullet-proof toggle-enabled + ForceLabelUpdate + caret-child tint dance
        // on every OnEnable/OnSelect, plus reasserts the caret config periodically
        // while focused. We use the same component the chat input field relies on.
        private static void InstallCaretFixer(TMP_InputField input)
        {
            if (input == null) return;
            var fixer = input.gameObject.GetComponent<AIChatCaretFixer>();
            if (fixer == null)
                fixer = input.gameObject.AddComponent<AIChatCaretFixer>();
            fixer.Set(input);
        }

        // ---------- Behavior ----------

        private void LoadFromManager()
        {
            if (_staticSkillManager == null) return;
            // Capture which file we're editing (main_prompt.txt vs test_main_prompt.txt)
            // and its text now, so SaveAndClose writes back to the same file and can skip
            // a no-op write.
            _activeMainPromptPath = _staticSkillManager.ActiveMainPromptPath;
            _loadedMainPromptText = _staticSkillManager.MainPrompt ?? "";
            if (_mainPromptField != null)
                _mainPromptField.text = _loadedMainPromptText;
            if (_mainPromptLabelTmp != null)
            {
                string file = string.IsNullOrEmpty(_activeMainPromptPath)
                    ? "main_prompt.txt"
                    : Path.GetFileName(_activeMainPromptPath);
                _mainPromptLabelTmp.text = "Main system prompt (aichat/" + file + ") - edited live; saved when you close this panel:";
            }
            if (_keepLastNField != null)
                _keepLastNField.text = AIChatPanel.GetKeepLastNMedia().ToString();
            if (_presetPrefixField != null)
                _presetPrefixField.text = AIChatPanel.GetPresetPrefix();
            if (_maxEdgeField != null)
                _maxEdgeField.text = AIChatPanel.GetAttachmentMaxEdge().ToString();
            if (_compactKeepNField != null)
                _compactKeepNField.text = AIChatPanel.GetCompactKeepN().ToString();
            if (_imageContextLimitField != null)
                _imageContextLimitField.text = AIChatPanel.GetImageContextLimit().ToString();
            if (_userPostMessageField != null)
                _userPostMessageField.text = AIChatPanel.GetUserPostMessage();
            if (_keepOldToolCallsToggle != null)
                _keepOldToolCallsToggle.isOn = AIChatPanel.GetKeepOldToolCallsInPrompt();
            if (_autoCaptionGeneratedImagesToggle != null)
                _autoCaptionGeneratedImagesToggle.isOn = AIChatPanel.GetAutoCaptionGeneratedImages();
            if (_showDebugStuffToggle != null)
                _showDebugStuffToggle.isOn = AIChatPanel.GetShowDebugStuff();
            if (_mainPanel != null)
                TMPInputFieldUndo.ResetHistoryInChildren(_mainPanel);
            RebuildSkillRows();
        }

        private void RebuildSkillRows()
        {
            if (_skillsContent == null) return;
            for (int i = _skillsContent.childCount - 1; i >= 0; i--)
                Destroy(_skillsContent.GetChild(i).gameObject);

            if (_staticSkillManager == null) return;
            foreach (var s in _staticSkillManager.GetSkills())
            {
                var row = new GameObject("Skill_" + s.Id);
                row.transform.SetParent(_skillsContent, false);
                var le = row.AddComponent<LayoutElement>();
                // Floor only - row grows vertically when the description wraps
                // to multiple lines. Without this the bottom of long summaries
                // got clipped or overflowed past the right edge of the panel.
                le.minHeight = 46f;
                row.AddComponent<Image>().color = RowBg;

                var v = row.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(8, 8, 4, 4);
                v.spacing = 1;
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
                row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var idTmp = MakeLabel(s.Id + "  -  " + (s.Inputs == SkillInputs.None ? "no inputs" : "needs " + s.Inputs.ToString().ToLowerInvariant()));
                idTmp.transform.SetParent(row.transform, false);
                var idRt = idTmp.GetComponent<TextMeshProUGUI>();
                idRt.fontStyle = FontStyles.Bold;
                idRt.fontSize = BaseFontSize;

                // Description: built inline (not via MakeLabel) so it can wrap
                // and let TMP report its own multi-line preferred height instead
                // of the fixed 16px MakeLabel forces via LayoutElement.
                var sumGo = new GameObject("Desc");
                sumGo.transform.SetParent(row.transform, false);
                sumGo.AddComponent<RectTransform>();
                var sumTmp = sumGo.AddComponent<TextMeshProUGUI>();
                sumTmp.text = s.Summary ?? "";
                sumTmp.font = _font;
                sumTmp.fontSize = BaseFontSize - 1;
                sumTmp.color = TextDark;
                sumTmp.alignment = TextAlignmentOptions.TopLeft;
                sumTmp.textWrappingMode = TextWrappingModes.Normal;
                sumTmp.raycastTarget = false;
            }
        }

        private void SaveAndClose()
        {
            try
            {
                if (_staticSkillManager != null && _mainPromptField != null)
                {
                    // Write back to the SAME file the editor loaded from. In "test_"
                    // preset mode that's test_main_prompt.txt, so an experimental prompt
                    // is never saved over the production main_prompt.txt. Only write when
                    // the text actually changed - this also avoids forking a
                    // test_main_prompt.txt into existence just by opening + closing.
                    string path = string.IsNullOrEmpty(_activeMainPromptPath)
                        ? _staticSkillManager.ActiveMainPromptPath
                        : _activeMainPromptPath;
                    string newText = _mainPromptField.text ?? "";
                    if (!string.IsNullOrEmpty(path) && newText != _loadedMainPromptText)
                    {
                        string dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllText(path, newText);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("AIChatSettingsPanel: failed to save the main system prompt: " + ex.Message);
            }

            // Persist the keep-last-N setting to PlayerPrefs.
            if (_keepLastNField != null && int.TryParse(_keepLastNField.text, out int keepN))
                AIChatPanel.SetKeepLastNMedia(keepN);

            // Persist the global preset prefix. Empty string is fine and means "use
            // bare names" (no prefix substitution beyond stripping the {{...}} markers).
            if (_presetPrefixField != null)
                AIChatPanel.SetPresetPrefix(_presetPrefixField.text ?? "");

            // Persist the attachment max-edge cap. Empty / unparseable -> leave the
            // existing value untouched. SetAttachmentMaxEdge clamps to a sane range.
            if (_maxEdgeField != null && int.TryParse(_maxEdgeField.text, out int maxEdge))
                AIChatPanel.SetAttachmentMaxEdge(maxEdge);

            // Persist the compact "keep last N exchanges" value (used by both the
            // Truncate and Summarize buttons).
            if (_compactKeepNField != null && int.TryParse(_compactKeepNField.text, out int compactN))
                AIChatPanel.SetCompactKeepN(compactN);

            if (_imageContextLimitField != null && int.TryParse(_imageContextLimitField.text, out int imageContextLimit))
                AIChatPanel.SetImageContextLimit(imageContextLimit);

            if (_userPostMessageField != null)
                AIChatPanel.SetUserPostMessage(_userPostMessageField.text ?? "");

            if (_keepOldToolCallsToggle != null)
                AIChatPanel.SetKeepOldToolCallsInPrompt(_keepOldToolCallsToggle.isOn);
            if (_autoCaptionGeneratedImagesToggle != null)
                AIChatPanel.SetAutoCaptionGeneratedImages(_autoCaptionGeneratedImagesToggle.isOn);
            if (_showDebugStuffToggle != null)
                AIChatPanel.SetShowDebugStuff(_showDebugStuffToggle.isOn);

            // Trigger the host's reload + UI update.
            _staticSkillManager?.Reload();
            try { _staticOnClose?.Invoke(); } catch (Exception ex) { Debug.LogError("AIChatSettingsPanel: onClose threw: " + ex); }

            Hide();
        }

        private void OpenAIChatFolder()
        {
            if (_staticSkillManager == null) return;
            string folder = Path.GetDirectoryName(_staticSkillManager.MainPromptPath);
            if (string.IsNullOrEmpty(folder)) return;

            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
#else
                Application.OpenURL("file://" + folder);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError("AIChatSettingsPanel: failed to open aichat folder: " + ex.Message);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SaveAndClose();
        }

        // ---------- Tiny UI helpers ----------

        private GameObject MakeLabel(string text)
        {
            var go = new GameObject("Label");
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 16f;
            le.preferredHeight = 16f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = _font;
            tmp.fontSize = BaseFontSize;
            tmp.color = TextDark;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            return go;
        }

        private Button MakeButton(Transform parent, string text, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btn = new GameObject("Btn_" + text);
            btn.transform.SetParent(parent, false);
            var rt = btn.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = btn.AddComponent<Image>();
            img.color = Color.white;
            var b = btn.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);

            var tx = new GameObject("Text");
            tx.transform.SetParent(btn.transform, false);
            var txRt = tx.AddComponent<RectTransform>();
            txRt.anchorMin = Vector2.zero;
            txRt.anchorMax = Vector2.one;
            txRt.offsetMin = Vector2.zero;
            txRt.offsetMax = Vector2.zero;
            var txTmp = tx.AddComponent<TextMeshProUGUI>();
            txTmp.text = text;
            txTmp.font = _font;
            txTmp.fontSize = BaseFontSize;
            txTmp.fontStyle = FontStyles.Bold;
            txTmp.color = TextDark;
            txTmp.alignment = TextAlignmentOptions.Center;
            txTmp.raycastTarget = false;
            return b;
        }

        private Toggle MakeSettingToggle(Transform parent, string label, Vector2 anchoredPos, Vector2 size, bool initialOn, string tooltip)
        {
            var row = new GameObject("Toggle_" + label);
            row.transform.SetParent(parent, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 0);
            rowRt.anchorMax = new Vector2(0, 0);
            rowRt.pivot = new Vector2(0, 0);
            rowRt.anchoredPosition = anchoredPos;
            rowRt.sizeDelta = size;
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0);
            bg.raycastTarget = true;
            if (!string.IsNullOrEmpty(tooltip))
            {
                var tt = row.AddComponent<RTToolTip>();
                tt._text = tooltip;
            }

            var toggle = row.AddComponent<Toggle>();

            var box = new GameObject("Box");
            box.transform.SetParent(row.transform, false);
            var boxRt = box.AddComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0, 0.5f);
            boxRt.anchorMax = new Vector2(0, 0.5f);
            boxRt.pivot = new Vector2(0, 0.5f);
            boxRt.anchoredPosition = new Vector2(0, 0);
            boxRt.sizeDelta = new Vector2(18, 18);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = Color.white;

            var mark = new GameObject("Checkmark");
            mark.transform.SetParent(box.transform, false);
            var markRt = mark.AddComponent<RectTransform>();
            markRt.anchorMin = Vector2.zero;
            markRt.anchorMax = Vector2.one;
            markRt.offsetMin = new Vector2(3, 3);
            markRt.offsetMax = new Vector2(-3, -3);
            var markImg = mark.AddComponent<Image>();
            markImg.color = new Color(0.12f, 0.45f, 0.16f, 1f);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(row.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0, 0);
            textRt.anchorMax = new Vector2(1, 1);
            textRt.offsetMin = new Vector2(24, 0);
            textRt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.font = _font;
            tmp.fontSize = BaseFontSize;
            tmp.color = TextDark;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;

            toggle.targetGraphic = boxImg;
            toggle.graphic = markImg;
            toggle.isOn = initialOn;
            return toggle;
        }
    }
}
