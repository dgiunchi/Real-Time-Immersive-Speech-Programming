using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Persistent marker stamped on EVERY object produced by an action plan
    /// (SP-02 disguise / false-affordance defence). Because it is a real
    /// component the provenance survives reparenting/renaming, so the executor
    /// can always tell "this is AI-generated" apart from authentic scene objects
    /// and overlap/occlusion guards can scope themselves to generated content.
    /// IL2CPP-safe: a plain MonoBehaviour, no reflection.
    ///
    /// It also carries the object's SEMANTIC IDENTITY, which is what lets a person say
    /// "remove Saturn" instead of naming a Unity object. Identity lives here rather than
    /// in a parallel component for the same reason provenance does: this survives
    /// reparenting and renaming, and one marker means there is exactly one answer to
    /// "what is this object and where did it come from".
    /// </summary>
    public sealed class GeneratedMarker : MonoBehaviour
    {
        /// <summary>Stable per-session id. Unity names are not unique and change.</summary>
        public int RuntimeId;

        /// <summary>What the user would call it — "Saturn", "north-west tower". May be
        /// empty when the generator gave no useful name; resolution then falls back to
        /// pointing, selection or recency.</summary>
        public string SemanticName = "";

        /// <summary>Which generation produced it, so a whole creation can be addressed.</summary>
        public int GenerationId = -1;

        /// <summary>Coarse composition role, used by the spatial layer to decide whether
        /// this object belongs on the ground or in the air. Not a security property.</summary>
        public string Role = "";

        public float CreatedAt;
    }

    /// <summary>
    /// Tracks objects spawned by action plans so they can be rolled back (undo)
    /// and so the per-session spawn cap can be enforced. Phase 1 provides the
    /// data structure + <see cref="UndoAll"/>; richer undo/redo arrives later.
    /// </summary>
    public sealed class GeneratedObjectTracker : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        public int SpawnedCount => _spawned.Count;

        /// <summary>Read-only view of currently-tracked generated objects (for overlap checks).</summary>
        public IReadOnlyList<GameObject> Spawned => _spawned;

        /// <summary>True if spawning <paramref name="n"/> more stays within the session cap.</summary>
        public bool CanSpawn(int n) => _spawned.Count + n <= ProtocolModels.MaxTotalSpawnedPerSession;

        public void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        /// <summary>Rollback: destroy everything this session generated.</summary>
        public void UndoAll()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i]);
                }
            }
            _spawned.Clear();
        }
    }
}
