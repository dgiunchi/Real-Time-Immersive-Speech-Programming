using System;
using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    // Versioned transition stream; SceneContext remains the authoritative state snapshot.
    public sealed class QuestWorldStateReporter : MonoBehaviour
    {
        public const int ProtocolVersion=1;
        private readonly Dictionary<string,int> generations=new Dictionary<string,int>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> objectAnchors=new Dictionary<string,string>(StringComparer.Ordinal);
        public JObject LastPreparedPayload { get; private set; }
        public event Action<JObject> EventPrepared;
        public int AvailabilityGeneration(string id)=>!string.IsNullOrWhiteSpace(id)&&generations.TryGetValue(id,out var value)?value:0;
        public int MarkAvailable(AIEditableObject item,string containerId,string reason){if(!item)return 0;var generation=AvailabilityGeneration(item.objectId)+1;generations[item.objectId]=generation;Send("OBJECT_AVAILABILITY_GENERATION",item.objectId,containerId,null,generation,"available",new { reason });DreamCodeVR2ClientLogger.Event("quest","OBJECT_AVAILABILITY_CHANGED",null,new { object_id=item.objectId,container_id=containerId,availability_generation=generation,reason });return generation;}
        public bool DrawerStateChanged(AIEditableObject item,string state)
        {
            var normalized=NormalizeState(state,"open","closed");
            if(!item||string.IsNullOrWhiteSpace(item.objectId)||string.IsNullOrWhiteSpace(normalized))return false;
            Send("DRAWER_STATE_CHANGED",item.objectId,null,null,0,normalized,null,stateField:normalized);
            return true;
        }
        public bool DoorStateChanged(AIEditableObject item,string state)
        {
            var normalized=NormalizeState(state,"open","closed");
            if(!item||string.IsNullOrWhiteSpace(item.objectId)||string.IsNullOrWhiteSpace(normalized))return false;
            Send("DOOR_STATE_CHANGED",item.objectId,null,null,0,normalized,null,stateField:normalized);
            return true;
        }
        public bool ObjectAnchorChanged(AIEditableObject item,string anchorId)
        {
            if(!item||string.IsNullOrWhiteSpace(item.objectId))return false;
            var normalized=string.IsNullOrWhiteSpace(anchorId)?null:anchorId.Trim();
            if(!string.IsNullOrWhiteSpace(normalized))objectAnchors[item.objectId]=normalized; else objectAnchors.Remove(item.objectId);
            Send("OBJECT_ANCHOR_CHANGED",item.objectId,null,null,AvailabilityGeneration(item.objectId),normalized??string.Empty,null,anchorId:normalized);
            return true;
        }
        public bool DrawerOpened(string drawerId,string revealedObjectId,int generation)
        {
            if(string.IsNullOrWhiteSpace(drawerId)||string.IsNullOrWhiteSpace(revealedObjectId)||generation<=0)return false;
            Send("DRAWER_OPEN_TRANSITION",revealedObjectId,drawerId,null,generation,"open",new { transition="closed_to_open" },revealedObjectId:revealedObjectId,transition:"closed_to_open");
            return true;
        }
        public int Revealed(AIEditableObject item,string containerId){if(!item)return 0;var generation=MarkAvailable(item,containerId,"reveal");Send("OBJECT_REVEALED",item.objectId,containerId,null,generation,"revealed",new { transition="closed_to_open" },revealedObjectId:item.objectId,transition:"closed_to_open");return generation;}
        public void LockChanged(QuestLockController lockController){var item=lockController?lockController.GetComponent<AIEditableObject>():null;Send("LOCK_STATE_CHANGED",item?.objectId,null,item?.objectId,0,lockController&&lockController.IsLocked?"locked":"unlocked",new { target_id=lockController?.physicalTargetObjectId??lockController?.associatedTargetObjectId });}
        public void SphereProfile(AIEditableObject item,string profile){Send("SPHERE_PROFILE_CHANGED",item?.objectId,null,null,AvailabilityGeneration(item?.objectId),profile,null);}
        public void LightProfile(AIEditableObject item,string oldProfile,string newProfile,Color appliedColor){Send("LIGHT_PROFILE_CHANGED",item?.objectId,null,null,0,newProfile,new { lamp_id=item?.objectId,old_profile=oldProfile,new_profile=newProfile,applied_color_rgba=new { r=appliedColor.r,g=appliedColor.g,b=appliedColor.b,a=appliedColor.a } });}
        public bool PaintingStateChanged(AIEditableObject item,string oldState,string newState,bool physicalAlignmentConfirmed,string sessionId=null,string canonicalSetId=null)
        {
            if(!item||string.IsNullOrWhiteSpace(item.objectId)||string.IsNullOrWhiteSpace(newState))return false;
            var normalizedOld=string.IsNullOrWhiteSpace(oldState)?null:oldState.Trim().ToLowerInvariant();
            var normalizedNew=newState.Trim().ToLowerInvariant();
            if(normalizedOld==normalizedNew)return false;
            var state=new Dictionary<string,string>(StringComparer.Ordinal){[item.objectId]=normalizedNew};
            Send("PAINTING_STATE_CHANGED",item.objectId,null,null,0,state,new { object_id=item.objectId,old_state=normalizedOld,new_state=normalizedNew,physical_alignment_confirmed=physicalAlignmentConfirmed },null,sessionId,canonicalSetId);
            return true;
        }
        public void ResetCompleted(string resetRequestId=null,string sessionId=null,string canonicalSetId=null){generations.Clear();objectAnchors.Clear();var state=BuildResetState();Send("RESET_COMPLETED",null,null,null,0,state,new { availability_generation_reset=true,reset_request_id=resetRequestId },resetRequestId,sessionId,canonicalSetId);DreamCodeVR2ClientLogger.Event("quest","RESET_COMPLETED_STATE_SNAPSHOT",null,new { reset_request_id=resetRequestId,canonical_set_id=canonicalSetId,semantic_state=state });}
        private object BuildResetState(){var values=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var item in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)){if(!item||string.IsNullOrWhiteSpace(item.objectId))continue;var semanticInactivity=item.GetComponent<QuestSemanticInactivityMarker>();var drawer=item.GetComponent<ExperimentalDrawerController>();var lockController=item.GetComponent<QuestLockController>();var door=item.GetComponent<QuestDoorController>()??item.GetComponentInChildren<QuestDoorController>(true);var painting=item.GetComponent<QuestPaintingController>();var lamp=item.GetComponent<QuestLampController>();var sphere=item.GetComponent<C1QuestSphereController>();values[item.objectId]=semanticInactivity&&semanticInactivity.reportsInactive?"inactive":!item.gameObject.activeSelf?"inactive":drawer?(drawer.IsOpen?"open":"closed"):lockController?(lockController.IsLocked?"locked":"unlocked"):door?(door.IsOpen?"open":"closed"):painting?(painting.IsAligned?"aligned":"crooked"):lamp?lamp.ColorProfile:sphere?sphere.SphereProfile:"active";}return values;}
        private void Send(string type,string objectId,string containerId,string lockId,int generation,object state,object details,string resetRequestId=null,string sessionOverride=null,string canonicalSetOverride=null,string stateField=null,string anchorId=null,string revealedObjectId=null,string transition=null)
        {
            var manager=FindFirstObjectByType<ExperimentConditionManager>();
            var payload=CreatePayload(type,objectId,containerId,lockId,generation,state,details,resetRequestId,sessionOverride,canonicalSetOverride,stateField,anchorId,revealedObjectId,transition);
            Emit(payload);
            DreamCodeVR2ClientLogger.Event("quest","QUEST_WORLD_STATE_EVENT",null,new { protocol_version=ProtocolVersion,event_type=type,object_id=objectId,container_id=containerId,availability_generation=generation,condition=manager?.condition.ToString(),reset_request_id=resetRequestId });
        }
        private JObject CreatePayload(string type,string objectId,string containerId,string lockId,int generation,object state,object details,string resetRequestId,string sessionOverride,string canonicalSetOverride,string stateField,string anchorId,string revealedObjectId,string transition)
        {
            var instance=FindFirstObjectByType<QuestInstanceController>();
            var task=instance&&instance.runtimeState?instance.runtimeState.GetCurrentTask():null;
            var manager=FindFirstObjectByType<ExperimentConditionManager>();
            var protocol=FindFirstObjectByType<AuthoringProtocolClient>();
            return JObject.FromObject(new { protocol_version=ProtocolVersion,event_id=Guid.NewGuid().ToString("N"),timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),peer=protocol?protocol.CurrentPeerUuid:null,session_id=sessionOverride??manager?.sessionId,canonical_set_id=canonicalSetOverride??QuestCanonicalSetIds.Normalize(manager?.activeQuestSetId),reset_request_id=resetRequestId,quest_instance_id=instance?.ActiveInstance?.questId,task_id=task?.taskId,event_type=type,object_id=objectId,container_id=containerId,lock_id=lockId,availability_generation=generation,semantic_state=state,state=stateField,anchor_id=anchorId,revealed_object_id=revealedObjectId,transition,details});
        }
        private void Emit(JObject payload)
        {
            if(payload==null)return;
            LastPreparedPayload=(JObject)payload.DeepClone();
            EventPrepared?.Invoke((JObject)payload.DeepClone());
            FindFirstObjectByType<AuthoringProtocolClient>()?.SendQuestWorldStateEvent(payload);
        }
        private static string NormalizeState(string value,string defaultValue,params string[] allowed)
        {
            var normalized=(value??defaultValue??string.Empty).Trim().ToLowerInvariant();
            foreach(var candidate in allowed??Array.Empty<string>())if(string.Equals(normalized,candidate,StringComparison.Ordinal))return normalized;
            return null;
        }
    }
    public static class QuestCanonicalSetIds{public static string Normalize(string id){id=(id??string.Empty).Trim().ToLowerInvariant();return id.StartsWith("set_a",StringComparison.Ordinal)?"set_a":id.StartsWith("set_b",StringComparison.Ordinal)?"set_b":id.StartsWith("set_c",StringComparison.Ordinal)?"set_c":id;}}
}
