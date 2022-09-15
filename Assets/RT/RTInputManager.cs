using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

/*
A simple input manager for multiple gamepads
*/

public class RTButtonState
{
    public enum eFriendlyName
    {
        BUTTON_UNKNOWN,
        BUTTON_LEFT,
        BUTTON_RIGHT,
        BUTTON_UP,
        BUTTON_DOWN,
        BUTTON_A,
        BUTTON_B,
        BUTTON_X,
        BUTTON_Y,
        BUTTON_START,
        BUTTON_SELECT,

        //add more above
        COUNT
    }

    public bool _bDown = false;
    public eFriendlyName _friendlyName = eFriendlyName.BUTTON_UNKNOWN;
    public int _rawButtonIndex;
}

public enum eRTButtonEvent
{
    DOWN,
    UP
}

public enum eRTAxis
{
    HORIZONTAL,
    VERTICAL
}


public class RTInputDevice
{
    public const int C_BUTTON_COUNT = 16;
    public const int C_AXIS_COUNT = 2; //just for movement

    public int _uniqueID; //what we call ourselves to outside people
    public int _unityGamepadID = -1; //what unity calls it
    KeyCode _buttonZeroGamepadKeycode = KeyCode.None;  //Joystick1Button0  or whatever is needed
    public int GetUniqueID() { return _uniqueID; }
    public int GetUnityGamepadID() { return _unityGamepadID; }

    float[] _axis = new float[C_AXIS_COUNT];
    RTButtonState[] _buttons = new RTButtonState[C_BUTTON_COUNT];
    KeyCode[] _altKeys = new KeyCode[(int)RTButtonState.eFriendlyName.COUNT];

    bool _bUsingAltKeys = false;

    public float GetAxis(eRTAxis axis) { return _axis[(int)axis]; }

    public event Action<RTButtonState, eRTButtonEvent> OnButtonEvent;

    bool _bGamepadAnalog = false; //if true, is proportional

    int GetButtonRawIndexFromFriendlyName(RTButtonState.eFriendlyName friendlyName)
    {
        for (int i = 0; i < C_BUTTON_COUNT; i++)
        {
            if (_buttons[i]._friendlyName == friendlyName)
            {
                return i;
            }
        }

        return -1;
    }

    public RTButtonState GetButtonState(RTButtonState.eFriendlyName friendlyName)
    {
        return _buttons[GetButtonRawIndexFromFriendlyName(friendlyName)];
    }

    public bool GetButton(RTButtonState.eFriendlyName friendlyName)
    {
        if (_buttons[GetButtonRawIndexFromFriendlyName(friendlyName)]._bDown) return true;

        if (_bUsingAltKeys)
        {
            if (Input.GetKey(_altKeys[(int)friendlyName]))
            {
                return true;
            }
        }
        return false;
    }


    public void InitGamepad(int unityGamepadID)
    {
        _unityGamepadID = unityGamepadID;

        switch (_unityGamepadID)
        {
            case 0: _buttonZeroGamepadKeycode = KeyCode.Joystick1Button0; break;
            case 1: _buttonZeroGamepadKeycode = KeyCode.Joystick2Button0; break;
            case 2: _buttonZeroGamepadKeycode = KeyCode.Joystick3Button0; break;
            case 3: _buttonZeroGamepadKeycode = KeyCode.Joystick4Button0; break;
            case 4: _buttonZeroGamepadKeycode = KeyCode.Joystick5Button0; break;
            case 5: _buttonZeroGamepadKeycode = KeyCode.Joystick6Button0; break;
            case 6: _buttonZeroGamepadKeycode = KeyCode.Joystick7Button0; break;
            case 7: _buttonZeroGamepadKeycode = KeyCode.Joystick8Button0; break;

            default:
                Debug.LogError("We don't handle this joystick yet");
                break;
        }
    }

    public void Init(int uniqueID)
    {
        _uniqueID = uniqueID;
    
        for (int i = 0; i < C_BUTTON_COUNT; i++)
        {
            _buttons[i] = new RTButtonState();
            _buttons[i]._rawButtonIndex = i;
        }

        //set some friendly names, this part would need to be tuned to each gamepad.. but Unity is adding new input code
        //so this will all be useless soon so faking for now

        _buttons[0]._friendlyName = RTButtonState.eFriendlyName.BUTTON_A;
        _buttons[1]._friendlyName = RTButtonState.eFriendlyName.BUTTON_B;

    }

