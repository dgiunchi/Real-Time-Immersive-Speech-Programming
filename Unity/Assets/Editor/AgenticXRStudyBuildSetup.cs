using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

/// <summary>
/// Creates the versioned XR configuration used by the AgenticXR study.
/// The initial study target is Windows x64 with a Quest headset over Link/Air Link.
/// </summary>
public static class AgenticXRStudyBuildSetup
{
    private const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    [MenuItem("AgenticXR/Configure Windows OpenXR Study Build")]
    public static void ConfigureWindowsOpenXR()
    {
        const BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;

        var settingsGuids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
        if (settingsGuids.Length == 0)
        {
            throw new InvalidOperationException(
                "XR Plug-in Management did not create XRGeneralSettingsPerBuildTarget.asset.");
        }

        var settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuids[0]);
        var perTargetSettings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
        if (perTargetSettings == null)
        {
            throw new InvalidOperationException($"Could not load XR settings at {settingsPath}.");
        }

        if (!perTargetSettings.HasManagerSettingsForBuildTarget(targetGroup))
        {
            perTargetSettings.CreateDefaultManagerSettingsForBuildTarget(targetGroup);
        }

        var manager = perTargetSettings.ManagerSettingsForBuildTarget(targetGroup);
        manager.automaticLoading = true;
        manager.automaticRunning = true;
        if (!XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderType, targetGroup) &&
            !XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, targetGroup))
        {
            throw new InvalidOperationException("Unity could not assign the OpenXR loader for Windows.");
        }

        // OpenXRPackageSettings is internal to Unity's OpenXR editor assembly. Calling its
        // public factory by reflection lets Unity serialize its own supported asset schema.
        var packageSettingsType = Type.GetType(
            "UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
        var getOrCreate = packageSettingsType?.GetMethod(
            "GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static);
        var packageSettings = getOrCreate?.Invoke(null, null);
        if (packageSettings == null)
        {
            throw new InvalidOperationException("Unity could not create the OpenXR package settings asset.");
        }

        var getTargetSettings = packageSettingsType.GetMethod(
            "GetSettingsForBuildTargetGroup", BindingFlags.Public | BindingFlags.Instance);
        if (getTargetSettings?.Invoke(packageSettings, new object[] { targetGroup }) == null)
        {
            throw new InvalidOperationException("Unity could not create Windows OpenXR settings.");
        }

        EditorUtility.SetDirty(perTargetSettings);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty((UnityEngine.Object)packageSettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AgenticXR] Windows OpenXR study build configured successfully.");
    }
}
