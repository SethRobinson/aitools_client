using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum AppSettingsTab
{
    General,
    Configuration,
    Audio,
    LLM
}

/// <summary>
/// Unified app settings window. Replaces the separate General Settings, raw config editor,
/// and direct LLM Settings entry points with one tabbed settings surface.
/// </summary>
public class AppSettingsPanel : MonoBehaviour
{
    private static AppSettingsPanel _instance;
    private static GameObject _panelRoot;

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;
    private RectTransform _contentHost;
    private TextMeshProUGUI _footerStatusText;
    private TextMeshProUGUI _generalStatusText;
    private TextMeshProUGUI _configStatusText;
    private TextMeshProUGUI _audioStatusText;
    private GameObject _forceReconnectDialogRoot;
    private TMP_InputField _imageEditorInput;
    private TMP_Dropdown _ttsProviderDropdown;
    private TMP_Dropdown _elevenLabsVoiceDropdown;
    private TMP_InputField _elevenLabsApiKeyInput;
    private TMP_InputField _elevenLabsVoiceIdInput;
    private TMP_InputField _ttsTestTextInput;
    private Toggle _stripThinkTagsToggle;

    private readonly Dictionary<AppSettingsTab, Button> _tabButtons = new Dictionary<AppSettingsTab, Button>();
    private readonly List<ComfyServerConfig> _workingServers = new List<ComfyServerConfig>();
    private bool _configDirty = false;
    private bool _stripThinkTags = true;
    private AppSettingsTab _activeTab = AppSettingsTab.General;
    private string _ttsTestText = "This is a test of the Text To Speech settings.";

    private const float PanelWidth = 900f;
    private const float PanelHeight = 790f;
    private const float HeaderHeight = 40f;
    private const float TabHeight = 42f;
    private const float FooterHeight = 48f;
    private const float BaseFontSize = 14f;

    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color RowBg = new Color(0.84f, 0.84f, 0.86f, 1f);
    private static readonly Color ActiveTabBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color InactiveTabBg = new Color(0.70f, 0.70f, 0.72f, 1f);
    private static readonly Color ButtonBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color InputBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextMuted = new Color(0.18f, 0.18f, 0.18f, 0.82f);
    private static readonly Color CheckColor = new Color(0.18f, 0.45f, 0.18f, 1f);

    public static void Show(AppSettingsTab tab = AppSettingsTab.General)
    {
        // The LLM Settings "tab" is a launcher for the standalone advanced dialog, not a
        // content page. Open the window on General and pop the advanced LLM dialog on top.
        if (tab == AppSettingsTab.LLM)
        {
            ShowInternal(AppSettingsTab.General);
            LLMSettingsPanel.Show();
            return;
        }

        ShowInternal(tab);
    }

    private static void ShowInternal(AppSettingsTab tab)
    {
        if (_instance != null)
        {
            _panelRoot.SetActive(true);
            _instance.SetTab(tab);
            return;
        }

        _panelRoot = new GameObject("AppSettingsPanel");
        _instance = _panelRoot.AddComponent<AppSettingsPanel>();
        _instance.CreateUI(tab);
    }

