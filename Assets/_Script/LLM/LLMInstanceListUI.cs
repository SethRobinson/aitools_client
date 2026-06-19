using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI component for displaying and managing the list of LLM instances.
/// Shows at the top of LLMSettingsPanel.
/// </summary>
public class LLMInstanceListUI
{
    public GameObject sectionRoot;
    
    // List display
    private ScrollRect _scrollRect;
    private RectTransform _contentRoot;
    private List<InstanceListItem> _listItems = new List<InstanceListItem>();
    
    // Buttons
    private Button _addButton;
    private Button _duplicateButton;
    private Button _moveUpButton;
    private Button _moveDownButton;
    private Button _removeButton;
    
    // Selected instance
    private int _selectedInstanceID = -1;
    
    // Callbacks
    public event Action<int> OnInstanceSelected;
    public event Action OnInstancesChanged;
    // Fired when a row's active checkbox is toggled (instanceID, isActive).
    public event Action<int, bool> OnInstanceActiveChanged;
    
    private readonly TMP_FontAsset _font;
    private readonly Action<GameObject> _styleApplier;
    
    // Theme colors (matching LLMSettingsPanel)
    private static readonly Color SectionBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color ListBg = new Color(0.90f, 0.90f, 0.92f, 1f);
    private static readonly Color ItemBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color ItemSelectedBg = new Color(0.7f, 0.85f, 1f, 1f);
    private static readonly Color ItemInactiveBg = new Color(0.86f, 0.86f, 0.88f, 1f);
    private static readonly Color TextDark = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
    private static readonly Color TextMuted = new Color(0.55f, 0.55f, 0.57f, 1f);
    private static readonly Color CheckColor = new Color(0.2f, 0.5f, 0.2f, 1f);
    private static readonly Color HeaderColor = new Color(0f, 0.45f, 0.70f, 1f);
    
    private const float LIST_HEIGHT = 120f;
    private const float ITEM_HEIGHT = 28f;
    
    public LLMInstanceListUI(TMP_FontAsset font, Action<GameObject> styleApplier)
    {
        _font = font;
        _styleApplier = styleApplier;
    }
    
    public GameObject Build(Transform parent, LLMInstancesConfig config)
    {
        sectionRoot = new GameObject("InstanceListSection");
        sectionRoot.transform.SetParent(parent, false);
        
        var sectionImg = sectionRoot.AddComponent<Image>();
        sectionImg.color = SectionBg;
        
        var vlg = sectionRoot.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        var csf = sectionRoot.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Header with title
        CreateHeader(sectionRoot.transform);
        
        // List container
        CreateListContainer(sectionRoot.transform);
        
        // Button row (Add/Duplicate/Move/Remove)
        CreateButtonRow(sectionRoot.transform);
        
        // Populate with initial data
        RefreshList(config);
        
        return sectionRoot;
    }
    
    private void CreateHeader(Transform parent)
    {
        var headerObj = new GameObject("Header");
        headerObj.transform.SetParent(parent, false);
        
        var headerLE = headerObj.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 24f;
        
        var headerText = headerObj.AddComponent<TextMeshProUGUI>();
        headerText.font = _font;
        headerText.text = "LLM Instances";
        headerText.fontSize = 16f;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = HeaderColor;
        headerText.alignment = TextAlignmentOptions.MidlineLeft;
    }
    
    private void CreateListContainer(Transform parent)
    {
        var listContainer = new GameObject("ListContainer");
        listContainer.transform.SetParent(parent, false);
        
        var containerLE = listContainer.AddComponent<LayoutElement>();
        containerLE.preferredHeight = LIST_HEIGHT;
        containerLE.flexibleWidth = 1f;
        
        var containerImg = listContainer.AddComponent<Image>();
        containerImg.color = ListBg;
        
        // ScrollView
        var scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(listContainer.transform, false);
        
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(4, 4);
        scrollRt.offsetMax = new Vector2(-4, -4);
        
        _scrollRect = scrollGo.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.scrollSensitivity = 20f;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        
        // Viewport (leave room for scrollbar on right)
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-14, 0); // Leave room for scrollbar
        
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = ListBg;
        
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        
        // Content
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        
        _contentRoot = content.AddComponent<RectTransform>();
        _contentRoot.anchorMin = new Vector2(0, 1);
        _contentRoot.anchorMax = new Vector2(1, 1);
        _contentRoot.pivot = new Vector2(0.5f, 1);
        _contentRoot.anchoredPosition = Vector2.zero;
        _contentRoot.sizeDelta = Vector2.zero;
        
