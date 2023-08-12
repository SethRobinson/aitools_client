using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using System.IO;

//A test to see how fast we can generate images and display them

public class CrazyCamPreset
{

    public string _sampler = "Euler a";
    public int _steps = 20;

    public string _prompt;
    public bool _fixFaces = true;
    public string _maskContents = "original";
    public float _denoisingStrength = 0.35f;
    public string _presetName = "error";
    public bool _noTranslucency = false;
    public float _maskBlending = 0.0f;
    public int _maskMode = CrazyCamLogic.eMaskForegroundOnly;
    public float _cfg = 7.5f;
    public float _pix2pixcfg = 1.5f;
    public string _modelRequirements = "";

    //controlnet
    public bool _useControlNet = false;
    public string _controlNetPreprocessor = "depth";
    public string _controlNetModel = "depth";
    public float _controlNetWeight = 1.0f;
    public float _controlNetGuidanceEnd = 1.0f;
    public float _controlNetGuidanceStart = 0.0f; //ignored currently
}

public class CrazyCamLogic : MonoBehaviour
{
    public Button m_pauseButton;
    public MeshRenderer m_meshRenderer;
    public MeshRenderer m_processedRenderer;

    string m_json; //store this for requests so we don't have to compute it each time
    public TMP_Dropdown m_camDropdown;
    public TMP_Dropdown m_presetDropdown;
    public Toggle m_noTranslucencyToggle;
    public Toggle m_autoSaveImages;
    public TMP_Dropdown m_maskDropdown;

    //yeah, I should use an enum but casting everything to int sucks
    public const int eMaskEntireImage = 0;
    public const int eMaskForegroundOnly = 1;
    public const int eMaskBackgroundOnly = 2;
    bool m_renderingIsPaused = false;
    public int m_maskMode = eMaskEntireImage;

    static CrazyCamLogic _this = null;
    List<CrazyCamPreset> m_presets;

    float m_timeBetweenPicsSeconds = 0.3f;
    float m_timer;
    float m_delayBetweenSnaps = 0;

    private void Awake()
    {
        _this = this;
    }

    public static CrazyCamLogic Get() { return _this; }

    public void OnTogglePause()
    {
        m_renderingIsPaused = !m_renderingIsPaused;

        UpdateCrazyCamPauseButtonStatus();

    }

    public void UpdateCrazyCamPauseButtonStatus()
    {
        TMPro.TextMeshProUGUI buttonText = m_pauseButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();

        if (!m_renderingIsPaused)
        {
            //RTUtil.SetButtonColor(m_pauseButton, new Color(1, 0, 0, 1));
            buttonText.text = "Pause";
         }
        else
        {
            buttonText.text = "Unpause";
           // RTUtil.SetButtonColor(m_pauseButton, new Color(0, 1, 0, 1));
        }
    }

    public CrazyCamPreset AddPreset(string presetName, string prompt, string maskContents, float denoisingStrength, bool bFixFaces, bool bNoTranslucency, float maskBlending, int maskMode, float cfg,
        float pix2pixcfg, string suggestedModel)
    {
        CrazyCamPreset preset = new CrazyCamPreset();
        preset._prompt = prompt;
        preset._maskContents = maskContents;
        preset._presetName = presetName;
        preset._denoisingStrength = denoisingStrength;
        preset._fixFaces= bFixFaces;
        preset._noTranslucency= bNoTranslucency;
        preset._maskBlending = maskBlending;
        preset._maskMode = maskMode;
        preset._cfg = cfg;
        preset._pix2pixcfg= pix2pixcfg;
        preset._modelRequirements = suggestedModel;

        m_presets.Add(preset);

        var options = new List<TMP_Dropdown.OptionData>();
        var option = new TMP_Dropdown.OptionData();
        option.text = presetName;
        options.Add(option);

        m_presetDropdown.AddOptions(options);
        return preset;
    }

    public void OnDelayChanged(string delayString)
    {
        float delay = 0;

        float.TryParse(delayString, out delay);
        m_timeBetweenPicsSeconds = delay;
        m_delayBetweenSnaps = delay;
        m_timer = 0;

        Debug.Log("got delay " + delay);
    }

