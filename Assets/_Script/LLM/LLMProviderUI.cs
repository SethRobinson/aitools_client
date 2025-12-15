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

    private readonly LLMProvider _provider;
    private readonly TMP_FontAsset _font;
    private readonly TMP_DefaultControls.Resources _tmpResources;
    private readonly Action<GameObject> _styleApplier;

    private static readonly Color SectionBg = new Color(0.20f, 0.20f, 0.22f, 1f);
    private static readonly Color HeaderColor = new Color(0.50f, 0.75f, 1.00f, 1f);
    private static readonly Color LabelColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color ButtonBg = new Color(0.30f, 0.45f, 0.60f, 1f);

    private const float RowHeight = 34f;
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

        if (_provider == LLMProvider.OpenAI || _provider == LLMProvider.Anthropic)
            apiKeyInput = CreateInputRow(sectionRoot.transform, "API Key:", settings.apiKey, "Enter API key...", true);

        endpointInput = CreateInputRow(sectionRoot.transform, "Endpoint:", settings.endpoint, "http://localhost:8080", false);

        CreateModelRow(sectionRoot.transform, settings, showRefreshButton);

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
        // Ensure the root background isn't white even if child naming differs.
        var inputRootImg = inputGo.GetComponent<Image>();
        if (inputRootImg != null) inputRootImg.color = new Color(0.15f, 0.15f, 0.17f, 1f);

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
        // Ensure the root background isn't white even if child naming differs.
        var ddRootImg = ddGo.GetComponent<Image>();
        if (ddRootImg != null) ddRootImg.color = new Color(0.15f, 0.15f, 0.17f, 1f);

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
                modelDropdown.captionText.fontSize = 12;
                modelDropdown.captionText.color = Color.white;
            }
            if (modelDropdown.itemText != null)
            {
                modelDropdown.itemText.font = _font;
                modelDropdown.itemText.fontSize = 12;
                modelDropdown.itemText.color = Color.white;
            }

            if (modelDropdown.template != null)
                modelDropdown.template.sizeDelta = new Vector2(modelDropdown.template.sizeDelta.x, 180f);
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
        tmp.color = Color.white;
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
    }

    public void ApplyToSettings(LLMProviderSettings settings)
    {
        if (apiKeyInput != null)
            settings.apiKey = apiKeyInput.text;
        if (endpointInput != null)
            settings.endpoint = endpointInput.text;
        if (modelDropdown != null && modelDropdown.options != null && modelDropdown.options.Count > 0)
            settings.selectedModel = modelDropdown.options[modelDropdown.value].text;
    }

    public void UpdateModelDropdown(List<string> models, string selectedModel)
    {
        if (modelDropdown == null) return;

        modelDropdown.ClearOptions();
        if (models == null || models.Count == 0)
        {
            modelDropdown.AddOptions(new List<string> { "(no models)" });
            modelDropdown.value = 0;
            return;
        }

        modelDropdown.AddOptions(models);
        int idx = !string.IsNullOrEmpty(selectedModel) ? models.IndexOf(selectedModel) : -1;
        modelDropdown.value = idx >= 0 ? idx : 0;
        modelDropdown.RefreshShownValue();
    }

    private string GetProviderDisplayName()
    {
        return _provider switch
        {
            LLMProvider.OpenAI => "OpenAI",
            LLMProvider.Anthropic => "Anthropic",
            LLMProvider.LlamaCpp => "llama.cpp",
            LLMProvider.Ollama => "Ollama",
            _ => _provider.ToString()
        };
    }
}