using System;
using DreamCodeVR2.ExperimentalAuthoring;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class RuntimeTaskValidator : MonoBehaviour
    {
        public bool ValidateNextTask(NextTaskSpec spec, out string error)
        {
            error=null; if(spec==null||string.IsNullOrWhiteSpace(spec.taskId)||string.IsNullOrWhiteSpace(spec.playerInstruction)){error="Task specification is incomplete.";return false;}
            foreach(var condition in spec.successConditions??Array.Empty<RuntimeSuccessCondition>())if(!IsAllowed(condition)){error="Task uses a non-allowlisted success condition.";return false;} return true;
        }
        public bool IsSatisfied(RuntimeSuccessCondition condition)
        {
            if(condition==null)return false; var obj=AuthoringActionExecutor.FindEditable(condition.object_id);
            switch(condition.type)
            {
                case "OBJECT_AT_ANCHOR": return obj&&obj.GetComponentInParent<AuthoringAnchor>()&&obj.GetComponentInParent<AuthoringAnchor>().anchorId==condition.anchor_id;
                case "OBJECT_HAS_STATE": return obj&&obj.GetComponent<AuthoringSemanticState>()?.state==condition.value;
                case "OBJECT_HAS_AFFORDANCE": var a=obj?obj.GetComponent<AuthoringAffordanceState>():null;return a&&a.Get(condition.value);
                case "OBJECT_GRABBED": return obj&&obj.GetComponent<ExperimentalGrabbableAdapter>()?.IsHeld==true;
                case "OBJECT_LINK_ACTIVE": return obj&&obj.GetComponent<AuthoringObjectLink>();
                case "OBJECT_BEHAVIOR_ACTIVE": return obj&&obj.GetComponent<AuthoringRuntimeBehavior>()?.IsActive==true;
                case "MULTIPLE_CONDITIONS_ALL": foreach(var child in condition.children??Array.Empty<RuntimeSuccessCondition>())if(!IsSatisfied(child))return false;return true;
                case "MULTIPLE_CONDITIONS_ANY": foreach(var child in condition.children??Array.Empty<RuntimeSuccessCondition>())if(IsSatisfied(child))return true;return false;
                default:return false;
            }
        }
        private static bool IsAllowed(RuntimeSuccessCondition c)
        { if(c==null)return false; switch(c.type){case "OBJECT_AT_ANCHOR":case "OBJECT_HAS_STATE":case "OBJECT_HAS_AFFORDANCE":case "OBJECT_GRABBED":case "OBJECT_USED_WITH":case "OBJECT_LINK_ACTIVE":case "OBJECT_BEHAVIOR_ACTIVE":case "SEQUENCE_COMPLETED":case "MULTIPLE_CONDITIONS_ALL":case "MULTIPLE_CONDITIONS_ANY":return true;default:return false;} }
    }
}
