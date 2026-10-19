using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AITools.AIChat.Video;
using AITools.ScreenCapture;
using SimpleJSON;
using UnityEngine;

/// <summary>Opt-in, offline command-line check of a freshly extracted release.</summary>
public static class PackageSmokeCheck
{
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [Serializable] sealed class Result { public string name; public bool passed; public string detail; }
    [Serializable] sealed class Report
    {
        public string state = "running", unityVersion, buildDate;
        public List<Result> checks = new List<Result>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Run()
    {
        if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("-package-smoke-check")) return;
        // BeforeSceneLoad runs before scene Awake/Start. Disable the scene roots before
        // callbacks can touch preferences, servers or hotkeys, then use Unity's normal quit.
        // The scene is not registered as active yet. Unity's object search must
        // include inactive objects here, regardless of their serialized active flag.
        GameObject[] roots = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.parent == null).Select(t => t.gameObject).ToArray();
        foreach (GameObject rootObject in roots) rootObject.SetActive(false);
        var report = new Report { unityVersion = Application.unityVersion };
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string evidence = Path.Combine(root, "package-smoke");
        int exitCode = 1;
        try
        {
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-nographics"))
                throw new InvalidOperationException("Use -batchmode -nographics -package-smoke-check.");
            SetErrorMode(0x8003); // Child failures report exit codes instead of interactive error dialogs.
            CapturePaths.AssertNoLinks(evidence);
            Directory.CreateDirectory(evidence);
            Check(report, "Unity player and managed assemblies", () =>
            {
                if (GameLogic.Get() != null) throw new Exception("Normal application startup ran before the smoke check.");
                if (IntPtr.Size != 8) throw new Exception("Expected the x64 player.");
                var stamp = Resources.Load<TextAsset>("build_date");
                if (stamp == null || string.IsNullOrWhiteSpace(stamp.text)) throw new Exception("Missing built resource: build_date.");
                report.buildDate = stamp.text.Trim();
                return "x64 Unity " + Application.unityVersion + "; build resource loaded";
            });
            Check(report, "Scene script dependencies", () =>
            {
                bool gameLogic = false;
                foreach (GameObject rootObject in roots)
                    foreach (Transform child in rootObject.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (Component component in child.GetComponents<Component>())
                        {
                            if (component == null) throw new Exception("Missing scene script on " + child.name);
                            if (component is GameLogic) gameLogic = true;
                        }
                    }
                if (!gameLogic) throw new Exception("Main scene has no GameLogic component.");
                return "Main scene loaded; GameLogic and all attached scripts resolved";
            });
            Check(report, "Shipped runtime data", () =>
            {
                foreach (string path in new[] { "model_data.json", "ComfyUI/text_to_img_qwen21.json", "ComfyUI/img_to_img_qwen21_edit.json" })
                    if (!JSON.Parse(File.ReadAllText(Path.Combine(root, path))).IsObject) throw new Exception("Invalid JSON: " + path);
                foreach (string path in new[] { "Presets/Prompt To Image (Qwen Image 2.1).txt", "aichat/main_prompt.txt", "utils/RTClip.dll", "utils/RTClip.runtimeconfig.json" })
                    if (!File.Exists(Path.Combine(root, path))) throw new FileNotFoundException("Missing runtime input: " + path);
                return "Model/workflow JSON, preset, chat prompt and clipboard files present";
            });
            Check(report, "Recorder native dependencies", () =>
            {
                var run = RunTool(root, "utils/ScreenCapture/AIToolsCapture.exe", "--check");
                var response = JSON.Parse(run.Stdout);
                if (response["type"].Value != "ready") throw new Exception("Recorder did not report ready: " + run.Stdout);
                return response["message"].Value;
            });
            Check(report, "yt-dlp executable", () => RunTool(root, "utils/yt-dlp/yt-dlp.exe", "--version").Stdout.Trim());
            string movie = Path.Combine(evidence, "synthetic.mp4");
            Check(report, "FFmpeg H.264/AAC encode", () =>
            {
                RunTool(root, "utils/ffmpeg/bin/ffmpeg.exe", "-hide_banner -nostdin -y -f lavfi -i color=c=blue:s=320x240:r=25:d=0.4 -f lavfi -i sine=frequency=440:sample_rate=48000:duration=0.4 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest \"" + movie + "\"");
                if (!File.Exists(movie) || new FileInfo(movie).Length == 0) throw new Exception("Encoder produced no video.");
                return "Synthetic 320x240, 25 FPS clip with generated audio";
            });
            Check(report, "ffprobe decode/probe", () =>
            {
                var result = RunTool(root, "utils/ffmpeg/bin/ffprobe.exe", "-v error -show_streams -show_format -of json \"" + movie + "\"");
                var probe = JSON.Parse(result.Stdout);
                var streams = probe["streams"].AsArray;
                bool video = false, audio = false;
                foreach (JSONNode stream in streams)
                {
                    if (stream["codec_name"].Value == "h264" && stream["width"].AsInt == 320 && stream["height"].AsInt == 240 && stream["avg_frame_rate"].Value == "25/1") video = true;
                    if (stream["codec_name"].Value == "aac") audio = true;
                }
                if (!video || !audio || probe["format"]["duration"].AsDouble < 0.35) throw new Exception("Unexpected synthetic movie format.");
                RunTool(root, "utils/ffmpeg/bin/ffmpeg.exe", "-hide_banner -nostdin -v error -i \"" + movie + "\" -f null -");
                return "H.264/AAC streams, dimensions, FPS and duration verified; decode succeeded";
            });
            report.state = report.checks.All(c => c.passed) ? "passed" : "failed";
            exitCode = report.state == "passed" ? 0 : 1;
        }
        catch (Exception ex) { report.state = "failed"; report.checks.Add(new Result { name = "Smoke startup", detail = ex.Message }); }
        try { File.WriteAllText(Path.Combine(evidence, "report.json"), JsonUtility.ToJson(report, true)); }
        catch (Exception ex) { Debug.LogError("Could not write smoke report: " + ex.Message); exitCode = 1; }
        Debug.Log("Package smoke check: " + report.state);
        // Environment.Exit can hang Mono teardown inside Unity. The inactive scene roots
        // let Unity quit cleanly without starting normal application services.
        Application.Quit(exitCode);
    }

    static void Check(Report report, string name, Func<string> action)
    {
        var result = new Result { name = name };
        try { result.detail = action(); result.passed = true; }
        catch (Exception ex) { result.detail = ex.Message; }
        report.checks.Add(result);
        Debug.Log("Package smoke: " + name + ": " + (result.passed ? "PASS" : "FAIL " + result.detail));
    }

    static FfmpegTool.ProcessResult RunTool(string root, string relative, string arguments)
    {
        string executable = Path.Combine(root, relative);
        if (!File.Exists(executable)) throw new FileNotFoundException("Missing executable: " + relative);
        var result = FfmpegTool.RunProcessCancellable(executable, arguments, 30000, null, null);
        if (!result.Success) throw new Exception(relative + " failed (" + result.ExitCode + "): " + result.Stderr + result.Stdout);
        return result;
    }
}
