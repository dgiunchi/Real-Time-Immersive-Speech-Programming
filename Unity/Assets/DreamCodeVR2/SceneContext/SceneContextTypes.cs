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
