using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
Attach to any GameObject to allow it to be dragged around the screen with the mouse.
The GameObject should have a BoxCollider2D component.

by Seth A Robinson
*/

public class WorldObjectDrag : MonoBehaviour
{
    private Vector3 _offset;
    private bool _dragging = false;
    private Camera _camera;

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
            }
        }

        if (_dragging && Input.GetMouseButton((int)dragButton))
        {
            transform.position = mousePosition + _offset;
        }

        if (Input.GetMouseButtonUp((int)dragButton))
        {
            _dragging = false;
        }
    }
}
