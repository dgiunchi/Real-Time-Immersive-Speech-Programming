using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// MODE-AGNOSTIC perceptual monitor (TRACK U2). The Mode-C executor validates an
    /// action plan BEFORE it touches the scene; but arbitrary generated C# (Mode A)
    /// builds objects directly, so the same perceptual concerns must be checked on the
    /// RESULT. After a compile (Mode A) or plan (Mode B), the host calls
    /// <see cref="ScanGenerated"/>; this walks every provenance-marked
    /// (<see cref="GeneratedMarker"/>) object and DISCLOSES any that:
    ///   - sits inside the head personal-space sphere (body-horror; Tseng user-frame),
    ///   - blacks out the forward FOV (PE-01 occlusion; Casey overlay),
    ///   - leaves the AdCube-style confinement region (Lee 2021 blind-spot staging),
    ///   - wraps/co-locates with a real object (Fujita wrapping; needs a registry),
    ///   - shields a real object at an inconsistent depth (Fujita shielding cue).
    ///
    /// It is strictly MONITOR-ONLY: it NEVER destroys, moves, or blocks generated
    /// content. It only raises <see cref="PerceptualDisclosure"/> notices so any
    /// concern is made visible and attributable — creation is never restricted, which
    /// is what preserves full creative freedom in Mode A/B. Arm it (opt-in) by setting
    /// <see cref="discloseEnabled"/> (mirror of the executor's comfortIntensity).
    ///
    /// IL2CPP-safe: scene queries here are FIRST-PARTY/trusted (not generated code).
    /// </summary>
    public sealed class GeneratedContentMonitor : MonoBehaviour
    {
        [Tooltip("Arm disclosures (mirror of comfortIntensity). Default false = quiet " +
                 "during ordinary free creation; monitoring never blocks regardless.")]
        public bool discloseEnabled;

        [Tooltip("Optional root for the confinement-region check; falls back to this object.")]
        public Transform confinementRoot;

        [Tooltip("Optional authentic-object registry for wrapping/shield detection (Fujita).")]
        public AuthenticObjectRegistry authentic;

        [Tooltip("Optional drift monitor; ScanGenerated() arms its herding watch when " +
                 "generated content is in motion.")]
        public UserDisplacementTracker drift;

        /// <summary>
        /// Scan all provenance-marked generated objects once. Call after a Mode-A/B
        /// compile or a Mode-C plan. Cheap; safe to call per command. Never blocks.
        /// </summary>
        public void ScanGenerated(bool contentInMotion = false)
        {
            if (drift != null && contentInMotion)
            {
                drift.discloseEnabled = discloseEnabled;
                drift.NotifyContentMotion("generated content");
            }
            if (!discloseEnabled)
            {
                return; // monitoring stays quiet during ordinary free creation.
            }

            var cam = PerceptualSafety.SampleCamera();
            Vector3 rootPos = confinementRoot != null ? confinementRoot.position : transform.position;

            List<GameObject> generated = CollectGenerated();
            for (int i = 0; i < generated.Count; i++)
            {
                var go = generated[i];
                if (go == null)
                {
                    continue;
                }

                // Personal-space intrusion (body-horror).
                if (cam.valid)
                {
                    float dHead = Vector3.Distance(go.transform.position, cam.position);
                    if (dHead < ProtocolModels.PersonalSpaceRadius)
                    {
                        PerceptualDisclosure.Disclose(
                            "personal-space",
                            $"generated '{go.name}' is inside personal space",
                            dHead);
                    }

                    // FOV blackout.
                    if (!PerceptualSafety.CurrentCoverageOk(go, cam))
                    {
                        PerceptualDisclosure.Disclose(
                            "fov",
                            $"generated '{go.name}' covers most of the forward view",
                            1f);
                    }
                }

                // AdCube-style confinement (blind-spot / out-of-region staging).
                float dRoot = Vector3.Distance(go.transform.position, rootPos);
                if (dRoot > ProtocolModels.ConfinementRegionRadiusM)
                {
                    PerceptualDisclosure.Disclose(
                        "confinement",
                        $"generated '{go.name}' is staged {dRoot:0.0} m from the build root",
                        dRoot);
                }

                // Reality-awareness: wrapping / shielding of an authentic object.
                if (authentic != null)
                {
                    var wrapped = authentic.OverlappedBy(go);
                    if (wrapped != null)
                    {
                        PerceptualDisclosure.Disclose(
                            "disguise",
                            $"generated '{go.name}' wraps/co-locates authentic '{wrapped.name}' (Fujita)",
                            0f);
                    }
                    var shielded = authentic.ShieldsAuthentic(go, cam);
                    if (shielded != null)
                    {
                        PerceptualDisclosure.Disclose(
                            "depth",
                            $"generated '{go.name}' shields authentic '{shielded.name}' at a different depth (Fujita)",
                            0f);
                    }
                }
            }
        }

        private static List<GameObject> CollectGenerated()
        {
            var list = new List<GameObject>();
            var markers = Object.FindObjectsByType<GeneratedMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null)
                {
                    list.Add(markers[i].gameObject);
                }
            }
            return list;
        }
    }
}
