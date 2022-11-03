using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

//A test to see how fast we can generate images and display them

public class ShootingGalleryLogic : MonoBehaviour
{
    public GameObject m_blockPrefab;
    public GameObject m_levelPrefab;
    public GameObject m_splatPrefab; //what enemies get hit with
    public GameObject m_playerSplatPrefab; //what the player gets hit with
    public GameObject m_textOverlayPrefab; //used for the "don't hit friendlies" message

    public Texture2D m_templateTexture;
    public Texture2D m_alphaTexture;

    public Material m_alphaMat;
    public Texture2D m_crosshairTexture;

    public TMPro.TMP_Text m_scoreText;

    string m_opposingTeamjson; //store this for requests so we don't have to compute it each time
    string m_yourTeamjson; //store this for requests so we don't have to compute it each time

    static ShootingGalleryLogic _this = null;
    Color m_oldBGColor;
    GameObject m_levelRoot;
    int m_score;


    public List<AnimationClip> m_upClips;

    public List<AnimationClip> m_rightClips;
    string m_opposingTeamMember;
    string m_yourTeamMember;
    public TMP_InputField m_opposingTeamMemberInputField;
    public TMP_InputField m_yourTeamMemberInputField;

    bool m_opposingTeamPromptHasChanged = true;
    bool m_yourTeamPromptHasChanged = true;

    float m_coolDownTimer;
    float m_coolDownAmountSeconds = 0.15f;
    Tween m_smallShakeTween;
    Tween m_bigShakeTween;

    enum eMode
    {
        MODE_OFF,
        MODE_ON,

        MODE_COUNT
    }

    eMode m_mode = eMode.MODE_OFF;

    private void Awake()
    {
        _this = this;
    }

    public bool GUIIsBeingUsed()
    {
        if (EventSystem.current.IsPointerOverGameObject()) return true;

        if (m_opposingTeamMemberInputField.isFocused) return true;
        if (m_yourTeamMemberInputField.isFocused) return true;

        return false;
    }

    public void UpdateScore()
    {
        m_scoreText.text = m_score.ToString();
    }

    public void ModScore(int mod)
    {
        m_score += mod;
        UpdateScore();
    }
    public void UpdateFromGUI()
    {
        if (m_opposingTeamMember != m_opposingTeamMemberInputField.text)
        {
            m_opposingTeamMember = m_opposingTeamMemberInputField.text;
            m_opposingTeamPromptHasChanged = true;
        }

        if (m_yourTeamMember != m_yourTeamMemberInputField.text)
        {
            m_yourTeamMember = m_yourTeamMemberInputField.text;
            m_yourTeamPromptHasChanged = true;
        }
        
    }
   
    public AnimationClip GetRandomAnimationClip(string name)
    {
        if (name == "up")
        {
            return m_upClips[UnityEngine.Random.Range(0, m_upClips.Count)];
        } else if (name == "right")
        {
            return m_rightClips[UnityEngine.Random.Range(0, m_upClips.Count)];
        }

        return null;
    }

    public SpawnPoint GetOpenSpawnPoint()
    {

        List<GameObject> spawnList = new List<GameObject>();

        RTUtil.AddObjectsToListByNameIncludingInactive(m_levelRoot, "Spawn", false, spawnList);

        //we now have a list of all spawnpoints.
        //create a new list but only include spawnpoints that aren't "busy"

        List<GameObject> spawnListNotBusy = new List<GameObject>();

        foreach (GameObject go in spawnList)
        {
            if (!go.GetComponent<SpawnPoint>().IsBusy())
            {
                spawnListNotBusy.Add(go);
            }
        }


        if (spawnListNotBusy.Count == 0) return null;

        return spawnListNotBusy[UnityEngine.Random.Range(0, spawnListNotBusy.Count)].GetComponent<SpawnPoint>();
    }

    public static ShootingGalleryLogic Get() { return _this; }
    public void OnImageRenderFinished(Texture2D tex, RTDB db)
    {
        if (m_mode == eMode.MODE_OFF) return;

        //The render is finished, let's grab the image (a Texture2D) and use it however we want
     
        GameObject pizzaObj = Instantiate(m_blockPrefab, m_levelRoot.transform);
        GalleryTarget pizza = pizzaObj.GetComponent<GalleryTarget>();
        pizza.InitImage(m_alphaMat, tex, null);

        //it's ready to be attached to a target, if we can find one

        SpawnPoint spawnPoint = GetOpenSpawnPoint();

        if (spawnPoint == null)
        {
            Debug.Log("No spawn point open!");
            GameObject.Destroy(pizzaObj);
            return;
        }
        
        spawnPoint.SpawnTarget(pizzaObj, db);

    }

    //thanks to https://wintermutedigital.com/post/2020-01-29-the-ultimate-guide-to-custom-cursors-in-unity/
    void AddCrosshairs()
    {
        Vector2 cursorOffset = new Vector2(m_crosshairTexture.width / 2, m_crosshairTexture.height / 2);
        Cursor.SetCursor(m_crosshairTexture, cursorOffset, CursorMode.ForceSoftware);
    }


    void RemoveCrosshairs()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
    public void OnStartGameMode()
    {
        m_mode = eMode.MODE_ON;
        GameLogic.Get().SetGameMode(GameLogic.eGameMode.EXPERIMENT);
        GameLogic.Get().SetToolsVisible(false);
        ImageGenerator.Get().SetGenerate(false);
        GameLogic.Get().OnClearButton();
        GameLogic.Get().OnFixFacesChanged(false); 
        GameLogic.Get().SetInpaintStrength(1);
        GameLogic.Get().SetAlphaMaskFeatheringPower(4);
        m_score = 0;
        UpdateScore();
        //GameLogic.Get().SetMaskContentByName("latent noise");
        RTUtil.FindObjectOrCreate("GalleryGUI").SetActive(true);
        m_oldBGColor = Camera.allCameras[0].backgroundColor;
        Camera.allCameras[0].backgroundColor = Color.black;
        
        m_levelRoot = Instantiate(m_levelPrefab, null);
        m_levelRoot.name = "Gallery";

        m_opposingTeamPromptHasChanged = true;
        m_yourTeamPromptHasChanged = true;
        AddCrosshairs();
        UpdateFromGUI();

        RTAudioManager.Get().PlayMusic("Chee Zee Jungle", 0.5f, 1.0f, true);
    }

