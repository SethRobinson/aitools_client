using System.Collections;
using UnityEngine;
using System.IO;
using System;
using TMPro;
using B83.Image.BMP;
using UnityEngine.Rendering;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;

public class PicJobData
{
    public string _name = ""; // can be "replace"
    public string _parm1 = ""; // thing to replace
    public string _parm2 = ""; // thing to replace with

    public PicJobData Clone()
    {
        return new PicJobData()
        {
            _name = this._name,
            _parm1 = this._parm1,
            _parm2 = this._parm2
        };
    }
}
public class PicJob
{
    public string _job = "none";
    public string _parm_1_string = "";
    public string _workflow = "";
    public string _requestedAudioPrompt = "";
    public string _requestedAudioNegativePrompt = "";
    public string _requestedPrompt = "";
    public string _requestedNegativePrompt = "";
    public string _requestedSegmentationPrompt = "";
    public RTRendererType requestedRenderer = RTRendererType.ComfyUI;
    public int _serverID = -1;
    public List<PicJobData> _data = new List<PicJobData>();
    public string _originalJobString = "";
    public float _timeOfStart = 0;
    public float _timeOfEnd = 0;
    public PicJob Clone()
    {
        PicJob clone = (PicJob)this.MemberwiseClone();
        clone._data = new List<PicJobData>();

        foreach (PicJobData d in _data)
        {
            clone._data.Add(d.Clone());
        }

        return clone;
    }
}

public class UndoEvent
{
    public Texture2D m_texture;
    public Sprite m_sprite;
    public bool m_active = false;

    public RTRendererType m_requestedRenderer = RTRendererType.Any_Local;
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
    public PicJob m_picJob = new PicJob();
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
    public PicMovie m_picMovie;
    List<PicJob> m_picJobs = new List<PicJob>();
    List<PicJob> _jobHistory = new List<PicJob>();
    PicJob m_jobDefaultInfo = null;
    

    string m_mediaRemoteFilename = ""; //sometimes media has no name because it was generated, but we need to know the filename for sending/loading remotely on ComfyUI

    bool m_bNeedsToUpdateInfoPanel = false;

    public Action<GameObject> m_onFinishedRenderingCallback;

    public Camera m_camera;
    List<string> m_jobList = new List<string>();
    bool m_allowServerJobOverrides = true;
    UndoEvent m_undoevent = new UndoEvent();
    UndoEvent m_curEvent = new UndoEvent(); //useful for just saving the current status, makes it easy to copy to/from a real undo event
    bool m_isDestroyed;
    string m_editFilename = "";
    bool m_bLocked;
    bool m_bDisableUndo;
    float m_genericTimerStart = 0; //used to countdown for Dalle3
    string m_genericTimerText = "Waiting...";
    bool m_noUndo = false;

    public AIGuideManager.PassedInfo m_aiPassedInfo; //a misc place to store things the AI guide wants to
    bool m_waitingForPicJob = false;

    public void SetMediaRemoteFilename(string fname)
    {
        m_mediaRemoteFilename = fname;
    }
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

    public void UnloadToSaveMemoryIfPossible()
    {
        if (IsMovie())
        {
            m_picMovie.UnloadTheMovieToSaveMemory();
        }
    }
    public void MakeDraggable()
    {

        //we want to be able to drag this around, so we'll have to disable the mask drawing stuff
        //m_picMaskScript.SetMaskVisible(false);
        //Add ObjectDrag to us
        

    }
    public string GetInfoText()
    {

        //go through every job in _jobHistory and add it to the string
        string msg = "";
        int jobCounter = 1;
        string c1 = "`4";


        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            msg += $@"{c1}Image size X:`` {(int)m_pic.sprite.texture.width} {c1}, Y: ``{(int)m_pic.sprite.texture.height}";
        }

        msg += $@" {c1}Mask Rect size X: ``{(int)m_targetRectScript.GetOffsetRect().width}{c1}, Y: ``{(int)m_targetRectScript.GetOffsetRect().height}
";

