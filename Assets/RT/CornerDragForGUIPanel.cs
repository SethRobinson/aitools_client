using UnityEngine;
using UnityEngine.EventSystems;

public class CornerDragForGUIPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform panelRectTransform;
    private RectTransform handleRectTransform;

    private Vector2 originalSizeDelta;
    private Vector2 initialClickPosition;
    private Vector3 originalAnchoredPosition;

    private float minWidth = 100;
    private float maxWidth = 5000;
    private float minHeight = 100;
    private float maxHeight = 5000;

    private Canvas canvas;

    private void Awake()
    {
        handleRectTransform = GetComponent<RectTransform>();
        if (panelRectTransform == null)
        {
            panelRectTransform = handleRectTransform.parent.GetComponent<RectTransform>();
        }
        originalSizeDelta = panelRectTransform.sizeDelta;

        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalSizeDelta = panelRectTransform.sizeDelta;
        originalAnchoredPosition = panelRectTransform.anchoredPosition;
        initialClickPosition = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 currentDragDistance = (eventData.position - initialClickPosition) / canvas.scaleFactor;
        Vector2 newSizeDelta = originalSizeDelta + new Vector2(currentDragDistance.x, -currentDragDistance.y);

        newSizeDelta = new Vector2(
            Mathf.Clamp(newSizeDelta.x, minWidth, maxWidth),
            Mathf.Clamp(newSizeDelta.y, minHeight, maxHeight)
        );

        Vector2 positionChange = (newSizeDelta - originalSizeDelta) / 2;
        panelRectTransform.anchoredPosition = originalAnchoredPosition + new Vector3(positionChange.x, -positionChange.y);
        panelRectTransform.sizeDelta = newSizeDelta;
    }

    public void OnEndDrag(PointerEventData eventData) { }
}
