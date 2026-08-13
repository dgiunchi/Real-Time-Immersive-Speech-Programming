using System;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public enum QuestEventType { ObjectPickedUp, ObjectDropped, ObjectPlacedInZone, ObjectStateChanged, ButtonPressed, LockOpened, ObjectCreated, BehaviorAdded, LinkActivated, HintRequested, IncorrectAttempt, TaskStarted, TaskCompleted }
    [Serializable] public class QuestEvent { public QuestEventType type; public string objectId; public string secondaryObjectId; public string detail; public long timestamp; }
    public class QuestEventBus : MonoBehaviour
    {
        public static QuestEventBus Instance { get; private set; }
        public event Action<QuestEvent> Published;
        private void Awake(){Instance=this;}
        private void OnDestroy(){if(Instance==this)Instance=null;}
        public void Publish(QuestEventType type,string objectId=null,string secondaryObjectId=null,string detail=null) => Published?.Invoke(new QuestEvent{type=type,objectId=objectId,secondaryObjectId=secondaryObjectId,detail=detail,timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
    }
}
