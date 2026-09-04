using System;
using System.Collections;
using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using DreamCodeVR2.SceneContext;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    // Server consequences are deliberately outside the participant command/authoring paths.
    public sealed class QuestConsequenceDispatcher : MonoBehaviour
    {
        private readonly HashSet<string> applied=new HashSet<string>(StringComparer.Ordinal);
        private AuthoringProtocolClient protocol; private ExperimentConditionManager condition;
        private void Resolve(){if(!protocol)protocol=FindFirstObjectByType<AuthoringProtocolClient>();if(!condition)condition=FindFirstObjectByType<ExperimentConditionManager>();}
        public void Receive(QuestConsequenceInstruction instruction)
        {
            Resolve();var id=instruction?.instructionId;
            DreamCodeVR2ClientLogger.Event("quest","QUEST_CONSEQUENCE_RECEIVED",null,new { instruction_id=id,session_id=instruction?.sessionId,canonical_set_id=instruction?.canonicalSetId,task_id=instruction?.sourceTaskId,instruction_type=instruction?.instructionType });
            if(instruction==null||instruction.protocolVersion!=1||string.IsNullOrWhiteSpace(id)){Reject(instruction,"malformed_instruction");return;}
            if(!ContextMatches(instruction)){Reject(instruction,"stale_context");return;}
            if(applied.Contains(id)){Ack(instruction,true,null,"idempotent");return;}
            StartCoroutine(ApplyRoutine(instruction));
        }
        public void ReceiveReset(QuestResetRequest request,ServerQuestInstanceDto setup)
        {
            Resolve();DreamCodeVR2ClientLogger.Event("quest","QUEST_RESET_REQUEST_RECEIVED",null,new { reset_request_id=request?.RequestId,session_id=request?.sessionId,canonical_set_id=request?.canonicalSetId });
            if(request==null||request.protocolVersion!=1||setup==null){DreamCodeVR2ClientLogger.Warn("quest","QUEST_CONSEQUENCE_REJECTED_STALE_CONTEXT","Reset request is incomplete.",new { reset_request_id=request?.RequestId });return;}
            condition.sessionId=request.sessionId;condition.activeQuestSetId=QuestCanonicalSetIds.Normalize(request.canonicalSetId);condition.activeQuestInstanceId=condition.activeQuestSetId;
            FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough();
            var instance=ToRuntime(setup);var controller=FindFirstObjectByType<QuestInstanceController>();controller?.Apply(instance);var sphere=AuthoringActionExecutor.FindEditable("sphere_001")?.GetComponent<C1QuestSphereController>();FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("canonical reset applied");DreamCodeVR2ClientLogger.Event("quest","QUEST_CANONICAL_SET_APPLIED",null,new { canonical_set_id=condition.activeQuestSetId,condition=condition.condition.ToString(),reset_request_id=request.RequestId,session_id=request.sessionId,placements_applied_count=instance.placements?.Length??0,initial_states_applied=instance.initialStates?.Length??0,sphere_profile=sphere?sphere.SphereProfile:null });FindFirstObjectByType<QuestWorldStateReporter>()?.ResetCompleted(request.RequestId,request.sessionId,condition.activeQuestSetId);
        }
        private IEnumerator ApplyRoutine(QuestConsequenceInstruction i)
        {
            var type=(i.instructionType??string.Empty).ToUpperInvariant();var target=Target(i);string error=null;string state=null;
            switch(type)
            {
                case "SET_LOCK_STATE": {var lockItem=AuthoringActionExecutor.FindEditable(i.lockId??TargetId(i));var lockController=lockItem?lockItem.GetComponent<QuestLockController>():null;var unlocked=string.Equals(Value(i,"state","locked"),"unlocked",StringComparison.OrdinalIgnoreCase);if(!lockController)error="lock_not_found";else {lockController.SetLocked(!unlocked);if(lockController.IsLocked==unlocked)error="physical_state_application_failed";else state=unlocked?"unlocked":"locked";}break;}
                case "SET_LIGHT_PROFILE": {var profile=Value(i,"profile","light_profile","color_profile");if(string.Equals(i.targetObjectId,"puzzle_lamps",StringComparison.OrdinalIgnoreCase)){foreach(var lampId in new[]{"lamp_001","lamp_002","lamp_003","lamp_004"}){var lamp=AuthoringActionExecutor.FindEditable(lampId)?.GetComponent<QuestLampController>();if(!lamp||!lamp.TrySetColorProfile(profile,out _)){error="canonical_light_source_not_resolved";break;}}state=error==null?profile:null;}else {var lamp=target?target.GetComponent<QuestLampController>():null;if(!lamp)error="target_not_found";else if(!lamp.TrySetColorProfile(profile,out _))error="canonical_light_source_not_resolved";else state=lamp.ColorProfile;}break;}
                case "SET_OBJECT_VISIBILITY": {if(!target)error="target_not_found";else {var visible=string.Equals(Value(i,"visible","state"),"true",StringComparison.OrdinalIgnoreCase)||string.Equals(Value(i,"visible","state"),"visible",StringComparison.OrdinalIgnoreCase);var note=target.GetComponent<QuestNoteController>();if(note)note.SetVisible(visible);else target.gameObject.SetActive(visible);state=visible?"visible":"hidden";}break;}
                case "SET_CLUE_TEXT": {var note=target?target.GetComponent<QuestNoteController>():null;if(!note)error="target_not_found";else {note.Configure(Value(i,"text"),target.gameObject.activeSelf);state="text_set";}break;}
                case "REVEAL_OBJECT_IN_CONTAINER": {if(!target)error="target_not_found";else {var preserveTransform=target.GetComponent<QuestNoteController>()||string.Equals(target.objectId,"clue_note_001",StringComparison.OrdinalIgnoreCase);var anchor=FindAnchor(i.containerId??Value(i,"anchor_id"));if(!preserveTransform&&!anchor)error="invalid_container";else {if(!preserveTransform){target.transform.SetParent(anchor.transform,true);if((target.objectId??string.Empty).StartsWith("key",StringComparison.OrdinalIgnoreCase))KeyPoseNormalizer.Normalize(target,"consequence_reveal",anchor.transform);}target.gameObject.SetActive(true);FindFirstObjectByType<QuestWorldStateReporter>()?.Revealed(target,i.containerId);state="revealed";}}break;}
                case "CLOSE_DRAWER": {var drawer=target?target.GetComponent<ExperimentalDrawerController>():null;if(!drawer)error="target_not_found";else if(!drawer.TryClose(out _))error="physical_state_application_failed";else {while(drawer.IsMoving)yield return null;if(drawer.IsOpen)error="physical_state_application_failed";else state="closed";}break;}
                case "SET_SPHERE_PROFILE": {var sphere=target?target.GetComponent<C1QuestSphereController>():null;var profile=Value(i,"profile","sphere_profile");if(!sphere)error="target_not_found";else if(!sphere.TrySetProfile(profile,out _))error="unsupported_profile";else state=sphere.SphereProfile;break;}
                default:error="unsupported_instruction";break;
            }
            if(error==null){applied.Add(i.instructionId);FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("quest consequence");DreamCodeVR2ClientLogger.Event("quest","QUEST_CONSEQUENCE_APPLIED",null,new { instruction_id=i.instructionId,instruction_type=type,semantic_state=state });Ack(i,true,null,state);}else Reject(i,error);
        }
        private bool ContextMatches(QuestConsequenceInstruction i)=>condition!=null&&string.Equals(i.sessionId,condition.sessionId,StringComparison.Ordinal)&&string.Equals(QuestCanonicalSetIds.Normalize(i.canonicalSetId),QuestCanonicalSetIds.Normalize(condition.activeQuestSetId),StringComparison.Ordinal);
        private void Reject(QuestConsequenceInstruction i,string reason){DreamCodeVR2ClientLogger.Warn("quest","QUEST_CONSEQUENCE_REJECTED",reason,new { instruction_id=i?.instructionId,session_id=i?.sessionId,canonical_set_id=i?.canonicalSetId });Ack(i,false,reason,null);}
        private void Ack(QuestConsequenceInstruction i,bool success,string reason,string state){protocol?.SendQuestConsequenceAck(i?.instructionId,i?.sessionId,QuestCanonicalSetIds.Normalize(i?.canonicalSetId),i?.sourceTaskId,success,reason,state);}
        private static AIEditableObject Target(QuestConsequenceInstruction i)=>AuthoringActionExecutor.FindEditable(TargetId(i)??i?.lockId);
        private static string TargetId(QuestConsequenceInstruction i)=>i?.targetObjectId??Value(i,"target_object_id","drawer_id","object_id","lamp_id","sphere_id");
        private static string Value(QuestConsequenceInstruction i,params string[] keys){foreach(var key in keys){var value=i?.payload?[key];if(value!=null)return value.ToString();}return null;}
        private static AuthoringAnchor FindAnchor(string id){foreach(var anchor in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(anchor&&string.Equals(anchor.anchorId,id,StringComparison.OrdinalIgnoreCase))return anchor;return null;}
        private static QuestInstance ToRuntime(ServerQuestInstanceDto setup)
        {
            var placements=new List<QuestPlacementBinding>();foreach(var p in setup.placements??Array.Empty<ServerQuestPlacementDto>())placements.Add(new QuestPlacementBinding{objectId=p.object_id,anchorId=p.anchor_id});
            var bindings=new List<QuestLockBinding>();foreach(var b in setup.key_lock_bindings??Array.Empty<ServerQuestBindingDto>())bindings.Add(new QuestLockBinding{lockId=QuestCanonicalIds.Normalize(b.lock_id),requiredKeyId=b.key_id,targetObjectId=b.role=="exit"?"door_001":Target(setup,"drawer")});
            var notes=new List<QuestNoteBinding>();foreach(var property in setup.clue_texts?.Properties()??new JProperty[0])notes.Add(new QuestNoteBinding{noteId=property.Name,text=property.Value.ToString(),visible=false});
            var states=new List<QuestInitialStateBinding>();foreach(var property in setup.initial_states?.Properties()??new JProperty[0])states.Add(new QuestInitialStateBinding{objectId=property.Name,state=property.Value.ToString()});
            var runtime=new List<QuestRuntimeObjectSpec>();foreach(var item in setup.required_runtime_objects??Array.Empty<ServerRequiredRuntimeObjectDto>())runtime.Add(new QuestRuntimeObjectSpec{objectId=item.object_id,primitive=item.primitive,semanticProfile=item.semantic_profile,sphereProfile=item.sphere_profile,initialAnchorId=item.initial_placement_anchor??item.placement_anchor,presetId=item.preset_id,initialSemanticState=item.initial_semantic_state,initialGrabbable=item.initial_grabbable,canonicalSizeMeters=item.canonical_size_m,canonicalScale=item.canonical_scale,source="required_runtime_objects"});
            return new QuestInstance{questId=setup.quest_instance_id,questSetId=QuestCanonicalSetIds.Normalize(setup.quest_set_id),targetDrawerId=Target(setup,"drawer"),selectedLampId=Target(setup,"lamp"),placements=placements.ToArray(),lockBindings=bindings.ToArray(),notes=notes.ToArray(),initialStates=states.ToArray(),requiredRuntimeObjects=runtime.ToArray(),relevantObjectIds=setup.relevant_object_ids};
        }
        private static string Target(ServerQuestInstanceDto setup,string name)=>setup?.task_targets==null?null:(string)setup.task_targets[name];
    }
}
