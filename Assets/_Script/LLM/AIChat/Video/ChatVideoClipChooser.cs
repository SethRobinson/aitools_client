using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AITools.AIChat.Video
{
    /// <summary>
    /// Small modal used when a user drops a long local video over AI Chat. It lets
    /// them pick a short FFmpeg clip without sending the original movie to ComfyUI.
    /// </summary>
    public class ChatVideoClipChooser : MonoBehaviour
    {
        private RectTransform _root;
        private TMP_FontAsset _font;
        private string _sourcePath;
        private string _previewSourcePath;
        private string _previewProxyPath;
        private string _titleText = "Import Video Clip";
        private string _confirmText = "Import Clip";
        private FfmpegTool.VideoInfo _info;
        private Action<ClipSelection> _onConfirm;
        private Action _onCancel;
        // Invoked by the "Import still" button with the current preview position in
        // seconds. Unlike Confirm/Cancel it does NOT close the dialog, so the user can
        // scrub and import several stills.
        private Action<float> _onImportStill;

        private VideoPlayer _player;
        private RenderTexture _rt;
        private RawImage _preview;
        private TextMeshProUGUI _previewHint;
        private Slider _slider;
        private TextMeshProUGUI _timeText;
        private GameObject _proxyProgressRoot;
        private Image _proxyProgressFill;
        private TextMeshProUGUI _proxyProgressText;
        private TMP_InputField _startField;
        private TMP_InputField _durationField;
        private TMP_InputField _fpsField;
        private Toggle _includeAudioToggle;
        private Button _playButton;
        private TextMeshProUGUI _playButtonLabel;
        private Button _duration3Button;
        private Button _duration5Button;
        private Button _duration8Button;
        private float _duration = FfmpegTool.DefaultClipDurationSeconds;
        private float _selectedStartSeconds;
        private float _previewCurrentSeconds;
        private float _initialStartSeconds;
        private double _fps = FfmpegTool.DefaultFps;
        private bool _includeAudio = true;
        private bool _prepared;
        private bool _proxyTried;
        private bool _proxyConversionInFlight;
        private bool _ignoreSlider;
        private bool _ignoreDurationField;
        private bool _ignoreFpsField;
        private float _proxyProgress;
        private string _proxyProgressMessage = "";
        private FfmpegTool.CancelToken _proxyCancelToken;
        private const string ModalCanvasName = "VideoClipChooserCanvas";
        private const float HeaderDragHeight = 58f;
        private const float MinDialogWidth = 500f;
        private const float MinDialogHeight = 470f;
        private const float MaxDialogWidth = 920f;
        private const float MaxDialogHeight = 760f;

        public sealed class ClipSelection
        {
            public float StartSeconds;
            public float DurationSeconds;
            public double Fps;
            public bool IncludeAudio;
        }

        public static ChatVideoClipChooser Show(
            RectTransform parent,
            TMP_FontAsset font,
            string sourcePath,
            FfmpegTool.VideoInfo info,
            Action<ClipSelection> onConfirm,
            Action onCancel,
            string titleText = "Import Video Clip",
            string confirmText = "Import Clip",
            float initialStartSeconds = 0f,
            Action<float> onImportStill = null)
        {
            RectTransform dialogParent = ResolveDialogParent(parent);
            var go = new GameObject("AIChatVideoClipChooser");
            go.transform.SetParent(dialogParent, false);
            var chooser = go.AddComponent<ChatVideoClipChooser>();
            chooser.Initialize(dialogParent, font, sourcePath, info, onConfirm, onCancel, titleText, confirmText, initialStartSeconds, onImportStill);
            return chooser;
        }

        private void Initialize(RectTransform parent, TMP_FontAsset font, string sourcePath, FfmpegTool.VideoInfo info, Action<ClipSelection> onConfirm, Action onCancel, string titleText, string confirmText, float initialStartSeconds, Action<float> onImportStill)
        {
            _font = font;
            _sourcePath = sourcePath;
            _previewSourcePath = sourcePath;
            _titleText = string.IsNullOrWhiteSpace(titleText) ? "Import Video Clip" : titleText;
            _confirmText = string.IsNullOrWhiteSpace(confirmText) ? "Import Clip" : confirmText;
            _info = info ?? new FfmpegTool.VideoInfo();
            _initialStartSeconds = ClampStartSeconds(initialStartSeconds);
            _selectedStartSeconds = _initialStartSeconds;
            _previewCurrentSeconds = _initialStartSeconds;
            _fps = _info.Fps > 0 ? ClampFps(_info.Fps) : FfmpegTool.DefaultFps;
            _includeAudio = true;
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _onImportStill = onImportStill;

            _root = gameObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            Vector2 parentSize = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);
            if (parentSize.x <= 1f || parentSize.y <= 1f)
                parentSize = new Vector2(Screen.width, Screen.height);
            float dialogW = parentSize.x > 0f ? Mathf.Clamp(parentSize.x - 32f, MinDialogWidth, 720f) : 660f;
            float dialogH = parentSize.y > 0f ? Mathf.Clamp(parentSize.y - 36f, MinDialogHeight, 590f) : 520f;
            _root.sizeDelta = new Vector2(dialogW, dialogH);
            _root.anchoredPosition = Vector2.zero;

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.94f, 0.94f, 0.96f, 0.98f);

            BuildUI();
            SetStartSeconds(_initialStartSeconds);
            SetSliderSeconds(_initialStartSeconds);
            if (FfmpegTool.ShouldUseUnityPreviewProxy(_info))
                StartCoroutine(ConvertPreviewProxyAndRetry("Unity preview proxy required for " + (_info.CodecName ?? "this codec")));
            else
                PreparePreview();
            UpdateTimeLabel();
        }

        private void BuildUI()
        {
            float dialogW = _root.sizeDelta.x;
            float dialogH = _root.sizeDelta.y;
            float innerW = Mathf.Max(496f, dialogW - 44f);
            float previewH = Mathf.Clamp(dialogH - 230f, 220f, 360f);
            float previewTop = -74f;
            float sliderY = previewTop - previewH - 22f;
            float controlsY = sliderY - 32f;
            float durationY = controlsY - 33f;
            float actionY = durationY - 38f;
            float left = -innerW * 0.5f;
            float right = innerW * 0.5f;

            CreateDragHeader(innerW);
            CreateLabel("Title", _titleText, new Vector2(0, -18), new Vector2(innerW, 24), 18, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            string file = System.IO.Path.GetFileName(_sourcePath);
            string meta = $"{file}   {FormatTime((float)_info.DurationSeconds)}";
            if (_info.Width > 0 && _info.Height > 0)
                meta += $"   {_info.Width}x{_info.Height}";
            CreateLabel("Meta", meta, new Vector2(0, -45), new Vector2(innerW, 20), 11, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(transform, false);
            var previewRt = previewGo.AddComponent<RectTransform>();
            previewRt.anchorMin = new Vector2(0.5f, 1f);
            previewRt.anchorMax = new Vector2(0.5f, 1f);
            previewRt.pivot = new Vector2(0.5f, 1f);
            previewRt.sizeDelta = new Vector2(innerW, previewH);
            previewRt.anchoredPosition = new Vector2(0, previewTop);
            var previewBg = previewGo.AddComponent<Image>();
            previewBg.color = new Color(0.04f, 0.04f, 0.05f, 1f);

            var rawGo = new GameObject("RawImage");
            rawGo.transform.SetParent(previewGo.transform, false);
            var rawRt = rawGo.AddComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(4, 4);
            rawRt.offsetMax = new Vector2(-4, -4);
            _preview = rawGo.AddComponent<RawImage>();
            _preview.color = new Color(1f, 1f, 1f, 0.4f);
            var aspect = rawGo.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = GetPreviewAspectRatio();

            _previewHint = CreateLabel("PreviewHint", "Preparing preview...", new Vector2(0, previewTop - previewH * 0.5f + 10f), new Vector2(innerW - 24f, 22), 12, FontStyles.Normal, TextAlignmentOptions.Center);
            CreateProxyProgress(new Vector2(0, previewTop - previewH * 0.5f - 18f), new Vector2(Mathf.Min(360f, innerW - 96f), 34f));

            float timeW = 112f;
            float sliderGap = 12f;
            float sliderW = innerW - timeW - sliderGap;
            _slider = CreateSlider("Scrub", new Vector2(-(timeW + sliderGap) * 0.5f, sliderY), new Vector2(sliderW, 22));
            _slider.onValueChanged.AddListener(OnSliderValueChanged);

            _timeText = CreateLabel("Time", "0.0s / 0.0s", new Vector2(sliderW * 0.5f + sliderGap * 0.5f, sliderY), new Vector2(timeW, 20), 10, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            _playButton = CreateButton("Play", "Play", new Vector2(left + 32f, controlsY), new Vector2(60, 26), TogglePlay);
            _playButtonLabel = _playButton != null ? _playButton.GetComponentInChildren<TextMeshProUGUI>() : null;

            CreateLabel("StartLabel", "Start", new Vector2(left + 112f, controlsY - 1f), new Vector2(48, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            _startField = CreateInput("StartInput", new Vector2(left + 170f, controlsY), new Vector2(62, 26), "0");
            if (_startField != null)
                _startField.onEndEdit.AddListener(OnStartFieldChanged);

            CreateLabel("FpsLabel", "FPS", new Vector2(left + 240f, controlsY - 1f), new Vector2(34, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            _fpsField = CreateInput("FpsInput", new Vector2(left + 292f, controlsY), new Vector2(58, 26), FormatNumber(_fps));
            if (_fpsField != null)
                _fpsField.onEndEdit.AddListener(OnFpsFieldChanged);

            CreateLabel("DurationLabel", "Duration", new Vector2(left + 42f, durationY - 1f), new Vector2(74, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _durationField = CreateInput("DurationInput", new Vector2(left + 124f, durationY), new Vector2(58, 26), FfmpegTool.DefaultClipDurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (_durationField != null)
                _durationField.onEndEdit.AddListener(OnDurationFieldChanged);
            CreateLabel("DurationUnit", "seconds", new Vector2(left + 184f, durationY - 1f), new Vector2(58, 22), 10, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            _duration3Button = CreateButton("Dur3", "3s", new Vector2(left + 250f, durationY), new Vector2(42, 26), () => SetDuration(3f));
            _duration5Button = CreateButton("Dur5", "5s", new Vector2(left + 298f, durationY), new Vector2(42, 26), () => SetDuration(5f));
            _duration8Button = CreateButton("Dur8", "8s", new Vector2(left + 346f, durationY), new Vector2(42, 26), () => SetDuration(8f));
            _includeAudioToggle = CreateToggle("IncludeAudio", "Audio", new Vector2(right - 36f, durationY), new Vector2(78f, 26f), _includeAudio, on =>
            {
                _includeAudio = on;
            });

            // "Import still" grabs the single frame at the current scrub position as a
            // still image and leaves the dialog open, so it sits left of Import Clip.
            if (_onImportStill != null)
                CreateButton("ImportStill", "Import still", new Vector2(right - 326f, actionY), new Vector2(118, 28), ImportStill);
            CreateButton("Import", _confirmText, new Vector2(right - 200f, actionY), new Vector2(118, 28), Confirm);
            CreateButton("Cancel", "Cancel", new Vector2(right - 82f, actionY), new Vector2(84, 28), Cancel);
            CreateResizeGrip();
            RefreshDurationControls();
            RefreshPlayButton();
        }

        private void PreparePreview()
        {
            try
            {
                ReleasePreviewPlayer();
                ReleasePreviewTexture();
                _player = gameObject.AddComponent<VideoPlayer>();
                _player.source = VideoSource.Url;
                _player.url = _previewSourcePath;
                _player.playOnAwake = false;
                _player.isLooping = true;
                _player.waitForFirstFrame = true;
                _player.audioOutputMode = VideoAudioOutputMode.None;
                _player.renderMode = VideoRenderMode.RenderTexture;

                Vector2Int rtSize = GetFittedPreviewTextureSize(
                    _info.Width > 0 ? _info.Width : 640,
                    _info.Height > 0 ? _info.Height : 360,
                    _info.RotationDegrees);
                _rt = CreatePreviewRenderTexture(rtSize);
                _player.targetTexture = _rt;
                if (_preview != null)
                {
                    _preview.texture = _rt;
                    _preview.color = Color.white;
                }

                _player.prepareCompleted += OnPrepared;
                _player.errorReceived += OnPreviewError;
                _player.Prepare();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ChatVideoClipChooser preview failed: " + ex.Message);
                OnPreviewError(_player, ex.Message);
            }
        }

        private void OnPrepared(VideoPlayer source)
        {
            _prepared = true;
            _proxyConversionInFlight = false;
            SyncPreviewTextureToPreparedVideo(source);
            if (_previewHint != null)
                _previewHint.gameObject.SetActive(false);
            SetProxyProgressVisible(false);
            try
            {
                source.time = _initialStartSeconds;
                source.Play();
                source.Pause();
            }
            catch { }
            _previewCurrentSeconds = _initialStartSeconds;
            SetStartSeconds(_selectedStartSeconds);
            SetSliderSeconds(_initialStartSeconds);
            RefreshPlayButton();
            UpdateTimeLabel();
        }

        private void OnPreviewError(VideoPlayer source, string message)
        {
            Debug.LogWarning("ChatVideoClipChooser preview error: " + message);
            if (_proxyTried || _proxyConversionInFlight)
            {
                if (_previewHint != null)
                {
                    _previewHint.text = "Could not preview this video.";
                    _previewHint.gameObject.SetActive(true);
                }
                SetProxyProgressVisible(false);
                return;
            }

            StartCoroutine(ConvertPreviewProxyAndRetry(message));
        }

        private void Update()
        {
            if (_player != null && _prepared && _info.DurationSeconds > 0)
            {
                _previewCurrentSeconds = ClampPreviewSeconds((float)_player.time);
                _ignoreSlider = true;
                _slider.value = Mathf.Clamp01(_previewCurrentSeconds / (float)_info.DurationSeconds);
                _ignoreSlider = false;
            }
            RefreshPlayButton();
            UpdateTimeLabel();
        }

        private void OnSliderValueChanged(float value)
        {
            if (_ignoreSlider || _player == null || _info.DurationSeconds <= 0) return;
            float t = ClampPreviewSeconds((float)(Mathf.Clamp01(value) * _info.DurationSeconds));
            _previewCurrentSeconds = t;
            try { _player.time = t; } catch { }
            SetStartSeconds(t);
            UpdateTimeLabel();
        }

        private void TogglePlay()
        {
            if (_proxyConversionInFlight || _player == null || !_prepared) return;
            if (_player.isPlaying) _player.Pause();
            else _player.Play();
            RefreshPlayButton();
        }

        private IEnumerator ConvertPreviewProxyAndRetry(string sourceError)
        {
            _proxyTried = true;
            _proxyConversionInFlight = true;
            _prepared = false;
            ReleasePreviewPlayer();
            SetProxyProgress(0f, "Converting preview... 0%");
            SetProxyProgressVisible(true);
            if (_previewHint != null)
            {
                _previewHint.text = "Preview failed in Windows; converting with FFmpeg...";
                _previewHint.gameObject.SetActive(true);
            }
            RefreshPlayButton();

            FfmpegTool.ClipResult result = null;
            _proxyCancelToken = new FfmpegTool.CancelToken();
            double proxyFps = _info != null && _info.Fps > 0 ? Math.Min(_info.Fps, 30) : 30;
            yield return FfmpegTool.CreatePreviewProxy(
                _sourcePath,
                _info != null ? _info.DurationSeconds : 0,
                proxyFps,
                r => result = r,
                (p, msg) => SetProxyProgress(p, msg),
                _proxyCancelToken);
            _proxyCancelToken = null;

            if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.OutputPath) || !System.IO.File.Exists(result.OutputPath))
            {
                _proxyConversionInFlight = false;
                string err = result != null ? result.Error : "unknown error";
                Debug.LogWarning("ChatVideoClipChooser preview proxy failed after VideoPlayer error: " + sourceError + "\n" + err);
                if (_previewHint != null)
                {
                    _previewHint.text = "Could not build a preview for this video.";
                    _previewHint.gameObject.SetActive(true);
                }
                SetProxyProgressVisible(false);
                RefreshPlayButton();
                yield break;
            }

            _previewProxyPath = result.OutputPath;
            _previewSourcePath = _previewProxyPath;
            SetProxyProgress(1f, "Preview ready");
            PreparePreview();
        }

        private void SetDuration(float seconds)
        {
            _duration = Mathf.Clamp(seconds, 0.1f, 60f);
            SetDurationFieldText(_duration);
            RefreshDurationControls();
        }

        private void OnDurationFieldChanged(string text)
        {
            if (_ignoreDurationField) return;
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                _duration = Mathf.Clamp(seconds, 0.1f, 60f);
            SetDurationFieldText(_duration);
            RefreshDurationControls();
        }

        private void OnFpsFieldChanged(string text)
        {
            if (_ignoreFpsField) return;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fps))
                _fps = ClampFps(fps);
            SetFpsFieldText(_fps);
        }

        private void OnStartFieldChanged(string text)
        {
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                SetStartSeconds(seconds);
            else
                SetStartSeconds(_selectedStartSeconds);

            SetSliderSeconds(_selectedStartSeconds);
            if (_player != null && _prepared)
            {
                try
                {
                    _player.time = _selectedStartSeconds;
                    _previewCurrentSeconds = _selectedStartSeconds;
                }
                catch { }
            }
            UpdateTimeLabel();
        }

        private void SetStartSeconds(float seconds)
        {
            _selectedStartSeconds = ClampStartSeconds(seconds);
            if (_startField != null)
                _startField.SetTextWithoutNotify(_selectedStartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        private void SetSliderSeconds(float seconds)
        {
            if (_slider == null || _info == null || _info.DurationSeconds <= 0) return;
            _ignoreSlider = true;
            _slider.value = Mathf.Clamp01(ClampStartSeconds(seconds) / (float)_info.DurationSeconds);
            _ignoreSlider = false;
        }

        private float GetCurrentPreviewSeconds()
        {
            if (_player != null && _prepared && _player.isPlaying)
                _previewCurrentSeconds = ClampPreviewSeconds((float)_player.time);
            if (_slider != null && _info.DurationSeconds > 0)
            {
                float sliderSeconds = ClampPreviewSeconds(_slider.value * (float)_info.DurationSeconds);
                if (_player == null || !_prepared || !_player.isPlaying)
                    _previewCurrentSeconds = sliderSeconds;
            }
            return _previewCurrentSeconds;
        }

        private float ClampStartSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                seconds = 0f;
            seconds = Mathf.Max(0f, seconds);
            if (_info != null && _info.DurationSeconds > 0)
                seconds = Mathf.Min(seconds, Mathf.Max(0f, (float)_info.DurationSeconds - 0.1f));
            return seconds;
        }

        private float ClampPreviewSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                seconds = 0f;
            seconds = Mathf.Max(0f, seconds);
            if (_info != null && _info.DurationSeconds > 0)
                seconds = Mathf.Min(seconds, (float)_info.DurationSeconds);
            return seconds;
        }

        private void Confirm()
        {
            float start = _selectedStartSeconds;
            if (_startField != null && _startField.isFocused
                && float.TryParse(_startField.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float editedStart))
            {
                start = ClampStartSeconds(editedStart);
                _selectedStartSeconds = start;
            }

            start = Mathf.Max(0f, start);
            if (_info.DurationSeconds > 0)
                start = Mathf.Min(start, Mathf.Max(0f, (float)_info.DurationSeconds - 0.1f));
            OnDurationFieldChanged(_durationField != null ? _durationField.text : null);
            OnFpsFieldChanged(_fpsField != null ? _fpsField.text : null);
            float dur = _duration;
            if (_info.DurationSeconds > 0)
                dur = Mathf.Clamp(dur, 0.1f, Mathf.Max(0.1f, (float)_info.DurationSeconds - start));

            var cb = _onConfirm;
            Destroy(gameObject);
            cb?.Invoke(new ClipSelection
            {
                StartSeconds = start,
                DurationSeconds = dur,
                Fps = _fps,
                IncludeAudio = _includeAudioToggle == null ? _includeAudio : _includeAudioToggle.isOn
            });
        }

        private void Cancel()
        {
            var cb = _onCancel;
            Destroy(gameObject);
            cb?.Invoke();
        }

        // Grab the frame at the current preview position. Deliberately does NOT close
        // the dialog so the user can scrub to another spot and import more stills.
        private void ImportStill()
        {
            float seconds = GetCurrentPreviewSeconds();
            _onImportStill?.Invoke(seconds);
        }

        private void RefreshPlayButton()
        {
            if (_playButtonLabel != null)
                _playButtonLabel.text = _proxyConversionInFlight ? "Wait" : (_player != null && _player.isPlaying ? "Pause" : "Play");
            if (_playButton != null)
                _playButton.interactable = !_proxyConversionInFlight && _prepared;
        }

        private void SetDurationFieldText(float seconds)
        {
            if (_durationField == null) return;
            _ignoreDurationField = true;
            _durationField.text = FormatNumber(seconds);
            _ignoreDurationField = false;
        }

        private void SetFpsFieldText(double fps)
        {
            if (_fpsField == null) return;
            _ignoreFpsField = true;
            _fpsField.text = FormatNumber(fps);
            _ignoreFpsField = false;
        }

        private void RefreshDurationControls()
        {
            SetDurationButtonActive(_duration3Button, Mathf.Abs(_duration - 3f) < 0.01f);
            SetDurationButtonActive(_duration5Button, Mathf.Abs(_duration - 5f) < 0.01f);
            SetDurationButtonActive(_duration8Button, Mathf.Abs(_duration - 8f) < 0.01f);
        }

        private static void SetDurationButtonActive(Button button, bool active)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            if (image == null) return;
            image.color = active
                ? new Color(0.25f, 0.52f, 0.90f, 1f)
                : new Color(0.18f, 0.24f, 0.32f, 1f);
        }

        private static double ClampFps(double fps)
        {
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
                return FfmpegTool.DefaultFps;
            return Math.Max(1, Math.Min(120, fps));
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static RectTransform ResolveDialogParent(RectTransform preferred)
        {
            Canvas canvas = GetOrCreateModalCanvas();
            if (canvas != null)
                return canvas.transform as RectTransform;

            var canvasGo = new GameObject(ModalCanvasName, typeof(RectTransform));
            canvas = canvasGo.AddComponent<Canvas>();
            ConfigureModalCanvas(canvas);
            return canvasGo.transform as RectTransform;
        }

        private static Canvas GetOrCreateModalCanvas()
        {
            foreach (var existing in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (existing == null || existing.gameObject == null) continue;
                if (existing.gameObject.name != ModalCanvasName) continue;
                if (!existing.gameObject.scene.IsValid()) continue;
                ConfigureModalCanvas(existing);
                return existing;
            }

            var canvasGo = new GameObject(ModalCanvasName, typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            ConfigureModalCanvas(canvas);
            return canvas;
        }

        private static void ConfigureModalCanvas(Canvas canvas)
        {
            if (canvas == null) return;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5000;
            canvas.gameObject.SetActive(true);

            var rt = canvas.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            canvas.transform.SetAsLastSibling();
        }

        private void OnDestroy()
        {
            if (_proxyCancelToken != null)
                _proxyCancelToken.Cancel();
            ReleasePreviewPlayer();
            ReleasePreviewTexture();
            if (!string.IsNullOrWhiteSpace(_previewProxyPath))
            {
                try { System.IO.File.Delete(_previewProxyPath); } catch { }
            }
        }

        private void ReleasePreviewPlayer()
        {
            if (_player == null) return;
            try { _player.Stop(); } catch { }
            _player.prepareCompleted -= OnPrepared;
            _player.errorReceived -= OnPreviewError;
            _player.targetTexture = null;
            Destroy(_player);
            _player = null;
        }

        private void ReleasePreviewTexture()
        {
            if (_preview != null)
                _preview.texture = null;
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        private RenderTexture CreatePreviewRenderTexture(Vector2Int size)
        {
            size.x = Mathf.Clamp(size.x, 16, 1920);
            size.y = Mathf.Clamp(size.y, 16, 1080);
            var texture = new RenderTexture(size.x, size.y, 0);
            texture.Create();
            return texture;
        }

        private void SyncPreviewTextureToPreparedVideo(VideoPlayer source)
        {
            if (source == null) return;
            int videoW = (int)source.width;
            int videoH = (int)source.height;
            if (videoW <= 0 || videoH <= 0) return;

            Vector2Int fitted = GetFittedPreviewTextureSize(videoW, videoH, 0);
            if (_rt == null || _rt.width != fitted.x || _rt.height != fitted.y)
            {
                if (_rt != null)
                {
                    source.targetTexture = null;
                    _rt.Release();
                    Destroy(_rt);
                }
                _rt = CreatePreviewRenderTexture(fitted);
                source.targetTexture = _rt;
                if (_preview != null)
                    _preview.texture = _rt;
            }

            ApplyPreviewAspectRatio(videoW / (float)videoH);
        }

        private Vector2Int GetFittedPreviewTextureSize(int width, int height, int rotationDegrees)
        {
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            int rotation = Mathf.Abs(rotationDegrees) % 180;
            if (rotation == 90)
            {
                int tmp = width;
                width = height;
                height = tmp;
            }

            float scale = Mathf.Min(1f, Mathf.Min(1920f / width, 1080f / height));
            int fittedW = Mathf.Max(16, Mathf.RoundToInt(width * scale));
            int fittedH = Mathf.Max(16, Mathf.RoundToInt(height * scale));
            return new Vector2Int(fittedW, fittedH);
        }

        private void ApplyPreviewAspectRatio(float aspect)
        {
            if (_preview == null) return;
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect))
                return;
            var fitter = _preview.GetComponent<AspectRatioFitter>();
            if (fitter != null)
                fitter.aspectRatio = Mathf.Clamp(aspect, 0.1f, 10f);
        }

        private TextMeshProUGUI CreateLabel(string name, string text, Vector2 anchored, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (_font != null) tmp.font = _font;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = new Color(0.12f, 0.12f, 0.15f);
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Button CreateButton(string name, string text, Vector2 anchored, Vector2 size, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.24f, 0.32f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var label = CreateChildText(go.transform, text, 11, FontStyles.Bold, TextAlignmentOptions.Center);
            label.color = Color.white;
            return btn;
        }

        private Slider CreateSlider(string name, Vector2 anchored, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 8f);
            bgRt.anchoredPosition = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = new Color(0.22f, 0.22f, 0.25f, 1f);
            bg.raycastTarget = false;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(-12f, 8f);
            fillAreaRt.anchoredPosition = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.52f, 0.90f, 1f);
            fillImg.raycastTarget = false;

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(6, 0);
            handleAreaRt.offsetMax = new Vector2(-6, 0);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRt = handle.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0.5f);
            handleRt.anchorMax = new Vector2(0f, 0.5f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(8, 18);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private TMP_InputField CreateInput(string name, Vector2 anchored, Vector2 size, string value)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
            var img = go.AddComponent<Image>();
            img.color = Color.white;

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            input.customCaretColor = true;
            input.caretColor = Color.black;
            input.selectionColor = new Color(0.25f, 0.52f, 0.90f, 0.55f);
            input.caretWidth = 5;
            input.caretBlinkRate = 0.6f;

            var viewportGo = new GameObject("Text Area");
            viewportGo.transform.SetParent(go.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(4, 0);
            viewportRt.offsetMax = new Vector2(-4, 0);
            viewportGo.AddComponent<RectMask2D>();

            var text = CreateChildText(viewportGo.transform, value, 11, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            text.margin = new Vector4(6, 0, 4, 0);
            text.color = Color.black;
            input.textViewport = viewportRt;
            input.textComponent = text;
            input.text = value;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            var caretFixer = go.AddComponent<global::AIChatCaretFixer>();
            caretFixer.Set(input);
            return input;
        }

        private void CreateProxyProgress(Vector2 anchored, Vector2 size)
        {
            var root = new GameObject("PreviewProxyProgress");
            root.transform.SetParent(transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.11f, 0.88f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(root.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _proxyProgressFill = fillGo.AddComponent<Image>();
            _proxyProgressFill.color = new Color(0.25f, 0.52f, 0.90f, 0.95f);
            _proxyProgressFill.raycastTarget = false;

            var label = CreateChildText(root.transform, "Converting preview...", 11, FontStyles.Bold, TextAlignmentOptions.Center);
            label.color = Color.white;
            _proxyProgressText = label;
            _proxyProgressRoot = root;
            SetProxyProgressVisible(false);
        }

        private void SetProxyProgressVisible(bool visible)
        {
            if (_proxyProgressRoot != null)
                _proxyProgressRoot.SetActive(visible);
            UpdateProxyProgressUi();
        }

        private void SetProxyProgress(float progress, string message)
        {
            _proxyProgress = Mathf.Clamp01(progress);
            if (!string.IsNullOrWhiteSpace(message))
                _proxyProgressMessage = message;
            UpdateProxyProgressUi();
        }

        private void UpdateProxyProgressUi()
        {
            if (_proxyProgressFill != null)
                _proxyProgressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(_proxyProgress), 1f);
            if (_proxyProgressText != null)
                _proxyProgressText.text = string.IsNullOrWhiteSpace(_proxyProgressMessage)
                    ? "Converting preview..."
                    : _proxyProgressMessage;
        }

        private Toggle CreateToggle(string name, string text, Vector2 anchored, Vector2 size, bool initialValue, Action<bool> onChanged)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;

            var boxGo = new GameObject("Box");
            boxGo.transform.SetParent(go.transform, false);
            var boxRt = boxGo.AddComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0f, 0.5f);
            boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.sizeDelta = new Vector2(16, 16);
            boxRt.anchoredPosition = new Vector2(0, 0);
            var boxImg = boxGo.AddComponent<Image>();
            boxImg.color = Color.white;

            var checkGo = new GameObject("Check");
            checkGo.transform.SetParent(boxGo.transform, false);
            var checkRt = checkGo.AddComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(3, 3);
            checkRt.offsetMax = new Vector2(-3, -3);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = new Color(0.25f, 0.52f, 0.90f, 1f);
            checkImg.raycastTarget = false;

            var label = CreateChildText(go.transform, text, 11, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.offsetMin = new Vector2(22, 0);
            labelRt.offsetMax = Vector2.zero;
            label.color = new Color(0.12f, 0.12f, 0.15f);

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = initialValue;
            toggle.onValueChanged.AddListener(on => onChanged?.Invoke(on));
            return toggle;
        }

        private void CreateDragHeader(float innerW)
        {
            var go = new GameObject("DragHeader");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(innerW, HeaderDragHeight);
            rt.anchoredPosition = new Vector2(0, -8f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true;
            var drag = go.AddComponent<global::PanelDragHandler>();
            drag.SetTarget(_root, HeaderDragHeight);
        }

        private void CreateResizeGrip()
        {
            var go = new GameObject("ResizeGrip");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(28f, 28f);
            rt.anchoredPosition = new Vector2(-6f, 6f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true;
            CreateGripLine(go.transform, "GripLineSmall", 9f);
            CreateGripLine(go.transform, "GripLineMedium", 15f);
            CreateGripLine(go.transform, "GripLineLarge", 21f);
            var grip = go.AddComponent<ResizeGripDragHandler>();
            grip.SetOwner(this);
        }

        private void CreateGripLine(Transform parent, string name, float offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(16f, 2f);
            rt.anchoredPosition = new Vector2(-offset, offset);
            rt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.27f, 0.34f, 0.95f);
            img.raycastTarget = false;
        }

        private void ResizeTo(Vector2 size, Vector2 anchoredPosition)
        {
            var parent = _root != null ? _root.parent as RectTransform : null;
            Vector2 parentSize = parent != null && parent.rect.size.x > 1f && parent.rect.size.y > 1f
                ? parent.rect.size
                : new Vector2(Screen.width, Screen.height);

            float maxW = Mathf.Min(MaxDialogWidth, Mathf.Max(MinDialogWidth, parentSize.x - 16f));
            float maxH = Mathf.Min(MaxDialogHeight, Mathf.Max(MinDialogHeight, parentSize.y - 16f));
            size.x = Mathf.Clamp(size.x, MinDialogWidth, maxW);
            size.y = Mathf.Clamp(size.y, MinDialogHeight, maxH);
            _root.sizeDelta = size;
            _root.anchoredPosition = global::PanelDragHandler.ClampAnchoredPosition(_root, anchoredPosition, HeaderDragHeight);
        }

        private void RebuildUiAfterResize()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            _preview = null;
            _previewHint = null;
            _slider = null;
            _timeText = null;
            _startField = null;
            _durationField = null;
            _fpsField = null;
            _includeAudioToggle = null;
            _playButton = null;
            _playButtonLabel = null;
            _proxyProgressRoot = null;
            _proxyProgressFill = null;
            _proxyProgressText = null;
            _duration3Button = null;
            _duration5Button = null;
            _duration8Button = null;

            BuildUI();
            if (_preview != null && _rt != null)
            {
                _preview.texture = _rt;
                _preview.color = Color.white;
            }
            if (_previewHint != null && _prepared)
                _previewHint.gameObject.SetActive(false);
            SetProxyProgressVisible(_proxyConversionInFlight);
            SetStartSeconds(_selectedStartSeconds);
            SetDurationFieldText(_duration);
            SetFpsFieldText(_fps);
            SetSliderSeconds(GetCurrentPreviewSeconds());
            RefreshPlayButton();
            UpdateTimeLabel();
        }

        private sealed class ResizeGripDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private ChatVideoClipChooser _owner;
            private RectTransform _target;
            private RectTransform _parent;
            private Vector2 _startPointerLocal;
            private Vector2 _startSize;
            private Vector2 _startAnchoredPosition;

            public void SetOwner(ChatVideoClipChooser owner)
            {
                _owner = owner;
                _target = owner != null ? owner._root : null;
                _parent = _target != null ? _target.parent as RectTransform : null;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_target == null || _parent == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out _startPointerLocal);
                _startSize = _target.sizeDelta;
                _startAnchoredPosition = _target.anchoredPosition;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_owner == null || _target == null || _parent == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parent,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 local);
                Vector2 delta = local - _startPointerLocal;
                float widthDelta = delta.x;
                float heightDelta = -delta.y;
                Vector2 newSize = new Vector2(_startSize.x + widthDelta, _startSize.y + heightDelta);
                Vector2 newPos = _startAnchoredPosition + new Vector2(widthDelta * 0.5f, -heightDelta * 0.5f);
                _owner.ResizeTo(newSize, newPos);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                _owner?.RebuildUiAfterResize();
            }
        }

        private TextMeshProUGUI CreateChildText(Transform parent, string text, float fontSize, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (_font != null) tmp.font = _font;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void UpdateTimeLabel()
        {
            if (_timeText == null) return;
            float cur = GetCurrentPreviewSeconds();
            _timeText.text = FormatTime(cur) + " / " + FormatTime((float)_info.DurationSeconds);
        }

        private float GetPreviewAspectRatio()
        {
            if (_rt != null && _rt.width > 0 && _rt.height > 0)
                return Mathf.Clamp(_rt.width / (float)_rt.height, 0.1f, 10f);
            if (_info != null && _info.Width > 0 && _info.Height > 0)
            {
                Vector2Int displaySize = GetFittedPreviewTextureSize(_info.Width, _info.Height, _info.RotationDegrees);
                return Mathf.Clamp(displaySize.x / (float)displaySize.y, 0.1f, 10f);
            }
            return 16f / 9f;
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0;
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m > 0 ? $"{m}:{s:00.0}" : $"{s:0.0}s";
        }
    }
}
