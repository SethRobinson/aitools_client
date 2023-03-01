using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//example script showing how to use RTSimpleNavBar.  This script you have to write custom for your menu.

public class PicMenuNavBar : MonoBehaviour
{
    public PicMain _picMain;

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
        
        //_navBar.SetToolTipLocation(transform.position);
        _navBar.Reset();
        _navBar.AddOption("Rerender image", _picMain.OnReRenderButton, "Render again with same seed.Useful for getting the same pic with more steps");
        _navBar.AddOption("Upscale 2X", _picMain.OnUpscaleButton, "Upscales 2X, beware large image sizes.  Also applies face correction.");
        _navBar.AddOption("Interrogate", _picMain.OnInterrogateButton, "Examines the image and replaces your current text prompt<br>with the description of it");
        _navBar.AddOption("Blur", _picMain.OnMutateButton, "Applies a blur filter to the entire image");
        _navBar.AddOption("Generate mask of the foreground (DIS)", _picMain.OnGenerateMaskButton, "Uses AI to mask out the subject.<br>Need the background?  Use Ctrl-I to inverse it.");
        _navBar.AddOption("Generate mask of the foreground - method 2 (DIS)", _picMain.OnGenerateMaskButtonSimple, "Uses AI to mask out the subject.<br>This version doesn't allow translucency and expands a bit, better for replacing things.");
        _navBar.AddOption("Toggle smoothing", _picMain.OnToggleSmoothing, "Toggles bilinear filtering, aka smoothing<br>(Doesn't actually change the image at all)");

    }

}
