using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Programmatic Server Settings panel. Dynamically creates all UI elements.
/// Each server can have its own panel instance, tracked by server ID.
/// </summary>
public class ServerSettingsPanel : MonoBehaviour
{
    // Track open panels by server ID
    private static Dictionary<int, ServerSettingsPanel> _openPanels = new Dictionary<int, ServerSettingsPanel>();

    private GameObject _panelRoot;
    private TMP_FontAsset _font;
    private RectTransform _mainPanel;

    // UI references (previously from prefab)
    private TMP_Dropdown _presetDropdown;
    private TMP_Dropdown _autoPicDropdown;
    private TMP_Text _titleText;
    private TMP_Text _settingsText;
    private TMP_InputField _jobListInputField;

    private int _serverID = -1;

    // Panel dimensions
    private const float PANEL_WIDTH = 680f;
    private const float PANEL_HEIGHT = 370f;
    private const float HEADER_HEIGHT = 36f;
    private const float BASE_FONT_SIZE = 14f;

    // Theme colors (matching existing panels)
    private static readonly Color PanelBg = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color HeaderBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color InputFieldBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color TextDark = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextTitle = new Color(0f, 0f, 0f, 1f);

    // Sprite caching
    private static Sprite _uiBackgroundSprite;
    private static Sprite _checkmarkSprite;
    private static Sprite _dropdownArrowSprite;
    private static Color? _checkmarkColor;
    private static Color? _dropdownArrowColor;
    private static bool _spritesCached;

    #region Static Methods

    public static void Show(int serverID)
    {
        if (_openPanels.TryGetValue(serverID, out var existingPanel))
        {
            // Panel exists - just make sure it's visible
            if (existingPanel._panelRoot != null)
            {
                existingPanel._panelRoot.SetActive(true);
                existingPanel.RefreshFromSettings();
                return;
            }
            else
            {
                // Panel root was destroyed, remove from dictionary
                _openPanels.Remove(serverID);
            }
        }

        // Create new panel
        var panelRoot = new GameObject("ServerSettingsPanel" + serverID);
        var panel = panelRoot.AddComponent<ServerSettingsPanel>();
        panel._panelRoot = panelRoot;
        panel._serverID = serverID;
        panel.CreateUI();

        _openPanels[serverID] = panel;
    }

    public static void Hide(int serverID)
    {
        if (_openPanels.TryGetValue(serverID, out var panel))
        {
            if (panel._panelRoot != null)
            {
                Destroy(panel._panelRoot);
            }
            _openPanels.Remove(serverID);
        }
    }

    public static void Toggle(int serverID)
    {
        if (_openPanels.TryGetValue(serverID, out var panel) && panel._panelRoot != null && panel._panelRoot.activeSelf)
        {
            Hide(serverID);
        }
        else
        {
            Show(serverID);
        }
    }

    public static ServerSettingsPanel Get(int serverID)
    {
        _openPanels.TryGetValue(serverID, out var panel);
        return panel;
    }

    #endregion

    #region Public Methods (existing API)

    public void AddEveryItemToJobList(ref List<string> joblist)
    {
        List<string> jobsToAdd = GetPicJobListAsListOfStrings();
        foreach (string job in jobsToAdd)
        {
            joblist.Add(job);
        }
    }

    public List<string> GetPicJobListAsListOfStrings()
    {
        if (_jobListInputField == null) return new List<string>();
        // Delegate to GameLogic which handles multi-line @end blocks
        return GameLogic.Get().GetPicJobListAsListOfStrings(_jobListInputField.text);
    }

    public void SetJobList(string joblist)
    {
        if (_jobListInputField != null)
            _jobListInputField.text = joblist;
    }

    public void AddJobToJobList(string job, ref string jobList)
    {
        if (jobList.Length > 0)
        {
            jobList += "\n";
        }
        jobList += job;
    }

    #endregion

    #region Unity Lifecycle

    private void OnDestroy()
    {
        if (_serverID >= 0 && _openPanels.ContainsKey(_serverID))
        {
            _openPanels.Remove(_serverID);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Hide(_serverID);
        }
    }

    #endregion

    #region UI Creation

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private static TMP_DefaultControls.Resources BuildTMPResources()
    {
        return new TMP_DefaultControls.Resources();
    }

