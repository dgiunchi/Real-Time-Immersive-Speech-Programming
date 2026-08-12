// DreamCodeVR+ — the player rig, built from Unity's supported XR components.
//
// ROOT CAUSE THIS FILE EXISTS TO FIX
// ---------------------------------
// The previous rig was a hand-rolled hierarchy of plain GameObjects. It had an XR Origin
// in NAME only: no XROrigin component and, critically, NO TrackedPoseDriver on the camera.
// Nothing ever wrote the head pose to the camera transform. Two consequences, which are
// exactly the two symptoms reported from the headset:
//
//   * The camera sat at the origin's local (0,0,0) forever — i.e. ON THE FLOOR. Hence
//     "the camera is far too low, almost at floor level".
//   * With no pose ever applied, the rendered view never moved relative to the world, so
//     the scene behaved as though it were painted on the inside of the visor. Hence
//     "the environment feels like a static image that moves with me".
//
// OpenXR starting successfully proved only that the RUNTIME was up. It said nothing about
// whether the pose ever reached the scene graph. Those are different claims and were
// previously conflated.
//
// THE MODEL (kept deliberately explicit)
//   WORLD  — floor, platform, architecture, UI. Lives at the scene root. Never moves.
//   PLAYER — XR Origin > Camera Offset > { Camera, LeftHand, RightHand }. Moves through it.
//
// The camera's pose is written ONLY by TrackedPoseDriver, from the runtime. Locomotion
// moves the XR ORIGIN. Nothing else may write the camera transform.

using UnityEngine;
using UnityEngine.SpatialTracking;
using Unity.XR.CoreUtils;

namespace DreamCodeVRPlus
{
    public static class DcvrXrRig
    {
        public sealed class Rig
        {
            public XROrigin Origin;
            public Transform OriginTransform;
            public Transform CameraOffset;
            public Camera Head;
            public Transform LeftHand;
            public Transform RightHand;
        }

        /// <summary>Build XR Origin > Camera Offset > (Camera, hands) using the supported
        /// components, and hand the camera pose to the runtime.</summary>
        public static Rig Build(Camera cam)
        {
            if (cam == null) { return null; }

            var originGo = new GameObject("XR Origin");
            originGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            // worldPositionStays:false — the camera is about to be driven by the runtime,
            // so any preserved world pose would be stale within one frame.
            cam.transform.SetParent(offsetGo.transform, worldPositionStays: false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.tag = "MainCamera";

            // THE FIX. Without this the camera never receives the head pose.
            // UpdateAndBeforeRender applies it again immediately before rendering, which is
            // what keeps latency low enough to be comfortable.
            AddPoseDriver(cam.gameObject, TrackedPoseDriver.TrackedPose.Center);

            Transform left = BuildHand(offsetGo.transform, "LeftHand Controller",
                                       TrackedPoseDriver.TrackedPose.LeftPose);
            Transform right = BuildHand(offsetGo.transform, "RightHand Controller",
                                        TrackedPoseDriver.TrackedPose.RightPose);

            // An untracked controller leaves its transform at the rig origin — i.e. inside
            // the wearer's head — so the model must follow the TRACKING state, not merely
            // exist. See DcvrHandVisibility.
            DcvrHandVisibility.Attach(left, UnityEngine.XR.XRNode.LeftHand);
            DcvrHandVisibility.Attach(right, UnityEngine.XR.XRNode.RightHand);

            // XROrigin owns the tracking-origin mode. Floor means the runtime reports poses
            // relative to the physical floor of the guardian, so the wearer's real standing
            // height becomes their eye height — no Y offset of our own, and none wanted:
            // combining Floor mode with a manual camera offset is what produces people
            // floating or sunk into the ground.
            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offsetGo;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            Debug.Log("[DcvrXrRig] built XR Origin > Camera Offset > Camera(+TrackedPoseDriver)");

            return new Rig
            {
                Origin = origin,
                OriginTransform = originGo.transform,
                CameraOffset = offsetGo.transform,
                Head = cam,
                LeftHand = left,
                RightHand = right,
            };
        }

        private static Transform BuildHand(Transform parent, string name,
                                           TrackedPoseDriver.TrackedPose pose)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AddPoseDriver(go, pose);

            // A slim emissive pointer. Its parallax against the world is the fastest
            // way for a wearer to confirm positional tracking is live.
            var body = DcvrPrim.Create(PrimitiveType.Cube, "body");
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.035f, 0.035f, 0.10f);

            var tip = DcvrPrim.Create(PrimitiveType.Cube, "tip");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.13f);
            tip.transform.localScale = new Vector3(0.012f, 0.012f, 0.16f);

            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            if (holo != null)
            {
                var m = new Material(holo) { name = "DCVR_HandMat" };
                m.SetColor("_Color", DcvrWorld.Cyan);
                m.SetFloat("_Alpha", 0.8f);
                body.GetComponent<Renderer>().sharedMaterial = m;
                tip.GetComponent<Renderer>().sharedMaterial = m;
            }
            return go.transform;
        }

        private static void AddPoseDriver(GameObject go, TrackedPoseDriver.TrackedPose pose)
        {
            var tpd = go.AddComponent<TrackedPoseDriver>();
            tpd.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, pose);
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }
    }
}
