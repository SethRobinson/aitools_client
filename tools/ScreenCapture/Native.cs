using System;
using System.Drawing;
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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position, ref Size size,
            IntPtr source, ref Point origin, uint key, ref Blend blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

        internal static void PresentLayer(Form form, Bitmap bitmap)
        {
            // Memory DC and our own bitmap only: no screen DC, screenshot, or background copy.
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("Could not allocate the selection surface.");
            IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(dc, image);
                Point position = form.Location, origin = Point.Empty;
                Size size = bitmap.Size;
                var blend = new Blend { Alpha = 255, Format = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
                if (!UpdateLayeredWindow(form.Handle, IntPtr.Zero, ref position, ref size, dc, ref origin, 0, ref blend, 2))
                    throw new InvalidOperationException("Could not display the selection overlay (error " + Marshal.GetLastWin32Error() + ").");
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(dc, previous);
                if (image != IntPtr.Zero) DeleteObject(image);
                DeleteDC(dc);
            }
        }

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
