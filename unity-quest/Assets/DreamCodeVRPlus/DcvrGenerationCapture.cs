// DreamCodeVR+ — making sure everything a generation creates belongs to that generation.
//
// THE PROBLEM (§40, §42, §43)
// Generated code creates objects however it likes. `GameObject.CreatePrimitive` leaves
// them at the scene root; a script may parent them to itself, to each other, or — the
// case that actually matters — to `Camera.main.transform`, which glues the user's own
// creation to their face. Nothing about being generated makes an object visible to the
// authoring system, so without this pass a creation cannot be named, moved, fitted,
// deleted or cleared. It would be litter in the scene that only a full reset removes.
//
// THE APPROACH: least invasive that actually works.
// The generated script is hosted ON the group root, so well-behaved code that parents to
// its own `transform` lands inside the group for free. Everything else is caught by a
// diff: snapshot the scene roots before execution, sweep afterwards, and adopt whatever
// is new. Two frames of delay, once per generation — nothing here runs per frame (§73).
//
// The alternative designs were considered and rejected: instrumenting a creation factory
// would require the generated code to cooperate (it has no reason to), and a provenance
// component can only be added by code that already knows about us. A diff assumes nothing
// about what the model wrote, which is the only safe assumption available.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamCodeVRPlus
{
    public sealed class DcvrGenerationCapture : MonoBehaviour
    {
        public static DcvrGenerationCapture Instance { get; private set; }

        // Object identity, not instance IDs: Unity 6 is retiring the int form of
        // GetInstanceID and the replacement EntityId no longer converts to int. Reference
        // identity is what the diff actually needs, and it cannot go stale — the set is
        // cleared and rebuilt on every snapshot.
        private readonly HashSet<GameObject> _rootsBefore = new HashSet<GameObject>();

        public static DcvrGenerationCapture Ensure()
        {
            if (Instance != null) { return Instance; }
            GameObject go = GameObject.Find("DCVR_GenerationCapture") ?? new GameObject("DCVR_GenerationCapture");
            go.transform.SetParent(null, true);
            Instance = go.GetComponent<DcvrGenerationCapture>() ?? go.AddComponent<DcvrGenerationCapture>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
        }

        /// <summary>Names that must never be adopted, whatever a script does with them.
        /// These are the application, not the user's work.</summary>
        private static readonly string[] ProtectedRoots =
        {
            "XR Origin", "Main Camera", "GeneratedContent", "DreamCodeVR_World",
            "Managers", "DCVR_", "Directional Light", "EventSystem",
        };

        private static bool IsProtected(GameObject go)
        {
            string n = go.name;
            for (int i = 0; i < ProtectedRoots.Length; i++)
            {
                if (n.StartsWith(ProtectedRoots[i], System.StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        /// <summary>Record the scene roots that existed before a generation ran.</summary>
        public void Snapshot()
        {
            _rootsBefore.Clear();
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                _rootsBefore.Add(go);
            }
        }

        /// <summary>Adopt everything the generation produced, then hand the finished group
        /// to the spatial compositor.
        ///
        /// Waits two frames first: Unity runs `Start` before the next `Update`, and a
        /// script that builds in a coroutine or on its second frame is common enough that
        /// sweeping immediately would miss half a creation. Two frames is the shortest
        /// delay that reliably sees the whole thing.</summary>
        public IEnumerator CaptureAfterExecution(GenerationGroup group, string floatingHint)
        {
            yield return null;
            yield return null;

            DcvrGeneratedContent content = DcvrGeneratedContent.Ensure();
            int adopted = 0;

            // 1. New scene-root objects — the CreatePrimitive default.
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (_rootsBefore.Contains(go)) { continue; }
                if (IsProtected(go)) { continue; }
                content.Register(go, group, NameFor(go), "");
                adopted++;
            }

            // 2. Anything parented under the rig or the camera (§43). This is the case
            //    that ruins the experience rather than merely losing track of an object:
            //    content under the camera offset moves with the head, so the user's own
            //    creation follows them around the room. Re-parent with worldPositionStays
            //    so it is left exactly where it was drawn, then let the compositor place
            //    it properly with everything else.
            int rescued = RescueFromRig(content, group);
            adopted += rescued;

            // 3. Objects the script built under its host (the group root) are already in
            //    the right branch but not yet registered, so they have no identity and
            //    cannot be addressed by name.
            adopted += RegisterUnregisteredDescendants(content, group);

            if (adopted == 0)
            {
                Debug.Log($"[DcvrCapture] gen={group.Id} produced no adoptable objects");
                yield break;
            }

            // P0: give everything a material that can be drawn in stereo, BEFORE it is
            // placed and shown. `GameObject.CreatePrimitive` attaches a legacy-pipeline
            // material that does not exist in this URP build, so Unity substitutes
            // `Hidden/InternalErrorShader` — which ignores the per-eye view matrix and
            // therefore draws the object at the same screen position in both eyes. That is
            // what made one creation look like two, and what made it appear to follow the
            // wearer's head. Every generated object passes through here, so this is the
            // one place the guarantee can actually be made.
            // Roles are inferred from the SEMANTIC name the model gave each part ("Gate
            // Arch", "Lamp Housing"), not from the user's prompt — so a subject nobody
            // anticipated gets sensible materials for free, and nothing here is a
            // per-subject special case.
            DcvrGeneratedContent reg = content;
            int repaired = DcvrMaterials.RepairSubtree(group.Root.gameObject, go =>
            {
                var m = go.GetComponent<GeneratedMarker>();
                string s = m != null && !string.IsNullOrEmpty(m.SemanticName) ? m.SemanticName : go.name;
                return s;
            });
            _ = reg;

            // Generated content is lit by its own neutral rig, so its hues survive the
            // environment's deliberately blue key. Layer assignment must happen before the
            // first frame it is visible, or it flashes dark for a frame.
            DcvrGeneratedLighting.Ensure();
            DcvrGeneratedLighting.ApplyLayer(group.Root.gameObject);

            bool floating = DcvrSpatialCompositor.IsFloatingRole(floatingHint);
            DcvrSpatialCompositor.Ensure().Place(group, floating);

            // Log the NAMES, not just the count. "Adopted 37 objects" says the capture
            // worked; it says nothing about whether the user can address any of them, and
            // those are the two different failures this path produces. Naming is the one
            // part of the pipeline the model owns outright, so when "make the gate red"
            // misses, this line is the difference between debugging the resolver and
            // discovering the parts were all called Cube.
            var names = new List<string>();
            foreach (GameObject go in group.Objects)
            {
                if (go == null) { continue; }
                var m = go.GetComponent<GeneratedMarker>();
                if (m != null && !string.IsNullOrEmpty(m.SemanticName)) { names.Add(m.SemanticName); }
                if (names.Count >= 12) { break; }
            }
            // Visual-diversity telemetry (§31): how many distinct colours and roles a
            // creation actually ended up with. A complex object that comes out 90% one
            // appearance is the failure this pass exists to catch, and it is only visible
            // if it is counted.
            var roles = new HashSet<string>();
            var colours = new HashSet<int>();
            foreach (GameObject go in group.Objects)
            {
                if (go == null) { continue; }
                var rend = go.GetComponent<Renderer>();
                if (rend == null || rend.sharedMaterial == null) { continue; }
                roles.Add(rend.sharedMaterial.name);
                Color c = rend.sharedMaterial.HasProperty("_BaseColor")
                    ? rend.sharedMaterial.GetColor("_BaseColor")
                    : rend.sharedMaterial.color;
                colours.Add((Mathf.RoundToInt(c.r * 12) << 8) | (Mathf.RoundToInt(c.g * 12) << 4)
                            | Mathf.RoundToInt(c.b * 12));
            }

            Debug.Log($"[DcvrCapture] gen={group.Id} adopted={adopted} rescued-from-rig={rescued} "
                      + $"materials-repaired={repaired} floating={floating} "
                      + $"named={names.Count}/{group.Objects.Count} [{string.Join(", ", names)}]");
            Debug.Log($"[DcvrVisual] gen={group.Id} parts={group.Objects.Count} "
                      + $"distinct-roles={roles.Count} distinct-colours={colours.Count} "
                      + $"cached-materials={DcvrMaterialSystem.CachedMaterialCount} "
                      + $"[{string.Join(", ", roles)}]");
        }

        private static int RescueFromRig(DcvrGeneratedContent content, GenerationGroup group)
        {
            int n = 0;
            foreach (Transform suspectRoot in RigRoots())
            {
                if (suspectRoot == null) { continue; }
                // Snapshot the children first: re-parenting mutates the collection.
                var children = new List<Transform>();
                foreach (Transform c in suspectRoot) { children.Add(c); }

                foreach (Transform c in children)
                {
                    if (c == null) { continue; }
                    if (IsRigOwned(c.gameObject)) { continue; }
                    if (c.GetComponent<Renderer>() == null
                        && c.GetComponentInChildren<Renderer>(true) == null)
                    {
                        continue;   // no geometry: not user content
                    }
                    Debug.LogWarning($"[DcvrCapture] '{c.name}' was parented to the player rig "
                                     + $"('{suspectRoot.name}') — re-parenting into the world so it "
                                     + "cannot follow the head");
                    c.SetParent(group.Root, worldPositionStays: true);
                    content.Register(c.gameObject, group, NameFor(c.gameObject), "");
                    n++;
                }
            }
            return n;
        }

        private static IEnumerable<Transform> RigRoots()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                yield return cam.transform;
                if (cam.transform.parent != null) { yield return cam.transform.parent; }
            }
            GameObject origin = GameObject.Find("XR Origin");
            if (origin != null) { yield return origin.transform; }
        }

        /// <summary>Parts of the rig itself, which must stay where they are.</summary>
        private static bool IsRigOwned(GameObject go)
        {
            string n = go.name;
            return n.StartsWith("DCVR_", System.StringComparison.Ordinal)
                || n.Contains("Camera")
                || n.Contains("Controller")
                || n.Contains("Hand")
                || n.Contains("Offset");
        }

        private static int RegisterUnregisteredDescendants(DcvrGeneratedContent content, GenerationGroup group)
        {
            if (group.Root == null) { return 0; }
            int n = 0;
            foreach (Transform t in group.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t == group.Root) { continue; }
                GameObject go = t.gameObject;
                if (content.IsRegistered(go)) { continue; }
                if (go.GetComponent<Renderer>() == null) { continue; }   // empties are structure
                content.Register(go, group, NameFor(go), "");
                n++;
            }
            return n;
        }

        /// <summary>Recover a human-usable name from whatever the script called the object.
        ///
        /// Generated code names things well far more often than not — `Sun`, `Tower_NW`,
        /// `Planet3` — because the model is describing what it is building. What it does
        /// not do is strip Unity's clone suffixes, so `Sphere(Clone)` and `Cube (1)` need
        /// cleaning before a person can say them out loud. Where nothing useful survives,
        /// the name is left empty and the object stays addressable by pointing.</summary>
        private static string NameFor(GameObject go)
        {
            string n = go.name;
            int paren = n.IndexOf('(');
            if (paren > 0) { n = n.Substring(0, paren); }

            // DCVRGEN_ is the provenance prefix the generation prompt stamps on everything
            // it creates. It is meaningful to the system and meaningless to a person — nobody
            // says "delete DCVRGEN Saturn" — so it comes off before the name is used as a
            // handle. The prefix itself stays on the GameObject, where the provenance
            // machinery still reads it.
            foreach (string prefix in new[] { "DCVRGEN_", "DCVRGEN", "dcvrgen_" })
            {
                if (n.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    n = n.Substring(prefix.Length);
                    break;
                }
            }

            // CamelCase to words, so "NorthWestTower" can be said as "north west tower".
            n = SplitCamelCase(n);
            n = n.Replace('_', ' ').Trim();

            // A bare primitive type name says nothing a user would say.
            switch (n.ToLowerInvariant())
            {
                case "cube":
                case "sphere":
                case "capsule":
                case "cylinder":
                case "plane":
                case "quad":
                case "gameobject":
                case "new game object":
                    return "";
                default:
                    return n;
            }
        }

        /// <summary>"NorthWestTower" -> "North West Tower".
        ///
        /// Generated code names things in CamelCase because that is how C# identifiers
        /// look. People say them as words, and the resolver normalises to words, so the
        /// two only meet if the split happens here.</summary>
        private static string SplitCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) { return s; }
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool boundary = i > 0
                                && char.IsUpper(c)
                                && (char.IsLower(s[i - 1]) || char.IsDigit(s[i - 1]));
                if (boundary) { sb.Append(' '); }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
