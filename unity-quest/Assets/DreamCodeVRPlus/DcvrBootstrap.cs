// DreamCodeVR+ — scene bootstrap.
//
// Sits in the saved scene and assembles the PLAYER at runtime. The WORLD is already in
// the scene as real, inspectable GameObjects; this only builds the rig around the wearer
// and wires the diagnostics.
//
// The split matters. The world is authored and saved, so its hierarchy and transforms can
// be read in the Editor. The rig is built at runtime because a TrackedPoseDriver's value
// comes from the component existing when the XR runtime is up, not from being serialised.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrBootstrap : MonoBehaviour
    {
        [Tooltip("Show the 6DoF diagnostic panel. Turn off for the polished build.")]
        public bool showDiagnostics = true;

        private DcvrXrRig.Rig _rig;

        private IEnumerator Start()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            // Wait for the XR runtime BEFORE building the rig. Sampling XR state
            // synchronously in Start() is what produced a flat build previously.
            bool xrReady = false;
            bool done = false;
            DcvrXr.Boot(ok => { xrReady = ok; done = true; });
            while (!done) { yield return null; }

            if (xrReady)
            {
                _rig = DcvrXrRig.Build(cam);
                DcvrLocomotion.Attach(_rig.OriginTransform, cam);
                Debug.Log("[DcvrBootstrap] immersive rig ready");
            }
            else
            {
                // Flat/editor only. Safe to place the camera here precisely because no XR
                // runtime is presenting; under XR this would fight tracking.
                cam.transform.position = new Vector3(0f, 1.7f, 0f);
                cam.transform.rotation = Quaternion.identity;
                Debug.LogWarning("[DcvrBootstrap] no XR runtime — flat fallback");
            }

            if (showDiagnostics)
            {
                Transform[] anchors =
                {
                    Find("NearCube_Yellow"),
                    Find("Tower_Blue"),
                    Find("Platform"),
                };
                string[] names = { "nearYellow", "farTower", "platform" };
                // The panel is placed in WORLD space and parented to nothing, so it stays
                // where it is put and can be walked around.
                DcvrDiagnostics.Attach(_rig, null, new Vector3(0f, 1.6f, 2.5f), anchors, names);
            }
        }

        private static Transform Find(string n)
        {
            GameObject go = GameObject.Find(n);
            return go != null ? go.transform : null;
        }
    }
}
