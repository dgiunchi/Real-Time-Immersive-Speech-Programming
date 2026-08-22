using UnityEngine;

namespace AgenticXR.Study
{
    public sealed class L5SequenceCompletedDetector : StudySuccessDetector
    {
        public Transform[] sequenceMarkers;
        public float positionToleranceMeters = 0.02f;
        private Vector3[] startPositions;
        private bool observedDeparture;
        private float resetSince = -1f;

        protected override void OnArmed()
        {
            observedDeparture = false;
            resetSince = -1f;
            startPositions = sequenceMarkers == null ? new Vector3[0] : new Vector3[sequenceMarkers.Length];
            for (var index = 0; index < startPositions.Length; index++)
                startPositions[index] = sequenceMarkers[index] != null ? sequenceMarkers[index].localPosition : Vector3.zero;
        }

        private void Update()
        {
            if (!IsArmed || HasFired || sequenceMarkers == null || startPositions == null) return;
            var allAtStart = true;
            for (var index = 0; index < sequenceMarkers.Length; index++)
            {
                if (sequenceMarkers[index] == null) continue;
                if (Vector3.Distance(sequenceMarkers[index].localPosition, startPositions[index]) <= positionToleranceMeters) continue;
                allAtStart = false;
                observedDeparture = true;
            }
            if (!observedDeparture || !allAtStart) { resetSince = -1f; return; }
            if (resetSince < 0f) { resetSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - resetSince >= settleWindowSeconds)
                FireOnce("{\"sequenceCompletedAndReset\":true}");
        }
    }
}
