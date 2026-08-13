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
            if(envelope.type=="proposal"&&envelope.proposal!=null) ReceiveProposal(envelope.proposal);
            else if(envelope.type=="execute"&&envelope.execution!=null) Execute(envelope.execution);
            else if(envelope.type=="undo"&&envelope.undo!=null) Undo(envelope.undo);
            else if(envelope.type=="predefined_command" && envelope.predefinedCommand!=null) ExecutePredefined(envelope.predefinedCommand);
            else if(envelope.type=="next_task" && envelope.nextTask!=null) ActivateNextTask(envelope.nextTask);
            else if(envelope.type=="scene_api" && envelope.sceneApi!=null) ExecuteSceneApi(envelope.sceneApi);
            else if(envelope.type=="behavior_api" && envelope.behaviorApi!=null) ExecuteBehaviorApi(envelope.behaviorApi);
            else SendAck(null,null,false,"unsupported_message");
        }
        private void ReceiveProposal(AuthoringProposal p)
        { if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAck(p.proposalId,p.actionId,false,"authoring_unavailable");return;} if(p.proactive){SendAck(p.proposalId,p.actionId,false,"proactive_authoring_disabled");return;} proposalPresenter?.Show(p); telemetry?.Log("proposal_received",p.action?.targetObjectId,p.actionId,true); SendAck(p.proposalId,p.actionId,true,"proposal_received"); }
        public void Confirm(AuthoringProposal p){if(p==null)return; telemetry?.Log("proposal_confirmed",p.action?.targetObjectId,p.actionId,true); Execute(new AuthoringExecutionRequest{action=p.action});}
        public void Reject(AuthoringProposal p){telemetry?.Log("proposal_rejected",p?.action?.targetObjectId,p?.actionId,true);SendAck(p?.proposalId,p?.actionId,false,"participant_rejected");}
        public void Modify(AuthoringProposal p){telemetry?.Log("proposal_modify_selected",p?.action?.targetObjectId,p?.actionId,true);SendAck(p?.proposalId,p?.actionId,false,"modify_requested");}
        private void Execute(AuthoringExecutionRequest request){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAck(null,request.action?.actionId,false,"authoring_unavailable");return;} telemetry?.Log("action_execution_started",request.action?.targetObjectId,request.action?.actionId,true);var result=executor.Execute(request.action); telemetry?.Log(result.success?"action_applied":"action_failed",request.action?.targetObjectId,result.actionId,result.success);SendJson("execution_result",result);sceneContext?.SendSceneContextSnapshot("authoring execution");}
        private void Undo(AuthoringUndoRequest request){telemetry?.Log("undo_requested",null,request?.actionId,true);var result=undoManager.UndoLast();telemetry?.Log(result.success?"undo_applied":"action_failed",null,result.actionId,result.success);SendJson("undo_result",result);sceneContext?.SendSceneContextSnapshot("authoring undo");}
        private void ExecutePredefined(PredefinedVoiceCommand command){if(conditionManager==null||conditionManager.condition!=ExperimentCondition.VoiceCommandBaseline){SendAck(null,command?.commandId,false,"predefined_command_not_available");return;}var result=predefinedCommandExecutor.Execute(command);SendJson("predefined_command_ack",result);}
        private void ExecuteSceneApi(SceneApiCall call){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAck(null,call?.action?.actionId,false,"authoring_unavailable");return;}var result=new SceneApiExecutor(executor).Execute(call);SendJson("execution_result",result);sceneContext?.SendSceneContextSnapshot("SceneAPI");}
        private void ExecuteBehaviorApi(BehaviorApiCall call){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAck(null,call?.action?.actionId,false,"authoring_unavailable");return;}var result=new BehaviorApiExecutor(executor).Execute(call);SendJson("execution_result",result);sceneContext?.SendSceneContextSnapshot("BehaviorAPI");}
        private void ActivateNextTask(NextTaskSpec spec){if(conditionManager==null||!conditionManager.IsDynamicStorytelling){SendNextTaskAck(spec?.taskId,false,"dynamic_storytelling_not_active");return;}var success=dynamicStoryTaskController!=null&&dynamicStoryTaskController.ActivateNextTask(spec,out var error);SendNextTaskAck(spec?.taskId,success,success?"activated":error);}
        public void SendSessionConfiguration(ExperimentConditionManager manager){SendJson("session_configuration",new { participantCode=manager.participantCode,sessionId=manager.sessionId,condition=manager.condition.ToString(),questId=manager.questId,questVariant=manager.questVariant,conditionOrderIndex=manager.conditionOrderIndex,configuration=manager.studyConfiguration?manager.studyConfiguration.ExportJson():null });}
        public void SendAck(string proposalId,string actionId,bool accepted,string reason){SendJson("ack",new AuthoringAcknowledgement{type="ack",peer=Peer(),proposalId=proposalId,actionId=actionId,accepted=accepted,reason=reason});}
        public void SendExperimentEvent(ExperimentEvent evt){SendJson("experiment_event",evt);}
        public void SendTaskCompleted(string taskId){SendJson("task_completed",new { taskId });}
        public void SendNextTaskAck(string taskId,bool accepted,string reason){SendJson("next_task_ack",new { taskId, accepted, reason });}
        private void SendJson(string type,object body){Resolve();if(string.IsNullOrEmpty(Peer()))return;var json=JsonConvert.SerializeObject(new { type, body });var peer=Encoding.UTF8.GetBytes(Peer());var payload=Encoding.UTF8.GetBytes(json);var message=ReferenceCountedSceneGraphMessage.Rent(peer.Length+payload.Length);peer.CopyTo(new Span<byte>(message.bytes,message.start,peer.Length));payload.CopyTo(new Span<byte>(message.bytes,message.start+peer.Length,payload.Length));outgoing.Send(message);}
        private string Peer()=>roomClient!=null&&roomClient.Me!=null?roomClient.Me.uuid:null;
    }
}
