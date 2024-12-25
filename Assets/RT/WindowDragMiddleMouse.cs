using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/*
Attach to any GUI object to allow it to be dragged around the screen with the mouse

by Seth A Robinson
*/

public class WindowDragMiddleMouse : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerClickHandler
{
    public Transform baseCanvas; // Set this in the Inspector to the object you want to move
    public Transform moveTarget;
    private Vector3 _vStartDragPos;
    private bool _bDragging = false;
    private Vector3 _originalPosition;

    private void Start()
    {
        // If moveTarget is not set, use the parent object by default
        if (baseCanvas == null)
        {
            baseCanvas = transform.parent;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_bDragging)
        {
            // Convert screen position to world position
            Vector3 worldPoint;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(baseCanvas as RectTransform, eventData.position, eventData.pressEventCamera, out worldPoint);
            Vector3 vOffset = worldPoint - _vStartDragPos;
            _vStartDragPos = worldPoint;

            if (moveTarget != null)
            {
                moveTarget.position += vOffset; // Move the specified target object
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Middle)
        {
            return;
        }
        if (baseCanvas != null)
        {
            baseCanvas.SetAsLastSibling();
        }

        // Convert screen position to world position
        RectTransformUtility.ScreenPointToWorldPointInRectangle(baseCanvas as RectTransform, eventData.position, eventData.pressEventCamera, out _vStartDragPos);
        _bDragging = true;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _bDragging = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (baseCanvas != null)
        {
            baseCanvas.SetAsLastSibling();
        }
    }
}
