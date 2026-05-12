using System;
using System.Collections.Generic;
using System.IO;
using B83.Win32;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reusable image-attachment zone for any chat-style input field. Hosts:
///   - A list of attachments (Texture2D thumb + raw byte[] PNG to send).
///   - A horizontal thumbnail strip (built into a caller-provided RectTransform).
///   - A drag-and-drop intercept that claims any file dropped over a caller-provided rect.
///   - A Ctrl+V handler that runs <c>utils\RTClip.exe</c> to pull an image off the
///     Windows clipboard, fired only while a caller-provided TMP_InputField is focused.
///
/// Hosts (e.g. AIChatPanel, AdventureInput) call <see cref="Initialize"/> once, then
/// subscribe to <see cref="OnAttachmentsChanged"/> to re-layout their own surrounding UI
/// (e.g. grow a footer when attachments appear). Hosts also call
/// <see cref="GetAttachmentBytes"/> at submit time and then <see cref="ClearAttachments"/>.
/// </summary>
public class ChatImageAttachmentZone : MonoBehaviour
{
    // Monotonic id source: stable across remove/add so a host can correlate an
    // async caption result back to the right attachment even after the user has
    // X'd earlier ones and the visible index has shifted.
    private static int _nextAttachmentId = 1;

    private class ChatAttachment
    {
        public int id;
        public Texture2D thumb;
        public byte[] pngBytes;
        public int width;
        public int height;
        // Two captions returned by the host's vision pass: a one-line summary
        // for cramped UI labels, and a detailed paragraph for the LLM payload.
        // Either or both may be null if no vision LLM was available.
        public string captionShort;
        public string captionLong;
        public bool captionInFlight;
    }

    /// <summary>
    /// Public read-only snapshot of an attachment, exposing the bytes plus the
    /// pre-computed metadata (dimensions + short/long captions) the host needs
    /// at send time to decide whether to ship raw base64 or a caption-only
    /// metadata block.
    /// </summary>
    public struct AttachmentInfo
    {
        public int id;                  // stable id; survives remove of earlier attachments
        public byte[] bytes;
        public int width;
        public int height;
        public string captionShort;     // null/empty if no caption is available
        public string captionLong;      // null/empty if no caption is available
    }

    public event Action OnAttachmentsChanged;
    /// <summary>Fires once per AddAttachment with the new attachment's info (incl. stable id).</summary>
    public event Action<AttachmentInfo> OnAttachmentAdded;
    /// <summary>
    /// Fires when an attachment is removed (typically: user clicked the X) while its
    /// caption was still in flight. The id matches <see cref="AttachmentInfo.id"/> from
    /// the OnAttachmentAdded event. Hosts use this to cancel the pending vision LLM
    /// work tied to that attachment - releasing the LLM busy slot immediately rather
    /// than waiting for the request to finish (which on a hung local model is "never").
    /// </summary>
    public event Action<int> OnCaptionCancelled;

    private readonly List<ChatAttachment> _attachments = new List<ChatAttachment>();

    // Configuration set in Initialize.
    private RectTransform _dropTargetRect;
    private RectTransform _stripContainer;
    private TMP_InputField _pasteField;
    private TMP_FontAsset _font;
    private int _maxAttachments = 8;
    private float _stripHeight = 70f;
    // Optional callback the host wires up to return the current max-edge cap
    // (in pixels). Queried per AddAttachment so a runtime settings change
    // takes effect on the next drop without re-initializing the zone. Returns
    // 0 / negative to disable resizing.
    private Func<int> _maxEdgeProvider;

    // Per-instance drop claimant lambda; kept around so we can deregister on destroy.
    private Func<List<string>, POINT, bool> _claimDelegate;

    public bool HasAttachments => _attachments.Count > 0;
    public int Count => _attachments.Count;

    /// <summary>
    /// Returns the raw PNG bytes for each currently-attached image. The caller is
    /// expected to base64-encode them and either push them through
    /// <c>GPTPromptManager.AddPendingImage</c> or attach them directly to a
    /// <c>GTPChatLine</c>. Does NOT clear; call <see cref="ClearAttachments"/> after.
    /// </summary>
    public IReadOnlyList<byte[]> GetAttachmentBytes()
    {
        var list = new List<byte[]>(_attachments.Count);
        foreach (var att in _attachments)
        {
            if (att != null && att.pngBytes != null)
                list.Add(att.pngBytes);
        }
        return list;
    }

