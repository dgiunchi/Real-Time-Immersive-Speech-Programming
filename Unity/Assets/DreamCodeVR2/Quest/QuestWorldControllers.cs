using System;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using DreamCodeVR2.SceneContext;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    [Serializable] public class QuestLockBinding { public string lockId; public string requiredKeyId; public string targetObjectId; }
    [Serializable] public class QuestNoteBinding { public string noteId; public string text; public bool visible; public string anchorId; }
    [Serializable] public class QuestInstance { public string questId; public QuestLockBinding[] lockBindings; public QuestNoteBinding[] notes; public string selectedLampId; public QuestPlan plan; public bool requiresC1Sphere; public string c1SphereId="sphere_001"; public string c1SphereStartAnchorId; public string c1SpherePlacementAnchorId; }

    // Deliberately protocol-agnostic: the experiment/session layer may configure this component
    // without exposing bindings through participant-facing labels.
    public class QuestInstanceController : MonoBehaviour
    {
        public QuestInstance ActiveInstance { get; private set; }
        public QuestRuntimeState runtimeState;
        public void Apply(QuestInstance instance)
        {
            if(instance==null)return;ActiveInstance=instance;if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();
            ClearC1QuestSphere();
            foreach(var binding in instance.lockBindings??Array.Empty<QuestLockBinding>()){var item=AuthoringActionExecutor.FindEditable(binding.lockId);item?.GetComponent<QuestLockController>()?.Configure(binding.requiredKeyId,binding.targetObjectId);}
            foreach(var note in instance.notes??Array.Empty<QuestNoteBinding>()){var item=AuthoringActionExecutor.FindEditable(note.noteId);item?.GetComponent<QuestNoteController>()?.Configure(note.text,note.visible);}
            if(!string.IsNullOrWhiteSpace(instance.selectedLampId))foreach(var lamp in FindObjectsByType<QuestLampController>(FindObjectsInactive.Include,FindObjectsSortMode.None))lamp.SetLampState(lamp.GetComponent<AIEditableObject>()?.objectId==instance.selectedLampId);
            if(instance.plan!=null)runtimeState?.StartQuest(instance.plan);DreamCodeVR2ClientLogger.Event("quest","QUEST_INSTANCE_APPLIED",null,new { quest_id=instance.questId });
            var manager=FindFirstObjectByType<ExperimentConditionManager>();
            if(manager&&manager.condition==ExperimentCondition.VoiceCommandBaseline)
            {
                foreach(var key in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(key&&key.objectId!=null&&key.objectId.IndexOf("key",StringComparison.OrdinalIgnoreCase)>=0){var grab=key.GetComponent<ExperimentalGrabbableAdapter>();if(grab){grab.SetGrabbable(false);DreamCodeVR2ClientLogger.Event("quest","C1_GRABBABLE_BLOCKED",null,new { object_id=key.objectId });}}
                if(instance.requiresC1Sphere) CreateC1QuestSphere(instance);
            }
        }
        public bool AllowsC1PlaceIn(string objectId,string receptacleId,out AuthoringAnchor anchor)
        {
            anchor=null;var instance=ActiveInstance;if(instance==null||!instance.requiresC1Sphere||string.IsNullOrWhiteSpace(instance.c1SpherePlacementAnchorId)||!string.Equals(instance.c1SphereId,objectId,StringComparison.Ordinal)||!string.Equals(receptacleId,"basket_001",StringComparison.Ordinal))return false;
            foreach(var candidate in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(candidate&&candidate.anchorId==instance.c1SpherePlacementAnchorId){anchor=candidate;return true;}return false;
        }
        public void ClearC1QuestSphere(){foreach(var item in FindObjectsByType<C1QuestSphereController>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(item)Destroy(item.gameObject);}
        private void CreateC1QuestSphere(QuestInstance instance)
        {
            if(string.IsNullOrWhiteSpace(instance.c1SphereStartAnchorId)){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest instance does not define a start anchor.",new { quest_id=instance.questId });return;}
            AuthoringAnchor anchor=null;foreach(var candidate in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(candidate&&candidate.anchorId==instance.c1SphereStartAnchorId){anchor=candidate;break;}
            if(!anchor){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest sphere start anchor is unavailable.",new { anchor_id=instance.c1SphereStartAnchorId });return;}
            var id=string.IsNullOrWhiteSpace(instance.c1SphereId)?"sphere_001":instance.c1SphereId;if(AuthoringActionExecutor.FindEditable(id)){DreamCodeVR2ClientLogger.Warn("quest","C1_QUEST_SPHERE_CREATE_FAILED","Quest sphere ID is already in use.",new { object_id=id });return;}
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=id;sphere.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);sphere.transform.SetParent(anchor.transform,true);
            var editable=sphere.AddComponent<AIEditableObject>();editable.objectId=id;editable.displayName="Quest Sphere";editable.labels=new[]{"quest_sphere","sphere","primitive"};editable.editable=false;
            var body=sphere.AddComponent<Rigidbody>();body.isKinematic=true;var grab=sphere.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(false);DreamCodeVR2ClientLogger.Event("quest","C1_GRABBABLE_BLOCKED",null,new { object_id=id });
            var voice=sphere.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"MOVE_TO_PRESET","PLACE_IN"};voice.predefinedPresets=new[]{"soccer_ball"};
            var marker=sphere.AddComponent<C1QuestSphereController>();marker.instanceController=this;marker.placementAnchorId=instance.c1SpherePlacementAnchorId;anchor.SetOccupied(true);
            DreamCodeVR2ClientLogger.Event("quest","C1_QUEST_SPHERE_CREATED",null,new { object_id=id,anchor_id=anchor.anchorId });
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
            SoccerBallAppearanceApplied=true;var editable=GetComponent<AIEditableObject>();if(editable&&editable.labels!=null&&Array.IndexOf(editable.labels,"soccer_ball")<0){var labels=new string[editable.labels.Length+1];Array.Copy(editable.labels,labels,editable.labels.Length);labels[labels.Length-1]="soccer_ball";editable.labels=labels;}
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
        public void Configure(string keyId, string targetId, bool locked = true) { requiredKeyId=keyId; associatedTargetObjectId=targetId; IsLocked=locked; Publish("configured"); }
        public bool TryUseKey(string keyId, out string error)
        {
            DreamCodeVR2ClientLogger.Event("quest", "LOCK_USE_ATTEMPT", null, new { lock_id=Id(), key_id=keyId });
            if (!IsLocked) { error="The lock is already unlocked."; return false; }
            if (string.IsNullOrWhiteSpace(requiredKeyId) || !string.Equals(requiredKeyId,keyId,StringComparison.Ordinal)) { error="That key does not fit this lock."; DreamCodeVR2ClientLogger.Event("quest", "LOCK_WRONG_KEY", error, new { lock_id=Id(), key_id=keyId }); return false; }
            IsLocked=false; error=null; DreamCodeVR2ClientLogger.Event("quest", "LOCK_UNLOCKED", null, new { lock_id=Id(), key_id=keyId, target_id=associatedTargetObjectId }); eventBus?.Publish(QuestEventType.LockOpened,Id(),keyId); Publish("unlocked"); return true;
        }
        public void ResetLocked() { IsLocked=true; Publish("locked"); }
        public static QuestLockController FindForTarget(string targetObjectId)
        {
            if (string.IsNullOrWhiteSpace(targetObjectId)) return null;
            foreach (var candidate in FindObjectsByType<QuestLockController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate && string.Equals(candidate.associatedTargetObjectId, targetObjectId, StringComparison.Ordinal)) return candidate;
            return null;
        }
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
        private void Publish(string state){var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state=IsLocked?"locked":"unlocked";sceneContext?.SendSceneContextSnapshot("lock "+state);}
    }

    public class QuestDoorController : MonoBehaviour
    {
        public Transform closedAnchor; public Transform openAnchor; public QuestLockController lockController;
        public bool IsOpen { get; private set; } public bool IsLocked => lockController && lockController.IsLocked;
        public SceneContextTransmitter sceneContext; public QuestEventBus eventBus;
        public bool TryOpen(out string error)
        {
            DreamCodeVR2ClientLogger.Event("quest","DOOR_OPEN_ATTEMPT",null,new { door_id=Id() });
            if(IsLocked){error="The door is locked.";DreamCodeVR2ClientLogger.Event("quest","DOOR_LOCKED_REJECTION",error,new { door_id=Id() });return false;}
            if(!Valid(out error))return false; transform.SetPositionAndRotation(openAnchor.position,openAnchor.rotation);IsOpen=true;Publish("open");DreamCodeVR2ClientLogger.Event("quest","DOOR_OPENED",null,new { door_id=Id() });return true;
        }
        public bool TryClose(out string error){if(!Valid(out error))return false;transform.SetPositionAndRotation(closedAnchor.position,closedAnchor.rotation);IsOpen=false;Publish("closed");return true;}
        private bool Valid(out string error){if(!closedAnchor||!openAnchor||Vector3.Distance(closedAnchor.position,openAnchor.position)<.001f){error="Door OpenAnchor must be positioned away from DoorClosedAnchor.";return false;}error=null;return true;}
        private string Id()=>GetComponent<AIEditableObject>()?.objectId??gameObject.name;
        private void Publish(string state){var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state=IsOpen?"open":"closed";eventBus?.Publish(QuestEventType.ObjectStateChanged,Id(),null,semantic.state);sceneContext?.SendSceneContextSnapshot("door "+state);}
    }

    public class QuestPaintingController : MonoBehaviour
    {
        public Transform crookedAnchor; public Transform alignedAnchor; public GameObject clueToReveal;
        public bool IsAligned { get; private set; } public SceneContextTransmitter sceneContext; public QuestEventBus eventBus;
        public bool TryAlign(out string error)
        {
            if(!crookedAnchor||!alignedAnchor||Quaternion.Angle(crookedAnchor.rotation,alignedAnchor.rotation)<.1f){error="PaintingAlignedAnchor requires manual Scene View rotation.";return false;}
            transform.SetPositionAndRotation(alignedAnchor.position,alignedAnchor.rotation);IsAligned=true;if(clueToReveal)clueToReveal.SetActive(true);var semantic=GetComponent<AuthoringSemanticState>()??gameObject.AddComponent<AuthoringSemanticState>();semantic.state="aligned";eventBus?.Publish(QuestEventType.ObjectStateChanged,Id(),null,"aligned");sceneContext?.SendSceneContextSnapshot("painting aligned");DreamCodeVR2ClientLogger.Event("quest","PAINTING_ALIGNED",null,new { object_id=Id() });error=null;return true;
        }
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

    public class QuestNoteController : MonoBehaviour
    {
        public string QuestText { get; private set; }
        public void Configure(string text, bool visible){QuestText=text;gameObject.SetActive(visible);}
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
