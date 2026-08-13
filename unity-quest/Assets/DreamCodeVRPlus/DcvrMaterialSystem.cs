// DreamCodeVR+ — what a surface is made of, and how to render that safely.
//
// The same division of labour the spatial layer uses:
//
//     the model decides WHAT IT SHOULD LOOK LIKE     (stone, wood, gold, a warm grey)
//     the runtime decides HOW TO RENDER IT SAFELY    (which URP shader, which PBR values)
//
// The model never names a shader. It cannot: a shader name it invents is a shader this
// build does not contain, and the P0 defect was precisely what happens when an unrenderable
// material reaches the headset. Roles are a small closed vocabulary, and anything outside it
// falls back rather than failing.
//
// THE MODEL'S COLOURS ARE AUTHORITATIVE. This system supplies metallic, smoothness and
// emission — the properties that make wood look unlike metal — and leaves hue alone unless
// the model gave none. Overriding a semantic colour with a hard-coded palette would undo
// the part of the pipeline that already works, and would quietly turn a general system into
// a lookup table of subjects someone remembered to code for.
//
// PBR values below are starting points chosen to be clearly DISTINGUISHABLE from each other
// under the generated-content lighting rig, not to be physically accurate. On a mobile GPU
// with no reflection probes, the perceptible axes are roughly "how shiny" and "how metal",
// so those are the ones that carry the difference.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>A closed vocabulary of surface kinds. Deliberately small — a hundred roles
    /// would be a hundred things to tune and would not look any better than a dozen.</summary>
    public enum DcvrMaterialRole
    {
        Generic,
        Stone,
        Wood,
        Metal,
        PaintedMetal,
        Concrete,
        GlassLike,
        Water,
        Grass,
        Soil,
        Fabric,
        Plastic,
        Ceramic,
        Gold,
        Silver,
        Organic,
        Emissive,
    }

    /// <summary>How a surface should look. The model fills in what it knows; the runtime
    /// clamps everything and supplies the rest.</summary>
    public struct DcvrMaterialDescriptor
    {
        public DcvrMaterialRole Role;
        public Color BaseColor;
        public bool HasColor;
        /// <summary>0 = leave the role's default. Otherwise a multiplier on the role's
        /// emission, so "the lamp is brighter" does not require a new role.</summary>
        public float EmissionBoost;
    }

    public static class DcvrMaterialSystem
    {
        // ---- role -> surface properties --------------------------------------------

        private struct RoleProfile
        {
            public float Metallic;
            public float Smoothness;
            public float Emission;      // multiplier on base colour; 0 for ordinary surfaces
            public Color Fallback;      // used ONLY when the model supplied no colour
        }

        /// <summary>Tuned to be distinguishable under the generated-content rig, not to be
        /// physically correct. The two axes a Quest can actually show are how shiny and how
        /// metal a thing is, so those carry the difference between wood and iron.</summary>
        private static RoleProfile Profile(DcvrMaterialRole role)
        {
            switch (role)
            {
                case DcvrMaterialRole.Stone:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.10f, Fallback = new Color(0.52f, 0.50f, 0.46f) };
                case DcvrMaterialRole.Concrete:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.14f, Fallback = new Color(0.62f, 0.61f, 0.58f) };
                case DcvrMaterialRole.Wood:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.26f, Fallback = new Color(0.40f, 0.24f, 0.12f) };
                case DcvrMaterialRole.Metal:
                    return new RoleProfile { Metallic = 0.90f, Smoothness = 0.62f, Fallback = new Color(0.55f, 0.57f, 0.60f) };
                case DcvrMaterialRole.PaintedMetal:
                    return new RoleProfile { Metallic = 0.25f, Smoothness = 0.55f, Fallback = new Color(0.70f, 0.72f, 0.75f) };
                case DcvrMaterialRole.Gold:
                    return new RoleProfile { Metallic = 1.00f, Smoothness = 0.78f, Fallback = new Color(1.00f, 0.78f, 0.34f) };
                case DcvrMaterialRole.Silver:
                    return new RoleProfile { Metallic = 1.00f, Smoothness = 0.80f, Fallback = new Color(0.85f, 0.86f, 0.88f) };
                case DcvrMaterialRole.GlassLike:
                    // Deliberately OPAQUE. Real transparency costs overdraw and sorting bugs
                    // on a mobile tiler, and reads worse in VR than a dark, smooth surface
                    // that behaves like glass in every way the eye checks at this distance.
                    return new RoleProfile { Metallic = 0.10f, Smoothness = 0.92f, Fallback = new Color(0.10f, 0.16f, 0.24f) };
                case DcvrMaterialRole.Water:
                    return new RoleProfile { Metallic = 0.05f, Smoothness = 0.88f, Fallback = new Color(0.13f, 0.36f, 0.52f) };
                case DcvrMaterialRole.Grass:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.08f, Fallback = new Color(0.26f, 0.46f, 0.20f) };
                case DcvrMaterialRole.Soil:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.06f, Fallback = new Color(0.34f, 0.26f, 0.18f) };
                case DcvrMaterialRole.Fabric:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.04f, Fallback = new Color(0.80f, 0.76f, 0.66f) };
                case DcvrMaterialRole.Plastic:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.48f, Fallback = new Color(0.72f, 0.72f, 0.74f) };
                case DcvrMaterialRole.Ceramic:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.70f, Fallback = new Color(0.90f, 0.89f, 0.86f) };
                case DcvrMaterialRole.Organic:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.22f, Fallback = new Color(0.45f, 0.40f, 0.32f) };
                case DcvrMaterialRole.Emissive:
                    // The only role that glows by default. Kept modest: a large bright
                    // surface is uncomfortable in a headset, and the perceptual limits stay.
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.40f, Emission = 0.85f, Fallback = new Color(1.00f, 0.86f, 0.55f) };
                default:
                    return new RoleProfile { Metallic = 0.00f, Smoothness = 0.30f, Fallback = new Color(0.72f, 0.74f, 0.78f) };
            }
        }

        /// <summary>Infer a role from a part's own name.
        ///
        /// Uses the SEMANTIC NAME the model already produces ("Gate Arch", "Lamp Housing"),
        /// which is why naming was worth getting right — one piece of information now serves
        /// both natural-language editing and appearance. This is a fallback for when the
        /// model gives no explicit role, and it is matching against the model's OWN words,
        /// not against the user's prompt, so it never becomes a per-subject special case.</summary>
        public static DcvrMaterialRole InferRole(string semanticName)
        {
            if (string.IsNullOrEmpty(semanticName)) { return DcvrMaterialRole.Generic; }
            string n = semanticName.ToLowerInvariant();

            // Ordered most-specific first: a "gold trim" is gold before it is metal.
            if (Has(n, "gold", "brass", "bronze")) { return DcvrMaterialRole.Gold; }
            if (Has(n, "silver", "chrome", "steel")) { return DcvrMaterialRole.Silver; }
            if (Has(n, "lamp", "light", "glow", "eye", "beacon", "neon", "screen", "panel light", "core", "sun")) { return DcvrMaterialRole.Emissive; }
            if (Has(n, "window", "glass", "pane", "porthole", "windshield", "canopy")) { return DcvrMaterialRole.GlassLike; }
            if (Has(n, "water", "pond", "sea", "lake", "river", "wave")) { return DcvrMaterialRole.Water; }
            if (Has(n, "grass", "lawn", "leaf", "leaves", "foliage", "hedge", "bush")) { return DcvrMaterialRole.Grass; }
            if (Has(n, "soil", "dirt", "earth mound", "mud", "sand")) { return DcvrMaterialRole.Soil; }
            if (Has(n, "sail", "flag", "banner", "cloth", "curtain", "fabric", "tent", "scarf")) { return DcvrMaterialRole.Fabric; }
            if (Has(n, "stone", "rock", "wall", "brick", "boulder", "cobble", "granite", "masonry")) { return DcvrMaterialRole.Stone; }
            if (Has(n, "wood", "plank", "timber", "log", "mast", "beam", "deck", "barrel", "crate", "fence")) { return DcvrMaterialRole.Wood; }
            if (Has(n, "runway", "road", "asphalt", "pavement", "concrete", "platform", "tarmac", "path")) { return DcvrMaterialRole.Concrete; }
            if (Has(n, "rail", "pipe", "girder", "joint", "bolt", "hinge", "chain", "anchor", "cannon", "engine", "antenna", "strut")) { return DcvrMaterialRole.Metal; }
            if (Has(n, "hull", "body", "chassis", "fuselage", "armor", "armour", "plate", "casing")) { return DcvrMaterialRole.PaintedMetal; }
            if (Has(n, "trunk", "branch", "plant", "flower", "animal", "horse", "tree")) { return DcvrMaterialRole.Organic; }
            return DcvrMaterialRole.Generic;
        }

        private static bool Has(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.Contains(needles[i])) { return true; }
            }
            return false;
        }

        public static DcvrMaterialRole ParseRole(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) { return DcvrMaterialRole.Generic; }
            switch (s.Trim().ToLowerInvariant().Replace(" ", "_"))
            {
                case "stone": case "rock": return DcvrMaterialRole.Stone;
                case "wood": case "timber": return DcvrMaterialRole.Wood;
                case "metal": case "iron": return DcvrMaterialRole.Metal;
                case "painted_metal": case "painted": return DcvrMaterialRole.PaintedMetal;
                case "concrete": return DcvrMaterialRole.Concrete;
                case "glass": case "glass_like": return DcvrMaterialRole.GlassLike;
                case "water": return DcvrMaterialRole.Water;
                case "grass": return DcvrMaterialRole.Grass;
                case "soil": case "dirt": return DcvrMaterialRole.Soil;
                case "fabric": case "cloth": return DcvrMaterialRole.Fabric;
                case "plastic": return DcvrMaterialRole.Plastic;
                case "ceramic": return DcvrMaterialRole.Ceramic;
                case "gold": return DcvrMaterialRole.Gold;
                case "silver": return DcvrMaterialRole.Silver;
                case "organic": return DcvrMaterialRole.Organic;
                case "emissive": case "light": return DcvrMaterialRole.Emissive;
                default: return DcvrMaterialRole.Generic;
            }
        }

        // ---- the cache (§20) --------------------------------------------------------

        // Keyed on QUANTISED values, so a hundred nearly-identical browns collapse into one
        // material instead of a hundred. Without quantisation a "cache" keyed on exact RGB
        // is just a leak with extra steps.
        private static readonly Dictionary<long, Material> _cache = new Dictionary<long, Material>(128);

        /// <summary>How many distinct materials exist. Reported in the capture telemetry so
        /// the batching cost of visual variety is visible rather than assumed.</summary>
        public static int CachedMaterialCount => _cache.Count;

        private const int ColorSteps = 24;   // ~24 levels per channel: finer than the eye
                                             // needs at these sizes, coarse enough to reuse

        private static long Key(DcvrMaterialRole role, Color c, float metallic, float smooth, float emission)
        {
            long k = (long)role;
            k = k * 32 + Mathf.RoundToInt(Mathf.Clamp01(c.r) * ColorSteps);
            k = k * 32 + Mathf.RoundToInt(Mathf.Clamp01(c.g) * ColorSteps);
            k = k * 32 + Mathf.RoundToInt(Mathf.Clamp01(c.b) * ColorSteps);
            k = k * 12 + Mathf.RoundToInt(Mathf.Clamp01(metallic) * 10);
            k = k * 12 + Mathf.RoundToInt(Mathf.Clamp01(smooth) * 10);
            k = k * 12 + Mathf.RoundToInt(Mathf.Clamp01(emission) * 10);
            return k;
        }

        /// <summary>Build (or reuse) a stereo-safe material for this descriptor.</summary>
        public static Material Resolve(DcvrMaterialDescriptor d)
        {
            RoleProfile p = Profile(d.Role);

            // The model's colour wins. The role only supplies one when there is none.
            Color baseColor = d.HasColor ? Sanitize(d.BaseColor) : p.Fallback;

            float emission = p.Emission;
            if (d.EmissionBoost > 0f) { emission = Mathf.Clamp(emission + d.EmissionBoost, 0f, 1.6f); }

            long key = Key(d.Role, baseColor, p.Metallic, p.Smoothness, emission);
            if (_cache.TryGetValue(key, out Material cached) && cached != null) { return cached; }

            Shader sh = DcvrMaterials.UrpLit;
            var m = new Material(sh)
            {
                name = $"DCVR_{d.Role}",
                enableInstancing = true,
            };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", baseColor); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", baseColor); }
            if (m.HasProperty("_Metallic")) { m.SetFloat("_Metallic", p.Metallic); }
            if (m.HasProperty("_Smoothness")) { m.SetFloat("_Smoothness", p.Smoothness); }

            if (emission > 0.001f)
            {
                // Emission is tinted by the surface's OWN colour, so a lamp glows in its own
                // hue rather than every emissive thing glowing the same white.
                Color e = baseColor * emission;
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", e); }
            }

            _cache[key] = m;
            return m;
        }

        /// <summary>Reject anything that cannot be rendered sanely (§28).
        ///
        /// NaN and infinities come out of arithmetic in generated code and turn a surface
        /// black or invisible; a colour brighter than 1 becomes an unintended HDR bloom
        /// source, which in a headset is a comfort problem rather than an aesthetic one.</summary>
        public static Color Sanitize(Color c)
        {
            float r = Fix(c.r), g = Fix(c.g), b = Fix(c.b);
            // A colour so dark it is indistinguishable from black loses all hue information;
            // lift it just enough to stay readable rather than clamping it away.
            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max < 0.04f) { return new Color(0.06f, 0.06f, 0.07f, 1f); }
            return new Color(r, g, b, 1f);
        }

        private static float Fix(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { return 0.5f; }
            return Mathf.Clamp01(v);
        }

        /// <summary>Drop cached materials. Called by the full clear so a long session cannot
        /// accumulate materials for creations that no longer exist.</summary>
        public static void ClearCache()
        {
            foreach (KeyValuePair<long, Material> kv in _cache)
            {
                if (kv.Value != null) { Object.Destroy(kv.Value); }
            }
            _cache.Clear();
        }
    }
}
