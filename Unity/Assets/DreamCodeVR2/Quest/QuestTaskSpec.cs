using System;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestTaskSpec
    {
        public int step;
        public string type;
        public string target;
        public string key;
        public string @lock;
        public string object_to_create;
        public string primitive;
        public string material;
        public string target_anchor;
        public bool requires_planning;
        public bool has_error_risk;
        public string description;
        public bool questCritical;
        public string[] protectedDuringTask;
        public string[] protectedProperties;
        public string[] forbiddenAffordanceChanges;
        public string successState;
        public bool directCompletionForbidden = true;
        public string[] allowedAuthoringOperations;
        // Fixed C1/C2 plans can use the same actual-world predicates as generated C3 tasks.
        public DreamCodeVR2.ExperimentalAuthoring.RuntimeSuccessCondition[] successConditions;
    }
}
