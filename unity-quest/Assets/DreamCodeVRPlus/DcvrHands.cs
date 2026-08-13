// DreamCodeVR+ — articulated hand tracking.
//
// Controllers remain the primary input; this is additive. When the wearer puts the
// controllers down the runtime switches to hands, and without this the world simply loses
// their hands — which in VR reads as the system having stopped working.
//
// Joints are drawn as small emissive cubes rather than a skinned mesh: no rigged asset to
// ship, a fixed and tiny draw cost, and it matches the holographic language the rest of
// the environment already speaks. Anatomical fidelity is not the point; presence is.
//
// The same rule as the controller models applies and for the same hard-won reason — a
// joint with no real pose must not be drawn, or it renders at the rig origin, which is
// inside the wearer's head.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace DreamCodeVRPlus
{
    public sealed class DcvrHands : MonoBehaviour
    {
        private const float MinPoseDistance = 0.12f;

        private XRHandSubsystem _subsystem;
        private Transform[] _left, _right;

        public static DcvrHands Attach(Transform origin)
        {
            if (origin == null) { return null; }
            var go = new GameObject("DCVR_Hands");
            go.transform.SetParent(origin, false);
            var h = go.AddComponent<DcvrHands>();
            h.Construct();
            return h;
        }

        private void Construct()
        {
            _left = BuildJoints("L");
            _right = BuildJoints("R");
            AcquireSubsystem();
        }

        private void AcquireSubsystem()
        {
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            if (subsystems.Count > 0)
            {
                _subsystem = subsystems[0];
                Debug.Log("[DcvrHands] hand subsystem acquired");
            }
        }

        private Transform[] BuildJoints(string side)
        {
            var root = new GameObject("Hand" + side).transform;
            root.SetParent(transform, false);

            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            Material mat = null;
            if (holo != null)
            {
                mat = new Material(holo) { name = "DCVR_HandJointMat" + side };
                mat.SetColor("_Color", DcvrWorld.Cyan);
                mat.SetFloat("_Alpha", 0.85f);
            }

            int count = (int)XRHandJointID.EndMarker - (int)XRHandJointID.BeginMarker;
            var joints = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var j = DcvrPrim.Create(PrimitiveType.Cube, $"j{i}");
                j.transform.SetParent(root, false);
                // Fingertips smaller than the palm, so the hand reads as a hand.
                j.transform.localScale = Vector3.one * (i < 3 ? 0.019f : 0.011f);
                if (mat != null) { j.GetComponent<Renderer>().sharedMaterial = mat; }
                j.SetActive(false);
                joints[i] = j.transform;
            }
            return joints;
        }

        private void Update()
        {
            if (_subsystem == null) { AcquireSubsystem(); return; }
            if (!_subsystem.running) { HideAll(); return; }

            Apply(_subsystem.leftHand, _left);
            Apply(_subsystem.rightHand, _right);
        }

        private void Apply(XRHand hand, Transform[] joints)
        {
            if (!hand.isTracked) { Hide(joints); return; }

            for (int i = 0; i < joints.Length; i++)
            {
                var id = (XRHandJointID)((int)XRHandJointID.BeginMarker + i);
                XRHandJoint joint = hand.GetJoint(id);

                if (!joint.TryGetPose(out Pose pose) || pose.position.magnitude < MinPoseDistance)
                {
                    if (joints[i].gameObject.activeSelf) { joints[i].gameObject.SetActive(false); }
                    continue;
                }

                if (!joints[i].gameObject.activeSelf) { joints[i].gameObject.SetActive(true); }
                joints[i].localPosition = pose.position;
                joints[i].localRotation = pose.rotation;
            }
        }

        private void HideAll() { Hide(_left); Hide(_right); }

        private static void Hide(Transform[] joints)
        {
            if (joints == null) { return; }
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null && joints[i].gameObject.activeSelf)
                {
                    joints[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
