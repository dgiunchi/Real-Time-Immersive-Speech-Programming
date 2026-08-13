// DreamCodeVR+ — walking around the world.
//
// Left stick moves, right stick snap-turns. Deliberately the comfortable industry
// default rather than the impressive one:
//
//   * SNAP turning, not smooth. Continuous yaw is the single strongest trigger for
//     simulator sickness; a discrete 30-degree step gives no sustained rotational
//     optic flow at all.
//   * A comfort vignette that closes only WHILE translating and opens the instant the
//     stick returns to centre.
//   * Movement is head-relative on the horizontal plane only — looking up must never
//     fly the wearer into the sky.
//
// Architecturally this moves the RIG ROOT and never the camera. The camera pose belongs
// to the headset; writing to it fights tracking and is a well-known way to make people
// ill. The distinction is also the one the perceptual-safety work depends on — the user
// frame IS the rig root, so displacement of the wearer is measurable in exactly one place.
//
// Input goes through UnityEngine.XR.InputDevices rather than the Input System package:
// it ships with the engine, needs no extra dependency or action-map asset, and works
// directly against the OpenXR device layer.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrLocomotion : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 2.2f;          // brisk walk; running in VR is rarely comfortable
        public float snapDegrees = 30f;
        public float deadZone = 0.18f;          // thumbsticks rest noisily off-centre
        public float playRadius = 42f;          // soft bound so the wearer cannot get lost

        [Header("Comfort")]
        public bool comfortVignette = true;
        public float vignetteStrength = 0.55f;

        private Transform _rig;
        private Transform _head;
        private bool _snapArmed = true;
        private float _vignette;
        private Material _vignetteMat;
        private Transform _vignetteQuad;

        private static readonly List<InputDevice> Devices = new List<InputDevice>();

        public static DcvrLocomotion Attach(Transform rigRoot, Camera head)
        {
            if (rigRoot == null || head == null) { return null; }
            var go = new GameObject("DCVR_Locomotion");
            go.transform.SetParent(rigRoot, false);
            var l = go.AddComponent<DcvrLocomotion>();
            l._rig = rigRoot;
            l._head = head.transform;
            l.BuildVignette(head);
            return l;
        }

        private void BuildVignette(Camera head)
        {
            if (!comfortVignette) { return; }
            Shader s = Shader.Find("DreamCodeVRPlus/Vignette");
            if (s == null)
            {
                Debug.LogWarning("[DcvrLocomotion] vignette shader missing; comfort tunnel disabled");
                return;
            }
            var quad = DcvrPrim.Create(PrimitiveType.Quad);
            quad.name = "DCVR_ComfortVignette";
            quad.transform.SetParent(head.transform, false);
            // Just beyond the near plane, sized to over-cover the field of view so the
            // edges never peel away from the periphery when the head rolls.
            quad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            quad.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
            _vignetteMat = new Material(s) { name = "DCVR_VignetteMat" };
            _vignetteMat.SetFloat("_Amount", 0f);
            quad.GetComponent<Renderer>().sharedMaterial = _vignetteMat;
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // Start OFF. This is the only renderer parented to the camera, so it is the
            // only thing that can ever be drawn on the wearer's eye; it earns its place
            // frame by frame rather than by default.
            quad.SetActive(false);
            _vignetteQuad = quad.transform;
        }

        private void Update()
        {
            if (_rig == null || _head == null) { return; }
            float dt = Time.deltaTime;

            Vector2 move = ReadAxis(XRNode.LeftHand);
            Vector2 turn = ReadAxis(XRNode.RightHand);

            // ---- translate (head-relative, horizontal only) ----
            float mag = move.magnitude;
            if (mag > deadZone)
            {
                Vector3 fwd = _head.forward;
                Vector3 right = _head.right;
                fwd.y = 0f; right.y = 0f;
                fwd.Normalize(); right.Normalize();

                Vector3 delta = (fwd * move.y + right * move.x) * (moveSpeed * dt);
                Vector3 next = _rig.position + delta;

                // Soft bound: past the play radius, only motion back toward the centre is
                // accepted. Nothing teleports and nothing is snatched away from the wearer.
                Vector2 flat = new Vector2(next.x, next.z);
                if (flat.magnitude > playRadius)
                {
                    Vector2 cur = new Vector2(_rig.position.x, _rig.position.z);
                    if (flat.magnitude > cur.magnitude) { delta = Vector3.zero; }
                }
                _rig.position += delta;
            }

            // ---- snap turn ----
            if (Mathf.Abs(turn.x) > 0.7f)
            {
                if (_snapArmed)
                {
                    // Rotate the rig AROUND THE HEAD, not around the rig origin, or the
                    // wearer is swung sideways through an arc instead of turning on the spot.
                    Vector3 pivot = new Vector3(_head.position.x, _rig.position.y, _head.position.z);
                    _rig.RotateAround(pivot, Vector3.up, Mathf.Sign(turn.x) * snapDegrees);
                    _snapArmed = false;
                }
            }
            else if (Mathf.Abs(turn.x) < 0.4f)
            {
                _snapArmed = true;   // hysteresis: one turn per deliberate flick
            }

            // ---- comfort vignette ----
            if (_vignetteMat != null)
            {
                float want = (mag > deadZone) ? vignetteStrength * Mathf.Clamp01(mag) : 0f;
                // Open faster than it closes: the tunnel should never linger after the
                // wearer has stopped, or it reads as a fault rather than a comfort aid.
                float rate = want > _vignette ? 4f : 7f;
                _vignette = Mathf.MoveTowards(_vignette, want, rate * dt);
                _vignetteMat.SetFloat("_Amount", _vignette);
                if (_vignetteQuad != null)
                {
                    _vignetteQuad.gameObject.SetActive(_vignette > 0.001f);
                }
            }
        }

        /// <summary>Primary 2D axis of a hand, or zero if that controller is absent.
        /// Absence is normal — hand tracking, a dropped controller, or the editor — and
        /// must degrade to "no input" rather than throwing.</summary>
        private static Vector2 ReadAxis(XRNode node)
        {
            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, Devices);
            for (int i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 v))
                {
                    return v;
                }
            }
            return Vector2.zero;
        }
    }
}
