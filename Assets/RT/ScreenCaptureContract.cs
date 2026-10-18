using System;
using System.Collections.Generic;
using System.Globalization;
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
        public double countdownSeconds = 3;
        public bool fixedSize;
        public int fixedWidth = 1280;
        public int fixedHeight = 720;
        public string aspectRatio = "Any";

        public static readonly int[] FrameRates = { 16, 24, 30, 60, 120 };

        public static bool TryParseFrameRate(string text, out int value) =>
            int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

        public bool IsValid(out string error)
        {
            if (fps <= 0) { error = "FPS must be a positive whole number."; return false; }
            if (double.IsNaN(countdownSeconds) || double.IsInfinity(countdownSeconds) || countdownSeconds < 0 || countdownSeconds > 3600)
            { error = "Countdown must be between 0 and 3600 seconds."; return false; }
            if (fixedWidth < 2 || fixedHeight < 2 || fixedWidth > 16384 || fixedHeight > 16384 || (fixedWidth & 1) != 0 || (fixedHeight & 1) != 0)
            { error = "Fixed dimensions must be even pixel counts between 2 and 16384."; return false; }
            if (!CaptureAspectRatio.TryGet(aspectRatio, out _)) { error = "Choose an aspect ratio from the list."; return false; }
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

    public struct CaptureAspectRatio
    {
        public readonly string Name;
        public readonly int Width, Height;
        public bool IsLocked => Width > 0 && Height > 0;
        private CaptureAspectRatio(string name, int width, int height) { Name = name; Width = width; Height = height; }
        public static readonly CaptureAspectRatio[] Options =
        {
            new CaptureAspectRatio("Any", 0, 0),
            new CaptureAspectRatio("1:1", 1, 1),
            new CaptureAspectRatio("16:9", 16, 9), new CaptureAspectRatio("9:16", 9, 16),
            new CaptureAspectRatio("4:3", 4, 3), new CaptureAspectRatio("3:4", 3, 4),
            new CaptureAspectRatio("3:2", 3, 2), new CaptureAspectRatio("2:3", 2, 3),
            new CaptureAspectRatio("4:5", 4, 5), new CaptureAspectRatio("5:4", 5, 4),
            new CaptureAspectRatio("16:10", 16, 10), new CaptureAspectRatio("10:16", 10, 16),
            new CaptureAspectRatio("21:9", 21, 9), new CaptureAspectRatio("9:21", 9, 21),
            new CaptureAspectRatio("32:9", 32, 9),
            new CaptureAspectRatio("1.85:1", 185, 100),
            new CaptureAspectRatio("2.35:1", 235, 100), new CaptureAspectRatio("2.39:1", 239, 100)
        };
        public static bool TryGet(string name, out CaptureAspectRatio ratio)
        {
            foreach (var option in Options)
                if (option.Name == name) { ratio = option; return true; }
            ratio = default(CaptureAspectRatio); return false;
        }
    }

    public static class CaptureGeometry
    {
        public static bool Fits(PixelRect monitor, int width, int height) => width >= 2 && height >= 2 && width <= monitor.Width && height <= monitor.Height;

        public static PixelRect Move(PixelRect monitor, PixelRect rect, int x, int y)
        {
            if (!Fits(monitor, rect.Width, rect.Height)) return default(PixelRect);
            return new PixelRect(Math.Max(monitor.X, Math.Min(x, monitor.X + monitor.Width - rect.Width)),
                Math.Max(monitor.Y, Math.Min(y, monitor.Y + monitor.Height - rect.Height)), rect.Width, rect.Height);
        }

        // Corner handles keep the opposite corner anchored; sides move the whole region.
        public static PixelRect Resize(PixelRect monitor, PixelRect rect, bool left, bool top, int x, int y, string aspectRatio = "Any")
        {
            int anchorX = left ? rect.X + rect.Width : rect.X;
            int anchorY = top ? rect.Y + rect.Height : rect.Y;
            if (CaptureAspectRatio.TryGet(aspectRatio, out var ratio) && ratio.IsLocked)
                return AnchoredAspect(monitor, anchorX, anchorY, left, top,
                    Math.Max(0, left ? anchorX - x : x - anchorX), Math.Max(0, top ? anchorY - y : y - anchorY), ratio);
            x = left ? Math.Max(monitor.X, Math.Min(x, anchorX - 2)) : Math.Min(monitor.X + monitor.Width, Math.Max(x, anchorX + 2));
            y = top ? Math.Max(monitor.Y, Math.Min(y, anchorY - 2)) : Math.Min(monitor.Y + monitor.Height, Math.Max(y, anchorY + 2));
            int width = Math.Abs(x - anchorX) / 2 * 2, height = Math.Abs(y - anchorY) / 2 * 2;
            return new PixelRect(left ? anchorX - width : anchorX, top ? anchorY - height : anchorY, width, height);
        }

        public static string Describe(PixelRect rect) => rect.Width + " x " + rect.Height + " px   X: " + rect.X + "  Y: " + rect.Y;

        // All inputs are physical desktop pixels, including on mixed-DPI desktops.
        // Keep both corners inside the starting monitor and use even H.264 dimensions.
        public static PixelRect Select(PixelRect monitor, int startX, int startY, int endX, int endY, string aspectRatio = "Any")
        {
            if (CaptureAspectRatio.TryGet(aspectRatio, out var ratio) && ratio.IsLocked)
            {
                if (startX == endX && startY == endY) return default(PixelRect);
                return AnchoredAspect(monitor, startX, startY, endX < startX, endY < startY,
                    Math.Abs(endX - startX), Math.Abs(endY - startY), ratio);
            }
            int left = Math.Max(monitor.X, Math.Min(startX, endX));
            int top = Math.Max(monitor.Y, Math.Min(startY, endY));
            int right = Math.Min(monitor.X + monitor.Width, Math.Max(startX, endX));
            int bottom = Math.Min(monitor.Y + monitor.Height, Math.Max(startY, endY));
            int width = Math.Max(0, right - left) / 2 * 2;
            int height = Math.Max(0, bottom - top) / 2 * 2;
            return new PixelRect(left, top, width, height);
        }

        // Apply a new lock to an existing selection. Moving back inside the monitor
        // is allowed here; dragging a corner instead preserves its opposite anchor.
        public static PixelRect ApplyAspect(PixelRect monitor, PixelRect rect, string aspectRatio)
        {
            if (!CaptureAspectRatio.TryGet(aspectRatio, out var ratio) || !ratio.IsLocked) return rect;
            var size = AspectSize(monitor.Width, monitor.Height, rect.Width, rect.Height, ratio);
            return Move(monitor, size, rect.X, rect.Y);
        }

        private static PixelRect AnchoredAspect(PixelRect monitor, int anchorX, int anchorY, bool left, bool top, int width, int height, CaptureAspectRatio ratio)
        {
            int maxWidth = left ? anchorX - monitor.X : monitor.X + monitor.Width - anchorX;
            int maxHeight = top ? anchorY - monitor.Y : monitor.Y + monitor.Height - anchorY;
            var size = AspectSize(maxWidth, maxHeight, width, height, ratio);
            return new PixelRect(left ? anchorX - size.Width : anchorX, top ? anchorY - size.Height : anchorY, size.Width, size.Height);
        }

        private static PixelRect AspectSize(int maxWidth, int maxHeight, int width, int height, CaptureAspectRatio ratio)
        {
            bool landscape = ratio.Width >= ratio.Height;
            double scale = landscape ? (double)ratio.Width / ratio.Height : (double)ratio.Height / ratio.Width;
            int maxPrimary = (landscape ? maxWidth : maxHeight) / 2 * 2;
            int maxSecondary = (landscape ? maxHeight : maxWidth) / 2 * 2;
            maxPrimary = Math.Min(maxPrimary, (int)Math.Floor(maxSecondary * scale / 2) * 2);
            int minPrimary = (int)Math.Ceiling(scale) * 2;
            if (maxPrimary < minPrimary || maxSecondary < 2) return default(PixelRect);
            // Project the pointer onto the ratio's diagonal, then derive the shorter
            // side from the longer one, within one pixel of the selected ratio.
            // Reapplying a lock to rounded dimensions is stable (no size creep).
            double desired = ((landscape ? width : height) * scale + (landscape ? height : width)) * scale / (scale * scale + 1);
            int primary = Math.Max(minPrimary, Math.Min(maxPrimary, (int)Math.Round(desired / 2, MidpointRounding.AwayFromZero) * 2));
            int secondary = (int)Math.Round(primary / scale / 2, MidpointRounding.AwayFromZero) * 2;
            return landscape ? new PixelRect(0, 0, primary, secondary) : new PixelRect(0, 0, secondary, primary);
        }

        public static PixelRect RelativeToMonitor(PixelRect selection, PixelRect monitor)
        {
            return new PixelRect(selection.X - monitor.X, selection.Y - monitor.Y, selection.Width, selection.Height);
        }
    }

    public static class CaptureCountdown
    {
        public static double Remaining(double seconds, double elapsedSeconds) => Math.Max(0, seconds - elapsedSeconds);
        public static bool Finished(double seconds, double elapsedSeconds) => elapsedSeconds >= seconds;
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
