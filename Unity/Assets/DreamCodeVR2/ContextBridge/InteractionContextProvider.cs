using System;
using System.Linq;
using UnityEngine;

namespace DreamCodeVR2.ContextBridge
{
    public class InteractionContextProvider : MonoBehaviour
    {
        public SceneRegistry sceneRegistry;
        public Transform[] pointerOrigins;
        public LayerMask raycastLayers = Physics.DefaultRaycastLayers;
        public float maxRayDistance = 8f;
        public bool useExistingSelection = true;
        public bool raycastEverySnapshot = true;
        public global::CodeGenerationManager codeGenerationManager;
        public global::SelectObjectRay[] existingSelectionSources;

        private bool hasCachedPointing;
        private ObjectSummary cachedPointedObject;
        private SerializableVector3 cachedPointedWorldPosition;

        public InteractionContextSnapshot CaptureSnapshot(string peer)
        {
            EnsureRegistry();

            var snapshot = new InteractionContextSnapshot
            {
                peer = peer,
                timestamp_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                scene_version = sceneRegistry ? sceneRegistry.CurrentSceneVersion : 0,
                active_selection = ResolveActiveSelection(),
                last_action = null,
                pending_confirmation = null
            };

            if (raycastEverySnapshot || !hasCachedPointing)
            {
                UpdatePointingCache();
            }

            snapshot.pointed_object = cachedPointedObject;
            snapshot.pointed_world_position = cachedPointedWorldPosition;
            return snapshot;
        }

        public void RefreshPointing()
        {
            EnsureRegistry();
            UpdatePointingCache();
        }

        public AIEditableObject GetCurrentPointedEditableObject()
        {
            EnsureRegistry();
            if (!TryGetBestPointerHit(out var hit))
            {
                return null;
            }

            return hit.collider ? hit.collider.GetComponentInParent<AIEditableObject>() : null;
        }

        public AIEditableObject GetCurrentSelectedEditableObject()
        {
            return ResolveActiveSelectionObject();
        }

        private void EnsureRegistry()
        {
            if (!sceneRegistry)
            {
                if (SceneRegistry.Instance)
                {
                    sceneRegistry = SceneRegistry.Instance;
                }
                else
                {
                    var registries = FindObjectsByType<SceneRegistry>(FindObjectsSortMode.None);
                    if (registries != null && registries.Length > 0)
                    {
                        sceneRegistry = registries[0];
                    }
                }
            }
        }

        private ObjectSummary ResolveActiveSelection()
        {
            if (!useExistingSelection || !sceneRegistry)
            {
                return null;
            }

            var selectedObject = ResolveActiveSelectionObject();
            if (!selectedObject)
            {
                return null;
            }

            if (sceneRegistry.TryGetSummary(selectedObject.gameObject, out var summary))
            {
                return summary;
            }

            return null;
        }

        private AIEditableObject ResolveActiveSelectionObject()
        {
            if (!useExistingSelection || !sceneRegistry)
            {
                return null;
            }

            if (codeGenerationManager && codeGenerationManager.targetObject)
            {
                var editableFromManager = codeGenerationManager.targetObject.GetComponentInParent<AIEditableObject>();
                if (editableFromManager)
                {
                    return editableFromManager;
                }
            }

            if (existingSelectionSources == null)
            {
                return null;
            }

            foreach (var source in existingSelectionSources)
            {
                if (!source || !source.selectedObject)
                {
                    continue;
                }

                var editableFromSource = source.selectedObject.GetComponentInParent<AIEditableObject>();
                if (editableFromSource)
                {
                    return editableFromSource;
                }
            }

            return null;
        }

        private void UpdatePointingCache()
        {
            cachedPointedObject = null;
            cachedPointedWorldPosition = null;
            hasCachedPointing = true;

            if (!sceneRegistry || pointerOrigins == null || pointerOrigins.Length == 0)
            {
                return;
            }

            if (!TryGetBestPointerHit(out var hit))
            {
                return;
            }

            cachedPointedWorldPosition = SerializableVector3.From(hit.point);
            if (sceneRegistry.TryGetSummary(hit.collider, out var summary))
            {
                cachedPointedObject = summary;
            }
        }

        private bool TryGetBestPointerHit(out RaycastHit bestHit)
        {
            bestHit = default;
            var foundHit = false;
            var bestDistance = float.PositiveInfinity;

            foreach (var origin in pointerOrigins)
            {
                if (!origin)
                {
                    continue;
                }

                foreach (var hit in Physics.RaycastAll(
                    origin.position,
                    origin.forward,
                    maxRayDistance,
                    raycastLayers,
                    QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
                {
                    if (!TryResolveEditableHit(hit, out _))
                    {
                        continue;
                    }

                    if (hit.distance >= bestDistance)
                    {
                        continue;
                    }

                    bestHit = hit;
                    bestDistance = hit.distance;
                    foundHit = true;
                    break;
                }
            }

            return foundHit;
        }

        private bool TryResolveEditableHit(RaycastHit hit, out AIEditableObject editableObject)
        {
            editableObject = null;

            var collider = hit.collider;
            if (!collider)
            {
                return false;
            }

            editableObject = collider.GetComponentInParent<AIEditableObject>();
            if (!editableObject)
            {
                return false;
            }

            var hitObject = collider.gameObject;
            var resolvedObject = editableObject.gameObject;
            return hitObject.CompareTag("game") || resolvedObject.CompareTag("game");
        }
    }
}