    /// <summary>
    /// Returns full per-attachment info (bytes + dimensions + caption) in the
    /// same order they were attached. Used by the host at send time so it can
    /// build a metadata block instead of (or alongside) shipping base64.
    /// </summary>
    public IReadOnlyList<AttachmentInfo> GetAttachmentInfo()
    {
        var list = new List<AttachmentInfo>(_attachments.Count);
        foreach (var att in _attachments)
        {
            if (att == null || att.pngBytes == null) continue;
            list.Add(new AttachmentInfo
            {
                id = att.id,
                bytes = att.pngBytes,
                width = att.width,
                height = att.height,
                captionShort = att.captionShort,
                captionLong = att.captionLong,
            });
        }
        return list;
    }

    private ChatAttachment FindById(int id)
    {
        foreach (var att in _attachments)
            if (att != null && att.id == id) return att;
        return null;
    }

    /// <summary>
    /// How many attachments are still waiting on a caption result. The host
    /// uses this to gate the Send button so a user message can't fly out
    /// before its attachments have been described.
    /// </summary>
    public int CountInFlightCaptions()
    {
        int n = 0;
        foreach (var att in _attachments)
            if (att != null && att.captionInFlight) n++;
        return n;
    }

    /// <summary>
    /// Host writes the captioning result back here, keyed by the stable
    /// <see cref="AttachmentInfo.id"/>. Pass null/empty for both fields to
    /// clear the in-flight flag without recording a caption (e.g. when no
    /// vision LLM is available). If the attachment was removed before the
    /// caption arrived, this is a silent no-op. Always raises
    /// <see cref="OnAttachmentsChanged"/> when the attachment is still around
    /// so the host re-evaluates Send button state.
    /// </summary>
    public void SetCaption(int id, string captionShort, string captionLong)
    {
        var att = FindById(id);
        if (att == null) return;
        att.captionShort = string.IsNullOrEmpty(captionShort) ? null : captionShort;
        att.captionLong  = string.IsNullOrEmpty(captionLong)  ? null : captionLong;
        att.captionInFlight = false;
        OnAttachmentsChanged?.Invoke();
    }

    public void ClearAttachments()
    {
        foreach (var att in _attachments)
        {
            if (att != null && att.thumb != null)
                UnityEngine.Object.Destroy(att.thumb);
        }
        _attachments.Clear();
        Refresh();
    }

    /// <summary>
    /// Wire this zone to the host's UI. Must be called once before any drops/pastes are
    /// expected. Drop hit-testing uses <paramref name="dropTarget"/>; thumbnails are
    /// built inside <paramref name="stripContainer"/>; Ctrl+V only fires while
    /// <paramref name="pasteField"/> is focused.
    /// </summary>
    public void Initialize(
        RectTransform dropTarget,
        RectTransform stripContainer,
        TMP_InputField pasteField,
        TMP_FontAsset font,
        int maxAttachments = 8,
        float stripHeight = 70f,
        Func<int> maxEdgeProvider = null)
    {
        _dropTargetRect = dropTarget;
        _stripContainer = stripContainer;
        _pasteField = pasteField;
        _font = font;
        _maxAttachments = Mathf.Max(1, maxAttachments);
        _stripHeight = Mathf.Max(16f, stripHeight);
        _maxEdgeProvider = maxEdgeProvider;

        // Register with the global drop hook. Multiple zones can register independently;
        // first one whose hit-test passes wins.
        if (_claimDelegate == null)
        {
            _claimDelegate = TryClaimDrop;
            DragAndDropHandler.ClaimHandlers.Add(_claimDelegate);
        }

        // Initial layout pass so the strip starts at zero height when empty.
        Refresh();
    }

    private void OnDestroy()
    {
        if (_claimDelegate != null)
        {
            DragAndDropHandler.ClaimHandlers.Remove(_claimDelegate);
            _claimDelegate = null;
        }

        // Free any attachment textures we still own.
        foreach (var att in _attachments)
        {
            if (att != null && att.thumb != null)
                UnityEngine.Object.Destroy(att.thumb);
        }
        _attachments.Clear();
    }

    private void Update()
    {
        // Ctrl+V paste -> if Windows clipboard has an image, attach it. We let TMP_InputField
        // do its own text paste in parallel; if the clipboard had only an image then no text
        // is pasted and only the attachment is added.
        if (_pasteField != null && _pasteField.isFocused
            && Input.GetKeyDown(KeyCode.V)
            && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            TryPasteImageFromClipboard();
        }
    }

