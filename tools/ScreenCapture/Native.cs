using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AITools.ScreenCapture.Helper
{
    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

        internal static void Exclude(Form form)
        {
            if (!SetWindowDisplayAffinity(form.Handle, 0x11))
                throw new InvalidOperationException("Windows could not exclude recording controls from capture (error " + Marshal.GetLastWin32Error() + ").");
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        internal event Action Pressed;
        internal event Action CancelPressed;
        private bool _cancelEnabled;
        internal HotkeyWindow() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }
        internal string Register(CaptureSettings settings)
        {
            Native.UnregisterHotKey(Handle, 1);
            if (!settings.hotkeyEnabled) return null;
            uint modifiers, key;
            string error;
            if (!Hotkey.TryParse(settings.hotkey, out modifiers, out key, out error)) return error;
            if (!Native.RegisterHotKey(Handle, 1, modifiers | 0x4000, key))
                return "Could not register " + settings.hotkey + ". It may be in use by another app. Record screen still works; change the shortcut in General Settings.";
            return null;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                if (message.WParam.ToInt32() == 2) CancelPressed?.Invoke();
                else Pressed?.Invoke();
            }
            base.WndProc(ref message);
        }
        internal void EnableCancel(bool enabled)
        {
            if (enabled == _cancelEnabled || Handle == IntPtr.Zero) return;
            Native.UnregisterHotKey(Handle, 2);
            _cancelEnabled = enabled && Native.RegisterHotKey(Handle, 2, 0x4000, 0x1B);
        }
        public void Dispose() { if (Handle != IntPtr.Zero) { Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2); DestroyHandle(); } }
    }
}
