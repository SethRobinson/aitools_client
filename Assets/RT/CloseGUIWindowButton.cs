using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//To use this, attach to a button to your main panel, then add a button event that calls OnCloseWindow.
//It's optional to set a specific GameObject to close, just drag it into the windowToClose parm

public class CloseGUIWindowButton : MonoBehaviour
{

    public GameObject m_windowToClose;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void OnCloseWindow()
    {
        if (m_windowToClose == null)
        {
            m_windowToClose = gameObject;
        }

        GameObject.Destroy(m_windowToClose);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
