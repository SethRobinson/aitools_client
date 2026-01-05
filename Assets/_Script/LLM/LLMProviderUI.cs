using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the provider-specific settings UI. Uses TMP_DefaultControls for stable controls.
/// </summary>
public class LLMProviderUI
{
    public GameObject sectionRoot;

    public TMP_InputField apiKeyInput;
    public TMP_InputField endpointInput;
    public TMP_Dropdown modelDropdown;
    public Button refreshModelsButton;
    
    // Ollama-specific controls
    public Toggle overrideContextToggle;
    public Slider contextSlider;
    public TextMeshProUGUI contextValueLabel;
    public TextMeshProUGUI modelInfoLabel;
    private GameObject _contextSliderRow; // Reference to hide/show based on toggle
    
    // Cached model info for VRAM estimation
    private OllamaModelInfo _cachedModelInfo;
    private int _currentContextValue = 8192;
    private bool _overrideContext = false;
    
    // llama.cpp-specific controls
    public Toggle thinkingModeToggle;
    public TextMeshProUGUI serverModeLabel;
    private GameObject _thinkingModeRow; // Reference to hide/show based on model
    private bool _enableThinking = true;
    private bool _isRouterMode = false;
    
    // Callback for when model selection changes (for Ollama to fetch model info)
    public event Action<string> OnModelChanged;

    private readonly LLMProvider _provider;
    private readonly TMP_FontAsset _font;
    private readonly TMP_DefaultControls.Resources _tmpResources;
    private readonly Action<GameObject> _styleApplier;

