using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Reusable, programmatically-built popup that lets the user pick a file from the Presets/
/// folder with a live filename filter.
///
/// Usage:
///     PresetPickerDialog.Show(new PresetPickerDialog.Options {
///         Title = "Select Preset",
///         CurrentSelection = currentName,
///         FileFilterPrefix = null,         // "AutoPic" for AutoPic-only
///         SpecialNoneLabel = "<no selection>",
///     },
///     onSelect: (name) => { ... },
///     onCancel: () => { ... });
/// </summary>
public class PresetPickerDialog : MonoBehaviour
{
    public class Options
    {
        public string Title = "Select Preset";
        public string CurrentSelection;
        public string FileFilterPrefix;
        public bool ExcludeAutoPicAndSummarize; // Hide AutoPic*/AdventureSummarize.txt (job-script pickers)
        public string SpecialNoneLabel;
        public string InitialFilterText;
    }

    private Options _opts;
    private System.Action<string> _onSelect;
    private System.Action _onCancel;

    private TMP_FontAsset _font;
    private RectTransform _mainPanel;
    private TMP_InputField _filterInput;
    private RectTransform _listContent;
    private List<string> _allEntries = new List<string>();
    private List<RowEntry> _rows = new List<RowEntry>();
    private int _highlightedIndex = -1;

    // Panel dimensions
    private const float PANEL_WIDTH = 520f;
    private const float PANEL_HEIGHT = 480f;
    private const float HEADER_HEIGHT = 36f;
    private const float FILTER_HEIGHT = 32f;
    private const float FOOTER_HEIGHT = 44f;
    private const float ROW_HEIGHT = 26f;
    private const float BASE_FONT_SIZE = 14f;

    // Theme colors (matching ServerSettingsPanel/GenerateSettingsPanel)
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color InputFieldBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color RowBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color RowAltBg = new Color(0.94f, 0.94f, 0.95f, 1f);
    private static readonly Color RowHoverBg = new Color(0.85f, 0.90f, 1f, 1f);
    private static readonly Color RowHighlightBg = new Color(0.6f, 0.78f, 1f, 1f);
    private static readonly Color TextDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color SpecialItemColor = new Color(0.30f, 0.30f, 0.45f, 1f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 1f);

    private static Sprite _uiBackgroundSprite;
    private static bool _spritesCached;

    private class RowEntry
    {
        public string FileName;
        public Button Button;
        public Image Background;
        public TextMeshProUGUI Label;
        public bool IsSpecial;
    }

    public static PresetPickerDialog Show(Options opts, System.Action<string> onSelect, System.Action onCancel = null)
    {
        if (opts == null) opts = new Options();

        var go = new GameObject("PresetPickerDialog");
        var dlg = go.AddComponent<PresetPickerDialog>();
        dlg._opts = opts;
        dlg._onSelect = onSelect;
        dlg._onCancel = onCancel;
        dlg.CreateUI();
        return dlg;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private static void CacheSprite()
    {
        if (_spritesCached) return;

        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null) continue;
            if (img.type != Image.Type.Sliced) continue;
            _uiBackgroundSprite = img.sprite;
            break;
        }

