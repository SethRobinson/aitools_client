using System;
using System.Collections.Generic;
using System.IO;

// Shared with the Windows helper and its offline checks. No Unity or WinForms dependencies.
namespace AITools.ScreenCapture
{
    [Serializable]
    public sealed class CaptureSettings
    {
        public int fps = 30;
        public bool audio = true;
        public bool cursor = true;
        public bool hotkeyEnabled = true;
        public string hotkey = "Ctrl+Alt+F9";

        public static readonly int[] FrameRates = { 16, 24, 30, 60, 120 };

        public bool IsValid(out string error)
        {
            if (Array.IndexOf(FrameRates, fps) < 0) { error = "Choose 16, 24, 30, 60, or 120 fps."; return false; }
            uint modifiers, key;
            return Hotkey.TryParse(hotkey, out modifiers, out key, out error);
        }
    }

    [Serializable]
    public sealed class CaptureCommand
    {
        public string command;
        public CaptureSettings settings;
    }

    [Serializable]
    public sealed class CaptureEvent
    {
        public string type;
        public string state;
        public string message;
        public string path;
        public string sessionId;
        public CaptureSettings settings;
    }

    public static class Hotkey
    {
        public static bool TryParse(string text, out uint modifiers, out uint key, out string error)
        {
            modifiers = key = 0;
            error = "Use Ctrl and/or Alt, optional Shift, and a letter, number, or F1-F24 (for example Ctrl+Alt+F9).";
            if (string.IsNullOrWhiteSpace(text)) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in text.Split('+'))
            {
                string token = part.Trim().ToUpperInvariant();
                if (!seen.Add(token)) return false;
                if (token == "CTRL") modifiers |= 2;
                else if (token == "ALT") modifiers |= 1;
                else if (token == "SHIFT") modifiers |= 4;
                else
                {
                    if (key != 0) return false;
                    int function;
                    if (token.StartsWith("F", StringComparison.Ordinal) && int.TryParse(token.Substring(1), out function) && function >= 1 && function <= 24)
                        key = (uint)(0x70 + function - 1);
                    else if (token.Length == 1 && ((token[0] >= 'A' && token[0] <= 'Z') || (token[0] >= '0' && token[0] <= '9')))
                        key = token[0];
                    else return false;
                }
            }
            if (key == 0 || (modifiers & 3) == 0) return false;
            error = null;
            return true;
        }
    }

    public struct PixelRect
    {
        public int X, Y, Width, Height;
        public PixelRect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    }

    public static class CaptureGeometry
    {
        // All inputs are physical desktop pixels, including on mixed-DPI desktops.
        // Keep both corners inside the starting monitor and use even H.264 dimensions.
        public static PixelRect Select(PixelRect monitor, int startX, int startY, int endX, int endY)
        {
            int left = Math.Max(monitor.X, Math.Min(startX, endX));
            int top = Math.Max(monitor.Y, Math.Min(startY, endY));
            int right = Math.Min(monitor.X + monitor.Width, Math.Max(startX, endX));
            int bottom = Math.Min(monitor.Y + monitor.Height, Math.Max(startY, endY));
            int width = Math.Max(0, right - left) / 2 * 2;
            int height = Math.Max(0, bottom - top) / 2 * 2;
            return new PixelRect(left, top, width, height);
        }

        public static PixelRect RelativeToMonitor(PixelRect selection, PixelRect monitor)
        {
            return new PixelRect(selection.X - monitor.X, selection.Y - monitor.Y, selection.Width, selection.Height);
        }
    }

    public enum CaptureStage { Idle, Selecting, Ready, Countdown, Starting, Recording, Finishing }

    public sealed class CaptureSession
    {
        public CaptureStage Stage { get; private set; }
        public string Id { get; private set; }
        public bool Active => Stage == CaptureStage.Countdown || Stage == CaptureStage.Starting || Stage == CaptureStage.Recording || Stage == CaptureStage.Finishing;

        public bool Select() { if (Active) return false; Stage = CaptureStage.Selecting; return true; }
        public void Ready(bool hasSelection) { if (!Active) Stage = hasSelection ? CaptureStage.Ready : CaptureStage.Idle; }
        public bool Countdown()
        {
            if (Stage != CaptureStage.Ready) return false;
            Id = Guid.NewGuid().ToString("N");
            Stage = CaptureStage.Countdown;
            return true;
        }
        public bool Start() { if (Stage != CaptureStage.Countdown) return false; Stage = CaptureStage.Starting; return true; }
        public void Recording(string id) { if (id == Id && Stage == CaptureStage.Starting) Stage = CaptureStage.Recording; }
        public bool Stop()
        {
            if (Stage != CaptureStage.Starting && Stage != CaptureStage.Recording) return false;
            Stage = CaptureStage.Finishing;
            return true;
        }
        public void CancelCountdown() { if (Stage == CaptureStage.Countdown) { Stage = CaptureStage.Ready; Id = null; } }
        // Completion is accepted once, and stale native callbacks cannot complete a newer session.
        public bool Complete(string id)
        {
            if (id == null || id != Id || !Active) return false;
            Stage = CaptureStage.Ready;
            Id = null;
            return true;
        }
    }

    public static class CapturePaths
    {
        public static string CacheDirectory(string appRoot)
        {
            if (string.IsNullOrWhiteSpace(appRoot) || !Path.IsPathRooted(appRoot)) throw new IOException("Missing absolute application root.");
            string result = Path.GetFullPath(Path.Combine(appRoot, "tempCache", "screen_recordings"));
            AssertNoLinks(result);
            return result;
        }

        public static bool IsRecording(string directory, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            string full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return false;
            string stem = Path.GetFileNameWithoutExtension(full);
            Guid id;
            if (!stem.StartsWith("capture_", StringComparison.Ordinal) || !Guid.TryParseExact(stem.Substring(8), "N", out id) || !string.Equals(Path.GetExtension(full), ".mp4", StringComparison.OrdinalIgnoreCase)) return false;
            AssertNoLinks(full);
            return true;
        }

        public static void AssertNoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Refusing linked capture path: " + current);
            }
        }
    }
}