    private static void CacheSpritesFromExistingDropdown()
    {
        if (_spritesCached) return;

        TMP_Dropdown best = null;
        foreach (var dd in Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
        {
            if (dd == null) continue;

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

            bool hasBg = ddImg != null && ddImg.sprite != null;
            bool hasArrow = arrowSprite != null;
            bool hasCheck = checkSprite != null;
            if (hasBg && hasArrow && hasCheck)
            {
                best = dd;
                break;
            }

            if (best == null && hasBg && hasArrow)
                best = dd;
        }

        if (best == null) return;

        var ddBg = best.GetComponent<Image>();
        if (ddBg != null && ddBg.sprite != null)
            _uiBackgroundSprite = ddBg.sprite;

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

        if (_uiBackgroundSprite != null && _dropdownArrowSprite != null && _checkmarkSprite != null)
            _spritesCached = true;
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

    private void ApplyFontAndColor(GameObject go)
    {
        CacheSpritesFromExistingDropdown();

        foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (_font != null)
                t.font = _font;
            t.fontSize = Mathf.Max(BASE_FONT_SIZE, t.fontSize);
            t.color = TextDark;
        }

        foreach (var img in go.GetComponentsInChildren<Image>(true))
        {
            string n = img.gameObject.name.ToLowerInvariant();

            if (n.Contains("checkmark"))
            {
                if (_checkmarkSprite != null)
                    img.sprite = _checkmarkSprite;
                img.color = _checkmarkColor ?? img.color;
                continue;
            }

            if (n.Contains("handle")) continue;

            if (n.Contains("arrow"))
            {
                if (_dropdownArrowSprite != null)
                    img.sprite = _dropdownArrowSprite;
                img.color = _dropdownArrowColor ?? img.color;
                continue;
            }

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

            bool isDropdownListSurface = isInsideDropdownTemplate &&
                (n.Contains("template") ||
                 n.Contains("viewport") ||
                 n.Contains("item background") ||
                 n.Contains("dropdown list") ||
                 n.Contains("content"));

            if (isDropdownListSurface)
            {
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = new Color(1f, 1f, 1f, 1f);
                continue;
            }

            bool isControlRoot = img.GetComponent<TMP_InputField>() != null || img.GetComponent<TMP_Dropdown>() != null;
            if (isControlRoot)
            {
                ApplyUISprite(img);
                img.color = InputFieldBg;
            }
        }

        foreach (var input in go.GetComponentsInChildren<TMP_InputField>(true))
        {
            ConfigureInputFieldVisuals(input);
        }

        foreach (var dd in go.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dd.captionText != null)
            {
                dd.captionText.font = _font;
                dd.captionText.fontSize = BASE_FONT_SIZE;
                dd.captionText.color = TextDark;
            }
            if (dd.itemText != null)
            {
                dd.itemText.font = _font;
                dd.itemText.fontSize = BASE_FONT_SIZE;
                dd.itemText.color = TextDark;
            }
        }
    }