        foreach (var job in _jobHistory)
        {
            string serverTemp = "none";

            if (job._serverID != -1)
            {
                serverTemp = Config.Get().GetServerNameByGPUID(job._serverID);
            }   

            string finished = "";

            //calculate the seconds taken
            if (job._timeOfEnd > 0)
            {
                float seconds = job._timeOfEnd - job._timeOfStart;
                finished = $@"{c1}Time:`` {seconds.ToString("0.0#")}s";
            }

            if (msg != "") msg += "\n";
            msg += $@"{c1}#{jobCounter}`` {c1}{job._job}:`` {job._workflow} {c1}Server ID:`` {serverTemp} {finished}
{c1} Prompt:`` {job._requestedPrompt}
{c1} Neg Prompt:`` {job._requestedNegativePrompt}
{c1} Audio Prompt:`` {job._requestedAudioPrompt}
{c1} Neg Audio Prompt:`` {job._requestedAudioNegativePrompt}
{c1} Segmentation Prompt:`` {job._requestedSegmentationPrompt}
{c1} Parm 1: ``{job._parm_1_string}";

            //go through each _data and display it
            foreach (var data in job._data)
            {
                msg += $@"{c1} Mod:`` {data._name} {c1}Old:`` {data._parm1} {c1}New:`` {data._parm2}\n";
            }


            jobCounter++;
        }

        if (m_jobList.Count > 0)
        {
            msg += $@"
{c1}Queued:``
";

            int joblistCounter = 0;
            foreach (var job in m_jobList)
            {
                if (joblistCounter > 0) msg += "\n";
                msg += $@" {job}";
            }
        }

