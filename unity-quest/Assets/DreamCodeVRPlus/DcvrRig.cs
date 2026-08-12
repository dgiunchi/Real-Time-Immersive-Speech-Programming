// DreamCodeVR+ — camera setup for the headset.
//
// In a stereo XR build the HMD owns the camera pose: OpenXR drives it through a tracked
// pose driver, and the app must not write to that transform. Doing so fights head
// tracking and is a well-known cause of discomfort, so the camera is only positioned
// when XR is NOT active (editor / flat build), and is otherwise left alone.
//
// The rig follows the standard XR layout: a "rig root" at floor level with the camera
// as a child at eye height. Moving the ROOT is safe; moving the camera is not. That
// distinction is also what the perceptual-safety work depends on — UserFrameGuardian
// reasons about displacement of the user's frame, which is the rig root.

using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public static class DcvrRig
    {
        /// <summary>Approximate standing eye height, used only for the non-XR fallback.
        /// Under XR the runtime supplies the real value from the wearer's setup.</summary>
        private const float FallbackEyeHeight = 1.6f;

        public static bool XrActive =>
            XRSettings.enabled && !string.IsNullOrEmpty(XRSettings.loadedDeviceName);

        public static void Configure(Camera cam, DcvrWorld world)
        {
            if (cam == null)
            {
                Debug.LogWarning("[DcvrRig] no main camera; skipping rig setup");
                return;
            }

            // Dark clear colour behind the skybox so any gap reads as space, not as the
            // default blue that instantly says "untouched Unity project".
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.01f, 0.02f, 0.04f);
            // Near plane tight enough to lean into the platform; far plane only as far as
            // the fog, since anything beyond it is invisible and would just cost fill.
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;

            if (XrActive)
            {
                EnsureRigRoot(cam);
                Debug.Log($"[DcvrRig] XR active ({XRSettings.loadedDeviceName}) — " +
                          "head tracking owns the camera pose");
                return;
            }

            // Flat / editor: place a fixed viewpoint looking at the platform so the same
            // scene is inspectable without a headset (this is what the offscreen
            // look-dev renders capture).
            cam.transform.position = new Vector3(0f, FallbackEyeHeight, -5.2f);
            cam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            Debug.Log("[DcvrRig] XR inactive — flat fallback viewpoint");
        }

        /// <summary>Give the camera a rig root if it has none, so later work can displace
        /// the user's frame without ever touching the tracked camera transform.</summary>
        private static void EnsureRigRoot(Camera cam)
        {
            if (cam.transform.parent != null) { return; }
            var root = new GameObject("DCVR_RigRoot");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            cam.transform.SetParent(root.transform, worldPositionStays: true);
        }
    }
}
