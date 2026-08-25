using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

/// <summary>
/// Builds the study scene as a standalone Quest APK.
///
/// The study's primary target is Windows x64 with the Quest over Link, which has
/// no macOS client. This exists so the interaction layer can be exercised on a
/// real headset from a macOS host: tracking, controllers, locomotion, region
/// entry, grasp and use, and the world-space consent panel.
///
/// It is not a replacement for the Link build. Quest requires IL2CPP, which is
/// ahead-of-time, so RoslynCSharp cannot load a compiled assembly at runtime.
/// Runtime code generation is therefore expected to be unavailable in this build,
/// and any rehearsal evidence taken from it must say so.
/// </summary>
public static class AgenticXRQuestBuild
{
    private const string Tag = "[AgenticXRQuestBuild]";
    private const string ScenePath = "Assets/Scenes/AgenticXRStudy.unity";
    private const string OculusLoaderType = "Unity.XR.Oculus.OculusLoader";

    [MenuItem("AgenticXR/Build Quest APK")]
    public static void Build()
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Quest");
        Directory.CreateDirectory(outputDir);
        var apkPath = Path.Combine(outputDir, "AgenticXRStudy.apk");

        // Building for Quest switches the active build target, and the study's
        // primary target is Windows Standalone. Leaving the project on Android
        // would change what a Windows collaborator sees when they next open it,
        // so the original target is restored before returning either way.
        var originalTarget = EditorUserBuildSettings.activeBuildTarget;
        var originalGroup = BuildPipeline.GetBuildTargetGroup(originalTarget);

        try
        {
            if (!File.Exists(ScenePath))
                throw new InvalidOperationException($"{ScenePath} does not exist. Run AgenticXR/Build Study Scene first.");

            Debug.Log($"{Tag} switching active build target to Android");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity could not switch the active build target to Android.");

            // Quest is ARM64 only, and Unity has no Mono backend for ARM64 Android,
            // so IL2CPP is not a choice here.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = false;
            // Push-to-talk needs the microphone; without this the headset build is
            // silent and the failure looks like an STT problem.
            PlayerSettings.Android.androidTVCompatibility = false;
            // defaultInterfaceOrientation is deliberately not set: it is a global
            // player setting, so changing it here would alter the Windows build
            // too. The Android manifest already pins the activity to landscape.

            Debug.Log($"{Tag} assigning the Oculus XR loader for Android");
            var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (guids.Length == 0) throw new InvalidOperationException("XR Plug-in Management has no settings asset.");
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            manager.automaticLoading = true;
            manager.automaticRunning = true;
            if (!XRPackageMetadataStore.AssignLoader(manager, OculusLoaderType, BuildTargetGroup.Android) &&
                !XRPackageMetadataStore.IsLoaderAssigned(OculusLoaderType, BuildTargetGroup.Android))
                throw new InvalidOperationException("Unity could not assign the Oculus loader for Android.");
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();

            Debug.Log($"{Tag} building {apkPath}");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            var summary = report.summary;
            Debug.Log($"{Tag} result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"size={summary.totalSize} time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var message in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError($"{Tag} build error in '{step.name}': {message.content}");
                Debug.LogError($"{Tag} FAIL");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"{Tag} PASS apk={apkPath} bytes={new FileInfo(apkPath).Length}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError($"{Tag} FAIL {exception}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
        finally
        {
            if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
            {
                Debug.Log($"{Tag} restoring the active build target to {originalTarget}");
                EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalTarget);
            }
        }
    }
}
