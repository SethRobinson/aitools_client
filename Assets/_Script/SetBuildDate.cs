using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SetBuildDate : MonoBehaviour
{

	// Use this for initialization
	void Start ()
    {
        TextMeshProUGUI tm = GetComponent<TextMeshProUGUI>();
        tm.text = "V"+Config.Get().GetVersionString()+" Compiled "+RTBuildInfo.Timestamp;
	}
	
	
}
