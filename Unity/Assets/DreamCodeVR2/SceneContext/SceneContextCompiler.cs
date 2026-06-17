using System;
using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamCodeVR2.SceneContext
{
    public class SceneContextCompiler : MonoBehaviour
    {
        public SceneRegistry sceneRegistry;
        public bool includeInactiveEditableObjects = true;

        public SceneContextPacket CaptureSnapshot(string peerUuid)
        {
            EnsureSceneRegistry();

            var editableObjects = includeInactiveEditableObjects
                ? FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                : FindObjectsByType<AIEditableObject>(FindObjectsSortMode.None);

            var objects = new List<SceneObjectSummary>(editableObjects.Length);
            foreach (var editableObject in editableObjects)
            {
                if (!editableObject || !editableObject.gameObject.scene.IsValid())
                {
                    continue;
                }

                objects.Add(BuildObjectSummary(editableObject));
            }

            objects.Sort(CompareObjects);

            return new SceneContextPacket
            {
                peer = peerUuid,
                timestamp_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                scene_version = sceneRegistry ? sceneRegistry.CurrentSceneVersion : 0,
                scene_name = SceneManager.GetActiveScene().name,
                objects = objects.ToArray()
            };
        }

        private void EnsureSceneRegistry()
        {
            if (sceneRegistry)
            {
                return;
            }

            if (SceneRegistry.Instance)
            {
                sceneRegistry = SceneRegistry.Instance;
                return;
            }

            var registries = FindObjectsByType<SceneRegistry>(FindObjectsSortMode.None);
            if (registries != null && registries.Length > 0)
            {
                sceneRegistry = registries[0];
            }
        }

        private SceneObjectSummary BuildObjectSummary(AIEditableObject editableObject)
        {
            var objectId = ResolveObjectId(editableObject);
            var labels = ResolveLabels(editableObject);
            return new SceneObjectSummary
            {
                id = objectId,
                display_name = string.IsNullOrWhiteSpace(editableObject.displayName) ? editableObject.gameObject.name : editableObject.displayName,
                unity_name = editableObject.gameObject.name,
                semantic_types = labels,
                labels = labels,
                description = string.IsNullOrWhiteSpace(editableObject.description) ? null : editableObject.description,
                position = SerializableVector3.From(editableObject.transform.position),
                rotation = SerializableVector3.From(editableObject.transform.rotation.eulerAngles),
                scale = SerializableVector3.From(editableObject.transform.lossyScale),
                active = editableObject.gameObject.activeInHierarchy,
                editable = editableObject.editable,
                parent_id = ResolveParentId(editableObject),
                materials = BuildMaterials(editableObject),
                components = BuildComponents(editableObject),
                available_operations = BuildAvailableOperations(editableObject)
            };
        }

        private static string ResolveObjectId(AIEditableObject editableObject)
        {
            return string.IsNullOrWhiteSpace(editableObject.objectId)
                ? editableObject.gameObject.name
                : editableObject.objectId;
        }

        private static string[] ResolveLabels(AIEditableObject editableObject)
        {
            if (editableObject.labels == null || editableObject.labels.Length == 0)
            {
                return Array.Empty<string>();
            }

            return editableObject.labels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string ResolveParentId(AIEditableObject editableObject)
        {
            var current = editableObject.transform.parent;
            while (current != null)
            {
                var parentEditable = current.GetComponent<AIEditableObject>();
                if (parentEditable)
                {
                    return ResolveObjectId(parentEditable);
                }

                current = current.parent;
            }

            return null;
        }

        private static SceneMaterialSummary[] BuildMaterials(AIEditableObject editableObject)
        {
            var materials = new List<SceneMaterialSummary>();
            var renderers = editableObject.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (!renderer)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                for (var index = 0; index < sharedMaterials.Length; index++)
                {
                    var material = sharedMaterials[index];
                    if (!material)
                    {
                        continue;
                    }

                    materials.Add(new SceneMaterialSummary
                    {
                        slot = $"{renderer.gameObject.name}[{index}]",
                        material_name = material.name,
                        shader_name = material.shader ? material.shader.name : null,
                        primary_color = ResolvePrimaryColor(material)
                    });
                }
            }

            return materials.Count == 0 ? null : materials.ToArray();
        }

        private static SerializableColor ResolvePrimaryColor(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                return SerializableColor.From(material.GetColor("_BaseColor"));
            }

            if (material.HasProperty("_Color"))
            {
                return SerializableColor.From(material.GetColor("_Color"));
            }

            return null;
        }

        private static SceneComponentSummary[] BuildComponents(AIEditableObject editableObject)
        {
            var components = new List<SceneComponentSummary>();
            foreach (var component in editableObject.GetComponents<Component>())
            {
                if (!component || component is Transform)
                {
                    continue;
                }

                components.Add(new SceneComponentSummary
                {
                    type_name = component.GetType().Name,
                    enabled = ResolveEnabled(component)
                });
            }

            if (components.Count == 0)
            {
                return null;
            }

            components.Sort((left, right) => string.CompareOrdinal(left.type_name, right.type_name));
            return components.ToArray();
        }

        private static bool? ResolveEnabled(Component component)
        {
            switch (component)
            {
                case Behaviour behaviour:
                    return behaviour.enabled;
                case Renderer renderer:
                    return renderer.enabled;
                case Collider collider:
                    return collider.enabled;
                default:
                    return null;
            }
        }

        private static string[] BuildAvailableOperations(AIEditableObject editableObject)
        {
            return editableObject.editable ? new[] { "edit" } : null;
        }

        private static int CompareObjects(SceneObjectSummary left, SceneObjectSummary right)
        {
            var idCompare = string.CompareOrdinal(left?.id, right?.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.CompareOrdinal(left?.unity_name, right?.unity_name);
        }
    }
}
