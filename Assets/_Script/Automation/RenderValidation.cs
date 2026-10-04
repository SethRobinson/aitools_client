using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Opt-in, local rendering fixtures shared by the editor bridge and player checks.</summary>
public sealed class RenderValidation : MonoBehaviour
{
    [Serializable] public sealed class Check { public string name; public bool passed; public string detail; }
    [Serializable] public sealed class Report
    {
        public string state = "running", pipeline, colorSpace, graphicsAPI, output;
        public int width, height;
        public float averageFrameMilliseconds;
        public long allocatedMemory;
        public List<Check> checks = new List<Check>();
        public List<string> shaders = new List<string>();
    }

    static RenderValidation _running;
    static Report _last;
    readonly List<GameObject> _fixtures = new List<GameObject>();
    Report _report;
    bool _quit;
    Camera _camera;
    Vector3 _cameraPosition;
    float _cameraSize;
    Color _cameraBackground;

    public static string StatusJson() => _last == null ? "{\"state\":\"idle\"}" : JsonUtility.ToJson(_last);

    public static bool Begin(string label, bool quit = false)
    {
        if (_running != null || !Application.isPlaying || ImageGenerator.Get() == null) return false;
        if (string.IsNullOrEmpty(label) || label.Any(c => !char.IsLetterOrDigit(c) && c != '-')) return false;
        // A fresh play session avoids deleting or changing the user's existing pictures.
        if (FindObjectsByType<PicMain>(FindObjectsSortMode.None).Length != 0) return false;
        var go = new GameObject("~RenderValidation");
        _running = go.AddComponent<RenderValidation>();
        _running._quit = quit;
        _last = _running._report = new Report
        {
            output = Path.GetFullPath(Path.Combine("build", "render-validation", label)),
            pipeline = GraphicsSettings.currentRenderPipeline == null ? "Built-In" : GraphicsSettings.currentRenderPipeline.GetType().Name,
            colorSpace = QualitySettings.activeColorSpace.ToString(),
            graphicsAPI = SystemInfo.graphicsDeviceType.ToString(), width = Screen.width, height = Screen.height
        };
        Directory.CreateDirectory(_last.output);
        _running.StartCoroutine(_running.RunGuarded());
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void PlayerStartup()
    {
        if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("-render-validation")) return;
        AutomationDriver.EnsureExists();
        new GameObject("~RenderValidationStartup").AddComponent<RenderValidation>().StartCoroutine(StartPlayer());
    }

    static IEnumerator StartPlayer()
    {
        yield return new WaitForSecondsRealtime(3);
        if (!Begin("player", true)) Application.Quit(2);
    }

    IEnumerator RunGuarded()
    {
        _camera = Camera.main;
        _cameraPosition = _camera.transform.position;
        _cameraSize = _camera.orthographicSize;
        _cameraBackground = _camera.backgroundColor;
        // Flatten nested coroutines so fixture failures always produce a report.
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0)
        {
            object next = null;
            bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception e) { Record("fixture exception", false, e.ToString()); break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        _report.state = _report.checks.All(c => c.passed) ? "passed" : "failed";
        File.WriteAllText(Path.Combine(_report.output, "report.json"), JsonUtility.ToJson(_report, true));
        _camera.transform.position = _cameraPosition;
        _camera.orthographicSize = _cameraSize;
        _camera.backgroundColor = _cameraBackground;
        foreach (var fixture in _fixtures) if (fixture != null) Destroy(fixture);
        GameLogic.Get().SetToolsVisible(true);
        _running = null;
        if (_quit) Application.Quit(_report.state == "passed" ? 0 : 1);
        Destroy(gameObject);
    }

    void Record(string name, bool passed, string detail = "") => _report.checks.Add(new Check { name = name, passed = passed, detail = detail });
    void Save(Texture2D texture, string name) => File.WriteAllBytes(Path.Combine(_report.output, name + ".png"), texture.EncodeToPNG());

