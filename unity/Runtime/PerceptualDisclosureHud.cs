using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// PURE, engine-independent aggregator for <see cref="PerceptualDisclosure.Notice"/>
    /// events (Phase 6). Bounded ring buffer + rapid-repeat coalescing, so a monitor
    /// that fires every frame (e.g. drift/vection) becomes ONE entry with a running
    /// count instead of flooding the surface. No UnityEngine dependency, so it is
    /// deterministically EditMode-testable without a device.
    ///
    /// This is the missing CONSUMER for the existing (producer-only)
    /// <see cref="PerceptualDisclosure"/> channel: it turns Debug.Log-only detections
    /// into a structured, user-facing / auditable feed (Tseng 2022 perceived-agency;
    /// Krauss 2024 covert-deception). Rendering + off-device delivery are separate,
    /// on-device-pending concerns.
    /// </summary>
    public sealed class DisclosureFeed
    {
        public readonly struct Entry
        {
            public readonly string detector;
            public readonly string reason;
            public readonly float metric;
            public readonly int count; // how many coalesced occurrences this entry represents

            public Entry(string detector, string reason, float metric, int count)
            {
                this.detector = detector;
                this.reason = reason;
                this.metric = metric;
                this.count = count;
            }
        }

        private readonly Entry[] _ring;
        private int _head; // next write index
        private int _size;
        private long _lastMs;
        private readonly long _coalesceWindowMs;

        public DisclosureFeed(int capacity = 16, long coalesceWindowMs = 750)
        {
            _ring = new Entry[capacity < 1 ? 1 : capacity];
            _coalesceWindowMs = coalesceWindowMs;
        }

        public int Count => _size;

        /// <summary>
        /// Record a disclosure. If it repeats the most-recent entry's detector within
        /// the coalesce window, the last entry's count is incremented (and its reason/
        /// metric updated to the latest) instead of appending a new one.
        /// </summary>
        public void Push(string detector, string reason, float metric, long nowMs)
        {
            if (_size > 0)
            {
                int lastIdx = (_head - 1 + _ring.Length) % _ring.Length;
                Entry last = _ring[lastIdx];
                if (last.detector == detector && nowMs - _lastMs <= _coalesceWindowMs)
                {
                    _ring[lastIdx] = new Entry(detector, reason, metric, last.count + 1);
                    _lastMs = nowMs;
                    return;
                }
            }

            _ring[_head] = new Entry(detector, reason, metric, 1);
            _head = (_head + 1) % _ring.Length;
            if (_size < _ring.Length)
            {
                _size++;
            }

            _lastMs = nowMs;
        }

        /// <summary>Entries newest-first (most recent disclosure at index 0).</summary>
        public List<Entry> Recent()
        {
            var outList = new List<Entry>(_size);
            for (int i = 0; i < _size; i++)
            {
                int idx = (_head - 1 - i + _ring.Length * 2) % _ring.Length;
                outList.Add(_ring[idx]);
            }

            return outList;
        }

        public void Clear()
        {
            _head = 0;
            _size = 0;
            _lastMs = 0;
        }
    }

    /// <summary>
    /// Opt-in HUD that renders the <see cref="DisclosureFeed"/> as a simple on-screen
    /// log so covert-manipulation detections are made VISIBLE to the user.
    ///
    /// DEFAULT OFF: <see cref="showHud"/> is false, so adding this component changes
    /// nothing until it is armed (legacy byte-identical). It subscribes to the STATIC
    /// <see cref="PerceptualDisclosure.OnDisclosure"/> event in OnEnable and — critically
    /// — UNSUBSCRIBES in OnDisable/OnDestroy so it cannot leak across in-editor Play
    /// sessions.
    ///
    /// ON-DEVICE PENDING: a proper head-locked, world-space HUD needs a headset; this
    /// OnGUI overlay is a functional placeholder and makes no runtime claim.
    /// </summary>
    public sealed class PerceptualDisclosureHud : MonoBehaviour
    {
        [Tooltip("Render the disclosure overlay. DEFAULT OFF = no visual change.")]
        public bool showHud = false;

        [Tooltip("How many recent disclosures to keep/show.")]
        public int capacity = 16;

        private DisclosureFeed _feed;

        private void Awake()
        {
            _feed = new DisclosureFeed(capacity);
        }

        private void OnEnable()
        {
            PerceptualDisclosure.OnDisclosure += OnNotice;
        }

        private void OnDisable()
        {
            // MUST unsubscribe (static event) so a leaked subscription does not persist
            // across Play sessions and double-render.
            PerceptualDisclosure.OnDisclosure -= OnNotice;
        }

        private void OnNotice(PerceptualDisclosure.Notice n)
        {
            _feed?.Push(n.detector, n.reason, n.metric, NowMs());
        }

        private static long NowMs()
        {
            return (long)(Time.realtimeSinceStartup * 1000f);
        }

        private void OnGUI()
        {
            if (!showHud || _feed == null || _feed.Count == 0)
            {
                return;
            }

            const int w = 460;
            int y = 8;
            GUI.Box(new Rect(6, 6, w, 20 + _feed.Count * 18), "System disclosures");
            y += 22;
            foreach (DisclosureFeed.Entry e in _feed.Recent())
            {
                string suffix = e.count > 1 ? $" (x{e.count})" : string.Empty;
                GUI.Label(new Rect(12, y, w - 12, 18), $"[{e.detector}] {e.reason}{suffix}");
                y += 18;
            }
        }
    }
}
