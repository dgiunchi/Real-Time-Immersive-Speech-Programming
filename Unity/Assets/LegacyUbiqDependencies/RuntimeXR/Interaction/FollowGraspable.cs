using UnityEngine;
using Ubiq.XR;

namespace Ubiq.Samples
{
    // Compatibility component retained by the study scene from Ubiq 0.4.2.
    public class FollowGraspable : MonoBehaviour, IGraspable
    {
        private Vector3 localGrabPoint;
        private Quaternion localGrabRotation;
        private Transform follow;

        public void Grasp(Hand controller)
        {
            var handTransform = controller.transform;
            localGrabPoint = handTransform.InverseTransformPoint(transform.position);
            localGrabRotation = Quaternion.Inverse(handTransform.rotation) * transform.rotation;
            follow = handTransform;
        }

        public void Release(Hand controller)
        {
            follow = null;
        }

        private void Update()
        {
            if (!follow) return;
            transform.rotation = follow.rotation * localGrabRotation;
            transform.position = follow.TransformPoint(localGrabPoint);
        }
    }
}
