// DreamCodeVR+ — the effects that make the security architecture visible.
//
// This is the visual half of the dissertation's central claim: XR has two defence
// planes, and the second one — perceptual safety — has no equivalent in ordinary
// application security. An examiner can read that in a paper. In the headset they
// should be able to SEE it:
//
//   Materialize        an approved plan built something. Creation is cheap and calm.
//   Shield             a request was refused. A barrier assembles between the wearer
//                      and the creation area, panel by panel, and nothing is built.
//   PersonalSpace      the user-frame invariant, normally invisible, hardening into a
//                      lattice when something reaches toward the wearer.
//   Shockwave          a ground ring marking the moment a decision landed.
//
// Everything is pooled and short-lived. Transparent overdraw is the expensive thing on
// a standalone headset, so effects are small on screen, few, and always tear down.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrEffects : MonoBehaviour
    {
        private const int ShieldPanels = 24;
        private const float ShieldRadius = 1.9f;

        private Transform _shieldRoot;
        private readonly List<Transform> _shieldPanels = new List<Transform>();
        private readonly List<Material> _shieldMats = new List<Material>();

        private Transform _sphere;
        private Material _sphereMat;

        private Material _shockMat;
        private Transform _shock;

        private Coroutine _shieldRoutine;
        private Coroutine _sphereRoutine;

        /// <summary>Optional near layer whose guardrail ring mirrors the security state.
        /// Set by the bootstrap; the effects work without it.</summary>
        public DcvrNearLayer NearLayer { get; set; }

        public static DcvrEffects Attach(Transform parent)
        {
            var go = new GameObject("DCVR_Effects");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<DcvrEffects>();
            fx.Construct();
            return fx;
        }

        private void Construct()
        {
            BuildShield();
            BuildPersonalSpace();
            BuildShockwave();
        }

        // ---- shield ---------------------------------------------------------------
        /// <summary>A dome of panels between the wearer and the creation area. Built once,
        /// hidden; assembling is a staggered scale-in rather than instantiation, so a block
        /// costs no allocation at the moment it matters.</summary>
        private void BuildShield()
        {
            _shieldRoot = new GameObject("DCVR_Shield").transform;
            _shieldRoot.SetParent(transform, false);
            // Sits just in front of the platform, facing the wearer.
            // Between the wearer and the creation zone, facing them. Anchored to the
            // creation zone rather than the platform centre: the wearer now stands ON the
            // platform, so an offset from its centre put the barrier behind their back.
            _shieldRoot.localPosition = new Vector3(DcvrWorld.CreationZone.x, 1.3f,
                                                    DcvrWorld.CreationZone.z - 1.1f);

            for (int i = 0; i < ShieldPanels; i++)
            {
                // Two staggered rows of panels reading as a hex curtain.
                int row = i / (ShieldPanels / 2);
                int col = i % (ShieldPanels / 2);
                float t = col / (float)(ShieldPanels / 2 - 1);
                float ang = Mathf.Lerp(-38f, 38f, t) * Mathf.Deg2Rad;

                var panel = DcvrPrim.Create(PrimitiveType.Quad);
                panel.name = $"DCVR_ShieldPanel{i}";
                panel.transform.SetParent(_shieldRoot, false);
                panel.transform.localPosition = new Vector3(
                    Mathf.Sin(ang) * ShieldRadius,
                    (row == 0 ? -0.30f : 0.30f) + (col % 2 == 0 ? 0.07f : -0.07f),
                    Mathf.Cos(ang) * ShieldRadius - ShieldRadius);
                panel.transform.localRotation = Quaternion.Euler(0f, -Mathf.Rad2Deg * ang, 0f);
                panel.transform.localScale = Vector3.zero;

                Material m = MakeHolo("DCVR_ShieldMat" + i, DcvrWorld.Red, 0.0f);
                if (m != null)
                {
                    m.SetFloat("_ScanSpeed", 1.6f);
                    m.SetFloat("_ScanDensity", 26f);
                    panel.GetComponent<Renderer>().sharedMaterial = m;
                    _shieldMats.Add(m);
                }
                var r = panel.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _shieldPanels.Add(panel.transform);
            }
            _shieldRoot.gameObject.SetActive(false);
        }

        /// <summary>Raise the barrier. Colour carries the meaning: red for a refusal.</summary>
        public void ShowShield(Color color, float hold = 2.6f)
        {
            NearLayer?.SetGuardrailState(color);
            if (_shieldRoot == null) { return; }
            if (_shieldRoutine != null) { StopCoroutine(_shieldRoutine); }
            _shieldRoutine = StartCoroutine(ShieldRoutine(color, hold));
        }

        private IEnumerator ShieldRoutine(Color color, float hold)
        {
            _shieldRoot.gameObject.SetActive(true);
            foreach (Material m in _shieldMats) { m.SetColor("_Color", color); }

            // Assemble: panels snap in on a stagger. The sequencing is the point — a
            // barrier that simply appears reads as a texture, one that BUILDS reads as
            // a system reacting.
            const float assemble = 0.42f;
            for (int i = 0; i < _shieldPanels.Count; i++)
            {
                float delay = (i / (float)_shieldPanels.Count) * assemble;
                StartCoroutine(PanelIn(_shieldPanels[i], delay));
            }
            yield return new WaitForSeconds(assemble + hold);

            // Dissolve: fade together, faster than the assemble.
            const float fade = 0.45f;
            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                float k = 1f - (t / fade);
                foreach (Material m in _shieldMats) { m.SetFloat("_Alpha", 0.42f * k); }
                for (int i = 0; i < _shieldPanels.Count; i++)
                {
                    Vector3 s = _shieldPanels[i].localScale;
                    _shieldPanels[i].localScale = new Vector3(s.x, s.y * 0.92f, 1f);
                }
                yield return null;
            }
            _shieldRoot.gameObject.SetActive(false);
            _shieldRoutine = null;
        }

        private IEnumerator PanelIn(Transform panel, float delay)
        {
            yield return new WaitForSeconds(delay);
            const float dur = 0.16f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                // Slight overshoot so it lands with weight instead of easing in limply.
                float s = Mathf.Sin(k * Mathf.PI * 0.5f) * 1.06f;
                panel.localScale = new Vector3(0.52f * s, 0.62f * s, 1f);
                yield return null;
            }
            panel.localScale = new Vector3(0.52f, 0.62f, 1f);
        }

        // ---- personal space --------------------------------------------------------
        /// <summary>The user-frame invariant. Invisible until something warrants showing
        /// it — a safety boundary you can always see is just furniture, and it would sit
        /// in the wearer's view for the whole session.</summary>
        private void BuildPersonalSpace()
        {
            var go = DcvrPrim.Create(PrimitiveType.Sphere);
            go.name = "DCVR_PersonalSpace";
            go.transform.SetParent(transform, false);
            // Centred on the wearer, not the world: this is THEIR frame.
            // Centred on the wearer's head, updated each frame in Update — this is THEIR
            // frame, and the rig moves under locomotion.
            go.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            go.transform.localScale = Vector3.one * 2.0f;

            _sphereMat = MakeHolo("DCVR_PersonalSpaceMat", DcvrWorld.Cyan, 0f);
            if (_sphereMat != null)
            {
                _sphereMat.SetFloat("_RimPower", 1.6f);
                _sphereMat.SetFloat("_ScanSpeed", 0.9f);
                _sphereMat.SetFloat("_ScanDensity", 14f);
                go.GetComponent<Renderer>().sharedMaterial = _sphereMat;
            }
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _sphere = go.transform;
            go.SetActive(false);
        }

        /// <summary>Keep the personal-space shell on the wearer. It IS the user frame, so
        /// it must ride the head rather than sit at the world origin — otherwise it
        /// visualises a boundary around a point in space nobody is standing at.
        ///
        /// LateUpdate, so the tracked pose has already been applied this frame. Position
        /// only, never rotation: a shell that pitches and rolls with the head reads as
        /// something stuck to the face instead of a boundary around a person.</summary>
        private void LateUpdate()
        {
            if (_sphere == null || !_sphere.gameObject.activeSelf) { return; }
            Camera cam = Camera.main;
            if (cam == null) { return; }
            Vector3 p = cam.transform.position;
            _sphere.position = new Vector3(p.x, p.y - 0.35f, p.z);
        }

        public void PulsePersonalSpace(Color color, float hold = 1.5f)
        {
            if (_sphere == null || _sphereMat == null) { return; }
            if (_sphereRoutine != null) { StopCoroutine(_sphereRoutine); }
            _sphereRoutine = StartCoroutine(SphereRoutine(color, hold));
        }

        private IEnumerator SphereRoutine(Color color, float hold)
        {
            _sphere.gameObject.SetActive(true);
            _sphereMat.SetColor("_Color", color);

            const float rise = 0.22f;
            float t = 0f;
            while (t < rise)
            {
                t += Time.deltaTime;
                _sphereMat.SetFloat("_Alpha", Mathf.Lerp(0f, 0.22f, t / rise));
                yield return null;
            }
            yield return new WaitForSeconds(hold);
            t = 0f;
            const float fall = 0.5f;
            while (t < fall)
            {
                t += Time.deltaTime;
                _sphereMat.SetFloat("_Alpha", Mathf.Lerp(0.22f, 0f, t / fall));
                yield return null;
            }
            _sphere.gameObject.SetActive(false);
            _sphereRoutine = null;
        }

        // ---- shockwave --------------------------------------------------------------
        private void BuildShockwave()
        {
            var go = DcvrPrim.Create(PrimitiveType.Quad);
            go.name = "DCVR_Shockwave";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = DcvrWorld.PlatformCenter + new Vector3(0f, 0.14f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.zero;
            _shockMat = MakeHolo("DCVR_ShockMat", DcvrWorld.Cyan, 0f);
            if (_shockMat != null)
            {
                _shockMat.SetFloat("_RimPower", 1.2f);
                go.GetComponent<Renderer>().sharedMaterial = _shockMat;
            }
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _shock = go.transform;
            go.SetActive(false);
        }

        public void Shockwave(Color color)
        {
            NearLayer?.SetGuardrailState(color);
            StartCoroutine(ShockRoutine(color));
        }

        private IEnumerator ShockRoutine(Color color)
        {
            if (_shock == null || _shockMat == null) { yield break; }
            _shock.gameObject.SetActive(true);
            _shockMat.SetColor("_Color", color);
            const float dur = 0.85f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                float size = Mathf.Lerp(0.4f, 7.5f, k);
                _shock.localScale = new Vector3(size, size, 1f);
                _shockMat.SetFloat("_Alpha", Mathf.Lerp(0.55f, 0f, k));
                yield return null;
            }
            _shock.gameObject.SetActive(false);
        }

        // ---- materialization --------------------------------------------------------
        /// <summary>Bring a newly created object into existence: a bright shell collapses
        /// onto it while the object itself scales up with a small overshoot.</summary>
        public void Materialize(GameObject target)
        {
            if (target != null) { StartCoroutine(MaterializeRoutine(target)); }
        }

        private IEnumerator MaterializeRoutine(GameObject target)
        {
            Vector3 finalScale = target.transform.localScale;

            var shell = DcvrPrim.Create(PrimitiveType.Sphere);
            shell.name = "DCVR_MaterializeShell";
            shell.transform.SetParent(target.transform.parent, false);
            shell.transform.position = target.transform.position;
            Material sm = MakeHolo("DCVR_MatShell", DcvrWorld.Cyan, 0.5f);
            if (sm != null) { shell.GetComponent<Renderer>().sharedMaterial = sm; }
            var sr = shell.GetComponent<Renderer>();
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            const float dur = 0.55f;
            float t = 0f;
            float maxDim = Mathf.Max(finalScale.x, Mathf.Max(finalScale.y, finalScale.z));
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                // Object grows with a slight overshoot; shell collapses onto it.
                float grow = Mathf.Sin(k * Mathf.PI * 0.5f) * 1.08f;
                target.transform.localScale = finalScale * Mathf.Min(grow, 1.08f);
                float shellSize = Mathf.Lerp(maxDim * 3.4f, maxDim * 1.02f, k);
                shell.transform.localScale = Vector3.one * shellSize;
                if (sm != null) { sm.SetFloat("_Alpha", Mathf.Lerp(0.55f, 0f, k)); }
                yield return null;
            }
            target.transform.localScale = finalScale;
            DestroySafe(shell);
        }

        // ---- helpers -----------------------------------------------------------------
        private static Material MakeHolo(string name, Color color, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null)
            {
                Debug.LogError("[DcvrEffects] Holo shader missing from build");
                return null;
            }
            var m = new Material(s) { name = name };
            m.SetColor("_Color", color);
            m.SetFloat("_Alpha", alpha);
            return m;
        }

        private static void DestroySafe(Object o)
        {
            if (o == null) { return; }
            if (Application.isPlaying) { Destroy(o); } else { DestroyImmediate(o); }
        }
    }
}
