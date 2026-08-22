using Ubiq.XR;
using UnityEngine;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StudyDoneButtonState))]
    public sealed class StudyDoneButtonUseable : MonoBehaviour, IUseable
    {
        private StudyDoneButtonState state;

        private void Awake() => state = GetComponent<StudyDoneButtonState>();

        public void Use(Hand controller)
        {
            if (state == null) state = GetComponent<StudyDoneButtonState>();
            state.Press();
        }

        public void UnUse(Hand controller) { }
    }
}
