using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Frame-based input reads backed only by the Input System package.
/// KeyCode is retained for existing serialized Inspector bindings, not legacy input reads.
/// </summary>
public static class RTInput
{
    public static bool mousePresent => Mouse.current != null;
    public static Vector3 mousePosition => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
    public static Vector2 mouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

    // Input System 1.20 defaults to UniformAcrossAllPlatforms: one tick is 1,
    // including on Windows. Keep fractional ticks for smooth scrolling.
    public static Vector2 mouseScrollDelta => Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;

    public static bool GetKey(KeyCode key) => GetControl(key)?.isPressed ?? false;
    public static bool GetKeyDown(KeyCode key) => GetControl(key)?.wasPressedThisFrame ?? false;
    public static bool GetKeyUp(KeyCode key) => GetControl(key)?.wasReleasedThisFrame ?? false;
    public static bool GetMouseButton(int button) => GetMouseControl(button)?.isPressed ?? false;
    public static bool GetMouseButtonDown(int button) => GetMouseControl(button)?.wasPressedThisFrame ?? false;
    public static bool GetMouseButtonUp(int button) => GetMouseControl(button)?.wasReleasedThisFrame ?? false;

    // Preserve SceneLikeCamera's serialized axis names and the 0.1 sensitivity
    // of these three axes in the old InputManager asset. No named-axis backend is used.
    public static float GetMouseAxis(string axis)
    {
        switch (axis)
        {
            case "Mouse X": return mouseDelta.x * 0.1f;
            case "Mouse Y": return mouseDelta.y * 0.1f;
            case "Mouse ScrollWheel": return mouseScrollDelta.y * 0.1f;
            default: return 0f;
        }
    }

    private static ButtonControl GetMouseControl(int button)
    {
        var mouse = Mouse.current;
        if (mouse == null) return null;
        switch (button)
        {
            case 0: return mouse.leftButton;
            case 1: return mouse.rightButton;
            case 2: return mouse.middleButton;
            case 3: return mouse.backButton;
            case 4: return mouse.forwardButton;
            default: return null;
        }
    }

    private static ButtonControl GetControl(KeyCode key)
    {
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
            return GetMouseControl(key - KeyCode.Mouse0);

        var keyboard = Keyboard.current;
        if (keyboard == null) return null;
        Key mapped = ToKey(key);
        return mapped != Key.None ? keyboard[mapped] : null;
    }

