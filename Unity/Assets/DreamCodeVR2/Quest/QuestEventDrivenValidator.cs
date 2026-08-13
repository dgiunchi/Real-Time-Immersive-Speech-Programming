using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class QuestEventDrivenValidator : MonoBehaviour
    {
        public QuestRuntimeState runtimeState; public QuestEventBus eventBus;
        private void OnEnable(){Resolve();if(eventBus)eventBus.Published+=OnEvent;}
        private void OnDisable(){if(eventBus)eventBus.Published-=OnEvent;}
        private void Resolve(){if(!runtimeState)runtimeState=FindFirstObjectByType<QuestRuntimeState>();if(!eventBus)eventBus=QuestEventBus.Instance?QuestEventBus.Instance:FindFirstObjectByType<QuestEventBus>();}
        private void OnEvent(QuestEvent evt)
        {
            var task=runtimeState?.GetCurrentTask(); if(task==null) return;
            var matchesTarget=string.IsNullOrEmpty(task.target)||task.target==evt.objectId;
            var complete=(task.type=="ReadClue"&&evt.type==QuestEventType.ObjectPickedUp&&matchesTarget)
                ||(task.type=="RetrieveKey"&&evt.type==QuestEventType.ObjectPickedUp&&matchesTarget)
                ||(task.type=="UseKeyWithLock"&&evt.type==QuestEventType.LockOpened&&matchesTarget)
                ||(task.type=="CreateTextureAndPlaceObject"&&evt.type==QuestEventType.ObjectPlacedInZone&&matchesTarget)
                ||(task.type=="UnlockDoorWithKey"&&evt.type==QuestEventType.LockOpened&&matchesTarget)
                ||(task.type=="StraightenAndMovePainting"&&evt.type==QuestEventType.ObjectPlacedInZone&&matchesTarget);
            if(!complete) return; runtimeState.MarkCurrentTaskCompleted("Validated from " + evt.type); runtimeState.AdvanceToNextTask();
        }
    }
}