    public void SetAltKeys(KeyCode left, KeyCode right, KeyCode up, KeyCode down, KeyCode button1, KeyCode button2)
    {
        _bUsingAltKeys = true;

        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_LEFT] = left;
        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_RIGHT] = right;
        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_UP] = up;
        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_DOWN] = down;

        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_A] = button1;
        _altKeys[(int)RTButtonState.eFriendlyName.BUTTON_B] = button2;
    }

    public void Update()
    {

        //process keys first if need be
        _axis[(int)eRTAxis.HORIZONTAL] = 0;
        _axis[(int)eRTAxis.VERTICAL] = 0;

        if (_buttonZeroGamepadKeycode != KeyCode.None)
        {
            _axis[(int)eRTAxis.HORIZONTAL] = Input.GetAxis("Joy" + (_unityGamepadID + 1) + "Axis1X");
            _axis[(int)eRTAxis.VERTICAL] = Input.GetAxis("Joy" + (_unityGamepadID + 1) + "Axis1Y");

            if (!_bGamepadAnalog)
            {
                Vector2 vTemp = new Vector2(_axis[(int)eRTAxis.HORIZONTAL], _axis[(int)eRTAxis.VERTICAL]);

                if (vTemp.magnitude > 0.2f)
                {
                    vTemp.Normalize();
                    _axis[(int)eRTAxis.HORIZONTAL] = vTemp.x;
                    _axis[(int)eRTAxis.VERTICAL] = vTemp.y;
                }
            }
           
        }

        if (_bUsingAltKeys)
        {
            if (Input.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_LEFT])) _axis[(int)eRTAxis.HORIZONTAL] = -1.0f;
            if (Input.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_RIGHT])) _axis[(int)eRTAxis.HORIZONTAL] = 1.0f;
            if (Input.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_UP])) _axis[(int)eRTAxis.VERTICAL] = -1.0f;
            if (Input.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_DOWN])) _axis[(int)eRTAxis.VERTICAL] = 1.0f;
        }


        //why don't I do all these in the same loop?  Because I think the order could matter in some cases!
        if (_buttonZeroGamepadKeycode != KeyCode.None)
        {
            for (int i = 0; i < C_BUTTON_COUNT; i++)
            {
                _buttons[i]._bDown = Input.GetKey((KeyCode)((int)_buttonZeroGamepadKeycode + i));
            }

            for (int i = 0; i < C_BUTTON_COUNT; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)_buttonZeroGamepadKeycode + i)))
                {
                    if (OnButtonEvent != null)
                        OnButtonEvent(_buttons[i], eRTButtonEvent.DOWN);
                }
            }

            for (int i = 0; i < C_BUTTON_COUNT; i++)
            {
                if (Input.GetKeyUp((KeyCode)((int)_buttonZeroGamepadKeycode + i)))
                {
                    if (OnButtonEvent != null)
                        OnButtonEvent(_buttons[i], eRTButtonEvent.UP);
                }
            }
        }

        if (_bUsingAltKeys)
        {
            //hacky for now
            if (Input.GetKeyDown(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_A]))
            {
                    if (OnButtonEvent != null)
                    {
                        OnButtonEvent(_buttons[0], eRTButtonEvent.DOWN);
                    }
             }

            if (Input.GetKeyUp(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_A]))
            {
                    if (OnButtonEvent != null)
                    {
                        OnButtonEvent(_buttons[0], eRTButtonEvent.UP);
                    }
            }
        }
    }
}

public class RTInputManager : MonoBehaviour
{

    public event Action<RTInputDevice> OnGamepadConnected;
    //public event Action<int> OnGamepadDisconnected;

    LinkedList<RTInputDevice> _devices;
    int _nextID = 0;
    public const int C_MAX_PLAYERS = 4;

    static RTInputManager _this;

    static public RTInputManager Get() { return _this; }

    void Awake ()
    {
        _this = this;
        _devices = new LinkedList<RTInputDevice>();

        for (int i=0; i < C_MAX_PLAYERS; i++)
        {
            RTInputDevice d = new RTInputDevice();
            d.Init(_nextID++);

       
                //some extra default keymapping so we can use keyboard keys in addition to the gamepad
                if (i == 0)
                {
                    d.SetAltKeys(KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
                        KeyCode.RightControl, KeyCode.Slash);
                }
                if (i == 1)
                {
                    d.SetAltKeys(KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S,
                        KeyCode.Space, KeyCode.N);
                }


            _devices.AddLast(d);
        }
    }
	
    public RTInputDevice GetDeviceByUnityID(int unityJoystickID)
    {
        var item = _devices.First;

        while(item != null)
        {
            if (item.Value.GetUnityGamepadID() == unityJoystickID)
            {
                return item.Value;
            }
            item = item.Next;
        }

        return null;
    }

    public RTInputDevice GetDeviceByUniqueID(int uniqueID)
    {
        var item = _devices.First;

        while (item != null)
        {
            if (item.Value.GetUniqueID() == uniqueID)
            {
                return item.Value;
            }
            item = item.Next;
        }

        return null;
    }

    // Update is called once per frame
    void Update ()
    {

        //Debug.Log("Found " + UnityEngine.Experimental.Input.Gamepad.all.Count  + " gamepads");

        for (int i=0; i < Input.GetJoystickNames().Length; i++)
        {
            RTInputDevice d = GetDeviceByUnityID(i);
            if (d == null)
            {
                //wow, it's new, assign this joystick to an existing player
                d = GetDeviceByUniqueID(i); //todo, this won't work with unplugging/plugging devices probably
                d.InitGamepad(i);

                if (OnGamepadConnected != null)
                    OnGamepadConnected(d);
            }
        }


        var device = _devices.First;
        while(device != null)
        {
            device.Value.Update();
            device = device.Next;
        }
    }
}
