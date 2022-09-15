using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LockIcon : MonoBehaviour
{

    public PicMain m_picScript;
    public Button m_button;
    public Sprite m_lockedImage;
    public Sprite m_unlockedImage;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetLocked(bool bNew)
    {
        Debug.Log("Clicked lock");
        m_picScript.SetLocked(bNew);

        //change icons to match new state
        if (bNew)
        {
            m_button.image.sprite = m_lockedImage;
        } else
        {
            m_button.image.sprite = m_unlockedImage;
        }
    }

    public void OnLockButtonClicked()
    {
        SetLocked(!m_picScript.GetLocked());
    }

    

}
