// DreamCodeVR+ — lighting for the things the user makes, without relighting the world.
//
// THE PROBLEM, MEASURED
// The environment's key light is deliberately blue (0.62, 0.80, 1.00) at 0.85 intensity
// over a near-black ambient, because almost everything in the scene is emissive and only
// needs a rim to sit in space. Generated content is not emissive — it is ordinary URP/Lit
// geometry wearing the colours the model chose — and under that rig:
//
//     warm grey stone (0.48,0.45,0.40)  renders as  (0.18, 0.24, 0.28)   dark blue-grey
//     brown wood      (0.30,0.12,0.04)  renders as  (0.11, 0.06, 0.03)   nearly black
//     brick red       (0.55,0.12,0.10)  renders as  (0.21, 0.06, 0.07)   nearly black
//
// Every hue collapses toward the same dark blue, because the light multiplies red by 0.62
// and blue by 1.00 and then the whole thing is scaled to about a fifth of its luminance.
// The model's colours were right the whole time; they were simply never being shown.
//
// THE APPROACH: LIGHT THE CREATIONS, NOT THE WORLD (Strategy D)
// A global lighting change would fix the hues and alter an environment that has already
// been accepted. Instead, generated content goes on its own layer and gets its own neutral
// rig, culled to that layer alone. The environment's lighting is not touched at all, and
// creations are lit as what they are: real surfaces in a room.
//
// Two lights, not a dozen. A key for form and a fill so the shadow side stays readable —
// the fill is what stops a rotated cube from having one face at pure black. Generated
// "lights" in creative content (a lamp, a sun) stay EMISSIVE rather than becoming real
// Unity lights, because a scene that spawns a realtime light per lamp is a scene that
// stops holding 72 Hz.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrGeneratedLighting
    {
        public const string LayerName = "DCVR_Generated";

        private static int _layer = -1;
        private static Transform _root;

        /// <summary>The layer index, or -1 if the build has no such layer (in which case
        /// everything degrades to the environment's own lighting rather than breaking).</summary>
        public static int Layer
        {
            get
            {
                if (_layer == -1) { _layer = LayerMask.NameToLayer(LayerName); }
                return _layer;
            }
        }

        /// <summary>Build the neutral rig and stop the environment's blue key from also
        /// lighting generated content — being lit twice would put the blue bias back.</summary>
        public static void Ensure()
        {
            if (_root != null) { return; }
            int layer = Layer;
            if (layer < 0)
            {
                Debug.LogWarning($"[DcvrGenLight] layer '{LayerName}' missing from the build; "
                                 + "generated content will use the environment lighting");
                return;
            }

            var go = new GameObject("DCVR_GeneratedLighting");
            go.transform.SetParent(null, true);
            _root = go.transform;

            int mask = 1 << layer;

            // KEY — neutral white, from high and slightly to one side, so form reads. Angled
            // differently from the environment key so creations are not lit edge-on.
            var key = new GameObject("GenKey");
            key.transform.SetParent(_root, false);
            key.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            Light kl = key.AddComponent<Light>();
            kl.type = LightType.Directional;
            kl.color = new Color(1.00f, 0.98f, 0.94f);   // very slightly warm; neutral reads clinical
            kl.intensity = 1.45f;
            kl.shadows = LightShadows.None;              // nothing here casts a shadow worth the cost
            kl.cullingMask = mask;
            kl.renderMode = LightRenderMode.ForcePixel;

            // FILL — from below and behind, dim and cool. This is the light that keeps a
            // shadowed face at a readable dark version of its own colour instead of black,
            // which is exactly the failure the wearer described as "everything looks the same".
            var fill = new GameObject("GenFill");
            fill.transform.SetParent(_root, false);
            fill.transform.rotation = Quaternion.Euler(-24f, 140f, 0f);
            Light fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.color = new Color(0.72f, 0.80f, 0.92f);
            fl.intensity = 0.55f;
            fl.shadows = LightShadows.None;
            fl.cullingMask = mask;
            fl.renderMode = LightRenderMode.ForcePixel;

            ExcludeGeneratedFromEnvironmentLights(layer);

            Debug.Log($"[DcvrGenLight] neutral rig on layer {layer} "
                      + $"(key {kl.intensity:F2}, fill {fl.intensity:F2}); environment lighting untouched");
        }

        /// <summary>Remove the generated layer from every EXISTING light's culling mask.
        ///
        /// Without this a creation is lit by both rigs and the environment's blue bias comes
        /// straight back, at higher total intensity. The environment's own lighting is not
        /// otherwise modified — only which layers it reaches.</summary>
        private static void ExcludeGeneratedFromEnvironmentLights(int layer)
        {
            int inverse = ~(1 << layer);
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == null || (_root != null && l.transform.IsChildOf(_root))) { continue; }
                l.cullingMask &= inverse;
            }
        }

        /// <summary>Put an object and everything under it on the generated layer.
        ///
        /// Called from the capture pass, so it applies to every creative path uniformly.
        /// Silently does nothing when the layer is absent, which keeps a build without it
        /// merely duller rather than broken.</summary>
        public static void ApplyLayer(GameObject root)
        {
            int layer = Layer;
            if (layer < 0 || root == null) { return; }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }
    }
}
