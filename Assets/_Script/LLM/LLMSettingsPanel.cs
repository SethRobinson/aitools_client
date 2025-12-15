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
    private const float HEADER_HEIGHT = 40f;
    private const float FOOTER_HEIGHT = 60f;
    private const float BaseFontSize = 14f;

    // Theme - pulled from existing UI prefabs / Main.unity:
    // - Panel backgrounds are white with slight alpha (uses built-in UI sprite 10907)
    // - Title text is black
    // - Body text is dark gray (~0.196)
    // Slightly darker gray surfaces than pure white (per request), but still consistent with existing UI style.
    private static readonly Color PanelBg = new Color(0.88f, 0.88f, 0.88f, 0.97f);
    private static readonly Color HeaderBg = new Color(0.84f, 0.84f, 0.84f, 1f);
    private static readonly Color FooterBg = new Color(0.84f, 0.84f, 0.84f, 1f);
    private static readonly Color RowBg = new Color(0.90f, 0.90f, 0.90f, 1f);
    private static readonly Color ButtonPrimary = new Color(1f, 1f, 1f, 1f);
    private static readonly Color ButtonSecondary = new Color(1f, 1f, 1f, 1f);
    private static readonly Color InputFieldBg = new Color(0.97f, 0.97f, 0.97f, 1f);
    private static readonly Color TextDark = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextPlaceholder = new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.5f);
    private static readonly Color BackdropBg = new Color(0.12f, 0.12f, 0.12f, 0.65f);

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

    private static Sprite _checkmarkSprite;
    private static Sprite _dropdownArrowSprite;
    private static Sprite _uiBackgroundSprite;
    private static Color? _checkmarkColor;
    private static Color? _dropdownArrowColor;
    private static bool _spritesCached;

    private static bool _inputStyleCached;
    private static Color _cachedSelectionColor = new Color(0.25f, 0.5f, 1f, 0.35f);
    private static Color _cachedCaretColor = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
    private static int _cachedCaretWidth = 2;

    internal static void ConfigureInputFieldVisuals(TMP_InputField input, TMP_FontAsset fontOrNull)
    {
        if (input == null) return;

        CacheInputStyleFromExistingInputField();

        if (input.textComponent != null)
        {
            if (fontOrNull != null)
                input.textComponent.font = fontOrNull;
            input.textComponent.fontSize = BaseFontSize;
            input.textComponent.color = TextDark;

            // Prevent caret from getting clipped/invisible at the far right edge for long strings (e.g. endpoints).
            var m = input.textComponent.margin;
            input.textComponent.margin = new Vector4(m.x, m.y, Mathf.Max(m.z, 10f), m.w);
        }

        input.customCaretColor = true;
        // Be explicit (don't rely on cached values) - we want visible caret + selection even during early boot.
        input.caretColor = _cachedCaretColor.a <= 0.001f ? TextDark : _cachedCaretColor;
        input.caretWidth = _cachedCaretWidth > 0 ? _cachedCaretWidth : 2;
        input.selectionColor = _cachedSelectionColor.a <= 0.05f ? new Color(0.25f, 0.5f, 1f, 0.40f) : _cachedSelectionColor;

        if (input.placeholder is TextMeshProUGUI ph)
        {
            if (fontOrNull != null)
                ph.font = fontOrNull;
            ph.fontSize = BaseFontSize;
            ph.color = TextPlaceholder;
        }
    }

    private void EnsureInputFieldFixer(TMP_InputField input)
    {
        if (input == null) return;

        var fixer = input.GetComponent<LLMInputFieldVisualFixer>();
        if (fixer == null)
            fixer = input.gameObject.AddComponent<LLMInputFieldVisualFixer>();

        fixer.Set(input, _font);
    }

    /// <summary>
    /// Find and cache sprites from an existing TMP_Dropdown in the scene.
    /// This reuses the same Checkmark and DropdownArrow sprites that other UI uses.
    /// </summary>
    private static void CacheSpritesFromExistingDropdown()
    {
        if (_spritesCached) return;

        // IMPORTANT:
        // On first open, FindAnyObjectByType may return the dropdown we just created in this panel,
        // which often has null sprites (TMP_DefaultControls.Resources is empty). If we cache those,
        // selection/checkmarks will be invisible until something later forces a refresh.
        // So we scan all dropdowns and pick one OUTSIDE of this panel that has real sprites.
        TMP_Dropdown best = null;
        foreach (var dd in Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
        {
            if (dd == null) continue;
            if (_panelRoot != null && dd.transform.IsChildOf(_panelRoot.transform)) continue;

            var ddImg = dd.GetComponent<Image>();
            var arrowImg = dd.transform.Find("Arrow")?.GetComponent<Image>();
            Sprite arrowSprite = arrowImg != null ? arrowImg.sprite : null;

            Sprite checkSprite = null;
            Color? checkColor = null;
            if (dd.template != null)
            {
                foreach (var img in dd.template.GetComponentsInChildren<Image>(true))
                {
                    if (img != null && img.gameObject.name.Contains("Checkmark") && img.sprite != null)
                    {
                        checkSprite = img.sprite;
                        checkColor = img.color;
                        break;
                    }
                }
            }

            // Prefer a dropdown that has both arrow + checkmark sprites, plus a background sprite.
            bool hasBg = ddImg != null && ddImg.sprite != null;
            bool hasArrow = arrowSprite != null;
            bool hasCheck = checkSprite != null;
            if (hasBg && hasArrow && hasCheck)
            {
                best = dd;
                break;
            }

            // Fallback: keep the best partial match (background + arrow at least).
            if (best == null && hasBg && hasArrow)
                best = dd;
        }

        if (best == null)
        {
            // Don't mark cached; try again later (e.g. after UI finishes booting).
            return;
        }

        // Cache background sprite used by the existing UI dropdown (usually the built-in UI background)
        var ddBg = best.GetComponent<Image>();
        if (ddBg != null && ddBg.sprite != null)
            _uiBackgroundSprite = ddBg.sprite;

        // Find the Arrow image in the existing dropdown
        var arrowTransform = best.transform.Find("Arrow");
        if (arrowTransform != null)
        {
            var arrowImg = arrowTransform.GetComponent<Image>();
            if (arrowImg != null && arrowImg.sprite != null)
            {
                _dropdownArrowSprite = arrowImg.sprite;
                _dropdownArrowColor = arrowImg.color;
            }
        }

        // Find the Checkmark in the template
        if (best.template != null)
        {
            var checkmarks = best.template.GetComponentsInChildren<Image>(true);
            foreach (var img in checkmarks)
            {
                if (img.gameObject.name.Contains("Checkmark") && img.sprite != null)
                {
                    _checkmarkSprite = img.sprite;
                    _checkmarkColor = img.color;
                    break;
                }
            }
        }

        // Only lock caching once we have the important sprites.
        if (_uiBackgroundSprite != null && _dropdownArrowSprite != null && _checkmarkSprite != null)
            _spritesCached = true;
    }

    private static void CacheInputStyleFromExistingInputField()
    {
        if (_inputStyleCached) return;

        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (input == null) continue;
            if (_panelRoot != null && input.transform.IsChildOf(_panelRoot.transform)) continue;

            // Use the first real input field we find.
            _cachedSelectionColor = input.selectionColor;
            _cachedCaretWidth = input.caretWidth;
            _cachedCaretColor = input.customCaretColor ? input.caretColor : _cachedCaretColor;
            _inputStyleCached = true;
            break;
        }
    }

    private static void ApplyUISprite(Image img)
    {
        if (img == null) return;
        CacheSpritesFromExistingDropdown();
        if (_uiBackgroundSprite != null)
        {
            img.sprite = _uiBackgroundSprite;
            img.type = Image.Type.Sliced;
        }
    }

    private static void ApplySolidBackground(Image img, Color color)
    {
        if (img == null) return;
        img.sprite = null;
        img.type = Image.Type.Simple;
        img.color = color;
    }

    private void ApplyFontAndColor(GameObject go)
    {
        // Cache sprites from existing dropdowns in the scene
        CacheSpritesFromExistingDropdown();
        CacheInputStyleFromExistingInputField();

        foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (_font != null)
                t.font = _font;
            t.fontSize = Mathf.Max(BaseFontSize, t.fontSize);
            t.color = TextDark;
        }

        // Restyle default control Images to match existing UI.
        foreach (var img in go.GetComponentsInChildren<Image>(true))
        {
            string n = img.gameObject.name.ToLowerInvariant();

            // Selection checkmark - use sprite from existing UI
            if (n.Contains("checkmark"))
            {
                if (_checkmarkSprite != null)
                    img.sprite = _checkmarkSprite;
                img.color = _checkmarkColor ?? img.color;
                continue;
            }

            if (n.Contains("handle"))
            {
                continue;
            }

            // Arrow icon - use sprite from existing UI
            if (n.Contains("arrow"))
            {
                if (_dropdownArrowSprite != null)
                    img.sprite = _dropdownArrowSprite;
                img.color = _dropdownArrowColor ?? img.color;
                continue;
            }

            // Dropdown option list surfaces should be flat (no rounded rect / sliced sprite)
            // to match the rest of the app's dropdown lists.
            bool isDropdownListSurface =
                n.Contains("template") ||
                n.Contains("viewport") ||
                n.Contains("item background") ||
                n.Contains("dropdown list") ||
                n.Contains("content");

            if (isDropdownListSurface)
            {
                ApplySolidBackground(img, new Color(0.92f, 0.92f, 0.92f, 1f));
                continue;
            }

            // For control backgrounds created via TMP_DefaultControls, ensure we use the same UI sprite + white fill.
            bool isControlRoot = img.GetComponent<TMP_InputField>() != null || img.GetComponent<TMP_Dropdown>() != null;
            bool isDropdownTemplateSurface =
                n.Contains("template") || n.Contains("viewport") || n.Contains("item background") || n.Contains("content");

            if (isControlRoot || isDropdownTemplateSurface)
            {
                ApplyUISprite(img);
                img.color = InputFieldBg;
            }
        }

        // InputField text colors for existing UI theme
        foreach (var input in go.GetComponentsInChildren<TMP_InputField>(true))
        {
            // Apply now, and also install a fixer that reapplies on enable/select.
            ConfigureInputFieldVisuals(input, _font);
            EnsureInputFieldFixer(input);
        }

        // If TMP has already spawned selection caret graphics, ensure they match our caret color (especially on first open).
        foreach (var caret in go.GetComponentsInChildren<TMP_SelectionCaret>(true))
        {
            caret.color = _cachedCaretColor.a <= 0.001f ? TextDark : _cachedCaretColor;
        }

        // Dropdown text colors for light theme
        foreach (var dd in go.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dd.captionText != null)
            {
                dd.captionText.font = _font;
                dd.captionText.fontSize = BaseFontSize;
                dd.captionText.color = TextDark;
            }
            if (dd.itemText != null)
            {
                dd.itemText.font = _font;
                dd.itemText.fontSize = BaseFontSize;
                dd.itemText.color = TextDark;
            }
        }
    }

    private System.Collections.IEnumerator RestyleNextFrame()
    {
        // Wait one frame so TMP can finish internal initialization/layout,
        // then restyle again (fixes "first open after app start" missing caret/selection).
        yield return null;
        if (_panelRoot != null)
            ApplyFontAndColor(_panelRoot);

        // IMPORTANT:
        // On first open after app start, TMP_InputField can end up with caret/selection visuals not fully initialized
        // until the containing provider section is disabled/enabled once (which is what "switch provider" does).
        // We replicate that lifecycle here once to ensure caret + selection work immediately.
        if (_panelRoot != null)
            ForceReinitializeTMPInputFields();

        // If sprite caching failed during early boot, try one more time shortly after.
        yield return new WaitForSeconds(0.05f);
        if (_panelRoot != null)
        {
            ApplyFontAndColor(_panelRoot);
            ForceReinitializeTMPInputFields();
        }
    }

    private void ForceReinitializeTMPInputFields()
    {
        // Force TMP to rebuild caret/selection internals by toggling the component.
        // This mirrors what happens when switching providers (sections disable/enable).
        foreach (var input in _panelRoot.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input == null) continue;

            bool wasEnabled = input.enabled;
            input.enabled = false;
            input.enabled = true;

            // Reapply our visuals after TMP's OnEnable runs.
            ConfigureInputFieldVisuals(input, _font);
            EnsureInputFieldFixer(input);

            // Force text geometry/caret layout update.
            input.ForceLabelUpdate();

            // Ensure any spawned caret graphic uses our caret color.
            foreach (var caret in input.GetComponentsInChildren<TMP_SelectionCaret>(true))
                caret.color = input.caretColor;

            // Preserve original enabled state if it was disabled for some reason.
            if (!wasEnabled)
                input.enabled = false;
        }

        Canvas.ForceUpdateCanvases();
    }

    private void CreateUI()
    {
        _font = FindFont();
        _workingSettings = LLMSettingsManager.Get().GetSettingsClone();
        CacheSpritesFromExistingDropdown();

        // Canvas
        var canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panelRoot.AddComponent<GraphicRaycaster>();

        // Darken the background behind the modal, matching the rest of the app's dialog behavior.
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(_panelRoot.transform, false);
        var backdropRt = backdrop.AddComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.color = BackdropBg;
        backdropImg.raycastTarget = true; // block clicks to underlying UI while the modal is open

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

        // Ensure initial open gets correct caret/selection styling even during early boot.
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
        xTxt.fontSize = 15;
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
        ApplyUISprite(img);
        img.color = bg;
        var button = btn.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        // Match existing UI button tint behavior
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
        tmp.fontSize = 15;
        tmp.fontStyle = FontStyles.Bold;
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
        vpRt.offsetMax = new Vector2(-22, 0);
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
        const float rowHeight = 40f;
        const float labelWidth = 140f;
        const float pad = 12f;

        var row = new GameObject("ActiveProviderRow");
        row.transform.SetParent(parent, false);
        var rowImg = row.AddComponent<Image>();
        ApplyUISprite(rowImg);
        rowImg.color = RowBg;
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
        label.fontSize = 15;
        label.color = TextDark;
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
            _activeProviderDropdown.captionText.fontSize = BaseFontSize;
            _activeProviderDropdown.captionText.color = TextDark;
        }

        // Ensure item text uses our font
        if (_activeProviderDropdown.itemText != null)
        {
            _activeProviderDropdown.itemText.font = _font;
            _activeProviderDropdown.itemText.fontSize = BaseFontSize;
            _activeProviderDropdown.itemText.color = TextDark;
        }
    }

    private void OnProviderChanged(int index)
    {
        _workingSettings.activeProvider = (LLMProvider)index;
        UpdateVisibleProvider();
        if (_panelRoot != null)
            ApplyFontAndColor(_panelRoot);
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
        if (GameLogic.Get() != null)
        {
            GameLogic.Get().UpdateActiveLLMLabel();
        }
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

/// <summary>
/// Ensures TMP_InputField caret + selection remain visible on the first open after app start.
/// TMP can overwrite some visuals during initialization; reapplying on enable/select fixes it reliably.
/// </summary>
public class LLMInputFieldVisualFixer : MonoBehaviour, ISelectHandler
{
    private TMP_InputField _input;
    private TMP_FontAsset _font;

    public void Set(TMP_InputField input, TMP_FontAsset font)
    {
        _input = input;
        _font = font;
    }

    private void OnEnable()
    {
        LLMSettingsPanel.ConfigureInputFieldVisuals(_input, _font);
        StartCoroutine(ReapplyAfterTMPInit());
    }

    public void OnSelect(BaseEventData eventData)
    {
        LLMSettingsPanel.ConfigureInputFieldVisuals(_input, _font);
        StartCoroutine(ReapplyAfterTMPInit());

        // Ensure TMP is in "active editing" state immediately (helps caret creation on first open).
        if (_input != null)
            _input.ActivateInputField();
    }

    private System.Collections.IEnumerator ReapplyAfterTMPInit()
    {
        // TMP often creates its caret graphic late (end of frame). Reapply after it exists.
        yield return null;

        LLMSettingsPanel.ConfigureInputFieldVisuals(_input, _font);

        // If TMP has spawned a TMP_SelectionCaret graphic, ensure it uses our caret color.
        if (_input != null)
        {
            foreach (var caret in _input.GetComponentsInChildren<TMP_SelectionCaret>(true))
            {
                caret.color = _input.caretColor;
            }
        }
    }
}