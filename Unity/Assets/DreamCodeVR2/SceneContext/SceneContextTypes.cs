using System;
using DreamCodeVR2.ContextBridge;

namespace DreamCodeVR2.SceneContext
{
    [Serializable]
    public class SerializableColor
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public SerializableColor()
        {
        }

        public SerializableColor(UnityEngine.Color value)
        {
            r = value.r;
            g = value.g;
            b = value.b;
            a = value.a;
        }

        public static SerializableColor From(UnityEngine.Color value)
        {
            return new SerializableColor(value);
        }
    }

    [Serializable]
    public class SceneMaterialSummary
    {
        public string slot;
        public string material_name;
        public string shader_name;
        public SerializableColor primary_color;
    }

    [Serializable]
    public class SceneComponentSummary
    {
        public string type_name;
        public bool? enabled;
    }

    [Serializable]
    public class SceneObjectSummary
    {
        public string id;
        public string display_name;
        public string unity_name;
        public string[] semantic_types;
        public string[] labels;
        public string description;
        public SerializableVector3 position;
        public SerializableVector3 rotation;
        public SerializableVector3 scale;
        public bool active;
        public bool editable;
        public string parent_id;
        public SceneMaterialSummary[] materials;
        public SceneComponentSummary[] components;
        public string[] available_operations;
        public string[] allowed_editable_properties;
        public string[] allowed_behaviors;
        public bool quest_critical;
        public string semantic_state;
        public bool runtime_created;
        public string[] active_authoring_behaviors;
        public string parent_anchor;
        public bool currently_held;
        public string[] player_authored_affordances;
        public string created_by_action_id;
        public string created_during_task_id;
        public string[] predefined_voice_commands;
        public string[] editable_affordances;
        public bool protected_for_current_task;
    }

    [Serializable]
    public class SceneContextPacket
    {
        public int schema_version = 0;
        public string type = "SceneContextUpdate";
        public string peer;
        public long timestamp_unix_ms;
        public int scene_version;
        public string scene_name;
        public SceneObjectSummary[] objects;
    }
}
