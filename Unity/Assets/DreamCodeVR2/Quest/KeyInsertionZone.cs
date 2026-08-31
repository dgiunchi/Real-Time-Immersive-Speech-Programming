using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    [RequireComponent(typeof(Collider))]
    public sealed class KeyInsertionZone : MonoBehaviour
    {
        public QuestLockController lockController;
        private readonly HashSet<ExperimentalGrabbableAdapter> inside=new HashSet<ExperimentalGrabbableAdapter>();
        private void Awake(){var collider=GetComponent<Collider>();collider.isTrigger=true;}
        private void OnEnable(){ExperimentalGrabbableAdapter.Released+=OnReleased;}
        private void OnDisable(){ExperimentalGrabbableAdapter.Released-=OnReleased;inside.Clear();}
        private void OnTriggerEnter(Collider other){var key=other?other.GetComponentInParent<ExperimentalGrabbableAdapter>():null;if(!key)return;inside.Add(key);Trace("KEY_INSERT_ZONE_ENTER",key,null,false);}
        private void OnTriggerExit(Collider other){var key=other?other.GetComponentInParent<ExperimentalGrabbableAdapter>():null;if(!key)return;inside.Remove(key);Trace("KEY_INSERT_ZONE_EXIT",key,null,false);}
        private void OnReleased(ExperimentalGrabbableAdapter key,string keyId,bool wasHeld)
        {
            if(!key||!inside.Contains(key)||!wasHeld||!key.grabbable)return;
            var manager=FindFirstObjectByType<ExperimentConditionManager>();
            if(manager&&manager.condition==ExperimentCondition.VoiceCommandBaseline)return;
            Trace("KEY_PHYSICAL_RELEASE_ATTEMPT",key,keyId,true);
            string error=null;
            if(lockController&&lockController.TryUseKey(keyId,out error))Trace("KEY_PHYSICAL_INSERT_SUCCESS",key,keyId,true);
            else Trace("KEY_PHYSICAL_INSERT_REJECTED",key,keyId,true,error);
        }
        private void Trace(string eventName,ExperimentalGrabbableAdapter key,string keyId,bool wasGrabbed,string error=null)
        {
            var lockItem=lockController?lockController.GetComponent<AIEditableObject>():null;var keyItem=key?key.GetComponent<AIEditableObject>():null;
            DreamCodeVR2ClientLogger.Event("quest",eventName,error,new { condition=FindFirstObjectByType<ExperimentConditionManager>()?.condition.ToString(),key_id=keyId??keyItem?.objectId,lock_id=lockItem?.objectId,required_key_id=lockController?.requiredKeyId,was_grabbable=key?.grabbable,was_grabbed=wasGrabbed,inside_zone=key!=null&&inside.Contains(key),binding_match=lockController!=null&&keyItem!=null&&lockController.requiredKeyId==keyItem.objectId });
        }
    }
}
