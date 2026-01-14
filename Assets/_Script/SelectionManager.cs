using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Handles marquee (rubber-band) selection of Pics and Adventure texts.
/// Left-click and drag from empty space to create a selection rectangle.
/// Ctrl+Left-click on items to add/remove them from selection individually.
/// Press Delete to remove selected items (respects locked state).
/// </summary>
public class SelectionManager : MonoBehaviour
{
    // Singleton access
    private static SelectionManager _instance;
    public static SelectionManager Get() => _instance;

    // Selection state
    private HashSet<GameObject> _selectedItems = new HashSet<GameObject>();
    private bool _isDragging = false;
    private Vector3 _dragStartWorldPos;
    private Vector3 _dragCurrentWorldPos;
    private Vector2 _dragStartScreenPos;

    // UI for selection rectangle
    private Canvas _selectionCanvas;
    private RectTransform _selectionRect;
    private Image _selectionRectImage;

    // Configuration
    [SerializeField] private Color _selectionRectColor = new Color(0.3f, 0.6f, 1f, 0.25f);
    [SerializeField] private Color _selectionRectBorderColor = new Color(0.3f, 0.6f, 1f, 0.8f);

    private Camera _camera;

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            _camera = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
        }
        CreateSelectionRectUI();
    }

    private void CreateSelectionRectUI()
    {
        // Create a screen-space overlay canvas for the selection rectangle
        GameObject canvasGO = new GameObject("SelectionCanvas");
        canvasGO.transform.SetParent(transform);
        _selectionCanvas = canvasGO.AddComponent<Canvas>();
        _selectionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _selectionCanvas.sortingOrder = 1000; // Above most UI

        // Add a CanvasScaler for consistent sizing
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Create the selection rectangle image
        GameObject rectGO = new GameObject("SelectionRect");
        rectGO.transform.SetParent(canvasGO.transform);
        _selectionRect = rectGO.AddComponent<RectTransform>();
        _selectionRectImage = rectGO.AddComponent<Image>();
        _selectionRectImage.color = _selectionRectColor;
        _selectionRectImage.raycastTarget = false;

        // Add outline effect for border
        var outline = rectGO.AddComponent<Outline>();
        outline.effectColor = _selectionRectBorderColor;
        outline.effectDistance = new Vector2(2, 2);

        // Initially hide
        _selectionRectImage.enabled = false;
    }

    private void Update()
    {
        HandleSelectionInput();
        HandleDeleteInput();
    }

    private void HandleSelectionInput()
    {
        // Check for left mouse button down to start drag
        if (Input.GetMouseButtonDown(0) && !_isDragging)
        {
            // Don't start selection if over UI
            if (EventSystem.current.IsPointerOverGameObject())
                return;

            // Check for Ctrl+Click to toggle selection on items
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            
            if (ctrlHeld)
            {
                // Ctrl+Click on a Pic toggles its selection
                GameObject clickedPic = GetPicUnderMouse();
                if (clickedPic != null)
                {
                    ToggleSelection(clickedPic);
                    return;
                }

                // Ctrl+Click on an Adventure text toggles its selection
                GameObject clickedText = GetAdventureTextUnderMouse();
                if (clickedText != null)
                {
                    ToggleSelection(clickedText);
                    return;
                }
            }

            // Don't start selection if over a Pic
            if (IsPicUnderMouse())
                return;

            // Don't start selection if over an Adventure text
            if (IsAdventureTextUnderMouse())
                return;

            // Start selection drag
            StartDrag();
        }

        // Update drag
        if (_isDragging && Input.GetMouseButton(0))
        {
            UpdateDrag();
        }

        // End drag on mouse up
        if (_isDragging && Input.GetMouseButtonUp(0))
        {
            EndDrag();
        }

        // Clear selection on click elsewhere (when not starting a new drag)
        // Don't clear if Ctrl is held (allows Ctrl+Click on empty space without clearing)
        if (Input.GetMouseButtonDown(0) && !_isDragging && _selectedItems.Count > 0)
        {
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld)
                return;
                
            if (!EventSystem.current.IsPointerOverGameObject())
            {
                // Check if clicking on a selected item - if so, don't clear
                GameObject clickedPic = GetPicUnderMouse();
                if (clickedPic != null && _selectedItems.Contains(clickedPic))
                    return;

                GameObject clickedText = GetAdventureTextUnderMouse();
                if (clickedText != null && _selectedItems.Contains(clickedText))
                    return;

                ClearSelection();
            }
        }
    }
    
    /// <summary>
    /// Toggles an item's selection state (adds if not selected, removes if selected).
    /// Used for Ctrl+Click functionality.
    /// </summary>
    private void ToggleSelection(GameObject go)
    {
        if (_selectedItems.Contains(go))
        {
            RemoveFromSelection(go);
            if (_selectedItems.Count > 0)
            {
                RTQuickMessageManager.Get().ShowMessage($"{_selectedItems.Count} item(s) selected");
            }
            else
            {
                RTQuickMessageManager.Get().ShowMessage("Selection cleared");
            }
        }
        else
        {
            AddToSelection(go);
            RTQuickMessageManager.Get().ShowMessage($"{_selectedItems.Count} item(s) selected - Press Delete to remove");
        }
    }

    private void StartDrag()
    {
        _isDragging = true;
        _dragStartScreenPos = Input.mousePosition;
        _dragStartWorldPos = _camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0));
        _dragCurrentWorldPos = _dragStartWorldPos;

        // Clear previous selection when starting new drag
        ClearSelection();

        _selectionRectImage.enabled = true;
        UpdateSelectionRectUI();
    }

    private void UpdateDrag()
    {
        _dragCurrentWorldPos = _camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0));
        UpdateSelectionRectUI();
    }

    private void EndDrag()
    {
        _isDragging = false;
        _selectionRectImage.enabled = false;

        // Only select if we dragged a meaningful distance
        Vector2 dragDistance = (Vector2)Input.mousePosition - _dragStartScreenPos;
        if (dragDistance.magnitude < 5f)
        {
            return; // Too small, treat as click not drag
        }

        // Find and select items within the selection bounds
        SelectItemsInRect();
    }

    private void UpdateSelectionRectUI()
    {
        // Convert world positions to screen positions
        Vector2 startScreen = _dragStartScreenPos;
        Vector2 currentScreen = Input.mousePosition;

        // Calculate rect in screen space
        float minX = Mathf.Min(startScreen.x, currentScreen.x);
        float maxX = Mathf.Max(startScreen.x, currentScreen.x);
        float minY = Mathf.Min(startScreen.y, currentScreen.y);
        float maxY = Mathf.Max(startScreen.y, currentScreen.y);

        _selectionRect.position = new Vector3(minX, minY, 0);
        _selectionRect.sizeDelta = new Vector2(maxX - minX, maxY - minY);
        _selectionRect.pivot = Vector2.zero;
        _selectionRect.anchorMin = Vector2.zero;
        _selectionRect.anchorMax = Vector2.zero;
    }

    private void SelectItemsInRect()
    {
        // Calculate world-space bounds of selection
        Rect selectionWorldRect = GetWorldSelectionRect();

        // Find all Pics
        var picsParent = RTUtil.FindObjectOrCreate("Pics").transform;
        var picScripts = picsParent.GetComponentsInChildren<PicMain>();

        foreach (var picScript in picScripts)
        {
            if (picScript == null || picScript.IsDestroyed())
                continue;

            // Get the SpriteRenderer bounds
            var spriteRenderer = picScript.m_pic;
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;

            Bounds picBounds = spriteRenderer.bounds;
            Rect picRect = new Rect(
                picBounds.min.x,
                picBounds.min.y,
                picBounds.size.x,
                picBounds.size.y
            );

            if (RectsOverlap(selectionWorldRect, picRect))
            {
                AddToSelection(picScript.gameObject);
            }
        }

        // Find all Adventure texts
        var adventuresParent = RTUtil.FindObjectOrCreate("Adventures").transform;
        var adventureTexts = adventuresParent.GetComponentsInChildren<AdventureText>();

        foreach (var adventureText in adventureTexts)
        {
            if (adventureText == null)
                continue;

            // Get world bounds from the panel transform
            Rect textWorldRect = GetAdventureTextWorldRect(adventureText);

            if (RectsOverlap(selectionWorldRect, textWorldRect))
            {
                AddToSelection(adventureText.gameObject);
            }
        }

        // Show message with selection count
        if (_selectedItems.Count > 0)
        {
            RTQuickMessageManager.Get().ShowMessage($"Selected {_selectedItems.Count} item(s) - Press Delete to remove");
        }
    }

    private Rect GetWorldSelectionRect()
    {
        float minX = Mathf.Min(_dragStartWorldPos.x, _dragCurrentWorldPos.x);
        float maxX = Mathf.Max(_dragStartWorldPos.x, _dragCurrentWorldPos.x);
        float minY = Mathf.Min(_dragStartWorldPos.y, _dragCurrentWorldPos.y);
        float maxY = Mathf.Max(_dragStartWorldPos.y, _dragCurrentWorldPos.y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private Rect GetAdventureTextWorldRect(AdventureText adventureText)
    {
        // Get the panel transform which has the RectTransform
        Transform panelTransform = adventureText._panelTransform;
        if (panelTransform == null)
        {
            // Fallback to the gameobject's position
            Vector3 pos = adventureText.transform.position;
            return new Rect(pos.x - 1f, pos.y - 1f, 2f, 2f);
        }

        RectTransform rectTransform = panelTransform.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Vector3 pos = adventureText.transform.position;
            return new Rect(pos.x - 1f, pos.y - 1f, 2f, 2f);
        }

        // Get world corners
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        // corners[0] = bottom-left, corners[1] = top-left, corners[2] = top-right, corners[3] = bottom-right
        float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
        float maxY = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private bool RectsOverlap(Rect a, Rect b)
    {
        return a.xMin < b.xMax && a.xMax > b.xMin &&
               a.yMin < b.yMax && a.yMax > b.yMin;
    }

    private void AddToSelection(GameObject go)
    {
        if (_selectedItems.Add(go))
        {
            // Notify the item it's selected
            var picMain = go.GetComponent<PicMain>();
            if (picMain != null)
            {
                picMain.SetSelected(true);
                return;
            }

            var adventureText = go.GetComponent<AdventureText>();
            if (adventureText != null)
            {
                adventureText.SetSelected(true);
            }
        }
    }

    private void RemoveFromSelection(GameObject go)
    {
        if (_selectedItems.Remove(go))
        {
            // Notify the item it's deselected
            var picMain = go.GetComponent<PicMain>();
            if (picMain != null)
            {
                picMain.SetSelected(false);
                return;
            }

            var adventureText = go.GetComponent<AdventureText>();
            if (adventureText != null)
            {
                adventureText.SetSelected(false);
            }
        }
    }

    public void ClearSelection()
    {
        foreach (var go in _selectedItems)
        {
            if (go == null)
                continue;

            var picMain = go.GetComponent<PicMain>();
            if (picMain != null)
            {
                picMain.SetSelected(false);
                continue;
            }

            var adventureText = go.GetComponent<AdventureText>();
            if (adventureText != null)
            {
                adventureText.SetSelected(false);
            }
        }
        _selectedItems.Clear();
    }

    private void HandleDeleteInput()
    {
        // Check for Delete key with selected items
        if (Input.GetKeyDown(KeyCode.Delete) && _selectedItems.Count > 0)
        {
            // Don't delete if typing in an input field
            // Note: We only check for focused input fields, not mouse-over-UI,
            // because the user may have just finished selecting with the mouse still over UI
            if (IsAnyInputFieldFocused())
                return;

            DeleteSelectedItems();
        }
    }
    
    /// <summary>
    /// Check if any text input field is currently focused.
    /// This is separate from GUIIsBeingUsed() which also checks mouse position.
    /// </summary>
    private bool IsAnyInputFieldFocused()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;
            
        var selected = eventSystem.currentSelectedGameObject;
        if (selected == null)
            return false;
            
        // Check for TMP_InputField
        var tmpInput = selected.GetComponent<TMP_InputField>();
        if (tmpInput != null && tmpInput.isFocused)
            return true;
            
        // Check for legacy InputField
        var legacyInput = selected.GetComponent<InputField>();
        if (legacyInput != null && legacyInput.isFocused)
            return true;
            
        return false;
    }

    private void DeleteSelectedItems()
    {
        int deletedCount = 0;
        int skippedLocked = 0;

        // Copy to list since we'll be modifying the collection
        var itemsToProcess = new List<GameObject>(_selectedItems);

        foreach (var go in itemsToProcess)
        {
            if (go == null)
            {
                _selectedItems.Remove(go);
                continue;
            }

            // Check if it's a Pic
            var picMain = go.GetComponent<PicMain>();
            if (picMain != null)
            {
                // Skip locked items
                if (picMain.GetLocked())
                {
                    skippedLocked++;
                    continue;
                }

                picMain.SafelyKillThisPic();
                deletedCount++;
                _selectedItems.Remove(go);
                continue;
            }

            // Check if it's an Adventure text
            var adventureText = go.GetComponent<AdventureText>();
            if (adventureText != null)
            {
                Destroy(go);
                deletedCount++;
                _selectedItems.Remove(go);
            }
        }

        // Show message
        string message = $"Deleted {deletedCount} item(s)";
        if (skippedLocked > 0)
        {
            message += $" (skipped {skippedLocked} locked)";
        }
        RTQuickMessageManager.Get().ShowMessage(message);

        ClearSelection();
    }

    // Helper methods to check what's under the mouse
    private bool IsPicUnderMouse()
    {
        return GetPicUnderMouse() != null;
    }

    private GameObject GetPicUnderMouse()
    {
        Vector2 ray = new Vector2(
            _camera.ScreenToWorldPoint(Input.mousePosition).x,
            _camera.ScreenToWorldPoint(Input.mousePosition).y
        );
        RaycastHit2D hit = Physics2D.Raycast(ray, Vector2.zero);
        if (hit.collider != null)
        {
            // Pics have collider on a child, parent has PicMain
            var picMain = hit.collider.gameObject.GetComponentInParent<PicMain>();
            if (picMain != null)
            {
                return picMain.gameObject;
            }
        }
        return null;
    }

    private bool IsAdventureTextUnderMouse()
    {
        return GetAdventureTextUnderMouse() != null;
    }

    private GameObject GetAdventureTextUnderMouse()
    {
        // Adventure texts are UI elements, check with a different approach
        var adventuresParent = RTUtil.FindObjectOrCreate("Adventures").transform;
        var adventureTexts = adventuresParent.GetComponentsInChildren<AdventureText>();

        Vector3 mouseWorldPos = _camera.ScreenToWorldPoint(Input.mousePosition);

        foreach (var adventureText in adventureTexts)
        {
            if (adventureText == null)
                continue;

            Rect worldRect = GetAdventureTextWorldRect(adventureText);
            if (worldRect.Contains(new Vector2(mouseWorldPos.x, mouseWorldPos.y)))
            {
                return adventureText.gameObject;
            }
        }
        return null;
    }

    // Public API
    public bool IsDragging() => _isDragging;
    public int GetSelectedCount() => _selectedItems.Count;
    public bool IsSelected(GameObject go) => _selectedItems.Contains(go);
}
