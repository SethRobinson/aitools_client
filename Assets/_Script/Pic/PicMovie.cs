using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using AITools.AIChat.Video;

public class PicMovie : MonoBehaviour
{
    public Material _materialTemplate; // We'll use this to copy from
    public VideoPlayer _videoPlayer;
    public GameObject _movieObject;
    private RenderTexture _renderTexture; // We'll create this dynamically
    public Renderer _renderer;
    string m_fileName;
    string m_playbackFileName;
    string m_playbackProxyFileName;
    string m_playbackProxySourceFileName;
    string m_playbackProxyAttemptedSourceFileName;
    string m_playbackProxyConversionSourceFileName;
    float _updateTimerSeconds = 0.0f;
    float _updateIntervalSeconds = 0.1f;
    public PicMain _picMainScript;
    Vector2Int m_movieSize = new Vector2Int(0, 0);
    bool m_bAutoDeleteFileWhenDone = true;
    bool _bDidCleanupSoAllowReload = false;
    bool _bIsHidden = false;

    GameObject _progressBarContainer;
    Image _progressBarFill;
    TextMeshProUGUI _progressBarTimeText;
    TextMeshProUGUI _playPauseButtonText;
    bool _progressBarCreated = false;
    bool _showingConversionProgress = false;
    bool _playbackProxyConversionInFlight = false;
    FfmpegTool.CancelToken _playbackProxyCancelToken;
    const float PROGRESS_BAR_HEIGHT = 8f;

    // Play/seek intent model. Unity 6 + Media Foundation is unreliable around seeks:
    // it halts an audio-clocked player while a seek resolves, reports stale/zero
    // times for a few frames afterwards, and sometimes swallows a Play() issued from
    // the seekCompleted callback while still claiming isPlaying == true. So instead
    // of trusting one-shot Play()/Pause() calls, we track what the USER wants
    // (_wantPlaying) and ReconcilePlayState() nudges the player toward it every tick,
    // including a Pause+Play kick when the pipeline froze right after a seek. The
    // scrub/seek fields hold the target position so the bar displays intent, not the
    // player's transient values (which made the bar flicker old/new on a click).
    bool _wantPlaying = false;               // user intent; every load path auto-plays, so PlayMovieDirect sets it true
    bool _isScrubbing = false;               // pointer held down on the seek bar
    double _scrubSeconds = 0;                // displayed position while scrubbing
    bool _seekPending = false;               // seek issued, waiting on seekCompleted/timeout
    double _seekTargetSeconds = 0;
    float _seekStartedTime = 0f;
    const float SEEK_TIMEOUT_SECONDS = 1.5f; // treat the seek as done if seekCompleted never fires

    // Seeks are SERIALIZED: Media Foundation wedges hard when a new time= lands
    // while it is still resolving the previous seek (reproduced with rapid re-clicks
    // on the bar), so a seek requested during a pending one queues the target and
    // FinishSeek issues it once the pipeline settles.
    bool _hasQueuedSeek = false;
    double _queuedSeekSeconds = 0;

    // Rapid seeks must also not bounce play/pause per click: resuming playback
    // BETWEEN the seeks of a burst is what actually wedges Media Foundation (five
    // spaced clicks froze the clock for seconds even with seeks serialized). A seek
    // issued within SEEK_BURST_WINDOW of the previous one keeps the player paused
    // and arms a debounced resume; the tick resumes once the burst goes quiet -
    // the same shape as a scrub drag: pause once, seek N times, resume once.
    float _lastSeekIssuedTime = -999f;
    bool _currentSeekIsBurst = false;
    float _pendingResumeAtTime = 0f; // >0 = resume armed for this Time.time
    const float SEEK_BURST_WINDOW_SECONDS = 0.4f;

    // Post-seek display hold: distrust live player time briefly after a seek settles.
    // HOLD < TOLERANCE so normal 1x playback can't legitimately drift beyond the
    // tolerance inside the hold window (i.e. no snap-back on real progress).
    double _postSeekHoldSeconds = 0;
    float _postSeekHoldUntilTime = 0f;
    const float POST_SEEK_HOLD_SECONDS = 0.45f;
    const float POST_SEEK_HOLD_TOLERANCE = 0.5f;

    // Post-seek frozen-pipeline watch: while armed, "isPlaying but the clock is not
    // moving" across a few ticks earns a Pause+Play kick (capped per seek).
    float _postSeekKickUntilTime = 0f;
    int _postSeekKicks = 0;
    int _frozenTicks = 0;
    double _lastReconcileTime = -1;
    const float POST_SEEK_KICK_WINDOW_SECONDS = 3f;
    const int POST_SEEK_KICK_MAX = 3;

    // Audio is routed through an AudioSource (not VideoAudioOutputMode.Direct) because
    // Direct mode desyncs the audio track from the video pipeline on the first play
    // of every new clip (visible as a frozen first frame + chipmunk-speed audio,
    // then a clean second loop). Unity marked this Won't-Fix on issuetracker — it
    // stems from the underlying platform decoder (Media Foundation / AVPlayer) — and
    // the official workaround is AudioOutputMode.AudioSource.
    AudioSource _audioSource;

    // Set true by overlay UI (e.g. ChatPicMirror) while the pointer is over a
    // chat-side mirror of this movie. World-space hover can't detect chat overlays,
    // so the mirror has to grant permission explicitly. Still gated by global mute.
    bool _bExternalAudioPermit = false;

    // Two-stage prepare: the first Prepare() runs with targetTexture=null because we
    // don't know the video dimensions yet. Once prepareCompleted gives us those, we
    // create the real RT, bind it, and Prepare() AGAIN so the decoder pipeline
    // initializes against the actual target. Without this the very first playback of
    // every new clip shows a frozen frame while audio races (Unity 6 / Media Foundation).
    bool _waitingForSecondPrepare = false;

    // Media Foundation can wedge a Prepare() forever without firing errorReceived when
    // too many videos prepare at once (big canvas after the "\" unload-all hotkey).
    // FirstPrepareWatchdog cancels a wedged first prepare and lets Update()'s lazy
    // reload retry after a growing backoff. _playGeneration guards stale watchdogs:
    // it is bumped every time playback state resets.
    int _playGeneration = 0;
    int _consecutivePrepareFailures = 0;
    float _nextAutoReloadTime = 0f;
    const float FIRST_PREPARE_TIMEOUT_SECONDS = 10f;

    // After unload-all, every visible movie used to reload in the same 0.1s tick,
    // racing 100+ simultaneous Prepare() calls into Media Foundation (and an ffprobe
    // process each). Lazy reloads claim a global slot instead so they trickle in a
    // few per second.
    static float s_nextGlobalReloadTime = 0f;
    const float RELOAD_STAGGER_SECONDS = 0.1f;

    void Start()
    {
        if (_videoPlayer == null)
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        }

        if (_audioSource == null)
        {
            _audioSource = gameObject.GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.playOnAwake = false;
        ApplyClipVolume();

        _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _videoPlayer.SetTargetAudioSource(0, _audioSource);
        // Unity 6 enforces these more strictly than older versions. Setting them once
        // here keeps PlayMovie() from racing the first frame onto a stale/null target.
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.playOnAwake = false;
        // DSPTime drives video time from the audio clock — without this, in Unity 6
        // the game clock can race ahead of the decoder on the first play of a clip,
        // producing a frozen first frame while audio sprints. skipOnDrop=false stops
        // the player from "catching up" by skipping the frames it hasn't decoded yet.
        _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
        _videoPlayer.skipOnDrop = false;
        CreateProgressBarUI();
    }
    public void OnSetHidden()
    {
        _bIsHidden = true;
        _renderer.enabled = false;

    }

