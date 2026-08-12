// DreamCodeVR+ — project-level render setup, in code.
//
// Creates and assigns the URP asset, builds the skybox material, pins the custom
// shaders so the player build cannot strip them, and applies the Quest quality budget.
// Run once via -executeMethod; safe to re-run (everything is created only if absent).
//
// Shader stripping is the reason this exists rather than being clicked in the GUI: a
// shader referenced only by a runtime-created Material is invisible to the build
// scanner and gets dropped, which shows up as a magenta scene on the device and
// nowhere else. Always Included Shaders is the fix.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class DcvrSceneBuilder
{
    private const string ArtDir = "Assets/DreamCodeVRPlus/Art";
    private const string UrpAssetPath = ArtDir + "/DcvrUrpAsset.asset";
    private const string UrpRendererPath = ArtDir + "/DcvrUrpRenderer.asset";
    private const string SkyboxMatPath = ArtDir + "/DcvrSkybox.mat";

    private static readonly string[] CustomShaders =
    {
        "DreamCodeVRPlus/SkyGradient",
        "DreamCodeVRPlus/Holo",
        "DreamCodeVRPlus/Grid",
        "Universal Render Pipeline/Unlit",
    };

    [MenuItem("DreamCodeVR+/Set up rendering")]
    public static void SetUp()
    {
        Directory.CreateDirectory(ArtDir);
        UniversalRenderPipelineAsset urp = EnsureUrpAsset();
        EnsureSkybox();
        EnsureAlwaysIncludedShaders();
        ApplyQualityBudget(urp);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DcvrSceneBuilder] rendering configured (URP + skybox + shader pins)");
    }

    public static void SetUpAndExit()
    {
        SetUp();
        EditorApplication.Exit(0);
    }

    private static UniversalRenderPipelineAsset EnsureUrpAsset()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
        if (urp == null)
        {
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            rendererData.name = "DcvrUrpRenderer";
            AssetDatabase.CreateAsset(rendererData, UrpRendererPath);

            urp = UniversalRenderPipelineAsset.Create(rendererData);
            urp.name = "DcvrUrpAsset";
            AssetDatabase.CreateAsset(urp, UrpAssetPath);
        }

        // Quest budget: no shadow cascades, short shadow distance (nothing casts anyway),
        // 4x MSAA because on a tile-based GPU it is cheap and it is the single biggest
        // readability win for thin emissive geometry like the rings and grid lines.
        urp.msaaSampleCount = 4;
        urp.supportsHDR = false;              // mobile: HDR costs bandwidth, buys little here
        urp.shadowDistance = 20f;
        urp.renderScale = 1.0f;

        GraphicsSettings.defaultRenderPipeline = urp;
        QualitySettings.renderPipeline = urp;
        EditorUtility.SetDirty(urp);
        return urp;
    }

    private static void EnsureSkybox()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMatPath);
        if (mat == null)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/SkyGradient");
            if (s == null)
            {
                Debug.LogError("[DcvrSceneBuilder] SkyGradient shader missing");
                return;
            }
            mat = new Material(s) { name = "DcvrSkybox" };
            AssetDatabase.CreateAsset(mat, SkyboxMatPath);
        }
        mat.SetColor("_GroundColor", new Color(0.015f, 0.022f, 0.035f));
        mat.SetColor("_HorizonColor", new Color(0.045f, 0.150f, 0.250f));
        mat.SetColor("_SkyColor", new Color(0.008f, 0.015f, 0.040f));
        mat.SetColor("_GlowColor", new Color(0.070f, 0.480f, 0.660f));
        mat.SetFloat("_GlowPower", 16f);
        RenderSettings.skybox = mat;
        EditorUtility.SetDirty(mat);
    }

    /// <summary>Pin every shader only referenced from runtime-created materials.</summary>
    private static void EnsureAlwaysIncludedShaders()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[DcvrSceneBuilder] cannot open GraphicsSettings.asset");
            return;
        }

        var so = new SerializedObject(assets[0]);
        SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null) { return; }

        var present = new HashSet<string>();
        for (int i = 0; i < arr.arraySize; i++)
        {
            var sh = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (sh != null) { present.Add(sh.name); }
        }

        foreach (string name in CustomShaders)
        {
            if (present.Contains(name)) { continue; }
            Shader sh = Shader.Find(name);
            if (sh == null)
            {
                Debug.LogWarning($"[DcvrSceneBuilder] shader not found, cannot pin: {name}");
                continue;
            }
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            Debug.Log($"[DcvrSceneBuilder] pinned shader: {name}");
        }
        so.ApplyModifiedProperties();
    }

    private static void ApplyQualityBudget(UniversalRenderPipelineAsset urp)
    {
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
        QualitySettings.shadowDistance = 20f;
        QualitySettings.vSyncCount = 0;      // the XR compositor paces frames, not vsync
        QualitySettings.skinWeights = SkinWeights.TwoBones;
        QualitySettings.globalTextureMipmapLimit = 0;
        if (urp != null) { QualitySettings.renderPipeline = urp; }
    }
}
