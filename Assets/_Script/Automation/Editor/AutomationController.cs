using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

// Editor-hosted loopback control server for the automation harness.
//
// This lives in the Editor assembly ON PURPOSE: it must survive entering/exiting play
// mode AND C# domain reloads so the HTTP port never dies mid-loop. Static state is reset
// on every domain reload, so anything that must persist across a recompile (the in-flight
// "rebuild & restart" stage) is stored in SessionState, which survives reloads within an
// editor session.
//
// Split of responsibilities:
//   - HTTP listener thread: accepts requests, reads a cached status SNAPSHOT, and for
//     mutating commands enqueues an Action onto the main thread. It must never touch
//     EditorApplication.* (not thread-safe) directly.
//   - Editor-update tick (main thread): drains the queue, refreshes the snapshot from
//     EditorApplication state + AutomationBridge, and pumps the rebuild state machine.
//
// Endpoints (loopback only, http://127.0.0.1:<port>/):
//   GET  /status     -> JSON: playing/compiling/driverReady/idle/chatActive/stage/...
//   POST /rebuild    -> exit play (if needed) -> recompile -> re-enter play, no clicks
//   POST /play       -> enter play mode
//   POST /stop       -> exit play mode
//   POST /open_chat   -> open (create if needed) the AI Chat panel
//   POST /settings    -> body: tab=<general|configuration|comfyui|audio|llm>; open Settings panel
//   POST /llm_settings -> open the advanced LLM Settings panel
//   POST /server_settings -> body: id=<serverID>; open that server's Overrides panel
//   POST /chat        -> body = message text; open chat + send one turn
//   POST /chat_import_video -> body: path=<file>, optional start=<seconds>, duration=<seconds>, fps=<n>, audio=<true|false>; import clipped Movie bubble
//   GET  /chat_images -> JSON array: index/w/h/busy/movie for each chat image
//   POST /save        -> body: index=<n|latest>, path=<file>; save chat image PNG
//   POST /screenshot  -> body: path=<file> [x,y,w,h top-left region]; capture game view
//
// Off by default. Toggle via Tools > RT Automation > Enable Control Server.
[InitializeOnLoad]
public static class AutomationController
{
    const int kPort = 8772;

    const string kEnabledPref = "RT_Automation_Enabled";      // EditorPrefs: persists across sessions
    const string kStageKey = "RT_Automation_RebuildStage";     // SessionState: survives domain reload
    const string kCompileRequestedKey = "RT_Automation_CompileRequested";
    const string kCompileSinceKey = "RT_Automation_CompileSince"; // editor-uptime when compile was requested

    // Safety net: if an explicit recompile somehow does NOT trigger a domain reload (e.g.
    // nothing changed), advance to the play stage after this many seconds of idle instead
    // of hanging forever. The normal path advances immediately on afterAssemblyReload.
    const double kCompileFallbackSeconds = 12.0;

    // Rebuild state machine stages (stored in SessionState).
    const string kStageNone = "";
    const string kStageStop = "stop";       // waiting for play mode to fully exit
    const string kStageCompile = "compile"; // requesting + waiting for script compilation
    const string kStagePlay = "play";       // re-entering play mode, waiting for driver

    static TcpListener _listener;
    static Thread _listenerThread;
    static volatile bool _running;

    static readonly object _lock = new object();
    static readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

    // Cached status snapshot, refreshed each editor tick on the main thread and read by
    // the listener thread. Guarded by _snapLock.
    static readonly object _snapLock = new object();
    static bool _snapPlaying, _snapCompiling, _snapDriverReady, _snapIdle, _snapChatActive;
    static string _snapStage = kStageNone;

    static AutomationController()
    {
        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += StopServer;
        AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        EditorApplication.quitting += StopServer;
        if (IsEnabled)
            StartServer();
    }

    // ---- Enable toggle ----------------------------------------------------

    static bool IsEnabled
    {
        get { return EditorPrefs.GetBool(kEnabledPref, false); }
        set { EditorPrefs.SetBool(kEnabledPref, value); }
    }

    [MenuItem("Tools/RT Automation/Enable Control Server", false, 0)]
    static void ToggleEnabled()
    {
        IsEnabled = !IsEnabled;
        if (IsEnabled) StartServer();
        else StopServer();
        Debug.Log($"[Automation] Control server {(IsEnabled ? "ENABLED" : "disabled")} (loopback :{kPort}).");
    }

