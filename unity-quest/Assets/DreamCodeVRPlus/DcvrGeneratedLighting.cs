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
// STRATEGY D WAS TRIED AND MEASURED UNAFFORDABLE.
// The first version of this file put generated content on its own layer and gave it a
// dedicated neutral rig — a key and a fill, culled to that layer alone. It looked like the
// right answer: correct hues, real shading, environment untouched.
//
// It cost half the frame rate. Measured on device:
//
//     before                     72.0 fps   median 13.9 ms
//     with two extra lights      36.0 fps   median 27.8 ms
//     ...with the scene CLEARED  36.0 fps   median 27.8 ms   <- the giveaway
//
// The last row is the finding. With zero generated objects in the scene the cost was
// unchanged, so it was never about what the lights illuminated. In URP's forward path an
// additional directional light is evaluated per fragment across the frame; a culling mask
// controls which objects it AFFECTS, not whether the additional-light loop runs. Two of
// them over a 1680x1760 per-eye buffer is ~14 ms, and once a frame misses the 13.9 ms
// budget the compositor halves the rate outright — hence exactly 36, not "somewhat less".
//
// So this class no longer creates lights. It keeps the LAYER, because that is free and
// still useful (it documents what is generated, and leaves the door open for a future
// single-light experiment), and the visibility problem is solved instead by a modest
// emission floor on generated materials — Strategy C. That costs nothing per frame: it is
// a term in a shader that already runs.
//
// Generated "lights" in creative content (a lamp, a sun) were always going to be emissive
// rather than real Unity lights, for exactly the reason this measurement demonstrates.

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

        /// <summary>Set up the generated-content layer. Creates NO lights — see the note
        /// at the top of this file for the measurement that removed them.</summary>
        public static void Ensure()
        {
            if (_root != null) { return; }
            int layer = Layer;
            if (layer < 0)
            {
                Debug.LogWarning($"[DcvrGenLight] layer '{LayerName}' missing from the build; "
                                 + "generated content will still render, just untagged");
                return;
            }

            var go = new GameObject("DCVR_GeneratedLighting");
            go.transform.SetParent(null, true);
            _root = go.transform;

            Debug.Log($"[DcvrGenLight] generated layer {layer}; no extra realtime lights "
                      + "(measured at half frame rate) — visibility comes from the material "
                      + "emission floor instead");
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
