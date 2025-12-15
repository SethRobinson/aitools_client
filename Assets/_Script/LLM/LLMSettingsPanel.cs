using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Programmatic LLM settings window. Uses TMP_DefaultControls for stable dropdown/input behavior.
/// </summary>
public class LLMSettingsPanel : MonoBehaviour
{
    private static LLMSettingsPanel _instance;
    private static GameObject _panelRoot;

    private TMP_FontAsset _font;
    private LLMSettings _workingSettings;

    private RectTransform _mainPanel;
    private TMP_Dropdown _activeProviderDropdown;

    private LLMProviderUI _openAIUI;
    private LLMProviderUI _anthropicUI;
    private LLMProviderUI _llamaCppUI;
    private LLMProviderUI _ollamaUI;

    private const float PANEL_WIDTH = 640f;
    private const float PANEL_HEIGHT = 520f;
    private const float HEADER_HEIGHT = 32f;
    private const float FOOTER_HEIGHT = 54f;

    // Theme (rough match for existing UI)
    private static readonly Color PanelBg = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color HeaderBg = new Color(0.35f, 0.42f, 0.50f, 1f);
    private static readonly Color FooterBg = new Color(0.24f, 0.24f, 0.24f, 1f);
    private static readonly Color RowBg = new Color(0.28f, 0.32f, 0.38f, 1f);
    private static readonly Color ButtonPrimary = new Color(0.30f, 0.40f, 0.50f, 1f);
    private static readonly Color ButtonSecondary = new Color(0.35f, 0.35f, 0.35f, 1f);

    public static void Show()
    {
        if (_instance != null)
        {
            _panelRoot.SetActive(true);
            _instance.RefreshFromSettings();
            return;
        }

        _panelRoot = new GameObject("LLMSettingsPanel");
        _instance = _panelRoot.AddComponent<LLMSettingsPanel>();
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
        _instance = null;
        _panelRoot = null;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private static TMP_DefaultControls.Resources BuildTMPResources()
    {
        // Unity 6 no longer guarantees the legacy \"UI/Skin/*.psd\" built-in paths exist at runtime.
        // TMP_DefaultControls works fine with null sprites; we restyle the Images ourselves after creation.
        return new TMP_DefaultControls.Resources();
    }

    private void ApplyFontAndColor(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font = _font;
            t.fontSize = Mathf.Max(12, t.fontSize);
            t.color = Color.white;
        }

        // Restyle default control Images so nothing is white-on-white.
        // TMP_DefaultControls creates white Image components; we force a dark theme here.
        foreach (var img in go.GetComponentsInChildren<Image>(true))
        {
            string n = img.gameObject.name.ToLowerInvariant();

            // Selection/check visuals
            if (n.Contains("checkmark"))
            {
                img.color = new Color(0.50f, 0.75f, 1.00f, 1f);
                continue;
            }

            if (n.Contains("handle"))
            {
                img.color = new Color(0.45f, 0.45f, 0.50f, 1f);
                continue;
            }

            // Arrow icon (if present as Image)
            if (n.Contains("arrow"))
            {
                img.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                continue;
            }

            // Everything else becomes a dark surface.
            // (This covers InputField/Dropdown root backgrounds, template backgrounds, item backgrounds, etc.)
            img.color = new Color(0.15f, 0.15f, 0.17f, 1f);
        }

        // InputField placeholder text is often dark by default.
        foreach (var input in go.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input.textComponent != null)
            {
                input.textComponent.font = _font;
                input.textComponent.fontSize = 12;
                input.textComponent.color = Color.white;
            }

            if (input.placeholder is TextMeshProUGUI ph)
            {
                ph.font = _font;
                ph.fontSize = 12;
                ph.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        // Dropdown item labels are often dark by default.
        foreach (var dd in go.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dd.captionText != null)
            {
                dd.captionText.font = _font;
                dd.captionText.fontSize = 12;
                dd.captionText.color = Color.white;
            }
            if (dd.itemText != null)
            {
                dd.itemText.font = _font;
                dd.itemText.fontSize = 12;
                dd.itemText.color = Color.white;
            }
        }
    }

