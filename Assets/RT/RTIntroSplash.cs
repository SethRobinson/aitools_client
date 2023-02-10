using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RTIntroSplash : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void OnCloseButtonClicked()
    {
       // Debug.Log("Close button clicked");
        GameObject.Destroy(gameObject);
       
    }
    public void OnLogoClicked()
    {
        //Debug.Log("Clicked logo, opening website");
        RTUtil.PopupUnblockOpenURL("https://www.rtsoft.com");

    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
