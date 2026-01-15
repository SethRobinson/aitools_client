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
    
    // Multi-instance support
    private LLMInstancesConfig _workingInstancesConfig;
    private LLMInstanceListUI _instanceListUI;
    private int _selectedInstanceID = -1;
    private TMP_Dropdown _jobModeDropdown;
    private GameObject _jobModeRow;
    private TMP_InputField _maxConcurrentInput;
    private GameObject _maxConcurrentRow;
    private TMP_InputField _displayNameInput;
    private GameObject _displayNameRow;

    private RectTransform _mainPanel;
    private TMP_Dropdown _activeProviderDropdown;

    private LLMProviderUI _openAIUI;
    private LLMProviderUI _anthropicUI;
    private LLMProviderUI _llamaCppUI;
    private LLMProviderUI _ollamaUI;
    private LLMProviderUI _geminiUI;
    private LLMProviderUI _openAICompatibleUI;

    private const float PANEL_WIDTH = 640f;
    private const float PANEL_HEIGHT = 620f; // Increased to fit instance list
    private const float HEADER_HEIGHT = 40f;
    private const float FOOTER_HEIGHT = 60f;
    private const float BaseFontSize = 14f;

    // Theme - pulled from existing UI prefabs / Main.unity:
    // - Panel backgrounds are white with slight alpha (uses built-in UI sprite 10907)
    // - Title text is black
    // - Body text is dark gray (~0.196)
    // Slightly darker gray surfaces than pure white (per request), but still consistent with existing UI style.
    // NOTE: These values are intentionally darker than UI-default light gray so this panel
    // matches the rest of the app's main UI panels.
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color FooterBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color RowBg = new Color(0.82f, 0.82f, 0.84f, 1f);
    private static readonly Color ButtonPrimary = new Color(1f, 1f, 1f, 1f);
    private static readonly Color ButtonSecondary = new Color(1f, 1f, 1f, 1f);
    // White so dropdowns/inputs pop against the background.
    private static readonly Color InputFieldBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0,0,0, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextPlaceholder = new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.5f);
    private static readonly Color BackdropBg = new Color(0.12f, 0.12f, 0.12f, 0.65f);

    // If true, show a dark backdrop and block clicks to underlying UI (modal behavior).
    // Default is false per request: do NOT dim the screen behind the panel.
    private const bool UseModalBackdrop = false;

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

            // Check if this image is part of a dropdown template (not our main panel's viewport/content).
            bool isInsideDropdownTemplate = false;
            Transform parent = img.transform.parent;
            while (parent != null)
            {
                string parentName = parent.name.ToLowerInvariant();
                if (parentName.Contains("template") && parent.GetComponentInParent<TMP_Dropdown>() != null)
                {
                    isInsideDropdownTemplate = true;
                    break;
                }
                parent = parent.parent;
            }

            // Dropdown option list surfaces should be flat (no rounded rect / sliced sprite)
            // to match the rest of the app's dropdown lists.
            bool isDropdownListSurface = isInsideDropdownTemplate &&
                (n.Contains("template") ||
                 n.Contains("viewport") ||
                 n.Contains("item background") ||
                 n.Contains("dropdown list") ||
                 n.Contains("content"));

            if (isDropdownListSurface)
            {
                // Keep dropdown list surface readable on top of PanelBg.
                ApplySolidBackground(img, new Color(1f, 1f, 1f, 1f));
                continue;
            }

            // For control backgrounds created via TMP_DefaultControls, ensure we use the same UI sprite + white fill.
            bool isControlRoot = img.GetComponent<TMP_InputField>() != null || img.GetComponent<TMP_Dropdown>() != null;

            if (isControlRoot)
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
        
        // Load instances config
        var instanceManager = LLMInstanceManager.Get();
        if (instanceManager != null)
        {
            _workingInstancesConfig = instanceManager.GetConfigClone();
        }
        else
        {
            _workingInstancesConfig = LLMInstancesConfig.CreateDefault();
        }
        
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

        // Optional modal backdrop (disabled by default).
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(_panelRoot.transform, false);
        var backdropRt = backdrop.AddComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.color = UseModalBackdrop ? BackdropBg : Color.clear;
        // Non-modal: don't dim and don't block clicks to underlying UI.
        backdropImg.raycastTarget = UseModalBackdrop;
        backdropImg.enabled = UseModalBackdrop;

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

        CreateFooterButton(footer.transform, "Apply and close", ButtonPrimary, -135f, 180f, OnApplyAndCloseClicked);
        CreateFooterButton(footer.transform, "Apply", ButtonPrimary, 10f, 90f, OnApplyClicked);
        CreateFooterButton(footer.transform, "Cancel", ButtonSecondary, 120f, 90f, Hide);
    }

    private void CreateFooterButton(Transform parent, string text, Color bg, float xOffset, float width, UnityEngine.Events.UnityAction onClick)
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

        // Instance list at the top
        _instanceListUI = new LLMInstanceListUI(_font, ApplyFontAndColor);
        _instanceListUI.Build(content.transform, _workingInstancesConfig);
        _instanceListUI.OnInstanceSelected += OnInstanceSelected;
        _instanceListUI.OnInstancesChanged += OnInstancesChanged;
        
        // Display Name row (for selected instance)
        CreateDisplayNameRow(content.transform);
        
        // Job Mode row (for selected instance)
        CreateJobModeRow(content.transform);
        
        // Max Concurrent Tasks row (for selected instance)
        CreateMaxConcurrentRow(content.transform);

        // Row: Active provider (explicit layout) - hidden when using multi-instance
        CreateActiveProviderRow(content.transform);

        // Provider panels
        _openAIUI = new LLMProviderUI(LLMProvider.OpenAI, _font, BuildTMPResources(), ApplyFontAndColor);
        _openAIUI.Build(content.transform, _workingSettings.openAI, false);

        _anthropicUI = new LLMProviderUI(LLMProvider.Anthropic, _font, BuildTMPResources(), ApplyFontAndColor);
        _anthropicUI.Build(content.transform, _workingSettings.anthropic, false);

        _llamaCppUI = new LLMProviderUI(LLMProvider.LlamaCpp, _font, BuildTMPResources(), ApplyFontAndColor);
        _llamaCppUI.Build(content.transform, _workingSettings.llamaCpp, true);
        if (_llamaCppUI.refreshModelsButton != null)
            _llamaCppUI.refreshModelsButton.onClick.AddListener(OnRefreshLlamaCppModel);

        _ollamaUI = new LLMProviderUI(LLMProvider.Ollama, _font, BuildTMPResources(), ApplyFontAndColor);
        _ollamaUI.Build(content.transform, _workingSettings.ollama, true);
        if (_ollamaUI.refreshModelsButton != null)
            _ollamaUI.refreshModelsButton.onClick.AddListener(OnRefreshOllamaModels);
        _ollamaUI.OnModelChanged += OnOllamaModelChanged;

        _geminiUI = new LLMProviderUI(LLMProvider.Gemini, _font, BuildTMPResources(), ApplyFontAndColor);
        _geminiUI.Build(content.transform, _workingSettings.gemini, false);

        _openAICompatibleUI = new LLMProviderUI(LLMProvider.OpenAICompatible, _font, BuildTMPResources(), ApplyFontAndColor);
        _openAICompatibleUI.Build(content.transform, _workingSettings.openAICompatible, true);
        if (_openAICompatibleUI.refreshModelsButton != null)
            _openAICompatibleUI.refreshModelsButton.onClick.AddListener(OnRefreshOpenAICompatibleModels);

        // Select the first instance if any, otherwise hide all provider UIs
        if (_workingInstancesConfig != null && _workingInstancesConfig.instances.Count > 0)
        {
            OnInstanceSelected(_workingInstancesConfig.instances[0].instanceID);
        }
        else
        {
            // No instances - hide all provider UIs and instance-specific rows
            HideAllProviderUIs();
            _displayNameRow?.SetActive(false);
            _jobModeRow?.SetActive(false);
            _maxConcurrentRow?.SetActive(false);
            if (_activeProviderDropdown?.transform.parent != null)
                _activeProviderDropdown.transform.parent.gameObject.SetActive(false);
        }
    }
    
    private void CreateDisplayNameRow(Transform parent)
    {
        const float rowHeight = 36f;
        const float labelWidth = 100f;
        const float pad = 12f;

        _displayNameRow = new GameObject("DisplayNameRow");
        _displayNameRow.transform.SetParent(parent, false);
        var rowImg = _displayNameRow.AddComponent<Image>();
        ApplyUISprite(rowImg);
        rowImg.color = RowBg;
        var rowLE = _displayNameRow.AddComponent<LayoutElement>();
        rowLE.preferredHeight = rowHeight;

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(_displayNameRow.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.sizeDelta = new Vector2(labelWidth, 0);
        labelRt.anchoredPosition = new Vector2(pad, 0);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = "Display Name:";
        label.fontSize = 14;
        label.color = TextDark;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Input field
        var inputGo = TMP_DefaultControls.CreateInputField(BuildTMPResources());
        inputGo.name = "DisplayNameInput";
        inputGo.transform.SetParent(_displayNameRow.transform, false);
        ApplyFontAndColor(inputGo);

        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(1, 1);
        inputRt.offsetMin = new Vector2(labelWidth + pad, 4f);
        inputRt.offsetMax = new Vector2(-pad, -4f);

        _displayNameInput = inputGo.GetComponent<TMP_InputField>();
        _displayNameInput.contentType = TMP_InputField.ContentType.Standard;
        _displayNameInput.text = "";
        _displayNameInput.onEndEdit.AddListener(OnDisplayNameChanged);

        if (_displayNameInput.textComponent != null)
        {
            _displayNameInput.textComponent.font = _font;
            _displayNameInput.textComponent.fontSize = 13;
            _displayNameInput.textComponent.color = TextDark;
        }

        // Hidden by default until an instance is selected
        _displayNameRow.SetActive(false);
    }
    
    private void OnDisplayNameChanged(string value)
    {
        if (_selectedInstanceID < 0) return;
        
        var instance = _workingInstancesConfig?.GetInstance(_selectedInstanceID);
        if (instance != null)
        {
            instance.name = value;
            _instanceListUI?.UpdateItemDisplay(instance);
        }
    }
    
    private void CreateJobModeRow(Transform parent)
    {
        const float rowHeight = 36f;
        const float labelWidth = 100f;
        const float pad = 12f;

        _jobModeRow = new GameObject("JobModeRow");
        _jobModeRow.transform.SetParent(parent, false);
        var rowImg = _jobModeRow.AddComponent<Image>();
        ApplyUISprite(rowImg);
        rowImg.color = RowBg;
        var rowLE = _jobModeRow.AddComponent<LayoutElement>();
        rowLE.preferredHeight = rowHeight;

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(_jobModeRow.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.sizeDelta = new Vector2(labelWidth, 0);
        labelRt.anchoredPosition = new Vector2(pad, 0);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = "Job Mode:";
        label.fontSize = 14;
        label.color = TextDark;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Dropdown
        var ddGo = TMP_DefaultControls.CreateDropdown(BuildTMPResources());
        ddGo.name = "JobModeDropdown";
        ddGo.transform.SetParent(_jobModeRow.transform, false);
        ApplyFontAndColor(ddGo);

        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 0);
        ddRt.anchorMax = new Vector2(0.5f, 1);
        ddRt.offsetMin = new Vector2(labelWidth + pad, 4f);
        ddRt.offsetMax = new Vector2(-pad, -4f);

        _jobModeDropdown = ddGo.GetComponent<TMP_Dropdown>();
        _jobModeDropdown.options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("Any"),
            new TMP_Dropdown.OptionData("Big Jobs Only"),
            new TMP_Dropdown.OptionData("Small Jobs Only"),
            new TMP_Dropdown.OptionData("Vision Jobs Only"),
            new TMP_Dropdown.OptionData("Non-Vision Only")
        };
        _jobModeDropdown.onValueChanged.AddListener(OnJobModeChanged);

        if (_jobModeDropdown.captionText != null)
        {
            _jobModeDropdown.captionText.font = _font;
            _jobModeDropdown.captionText.fontSize = 13;
            _jobModeDropdown.captionText.color = TextDark;
        }
        
        // Help text
        var helpObj = new GameObject("HelpText");
        helpObj.transform.SetParent(_jobModeRow.transform, false);
        var helpRt = helpObj.AddComponent<RectTransform>();
        helpRt.anchorMin = new Vector2(0.5f, 0);
        helpRt.anchorMax = new Vector2(1, 1);
        helpRt.offsetMin = new Vector2(pad, 0);
        helpRt.offsetMax = new Vector2(-pad, 0);

        var helpText = helpObj.AddComponent<TextMeshProUGUI>();
        helpText.font = _font;
        helpText.text = "(Vision=image analysis)";
        helpText.fontSize = 11;
        helpText.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        helpText.alignment = TextAlignmentOptions.MidlineLeft;
    }
    
    private void OnJobModeChanged(int index)
    {
        if (_selectedInstanceID < 0) return;
        
        var instance = _workingInstancesConfig?.GetInstance(_selectedInstanceID);
        if (instance != null)
        {
            instance.jobMode = (LLMJobMode)index;
            _instanceListUI?.UpdateItemDisplay(instance);
        }
    }
    
    private void CreateMaxConcurrentRow(Transform parent)
    {
        const float rowHeight = 36f;
        const float labelWidth = 140f;
        const float pad = 12f;

        _maxConcurrentRow = new GameObject("MaxConcurrentRow");
        _maxConcurrentRow.transform.SetParent(parent, false);
        var rowImg = _maxConcurrentRow.AddComponent<Image>();
        ApplyUISprite(rowImg);
        rowImg.color = RowBg;
        var rowLE = _maxConcurrentRow.AddComponent<LayoutElement>();
        rowLE.preferredHeight = rowHeight;

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(_maxConcurrentRow.transform, false);
        var labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(0, 1);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.sizeDelta = new Vector2(labelWidth, 0);
        labelRt.anchoredPosition = new Vector2(pad, 0);

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = "Max Concurrent:";
        label.fontSize = 14;
        label.color = TextDark;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Input field
        var inputGo = TMP_DefaultControls.CreateInputField(BuildTMPResources());
        inputGo.name = "MaxConcurrentInput";
        inputGo.transform.SetParent(_maxConcurrentRow.transform, false);
        ApplyFontAndColor(inputGo);

        var inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(0, 1);
        inputRt.pivot = new Vector2(0, 0.5f);
        inputRt.anchoredPosition = new Vector2(labelWidth + pad, 0);
        inputRt.sizeDelta = new Vector2(60, -8f);

        _maxConcurrentInput = inputGo.GetComponent<TMP_InputField>();
        _maxConcurrentInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _maxConcurrentInput.text = "1";
        _maxConcurrentInput.onEndEdit.AddListener(OnMaxConcurrentChanged);

        if (_maxConcurrentInput.textComponent != null)
        {
            _maxConcurrentInput.textComponent.font = _font;
            _maxConcurrentInput.textComponent.fontSize = 13;
            _maxConcurrentInput.textComponent.color = TextDark;
        }
        
        // Help text
        var helpObj = new GameObject("HelpText");
        helpObj.transform.SetParent(_maxConcurrentRow.transform, false);
        var helpRt = helpObj.AddComponent<RectTransform>();
        helpRt.anchorMin = new Vector2(0, 0);
        helpRt.anchorMax = new Vector2(1, 1);
        helpRt.pivot = new Vector2(0, 0.5f);
        helpRt.anchoredPosition = new Vector2(labelWidth + pad + 70, 0);
        helpRt.sizeDelta = new Vector2(-labelWidth - pad - 80, 0);

        var helpText = helpObj.AddComponent<TextMeshProUGUI>();
        helpText.font = _font;
        helpText.text = "(0 = disabled, 1+ = max parallel tasks)";
        helpText.fontSize = 11;
        helpText.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        helpText.alignment = TextAlignmentOptions.MidlineLeft;
    }
    
    private void OnMaxConcurrentChanged(string value)
    {
        if (_selectedInstanceID < 0) return;
        
        var instance = _workingInstancesConfig?.GetInstance(_selectedInstanceID);
        if (instance != null)
        {
            if (int.TryParse(value, out int maxTasks))
            {
                instance.maxConcurrentTasks = Mathf.Max(0, maxTasks); // 0 = disabled
                _maxConcurrentInput.text = instance.maxConcurrentTasks.ToString();
                _instanceListUI?.UpdateItemDisplay(instance);
            }
            else
            {
                // Reset to current value if parse fails
                _maxConcurrentInput.text = instance.maxConcurrentTasks.ToString();
            }
        }
    }
    
    private void OnInstanceSelected(int instanceID)
    {
        _selectedInstanceID = instanceID;
        
        var instance = _workingInstancesConfig?.GetInstance(instanceID);
        if (instance == null)
        {
            // No instance selected - hide provider UI
            _displayNameRow?.SetActive(false);
            _jobModeRow?.SetActive(false);
            _maxConcurrentRow?.SetActive(false);
            if (_activeProviderDropdown?.transform.parent != null)
                _activeProviderDropdown.transform.parent.gameObject.SetActive(false);
            HideAllProviderUIs();
            return;
        }
        
        // Show display name row
        _displayNameRow?.SetActive(true);
        if (_displayNameInput != null)
            _displayNameInput.text = instance.name ?? "";
        
        // Show job mode row
        _jobModeRow?.SetActive(true);
        if (_jobModeDropdown != null)
            _jobModeDropdown.value = (int)instance.jobMode;
        
        // Show max concurrent row
        _maxConcurrentRow?.SetActive(true);
        if (_maxConcurrentInput != null)
            _maxConcurrentInput.text = instance.maxConcurrentTasks.ToString();
        
        // Update provider dropdown
        if (_activeProviderDropdown != null)
        {
            _activeProviderDropdown.transform.parent.gameObject.SetActive(true);
            _activeProviderDropdown.SetValueWithoutNotify((int)instance.providerType);
        }
        
        // Update the working settings with instance settings
        UpdateWorkingSettingsFromInstance(instance);
        
        // Refresh the provider UIs
        RefreshProviderUIsFromInstance(instance);
        
        // Show only the relevant provider UI
        UpdateVisibleProviderForInstance(instance.providerType);
        
        // Auto-refresh models if the instance has no available models
        if (instance.settings.availableModels == null || instance.settings.availableModels.Count == 0)
        {
            if (instance.providerType == LLMProvider.LlamaCpp && !string.IsNullOrEmpty(instance.settings.endpoint))
            {
                AutoRefreshLlamaCppModel();
            }
            else if (instance.providerType == LLMProvider.Ollama && !string.IsNullOrEmpty(instance.settings.endpoint))
            {
                OnRefreshOllamaModels();
            }
            else if (instance.providerType == LLMProvider.OpenAICompatible && !string.IsNullOrEmpty(instance.settings.endpoint))
            {
                AutoRefreshOpenAICompatibleModels();
            }
        }
    }
    
    private void OnInstancesChanged()
    {
        // Reload working config from manager
        var manager = LLMInstanceManager.Get();
        if (manager != null)
        {
            _workingInstancesConfig = manager.GetConfigClone();
        }
    }
    
    private void UpdateWorkingSettingsFromInstance(LLMInstanceInfo instance)
    {
        if (instance?.settings == null) return;
        
        // Copy instance settings into the appropriate provider slot
        switch (instance.providerType)
        {
            case LLMProvider.OpenAI:
                _workingSettings.openAI = instance.settings.Clone();
                break;
            case LLMProvider.Anthropic:
                _workingSettings.anthropic = instance.settings.Clone();
                break;
            case LLMProvider.LlamaCpp:
                _workingSettings.llamaCpp = instance.settings.Clone();
                break;
            case LLMProvider.Ollama:
                _workingSettings.ollama = instance.settings.Clone();
                break;
            case LLMProvider.Gemini:
                _workingSettings.gemini = instance.settings.Clone();
                break;
            case LLMProvider.OpenAICompatible:
                _workingSettings.openAICompatible = instance.settings.Clone();
                break;
        }
        _workingSettings.activeProvider = instance.providerType;
    }
    
    private void RefreshProviderUIsFromInstance(LLMInstanceInfo instance)
    {
        if (instance?.settings == null) return;
        
        switch (instance.providerType)
        {
            case LLMProvider.OpenAI:
                _openAIUI?.UpdateFromSettings(instance.settings);
                break;
            case LLMProvider.Anthropic:
                _anthropicUI?.UpdateFromSettings(instance.settings);
                break;
            case LLMProvider.LlamaCpp:
                _llamaCppUI?.UpdateFromSettings(instance.settings);
                break;
            case LLMProvider.Ollama:
                _ollamaUI?.UpdateFromSettings(instance.settings);
                break;
            case LLMProvider.Gemini:
                _geminiUI?.UpdateFromSettings(instance.settings);
                break;
            case LLMProvider.OpenAICompatible:
                _openAICompatibleUI?.UpdateFromSettings(instance.settings);
                break;
        }
    }
    
    private void UpdateVisibleProviderForInstance(LLMProvider provider)
    {
        if (_openAIUI?.sectionRoot != null) _openAIUI.sectionRoot.SetActive(provider == LLMProvider.OpenAI);
        if (_anthropicUI?.sectionRoot != null) _anthropicUI.sectionRoot.SetActive(provider == LLMProvider.Anthropic);
        if (_llamaCppUI?.sectionRoot != null) _llamaCppUI.sectionRoot.SetActive(provider == LLMProvider.LlamaCpp);
        if (_ollamaUI?.sectionRoot != null) _ollamaUI.sectionRoot.SetActive(provider == LLMProvider.Ollama);
        if (_geminiUI?.sectionRoot != null) _geminiUI.sectionRoot.SetActive(provider == LLMProvider.Gemini);
        if (_openAICompatibleUI?.sectionRoot != null) _openAICompatibleUI.sectionRoot.SetActive(provider == LLMProvider.OpenAICompatible);
    }
    
    private void HideAllProviderUIs()
    {
        if (_openAIUI?.sectionRoot != null) _openAIUI.sectionRoot.SetActive(false);
        if (_anthropicUI?.sectionRoot != null) _anthropicUI.sectionRoot.SetActive(false);
        if (_llamaCppUI?.sectionRoot != null) _llamaCppUI.sectionRoot.SetActive(false);
        if (_ollamaUI?.sectionRoot != null) _ollamaUI.sectionRoot.SetActive(false);
        if (_geminiUI?.sectionRoot != null) _geminiUI.sectionRoot.SetActive(false);
        if (_openAICompatibleUI?.sectionRoot != null) _openAICompatibleUI.sectionRoot.SetActive(false);
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
            new TMP_Dropdown.OptionData("Gemini"),
            new TMP_Dropdown.OptionData("OpenAI Compatible"),
        };
        _activeProviderDropdown.onValueChanged.AddListener(OnProviderChanged);

        // Make the dropdown list tall enough and not absurd.
        var template = _activeProviderDropdown.template;
        if (template != null)
        {
            template.sizeDelta = new Vector2(template.sizeDelta.x, 190f);
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
        
        // Update selected instance's provider type
        if (_selectedInstanceID >= 0 && _workingInstancesConfig != null)
        {
            var instance = _workingInstancesConfig.GetInstance(_selectedInstanceID);
            if (instance != null)
            {
                instance.providerType = (LLMProvider)index;
                
                // Update the instance name to match the new provider
                switch ((LLMProvider)index)
                {
                    case LLMProvider.OpenAI:
                        instance.name = "OpenAI";
                        break;
                    case LLMProvider.Anthropic:
                        instance.name = "Anthropic";
                        break;
                    case LLMProvider.LlamaCpp:
                        instance.name = "llama.cpp";
                        break;
                    case LLMProvider.Ollama:
                        instance.name = "Ollama";
                        break;
                    case LLMProvider.Gemini:
                        instance.name = "Gemini";
                        break;
                    case LLMProvider.OpenAICompatible:
                        instance.name = "OpenAI Compatible";
                        break;
                }
                
                // Reset settings for the new provider type
                instance.settings = LLMInstanceInfo.CreateDefault((LLMProvider)index, 0).settings;
                RefreshProviderUIsFromInstance(instance);
                _instanceListUI?.UpdateItemDisplay(instance);
            }
        }
        
        UpdateVisibleProvider();
        if (_panelRoot != null)
            ApplyFontAndColor(_panelRoot);
        
        // Auto-refresh llama.cpp model when switching to it
        if ((LLMProvider)index == LLMProvider.LlamaCpp)
        {
            AutoRefreshLlamaCppModel();
        }
        
        // Auto-fetch Ollama model info when switching to it
        if ((LLMProvider)index == LLMProvider.Ollama && 
            !string.IsNullOrEmpty(_workingSettings.ollama.selectedModel))
        {
            FetchOllamaModelInfo();
        }
    }

    private void UpdateVisibleProvider()
    {
        var active = _workingSettings.activeProvider;
        if (_openAIUI?.sectionRoot != null) _openAIUI.sectionRoot.SetActive(active == LLMProvider.OpenAI);
        if (_anthropicUI?.sectionRoot != null) _anthropicUI.sectionRoot.SetActive(active == LLMProvider.Anthropic);
        if (_llamaCppUI?.sectionRoot != null) _llamaCppUI.sectionRoot.SetActive(active == LLMProvider.LlamaCpp);
        if (_ollamaUI?.sectionRoot != null) _ollamaUI.sectionRoot.SetActive(active == LLMProvider.Ollama);
        if (_geminiUI?.sectionRoot != null) _geminiUI.sectionRoot.SetActive(active == LLMProvider.Gemini);
        if (_openAICompatibleUI?.sectionRoot != null) _openAICompatibleUI.sectionRoot.SetActive(active == LLMProvider.OpenAICompatible);
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
            
            // Also update the selected instance's settings if it's an Ollama instance
            if (_selectedInstanceID >= 0 && _workingInstancesConfig != null)
            {
                var instance = _workingInstancesConfig.GetInstance(_selectedInstanceID);
                if (instance != null && instance.providerType == LLMProvider.Ollama)
                {
                    instance.settings.availableModels = new List<string>(models);
                    
                    // Keep selected model if it's in the list, otherwise select first
                    if (string.IsNullOrEmpty(instance.settings.selectedModel) || 
                        !instance.settings.availableModels.Contains(instance.settings.selectedModel))
                    {
                        instance.settings.selectedModel = models[0];
                    }
                }
            }
            
            RTQuickMessageManager.Get().ShowMessage("Found " + models.Count + " models");
            
            // Fetch model info for the selected model
            FetchOllamaModelInfo(endpoint);
        });
    }
    
    private void FetchOllamaModelInfo(string endpoint = null, string modelName = null)
    {
        if (endpoint == null)
            endpoint = _ollamaUI.endpointInput != null ? _ollamaUI.endpointInput.text : _workingSettings.ollama.endpoint;
        
        if (string.IsNullOrEmpty(modelName))
        {
            if (_ollamaUI.modelDropdown != null && _ollamaUI.modelDropdown.options.Count > 0)
                modelName = _ollamaUI.modelDropdown.options[_ollamaUI.modelDropdown.value].text;
        }
        
        if (string.IsNullOrEmpty(modelName) || modelName == "(no models)")
            return;
        
        OllamaModelInfoFetcher.Fetch(endpoint, modelName, (info, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("Failed to fetch Ollama model info: " + error);
                RTQuickMessageManager.Get().ShowMessage("Error: " + error);
                return;
            }

            if (info != null)
            {
                _ollamaUI.SetModelInfo(info);
                _workingSettings.ollama.maxContextLength = (int)info.contextLength;
            }
        });
    }
    
    private void OnOllamaModelChanged(string modelName)
    {
        // When the model changes in the dropdown, fetch the model info
        FetchOllamaModelInfo(null, modelName);
    }

    private void OnRefreshOpenAICompatibleModels()
    {
        RefreshOpenAICompatibleModelsInternal(true);
    }

    private void AutoRefreshOpenAICompatibleModels()
    {
        RefreshOpenAICompatibleModelsInternal(false);
    }

    private void RefreshOpenAICompatibleModelsInternal(bool showStartMessage)
    {
        string endpoint = _openAICompatibleUI.endpointInput != null ? _openAICompatibleUI.endpointInput.text : _workingSettings.openAICompatible.endpoint;
        
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length < 5)
        {
            return; // No endpoint configured
        }
        
        if (showStartMessage)
        {
            RTQuickMessageManager.Get().ShowMessage("Fetching models...");
        }

        // Use the OpenAI-compatible fetch which uses /v1/models (not /models like llama.cpp)
        LlamaCppModelFetcher.FetchOpenAICompatibleModels(endpoint, (modelsInfo, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                if (showStartMessage)
                {
                    RTQuickMessageManager.Get().ShowMessage("Error: " + error);
                }
                return;
            }

            if (modelsInfo == null || modelsInfo.modelIds.Count == 0)
            {
                if (showStartMessage)
                {
                    RTQuickMessageManager.Get().ShowMessage("No models found");
                }
                return;
            }

            // Get model names (use modelNames if available, otherwise modelIds)
            var modelList = modelsInfo.modelNames.Count > 0 ? modelsInfo.modelNames : modelsInfo.modelIds;
            
            // Update global settings with the models
            _workingSettings.openAICompatible.availableModels = new List<string>(modelList);
            
            // Keep selected model if it's in the list, otherwise select first
            if (string.IsNullOrEmpty(_workingSettings.openAICompatible.selectedModel) || 
                !_workingSettings.openAICompatible.availableModels.Contains(_workingSettings.openAICompatible.selectedModel))
            {
                _workingSettings.openAICompatible.selectedModel = modelList[0];
            }
            
            // Also update the selected instance's settings if it's an OpenAI Compatible instance
            if (_selectedInstanceID >= 0 && _workingInstancesConfig != null)
            {
                var instance = _workingInstancesConfig.GetInstance(_selectedInstanceID);
                if (instance != null && instance.providerType == LLMProvider.OpenAICompatible)
                {
                    instance.settings.availableModels = new List<string>(modelList);
                    
                    // Keep selected model if it's in the list, otherwise select first
                    if (string.IsNullOrEmpty(instance.settings.selectedModel) || 
                        !instance.settings.availableModels.Contains(instance.settings.selectedModel))
                    {
                        instance.settings.selectedModel = modelList[0];
                    }
                }
            }
            
            // Update UI
            _openAICompatibleUI.UpdateModelDropdown(_workingSettings.openAICompatible.availableModels, _workingSettings.openAICompatible.selectedModel);
            
            if (showStartMessage)
            {
                RTQuickMessageManager.Get().ShowMessage($"Found {modelList.Count} model(s)");
            }
        });
    }

    private void OnRefreshLlamaCppModel()
    {
        RefreshLlamaCppModelInternal(true);
    }

    /// <summary>
    /// Auto-refresh llama.cpp model silently (no message on start, only on completion).
    /// </summary>
    private void AutoRefreshLlamaCppModel()
    {
        RefreshLlamaCppModelInternal(false);
    }

    private void RefreshLlamaCppModelInternal(bool showStartMessage)
    {
        string endpoint = _llamaCppUI.endpointInput != null ? _llamaCppUI.endpointInput.text : _workingSettings.llamaCpp.endpoint;
        
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length < 5)
        {
            return; // No endpoint configured
        }
        
        if (showStartMessage)
        {
            RTQuickMessageManager.Get().ShowMessage("Fetching llama.cpp models...");
        }

        // Use the new multi-model fetch to support router mode
        LlamaCppModelFetcher.FetchModelsInfo(endpoint, (modelsInfo, error) =>
        {
            if (!string.IsNullOrEmpty(error))
            {
                if (showStartMessage)
                {
                    RTQuickMessageManager.Get().ShowMessage("Error: " + error);
                }
                return;
            }

            if (modelsInfo == null || modelsInfo.modelIds.Count == 0)
            {
                if (showStartMessage)
                {
                    RTQuickMessageManager.Get().ShowMessage("No models found");
                }
                return;
            }

            // Get model names (use modelNames if available, otherwise modelIds)
            var modelList = modelsInfo.modelNames.Count > 0 ? modelsInfo.modelNames : modelsInfo.modelIds;
            
            // Update global settings with the models
            _workingSettings.llamaCpp.availableModels = new List<string>(modelList);
            _workingSettings.llamaCpp.isRouterMode = modelsInfo.IsRouterMode;
            
            // Keep selected model if it's in the list, otherwise select first
            if (string.IsNullOrEmpty(_workingSettings.llamaCpp.selectedModel) || 
                !_workingSettings.llamaCpp.availableModels.Contains(_workingSettings.llamaCpp.selectedModel))
            {
                _workingSettings.llamaCpp.selectedModel = modelList[0];
            }
            
            // Also update the selected instance's settings if it's a llama.cpp instance
            if (_selectedInstanceID >= 0 && _workingInstancesConfig != null)
            {
                var instance = _workingInstancesConfig.GetInstance(_selectedInstanceID);
                if (instance != null && instance.providerType == LLMProvider.LlamaCpp)
                {
                    instance.settings.availableModels = new List<string>(modelList);
                    instance.settings.isRouterMode = modelsInfo.IsRouterMode;
                    
                    // Keep selected model if it's in the list, otherwise select first
                    if (string.IsNullOrEmpty(instance.settings.selectedModel) || 
                        !instance.settings.availableModels.Contains(instance.settings.selectedModel))
                    {
                        instance.settings.selectedModel = modelList[0];
                    }
                }
            }
            
            // Update UI
            _llamaCppUI.UpdateModelDropdown(_workingSettings.llamaCpp.availableModels, _workingSettings.llamaCpp.selectedModel);
            _llamaCppUI.SetRouterMode(modelsInfo.IsRouterMode, modelList.Count);
            
            // Also update the manager so it persists
            LLMSettingsManager.Get().SetLlamaCppModels(modelsInfo);
            
            if (showStartMessage)
            {
                string modeStr = modelsInfo.IsRouterMode ? "Router Mode" : "Single Model";
                RTQuickMessageManager.Get().ShowMessage($"Found {modelList.Count} model(s) ({modeStr})");
            }
        });
    }

    private void ApplySettings()
    {
        // Apply provider UI settings to the currently selected instance
        if (_selectedInstanceID >= 0 && _workingInstancesConfig != null)
        {
            var instance = _workingInstancesConfig.GetInstance(_selectedInstanceID);
            if (instance != null)
            {
                // Get settings from the appropriate provider UI
                switch (instance.providerType)
                {
                    case LLMProvider.OpenAI:
                        _openAIUI?.ApplyToSettings(instance.settings);
                        break;
                    case LLMProvider.Anthropic:
                        _anthropicUI?.ApplyToSettings(instance.settings);
                        break;
                    case LLMProvider.LlamaCpp:
                        _llamaCppUI?.ApplyToSettings(instance.settings);
                        break;
                    case LLMProvider.Ollama:
                        _ollamaUI?.ApplyToSettings(instance.settings);
                        break;
                    case LLMProvider.Gemini:
                        _geminiUI?.ApplyToSettings(instance.settings);
                        break;
                    case LLMProvider.OpenAICompatible:
                        _openAICompatibleUI?.ApplyToSettings(instance.settings);
                        break;
                }
            }
        }
        
        // Save instances to manager
        var instanceManager = LLMInstanceManager.Get();
        if (instanceManager != null && _workingInstancesConfig != null)
        {
            instanceManager.ApplyConfig(_workingInstancesConfig);
        }
        
        // Also update legacy settings for backward compatibility
        _openAIUI.ApplyToSettings(_workingSettings.openAI);
        _anthropicUI.ApplyToSettings(_workingSettings.anthropic);
        _llamaCppUI.ApplyToSettings(_workingSettings.llamaCpp);
        _ollamaUI.ApplyToSettings(_workingSettings.ollama);
        _geminiUI.ApplyToSettings(_workingSettings.gemini);
        _openAICompatibleUI.ApplyToSettings(_workingSettings.openAICompatible);

        LLMSettingsManager.Get().ApplySettings(_workingSettings);
        if (GameLogic.Get() != null)
        {
            GameLogic.Get().UpdateActiveLLMLabel();
        }
        RTQuickMessageManager.Get().ShowMessage("LLM settings saved");
    }

    private void OnApplyClicked()
    {
        ApplySettings();
    }

    private void OnApplyAndCloseClicked()
    {
        ApplySettings();
        Hide();
    }

    private void RefreshFromSettings()
    {
        _workingSettings = LLMSettingsManager.Get().GetSettingsClone();
        
        // Refresh instance config
        var instanceManager = LLMInstanceManager.Get();
        if (instanceManager != null)
        {
            _workingInstancesConfig = instanceManager.GetConfigClone();
            _instanceListUI?.RefreshList(_workingInstancesConfig);
            
            // Select first instance if none selected, or hide UI if no instances exist
            if (_workingInstancesConfig.instances.Count == 0)
            {
                // No instances - hide all provider UIs and instance-specific rows
                _selectedInstanceID = -1;
                HideAllProviderUIs();
                _displayNameRow?.SetActive(false);
                _jobModeRow?.SetActive(false);
                _maxConcurrentRow?.SetActive(false);
                if (_activeProviderDropdown?.transform.parent != null)
                    _activeProviderDropdown.transform.parent.gameObject.SetActive(false);
                return; // Don't continue with provider UI updates
            }
            else if (_selectedInstanceID < 0)
            {
                OnInstanceSelected(_workingInstancesConfig.instances[0].instanceID);
            }
            else
            {
                OnInstanceSelected(_selectedInstanceID);
            }
        }

        if (_activeProviderDropdown != null)
            _activeProviderDropdown.value = (int)_workingSettings.activeProvider;

        _openAIUI?.UpdateFromSettings(_workingSettings.openAI);
        _anthropicUI?.UpdateFromSettings(_workingSettings.anthropic);
        _llamaCppUI?.UpdateFromSettings(_workingSettings.llamaCpp);
        _ollamaUI?.UpdateFromSettings(_workingSettings.ollama);
        _geminiUI?.UpdateFromSettings(_workingSettings.gemini);
        _openAICompatibleUI?.UpdateFromSettings(_workingSettings.openAICompatible);

        UpdateVisibleProvider();
        
        // Auto-refresh llama.cpp model if it's the active provider
        if (_workingSettings.activeProvider == LLMProvider.LlamaCpp)
        {
            AutoRefreshLlamaCppModel();
        }
        
        // Auto-fetch Ollama model info if Ollama is the active provider and has a selected model
        if (_workingSettings.activeProvider == LLMProvider.Ollama && 
            !string.IsNullOrEmpty(_workingSettings.ollama.selectedModel))
        {
            FetchOllamaModelInfo();
        }
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