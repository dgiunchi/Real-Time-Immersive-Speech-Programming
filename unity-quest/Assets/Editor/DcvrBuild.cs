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
    // The saved, inspectable diagnostic scene is the build scene. It is authored on
    // disk rather than conjured at runtime, so its hierarchy and transforms can be read
    // in the Editor — which is what made the flat-rig bug findable at all.
    public const string MainScenePath = "Assets/Scenes/DreamCodeVRQuest.unity";
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
            AssertXrConfigured();
            EnsureShadersIncluded();
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

    /// <summary>Force every DreamCodeVR+ shader into the build.
    ///
    /// The environment is generated at runtime, so its materials are created with
    /// Shader.Find and no material ASSET ever references them. Unity's build-time shader
    /// stripping only keeps shaders it can see referenced, so ours were dropped — and the
    /// failure is quiet: Shader.Find returns null on device and the surface renders as
    /// magenta or, worse, the feature disables itself. The comfort vignette shipped
    /// missing for exactly this reason and only a logcat warning revealed it.
    ///
    /// Registering them in Always Included Shaders makes the runtime lookup reliable.</summary>
    public static void EnsureShadersIncluded()
    {
        string[] wanted =
        {
            "DreamCodeVRPlus/Holo",
            "DreamCodeVRPlus/Grid",
            "DreamCodeVRPlus/SkyGradient",
            "DreamCodeVRPlus/Vignette",
            "DreamCodeVRPlus/Dissolve",
        };

        var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (graphics == null || graphics.Length == 0)
        {
            Debug.LogWarning("[DcvrBuild] cannot open GraphicsSettings; shaders not pinned");
            return;
        }

        var so = new UnityEditor.SerializedObject(graphics[0]);
        UnityEditor.SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
        {
            Debug.LogWarning("[DcvrBuild] m_AlwaysIncludedShaders not found; shaders not pinned");
            return;
        }

        int added = 0;
        foreach (string name in wanted)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                throw new Exception($"[DcvrBuild] shader '{name}' does not compile or is missing — " +
                                    "refusing to build a player whose visuals would be broken.");
            }

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    present = true;
                    break;
                }
            }
            if (present) { continue; }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            added++;
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"[DcvrBuild] shaders pinned into the build (+{added} newly added)");
    }

    /// <summary>Refuse to build a Quest APK that has no XR provider.
    ///
    /// This exists because the project already shipped one silently-flat build. Everything
    /// downstream still "succeeds" — the APK compiles, installs, launches, and even carries
    /// the correct VR manifest entries — so nothing fails until a human puts the headset on
    /// and finds a 2D panel. That is the most expensive possible place to discover it.
    /// Failing the build here converts an hour of headset debugging into one line of log.</summary>
    public static void AssertXrConfigured()
    {
        UnityEngine.Object obj = null;
        UnityEditor.EditorBuildSettings.TryGetConfigObject(
            "com.unity.xr.management.loader_settings", out obj);

        if (obj == null)
        {
            throw new Exception(
                "[DcvrBuild] XR Plug-in Management has no loader settings registered — " +
                "this would build a FLAT Android app, not a VR app. Configure XR for Android.");
        }

        var so = new UnityEditor.SerializedObject(obj);
        UnityEditor.SerializedProperty keys = so.FindProperty("Keys");
        UnityEditor.SerializedProperty values = so.FindProperty("Values");
        bool androidHasLoader = false;

        if (keys != null && values != null)
        {
            for (int i = 0; i < keys.arraySize && i < values.arraySize; i++)
            {
                // BuildTargetGroup.Android == 7
                if (keys.GetArrayElementAtIndex(i).intValue != 7) { continue; }
                var entry = values.GetArrayElementAtIndex(i).objectReferenceValue;
                if (entry == null) { continue; }
                var entrySo = new UnityEditor.SerializedObject(entry);
                var mgr = entrySo.FindProperty("m_LoaderManagerInstance");
                if (mgr?.objectReferenceValue == null) { continue; }
                var mgrSo = new UnityEditor.SerializedObject(mgr.objectReferenceValue);
                var loaders = mgrSo.FindProperty("m_Loaders");
                androidHasLoader = loaders != null && loaders.arraySize > 0;
                var initOnStart = entrySo.FindProperty("m_InitManagerOnStart");
                if (initOnStart != null && !initOnStart.boolValue)
                {
                    throw new Exception(
                        "[DcvrBuild] XR 'Initialize XR on Startup' is OFF for Android — " +
                        "the app would launch flat.");
                }
            }
        }

        if (!androidHasLoader)
        {
            throw new Exception(
                "[DcvrBuild] no XR loader assigned for Android (OpenXR missing) — " +
                "this would build a FLAT Android app, not a VR app.");
        }

        Debug.Log("[DcvrBuild] XR check OK — Android has an XR loader and initialises on startup");
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
