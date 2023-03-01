using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RTToolTipPrefabScript : MonoBehaviour
{
    Vector3 _originalPos;
    public GameObject _backGround;
    public TMPro.TMP_Text _textObj;
    public CanvasGroup _canvasGroup;
    bool _bNeedsUpdate = true;
    bool _bDidFirstUpdate = false;
    // Start is called before the first frame update
    TextAlignment _alignment = TextAlignment.Center;

    void Start()
    {
        _canvasGroup.alpha = 0;  //avoid a flicker while we change its position
        _originalPos = _backGround.transform.position;
    }

    public void SetAlignment(TextAlignment alignment)
    {
        _alignment = alignment; 
    }
    void Reposition()
    {
        _textObj.enabled = true;

       // _textObj.alignment = TMPro.TextAlignmentOptions.Left;
        var vPos = _originalPos;
        var rt = _backGround.GetComponent<RectTransform>();

        if (_alignment == TextAlignment.Left)
        {
            rt.pivot = new Vector2(0, 0);
            rt.ForceUpdateRectTransforms();
        }
        else
        {
            rt.ForceUpdateRectTransforms();

            //move up, and then we'll tweak that if it looks like it's off the screen
            vPos.y += 48;

        }



        float offscreenX = vPos.x + rt.offsetMin.x;
        float offscreenY = vPos.y - rt.offsetMin.y;
     
        if (offscreenX  < 0)
        {
            //move it to the right
            vPos.x += -offscreenX;
        }

        if (vPos.x + rt.offsetMax.x > Screen.width)
        {
            //move to the left a bit
            vPos.x += Screen.width- (vPos.x + rt.offsetMax.x);
        }

        if (offscreenY > Screen.height)
        {
            //move it below us, there is no room above
            vPos.y -= (48+24+ (rt.offsetMax.y*2));
        }

        _backGround.transform.position = vPos;
        _canvasGroup.alpha = 1;
    }

    void OnBecameInvisible()
    {
        Debug.Log("Invisible");
    }

    void OnDisable()
    {
        //Debug.Log("PrintOnDisable: script was disabled");
        Destroy(gameObject);
    }

    void OnEnable()
    {
        //Debug.Log("PrintOnEnable: script was enabled");
        
    }


    // Update is called once per frame
    void Update()
    {

        if (!_bDidFirstUpdate)
        {
            _bDidFirstUpdate = true;
            return;
        }
      
        if (_bNeedsUpdate)
        {
             Reposition();
            _bNeedsUpdate = false;
        }
    }
}
