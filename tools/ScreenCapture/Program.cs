using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ScreenRecorderLib;

namespace AITools.ScreenCapture.Helper
{
    internal static class Program
    {
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32768 };
        private static readonly object OutputLock = new object();
        internal static void Emit(CaptureEvent message)
        {
            lock (OutputLock)
            {
                try { Console.Out.WriteLine(Json.Serialize(message)); Console.Out.Flush(); }
                catch (IOException) { }
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                // A GUI-subsystem process has no console code page. Set the redirected
                // stream encodings without Console.OutputEncoding (which calls SetConsoleOutputCP).
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
                // Checks dependency loading only. Never creates a window, registers a hotkey,
                // enumerates desktop pixels, or starts a recording.
                if (args.Length == 1 && args[0] == "--check")
                {
                    var options = RecorderOptions.Default;
                    string runtime = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                    var bundled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "ScreenRecorderLib.dll", "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll",
                        "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll", "vcruntime140.dll",
                        "vcruntime140_1.dll", "vcruntime140_threads.dll", "concrt140.dll"
                    };
                    foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                    {
                        // CLR0400-suffixed runtime modules belong to Windows/.NET Framework.
                        if (bundled.Contains(module.ModuleName))
                        {
                            if (!string.Equals(Path.GetDirectoryName(module.FileName), runtime, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Dependency loaded from outside the helper package: " + module.ModuleName);
                        }
                    }
                    Emit(new CaptureEvent { type = "ready", message = "ScreenRecorderLib 7.0.1 with AI Tools HDR support loaded; no capture started." });
                    GC.KeepAlive(options);
                    return 0;
                }
                if (args.Length != 6 || args[0] != "--parent" || args[2] != "--parent-start" || args[4] != "--cache")
                    throw new ArgumentException("Expected --parent <pid> --parent-start <ticks> --cache <absolute directory>.");
                Process parent = Process.GetProcessById(int.Parse(args[1]));
                if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(args[3])) throw new InvalidOperationException("Parent process changed.");
                string cache = Path.GetFullPath(args[5]);
                if (!Path.IsPathRooted(args[5]) || !string.Equals(new DirectoryInfo(cache).Name, "screen_recordings", StringComparison.Ordinal) ||
                    !string.Equals(Directory.GetParent(cache)?.Name, "tempCache", StringComparison.Ordinal))
                    throw new IOException("Capture output must be the app's tempCache/screen_recordings directory.");
                CapturePaths.AssertNoLinks(cache);
                Directory.CreateDirectory(cache);
                if (Environment.OSVersion.Version.Build < 19041) throw new NotSupportedException("Screen recording requires Windows 10 version 2004 or newer.");
                Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new CaptureApplication(parent, cache))
                {
                    Application.ThreadException += (sender, e) => app.Fail(e.Exception.Message);
                    Application.Run(app);
                }
                return 0;
            }
            catch (Exception ex) { Emit(new CaptureEvent { type = "error", message = "Screen recorder: " + ex.Message }); return 1; }
        }
    }

    internal sealed class CaptureApplication : ApplicationContext
    {
        private readonly Process _parent;
        private readonly string _cache;
        private readonly Control _dispatcher = new Control();
        private readonly CaptureToolbar _toolbar = new CaptureToolbar();
        private readonly HotkeyWindow _hotkey = new HotkeyWindow();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 25 };
        private readonly List<SelectionOverlay> _overlays = new List<SelectionOverlay>();
        private readonly CaptureSession _session = new CaptureSession();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Stopwatch _shutdownClock = new Stopwatch();
        private readonly CaptureWorker _worker;
        private CaptureSettings _settings = new CaptureSettings();
        private CaptureSettings _recordSettings;
        private RegionBorder _border;
        // Accessed only on _worker, never on the toolbar thread.
        private Recorder _recorder;
        private bool _nativeStarted;
        private int _nativeAttempt;
        private CancellationTokenSource _startCancellation;
        private string _completionPending;
        private bool _checkingDevice;
        private Screen _screen;
        private PixelRect _region;
        private string _outputPath, _audioDevice, _startupWarning;
        private bool _hardware, _shutdown, _configured;
        private volatile bool _disposed;
        private int _attempt;
        private long _lastDeviceCheck;

        internal CaptureApplication(Process parent, string cache)
        {
            _parent = parent; _cache = cache;
            IntPtr handle = _dispatcher.Handle;
            _worker = new CaptureWorker(ex => Post(() => Fail(ex.Message)));
            _toolbar.SelectRequested += Select;
            _toolbar.RecordRequested += RecordOrStop;
            _toolbar.CloseRequested += CloseToolbar;
            _toolbar.CancelRequested += Cancel;
            _toolbar.SettingsChanged += () => { _toolbar.ReadSettings(_settings); ApplyRegionSettings(); EmitSettings(); Refresh(); };
            _hotkey.Pressed += () => { if (_session.Active) RecordOrStop(); else ShowToolbar(); };
            _hotkey.CancelPressed += Cancel;
            _timer.Tick += Tick; _timer.Start();
            _toolbar.Apply(_settings);
            new Thread(ReadCommands) { IsBackground = true, Name = "Capture commands" }.Start();
            Program.Emit(new CaptureEvent { type = "ready" });
        }

        private void Post(Action action)
        {
            if (_disposed) return;
            try { _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) { try { action(); } catch (Exception ex) { Fail(ex.Message); } } })); }
            catch (InvalidOperationException) { }
        }

        private void ReadCommands()
        {
            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (line.Length > 32768) { Post(() => Fail("Recorder command was too large.")); continue; }
                    CaptureCommand command;
                    try { command = new JavaScriptSerializer { MaxJsonLength = 32768 }.Deserialize<CaptureCommand>(line); }
                    catch (Exception) { Post(() => Fail("Invalid recorder command.")); continue; }
                    Post(() => Command(command));
                }
            }
            catch (IOException) { }
            finally { Post(Shutdown); }
        }

        private void Command(CaptureCommand command)
        {
            if (command == null) return;
            switch (command.command)
            {
                case "configure":
                    string error;
                    if (command.settings == null || !command.settings.IsValid(out error)) { Fail("Invalid screen recorder settings."); return; }
                    _settings = command.settings; _configured = true; _toolbar.Apply(_settings);
                    if (!_session.Active) ApplyRegionSettings();
                    _startupWarning = _hotkey.Register(_settings);
                    if (_startupWarning != null) Program.Emit(new CaptureEvent { type = "error", message = _startupWarning });
                    EmitSettings(); Refresh(); break;
                case "show": if (_configured) ShowToolbar(); break;
                case "stop": Stop(); break;
                case "shutdown": Shutdown(); break;
                default: Fail("Unknown screen recorder command."); break;
            }
        }

        private void EmitSettings() { Program.Emit(new CaptureEvent { type = "settings", settings = _settings }); }
        private void ApplyRegionSettings()
        {
            if (_screen == null) return;
            if (_settings.fixedSize)
            {
                var monitor = MonitorRect();
                if (!CaptureGeometry.Fits(monitor, _settings.fixedWidth, _settings.fixedHeight))
                {
                    _screen = null; CloseOverlays(); _session.Ready(false);
                    _startupWarning = "Fixed size does not fit the selected monitor. Select another monitor or change the dimensions.";
                    EmitState(); return;
                }
                _region = CaptureGeometry.Move(monitor, new PixelRect(0, 0, _settings.fixedWidth, _settings.fixedHeight), _region.X, _region.Y);
            }
            else _region = CaptureGeometry.ApplyAspect(MonitorRect(), _region, _settings.aspectRatio);
            _border?.Dispose(); _border = null;
            if (_toolbar.Visible) ShowBorder();
        }
        private PixelRect MonitorRect() => new PixelRect(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height);
        private void ShowBorder()
        {
            if (_screen == null || _border != null) return;
            _border = new RegionBorder(MonitorRect(), _region, _settings.fixedSize, _settings.aspectRatio, rect => { _region = rect; Refresh(); });
            _border.SetState(_session.Active, _session.Stage == CaptureStage.Recording || _session.Stage == CaptureStage.Starting);
            _border.Show();
        }
        private void EmitState()
        {
            _hotkey.EnableCancel(_session.Stage == CaptureStage.Selecting || _session.Stage == CaptureStage.Countdown);
            Program.Emit(new CaptureEvent { type = "state", state = _session.Stage.ToString() });
            Refresh();
        }
        private void ShowToolbar()
        {
            if (_shutdown) return;
            if (_overlays.Count != 0) { _overlays[0].Activate(); return; }
            if (!_toolbar.Visible)
            {
                Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
                _toolbar.Location = new Point(work.Left + Math.Max(0, (work.Width - _toolbar.Width) / 2), work.Top + 20);
                _toolbar.Show();
            }
            ShowBorder();
            _toolbar.Activate(); Native.SetForegroundWindow(_toolbar.Handle); Refresh();
        }

        private void Select()
        {
            if (!_session.Select()) return;
            CloseOverlays(); _toolbar.Hide();
            foreach (Screen screen in Screen.AllScreens)
            {
                var overlay = new SelectionOverlay(screen, _settings, Selected, Cancel);
                _overlays.Add(overlay); overlay.Show();
            }
            _overlays.FirstOrDefault(o => o.Bounds.Contains(Cursor.Position))?.Activate();
            EmitState();
        }
        private void Selected(Screen screen, PixelRect rectangle)
        {
            _screen = screen; _region = rectangle;
            CloseOverlays(); _session.Ready(true); ShowToolbar(); EmitState();
        }
        private void CloseOverlays()
        {
            foreach (var overlay in _overlays) overlay.Dispose();
            _overlays.Clear();
            _border?.Dispose(); _border = null;
        }
        private void Cancel()
        {
            if (_session.Stage == CaptureStage.Countdown) _session.CancelCountdown();
            else if (_session.Active) return;
            CloseOverlays(); _session.Ready(_screen != null); ShowToolbar(); EmitState();
        }
        private void CloseToolbar()
        {
            if (_session.Stage == CaptureStage.Countdown) Cancel();
            if (_session.Active) Stop();
            else { CloseOverlays(); _toolbar.Hide(); }
        }
        private void RecordOrStop()
        {
            if (_session.Stage == CaptureStage.Countdown) { Cancel(); return; }
            if (_session.Active) { Stop(); return; }
            if (!_session.Countdown()) return;
            _recordSettings = new CaptureSettings { fps = _settings.fps, audio = _settings.audio, cursor = _settings.cursor, countdownSeconds = _settings.countdownSeconds };
            _clock.Restart(); _startupWarning = null; EmitState();
            if (CaptureCountdown.Finished(_recordSettings.countdownSeconds, 0)) BeginRecording();
        }

        private void BeginRecording()
        {
            if (!_session.Start()) return;
            _outputPath = null;
            _startCancellation = new CancellationTokenSource();
            _clock.Restart(); _lastDeviceCheck = 0; _audioDevice = null;
            Screen current = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _screen.DeviceName && s.Bounds == _screen.Bounds);
            if (current == null) { FailSession("The selected monitor changed. Select an area again."); return; }
            ShowBorder(); _border.SetState(true, true);
            EmitState();
            StartEncoder(true);
        }

        private void StartEncoder(bool hardware)
        {
            _hardware = hardware;
            int attempt = ++_attempt;
            string id = _session.Id;
            _outputPath = Path.Combine(_cache, "capture_" + Guid.NewGuid().ToString("N") + ".mp4");
            CapturePaths.AssertNoLinks(_outputPath);
            Rectangle bounds = _screen.Bounds;
            PixelRect local = CaptureGeometry.RelativeToMonitor(_region, new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            string path = _outputPath, display = _screen.DeviceName, audioDevice = _audioDevice;
            CaptureSettings settings = _recordSettings;
            CancellationToken cancelled = _startCancellation.Token;
            var startup = Stopwatch.StartNew();
            _worker.Enqueue(() =>
            {
                try
                {
                    DisposeRecorder();
                    if (cancelled.IsCancellationRequested) { Post(() => Complete(id, null, null, true)); return; }
                    if (settings.audio && audioDevice == null)
                    {
                        var device = Recorder.GetSystemAudioLoopbackDevices().FirstOrDefault(d => d.IsDefaultDevice);
                        if (device == null) throw new InvalidOperationException("No default playback device is available for system audio.");
                        audioDevice = device.DeviceName;
                        Post(() => { if (id == _session.Id) _audioDevice = audioDevice; });
                    }
                    long deviceMs = startup.ElapsedMilliseconds;
                    if (cancelled.IsCancellationRequested) { Post(() => Complete(id, null, null, true)); return; }
                    _nativeAttempt = attempt;
                    _recorder = Recorder.CreateRecorder(EncoderOptions(display, local, settings, audioDevice, hardware));
                    long createMs = startup.ElapsedMilliseconds;
                    _recorder.OnStatusChanged += (s, e) =>
                    {
                        if (e.Status != RecorderStatus.Recording) return;
                        long readyMs = startup.ElapsedMilliseconds;
                        Post(() =>
                        {
                            if (attempt != _attempt || id != _session.Id || _session.Stage != CaptureStage.Starting) return;
                            Program.Emit(new CaptureEvent { type = "diagnostic", message = "Startup " + readyMs + " ms (" + (hardware ? "hardware" : "software") + "): device " + deviceMs + " ms, recorder " + (createMs - deviceMs) + " ms, first frame " + (readyMs - createMs) + " ms." });
                            _session.Recording(id); _clock.Restart(); EmitState();
                        });
                    };
                    _recorder.OnRecordingComplete += (s, e) => Post(() => { if (attempt == _attempt) Complete(id, e.FilePath, null); });
                    _recorder.OnRecordingFailed += (s, e) => Post(() => { if (attempt == _attempt) EncoderFailed(id, e.FilePath, e.Error); });
                    if (cancelled.IsCancellationRequested) { Post(() => Complete(id, null, null, true)); return; }
                    _nativeStarted = true;
                    _recorder.Record(path);
                }
                catch (Exception ex) { Post(() => { if (attempt == _attempt) EncoderFailed(id, path, ex.Message); }); }
            });
        }

        private static RecorderOptions EncoderOptions(string display, PixelRect local, CaptureSettings settings, string audioDevice, bool hardware)
        {
            var options = RecorderOptions.Default;
            options.SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { new DisplayRecordingSource(display)
                {
                    RecorderApi = RecorderApi.DesktopDuplication,
                    SourceRect = new ScreenRect(local.X, local.Y, local.Width, local.Height),
                    IsCursorCaptureEnabled = settings.cursor
                } }
            };
            options.OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video };
            options.VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = settings.fps, IsFixedFramerate = true, Quality = 80,
                IsHardwareEncodingEnabled = hardware, IsMp4FastStartEnabled = true,
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.High }
            };
            options.AudioOptions = new AudioOptions
            {
                IsAudioEnabled = settings.audio, Bitrate = AudioBitrate.bitrate_192kbps, Channels = AudioChannels.Stereo,
                AudioSources = settings.audio ? new List<AudioSourceBase> { new LoopbackAudioSource(audioDevice) } : new List<AudioSourceBase>()
            };
            options.MouseOptions = new MouseOptions { IsMousePointerEnabled = settings.cursor, IsMouseClicksDetected = false };
            options.LogOptions = new LogOptions { IsLogEnabled = false };
            return options;
        }

        private void DisposeRecorder()
        {
            Recorder recorder = _recorder;
            _recorder = null; _nativeStarted = false;
            recorder?.Dispose();
        }

        private void EncoderFailed(string id, string path, string error)
        {
            if (id != _session.Id) return;
            if (_hardware && _session.Stage == CaptureStage.Starting && !_shutdown)
            {
                _startupWarning = "Hardware encoder unavailable; using software encoding at " + _recordSettings.fps + " fps.";
                Program.Emit(new CaptureEvent { type = "notice", message = _startupWarning });
                StartEncoder(false); return;
            }
            Complete(id, path, error);
        }
        private void Stop()
        {
            if (_session.Stage == CaptureStage.Countdown) { Cancel(); return; }
            if (!_session.Stop()) return;
            _startCancellation?.Cancel();
            int attempt = _attempt;
            _worker.Enqueue(() => { if (_nativeAttempt == attempt && _nativeStarted) _recorder?.Stop(); });
            EmitState();
        }
        private void Complete(string id, string path, string error, bool cancelled = false)
        {
            if (id == null || id != _session.Id || _completionPending == id) return;
            _completionPending = id; ++_attempt;
            _startCancellation?.Cancel();
            _session.Stop(); EmitState();
            // A native destructor can wait for a capture thread. Finalize off the UI
            // thread, then deliver completion exactly once, with the output closed.
            _worker.Enqueue(() =>
            {
                try { DisposeRecorder(); }
                catch (Exception ex) { error = error ?? ex.Message; }
                Post(() => DeliverCompletion(id, path, error, cancelled));
            });
        }
        private void DeliverCompletion(string id, string path, string error, bool cancelled)
        {
            if (!_session.Complete(id)) return;
            _completionPending = null;
            _startCancellation?.Dispose(); _startCancellation = null;
            CloseOverlays(); _toolbar.Hide();
            if (!string.IsNullOrWhiteSpace(error)) Program.Emit(new CaptureEvent { type = "error", message = "Recording stopped: " + error });
            if (!string.IsNullOrEmpty(path) && CapturePaths.IsRecording(_cache, path) && File.Exists(path) && new FileInfo(path).Length > 0)
            {
                Native.AllowSetForegroundWindow(_parent.Id);
                Program.Emit(new CaptureEvent { type = "completed", sessionId = id, path = path });
            }
            else if (error == null && !cancelled) Program.Emit(new CaptureEvent { type = "error", message = "The recorder did not produce a video file." });
            EmitState();
            if (_shutdown) ExitThread();
        }
        private void FailSession(string error) { Complete(_session.Id, _outputPath, error); }
        internal void Fail(string error)
        {
            _startupWarning = error;
            Program.Emit(new CaptureEvent { type = "error", message = error });
            CloseOverlays();
            if (_session.Stage == CaptureStage.Starting) FailSession(error);
            else if (_session.Stage == CaptureStage.Countdown) _session.CancelCountdown();
            else if (_session.Active) Stop();
            _session.Ready(_screen != null); EmitState();
        }

        private void Tick(object sender, EventArgs args)
        {
            try
            {
                if (!_shutdown && _parent.HasExited) Shutdown();
                if (_shutdown && _shutdownClock.ElapsedMilliseconds > 10000) { ExitThread(); return; }
                if (_session.Stage == CaptureStage.Countdown && CaptureCountdown.Finished(_recordSettings.countdownSeconds, _clock.Elapsed.TotalSeconds)) BeginRecording();
                if (_session.Stage == CaptureStage.Recording && _clock.ElapsedMilliseconds - _lastDeviceCheck >= 1000)
                {
                    _lastDeviceCheck = _clock.ElapsedMilliseconds;
                    if (!Screen.AllScreens.Any(s => s.DeviceName == _screen.DeviceName && s.Bounds == _screen.Bounds))
                        Fail("The captured monitor was disconnected or its layout changed.");
                    else if (_audioDevice != null && !_checkingDevice)
                    {
                        _checkingDevice = true;
                        string id = _session.Id, device = _audioDevice;
                        _worker.Enqueue(() =>
                        {
                            string error = null;
                            try { if (!Recorder.GetSystemAudioLoopbackDevices().Any(d => d.DeviceName == device)) error = "The recorded audio output device was disconnected."; }
                            catch (Exception ex) { error = "Could not check the recorded audio output device: " + ex.Message; }
                            Post(() => { _checkingDevice = false; if (id == _session.Id && _session.Stage == CaptureStage.Recording && error != null) Fail(error); });
                        });
                    }
                }
                Refresh();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }
        private void Refresh()
        {
            string status;
            switch (_session.Stage)
            {
                case CaptureStage.Countdown: status = "Starting in " + CaptureCountdown.Remaining(_recordSettings.countdownSeconds, _clock.Elapsed.TotalSeconds).ToString("0.0") + "s... Esc cancels."; break;
                case CaptureStage.Starting: status = "Preparing recording... " + _clock.Elapsed.TotalSeconds.ToString("0.0") + "s | Stop cancels"; break;
                case CaptureStage.Recording: status = "Recording " + _clock.Elapsed.ToString(@"hh\:mm\:ss") + " | " + _recordSettings.fps + " fps" + (_settings.hotkeyEnabled ? " | " + _settings.hotkey + " stops" : ""); break;
                case CaptureStage.Finishing: status = "Finishing recording..."; break;
                default: status = _startupWarning ?? (_screen == null ? "Select an area on any monitor." : _region.Width + " x " + _region.Height + " | " + _screen.DeviceName + " | Ready"); break;
            }
            _toolbar.UpdateState(_session.Stage, status);
            _border?.SetState(_session.Active, _session.Stage == CaptureStage.Recording || _session.Stage == CaptureStage.Starting || _session.Stage == CaptureStage.Finishing);
        }
        private void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true; _shutdownClock.Start();
            _hotkey.Dispose(); CloseOverlays(); _toolbar.Hide();
            if (_session.Stage == CaptureStage.Countdown) _session.CancelCountdown();
            if (_session.Active) Stop(); else ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true; _timer.Dispose(); CloseOverlays(); _hotkey.Dispose();
                _startCancellation?.Cancel();
                _worker.Enqueue(DisposeRecorder); _worker.Dispose();
                _toolbar.AllowClose = true; _toolbar.Dispose(); _dispatcher.Dispose(); _parent.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