    public void SetAutoDeleteFileWhenDone(bool bAutoDelete)
    {
        m_bAutoDeleteFileWhenDone = bAutoDelete;
    }

    // Called by chat-side mirrors (ChatPicMirror) on pointer enter/exit. Lets a UI
    // overlay grant unmute permission when the world Pic itself is obscured by the
    // panel containing that mirror. Cleared automatically in CleanupVideoResources.
    public void SetExternalAudioPermit(bool on)
    {
        _bExternalAudioPermit = on;
    }

    public Vector2Int GetMovieSize() { return m_movieSize; }

    // The position the user sees: the scrub/seek target while one is in flight, and
    // (briefly) still the target right after the seek settles — the player reports
    // stale/zero times for a few frames there, and returning those made the bar
    // flicker between the old and new position on a click.
    public double GetCurrentPlaybackTimeSeconds()
    {
        if (_isScrubbing) return _scrubSeconds;
        if (_hasQueuedSeek) return _queuedSeekSeconds;
        if (_seekPending) return _seekTargetSeconds;
        if (_videoPlayer == null) return 0;

        double live;
        try { live = System.Math.Max(0, _videoPlayer.time); }
        catch { return 0; }

        if (Time.time < _postSeekHoldUntilTime
            && System.Math.Abs(live - _postSeekHoldSeconds) > POST_SEEK_HOLD_TOLERANCE)
        {
            return _postSeekHoldSeconds;
        }
        return live;
    }

    // "Playing" from the user's point of view (drives the button glyph): the player
    // itself may be deliberately paused mid-scrub/seek, or transiently wedged.
    public bool IsPlayingOrWillResume()
    {
        return IsMovie() && _wantPlaying;
    }

    // showMessage: the P hotkey toasts the new state; the on-screen button doesn't
    // need to since its icon flips.
    public void TogglePlay(bool showMessage = false)
    {
        if (_showingConversionProgress || _playbackProxyConversionInFlight)
            return;

        if (!IsMovie())
        {
            //show message
            RTQuickMessageManager.Get().ShowMessage("No movie loaded");
            return;
        }

        _wantPlaying = !_wantPlaying;
        _frozenTicks = 0;
        if (showMessage)
            RTQuickMessageManager.Get().ShowMessage(_wantPlaying ? "Playing movie" : "Pausing movie");

        // While a scrub/seek is in flight the player stays paused; FinishSeek and
        // ReconcilePlayState apply the new intent once it settles.
        if (!_isScrubbing && !_seekPending && _videoPlayer != null)
        {
            if (_wantPlaying)
            {
                _videoPlayer.waitForFirstFrame = false; // warm resume; see ResumeAfterSeekNow
                _videoPlayer.Play();
            }
            else
            {
                _videoPlayer.Pause();
            }
        }

        UpdateProgressBar();
    }

    public void PauseIfPlaying()
    {
        _wantPlaying = false;
        _pendingResumeAtTime = 0f;
        if (_videoPlayer != null && _videoPlayer.isPlaying)
        {
            _videoPlayer.Pause();
        }
    }

    public string GetFileName()
    {
        return m_fileName;
    }

    public string GetProcessingFileName()
    {
        if (CanReusePlaybackProxy(m_fileName))
            return m_playbackProxyFileName;
        return m_fileName;
    }

    public string GetFileNameWithoutPath()
    {
        return System.IO.Path.GetFileName(m_fileName);
    }

    public string GetProcessingFileNameWithoutPath()
    {
        return System.IO.Path.GetFileName(GetProcessingFileName());
    }

    public string GetFileExtensionOfMovie()
    {
        return System.IO.Path.GetExtension(m_fileName);
    }

    // AI Chat "Audio #N" bubbles play a waveform preview movie; this is the ORIGINAL
    // sound file behind it (wav/flac/mp3), so saving the pic also delivers the real audio.
    string m_companionAudioFile;

    public void SetCompanionAudioFile(string path)
    {
        m_companionAudioFile = path;
    }

    public string GetCompanionAudioFile()
    {
        return m_companionAudioFile;
    }

    public void SaveMovie(string path)
    {
        string newFileName;
        if (string.IsNullOrEmpty(path))
        {
            // Original behavior - just remove temp_ prefix
            newFileName = m_fileName.Replace("temp_", "");
            RTUtil.CopyFile(m_fileName, newFileName);
        }
        else
        {
            // Extract just the filename without the path
            string fileName = System.IO.Path.GetFileName(m_fileName);

            // Remove temp_ prefix if it exists
            fileName = fileName.Replace("temp_", "");

            // Combine the provided path with the filename
            newFileName = System.IO.Path.Combine(Config.Get().GetBaseFileDir(path) + "/", fileName);

            RTUtil.CopyFile(m_fileName, newFileName);
        }
        SaveCompanionAudioNextTo(newFileName);
    }

    // Copies the original sound file (audio bubbles) next to the saved preview movie,
    // same stem, its own extension: "sfx_..._preview.mp4" + "sfx_..._preview.wav".
    void SaveCompanionAudioNextTo(string savedMoviePath)
    {
        if (string.IsNullOrEmpty(m_companionAudioFile) || string.IsNullOrEmpty(savedMoviePath)) return;
        try
        {
            if (!System.IO.File.Exists(m_companionAudioFile)) return;
            string audioPath = System.IO.Path.ChangeExtension(savedMoviePath, System.IO.Path.GetExtension(m_companionAudioFile));
            RTUtil.CopyFile(m_companionAudioFile, audioPath);
            RTQuickMessageManager.Get().ShowMessage("Saved audio as " + System.IO.Path.GetFileName(audioPath));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("PicMovie: could not save companion audio: " + e.Message);
        }
    }

    public void SaveMovieWithNewFilename(string path)
    {

        //if path doesn't have a file extension, add the one from m_fileName
        if (!path.Contains("."))
        {
            path += System.IO.Path.GetExtension(m_fileName);
        }

        // Extract just the filename without the path
        string fileName = System.IO.Path.GetFileName(m_fileName);

        RTUtil.CopyFile(m_fileName, path);
        SaveCompanionAudioNextTo(path);
    }


  
    public void DeleteMovieIfNeeded()
    {
        if (m_bAutoDeleteFileWhenDone)
        {
            if (m_fileName != null && m_fileName.Length > 0)
            {
                //delete the file
                if (GameLogic.Get().GetAutoSave() || GameLogic.Get().GetAutoSavePNG())
                {
                    //keep the file
                }
                else
                {
                    RTUtil.DeleteFileIfItExists(m_fileName);
                }
            }
        }
    }
    private void OnDestroy()
    {
        KillMovie();
    }
    // Update is called once per frame

