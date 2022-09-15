using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/*Simple way to read input from the left/right VR controllers, just attach this script to a game object


    //Must have Unity's XRInteraction.Toolkit package installed.

    Read it like this:
  
        if (RTXRInputManager.Left().IsButtonDown(InputHelpers.Button.SecondaryButton))
        {
            Debug.Log("Secondary LEFT is being held down now");
        }

        if (RTXRInputManager.Left().IsButtonPressed(InputHelpers.Button.SecondaryButton))
        {
            Debug.Log("Secondary LEFT pressed");
        }

        if (RTXRInputManager.Left().IsButtonReleased(InputHelpers.Button.SecondaryButton))
        {
            Debug.Log("Secondary LEFT released");
        }

   
    Author:  Seth A. Robinson 2/24/2020

    Check InputHelpers.Button for the full list of buttons.  Will return false for everything if the controller isn't there.
    */
public class RTXRInputManager : MonoBehaviour
{

    public enum eController
    {
        LEFT =0,
        RIGHT,

        //
        COUNT
    }

    public class ButtonState
    {
        public bool _down; //is down right now
        public bool _pressed;
        public bool _released;

    }
    public class DeviceState
    {

    
        ButtonState[] _buttons = new ButtonState[Enum.GetNames(typeof(InputHelpers.Button)).Length];

        public DeviceState()
        {

            Debug.Assert(Enum.GetNames(typeof(InputHelpers.Button)).Length < 150); //wtf happened?  why so many buttons?
           for (int i=0; i < _buttons.Length; i++)
            {
                _buttons[i] = new ButtonState();
            }
        }

        public void Update(InputDevice ?device)
        {
            bool result;

            for (int i=0; i < _buttons.Length; i++ )
            {

                if (InputHelpers.IsPressed((UnityEngine.XR.InputDevice)device, (InputHelpers.Button)i, out result))
                {
                    if (result == _buttons[i]._down)
                    {
                        _buttons[i]._pressed = false; //that can't be true now though
                        _buttons[i]._released = false;
                        continue; //who cares, nothing changed

                    }

                    _buttons[i]._down = result;

                    //something changed
                    if (result)
                    {
                        //button is now on
                        _buttons[i]._pressed = true;
                    } else
                    {
                        _buttons[i]._released = true;
                    }
                }
            }
        }

     
        public bool IsButtonDown(InputHelpers.Button button)
        {
            return _buttons[(int)button]._down;
        }
        public bool IsButtonPressed(InputHelpers.Button button)
        {
            return _buttons[(int)button]._pressed;
        }
        public bool IsButtonReleased(InputHelpers.Button button)
        {
            return _buttons[(int)button]._released;
        }
    }
  
 
    static RTXRInputManager _this;

    InputDevice?[] _devices = new InputDevice?[(int)eController.COUNT];

    DeviceState[] _state = new DeviceState[(int)eController.COUNT];

    bool _rightLeftIsToggled;

    void Awake()
       {
        _this = this;

        for (int i=0; i < (int)eController.COUNT; i++)
        {
            _state[i] = new DeviceState();
        }
       }

    static public RTXRInputManager Get() { return _this; }
    static public DeviceState Left() 
    {
        if (_this._rightLeftIsToggled)
        {
            return _this._state[(int)eController.RIGHT];
        }
        else
        {
            return _this._state[(int)eController.LEFT];
        }
    }
    static public DeviceState Right() 
    {
        if (!_this._rightLeftIsToggled)
        {
            return _this._state[(int)eController.RIGHT];
        }
        else
        {
            return _this._state[(int)eController.LEFT];
        }
    }

    public void ToggleLeftAndRight()
    {
        _rightLeftIsToggled = !_rightLeftIsToggled;
    }

    void Start()
    {
        Debug.Log("Scanning for XRVR controllers");
        List<InputDevice> allDevices = new List<InputDevice>();
        InputDevices.GetDevices(allDevices);
        foreach (InputDevice device in allDevices)
            InputDevices_deviceConnected(device);

        InputDevices.deviceConnected += InputDevices_deviceConnected;
        InputDevices.deviceDisconnected += InputDevices_deviceDisconnected;
    }

    private void OnDisable()
    {
        InputDevices.deviceConnected -= InputDevices_deviceConnected;
        InputDevices.deviceDisconnected -= InputDevices_deviceDisconnected;
     
    }

    int GetDeviceType(InputDevice device)
    {

        int hand = -1; //Unsupported

#if UNITY_2019_3_OR_NEWER
        if ((device.characteristics & InputDeviceCharacteristics.Left) != 0)
#else
                    if (connectedDevice.role == InputDeviceRole.LeftHanded)
#endif
        {
            //Debug.Log("Found left");
            hand = (int)eController.LEFT; //it's left hand
        }

#if UNITY_2019_3_OR_NEWER
        if ((device.characteristics & InputDeviceCharacteristics.Right) != 0)
#else
                    if (connectedDevice.role == InputDeviceRole.RightHanded)
#endif
        {
            //Debug.Log("Found left");
            hand = (int)eController.RIGHT; //it's right hand
        }

        return hand;
    }

    private void InputDevices_deviceConnected(InputDevice device)
    {

        Debug.Log("Device connection: "+device.ToString());
        int hand = GetDeviceType(device);
        
    
        if (hand == -1)
        {
            Debug.Log("Ignoring device, it's probably the headset or a tracker, add support in RTXRInputManager");
            return;
        } 

        Debug.Assert(_devices[hand] == null);
        if (_devices[hand] != null)
        {
            Debug.Log("Something wrong, we have two of the same hand controller?  Ignoring this device");
            //warn:  When it is disconnected, it will kill our real controller which probably isn't great.  But I don't know why this would ever happen so whatever
            return;
        }
        _devices[hand] = device;
    }

    private void InputDevices_deviceDisconnected(InputDevice device)
    {
        Debug.Log("Controller lost connection: " + device.ToString());

        int hand = GetDeviceType(device);

        if (hand == -1)
        {
            Debug.Log("Ignoring device, it's probably the headset or a tracker, add support in RTXRInputManager");
            return;
        }

        _devices[hand] = null;
    }


    // Update is called once per frame
    void Update()
    {
        for (int i=0; i < (int)eController.COUNT; i++)
        {
            if (_devices[i] == null) continue;

            _state[i].Update(_devices[i]);

        }
    }
}