    private static Key ToKey(KeyCode key)
    {
        // These enums have different numeric values, so never cast between them.
        switch (key)
        {
            case KeyCode.A: return Key.A;
            case KeyCode.B: return Key.B;
            case KeyCode.C: return Key.C;
            case KeyCode.D: return Key.D;
            case KeyCode.E: return Key.E;
            case KeyCode.F: return Key.F;
            case KeyCode.G: return Key.G;
            case KeyCode.H: return Key.H;
            case KeyCode.I: return Key.I;
            case KeyCode.J: return Key.J;
            case KeyCode.K: return Key.K;
            case KeyCode.L: return Key.L;
            case KeyCode.M: return Key.M;
            case KeyCode.N: return Key.N;
            case KeyCode.O: return Key.O;
            case KeyCode.P: return Key.P;
            case KeyCode.Q: return Key.Q;
            case KeyCode.R: return Key.R;
            case KeyCode.S: return Key.S;
            case KeyCode.T: return Key.T;
            case KeyCode.U: return Key.U;
            case KeyCode.V: return Key.V;
            case KeyCode.W: return Key.W;
            case KeyCode.X: return Key.X;
            case KeyCode.Y: return Key.Y;
            case KeyCode.Z: return Key.Z;
            case KeyCode.Alpha0: return Key.Digit0;
            case KeyCode.Alpha1: return Key.Digit1;
            case KeyCode.Alpha2: return Key.Digit2;
            case KeyCode.Alpha3: return Key.Digit3;
            case KeyCode.Alpha4: return Key.Digit4;
            case KeyCode.Alpha5: return Key.Digit5;
            case KeyCode.Alpha6: return Key.Digit6;
            case KeyCode.Alpha7: return Key.Digit7;
            case KeyCode.Alpha8: return Key.Digit8;
            case KeyCode.Alpha9: return Key.Digit9;
            case KeyCode.Keypad0: return Key.Numpad0;
            case KeyCode.Keypad1: return Key.Numpad1;
            case KeyCode.Keypad2: return Key.Numpad2;
            case KeyCode.Keypad3: return Key.Numpad3;
            case KeyCode.Keypad4: return Key.Numpad4;
            case KeyCode.Keypad5: return Key.Numpad5;
            case KeyCode.Keypad6: return Key.Numpad6;
            case KeyCode.Keypad7: return Key.Numpad7;
            case KeyCode.Keypad8: return Key.Numpad8;
            case KeyCode.Keypad9: return Key.Numpad9;
            case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
            case KeyCode.KeypadDivide: return Key.NumpadDivide;
            case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
            case KeyCode.KeypadMinus: return Key.NumpadMinus;
            case KeyCode.KeypadPlus: return Key.NumpadPlus;
            case KeyCode.KeypadEnter: return Key.NumpadEnter;
            case KeyCode.KeypadEquals: return Key.NumpadEquals;
            case KeyCode.F1: return Key.F1;
            case KeyCode.F2: return Key.F2;
            case KeyCode.F3: return Key.F3;
            case KeyCode.F4: return Key.F4;
            case KeyCode.F5: return Key.F5;
            case KeyCode.F6: return Key.F6;
            case KeyCode.F7: return Key.F7;
            case KeyCode.F8: return Key.F8;
            case KeyCode.F9: return Key.F9;
            case KeyCode.F10: return Key.F10;
            case KeyCode.F11: return Key.F11;
            case KeyCode.F12: return Key.F12;
            case KeyCode.F13: return Key.F13;
            case KeyCode.F14: return Key.F14;
            case KeyCode.F15: return Key.F15;
            case KeyCode.Return: return Key.Enter;
            case KeyCode.Escape: return Key.Escape;
            case KeyCode.Space: return Key.Space;
            case KeyCode.Tab: return Key.Tab;
            case KeyCode.Backspace: return Key.Backspace;
            case KeyCode.Delete: return Key.Delete;
            case KeyCode.Insert: return Key.Insert;
            case KeyCode.Home: return Key.Home;
            case KeyCode.End: return Key.End;
            case KeyCode.PageUp: return Key.PageUp;
            case KeyCode.PageDown: return Key.PageDown;
            case KeyCode.UpArrow: return Key.UpArrow;
            case KeyCode.DownArrow: return Key.DownArrow;
            case KeyCode.LeftArrow: return Key.LeftArrow;
            case KeyCode.RightArrow: return Key.RightArrow;
            case KeyCode.LeftShift: return Key.LeftShift;
            case KeyCode.RightShift: return Key.RightShift;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftAlt: return Key.LeftAlt;
            case KeyCode.RightAlt: return Key.RightAlt;
            case KeyCode.LeftCommand:
            case KeyCode.LeftWindows: return Key.LeftMeta;
            case KeyCode.RightCommand:
            case KeyCode.RightWindows: return Key.RightMeta;
            case KeyCode.CapsLock: return Key.CapsLock;
            case KeyCode.Numlock: return Key.NumLock;
            case KeyCode.ScrollLock: return Key.ScrollLock;
            case KeyCode.Print: return Key.PrintScreen;
            case KeyCode.Pause: return Key.Pause;
            case KeyCode.Menu: return Key.ContextMenu;
            case KeyCode.BackQuote: return Key.Backquote;
            case KeyCode.Minus: return Key.Minus;
            case KeyCode.Equals: return Key.Equals;
            case KeyCode.LeftBracket: return Key.LeftBracket;
            case KeyCode.RightBracket: return Key.RightBracket;
            case KeyCode.Backslash: return Key.Backslash;
            case KeyCode.Semicolon: return Key.Semicolon;
            case KeyCode.Quote: return Key.Quote;
            case KeyCode.Comma: return Key.Comma;
            case KeyCode.Period: return Key.Period;
            case KeyCode.Slash: return Key.Slash;
            default: return Key.None;
        }
    }
}
