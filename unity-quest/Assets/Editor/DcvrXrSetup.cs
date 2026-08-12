// DreamCodeVR+ — immersive XR configuration, done in code.
//
// Turns the project from a flat Android panel app into a real stereo Quest 3 build:
// OpenXR loader on Android, Meta Quest Support enabled, Touch Plus controllers bound,
// single-pass instanced stereo (the cheap path on a tile-based mobile GPU).
//
// XR settings normally live in ScriptableObjects created by the XR Plug-in Management
// GUI. Creating them here keeps the whole device configuration reproducible from a
// clean checkout and reviewable in source, which is the same reason DcvrBuild.cs owns
// the player settings.

using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

public static class DcvrXrSetup
{
    private const string XrDir = "Assets/XR";
    private const string SettingsAsset = XrDir + "/XRGeneralSettings.asset";
    private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    [MenuItem("DreamCodeVR+/Configure XR for Quest 3")]
    public static void ConfigureXr()
    {
        Directory.CreateDirectory(XrDir);

        if (!EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsAsset);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, SettingsAsset);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        XRGeneralSettings settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (settings == null || settings.Manager == null)
        {
            Debug.LogError("[DcvrXrSetup] could not create XRGeneralSettings for Android");
            EditorApplication.Exit(1);
            return;
        }

        settings.InitManagerOnStart = true;

        if (!XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXrLoaderType,
                                                 BuildTargetGroup.Android))
        {
            Debug.LogError("[DcvrXrSetup] failed to assign the OpenXR loader");
            EditorApplication.Exit(1);
            return;
        }

        // Features must be refreshed before GetFeature can see them.
        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        OpenXRSettings openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openxr == null)
        {
            Debug.LogError("[DcvrXrSetup] no OpenXRSettings for Android");
            EditorApplication.Exit(1);
            return;
        }

        Enable<MetaQuestFeature>(openxr, "Meta Quest Support");
        Enable<MetaQuestTouchPlusControllerProfile>(openxr, "Touch Plus controllers");

        // Stereo rendering: one draw, both eyes. The single biggest cheap win on Quest.
        openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        openxr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;

        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(openxr);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DcvrXrSetup] OK — OpenXR + Meta Quest, single-pass instanced stereo");
    }

    /// <summary>Batchmode entry point (-executeMethod DcvrXrSetup.ConfigureXrAndExit).</summary>
    public static void ConfigureXrAndExit()
    {
        ConfigureXr();
        EditorApplication.Exit(0);
    }

    private static void Enable<T>(OpenXRSettings settings, string label) where T : OpenXRFeature
    {
        T feature = settings.GetFeature<T>();
        if (feature == null)
        {
            Debug.LogError($"[DcvrXrSetup] feature missing: {label}");
            return;
        }
        feature.enabled = true;
        EditorUtility.SetDirty(feature);
        Debug.Log($"[DcvrXrSetup] enabled {label}");
    }
}
