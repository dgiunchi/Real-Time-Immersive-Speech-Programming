using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using Ubiq.XR;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    [RequireComponent(typeof(Rigidbody))]
    public class ExperimentalGrabbableAdapter : MonoBehaviour, IGraspable
    {
        public bool grabbable; public bool IsHeld { get; private set; }
        public QuestEventBus eventBus; public SceneContextTransmitter sceneContext; private Transform originalParent; private Rigidbody body; private bool originalKinematic;
        private void Awake(){body=GetComponent<Rigidbody>();originalParent=transform.parent;originalKinematic=body&&body.isKinematic;}
        public void SetGrabbable(bool value) { grabbable=value; if(!value) IsHeld=false; sceneContext?.SendSceneContextSnapshot("grabbable changed"); }
        public void Grasp(Hand proxy) { if(!grabbable||!proxy)return; IsHeld=true; if(body)body.isKinematic=true; transform.SetParent(proxy.transform,true); var id=GetComponent<AIEditableObject>()?.objectId; eventBus?.Publish(QuestEventType.ObjectPickedUp,id); sceneContext?.SendSceneContextSnapshot("object grabbed"); }
        public void Release(Hand proxy) { if(!IsHeld)return; IsHeld=false; transform.SetParent(originalParent,true); if(body)body.isKinematic=originalKinematic; var id=GetComponent<AIEditableObject>()?.objectId; eventBus?.Publish(QuestEventType.ObjectDropped,id); sceneContext?.SendSceneContextSnapshot("object released"); }
    }
}
