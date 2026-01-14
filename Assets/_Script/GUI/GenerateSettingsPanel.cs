using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Programmatically-created General Settings panel.
/// Follows the LLMSettingsPanel pattern for dynamic UI creation.
/// </summary>
public class GenerateSettingsPanel : MonoBehaviour
{
    private static GenerateSettingsPanel _instance;
    private static GameObject _panelRoot;

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;

    // UI references
    private Toggle _randomizeToggle;
    private Toggle _cameraFollowToggle;
    private Toggle _autoSaveToggle;
    private Toggle _autoSavePNGToggle;
    private Toggle _stripThinkTagsToggle;
    private TMP_InputField _maxPicsInput;
    private TextMeshProUGUI _statusText;
    private TMP_Dropdown _autoPicDropdown;

    // Panel dimensions
    private const float PANEL_WIDTH = 800f;
    private const float PANEL_HEIGHT = 780f;
    private const float HEADER_HEIGHT = 40f;
    private const float FOOTER_HEIGHT = 50f;
    private const float BASE_FONT_SIZE = 14f;

    // Theme colors (matching existing UI)
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color InputFieldBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color CheckmarkColor = new Color(0.2f, 0.5f, 0.2f, 1f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 1f);

    private static Sprite _uiBackgroundSprite;
    private static Sprite _dropdownArrowSprite;
    private static Color? _dropdownArrowColor;
    private static bool _spritesCached;

    public static GenerateSettingsPanel Get()
    {
        return _instance;
    }

    public static void Show()
    {
        if (_instance != null)
        {
            _panelRoot.SetActive(true);
            _instance.RefreshFromSettings();
            return;
        }

        _panelRoot = new GameObject("GenerateSettingsPanel");
        _instance = _panelRoot.AddComponent<GenerateSettingsPanel>();
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

    // Legacy compatibility methods
    public void ShowWindow() => Show();
    public void HideWindow() => Hide();
    public void ToggleWindow() => Toggle();

    // Public accessor for stripThinkTagsToggle (used by GameLogic.PrepareLLMLinesForSending)
    // Returns a dummy toggle with isOn=true if panel hasn't been created yet
    public Toggle m_stripThinkTagsToggle
    {
        get
        {
            if (_stripThinkTagsToggle != null)
                return _stripThinkTagsToggle;
            
            // Return a temporary toggle with default value if panel not created yet
            // This maintains backwards compatibility
            EnsureCreated();
            return _stripThinkTagsToggle;
        }
    }

    /// <summary>
    /// Ensures the panel is created (but hidden). Call during app init or when settings are accessed.
    /// </summary>
    public static void EnsureCreated()
    {
        if (_instance == null)
        {
            Show();
            Hide();
        }
    }

    /// <summary>
    /// Static accessor for strip think tags setting. Returns true if panel not created yet.
    /// </summary>
    public static bool GetStripThinkTags()
    {
        if (_instance != null && _instance._stripThinkTagsToggle != null)
            return _instance._stripThinkTagsToggle.isOn;
        return true; // Default to true
    }

    /// <summary>
    /// Static accessor for default AutoPic script. Returns the selected script name,
    /// falling back to "AutoPic.txt" if nothing is configured or the file doesn't exist.
    /// </summary>
    public static string GetDefaultAutoPicScript()
    {
        const string defaultScript = "AutoPic.txt";

        // Try to get from UserPreferences first
        var prefs = UserPreferences.Get();
        string savedScript = prefs != null && !string.IsNullOrEmpty(prefs.DefaultAutoPicScript) 
            ? prefs.DefaultAutoPicScript 
            : defaultScript;

        // Verify the script exists in PresetManager
        if (PresetManager.Get() != null && PresetManager.Get().DoesPresetExistByNameNotCaseSensitive(savedScript))
        {
            return savedScript;
        }

        // Fall back to default if saved script doesn't exist
        if (PresetManager.Get() != null && PresetManager.Get().DoesPresetExistByNameNotCaseSensitive(defaultScript))
        {
            return defaultScript;
        }

        // Last resort: return what was configured even if it doesn't exist
        return savedScript;
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

    private static void CacheSpritesFromExistingUI()
    {
        if (_spritesCached) return;

        // Find dropdown arrow sprite from existing dropdowns
        foreach (var dd in Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
        {
            if (dd == null) continue;
            if (_panelRoot != null && dd.transform.IsChildOf(_panelRoot.transform)) continue;

            var ddImg = dd.GetComponent<Image>();
            var arrowImg = dd.transform.Find("Arrow")?.GetComponent<Image>();

            if (ddImg != null && ddImg.sprite != null && _uiBackgroundSprite == null)
            {
                _uiBackgroundSprite = ddImg.sprite;
            }

            if (arrowImg != null && arrowImg.sprite != null && _dropdownArrowSprite == null)
            {
                _dropdownArrowSprite = arrowImg.sprite;
                _dropdownArrowColor = arrowImg.color;
            }

            if (_uiBackgroundSprite != null && _dropdownArrowSprite != null)
            {
                _spritesCached = true;
                return;
            }
        }

        // Fallback: Find an existing Image in the scene with a sprite
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null) continue;
            if (_panelRoot != null && img.transform.IsChildOf(_panelRoot.transform)) continue;

            // Look for UI background sprite (the rounded rect)
            if (img.type == Image.Type.Sliced && img.sprite.name.Contains("Background") || 
                img.sprite.name.Contains("UISprite"))
            {
                _uiBackgroundSprite = img.sprite;
                break;
            }
        }

        // Fallback: use any sliced sprite
        if (_uiBackgroundSprite == null)
        {
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img == null || img.sprite == null) continue;
                if (_panelRoot != null && img.transform.IsChildOf(_panelRoot.transform)) continue;
                if (img.type == Image.Type.Sliced)
                {
                    _uiBackgroundSprite = img.sprite;
                    break;
                }
            }
        }