        /*
        var c = GetCurrentStats();

        string c1 = "`4";

        string rendererRequested = GetCurrentStats().m_requestedRenderer.ToString();

        string msg =
$@"`8{c1}Last Operation:`` {c.m_lastOperation} {c1}Renderer:`` {rendererRequested} {c1}on ServerID: ``{c.m_gpu} {Config.Get().GetServerNameByGPUID(c.m_gpu)} ({Config.Get().GetServerAddressByGPUID(c.m_gpu)})
";

        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            msg += $@"{c1}Image size X:`` {(int)m_pic.sprite.texture.width} {c1}, Y: ``{(int)m_pic.sprite.texture.height}";
        }

msg += $@" {c1}Mask Rect size X: ``{(int)m_targetRectScript.GetOffsetRect().width}{c1}, Y: ``{(int)m_targetRectScript.GetOffsetRect().height}
{c1}Model:`` {c.m_lastModel}
{c1}Sampler:`` {c.m_lastSampler} {c1}Steps:`` {c.m_lastSteps} {c1}Seed:`` {c.m_lastSeed}
{c1}CFG Scale:`` {c.m_lastCFGScale} {c1}Tiling: ``{c.m_tiling} {c1}Fix Faces:`` {c.m_fixFaces}
{c1}Prompt:`` {c.m_picJob.requestedPrompt}
{c1}Negative prompt:`` {c.m_picJob.requestedNegativePrompt}
{c1}Audio prompt:`` {c.m_picJob.requestedAudioPrompt}
{c1}Audio negative prompt:`` {c.m_picJob.requestedAudioNegativePrompt}
";

        /*
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
      
        */
        return RTUtil.ConvertSansiToUnityColors(msg);
    }

    public void SafelyKillThisPic()
    {
        KillGPUProcesses();
        m_isDestroyed = true;
        GameObject.Destroy(gameObject);
    }

    public void SafelyKillThisPicAndDeleteHoles()
    {
        KillGPUProcesses();
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
        if (m_picJobs.Count > 0) return true;
        if (m_jobList.Count > 0) return true;
        return false;
    }


    public int GetJobListLeft() { return m_jobList.Count; }
    public int GetJobsLeft() { return m_picJobs.Count; }  

    public bool StillHasJobActivityToDo() { return GetJobListLeft() > 0 || GetJobsLeft() > 0; }

    public bool IsBusyBasic()
    {
        if (m_picTextToImageScript.IsBusy()) return true;
        if (m_picInpaintScript.IsBusy()) return true;
        if (m_picUpscaleScript.IsBusy()) return true;
        if (m_waitingForPicJob) return true;
        return false;
    }

    public bool IsMovie()
    {
        return m_picMovie.IsMovie();
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

        ClearJobs();
    }

    void ClearJobs()
    {
        m_picJobs.Clear();
        m_jobList.Clear();
        m_waitingForPicJob = false;
    }
    public bool GetLocked() { return m_bLocked; }
    public void SetLocked(bool bNew) { m_bLocked = bNew; }
    public void SetStatusMessage(string msg)
    {
        //if (GameLogic.Get().GetTurbo())
        //{
        //    m_text.text = ""; //hack to not show messages when in turbo mode
        //}
        //else
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
        if (m_noUndo) return false;

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
            m_undoevent.m_texture = null;
        }
        //Debug.Log("Image added to undo");

        return true;
    }

    public void UndoImage()
    {
      
        if (m_undoevent.m_active && m_undoevent.m_sprite != null)
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


            string fExt = Path.GetExtension(filename).ToLower();
            bool bNeedToProcessAlpha = false;

            if (fExt == ".mp4" || fExt == ".avi" || fExt == ".mov")
            {
                //special handling for movies
                m_picMovie.SetAutoDeleteFileWhenDone(false);
                m_picMovie.PlayMovie(filename);
                return;
            }

            Debug.Log("Loading "+filename+" from disk");
            var buffer = File.ReadAllBytes(filename);
            Texture2D texture = null;
            
         
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
                texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
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
        
        targetPicScript._jobHistory = new List<PicJob>(_jobHistory); //copy the job history
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

        
        if (GameLogic.Get().GetAnyAutoSave())
        {
            m_picMovie.SaveMovie("/" + Config._saveDirName);
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

        if (IsMovie())
        {
            m_picMovie.SaveMovie("/" + Config._saveDirName);
        }
        else
        {
            SaveFile("", "/" + Config._saveDirName);
        }
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
        //GameLogic.Get().ShowCompatibilityWarningIfNeeded();
        Debug.Log("Generating mask...");
        var e = new ScheduledGPUEvent();
        e.mode = "genmask";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnGenerateMaskButtonSimple()
    {
        //GameLogic.Get().ShowCompatibilityWarningIfNeeded();
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

   

    List<string> ConvertHistoryBackToJobList()
    {
        List<string> jobList = new List<string>();
        foreach (PicJob job in _jobHistory)
        {
            if (job._originalJobString != null)
                jobList.Add(job._originalJobString);
        }
        return jobList;
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
      
        /*
        GetCurrentStats().m_picJob.requestedPrompt = m_picTextToImageScript.GetPrompt();
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
        */
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
    public void ClearErrorsAndJobs()
    {
        ClearJobs();
        SetStatusMessage("");
        if (IsBusy())
        {
            StartCoroutine(m_picTextToImageScript.CancelRender());
        }
    }

    public void OnReRenderButton()
    {
        if (_jobHistory.Count == 0)
        {
            SetStatusMessage("Nothing to re-render.");
            return;
        }

        RemoveScheduledCalls();
        m_picMovie.KillMovie();
        AddImageUndo(true);

        PicJob job = _jobHistory[0]; //remember the starting state

        List<string> jobList = ConvertHistoryBackToJobList();
        _jobHistory.Clear();

        AddJobListWithStartingJobInfo(job, jobList);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnReRenderNewSeedButton()
    {

        OnReRenderButton();

        PicTextToImage textToImage = GetComponent<PicTextToImage>();
        textToImage.SetSeed(-1); //causes it to be random again
    }

    //I return it, in case the caller wants to set additional parms
    public ScheduledGPUEvent OnRenderButton(string promptAddition)
    {
        RemoveScheduledCalls();
        var e = new ScheduledGPUEvent();
        e.mode = "render";
        e.targetObj = this.gameObject;
        //e.promptOverride = promptAddition;

        //let's assert so we can remember to fix this later
        
            Debug.Assert(false, "We need a way to reschedule everything that happened");
        

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

    public void ApplyToolsIfNeededMainWorkflowJob(ref PicJob jobMain)
    {
        //don't try to change jobMain, it's probably coming from a copy

        /*
        if (GameLogic.Get().m_applyTool1Toggle.isOn)
        {
            OnTool1();
            //modify the requestedAudioPrompt on the last entry of jobs

            m_picJobs[m_picJobs.Count - 1].requestedAudioPrompt = jobMain.requestedAudioPrompt;
            m_picJobs[m_picJobs.Count - 1].requestedAudioNegativePrompt = jobMain.requestedAudioNegativePrompt;
            m_picJobs[m_picJobs.Count - 1].requestedPrompt = jobMain.requestedPrompt;
            m_picJobs[m_picJobs.Count - 1].requestedNegativePrompt = jobMain.requestedNegativePrompt;
        }
        */
    }

    public void AddJobListWithStartingJobInfo(PicJob job, List<string> jobList)
    {

      if (m_picJobs.Count == 0)
        {
            //instead of adding it here, let's put it in a special starting default job parms
           if (m_jobDefaultInfo != null)
            {
                //Error message
                RTConsole.Log("Error, we're trying to add a job to the job list, but there's already a default job set!");
            }

            m_jobDefaultInfo = job;
        } else
        {
            RTConsole.Log("Jobs already active so ignoring the default starting info we were sent");
            //m_picJobs.Add(job);
        }

        AddJobList(jobList);
    }

    public void OnTool1Button()
    {
       
        OnTool1();

        if (GetJobsLeft() == 1)
        {
            SetStatusMessage("Waiting for GPU...");
        } else
        {
            //show a message that it's been scheduled on the screen
            RTQuickMessageManager.Get().ShowMessage("Running joblist...");
        }
    }

    public void OnTool1() //dotool1
    {

        //if we have no history, let's fill in some defaults
      

        if (_jobHistory.Count() == 0)
        {
            PicJob jobDefaultInfoToStartWith = new PicJob();

            jobDefaultInfoToStartWith._requestedPrompt = GameLogic.Get().GetModifiedGlobalPrompt();
            jobDefaultInfoToStartWith._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
            jobDefaultInfoToStartWith._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            jobDefaultInfoToStartWith._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            jobDefaultInfoToStartWith.requestedRenderer = RTRendererType.ComfyUI;
           
            AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, GameLogic.Get().GetPicJobListAsListOfStrings());
        }
        else
        {

            //Now, we *DO* have history, but we still want to overwrite things with the latest prompts, right?

            m_curEvent.m_picJob._requestedPrompt = GameLogic.Get().GetPrompt();
            m_curEvent.m_picJob._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
            //m_curEvent.m_picJob._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            //m_curEvent.m_picJob._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            
            GameLogic.Get().AddEveryItemToJobList(ref m_jobList);
        }
    }

    public void OnFinishedJob()
    {
        m_waitingForPicJob = false;
        m_curEvent.m_picJob._timeOfEnd = Time.realtimeSinceStartup;

      
        SetNeedsToUpdateInfoPanelFlag();
    }

    public void OnFinishedRenderingWorkflow(bool bSuccess)
    {
        OnFinishedJob();
        //rewrite the last job in our history with this updated version, but bounds check everything to make sure it's safe
        if (_jobHistory.Count > 0)
        {
            _jobHistory[_jobHistory.Count - 1] = m_curEvent.m_picJob;
        } else
        {
            //Show error
            RTConsole.Log("Error:  No job history to update with the latest job");
        }

    }
    void OnUploadFinished(RTDB db)
    {
        OnFinishedJob();
        if (db.GetInt32("success") == 0)
        {
            RTConsole.Log("Error uploading file: " + db.GetString("error")+" canceling other jobs");

            SetStatusMessage("Failed to send\nfile to Server "+db.GetInt32("serverID"));
            ClearJobs();
        }
        else
        {
            RTConsole.Log("File " + db.GetString("name") + " uploaded successfully");
            UpdateJobs();
        }
    }

    public void SetJobWithInfoFromCur(ref PicJob job)
    {
        m_curEvent.m_picJob = job;
        /*
        job._requestedPrompt = m_curEvent.m_picJob._requestedPrompt;
        job._requestedNegativePrompt = m_curEvent.m_picJob._requestedNegativePrompt;
        job._requestedAudioPrompt = m_curEvent.m_picJob._requestedAudioPrompt;
        job._requestedAudioNegativePrompt = m_curEvent.m_picJob._requestedAudioNegativePrompt;
        */
    }

    public void ProcessJobIntoFinal(ref PicJob job)
    {
        string mediaLocalFilename = "";

        if (m_picMovie.IsMovie())
        {
            //the VHS movie loader requires the "temp/" part of the path
            mediaLocalFilename = m_picMovie.GetFileName();
            m_mediaRemoteFilename = m_picMovie.GetFileNameWithoutPath();
        }

        //Replace the <MOVIE_FILENAME> with the actual filename in job.m_string_parm1
        job._parm_1_string = job._parm_1_string.Replace("<MEDIA_FILENAME>", m_mediaRemoteFilename);
        job._parm_1_string = job._parm_1_string.Replace("<MEDIA_LOCAL_FILENAME>", mediaLocalFilename);
    }
  
    public void AddJobList(List<String> jobList)
    {

        foreach (var item in jobList)
        {
            string itemTrimmed = item.Trim();
            if (itemTrimmed.Length > 0 && itemTrimmed[0] != '-' && itemTrimmed[0] != '@')
                m_jobList.Add(item);
        }
       
    }


    void DoVarCopy(ref PicJob job, string source, string dest)
    {
        string temp = "";
        string sourceOriginal = source;
        source = source.ToLower().Trim();

        if (source == "prompt") temp = job._requestedPrompt;
        if (source == "audio_prompt") temp = job._requestedAudioPrompt;
        if (source == "negative_prompt") temp = job._requestedNegativePrompt;
        if (source == "audio_negative_prompt") temp = job._requestedAudioNegativePrompt;
        if (source == "segmentation_prompt") temp = job._requestedSegmentationPrompt;

        if (temp == "")
        {
            temp = sourceOriginal; //it's not a var I guess
        }

        if (dest == "prompt") job._requestedPrompt = temp;
        if (dest == "audio_prompt") job._requestedAudioPrompt = temp;
        if (dest == "negative_prompt") job._requestedNegativePrompt = temp;
        if (dest == "audio_negative_prompt") job._requestedAudioNegativePrompt = temp;
        if (dest == "segmentation_prompt") job._requestedSegmentationPrompt = temp;
    }

    public void SetNoUndo(bool bNoUndo)
    {
        m_noUndo = bNoUndo;
    }

    public bool GetNoUndo()
    {
        return m_noUndo;
    }


    public bool IsWaitingForGPU()
    {
        if (m_waitingForPicJob || IsBusyBasic()) return false;
        if (!StillHasJobActivityToDo()) return false;

        return true;
    }

    private bool ProcessResizeIfLargerOrForcedCommand(string[] commandParts, bool bForceResize)
{
    // Expected format:
    // resize_if_larger|x|<newWidth>|y|<newHeight>|aspect_correct|<1 or 0>
    if (commandParts.Length < 7)
    {
        RTConsole.Log("Error: 'resize_if_larger' command missing parameters.");
        RTQuickMessageManager.Get().ShowMessage("Error: 'resize_if_larger' command missing parameters.");
        return false;
    }

    if (!commandParts[1].Trim().Equals("x", StringComparison.OrdinalIgnoreCase) ||
        !commandParts[3].Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
        !commandParts[5].Trim().Equals("aspect_correct", StringComparison.OrdinalIgnoreCase))
    {
        RTConsole.Log("Error: 'resize_if_larger' command parameters malformed.");
        RTQuickMessageManager.Get().ShowMessage("Error: 'resize_if_larger' command parameters malformed.");
        return false;
    }

    if (!int.TryParse(commandParts[2], out int newWidth))
    {
        RTConsole.Log("Error: invalid width in 'resize_if_larger' command.");
        RTQuickMessageManager.Get().ShowMessage("Error: invalid width in 'resize_if_larger' command.");
        return false;
    }

    if (!int.TryParse(commandParts[4], out int newHeight))
    {
        RTConsole.Log("Error: invalid height in 'resize_if_larger' command.");
        RTQuickMessageManager.Get().ShowMessage("Error: invalid height in 'resize_if_larger' command.");
        return false;
    }

    // Determine whether to keep aspect ratio.
    bool bKeepAspect = false;
    string aspectValue = commandParts[6].Trim();
    if (aspectValue.Equals("1") || aspectValue.Equals("true", StringComparison.OrdinalIgnoreCase))
    {
        bKeepAspect = true;
    }

    // Get the current texture.
    Texture2D currentTexture = m_pic.sprite.texture;
    if (currentTexture != null && (bForceResize || currentTexture.width > newWidth || currentTexture.height > newHeight))
    {
        Resize(newWidth, newHeight, bKeepAspect, FilterMode.Bilinear);
       // RTQuickMessageManager.Get().ShowMessage($"Image resized to {newWidth}x{newHeight}. Keep aspect: {bKeepAspect}");
    }
    else
    {
       // RTQuickMessageManager.Get().ShowMessage("Image is already within the specified size.");
    }

    return true;
}



    public void UpdateJobs()
    {
        if (m_waitingForPicJob || IsBusyBasic()) return;

        //now is a good time to start  job I guess

        if (!StillHasJobActivityToDo()) return;

        
        int serverID = Config.Get().GetFreeGPU(RTRendererType.ComfyUI, false);

        if (serverID == -1)
        {
             SetStatusMessage("Waiting for GPU...");
                //nothing available yet
            return;
        }

        var serverInfo = Config.Get().GetGPUInfo(serverID);

        if (m_picJobs.Count > 0)
        {
            m_waitingForPicJob = true;
            PicJob job = m_picJobs[0];
            m_picJobs.RemoveAt(0);
            job._serverID = serverID;
            job._timeOfStart = Time.realtimeSinceStartup;

            ProcessJobIntoFinal(ref job);

            if (job._job == "run_workflow" || job._job == "run_dalle")
            {
                SetStatusMessage("Waiting for GPU to\nrun workflow...");

                //we'll schedule it as a GPU event
                var e = new ScheduledGPUEvent();
                e.mode = "render";
                e.m_picJob = job;
                e.workflow = job._workflow;
                e.targetObj = gameObject;

                //trim the whitespace from the strings above
                e.requestedRenderer = job.requestedRenderer;
                PassInTempInfoPicJob(job);

                _jobHistory.Add(job.Clone());
                SetNeedsToUpdateInfoPanelFlag();

                if (!ImageGenerator.Get().RunGPUEventNow(e, serverID, false))
                {
                    ImageGenerator.Get().ScheduleGPURequest(e);
                }

            }
            else

            if (job._job == "upload_to_comfy")
            {
                //add a file to the ComfyUI server
                ComfyUIFileUploader uploaderScript = ComfyUIFileUploader.CreateObject();
                SetStatusMessage("Uploading to\nComfyUI...");
                string remoteFileName = "";

                if (IsMovie())
                {
                   remoteFileName = Path.GetFileName(job._parm_1_string);
                   uploaderScript.UploadFile(serverID, job._parm_1_string, remoteFileName, OnUploadFinished);
                }
                else
                {

                    if (m_pic.sprite == null || m_pic.sprite.texture == null)
                    {
                        ClearErrorsAndJobs();
                        SetStatusMessage("Need image/movie\nfirst!");
                        return;
                    }
                    byte[] pngBytes;

                    if (m_mask.sprite != null && m_mask.sprite.texture != null)
                    {
                        Texture2D finalTexture = Tex2DExtension.CombineTexturesForAlpha(m_pic.sprite.texture, m_mask.sprite.texture);
                        pngBytes = finalTexture.EncodeToPNG();

                    } else
                    {
                        pngBytes = m_pic.sprite.texture.EncodeToPNG();
                    }

                    //for debugging purposes, let's write the png to disk
                    //File.WriteAllBytes("test_debug_pic.png", pngBytes);

                    //let's get only the filename from job.parm_1_string
                    remoteFileName = Path.GetFileName(m_mediaRemoteFilename);
                    m_mediaRemoteFilename = remoteFileName;
                    uploaderScript.UploadFileInMemory(serverID, pngBytes, remoteFileName, OnUploadFinished);
                }
            }
        }
        else
        {
            if (m_jobList.Count > 0)
            {
                //convert the joblist into a real Job
                SetNoUndo(false);
                //there is a chance we need to use a different workflow, if the server wants it
                if (m_allowServerJobOverrides && serverInfo._jobListOverride != "")
                {
                    m_allowServerJobOverrides = false; //once is enough
                    m_jobList.Clear();
                    AddJobList(serverInfo.GetPicJobListAsListOfStrings());
                }

                
                string workFlowNameWithoutTest = m_jobList[0];

                //remove "test_" from the front if it's there
                if (workFlowNameWithoutTest.StartsWith("test_"))
                {
                    workFlowNameWithoutTest = workFlowNameWithoutTest.Substring(5);
                }


                PicJob job = new PicJob();
                job = m_curEvent.m_picJob.Clone();

                //kill anything that shouldn't be carrried to the next render

                job._data.Clear();
                job._serverID = -1;


                if (m_jobDefaultInfo != null)
                {
                    job = m_jobDefaultInfo;
                    m_jobDefaultInfo = null;
                }
                job._originalJobString = m_jobList[0];

                job._job = "run_workflow";
                //separate m_jobList[0] into a vector of strings separated by space
                string[] words = m_jobList[0].Split('@');
                job._workflow = words[0];

                bool bSkipNextParm = false;

                //if job._workflow ends in a '-' or '/' then remove it.  Also trim it after that
                if (job._workflow.EndsWith("-"))
                {
                    job._workflow = job._workflow.Substring(0, job._workflow.Length - 1);
                    bSkipNextParm = true; //A dash?!!  We'll need to ignore the next parm I guess
                }

                job._workflow = job._workflow.Trim();

                for (int i=1; i < words.Length; i++)
                {

                    if (bSkipNextParm)
                    {
                        bSkipNextParm = false;

                        if (words[i].EndsWith("-"))
                        {
                            bSkipNextParm = true;
                        }
                        continue;
                    }

                    if (words[i].EndsWith("-"))
                    {
                        bSkipNextParm = true;
                    }


                    //this must be a command.  Split it up by | character
                    string[] commandParts = words[i].Split('|');
                    if (commandParts.Length >= 1)
                    {
                        PicJobData picJobData = new PicJobData();
                        picJobData._name = commandParts[0].Trim();

                        if (commandParts.Length >= 2)
                        {
                            picJobData._parm1 = commandParts[1];
                        }

                        if (commandParts.Length >= 3)
                        {
                            picJobData._parm2 = commandParts[2];
                        }


                        if (picJobData._name.ToLower() == "copy")
                        {
                            DoVarCopy(ref job, picJobData._parm1, picJobData._parm2);
                        }
                        else if (picJobData._name.ToLower() == "fill_mask_if_blank")
                        {
                            FillAlphaMaskIfBlank();
                        }
                        else if (picJobData._name.ToLower() == "no_undo")
                        {
                            SetNoUndo(true);
                        }
                        else if (picJobData._name.ToLower() == "resize_if_larger")
                        {
                            ProcessResizeIfLargerOrForcedCommand(commandParts, false);
                        }
                        else if (picJobData._name.ToLower() == "resize")
                        {
                            ProcessResizeIfLargerOrForcedCommand(commandParts, true);
                        }

                        //add it
                        job._data.Add(picJobData);
                    }
                    else
                    {
                        //show error
                        RTConsole.Log("Error parsing command: " + words[i]+" in joblist line "+ m_jobList[0]);    
                    }
                }

                //actually, job

                if (workFlowNameWithoutTest.StartsWith("img_") || workFlowNameWithoutTest.StartsWith("video_") || workFlowNameWithoutTest.StartsWith("vid_"))
                {
                    //add a new job to upload the file we're gonna need
                    PicJob uploaderJob = new PicJob();
                    uploaderJob._job = "upload_to_comfy";
                    uploaderJob._parm_1_string = "<MEDIA_LOCAL_FILENAME>";
                    m_picJobs.Add(uploaderJob);

                    if (workFlowNameWithoutTest.StartsWith("img_"))
                    {
                        if (m_mediaRemoteFilename == "")
                        {
                            m_mediaRemoteFilename = "pic_" + System.Guid.NewGuid() + ".png";
                        }

                        job._parm_1_string = "temp/" + m_mediaRemoteFilename;//for images, we need temp
                    }

                    if (workFlowNameWithoutTest.StartsWith("video_") || workFlowNameWithoutTest.StartsWith("vid_"))
                    {
                        job._parm_1_string = "temp/<MEDIA_FILENAME>"; //it will get replaced with the actual movie name later
                    }
                }

                SetJobWithInfoFromCur(ref job);
                m_picJobs.Add(job);
                //remove the first string in m_joblist
                m_jobList.RemoveAt(0);
                UpdateJobs();
            }
        }
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

    public bool IsVisible()
    {
        return m_pic.isVisible;
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

        UpdateJobs();
       
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

    public void PassInTempInfoPicJob(PicJob picJob)
    {
        GetCurrentStats().m_picJob = picJob;
        SetNeedsToUpdateInfoPanelFlag();
    }

    public void PassInTempInfo(RTRendererType requestedRenderer, int gpuID)
    {
      /*
        GetCurrentStats().m_lastPromptUsed = m_picTextToImageScript.GetPrompt();
        GetCurrentStats().m_requestedRenderer = requestedRenderer;
        GetCurrentStats().m_lastNegativePromptUsed = GameLogic.Get().GetNegativePrompt();
      */
       
        /*
        GetCurrentStats().m_gpu = gpuID;
        if (_jobHistory.Count > 0)
        {
            _jobHistory[_jobHistory.Count - 1]._infoGPUUsed = gpuID;
        }
        SetNeedsToUpdateInfoPanelFlag();
        */
    }

}
