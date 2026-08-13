using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringProposalPresenter : MonoBehaviour
    {
        public DreamCodeVRAuthoringUIController ui; public AuthoringProtocolClient protocol; public AuthoringProposal PendingProposal { get; private set; }
        public bool HasPendingProposal => PendingProposal != null;
        private void Start(){if(!ui)ui=FindFirstObjectByType<DreamCodeVRAuthoringUIController>();if(!protocol)protocol=FindFirstObjectByType<AuthoringProtocolClient>();}
        public void Show(AuthoringProposal proposal){if(HasPendingProposal){protocol?.SendAck(proposal.proposalId,proposal.actionId,false,"proposal_already_pending");return;}PendingProposal=proposal;ui?.ShowProposal(proposal.proactive?"A possible change is available: "+proposal.expectedEffect:"I understood: "+proposal.expectedEffect+" Apply this change?",proposal.targetDisplayName,proposal.reason);}
        public void Confirm(){var p=PendingProposal;Clear();protocol?.Confirm(p);} public void Reject(){var p=PendingProposal;Clear();protocol?.Reject(p);} public void Modify(){var p=PendingProposal;Clear();protocol?.Modify(p);} public void Cancel()=>Reject();
        private void Clear(){PendingProposal=null;ui?.HideProposal();}
    }
}
