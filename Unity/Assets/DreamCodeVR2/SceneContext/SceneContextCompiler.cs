using System;
using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
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
                ,allowed_editable_properties = editableObject.GetComponent<AuthoringCapabilities>()?.editableProperties
                ,allowed_behaviors = editableObject.GetComponent<AuthoringCapabilities>()?.allowedBehaviors
                ,quest_critical = editableObject.GetComponent<AuthoringCapabilities>() && editableObject.GetComponent<AuthoringCapabilities>().questCritical
                ,semantic_state = editableObject.GetComponent<AuthoringSemanticState>()?.state
                ,runtime_created = editableObject.labels != null && Array.Exists(editableObject.labels, label => label == "runtime_created")
                ,active_authoring_behaviors = BuildActiveBehaviors(editableObject)
                ,parent_anchor = editableObject.GetComponentInParent<AuthoringAnchor>()?.anchorId
                ,currently_held = editableObject.GetComponent<ExperimentalGrabbableAdapter>()?.IsHeld ?? false
                ,player_authored_affordances = BuildAffordances(editableObject)
                ,created_by_action_id = editableObject.GetComponent<RuntimeAuthoringMetadata>()?.createdByActionId
                ,created_during_task_id = editableObject.GetComponent<RuntimeAuthoringMetadata>()?.createdDuringTaskId
                ,predefined_voice_commands = editableObject.GetComponent<VoiceCommandCapabilities>()?.predefinedVoiceActions
                ,predefined_presets = editableObject.GetComponent<VoiceCommandCapabilities>()?.predefinedPresets
                ,editable_affordances = new[] { "grabbable", "movable", "interactable", "gravity_enabled", "kinematic", "collision_enabled" }
                ,protected_for_current_task = IsProtectedForCurrentTask(editableObject)
                ,is_open = ResolveOpenState(editableObject)
                ,is_locked = editableObject.GetComponent<DreamCodeVR2.Quest.QuestLockController>()?.IsLocked
                ,required_key_id = editableObject.GetComponent<DreamCodeVR2.Quest.QuestLockController>()?.requiredKeyId
                ,associated_target_object_id = editableObject.GetComponent<DreamCodeVR2.Quest.QuestLockController>()?.associatedTargetObjectId
                ,is_aligned = editableObject.GetComponent<DreamCodeVR2.Quest.QuestPaintingController>()?.IsAligned
                ,is_lamp_active = editableObject.GetComponent<DreamCodeVR2.Quest.QuestLampController>()?.IsActive
                ,light_profile = editableObject.GetComponent<DreamCodeVR2.Quest.QuestLampController>()?.ColorProfile
                ,sphere_profile = editableObject.GetComponent<DreamCodeVR2.Quest.C1QuestSphereController>()?.SphereProfile
                ,placement_anchor_ids = BuildPlacementAnchors(editableObject)
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
            var capabilities = editableObject.GetComponent<AuthoringCapabilities>();
            return capabilities ? capabilities.allowedOperations : editableObject.editable ? new[] { "edit" } : null;
        }

        private static string[] BuildActiveBehaviors(AIEditableObject editableObject)
        {
            var behaviors = editableObject.GetComponents<AuthoringRuntimeBehavior>();
            if (behaviors == null || behaviors.Length == 0) return null;
            return behaviors.Where(behavior => behavior && behavior.enabled).Select(behavior => behavior.GetType().Name).ToArray();
        }

        private static string[] BuildAffordances(AIEditableObject editableObject)
        {
            var state=editableObject.GetComponent<AuthoringAffordanceState>();
            if(!state)return null;
            var values=new List<string>(); if(state.grabbable)values.Add("grabbable");if(state.movable)values.Add("movable");if(state.interactable)values.Add("interactable");return values.ToArray();
        }

        private static bool IsProtectedForCurrentTask(AIEditableObject editableObject)
        {
            var task=FindFirstObjectByType<DreamCodeVR2.Quest.QuestRuntimeState>()?.GetCurrentTask();
            if(task?.protectedDuringTask==null)return false;
            return Array.Exists(task.protectedDuringTask,id=>id==editableObject.objectId);
        }

        private static bool? ResolveOpenState(AIEditableObject editableObject)
        {
            var drawer=editableObject.GetComponent<ExperimentalDrawerController>(); if(drawer) return drawer.IsOpen;
            var door=editableObject.GetComponent<DreamCodeVR2.Quest.QuestDoorController>(); return door ? door.IsOpen : null;
        }

        private static string[] BuildPlacementAnchors(AIEditableObject editableObject)
        {
            var values=editableObject.GetComponentsInChildren<AuthoringAnchor>(true); if(values==null||values.Length==0)return null;
            return values.Where(anchor=>anchor&&!string.IsNullOrWhiteSpace(anchor.anchorId)).Select(anchor=>anchor.anchorId).ToArray();
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
