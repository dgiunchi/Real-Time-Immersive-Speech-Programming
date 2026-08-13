// DreamCodeVR+ — per-class visual signatures for a blocked request.
//
// A single red barrier says "refused". It does not say WHY, and the dissertation's central
// claim is that the why matters: XR has two defence planes, and the second one has no
// equivalent in ordinary application security. So the visual is chosen from the reason the
// backend actually gave.
//
//   EXFILTRATION   a data stream leaves the platform and is severed mid-flight.
//   SENSOR         the personal-space shell hardens — something reached for the wearer.
//   PERCEPTUAL     the shell hardens AND the horizon steadies: the user-frame invariant.
//   SPAWN ABUSE    the creation zone clamps shut.
//   GENERIC        the barrier alone.
//
// Every signature is driven by the reason string on the real decision. Nothing here
// invents a threat classification of its own: if the backend did not say it, it is not
// shown, and an unrecognised reason falls back to the generic barrier rather than guessing.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public enum DcvrAttackClass
    {
        Generic,
        Exfiltration,
        Sensor,
        Perceptual,
        SpawnAbuse,
    }

    public sealed class DcvrAttackSignature : MonoBehaviour
    {
        private DcvrEffects _fx;
        private Transform _streamRoot;
        private Material _streamMat;

        public static DcvrAttackSignature Attach(DcvrEffects fx)
        {
            var go = new GameObject("DCVR_AttackSignature");
            var a = go.AddComponent<DcvrAttackSignature>();
            a._fx = fx;
            a.BuildStream();
            return a;
        }

        /// <summary>Map a backend reason to a class. Deliberately conservative: the strings
        /// come from the router's own vocabulary, and anything unrecognised is Generic
        /// rather than a guess.</summary>
        public static DcvrAttackClass Classify(string reason)
        {
            if (string.IsNullOrEmpty(reason)) { return DcvrAttackClass.Generic; }
            string r = reason.ToLowerInvariant();

            if (r.Contains("exfiltrat") || r.Contains("network") || r.Contains("send")
                || r.Contains("upload") || r.Contains("ransomware") || r.Contains("encrypt"))
            {
                return DcvrAttackClass.Exfiltration;
            }
            if (r.Contains("camera") || r.Contains("microphone") || r.Contains("screen")
                || r.Contains("sensor") || r.Contains("record"))
            {
                return DcvrAttackClass.Sensor;
            }
            if (r.Contains("boundary") || r.Contains("chaperone") || r.Contains("guardian")
                || r.Contains("locomot") || r.Contains("viewpoint") || r.Contains("joystick"))
            {
                return DcvrAttackClass.Perceptual;
            }
            if (r.Contains("spawn") || r.Contains("budget") || r.Contains("flood"))
            {
                return DcvrAttackClass.SpawnAbuse;
            }
            return DcvrAttackClass.Generic;
        }

        public void Play(string reason)
        {
            DcvrAttackClass cls = Classify(reason);
            _fx?.ShowShield(DcvrWorld.Red);

            switch (cls)
            {
                case DcvrAttackClass.Exfiltration:
                    StartCoroutine(SeverStream());
                    break;
                case DcvrAttackClass.Sensor:
                    _fx?.PulsePersonalSpace(DcvrWorld.Red, 2.0f);
                    break;
                case DcvrAttackClass.Perceptual:
                    // The one case where the wearer's own frame is the target, so the
                    // shell is the point rather than a garnish.
                    _fx?.PulsePersonalSpace(DcvrWorld.Red, 2.6f);
                    break;
                case DcvrAttackClass.SpawnAbuse:
                    _fx?.Shockwave(DcvrWorld.Red);
                    break;
            }
            Debug.Log($"[DcvrSignature] {cls} — \"{reason}\"");
        }

        /// <summary>A line of segments running from the creation zone out past the platform
        /// edge: content trying to leave. Hidden until an exfiltration attempt is blocked.</summary>
        private void BuildStream()
        {
            _streamRoot = new GameObject("ExfilStream").transform;
            _streamRoot.SetParent(transform, false);
            _streamMat = Holo(DcvrWorld.Red, 0f);

            const int segments = 16;
            for (int i = 0; i < segments; i++)
            {
                var q = DcvrPrim.Create(PrimitiveType.Cube, $"packet{i}");
                q.transform.SetParent(_streamRoot, false);
                float t = i / (float)(segments - 1);
                q.transform.localPosition = Vector3.Lerp(
                    DcvrWorld.CreationZone,
                    DcvrWorld.CreationZone + new Vector3(6.5f, 1.6f, 1.5f), t);
                q.transform.localScale = new Vector3(0.09f, 0.09f, 0.26f);
                q.transform.localRotation = Quaternion.LookRotation(new Vector3(6.5f, 1.6f, 1.5f));
                q.GetComponent<Renderer>().sharedMaterial = _streamMat;
            }
            _streamRoot.gameObject.SetActive(false);
        }

        private IEnumerator SeverStream()
        {
            _streamRoot.gameObject.SetActive(true);

            // Packets run outward…
            const float run = 0.5f;
            float t = 0f;
            while (t < run)
            {
                t += Time.deltaTime;
                _streamMat.SetFloat("_Alpha", Mathf.Lerp(0f, 0.8f, t / run));
                yield return null;
            }

            // …and are cut. The shield is already up; this is the stream failing to cross it.
            const float cut = 0.7f;
            t = 0f;
            while (t < cut)
            {
                t += Time.deltaTime;
                _streamMat.SetFloat("_Alpha", Mathf.Lerp(0.8f, 0f, t / cut));
                yield return null;
            }
            _streamRoot.gameObject.SetActive(false);
        }

        private static Material Holo(Color c, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return null; }
            var m = new Material(s) { name = "DCVR_SignatureMat" };
            m.SetColor("_Color", c);
            m.SetFloat("_Alpha", alpha);
            m.SetFloat("_ScanSpeed", 2.2f);
            return m;
        }
    }
}
