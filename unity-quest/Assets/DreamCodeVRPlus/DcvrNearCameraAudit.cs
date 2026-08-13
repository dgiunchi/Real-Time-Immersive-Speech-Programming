// DreamCodeVR+ — startup inventory of anything rendering close to the wearer's eye.
//
// Permanent instrumentation, deliberately. Three separate builds shipped with geometry
// drawn on the wearer's eye — a controller model left at the rig origin, which is inside
// their head — and each time it was diagnosed by reasoning about candidates rather than
// by looking. This looks.
//
// Runs once, a few seconds after start (so one-shot effects and the power-on sequence have
// settled), and reports every active renderer within 2 m of the camera with the numbers
// needed to judge it: distance, world-space bounds, lossy scale and material. Anything
// occupying appreciable width a few centimetres from the eye is wrong by inspection.
//
// It logs; it never hides anything by itself. Deciding what may be near the eye is the
// job of the component that put it there.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrNearCameraAudit : MonoBehaviour
    {
        private const float Radius = 2f;
        private const float Delay = 5f;

        public static void Run() =>
            new GameObject("DCVR_NearCameraAudit").AddComponent<DcvrNearCameraAudit>();

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(Delay);

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[NearCameraRenderer] no camera to audit");
                yield break;
            }

            Vector3 eye = cam.transform.position;
            int found = 0;

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) { continue; }
                float d = Vector3.Distance(eye, r.bounds.center);
                if (d > Radius) { continue; }

                found++;
                Transform p = r.transform.parent;
                Debug.Log($"[NearCameraRenderer] name={r.name} parent={(p != null ? p.name : "<root>")} " +
                          $"distance={d:F3}m bounds={r.bounds.size} lossyScale={r.transform.lossyScale} " +
                          $"material={(r.sharedMaterial != null ? r.sharedMaterial.name : "none")}");
            }

            Debug.Log($"[NearCameraRenderer] {found} renderer(s) within {Radius} m of the eye " +
                      $"at ({eye.x:F2},{eye.y:F2},{eye.z:F2}). " +
                      "Expect only floor/platform when standing; NOTHING under a metre.");
            Destroy(gameObject);
        }
    }
}