    public void Start()
    {
        Debug.Log("Doing presets");
        m_presets = new List<CrazyCamPreset>();
        m_presetDropdown.ClearOptions();
        AddPreset("Preset: Use active settings", "", "", 0, true, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, ""); //special case, index 0 won't change anything
        AddPreset("Fix my face (method 1)", "good looking person", "original", 0, true, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Fix my face (method 2)", "good looking person", "original", 0.1f, false, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");

        AddPreset("Muscle man", "body builder, man, handsome, ripped, athlete, perfect body, large muscles", "original", 0.25f, true, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Beautiful woman", "beautiful woman, elegant, cute, athlete, perfect body", "original", 0.25f, true, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Zombie person", "a zombie", "original", 0.25f, true, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("My room has spiderwebs", "filled with ((cobwebs)), disgusting, spiders, horror", "original", 0.39f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("A monster", "a (((scary monster))) in a room", "original", 0.69f, false, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("A giant spider", "a (((giant spider))) in a room", "original", 1.0f, false, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Spider man", "spider man, super hero, epic, red costume, spiderman mask", "original", 0.64f, false, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Erase me", "an empty room", "fill", 1.0f, false, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Old woman", "old woman, wrinkles, studio portrait, award winning", "original", 0.41f, true, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Old man", "old man, wrinkles, studio portrait, award winning", "original", 0.41f, true, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("High five me", "two people giving high five, hands touching", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Happy new year w/ friends", "great friends celebrating the new year in times square, smiling, posing for picture, holding champagne glass", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Proposing at disney", "Man proposes to girlfriend at Disneyland, posing for photo, happy", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Breaking up at disney", "((sad)), scowling, depressed, couple at disneyland, frustrated", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Hug me", "a loving couple, hugging, hug, in love, smiling, playing, candid, mischievous", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Copy me", "two people, identical twins, same pose", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        
        AddPreset("Ninja fight", "person fighting ninja, getting hit, reaction, punched, action shot, epic, fireball", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Holding fire", "person creating magic with hands, magic fire", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Holding light saber", "person holding light saber, epic, dramatic lighting, star wars", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("Holding a hamburger", "person posing with a delicious small hamburger, bokeh, hamburger in hand", "latent noise", 1.0f, true, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("In the bad place", "((burning in hell)), horror, scary, screaming, epic, detailed, satan, skeleton, ghoul", "latent noise", 1.0f, false, false, 0, eMaskBackgroundOnly, 7.5f, 1.5f, "");

        AddPreset("(for dog) A cat", "a cat in a room", "original", 0.45f, false, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("(for dog) Cavalier with shades", "a Cavalier King Charles Spaniel wearing sunglasses", "original", 0.45f, false, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "inpaint");
        AddPreset("(pix2pix) Snowy", "make it snowing", "original",  0.70f, false, false, 0, eMaskBackgroundOnly, 12.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Lego Person", "change person to lego", "original", 0.70f, false, false, 0, eMaskForegroundOnly, 14.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Wearing leather", "change clothes to leather", "original", 0.70f, false, false, 0, eMaskForegroundOnly, 7.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Bald", "change hair to bald", "original", 0.46f, false, false, 0, eMaskForegroundOnly, 5.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Nice mustache", "Add mustache", "original", 0.46f, false, false, 0, eMaskForegroundOnly, 5.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Nice beard", "Add beard", "original", 0.46f, false, false, 0, eMaskForegroundOnly, 5.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Old man", "change to an old man", "original", 0.7f, false, false, 0, eMaskForegroundOnly, 2.5f, 1.5f, "pix2pix");
        AddPreset("(pix2pix) Blue skies", "show sky outside window", "original", 0.7f, false, false, 0, eMaskBackgroundOnly, 12.5f, 1.5f, "pix2pix");
        var p = AddPreset("(controlnet) Spider man (partial)", "spider man, super hero, epic, red costume, spiderman mask", "latent noise", 1.0f, false, true, 0, eMaskForegroundOnly, 7.5f, 1.5f, "");
        p._useControlNet = true;
        p = AddPreset("(controlnet) Spider man (full)", "spider man, super hero, epic, red costume, spiderman mask", "latent noise", 1.0f, false, true, 0, eMaskEntireImage, 7.5f, 1.5f, "");
        p._useControlNet = true;
    }

    public void SetPresetByIndex(int index)
    {
        m_presetDropdown.value = index;
    }
    public void OnStartGameMode()
    {

        GameLogic.Get().ShowCompatibilityWarningIfNeeded();

        m_timer = 0;

        GameLogic.Get().SetToolsVisible(false);
        ImageGenerator.Get().SetGenerate(false);
        GameLogic.Get().OnClearButton();
        
        
        if (GameLogic.Get().GetSeed() < 0 && !GameLogic.Get().IsActiveModelPix2Pix())
        {
            //    GameLogic.Get().SetSeed(0);
            RTConsole.Log("Warning: Seed is set to -1 (random), this can make faces and things change a lot from frame to frame");
        }

        RTUtil.FindObjectOrCreate("CrazyCamGUI").SetActive(true);
        RTUtil.FindObjectOrCreate("CrazyCamMode").SetActive(true);

        CameraManager.Get().OnCameraStartedCallback += OnCameraStarted;
        CameraManager.Get().OnCameraInfoAvailableCallback += OnCameraInfoAvailable;
        CameraManager.Get().OnCameraDisplayedNewFrameCallback += OnCameraDisplayedNewFrame;

        CameraManager.Get().InitCamera(m_meshRenderer);

        SetPresetByIndex(0);

    }

  
    void SetCamDropdownByIndex(int index)
    {
        
        if (index < m_camDropdown.options.Count)
        {
            m_camDropdown.value = index;
        }
    }

    void SetMaskDropdownByIndex(int index)
    {

        if (index < m_camDropdown.options.Count)
        {
            m_maskDropdown.value = index;
        }
    }

    public void OnCameraInfoAvailable(WebCamDevice[] devices)
    {
     
        List<string> list = new List<string>();

        for (int cameraIndex = 0; cameraIndex < devices.Length; ++cameraIndex)
        {
            Debug.Log("Crazycam: devices[cameraIndex].name: " + devices[cameraIndex].name + " Front facing: " + devices[cameraIndex].isFrontFacing);
            list.Add(devices[cameraIndex].name);

        }

        m_camDropdown.ClearOptions();
        m_camDropdown.AddOptions(list);

        if (CameraManager.Get().GetCurrentCameraIndex() > m_camDropdown.options.Count)
        {
            CameraManager.Get().SetCameraByIndex(0); //at least we have this one
        }

        SetCamDropdownByIndex(CameraManager.Get().GetCurrentCameraIndex());
    }

    public void OnCameraDropdownChanged()
    {
        Debug.Log("Camera changed to index "+m_camDropdown.value);
        CameraManager.Get().SetCameraByIndex(m_camDropdown.value);
    }

    public void OnMaskDropdownChanged()
    {
       m_maskMode = m_maskDropdown.value;
    }

    public void OnPresetDropdownChanged()
    {
        int choice = m_presetDropdown.value;

        Debug.Log("Preset changed to index " + choice);
        if (choice == 0)
        {
            RTQuickMessageManager.Get().ShowMessage("(leaving settings as they are, quit back to main screen to change them)");
            return;
        }

        RTQuickMessageManager.Get().ShowMessage("Changing settings to " + m_presetDropdown.options[choice].text);

        var preset = m_presets[choice];

        var gl = GameLogic.Get();

        gl.SetPrompt(preset._prompt);
        gl.SetSamplerByName(preset._sampler);
        gl.SetSteps(preset._steps);
        gl.SetFixFaces(preset._fixFaces);
        gl.SetMaskContentByName(preset._maskContents);
        gl.SetInpaintStrength(preset._denoisingStrength);
        gl.SetAlphaMaskFeatheringPower(preset._maskBlending);
        gl.SetTextStrength(preset._cfg);
        gl.SetPix2PixTextStrength(preset._pix2pixcfg);
        m_noTranslucencyToggle.isOn = preset._noTranslucency;
        m_maskMode = preset._maskMode;

        if (preset._modelRequirements.Length > 0)
        {
            if (GameLogic.Get().GetActiveModelFilename().Contains(preset._modelRequirements))
            {
            } else
            {
                RTQuickMessageManager.Get().ShowMessage("Warning:  You should switch to a SD model with "+preset._modelRequirements+" in the filename!");

            }
        }
        SetMaskDropdownByIndex(m_maskMode);
        if (gl.GetSeed() == -1)
        {
            gl.SetSeed(0);
        }

        gl.OnUseControlNet(preset._useControlNet);

        if (preset._useControlNet)
        {
            //also set other control net settings, since we're using it 'n all
            gl.SetControlNetGuidance(preset._controlNetGuidanceEnd);
            gl.SetControlNetWeight(preset._controlNetWeight);
            gl.SetCurrentControlNetPreprocessorBySubstring(preset._controlNetPreprocessor);
            gl.SetCurrentControlNetModelBySubstring(preset._controlNetModel);

        }
    }

    public void OnCameraStarted(WebCamTexture device)
    {
        float aspectX = (float)device.width / (float)device.height;
        Debug.Log("Camera started.  W:" + device.width + " H:" + device.height+" AspectX: "+aspectX);
        var vScale = m_meshRenderer.gameObject.transform.parent.localScale;
        vScale.x = aspectX;
        m_meshRenderer.gameObject.transform.parent.localScale = vScale;

        
        var processedVScale = m_processedRenderer.gameObject.transform.parent.localScale;
        float processedAspectX = (float)GameLogic.Get().GetGenWidth() / (float)GameLogic.Get().GetGenHeight();
        processedVScale.x = processedAspectX;

        m_processedRenderer.gameObject.transform.parent.localScale = processedVScale;
    }

    public void OnEndGameMode()
    {

        CameraManager.Get().OnCameraStartedCallback -= OnCameraStarted;
        CameraManager.Get().OnCameraInfoAvailableCallback -= OnCameraInfoAvailable;
        CameraManager.Get().OnCameraDisplayedNewFrameCallback -= OnCameraDisplayedNewFrame;

        GameLogic.Get().OnClearButton();
        GameLogic.Get().SetToolsVisible(true);
        RTUtil.FindObjectOrCreate("CrazyCamGUI").SetActive(false);
        RTUtil.FindObjectOrCreate("CrazyCamMode").SetActive(false);

        CameraManager.Get().StopCamera();

//        RTMessageManager.Get().Schedule(0.3f, RTUtil.KillAllObjectsByNameWrapper, null, "RTToolTipPrefab", true, 0);
        RTMessageManager.Get().Schedule(0.3f, (name, bIsWildcardString) => RTUtil.KillAllObjectsByName(null, name, bIsWildcardString), "RTToolTipPrefab", true);

    }

    public void OnImageRenderFinished(Texture2D tex, RTDB db)
    {
        if (m_renderingIsPaused) return;


        //if m_autoSaveImages is checked, save the image
        if (m_autoSaveImages.isOn)
        {
            string tempDir = Application.dataPath;
            //get the Assets dir, but strip off the word Assets
            tempDir = tempDir.Replace('/', '\\');
            tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));
            //tack on subdir if needed
            tempDir = tempDir + "/autosave";
            //reconvert to \\ (I assume this code would have to change if it wasn't Windows... uhh
            tempDir = tempDir.Replace('/', '\\');
            string fileName = tempDir + "\\CrazyCam_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";

            RTConsole.Log("Saving image to " + fileName);
            File.WriteAllBytes(fileName, tex.EncodeToPNG());
        }
        //Debug.Log("Got image");
        m_processedRenderer.material.mainTexture = tex;
        float timeTaken = Time.time - db.GetFloat("startTime");

        if (m_delayBetweenSnaps == 0)
        {
            m_timeBetweenPicsSeconds = timeTaken / Config.Get().GetGPUCount();
        }
        //Debug.Log(timeTaken);
    }
    void OnCameraDisplayedNewFrame(WebCamTexture webCamTex)
    {
        //Debug.Log("NEW FRAME");

        if (m_timer > Time.time) return; //we don't want to show pics this fast
        if (!Config.Get().IsAnyGPUFree()) return;
        if (m_renderingIsPaused) return;

        m_timer = Time.time + m_timeBetweenPicsSeconds;
        RTDB db = new RTDB();
        db.Set("startTime", Time.time);
        //let's remember how long it takes from start to finish to extract from webcam, get it inpainted, and display it
     
        float aspectRatio = (float)webCamTex.width / (float)webCamTex.height;
        float targetAspectRatio = (float)GameLogic.Get().GetGenWidth() / (float)GameLogic.Get().GetGenHeight();

        Rect rect;
        if (aspectRatio > 1)
        {
            float newWidth = webCamTex.height * aspectRatio;
            float excessWidth = (newWidth - webCamTex.height) / 2;
            rect = new Rect(excessWidth, 0, webCamTex.height, webCamTex.height);
        }
        else
        {
            float newHeight = webCamTex.width / aspectRatio;
            float excessHeight = (newHeight - webCamTex.width) / 2;
            rect = new Rect(0, excessHeight, webCamTex.width, webCamTex.width);
        }

        //Debug.Log("Tex of "+webCamTex.width+","+webCamTex.height+" squared to Rect: " + rect);
        //Save the image to the Texture2D
        Texture2D texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);

        texture.SetPixels(webCamTex.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height));
       
        
        texture.Apply();
        ResizeTool.Resize(texture, GameLogic.Get().GetGenWidth(), GameLogic.Get().GetGenHeight(), true);
      

        //Encode it as a PNG.
        //byte[] bytes = texture.EncodeToPNG();

        bool bOperateOnSubjectMaskOnly = false;
        bool bReverseMask = false;
        
        switch (m_maskMode)
        {
            case eMaskEntireImage:
                bOperateOnSubjectMaskOnly = false;
                bReverseMask = false;
                break;

            case eMaskForegroundOnly:
                bOperateOnSubjectMaskOnly = true;
                bReverseMask = false;
                break;

            case eMaskBackgroundOnly:
                bOperateOnSubjectMaskOnly = true;
                bReverseMask = true;
                break;

          
               // Debug.Log("Error, bad mask type");

        }
        var json = GamePicManager.Get().BuildJSonRequestForInpaint(GameLogic.Get().GetPrompt(), GameLogic.Get().GetNegativePrompt(), texture, null, false, bOperateOnSubjectMaskOnly,
            m_noTranslucencyToggle.isOn, bReverseMask, GameLogic.Get().GetUseControlNet());

        GamePicManager.Get().SpawnInpaintRequest(json, OnImageRenderFinished, db);
    }

    void Update()
    {
      
    }

}
