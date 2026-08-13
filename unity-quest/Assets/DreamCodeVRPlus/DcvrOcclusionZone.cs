// DreamCodeVR+ — the forward occlusion zone, made visible only when it matters.
//
// The perceptual-safety layer reserves the wearer's forward view: generated content may
// not occlude what they need to see to stay oriented and safe. That rule is invisible by
// design, which is a problem for a demonstration — an examiner cannot see a constraint
// being honoured, only violated.
//
// So this draws the protected volume, and the policy is the point:
//
//   normal operation      invisible. A safety boundary that is always on screen is
//                         furniture; it stops being read after a minute and costs the
//                         wearer forward visibility for the whole session — which is
//                         precisely the harm the rule exists to prevent.
//   perceptual event      fades in over ~0.3 s, holds ~1.5 s, fades out.
//
// The CHECK is not gated on the visual. This class only renders; enforcement lives in the
// validator and the Unity-side bounds re-check, and disabling this changes nothing about
// what is allowed.
//
// Geometry is a low, wide wedge built from a handful of quads rather than a cone mesh:
// cheaper, and the flat facets read more like a projected safety field than a solid.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrOcclusionZone : MonoBehaviour
    {
        private const float Reach = 2.6f;        // how far ahead the zone is protected
        private const float HalfAngle = 26f;     // degrees either side of forward
        private const float PeakAlpha = 0.16f;

        private Material _mat;
        private Transform _wedge;
        private Coroutine _routine;

        public static DcvrOcclusionZone Build()
        {
            var go = new GameObject("DCVR_OcclusionZone");
            var z = go.AddComponent<DcvrOcclusionZone>();
            z.Construct();
            return z;
        }

        private void Construct()
        {
            _wedge = new GameObject("Wedge").transform;
            _wedge.SetParent(transform, false);
            _mat = Holo(DcvrWorld.Cyan, 0f);

            // Fan of quads sweeping the protected arc, tapering with distance so the
            // near edge reads as the boundary rather than a wall in front of the face.
            const int slices = 9;
            for (int i = 0; i < slices; i++)
            {
                float t = i / (float)(slices - 1);
                float ang = Mathf.Lerp(-HalfAngle, HalfAngle, t) * Mathf.Deg2Rad;

                var q = DcvrPrim.Create(PrimitiveType.Quad, $"slice{i}");
                q.transform.SetParent(_wedge, false);
                float dist = Reach * 0.62f;
                q.transform.localPosition = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);
                q.transform.localRotation = Quaternion.Euler(90f, -ang * Mathf.Rad2Deg, 0f);
                q.transform.localScale = new Vector3(0.30f, Reach * 0.78f, 1f);
                q.GetComponent<Renderer>().sharedMaterial = _mat;
            }

            _wedge.gameObject.SetActive(false);
        }

        /// <summary>Reveal the zone briefly. Called when a perceptual-plane decision is made,
        /// never on a timer — the boundary is evidence, not decoration.</summary>
        public void Reveal(Color color, float hold = 1.5f)
        {
            if (_mat == null) { return; }
            if (_routine != null) { StopCoroutine(_routine); }
            _routine = StartCoroutine(RevealRoutine(color, hold));
        }

        private IEnumerator RevealRoutine(Color color, float hold)
        {
            _wedge.gameObject.SetActive(true);
            _mat.SetColor("_Color", color);

            const float rise = 0.3f;
            float t = 0f;
            while (t < rise)
            {
                t += Time.deltaTime;
                _mat.SetFloat("_Alpha", Mathf.Lerp(0f, PeakAlpha, t / rise));
                yield return null;
            }

            yield return new WaitForSeconds(hold);

            const float fall = 0.55f;
            t = 0f;
            while (t < fall)
            {
                t += Time.deltaTime;
                _mat.SetFloat("_Alpha", Mathf.Lerp(PeakAlpha, 0f, t / fall));
                yield return null;
            }

            _wedge.gameObject.SetActive(false);
            _routine = null;
        }

        /// <summary>Anchor to the wearer's position and heading, on the horizontal plane
        /// only. Following head pitch would swing the zone up at the sky when they look up,
        /// which is not what "the forward view" means.</summary>
        private void LateUpdate()
        {
            if (_wedge == null || !_wedge.gameObject.activeSelf) { return; }
            Camera cam = Camera.main;
            if (cam == null) { return; }

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { return; }

            transform.position = new Vector3(cam.transform.position.x,
                                             cam.transform.position.y - 0.25f,
                                             cam.transform.position.z);
            transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        private static Material Holo(Color c, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return null; }
            var m = new Material(s) { name = "DCVR_OcclusionMat" };
            m.SetColor("_Color", c);
            m.SetFloat("_Alpha", alpha);
            m.SetFloat("_ScanSpeed", 0.8f);
            m.SetFloat("_ScanDensity", 10f);
            return m;
        }
    }
}
