using UnityEngine;

/// <summary>
/// Marker component that lets the mouse wheel "pass through" a UI panel so the global
/// camera zoom keeps working while the cursor is hovering this UI element.
///
/// The camera mover (<see cref="SimpleCameraMoverWithPinch"/>) raycasts the UI under
/// the cursor and walks up from the topmost hit; if it finds a MouseWheelPassthrough
/// anywhere in that ancestor chain, it treats the cursor as "not over UI" for the
/// purpose of mouse-wheel zoom (panning and pinch are unaffected and remain blocked).
///
/// Add this component to the root of any panel that should NOT eat the mouse wheel.
/// Adventure text panels add it programmatically in AdventureText.Awake().
/// </summary>
public class MouseWheelPassthrough : MonoBehaviour
{
}
