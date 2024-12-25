using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

public class PicMovie : MonoBehaviour
{
    public Material _materialTemplate; // We'll use this to copy from
    public VideoPlayer _videoPlayer;
    public GameObject _movieObject;
    private RenderTexture _renderTexture; // We'll create this dynamically
    public Renderer _renderer;
    string m_fileName;

    // Start is called before the first frame update
    void Start()
    {
        if (_videoPlayer == null)
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        }
    }

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

            //disable and enable the videoplayer, helps fix a bug
            _videoPlayer.enabled = false;
            _videoPlayer.enabled = true;

            _videoPlayer.Play();
        }
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
    private void OnDestroy()
    {
        DeleteMovieIfNeeded();
        // Clean up video player events
        if (_videoPlayer != null)
        {
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoPlayer.Stop();
        }

        // Release render texture
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
    // Update is called once per frame
    
    public bool IsMovie()
    {
        return _movieObject.activeSelf;
    }

    private void CleanupVideoResources()
    {
        if (_videoPlayer.isPlaying)
        {
            _videoPlayer.Stop();
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        // Clear material reference
        if (_renderer.material != _materialTemplate)
        {
            Destroy(_renderer.material);
            _renderer.material = null;
        }

        System.GC.Collect();
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

    public void PlayMovie(string filename)
    {
        try
        {
            // Check memory before starting
            if (RTUtil.IsMemoryLow())
            {
                // Force garbage collection
                System.GC.Collect();
                Resources.UnloadUnusedAssets();

                // Check again after cleanup
                if (RTUtil.IsMemoryLow())
                {
                    RTQuickMessageManager.Get().ShowMessage("Low memory warning - video playback may be affected");

                    return;
                }
            }

            // Ensure cleanup of previous resources
            CleanupVideoResources();

        m_fileName = filename;

        _movieObject.SetActive(true);
            try
            {
                _renderTexture = new RenderTexture(1920, 1080, 0);
                if (!_renderTexture.Create())
                {
                    throw new System.Exception("Failed to create render texture");
                }
                _videoPlayer.targetTexture = _renderTexture;
            }
            catch (System.Exception e)
            {
                HandleVideoError($"Failed to initialize video resources: {e.Message}");
                return;
            }

            // Assign the RenderTexture to a material
            Material newMaterial = new Material(_materialTemplate);
            
            if (newMaterial == null)
            {
                HandleVideoError($"newMaterial failed to init.  Out of mem?");
                return;
            }
            newMaterial.mainTexture = _renderTexture;
            _renderer.material = newMaterial;

            // Configure video player with additional error handling
            ConfigureVideoPlayer(filename);
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

    private void HandleVideoError(string message)
    {
        // Debug.LogError($"VideoPlayer error for {_videoPlayer.url}: {message}");
        //_movieObject.SetActive(false);
        RTQuickMessageManager.Get().ShowMessage("Can't play the video.  Corrupted?  Make sure the length is a valid #");

        // Clean up handlers
        _videoPlayer.prepareCompleted -= OnVideoPrepared;
        _videoPlayer.errorReceived -= OnVideoError;
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        // Get the video dimensions
        float videoWidth = source.width;
        float videoHeight = source.height;

        // Calculate aspect ratio
        float aspectRatio = videoWidth / videoHeight;

        // Adjust the _movieObject scale to maintain aspect ratio, modifying Y only
        Vector3 scale = _movieObject.transform.localScale;
        scale.y = scale.x / aspectRatio;
        _movieObject.transform.localScale = scale;

        // Start playing the video
        source.Play();

        // Remove the event listener to prevent duplicate handling
        _videoPlayer.prepareCompleted -= OnVideoPrepared;
    }
}

