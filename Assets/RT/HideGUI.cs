﻿using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]

public class HideGUI : MonoBehaviour
{

    public enum eStartupAlpha
    {
        DontChange,
        SetAlphaToOne,
            SetAlphaToZero
    }

    public eStartupAlpha _SetAlphaAtStart = eStartupAlpha.SetAlphaToZero;
    public bool SetInactive = true;

    public bool DisableInteractable = false;
    public bool DisableBlocksRaycasts = false;

 	// Use this for initialization
	void Start ()
    {

        switch (_SetAlphaAtStart)
        {
            case eStartupAlpha.DontChange:

                break;

            case eStartupAlpha.SetAlphaToOne:
                GetComponent<CanvasGroup>().alpha = 1.0f;
                break;

            case eStartupAlpha.SetAlphaToZero:
                GetComponent<CanvasGroup>().alpha = 0.0f;
                break;

        }

        //assume by now other scripts have had a chance to init their statics, etc, so we can go disable ourselves now
        if (SetInactive)
        {
            gameObject.SetActive(false);
        }

        if (DisableInteractable)
        {
            GetComponent<CanvasGroup>().interactable = false;
        }

        if (DisableBlocksRaycasts)
        {
            GetComponent<CanvasGroup>().blocksRaycasts = false;
        }

        Destroy(this);
    }

}
