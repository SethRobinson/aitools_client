using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//A test to see how fast we can generate images and display them

public class PizzaLogic : MonoBehaviour
{
    public GameObject m_pizzaPrefab;
    public Texture2D m_templateTexture;
    public Texture2D m_alphaTexture;

    public Material m_alphaMat;
    public Material m_lumaMat; //we won't use this if we're using an alpha

    bool m_generatePizza;
    GameObject m_basePizzaTemplate;
    PicMain m_basePicMain;
    string m_json; //store this for requests so we don't have to compute it each time
    static PizzaLogic _this = null;
    Color m_oldBGColor;
    string m_negativePrompt = "pixelated, blurry, deformed, ugly";

    private void Awake()
    {
        _this = this;
    }

    public static PizzaLogic Get() { return _this; }

    public void OnPizzaRenderFinished(Texture2D tex, RTDB db)
    {
        if (!m_generatePizza)
        {
            return;
        }

        //The render is finished, let's grab the image (a Texture2D) and use it however we want
        GameObject pizzaParent = RTUtil.FindObjectOrCreate("Pizzas");

        GameObject pizzaObj = Instantiate(m_pizzaPrefab, pizzaParent.transform);
        Pizza pizza = pizzaObj.GetComponent<Pizza>();
        pizza.InitPizza(m_alphaMat, tex, m_alphaTexture);
    }

    public void OnStartGameMode()
    {
        GameLogic.Get().SetToolsVisible(false);
        ImageGenerator.Get().SetGenerate(false);
        GameLogic.Get().OnClearButton();
        GameLogic.Get().OnFixFacesChanged(false); //don't want faces on our pizza
        GameLogic.Get().SetInpaintStrength(1.0f);
        GameLogic.Get().SetSeed(-1); //make sure it's random
        GameLogic.Get().SetAlphaMaskFeatheringPower(20);
        //GameLogic.Get().SetMaskContentByName("latent noise");
        RTUtil.FindObjectOrCreate("PizzaGUI").SetActive(true);
        m_oldBGColor = Camera.allCameras[0].backgroundColor;
        Camera.allCameras[0].backgroundColor = Color.black;

        //save the json request, we can re-use it for each pizza
        m_json = GamePicManager.Get().BuildJSonRequestForInpaint("pizza, top view",  m_negativePrompt, m_templateTexture, m_alphaTexture, false);
        m_generatePizza = true;

        RTAudioManager.Get().PlayMusic("JOHN_MICHEL_CELLO-BACH_AVE_MARIA", 0.5f, 1.0f, true); 
    }

    public void OnEndGameMode()
    {
        Camera.allCameras[0].backgroundColor = m_oldBGColor;
        m_generatePizza = false;
        GameLogic.Get().OnClearButton();
        GameLogic.Get().SetToolsVisible(true);
        RTUtil.DestroyChildren(RTUtil.FindObjectOrCreate("Pizzas").transform);
        RTUtil.FindObjectOrCreate("PizzaGUI").SetActive(false);
        RTAudioManager.Get().StopMusic();
    }

    void Update()
    {
        if (!m_generatePizza) return;


        int gpuID = Config.Get().GetFreeGPU(RTRendererType.AI_Tools_or_A1111);

        if (gpuID != -1)
        {
            //this will send the json request to the aitools_server, and callback with the created image
            GamePicManager.Get().SpawnInpaintRequest(m_json, OnPizzaRenderFinished, new RTDB(), gpuID);
        }

    }

}
