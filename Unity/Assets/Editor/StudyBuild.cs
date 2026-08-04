using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line build entry point for the study APK.
///
/// WHY THIS EXISTS
/// Every C# change in this project has to reach the headset before it does
/// anything, and the only route is a full IL2CPP rebuild. Doing that through the
/// Editor GUI means remembering the scene list, the target, and the output path
/// each time — and getting one of them wrong produces an APK that installs and
/// then behaves like the old one, which is the most expensive kind of mistake
/// here because it looks like a code bug.
///
/// Run it headless:
///
///   /Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
///     -batchmode -quit -nographics -projectPath Unity \
///     -buildTarget Android -executeMethod StudyBuild.BuildApk \
///     -logFile /tmp/build.log
///
/// The APK lands in Unity/Builds/DreamCodeVR-study.apk. Install with:
///
///   adb install -r Unity/Builds/DreamCodeVR-study.apk
/// </summary>
public static class StudyBuild
{
    private const string OutputDir  = "Builds";
    private const string ApkName    = "DreamCodeVR-study.apk";

    [MenuItem("Study/Build Quest APK")]
    public static void BuildApk()
    {
        var scenes = EnabledScenes();
        if (scenes.Length == 0)
        {
            Fail("No scenes are enabled in Build Settings — the APK would open on " +
                 "an empty scene. Add the study scene and enable it.");
            return;
        }

        Directory.CreateDirectory(OutputDir);
        var apkPath = Path.Combine(OutputDir, ApkName);

        // Quest is arm64-only; leaving ARMv7 on doubles build time and produces
        // a slice the headset will not load.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        Log($"Building {scenes.Length} scene(s) → {apkPath}");
        foreach (var s in scenes) Log($"  · {s}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = apkPath,
            target           = BuildTarget.Android,
            targetGroup      = BuildTargetGroup.Android,
            options          = BuildOptions.None
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Log($"BUILD OK — {apkPath} ({summary.totalSize / (1024 * 1024)} MB, " +
                $"{summary.totalTime.TotalMinutes:F1} min)");
            Log($"Install with:  adb install -r {apkPath}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        else
        {
            // Errors are already in the log above; this is the line a script
            // greps for, and the non-zero exit is what stops a CI step from
            // reporting success on a failed build.
            Fail($"BUILD FAILED — {summary.result}, {summary.totalErrors} error(s)");
        }
    }

    private static string[] EnabledScenes() =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    private static void Log(string msg)  => Debug.Log($"[StudyBuild] {msg}");

    private static void Fail(string msg)
    {
        Debug.LogError($"[StudyBuild] {msg}");
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
