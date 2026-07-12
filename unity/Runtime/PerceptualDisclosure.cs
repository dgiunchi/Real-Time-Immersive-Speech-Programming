using System;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Agency-transparency channel (TRACK U2). The perceptual/embodied DETECTION
    /// guards (drift, vection, confinement, depth-mismatch) are MONITOR-ONLY: they
    /// never block free creation. When a detector trips, it raises a
    /// <see cref="PerceptualDisclosure"/> here instead of refusing the plan, so the
    /// manipulation is made VISIBLE and correctly attributed to the system rather
    /// than silently affecting the user (Tseng 2022 "perceived agency"; Krauss 2024
    /// covert-deception). The networked demo forwards these to the backend as a
    /// Safety event (NID 96) for the admin panel's safety log.
    ///
    /// Pure/IL2CPP-safe: a static event + a struct, no reflection, no IO/Net.
    /// </summary>
    public static class PerceptualDisclosure
    {
        /// <summary>One disclosure: which detector, a human-readable reason, a metric.</summary>
        public readonly struct Notice
        {
            public readonly string detector; // e.g. "drift", "vection", "confinement", "depth"
            public readonly string reason;   // human-readable, user-facing
            public readonly float metric;    // the measured value that tripped the threshold

            public Notice(string detector, string reason, float metric)
            {
                this.detector = detector;
                this.reason = reason;
                this.metric = metric;
            }
        }

        /// <summary>
        /// Raised whenever a monitor-only guard trips. Subscribers (the networked
        /// demo, a HUD indicator, tests) may forward/display it. Never throws to the
        /// guard: exceptions in subscribers are swallowed so a detector can never
        /// affect the scene.
        /// </summary>
        public static event Action<Notice> OnDisclosure;

        /// <summary>Raise a disclosure. Logged + dispatched. Never blocks, never throws.</summary>
        public static void Disclose(string detector, string reason, float metric)
        {
            // Visible by default so the manipulation is never silent.
            Debug.Log($"[PerceptualDisclosure:{detector}] {reason} (metric={metric:0.###})");
            var handlers = OnDisclosure;
            if (handlers == null)
            {
                return;
            }
            foreach (Action<Notice> h in handlers.GetInvocationList())
            {
                try
                {
                    h(new Notice(detector, reason, metric));
                }
                catch (Exception e)
                {
                    // A faulty subscriber must never disturb a monitor-only guard.
                    Debug.LogWarning($"[PerceptualDisclosure] subscriber threw, ignored: {e.Message}");
                }
            }
        }
    }
}
