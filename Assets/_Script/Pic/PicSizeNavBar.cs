using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//example script showing how to use RTSimpleNavBar.  This script you have to write custom for your menu.

public class PicSizeNavBar : MonoBehaviour
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
        _navBar.AddOption("Crop to mask rect", CropToMaskRect, "Move/resize the active mask around this image, then choose this<br>to crop to that part.");

        _navBar.AddOption("Resize to 128X128", Resize128, "Can be useful for making pixel art look better");
        _navBar.AddOption("Resize to 256X256", Resize256, "Might be a useful size for, I don't know, something");
        _navBar.AddOption("Resize to 512X512", Resize512, "SD 1.5 models work best on images this size.<br>Hint: Use crop to mask size first to isolate the best parts first");
        _navBar.AddOption("Resize to 512X512 (Keep aspect ratio)", Resize512WithAspect, "Like above but the image will crop if needed instead of stretching everything out");
        _navBar.AddOption("Resize to 768X768", Resize768, "Another useful size, maybe");
        _navBar.AddOption("Resize to 768X768 (Keep aspect ratio)", Resize768WithAspect, "Another useful size, maybe");

        _navBar.AddOption("Resize to 1024X1024", Resize1024, "Another useful size, maybe");
        _navBar.AddOption("Resize to 1024X1024 (Keep aspect ratio)", Resize1024WithAspect, "Another useful size, maybe");

        _navBar.AddOption("Resize to 1536X1536", Resize1536, "Another useful size, maybe");
        _navBar.AddOption("Resize to 1536X1536 (Keep aspect ratio)", Resize1536WithAspect, "Another useful size, maybe");

        _navBar.AddOption("Resize to 2048X2048", Resize2048, "Another useful size, maybe");
        _navBar.AddOption("Resize to 2048X2048 (Keep aspect ratio)", Resize2048WithAspect, "Another useful size, maybe");
        _navBar.AddOption("Add 30% empty border and mask it)", AddBorder, "To outpaint, do img2img with 1.0 Denoising + Mask Contents=Latent Noise, blending at 0.");


    }

    public void Resize128()
    {
        _picMain.Resize(128, 128, false);
    }
    public void Resize256()
    {
        _picMain.Resize(256, 256, false);
    }

    public void Resize512()
    {
        _picMain.Resize(512,512,false);
    }
    public void Resize512WithAspect()
    {
        _picMain.Resize(512, 512, true);
    }

    public void CropToMaskRect()
    {
        _picMain.CropToMaskRect();
    }

    public void Resize768()
    {
        _picMain.Resize(768,768, false);
    }
    public void Resize768WithAspect()
    {
        _picMain.Resize(768, 768, true);
    }

    public void Resize1024()
    {
        _picMain.Resize(1024, 1024, false);
    }
    public void Resize1024WithAspect()
    {
        _picMain.Resize(1024, 1024, true);
    }

    public void Resize1536()
    {
        _picMain.Resize(1536, 1536, false);
    }
    public void Resize1536WithAspect()
    {
        _picMain.Resize(1536, 1536, true);
    }

    public void Resize2048()
    {
        _picMain.Resize(2048, 2048, false);
    }

    public void AddBorder()
    {
        //run as coroutine
   
        StartCoroutine(_picMain.AddBorder((int) ((float)_picMain.m_pic.sprite.texture.width*0.15f), (int)((float)_picMain.m_pic.sprite.texture.width * 0.15f),
            (int)((float)_picMain.m_pic.sprite.texture.width * 0.15f), 
            (int)((float)_picMain.m_pic.sprite.texture.width * 0.15f),
            new Color(0,0,0,1), true));
    }
    public void Resize2048WithAspect()
    {
        _picMain.Resize(2048, 2048, true);
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
