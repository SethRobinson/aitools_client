using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RemoveBackgroundGUI : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        //init things with existing value
        Toggle toggle = this.gameObject.GetComponent<Toggle>();

        if (toggle != null)
        {
            GameLogic.Get().OnRemoveBackground(toggle.isOn);
        }
        else
        {
            Debug.LogError("Can't find tiled toggle");
        }
    }

}
