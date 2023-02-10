using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//Modified from https://forum.unity.com/threads/slider-bar-stepping.267467/
//To use this, drag onto a slider GUI object, and add an OnValueChanged callback to UpdateStep()

public class SliderStep : MonoBehaviour
{
    public float stepAmount = 0.5f;
    Slider mySlider = null;
    int numberOfSteps = 0;

    // Start is called before the first frame update
    private void Awake()
    {
        mySlider = this.gameObject.GetComponent<Slider>();
    }
    void Start()
    {
        mySlider = GetComponent<Slider>();
        numberOfSteps = Mathf.CeilToInt( (float)mySlider.maxValue / stepAmount);
    }
   
    public void UpdateStep()
    {
        float range = (mySlider.value / mySlider.maxValue) * numberOfSteps;
        int ceil = Mathf.CeilToInt(range);
        mySlider.value = ceil * stepAmount;
    }
}
