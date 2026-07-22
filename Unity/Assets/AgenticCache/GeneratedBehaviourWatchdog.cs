using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgenticCache
{
    // Coarse main-thread defense for generated behaviours. Unity cannot pre-empt a
    // truly infinite Update() loop on the main thread; this watchdog catches code
    // that returns but repeatedly blows the frame or allocation budget and disables
    // it before the session continues degrading.
    public sealed class GeneratedBehaviourWatchdog : MonoBehaviour
    {
        private sealed class Watched
        {
            public MonoBehaviour behaviour;
            public string artifactId;
            public int frameBudgetStrikes;
            public int allocationBudgetStrikes;
        }

        public CacheExchangeManager manager;
        public float maxFrameTimeMs = 50f;
        public long maxManagedAllocationPerFrameBytes = 4 * 1024 * 1024;
        public int strikesBeforeDisable = 3;

        private readonly Dictionary<MonoBehaviour, Watched> watched = new Dictionary<MonoBehaviour, Watched>();
        private long previousManagedBytes;

        private void Start() => previousManagedBytes = GC.GetTotalMemory(false);

        public void Register(MonoBehaviour behaviour, string artifactId)
        {
            if (behaviour == null) return;
            watched[behaviour] = new Watched { behaviour = behaviour, artifactId = artifactId };
        }

        public void Unregister(MonoBehaviour behaviour)
        {
            if (behaviour != null) watched.Remove(behaviour);
        }

        private void LateUpdate()
        {
            var managedBytes = GC.GetTotalMemory(false);
            var allocationDelta = Math.Max(0, managedBytes - previousManagedBytes);
            previousManagedBytes = managedBytes;
            var frameMs = Time.unscaledDeltaTime * 1000f;
            var toDisable = new List<Watched>();

            foreach (var entry in watched.Values)
            {
                if (entry.behaviour == null || !entry.behaviour.enabled) continue;
                entry.frameBudgetStrikes = frameMs > maxFrameTimeMs ? entry.frameBudgetStrikes + 1 : 0;
                entry.allocationBudgetStrikes = allocationDelta > maxManagedAllocationPerFrameBytes ? entry.allocationBudgetStrikes + 1 : 0;
                if (entry.frameBudgetStrikes >= strikesBeforeDisable || entry.allocationBudgetStrikes >= strikesBeforeDisable)
                    toDisable.Add(entry);
            }

            foreach (var entry in toDisable)
            {
                entry.behaviour.enabled = false;
                var reason = entry.frameBudgetStrikes >= strikesBeforeDisable
                    ? "generated behaviour exceeded the frame budget"
                    : "generated behaviour exceeded the managed-allocation budget";
                Debug.LogError("[AgenticXR] watchdog disabled " + entry.artifactId + ": " + reason);
                manager?.ReportExecutionWatchdog(entry.artifactId, reason, frameMs, allocationDelta);
            }
        }
    }
}
