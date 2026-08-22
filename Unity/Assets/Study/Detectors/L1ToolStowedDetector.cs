using System.Collections.Generic;
using UnityEngine;

namespace AgenticXR.Study
{
    public sealed class L1ToolStowedDetector : StudySuccessDetector
    {
        public Rigidbody[] tools;
        public Collider[] trays;
        private readonly Dictionary<Rigidbody, float> restingSince = new Dictionary<Rigidbody, float>();

        protected override void OnArmed() => restingSince.Clear();

        private void Update()
        {
            if (!IsArmed || HasFired || tools == null || trays == null) return;
            foreach (var tool in tools)
            {
                if (tool == null) continue;
                Collider containingTray = null;
                foreach (var tray in trays)
                    if (tray != null && tray.bounds.Contains(tool.worldCenterOfMass)) { containingTray = tray; break; }
                if (containingTray == null || !IsAtRest(tool)) { restingSince.Remove(tool); continue; }
                if (!restingSince.TryGetValue(tool, out var since)) { restingSince[tool] = Time.unscaledTime; continue; }
                if (Time.unscaledTime - since < settleWindowSeconds) continue;
                FireOnce("{\"toolId\":\"" + EscapeId(tool.gameObject.name) + "\",\"trayId\":\"" +
                    EscapeId(containingTray.gameObject.name) + "\"}");
                return;
            }
        }

        private static string EscapeId(string value) => AgenticCache.AgenticSceneRegistry.Escape(value);
    }
}
