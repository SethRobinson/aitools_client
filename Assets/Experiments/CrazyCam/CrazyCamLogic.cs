using System.Collections;

using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using DG.Tweening;
using System.IO;

//A test to see how fast we can generate images and display them

public class CrazySnapshotPreset
{
    public string _name;
    public string _prompt;
    public string _processingMessage = "Haunting image...";

}


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
    bool m_bGrabNextFrame = false; //only for snapshot mode
    //the tmp font asset
    public TMP_FontAsset _crazyCamFontAsset;

    public GameObject _batPrefab;
    static CrazyCamLogic _this = null;
    List<CrazyCamPreset> m_presets;
    List<CrazySnapshotPreset> m_snapshotPresets = new List<CrazySnapshotPreset>();
    public TMPro.TextMeshProUGUI _hauntingTextOverlay;

    float m_timeBetweenPicsSeconds = 0.3f;
    float m_timer;
    float m_delayBetweenSnaps = 0;
    bool m_bIsActive = false;
    bool m_bInSnapshotMode = true;

    private void Awake()
    {
        _this = this;
    }

    public bool IsInSnapshotMode() { return m_bInSnapshotMode ; }
    public void SetInSnapshotMode(bool bNew) { m_bInSnapshotMode = bNew; }


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

    public void ClearSnapshotPresets()
    {
        m_snapshotPresets.Clear();
    }
    public CrazySnapshotPreset AddSnapshotPreset(string presetName, string prompt, string processingText)
    {
        CrazySnapshotPreset preset = new CrazySnapshotPreset();
        preset._prompt = prompt;
        preset._processingMessage = processingText;
        preset._name = presetName;
        m_snapshotPresets.Add(preset);
        return preset;
    }

    public void ClearHauntingOverlay()
    {
        _hauntingTextOverlay.text = "";
    }

    public void SetHauntingTextAndFadeItIn(string msg)
    {
        _hauntingTextOverlay.text = msg;
        _hauntingTextOverlay.alpha = 0;
        //Fade alpha in using Dotween
        _hauntingTextOverlay.DOFade(1.0f, 2.0f);
    }

    public void SetHauntingFadeOut()
    {
        //Fade alpha out using Dotween
        _hauntingTextOverlay.DOFade(0.0f, 2.0f);
    }

    public CrazyCamPreset AddPreset(string presetName, string prompt, string maskContents, float denoisingStrength, bool bFixFaces, bool bNoTranslucency, float maskBlending, int maskMode, float cfg,
        float pix2pixcfg, string suggestedModel)
    {
        CrazyCamPreset preset = new CrazyCamPreset();
        preset._prompt = prompt;
        preset._maskContents = maskContents;
        preset._presetName = presetName;
        preset._denoisingStrength = denoisingStrength;
        preset._fixFaces = bFixFaces;
        preset._noTranslucency = bNoTranslucency;
        preset._maskBlending = maskBlending;
        preset._maskMode = maskMode;
        preset._cfg = cfg;
        preset._pix2pixcfg = pix2pixcfg;
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

        /*
        
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

        */
     
     //   AddSnapshotPreset("Ghosts", "Without changing anything else, change the people in the image to be ghostly and translucent.", "Haunting image...");
    }

    
    public void SetPresetByIndex(int index)
    {
        m_presetDropdown.value = index;
    }

    public void OnStartGameMode()
    {
        m_timer = 0;
        m_bIsActive = true;
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
        // Request capture settings from config (Unity will pick closest supported)
        CameraManager.Get().SetRequestedResolution(
            Config.Get().GetCrazyCamRequestedWidth(),
            Config.Get().GetCrazyCamRequestedHeight(),
            Config.Get().GetCrazyCamRequestedFPS());

        CameraManager.Get().InitCamera(m_meshRenderer);

        SetPresetByIndex(0);

        if (IsInSnapshotMode())
        {
            RTMessageManager.Get().Schedule(2, this.StartSnapshotSequence);
        }

    }

    public void CreateFlashEffect()
    {
        // Find the "MainCanvas"
        Canvas mainCanvas = RTUtil.FindObjectOrCreate("MainCanvas").GetComponent<Canvas>();

        GameObject flashObj = new GameObject("CameraFlash");
        flashObj.transform.SetParent(mainCanvas.transform);

        var image = flashObj.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.white;

        var rectTransform = flashObj.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        // Make sure it's on top
        flashObj.transform.SetAsLastSibling();

        // Flash in and out quickly
        RTMessageManager.Get().Schedule(0.1f, (obj) => GameObject.Destroy(obj), flashObj);
    }

    public void SnapShotGrabNextFrame()
    {
               m_bGrabNextFrame = true; 
    }

    public void MakeCameraLive()
    {

        m_meshRenderer.material.mainTexture = CameraManager.Get().GetWebCamTexture();
    }

    public void StartSnapshotSequence()
    {
        if (!m_bIsActive) return;

        MakeCameraLive();

        int offset = 0;

        for (int i = 3; i >= 1; i--)
        {
            RTMessageManager.Get().Schedule((offset + 4) - i, this.OnShowText, i.ToString());
            RTMessageManager.Get().Schedule((offset + 4) - i, RTAudioManager.Get().Play, "heartbeat"); //plays crap.wav from Resources dir in 2 seconds
        }

        RTMessageManager.Get().Schedule(offset + 4, RTAudioManager.Get().Play, "snap"); //plays crap.wav from Resources dir in 2 seconds
        RTMessageManager.Get().Schedule(offset + 4, this.CreateFlashEffect); //plays crap.wav from Resources dir in 2 seconds
        RTMessageManager.Get().Schedule(offset + 4, this.SnapShotGrabNextFrame); //plays crap.wav from Resources dir in 2 seconds

        //setup a looping sound and remember its id
        RTMessageManager.Get().Schedule(offset + 4, RTAudioManager.Get().PlayMusic, "bats_looping", Config.Get()._snapShotBatSoundVolumeMod, 1.0f, true);
    }

    public void OnShowText(string msg)
        {
            //Let's create a UI text message using TMpro in the center of the screen from scratch
            GameObject textObj = new GameObject();
            textObj.name = "CrazyCamIntroText";
            textObj.transform.SetParent(RTUtil.FindObjectOrCreate("MainCanvas").transform);
        
            var text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = _crazyCamFontAsset;
        
            text.text = msg;
            text.fontSize = 300;
            text.alignment = TextAlignmentOptions.Center; // Use Midline for horizontal and vertical center
            text.color = Color.darkRed;
        
            // Add drop shadow for better readability
            var shadow = textObj.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(4, -4);
        
            // Set up RectTransform for proper centering
            var rectTransform = text.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(800, 200);
            rectTransform.anchoredPosition = new Vector2(0, 0);
        
            RTMessageManager.Get().Schedule(1, (obj) => GameObject.Destroy(obj), textObj);
    }
    public void OnEndGameMode()
    {
        RTMessageManager.Get().RemoveScheduledCalls((System.Action<Vector3, Vector3, float>)CreateBat);
        RTMessageManager.Get().RemoveScheduledCalls(SnapShotGrabNextFrame);

        m_bIsActive = false;
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

        ClearAllBats();
        m_bGrabNextFrame = false;
        MakeCameraLive();
        SetHauntingFadeOut();
        RTAudioManager.Get().StopMusic();

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
        Debug.Log("Camera changed to index " + m_camDropdown.value);
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
        //  gl.SetSamplerByName(preset._sampler);
        // gl.SetSteps(preset._steps);
        // gl.SetFixFaces(preset._fixFaces);
        // gl.SetMaskContentByName(preset._maskContents);
        //   gl.SetInpaintStrength(preset._denoisingStrength);
        //    gl.SetAlphaMaskFeatheringPower(preset._maskBlending);
        //   gl.SetTextStrength(preset._cfg);
        // gl.SetPix2PixTextStrength(preset._pix2pixcfg);
        m_noTranslucencyToggle.isOn = preset._noTranslucency;
        m_maskMode = preset._maskMode;

        if (preset._modelRequirements.Length > 0)
        {
            if (GameLogic.Get().GetActiveModelFilename().Contains(preset._modelRequirements))
            {
            }
            else
            {
                RTQuickMessageManager.Get().ShowMessage("Warning:  You should switch to a SD model with " + preset._modelRequirements + " in the filename!");
            }
        }
        SetMaskDropdownByIndex(m_maskMode);
        if (gl.GetSeed() == -1)
        {
            gl.SetSeed(0);
        }

        /*
        gl.OnUseControlNet(preset._useControlNet);

        if (preset._useControlNet)
        {
            //also set other control net settings, since we're using it 'n all
            gl.SetControlNetGuidance(preset._controlNetGuidanceEnd);
            gl.SetControlNetWeight(preset._controlNetWeight);
            gl.SetCurrentControlNetPreprocessorBySubstring(preset._controlNetPreprocessor);
            gl.SetCurrentControlNetModelBySubstring(preset._controlNetModel);

        }
        */
    }

    public void OnCameraStarted(WebCamTexture device)
    {
        float aspectX = (float)device.width / (float)device.height;
        Debug.Log("Camera started.  W:" + device.width + " H:" + device.height + " AspectX: " + aspectX);
        var vScale = m_meshRenderer.gameObject.transform.parent.localScale;
        vScale.x = aspectX;
        m_meshRenderer.gameObject.transform.parent.localScale = vScale;


        var processedVScale = m_processedRenderer.gameObject.transform.parent.localScale;
        float processedAspectX = (float)GameLogic.Get().GetGenWidth() / (float)GameLogic.Get().GetGenHeight();
        processedVScale.x = processedAspectX;

        m_processedRenderer.gameObject.transform.parent.localScale = processedVScale;
    }
    public void OnSnapShotModeImageRenderFinished(GameObject picGameObject)
    {

        PicMain picMain = picGameObject.GetComponent<PicMain>();
        Texture2D tex = new Texture2D(picMain.m_pic.sprite.texture.width, picMain.m_pic.sprite.texture.height, TextureFormat.ARGB32, false);
        tex.SetPixels(picMain.m_pic.sprite.texture.GetPixels());
        tex.Apply();
        ResizeTool.Resize(tex, GameLogic.Get().GetGenWidth(), GameLogic.Get().GetGenHeight(), true);

        //m_processedRenderer.material.mainTexture = tex;

        m_meshRenderer.material.mainTexture = tex;

        //oh, let's kill the pic too
        GameObject.Destroy(picGameObject);
        RTMessageManager.Get().Schedule(7, this.StartSnapshotSequence);
        ClearAllBats();
        RTAudioManager.Get().StopMusic();
        SetHauntingFadeOut();
        RTMessageManager.Get().Schedule(0, RTAudioManager.Get().Play, "image_reveal"); //plays crap.wav from Resources dir in 2 seconds

        if (m_autoSaveImages)
        {
            //generate a random filename UUID
            string fileName = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + "crazyCam_" + System.Guid.NewGuid() + ".png";
            File.WriteAllBytes(fileName, tex.EncodeToPNG());
        }

    }

    public void OnImageRenderFinished(GameObject picGameObject)
    {
        if (m_renderingIsPaused) return;


        PicMain picMain = picGameObject.GetComponent<PicMain>();

        // I need to do something like this: m_processedRenderer.material.mainTexture = picMain.m_pic.sprite.texture; but I need to make sure I own the texture, otherwise
        //when the sprite is destroyed, it will destroy my texture
        //so I need to make a copy of the texture
        Texture2D tex = new Texture2D(picMain.m_pic.sprite.texture.width, picMain.m_pic.sprite.texture.height, TextureFormat.ARGB32, false);
        tex.SetPixels(picMain.m_pic.sprite.texture.GetPixels());
        tex.Apply();
        ResizeTool.Resize(tex, GameLogic.Get().GetGenWidth(), GameLogic.Get().GetGenHeight(), true);
        m_processedRenderer.material.mainTexture = tex;

      
        if (m_autoSaveImages)
        {
            //generate a random filename UUID
            string fileName = Config.Get().GetBaseFileDir("/" + Config._saveDirName + "/") + "crazyCam_" + System.Guid.NewGuid() + ".png";
            File.WriteAllBytes(fileName, tex.EncodeToPNG());

        }
        //oh, let's kill the pic too
        GameObject.Destroy(picGameObject);
    }

    void CreateBat(Vector3 vStartingPos, Vector3 vTargetPos, float animationDuration)
    {

        if (!m_bIsActive) return;

        Transform parentObj = RTUtil.FindObjectOrCreate("CrazyCamMode").transform;

        //let's instaniate our _batPrefab here
        var obj = GameObject.Instantiate(_batPrefab);
        obj.transform.SetParent(parentObj);

        obj.transform.localPosition = vStartingPos;

        //Call SetTarget on Bat script on obj
        var bat = obj.GetComponent<Bat>();
        bat.FlyTo(vStartingPos, vTargetPos, animationDuration, false);
    }

    public void ClearAllBats()
    {
        StartCoroutine(ClearAllBatsCoroutine());
    }

    private IEnumerator ClearAllBatsCoroutine()
    {
        Transform parentObj = RTUtil.FindObjectOrCreate("CrazyCamMode").transform;

        // Get all Bat components from children
        Bat[] bats = parentObj.GetComponentsInChildren<Bat>();

        if (bats.Length == 0)
            yield break;

        // Calculate delay between each bat to spread over 1 second
        float totalDuration = 0.4f;
        float delayPerBat = totalDuration / (float)bats.Length;

        foreach (Bat bat in bats)
        {
            if (bat != null)
            {
                bat.FlyAway();
                yield return new WaitForSeconds(delayPerBat);
            }
        }
    }

    void CreateFilmDevelopmentEffect()
    {

        if (!m_bIsActive) return;

        // Get the texture from the mesh renderer
        Texture texture = m_meshRenderer.material.mainTexture;
        if (texture == null) return;

        // Create a 3D quad in world space
        GameObject overlayObj = new GameObject("FilmDevelopmentEffect");

        // Add a mesh renderer and mesh filter
        var meshFilter = overlayObj.AddComponent<MeshFilter>();
        var meshRenderer = overlayObj.AddComponent<MeshRenderer>();

        // Create a quad mesh
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        meshFilter.mesh = quad.GetComponent<MeshFilter>().mesh;
        GameObject.Destroy(quad);

        // Calculate the aspect ratio from the texture
        float aspectRatio = (float)texture.width / (float)texture.height;

        // Position it at the same location as the camera mesh renderer
        overlayObj.transform.position = m_meshRenderer.transform.position;
        overlayObj.transform.rotation = m_meshRenderer.transform.rotation;

        // Scale it to match the texture aspect ratio
        Vector3 scale = m_meshRenderer.transform.parent.parent.localScale;
        scale.x = scale.y * aspectRatio; // Adjust width based on height and aspect ratio
        overlayObj.transform.localScale = scale;

        // Create a material with the overlay color
        Material overlayMaterial = new Material(Shader.Find("Standard"));
        overlayMaterial.color = new Color(1, 0, 0, 0.5f);
        overlayMaterial.SetFloat("_Mode", 3); // Transparent mode
        overlayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        overlayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        overlayMaterial.SetInt("_ZWrite", 0);
        overlayMaterial.DisableKeyword("_ALPHATEST_ON");
        overlayMaterial.EnableKeyword("_ALPHABLEND_ON");
        overlayMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        overlayMaterial.renderQueue = 3000;

        meshRenderer.material = overlayMaterial;

        // Position it slightly in front of the camera mesh
        overlayObj.transform.Translate(0, 0, -0.01f);

        //RTMessageManager.Get().Schedule(1.0f, (obj) => GameObject.Destroy(obj), overlayObj);

        //ok,  now that we know the rect and position...

        //calculate the world rect of the overlayObj
        Rect worldRectOfOverlay = new Rect(overlayObj.transform.position.x - (overlayObj.transform.localScale.x / 2),
            overlayObj.transform.position.y - (overlayObj.transform.localScale.y / 2),
            overlayObj.transform.localScale.x,
            overlayObj.transform.localScale.y);


        float chunksWidth = 20;
        float chunksHeight = 10;
        Transform parentObj = RTUtil.FindObjectOrCreate("CrazyCamMode").transform;

        float gapX = (overlayObj.transform.localScale.x / chunksWidth);
        float gapY = (overlayObj.transform.localScale.y / chunksHeight);

        Vector3 vStartingPos = new Vector3(-5.0f, 5.0f, 0.27f);
        
        float animationDuration = 10.0f;

        for (float x = 0; x < overlayObj.transform.localScale.x; )
        {
            for (float y = 0; y < overlayObj.transform.localScale.y; )
            {
                y += gapY;
                Vector3 vTargetPos = new Vector3(worldRectOfOverlay.xMin + x + (gapX / 2), worldRectOfOverlay.yMin + y + ((gapY / 2) * -1.0f), 0.27f);
                 float batSpeed = 1.0f;

                float seconds = animationDuration * (x / overlayObj.transform.localScale.x);
                //let's also smooth out the seconds using the Y
                seconds += gapX * (y / overlayObj.transform.localScale.y);
                
                //add some slight randomness to the target pos and speed
                vTargetPos += new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0);
                batSpeed += Random.Range(-0.2f, 0.2f);

                //randomize their size too

                RTMessageManager.Get().Schedule(seconds, this.CreateBat, vStartingPos, vTargetPos, batSpeed);
             }
            x += gapX;
        }


        //oh, let's kill the overlayObj
        GameObject.Destroy(overlayObj);
    }

    void GrabCurrentCameraImageAndProcessIt(WebCamTexture webCamTex)
    {
        float aspectRatio = (float)webCamTex.width / (float)webCamTex.height;
        float targetAspectRatio = (float)GameLogic.Get().GetGenWidth() / (float)GameLogic.Get().GetGenHeight();

        Rect rect;
        if (aspectRatio > targetAspectRatio)
        {
            // Camera feed is wider than target: crop width
            float cropWidth = webCamTex.height * targetAspectRatio;
            float x = (webCamTex.width - cropWidth) * 0.5f;
            rect = new Rect(x, 0, cropWidth, webCamTex.height);
        }
        else
        {
            // Camera feed is taller than target: crop height
            float cropHeight = webCamTex.width / targetAspectRatio;
            float y = (webCamTex.height - cropHeight) * 0.5f;
            rect = new Rect(0, y, webCamTex.width, cropHeight);
        }

        //Debug.Log("Tex of "+webCamTex.width+","+webCamTex.height+" squared to Rect: " + rect);
        //Save the image to the Texture2D
        Texture2D texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);

        texture.SetPixels(webCamTex.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height));
        texture.Apply();
        
        ResizeTool.Resize(texture, GameLogic.Get().GetGenWidth(), GameLogic.Get().GetGenHeight(), true);


        if (IsInSnapshotMode())
        {
            //in snapshot mode, we want to see the original image until the processed one is ready
            m_meshRenderer.material.mainTexture = texture;

            CreateFilmDevelopmentEffect();
        }

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

        }

        int gpu = Config.Get().GetFreeGPU(RTRendererType.Any_Local);
        if (gpu != -1)
        {
     
            List<string> jobsTodo = GameLogic.Get().GetPicJobListAsListOfStrings();
            if (jobsTodo.Count == 0)
            {
                RTQuickMessageManager.Get().ShowMessage("Add jobs to do!");
                return;
            }

            PicJob jobDefaultInfoToStartWith = new PicJob();

            jobDefaultInfoToStartWith._requestedPrompt = GameLogic.Get().GetModifiedGlobalPrompt();

            if (jobDefaultInfoToStartWith._requestedPrompt == "")
            {
                //let's trandomly set the prompt from one of the snapshot presets
                if (m_snapshotPresets.Count > 0)
                {
                    int randomIndex = Random.Range(0, m_snapshotPresets.Count);
                    var preset = m_snapshotPresets[randomIndex];
                    jobDefaultInfoToStartWith._requestedPrompt = preset._prompt;
                    //RTQuickMessageManager.Get().ShowMessage(preset._name + ": " + preset._prompt);
                    SetHauntingTextAndFadeItIn(preset._processingMessage);
                }
            }
            string audio = "";

            if (audio == "")
            {
                audio = Config.Get().GetDefaultAudioPrompt();
            }
            jobDefaultInfoToStartWith._requestedAudioPrompt = audio;
            jobDefaultInfoToStartWith._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            jobDefaultInfoToStartWith.requestedRenderer = RTRendererType.Any_Local;

            GameObject pic = ImageGenerator.Get().AddImageByTexture(texture);
            PicMain picMain = pic.GetComponent<PicMain>();
            picMain.SetDisableUndo(true);
            picMain.ClearRenderingCallbacks();
            PicTextToImage textToImage = pic.GetComponent<PicTextToImage>();
            picMain.AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, jobsTodo);
            picMain.SetStatusMessage("AI guided\n(waiting)");

            //oh, we want to know when it's done so we can grab the resulting image out of it

            if (IsInSnapshotMode())
            {
                pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnSnapShotModeImageRenderFinished;
            }
            else
            {
                pic.GetComponent<PicMain>().m_onFinishedRenderingCallback += OnImageRenderFinished;
            }
        }
    }
    void OnCameraDisplayedNewFrame(WebCamTexture webCamTex)
    {
        //Debug.Log("NEW FRAME");

        if (m_timer > Time.time) return; //we don't want to show pics this fast
        if (!Config.Get().IsAnyGPUFree()) return;
        if (m_renderingIsPaused) return;

        m_timer = Time.time + m_timeBetweenPicsSeconds;

        if (IsInSnapshotMode())
        {
            if (m_bGrabNextFrame)
            {
                GrabCurrentCameraImageAndProcessIt(webCamTex);
                m_bGrabNextFrame = false;
            }
        } else
        {
            GrabCurrentCameraImageAndProcessIt(webCamTex);
        }
       
    }

    void Update()
    {

    }

}
