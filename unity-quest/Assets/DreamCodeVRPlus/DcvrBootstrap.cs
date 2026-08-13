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

        [Tooltip("Production scene: wire the HUD, effects and networked client.")]
        public bool production = false;

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
            cam.farClipPlane = 700f;

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
                // Hands are additive: controllers stay primary, but when they are put down
                // the runtime switches to hand tracking and the wearer must not lose their
                // hands — in VR that reads as the system having stopped.
                DcvrHands.Attach(_rig.OriginTransform);
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

            if (production)
            {
                // HUD and effects live in WORLD space, parented to nothing. They must stay
                // where they are placed so the wearer can walk up to, past and behind them.
                DcvrWorld world = FindAnyObjectByType<DcvrWorld>();

                // The authoring runtime. All three are created at the SCENE ROOT — never
                // under the rig — so nothing the user makes can inherit head or body
                // movement. Built before the client so the first backend message already
                // has somewhere to put its results.
                DcvrGeneratedContent.Ensure();
                DcvrSpatialCompositor.Ensure();
                DcvrGenerationCapture.Ensure();
                DcvrLocalCommands.Ensure();

                // Status panel, moved off the centre line and further out. Straight ahead
                // at 3.4 m is exactly where creations appear, so a panel there is a panel
                // in front of the thing you just asked for.
                var hud = DcvrHud.Build(null, new Vector3(1.25f, 1.62f, 3.9f));
                var fx = DcvrEffects.Attach(null);
                var preview = DcvrCodePreview.Build(null, new Vector3(1.9f, 1.55f, 3.1f));
                DcvrNameShow title = DcvrNameShow.Build(new Vector3(-5.2f, 2.2f, 5.4f), -32f);

                // The networked Mode-C client drives all of the above from real backend
                // decisions. It is created last so the visuals exist before the first
                // message can arrive.
                // Guardrail ring mirrors the security state, so the platform itself
                // reacts to decisions rather than only the panel.
                fx.NearLayer = FindAnyObjectByType<DcvrNearLayer>();

                var client = new GameObject("ModeCNetworkedDemo")
                    .AddComponent<ModeCNetworkedDemo>();
                // Audio is synthesised at startup — no assets to license or ship.
                DcvrAudio.Build(DcvrWorld.CreationZone);
                var signature = DcvrAttackSignature.Attach(fx);
                client.AttachPresentation(world, hud, fx, preview, signature);

                // Onboarding hint, separate from the status panel so it can leave when the
                // user starts building for real (§32, §33).
                DcvrTutorial tutorial = DcvrTutorial.Build(DcvrWorld.CreationZone,
                                                           world != null ? world.Target : null);
                client.AttachTutorial(tutorial);

                DcvrStartup.Run(fx.NearLayer, hud, title.transform);
                Debug.Log("[DcvrBootstrap] production presentation wired");

                // One-shot audit of anything drawn near the eye. Kept in the build: this
                // class of bug shipped three times and is invisible from the desk.
                DcvrNearCameraAudit.Run();
                DcvrPerf.Run();

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
