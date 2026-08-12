// DreamCodeVR+ — hide controller models that are not actually being tracked.
//
// TrackedPoseDriver writes a pose only while the device reports one. When a controller is
// asleep, put down, or absent, the driver leaves the transform at IDENTITY — which, under
// an XR rig, is the Camera Offset origin: the wearer's own head. The controller mesh then
// renders a centimetre from the eye, where even a 3 cm pointer fills a large part of the
// field of view as an unidentifiable slab.
//
// That is exactly what happened here: a bright cyan wedge covering a third of the view,
// which the diagnostic had already hinted at by reporting both hand positions as identical
// to the head position.
//
// So visibility follows the tracking state rather than the component's existence.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrHandVisibility : MonoBehaviour
    {
        private XRNode _node;
        private Renderer[] _renderers;
        private bool _visible = true;
        private static readonly List<InputDevice> Devices = new List<InputDevice>();

        public static void Attach(Transform hand, XRNode node)
        {
            if (hand == null) { return; }
            var v = hand.gameObject.AddComponent<DcvrHandVisibility>();
            v._node = node;
            v._renderers = hand.GetComponentsInChildren<Renderer>(includeInactive: true);
            v.SetVisible(false);   // start hidden; nothing is tracked before the first poll
        }

        private void Update()
        {
            SetVisible(IsTracked(_node));
        }

        /// <summary>Tracked means the runtime is actively reporting a pose — not merely that
        /// a device object exists. A paired-but-idle controller still enumerates.</summary>
        private static bool IsTracked(XRNode node)
        {
            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, Devices);
            for (int i = 0; i < Devices.Count; i++)
            {
                if (!Devices[i].isValid) { continue; }
                if (Devices[i].TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
                {
                    return true;
                }
            }
            return false;
        }

        private void SetVisible(bool visible)
        {
            if (visible == _visible || _renderers == null) { return; }
            _visible = visible;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) { _renderers[i].enabled = visible; }
            }
        }
    }
}
