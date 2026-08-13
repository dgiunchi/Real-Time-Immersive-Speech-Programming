// DreamCodeVR+ — three cubes that say which layer is broken.
//
// The wearer reports one object appearing as two. That could be the XR rig, the render
// path, the capture layer, the material, or the generated code itself, and changing things
// at random until it looks right is how you end up not knowing what was wrong. So three
// cubes are placed side by side, each built by a DIFFERENT layer, and the wearer only has
// to say which of them double:
//
//   LEVEL 1  L1_Static      built here, in plain code, with an explicitly-assigned URP
//                           material. No LLM, no capture, no compositor, no Update.
//   LEVEL 2  L2_Prim        built with DcvrPrim — the project's own primitive helper —
//                           with whatever material that path leaves in place.
//   LEVEL 3  L3_Primitive   built with GameObject.CreatePrimitive and its DEFAULT material,
//                           which is exactly what generated C# does.
//
// Reading the result:
//   all three double        -> the XR rig / stereo mode / camera setup
//   only L3 doubles         -> the default material from CreatePrimitive
//   L2 and L3 double        -> material assignment in the primitive path
//   none double in this test but a real creation does -> generated code or the compositor
//
// Placed at a fixed WORLD position, not relative to the wearer, so "does it follow me" is
// answerable at the same time: walk past them and they must stay put.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrStereoFixture
    {
        private const float Height = 1.3f;
        private const float Size = 0.25f;

        public static void Build()
        {
            var root = new GameObject("DCVR_StereoFixture");
            root.transform.SetParent(null, true);
            root.transform.position = Vector3.zero;

            // Fixed world coordinates. Nothing here consults the camera — if these move
            // when the wearer moves, the fault is not in the creative pipeline at all.
            MakeLevel1(root.transform, new Vector3(-0.45f, Height, 2.2f));
            MakeLevel2(root.transform, new Vector3(0.00f, Height, 2.2f));
            MakeLevel3(root.transform, new Vector3(0.30f, Height, 2.2f));
            MakeLevel4(root.transform, new Vector3(0.75f, Height, 2.2f));

            Debug.Log("[StereoFixture] four cubes at z=2.2, left to right: "
                      + "L1 explicit URP material / L2 DcvrPrim / L3 raw CreatePrimitive "
                      + "(EXPECTED BROKEN) / L4 CreatePrimitive + repair. "
                      + "Report which appear doubled — L3 alone confirms the diagnosis.");
            DcvrStereoProbe.Run("fixture built");
        }

        /// <summary>Level 1 — the control. Explicit URP unlit material, nothing else.</summary>
        private static void MakeLevel1(Transform parent, Vector3 pos)
        {
            GameObject go = DcvrPrim.Create(PrimitiveType.Cube, "L1_Static");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Size;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = "L1_Mat", enableInstancing = true };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f)); }
            go.GetComponent<Renderer>().sharedMaterial = m;
            Report(go, "L1 explicit URP unlit");
        }

        /// <summary>Level 2 — the project's primitive helper, material left as it comes.</summary>
        private static void MakeLevel2(Transform parent, Vector3 pos)
        {
            GameObject go = DcvrPrim.Create(PrimitiveType.Cube, "L2_Prim");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Size;
            Report(go, "L2 DcvrPrim default");
        }

        /// <summary>Level 3 — what generated C# actually does, verbatim.</summary>
        private static void MakeLevel3(Transform parent, Vector3 pos)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "L3_Primitive";
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Size;

            // Generated code sets a colour, which instantiates the material it was given.
            var r = go.GetComponent<Renderer>();
            if (r != null) { r.material.color = new Color(1f, 0.5f, 0.2f); }
            Report(go, "L3 CreatePrimitive default material");
        }

        /// <summary>Level 4 — the same raw CreatePrimitive as L3, then put through the
        /// repair the runtime applies to every generated object. L3 and L4 differ by
        /// exactly one function call, so comparing them in the headset isolates the fix
        /// from everything else: if L3 doubles and L4 does not, the cause and the cure are
        /// both established by the wearer rather than argued from logs.</summary>
        private static void MakeLevel4(Transform parent, Vector3 pos)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "L4_Repaired";
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Size;

            var r = go.GetComponent<Renderer>();
            if (r != null) { r.material.color = new Color(0.3f, 0.9f, 0.4f); }

            int repaired = DcvrMaterials.RepairSubtree(go);
            Report(go, $"L4 CreatePrimitive + RepairSubtree (repaired {repaired})");
        }

        private static void Report(GameObject go, string what)
        {
            var r = go.GetComponent<Renderer>();
            Material m = r != null ? r.sharedMaterial : null;
            Shader sh = m != null ? m.shader : null;
            Debug.Log($"[StereoFixture] {what}: '{go.name}' world={go.transform.position} "
                      + $"shader='{(sh == null ? "<none>" : sh.name)}' "
                      + $"instancing={(m != null && m.enableInstancing)} "
                      + $"renderers={go.GetComponentsInChildren<Renderer>(true).Length}");
        }
    }
}
