using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.GraphicsBuffer;

//Attach to GUI object for tooltip.  Needs RTToolTipManger running somewhere as well

//By Seth A. Robinson, 2022

public class RTToolTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string _text = "Change this text to the tooltip you want!";

    GameObject m_tipInstance;
    Vector3 _vCustomLocation = new Vector3(0, 0, 0);
    bool _bUsingCustomLocation = false;
    TextAlignment _alignment = TextAlignment.Center;
    // Start is called before the first frame update
    float _delayTimer = 0;
    
    void Start()
    {
        
    }

    //stop showing the tip when this component is removed or destroyed
    private void OnDestroy()
    {
        ShowTip(false);
    }

    public void SetCustomLocationSetup(Vector3 vPos)
    {
        _vCustomLocation = vPos;
        _bUsingCustomLocation = true;
    }

    public void SetAlignment(TextAlignment alignment)
    {
        _alignment = alignment;
    }

    void ShowTip(bool bNew)
    {
        if (bNew)
        {
            if (m_tipInstance == null)
            {
                RTToolTipManager tipManager = RTToolTipManager.Get();
                //create it
                m_tipInstance = Instantiate(tipManager.m_toolTipPrefab, gameObject.transform);
                var bg = RTUtil.FindInChildrenIncludingInactive(m_tipInstance, "BG");
                var textObject = RTUtil.FindInChildrenIncludingInactive(m_tipInstance, "Text");
                TMPro.TMP_Text textComp = textObject.GetComponent<TMPro.TMP_Text>();
                textComp.text = _text;
                RTToolTipPrefabScript _tipPrefabScript = m_tipInstance.GetComponent<RTToolTipPrefabScript>();
                if (_tipPrefabScript == null)
                {
                    Debug.LogError("Prefab doesn't have RTToolTipPrefabScript attached, WHY?!");
                }
                
                if (_bUsingCustomLocation)
                {
                    m_tipInstance.transform.position = _vCustomLocation;
                }

                _tipPrefabScript.SetAlignment(_alignment);

                //can't position the canvas itself, so we'll grab its child
                RawImage rawImage = bg.GetComponent<RawImage>();

                var myCanvas = gameObject.GetComponentInParent<Canvas>();

                if (!myCanvas || myCanvas.renderMode == RenderMode.WorldSpace)
                {
                    var cam = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
                    m_tipInstance.transform.SetParent(null); //move to root, we can't be a screen canvas attached to a world canvas

                    //special handling, we need to convert the camera position to screenspace first
                    Vector3 screenPos = cam.WorldToScreenPoint(transform.position);

                    if (_bUsingCustomLocation)
                    {
                       screenPos = cam.WorldToScreenPoint(_vCustomLocation);
                    }

                    m_tipInstance.transform.position = screenPos;
             
                }

            }
        } else
        {
            //stop showing it
            if (m_tipInstance)
            {
                GameObject.Destroy(m_tipInstance);
                m_tipInstance = null;
            }
        }
        
       
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        _delayTimer = Time.time + RTToolTipManager.Get().m_delayBeforeShowingSeconds;
      
    }

    public void OnPointerExit(PointerEventData eventData)
    {
       ShowTip(false);
       _delayTimer = 0;
    }

    
    // Update is called once per frame
    void Update()
    {
          if (_delayTimer != 0 && _delayTimer < Time.time)
        {
            ShowTip(true);
            _delayTimer = 0;
        }
    }
}
