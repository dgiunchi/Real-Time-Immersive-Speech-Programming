using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    [CreateAssetMenu(menuName = "DreamCodeVR2/Study Configuration", fileName = "StudyConfiguration")]
    public class StudyConfiguration : ScriptableObject
    {
        public ExperimentCondition condition = ExperimentCondition.PlayerAuthoring;
        public string[] allowedPredefinedCommands = { "OPEN", "CLOSE", "ACTIVATE", "DEACTIVATE", "MOVE_TO_PRESET", "USE_WITH" };
        public string[] allowedOperations = { "color", "visible", "active", "kinematic", "gravity_enabled", "scale", "rotate_continuously", "move_between_anchors", "blink", "follow_target", "cube", "sphere", "bridge_segment", "platform" };
        public bool requireConfirmation = true;
        public int maximumGeneratedObjects = 4;
        public int maximumActiveBehaviors = 8;
        public int maximumProposalsPerTask = 2;
        public float taskTimeThresholdSeconds = 120f;
        public int incorrectAttemptThreshold = 3;
        public int hintThreshold = 2;
        public bool undoAvailable = true;
        public bool modificationsAllowed = true;
        public bool cosmeticActionsEnabled = true;
        public bool directTaskCompletionForbidden = true;
        public string[] allowedAffordances = { "grabbable", "movable", "interactable", "gravity_enabled", "kinematic", "collision_enabled" };
        public float taskGenerationTimeoutSeconds = 15f;
        public string taskGenerationFallback = "end_playthrough";
        public bool allowProactiveAuthoringProposals = false;
        public string ExportJson() => JsonUtility.ToJson(this, true);
    }
}
