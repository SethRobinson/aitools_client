using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/*
 Example of use:

Somewhere, do this:
 public GameObject m_notepadTemplatePrefab;  (attach to RTNotepad prefab)


   Then do this:

    RTNotepad m_activeNotepad;

    public void OnConfigButton()
    {
        m_activeNotepad = RTNotepad.OpenFile("some text here", m_notepadTemplatePrefab);
        m_activeNotepad.m_onClickedSavedCallback += OnConfigSaved;
        m_activeNotepad.m_onClickedCancelCallback += OnConfigCanceled;
        m_activeNotepad.m_onClickedOpenExternalCallback += OnOpenExternal;
        m_activeNotepad.m_onClickedReloadCallback += OnReload;
    }

    void OnConfigSaved(string text)
    {
        Debug.Log("They clicked save.  Text entered: " + text);
    }
    void OnConfigCanceled(string text)
    {
        Debug.Log("They clicked cancel.  Text entered: " + text);
    }
    void OnOpenExternal(string text)
    {
        // Open file in external editor, e.g.: System.Diagnostics.Process.Start("config.txt");
    }
    void OnReload(string text)
    {
        // Reload from disk and update: m_activeNotepad.SetText(File.ReadAllText("config.txt"));
    }

*/


public class RTNotepad : MonoBehaviour
{
    private const float WindowWidth = 980f;
    private const float WindowHeight = 700f;
    private const float MinWindowWidth = 620f;
    private const float MinWindowHeight = 420f;
    private const float HeaderHeight = 42f;
    private const float FooterHeight = 58f;
    private const float WindowMargin = 14f;
    private const int WindowSortingOrder = 100;

    public TMP_InputField m_textInput;
    public Action<String> m_onClickedSavedCallback;
    public Action<String> m_onClickedCancelCallback;
    public Action<String> m_onClickedApplyCallback; //when they want to Apply but not save
    public Action<String> m_onClickedOpenExternalCallback; //when they want to open in external editor
    public Action<String> m_onClickedReloadCallback; //when they want to reload from disk
    public Button m_applyButton;
    public Button m_saveButton;
    public Button m_openExternalButton;
    public Button m_reloadButton;

    //This is a little helper object designed to be called statically to create the real thing
    public static RTNotepad OpenFile(string defaultText, GameObject prefab, string title = "Text Editor")
    {
        GameObject go = Instantiate(prefab);
        RTNotepad goScript = go.GetComponent<RTNotepad>();
        goScript.ConfigureWindow(title);
        goScript.m_textInput.text = defaultText;
        return goScript;
    }

    public void BringToFront()
    {
        ConfigureCanvas();
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
    }

    public void FocusTextInput()
    {
        if (m_textInput == null)
            return;

        m_textInput.ActivateInputField();
    }

    public void SetTitle(string title)
    {
        TextMeshProUGUI titleText = GetHeaderTitle();
        if (titleText != null)
            titleText.text = title;
    }
  
    public void SetApplyButtonVisible(bool bNew)
    {
        if (m_applyButton != null)
            m_applyButton.gameObject.SetActive(bNew);
        LayoutButtons();
    }

    public void SetSaveButtonVisible(bool bNew)
    {
        if (m_saveButton != null)
            m_saveButton.gameObject.SetActive(bNew);
        LayoutButtons();
    }

    public void OnClickedSave()
    {
        m_onClickedSavedCallback?.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }

    public void OnClickedApply()
    {
        m_onClickedApplyCallback?.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }

    public void OnClickedCancel()
    {
        m_onClickedCancelCallback?.Invoke(m_textInput.text);
        GameObject.Destroy(gameObject);
    }

    public void OnClickedOpenExternal()
    {
        m_onClickedOpenExternalCallback?.Invoke(m_textInput.text);
        // Don't destroy - keep dialog open so user can reload after editing
    }

    public void OnClickedReload()
    {
        m_onClickedReloadCallback?.Invoke(m_textInput.text);
        // Don't destroy - just refreshes the text
    }

