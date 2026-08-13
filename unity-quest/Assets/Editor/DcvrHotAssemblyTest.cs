// DreamCodeVR+ — offline proof that server-compiled C# really executes.
//
// The on-device claim is "arbitrary validated C# runs on a Quest 3". Believing that from
// a successful APK build would be the same mistake this project already made once, when a
// stereo swapchain line from the Horizon shell was recorded as ours. So the interpreter is
// exercised here, in the Editor, against a REAL assembly emitted by the Roslyn service —
// same bytes the headset receives — before any APK is built.
//
//   dotnet run --project services/roslyn-analyzer   # then POST /compile, save the base64
//   Unity -batchmode -quit -projectPath unity-quest \
//         -executeMethod DcvrHotAssemblyTest.Run -dcvrAssembly /tmp/spinner.b64
//
// The Editor cannot fire Awake/Start outside play mode, so this drives the two lifecycle
// callbacks through the AppDomain directly. That is the part actually under test: whether
// interpreted IL can call into UnityEngine and mutate a real GameObject. Unity's own
// callback timing is not in doubt and is verified on device.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DreamCodeVRPlus;
using ILRuntime.CLR.Method;

public static class DcvrHotAssemblyTest
{
    public static void Run()
    {
        string path = ArgValue("-dcvrAssembly");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Fail($"no assembly at '{path}' — pass -dcvrAssembly <file containing base64>");
            return;
        }

        string b64 = File.ReadAllText(path).Trim();
        Debug.Log($"[HotTest] loaded {b64.Length} base64 chars from {path}");

        var target = new GameObject("HotTestTarget");
        target.transform.localScale = Vector3.one;

        var host = new GameObject("DCVR_HotAssembly_Test");
        var loader = host.AddComponent<DcvrHotAssembly>();

        if (!loader.LoadAndRun(b64, target, out string error))
        {
            Fail("LoadAndRun refused: " + error);
            return;
        }

        var adaptor = target.GetComponent<DcvrMonoBehaviourAdapter.Adaptor>();
        if (adaptor == null) { Fail("no adaptor component was attached"); return; }
        if (adaptor.ILInstance == null) { Fail("adaptor has no interpreted instance"); return; }
        if (adaptor.AppDomain == null) { Fail("adaptor has no AppDomain"); return; }

        string typeName = adaptor.ILInstance.Type.FullName;
        Debug.Log($"[HotTest] attached interpreted type: {typeName}");

        // --- the real test: does interpreted IL reach UnityEngine? -----------------
        // Assert on ANY observable effect, not on one specific mutation. A fixture that
        // builds a hierarchy (a solar system) touches none of its own transform, and a
        // fixture that recolours touches only the renderer — an assertion tied to one of
        // them reports a working interpreter as broken, which is the more expensive error
        // here because it sends you hunting through ILRuntime instead of the test.
        Vector3 scaleBefore = target.transform.localScale;
        string nameBefore = target.name;
        int childrenBefore = target.transform.childCount;

        IMethod start = adaptor.ILInstance.Type.GetMethod("Start", 0, true);
        if (start == null) { Fail("interpreted type has no Start()"); return; }
        adaptor.AppDomain.Invoke(start, adaptor.ILInstance);

        bool scaleChanged = target.transform.localScale != scaleBefore;
        bool nameChanged = target.name != nameBefore;
        bool built = target.transform.childCount != childrenBefore;

        Debug.Log($"[HotTest] scale {scaleBefore} -> {target.transform.localScale}; "
                  + $"name '{nameBefore}' -> '{target.name}'; "
                  + $"children {childrenBefore} -> {target.transform.childCount}");
        DumpHierarchy(target.transform, 1);

        if (!scaleChanged && !nameChanged && !built)
        {
            Fail("interpreted Start() ran but nothing observable changed — "
                 + "the script's Unity calls are not reaching the engine");
            return;
        }

        // Update() as well: a rotation is the common case for generated behaviour, and it
        // is the one that runs every frame, so a failure here would be a per-frame failure.
        //
        // Use a FRAME-INDEPENDENT fixture for this (rotate by a constant, not by
        // Time.deltaTime). Outside play mode deltaTime is 0, so a deltaTime-scaled rotation
        // produces no movement and the test cannot tell "the interpreter is broken" from
        // "the Editor isn't running frames" — an assertion that can't fail meaningfully is
        // worse than no assertion.
        IMethod update = adaptor.ILInstance.Type.GetMethod("Update", 0, true);
        if (update == null)
        {
            // Not a failure. A scene-BUILDING script has no reason to run every frame — it
            // creates its objects in Start and is done, which is the most common shape of
            // generated program. Requiring Update made this test reject exactly the case
            // it exists to protect. Start has already proved the interpreter reaches the
            // engine; there is simply nothing further to drive.
            Debug.Log("[HotTest] no Update() — build-only script, nothing further to drive");
            loader.ClearAll();
            Debug.Log("[HotTest] PASS — server-compiled C# was interpreted and drove the engine");
            EditorApplication.Exit(0);
            return;
        }

        // Measure the whole subtree, for the same reason: a scene-building script animates
        // its children, not itself.
        Quaternion[] rotBefore = SubtreeRotations(target.transform);
        for (int i = 0; i < 5; i++)
        {
            adaptor.AppDomain.Invoke(update, adaptor.ILInstance);
        }
        Quaternion[] rotAfter = SubtreeRotations(target.transform);

        float moved = 0f;
        for (int i = 0; i < rotBefore.Length && i < rotAfter.Length; i++)
        {
            moved = Mathf.Max(moved, Quaternion.Angle(rotBefore[i], rotAfter[i]));
        }
        Debug.Log($"[HotTest] Update() x5: largest rotation across the subtree = {moved:F2}°");
        if (moved < 1f)
        {
            Fail("interpreted Update() moved nothing across 5 calls");
            return;
        }

        // The clear must actually stop it, not merely hide it.
        loader.ClearAll();
        if (loader.LiveScriptCount != 0) { Fail("ClearAll left live scripts"); return; }

        Debug.Log("[HotTest] PASS — server-compiled C# was interpreted and drove the engine");
        EditorApplication.Exit(0);
    }

    private static void DumpHierarchy(Transform t, int depth)
    {
        if (depth > 4) { return; }
        for (int i = 0; i < t.childCount; i++)
        {
            Transform c = t.GetChild(i);
            var r = c.GetComponent<Renderer>();
            Debug.Log($"[HotTest]   {new string(' ', depth * 2)}{c.name}"
                      + $"  pos={c.localPosition}  scale={c.localScale}"
                      + (r != null ? $"  colour={r.sharedMaterial.color}" : ""));
            DumpHierarchy(c, depth + 1);
        }
    }

    private static Quaternion[] SubtreeRotations(Transform root)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        var q = new Quaternion[all.Length];
        for (int i = 0; i < all.Length; i++) { q[i] = all[i].localRotation; }
        return q;
    }

    private static void Fail(string why)
    {
        Debug.LogError("[HotTest] FAIL — " + why);
        EditorApplication.Exit(1);
    }

    private static string ArgValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag) { return args[i + 1]; }
        }
        return null;
    }
}
