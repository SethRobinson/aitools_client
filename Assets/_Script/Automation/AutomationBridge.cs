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

    /// <summary>Send a chat message. False if no driver or no chat panel is open.</summary>
    public static bool SendChat(string text)
    {
        return _driver != null && _driver.SendChat(text);
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
