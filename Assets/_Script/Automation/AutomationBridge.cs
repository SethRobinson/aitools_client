using System;

// Runtime-side seam between the editor-hosted control server (AutomationController,
// which lives in the Editor assembly and survives domain reloads / play-stop-start)
// and the play-mode app. The editor assembly can reference runtime types, but runtime
// cannot reference editor types, so all shared state that both sides touch lives here
// in the runtime assembly as plain statics.
//
// Both the editor controller and the play-mode AutomationDriver run in the same
// AppDomain (in-editor) on the main thread, so these statics are read/written without
// locking. The HTTP listener thread must NOT call into here directly; it reads a cached
// snapshot maintained by the controller's editor-update tick. See AutomationController.
public static class AutomationBridge
{
    static AutomationDriver _driver;

    /// <summary>True once a play-mode AutomationDriver has registered itself.</summary>
    public static bool IsDriverReady => _driver != null;

    /// <summary>The current play-mode driver, or null when not in play mode.</summary>
    public static AutomationDriver Driver => _driver;

    public static void OnDriverAwake(AutomationDriver driver)
    {
        _driver = driver;
    }

    public static void OnDriverDestroyed(AutomationDriver driver)
    {
        if (_driver == driver)
            _driver = null;
    }

    /// <summary>
    /// True when the app is fully settled: no chat turn streaming, no pending sidecars,
    /// no queued auto-resume, the action pump drained, and no pic still rendering.
    /// </summary>
    public static bool IsIdle()
    {
        return _driver != null && _driver.IsFullyIdle();
    }

    /// <summary>True when an AI Chat panel instance exists.</summary>
    public static bool IsChatActive => AIChatPanel.IsChatActive;

    /// <summary>Open (creating if needed) the AI Chat panel. False if no driver yet.</summary>
    public static bool OpenChat()
    {
        if (_driver == null) return false;
        _driver.OpenChat();
        return true;
    }

    /// <summary>Open the unified Settings panel on a requested tab. False if no driver yet.</summary>
    public static bool OpenSettings(string tabName)
    {
        if (_driver == null) return false;
        _driver.OpenSettings(tabName);
        return true;
    }

    /// <summary>Open the advanced LLM Settings panel. False if no driver yet.</summary>
    public static bool OpenLLMSettings()
    {
        if (_driver == null) return false;
        _driver.OpenLLMSettings();
        return true;
    }

    /// <summary>Open one server's Overrides panel. False if no driver yet.</summary>
    public static bool OpenServerSettings(int serverID)
    {
        if (_driver == null) return false;
        _driver.OpenServerSettings(serverID);
        return true;
    }

    /// <summary>Send a chat message. False if no driver or no chat panel is open.</summary>
    public static bool SendChat(string text)
    {
        return _driver != null && _driver.SendChat(text);
    }

    /// <summary>Focus a TMP_InputField by hierarchy-path substring; see AutomationDriver.FocusInput.</summary>
    public static bool FocusInput(string nameSubstring, bool selectAll, out string error, out string matchedPath, out bool hasCaretGraphic)
    {
        error = "no driver";
        matchedPath = "";
        hasCaretGraphic = false;
        if (_driver == null) return false;
        return _driver.FocusInput(nameSubstring, selectAll, out error, out matchedPath, out hasCaretGraphic);
    }

    /// <summary>Import a local video file into AI Chat as a clipped Movie bubble.</summary>
    public static bool ImportChatVideo(string path, float startSeconds, float durationSeconds, double fps, bool includeAudio, out string error)
    {
        error = "no driver";
        if (_driver == null) return false;
        return _driver.ImportChatVideo(path, startSeconds, durationSeconds, fps, includeAudio, out error);
    }

    /// <summary>Run AI Chat's Compact feature (mode "summarize" or "truncate").</summary>
    public static bool CompactChat(string mode, int keepExchanges, out string error)
    {
        error = "no driver";
        if (_driver == null) return false;
        return _driver.CompactChat(mode, keepExchanges, out error);
    }

    /// <summary>JSON array describing the current chat images. "[]" if no chat panel.</summary>
    public static string ChatImagesJson()
    {
        return AIChatPanel.AutomationChatImagesJson();
    }

    /// <summary>Save a chat image to disk as PNG. index &lt;= 0 means latest.</summary>
    public static bool Save(int index, string path, out string error)
    {
        error = "no driver";
        if (_driver == null) return false;
        return _driver.Save(index, path, out error);
    }

    /// <summary>Capture the game view (full screen if w/h non-positive) to a PNG.</summary>
    public static bool Screenshot(string path, int x, int y, int w, int h)
    {
        if (_driver == null) return false;
        _driver.CaptureScreenshot(path, x, y, w, h);
        return true;
    }
}
