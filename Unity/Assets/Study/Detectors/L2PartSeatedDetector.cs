using System.Collections.Generic;
using UnityEngine;

namespace AgenticXR.Study
{
    public sealed class L2PartSeatedDetector : StudySuccessDetector
    {
        public Rigidbody[] parts;
        public Collider[] matchingSockets;
        private readonly Dictionary<int, float> restingSince = new Dictionary<int, float>();

        protected override void OnArmed() => restingSince.Clear();

        private void Update()
        {
            if (!IsArmed || HasFired || parts == null || matchingSockets == null) return;
            var count = Mathf.Min(parts.Length, matchingSockets.Length);
            for (var index = 0; index < count; index++)
            {
                var part = parts[index];
                var socket = matchingSockets[index];
                if (part == null || socket == null)
                { restingSince.Remove(index); continue; }
                var acceptanceBounds = socket.bounds;
                acceptanceBounds.Expand(new Vector3(0.12f, 0.2f, 0.12f));
                if (!acceptanceBounds.Contains(part.worldCenterOfMass) || !IsAtRest(part))
                { restingSince.Remove(index); continue; }
                if (!restingSince.TryGetValue(index, out var since)) { restingSince[index] = Time.unscaledTime; continue; }
                if (Time.unscaledTime - since < settleWindowSeconds) continue;
                FireOnce("{\"partId\":\"" + AgenticCache.AgenticSceneRegistry.Escape(part.gameObject.name) +
                    "\",\"socketId\":\"" + AgenticCache.AgenticSceneRegistry.Escape(socket.gameObject.name) + "\"}");
                return;
            }
        }
    }
}
