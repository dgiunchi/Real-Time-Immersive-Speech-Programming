using System;
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

        private void EnsureRegistry()
        {
            if (!sceneRegistry)
            {
                sceneRegistry = SceneRegistry.Instance ? SceneRegistry.Instance : FindObjectOfType<SceneRegistry>();
            }
        }

        private ObjectSummary ResolveActiveSelection()
        {
            if (!useExistingSelection || !sceneRegistry)
            {
                return null;
            }

            if (codeGenerationManager && codeGenerationManager.targetObject)
            {
                if (sceneRegistry.TryGetSummary(codeGenerationManager.targetObject, out var summary))
                {
                    return summary;
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

                if (sceneRegistry.TryGetSummary(source.selectedObject, out var summary))
                {
                    return summary;
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

                if (!Physics.Raycast(
                    origin.position,
                    origin.forward,
                    out var hit,
                    maxRayDistance,
                    raycastLayers,
                    QueryTriggerInteraction.Ignore))
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
            }

            return foundHit;
        }
    }
}
