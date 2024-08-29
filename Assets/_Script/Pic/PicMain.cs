using System.Collections;
using UnityEngine;
using System.IO;
using System;
using TMPro;
using B83.Image.BMP;
using UnityEngine.Rendering;
using System.Linq;

public class UndoEvent
{
    public Texture2D m_texture;
    public Sprite m_sprite;
    public bool m_active = false;

    public string m_lastPromptUsed = "";
    public RTRendererType m_requestedRenderer = RTRendererType.Any_Local;
    public string m_lastNegativePromptUsed = "";
    public long m_lastSeed = -1;
    public int m_lastSteps = 0;
    public string m_lastModel = "?";
    public string m_lastSampler = "?";
    public string m_lastOperation = "None";
    public float m_lastDenoisingStrength = 0;
    public float m_lastCFGScale = 0;
    public int m_gpu = -1;
    public string m_maskContents = "";
    public float m_maskBlending = 0;
    public bool m_fixFaces = false;
    public bool m_tiling = false;
    public bool m_bUsingPix2Pix = false;
    public float m_pix2pixCFG = 0;

    public bool m_bUsingControlNet = false;
    public string m_lastControlNetModel = "";
    public string m_lastControlNetModelPreprocessor = "";
    public float m_lastControlNetWeight = 0;
    public float m_lastControlNetGuidance = 0;
  
}

public class PicMain : MonoBehaviour
{
    public TextMeshPro m_text;
    public Canvas m_canvas;
    public SpriteRenderer m_pic;
    public SpriteRenderer m_mask;
    private bool m_editFileHasChanged;
    private FileSystemWatcher m_editFileWatcher;
    public PicMask m_picMaskScript;
    public PicTargetRect m_targetRectScript;
    public PicTextToImage m_picTextToImageScript;
    public PicInpaint m_picInpaintScript;
    public PicUpscale m_picUpscaleScript;
    public PicGenerator m_picGeneratorScript;
    public PicInterrogate m_picInterrogateScript;
    public PicGenerateMask m_picGenerateMaskScript;
    public PicInfoPanel m_infoPanelScript;
    bool m_bNeedsToUpdateInfoPanel = false;

    public Action<GameObject> m_onFinishedRenderingCallback;

    public Camera m_camera;

    UndoEvent m_undoevent = new UndoEvent();
    UndoEvent m_curEvent = new UndoEvent(); //useful for just saving the current status, makes it easy to copy to/from a real undo event
    bool m_isDestroyed;
    string m_editFilename = "";
    bool m_bLocked;
    bool m_bDisableUndo;
    float m_genericTimerStart = 0; //used to countdown for Dalle3
    string m_genericTimerText = "Waiting...";

    public AIGuideManager.PassedInfo m_aiPassedInfo; //a misc place to store things the AI guide wants to


