using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Runtime ENFORCEMENT of the user-frame invariant (TRACK U2) — the robust answer
    /// to the view/herding and disguise vectors that a lexical C# denylist provably
    /// cannot separate from legitimate creative authoring (an adversarial review of all
    /// six papers confirmed both the under- and over-blocking of a token list). Whereas
    /// the C# validator's `DeployHardened` profile bans only the unambiguous device/rig
    /// APIs, THIS component enforces — at runtime, where it actually knows which object
    /// is the live rig and which objects are authentic — the two invariants that a lexer
    /// cannot:
    ///   1. Generated content may not net-displace the user's frame. Whatever a
    ///      generated `Update()` does to the main camera / rig transform (Optical
    ///      Marionette view-shift, Human-Joystick herd, VPPM tracking offset), the
    ///      guardian re-asserts the app/tracking-owned pose every `LateUpdate`, so the
    ///      tampering is erased before it can accumulate.
    ///   2. Generated content may not hide/forge a registered AUTHENTIC (real-world,
    ///      non-generated) object. Any authentic renderer a generated script disables
    ///      is re-enabled (Fujita transparency / Tseng false-negative).
    ///
    /// It is OPT-IN (`enforceUserFrame`, default false) so ordinary free creation is
    /// untouched — and even when on, it only constrains the live rig and explicitly
    /// REGISTERED authentic objects, never the editable content the user is building
    /// (so "hide the original cube and build a house" still works). The shipped Mode-B
    /// hardened deployment enables it.
    ///
    /// Unity-untested (no headset available); written to the established pattern.
    /// IL2CPP-safe: no reflection, no IO/Net.
    /// </summary>
    public sealed class UserFrameGuardian : MonoBehaviour
    {
        [Tooltip("Enforce the user-frame invariant (re-assert rig pose + protect " +
                 "authentic objects). Default false = free creation untouched. The " +
                 "shipped Mode-B hardened deployment sets this true.")]
        public bool enforceUserFrame;

        [Tooltip("Registered authentic (real-world, non-generated) objects to protect " +
                 "from being hidden by generated code. Empty by default, so the editable " +
                 "content target is never protected and creative 'hide the original' works.")]
        public AuthenticObjectRegistry authentic;

        private Camera _cam;
        private bool _haveAnchor;
        private Vector3 _anchorPos;
        private Quaternion _anchorRot;

        private void EnsureCamera()
        {
            if (_cam == null)
            {
                _cam = Camera.main;
            }
        }

        /// <summary>
        /// Refresh the app/tracking-owned anchor pose. In a full VR build this is called
        /// each frame with the XR-tracked head pose (so real head motion is preserved and
        /// only GENERATED deltas are erased); in the editor/demo it captures the app's
        /// own camera pose once. Call when you trust the current pose (e.g. before a plan).
        /// </summary>
        public void CaptureAnchor()
        {
            EnsureCamera();
            if (_cam == null)
            {
                return;
            }
            _anchorPos = _cam.transform.position;
            _anchorRot = _cam.transform.rotation;
            _haveAnchor = true;
        }

        private void Start()
        {
            CaptureAnchor();
        }

        private void LateUpdate()
        {
            if (!enforceUserFrame)
            {
                return; // free creation: do not touch the rig or any object.
            }

            // (1) Re-assert the user-frame pose: erase any generated displacement of the
            // live camera/rig accrued during this frame's Update().
            EnsureCamera();
            if (_cam != null)
            {
                if (!_haveAnchor)
                {
                    CaptureAnchor();
                }
                else if (_cam.transform.position != _anchorPos
                         || _cam.transform.rotation != _anchorRot)
                {
                    _cam.transform.SetPositionAndRotation(_anchorPos, _anchorRot);
                    PerceptualDisclosure.Disclose(
                        "user-frame",
                        "reverted generated displacement of the user's view/rig",
                        0f);
                }
            }

            // (2) Protect registered authentic objects from being hidden/forged.
            if (authentic != null && authentic.authentic != null)
            {
                var list = authentic.authentic;
                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null || go.GetComponent<GeneratedMarker>() != null)
                    {
                        continue; // only protect real, non-generated objects.
                    }
                    if (!go.activeSelf)
                    {
                        go.SetActive(true);
                        PerceptualDisclosure.Disclose(
                            "authentic-hide",
                            $"re-activated authentic '{go.name}' hidden by generated code",
                            0f);
                    }
                    var r = go.GetComponent<Renderer>();
                    if (r != null && !r.enabled)
                    {
                        r.enabled = true;
                        PerceptualDisclosure.Disclose(
                            "authentic-hide",
                            $"re-enabled renderer of authentic '{go.name}' hidden by generated code",
                            0f);
                    }
                }
            }
        }
    }
}
