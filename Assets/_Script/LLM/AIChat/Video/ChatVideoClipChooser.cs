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
    /// Trim/export dialog for a local video: shown when a long video is dropped over AI
    /// Chat and from a movie pic's "Export movie or audio clip". A start/end marker pair
    /// picks the range; the Export buttons cut a video clip, an audio-only WAV, or a
    /// still frame from it and send each to the checked destinations ("Export to file" =
    /// the app's output folder, "Export to AI Chat" = a Movie/Audio/still bubble). The
    /// dialog stays open between exports so several things can be grabbed; Close ends it.
    /// Export work runs on GameLogic's coroutines so closing mid-export can't kill it.
    /// </summary>
    public class ChatVideoClipChooser : MonoBehaviour
    {
        private RectTransform _root;
        private TMP_FontAsset _font;
        private string _sourcePath;
        private string _previewSourcePath;
        private string _previewProxyPath;
        private string _titleText = "Export Video / Audio Clip";
        private FfmpegTool.VideoInfo _info;
        // Invoked by the Close button; the bool says whether anything was exported.
        // NOT invoked when the dialog is destroyed externally (chat Clear) - that
        // caller owns its own cleanup.
        private Action<bool> _onClose;

        private VideoPlayer _player;
        private AudioSource _audioSource;
        private RenderTexture _rt;
        private RawImage _preview;
        private TextMeshProUGUI _previewHint;
        private Slider _slider;
        private TextMeshProUGUI _timeText;
        private GameObject _proxyProgressRoot;
        private Image _proxyProgressFill;
        private TextMeshProUGUI _proxyProgressText;
        private TMP_InputField _startField;
        private TMP_InputField _endField;
        private TMP_InputField _durationField;
        private TMP_InputField _fpsField;
        private Toggle _includeAudioToggle;
        private Toggle _exportToFileToggle;
        private Toggle _exportToChatToggle;
        private Button _playButton;
        private TextMeshProUGUI _playButtonLabel;
        private Button _duration3Button;
        private Button _duration5Button;
        private Button _duration8Button;
        private RectTransform _markerArea;
        private RectTransform _rangeFill;
        private RectTransform _startMarker;
        private RectTransform _endMarker;
        private float _duration = FfmpegTool.DefaultClipDurationSeconds;
        private float _selectedStartSeconds;
        private float _selectedEndSeconds;
        private float _previewCurrentSeconds;
        private float _initialStartSeconds;
        private double _fps = FfmpegTool.DefaultFps;
        private bool _includeAudio = true;
        private bool _exportToFile;
        private bool _exportToChat = true;
        private bool _exportedAnything;
        private bool _exportBusy;
        private bool _prepared;
        private bool _proxyTried;
        private bool _proxyConversionInFlight;
        private bool _ignoreSlider;
        private bool _isScrubbing;
        private bool _isDraggingMarker;
        private bool _ignoreDurationField;
        private bool _ignoreFpsField;
        private float _proxyProgress;
        private string _proxyProgressMessage = "";
        private FfmpegTool.CancelToken _proxyCancelToken;

        // ---- Playback state. The single source of truth is _wantPlaying (the user's
        // intent); an Update-tick state machine reconciles the VideoPlayer toward it,
        // loops the selection, serializes seeks, and un-wedges Media Foundation. See
        // UpdatePlaybackStateMachine.
        private bool _wantPlaying;
        private bool _seekPending;
        private float _pendingSeekSeconds;
        private float _seekIssuedAt;
        private bool _hasQueuedSeek;
        private float _queuedSeekSeconds;
        private float _postSeekHoldUntil;
        private float _playIssuedAt;
        private float _lastObservedTime = -1f;
        private float _timeFrozenSince;
        private bool _loopArmed = true;
        private float _loopFiredClockTime = -999f;

        private const string ModalCanvasName = "VideoClipChooserCanvas";
        private const string PrefsExportToFile = "clipchooser_export_to_file";
        private const string PrefsExportToChat = "clipchooser_export_to_chat";
        private const float HeaderDragHeight = 58f;
        private const float MinDialogWidth = 500f;
        private const float MinDialogHeight = 500f;
        private const float MaxDialogWidth = 920f;
        private const float MaxDialogHeight = 790f;
        private const float MinClipSeconds = 0.1f;
        private const float SeekWatchdogSeconds = 1.5f;
        private const float PostSeekHoldSeconds = 0.35f;
        private const float FrozenClockKickSeconds = 0.8f;

        public sealed class ClipSelection
        {
            public float StartSeconds;
            public float DurationSeconds;
            public double Fps;
            public bool IncludeAudio;
            /// <summary>Also extract the selected range's audio as a WAV Audio bubble
            /// (automation import path; the dialog itself uses its Export audio button).</summary>
            public bool SaveAudioWav;
        }

        public static ChatVideoClipChooser Show(
            RectTransform parent,
            TMP_FontAsset font,
            string sourcePath,
            FfmpegTool.VideoInfo info,
            Action<bool> onClose = null,
            string titleText = "Export Video / Audio Clip",
            float initialStartSeconds = 0f)
        {
            RectTransform dialogParent = ResolveDialogParent(parent);
            var go = new GameObject("AIChatVideoClipChooser");
            go.transform.SetParent(dialogParent, false);
            var chooser = go.AddComponent<ChatVideoClipChooser>();
            chooser.Initialize(dialogParent, font, sourcePath, info, onClose, titleText, initialStartSeconds);
            return chooser;
        }

        private void Initialize(RectTransform parent, TMP_FontAsset font, string sourcePath, FfmpegTool.VideoInfo info, Action<bool> onClose, string titleText, float initialStartSeconds)
        {
            _font = font;
            _sourcePath = sourcePath;
            _previewSourcePath = sourcePath;
            _titleText = string.IsNullOrWhiteSpace(titleText) ? "Export Video / Audio Clip" : titleText;
            _info = info ?? new FfmpegTool.VideoInfo();
            _initialStartSeconds = ClampStartSeconds(initialStartSeconds);
            _selectedStartSeconds = _initialStartSeconds;
            _selectedEndSeconds = ClampEndSeconds(_initialStartSeconds + _duration);
            _duration = Mathf.Max(MinClipSeconds, _selectedEndSeconds - _selectedStartSeconds);
            _previewCurrentSeconds = _initialStartSeconds;
            _fps = _info.Fps > 0 ? ClampFps(_info.Fps) : FfmpegTool.DefaultFps;
            _includeAudio = true;
            _exportToFile = PlayerPrefs.GetInt(PrefsExportToFile, 0) != 0;
            _exportToChat = PlayerPrefs.GetInt(PrefsExportToChat, 1) != 0;
            _onClose = onClose;

            _root = gameObject.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            Vector2 parentSize = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);
            if (parentSize.x <= 1f || parentSize.y <= 1f)
                parentSize = new Vector2(Screen.width, Screen.height);
            float dialogW = parentSize.x > 0f ? Mathf.Clamp(parentSize.x - 32f, MinDialogWidth, 720f) : 660f;
            float dialogH = parentSize.y > 0f ? Mathf.Clamp(parentSize.y - 36f, MinDialogHeight, 620f) : 550f;
            _root.sizeDelta = new Vector2(dialogW, dialogH);
            _root.anchoredPosition = Vector2.zero;

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.94f, 0.94f, 0.96f, 0.98f);

            BuildUI();
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
            float previewH = Mathf.Clamp(dialogH - 262f, 220f, 360f);
            float previewTop = -74f;
            float sliderY = previewTop - previewH - 22f;
            float controlsY = sliderY - 32f;
            float durationY = controlsY - 33f;
            float destY = durationY - 32f;
            float actionY = destY - 36f;
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
            _startField = CreateInput("StartInput", new Vector2(left + 170f, controlsY), new Vector2(62, 26), FormatNumber(_selectedStartSeconds));
            if (_startField != null)
                _startField.onEndEdit.AddListener(OnStartFieldChanged);

            CreateLabel("EndLabel", "End", new Vector2(left + 224f, controlsY - 1f), new Vector2(36, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            _endField = CreateInput("EndInput", new Vector2(left + 274f, controlsY), new Vector2(62, 26), FormatNumber(_selectedEndSeconds));
            if (_endField != null)
                _endField.onEndEdit.AddListener(OnEndFieldChanged);

            CreateLabel("FpsLabel", "FPS", new Vector2(left + 336f, controlsY - 1f), new Vector2(34, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            _fpsField = CreateInput("FpsInput", new Vector2(left + 388f, controlsY), new Vector2(58, 26), FormatNumber(_fps));
            if (_fpsField != null)
                _fpsField.onEndEdit.AddListener(OnFpsFieldChanged);

            CreateLabel("DurationLabel", "Duration", new Vector2(left + 42f, durationY - 1f), new Vector2(74, 22), 11, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _durationField = CreateInput("DurationInput", new Vector2(left + 124f, durationY), new Vector2(58, 26), FormatNumber(_duration));
            if (_durationField != null)
                _durationField.onEndEdit.AddListener(OnDurationFieldChanged);
            CreateLabel("DurationUnit", "seconds", new Vector2(left + 184f, durationY - 1f), new Vector2(58, 22), 10, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            _duration3Button = CreateButton("Dur3", "3s", new Vector2(left + 250f, durationY), new Vector2(42, 26), () => SetDuration(3f));
            _duration5Button = CreateButton("Dur5", "5s", new Vector2(left + 298f, durationY), new Vector2(42, 26), () => SetDuration(5f));
            _duration8Button = CreateButton("Dur8", "8s", new Vector2(left + 346f, durationY), new Vector2(42, 26), () => SetDuration(8f));
            _includeAudioToggle = CreateToggle("IncludeAudio", "Audio", new Vector2(right - 36f, durationY), new Vector2(78f, 26f), _includeAudio, on =>
            {
                _includeAudio = on;
                ApplyPreviewAudioSettings();
            });

            // Destinations for every Export button; remembered across sessions.
            _exportToFileToggle = CreateToggle("ExportToFile", "Export to file", new Vector2(left + 66f, destY), new Vector2(132f, 26f), _exportToFile, on =>
            {
                _exportToFile = on;
                PlayerPrefs.SetInt(PrefsExportToFile, on ? 1 : 0);
                PlayerPrefs.Save();
            });
            _exportToChatToggle = CreateToggle("ExportToChat", "Export to AI Chat", new Vector2(left + 240f, destY), new Vector2(146f, 26f), _exportToChat, on =>
            {
                _exportToChat = on;
                PlayerPrefs.SetInt(PrefsExportToChat, on ? 1 : 0);
                PlayerPrefs.Save();
            });

            // Every Export button leaves the dialog open so several clips/stills can be
            // grabbed from one source; Close ends the session.
            CreateButton("ExportStill", "Export still", new Vector2(right - 364f, actionY), new Vector2(96, 28), ExportStill);
            if (_info.HasAudio)
                CreateButton("ExportAudio", "Export audio clip", new Vector2(right - 252f, actionY), new Vector2(116, 28), ExportAudioClip);
            CreateButton("ExportVideo", "Export video clip", new Vector2(right - 130f, actionY), new Vector2(116, 28), ExportVideoClip);
            CreateButton("Close", "Close", new Vector2(right - 36f, actionY), new Vector2(64, 28), Close);
            CreateResizeGrip();
            RefreshRangeUi();
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
                // Looping is handled by the playback state machine (it loops the
                // selected start..end range, not the whole file).
                _player.isLooping = false;
                _player.waitForFirstFrame = true;
                // Audio goes through an AudioSource, not Direct — Direct mode desyncs
                // the audio track on the first play of a clip under Media Foundation
                // (see PicMovie). DSPTime + skipOnDrop=false is part of the same fix.
                if (_audioSource == null)
                {
                    _audioSource = gameObject.GetComponent<AudioSource>();
                    if (_audioSource == null)
                        _audioSource = gameObject.AddComponent<AudioSource>();
                }
                _audioSource.playOnAwake = false;
                _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _player.controlledAudioTrackCount = 1;
                _player.EnableAudioTrack(0, true);
                _player.SetTargetAudioSource(0, _audioSource);
                _player.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
                _player.skipOnDrop = false;
                ApplyPreviewAudioSettings();
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
            source.seekCompleted -= OnSeekCompleted;
            source.seekCompleted += OnSeekCompleted;
            // Prime: without one Play/Pause the first frame never lands in the RT.
            try
            {
                source.Play();
                source.Pause();
            }
            catch { }
            // Seek to the requested start; skip the no-op seek at 0 (Media Foundation
            // can fail to complete a seek to the position it is already at).
            if (_initialStartSeconds > 0.05f)
            {
                SeekTo(_initialStartSeconds);
            }
            else
            {
                _previewCurrentSeconds = 0f;
                SetSliderSeconds(0f);
            }
            RefreshRangeUi();
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

        // ---- Playback state machine ------------------------------------------------
        //
        // Rules distilled from PicMovie's Media Foundation battles:
        //  - Seeks are SERIALIZED; a seek requested during a pending one queues the
        //    newest target (overlapping time= assignments wedge MF).
        //  - Play/Pause is reconciled toward _wantPlaying from the Update tick, never
        //    inside seekCompleted.
        //  - seekCompleted can be silently dropped; a watchdog force-clears the pending
        //    flag so the dialog never wedges.
        //  - A frozen clock while isPlaying reads true is kicked by RE-SEEKING with a
        //    tiny nudge (Pause+Play does not unfreeze MF).
        //  - The slider shows the seek target plus a short post-seek hold, never raw
        //    live time, which reports stale values for a few frames after a seek.

        private void Update()
        {
            ApplyPreviewAudioSettings();
            UpdatePlaybackStateMachine();

            if (_prepared && _player != null && !_seekPending && !_isScrubbing && !_isDraggingMarker
                && _info.DurationSeconds > 0 && Time.unscaledTime >= _postSeekHoldUntil)
            {
                _previewCurrentSeconds = ClampPreviewSeconds((float)_player.time);
                SetSliderSeconds(_previewCurrentSeconds);
            }
            RefreshPlayButton();
            UpdateTimeLabel();
        }

        private void UpdatePlaybackStateMachine()
        {
            if (_player == null || !_prepared) return;
            float now = Time.unscaledTime;

            if (_seekPending)
            {
                if (now - _seekIssuedAt > SeekWatchdogSeconds)
                {
                    _seekPending = false;
                    _postSeekHoldUntil = now + PostSeekHoldSeconds;
                }
                else
                {
                    return;
                }
            }

            if (_hasQueuedSeek)
            {
                _hasQueuedSeek = false;
                IssueSeek(_queuedSeekSeconds);
                return;
            }

            bool shouldPlay = _wantPlaying && !_isScrubbing && !_isDraggingMarker;
            float time = (float)_player.time;
            float loopEps = GetLoopEndEpsilon();

            if (!shouldPlay)
            {
                if (_player.isPlaying)
                {
                    try { _player.Pause(); } catch { }
                }
                _lastObservedTime = -1f; // restart the progress watch on the next play
                return;
            }

            // Re-arm the loop once the clock has left the boundary (back at the start
            // on success, or drifted elsewhere after a silently failed seek).
            if (!_loopArmed && (time < _selectedEndSeconds - loopEps - 0.05f
                                || Mathf.Abs(time - _loopFiredClockTime) > 0.3f))
                _loopArmed = true;

            // Loop the selection: hitting the end marker jumps back to the start
            // marker. Fires ONCE per boundary hit - re-seeking every tick while the
            // clock sits at the boundary is a seek storm, and seek storms wedge Media
            // Foundation (observed: clock frozen at the last in-range frame with
            // isPlaying true while every seek "completed" without moving it). Pause
            // first: pause -> single seek -> tick-resume is the proven-safe sequence.
            if (_loopArmed && time >= _selectedEndSeconds - loopEps)
            {
                _loopArmed = false;
                _loopFiredClockTime = time;
                if (_player.isPlaying)
                {
                    try { _player.Pause(); } catch { }
                }
                SeekTo(_selectedStartSeconds);
                return;
            }

            // Play() is async and isPlaying lags it; re-issuing it every frame keeps
            // restarting the pipeline and the clock never moves. Issue it sparingly and
            // let the progress watch below handle a start that doesn't take.
            if (!_player.isPlaying && now - _playIssuedAt > 0.3f)
            {
                try { _player.Play(); } catch { }
                _playIssuedAt = now;
            }

            // Progress watch: whatever isPlaying claims (MF lies in both directions), a
            // clock that hasn't moved gets kicked by RE-SEEKING with a tiny nudge - the
            // only unfreeze that works (Pause+Play does not; see PicMovie). A clock
            // frozen at/past the end marker is kicked to the START marker instead of
            // forward, so a wedged loop jump gets retried.
            if (_lastObservedTime < 0f || Mathf.Abs(time - _lastObservedTime) > 0.0005f)
            {
                _lastObservedTime = time;
                _timeFrozenSince = now;
            }
            else if (now - _timeFrozenSince > FrozenClockKickSeconds && now >= _postSeekHoldUntil)
            {
                _timeFrozenSince = now;
                float kickTarget = time >= _selectedEndSeconds - loopEps
                    ? _selectedStartSeconds
                    : time + 0.05f;
                SeekTo(ClampPreviewSeconds(kickTarget));
            }
        }

        // One source video frame plus margin: the last displayed frame before the end
        // marker must already trigger the loop.
        private float GetLoopEndEpsilon()
        {
            float fps = _player != null && _player.frameRate > 0.01f
                ? _player.frameRate
                : (_info != null && _info.Fps > 0 ? (float)_info.Fps : FfmpegTool.DefaultFps);
            return Mathf.Clamp(1.2f / Mathf.Max(1f, fps), 0.03f, 0.15f);
        }

        private void SeekTo(float seconds)
        {
            if (_player == null || !_prepared) return;
            seconds = ClampPreviewSeconds(seconds);
            _previewCurrentSeconds = seconds;
            if (_seekPending)
            {
                _queuedSeekSeconds = seconds; // newest target wins
                _hasQueuedSeek = true;
                SetSliderSeconds(seconds);
                return;
            }
            IssueSeek(seconds);
        }

        private void IssueSeek(float seconds)
        {
            _seekPending = true;
            _pendingSeekSeconds = seconds;
            _seekIssuedAt = Time.unscaledTime;
            _previewCurrentSeconds = seconds;
            SetSliderSeconds(seconds);
            try
            {
                _player.time = seconds;
            }
            catch
            {
                _seekPending = false;
            }
        }

        private void OnSeekCompleted(VideoPlayer source)
        {
            if (source == null || source != _player || !_seekPending) return;
            _seekPending = false;
            _postSeekHoldUntil = Time.unscaledTime + PostSeekHoldSeconds;
            _previewCurrentSeconds = _pendingSeekSeconds;
            SetSliderSeconds(_pendingSeekSeconds);
            _lastObservedTime = -1f;
            _timeFrozenSince = Time.unscaledTime;
            UpdateTimeLabel();
        }

        private void TogglePlay()
        {
            if (_proxyConversionInFlight || _player == null || !_prepared) return;
            _wantPlaying = !_wantPlaying;
            if (_wantPlaying)
            {
                // Start inside the selection; outside it, jump to the start marker.
                float t = _previewCurrentSeconds;
                if (t < _selectedStartSeconds - 0.05f || t >= _selectedEndSeconds - 0.05f)
                    SeekTo(_selectedStartSeconds);
            }
            RefreshPlayButton();
        }

        // ---- Scrubbing (playhead) --------------------------------------------------
        // A plain CLICK on the bar seeks in place without pausing; a DRAG holds the
        // player paused and seeks once on release.

        private void OnSliderValueChanged(float value)
        {
            if (_ignoreSlider || _player == null || _info.DurationSeconds <= 0) return;
            float t = ClampPreviewSeconds((float)(Mathf.Clamp01(value) * _info.DurationSeconds));
            _previewCurrentSeconds = t;
            if (!_isScrubbing)
                SeekTo(t);
            UpdateTimeLabel();
        }

        private void BeginSliderScrub()
        {
            _isScrubbing = true; // the state machine pauses playback on the next tick
        }

        private void EndSliderScrub()
        {
            if (!_isScrubbing) return;
            _isScrubbing = false;
            float t = _slider != null && _info != null && _info.DurationSeconds > 0
                ? ClampPreviewSeconds(_slider.value * (float)_info.DurationSeconds)
                : _previewCurrentSeconds;
            SeekTo(t);
            UpdateTimeLabel();
        }

        // ---- Trim markers (start/end) ----------------------------------------------
        // A drag holds playback paused and seeks once on release, landing the preview
        // on the frame the marker points at; a plain click just seeks to that marker.

        private void DragMarkerToFraction(bool isEndMarker, float fraction)
        {
            if (_info == null || _info.DurationSeconds <= 0) return;
            _isDraggingMarker = true; // idempotent; the state machine holds pause
            float t = Mathf.Clamp01(fraction) * (float)_info.DurationSeconds;
            ApplyRangeEdit(isEndMarker ? RangeEdit.End : RangeEdit.StartKeepEnd, t);
            UpdateTimeLabel();
        }

        private void FinishMarkerInteraction(bool isEndMarker)
        {
            bool wasDragging = _isDraggingMarker;
            _isDraggingMarker = false;

            // Alt+click a marker: snap the MARKER to the current playhead position
            // (clamped by the usual range rules) instead of seeking the playhead to
            // the marker.
            if (!wasDragging && IsAltHeld())
            {
                ApplyRangeEdit(isEndMarker ? RangeEdit.End : RangeEdit.StartKeepEnd, _previewCurrentSeconds);
                UpdateTimeLabel();
                return;
            }

            SeekTo(isEndMarker ? _selectedEndSeconds : _selectedStartSeconds);
            UpdateTimeLabel();
        }

        // The bridge flag lets scripted tests exercise alt-clicks: the editor may lack
        // OS keyboard focus during automation, so real Alt key state is unreliable.
        private static bool IsAltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                || global::AutomationBridge.SyntheticAltHeld;
        }

        private float FractionFromPointer(PointerEventData eventData)
        {
            if (_markerArea == null) return 0f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, eventData.position, eventData.pressEventCamera, out Vector2 local);
            Rect r = _markerArea.rect;
            return r.width > 0f ? Mathf.Clamp01((local.x - r.xMin) / r.width) : 0f;
        }

        // Preview audio follows the Audio toggle (what you hear is what the exported
        // video clip will keep) plus the app's global mute and clip volume settings.
        private void ApplyPreviewAudioSettings()
        {
            if (_audioSource == null) return;
            var gameLogic = global::GameLogic.Get();
            _audioSource.volume = gameLogic != null ? gameLogic.GetClipVolume() : 1f;
            _audioSource.mute = !_includeAudio || (gameLogic != null && gameLogic.GetGlobalMute());
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
            var cancelToken = new FfmpegTool.CancelToken();
            _proxyCancelToken = cancelToken;
            double proxyFps = _info != null && _info.Fps > 0 ? Math.Min(_info.Fps, 30) : 30;
            double sourceDuration = _info != null ? _info.DurationSeconds : 0;
            yield return FfmpegTool.CreatePreviewProxy(
                _sourcePath,
                sourceDuration,
                proxyFps,
                r => result = r,
                (p, msg) => SetProxyProgress(p, msg),
                cancelToken,
                includeAudio: true);

            if (!cancelToken.CancelRequested
                && (result == null || !result.Success || string.IsNullOrWhiteSpace(result.OutputPath) || !System.IO.File.Exists(result.OutputPath)))
            {
                Debug.LogWarning("ChatVideoClipChooser audio preview proxy failed; retrying without audio for " + _sourcePath);
                SetProxyProgress(0f, "Retrying conversion without audio...");
                result = null;
                yield return FfmpegTool.CreatePreviewProxy(
                    _sourcePath,
                    sourceDuration,
                    proxyFps,
                    r => result = r,
                    (p, msg) => SetProxyProgress(p, msg),
                    cancelToken);
            }
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

        private enum RangeEdit
        {
            /// <summary>Move the start, dragging the end along to keep the clip length (typed Start field).</summary>
            StartKeepDuration,
            /// <summary>Trim the start against a fixed end (start marker drag).</summary>
            StartKeepEnd,
            /// <summary>Move the end; start stays put (end marker drag / End field).</summary>
            End,
            /// <summary>Set the clip length from the start (Duration field / 3s-5s-8s buttons).</summary>
            Duration
        }

        // The one place the trim range moves; keeps start < end and the clip inside the
        // source (any length down to 0.1s - no max), then refreshes every control that
        // displays the range.
        private void ApplyRangeEdit(RangeEdit kind, float value)
        {
            float start = _selectedStartSeconds;
            float end = _selectedEndSeconds;
            switch (kind)
            {
                case RangeEdit.StartKeepDuration:
                {
                    float dur = Mathf.Max(end - start, MinClipSeconds);
                    start = ClampStartSeconds(value);
                    end = ClampEndSeconds(start + dur);
                    start = Mathf.Min(start, end - MinClipSeconds);
                    break;
                }
                case RangeEdit.StartKeepEnd:
                    start = Mathf.Min(ClampStartSeconds(value), end - MinClipSeconds);
                    break;
                case RangeEdit.End:
                    end = Mathf.Max(ClampEndSeconds(value), start + MinClipSeconds);
                    break;
                case RangeEdit.Duration:
                    end = Mathf.Max(start + MinClipSeconds, ClampEndSeconds(start + Mathf.Max(value, MinClipSeconds)));
                    break;
            }

            _selectedStartSeconds = Mathf.Max(0f, start);
            _selectedEndSeconds = Mathf.Max(_selectedStartSeconds + MinClipSeconds * 0.5f, end);
            _duration = _selectedEndSeconds - _selectedStartSeconds;
            RefreshRangeUi();
        }

        private void RefreshRangeUi()
        {
            if (_startField != null)
                _startField.SetTextWithoutNotify(FormatNumber(_selectedStartSeconds));
            if (_endField != null)
                _endField.SetTextWithoutNotify(FormatNumber(_selectedEndSeconds));
            SetDurationFieldText(_duration);
            RefreshDurationControls();
            UpdateRangeMarkerVisuals();
        }

        private void UpdateRangeMarkerVisuals()
        {
            if (_markerArea == null) return;
            float dur = _info != null && _info.DurationSeconds > 0 ? (float)_info.DurationSeconds : 0f;
            float f0 = dur > 0 ? Mathf.Clamp01(_selectedStartSeconds / dur) : 0f;
            float f1 = dur > 0 ? Mathf.Clamp01(_selectedEndSeconds / dur) : 1f;
            if (_startMarker != null)
            {
                _startMarker.anchorMin = new Vector2(f0, 0f);
                _startMarker.anchorMax = new Vector2(f0, 0.5f);
            }
            if (_endMarker != null)
            {
                _endMarker.anchorMin = new Vector2(f1, 0.5f);
                _endMarker.anchorMax = new Vector2(f1, 1f);
            }
            if (_rangeFill != null)
            {
                _rangeFill.anchorMin = new Vector2(f0, 0.5f);
                _rangeFill.anchorMax = new Vector2(f1, 0.5f);
                _rangeFill.offsetMin = new Vector2(0f, -4f);
                _rangeFill.offsetMax = new Vector2(0f, 4f);
            }
        }

        private void SetDuration(float seconds)
        {
            ApplyRangeEdit(RangeEdit.Duration, seconds);
        }

        private void OnDurationFieldChanged(string text)
        {
            if (_ignoreDurationField) return;
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                ApplyRangeEdit(RangeEdit.Duration, seconds);
            else
                RefreshRangeUi();
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
                ApplyRangeEdit(RangeEdit.StartKeepDuration, seconds);
            else
                RefreshRangeUi();

            SeekPreviewToRangePoint(_selectedStartSeconds);
        }

        private void OnEndFieldChanged(string text)
        {
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                ApplyRangeEdit(RangeEdit.End, seconds);
            else
                RefreshRangeUi();

            SeekPreviewToRangePoint(_selectedEndSeconds);
        }

        // Jump the preview to a just-edited range endpoint so the user sees that frame.
        private void SeekPreviewToRangePoint(float seconds)
        {
            if (_player != null && _prepared)
                SeekTo(seconds);
            else
                SetSliderSeconds(seconds);
            UpdateTimeLabel();
        }

        private void SetSliderSeconds(float seconds)
        {
            if (_slider == null || _info == null || _info.DurationSeconds <= 0) return;
            _ignoreSlider = true;
            _slider.value = Mathf.Clamp01(ClampPreviewSeconds(seconds) / (float)_info.DurationSeconds);
            _ignoreSlider = false;
        }

        private float GetCurrentPreviewSeconds()
        {
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

        private float ClampEndSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                seconds = MinClipSeconds;
            seconds = Mathf.Max(MinClipSeconds, seconds);
            if (_info != null && _info.DurationSeconds > 0)
                seconds = Mathf.Min(seconds, (float)_info.DurationSeconds);
            return seconds;
        }

        // ---- Exports ---------------------------------------------------------------
        // Each button cuts from the ORIGINAL source and delivers to the checked
        // destinations. The work runs on GameLogic's coroutines so closing the dialog
        // mid-export cannot kill it; the routines only touch locals, statics, and
        // plain fields afterwards (never Unity-side members of this destroyed object).

        private ClipSelection BuildSelection()
        {
            // Commit any still-focused numeric field the way its onEndEdit would.
            if (_startField != null && _startField.isFocused
                && float.TryParse(_startField.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float editedStart))
                ApplyRangeEdit(RangeEdit.StartKeepDuration, editedStart);
            if (_endField != null && _endField.isFocused
                && float.TryParse(_endField.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float editedEnd))
                ApplyRangeEdit(RangeEdit.End, editedEnd);
            if (_durationField != null && _durationField.isFocused)
                OnDurationFieldChanged(_durationField.text);
            OnFpsFieldChanged(_fpsField != null ? _fpsField.text : null);

            float start = Mathf.Max(0f, _selectedStartSeconds);
            if (_info.DurationSeconds > 0)
                start = Mathf.Min(start, Mathf.Max(0f, (float)_info.DurationSeconds - 0.1f));
            float dur = Mathf.Max(MinClipSeconds, _selectedEndSeconds - start);
            if (_info.DurationSeconds > 0)
                dur = Mathf.Clamp(dur, MinClipSeconds, Mathf.Max(MinClipSeconds, (float)_info.DurationSeconds - start));

            return new ClipSelection
            {
                StartSeconds = start,
                DurationSeconds = dur,
                Fps = _fps,
                IncludeAudio = _includeAudioToggle == null ? _includeAudio : _includeAudioToggle.isOn
            };
        }

        private bool BeginExport(out bool toFile, out bool toChat)
        {
            toFile = _exportToFile;
            toChat = _exportToChat;
            if (!toFile && !toChat)
            {
                ShowToast("Check 'Export to file' and/or 'Export to AI Chat' first");
                return false;
            }
            if (_exportBusy)
            {
                ShowToast("Still working on the previous export...");
                return false;
            }
            _exportBusy = true;
            _exportedAnything = true;
            return true;
        }

        private static MonoBehaviour GetExportRunner(MonoBehaviour fallback)
        {
            var gameLogic = global::GameLogic.Get();
            return gameLogic != null ? (MonoBehaviour)gameLogic : fallback;
        }

        private static void ShowToast(string message)
        {
            global::RTQuickMessageManager.Get().ShowMessage(message);
        }

        private void ExportVideoClip()
        {
            if (!BeginExport(out bool toFile, out bool toChat)) return;
            GetExportRunner(this).StartCoroutine(ExportVideoClipRoutine(_sourcePath, BuildSelection(), toFile, toChat));
        }

        private IEnumerator ExportVideoClipRoutine(string sourcePath, ClipSelection sel, bool toFile, bool toChat)
        {
            ShowToast("Exporting video clip...");
            string outputPath = FfmpegTool.GetClipOutputPath(sourcePath);
            FfmpegTool.ClipResult result = null;
            yield return FfmpegTool.CreateClip(sourcePath, sel.StartSeconds, sel.DurationSeconds, outputPath, r => result = r,
                fps: sel.Fps, includeAudio: sel.IncludeAudio);
            if (result == null || !result.Success)
            {
                ShowToast("Could not export video clip: " + (result != null ? result.Error : "unknown error"));
                _exportBusy = false;
                yield break;
            }

            string dims = null;
            if (toChat)
            {
                FfmpegTool.VideoInfo outInfo = null;
                yield return FfmpegTool.ProbeVideo(result.OutputPath, (i, e) => outInfo = i);
                if (outInfo != null && outInfo.Width > 0 && outInfo.Height > 0)
                {
                    dims = $"{outInfo.Width}x{outInfo.Height}";
                    if (outInfo.Fps > 0)
                        dims += $" @{outInfo.Fps:0.##}fps";
                }
            }

            if (toFile)
            {
                if (TryCopyToOutputFolder(result.OutputPath, out string savedPath, out string fileError))
                    ShowToast("Saved " + global::Config._saveDirName + "/" + System.IO.Path.GetFileName(savedPath));
                else
                    ShowToast("Could not save video clip: " + fileError);
            }
            if (toChat)
            {
                if (global::AIChatPanel.AddLocalMovieClipToChat(result.OutputPath, dims, out string chatError))
                    ShowToast("Added video clip to AI Chat");
                else
                    ShowToast("Could not add clip to AI Chat: " + chatError);
            }
            _exportBusy = false;
        }

        private void ExportAudioClip()
        {
            if (!BeginExport(out bool toFile, out bool toChat)) return;
            var sel = BuildSelection();
            GetExportRunner(this).StartCoroutine(ExportAudioClipRoutine(_sourcePath, sel.StartSeconds, sel.DurationSeconds, toFile, toChat));
        }

        private IEnumerator ExportAudioClipRoutine(string sourcePath, float startSeconds, float durationSeconds, bool toFile, bool toChat)
        {
            ShowToast("Exporting audio clip...");
            if (toFile)
            {
                string wavPath = FfmpegTool.GetExtractedAudioWavPath(sourcePath);
                FfmpegTool.ClipResult wav = null;
                yield return FfmpegTool.ExtractAudioWavSection(sourcePath, startSeconds, durationSeconds, wavPath, r => wav = r);
                if (wav == null || !wav.Success)
                    ShowToast("Could not export audio clip: " + (wav != null ? wav.Error : "unknown error"));
                else if (TryCopyToOutputFolder(wavPath, out string savedPath, out string fileError))
                    ShowToast("Saved " + global::Config._saveDirName + "/" + System.IO.Path.GetFileName(savedPath));
                else
                    ShowToast("Could not save audio clip: " + fileError);
            }
            if (toChat)
            {
                // Extracts its own WAV from the source and lands it as an Audio bubble.
                if (global::AIChatPanel.AddLocalClipAudioToChat(sourcePath, startSeconds, durationSeconds, out string chatError))
                    ShowToast("Added audio clip to AI Chat");
                else
                    ShowToast("Could not add audio to AI Chat: " + chatError);
            }
            _exportBusy = false;
        }

        private void ExportStill()
        {
            if (!BeginExport(out bool toFile, out bool toChat)) return;
            string dims = _info != null && _info.Width > 0 && _info.Height > 0 ? $"{_info.Width}x{_info.Height}" : null;
            GetExportRunner(this).StartCoroutine(ExportStillRoutine(_sourcePath, GetCurrentPreviewSeconds(), dims, toFile, toChat));
        }

        private IEnumerator ExportStillRoutine(string sourcePath, float atSeconds, string dims, bool toFile, bool toChat)
        {
            ShowToast("Exporting still...");
            if (toFile)
            {
                string pngPath = FfmpegTool.GetStillFrameOutputPath(sourcePath);
                FfmpegTool.ClipResult still = null;
                yield return FfmpegTool.ExtractStillFrame(sourcePath, atSeconds, pngPath, r => still = r);
                if (still == null || !still.Success)
                    ShowToast("Could not export still: " + (still != null ? still.Error : "unknown error"));
                else if (TryCopyToOutputFolder(pngPath, out string savedPath, out string fileError))
                    ShowToast("Saved " + global::Config._saveDirName + "/" + System.IO.Path.GetFileName(savedPath));
                else
                    ShowToast("Could not save still: " + fileError);
            }
            if (toChat)
            {
                // Extracts the frame itself from the source at native resolution.
                if (global::AIChatPanel.AddLocalStillFrameToChat(sourcePath, atSeconds, dims, out string chatError))
                    ShowToast("Added still to AI Chat");
                else
                    ShowToast("Could not add still to AI Chat: " + chatError);
            }
            _exportBusy = false;
        }

        private static bool TryCopyToOutputFolder(string srcPath, out string savedPath, out string error)
        {
            savedPath = null;
            error = null;
            try
            {
                var config = global::Config.Get();
                if (config == null)
                {
                    error = "no Config instance";
                    return false;
                }
                string dir = config.GetBaseFileDir("/" + global::Config._saveDirName) + "/";
                System.IO.Directory.CreateDirectory(dir);
                savedPath = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(srcPath));
                System.IO.File.Copy(srcPath, savedPath, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void Close()
        {
            var cb = _onClose;
            bool exported = _exportedAnything;
            Destroy(gameObject);
            cb?.Invoke(exported);
        }

        private void RefreshPlayButton()
        {
            if (_playButtonLabel != null)
                _playButtonLabel.text = _proxyConversionInFlight ? "Wait" : (_wantPlaying ? "Pause" : "Play");
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
            _seekPending = false;
            _hasQueuedSeek = false;
            _wantPlaying = false;
            _loopArmed = true;
            if (_player == null) return;
            try { _player.Stop(); } catch { }
            _player.prepareCompleted -= OnPrepared;
            _player.seekCompleted -= OnSeekCompleted;
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
            // Raycastable so clicking anywhere on the bar jumps the playhead there
            // (the Slider jumps its value on pointer down over any raycast child).
            bg.raycastTarget = true;

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

            // Detects real drags (begin/end) so a drag pauses playback and seeks once
            // on release, while a plain click seeks in place without pausing.
            var scrubHandler = go.AddComponent<SliderScrubHandler>();
            scrubHandler.SetOwner(this);
            var slider = go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            // Trim range lives in the same handle-slide space as the playhead so one
            // 0..1 fraction maps the playhead, the highlight, and both markers alike.
            _markerArea = handleAreaRt;
            _rangeFill = CreateRangeFill(handleArea.transform);
            _rangeFill.SetAsFirstSibling(); // draw under the playhead handle
            _startMarker = CreateRangeMarker(handleArea.transform, "StartMarker", isEndMarker: false);
            _endMarker = CreateRangeMarker(handleArea.transform, "EndMarker", isEndMarker: true);
            return slider;
        }

        private RectTransform CreateRangeFill(Transform parent)
        {
            var go = new GameObject("RangeFill");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, -4f);
            rt.offsetMax = new Vector2(0f, 4f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.30f, 0.75f, 0.42f, 0.35f);
            img.raycastTarget = false;
            return rt;
        }

        // A trim marker: a colored vertical line across the bar with a small flag - the
        // START marker's flag hangs BELOW the bar and the END marker's sits ABOVE it,
        // and each marker's invisible grab surface covers only its own half of the row
        // (start = lower half + below, end = upper half + above), so the two markers
        // stay individually grabbable even when a short selection puts them on top of
        // each other. The marker's own pointer/drag handlers consume events before the
        // Slider sees them, so dragging a marker never scrubs the playhead.
        private RectTransform CreateRangeMarker(Transform parent, string name, bool isEndMarker)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            float fx = isEndMarker ? 1f : 0f;
            rt.anchorMin = new Vector2(fx, isEndMarker ? 0.5f : 0f);
            rt.anchorMax = new Vector2(fx, isEndMarker ? 1f : 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-10f, isEndMarker ? 0f : -10f);
            rt.offsetMax = new Vector2(10f, isEndMarker ? 12f : 0f);

            var grabImg = go.AddComponent<Image>();
            grabImg.color = new Color(0f, 0f, 0f, 0.001f);
            grabImg.raycastTarget = true;

            Color color = isEndMarker ? new Color(0.87f, 0.34f, 0.30f, 1f) : new Color(0.22f, 0.68f, 0.36f, 1f);

            // The line pokes past this half-row rect so it visibly crosses the whole bar.
            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(go.transform, false);
            var lineRt = lineGo.AddComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0.5f, 0f);
            lineRt.anchorMax = new Vector2(0.5f, 0f);
            lineRt.pivot = new Vector2(0.5f, 0f);
            lineRt.sizeDelta = new Vector2(3f, isEndMarker ? 20f : 19f);
            lineRt.anchoredPosition = new Vector2(0f, isEndMarker ? -4f : 6f);
            var lineImg = lineGo.AddComponent<Image>();
            lineImg.color = color;
            lineImg.raycastTarget = false;

            // Flags point OUTWARD - the direction that marker drags to extend the
            // range: end/red to the right, start/green to the left.
            var flagGo = new GameObject("Flag");
            flagGo.transform.SetParent(go.transform, false);
            var flagRt = flagGo.AddComponent<RectTransform>();
            flagRt.anchorMin = new Vector2(0.5f, isEndMarker ? 1f : 0f);
            flagRt.anchorMax = new Vector2(0.5f, isEndMarker ? 1f : 0f);
            flagRt.pivot = new Vector2(isEndMarker ? 0f : 1f, isEndMarker ? 1f : 0f);
            flagRt.sizeDelta = new Vector2(9f, 7f);
            flagRt.anchoredPosition = new Vector2(isEndMarker ? 1f : -1f, 0f);
            var flagImg = flagGo.AddComponent<Image>();
            flagImg.color = color;
            flagImg.raycastTarget = false;

            var handler = go.AddComponent<RangeMarkerDragHandler>();
            handler.Configure(this, isEndMarker);
            return rt;
        }

        private sealed class RangeMarkerDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            private ChatVideoClipChooser _owner;
            private bool _isEndMarker;

            public void Configure(ChatVideoClipChooser owner, bool isEndMarker)
            {
                _owner = owner;
                _isEndMarker = isEndMarker;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                // Swallow the press so the Slider underneath never jump-scrubs; the
                // drag flag is set on the first real drag event, not here, so a plain
                // click never pauses playback.
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_owner == null) return;
                _owner.DragMarkerToFraction(_isEndMarker, _owner.FractionFromPointer(eventData));
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                // Handles both a finished drag and a plain click: either way, land the
                // preview on this marker's frame.
                _owner?.FinishMarkerInteraction(_isEndMarker);
            }
        }

        private sealed class SliderScrubHandler : MonoBehaviour, IBeginDragHandler, IEndDragHandler
        {
            private ChatVideoClipChooser _owner;

            public void SetOwner(ChatVideoClipChooser owner)
            {
                _owner = owner;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                _owner?.BeginSliderScrub();
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                _owner?.EndSliderScrub();
            }
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
            CreateGripLine(go.transform, "GripLineSmall", 9f, 8f);
            CreateGripLine(go.transform, "GripLineMedium", 15f, 14f);
            CreateGripLine(go.transform, "GripLineLarge", 21f, 20f);
            var grip = go.AddComponent<ResizeGripDragHandler>();
            grip.SetOwner(this);
        }

        // One "/" stroke of the classic corner grip: parallel ridges facing the
        // bottom-right corner, shortest nearest the corner. The rotation must be +45
        // (perpendicular to the (-offset, offset) placement axis) - at -45 the three
        // strokes lie along the very line they are offset on and collapse into one
        // long diagonal.
        private void CreateGripLine(Transform parent, string name, float offset, float length)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(length, 2f);
            rt.anchoredPosition = new Vector2(-offset, offset);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
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
            _endField = null;
            _durationField = null;
            _fpsField = null;
            _includeAudioToggle = null;
            _exportToFileToggle = null;
            _exportToChatToggle = null;
            _playButton = null;
            _playButtonLabel = null;
            _proxyProgressRoot = null;
            _proxyProgressFill = null;
            _proxyProgressText = null;
            _duration3Button = null;
            _duration5Button = null;
            _duration8Button = null;
            _markerArea = null;
            _rangeFill = null;
            _startMarker = null;
            _endMarker = null;
            _isDraggingMarker = false;
            _isScrubbing = false;

            BuildUI();
            if (_preview != null && _rt != null)
            {
                _preview.texture = _rt;
                _preview.color = Color.white;
            }
            if (_previewHint != null && _prepared)
                _previewHint.gameObject.SetActive(false);
            SetProxyProgressVisible(_proxyConversionInFlight);
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
