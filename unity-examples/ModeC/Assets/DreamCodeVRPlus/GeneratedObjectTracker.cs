using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Tracks objects spawned by action plans so they can be rolled back (undo)
    /// and so the per-session spawn cap can be enforced. Phase 1 provides the
    /// data structure + <see cref="UndoAll"/>; richer undo/redo arrives later.
    /// </summary>
    public sealed class GeneratedObjectTracker : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        public int SpawnedCount => _spawned.Count;

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
