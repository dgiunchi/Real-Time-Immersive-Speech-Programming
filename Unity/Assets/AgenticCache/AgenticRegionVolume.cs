using UnityEngine;

namespace AgenticCache
{
    // Authorable in-scene trigger volume for the L2 "context" trigger path.
    // This MonoBehaviour intentionally lives in its own same-named file so Unity
    // can persist a stable MonoScript reference in generated scenes.
    public sealed class AgenticRegionVolume : MonoBehaviour
    {
        [Tooltip("Stable region identifier reported to Shared XR Memory, e.g. 'workshop-entrance'.")]
        public string regionId = "region";

        [Tooltip("Fallback box extents (local, metres) when no Collider is attached.")]
        public Vector3 size = new Vector3(4f, 3f, 4f);

        public bool Contains(Vector3 worldPoint)
        {
            var attached = GetComponent<Collider>();
            if (attached != null) return attached.bounds.Contains(worldPoint);
            var local = transform.InverseTransformPoint(worldPoint);
            var half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