        var contentVlg = content.AddComponent<VerticalLayoutGroup>();
        contentVlg.padding = new RectOffset(2, 2, 2, 2);
        contentVlg.spacing = 2;
        contentVlg.childControlWidth = true;
        contentVlg.childControlHeight = true;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;
        
        var contentCsf = content.AddComponent<ContentSizeFitter>();
        contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        _scrollRect.viewport = vpRt;
        _scrollRect.content = _contentRoot;
        
        // Vertical scrollbar
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(scrollGo.transform, false);
        
        var sbRt = sbGo.AddComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = new Vector2(1, 1);
        sbRt.pivot = new Vector2(1, 0.5f);
        sbRt.sizeDelta = new Vector2(12, 0);
        sbRt.anchoredPosition = Vector2.zero;
        
        var sbImg = sbGo.AddComponent<Image>();
        sbImg.color = new Color(0.22f, 0.22f, 0.24f, 1f);
        
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
        _scrollRect.verticalScrollbar = scrollbar;
        _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
    }
    
    private void CreateButtonRow(Transform parent)
    {
        var rowObj = new GameObject("ButtonRow");
        rowObj.transform.SetParent(parent, false);
        
        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 32f;
        
        var hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        
        // Add button (defaults to llama.cpp - user can change provider type below)
        _addButton = CreateButton(rowObj.transform, "+ Add", 70f, OnAddClicked);
        
        // Duplicate button (copies selected instance)
        _duplicateButton = CreateButton(rowObj.transform, "+ Duplicate", 90f, OnDuplicateClicked);

        // Move buttons (changes routing priority when utilization ties)
        _moveUpButton = CreateButton(rowObj.transform, "\u2191", 34f, OnMoveUpClicked);
        _moveDownButton = CreateButton(rowObj.transform, "\u2193", 34f, OnMoveDownClicked);
        
        // Remove button
        _removeButton = CreateButton(rowObj.transform, "- Remove", 80f, OnRemoveClicked);
        
        // Spacer
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(rowObj.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.flexibleWidth = 1f;
    }
    
    private Button CreateButton(Transform parent, string text, float width, Action onClick)
    {
        var btnObj = new GameObject("Btn_" + text);
        btnObj.transform.SetParent(parent, false);
        
        var le = btnObj.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        
        var img = btnObj.AddComponent<Image>();
        img.color = ItemBg;
        
        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());
        
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = 12f;
        tmp.color = TextDark;
        tmp.alignment = TextAlignmentOptions.Center;
        
        return btn;
    }
    
    private void OnAddClicked()
    {
        var manager = LLMInstanceManager.Get();
        if (manager != null)
        {
            // Default to llama.cpp - user can change the provider type in the settings below
            int newID = manager.AddInstance(LLMProvider.LlamaCpp);
            _selectedInstanceID = newID;
            OnInstancesChanged?.Invoke();
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(newID);
        }
    }
    
    private void OnDuplicateClicked()
    {
        if (_selectedInstanceID < 0) return;
        
        var manager = LLMInstanceManager.Get();
        if (manager != null)
        {
            // Get the selected instance
            var sourceInstance = manager.GetInstance(_selectedInstanceID);
            if (sourceInstance == null) return;
            
            // Clone it (this creates a deep copy with all settings)
            var clonedInstance = sourceInstance.Clone();
            
            // Append " (Copy)" to the name to differentiate
            clonedInstance.name = sourceInstance.name + " (Copy)";
            
            // AddInstance will assign a new ID automatically
            manager.AddInstance(clonedInstance);
            
            // Select the new instance
            _selectedInstanceID = clonedInstance.instanceID;
            OnInstancesChanged?.Invoke();
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(clonedInstance.instanceID);
        }
    }
    
    private void OnRemoveClicked()
    {
        if (_selectedInstanceID < 0) return;
        
        var manager = LLMInstanceManager.Get();
        if (manager != null)
        {
            manager.RemoveInstance(_selectedInstanceID);
            _selectedInstanceID = -1;
            
            // Select first remaining instance if any
            var instances = manager.GetAllInstances();
            if (instances.Count > 0)
            {
                _selectedInstanceID = instances[0].instanceID;
            }
            
            OnInstancesChanged?.Invoke();
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(_selectedInstanceID);
        }
    }

    private void OnMoveUpClicked()
    {
        MoveSelectedInstance(-1);
    }

    private void OnMoveDownClicked()
    {
        MoveSelectedInstance(1);
    }

    private void MoveSelectedInstance(int direction)
    {
        if (_selectedInstanceID < 0) return;

        var manager = LLMInstanceManager.Get();
        if (manager == null) return;

        if (manager.MoveInstance(_selectedInstanceID, direction))
        {
            OnInstancesChanged?.Invoke();
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(_selectedInstanceID);
        }
    }
    
    /// <summary>
    /// Refresh the list display with current config.
    /// </summary>
    public void RefreshList(LLMInstancesConfig config)
    {
        // Clear existing items
        foreach (var item in _listItems)
        {
            if (item.gameObject != null)
                UnityEngine.Object.Destroy(item.gameObject);
        }
        _listItems.Clear();
        
        if (config == null || config.instances == null)
        {
            _selectedInstanceID = -1;
            UpdateButtonStates();
            return;
        }
        
        // Create new items
        foreach (var inst in config.instances)
        {
            var item = CreateListItem(inst);
            _listItems.Add(item);
        }
        
        // Update selection visuals
        if (_selectedInstanceID >= 0 && GetSelectedListIndex() < 0)
            _selectedInstanceID = -1;
        
        // If no selection but we have items, select the first one
        if (_selectedInstanceID < 0 && config.instances.Count > 0)
        {
            _selectedInstanceID = config.instances[0].instanceID;
            OnInstanceSelected?.Invoke(_selectedInstanceID);
        }

        UpdateSelectionVisuals();
        UpdateButtonStates();
    }
    
    private InstanceListItem CreateListItem(LLMInstanceInfo instance)
    {
        var itemObj = new GameObject("Instance_" + instance.instanceID);
        itemObj.transform.SetParent(_contentRoot, false);
        
        var le = itemObj.AddComponent<LayoutElement>();
        le.preferredHeight = ITEM_HEIGHT;
        
        var img = itemObj.AddComponent<Image>();
        img.color = ItemBg;
        
        var btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = img;

        int capturedID = instance.instanceID;
        btn.onClick.AddListener(() => OnItemClicked(capturedID));

        // Active/inactive checkbox on the left so it's visible at a glance.
        var toggleGo = new GameObject("ActiveToggle");
        toggleGo.transform.SetParent(itemObj.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0, 0.5f);
        toggleRt.anchorMax = new Vector2(0, 0.5f);
        toggleRt.pivot = new Vector2(0, 0.5f);
        toggleRt.sizeDelta = new Vector2(18, 18);
        toggleRt.anchoredPosition = new Vector2(6, 0);

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(toggleGo.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = Color.white;

        var checkGo = new GameObject("Checkmark");
        checkGo.transform.SetParent(bgGo.transform, false);
        var checkRt = checkGo.AddComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        var checkTmp = checkGo.AddComponent<TextMeshProUGUI>();
        checkTmp.font = _font;
        checkTmp.fontSize = 14f;
        checkTmp.color = CheckColor;
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.text = "✓";

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkTmp;
        toggle.isOn = instance.isActive;
        toggle.onValueChanged.AddListener(isOn => OnRowActiveToggled(capturedID, isOn));

        // Text (shifted right to make room for the checkbox)
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(itemObj.transform, false);

        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(30, 0);
        textRt.offsetMax = new Vector2(-8, 0);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = instance.GetDisplayString();
        tmp.fontSize = 12f;
        tmp.color = TextDark;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        var listItem = new InstanceListItem
        {
            gameObject = itemObj,
            instanceID = instance.instanceID,
            image = img,
            text = tmp,
            activeToggle = toggle
        };
        ApplyActiveVisual(listItem, instance.isActive);
        return listItem;
    }

    private void OnRowActiveToggled(int instanceID, bool isActive)
    {
        // Update this row's visuals immediately.
        foreach (var item in _listItems)
        {
            if (item.instanceID == instanceID)
            {
                ApplyActiveVisual(item, isActive);
                break;
            }
        }
        OnInstanceActiveChanged?.Invoke(instanceID, isActive);
    }

    /// <summary>
    /// Gray out a row when its instance is inactive.
    /// </summary>
    private void ApplyActiveVisual(InstanceListItem item, bool isActive)
    {
        if (item.text != null)
            item.text.color = isActive ? TextDark : TextMuted;
        if (item.image != null && item.instanceID != _selectedInstanceID)
            item.image.color = isActive ? ItemBg : ItemInactiveBg;
    }
    
    private void OnItemClicked(int instanceID)
    {
        _selectedInstanceID = instanceID;
        UpdateSelectionVisuals();
        UpdateButtonStates();
        OnInstanceSelected?.Invoke(instanceID);
    }
    
    private void UpdateSelectionVisuals()
    {
        foreach (var item in _listItems)
        {
            if (item.text != null)
                item.text.color = (item.activeToggle == null || item.activeToggle.isOn) ? TextDark : TextMuted;

            if (item.image != null)
            {
                if (item.instanceID == _selectedInstanceID)
                    item.image.color = ItemSelectedBg;
                else
                    item.image.color = (item.activeToggle != null && !item.activeToggle.isOn) ? ItemInactiveBg : ItemBg;
            }
        }
    }
    
    /// <summary>
    /// Get the currently selected instance ID.
    /// </summary>
    public int GetSelectedInstanceID()
    {
        return _selectedInstanceID;
    }
    
    /// <summary>
    /// Set the selected instance ID.
    /// </summary>
    public void SetSelectedInstanceID(int id)
    {
        _selectedInstanceID = id;
        UpdateSelectionVisuals();
        UpdateButtonStates();
    }

    /// <summary>
    /// Reapply row colors after broad panel styling has reset TMP text colors.
    /// </summary>
    public void RefreshVisuals()
    {
        UpdateSelectionVisuals();
        UpdateButtonStates();
    }
    
    /// <summary>
    /// Update a specific item's display text.
    /// </summary>
    public void UpdateItemDisplay(LLMInstanceInfo instance)
    {
        foreach (var item in _listItems)
        {
            if (item.instanceID == instance.instanceID)
            {
                if (item.text != null)
                    item.text.text = instance.GetDisplayString();
                if (item.activeToggle != null)
                    item.activeToggle.SetIsOnWithoutNotify(instance.isActive);
                ApplyActiveVisual(item, instance.isActive);
                break;
            }
        }
    }

    private int GetSelectedListIndex()
    {
        for (int i = 0; i < _listItems.Count; i++)
        {
            if (_listItems[i].instanceID == _selectedInstanceID)
                return i;
        }
        return -1;
    }

    private void UpdateButtonStates()
    {
        int selectedIndex = GetSelectedListIndex();
        bool hasSelection = selectedIndex >= 0;

        if (_duplicateButton != null)
            _duplicateButton.interactable = hasSelection;
        if (_removeButton != null)
            _removeButton.interactable = hasSelection;
        if (_moveUpButton != null)
            _moveUpButton.interactable = hasSelection && selectedIndex > 0;
        if (_moveDownButton != null)
            _moveDownButton.interactable = hasSelection && selectedIndex < _listItems.Count - 1;
    }
    
    private class InstanceListItem
    {
        public GameObject gameObject;
        public int instanceID;
        public Image image;
        public TextMeshProUGUI text;
        public Toggle activeToggle;
    }
}

