using UnityEngine;
using System.Collections;
using System.IO;
using System.Linq.Expressions;
using System;

public class CameraManager : MonoBehaviour
{
    private WebCamDevice[] devices;
    private WebCamTexture _texture;
    private int _currentCameraIndex = 8;

    private MeshRenderer m_meshRenderer;

    public static CameraManager _this;
    // Use this for initialization
    public Action<WebCamTexture> OnCameraStartedCallback;
    public Action<WebCamDevice[]> OnCameraInfoAvailableCallback;
    public Action<WebCamTexture> OnCameraDisplayedNewFrameCallback;
    float _restartCameraTimer;

    bool m_camStarted = false;
    private void Awake()
    {
        _this = this; 
    }
    public int GetCurrentCameraIndex() { return _currentCameraIndex; }
    public static CameraManager Get()
    {
        return _this;
    }

    void Start()
    {
        
    }

    public void InitCamera(MeshRenderer meshrendererToUse)
    {
        m_meshRenderer = meshrendererToUse;
        StartCoroutine(InitWebcams());
    }
    public bool IsOn()
    {
        return _texture != null;
    }

    public void SetCameraByIndex(int index)
    {

        if (index != _currentCameraIndex) 
        { 
           _currentCameraIndex= index;
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
                    //available on iOS/Android only I guess
                    foreach (Resolution res in devices[cameraIndex].availableResolutions)
                    {
                        Debug.Log("Res: X: " + res.width + " : Y: " + res.height + " FPS: " + res.refreshRateRatio);
                    }
                }

            }
            if (OnCameraInfoAvailableCallback != null)
                OnCameraInfoAvailableCallback.Invoke(devices);

        }
        else
        {
            RTConsole.Log("no webcams found");
        }

       _restartCameraTimer= Time.time + 1;
    }

    public void StartCamera()
    {
        Debug.Log($"USER PERMISSION {Application.HasUserAuthorization(UserAuthorization.WebCam)}");
        //Debug.Log($"DEVICES AMOUNT {WebCamTexture.devices.Length}");
        m_camStarted = false;

        if (WebCamTexture.devices.Length > 0 && _currentCameraIndex < WebCamTexture.devices.Length)
        {
            WebCamDevice device = WebCamTexture.devices[_currentCameraIndex];
            _texture = new WebCamTexture(device.name);
            //_display.texture = _texture;
          
            m_meshRenderer.material.mainTexture = _texture;
           _texture.Play();
            Debug.Log($"START PLAYING!");
        } else
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

            //OnCameraStartedCallback = null;
            m_camStarted = false;
            _restartCameraTimer = 0;
        }
    }

    //Texture2D tex = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
    //tex.SetPixels(webcamTexture.GetPixels());
    //    tex.Apply();

    private void OnDestroy()
    {
        // Deactivate our camera
        StopCamera();
    }

    public void GrabFrame()
    {
        Texture2D texture = new Texture2D(_texture.width, _texture.height, TextureFormat.ARGB32, false);

        //Save the image to the Texture2D
        texture.SetPixels(_texture.GetPixels());
        texture.Apply();

        //Encode it as a PNG.
        byte[] bytes = texture.EncodeToPNG();

        //Save it in a file.
       // File.WriteAllBytes("crap.png", bytes);

        //StartCoroutine(PythonManager.Get().ProcessFrame());
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
                    if (OnCameraStartedCallback != null)
                        OnCameraStartedCallback.Invoke(_texture);
                }
            } else
            {
                //cam is running normally
               
                if (OnCameraDisplayedNewFrameCallback != null)
                    OnCameraDisplayedNewFrameCallback.Invoke(_texture);

            }

        } else
        {
            if (_restartCameraTimer != 0)
            {
                if (_restartCameraTimer < Time.time)
                {
                    StartCamera();
                    _restartCameraTimer = 0;
                }
            }
        }

        //if (Input.GetKeyDown(KeyCode.S))
        //{

        //    Debug.Log("Screenshot!");
        //    GrabFrame();

        //}
    }

}