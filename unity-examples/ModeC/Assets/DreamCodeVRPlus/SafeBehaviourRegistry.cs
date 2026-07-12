using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// The allow-list. Each method maps ONE action type to a vetted UnityEngine
    /// operation, re-clamping numeric values to <see cref="ProtocolModels"/>.
    /// No reflection, no System.IO, no System.Net, no dynamic/compiled code.
    /// </summary>
    public static class SafeBehaviourRegistry
    {
        public static bool SetColor(GameObject go, string hex)
        {
            if (go == null || string.IsNullOrEmpty(hex))
            {
                return false;
            }
            if (!ColorUtility.TryParseHtmlString(hex, out Color c))
            {
                return false;
            }
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return false;
            }
            var mat = renderer.material;
            mat.color = c; // built-in pipeline (_Color)
            // URP/HDRP use _BaseColor; set it too so the change is visible on modern
            // render pipelines (and on Quest, which uses URP).
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }
            return true;
        }

        public static bool SetScale(GameObject go, float value)
        {
            if (go == null)
            {
                return false;
            }
            float v = Mathf.Clamp(value, ProtocolModels.ScaleMin, ProtocolModels.ScaleMax);
            go.transform.localScale = Vector3.one * v;
            return true;
        }

        public static bool Move(GameObject go, string axis, string mode, float speed, float amplitude)
        {
            if (go == null)
            {
                return false;
            }
            var mover = go.GetComponent<SafeMover>() ?? go.AddComponent<SafeMover>();
            mover.Configure(
                ParseAxis(axis),
                mode == "oscillate",
                Mathf.Clamp(speed, 0f, ProtocolModels.MoveSpeedMax),
                Mathf.Clamp(amplitude, 0f, ProtocolModels.MoveAmplitudeMax));
            return true;
        }

        public static bool Rotate(GameObject go, string axis, float degPerSec)
        {
            if (go == null)
            {
                return false;
            }
            var rotator = go.GetComponent<SafeRotator>() ?? go.AddComponent<SafeRotator>();
            rotator.Configure(ParseAxis(axis), Mathf.Clamp(degPerSec, 0f, ProtocolModels.RotateDegPerSecMax));
            return true;
        }

        public static bool SpawnPrimitive(
            string shape, int count, string parent,
            GameObject target, GameObject sceneRoot, GeneratedObjectTracker tracker)
        {
            int n = Mathf.Clamp(count, 1, ProtocolModels.MaxSpawnCount);
            if (tracker != null && !tracker.CanSpawn(n))
            {
                Debug.LogWarning("[SafeBehaviourRegistry] session spawn cap reached; refusing.");
                return false;
            }

            if (!TryParseShape(shape, out PrimitiveType type))
            {
                return false;
            }

            Transform parentT = (parent == "scene_root" ? sceneRoot : target)?.transform;
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(type);
                if (parentT != null)
                {
                    go.transform.SetParent(parentT, false);
                }
                go.transform.localPosition = new Vector3(i * 1.1f, 0f, 0f);
                tracker?.Track(go);
            }
            return true;
        }

        public static bool SetPhysics(GameObject go, bool gravity, float mass)
        {
            if (go == null)
            {
                return false;
            }
            var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
            rb.useGravity = gravity;
            rb.mass = Mathf.Clamp(mass, ProtocolModels.PhysicsMassMin, ProtocolModels.PhysicsMassMax);
            return true;
        }

        private static bool TryParseShape(string shape, out PrimitiveType type)
        {
            switch (shape)
            {
                case "cube": type = PrimitiveType.Cube; return true;
                case "sphere": type = PrimitiveType.Sphere; return true;
                case "capsule": type = PrimitiveType.Capsule; return true;
                case "cylinder": type = PrimitiveType.Cylinder; return true;
                case "plane": type = PrimitiveType.Plane; return true;
                case "quad": type = PrimitiveType.Quad; return true;
                default: type = PrimitiveType.Cube; return false;
            }
        }

        private static Vector3 ParseAxis(string axis)
        {
            switch (axis)
            {
                case "x": return Vector3.right;
                case "z": return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private static void TrySetGameTag(GameObject go)
        {
            // The original DreamCodeVR uses the "game" tag; if the project has not
            // defined it, assigning would throw — so we guard defensively.
            try
            {
                // tag intentionally NOT set (avoids "Tag not defined" errors; provenance is the GeneratedMarker).
            }
            catch (UnityEngine.UnityException)
            {
                // Tag "game" not defined in TagManager; safe to ignore for the skeleton.
            }
        }
    }

    /// <summary>Prebuilt, bounded mover. NOT runtime-compiled.</summary>
    public sealed class SafeMover : MonoBehaviour
    {
        private Vector3 _axis = Vector3.up;
        private bool _oscillate;
        private float _speed = 1f;
        private float _amplitude = 1f;
        private Vector3 _origin;
        private bool _hasOrigin;

        public void Configure(Vector3 axis, bool oscillate, float speed, float amplitude)
        {
            _axis = axis;
            _oscillate = oscillate;
            _speed = speed;
            _amplitude = amplitude;
            if (!_hasOrigin)
            {
                _origin = transform.position;
                _hasOrigin = true;
            }
        }

        private void Update()
        {
            if (_oscillate)
            {
                transform.position = _origin + _axis * (Mathf.Sin(Time.time * _speed) * _amplitude);
            }
            else
            {
                transform.position += _axis * (_speed * Time.deltaTime);
            }
        }
    }

    /// <summary>Prebuilt, bounded rotator. NOT runtime-compiled.</summary>
    public sealed class SafeRotator : MonoBehaviour
    {
        private Vector3 _axis = Vector3.up;
        private float _degPerSec;

        public void Configure(Vector3 axis, float degPerSec)
        {
            _axis = axis;
            _degPerSec = degPerSec;
        }

        private void Update()
        {
            transform.Rotate(_axis, _degPerSec * Time.deltaTime);
        }
    }
}
