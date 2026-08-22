using UnityEngine;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    public sealed class StudyNpcProxy : MonoBehaviour
    {
        public Vector3 fixedPosition;
        public float idlePhase;
        public float idleAmplitudeDegrees = 3f;

        private void Awake() => fixedPosition = transform.localPosition;
        private void Update()
        {
            transform.localPosition = fixedPosition;
            transform.localRotation = Quaternion.Euler(0f,
                Mathf.Sin(Time.unscaledTime * 0.7f + idlePhase) * idleAmplitudeDegrees, 0f);
        }
    }
}
