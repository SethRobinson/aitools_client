using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Export options dialog for Adventure mode. Shows available export formats based on current mode.
/// </summary>
public class AdventureExportDialog : MonoBehaviour
{
    private static AdventureExportDialog _instance;
    private static GameObject _dialogRoot;

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;
    
    // UI References
    private Button _exportCYOAHTMLButton;
    private Button _exportCYOATwineButton;
    private Button _exportQuizHTMLButton;
    private TextMeshProUGUI _exportCYOAHTMLText;
    private TextMeshProUGUI _exportCYOATwineText;
    private TextMeshProUGUI _exportQuizHTMLText;
    
    // Progress bar
    private GameObject _progressContainer;
    private Image _progressFill;
    private TextMeshProUGUI _progressText;
    private Button _cancelButton;
    
    private Coroutine _activeExportCoroutine;
    private bool _exportInProgress;

    private const float PANEL_WIDTH = 460f;
    private const float PANEL_HEIGHT = 480f;
    private const float HEADER_HEIGHT = 40f;
    private const float BUTTON_HEIGHT = 32f;
    private const float BaseFontSize = 13f;

    // Theme colors matching LLMSettingsPanel
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color ButtonEnabled = new Color(1f, 1f, 1f, 1f);
    private static readonly Color ButtonDisabled = new Color(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Color TextDark = new Color(0, 0, 0, 1f);
    private static readonly Color TextDisabled = new Color(0.45f, 0.45f, 0.45f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color ProgressBg = new Color(0.3f, 0.3f, 0.3f, 1f);
    private static readonly Color ProgressFill = new Color(0.2f, 0.7f, 0.3f, 1f);
    private static readonly Color BackdropBg = new Color(0.12f, 0.12f, 0.12f, 0.65f);

    public static void Show()
    {
        if (_instance != null)
        {
            _dialogRoot.SetActive(true);
            _instance.RefreshButtonStates();
            return;
        }

        _dialogRoot = new GameObject("AdventureExportDialog");
        _instance = _dialogRoot.AddComponent<AdventureExportDialog>();
        _instance.CreateUI();
    }

    public static void Hide()
    {
        if (_instance != null && _instance._exportInProgress)
        {
            // Don't allow closing while export is in progress
            return;
        }
        
        if (_dialogRoot != null)
            _dialogRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        _instance = null;
        _dialogRoot = null;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private void CreateUI()
    {
        _font = FindFont();

        // Canvas
        var canvas = _dialogRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // Above other UI

        var scaler = _dialogRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _dialogRoot.AddComponent<GraphicRaycaster>();

        // Modal backdrop
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(_dialogRoot.transform, false);
        var backdropRt = backdrop.AddComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.color = BackdropBg;
        backdropImg.raycastTarget = true;
        // Click backdrop to close (unless export in progress)
        var backdropBtn = backdrop.AddComponent<Button>();
        backdropBtn.onClick.AddListener(Hide);

        // Main panel
        var main = new GameObject("MainPanel");
        main.transform.SetParent(_dialogRoot.transform, false);
        _mainPanel = main.AddComponent<RectTransform>();
        _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
        _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
        _mainPanel.pivot = new Vector2(0.5f, 0.5f);
        _mainPanel.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
        var panelImg = main.AddComponent<Image>();
        panelImg.color = PanelBg;

        CreateHeader();
        CreateContent();
        CreateProgressBar();

        RefreshButtonStates();
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

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-36, 0);

        var title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "Export Adventure";
        title.font = _font;
        title.fontSize = 18;
        title.fontStyle = FontStyles.Bold;
        title.color = TextTitle;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        // Close button (X)
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

    private void CreateContent()
    {
        var content = new GameObject("Content");
        content.transform.SetParent(_mainPanel, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 0);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.offsetMin = new Vector2(20, 70); // Leave space for progress bar at bottom
        contentRt.offsetMax = new Vector2(-20, -HEADER_HEIGHT - 5);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // Info text
        var infoObj = new GameObject("InfoText");
        infoObj.transform.SetParent(content.transform, false);
        var infoLE = infoObj.AddComponent<LayoutElement>();
        infoLE.preferredHeight = 30;
        
        var infoText = infoObj.AddComponent<TextMeshProUGUI>();
        infoText.text = "Some options may be grayed out if they don't apply to the current adventure type.";
        infoText.font = _font;
        infoText.fontSize = 11;
        infoText.fontStyle = FontStyles.Italic;
        infoText.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        infoText.alignment = TextAlignmentOptions.Center;

        // Export CYOA to HTML button
        CreateExportButton(content.transform, "Export CYOA story to HTML and play it", 
            out _exportCYOAHTMLButton, out _exportCYOAHTMLText, OnExportCYOAHTML);

        // Export CYOA to Twine button
        CreateExportButton(content.transform, "Export CYOA story to Twine (.twee)", 
            out _exportCYOATwineButton, out _exportCYOATwineText, OnExportCYOATwine);

        // Export Quiz to HTML button
        CreateExportButton(content.transform, "Export Quiz to HTML", 
            out _exportQuizHTMLButton, out _exportQuizHTMLText, OnExportQuizHTML);
    }

    private void CreateExportButton(Transform parent, string text, out Button button, out TextMeshProUGUI textComponent, UnityEngine.Events.UnityAction onClick)
    {
        var btnObj = new GameObject("Btn_" + text.Replace(" ", ""));
        btnObj.transform.SetParent(parent, false);
        var le = btnObj.AddComponent<LayoutElement>();
        le.preferredHeight = BUTTON_HEIGHT;

        var img = btnObj.AddComponent<Image>();
        img.color = ButtonEnabled;
        button = btnObj.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.9f, 0.95f, 1f, 1f),
            pressedColor = new Color(0.8f, 0.85f, 0.9f, 1f),
            selectedColor = new Color(0.9f, 0.95f, 1f, 1f),
            disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        var txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        var txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(10, 0);
        txtRt.offsetMax = new Vector2(-10, 0);

        textComponent = txtObj.AddComponent<TextMeshProUGUI>();
        textComponent.font = _font;
        textComponent.text = text;
        textComponent.fontSize = BaseFontSize;
        textComponent.fontStyle = FontStyles.Bold;
        textComponent.color = TextDark;
        textComponent.alignment = TextAlignmentOptions.Center;
    }

    private void CreateProgressBar()
    {
        _progressContainer = new GameObject("ProgressContainer");
        _progressContainer.transform.SetParent(_mainPanel, false);
        var containerRt = _progressContainer.AddComponent<RectTransform>();
        containerRt.anchorMin = new Vector2(0, 0);
        containerRt.anchorMax = new Vector2(1, 0);
        containerRt.pivot = new Vector2(0.5f, 0);
        containerRt.sizeDelta = new Vector2(0, 60);
        containerRt.anchoredPosition = new Vector2(0, 10);

        // Progress bar background
        var barBg = new GameObject("ProgressBg");
        barBg.transform.SetParent(_progressContainer.transform, false);
        var barBgRt = barBg.AddComponent<RectTransform>();
        barBgRt.anchorMin = new Vector2(0, 0.5f);
        barBgRt.anchorMax = new Vector2(1, 0.5f);
        barBgRt.pivot = new Vector2(0.5f, 0.5f);
        barBgRt.sizeDelta = new Vector2(-40, 24);
        barBgRt.anchoredPosition = new Vector2(0, 10);
        var barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = ProgressBg;

        // Progress bar fill
        var barFill = new GameObject("ProgressFill");
        barFill.transform.SetParent(barBg.transform, false);
        var barFillRt = barFill.AddComponent<RectTransform>();
        barFillRt.anchorMin = new Vector2(0, 0);
        barFillRt.anchorMax = new Vector2(0, 1); // Will be updated via code
        barFillRt.pivot = new Vector2(0, 0.5f);
        barFillRt.offsetMin = new Vector2(2, 2);
        barFillRt.offsetMax = new Vector2(-2, -2);
        _progressFill = barFill.AddComponent<Image>();
        _progressFill.color = ProgressFill;

        // Progress text
        var textObj = new GameObject("ProgressText");
        textObj.transform.SetParent(_progressContainer.transform, false);
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0, 1);
        textRt.anchorMax = new Vector2(1, 1);
        textRt.pivot = new Vector2(0.5f, 1);
        textRt.sizeDelta = new Vector2(0, 20);
        textRt.anchoredPosition = new Vector2(0, 0);
        
        _progressText = textObj.AddComponent<TextMeshProUGUI>();
        _progressText.font = _font;
        _progressText.text = "Exporting...";
        _progressText.fontSize = 12;
        _progressText.color = TextDark;
        _progressText.alignment = TextAlignmentOptions.Center;

        // Cancel button
        var cancelObj = new GameObject("CancelBtn");
        cancelObj.transform.SetParent(_progressContainer.transform, false);
        var cancelRt = cancelObj.AddComponent<RectTransform>();
        cancelRt.anchorMin = new Vector2(0.5f, 0);
        cancelRt.anchorMax = new Vector2(0.5f, 0);
        cancelRt.pivot = new Vector2(0.5f, 0);
        cancelRt.sizeDelta = new Vector2(80, 24);
        cancelRt.anchoredPosition = new Vector2(0, -5);

        var cancelImg = cancelObj.AddComponent<Image>();
        cancelImg.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        _cancelButton = cancelObj.AddComponent<Button>();
        _cancelButton.targetGraphic = cancelImg;
        _cancelButton.onClick.AddListener(OnCancelExport);

        var cancelTxtObj = new GameObject("Text");
        cancelTxtObj.transform.SetParent(cancelObj.transform, false);
        var cancelTxtRt = cancelTxtObj.AddComponent<RectTransform>();
        cancelTxtRt.anchorMin = Vector2.zero;
        cancelTxtRt.anchorMax = Vector2.one;
        cancelTxtRt.offsetMin = Vector2.zero;
        cancelTxtRt.offsetMax = Vector2.zero;

        var cancelTxt = cancelTxtObj.AddComponent<TextMeshProUGUI>();
        cancelTxt.font = _font;
        cancelTxt.text = "Cancel";
        cancelTxt.fontSize = 12;
        cancelTxt.color = TextDark;
        cancelTxt.alignment = TextAlignmentOptions.Center;

        // Hide progress bar initially
        _progressContainer.SetActive(false);
    }

    private void RefreshButtonStates()
    {
        var mode = AdventureLogic.Get().GetMode();

        bool canExportCYOA = mode == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE;
        bool canExportQuiz = mode == AdventureMode.QUIZ;

        SetButtonEnabled(_exportCYOAHTMLButton, _exportCYOAHTMLText, canExportCYOA);
        SetButtonEnabled(_exportCYOATwineButton, _exportCYOATwineText, canExportCYOA);
        SetButtonEnabled(_exportQuizHTMLButton, _exportQuizHTMLText, canExportQuiz);
    }

    private void SetButtonEnabled(Button button, TextMeshProUGUI text, bool enabled)
    {
        button.interactable = enabled;
        text.color = enabled ? TextDark : TextDisabled;
        button.GetComponent<Image>().color = enabled ? ButtonEnabled : ButtonDisabled;
    }

    private void ShowProgress(bool show)
    {
        _progressContainer.SetActive(show);
        _exportCYOAHTMLButton.interactable = !show && AdventureLogic.Get().GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE;
        _exportCYOATwineButton.interactable = !show && AdventureLogic.Get().GetMode() == AdventureMode.CHOOSE_YOUR_OWN_ADVENTURE;
        _exportQuizHTMLButton.interactable = !show && AdventureLogic.Get().GetMode() == AdventureMode.QUIZ;
    }

    public void UpdateProgress(float progress, string message = null)
    {
        if (_progressFill != null)
        {
            var rt = _progressFill.GetComponent<RectTransform>();
            rt.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
        }
        
        if (_progressText != null && message != null)
        {
            _progressText.text = message;
        }
    }

    private void OnExportCYOAHTML()
    {
        if (_exportInProgress) return;
        
        _exportInProgress = true;
        ShowProgress(true);
        UpdateProgress(0, "Starting HTML export...");
        
        var exporter = AdventureLogic.Get().gameObject.GetComponent<AdventureExportHTML>();
        if (exporter == null)
        {
            exporter = AdventureLogic.Get().gameObject.AddComponent<AdventureExportHTML>();
        }
        
        _activeExportCoroutine = StartCoroutine(ExportWithProgress(exporter.Export(OnProgressUpdate)));
    }

    private void OnExportCYOATwine()
    {
        if (_exportInProgress) return;
        
        _exportInProgress = true;
        ShowProgress(true);
        UpdateProgress(0, "Starting Twine export...");
        
        var exporter = AdventureLogic.Get().gameObject.GetComponent<AdventureExportTwine>();
        _activeExportCoroutine = StartCoroutine(ExportWithProgress(exporter.Export(OnProgressUpdate)));
    }

    private void OnExportQuizHTML()
    {
        if (_exportInProgress) return;
        
        _exportInProgress = true;
        ShowProgress(true);
        UpdateProgress(0, "Starting Quiz export...");
        
        var exporter = AdventureLogic.Get().gameObject.GetComponent<AdventureExportQuiz>();
        _activeExportCoroutine = StartCoroutine(ExportWithProgress(exporter.Export(OnProgressUpdate)));
    }

    private void OnProgressUpdate(float progress, string message)
    {
        UpdateProgress(progress, message);
    }

    private IEnumerator ExportWithProgress(IEnumerator exportCoroutine)
    {
        yield return exportCoroutine;
        
        _exportInProgress = false;
        ShowProgress(false);
        Hide();
    }

    private void OnCancelExport()
    {
        if (_activeExportCoroutine != null)
        {
            StopCoroutine(_activeExportCoroutine);
            _activeExportCoroutine = null;
        }
        
        _exportInProgress = false;
        ShowProgress(false);
        RTQuickMessageManager.Get().ShowMessage("Export cancelled");
    }
}

