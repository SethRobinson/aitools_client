using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tiny adapter that forwards UI pointer clicks to a callback. Used to repurpose existing
/// scene-wired UI elements (e.g. a now-disabled TMP_Dropdown) as click targets that open
/// the PresetPickerDialog without having to edit the scene.
/// </summary>
public class ClickToPickHandler : MonoBehaviour, IPointerClickHandler
{
    public System.Action OnClickedAction;

    public void OnPointerClick(PointerEventData eventData)
    {
        OnClickedAction?.Invoke();
    }
}
