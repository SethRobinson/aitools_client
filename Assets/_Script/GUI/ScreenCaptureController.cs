using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AITools.AIChat.Video;
using AITools.ScreenCapture;
using UnityEngine;

/// <summary>Owns the Windows recorder process. All protocol callbacks reach Unity on Update.</summary>
public sealed class ScreenCaptureController : MonoBehaviour
{
    private const string PreferencesKey = "screen_capture_settings_v1";
    private const string ClockPreferencesKey = "desktop_clock_settings_v1";
    private static ScreenCaptureController _instance;
    private readonly ConcurrentQueue<KeyValuePair<Process, string>> _events = new ConcurrentQueue<KeyValuePair<Process, string>>();
    private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);
    private Process _process;
    private string _cache;
    private string _status = "Starting screen recorder...";
    private string _lastError;
    private bool _ready, _showWhenReady, _quitting;
    private bool _showClockWhenReady;
    private ChatVideoClipChooser _chooser;
    private readonly Queue<string> _pendingClips = new Queue<string>();
    private bool _openingClip;

    public static string StatusText => _instance == null ? "Recorder starts with AI Tools." : _instance._lastError ?? _instance._status;

    public static CaptureSettings GetSettings()
    {
        try
        {
            // Overwrite an initialized object so older preferences retain new-field defaults.
            var settings = new CaptureSettings();
            string json = PlayerPrefs.GetString(PreferencesKey, "");
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, settings);
            if (settings.IsValid(out _)) return settings;
        }
        catch (Exception) { }
        return new CaptureSettings();
    }

    public static bool SetSettings(CaptureSettings settings, out string error)
    {
        if (settings == null) { error = "Missing capture settings."; return false; }
        if (!settings.IsValid(out error)) return false;
        SaveSettings(settings);
        if (_instance != null)
        {
            _instance._lastError = null;
            if (_instance._ready) _instance.Send(new CaptureCommand { command = "configure", settings = settings });
        }
        return true;
    }

    private static void SaveSettings(CaptureSettings settings)
    {
        PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(settings));
        PlayerPrefs.Save();
    }

    public static void EnsureCreated()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_instance != null || GameLogic.Get() == null) return;
        _instance = GameLogic.Get().gameObject.AddComponent<ScreenCaptureController>();
        Application.runInBackground = true;
        _instance.StartHelper();
