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
    // Cache of the last history we composited, so we don't re-decode every PNG and
    // re-blit the whole timeline on every UpdateInfoPanel tick while the panel is open.
    // We compare by the underlying (byte[][] inputs, byte[] output) references, which are
    // stable per _jobHistory record until that record actually changes.
    private List<ImageHistoryRow> _lastHistory;

    // One generation step of the Pic's history: the input images it was fed (slots 0..4,
    // empty slots null) and the result it produced (null until the result arrives).
    public class ImageHistoryRow
    {
        public byte[][] inputs;
        public byte[] output;
    }

    // Per-thumbnail edge length in pixels. 256 keeps each image recognizable without
    // producing huge composites; shrunk automatically for very long histories (below).
    private const int InputThumbSize = 256;
    private const int InputThumbGap = 4;
    // Wider gap + a thin colored bar separating a step's input thumbnails from its result.
    private const int OutputSeparatorGap = InputThumbSize / 4;
    private const int OutputDividerWidth = 3;
    private static readonly Color OutputDividerColor = new Color(0.2f, 0.85f, 0.3f, 1f);
    // Vertical gap between timeline rows (one row per generation step).
    private const int InputRowGap = 12;
    // Clamp the composite texture's largest edge; thumbnails shrink for long chains so we
    // never blow past Unity's texture size limit.
    private const int MaxCompositeEdge = 8192;

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
    /// Show the Pic's full image history as a vertical timeline in the info panel's
    /// existing sprite slot (the same slot the legacy ControlNet preview used). One row
    /// per generation step, oldest at top -> newest at bottom; each row shows that step's
    /// input thumbnails, a green divider, then its result. This surfaces the original
    /// anchor images used in early steps, which the latest step only sees as a single
    /// already-composited input. Pass null or an empty list to clear.
    ///
    /// We re-use the existing prefab fields (_spriteOne / _spriteRendererOne) and the
    /// existing SetSprite path so no prefab edit is required to enable this feature.
    /// </summary>
    public void SetImageHistory(List<ImageHistoryRow> rows)
    {
        // Nothing to show: clear whatever was last shown.
        if (rows == null || rows.Count == 0)
        {
            if (_lastHistory != null)
            {
                SetSprite(null);
                _lastHistory = null;
                UpdateVisuals();
            }
            return;
        }

        // Same history as last call -> nothing to do. Avoids re-decoding every PNG and
        // re-blitting the whole timeline each frame the panel is open. Compared by the
        // underlying byte-array references, which are stable per history record.
        if (HistoryMatchesCache(rows))
        {
            return;
        }

        // Decode every cell. Per row: input textures (empty slots skipped) + optional output.
        int rowCount = rows.Count;
        var rowInputs = new List<List<Texture2D>>(rowCount);
        var rowOutput = new List<Texture2D>(rowCount);
        int maxInputCols = 0;
        bool anyOutput = false;

        for (int r = 0; r < rowCount; r++)
        {
            var ins = new List<Texture2D>();
            byte[][] inPngs = rows[r].inputs;
            if (inPngs != null)
            {
                for (int i = 0; i < inPngs.Length; i++)
                {
                    if (inPngs[i] == null || inPngs[i].Length == 0) continue;
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(inPngs[i])) ins.Add(t);
                    else UnityEngine.Object.Destroy(t);
                }
            }
            Texture2D outT = null;
            byte[] outPng = rows[r].output;
            if (outPng != null && outPng.Length > 0)
            {
                outT = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!outT.LoadImage(outPng)) { UnityEngine.Object.Destroy(outT); outT = null; }
            }
            rowInputs.Add(ins);
            rowOutput.Add(outT);
            if (ins.Count > maxInputCols) maxInputCols = ins.Count;
            if (outT != null) anyOutput = true;
        }

        // Pick a thumbnail size that keeps the composite within texture limits for long chains.
        int thumb = InputThumbSize;
        int sepWidth = anyOutput ? (OutputSeparatorGap + OutputDividerWidth + OutputSeparatorGap) : 0;
        int natW = maxInputCols * thumb + Mathf.Max(0, maxInputCols - 1) * InputThumbGap + sepWidth + (anyOutput ? thumb : 0);
        int natH = rowCount * thumb + Mathf.Max(0, rowCount - 1) * InputRowGap;
        int biggest = Mathf.Max(natW, natH);
        if (biggest > MaxCompositeEdge)
        {
            float scale = (float)MaxCompositeEdge / biggest;
            thumb = Mathf.Max(24, Mathf.FloorToInt(thumb * scale));
        }

        // Compute exact per-row widths with the chosen thumb, then the overall composite size.
        int compW = 1;
        for (int r = 0; r < rowCount; r++)
        {
            int inCount = rowInputs[r].Count;
            int w = inCount * thumb + Mathf.Max(0, inCount - 1) * InputThumbGap;
            if (rowOutput[r] != null)
            {
                if (inCount > 0) w += OutputSeparatorGap + OutputDividerWidth + OutputSeparatorGap;
                w += thumb;
            }
            if (w > compW) compW = w;
        }
        int compH = Mathf.Max(1, rowCount * thumb + Mathf.Max(0, rowCount - 1) * InputRowGap);

        Texture2D comp = new Texture2D(compW, compH, TextureFormat.RGBA32, false);
        // Start with transparent so letterbox gaps from "fit" mode don't show garbage.
        comp.Fill(new Color(0f, 0f, 0f, 0f));

        for (int r = 0; r < rowCount; r++)
        {
            // Oldest row (index 0) on top. Texture y is bottom-up, so top = highest y.
            int rowY = (rowCount - 1 - r) * (thumb + InputRowGap);
            int cursorX = 0;
            var ins = rowInputs[r];
            for (int i = 0; i < ins.Count; i++)
            {
                comp.BlitImageFitted(ins[i], cursorX, rowY, thumb, thumb, "fit", 1f);
                cursorX += thumb + InputThumbGap;
            }
            if (rowOutput[r] != null)
            {
                if (ins.Count > 0)
                {
                    // Reclaim the trailing gap after the last input, then: gap, divider, gap.
                    cursorX -= InputThumbGap;
                    cursorX += OutputSeparatorGap;
                    int dividerX0 = cursorX;
                    for (int x = dividerX0; x < dividerX0 + OutputDividerWidth && x < comp.width; x++)
                        for (int y = rowY; y < rowY + thumb && y < comp.height; y++)
                            comp.SetPixel(x, y, OutputDividerColor);
                    cursorX += OutputDividerWidth + OutputSeparatorGap;
                }
                comp.BlitImageFitted(rowOutput[r], cursorX, rowY, thumb, thumb, "fit", 1f);
            }
        }

        comp.Apply();

        // Destroy temps.
        for (int r = 0; r < rowCount; r++)
        {
            foreach (var t in rowInputs[r]) UnityEngine.Object.Destroy(t);
            if (rowOutput[r] != null) UnityEngine.Object.Destroy(rowOutput[r]);
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
        _lastHistory = rows;
        UpdateVisuals();
    }

    // True when the new history is the same as what we last composited - compared by the
    // underlying (inputs, output) references so we skip the expensive re-decode/re-blit.
    private bool HistoryMatchesCache(List<ImageHistoryRow> rows)
    {
        if (_lastHistory == null || _lastHistory.Count != rows.Count) return false;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!ReferenceEquals(_lastHistory[i].inputs, rows[i].inputs)) return false;
            if (!ReferenceEquals(_lastHistory[i].output, rows[i].output)) return false;
        }
        return true;
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
