using System;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using DreamCodeVR2.SceneContext;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public static class QuestRuntimeObjectFactory
    {
        public static void Ensure(QuestRuntimeObjectSpec spec,QuestInstanceController owner)
        {
            if(spec==null||string.IsNullOrWhiteSpace(spec.objectId))return;var id=QuestCanonicalIds.Normalize(spec.objectId);var existing=AuthoringActionExecutor.FindEditable(id);
            if(existing){ApplyInitialSphereProfile(existing.GetComponent<C1QuestSphereController>(),spec,owner);DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_REUSED",null,new { object_id=id,unity_name=existing.gameObject.name,source=spec.source });return;}
            if(!string.Equals(spec.primitive,"sphere",StringComparison.OrdinalIgnoreCase)){DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CREATE_UNSUPPORTED","Runtime primitive is not supported.",new { object_id=id,primitive=spec.primitive });return;}
            if(!TryResolveInitialAnchor(spec.initialAnchorId,out var anchor,out var resolvedAnchorId,out var resolutionError))
            {
                DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CREATE_FAILED","Initial placement anchor is unavailable.",new { object_id=id,anchor_id=spec.initialAnchorId,resolved_anchor_id=resolvedAnchorId,error=resolutionError });return;
            }
            if(TryGetInaccessibleContainer(anchor,out var containerId,out var lockId,out var lockState))
            {
                DreamCodeVR2ClientLogger.Warn("quest","QUEST_REQUIRED_OBJECT_UNREACHABLE","A required runtime object was declared below an inaccessible locked container.",new { quest_instance_id=owner?.ActiveInstance?.questId,task_id=owner?.runtimeState?.GetCurrentTask()?.taskId,object_id=id,anchor_id=resolvedAnchorId,container_id=containerId,lock_id=lockId,lock_state=lockState });return;
            }
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=id;sphere.tag="game";sphere.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);
            if(IsSoccerProfile(spec))QuestSoccerBall.SetWorldDiameter(sphere.transform);else if(spec.canonicalSizeMeters>0f)sphere.transform.localScale=Vector3.one*spec.canonicalSizeMeters;else if(spec.canonicalScale>0f)sphere.transform.localScale=Vector3.one*spec.canonicalScale;
            var radius=QuestSoccerBall.EffectiveWorldRadius(sphere.GetComponent<SphereCollider>());sphere.transform.position=QuestSoccerBall.SpawnPosition(anchor,radius);sphere.transform.SetParent(anchor.transform,true);anchor.SetOccupied(true);
            var editable=sphere.AddComponent<AIEditableObject>();editable.objectId=id;editable.displayName="Runtime "+(spec.primitive??"object");editable.labels=new[]{"runtime_puzzle_object",spec.primitive??"object"};editable.editable=false;
            var body=sphere.AddComponent<Rigidbody>();body.isKinematic=true;var grab=sphere.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(spec.initialGrabbable);
            var voice=sphere.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"move_to_preset","place_in"};if(IsSoccerProfile(spec))voice.predefinedPresets=new[]{"soccer_ball"};
            var marker=sphere.AddComponent<C1QuestSphereController>();marker.instanceController=owner;marker.placementAnchorId=owner.ActiveInstance?.c1SpherePlacementAnchorId;
            ApplyInitialSphereProfile(marker,spec,owner);
            if(!string.IsNullOrWhiteSpace(spec.initialSemanticState)){var state=sphere.AddComponent<AuthoringSemanticState>();state.state=spec.initialSemanticState;}
            var containingDrawer=anchor.GetComponentInParent<ExperimentalDrawerController>();
            if(containingDrawer&&containingDrawer.IsOpen)
            {
                if(containingDrawer.TryClose(out var closeError))DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_CONTAINER_CLOSING",null,new { object_id=id,drawer_id=containingDrawer.GetComponent<AIEditableObject>()?.objectId,reason="initial_runtime_object_placement" });
                else DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CONTAINER_CLOSE_FAILED",closeError,new { object_id=id,drawer_id=containingDrawer.GetComponent<AIEditableObject>()?.objectId });
            }
            FindFirstContext()?.SendSceneContextSnapshot("runtime object created");DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_CREATED",null,new { object_id=id,unity_name=sphere.name,declared_anchor_id=spec.initialAnchorId,resolved_anchor_id=resolvedAnchorId,parent_gameobject=anchor.gameObject.name,parent_drawer_id=containingDrawer?containingDrawer.GetComponent<AIEditableObject>()?.objectId:null,position=sphere.transform.position,scale=sphere.transform.lossyScale,tag=sphere.tag,grabbable=grab.grabbable,condition=UnityEngine.Object.FindFirstObjectByType<ExperimentConditionManager>()?.condition.ToString(),source=spec.source });
        }
        private static void ApplyInitialSphereProfile(C1QuestSphereController controller,QuestRuntimeObjectSpec spec,QuestInstanceController owner)
        {
            if(string.IsNullOrWhiteSpace(spec?.sphereProfile))return;
            var condition=UnityEngine.Object.FindFirstObjectByType<ExperimentConditionManager>()?.condition.ToString();
            var setId=QuestCanonicalSetIds.Normalize(owner?.ActiveInstance?.questSetId);
            var error=(string)null;
            var success=controller&&controller.TrySetProfile(spec.sphereProfile,out error);
            var details=new { object_id=spec.objectId,profile=spec.sphereProfile,canonical_set_id=setId,condition,source="initial_runtime_setup",success,resulting_profile=controller?controller.SphereProfile:null,controller_found=controller!=null };
            if(success)DreamCodeVR2ClientLogger.Event("quest","QUEST_RUNTIME_OBJECT_PROFILE_APPLIED",null,details);
            else DreamCodeVR2ClientLogger.Warn("quest","QUEST_RUNTIME_OBJECT_PROFILE_APPLIED",error??"sphere_profile_application_failed",details);
        }
        private static bool IsSoccerProfile(QuestRuntimeObjectSpec spec)=>string.Equals(spec.presetId,"soccer_ball",StringComparison.OrdinalIgnoreCase)||string.Equals(spec.semanticProfile,"soccer_ball",StringComparison.OrdinalIgnoreCase);
        // `soccer_ball_anchor` is a protocol alias for the authored, accessible desk surface.
        // It is deliberately not allowed to fall back to any drawer-inside anchor.
        public static bool TryResolveInitialAnchor(string declaredAnchorId,out AuthoringAnchor anchor,out string resolvedAnchorId,out string error)
        {
            anchor=null;resolvedAnchorId=declaredAnchorId;error=null;
            if(string.IsNullOrWhiteSpace(declaredAnchorId)){error="missing_anchor_id";return false;}
            var exact=FindAnchor(declaredAnchorId,out var duplicate);
            if(duplicate){error="ambiguous_anchor_id";return false;}
            if(exact){anchor=exact;return true;}
            if(string.Equals(declaredAnchorId,"table_001.soccer_ball_anchor",StringComparison.OrdinalIgnoreCase))
            {
                resolvedAnchorId="table_001.desk_surface_anchor";
                anchor=FindAnchor(resolvedAnchorId,out duplicate);
                if(duplicate){error="ambiguous_alias_target";return false;}
                if(anchor){DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_ANCHOR_ALIAS_RESOLVED",null,new { declared_anchor_id=declaredAnchorId,resolved_anchor_id=resolvedAnchorId });return true;}
            }
            error="anchor_not_found";return false;
        }
        private static AuthoringAnchor FindAnchor(string id,out bool duplicate)
        {
            AuthoringAnchor found=null;duplicate=false;
            foreach(var candidate in UnityEngine.Object.FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(candidate&&string.Equals(candidate.anchorId,id,StringComparison.OrdinalIgnoreCase)){if(found){duplicate=true;return null;}found=candidate;}
            return found;
        }
        private static bool TryGetInaccessibleContainer(AuthoringAnchor anchor,out string containerId,out string lockId,out string lockState)
        {
            containerId=null;lockId=null;lockState=null;var drawer=anchor?anchor.GetComponentInParent<ExperimentalDrawerController>():null;if(!drawer)return false;
            containerId=drawer.GetComponent<AIEditableObject>()?.objectId;var lockController=QuestLockController.FindForTarget(containerId);lockId=lockController?lockController.GetComponent<AIEditableObject>()?.objectId:null;lockState=lockController?(lockController.IsLocked?"locked":"unlocked"):"unbound";
            return lockController&&lockController.IsLocked;
        }
        private static SceneContextTransmitter FindFirstContext()=>UnityEngine.Object.FindFirstObjectByType<SceneContextTransmitter>();
    }
}
