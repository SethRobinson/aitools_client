using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;



public class ModelModItem
{
    public enum ModelType
    {
        LORA,
        EMBEDDING
    }
    public string name = "Unknown";
    public string alias;
    public string path;
    public string resolution;
    public string modelName;
    public GameObject gameObject;
    public List<string> exampleList= new List<string>();
    public ModelType type = ModelType.LORA;
   
}

public class ModelModManager : MonoBehaviour
{

    static ModelModManager _this;
    List<ModelModItem> modItems = new List<ModelModItem>();

    public GameObject _itemGUITemplate;
    Vector3 _vCurSpawnPos = new Vector3(0, 0, 0);
    Vector2 _vSpawnPadding = new Vector2(5, 5);
    Vector2 _vInitialSpawnPos;
    private Vector2? _itemSize = null;
    float _timeOffsetSecondsToLoadImage;
    const float _timeToWaitBetweenImageLoads = 0.1f;
    public Transform _contentTransform;
    public CanvasGroup _canvasGroup;

    // Start is called before the first frame update
    void Awake()
    {
        _this = this;
    }
    
    static public ModelModManager Get()
    {
        return _this;
    }

    void Start()
    {
        HideWindow();
    }

    public void HideWindow()
    {
        _canvasGroup.alpha = 0;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
    }

    public void ShowWindow()
    {
        _canvasGroup.alpha = 1;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
    }

    public void ToggleWindow()
    {
        if (_canvasGroup.alpha == 0)
        {
            ShowWindow();
        } else
        {
            HideWindow();
        }
    }
 
    public void CreateGUIRepresentationOfItem(ModelModItem item)
    {
        //create item using _itemGUITemplate
     
        Vector3 vNewPos = _vCurSpawnPos;

        GameObject newItem = Instantiate(_itemGUITemplate, _contentTransform.transform);
        item.gameObject = newItem;
        newItem.transform.localPosition = vNewPos;

        //get local size of the newItem GUI object we spawned
        
       _vCurSpawnPos.x += GetItemSize().x + _vSpawnPadding.x;
     
        if (_vCurSpawnPos.x > (GetPanelSize().x/2) - GetItemSize().x)
        {
            _vCurSpawnPos.x = _vInitialSpawnPos.x;
            _vCurSpawnPos.y -= (GetItemSize().y + _vSpawnPadding.y);
        }


        ModelModGUIItem itemGUI = newItem.GetComponent<ModelModGUIItem>();
        itemGUI.InitStuff(item, _timeOffsetSecondsToLoadImage);
        _timeOffsetSecondsToLoadImage += _timeToWaitBetweenImageLoads;

    }
    // Add an item to the list
    public void AddModItem(ModelModItem item)
    {
        modItems.Add(item);
        CreateGUIRepresentationOfItem(item);
    }

    public Vector2 GetItemSize()
    {
        if (_itemSize == null)
        {
            // Instantiate the item from the prefab
            GameObject itemInstance = Instantiate(_itemGUITemplate, gameObject.transform);

            // Now you can access runtime properties
            RectTransform rectTransform = itemInstance.GetComponent<RectTransform>();
            _itemSize = rectTransform.rect.size;

            // Remember to destroy the instantiated object
            Destroy(itemInstance);
        }

        return _itemSize.Value;
    }
    // Remove an item from the list by name

    public void ClearModGUIHighlights()
    {
        //cycle through our gameObject list and clear the highlight
        foreach (ModelModItem item in modItems)
        {
            ModelModGUIItem itemGUI = item.gameObject.GetComponent<ModelModGUIItem>();
            itemGUI.ClearHighlight();
        }
    }

    public void KillAllModGUIThings()
    {
        //cycle through our gameObject list and clear the highlight
        foreach (ModelModItem item in modItems)
        {
            Destroy(item.gameObject);
        }
    }
    public void ClearModItems()
    {
        const float topOffset = -17;

        KillAllModGUIThings();
        modItems.Clear();
        _vCurSpawnPos = new Vector3(- (GetPanelSize().x/2 - _vSpawnPadding.x),
            (GetPanelSize().y / 2) - (GetItemSize().y+ topOffset), 0);
     
        //compensate because we're centered, not upper left
        _vCurSpawnPos.x += GetItemSize().x / 2;
        _vInitialSpawnPos = _vCurSpawnPos;
    }

    //get size of this GUI panel in world coordinates
    public Vector2 GetPanelSize()
    {
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        return rectTransform.rect.size;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
