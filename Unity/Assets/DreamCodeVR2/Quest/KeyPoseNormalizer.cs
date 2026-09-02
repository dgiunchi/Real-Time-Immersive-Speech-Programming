using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    // Captures the imported key mesh's authored orientation once. All later pose changes
    // reuse that contract instead of applying per-feature Euler corrections.
    public sealed class KeyPoseNormalizer : MonoBehaviour
    {
        private Transform visualRoot;
        private Quaternion canonicalVisualLocalRotation;
        private Vector3 canonicalRootLocalScale;
        private bool captured;

        public static void Normalize(AIEditableObject key, string reason, Transform targetAnchor = null)
        {
            if (!IsKey(key)) return;
            var normalizer = key.GetComponent<KeyPoseNormalizer>() ?? key.gameObject.AddComponent<KeyPoseNormalizer>();
            normalizer.Apply(reason, targetAnchor);
        }

        public static void NormalizeVisualOnly(AIEditableObject key, string reason)
        {
            if (!IsKey(key)) return;
            var normalizer = key.GetComponent<KeyPoseNormalizer>() ?? key.gameObject.AddComponent<KeyPoseNormalizer>();
            normalizer.ApplyVisualOnly(reason);
        }

        private void Apply(string reason, Transform targetAnchor)
        {
            Capture();
            if (targetAnchor) transform.SetPositionAndRotation(targetAnchor.position, targetAnchor.rotation);
            transform.localScale = canonicalRootLocalScale;
            ApplyVisualOnly(reason);
        }

        private void ApplyVisualOnly(string reason)
        {
            Capture();
            if (visualRoot && visualRoot != transform) visualRoot.localRotation = canonicalVisualLocalRotation;
            ClearVelocities();
            var item = GetComponent<AIEditableObject>();
            DreamCodeVR2ClientLogger.Event("quest", "KEY_POSE_NORMALIZED", null, new { key_id = item?.objectId, reason, parent = transform.parent ? transform.parent.gameObject.name : null, world_position = transform.position, world_rotation = transform.rotation, local_position = transform.localPosition, local_rotation = transform.localRotation });
        }

        private void Capture()
        {
            if (captured) return;
            captured = true;
            canonicalRootLocalScale = transform.localScale;
            var renderer = GetComponentInChildren<Renderer>(true);
            visualRoot = renderer ? renderer.transform : transform;
            canonicalVisualLocalRotation = visualRoot.localRotation;
        }

        private void ClearVelocities()
        {
            var body = GetComponent<Rigidbody>();
            if (!body || !body.isKinematic) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private static bool IsKey(AIEditableObject item)
        {
            return item && !string.IsNullOrWhiteSpace(item.objectId) && item.objectId.IndexOf("key", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
