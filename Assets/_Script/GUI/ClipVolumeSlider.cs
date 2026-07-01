using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClipVolumeSlider : MonoBehaviour
{
    private const float VerticalOffset = -46f;

    public TextMeshProUGUI m_text;
    public Slider m_slider;

    void Start()
    {
        if (m_slider != null && GameLogic.Get() != null)
            m_slider.SetValueWithoutNotify(GameLogic.Get().GetClipVolume());
        UpdateValue();
    }

    public void UpdateValue()
    {
        if (m_slider == null || GameLogic.Get() == null)
            return;

        float volume = Mathf.Clamp01(m_slider.value);
        if (!Mathf.Approximately(m_slider.value, volume))
            m_slider.SetValueWithoutNotify(volume);

        if (m_text != null)
            m_text.text = "Volume: " + Mathf.RoundToInt(volume * 100f) + "%";

        GameLogic.Get().SetClipVolume(volume);
    }

    public static void CreateUnderPenSizeSlider(PenSizeSlider penSizeSlider)
    {
        if (penSizeSlider == null || penSizeSlider.m_slider == null || penSizeSlider.m_text == null)
            return;

        Transform parent = penSizeSlider.transform.parent;
        if (parent == null || parent.Find("ClipVolumeSlider") != null)
            return;

        TextMeshProUGUI label = CreateLabel(penSizeSlider, parent);
        Slider slider = CreateSlider(penSizeSlider, parent);
        if (slider == null)
            return;

        ClipVolumeSlider volumeSlider = slider.gameObject.AddComponent<ClipVolumeSlider>();
        volumeSlider.m_text = label;
        volumeSlider.m_slider = slider;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        if (GameLogic.Get() != null)
            slider.SetValueWithoutNotify(GameLogic.Get().GetClipVolume());
        slider.onValueChanged.AddListener(_ => volumeSlider.UpdateValue());
        volumeSlider.UpdateValue();
    }

    private static TextMeshProUGUI CreateLabel(PenSizeSlider penSizeSlider, Transform parent)
    {
        GameObject labelObj = Instantiate(penSizeSlider.m_text.gameObject, parent);
        labelObj.name = "ClipVolumeLabel";
        SetLayerRecursively(labelObj, penSizeSlider.gameObject.layer);

        RectTransform sourceRect = penSizeSlider.m_text.GetComponent<RectTransform>();
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        if (sourceRect != null && labelRect != null)
        {
            CopyRectLayout(sourceRect, labelRect);
            labelRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, VerticalOffset);
        }

        return labelObj.GetComponent<TextMeshProUGUI>();
    }

    private static Slider CreateSlider(PenSizeSlider penSizeSlider, Transform parent)
    {
        RectTransform sourceRect = penSizeSlider.GetComponent<RectTransform>();
        if (sourceRect == null)
            return null;

        GameObject sliderObj = new GameObject("ClipVolumeSlider", typeof(RectTransform));
        sliderObj.layer = penSizeSlider.gameObject.layer;

        RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.SetParent(parent, false);
        CopyRectLayout(sourceRect, sliderRect);
        sliderRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, VerticalOffset);

        for (int i = 0; i < penSizeSlider.transform.childCount; i++)
        {
            Transform child = penSizeSlider.transform.GetChild(i);
            GameObject childCopy = Instantiate(child.gameObject, sliderRect);
            childCopy.name = child.name;
            SetLayerRecursively(childCopy, sliderObj.layer);
        }

        Slider sourceSlider = penSizeSlider.m_slider;
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.navigation = sourceSlider.navigation;
        slider.transition = sourceSlider.transition;
        slider.colors = sourceSlider.colors;
        slider.spriteState = sourceSlider.spriteState;
        slider.animationTriggers = sourceSlider.animationTriggers;
        slider.interactable = sourceSlider.interactable;
        slider.direction = sourceSlider.direction;
        slider.fillRect = FindRect(sliderObj.transform, sourceSlider.fillRect != null ? sourceSlider.fillRect.name : "Fill");
        slider.handleRect = FindRect(sliderObj.transform, sourceSlider.handleRect != null ? sourceSlider.handleRect.name : "Handle");
        slider.targetGraphic = FindGraphic(sliderObj.transform, sourceSlider.targetGraphic != null ? sourceSlider.targetGraphic.name : "Handle");
        return slider;
    }

    private static RectTransform FindRect(Transform root, string objectName)
    {
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static Graphic FindGraphic(Transform root, string objectName)
    {
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.name == objectName)
                return graphic;
        }
        return null;
    }

    private static void CopyRectLayout(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
        target.localPosition = source.localPosition;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
