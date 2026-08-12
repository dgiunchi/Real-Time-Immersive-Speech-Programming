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
    /// </summary>
    public sealed class GeneratedMarker : MonoBehaviour
    {
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
