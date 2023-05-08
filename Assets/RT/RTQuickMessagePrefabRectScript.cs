using UnityEngine;

public class RTQuickMessagePrefabRectScript : MonoBehaviour
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

       // RTMessageManager.Get().Schedule(1, this.Die);
    }

    public void SetKillTime(float timeInSecondsBeforeKillingIt)
    {
        RTMessageManager.Get().Schedule(timeInSecondsBeforeKillingIt, this.Die);
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
