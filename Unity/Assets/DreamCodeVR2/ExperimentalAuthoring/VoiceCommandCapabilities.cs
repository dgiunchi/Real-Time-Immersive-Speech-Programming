using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Explicit scene-authored command surface for C1. No component-name reflection is used.
    public class VoiceCommandCapabilities : MonoBehaviour
    {
        // Commands are advertised only by the bootstrap/scene configuration after it binds a
        // concrete controller. An unconfigured component must not expose a phantom verb.
        public string[] predefinedVoiceActions = Array.Empty<string>();
        // Presets are per-object and are exported in SceneContext only when a controller can
        // execute them. They are intentionally separate from voice verbs.
        public string[] predefinedPresets = Array.Empty<string>();
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
        public bool TryOpen(out string error) { if(drawer){var success=drawer.TryOpen(out error);if(success)IsOpen=true;return success;} error=null;IsOpen=true;if(openState)openState.SetActive(true);if(closedState)closedState.SetActive(false);return true; }
        public bool TryClose(out string error) { if(drawer){var success=drawer.TryClose(out error);if(success)IsOpen=false;return success;} error=null;IsOpen=false;if(openState)openState.SetActive(false);if(closedState)closedState.SetActive(true);return true; }
        public void Open() { TryOpen(out _); }
        public void Close() { TryClose(out _); }
        public void SetActiveState(bool active) { if(activeState)activeState.SetActive(active); else gameObject.SetActive(active); }
        public void MoveToPreset(string preset) { var destination=string.Equals(preset,"down",StringComparison.OrdinalIgnoreCase)?downPreset:upPreset; if(destination)transform.SetPositionAndRotation(destination.position,destination.rotation); }
        public void UseWith(GameObject other) { var state=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>(); state.state="used"; }
    }
}
