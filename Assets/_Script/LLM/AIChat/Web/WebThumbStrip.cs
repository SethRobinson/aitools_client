using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AITools.AIChat.Web
{
    public enum WebThumbVerdict
    {
        /// <summary>Downloaded, vision check not finished yet.</summary>
        Pending,
        /// <summary>Vision check said SUITABLE (or the image entered chat).</summary>
        Suitable,
        /// <summary>Rejected: vision check UNSUITABLE, too narrow, reused, etc.</summary>
        Unsuitable,
        /// <summary>Accepted without a verdict (no vision LLM, timeout, verify="false").</summary>
        Unverified
    }

    /// <summary>
    /// One thumbnail in a <see cref="WebThumbStrip"/>: the file it came from, what the
    /// vision check said about it, and (when it exists) the world Pic that holds it.
    /// </summary>
    public sealed class WebThumbEntry
    {
        /// <summary>Full-resolution file on disk (kept for the session so a click can load it).</summary>
        public string FilePath;
        /// <summary>Short label shown on the cell, e.g. the download ordinal "3".</summary>
        public string Label;
        /// <summary>Hover text: what the image is / where it came from.</summary>
        public string Title;
        public int Width;
        public int Height;
        public WebThumbVerdict Verdict = WebThumbVerdict.Pending;
        public string Reason;
        /// <summary>The chat/world Pic that already holds this media (accepted images and clips).</summary>
        public PicMain LinkedPic;
        /// <summary>A Pic created by clicking this thumbnail, so a second click focuses instead of duplicating.</summary>
        public PicMain SpawnedPic;

        internal Texture2D Thumb;
        internal RawImage Raw;
        internal Image Badge;
        internal TextMeshProUGUI BadgeText;
        internal Image Frame;

        public string VerdictWord
        {
            get
            {
                switch (Verdict)
                {
                    case WebThumbVerdict.Suitable: return "SUITABLE";
                    case WebThumbVerdict.Unsuitable: return "UNSUITABLE";
                    case WebThumbVerdict.Unverified: return "unverified";
                    default: return "checking...";
                }
            }
        }
    }

    /// <summary>
    /// A wrapping row of small thumbnails under a Web trace bubble: every image the web
    /// fetch downloaded and showed to the vision LLM (accepted AND rejected), each with a
    /// verdict badge. Hovering shows the verdict/reason in a hint line; clicking hands the
    /// entry to the host (which focuses the existing world Pic or loads the file into a new
    /// one). Rejected candidates used to be deleted silently, so the user never saw what the
    /// model looked at and could not rescue a wrongly rejected photo.
    /// Thumbnail textures are downscaled copies owned by this component and destroyed with it.
    /// </summary>
    public sealed class WebThumbStrip : MonoBehaviour
    {
        public const int ThumbMaxSide = 128;
        private const float CellSize = 72f;
        private const float CellSpacing = 4f;

        private static readonly Color BadgePending = new Color(0.55f, 0.55f, 0.60f, 0.95f);
        private static readonly Color BadgeSuitable = new Color(0.16f, 0.60f, 0.26f, 0.95f);
        private static readonly Color BadgeUnsuitable = new Color(0.78f, 0.20f, 0.20f, 0.95f);
        private static readonly Color BadgeUnverified = new Color(0.80f, 0.55f, 0.10f, 0.95f);
        private static readonly Color CellBg = new Color(0.90f, 0.91f, 0.94f, 1f);
        private static readonly Color CellBgRejected = new Color(0.94f, 0.88f, 0.88f, 1f);

        private readonly List<WebThumbEntry> _entries = new List<WebThumbEntry>();
        private Transform _grid;
        private TextMeshProUGUI _hint;
        private TMP_FontAsset _font;
        private float _fontSize;
        private Action<WebThumbEntry> _onClick;
        private string _idleHint = "Images the vision check looked at. Hover for the verdict, click to put one on the canvas.";

        public IReadOnlyList<WebThumbEntry> Entries => _entries;
        public int Count => _entries.Count;

        /// <summary>
        /// Build the strip as the last child of <paramref name="bubble"/> (a VerticalLayoutGroup
        /// bubble from AIChatPanel.AppendBubble).
        /// </summary>
        public static WebThumbStrip Create(Transform bubble, TMP_FontAsset font, float fontSize, Action<WebThumbEntry> onClick)
        {
            var go = new GameObject("WebThumbs");
            go.transform.SetParent(bubble, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 4, 2);
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = CellSize + 20f;
            le.preferredHeight = -1f;
            le.flexibleHeight = -1f;

            var strip = go.AddComponent<WebThumbStrip>();
            strip._font = font;
            strip._fontSize = fontSize;
            strip._onClick = onClick;

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(go.transform, false);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellSize, CellSize);
            grid.spacing = new Vector2(CellSpacing, CellSpacing);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            strip._grid = gridGo.transform;

            var hintGo = new GameObject("Hint");
            hintGo.transform.SetParent(go.transform, false);
            var hintLE = hintGo.AddComponent<LayoutElement>();
            hintLE.minHeight = 12f;
            hintLE.preferredHeight = -1f;
            hintLE.flexibleHeight = -1f;
            var hint = hintGo.AddComponent<TextMeshProUGUI>();
            if (font != null) hint.font = font;
            hint.fontSize = Mathf.Max(9f, fontSize - 2f);
            hint.color = new Color(0.30f, 0.32f, 0.40f);
            hint.richText = false;
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.raycastTarget = false;
            hint.text = strip._idleHint;
            strip._hint = hint;
            return strip;
        }

        /// <summary>
        /// Add a thumbnail decoded from <paramref name="imageBytes"/> (PNG/JPEG). Returns null
        /// when the bytes cannot be decoded; the strip then shows nothing for that download.
        /// </summary>
        public WebThumbEntry AddThumb(byte[] imageBytes, string filePath, string label, string title)
        {
            Texture2D thumb = BuildThumbnail(imageBytes, out int w, out int h);
            if (thumb == null) return null;

            var e = new WebThumbEntry
            {
                FilePath = filePath,
                Label = label ?? "",
                Title = title ?? "",
                Width = w,
                Height = h,
                Thumb = thumb
            };
            _entries.Add(e);
            BuildCell(e);
            RequestLayout();
            return e;
        }

        public void SetVerdict(WebThumbEntry e, WebThumbVerdict verdict, string reason)
        {
            if (e == null) return;
            e.Verdict = verdict;
            e.Reason = reason;
            ApplyVerdictVisuals(e);
        }

        public void SetLinkedPic(WebThumbEntry e, PicMain pic)
        {
            if (e == null) return;
            e.LinkedPic = pic;
        }

        private void BuildCell(WebThumbEntry e)
        {
            var cell = new GameObject("Thumb_" + e.Label);
            cell.transform.SetParent(_grid, false);
            var bg = cell.AddComponent<Image>();
            bg.color = CellBg;
            e.Frame = bg;

            var btn = cell.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.82f, 0.86f, 0.98f, 1f);
            colors.pressedColor = new Color(0.70f, 0.76f, 0.95f, 1f);
            btn.colors = colors;
            var captured = e;
            btn.onClick.AddListener(() => { try { _onClick?.Invoke(captured); } catch (Exception ex) { Debug.LogWarning("WebThumbStrip click: " + ex.Message); } });

            var hover = cell.AddComponent<HoverRelay>();
            hover.strip = this;
            hover.entry = e;

            // Aspect-fitted picture inside the square cell.
            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(cell.transform, false);
            var imgRt = imgGo.AddComponent<RectTransform>();
            imgRt.anchorMin = new Vector2(0.5f, 0.5f);
            imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.pivot = new Vector2(0.5f, 0.5f);
            float inner = CellSize - 4f;
            float aspect = e.Height > 0 ? (float)e.Width / e.Height : 1f;
            float fitW = inner, fitH = inner / Mathf.Max(0.001f, aspect);
            if (fitH > inner) { fitH = inner; fitW = inner * aspect; }
            imgRt.sizeDelta = new Vector2(fitW, fitH);
            imgRt.anchoredPosition = Vector2.zero;
            var raw = imgGo.AddComponent<RawImage>();
            raw.texture = e.Thumb;
            raw.raycastTarget = false;
            e.Raw = raw;

            // Verdict badge, top-left corner.
            var badgeGo = new GameObject("Badge");
            badgeGo.transform.SetParent(cell.transform, false);
            var badgeRt = badgeGo.AddComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.sizeDelta = new Vector2(16f, 16f);
            badgeRt.anchoredPosition = new Vector2(1f, -1f);
            var badge = badgeGo.AddComponent<Image>();
            badge.raycastTarget = false;
            e.Badge = badge;

            var badgeTxtGo = new GameObject("Text");
            badgeTxtGo.transform.SetParent(badgeGo.transform, false);
            var btRt = badgeTxtGo.AddComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;
            var badgeTxt = badgeTxtGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) badgeTxt.font = _font;
            badgeTxt.fontSize = 11f;
            badgeTxt.fontStyle = FontStyles.Bold;
            badgeTxt.color = Color.white;
            badgeTxt.alignment = TextAlignmentOptions.Center;
            badgeTxt.raycastTarget = false;
            e.BadgeText = badgeTxt;

            // Ordinal label, bottom-right corner.
            if (!string.IsNullOrEmpty(e.Label))
            {
                var lblGo = new GameObject("Label");
                lblGo.transform.SetParent(cell.transform, false);
                var lblRt = lblGo.AddComponent<RectTransform>();
                lblRt.anchorMin = new Vector2(1f, 0f);
                lblRt.anchorMax = new Vector2(1f, 0f);
                lblRt.pivot = new Vector2(1f, 0f);
                lblRt.sizeDelta = new Vector2(26f, 13f);
                lblRt.anchoredPosition = new Vector2(-1f, 1f);
                var lblBg = lblGo.AddComponent<Image>();
                lblBg.color = new Color(0f, 0f, 0f, 0.55f);
                lblBg.raycastTarget = false;
                var lblTxtGo = new GameObject("Text");
                lblTxtGo.transform.SetParent(lblGo.transform, false);
                var ltRt = lblTxtGo.AddComponent<RectTransform>();
                ltRt.anchorMin = Vector2.zero;
                ltRt.anchorMax = Vector2.one;
                ltRt.offsetMin = Vector2.zero;
                ltRt.offsetMax = Vector2.zero;
                var lblTxt = lblTxtGo.AddComponent<TextMeshProUGUI>();
                if (_font != null) lblTxt.font = _font;
                lblTxt.fontSize = 9f;
                lblTxt.color = Color.white;
                lblTxt.alignment = TextAlignmentOptions.Center;
                lblTxt.raycastTarget = false;
                lblTxt.text = e.Label;
            }

            ApplyVerdictVisuals(e);
        }

        private void ApplyVerdictVisuals(WebThumbEntry e)
        {
            if (e.Badge == null) return;
            switch (e.Verdict)
            {
                case WebThumbVerdict.Suitable:
                    e.Badge.color = BadgeSuitable; e.BadgeText.text = "✓";
                    if (e.Frame != null) e.Frame.color = CellBg;
                    if (e.Raw != null) e.Raw.color = Color.white;
                    break;
                case WebThumbVerdict.Unsuitable:
                    e.Badge.color = BadgeUnsuitable; e.BadgeText.text = "X";
                    if (e.Frame != null) e.Frame.color = CellBgRejected;
                    if (e.Raw != null) e.Raw.color = new Color(1f, 1f, 1f, 0.75f);
                    break;
                case WebThumbVerdict.Unverified:
                    e.Badge.color = BadgeUnverified; e.BadgeText.text = "?";
                    if (e.Frame != null) e.Frame.color = CellBg;
                    if (e.Raw != null) e.Raw.color = Color.white;
                    break;
                default:
                    e.Badge.color = BadgePending; e.BadgeText.text = "-";
                    if (e.Frame != null) e.Frame.color = CellBg;
                    if (e.Raw != null) e.Raw.color = new Color(1f, 1f, 1f, 0.6f);
                    break;
            }
            if (_hoverEntry == e) ShowHint(e);
        }

        private WebThumbEntry _hoverEntry;

        internal void OnCellEnter(WebThumbEntry e)
        {
            _hoverEntry = e;
            ShowHint(e);
        }

        internal void OnCellExit(WebThumbEntry e)
        {
            if (_hoverEntry == e) _hoverEntry = null;
            if (_hint != null) _hint.text = _idleHint;
        }

        private void ShowHint(WebThumbEntry e)
        {
            if (_hint == null || e == null) return;
            string s = (string.IsNullOrEmpty(e.Label) ? "" : "#" + e.Label + " ") + e.VerdictWord;
            if (!string.IsNullOrEmpty(e.Reason)) s += " - " + e.Reason;
            if (e.Width > 0 && e.Height > 0) s += " (" + e.Width + "x" + e.Height + ")";
            if (!string.IsNullOrEmpty(e.Title)) s += "\n" + e.Title;
            bool hasPic = (e.LinkedPic != null && e.LinkedPic.gameObject != null) || (e.SpawnedPic != null && e.SpawnedPic.gameObject != null);
            s += hasPic ? "\nClick to show it on the canvas." : "\nClick to add it to the canvas as a new image.";
            _hint.text = s;
            RequestLayout();
        }

        private void RequestLayout()
        {
            var rt = transform as RectTransform;
            if (rt != null) LayoutRebuilder.MarkLayoutForRebuild(rt);
            var parent = transform.parent as RectTransform;
            if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        /// <summary>
        /// Decode the image and shrink it to at most <see cref="ThumbMaxSide"/> pixels on the
        /// long side. The full-size decode is destroyed immediately: web images are capped at
        /// 2048 px, and a dozen of those per fetch would be a lot of VRAM for 72 px cells.
        /// </summary>
        private static Texture2D BuildThumbnail(byte[] bytes, out int width, out int height)
        {
            width = 0; height = 0;
            if (bytes == null || bytes.Length == 0) return null;
            Texture2D full = null;
            try
            {
                full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!full.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(full);
                    return null;
                }
                width = full.width;
                height = full.height;
                int longest = Mathf.Max(width, height);
                if (longest <= ThumbMaxSide)
                {
                    full.filterMode = FilterMode.Bilinear;
                    return full;
                }
                float scale = ThumbMaxSide / (float)longest;
                int tw = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                int th = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(full, rt);
                    RenderTexture.active = rt;
                    var small = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                    small.ReadPixels(new Rect(0, 0, tw, th), 0, 0, false);
                    small.Apply(false, true);
                    small.filterMode = FilterMode.Bilinear;
                    return small;
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.Destroy(full);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("WebThumbStrip: thumbnail decode failed: " + ex.Message);
                if (full != null) UnityEngine.Object.Destroy(full);
                return null;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e != null && e.Thumb != null)
                {
                    UnityEngine.Object.Destroy(e.Thumb);
                    e.Thumb = null;
                }
            }
            _entries.Clear();
        }

        private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public WebThumbStrip strip;
            public WebThumbEntry entry;
            public void OnPointerEnter(PointerEventData eventData) { if (strip != null) strip.OnCellEnter(entry); }
            public void OnPointerExit(PointerEventData eventData) { if (strip != null) strip.OnCellExit(entry); }
        }
    }
}
