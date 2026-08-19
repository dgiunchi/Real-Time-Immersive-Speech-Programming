using System;
using System.Text;
using DreamCodeVR2.SceneContext;
using Newtonsoft.Json;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringProtocolClient : MonoBehaviour
    {
        public NetworkId incomingNetworkId = new NetworkId(101); public NetworkId outgoingNetworkId = new NetworkId(102);
        public ExperimentConditionManager conditionManager; public AuthoringActionExecutor executor; public AuthoringUndoManager undoManager;
        public AuthoringProposalPresenter proposalPresenter; public ExperimentTelemetry telemetry; public SceneContextTransmitter sceneContext;
        public PredefinedVoiceCommandExecutor predefinedCommandExecutor; public DreamCodeVR2.Quest.DynamicStoryTaskController dynamicStoryTaskController;
        private NetworkContext outgoing; private RoomClient roomClient;
        public void ClearPendingProtocolState() { proposalPresenter?.Cancel(); }
        private void Start() { NetworkScene.Register(this,incomingNetworkId); outgoing=NetworkScene.Register(this,outgoingNetworkId); Resolve(); }
        private void Resolve(){if(!conditionManager)conditionManager=FindFirstObjectByType<ExperimentConditionManager>();if(!executor)executor=FindFirstObjectByType<AuthoringActionExecutor>();if(!undoManager)undoManager=FindFirstObjectByType<AuthoringUndoManager>();if(!proposalPresenter)proposalPresenter=FindFirstObjectByType<AuthoringProposalPresenter>();if(!telemetry)telemetry=FindFirstObjectByType<ExperimentTelemetry>();if(!sceneContext)sceneContext=FindFirstObjectByType<SceneContextTransmitter>();if(!predefinedCommandExecutor)predefinedCommandExecutor=FindFirstObjectByType<PredefinedVoiceCommandExecutor>();if(!dynamicStoryTaskController)dynamicStoryTaskController=FindFirstObjectByType<DreamCodeVR2.Quest.DynamicStoryTaskController>();if(!roomClient)roomClient=NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();}
        public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
        {
            Resolve(); var raw=Encoding.UTF8.GetString(data.bytes,data.start,data.length); if(raw.Length>=36&&raw[0]=='{'==false) raw=raw.Substring(36);
            AuthoringEnvelope envelope; try { envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>(raw); } catch(Exception) { SendAck(null,null,false,"malformed_message"); return; }
            if(envelope==null) { SendAck(null,null,false,"malformed_message"); return; }
            if(envelope.type=="AuthoringProposal"&&envelope.action!=null) ReceiveProposal(new AuthoringProposal{action=envelope.action,actionId=envelope.action_id,interpretation=envelope.interpretation,expectedEffect=envelope.expected_effect,targetDisplayName=envelope.target_object_id});
            else if(envelope.type=="AuthoringExecutionRequest"&&envelope.action!=null) Execute(new AuthoringExecutionRequest{action=envelope.action});
            else if(envelope.type=="AuthoringUndoRequest") Undo(new AuthoringUndoRequest{actionId=envelope.action_id});
            else if(envelope.type=="PredefinedCommandProposal"&&envelope.command!=null) proposalPresenter?.Show(new AuthoringProposal{actionId=envelope.command_id,interpretation=envelope.interpretation,expectedEffect=envelope.interpretation});
            else if(envelope.type=="PredefinedCommandExecutionRequest"&&envelope.command!=null) ExecutePredefined(envelope.command);
            else if(envelope.type=="NextTaskGenerated") dynamicStoryTaskController?.StoreGeneratedTask(envelope.task);
            else if(envelope.type=="NextTaskActivationRequest") ActivateNextTask(dynamicStoryTaskController?.GetGeneratedTask(envelope.task_id));
            else if(envelope.type=="PredefinedCommandRejected"||envelope.type=="AuthoringRejected"||envelope.type=="AuthoringStatus") telemetry?.Log("server_status",null,envelope.action_id??envelope.command_id,false);
            else SendAuthoringAck(envelope.action_id,"failed","unsupported_message");
        }
        private void ReceiveProposal(AuthoringProposal p)
        { if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(p.actionId,"failed","authoring_unavailable");return;} proposalPresenter?.Show(p); telemetry?.Log("proposal_received",p.action?.targetObjectId,p.actionId,true); }
        public void Confirm(AuthoringProposal p){if(p==null)return; telemetry?.Log("proposal_confirmed",p.action?.targetObjectId,p.actionId,true);}
        public void Reject(AuthoringProposal p){telemetry?.Log("proposal_rejected",p?.action?.targetObjectId,p?.actionId,true);}
        public void Modify(AuthoringProposal p){telemetry?.Log("proposal_modify_selected",p?.action?.targetObjectId,p?.actionId,true);}
        private void Execute(AuthoringExecutionRequest request){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(request.action?.actionId,"failed","authoring_unavailable");return;} MapOperation(request.action);var result=executor.Execute(request.action);SendAuthoringAck(result.actionId,result.success?"applied":"failed",result.message);sceneContext?.SendSceneContextSnapshot("authoring execution");}
        private void Undo(AuthoringUndoRequest request){var result=undoManager.UndoLast();SendAuthoringAck(result.actionId,result.success?"undone":"failed",result.message);sceneContext?.SendSceneContextSnapshot("authoring undo");}
        private void ExecutePredefined(PredefinedVoiceCommand command){if(conditionManager==null||conditionManager.condition!=ExperimentCondition.VoiceCommandBaseline){SendPredefinedAck(command?.commandId,"failed","predefined_command_not_available");return;}var result=predefinedCommandExecutor.Execute(command);SendPredefinedAck(command.commandId,result.success?"applied":"failed",result.message);}
        private void ActivateNextTask(NextTaskSpec spec){if(conditionManager==null||!conditionManager.IsDynamicStorytelling){SendNextTaskAck(spec?.taskId,false,"dynamic_storytelling_not_active");return;}var error="Dynamic story task controller is unavailable.";var success=dynamicStoryTaskController!=null&&dynamicStoryTaskController.ActivateNextTask(spec,out error);if(!success)SendNextTaskAck(spec?.taskId,false,error);}
        public void SendAck(string proposalId,string actionId,bool accepted,string reason){SendAuthoringAck(actionId,accepted?"applied":"failed",reason);}
        public void SendExperimentEvent(ExperimentEvent evt)
        {
            if(evt==null||!IsCanonicalExperimentEvent(evt.eventType))return;
            SendFlat(new { type="ExperimentStateEvent", @event=evt.eventType, task_id=evt.taskId });
        }
        public AuthoringExecutionResult ExecuteLocalSceneApi(SceneApiCall call)
        {
            Resolve();
            if(conditionManager==null||!conditionManager.IsAuthoringAvailable)return new AuthoringExecutionResult{actionId=call?.action?.actionId,success=false,message="Authoring is unavailable for this condition."};
            return new SceneApiExecutor(executor).Execute(call);
        }
        public AuthoringExecutionResult ExecuteLocalBehaviorApi(BehaviorApiCall call)
        {
            Resolve();
            if(conditionManager==null||!conditionManager.IsAuthoringAvailable)return new AuthoringExecutionResult{actionId=call?.action?.actionId,success=false,message="Authoring is unavailable for this condition."};
            return new BehaviorApiExecutor(executor).Execute(call);
        }
        public AuthoringExecutionResult ExecuteLocalPredefined(PredefinedVoiceCommand command)
        {
            Resolve();
            if(conditionManager==null||conditionManager.condition!=ExperimentCondition.VoiceCommandBaseline)return new AuthoringExecutionResult{actionId=command?.commandId,success=false,message="Predefined commands are available only in C1."};
            return predefinedCommandExecutor.Execute(command);
        }
        public void SendTaskCompleted(string taskId){SendFlat(new { type="ExperimentStateEvent", @event="task_completed", task_id=taskId });}
        public void SendNextTaskAck(string taskId,bool accepted,string reason){if(accepted)SendFlat(new { type="NextTaskAck", status="activated", task_id=taskId });}
        private void SendAuthoringAck(string actionId,string status,string detail){SendFlat(new { type="AuthoringAck", action_id=actionId, status, detail });}
        private void SendPredefinedAck(string commandId,string status,string detail){SendFlat(new { type="PredefinedCommandAck", command_id=commandId, status, detail });}
        private void SendFlat(object dto){Resolve();if(string.IsNullOrEmpty(Peer()))return;var payload=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(dto));var peer=Encoding.UTF8.GetBytes(Peer());var message=ReferenceCountedSceneGraphMessage.Rent(peer.Length+payload.Length);peer.CopyTo(new Span<byte>(message.bytes,message.start,peer.Length));payload.CopyTo(new Span<byte>(message.bytes,message.start+peer.Length,payload.Length));outgoing.Send(message);}
        private void MapOperation(AuthoringAction a){switch(a.operation){case "set_property":a.kind=AuthoringActionKind.SET_PROPERTY;break;case "set_affordance":a.kind=AuthoringActionKind.SET_AFFORDANCE;break;case "create_object":a.kind=AuthoringActionKind.CREATE_OBJECT;break;case "relocate_object":a.kind=AuthoringActionKind.RELOCATE_OBJECT;break;case "toggle_state":a.kind=AuthoringActionKind.TOGGLE_STATE;break;case "add_behavior":a.kind=AuthoringActionKind.ADD_BEHAVIOR;break;case "link_objects":a.kind=AuthoringActionKind.LINK_OBJECTS;break;}}
        private static bool IsCanonicalExperimentEvent(string value)=>value=="task_started"||value=="task_completed"||value=="incorrect_attempt"||value=="hint_requested"||value=="session_completed";
        private string Peer()=>roomClient!=null&&roomClient.Me!=null?roomClient.Me.uuid:null;
        public string CurrentPeerUuid { get { Resolve(); return Peer(); } }
    }
}
