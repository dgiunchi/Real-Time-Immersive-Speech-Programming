using System;
using System.Collections.Generic;
using System.Linq;
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
        public DreamCodeVR2.Quest.QuestInstanceController questInstanceController;
        public DreamCodeVR2.Quest.QuestConsequenceDispatcher consequenceDispatcher;
        private DreamCodeVR2.Quest.QuestInstance pendingFixedQuestInstance;
        private DreamCodeVR2.Quest.QuestTaskSpec pendingFixedTask;
        private string pendingFixedTaskId;
        private readonly HashSet<string> generatedTaskIdsThisSession = new HashSet<string>();
        private readonly HashSet<string> terminalSuccessfulPredefinedCommandIds = new HashSet<string>();
        private NetworkContext outgoing; private bool outgoingRegistered; private RoomClient roomClient;
        public void ClearPendingProtocolState() { proposalPresenter?.Cancel(); }
        private void Start() { NetworkScene.Register(this,incomingNetworkId); outgoing=NetworkScene.Register(this,outgoingNetworkId); outgoingRegistered=true; Resolve(); }
        private void Resolve(){if(!conditionManager)conditionManager=FindFirstObjectByType<ExperimentConditionManager>();if(!executor)executor=FindFirstObjectByType<AuthoringActionExecutor>();if(!undoManager)undoManager=FindFirstObjectByType<AuthoringUndoManager>();if(!proposalPresenter)proposalPresenter=FindFirstObjectByType<AuthoringProposalPresenter>();if(!telemetry)telemetry=FindFirstObjectByType<ExperimentTelemetry>();if(!sceneContext)sceneContext=FindFirstObjectByType<SceneContextTransmitter>();if(!predefinedCommandExecutor)predefinedCommandExecutor=FindFirstObjectByType<PredefinedVoiceCommandExecutor>();if(!dynamicStoryTaskController)dynamicStoryTaskController=FindFirstObjectByType<DreamCodeVR2.Quest.DynamicStoryTaskController>();if(!questInstanceController)questInstanceController=FindFirstObjectByType<DreamCodeVR2.Quest.QuestInstanceController>();if(!consequenceDispatcher)consequenceDispatcher=FindFirstObjectByType<DreamCodeVR2.Quest.QuestConsequenceDispatcher>();if(!roomClient)roomClient=NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();}
        public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
        {
            var wirePayload=Encoding.UTF8.GetString(data.bytes,data.start,data.length);
            var sourcePeer=(string)null;
            var raw=wirePayload;
            if(raw.Length>=36&&raw[0]=='{'==false){sourcePeer=raw.Substring(0,36);raw=raw.Substring(36);}
            // This is intentionally the first NID101 diagnostic, before JSON parsing or dispatch.
            DreamCodeVR2ClientLogger.Event("protocol","NID101_RAW_RECEIVED",null,new { timestamp_unix_ms=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), peer=sourcePeer, raw_json=TruncateDiagnosticJson(raw) });
            Resolve();
            AuthoringEnvelope envelope;
            try { envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>(raw); }
            catch(Exception exception) { DreamCodeVR2ClientLogger.Warn("protocol","NID101_PARSE_FAILED","NID101 JSON deserialization failed.",new { task_id=(string)null,error=exception.Message });SendAck(null,null,false,"malformed_message"); return; }
            if(envelope==null) { DreamCodeVR2ClientLogger.Warn("protocol","NID101_PARSE_FAILED","NID101 JSON deserialized to null.",new { task_id=(string)null,error="null_envelope" });SendAck(null,null,false,"malformed_message"); return; }
            var generatedTaskId=envelope.task?.task_id;
            DreamCodeVR2ClientLogger.Event("protocol","NID101_PARSED",null,new { message_type=envelope.type,task_id=envelope.task_id,task_task_id=envelope.type=="NextTaskGenerated"?generatedTaskId:null,quest_instance_present=envelope.type=="NextTaskGenerated"&&envelope.quest_instance!=null });
            if(envelope.type=="NextTaskGenerated"&&!string.IsNullOrWhiteSpace(generatedTaskId))generatedTaskIdsThisSession.Add(generatedTaskId);
            DreamCodeVR2ClientLogger.Event("protocol", "NID101_RECEIVED", null, new { type=envelope.type, action_id=envelope.action_id, command_id=envelope.command_id, task_id=envelope.task_id });
            // Keep the complete server response for Quest-side diagnosis. NID101 is the
            // authoring/task control channel (never microphone audio), so this is safe and
            // makes parser rejections and target resolution inspectable after a device run.
            DreamCodeVR2ClientLogger.Event("protocol","NID101_SERVER_PAYLOAD",null,new { type=envelope.type,payload=raw.Length>12000?raw.Substring(0,12000)+"...[truncated]":raw });
            if(envelope.type=="AuthoringProposal") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_PROPOSAL",null,new { action_id=envelope.action_id });
            else if(envelope.type=="AuthoringExecutionRequest") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_EXECUTION_REQUEST",null,new { action_id=envelope.action_id });
            else if(envelope.type=="AuthoringUndoRequest") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_UNDO_REQUEST",null,new { action_id=envelope.action_id });
            else if(envelope.type=="PredefinedCommandProposal") { DreamCodeVR2ClientLogger.Event("protocol","PREDEFINED_COMMAND_PROPOSAL",null,new { command_id=envelope.command_id,target_object_id=envelope.command?.targetObjectId,command=envelope.command?.command,preset=envelope.command?.preset,secondary_object_id=envelope.command?.secondaryObjectId,interpretation=envelope.interpretation }); DreamCodeVR2ClientLogger.Event("c1","PREDEFINED_COMMAND_PROPOSED",null,new { command_id=envelope.command_id }); }
            else if(envelope.type=="PredefinedCommandExecutionRequest") DreamCodeVR2ClientLogger.Event("protocol","PREDEFINED_COMMAND_EXECUTION_REQUEST",null,new { command_id=envelope.command_id,target_object_id=envelope.command?.targetObjectId,command=envelope.command?.command,preset=envelope.command?.preset,secondary_object_id=envelope.command?.secondaryObjectId });
            else if(envelope.type=="AuthoringStatus") DreamCodeVR2ClientLogger.Event("protocol","AUTHORING_STATUS",null,new { action_id=envelope.action_id });
            if(envelope.type=="PredefinedCommandProposal"||envelope.type=="PredefinedCommandRejected"||envelope.type=="AuthoringProposal"||envelope.type=="AuthoringRejected"||envelope.type=="AuthoringStatus") FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.ResolveProcessingForServerResponse(envelope.type);
            if(envelope.type=="AuthoringProposal"&&envelope.action!=null) ReceiveProposal(new AuthoringProposal{action=envelope.action,actionId=envelope.action_id,interpretation=envelope.interpretation,expectedEffect=envelope.expected_effect,targetDisplayName=envelope.target_object_id});
            else if(envelope.type=="AuthoringExecutionRequest"&&envelope.action!=null) Execute(new AuthoringExecutionRequest{action=envelope.action});
            else if(envelope.type=="AuthoringUndoRequest") Undo(new AuthoringUndoRequest{actionId=envelope.action_id});
            else if(envelope.type=="PredefinedCommandProposal"&&envelope.command!=null) proposalPresenter?.ShowC1PredefinedProposal(envelope.command,OriginalUtterance(envelope),envelope.command_id);
            else if(envelope.type=="PredefinedCommandExecutionRequest"&&envelope.command!=null) ExecutePredefined(envelope.command);
            else if(envelope.type=="QuestConsequenceInstruction") consequenceDispatcher?.Receive(envelope.consequence??new QuestConsequenceInstruction{protocolVersion=envelope.protocolVersion,instructionId=envelope.instructionId,sessionId=envelope.sessionId,canonicalSetId=envelope.canonicalSetId,sourceTaskId=envelope.sourceTaskId,instructionType=envelope.instructionType,targetObjectId=envelope.target_object_id,containerId=envelope.containerId,lockId=envelope.lockId,payload=envelope.payload});
            else if(envelope.type=="QuestResetRequest") consequenceDispatcher?.ReceiveReset(envelope.resetRequest??new QuestResetRequest{protocolVersion=envelope.protocolVersion,resetRequestId=envelope.resetRequestId,sessionId=envelope.sessionId,canonicalSetId=envelope.canonicalSetId},envelope.quest_instance);
            else if(envelope.type=="NextTaskGenerated") HandleNextTaskGenerated(envelope);
            else if(envelope.type=="NextTaskActivationRequest") HandleNextTaskActivation(envelope.task_id);
            else if(envelope.type=="PredefinedCommandRejected") { if(ShouldSuppressPredefinedFailureFeedback(envelope.command_id,"server_rejection")) return; DreamCodeVR2ClientLogger.Warn("c1","PREDEFINED_COMMAND_REJECTED_BY_SERVER",envelope.reasonCode??envelope.reason,new { command_id=envelope.command_id,interpretation=envelope.interpretation,target_object_id=envelope.target_object_id,resolution_stage=envelope.resolutionStage });proposalPresenter?.DismissRejectedPredefinedProposal(envelope.command_id);proposalPresenter?.ShowServerFeedback(envelope.participantMessage,envelope.reasonCode??envelope.reason,"server_rejection",envelope.command_id,envelope.command_id); telemetry?.Log("server_status",null,envelope.command_id,false); }
            else if(envelope.type=="PredefinedCommandAck"&&string.Equals(envelope.status,"failed",StringComparison.OrdinalIgnoreCase)) { if(ShouldSuppressPredefinedFailureFeedback(envelope.command_id,"server_execution_feedback")) return; proposalPresenter?.ShowServerFeedback(envelope.participantMessage,envelope.reasonCode??envelope.reason??envelope.detail,"server_execution_feedback",envelope.command_id,envelope.command_id); }
            else if(envelope.type=="PredefinedCommandAck") telemetry?.Log("server_status",null,envelope.command_id,true);
            else if(envelope.type=="AuthoringRejected"){proposalPresenter?.ShowServerFeedback(envelope.participantMessage,envelope.reasonCode??envelope.reason,"authoring_rejection",envelope.action_id??envelope.command_id,envelope.command_id);telemetry?.Log("server_status",null,envelope.action_id??envelope.command_id,false);}
            else if(envelope.type=="AuthoringStatus") telemetry?.Log("server_status",null,envelope.action_id??envelope.command_id,false);
            else SendAuthoringAck(envelope.action_id,"failed","unsupported_message");
        }
        private static string OriginalUtterance(AuthoringEnvelope envelope)
        {
            if(!string.IsNullOrWhiteSpace(envelope?.originalUtterance))return envelope.originalUtterance;
            if(!string.IsNullOrWhiteSpace(envelope?.recognizedUtterance))return envelope.recognizedUtterance;
            if(!string.IsNullOrWhiteSpace(envelope?.utterance))return envelope.utterance;
            if(!string.IsNullOrWhiteSpace(envelope?.transcript))return envelope.transcript;
            if(!string.IsNullOrWhiteSpace(envelope?.command?.originalUtterance))return envelope.command.originalUtterance;
            if(!string.IsNullOrWhiteSpace(envelope?.command?.recognizedUtterance))return envelope.command.recognizedUtterance;
            if(!string.IsNullOrWhiteSpace(envelope?.command?.utterance))return envelope.command.utterance;
            return envelope?.command?.transcript;
        }
        private void ReceiveProposal(AuthoringProposal p)
        { if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(p.actionId,"failed","authoring_unavailable");return;} DreamCodeVR2ClientLogger.Event("authoring", "AUTHORING_PROPOSED", null, new { action_id=p.actionId, target_object_id=p.action?.targetObjectId });DreamCodeVR2ClientLogger.Event("authoring", conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_PROPOSAL_DISPLAYED":"C3_PROPOSAL_DISPLAYED", null, new { action_id=p.actionId, target_object_id=p.action?.targetObjectId }); proposalPresenter?.Show(p); telemetry?.Log("proposal_received",p.action?.targetObjectId,p.actionId,true); }
        public void Confirm(AuthoringProposal p){if(p==null)return; DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_CONFIRMED",null,new { action_id=p.actionId });telemetry?.Log("proposal_confirmed",p.action?.targetObjectId,p.actionId,true);}
        public void Reject(AuthoringProposal p){DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_REJECTED",null,new { action_id=p?.actionId });telemetry?.Log("proposal_rejected",p?.action?.targetObjectId,p?.actionId,true);}
        public void Modify(AuthoringProposal p){telemetry?.Log("proposal_modify_selected",p?.action?.targetObjectId,p?.actionId,true);}
        private void Execute(AuthoringExecutionRequest request){if(conditionManager==null||!conditionManager.IsAuthoringAvailable){SendAuthoringAck(request.action?.actionId,"failed","authoring_unavailable","authoring_not_available");return;}var conditionEvent=conditionManager.condition==ExperimentCondition.PlayerAuthoring?"C2_EXECUTION_REQUEST_RECEIVED":"C3_EXECUTION_REQUEST_RECEIVED";DreamCodeVR2ClientLogger.Event("authoring",conditionEvent,null,new { action_id=request.action?.actionId });DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_EXECUTION_START",null,new { action_id=request.action?.actionId, operation=request.action?.operation, target_object_id=request.action?.targetObjectId });var op=(request.action?.operation??string.Empty).ToLowerInvariant();var operational=op=="activate"||op=="deactivate"||op=="toggle"||op=="open"||op=="close"||op=="use_with";if(!operational)MapOperation(request.action);var result=operational?(FindFirstObjectByType<DreamCodeVR2.Quest.QuestOperationalInteractionExecutor>()??gameObject.AddComponent<DreamCodeVR2.Quest.QuestOperationalInteractionExecutor>()).Execute(request.action):executor.Execute(request.action);DreamCodeVR2ClientLogger.Event("authoring",result.success?"AUTHORING_EXECUTION_APPLIED":"AUTHORING_EXECUTION_FAILED",result.message,new { action_id=result.actionId,error_code=result.error?.code });SendAuthoringAck(result.actionId,result.success?"applied":"failed",result.message,result.error?.code);sceneContext?.SendSceneContextSnapshot("authoring execution");}
        private void Undo(AuthoringUndoRequest request){DreamCodeVR2ClientLogger.Event("authoring","AUTHORING_UNDO_START",null,new { action_id=request.actionId });var result=undoManager.UndoLast();DreamCodeVR2ClientLogger.Event("authoring",result.success?"AUTHORING_UNDO_APPLIED":"AUTHORING_UNDO_FAILED",result.message,new { action_id=result.actionId });SendAuthoringAck(result.actionId,result.success?"undone":"failed",result.message);sceneContext?.SendSceneContextSnapshot("authoring undo");}
        private void ExecutePredefined(PredefinedVoiceCommand command){if(conditionManager==null||conditionManager.condition!=ExperimentCondition.VoiceCommandBaseline){proposalPresenter?.ShowC1Failure("predefined_command_not_available","local_gate",command?.commandId);SendPredefinedAck(command?.commandId,"failed","predefined_command_not_available","predefined_command_not_available");return;}if(IsDuplicatePredefinedExecution(command?.commandId)){DreamCodeVR2ClientLogger.Warn("c1","PREDEFINED_COMMAND_DUPLICATE_EXECUTION_IGNORED","Duplicate predefined execution request ignored after terminal local success.",new { command_id=command?.commandId,target_object_id=command?.targetObjectId,command=command?.command,preset=command?.preset });return;}DreamCodeVR2ClientLogger.Event("c1","PREDEFINED_COMMAND_CONFIRMED",null,new { command_id=command?.commandId });var result=predefinedCommandExecutor.Execute(command);DreamCodeVR2ClientLogger.Event("c1",result.success?"PREDEFINED_COMMAND_EXECUTED":"PREDEFINED_COMMAND_FAILED",result.message,new { command_id=command?.commandId,target_object_id=command?.targetObjectId,command=command?.command,preset=command?.preset,secondary_object_id=command?.secondaryObjectId,error_code=result.error?.code });if(result.success&&!string.IsNullOrWhiteSpace(command?.commandId))terminalSuccessfulPredefinedCommandIds.Add(command.commandId);var drawer=AuthoringActionExecutor.FindEditable(command?.targetObjectId)?.GetComponent<ExperimentalDrawerController>();proposalPresenter?.ShowC1ExecutionFeedback(result,drawer,command?.commandId);SendPredefinedAck(command.commandId,result.success?"applied":"failed",result.message,result.error?.code);}
        private bool IsDuplicatePredefinedExecution(string commandId)=>!string.IsNullOrWhiteSpace(commandId)&&terminalSuccessfulPredefinedCommandIds.Contains(commandId);
        private bool ShouldSuppressPredefinedFailureFeedback(string commandId,string source)
        {
            if(!IsDuplicatePredefinedExecution(commandId))return false;
            DreamCodeVR2ClientLogger.Warn("c1","PREDEFINED_COMMAND_STALE_FAILURE_SUPPRESSED","Ignored stale predefined failure after terminal local success.",new { command_id=commandId,source });
            return true;
        }
        private void ActivateNextTask(NextTaskSpec spec){if(conditionManager==null||!conditionManager.IsDynamicStorytelling){DreamCodeVR2ClientLogger.Warn("c3","C3_ACTIVATION_FAILED","C3 is not active.",new { task_id=spec?.taskId });Debug.LogWarning("[C3] activation ignored because C3 is not active.",this);return;}var error="Dynamic story task controller is unavailable.";var success=dynamicStoryTaskController!=null&&dynamicStoryTaskController.ActivateNextTask(spec,out error);DreamCodeVR2ClientLogger.Event("c3",success?"C3_ACTIVATION_SUCCESS":"C3_ACTIVATION_FAILED",success?null:error,new { task_id=spec?.taskId });if(!success)Debug.LogWarning("[C3] activation failed locally: "+error,this);}
        private void HandleNextTaskGenerated(AuthoringEnvelope envelope)
        {
            if(conditionManager?.IsDynamicStorytelling==true)
            {
                if(NextTaskWireConverter.TryConvert(envelope.task,out var task,out var error)){dynamicStoryTaskController?.StoreGeneratedTask(task);DreamCodeVR2ClientLogger.Event("c3","C3_WIRE_CONVERSION_SUCCESS",null,new { task_id=task.taskId });DreamCodeVR2ClientLogger.Event("c3","C3_NEXT_TASK_GENERATED",null,new { task_id=task.taskId });}
                else {DreamCodeVR2ClientLogger.Warn("c3","C3_WIRE_CONVERSION_FAILED",error,new { task_id=envelope.task_id });Debug.LogWarning("[C3] rejected generated task: "+error,this);}
                return;
            }
            if(envelope.quest_instance!=null)
            {
                if(FixedQuestWireConverter.TryConvert(envelope.task,envelope.quest_instance,out pendingFixedQuestInstance,out var setupError))
                { pendingFixedTask=pendingFixedQuestInstance.plan.tasks[0];pendingFixedTaskId=pendingFixedTask.taskId;DreamCodeVR2ClientLogger.Event("quest","FIXED_QUEST_WIRE_RECEIVED",null,new { task_id=pendingFixedTaskId,quest_instance_id=pendingFixedQuestInstance.questId }); }
                else DreamCodeVR2ClientLogger.Warn("quest","FIXED_QUEST_WIRE_CONVERSION_FAILED",setupError,new { task_id=envelope.task_id });
            }
            else if(FixedQuestWireConverter.TryConvertTask(envelope.task,out pendingFixedTask,out var taskError))
            { pendingFixedQuestInstance=null;pendingFixedTaskId=pendingFixedTask.taskId;DreamCodeVR2ClientLogger.Event("quest","FIXED_QUEST_TASK_RECEIVED",null,new { task_id=pendingFixedTaskId }); }
            else DreamCodeVR2ClientLogger.Warn("quest","FIXED_QUEST_WIRE_CONVERSION_FAILED",taskError,new { task_id=envelope.task_id });
        }
        private void HandleNextTaskActivation(string taskId)
        {
            var generatedSeen=!string.IsNullOrWhiteSpace(taskId)&&generatedTaskIdsThisSession.Contains(taskId);
            if(conditionManager?.IsDynamicStorytelling==true){LogActivationCorrelation(taskId,generatedSeen,false);DreamCodeVR2ClientLogger.Event("c3","C3_TASK_ACTIVATION_REQUEST",null,new { task_id=taskId });ActivateNextTask(dynamicStoryTaskController?.GetGeneratedTask(taskId));return;}
            if(questInstanceController==null){LogActivationCorrelation(taskId,generatedSeen,false);DreamCodeVR2ClientLogger.Warn("quest","FIXED_QUEST_ACTIVATION_FAILED","Quest instance controller is unavailable.",new { task_id=taskId });return;}
            if(pendingFixedTask==null)
            {
                if(FixedQuestActivationFallback.TryCreate(taskId,conditionManager?.condition??ExperimentCondition.VoiceCommandBaseline,out var fallback))
                {LogActivationCorrelation(taskId,generatedSeen,true);questInstanceController.Apply(fallback);DreamCodeVR2ClientLogger.Event("quest","FIXED_QUEST_ACTIVATED_FALLBACK",null,new { task_id=taskId,quest_instance_id=fallback.questId });return;}
                LogActivationCorrelation(taskId,generatedSeen,false);
                DreamCodeVR2ClientLogger.Warn("quest","FIXED_QUEST_ACTIVATION_FAILED","No matching fixed quest task is pending.",new { task_id=taskId });return;
            }
            if(!string.IsNullOrWhiteSpace(taskId)&&taskId!=pendingFixedTaskId){LogActivationCorrelation(taskId,generatedSeen,false);DreamCodeVR2ClientLogger.Warn("quest","FIXED_QUEST_ACTIVATION_FAILED","Pending fixed task ID does not match activation request.",new { task_id=taskId,pending_task_id=pendingFixedTaskId });return;}
            LogActivationCorrelation(taskId,generatedSeen,false);
            if(pendingFixedQuestInstance!=null){questInstanceController.Apply(pendingFixedQuestInstance);questInstanceController.runtimeState?.SetAwaitingServerTask(true);}else questInstanceController.ActivateServerTask(pendingFixedTask);
            DreamCodeVR2ClientLogger.Event("quest","FIXED_QUEST_ACTIVATED",null,new { task_id=taskId,quest_instance_id=questInstanceController.ActiveInstance?.questId });pendingFixedQuestInstance=null;pendingFixedTask=null;pendingFixedTaskId=null;
        }
        private void LogActivationCorrelation(string taskId,bool generatedSeen,bool fallbackUsed)=>DreamCodeVR2ClientLogger.Event("protocol","NID101_ACTIVATION_CORRELATION",null,new { task_id=taskId,generated_seen=generatedSeen,fallback_used=fallbackUsed });
        private static string TruncateDiagnosticJson(string json)=>string.IsNullOrEmpty(json)||json.Length<=20000?json:json.Substring(0,20000)+"...[truncated]";
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
        public void SendQuestWorldStateEvent(object worldEvent){var flat=JObject.FromObject(worldEvent??new {});flat["type"]="QuestWorldStateEvent";DreamCodeVR2ClientLogger.Event("quest","QUEST_WORLD_STATE_WIRE_PAYLOAD",null,new { type=(string)flat["type"],protocol_version=(int?)flat["protocol_version"],event_type=(string)flat["event_type"],event_id=(string)flat["event_id"],session_id=(string)flat["session_id"],canonical_set_id=(string)flat["canonical_set_id"],reset_request_id=(string)flat["reset_request_id"],semantic_state_type=flat["semantic_state"]?.Type.ToString(),semantic_state_keys=flat["semantic_state"] is JObject state?state.Properties().Select(x=>x.Name).ToArray():null });SendFlat(flat);}
        public void SendQuestConsequenceAck(string instructionId,string sessionId,string canonicalSetId,string sourceTaskId,bool success,string reasonCode,string semanticState){SendFlat(new { type="QuestConsequenceAck", protocol_version=1, instruction_id=instructionId,session_id=sessionId,canonical_set_id=canonicalSetId,source_task_id=sourceTaskId,success,reason_code=reasonCode,semantic_state=semanticState });DreamCodeVR2ClientLogger.Event("quest","QUEST_CONSEQUENCE_ACK_SENT",null,new { instruction_id=instructionId,session_id=sessionId,canonical_set_id=canonicalSetId,success,reason_code=reasonCode });}
        private void SendAuthoringAck(string actionId,string status,string detail,string reasonCode=null){SendFlat(new { type="AuthoringAck", action_id=actionId, status, detail, reason_code=reasonCode });}
        private void SendPredefinedAck(string commandId,string status,string detail,string reasonCode=null){SendFlat(new { type="PredefinedCommandAck", command_id=commandId, status, detail, reason_code=reasonCode });}
        private void SendFlat(object dto){Resolve();if(!outgoingRegistered||string.IsNullOrEmpty(Peer())){DreamCodeVR2ClientLogger.Event("protocol","NID102_SEND_DEFERRED",null,new { reason=!outgoingRegistered?"outgoing_context_not_registered":"peer_unavailable",type=JObject.FromObject(dto??new {})["type"]?.ToString() });return;}var json=JsonConvert.SerializeObject(dto);var payload=Encoding.UTF8.GetBytes(json);var peer=Encoding.UTF8.GetBytes(Peer());var message=ReferenceCountedSceneGraphMessage.Rent(peer.Length+payload.Length);peer.CopyTo(new Span<byte>(message.bytes,message.start,peer.Length));payload.CopyTo(new Span<byte>(message.bytes,message.start+peer.Length,payload.Length));outgoing.Send(message);var metadata=JObject.Parse(json);DreamCodeVR2ClientLogger.Event("protocol","NID102_SENT",null,new { type=(string)metadata["type"], action_id=(string)metadata["action_id"], command_id=(string)metadata["command_id"], task_id=(string)metadata["task_id"], status=(string)metadata["status"], @event=(string)metadata["event"], payload_bytes=payload.Length });}
        private void MapOperation(AuthoringAction a){switch(a.operation){case "set_property":a.kind=AuthoringActionKind.SET_PROPERTY;break;case "set_affordance":a.kind=AuthoringActionKind.SET_AFFORDANCE;break;case "create_object":a.kind=AuthoringActionKind.CREATE_OBJECT;break;case "relocate_object":a.kind=AuthoringActionKind.RELOCATE_OBJECT;break;case "toggle_state":a.kind=AuthoringActionKind.TOGGLE_STATE;break;case "add_behavior":a.kind=AuthoringActionKind.ADD_BEHAVIOR;break;case "link_objects":a.kind=AuthoringActionKind.LINK_OBJECTS;break;}}
        private static bool IsCanonicalExperimentEvent(string value)=>value=="task_started"||value=="task_completed"||value=="incorrect_attempt"||value=="hint_requested"||value=="session_completed";
        private string Peer()=>roomClient!=null&&roomClient.Me!=null?roomClient.Me.uuid:null;
        public string CurrentPeerUuid { get { Resolve(); return Peer(); } }
    }
}
