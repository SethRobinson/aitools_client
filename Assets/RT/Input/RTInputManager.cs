using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
        COUNT
    }

    public bool _bDown = false;
    public eFriendlyName _friendlyName = eFriendlyName.BUTTON_UNKNOWN;
    public int _rawButtonIndex;
}

public enum eRTButtonEvent { DOWN, UP }
public enum eRTAxis { HORIZONTAL, VERTICAL }

public class RTInputDevice
{
    public const int C_BUTTON_COUNT = 16;
    public const int C_AXIS_COUNT = 2;
    public int _uniqueID;
    public int _unityGamepadID = -1;
    public int GetUniqueID() => _uniqueID;
    public int GetUnityGamepadID() => _unityGamepadID;

    readonly float[] _axis = new float[C_AXIS_COUNT];
    readonly RTButtonState[] _buttons = new RTButtonState[C_BUTTON_COUNT];
    readonly bool[] _pressed = new bool[C_BUTTON_COUNT];
    readonly bool[] _released = new bool[C_BUTTON_COUNT];
    readonly KeyCode[] _altKeys = new KeyCode[(int)RTButtonState.eFriendlyName.COUNT];
    bool _bUsingAltKeys;
    bool _bGamepadAnalog = false;

    // Keep the device identity, not an index that changes when another pad disconnects.
    internal Gamepad Gamepad { get; private set; }
    internal int LastDeviceId { get; private set; } = -1;
    public event Action<RTButtonState, eRTButtonEvent> OnButtonEvent;
    public float GetAxis(eRTAxis axis) => _axis[(int)axis];

    public RTButtonState GetButtonState(RTButtonState.eFriendlyName friendlyName)
    {
        if (friendlyName == RTButtonState.eFriendlyName.BUTTON_UNKNOWN) return null;
        foreach (var button in _buttons)
            if (button._friendlyName == friendlyName) return button;
        return null;
    }

    public bool GetButton(RTButtonState.eFriendlyName friendlyName)
    {
        if (GetButtonState(friendlyName)?._bDown == true) return true;
        return _bUsingAltKeys && friendlyName > RTButtonState.eFriendlyName.BUTTON_UNKNOWN
            && friendlyName < RTButtonState.eFriendlyName.COUNT
            && RTInput.GetKey(_altKeys[(int)friendlyName]);
    }

    // The public Unity index API stays zero-based; manager routing uses the device itself.
    public void InitGamepad(int unityGamepadID)
    {
        BindGamepad(unityGamepadID >= 0 && unityGamepadID < UnityEngine.InputSystem.Gamepad.all.Count
            ? UnityEngine.InputSystem.Gamepad.all[unityGamepadID] : null, unityGamepadID);
    }

    internal void BindGamepad(Gamepad gamepad, int index)
    {
        Gamepad = gamepad;
        _unityGamepadID = gamepad != null ? index : -1;
        if (gamepad != null) LastDeviceId = gamepad.deviceId;
    }

