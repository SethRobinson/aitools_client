using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AITools.AIChat.UI
{
    /// <summary>
    /// Floating, non-modal "Thinking" window that shows a reply's reasoning (the
    /// &lt;think&gt; block) instead of expanding it inline under the chat bubble. Opened by
    /// a click on a bubble's "[thinking...]" / "[thinking]" marker; AIChatPanel keeps
    /// pushing the live stream buffer into it while the reasoning is still arriving and
    /// hands it the final text on completion / Stop. Closing it (the X, or clicking the
    /// same marker again) only destroys this window: the chat turn, the stream and the
    /// stored reasoning are untouched, and the marker reopens it any time.
    ///
    /// One window at a time: clicking another bubble's marker retargets it. The text is
    /// rendered with rich text OFF (reasoning is full of literal &lt;tags&gt; and
    /// backslashes), throttled to a few refreshes per second so long DeepSeek-style
    /// think blocks don't re-layout on every token, and auto-follows the bottom while
    /// the user hasn't scrolled up. Copy puts the whole reasoning on the clipboard.
    /// Lives on its own ScreenSpaceOverlay canvas above the chat panel but below the
    /// modal dialogs (clip chooser), with the shared PanelDragHandler header and a
    /// bottom-right resize grip; size and position persist for the session.
    ///
    /// The same window (kind <see cref="WindowKind.WebTrace"/>) shows a Web bubble's FULL
    /// fetch trace when its "[details]" marker is clicked: the bubble only keeps the
    /// title, the outcome lines and the thumbnails (WebTraceBubble), and the host pushes
    /// every new trace line in here while the fetch still runs, exactly like reasoning.
    /// </summary>
    public class ChatThinkingWindow : MonoBehaviour
    {
        /// <summary>What the window is showing; it changes the titles, placeholders and the header tint.</summary>
        public enum WindowKind { Thinking, WebTrace }

        private const string CanvasName = "ChatThinkingWindowCanvas";
        private const int CanvasSortingOrder = 4000;   // chat panel 100 / settings 110 < this < clip chooser 5000
        private const float HeaderHeight = 32f;
        private const float MinWidth = 320f;
        private const float MinHeight = 200f;
        private const float RefreshIntervalSeconds = 0.12f;

        private static readonly Color HeaderTintThinking = new Color(0.80f, 0.78f, 0.88f, 1f);
        private static readonly Color HeaderTintWebTrace = new Color(0.74f, 0.85f, 0.90f, 1f);
        private static readonly Color TitleColorThinking = new Color(0.30f, 0.20f, 0.45f);
        private static readonly Color TitleColorWebTrace = new Color(0.10f, 0.38f, 0.52f);

        private WindowKind _kind = WindowKind.Thinking;
        private string TitleLive => _kind == WindowKind.WebTrace ? "Web trace (working...)" : "Thinking (streaming...)";
        private string TitleDone => _kind == WindowKind.WebTrace ? "Web trace" : "Thinking";
        private string TitleGone => _kind == WindowKind.WebTrace ? "Web trace (bubble no longer in chat)" : "Thinking (bubble no longer in chat)";
        private string PlaceholderLive => _kind == WindowKind.WebTrace ? "(waiting for the first trace line...)" : "(waiting for reasoning...)";
        private string PlaceholderEmpty => _kind == WindowKind.WebTrace ? "(empty trace)" : "(no reasoning captured)";
        private string CopyToast => _kind == WindowKind.WebTrace ? "Web trace copied to clipboard" : "Reasoning copied to clipboard";

        private static ChatThinkingWindow _instance;
        private static Vector2 _lastSize = new Vector2(600f, 460f);
        private static bool _hasLastPosition;
        private static Vector2 _lastPosition;

        private RectTransform _root;
        private TMP_FontAsset _font;
        private float _fontSize;
        private TextMeshProUGUI _title;
        private Image _headerImg;
        private TextMeshProUGUI _text;
        private ScrollRect _scroll;
        private RectTransform _content;

        private TMP_InputField _sourceField;
        private string _pendingText = "";
        private string _shownText;
        private bool _dirty;
        private bool _live;
        private float _nextRefreshAt;
        private Coroutine _scrollToBottom;

        /// <summary>The open window, or null.</summary>
        public static ChatThinkingWindow Current => _instance;

        /// <summary>True while the window still expects more reasoning from the stream (or more trace lines from a running fetch).</summary>
        public bool IsLive => _live;

        /// <summary>Reasoning of an assistant bubble, or the full trace of a Web bubble.</summary>
        public WindowKind Kind => _kind;

        /// <summary>Current header text ("Thinking (streaming...)" / "Thinking" / "Web trace" / bubble-gone).</summary>
        public string TitleText => _title != null ? _title.text : "";

        /// <summary>Length of the reasoning text handed to the window (pending or shown).</summary>
        public int TextLength => (_pendingText ?? "").Length;

        /// <summary>Layout telemetry for the automation bridge: "x,y,w,h" of the window (anchored position + size) and its parent canvas rect size.</summary>
        public string LayoutDebug()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (_root == null) return "";
            var parent = _root.parent as RectTransform;
            return "pos=" + _root.anchoredPosition.x.ToString("0", ci) + "," + _root.anchoredPosition.y.ToString("0", ci)
                + " size=" + _root.sizeDelta.x.ToString("0", ci) + "," + _root.sizeDelta.y.ToString("0", ci)
                + " parent=" + (parent != null ? parent.rect.width.ToString("0", ci) + "," + parent.rect.height.ToString("0", ci) : "none")
                + " screen=" + Screen.width + "," + Screen.height;
        }

        /// <summary>True when this window mirrors <paramref name="field"/>'s bubble (false once that bubble was destroyed).</summary>
        public bool IsShowing(TMP_InputField field)
        {
            return field != null && _sourceField != null && ReferenceEquals(_sourceField, field);
        }

        /// <summary>
        /// Open (or retarget) the window for a bubble. <paramref name="live"/> = the
        /// reasoning is still streaming, so the title says so and the view follows the
        /// bottom; the caller keeps feeding SetText until it flips live off.
        /// </summary>
        public static ChatThinkingWindow Show(TMP_FontAsset font, float fontSize, TMP_InputField sourceField, string text, bool live)
        {
            return Show(font, fontSize, sourceField, text, live, WindowKind.Thinking);
        }

        /// <summary>
        /// Open (or retarget) the window for a bubble of the given <paramref name="kind"/>:
        /// an assistant bubble's reasoning, or a Web bubble's full trace (live while the
        /// fetch still runs; the host feeds SetText per trace line).
        /// </summary>
        public static ChatThinkingWindow Show(TMP_FontAsset font, float fontSize, TMP_InputField sourceField, string text, bool live, WindowKind kind)
        {
            if (_instance == null)
            {
                var parent = ResolveCanvasParent();
                var go = new GameObject("ChatThinkingWindow");
                go.transform.SetParent(parent, false);
                _instance = go.AddComponent<ChatThinkingWindow>();
                _instance.Build(font, fontSize);
            }
            _instance.Retarget(sourceField, text, live, kind);
            _instance.transform.SetAsLastSibling();
            return _instance;
        }

        public static void CloseIfOpen()
        {
            if (_instance != null)
                _instance.Close();
        }

        /// <summary>Replace the displayed reasoning (applied on the next throttled refresh).</summary>
        public void SetText(string text, bool live)
        {
            _pendingText = text ?? "";
            _live = live;
            _dirty = true;
            UpdateTitle();
        }

        public void Close()
        {
            // Destroy is deferred to end of frame; drop the singleton now so a status read
            // (or a marker click) in the same frame already sees the window as gone.
            RememberRectAndRelease();
            Destroy(gameObject);
        }

        private void Retarget(TMP_InputField sourceField, string text, bool live, WindowKind kind)
        {
            bool sameBubble = IsShowing(sourceField) && _kind == kind;
            _sourceField = sourceField;
            _kind = kind;
            _pendingText = text ?? "";
            _live = live;
            _dirty = true;
            ApplyKindVisuals();
            if (!sameBubble)
            {
                // A different bubble: show its text from the top (a finished block) or
                // follow the bottom (still streaming / fetching), without waiting for the throttle.
                _nextRefreshAt = 0f;
                ApplyPending(forceScroll: live ? ScrollTarget.Bottom : ScrollTarget.Top);
            }
            UpdateTitle();
        }

        private void ApplyKindVisuals()
        {
            if (_headerImg != null) _headerImg.color = _kind == WindowKind.WebTrace ? HeaderTintWebTrace : HeaderTintThinking;
            if (_title != null) _title.color = _kind == WindowKind.WebTrace ? TitleColorWebTrace : TitleColorThinking;
        }

        private void Update()
        {
            if (_live && _sourceField == null)
            {
                // Clear / Rewind / rebuild destroyed the bubble mid-stream: keep what we
                // have, stop claiming it is live.
                _live = false;
                UpdateTitle();
            }
            if (_dirty && Time.unscaledTime >= _nextRefreshAt)
                ApplyPending(ScrollTarget.KeepOrFollow);
        }

        private enum ScrollTarget { KeepOrFollow, Top, Bottom }

        private void ApplyPending(ScrollTarget forceScroll)
        {
            _dirty = false;
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            if (_text == null) return;
            string t = _pendingText.Trim();
            if (t.Length == 0) t = _live ? PlaceholderLive : PlaceholderEmpty;
            if (string.Equals(t, _shownText, System.StringComparison.Ordinal) && forceScroll == ScrollTarget.KeepOrFollow)
                return;

            bool wasAtBottom = global::AIChatPanel.IsScrollAtBottom(_scroll);
            _shownText = t;
            _text.text = t;
            if (_content != null)
                LayoutRebuilder.MarkLayoutForRebuild(_content);

            bool goBottom = forceScroll == ScrollTarget.Bottom || (forceScroll == ScrollTarget.KeepOrFollow && wasAtBottom);
            if (goBottom || forceScroll == ScrollTarget.Top)
            {
                if (_scrollToBottom != null) StopCoroutine(_scrollToBottom);
                _scrollToBottom = StartCoroutine(ScrollDeferred(goBottom ? 0f : 1f));
            }
        }

        private IEnumerator ScrollDeferred(float normalized)
        {
            // Layout runs at end of frame; set the position once the content has its new height.
            yield return null;
            if (_scroll != null)
                _scroll.verticalNormalizedPosition = normalized;
            _scrollToBottom = null;
        }

        private void UpdateTitle()
        {
            if (_title == null) return;
            _title.text = _live ? TitleLive : (_sourceField == null ? TitleGone : TitleDone);
        }

        private void CopyToClipboard()
        {
            try
            {
                GUIUtility.systemCopyBuffer = _pendingText ?? "";
                global::RTQuickMessageManager.Get().ShowMessage(CopyToast);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("ChatThinkingWindow: copy failed: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            RememberRectAndRelease();
        }

        private void RememberRectAndRelease()
        {
            if (_root != null)
            {
                _lastSize = _root.sizeDelta;
                _lastPosition = _root.anchoredPosition;
                _hasLastPosition = true;
            }
            if (_instance == this)
                _instance = null;
        }

        // ---------- construction ----------

        private void Build(TMP_FontAsset font, float fontSize)
        {
            _font = font;
            _fontSize = Mathf.Max(9f, fontSize);

            _root = gameObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            // Not clamped here: the canvas may have been created this frame with a zero
            // rect, and clamping against that mangles the position. ReclampNextFrame does it.
            _root.sizeDelta = _lastSize;
            _root.anchoredPosition = _hasLastPosition ? _lastPosition : DefaultPosition();

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.94f, 0.94f, 0.96f, 0.98f);
            bg.raycastTarget = true;

            BuildHeader();
            BuildBody();
            BuildResizeGrip();
            // A canvas created this frame still has a zero-sized rect, so the clamp above
            // ran against an empty parent (and pushed the window down by half its height on
            // first open). Re-clamp once the canvas has laid itself out.
            StartCoroutine(ReclampNextFrame());
        }

        private IEnumerator ReclampNextFrame()
        {
            yield return null;
            if (_root == null) yield break;
            _root.sizeDelta = ClampSize(_root.sizeDelta);
            // Clamp the INTENDED position (remembered or default), never the one Build set
            // while the parent rect was still empty.
            _root.anchoredPosition = global::PanelDragHandler.ClampAnchoredPosition(
                _root, _hasLastPosition ? _lastPosition : DefaultPosition(), HeaderHeight);
        }

        private Vector2 DefaultPosition()
        {
            // Right of centre so it doesn't sit exactly over the chat's newest bubble.
            var parent = _root.parent as RectTransform;
            bool parentLaidOut = parent != null && parent.rect.width > 1f && parent.rect.height > 1f;
            float parentW = parentLaidOut ? parent.rect.width : Screen.width;
            float x = Mathf.Max(0f, parentW * 0.5f - _root.sizeDelta.x * 0.5f - 24f);
            var pos = new Vector2(x, 0f);
            // Only clamp against a parent that has a size; a zero rect (canvas created this
            // frame) would pull the window half off-screen.
            return parentLaidOut ? global::PanelDragHandler.ClampAnchoredPosition(_root, pos, HeaderHeight) : pos;
        }

        private void BuildHeader()
        {
            var header = new GameObject("Header");
            header.transform.SetParent(transform, false);
            var rt = header.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, HeaderHeight);
            rt.anchoredPosition = Vector2.zero;
            var img = header.AddComponent<Image>();
            img.color = HeaderTintThinking;
            img.raycastTarget = true;
            _headerImg = img;
            header.AddComponent<global::PanelDragHandler>().SetTarget(_root, HeaderHeight);

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(header.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(10f, 0f);
            titleRt.offsetMax = new Vector2(-(global::RTWindowChrome.CloseButtonSize + 12f + 64f + 8f), 0f);
            _title = titleGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) _title.font = _font;
            _title.fontSize = 14f;
            _title.fontStyle = FontStyles.Bold;
            _title.color = TitleColorThinking;
            _title.alignment = TextAlignmentOptions.MidlineLeft;
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.raycastTarget = false;
            _title.text = TitleDone;

            // Copy button left of the close X.
            var copyGo = new GameObject("Copy");
            copyGo.transform.SetParent(header.transform, false);
            var copyRt = copyGo.AddComponent<RectTransform>();
            copyRt.anchorMin = new Vector2(1f, 0.5f);
            copyRt.anchorMax = new Vector2(1f, 0.5f);
            copyRt.pivot = new Vector2(1f, 0.5f);
            copyRt.sizeDelta = new Vector2(60f, 22f);
            copyRt.anchoredPosition = new Vector2(-(global::RTWindowChrome.CloseButtonSize + 12f), 0f);
            var copyImg = copyGo.AddComponent<Image>();
            copyImg.color = new Color(0.18f, 0.24f, 0.32f, 1f);
            var copyBtn = copyGo.AddComponent<Button>();
            copyBtn.targetGraphic = copyImg;
            copyBtn.onClick.AddListener(CopyToClipboard);
            var copyLabelGo = new GameObject("Label");
            copyLabelGo.transform.SetParent(copyGo.transform, false);
            var copyLabelRt = copyLabelGo.AddComponent<RectTransform>();
            copyLabelRt.anchorMin = Vector2.zero;
            copyLabelRt.anchorMax = Vector2.one;
            copyLabelRt.offsetMin = Vector2.zero;
            copyLabelRt.offsetMax = Vector2.zero;
            var copyLabel = copyLabelGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) copyLabel.font = _font;
            copyLabel.text = "Copy";
            copyLabel.fontSize = 11f;
            copyLabel.fontStyle = FontStyles.Bold;
            copyLabel.color = Color.white;
            copyLabel.alignment = TextAlignmentOptions.Center;
            copyLabel.raycastTarget = false;

            global::RTWindowChrome.CreateCloseButton(rt, Close);
        }

        private void BuildBody()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            var rt = body.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f);
            rt.offsetMax = new Vector2(-6f, -HeaderHeight - 4f);

            _scroll = body.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 30f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(body.transform, false);
            var vpRt = viewportGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-16f, 0f);
            var vpImg = viewportGo.AddComponent<Image>();
            vpImg.color = Color.white;          // raycast target so the wheel scrolls the view
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 10);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(contentGo.transform, false);
            textGo.AddComponent<RectTransform>();
            _text = textGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) _text.font = _font;
            _text.fontSize = _fontSize;
            _text.color = new Color(0.16f, 0.17f, 0.22f);
            _text.richText = false;                 // reasoning is full of literal <tags>
            _text.parseCtrlCharacters = false;      // and of \n-looking path text
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.raycastTarget = false;

            _scroll.viewport = vpRt;
            _scroll.content = _content;
            _scroll.verticalScrollbar = BuildScrollbar(body.transform);
        }

        private Scrollbar BuildScrollbar(Transform host)
        {
            var sbGo = new GameObject("Scrollbar");
            sbGo.transform.SetParent(host, false);
            var sbRt = sbGo.AddComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.sizeDelta = new Vector2(12f, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            var sbImg = sbGo.AddComponent<Image>();
            sbImg.color = new Color(0.85f, 0.85f, 0.88f, 1f);
            var sb = sbGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var areaGo = new GameObject("Sliding Area");
            areaGo.transform.SetParent(sbGo.transform, false);
            var areaRt = areaGo.AddComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(2f, 2f);
            areaRt.offsetMax = new Vector2(-2f, -2f);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(areaGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.45f, 0.45f, 0.55f, 1f);
            sb.targetGraphic = handleImg;
            sb.handleRect = handleRt;
            return sb;
        }

        private void BuildResizeGrip()
        {
            var go = new GameObject("ResizeGrip");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = new Vector2(-2f, 2f);
            global::RTWindowChrome.ConfigureResizeGrip(rt);
            go.AddComponent<ResizeGripDragHandler>().SetOwner(this);
        }

        private Vector2 ClampSize(Vector2 size)
        {
            var parent = _root != null ? _root.parent as RectTransform : null;
            Vector2 parentSize = parent != null && parent.rect.width > 1f && parent.rect.height > 1f
                ? parent.rect.size
                : new Vector2(Screen.width, Screen.height);
            size.x = Mathf.Clamp(size.x, MinWidth, Mathf.Max(MinWidth, parentSize.x - 16f));
            size.y = Mathf.Clamp(size.y, MinHeight, Mathf.Max(MinHeight, parentSize.y - 16f));
            return size;
        }

        private void ResizeTo(Vector2 size, Vector2 anchoredPosition)
        {
            if (_root == null) return;
            _root.sizeDelta = ClampSize(size);
            _root.anchoredPosition = global::PanelDragHandler.ClampAnchoredPosition(_root, anchoredPosition, HeaderHeight);
        }

        private static RectTransform ResolveCanvasParent()
        {
            foreach (var existing in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (existing == null || existing.gameObject == null) continue;
                if (existing.gameObject.name != CanvasName) continue;
                if (!existing.gameObject.scene.IsValid()) continue;
                ConfigureCanvas(existing);
                return existing.transform as RectTransform;
            }
            var canvasGo = new GameObject(CanvasName, typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            ConfigureCanvas(canvas);
            return canvasGo.transform as RectTransform;
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = CanvasSortingOrder;
            canvas.gameObject.SetActive(true);
            var rt = canvas.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        /// <summary>Bottom-right grip: dragging grows the window right/down, keeping the top-left corner put.</summary>
        private sealed class ResizeGripDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private ChatThinkingWindow _owner;
            private RectTransform _parent;
            private Vector2 _startPointerLocal;
            private Vector2 _startSize;
            private Vector2 _startAnchoredPosition;

            public void SetOwner(ChatThinkingWindow owner)
            {
                _owner = owner;
                _parent = owner != null && owner._root != null ? owner._root.parent as RectTransform : null;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_owner == null || _owner._root == null || _parent == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out _startPointerLocal);
                _startSize = _owner._root.sizeDelta;
                _startAnchoredPosition = _owner._root.anchoredPosition;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_owner == null || _owner._root == null || _parent == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, eventData.pressEventCamera, out Vector2 local);
                Vector2 delta = local - _startPointerLocal;
                float widthDelta = delta.x;
                float heightDelta = -delta.y;
                Vector2 newSize = new Vector2(_startSize.x + widthDelta, _startSize.y + heightDelta);
                // Centre pivot: shift by half the growth so the top-left corner stays fixed.
                Vector2 newPos = _startAnchoredPosition + new Vector2(widthDelta * 0.5f, -heightDelta * 0.5f);
                _owner.ResizeTo(newSize, newPos);
            }
        }
    }
}
