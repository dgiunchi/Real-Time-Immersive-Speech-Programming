using System;

namespace DreamCodeVR2.ContextBridge
{
    [Serializable]
    public class InteractionContextSnapshot
    {
        public int schema_version = 1;
        public string type = "InteractionContextUpdate";
        public string peer;
        public long timestamp_unix_ms;
        public int scene_version;
        public ObjectSummary active_selection;
        public ObjectSummary pointed_object;
        public SerializableVector3 pointed_world_position;
        public object last_action;
        public object pending_confirmation;
        public string current_task_id;
        public string[] recently_interacted_object_ids;
        public bool object_currently_held;
        public string last_incorrect_attempt;
        public int hint_count;
    }
}
