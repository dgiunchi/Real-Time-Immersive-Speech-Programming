using UnityEngine;

namespace AgenticXR.Study
{
    public sealed class L3MarkerPlacedDetector : StudySuccessDetector
    {
        public Rigidbody marker;
        public Collider[] pads;
        public StudyDoneButtonState doneButton;
        private Collider settledPad;
        private float restingSince = -1f;

        protected override void OnArmed()
        {
            settledPad = null;
            restingSince = -1f;
            doneButton?.ResetButton();
        }

        private void Update()
        {
            if (!IsArmed || HasFired || marker == null || pads == null) return;
            Collider currentPad = null;
            foreach (var pad in pads)
                if (pad != null && pad.bounds.Contains(marker.worldCenterOfMass)) { currentPad = pad; break; }
            if (currentPad == null || !IsAtRest(marker)) { settledPad = null; restingSince = -1f; return; }
            if (currentPad != settledPad) { settledPad = currentPad; restingSince = Time.unscaledTime; }
            if (Time.unscaledTime - restingSince < settleWindowSeconds || doneButton == null || !doneButton.WasPressed) return;
            FireOnce("{\"padId\":\"" + AgenticCache.AgenticSceneRegistry.Escape(settledPad.gameObject.name) + "\"}");
        }
    }
}
