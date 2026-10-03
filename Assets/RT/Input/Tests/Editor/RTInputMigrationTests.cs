using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// InputTestFixture substitutes an isolated runtime. These tests never send OS input.
public class RTInputMigrationTests : InputTestFixture
{
    GameObject _managerObject;

    public override void TearDown()
    {
        if (_managerObject != null) Object.DestroyImmediate(_managerObject);
        base.TearDown();
    }

    [Test]
    public void MissingDevicesAndUnknownBindingsAreNeutral()
    {
        Assert.That(RTInput.mousePresent, Is.False);
        Assert.That(RTInput.mousePosition, Is.EqualTo(Vector3.zero));
        Assert.That(RTInput.mouseDelta, Is.EqualTo(Vector2.zero));
        Assert.That(RTInput.mouseScrollDelta, Is.EqualTo(Vector2.zero));
        Assert.That(RTInput.GetKey(KeyCode.A), Is.False);
        Assert.That(RTInput.GetKeyDown(KeyCode.A), Is.False);
        Assert.That(RTInput.GetKeyUp(KeyCode.A), Is.False);
        Assert.That(RTInput.GetMouseButton(0), Is.False);
        Assert.That(RTInput.GetMouseButtonDown(0), Is.False);
        Assert.That(RTInput.GetMouseButtonUp(0), Is.False);
        InputSystem.AddDevice<Keyboard>();
        InputSystem.AddDevice<Mouse>();
        Assert.That(RTInput.GetKey(KeyCode.None), Is.False);
        Assert.That(RTInput.GetKey((KeyCode)(-1)), Is.False);
        Assert.That(RTInput.GetMouseButton(99), Is.False);
        Assert.That(RTInput.GetMouseAxis("unknown"), Is.Zero);
    }

