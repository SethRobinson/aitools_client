using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ConsoleToggle : MonoBehaviour
{
    string _oldText;

	public void ToggleConsole()
    { 
        //print("Toggling debug console");

        GameObject debugCanvas = RTConsole.Get().transform.parent.gameObject;

        debugCanvas.SetActive(!debugCanvas.activeSelf);
        if (debugCanvas.activeSelf)
        {
            RTConsole.Get().SetFocusOnInput(_oldText);
        }
        else
        {
            //save what was there
            _oldText = RTConsole.Get().GetCurrentText().Replace("~", "");

        }
        //return debugCanvas.activeSelf;
    }

	// Update is called once per frame
	void Update ()
    {

        //    if (!RTUtil.IsHeadless())
        if (Keyboard.current != null)
        {
            if (Keyboard.current.backquoteKey.wasPressedThisFrame && Keyboard.current.shiftKey.isPressed)
            {
                ToggleConsole();
            }
        }

    }
}
