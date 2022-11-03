using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.Rendering;

//A test to see how fast we can generate images and display them

public class BreakoutLogic : MonoBehaviour
{
    public GameObject m_blockPrefab;
    public Texture2D m_templateTexture;
    public Texture2D m_alphaTexture;

    public Material m_alphaMat;
    public Material m_lumaMat; //we won't use this if we're using an alpha

    string m_json; //store this for requests so we don't have to compute it each time
   
    static BreakoutLogic _this = null;

    private void Awake()
    {
        _this = this;
    }

    public static BreakoutLogic Get() { return _this; }
    public void OnBoardRenderFinished(Texture2D tex, RTDB db)
    {
       
        //The render is finished, let's grab the image (a Texture2D) and use it however we want
        GameObject boardParent = RTUtil.FindObjectOrCreate("Breakout");

      
        GameObject pizzaObj = Instantiate(m_blockPrefab, boardParent.transform);
        Pizza pizza = pizzaObj.GetComponent<Pizza>();
        pizza.InitPizza(m_lumaMat, tex, m_alphaTexture);
        
    }

    public void OnStartBreakout()
    {
        GameLogic.Get().SetToolsVisible(false);
        ImageGenerator.Get().SetGenerate(false);
        GameLogic.Get().OnClearButton();
        GameLogic.Get().OnFixFacesChanged(false); //don't want faces on our pizza
        GameLogic.Get().SetInpaintStrength(1.0f);
        GameLogic.Get().SetAlphaMaskFeatheringPower(20);
        //GameLogic.Get().SetMaskContentByName("latent noise");
        RTUtil.FindObjectOrCreate("BreakoutGUI").SetActive(true);
        Camera.allCameras[0].backgroundColor = Color.black;

        //save the json request, we can re-use it for each pizza
        m_json = GamePicManager.Get().BuildJSonRequestForInpaint("a cute dog in front of a black background, cartoon", "", m_templateTexture, m_alphaTexture, true);

        if (Config.Get().IsAnyGPUFree())
        {
            //this will send the json request to the aitools_server, and callback with the created image
            GamePicManager.Get().SpawnInpaintRequest(m_json, OnBoardRenderFinished, new RTDB());
        }


    }

    public void OnEndPizza()
    {
      
        GameLogic.Get().OnClearButton();
        GameLogic.Get().SetToolsVisible(true);
        RTUtil.DestroyChildren(RTUtil.FindObjectOrCreate("Pizzas").transform);
        RTUtil.FindObjectOrCreate("PizzaGUI").SetActive(false);
    }

    void Update()
    {
      
    }

}
