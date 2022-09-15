using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//********** DON'T USE THIS!  Use RTMessageManager instead, that version is more powerful.  This still exists
//for compatibility with older stuff I did.

/*
 
 Created by Seth A. Robinson - rtsoft.com 2013
 
 A way to schedule (in seconds) calls to script functions on objects (that must have unique names)
 
 TO SETUP:
 
 Create a GameObject in unity called RTEventManager and attach this script
 
 TO USE:
 
 RTEventManager.Get().Schedule("nameOfGameObject", "FunctionName", secondsToCallItIn, any single parm);
 
 //So, to use with audiomanager to play a sfx in two seconds:
 
 RTEventManager.Get().Schedule(RTAudioManager.GetName(), "PlayMusic", 2, "audio/somesfx");
 
 //To send multiple parms, you can send a RTDB object which is a key/data pair dictionary you can send
 
 //Example of sending multiple parms with RTAudioManager's PlayEx function:
 RTEventManager.Get().Schedule(RTAudioManager.GetName(), "PlayEx", 2, new RTDB("fileName", "chalk",
			"volume", 1.0f, "pitch", 2.0f));
 
 */

public class RTEvent
{
	
	public void Send()
	{
		GameObject o = GameObject.Find(targetName);
		
		if (!o)
		{
			Debug.Log("Can't find gameobject "+targetName);	
			return;
		}
		
		o.SendMessage(targetFunctionName, value, SendMessageOptions.RequireReceiver);	
	}
	
	public object value;
	public float deliveryTime;
	public string targetName;
	public string targetFunctionName;
	
}

public class RTEventManager : MonoBehaviour 
{

    //TODO: Change this to a LinkedList.  Screw whoever named an array a list in C#.  Seriously?
	List<RTEvent> _events;
		
	static RTEventManager _this = null;

	
	public RTEventManager()
	{
		_events = new List<RTEvent>();
		_this = this;
	}
	
	public void Start()
	{
		gameObject.name = "RTEventManager";
		//Debug.Log("RTEventManager initted initted, gameobject we're in renamed to RTEventManager initted");
	}
	public void Schedule(string objectName, string functionName, float time)
	{
		Schedule(objectName, functionName, time, null);	
	}
		
	public void Schedule(string targetName, string targetFunctioName, float time, object value)
	{
		//Debug.Log ("Scheduling "+targetName);
		
		RTEvent e = new RTEvent();
		e.targetName = targetName;
		e.targetFunctionName = targetFunctioName;
		e.deliveryTime = Time.time+time;
		e.value = value;
		
		_events.Add(e);
	}
	
	public void Update()
	{
		//Debug.Log("List size is "+m_events.Count);
		
		//make a copy so we can safely remove events while iterating
		List<RTEvent> tempList = new List<RTEvent>(_events);
		
		foreach (RTEvent e in tempList) // Loop through List with foreach
		{
		    if (e.deliveryTime < Time.time)
			{
				e.Send();
				_events.Remove(e);
			}
		}

	}
	
	public static RTEventManager Get()
	{
		if (!_this)
		{
            //Actually, let's just create it
            _this = new GameObject("RTEventManager").AddComponent<RTEventManager>();
		}
		return _this;
	}
	
	public static string GetName()
	{
		return Get ().name;
	}
	
}
