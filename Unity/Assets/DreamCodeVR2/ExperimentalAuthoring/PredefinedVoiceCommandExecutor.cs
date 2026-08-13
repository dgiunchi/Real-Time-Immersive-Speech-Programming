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
            var target=AuthoringActionExecutor.FindEditable(command?.targetObjectId);
            if(!target) return Fail(command,"missing_target","The selected object is unavailable.");
            var capabilities=target.GetComponent<VoiceCommandCapabilities>();
            if(!capabilities||!capabilities.Allows(command.command)||!capabilities.target) return Fail(command,"command_not_allowed","That voice command is unavailable for this object.");
            switch(command.command.ToUpperInvariant())
            {
                case "OPEN": capabilities.target.Open(); break;
                case "CLOSE": capabilities.target.Close(); break;
                case "ACTIVATE": capabilities.target.SetActiveState(true); break;
                case "DEACTIVATE": capabilities.target.SetActiveState(false); break;
                case "MOVE_TO_PRESET": capabilities.target.MoveToPreset(command.preset); break;
                case "USE_WITH": capabilities.target.UseWith(AuthoringActionExecutor.FindEditable(command.preset)?.gameObject); break;
                default: return Fail(command,"command_not_allowed","That voice command is unavailable.");
            }
            sceneContext?.SendSceneContextSnapshot("predefined voice command"); telemetry?.Log("predefined_command_applied",target.objectId,command.commandId,true);
            return new AuthoringExecutionResult{actionId=command.commandId,success=true,message="Command applied."};
        }
        private static AuthoringExecutionResult Fail(PredefinedVoiceCommand command,string code,string message)=>new AuthoringExecutionResult{actionId=command?.commandId,success=false,message=message,error=new AuthoringValidationError{code=code,message=message}};
    }
}
