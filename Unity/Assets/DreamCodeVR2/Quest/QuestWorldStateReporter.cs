using System;
using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    // Versioned transition stream; SceneContext remains the authoritative state snapshot.
    public sealed class QuestWorldStateReporter : MonoBehaviour
    {
        public const int ProtocolVersion=1;
        private readonly Dictionary<string,int> generations=new Dictionary<string,int>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> qualifiedDrawerOpens=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly Dictionary<string,DrawerDiscoveryEvidence> discoveries=new Dictionary<string,DrawerDiscoveryEvidence>(StringComparer.Ordinal);
        private sealed class DrawerDiscoveryEvidence { public string containerId; public string objectId; public int generation; public string transition; }
        public int AvailabilityGeneration(string id)=>!string.IsNullOrWhiteSpace(id)&&generations.TryGetValue(id,out var value)?value:0;
        public int MarkAvailable(AIEditableObject item,string containerId,string reason){if(!item)return 0;var generation=AvailabilityGeneration(item.objectId)+1;generations[item.objectId]=generation;Send("OBJECT_AVAILABILITY_GENERATION",item.objectId,containerId,null,generation,"available",new { reason });DreamCodeVR2ClientLogger.Event("quest","OBJECT_AVAILABILITY_CHANGED",null,new { object_id=item.objectId,container_id=containerId,availability_generation=generation,reason });return generation;}
        public void DrawerOpened(string drawerId)
        {
            if(string.IsNullOrWhiteSpace(drawerId))return;
            qualifiedDrawerOpens[drawerId]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Send("DRAWER_OPEN_TRANSITION",drawerId,null,null,0,"open",new { transition="CLOSED_TO_OPEN" });
            var drawer=AuthoringActionExecutor.FindEditable(drawerId);
            foreach(var item in drawer?drawer.GetComponentsInChildren<AIEditableObject>(true):Array.Empty<AIEditableObject>())
                if(item&&item!=drawer&&item.gameObject.activeInHierarchy)RecordDrawerDiscovery(item,drawerId,AvailabilityGeneration(item.objectId));
        }
        public void DrawerClosed(string drawerId){if(!string.IsNullOrWhiteSpace(drawerId))qualifiedDrawerOpens.Remove(drawerId);}
        public void Revealed(AIEditableObject item,string containerId)
        {
            if(!item)return;
            var generation=MarkAvailable(item,containerId,"reveal");
            Send("OBJECT_REVEALED",item.objectId,containerId,null,generation,"revealed",new { transition="CLOSED_TO_OPEN" });
            RecordDrawerDiscovery(item,containerId,generation);
        }
        public bool EvaluateDrawerDiscovery(string taskId,string expectedContainerId,string expectedObjectId)
        {
            discoveries.TryGetValue(expectedObjectId??string.Empty,out var evidence);
            var expectedGeneration=AvailabilityGeneration(expectedObjectId);
            var actualContainer=evidence?.containerId;var actualObject=evidence?.objectId;var actualGeneration=evidence?.generation??-1;var transition=evidence?.transition;
            var result=evidence!=null&&string.Equals(expectedContainerId,actualContainer,StringComparison.Ordinal)&&string.Equals(expectedObjectId,actualObject,StringComparison.Ordinal)&&expectedGeneration==actualGeneration&&string.Equals(transition,"CLOSED_TO_OPEN",StringComparison.Ordinal);
            DreamCodeVR2ClientLogger.Event("quest","DRAWER_DISCOVERY_EVALUATION",null,new { task_id=taskId,expected_container=expectedContainerId,actual_container=actualContainer,expected_object=expectedObjectId,actual_object=actualObject,expected_generation=expectedGeneration,actual_generation=actualGeneration,transition,result });
            return result;
        }
        private void RecordDrawerDiscovery(AIEditableObject item,string containerId,int generation)
        {
            if(!item||string.IsNullOrWhiteSpace(containerId)||!qualifiedDrawerOpens.ContainsKey(containerId))return;
            discoveries[item.objectId]=new DrawerDiscoveryEvidence{containerId=containerId,objectId=item.objectId,generation=generation,transition="CLOSED_TO_OPEN"};
            (QuestEventBus.Instance??FindFirstObjectByType<QuestEventBus>())?.Publish(QuestEventType.DrawerDiscovery,item.objectId,containerId,"CLOSED_TO_OPEN");
        }
        public void LockChanged(QuestLockController lockController){var item=lockController?lockController.GetComponent<AIEditableObject>():null;Send("LOCK_STATE_CHANGED",item?.objectId,null,item?.objectId,0,lockController&&lockController.IsLocked?"locked":"unlocked",new { target_id=lockController?.physicalTargetObjectId??lockController?.associatedTargetObjectId });}
        public void SphereProfile(AIEditableObject item,string profile){Send("SPHERE_PROFILE_CHANGED",item?.objectId,null,null,AvailabilityGeneration(item?.objectId),profile,null);}
        public void LightProfile(AIEditableObject item,string oldProfile,string newProfile,Color appliedColor){Send("LIGHT_PROFILE_CHANGED",item?.objectId,null,null,0,newProfile,new { lamp_id=item?.objectId,old_profile=oldProfile,new_profile=newProfile,applied_color_rgba=new { r=appliedColor.r,g=appliedColor.g,b=appliedColor.b,a=appliedColor.a } });}
        public void ResetCompleted(string resetRequestId=null,string sessionId=null,string canonicalSetId=null){generations.Clear();qualifiedDrawerOpens.Clear();discoveries.Clear();var state=BuildResetState();Send("RESET_COMPLETED",null,null,null,0,state,new { availability_generation_reset=true,reset_request_id=resetRequestId },resetRequestId,sessionId,canonicalSetId);DreamCodeVR2ClientLogger.Event("quest","RESET_COMPLETED_STATE_SNAPSHOT",null,new { reset_request_id=resetRequestId,canonical_set_id=canonicalSetId,semantic_state=state });}
        private object BuildResetState(){var values=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var item in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)){if(!item||string.IsNullOrWhiteSpace(item.objectId))continue;var semanticInactivity=item.GetComponent<QuestSemanticInactivityMarker>();var drawer=item.GetComponent<ExperimentalDrawerController>();var lockController=item.GetComponent<QuestLockController>();var door=item.GetComponent<QuestDoorController>()??item.GetComponentInChildren<QuestDoorController>(true);var painting=item.GetComponent<QuestPaintingController>();var lamp=item.GetComponent<QuestLampController>();var sphere=item.GetComponent<C1QuestSphereController>();values[item.objectId]=semanticInactivity&&semanticInactivity.reportsInactive?"inactive":!item.gameObject.activeSelf?"inactive":drawer?(drawer.IsOpen?"open":"closed"):lockController?(lockController.IsLocked?"locked":"unlocked"):door?(door.IsOpen?"open":"closed"):painting?(painting.IsAligned?"aligned":"crooked"):lamp?lamp.ColorProfile:sphere?sphere.SphereProfile:"active";}return values;}
        private void Send(string type,string objectId,string containerId,string lockId,int generation,object state,object details,string resetRequestId=null,string sessionOverride=null,string canonicalSetOverride=null){var instance=FindFirstObjectByType<QuestInstanceController>();var task=instance&&instance.runtimeState?instance.runtimeState.GetCurrentTask():null;var manager=FindFirstObjectByType<ExperimentConditionManager>();var protocol=FindFirstObjectByType<AuthoringProtocolClient>();var payload=new { protocol_version=ProtocolVersion,event_id=Guid.NewGuid().ToString("N"),timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),peer=protocol?protocol.CurrentPeerUuid:null,session_id=sessionOverride??manager?.sessionId,canonical_set_id=canonicalSetOverride??QuestCanonicalSetIds.Normalize(manager?.activeQuestSetId),reset_request_id=resetRequestId,quest_instance_id=instance?.ActiveInstance?.questId,task_id=task?.taskId,event_type=type,object_id=objectId,container_id=containerId,lock_id=lockId,availability_generation=generation,semantic_state=state,details};protocol?.SendQuestWorldStateEvent(payload);DreamCodeVR2ClientLogger.Event("quest","QUEST_WORLD_STATE_EVENT",null,new { protocol_version=ProtocolVersion,event_type=type,object_id=objectId,container_id=containerId,availability_generation=generation,condition=manager?.condition.ToString(),reset_request_id=resetRequestId });}
    }
    public static class QuestCanonicalSetIds{public static string Normalize(string id){id=(id??string.Empty).Trim().ToLowerInvariant();return id.StartsWith("set_a",StringComparison.Ordinal)?"set_a":id.StartsWith("set_b",StringComparison.Ordinal)?"set_b":id.StartsWith("set_c",StringComparison.Ordinal)?"set_c":id;}}
}
