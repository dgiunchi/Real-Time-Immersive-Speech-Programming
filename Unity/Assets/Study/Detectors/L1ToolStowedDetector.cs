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
                {
                    if (tray == null) continue;
                    var acceptanceBounds = tray.bounds;
                    // Placement is performed by an XR ray and the authored tray
                    // is shallow. Allow a small tolerance around its logical
                    // volume so visually correct placements are not rejected by
                    // centre-of-mass rounding or contact jitter.
                    acceptanceBounds.Expand(new Vector3(0.12f, 0.2f, 0.12f));
                    if (acceptanceBounds.Contains(tool.worldCenterOfMass))
                    {
                        containingTray = tray;
                        break;
                    }
                }
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
