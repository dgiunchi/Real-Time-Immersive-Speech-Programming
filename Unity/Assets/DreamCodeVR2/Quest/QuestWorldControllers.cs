using System;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using DreamCodeVR2.SceneContext;
using TMPro;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    [Serializable] public class QuestLockBinding { public string lockId; public string requiredKeyId; public string targetObjectId; }
    [Serializable] public class QuestNoteBinding { public string noteId; public string text; public bool visible; public string anchorId; }
    [Serializable] public class QuestPlacementBinding { public string objectId; public string anchorId; }
    [Serializable] public class QuestInitialStateBinding { public string objectId; public string state; }
    [Serializable] public class QuestRuntimeObjectSpec { public string objectId; public string primitive; public string semanticProfile; public string presetId; public string materialProfile; public string initialAnchorId; public string initialSemanticState; public bool initialGrabbable; public float canonicalSizeMeters; public float canonicalScale; public string source; }
    [Serializable] public class QuestInstance { public string questId; public string questSetId; public QuestLockBinding[] lockBindings; public QuestNoteBinding[] notes; public QuestPlacementBinding[] placements; public QuestInitialStateBinding[] initialStates; public QuestRuntimeObjectSpec[] requiredRuntimeObjects; public string[] relevantObjectIds; public string selectedLampId; public string targetDrawerId; public QuestPlan plan; public bool requiresC1Sphere; public string c1SphereId="sphere_001"; public string c1SphereStartAnchorId; public string c1SpherePlacementAnchorId; }

    public static class QuestSoccerBall
    {
        // Unity primitive spheres have a one-metre diameter at local scale one.
        public const float CanonicalDiameterMeters = 0.16f;
        public const float CanonicalRadiusMeters = CanonicalDiameterMeters * 0.5f;
        public static Vector3 SpawnPosition(AuthoringAnchor anchor, float effectiveWorldRadius)
        {
            return anchor && anchor.placementMode == AnchorPlacementMode.Surface
                ? anchor.transform.position + anchor.transform.up * effectiveWorldRadius
                : anchor ? anchor.transform.position : Vector3.zero;
        }
        public static float EffectiveWorldRadius(SphereCollider collider)
        {
            if (!collider) return CanonicalRadiusMeters;
            var scale=collider.transform.lossyScale;
            return collider.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
        }
        public static void SetWorldDiameter(Transform target)
        {
            if (!target) return;
            var parentScale=target.parent?target.parent.lossyScale:Vector3.one;
            target.localScale=new Vector3(
                CanonicalDiameterMeters/Mathf.Max(Mathf.Abs(parentScale.x),.0001f),
                CanonicalDiameterMeters/Mathf.Max(Mathf.Abs(parentScale.y),.0001f),
                CanonicalDiameterMeters/Mathf.Max(Mathf.Abs(parentScale.z),.0001f));
        }
    }

    // Deliberately protocol-agnostic: the experiment/session layer may configure this component
    // without exposing bindings through participant-facing labels.
    public class QuestInstanceController : MonoBehaviour
    {
        public QuestInstance ActiveInstance { get; private set; }
        public ResolvedQuestInstance ActiveResolvedInstance { get; private set; }
        public QuestRuntimeState runtimeState;
        private readonly System.Collections.Generic.Dictionary<string,string> previousRequiredKeyIds=new System.Collections.Generic.Dictionary<string,string>(StringComparer.Ordinal);
        public void Apply(QuestInstance instance)
        {
            if(instance==null)return;DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_RECEIVED",null,new { quest_instance_id=instance.questId,quest_set_id=instance.questSetId });ActiveResolvedInstance=QuestInstanceResolver.Resolve(instance);ActiveInstance=instance;if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();
            ResetControlledState();
            EnsureRuntimeObjects(ActiveResolvedInstance);
            foreach(var binding in ActiveResolvedInstance.lockBindings)ApplyLockBinding(instance,binding);
            ApplyPlacements(ActiveResolvedInstance.placements);
            FindFirstObjectByType<QuestObjectVisibilityController>()?.ApplyFixedInstance(instance);
            foreach(var note in instance.notes??Array.Empty<QuestNoteBinding>()){var item=AuthoringActionExecutor.FindEditable(note.noteId);item?.GetComponent<QuestNoteController>()?.Configure(note.text,note.visible);}
            ConfigureA1DrawerContents(ActiveResolvedInstance);
            ApplyInitialStates(instance.initialStates);
            CloseRuntimeObjectContainersUnlessExplicitlyOpen(ActiveResolvedInstance,instance.initialStates);
            LogLockBindingSummary(instance.questId);
            if(!string.IsNullOrWhiteSpace(instance.selectedLampId))foreach(var lamp in FindObjectsByType<QuestLampController>(FindObjectsInactive.Include,FindObjectsSortMode.None))lamp.SetLampState(lamp.GetComponent<AIEditableObject>()?.objectId==instance.selectedLampId);
            if(instance.plan!=null)runtimeState?.StartQuest(instance.plan);DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_APPLIED",null,new { quest_id=instance.questId });
            var manager=FindFirstObjectByType<ExperimentConditionManager>();
            if(manager&&manager.condition==ExperimentCondition.VoiceCommandBaseline)
            {
                foreach(var painting in FindObjectsByType<QuestPaintingController>(FindObjectsInactive.Include,FindObjectsSortMode.None))painting.SetPhysicalManipulation(false);
                foreach(var key in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(key&&key.objectId!=null&&key.objectId.IndexOf("key",StringComparison.OrdinalIgnoreCase)>=0){var grab=key.GetComponent<ExperimentalGrabbableAdapter>();if(grab){grab.SetGrabbable(false);DreamCodeVR2ClientLogger.Event("quest","C1_GRABBABLE_BLOCKED",null,new { object_id=key.objectId });}}
            }
            else if(manager){foreach(var painting in FindObjectsByType<QuestPaintingController>(FindObjectsInactive.Include,FindObjectsSortMode.None))painting.SetPhysicalManipulation(true);}
        }
        public void ActivateServerTask(QuestTaskSpec task)
        {
            if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();
            runtimeState?.ActivateAppendedServerTask(task);
        }
        public bool AllowsC1PlaceIn(string objectId,string receptacleId,out AuthoringAnchor anchor)
        {
            anchor=null;var instance=ActiveInstance;if(instance==null||!instance.requiresC1Sphere||string.IsNullOrWhiteSpace(instance.c1SpherePlacementAnchorId)||!string.Equals(instance.c1SphereId,objectId,StringComparison.Ordinal)||!string.Equals(receptacleId,"basket_001",StringComparison.Ordinal))return false;
            foreach(var candidate in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(candidate&&candidate.anchorId==instance.c1SpherePlacementAnchorId){anchor=candidate;return true;}return false;
        }
        public void ClearC1QuestSphere(){foreach(var item in FindObjectsByType<C1QuestSphereController>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(item)Destroy(item.gameObject);}
        private void EnsureRuntimeObjects(ResolvedQuestInstance resolved){foreach(var spec in resolved?.requiredRuntimeObjects??Array.Empty<QuestRuntimeObjectSpec>())QuestRuntimeObjectFactory.Ensure(spec,this);}
        private void ResetControlledState(){previousRequiredKeyIds.Clear();FindFirstObjectByType<QuestObjectVisibilityController>()?.RestoreAll();ClearC1QuestSphere();foreach(var inserted in FindObjectsByType<QuestInsertedKeyState>(FindObjectsInactive.Include,FindObjectsSortMode.None))inserted.Restore();foreach(var reveal in FindObjectsByType<QuestDrawerContentsReveal>(FindObjectsInactive.Include,FindObjectsSortMode.None))reveal.ClearConfiguration();foreach(var drawer in FindObjectsByType<ExperimentalDrawerController>(FindObjectsInactive.Include,FindObjectsSortMode.None))drawer.ResetClosed();foreach(var lockController in FindObjectsByType<QuestLockController>(FindObjectsInactive.Include,FindObjectsSortMode.None)){var item=lockController.GetComponent<AIEditableObject>();if(item&&!string.IsNullOrWhiteSpace(item.objectId))previousRequiredKeyIds[item.objectId]=lockController.requiredKeyId;lockController.ClearQuestBinding();}foreach(var door in FindObjectsByType<QuestDoorController>(FindObjectsInactive.Include,FindObjectsSortMode.None))door.TryClose(out _);foreach(var painting in FindObjectsByType<QuestPaintingController>(FindObjectsInactive.Include,FindObjectsSortMode.None))painting.ResetCrooked();foreach(var lamp in FindObjectsByType<QuestLampController>(FindObjectsInactive.Include,FindObjectsSortMode.None))lamp.SetLampState(false);foreach(var clue in FindObjectsByType<QuestNoteController>(FindObjectsInactive.Include,FindObjectsSortMode.None))clue.ResetToDefault(false);}
        private void ApplyLockBinding(QuestInstance instance,QuestLockBinding binding)
        {
            var item=AuthoringActionExecutor.FindEditable(binding?.lockId);var controller=item?item.GetComponent<QuestLockController>():null;previousRequiredKeyIds.TryGetValue(binding?.lockId??string.Empty,out var previous);
            if(controller)controller.Configure(binding.requiredKeyId,binding.targetObjectId);
            DreamCodeVR2ClientLogger.Event("quest","QUEST_LOCK_BINDING_APPLIED",null,new { quest_instance_id=instance?.questId,lock_id=binding?.lockId,required_key_id=binding?.requiredKeyId,lock_gameobject_name=item?item.gameObject.name:null,lock_controller_found=controller!=null,previous_required_key_id=previous,new_required_key_id=controller?.requiredKeyId });
            var target=AuthoringActionExecutor.FindEditable(binding?.targetObjectId);DreamCodeVR2ClientLogger.Event("quest","QUEST_LOCK_TARGET_BOUND",null,new { quest_instance_id=instance?.questId,lock_id=binding?.lockId,drawer_id=binding?.targetObjectId,lock_controller_instance_id=controller?controller.GetInstanceID():0,drawer_controller_found=target&&target.GetComponent<ExperimentalDrawerController>(),drawer_has_box_collider=target&&target.GetComponent<BoxCollider>() });
            if(!controller)DreamCodeVR2ClientLogger.Warn("quest","QUEST_LOCK_BINDING_MISSING_CONTROLLER","Quest binding did not resolve a canonical lock controller.",new { quest_instance_id=instance?.questId,lock_id=binding?.lockId,required_key_id=binding?.requiredKeyId });
        }
        private static void LogLockBindingSummary(string questInstanceId)
        {
            var active=new System.Collections.Generic.List<object>();
            foreach(var controller in FindObjectsByType<QuestLockController>(FindObjectsInactive.Include,FindObjectsSortMode.None)){var item=controller?controller.GetComponent<AIEditableObject>():null;active.Add(new { lock_id=item?.objectId,required_key_id=controller?.requiredKeyId,associated_target_object_id=controller?.associatedTargetObjectId,is_locked=controller?.IsLocked });}
            DreamCodeVR2ClientLogger.Event("quest","QUEST_LOCK_BINDING_SUMMARY",null,new { quest_instance_id=questInstanceId,active_locks=active.ToArray() });
        }
        private static AuthoringAnchor FindAnchor(string anchorId){foreach(var anchor in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(anchor&&anchor.anchorId==anchorId)return anchor;return null;}
        private static void CloseRuntimeObjectContainersUnlessExplicitlyOpen(ResolvedQuestInstance resolved,QuestInitialStateBinding[] initialStates)
        {
            foreach(var spec in resolved?.requiredRuntimeObjects??Array.Empty<QuestRuntimeObjectSpec>())
            {
                var anchor=FindAnchor(spec?.initialAnchorId);var drawer=anchor?anchor.GetComponentInParent<ExperimentalDrawerController>():null;
                if(!drawer)continue;
                var drawerId=drawer.GetComponent<AIEditableObject>()?.objectId;
                var explicitlyOpen=false;
                foreach(var state in initialStates??Array.Empty<QuestInitialStateBinding>())
                    if(string.Equals(QuestCanonicalIds.NormalizeTaskObject(resolved.questId,state?.objectId),drawerId,StringComparison.OrdinalIgnoreCase)&&string.Equals(state?.state,"open",StringComparison.OrdinalIgnoreCase)){explicitlyOpen=true;break;}
                if(explicitlyOpen)continue;
                if(drawer.TryClose(out var error))DreamCodeVR2ClientLogger.Event("quest","RUNTIME_OBJECT_CONTAINER_CLOSED",null,new { object_id=spec.objectId,drawer_id=drawerId,reason="initial_runtime_object_placement" });
                else DreamCodeVR2ClientLogger.Warn("quest","RUNTIME_OBJECT_CONTAINER_CLOSE_FAILED",error,new { object_id=spec.objectId,drawer_id=drawerId });
            }
        }
        private static void ApplyPlacements(QuestPlacementBinding[] placements)
        {
            var claimedExclusiveAnchors=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var placement in placements??Array.Empty<QuestPlacementBinding>())
            {
                var item=AuthoringActionExecutor.FindEditable(placement?.objectId);var anchor=FindAnchor(placement?.anchorId);
                if(!item||!anchor){DreamCodeVR2ClientLogger.Warn("quest","QUEST_INSTANCE_PLACEMENT_IGNORED","Quest placement could not resolve its object or anchor.",new { object_id=placement?.objectId,anchor_id=placement?.anchorId });continue;}
                if(anchor.occupancyPolicy!=AnchorOccupancyPolicy.Multiple&&!claimedExclusiveAnchors.Add(anchor.anchorId)){DreamCodeVR2ClientLogger.Warn("quest","QUEST_PLACEMENT_CAPACITY_EXCEEDED","Quest configuration places multiple independent objects in an exclusive region.",new { object_id=placement.objectId,anchor_id=anchor.anchorId,capacity=anchor.occupancyPolicy.ToString() });continue;}
                // Clue-note transforms are intentionally authored scene composition (for
                // example, below the painting). The payload's anchor assignment identifies
                // the clue context; it must not relocate the rendered note to a ball anchor.
                if(item.GetComponent<QuestNoteController>()||item.objectId?.StartsWith("clue_note",StringComparison.OrdinalIgnoreCase)==true){DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_CLUE_POSITION_PRESERVED",null,new { object_id=placement.objectId,anchor_id=placement.anchorId });continue;}
                item.transform.SetParent(anchor.transform,true);item.transform.rotation=anchor.transform.rotation;item.transform.position=anchor.placementMode==AnchorPlacementMode.Surface?anchor.transform.position+anchor.transform.up*SupportExtentAlong(item.transform,anchor.transform.up):anchor.transform.position;anchor.SetOccupied(true);
                DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_PLACEMENT_APPLIED",null,new { object_id=placement.objectId,anchor_id=placement.anchorId });
            }
        }
        private static void ConfigureA1DrawerContents(ResolvedQuestInstance resolved)
        {
            // The deployed A1 setup placed key_002 on the desk even though the intended
            // progression makes it a reward inside the drawer opened with key_001.
            if(!string.Equals(resolved?.questId,"set_a_instance_1",StringComparison.OrdinalIgnoreCase))return;
            var drawerId=resolved.targetDrawerId;
            var drawerItem=AuthoringActionExecutor.FindEditable(drawerId);
            var drawer=drawerItem?drawerItem.GetComponent<ExperimentalDrawerController>():null;
            var anchor=FindAnchor(drawerId+".drawer_inside_anchor");
            if(!drawer||!anchor){DreamCodeVR2ClientLogger.Warn("quest","QUEST_DRAWER_CONTENTS_CONFIGURATION_FAILED","Quest drawer contents could not resolve the selected drawer or its inside anchor.",new { quest_instance_id=resolved.questId,drawer_id=drawerId,anchor_id=drawerId+".drawer_inside_anchor",drawer_found=drawer!=null,anchor_found=anchor!=null });return;}
            var contents=new System.Collections.Generic.List<GameObject>();
            foreach(var contentId in new[]{"key_002","clue_note_002"})
            {
                var item=AuthoringActionExecutor.FindEditable(contentId);
                if(!item){DreamCodeVR2ClientLogger.Warn("quest","QUEST_DRAWER_CONTENT_MISSING","Quest drawer content is unavailable.",new { quest_instance_id=resolved.questId,drawer_id=drawerId,object_id=contentId });continue;}
                if(contentId=="clue_note_002")item.GetComponent<QuestNoteController>()?.Configure("The Silver Key opens the exit door.",false);
                PlaceDrawerContent(drawerId,anchor,item,contentId=="clue_note_002"?"drawer_note_anchor":"drawer_key_anchor");contents.Add(item.gameObject);
            }
            var reveal=drawer.GetComponent<QuestDrawerContentsReveal>()??drawer.gameObject.AddComponent<QuestDrawerContentsReveal>();
            reveal.Configure(resolved.questId,drawerId,contents.ToArray());
        }
        private static void PlaceDrawerContent(string drawerId,AuthoringAnchor insideAnchor,AIEditableObject item,string anchorName)
        {
            var slot=insideAnchor.transform.Find(anchorName);
            if(!slot)
            {
                slot=new GameObject(anchorName).transform;slot.SetParent(insideAnchor.transform,false);
                // These named drawer-local anchors deliberately keep the key and note apart.
                slot.localPosition=anchorName=="drawer_key_anchor"?new Vector3(-.075f,.012f,0):new Vector3(.025f,0f,0);
                slot.localRotation=Quaternion.identity;
            }
            item.transform.SetParent(slot,false);item.transform.localPosition=Vector3.zero;item.transform.localRotation=Quaternion.identity;
            if(anchorName=="drawer_note_anchor"){AlignNoteFaceUp(item,slot);RestOnAnchorSurface(item,slot);}
            DreamCodeVR2ClientLogger.Event("quest","QUEST_DRAWER_CONTENT_PLACED",null,new { drawer_id=drawerId,object_id=item.objectId,anchor_id=drawerId+"."+anchorName,world_position=item.transform.position,world_rotation=item.transform.rotation,local_position=slot.localPosition });
        }
        private static void AlignNoteFaceUp(AIEditableObject note,Transform slot)
        {
            var text=note.GetComponentInChildren<TMP_Text>(true);
            if(!text)return;
            // TMP's visible face is negative-forward. Then resolve the remaining roll
            // using the actual text top direction and the anchor's readable direction.
            var readableNormal=-text.transform.forward;note.transform.rotation=Quaternion.FromToRotation(readableNormal,slot.up)*note.transform.rotation;
            var projectedTextUp=Vector3.ProjectOnPlane(text.transform.up,slot.up).normalized;var desiredUp=Vector3.ProjectOnPlane(slot.forward,slot.up).normalized;
            if(projectedTextUp.sqrMagnitude>.0001f&&desiredUp.sqrMagnitude>.0001f)note.transform.Rotate(slot.up,Vector3.SignedAngle(projectedTextUp,desiredUp,slot.up),Space.World);
            var faceDot=Vector3.Dot(-text.transform.forward,slot.up);var upDot=Vector3.Dot(Vector3.ProjectOnPlane(text.transform.up,slot.up).normalized,desiredUp);
            DreamCodeVR2ClientLogger.Event("quest","QUEST_NOTE_READABILITY_ALIGNED",null,new { object_id=note.objectId,readable_face_dot=faceDot,text_up_dot=upDot,surface_normal=slot.up,readable_up=desiredUp });
        }
        private static void RestOnAnchorSurface(AIEditableObject item,Transform slot)
        {
            var up=slot.up.normalized;var found=false;var bounds=new Bounds(item.transform.position,Vector3.zero);
            foreach(var renderer in item.GetComponentsInChildren<Renderer>(true)){if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
            if(!found)return;
            var extent=SupportExtentAlong(item.transform,up);var lowest=Vector3.Dot(bounds.center,up)-extent;
            item.transform.position+=up*(Vector3.Dot(slot.position,up)-lowest);
        }
        private static float SupportExtentAlong(Transform item,Vector3 up)
        {
            var bounds=new Bounds(item.position,Vector3.zero);var found=false;
            foreach(var renderer in item.GetComponentsInChildren<Renderer>(true)){if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
            foreach(var collider in item.GetComponentsInChildren<Collider>(true)){if(!found){bounds=collider.bounds;found=true;}else bounds.Encapsulate(collider.bounds);}
            if(!found)return 0f;var e=bounds.extents;var n=up.normalized;return Mathf.Abs(n.x)*e.x+Mathf.Abs(n.y)*e.y+Mathf.Abs(n.z)*e.z;
        }
        private static void ApplyInitialStates(QuestInitialStateBinding[] states)
        {
            foreach(var state in states??Array.Empty<QuestInitialStateBinding>())
            {
                var item=AuthoringActionExecutor.FindEditable(state?.objectId);if(!item)continue;var value=(state.state??string.Empty).ToLowerInvariant();
                var drawer=item.GetComponent<ExperimentalDrawerController>();var door=item.GetComponent<QuestDoorController>();var lamp=item.GetComponent<QuestLampController>();var lockController=item.GetComponent<QuestLockController>()??QuestLockController.FindForTarget(item.objectId);string error;
                if(value=="open"&&drawer)drawer.TryOpen(out error); else if(value=="closed"&&drawer)drawer.TryClose(out error);
                else if(value=="open"&&door)door.TryOpen(out error); else if(value=="closed"&&door)door.TryClose(out error);
                else if((value=="active"||value=="on")&&lamp)lamp.SetLampState(true); else if((value=="inactive"||value=="off")&&lamp)lamp.SetLampState(false);
                else if(value=="locked"&&lockController)lockController.ResetLocked();
                else {DreamCodeVR2ClientLogger.Warn("quest","QUEST_INSTANCE_INITIAL_STATE_IGNORED","Quest initial state has no compatible local controller.",new { object_id=state?.objectId,state=state?.state });continue;}
                DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_INITIAL_STATE_APPLIED",null,new { object_id=state.objectId,state=state.state });
            }
        }
        private void CreateC1QuestSphere(QuestInstance instance)
        {
            var sphereId=string.IsNullOrWhiteSpace(instance.c1SphereId)?"sphere_001":instance.c1SphereId;
            if(string.IsNullOrWhiteSpace(instance.c1SphereStartAnchorId)){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest instance does not define a start anchor.",new { sphere_id=sphereId,requested_start_anchor=instance.c1SphereStartAnchorId,resolved_anchor=(string)null });return;}
            AuthoringAnchor anchor=null;foreach(var candidate in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(candidate&&candidate.anchorId==instance.c1SphereStartAnchorId){anchor=candidate;break;}
            if(!anchor){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest sphere start anchor is unavailable.",new { sphere_id=sphereId,requested_start_anchor=instance.c1SphereStartAnchorId,resolved_anchor=(string)null });return;}
            var id=sphereId;if(AuthoringActionExecutor.FindEditable(id)){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest sphere ID is already in use.",new { sphere_id=id,requested_start_anchor=instance.c1SphereStartAnchorId,resolved_anchor=anchor.anchorId });return;}
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=id;sphere.tag="game";sphere.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);sphere.transform.SetParent(anchor.transform,true);QuestSoccerBall.SetWorldDiameter(sphere.transform);
            var radius=QuestSoccerBall.EffectiveWorldRadius(sphere.GetComponent<SphereCollider>());sphere.transform.position=QuestSoccerBall.SpawnPosition(anchor,radius);
            var editable=sphere.AddComponent<AIEditableObject>();editable.objectId=id;editable.displayName="Quest Sphere";editable.labels=new[]{"quest_sphere","sphere","primitive"};editable.editable=false;
            var body=sphere.AddComponent<Rigidbody>();body.isKinematic=true;var grab=sphere.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(false);DreamCodeVR2ClientLogger.Event("quest","C1_GRABBABLE_BLOCKED",null,new { object_id=id });
            var voice=sphere.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"move_to_preset","place_in"};voice.predefinedPresets=new[]{"soccer_ball"};
            var marker=sphere.AddComponent<C1QuestSphereController>();marker.instanceController=this;marker.placementAnchorId=instance.c1SpherePlacementAnchorId;anchor.SetOccupied(true);
            FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("C1 quest sphere created");DreamCodeVR2ClientLogger.Event("quest","C1_QUEST_SPHERE_CREATED",null,new { sphere_id=id,requested_start_anchor=instance.c1SphereStartAnchorId,resolved_anchor=anchor.anchorId,placement_mode=anchor.placementMode.ToString(),diameter_m=QuestSoccerBall.CanonicalDiameterMeters,effective_radius_m=radius });
        }
    }

    public class C1QuestSphereController : MonoBehaviour
    {
        public QuestInstanceController instanceController; public string placementAnchorId; public bool SoccerBallAppearanceApplied { get; private set; }
        public bool TryApplySoccerBallPreset(out string error)
        {
            var material=Resources.Load<Material>("SoccerBall");
            if(!material){error="Soccer-ball material asset Resources/SoccerBall is not available.";DreamCodeVR2ClientLogger.Warn("quest","SOCCER_BALL_PRESET_FAILED",error,new { object_id=Id() });return false;}
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.material=material;
            SoccerBallAppearanceApplied=true;var editable=GetComponent<AIEditableObject>();if(editable){var labels=new System.Collections.Generic.List<string>(editable.labels??Array.Empty<string>());if(!labels.Contains("soccer_ball"))labels.Add("soccer_ball");if(!labels.Contains("ball"))labels.Add("ball");editable.labels=labels.ToArray();}
            var state=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();state.state="soccer_ball";FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("soccer ball preset");DreamCodeVR2ClientLogger.Event("quest","SOCCER_BALL_PRESET_APPLIED",null,new { object_id=Id() });error=null;return true;
        }
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
    }

    // Quest bindings, rather than labels or colours, decide which key opens a lock.
    public class QuestLockController : MonoBehaviour
    {
        public string requiredKeyId;
        public string associatedTargetObjectId;
        public bool IsLocked { get; private set; } = true;
        public bool IsUnlocked => !IsLocked;
        public QuestEventBus eventBus; public SceneContextTransmitter sceneContext;
        private static readonly System.Collections.Generic.Dictionary<string,int> lastSuccessfulControllerByTarget=new System.Collections.Generic.Dictionary<string,int>(StringComparer.Ordinal);
        public void Configure(string keyId, string targetId, bool locked = true) { var before=IsLocked;requiredKeyId=keyId; associatedTargetObjectId=targetId; IsLocked=locked;Trace("LOCK_STATE_CONFIGURED",null,before); Publish("configured"); }
        public void ClearQuestBinding(){var before=IsLocked;if(!string.IsNullOrWhiteSpace(associatedTargetObjectId))lastSuccessfulControllerByTarget.Remove(associatedTargetObjectId);requiredKeyId=null;associatedTargetObjectId=null;IsLocked=true;Trace("LOCK_STATE_BINDING_CLEARED",null,before);Publish("binding cleared");}
        public bool TryUseKey(string keyId, out string error)
        {
            var isLockedBefore=IsLocked;var bindingMatch=!string.IsNullOrWhiteSpace(requiredKeyId)&&string.Equals(requiredKeyId,keyId,StringComparison.Ordinal);
            DreamCodeVR2ClientLogger.Event("quest", "LOCK_USE_ATTEMPT", null, new { lock_id=Id(), incoming_key_id=keyId,required_key_id=requiredKeyId,is_locked_before=isLockedBefore,controller_instance_id=GetInstanceID(),binding_match=bindingMatch });
            if (!IsLocked) { error="The lock is already unlocked."; return false; }
            if (!bindingMatch) { error="That key does not fit this lock."; DreamCodeVR2ClientLogger.Event("quest", "LOCK_WRONG_KEY", error, new { lock_id=Id(), incoming_key_id=keyId,required_key_id=requiredKeyId,binding_match=false }); return false; }
            IsLocked=false;if(!string.IsNullOrWhiteSpace(associatedTargetObjectId))lastSuccessfulControllerByTarget[associatedTargetObjectId]=GetInstanceID(); SnapKeyIntoLock(keyId); error=null; Trace("LOCK_USE_SUCCESS",keyId,isLockedBefore);DreamCodeVR2ClientLogger.Event("quest", "LOCK_UNLOCKED", null, new { lock_id=Id(), key_id=keyId, target_id=associatedTargetObjectId,is_locked_after=IsLocked,is_unlocked_after=IsUnlocked,controller_instance_id=GetInstanceID() }); eventBus?.Publish(QuestEventType.LockOpened,Id(),keyId);Trace("LOCK_STATE_AFTER_UNLOCK_EVENT",keyId,isLockedBefore); Publish("unlocked");Trace("LOCK_STATE_AFTER_SCENE_CONTEXT_REFRESH",keyId,isLockedBefore); return true;
        }
        public void ResetLocked() { var before=IsLocked;IsLocked=true;Trace("LOCK_STATE_RESET_LOCKED",null,before); Publish("locked"); }
        public static QuestLockController FindForTarget(string targetObjectId)
        {
            if (string.IsNullOrWhiteSpace(targetObjectId)) return null;
            foreach (var candidate in FindObjectsByType<QuestLockController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate && string.Equals(candidate.associatedTargetObjectId, targetObjectId, StringComparison.Ordinal)) return candidate;
            return null;
        }
        public static bool CanOpenTarget(string targetObjectId,out string error)
        {
            var controller=FindForTarget(targetObjectId);lastSuccessfulControllerByTarget.TryGetValue(targetObjectId,out var successfulControllerId);
            var locked=controller&&controller.IsLocked;
            var drawer=AuthoringActionExecutor.FindEditable(targetObjectId)?.GetComponent<ExperimentalDrawerController>();
            DreamCodeVR2ClientLogger.Event("quest","DRAWER_OPEN_GATE",null,new { drawer_id=targetObjectId,resolved_lock_id=controller?controller.Id():null,lock_controller_instance_id=controller?controller.GetInstanceID():0,unlock_controller_instance_id=successfulControllerId,same_lock_controller_as_unlock=successfulControllerId!=0&&controller&&successfulControllerId==controller.GetInstanceID(),lock_is_locked=locked,lock_is_unlocked=controller&&controller.IsUnlocked,drawer_local_locked_state=(bool?)null,drawer_controller_found=drawer!=null,open_allowed=!locked });
            if(locked){error="The target is locked.";return false;} error=null;return true;
        }
        private void SnapKeyIntoLock(string keyId)
        {
            var key=AuthoringActionExecutor.FindEditable(keyId);
            if(!key){DreamCodeVR2ClientLogger.Warn("quest","KEY_INSERTION_FAILED","The accepted key could not be resolved for visual insertion.",new { lock_id=Id(),key_id=keyId });return;}
            var inserted=key.GetComponent<QuestInsertedKeyState>()??key.gameObject.AddComponent<QuestInsertedKeyState>();
            inserted.SnapToLock(transform,Id(),keyId);
        }
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
        private void Trace(string eventName,string incomingKeyId,bool isLockedBefore){DreamCodeVR2ClientLogger.Event("quest",eventName,null,new { lock_id=Id(),required_key_id=requiredKeyId,incoming_key_id=incomingKeyId,is_locked_before=isLockedBefore,is_locked_after=IsLocked,is_unlocked_after=IsUnlocked,controller_instance_id=GetInstanceID(),target_id=associatedTargetObjectId });}
        private void Publish(string state){var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state=IsLocked?"locked":"unlocked";sceneContext?.SendSceneContextSnapshot("lock "+state);}
    }

    // Captures a key's current quest placement before the successful USE_WITH action,
    // then restores it when the quest is reset or replaced.
    public class QuestInsertedKeyState : MonoBehaviour
    {
        private Transform originalParent; private Vector3 originalPosition; private Quaternion originalRotation; private Vector3 originalLocalScale; private Transform insertionPose; private float visualCenterHeightOffset; private bool captured; private Rigidbody body; private bool wasKinematic; private ExperimentalGrabbableAdapter grabbable; private bool wasGrabbable;
        public void SnapToLock(Transform lockTransform,string lockId,string keyId)
        {
            if(!captured){captured=true;originalParent=transform.parent;originalPosition=transform.position;originalRotation=transform.rotation;originalLocalScale=transform.localScale;body=GetComponent<Rigidbody>();if(body)wasKinematic=body.isKinematic;grabbable=GetComponent<ExperimentalGrabbableAdapter>();if(grabbable)wasGrabbable=grabbable.grabbable;}
            var slot=lockTransform.Find("key_insert_anchor");var usedAuthoredAnchor=slot!=null;
            if(!slot){var go=new GameObject("key_insert_anchor");slot=go.transform;slot.SetParent(lockTransform,false);slot.localPosition=KeyInsertLocalOffset;slot.localRotation=Quaternion.identity;}
            if(body)body.isKinematic=true;if(grabbable)grabbable.SetGrabbable(false);
            // The imported lock has a non-uniform scale (.02/.05/.02). Parenting a
            // rendered key below it produces a sheared key even with worldPositionStays.
            // Keep the key under its original hierarchy and follow the lock pose in world
            // space instead; this also keeps it attached when the containing drawer moves.
            insertionPose=slot;ApplyInsertionPose(true);
            FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("key inserted into lock");
            var lockParentItem=lockTransform.parent?lockTransform.parent.GetComponent<AIEditableObject>():null;
            DreamCodeVR2ClientLogger.Event("quest","KEY_INSERT_POSE_APPLIED",null,new { key_id=keyId,lock_id=lockId,anchor_name=slot.name,used_authored_anchor=usedAuthoredAnchor,follow_mode="world_pose_without_reparenting",lock_parent_object_id=lockParentItem?lockParentItem.objectId:null,visual_center_height_offset=visualCenterHeightOffset,world_position=transform.position,world_rotation=transform.rotation,world_scale=transform.lossyScale });
            DreamCodeVR2ClientLogger.Event("quest","KEY_SNAPPED_TO_LOCK",null,new { key_id=keyId,lock_id=lockId,slot_name=slot.name,slot_created=!usedAuthoredAnchor });
        }
        // The imported lock pivot is slightly above the visible keyhole.
        private static readonly Vector3 KeyInsertLocalOffset=new Vector3(0f,-.15f,1.15f);
        private void LateUpdate(){if(captured&&insertionPose)ApplyInsertionPose(false);}
        private void ApplyInsertionPose(bool measureVisualOffset)
        {
            transform.SetPositionAndRotation(insertionPose.position,insertionPose.rotation);
            if(measureVisualOffset)visualCenterHeightOffset=VisualBounds().center.y-transform.position.y;
            transform.position-=Vector3.up*visualCenterHeightOffset;
        }
        private Bounds VisualBounds()
        {
            var found=false;var bounds=new Bounds(transform.position,Vector3.zero);
            foreach(var renderer in GetComponentsInChildren<Renderer>(true)){if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
            return bounds;
        }
        public void Restore()
        {
            if(!captured)return;insertionPose=null;visualCenterHeightOffset=0f;transform.SetParent(originalParent,true);transform.SetPositionAndRotation(originalPosition,originalRotation);transform.localScale=originalLocalScale;if(body)body.isKinematic=wasKinematic;if(grabbable)grabbable.SetGrabbable(wasGrabbable);captured=false;
            DreamCodeVR2ClientLogger.Event("quest","KEY_INSERTION_RESTORED",null,new { key_id=GetComponent<AIEditableObject>()?.objectId });
        }
    }

    // Hides A1 rewards until its locked drawer completes an opening motion.
    public class QuestDrawerContentsReveal : MonoBehaviour
    {
        private string questId; private string drawerId; private GameObject[] contents=Array.Empty<GameObject>(); private ExperimentalDrawerController drawer;
        public void Configure(string newQuestId,string newDrawerId,GameObject[] newContents)
        {
            ClearConfiguration();questId=newQuestId;drawerId=newDrawerId;contents=newContents??Array.Empty<GameObject>();drawer=GetComponent<ExperimentalDrawerController>();if(drawer)drawer.MotionCompleted+=OnDrawerMotionCompleted;
            foreach(var content in contents)if(content)content.SetActive(false);
            DreamCodeVR2ClientLogger.Event("quest","QUEST_DRAWER_CONTENTS_HIDDEN",null,new { quest_instance_id=questId,drawer_id=drawerId,object_ids=ContentIds() });
        }
        public void ClearConfiguration(){if(drawer)drawer.MotionCompleted-=OnDrawerMotionCompleted;foreach(var content in contents)if(content)content.SetActive(true);contents=Array.Empty<GameObject>();drawer=null;questId=null;drawerId=null;}
        private void OnDrawerMotionCompleted(bool open){if(!open)return;foreach(var content in contents)if(content)content.SetActive(true);FindFirstObjectByType<SceneContextTransmitter>()?.SendSceneContextSnapshot("drawer contents revealed");DreamCodeVR2ClientLogger.Event("quest","QUEST_OBJECT_REVEALED",null,new { quest_instance_id=questId,drawer_id=drawerId,object_ids=ContentIds(),reason="drawer_opened" });}
        private string[] ContentIds(){var ids=new System.Collections.Generic.List<string>();foreach(var content in contents){var id=content?content.GetComponent<AIEditableObject>()?.objectId:null;if(!string.IsNullOrWhiteSpace(id))ids.Add(id);}return ids.ToArray();}
        private void OnDestroy(){if(drawer)drawer.MotionCompleted-=OnDrawerMotionCompleted;}
    }

    public class QuestDoorController : MonoBehaviour
    {
        public Transform closedAnchor; public Transform openAnchor; public Transform movingDoor; public QuestLockController lockController;
        public bool IsOpen { get; private set; } public bool IsLocked => lockController && lockController.IsLocked;
        public SceneContextTransmitter sceneContext; public QuestEventBus eventBus;
        public bool TryOpen(out string error)
        {
            DreamCodeVR2ClientLogger.Event("quest","DOOR_OPEN_ATTEMPT",null,new { door_id=Id() });
            if(IsLocked){error="The door is locked.";DreamCodeVR2ClientLogger.Event("quest","DOOR_LOCKED_REJECTION",error,new { door_id=Id() });return false;}
            if(!Valid(out error))return false;MoveLeaf(openAnchor);IsOpen=true;Publish("open");DreamCodeVR2ClientLogger.Event("quest","DOOR_OPENED",null,new { door_id=Id(),moving_door_name=MotionTarget().name });return true;
        }
        public bool TryClose(out string error){if(!Valid(out error))return false;MoveLeaf(closedAnchor);IsOpen=false;Publish("closed");return true;}
        private Transform MotionTarget()=>movingDoor?movingDoor:transform;
        private void MoveLeaf(Transform pose){MotionTarget().rotation=pose.rotation;}
        private bool Valid(out string error){if(!closedAnchor||!openAnchor||Quaternion.Angle(closedAnchor.rotation,openAnchor.rotation)<.1f){error="DoorOpenAnchor must have a different rotation from DoorClosedAnchor.";return false;}error=null;return true;}
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
        private void Publish(string state){var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state=IsOpen?"open":"closed";eventBus?.Publish(QuestEventType.ObjectStateChanged,Id(),null,semantic.state);sceneContext?.SendSceneContextSnapshot("door "+state);}
    }

    public class QuestPaintingController : MonoBehaviour
    {
        public Transform crookedAnchor; public Transform alignedAnchor; public GameObject clueToReveal;
        public float alignmentPositionTolerance=.06f; public float alignmentRotationTolerance=8f;
        public bool IsAligned { get; private set; } public SceneContextTransmitter sceneContext; public QuestEventBus eventBus;
        public void SetPhysicalManipulation(bool enabled){var adapter=GetComponent<ExperimentalGrabbableAdapter>();if(enabled){var body=GetComponent<Rigidbody>()??gameObject.AddComponent<Rigidbody>();body.isKinematic=false;adapter=adapter??gameObject.AddComponent<ExperimentalGrabbableAdapter>();adapter.SetGrabbable(true);DreamCodeVR2ClientLogger.Event("quest","PAINTING_PHYSICAL_GRAB_ENABLED",null,new { object_id=Id() });}else if(adapter)adapter.SetGrabbable(false);}
        private void Update(){if(IsAligned||!alignedAnchor)return;var manager=FindFirstObjectByType<ExperimentConditionManager>();if(manager==null||manager.condition==ExperimentCondition.VoiceCommandBaseline)return;if(Vector3.Distance(transform.position,alignedAnchor.position)<=alignmentPositionTolerance&&Quaternion.Angle(transform.rotation,alignedAnchor.rotation)<=alignmentRotationTolerance)CompleteAlignment();}
        public bool TryAlign(out string error)
        {
            if(!crookedAnchor||!alignedAnchor||Quaternion.Angle(crookedAnchor.rotation,alignedAnchor.rotation)<.1f){error="PaintingAlignedAnchor requires manual Scene View rotation.";return false;}
            transform.SetPositionAndRotation(alignedAnchor.position,alignedAnchor.rotation);CompleteAlignment();error=null;return true;
        }
        private void CompleteAlignment(){if(IsAligned)return;IsAligned=true;if(clueToReveal)clueToReveal.SetActive(true);var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state="aligned";eventBus?.Publish(QuestEventType.ObjectStateChanged,Id(),null,"aligned");sceneContext?.SendSceneContextSnapshot("painting aligned");DreamCodeVR2ClientLogger.Event("quest","PAINTING_ALIGNED",null,new { object_id=Id() });}
        public void ResetCrooked(){if(crookedAnchor)transform.SetPositionAndRotation(crookedAnchor.position,crookedAnchor.rotation);IsAligned=false;if(clueToReveal)clueToReveal.SetActive(false);var semantic=GetComponent<AuthoringSemanticState>();if(semantic)semantic.state="crooked";}
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
    }

    public class QuestLampController : MonoBehaviour
    {
        public bool IsActive { get; private set; } public SceneContextTransmitter sceneContext; public QuestEventBus eventBus;
        public void SetLampState(bool active){IsActive=active;var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state=active?"active":"inactive";eventBus?.Publish(QuestEventType.ObjectStateChanged,Id(),null,semantic.state);sceneContext?.SendSceneContextSnapshot("lamp state");DreamCodeVR2ClientLogger.Event("quest","LAMP_STATE_CHANGED",null,new { object_id=Id(), active });}
        public void Toggle(){SetLampState(!IsActive);}
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
    }

    // Shared typed puzzle interactions used by C2/C3 authoring transport, distinct from world authoring.
    public class QuestOperationalInteractionExecutor : MonoBehaviour
    {
        public AuthoringExecutionResult Execute(AuthoringAction action)
        {
            var target=AuthoringActionExecutor.FindEditable(action?.targetObjectId);if(!target)return Fail(action,"missing_target","Operational target is unavailable.");var op=(action.operation??string.Empty).ToLowerInvariant();string error;
            if(op=="activate"||op=="deactivate"||op=="toggle"){var lamp=target.GetComponent<QuestLampController>();if(!lamp)return Fail(action,"missing_lamp","Target is not a lamp.");if(op=="activate")lamp.SetLampState(true);else if(op=="deactivate")lamp.SetLampState(false);else lamp.Toggle();DreamCodeVR2ClientLogger.Event("quest","LAMP_INTERACTION_APPLIED",null,new { action_id=action.actionId,object_id=target.objectId,operation=op });return Ok(action,"Lamp interaction applied.");}
            if(op=="open"||op=="close"){var open=op=="open";var drawer=target.GetComponent<ExperimentalDrawerController>();if(drawer){if(open&&!QuestLockController.CanOpenTarget(target.objectId,out error))return Fail(action,"target_locked",error);if(!(open?drawer.TryOpen(out error):drawer.TryClose(out error)))return Fail(action,"motion_failed",error);DreamCodeVR2ClientLogger.Event("quest","DRAWER_INTERACTION",null,new { action_id=action.actionId,object_id=target.objectId,operation=op });return Ok(action,"Drawer interaction applied.");}var door=target.GetComponent<QuestDoorController>();if(door){if(!(open?door.TryOpen(out error):door.TryClose(out error)))return Fail(action,"door_failed",error);DreamCodeVR2ClientLogger.Event("quest","DOOR_INTERACTION",null,new { action_id=action.actionId,object_id=target.objectId,operation=op });return Ok(action,"Door interaction applied.");}return Fail(action,"missing_openable","Target is not operationally openable.");}
            if(op=="use_with"){var lockTarget=AuthoringActionExecutor.FindEditable(action.secondaryObjectId)?.GetComponent<QuestLockController>();if(!lockTarget)return Fail(action,"missing_lock","Lock is unavailable.");if(!lockTarget.TryUseKey(target.objectId,out error))return Fail(action,"key_lock_failed",error);DreamCodeVR2ClientLogger.Event("quest","KEY_LOCK_INTERACTION",null,new { action_id=action.actionId,key_id=target.objectId,lock_id=action.secondaryObjectId });return Ok(action,"Key-lock interaction applied.");}
            return Fail(action,"unsupported_interaction","Operational interaction is unsupported.");
        }
        private static AuthoringExecutionResult Ok(AuthoringAction action,string message)=>new AuthoringExecutionResult{actionId=action.actionId,success=true,message=message};
        private static AuthoringExecutionResult Fail(AuthoringAction action,string code,string message){DreamCodeVR2ClientLogger.Warn("quest","LAMP_INTERACTION_FAILED",message,new { action_id=action?.actionId,code });return new AuthoringExecutionResult{actionId=action?.actionId,success=false,message=message,error=new AuthoringValidationError{code=code,message=message}};}
    }

    public class QuestNoteController : MonoBehaviour
    {
        [SerializeField] private TMP_Text renderedText;
        private string defaultText; private Transform defaultParent; private Vector3 defaultPosition; private Quaternion defaultRotation; private Vector3 defaultScale; private bool defaultTransformCaptured; public string QuestText { get; private set; }
        private void Awake(){ResolveRenderer();CaptureDefaultText();CaptureDefaultTransform();}
        public void Configure(string text, bool visible)
        {
            ResolveRenderer();CaptureDefaultText();QuestText=string.IsNullOrWhiteSpace(text)?defaultText:text;
            if(renderedText)renderedText.text=QuestText;
            DreamCodeVR2ClientLogger.Event("quest",string.IsNullOrWhiteSpace(text)?"QUEST_CLUE_TEXT_FALLBACK":"QUEST_CLUE_TEXT_APPLIED",null,new { clue_id=GetComponent<AIEditableObject>()?.objectId,has_renderer=renderedText!=null });
            gameObject.SetActive(visible);
        }
        public void ResetToDefault(bool visible){ResolveRenderer();CaptureDefaultText();CaptureDefaultTransform();transform.SetParent(defaultParent,true);transform.SetPositionAndRotation(defaultPosition,defaultRotation);transform.localScale=defaultScale;QuestText=defaultText;if(renderedText)renderedText.text=defaultText;gameObject.SetActive(visible);}
        private void ResolveRenderer(){if(!renderedText)renderedText=GetComponentInChildren<TMP_Text>(true);}
        private void CaptureDefaultText(){if(defaultText==null&&renderedText)defaultText=renderedText.text??string.Empty;}
        private void CaptureDefaultTransform(){if(defaultTransformCaptured)return;defaultTransformCaptured=true;defaultParent=transform.parent;defaultPosition=transform.position;defaultRotation=transform.rotation;defaultScale=transform.localScale;}
    }

    // A placement is accepted only against this exact configured anchor ID; callers may use
    // NotifyPlaced after a deterministic relocation, while physical trigger integration can call it too.
    public class QuestPlacementMonitor : MonoBehaviour
    {
        public AuthoringAnchor anchor;
        public QuestEventBus eventBus;
        public SceneContextTransmitter sceneContext;
        public bool NotifyPlaced(AIEditableObject item)
        {
            if(!anchor||!item)return false; item.transform.SetParent(anchor.transform,true);anchor.SetOccupied(true);eventBus?.Publish(QuestEventType.ObjectPlacedInZone,item.objectId,anchor.anchorId);sceneContext?.SendSceneContextSnapshot("object placed at anchor");DreamCodeVR2ClientLogger.Event("quest","OBJECT_PLACED_AT_ANCHOR",null,new { object_id=item.objectId, anchor_id=anchor.anchorId });return true;
        }
        private void OnTriggerEnter(Collider other)
        {
            var item=other ? other.GetComponentInParent<AIEditableObject>() : null;
            if(item) NotifyPlaced(item);
        }
    }
}
