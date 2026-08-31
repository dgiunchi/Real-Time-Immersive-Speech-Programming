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
            if(existing){DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_REUSED",null,new { object_id=id,unity_name=existing.gameObject.name,source=spec.source });return;}
            if(!string.Equals(spec.primitive,"sphere",StringComparison.OrdinalIgnoreCase)){DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CREATE_UNSUPPORTED","Runtime primitive is not supported.",new { object_id=id,primitive=spec.primitive });return;}
            var anchor=FindAnchor(spec.initialAnchorId);if(!anchor){DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CREATE_FAILED","Initial placement anchor is unavailable.",new { object_id=id,anchor_id=spec.initialAnchorId });return;}
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=id;sphere.tag="game";sphere.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);
            if(IsSoccerProfile(spec))QuestSoccerBall.SetWorldDiameter(sphere.transform);else if(spec.canonicalSizeMeters>0f)sphere.transform.localScale=Vector3.one*spec.canonicalSizeMeters;else if(spec.canonicalScale>0f)sphere.transform.localScale=Vector3.one*spec.canonicalScale;
            var radius=QuestSoccerBall.EffectiveWorldRadius(sphere.GetComponent<SphereCollider>());sphere.transform.position=QuestSoccerBall.SpawnPosition(anchor,radius);sphere.transform.SetParent(anchor.transform,true);anchor.SetOccupied(true);
            var editable=sphere.AddComponent<AIEditableObject>();editable.objectId=id;editable.displayName="Runtime "+(spec.primitive??"object");editable.labels=new[]{"runtime_puzzle_object",spec.primitive??"object"};editable.editable=false;
            var body=sphere.AddComponent<Rigidbody>();body.isKinematic=true;var grab=sphere.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(spec.initialGrabbable);
            var voice=sphere.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"move_to_preset","place_in"};if(IsSoccerProfile(spec))voice.predefinedPresets=new[]{"soccer_ball"};
            var marker=sphere.AddComponent<C1QuestSphereController>();marker.instanceController=owner;marker.placementAnchorId=owner.ActiveInstance?.c1SpherePlacementAnchorId;
            if(!string.IsNullOrWhiteSpace(spec.initialSemanticState)){var state=sphere.AddComponent<AuthoringSemanticState>();state.state=spec.initialSemanticState;}
            var containingDrawer=anchor.GetComponentInParent<ExperimentalDrawerController>();
            if(containingDrawer&&containingDrawer.IsOpen)
            {
                if(containingDrawer.TryClose(out var closeError))DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_CONTAINER_CLOSING",null,new { object_id=id,drawer_id=containingDrawer.GetComponent<AIEditableObject>()?.objectId,reason="initial_runtime_object_placement" });
                else DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CONTAINER_CLOSE_FAILED",closeError,new { object_id=id,drawer_id=containingDrawer.GetComponent<AIEditableObject>()?.objectId });
            }
            FindFirstContext()?.SendSceneContextSnapshot("runtime object created");DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_CREATED",null,new { object_id=id,unity_name=sphere.name,position=sphere.transform.position,scale=sphere.transform.lossyScale,tag=sphere.tag,grabbable=grab.grabbable,condition=UnityEngine.Object.FindFirstObjectByType<ExperimentConditionManager>()?.condition.ToString(),source=spec.source });
        }
        private static bool IsSoccerProfile(QuestRuntimeObjectSpec spec)=>string.Equals(spec.presetId,"soccer_ball",StringComparison.OrdinalIgnoreCase)||string.Equals(spec.semanticProfile,"soccer_ball",StringComparison.OrdinalIgnoreCase);
        private static AuthoringAnchor FindAnchor(string id){foreach(var anchor in UnityEngine.Object.FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(anchor&&anchor.anchorId==id)return anchor;return null;}
        private static SceneContextTransmitter FindFirstContext()=>UnityEngine.Object.FindFirstObjectByType<SceneContextTransmitter>();
    }
}
