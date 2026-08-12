// DreamCodeVR+ — hide controller models that have no real pose.
//
// TrackedPoseDriver writes a pose only while the runtime supplies one. When it does not,
// the transform is left at IDENTITY — which under an XR rig is the Camera Offset origin,
// i.e. inside the wearer's head. The model then renders a centimetre from the eye, where
// a 3 cm slab covers a large part of the field of view and is unrecognisable as anything.
//
// A first attempt gated on CommonUsages.isTracked. That correctly hid a sleeping
// controller, but an on-device probe showed the right hand still sitting at exactly
// distance 0.00 m from the eye and covering 23% of the view: the device was reporting
// tracked while no pose was reaching the transform.
//
// So the test is now GEOMETRIC rather than advisory. A controller cannot physically be
// inside the wearer's skull, so a pose within a few centimetres of the rig origin is not
// a pose — it is the absence of one. That holds regardless of what any device flag claims,
// and it is the condition that actually matters: is this thing about to be drawn on the
// wearer's eye?

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrHandVisibility : MonoBehaviour
    {
        /// <summary>Any reported pose closer than this to the rig origin is treated as "no
        /// pose". Real controllers are held well beyond arm-to-head distance even when
        /// resting against the chest.</summary>
        private const float MinPoseDistance = 0.12f;

        private XRNode _node;
        private Renderer[] _renderers;
        private bool _visible;
        private static readonly List<InputDevice> Devices = new List<InputDevice>();

        public static void Attach(Transform hand, XRNode node)
        {
            if (hand == null) { return; }
            var v = hand.gameObject.AddComponent<DcvrHandVisibility>();
            v._node = node;
            v._renderers = hand.GetComponentsInChildren<Renderer>(includeInactive: true);
            v._visible = true;
            v.SetVisible(false);   // start hidden; nothing has a pose before the first poll
        }

        private void Update()
        {
            SetVisible(HasRealPose());
        }

        private bool HasRealPose()
        {
            // Degenerate transform: whatever the device claims, nothing is being written.
            if (transform.localPosition.magnitude < MinPoseDistance) { return false; }

            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(_node, Devices);
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
