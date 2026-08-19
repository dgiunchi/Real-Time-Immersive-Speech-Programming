using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringCapabilities : MonoBehaviour
    {
        public string[] allowedOperations = { "SET_PROPERTY", "ADD_BEHAVIOR", "RELOCATE_OBJECT", "TOGGLE_STATE" };
        public string[] editableProperties = { "color", "visible", "active", "kinematic", "gravity_enabled", "scale" };
        public string[] allowedBehaviors = { "rotate_continuously", "blink" };
        public bool canMove = true; public bool canHide = true; public bool canDeactivate; public bool canLink = true;
        public bool questCritical; public bool changesRequireConfirmation = true;
        public string[] forbiddenAffordanceChanges;
        public string[] protectedProperties;
        public string[] allowedAuthoringOperations;
        public float minimumScale = 0.25f; public float maximumScale = 2f;
        public bool AllowsOperation(string operation) => Contains(allowedOperations, operation);
        public bool AllowsProperty(string property) => Contains(editableProperties, property);
        public bool AllowsBehavior(string behavior) => Contains(allowedBehaviors, behavior);
        private static bool Contains(string[] values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value)) return false;
            foreach (var item in values) if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
