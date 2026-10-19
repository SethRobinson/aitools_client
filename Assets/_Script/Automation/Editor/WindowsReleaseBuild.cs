using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds the release through the already-open editor, without desktop input.</summary>
[InitializeOnLoad]
public static class WindowsReleaseBuild
{
    const string Pending = "AI_Tools_ReleaseBuild";
    public static readonly string ReportPath = Path.GetFullPath("build/release-build.json");
    public static bool IsPending => SessionState.GetString(Pending, "") != "";
    [Serializable] sealed class Report { public string id, state, error; public int errors, warnings; }
    static WindowsReleaseBuild() { EditorApplication.update += Tick; }

    public static void Request(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Expected a build request GUID.");
        if (SessionState.GetString(Pending, "") != "" || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("A build is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before building a release.");
        RequireReleaseProfile();
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the modified scene before building a release.");
        Write(new Report { id = id, state = "queued" });
        SessionState.SetString(Pending, id);
        AssetDatabase.Refresh();
        CompilationPipeline.RequestScriptCompilation();
    }
    static void Tick()
    {
        string id = SessionState.GetString(Pending, "");
        if (id == "" || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.EraseString(Pending);
        var report = new Report { id = id, state = "building" };
        try
        {
            if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Scripts have compile errors.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Editor is busy.");
            Write(report);
            var profile = RequireReleaseProfile();
            var scenes = profile.overrideGlobalScenes ? profile.scenes : EditorBuildSettings.scenes;
            // Per-build defines avoid editing/re-saving the active profile, which can
            // fail on a transient file lock and leave the editor waiting on a modal.
            BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = Path.GetFullPath("build/win/aitools_client.exe"),
                extraScriptingDefines = new[] { "RT_RELEASE" },
                options = BuildOptions.None
            });
            report.errors = (int)result.summary.totalErrors;
            report.warnings = (int)result.summary.totalWarnings;
            if (result.summary.result != BuildResult.Succeeded) throw new Exception("Release build " + result.summary.result);
            report.state = "built";
        }
        catch (Exception ex) { report.state = "failed"; report.error = ex.Message; Debug.LogException(ex); }
        Write(report);
    }
    static BuildProfile RequireReleaseProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Settings/Build Profiles/ReleaseBuildProfile.asset");
        if (profile == null || BuildProfile.GetActiveBuildProfile() != profile)
            throw new InvalidOperationException("Make ReleaseBuildProfile active before using BuildWin64.bat --editor.");
        return profile;
    }
    static void Write(Report report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
}
