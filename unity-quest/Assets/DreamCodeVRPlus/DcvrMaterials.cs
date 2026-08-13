// DreamCodeVR+ — giving generated geometry a material that can be seen in stereo.
//
// THE P0 DEFECT THIS FIXES
// The wearer reported that one generated object appeared as TWO, and that it seemed to
// follow their gaze. An automated test had already asserted that generated transforms do
// not move when the rig does, and it passed — correctly. The transforms were never the
// problem. The problem was what the objects were being drawn WITH.
//
// `GameObject.CreatePrimitive` attaches Unity's built-in `Default-Material`, whose shader
// belongs to the legacy render pipeline. This project renders with URP, and the legacy
// shader is not in the build at all, so Unity substitutes `Hidden/InternalErrorShader` —
// measured on device, not assumed. Generated C# calls `CreatePrimitive` for everything it
// builds, so every creation was drawn by the error shader.
//
// WHY THAT PRODUCES *EXACTLY* THE TWO REPORTED SYMPTOMS
// The build runs OpenXR single-pass stereo: both eyes are rendered in one pass, and a
// shader only positions itself correctly per eye if it participates in the stereo macros.
// The internal error shader does not. So the object is drawn at the SAME place in the left
// and right eye images, and:
//
//   * it carries NO binocular disparity, so the two images have nothing to fuse into a
//     single depth percept — the brain reports two flat objects, which is precisely what
//     closing one eye then the other makes obvious;
//   * an object pinned to the same screen coordinates in both eyes moves with the view by
//     construction, which is indistinguishable from being glued to the wearer's head.
//
// One cause, both symptoms. Nothing was wrong with the hierarchy or the placement.
//
// THE FIX: no generated renderer may carry a material we cannot draw in stereo. Every
// object passes through the capture layer, so that is where this runs — one choke point,
// applied uniformly, rather than trusting generated code to pick a shader.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrMaterials
    {
        private static Shader _urpLit;
        private static Shader _urpUnlit;

        /// <summary>The lit URP shader, resolved once.
        ///
        /// Lit rather than Unlit for generated content: creations are objects in a lit
        /// world, and unlit geometry reads as a flat sticker next to the environment.</summary>
        public static Shader UrpLit
        {
            get
            {
                if (_urpLit == null)
                {
                    _urpLit = Shader.Find("Universal Render Pipeline/Lit")
                              ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                              ?? UrpUnlit;
                }
                return _urpLit;
            }
        }

        public static Shader UrpUnlit
        {
            get
            {
                if (_urpUnlit == null)
                {
                    _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
                }
                return _urpUnlit;
            }
        }

        /// <summary>True when this material will draw correctly in single-pass stereo.
        ///
        /// The test is by shader FAMILY, not by name matching: URP's own shaders and the
        /// project's own shaders both declare the stereo macros, and anything else in this
        /// build is either the legacy pipeline (not present, so substituted) or the error
        /// shader itself. A missing material counts as unusable, because Unity will pick
        /// the same substitute for it.</summary>
        public static bool IsStereoSafe(Material m)
        {
            if (m == null || m.shader == null) { return false; }
            string n = m.shader.name;
            if (n.StartsWith("Hidden/", System.StringComparison.Ordinal)) { return false; }
            return n.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal)
                   || n.StartsWith("DreamCodeVRPlus/", System.StringComparison.Ordinal);
        }

        /// <summary>Replace any unusable material on this subtree, preserving the colour the
        /// generated code chose.
        ///
        /// Colour is carried over deliberately. Generated code sets `renderer.material.color`
        /// to express intent — a red roof, a blue planet — and that intent survives even
        /// when the shader it was written onto cannot be drawn. Silently making everything
        /// grey would fix the stereo bug and lose the creation's design.
        ///
        /// Returns how many renderers had to be repaired, so the caller can log a number
        /// that means something rather than a reassuring message.</summary>
        public static int RepairSubtree(GameObject root)
        {
            return RepairSubtree(root, null);
        }

        /// <summary>Normalise every renderer in the subtree so it is drawable in stereo AND
        /// looks like what it is meant to be made of.
        ///
        /// Two jobs, deliberately in one pass, because they need the same information at the
        /// same moment: the colour the generator chose is only readable BEFORE the material
        /// is replaced, and the role is only inferable from the object's semantic name.
        ///
        /// The colour is authoritative and is never overridden — the model's semantic hues
        /// were measured to be correct, so this supplies only the properties it did not: how
        /// metallic, how smooth, whether it glows. Splitting these into two passes would mean
        /// reading the old material twice or caching it, for no benefit.
        ///
        /// `nameOf` maps a renderer to its semantic name; null falls back to the GameObject
        /// name, which is what generated code sets and is usually meaningful.</summary>
        public static int RepairSubtree(GameObject root, System.Func<GameObject, string> nameOf)
        {
            if (root == null) { return 0; }
            int repaired = 0;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) { continue; }
                Material current = r.sharedMaterial;

                // RECOVER THE INTENT FIRST. This is measured, not assumed: the substituted
                // error shader has no `_BaseColor` but DOES carry `_Color`, so the colour
                // the generator set is still readable here. Losing it would turn every
                // creation grey, which is the failure this whole pass exists to avoid.
                bool haveColor = false;
                Color wanted = Color.white;
                if (current != null)
                {
                    if (current.HasProperty("_BaseColor")) { wanted = current.GetColor("_BaseColor"); haveColor = true; }
                    else if (current.HasProperty("_Color")) { wanted = current.GetColor("_Color"); haveColor = true; }
                }

                string semantic = nameOf != null ? nameOf(r.gameObject) : r.gameObject.name;
                DcvrMaterialRole role = DcvrMaterialSystem.InferRole(semantic);

                // Already-safe materials still get their ROLE applied — a URP material with
                // the right colour but plastic-flat shading is exactly the "everything looks
                // the same" complaint, and Mode C produces those.
                r.sharedMaterial = DcvrMaterialSystem.Resolve(new DcvrMaterialDescriptor
                {
                    Role = role,
                    BaseColor = wanted,
                    HasColor = haveColor,
                });
                if (!IsStereoSafe(current)) { repaired++; }
            }
            return repaired;
        }

        /// <summary>A fresh stereo-safe material of the given colour.</summary>
        public static Material Make(Color c)
        {
            Shader sh = UrpLit;
            var m = new Material(sh)
            {
                name = "DCVR_GeneratedMat",
                // Instancing matters on a mobile GPU drawing dozens of small primitives,
                // and single-pass-instanced stereo uses the same machinery.
                enableInstancing = true,
            };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
            // Generated scenes are dozens of small objects; specular highlights on all of
            // them read as noise and cost fill rate for nothing.
            if (m.HasProperty("_Smoothness")) { m.SetFloat("_Smoothness", 0.15f); }
            if (m.HasProperty("_Metallic")) { m.SetFloat("_Metallic", 0f); }
            return m;
        }
    }
}
