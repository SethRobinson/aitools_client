using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//allows right mouse to close the app, useful during development, probably not a great idea for your
//released product tho

public class QuitOnRightMouseButton : MonoBehaviour
{
  
    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Debug.Log("Quitting app because right mouse button was pressed!");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            Application.Quit();
        }
    }
}
