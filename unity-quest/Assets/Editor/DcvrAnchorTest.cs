// DreamCodeVR+ — proving the world does not move with the user.
//
//     THE USER MOVES. THE WORLD DOES NOT. CREATIONS DO NOT.
//
// This project has shipped three builds where that was false and the world appeared glued
// to the visor. Every time, the build succeeded, the logs looked reasonable, and the fault
// was only discoverable by wearing the headset. That is an expensive way to find out, so
// the invariant is asserted here instead — in the Editor, before an APK exists.
//
//   Unity -batchmode -quit -projectPath unity-quest \
//         -executeMethod DcvrAnchorTest.Run -logFile -
//
// It exits non-zero if anything the user made, or anything in the environment, moves when
// the player does. The test deliberately moves the RIG rather than the camera directly,
// because that is what locomotion does and it is the case that actually broke.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using DreamCodeVRPlus;

public static class DcvrAnchorTest
{
    private static int _failures;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DreamCodeVRQuest.unity", OpenSceneMode.Single);

        // A camera stands in for the headset. Under XR a TrackedPoseDriver owns this
        // transform; here we move it by hand, which is exactly what walking does to it.
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
        }
        var rig = new GameObject("XR Origin");
        cam.transform.SetParent(rig.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        rig.transform.position = Vector3.zero;

        DcvrGeneratedContent content = DcvrGeneratedContent.Ensure();
        DcvrSpatialCompositor.Ensure();

        // --- build a creation the way the runtime does -----------------------------
        GenerationGroup group = content.BeginGroup("build a small castle");
        for (int i = 0; i < 4; i++)
        {
            GameObject t = DcvrPrim.Create(PrimitiveType.Cube);
            t.name = "Tower_" + i;
            t.transform.position = new Vector3(i * 0.7f, 0.5f, 3f);
            content.Register(t, group, "tower " + i, "");
        }
        DcvrSpatialCompositor.Instance.Place(group, floating: false);

        // --- record every world transform we care about ----------------------------
        Vector3 creationBefore = WorldCenter(group);
        Vector3 camBefore = cam.transform.position;
        var envBefore = new System.Collections.Generic.Dictionary<string, Vector3>();
        foreach (string n in new[] { "DreamCodeVR_World", "DCVR_World", "NearLayer", "DepthLayers" })
        {
            GameObject go = GameObject.Find(n);
            if (go != null) { envBefore[n] = go.transform.position; }
        }

        // --- MOVE THE PLAYER, several ways -----------------------------------------
        // Stick locomotion / room-scale walking move the RIG. Leaning and crouching move
        // the camera within it. Both must leave the world alone.
        rig.transform.position += new Vector3(2.5f, 0f, -1.75f);
        rig.transform.Rotate(0f, 47f, 0f);
        cam.transform.localPosition += new Vector3(0.3f, -0.45f, 0.2f);

        Vector3 camAfter = cam.transform.position;
        Vector3 creationAfter = WorldCenter(group);

        // --- assert -----------------------------------------------------------------
        Check(Vector3.Distance(camBefore, camAfter) > 0.5f,
              $"the test must actually move the player (camera moved {Vector3.Distance(camBefore, camAfter):F3} m)");

        Check(Vector3.Distance(creationBefore, creationAfter) < 0.001f,
              $"GENERATED CONTENT MOVED WITH THE PLAYER — {creationBefore} -> {creationAfter}. "
              + "Something is parenting creations under the rig or recomputing them from the head pose.");

        foreach (var kv in envBefore)
        {
            GameObject go = GameObject.Find(kv.Key);
            if (go == null) { continue; }
            Check(Vector3.Distance(kv.Value, go.transform.position) < 0.001f,
                  $"ENVIRONMENT '{kv.Key}' MOVED WITH THE PLAYER — {kv.Value} -> {go.transform.position}");
        }

        // The content root must not be under the rig at all. Position equality could hold
        // by luck for one frame; ancestry is the structural guarantee.
        Check(!IsUnder(content.transform, rig.transform),
              "GeneratedContent is parented under the XR rig — it will inherit locomotion");
        Check(!IsUnder(content.transform, cam.transform),
              "GeneratedContent is parented under the camera — it will follow the head");

        // --- parallax: near content must shift more across the view than far content ---
        CheckParallax(cam, rig);

        // --- THE ACTUAL P0 BUG: stereo-drawable materials -----------------------------
        CheckStereoSafeMaterials();

        // --- deletion and clearing leave no stale state (§54, §55) -------------------
        GameObject victim = null;
        foreach (GameObject go in content.AllObjects) { victim = go; break; }
        int before = content.ObjectCount;
        content.DeleteObject(victim);
        Check(content.ObjectCount == before - 1,
              $"deleting one object should drop the registry count by one ({before} -> {content.ObjectCount})");

        content.BeginGroup("create a garden");
        content.ClearAll();
        Check(content.ObjectCount == 0, "ClearAll must empty the registry");
        Check(content.Groups.Count == 0, "ClearAll must empty the group list");
        Check(content.transform.childCount == 0,
              $"ClearAll must leave no children under GeneratedContent (found {content.transform.childCount})");
        Check(GameObject.Find("DreamCodeVR_World") != null,
              "ClearAll must NOT remove the environment");

        if (_failures > 0)
        {
            Debug.LogError($"[AnchorTest] FAILED — {_failures} invariant(s) violated");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("[AnchorTest] PASS — creations and environment stay put while the player moves");
        EditorApplication.Exit(0);
    }

    /// <summary>Every generated renderer must carry a material this build can draw in
    /// single-pass stereo.
    ///
    /// THIS TEST EXISTS BECAUSE THE REST OF THIS FILE PASSED WHILE THE HEADSET WAS WRONG.
    /// The wearer saw one object as two, and saw it track their head. The transform
    /// assertions above were all green and all correct — the transforms were never the
    /// problem. `GameObject.CreatePrimitive` attaches a legacy-pipeline material that is
    /// not in this URP build, so Unity substitutes `Hidden/InternalErrorShader`, which does
    /// not participate in stereo and therefore draws to the same screen position in BOTH
    /// eyes: no disparity to fuse, and an image that follows the view by construction.
    ///
    /// A test that only checks where objects ARE cannot see a fault in how they are DRAWN,
    /// which is why this asserts the material rather than the position.</summary>
    private static void CheckStereoSafeMaterials()
    {
        // Exactly the two construction routes generated content uses.
        var viaPrim = DcvrPrim.Create(PrimitiveType.Cube, "StereoCheck_Prim");
        var viaPrimitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        viaPrimitive.name = "StereoCheck_CreatePrimitive";

        Check(DcvrMaterials.IsStereoSafe(viaPrim.GetComponent<Renderer>().sharedMaterial),
              "DcvrPrim must produce a stereo-drawable material by default, got "
              + ShaderNameOf(viaPrim));

        // NOTE ON WHAT THIS CAN AND CANNOT PROVE. In the EDITOR, `CreatePrimitive` finds
        // URP's default material and is already safe, so the raw defect does not reproduce
        // here — it appears only in the stripped IL2CPP build, where the legacy shader is
        // absent and Unity substitutes the error shader. That asymmetry is exactly why the
        // bug reached a wearer. So this asserts the GUARANTEE the runtime makes (whatever
        // came in, what goes out is drawable in stereo) rather than the platform-specific
        // substitution, and `DcvrStereoProbe` reports the real shader from the device.
        bool rawWasUnsafe = !DcvrMaterials.IsStereoSafe(viaPrimitive.GetComponent<Renderer>().sharedMaterial);
        int repaired = DcvrMaterials.RepairSubtree(viaPrimitive);
        Check(DcvrMaterials.IsStereoSafe(viaPrimitive.GetComponent<Renderer>().sharedMaterial),
              "RepairSubtree must make a CreatePrimitive object stereo-drawable, got "
              + ShaderNameOf(viaPrimitive));
        Debug.Log($"[AnchorTest] CreatePrimitive default was {(rawWasUnsafe ? "UNSAFE (as expected)" : "already safe")}; "
                  + $"repaired {repaired} renderer(s) -> {ShaderNameOf(viaPrimitive)}");

        // Colour intent must survive the repair, or fixing stereo would silently grey out
        // every creation the model designed.
        var coloured = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        coloured.name = "StereoCheck_Colour";
        coloured.GetComponent<Renderer>().material.color = new Color(0.9f, 0.1f, 0.1f);
        DcvrMaterials.RepairSubtree(coloured);
        Material after = coloured.GetComponent<Renderer>().sharedMaterial;
        Color got = after.HasProperty("_BaseColor") ? after.GetColor("_BaseColor") : after.color;
        Check(got.r > got.g && got.r > got.b,
              $"the repair must preserve the generated colour; red became {got}");

        // And nothing already in the registry may be unsafe.
        DcvrGeneratedContent content = DcvrGeneratedContent.Instance;
        if (content != null)
        {
            foreach (GameObject go in content.AllObjects)
            {
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Check(DcvrMaterials.IsStereoSafe(r.sharedMaterial),
                          $"registered object '{go.name}' has a non-stereo material "
                          + $"({(r.sharedMaterial == null ? "<none>" : r.sharedMaterial.shader.name)})");
                }
            }
        }

        Object.DestroyImmediate(viaPrim);
        Object.DestroyImmediate(viaPrimitive);
        Object.DestroyImmediate(coloured);
    }

    private static string ShaderNameOf(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null) { return "<none>"; }
        return r.sharedMaterial.shader.name;
    }

    /// <summary>Near things sweep across the view faster than far things when you step
    /// sideways. If they move together, the "world" is a backdrop pinned to the camera —
    /// which is precisely how a flat build masquerades as a 3D one.</summary>
    private static void CheckParallax(Camera cam, GameObject rig)
    {
        var near = DcvrPrim.Create(PrimitiveType.Cube);
        near.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
        var far = DcvrPrim.Create(PrimitiveType.Cube);
        far.transform.position = cam.transform.position + cam.transform.forward * 30f;

        Vector3 n0 = cam.WorldToViewportPoint(near.transform.position);
        Vector3 f0 = cam.WorldToViewportPoint(far.transform.position);
        rig.transform.position += rig.transform.right * 1.0f;
        Vector3 n1 = cam.WorldToViewportPoint(near.transform.position);
        Vector3 f1 = cam.WorldToViewportPoint(far.transform.position);

        float nearShift = Mathf.Abs(n1.x - n0.x);
        float farShift = Mathf.Abs(f1.x - f0.x);
        Debug.Log($"[AnchorTest] parallax: near={nearShift:F4} far={farShift:F4} "
                  + $"ratio={(farShift > 1e-6f ? nearShift / farShift : 999f):F1}x");
        Check(nearShift > farShift * 2f,
              $"no parallax: near object shifted {nearShift:F4} vs far {farShift:F4} — "
              + "the world is not being viewed in 3D");

        Object.DestroyImmediate(near);
        Object.DestroyImmediate(far);
    }

    private static Vector3 WorldCenter(GenerationGroup g)
    {
        return g.TryGetBounds(out Bounds b) ? b.center : g.Root.position;
    }

    private static bool IsUnder(Transform t, Transform root)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p == root) { return true; }
        }
        return false;
    }

    private static void Check(bool ok, string message)
    {
        if (ok) { return; }
        Debug.LogError("[AnchorTest] " + message);
        _failures++;
    }
}
