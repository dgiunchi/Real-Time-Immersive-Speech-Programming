// DreamCodeVR+ — where a creation goes, and how it is made to fit.
//
// THE DIVISION OF LABOUR (§2, §87)
//
//     the model decides WHAT exists and how the parts relate
//     this file decides WHERE it goes and how it fits safely
//
// Asking a language model for world coordinates produces exactly what you would expect:
// planets at arbitrary points, houses half underground, a castle centred on the user's
// head. It is being asked to do metric reasoning it has no way to check, about a room it
// cannot see. Relationships — "planets orbit the sun", "towers at the corners" — are
// something it CAN state reliably, and those are resolved into coordinates here, where
// the floor height, the platform radius, the user's pose and what is already built are
// all actually known.
//
// The layout strategies are primitives, not a catalogue of subjects (§0, §15). "Radial"
// is not a solar-system generator; it is what you use for anything arranged around a
// centre, including a ring of standing stones or a roundabout. Nothing here matches on
// prompt text.
//
// ONE USE OF THE PLAYER POSE, AND ONLY ONE (§18, §88). The pose picks the initial spawn
// point. After that the creation is world-space content and nothing in this file touches
// it again — there is no Update() here at all, which is the structural reason a creation
// cannot follow the head.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>How a set of sibling objects is arranged. Deliberately generic.</summary>
    public enum DcvrLayout
    {
        None,       // the generator gave real positions; respect them
        Single,
        Linear,
        Grid,
        Rows,
        Radial,     // evenly around a centre
        Orbital,    // around a centre at growing radii — planets, electrons, moons
        Ring,
        Cluster,
        Scatter,    // deterministic pseudo-random spread — forests, debris, crowds
        Perimeter,  // corners/edges of a square — towers, fence posts, columns
    }

    public sealed class DcvrSpatialCompositor : MonoBehaviour
    {
        public static DcvrSpatialCompositor Instance { get; private set; }

        // Read from the built world rather than invented, so these stay true if the
        // platform is resized.
        private const float FloorY = 0f;
        private const float PlatformRadius = DcvrWorld.PlatformRadius;   // 4 m

        /// <summary>Never place anything nearer than this to the user (§44). This is the
        /// same constant the perceptual validator uses; it is a safety bound, not a
        /// composition preference.</summary>
        private const float PersonalSpace = ProtocolModels.PersonalSpaceRadius;

        /// <summary>Closest a creation may be placed, measured to its NEAREST face. Well
        /// outside personal space: an object you cannot focus on is not a creation you can
        /// look at.</summary>
        private const float MinComfortDistance = 1.2f;

        private const float GroundClearance = 0.005f;

        private readonly List<Slot> _slots = new List<Slot>();

        private sealed class Slot
        {
            public int GroupId;
            public Vector3 Center;
            public float Radius;
        }

        public static DcvrSpatialCompositor Ensure()
        {
            if (Instance != null) { return Instance; }
            GameObject go = GameObject.Find("DCVR_SpatialCompositor") ?? new GameObject("DCVR_SpatialCompositor");
            go.transform.SetParent(null, true);
            Instance = go.GetComponent<DcvrSpatialCompositor>() ?? go.AddComponent<DcvrSpatialCompositor>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
        }

        // ---- the creation area (§10) ------------------------------------------------

        /// <summary>Where new work should appear, derived from the user's BODY and
        /// horizontal gaze.
        ///
        /// The forward vector is flattened deliberately. Head pitch is where someone is
        /// looking, not where they are: if they glance at the floor while saying "create a
        /// house", the house belongs in front of them, not buried. Using the raw camera
        /// forward is the single most common way to produce underground buildings.</summary>
        public struct CreationArea
        {
            public Vector3 Origin;      // the user's feet, on the floor plane
            public Vector3 Forward;     // horizontal, normalised
            public float FloorY;
            public float UsableRadius;
        }

        public CreationArea GetCreationArea()
        {
            var area = new CreationArea { FloorY = FloorY, UsableRadius = PlatformRadius };

            Camera cam = Camera.main;
            if (cam == null)
            {
                area.Origin = Vector3.zero;
                area.Forward = Vector3.forward;
                return area;
            }

            Vector3 head = cam.transform.position;
            area.Origin = new Vector3(head.x, FloorY, head.z);

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            // Looking straight up or down leaves no horizontal component to normalise.
            area.Forward = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            return area;
        }

        /// <summary>How far away a creation of this size belongs (§11).
        ///
        /// Scaled from the content's own footprint rather than fixed, because "a chess
        /// piece" and "a city" want very different distances and a single constant is
        /// wrong for both. The rule is roughly "stand back far enough to see it whole".</summary>
        private static float DistanceFor(float radius)
        {
            float d = 1.8f + radius * 1.6f;
            return Mathf.Clamp(d, MinComfortDistance + radius, 9f);
        }

        // ---- layout (§15) -----------------------------------------------------------

        /// <summary>Resolve sibling objects into local positions for a named strategy.
        ///
        /// Returns LOCAL offsets around the group origin. Deterministic: the same request
        /// twice gives the same arrangement, which matters because a demo that rearranges
        /// itself between runs is impossible to talk about.</summary>
        public static Vector3[] Arrange(DcvrLayout layout, int count, float spacing, int seed)
        {
            var p = new Vector3[Mathf.Max(count, 0)];
            if (count <= 0) { return p; }
            if (count == 1 || layout == DcvrLayout.Single) { return p; }

            switch (layout)
            {
                case DcvrLayout.Linear:
                {
                    float span = spacing * (count - 1);
                    for (int i = 0; i < count; i++) { p[i] = new Vector3(-span * 0.5f + i * spacing, 0f, 0f); }
                    break;
                }
                case DcvrLayout.Rows:
                {
                    int perRow = Mathf.CeilToInt(Mathf.Sqrt(count));
                    for (int i = 0; i < count; i++)
                    {
                        int r = i / perRow, c = i % perRow;
                        p[i] = new Vector3((c - (perRow - 1) * 0.5f) * spacing, 0f, -r * spacing);
                    }
                    break;
                }
                case DcvrLayout.Grid:
                {
                    int side = Mathf.CeilToInt(Mathf.Sqrt(count));
                    for (int i = 0; i < count; i++)
                    {
                        int r = i / side, c = i % side;
                        p[i] = new Vector3((c - (side - 1) * 0.5f) * spacing,
                                           0f,
                                           (r - (side - 1) * 0.5f) * spacing);
                    }
                    break;
                }
                case DcvrLayout.Radial:
                case DcvrLayout.Ring:
                {
                    float radius = Mathf.Max(spacing, spacing * count / (2f * Mathf.PI));
                    for (int i = 0; i < count; i++)
                    {
                        float a = i / (float)count * Mathf.PI * 2f;
                        p[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    }
                    break;
                }
                case DcvrLayout.Orbital:
                {
                    // Index 0 is the centre; the rest sit at growing radii, each at a
                    // different angle so nothing hides behind anything else.
                    p[0] = Vector3.zero;
                    for (int i = 1; i < count; i++)
                    {
                        float radius = spacing * i;
                        float a = i * 2.399963f;    // golden angle: no two align
                        p[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    }
                    break;
                }
                case DcvrLayout.Perimeter:
                {
                    // Corners first, then edges — so four objects give four corners, which
                    // is what "a castle with four towers" should produce.
                    float h = spacing;
                    var corners = new[]
                    {
                        new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h),
                        new Vector3(h, 0f, h), new Vector3(-h, 0f, h),
                    };
                    for (int i = 0; i < count; i++)
                    {
                        if (i < 4) { p[i] = corners[i]; }
                        else
                        {
                            float a = (i - 4) / (float)Mathf.Max(1, count - 4) * Mathf.PI * 2f;
                            p[i] = new Vector3(Mathf.Cos(a) * h * 1.414f, 0f, Mathf.Sin(a) * h * 1.414f);
                        }
                    }
                    break;
                }
                case DcvrLayout.Cluster:
                case DcvrLayout.Scatter:
                {
                    // Deterministic from the seed, and rejection-spaced so nothing lands
                    // on top of anything else. Not true blue noise; enough to read as
                    // natural rather than gridded.
                    var rng = new System.Random(seed == 0 ? 1 : seed);
                    float spread = spacing * Mathf.Sqrt(count) * 0.9f;
                    float minSep = spacing * 0.75f;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 c = Vector3.zero;
                        for (int attempt = 0; attempt < 12; attempt++)
                        {
                            float x = (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                            float z = (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                            c = new Vector3(x, 0f, z);
                            bool ok = true;
                            for (int j = 0; j < i; j++)
                            {
                                if (Vector3.Distance(c, p[j]) < minSep) { ok = false; break; }
                            }
                            if (ok) { break; }
                        }
                        p[i] = c;
                    }
                    break;
                }
            }
            return p;
        }

        /// <summary>Map a coarse role word to a layout. Roles come from the generator's
        /// own description of the structure, never from matching the user's prompt.</summary>
        public static DcvrLayout LayoutFromRole(string role)
        {
            switch ((role ?? "").ToLowerInvariant())
            {
                case "orbit":
                case "orbital": return DcvrLayout.Orbital;
                case "radial":
                case "around": return DcvrLayout.Radial;
                case "ring": return DcvrLayout.Ring;
                case "corners":
                case "perimeter": return DcvrLayout.Perimeter;
                case "grid": return DcvrLayout.Grid;
                case "rows": return DcvrLayout.Rows;
                case "row":
                case "line":
                case "linear": return DcvrLayout.Linear;
                case "scatter":
                case "cluster": return DcvrLayout.Scatter;
                default: return DcvrLayout.None;
            }
        }

        // ---- grounding (§12, §13) ---------------------------------------------------

        /// <summary>Roles that belong in the air. Everything else meets the floor.
        ///
        /// This is a small vocabulary of composition roles, not a list of subjects: it
        /// answers "does this touch the ground", which is a spatial question the generator
        /// can answer about anything it invents.</summary>
        public static bool IsFloatingRole(string role)
        {
            switch ((role ?? "").ToLowerInvariant())
            {
                case "float":
                case "floating":
                case "orbit":
                case "orbital":
                case "celestial":
                case "sky":
                case "aerial":
                case "suspended":
                case "ceiling":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Combined world bounds of a subtree's renderers.
        ///
        /// Renderer bounds, not the transform position: an object's pivot is wherever the
        /// mesh author put it, so "move it down by its position" buries some objects and
        /// floats others. Only the actual geometry knows where its lowest point is.</summary>
        public static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) { continue; }
                if (!any) { bounds = r.bounds; any = true; }
                else { bounds.Encapsulate(r.bounds); }
            }
            return any;
        }

        // ---- the whole placement pass (§14, §39, §45-47, §51) -----------------------

        /// <summary>Place a finished creation in the world: size it to fit, put it at a
        /// sensible distance in a free area, sit it on the floor unless it is meant to
        /// fly, and keep it out of the user.
        ///
        /// Runs ONCE, after the objects exist. Everything it needs — real bounds, real
        /// user pose, real occupancy — only becomes knowable at that point, which is why
        /// this is a post-pass rather than something the generator is asked to get right.</summary>
        public void Place(GenerationGroup group, bool floating)
        {
            if (group?.Root == null) { return; }
            Transform root = group.Root;

            if (!TryGetBounds(root, out Bounds b))
            {
                // Nothing renderable — put the empty group somewhere sane and stop.
                CreationArea emptyArea = GetCreationArea();
                root.position = emptyArea.Origin + emptyArea.Forward * 2.5f;
                return;
            }

            CreationArea area = GetCreationArea();

            // 1. FIT. Scale the whole group uniformly if it is too big for the space.
            //    Uniform, and applied to the group root, so the relationships the
            //    generator described survive — a castle scaled to fit is still a castle,
            //    but a castle with only its towers shrunk is not (§14).
            float maxExtent = Mathf.Max(b.size.x, b.size.z);
            float maxAllowed = area.UsableRadius * 1.6f;
            if (maxExtent > maxAllowed && maxExtent > 0.001f)
            {
                float k = maxAllowed / maxExtent;
                root.localScale *= k;
                TryGetBounds(root, out b);
            }
            // Height gets its own cap: something can fit the floor plan and still be a
            // tower you cannot see the top of.
            const float maxHeight = 6.0f;
            if (b.size.y > maxHeight && b.size.y > 0.001f)
            {
                float k = maxHeight / b.size.y;
                root.localScale *= k;
                TryGetBounds(root, out b);
            }

            // 2. CHOOSE A FREE SPOT at a distance suited to the size (§11, §46, §47).
            float radius = Mathf.Max(b.extents.x, b.extents.z);
            Vector3 center = FindFreeCenter(area, radius, group.Id);

            // 3. MOVE the group so its bounds centre lands on that spot. Offset from the
            //    measured centre rather than assuming the root pivot is central — group
            //    roots sit at the origin and the content can be anywhere relative to it.
            Vector3 delta = center - new Vector3(b.center.x, root.position.y, b.center.z);
            root.position += new Vector3(delta.x, 0f, delta.z);
            TryGetBounds(root, out b);

            // 4. HEIGHT. Ground it, or lift it to a comfortable floating band (§12, §13).
            float yShift;
            if (floating)
            {
                // Aim the group's CENTRE near eye level, then guarantee floor clearance
                // so a "floating" thing can never end up half-buried.
                float eye = Camera.main != null ? Camera.main.transform.position.y : 1.6f;
                float targetCenterY = Mathf.Clamp(eye + 0.15f, area.FloorY + b.extents.y + 0.3f, area.FloorY + 4.5f);
                yShift = targetCenterY - b.center.y;
                if (b.min.y + yShift < area.FloorY + 0.25f)
                {
                    yShift = area.FloorY + 0.25f - b.min.y;
                }
            }
            else
            {
                yShift = area.FloorY + GroundClearance - b.min.y;
            }
            root.position += new Vector3(0f, yShift, 0f);
            TryGetBounds(root, out b);

            // 5. PERSONAL SPACE, last (§44, §45). Every earlier step can move things, so
            //    this is checked on the final position or not meaningfully at all.
            PushOutOfPersonalSpace(root, ref b, area);

            _slots.Add(new Slot { GroupId = group.Id, Center = b.center, Radius = Mathf.Max(b.extents.x, b.extents.z) });
            LogPlacement(group, b, area);
        }

        /// <summary>Find a centre that does not sit on top of an existing creation.
        ///
        /// Tries straight ahead first, then fans outwards in alternating directions. A
        /// person who builds a castle and then a robot expects them side by side, not
        /// interpenetrating (§46).</summary>
        private Vector3 FindFreeCenter(CreationArea area, float radius, int groupId)
        {
            float distance = DistanceFor(radius);
            Vector3 ahead = area.Origin + area.Forward * distance;

            if (IsFree(ahead, radius, groupId)) { return ahead; }

            // Fan out: ±25°, ±50°, ±75°, then step back and repeat.
            for (int ring = 0; ring < 3; ring++)
            {
                float d = distance + ring * (radius * 1.5f + 1.0f);
                for (int step = 1; step <= 3; step++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        float angle = sign * step * 25f;
                        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * area.Forward;
                        Vector3 candidate = area.Origin + dir * d;
                        if (IsFree(candidate, radius, groupId)) { return candidate; }
                    }
                }
            }

            // Everything nearby is taken. Place it behind the crowd rather than refusing —
            // a creation the user has to turn around to see beats no creation at all.
            return area.Origin + area.Forward * (distance + 6f);
        }

        private bool IsFree(Vector3 center, float radius, int groupId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.GroupId == groupId) { continue; }
                float need = (s.Radius + radius) * 0.85f;   // slight overlap is fine
                Vector2 a = new Vector2(center.x, center.z);
                Vector2 b = new Vector2(s.Center.x, s.Center.z);
                if (Vector2.Distance(a, b) < need) { return false; }
            }
            return true;
        }

        /// <summary>Push a creation away from the user until it clears personal space.
        ///
        /// Measured to the NEAREST face of the bounds, not to the centre: a 4 m wide
        /// object centred 2 m away already has the user inside it.</summary>
        private static void PushOutOfPersonalSpace(Transform root, ref Bounds b, CreationArea area)
        {
            Camera cam = Camera.main;
            Vector3 user = cam != null ? cam.transform.position : area.Origin + Vector3.up * 1.6f;

            for (int guard = 0; guard < 8; guard++)
            {
                Vector3 closest = b.ClosestPoint(user);
                float gap = Vector3.Distance(closest, user);
                bool inside = b.Contains(user);
                if (!inside && gap >= Mathf.Max(PersonalSpace, MinComfortDistance)) { return; }

                Vector3 away = new Vector3(b.center.x - user.x, 0f, b.center.z - user.z);
                if (away.sqrMagnitude < 1e-4f) { away = area.Forward; }
                away.Normalize();

                float push = inside
                    ? Mathf.Max(b.extents.x, b.extents.z) + MinComfortDistance
                    : (MinComfortDistance - gap) + 0.25f;
                root.position += away * push;
                TryGetBounds(root, out b);
            }
        }

        // ---- explicit placement (§48, §49) ------------------------------------------

        /// <summary>Place at a point the user pointed to, or beside a named object.
        /// Explicit spatial intent overrides automatic placement — but not safety, so it
        /// still grounds and still clears personal space.</summary>
        public void PlaceAt(GenerationGroup group, Vector3 worldPoint, bool floating)
        {
            if (group?.Root == null) { return; }
            Transform root = group.Root;
            if (!TryGetBounds(root, out Bounds b)) { root.position = worldPoint; return; }

            CreationArea area = GetCreationArea();
            Vector3 delta = worldPoint - new Vector3(b.center.x, root.position.y, b.center.z);
            root.position += new Vector3(delta.x, 0f, delta.z);
            TryGetBounds(root, out b);

            float yShift = floating
                ? Mathf.Max(0f, area.FloorY + 0.25f - b.min.y)
                : area.FloorY + GroundClearance - b.min.y;
            root.position += new Vector3(0f, yShift, 0f);
            TryGetBounds(root, out b);

            PushOutOfPersonalSpace(root, ref b, area);
            _slots.Add(new Slot { GroupId = group.Id, Center = b.center, Radius = Mathf.Max(b.extents.x, b.extents.z) });
        }

        /// <summary>A free point adjacent to an existing object, for "next to Earth".</summary>
        public Vector3 BesideObject(GameObject anchor, float radius)
        {
            if (anchor == null) { return GetCreationArea().Origin + GetCreationArea().Forward * 2.5f; }
            var r = anchor.GetComponent<Renderer>();
            Bounds ab = r != null ? r.bounds : new Bounds(anchor.transform.position, Vector3.one * 0.5f);

            CreationArea area = GetCreationArea();
            // Prefer the side away from the user, so the new object does not hide the one
            // it was placed next to.
            Vector3 right = Vector3.Cross(Vector3.up, area.Forward).normalized;
            float gap = Mathf.Max(ab.extents.x, ab.extents.z) + radius + 0.3f;
            Vector3 candidate = ab.center + right * gap;
            if (!IsFree(candidate, radius, -1)) { candidate = ab.center - right * gap; }
            return candidate;
        }

        // ---- slots ------------------------------------------------------------------

        public void ReleaseSlot(int groupId) => _slots.RemoveAll(s => s.GroupId == groupId);
        public void ReleaseAllSlots() => _slots.Clear();

        // ---- telemetry (§72) --------------------------------------------------------

        /// <summary>One line per creation, at creation time. Not per frame (§73).</summary>
        private static void LogPlacement(GenerationGroup g, Bounds b, CreationArea area)
        {
            Camera cam = Camera.main;
            float dist = cam != null
                ? Vector3.Distance(new Vector3(cam.transform.position.x, 0f, cam.transform.position.z),
                                   new Vector3(b.center.x, 0f, b.center.z))
                : 0f;
            Debug.Log($"[DcvrSpatial] gen={g.Id} name='{g.SemanticName}' objects={g.Objects.Count} "
                      + $"bounds={b.size} lowestY={b.min.y:F2} highestY={b.max.y:F2} floorY={area.FloorY:F2} "
                      + $"distance={dist:F2} scale={g.Root.localScale.x:F3} center={b.center}");
        }
    }
}
