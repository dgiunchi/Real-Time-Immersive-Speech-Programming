using System;
using System.Text;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            DreamCodeVR2ClientLogger.Event("protocol", "NID101_RECEIVED", null, new { type=envelope.type, action_id=envelope.action_id, command_id=envelope.command_id, task_id=envelope.task_id });
            if(envelope.type=="AuthoringProposal") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_PROPOSAL",null,new { action_id=envelope.action_id });
            else if(envelope.type=="AuthoringExecutionRequest") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_EXECUTION_REQUEST",null,new { action_id=envelope.action_id });
            else if(envelope.type=="AuthoringUndoRequest") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_UNDO_REQUEST",null,new { action_id=envelope.action_id });
            else if(envelope.type=="PredefinedCommandProposal") { DreamCodeVR2ClientLogger.Event("protocol","PREDEFINED_COMMAND_PROPOSAL",null,new { command_id=envelope.command_id }); DreamCodeVR2ClientLogger.Event("c1","PREDEFINED_COMMAND_PROPOSED",null,new { command_id=envelope.command_id }); }
            else if(envelope.type=="PredefinedCommandExecutionRequest") DreamCodeVR2ClientLogger.Event("protocol","PREDEFINED_COMMAND_EXECUTION_REQUEST",null,new { command_id=envelope.command_id });
            else if(envelope.type=="AuthoringStatus") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_STATUS",null,new { action_id=envelope.action_id });
            if(envelope.type=="PredefinedCommandProposal"||envelope.type=="PredefinedCommandRejected"||envelope.type=="AuthoringProposal"||envelope.type=="AuthoringRejected"||envelope.type=="AuthoringStatus") FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.ResolveProcessingForServerResponse(envelope.type);
            if(envelope.type=="AuthoringProposal"&&envelope.action!=null) ReceiveProposal(new AuthoringProposal{action=envelope.action,actionId=envelope.action_id,interpretation=envelope.interpretation,expectedEffect=envelope.expected_effect,targetDisplayName=envelope.target_object_id});
            else if(envelope.type=="AuthoringExecutionRequest"&&envelope.action!=null) Execute(new AuthoringExecutionRequest{action=envelope.action});
            else if(envelope.type=="AuthoringUndoRequest") Undo(new AuthoringUndoRequest{actionId=envelope.action_id});
            else if(envelope.type=="PredefinedCommandProposal"&&envelope.command!=null) proposalPresenter?.Show(new AuthoringProposal{actionId=envelope.command_id,interpretation=envelope.interpretation,expectedEffect=envelope.interpretation});
            else if(envelope.type=="PredefinedCommandExecutionRequest"&&envelope.command!=null) ExecutePredefined(envelope.command);
            else if(envelope.type=="NextTaskGenerated") { if(NextTaskWireConverter.TryConvert(envelope.task,out var task,out var error)){dynamicStoryTaskController?.StoreGeneratedTask(task);DreamCodeVR2ClientLogger.Event("c3", "C3_WIRE_CONVERSION_SUCCESS", null, new { task_id=task.taskId });DreamCodeVR2ClientLogger.Event("c3", "C3_NEXT_TASK_GENERATED", null, new { task_id=task.taskId });}else {DreamCodeVR2ClientLogger.Warn("c3", "C3_WIRE_CONVERSION_FAILED", error, new { task_id=envelope.task_id });DreamCodeVR2ClientLogger.Warn("c3", "C3_TASK_REJECTED", error, new { task_id=envelope.task_id });Debug.LogWarning("[C3] rejected generated task: "+error,this);} }
            else if(envelope.type=="NextTaskActivationRequest") { DreamCodeVR2ClientLogger.Event("c3", "C3_TASK_ACTIVATION_REQUEST", null, new { task_id=envelope.task_id }); ActivateNextTask(dynamicStoryTaskController?.GetGeneratedTask(envelope.task_id)); }
            else if(envelope.type=="PredefinedCommandRejected") { proposalPresenter?.DismissRejectedPredefinedProposal(envelope.command_id); telemetry?.Log("server_status",null,envelope.command_id,false); }
            else if(envelope.type=="AuthoringRejected"||envelope.type=="AuthoringStatus") telemetry?.Log("server_status",null,envelope.action_id??envelope.command_id,false);
            else SendAuthoringAck(envelope.action_id,"failed","unsupported_message");
        }
        private void ReceiveProposal(AuthoringProposal p)
        { if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(p.actionId,"failed","authoring_unavailable");return;} DreamCodeVR2ClientLogger.Event("authoring", "AUTHORING_PROPOSED", null, new { action_id=p.actionId, target_object_id=p.action?.targetObjectId });DreamCodeVR2ClientLogger.Event("authoring", conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_PROPOSAL_DISPLAYED":"C3_PROPOSAL_DISPLAYED", null, new { action_id=p.actionId, target_object_id=p.action?.targetObjectId }); proposalPresenter?.Show(p); telemetry?.Log("proposal_received",p.action?.targetObjectId,p.actionId,true); }
        public void Confirm(AuthoringProposal p){if(p==null)return; DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_CONFIRMED",null,new { action_id=p.actionId });telemetry?.Log("proposal_confirmed",p.action?.targetObjectId,p.actionId,true);}
        public void Reject(AuthoringProposal p){DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_REJECTED",null,new { action_id=p?.actionId });telemetry?.Log("proposal_rejected",p?.action?.targetObjectId,p?.actionId,true);}
        public void Modify(AuthoringProposal p){telemetry?.Log("proposal_modify_selected",p?.action?.targetObjectId,p?.actionId,true);}
        private void Execute(AuthoringExecutionRequest request){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(request.action?.actionId,"failed","authoring_unavailable");return;}var conditionEvent=conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_EXECUTION_REQUEST_RECEIVED":"C1_EXECUTION_REQUEST_RECEIVED";DreamCodeVR2ClientLogger.Event("authoring",conditionEvent,null,new { action_id=request.action?.actionId });DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_EXECUTION_START",null,new { action_id=request.action?.actionId, operation=request.action?.operation, target_object_id=request.action?.targetObjectId });MapOperation(request.action);var result=executor.Execute(request.action);DreamCodeVR2ClientLogger.Event("authoring",result.success?"AUTHORING_EXECUTION_APPLIED":"AUTHORING_EXECUTION_FAILED",result.message,new { action_id=result.actionId });DreamCodeVR2ClientLogger.Event("authoring",result.success?(conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_EXECUTION_APPLIED":"C1_EXECUTION_APPLIED"):(conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_EXECUTION_FAILED":"C1_EXECUTION_FAILED"),result.message,new { action_id=result.actionId });SendAuthoringAck(result.actionId,result.success?"applied":"failed",result.message);sceneContext?.SendSceneContextSnapshot("authoring execution");}
        private void Undo(AuthoringUndoRequest request){DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_UNDO_START",null,new { action_id=request.actionId });var result=undoManager.UndoLast();DreamCodeVR2ClientLogger.Event("authoring",result.success?"AUTHORING_UNDO_APPLIED":"AUTHORING_UNDO_FAILED",result.message,new { action_id=result.actionId });SendAuthoringAck(result.actionId,result.success?"undone":"failed",result.message);sceneContext?.SendSceneContextSnapshot("authoring undo");}
        private void ExecutePredefined(PredefinedVoiceCommand command){if(conditionManager==null||conditionManager.condition!=ExperimentCondition.VoiceCommandBaseline){SendPredefinedAck(command?.commandId,"failed","predefined_command_not_available");return;}DreamCodeVR2ClientLogger.Event("c1","PREDEFINED_COMMAND_CONFIRMED",null,new { command_id=command?.commandId });var result=predefinedCommandExecutor.Execute(command);DreamCodeVR2ClientLogger.Event("c1",result.success?"PREDEFINED_COMMAND_EXECUTED":"PREDEFINED_COMMAND_FAILED",result.message,new { command_id=command?.commandId });var drawer=AuthoringActionExecutor.FindEditable(command?.targetObjectId)?.GetComponent<ExperimentalDrawerController>();proposalPresenter?.ShowC1ExecutionFeedback(result,drawer);SendPredefinedAck(command.commandId,result.success?"applied":"failed",result.message);}
        private void ActivateNextTask(NextTaskSpec spec){if(conditionManager==null||!conditionManager.IsDynamicStorytelling){DreamCodeVR2ClientLogger.Warn("c3","C3_ACTIVATION_FAILED","C3 is not active.",new { task_id=spec?.taskId });Debug.LogWarning("[C3] activation ignored because C3 is not active.",this);return;}var error="Dynamic story task controller is unavailable.";var success=dynamicStoryTaskController!=null&&dynamicStoryTaskController.ActivateNextTask(spec,out error);DreamCodeVR2ClientLogger.Event("c3",success?"C3_ACTIVATION_SUCCESS":"C3_ACTIVATION_FAILED",success?null:error,new { task_id=spec?.taskId });if(!success)Debug.LogWarning("[C3] activation failed locally: "+error,this);}
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
        public void SendNextTaskAck(string taskId){DreamCodeVR2ClientLogger.Event("c3","C3_NEXT_TASK_ACK_SENT",null,new { task_id=taskId });SendFlat(new { type="NextTaskAck", status="activated", task_id=taskId });}
        private void SendAuthoringAck(string actionId,string status,string detail){SendFlat(new { type="AuthoringAck", action_id=actionId, status, detail });}
        private void SendPredefinedAck(string commandId,string status,string detail){SendFlat(new { type="PredefinedCommandAck", command_id=commandId, status, detail });}
        private void SendFlat(object dto){Resolve();if(string.IsNullOrEmpty(Peer()))return;var json=JsonConvert.SerializeObject(dto);var payload=Encoding.UTF8.GetBytes(json);var peer=Encoding.UTF8.GetBytes(Peer());var message=ReferenceCountedSceneGraphMessage.Rent(peer.Length+payload.Length);peer.CopyTo(new Span<byte>(message.bytes,message.start,peer.Length));payload.CopyTo(new Span<byte>(message.bytes,message.start+peer.Length,payload.Length));outgoing.Send(message);var metadata=JObject.Parse(json);DreamCodeVR2ClientLogger.Event("protocol","NID102_SENT",null,new { type=(string)metadata["type"], action_id=(string)metadata["action_id"], command_id=(string)metadata["command_id"], task_id=(string)metadata["task_id"], status=(string)metadata["status"], @event=(string)metadata["event"], payload_bytes=payload.Length });}
        private void MapOperation(AuthoringAction a){switch(a.operation){case "set_property":a.kind=AuthoringActionKind.SET_PROPERTY;break;case "set_affordance":a.kind=AuthoringActionKind.SET_AFFORDANCE;break;case "create_object":a.kind=AuthoringActionKind.CREATE_OBJECT;break;case "relocate_object":a.kind=AuthoringActionKind.RELOCATE_OBJECT;break;case "toggle_state":a.kind=AuthoringActionKind.TOGGLE_STATE;break;case "add_behavior":a.kind=AuthoringActionKind.ADD_BEHAVIOR;break;case "link_objects":a.kind=AuthoringActionKind.LINK_OBJECTS;break;}}
        private static bool IsCanonicalExperimentEvent(string value)=>value=="task_started"||value=="task_completed"||value=="incorrect_attempt"||value=="hint_requested"||value=="session_completed";
        private string Peer()=>roomClient!=null&&roomClient.Me!=null?roomClient.Me.uuid:null;
        public string CurrentPeerUuid { get { Resolve(); return Peer(); } }
    }
}
