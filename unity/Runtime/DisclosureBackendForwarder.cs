using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Opt-in OUT-OF-PROCESS transparency forwarder (Phase 6). Subscribes to the static
    /// <see cref="PerceptualDisclosure.OnDisclosure"/> channel, JSON-encodes each notice,
    /// and buffers it on a thread-safe queue. A host (the networked demo) drains
    /// <see cref="TryDequeue"/> on its main loop and enqueues the JSON onto its outbound
    /// control queue under <see cref="NidDisclosure"/>, so covert-manipulation detections
    /// are auditable OFF the headset (the admin panel's safety log) — outside the
    /// potentially-compromised in-process runtime.
    ///
    /// DEFAULT OFF: <see cref="forwardToBackend"/> is false, so this changes nothing
    /// until armed (legacy byte-identical). Unsubscribes in OnDisable/OnDestroy (static
    /// event). Uses a NEW network id (97) so it never collides with NID 96 (compile
    /// result) or NID 95 (feedback) despite the stale doc-comment on PerceptualDisclosure.
    ///
    /// ON-DEVICE PENDING: end-to-end wire delivery + admin-panel rendering need the
    /// backend + a headset; this component makes no runtime claim. The JSON encoder is
    /// verified in EditMode only.
    /// </summary>
    public sealed class DisclosureBackendForwarder : MonoBehaviour
    {
        /// <summary>Dedicated disclosure network id (NOT 95 feedback, NOT 96 compile).</summary>
        public const uint NidDisclosure = 97;

        [Tooltip("Forward disclosures to the backend safety log. DEFAULT OFF.")]
        public bool forwardToBackend = false;

        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();

        private void OnEnable()
        {
            PerceptualDisclosure.OnDisclosure += OnNotice;
        }

        private void OnDisable()
        {
            PerceptualDisclosure.OnDisclosure -= OnNotice;
        }

        private void OnNotice(PerceptualDisclosure.Notice n)
        {
            if (!forwardToBackend)
            {
                return;
            }

            _pending.Enqueue(EncodeNotice(n.detector, n.reason, n.metric));
        }

        /// <summary>
        /// Host drains this (main thread) and enqueues the JSON onto its own control-out
        /// queue as (<see cref="NidDisclosure"/>, json). Thread-safe.
        /// </summary>
        public bool TryDequeue(out string json)
        {
            return _pending.TryDequeue(out json);
        }

        /// <summary>
        /// Encode one notice as a compact, safety-log JSON object. Pure and total
        /// (IL2CPP-safe: manual escaping, no reflection). EditMode-testable.
        /// </summary>
        public static string EncodeNotice(string detector, string reason, float metric)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"type\":\"disclosure\",\"detector\":");
            AppendJsonString(sb, detector);
            sb.Append(",\"reason\":");
            AppendJsonString(sb, reason);
            sb.Append(",\"metric\":");
            sb.Append(metric.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }
            }

            sb.Append('"');
        }
    }
}