    static Texture2D Pattern(bool alpha)
    {
        var texture = new Texture2D(512, 256, TextureFormat.RGBA32, false);
        var colors = new Color32[512 * 256];
        for (int y = 0; y < 256; y++) for (int x = 0; x < 512; x++)
        {
            var c = new Color32((byte)(x / 2), (byte)y, (byte)(x < 256 ? 48 : 208), 255);
            if (alpha) c.a = (byte)(x < 128 ? 0 : x < 256 ? 64 : x < 384 ? 128 : 255);
            if (x < 32 && y > 224) c = new Color32(255, 255, 255, 255);
            colors[y * 512 + x] = c;
        }
        texture.SetPixels32(colors); texture.Apply();
        return texture;
    }

    PicMain AddPic(Texture2D texture, Vector3 position)
    {
        var go = ImageGenerator.Get().AddImageByTexture(texture);
        _fixtures.Add(go); go.transform.position = position;
        return go.GetComponent<PicMain>();
    }

    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(0.2f);
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Save(image, name); Destroy(image);
    }

    IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(1);
        var splash = FindFirstObjectByType<StartupSplashPanel>();
        if (splash != null) Destroy(splash.gameObject);
        RTConsole.Get().transform.parent.gameObject.SetActive(false);
        var opaque = Pattern(false); var alpha = Pattern(true);
        Save(opaque, "source-opaque"); Save(alpha, "source-alpha");
        var first = AddPic(opaque, new Vector3(-3, 2, 0));
        var second = AddPic(alpha, new Vector3(3, 2, 0));
        yield return null;
        first.SaveFile(Path.Combine(_report.output, "saved-opaque.png"), bSaveAsPNG: true, bWriteOutTextFileToo: false);
        second.SaveFile(Path.Combine(_report.output, "saved-alpha.png"), bSaveAsPNG: true, bWriteOutTextFileToo: false);
        CheckSaved(opaque, "saved-opaque"); CheckSaved(alpha, "saved-alpha");
        var thumb = alpha.Duplicate(); ResizeTool.Resize(thumb, 128, 64, false); Save(thumb, "thumbnail"); Destroy(thumb);

        var font = AIGuideManager.Get().GetFontByID(0);
        var text = RTUtil.RenderTextToTexture2D("Color <color=#40C080>alpha</color>\nSecond line", 512, 256, font, 400,
            new Color(0.9f, 0.6f, 0.2f, 1), false, Vector2.one, FontStyles.Bold, TextAlignmentOptions.Left, true);
        Save(text, "text-left"); CheckText(text, "text-left");
        var third = AddPic(text, new Vector3(-3, -2, 0));
        var auto = RTUtil.RenderTextToTexture2D("Centered wrapped caption with auto sizing", 512, 256, font, 500,
            Color.white, true, Vector2.one, FontStyles.Normal, TextAlignmentOptions.Center, true, 500, 180);
        Save(auto, "text-auto"); CheckText(auto, "text-auto"); Destroy(auto);
        // Exercise the legacy overload used by image labels/Adventure/AI Guide.
        first.AddTextLabelToImage("Rendering check"); Save(first.m_pic.sprite.texture, "image-label");
        first.SetSelected(true);
        var mask = Pattern(true); second.SetMask(mask, false);
        second.GetComponent<PicMask>().SetMaskVisible(true);
        _camera.transform.position = new Vector3(0, 0, -10);
        _camera.orthographicSize = 7;
        GameLogic.Get().SetToolsVisible(false);
        yield return Capture("workspace");
        _camera.orthographicSize = 4.5f;
        yield return Capture("workspace-zoom");
        _camera.orthographicSize = 7;
        float start = Time.realtimeSinceStartup;
        for (int i = 0; i < 90; i++) yield return null;
        _report.averageFrameMilliseconds = (Time.realtimeSinceStartup - start) * 1000 / 90;
        _report.allocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();

        string video = Path.GetFullPath(Path.Combine("build", "render-validation", "fixture.mp4"));
        Record("video fixture exists", File.Exists(video));
        if (File.Exists(video))
        {
            // PicMovie owns its source and may delete it when its Pic is destroyed.
            string movieSource = Path.Combine(_report.output, "movie-source.mp4");
            File.Copy(video, movieSource, true);
            var moviePic = AddPic(Pattern(false), new Vector3(3, -2, 0));
            yield return null;
            var movie = moviePic.GetComponent<PicMovie>(); movie.PlayMovie(movieSource, true);
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!movie._videoPlayer.isPrepared || movie._videoPlayer.time < 0.3) && Time.realtimeSinceStartup < deadline) yield return null;
            Record("movie prepared", movie._videoPlayer.isPrepared, movie.GetPlaybackDebugJson());
            double before = movie._videoPlayer.time;
            yield return new WaitForSecondsRealtime(0.5f);
            Record("movie advances", movie._videoPlayer.time > before, movie.GetPlaybackDebugJson());
            yield return Capture("movie");
            movie.PauseIfPlaying();
            yield return new WaitForSecondsRealtime(0.3f);
            before = movie._videoPlayer.time;
            yield return new WaitForSecondsRealtime(0.3f);
            Record("movie pauses", Math.Abs(movie._videoPlayer.time - before) < 0.1, movie.GetPlaybackDebugJson());
            var bar = movie.GetComponentsInChildren<EventTrigger>().FirstOrDefault(t => t.name == "ProgressBg");
            Record("movie seek control", bar != null);
            if (bar != null)
            {
                var rect = (RectTransform)bar.transform;
                var point = rect.TransformPoint(new Vector3(rect.rect.xMin + rect.rect.width * 0.5f, rect.rect.center.y, 0));
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(_camera, point) };
                ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                yield return new WaitForSecondsRealtime(2);
                Record("movie seeks", Math.Abs(movie._videoPlayer.time - movie._videoPlayer.length * 0.5) < 0.4, movie.GetPlaybackDebugJson());
                movie.TogglePlay();
                before = movie._videoPlayer.time;
                yield return new WaitForSecondsRealtime(0.6f);
                Record("movie resumes after seek", movie._videoPlayer.time > before, movie.GetPlaybackDebugJson());
            }
            movie.PauseIfPlaying();
            moviePic.OnExportMovieClipButton();
            yield return new WaitForSecondsRealtime(2);
            yield return Capture("clip-chooser");
            var chooser = FindFirstObjectByType<AITools.AIChat.Video.ChatVideoClipChooser>();
            Record("clip chooser opens", chooser != null);
            if (chooser != null) Destroy(chooser.gameObject);
        }

        GameLogic.Get().SetToolsVisible(true);
        AIChatPanel.Show();
        yield return null;
        var input = FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).FirstOrDefault(f => f.transform.root.name.Contains("AIChat"));
        if (input != null)
        {
            input.text = "Rendering check: 日本語 and Latin text\nSecond line for clipping and selection";
            input.Select(); input.ActivateInputField(); input.selectionAnchorPosition = 0; input.selectionFocusPosition = input.text.Length;
        }
        yield return Capture("chat");
        if (input != null) input.text = "";
        AIChatPanel.Hide();
        AppSettingsPanel.Show(); yield return Capture("settings"); AppSettingsPanel.Hide();
        AdventureLogic.Get().OnStartGameMode(); yield return Capture("adventure"); AdventureLogic.Get().OnEndGameMode();
        AIGuideManager.Get().ShowWindow(); yield return Capture("ai-guide"); AIGuideManager.Get().HideWindow();
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            foreach (var material in renderer.sharedMaterials)
                if (material != null)
                {
                    string shader = material.shader == null ? "MISSING" : material.shader.name;
                    if (!_report.shaders.Contains(shader)) _report.shaders.Add(shader);
                    Record("material " + renderer.name, material.shader != null && material.shader.isSupported && shader != "Hidden/InternalErrorShader", shader);
                }
    }

    void CheckSaved(Texture2D source, string name)
    {
        var loaded = new Texture2D(2, 2); loaded.LoadImage(File.ReadAllBytes(Path.Combine(_report.output, name + ".png")));
        Record(name + " exact pixels", source.width == loaded.width && source.height == loaded.height && source.GetPixels32().SequenceEqual(loaded.GetPixels32()));
        Destroy(loaded);
    }

    void CheckText(Texture2D texture, string name)
    {
        var pixels = texture.GetPixels32();
        int visible = pixels.Count(p => p.a > 0), clear = pixels.Count(p => p.a == 0);
        Record(name + " visible with transparent background", visible > 100 && clear > pixels.Length / 2, "visible=" + visible + ", clear=" + clear);
    }
}
