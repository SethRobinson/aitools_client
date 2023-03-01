using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class ControlNetWeightSlider : MonoBehaviour
{

    public TMPro.TextMeshProUGUI m_text;
    public UnityEngine.UI.Slider m_slider;

    public void UpdateValue(float value)
    {
        m_text.text = "Weight: " + value.ToString("0.0#", CultureInfo.InvariantCulture);
        GameLogic.Get().SetControlNetWeight(value);
        m_slider.SetValueWithoutNotify(value);
    }
}
