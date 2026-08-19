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
        public void StoreGeneratedTask(NextTaskSpec task){generatedTask=task;}
        public NextTaskSpec GetGeneratedTask(string taskId)=>generatedTask!=null&&generatedTask.taskId==taskId?generatedTask:null;
        public QuestEventBus eventBus;
        private void OnEnable(){Resolve();if(runtimeState)runtimeState.TaskCompleted+=OnTaskCompleted;if(eventBus)eventBus.Published+=OnQuestEvent;}
        private void OnDisable(){if(runtimeState)runtimeState.TaskCompleted-=OnTaskCompleted;if(eventBus)eventBus.Published-=OnQuestEvent;}
        private void Resolve(){if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();if(!validator)validator=FindFirstObjectByType<RuntimeTaskValidator>();if(!sceneContext)sceneContext=FindFirstObjectByType<SceneContextTransmitter>();if(!protocol)protocol=FindFirstObjectByType<AuthoringProtocolClient>();if(!ui)ui=FindFirstObjectByType<DreamCodeVRAuthoringUIController>();if(!eventBus)eventBus=QuestEventBus.Instance?QuestEventBus.Instance:FindFirstObjectByType<QuestEventBus>();}
        private void OnTaskCompleted(QuestTaskSpec task){var manager=FindFirstObjectByType<ExperimentConditionManager>();if(manager==null||!manager.IsDynamicStorytelling)return;WaitingForNextTask=true;ui?.SetStatus("Preparing the next objective...");sceneContext?.SendSceneContextSnapshot("task completed before next task");protocol?.SendTaskCompleted(task.step.ToString());}
        public bool ActivateNextTask(NextTaskSpec spec,out string error){Resolve();if(!WaitingForNextTask){error="No next task is pending.";return false;}if(!validator){error="Runtime task validator is unavailable.";return false;}if(!validator.ValidateNextTask(spec,out error))return false;ActiveDynamicTask=spec;WaitingForNextTask=false;runtimeState?.ActivateDynamicTask(new QuestTaskSpec{step=1,type="DynamicRuntimeTask",target=spec.requiredObjects!=null&&spec.requiredObjects.Length>0?spec.requiredObjects[0]:null,description=spec.playerInstruction,protectedDuringTask=spec.protectedObjects,allowedAuthoringOperations=spec.allowedAuthoringScope});ui?.SetStatus(spec.playerInstruction);protocol?.SendNextTaskAck(spec.taskId,true,"activated");return true;}
        private void OnQuestEvent(QuestEvent evt){if(ActiveDynamicTask==null||WaitingForNextTask||runtimeState?.GetCurrentTask()?.type!="DynamicRuntimeTask")return;foreach(var condition in ActiveDynamicTask.successConditions??System.Array.Empty<RuntimeSuccessCondition>())if(!validator.IsSatisfied(condition))return;runtimeState.MarkCurrentTaskCompleted("Dynamic task conditions satisfied");ActiveDynamicTask=null;}
        public void ResetDynamicState(){WaitingForNextTask=false;ActiveDynamicTask=null;}
    }
}
