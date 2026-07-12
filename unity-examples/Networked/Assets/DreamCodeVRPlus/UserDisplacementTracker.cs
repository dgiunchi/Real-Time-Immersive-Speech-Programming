using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Cumulative user-displacement monitor (TRACK U2) — the detection answer to the
    /// redirection attacks whose harm is the INTEGRAL of many imperceptible nudges,
    /// not any single out-of-bounds value: Optical Marionette (Ishii 2016), the Human
    /// Joystick (Casey 2021), and Tseng's (2022) walking-puppetry.
    ///
    /// Mode C has no verb that can move the user's frame, so this cannot be the
    /// ATTACKER here — its job is to MONITOR. After a plan that animates/moves content
    /// is applied, we watch the real head pose for a short window; if the user's body
    /// drifts more than <see cref="ProtocolModels.UserDriftDisclosureThresholdM"/> while
    /// that content is in motion, we DISCLOSE it (the user may be being herded). It is
    /// strictly MONITOR-ONLY: it never rejects a plan and never restricts creation, so
    /// a user simply walking around their room is free — the disclosure just makes any
    /// correlation between content motion and body drift visible and attributable.
    ///
    /// Pure/IL2CPP-safe: no reflection, no IO/Net.
    /// </summary>
    public sealed class UserDisplacementTracker : MonoBehaviour
    {
        // How long (seconds) after a content-motion plan we watch for herding.
        private const float WatchWindowSeconds = 8f;

        // When false the monitor still tracks session telemetry silently but does not
        // raise disclosures (kept quiet during ordinary free creation). The executor
        // sets this from comfortIntensity so monitoring is opt-in like the other guards.
        public bool discloseEnabled;

        private bool _watching;
        private bool _disclosedThisWindow;
        private float _watchUntil;
        private Vector3 _watchBaseline;
        private string _watchLabel = "";

        // Session telemetry (total path length the head has travelled this session).
        private bool _hasLast;
        private Vector3 _lastSample;
        private float _sessionPathLength;

        /// <summary>Total distance the head has travelled this session (telemetry).</summary>
        public float SessionPathLength => _sessionPathLength;

        /// <summary>True while inside an active herding-watch window.</summary>
        public bool IsWatching => _watching;

        /// <summary>
        /// Called by the executor when a plan that moves/animates content is applied.
        /// Opens (or refreshes) a watch window anchored at the current head position.
        /// </summary>
        public void NotifyContentMotion(string label)
        {
            var cam = PerceptualSafety.SampleCamera();
            if (!cam.valid)
            {
                return; // no head pose to anchor against; nothing to watch.
            }
            _watching = true;
            _disclosedThisWindow = false;
            _watchUntil = Time.time + WatchWindowSeconds;
            _watchBaseline = cam.position;
            _watchLabel = string.IsNullOrEmpty(label) ? "content motion" : label;
        }

        private void Update()
        {
            var cam = PerceptualSafety.SampleCamera();
            if (!cam.valid)
            {
                return;
            }

            // Session path-length telemetry (always on, silent).
            if (_hasLast)
            {
                _sessionPathLength += Vector3.Distance(cam.position, _lastSample);
            }
            _lastSample = cam.position;
            _hasLast = true;

            if (!_watching)
            {
                return;
            }
            if (Time.time > _watchUntil)
            {
                _watching = false;
                return;
            }

            float drift = Vector3.Distance(cam.position, _watchBaseline);
            if (!_disclosedThisWindow && discloseEnabled
                && drift > ProtocolModels.UserDriftDisclosureThresholdM)
            {
                _disclosedThisWindow = true; // one disclosure per window (no spam).
                PerceptualDisclosure.Disclose(
                    "drift",
                    $"user moved {drift:0.00} m while '{_watchLabel}' was animating " +
                    "(possible herding; disclosed, not blocked)",
                    drift);
            }
        }
    }
}
