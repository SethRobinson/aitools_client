using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AITools.AIChat.Video;
using AITools.ScreenCapture;
using AITools.ScreenCapture.Helper;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static void Run(IEnumerator routine)
    {
        while (routine.MoveNext())
        {
            if (routine.Current is IEnumerator nested) Run(nested);
            else Thread.Sleep(5);
        }
    }
    private static string Q(string value) => "\"" + value + "\"";
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]);
            UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
            string evidence = Path.Combine(root, "build", "capture-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            SettingsAndGeometry();
            AspectRatios();
            Sessions();
            WorkerLifecycle();
            Paths(evidence);
            DependencyCheck(root);
            RejectedStartup(root);
            Media(root, evidence);
            Console.WriteLine("PASS: " + _checks + " screen recording checks. Evidence: " + evidence);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void AspectRatios()
    {
        Check(new CaptureSettings().aspectRatio == "Any", "Old preferences must default to an unlocked ratio");
        Check(CaptureAspectRatio.Options.Select(r => r.Name).Distinct().Count() == CaptureAspectRatio.Options.Length, "Duplicate ratio choices");
        Check(!new CaptureSettings { aspectRatio = "not-a-ratio" }.IsValid(out _), "Unsupported ratio accepted");
        var oldPreferences = JsonSerializer.Deserialize<CaptureSettings>("{\"fps\":60,\"audio\":false}", new JsonSerializerOptions { IncludeFields = true });
        Check(oldPreferences.aspectRatio == "Any" && oldPreferences.fps == 60 && !oldPreferences.audio, "Old preferences lost their values or new defaults");
        foreach (var ratio in CaptureAspectRatio.Options)
        {
            Check(new CaptureSettings { aspectRatio = ratio.Name }.IsValid(out _), "Supported ratio rejected: " + ratio.Name);
            var json = JsonSerializer.Serialize(new CaptureSettings { aspectRatio = ratio.Name, fixedSize = true }, new JsonSerializerOptions { IncludeFields = true });
            var restored = JsonSerializer.Deserialize<CaptureSettings>(json, new JsonSerializerOptions { IncludeFields = true });
            Check(restored.aspectRatio == ratio.Name && restored.fixedSize, "Switching modes must retain the Custom ratio");
            foreach (var monitor in new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(-2560, -400, 2560, 1440), new PixelRect(3840, -2400, 1440, 2560) })
            {
                var original = new PixelRect(monitor.X + 40, monitor.Y + 60, 614, 392);
                var adjusted = CaptureGeometry.ApplyAspect(monitor, original, ratio.Name);
                if (!ratio.IsLocked)
                {
                    Check(adjusted.Equals(original) && CaptureGeometry.Select(monitor, original.X, original.Y, original.X + 101, original.Y + 33, ratio.Name).Width == 100, "Any changed free selection");
                    continue;
                }
                CheckAspectRect(monitor, adjusted, ratio);
                Check(CaptureGeometry.ApplyAspect(monitor, adjusted, ratio.Name).Equals(adjusted), "Reapplying ratio moved or resized the box");
                var moved = CaptureGeometry.Move(monitor, adjusted, monitor.X - 5000, monitor.Y + 5000);
                Check(moved.Width == adjusted.Width && moved.Height == adjusted.Height, "Moving a locked rectangle changed size");
                for (int corner = 0; corner < 4; ++corner)
                {
                    bool left = (corner & 1) == 0, top = corner < 2;
                    int x = monitor.X + monitor.Width / 2, y = monitor.Y + monitor.Height / 2;
                    // Drag well beyond each monitor edge, including negative-coordinate desktops.
                    var selected = CaptureGeometry.Select(monitor, x, y, x + (left ? -5000 : 5000), y + (top ? -3000 : 3000), ratio.Name);
                    CheckAspectRect(monitor, selected, ratio);
                    Check((left ? selected.X + selected.Width : selected.X) == x && (top ? selected.Y + selected.Height : selected.Y) == y, "Selection moved its starting corner");
                    int anchorX = left ? adjusted.X + adjusted.Width : adjusted.X;
                    int anchorY = top ? adjusted.Y + adjusted.Height : adjusted.Y;
                    var resized = CaptureGeometry.Resize(monitor, adjusted, left, top, x + (left ? -5000 : 5000), y + (top ? -3000 : 3000), ratio.Name);
                    CheckAspectRect(monitor, resized, ratio);
                    Check((left ? resized.X + resized.Width : resized.X) == anchorX && (top ? resized.Y + resized.Height : resized.Y) == anchorY, "Locked resize moved its opposite corner");
                    var crossed = CaptureGeometry.Resize(monitor, adjusted, left, top, anchorX + (left ? 10 : -10), anchorY + (top ? 10 : -10), ratio.Name);
                    CheckAspectRect(monitor, crossed, ratio);
                    // Even-pixel rounding must not cause a size jump on a stationary corner.
                    var still = CaptureGeometry.Resize(monitor, adjusted, left, top, left ? adjusted.X : adjusted.X + adjusted.Width, top ? adjusted.Y : adjusted.Y + adjusted.Height, ratio.Name);
                    Check(still.Equals(adjusted), "Stationary resize changed ratio-rounded dimensions");
                }
                var exact = CaptureGeometry.Select(monitor, monitor.X, monitor.Y, monitor.X + ratio.Width * 2, monitor.Y + ratio.Height * 2, ratio.Name);
                Check(exact.Width == ratio.Width * 2 && exact.Height == ratio.Height * 2, "Exact ratio dimensions must stay exact");
                var tiny = CaptureGeometry.Select(monitor, monitor.X + monitor.Width - 1, monitor.Y + monitor.Height - 1, monitor.X + monitor.Width + 100, monitor.Y + monitor.Height + 100, ratio.Name);
                Check(tiny.Width == 0 && tiny.Height == 0, "One pixel at a monitor edge cannot contain an even capture");
                Check(CaptureGeometry.Select(monitor, original.X, original.Y, original.X, original.Y, ratio.Name).Width == 0, "Click without dragging unexpectedly selected an area");
            }
        }
    }
    private static void CheckAspectRect(PixelRect monitor, PixelRect rect, CaptureAspectRatio ratio)
    {
        Check(rect.Width >= 2 && rect.Height >= 2 && rect.Width % 2 == 0 && rect.Height % 2 == 0 &&
            rect.X >= monitor.X && rect.Y >= monitor.Y && rect.X + rect.Width <= monitor.X + monitor.Width && rect.Y + rect.Height <= monitor.Y + monitor.Height,
            "Ratio rectangle must stay inside its monitor with even dimensions: " + ratio.Name);
        double scale = ratio.Width >= ratio.Height ? (double)ratio.Width / ratio.Height : (double)ratio.Height / ratio.Width;
        double error = ratio.Width >= ratio.Height ? Math.Abs(rect.Height - rect.Width / scale) : Math.Abs(rect.Width - rect.Height / scale);
        Check(error <= 1.000001, "Ratio changed beyond pixel rounding: " + ratio.Name + " " + CaptureGeometry.Describe(rect));
    }
    private static void SettingsAndGeometry()
    {
        var settings = new CaptureSettings();
        Check(settings.countdownSeconds == 3 && !settings.fixedSize && settings.fixedWidth == 1280 && settings.fixedHeight == 720, "Region/countdown defaults");
        foreach (double seconds in new[] { 0, 0.3, 3.0, 10.25 })
        {
            settings.countdownSeconds = seconds;
            Check(settings.IsValid(out _) && CaptureCountdown.Finished(seconds, seconds) && CaptureCountdown.Remaining(seconds, seconds + 1) == 0, "Countdown boundary " + seconds);
        }
        Check(!CaptureCountdown.Finished(0.3, 0.299) && CaptureCountdown.Finished(0, 0), "Fractional/zero countdown timing");
        foreach (double seconds in new[] { -1, double.NaN, double.PositiveInfinity })
        { settings.countdownSeconds = seconds; Check(!settings.IsValid(out _), "Invalid countdown accepted"); }
        settings.countdownSeconds = 3;
        settings.fixedWidth = 1279; Check(!settings.IsValid(out _), "Odd fixed size accepted"); settings.fixedWidth = 1280;
        Check(settings.fps == 30 && settings.audio && settings.cursor && settings.hotkeyEnabled, "Capture defaults changed");
        foreach (int fps in CaptureSettings.FrameRates) { settings.fps = fps; Check(settings.IsValid(out _), "Supported frame rate rejected"); }
        foreach (int rate in new[] { 1, 25, 50, 144, 240, 1000, int.MaxValue })
        {
            settings.fps = rate;
            Check(settings.IsValid(out _), "Custom FPS rejected or arbitrarily capped");
            Check(CaptureSettings.TryParseFrameRate(" " + rate + " ", out int parsedRate) && parsedRate == rate, "Custom FPS parsing changed the target");
        }
        foreach (string badRate in new[] { "", "0", "-1", "29.97", "NaN", "Infinity", "3e1", "25fps", "2147483648" })
            Check(!CaptureSettings.TryParseFrameRate(badRate, out _), "Invalid FPS text accepted: " + badRate);
        foreach (int badRate in new[] { 0, -1, int.MinValue })
        { settings.fps = badRate; Check(!settings.IsValid(out _), "Invalid FPS setting accepted"); }
        Check(Hotkey.TryParse("Ctrl+Alt+F9", out uint mods, out uint key, out _) && mods == 3 && key == 0x78, "Default shortcut mapping");
        Check(Hotkey.TryParse("shift + alt + r", out mods, out key, out _) && mods == 5 && key == 'R', "Alternate shortcut mapping");
        foreach (string bad in new[] { "", "F9", "Win+F9", "Ctrl+Ctrl+A", "Ctrl+A+B", "Alt+F25", "Ctrl+" })
            Check(!Hotkey.TryParse(bad, out _, out _, out _), "Bad shortcut accepted: " + bad);
        foreach (var monitor in new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(-2560, -400, 2560, 1440), new PixelRect(1920, 0, 3840, 2160) })
        {
            var region = CaptureGeometry.Select(monitor, monitor.X + 40, monitor.Y + 60, monitor.X + 1041, monitor.Y + 661);
            var relative = CaptureGeometry.RelativeToMonitor(region, monitor);
            Check(relative.X == 40 && relative.Y == 60 && relative.Width == 1000 && relative.Height == 600, "Physical pixels must not be scaled twice");
            var reverse = CaptureGeometry.Select(monitor, monitor.X + 1041, monitor.Y + 661, monitor.X + 40, monitor.Y + 60);
            Check(reverse.X == region.X && reverse.Y == region.Y && reverse.Width == region.Width, "Reverse drag mismatch");
            var edge = CaptureGeometry.Select(monitor, monitor.X + 20, monitor.Y + 20, monitor.X - 3000, monitor.Y - 3000);
            Check(edge.X == monitor.X && edge.Y == monitor.Y && edge.Width == 20 && edge.Height == 20, "Cross-monitor drag escaped starting monitor");
            var moved = CaptureGeometry.Move(monitor, region, monitor.X - 5000, monitor.Y + 5000);
            Check(moved.X == monitor.X && moved.Y == monitor.Y + monitor.Height - region.Height && moved.Width == region.Width && moved.Height == region.Height, "Move must clamp without resizing");
            Check(!CaptureGeometry.Fits(monitor, monitor.Width + 2, 100), "Oversize fixed rectangle must be rejected");
            Check(CaptureGeometry.Move(monitor, new PixelRect(0, 0, monitor.Width + 2, 100), 0, 0).Width == 0, "Fixed dimensions silently shrank");
            for (int corner = 0; corner < 4; corner++)
            {
                bool left = (corner & 1) == 0, top = corner < 2;
                var resized = CaptureGeometry.Resize(monitor, region, left, top, left ? monitor.X - 200 : monitor.X + monitor.Width + 200, top ? monitor.Y - 200 : monitor.Y + monitor.Height + 200);
                Check(resized.X >= monitor.X && resized.Y >= monitor.Y && resized.X + resized.Width <= monitor.X + monitor.Width && resized.Y + resized.Height <= monitor.Y + monitor.Height && resized.Width % 2 == 0 && resized.Height % 2 == 0, "Resize escaped monitor or produced odd dimensions");
                Check((left ? resized.X + resized.Width : resized.X) == (left ? region.X + region.Width : region.X) && (top ? resized.Y + resized.Height : resized.Y) == (top ? region.Y + region.Height : region.Y), "Resize moved the opposite corner");
                var crossed = CaptureGeometry.Resize(monitor, region, left, top, left ? monitor.X + monitor.Width : monitor.X, top ? monitor.Y + monitor.Height : monitor.Y);
                Check(crossed.Width == 2 && crossed.Height == 2, "Crossed corner must stop at minimum size");
            }
        }
        var json = JsonSerializer.Serialize(new CaptureCommand { command = "configure", settings = new CaptureSettings { audio = false, cursor = false, fps = 120, fixedSize = true, fixedWidth = 960, fixedHeight = 600, countdownSeconds = 0.3 } }, new JsonSerializerOptions { IncludeFields = true });
        var parsed = JsonSerializer.Deserialize<CaptureCommand>(json, new JsonSerializerOptions { IncludeFields = true });
        Check(parsed.command == "configure" && !parsed.settings.audio && !parsed.settings.cursor && parsed.settings.fps == 120, "Protocol must retain false and 120 fps");
        Check(parsed.settings.fixedSize && parsed.settings.fixedWidth == 960 && parsed.settings.fixedHeight == 600 && parsed.settings.countdownSeconds == 0.3, "Protocol must retain fixed size and fractional countdown");
        settings.fps = 144;
        var savedRate = JsonSerializer.Deserialize<CaptureSettings>(JsonSerializer.Serialize(settings, new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true });
        Check(savedRate.fps == 144 && savedRate.IsValid(out _), "Saved custom FPS must survive a restart");
    }
    private static void Sessions()
    {
        var s = new CaptureSession();
        Check(!s.Countdown() && !s.Stop(), "Cannot record before choosing a region");
        s.Select(); s.Ready(true); Check(s.Countdown(), "Ready session should count down");
        string cancelled = s.Id;
        s.CancelCountdown(); Check(s.Stage == CaptureStage.Ready && !s.Complete(cancelled), "Cancelled countdown must never deliver a clip");
        s.Countdown(); string id = s.Id; Check(!s.Countdown() && !s.Select(), "Prevent concurrent capture");
        s.Start(); Check(s.Stop(), "Stop during encoder startup");
        s.Recording(id); Check(s.Stage == CaptureStage.Finishing, "Late recording callback undid Stop");
        Check(s.Complete(id) && !s.Complete(id), "Deliver completion exactly once");
        s.Countdown(); string next = s.Id; s.Start(); s.Recording(next);
        Check(!s.Complete(id) && s.Stage == CaptureStage.Recording, "Stale callback interrupted a new recording");
        s.Stop(); Check(!s.Stop() && s.Complete(next), "Repeated Stop must be harmless");
    }
    private static void WorkerLifecycle()
    {
        var order = new ConcurrentQueue<string>();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var done = new ManualResetEventSlim())
        using (var cancellation = new CancellationTokenSource())
        using (var worker = new CaptureWorker(ex => order.Enqueue(ex.Message)))
        {
            int caller = Thread.CurrentThread.ManagedThreadId, workerId = 0;
            ApartmentState apartment = ApartmentState.Unknown;
            worker.Enqueue(() =>
            {
                workerId = Thread.CurrentThread.ManagedThreadId;
                apartment = Thread.CurrentThread.GetApartmentState();
                entered.Set();
                if (!release.Wait(5000)) throw new Exception("Worker fixture release timed out");
                order.Enqueue(cancellation.IsCancellationRequested ? "cancelled startup" : "record");
            });
            try
            {
                Check(entered.Wait(5000), "Encoder work did not start");
                // Submission must return while startup is blocked, just as Stop/close must.
                cancellation.Cancel();
                Check(worker.Enqueue(() => order.Enqueue("stop")), "Stop could not be queued during startup");
                worker.Enqueue(() => { throw new InvalidOperationException("expected failure"); });
                worker.Enqueue(() => { order.Enqueue("dispose"); done.Set(); });
                worker.Dispose();
                Check(!worker.Enqueue(() => order.Enqueue("unexpected")), "Closed worker accepted new work");
                Check(!done.IsSet, "Concurrent recorder calls escaped serialization");
            }
            finally { release.Set(); }
            Check(done.Wait(5000), "Worker did not drain Stop/disposal after startup");
            Check(workerId != caller && apartment == ApartmentState.MTA, "Encoder work must use its own MTA thread");
            Check(string.Join(",", order) == "cancelled startup,stop,expected failure,dispose", "Cancellation/error/shutdown ordering changed");
        }
    }
    private static void Paths(string root)
    {
        string cache = CapturePaths.CacheDirectory(root);
        Check(CapturePaths.IsRecording(cache, Path.Combine(cache, "capture_" + Guid.NewGuid().ToString("N") + ".mp4")), "Valid capture path rejected");
        Check(!CapturePaths.IsRecording(cache, Path.Combine(root, "capture_" + Guid.NewGuid().ToString("N") + ".mp4")), "Outside cache path accepted");
        Check(!CapturePaths.IsRecording(cache, Path.Combine(cache, "other.mp4")), "Foreign cache file accepted");
        Check(!CapturePaths.IsRecording(cache, "relative.mp4"), "Relative capture path accepted");
    }
    private static void DependencyCheck(string root)
    {
        var start = new ProcessStartInfo(Path.Combine(root, "utils", "ScreenCapture", "AIToolsCapture.exe"), "--check")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd();
            Check(process.WaitForExit(15000) && process.ExitCode == 0, "Packaged helper dependencies: " + output + error);
            var ready = JsonSerializer.Deserialize<CaptureEvent>(output, new JsonSerializerOptions { IncludeFields = true });
            Check(ready.type == "ready", "GUI helper must return UTF-8 JSON over redirected stdout");
        }
    }
    private static void RejectedStartup(string root)
    {
        // Both errors occur before creating UI or registering shortcuts.
        var parent = Process.GetCurrentProcess();
        string mismatchedParent = "--parent " + parent.Id + " --parent-start " +
            (parent.StartTime.ToUniversalTime().Ticks + 1) + " --cache " + Q(Path.Combine(root, "tempCache", "screen_recordings"));
        foreach (string arguments in new[] { "--invalid-argument", mismatchedParent })
        {
            var start = new ProcessStartInfo(Path.Combine(root, "utils", "ScreenCapture", "AIToolsCapture.exe"), arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                Check(process.WaitForExit(15000) && process.ExitCode == 1 &&
                    JsonSerializer.Deserialize<CaptureEvent>(output, new JsonSerializerOptions { IncludeFields = true }).type == "error",
                    "Invalid startup must report an error over the pipe without opening UI: " + output + error);
            }
        }
    }
    private static void Media(string root, string evidence)
    {
        string ffmpeg = Path.Combine(root, "utils", "ffmpeg", "bin", "ffmpeg.exe");
        string source = Path.Combine(evidence, "synthetic.mp4");
        // Synthetic-only frames and tone. No desktop/audio-device input is opened.
        var generation = FfmpegTool.RunProcessCancellable(ffmpeg,
            "-hide_banner -y -f lavfi -i testsrc2=size=960x600:rate=120:duration=2.4 -f lavfi -i " +
            Q("aevalsrc=if(between(t\\,0.6\\,1.8)\\,0.25*sin(2*PI*440*t)\\,0):s=48000:d=2.4") +
            " -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -c:a aac -shortest " + Q(source), 60000, null, null);
        Check(generation.Success, "Synthetic fixture generation: " + generation.Stderr);
        FfmpegTool.VideoInfo original = null;
        Run(FfmpegTool.ProbeVideo(source, (i, error) => original = i));
        Check(original != null && original.Width == 960 && original.Height == 600 && original.HasAudio, "Synthetic source probe");
        foreach (int rate in CaptureSettings.FrameRates.Concat(new[] { 25, 50, 144 }))
        {
            string path = Path.Combine(evidence, "native_" + rate + ".mp4");
            FfmpegTool.ClipResult result = null;
            Run(FfmpegTool.CreateClip(source, 0.3f, 1.5f, path, r => result = r, fps: rate, maxWidth: original.Width, maxHeight: original.Height, includeAudio: true, preserveRequestedFrameRate: true));
            Check(result != null && result.Success, "Native export at " + rate + ": " + result?.Error);
            FfmpegTool.VideoInfo info = null; Run(FfmpegTool.ProbeVideo(path, (i, error) => info = i));
            Check(info != null && info.Width == 960 && info.Height == 600, "Native export was resized");
            Check(Math.Abs(info.Fps - rate) < 0.01 && Math.Abs(info.DurationSeconds - 1.5) < 0.15 && info.HasAudio, "Frame rate/duration/audio changed at " + rate + " FPS");
            var silence = FfmpegTool.RunProcessCancellable(ffmpeg, "-hide_banner -i " + Q(path) + " -af silencedetect=noise=-40dB:d=0.1 -vn -f null -", 15000, null, null);
            Check(silence.Success && silence.Stderr.Contains("silence_start: 0"), "Leading silence shifted audio");
        }
        string legacy = Path.Combine(evidence, "existing-defaults.mp4");
        FfmpegTool.ClipResult old = null;
        Run(FfmpegTool.CreateClip(source, 0, 0.7f, legacy, r => old = r));
        Check(old.Success, "Existing import defaults failed");
        FfmpegTool.VideoInfo legacyInfo = null; Run(FfmpegTool.ProbeVideo(legacy, (i, error) => legacyInfo = i));
        Check(legacyInfo.Width <= 832 && legacyInfo.Height <= 480 && legacyInfo.Fps == 16 && legacyInfo.DurationSeconds < 1, "Existing sizing/FPS/short-clip behavior changed");
    }
}
