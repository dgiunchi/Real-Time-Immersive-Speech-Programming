using UnityEngine;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    public sealed class StudyDoneButtonState : MonoBehaviour
    {
        public bool WasPressed { get; private set; }
        public void Press() => WasPressed = true;
        public void ResetButton() => WasPressed = false;
        private void OnMouseDown() => Press();
    }
}
