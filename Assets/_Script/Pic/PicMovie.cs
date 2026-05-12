using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class PicMovie : MonoBehaviour
{
    public Material _materialTemplate; // We'll use this to copy from
    public VideoPlayer _videoPlayer;
    public GameObject _movieObject;
    private RenderTexture _renderTexture; // We'll create this dynamically
    public Renderer _renderer;
    string m_fileName;
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
    const float PROGRESS_BAR_HEIGHT = 8f;

    void Start()
    {
        if (_videoPlayer == null)
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        }

        _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
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
    public Vector2Int GetMovieSize() { return m_movieSize; }
    public void TogglePlay()
    {
        if (!IsMovie())
        {
            //show message
            RTQuickMessageManager.Get().ShowMessage("No movie loaded");
            return;
        }
        if (_videoPlayer.isPlaying)
        {
            RTQuickMessageManager.Get().ShowMessage("Pausing movie");
            _videoPlayer.Pause();
        }
        else
        {
            RTQuickMessageManager.Get().ShowMessage("Playing movie");
            _videoPlayer.Play();
        }
    }

    public string GetFileName()
    {
        return m_fileName;
    }

    public string GetFileNameWithoutPath()
    {
        return System.IO.Path.GetFileName(m_fileName);
    }
    public string GetFileExtensionOfMovie()
    {
        return System.IO.Path.GetExtension(m_fileName);
    }

    public void SaveMovie(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            // Original behavior - just remove temp_ prefix
            string newFileName = m_fileName.Replace("temp_", "");
            RTUtil.CopyFile(m_fileName, newFileName);
        }
        else
        {
            // Extract just the filename without the path
            string fileName = System.IO.Path.GetFileName(m_fileName);

            // Remove temp_ prefix if it exists
            fileName = fileName.Replace("temp_", "");

            // Combine the provided path with the filename
            string newFilePath = System.IO.Path.Combine(Config.Get().GetBaseFileDir(path) + "/", fileName);

            RTUtil.CopyFile(m_fileName, newFilePath);
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



        //if app doesn't have focus, exit
        if (!Application.isFocused)
        {
            return;
        }

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
            if (_bDidCleanupSoAllowReload && m_fileName != null && m_fileName.Length > 0 && _renderTexture == null && isVisible)
            {
                PlayMovie(m_fileName);
                _bDidCleanupSoAllowReload = false;
            }

            if (_videoPlayer.isPlaying)
            {
                GameObject go = GameLogic.Get().GetPicWereHoveringOver();
                // GetPicWereHoveringOver() does a 2D physics raycast that ignores UI canvases,
                // so it will happily report "hovering" even when an overlay panel (AI Chat,
                // settings dialogs, etc.) is sitting on top of the movie - which would
                // unmute audio for a video the user can't actually see. Treat any UI
                // canvas in front of the mouse as "not hovering" so the audio stays muted.
                if (go == gameObject && !IsMouseObscuredByOtherUI())
                {
                    _videoPlayer.SetDirectAudioMute(0, GameLogic.Get().GetGlobalMute());

                }
                else
                {
                    _videoPlayer.SetDirectAudioMute(0, true);
                }
            }

            UpdateProgressBar();
        }

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
        btn.onClick.AddListener(TogglePlay);

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

        EventTrigger trigger = bgObj.AddComponent<EventTrigger>();
        EventTrigger.Entry pointerDown = new EventTrigger.Entry();
        pointerDown.eventID = EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => OnProgressBarClicked((PointerEventData)data, bgRect));
        trigger.triggers.Add(pointerDown);

        EventTrigger.Entry drag = new EventTrigger.Entry();
        drag.eventID = EventTriggerType.Drag;
        drag.callback.AddListener((data) => OnProgressBarClicked((PointerEventData)data, bgRect));
        trigger.triggers.Add(drag);

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

    void OnProgressBarClicked(PointerEventData eventData, RectTransform barRect)
    {
        if (_videoPlayer == null || _videoPlayer.length <= 0) return;

        Camera cam = _picMainScript.GetCamera();
        if (cam == null) cam = Camera.main;

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(barRect, eventData.position, cam, out localPoint))
            return;

        float normalized = Mathf.Clamp01((localPoint.x + barRect.rect.width * barRect.pivot.x) / barRect.rect.width);
        _videoPlayer.time = normalized * _videoPlayer.length;
    }

    void UpdateProgressBar()
    {
        if (_progressBarContainer == null) return;

        bool shouldShow = IsMovie() && _videoPlayer.length > 0;
        if (_progressBarContainer.activeSelf != shouldShow)
            _progressBarContainer.SetActive(shouldShow);

        if (!shouldShow) return;

        // Position the bar just below the actual movie quad
        RectTransform canvasRect = _picMainScript.GetCanvas().GetComponent<RectTransform>();
        float movieBottomWorld = _movieObject.transform.position.y
            - _movieObject.transform.lossyScale.y * 0.5f;
        Vector3 localInCanvas = canvasRect.InverseTransformPoint(
            new Vector3(_movieObject.transform.position.x, movieBottomWorld, _movieObject.transform.position.z));

        RectTransform containerRect = _progressBarContainer.GetComponent<RectTransform>();
        containerRect.anchoredPosition = new Vector2(0, localInCanvas.y);
        containerRect.sizeDelta = new Vector2(canvasRect.sizeDelta.x, PROGRESS_BAR_HEIGHT);

        double currentTime = _videoPlayer.time;
        double totalTime = _videoPlayer.length;
        float progress = Mathf.Clamp01((float)(currentTime / totalTime));

        _progressBarFill.rectTransform.anchorMax = new Vector2(progress, 1);
        _progressBarTimeText.text = $"{currentTime:F1}s / {totalTime:F1}s";

        if (_playPauseButtonText != null)
            _playPauseButtonText.text = _videoPlayer.isPlaying ? "\u2590\u2590" : "\u25B6";
    }

    public bool IsMovie()
    {
        return _movieObject.activeSelf;
    }

    public void KillMovie()
    {
        DeleteMovieIfNeeded();
        CleanupVideoResources();
        _bDidCleanupSoAllowReload = false;
        m_bAutoDeleteFileWhenDone = true;
        m_fileName = null;
        m_movieSize = new Vector2Int(0, 0);
        _movieObject.SetActive(false);

        if (_progressBarContainer != null)
            _progressBarContainer.SetActive(false);
    }

    private void CleanupVideoResources()
    {
        if (_videoPlayer.isPlaying)
        {
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
            // Pause playback and show warning
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
        if (!forceLoad && (!Application.isFocused || !_picMainScript.IsVisible()))
        {
        
            m_fileName = filename;
            _movieObject.SetActive(true);

            _bDidCleanupSoAllowReload = true;
            return; //don't play it now
        }

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
            m_fileName = filename;
            _bDidCleanupSoAllowReload = false;
            _movieObject.SetActive(true);

            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = filename;
            _videoPlayer.isLooping = true;
            _videoPlayer.playOnAwake = false;  // Changed to false
            
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.loopPointReached -= OnVideoLoop;
            _videoPlayer.loopPointReached += OnVideoLoop;
            //_videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.controlledAudioTrackCount = 1;
            //Debug.Log("Audio track count: " + _videoPlayer.audioTrackCount);
            //_videoPlayer.SetDirectAudioMute(0, false);
            //_videoPlayer.SetDirectAudioVolume(0, 1.0f);
            if (forceLoad)
                _videoPlayer.SetDirectAudioMute(0, true);
            

            _videoPlayer.Prepare();
        }
        catch (System.Exception e)
        {
            HandleVideoError($"Critical error in PlayMovie: {e.Message}");
            CleanupVideoResources();
        }
    }



    private void OnVideoError(VideoPlayer source, string message)
    {
        HandleVideoError(message);
    }

    public void UnloadTheMovieToSaveMemory()
    {
        CleanupVideoResources();
        _bDidCleanupSoAllowReload = true;
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

            source.Play();
        }
        catch (System.Exception e)
        {
            HandleVideoError($"Error in OnVideoPrepared: {e.Message}");
        }
    }
}

