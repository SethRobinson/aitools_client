using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SeedGUI : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        //init things with whatever is already in the input box
        TMPro.TMP_InputField tMP_InputField = GetComponent<TMPro.TMP_InputField>();
        if (tMP_InputField != null)
        {
            GameLogic.Get().OnSeedChanged(tMP_InputField.text);
        }
        else
        {
            Debug.LogError("Can't find input field");
        }

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
