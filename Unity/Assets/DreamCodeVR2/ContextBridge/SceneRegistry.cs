using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVR2.ContextBridge
{
    public class SceneRegistry : MonoBehaviour
    {
        public static SceneRegistry Instance { get; private set; }

        public bool autoDiscoverOnStart = true;
        public bool allowFallbackSummaries = true;
        public bool fallbackEditable;
        [SerializeField] private int sceneVersion;

        private readonly Dictionary<AIEditableObject, ObjectSummary> summariesByComponent =
            new Dictionary<AIEditableObject, ObjectSummary>();
        private readonly Dictionary<GameObject, AIEditableObject> componentsByGameObject =
            new Dictionary<GameObject, AIEditableObject>();
        private readonly List<ObjectSummary> summaryCache = new List<ObjectSummary>();

        public int CurrentSceneVersion => sceneVersion;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogWarning("[ContextBridge] multiple SceneRegistry instances found; using the newest instance.", this);
            }

            Instance = this;
        }

        private void Start()
        {
            if (!autoDiscoverOnStart)
            {
                return;
            }

            foreach (var editableObject in FindObjectsOfType<AIEditableObject>(true))
            {
                Register(editableObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Register(AIEditableObject obj)
        {
            if (!obj)
            {
                return;
            }

            summariesByComponent[obj] = obj.ToSummary();
            componentsByGameObject[obj.gameObject] = obj;

            if (HasDuplicateObjectId(obj))
            {
                Debug.LogWarning($"[ContextBridge] duplicate AIEditableObject objectId={obj.objectId}", obj);
            }
        }

        public void Unregister(AIEditableObject obj)
        {
            if (!obj)
            {
                return;
            }

            summariesByComponent.Remove(obj);
            componentsByGameObject.Remove(obj.gameObject);
        }

        public bool TryGetSummary(GameObject obj, out ObjectSummary summary)
        {
            summary = null;
            if (!obj)
            {
                return false;
            }

            var editableObject = obj.GetComponentInParent<AIEditableObject>();
            if (editableObject)
            {
                Register(editableObject);
                summary = summariesByComponent[editableObject];
                return true;
            }

            if (!allowFallbackSummaries)
            {
                return false;
            }

            summary = CreateFallbackSummary(obj);
            return true;
        }

        public bool TryGetSummary(Collider collider, out ObjectSummary summary)
        {
            summary = null;
            return collider && TryGetSummary(collider.gameObject, out summary);
        }

        public IReadOnlyList<ObjectSummary> GetAllSummaries()
        {
            summaryCache.Clear();
            foreach (var item in summariesByComponent)
            {
                if (item.Key)
                {
                    summaryCache.Add(item.Key.ToSummary());
                }
            }

            return summaryCache;
        }

        private ObjectSummary CreateFallbackSummary(GameObject obj)
        {
            var summary = new ObjectSummary
            {
                id = obj.name,
                display_name = obj.name,
                unity_name = obj.name,
                labels = new string[0],
                editable = fallbackEditable,
                active = obj.activeInHierarchy,
                position = SerializableVector3.From(obj.transform.position),
                rotation_euler = SerializableVector3.From(obj.transform.rotation.eulerAngles),
                source = "fallback"
            };

            var renderer = obj.GetComponentInChildren<Renderer>();
            if (renderer)
            {
                summary.bounds_center = SerializableVector3.From(renderer.bounds.center);
                summary.bounds_size = SerializableVector3.From(renderer.bounds.size);
            }

            return summary;
        }

        private bool HasDuplicateObjectId(AIEditableObject obj)
        {
            if (string.IsNullOrWhiteSpace(obj.objectId))
            {
                return false;
            }

            foreach (var registered in summariesByComponent.Keys)
            {
                if (registered && registered != obj && registered.objectId == obj.objectId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
