using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

/*
    RTMessageManager by Seth A. Robinson 10/16/2018

    Improved event manager to call any method  with a delay  
    
    - Won't call on a deleted game object
    - Gives clear message if function can't be called
    - works with any parms
    - works with instanced classes, static classes, lambdas, etc
    - can remove messages later
    - first parm is time is seconds to deliver, the rest are the parms to whatever method you're calling
    - Note: For parms that can be interpreted more than one way (like 1 can be an int, float, double, etc) than you need to cast or make clear which kind of 
      parm it is, or it will give a weird compile-time error because it doesn't match the method being sent
    - RTMessageManager creates itself on first use if needed
     

    NOTE:  It only works with functions that don't have return arguements!

    Usage:

    Calling a global:

    RTMessageManager.Get().Schedule(1, Spawn);
    RTMessageManager.Get().Schedule(1, SpawnByName, "A string parm");

    Calling a function in a script in a gameobject (or not a gameobject)

    RTMessageManager.Get().Schedule(1, someObj.Spawn);
    RTMessageManager.Get().Schedule(1, this.Spawn);
    RTMessageManager.Get().Schedule(1, someObj.SpawnByName, "A string parm");


    //Using RTAudioManager to play sfx with a delay:

    RTMessageManager.Get().Schedule(1, RTAudioManager.Get().Play, _drinkClip); //plays clip in 1 seconds
    RTMessageManager.Get().Schedule(2, RTAudioManager.Get().Play, "crap.wav"); //plays crap.wav from Resources dir in 2 seconds
   
    //Note that you have to include ALL optional parms, and clearly define int vs float (via the f at the end)
    RTMessageManager.Get().Schedule(1, RTAudioManager.Get().PlayEx, "blip_lose2", 1.0f, 1.0f, false, 0.0f);
  
    //Play audio in 3.3 seconds

    RTMessageManager.Get().Schedule(3.3f , RTAudioManager.Get().PlayEx, _drinkClip, 1.0f, 1.5f);

    To remove previously scheduled messages:
    RTMessageManager.Get().RemoveScheduledCalls(Spawn); //works if if the method had no parms

    //you have to "describe" the function type when removing calls with parms, annoying yes but I don't know another way, I rarely remove messages so meh
    RTMessageManager.Get().RemoveScheduledCalls((System.Action<string>)SpawnByName);
 
 */

public class RTMessage
{

    public void Send()
    {
        if (_delegate != null)
        {
            //Debug.Log("Fun being called " + _delegate.Method+" static:"+ _delegate.Method.IsStatic+" name: "+
            //    _delegate.Method.Name+" generic method: "+ _delegate.Method.IsGenericMethod);

            try
            {
                if ((MonoBehaviour)_delegate.Target == null)
                {
                    //script we were calling back in gone
                    //Hmm, maybe we should have an option to turn this warning on...
                    //Debug.Log("RTMessageManager Warning: RTMessage can't call " + _delegate.Method.Name + " - " + _delegate.Method + ", it no longer exists");
                    return;
                }
            }
            catch
            {
            }

            if (_parms == null)
            {
                //optimization for calls with no parms, in theory at least.  I didn't notice a diff
                ((Action)_delegate).Invoke();
            } else
            {
                _delegate.DynamicInvoke(_parms);
            }
            
            
        }
      
    }

    public Delegate _delegate;

    public object value;
    public float deliveryTime;
    public object[] _parms;
    
}

public class RTMessageManager : MonoBehaviour
{
    static RTMessageManager _this = null;
    LinkedList<RTMessage> _events;

    float _curTime = 0;
    float _timeMod = 1.0f; //0 is pause, 2 would be 2x speed


    public void Awake()
    {
        _events = new LinkedList<RTMessage>();
        _this = this;
    }

    public void Start()
    {
        gameObject.name = "RTMessageManager";
        DontDestroyOnLoad(gameObject);

        //Debug.Log("RTMessageManager initted initted, gameobject we're in renamed to RTMessageManager initted");
    }

    void AddMessage(Delegate method, float delayInSeconds, object[] parms)
    {
        RTMessage r = new RTMessage();
        r._delegate = method;
        r.deliveryTime = _curTime + delayInSeconds;
        r._parms = parms;

        //sort on insert

        var node = _events.First;

        while (node != null)
        {
            if (node.Value.deliveryTime > r.deliveryTime)
            {
                //ok, insert before this guy
                break;
            }
            node = node.Next;
        }

        if (node != null)
        {
            _events.AddBefore(node, r);
        } else
        {
            _events.AddLast(r);
        }

    }

    public void Schedule(float delayInSeconds, Action method)
    {
        AddMessage(method, delayInSeconds, null);
    }

    public void Schedule<T>(float delayInSeconds, Action<T> method, T o1)
    {
        AddMessage(method, delayInSeconds, new object[] { o1 });
    }

    public void Schedule<T,T2>(float delayInSeconds, Action<T,T2> method, T o1, T2 o2)
    {
        AddMessage(method, delayInSeconds, new object[] { o1, o2 });
    }

    public void Schedule<T, T2, T3>(float delayInSeconds, Action<T, T2, T3> method, T o1, T2 o2, T3 o3)
    {
        AddMessage(method, delayInSeconds, new object[] { o1, o2, o3 });
    }

    public void Schedule<T, T2, T3, T4>(float delayInSeconds, Action<T, T2, T3, T4> method, T o1, T2 o2, T3 o3, T4 o4)
    {
        AddMessage(method, delayInSeconds, new object[] { o1, o2, o3, o4 });
    }

    public void Schedule<T, T2, T3, T4, T5>(float delayInSeconds, Action<T, T2, T3, T4, T5> method, T o1, T2 o2, T3 o3, T4 o4, T5 o5)
    {
        AddMessage(method, delayInSeconds, new object[] { o1, o2, o3, o4, o5 });
    }

    public void Schedule<T, T2, T3, T4, T5, T6>(float delayInSeconds, Action<T, T2, T3, T4, T5, T6> method, T o1, T2 o2, T3 o3, T4 o4, T5 o5, T6 o6)
    {
        AddMessage(method, delayInSeconds, new object[] { o1, o2, o3, o4, o5, o6 });
    }

    void RemoveScheduledCalls(Delegate del)
    {
        var node = _events.First;
        while (node != null)
        {
            var next = node.Next;

            if (node.Value._delegate == del)
            {
                _events.Remove(node);
            }
            node = next;
        }
    }

    public void RemoveScheduledCalls(Action del)
    {
        RemoveScheduledCalls((Delegate)del);
    }

    //example of usage, as compiler is too dumb to know what it is:
    // RTMessageManager.Get().RemoveScheduledCalls((System.Action<string>)SpawnOneParm);
    public void RemoveScheduledCalls<T>(T del)
    {
        RemoveScheduledCalls((Delegate)(object)del);
    }
    
    public static RTMessageManager Get()
    {
        if (!_this)
        {
            //Actually, let's just create it
            _this = new GameObject("RTMessageManager").AddComponent<RTMessageManager>();
        }
        return _this;
    }

    public void SetTimeMod(float mod)
    {
        _timeMod = mod;
    }

    // Update is called once per frame
    void Update()
    {

        _curTime += Time.deltaTime * _timeMod;

        var node = _events.First;
        while (node != null)
        {
            var next = node.Next;

            if (node.Value.deliveryTime < _curTime)
            {
                var tempNode = node;
                _events.Remove(node);
                tempNode.Value.Send();
                
            } else
            {
                //list is sorted, so we can safely assume there are no other events right now
                break;
            }
            node = next;
        }
    }
}
