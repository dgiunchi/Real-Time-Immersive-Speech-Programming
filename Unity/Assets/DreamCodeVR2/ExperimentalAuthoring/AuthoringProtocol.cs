using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public enum ExperimentCondition { VoiceCommandBaseline, PlayerAuthoring, DynamicStorytelling }
    public enum AuthoringStatus { Idle, Listening, Interpreting, Validating, AwaitingConfirmation, Applying, Applied, Rejected, Failed, Undone }
    public enum AuthoringActionKind { SET_PROPERTY, SET_AFFORDANCE, ADD_BEHAVIOR, CREATE_OBJECT, LINK_OBJECTS, RELOCATE_OBJECT, TOGGLE_STATE }

    [Serializable] public class AuthoringAction
    {
        [JsonProperty("action_id")] public string actionId;
        public AuthoringActionKind kind;
        [JsonProperty("target_object_id")] public string targetObjectId;
        [JsonProperty("secondary_object_id")] public string secondaryObjectId;
        public string operation;
        public string value;
        public float numericValue;
        public string anchorId;
        [JsonProperty("parameters")] public JObject parameters;
        [JsonProperty("api_call")] public ApiCall apiCall;
        public string behaviorId;
        public bool allowFinalGoalBypass;
    }
    [Serializable] public class ApiCall { public string api; public string method; }
    [Serializable] public class AuthoringProposal
    {
        public string proposalId;
        public string actionId;
        public string interpretation;
        public string targetDisplayName;
        public string expectedEffect;
        public string reason;
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
    [Serializable] public class AuthoringEnvelope { public string type; public AuthoringAction action; public PredefinedVoiceCommand command; public string command_id; public string action_id; public string reason; public NextTaskSpec task; public string task_id; public string status; public bool confirmation_required; public string interpretation; public string expected_effect; public string target_object_id; }
    [Serializable] public class PredefinedVoiceCommand
    {
        [JsonProperty("command_id")] public string commandId;
        [JsonProperty("target_object_id")] public string targetObjectId;
        [JsonProperty("intent")] public string command;
        [JsonProperty("preset_id")] public string preset;
        [JsonProperty("secondary_object_id")] public string secondaryObjectId;
        [JsonProperty("peer_uuid")] public string peerUuid;
        [JsonProperty("schema_version")] public string schemaVersion;
    }
    [Serializable] public class PredefinedCommandProposal { public string proposalId; public string interpretation; public string targetDisplayName; public string expectedEffect; public PredefinedVoiceCommand command; }
    [Serializable] public class NextTaskSpec
    {
        [JsonProperty("task_id")] public string taskId;
        public string title;
        [JsonProperty("player_instruction")] public string playerInstruction;
        [JsonProperty("task_type")] public string taskType;
        [JsonProperty("required_objects")] public string[] requiredObjects;
        [JsonProperty("success_conditions")] public RuntimeSuccessCondition[] successConditions;
        public string[] dependencies;
        [JsonProperty("protected_objects")] public string[] protectedObjects;
        [JsonProperty("allowed_authoring_scope")] public string[] allowedAuthoringScope;
        [JsonProperty("narrative_context")] public string narrativeContext;
    }
    [Serializable] public class RuntimeSuccessCondition { public string type; public string object_id; public string anchor_id; public string value; public RuntimeSuccessCondition[] children; }
}
