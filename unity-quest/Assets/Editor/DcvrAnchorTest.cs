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
