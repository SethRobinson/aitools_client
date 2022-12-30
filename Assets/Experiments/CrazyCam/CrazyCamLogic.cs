using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

//A test to see how fast we can generate images and display them

class CrazyCamPreset
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
    public bool _reverse = false;
}

public class CrazyCamLogic : MonoBehaviour
{
    public MeshRenderer m_meshRenderer;
    public MeshRenderer m_processedRenderer;

    string m_json; //store this for requests so we don't have to compute it each time
    public TMP_Dropdown m_camDropdown;
    public TMP_Dropdown m_presetDropdown;
    public Toggle m_noTranslucencyToggle;
    public Toggle m_reverseMaskToggle;


    static CrazyCamLogic _this = null;
    List<CrazyCamPreset> m_presets;

    float m_timeBetweenPicsSeconds = 0.3f;
    float m_timer;

    private void Awake()
    {
        _this = this;
    }

    public static CrazyCamLogic Get() { return _this; }


    public void AddPreset(string presetName, string prompt, string maskContents, float denoisingStrength, bool bFixFaces, bool bNoTranslucency, float maskBlending, bool bReverse)
    {
        CrazyCamPreset preset = new CrazyCamPreset();
        preset._prompt = prompt;
        preset._maskContents = maskContents;
        preset._presetName = presetName;
        preset._denoisingStrength = denoisingStrength;
        preset._fixFaces= bFixFaces;
        preset._noTranslucency= bNoTranslucency;
        preset._maskBlending = maskBlending;
        preset._reverse = bReverse;

        m_presets.Add(preset);

        var options = new List<TMP_Dropdown.OptionData>();
        var option = new TMP_Dropdown.OptionData();
        option.text = presetName;
        options.Add(option);

        m_presetDropdown.AddOptions(options);

    }

