// DreamCodeVR+ — the operations that never need a language model.
//
// "Delete Saturn" is bookkeeping. "Clear everything" is a system call. "Make this red" is
// a bounded property write on an object the user is pointing at. None of them is a
// creative act, and routing them through a generative model buys nothing while costing a
// round trip, a few cents, and a chance of the model returning something unexpected for
// an operation whose meaning was never in doubt (§23, §89).
//
// THE RULE THAT MATTERS (§26)
//
//     the local path is NO AI. it is not NO SAFETY.
//
// Every operation here goes through the same numeric bounds as a validated action plan —
// the ones in `ProtocolModels`, mirrored from the Rust source of truth — and the same
// personal-space invariant. Skipping the model does not skip the validator, because the
// validator is not the model. Confusing the two would turn a latency optimisation into a
// hole, and it would be the kind of hole that only shows up when someone asks the fast
// path to do something the slow path would have refused.
//
// Deletion is deliberately conservative: an operation that cannot resolve its target does
// nothing and says so. Deleting the wrong object is far worse than deleting nothing.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public enum DcvrOp
    {
        Unknown,
        Delete,        // one object
        DeleteGroup,   // a whole creation
        ClearAll,
        SetColor,
        SetScale,      // relative: bigger / smaller
        Move,
        Rotate,
    }

    public sealed class DcvrLocalCommands : MonoBehaviour
    {
        public static DcvrLocalCommands Instance { get; private set; }

        public static DcvrLocalCommands Ensure()
        {
            if (Instance != null) { return Instance; }
            GameObject go = GameObject.Find("DCVR_LocalCommands") ?? new GameObject("DCVR_LocalCommands");
            go.transform.SetParent(null, true);
            Instance = go.GetComponent<DcvrLocalCommands>() ?? go.AddComponent<DcvrLocalCommands>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
        }

        public struct Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) => new Result { Ok = false, Message = m };
            public static Result Good(string m) => new Result { Ok = true, Message = m };
        }

        /// <summary>Run a resolved operation.
        ///
        /// `target` is the user's own word for the thing ("Saturn", "the castle") or empty
        /// for a deictic reference, which resolves to what they are pointing at.</summary>
        public Result Execute(DcvrOp op, string target, string value, Vector3 axis, float amount)
        {
            DcvrGeneratedContent content = DcvrGeneratedContent.Ensure();

            switch (op)
            {
                case DcvrOp.ClearAll:
                {
                    int n = content.ClearAll();
                    return Result.Good($"cleared {n} generated object(s)");
                }

                case DcvrOp.DeleteGroup:
                {
                    GenerationGroup g = content.ResolveGroup(target);
                    if (g == null) { return Result.Fail($"no creation called '{target}'"); }
                    string name = string.IsNullOrEmpty(g.SemanticName) ? $"generation {g.Id}" : g.SemanticName;
                    int n = g.Objects.Count;
                    content.DeleteGroup(g);
                    return Result.Good($"removed '{name}' ({n} object(s))");
                }

                case DcvrOp.Delete:
                {
                    // A named delete that does not resolve to exactly one object falls back
                    // to the GROUP of that name before giving up — "delete the castle"
                    // reads as one thing to a person whether it is one object or twenty.
                    GameObject go = content.Resolve(target);
                    if (go == null)
                    {
                        GenerationGroup g = content.ResolveGroup(target);
                        if (g != null) { return Execute(DcvrOp.DeleteGroup, target, value, axis, amount); }
                        return Result.Fail(string.IsNullOrEmpty(target)
                            ? "nothing is selected — point at an object first"
                            : $"could not find '{target}'");
                    }
                    string label = LabelOf(go, target);
                    content.DeleteObject(go);
                    return Result.Good($"removed {label}");
                }

                case DcvrOp.SetColor:
                    return ApplyColor(content, target, value);

                case DcvrOp.SetScale:
                    return ApplyScale(content, target, amount);

                case DcvrOp.Move:
                    return ApplyMove(content, target, axis, amount);

                case DcvrOp.Rotate:
                    return ApplyRotate(content, target, amount);

                default:
                    return Result.Fail("unrecognised operation");
            }
        }

        // ---- property operations, all bounds-checked --------------------------------

        private static Result ApplyColor(DcvrGeneratedContent content, string target, string colorText)
        {
            if (!TryParseColor(colorText, out Color c)) { return Result.Fail($"unknown colour '{colorText}'"); }

            // Colouring is non-destructive, so a plural match is applied to ALL of it. A
            // castle gate really is a frame, an arch and two doors; recolouring one of the
            // four and refusing the rest as "ambiguous" is a worse answer than doing what
            // the person plainly meant. Deletion keeps the stricter rule, because getting
            // that wrong cannot be undone.
            List<GameObject> targets = content.ResolveAll(target);
            if (targets.Count == 0) { return NotFound(target); }

            int n = 0;
            foreach (GameObject go in targets)
            {
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) { continue; }
                    r.material.color = c;
                    n++;
                }
                content.NoteReference(go);
            }
            if (n == 0) { return Result.Fail("that object has nothing to colour"); }
            return targets.Count == 1
                ? Result.Good($"{LabelOf(targets[0], target)} is now {colorText}")
                : Result.Good($"{targets.Count} '{target}' parts are now {colorText}");
        }

        private static Result ApplyScale(DcvrGeneratedContent content, string target, float factor)
        {
            GameObject go = content.Resolve(target);
            if (go == null) { return NotFound(target); }

            // Clamp against the SAME limits a validated plan gets. The scale is relative,
            // so the bound applies to the resulting absolute scale, not the multiplier —
            // otherwise "make it bigger" five times walks past a limit one step at a time.
            Vector3 cur = go.transform.localScale;
            float wanted = cur.x * factor;
            float clamped = Mathf.Clamp(wanted, ProtocolModels.ScaleMin, ProtocolModels.ScaleMax);
            if (Mathf.Approximately(clamped, cur.x))
            {
                return Result.Fail($"{LabelOf(go, target)} is already at its size limit");
            }

            float k = clamped / Mathf.Max(cur.x, 0.0001f);
            go.transform.localScale = cur * k;

            if (!ClearsPersonalSpace(go))
            {
                go.transform.localScale = cur;   // undo: growing into the user is refused
                return Result.Fail("refused — that would put the object inside your personal space");
            }

            content.NoteReference(go);
            return Result.Good($"{LabelOf(go, target)} scaled to {clamped:F2}");
        }

        private static Result ApplyMove(DcvrGeneratedContent content, string target, Vector3 axis, float metres)
        {
            GameObject go = content.Resolve(target);
            if (go == null) { return NotFound(target); }

            float d = Mathf.Clamp(metres, -ProtocolModels.MoveMaxTotalDistance, ProtocolModels.MoveMaxTotalDistance);

            // "Left" means the user's left, not the world's. Direction is resolved against
            // the horizontal camera basis so the result matches what they meant, and the
            // pitch is dropped for the same reason the creation area drops it.
            Camera cam = Camera.main;
            Vector3 worldAxis = axis;
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) { fwd = Vector3.forward; }
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                worldAxis = right * axis.x + Vector3.up * axis.y + fwd * axis.z;
            }

            Vector3 before = go.transform.position;
            go.transform.position = before + worldAxis.normalized * d;

            if (!ClearsPersonalSpace(go))
            {
                go.transform.position = before;
                return Result.Fail("refused — that would move the object into your personal space");
            }
            if (!ClearsFloor(go))
            {
                go.transform.position = before;
                return Result.Fail("refused — that would push the object through the floor");
            }

            content.NoteReference(go);
            return Result.Good($"{LabelOf(go, target)} moved {d:F2} m");
        }

        private static Result ApplyRotate(DcvrGeneratedContent content, string target, float degrees)
        {
            GameObject go = content.Resolve(target);
            if (go == null) { return NotFound(target); }
            go.transform.Rotate(0f, Mathf.Clamp(degrees, -360f, 360f), 0f, Space.World);
            content.NoteReference(go);
            return Result.Good($"{LabelOf(go, target)} rotated");
        }

        // ---- safety helpers ---------------------------------------------------------

        /// <summary>The same personal-space invariant the plan validator enforces. A local
        /// edit is still an edit, and the user can still be standing next to it.</summary>
        private static bool ClearsPersonalSpace(GameObject go)
        {
            Camera cam = Camera.main;
            if (cam == null) { return true; }
            Vector3 user = cam.transform.position;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) { continue; }
                if (r.bounds.Contains(user)) { return false; }
                if (Vector3.Distance(r.bounds.ClosestPoint(user), user) < ProtocolModels.PersonalSpaceRadius)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ClearsFloor(GameObject go)
        {
            if (!DcvrSpatialCompositor.TryGetBounds(go.transform, out Bounds b)) { return true; }
            return b.min.y > -0.25f;
        }

        private static Result NotFound(string target) => Result.Fail(
            string.IsNullOrEmpty(target)
                ? "nothing is selected — point at an object first"
                : $"could not find '{target}'");

        private static string LabelOf(GameObject go, string spoken)
        {
            var m = go.GetComponent<GeneratedMarker>();
            if (m != null && !string.IsNullOrEmpty(m.SemanticName)) { return m.SemanticName; }
            return string.IsNullOrEmpty(spoken) ? "that object" : spoken;
        }

        /// <summary>Colour words, plus `#rrggbb` for anything the backend resolved itself.
        /// A closed vocabulary on purpose: a colour that fails to parse is reported, not
        /// guessed at, so "make it chartreuse" falls through to the model rather than
        /// silently producing something else.</summary>
        public static bool TryParseColor(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrWhiteSpace(s)) { return false; }
            string t = s.Trim().ToLowerInvariant();

            if (t.StartsWith("#") && ColorUtility.TryParseHtmlString(t, out Color parsed))
            {
                c = parsed;
                return true;
            }

            switch (t)
            {
                case "red": c = new Color(0.90f, 0.15f, 0.15f); return true;
                case "green": c = new Color(0.15f, 0.80f, 0.25f); return true;
                case "blue": c = new Color(0.15f, 0.35f, 0.95f); return true;
                case "yellow": c = new Color(0.95f, 0.85f, 0.15f); return true;
                case "orange": c = new Color(0.98f, 0.55f, 0.10f); return true;
                case "purple": c = new Color(0.60f, 0.25f, 0.85f); return true;
                case "pink": c = new Color(0.98f, 0.45f, 0.70f); return true;
                case "cyan": c = new Color(0.20f, 0.85f, 0.95f); return true;
                case "white": c = Color.white; return true;
                case "black": c = new Color(0.06f, 0.06f, 0.07f); return true;
                case "grey":
                case "gray": c = new Color(0.5f, 0.5f, 0.52f); return true;
                case "brown": c = new Color(0.45f, 0.29f, 0.15f); return true;
                default: return false;
            }
        }
    }
}
