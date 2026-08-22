using UnityEngine;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    public sealed class L4TrialDoor : MonoBehaviour
    {
        public bool trialLocal = true;
        public bool persistent = false;
        public bool offEgressPath = true;
        public bool participantLocomotionAllowed = false;
        public int scriptedNpcProxyCount = 2;
        public Vector3 closedLocalEuler;
        public Vector3 fullyOpenLocalEuler = new Vector3(0f, 90f, 0f);

        public void ResetClosed() => transform.localRotation = Quaternion.Euler(closedLocalEuler);
        public bool IsFullyOpen(float toleranceDegrees) =>
            Quaternion.Angle(transform.localRotation, Quaternion.Euler(fullyOpenLocalEuler)) <= toleranceDegrees;
    }
}
