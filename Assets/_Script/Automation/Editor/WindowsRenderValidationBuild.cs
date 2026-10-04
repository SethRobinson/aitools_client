using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Fixed-purpose build/launch command. Never starts a second editor.</summary>
[InitializeOnLoad]
public static class WindowsRenderValidationBuild
{
    [Serializable] sealed class Failure { public string state = "failed"; public string error; }
    const string Pending = "RT_RenderValidation_BuildPending";
    public static readonly string ReportPath = Path.GetFullPath("build/render-validation/windows-build.json");
    static readonly string Output = Path.GetFullPath("build/urp-validation");
    static System.Diagnostics.Process _player;

    static WindowsRenderValidationBuild() { EditorApplication.update += Tick; }

    public static void Request()
    {
        if (SessionState.GetBool(Pending, false) || BuildPipeline.isBuildingPlayer) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, "{\"state\":\"queued\"}");
        SessionState.SetBool(Pending, true);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    static void Tick()
    {
        if (_player != null && _player.HasExited)
        {
            File.WriteAllText(ReportPath, "{\"state\":\"player-exited\",\"exitCode\":" + _player.ExitCode + "}");
            _player.Dispose(); _player = null;
        }
        if (!SessionState.GetBool(Pending, false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Pending, false);
        try
        {
            if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Scripts have compile errors.");
            File.WriteAllText(ReportPath, "{\"state\":\"building\"}");
            Directory.CreateDirectory(Output);
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Settings/Build Profiles/ReleaseBuildProfile.asset");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = Path.Combine(Output, "aitools_client.exe"),
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build " + report.summary.result + ": " + report.summary.totalErrors + " errors.");

            // Local validation deployment; leave the existing release build/archive alone.
            foreach (string dir in new[] { "utils", "web", "Adventure", "AIGuide", "ComfyUI", "Presets", "aichat" })
                CopyRuntimeDirectory(dir, Path.Combine(Output, dir));
            File.Copy("model_data.json", Path.Combine(Output, "model_data.json"), true);
            string fixtureDir = Path.Combine(Output, "build", "render-validation");
            Directory.CreateDirectory(fixtureDir);
            File.Copy("build/render-validation/fixture.mp4", Path.Combine(fixtureDir, "fixture.mp4"), true);
            // Empty local server config keeps these deterministic checks offline.
            File.WriteAllText(Path.Combine(Output, "config.txt"), "# Local rendering validation: no generation servers.\n");
            File.WriteAllText(ReportPath, "{\"state\":\"built\",\"buildErrors\":0,\"buildWarnings\":" + report.summary.totalWarnings + "}");
        }
        catch (Exception e)
        {
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new Failure { error = e.Message }));
            Debug.LogException(e);
        }
    }

    // Separate opt-in: the caller must explicitly authorize showing this window.
    // Hidden/minimized players skip the normal backbuffer and yield black captures.
    public static void RunVisible()
    {
        if (_player != null && !_player.HasExited) throw new InvalidOperationException("A validation player is already running.");
        if (BuildPipeline.isBuildingPlayer || SessionState.GetBool(Pending, false)) throw new InvalidOperationException("Wait for the build to finish.");
        string previousReport = Path.Combine(Output, "build", "render-validation", "player", "report.json");
        if (File.Exists(previousReport)) File.Delete(previousReport);
        _player = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Output, "aitools_client.exe"),
            WorkingDirectory = Output,
            Arguments = "-render-validation -screen-fullscreen 0 -screen-width 1587 -screen-height 738 -logFile player-validation.log",
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
        });
        File.WriteAllText(ReportPath, "{\"state\":\"player-running\",\"pid\":" + _player.Id + "}");
    }

    static void CopyRuntimeDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.GetFiles(source))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("test", StringComparison.OrdinalIgnoreCase) || name.StartsWith("local_", StringComparison.OrdinalIgnoreCase)
                || name.Contains("_cached_api") || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(path, Path.Combine(destination, name), true);
        }
        foreach (string path in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(path);
            if (name == "Unused" || name.StartsWith(".") || name == "__pycache__") continue;
            CopyRuntimeDirectory(path, Path.Combine(destination, name));
        }
    }
}
