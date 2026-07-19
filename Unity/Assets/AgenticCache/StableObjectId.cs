using System;
using System.Text;
using UnityEngine;

namespace AgenticCache
{
    [DisallowMultipleComponent]
    public sealed class StableObjectId : MonoBehaviour
    {
        [SerializeField] private string value;
        [SerializeField] private long revision = 1;
        private string lastFingerprint;

        public string Value => value;
        public long Revision => revision;

        public void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(value))
            {
                value = "xr-" + Fnv1a64(BuildHierarchyPath(transform)).ToString("x16");
            }
            if (string.IsNullOrEmpty(lastFingerprint)) lastFingerprint = Fingerprint();
        }

        public void ResolveDuplicate(string discriminator)
        {
            value = "xr-" + Fnv1a64(BuildHierarchyPath(transform) + "|" + discriminator).ToString("x16");
            lastFingerprint = Fingerprint();
        }

        public bool RefreshRevision()
        {
            EnsureInitialized();
            var fingerprint = Fingerprint();
            if (fingerprint == lastFingerprint) return false;
            lastFingerprint = fingerprint;
            revision += 1;
            return true;
        }

        private string Fingerprint()
        {
            var t = transform;
            var fingerprint = new StringBuilder(string.Join("|", gameObject.name, gameObject.tag,
                t.localPosition.ToString("R"), t.localRotation.ToString("R"),
                t.localScale.ToString("R")));
            foreach (var component in GetComponents<Component>())
            {
                if (component == null) continue;
                fingerprint.Append('|').Append(component.GetType().FullName);
                foreach (var field in component.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                {
                    if (!IsFingerprintValue(field.FieldType)) continue;
                    try { fingerprint.Append('|').Append(field.Name).Append('=').Append(field.GetValue(component)); }
                    catch { /* inaccessible runtime field; component type still participates */ }
                }
            }
            return fingerprint.ToString();
        }

        private static bool IsFingerprintValue(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) ||
            type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Color);

        private static string BuildHierarchyPath(Transform current)
        {
            var path = current.name + "[" + current.GetSiblingIndex() + "]";
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "[" + current.GetSiblingIndex() + "]/" + path;
            }
            return current.gameObject.scene.name + ":" + path;
        }

        private static ulong Fnv1a64(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= prime;
            }
            return hash;
        }
    }
}
