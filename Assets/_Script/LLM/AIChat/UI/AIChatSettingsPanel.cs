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
        private RectTransform _skillsContent;
        private TMP_InputField _keepLastNField;
        private TMP_InputField _presetPrefixField;

        private const float DEFAULT_WIDTH = 760f;
        private const float DEFAULT_HEIGHT = 620f;
        private const float HEADER_HEIGHT = 40f;
        private const float FOOTER_HEIGHT = 85f;
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

            var close = new GameObject("Close");
            close.transform.SetParent(header.transform, false);
            var closeRt = close.AddComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1, 0.5f);
            closeRt.anchorMax = new Vector2(1, 0.5f);
            closeRt.pivot = new Vector2(1, 0.5f);
            closeRt.sizeDelta = new Vector2(24, 24);
            closeRt.anchoredPosition = new Vector2(-6, 0);
            close.AddComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            var closeBtn = close.AddComponent<Button>();
            closeBtn.onClick.AddListener(SaveAndClose);

            var x = new GameObject("X");
            x.transform.SetParent(close.transform, false);
            var xRt = x.AddComponent<RectTransform>();
            xRt.anchorMin = Vector2.zero;
            xRt.anchorMax = Vector2.one;
            xRt.offsetMin = Vector2.zero;
            xRt.offsetMax = Vector2.zero;
            var xTmp = x.AddComponent<TextMeshProUGUI>();
            xTmp.text = "X";
            xTmp.font = _font;
            xTmp.fontSize = 14;
            xTmp.color = Color.white;
            xTmp.alignment = TextAlignmentOptions.Center;
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

            // Top half: main_prompt.txt editor.
            var promptLabel = MakeLabel("Main system prompt (aichat/main_prompt.txt) - edited live; saved when you close this panel:");
            promptLabel.transform.SetParent(body.transform, false);
            var labRt = promptLabel.GetComponent<RectTransform>();
            labRt.anchorMin = new Vector2(0, 1);
            labRt.anchorMax = new Vector2(1, 1);
            labRt.pivot = new Vector2(0.5f, 1);
            labRt.sizeDelta = new Vector2(0, 18);
            labRt.anchoredPosition = new Vector2(0, -2);

            var promptInputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            promptInputGo.name = "MainPromptInput";
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
            if (_mainPromptField.placeholder is TextMeshProUGUI pp)
            {
                pp.text = "Type the main system prompt here...";
                pp.color = new Color(0, 0, 0, 0.4f);
                pp.font = _font;
                pp.fontSize = BaseFontSize;
            }

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

            var skSb = new GameObject("Scrollbar");
            skSb.transform.SetParent(skScrollGo.transform, false);
            var skSbRt = skSb.AddComponent<RectTransform>();
            skSbRt.anchorMin = new Vector2(1, 0);
            skSbRt.anchorMax = new Vector2(1, 1);
            skSbRt.pivot = new Vector2(1, 0.5f);
            skSbRt.sizeDelta = new Vector2(18, 0);
            skSbRt.anchoredPosition = Vector2.zero;
            skSb.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.24f, 1f);
            var sb = skSb.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var sbHandle = new GameObject("Handle");
            sbHandle.transform.SetParent(skSb.transform, false);
            var sbHandleRt = sbHandle.AddComponent<RectTransform>();
            sbHandleRt.anchorMin = Vector2.zero;
            sbHandleRt.anchorMax = Vector2.one;
            sbHandleRt.offsetMin = new Vector2(3, 3);
            sbHandleRt.offsetMax = new Vector2(-3, -3);
            var sbHandleImg = sbHandle.AddComponent<Image>();
            sbHandleImg.color = new Color(0.45f, 0.45f, 0.5f, 1f);
            sb.handleRect = sbHandleRt;
            sb.targetGraphic = sbHandleImg;
            skScroll.verticalScrollbar = sb;
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

            // Centered: "Keep last N media on Clear: [10]" - persisted to PlayerPrefs
            // via AIChatPanel.GetKeepLastNMedia/SetKeepLastNMedia. Saved on close.
            var keepRowGo = new GameObject("KeepLastNRow");
            keepRowGo.transform.SetParent(footer.transform, false);
            var keepRowRT = keepRowGo.AddComponent<RectTransform>();
            keepRowRT.anchorMin = new Vector2(0.5f, 0);
            keepRowRT.anchorMax = new Vector2(0.5f, 0);
            keepRowRT.pivot = new Vector2(0.5f, 0);
            keepRowRT.sizeDelta = new Vector2(280, 30);
            keepRowRT.anchoredPosition = new Vector2(0, 10);

            var keepLabelGo = new GameObject("Label");
            keepLabelGo.transform.SetParent(keepRowGo.transform, false);
            var keepLabelRT = keepLabelGo.AddComponent<RectTransform>();
            keepLabelRT.anchorMin = new Vector2(0, 0);
            keepLabelRT.anchorMax = new Vector2(1, 1);
            keepLabelRT.offsetMin = Vector2.zero;
            keepLabelRT.offsetMax = new Vector2(-60, 0);
            var keepLabelTmp = keepLabelGo.AddComponent<TextMeshProUGUI>();
            keepLabelTmp.text = "Keep last N media on Clear:";
            keepLabelTmp.font = _font;
            keepLabelTmp.fontSize = BaseFontSize;
            keepLabelTmp.color = TextDark;
            keepLabelTmp.alignment = TextAlignmentOptions.MidlineRight;
            keepLabelTmp.raycastTarget = false;

            var keepInputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            keepInputGo.name = "KeepLastNInput";
            keepInputGo.transform.SetParent(keepRowGo.transform, false);
            var keepInputRT = keepInputGo.GetComponent<RectTransform>();
            keepInputRT.anchorMin = new Vector2(1, 0.5f);
            keepInputRT.anchorMax = new Vector2(1, 0.5f);
            keepInputRT.pivot = new Vector2(1, 0.5f);
            keepInputRT.sizeDelta = new Vector2(54, 26);
            keepInputRT.anchoredPosition = new Vector2(0, 0);
            var keepInputImg = keepInputGo.GetComponent<Image>();
            if (keepInputImg != null) { keepInputImg.sprite = null; keepInputImg.color = InputFieldBg; }
            _keepLastNField = keepInputGo.GetComponent<TMP_InputField>();
            _keepLastNField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _keepLastNField.characterLimit = 4;
            _keepLastNField.textComponent.font = _font;
            _keepLastNField.textComponent.fontSize = BaseFontSize;
            _keepLastNField.textComponent.color = TextDark;
            _keepLastNField.textComponent.alignment = TextAlignmentOptions.Center;
            if (_keepLastNField.placeholder is TextMeshProUGUI kp)
            {
                kp.text = "10";
                kp.color = new Color(0, 0, 0, 0.4f);
                kp.font = _font;
                kp.fontSize = BaseFontSize;
                kp.alignment = TextAlignmentOptions.Center;
            }

            // Centered above the keep-N row: "Preset prefix: [_______]" - prepended
            // to every {{...}}-wrapped preset name in skill md / main_prompt at
            // prompt-build time. Empty = bare names (default). Persisted to
            // PlayerPrefs via AIChatPanel.GetPresetPrefix/SetPresetPrefix.
            var prefixRowGo = new GameObject("PresetPrefixRow");
            prefixRowGo.transform.SetParent(footer.transform, false);
            var prefixRowRT = prefixRowGo.AddComponent<RectTransform>();
            prefixRowRT.anchorMin = new Vector2(0.5f, 0);
            prefixRowRT.anchorMax = new Vector2(0.5f, 0);
            prefixRowRT.pivot = new Vector2(0.5f, 0);
            prefixRowRT.sizeDelta = new Vector2(360, 30);
            prefixRowRT.anchoredPosition = new Vector2(0, 45);

            var prefixLabelGo = new GameObject("Label");
            prefixLabelGo.transform.SetParent(prefixRowGo.transform, false);
            var prefixLabelRT = prefixLabelGo.AddComponent<RectTransform>();
            prefixLabelRT.anchorMin = new Vector2(0, 0);
            prefixLabelRT.anchorMax = new Vector2(1, 1);
            prefixLabelRT.offsetMin = Vector2.zero;
            prefixLabelRT.offsetMax = new Vector2(-180, 0);
            var prefixLabelTmp = prefixLabelGo.AddComponent<TextMeshProUGUI>();
            prefixLabelTmp.text = "Preset prefix:";
            prefixLabelTmp.font = _font;
            prefixLabelTmp.fontSize = BaseFontSize;
            prefixLabelTmp.color = TextDark;
            prefixLabelTmp.alignment = TextAlignmentOptions.MidlineRight;
            prefixLabelTmp.raycastTarget = false;

            var prefixInputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            prefixInputGo.name = "PresetPrefixInput";
            prefixInputGo.transform.SetParent(prefixRowGo.transform, false);
            var prefixInputRT = prefixInputGo.GetComponent<RectTransform>();
            prefixInputRT.anchorMin = new Vector2(1, 0.5f);
            prefixInputRT.anchorMax = new Vector2(1, 0.5f);
            prefixInputRT.pivot = new Vector2(1, 0.5f);
            prefixInputRT.sizeDelta = new Vector2(170, 26);
            prefixInputRT.anchoredPosition = new Vector2(0, 0);
            var prefixInputImg = prefixInputGo.GetComponent<Image>();
            if (prefixInputImg != null) { prefixInputImg.sprite = null; prefixInputImg.color = InputFieldBg; }
            _presetPrefixField = prefixInputGo.GetComponent<TMP_InputField>();
            _presetPrefixField.contentType = TMP_InputField.ContentType.Standard;
            _presetPrefixField.characterLimit = 32;
            _presetPrefixField.textComponent.font = _font;
            _presetPrefixField.textComponent.fontSize = BaseFontSize;
            _presetPrefixField.textComponent.color = TextDark;
            _presetPrefixField.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            if (_presetPrefixField.placeholder is TextMeshProUGUI pp2)
            {
                pp2.text = "(empty = use bare names; e.g. test_)";
                pp2.color = new Color(0, 0, 0, 0.4f);
                pp2.font = _font;
                pp2.fontSize = BaseFontSize - 1;
                pp2.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        // ---------- Behavior ----------

        private void LoadFromManager()
        {
            if (_staticSkillManager == null) return;
            if (_mainPromptField != null)
                _mainPromptField.text = _staticSkillManager.MainPrompt ?? "";
            if (_keepLastNField != null)
                _keepLastNField.text = AIChatPanel.GetKeepLastNMedia().ToString();
            if (_presetPrefixField != null)
                _presetPrefixField.text = AIChatPanel.GetPresetPrefix();
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
                le.minHeight = 46f;
                le.preferredHeight = 46f;
                row.AddComponent<Image>().color = RowBg;

                var v = row.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(8, 8, 4, 4);
                v.spacing = 1;
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;

                var idTmp = MakeLabel(s.Id + "  -  " + (s.Inputs == SkillInputs.None ? "no inputs" : "needs " + s.Inputs.ToString().ToLowerInvariant()));
                idTmp.transform.SetParent(row.transform, false);
                var idRt = idTmp.GetComponent<TextMeshProUGUI>();
                idRt.fontStyle = FontStyles.Bold;
                idRt.fontSize = BaseFontSize;

                var sumTmp = MakeLabel(s.Summary);
                sumTmp.transform.SetParent(row.transform, false);
                sumTmp.GetComponent<TextMeshProUGUI>().fontSize = BaseFontSize - 1;
            }
        }

        private void SaveAndClose()
        {
            try
            {
                if (_staticSkillManager != null && _mainPromptField != null)
                {
                    string path = _staticSkillManager.MainPromptPath;
                    string newText = _mainPromptField.text ?? "";
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(path, newText);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("AIChatSettingsPanel: failed to save main_prompt.txt: " + ex.Message);
            }

            // Persist the keep-last-N setting to PlayerPrefs.
            if (_keepLastNField != null && int.TryParse(_keepLastNField.text, out int keepN))
                AIChatPanel.SetKeepLastNMedia(keepN);

            // Persist the global preset prefix. Empty string is fine and means "use
            // bare names" (no prefix substitution beyond stripping the {{...}} markers).
            if (_presetPrefixField != null)
                AIChatPanel.SetPresetPrefix(_presetPrefixField.text ?? "");

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
    }
}
