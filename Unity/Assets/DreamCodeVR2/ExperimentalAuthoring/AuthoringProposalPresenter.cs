using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringProposalPresenter : MonoBehaviour
    {
        public DreamCodeVRAuthoringUIController ui; public AuthoringProtocolClient protocol; public AuthoringProposal PendingProposal { get; private set; }
        public bool HasPendingProposal => PendingProposal != null;
        private void Start(){if(!ui)ui=FindFirstObjectByType<DreamCodeVRAuthoringUIController>();if(!protocol)protocol=FindFirstObjectByType<AuthoringProtocolClient>();}
        public void Show(AuthoringProposal proposal){if(HasPendingProposal){Debug.LogWarning("[Authoring] proposal ignored while another proposal is displayed.");return;}PendingProposal=proposal;ui?.ShowProposal("I understood: "+proposal.expectedEffect+" Apply this change?",proposal.targetDisplayName,proposal.reason);}
        public void ShowC1PredefinedProposal(PredefinedVoiceCommand command,string originalUtterance,string commandId)
        {
            if(HasPendingProposal){Debug.LogWarning("[Authoring] predefined proposal ignored while another proposal is displayed.");return;}
            var spoken=string.IsNullOrWhiteSpace(originalUtterance)?ParticipantFacingText.Describe(command):originalUtterance.Trim();
            PendingProposal=new AuthoringProposal{actionId=commandId,interpretation=spoken,expectedEffect=spoken};
            ui?.ShowProposal("You said:\n\""+spoken+"\"\n\nConfirm this action?",null,null);
        }
        public void Confirm(){var p=PendingProposal;Clear();protocol?.Confirm(p);} public void Reject(){var p=PendingProposal;Clear();protocol?.Reject(p);} public void Modify(){var p=PendingProposal;Clear();protocol?.Modify(p);} public void Cancel()=>Reject();
        // The server sends this after the participant says "no" to a C1 proposal.
        // It is only a terminal UI state: no acknowledgement is sent back from here.
        public bool DismissRejectedPredefinedProposal(string commandId)
        {
            var pending = PendingProposal;
            if (pending == null) return false;
            if (!string.IsNullOrWhiteSpace(commandId) && !string.IsNullOrWhiteSpace(pending.actionId) && pending.actionId != commandId)
            {
                Debug.LogWarning("[Authoring] ignored rejected predefined command for a non-pending proposal.");
                return false;
            }

            Clear();
            DreamCodeVR2ClientLogger.Event("participant_ui", "C1_COMMAND_CANCELLED", null, new { command_id = commandId });
            return true;
        }
        public void ShowC1ExecutionFeedback(AuthoringExecutionResult result, ExperimentalDrawerController drawer, string commandId)
        {
            PendingProposal = null;
            if (result == null || !result.success)
            {
                ShowC1Failure(result?.error?.code??"local_execution_failed","local_executor",commandId);
                return;
            }
            if (drawer && drawer.IsMoving)
            {
                System.Action<bool> completed = null;
                completed = _ => { drawer.MotionCompleted -= completed; ShowC1SuccessFeedback(); };
                drawer.MotionCompleted += completed;
                return;
            }
            ShowC1SuccessFeedback();
        }
        // This is deliberately the only mapping from diagnostics/protocol reasons to
        // participant text. Raw server details and object IDs never reach the panel.
        public static string ParticipantSafeFailureMessage(string reasonCode)
        {
            switch((reasonCode??string.Empty).Trim().ToLowerInvariant())
            {
                case "ambiguous_target": return "Please specify which object.";
                case "missing_capability": case "command_not_allowed": case "capability_rejected": case "unsupported_interaction": return "That action is not available.";
                case "target_not_in_task_scope": case "quest_integrity": case "predefined_command_not_available": return "That action is not available right now.";
                case "wrong_key": case "invalid_key": case "key_lock_failed": case "lock_rejected": return "That key does not fit this lock.";
                case "object_locked": case "target_locked": return "The object is locked.";
                case "missing_preset": case "unsupported_preset": case "soccer_ball_preset_failed": case "missing_soccer_material": return "That transformation is not available.";
                case "missing_target": return "That object is not available.";
                default: return "Command failed.";
            }
        }
        public void ShowC1Failure(string reasonCode,string source,string commandId)
        {
            var participantMessage=ParticipantSafeFailureMessage(reasonCode);
            PendingProposal=null;
            ui?.ShowC1CommandFeedback(false,participantMessage);
            DreamCodeVR2ClientLogger.Event("participant_ui","VOICE_COMMAND_FEEDBACK_SHOWN",null,new { feedback_type="failure",reason_code=string.IsNullOrWhiteSpace(reasonCode)?"unknown":reasonCode,source,command_id=commandId });
        }
        private void ShowC1SuccessFeedback(){ui?.ShowC1CommandFeedback(true,null);DreamCodeVR2ClientLogger.Event("participant_ui", "C1_COMMAND_SUCCESS_FEEDBACK_SHOWN");}
        private void Clear(){PendingProposal=null;ui?.HideProposal();}
    }
}
