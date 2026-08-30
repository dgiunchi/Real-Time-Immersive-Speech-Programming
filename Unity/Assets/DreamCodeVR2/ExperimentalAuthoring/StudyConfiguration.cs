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
        [Header("Ubiq Room Server")]
        [Tooltip("IP address or DNS name used by the existing Ubiq connection configuration.")]
        public string ubiqServerHost = "130.136.2.161";
        [Min(1), Tooltip("TCP Room Server port used by Ubiq.")]
        public int ubiqServerPort = 50000;

        [Header("Research Control")]
        public bool researcherMode;
        [Tooltip("Base URL for the Research Control HTTP API. Quest builds must use the reachable LAN address.")]
        public string researcherControlBaseUrl = "http://130.136.2.161:50001";
        [Header("PTT microphone")]
        [Range(1f,4f), Tooltip("Digital gain applied before PTT PCM16 serialization; does not affect playback.")]
        public float pttMicGain = 2f;
        [Header("Participant UX")]
        [Min(1f), Tooltip("Maximum time the participant UI remains in Processing while waiting for a relevant NID101 response.")]
        public float processingResponseTimeoutSeconds = 10f;
        [Header("Client logging")]
        public bool enableFileLogging = true;
        public bool logTranscripts = false;
        public bool verboseNetworkLogging = true;
        public string ExportJson() => JsonUtility.ToJson(this, true);
    }
}
