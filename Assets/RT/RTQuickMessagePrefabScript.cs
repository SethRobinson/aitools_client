using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RTQuickMessagePrefabScript : MonoBehaviour
{
    Vector3 _originalPos;
    public GameObject _backGround;
    public TMPro.TMP_Text _textObj;
    public CanvasGroup _canvasGroup;
    bool _bNeedsUpdate = true;
    bool _bDidFirstUpdate = false;
    // Start is called before the first frame update
    void Start()
    {
        _canvasGroup.alpha = 0;  //avoid a flicker while we change its position
        _originalPos = _backGround.transform.position;

        
        RTMessageManager.Get().Schedule(1, this.Die);
    }

    void Die()
    {
        GameObject.Destroy(gameObject);
    }
    void Reposition()
    {
        _textObj.enabled = true;
      
        var vPos = _originalPos;
        var rt = _backGround.GetComponent<RectTransform>();
        rt.ForceUpdateRectTransforms();
        //move up
        vPos.y += 48;
     
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
