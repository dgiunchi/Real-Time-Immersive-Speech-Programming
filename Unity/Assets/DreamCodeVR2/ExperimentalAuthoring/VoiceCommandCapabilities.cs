using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Explicit scene-authored command surface for C1. No component-name reflection is used.
    public class VoiceCommandCapabilities : MonoBehaviour
    {
        public string[] predefinedVoiceActions = { "OPEN", "CLOSE", "ACTIVATE", "DEACTIVATE", "MOVE_TO_PRESET", "USE_WITH" };
        public PredefinedVoiceCommandTarget target;
        public bool Allows(string command)
        {
            if (predefinedVoiceActions == null) return false;
            foreach (var allowed in predefinedVoiceActions) if (string.Equals(allowed, command, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    // Assign this adapter in the Inspector to bind the intentionally limited command verbs to a scene object.
    public class PredefinedVoiceCommandTarget : MonoBehaviour
    {
        public GameObject openState; public GameObject closedState; public GameObject activeState;
        public Transform upPreset; public Transform downPreset; public bool IsOpen { get; private set; }
        public ExperimentalDrawerController drawer;
        public void Open() { if(drawer){drawer.Open();IsOpen=true;return;} IsOpen = true; if(openState)openState.SetActive(true); if(closedState)closedState.SetActive(false); }
        public void Close() { if(drawer){drawer.Close();IsOpen=false;return;} IsOpen = false; if(openState)openState.SetActive(false); if(closedState)closedState.SetActive(true); }
        public void SetActiveState(bool active) { if(activeState)activeState.SetActive(active); else gameObject.SetActive(active); }
        public void MoveToPreset(string preset) { var destination=string.Equals(preset,"down",StringComparison.OrdinalIgnoreCase)?downPreset:upPreset; if(destination)transform.SetPositionAndRotation(destination.position,destination.rotation); }
        public void UseWith(GameObject other) { var state=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>(); state.state="used"; }
    }
}
