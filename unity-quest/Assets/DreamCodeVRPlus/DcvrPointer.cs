// DreamCodeVR+ — pointing at something so you can say "delete this".
//
// A deictic command needs a referent. Without one, "make this red" has to be answered by
// a language model guessing from context, which is both slower and wrong more often than
// simply knowing what the user is aiming at.
//
// DELIBERATELY NOT AN EYE BEAM (§29). An earlier build in this project drew geometry with
// no valid pose at the rig origin, which put a large cyan block directly on the wearer's
// eye and made the headset unusable. The lessons are baked into the code below rather
// than left as a warning:
//
//   * the ray ORIGINATES AT THE CONTROLLER, never at the camera;
//   * it is not drawn at all until the controller reports a pose far enough from the rig
//     origin to be real — the same geometric test `DcvrHandVisibility` uses, because the
//     `isTracked` flag was observed lying;
//   * the line starts 3 cm ahead of the controller and is 4 mm thick, so even in the worst
//     case there is nothing large near anyone's face.
//
// It only ever targets generated content. Pointing at the floor, the platform or the city
// selects nothing, which means "delete this" cannot be aimed at the application.

using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrPointer : MonoBehaviour
    {
        private const float MinPoseDistance = 0.12f;   // below this the pose is not real
        private const float MaxRange = 12f;
        private const float LineStart = 0.03f;
        private const float LineWidth = 0.004f;

        private XRNode _node;
        private LineRenderer _line;
        private Transform _dot;
        private GameObject _hit;

        // Re-cast a few times a second, not every frame: this drives a selection, and a
        // 10 Hz selection is indistinguishable from a 72 Hz one to a person holding a
        // controller (§73).
        private const float CastInterval = 0.1f;
        private float _nextCast;

        /// <summary>Attach the ray, or quietly do nothing.
        ///
        /// Wrapped because this runs inside rig construction: a pointer that cannot build
        /// its material must not be able to leave the wearer with no controllers.</summary>
        public static DcvrPointer Attach(Transform hand, XRNode node)
        {
            try
            {
                var p = hand.gameObject.AddComponent<DcvrPointer>();
                p._node = node;
                if (!p.Build())
                {
                    Destroy(p);
                    return null;
                }
                return p;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DcvrPointer] disabled: {e.Message}");
                return null;
            }
        }

        /// <summary>Resolve a shader that actually exists in THIS build.
        ///
        /// `Shader.Find("Unlit/Color")` returns null under IL2CPP: the built-in shader is
        /// stripped because nothing in the project references it, and `new Material(null)`
        /// throws. The first version of this class did exactly that, and because it threw
        /// from inside `DcvrXrRig.Build`, it took the ENTIRE rig down with it — no
        /// controllers, no hands, on device only. A cosmetic detail should not be able to
        /// break tracking, so the caller treats a missing shader as "no pointer" rather
        /// than as an error.</summary>
        private static Material MakeLineMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Sprites/Default");
            if (sh == null) { return null; }
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
            return m;
        }

        private bool Build()
        {
            Material lineMat = MakeLineMaterial(new Color(0.35f, 0.85f, 1f, 1f));
            Material dotMat = MakeLineMaterial(new Color(0.45f, 0.95f, 1f, 1f));
            if (lineMat == null || dotMat == null)
            {
                Debug.LogWarning("[DcvrPointer] no usable unlit shader in this build — "
                                 + "selection ray disabled (tracking is unaffected)");
                return false;
            }

            var go = new GameObject("DCVR_PointerLine");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.positionCount = 2;
            _line.startWidth = LineWidth;
            _line.endWidth = LineWidth * 0.6f;
            _line.material = lineMat;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;

            GameObject dot = DcvrPrim.Create(PrimitiveType.Sphere);
            dot.name = "DCVR_PointerDot";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.02f;
            var r = dot.GetComponent<Renderer>();
            r.sharedMaterial = dotMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dot = dot.transform;
            _dot.gameObject.SetActive(false);
            return true;
        }

        /// <summary>The controller has a real pose. Same geometric test as
        /// `DcvrHandVisibility`: a pose sitting at the rig origin is the untracked default,
        /// and drawing from it is what put geometry on the wearer's eye.</summary>
        private bool HasRealPose()
        {
            if (transform.localPosition.magnitude < MinPoseDistance) { return false; }
            Camera cam = Camera.main;
            if (cam != null && Vector3.Distance(transform.position, cam.transform.position) < MinPoseDistance)
            {
                return false;
            }
            InputDevice d = InputDevices.GetDeviceAtXRNode(_node);
            return !d.isValid || !d.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || tracked;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCast) { return; }
            _nextCast = Time.unscaledTime + CastInterval;

            if (!HasRealPose())
            {
                SetVisible(false);
                return;
            }

            Vector3 origin = transform.position + transform.forward * LineStart;
            GameObject found = PickFromRegistry(origin, transform.forward, out float dist);
            _hit = found;
            SetVisible(true);
            _line.SetPosition(0, new Vector3(0f, 0f, LineStart));
            _line.SetPosition(1, new Vector3(0f, 0f, LineStart + dist));
            _dot.gameObject.SetActive(found != null);
            if (found != null) { _dot.position = origin + transform.forward * dist; }

            DcvrGeneratedContent content = DcvrGeneratedContent.Instance;
            if (content != null) { content.PointedObject = found; }
        }

        private void SetVisible(bool on)
        {
            if (_line != null) { _line.enabled = on; }
            if (!on)
            {
                if (_dot != null) { _dot.gameObject.SetActive(false); }
                if (_hit != null)
                {
                    _hit = null;
                    DcvrGeneratedContent content = DcvrGeneratedContent.Instance;
                    if (content != null && content.PointedObject != null) { content.PointedObject = null; }
                }
            }
        }

        /// <summary>Nearest generated object whose bounds the ray enters.
        ///
        /// Bounds, not `Physics.Raycast`, for three reasons that all point the same way:
        /// `DcvrPrim` deliberately builds primitives WITHOUT colliders (they cost memory
        /// for a scene that simulates nothing, and the collider classes were stripped from
        /// this build for a long time); generated code may or may not attach any; and a
        /// physics ray would happily hit the floor, the platform and the city, which must
        /// never be selectable. Testing the registry means the pointer can only ever
        /// return the user's own work — the scoping is structural rather than a filter
        /// applied afterwards.
        ///
        /// The registry is bounded by the session spawn cap and this runs at 10 Hz, so the
        /// linear scan is not worth indexing.</summary>
        private static GameObject PickFromRegistry(Vector3 origin, Vector3 dir, out float distance)
        {
            distance = MaxRange;
            DcvrGeneratedContent content = DcvrGeneratedContent.Instance;
            if (content == null) { return null; }

            var ray = new Ray(origin, dir);
            GameObject best = null;
            float bestT = MaxRange;

            foreach (GameObject go in content.AllObjects)
            {
                if (!DcvrSpatialCompositor.TryGetBounds(go.transform, out Bounds b)) { continue; }
                if (!b.IntersectRay(ray, out float t)) { continue; }
                if (t < 0f || t >= bestT) { continue; }
                bestT = t;
                best = go;
            }

            if (best != null) { distance = bestT; }
            return best;
        }
    }
}
