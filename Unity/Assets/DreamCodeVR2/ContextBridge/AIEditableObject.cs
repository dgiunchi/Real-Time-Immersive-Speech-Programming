using UnityEngine;

namespace DreamCodeVR2.ContextBridge
{
    public class AIEditableObject : MonoBehaviour
    {
        public string objectId;
        public string displayName;
        public string description;
        public string[] labels;
        public bool editable = true;
        public bool includeRendererBounds = true;

        private void OnEnable()
        {
            var registry = SceneRegistry.Instance;
            if (registry)
            {
                registry.Register(this);
            }
        }

        private void OnDisable()
        {
            var registry = SceneRegistry.Instance;
            if (registry)
            {
                registry.Unregister(this);
            }
        }

        public ObjectSummary ToSummary()
        {
            var summary = new ObjectSummary
            {
                id = string.IsNullOrWhiteSpace(objectId) ? gameObject.name : objectId,
                display_name = string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName,
                unity_name = gameObject.name,
                description = description,
                labels = labels ?? new string[0],
                editable = editable,
                active = isActiveAndEnabled && gameObject.activeInHierarchy,
                position = SerializableVector3.From(transform.position),
                rotation_euler = SerializableVector3.From(transform.rotation.eulerAngles),
                source = "AIEditableObject"
            };

            if (includeRendererBounds && TryGetRendererBounds(out var bounds))
            {
                summary.bounds_center = SerializableVector3.From(bounds.center);
                summary.bounds_size = SerializableVector3.From(bounds.size);
            }

            return summary;
        }

        private bool TryGetRendererBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }
    }
}