    // Theme pulled from existing UI: white surfaces + dark text
    // (Panel/backplate sprite is applied by LLMSettingsPanel.ApplyFontAndColor via TMP_DefaultControls styling.)
    private static readonly Color SectionBg = new Color(1f, 1f, 1f, 0f); // let parent panel show through
    private static readonly Color HeaderColor = new Color(0f, 0.45f, 0.70f, 1f); // keep the blue section heading
    private static readonly Color LabelColor = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
    // Match the LLMSettingsPanel control surface tint so inputs/dropdowns remain distinct
    // from the darker panel background.
    private static readonly Color ButtonBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color InputBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);

    private const float RowHeight = 40f;
    private const float LabelWidth = 110f;

    public LLMProviderUI(LLMProvider provider, TMP_FontAsset font, TMP_DefaultControls.Resources tmpResources, Action<GameObject> styleApplier)
    {
        _provider = provider;
        _font = font;
        _tmpResources = tmpResources;
        _styleApplier = styleApplier;
    }

    public GameObject Build(Transform parent, LLMProviderSettings settings, bool showRefreshButton)
    {
        sectionRoot = new GameObject(_provider + "Section");
        sectionRoot.transform.SetParent(parent, false);
        // Keep a transparent image so layout/background remains consistent if needed, but don't tint the panel.
        sectionRoot.AddComponent<Image>().color = SectionBg;

        var vlg = sectionRoot.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 14, 16);
        vlg.spacing = 12;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = sectionRoot.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateHeader(sectionRoot.transform);

        if (_provider == LLMProvider.OpenAI || _provider == LLMProvider.Anthropic || _provider == LLMProvider.Gemini)
            apiKeyInput = CreateInputRow(sectionRoot.transform, "API Key:", settings.apiKey, "Enter API key...", true);

        endpointInput = CreateInputRow(sectionRoot.transform, "Endpoint:", settings.endpoint, "http://localhost:8080", false);

        CreateModelRow(sectionRoot.transform, settings, showRefreshButton);

        // Add Ollama-specific context length controls
        if (_provider == LLMProvider.Ollama)
        {
            _overrideContext = settings.overrideContextLength;
            _currentContextValue = settings.contextLength;
            CreateOverrideContextRow(sectionRoot.transform, settings);
            CreateContextSliderRow(sectionRoot.transform, settings);
            CreateModelInfoRow(sectionRoot.transform);
            UpdateContextControlsVisibility();
        }

        // Add llama.cpp-specific controls
        if (_provider == LLMProvider.LlamaCpp)
        {
            _enableThinking = settings.enableThinking;
            _isRouterMode = settings.isRouterMode;
            CreateServerModeRow(sectionRoot.transform, settings);
            CreateThinkingModeRow(sectionRoot.transform, settings);
            UpdateThinkingModeVisibility();
        }

        // Add Gemini-specific controls (thinking mode)
        if (_provider == LLMProvider.Gemini)
        {
            _enableThinking = settings.enableThinking;
            CreateGeminiThinkingModeRow(sectionRoot.transform, settings);
        }

        return sectionRoot;
    }

    private void CreateHeader(Transform parent)
    {
        var header = new GameObject("Header");
        header.transform.SetParent(parent, false);
        var le = header.AddComponent<LayoutElement>();
        le.preferredHeight = 24;

        var tmp = header.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.fontSize = 14;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = HeaderColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text = GetProviderDisplayName() + " Settings";
        tmp.overflowMode = TextOverflowModes.Overflow;
    }

    private TMP_InputField CreateInputRow(Transform parent, string labelText, string value, string placeholder, bool password)
    {
        var row = CreateRowContainer(parent, labelText);

        CreateRowLabel(row.transform, labelText);

        var inputGo = TMP_DefaultControls.CreateInputField(_tmpResources);
        inputGo.name = "Input_" + labelText;
        inputGo.transform.SetParent(row.transform, false);
        _styleApplier?.Invoke(inputGo);
        // Ensure the root background matches light theme
        var inputRootImg = inputGo.GetComponent<Image>();
        if (inputRootImg != null) inputRootImg.color = InputBg;

        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(1, 1);
        inputRt.offsetMin = new Vector2(LabelWidth + 16, 5);
        inputRt.offsetMax = new Vector2(-6, -5);

        var input = inputGo.GetComponent<TMP_InputField>();
        if (input != null)
        {
            input.text = value ?? string.Empty;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            if (input.textComponent != null)
            {
                input.textComponent.font = _font;
                input.textComponent.fontSize = 14;
                input.textComponent.color = TextDark;
            }
            if (input.placeholder is TextMeshProUGUI ph)
            {
                ph.font = _font;
                ph.fontSize = 14;
                ph.color = new Color(0.45f, 0.45f, 0.50f, 1f);
            }
        }

        return input;
    }

    private void CreateModelRow(Transform parent, LLMProviderSettings settings, bool showRefreshButton)
    {
        var row = CreateRowContainer(parent, "Model");
        CreateRowLabel(row.transform, "Model:");

        // Dropdown
        var ddGo = TMP_DefaultControls.CreateDropdown(_tmpResources);
        ddGo.name = "ModelDropdown";
        ddGo.transform.SetParent(row.transform, false);
        _styleApplier?.Invoke(ddGo);
        // Ensure the root background matches light theme
        var ddRootImg = ddGo.GetComponent<Image>();
        if (ddRootImg != null) ddRootImg.color = InputBg;

        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 0);
        ddRt.anchorMax = showRefreshButton ? new Vector2(1, 1) : new Vector2(1, 1);

        float rightPad = showRefreshButton ? 86f : 6f;
        ddRt.offsetMin = new Vector2(LabelWidth + 16, 5);
        ddRt.offsetMax = new Vector2(-rightPad, -5);

        modelDropdown = ddGo.GetComponent<TMP_Dropdown>();
        UpdateModelDropdown(settings.availableModels, settings.selectedModel);

        if (modelDropdown != null)
        {
            if (modelDropdown.captionText != null)
            {
                modelDropdown.captionText.font = _font;
                modelDropdown.captionText.fontSize = 14;
                modelDropdown.captionText.color = TextDark;
            }
            if (modelDropdown.itemText != null)
            {
                modelDropdown.itemText.font = _font;
                modelDropdown.itemText.fontSize = 14;
                modelDropdown.itemText.color = TextDark;
            }

            if (modelDropdown.template != null)
                modelDropdown.template.sizeDelta = new Vector2(modelDropdown.template.sizeDelta.x, 180f);
            
            // Add change listener
            modelDropdown.onValueChanged.AddListener(OnModelDropdownChanged);
        }

        if (showRefreshButton)
        {
            refreshModelsButton = CreateSmallButton(row.transform, "Refresh", 76f);
            var btnRt = refreshModelsButton.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1, 0.5f);
            btnRt.anchorMax = new Vector2(1, 0.5f);
            btnRt.pivot = new Vector2(1, 0.5f);
            btnRt.sizeDelta = new Vector2(76f, 24f);
            btnRt.anchoredPosition = new Vector2(-6f, 0);
        }
    }

    private GameObject CreateRowContainer(Transform parent, string name)
    {
        var row = new GameObject("Row_" + name);
        row.transform.SetParent(parent, false);

        // Ensure RectTransform exists BEFORE adding layout components.
        var rt = row.GetComponent<RectTransform>();
        if (rt == null) rt = row.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, RowHeight);

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = RowHeight;

        return row;
    }

    private void CreateRowLabel(Transform parent, string text)
    {
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent, false);

        var rt = labelObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(LabelWidth, 0);
        rt.anchoredPosition = new Vector2(0, 0);

        var tmp = labelObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.fontSize = 12;
        tmp.color = LabelColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text = text;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    private Button CreateSmallButton(Transform parent, string text, float width)
    {
        var btn = new GameObject("Button_" + text);
        btn.transform.SetParent(parent, false);

        var rt = btn.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 24);

        var img = btn.AddComponent<Image>();
        img.color = ButtonBg;

        var button = btn.AddComponent<Button>();
        button.targetGraphic = img;

        var txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btn.transform, false);

        var txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.fontSize = 11;
        tmp.color = Color.black;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = text;

        return button;
    }

    public void UpdateFromSettings(LLMProviderSettings settings)
    {
        if (apiKeyInput != null)
            apiKeyInput.text = settings.apiKey;
        if (endpointInput != null)
            endpointInput.text = settings.endpoint;
        if (modelDropdown != null)
            UpdateModelDropdown(settings.availableModels, settings.selectedModel);
        
        // Ollama-specific
        if (_provider == LLMProvider.Ollama)
        {
            _overrideContext = settings.overrideContextLength;
            _currentContextValue = settings.contextLength;
            if (overrideContextToggle != null)
                overrideContextToggle.isOn = settings.overrideContextLength;
            if (contextSlider != null)
            {
                contextSlider.value = ContextToSliderValue(settings.contextLength);
                UpdateContextLabel();
            }
            UpdateContextControlsVisibility();
        }
        
        // llama.cpp-specific
        if (_provider == LLMProvider.LlamaCpp)
        {
            _enableThinking = settings.enableThinking;
            _isRouterMode = settings.isRouterMode;
            if (thinkingModeToggle != null)
                thinkingModeToggle.isOn = settings.enableThinking;
            UpdateServerModeLabel(settings);
            UpdateThinkingModeVisibility();
        }
        
        // Gemini-specific
        if (_provider == LLMProvider.Gemini)
        {
            _enableThinking = settings.enableThinking;
            if (thinkingModeToggle != null)
                thinkingModeToggle.isOn = settings.enableThinking;
        }
    }

    public void ApplyToSettings(LLMProviderSettings settings)
    {
        if (apiKeyInput != null)
            settings.apiKey = apiKeyInput.text;
        if (endpointInput != null)
            settings.endpoint = endpointInput.text;
        if (modelDropdown != null && modelDropdown.options != null && modelDropdown.options.Count > 0)
            settings.selectedModel = modelDropdown.options[modelDropdown.value].text;
        
        // Ollama-specific
        if (_provider == LLMProvider.Ollama)
        {
            settings.overrideContextLength = _overrideContext;
            if (contextSlider != null)
            {
                settings.contextLength = _currentContextValue;
                if (_cachedModelInfo != null)
                    settings.maxContextLength = (int)_cachedModelInfo.contextLength;
            }
        }
        
        // llama.cpp-specific
        if (_provider == LLMProvider.LlamaCpp)
        {
            settings.enableThinking = _enableThinking;
            settings.isRouterMode = _isRouterMode;
        }
        
        // Gemini-specific
        if (_provider == LLMProvider.Gemini)
        {
            settings.enableThinking = _enableThinking;
        }
    }

    public void UpdateModelDropdown(List<string> models, string selectedModel)
    {
        if (modelDropdown == null) return;

        modelDropdown.ClearOptions();
        if (models == null || models.Count == 0)
        {
            modelDropdown.AddOptions(new List<string> { "(no models)" });
            modelDropdown.value = 0;
            
            // Update thinking mode visibility for llama.cpp
            if (_provider == LLMProvider.LlamaCpp)
            {
                UpdateThinkingModeVisibility();
            }
            return;
        }

        modelDropdown.AddOptions(models);
        int idx = !string.IsNullOrEmpty(selectedModel) ? models.IndexOf(selectedModel) : -1;
        modelDropdown.value = idx >= 0 ? idx : 0;
        modelDropdown.RefreshShownValue();
        
        // Update thinking mode visibility for llama.cpp (since setting value programmatically
        // may not trigger the OnValueChanged event)
        if (_provider == LLMProvider.LlamaCpp)
        {
            UpdateThinkingModeVisibility();
        }
    }
    
    private void OnModelDropdownChanged(int index)
    {
        if (modelDropdown == null || modelDropdown.options == null || modelDropdown.options.Count == 0)
            return;
        
        string modelName = modelDropdown.options[index].text;
        if (modelName != "(no models)")
        {
            // Clear cached model info when model changes
            _cachedModelInfo = null;
            UpdateContextLabel();
            UpdateModelInfoDisplay();
            
            // Update thinking mode visibility for llama.cpp (based on model name)
            if (_provider == LLMProvider.LlamaCpp)
            {
                UpdateThinkingModeVisibility();
            }
            
            // Fire the event for the parent to fetch model info
            OnModelChanged?.Invoke(modelName);
        }
    }

    private string GetProviderDisplayName()
    {
        return _provider switch
        {
            LLMProvider.OpenAI => "OpenAI",
            LLMProvider.Anthropic => "Anthropic",
            LLMProvider.LlamaCpp => "llama.cpp",
            LLMProvider.Ollama => "Ollama",
            LLMProvider.Gemini => "Gemini",
            LLMProvider.OpenAICompatible => "OpenAI Compatible",
            _ => _provider.ToString()
        };
    }

    #region Ollama Context Slider

    // Context length steps in tokens (logarithmic-ish scale matching Ollama's UI)
    private static readonly int[] ContextSteps = new int[]
    {
        2048,    // 2k
        4096,    // 4k
        8192,    // 8k
        16384,   // 16k
        32768,   // 32k
        65536,   // 64k
        131072,  // 128k
        262144   // 256k
    };

    private void CreateOverrideContextRow(Transform parent, LLMProviderSettings settings)
    {
        var row = CreateRowContainer(parent, "OverrideContext");
        
        // Checkbox + label
        var toggleContainer = new GameObject("ToggleContainer");
        toggleContainer.transform.SetParent(row.transform, false);
        var containerRt = toggleContainer.AddComponent<RectTransform>();
        containerRt.anchorMin = Vector2.zero;
        containerRt.anchorMax = Vector2.one;
        containerRt.offsetMin = Vector2.zero;
        containerRt.offsetMax = Vector2.zero;
        
        // Create toggle
        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(toggleContainer.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0, 0.5f);
        toggleRt.anchorMax = new Vector2(0, 0.5f);
        toggleRt.pivot = new Vector2(0, 0.5f);
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
        bgImg.color = InputBg;
        
        // Checkmark
        var checkGo = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.1f, 0.1f);
        checkRt.anchorMax = new Vector2(0.9f, 0.9f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.fontSize = 14;
        checkTmp.color = new Color(0.2f, 0.5f, 0.2f, 1f);
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.text = "✓";
        
        overrideContextToggle = toggleGo.AddComponent<Toggle>();
        overrideContextToggle.targetGraphic = bgImg;
        overrideContextToggle.graphic = checkTmp;
        overrideContextToggle.isOn = settings.overrideContextLength;
        overrideContextToggle.onValueChanged.AddListener(OnOverrideContextChanged);
        
        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(toggleContainer.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(28, 0);
        labelRt.offsetMax = new Vector2(0, 0);
        
        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.font = _font;
        labelTmp.fontSize = 12;
        labelTmp.color = LabelColor;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.text = "Override context length";
    }
    
    private void OnOverrideContextChanged(bool value)
    {
        _overrideContext = value;
        UpdateContextControlsVisibility();
    }
    
    private void UpdateContextControlsVisibility()
    {
        if (_contextSliderRow != null)
        {
            _contextSliderRow.SetActive(_overrideContext);
        }
    }

    private void CreateContextSliderRow(Transform parent, LLMProviderSettings settings)
    {
        // Create a taller row to accommodate tick labels above slider
        var row = new GameObject("Row_Context");
        row.transform.SetParent(parent, false);
        _contextSliderRow = row; // Save reference for visibility control
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0, 70f);
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 70f;

        // Use full width for slider (checkbox already explains what this is)
        float sliderLeftOffset = 8;
        float sliderRightPad = 8;
        
        // Tick labels above the slider
        var tickContainer = new GameObject("TickLabels");
        tickContainer.transform.SetParent(row.transform, false);
        var tickRt = tickContainer.AddComponent<RectTransform>();
        tickRt.anchorMin = new Vector2(0, 1);
        tickRt.anchorMax = new Vector2(1, 1);
        tickRt.pivot = new Vector2(0.5f, 1);
        tickRt.offsetMin = new Vector2(sliderLeftOffset, 0);
        tickRt.offsetMax = new Vector2(-sliderRightPad, 0);
        tickRt.sizeDelta = new Vector2(0, 14);
        tickRt.anchoredPosition = new Vector2(0, -2); // Closer to top
        
        // Add tick labels for each context step
        string[] tickLabels = { "2k", "4k", "8k", "16k", "32k", "64k", "128k", "256k" };
        for (int i = 0; i < tickLabels.Length; i++)
        {
            var tickLabelObj = new GameObject("Tick_" + tickLabels[i]);
            tickLabelObj.transform.SetParent(tickContainer.transform, false);
            var tickLabelRt = tickLabelObj.AddComponent<RectTransform>();
            
            float normalizedPos = (float)i / (tickLabels.Length - 1);
            tickLabelRt.anchorMin = new Vector2(normalizedPos, 0);
            tickLabelRt.anchorMax = new Vector2(normalizedPos, 1);
            tickLabelRt.pivot = new Vector2(0.5f, 0.5f);
            tickLabelRt.sizeDelta = new Vector2(40, 16);
            tickLabelRt.anchoredPosition = Vector2.zero;
            
            var tickTmp = tickLabelObj.AddComponent<TextMeshProUGUI>();
            tickTmp.font = _font;
            tickTmp.fontSize = 10;
            tickTmp.color = new Color(0.4f, 0.4f, 0.45f, 1f);
            tickTmp.alignment = TextAlignmentOptions.Center;
            tickTmp.text = tickLabels[i];
        }

        // Slider in the middle
        var sliderContainer = new GameObject("SliderContainer");
        sliderContainer.transform.SetParent(row.transform, false);
        var containerRt = sliderContainer.AddComponent<RectTransform>();
        containerRt.anchorMin = new Vector2(0, 0.5f);
        containerRt.anchorMax = new Vector2(1, 0.5f);
        containerRt.pivot = new Vector2(0.5f, 0.5f);
        containerRt.offsetMin = new Vector2(sliderLeftOffset, 0);
        containerRt.offsetMax = new Vector2(-sliderRightPad, 0);
        containerRt.sizeDelta = new Vector2(0, 12);
        containerRt.anchoredPosition = new Vector2(0, 8); // Shifted up for clearance below

        // Create slider
        var sliderGo = new GameObject("Slider");
        sliderGo.transform.SetParent(sliderContainer.transform, false);
        var sliderRt = sliderGo.AddComponent<RectTransform>();
        sliderRt.anchorMin = Vector2.zero;
        sliderRt.anchorMax = Vector2.one;
        sliderRt.offsetMin = Vector2.zero;
        sliderRt.offsetMax = Vector2.zero;

        // Background track
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(sliderGo.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0, 0.4f);
        bgRt.anchorMax = new Vector2(1, 0.6f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0.6f, 0.6f, 0.65f, 1f);

        // Fill area
        var fillAreaGo = new GameObject("Fill Area");
        fillAreaGo.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillAreaGo.AddComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0, 0.4f);
        fillAreaRt.anchorMax = new Vector2(1, 0.6f);
        fillAreaRt.offsetMin = new Vector2(0, 0);
        fillAreaRt.offsetMax = new Vector2(0, 0);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0, 1);
        fillRt.sizeDelta = Vector2.zero;
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.color = new Color(0.25f, 0.55f, 0.85f, 1f);

        // Handle slide area
        var handleAreaGo = new GameObject("Handle Slide Area");
        handleAreaGo.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = Vector2.zero;
        handleAreaRt.offsetMax = Vector2.zero;

        var handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(handleAreaGo.transform, false);
        var handleRt = handleGo.AddComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(10, 12); // Small handle to avoid overlapping labels
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.color = new Color(0.3f, 0.3f, 0.35f, 1f); // Darker handle for visibility

        // Configure slider
        contextSlider = sliderGo.AddComponent<Slider>();
        contextSlider.fillRect = fillRt;
        contextSlider.handleRect = handleRt;
        contextSlider.minValue = 0;
        contextSlider.maxValue = ContextSteps.Length - 1;
        contextSlider.wholeNumbers = true;
        contextSlider.value = ContextToSliderValue(settings.contextLength);
        contextSlider.onValueChanged.AddListener(OnContextSliderChanged);

        // Value + VRAM label below the slider
        var valueLabelObj = new GameObject("ValueLabel");
        valueLabelObj.transform.SetParent(row.transform, false);
        var valueRt = valueLabelObj.AddComponent<RectTransform>();
        valueRt.anchorMin = new Vector2(0, 0);
        valueRt.anchorMax = new Vector2(1, 0);
        valueRt.pivot = new Vector2(0, 0);
        valueRt.offsetMin = new Vector2(sliderLeftOffset, 2);
        valueRt.offsetMax = new Vector2(-sliderRightPad, 0);
        valueRt.sizeDelta = new Vector2(0, 14);

        contextValueLabel = valueLabelObj.AddComponent<TextMeshProUGUI>();
        contextValueLabel.font = _font;
        contextValueLabel.fontSize = 11;
        contextValueLabel.color = LabelColor;
        contextValueLabel.alignment = TextAlignmentOptions.MidlineLeft;
        
        UpdateContextLabel();
    }

    private void CreateModelInfoRow(Transform parent)
    {
        var row = CreateRowContainer(parent, "ModelInfo");
        row.GetComponent<LayoutElement>().preferredHeight = 50f;

        modelInfoLabel = row.AddComponent<TextMeshProUGUI>();
        modelInfoLabel.font = _font;
        modelInfoLabel.fontSize = 11;
        modelInfoLabel.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        modelInfoLabel.alignment = TextAlignmentOptions.TopLeft;
        modelInfoLabel.textWrappingMode = TextWrappingModes.Normal;
        modelInfoLabel.text = "Select a model and click Refresh to see model details.";
    }

    private int ContextToSliderValue(int context)
    {
        for (int i = ContextSteps.Length - 1; i >= 0; i--)
        {
            if (context >= ContextSteps[i])
                return i;
        }
        return 0;
    }

    private int SliderValueToContext(float sliderValue)
    {
        int idx = Mathf.Clamp(Mathf.RoundToInt(sliderValue), 0, ContextSteps.Length - 1);
        return ContextSteps[idx];
    }

    private void OnContextSliderChanged(float value)
    {
        _currentContextValue = SliderValueToContext(value);
        UpdateContextLabel();
    }

    private void UpdateContextLabel()
    {
        if (contextValueLabel == null) return;
        
        string ctxStr = "Selected: " + FormatContextSize(_currentContextValue) + " tokens";
        
        if (_cachedModelInfo != null)
        {
            ctxStr += "  •  " + _cachedModelInfo.GetVRAMEstimateString(_currentContextValue);
        }
        
        contextValueLabel.text = ctxStr;
    }

    private string FormatContextSize(int tokens)
    {
        if (tokens >= 1024 * 1024)
            return $"{tokens / (1024 * 1024)}M";
        if (tokens >= 1024)
            return $"{tokens / 1024}k";
        return tokens.ToString();
    }

    /// <summary>
    /// Update the cached model info and refresh the UI display.
    /// </summary>
    public void SetModelInfo(OllamaModelInfo info)
    {
        _cachedModelInfo = info;
        UpdateContextLabel();
        UpdateModelInfoDisplay();
    }

    private void UpdateModelInfoDisplay()
    {
        if (modelInfoLabel == null) return;
        
        if (_cachedModelInfo == null)
        {
            modelInfoLabel.text = "Select a model and click Refresh to see model details.";
            return;
        }

        string infoText = "";
        
        if (!string.IsNullOrEmpty(_cachedModelInfo.family))
            infoText += $"Family: {_cachedModelInfo.family}";
        
        if (!string.IsNullOrEmpty(_cachedModelInfo.parameterSize))
        {
            if (infoText.Length > 0) infoText += "  •  ";
            infoText += $"Size: {_cachedModelInfo.parameterSize}";
        }
        
        if (!string.IsNullOrEmpty(_cachedModelInfo.quantizationLevel))
        {
            if (infoText.Length > 0) infoText += "  •  ";
            infoText += $"Quant: {_cachedModelInfo.quantizationLevel}";
        }
        
        if (_cachedModelInfo.contextLength > 0)
        {
            if (infoText.Length > 0) infoText += "\n";
            infoText += $"Max context: {FormatContextSize((int)_cachedModelInfo.contextLength)}";
        }
        
        if (string.IsNullOrEmpty(infoText))
            infoText = "Model info loaded.";
        
        modelInfoLabel.text = infoText;
    }

    /// <summary>
    /// Get the current context length value from the slider.
    /// </summary>
    public int GetContextLength()
    {
        return _currentContextValue;
    }

    #endregion

    #region llama.cpp Controls

    private void CreateServerModeRow(Transform parent, LLMProviderSettings settings)
    {
        var row = CreateRowContainer(parent, "ServerMode");
        row.GetComponent<LayoutElement>().preferredHeight = 24f;

        serverModeLabel = row.AddComponent<TextMeshProUGUI>();
        serverModeLabel.font = _font;
        serverModeLabel.fontSize = 11;
        serverModeLabel.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        serverModeLabel.alignment = TextAlignmentOptions.MidlineLeft;
        
        UpdateServerModeLabel(settings);
    }

    private void UpdateServerModeLabel(LLMProviderSettings settings)
    {
        if (serverModeLabel == null) return;
        
        if (settings.availableModels.Count > 1)
        {
            serverModeLabel.text = $"Router Mode: {settings.availableModels.Count} models available";
            serverModeLabel.color = new Color(0.2f, 0.5f, 0.3f, 1f); // Green tint for router mode
        }
        else if (settings.availableModels.Count == 1)
        {
            serverModeLabel.text = "Single Model Mode";
            serverModeLabel.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        }
        else
        {
            serverModeLabel.text = "Click Refresh to detect server mode";
            serverModeLabel.color = new Color(0.45f, 0.45f, 0.5f, 1f);
        }
    }

    private void CreateThinkingModeRow(Transform parent, LLMProviderSettings settings)
    {
        var row = CreateRowContainer(parent, "ThinkingMode");
        _thinkingModeRow = row;
        
        // Checkbox + label container
        var toggleContainer = new GameObject("ToggleContainer");
        toggleContainer.transform.SetParent(row.transform, false);
        var containerRt = toggleContainer.AddComponent<RectTransform>();
        containerRt.anchorMin = Vector2.zero;
        containerRt.anchorMax = Vector2.one;
        containerRt.offsetMin = Vector2.zero;
        containerRt.offsetMax = Vector2.zero;
        
        // Create toggle
        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(toggleContainer.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0, 0.5f);
        toggleRt.anchorMax = new Vector2(0, 0.5f);
        toggleRt.pivot = new Vector2(0, 0.5f);
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
        bgImg.color = InputBg;
        
        // Checkmark
        var checkGo = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.1f, 0.1f);
        checkRt.anchorMax = new Vector2(0.9f, 0.9f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.fontSize = 14;
        checkTmp.color = new Color(0.2f, 0.5f, 0.2f, 1f);
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.text = "✓";
        
        thinkingModeToggle = toggleGo.AddComponent<Toggle>();
        thinkingModeToggle.targetGraphic = bgImg;
        thinkingModeToggle.graphic = checkTmp;
        thinkingModeToggle.isOn = settings.enableThinking;
        thinkingModeToggle.onValueChanged.AddListener(OnThinkingModeChanged);
        
        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(toggleContainer.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(28, 0);
        labelRt.offsetMax = new Vector2(0, 0);
        
        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.font = _font;
        labelTmp.fontSize = 12;
        labelTmp.color = LabelColor;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.text = "Enable thinking mode (GLM/DeepSeek)";
    }
    
    private void OnThinkingModeChanged(bool value)
    {
        _enableThinking = value;
    }
    
    private void UpdateThinkingModeVisibility()
    {
        if (_thinkingModeRow == null) return;
        
        // Show thinking mode toggle only for models that support it (GLM and DeepSeek models)
        string currentModel = GetCurrentModelName();
        if (string.IsNullOrEmpty(currentModel))
        {
            _thinkingModeRow.SetActive(false);
            return;
        }
        
        string modelLower = currentModel.ToLowerInvariant();
        bool supportsThinking = modelLower.Contains("glm") || modelLower.Contains("deepseek");
        
        _thinkingModeRow.SetActive(supportsThinking);
    }
    
    /// <summary>
    /// Get the currently selected model name from the dropdown.
    /// </summary>
    public string GetCurrentModelName()
    {
        if (modelDropdown == null || modelDropdown.options == null || modelDropdown.options.Count == 0)
            return "";
        
        string modelName = modelDropdown.options[modelDropdown.value].text;
        return modelName == "(no models)" ? "" : modelName;
    }

    /// <summary>
    /// Update llama.cpp router mode state.
    /// </summary>
    public void SetRouterMode(bool isRouterMode, int modelCount)
    {
        _isRouterMode = isRouterMode;
        if (serverModeLabel != null)
        {
            if (isRouterMode)
            {
                serverModeLabel.text = $"Router Mode: {modelCount} models available";
                serverModeLabel.color = new Color(0.2f, 0.5f, 0.3f, 1f);
            }
            else if (modelCount == 1)
            {
                serverModeLabel.text = "Single Model Mode";
                serverModeLabel.color = new Color(0.35f, 0.35f, 0.4f, 1f);
            }
            else
            {
                serverModeLabel.text = "No models detected";
                serverModeLabel.color = new Color(0.5f, 0.35f, 0.35f, 1f);
            }
        }
    }

    /// <summary>
    /// Get whether thinking mode is enabled.
    /// </summary>
    public bool GetThinkingModeEnabled()
    {
        return _enableThinking;
    }

    #endregion

    #region Gemini Controls

    private void CreateGeminiThinkingModeRow(Transform parent, LLMProviderSettings settings)
    {
        var row = CreateRowContainer(parent, "ThinkingMode");
        
        // Checkbox + label container
        var toggleContainer = new GameObject("ToggleContainer");
        toggleContainer.transform.SetParent(row.transform, false);
        var containerRt = toggleContainer.AddComponent<RectTransform>();
        containerRt.anchorMin = Vector2.zero;
        containerRt.anchorMax = Vector2.one;
        containerRt.offsetMin = Vector2.zero;
        containerRt.offsetMax = Vector2.zero;
        
        // Create toggle
        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(toggleContainer.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0, 0.5f);
        toggleRt.anchorMax = new Vector2(0, 0.5f);
        toggleRt.pivot = new Vector2(0, 0.5f);
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
        bgImg.color = InputBg;
        
        // Checkmark
        var checkGo = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.1f, 0.1f);
        checkRt.anchorMax = new Vector2(0.9f, 0.9f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.fontSize = 14;
        checkTmp.color = new Color(0.2f, 0.5f, 0.2f, 1f);
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.text = "✓";
        
        thinkingModeToggle = toggleGo.AddComponent<Toggle>();
        thinkingModeToggle.targetGraphic = bgImg;
        thinkingModeToggle.graphic = checkTmp;
        thinkingModeToggle.isOn = settings.enableThinking;
        thinkingModeToggle.onValueChanged.AddListener(OnThinkingModeChanged);
        
        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(toggleContainer.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(28, 0);
        labelRt.offsetMax = new Vector2(0, 0);
        
        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.font = _font;
        labelTmp.fontSize = 12;
        labelTmp.color = LabelColor;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.text = "Enable thinking mode";
    }

    #endregion
}