using DreamCodeVR2.ExperimentalAuthoring;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class DynamicStoryTaskController : MonoBehaviour
    {
        public QuestRuntimeState runtimeState; public RuntimeTaskValidator validator; public SceneContextTransmitter sceneContext; public AuthoringProtocolClient protocol;
        public DreamCodeVRAuthoringUIController ui;
        public bool WaitingForNextTask { get; private set; } public NextTaskSpec ActiveDynamicTask { get; private set; }
        private NextTaskSpec generatedTask;
        private string completedTaskId;
        public void StoreGeneratedTask(NextTaskSpec task){generatedTask=task;if(ActiveDynamicTask==null)WaitingForNextTask=true;}
        public NextTaskSpec GetGeneratedTask(string taskId)=>generatedTask!=null&&generatedTask.taskId==taskId?generatedTask:null;
        public QuestEventBus eventBus;
        private void OnEnable(){Resolve();if(runtimeState)runtimeState.TaskCompleted+=OnTaskCompleted;if(eventBus)eventBus.Published+=OnQuestEvent;}
        private void OnDisable(){if(runtimeState)runtimeState.TaskCompleted-=OnTaskCompleted;if(eventBus)eventBus.Published-=OnQuestEvent;}
        private void Resolve(){if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();if(!validator)validator=FindFirstObjectByType<RuntimeTaskValidator>();if(!sceneContext)sceneContext=FindFirstObjectByType<SceneContextTransmitter>();if(!protocol)protocol=FindFirstObjectByType<AuthoringProtocolClient>();if(!ui)ui=FindFirstObjectByType<DreamCodeVRAuthoringUIController>();if(!eventBus)eventBus=QuestEventBus.Instance?QuestEventBus.Instance:FindFirstObjectByType<QuestEventBus>();}
        private void OnTaskCompleted(QuestTaskSpec task)
        {
            var manager=FindFirstObjectByType<ExperimentConditionManager>();if(manager==null||!manager.IsDynamicStorytelling||ActiveDynamicTask==null)return;
            var taskId=ActiveDynamicTask.taskId;if(string.IsNullOrWhiteSpace(taskId)||taskId==completedTaskId)return;
            completedTaskId=taskId;DreamCodeVR2ClientLogger.Event("c3","TASK_COMPLETED",null,new { task_id=taskId });WaitingForNextTask=true;ui?.SetStatus("Preparing the next objective...");sceneContext?.SendSceneContextSnapshot("c3_task_completion");protocol?.SendTaskCompleted(taskId);
        }
        public bool ActivateNextTask(NextTaskSpec spec,out string error){Resolve();DreamCodeVR2ClientLogger.Event("c3","C3_ACTIVATION_REQUEST",null,new { task_id=spec?.taskId });if(!WaitingForNextTask){error="No next task is pending.";return false;}if(!validator){error="Runtime task validator is unavailable.";return false;}if(!validator.ValidateNextTask(spec,out error))return false;FindFirstObjectByType<QuestObjectVisibilityController>()?.ApplyDynamicCandidatePool(spec.candidateObjectIds);ActiveDynamicTask=spec;completedTaskId=null;WaitingForNextTask=false;runtimeState?.ActivateDynamicTask(new QuestTaskSpec{step=1,type="DynamicRuntimeTask",target=spec.requiredObjects!=null&&spec.requiredObjects.Length>0?spec.requiredObjects[0]:null,description=spec.playerInstruction,protectedDuringTask=spec.protectedObjects,allowedAuthoringOperations=spec.allowedAuthoringScope?.GetAllowedOperations()});ui?.SetStatus(spec.playerInstruction);protocol?.SendNextTaskAck(spec.taskId);DreamCodeVR2ClientLogger.Event("c3","TASK_ACTIVATED",null,new { task_id=spec.taskId });TryCompleteActiveTask();return true;}
        private void OnQuestEvent(QuestEvent evt){TryCompleteActiveTask();}
        private void TryCompleteActiveTask(){if(ActiveDynamicTask==null||WaitingForNextTask||runtimeState?.GetCurrentTask()?.type!="DynamicRuntimeTask")return;foreach(var condition in ActiveDynamicTask.successConditions??System.Array.Empty<RuntimeSuccessCondition>())if(!validator.IsSatisfied(condition))return;DreamCodeVR2ClientLogger.Event("c3","TASK_SUCCESS_CONDITION_CHANGED",null,new { task_id=ActiveDynamicTask.taskId });runtimeState.MarkCurrentTaskCompleted("Dynamic task conditions satisfied");ActiveDynamicTask=null;}
        public void ResetDynamicState(){WaitingForNextTask=false;ActiveDynamicTask=null;generatedTask=null;completedTaskId=null;}
    }
}
