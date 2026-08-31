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
    [Serializable] public class AuthoringEnvelope { public string type; public AuthoringAction action; public PredefinedVoiceCommand command; public string command_id; public string action_id; public string reason; public string detail; public ServerNextTaskDto task; public ServerQuestInstanceDto quest_instance; public string task_id; public string status; public bool confirmation_required; public string interpretation; public string expected_effect; public string target_object_id; [JsonProperty("original_utterance")] public string originalUtterance; [JsonProperty("recognized_utterance")] public string recognizedUtterance; public string utterance; public string transcript; }
    [Serializable] public class PredefinedVoiceCommand
    {
        [JsonProperty("command_id")] public string commandId;
        [JsonProperty("target_object_id")] public string targetObjectId;
        [JsonProperty("intent")] public string command;
        [JsonProperty("preset_id")] public string preset;
        [JsonProperty("secondary_object_id")] public string secondaryObjectId;
        [JsonProperty("peer_uuid")] public string peerUuid;
        [JsonProperty("schema_version")] public string schemaVersion;
        [JsonProperty("original_utterance")] public string originalUtterance;
        [JsonProperty("recognized_utterance")] public string recognizedUtterance;
        public string utterance;
        public string transcript;
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
        [JsonProperty("allowed_authoring_scope")] public TaskPolicyScopeDto allowedAuthoringScope;
        [JsonProperty("allowed_solution_scope")] public TaskPolicyScopeDto allowedSolutionScope;
        [JsonProperty("narrative_context")] public string narrativeContext;
        [JsonProperty("candidate_object_ids")] public string[] candidateObjectIds;
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
        [JsonProperty("allowed_authoring_scope")] public TaskPolicyScopeDto allowed_authoring_scope;
        [JsonProperty("allowed_solution_scope")] public TaskPolicyScopeDto allowed_solution_scope;
        [JsonProperty("narrative_context")] public string narrative_context;
        [JsonProperty("quest_setup")] public ServerQuestSetupDto[] quest_setup;
        [JsonProperty("candidate_object_ids")] public string[] candidate_object_ids;
    }
    [Serializable] public class ServerQuestSetupDto { [JsonProperty("object_id")] public string object_id; public string primitive; [JsonProperty("placement_anchor")] public string placement_anchor; [JsonProperty("initial_grabbable")] public bool initial_grabbable; [JsonProperty("preset_id")] public string preset_id; }
    [Serializable] public class ServerRequiredRuntimeObjectDto { [JsonProperty("object_id")] public string object_id; public string primitive; [JsonProperty("object_type")] public string object_type; [JsonProperty("semantic_profile")] public string semantic_profile; [JsonProperty("preset_id")] public string preset_id; [JsonProperty("material_profile")] public string material_profile; [JsonProperty("initial_placement_anchor")] public string initial_placement_anchor; [JsonProperty("placement_anchor")] public string placement_anchor; [JsonProperty("initial_semantic_state")] public string initial_semantic_state; [JsonProperty("initial_grabbable")] public bool initial_grabbable; [JsonProperty("canonical_size_m")] public float canonical_size_m; [JsonProperty("canonical_scale")] public float canonical_scale; }
    [Serializable] public class ServerQuestBindingDto { [JsonProperty("key_id")] public string key_id; [JsonProperty("lock_id")] public string lock_id; public string role; }
    [Serializable] public class ServerQuestPlacementDto { [JsonProperty("object_id")] public string object_id; [JsonProperty("anchor_id")] public string anchor_id; }
    [Serializable] public class ServerQuestInstanceDto { [JsonProperty("schema_version")] public string schema_version; [JsonProperty("quest_instance_id")] public string quest_instance_id; [JsonProperty("quest_set_id")] public string quest_set_id; [JsonProperty("placements")] public ServerQuestPlacementDto[] placements; [JsonProperty("key_lock_bindings")] public ServerQuestBindingDto[] key_lock_bindings; [JsonProperty("task_targets")] public JObject task_targets; [JsonProperty("clue_texts")] public JObject clue_texts; [JsonProperty("initial_states")] public JObject initial_states; [JsonProperty("anchor_assignments")] public JObject anchor_assignments; [JsonProperty("c1_setup")] public ServerQuestSetupDto[] c1_setup; [JsonProperty("required_runtime_objects")] public ServerRequiredRuntimeObjectDto[] required_runtime_objects; [JsonProperty("relevant_object_ids")] public string[] relevant_object_ids; }
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
            NormalizeProtocolObjectReferences(converted,wire.required_objects,out var requiredObjects);
            runtime=new NextTaskSpec{taskId=wire.task_id,title=wire.title,playerInstruction=wire.player_instruction,taskType=wire.task_type,requiredObjects=requiredObjects,successConditions=ApplyCausalSuccessSemantics(wire,converted),dependencies=wire.dependencies,protectedObjects=wire.protected_objects,allowedAuthoringScope=wire.allowed_authoring_scope,allowedSolutionScope=wire.allowed_solution_scope,narrativeContext=wire.narrative_context,candidateObjectIds=wire.candidate_object_ids};
            return true;
        }
        // Compatibility is protocol-boundary lock-alias normalization only. Canonical
        // drawer IDs supplied by the server always pass through unchanged.
        private static void NormalizeProtocolObjectReferences(RuntimeSuccessCondition[] conditions,string[] sourceRequiredObjects,out string[] requiredObjects)
        {
            requiredObjects=sourceRequiredObjects==null?Array.Empty<string>():Array.ConvertAll(sourceRequiredObjects,DreamCodeVR2.Quest.QuestCanonicalIds.Normalize);
            foreach(var condition in conditions??Array.Empty<RuntimeSuccessCondition>())if(condition!=null)condition.object_id=DreamCodeVR2.Quest.QuestCanonicalIds.Normalize(condition.object_id);
        }
        // A server task may include a visibility consequence beside its causal action
        // (for example painting_aligned plus object_revealed:clue_note_001). The local
        // task completes on the requested world action; discovery predicates remain for
        // tasks that explicitly ask the participant to discover/retrieve/inspect content.
        private static RuntimeSuccessCondition[] ApplyCausalSuccessSemantics(ServerNextTaskDto wire,RuntimeSuccessCondition[] conditions)
        {
            var hasCausalAction=false;
            foreach(var condition in conditions??Array.Empty<RuntimeSuccessCondition>())if(IsCausalActionCondition(condition)) { hasCausalAction=true;break; }
            if(!hasCausalAction||ExplicitlyRequiresDiscovery(wire))return conditions;
            var retained=new List<RuntimeSuccessCondition>();
            foreach(var condition in conditions??Array.Empty<RuntimeSuccessCondition>())if(!string.Equals(condition?.type,"OBJECT_REVEALED",StringComparison.OrdinalIgnoreCase))retained.Add(condition);
            return retained.ToArray();
        }
        private static bool IsCausalActionCondition(RuntimeSuccessCondition condition)
        {
            switch((condition?.type??string.Empty).ToUpperInvariant())
            {
                case "PAINTING_ALIGNED": case "OBJECT_OPEN": case "LOCK_UNLOCKED": case "OBJECT_ACTIVE": case "OBJECT_AT_ANCHOR": case "DOOR_OPEN": return true;
                default:return false;
            }
        }
        private static bool ExplicitlyRequiresDiscovery(ServerNextTaskDto wire)
        {
            var text=((wire?.task_type??string.Empty)+" "+(wire?.player_instruction??string.Empty)).ToLowerInvariant();
            return text.Contains("find ")||text.Contains("retrieve")||text.Contains("inspect")||text.Contains("read ")||text.Contains("pick up")||text.Contains("pickup")||text.Contains("grab ");
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
    [Serializable]
    [JsonConverter(typeof(TaskPolicyScopeJsonConverter))]
    public class TaskPolicyScopeDto
    {
        // Canonical server scopes are objects. These named entries preserve the current
        // condition/action policy contract; extension data retains future named entries
        // without treating the whole task payload as untyped JSON.
        [JsonProperty("allowed_operations")] public string[] allowed_operations;
        [JsonProperty("operations")] public string[] operations;
        [JsonProperty("actions")] public string[] actions;
        [JsonProperty("object_ids")] public string[] object_ids;
        [JsonProperty("by_condition")] public JObject by_condition;
        [JsonProperty("by_action")] public JObject by_action;
        [JsonIgnore] public string[] legacy_operations;
        [JsonExtensionData] public IDictionary<string,JToken> additional_entries;

        public string[] GetAllowedOperations()
        {
            var values=new List<string>();
            Add(values,legacy_operations);Add(values,allowed_operations);Add(values,operations);Add(values,actions);
            return values.ToArray();
        }
        private static void Add(List<string> values,string[] source)
        {
            foreach(var value in source??Array.Empty<string>()) if(!string.IsNullOrWhiteSpace(value)&&!values.Contains(value)) values.Add(value);
        }
    }
    public class TaskPolicyScopeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)=>objectType==typeof(TaskPolicyScopeDto);
        public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
        {
            if(reader.TokenType==JsonToken.Null)return new TaskPolicyScopeDto();
            var token=JToken.Load(reader);
            if(token.Type==JTokenType.Array)return new TaskPolicyScopeDto{legacy_operations=token.ToObject<string[]>(serializer)??Array.Empty<string>()};
            if(token.Type!=JTokenType.Object)throw new JsonSerializationException("Task policy scope must be an object or legacy string array.");
            var scope=new TaskPolicyScopeDto();serializer.Populate(token.CreateReader(),scope);return scope;
        }
        public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
        {
            var scope=value as TaskPolicyScopeDto;if(scope==null){writer.WriteNull();return;}
            var result=new JObject();
            if(scope.allowed_operations!=null)result["allowed_operations"]=JToken.FromObject(scope.allowed_operations,serializer);
            if(scope.operations!=null)result["operations"]=JToken.FromObject(scope.operations,serializer);
            if(scope.actions!=null)result["actions"]=JToken.FromObject(scope.actions,serializer);
            if(scope.object_ids!=null)result["object_ids"]=JToken.FromObject(scope.object_ids,serializer);
            if(scope.by_condition!=null)result["by_condition"]=scope.by_condition;
            if(scope.by_action!=null)result["by_action"]=scope.by_action;
            foreach(var entry in scope.additional_entries??new Dictionary<string,JToken>())if(result[entry.Key]==null)result[entry.Key]=entry.Value;
            result.WriteTo(writer);
        }
    }
    public static class FixedQuestWireConverter
    {
        public static bool TryConvertTask(ServerNextTaskDto wire,out DreamCodeVR2.Quest.QuestTaskSpec task,out string error)
        {
            task=null;
            if(!NextTaskWireConverter.TryConvert(wire,out var runtime,out error))return false;
            var target=runtime.requiredObjects!=null&&runtime.requiredObjects.Length>0?runtime.requiredObjects[0]:null;
            task=new DreamCodeVR2.Quest.QuestTaskSpec{taskId=runtime.taskId,step=0,type=runtime.taskType,target=target,description=runtime.playerInstruction,successConditions=NormalizeLockConditions(runtime.successConditions)};
            return true;
        }
        public static bool TryConvert(ServerNextTaskDto wire, ServerQuestInstanceDto setup, out DreamCodeVR2.Quest.QuestInstance instance, out string error)
        {
            instance=null; error=null;
            if(!TryConvertTask(wire,out var task,out error)) return false;
            if(setup==null||string.IsNullOrWhiteSpace(setup.quest_instance_id)){error="Fixed quest task has no quest_instance setup.";return false;}
            task.step=1;
            var bindings=new List<DreamCodeVR2.Quest.QuestLockBinding>();
            foreach(var binding in setup.key_lock_bindings??Array.Empty<ServerQuestBindingDto>())
            {
                var canonicalLock=NormalizeLockId(binding.lock_id);
                bindings.Add(new DreamCodeVR2.Quest.QuestLockBinding{requiredKeyId=binding.key_id,lockId=canonicalLock,targetObjectId=binding.role=="exit"?"door_001":Target(setup,"drawer")});
            }
            var notes=new List<DreamCodeVR2.Quest.QuestNoteBinding>();
            if(setup.clue_texts!=null)foreach(var property in setup.clue_texts.Properties())notes.Add(new DreamCodeVR2.Quest.QuestNoteBinding{noteId=property.Name,text=(string)property.Value,visible=false});
            var placements=new List<DreamCodeVR2.Quest.QuestPlacementBinding>();
            foreach(var placement in setup.placements??Array.Empty<ServerQuestPlacementDto>())AddPlacement(placements,placement?.object_id,placement?.anchor_id);
            if(setup.anchor_assignments!=null)foreach(var property in setup.anchor_assignments.Properties())AddPlacement(placements,property.Name,(string)property.Value);
            var initialStates=new List<DreamCodeVR2.Quest.QuestInitialStateBinding>();
            if(setup.initial_states!=null)foreach(var property in setup.initial_states.Properties())initialStates.Add(new DreamCodeVR2.Quest.QuestInitialStateBinding{objectId=NormalizeLockId(property.Name),state=(string)property.Value});
            var selectedDrawer=Target(setup,"drawer");
            var runtimeObjects=ConvertRuntimeObjects(setup,wire);
            instance=new DreamCodeVR2.Quest.QuestInstance{questId=setup.quest_instance_id,questSetId=setup.quest_set_id,targetDrawerId=selectedDrawer,selectedLampId=Target(setup,"lamp"),lockBindings=bindings.ToArray(),notes=notes.ToArray(),placements=placements.ToArray(),initialStates=initialStates.ToArray(),requiredRuntimeObjects=runtimeObjects,relevantObjectIds=setup.relevant_object_ids,plan=new DreamCodeVR2.Quest.QuestPlan{quest_id=setup.quest_instance_id,title=setup.quest_set_id,tasks=new List<DreamCodeVR2.Quest.QuestTaskSpec>{task}}};
            var sphere=Array.Find(runtimeObjects,candidate=>candidate!=null&&candidate.objectId=="sphere_001");
            if(sphere!=null){instance.requiresC1Sphere=true;instance.c1SphereId=sphere.objectId;instance.c1SphereStartAnchorId=sphere.initialAnchorId;instance.c1SpherePlacementAnchorId="basket_001.basket_inside_anchor";}
            return true;
        }
        private static DreamCodeVR2.Quest.QuestRuntimeObjectSpec[] ConvertRuntimeObjects(ServerQuestInstanceDto setup,ServerNextTaskDto wire)
        {
            var primary=setup?.required_runtime_objects??Array.Empty<ServerRequiredRuntimeObjectDto>();var source=primary.Length>0?"required_runtime_objects":"legacy_quest_setup";var values=new List<DreamCodeVR2.Quest.QuestRuntimeObjectSpec>();
            if(primary.Length>0)foreach(var runtimeObject in primary)if(runtimeObject!=null&&!string.IsNullOrWhiteSpace(runtimeObject.object_id))values.Add(new DreamCodeVR2.Quest.QuestRuntimeObjectSpec{objectId=DreamCodeVR2.Quest.QuestCanonicalIds.Normalize(runtimeObject.object_id),primitive=string.IsNullOrWhiteSpace(runtimeObject.primitive)?runtimeObject.object_type:runtimeObject.primitive,semanticProfile=runtimeObject.semantic_profile,presetId=runtimeObject.preset_id,materialProfile=runtimeObject.material_profile,initialAnchorId=string.IsNullOrWhiteSpace(runtimeObject.initial_placement_anchor)?runtimeObject.placement_anchor:runtimeObject.initial_placement_anchor,initialSemanticState=runtimeObject.initial_semantic_state,initialGrabbable=runtimeObject.initial_grabbable,canonicalSizeMeters=runtimeObject.canonical_size_m,canonicalScale=runtimeObject.canonical_scale,source=source});
            else {var legacy=wire?.quest_setup??setup?.c1_setup??Array.Empty<ServerQuestSetupDto>();foreach(var legacyObject in legacy)if(legacyObject!=null&&!string.IsNullOrWhiteSpace(legacyObject.object_id))values.Add(new DreamCodeVR2.Quest.QuestRuntimeObjectSpec{objectId=DreamCodeVR2.Quest.QuestCanonicalIds.Normalize(legacyObject.object_id),primitive=legacyObject.primitive,presetId=legacyObject.preset_id,initialAnchorId=legacyObject.placement_anchor,initialGrabbable=legacyObject.initial_grabbable,source=source});DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_LEGACY_FALLBACK_USED",null,new { quest_instance_id=setup?.quest_instance_id,count=values.Count });}
            foreach(var value in values)DreamCodeVR2ClientLogger.Event("quest","REQUIRED_RUNTIME_OBJECT_RECEIVED",null,new { quest_instance_id=setup?.quest_instance_id,object_id=value.objectId,object_type=value.primitive,initial_anchor=value.initialAnchorId,source=value.source });return values.ToArray();
        }
        private static string Target(ServerQuestInstanceDto setup,string name)=>setup.task_targets==null?null:(string)setup.task_targets[name];
        private static void AddPlacement(List<DreamCodeVR2.Quest.QuestPlacementBinding> values,string objectId,string anchorId)
        {
            if(string.IsNullOrWhiteSpace(objectId)||string.IsNullOrWhiteSpace(anchorId)||values.Exists(value=>value.objectId==objectId))return;
            values.Add(new DreamCodeVR2.Quest.QuestPlacementBinding{objectId=objectId,anchorId=anchorId});
        }
        private static string NormalizeLockId(string id)
        {
            // Server drawer-lock aliases are logical puzzle IDs; resolve them once to
            // the scene's canonical lock objects. lock_001 is the exit-door lock.
            return DreamCodeVR2.Quest.QuestCanonicalIds.Normalize(id);
        }
        private static RuntimeSuccessCondition[] NormalizeLockConditions(RuntimeSuccessCondition[] conditions)
        {
            foreach(var condition in conditions??Array.Empty<RuntimeSuccessCondition>())if(condition!=null&&string.Equals(condition.type,"LOCK_UNLOCKED",StringComparison.OrdinalIgnoreCase))condition.object_id=NormalizeLockId(condition.object_id);
            return conditions;
        }
    }
    // The deployed server currently sends the activation request without the documented
    // preceding NextTaskGenerated payload. Keep this narrow fallback for the four canonical
    // first tasks so a valid session still has an active local task and a populated context.
    public static class FixedQuestActivationFallback
    {
        public static bool TryCreate(string taskId, ExperimentCondition condition, out DreamCodeVR2.Quest.QuestInstance instance)
        {
            instance=null;if(string.IsNullOrWhiteSpace(taskId)||!taskId.EndsWith(":T1",StringComparison.OrdinalIgnoreCase))return false;
            var instanceId=taskId.Substring(0,taskId.Length-3);var drawer="table_drawer_002";var drawerLock="lock_002";var drawerKey="key_001";var exitKey="key_002";var lamp="lamp_001";var setId="set_a_ball_and_drawer";var instruction="Turn the sphere into a soccer ball and place it in the basket.";var target="sphere_001";var conditions=new[]{new RuntimeSuccessCondition{type="OBJECT_AT_ANCHOR",object_id="sphere_001",anchor_id="basket_001.basket_inside_anchor"}};
            switch(instanceId)
            {
                case "set_a_instance_2": drawer="table_drawer_002";drawerLock="lock_002";break;
                case "set_b_instance_1": setId="set_b_search_and_locks";drawer="cabinet_drawer_002";drawerLock="lock_003";instruction="Find the required key and use it to unlock the cabinet drawer.";target=drawer;conditions=new[]{new RuntimeSuccessCondition{type="LOCK_UNLOCKED",object_id=drawerLock}};break;
                case "set_c_instance_1": setId="set_c_alternate_key_relation_lamp";drawer="cabinet_drawer_002";drawerLock="lock_003";drawerKey="key_002";exitKey="key_001";lamp="lamp_003";instruction="Straighten the painting and reveal the first clue.";target="painting_001";conditions=new[]{new RuntimeSuccessCondition{type="PAINTING_ALIGNED",object_id="painting_001"},new RuntimeSuccessCondition{type="OBJECT_REVEALED",object_id="clue_note_001"}};break;
                case "set_a_instance_1": break;
                default:return false;
            }
            instance=new DreamCodeVR2.Quest.QuestInstance{questId=instanceId,questSetId=setId,targetDrawerId=drawer,selectedLampId=lamp,lockBindings=new[]{new DreamCodeVR2.Quest.QuestLockBinding{lockId=drawerLock,requiredKeyId=drawerKey,targetObjectId=drawer},new DreamCodeVR2.Quest.QuestLockBinding{lockId="lock_001",requiredKeyId=exitKey,targetObjectId="door_001"}},plan=new DreamCodeVR2.Quest.QuestPlan{quest_id=instanceId,title=setId,tasks=new List<DreamCodeVR2.Quest.QuestTaskSpec>{new DreamCodeVR2.Quest.QuestTaskSpec{taskId=taskId,step=1,type="FixedServerTask",target=target,description=instruction,successConditions=conditions}}}};
            if(condition==ExperimentCondition.VoiceCommandBaseline&&setId=="set_a_ball_and_drawer"){instance.requiresC1Sphere=true;instance.c1SphereId="sphere_001";instance.c1SphereStartAnchorId=instanceId=="set_a_instance_2"?"table_drawer_003.drawer_inside_anchor":"table_001.desk_surface_anchor";instance.c1SpherePlacementAnchorId="basket_001.basket_inside_anchor";}
            return true;
        }
    }
    [Serializable] public class RuntimeSuccessCondition { public string type; public string object_id; public string anchor_id; public string value; public RuntimeSuccessCondition[] children; }
}
