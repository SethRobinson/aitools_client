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

    // Reference equality cache so we don't pay to re-decode PNGs and re-composite a
    // thumbnail strip on every UpdateInfoPanel tick while the panel is open. The
    // byte[][] reference is stable for a given history record, so a simple identity
    // check is enough.
    private byte[][] _lastInputImagePngs;

    // Per-thumbnail edge length in pixels for the composite strip. 256 keeps each
    // input recognizable without producing megabyte-scale composites for 5-input jobs.
    private const int InputThumbSize = 256;
    private const int InputThumbGap = 4;

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
        EnsureSpriteOnTopOfInfoPanelBg();
    }

    /// <summary>
    /// Show the input images that fed an N-input image-to-image job as a horizontal
    /// thumbnail strip in the info panel's existing sprite slot (the same slot the
    /// legacy ControlNet preview used). Pass null or an all-null array to clear.
    ///
    /// We re-use the existing prefab fields (_spriteOne / _spriteRendererOne) and the
    /// existing SetSprite path so no prefab edit is required to enable this feature.
    /// </summary>
    public void SetInputImages(byte[][] inputPngs)
    {
        // No inputs: clear whatever was last shown (incl. the cached strip below).
        if (inputPngs == null)
        {
            if (_lastInputImagePngs != null)
            {
                SetSprite(null);
                _lastInputImagePngs = null;
                UpdateVisuals();
            }
            return;
        }

        // Same job's inputs as last call -> nothing to do. Avoids decoding 4 PNGs and
        // BlitImageFitted'ing the strip every frame the panel is open.
        if (ReferenceEquals(inputPngs, _lastInputImagePngs))
        {
            return;
        }

        // Collect the non-empty slots in their original input-index order so the strip
        // reads left-to-right input1, input2, input3, ... (skipping holes).
        List<byte[]> valid = new List<byte[]>();
        for (int i = 0; i < inputPngs.Length; i++)
        {
            if (inputPngs[i] != null && inputPngs[i].Length > 0)
                valid.Add(inputPngs[i]);
        }
        if (valid.Count == 0)
        {
            SetSprite(null);
            _lastInputImagePngs = inputPngs;
            UpdateVisuals();
            return;
        }

        // Decode each PNG into a temp Texture2D - we throw these away after the blit.
        List<Texture2D> decoded = new List<Texture2D>(valid.Count);
        for (int i = 0; i < valid.Count; i++)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(valid[i]))
            {
                decoded.Add(tex);
            }
            else
            {
                UnityEngine.Object.Destroy(tex);
            }
        }
        if (decoded.Count == 0)
        {
            SetSprite(null);
            _lastInputImagePngs = inputPngs;
            UpdateVisuals();
            return;
        }

        int count = decoded.Count;
        int compW = (InputThumbSize * count) + (InputThumbGap * Mathf.Max(0, count - 1));
        int compH = InputThumbSize;

        Texture2D comp = new Texture2D(compW, compH, TextureFormat.RGBA32, false);
        // Start with transparent so letterbox gaps from "fit" mode don't show garbage.
        comp.Fill(new Color(0f, 0f, 0f, 0f));

        int cursorX = 0;
        for (int i = 0; i < count; i++)
        {
            comp.BlitImageFitted(decoded[i], cursorX, 0, InputThumbSize, InputThumbSize, "fit", 1f);
            cursorX += InputThumbSize + InputThumbGap;
        }
        comp.Apply();

        for (int i = 0; i < decoded.Count; i++)
        {
            UnityEngine.Object.Destroy(decoded[i]);
        }

        // Match SetControlImage's pixelsPerUnit math so the strip lives in the same
        // world units as the legacy single-image preview.
        float biggestSize = Mathf.Max(comp.width, comp.height);
        Sprite strip = Sprite.Create(
            comp,
            new Rect(0, 0, comp.width, comp.height),
            new Vector2(0.5f, 0.5f),
            biggestSize / 5.12f,
            0,
            SpriteMeshType.FullRect);

        SetSprite(strip);
        _lastInputImagePngs = inputPngs;
        UpdateVisuals();
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

    /// <summary>
    /// Force <see cref="_spriteRendererOne"/> to render above the InfoPanel's dark
    /// translucent background. The prefab has this SpriteRenderer at sortingOrder=6
    /// in the same sortingLayer as the parent <c>CanvasExtra</c> Canvas (also
    /// sortingOrder=6), which renders the dark bg Image. When sort orders tie the
    /// Sprite ends up rendered first / behind the canvas, so any non-near-black
    /// image (e.g. an N-input photo strip) shows up visibly darkened by the bg's
    /// ~64% black overlay. We bump the SpriteRenderer one notch above whatever
    /// parent canvas is in effect, picking up its sortingLayer at the same time so
    /// nothing else in the panel order gets accidentally hopped over.
    /// </summary>
    private void EnsureSpriteOnTopOfInfoPanelBg()
    {
        if (_spriteRendererOne == null) return;
        Canvas parentCanvas = _spriteRendererOne.GetComponentInParent<Canvas>();
        if (parentCanvas == null) return;
        _spriteRendererOne.sortingLayerID = parentCanvas.sortingLayerID;
        _spriteRendererOne.sortingOrder = parentCanvas.sortingOrder + 1;
    }

private void OnDestroy()
    {
        KillSprites();
    }
}