    public void Start()
    {
        Debug.Log("Doing presets");
        m_presets = new List<CrazyCamPreset>();
        m_presetDropdown.ClearOptions();
        AddPreset("Preset: Use active settings", "", "", 0, true, false, 0, false); //special case, index 0 won't change anything
        AddPreset("Fix my face (method 1)", "good looking person", "original", 0, true, false, 0, false);
        AddPreset("Fix my face (method 2)", "good looking person", "original", 0.1f, false, false, 0, false);

        AddPreset("Muscle man", "body builder, man, handsome, ripped, athlete, perfect body, large muscles", "original", 0.25f, true, false, 0, false);
        AddPreset("Beautiful woman", "beautiful woman, elegant, cute, athlete, perfect body", "original", 0.25f, true, false, 0, false);
        AddPreset("Zombie person", "a zombie", "original", 0.25f, true, false, 0, false);
        AddPreset("My room has spiderwebs", "filled with ((cobwebs)), disgusting, spiders, horror", "original", 0.39f, true, false, 0, true);
        AddPreset("A monster", "a (((scary monster))) in a room", "original", 0.69f, false, true, 0, false);
        AddPreset("A giant spider", "a (((giant spider))) in a room", "original", 1.0f, false, true, 0, false);
        AddPreset("Spider man", "spider man", "original", 0.64f, false, true, 0, false);
        AddPreset("Erase me", "an empty room", "fill", 1.0f, false, true, 0, false);
        AddPreset("Old woman", "old woman, wrinkles, studio portrait, award winning", "original", 0.41f, true, true, 0, false);
        AddPreset("Old man", "old man, wrinkles, studio portrait, award winning", "original", 0.41f, true, true, 0, false);
        AddPreset("High five me", "two people giving high five, hands touching", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Happy new year w/ friends", "great friends celebrating the new year in times square, smiling, posing for picture", "latent noise", 1.0f, false, false, 0, true);
        AddPreset("Proposing at disney", "Man proposes to girlfriend at Disneyland, posing for photo, happy", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Breaking up at disney", "((sad)), scowling, depressed, couple at disneyland, frustrated, angry", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Hug me", "a loving couple, hugging, hug, in love, smiling, playing, candid, mischievous", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Copy me", "two people, synchronized movement, same position", "latent noise", 1.0f, true, false, 0, true);
        
        AddPreset("Ninja fight", "person fighting ninja, getting hit, reaction, punched, action shot, epic, fireball", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Holding fire", "person creating magic with hands, magic fire", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Holding light saber", "person holding light saber, epic, dramatic lighting, star wars", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("Holding a hamburger", "person posing with a delicious small hamburger, bokeh, hamburger in hand", "latent noise", 1.0f, true, false, 0, true);
        AddPreset("In the bad place", "((burning in hell)), horror, scary, screaming, epic, detailed, satan, skeleton, ghoul", "latent noise", 1.0f, false, false, 0, true);

        AddPreset("(for dog) A cat", "a cat in a room", "original", 0.45f, false, false, 0, false);
        AddPreset("(for dog) Cavalier with shades", "a Cavalier King Charles Spaniel wearing sunglasses", "original", 0.45f, false, false, 0, false);

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
        if (GameLogic.Get().GetSeed() < 0)
        {
            GameLogic.Get().SetSeed(0);
            Debug.Log("Setting seed to 0 for Crazy Cam, we don't want -1 because random seeds will look worse");
        }
        //GameLogic.Get().OnFixFacesChanged(false); //don't want faces on our pizza
       // GameLogic.Get().SetInpaintStrength(1.0f);
       // GameLogic.Get().SetAlphaMaskFeatheringPower(20);
        //GameLogic.Get().SetMaskContentByName("latent noise");
        RTUtil.FindObjectOrCreate("CrazyCamGUI").SetActive(true);
        RTUtil.FindObjectOrCreate("CrazyCamMode").SetActive(true);
        //Camera.allCameras[0].backgroundColor = Color.black;

        //save the json request, we can re-use it for each pizza
        //m_json = GamePicManager.Get().BuildJSonRequestForInpaint("a cute dog in front of a black background, cartoon", "", m_templateTexture, m_alphaTexture, true);


        /*
        if (Config.Get().IsAnyGPUFree())
        {
            //this will send the json request to the aitools_server, and callback with the created image
            GamePicManager.Get().SpawnInpaintRequest(m_json, OnBoardRenderFinished, new RTDB());
        }
        */
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

        m_noTranslucencyToggle.isOn = preset._noTranslucency;
        m_reverseMaskToggle.isOn = preset._reverse;

        if (gl.GetSeed() == -1)
        {
            gl.SetSeed(0);
        }
    }

    public void OnCameraStarted(WebCamTexture device)
    {
        float aspectX = (float)device.width / (float)device.height;
        Debug.Log("Camera started.  W:" + device.width + " H:" + device.height+" AspectX: "+aspectX);
        var vScale = m_meshRenderer.gameObject.transform.parent.localScale;
        vScale.x = aspectX;
        m_meshRenderer.gameObject.transform.parent.localScale = vScale;
       
        
        //m_processedRenderer.gameObject.transform.parent.localScale = vScale;
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
    }

    public void OnImageRenderFinished(Texture2D tex, RTDB db)
    {
        //Debug.Log("Got image");
        m_processedRenderer.material.mainTexture = tex;
        float timeTaken = Time.time - db.GetFloat("startTime");
        m_timeBetweenPicsSeconds= timeTaken/Config.Get().GetGPUCount();
        //Debug.Log(timeTaken);
    }
    void OnCameraDisplayedNewFrame(WebCamTexture webCamTex)
    {
        //Debug.Log("NEW FRAME");

        if (m_timer > Time.time) return; //we don't want to show pics this fast
        if (!Config.Get().IsAnyGPUFree()) return;

        m_timer = Time.time + m_timeBetweenPicsSeconds;
        RTDB db = new RTDB();
        db.Set("startTime", Time.time);
        //let's remember how long it takes from start to finish to extract from webcam, get it inpainted, and display it

      
        float aspectRatio = (float)webCamTex.width / (float)webCamTex.height;

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
        var json = GamePicManager.Get().BuildJSonRequestForInpaint(GameLogic.Get().GetPrompt(), GameLogic.Get().GetNegativePrompt(), texture, null, false, true,
            m_noTranslucencyToggle.isOn, m_reverseMaskToggle.isOn);

      
        GamePicManager.Get().SpawnInpaintRequest(json, OnImageRenderFinished, db);
    }

    void Update()
    {
      
    }

}