    // Allow external code to update the text (useful for reload)
    public void SetText(string text)
    {
        m_textInput.text = text;
    }

    public void SetOpenExternalButtonVisible(bool bNew)
    {
        if (m_openExternalButton != null)
            m_openExternalButton.gameObject.SetActive(bNew);
        LayoutButtons();
    }

    public void SetReloadButtonVisible(bool bNew)
    {
        if (m_reloadButton != null)
            m_reloadButton.gameObject.SetActive(bNew);
        LayoutButtons();
    }

    private void ConfigureWindow(string title)
    {
        ConfigureCanvas();

        RectTransform panel = GetPanel();
        if (panel == null)
            return;

        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(WindowWidth, WindowHeight);
        panel.localScale = Vector3.one;

        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = new Color(0.94f, 0.95f, 0.96f, 1f);
            panelImage.raycastTarget = true;
        }

        CanvasGroup panelGroup = panel.GetComponent<CanvasGroup>();
        if (panelGroup != null)
        {
            panelGroup.blocksRaycasts = true;
            panelGroup.interactable = true;
            panelGroup.alpha = 1f;
        }

        CreateOrUpdateHeader(panel, title);
        RectTransform footer = CreateOrUpdateFooter(panel);
        LayoutTextInput(panel);
        ReparentButtonToFooter(m_cancelButton, footer);
        ReparentButtonToFooter(m_saveButton, footer);
        ReparentButtonToFooter(m_applyButton, footer);
        ReparentButtonToFooter(m_openExternalButton, footer);
        ReparentButtonToFooter(m_reloadButton, footer);
        LayoutButtons();
        CreateOrUpdateResizeGrip(panel);
    }

    private Button m_cancelButton => FindButton("CancelButton");

    private void ConfigureCanvas()
    {
        transform.localScale = Vector3.one;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = WindowSortingOrder;
        }

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
    }

    private RectTransform GetPanel()
    {
        Transform panel = transform.Find("Panel");
        if (panel == null)
            return null;
        return panel as RectTransform;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private void CreateOrUpdateHeader(RectTransform panel, string title)
    {
        RectTransform header = GetOrCreateChild(panel, "RTNotepadHeader");
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = Vector2.zero;
        header.sizeDelta = new Vector2(0f, HeaderHeight);
        header.SetAsLastSibling();

        Image headerImage = header.GetComponent<Image>();
        if (headerImage == null)
            headerImage = header.gameObject.AddComponent<Image>();
        headerImage.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        headerImage.raycastTarget = true;

        PanelDragHandler dragHandler = header.GetComponent<PanelDragHandler>();
        if (dragHandler == null)
            dragHandler = header.gameObject.AddComponent<PanelDragHandler>();
        dragHandler.SetTarget(panel, HeaderHeight);

        RectTransform titleRt = GetOrCreateChild(header, "Title");
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(16f, 0f);
        titleRt.offsetMax = new Vector2(-54f, 0f);

        TextMeshProUGUI titleText = titleRt.GetComponent<TextMeshProUGUI>();
        if (titleText == null)
            titleText = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        titleText.font = FindFont();
        titleText.text = title;
        titleText.fontSize = 18f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.raycastTarget = false;

        RectTransform closeRt = GetOrCreateChild(header, "CloseButton");
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-10f, 0f);
        closeRt.sizeDelta = new Vector2(30f, 28f);

        Image closeImage = closeRt.GetComponent<Image>();
        if (closeImage == null)
            closeImage = closeRt.gameObject.AddComponent<Image>();
        closeImage.color = new Color(0.84f, 0.87f, 0.90f, 1f);

        Button closeButton = closeRt.GetComponent<Button>();
        if (closeButton == null)
            closeButton = closeRt.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(OnClickedCancel);

        RectTransform closeLabelRt = GetOrCreateChild(closeRt, "Text");
        closeLabelRt.anchorMin = Vector2.zero;
        closeLabelRt.anchorMax = Vector2.one;
        closeLabelRt.offsetMin = Vector2.zero;
        closeLabelRt.offsetMax = Vector2.zero;

        TextMeshProUGUI closeLabel = closeLabelRt.GetComponent<TextMeshProUGUI>();
        if (closeLabel == null)
            closeLabel = closeLabelRt.gameObject.AddComponent<TextMeshProUGUI>();
        closeLabel.font = FindFont();
        closeLabel.text = "X";
        closeLabel.fontSize = 14f;
        closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.color = new Color(0.08f, 0.09f, 0.10f, 1f);
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.raycastTarget = false;
    }

    private RectTransform CreateOrUpdateFooter(RectTransform panel)
    {
        RectTransform footer = GetOrCreateChild(panel, "RTNotepadFooter");
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = Vector2.zero;
        footer.sizeDelta = new Vector2(0f, FooterHeight);
        footer.SetAsLastSibling();

        Image footerImage = footer.GetComponent<Image>();
        if (footerImage == null)
            footerImage = footer.gameObject.AddComponent<Image>();
        footerImage.color = new Color(0.86f, 0.88f, 0.90f, 1f);
        footerImage.raycastTarget = false;

        return footer;
    }

    private void LayoutTextInput(RectTransform panel)
    {
        if (m_textInput == null)
            return;

        RectTransform inputRt = m_textInput.GetComponent<RectTransform>();
        inputRt.SetParent(panel, false);
        inputRt.anchorMin = Vector2.zero;
        inputRt.anchorMax = Vector2.one;
        inputRt.pivot = new Vector2(0.5f, 0.5f);
        inputRt.offsetMin = new Vector2(WindowMargin, FooterHeight + WindowMargin);
        inputRt.offsetMax = new Vector2(-WindowMargin - 22f, -HeaderHeight - WindowMargin);

        Image inputImage = m_textInput.GetComponent<Image>();
        if (inputImage != null)
            inputImage.color = Color.white;

        m_textInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        m_textInput.scrollSensitivity = 20f;
        m_textInput.textViewport.offsetMin = new Vector2(10f, 6f);
        m_textInput.textViewport.offsetMax = new Vector2(-10f, -6f);

        if (m_textInput.textComponent != null)
        {
            m_textInput.textComponent.font = FindFont();
            m_textInput.textComponent.fontSize = 14f;
            m_textInput.textComponent.color = new Color(0.08f, 0.09f, 0.10f, 1f);
            m_textInput.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
        }

        if (m_textInput.placeholder is TextMeshProUGUI placeholder)
        {
            placeholder.font = FindFont();
            placeholder.color = new Color(0.20f, 0.22f, 0.24f, 0.55f);
        }

        if (m_textInput.verticalScrollbar != null)
        {
            RectTransform scrollbarRt = m_textInput.verticalScrollbar.GetComponent<RectTransform>();
            scrollbarRt.SetParent(panel, false);
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 0.5f);
            scrollbarRt.offsetMin = new Vector2(-WindowMargin - 18f, FooterHeight + WindowMargin);
            scrollbarRt.offsetMax = new Vector2(-WindowMargin, -HeaderHeight - WindowMargin);
        }
    }

    private void ReparentButtonToFooter(Button button, RectTransform footer)
    {
        if (button == null || footer == null)
            return;

        button.transform.SetParent(footer, false);
        NormalizeButton(button);
    }

    private void LayoutButtons()
    {
        RectTransform footer = GetFooter();
        if (footer == null)
            return;

        float x = -14f;
        LayoutButton(m_cancelButton, ref x, 86f);
        LayoutButton(m_saveButton, ref x, 118f);
        LayoutButton(m_applyButton, ref x, 100f);
        LayoutButton(m_reloadButton, ref x, 86f);
        LayoutButton(m_openExternalButton, ref x, 126f);
    }

    private void LayoutButton(Button button, ref float rightX, float width)
    {
        if (button == null || !button.gameObject.activeSelf)
            return;

        RectTransform rt = button.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(width, 32f);
        rt.anchoredPosition = new Vector2(rightX, 0f);
        rightX -= width + 10f;
        NormalizeButton(button);
    }

    private void NormalizeButton(Button button)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = Color.white;
            button.targetGraphic = image;
        }

        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.94f, 0.96f, 0.97f, 1f),
            pressedColor = new Color(0.78f, 0.82f, 0.84f, 1f),
            selectedColor = new Color(0.94f, 0.96f, 0.97f, 1f),
            disabledColor = new Color(0.78f, 0.82f, 0.84f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        foreach (TextMeshProUGUI label in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.font = FindFont();
            label.fontSize = 13f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 13f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.08f, 0.09f, 0.10f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }

    private void CreateOrUpdateResizeGrip(RectTransform panel)
    {
        RectTransform grip = GetOrCreateChild(panel, "RTNotepadResizeGrip");
        grip.anchorMin = new Vector2(1f, 0f);
        grip.anchorMax = new Vector2(1f, 0f);
        grip.pivot = new Vector2(1f, 0f);
        grip.anchoredPosition = Vector2.zero;
        grip.sizeDelta = new Vector2(24f, 24f);
        grip.SetAsLastSibling();

        Image gripImage = grip.GetComponent<Image>();
        if (gripImage == null)
            gripImage = grip.gameObject.AddComponent<Image>();
        gripImage.color = new Color(0.45f, 0.49f, 0.53f, 0.50f);
        gripImage.raycastTarget = true;

        RTNotepadResizeGrip resize = grip.GetComponent<RTNotepadResizeGrip>();
        if (resize == null)
            resize = grip.gameObject.AddComponent<RTNotepadResizeGrip>();
        resize.SetTarget(panel, MinWindowWidth, MinWindowHeight, HeaderHeight);
    }

    private RectTransform GetOrCreateChild(RectTransform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child as RectTransform;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private TextMeshProUGUI GetHeaderTitle()
    {
        RectTransform panel = GetPanel();
        if (panel == null)
            return null;
        Transform header = panel.Find("RTNotepadHeader");
        if (header == null)
            return null;
        Transform title = header.Find("Title");
        return title != null ? title.GetComponent<TextMeshProUGUI>() : null;
    }

    private RectTransform GetFooter()
    {
        RectTransform panel = GetPanel();
        if (panel == null)
            return null;
        Transform footer = panel.Find("RTNotepadFooter");
        return footer as RectTransform;
    }

    private Button FindButton(string name)
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
                return button;
        }

        return null;
    }
}

public class RTNotepadResizeGrip : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _target;
    private Vector2 _startPointer;
    private Vector2 _startSize;
    private Vector2 _startPosition;
    private float _minWidth;
    private float _minHeight;
    private float _headerHeight;

    public void SetTarget(RectTransform target, float minWidth, float minHeight, float headerHeight)
    {
        _target = target;
        _minWidth = Mathf.Max(240f, minWidth);
        _minHeight = Mathf.Max(180f, minHeight);
        _headerHeight = Mathf.Max(8f, headerHeight);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_target == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out _startPointer);
        _startSize = _target.sizeDelta;
        _startPosition = _target.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _target.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 pointer);

        Vector2 delta = pointer - _startPointer;
        float width = Mathf.Max(_minWidth, _startSize.x + delta.x);
        float height = Mathf.Max(_minHeight, _startSize.y - delta.y);
        float effectiveDeltaX = width - _startSize.x;
        float effectiveDeltaY = _startSize.y - height;

        _target.sizeDelta = new Vector2(width, height);
        _target.anchoredPosition = PanelDragHandler.ClampAnchoredPosition(
            _target,
            _startPosition + new Vector2(effectiveDeltaX * 0.5f, effectiveDeltaY * 0.5f),
            _headerHeight);
    }
}
