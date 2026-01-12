using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
Attach to any GameObject to allow it to be dragged around the screen with the mouse.
The GameObject should have a BoxCollider2D component.

Supports multi-selection dragging: if this object is part of a selection (via SelectionManager),
dragging this object will move all selected objects together.

by Seth A Robinson
*/

public class WorldObjectDrag : MonoBehaviour
{
    private Vector3 _offset;
    private bool _dragging = false;
    private Camera _camera;
    
    // For multi-drag support
    private static WorldObjectDrag _activeDragger = null; // The one object initiating the drag
    private static List<(Transform transform, Vector3 offset)> _multiDragTargets = new List<(Transform, Vector3)>();

    public BoxCollider2D boxCollider2D;
    public bool _RequireControlKeyToo = false;

    public enum MouseButton { Left = 0, Middle = 2, Right = 1 }
    public MouseButton dragButton = MouseButton.Left;

    void Start()
    {
        _camera = Camera.main;
    }

    void Update()
    {
        if (_RequireControlKeyToo && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
        {
            return;
        }

        Vector3 mousePosition = _camera.ScreenToWorldPoint(Input.mousePosition);
        mousePosition.z = 0f;

        if (Input.GetMouseButtonDown((int)dragButton))
        {
            if (boxCollider2D.OverlapPoint(mousePosition))
            {
                _dragging = true;
                _offset = transform.position - mousePosition;
                
                // Check if this object is part of a selection for multi-drag
                SetupMultiDrag(mousePosition);
            }
        }

        if (_dragging && Input.GetMouseButton((int)dragButton))
        {
            // If we're the active dragger for a multi-selection, move all targets
            if (_activeDragger == this && _multiDragTargets.Count > 0)
            {
                foreach (var target in _multiDragTargets)
                {
                    if (target.transform != null)
                    {
                        target.transform.position = mousePosition + target.offset;
                    }
                }
            }
            else
            {
                // Single object drag
                transform.position = mousePosition + _offset;
            }
        }

        if (Input.GetMouseButtonUp((int)dragButton))
        {
            if (_dragging)
            {
                _dragging = false;
                
                // Clean up multi-drag state if we were the active dragger
                if (_activeDragger == this)
                {
                    _activeDragger = null;
                    _multiDragTargets.Clear();
                }
            }
        }
    }
    
    /// <summary>
    /// Sets up multi-drag if this object is part of a selection.
    /// </summary>
    private void SetupMultiDrag(Vector3 mousePosition)
    {
        var selectionManager = SelectionManager.Get();
        if (selectionManager == null || selectionManager.GetSelectedCount() <= 1)
            return;
            
        // Check if this object is selected
        if (!selectionManager.IsSelected(gameObject))
            return;
            
        // We're part of a multi-selection, set up to drag all selected items
        _activeDragger = this;
        _multiDragTargets.Clear();
        
        // Get all selected Pics
        var picsParent = RTUtil.FindObjectOrCreate("Pics").transform;
        var picScripts = picsParent.GetComponentsInChildren<PicMain>();
        foreach (var picScript in picScripts)
        {
            if (picScript != null && !picScript.IsDestroyed() && selectionManager.IsSelected(picScript.gameObject))
            {
                Vector3 offset = picScript.transform.position - mousePosition;
                _multiDragTargets.Add((picScript.transform, offset));
            }
        }
        
        // Get all selected Adventure texts
        var adventuresParent = RTUtil.FindObjectOrCreate("Adventures").transform;
        var adventureTexts = adventuresParent.GetComponentsInChildren<AdventureText>();
        foreach (var adventureText in adventureTexts)
        {
            if (adventureText != null && selectionManager.IsSelected(adventureText.gameObject))
            {
                Vector3 offset = adventureText.transform.position - mousePosition;
                _multiDragTargets.Add((adventureText.transform, offset));
            }
        }
    }
}
