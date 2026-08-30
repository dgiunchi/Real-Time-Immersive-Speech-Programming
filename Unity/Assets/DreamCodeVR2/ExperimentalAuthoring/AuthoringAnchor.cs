using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public enum AnchorOccupancyPolicy { Multiple, Single, ReplaceExisting }
    // CENTER is for containment volumes (drawers/baskets); SURFACE places the object's
    // support point on the anchor plane using its effective world-space radius.
    public enum AnchorPlacementMode { Center, Surface }
    public class AuthoringAnchor : MonoBehaviour
    {
        public string anchorId; public string semanticLabel;
        public string[] allowedSpawnTypes = { "cube", "sphere", "bridge_segment", "platform" };
        public string[] allowedRelocationObjectTypes = { "*" };
        public AnchorOccupancyPolicy occupancyPolicy = AnchorOccupancyPolicy.Single;
        public AnchorPlacementMode placementMode = AnchorPlacementMode.Center;
        public bool questRestricted;
        public bool IsOccupied { get; private set; }
        public bool AllowsSpawn(string type) => Allows(allowedSpawnTypes, type);
        public bool AllowsRelocation(string type) => Allows(allowedRelocationObjectTypes, type);
        public void SetOccupied(bool occupied) => IsOccupied = occupied;
        private static bool Allows(string[] allowed, string value)
        {
            if (allowed == null) return false;
            foreach (var candidate in allowed) if (candidate == "*" || string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
