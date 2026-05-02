using System;
using System.Collections.Generic;
using System.IO;
using B83.Win32;
using TMPro;
using UnityEngine;
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
    private class ChatAttachment
    {
        public Texture2D thumb;
        public byte[] pngBytes;
    }

    public event Action OnAttachmentsChanged;

    private readonly List<ChatAttachment> _attachments = new List<ChatAttachment>();

    // Configuration set in Initialize.
    private RectTransform _dropTargetRect;
    private RectTransform _stripContainer;
    private TMP_InputField _pasteField;
    private TMP_FontAsset _font;
    private int _maxAttachments = 8;
    private float _stripHeight = 70f;

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
        float stripHeight = 70f)
    {
        _dropTargetRect = dropTarget;
        _stripContainer = stripContainer;
        _pasteField = pasteField;
        _font = font;
        _maxAttachments = Mathf.Max(1, maxAttachments);
        _stripHeight = Mathf.Max(16f, stripHeight);

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

        byte[] pngBytes;
        try { pngBytes = tex.EncodeToPNG(); }
        catch (Exception ex)
        {
            UnityEngine.Object.Destroy(tex);
            Debug.LogError("ChatImageAttachmentZone: failed to re-encode attachment as PNG: " + ex);
            return;
        }

        _attachments.Add(new ChatAttachment { thumb = tex, pngBytes = pngBytes });
        Refresh();
        RTQuickMessageManager.Get().ShowMessage($"Attached image ({_attachments.Count} pending)");
    }

    private void RemoveAttachment(int idx)
    {
        if (idx < 0 || idx >= _attachments.Count) return;
        if (_attachments[idx].thumb != null)
            UnityEngine.Object.Destroy(_attachments[idx].thumb);
        _attachments.RemoveAt(idx);
        Refresh();
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
        if (!RectTransformUtility.RectangleContainsScreenPoint(_dropTargetRect, unityScreenPos, null))
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
}
