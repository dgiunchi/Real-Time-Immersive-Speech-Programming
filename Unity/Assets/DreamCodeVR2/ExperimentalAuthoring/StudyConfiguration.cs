using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    [CreateAssetMenu(menuName = "DreamCodeVR2/Study Configuration", fileName = "StudyConfiguration")]
    public class StudyConfiguration : ScriptableObject
    {
        public ExperimentCondition condition = ExperimentCondition.PlayerAuthoring;
        public string[] allowedPredefinedCommands = { "OPEN", "CLOSE" };
        public string[] allowedOperations = { "setProperty", "setAffordance", "createObject", "relocateObject", "setSemanticState", "rotate_continuously", "blink", "activate" };
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
        [Header("Researcher tooling")]
        public bool researcherMode;
        [Tooltip("Editor defaults to localhost. Quest builds must use the LAN address of the development PC.")]
        public string researcherControlBaseUrl = "http://130.136.2.161:50001";
        public string ExportJson() => JsonUtility.ToJson(this, true);
    }
}
