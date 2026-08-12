// DreamCodeVR+ — explicit XR bring-up and a real room-scale rig.
//
// WHY THIS EXISTS (root cause of the flat build, recorded so it is not repeated):
//
//   1. XR was detected ONCE, in Start(). XR Management finishes InitializeLoader() and
//      StartSubsystems() asynchronously, so XRSettings.loadedDeviceName is still empty at
//      that moment. The old code therefore committed permanently to its non-XR fallback.
//   2. That fallback then WROTE cam.transform directly. Under a live XR runtime this
//      fights head tracking every frame and produces precisely the reported symptom — an
//      image that follows the head instead of a world that stays put.
//   3. There was no XR Origin and no floor tracking origin, so even when tracking did
//      apply, physical steps were not anchored to the play space and eye height was wrong.
//
// The fix is to WAIT for the runtime, verify it, and never touch the tracked camera.
//
// It also logs its own state under a [DcvrXR] tag. The previous "stereo swapchain
// confirmed" note was read from Horizon Shell's log lines (package=com.oculus.vrshell),
// not from this application — filter logcat by THIS process before believing any of it.

using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace DreamCodeVRPlus
{
    public sealed class DcvrXr : MonoBehaviour
    {
        /// <summary>Standing eye height used ONLY when there is no XR runtime (editor,
        /// look-dev). Under XR the wearer's own guardian setup supplies this.</summary>
        public const float FallbackEyeHeight = 1.6f;

        public static bool DeviceActive =>
            XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName);

        /// <summary>The XR Origin: the play-space anchor. The camera is a CHILD of this and
        /// its local pose belongs entirely to the runtime. Locomotion moves this, never the
        /// camera — which is also the user-frame invariant the safety work depends on.</summary>
        public static Transform Origin { get; private set; }

        private System.Action<bool> _onReady;

        public static DcvrXr Boot(System.Action<bool> onReady)
        {
            var go = new GameObject("DCVR_XR");
            DontDestroyOnLoad(go);
            var x = go.AddComponent<DcvrXr>();
            x._onReady = onReady;
            x.StartCoroutine(x.Run());
            return x;
        }

        private IEnumerator Run()
        {
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                // No XR Management settings reached the player at all. This is a BUILD
                // configuration failure, not a device problem, and it is worth shouting
                // about: everything downstream will silently look like a flat app.
                Debug.LogError("[DcvrXR] XRGeneralSettings missing from the build — " +
                               "XR Plug-in Management was not baked in. Flat fallback.");
                Finish(false);
                yield break;
            }

            XRManagerSettings manager = settings.Manager;

            if (manager.activeLoader == null)
            {
                Debug.Log("[DcvrXR] initialising XR loader…");
                yield return manager.InitializeLoader();
            }

            if (manager.activeLoader == null)
            {
                Debug.LogError("[DcvrXR] no XR loader initialised — OpenXR failed to start. " +
                               "Check the OpenXR runtime and that an interaction profile is " +
                               "enabled for Android. Flat fallback.");
                Finish(false);
                yield break;
            }

            manager.StartSubsystems();
            Debug.Log($"[DcvrXR] loader active: {manager.activeLoader.name}");

            // The display subsystem reports isDeviceActive only once it is actually
            // presenting. Poll briefly rather than assuming — this is the exact check the
            // old one-shot version got wrong.
            const float timeout = 10f;
            float waited = 0f;
            while (!DeviceActive && waited < timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!DeviceActive)
            {
                Debug.LogError($"[DcvrXR] XR did not become active within {timeout:F0}s " +
                               "(loadedDeviceName empty). Flat fallback.");
                Finish(false);
                yield break;
            }

            Debug.Log($"[DcvrXR] XR ACTIVE — device='{XRSettings.loadedDeviceName}' " +
                      $"eyeTexture={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} " +
                      $"stereo={XRSettings.stereoRenderingMode}");

            SetFloorTrackingOrigin();
            Finish(true);
        }

        /// <summary>Floor-relative tracking is what makes room-scale work: the origin sits
        /// on the physical floor of the guardian, so the wearer's real height and real steps
        /// map one-to-one into the world. Device-relative origin would put the floor at
        /// wherever the headset happened to be at startup.</summary>
        private static void SetFloorTrackingOrigin()
        {
            var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (XRInputSubsystem s in subsystems)
            {
                if (s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                {
                    Debug.Log("[DcvrXR] tracking origin = Floor (room-scale)");
                    s.TryRecenter();
                }
                else
                {
                    Debug.LogWarning("[DcvrXR] Floor tracking origin refused; " +
                                     "falling back to whatever the runtime provides");
                }
            }
        }

        /// <summary>Build the origin hierarchy: Origin -> CameraOffset -> Camera. Under a
        /// floor origin the offset stays at zero, because the runtime already reports the
        /// head pose relative to the floor; the node exists so locomotion and any future
        /// height adjustment have somewhere to live that is NOT the tracked camera.</summary>
        public static Transform BuildOrigin(Camera cam)
        {
            if (cam == null) { return null; }

            var origin = new GameObject("DCVR_XROrigin").transform;
            origin.position = Vector3.zero;
            origin.rotation = Quaternion.identity;

            var offset = new GameObject("DCVR_CameraOffset").transform;
            offset.SetParent(origin, false);
            offset.localPosition = Vector3.zero;

            // Re-parent WITHOUT preserving world position: the camera's local pose is
            // about to be driven by the runtime, so any world pose we preserved would be
            // stale by the next frame anyway.
            cam.transform.SetParent(offset, worldPositionStays: false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;

            Origin = origin;
            return origin;
        }

        private void Finish(bool ok)
        {
            _onReady?.Invoke(ok);
            _onReady = null;
        }
    }
}