        _spritesCached = _uiBackgroundSprite != null;
    }

    private static void ApplyUISprite(Image img)
    {
        if (img == null) return;
        CacheSpritesFromExistingUI();
        if (_uiBackgroundSprite != null)
        {
            img.sprite = _uiBackgroundSprite;
            img.type = Image.Type.Sliced;
        }
    }

    private void CreateUI()
    {
        _font = FindFont();
        CacheSpritesFromExistingUI();

        // Canvas
        var canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panelRoot.AddComponent<GraphicRaycaster>();

        // Main panel
        var main = new GameObject("MainPanel");
        main.transform.SetParent(_panelRoot.transform, false);
        _mainPanel = main.AddComponent<RectTransform>();
        _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
        _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
        _mainPanel.pivot = new Vector2(0.5f, 0.5f);
        _mainPanel.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
        var panelImg = main.AddComponent<Image>();
        ApplyUISprite(panelImg);
        panelImg.color = PanelBg;

        CreateHeader();
        RectTransform contentRoot = CreateContentRoot();
        CreateFooter();
        BuildContent(contentRoot);

        RefreshFromSettings();
        StartCoroutine(RestyleNextFrame());
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
        ApplyUISprite(headerImg);
        headerImg.color = HeaderBg;

        // Make header draggable
        var dragHandler = header.AddComponent<GenerateSettingsDragHandler>();
        dragHandler.SetTarget(_mainPanel);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-36, 0);

        var title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "General Settings";
        title.font = _font;
        title.fontSize = 18;
        title.fontStyle = FontStyles.Bold;
        title.color = TextTitle;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        // Close button
        var close = new GameObject("Close");
        close.transform.SetParent(header.transform, false);
        var closeRt = close.AddComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1, 0.5f);
        closeRt.anchorMax = new Vector2(1, 0.5f);
        closeRt.pivot = new Vector2(1, 0.5f);
        closeRt.sizeDelta = new Vector2(28, 28);
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
        xTxt.fontSize = 16;
        xTxt.fontStyle = FontStyles.Bold;
        xTxt.color = Color.white;
        xTxt.alignment = TextAlignmentOptions.Center;
    }

    private RectTransform CreateContentRoot()
    {
        var root = new GameObject("ContentRoot");
        root.transform.SetParent(_mainPanel, false);
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, FOOTER_HEIGHT);
        rt.offsetMax = new Vector2(0, -HEADER_HEIGHT);
        return rt;
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
        ApplyUISprite(footerImg);
        footerImg.color = FooterBg;

        CreateFooterButton(footer.transform, "Ok", -60f, 100f, Hide);
        CreateFooterButton(footer.transform, "Copy to clipboard", 80f, 150f, OnCopyToClipboard);
    }

    private void CreateFooterButton(Transform parent, string text, float xOffset, float width, UnityEngine.Events.UnityAction onClick)
    {
        var btn = new GameObject("Btn_" + text);
        btn.transform.SetParent(parent, false);
        var rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0);
        rt.sizeDelta = new Vector2(width, 32);

        var img = btn.AddComponent<Image>();
        ApplyUISprite(img);
        img.color = ButtonColor;
        var button = btn.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f),
            pressedColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 1f),
            selectedColor = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f),
            disabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 0.5019608f),
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
        tmp.color = TextTitle;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private void BuildContent(RectTransform contentRoot)
    {
        // ScrollView
        var scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(contentRoot, false);
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

        // Viewport
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-16, 0);
        var vpImg = viewport.AddComponent<Image>();
        ApplyUISprite(vpImg);
        vpImg.color = PanelBg;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // Content
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 12, 12);
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRt;
        scrollRect.content = contentRt;

        // Scrollbar
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(scrollGo.transform, false);
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = new Vector2(1, 1);
        sbRt.pivot = new Vector2(1, 0.5f);
        sbRt.sizeDelta = new Vector2(12, 0);
        sbRt.anchoredPosition = Vector2.zero;
        sbGo.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.24f, 1f);

        var scrollbar = sbGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var handle = new GameObject("Handle");
        handle.transform.SetParent(sbGo.transform, false);
        var handleRt = handle.AddComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = new Vector2(2, 2);
        handleRt.offsetMax = new Vector2(-2, -2);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(0.45f, 0.45f, 0.5f, 1f);

        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handleImg;
        scrollRect.verticalScrollbar = scrollbar;

        // Build all the settings rows
        CreateMaxPicsRow(content.transform);
        _randomizeToggle = CreateToggleRow(content.transform, "Randomize prompts", 
            "Randomizing prompts will randomly omit a word or phrase between commas. This is an easy way to add a bit of variation to your generations.",
            OnRandomizeToggleChanged, 70);
        _cameraFollowToggle = CreateToggleRow(content.transform, "Camera follow mode - Causes camera to auto move down vertically when a new row of rendered image starts.",
            null, OnCameraFollowToggleChanged, 40);
        _autoSaveToggle = CreateToggleRow(content.transform, "Auto save all generated pics as .bmp (including mask) to the autosave subdir",
            null, OnAutoSaveToggleChanged, 40);
        _autoSavePNGToggle = CreateToggleRow(content.transform, "Auto save all generated pics as a .png to the autosave subdir",
            null, OnAutoSavePNGToggleChanged, 40);

        CreateSectionHeader(content.transform, "Adventure Mode:");
        CreateAutoPicDropdownRow(content.transform);

        CreateSectionHeader(content.transform, "LLM Settings:");
        _stripThinkTagsToggle = CreateToggleRow(content.transform, "Strip <think> tags when sending to LLMs",
            null, OnStripThinkTagsChanged, 24);

        CreateSectionHeader(content.transform, "Current status:");
        CreateStatusTextRow(content.transform);
    }

    private void CreateMaxPicsRow(Transform parent)
    {
        var row = new GameObject("MaxPicsRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 32;
        rowLayout.preferredHeight = 32;

        // Label before - manual positioning
        var labelBefore = new GameObject("LabelBefore");
        labelBefore.transform.SetParent(row.transform, false);
        var labelBeforeRt = labelBefore.AddComponent<RectTransform>();
        labelBeforeRt.anchorMin = new Vector2(0, 0);
        labelBeforeRt.anchorMax = new Vector2(0, 1);
        labelBeforeRt.pivot = new Vector2(0, 0.5f);
        labelBeforeRt.sizeDelta = new Vector2(195, 0);
        labelBeforeRt.anchoredPosition = new Vector2(0, 0);
        
        var labelBeforeTMP = labelBefore.AddComponent<TextMeshProUGUI>();
        labelBeforeTMP.font = _font;
        labelBeforeTMP.fontSize = BASE_FONT_SIZE;
        labelBeforeTMP.color = TextDark;
        labelBeforeTMP.text = "Stop after generating/inpainting";
        labelBeforeTMP.alignment = TextAlignmentOptions.MidlineLeft;

        // Input field - manual positioning using TMP_DefaultControls
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "MaxPicsInput";
        inputGo.transform.SetParent(row.transform, false);
        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0.5f);
        inputRt.anchorMax = new Vector2(0, 0.5f);
        inputRt.pivot = new Vector2(0, 0.5f);
        inputRt.sizeDelta = new Vector2(70, 28);
        inputRt.anchoredPosition = new Vector2(200, 0);

        _maxPicsInput = inputGo.GetComponent<TMP_InputField>();
        _maxPicsInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _maxPicsInput.onEndEdit.AddListener(OnMaxPicsChanged);
        
        // Configure caret and selection for visibility
        _maxPicsInput.caretWidth = 2;
        _maxPicsInput.customCaretColor = true;
        _maxPicsInput.caretColor = TextDark;
        _maxPicsInput.selectionColor = new Color(0.25f, 0.5f, 1f, 0.4f);
        
        var inputImg = inputGo.GetComponent<Image>();
        ApplyUISprite(inputImg);
        inputImg.color = InputFieldBg;

        if (_maxPicsInput.textComponent != null)
        {
            _maxPicsInput.textComponent.font = _font;
            _maxPicsInput.textComponent.fontSize = BASE_FONT_SIZE;
            _maxPicsInput.textComponent.color = TextDark;
            _maxPicsInput.textComponent.alignment = TextAlignmentOptions.Center;
            
            // Ensure text area has proper margins so caret isn't clipped
            var textMargin = _maxPicsInput.textComponent.margin;
            _maxPicsInput.textComponent.margin = new Vector4(5, textMargin.y, 5, textMargin.w);
        }

        if (_maxPicsInput.placeholder is TextMeshProUGUI ph)
        {
            ph.font = _font;
            ph.fontSize = BASE_FONT_SIZE;
            ph.text = "0";
        }
        
        // Fix the Text Area child to have proper sizing
        var textArea = inputGo.transform.Find("Text Area");
        if (textArea != null)
        {
            var textAreaRt = textArea.GetComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(5, 2);
            textAreaRt.offsetMax = new Vector2(-5, -2);
        }

        // Label after - manual positioning
        var labelAfter = new GameObject("LabelAfter");
        labelAfter.transform.SetParent(row.transform, false);
        var labelAfterRt = labelAfter.AddComponent<RectTransform>();
        labelAfterRt.anchorMin = new Vector2(0, 0);
        labelAfterRt.anchorMax = new Vector2(1, 1);
        labelAfterRt.offsetMin = new Vector2(275, 0); // Adjusted for larger input
        labelAfterRt.offsetMax = new Vector2(0, 0);
        
        var labelAfterTMP = labelAfter.AddComponent<TextMeshProUGUI>();
        labelAfterTMP.font = _font;
        labelAfterTMP.fontSize = BASE_FONT_SIZE;
        labelAfterTMP.color = TextDark;
        labelAfterTMP.text = "pics. (0 for unlimited)";
        labelAfterTMP.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private Toggle CreateToggleRow(Transform parent, string labelText, string descriptionText, UnityEngine.Events.UnityAction<bool> callback, float height = 24)
    {
        bool hasDescription = !string.IsNullOrEmpty(descriptionText);
        
        // Outer container - uses manual positioning, not layout group
        var row = new GameObject("ToggleRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        var rowLayout = row.AddComponent<LayoutElement>();
        
        // Use provided height
        rowLayout.minHeight = height;
        rowLayout.preferredHeight = height;

        // Toggle checkbox - positioned manually on the left
        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0, 1);
        toggleRt.anchorMax = new Vector2(0, 1);
        toggleRt.pivot = new Vector2(0, 1);
        toggleRt.sizeDelta = new Vector2(20, 20);
        toggleRt.anchoredPosition = new Vector2(0, 0);

        // Background
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(toggleGo.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bgGo.AddComponent<Image>();
        ApplyUISprite(bgImg);
        bgImg.color = InputFieldBg;

        // Checkmark
        var checkGo = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(2, 2);
        checkRt.offsetMax = new Vector2(-2, -2);
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.fontSize = 14;
        checkTmp.color = CheckmarkColor;
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.text = "✓";

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkTmp;
        toggle.isOn = false;
        toggle.onValueChanged.AddListener(callback);

        // Label - positioned to the right of the toggle, at the top
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        
        if (hasDescription)
        {
            // Label at top, description below
            labelRt.anchorMin = new Vector2(0, 1);
            labelRt.anchorMax = new Vector2(1, 1);
            labelRt.pivot = new Vector2(0, 1);
            labelRt.sizeDelta = new Vector2(0, 20);
            labelRt.anchoredPosition = new Vector2(28, 0);
            labelRt.offsetMin = new Vector2(28, -20);
            labelRt.offsetMax = new Vector2(-5, 0);
        }
        else
        {
            // Label fills entire row, vertically centered
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 1);
            labelRt.offsetMin = new Vector2(28, 0);
            labelRt.offsetMax = new Vector2(-5, 0);
        }
        
        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.font = _font;
        labelTmp.fontSize = BASE_FONT_SIZE;
        labelTmp.color = TextDark;
        labelTmp.alignment = hasDescription ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        labelTmp.text = labelText;
        labelTmp.textWrappingMode = TextWrappingModes.Normal;
        labelTmp.overflowMode = TextOverflowModes.Ellipsis;

        // Description (if provided) - positioned below the label
        if (hasDescription)
        {
            var descObj = new GameObject("Description");
            descObj.transform.SetParent(row.transform, false);
            var descRt = descObj.AddComponent<RectTransform>();
            // Anchor to fill area below the label (top 20px)
            descRt.anchorMin = new Vector2(0, 0);
            descRt.anchorMax = new Vector2(1, 1);
            descRt.offsetMin = new Vector2(28, 0);
            descRt.offsetMax = new Vector2(-5, -22); // Leave 22px for label at top
            
            var descTmp = descObj.AddComponent<TextMeshProUGUI>();
            descTmp.font = _font;
            descTmp.fontSize = BASE_FONT_SIZE - 1;
            descTmp.color = new Color(TextDark.r, TextDark.g, TextDark.b, 0.7f);
            descTmp.alignment = TextAlignmentOptions.TopLeft;
            descTmp.text = descriptionText;
            descTmp.textWrappingMode = TextWrappingModes.Normal;
            descTmp.overflowMode = TextOverflowModes.Ellipsis;
        }

        return toggle;
    }

    private void CreateSectionHeader(Transform parent, string text)
    {
        // Add some spacing before section headers
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.minHeight = 8;
        spacerLayout.preferredHeight = 8;
        
        var header = new GameObject("SectionHeader");
        header.transform.SetParent(parent, false);
        var headerLayout = header.AddComponent<LayoutElement>();
        headerLayout.minHeight = 24;
        headerLayout.preferredHeight = 24;

        var headerTmp = header.AddComponent<TextMeshProUGUI>();
        headerTmp.font = _font;
        headerTmp.fontSize = BASE_FONT_SIZE;
        headerTmp.fontStyle = FontStyles.Bold;
        headerTmp.color = TextDark;
        headerTmp.alignment = TextAlignmentOptions.Center;
        headerTmp.text = text;
    }

    private void CreateStatusTextRow(Transform parent)
    {
        var row = new GameObject("StatusRow");
        row.transform.SetParent(parent, false);
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 60;
        rowLayout.preferredHeight = 80;
        rowLayout.flexibleHeight = 1;

        _statusText = row.AddComponent<TextMeshProUGUI>();
        _statusText.font = _font;
        _statusText.fontSize = BASE_FONT_SIZE;
        _statusText.color = TextDark;
        _statusText.alignment = TextAlignmentOptions.TopLeft;
        _statusText.text = "Not generating";
        _statusText.textWrappingMode = TextWrappingModes.Normal;
    }

    private void CreateAutoPicDropdownRow(Transform parent)
    {
        const float rowHeight = 32f;
        const float labelWidth = 180f;
        const float pad = 12f;

        var row = new GameObject("AutoPicRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = rowHeight;
        rowLayout.preferredHeight = rowHeight;

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.sizeDelta = new Vector2(labelWidth, 0);
        labelRt.anchoredPosition = new Vector2(0, 0);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = "Default AutoPic script:";
        label.fontSize = BASE_FONT_SIZE;
        label.color = TextDark;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Dropdown using TMP_DefaultControls
        var ddGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        ddGo.name = "AutoPicDropdown";
        ddGo.transform.SetParent(row.transform, false);

        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 0);
        ddRt.anchorMax = new Vector2(1, 1);
        ddRt.offsetMin = new Vector2(labelWidth + pad, 4f);
        ddRt.offsetMax = new Vector2(-pad, -4f);

        _autoPicDropdown = ddGo.GetComponent<TMP_Dropdown>();
        
        // Populate dropdown with AutoPic*.txt files from Presets folder
        PopulateAutoPicDropdown();

        _autoPicDropdown.onValueChanged.AddListener(OnAutoPicDropdownChanged);

        // Style the dropdown
        var ddImg = ddGo.GetComponent<Image>();
        if (ddImg != null)
        {
            ApplyUISprite(ddImg);
            ddImg.color = InputFieldBg;
        }

        // Apply arrow sprite if we have one cached
        var arrowTransform = ddGo.transform.Find("Arrow");
        if (arrowTransform != null)
        {
            var arrowImg = arrowTransform.GetComponent<Image>();
            if (arrowImg != null)
            {
                if (_dropdownArrowSprite != null)
                    arrowImg.sprite = _dropdownArrowSprite;
                arrowImg.color = _dropdownArrowColor ?? TextDark;
            }
        }

        if (_autoPicDropdown.captionText != null)
        {
            _autoPicDropdown.captionText.font = _font;
            _autoPicDropdown.captionText.fontSize = BASE_FONT_SIZE;
            _autoPicDropdown.captionText.color = TextDark;
        }

        if (_autoPicDropdown.itemText != null)
        {
            _autoPicDropdown.itemText.font = _font;
            _autoPicDropdown.itemText.fontSize = BASE_FONT_SIZE;
            _autoPicDropdown.itemText.color = TextDark;
        }

        // Make the dropdown list tall enough and style the scrollbar for better visibility
        if (_autoPicDropdown.template != null)
        {
            _autoPicDropdown.template.sizeDelta = new Vector2(_autoPicDropdown.template.sizeDelta.x, 150f);
            
            // Style the dropdown scrollbar for better contrast
            var scrollbar = _autoPicDropdown.template.GetComponentInChildren<Scrollbar>(true);
            if (scrollbar != null)
            {
                // Style scrollbar background (light gray track)
                var scrollbarImg = scrollbar.GetComponent<Image>();
                if (scrollbarImg != null)
                {
                    scrollbarImg.color = new Color(0.75f, 0.75f, 0.77f, 1f);
                }
                
                // Style scrollbar handle (darker for contrast against light track)
                if (scrollbar.handleRect != null)
                {
                    var handleImg = scrollbar.handleRect.GetComponent<Image>();
                    if (handleImg != null)
                    {
                        handleImg.color = new Color(0.45f, 0.45f, 0.5f, 1f);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Static method to get list of AutoPic file names from Presets folder.
    /// Files starting with "AutoPic" or "test_AutoPic" (case-insensitive) are included.
    /// Used by both GenerateSettingsPanel and ServerSettingsPanel.
    /// </summary>
    public static List<string> GetAutoPicFileNames()
    {
        var fileNames = new List<string>();

        // Look for files matching AutoPic*.txt or test_AutoPic*.txt patterns (case-insensitive) in Presets folder
        if (Directory.Exists("Presets"))
        {
            string[] files = Directory.GetFiles("Presets", "*.txt");
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                // Case-insensitive match for AutoPic*.txt or test_AutoPic*.txt
                if (fileName.StartsWith("AutoPic", System.StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("test_AutoPic", System.StringComparison.OrdinalIgnoreCase))
                {
                    fileNames.Add(fileName);
                }
            }
        }

        // Sort alphabetically for consistent ordering (case-insensitive)
        fileNames.Sort((a, b) => string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase));

        return fileNames;
    }

    private void PopulateAutoPicDropdown()
    {
        if (_autoPicDropdown == null) return;

        _autoPicDropdown.ClearOptions();
        var fileNames = GetAutoPicFileNames();

        // Convert to dropdown options
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (string fileName in fileNames)
        {
            options.Add(new TMP_Dropdown.OptionData(fileName));
        }

        // If no AutoPic files found, add a default entry
        if (options.Count == 0)
        {
            options.Add(new TMP_Dropdown.OptionData("AutoPic.txt"));
        }

        _autoPicDropdown.AddOptions(options);
    }

    private void RefreshFromSettings()
    {
        if (GameLogic.Get() == null) return;

        _randomizeToggle.isOn = GameLogic.Get().GetRandomizePrompt();
        _cameraFollowToggle.isOn = GameLogic.Get().GetCameraFollow();
        _autoSaveToggle.isOn = GameLogic.Get().GetAutoSave();
        _autoSavePNGToggle.isOn = GameLogic.Get().GetAutoSavePNG();
        _maxPicsInput.text = GameLogic.Get().GetMaxToGenerate().ToString();
        
        // Strip think tags defaults to true
        _stripThinkTagsToggle.isOn = true;

        // Set AutoPic dropdown from UserPreferences
        RefreshAutoPicDropdown();
    }

    private void RefreshAutoPicDropdown()
    {
        if (_autoPicDropdown == null) return;
        
        // Always repopulate the dropdown to ensure options are fresh
        PopulateAutoPicDropdown();
        
        if (_autoPicDropdown.options.Count == 0) return;

        string savedScript = "AutoPic.txt"; // Default
        var prefs = UserPreferences.Get();
        if (prefs != null && !string.IsNullOrEmpty(prefs.DefaultAutoPicScript))
        {
            savedScript = prefs.DefaultAutoPicScript;
        }

        // Find the saved script in the dropdown options
        // Use SetValueWithoutNotify to avoid triggering the save callback
        bool found = false;
        for (int i = 0; i < _autoPicDropdown.options.Count; i++)
        {
            if (string.Equals(_autoPicDropdown.options[i].text, savedScript, System.StringComparison.OrdinalIgnoreCase))
            {
                _autoPicDropdown.SetValueWithoutNotify(i);
                found = true;
                break;
            }
        }

        // If not found, default to AutoPic.txt or first available
        // Still use SetValueWithoutNotify to avoid overwriting the user's preference
        if (!found)
        {
            for (int i = 0; i < _autoPicDropdown.options.Count; i++)
            {
                if (string.Equals(_autoPicDropdown.options[i].text, "AutoPic.txt", System.StringComparison.OrdinalIgnoreCase))
                {
                    _autoPicDropdown.SetValueWithoutNotify(i);
                    found = true;
                    break;
                }
            }
            // If still not found, just use index 0
            if (!found)
            {
                _autoPicDropdown.SetValueWithoutNotify(0);
            }
        }
        
        // Force the dropdown to update its displayed text
        _autoPicDropdown.RefreshShownValue();
    }

    private IEnumerator RestyleNextFrame()
    {
        yield return null;
        // Force layout update
        Canvas.ForceUpdateCanvases();
        
        // Force TMP input field to reinitialize caret/selection
        ForceReinitializeTMPInputFields();
        
        yield return null;
        ForceReinitializeTMPInputFields();
    }
    
    private void ForceReinitializeTMPInputFields()
    {
        if (_maxPicsInput == null) return;
        
        // Toggle the input field to force TMP to rebuild caret/selection internals
        bool wasEnabled = _maxPicsInput.enabled;
        _maxPicsInput.enabled = false;
        _maxPicsInput.enabled = wasEnabled;
        
        // Reapply visual settings
        _maxPicsInput.caretWidth = 2;
        _maxPicsInput.customCaretColor = true;
        _maxPicsInput.caretColor = TextDark;
        _maxPicsInput.selectionColor = new Color(0.25f, 0.5f, 1f, 0.4f);
        
        if (_maxPicsInput.textComponent != null)
        {
            _maxPicsInput.textComponent.color = TextDark;
        }
        
        _maxPicsInput.ForceLabelUpdate();
    }

    // Callbacks
    private void OnRandomizeToggleChanged(bool value)
    {
        GameLogic.Get()?.SetRandomizePrompt(value);
    }

    private void OnCameraFollowToggleChanged(bool value)
    {
        GameLogic.Get()?.SetCameraFollow(value);
    }

    private void OnAutoSaveToggleChanged(bool value)
    {
        GameLogic.Get()?.SetAutoSave(value);
    }

    private void OnAutoSavePNGToggleChanged(bool value)
    {
        GameLogic.Get()?.SetAutoSavePNG(value);
    }

    private void OnStripThinkTagsChanged(bool value)
    {
        // This is read directly from the toggle by other code
    }

    private void OnAutoPicDropdownChanged(int index)
    {
        if (_autoPicDropdown == null || index < 0 || index >= _autoPicDropdown.options.Count) return;

        string selectedScript = _autoPicDropdown.options[index].text;
        var prefs = UserPreferences.Get();
        if (prefs != null)
        {
            prefs.DefaultAutoPicScript = selectedScript;
            prefs.Save();
        }
    }

    private void OnMaxPicsChanged(string value)
    {
        int result = 0;
        int.TryParse(value, out result);
        GameLogic.Get()?.SetMaxToGenerate(result);
    }

    private void OnCopyToClipboard()
    {
        if (_statusText != null)
        {
            GUIUtility.systemCopyBuffer = _statusText.text;
            RTConsole.Log("Copied status to clipboard");
        }
    }

    private void Update()
    {
        // Update status text
        if (_statusText != null && ImageGenerator.Get() != null && GameLogic.Get() != null)
    {
        string text;

        if (ImageGenerator.Get().IsGenerating())
        {
            string maxToGen;
            if (GameLogic.Get().GetMaxToGenerate() > 0)
            {
                maxToGen = GameLogic.Get().GetMaxToGenerate().ToString();
                }
                else
            {
                maxToGen = "unlimited";
            }
            
                text = "Generating " + ImageGenerator.Get().GetCurrentGenerationCount() + " of " + maxToGen + ":\n";

            if (GameLogic.Get().GetLastModifiedPrompt() != "")
            {
                text += "\nLast modified prompt was:\n" + GameLogic.Get().GetLastModifiedPrompt();
            }
        }
        else
        {
            text = "Not generating";
        }

            _statusText.text = text;
        }

        // Close on Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Hide();
        }
    }
}

/// <summary>
/// Simple drag handler for the panel header.
/// </summary>
public class GenerateSettingsDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    private RectTransform _target;
    private Vector2 _dragOffset;

    public void SetTarget(RectTransform target) => _target = target;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_target == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint);
        _dragOffset = _target.anchoredPosition - localPoint;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint);
        _target.anchoredPosition = localPoint + _dragOffset;
    }
}
