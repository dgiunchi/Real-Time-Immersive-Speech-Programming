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
    }
}
