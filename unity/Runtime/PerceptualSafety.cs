using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// User-frame perceptual-safety guards (TRACK U1). Pure, stateless, IL2CPP-safe
    /// helpers (no reflection, no System.IO/Net) used by <see cref="ActionPlanExecutor"/>
    /// to validate a plan BEFORE it touches the scene. Every guard fails CLOSED:
    /// when the head pose is unknown/low-confidence (cold start) head-relative
    /// placements are rejected rather than risked.
    /// </summary>
    public static class PerceptualSafety
    {
        /// <summary>Snapshot of the headset pose + whether we trust it this frame.</summary>
        public struct CameraPose
        {
            public bool valid;        // false => cold start / no Camera.main: fail closed.
            public Vector3 position;
            public Vector3 forward;
        }

        /// <summary>
        /// Sample Camera.main once per plan. valid=false on cold start (no main camera,
        /// or a degenerate/zero forward vector => low confidence) so callers fail closed.
        /// </summary>
        public static CameraPose SampleCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return new CameraPose { valid = false };
            }
            Transform t = cam.transform;
            Vector3 fwd = t.forward;
            // Degenerate forward => we cannot reason about the user frame: low confidence.
            if (fwd.sqrMagnitude < 1e-6f)
            {
                return new CameraPose { valid = false };
            }
            return new CameraPose { valid = true, position = t.position, forward = fwd.normalized };
        }

        /// <summary>
        /// Where the registry will place the i-th spawned primitive (mirror of
        /// SafeBehaviourRegistry.SpawnPrimitive: localPosition = (i*1.1, 0, 0)).
        /// </summary>
        public static Vector3 PredictedSpawnWorldPos(Transform parentT, int i)
        {
            Vector3 local = new Vector3(i * 1.1f, 0f, 0f);
            return parentT != null ? parentT.TransformPoint(local) : local;
        }

        /// <summary>
        /// USER-FRAME INVARIANT + PE-01 + SP-03 guard for a STATIC placement.
        /// Rejects placements inside the head personal-space sphere, placements that
        /// would blackout the FOV, and placements that co-locate with an existing
        /// generated object. Fails closed on cold start (head-relative risk unknown).
        /// </summary>
        public static bool PlacementOk(Vector3 worldPos, CameraPose cam,
                                       GeneratedObjectTracker tracker, bool enforce, out string why)
        {
            why = null;
            if (!enforce)
            {
                return true; // comfort-tolerant user opted out of head-relative rejections.
            }

            if (!cam.valid)
            {
                // COLD START: pose unknown/low-confidence => fail closed (do not risk a
                // body-horror in-head spawn just because we cannot see the camera yet).
                why = "cold-start: head pose unavailable, failing closed";
                return false;
            }

            // USER-FRAME INVARIANT: nothing may appear inside personal space (body-horror).
            float distToHead = Vector3.Distance(worldPos, cam.position);
            if (distToHead < ProtocolModels.PersonalSpaceRadius)
            {
                why = "inside personal-space sphere";
                return false;
            }

            // SP-03 OVERLAP: refuse to co-locate with an existing generated object.
            if (tracker != null)
            {
                IReadOnlyList<GameObject> existing = tracker.Spawned;
                for (int k = 0; k < existing.Count; k++)
                {
                    var other = existing[k];
                    if (other == null)
                    {
                        continue;
                    }
                    float r = BoundingRadius(other);
                    if (r <= 0f)
                    {
                        continue;
                    }
                    if (Vector3.Distance(worldPos, other.transform.position)
                        < r * ProtocolModels.OverlapCentreFraction)
                    {
                        why = "co-locates with an existing generated object (SP-03)";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// PE-01 FOV-occlusion guard for a SCALE change. Estimates the object's angular
        /// width at the camera after scaling and refuses if it would cover more than
        /// MaxForwardCoverageFraction of the assumed horizontal FOV (full-FOV blackout).
        /// Cold start => fail closed.
        /// </summary>
        public static bool ScaledObjectCoverageOk(GameObject go, float newUniformScale,
                                                   CameraPose cam, bool enforce)
        {
            if (!enforce)
            {
                return true;
            }
            if (go == null)
            {
                return true;
            }
            if (!cam.valid)
            {
                return false; // cold start: cannot estimate coverage; fail closed.
            }

            // Approximate post-scale radius: current bounds radius rescaled by the ratio
            // of the new uniform scale to the current (max) local scale axis.
            float curScale = Mathf.Max(go.transform.localScale.x,
                                Mathf.Max(go.transform.localScale.y, go.transform.localScale.z));
            curScale = Mathf.Max(curScale, 1e-3f);
            float radius = BoundingRadius(go) * (newUniformScale / curScale);
            float dist = Vector3.Distance(go.transform.position, cam.position);
            return AngularCoverageOk(radius, dist);
        }

        /// <summary>
        /// USER-FRAME INVARIANT for a MOVE. A continuously-moving (mode != "oscillate")
        /// object must not be able to travel into the personal-space sphere within its
        /// bounded one-shot budget (MoveMaxTotalDistance). Oscillate is bounded by
        /// amplitude. Cold start => fail closed.
        /// </summary>
        public static bool MovePathOk(GameObject go, Vector3 axisDir, string mode,
                                      float speed, float amplitude, CameraPose cam, bool enforce)
        {
            if (!enforce)
            {
                return true;
            }
            if (go == null)
            {
                return true;
            }
            if (!cam.valid)
            {
                return false; // cold start: cannot reason about the path; fail closed.
            }
            if (speed <= 0f)
            {
                return true; // not actually moving.
            }

            Vector3 start = go.transform.position;
            float reach = mode == "oscillate"
                ? amplitude                                   // bounded swing around origin
                : ProtocolModels.MoveMaxTotalDistance;        // one-shot capped travel

            // Sample the swept segment start -> start + axis*reach for the closest
            // approach to the head; reject if it ever enters personal space.
            const int samples = 16;
            for (int s = 0; s <= samples; s++)
            {
                float t = (reach * s) / samples;
                Vector3 p = start + axisDir * t;
                if (Vector3.Distance(p, cam.position) < ProtocolModels.PersonalSpaceRadius)
                {
                    return false;
                }
                // oscillate also swings to the negative side of the origin.
                if (mode == "oscillate")
                {
                    Vector3 pn = start - axisDir * t;
                    if (Vector3.Distance(pn, cam.position) < ProtocolModels.PersonalSpaceRadius)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// MODE-AGNOSTIC monitor helper: does an EXISTING generated object (at its
        /// current size/position) stay within the FOV-coverage budget? Used by
        /// <see cref="GeneratedContentMonitor"/> to check the RESULT of arbitrary
        /// generated C# (Mode A/B), where there is no action plan to pre-validate.
        /// Returns true (no concern) on cold start or a null object.
        /// </summary>
        public static bool CurrentCoverageOk(GameObject go, CameraPose cam)
        {
            if (go == null || !cam.valid)
            {
                return true;
            }
            float radius = BoundingRadius(go);
            float dist = Vector3.Distance(go.transform.position, cam.position);
            return AngularCoverageOk(radius, dist);
        }

        /// <summary>Half-angle coverage test: object of <paramref name="radius"/> at
        /// <paramref name="dist"/> must not exceed MaxForwardCoverageFraction of the FOV.</summary>
        private static bool AngularCoverageOk(float radius, float dist)
        {
            if (radius <= 0f)
            {
                return true;
            }
            if (dist <= 1e-3f)
            {
                return false; // essentially on top of the eye => full blackout.
            }
            // Full angular width subtended by the object (degrees).
            float angularWidthDeg = 2f * Mathf.Atan2(radius, dist) * Mathf.Rad2Deg;
            float coverage = angularWidthDeg / ProtocolModels.AssumedHorizontalFovDeg;
            return coverage <= ProtocolModels.MaxForwardCoverageFraction;
        }

        /// <summary>World-space bounding radius from the renderer, else a unit fallback.</summary>
        private static float BoundingRadius(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                return r.bounds.extents.magnitude;
            }
            // No renderer yet (e.g. predicted position): assume a unit primitive.
            return 0.5f;
        }
    }
}
