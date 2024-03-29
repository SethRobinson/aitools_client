using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UpdateSplashGUI : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {

    }

    public void OnCloseButtonClicked()
    {
        // Debug.Log("Close button clicked");
        gameObject.transform.parent.gameObject.SetActive(false);
    }
    public void OnLogoClicked()
    {
        //Debug.Log("Clicked logo, opening website");
        RTUtil.PopupUnblockOpenURL("https://www.rtsoft.com");

    }

    public void OnDownloadUpdate()
    {
        
        string downloadLink = "https://www.rtsoft.com/files/SethsAIToolsWindows.zip";

        RTConsole.Log("Downloading " + downloadLink);
        RTUtil.PopupUnblockOpenURL("https://www.rtsoft.com/files/SethsAIToolsWindows.zip");
        OnCloseButtonClicked();


    }
    // Update is called once per frame
    void Update()
    {

    }
}