    private void CreateUI()
    {
        _font = FindFont();
        _workingSettings = LLMSettingsManager.Get().GetSettingsClone();

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
        main.AddComponent<Image>().color = PanelBg;

        CreateHeader();
        RectTransform contentRoot = CreateContentRoot();
        CreateFooter();

        BuildContent(contentRoot);

        RefreshFromSettings();
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

        header.AddComponent<PanelDragHandler>().SetTarget(_mainPanel);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-36, 0);

        var title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "LLM Settings";
        title.font = _font;
        title.fontSize = 15;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;

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
        xTxt.fontSize = 13;
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
        footer.AddComponent<Image>().color = FooterBg;

        CreateFooterButton(footer.transform, "Save", ButtonPrimary, -70f, OnSaveClicked);
        CreateFooterButton(footer.transform, "Cancel", ButtonSecondary, 70f, Hide);
    }

    private void CreateFooterButton(Transform parent, string text, Color bg, float xOffset, UnityEngine.Events.UnityAction onClick)
    {
        var btn = new GameObject("Btn_" + text);
        btn.transform.SetParent(parent, false);
        var rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0);
        rt.sizeDelta = new Vector2(110, 32);

        var img = btn.AddComponent<Image>();
        img.color = bg;
        var button = btn.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

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
        tmp.fontSize = 13;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
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
        vpRt.offsetMax = new Vector2(-22, 0);
        var vpImg = viewport.AddComponent<Image>();
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
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.spacing = 14;
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
        scrollRect.verticalScrollbar = scrollbar;

        // Row: Active provider (explicit layout)
        CreateActiveProviderRow(content.transform);

        // Provider panels
        _openAIUI = new LLMProviderUI(LLMProvider.OpenAI, _font, BuildTMPResources(), ApplyFontAndColor);
        _openAIUI.Build(content.transform, _workingSettings.openAI, false);

        _anthropicUI = new LLMProviderUI(LLMProvider.Anthropic, _font, BuildTMPResources(), ApplyFontAndColor);
        _anthropicUI.Build(content.transform, _workingSettings.anthropic, false);

        _llamaCppUI = new LLMProviderUI(LLMProvider.LlamaCpp, _font, BuildTMPResources(), ApplyFontAndColor);
        _llamaCppUI.Build(content.transform, _workingSettings.llamaCpp, false);

        _ollamaUI = new LLMProviderUI(LLMProvider.Ollama, _font, BuildTMPResources(), ApplyFontAndColor);
        _ollamaUI.Build(content.transform, _workingSettings.ollama, true);
        if (_ollamaUI.refreshModelsButton != null)
            _ollamaUI.refreshModelsButton.onClick.AddListener(OnRefreshOllamaModels);

        UpdateVisibleProvider();
    }

    private void CreateActiveProviderRow(Transform parent)
    {
        const float rowHeight = 32f;
        const float labelWidth = 140f;
        const float pad = 12f;

        var row = new GameObject("ActiveProviderRow");
        row.transform.SetParent(parent, false);
        row.AddComponent<Image>().color = RowBg;
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = rowHeight;

        var rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0, rowHeight);

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.sizeDelta = new Vector2(labelWidth, 0);
        labelRt.anchoredPosition = new Vector2(pad, 0);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = "Active Provider:";
        label.fontSize = 13;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Dropdown (TMP_DefaultControls)
        var ddGo = TMP_DefaultControls.CreateDropdown(BuildTMPResources());
        ddGo.name = "ActiveProviderDropdown";
        ddGo.transform.SetParent(row.transform, false);
        ApplyFontAndColor(ddGo);

        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 0);
        ddRt.anchorMax = new Vector2(1, 1);
        ddRt.offsetMin = new Vector2(labelWidth + pad * 2f, 5f);
        ddRt.offsetMax = new Vector2(-pad, -5f);

        _activeProviderDropdown = ddGo.GetComponent<TMP_Dropdown>();
        _activeProviderDropdown.options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("OpenAI"),
            new TMP_Dropdown.OptionData("Anthropic"),
            new TMP_Dropdown.OptionData("llama.cpp"),
            new TMP_Dropdown.OptionData("Ollama"),
        };
        _activeProviderDropdown.onValueChanged.AddListener(OnProviderChanged);

        // Make the dropdown list tall enough and not absurd.
        var template = _activeProviderDropdown.template;
        if (template != null)
        {
            template.sizeDelta = new Vector2(template.sizeDelta.x, 160f);
        }

        // Ensure caption looks right
        if (_activeProviderDropdown.captionText != null)
        {
            _activeProviderDropdown.captionText.font = _font;
            _activeProviderDropdown.captionText.fontSize = 12;
            _activeProviderDropdown.captionText.color = Color.white;
        }

        // Ensure item text uses our font
        if (_activeProviderDropdown.itemText != null)
        {
            _activeProviderDropdown.itemText.font = _font;
            _activeProviderDropdown.itemText.fontSize = 12;
            _activeProviderDropdown.itemText.color = Color.white;
        }
    }

    private void OnProviderChanged(int index)
    {
        _workingSettings.activeProvider = (LLMProvider)index;
        UpdateVisibleProvider();
    }

    private void UpdateVisibleProvider()
    {
        var active = _workingSettings.activeProvider;
        if (_openAIUI?.sectionRoot != null) _openAIUI.sectionRoot.SetActive(active == LLMProvider.OpenAI);
        if (_anthropicUI?.sectionRoot != null) _anthropicUI.sectionRoot.SetActive(active == LLMProvider.Anthropic);
        if (_llamaCppUI?.sectionRoot != null) _llamaCppUI.sectionRoot.SetActive(active == LLMProvider.LlamaCpp);
        if (_ollamaUI?.sectionRoot != null) _ollamaUI.sectionRoot.SetActive(active == LLMProvider.Ollama);
    }

    private void OnRefreshOllamaModels()
    {
        string endpoint = _ollamaUI.endpointInput != null ? _ollamaUI.endpointInput.text : _workingSettings.ollama.endpoint;
        RTQuickMessageManager.Get().ShowMessage("Fetching Ollama models...");

        OllamaModelFetcher.Fetch(endpoint, (models, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                RTQuickMessageManager.Get().ShowMessage("Error: " + error);
                return;
            }

            if (models == null || models.Count == 0)
            {
                RTQuickMessageManager.Get().ShowMessage("No models found");
                return;
            }

            _workingSettings.ollama.availableModels = models;
            _ollamaUI.UpdateModelDropdown(models, _workingSettings.ollama.selectedModel);
            RTQuickMessageManager.Get().ShowMessage("Found " + models.Count + " models");
        });
    }

    private void OnSaveClicked()
    {
        _openAIUI.ApplyToSettings(_workingSettings.openAI);
        _anthropicUI.ApplyToSettings(_workingSettings.anthropic);
        _llamaCppUI.ApplyToSettings(_workingSettings.llamaCpp);
        _ollamaUI.ApplyToSettings(_workingSettings.ollama);

        LLMSettingsManager.Get().ApplySettings(_workingSettings);
        RTQuickMessageManager.Get().ShowMessage("LLM settings saved");
        Hide();
    }

    private void RefreshFromSettings()
    {
        _workingSettings = LLMSettingsManager.Get().GetSettingsClone();

        if (_activeProviderDropdown != null)
            _activeProviderDropdown.value = (int)_workingSettings.activeProvider;

        _openAIUI?.UpdateFromSettings(_workingSettings.openAI);
        _anthropicUI?.UpdateFromSettings(_workingSettings.anthropic);
        _llamaCppUI?.UpdateFromSettings(_workingSettings.llamaCpp);
        _ollamaUI?.UpdateFromSettings(_workingSettings.ollama);

        UpdateVisibleProvider();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Hide();
    }
}

public class PanelDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
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