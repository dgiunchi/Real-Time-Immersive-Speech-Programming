using System;
using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.Quest;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringActionExecutor : MonoBehaviour
    {
        public StudyConfiguration studyConfiguration; public AuthoringUndoManager undoManager; public SceneContextTransmitter sceneContextTransmitter;
        public event Action<AuthoringExecutionResult> ActionFinished; public bool IsExecuting { get; private set; }
        private readonly HashSet<string> processedActionIds = new HashSet<string>();
        public void ClearProcessedActions() => processedActionIds.Clear();
        public AuthoringExecutionResult Execute(AuthoringAction action)
        {
            if (!undoManager) undoManager = FindFirstObjectByType<AuthoringUndoManager>();
            if (!undoManager) return Fail(action, "undo_unavailable", "The reversible action runtime is not configured.");
            if (IsExecuting) return Fail(action, "action_in_progress", "Another action is already being applied.");
            if (action == null || string.IsNullOrWhiteSpace(action.actionId)) return Fail(action, "malformed_action", "Action ID is required.");
            if (processedActionIds.Contains(action.actionId)) return Fail(action, "duplicate_action", "This action was already processed.");
            IsExecuting = true;
            try { var result = ValidateAndApply(action); if (result.success) { processedActionIds.Add(action.actionId); if(action.kind==AuthoringActionKind.SET_PROPERTY){var target=FindEditable(action.targetObjectId);if(target)(target.GetComponent<AuthoringPropertyMarker>()??target.gameObject.AddComponent<AuthoringPropertyMarker>()).Mark(action.operation);} } ActionFinished?.Invoke(result); sceneContextTransmitter?.SendSceneContextSnapshot(result.success ? "authoring action" : "authoring failure"); return result; }
            finally { IsExecuting = false; }
        }
        private AuthoringExecutionResult ValidateAndApply(AuthoringAction action)
        {
            if (studyConfiguration && studyConfiguration.directTaskCompletionForbidden && action.allowFinalGoalBypass) return Fail(action,"goal_bypass","Direct final-goal completion is forbidden.");
            if (ViolatesActiveTaskProtection(action, out var taskFailure)) return Fail(action, "quest_integrity", taskFailure);
            if (action.kind == AuthoringActionKind.CREATE_OBJECT) return CreateObject(action);
            var target = FindEditable(action.targetObjectId); if (!target) return Fail(action,"missing_target","The requested object no longer exists.");
            var caps = target.GetComponent<AuthoringCapabilities>(); if (!caps || !target.editable) return Fail(action,"not_editable","This object cannot be changed.");
            if (caps.questCritical && (action.operation == "active" || action.operation == "visible") && IsFalse(action.value)) return Fail(action,"quest_critical","A required quest object cannot be removed.");
            if (action.kind == AuthoringActionKind.SET_PROPERTY) return SetProperty(action,target,caps);
            if (action.kind == AuthoringActionKind.SET_AFFORDANCE) return SetAffordance(action,target,caps);
            if (action.kind == AuthoringActionKind.ADD_BEHAVIOR) return AddBehavior(action,target,caps);
            if (action.kind == AuthoringActionKind.RELOCATE_OBJECT) return Relocate(action,target,caps);
            if (action.kind == AuthoringActionKind.TOGGLE_STATE) return ToggleState(action,target,caps);
            if (action.kind == AuthoringActionKind.LINK_OBJECTS) return Link(action,target,caps);
            return Fail(action,"unsupported_action","This action type is not allowed.");
        }
        private AuthoringExecutionResult SetAffordance(AuthoringAction a, AIEditableObject target, AuthoringCapabilities caps)
        {
            if (!caps.AllowsOperation("SET_AFFORDANCE")) return Fail(a,"capability_rejected","This affordance cannot be changed.");
            foreach (var forbidden in caps.forbiddenAffordanceChanges ?? Array.Empty<string>()) if (string.Equals(forbidden,a.operation,StringComparison.OrdinalIgnoreCase)) return Fail(a,"capability_rejected","This affordance is protected.");
            var enabled=!IsFalse(a.value);
            if(a.operation=="gravity_enabled"||a.operation=="kinematic") return SetProperty(new AuthoringAction{actionId=a.actionId,kind=AuthoringActionKind.SET_PROPERTY,targetObjectId=a.targetObjectId,operation=a.operation,value=a.value},target,caps);
            if(a.operation=="collision_enabled"){var colliders=target.GetComponentsInChildren<Collider>(true);var old=new bool[colliders.Length];for(var i=0;i<colliders.Length;i++){old[i]=colliders[i].enabled;colliders[i].enabled=enabled;}undoManager.Push(a.actionId,()=>{for(var i=0;i<colliders.Length;i++)if(colliders[i])colliders[i].enabled=old[i];});return Ok(a,"Collision affordance updated.");}
            if(a.operation=="grabbable"||a.operation=="movable"||a.operation=="interactable"){var affordance=target.GetComponent<AuthoringAffordanceState>()??target.gameObject.AddComponent<AuthoringAffordanceState>();var old=affordance.Get(a.operation);if(a.operation=="grabbable"){var adapter=target.GetComponent<ExperimentalGrabbableAdapter>();if(!adapter)return Fail(a,"missing_grab_adapter","This object has no approved grasp adapter.");adapter.SetGrabbable(enabled);}affordance.Set(a.operation,enabled);undoManager.Push(a.actionId,()=>{if(affordance){affordance.Set(a.operation,old);var adapter=target.GetComponent<ExperimentalGrabbableAdapter>();if(adapter)adapter.SetGrabbable(old);}});return Ok(a,"Affordance updated.");}
            return Fail(a,"unsupported_affordance","Affordance is not allowlisted.");
        }
        private bool ViolatesActiveTaskProtection(AuthoringAction action,out string message)
        {
            message=null; var runtime=FindFirstObjectByType<QuestRuntimeState>(); var task=runtime?.GetCurrentTask(); if(task==null)return false;
            var target=action.targetObjectId; foreach(var protectedId in task.protectedDuringTask??Array.Empty<string>()) if(protectedId==target)
            {
                if((action.kind==AuthoringActionKind.SET_PROPERTY&&(action.operation=="active"||action.operation=="visible")&&IsFalse(action.value))||action.kind==AuthoringActionKind.TOGGLE_STATE){message="This change would bypass the current task.";return true;}
                foreach(var property in task.protectedProperties??Array.Empty<string>()) if(property==action.operation){message="This task property is protected.";return true;}
                foreach(var affordance in task.forbiddenAffordanceChanges??Array.Empty<string>()) if(affordance==action.operation){message="This task affordance is protected.";return true;}
            }
            if(task.directCompletionForbidden && action.allowFinalGoalBypass){message="Direct task completion is forbidden.";return true;} return false;
        }
        private AuthoringExecutionResult SetProperty(AuthoringAction a, AIEditableObject target, AuthoringCapabilities caps)
        {
            if (!caps.AllowsOperation("SET_PROPERTY") || !caps.AllowsProperty(a.operation)) return Fail(a,"capability_rejected","That property is not editable for this object.");
            if (a.operation == "color") { if (!ColorUtility.TryParseHtmlString(a.value, out var color)) return Fail(a,"invalid_value","Color must be a hex color."); var rs=target.GetComponentsInChildren<Renderer>(true); var old=new Color[rs.Length]; for(int i=0;i<rs.Length;i++){ old[i]=ReadColor(rs[i].material); SetColor(rs[i].material,color); } undoManager.Push(a.actionId,()=>{for(int i=0;i<rs.Length;i++) if(rs[i]) SetColor(rs[i].material,old[i]);}); return Ok(a,"Color applied."); }
            if (a.operation == "visible") { if(!caps.canHide) return Fail(a,"capability_rejected","This object cannot be hidden."); var old=target.gameObject.activeSelf; target.gameObject.SetActive(!IsFalse(a.value)); undoManager.Push(a.actionId,()=>target.gameObject.SetActive(old)); return Ok(a,"Visibility updated."); }
            if (a.operation == "active") { if(!caps.canDeactivate) return Fail(a,"capability_rejected","This object cannot be deactivated."); var old=target.gameObject.activeSelf; target.gameObject.SetActive(!IsFalse(a.value)); undoManager.Push(a.actionId,()=>target.gameObject.SetActive(old)); return Ok(a,"Active state updated."); }
            if (a.operation == "kinematic" || a.operation == "gravity_enabled") { var body=target.GetComponent<Rigidbody>(); if(!body)return Fail(a,"missing_rigidbody","The object has no Rigidbody."); var old=a.operation=="kinematic"?body.isKinematic:body.useGravity; if(a.operation=="kinematic") body.isKinematic=!IsFalse(a.value); else body.useGravity=!IsFalse(a.value); undoManager.Push(a.actionId,()=>{if(body){if(a.operation=="kinematic")body.isKinematic=old;else body.useGravity=old;}}); return Ok(a,"Physics property updated."); }
            if (a.operation == "scale") { var scale=Mathf.Clamp(a.numericValue,caps.minimumScale,caps.maximumScale); if(Mathf.Abs(scale-a.numericValue)>.001f)return Fail(a,"out_of_bounds","Scale is outside the allowed range."); var old=target.transform.localScale; target.transform.localScale=Vector3.one*scale; undoManager.Push(a.actionId,()=>{if(target)target.transform.localScale=old;}); return Ok(a,"Scale updated."); }
            return Fail(a,"unsupported_property","Property is not allowlisted.");
        }
        private AuthoringExecutionResult AddBehavior(AuthoringAction a, AIEditableObject t, AuthoringCapabilities c)
        { if(!c.AllowsOperation("ADD_BEHAVIOR")||!c.AllowsBehavior(a.operation))return Fail(a,"capability_rejected","That behavior is not allowed."); AuthoringRuntimeBehavior b=null; if(a.operation=="rotate_continuously"){b=t.gameObject.AddComponent<AuthoringRotateBehavior>();((AuthoringRotateBehavior)b).degreesPerSecond=Mathf.Clamp(a.numericValue==0?45f:a.numericValue,-180f,180f);} else if(a.operation=="blink"){b=t.gameObject.AddComponent<AuthoringBlinkBehavior>();((AuthoringBlinkBehavior)b).interval=Mathf.Clamp(a.numericValue==0?.5f:a.numericValue,.1f,5f);} if(!b)return Fail(a,"unsupported_behavior","Behavior is not allowlisted."); b.Configure(a);FindFirstObjectByType<QuestEventBus>()?.Publish(QuestEventType.BehaviorAdded,t.objectId,null,a.operation); undoManager.Push(a.actionId,()=>{if(b)Destroy(b);}); return Ok(a,"Behavior added."); }
        private AuthoringExecutionResult Relocate(AuthoringAction a, AIEditableObject t, AuthoringCapabilities c)
        { if(!c.canMove||!c.AllowsOperation("RELOCATE_OBJECT"))return Fail(a,"capability_rejected","This object cannot be moved."); var anchor=FindAnchor(a.anchorId); if(!anchor||!anchor.AllowsRelocation(ObjectType(t)))return Fail(a,"invalid_anchor","The named anchor is not valid for this object."); if(anchor.questRestricted)return Fail(a,"quest_restricted_anchor","This anchor is reserved for the quest."); if(anchor.occupancyPolicy==AnchorOccupancyPolicy.Single&&anchor.IsOccupied)return Fail(a,"anchor_occupied","The anchor is occupied."); var parent=t.transform.parent;var pos=t.transform.position;var rot=t.transform.rotation; t.transform.SetParent(anchor.transform);t.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);anchor.SetOccupied(true);anchor.GetComponent<QuestPlacementMonitor>()?.NotifyPlaced(t);undoManager.Push(a.actionId,()=>{if(t){t.transform.SetParent(parent);t.transform.SetPositionAndRotation(pos,rot);}anchor.SetOccupied(false);});return Ok(a,"Object moved to approved anchor."); }
        private AuthoringExecutionResult ToggleState(AuthoringAction a, AIEditableObject t, AuthoringCapabilities c) { if(!c.AllowsOperation("TOGGLE_STATE"))return Fail(a,"capability_rejected","State cannot be toggled."); var state=t.GetComponent<AuthoringSemanticState>()??t.gameObject.AddComponent<AuthoringSemanticState>();var old=state.state;state.state=a.value;undoManager.Push(a.actionId,()=>{if(state)state.state=old;});return Ok(a,"Semantic state updated."); }
        private AuthoringExecutionResult Link(AuthoringAction a, AIEditableObject t, AuthoringCapabilities c) { if(!c.canLink||!c.AllowsOperation("LINK_OBJECTS"))return Fail(a,"capability_rejected","This object cannot participate in links."); if(a.operation!="activate")return Fail(a,"unsupported_link","Only activation links are available."); var other=FindEditable(a.secondaryObjectId);if(!other)return Fail(a,"missing_secondary","The linked object does not exist.");var link=t.gameObject.AddComponent<AuthoringObjectLink>();link.linkId=a.actionId;link.sourceObjectId=t.objectId;link.targetObjectId=other.objectId;link.linkOperation=a.operation;link.propertyValue=a.value;FindFirstObjectByType<QuestEventBus>()?.Publish(QuestEventType.LinkActivated,t.objectId,other.objectId);undoManager.Push(a.actionId,()=>{if(link)Destroy(link);});return Ok(a,"Objects linked."); }
        private AuthoringExecutionResult CreateObject(AuthoringAction a) { var anchor=FindAnchor(a.anchorId);if(!anchor||!anchor.AllowsSpawn(a.operation)||anchor.questRestricted||(anchor.occupancyPolicy==AnchorOccupancyPolicy.Single&&anchor.IsOccupied))return Fail(a,"invalid_anchor","Object must use an approved, available anchor."); if(a.operation!="cube"&&a.operation!="sphere"&&a.operation!="bridge_segment"&&a.operation!="platform")return Fail(a,"unsupported_object","Object type is not allowlisted.");var o=GameObject.CreatePrimitive(a.operation=="sphere"?PrimitiveType.Sphere:PrimitiveType.Cube);var runtimeId=!string.IsNullOrWhiteSpace(a.targetObjectId)?a.targetObjectId:"runtime_"+a.actionId;if(FindEditable(runtimeId)){Destroy(o);return Fail(a,"duplicate_object_id","The requested runtime object ID already exists.");}o.name=runtimeId;o.transform.SetPositionAndRotation(anchor.transform.position,anchor.transform.rotation);o.transform.SetParent(anchor.transform);var edit=o.AddComponent<AIEditableObject>();edit.objectId=runtimeId;edit.displayName=a.operation;edit.labels=new[]{"runtime_created",a.operation};var meta=o.AddComponent<RuntimeAuthoringMetadata>();meta.createdByActionId=a.actionId;meta.createdDuringTaskId=FindFirstObjectByType<QuestRuntimeState>()?.GetCurrentTask()?.step.ToString();var caps=o.AddComponent<AuthoringCapabilities>();caps.questCritical=false;if(a.operation=="sphere"){caps.allowedOperations=new[]{"SET_PROPERTY","SET_AFFORDANCE","RELOCATE_OBJECT","TOGGLE_STATE"};caps.editableProperties=new[]{"color","scale","kinematic","gravity_enabled"};var body=o.AddComponent<Rigidbody>();body.useGravity=true;var grab=o.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(false);var requestedMaterial=a.parameters?["material"]?.ToString()??a.value;if(requestedMaterial=="soccer_ball_material"){QuestSoccerBall.SetWorldDiameter(o.transform);var soccer=Resources.Load<Material>("SoccerBall");if(!soccer){Destroy(o);return Fail(a,"missing_soccer_material","Resources/SoccerBall is unavailable.");}o.GetComponent<Renderer>().material=soccer;edit.labels=new[]{"runtime_created","sphere","soccer_ball"};var state=o.AddComponent<AuthoringSemanticState>();state.state="soccer_ball";DreamCodeVR2ClientLogger.Event("quest","SOCCER_BALL_MATERIAL_APPLIED",null,new { object_id=runtimeId,diameter_m=QuestSoccerBall.CanonicalDiameterMeters });}DreamCodeVR2ClientLogger.Event("quest","SOCCER_BALL_CREATED",null,new { object_id=runtimeId });}anchor.SetOccupied(true);FindFirstObjectByType<QuestRuntimeState>()?.OnObjectCreated(edit.objectId);undoManager.Push(a.actionId,()=>{if(o)Destroy(o);anchor.SetOccupied(false);});return Ok(a,"Object created at approved anchor."); }
        public static AIEditableObject FindEditable(string id) { foreach(var item in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(item&&(item.objectId==id||item.gameObject.name==id))return item;return null; }
        private static AuthoringAnchor FindAnchor(string id) { foreach(var item in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(item&&item.anchorId==id)return item;return null; }
        private static bool IsFalse(string v)=>string.Equals(v,"false",StringComparison.OrdinalIgnoreCase)||v=="0";
        private static string ObjectType(AIEditableObject o)=>o.labels!=null&&o.labels.Length>0?o.labels[0]:o.gameObject.name;
        private static Color ReadColor(Material m)=>m&&m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):m&&m.HasProperty("_Color")?m.GetColor("_Color"):Color.white;
        private static void SetColor(Material m,Color c){if(!m)return;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Color"))m.color=c;}
        private AuthoringExecutionResult Ok(AuthoringAction a,string m)=>new AuthoringExecutionResult{actionId=a.actionId,success=true,message=m};
        private AuthoringExecutionResult Fail(AuthoringAction a,string code,string m)=>new AuthoringExecutionResult{actionId=a?.actionId,success=false,message=m,error=new AuthoringValidationError{code=code,message=m}};
    }
    public class AuthoringSemanticState : MonoBehaviour { public string state; }
    public class AuthoringAffordanceState : MonoBehaviour
    {
        public bool grabbable; public bool movable; public bool interactable;
        public bool Get(string id)=>id=="grabbable"?grabbable:id=="movable"?movable:interactable;
        public void Set(string id,bool value){if(id=="grabbable")grabbable=value;else if(id=="movable")movable=value;else if(id=="interactable")interactable=value;}
    }
    public class RuntimeAuthoringMetadata : MonoBehaviour { public string createdByActionId; public string createdDuringTaskId; }
    public class AuthoringPropertyMarker : MonoBehaviour { public string[] appliedProperties=new string[0]; public void Mark(string property){if(string.IsNullOrWhiteSpace(property)||Array.IndexOf(appliedProperties,property)>=0)return;var next=new string[appliedProperties.Length+1];Array.Copy(appliedProperties,next,appliedProperties.Length);next[next.Length-1]=property;appliedProperties=next;} public bool Has(string property)=>Array.IndexOf(appliedProperties,property)>=0; }
}
