using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TilingGUI : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        //init things with existing value
        Toggle toggle = this.gameObject.GetComponent<Toggle>();

        if (toggle != null)
        {
            GameLogic.Get().OnTilingButton(toggle.isOn);
        }
        else
        {
            Debug.LogError("Can't find tiled toggle");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
