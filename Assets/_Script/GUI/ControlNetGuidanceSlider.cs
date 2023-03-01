using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class ControlNetGuidanceSlider : MonoBehaviour
{

    public TMPro.TextMeshProUGUI m_text;
    public UnityEngine.UI.Slider m_slider;

    // Start is called before the first frame update
   
    public void UpdateValue(float value)
    {
        m_text.text = "Guidance Strength (T): " + value.ToString("0.0#", CultureInfo.InvariantCulture);
        GameLogic.Get().SetControlNetGuidance(value);
        m_slider.SetValueWithoutNotify(value);
    }
}
