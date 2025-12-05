using System;
using System.Collections;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    private WebCamDevice[] devices;
    private WebCamTexture _texture;
    private int _currentCameraIndex = 8;

    private MeshRenderer m_meshRenderer;

    public static CameraManager _this;
    public Action<WebCamTexture> OnCameraStartedCallback;
    public Action<WebCamDevice[]> OnCameraInfoAvailableCallback;
    public Action<WebCamTexture> OnCameraDisplayedNewFrameCallback;
    float _restartCameraTimer;

    bool m_camStarted = false;

    // Requested capture settings (can be changed before starting)
    private int _requestedWidth = 1280;
    private int _requestedHeight = 720;
    private int _requestedFPS = 30;

    private void Awake()
    {
        _this = this;
    }

    public WebCamTexture GetWebCamTexture() { return _texture; }
    public static CameraManager Get() => _this;
    public int GetCurrentCameraIndex() { return _currentCameraIndex; }
    public bool IsOn() { return _texture != null; }

    public void SetRequestedResolution(int width, int height, int fps = 30)
    {
        _requestedWidth = Mathf.Max(1, width);
        _requestedHeight = Mathf.Max(1, height);
        _requestedFPS = Mathf.Max(1, fps);

        // If the camera is already running, restart to apply
        if (_texture != null)
        {
            StopCamera();
            _restartCameraTimer = Time.time + 0.1f;
        }
    }

    // Returns Unity-reported resolutions when available (iOS/Android typically).
    public Resolution[] GetAvailableResolutions(int deviceIndex)
    {
        if (deviceIndex < 0 || deviceIndex >= WebCamTexture.devices.Length) return null;
        return WebCamTexture.devices[deviceIndex].availableResolutions;
    }

    // Actual active resolution after Play()
    public Vector2Int GetActiveResolution()
    {
        if (_texture == null) return new Vector2Int(0, 0);
        return new Vector2Int(_texture.width, _texture.height);
    }

    public void InitCamera(MeshRenderer meshrendererToUse)
    {
        m_meshRenderer = meshrendererToUse;
        StartCoroutine(InitWebcams());
    }

    public void SetCameraByIndex(int index)
    {
        if (index != _currentCameraIndex)
        {
            _currentCameraIndex = index;
            StopCamera();
            _restartCameraTimer = Time.time + 1;
        }
    }

    private IEnumerator InitWebcams()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            devices = WebCamTexture.devices;
            Debug.Log("Found " + devices.Length + " webcams");
            for (int cameraIndex = 0; cameraIndex < devices.Length; ++cameraIndex)
            {
                Debug.Log("devices[cameraIndex].name: " + devices[cameraIndex].name + " Front facing: " + devices[cameraIndex].isFrontFacing);

                if (devices[cameraIndex].availableResolutions != null)
                {
                    foreach (Resolution res in devices[cameraIndex].availableResolutions)
                    {
                        Debug.Log("Res: X: " + res.width + " : Y: " + res.height + " FPS: " + res.refreshRateRatio);
                    }
                }
            }
            OnCameraInfoAvailableCallback?.Invoke(devices);
        }
        else
        {
            RTConsole.Log("no webcams found");
        }

        _restartCameraTimer = Time.time + 1;
    }

    public void StartCamera()
    {
        Debug.Log($"USER PERMISSION {Application.HasUserAuthorization(UserAuthorization.WebCam)}");
        m_camStarted = false;

        if (WebCamTexture.devices.Length > 0 && _currentCameraIndex < WebCamTexture.devices.Length)
        {
            WebCamDevice device = WebCamTexture.devices[_currentCameraIndex];

            // Request a specific resolution/FPS (Unity picks the closest supported)
            _texture = new WebCamTexture(device.name, _requestedWidth, _requestedHeight, _requestedFPS);

            m_meshRenderer.material.mainTexture = _texture;
            _texture.Play();
            Debug.Log($"START PLAYING requested {_requestedWidth}x{_requestedHeight}@{_requestedFPS}fps");
        }
        else
        {
            Debug.Log("Unable to start camera device id " + _currentCameraIndex + ", there are only " + WebCamTexture.devices.Length + " devices");
        }
    }

    public void StopCamera()
    {
        if (_texture != null)
        {
            _texture.Stop();
            _texture = null;
            m_camStarted = false;
            _restartCameraTimer = 0;
        }
    }

    private void OnDestroy()
    {
        StopCamera();
    }

    public void GrabFrame()
    {
        Texture2D texture = new Texture2D(_texture.width, _texture.height, TextureFormat.ARGB32, false);
        texture.SetPixels(_texture.GetPixels());
        texture.Apply();
        byte[] bytes = texture.EncodeToPNG();
        // File.WriteAllBytes("crap.png", bytes);
    }

    private void Update()
    {
        if (_texture != null)
        {
            _restartCameraTimer = 0;
            if (!m_camStarted)
            {
                if (_texture.didUpdateThisFrame)
                {
                    m_camStarted = true;
                    OnCameraStartedCallback?.Invoke(_texture);
                    Debug.Log($"Camera active at {_texture.width}x{_texture.height} ({_texture.deviceName})");
                }
            }
            else
            {
                OnCameraDisplayedNewFrameCallback?.Invoke(_texture);
            }
        }
        else
        {
            if (_restartCameraTimer != 0 && _restartCameraTimer < Time.time)
            {
                StartCamera();
                _restartCameraTimer = 0;
            }
        }
    }
}