using UnityEditor;

namespace EliteIT.LPWFPLite.Demo
{
    using UnityEngine;

    /// <summary>
    /// Makes this transform orbit around a center point at a fixed radius.
    /// Visualizes the orbit with a wire disc.
    /// </summary>
    public class Orbit : MonoBehaviour
    {
        [Header("Orbit")] [Tooltip("Center point to orbit around. If not set, stays at world origin.")]
        public Transform center;

        [Tooltip("Orbit radius in world units.")]
        public float radius = 3f;

        [Tooltip("Angular speed in degrees per second.")]
        public float angularSpeedDeg = 45f;

        [Tooltip("Axis to orbit around (e.g., Vector3.up = horizontal orbit).")]
        public Vector3 axis = Vector3.up;

        private Vector3 orbitCenter;

        void Start()
        {
            orbitCenter = center ? center.position : Vector3.zero;

            // Snap to correct radius if not already
            Vector3 dir = (transform.position - orbitCenter).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward; // fallback
            transform.position = orbitCenter + dir * radius;
        }

        void Update()
        {
            orbitCenter = center ? center.position : Vector3.zero;

            // Rotate around the center
            transform.RotateAround(orbitCenter, axis.normalized, angularSpeedDeg * Time.deltaTime);

            // Keep the radius locked
            Vector3 dir = (transform.position - orbitCenter).normalized;
            transform.position = orbitCenter + dir * radius;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            DrawOrbitDisc();
        }

        void OnDrawGizmosSelected()
        {
            DrawOrbitDisc();
        }

        private void DrawOrbitDisc()
        {
            Vector3 c = center ? center.position : Vector3.zero;
            Vector3 n = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.up;

#if UNITY_EDITOR
            Handles.color = new Color(0.25f, 0.6f, 1f, 1f);
            Handles.DrawWireDisc(c, n, radius);
            Handles.DrawDottedLine(c, transform.position, 3f);
#endif
        }
#endif
    }
}