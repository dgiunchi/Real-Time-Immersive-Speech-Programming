// DreamCodeVR+ — generate and SAVE a real scene asset, then PROVE it is spatial.
//
// The world used to be created entirely at runtime, so there was nothing to open and
// inspect: no hierarchy to read, no transforms to check, no way to see whether the world
// root was independent of the player. Everything had to be inferred from device logs, and
// that is how a broken rig survived several builds.
//
// This writes Assets/Scenes/DreamCodeVRQuest.unity as genuine GameObjects, then renders
// from three offset camera positions and MEASURES the parallax between a near object and
// a far one. Three screenshots nobody diffs prove nothing, so the ratio is asserted and
// the run fails if the scene behaves like a flat backdrop.
//
//   Unity -batchmode -quit -projectPath unity-quest \
//         -executeMethod DcvrQuestScene.GenerateAndVerify -logFile -

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class DcvrQuestScene
{
    public const string ScenePath = "Assets/Scenes/DreamCodeVRQuest.unity";
    private const string OutDir = "LookDev/spatial";
    private const int Width = 1100;
    private const int Height = 620;

    public static void GenerateAndVerify()
    {
        try
        {
            Generate();
            bool ok = VerifyParallax();
            EditorApplication.Exit(ok ? 0 : 2);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DcvrScene] " + e);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("DreamCodeVR+/Generate diagnostic Quest scene")]
    public static void Generate()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // WORLD — scene root, independent of the player.
        DreamCodeVRPlus.DcvrTestWorld.Build();

        // PLAYER — a plain camera in the saved scene. The XR rig (XR Origin +
        // TrackedPoseDriver) is assembled at runtime by DcvrXrRig.
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.07f, 0.11f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        camGo.transform.position = new Vector3(0f, 1.7f, 0f);

        var managers = new GameObject("Managers");
        managers.AddComponent<DreamCodeVRPlus.DcvrBootstrap>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log("[DcvrScene] saved " + ScenePath);
        DumpHierarchy(scene);
    }

    private static void DumpHierarchy(Scene scene)
    {
        Debug.Log("[DcvrScene] ---- HIERARCHY ----");
        foreach (GameObject root in scene.GetRootGameObjects()) { Print(root.transform, 0); }
        Debug.Log("[DcvrScene] ---- END HIERARCHY ----");
    }

    private static void Print(Transform t, int depth)
    {
        if (t.name.StartsWith("Tile_")) { return; }   // 576 floor tiles would be noise
        Vector3 p = t.position;
        Debug.Log($"[DcvrScene] {new string(' ', depth * 2)}{t.name}  world=({p.x:F2},{p.y:F2},{p.z:F2})");
        if (depth > 3) { return; }
        for (int i = 0; i < t.childCount; i++) { Print(t.GetChild(i), depth + 1); }
    }

    /// <summary>Render from three positions and MEASURE whether a near object moves more on
    /// screen than a far one. That ratio is what separates a 3D scene from a backdrop.</summary>
    public static bool VerifyParallax()
    {
        Directory.CreateDirectory(OutDir);
        Camera cam = Object.FindAnyObjectByType<Camera>();
        Transform near = GameObject.Find("NearCube_Yellow")?.transform;
        Transform far = GameObject.Find("Tower_Blue")?.transform;
        if (cam == null || near == null || far == null)
        {
            Debug.LogError("[DcvrScene] camera or parallax reference objects missing");
            return false;
        }

        var shots = new (string name, Vector3 pos)[]
        {
            ("A-start", new Vector3(0f, 1.7f, 0f)),
            ("B-left1m", new Vector3(-1f, 1.7f, 0f)),
            ("C-fwd2m-right1m", new Vector3(1f, 1.7f, 2f)),
        };

        var nearS = new Vector3[shots.Length];
        var farS = new Vector3[shots.Length];

        for (int i = 0; i < shots.Length; i++)
        {
            cam.transform.position = shots[i].pos;
            cam.transform.rotation = Quaternion.identity;
            nearS[i] = cam.WorldToScreenPoint(near.position);
            farS[i] = cam.WorldToScreenPoint(far.position);
            Capture(cam, Path.Combine(OutDir, shots[i].name + ".png"));
            Debug.Log($"[DcvrScene] {shots[i].name}: nearX={nearS[i].x:F0} farX={farS[i].x:F0}");
        }

        // Report what the renderers actually hold, so a colour problem is diagnosed from
        // data rather than from squinting at a PNG.
        foreach (string n in new[] { "NearCube_Yellow", "Pillar_Red", "Tower_Blue", "Platform" })
        {
            var go = GameObject.Find(n);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { Debug.Log($"[DcvrScene] colour {n}: NO RENDERER"); continue; }
            Material m = r.sharedMaterial;
            Debug.Log($"[DcvrScene] colour {n}: mat={(m != null ? m.name : "NULL")} " +
                      $"shader={(m != null && m.shader != null ? m.shader.name : "NULL")} " +
                      $"baseColor={(m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "n/a")}");
        }

        float nearShift = Mathf.Abs(nearS[1].x - nearS[0].x);
        float farShift = Mathf.Abs(farS[1].x - farS[0].x);
        Debug.Log($"[DcvrScene] PARALLAX over a 1 m sidestep: near(z={near.position.z:F1}) " +
                  $"moved {nearShift:F0}px, far(z={far.position.z:F1}) moved {farShift:F0}px");

        if (nearShift < 20f)
        {
            Debug.LogError("[DcvrScene] FAIL: near object barely moved — camera is not translating.");
            return false;
        }
        if (nearShift <= farShift * 2f)
        {
            Debug.LogError($"[DcvrScene] FAIL: near/far ratio {nearShift / Mathf.Max(farShift, 1f):F2} " +
                           "— everything moves together like a flat backdrop.");
            return false;
        }
        Debug.Log($"[DcvrScene] PARALLAX OK — ratio {nearShift / Mathf.Max(farShift, 1f):F1}x; scene is spatial");
        return true;
    }

    private static void Capture(Camera cam, string path)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        RenderTexture prev = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            // Camera.Render() is a built-in-pipeline call. Under URP it does not run the
            // pipeline and produces a garbage frame — which is why the first spatial
            // renders came out uniformly green while the scene data was perfectly correct.
            // Submitting a render request drives the actual pipeline.
            if (RenderPipelineManager.currentPipeline != null)
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, req);
            }
            else
            {
                cam.Render();   // built-in pipeline fallback
            }
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prev;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
