using UnityEngine;
using DreamCodeVR2.ExperimentalAuthoring;

namespace DreamCodeVR2.Quest
{
    public class QuestEventDrivenValidator : MonoBehaviour
    {
        public QuestRuntimeState runtimeState; public QuestEventBus eventBus; public RuntimeTaskValidator runtimeValidator;
        private void OnEnable(){Resolve();if(eventBus)eventBus.Published+=OnEvent;}
        private void OnDisable(){if(eventBus)eventBus.Published-=OnEvent;}
        private void Resolve(){if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();if(!eventBus)eventBus=QuestEventBus.Instance?QuestEventBus.Instance:FindFirstObjectByType<QuestEventBus>();if(!runtimeValidator)runtimeValidator=FindFirstObjectByType<RuntimeTaskValidator>();}
        private void OnEvent(QuestEvent evt)
        {
            var task=runtimeState?.GetCurrentTask(); if(task==null) return;
            if(task.successConditions!=null&&task.successConditions.Length>0)
            {
                if(runtimeValidator==null)return;
                RuntimeSuccessCondition triggeringCondition=null;
                foreach(var condition in task.successConditions)
                {
                    var result=runtimeValidator.IsSatisfied(condition,task.taskId);
                    var lockController=condition?.type=="LOCK_UNLOCKED"?AuthoringActionExecutor.FindEditable(condition.object_id)?.GetComponent<QuestLockController>():null;
                    DreamCodeVR2.ExperimentalAuthoring.DreamCodeVR2ClientLogger.Event("quest","TASK_SUCCESS_EVALUATION",null,new { task_id=task.taskId,condition=condition?.type,current_value=result,result,triggering_event_type=evt.type.ToString(),triggering_event_object_id=evt.objectId,lock_id=condition?.object_id,lock_is_unlocked=lockController?.IsUnlocked,lock_controller_instance_id=lockController?lockController.GetInstanceID():0 });
                    if(!result)return;
                    if(triggeringCondition==null)triggeringCondition=condition;
                }
                CompleteFixedOrLocalTask(task,"Validated from actual world state",triggeringCondition?.type);return;
            }
            var matchesTarget=string.IsNullOrEmpty(task.target)||task.target==evt.objectId;
            var complete=(task.type=="ReadClue"&&evt.type==QuestEventType.ObjectPickedUp&&matchesTarget)
                ||(task.type=="RetrieveKey"&&evt.type==QuestEventType.ObjectPickedUp&&matchesTarget)
                ||(task.type=="UseKeyWithLock"&&evt.type==QuestEventType.LockOpened&&matchesTarget)
                ||(task.type=="CreateTextureAndPlaceObject"&&evt.type==QuestEventType.ObjectPlacedInZone&&matchesTarget)
                ||(task.type=="UnlockDoorWithKey"&&evt.type==QuestEventType.LockOpened&&matchesTarget)
                ||(task.type=="StraightenAndMovePainting"&&evt.type==QuestEventType.ObjectStateChanged&&matchesTarget&&evt.detail=="aligned")
                ||(task.type=="OpenDoor"&&evt.type==QuestEventType.ObjectStateChanged&&matchesTarget&&evt.detail=="open")
                ||(task.type=="OpenDrawer"&&evt.type==QuestEventType.ObjectStateChanged&&matchesTarget&&evt.detail=="open")
                ||(task.type=="SetLampState"&&evt.type==QuestEventType.ObjectStateChanged&&matchesTarget&&evt.detail==task.successState);
            if(!complete) return; CompleteFixedOrLocalTask(task,"Validated from " + evt.type,evt.type.ToString());
        }
        private void CompleteFixedOrLocalTask(QuestTaskSpec task,string reason,string triggeringCondition)
        {
            if(!runtimeState.MarkCurrentTaskCompleted(reason))return;
            DreamCodeVR2.ExperimentalAuthoring.DreamCodeVR2ClientLogger.Event("quest","TASK_COMPLETED",null,new { task_id=task.taskId,triggering_condition=triggeringCondition,completion_source="QuestEventDrivenValidator.RuntimeTaskValidator.IsSatisfied" });
            var manager=FindFirstObjectByType<DreamCodeVR2.ExperimentalAuthoring.ExperimentConditionManager>();
            if(manager?.IsDynamicStorytelling!=true&&!string.IsNullOrWhiteSpace(task?.taskId))
                FindFirstObjectByType<DreamCodeVR2.ExperimentalAuthoring.AuthoringProtocolClient>()?.SendTaskCompleted(task.taskId);
            runtimeState.AdvanceToNextTask();
        }
    }
}
