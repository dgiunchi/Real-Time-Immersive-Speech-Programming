// DreamCodeVR+ — the near layer: everything within arm's reach to ~10 m.
//
// This layer is what sells presence. Distant architecture reads as a backdrop no matter
// how large it is, because at 40 m a head movement produces almost no parallax. Objects
// at 2-8 m shift strongly against that backdrop every time the wearer leans, and that
// relative motion is the single strongest depth cue a headset can deliver.
//
// It is also where the platform's identity lives: the rim, the pylons and the guardrail
// ring are the things close enough to read detail on.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrNearLayer : MonoBehaviour
    {
        private readonly List<Material> _pylonMats = new List<Material>();
        private readonly List<Transform> _emitters = new List<Transform>();
        private Material _guardrailMat;
        private float _phase;

        public static DcvrNearLayer Build(Transform worldRoot)
        {
            var go = new GameObject("NearLayer");
            go.transform.SetParent(worldRoot, false);
            var n = go.AddComponent<DcvrNearLayer>();
            n.Construct();
            return n;
        }

        private const float PlatformTop = 0.12f;   // disc centre 0.06 + half-height 0.06

        private void Construct()
        {
            BuildPlatformRim();
            BuildPylons();
            BuildGuardrailRing();
            BuildCreationPedestal();
        }

        /// <summary>A raised lip around the platform edge. Physically useful as well as
        /// decorative: it gives the wearer a visible cue for where the platform ends,
        /// which matters because they can genuinely walk on it.</summary>
        private void BuildPlatformRim()
        {
            const int segments = 72;
            float r = DcvrWorld.PlatformRadius;
            var rim = new GameObject("PlatformRim").transform;
            rim.SetParent(transform, false);

            Material m = Holo(DcvrWorld.Cyan, 0.85f);
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var seg = DcvrPrim.Create(PrimitiveType.Cube, $"Rim{i}");
                seg.transform.SetParent(rim, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(a) * r, PlatformTop + 0.05f, Mathf.Sin(a) * r);
                seg.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                // Small gaps between segments read as engineered rather than extruded.
                seg.transform.localScale = new Vector3(0.10f, 0.16f, (2f * Mathf.PI * r / segments) * 0.72f);
                seg.GetComponent<Renderer>().sharedMaterial = m;
                seg.isStatic = true;
            }
        }

        /// <summary>Pylons just off the platform. Close enough for strong parallax, spaced
        /// so they never block the creation zone ahead.</summary>
        private void BuildPylons()
        {
            var root = new GameObject("Pylons").transform;
            root.SetParent(transform, false);
            var rng = new System.Random(2231);

            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                // Skip the forward arc: nothing should stand between the wearer and the
                // creation zone.
                float a = (i / (float)count) * Mathf.PI * 2f + 0.31f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 26f) { continue; }

                float dist = DcvrWorld.PlatformRadius + 1.6f + (float)rng.NextDouble() * 2.2f;
                float h = 1.6f + (float)rng.NextDouble() * 1.4f;
                var p = new GameObject($"Pylon{i}").transform;
                p.SetParent(root, false);
                p.localPosition = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
                p.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);

                var body = DcvrPrim.Create(PrimitiveType.Cube, "body");
                body.transform.SetParent(p, false);
                body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
                body.transform.localScale = new Vector3(0.24f, h, 0.24f);
                body.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.04f, 0.055f, 0.08f));
                body.isStatic = true;

                // A lit strip up one face, and a cap that pulses.
                var strip = DcvrPrim.Create(PrimitiveType.Quad, "strip");
                strip.transform.SetParent(p, false);
                strip.transform.localPosition = new Vector3(0f, h * 0.5f, -0.125f);
                strip.transform.localScale = new Vector3(0.05f, h * 0.82f, 1f);
                Material sm = Holo(DcvrWorld.Cyan, 0.55f);
                strip.GetComponent<Renderer>().sharedMaterial = sm;
                _pylonMats.Add(sm);

                var cap = DcvrPrim.Create(PrimitiveType.Cube, "cap");
                cap.transform.SetParent(p, false);
                cap.transform.localPosition = new Vector3(0f, h + 0.05f, 0f);
                cap.transform.localScale = new Vector3(0.3f, 0.03f, 0.3f);
                Material cm = Holo(DcvrWorld.Cyan, 0.7f);
                cap.GetComponent<Renderer>().sharedMaterial = cm;
                _pylonMats.Add(cm);
                _emitters.Add(cap.transform);
            }
        }

        /// <summary>The guardrail status ring: a thin band on the platform that carries the
        /// system's current safety state in colour. It is the one always-visible piece of
        /// security UI, which is why it is a ring underfoot rather than another panel — it
        /// is readable from any heading without occupying the forward view.</summary>
        private void BuildGuardrailRing()
        {
            const int segments = 96;
            float r = DcvrWorld.PlatformRadius - 0.45f;
            var ring = new GameObject("GuardrailRing").transform;
            ring.SetParent(transform, false);
            _guardrailMat = Holo(DcvrWorld.Cyan, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var seg = DcvrPrim.Create(PrimitiveType.Quad, $"Seg{i}");
                seg.transform.SetParent(ring, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(a) * r, PlatformTop + 0.012f, Mathf.Sin(a) * r);
                seg.transform.localRotation = Quaternion.Euler(90f, -a * Mathf.Rad2Deg, 0f);
                seg.transform.localScale = new Vector3(0.16f, (2f * Mathf.PI * r / segments) * 0.6f, 1f);
                seg.GetComponent<Renderer>().sharedMaterial = _guardrailMat;
                seg.isStatic = true;
            }
        }

        /// <summary>A low pedestal under the creation zone, so generated objects have
        /// somewhere to belong instead of hanging in space.</summary>
        private void BuildCreationPedestal()
        {
            var ped = DcvrPrim.Create(PrimitiveType.Cylinder, "CreationPedestal");
            ped.transform.SetParent(transform, false);
            ped.transform.localPosition = new Vector3(DcvrWorld.CreationZone.x, PlatformTop + 0.02f,
                                                      DcvrWorld.CreationZone.z);
            ped.transform.localScale = new Vector3(1.5f, 0.04f, 1.5f);
            ped.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.07f, 0.10f, 0.14f));

            var halo = DcvrPrim.Create(PrimitiveType.Quad, "PedestalHalo");
            halo.transform.SetParent(transform, false);
            halo.transform.localPosition = new Vector3(DcvrWorld.CreationZone.x, PlatformTop + 0.055f,
                                                      DcvrWorld.CreationZone.z);
            halo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            halo.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
            halo.GetComponent<Renderer>().sharedMaterial = Holo(DcvrWorld.Cyan, 0.22f);
        }

        /// <summary>Guardrail colour follows the live security state.</summary>
        public void SetGuardrailState(Color c)
        {
            if (_guardrailMat != null) { _guardrailMat.SetColor("_Color", c); }
        }

        private void Update()
        {
            // A slow shared breath across the near emissives. Everything on one phase reads
            // as a single system powering something, rather than as unrelated blinking.
            _phase += Time.deltaTime * 0.7f;
            float pulse = 0.45f + Mathf.Sin(_phase) * 0.12f;
            for (int i = 0; i < _pylonMats.Count; i++)
            {
                if (_pylonMats[i] != null) { _pylonMats[i].SetFloat("_Alpha", pulse); }
            }
            for (int i = 0; i < _emitters.Count; i++)
            {
                if (_emitters[i] == null) { continue; }
                Vector3 p = _emitters[i].localPosition;
                _emitters[i].localPosition = new Vector3(p.x, p.y + Mathf.Sin(_phase + i) * 0.00035f, p.z);
            }
        }

        private static Material Holo(Color c, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return Unlit(c); }
            var m = new Material(s) { name = "DCVR_NearHolo" };
            m.SetColor("_Color", c);
            m.SetFloat("_Alpha", alpha);
            return m;
        }

        private static Material Unlit(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(s) { name = "DCVR_NearUnlit" };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
            m.color = c;
            return m;
        }
    }
}
