using System;
using UnityEngine;

namespace DreamCodeVR2.ContextBridge
{
    [Serializable]
    public class SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3()
        {
        }

        public SerializableVector3(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public static SerializableVector3 From(Vector3 value)
        {
            return new SerializableVector3(value);
        }
    }

    [Serializable]
    public class ObjectSummary
    {
        public string id;
        public string display_name;
        public string unity_name;
        public string description;
        public string[] labels;
        public bool editable;
        public bool active;
        public SerializableVector3 position;
        public SerializableVector3 rotation_euler;
        public SerializableVector3 bounds_center;
        public SerializableVector3 bounds_size;
        public string source;
    }
}
