using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class PicInfoPanel : MonoBehaviour
{
    public GameObject _infoPanelObj;
    public TMPro.TMP_InputField _inputObj;
    public PicMain _picMain;
    // Start is called before the first frame update

    public SpriteRenderer _spriteRendererOne;
    public GameObject _spriteOne;
    public CopyWithoutColorTags _copyWithoutColorTags;

    void Start()
    {
    }

    public void UpdateVisuals()
    {
        if (_spriteRendererOne.sprite == null)
        {
            _spriteOne.SetActive(false);
        } else
        {
            _spriteOne.SetActive(true);
        }
    }

    public void SetInfoText(string msg)
    {
        _inputObj.text = msg;

        UpdateVisuals();
    }

    public bool IsPanelOpen()
    {
        return _infoPanelObj.activeSelf;
    }

   

    public void SetSprite(Sprite sprite)
    {
        KillSprites();
        _spriteRendererOne.sprite = sprite;
    }
        
      public void OnInfoButtonClicked()
    {
        if (IsPanelOpen())
        {
            //turn it off
            _infoPanelObj.SetActive(false);
        } else
        {

            _picMain.UpdateInfoPanel();
            _infoPanelObj.SetActive(true);
        }
    }

    public string GetTextInfoWithoutColors()
    {
        return RTUtil.RemoveColorAndFontTags(_inputObj.text); //note that I don't use GetParsedText() because the panel might be disabled and it won't work
    }
    public void OnSaveSpriteOne()
    {

        string postfix = _picMain.GetCurrentStats().m_lastControlNetModelPreprocessor;
        if (postfix.Length == 0)
        {
            postfix = "controlNet";
        }

       

        _picMain.SaveFile("", "/" + Config._saveDirName, _spriteRendererOne.sprite.texture, "_" + postfix);
    }
    // Update is called once per frame
    void Update()
    {

    }

    private void KillSprites()
    {
        if (_spriteRendererOne.sprite != null && _spriteRendererOne.sprite.texture != null)
        {
            UnityEngine.Object.Destroy(_spriteRendererOne.sprite.texture); //this should also cause the sprite to be destroyed?
        }
    }
private void OnDestroy()
    {
        KillSprites();
    }
}