    public static void Hide()
    {
        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    public static void Toggle(AppSettingsTab tab = AppSettingsTab.General)
    {
        if (_panelRoot != null && _panelRoot.activeSelf && _instance != null && _instance._activeTab == tab)
            Hide();
        else
            Show(tab);
    }

    public static void EnsureCreated()
    {
        if (_instance != null) return;
        Show(AppSettingsTab.General);
        Hide();
    }

    public static bool GetStripThinkTags()
    {
        return _instance == null ? true : _instance._stripThinkTags;
    }

    private void OnDestroy()
    {
        HideForceReconnectDialog();
        _instance = null;
        _panelRoot = null;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private void CreateUI(AppSettingsTab initialTab)
    {
        _font = FindFont();

        var canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

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
        _mainPanel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var panelImg = main.AddComponent<Image>();
        panelImg.color = PanelBg;

        CreateHeader();
        CreateTabStrip();
        CreateContentHost();
        CreateFooter();
        SetTab(initialTab);
    }

    private void CreateHeader()
    {
        var header = CreateRect("Header", _mainPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        header.sizeDelta = new Vector2(0, HeaderHeight);
        header.anchoredPosition = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = HeaderBg;

        var drag = header.gameObject.AddComponent<PanelDragHandler>();
        drag.SetTarget(_mainPanel, HeaderHeight);

        var title = CreateText("Title", header, "Settings", 18f, TextDark, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = new Vector2(12, 0);
        title.rectTransform.offsetMax = new Vector2(-42, 0);

        RTWindowChrome.CreateCloseButton(header, Hide);
    }

    private void CreateTabStrip()
    {
        var strip = CreateRect("Tabs", _mainPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        strip.sizeDelta = new Vector2(0, TabHeight);
        strip.anchoredPosition = new Vector2(0, -HeaderHeight);
        strip.gameObject.AddComponent<Image>().color = HeaderBg;

        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 4);
        layout.spacing = 8f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        CreateTabButton(strip, AppSettingsTab.General, "General Settings", 185f);
        CreateTabButton(strip, AppSettingsTab.Configuration, "ComfyUI Settings", 190f);
        CreateTabButton(strip, AppSettingsTab.Audio, "Audio", 110f);
        // LLM is a launcher, not a content page: it opens the standalone advanced dialog
        // directly. It is intentionally NOT added to _tabButtons (never highlighted active),
        // and it keeps the inactive-tab color so it never looks like the selected page.
        var llmLauncher = CreateButton(strip, "Tab_LLM", "LLM Settings", 165f, () => LLMSettingsPanel.Show());
        var llmLauncherImg = llmLauncher.GetComponent<Image>();
        if (llmLauncherImg != null)
            llmLauncherImg.color = InactiveTabBg;
    }

    private void CreateTabButton(RectTransform parent, AppSettingsTab tab, string text, float width)
    {
        var btn = CreateButton(parent, "Tab_" + tab, text, width, () => SetTab(tab));
        _tabButtons[tab] = btn;
    }

    private void CreateContentHost()
    {
        _contentHost = CreateRect("ContentHost", _mainPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        _contentHost.offsetMin = new Vector2(0, FooterHeight);
        _contentHost.offsetMax = new Vector2(0, -(HeaderHeight + TabHeight));
    }

    private void CreateFooter()
    {
        var footer = CreateRect("Footer", _mainPanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
        footer.sizeDelta = new Vector2(0, FooterHeight);
        footer.anchoredPosition = Vector2.zero;
        footer.gameObject.AddComponent<Image>().color = FooterBg;

        _footerStatusText = CreateText("Status", footer, "", 12.5f, TextMuted, TextAlignmentOptions.MidlineLeft);
        _footerStatusText.rectTransform.anchorMin = new Vector2(0, 0);
        _footerStatusText.rectTransform.anchorMax = new Vector2(1, 1);
        _footerStatusText.rectTransform.offsetMin = new Vector2(12, 0);
        _footerStatusText.rectTransform.offsetMax = new Vector2(-250, 0);

        var applyReconnect = CreateButton(footer, "OkApplyReconnect", "Ok (Apply and reconnect)", 220f, ApplyServerConfigurationAndClose);
        var applyReconnectRt = applyReconnect.GetComponent<RectTransform>();
        applyReconnectRt.anchorMin = new Vector2(1, 0.5f);
        applyReconnectRt.anchorMax = new Vector2(1, 0.5f);
        applyReconnectRt.pivot = new Vector2(1, 0.5f);
        applyReconnectRt.anchoredPosition = new Vector2(-16f, 0f);
    }

    private void SetTab(AppSettingsTab tab)
    {
        _activeTab = tab;

        foreach (var kvp in _tabButtons)
        {
            var image = kvp.Value.GetComponent<Image>();
            if (image != null)
                image.color = kvp.Key == tab ? ActiveTabBg : InactiveTabBg;
        }

        ClearContent();
        var content = CreateScrollContent();

        if (tab == AppSettingsTab.Configuration)
            BuildConfigurationTab(content);
        else if (tab == AppSettingsTab.Audio)
            BuildAudioTab(content);
        else
            BuildGeneralTab(content);

        SetFooterStatus("");
    }

    private void ClearContent()
    {
        for (int i = _contentHost.childCount - 1; i >= 0; i--)
        {
            Destroy(_contentHost.GetChild(i).gameObject);
        }
    }

    private RectTransform CreateScrollContent()
    {
        var scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(_contentHost, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(8, 8);
        viewportRt.offsetMax = new Vector2(-22, -8);
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.offsetMin = new Vector2(6, 0);
        contentRt.offsetMax = new Vector2(-6, 0);

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRt;
        scrollRect.content = contentRt;

        var scrollbarGo = new GameObject("Scrollbar");
        scrollbarGo.transform.SetParent(scrollGo.transform, false);
        var scrollbarRt = scrollbarGo.AddComponent<RectTransform>();
        scrollbarRt.anchorMin = new Vector2(1, 0);
        scrollbarRt.anchorMax = new Vector2(1, 1);
        scrollbarRt.pivot = new Vector2(1, 0.5f);
        scrollbarRt.sizeDelta = new Vector2(14, 0);
        scrollbarRt.offsetMin = new Vector2(-16, 8);
        scrollbarRt.offsetMax = new Vector2(-2, -8);
        var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbarGo.AddComponent<Image>().color = new Color(0.62f, 0.62f, 0.66f, 1f);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(scrollbarGo.transform, false);
        var handleRt = handle.AddComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = new Vector2(2, 2);
        handleRt.offsetMax = new Vector2(-2, -2);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(0.42f, 0.42f, 0.47f, 1f);
        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handleImg;
        scrollRect.verticalScrollbar = scrollbar;

        return contentRt;
    }

    private void BuildGeneralTab(RectTransform content)
    {
        var gl = GameLogic.Get();
        var prefs = UserPreferences.Get();

        CreateSectionHeader(content, "Generation");

        var maxRow = CreateRow(content, "MaxPicsRow", 34f);
        CreateLabel(maxRow, "Stop after generating/inpainting", 220f);
        var maxInput = CreateInput(maxRow, gl != null ? gl.GetMaxToGenerate().ToString() : "0", 76f);
        maxInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        maxInput.textComponent.alignment = TextAlignmentOptions.Center;
        maxInput.onEndEdit.AddListener(value =>
        {
            int.TryParse(value, out int result);
            GameLogic.Get()?.SetMaxToGenerate(Mathf.Max(0, result));
            maxInput.SetTextWithoutNotify(Mathf.Max(0, result).ToString());
        });
        CreateLabel(maxRow, "pics. (0 for unlimited)", 220f);

        CreateToggleRow(content, "Randomize prompts", gl != null && gl.GetRandomizePrompt(),
            value => GameLogic.Get()?.SetRandomizePrompt(value));
        CreateToggleRow(content, "Camera follow mode", gl != null && gl.GetCameraFollow(),
            value => GameLogic.Get()?.SetCameraFollow(value));
        CreateToggleRow(content, "Auto save generated pics as .bmp", gl != null && gl.GetAutoSave(),
            value => GameLogic.Get()?.SetAutoSave(value));
        CreateToggleRow(content, "Auto save generated pics as .png", gl != null && gl.GetAutoSavePNG(),
            value => GameLogic.Get()?.SetAutoSavePNG(value));

        CreateSectionHeader(content, "External Editor");
        var editorRow = CreateRow(content, "ImageEditorRow", 36f);
        CreateLabel(editorRow, "Photo editor .exe", 150f);
        _imageEditorInput = CreateInput(editorRow, Config.Get() != null ? Config.Get().GetImageEditorPathAndExe() : "", 650f);
        _imageEditorInput.onEndEdit.AddListener(value => SaveImageEditorPath(value));

        var editorButtonsRow = CreateRow(content, "ImageEditorButtonsRow", 34f);
        CreateLabel(editorButtonsRow, "", 150f);
        CreateButton(editorButtonsRow, "BrowseEditor", "Browse...", 92f, OnBrowseImageEditor);
        CreateButton(editorButtonsRow, "ClearEditor", "Clear", 70f, () =>
        {
            if (_imageEditorInput != null)
                _imageEditorInput.SetTextWithoutNotify("");
            SaveImageEditorPath("");
        });

        CreateSectionHeader(content, "Adventure Mode");
        var autoPicRow = CreateRow(content, "AutoPicRow", 36f);
        CreateLabel(autoPicRow, "Default AutoPic script", 180f);
        string savedAutoPic = prefs != null && !string.IsNullOrEmpty(prefs.DefaultAutoPicScript)
            ? prefs.DefaultAutoPicScript
            : "AutoPic.txt";
        var autoPicLabel = CreateTextButton(autoPicRow, "AutoPicButton", savedAutoPic, 300f, () =>
        {
            PresetPickerDialog.Show(new PresetPickerDialog.Options
            {
                Title = "Select Default AutoPic Script",
                CurrentSelection = GenerateSettingsPanel.GetDefaultAutoPicScript(),
                FileFilterPrefix = "AutoPic"
            }, fileName =>
            {
                if (string.IsNullOrEmpty(fileName)) return;
                var p = UserPreferences.Get();
                if (p != null)
                {
                    p.DefaultAutoPicScript = fileName;
                    p.Save();
                }
                SetTab(AppSettingsTab.General);
            });
        });
        autoPicLabel.textWrappingMode = TextWrappingModes.NoWrap;

        var quoteRow = CreateRow(content, "AdventureQuoteColorRow", 36f);
        CreateLabel(quoteRow, "Adventure quote color", 180f);
        var quoteInput = CreateInput(quoteRow, prefs != null ? prefs.AdventureQuoteColor ?? "" : "", 160f);
        quoteInput.onEndEdit.AddListener(value =>
        {
            var p = UserPreferences.Get();
            if (p == null) return;
            p.AdventureQuoteColor = value.Trim();
            p.Save();
        });

        CreateSectionHeader(content, "LLM Behavior");
        _stripThinkTagsToggle = CreateToggleRow(content, "Strip <think> tags when sending to LLMs", _stripThinkTags,
            value => _stripThinkTags = value);
        CreateToggleRow(content, "Write debug .json files", prefs == null || prefs.WriteDebugJsonFiles,
            value =>
            {
                var p = UserPreferences.Get();
                if (p == null) return;
                p.WriteDebugJsonFiles = value;
                p.Save();
            });

        CreateSectionHeader(content, "Current Status");
        var statusRow = CreateRow(content, "StatusRow", 64f);
        _generalStatusText = CreateText("StatusText", statusRow, "", 13f, TextDark, TextAlignmentOptions.TopLeft);
        var statusLayout = _generalStatusText.gameObject.AddComponent<LayoutElement>();
        statusLayout.flexibleWidth = 1f;
        CreateButton(statusRow, "Copy", "Copy", 80f, OnCopyGeneralStatus);

        RefreshGeneralStatus();
    }

    private void BuildConfigurationTab(RectTransform content)
    {
        if (!_configDirty)
            LoadServersFromConfig();

        CreateSectionHeader(content, "ComfyUI Servers");

        var help = CreateText("Help", content,
            "Add ComfyUI API endpoints here. Use --listen on remote ComfyUI servers, and add a token only for password-protected API endpoints.",
            13f, TextMuted, TextAlignmentOptions.TopLeft);
        help.textWrappingMode = TextWrappingModes.Normal;
        help.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        for (int i = 0; i < _workingServers.Count; i++)
            CreateServerEditor(content, i);

        var actions = CreateRow(content, "ConfigActions", 38f);
        CreateButton(actions, "AddServer", "Add Server", 120f, () =>
        {
            _workingServers.Add(new ComfyServerConfig { Url = "http://localhost:8188" });
            _configDirty = true;
            SetTab(AppSettingsTab.Configuration);
        });
        CreateButton(actions, "ReloadConfig", "Reload from config.txt", 160f, () =>
        {
            _configDirty = false;
            LoadServersFromConfig();
            SetTab(AppSettingsTab.Configuration);
            SetFooterStatus("Reloaded server rows from current config.");
        });
        CreateButton(actions, "ApplyReconnect", "Apply and reconnect", 170f, ApplyServerConfiguration);

        var statusRow = CreateRow(content, "ConfigStatusRow", 70f);
        _configStatusText = CreateText("ConfigStatus", statusRow, "", 13f, TextDark, TextAlignmentOptions.TopLeft);
        _configStatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _configStatusText.rectTransform.anchorMin = Vector2.zero;
        _configStatusText.rectTransform.anchorMax = Vector2.one;
        _configStatusText.rectTransform.offsetMin = new Vector2(6, 4);
        _configStatusText.rectTransform.offsetMax = new Vector2(-6, -4);
        RefreshConfigStatusText();
    }

    private void CreateServerEditor(RectTransform content, int index)
    {
        var server = _workingServers[index];
        var box = CreateVerticalBox(content, "Server_" + index, 150f);

        var titleRow = CreateRow(box, "ServerTitleRow", 28f);
        string title = "Server " + index;
        int liveIndex = FindLiveServerIndex(server.Url);
        if (liveIndex >= 0)
        {
            var live = Config.Get().GetGPUInfo(liveIndex);
            string liveName = string.IsNullOrEmpty(live._name) ? "ComfyUI" : live._name;
            title += " - connected as " + liveName + " Server " + liveIndex;
        }
        else
        {
            title += " - not connected";
        }
        var titleText = CreateText("Title", titleRow, title, 14f, TextDark, TextAlignmentOptions.MidlineLeft);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        if (liveIndex >= 0)
            CreateButton(titleRow, "Overrides", "Overrides...", 112f, () => ServerSettingsPanel.Show(liveIndex));
        CreateButton(titleRow, "Remove", "Remove", 86f, () =>
        {
            _workingServers.RemoveAt(index);
            _configDirty = true;
            SetTab(AppSettingsTab.Configuration);
        });

        var urlRow = CreateRow(box, "UrlRow", 34f);
        CreateLabel(urlRow, "URL", 78f);
        var urlInput = CreateInput(urlRow, server.Url, 620f);
        urlInput.onEndEdit.AddListener(value =>
        {
            server.Url = Config.NormalizeComfyServerUrl(value);
            urlInput.SetTextWithoutNotify(server.Url);
            _configDirty = true;
            RefreshConfigStatusText();
        });

        var nameTokenRow = CreateRow(box, "NameTokenRow", 34f);
        CreateLabel(nameTokenRow, "Name", 78f);
        var nameInput = CreateInput(nameTokenRow, server.DisplayName, 230f);
        nameInput.onEndEdit.AddListener(value =>
        {
            server.DisplayName = value.Trim();
            _configDirty = true;
        });
        CreateLabel(nameTokenRow, "Token", 58f);
        var tokenInput = CreateInput(nameTokenRow, server.AuthToken, 300f);
        tokenInput.onEndEdit.AddListener(value =>
        {
            server.AuthToken = value.Trim();
            _configDirty = true;
        });

        var vramRow = CreateRow(box, "VramRow", 34f);
        CreateLabel(vramRow, "VRAM (GB)", 78f);
        var vramText = server.VramGB > 0f ? server.VramGB.ToString("0.##", CultureInfo.InvariantCulture) : "";
        var vramInput = CreateInput(vramRow, vramText, 110f);
        vramInput.onEndEdit.AddListener(value =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                server.VramGB = 0f;
                _configDirty = true;
                return;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float gb) ||
                float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out gb))
            {
                server.VramGB = Mathf.Max(0f, gb);
                vramInput.SetTextWithoutNotify(server.VramGB > 0f ? server.VramGB.ToString("0.##", CultureInfo.InvariantCulture) : "");
                _configDirty = true;
            }
            else
            {
                SetFooterStatus("VRAM must be a number.");
                vramInput.SetTextWithoutNotify(server.VramGB > 0f ? server.VramGB.ToString("0.##", CultureInfo.InvariantCulture) : "");
            }
        });

        string status = GetLiveServerStatus(server.Url);
        var statusText = CreateText("LiveStatus", vramRow, status, 13f, TextMuted, TextAlignmentOptions.MidlineLeft);
        statusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private void BuildAudioTab(RectTransform content)
    {
        Config cfg = Config.Get();

        CreateSectionHeader(content, "Text To Speech");

        var box = CreateVerticalBox(content, "TextToSpeechBox", 292f);

        var providerRow = CreateRow(box, "TTSProviderRow", 36f);
        CreateLabel(providerRow, "Provider", 150f);
        int providerIndex = cfg != null && cfg.GetTextToSpeechProvider() == TextToSpeechProvider.ElevenLabs ? 1 : 0;
        _ttsProviderDropdown = CreateDropdown(providerRow, "TTSProviderDropdown",
            new List<string> { "None", "ElevenLabs" }, providerIndex, 220f);
        _ttsProviderDropdown.onValueChanged.AddListener(_ =>
        {
            SaveAudioSettingsFromFields();
            UpdateAudioControlInteractability();
        });

        var keyRow = CreateRow(box, "ElevenLabsKeyRow", 36f);
        CreateLabel(keyRow, "ElevenLabs API key", 150f);
        _elevenLabsApiKeyInput = CreateInput(keyRow, cfg != null ? cfg.GetElevenLabs_APIKey() : "", 560f);
        _elevenLabsApiKeyInput.contentType = TMP_InputField.ContentType.Password;
        _elevenLabsApiKeyInput.ForceLabelUpdate();
        _elevenLabsApiKeyInput.onEndEdit.AddListener(_ => SaveAudioSettingsFromFields());

        string currentVoiceID = cfg != null ? cfg.GetElevenLabs_voiceID() : ElevenLabsTextToSpeechManager.DefaultVoiceId;
        if (string.IsNullOrWhiteSpace(currentVoiceID))
            currentVoiceID = ElevenLabsTextToSpeechManager.DefaultVoiceId;

        var voiceRow = CreateRow(box, "ElevenLabsVoiceRow", 36f);
        CreateLabel(voiceRow, "Default voice", 150f);
        var voiceOptions = BuildElevenLabsVoiceOptions();
        _elevenLabsVoiceDropdown = CreateDropdown(voiceRow, "ElevenLabsVoiceDropdown",
            voiceOptions, GetElevenLabsVoiceDropdownIndex(currentVoiceID), 230f);
        _elevenLabsVoiceDropdown.onValueChanged.AddListener(index =>
        {
            if (index >= 0 && index < ElevenLabsTextToSpeechManager.DefaultVoicePresets.Length)
                _elevenLabsVoiceIdInput?.SetTextWithoutNotify(ElevenLabsTextToSpeechManager.DefaultVoicePresets[index].Value);
            SaveAudioSettingsFromFields();
        });

        var customVoiceRow = CreateRow(box, "ElevenLabsCustomVoiceRow", 36f);
        CreateLabel(customVoiceRow, "Custom voice ID", 150f);
        _elevenLabsVoiceIdInput = CreateInput(customVoiceRow, currentVoiceID, 560f);
        _elevenLabsVoiceIdInput.contentType = TMP_InputField.ContentType.Standard;
        _elevenLabsVoiceIdInput.onEndEdit.AddListener(_ =>
        {
            if (_elevenLabsVoiceDropdown != null)
            {
                _elevenLabsVoiceDropdown.SetValueWithoutNotify(GetElevenLabsVoiceDropdownIndex(_elevenLabsVoiceIdInput.text));
                _elevenLabsVoiceDropdown.RefreshShownValue();
            }
            SaveAudioSettingsFromFields();
        });

        var testTextRow = CreateRow(box, "TTSTestTextRow", 36f);
        CreateLabel(testTextRow, "Test phrase", 150f);
        _ttsTestTextInput = CreateInput(testTextRow, _ttsTestText, 560f);
        _ttsTestTextInput.onEndEdit.AddListener(value => _ttsTestText = value ?? "");

        var actionRow = CreateRow(box, "TTSActionRow", 36f);
        CreateLabel(actionRow, "", 150f);
        CreateButton(actionRow, "TestTTS", "Test", 90f, OnTestTextToSpeech);

        var statusRow = CreateRow(content, "AudioStatusRow", 70f);
        _audioStatusText = CreateText("AudioStatus", statusRow, BuildAudioStatusText(), 13f, TextDark, TextAlignmentOptions.TopLeft);
        _audioStatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _audioStatusText.rectTransform.anchorMin = Vector2.zero;
        _audioStatusText.rectTransform.anchorMax = Vector2.one;
        _audioStatusText.rectTransform.offsetMin = new Vector2(6, 4);
        _audioStatusText.rectTransform.offsetMax = new Vector2(-6, -4);

        UpdateAudioControlInteractability();
    }

    private List<string> BuildElevenLabsVoiceOptions()
    {
        var options = new List<string>();
        foreach (var preset in ElevenLabsTextToSpeechManager.DefaultVoicePresets)
            options.Add(preset.Key);
        options.Add("Custom");
        return options;
    }

    private int GetElevenLabsVoiceDropdownIndex(string voiceID)
    {
        voiceID = (voiceID ?? "").Trim();
        for (int i = 0; i < ElevenLabsTextToSpeechManager.DefaultVoicePresets.Length; i++)
        {
            if (string.Equals(ElevenLabsTextToSpeechManager.DefaultVoicePresets[i].Value, voiceID, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return ElevenLabsTextToSpeechManager.DefaultVoicePresets.Length;
    }

    private void SaveAudioSettingsFromFields()
    {
        Config cfg = Config.Get();
        if (cfg == null)
        {
            SetAudioStatus("Config is not initialized.");
            return;
        }

        TextToSpeechProvider provider = _ttsProviderDropdown != null && _ttsProviderDropdown.value == 1
            ? TextToSpeechProvider.ElevenLabs
            : TextToSpeechProvider.None;
        string apiKey = _elevenLabsApiKeyInput != null ? _elevenLabsApiKeyInput.text : cfg.GetElevenLabs_APIKey();
        string voiceID = _elevenLabsVoiceIdInput != null ? _elevenLabsVoiceIdInput.text : cfg.GetElevenLabs_voiceID();
        if (string.IsNullOrWhiteSpace(voiceID))
        {
            voiceID = ElevenLabsTextToSpeechManager.DefaultVoiceId;
            _elevenLabsVoiceIdInput?.SetTextWithoutNotify(voiceID);
        }

        cfg.SetTextToSpeechSettings(provider, apiKey, voiceID);
        SetAudioStatus(BuildAudioStatusText());
        SetFooterStatus("Saved Text To Speech settings to config.txt.");
    }

    private void UpdateAudioControlInteractability()
    {
        if (_elevenLabsApiKeyInput != null) _elevenLabsApiKeyInput.interactable = true;
        if (_elevenLabsVoiceDropdown != null) _elevenLabsVoiceDropdown.interactable = true;
        if (_elevenLabsVoiceIdInput != null) _elevenLabsVoiceIdInput.interactable = true;
        if (_ttsTestTextInput != null) _ttsTestTextInput.interactable = true;
    }

    private string BuildAudioStatusText()
    {
        Config cfg = Config.Get();
        if (cfg == null) return "Audio settings are not initialized.";

        if (cfg.GetTextToSpeechProvider() == TextToSpeechProvider.None)
            return "Text To Speech provider: None.";

        string voiceID = cfg.GetElevenLabs_voiceID();
        string voiceLabel = GetElevenLabsVoiceLabel(voiceID);
        string keyStatus = string.IsNullOrWhiteSpace(cfg.GetElevenLabs_APIKey()) ? "API key missing" : "API key saved";
        return "Text To Speech provider: ElevenLabs.\n" +
            keyStatus + ". Voice: " + voiceLabel + ".";
    }

    private string GetElevenLabsVoiceLabel(string voiceID)
    {
        voiceID = (voiceID ?? "").Trim();
        foreach (var preset in ElevenLabsTextToSpeechManager.DefaultVoicePresets)
        {
            if (string.Equals(preset.Value, voiceID, StringComparison.OrdinalIgnoreCase))
                return preset.Key;
        }
        return string.IsNullOrEmpty(voiceID) ? "(none)" : "Custom";
    }

    private void SetAudioStatus(string text)
    {
        if (_audioStatusText != null)
            _audioStatusText.text = text ?? "";
        SetFooterStatus(text);
    }

    private void OnTestTextToSpeech()
    {
        SaveAudioSettingsFromFields();
        _ttsTestText = _ttsTestTextInput != null ? _ttsTestTextInput.text : _ttsTestText;
        if (!ElevenLabsTextToSpeechManager.SpeakConfigured(_ttsTestText, SetAudioStatus))
            return;
    }

    private void LoadServersFromConfig()
    {
        _workingServers.Clear();
        if (Config.Get() != null)
        {
            var servers = Config.Get().GetModernComfyServerConfigs();
            foreach (var server in servers)
                _workingServers.Add(server.Clone());
        }
    }

    private void ApplyServerConfiguration()
    {
        ApplyServerConfiguration(false);
    }

    private void ApplyServerConfigurationAndClose()
    {
        ApplyServerConfiguration(true);
    }

    private void ApplyServerConfiguration(bool closeOnSuccess)
    {
        ApplyServerConfiguration(closeOnSuccess, false);
    }

    private void ApplyServerConfiguration(bool closeOnSuccess, bool forceReset)
    {
        if (!_configDirty)
            LoadServersFromConfig();

        if (!forceReset && HasActiveGenerationWork())
        {
            SetConfigStatus("Reconnect blocked because generation, queued GPU work, or a GPU request is active.");
            ShowForceReconnectDialog(closeOnSuccess);
            return;
        }

        if (!TryValidateServers(out var sanitized, out string validationError))
        {
            SetConfigStatus(validationError);
            return;
        }

        if (Config.Get() == null)
        {
            SetConfigStatus("Config is not initialized.");
            return;
        }

        if (forceReset)
            ForceCancelGenerationWorkForReconnect();

        if (!Config.Get().SaveModernComfyServerConfigs(sanitized, out string error))
        {
            SetConfigStatus("Failed to save config.txt: " + error);
            return;
        }

        _configDirty = false;
        LoadServersFromConfig();
        if (closeOnSuccess)
        {
            Hide();
            return;
        }

        SetTab(AppSettingsTab.Configuration);
        SetFooterStatus("Saved config.txt and reconnecting to ComfyUI servers.");
    }

    private void ShowForceReconnectDialog(bool closeOnSuccess)
    {
        HideForceReconnectDialog();

        if (_panelRoot == null)
            return;

        var overlay = CreateRect("ForceReconnectDialogOverlay", _panelRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        overlay.SetAsLastSibling();
        var overlayImg = overlay.gameObject.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.38f);
        var overlayButton = overlay.gameObject.AddComponent<Button>();
        overlayButton.targetGraphic = overlayImg;
        overlayButton.onClick.AddListener(HideForceReconnectDialog);
        _forceReconnectDialogRoot = overlay.gameObject;

        var dialog = CreateRect("Dialog", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        dialog.sizeDelta = new Vector2(590f, 270f);
        var dialogImg = dialog.gameObject.AddComponent<Image>();
        dialogImg.color = PanelBg;
        var dialogButton = dialog.gameObject.AddComponent<Button>();
        dialogButton.targetGraphic = dialogImg;
        dialogButton.onClick.AddListener(() => { });

        var title = CreateText("Title", dialog, "Force GPU reset?", 20f, TextDark, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.offsetMin = new Vector2(24f, -58f);
        title.rectTransform.offsetMax = new Vector2(-24f, -18f);

        var body = CreateText("Body", dialog, BuildForceReconnectWarningText(), 14f, TextMuted, TextAlignmentOptions.TopLeft);
        body.overflowMode = TextOverflowModes.Overflow;
        body.rectTransform.anchorMin = new Vector2(0f, 0f);
        body.rectTransform.anchorMax = new Vector2(1f, 1f);
        body.rectTransform.offsetMin = new Vector2(24f, 82f);
        body.rectTransform.offsetMax = new Vector2(-24f, -68f);

        var forceButton = CreateButton(dialog, "ForceReset", "Force reset", 140f, () => ApplyServerConfiguration(closeOnSuccess, true));
        var forceRt = forceButton.GetComponent<RectTransform>();
        forceRt.anchorMin = new Vector2(1f, 0f);
        forceRt.anchorMax = new Vector2(1f, 0f);
        forceRt.pivot = new Vector2(1f, 0f);
        forceRt.anchoredPosition = new Vector2(-154f, 24f);
        var forceImg = forceButton.GetComponent<Image>();
        if (forceImg != null)
            forceImg.color = new Color(0.72f, 0.22f, 0.18f, 1f);
        var forceLabel = forceButton.GetComponentInChildren<TextMeshProUGUI>();
        if (forceLabel != null)
            forceLabel.color = Color.white;

        var cancelButton = CreateButton(dialog, "Cancel", "Cancel", 110f, HideForceReconnectDialog);
        var cancelRt = cancelButton.GetComponent<RectTransform>();
        cancelRt.anchorMin = new Vector2(1f, 0f);
        cancelRt.anchorMax = new Vector2(1f, 0f);
        cancelRt.pivot = new Vector2(1f, 0f);
        cancelRt.anchoredPosition = new Vector2(-24f, 24f);
    }

    private void HideForceReconnectDialog()
    {
        if (_forceReconnectDialogRoot != null)
            Destroy(_forceReconnectDialogRoot);
        _forceReconnectDialogRoot = null;
    }

    private string BuildForceReconnectWarningText()
    {
        int rawBusy = 0;
        int pendingLLM = 0;
        var cfg = Config.Get();
        if (cfg != null)
        {
            for (int i = 0; i < cfg.GetGPUCount(); i++)
            {
                var info = cfg.GetGPUInfo(i);
                if (info == null) continue;
                if (info.IsGPUBusy) rawBusy++;
                pendingLLM += Mathf.Max(0, info.pendingLLMCount);
            }
        }

        var generator = ImageGenerator.Get();
        int queued = generator != null ? generator.GetCountOfQueudCommands() : 0;
        bool generating = generator != null && generator.IsGenerating();

        return
            "Some GPU work is still marked active. You can wait and try again, or force a reset now.\n\n" +
            "Force reset will stop continuous generation, clear queued GPU work, cancel active Pic jobs, release busy GPU flags, and reconnect ComfyUI servers. Running ComfyUI jobs are interrupted when possible.\n\n" +
            "Current state: " + rawBusy + " busy GPU(s), " + pendingLLM + " pending LLM-gated GPU job(s), " +
            queued + " queued GPU event(s), generation " + (generating ? "on" : "off") + ".";
    }

    private void ForceCancelGenerationWorkForReconnect()
    {
        HideForceReconnectDialog();

        var generator = ImageGenerator.Get();
        if (generator != null)
        {
            generator.ShutdownAllGPUProcesses(true);
        }
        else
        {
            var picsRoot = RTUtil.FindObjectOrCreate("Pics");
            var pics = picsRoot.transform.GetComponentsInChildren<PicMain>();
            foreach (var pic in pics)
            {
                if (pic != null && !pic.IsDestroyed())
                    pic.KillGPUProcesses(true);
            }
        }

        Config.Get()?.ForceClearRuntimeGPUState();
    }

    private bool TryValidateServers(out List<ComfyServerConfig> sanitized, out string error)
    {
        sanitized = new List<ComfyServerConfig>();
        error = "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _workingServers.Count; i++)
        {
            var source = _workingServers[i] ?? new ComfyServerConfig();
            var server = source.Clone();
            server.Url = Config.NormalizeComfyServerUrl(server.Url);
            server.DisplayName = (server.DisplayName ?? "").Trim();
            server.AuthToken = (server.AuthToken ?? "").Trim();

            if (string.IsNullOrEmpty(server.Url))
            {
                error = "Server " + i + " needs a URL.";
                return false;
            }

            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host))
            {
                error = "Server " + i + " URL must be http:// or https:// with a host.";
                return false;
            }

            if (!seen.Add(server.Url))
            {
                error = "Duplicate ComfyUI server URL: " + server.Url;
                return false;
            }

            sanitized.Add(server);
        }

        return true;
    }

    private bool HasActiveGenerationWork()
    {
        var generator = ImageGenerator.Get();
        if (generator != null && (generator.IsGenerating() || generator.GetCountOfQueudCommands() > 0))
            return true;

        var cfg = Config.Get();
        if (cfg == null) return false;

        for (int i = 0; i < cfg.GetGPUCount(); i++)
        {
            if (cfg.IsGPUBusy(i))
                return true;
        }

        return false;
    }

    private int FindLiveServerIndex(string url)
    {
        var cfg = Config.Get();
        if (cfg == null) return -1;

        string normalized = Config.NormalizeComfyServerUrl(url);
        for (int i = 0; i < cfg.GetGPUCount(); i++)
        {
            string live = Config.NormalizeComfyServerUrl(cfg.GetGPUInfo(i).remoteURL);
            if (string.Equals(normalized, live, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private string GetLiveServerStatus(string url)
    {
        int liveIndex = FindLiveServerIndex(url);
        if (liveIndex < 0) return "Not connected yet";

        var info = Config.Get().GetGPUInfo(liveIndex);
        string active = info._bIsActive ? "active" : "disabled";
        string busy = Config.Get().IsGPUBusy(liveIndex) ? ", busy" : ", idle";
        string vram = info._vramGB > 0f ? ", " + info._vramGB.ToString("0.##", CultureInfo.InvariantCulture) + " GB" : "";
        return "Connected as Server " + liveIndex + " (" + active + busy + vram + ")";
    }

    private void SaveImageEditorPath(string value)
    {
        if (Config.Get() == null) return;
        Config.Get().SetImageEditorPathAndExe(value);
        if (_imageEditorInput != null)
            _imageEditorInput.SetTextWithoutNotify(Config.Get().GetImageEditorPathAndExe());
        SetFooterStatus("Saved photo editor path to config.txt.");
    }

    private void OnBrowseImageEditor()
    {
        string current = _imageEditorInput != null ? _imageEditorInput.text : "";
        string picked = OpenExecutablePicker(current);
        if (string.IsNullOrEmpty(picked)) return;

        if (_imageEditorInput != null)
            _imageEditorInput.SetTextWithoutNotify(picked);
        SaveImageEditorPath(picked);
    }

    private string OpenExecutablePicker(string currentPath)
    {
        string initialDir = "";
        try
        {
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                initialDir = Path.GetDirectoryName(currentPath);
        }
        catch
        {
            initialDir = "";
        }

#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel("Select photo editor executable", initialDir, "exe");
#elif UNITY_STANDALONE_WIN
        return OpenWindowsExecutableDialog(initialDir);
#else
        SetFooterStatus("Browse is only available in the Unity editor or Windows builds. Paste the executable path instead.");
        return "";
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class OpenFileName
    {
        public int structSize = 0;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public string filter = null;
        public string customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 1;
        public string file = null;
        public int maxFile = 0;
        public string fileTitle = null;
        public int maxFileTitle = 0;
        public string initialDir = null;
        public string title = null;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public string defExt = null;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public string templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    private string OpenWindowsExecutableDialog(string initialDir)
    {
        var ofn = new OpenFileName();
        ofn.structSize = Marshal.SizeOf(ofn);
        ofn.filter = "Executable Files (*.exe)\0*.exe\0All Files (*.*)\0*.*\0\0";
        ofn.file = new string('\0', 4096);
        ofn.maxFile = ofn.file.Length;
        ofn.fileTitle = new string('\0', 512);
        ofn.maxFileTitle = ofn.fileTitle.Length;
        ofn.initialDir = initialDir;
        ofn.title = "Select photo editor executable";
        ofn.defExt = "exe";
        ofn.flags = 0x00001000 | 0x00000800 | 0x00000008; // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR

        if (!GetOpenFileName(ofn))
            return "";

        return ofn.file.Split('\0')[0];
    }
#endif

    private void RefreshGeneralStatus()
    {
        if (_generalStatusText == null) return;

        var generator = ImageGenerator.Get();
        var gl = GameLogic.Get();
        string text;
        if (generator != null && generator.IsGenerating())
        {
            string max = gl != null && gl.GetMaxToGenerate() > 0 ? gl.GetMaxToGenerate().ToString() : "unlimited";
            text = "Generating. Count: " + generator.GetCurrentGenerationCount() + " / " + max;
        }
        else
        {
            text = "Not generating.";
        }

        if (generator != null && generator.GetCountOfQueudCommands() > 0)
            text += "\nQueued GPU commands: " + generator.GetCountOfQueudCommands();

        if (gl != null && !string.IsNullOrEmpty(gl.GetLastModifiedPrompt()))
            text += "\nLast modified prompt:\n" + gl.GetLastModifiedPrompt();

        _generalStatusText.text = text;
    }

    private void OnCopyGeneralStatus()
    {
        RefreshGeneralStatus();
        if (_generalStatusText == null) return;
        GUIUtility.systemCopyBuffer = _generalStatusText.text;
        SetFooterStatus("Copied current status to clipboard.");
    }

    private void RefreshConfigStatusText()
    {
        if (_configStatusText == null) return;

        int liveCount = Config.Get() != null ? Config.Get().GetGPUCount() : 0;
        _configStatusText.text = _workingServers.Count + " configured row(s). " +
            liveCount + " connected server button(s). " +
            (_configDirty ? "Unsaved changes." : "No unsaved changes.");
    }

    private void SetConfigStatus(string text)
    {
        if (_configStatusText != null)
            _configStatusText.text = text;
        SetFooterStatus(text);
    }

    private void SetFooterStatus(string text)
    {
        if (_footerStatusText != null)
            _footerStatusText.text = text ?? "";
    }

    private void Update()
    {
        if (_activeTab == AppSettingsTab.General)
            RefreshGeneralStatus();
    }

    private RectTransform CreateVerticalBox(Transform parent, string name, float minHeight)
    {
        var box = new GameObject(name);
        box.transform.SetParent(parent, false);
        var rt = box.AddComponent<RectTransform>();
        var img = box.AddComponent<Image>();
        img.color = RowBg;

        var layout = box.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var le = box.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.preferredHeight = minHeight;
        return rt;
    }

    private RectTransform CreateRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name);
        row.transform.SetParent(parent, false);
        var rt = row.AddComponent<RectTransform>();

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 8f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return rt;
    }

    private void CreateSectionHeader(Transform parent, string text)
    {
        var header = new GameObject("Header_" + text);
        header.transform.SetParent(parent, false);
        var rt = header.AddComponent<RectTransform>();
        var le = header.AddComponent<LayoutElement>();
        le.minHeight = 28f;
        le.preferredHeight = 28f;

        var tmp = CreateText("Text", rt, text, 15f, TextDark, TextAlignmentOptions.MidlineLeft);
        tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.anchorMin = Vector2.zero;
        tmp.rectTransform.anchorMax = Vector2.one;
        tmp.rectTransform.offsetMin = Vector2.zero;
        tmp.rectTransform.offsetMax = Vector2.zero;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, float width)
    {
        var label = CreateText("Label", parent, text, BaseFontSize, TextDark, TextAlignmentOptions.MidlineLeft);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        return label;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private TMP_InputField CreateInput(Transform parent, string value, float width)
    {
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "Input";
        inputGo.transform.SetParent(parent, false);
        var rt = inputGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 30f);
        var le = inputGo.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        if (width >= 300f)
            le.flexibleWidth = 1f;

        var textArea = inputGo.transform.Find("Text Area") as RectTransform;
        if (textArea != null)
        {
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.pivot = new Vector2(0.5f, 0.5f);
            textArea.offsetMin = new Vector2(6, 2);
            textArea.offsetMax = new Vector2(-6, -2);
            if (textArea.GetComponent<RectMask2D>() == null)
                textArea.gameObject.AddComponent<RectMask2D>();
        }

        var input = inputGo.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        if (textArea != null)
            input.textViewport = textArea;
        LLMSettingsPanel.ConfigureInputFieldVisuals(input, _font);
        TMPInputFieldUndo.Ensure(input);

        if (input.textComponent != null)
        {
            var textRt = input.textComponent.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            input.textComponent.overflowMode = TextOverflowModes.Overflow;
            input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent.richText = false;
            input.textComponent.parseCtrlCharacters = false;
            input.textComponent.enableAutoSizing = false;
            input.textComponent.fontSize = BaseFontSize;
        }

        if (input.placeholder is TextMeshProUGUI placeholder)
        {
            var placeholderRt = placeholder.rectTransform;
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = Vector2.zero;
            placeholderRt.offsetMax = Vector2.zero;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.richText = false;
            placeholder.parseCtrlCharacters = false;
            placeholder.enableAutoSizing = false;
            placeholder.fontSize = BaseFontSize;
            placeholder.text = "";
            placeholder.gameObject.SetActive(false);
        }

        foreach (var placeholderText in inputGo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (placeholderText != input.textComponent &&
                placeholderText.name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                placeholderText.text = "";
                placeholderText.gameObject.SetActive(false);
            }
        }

        var img = inputGo.GetComponent<Image>();
        if (img != null)
            img.color = InputBg;

        input.SetTextWithoutNotify(value ?? "");
        input.ForceLabelUpdate();

        return input;
    }

    private TMP_Dropdown CreateDropdown(Transform parent, string name, List<string> options, int selectedIndex, float width)
    {
        var ddGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        ddGo.name = name;
        ddGo.transform.SetParent(parent, false);
        var rt = ddGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 30f);

        var le = ddGo.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = 30f;
        le.preferredHeight = 30f;

        var img = ddGo.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = InputBg;
        }

        var dropdown = ddGo.GetComponent<TMP_Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(options ?? new List<string>());
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, dropdown.options.Count - 1));
        dropdown.SetValueWithoutNotify(selectedIndex);
        dropdown.RefreshShownValue();

        if (dropdown.captionText != null)
        {
            dropdown.captionText.font = _font;
            dropdown.captionText.fontSize = BaseFontSize;
            dropdown.captionText.color = TextDark;
            dropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;
            dropdown.captionText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (dropdown.itemText != null)
        {
            dropdown.itemText.font = _font;
            dropdown.itemText.fontSize = BaseFontSize;
            dropdown.itemText.color = TextDark;
            dropdown.itemText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (dropdown.template != null)
            dropdown.template.sizeDelta = new Vector2(dropdown.template.sizeDelta.x, 210f);

        var arrow = CreateText("Arrow", ddGo.transform, "v", 12f, TextMuted, TextAlignmentOptions.Center);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.sizeDelta = new Vector2(24f, 0f);
        arrow.rectTransform.anchoredPosition = Vector2.zero;
        arrow.raycastTarget = false;

        return dropdown;
    }

    private Button CreateButton(Transform parent, string name, string text, float width, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject(name);
        btnGo.transform.SetParent(parent, false);
        var rt = btnGo.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 30f);
        var le = btnGo.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = 30f;
        le.preferredHeight = 30f;

        var img = btnGo.AddComponent<Image>();
        img.color = ButtonBg;
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        btn.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f),
            pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f),
            selectedColor = new Color(0.94f, 0.94f, 0.94f, 1f),
            disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        CreateTextButtonLabel(btnGo.transform, text);
        return btn;
    }

    private TextMeshProUGUI CreateTextButton(Transform parent, string name, string text, float width, UnityEngine.Events.UnityAction onClick)
    {
        var btn = CreateButton(parent, name, "", width, onClick);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = text;
        return label;
    }

    private void CreateTextButtonLabel(Transform parent, string text)
    {
        var label = CreateText("Text", parent, text, BaseFontSize, TextDark, TextAlignmentOptions.Center);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(4, 0);
        label.rectTransform.offsetMax = new Vector2(-4, 0);
        label.fontStyle = FontStyles.Bold;
    }

    private Toggle CreateToggleRow(Transform parent, string labelText, bool value, UnityEngine.Events.UnityAction<bool> callback)
    {
        var row = CreateRow(parent, "ToggleRow", 30f);

        var toggleRoot = new GameObject("Toggle");
        toggleRoot.transform.SetParent(row, false);
        var le = toggleRoot.AddComponent<LayoutElement>();
        le.minWidth = 24f;
        le.preferredWidth = 24f;

        var toggle = toggleRoot.AddComponent<Toggle>();
        var bg = CreateRect("Background", toggleRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        bg.sizeDelta = new Vector2(18, 18);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = Color.white;

        var check = CreateRect("Checkmark", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        check.sizeDelta = new Vector2(11, 11);
        var checkImg = check.gameObject.AddComponent<Image>();
        checkImg.color = CheckColor;

        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(callback);

        var label = CreateText("Label", row, labelText, BaseFontSize, TextDark, TextAlignmentOptions.MidlineLeft);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return toggle;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }
}
