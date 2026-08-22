using AgenticCache;
using UnityEngine;

namespace AgenticXR.Study
{
    public sealed class L4DoorOpenedDetector : StudySuccessDetector
    {
        public L4TrialDoor door;
        public AgenticRegionVolume approachRegion;
        public float fullyOpenToleranceDegrees = 2f;
        private float conditionSince = -1f;

        protected override void OnArmed() => conditionSince = -1f;

        private void Update()
        {
            if (!IsArmed || HasFired || door == null || approachRegion == null || Camera.main == null) return;
            if (!door.IsFullyOpen(fullyOpenToleranceDegrees) || !approachRegion.Contains(Camera.main.transform.position))
            {
                conditionSince = -1f;
                return;
            }
            if (conditionSince < 0f) { conditionSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - conditionSince < settleWindowSeconds) return;
            FireOnce("{\"doorId\":\"" + AgenticSceneRegistry.Escape(door.gameObject.name) +
                "\",\"participantInsideApproachRegion\":true}");
        }
    }
}
