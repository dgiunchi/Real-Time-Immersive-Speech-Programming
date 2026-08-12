// DreamCodeVR+ — visible, tracked controllers.
//
// Two purposes. Practically, seeing your hands anchored in the world is one of the
// strongest presence cues there is, and their parallax against the platform is immediate
// proof to a wearer that tracking is real. Diagnostically, they are the fastest way to
// tell "6DoF is working" from "the image is following my head": if the controllers move
// independently of your gaze, positional tracking is live.
//
// Deliberately geometry, not a model import: a small emissive pointer reads cleanly in
// the project's visual language and costs two draw calls.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrControllers : MonoBehaviour
    {
        private Transform _left, _right;
        private static readonly List<InputDevice> Devices = new List<InputDevice>();

        public static DcvrControllers Attach(Transform origin)
        {
            if (origin == null) { return null; }
            var go = new GameObject("DCVR_Controllers");
            go.transform.SetParent(origin, false);
            var c = go.AddComponent<DcvrControllers>();
            c._left = c.BuildHand("Left");
            c._right = c.BuildHand("Right");
            return c;
        }

        private Transform BuildHand(string side)
        {
            var root = new GameObject("DCVR_Hand" + side).transform;
            root.SetParent(transform, false);

            // A slim body plus a forward pointer: enough to read orientation at a glance.
            var body = DcvrPrim.Create(PrimitiveType.Cube);
            body.name = "body";
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(0.035f, 0.035f, 0.10f);

            var tip = DcvrPrim.Create(PrimitiveType.Cube);
            tip.name = "tip";
            tip.transform.SetParent(root, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.13f);
            tip.transform.localScale = new Vector3(0.012f, 0.012f, 0.16f);

            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            if (holo != null)
            {
                var m = new Material(holo) { name = "DCVR_HandMat" + side };
                m.SetColor("_Color", DcvrWorld.Cyan);
                m.SetFloat("_Alpha", 0.75f);
                m.SetFloat("_ScanSpeed", 0.6f);
                body.GetComponent<Renderer>().sharedMaterial = m;
                tip.GetComponent<Renderer>().sharedMaterial = m;
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return root;
        }

        private void Update()
        {
            Track(XRNode.LeftHand, _left);
            Track(XRNode.RightHand, _right);
        }

        /// <summary>Poses are LOCAL to the XR origin, so they are applied as local
        /// transforms. Applying them in world space would break the moment locomotion
        /// moved or rotated the origin.</summary>
        private static void Track(XRNode node, Transform t)
        {
            if (t == null) { return; }
            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, Devices);
            for (int i = 0; i < Devices.Count; i++)
            {
                bool gotPos = Devices[i].TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p);
                bool gotRot = Devices[i].TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r);
                if (gotPos || gotRot)
                {
                    if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); }
                    if (gotPos) { t.localPosition = p; }
                    if (gotRot) { t.localRotation = r; }
                    return;
                }
            }
            // Controller asleep or absent (hand tracking): hide rather than freeze a stale
            // pose in mid-air, which reads as a bug.
            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); }
        }
    }
}
