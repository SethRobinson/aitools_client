using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//example script showing how to use RTSimpleNavBar.  This script you have to write custom for your menu.
//Oh, you need to make sure you have RTToolTipManager added to a gameobject somewhere in your project
public class NavMenuExampleScript : MonoBehaviour
{

    RTSimpleNavBar _navBar;
    // Start is called before the first frame update
    void Start()
    {
        //setup our menu thing
        _navBar = GetComponent<RTSimpleNavBar>();
        BuildMenu();
    }

    public void BuildMenu()
    {
        _navBar.Reset();
        _navBar.AddOption("First Option", OnClickFirstOption, "This is the tool tip that's shown for the<br>first option");
        _navBar.AddOption("Second Option", OnClickSecondOption, "More optional info about this option");

    }

    public void OnClickFirstOption()
    {
        Debug.Log("Clicked upscale!");
    }

    public void OnClickSecondOption()
    {
        Debug.Log("Second option!");
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
