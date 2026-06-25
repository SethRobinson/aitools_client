using UnityEngine;

// Play-mode side of the automation harness. It registers itself with AutomationBridge so
// the editor-hosted control server can drive the running app, and (in later slices) it
// exposes the high-level actions: send a chat turn, query "fully idle", list chat images,
// and save a generated image to disk.
//
// Spawn rules:
//   - In the editor, AutomationController creates this on entering play mode, but only
//     when the control server is enabled (the enable decision lives editor-side).
//   - In a standalone build, it self-spawns at startup when "-enable_automation" is on
//     the command line (RTUtil.DoesCommandLineWordExist).
//
// It is off by default in both cases: no driver, no control surface.
public class AutomationDriver : MonoBehaviour
{
    static AutomationDriver _instance;
    public static AutomationDriver Instance => _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSpawnForStandalone()
    {
        // In the editor the controller owns the spawn decision (it knows whether the
        // control server is enabled). Standalone builds opt in via the command line.
        if (Application.isEditor) return;
        if (RTUtil.DoesCommandLineWordExist("-enable_automation"))
            EnsureExists();
    }

    /// <summary>Create the driver if it does not already exist. Safe to call repeatedly.</summary>
    public static void EnsureExists()
    {
        if (_instance != null) return;
        var go = new GameObject("~AutomationDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<AutomationDriver>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        AutomationBridge.OnDriverAwake(this);
        Debug.Log("[Automation] Driver ready.");
    }

    void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            AutomationBridge.OnDriverDestroyed(this);
        }
    }

    /// <summary>
    /// Whether the app is fully settled and safe to issue the next automated step. When a
    /// chat panel is live this reflects its streaming/sidecar/inspection/action-pump state
    /// plus any chat-generated Pic still rendering; with no chat panel, nothing chat-related
    /// is in flight so we report idle.
    /// </summary>
    public bool IsFullyIdle()
    {
        if (AIChatPanel.AutomationGetIdle(out bool idle))
            return idle;
        return true;
    }

    /// <summary>Open (creating if needed) the AI Chat panel.</summary>
    public void OpenChat()
    {
        AIChatPanel.Show();
    }

    /// <summary>Open the unified Settings panel on the requested tab.</summary>
    public void OpenSettings(string tabName)
    {
        AppSettingsTab tab = AppSettingsTab.General;
        string key = (tabName ?? "").Trim().ToLowerInvariant();
        if (key == "configuration" || key == "config" || key == "servers" || key == "comfyui" || key == "comfyui settings")
            tab = AppSettingsTab.Configuration;
        else if (key == "audio" || key == "tts" || key == "speech")
            tab = AppSettingsTab.Audio;
        else if (key == "llm" || key == "llms")
            tab = AppSettingsTab.LLM;

        AppSettingsPanel.Show(tab);
    }

    /// <summary>Open the advanced LLM Settings panel.</summary>
    public void OpenLLMSettings()
    {
        LLMSettingsPanel.Show();
    }

    /// <summary>Open one server's Overrides panel.</summary>
    public void OpenServerSettings(int serverID)
    {
        ServerSettingsPanel.Show(Mathf.Max(0, serverID));
    }

    /// <summary>Send a chat message through the live panel. False if no panel is open.</summary>
    public bool SendChat(string text)
    {
        return AIChatPanel.AutomationSend(text);
    }

    /// <summary>Save a chat image to disk as PNG. index &lt;= 0 means latest.</summary>
    public bool Save(int index, string path, out string error)
    {
        return AIChatPanel.AutomationSave(index, path, out error);
    }

    /// <summary>
    /// Capture the game view to a PNG. A non-positive width or height captures the full
    /// screen; otherwise (x,y,w,h) is a top-left-origin pixel region. Runs as a coroutine
    /// because a valid screen grab must wait for end-of-frame; the file appears ~1 frame later.
    /// </summary>
    public void CaptureScreenshot(string path, int x, int y, int w, int h)
    {
        StartCoroutine(CaptureRoutine(path, x, y, w, h));
    }

    System.Collections.IEnumerator CaptureRoutine(string path, int x, int y, int w, int h)
    {
        yield return new WaitForEndOfFrame();

        Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
        Texture2D cropped = null;
        try
        {
            Texture2D outTex = full;
            if (w > 0 && h > 0)
            {
                int sw = full.width, sh = full.height;
                int cx = Mathf.Clamp(x, 0, Mathf.Max(0, sw - 1));
                int cyTop = Mathf.Clamp(y, 0, Mathf.Max(0, sh - 1));
                int cw = Mathf.Clamp(w, 1, sw - cx);
                int ch = Mathf.Clamp(h, 1, sh - cyTop);
                // CaptureScreenshotAsTexture has bottom-left origin; flip the top-left y.
                int cyBottom = sh - (cyTop + ch);
                Color[] pixels = full.GetPixels(cx, cyBottom, cw, ch);
                cropped = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                cropped.SetPixels(pixels);
                cropped.Apply();
                outTex = cropped;
            }

            byte[] png = outTex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);
            Debug.Log($"[Automation] Screenshot saved: {path} ({outTex.width}x{outTex.height})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Automation] Screenshot failed: {e.Message}");
        }
        finally
        {
            if (cropped != null) Destroy(cropped);
            Destroy(full);
        }
    }
}
