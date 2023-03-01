using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class SetCanvasToActiveCamera : MonoBehaviour
{

    void Awake()
    {
        // Start is called before the first frame update
        var s = gameObject.GetComponent<Canvas>();
        s.worldCamera = Camera.allCameras[0];
    }
}
