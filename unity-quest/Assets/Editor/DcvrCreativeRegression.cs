// DreamCodeVR+ — does the spatial layer work for things nobody coded for?
//
// The point of this file is §0 and §90: the system must not be a set of demos. So the
// fixtures below are chosen to be structurally DIFFERENT from one another — one object, a
// grounded architectural composition, a scattered organic one, a floating hierarchy, an
// interior arrangement, a large representational model — rather than to be the subjects
// this project happens to talk about. Nothing in the runtime matches on any of these
// names; they exercise placement, fitting, grounding and bounds, and a new subject with
// the same shape gets the same treatment for free.
//
//   Unity -batchmode -quit -projectPath unity-quest \
//         -executeMethod DcvrCreativeRegression.Run -logFile -
//
// Every fixture must satisfy the same properties, and the run fails if any does not:
//   fits the area · sits on the floor (or deliberately floats) · does not intersect the
//   user · is reachable by name · is deletable · leaves the registry consistent.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using DreamCodeVRPlus;

public static class DcvrCreativeRegression
{
    private static int _failures;

    private struct Fixture
    {
        public string Prompt;
        public int Count;
        public DcvrLayout Layout;
        public float Spacing;
        public Vector3 Size;
        public bool Floating;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DreamCodeVRQuest.unity", OpenSceneMode.Single);
        Camera cam = EnsureCamera();

        var fixtures = new List<Fixture>
        {
            // single object
            new Fixture { Prompt = "create a blue cube", Count = 1,
                          Layout = DcvrLayout.Single, Spacing = 0f, Size = Vector3.one * 0.4f },

            // architectural, grounded, corner-based
            new Fixture { Prompt = "build a small castle with four towers", Count = 5,
                          Layout = DcvrLayout.Perimeter, Spacing = 1.6f,
                          Size = new Vector3(0.8f, 3.0f, 0.8f) },

            // organic scatter
            new Fixture { Prompt = "create a small garden with trees and benches", Count = 11,
                          Layout = DcvrLayout.Scatter, Spacing = 1.1f,
                          Size = new Vector3(0.6f, 1.8f, 0.6f) },

            // floating hierarchy at very different scales
            new Fixture { Prompt = "generate the solar system", Count = 9,
                          Layout = DcvrLayout.Orbital, Spacing = 0.85f,
                          Size = Vector3.one * 0.35f, Floating = true },

            // interior arrangement around a centre
            new Fixture { Prompt = "create a dining table with six chairs", Count = 7,
                          Layout = DcvrLayout.Radial, Spacing = 0.7f,
                          Size = new Vector3(0.5f, 0.9f, 0.5f) },

            // large representational model — must be SCALED to fit, not left enormous
            new Fixture { Prompt = "create a futuristic city model", Count = 24,
                          Layout = DcvrLayout.Grid, Spacing = 4.0f,
                          Size = new Vector3(3f, 14f, 3f) },

            // deliberately unlike anything above, and unlike anything the runtime knows
            new Fixture { Prompt = "build a maze", Count = 16,
                          Layout = DcvrLayout.Rows, Spacing = 0.9f,
                          Size = new Vector3(0.9f, 1.2f, 0.2f) },
        };

        DcvrGeneratedContent content = DcvrGeneratedContent.Ensure();
        DcvrSpatialCompositor.Ensure();

        Debug.Log("[Creative] prompt | objects | bounds | lowestY | distance | scale");
        var built = new List<GenerationGroup>();

        foreach (Fixture f in fixtures)
        {
            GenerationGroup g = Build(content, f);
            DcvrSpatialCompositor.Instance.Place(g, f.Floating);
            built.Add(g);
            Report(g, cam, f);
            CheckProperties(g, cam, f);
        }

        // Coexistence (§46): several creations must not pile onto one another.
        CheckNoBadOverlap(built);

        // Addressable by name, and deletable as a unit (§21, §27).
        CheckNamedResolution(content);

        int before = content.ObjectCount;
        GenerationGroup castle = content.ResolveGroup("castle");
        Check(castle != null, "the castle must be resolvable by name");
        if (castle != null)
        {
            int n = castle.Objects.Count;
            content.DeleteGroup(castle);
            Check(content.ObjectCount == before - n,
                  $"deleting the castle should remove exactly its {n} object(s) "
                  + $"({before} -> {content.ObjectCount})");
            Check(content.ResolveGroup("castle") == null, "the castle must be gone after deletion");
        }

        content.ClearAll();
        Check(content.ObjectCount == 0, "ClearAll must empty the registry");
        Check(GameObject.Find("DreamCodeVR_World") != null, "ClearAll must not touch the environment");

        if (_failures > 0)
        {
            Debug.LogError($"[Creative] FAILED — {_failures} property violation(s)");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("[Creative] PASS — every structurally different creation placed correctly");
        EditorApplication.Exit(0);
    }