#endif
    }

    public static void ShowToolbar()
    {
        EnsureCreated();
        if (_instance == null) return;
        _instance._showWhenReady = true;
        if (_instance._process == null || _instance._process.HasExited) _instance.StartHelper();
        if (_instance._ready)
        {
            _instance.Send(new CaptureCommand { command = "show" });
            _instance._showWhenReady = false;
        }
    }

    private static DesktopClockSettings GetClockSettings()
    {
        try
        {
            var settings = new DesktopClockSettings();
            string json = PlayerPrefs.GetString(ClockPreferencesKey, "");
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, settings);
            if (settings.IsValid()) return settings;
        }
        catch (Exception) { }
        return new DesktopClockSettings();
    }

    public static void ShowClock()
    {
        EnsureCreated();
        if (_instance == null)
        {
            RTQuickMessageManager.Get()?.ShowMessage("The desktop clock is available on Windows.");
            return;
        }
        _instance._showClockWhenReady = true;
        if (_instance._process == null || _instance._process.HasExited) _instance.StartHelper();
        if (_instance._ready)
        {
            _instance.Send(new CaptureCommand { command = "clock_show" });
            _instance._showClockWhenReady = false;
        }
    }

    private void StartHelper()
    {
        if (_quitting) return;
        try
        {
            string root = RTSafeFileSystem.GetAppRoot(Application.dataPath);
            string executable = Path.Combine(root, "utils", "ScreenCapture", "AIToolsCapture.exe");
            if (!File.Exists(executable)) { Error("Screen recorder helper is missing. Build it with scripts/BuildScreenCapture.ps1."); return; }
            _cache = CapturePaths.CacheDirectory(root);
            Directory.CreateDirectory(_cache);
            _process?.Dispose();
            _ready = false; _lastError = null;
            var parent = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(executable)
            {
                Arguments = "--parent " + parent.Id + " --parent-start " + parent.StartTime.ToUniversalTime().Ticks + " --cache \"" + _cache + "\"",
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process = process;
            process.OutputDataReceived += (sender, args) => { if (args.Data != null && _process == process) _events.Enqueue(new KeyValuePair<Process, string>(process, args.Data)); };
            process.ErrorDataReceived += (sender, args) => { if (!string.IsNullOrWhiteSpace(args.Data) && _process == process) _events.Enqueue(new KeyValuePair<Process, string>(process, "stderr:" + args.Data)); };
            process.Exited += (sender, args) => { if (!_quitting && _process == process) _events.Enqueue(new KeyValuePair<Process, string>(process, "exited:")); };
            process.Start();
            process.StandardInput.AutoFlush = true;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            _status = "Starting screen recorder...";
        }
        catch (Exception ex) { _ready = false; Error("Could not start screen recorder: " + ex.Message); }
    }

    private void Send(CaptureCommand command)
    {
        try
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if ((command.command == "show" || command.command == "clock_show") && _process != null) AllowSetForegroundWindow(_process.Id);
#endif
            _process?.StandardInput.WriteLine(JsonUtility.ToJson(command));
        }
        catch (Exception ex) { Error("Screen recorder connection failed: " + ex.Message); }
    }

    private void Update()
    {
        while (_events.TryDequeue(out KeyValuePair<Process, string> entry))
        {
            if (_quitting || entry.Key != _process) continue;
            string line = entry.Value;
            try
            {
                if (line.StartsWith("stderr:", StringComparison.Ordinal)) { UnityEngine.Debug.LogWarning("Screen recorder: " + line.Substring(7)); continue; }
                if (line == "exited:") { _ready = false; Error("Screen recorder closed unexpectedly. Click Record screen to restart it."); continue; }
                var message = JsonUtility.FromJson<CaptureEvent>(line);
                if (message == null) continue;
                switch (message.type)
                {
                    case "ready":
                        _ready = true; _status = "Ready. " + GetSettings().hotkey + " opens the recording toolbar.";
                        Send(new CaptureCommand { command = "configure", settings = GetSettings() });
                        Send(new CaptureCommand { command = "clock_configure", clockSettings = GetClockSettings() });
                        if (_showWhenReady) { Send(new CaptureCommand { command = "show" }); _showWhenReady = false; }
                        if (_showClockWhenReady) { Send(new CaptureCommand { command = "clock_show" }); _showClockWhenReady = false; }
                        break;
                    case "settings":
                        if (message.settings != null && message.settings.IsValid(out _)) SaveSettings(message.settings);
                        break;
                    case "clock_settings":
                        if (message.clockSettings != null && message.clockSettings.IsValid())
                        {
                            PlayerPrefs.SetString(ClockPreferencesKey, JsonUtility.ToJson(message.clockSettings));
                            PlayerPrefs.Save();
                        }
                        break;
                    case "clock_error":
                        UnityEngine.Debug.LogWarning(message.message);
                        RTQuickMessageManager.Get()?.ShowMessage(message.message);
                        break;
                    case "state": _status = "Screen recorder: " + message.state; break;
                    case "error": Error(message.message); break;
                    case "notice": RTQuickMessageManager.Get().ShowMessage(message.message); break;
                    case "diagnostic": UnityEngine.Debug.Log("Screen recorder: " + message.message); break;
                    case "completed":
                        if (!string.IsNullOrEmpty(message.sessionId) && !_completed.Contains(message.sessionId) &&
                            CapturePaths.IsRecording(_cache, message.path) && File.Exists(message.path))
                        {
                            _completed.Add(message.sessionId);
                            _pendingClips.Enqueue(message.path);
                        }
                        break;
                }
            }
            catch (Exception ex) { Error("Could not read screen recorder response: " + ex.Message); }
        }
        if (!_openingClip && _chooser == null && _pendingClips.Count > 0)
            StartCoroutine(OpenRecording(_pendingClips.Dequeue()));
    }

    private IEnumerator OpenRecording(string path)
    {
        _openingClip = true;
        FfmpegTool.VideoInfo info = null;
        string error = null;
        yield return FfmpegTool.ProbeVideo(path, (result, failure) => { info = result; error = failure; });
        if (info == null || !string.IsNullOrEmpty(error) || info.DurationSeconds <= 0)
        {
            Error("Could not open the recording: " + (error ?? "the video was not finalized") + ". Source retained at " + path);
            _openingClip = false;
            yield break;
        }
        RestoreWindow();
        _chooser = ChatVideoClipChooser.Show(null, null, path, info,
            onClose: exported => _chooser = null,
            titleText: "Trim Screen Recording",
            initialDurationSeconds: (float)info.DurationSeconds,
            preserveSourceResolution: true,
            preserveSourceFrameRate: true);
        _openingClip = false;
    }

    private void Error(string message)
    {
        _lastError = message;
        UnityEngine.Debug.LogWarning(message);
        RTQuickMessageManager.Get()?.ShowMessage(message);
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
#endif
    private static void RestoreWindow()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == IntPtr.Zero) return;
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
#endif
    }

    // Called explicitly before GameLogic deletes tempCache on application exit.
    public static void Shutdown()
    {
        if (_instance != null) _instance.StopHelper();
    }
    private void StopHelper()
    {
        if (_quitting) return;
        _quitting = true;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                Send(new CaptureCommand { command = "shutdown" });
                _process.StandardInput.Close();
                if (!_process.WaitForExit(12000)) { _process.Kill(); _process.WaitForExit(2000); }
            }
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning("Recorder shutdown: " + ex.Message); }
        finally { _process?.Dispose(); _process = null; }
    }
    private void OnDestroy() { StopHelper(); if (_instance == this) _instance = null; }
}
