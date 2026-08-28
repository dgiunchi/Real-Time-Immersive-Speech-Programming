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
    [Serializable] public class AuthoringEnvelope { public string type; public AuthoringAction action; public PredefinedVoiceCommand command; public string command_id; public string action_id; public string reason; public ServerNextTaskDto task; public string task_id; public string status; public bool confirmation_required; public string interpretation; public string expected_effect; public string target_object_id; }
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
    // Server wire contract: success conditions are canonical strings, not runtime objects.
    [Serializable] public class ServerNextTaskDto
    {
        [JsonProperty("task_id")] public string task_id;
        public string title;
        [JsonProperty("player_instruction")] public string player_instruction;
        [JsonProperty("task_type")] public string task_type;
        [JsonProperty("required_objects")] public string[] required_objects;
        [JsonProperty("success_conditions")] public string[] success_conditions;
        public string[] dependencies;
        [JsonProperty("protected_objects")] public string[] protected_objects;
        [JsonProperty("allowed_authoring_scope")] public string[] allowed_authoring_scope;
        [JsonProperty("narrative_context")] public string narrative_context;
    }
    public static class NextTaskWireConverter
    {
        public static bool TryConvert(ServerNextTaskDto wire, out NextTaskSpec runtime, out string error)
        {
            runtime=null; error=null;
            if(wire==null||string.IsNullOrWhiteSpace(wire.task_id)||string.IsNullOrWhiteSpace(wire.player_instruction)){error="Generated task is incomplete.";return false;}
            var conditions=wire.success_conditions??Array.Empty<string>(); var converted=new RuntimeSuccessCondition[conditions.Length];
            for(var index=0;index<conditions.Length;index++)
            {
                if(!TryConvertCondition(conditions[index],out converted[index])){error="Unsupported success condition: "+(conditions[index]??"<null>");return false;}
            }
            runtime=new NextTaskSpec{taskId=wire.task_id,title=wire.title,playerInstruction=wire.player_instruction,taskType=wire.task_type,requiredObjects=wire.required_objects,successConditions=converted,dependencies=wire.dependencies,protectedObjects=wire.protected_objects,allowedAuthoringScope=wire.allowed_authoring_scope,narrativeContext=wire.narrative_context};
            return true;
        }
        private static bool TryConvertCondition(string wire,out RuntimeSuccessCondition condition)
        {
            condition=null;if(string.IsNullOrWhiteSpace(wire))return false;
            var parts=wire.Split(':');if(parts.Length<2)return false;
            var name=parts[0].Trim().ToLowerInvariant();var objectId=parts[1].Trim();if(string.IsNullOrWhiteSpace(objectId))return false;
            string type;
            switch(name)
            {
                case "interact": type="OBJECT_GRABBED";break;
                case "painting_aligned": type="PAINTING_ALIGNED";break;
                case "object_revealed": type="OBJECT_REVEALED";break;
                case "object_held": type="OBJECT_HELD";break;
                case "object_at_anchor": if(parts.Length!=3||string.IsNullOrWhiteSpace(parts[2]))return false; condition=new RuntimeSuccessCondition{type="OBJECT_AT_ANCHOR",object_id=objectId,anchor_id=parts[2].Trim()};return true;
                case "object_open": type="OBJECT_OPEN";break;
                case "object_closed": type="OBJECT_CLOSED";break;
                case "lock_unlocked": type="LOCK_UNLOCKED";break;
                case "object_active": type="OBJECT_ACTIVE";break;
                case "object_inactive": type="OBJECT_INACTIVE";break;
                case "door_open": type="DOOR_OPEN";break;
                case "authoring_object_created": type="AUTHORING_OBJECT_CREATED";break;
                case "authoring_property_set": if(parts.Length!=3)return false;condition=new RuntimeSuccessCondition{type="AUTHORING_PROPERTY_SET",object_id=objectId,value=parts[2].Trim()};return true;
                default:return false;
            }
            condition=new RuntimeSuccessCondition{type=type,object_id=objectId};return true;
        }
    }
    [Serializable] public class RuntimeSuccessCondition { public string type; public string object_id; public string anchor_id; public string value; public RuntimeSuccessCondition[] children; }
}
