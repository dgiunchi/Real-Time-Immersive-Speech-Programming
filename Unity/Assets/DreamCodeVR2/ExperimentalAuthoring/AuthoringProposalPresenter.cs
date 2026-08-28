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
        public void ShowC1ExecutionFeedback(AuthoringExecutionResult result, ExperimentalDrawerController drawer)
        {
            PendingProposal = null;
            if (result == null || !result.success)
            {
                ui?.ShowC1CommandFeedback(false, result?.message);
                DreamCodeVR2ClientLogger.Event("participant_ui", "C1_COMMAND_FAILURE_FEEDBACK_SHOWN", result?.message);
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
        private void ShowC1SuccessFeedback(){ui?.ShowC1CommandFeedback(true,null);DreamCodeVR2ClientLogger.Event("participant_ui", "C1_COMMAND_SUCCESS_FEEDBACK_SHOWN");}
        private void Clear(){PendingProposal=null;ui?.HideProposal();}
    }
}