    // Start is called before the first frame update
    void Start()
    {
        if (m_text.text == "Sample text") //ugly hack because I'm lazy, but this will break if you change the prefab
        {
            SetStatusMessage("");
        }

        m_camera = Camera.allCameras[0];
        m_canvas.worldCamera = m_camera;

        int biggestSize = 512;
       
        if (m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            //this already has an image, it's possible, happens when Duplicate is used
        } else
        {
            Texture2D defaultTex = new Texture2D(biggestSize, biggestSize, TextureFormat.RGBA32, false);
            defaultTex.Fill(Color.black);
            defaultTex.FillAlpha(1.0f);
            defaultTex.Apply();
            m_pic.sprite = Sprite.Create(defaultTex, new Rect(0, 0, defaultTex.width, defaultTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

        }

        MakeDraggable();
    }
    public void SetDisableUndo(bool bNew)
    {
        m_bDisableUndo = bNew;
    }
    public Camera GetCamera() { return m_camera; }

    public Canvas GetCanvas() { return m_canvas; }
    public UndoEvent GetCurrentStats() { return m_curEvent; }

    public bool IsDestroyed()
    {
        return m_isDestroyed;
    }

    public void ClearRenderingCallbacks()
    {
        m_onFinishedRenderingCallback = null;
    }

    public void MakeDraggable()
    {

        //we want to be able to drag this around, so we'll have to disable the mask drawing stuff
        //m_picMaskScript.SetMaskVisible(false);
        //Add ObjectDrag to us
        

    }
    public string GetInfoText()
    {
       
        var c = GetCurrentStats();

        string c1 = "`4";

        string rendererRequested = GetCurrentStats().m_requestedRenderer.ToString();

        string msg =
$@"`8{c1}Last Operation:`` {c.m_lastOperation} {c1}Renderer:`` {rendererRequested} {c1}on ServerID: ``{c.m_gpu} {Config.Get().GetServerNameByGPUID(c.m_gpu)}
";

        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            msg += $@"{c1}Image size X:`` {(int)m_pic.sprite.texture.width} {c1}, Y: ``{(int)m_pic.sprite.texture.height}";
        }

msg += $@" {c1}Mask Rect size X: ``{(int)m_targetRectScript.GetOffsetRect().width}{c1}, Y: ``{(int)m_targetRectScript.GetOffsetRect().height}
{c1}Model:`` {c.m_lastModel}
{c1}Sampler:`` {c.m_lastSampler} {c1}Steps:`` {c.m_lastSteps} {c1}Seed:`` {c.m_lastSeed}
{c1}CFG Scale:`` {c.m_lastCFGScale} {c1}Tiling: ``{c.m_tiling} {c1}Fix Faces:`` {c.m_fixFaces}
{c1}Prompt:`` {c.m_lastPromptUsed}
{c1}Negative prompt:`` {c.m_lastNegativePromptUsed}
";


        if (GetCurrentStats().m_lastOperation == "img2img")
        {
            msg += $@"{c1}Denoising Strength:`` " + GetCurrentStats().m_lastDenoisingStrength + "\n";
            msg += $@"{c1}Mask Contents:`` " + GetCurrentStats().m_maskContents + " ";
            msg += $@"{c1}Mask Blending:`` " + GetCurrentStats().m_maskBlending.ToString("0.0#") + "\n";

            if (GetCurrentStats().m_bUsingPix2Pix)
            {
                msg += $@"{c1}Pix2Pix active: ``" + GetCurrentStats().m_bUsingPix2Pix + " ";
                msg += $@"{c1}CFG: ``" + GetCurrentStats().m_pix2pixCFG + "\n";
            }

            if (GetCurrentStats().m_bUsingControlNet)
            {
                msg += $@"{c1}Control Net active:`` ";
                msg += $@"{c1}Preprocessor:`` " + GetCurrentStats().m_lastControlNetModelPreprocessor + " ";
                msg += $@"{c1}Model:`` " + GetCurrentStats().m_lastControlNetModel + " ";
                msg += $@"{c1}Weight:`` " + GetCurrentStats().m_lastControlNetWeight+" Guidance: "+GetCurrentStats().m_lastControlNetGuidance + "\n";

            }
        }
      
        return RTUtil.ConvertSansiToUnityColors(msg);
    }

    public void SafelyKillThisPic()
    {
        m_isDestroyed = true;
        GameObject.Destroy(gameObject);
    }

    public void SafelyKillThisPicAndDeleteHoles()
    {

        m_isDestroyed = true;
        GameObject.Destroy(gameObject);
       
        /*
        if (!AdventureLogic.Get().IsActive())
        {
            ImageGenerator.Get().ReorganizePics(false);
        }
        */
    }

    public bool IsBusy()
    {
       
        if (IsBusyBasic()) return true;
        if (m_picGeneratorScript.GetIsGenerating()) return true;

        return false;
    }
    public bool IsBusyBasic()
    {
        if (m_picTextToImageScript.IsBusy()) return true;
        if (m_picInpaintScript.IsBusy()) return true;
        if (m_picUpscaleScript.IsBusy()) return true;

        return false;
    }



    public void SetVisible(bool bNew)
    {
        RTUtil.FindInChildrenIncludingInactive(gameObject, "Canvas").SetActive(bNew);
        RTUtil.FindInChildrenIncludingInactive(gameObject, "Pic").SetActive(bNew);
        RTUtil.FindInChildrenIncludingInactive(gameObject, "StatusText").SetActive(bNew);

    }
    public void KillGPUProcesses()
    {
        if (m_picTextToImageScript.IsBusy()) m_picTextToImageScript.SetForceFinish(true);
        if (m_picInpaintScript.IsBusy()) m_picInpaintScript.SetForceFinish(true);
        if (m_picUpscaleScript.IsBusy()) m_picUpscaleScript.SetForceFinish(true);
        if (m_picGenerateMaskScript != null && m_picGenerateMaskScript.IsBusy()) m_picGenerateMaskScript.SetForceFinish(true);
    }

    public bool GetLocked() { return m_bLocked; }
    public void SetLocked(bool bNew) { m_bLocked = bNew; }
    public void SetStatusMessage(string msg)
    {
        if (GameLogic.Get().GetTurbo())
        {
            m_text.text = ""; //hack to not show messages when in turbo mode
        }
        else
        {
            m_text.text = msg;
        }

        m_genericTimerStart = 0; //disable any timer
    }

   

    void KillUndoImageBuffers()
    {
        if (m_undoevent.m_sprite != null && m_undoevent.m_sprite.texture != null)
        {
            Destroy(m_undoevent.m_sprite.texture);
        }

    }
    public bool AddImageUndo(bool bDoFullCopy = false)
    {
        if (m_bDisableUndo) return false;

        m_undoevent = m_curEvent;
        m_undoevent.m_active = true;

        KillUndoImageBuffers();

        if (bDoFullCopy && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            Texture2D copyTexture = m_pic.sprite.texture.Duplicate();

            float biggestSize = Math.Max(copyTexture.width, copyTexture.height);

            UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(copyTexture, 
                new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);
            
            m_undoevent.m_sprite = newSprite;
            m_undoevent.m_texture = m_pic.sprite.texture;
        } else
        {
            m_undoevent.m_sprite = m_pic.sprite;
            m_undoevent.m_texture = m_pic.sprite.texture;
        }
        //Debug.Log("Image added to undo");

        return true;
    }

    public void UndoImage()
    {
      
        if (m_undoevent.m_active)
        {
            Debug.Log("Undo");

            m_curEvent = m_undoevent;

            Sprite tempSprite = m_pic.sprite;

            bool bSizeChanged = false;

            if (tempSprite.rect != m_undoevent.m_sprite.rect)
            {
                bSizeChanged = true;
            }

            m_pic.sprite = m_undoevent.m_sprite;
            m_undoevent.m_sprite = tempSprite;
            SetNeedsToUpdateInfoPanelFlag();
            MovePicUpIfNeeded();
            
            if (bSizeChanged)
            {
        //        Debug.Log("Size changed!  Throwing away mask because Seth is lazy");
        //        m_picMaskScript.RecreateMask();
            }

            m_picMaskScript.ResizeMaskIfNeeded();
            m_targetRectScript.UpdatePoints();

        }
        else
        {
            Debug.Log("Nothing to undo in this pic.");
        }
    }
    public void MovePicUpIfNeeded()
    {
        var vPos = m_pic.gameObject.transform.localPosition;
       // vPos.y = 0; //default

       // if (m_pic.sprite.bounds.size.y != 5.12f)
        {
           //um.. this pic is not square so it's letterboxed.  Let's move it up near the tool panel, looks better
            vPos.y = (5.12f - m_pic.sprite.bounds.size.y) / 2;
            //Debug.Log("Move to "+vPos);
        }

        m_pic.gameObject.transform.localPosition = vPos;

    }

   public PicMask GetMaskScript() { return m_picMaskScript; }

    public bool LoadImageFromClipboard()
    {

        //delete any existing image

        string tempDir = Application.dataPath;
        tempDir = tempDir.Replace('/', '\\');
        tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));
        string targetExe = tempDir+"\\utils\\RTClip.exe";
        string tempPngFile = tempDir + "\\winclip_image.png";
        RTUtil.DeleteFileIfItExists(tempPngFile);

        var processInfo = new System.Diagnostics.ProcessStartInfo(targetExe, "");
        processInfo.CreateNoWindow = true;
        processInfo.UseShellExecute = false;

        var process = System.Diagnostics.Process.Start(processInfo);

        process.WaitForExit();
        process.Close();

        if (File.Exists(tempPngFile))
        {
            RTConsole.Log("Importing from clipboard...");
            LoadImageByFilename(tempPngFile, false);
            RTUtil.DeleteFileIfItExists(tempPngFile);

        }
        else
        {
            RTConsole.Log("No image found on clipboard");
        }
       

