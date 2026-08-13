using System.Collections;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentalDrawerController : MonoBehaviour
    {
        public Vector3 closedLocalPosition; public Vector3 openLocalPosition = new Vector3(0,0,0.22f); public float duration=.35f;
        public bool IsOpen { get; private set; } public QuestEventBus eventBus; public SceneContextTransmitter sceneContext; private Coroutine motion;
        private void Awake(){closedLocalPosition=transform.localPosition;}
        public void Open(){Move(true);} public void Close(){Move(false);}
        private void Move(bool open){if(motion!=null)StopCoroutine(motion);motion=StartCoroutine(MoveRoutine(open));}
        private IEnumerator MoveRoutine(bool open){var from=transform.localPosition;var to=open?openLocalPosition:closedLocalPosition;var elapsed=0f;while(elapsed<Mathf.Clamp(duration,.05f,3f)){elapsed+=Time.deltaTime;transform.localPosition=Vector3.Lerp(from,to,elapsed/Mathf.Max(.05f,duration));yield return null;}transform.localPosition=to;IsOpen=open;var id=GetComponent<AIEditableObject>()?.objectId;eventBus?.Publish(QuestEventType.ObjectStateChanged,id,null,open?"open":"closed");sceneContext?.SendSceneContextSnapshot("drawer state");}
    }
}
