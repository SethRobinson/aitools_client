using HSVPicker;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;


//To use this, attach to a button (with no text is good), then attach a SethColorPickerPrefab (which overrides https://github.com/judah4/HSV-Color-Picker-Unity
//to make it moveable and have an OK button)
//This will auto wire itself to the button on startup, so don't do it manually

public class ColorPickerButtonScript : MonoBehaviour
{
    public Color m_defaultColor;
    public GameObject m_sethColorPickerPrefab;

    // Start is called before the first frame update
    void Start()
    {
        //grab this button's image
        UnityEngine.UI.Image image = GetComponent<UnityEngine.UI.Image>();
        image.color = m_defaultColor;

        //this is attached to a Unity Button, let's write it so when the button is pressed it will call OnClickedButton()
        UnityEngine.UI.Button button = GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(OnClickedButton);


    }

  

    // Update is called once per frame
    void Update()
    {
      
    }

    public void OnClickedButton()
    {
        //instantiate the color picker as a child of our parent gameobject
        GameObject colorPicker = GameObject.Instantiate(m_sethColorPickerPrefab);
        //make this object appear on top
        //dynamically link a callback when its color changes
        var picker = colorPicker.GetComponentInChildren<ColorPicker>();
        UnityEngine.UI.Image image = GetComponent<UnityEngine.UI.Image>();
         //set the default color of the color picker we instantiated
        picker.CurrentColor = m_defaultColor;

        picker.onValueChanged.AddListener(color =>
        {
            m_defaultColor = color;
            image.color = m_defaultColor;

        });

    }
}
