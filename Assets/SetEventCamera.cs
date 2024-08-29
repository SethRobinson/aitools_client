using UnityEngine;

public class SetEventCamera : MonoBehaviour
{
    private void Start()
    {
        SetCameraToActiveSceneCamera();
    }

    private void SetCameraToActiveSceneCamera()
    {
        Canvas canvas = GetComponent<Canvas>();

        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
        {
            Camera activeCamera = FindActiveCamera();

            if (activeCamera != null)
            {
                canvas.worldCamera = activeCamera;
                Debug.Log("Event Camera set to the active scene camera.");
            }
            else
            {
                Debug.LogWarning("No active camera found in the scene.");
            }
        }
        else
        {
            Debug.LogWarning("Canvas is not set to World Space or Canvas component is missing.");
        }
    }

    private Camera FindActiveCamera()
    {
        // Try to find the main camera first
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            return mainCamera;
        }

        // If no main camera, search for any camera in the scene
        Camera[] cameras = GameObject.FindObjectsOfType<Camera>();

        if (cameras.Length > 0)
        {
            return cameras[0]; // Return the first camera found
        }

        return null;
    }
}
