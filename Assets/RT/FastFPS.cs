using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//Super simple FPS counter for VR as my fancy one doesn't work right there

public class FastFPS : MonoBehaviour
{
    int _frameCounter;
    float _oneSecondTimer;

    TMPro.TextMeshProUGUI _textMeshPro;

    void Start()
    {
        _textMeshPro = GetComponent<TMPro.TextMeshProUGUI>();
        if (_textMeshPro == null)
        {
            Debug.Log("Put this script on a Textmeshpro text thingie");
        }
        _oneSecondTimer = Time.unscaledTime;
    }


    void Update()
    {

        _frameCounter++;

        if (Time.unscaledTime > _oneSecondTimer + 1)
        {
            //a second has passed, update with the # of frames we were able to show
            _textMeshPro.text = "FPS: " + _frameCounter;
            _frameCounter = 0;
            _oneSecondTimer = Time.unscaledTime;
        }

    }
}
