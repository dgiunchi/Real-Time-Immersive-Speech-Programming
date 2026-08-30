using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.SceneContext;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class PredefinedVoiceCommandExecutor : MonoBehaviour
    {
        public SceneContextTransmitter sceneContext; public ExperimentTelemetry telemetry;
        public AuthoringExecutionResult Execute(PredefinedVoiceCommand command)
        {
            DreamCodeVR2ClientLogger.Event("c1","PREDEFINED_COMMAND_EXECUTE_LOCAL",null,new { command_id=command?.commandId,target_object_id=command?.targetObjectId,command=command?.command,preset=command?.preset,secondary_object_id=command?.secondaryObjectId });
            var target=AuthoringActionExecutor.FindEditable(command?.targetObjectId);
            if(!target) return FailAndLog(command,"missing_target","The selected object is unavailable.");
            var capabilities=target.GetComponent<VoiceCommandCapabilities>();
            if(!capabilities||!capabilities.Allows(command.command)) return FailAndLog(command,"command_not_allowed","That voice command is unavailable for this object.");
            switch(command.command.ToUpperInvariant())
            {
                case "OPEN":
                    if(!DreamCodeVR2.Quest.QuestLockController.CanOpenTarget(target.objectId,out var lockError)) return Fail(command,"target_locked",lockError);
                    if(target.GetComponent<ExperimentalDrawerController>() is ExperimentalDrawerController drawer && !drawer.TryOpen(out var openError)) return Fail(command,"motion_configuration",openError);
                    if(target.GetComponent<DreamCodeVR2.Quest.QuestDoorController>() is DreamCodeVR2.Quest.QuestDoorController door && !door.TryOpen(out openError)) return Fail(command,"door_open_failed",openError);
                    if(!target.GetComponent<ExperimentalDrawerController>()&&!target.GetComponent<DreamCodeVR2.Quest.QuestDoorController>()) return Fail(command,"missing_controller","This object has no open controller."); break;
                case "CLOSE":
                    if(target.GetComponent<ExperimentalDrawerController>() is ExperimentalDrawerController closeDrawer && !closeDrawer.TryClose(out var closeError)) return Fail(command,"motion_configuration",closeError);
                    if(target.GetComponent<DreamCodeVR2.Quest.QuestDoorController>() is DreamCodeVR2.Quest.QuestDoorController closeDoor && !closeDoor.TryClose(out closeError)) return Fail(command,"door_close_failed",closeError);
                    if(!target.GetComponent<ExperimentalDrawerController>()&&!target.GetComponent<DreamCodeVR2.Quest.QuestDoorController>()) return Fail(command,"missing_controller","This object has no close controller."); break;
                case "ACTIVATE": if(target.GetComponent<DreamCodeVR2.Quest.QuestLampController>() is DreamCodeVR2.Quest.QuestLampController lamp) lamp.SetLampState(true); else return Fail(command,"missing_controller","This object has no activation controller."); break;
                case "DEACTIVATE": if(target.GetComponent<DreamCodeVR2.Quest.QuestLampController>() is DreamCodeVR2.Quest.QuestLampController offLamp) offLamp.SetLampState(false); else return Fail(command,"missing_controller","This object has no activation controller."); break;
                case "TOGGLE": if(target.GetComponent<DreamCodeVR2.Quest.QuestLampController>() is DreamCodeVR2.Quest.QuestLampController toggleLamp) toggleLamp.Toggle(); else return Fail(command,"missing_controller","This object has no toggle controller."); break;
                case "MOVE_TO_PRESET":
                    if(target.GetComponent<DreamCodeVR2.Quest.C1QuestSphereController>() is DreamCodeVR2.Quest.C1QuestSphereController sphere)
                    { if(!string.Equals(command.preset,"soccer_ball",System.StringComparison.OrdinalIgnoreCase))return Fail(command,"unsupported_preset","That sphere preset is unavailable.");if(!sphere.TryApplySoccerBallPreset(out var sphereError))return Fail(command,"soccer_ball_preset_failed",sphereError); }
                    else if(target.GetComponent<DreamCodeVR2.Quest.QuestPaintingController>() is DreamCodeVR2.Quest.QuestPaintingController painting && !painting.TryAlign(out var paintError)) return Fail(command,"painting_alignment_configuration",paintError);
                    else if(!target.GetComponent<DreamCodeVR2.Quest.QuestPaintingController>()) return Fail(command,"missing_controller","This object has no preset controller."); break;
                case "PLACE_IN":
                    DreamCodeVR2ClientLogger.Event("quest","PLACE_IN_REQUEST",null,new { command_id=command.commandId,object_id=target.objectId,receptacle_id=command.secondaryObjectId });
                    var receptacle=AuthoringActionExecutor.FindEditable(command.secondaryObjectId);
                    var instance=FindFirstObjectByType<DreamCodeVR2.Quest.QuestInstanceController>();
                    if(!receptacle||!instance||!instance.AllowsC1PlaceIn(target.objectId,receptacle.objectId,out var placementAnchor)){DreamCodeVR2ClientLogger.Warn("quest","PLACE_IN_FAILED","The requested placement is not allowed by the active quest.",new { command_id=command.commandId });return Fail(command,"placement_not_allowed","The requested placement is not allowed by the active quest.");}
                    var monitor=placementAnchor.GetComponent<DreamCodeVR2.Quest.QuestPlacementMonitor>();
                    if(!monitor||!monitor.NotifyPlaced(target)){DreamCodeVR2ClientLogger.Warn("quest","PLACE_IN_FAILED","The placement region is unavailable.",new { command_id=command.commandId });return Fail(command,"missing_placement_region","The placement region is unavailable.");}
                    target.transform.SetParent(placementAnchor.transform,true);
                    target.transform.SetPositionAndRotation(DreamCodeVR2.Quest.QuestSoccerBall.SpawnPosition(placementAnchor,DreamCodeVR2.Quest.QuestSoccerBall.EffectiveWorldRadius(target.GetComponent<SphereCollider>())),placementAnchor.transform.rotation);
                    DreamCodeVR2ClientLogger.Event("quest","PLACE_IN_APPLIED",null,new { command_id=command.commandId,object_id=target.objectId,anchor_id=placementAnchor.anchorId });break;
                case "USE_WITH":
                    var secondary=AuthoringActionExecutor.FindEditable(command.secondaryObjectId);
                    var lockController=secondary?secondary.GetComponent<DreamCodeVR2.Quest.QuestLockController>():null;
                    if(!lockController)return Fail(command,"missing_lock","The selected lock is unavailable.");
                    if(!IsKeyCompatible(target))return Fail(command,"invalid_key","The primary object is not a compatible key.");
                    if(!lockController.TryUseKey(target.objectId,out var useError))return Fail(command,"wrong_key",useError);
                    break;
                default: return Fail(command,"command_not_allowed","That voice command is unavailable.");
            }
            sceneContext?.SendSceneContextSnapshot("predefined voice command"); telemetry?.Log("predefined_command_applied",target.objectId,command.commandId,true);
            return new AuthoringExecutionResult{actionId=command.commandId,success=true,message="Command applied."};
        }
        private static bool IsKeyCompatible(AIEditableObject item)
        {
            if(!item)return false;
            if(item.labels!=null) foreach(var label in item.labels) if(!string.IsNullOrEmpty(label)&&label.IndexOf("key",System.StringComparison.OrdinalIgnoreCase)>=0)return true;
            return item.objectId!=null&&item.objectId.IndexOf("key",System.StringComparison.OrdinalIgnoreCase)>=0;
        }
        private static AuthoringExecutionResult Fail(PredefinedVoiceCommand command,string code,string message)=>new AuthoringExecutionResult{actionId=command?.commandId,success=false,message=message,error=new AuthoringValidationError{code=code,message=message}};
        private static AuthoringExecutionResult FailAndLog(PredefinedVoiceCommand command,string code,string message){var result=Fail(command,code,message);DreamCodeVR2ClientLogger.Warn("c1","PREDEFINED_COMMAND_LOCAL_REJECTION",message,new { command_id=command?.commandId,target_object_id=command?.targetObjectId,command=command?.command,preset=command?.preset,code });return result;}
    }
}
