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
        _navBar.AddOption("Rerender image", _picMain.OnReRenderButton, "Render again with same seed. Useful for getting the same pic with more steps");
        _navBar.AddOption("Rerender image with new seed", _picMain.OnReRenderNewSeedButton, "Render again with new seed. Useful for getting the same pic with more steps");
        _navBar.AddOption("Clear jobs/errors", _picMain.ClearErrorsAndJobs, "Clears any errors or jobs, good if an error happens but you want to use the pic again.");
        // _navBar.AddOption("Rerender copy with new seed", _picMain.OnReRenderCopyButton, "Duplicate image, change the seed, then render again");
        //  _navBar.AddOption("Generate Audio for video (MMAudio)", _picMain.OnGenAudio, "Uses ComfyUI");
        // _navBar.AddOption("Upscale 2X", _picMain.OnUpscaleButton, "Upscales 2X, beware large image sizes.  Also applies face correction.");
        // _navBar.AddOption("Interrogate", _picMain.OnInterrogateButton, "Examines the image and replaces your current text prompt<br>with the description of it");
        _navBar.AddOption("Blur", _picMain.OnMutateButton, "Applies a blur filter to the entire image");
       // _navBar.AddOption("Generate mask of the foreground (DIS)", _picMain.OnGenerateMaskButton, "Uses AI to mask out the subject.<br>Need the background?  Use Ctrl-I to inverse it.");
        //_navBar.AddOption("Generate mask of the foreground - method 2 (DIS)", _picMain.OnGenerateMaskButtonSimple, "Uses AI to mask out the subject.<br>This version doesn't allow translucency and expands a bit, better for replacing things.");
        _navBar.AddOption("Toggle smoothing", _picMain.OnToggleSmoothing, "Toggles bilinear filtering, aka smoothing<br>(Doesn't actually change the image at all)");
        _navBar.AddOption("Cleanup pixel art", _picMain.CleanupPixelArt, "Resamples image to 128x128 then scales it back up<br>(results in more pixelly output)");
        _navBar.AddOption("Save as PNG", _picMain.SaveFilePNG, "Saved as a PNG (no mask included).  The little S on the bar will save as bmp with the mask intact.)");
        //_navBar.AddOption("Render w/ AIT/A1111", _picMain.OnRenderWithAITOrA1111, "Works if you have a AI Tools or A1111 server");
        _navBar.AddOption("Render w/ ComfyUI", _picMain.OnRenderWithComfyUI, "Works if you have a ComfyUI server defined in your config");
        _navBar.AddOption("Render w/ OpenAI Image", _picMain.OnRenderWithOpenAIImageButton, "Hope you entered your OpenAI key and have cash in it");
        _navBar.AddOption("Copy to Temp Pic 1", _picMain.OnSetTemp1Button, "Set image to temp pic 1, useful to hold for more complicated scripting");
        _navBar.AddOption("Get Prompt From Image", _picMain.OnGetPromptFromImageButton, "Click to run the Image To Prompt preset");



    }

}
