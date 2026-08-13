// DreamCodeVR+ — "is it actually listening?"
//
// Pressing a trigger and then waiting in silence is the worst moment in the whole
// experience: the wearer cannot tell whether the microphone opened, whether the words were
// heard, or whether anything is happening at all, so they let go and try again — which
// cancels the utterance that was working. The fix is not more logging. It is telling them,
// immediately, in the headset.
//
// THIS IS UI, AND UI MAY FOLLOW THE HEAD.
// That is the one deliberate exception to the rule the rest of this project enforces. A
// status readout you have to go and look for is useless, so this sits at a fixed offset
// from the camera. CREATED CONTENT MAY NEVER DO THIS — the distinction is not stylistic,
// it is the difference between an interface and a hallucination, and keeping the two in
// separate classes is how it stays true.
//
// Stereo-safe by the same rule as everything else after the P0 defect: URP materials only,
// obtained through DcvrMaterials, never a shader picked by name and hoped for.
//
// It is small, high and slightly right — out of the creative area, unmistakable when it
// changes, and gone a couple of seconds after the work finishes.

using System.Collections;
using System.Text;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>What the pipeline is doing right now. Only states that ACTUALLY occur are
    /// ever shown: a deterministic edit never displays GENERATING, because no model was
    /// asked anything, and claiming otherwise would misrepresent the system to the person
    /// evaluating it.</summary>
    public enum DcvrVoiceState
    {
        Idle,
        Recording,
        Transcribing,
        Routing,
        Generating,
        Validating,
        Compiling,
        Executing,
        Done,
        Blocked,
        Error,
    }

    public sealed class DcvrVoiceOverlay : MonoBehaviour
    {
        public static DcvrVoiceOverlay Instance { get; private set; }

        // Head-relative placement. Far enough to focus comfortably, high and right so the
        // creation area stays clear.
        private static readonly Vector3 Offset = new Vector3(0.16f, 0.20f, 0.85f);
        private const float PanelWidth = 0.30f;
        private const float PanelHeight = 0.085f;
        private const float FollowLerp = 8f;
        private const float DoneHold = 2.2f;

        private Transform _panel;
        private Renderer _plate;
        private Material _plateMat;
        private object _line1;   // state
        private object _line2;   // detail

        private DcvrVoiceState _state = DcvrVoiceState.Idle;
        private float _recordStart;
        private float _hideAt;

        /// <summary>Longest the overlay may claim to be recording before it stops believing
        /// itself.
        ///
        /// The display previously depended on some LATER event to move it out of Recording,
        /// and when that event did not arrive on the voice path it sat there indefinitely.
        /// A recording indicator that can be wrong about recording is worse than none, so
        /// the state now expires on its own. This is a backstop, not the mechanism — the
        /// trigger release clears it directly — but a backstop is exactly what an indicator
        /// with a privacy meaning should have.
        ///
        /// Comfortably longer than the capture cap, so it can never pre-empt a legitimate
        /// long hold.</summary>
        private const float RecordingWatchdogSeconds = 20f;

        // Rebuilt only when the displayed second actually changes. A string built every
        // frame is 72 allocations a second for a readout that ticks once (§73, §45).
        private int _lastShownTenth = -1;
        private readonly StringBuilder _sb = new StringBuilder(16);

        public static DcvrVoiceOverlay Ensure()
        {
            if (Instance != null) { return Instance; }
            var go = new GameObject("DCVR_VoiceOverlay");
            go.transform.SetParent(null, true);
            Instance = go.AddComponent<DcvrVoiceOverlay>();
            Instance.Build();
            return Instance;
        }

        private void Build()
        {
            var panel = new GameObject("VoicePanel");
            panel.transform.SetParent(transform, false);
            _panel = panel.transform;

            GameObject plate = DcvrPrim.Create(PrimitiveType.Quad, "VoicePlate");
            plate.transform.SetParent(_panel, false);
            plate.transform.localScale = new Vector3(PanelWidth, PanelHeight, 1f);
            _plateMat = DcvrMaterials.Make(new Color(0.02f, 0.035f, 0.055f));
            _plate = plate.GetComponent<Renderer>();
            _plate.sharedMaterial = _plateMat;

            _line1 = DcvrText.Make(_panel, "", new Vector3(0f, 0.016f, -0.005f), 0.022f, Color.white);
            _line2 = DcvrText.Make(_panel, "", new Vector3(0f, -0.018f, -0.005f), 0.015f, DcvrWorld.Dim);

            SetActive(false);
        }

        private void SetActive(bool on)
        {
            if (_panel != null) { _panel.gameObject.SetActive(on); }
        }

        // ---- the state machine (§33) -----------------------------------------------

        /// <summary>Enter a state. `detail` is the human-readable half — a transcript, a
        /// refusal reason, what was built.</summary>
        public void Set(DcvrVoiceState state, string detail = "")
        {
            _state = state;
            SetActive(state != DcvrVoiceState.Idle);

            if (state == DcvrVoiceState.Recording)
            {
                _recordStart = Time.unscaledTime;
                _lastShownTenth = -1;
            }

            // Terminal states linger briefly, then clear themselves. A success banner that
            // stays forever becomes part of the scenery and stops being read.
            _hideAt = (state == DcvrVoiceState.Done
                       || state == DcvrVoiceState.Blocked
                       || state == DcvrVoiceState.Error)
                ? Time.unscaledTime + DoneHold
                : 0f;

            SetText(_line1, Label(state));
            SetText(_line2, Trim(detail));
            Tint(ColorFor(state));
        }

        public void Recording() => Set(DcvrVoiceState.Recording);
        public void Idle() => Set(DcvrVoiceState.Idle);

        private static string Label(DcvrVoiceState s)
        {
            switch (s)
            {
                case DcvrVoiceState.Recording: return "● RECORDING";
                case DcvrVoiceState.Transcribing: return "TRANSCRIBING…";
                case DcvrVoiceState.Routing: return "UNDERSTANDING…";
                case DcvrVoiceState.Generating: return "GENERATING…";
                case DcvrVoiceState.Validating: return "VALIDATING…";
                case DcvrVoiceState.Compiling: return "BUILDING…";
                case DcvrVoiceState.Executing: return "BUILDING…";
                case DcvrVoiceState.Done: return "✓ DONE";
                case DcvrVoiceState.Blocked: return "BLOCKED";
                case DcvrVoiceState.Error: return "PROBLEM";
                default: return "";
            }
        }

        private static Color ColorFor(DcvrVoiceState s)
        {
            switch (s)
            {
                case DcvrVoiceState.Recording: return new Color(0.95f, 0.25f, 0.30f);
                case DcvrVoiceState.Done: return DcvrWorld.Green;
                case DcvrVoiceState.Blocked: return DcvrWorld.Red;
                case DcvrVoiceState.Error: return new Color(0.98f, 0.62f, 0.15f);
                default: return DcvrWorld.Cyan;
            }
        }

        private void Tint(Color c)
        {
            if (_plateMat == null) { return; }
            Color bg = new Color(c.r * 0.16f, c.g * 0.16f, c.b * 0.16f, 1f);
            if (_plateMat.HasProperty("_BaseColor")) { _plateMat.SetColor("_BaseColor", bg); }
            if (_plateMat.HasProperty("_Color")) { _plateMat.SetColor("_Color", bg); }
        }

        private void Update()
        {
            Camera cam = Camera.main;
            if (cam == null) { return; }

            // Head-relative, smoothed. Rigidly locking it to the head makes a panel feel
            // welded to the face and is a reliable way to produce discomfort; lagging
            // slightly lets the eyes lead and the panel settle.
            Vector3 want = cam.transform.TransformPoint(Offset);
            if (_panel.gameObject.activeSelf)
            {
                _panel.position = Vector3.Lerp(_panel.position, want, Time.deltaTime * FollowLerp);
                _panel.rotation = Quaternion.Slerp(_panel.rotation, cam.transform.rotation,
                                                   Time.deltaTime * FollowLerp);
            }
            else
            {
                _panel.SetPositionAndRotation(want, cam.transform.rotation);
            }

            if (_state == DcvrVoiceState.Recording)
            {
                // Tenths, and only rebuilt when the tenth changes.
                int tenth = (int)((Time.unscaledTime - _recordStart) * 10f);
                if (tenth != _lastShownTenth)
                {
                    _lastShownTenth = tenth;
                    _sb.Clear();
                    _sb.Append(tenth / 10).Append('.').Append(tenth % 10).Append('s');
                    SetText(_line2, _sb.ToString());
                }
            }
            else if (_hideAt > 0f && Time.unscaledTime > _hideAt)
            {
                _hideAt = 0f;
                Set(DcvrVoiceState.Idle);
            }

            // Watchdog: nothing may leave the overlay asserting that the microphone is open.
            if (_state == DcvrVoiceState.Recording
                && Time.unscaledTime - _recordStart > RecordingWatchdogSeconds)
            {
                Debug.LogWarning("[DcvrVoiceOverlay] recording state expired without a "
                                 + "release — clearing (the microphone is bounded separately)");
                Set(DcvrVoiceState.Error, "recording ended");
            }
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            s = s.Replace('\n', ' ').Trim();
            return s.Length <= 44 ? s : s.Substring(0, 43) + "…";
        }

        private static void SetText(object handle, string value)
        {
            DcvrText.SetText(handle, value);
        }
    }
}
