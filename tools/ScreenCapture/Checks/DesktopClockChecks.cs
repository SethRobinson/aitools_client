using System;
using System.Text.Json;
using AITools.ScreenCapture;

internal static class DesktopClockChecks
{
    internal static void Run(Action<bool, string> check)
    {
        double now = 1000;
        var timer = new DesktopClockTimer(() => now);
        check(!timer.Running && timer.Elapsed == 0, "Clock should begin paused at zero");
        timer.Toggle(); now += 65.5;
        check(timer.Running && timer.Elapsed == 65.5, "Monotonic elapsed time, including fractional seconds");
        timer.Toggle(); now += 500;
        check(!timer.Running && timer.Elapsed == 65.5, "Pause excludes waiting time");
        timer.Toggle(); now += 25 * 3600 + 2;
        check(DesktopClockTimer.FormatElapsed(timer.Elapsed) == "25:01:07", "Total hours must survive day rollover");
        // Sampling is optional: no update calls were needed across that whole day.
        check(timer.Elapsed == 90067.5, "Delayed paint must not lose elapsed time");
        timer.Reset(); check(!timer.Running && timer.Elapsed == 0, "Reset also pauses");
        timer.Toggle(); now += 12;
        timer.PrepareCapture("canceled", 1); timer.StopCapture("canceled");
        timer.StartCapture("canceled", 1, true, now);
        check(timer.Running && timer.Elapsed == 12 && !timer.Synchronized, "Canceled startup preserves a manual stopwatch");
        timer.PrepareCapture("capture", 2);
        timer.PrepareCapture("capture", 3); // hardware retry
        timer.StartCapture("capture", 2, true, now);
        timer.StartCapture("old-session", 3, true, now);
        check(timer.Elapsed == 12 && !timer.Synchronized, "Old attempt or session must not start synchronization");
        double firstFrame = now;
        now += 0.75; // UI dispatch lags behind the native Recording callback.
        timer.StartCapture("capture", 3, true, firstFrame);
        check(timer.Synchronized && timer.Elapsed == .75, "Native timestamp excludes startup and includes dispatch delay");
        timer.Reset(); timer.Toggle();
        check(timer.Running && timer.Elapsed == .75, "Manual actions cannot alter synchronized recording");
        timer.StartCapture("capture", 3, true, now);
        check(timer.Elapsed == .75, "Duplicate Recording notification must not reset");
        timer.StopCapture("old-session");
        check(timer.Running, "Stale stop must not pause current capture");
        now += 4; timer.StopCapture("capture"); now += 90;
        check(!timer.Running && !timer.Synchronized && timer.Elapsed == 4.75, "Stop excludes finalization");
        timer.StartCapture("capture", 3, true, now);
        check(!timer.Running && timer.Elapsed == 4.75, "Late start after failure or stop must be rejected");
        timer.PrepareCapture("manual", 4); timer.StartCapture("manual", 4, false, now);
        check(!timer.Running && timer.Elapsed == 4.75, "Sync off preserves paused timer");
        timer.Toggle(); now += 2; timer.StopCapture("manual");
        check(timer.Running && timer.Elapsed == 6.75, "Recorder stop must not stop independent timer");
        timer.PrepareCapture("failed", 5);
        timer.StartCapture("failed", 5, true, now);
        double failureAt = now + 3;
        now += 10;
        timer.StopCapture("failed", failureAt);
        check(!timer.Running && timer.Elapsed == 3, "Delayed failure delivery must exclude time after the native failure");

        check(DesktopClockTimer.SecondAngle(15) == 90 && DesktopClockTimer.SecondAngle(60) == 0, "Second hand quarter and rollover");
        check(DesktopClockTimer.MinuteAngle(900) == 90 && DesktopClockTimer.MinuteAngle(3600) == 0, "Minute hand quarter and rollover");
        check(DesktopClockTimer.HourAngle(10800) == 90 && DesktopClockTimer.HourAngle(43200) == 0, "Hour hand quarter and rollover");
        check(DesktopClockTimer.HourAngle(3600) == 30 && DesktopClockTimer.MinuteAngle(65) == 6.5, "Hands interpolate independently");
        check(DesktopClockTimer.SecondAngle(1.5) == 9, "Second hand sweeps between seconds");

        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        var defaults = JsonSerializer.Deserialize<DesktopClockSettings>("{}", jsonOptions);
        check(defaults.IsValid() && defaults.digital && defaults.widthDip == 220 && !defaults.sync && !defaults.stopwatch, "Preference defaults");
        var settings = new DesktopClockSettings { stopwatch = true, digital = false, sync = true, locked = true, positionSaved = true, x = -1800, y = -500, widthDip = 310.5 };
        var command = new CaptureCommand { command = "clock_configure", clockSettings = settings };
        var restored = JsonSerializer.Deserialize<CaptureCommand>(JsonSerializer.Serialize(command, jsonOptions), jsonOptions);
        check(restored.clockSettings.IsValid() && restored.clockSettings.x == -1800 && restored.clockSettings.widthDip == 310.5 &&
            restored.clockSettings.stopwatch && restored.clockSettings.sync && restored.clockSettings.locked && !restored.clockSettings.digital, "Clock protocol round trip");
        settings.Copy().widthDip = 400;
        check(settings.widthDip == 310.5, "Window settings copy must not mutate stored configuration");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0, 139, 641 })
            check(!new DesktopClockSettings { widthDip = invalid }.IsValid(), "Invalid clock size accepted");

        foreach (int dpi in new[] { 96, 120, 144, 192 })
        foreach (bool digital in new[] { false, true })
        foreach (double width in new[] { 140d, 220, 640 })
        foreach (var monitor in new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(-2560, -400, 2560, 1400), new PixelRect(1920, -1200, 1080, 1920), new PixelRect(0, 0, 120, 100) })
        {
            var rect = DesktopClockGeometry.Fit(monitor, -50000, 50000, width, dpi, digital);
            check(rect.X >= monitor.X && rect.Y >= monitor.Y && rect.X + rect.Width <= monitor.X + monitor.Width &&
                rect.Y + rect.Height <= monitor.Y + monitor.Height, "Recovered clock must fit available monitor");
            check(Math.Abs(rect.Height - rect.Width * DesktopClockGeometry.HeightRatio(digital)) <= .5, "Clock resize must preserve proportions");
            check(DesktopClockGeometry.Fit(monitor, rect.X, rect.Y, width, dpi, digital).Equals(rect), "Repeated placement must be stable");
        }
        var one = DesktopClockGeometry.Fit(new PixelRect(0, 0, 4000, 3000), 100, 100, 220, 96, true);
        var two = DesktopClockGeometry.Fit(new PixelRect(0, 0, 4000, 3000), 100, 100, 220, 192, true);
        check(two.Width == one.Width * 2, "Logical clock width should follow monitor DPI");
    }
}
