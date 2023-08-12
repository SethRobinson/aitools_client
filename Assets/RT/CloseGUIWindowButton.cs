
using UnityEngine;
using UnityEngine.EventSystems;


//To use this, attach to a button to your main panel, then add a button event that calls OnCloseWindow.
//It's optional to set a specific GameObject to close, just drag it into the windowToClose parm

public class CloseGUIWindowButton : MonoBehaviour
{

    public GameObject m_windowToClose;
    public bool m_closeWindowIfClickedOutsideOfGUI = false;

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

    public void OnDeactivateWindow()
    {
        if (m_windowToClose == null)
        {
            m_windowToClose = gameObject;
        }

        m_windowToClose.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (m_closeWindowIfClickedOutsideOfGUI)
        {
            // Check if Left Mouse Button is pressed and not over a UI element
            if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            {
                OnCloseWindow();
            }
        }
    }
}
