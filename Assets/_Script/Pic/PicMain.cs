using System.Collections;
using UnityEngine;
using System.IO;
using System;
using TMPro;
using B83.Image.BMP;

public class UndoEvent
{
    public Texture2D m_texture;
    public Sprite m_sprite;
    public bool m_active = false;
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
    public Camera m_camera;

    UndoEvent m_undoevent = new UndoEvent();
    bool m_isDestroyed;
    string m_editFilename = "";
    bool m_bLocked;
    bool m_bDisableUndo;
    // Start is called before the first frame update
    void Start()
    {
        SetStatusMessage("");

        m_camera = Camera.allCameras[0];
        m_canvas.worldCamera = m_camera;
    }
    public void SetDisableUndo(bool bNew)
    {
        m_bDisableUndo = bNew;
    }
    public Camera GetCamera() { return m_camera; }

    public Canvas GetCanvas() { return m_canvas; }

    public bool IsDestroyed()
    {
        return m_isDestroyed;
    }

    public void SafelyKillThisPic()
    {
        m_isDestroyed = true;
        GameObject.Destroy(gameObject);
    }
    public bool IsBusy()
    {
        if (m_picTextToImageScript.IsBusy()) return true;
        if (m_picGeneratorScript.GetIsGenerating()) return true;
        if (m_picInpaintScript.IsBusy()) return true;
        if (m_picUpscaleScript.IsBusy()) return true;
        if (GetLocked()) return true;

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
    }

    public bool GetLocked() { return m_bLocked; }
    public void SetLocked(bool bNew) { m_bLocked = bNew; }
    public void SetStatusMessage(string msg)
    {
        m_text.text = msg;
    }

    public bool AddImageUndo(bool bDoFullCopy = false)
    {
        if (m_bDisableUndo) return false;

        m_undoevent.m_active = true;
        
        if (bDoFullCopy)
        {
            Texture2D copyTexture = m_pic.sprite.texture.Duplicate();

            float biggestSize = Math.Max(copyTexture.width, copyTexture.height);

            UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(copyTexture, 
                new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f));
            m_undoevent.m_sprite = newSprite;
        } else
        {
            m_undoevent.m_sprite = m_pic.sprite;
        }
        //Debug.Log("Image added to undo");

        return true;
    }

    public void UndoImage()
    {
      
        if (m_undoevent.m_active)
        {
            Debug.Log("Undo");
            Sprite tempSprite = m_pic.sprite;
            m_pic.sprite = m_undoevent.m_sprite;
            m_undoevent.m_sprite = tempSprite;
            m_picMaskScript.ResizeMaskIfNeeded();
        }
        else
        {
            Debug.Log("Nothing to undo in this pic.");
        }
    }
    public void MovePicUpIfNeeded()
    {
        var vPos = m_pic.gameObject.transform.localPosition;
        vPos.y = 0; //default

        if (m_pic.sprite.bounds.size.y != 5.12f)
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
            Debug.Log("Importing from clipboard...");
            LoadImageByFilename(tempPngFile, false);
            RTUtil.DeleteFileIfItExists(tempPngFile);

        }
        else
        {
            Debug.Log("No image found on clipboard");
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



    public void LoadImageByFilename(string filename, bool bResize = false)
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
                alphaTex = texture.GetAlphaMask();
              
                alphaTex.Apply();

                m_picMaskScript.SetMaskFromTexture(alphaTex);
                texture.FillAlpha(1.0f);
                texture.Apply();
            }
          
            newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);

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

    public GameObject Duplicate()
    {
        //make a copy of this entire object

        GameObject go = ImageGenerator.Get().CreateNewPic();
        PicMain targetPicScript = go.GetComponent<PicMain>();
        PicTargetRect targetRectScript = go.GetComponent<PicTargetRect>();
        PicTextToImage targetTextToImageScript = go.GetComponent<PicTextToImage>();

        targetPicScript.SetImage(m_pic.sprite.texture, true);
        targetPicScript.SetMask(m_mask.sprite.texture, true);
        targetTextToImageScript.SetSeed(m_picTextToImageScript.GetSeed()); //if we've set it, it will carry to the duplicate as well
        targetTextToImageScript.SetTextStrength(m_picTextToImageScript.GetTextStrength()); //if we've set it, it will carry to the duplicate as well
        targetTextToImageScript.SetPrompt(m_picTextToImageScript.GetPrompt()); //if we've set it, it will carry to the duplicate as well
        targetRectScript.SetOffsetRect(m_targetRectScript.GetOffsetRect());

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
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f));
       
        m_pic.sprite = newSprite;
        MovePicUpIfNeeded();
    }

    public void InvertMask()
    {
        Debug.Log("invert mask");
        m_mask.sprite.texture.Invert();
        m_mask.sprite.texture.Apply();
        SetMask(m_mask.sprite.texture, false);
    }

    public void SetMask(Texture2D newImage, bool bDoFullCopy)
    {
        if (bDoFullCopy)
        {
            newImage = newImage.Duplicate();
        }

        float biggestSize = Math.Max(newImage.width, newImage.height);

        UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(newImage,
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f));

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
        Debug.Log("File we're editing has changed");
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

    public void OnImageReplaced()
    {
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();
    }

    //save to a random filename if passed a blank filename
    public string SaveFile(string fname="") 
    {
        string tempDir = Application.dataPath;

        //this might be needed on mac or something, but it won't work for photoshop paths when saving, we need to keep it
        //backslashes for some reason
        /*
        tempDir = tempDir.Replace('\\', '/');
        tempDir = tempDir.Substring(0, tempDir.LastIndexOf('/'));
        tempDir += "/tempCache";
        */


        //default filename if none is sent in
        tempDir = tempDir.Replace('/', '\\');
        tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));


        string fileName = tempDir + "\\pic_" + System.Guid.NewGuid() + ".bmp";

        if (fname != null && fname != "")
        {
            fileName = fname;
        }

        Debug.Log("Saving file to " + fileName);

        var pngBytes = m_pic.sprite.texture.EncodeToBMP(m_mask.sprite.texture);
        File.WriteAllBytes(fileName, pngBytes);


        /*
         if we wanted png saving 
         
        string fileName = tempDir+"\\pic_"+ System.Guid.NewGuid()+".png";
       
        if (fname != null && fname != "")
        {
            fileName = fname;
        }
        
        Debug.Log("Saving file to "+fileName);

        var pngBytes = m_pic.sprite.texture.EncodeToPNG();
        File.WriteAllBytes(fileName, pngBytes);
        */
        return fileName;


    }

    public void SaveFileNoReturn2()
    {
        SaveFile();

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
                Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width / 5.12f);
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

        Sprite newSprite = Sprite.Create(newTex, new Rect(0, 0, newTex.width, newTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);
        m_pic.sprite = newSprite;
        m_pic.sprite.texture.Apply();

    }
    public void OnMutateButton()
    {
        Debug.Log("Blurring..");
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

    public void OnReRenderButton()
    {
        var e = new ScheduledGPUEvent();
        e.mode = "rerender";
        e.targetObj = this.gameObject;
        
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    
    private void OnDestroy()
    {
       InvalidateExportedEditFile();
    }

    // Update is called once per frame
    void Update()
    {

        if (m_editFileHasChanged)
        {
            m_editFileHasChanged = false;

            AddImageUndo();
            LoadImageByFilename(m_editFilename, false);
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
    }

}
