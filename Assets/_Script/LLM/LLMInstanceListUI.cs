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
    private Button _removeButton;
    
    // Selected instance
    private int _selectedInstanceID = -1;
    
    // Callbacks
    public event Action<int> OnInstanceSelected;
    public event Action OnInstancesChanged;
    
    private readonly TMP_FontAsset _font;
    private readonly Action<GameObject> _styleApplier;
    
    // Theme colors (matching LLMSettingsPanel)
    private static readonly Color SectionBg = new Color(0.75f, 0.75f, 0.77f, 1f);
    private static readonly Color ListBg = new Color(0.90f, 0.90f, 0.92f, 1f);
    private static readonly Color ItemBg = new Color(1f, 1f, 1f, 1f);
    private static readonly Color ItemSelectedBg = new Color(0.7f, 0.85f, 1f, 1f);
    private static readonly Color TextDark = new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
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
        
        // Button row (Add/Remove)
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
        
        // Viewport
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        
        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        
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
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(newID);
            OnInstancesChanged?.Invoke();
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
            
            RefreshList(manager.GetConfigClone());
            OnInstanceSelected?.Invoke(_selectedInstanceID);
            OnInstancesChanged?.Invoke();
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
        
        if (config == null || config.instances == null) return;
        
        // Create new items
        foreach (var inst in config.instances)
        {
            var item = CreateListItem(inst);
            _listItems.Add(item);
        }
        
        // Update selection visuals
        UpdateSelectionVisuals();
        
        // If no selection but we have items, select the first one
        if (_selectedInstanceID < 0 && config.instances.Count > 0)
        {
            _selectedInstanceID = config.instances[0].instanceID;
            UpdateSelectionVisuals();
            OnInstanceSelected?.Invoke(_selectedInstanceID);
        }
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
        
        // Text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(itemObj.transform, false);
        
        var textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8, 0);
        textRt.offsetMax = new Vector2(-8, 0);
        
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = instance.GetDisplayString();
        tmp.fontSize = 12f;
        tmp.color = TextDark;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        
        return new InstanceListItem
        {
            gameObject = itemObj,
            instanceID = instance.instanceID,
            image = img,
            text = tmp
        };
    }
    
    private void OnItemClicked(int instanceID)
    {
        _selectedInstanceID = instanceID;
        UpdateSelectionVisuals();
        OnInstanceSelected?.Invoke(instanceID);
    }
    
    private void UpdateSelectionVisuals()
    {
        foreach (var item in _listItems)
        {
            if (item.image != null)
            {
                item.image.color = (item.instanceID == _selectedInstanceID) ? ItemSelectedBg : ItemBg;
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
    }
    
    /// <summary>
    /// Update a specific item's display text.
    /// </summary>
    public void UpdateItemDisplay(LLMInstanceInfo instance)
    {
        foreach (var item in _listItems)
        {
            if (item.instanceID == instance.instanceID && item.text != null)
            {
                item.text.text = instance.GetDisplayString();
                break;
            }
        }
    }
    
    private class InstanceListItem
    {
        public GameObject gameObject;
        public int instanceID;
        public Image image;
        public TextMeshProUGUI text;
    }
}

