// DreamCodeVR+ — the mid and far layers.
//
// Depth in a headset comes from LAYERS at genuinely different distances, not from one
// ring of objects. Three bands, each doing a different job:
//
//   MID  (12-40 m)  arches, monoliths, suspended rings. Close enough that walking a few
//                   metres visibly changes their relationship to each other.
//   FAR  (45-95 m)  towers and slabs. Almost no parallax — these establish SCALE.
//   SKY             a single enormous ring far out, which is what makes the space read
//                   as a place rather than a plane with objects on it.
//
// Everything here is unlit, untextured and GPU-instanced where possible. On a mobile GPU
// the cost of this entire layer is a few dozen draw calls.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrDepthLayers : MonoBehaviour
    {
        private readonly List<Transform> _rings = new List<Transform>();
        private readonly List<Transform> _monoliths = new List<Transform>();
        private readonly List<Material> _seamMats = new List<Material>();
        private readonly List<float> _phases = new List<float>();
        private readonly List<Material> _streamMats = new List<Material>();
        private float _phase;

        public static DcvrDepthLayers Build(Transform worldRoot)
        {
            var go = new GameObject("DepthLayers");
            go.transform.SetParent(worldRoot, false);
            var d = go.AddComponent<DcvrDepthLayers>();
            d.BuildMid();
            d.BuildFar();
            d.BuildSkyRing();
            return d;
        }

        private void BuildMid()
        {
            var root = new GameObject("MidLayer").transform;
            root.SetParent(transform, false);
            var rng = new System.Random(9091);

            // Mid monoliths use the same family, denser windows since they are closer.
            Material body = Building(new Color(0.030f, 0.044f, 0.066f),
                                     new Color(0.090f, 0.160f, 0.220f), 0.48f, 2.7f, winW: 0.55f, winH: 0.85f);
            const int count = 11;

            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.12f;
                float dist = 30f + (float)rng.NextDouble() * 26f;
                float h = 6f + (float)rng.NextDouble() * 16f;
                float w = 1.6f + (float)rng.NextDouble() * 2.6f;

                var slab = DcvrPrim.Create(PrimitiveType.Cube, $"Monolith{i}");
                slab.transform.SetParent(root, false);
                slab.transform.localPosition = new Vector3(Mathf.Cos(a) * dist, h * 0.5f, Mathf.Sin(a) * dist);
                slab.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                slab.transform.localScale = new Vector3(w, h, w * 0.7f);
                slab.GetComponent<Renderer>().sharedMaterial = body;
                slab.isStatic = true;

                // A lit seam so each monolith has an edge to read at distance.
                var seam = DcvrPrim.Create(PrimitiveType.Quad, "seam");
                seam.transform.SetParent(slab.transform, false);
                seam.transform.localPosition = new Vector3(0f, 0f, -0.51f);
                seam.transform.localScale = new Vector3(0.06f, 0.82f, 1f);
                Material seamMat = Holo(DcvrWorld.Cyan, 0.18f + (float)rng.NextDouble() * 0.14f);
                seam.GetComponent<Renderer>().sharedMaterial = seamMat;

                // Registered for animation. Each monolith gets its own phase so the band
                // breathes as a population rather than pulsing in unison, which reads as
                // a working facility instead of a light show.
                _monoliths.Add(slab.transform);
                _seamMats.Add(seamMat);
                _phases.Add((float)rng.NextDouble() * Mathf.PI * 2f);
            }

            // Suspended rings: large, slowly counter-rotating, at different heights. They
            // give the middle distance something that MOVES, which reads as a working
            // facility rather than a static sculpture park.
            for (int i = 0; i < 3; i++)
            {
                float radius = 14f + i * 9f;
                float y = 17f + i * 6f;
                Transform ring = BuildRing($"SuspendedRing{i}", radius, 0.30f, 64);
                ring.SetParent(root, false);
                ring.localPosition = new Vector3(0f, y, 6f + i * 4f);
                // Near-horizontal, gently tilted. A steeply tilted ring reads as a blade
                // through the play space at head height.
                ring.localRotation = Quaternion.Euler(6f + i * 4f, 0f, i * 3f);
                _rings.Add(ring);
            }
        }

        private void BuildFar()
        {
            var root = new GameObject("FarLayer").transform;
            root.SetParent(transform, false);
            var rng = new System.Random(3345);

            Material near = Building(new Color(0.022f, 0.034f, 0.052f),
                                     new Color(0.075f, 0.140f, 0.200f), 0.42f, 2.9f);
            Material far = Building(new Color(0.016f, 0.026f, 0.042f),
                                    new Color(0.040f, 0.080f, 0.120f), 0.26f, 1.9f);

            // Three sub-bands rather than one ring. A single ring of towers reads as a
            // fence around the platform; layered depths read as a city that continues past
            // the horizon. Heights peak in the middle band so there is a downtown to look
            // at instead of an even wall.
            BuildTowerBand(root, rng, near, count: 30, minDist: 80f, spread: 55f,
                           minH: 26f, spreadH: 55f, minW: 7f, spreadW: 12f, name: "Inner");
            BuildTowerBand(root, rng, near, count: 34, minDist: 140f, spread: 90f,
                           minH: 45f, spreadH: 85f, minW: 10f, spreadW: 18f, name: "Mid");
            BuildTowerBand(root, rng, far, count: 30, minDist: 235f, spread: 130f,
                           minH: 30f, spreadH: 70f, minW: 12f, spreadW: 22f, name: "Outer");
        }

        /// <summary>One ring of towers. Angles are jittered off a regular division so the
        /// skyline is irregular without leaving gaps, and each tower is yawed to its own
        /// heading so faces are not all parallel.</summary>
        private static void BuildTowerBand(Transform root, System.Random rng, Material mat,
                                           int count, float minDist, float spread,
                                           float minH, float spreadH,
                                           float minW, float spreadW, string name)
        {
            var band = new GameObject(name).transform;
            band.SetParent(root, false);

            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f
                          + (float)(rng.NextDouble() - 0.5) * (Mathf.PI * 2f / count) * 0.8f;
                float dist = minDist + (float)rng.NextDouble() * spread;
                float h = minH + (float)rng.NextDouble() * spreadH;
                float w = minW + (float)rng.NextDouble() * spreadW;
                float d = w * (0.7f + (float)rng.NextDouble() * 0.5f);

                var tower = DcvrPrim.Create(PrimitiveType.Cube, $"{name}Tower{i}");
                tower.transform.SetParent(band, false);
                tower.transform.localPosition =
                    new Vector3(Mathf.Cos(a) * dist, h * 0.5f - 3f, Mathf.Sin(a) * dist);
                tower.transform.localRotation =
                    Quaternion.Euler(0f, -a * Mathf.Rad2Deg + (float)rng.NextDouble() * 30f, 0f);
                tower.transform.localScale = new Vector3(w, h, d);
                tower.GetComponent<Renderer>().sharedMaterial = mat;
                tower.isStatic = true;

                // A slim setback on some towers: two boxes read as far more architecture
                // than one, for one extra draw in a statically batched band.
                if (rng.NextDouble() < 0.45)
                {
                    var cap = DcvrPrim.Create(PrimitiveType.Cube, "setback");
                    cap.transform.SetParent(tower.transform, false);
                    cap.transform.localPosition = new Vector3(0f, 0.62f, 0f);
                    cap.transform.localScale = new Vector3(0.55f, 0.35f, 0.55f);
                    cap.GetComponent<Renderer>().sharedMaterial = mat;
                    cap.isStatic = true;
                }
            }
        }

        /// <summary>One enormous ring on the horizon. A single object of implausible scale
        /// does more for a sense of place than any amount of mid-distance detail, and it
        /// costs one mesh.</summary>
        private void BuildSkyRing()
        {
            Transform ring = BuildRing("SkyRing", 300f, 10f, 128);
            ring.SetParent(transform, false);
            ring.localPosition = new Vector3(0f, 120f, 330f);
            ring.localRotation = Quaternion.Euler(18f, 0f, 8f);
            foreach (Renderer r in ring.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = Holo(new Color(0.12f, 0.55f, 0.85f), 0.30f);
            }
            _rings.Add(ring);
        }

        private static Transform BuildRing(string name, float radius, float thickness, int segments)
        {
            var root = new GameObject(name).transform;
            Material m = Holo(DcvrWorld.Cyan, 0.32f);
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var seg = DcvrPrim.Create(PrimitiveType.Cube, $"s{i}");
                seg.transform.SetParent(root, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                seg.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                seg.transform.localScale = new Vector3(thickness, thickness,
                                                       (2f * Mathf.PI * radius / segments) * 0.8f);
                seg.GetComponent<Renderer>().sharedMaterial = m;
            }
            return root;
        }

        private void Update()
        {
            // Very slow throughout. These are large objects filling the periphery, and
            // peripheral motion is the classic vection trigger — everything here is at or
            // below the threshold where it registers as movement rather than as life.
            _phase += Time.deltaTime;

            // Monoliths breathe: a few centimetres of vertical drift and a slow seam
            // pulse, each on its own phase.
            for (int i = 0; i < _monoliths.Count; i++)
            {
                if (_monoliths[i] == null) { continue; }
                float ph = _phase * 0.35f + _phases[i];
                Vector3 p = _monoliths[i].localPosition;
                _monoliths[i].localPosition = new Vector3(p.x, p.y + Mathf.Sin(ph) * 0.0012f, p.z);
                if (_seamMats[i] != null)
                {
                    _seamMats[i].SetFloat("_Alpha", 0.20f + Mathf.Sin(ph * 1.3f) * 0.10f);
                }
            }
            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i] == null) { continue; }
                float dir = (i % 2 == 0) ? 1f : -1f;
                _rings[i].Rotate(Vector3.up, dir * (1.4f - i * 0.35f) * Time.deltaTime, Space.Self);
            }
            for (int i = 0; i < _streamMats.Count; i++)
            {
                if (_streamMats[i] != null)
                {
                    _streamMats[i].SetFloat("_Alpha", 0.20f + Mathf.Sin(_phase + i) * 0.08f);
                }
            }
        }

        private static Material Holo(Color c, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return Unlit(c); }
            var m = new Material(s) { name = "DCVR_DepthHolo" };
            m.SetColor("_Color", c);
            m.SetFloat("_Alpha", alpha);
            m.SetFloat("_ScanSpeed", 0.12f);
            return m;
        }

        /// <summary>Themed tower material. One per band, shared across every tower in it;
        /// the shader varies windows per object from world position, so a shared material
        /// still produces a skyline where no two towers look alike.</summary>
        private static Material Building(Color bottom, Color top, float litFraction,
                                         float emission, float winW = 1.5f, float winH = 2.0f)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Building");
            if (s == null) { return Unlit(bottom); }
            var m = new Material(s) { name = "DCVR_BuildingMat" };
            m.SetColor("_BaseColor", bottom);
            m.SetColor("_TopColor", top);
            m.SetColor("_WindowColor", DcvrWorld.Cyan);
            m.SetFloat("_LitFraction", litFraction);
            m.SetFloat("_Emission", emission);
            m.SetFloat("_WindowWidth", winW);
            m.SetFloat("_WindowHeight", winH);
            m.SetFloat("_Twinkle", 0.30f);
            m.enableInstancing = true;
            return m;
        }

        private static Material Unlit(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(s) { name = "DCVR_DepthUnlit" };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
            m.color = c;
            return m;
        }
    }
}