    public void Update()
    {
        bool pointerInsideAppWindow = IsPointerInsideAppWindow();

        // Pointer coordinates continue beyond the client area in a windowed player.
        // Mute before doing anything else so an out-of-window position can never be
        // projected onto a movie (or inherit a stale chat-mirror hover permit).
        if (!Application.isFocused || !pointerInsideAppWindow)
        {
            if (_audioSource != null)
                _audioSource.mute = true;
        }

        // Keep non-input maintenance running while the focused app merely has its
        // pointer outside, but retain the old early-out when focus is lost - except
        // when the automation bridge is driving us: bridge tests run with the editor
        // unfocused (an external agent poking HTTP endpoints), and the old early-out
        // left every movie black/unloadable for them. Audio stays muted above.
        if (!AppFocusedForPlayback())
            return;

        if (_bIsHidden)
        {
            if (!Input.GetKey(KeyCode.H))
            {
                //no longer hidden
                _renderer.enabled = true;

            }
        }

        //see if it's time to update
        if (_updateTimerSeconds  < Time.time)
        {
            _updateTimerSeconds  = Time.time + _updateIntervalSeconds;

            bool isVisible = _picMainScript.IsVisible();

            //if we have a valid movie filename, but it isn't loaded/playing, let's load and play it now
            if (!_playbackProxyConversionInFlight && _bDidCleanupSoAllowReload && m_fileName != null && m_fileName.Length > 0 && _renderTexture == null && isVisible
                && Time.time >= _nextAutoReloadTime && ClaimGlobalReloadSlot())
            {
                PlayMovie(m_fileName);
                _bDidCleanupSoAllowReload = false;
            }

            if (_videoPlayer.isPlaying && _audioSource != null)
            {
                ApplyClipVolume();
                GameObject go = GameLogic.Get().GetPicWereHoveringOver();
                // GetPicWereHoveringOver() does a 2D physics raycast that ignores UI canvases,
                // so it will happily report "hovering" even when an overlay panel (AI Chat,
                // settings dialogs, etc.) is sitting on top of the movie - which would
                // unmute audio for a video the user can't actually see. Treat any UI
                // canvas in front of the mouse as "not hovering" so the audio stays muted.
                // _bExternalAudioPermit lets a chat-side mirror grant unmute permission
                // for cases where the world Pic is covered by the chat panel itself.
                bool worldHover = (go == gameObject && !IsMouseObscuredByOtherUI());
                if (Application.isFocused && pointerInsideAppWindow && (worldHover || _bExternalAudioPermit))
                {
                    _audioSource.mute = GameLogic.Get().GetGlobalMute();
                }
                else
                {
                    _audioSource.mute = true;
                }
            }

            ReconcilePlayState();
            UpdateProgressBar();
        }

    }

    private static bool IsPointerInsideAppWindow()
    {
        Vector3 pointer = Input.mousePosition;
        return pointer.x >= 0f && pointer.y >= 0f
            && pointer.x < Screen.width && pointer.y < Screen.height;
    }

    // Playback logic treats "automation driver attached" as focus-equivalent so
    // bridge-driven tests can load/play/seek movies while the editor sits in the
    // background. Real OS focus still exclusively controls audio unmuting.
    private static bool AppFocusedForPlayback()
    {
        return Application.isFocused || AutomationBridge.IsDriverReady;
    }

    // One lazy reload may start per RELOAD_STAGGER_SECONDS across ALL movie pics, so
    // an unload-all doesn't stampede the decoder. Keep this the LAST condition in the
    // reload if() so slots are only claimed by movies that are actually ready to load.
    static bool ClaimGlobalReloadSlot()
    {
        if (Time.time < s_nextGlobalReloadTime) return false;
        s_nextGlobalReloadTime = Time.time + RELOAD_STAGGER_SECONDS;
        return true;
    }

    // Reused across all PicMovie instances to avoid GC churn each tick.
    private static readonly List<RaycastResult> s_uiRaycastResults = new List<RaycastResult>();

    /// <summary>
    /// True when the mouse is over a UI element on a Canvas OTHER than this movie's own
    /// PicMain canvas. Used to detect cases like the AI Chat panel covering the video -
    /// the world-space 2D raycast in GetPicWereHoveringOver() can't see UI, so without
    /// this check we'd unmute audio for a movie the user can't actually see.
    /// The movie's own progress bar lives on _picMainScript.GetCanvas() and is excluded
    /// so hovering it won't kill audio (it sits below the video quad anyway).
    /// </summary>
    private bool IsMouseObscuredByOtherUI()
    {
        var es = EventSystem.current;
        if (es == null) return false;
        if (!es.IsPointerOverGameObject()) return false;

        var ped = new PointerEventData(es) { position = Input.mousePosition };
        s_uiRaycastResults.Clear();
        es.RaycastAll(ped, s_uiRaycastResults);
        if (s_uiRaycastResults.Count == 0) return false;

        Canvas myCanvas = _picMainScript != null ? _picMainScript.GetCanvas() : null;
        Canvas myRoot = myCanvas != null ? myCanvas.rootCanvas : null;

        var topGo = s_uiRaycastResults[0].gameObject;
        if (topGo == null) return false;
        var hitCanvas = topGo.GetComponentInParent<Canvas>();
        if (hitCanvas == null) return false;

        return hitCanvas.rootCanvas != myRoot;
    }

    void CreateProgressBarUI()
    {
        if (_progressBarCreated) return;
        _progressBarCreated = true;

        Canvas canvas = _picMainScript.GetCanvas();
        if (canvas == null) return;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        int layer = canvas.gameObject.layer;

        // Container -- positioned dynamically in UpdateProgressBar based on movie height
        _progressBarContainer = new GameObject("VideoProgressBar");
        _progressBarContainer.layer = layer;
        RectTransform containerRect = _progressBarContainer.AddComponent<RectTransform>();
        containerRect.SetParent(canvasRect, false);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 1f);
        containerRect.sizeDelta = new Vector2(canvasRect.sizeDelta.x, PROGRESS_BAR_HEIGHT);

