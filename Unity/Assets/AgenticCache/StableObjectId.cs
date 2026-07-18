using System;
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
            return string.Join("|", gameObject.name, gameObject.tag,
                t.localPosition.ToString("R"), t.localRotation.ToString("R"),
                t.localScale.ToString("R"), GetComponents<Component>().Length.ToString());
        }

        private static string BuildHierarchyPath(Transform current)
        {
            var path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
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
