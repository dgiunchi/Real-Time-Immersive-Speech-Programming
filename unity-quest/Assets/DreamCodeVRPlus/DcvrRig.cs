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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public static class DcvrRig
    {
        /// <summary>Approximate standing eye height, used only for the non-XR fallback.
        /// Under XR the runtime supplies the real value from the wearer's setup.</summary>
        private const float FallbackEyeHeight = 1.6f;

        /// <summary>Live XR state. Do NOT sample this once at Start() to decide the rig —
        /// the runtime is not up yet at that point. DcvrXr owns that decision.</summary>
        public static bool XrActive => DcvrXr.DeviceActive;

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
            cam.farClipPlane = 700f;

            EnablePostProcessing(cam);
            EnsureBloom();

            // XR bring-up is ASYNCHRONOUS. Deciding here, synchronously, is exactly the
            // bug that produced a flat build: the loader had not finished, so this took
            // the fallback branch and then wrote the camera transform, fighting head
            // tracking every frame. Hand the decision to DcvrXr, which waits for the
            // runtime and reports honestly either way.
            DcvrXr.Boot(xrOk =>
            {
                if (xrOk)
                {
                    // DcvrXrRig owns the rig (XROrigin + TrackedPoseDriver + hands). This
                    // path exists only for scenes that have no bootstrap of their own.
                    DcvrXrRig.Rig rig = DcvrXrRig.Build(cam);
                    DcvrLocomotion.Attach(rig.OriginTransform, cam);
                    Debug.Log("[DcvrRig] immersive: XR rig built, camera is runtime-driven");
                }
                else
                {
                    // Flat / editor only. Writing the camera transform is safe HERE
                    // precisely because no XR runtime is presenting.
                    cam.transform.position = new Vector3(0f, DcvrXr.FallbackEyeHeight, -5.2f);
                    cam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
                    Debug.Log("[DcvrRig] no XR runtime — flat fallback viewpoint");
                }
            });
        }

        /// <summary>Bloom is what turns flat emissive geometry into something that reads as
        /// light. It is the one post-process worth its cost here; SSAO, depth of field and
        /// motion blur are all either expensive on a mobile GPU or actively uncomfortable
        /// in a headset, so none of them are enabled.</summary>
        public static void EnsureBloom()
        {
            if (Object.FindAnyObjectByType<Volume>() != null) { return; }

            var go = new GameObject("DCVR_PostVolume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DCVR_PostProfile";

            var bloom = profile.Add<Bloom>(overrides: true);
            // Threshold below 1 so the cyan emissives bloom without HDR enabled — the
            // scene is deliberately LDR to save bandwidth on a standalone headset.
            bloom.threshold.Override(0.62f);
            bloom.intensity.Override(1.15f);
            bloom.scatter.Override(0.62f);
            // Near-neutral. The cyan tint suited a world that was entirely holographic, but
            // generated content now carries real material colours — a coloured bloom would
            // drag a gold lamp toward cyan and undo the semantic palette. A trace of warmth
            // keeps it from looking clinical.
            bloom.tint.Override(new Color(1.00f, 0.97f, 0.94f));
            // High quality filtering costs extra samples; the look does not need it and
            // fill rate is the scarce resource here.
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Half);

            var grading = profile.Add<ColorAdjustments>(overrides: true);
            grading.postExposure.Override(0.15f);
            grading.contrast.Override(12f);
            // A little more saturation than before: the point of the material system is
            // that a red roof reads as red, and mobile LDR output flattens hues.
            grading.saturation.Override(14f);

            volume.profile = profile;
        }

        private static void EnablePostProcessing(Camera cam)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) { data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>(); }
            data.renderPostProcessing = true;
            // MSAA does the anti-aliasing (cheap on tile-based mobile GPUs); a post AA
            // pass on top would cost fill rate and soften the thin grid lines.
            data.antialiasing = AntialiasingMode.None;
        }

    }
}