    private void ConfigureInputFieldVisuals(TMP_InputField input)
    {
        if (input == null) return;

        if (input.textComponent != null)
        {
            if (_font != null)
                input.textComponent.font = _font;
            input.textComponent.fontSize = BASE_FONT_SIZE;
            input.textComponent.color = TextDark;
        }

        input.customCaretColor = true;
        input.caretColor = TextDark;
        input.caretWidth = 2;
        input.selectionColor = new Color(0.25f, 0.5f, 1f, 0.40f);

        if (input.placeholder is TextMeshProUGUI ph)
        {
            if (_font != null)
                ph.font = _font;
            ph.fontSize = BASE_FONT_SIZE;
            ph.color = new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.5f);
        }
    }

    private void CreateUI()
    {
        _font = FindFont();
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
        CreateContent();

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

        // Make header draggable using PanelDragHandler (from LLMSettingsPanel)
        var dragHandler = header.AddComponent<PanelDragHandler>();
        dragHandler.SetTarget(_mainPanel);

        // Title
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(header.transform, false);
        var titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.offsetMin = new Vector2(12, 0);
        titleRt.offsetMax = new Vector2(-40, 0);

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Server " + _serverID + " Settings";
        _titleText.font = _font;
        _titleText.fontSize = 18;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = TextTitle;
        _titleText.alignment = TextAlignmentOptions.MidlineLeft;

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
        closeBtn.onClick.AddListener(() => Hide(_serverID));

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

    private void CreateContent()
    {
        float yOffset = -HEADER_HEIGHT - 8;
        const float leftPad = 16f;
        const float rightPad = 16f;

        // Settings label (URL)
        var settingsLabel = new GameObject("SettingsLabel");
        settingsLabel.transform.SetParent(_mainPanel, false);
        var settingsRt = settingsLabel.AddComponent<RectTransform>();
        settingsRt.anchorMin = new Vector2(0, 1);
        settingsRt.anchorMax = new Vector2(1, 1);
        settingsRt.pivot = new Vector2(0, 1);
        settingsRt.offsetMin = new Vector2(leftPad, yOffset - 20);
        settingsRt.offsetMax = new Vector2(-rightPad, yOffset);

        _settingsText = settingsLabel.AddComponent<TextMeshProUGUI>();
        _settingsText.text = "URL: ";
        _settingsText.font = _font;
        _settingsText.fontSize = BASE_FONT_SIZE;
        _settingsText.color = TextDark;
        _settingsText.alignment = TextAlignmentOptions.MidlineLeft;

        yOffset -= 24;

        // Info label (help text) - needs more height for the long text
        const float infoHeight = 85f;
        var infoLabel = new GameObject("InfoLabel");
        infoLabel.transform.SetParent(_mainPanel, false);
        var infoRt = infoLabel.AddComponent<RectTransform>();
        infoRt.anchorMin = new Vector2(0, 1);
        infoRt.anchorMax = new Vector2(1, 1);
        infoRt.pivot = new Vector2(0, 1);
        infoRt.offsetMin = new Vector2(leftPad, yOffset - infoHeight);
        infoRt.offsetMax = new Vector2(-rightPad, yOffset);

        var infoText = infoLabel.AddComponent<TextMeshProUGUI>();
        infoText.text = "I don't know about you, but I have different video cards on different computers, each setup as a ComfyUI server. They each have different strengths and weaknesses, so these settings allow you to override the global defaults so one specific ComfyUI server can make videos, and the others images or whatever.\n\nSelecting a preset will have this job script override the main one, just for this server.";
        infoText.font = _font;
        infoText.fontSize = BASE_FONT_SIZE - 2;
        infoText.color = TextDark;
        infoText.alignment = TextAlignmentOptions.TopLeft;
        infoText.textWrappingMode = TextWrappingModes.Normal;
        infoText.overflowMode = TextOverflowModes.Truncate;

        yOffset -= (infoHeight + 8);

        // AutoPic Override label (above Preset for visibility)
        var autoPicLabelObj = new GameObject("AutoPicLabel");
        autoPicLabelObj.transform.SetParent(_mainPanel, false);
        var autoPicLabelRt = autoPicLabelObj.AddComponent<RectTransform>();
        autoPicLabelRt.anchorMin = new Vector2(0, 1);
        autoPicLabelRt.anchorMax = new Vector2(0, 1);
        autoPicLabelRt.pivot = new Vector2(0, 1);
        autoPicLabelRt.offsetMin = new Vector2(leftPad, yOffset - 24);
        autoPicLabelRt.offsetMax = new Vector2(leftPad + 100, yOffset);

        var autoPicLabelTmp = autoPicLabelObj.AddComponent<TextMeshProUGUI>();
        autoPicLabelTmp.text = "AutoPic Override:";
        autoPicLabelTmp.font = _font;
        autoPicLabelTmp.fontSize = BASE_FONT_SIZE;
        autoPicLabelTmp.color = TextDark;
        autoPicLabelTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // AutoPic Override dropdown
        var autoPicDdGo = TMP_DefaultControls.CreateDropdown(BuildTMPResources());
        autoPicDdGo.name = "AutoPicDropdown";
        autoPicDdGo.transform.SetParent(_mainPanel, false);
        ApplyFontAndColor(autoPicDdGo);

        var autoPicDdRt = autoPicDdGo.GetComponent<RectTransform>();
        autoPicDdRt.anchorMin = new Vector2(0, 1);
        autoPicDdRt.anchorMax = new Vector2(1, 1);
        autoPicDdRt.pivot = new Vector2(0, 1);
        autoPicDdRt.offsetMin = new Vector2(leftPad + 110, yOffset - 24);
        autoPicDdRt.offsetMax = new Vector2(-rightPad, yOffset);

        _autoPicDropdown = autoPicDdGo.GetComponent<TMP_Dropdown>();
        _autoPicDropdown.onValueChanged.AddListener(OnAutoPicDropdownChanged);

        // Make dropdown list taller
        if (_autoPicDropdown.template != null)
        {
            _autoPicDropdown.template.sizeDelta = new Vector2(_autoPicDropdown.template.sizeDelta.x, 150f);
        }

        yOffset -= 32;

        // Preset label (positioned directly in mainPanel, using same offset approach as dropdown)
        var presetLabelObj = new GameObject("PresetLabel");
        presetLabelObj.transform.SetParent(_mainPanel, false);
        var presetLabelRt = presetLabelObj.AddComponent<RectTransform>();
        presetLabelRt.anchorMin = new Vector2(0, 1);
        presetLabelRt.anchorMax = new Vector2(0, 1);
        presetLabelRt.pivot = new Vector2(0, 1);
        // Use offsetMin/offsetMax for consistent positioning with dropdown
        presetLabelRt.offsetMin = new Vector2(leftPad, yOffset - 24);
        presetLabelRt.offsetMax = new Vector2(leftPad + 55, yOffset);

        var presetLabelTmp = presetLabelObj.AddComponent<TextMeshProUGUI>();
        presetLabelTmp.text = "Preset:";
        presetLabelTmp.font = _font;
        presetLabelTmp.fontSize = BASE_FONT_SIZE;
        presetLabelTmp.color = TextDark;
        presetLabelTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Preset dropdown (positioned directly in mainPanel, next to label)
        var ddGo = TMP_DefaultControls.CreateDropdown(BuildTMPResources());
        ddGo.name = "PresetDropdown";
        ddGo.transform.SetParent(_mainPanel, false);
        ApplyFontAndColor(ddGo);

        var ddRt = ddGo.GetComponent<RectTransform>();
        ddRt.anchorMin = new Vector2(0, 1);
        ddRt.anchorMax = new Vector2(1, 1);
        ddRt.pivot = new Vector2(0, 1);
        // Stretch horizontal with proper offsets
        ddRt.offsetMin = new Vector2(leftPad + 55, yOffset - 24);
        ddRt.offsetMax = new Vector2(-rightPad, yOffset);

        _presetDropdown = ddGo.GetComponent<TMP_Dropdown>();
        _presetDropdown.onValueChanged.AddListener(OnPresetDropdownChanged);

        // Make dropdown list taller
        if (_presetDropdown.template != null)
        {
            _presetDropdown.template.sizeDelta = new Vector2(_presetDropdown.template.sizeDelta.x, 200f);
        }

        yOffset -= 32;

        // Job list input (multi-line)
        float jobListHeight = PANEL_HEIGHT - HEADER_HEIGHT - 8 - 24 - (85 + 8) - 32 - 32 - 16; // Remaining height (added -32 for AutoPic row)

        var jobListGo = TMP_DefaultControls.CreateInputField(BuildTMPResources());
        jobListGo.name = "JobListInput";
        jobListGo.transform.SetParent(_mainPanel, false);
        ApplyFontAndColor(jobListGo);

        var jobListRt = jobListGo.GetComponent<RectTransform>();
        jobListRt.anchorMin = new Vector2(0, 1);
        jobListRt.anchorMax = new Vector2(1, 1);
        jobListRt.pivot = new Vector2(0, 1);
        jobListRt.offsetMin = new Vector2(leftPad, yOffset - jobListHeight);
        jobListRt.offsetMax = new Vector2(-rightPad, yOffset);

        _jobListInputField = jobListGo.GetComponent<TMP_InputField>();
        _jobListInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        _jobListInputField.onValueChanged.AddListener((_) => OnJobListChanged());

        var jobListImg = jobListGo.GetComponent<Image>();
        ApplyUISprite(jobListImg);
        jobListImg.color = InputFieldBg;

        if (_jobListInputField.textComponent != null)
        {
            _jobListInputField.textComponent.font = _font;
            _jobListInputField.textComponent.fontSize = BASE_FONT_SIZE;
            _jobListInputField.textComponent.color = TextDark;
            _jobListInputField.textComponent.alignment = TextAlignmentOptions.TopLeft;
        }

        if (_jobListInputField.placeholder is TextMeshProUGUI ph)
        {
            ph.font = _font;
            ph.fontSize = BASE_FONT_SIZE;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.5f);
            ph.text = "The job script, if not blank, this will be used instead of the global one for this server specifically.";
            ph.alignment = TextAlignmentOptions.TopLeft;
            ph.textWrappingMode = TextWrappingModes.Normal;
        }

        // Fix the Text Area child
        var textArea = jobListGo.transform.Find("Text Area");
        if (textArea != null)
        {
            var textAreaRt = textArea.GetComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(8, 5);
            textAreaRt.offsetMax = new Vector2(-8, -5);
        }
    }

    private IEnumerator RestyleNextFrame()
    {
        yield return null;
        if (_panelRoot != null)
            ApplyFontAndColor(_panelRoot);
        Canvas.ForceUpdateCanvases();
    }

    #endregion

    #region Settings Logic

    private void RefreshFromSettings()
    {
        if (_serverID < 0) return;

        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        // Update title - include server name if available
        if (_titleText != null)
        {
            string title = "Server " + _serverID + " Settings";
            if (!string.IsNullOrEmpty(serverInfo._name))
            {
                title += " (" + serverInfo._name + ")";
            }
            _titleText.text = title;
        }

        // Update URL info
        if (_settingsText != null)
            _settingsText.text = "URL: " + serverInfo.remoteURL;

        // Populate preset dropdown
        if (_presetDropdown != null)
        {
            _presetDropdown.onValueChanged.RemoveListener(OnPresetDropdownChanged);
            PresetManager.Get().PopulatePresetDropdown(_presetDropdown, true);
            _presetDropdown.onValueChanged.AddListener(OnPresetDropdownChanged);
        }

        // Populate AutoPic dropdown
        RefreshAutoPicDropdown(serverInfo);

        // Set job list
        if (_jobListInputField != null)
            _jobListInputField.text = serverInfo._jobListOverride;
    }

    private void RefreshAutoPicDropdown(GPUInfo serverInfo)
    {
        if (_autoPicDropdown == null) return;

        _autoPicDropdown.onValueChanged.RemoveListener(OnAutoPicDropdownChanged);
        _autoPicDropdown.ClearOptions();

        // Add "use global" option first
        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("<use global>"));

        // Get AutoPic file names using shared method from GenerateSettingsPanel
        var fileNames = GenerateSettingsPanel.GetAutoPicFileNames();
        foreach (string fileName in fileNames)
        {
            options.Add(new TMP_Dropdown.OptionData(fileName));
        }

        _autoPicDropdown.AddOptions(options);

        // Select the current override value, or "<use global>" if none
        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(serverInfo._autoPicOverride))
        {
            for (int i = 0; i < _autoPicDropdown.options.Count; i++)
            {
                if (string.Equals(_autoPicDropdown.options[i].text, serverInfo._autoPicOverride, System.StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
        _autoPicDropdown.SetValueWithoutNotify(selectedIndex);
        _autoPicDropdown.RefreshShownValue();

        _autoPicDropdown.onValueChanged.AddListener(OnAutoPicDropdownChanged);
    }

    public void OnPresetDropdownChanged(int selectedIndex)
    {
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);

        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        // Get the text of the selected option
        string selected = _presetDropdown.options[_presetDropdown.value].text;

        if (selected == "<no selection>")
        {
            // Special case
            _jobListInputField.text = "";
            return;
        }

        var preset = PresetManager.Get().LoadPreset(selected, PresetManager.Get().GetActivePreset());
        _jobListInputField.text = preset.JobList;
    }

    public void OnJobListChanged()
    {
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        serverInfo._jobListOverride = _jobListInputField.text.Trim();
    }

    public void OnAutoPicDropdownChanged(int selectedIndex)
    {
        GPUInfo serverInfo = Config.Get().GetGPUInfo(_serverID);
        if (!Config.Get().IsValidGPU(_serverID))
        {
            RTConsole.Log("Invalid server ID " + _serverID);
            return;
        }

        string selected = _autoPicDropdown.options[_autoPicDropdown.value].text;
        
        if (selected == "<use global>")
        {
            serverInfo._autoPicOverride = "";
        }
        else
        {
            serverInfo._autoPicOverride = selected;
        }
    }

    // Legacy Init method - now just refreshes settings
    public void Init(int serverID)
    {
        _serverID = serverID;
        RefreshFromSettings();
    }

    #endregion
}