        return false;
    }

    //crashes when accessing image data on clipboard, no idea why.  Have tried in and out of threads

    /*
    public bool LoadImageFromClipboard()
    {
        Debug.Log("Trying to load image from clipboard...");

        Texture2D texture = null;

        System.Threading.Thread t = new System.Threading.Thread(() =>
        {

            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                Debug.Log("Found image on clipboard...");

                var dataObject = System.Windows.Forms.Clipboard.GetDataObject();

                var formats = dataObject.GetFormats(true);
                if (formats == null || formats.Length == 0)
                {

                }
                else
                {


                    foreach (var f in formats)
                        Debug.Log(" - " + f.ToString());
                }

            }


        });

        t.Start();
        t.Join();


        return true;
    }

    */


    public void LoadImageByFilename(string filename, bool bResize = false, bool bRenderAlphaHiddenAreasToo = false)
    {
        try
        {
            Debug.Log("Loading "+filename+" from disk");
            var buffer = File.ReadAllBytes(filename);
            Texture2D texture = null;
            
            string fExt = Path.GetExtension(filename).ToLower();
            bool bNeedToProcessAlpha = false;

            if (fExt == ".bmp")
            {
                //RTQuickMessageManager.Get().ShowMessage("Detected bmp");

                BMPLoader bmp = new BMPLoader();
                BMPImage im = bmp.LoadBMP(filename);
                texture = im.ToTexture2D();

                if (im.HasAlphaChannel())
                {
                    bNeedToProcessAlpha = true;
                }
            }
            else
            {
                texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                texture.LoadImage(buffer);
            }

            if (bResize)
            {
                Debug.Assert(false);
                //resize texture but keep aspect ratio
                if (texture.width != 512 || texture.height != 512)
                {
                    ResizeTool.Resize(texture, 512, 512, false, FilterMode.Bilinear);
                }
            }

            AddImageUndo();
            float biggestSize = Math.Max(texture.width, texture.height);
            Sprite newSprite = null;
            Texture2D alphaTex = null;

            if (bNeedToProcessAlpha)
            {
                bool bAlphaWasUsed = false;

                alphaTex = texture.GetAlphaMask(out bAlphaWasUsed);
                if (bAlphaWasUsed)
                {
                    //valid alpha found
                    alphaTex.Apply();
                    m_picMaskScript.SetMaskFromTexture(alphaTex);

                    if (bRenderAlphaHiddenAreasToo)
                    {
                        //yes, we have an alpha channel, but actually, let's drop it (it's in our mask now anyway) so we can see the hidden
                        //areas anyway.  We do this when loading a file from photoshop
                        texture.FillAlpha(1.0f); //clearing out the alpha in the image
                        texture.Apply();
                    }
                   
                } else
                {
                    //No alpha.  Disable it so it will be auto-filled for operations that need it
                    alphaTex = null;
                    texture.FillAlpha(1.0f);
                    texture.Apply();
                }
            }
          
            newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

            if (m_pic.sprite != null && m_pic.sprite.texture != null)
            {
                Destroy(m_pic.sprite.texture);
            }
            m_pic.sprite = newSprite;
            MovePicUpIfNeeded();
          
            SetStatusMessage("");
            m_picMaskScript.ResizeMaskIfNeeded();
            m_targetRectScript.UpdatePoints();

        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load image from "+filename+".  Does the file even exist?");
            System.Console.WriteLine(e.StackTrace);
        }

        SetNeedsToUpdateInfoPanelFlag();

    }

    public void CropToMaskRect()
    {
        AddImageUndo();

        var croppedTexture = ResizeTool.CropTexture(m_pic.sprite.texture, m_targetRectScript.GetOffsetRect());
        float biggestSize = Math.Max(croppedTexture.width, croppedTexture.height);

        m_pic.sprite = Sprite.Create(croppedTexture, new Rect(0, 0, croppedTexture.width, croppedTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();
        m_targetRectScript.OnMoveToPixelLocation(new Vector2(0, 0));
        SetNeedsToUpdateInfoPanelFlag();

    }

    public System.Collections.IEnumerator AddBorder(int left, int right, int top, int bottom, Color borderColor, bool bSetMaskToBorder)
    {
        //swap top and bottom, texture is upside down
        int temp = top;
        top = bottom;
        bottom = temp;

        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
           //exit from this couroutines
            yield break;

        }
        
        //AddImageUndo(true);

        // Create a new image that is black, that adds the border (based on the size of m_pic.sprite)
        int newWidth = m_pic.sprite.texture.width + left + right;
        int newHeight = m_pic.sprite.texture.height + top + bottom;

        Texture2D defaultTex = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        yield return null;
        defaultTex.Fill(borderColor);
        yield return null;
        //defaultTex.FillAlpha(1.0f);
        defaultTex.Apply();
        yield return null;
        float biggestSize = Math.Max(defaultTex.width, defaultTex.height);

        var newSprite = Sprite.Create(defaultTex, new Rect(0, 0, defaultTex.width, defaultTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

        // Copy the old image into the new one
        Color[] pixels = m_pic.sprite.texture.GetPixels();
        defaultTex.SetPixels(left, top, m_pic.sprite.texture.width, m_pic.sprite.texture.height, pixels);
        yield return null;
        defaultTex.Apply();
        yield return null;

        m_pic.sprite = newSprite;
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
      
        if (bSetMaskToBorder)
        {
            int overlayPixel = 4; // It helps to go one extra when outpainting?
                                  // Get four rects of the border
            Rect topRect = new Rect(0, 0, defaultTex.width, top + overlayPixel);
            Rect bottomRect = new Rect(0, defaultTex.height - (bottom + overlayPixel), defaultTex.width, bottom + overlayPixel);
            Rect leftRect = new Rect(0, 0, left + overlayPixel, m_pic.sprite.texture.height);
            Rect rightRect = new Rect(defaultTex.width - (right + overlayPixel), 0, right + overlayPixel, m_pic.sprite.texture.height);

            Color drawColor = new Color(1, 1, 1, 1);


            // Set all four rects
          
            m_mask.sprite.texture.SetPixels((int)topRect.x, (int)topRect.y, (int)topRect.width, (int)topRect.height, Enumerable.Repeat(drawColor, (int)topRect.width * (int)topRect.height).ToArray());
            yield return null;
            m_mask.sprite.texture.SetPixels((int)bottomRect.x, (int)bottomRect.y, (int)bottomRect.width, (int)bottomRect.height, Enumerable.Repeat(drawColor, (int)bottomRect.width * (int)bottomRect.height).ToArray());
            yield return null;
            m_mask.sprite.texture.SetPixels((int)leftRect.x, (int)leftRect.y, (int)leftRect.width, (int)leftRect.height, Enumerable.Repeat(drawColor, (int)leftRect.width * (int)leftRect.height).ToArray());
            yield return null; 
            m_mask.sprite.texture.SetPixels((int)rightRect.x, (int)rightRect.y, (int)rightRect.width, (int)rightRect.height, Enumerable.Repeat(drawColor, (int)rightRect.width * (int)rightRect.height).ToArray());
            yield return null;
            // Make sure the mask is shown
            m_picMaskScript.SetMaskVisible(true);
            m_targetRectScript.UpdatePoints();
          
        }

        m_mask.sprite.texture.Apply();
        m_pic.sprite.texture.Apply();
        m_picMaskScript.SetMaskModified(true);
    }



    public void Resize(int newWidth, int newHeight, bool bKeepAspect, FilterMode filterMode = FilterMode.Bilinear)
    {
        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
            return;
        }

        AddImageUndo(true);

        if (bKeepAspect)
        {

            //crop our texture to correct aspect ratio first
            var croppedTexture = ResizeTool.CropTextureToAspectRatio(m_pic.sprite.texture, newWidth, newHeight);

            UnityEngine.Object.Destroy(m_pic.sprite.texture); //this will also kill the sprite?

            //now do the real resizing
            ResizeTool.Resize(croppedTexture, newWidth, newHeight, false, filterMode);
            float biggestSize = Math.Max(croppedTexture.width, croppedTexture.height);
            m_pic.sprite = Sprite.Create(croppedTexture, new Rect(0, 0, croppedTexture.width, croppedTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        }
        else
        {
            ResizeTool.Resize(m_pic.sprite.texture, newWidth, newHeight, false, filterMode);
            float biggestSize = Math.Max(m_pic.sprite.texture.width, m_pic.sprite.texture.height);
            m_pic.sprite = Sprite.Create(m_pic.sprite.texture, new Rect(0, 0, m_pic.sprite.texture.width, m_pic.sprite.texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        }

        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();

        m_targetRectScript.OnMoveToPixelLocation(new Vector2(0, 0));
        SetNeedsToUpdateInfoPanelFlag();

    }

    public void OnTileButton()
    {

        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
            return;
        }

        //AddImageUndo(true); //actually why should we, pushing on tile again will perfectly recreate the original
        
        ResizeTool.SetupAsTile(m_pic.sprite.texture, m_pic.sprite.texture.filterMode);
    }
    public void StopFileWatchingIfNeeded()
    {
        if (m_editFileWatcher != null)
        {
            m_editFileWatcher.Changed -= OnChanged;
            m_editFileWatcher.EnableRaisingEvents = false;
            m_editFileWatcher = null;
        }
    }

    public void SetCurrentEvent(UndoEvent newEvent)
    {
        //instead of assign newEvent to m_curEvent, we'll copy it
        m_curEvent = new UndoEvent();
        
        //it would be slow to copy each thing by hand, so we'll itterate through its properties and copy them
        foreach (var prop in newEvent.GetType().GetFields())
        {
            prop.SetValue(m_curEvent, prop.GetValue(newEvent));
        }

        m_curEvent.m_sprite = null;
        m_curEvent.m_texture = null;


       

    }
    public GameObject Duplicate()
    {
        //make a copy of this entire object

        GameObject go = ImageGenerator.Get().CreateNewPic();
        PicMain targetPicScript = go.GetComponent<PicMain>();
        
        //if in adventure mode, go ahead and move it to the right of us
        if (AdventureLogic.Get().IsActive())
        {
            go.transform.position = new Vector3(transform.position.x + 5.12f, transform.position.y, transform.position.z);
        }

        PicTargetRect targetRectScript = go.GetComponent<PicTargetRect>();
        PicTextToImage targetTextToImageScript = go.GetComponent<PicTextToImage>();
        
        targetPicScript.SetImage(m_pic.sprite.texture, true);
        targetPicScript.SetMask(m_mask.sprite.texture, true);
        targetTextToImageScript.SetSeed(m_picTextToImageScript.GetSeed()); //if we've set it, it will carry to the duplicate as well
        targetTextToImageScript.SetTextStrength(m_picTextToImageScript.GetTextStrength()); //if we've set it, it will carry to the duplicate as well
        targetTextToImageScript.SetPrompt(m_picTextToImageScript.GetPrompt()); //if we've set it, it will carry to the duplicate as well
        targetTextToImageScript.SetNegativePrompt(m_picTextToImageScript.GetNegativePrompt()); //if we've set it, it will carry to the duplicate as well
        targetRectScript.SetOffsetRect(m_targetRectScript.GetOffsetRect());
        targetPicScript.SetCurrentEvent(m_curEvent); //copy the current event
        return go;
    }

    public void SetImage(Texture2D newImage, bool bDoFullCopy)
    {
        if (bDoFullCopy)
        {
            newImage = newImage.Duplicate();
        }

        float biggestSize = Math.Max(newImage.width, newImage.height);

        UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(newImage,
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);
       
        if (m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            UnityEngine.Object.Destroy(m_pic.sprite.texture); 
        }

        m_pic.sprite = newSprite;
        OnImageReplaced();
        MovePicUpIfNeeded();
        SetNeedsToUpdateInfoPanelFlag();
    }

    public void InvertMask()
    {
        Debug.Log("invert mask");
        m_mask.sprite.texture.Invert();
        m_mask.sprite.texture.Apply();
        //SetMask(m_mask.sprite.texture, false);
    }

    public void SetMask(Texture2D newImage, bool bDoFullCopy)
    {
        if (bDoFullCopy)
        {
            var originalImage = newImage;
            newImage = originalImage.Duplicate();
            
            //newImage.SetPixelsFromTextureWithAlphaMask(newImage, originalImage);
            //newImage.Fill(0.0);
            //newImage.FillAlpha(1.0f);
            //newImage.Apply();
        }

        float biggestSize = Math.Max(newImage.width, newImage.height);

        UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(newImage,
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);

        m_picMaskScript.SetMaskFromSprite(newSprite);
    }

    public void OnDuplicateButton()
    {
        Duplicate();
    }

    public void OnFileEditButton()
    {
        RTQuickMessageManager.Get().ShowMessage("Opening 32 bit bmp with alpha mask in image editor...");
        RTMessageManager.Get().Schedule(0.1f, OnFileEdit);
    }

    public void OnFileEdit()
    {
     
        //if we previously had a watch going, kill it
        StopFileWatchingIfNeeded();
        
        if (m_editFilename == "")
        {
            string tempDir = Application.dataPath;
            tempDir = tempDir.Replace('/', '\\');
            tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));
            tempDir += "\\tempCache";
            Directory.CreateDirectory(tempDir);
            m_editFilename = tempDir + "\\pic_" + System.Guid.NewGuid() + ".bmp";
        }

       
        SaveFile(m_editFilename); //if m_editFilename is blank, it will create a random one
      
        RunProcess(Config.Get().GetImageEditorPathAndExe(), false, m_editFilename);

        m_editFileWatcher = new FileSystemWatcher();
        m_editFileWatcher.Path = Path.GetDirectoryName(m_editFilename);
        m_editFileWatcher.Filter = Path.GetFileName(m_editFilename);

        // Watch for changes in LastAccess and LastWrite times, and
        // the renaming of files or directories.
        m_editFileWatcher.NotifyFilter = NotifyFilters.LastWrite;

        // Add event handlers
        m_editFileWatcher.Changed += OnChanged;

        // Begin watching
        m_editFileWatcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        RTConsole.Log("File we're editing has changed");
        m_editFileHasChanged = true;
    }

    public void InvalidateExportedEditFile()
    {
        if (m_editFilename == "") return;

        if (File.Exists(m_editFilename))
        {
            File.Delete(m_editFilename);
        }
        StopFileWatchingIfNeeded();
    }

    static void RunProcess(string command, bool runShell, string args = null)
    {
        //string projectCurrentDir = Directory.GetCurrentDirectory();
        //command = projectCurrentDir + "/" + command;
        try
        {
            UnityEngine.Debug.Log(string.Format("{0} Run command: {1}", DateTime.Now, command));

        System.Diagnostics.ProcessStartInfo ps = new System.Diagnostics.ProcessStartInfo(command);
        using (System.Diagnostics.Process p = new System.Diagnostics.Process())
        {
            ps.UseShellExecute = runShell;
            if (!runShell)
            {
                ps.RedirectStandardOutput = true;
                ps.RedirectStandardError = true;
                ps.StandardOutputEncoding = System.Text.ASCIIEncoding.ASCII;
            }

            if (args != null && args != "")
            {
                ps.Arguments = args;
            }
            p.StartInfo = ps;
            p.Start();
            
            /*
            p.WaitForExit();
            if (!runShell)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(output))
                {
                    UnityEngine.Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, output));
                }

                string errors = p.StandardError.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(errors))
                {
                    UnityEngine.Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, errors));
                }
            }
            */
        }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError(string.Format("{0} Failed to run command '{1}'. Error: {2}", DateTime.Now, command, ex.Message));
            RTQuickMessageManager.Get().ShowMessage("Failed to run command: " + command);
        }
    }

    public void OnImageReplaced()
    {
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();
    }

    public void AutoSaveImageIfNeeded()
    {
        if (GameLogic.Get().GetAutoSave())
        {
            SaveFile("", "/"+ Config._saveDirName);
        }

        if (GameLogic.Get().GetAutoSavePNG())
        {
            SaveFile("", "/"+Config._saveDirName, null, "", true);
        }
    }

    //save to a random filename if passed a blank filename
    public string SaveFile(string fname="", string subdir = "", Texture2D texToSave = null, string fNamePostFix = "", bool bSaveAsPNG =false, bool bWriteOutTextFileToo = true) 
    {
  
        string fileName = Config.Get().GetBaseFileDir(subdir) + "\\pic_" + System.Guid.NewGuid() + fNamePostFix ;

        if (bSaveAsPNG)
        {
            fileName += ".png";
        } else
        {
            fileName += ".bmp";
        }

        if (fname != null && fname != "")
        {
            fileName = fname;
        }
        byte[] pngBytes;


        if (bSaveAsPNG)
        {
            if (texToSave == null)
            {
                pngBytes = m_pic.sprite.texture.EncodeToPNG();

            }
            else
            {
                bWriteOutTextFileToo = false;
                pngBytes = texToSave.EncodeToPNG();
            }

        }
        else
        {
            if (texToSave == null)
            {
                pngBytes = m_pic.sprite.texture.EncodeToBMP(m_mask.sprite.texture);

            }
            else
            {
                bWriteOutTextFileToo = false;
                pngBytes = texToSave.EncodeToBMP();
            }
        }

        File.WriteAllBytes(fileName, pngBytes);

        if (bWriteOutTextFileToo)
        {
            //write out a text file with the same name, but with .txt extension
            string txtFileName = fileName.Substring(0, fileName.Length - 4) + ".txt";
            UpdateInfoPanel(); //OPTIMIZE:  We don't really need to do it if it's already been done...
            File.WriteAllText(txtFileName, m_infoPanelScript.GetTextInfoWithoutColors());
        }

        RTQuickMessageManager.Get().ShowMessage("Saved " + fileName);
        return fileName;
    }

    public string SaveFileJPG(string fname = "", string subdir = "", Texture2D texToSave = null, string fNamePostFix = "",int JPGquality = 80)
    {

        string fileName = Config.Get().GetBaseFileDir(subdir) + "\\pic_" + System.Guid.NewGuid() + fNamePostFix;

            fileName += ".jpg";
       
        if (fname != null && fname != "")
        {
            fileName = fname;
        }
        byte[] pngBytes;


        bool bWriteOutTextFileToo = true;


        if (texToSave == null)
        {
            pngBytes = m_pic.sprite.texture.EncodeToJPG(JPGquality);

        }
        else
        {
            bWriteOutTextFileToo = false;
            pngBytes = texToSave.EncodeToJPG(JPGquality);
        }

        File.WriteAllBytes(fileName, pngBytes);

        if (bWriteOutTextFileToo)
        {
            //write out a text file with the same name, but with .txt extension
            string txtFileName = fileName.Substring(0, fileName.Length - 4) + ".txt";
            UpdateInfoPanel(); //OPTIMIZE:  We don't really need to do it if it's already been done...
            File.WriteAllText(txtFileName, m_infoPanelScript.GetTextInfoWithoutColors());
        }

        RTQuickMessageManager.Get().ShowMessage("Saved " + fileName);
        return fileName;
    }

    public void SaveFileNoReturn2()
    {
        SaveFile("", "/" + Config._saveDirName);
    }

    public void SaveFilePNG()
    {
        SaveFile("", "/" + Config._saveDirName, null,"",true);
    }

    public void SaveFileNoReturn()
    {
        RTQuickMessageManager.Get().ShowMessage("Saving image...");

        RTMessageManager.Get().Schedule(0.01f, this.SaveFileNoReturn2);
    }

    //a method I tried that crashed all the time, don't know why

    /*
    public void OpenFile()
    {

        var fileContent = string.Empty;
        var filePath = string.Empty;
        Debug.Log("opening file");
        OpenFileDialog openFileDialog = new OpenFileDialog();

        openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();
        openFileDialog.Filter = "images files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        Debug.Log("opening file dialog");

        var results = openFileDialog.ShowDialog();

        if (results == DialogResult.OK)
        {

            filePath = openFileDialog.FileName;
            Debug.Log("Chose " + filePath);

            //Get the path of specified file
            var png = File.ReadAllBytes(filePath);

            try
            {
                var buffer = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                texture.LoadImage(buffer);
                AddImageUndo();
                Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestsize / 5.12f);
                m_pic.sprite = newSprite;

            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.StackTrace);
            }


        }
        else
        {
            Debug.LogError("Error loading file " + filePath);
        }
    }
    */


    public void OnInterrogateButton()
    {
         var e = new ScheduledGPUEvent();
        e.mode = "interrogate";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnUpscaleButton()
    {
        var e = new ScheduledGPUEvent();
        e.mode = "upscale";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void AddTextLabelToImage(string label)
    {
        if (label == null || label.Count() == 0) return;

        float bottom = 64;

        Rect textAreaRect = new Rect(0, 0, m_pic.sprite.texture.width, bottom);

        float maxSize = textAreaRect.width;
        if (textAreaRect.height > maxSize)
        {
            maxSize = textAreaRect.height;
        }

        float minSize = textAreaRect.width;
        if (textAreaRect.height < minSize)
        {
            minSize = textAreaRect.height;
        }

        float fontSize = maxSize / 4;
        float titleHeight = 0;
        titleHeight = textAreaRect.height * 0.34f;

        Texture2D tex;
        Rect rect;

        TMPro.FontStyles fontStyles = TMPro.FontStyles.Bold;
        int fontID = 0;

        tex = RTUtil.RenderTextToTexture2D(label, (int)textAreaRect.width,
           (int)(textAreaRect.height - titleHeight), AIGuideManager.Get().GetFontByID(fontID), fontSize, Color.white,
        false, new Vector2(1, 1), fontStyles);

        //File.WriteAllBytes("crap.png", tex.EncodeToPNG()); //for debugging

        rect = textAreaRect;
        rect.yMax -= titleHeight;
        //add it to our real texture
        m_pic.sprite.texture.BlitWithAlpha((int)rect.xMin, (int)(m_pic.sprite.texture.height - rect.height), tex,
            0, 0, (int)rect.width, (int)rect.height);
        m_pic.sprite.texture.Apply();

    }

    public void CleanupPixelArt()
    {
        AddTextLabelToImage("AI Generated");
        m_pic.sprite.texture.filterMode = FilterMode.Point;
        int originalWidth = m_pic.sprite.texture.width;
        int originalHeight = m_pic.sprite.texture.height;

        Resize(128, 128, true, FilterMode.Point);
        m_pic.sprite.texture.filterMode = FilterMode.Point;
        m_pic.sprite.texture.Apply();


        //now let's resize it back up to the original size it was using our saved width/height
       Resize(originalWidth, originalHeight, true, FilterMode.Point);
       m_pic.sprite.texture.filterMode = FilterMode.Point;
       m_pic.sprite.texture.Apply();
    }
    public void OnToggleSmoothing()
    {
        if (m_pic.sprite.texture.filterMode != FilterMode.Point)
        {
            m_pic.sprite.texture.filterMode = FilterMode.Point;
            if (m_undoevent != null && m_undoevent.m_texture != null)
            {
                m_undoevent.m_texture.filterMode = FilterMode.Point;
            }
        } else
        { 
               
           m_pic.sprite.texture.filterMode = FilterMode.Bilinear;
      
            if (m_undoevent != null && m_undoevent.m_texture != null)
             {
                m_undoevent.m_texture.filterMode = FilterMode.Bilinear;
             }
        }
      
    }

    public void FillAlphaMaskWithImageAlpha()
    {
        m_picMaskScript.SetMaskFromTextureAlpha(m_pic.sprite.texture);
    }


    public void FillAlphaMaskIfBlank()
    {
        m_picMaskScript.FillAlphaMaskIfBlank();
    }
    public void OnInpaintButton()
    {
        var e = new ScheduledGPUEvent();
        e.mode = "inpaint";
        e.targetObj = this.gameObject;
        FillAlphaMaskIfBlank();

        ImageGenerator.Get().ScheduleGPURequest(e);
        //ImageGenerator.Get().SetLastImg2ImgObject(this.gameObject); //if we wanted a non batch img2img to count for the restarting batch img2img button
        SetStatusMessage("Waiting for GPU...");
    }

    IEnumerator MutateAndWait()
    {
        //RGB texture test:

        var blurFilter = new ConvFilter.BoxBlurFilter();
        var processor = new ConvFilter.ConvolutionProcessor(m_pic.sprite.texture);

        for (int i = 0; i < 2; i++)
        {
            yield return StartCoroutine(processor.ComputeWith(blurFilter));
            processor = new ConvFilter.ConvolutionProcessor(processor.m_originalMap);
        }

        Debug.Log("Done!");

        var newTex = processor.m_originalMap;
        float biggestSize = Math.Max(newTex.width, newTex.height);

        Sprite newSprite = Sprite.Create(newTex, new Rect(0, 0, newTex.width, newTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        m_pic.sprite = newSprite;
        m_pic.sprite.texture.Apply();

    }

    public void OnGenerateMaskButton()
    {
        GameLogic.Get().ShowCompatibilityWarningIfNeeded();
        Debug.Log("Generating mask...");
        var e = new ScheduledGPUEvent();
        e.mode = "genmask";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnGenerateMaskButtonSimple()
    {
        GameLogic.Get().ShowCompatibilityWarningIfNeeded();
        Debug.Log("Generating mask...");
        var e = new ScheduledGPUEvent();
        e.mode = "genmask";
        e.targetObj = this.gameObject;
        e.disableTranslucency = true;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnMutateButton()
    {
        Debug.Log("Blurring..");
        AddImageUndo(true);

        StartCoroutine(MutateAndWait());

       //texture.Apply();

       // byte[] picPng = texture.EncodeToPNG();
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //have to rebuild the sprite though
     
       
        //Mask test

        /*
        byte[] picPng = m_mask.sprite.texture.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/../PreSavedScreen.png", picPng);

        //Texture2D texture = m_mask.sprite.texture.GetGaussianBlurredVersion();

        int revolutions = 4;
        Texture2D texture = m_mask.sprite.texture;

        for (int i = 0; i < revolutions; i++)
        {
           texture = texture.GetBlurredVersion();
        }

        picPng = texture.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //have to rebuild the sprite though
        Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width / 5.12f);
        m_mask.sprite = newSprite;
        m_mask.sprite.texture.Apply();

        */


        /*
        var e = new ScheduledGPUEvent();
        e.mode = "mutate";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
        */
    }

    public void OnReRenderCopyButton()
    {
        var newPic = Duplicate();

        var e = new ScheduledGPUEvent();
        e.mode = "rerender";
        e.targetObj = newPic;

        //if comfyui was used, make sure we specify that
        if (GetCurrentStats().m_gpu != -1)
        {
            e.requestedRenderer = Config.Get().GetGPUInfo(GetCurrentStats().m_gpu)._requestedRendererType;
        }
        else
        {
            //try to get it from somewhere else
            e.requestedRenderer = GetCurrentStats().m_requestedRenderer;
        }
        PicMain newPicMain = newPic.GetComponent<PicMain>();
        //set the seed of this pic
        PicTextToImage textToImage = GetComponent<PicTextToImage>();
        textToImage.SetSeed(-1); //causes it to be random again

        ImageGenerator.Get().ScheduleGPURequest(e);
        newPicMain.SetStatusMessage("Waiting for GPU...");
    }
  
    public void OnReRenderButton()
    {
        AddImageUndo(true);
        var e = new ScheduledGPUEvent();
        e.mode = "rerender";
        e.targetObj = this.gameObject;

        if (GetCurrentStats().m_gpu != -1)
        {
            e.requestedRenderer = Config.Get().GetGPUInfo(GetCurrentStats().m_gpu)._requestedRendererType;
        }
        else
        {
            //try to get it from somewhere else
            e.requestedRenderer = GetCurrentStats().m_requestedRenderer;
        }

        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");

    }
    public void OnRenderWithDalle3Button()
    {
       OnRenderWithDalle3();
    }

    public void StartGenericTimer(string text)
    {
        m_genericTimerText = text;
        m_genericTimerStart = Time.realtimeSinceStartup;
    }

    public void StopGenericTimer()
    {
        m_genericTimerText = "Done";
        m_genericTimerStart = 0;
    }

    public void OnRenderWithDalle3()
    {
       
        var e = new ScheduledGPUEvent();
        GetCurrentStats().m_requestedRenderer = RTRendererType.OpenAI_Dalle_3;

        //let's set the GPUID for the heck of it

        GetCurrentStats().m_gpu = Config.Get().GetFreeGPU(RTRendererType.OpenAI_Dalle_3, true);

        if (GetCurrentStats().m_gpu == -1)
        {
            //write message they can see
            RTQuickMessageManager.Get().ShowMessage("No Dalle-3 server is connected to, check your config");
            return;
        }

        SetStatusMessage("Waiting for Dalle3...");

        //if prompt is null, we'll set it
        if (m_picTextToImageScript.GetPrompt() == null || m_picTextToImageScript.GetPrompt().Length < 1)
        {
            m_picTextToImageScript.SetPrompt(GameLogic.Get().GetPrompt());
        }

        Dalle3Manager dalle3Script = gameObject.GetComponent<Dalle3Manager>();

        if (dalle3Script == null)
        {
            Debug.Log("Adding dalle3 script");
            dalle3Script = gameObject.AddComponent<Dalle3Manager>();
        }

        string json = dalle3Script.BuildJSON(m_picTextToImageScript.GetPrompt(), "dall-e-3");

        //test
        RTDB db = new RTDB();
        dalle3Script.SpawnRequest(json, OnDalle3CompletedCallback, db, Config.Get().GetOpenAI_APIKey());

        //Oh, let's start a timer
        StartGenericTimer("Dalle3...");
    }

    public void OnDalle3CompletedCallback(RTDB db, Texture2D texture)
    {
        StopGenericTimer();
        if (texture == null)
        {
            Debug.Log("Error getting dalle image: " + db.GetString("msg"));

            //if 429 (Too Many Requests) is in the text we'll wait and try again
            if (db.GetString("msg").Contains("429"))
            {
               Debug.Log("Got 429, waiting 5 seconds and trying again");
               RTMessageManager.Get().Schedule(UnityEngine.Random.Range(5.0f, 10.0f), OnRenderWithDalle3);
            }
            else
            {

                SetStatusMessage("" + db.GetString("msg"));
            }
            return;
        }

        AddImageUndo(true);

        SetStatusMessage("");
        //only do the undo if our sprite texture is valid

        SetImage(texture, true);
      
        GetCurrentStats().m_lastPromptUsed = m_picTextToImageScript.GetPrompt();
        GetCurrentStats().m_lastNegativePromptUsed = "";
        GetCurrentStats().m_lastSteps = 0;
        GetCurrentStats().m_lastCFGScale = 0;
        GetCurrentStats().m_lastSampler = "N/A";
        GetCurrentStats().m_tiling = false;
        GetCurrentStats().m_fixFaces = false;
        GetCurrentStats().m_lastSeed = 0;
        GetCurrentStats().m_lastModel = "Dalle 3";
        GetCurrentStats().m_bUsingControlNet = false;
        GetCurrentStats().m_bUsingPix2Pix = false;
        GetCurrentStats().m_lastOperation = "Dalle 3";
        
        SetNeedsToUpdateInfoPanelFlag();
        AutoSaveImageIfNeeded();
        
        if (m_onFinishedRenderingCallback != null)
            m_onFinishedRenderingCallback.Invoke(gameObject);
    }
    void RemoveScheduledCalls()
    {
        RTMessageManager.Get().Schedule(UnityEngine.Random.Range(5.0f, 10.0f), OnRenderWithDalle3);

        RTMessageManager.Get().RemoveScheduledCalls((System.Action)OnRenderWithDalle3);
    }
    public void OnRenderWithAITOrA1111()
    {
        int gpuID = Config.Get().GetFreeGPU(RTRendererType.AI_Tools_or_A1111, false);
        
        if (gpuID == -1)
        {
            //write message they can see
            RTQuickMessageManager.Get().ShowMessage("No AIT/A1111 server is connected to, check your config");
            return;
        }

        GetCurrentStats().m_requestedRenderer = RTRendererType.AI_Tools_or_A1111;
        GetCurrentStats().m_gpu = Config.Get().GetFreeGPU(GetCurrentStats().m_requestedRenderer, true); //show we don't care

        RemoveScheduledCalls();
        OnReRenderNewSeedButton();
    }

    public void OnRenderWithComfyUI()
    {
        int gpuID = Config.Get().GetFreeGPU(RTRendererType.ComfyUI, true);

        if (gpuID == -1)
        {
            //write message they can see
            RTQuickMessageManager.Get().ShowMessage("No ComfyUI server is connected to, check your config");
            return;
        }

        GetCurrentStats().m_requestedRenderer = RTRendererType.ComfyUI;
        GetCurrentStats().m_gpu = Config.Get().GetFreeGPU(GetCurrentStats().m_requestedRenderer, true); //show we don't care

        RemoveScheduledCalls();
        OnReRenderNewSeedButton();
    }

    public void OnReRenderNewSeedButton()
    {
        RemoveScheduledCalls();
        AddImageUndo();
        var e = new ScheduledGPUEvent();
        e.mode = "rerender";
        
        if (GetCurrentStats().m_gpu != -1)
        {
            e.requestedRenderer = Config.Get().GetGPUInfo(GetCurrentStats().m_gpu)._requestedRendererType;
        }
        else
        {

            //try to get it from somewhere else
            e.requestedRenderer = GetCurrentStats().m_requestedRenderer;
        }
        e.targetObj = this.gameObject;
        PicTextToImage textToImage = GetComponent<PicTextToImage>();
        textToImage.SetSeed(-1); //causes it to be random again
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");

    }

    //I return it, in case the caller wants to set additional parms
    public ScheduledGPUEvent OnRenderButton(string promptAddition)
    {
        RemoveScheduledCalls();
        var e = new ScheduledGPUEvent();
        e.mode = "render";
        e.targetObj = this.gameObject;
        e.promptOverride = promptAddition;
        
        if (GetCurrentStats().m_gpu != -1)
        {
            e.requestedRenderer = Config.Get().GetGPUInfo(GetCurrentStats().m_gpu)._requestedRendererType;
        }
        else
        {
            //try to get it from somewhere else
            e.requestedRenderer = GetCurrentStats().m_requestedRenderer;
        }

        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
        return e;
    }


    private void OnDestroy()
    {
       InvalidateExportedEditFile();

        if (m_pic.sprite && m_pic.sprite.texture)
        {
            UnityEngine.Object.Destroy(m_pic.sprite.texture); //this will also kill the sprite?
            UnityEngine.Object.Destroy(m_pic.sprite);
        }

        KillUndoImageBuffers();
    }

    public void SetNeedsToUpdateInfoPanelFlag()
    {
        m_bNeedsToUpdateInfoPanel = true;
    }

    public void SetControlImage(Texture tex)
    {

        float biggestSize = Math.Max(tex.width, tex.height);

        Sprite newSprite = Sprite.Create(tex as Texture2D, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        newSprite.texture.Apply();
        m_infoPanelScript.SetSprite(newSprite);
      
    }
    public void UpdateInfoPanel()
    {
        m_infoPanelScript.SetInfoText(GetInfoText());
        m_bNeedsToUpdateInfoPanel = false;

    }
    // Update is called once per frame
    void Update()
    {

        if (m_editFileHasChanged)
        {
            m_editFileHasChanged = false;

            AddImageUndo();
            LoadImageByFilename(m_editFilename, false, true);
        }

        //mask things faster when zoomed out by not rendering the GUI

        if (m_camera.orthographicSize > 9 || !m_pic.isVisible)
        {
            if (m_canvas.enabled)
            {
                m_canvas.enabled = false;
            }
        }
        else
        {
            m_canvas.enabled = true;
        }

       
        if (m_bNeedsToUpdateInfoPanel)
        {
            if (m_infoPanelScript.IsPanelOpen())
            {
                UpdateInfoPanel();
            }
            
        }

        if (m_genericTimerStart != 0)
        {
            float elapsed = Time.realtimeSinceStartup - m_genericTimerStart;

            m_text.text = m_genericTimerText+" "+elapsed.ToString("0.0#");
            
        }
    }

      public void PassInTempInfo(ScheduledGPUEvent e)
    {
        //this doesn't really matter, but let's update our current info based on what we think will happen
        GetCurrentStats().m_lastPromptUsed = m_picTextToImageScript.GetPrompt();
        GetCurrentStats().m_requestedRenderer = e.requestedRenderer;
    }

    public void PassInTempInfo(RTRendererType requestedRenderer, int gpuID)
    {
        GetCurrentStats().m_lastPromptUsed = m_picTextToImageScript.GetPrompt();
        GetCurrentStats().m_requestedRenderer = requestedRenderer;
        GetCurrentStats().m_gpu = gpuID;
    }

}