    private static GenerationGroup Build(DcvrGeneratedContent content, Fixture f)
    {
        GenerationGroup g = content.BeginGroup(f.Prompt);
        Vector3[] offsets = DcvrSpatialCompositor.Arrange(f.Layout, f.Count, f.Spacing, f.Prompt.GetHashCode());

        for (int i = 0; i < f.Count; i++)
        {
            GameObject go = DcvrPrim.Create(i == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
            go.name = $"Part_{i}";
            go.transform.SetParent(g.Root, false);
            // The generator gives relationships; the layout resolves them to offsets. The
            // vertical half-size is what puts a part's BASE on the group plane — using the
            // pivot instead is how objects end up half-buried.
            go.transform.localPosition = offsets[i] + new Vector3(0f, f.Size.y * 0.5f, 0f);
            go.transform.localScale = i == 0 ? f.Size * 1.4f : f.Size;
            content.Register(go, g, PartName(f.Prompt, i), f.Floating ? "orbital" : "");
        }
        return g;
    }

    /// <summary>Plausible per-part names, standing in for what a generator produces.</summary>
    private static string PartName(string prompt, int i)
    {
        if (prompt.Contains("solar"))
        {
            string[] bodies = { "sun", "mercury", "venus", "earth", "mars", "jupiter", "saturn", "uranus", "neptune" };
            return i < bodies.Length ? bodies[i] : "body " + i;
        }
        if (prompt.Contains("castle")) { return i == 0 ? "keep" : "tower " + i; }
        return "part " + i;
    }

    private static void Report(GenerationGroup g, Camera cam, Fixture f)
    {
        if (!g.TryGetBounds(out Bounds b)) { return; }
        float dist = Vector3.Distance(
            new Vector3(cam.transform.position.x, 0f, cam.transform.position.z),
            new Vector3(b.center.x, 0f, b.center.z));
        Debug.Log($"[Creative] \"{f.Prompt}\" | {g.Objects.Count} | {b.size} | "
                  + $"{b.min.y:F2} | {dist:F2} m | {g.Root.localScale.x:F3} | {f.Layout}");
    }

    private static void CheckProperties(GenerationGroup g, Camera cam, Fixture f)
    {
        string p = f.Prompt;
        if (!g.TryGetBounds(out Bounds b)) { Check(false, $"{p}: produced no bounds"); return; }

        // Fits the usable area, after any automatic scaling.
        float extent = Mathf.Max(b.size.x, b.size.z);
        Check(extent <= DcvrWorld.PlatformRadius * 1.8f,
              $"{p}: footprint {extent:F1} m does not fit the creation area");
        Check(b.size.y <= 6.5f, $"{p}: {b.size.y:F1} m tall — too tall to see");

        // Floor behaviour: grounded things touch, floating things clear the floor.
        if (f.Floating)
        {
            Check(b.min.y > 0.2f, $"{p}: floating content should clear the floor (min.y={b.min.y:F2})");
        }
        else
        {
            Check(Mathf.Abs(b.min.y) < 0.05f,
                  $"{p}: should sit ON the floor, not at {b.min.y:F2} — buried or hovering");
        }

        // Not inside the user, and not so far it reads as scenery.
        Vector3 user = cam.transform.position;
        Check(!b.Contains(user), $"{p}: the user is INSIDE the creation");
        float gap = Vector3.Distance(b.ClosestPoint(user), user);
        Check(gap >= ProtocolModels.PersonalSpaceRadius,
              $"{p}: only {gap:F2} m from the user — inside personal space");
        Check(gap < 20f, $"{p}: {gap:F1} m away — placed outside the usable area");
    }

    private static void CheckNoBadOverlap(List<GenerationGroup> groups)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (!groups[i].TryGetBounds(out Bounds a)) { continue; }
            for (int j = i + 1; j < groups.Count; j++)
            {
                if (!groups[j].TryGetBounds(out Bounds b)) { continue; }
                Vector2 ca = new Vector2(a.center.x, a.center.z);
                Vector2 cb = new Vector2(b.center.x, b.center.z);
                float sep = Vector2.Distance(ca, cb);
                float need = (Mathf.Max(a.extents.x, a.extents.z) + Mathf.Max(b.extents.x, b.extents.z)) * 0.4f;
                Check(sep >= need,
                      $"'{groups[i].SemanticName}' and '{groups[j].SemanticName}' occupy the same "
                      + $"space ({sep:F1} m apart, need {need:F1} m)");
            }
        }
    }

    private static void CheckNamedResolution(DcvrGeneratedContent content)
    {
        // A part name the generator produced must resolve to exactly one object.
        GameObject saturn = content.Resolve("saturn");
        Check(saturn != null, "'saturn' must resolve to the object of that name");

        // A name nothing has must resolve to NOTHING rather than falling back to whatever
        // was touched last — acting on the wrong object is worse than reporting a miss.
        Check(content.Resolve("thing that does not exist") == null,
              "an unknown name must not fall back to the last-referenced object");

        // Group resolution by a word inside the derived name.
        Check(content.ResolveGroup("solar system") != null, "'solar system' must resolve as a group");
        Check(content.ResolveGroup("garden") != null, "'garden' must resolve as a group");
    }

    private static Camera EnsureCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.transform.position = new Vector3(0f, 1.6f, 0f);
        cam.transform.rotation = Quaternion.identity;
        return cam;
    }

    private static void Check(bool ok, string message)
    {
        if (ok) { return; }
        Debug.LogError("[Creative] " + message);
        _failures++;
    }
}
