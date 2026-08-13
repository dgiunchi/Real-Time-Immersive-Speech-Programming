// DreamCodeVR+ — measured frame timing.
//
// Exists so performance can be REPORTED rather than asserted. The display refresh rate is
// easy to read from the OS and says nothing about whether the application is keeping up
// with it; the number that matters is how long our frames actually take.
//
// Reports median and 99th percentile over a rolling window, because a mean hides exactly
// the thing that matters in VR — the occasional long frame that causes a visible judder.

using System;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrPerf : MonoBehaviour
    {
        private const int Window = 240;       // ~3 s at 72 Hz
        private const float ReportEvery = 10f;

        private readonly float[] _frames = new float[Window];
        private int _count;
        private int _next;
        private float _timer;

        public static void Run() => new GameObject("DCVR_Perf").AddComponent<DcvrPerf>();

        private void Update()
        {
            _frames[_next] = Time.unscaledDeltaTime;
            _next = (_next + 1) % Window;
            if (_count < Window) { _count++; }

            _timer += Time.unscaledDeltaTime;
            if (_timer < ReportEvery || _count < Window) { return; }
            _timer = 0f;

            var sorted = new float[_count];
            Array.Copy(_frames, sorted, _count);
            Array.Sort(sorted);

            float median = sorted[_count / 2] * 1000f;
            float p99 = sorted[Mathf.Min(_count - 1, (int)(_count * 0.99f))] * 1000f;
            float worst = sorted[_count - 1] * 1000f;

            Debug.Log($"[DcvrPerf] frame ms  median={median:F2}  p99={p99:F2}  worst={worst:F2}" +
                      $"  =>  {1000f / median:F1} fps median, {1000f / p99:F1} fps at p99");
        }
    }
}