    public void OnEndGameMode()
    {
        m_mode = eMode.MODE_OFF;
        GameLogic.Get().SetGameMode(GameLogic.eGameMode.NORMAL);

        GameLogic.Get().OnClearButton();
        GameLogic.Get().SetToolsVisible(true);
        RTUtil.KillObjectByName("Gallery");
        RTUtil.FindObjectOrCreate("GalleryGUI").SetActive(false);
        Camera.allCameras[0].backgroundColor = m_oldBGColor;
        RemoveCrosshairs();
        RTAudioManager.Get().StopMusic();

    }

    void ShootGun()
    {
        var camera = Camera.allCameras[0];

        Vector2 ray = new Vector2(camera.ScreenToWorldPoint(Input.mousePosition).x, camera.ScreenToWorldPoint(Input.mousePosition).y);
        RaycastHit2D[] hits = Physics2D.RaycastAll(ray, Vector2.zero, 0.0f, ~0);
        
        if (hits.Length > 0)
        {

            int sorting = -100000;
            RaycastHit2D finalHit = new RaycastHit2D();

            foreach ( RaycastHit2D hit in hits)
            {
                SpriteRenderer spriteRenderer = hit.collider.gameObject.GetComponent<SpriteRenderer>();
           
                if (spriteRenderer != null)
                {
                    if (sorting <= spriteRenderer.sortingOrder)
                    {
                        sorting = spriteRenderer.sortingOrder;
                        finalHit = hit;
                    }
                }
            }

            if (finalHit.collider && finalHit.collider.gameObject != null)
            {

                var hitObj = finalHit.collider.gameObject;

                //Debug.Log("Hit "+hits.Length+" objects.  Closest (by sort layer) was " + hitObj.name);
                GameObject splatObj = Instantiate(m_splatPrefab, hitObj.transform);
                splatObj.GetComponent<Splat>().InitSplat(finalHit, sorting);

                if (m_smallShakeTween == null)
                {
                    m_smallShakeTween = m_levelRoot.transform.DOShakePosition(0.1f, 0.05f);
                    m_smallShakeTween.SetAutoKill(false);
                } else
                {
                    m_smallShakeTween.Rewind();
                    m_smallShakeTween.Play();
                }

            }


            //hit.collider.gameObject.transform.parent.gameObject;
        }
        else
        {
            Debug.Log("nothing");
        }

    }
    void HandleInput()
    {

        if (Input.GetMouseButton(0))
        {
            if (m_coolDownTimer < Time.time)
            {
                ShootGun();
                m_coolDownTimer = Time.time + m_coolDownAmountSeconds;
            }
        }
       
    }

    public void OnPlayerDamage(GalleryTarget galleryTargetScript, int damage)
    {
        GameObject splat = Instantiate(m_playerSplatPrefab, m_levelRoot.transform);
        splat.transform.position = galleryTargetScript.m_spriteRenderer.gameObject.transform.position;
        splat.transform.Rotate(0, 0, Random.Range(0, 360));

        if (m_bigShakeTween == null)
        {
            m_bigShakeTween =  m_levelRoot.transform.DOShakePosition(0.4f).SetDelay(0.45f);
            m_bigShakeTween.SetAutoKill(false);
        } else
        {
            m_bigShakeTween.Rewind();
            m_bigShakeTween.Play();
        }

        m_score = 0;
        UpdateScore();
    }


    void Update()
    {
        switch(m_mode)
        {
            case eMode.MODE_ON:

                if (RTUtil.IsMouseOverGameWindow && !GUIIsBeingUsed())
                {
                    HandleInput();
                }


                if (Config.Get().IsAnyGPUFree())
                {
                    //this will send the json request to the aitools_server, and callback with the created image
                    int r = UnityEngine.Random.Range(0, 5);

                    RTDB db = new RTDB();

                    if (r == 0)
                    {
                        //spawn friendly
                        if (m_yourTeamPromptHasChanged)
                        {
                            //save the json request, we can re-use it for each pizza
                            m_yourTeamjson = GamePicManager.Get().BuildJSonRequestForInpaint(m_yourTeamMember, "", m_templateTexture, m_alphaTexture, true);
                            m_yourTeamPromptHasChanged = false;
                        }
                        db.Set("tag", "Friend");

                        if (m_yourTeamMember != "")
                            GamePicManager.Get().SpawnInpaintRequest(m_yourTeamjson, OnImageRenderFinished, db);
                    }
                    else
                    {
                        //spawn opponent
                        if (m_opposingTeamPromptHasChanged)
                        {
                            //save the json request, we can re-use it for each pizza
                            m_opposingTeamjson = GamePicManager.Get().BuildJSonRequestForInpaint(m_opposingTeamMember, "", m_templateTexture, m_alphaTexture, true);
                            m_opposingTeamPromptHasChanged = false;
                        }
                        db.Set("tag", "Opponent");
                  
                        if (m_opposingTeamMember != "")
                            GamePicManager.Get().SpawnInpaintRequest(m_opposingTeamjson, OnImageRenderFinished, db);
                    }
                }

                break;
        }
      
    }

}