        // Play/Pause button on the left
        float buttonWidth = 14f;
        GameObject btnObj = new GameObject("PlayPauseBtn");
        btnObj.layer = layer;
        btnObj.AddComponent<CanvasRenderer>();
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.15f, 0.15f, 0.15f, 0.7f);
        btnImage.raycastTarget = true;
        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.SetParent(containerRect, false);
        btnRect.anchorMin = new Vector2(0, 0);
        btnRect.anchorMax = new Vector2(0, 1);
        btnRect.pivot = new Vector2(0, 0.5f);
        btnRect.anchoredPosition = Vector2.zero;
        btnRect.sizeDelta = new Vector2(buttonWidth, 0);

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        cb.pressedColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);
        btn.colors = cb;
        btn.onClick.AddListener(() => TogglePlay());

        GameObject btnTextObj = new GameObject("BtnText");
        btnTextObj.layer = layer;
        btnTextObj.AddComponent<CanvasRenderer>();
        _playPauseButtonText = btnTextObj.AddComponent<TextMeshProUGUI>();
        _playPauseButtonText.text = "\u2590\u2590";
        _playPauseButtonText.fontSize = 5f;
        _playPauseButtonText.alignment = TextAlignmentOptions.Center;
        _playPauseButtonText.color = Color.white;
        _playPauseButtonText.raycastTarget = false;
        _playPauseButtonText.textWrappingMode = TextWrappingModes.NoWrap;
        _playPauseButtonText.overflowMode = TextOverflowModes.Overflow;
        RectTransform btnTextRect = btnTextObj.GetComponent<RectTransform>();
        btnTextRect.SetParent(btnRect, false);
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.sizeDelta = Vector2.zero;
        btnTextRect.anchoredPosition = Vector2.zero;

        // Seek bar area (to the right of the button)
        GameObject seekArea = new GameObject("SeekArea");
        seekArea.layer = layer;
        RectTransform seekRect = seekArea.AddComponent<RectTransform>();
        seekRect.SetParent(containerRect, false);
        seekRect.anchorMin = new Vector2(0, 0);
        seekRect.anchorMax = new Vector2(1, 1);
        seekRect.offsetMin = new Vector2(buttonWidth + 1f, 0);
        seekRect.offsetMax = Vector2.zero;

        // Background of seek bar
        GameObject bgObj = new GameObject("ProgressBg");
        bgObj.layer = layer;
        bgObj.AddComponent<CanvasRenderer>();
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.55f);
        bgImage.raycastTarget = true;
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.SetParent(seekRect, false);
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        bgRect.anchoredPosition = Vector2.zero;

        // Press = start scrubbing, drag = move the scrub position, release = seek once.
        // Drag/PointerUp are delivered to the pressed object even after the pointer
        // leaves the bar, so releasing outside still finishes the scrub.
        EventTrigger trigger = bgObj.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerDown, data => OnSeekBarPointerDown(data, bgRect));
        AddTrigger(trigger, EventTriggerType.Drag, data => OnSeekBarDrag(data, bgRect));
        AddTrigger(trigger, EventTriggerType.PointerUp, data => OnSeekBarPointerUp(data, bgRect));

        // Fill
        GameObject fillObj = new GameObject("ProgressFill");
        fillObj.layer = layer;
        fillObj.AddComponent<CanvasRenderer>();
        _progressBarFill = fillObj.AddComponent<Image>();
        _progressBarFill.color = new Color(0.3f, 0.6f, 1f, 0.85f);
        _progressBarFill.raycastTarget = false;
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.SetParent(seekRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchoredPosition = Vector2.zero;

        // Time label
        GameObject textObj = new GameObject("ProgressTime");
        textObj.layer = layer;
        textObj.AddComponent<CanvasRenderer>();
        _progressBarTimeText = textObj.AddComponent<TextMeshProUGUI>();
        _progressBarTimeText.fontSize = 5.5f;
        _progressBarTimeText.alignment = TextAlignmentOptions.Center;
        _progressBarTimeText.color = Color.white;
        _progressBarTimeText.raycastTarget = false;
        _progressBarTimeText.textWrappingMode = TextWrappingModes.NoWrap;
        _progressBarTimeText.overflowMode = TextOverflowModes.Overflow;
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.SetParent(seekRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        _progressBarContainer.SetActive(false);
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action<PointerEventData> handler)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(data => handler((PointerEventData)data));
        trigger.triggers.Add(entry);
    }

    // Seeking only makes sense on a fully prepared clip: the two-stage prepare hasn't
    // bound the decoder yet before _renderTexture exists / the second prepare fires.
    bool CanSeek()
    {
        return IsMovie() && !_showingConversionProgress && !_playbackProxyConversionInFlight
            && _videoPlayer != null && _videoPlayer.length > 0
            && _renderTexture != null && !_waitingForSecondPrepare;
    }

    double SeekBarPointerToSeconds(PointerEventData eventData, RectTransform barRect)
    {
        Camera cam = _picMainScript.GetCamera();
        if (cam == null) cam = Camera.main;

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(barRect, eventData.position, cam, out localPoint))
            return GetCurrentPlaybackTimeSeconds();

        float normalized = Mathf.Clamp01((localPoint.x + barRect.rect.width * barRect.pivot.x) / barRect.rect.width);
        return normalized * _videoPlayer.length;
    }

    void OnSeekBarPointerDown(PointerEventData eventData, RectTransform barRect)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !CanSeek()) return;

        // Any queued (not yet issued) seek is superseded by this scrub. An IN-FLIGHT
        // seek deliberately stays pending: seeks must never overlap, so PointerUp
        // queues behind it if it still hasn't settled by then.
        _hasQueuedSeek = false;
        _pendingResumeAtTime = 0f; // resume decision restarts after this scrub
        _isScrubbing = true;
        _scrubSeconds = SeekBarPointerToSeconds(eventData, barRect);
        // Deliberately NOT pausing here: a plain click seeks while the player keeps
        // running (the clip chooser's slider pattern, the most reliable seek path
        // in the app). A real drag pauses on its first Drag event instead.
        UpdateProgressBar();
    }

    void OnSeekBarDrag(PointerEventData eventData, RectTransform barRect)
    {
        if (!_isScrubbing) return;
        // First drag movement turns the press into a real scrub: hold the player
        // paused for its duration (pause once, seek once on release, resume once).
        if (_videoPlayer != null && _videoPlayer.isPlaying)
            _videoPlayer.Pause();
        _scrubSeconds = SeekBarPointerToSeconds(eventData, barRect);
        UpdateProgressBar();
    }

    void OnSeekBarPointerUp(PointerEventData eventData, RectTransform barRect)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        SeekTo(CanSeek() ? SeekBarPointerToSeconds(eventData, barRect) : _scrubSeconds);
    }

    void SeekTo(double seconds, bool fromKick = false)
    {
        if (!CanSeek()) return;
        seconds = System.Math.Max(0, System.Math.Min(seconds, _videoPlayer.length));
        if (!fromKick)
            _postSeekKicks = 0; // a fresh user seek gets a fresh kick budget
        if (_seekPending)
        {
            // Never overlap seeks (see the queued-seek fields comment): remember the
            // newest target; FinishSeek issues it when the current seek settles.
            _hasQueuedSeek = true;
            _queuedSeekSeconds = seconds;
            UpdateProgressBar();
            return;
        }
        _hasQueuedSeek = false;
        _currentSeekIsBurst = !fromKick && Time.time - _lastSeekIssuedTime < SEEK_BURST_WINDOW_SECONDS;
        _lastSeekIssuedTime = Time.time;
        _pendingResumeAtTime = 0f;
        // A burst pauses the player across its seeks (pause once, seek many, resume
        // once - the scrub-drag shape). An isolated click-seek rides through while
        // playing: pausing every click added a resume step MF sometimes fumbled.
        if (_currentSeekIsBurst && _videoPlayer.isPlaying)
            _videoPlayer.Pause();
        _seekPending = true;
        _seekTargetSeconds = seconds;
        _seekStartedTime = Time.time;
        try
        {
            _videoPlayer.time = seconds;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("PicMovie: seek failed: " + e.Message);
            FinishSeek();
        }
        UpdateProgressBar();
    }

    void OnSeekCompleted(VideoPlayer source)
    {
        if (source == _videoPlayer)
            FinishSeek();
    }

    // Called from seekCompleted, or from the tick if that never fires (Media
    // Foundation can skip it, e.g. seeking onto the current frame). Seeks are
    // serialized, so at most one is ever in flight and a completion always belongs
    // to it; if a queued target is waiting, this issues it instead of resuming.
    void FinishSeek()
    {
        if (!_seekPending) return;
        _seekPending = false;

        // Distrust live player time briefly.
        _postSeekHoldSeconds = _seekTargetSeconds;
        _postSeekHoldUntilTime = Time.time + POST_SEEK_HOLD_SECONDS;

        if (_isScrubbing)
            return; // the user grabbed the bar again; stay paused until release

        if (_hasQueuedSeek)
        {
            // The pipeline just settled; now it is safe to issue the newest target.
            _hasQueuedSeek = false;
            SeekTo(_queuedSeekSeconds);
            return;
        }

        if (_wantPlaying && _videoPlayer != null)
        {
            // Watch for a frozen clock either way: even a seek issued while playing
            // can stall the pipeline (isPlaying stays true, clock parked).
            ArmFrozenWatch();
            if (!_videoPlayer.isPlaying)
            {
                // Resume from the tick, NEVER from inside the seekCompleted
                // callstack: a synchronous Play() here sometimes leaves Media
                // Foundation with a frozen clock while isPlaying lies true. An
                // isolated seek resumes on the next tick (<=0.1s); a burst waits
                // until no new seek has arrived for a full window.
                _pendingResumeAtTime = Time.time + (_currentSeekIsBurst ? SEEK_BURST_WINDOW_SECONDS : 0.01f);
            }
        }
        UpdateProgressBar();
    }

    void ArmFrozenWatch()
    {
        _postSeekKickUntilTime = Time.time + POST_SEEK_KICK_WINDOW_SECONDS;
        _frozenTicks = 0;
        _lastReconcileTime = -1;
    }

    // The one place playback restarts after a seek: arms the frozen-pipeline watch
    // at the moment we actually ask Media Foundation to run again.
    void ResumeAfterSeekNow()
    {
        _pendingResumeAtTime = 0f;
        ArmFrozenWatch(); // the kick budget is NOT reset here - only a user seek does
        if (_videoPlayer != null && !_videoPlayer.isPlaying)
        {
            // waitForFirstFrame=true is only needed for the FIRST play of a fresh
            // clip (the Unity 6 first-play glitch; PlayMovieDirect re-asserts it).
            // On a post-seek resume it makes Play() block on a "first frame" that
            // Media Foundation may have already delivered during the paused seek -
            // the player then reports isPlaying with a frozen clock forever, which
            // is the "seek left it paused/stuck" bug.
            _videoPlayer.waitForFirstFrame = false;
            _videoPlayer.Play();
        }
    }

    void ResetSeekState()
    {
        _isScrubbing = false;
        _seekPending = false;
        _hasQueuedSeek = false;
        _currentSeekIsBurst = false;
        _pendingResumeAtTime = 0f;
        _lastSeekIssuedTime = -999f;
        _postSeekHoldUntilTime = 0f;
        _postSeekKickUntilTime = 0f;
        _postSeekKicks = 0;
        _frozenTicks = 0;
        _lastReconcileTime = -1;
    }

    // Runs on the 0.1s tick: nudge the player toward what the user wants instead of
    // trusting any single Play()/Pause() call. Media Foundation sometimes swallows a
    // resume issued around a seek while still reporting isPlaying==true with a frozen
    // clock; while the post-seek watch is armed, that state earns a Pause+Play kick.
    void ReconcilePlayState()
    {
        if (_videoPlayer == null || !IsMovie() || _renderTexture == null || _waitingForSecondPrepare)
            return;
        if (_isScrubbing || _seekPending)
            return; // paused on purpose until the scrub/seek settles

        if (!_wantPlaying)
        {
            _pendingResumeAtTime = 0f;
            if (_videoPlayer.isPlaying)
                _videoPlayer.Pause();
            _frozenTicks = 0;
            return;
        }

        if (_pendingResumeAtTime > 0f)
        {
            if (Time.time < _pendingResumeAtTime)
                return; // deliberately paused until the seek burst quiets down
            ResumeAfterSeekNow();
            return;
        }

        if (!_videoPlayer.isPlaying)
        {
            _videoPlayer.waitForFirstFrame = false; // warm resume; see ResumeAfterSeekNow
            _videoPlayer.Play();
            _frozenTicks = 0;
            return;
        }

        if (Time.time >= _postSeekKickUntilTime)
            return;

        double t;
        try { t = _videoPlayer.time; } catch { return; }
        bool frozen = _lastReconcileTime >= 0 && System.Math.Abs(t - _lastReconcileTime) < 0.0001;
        _lastReconcileTime = t;
        if (!frozen)
        {
            _frozenTicks = 0;
            return;
        }

        if (++_frozenTicks >= 3 && _postSeekKicks < POST_SEEK_KICK_MAX)
        {
            _postSeekKicks++;
            _frozenTicks = 0;
            // Unfreeze by RE-SEEKING: a frozen MF pipeline responds to seeks, not
            // to Pause/Play (this is exactly what the old manual workaround of
            // clicking the bar a second time did). Nudge the target a hair so MF
            // cannot dismiss it as a same-position no-op; pause first so the
            // resume runs through the normal deferred-resume path on settle.
            double target = _seekTargetSeconds + 0.05 * _postSeekKicks;
            if (target > _videoPlayer.length)
                target = _seekTargetSeconds;
            _videoPlayer.Pause();
            SeekTo(target, fromKick: true);
        }
    }

    // Bridge/test telemetry: the live playback state as one JSON object, served by
    // the automation /movie_state endpoint. Keep every field cheap to read.
    public string GetPlaybackDebugJson()
    {
        bool isPlaying = false, isPrepared = false;
        double time = 0, length = 0;
        try
        {
            if (_videoPlayer != null)
            {
                isPlaying = _videoPlayer.isPlaying;
                isPrepared = _videoPlayer.isPrepared;
                time = _videoPlayer.time;
                length = _videoPlayer.length;
            }
        }
        catch { }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "{"
            + "\"wantPlaying\":" + (_wantPlaying ? "true" : "false")
            + ",\"isPlaying\":" + (isPlaying ? "true" : "false")
            + ",\"isPrepared\":" + (isPrepared ? "true" : "false")
            + ",\"time\":" + time.ToString("0.###", inv)
            + ",\"length\":" + length.ToString("0.###", inv)
            + ",\"displayTime\":" + GetCurrentPlaybackTimeSeconds().ToString("0.###", inv)
            + ",\"isScrubbing\":" + (_isScrubbing ? "true" : "false")
            + ",\"seekPending\":" + (_seekPending ? "true" : "false")
            + ",\"seekTarget\":" + _seekTargetSeconds.ToString("0.###", inv)
            + ",\"queuedSeek\":" + (_hasQueuedSeek ? _queuedSeekSeconds.ToString("0.###", inv) : "null")
            + ",\"resumeDebounceActive\":" + (_pendingResumeAtTime > 0f ? "true" : "false")
            + ",\"postSeekHoldActive\":" + (Time.time < _postSeekHoldUntilTime ? "true" : "false")
            + ",\"kickWindowActive\":" + (Time.time < _postSeekKickUntilTime ? "true" : "false")
            + ",\"postSeekKicks\":" + _postSeekKicks
            + ",\"frozenTicks\":" + _frozenTicks
            + ",\"waitingForSecondPrepare\":" + (_waitingForSecondPrepare ? "true" : "false")
            + ",\"hasRenderTexture\":" + (_renderTexture != null ? "true" : "false")
            + "}";
    }

    void UpdateProgressBar()
    {
        if (_progressBarContainer == null) return;

        if (_showingConversionProgress)
        {
            PositionProgressBar();
            if (!_progressBarContainer.activeSelf)
                _progressBarContainer.SetActive(true);
            if (_playPauseButtonText != null)
                _playPauseButtonText.text = "...";
            return;
        }

        bool shouldShow = IsMovie() && _videoPlayer != null && _videoPlayer.length > 0;
        if (_progressBarContainer.activeSelf != shouldShow)
            _progressBarContainer.SetActive(shouldShow);

        if (!shouldShow) return;

        if (_seekPending && Time.time - _seekStartedTime > SEEK_TIMEOUT_SECONDS)
            FinishSeek();

        PositionProgressBar();

        double currentTime = GetCurrentPlaybackTimeSeconds();
        double totalTime = _videoPlayer.length;
        float progress = Mathf.Clamp01((float)(currentTime / totalTime));

        _progressBarFill.rectTransform.anchorMax = new Vector2(progress, 1);
        _progressBarTimeText.text = $"{currentTime:F1}s / {totalTime:F1}s";

        if (_playPauseButtonText != null)
            _playPauseButtonText.text = IsPlayingOrWillResume() ? "\u2590\u2590" : "\u25B6";
    }

    void PositionProgressBar()
    {
        if (_progressBarContainer == null || _picMainScript == null || _picMainScript.GetCanvas() == null || _movieObject == null)
            return;

        RectTransform canvasRect = _picMainScript.GetCanvas().GetComponent<RectTransform>();
        float movieBottomWorld = _movieObject.transform.position.y
            - _movieObject.transform.lossyScale.y * 0.5f;
        Vector3 localInCanvas = canvasRect.InverseTransformPoint(
            new Vector3(_movieObject.transform.position.x, movieBottomWorld, _movieObject.transform.position.z));

        RectTransform containerRect = _progressBarContainer.GetComponent<RectTransform>();
        containerRect.anchoredPosition = new Vector2(0, localInCanvas.y);
        containerRect.sizeDelta = new Vector2(canvasRect.sizeDelta.x, PROGRESS_BAR_HEIGHT);
    }

    public bool IsMovie()
    {
        return _movieObject.activeSelf;
    }

    public void KillMovie()
    {
        CancelPlaybackProxyConversion();
        DeleteMovieIfNeeded();
        CleanupVideoResources();
        DeletePlaybackProxyIfNeeded();
        _bDidCleanupSoAllowReload = false;
        _consecutivePrepareFailures = 0;
        _nextAutoReloadTime = 0f;
        m_bAutoDeleteFileWhenDone = true;
        m_fileName = null;
        m_playbackFileName = null;
        m_playbackProxySourceFileName = null;
        m_playbackProxyAttemptedSourceFileName = null;
        m_playbackProxyConversionSourceFileName = null;
        m_movieSize = new Vector2Int(0, 0);
        _showingConversionProgress = false;
        _movieObject.SetActive(false);

        if (_progressBarContainer != null)
            _progressBarContainer.SetActive(false);
    }

    private void CleanupVideoResources()
    {
        _playGeneration++;
        _waitingForSecondPrepare = false;
        _bExternalAudioPermit = false;
        ResetSeekState();
        if (_videoPlayer != null)
        {
            // Stop() unconditionally: it also cancels an in-flight Prepare(). A player
            // wedged in "preparing" reports isPlaying == false, and calling Prepare()
            // on it again later is a no-op, so the old isPlaying-gated Stop() left
            // wedged movies permanently black through every reload.
            _videoPlayer.Stop();
        }

        if (_renderTexture != null)
        {
            if (_videoPlayer != null && _videoPlayer.targetTexture == _renderTexture)
                _videoPlayer.targetTexture = null;
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
            _bDidCleanupSoAllowReload = true;
        }

        // Clear material reference
        if (_renderer.material != _materialTemplate && _renderer.material != null)
        {
            Destroy(_renderer.material);
            _renderer.material = null;
        }

     // System.GC.Collect();
    }

    private void OnVideoLoop(VideoPlayer source)
    {
        // Check memory before starting next loop
        if (RTUtil.IsMemoryLow())
        {
            // Pause playback and show warning. Update the intent too, or
            // ReconcilePlayState would immediately restart it.
            _wantPlaying = false;
            source.Pause();
            RTQuickMessageManager.Get().ShowMessage("Playback paused - low memory");

            // Attempt cleanup
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
        }
    }

    private void ConfigureVideoPlayer(string filename)
    {
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.url = filename;
        _videoPlayer.isLooping = true;
        _videoPlayer.playOnAwake = true;

        // Set up error handlers
        _videoPlayer.prepareCompleted -= OnVideoPrepared;
        _videoPlayer.prepareCompleted += OnVideoPrepared;
        _videoPlayer.errorReceived -= OnVideoError;
        _videoPlayer.errorReceived += OnVideoError;

        // Add loopPointReached handler to check memory at end of playback
        _videoPlayer.loopPointReached -= OnVideoLoop;
        _videoPlayer.loopPointReached += OnVideoLoop;

        try
        {
            _videoPlayer.Prepare();
        }
        catch (System.Exception e)
        {
            HandleVideoError($"Exception during video preparation: {e.Message}");
        }
    }

    public bool TryEnsureLoadedForSnapshot()
    {
        if (_renderTexture != null)
            return true;

        if (string.IsNullOrEmpty(m_fileName))
            return false;

        if (!System.IO.File.Exists(m_fileName))
            return false;

        PlayMovie(m_fileName, forceLoad: true);
        return true;
    }

    public void PlayMovie(string filename, bool forceLoad = false)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return;

        if (!forceLoad && (!AppFocusedForPlayback() || !_picMainScript.IsVisible()))
        {
            m_fileName = filename;
            m_playbackFileName = filename;
            _movieObject.SetActive(true);

            _bDidCleanupSoAllowReload = true;
            return; //don't play it now
        }

        if (_playbackProxyConversionInFlight)
        {
            if (string.Equals(m_playbackProxyConversionSourceFileName, filename, System.StringComparison.OrdinalIgnoreCase))
                return;
            CancelPlaybackProxyConversion();
        }

        if (!string.IsNullOrWhiteSpace(m_playbackProxySourceFileName)
            && !string.Equals(m_playbackProxySourceFileName, filename, System.StringComparison.OrdinalIgnoreCase))
        {
            DeletePlaybackProxyIfNeeded();
        }

        if (CanReusePlaybackProxy(filename))
        {
            PlayMovieDirect(filename, m_playbackProxyFileName, forceLoad);
            return;
        }

        if (FfmpegTool.IsSupportedVideoExtension(filename))
        {
            StartCoroutine(PlayMovieWithProxyIfNeeded(filename, forceLoad));
            return;
        }

        PlayMovieDirect(filename, filename, forceLoad);
    }

    private IEnumerator PlayMovieWithProxyIfNeeded(string filename, bool forceLoad)
    {
        m_fileName = filename;
        m_playbackFileName = filename;
        _movieObject.SetActive(true);
        _bDidCleanupSoAllowReload = false;

        FfmpegTool.VideoInfo info = null;
        string probeError = null;
        yield return FfmpegTool.ProbeVideo(filename, (i, e) =>
        {
            info = i;
            probeError = e;
        });

        if (!string.Equals(m_fileName, filename, System.StringComparison.OrdinalIgnoreCase))
            yield break;

        if (info == null)
        {
            if (!string.IsNullOrWhiteSpace(probeError))
                Debug.LogWarning("PicMovie ffprobe failed, falling back to Unity VideoPlayer: " + probeError);
            PlayMovieDirect(filename, filename, forceLoad);
            yield break;
        }

        if (!FfmpegTool.ShouldUseUnityPreviewProxy(info))
        {
            PlayMovieDirect(filename, filename, forceLoad);
            yield break;
        }

        yield return CreatePlaybackProxyAndPlay(filename, forceLoad, info, "Converting video for Windows playback...");
    }

    private IEnumerator CreatePlaybackProxyAndPlay(string filename, bool forceLoad, FfmpegTool.VideoInfo info, string initialMessage)
    {
        m_playbackProxyAttemptedSourceFileName = filename;
        m_playbackProxyConversionSourceFileName = filename;
        _playbackProxyConversionInFlight = true;
        SetConversionProgress(0f, initialMessage);
        DeletePlaybackProxyIfNeeded();

        FfmpegTool.ClipResult result = null;
        var cancelToken = new FfmpegTool.CancelToken();
        _playbackProxyCancelToken = cancelToken;
        double proxyFps = info != null && info.Fps > 0 ? System.Math.Min(info.Fps, 30.0) : 30.0;
        double duration = info != null ? info.DurationSeconds : 0;

        yield return FfmpegTool.CreatePreviewProxy(
            filename,
            duration,
            proxyFps,
            r => result = r,
            (p, msg) =>
            {
                if (ReferenceEquals(_playbackProxyCancelToken, cancelToken)
                    && string.Equals(m_playbackProxyConversionSourceFileName, filename, System.StringComparison.OrdinalIgnoreCase))
                {
                    SetConversionProgress(p, string.IsNullOrWhiteSpace(msg) ? "Converting video..." : msg);
                }
            },
            cancelToken,
            includeAudio: true);

        bool stillCurrent = string.Equals(m_playbackProxyConversionSourceFileName, filename, System.StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(_playbackProxyCancelToken, cancelToken);
        if (stillCurrent && !cancelToken.CancelRequested
            && (result == null || !result.Success || string.IsNullOrWhiteSpace(result.OutputPath) || !System.IO.File.Exists(result.OutputPath)))
        {
            Debug.LogWarning("PicMovie audio preview proxy failed; retrying without audio for " + filename);
            SetConversionProgress(0f, "Retrying conversion without audio...");
            result = null;
            yield return FfmpegTool.CreatePreviewProxy(
                filename,
                duration,
                proxyFps,
                r => result = r,
                (p, msg) =>
                {
                    if (ReferenceEquals(_playbackProxyCancelToken, cancelToken)
                        && string.Equals(m_playbackProxyConversionSourceFileName, filename, System.StringComparison.OrdinalIgnoreCase))
                    {
                        SetConversionProgress(p, string.IsNullOrWhiteSpace(msg) ? "Converting video..." : msg);
                    }
                },
                cancelToken);
        }

        stillCurrent = string.Equals(m_playbackProxyConversionSourceFileName, filename, System.StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(_playbackProxyCancelToken, cancelToken);
        if (stillCurrent)
        {
            _playbackProxyCancelToken = null;
            _playbackProxyConversionInFlight = false;
            m_playbackProxyConversionSourceFileName = null;
            SetConversionProgressVisible(false);
        }
        if (!stillCurrent || cancelToken.CancelRequested)
            yield break;

        if (!string.Equals(m_fileName, filename, System.StringComparison.OrdinalIgnoreCase))
            yield break;

        if (result != null && result.Success && !string.IsNullOrWhiteSpace(result.OutputPath) && System.IO.File.Exists(result.OutputPath))
        {
            m_playbackProxyFileName = result.OutputPath;
            m_playbackProxySourceFileName = filename;
            RTQuickMessageManager.Get().ShowMessage("Converted video for Windows playback");
            PlayMovieDirect(filename, m_playbackProxyFileName, forceLoad);
            yield break;
        }

        string err = result != null ? result.Error : "unknown error";
        Debug.LogWarning("PicMovie preview proxy failed for " + filename + "\n" + err);
        RTQuickMessageManager.Get().ShowMessage("FFmpeg video conversion failed. Check utils/ffmpeg/bin.");
        PlayMovieDirect(filename, filename, forceLoad);
    }

    private void PlayMovieDirect(string sourceFilename, string playbackFilename, bool forceLoad = false)
    {
        try
        {
            if (RTUtil.IsMemoryLow())
            {
                System.GC.Collect();
                Resources.UnloadUnusedAssets();
                if (RTUtil.IsMemoryLow())
                {
                    RTQuickMessageManager.Get().ShowMessage("Low memory warning - video playback may be affected");
                    return;
                }
            }

            CleanupVideoResources();
            SetConversionProgressVisible(false);
            m_fileName = sourceFilename;
            m_playbackFileName = playbackFilename;
            _bDidCleanupSoAllowReload = false;
            _movieObject.SetActive(true);
            _wantPlaying = true; // every load path auto-plays

            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = playbackFilename;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;
            // Defensive re-assert — Start() sets these too, but if the prefab is
            // re-imported or another script touches the VideoPlayer at runtime we
            // want to be certain the first prepare/play uses the right mode and
            // doesn't race ahead of the first decoded frame.
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.waitForFirstFrame = true;

            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.loopPointReached -= OnVideoLoop;
            _videoPlayer.loopPointReached += OnVideoLoop;
            _videoPlayer.seekCompleted -= OnSeekCompleted;
            _videoPlayer.seekCompleted += OnSeekCompleted;
            _videoPlayer.controlledAudioTrackCount = 1;
            _videoPlayer.EnableAudioTrack(0, true);
            // Re-bind the AudioSource each play — controlledAudioTrackCount is a
            // serialized field that can reset the per-track target bindings.
            if (_audioSource != null)
            {
                _videoPlayer.SetTargetAudioSource(0, _audioSource);
                ApplyClipVolume();
            }
            if (forceLoad && _audioSource != null)
                _audioSource.mute = true;

            _videoPlayer.Prepare();
            StartCoroutine(FirstPrepareWatchdog(_playGeneration));
        }
        catch (System.Exception e)
        {
            HandleVideoError($"Critical error in PlayMovie: {e.Message}");
            CleanupVideoResources();
        }
    }

    // The SECOND prepare already has SecondPrepareSafetyNet, but nothing guarded the
    // FIRST one: if Media Foundation wedges it (no prepareCompleted, no errorReceived),
    // the quad stayed black forever and reloads couldn't recover it. Cancel the wedged
    // prepare and let Update()'s lazy reload retry after a growing backoff, so a
    // transient decoder squeeze self-heals once other videos release their sessions.
    private IEnumerator FirstPrepareWatchdog(int generation)
    {
        yield return new WaitForSeconds(FIRST_PREPARE_TIMEOUT_SECONDS);

        if (generation != _playGeneration)
            yield break; // superseded by a newer play/cleanup

        if (_renderTexture != null || (_videoPlayer != null && _videoPlayer.isPlaying))
            yield break; // first prepare completed normally

        _consecutivePrepareFailures++;
        float backoffSeconds = Mathf.Min(5f * _consecutivePrepareFailures, 30f);
        Debug.LogWarning("PicMovie: video prepare timed out for " + m_playbackFileName
            + " (attempt " + _consecutivePrepareFailures + "), retrying in " + backoffSeconds + "s");

        if (_videoPlayer != null)
            _videoPlayer.Stop();
        _bDidCleanupSoAllowReload = true;
        _nextAutoReloadTime = Time.time + backoffSeconds;
    }

    private bool CanReusePlaybackProxy(string sourceFilename)
    {
        return !string.IsNullOrWhiteSpace(m_playbackProxyFileName)
            && !string.IsNullOrWhiteSpace(m_playbackProxySourceFileName)
            && string.Equals(m_playbackProxySourceFileName, sourceFilename, System.StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(m_playbackProxyFileName);
    }

    private bool IsUsingPlaybackProxy()
    {
        return !string.IsNullOrWhiteSpace(m_playbackFileName)
            && !string.IsNullOrWhiteSpace(m_playbackProxyFileName)
            && string.Equals(m_playbackFileName, m_playbackProxyFileName, System.StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyClipVolume()
    {
        if (_audioSource == null)
            return;

        GameLogic gameLogic = GameLogic.Get();
        _audioSource.volume = gameLogic != null ? gameLogic.GetClipVolume() : 1f;
    }

    private bool TryStartPlaybackProxyAfterVideoError(string message)
    {
        if (_playbackProxyConversionInFlight)
            return true;
        if (IsUsingPlaybackProxy())
            return false;
        if (string.IsNullOrWhiteSpace(m_fileName) || !System.IO.File.Exists(m_fileName))
            return false;
        if (!FfmpegTool.IsSupportedVideoExtension(m_fileName))
            return false;
        if (string.Equals(m_playbackProxyAttemptedSourceFileName, m_fileName, System.StringComparison.OrdinalIgnoreCase))
            return false;

        Debug.LogWarning("PicMovie VideoPlayer failed, trying FFmpeg preview proxy: " + message);
        var info = new FfmpegTool.VideoInfo { Path = m_fileName, Fps = 30 };
        StartCoroutine(CreatePlaybackProxyAndPlay(m_fileName, false, info, "Windows could not play this video; converting..."));
        return true;
    }

    private void CancelPlaybackProxyConversion()
    {
        if (_playbackProxyCancelToken != null)
        {
            _playbackProxyCancelToken.Cancel();
            _playbackProxyCancelToken = null;
        }
        _playbackProxyConversionInFlight = false;
        m_playbackProxyConversionSourceFileName = null;
        SetConversionProgressVisible(false);
    }

    private void DeletePlaybackProxyIfNeeded(string keepSourceFilename = null)
    {
        if (string.IsNullOrWhiteSpace(m_playbackProxyFileName))
            return;

        if (!string.IsNullOrWhiteSpace(keepSourceFilename)
            && string.Equals(m_playbackProxySourceFileName, keepSourceFilename, System.StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(m_playbackProxyFileName))
        {
            return;
        }

        try { System.IO.File.Delete(m_playbackProxyFileName); } catch { }
        m_playbackProxyFileName = null;
        m_playbackProxySourceFileName = null;
    }

    private void SetConversionProgress(float progress, string message)
    {
        _showingConversionProgress = true;
        if (_movieObject != null)
            _movieObject.SetActive(true);
        if (_progressBarContainer != null)
            _progressBarContainer.SetActive(true);
        if (_progressBarFill != null)
            _progressBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        if (_progressBarTimeText != null)
            _progressBarTimeText.text = string.IsNullOrWhiteSpace(message) ? "Converting video..." : message;
        if (_playPauseButtonText != null)
            _playPauseButtonText.text = "...";
        PositionProgressBar();
    }

    private void SetConversionProgressVisible(bool visible)
    {
        _showingConversionProgress = visible;
        if (visible)
        {
            SetConversionProgress(0f, "Converting video...");
        }
        else if (_progressBarContainer != null)
        {
            _progressBarContainer.SetActive(false);
        }
    }



    private void OnVideoError(VideoPlayer source, string message)
    {
        if (TryStartPlaybackProxyAfterVideoError(message))
            return;
        HandleVideoError(message);
    }

    public void UnloadTheMovieToSaveMemory()
    {
        CleanupVideoResources();
        _bDidCleanupSoAllowReload = true;
        // An explicit unload/reload request (the "\" hotkey) should retry right away,
        // not wait out a backoff earned by an earlier wedged prepare.
        _nextAutoReloadTime = 0f;
    }

    private void HandleVideoError(string message)
    {
        // Debug.LogError($"VideoPlayer error for {_videoPlayer.url}: {message}");
        //_movieObject.SetActive(false);
        RTQuickMessageManager.Get().ShowMessage("Can't play the video.  Corrupted?  Make sure the length is a valid #");

        GameLogic.Get().AskAllMoviePicsToUnloadTheMovieToSaveMemory();
        
        // Clean up handlers
        _videoPlayer.prepareCompleted -= OnVideoPrepared;
        _videoPlayer.errorReceived -= OnVideoError;
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        try
        {
            // Second prepare callback — decoder is now bound to the real RT, safe to play.
            if (_waitingForSecondPrepare)
            {
                _waitingForSecondPrepare = false;
                source.Play();
                return;
            }

            // First prepare callback — we finally know the video dimensions, so build
            // the RT and bind it, then re-prepare to give the decoder a chance to
            // initialize against the actual target.
            _consecutivePrepareFailures = 0;
            _nextAutoReloadTime = 0f;
            _renderTexture = new RenderTexture((int)source.width, (int)source.height, 0);
            if (!_renderTexture.Create())
            {
                throw new System.Exception("Failed to create render texture");
            }
            _videoPlayer.targetTexture = _renderTexture;

            Material newMaterial = new Material(_materialTemplate);
            if (newMaterial == null)
            {
                HandleVideoError("newMaterial failed to init. Out of mem?");
                return;
            }
            newMaterial.mainTexture = _renderTexture;
            _renderer.material = newMaterial;
            m_movieSize = new Vector2Int((int)source.width, (int)source.height);
            float videoWidth = source.width;
            float videoHeight = source.height;
            Vector3 scale = _movieObject.transform.localScale;
            scale.y = scale.x * ((float)videoHeight / videoWidth);
            _movieObject.transform.localScale = scale;

            // Stop() flips isPrepared back to false so Prepare() actually re-runs and
            // fires the second prepareCompleted — without this, Prepare() on an
            // already-prepared player is a no-op and the second callback never fires
            // (which broke auto-play).
            _waitingForSecondPrepare = true;
            source.Stop();
            source.Prepare();
            // Safety net: if for any reason the second callback doesn't fire (some
            // Unity 6 builds skip it even after Stop), kick playback ourselves after
            // a short delay so the video still auto-plays.
            StartCoroutine(SecondPrepareSafetyNet(source));
        }
        catch (System.Exception e)
        {
            HandleVideoError($"Error in OnVideoPrepared: {e.Message}");
        }
    }

    private IEnumerator SecondPrepareSafetyNet(VideoPlayer source)
    {
        // Give the decoder a moment to re-prepare against the freshly bound RT.
        // 0.25s is plenty for a local file decode and short enough that the user
        // won't notice if the normal prepareCompleted path is already firing.
        yield return new WaitForSeconds(0.25f);
        if (_waitingForSecondPrepare && source != null && !source.isPlaying)
        {
            _waitingForSecondPrepare = false;
            source.Play();
        }
    }
}