    /// <summary>
    /// Add an image (raw PNG/JPEG/BMP bytes) as an attachment. Decodes into a Texture2D
    /// thumbnail and re-encodes to PNG so the bytes we ship match the
    /// <c>data:image/png;base64</c> mime advertised in the multimodal JSON builders.
    /// </summary>
    public void AddAttachment(byte[] imgBytes)
    {
        if (imgBytes == null || imgBytes.Length == 0) return;

        if (_attachments.Count >= _maxAttachments)
        {
            RTQuickMessageManager.Get().ShowMessage($"Max {_maxAttachments} attachments per message");
            return;
        }

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(imgBytes))
        {
            UnityEngine.Object.Destroy(tex);
            RTQuickMessageManager.Get().ShowMessage("Could not decode dropped image");
            return;
        }

        // Auto-downscale oversized drops. Huge source images (4K, 6K, mobile photos
        // at 8K+) blow up every downstream cost - PNG re-encode time, captioning
        // payload, image_to_image source bytes, hover-tooltip latency. A bilinear
        // GPU blit to fit-within-square keeps aspect ratio intact and runs in
        // a millisecond or two regardless of source size.
        int maxEdge = _maxEdgeProvider != null ? _maxEdgeProvider() : 0;
        if (maxEdge > 0 && (tex.width > maxEdge || tex.height > maxEdge))
        {
            int origW = tex.width;
            int origH = tex.height;
            var scaled = ScaleToFit(tex, maxEdge);
            if (scaled != null)
            {
                UnityEngine.Object.Destroy(tex);
                tex = scaled;
                RTQuickMessageManager.Get().ShowMessage(
                    $"Resized attachment {origW}x{origH} -> {tex.width}x{tex.height}");
            }
        }

        byte[] pngBytes;
        try { pngBytes = tex.EncodeToPNG(); }
        catch (Exception ex)
        {
            UnityEngine.Object.Destroy(tex);
            Debug.LogError("ChatImageAttachmentZone: failed to re-encode attachment as PNG: " + ex);
            return;
        }