    [TestCase(KeyCode.Alpha1, Key.Digit1)]
    [TestCase(KeyCode.Alpha0, Key.Digit0)]
    [TestCase(KeyCode.Return, Key.Enter)]
    [TestCase(KeyCode.KeypadEnter, Key.NumpadEnter)]
    [TestCase(KeyCode.KeypadPlus, Key.NumpadPlus)]
    [TestCase(KeyCode.KeypadMinus, Key.NumpadMinus)]
    [TestCase(KeyCode.LeftControl, Key.LeftCtrl)]
    [TestCase(KeyCode.RightControl, Key.RightCtrl)]
    [TestCase(KeyCode.LeftShift, Key.LeftShift)]
    [TestCase(KeyCode.RightShift, Key.RightShift)]
    [TestCase(KeyCode.LeftAlt, Key.LeftAlt)]
    [TestCase(KeyCode.RightAlt, Key.RightAlt)]
    [TestCase(KeyCode.LeftCommand, Key.LeftMeta)]
    [TestCase(KeyCode.RightWindows, Key.RightMeta)]
    [TestCase(KeyCode.BackQuote, Key.Backquote)]
    [TestCase(KeyCode.LeftBracket, Key.LeftBracket)]
    [TestCase(KeyCode.RightBracket, Key.RightBracket)]
    [TestCase(KeyCode.Backslash, Key.Backslash)]
    [TestCase(KeyCode.Minus, Key.Minus)]
    [TestCase(KeyCode.Equals, Key.Equals)]
    [TestCase(KeyCode.Delete, Key.Delete)]
    [TestCase(KeyCode.Escape, Key.Escape)]
    [TestCase(KeyCode.UpArrow, Key.UpArrow)]
    [TestCase(KeyCode.DownArrow, Key.DownArrow)]
    [TestCase(KeyCode.F10, Key.F10)]
    [TestCase(KeyCode.F15, Key.F15)]
    public void SerializedBindingsKeepHeldAndFrameEdgeSemantics(KeyCode binding, Key key)
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        Press(keyboard[key]);
        Assert.That(RTInput.GetKey(binding), Is.True);
        Assert.That(RTInput.GetKeyDown(binding), Is.True);
        Assert.That(RTInput.GetKeyDown(binding), Is.True, "Multiple Update/LateUpdate readers share the edge");
        Assert.That(RTInput.GetKeyUp(binding), Is.False);
        InputSystem.Update();
        Assert.That(RTInput.GetKey(binding), Is.True);
        Assert.That(RTInput.GetKeyDown(binding), Is.False);
        Release(keyboard[key]);
        Assert.That(RTInput.GetKey(binding), Is.False);
        Assert.That(RTInput.GetKeyUp(binding), Is.True);
        InputSystem.Update();
        Assert.That(RTInput.GetKeyUp(binding), Is.False);
    }

    [Test]
    public void AlphabetShortcutsAndBothSidesOfModifiersRemainIndependent()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        for (int i = 0; i < 26; i++)
        {
            var binding = (KeyCode)((int)KeyCode.A + i);
            var key = (Key)((int)Key.A + i);
            Press(keyboard[key]);
            Assert.That(RTInput.GetKeyDown(binding), Is.True, binding.ToString());
            Release(keyboard[key]);
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightCtrl, Key.LeftShift, Key.Z));
        InputSystem.Update();
        Assert.That(RTInput.GetKey(KeyCode.RightControl), Is.True);
        Assert.That(RTInput.GetKey(KeyCode.LeftControl), Is.False);
        Assert.That(RTInput.GetKey(KeyCode.LeftShift), Is.True);
        Assert.That(RTInput.GetKeyDown(KeyCode.Z), Is.True);
        Assert.That(RTInput.GetKey(KeyCode.RightShift), Is.False);
    }

    [TestCase(0, KeyCode.Mouse0, MouseButton.Left)]
    [TestCase(1, KeyCode.Mouse1, MouseButton.Right)]
    [TestCase(2, KeyCode.Mouse2, MouseButton.Middle)]
    [TestCase(3, KeyCode.Mouse3, MouseButton.Back)]
    [TestCase(4, KeyCode.Mouse4, MouseButton.Forward)]
    public void MouseBindingsReadCorrectButtons(int index, KeyCode binding, MouseButton button)
    {
        var mouse = InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(button));
        InputSystem.Update();
        Assert.That(RTInput.GetMouseButton(index), Is.True);
        Assert.That(RTInput.GetMouseButtonDown(index), Is.True);
        Assert.That(RTInput.GetKey(binding), Is.True);
        Assert.That(RTInput.GetKeyDown(binding), Is.True);
        InputSystem.Update();
        Assert.That(RTInput.GetMouseButtonDown(index), Is.False);
        InputSystem.QueueStateEvent(mouse, new MouseState());
        InputSystem.Update();
        Assert.That(RTInput.GetMouseButton(index), Is.False);
        Assert.That(RTInput.GetMouseButtonUp(index), Is.True);
        Assert.That(RTInput.GetKeyUp(binding), Is.True);
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    [TestCase(0.25f)]
    public void PointerAndFractionalScrollKeepTheirUnits(float wheel)
    {
        var mouse = InputSystem.AddDevice<Mouse>();
        Assert.That(InputSystem.settings.scrollDeltaBehavior,
            Is.EqualTo(InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms));
        InputSystem.QueueStateEvent(mouse, new MouseState
        {
            position = new Vector2(320, 180), delta = new Vector2(10, -5), scroll = new Vector2(0.5f, wheel)
        });
        InputSystem.Update();
        Assert.That(RTInput.mousePosition, Is.EqualTo(new Vector3(320, 180, 0)));
        Assert.That(RTInput.mouseDelta, Is.EqualTo(new Vector2(10, -5)));
        Assert.That(RTInput.mouseScrollDelta, Is.EqualTo(new Vector2(0.5f, wheel)));
        Assert.That(RTInput.GetMouseAxis("Mouse X"), Is.EqualTo(1f));
        Assert.That(RTInput.GetMouseAxis("Mouse Y"), Is.EqualTo(-0.5f));
        Assert.That(RTInput.GetMouseAxis("Mouse ScrollWheel"), Is.EqualTo(wheel * 0.1f));
        InputSystem.Update();
        Assert.That(RTInput.mouseScrollDelta, Is.EqualTo(Vector2.zero));
        InputSystem.RemoveDevice(mouse);
        Assert.That(RTInput.mousePresent, Is.False);
        Assert.That(RTInput.mousePosition, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void GamepadAndKeyboardShareButtonStateWithoutDuplicateEvents()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var pad = InputSystem.AddDevice<Gamepad>();
        var manager = CreateManager();
        Tick(manager);
        var player = manager.GetDeviceByUniqueID(0);
        var events = new List<eRTButtonEvent>();
        player.OnButtonEvent += (button, edge) =>
        {
            if (button._friendlyName == RTButtonState.eFriendlyName.BUTTON_A) events.Add(edge);
        };
        Press(pad.buttonSouth);
        Tick(manager);
        Press(keyboard.rightCtrlKey);
        Tick(manager);
        Release(pad.buttonSouth);
        Tick(manager);
        Assert.That(player.GetButton(RTButtonState.eFriendlyName.BUTTON_A), Is.True);
        Assert.That(events, Is.EqualTo(new[] { eRTButtonEvent.DOWN }));
        Release(keyboard.rightCtrlKey);
        Tick(manager);
        Assert.That(events, Is.EqualTo(new[] { eRTButtonEvent.DOWN, eRTButtonEvent.UP }));
        Assert.That(player.GetButton(RTButtonState.eFriendlyName.BUTTON_UNKNOWN), Is.False);
        Press(keyboard.slashKey);
        Tick(manager);
        Assert.That(player.GetButton(RTButtonState.eFriendlyName.BUTTON_B), Is.True);
        Set(pad.leftStick, Vector2.up);
        Tick(manager);
        Assert.That(player.GetAxis(eRTAxis.VERTICAL), Is.EqualTo(-1f));
        Press(keyboard.downArrowKey);
        Tick(manager);
        Assert.That(player.GetAxis(eRTAxis.VERTICAL), Is.EqualTo(1f));
    }

    [Test]
    public void GamepadTapWithinOneUpdateEmitsDownThenUp()
    {
        var pad = InputSystem.AddDevice<Gamepad>();
        var manager = CreateManager();
        Tick(manager);
        var player = manager.GetDeviceByUniqueID(0);
        var events = new List<eRTButtonEvent>();
        player.OnButtonEvent += (button, edge) =>
        {
            if (button._friendlyName == RTButtonState.eFriendlyName.BUTTON_A) events.Add(edge);
        };
        Press(pad.buttonSouth, queueEventOnly: true);
        Release(pad.buttonSouth, queueEventOnly: true);
        InputSystem.Update();
        Tick(manager);
        Assert.That(player.GetButton(RTButtonState.eFriendlyName.BUTTON_A), Is.False);
        Assert.That(events, Is.EqualTo(new[] { eRTButtonEvent.DOWN, eRTButtonEvent.UP }));
        InputSystem.Update();
        Tick(manager);
        Assert.That(events.Count, Is.EqualTo(2));
    }

    [Test]
    public void DisconnectClearsHeldButtonsAndReconnectKeepsLogicalPlayers()
    {
        var first = InputSystem.AddDevice<Gamepad>();
        var second = InputSystem.AddDevice<Gamepad>();
        var manager = CreateManager();
        int connections = 0;
        manager.OnGamepadConnected += _ => connections++;
        Tick(manager);
        var player0 = manager.GetDeviceByUniqueID(0);
        var player1 = manager.GetDeviceByUniqueID(1);
        Press(first.buttonSouth);
        Tick(manager);
        int releases = 0;
        player0.OnButtonEvent += (_, edge) => { if (edge == eRTButtonEvent.UP) releases++; };
        InputSystem.RemoveDevice(first);
        Tick(manager);
        Assert.That(player0.GetButton(RTButtonState.eFriendlyName.BUTTON_A), Is.False);
        Assert.That(player0.GetUnityGamepadID(), Is.EqualTo(-1));
        Assert.That(manager.GetDeviceByUnityID(0), Is.SameAs(player1));
        Assert.That(releases, Is.EqualTo(1));
        InputSystem.AddDevice(first);
        Tick(manager);
        Assert.That(manager.GetDeviceByUnityID(1), Is.SameAs(player0));
        Assert.That(manager.GetDeviceByUnityID(0), Is.SameAs(player1));
        Assert.That(connections, Is.EqualTo(3));
        Tick(manager);
        Assert.That(connections, Is.EqualTo(3));
    }

    [Test]
    public void ExtraPadsAreIgnoredAndDisabledPadReleasesState()
    {
        var pads = new Gamepad[5];
        for (int i = 0; i < pads.Length; i++) pads[i] = InputSystem.AddDevice<Gamepad>();
        var manager = CreateManager();
        int connections = 0;
        manager.OnGamepadConnected += _ => connections++;
        Tick(manager);
        Assert.That(connections, Is.EqualTo(4));
        Assert.That(manager.GetDeviceByUniqueID(4), Is.Null);
        Assert.That(manager.GetDeviceByUnityID(4), Is.Null);
        Press(pads[0].buttonSouth);
        Tick(manager);
        InputSystem.DisableDevice(pads[0]);
        Tick(manager);
        Assert.That(manager.GetDeviceByUniqueID(0).GetButton(RTButtonState.eFriendlyName.BUTTON_A), Is.False);
        Assert.That(manager.GetDeviceByUnityID(4), Is.SameAs(manager.GetDeviceByUniqueID(0)));
    }

    RTInputManager CreateManager()
    {
        _managerObject = new GameObject("Input migration test");
        var manager = _managerObject.AddComponent<RTInputManager>();
        // Edit-mode MonoBehaviours do not run Awake automatically.
        if (manager.GetDeviceByUniqueID(0) == null)
            typeof(RTInputManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
        return manager;
    }

    static void Tick(RTInputManager manager)
    {
        typeof(RTInputManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
    }
}