    [MenuItem("Tools/RT Automation/Enable Control Server", true)]
    static bool ToggleEnabledValidate()
    {
        Menu.SetChecked("Tools/RT Automation/Enable Control Server", IsEnabled);
        return true;
    }

    [MenuItem("Tools/RT Automation/Rebuild && Restart Play", false, 20)]
    static void MenuRebuild()
    {
        EnqueueMain(StartRebuild);
    }

    // ---- HTTP server ------------------------------------------------------

    static void StartServer()
    {
        if (_running) return;
        try
        {
            // Raw TcpListener on loopback rather than HttpListener: avoids the Windows
            // HTTP.sys URL-ACL reservation that otherwise demands admin for arbitrary ports.
            _listener = new TcpListener(IPAddress.Loopback, kPort);
            // Tolerate a lingering socket from the previous domain (rapid play-stop reloads).
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "RT-Automation-HTTP" };
            _listenerThread.Start();
            Debug.Log($"[Automation] Control server listening on http://127.0.0.1:{kPort}/");
        }
        catch (Exception e)
        {
            _running = false;
            Debug.LogError($"[Automation] Failed to start control server on :{kPort} — {e.Message}");
        }
    }

    static void StopServer()
    {
        if (!_running && _listener == null) return;
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        // The listener thread is a background thread blocked in AcceptTcpClient(); Stop()
        // makes it throw and exit. Don't Join() here — beforeAssemblyReload must return fast.
        _listenerThread = null;
    }

    static void ListenLoop()
    {
        while (_running)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch { break; } // listener stopped / disposed
            try { using (client) HandleClient(client); }
            catch (Exception e) { Debug.LogWarning($"[Automation] Request error: {e.Message}"); }
        }
    }

    // Minimal HTTP/1.1 request handler: read the request line, skip headers, route on the
    // path, write one JSON response, close. Sufficient for a loopback control channel.
    static void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;
        using (var stream = client.GetStream())
        {
            string requestLine = ReadLine(stream);
            if (string.IsNullOrEmpty(requestLine)) return;

            string[] parts = requestLine.Split(' ');
            string rawPath = parts.Length > 1 ? parts[1] : "/";

            // Drain the headers, capturing Content-Length so we can read a request body.
            int contentLength = 0;
            string headerLine;
            while (!string.IsNullOrEmpty(headerLine = ReadLine(stream)))
            {
                int colon = headerLine.IndexOf(':');
                if (colon > 0 && headerLine.Substring(0, colon).Trim().ToLowerInvariant() == "content-length")
                    int.TryParse(headerLine.Substring(colon + 1).Trim(), out contentLength);
            }
            string body = ReadBody(stream, contentLength);

            int q = rawPath.IndexOf('?');
            if (q >= 0) rawPath = rawPath.Substring(0, q);
            string path = rawPath.TrimEnd('/').ToLowerInvariant();

            switch (path)
            {
                case "":
                case "/status":
                    WriteJson(stream, 200, StatusJson());
                    break;

                case "/rebuild":
                    EnqueueMain(StartRebuild);
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"rebuild\"}");
                    break;

                case "/play":
                    EnqueueMain(() => { if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true; });
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"play\"}");
                    break;

                case "/stop":
                    EnqueueMain(() => { if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; });
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"stop\"}");
                    break;

                case "/open_chat":
                    EnqueueMain(() => AutomationBridge.OpenChat());
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"open_chat\"}");
                    break;

                case "/settings":
                {
                    var kv = ParseKeyValues(body);
                    string tab = kv.TryGetValue("tab", out var tabValue) ? tabValue : body;
                    EnqueueMain(() => AutomationBridge.OpenSettings(tab));
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"settings\"}");
                    break;
                }

                case "/llm_settings":
                    EnqueueMain(() => AutomationBridge.OpenLLMSettings());
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"llm_settings\"}");
                    break;

                case "/server_settings":
                {
                    var kv = ParseKeyValues(body);
                    int serverID = 0;
                    if (kv.TryGetValue("id", out var idText))
                        int.TryParse(idText, out serverID);
                    EnqueueMain(() => AutomationBridge.OpenServerSettings(serverID));
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"server_settings\"}");
                    break;
                }

                case "/chat":
                    // Body is the raw message text. Open the chat panel first so a fresh
                    // session can be driven without a separate /open_chat call.
                    string message = body;
                    EnqueueMain(() =>
                    {
                        AutomationBridge.OpenChat();
                        AutomationBridge.SendChat(message);
                    });
                    WriteJson(stream, 200, "{\"ok\":true,\"accepted\":\"chat\"}");
                    break;

                case "/chat_import_video":
                {
                    // Body: key=value lines. path=<file>; optional start=<seconds>, duration=<seconds>.
                    var kv = ParseKeyValues(body);
                    string videoPath = kv.TryGetValue("path", out var vp) ? vp : "";
                    float startSeconds = ParseFloat(kv, "start", 0f);
                    float durationSeconds = ParseFloat(kv, "duration", 5f);
                    float fps = ParseFloat(kv, "fps", 0f);
                    bool includeAudio = ParseBool(kv, "audio", ParseBool(kv, "include_audio", true));
                    if (ParseBool(kv, "no_audio", false))
                        includeAudio = false;
                    string result = RunOnMainAndWait(() =>
                    {
                        AutomationBridge.OpenChat();
                        bool ok = AutomationBridge.ImportChatVideo(videoPath, startSeconds, durationSeconds, fps, includeAudio, out string err);
                        return ok
                            ? $"{{\"ok\":true,\"accepted\":\"chat_import_video\",\"path\":{JsonStr(videoPath)}}}"
                            : $"{{\"ok\":false,\"error\":{JsonStr(err)}}}";
                    }, "{\"ok\":false,\"error\":\"timed out\"}");
                    WriteJson(stream, 200, result);
                    break;
                }

                case "/chat_images":
                    WriteJson(stream, 200, RunOnMainAndWait(AutomationBridge.ChatImagesJson, "[]"));
                    break;

                case "/save":
                {
                    // Body: key=value lines. index=<n|latest> (default latest), path=<file>.
                    var kv = ParseKeyValues(body);
                    int saveIndex = ParseIndex(kv);
                    string savePath = kv.TryGetValue("path", out var sp) ? sp : "";
                    string result = RunOnMainAndWait(() =>
                    {
                        bool ok = AutomationBridge.Save(saveIndex, savePath, out string err);
                        return ok
                            ? $"{{\"ok\":true,\"saved\":{JsonStr(savePath)}}}"
                            : $"{{\"ok\":false,\"error\":{JsonStr(err)}}}";
                    }, "{\"ok\":false,\"error\":\"timed out\"}");
                    WriteJson(stream, 200, result);
                    break;
                }

                case "/screenshot":
                {
                    // Body: path=<file>; optional x,y,w,h (top-left pixel region; omit for full).
                    var kv = ParseKeyValues(body);
                    string shotPath = kv.TryGetValue("path", out var pp) ? pp : "";
                    int x = ParseInt(kv, "x", 0), y = ParseInt(kv, "y", 0);
                    int w = ParseInt(kv, "w", 0), h = ParseInt(kv, "h", 0);
                    if (string.IsNullOrWhiteSpace(shotPath))
                    {
                        WriteJson(stream, 200, "{\"ok\":false,\"error\":\"no path given\"}");
                        break;
                    }
                    EnqueueMain(() => AutomationBridge.Screenshot(shotPath, x, y, w, h));
                    // Async: the file appears ~1 frame later. Client polls for the file.
                    WriteJson(stream, 200, $"{{\"ok\":true,\"accepted\":\"screenshot\",\"path\":{JsonStr(shotPath)}}}");
                    break;
                }

                default:
                    WriteJson(stream, 404, "{\"ok\":false,\"error\":\"unknown endpoint\"}");
                    break;
            }
        }
    }

    // Read a single CRLF-terminated line from the stream as ASCII. Returns "" on a bare
    // blank line (the header terminator) and on end-of-stream.
    static string ReadLine(NetworkStream stream)
    {
        var sb = new StringBuilder();
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (b == '\n') break;
            if (b != '\r') sb.Append((char)b);
        }
        return sb.ToString();
    }

    static string StatusJson()
    {
        bool playing, compiling, driverReady, idle, chatActive;
        string stage;
        lock (_snapLock)
        {
            playing = _snapPlaying; compiling = _snapCompiling;
            driverReady = _snapDriverReady; idle = _snapIdle; stage = _snapStage;
            chatActive = _snapChatActive;
        }
        bool busy = stage != kStageNone;
        // "ready" = settled and drivable: playing, compiled, driver registered, not mid-rebuild.
        bool ready = playing && !compiling && driverReady && !busy;
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"ok\":true,");
        sb.Append("\"enabled\":true,");
        sb.Append("\"playing\":").Append(playing ? "true" : "false").Append(",");
        sb.Append("\"compiling\":").Append(compiling ? "true" : "false").Append(",");
        sb.Append("\"driverReady\":").Append(driverReady ? "true" : "false").Append(",");
        sb.Append("\"idle\":").Append(idle ? "true" : "false").Append(",");
        sb.Append("\"chatActive\":").Append(chatActive ? "true" : "false").Append(",");
        sb.Append("\"ready\":").Append(ready ? "true" : "false").Append(",");
        sb.Append("\"rebuilding\":").Append(busy ? "true" : "false").Append(",");
        sb.Append("\"stage\":\"").Append(stage).Append("\",");
        sb.Append("\"port\":").Append(kPort);
        sb.Append("}");
        return sb.ToString();
    }

    // Read exactly contentLength bytes of body as UTF-8 (after the header terminator).
    static string ReadBody(NetworkStream stream, int contentLength)
    {
        if (contentLength <= 0) return "";
        byte[] buf = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = stream.Read(buf, read, contentLength - read);
            if (n <= 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    static void WriteJson(NetworkStream stream, int code, string json)
    {
        string reason = code == 200 ? "OK" : code == 404 ? "Not Found" : "Error";
        byte[] body = Encoding.UTF8.GetBytes(json);
        string header = $"HTTP/1.1 {code} {reason}\r\n" +
                        "Content-Type: application/json\r\n" +
                        $"Content-Length: {body.Length}\r\n" +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    // ---- Main-thread pump -------------------------------------------------

    static void EnqueueMain(Action a)
    {
        lock (_lock) { _mainThreadQueue.Enqueue(a); }
    }

    // Run fn on the main thread (next editor tick) and block the listener thread until it
    // returns. Used by endpoints that must return live data (chat_images, save). Returns
    // timeoutValue if the main thread doesn't service the queue in time.
    static T RunOnMainAndWait<T>(Func<T> fn, T timeoutValue, int timeoutMs = 5000)
    {
        using (var done = new ManualResetEventSlim(false))
        {
            T result = timeoutValue;
            EnqueueMain(() =>
            {
                try { result = fn(); }
                catch (Exception e) { Debug.LogError($"[Automation] {e}"); }
                finally { done.Set(); }
            });
            return done.Wait(timeoutMs) ? result : timeoutValue;
        }
    }

    // Parse a "key=value" newline-separated request body into a case-insensitive map.
    static Dictionary<string, string> ParseKeyValues(string body)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(body)) return d;
        foreach (var raw in body.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return d;
    }

    // index=<n|latest|empty>. "latest"/empty -> 0 (meaning newest). Otherwise the number.
    static int ParseIndex(Dictionary<string, string> kv)
    {
        if (!kv.TryGetValue("index", out string v) || string.IsNullOrWhiteSpace(v)) return 0;
        if (v.Trim().ToLowerInvariant() == "latest") return 0;
        return int.TryParse(v.Trim(), out int n) ? n : 0;
    }

    static int ParseInt(Dictionary<string, string> kv, string key, int fallback)
    {
        return kv.TryGetValue(key, out string v) && int.TryParse(v.Trim(), out int n) ? n : fallback;
    }

    static float ParseFloat(Dictionary<string, string> kv, string key, float fallback)
    {
        if (!kv.TryGetValue(key, out string v)) return fallback;
        return float.TryParse(v.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float n) ? n : fallback;
    }

    static bool ParseBool(Dictionary<string, string> kv, string key, bool fallback)
    {
        if (!kv.TryGetValue(key, out string v)) return fallback;
        string s = (v ?? "").Trim().ToLowerInvariant();
        if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
        if (s == "false" || s == "0" || s == "no" || s == "off") return false;
        return fallback;
    }

    // Minimal JSON string encoder (quotes + escapes) for paths/errors.
    static string JsonStr(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    static void OnEditorUpdate()
    {
        // Drain queued commands from the listener thread.
        while (true)
        {
            Action a = null;
            lock (_lock)
            {
                if (_mainThreadQueue.Count > 0) a = _mainThreadQueue.Dequeue();
            }
            if (a == null) break;
            try { a(); } catch (Exception e) { Debug.LogError($"[Automation] {e}"); }
        }

        bool playing = EditorApplication.isPlaying;
        bool compiling = EditorApplication.isCompiling || EditorApplication.isUpdating;

        // In the editor we own the spawn decision: ensure the play-mode driver exists once
        // we're actually playing and not still compiling.
        if (IsEnabled && playing && !compiling && !AutomationBridge.IsDriverReady)
            AutomationDriver.EnsureExists();

        PumpRebuild(playing, compiling);

        // Refresh the status snapshot for the listener thread.
        lock (_snapLock)
        {
            _snapPlaying = playing;
            _snapCompiling = compiling;
            _snapDriverReady = AutomationBridge.IsDriverReady;
            _snapIdle = AutomationBridge.IsIdle();
            _snapChatActive = AutomationBridge.IsChatActive;
            _snapStage = SessionState.GetString(kStageKey, kStageNone);
        }
    }

    // ---- Rebuild & restart state machine ----------------------------------
    //
    // Driven entirely off SessionState so it resumes correctly after the domain reloads
    // that exiting play mode and recompiling both trigger. The flow:
    //   stop    -> exit play mode, wait until fully out
    //   compile -> request a script compile, wait until done (a reload happens here)
    //   play    -> re-enter play mode, wait until the driver re-registers, then clear

    // Fires after every domain reload. A reload that lands while we're in the compile
    // stage IS the "compilation finished" signal — advance to re-entering play. Reloads
    // from other causes (play-mode exit/enter) are ignored here; PumpRebuild handles those.
    static void OnAfterAssemblyReload()
    {
        if (SessionState.GetString(kStageKey, kStageNone) == kStageCompile
            && SessionState.GetBool(kCompileRequestedKey, false))
        {
            SessionState.SetString(kStageKey, kStagePlay);
        }
    }

    static void StartRebuild()
    {
        SessionState.SetBool(kCompileRequestedKey, false);
        if (EditorApplication.isPlaying)
        {
            SessionState.SetString(kStageKey, kStageStop);
            EditorApplication.isPlaying = false;
        }
        else
        {
            SessionState.SetString(kStageKey, kStageCompile);
        }
    }

    static void PumpRebuild(bool playing, bool compiling)
    {
        string stage = SessionState.GetString(kStageKey, kStageNone);
        if (stage == kStageNone) return;

        switch (stage)
        {
            case kStageStop:
                // Wait until play mode has fully exited (a domain reload happens here).
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                    return;
                }
                SessionState.SetBool(kCompileRequestedKey, false);
                SessionState.SetString(kStageKey, kStageCompile);
                break;

            case kStageCompile:
                // Request a compile once. Advancement to the play stage happens in
                // OnAfterAssemblyReload — the post-compile domain reload is the reliable
                // "compile finished" signal (polling isCompiling races the request).
                if (!SessionState.GetBool(kCompileRequestedKey, false))
                {
                    SessionState.SetBool(kCompileRequestedKey, true);
                    SessionState.SetString(kCompileSinceKey, EditorApplication.timeSinceStartup.ToString("R"));
                    AssetDatabase.Refresh();
                    CompilationPipeline.RequestScriptCompilation();
                    return;
                }
                // Safety net for the rare case where no reload occurs: advance once enough
                // idle time has elapsed with nothing compiling.
                if (!compiling && double.TryParse(SessionState.GetString(kCompileSinceKey, "0"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double since)
                    && EditorApplication.timeSinceStartup - since > kCompileFallbackSeconds)
                {
                    SessionState.SetString(kStageKey, kStagePlay);
                }
                return;

            case kStagePlay:
                if (compiling) return;
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.isPlaying = true;
                    return;
                }
                // Done once we're playing and the driver has re-registered.
                if (EditorApplication.isPlaying && AutomationBridge.IsDriverReady)
                    SessionState.SetString(kStageKey, kStageNone);
                return;
        }
    }
}