        // captionInFlight starts true: the host wires OnAttachmentAdded to a
        // caption job and calls SetCaption when it returns (or returns null
        // when no vision LLM is available). Send is gated until this clears.
        var newAttachment = new ChatAttachment
        {
            id = _nextAttachmentId++,
            thumb = tex,
            pngBytes = pngBytes,
            width = tex.width,
            height = tex.height,
            captionShort = null,
            captionLong = null,
            captionInFlight = true,
        };
        _attachments.Add(newAttachment);
        Refresh();
        RTQuickMessageManager.Get().ShowMessage($"Attached image ({_attachments.Count} pending)");
        OnAttachmentAdded?.Invoke(new AttachmentInfo
        {
            id = newAttachment.id,
            bytes = newAttachment.pngBytes,
            width = newAttachment.width,
            height = newAttachment.height,
            captionShort = null,
            captionLong = null,
        });
    }

    private void RemoveAttachment(int idx)
    {
        if (idx < 0 || idx >= _attachments.Count) return;
        var att = _attachments[idx];
        bool wasInFlight = att != null && att.captionInFlight;
        int capturedId = att != null ? att.id : -1;
        if (att != null && att.thumb != null)
            UnityEngine.Object.Destroy(att.thumb);
        _attachments.RemoveAt(idx);
        Refresh();
        // Notify host AFTER list/UI is consistent so a cancel handler can safely
        // re-query state. Only fire if the caption was still pending - otherwise
        // there's nothing for the host to cancel.
        if (wasInFlight && capturedId >= 0)
        {
            try { OnCaptionCancelled?.Invoke(capturedId); }
            catch (Exception ex) { Debug.LogWarning("ChatImageAttachmentZone: OnCaptionCancelled handler threw: " + ex.Message); }
        }
    }

    /// <summary>
    /// Rebuild the strip's child thumbnails from the current attachment list. Strip
    /// container's height toggles between 0 and <see cref="_stripHeight"/> and its
    /// GameObject is hidden when there are no attachments. Always raises
    /// <see cref="OnAttachmentsChanged"/> after layout updates.
    /// </summary>
    private void Refresh()
    {
        if (_stripContainer != null)
        {
            for (int i = _stripContainer.childCount - 1; i >= 0; i--)
                Destroy(_stripContainer.GetChild(i).gameObject);

            bool hasAttachments = _attachments.Count > 0;
            _stripContainer.gameObject.SetActive(hasAttachments);
            _stripContainer.sizeDelta = new Vector2(_stripContainer.sizeDelta.x,
                hasAttachments ? _stripHeight : 0f);

            if (hasAttachments)
            {
                for (int i = 0; i < _attachments.Count; i++)
                {
                    int capturedIdx = i;
                    CreateThumb(_attachments[i], capturedIdx);
                }
            }
        }

        OnAttachmentsChanged?.Invoke();
    }

    private void CreateThumb(ChatAttachment att, int idx)
    {
        float sz = _stripHeight - 8f;

        var item = new GameObject("Attachment_" + idx);
        item.transform.SetParent(_stripContainer, false);
        var le = item.AddComponent<LayoutElement>();
        le.preferredWidth = sz;
        le.preferredHeight = sz;
        le.minWidth = sz;
        le.minHeight = sz;

        var bg = item.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.95f, 0.97f, 1f);

        var thumbGo = new GameObject("Thumb");
        thumbGo.transform.SetParent(item.transform, false);
        var thumbRt = thumbGo.AddComponent<RectTransform>();
        thumbRt.anchorMin = Vector2.zero;
        thumbRt.anchorMax = Vector2.one;
        thumbRt.offsetMin = new Vector2(2, 2);
        thumbRt.offsetMax = new Vector2(-2, -2);
        var raw = thumbGo.AddComponent<RawImage>();
        raw.texture = att.thumb;
        raw.raycastTarget = false;
        // Dim the thumbnail while a caption is being generated, so it's obvious
        // why Send is greyed out. Reset to full opacity once the caption arrives.
        raw.color = att.captionInFlight
            ? new Color(1f, 1f, 1f, 0.55f)
            : Color.white;

        var x = new GameObject("Remove");
        x.transform.SetParent(item.transform, false);
        var xRt = x.AddComponent<RectTransform>();
        xRt.anchorMin = new Vector2(1, 1);
        xRt.anchorMax = new Vector2(1, 1);
        xRt.pivot = new Vector2(1, 1);
        xRt.sizeDelta = new Vector2(18, 18);
        xRt.anchoredPosition = new Vector2(-1, -1);
        var xImg = x.AddComponent<Image>();
        xImg.color = new Color(0.55f, 0.25f, 0.25f, 0.95f);
        var xBtn = x.AddComponent<Button>();
        xBtn.targetGraphic = xImg;
        xBtn.onClick.AddListener(() => RemoveAttachment(idx));

        var xTxtGo = new GameObject("X");
        xTxtGo.transform.SetParent(x.transform, false);
        var xTxtRt = xTxtGo.AddComponent<RectTransform>();
        xTxtRt.anchorMin = Vector2.zero;
        xTxtRt.anchorMax = Vector2.one;
        xTxtRt.offsetMin = Vector2.zero;
        xTxtRt.offsetMax = Vector2.zero;
        var xTxt = xTxtGo.AddComponent<TextMeshProUGUI>();
        xTxt.text = "X";
        if (_font != null) xTxt.font = _font;
        xTxt.fontSize = 12;
        xTxt.fontStyle = FontStyles.Bold;
        xTxt.color = Color.white;
        xTxt.alignment = TextAlignmentOptions.Center;
        xTxt.raycastTarget = false;
    }

    // Pixel-space padding around the drop target's projected rect. Generous enough
    // to absorb DPI rounding error, the resize grip and footer drag bar that sit
    // just outside the parent's sizeDelta, and small mismatches between where Win32
    // says the drop landed and where Unity's screen origin is.
    private const float DropTargetPaddingPx = 40f;

    /// <summary>
    /// True if <paramref name="unityScreenPos"/> is "on" our drop target. Computes
    /// the drop target's actual screen-space rect from its world corners (works for
    /// ScreenSpaceOverlay canvases, where world corners ARE pixel coords), then pads
    /// it. RectTransformUtility.RectangleContainsScreenPoint is bypassed because it
    /// has been observed to mis-classify drops near the edges of the panel under
    /// some DPI scales.
    /// </summary>
    private bool IsPointOverDropTarget(Vector2 unityScreenPos)
    {
        if (_dropTargetRect == null) return false;
        var corners = new Vector3[4];
        _dropTargetRect.GetWorldCorners(corners);
        float minX = corners[0].x, minY = corners[0].y, maxX = corners[0].x, maxY = corners[0].y;
        for (int i = 1; i < 4; i++)
        {
            if (corners[i].x < minX) minX = corners[i].x;
            if (corners[i].y < minY) minY = corners[i].y;
            if (corners[i].x > maxX) maxX = corners[i].x;
            if (corners[i].y > maxY) maxY = corners[i].y;
        }
        return unityScreenPos.x >= minX - DropTargetPaddingPx
            && unityScreenPos.x <= maxX + DropTargetPaddingPx
            && unityScreenPos.y >= minY - DropTargetPaddingPx
            && unityScreenPos.y <= maxY + DropTargetPaddingPx;
    }

    /// <summary>
    /// DragAndDropHandler claim callback. Returns true if the drop landed over our drop
    /// target rect (and was therefore consumed as attachments) - false to let the next
    /// claimant or the default "open as new pic" handler run.
    /// </summary>
    private bool TryClaimDrop(List<string> files, POINT screenPos)
    {
        if (_dropTargetRect == null || !gameObject.activeInHierarchy) return false;

        // Win32 POINT is top-left origin; Unity screen coords are bottom-left origin.
        Vector2 unityScreenPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (!IsPointOverDropTarget(unityScreenPos))
            return false;

        bool addedAny = false;
        foreach (var f in files)
        {
            string ext;
            try { ext = new FileInfo(f).Extension.ToLowerInvariant(); }
            catch { continue; }

            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp")
                continue;
            if (!File.Exists(f)) continue;

            try
            {
                byte[] bytes = File.ReadAllBytes(f);
                AddAttachment(bytes);
                addedAny = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("ChatImageAttachmentZone: failed to read dropped file " + f + ": " + ex);
            }
        }

        // Even if no images were valid, return true to indicate "drop was over us" so the
        // default handler doesn't also try to load them as new pics.
        return addedAny || files.Count > 0;
    }

    /// <summary>
    /// Run utils\RTClip.exe to convert any image on the Windows clipboard into a temp
    /// PNG, then attach those bytes. Mirrors <c>PicMain.LoadImageFromClipboard</c>.
    /// Synchronous, but the helper exits within a few ms.
    /// </summary>
    private void TryPasteImageFromClipboard()
    {
        try
        {
            string root = Path.GetDirectoryName(Application.dataPath.Replace('/', '\\'));
            if (string.IsNullOrEmpty(root)) return;
            string exe = Path.Combine(root, "utils", "RTClip.exe");
            string tmpPng = Path.Combine(root, "winclip_image.png");

            if (!File.Exists(exe))
            {
                // Quietly skip - this build may not ship RTClip.exe.
                return;
            }

            if (File.Exists(tmpPng))
            {
                try { File.Delete(tmpPng); } catch { /* ignore */ }
            }

            var psi = new System.Diagnostics.ProcessStartInfo(exe, "")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return;
            proc.WaitForExit();
            proc.Close();

            if (File.Exists(tmpPng))
            {
                byte[] bytes = File.ReadAllBytes(tmpPng);
                AddAttachment(bytes);
                try { File.Delete(tmpPng); } catch { /* ignore */ }
            }
            // else: clipboard had no image; let TMP's text paste run as normal.
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ChatImageAttachmentZone: clipboard image paste failed: " + ex.Message);
        }
    }

    /// <summary>
    /// GPU bilinear downscale of <paramref name="src"/> so the longer edge fits
    /// inside <paramref name="maxEdge"/> pixels, preserving aspect ratio. Returns
    /// a freshly allocated Texture2D the caller takes ownership of (must be
    /// Destroy()ed). Returns null on failure or when no scaling is needed.
    /// </summary>
    private static Texture2D ScaleToFit(Texture2D src, int maxEdge)
    {
        if (src == null || maxEdge <= 0) return null;
        int sw = src.width, sh = src.height;
        if (sw <= maxEdge && sh <= maxEdge) return null;

        float scale = Mathf.Min((float)maxEdge / sw, (float)maxEdge / sh);
        int nw = Mathf.Max(1, Mathf.RoundToInt(sw * scale));
        int nh = Mathf.Max(1, Mathf.RoundToInt(sh * scale));

        RenderTexture rt = null;
        Texture2D dst = null;
        var prevActive = RenderTexture.active;
        var prevFilter = src.filterMode;
        try
        {
            // Bilinear filter on the source so the GPU resampler produces a smooth
            // mip-free downscale. Reset afterwards so we don't mutate the source's
            // long-lived filterMode for any other code path that might display it.
            src.filterMode = FilterMode.Bilinear;
            rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            dst = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            dst.Apply();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ChatImageAttachmentZone: ScaleToFit failed: " + ex.Message);
            if (dst != null) UnityEngine.Object.Destroy(dst);
            dst = null;
        }
        finally
        {
            RenderTexture.active = prevActive;
            src.filterMode = prevFilter;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
        }
        return dst;
    }
}