        _spritesCached = _uiBackgroundSprite != null;
    }

    private static void ApplyUISprite(Image img)
    {
        if (img == null) return;
        CacheSprite();
        if (_uiBackgroundSprite != null)
        {
            img.sprite = _uiBackgroundSprite;
            img.type = Image.Type.Sliced;
        }
    }

    private void CreateUI()
    {
        _font = FindFont();
        CacheSprite();

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Dim full-screen overlay; clicks on it cancel.
        var overlayGo = new GameObject("Overlay");
        overlayGo.transform.SetParent(transform, false);
        var overlayRt = overlayGo.AddComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        var overlayImg = overlayGo.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.40f);
        var overlayBtn = overlayGo.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(Cancel);

        var main = new GameObject("MainPanel");
        main.transform.SetParent(transform, false);
        _mainPanel = main.AddComponent<RectTransform>();
        _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
        _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
        _mainPanel.pivot = new Vector2(0.5f, 0.5f);
        _mainPanel.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
        var panelImg = main.AddComponent<Image>();
        ApplyUISprite(panelImg);
        panelImg.color = PanelBg;

        CreateHeader();
        CreateFilterRow();
        CreateScrollList();
        CreateFooter();

        RebuildEntries();
        ApplyFilter(_opts.InitialFilterText ?? "");

        if (!string.IsNullOrEmpty(_opts.CurrentSelection))
            HighlightByName(_opts.CurrentSelection);
        else if (_rows.Count > 0)
            SetHighlight(0, scrollIntoView: true);

        StartCoroutine(FocusFilterNextFrame());
    }

    private IEnumerator FocusFilterNextFrame()
    {
        yield return null;
        if (_filterInput == null) yield break;
        EventSystem.current?.SetSelectedGameObject(_filterInput.gameObject);
        _filterInput.ActivateInputField();
        _filterInput.caretPosition = _filterInput.text.Length;
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

        var dragHandler = header.AddComponent<PanelDragHandler>();
        dragHandler.SetTarget(_mainPanel);

        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-40, 0);

        var title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = _opts.Title ?? "Select Preset";
        title.font = _font;
        title.fontSize = 18;
        title.fontStyle = FontStyles.Bold;
        title.color = TextTitle;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        RTWindowChrome.CreateCloseButton(rt, Cancel);
    }

    private void CreateFilterRow()
    {
        var row = new GameObject("FilterRow");
        row.transform.SetParent(_mainPanel, false);
        var rt = row.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(12, -(HEADER_HEIGHT + 8 + FILTER_HEIGHT));
        rt.offsetMax = new Vector2(-12, -(HEADER_HEIGHT + 8));

        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "FilterInput";
        inputGo.transform.SetParent(row.transform, false);
        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = Vector2.zero;
        inputRt.anchorMax = Vector2.one;
        inputRt.offsetMin = Vector2.zero;
        inputRt.offsetMax = Vector2.zero;

        _filterInput = inputGo.GetComponent<TMP_InputField>();
        _filterInput.lineType = TMP_InputField.LineType.SingleLine;
        if (_filterInput.textComponent != null)
        {
            _filterInput.textComponent.font = _font;
            _filterInput.textComponent.fontSize = BASE_FONT_SIZE;
            _filterInput.textComponent.color = TextDark;
            _filterInput.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        }
        if (_filterInput.placeholder is TextMeshProUGUI ph)
        {
            ph.text = "Type to filter...";
            ph.font = _font;
            ph.fontSize = BASE_FONT_SIZE;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.20f, 0.20f, 0.20f, 0.5f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
        }

        var inputImg = inputGo.GetComponent<Image>();
        ApplyUISprite(inputImg);
        inputImg.color = InputFieldBg;

        _filterInput.customCaretColor = true;
        _filterInput.caretColor = TextDark;
        _filterInput.caretWidth = 2;
        _filterInput.selectionColor = new Color(0.25f, 0.5f, 1f, 0.40f);

        _filterInput.onValueChanged.AddListener(OnFilterChanged);
        _filterInput.onSubmit.AddListener(OnFilterSubmit);
    }

    private void CreateScrollList()
    {
        var scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(_mainPanel, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.offsetMin = new Vector2(12, FOOTER_HEIGHT);
        scrollRt.offsetMax = new Vector2(-12, -(HEADER_HEIGHT + 8 + FILTER_HEIGHT + 8));

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-14, 0);
        var vpImg = viewport.AddComponent<Image>();
        ApplyUISprite(vpImg);
        vpImg.color = InputFieldBg;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _listContent = content.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0, 1);
        _listContent.anchorMax = new Vector2(1, 1);
        _listContent.pivot = new Vector2(0.5f, 1);
        _listContent.anchoredPosition = Vector2.zero;
        _listContent.sizeDelta = Vector2.zero;

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 0;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vpRt;
        scrollRect.content = _listContent;

        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(scrollGo.transform, false);
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = new Vector2(1, 1);
        sbRt.pivot = new Vector2(1, 0.5f);
        sbRt.sizeDelta = new Vector2(12, 0);
        sbRt.anchoredPosition = Vector2.zero;
        sbGo.AddComponent<Image>().color = new Color(0.75f, 0.75f, 0.77f, 1f);

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

        // Hint text on the left
        var hintObj = new GameObject("Hint");
        hintObj.transform.SetParent(footer.transform, false);
        var hintRt = hintObj.AddComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0, 0);
        hintRt.anchorMax = new Vector2(0.6f, 1);
        hintRt.offsetMin = new Vector2(12, 0);
        hintRt.offsetMax = new Vector2(0, 0);

        var hintTxt = hintObj.AddComponent<TextMeshProUGUI>();
        hintTxt.text = "Enter to select, Esc to cancel";
        hintTxt.font = _font;
        hintTxt.fontSize = BASE_FONT_SIZE - 2;
        hintTxt.color = new Color(0.20f, 0.20f, 0.20f, 0.85f);
        hintTxt.alignment = TextAlignmentOptions.MidlineLeft;

        CreateFooterButton(footer.transform, "Cancel", -10f, 90f, Cancel);
    }

    private void CreateFooterButton(Transform parent, string text, float xOffset, float width, UnityEngine.Events.UnityAction onClick)
    {
        var btn = new GameObject("Btn_" + text);
        btn.transform.SetParent(parent, false);
        var rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0);
        rt.sizeDelta = new Vector2(width, 30);

        var img = btn.AddComponent<Image>();
        ApplyUISprite(img);
        img.color = ButtonColor;
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
        tmp.fontSize = BASE_FONT_SIZE;
        tmp.color = TextTitle;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private void RebuildEntries()
    {
        _allEntries.Clear();
        if (!string.IsNullOrEmpty(_opts.SpecialNoneLabel))
            _allEntries.Add(_opts.SpecialNoneLabel);
        var fileNames = PresetManager.GetPresetFileNames(_opts.FileFilterPrefix, _opts.ExcludeAutoPicAndSummarize);
        _allEntries.AddRange(fileNames);
    }

    private void OnFilterChanged(string text)
    {
        ApplyFilter(text);
    }

    private void OnFilterSubmit(string text)
    {
        // Enter inside the input field: select highlighted (or first match)
        ConfirmHighlighted();
    }

    private void ApplyFilter(string filterText)
    {
        // Tear down old rows
        foreach (var row in _rows)
        {
            if (row.Button != null) Destroy(row.Button.gameObject);
        }
        _rows.Clear();
        _highlightedIndex = -1;

        string needle = (filterText ?? "").Trim().ToLowerInvariant();

        int matchIdx = 0;
        foreach (string entry in _allEntries)
        {
            bool isSpecial = !string.IsNullOrEmpty(_opts.SpecialNoneLabel) && entry == _opts.SpecialNoneLabel;
            bool match = string.IsNullOrEmpty(needle) ||
                         entry.ToLowerInvariant().Contains(needle) ||
                         isSpecial; // Always show the special "<no selection>" / "<use global>" entry
            if (!match) continue;

            CreateRow(entry, matchIdx, isSpecial);
            matchIdx++;
        }

        // Default highlight to the first row, if any
        if (_rows.Count > 0)
            SetHighlight(0, scrollIntoView: false);
        else
            _highlightedIndex = -1;

        // Highlight the currently-selected entry if it survived the filter
        if (!string.IsNullOrEmpty(_opts.CurrentSelection))
            HighlightByName(_opts.CurrentSelection);
    }

    private void CreateRow(string fileName, int index, bool isSpecial)
    {
        var rowGo = new GameObject("Row_" + index);
        rowGo.transform.SetParent(_listContent, false);

        var rt = rowGo.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, ROW_HEIGHT);

        var le = rowGo.AddComponent<LayoutElement>();
        le.minHeight = ROW_HEIGHT;
        le.preferredHeight = ROW_HEIGHT;

        var bg = rowGo.AddComponent<Image>();
        bg.color = (index % 2 == 0) ? RowBg : RowAltBg;

        var btn = rowGo.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = new ColorBlock
        {
            normalColor = bg.color,
            highlightedColor = RowHoverBg,
            pressedColor = RowHighlightBg,
            selectedColor = bg.color,
            disabledColor = bg.color * 0.6f,
            colorMultiplier = 1f,
            fadeDuration = 0.05f
        };

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowGo.transform, false);
        var lblRt = labelObj.AddComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = new Vector2(10, 0);
        lblRt.offsetMax = new Vector2(-10, 0);

        var lbl = labelObj.AddComponent<TextMeshProUGUI>();
        lbl.text = fileName;
        lbl.font = _font;
        lbl.fontSize = BASE_FONT_SIZE;
        lbl.color = isSpecial ? SpecialItemColor : TextDark;
        if (isSpecial) lbl.fontStyle = FontStyles.Italic;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        lbl.overflowMode = TextOverflowModes.Ellipsis;

        var entry = new RowEntry
        {
            FileName = fileName,
            Button = btn,
            Background = bg,
            Label = lbl,
            IsSpecial = isSpecial,
        };
        _rows.Add(entry);

        int rowIdx = _rows.Count - 1;
        btn.onClick.AddListener(() => OnRowClicked(rowIdx));
    }

    private void OnRowClicked(int rowIdx)
    {
        if (rowIdx < 0 || rowIdx >= _rows.Count) return;
        Confirm(_rows[rowIdx].FileName);
    }

    private void HighlightByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].FileName, name, System.StringComparison.OrdinalIgnoreCase))
            {
                SetHighlight(i, scrollIntoView: true);
                return;
            }
        }
    }

    private void SetHighlight(int newIdx, bool scrollIntoView)
    {
        if (newIdx < 0 || newIdx >= _rows.Count)
        {
            _highlightedIndex = -1;
            return;
        }

        // Clear previous highlight
        if (_highlightedIndex >= 0 && _highlightedIndex < _rows.Count)
        {
            var prev = _rows[_highlightedIndex];
            var baseColor = (_highlightedIndex % 2 == 0) ? RowBg : RowAltBg;
            prev.Background.color = baseColor;
            var c = prev.Button.colors;
            c.normalColor = baseColor;
            c.selectedColor = baseColor;
            prev.Button.colors = c;
        }

        _highlightedIndex = newIdx;
        var row = _rows[_highlightedIndex];
        row.Background.color = RowHighlightBg;
        var nc = row.Button.colors;
        nc.normalColor = RowHighlightBg;
        nc.selectedColor = RowHighlightBg;
        row.Button.colors = nc;

        if (scrollIntoView)
            StartCoroutine(ScrollHighlightIntoViewNextFrame());
    }

    private IEnumerator ScrollHighlightIntoViewNextFrame()
    {
        yield return null;
        if (_highlightedIndex < 0 || _highlightedIndex >= _rows.Count) yield break;
        if (_listContent == null) yield break;

        var scrollRect = _listContent.GetComponentInParent<ScrollRect>();
        if (scrollRect == null || scrollRect.viewport == null) yield break;

        Canvas.ForceUpdateCanvases();

        var rowRt = _rows[_highlightedIndex].Button.transform as RectTransform;
        if (rowRt == null) yield break;

        float contentHeight = _listContent.rect.height;
        float viewportHeight = scrollRect.viewport.rect.height;
        if (contentHeight <= viewportHeight) yield break;

        // anchoredPosition.y on a top-anchored row is negative; convert to "distance from top"
        float rowTop = -rowRt.anchoredPosition.y;
        float rowBottom = rowTop + rowRt.rect.height;
        float viewTop = _listContent.anchoredPosition.y;
        float viewBottom = viewTop + viewportHeight;

        if (rowTop < viewTop)
        {
            _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, rowTop);
        }
        else if (rowBottom > viewBottom)
        {
            float newY = rowBottom - viewportHeight;
            _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, newY);
        }
    }

    private void ConfirmHighlighted()
    {
        if (_highlightedIndex < 0 || _highlightedIndex >= _rows.Count)
        {
            if (_rows.Count > 0)
                Confirm(_rows[0].FileName);
            return;
        }
        Confirm(_rows[_highlightedIndex].FileName);
    }

    private void Confirm(string name)
    {
        // Map the special label back to empty string so callers don't need to re-check.
        string result = name;
        if (!string.IsNullOrEmpty(_opts.SpecialNoneLabel) && name == _opts.SpecialNoneLabel)
            result = "";
        var cb = _onSelect;
        _onSelect = null;
        _onCancel = null;
        cb?.Invoke(result);
        Close();
    }

    private void Cancel()
    {
        var cb = _onCancel;
        _onSelect = null;
        _onCancel = null;
        cb?.Invoke();
        Close();
    }

    private void Close()
    {
        if (gameObject != null) Destroy(gameObject);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            int next = Mathf.Min((_highlightedIndex < 0 ? -1 : _highlightedIndex) + 1, _rows.Count - 1);
            if (next >= 0) SetHighlight(next, scrollIntoView: true);
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            int next = Mathf.Max((_highlightedIndex < 0 ? _rows.Count : _highlightedIndex) - 1, 0);
            if (_rows.Count > 0) SetHighlight(next, scrollIntoView: true);
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            // Submit even if focus left the input
            ConfirmHighlighted();
        }
    }
}
