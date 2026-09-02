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
        public static event System.Action<ExperimentalGrabbableAdapter,string,bool> Released;
        public bool grabbable; public bool IsHeld { get; private set; }
        public QuestEventBus eventBus; public SceneContextTransmitter sceneContext; private Transform originalParent; private Rigidbody body; private bool originalKinematic;
        private void Awake(){body=GetComponent<Rigidbody>();originalParent=transform.parent;originalKinematic=body&&body.isKinematic;}
        public void SetGrabbable(bool value) { grabbable=value; if(!value) IsHeld=false; if(value)KeyPoseNormalizer.NormalizeVisualOnly(GetComponent<AIEditableObject>(),"grab_prepare"); sceneContext?.SendSceneContextSnapshot("grabbable changed"); }
        public void Grasp(Hand proxy) { if(!grabbable||!proxy)return; KeyPoseNormalizer.NormalizeVisualOnly(GetComponent<AIEditableObject>(),"grab_prepare");IsHeld=true; if(body)body.isKinematic=true; transform.SetParent(proxy.transform,true); var id=GetComponent<AIEditableObject>()?.objectId; eventBus?.Publish(QuestEventType.ObjectPickedUp,id); sceneContext?.SendSceneContextSnapshot("object grabbed"); }
        public void Release(Hand proxy) { if(!IsHeld)return;var wasHeld=IsHeld; IsHeld=false; transform.SetParent(originalParent,true); if(body)body.isKinematic=originalKinematic; var id=GetComponent<AIEditableObject>()?.objectId; eventBus?.Publish(QuestEventType.ObjectDropped,id);Released?.Invoke(this,id,wasHeld);if(!(GetComponent<QuestInsertedKeyState>()?.IsInserted??false))KeyPoseNormalizer.NormalizeVisualOnly(GetComponent<AIEditableObject>(),"release");sceneContext?.SendSceneContextSnapshot("object released"); }
    }
}
