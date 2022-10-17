using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.GraphicsBuffer;

public class RTToolTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string _text = "Change this text to the tooltip you want!";

    GameObject m_tipInstance;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    void ShowTip(bool bNew)
    {
        if (bNew)
        {
            if (m_tipInstance == null)
            {
                //create it
                m_tipInstance = Instantiate(RTToolTipManager.Get().m_toolTipPrefab, gameObject.transform);
                var bg = RTUtil.FindInChildrenIncludingInactive(m_tipInstance, "BG");
                var textObject = RTUtil.FindInChildrenIncludingInactive(m_tipInstance, "Text");
                TMPro.TMP_Text textComp = textObject.GetComponent<TMPro.TMP_Text>();
                textComp.text = _text;
                //can't position the canvas itself, so we'll grab its child
                RawImage rawImage = bg.GetComponent<RawImage>();

                var myCanvas = gameObject.GetComponentInParent<Canvas>();

                if (!myCanvas || myCanvas.renderMode == RenderMode.WorldSpace)
                {
                    var cam = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
                    m_tipInstance.transform.SetParent(null); //move to root, we can't be a screen canvas attached to a world canvas

                    //special handling, we need to convert the camera position to screenspace first
                    Vector3 screenPos = cam.WorldToScreenPoint(transform.position);
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
        ShowTip(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
       ShowTip(false);
    }

   
    // Update is called once per frame
    void Update()
    {
          
    }
}
