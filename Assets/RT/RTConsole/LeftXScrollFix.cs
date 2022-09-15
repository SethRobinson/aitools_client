using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//I don't know why I need this .. (crawls into a ball and cries)

public class LeftXScrollFix : MonoBehaviour
{

    Vector3 _originalLocalPos;

  	// Use this for initialization
	void Start ()
    {
        _originalLocalPos = transform.localPosition;
        _originalLocalPos.x += 5; //the evil tweak so the letters on the left aren't cutoff
    }
	
	// Update is called once per frame
	void Update ()
    {
        Vector3 vTemp = transform.localPosition;
        vTemp.x = _originalLocalPos.x;


        transform.localPosition = vTemp;
	}
}
