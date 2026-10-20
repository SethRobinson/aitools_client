using System;
using AITools.AIChat.Video;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AITools.AIChat.Web
{
    /// <summary>Local UI only. No model action can launch Firefox or accept the login.</summary>
    public sealed class YouTubeLoginDialog : MonoBehaviour
    {
        public sealed class Request
        {
            public bool Completed;
            public bool Retry;
        }

        private static YouTubeLoginDialog _current;
        private readonly Request _request = new Request();
        private FfmpegTool.CancelToken _cancel;
        private TextMeshProUGUI _body;
        private Button _done;
        private TMP_FontAsset _font;
        private Action _onReady;
        private bool _opened;
        public static bool IsOpen => _current != null && !_current._request.Completed;

        public static Request Show(Transform parent, TMP_FontAsset font, FfmpegTool.CancelToken cancel,
            bool forRetry = true, Action onReady = null)
        {
            if (IsOpen)
            {
                _current._onReady += onReady;
                return _current._request;
            }
            var root = new GameObject("YouTubeSignIn", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var shade = root.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.45f);
            var dialog = root.AddComponent<YouTubeLoginDialog>();
            _current = dialog;
            dialog._font = font;
            dialog._cancel = cancel;
            dialog._onReady = onReady;
            dialog.Build(forRetry);
            return dialog._request;
        }

        private void Build(bool forRetry)
        {
            var panel = new GameObject("Dialog", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(680, 340);
            panel.GetComponent<Image>().color = new Color(0.96f, 0.97f, 0.98f);
            var title = Text(rt, "Title", forRetry ? "YouTube needs sign-in" : "YouTube login", 23);
            Place(title.rectTransform, 24, 20, -24, 55);
            _body = Text(rt, "Body",
                (forRetry ? "YouTube reported a sign-in or verification problem. Sign in to retry this request.\n\n" : "")
                + "Open Firefox to sign in to YouTube in a separate profile reserved for AI Tools. "
                + "Then close that Firefox window and return here. The session stays on this computer and will be reused for YouTube clips and transcripts.",
                17);
            Place(_body.rectTransform, 24, 70, -24, 245);
            Button(rt, "OpenFirefox", "Sign in with Firefox", 24, 235, OpenFirefox);
            _done = Button(rt, "Done", forRetry ? "Done, retry" : "Done, remember login", 275, 225, Ready);
            _done.interactable = false;
            Button(rt, "Cancel", "Cancel", 516, 140, () => Finish(false));
        }

        private void OpenFirefox()
        {
            if (_request.Completed || (_cancel != null && _cancel.CancelRequested)) { Finish(false); return; }
            if (!YouTubeLogin.TryOpenFirefox(out var error))
            {
                _body.text = error;
                // An already-open dedicated window can be completed without launching another.
                try { _opened |= YouTubeLogin.IsProfileOpen(); } catch { }
                _done.interactable = _opened;
                return;
            }
            _opened = true;
            _done.interactable = true;
            _body.text = "In the dedicated Firefox window, sign in to YouTube and complete any verification. "
                + "Close that window when finished, then click Done below.\n\n"
                + "AI Tools remembers this separate session locally. Your everyday browser profile is not used, "
                + "and your password and cookies are never sent to AI Chat.";
        }

        private void Ready()
        {
            if (!_opened || _request.Completed || (_cancel != null && _cancel.CancelRequested)) { Finish(false); return; }
            if (!YouTubeLogin.CanRetry(out var error)) { _body.text = error; return; }
            var cfg = Config.Get();
            if (cfg == null) { _body.text = "Settings are unavailable. Cancel and try again."; return; }
            cfg.SetYouTubeFirefoxLogin(true);
            Finish(true);
        }

        private void Update()
        {
            if (_cancel != null && _cancel.CancelRequested) Finish(false);
        }

        private void Finish(bool retry)
        {
            if (_request.Completed) return;
            _request.Retry = retry;
            _request.Completed = true;
            if (_current == this) _current = null;
            gameObject.SetActive(false);
            Destroy(gameObject);
            if (retry) _onReady?.Invoke();
        }

        private void OnDestroy()
        {
            _request.Completed = true;
            if (_current == this) _current = null;
        }

        private TextMeshProUGUI Text(Transform parent, string name, string value, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = new Color(0.12f, 0.15f, 0.18f);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, -bottom);
            rt.offsetMax = new Vector2(right, -top);
        }

        private Button Button(Transform parent, string name, string label, float left, float width, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(left, 24);
            rt.sizeDelta = new Vector2(width, 42);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.80f, 0.88f, 0.94f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            var text = Text(rt, "Label", label, 16);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
