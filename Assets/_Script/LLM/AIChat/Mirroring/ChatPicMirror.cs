using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AITools.AIChat.Mirroring
{
    /// <summary>
    /// Lightweight viewer that mirrors a world-space <see cref="PicMain"/> into a chat-side
    /// <see cref="RawImage"/>. Pic generation still flows through the unmodified
    /// PicMain/ImageGenerator pipeline (so undo, drag, status text, etc. all just work);
    /// this component just polls the Pic for its current texture/status and copies it.
    ///
    /// Aspect-ratio handling: the chat panel builds the image bubble with a
    /// HorizontalLayoutGroup container whose child alignment is centered AND that does
    /// NOT force-expand its child width. So we can size the inner RawImage to the
    /// aspect-correct width/height ourselves; whatever empty space remains on either
    /// side (when the source is narrower than the bubble) becomes natural padding.
    /// We recompute every frame because the container width can change (panel resize,
    /// font scaling, scrollbar appearing, etc.).
    ///
    /// Click on the image bubble to pan the camera over the world Pic so the user can
    /// keep editing/inspecting it normally.
    /// </summary>
    public class ChatPicMirror : MonoBehaviour, IPointerClickHandler
    {
        public RawImage targetImage;
        public TMP_Text statusLabel;

        // Sizes the RawImage itself (preferredWidth + preferredHeight) so the
        // HorizontalLayoutGroup can lay it out at the aspect-correct dimensions.
        public LayoutElement imageLayoutElement;
        // Sizes the row the image lives in (preferredHeight only) so the bubble's
        // outer VerticalLayoutGroup knows how tall to make this row.
        public LayoutElement containerLayoutElement;
        // RectTransform of the container - used to read the available width budget
        // (determined by the bubble's outer VLG) so we know how big to make the image.
        public RectTransform containerRT;
        // The AI Chat panel that may be covering the world Pic. On click we focus the
        // world camera into the largest visible screen region outside this rect.
        public RectTransform occludingPanel;
        // Media-column scroll rect. When this mirror changes its own layout, it only
        // follows to bottom if the user was already at the bottom before the change.
        public ScrollRect autoScrollTarget;

        public PicMain sourcePic;
        // Generous defaults so a square 1024x1024 generation isn't tiny in the chat.
        // For a typical ~800px-wide chat panel, a 600x600 preview reads naturally.
        // Both bounds are upper bounds; aspect ratio is always preserved within them.
        public float maxPreviewHeight = 600f;
        public float maxPreviewWidth = 800f;
        public float minPreviewHeight = 96f;
        public float minPreviewWidth = 96f;

        private Texture _lastBoundTexture;
        private float _sourceAspect = 0f;          // height / width of the source texture
        private float _lastAppliedW = -1f;
        private float _lastAppliedH = -1f;
        private float _lastContainerWidth = -1f;
        private string _lastStatus = "";
        private bool _hasSyncedStatus = false;
        private bool _picWentMissingNotified = false;
        private const float ScrollBottomPixelEpsilon = 12f;

        private void OnEnable()
        {
            // Drive an initial layout pass so the bubble doesn't pop in at 0 height.
            StartCoroutine(SyncOnceAfterLayout());
        }

        private IEnumerator SyncOnceAfterLayout()
        {
            yield return null;
            SyncFromSource();
            UpdateAspectFit();
        }

        private void Update()
        {
            SyncFromSource();
            UpdateAspectFit();
        }

        private void SyncFromSource()
        {
            if (sourcePic == null || sourcePic.gameObject == null)
            {
                NotifyPicMissing();
                return;
            }

            // 1) Texture
            Texture currentTex;
            if (sourcePic.TryGetCurrentTexture(out currentTex) && currentTex != null)
            {
                if (currentTex != _lastBoundTexture)
                {
                    if (targetImage != null)
                    {
                        targetImage.texture = currentTex;
                        targetImage.color = Color.white;
                    }
                    _lastBoundTexture = currentTex;
                }

                // Re-cache the aspect each frame so dynamic-size sources (movies that grow,
                // upscaler chains that swap textures, etc.) keep the image visually correct.
                int w = currentTex.width;
                int h = currentTex.height;
                if (w > 0 && h > 0)
                    _sourceAspect = (float)h / w;
            }
            else if (targetImage != null && targetImage.texture == null)
            {
                // No texture yet (queued for GPU) - leave a hint of the empty slot until
                // the first frame arrives.
                targetImage.color = new Color(1f, 1f, 1f, 0.18f);
            }

            // 2) Status text. Don't squash newlines - PicMain emits multi-line statuses
            // (e.g. "Waiting for GPU to\nrun workflow...") and the bubble's status row is
            // sized via TextMeshPro's natural preferred height to fit them.
            if (statusLabel != null)
            {
                string status = sourcePic.GetStatusMessage() ?? "";
                if (!_hasSyncedStatus || status != _lastStatus)
                {
                    bool shouldAutoScroll = IsScrollAtBottom(autoScrollTarget);
                    statusLabel.text = string.IsNullOrEmpty(status) ? "Done." : status;
                    _lastStatus = status;
                    _hasSyncedStatus = true;
                    RequestAutoScrollIfNeeded(shouldAutoScroll);
                }
            }
        }

        /// <summary>
        /// Recompute desired image width/height (aspect-correct, capped by both the
        /// available bubble width and maxPreviewHeight) every frame, and write them
        /// to the inner RawImage's LayoutElement and the row container's LayoutElement.
        /// Cheap: skip the writes when nothing meaningful changed.
        ///
        /// While the source Pic is still generating AND no texture has ever been
        /// bound yet, the image area collapses to zero so the bubble takes only the
        /// space its label + status row need - keeps the gallery compact during long
        /// renders. As soon as the render finishes, we expand back to the aspect-
        /// correct size.
        ///
        /// Multi-stage chain handling: in chained workflows (e.g. generate_image then
        /// image_to_movie chain="true") both stages run on the same Pic and
        /// IsBusyBasic stays true the whole time. Once the first stage publishes a
        /// texture (set in SyncFromSource via _lastBoundTexture), we keep the bubble
        /// visible at aspect-correct size for the rest of the chain so the user sees
        /// the still image while the next stage (video) is rendering. SyncFromSource
        /// will swap to the newer texture (e.g. movie RenderTexture) the moment it
        /// becomes available.
        /// </summary>
        private void UpdateAspectFit()
        {
            if (imageLayoutElement == null || containerLayoutElement == null
                || containerRT == null) return;

            // Collapsed-while-generating mode. Doesn't depend on _sourceAspect being
            // known yet (that only gets set when the first texture frame arrives).
            // Skip the collapse once any intermediate texture has been bound, so
            // chained workflows reveal the still image as soon as it lands instead
            // of staying hidden until the entire chain (e.g. video stage) finishes.
            bool isBusy = sourcePic != null && sourcePic.IsBusyBasic();
            bool hasIntermediate = _lastBoundTexture != null;
            if (isBusy && !hasIntermediate)
            {
                if (Mathf.Abs(_lastAppliedH) > 0.5f || _lastAppliedW > 0.5f)
                {
                    bool shouldAutoScroll = IsScrollAtBottom(autoScrollTarget);
                    _lastAppliedW = 0f;
                    _lastAppliedH = 0f;
                    _lastContainerWidth = -1f;
                    imageLayoutElement.preferredWidth = 0f;
                    imageLayoutElement.preferredHeight = 0f;
                    imageLayoutElement.minWidth = 0f;
                    imageLayoutElement.minHeight = 0f;
                    containerLayoutElement.preferredHeight = 0f;
                    containerLayoutElement.minHeight = 0f;
                    if (targetImage != null)
                        targetImage.color = new Color(0f, 0f, 0f, 0f); // hide while busy
                    RequestAutoScrollIfNeeded(shouldAutoScroll);
                }
                return;
            }

            // Done generating: re-show the image at aspect-correct size.
            if (targetImage != null && targetImage.color.a < 0.5f)
                targetImage.color = Color.white;

            if (_sourceAspect <= 0f) return;

            float availableWidth = containerRT.rect.width;
            if (availableWidth <= 1f) return; // layout hasn't resolved yet

            // Start with the smaller of the bubble width budget OR our explicit width cap.
            // Then derive height from aspect; if the result is taller than our height cap,
            // scale BOTH dimensions down (preserving aspect) so the height fits.
            float w = Mathf.Min(availableWidth, maxPreviewWidth);
            float h = w * _sourceAspect;

            if (h > maxPreviewHeight)
            {
                h = maxPreviewHeight;
                w = h / _sourceAspect; // preserve aspect
            }

            // Floor at minimums so the bubble isn't visually broken by a missing texture.
            w = Mathf.Max(minPreviewWidth, w);
            h = Mathf.Max(minPreviewHeight, h);

            // Width can never exceed the container budget (otherwise HLG will clip or shove
            // it past the bubble edge).
            if (w > availableWidth) w = availableWidth;

            // Skip layout writes when neither the container width nor the desired size
            // has meaningfully changed since last frame.
            if (Mathf.Abs(availableWidth - _lastContainerWidth) < 0.5f
                && Mathf.Abs(w - _lastAppliedW) < 0.5f
                && Mathf.Abs(h - _lastAppliedH) < 0.5f)
                return;

            _lastContainerWidth = availableWidth;
            _lastAppliedW = w;
            _lastAppliedH = h;

            bool shouldFollowBottom = IsScrollAtBottom(autoScrollTarget);
            imageLayoutElement.preferredWidth = w;
            imageLayoutElement.preferredHeight = h;
            imageLayoutElement.minWidth = Mathf.Min(minPreviewWidth, w);
            imageLayoutElement.minHeight = Mathf.Min(minPreviewHeight, h);
            containerLayoutElement.preferredHeight = h;
            containerLayoutElement.minHeight = Mathf.Min(minPreviewHeight, h);
            RequestAutoScrollIfNeeded(shouldFollowBottom);
        }

        private void NotifyPicMissing()
        {
            if (_picWentMissingNotified) return;
            _picWentMissingNotified = true;
            if (statusLabel != null)
                statusLabel.text = "(World Pic was deleted - preview is final)";
        }

        private static bool IsScrollAtBottom(ScrollRect scroll)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null)
                return true;

            float contentHeight = scroll.content.rect.height;
            float viewportHeight = scroll.viewport.rect.height;
            if (contentHeight <= viewportHeight + 1f)
                return true;

            // Pixel-based: normalized 5% of a tall chat is hundreds of pixels, which made
            // mid-scroll users register as "at bottom" and still get auto-scrolled.
            float scrollableRange = contentHeight - viewportHeight;
            float pixelsFromBottom = Mathf.Clamp01(scroll.verticalNormalizedPosition) * scrollableRange;
            return pixelsFromBottom <= ScrollBottomPixelEpsilon;
        }

        private void RequestAutoScrollIfNeeded(bool shouldAutoScroll)
        {
            if (shouldAutoScroll && autoScrollTarget != null)
                StartCoroutine(ScrollToBottomDeferred(autoScrollTarget));
        }

        private static IEnumerator ScrollToBottomDeferred(ScrollRect scroll)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 0f;

            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 0f;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (sourcePic == null || sourcePic.gameObject == null) return;

            // Move the world camera so the source Pic lands in the largest visible area
            // not covered by the AI Chat panel.
            // Reuse PicMain's own camera ref so we never pick the wrong one in multi-camera
            // setups (e.g. CrazyCam mode).
            Camera cam = sourcePic.GetCamera();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector3 picPos = sourcePic.transform.position;
            Vector2 targetScreenPoint = GetBestFocusScreenPoint();
            float depth = Mathf.Abs(Vector3.Dot(picPos - cam.transform.position, cam.transform.forward));
            if (depth <= 0.001f)
                depth = Mathf.Abs(picPos.z - cam.transform.position.z);

            Vector3 worldAtTarget = cam.ScreenToWorldPoint(new Vector3(targetScreenPoint.x, targetScreenPoint.y, depth));
            Vector3 delta = picPos - worldAtTarget;
            Vector3 newCamPos = cam.transform.position + delta;
            newCamPos.z = cam.transform.position.z;
            cam.transform.position = newCamPos;
        }

        private Vector2 GetBestFocusScreenPoint()
        {
            Rect screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            if (occludingPanel == null)
                return screenRect.center;

            Rect panelRect = GetScreenRect(occludingPanel);
            if (panelRect.width <= 1f || panelRect.height <= 1f || !panelRect.Overlaps(screenRect))
                return screenRect.center;

            panelRect = Intersect(panelRect, screenRect);
            Rect best = new Rect(0f, 0f, 0f, 0f);
            float bestArea = 0f;

            ConsiderFocusRegion(new Rect(0f, 0f, panelRect.xMin, screenRect.height), ref best, ref bestArea);
            ConsiderFocusRegion(new Rect(panelRect.xMax, 0f, screenRect.width - panelRect.xMax, screenRect.height), ref best, ref bestArea);
            ConsiderFocusRegion(new Rect(0f, panelRect.yMax, screenRect.width, screenRect.height - panelRect.yMax), ref best, ref bestArea);
            ConsiderFocusRegion(new Rect(0f, 0f, screenRect.width, panelRect.yMin), ref best, ref bestArea);

            return bestArea > 1f ? Inset(best, 24f).center : screenRect.center;
        }

        private static void ConsiderFocusRegion(Rect region, ref Rect best, ref float bestArea)
        {
            if (region.width <= 1f || region.height <= 1f) return;
            float area = region.width * region.height;
            if (area <= bestArea) return;
            bestArea = area;
            best = region;
        }

        private static Rect GetScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            return Rect.MinMaxRect(
                Mathf.Max(a.xMin, b.xMin),
                Mathf.Max(a.yMin, b.yMin),
                Mathf.Min(a.xMax, b.xMax),
                Mathf.Min(a.yMax, b.yMax));
        }

        private static Rect Inset(Rect rect, float margin)
        {
            float xMargin = Mathf.Min(margin, Mathf.Max(0f, rect.width * 0.45f));
            float yMargin = Mathf.Min(margin, Mathf.Max(0f, rect.height * 0.45f));
            rect.xMin += xMargin;
            rect.xMax -= xMargin;
            rect.yMin += yMargin;
            rect.yMax -= yMargin;
            return rect;
        }
    }
}
