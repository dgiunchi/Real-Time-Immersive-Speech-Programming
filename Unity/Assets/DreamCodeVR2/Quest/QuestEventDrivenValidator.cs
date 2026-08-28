using UnityEngine;

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
                if(runtimeValidator==null)return;foreach(var condition in task.successConditions)if(!runtimeValidator.IsSatisfied(condition))return;
                runtimeState.MarkCurrentTaskCompleted("Validated from actual world state");runtimeState.AdvanceToNextTask();return;
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
            if(!complete) return; runtimeState.MarkCurrentTaskCompleted("Validated from " + evt.type); runtimeState.AdvanceToNextTask();
        }
    }
}
