using System;
using System.Collections.Generic;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public enum ExperimentCondition { VoiceCommandBaseline, PlayerAuthoring, DynamicStorytelling }
    public enum AuthoringStatus { Idle, Listening, Interpreting, Validating, AwaitingConfirmation, Applying, Applied, Rejected, Failed, Undone }
    public enum AuthoringActionKind { SET_PROPERTY, SET_AFFORDANCE, ADD_BEHAVIOR, CREATE_OBJECT, LINK_OBJECTS, RELOCATE_OBJECT, TOGGLE_STATE }

    [Serializable] public class AuthoringAction
    {
        public string actionId;
        public AuthoringActionKind kind;
        public string targetObjectId;
        public string secondaryObjectId;
        public string operation;
        public string value;
        public float numericValue;
        public string anchorId;
        public string behaviorId;
        public bool allowFinalGoalBypass;
    }
    [Serializable] public class AuthoringProposal
    {
        public string proposalId;
        public string actionId;
        public string interpretation;
        public string targetDisplayName;
        public string expectedEffect;
        public string reason;
        public bool proactive;
        public AuthoringAction action;
    }
    [Serializable] public class AuthoringValidationError { public string code; public string message; public string field; }
    [Serializable] public class AuthoringExecutionRequest { public string requestId; public string peer; public AuthoringAction action; }
    [Serializable] public class AuthoringExecutionResult { public string actionId; public bool success; public string message; public AuthoringValidationError error; public long executionLatencyMs; }
    [Serializable] public class AuthoringUndoRequest { public string requestId; public string actionId; }
    [Serializable] public class AuthoringUndoResult { public string actionId; public bool success; public string message; }
    [Serializable] public class AuthoringAcknowledgement { public string type; public string peer; public string proposalId; public string actionId; public bool accepted; public string reason; }
    [Serializable] public class ExperimentEvent
    {
        public long timestamp; public string participantCode; public string sessionId; public string condition;
        public string questId; public string questVariant; public string taskId; public string eventType;
        public string[] objectIds; public string actionId; public bool success; public float latency; public string numericMetadata;
    }
    [Serializable] public class AuthoringEnvelope { public string type; public AuthoringProposal proposal; public AuthoringExecutionRequest execution; public AuthoringUndoRequest undo; public PredefinedVoiceCommand predefinedCommand; public NextTaskSpec nextTask; public SceneApiCall sceneApi; public BehaviorApiCall behaviorApi; }
    [Serializable] public class PredefinedVoiceCommand { public string commandId; public string targetObjectId; public string command; public string preset; }
    [Serializable] public class PredefinedCommandProposal { public string proposalId; public string interpretation; public string targetDisplayName; public string expectedEffect; public PredefinedVoiceCommand command; }
    [Serializable] public class NextTaskSpec { public string taskId; public string title; public string playerInstruction; public string taskType; public string[] requiredObjects; public RuntimeSuccessCondition[] successConditions; public string[] dependencies; public string[] protectedObjects; public string[] allowedAuthoringScope; public string narrativeContext; }
    [Serializable] public class RuntimeSuccessCondition { public string type; public string object_id; public string anchor_id; public string value; public RuntimeSuccessCondition[] children; }
}
