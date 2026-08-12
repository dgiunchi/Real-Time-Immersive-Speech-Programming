// DreamCodeVR+ — world-space text, with a fallback that cannot silently vanish.
//
// TextMeshPro is used when its font assets are present and legacy TextMesh otherwise.
// This is not belt-and-braces: TMP essential resources are not reliably imported by a
// batchmode build, and a TMP component with no font asset renders NOTHING — no error,
// no placeholder, just an invisible label. On a headset that failure is indistinguishable
// from a layout bug and costs a build cycle to diagnose, so the fallback is the difference
// between a demo that reads and one that is silently blank.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrText
    {
        /// <summary>Create a centred world-space label. Returns an opaque handle to pass
        /// back to <see cref="SetText"/> / <see cref="SetColor"/>.</summary>
        public static object Make(Transform parent, string content, Vector3 localPos,
                                  float size, Color color)
        {
            var go = new GameObject("DCVR_Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            System.Type tmpType = System.Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var comp = go.AddComponent(tmpType);
                if (comp != null && TrySetupTmp(comp, tmpType, content, size, color))
                {
                    return comp;
                }
                Object.Destroy(comp);
            }

            var tm = go.AddComponent<TextMesh>();
            tm.text = content;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            // Legacy TextMesh is raster, not SDF: author at a large point size and scale
            // down, or it is unreadably soft at VR focal distance.
            tm.fontSize = 96;
            tm.characterSize = size * 0.28f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }

        public static void SetText(object handle, string value)
        {
            if (handle is TextMesh tm) { tm.text = value; return; }
            if (handle is Component c) { c.GetType().GetProperty("text")?.SetValue(c, value); }
        }

        public static void SetColor(object handle, Color value)
        {
            if (handle is TextMesh tm) { tm.color = value; return; }
            if (handle is Component c) { c.GetType().GetProperty("color")?.SetValue(c, value); }
        }

        private static bool TrySetupTmp(Component comp, System.Type t, string content,
                                        float size, Color color)
        {
            try
            {
                t.GetProperty("text")?.SetValue(comp, content);
                t.GetProperty("fontSize")?.SetValue(comp, size * 40f);
                t.GetProperty("color")?.SetValue(comp, color);
                var align = System.Type.GetType("TMPro.TextAlignmentOptions, Unity.TextMeshPro");
                if (align != null)
                {
                    t.GetProperty("alignment")?.SetValue(comp, System.Enum.Parse(align, "Center"));
                }
                var rect = comp.GetComponent<RectTransform>();
                if (rect != null) { rect.sizeDelta = new Vector2(4f, 0.4f); }
                // No font asset means nothing will draw. Report that as failure so the
                // caller falls back instead of shipping an invisible label.
                return t.GetProperty("font")?.GetValue(comp) != null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DcvrText] TMP unavailable, using TextMesh: " + e.Message);
                return false;
            }
        }
    }
}