    public void Init(int uniqueID)
    {
        _uniqueID = uniqueID;
        for (int i = 0; i < C_BUTTON_COUNT; i++)
            _buttons[i] = new RTButtonState { _rawButtonIndex = i };
        _buttons[0]._friendlyName = RTButtonState.eFriendlyName.BUTTON_A;
        _buttons[1]._friendlyName = RTButtonState.eFriendlyName.BUTTON_B;
        _buttons[2]._friendlyName = RTButtonState.eFriendlyName.BUTTON_X;
        _buttons[3]._friendlyName = RTButtonState.eFriendlyName.BUTTON_Y;
        _buttons[6]._friendlyName = RTButtonState.eFriendlyName.BUTTON_SELECT;
        _buttons[7]._friendlyName = RTButtonState.eFriendlyName.BUTTON_START;
        _buttons[10]._friendlyName = RTButtonState.eFriendlyName.BUTTON_LEFT;
        _buttons[11]._friendlyName = RTButtonState.eFriendlyName.BUTTON_RIGHT;
        _buttons[12]._friendlyName = RTButtonState.eFriendlyName.BUTTON_UP;
        _buttons[13]._friendlyName = RTButtonState.eFriendlyName.BUTTON_DOWN;
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

    ButtonControl GetGamepadButton(int index)
    {
        if (Gamepad == null || !Gamepad.added || !Gamepad.enabled) return null;
        switch (index)
        {
            case 0: return Gamepad.buttonSouth;
            case 1: return Gamepad.buttonEast;
            case 2: return Gamepad.buttonWest;
            case 3: return Gamepad.buttonNorth;
            case 4: return Gamepad.leftShoulder;
            case 5: return Gamepad.rightShoulder;
            case 6: return Gamepad.selectButton;
            case 7: return Gamepad.startButton;
            case 8: return Gamepad.leftStickButton;
            case 9: return Gamepad.rightStickButton;
            case 10: return Gamepad.dpad.left;
            case 11: return Gamepad.dpad.right;
            case 12: return Gamepad.dpad.up;
            case 13: return Gamepad.dpad.down;
            case 14: return Gamepad.leftTrigger;
            case 15: return Gamepad.rightTrigger;
            default: return null;
        }
    }

    public void Update()
    {
        Vector2 movement = Gamepad != null && Gamepad.added && Gamepad.enabled
            ? Gamepad.leftStick.ReadValue() : Vector2.zero;
        if (!_bGamepadAnalog && movement.magnitude > 0.2f) movement.Normalize();
        _axis[(int)eRTAxis.HORIZONTAL] = movement.x;
        // The existing keyboard API uses negative Y for up.
        _axis[(int)eRTAxis.VERTICAL] = -movement.y;
        if (_bUsingAltKeys)
        {
            if (RTInput.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_LEFT])) _axis[0] = -1f;
            if (RTInput.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_RIGHT])) _axis[0] = 1f;
            if (RTInput.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_UP])) _axis[1] = -1f;
            if (RTInput.GetKey(_altKeys[(int)RTButtonState.eFriendlyName.BUTTON_DOWN])) _axis[1] = 1f;
        }

        // Update all states before dispatching, then downs before ups, as before.
        // Combining keyboard and pad state prevents duplicate or premature releases.
        for (int i = 0; i < C_BUTTON_COUNT; i++)
        {
            var button = _buttons[i];
            var control = GetGamepadButton(i);
            bool down = control?.isPressed ?? false;
            bool pressed = control?.wasPressedThisFrame ?? false;
            bool released = control?.wasReleasedThisFrame ?? false;
            if (_bUsingAltKeys && button._friendlyName != RTButtonState.eFriendlyName.BUTTON_UNKNOWN)
            {
                var key = _altKeys[(int)button._friendlyName];
                down |= RTInput.GetKey(key);
                pressed |= RTInput.GetKeyDown(key);
                released |= RTInput.GetKeyUp(key);
            }
            // A complete tap can begin and end during one input update.
            _pressed[i] = !button._bDown && (down || pressed);
            _released[i] = !down && (button._bDown || released);
            button._bDown = down;
        }
        for (int i = 0; i < C_BUTTON_COUNT; i++)
            if (_pressed[i]) OnButtonEvent?.Invoke(_buttons[i], eRTButtonEvent.DOWN);
        for (int i = 0; i < C_BUTTON_COUNT; i++)
            if (_released[i]) OnButtonEvent?.Invoke(_buttons[i], eRTButtonEvent.UP);
    }
}

/// <summary>Four logical players with stable gamepad assignments and keyboard alternatives.</summary>
public class RTInputManager : MonoBehaviour
{
    public const int C_MAX_PLAYERS = 4;
    public event Action<RTInputDevice> OnGamepadConnected;
    readonly RTInputDevice[] _devices = new RTInputDevice[C_MAX_PLAYERS];
    static RTInputManager _this;
    public static RTInputManager Get() => _this;

    void Awake()
    {
        _this = this;
        for (int i = 0; i < C_MAX_PLAYERS; i++)
        {
            var device = new RTInputDevice();
            device.Init(i);
            if (i == 0)
                device.SetAltKeys(KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
                    KeyCode.RightControl, KeyCode.Slash);
            if (i == 1)
                device.SetAltKeys(KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S, KeyCode.Space, KeyCode.N);
            _devices[i] = device;
        }
    }

    public RTInputDevice GetDeviceByUnityID(int unityJoystickID)
    {
        if (unityJoystickID < 0) return null;
        foreach (var device in _devices)
            if (device.GetUnityGamepadID() == unityJoystickID) return device;
        return null;
    }

    public RTInputDevice GetDeviceByUniqueID(int uniqueID)
    {
        return uniqueID >= 0 && uniqueID < C_MAX_PLAYERS ? _devices[uniqueID] : null;
    }

    void Update()
    {
        foreach (var device in _devices)
            if (device.Gamepad != null && (!device.Gamepad.added || !device.Gamepad.enabled))
                device.BindGamepad(null, -1);

        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            var gamepad = Gamepad.all[i];
            if (!gamepad.enabled) continue;
            RTInputDevice assigned = Array.Find(_devices, d => d.Gamepad == gamepad);
            if (assigned != null)
            {
                assigned._unityGamepadID = i;
                continue;
            }
            assigned = Array.Find(_devices, d => d.Gamepad == null && d.LastDeviceId == gamepad.deviceId)
                ?? Array.Find(_devices, d => d.Gamepad == null);
            if (assigned == null) continue; // More than four pads do not create extra players.
            assigned.BindGamepad(gamepad, i);
            OnGamepadConnected?.Invoke(assigned);
        }
        foreach (var device in _devices) device.Update();
    }

    void OnDestroy()
    {
        if (_this == this) _this = null;
    }
}
