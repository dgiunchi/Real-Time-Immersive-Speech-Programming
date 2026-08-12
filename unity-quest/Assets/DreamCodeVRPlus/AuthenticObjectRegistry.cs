using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Reality-awareness registry (TRACK U2) — the answer to Fujita's (2023) object
    /// disguise (wrapping / shielding) and Tseng's (2022) false-positive/swapping,
    /// where a generated object covers or co-locates with a REAL, trusted object.
    ///
    /// The Mode-C SP-03 overlap guard only knew about other GENERATED objects (the
    /// <see cref="GeneratedObjectTracker"/>); it could not protect authentic scene
    /// objects because it had no model of them. This registry IS that model: a list
    /// of real (non-generated) objects the monitor must protect. It can be assigned
    /// in the Inspector or auto-collected at start (every renderer WITHOUT a
    /// <see cref="GeneratedMarker"/> is treated as authentic). A future depth-mesh /
    /// passthrough integration would populate it from the real room scan.
    ///
    /// IL2CPP-safe: plain MonoBehaviour, no reflection.
    /// </summary>
    public sealed class AuthenticObjectRegistry : MonoBehaviour
    {
        [Tooltip("Real (non-generated) objects to protect from wrapping/occlusion. " +
                 "Leave empty + autoCollectAtStart to gather all non-generated renderers.")]
        public List<GameObject> authentic = new List<GameObject>();

        [Tooltip("If true and the list is empty, collect every renderer without a " +
                 "GeneratedMarker at Start as the authentic/real world. DEFAULT FALSE: " +
                 "explicit registration only, so the editable content target is never " +
                 "treated as 'real' and creative 'hide the original' is never reverted. " +
                 "Enable only when a real-world model (e.g. a passthrough mesh) exists.")]
        public bool autoCollectAtStart;

        private void Start()
        {
            if (!autoCollectAtStart || authentic.Count > 0)
            {
                return;
            }
            // First-party (trusted) scan — NOT generated code, so scene queries are fine.
            var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r != null && r.GetComponent<GeneratedMarker>() == null)
                {
                    authentic.Add(r.gameObject);
                }
            }
        }

        /// <summary>
        /// Returns the authentic object that <paramref name="generated"/> wraps /
        /// co-locates with (centre distance within the overlap fraction of the larger
        /// bounding radius), or null. Mirrors the Mode-C SP-03 overlap test but against
        /// REAL objects rather than other generated ones.
        /// </summary>
        public GameObject OverlappedBy(GameObject generated)
        {
            if (generated == null)
            {
                return null;
            }
            float gr = BoundingRadius(generated);
            for (int i = 0; i < authentic.Count; i++)
            {
                var a = authentic[i];
                if (a == null || a == generated)
                {
                    continue;
                }
                // Defensive: an item that became generated is no longer "authentic".
                if (a.GetComponent<GeneratedMarker>() != null)
                {
                    continue;
                }
                float ar = BoundingRadius(a);
                float d = Vector3.Distance(generated.transform.position, a.transform.position);
                if (d < Mathf.Max(gr, ar) * ProtocolModels.OverlapCentreFraction)
                {
                    return a;
                }
            }
            return null;
        }

        /// <summary>
        /// Fujita (2023) shielding cue: returns the authentic object that
        /// <paramref name="generated"/> sits directly in FRONT of (between it and the
        /// head) at a depth differing by more than FocalDepthMismatchM — a possible
        /// 2D-shield disguise — or null. Requires a valid head pose.
        /// </summary>
        public GameObject ShieldsAuthentic(GameObject generated, PerceptualSafety.CameraPose cam)
        {
            if (generated == null || !cam.valid)
            {
                return null;
            }
            Vector3 gDir = generated.transform.position - cam.position;
            float gDist = gDir.magnitude;
            if (gDist < 1e-3f)
            {
                return null;
            }
            gDir /= gDist;
            for (int i = 0; i < authentic.Count; i++)
            {
                var a = authentic[i];
                if (a == null || a == generated || a.GetComponent<GeneratedMarker>() != null)
                {
                    continue;
                }
                Vector3 aDir = a.transform.position - cam.position;
                float aDist = aDir.magnitude;
                if (aDist <= gDist)
                {
                    continue; // authentic object is nearer than/at the generated one: not shielded.
                }
                // Roughly the same bearing (generated occludes the authentic one)...
                if (Vector3.Dot(gDir, aDir.normalized) > 0.97f
                    && (aDist - gDist) > ProtocolModels.FocalDepthMismatchM)
                {
                    return a; // generated occluder sits in front at an inconsistent depth.
                }
            }
            return null;
        }

        private static float BoundingRadius(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            return r != null ? r.bounds.extents.magnitude : 0.5f;
        }
    }
}
