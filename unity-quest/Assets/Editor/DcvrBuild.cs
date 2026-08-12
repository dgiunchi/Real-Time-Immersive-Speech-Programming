// DreamCodeVR+ — deterministic Quest 3 build entry point.
//
// Everything the Quest build needs is configured here through supported Editor APIs
// rather than hand-edited ProjectSettings YAML, so the build is reproducible from a
// clean checkout and reviewable as source.
//
// Terminal use (see scripts/build-quest.sh):
//   Unity -batchmode -quit -projectPath <proj> -buildTarget Android \
//         -executeMethod DcvrBuild.BuildQuest -logFile -
//
// Mode C is the deployable architecture: the backend sends a bounded action plan, the
// device executes it through ActionPlanExecutor, and NO C# is compiled at runtime.
// RuntimeCSharpCompiler stays behind the DCVR_ROSLYN_ENABLED define (unset here), so
// Roslyn never enters an IL2CPP build — that path is editor/desktop research only.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class DcvrBuild
{
    public const string ApplicationId = "com.bham.dreamcodevrplus";
    public const string ProductName = "DreamCodeVR+";
    public const string CompanyName = "University of Birmingham";
    public const string MainScenePath = "Assets/Scenes/DcvrMain.unity";
    private const string OutputDir = "Builds";
    private const string ApkName = "DreamCodeVRPlus.apk";

    /// <summary>Entry point invoked by -executeMethod. Exits non-zero on failure so the
    /// shell wrapper (and CI) can react without scraping the log.</summary>
    public static void BuildQuest()
    {
        try
        {
            bool dev = HasArg("-dcvrDevelopment");
            ConfigureAndroidPlayer(dev);
            string scene = EnsureMainScene();

            Directory.CreateDirectory(OutputDir);
            string apk = Path.Combine(OutputDir, ApkName);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = dev
                    ? (BuildOptions.Development | BuildOptions.AllowDebugging)
                    : BuildOptions.None,
            };

            Debug.Log($"[DcvrBuild] building {(dev ? "DEVELOPMENT" : "RELEASE")} APK -> {apk}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;

            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DcvrBuild] SUCCESS  {s.outputPath}  " +
                          $"{s.totalSize / (1024f * 1024f):F1} MB  in {s.totalTime}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[DcvrBuild] FAILED result={s.result} errors={s.totalErrors}");
                EditorApplication.Exit(1);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DcvrBuild] EXCEPTION {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Player settings for a standalone Quest 3 (Horizon OS / Android 14, arm64).</summary>
    public static void ConfigureAndroidPlayer(bool development)
    {
        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;

        var android = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(android, ApplicationId);

        // IL2CPP + ARM64 is the only combination Quest 3 accepts. It is also precisely why
        // Mode A (runtime C# compilation) cannot ship here — stated, not worked around.
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);

        // Newtonsoft.Json resolves types reflectively; aggressive managed stripping can
        // remove them and fail only at runtime on device. Keep stripping minimal and pin
        // the assemblies explicitly in link.xml.
        PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // The client dials the backend over TCP and probes UDP for discovery.
        PlayerSettings.Android.forceInternetPermission = true;

        // RECORD_AUDIO for push-to-talk is emitted by Unity because ModeCNetworkedDemo
        // references the Microphone API. That inference is verified against the built APK
        // in scripts/build-quest.sh rather than assumed here.

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

        // APK, not AAB — this is sideloaded, never Store-submitted.
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        EditorUserBuildSettings.development = development;

        AssetDatabase.SaveAssets();
        Debug.Log($"[DcvrBuild] configured {ApplicationId} | IL2CPP | ARM64 | minSdk 32 | Linear");
    }

    /// <summary>Guarantee a buildable scene exists and is the one enabled in build settings.
    /// ModeCNetworkedDemo self-instantiates via RuntimeInitializeOnLoadMethod, so a scene
    /// with just a camera and light is enough for the functional smoke test.</summary>
    public static string EnsureMainScene()
    {
        if (File.Exists(MainScenePath))
        {
            SetBuildScenes(MainScenePath);
            return MainScenePath;
        }

        Directory.CreateDirectory("Assets/Scenes");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                  NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, MainScenePath);
        AssetDatabase.Refresh();
        SetBuildScenes(MainScenePath);
        Debug.Log($"[DcvrBuild] created {MainScenePath}");
        return MainScenePath;
    }

    private static void SetBuildScenes(string scenePath)
    {
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
    }

    private static bool HasArg(string name)
    {
        foreach (string a in Environment.GetCommandLineArgs())
        {
            if (string.Equals(a, name, StringComparison.Ordinal)) { return true; }
        }
        return false;
    }
}
