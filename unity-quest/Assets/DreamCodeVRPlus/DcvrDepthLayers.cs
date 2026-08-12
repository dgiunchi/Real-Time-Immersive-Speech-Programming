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

            Material body = Unlit(new Color(0.055f, 0.078f, 0.115f));
            const int count = 14;

            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.12f;
                float dist = 22f + (float)rng.NextDouble() * 24f;
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
                seam.GetComponent<Renderer>().sharedMaterial =
                    Holo(DcvrWorld.Cyan, 0.18f + (float)rng.NextDouble() * 0.14f);
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

            // Two tones only. At this distance shape and silhouette carry everything, and
            // extra material variety just costs draw calls.
            Material near = Unlit(new Color(0.028f, 0.042f, 0.065f));
            Material far = Unlit(new Color(0.018f, 0.028f, 0.045f));

            const int count = 34;
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.1f;
                float dist = 58f + (float)rng.NextDouble() * 55f;
                float h = 14f + (float)rng.NextDouble() * 42f;
                float w = 5f + (float)rng.NextDouble() * 11f;

                var tower = DcvrPrim.Create(PrimitiveType.Cube, $"FarTower{i}");
                tower.transform.SetParent(root, false);
                tower.transform.localPosition = new Vector3(Mathf.Cos(a) * dist, h * 0.5f - 3f,
                                                            Mathf.Sin(a) * dist);
                tower.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                tower.transform.localScale = new Vector3(w, h, w);
                tower.GetComponent<Renderer>().sharedMaterial = dist < 70f ? near : far;
                tower.isStatic = true;
            }
        }

        /// <summary>One enormous ring on the horizon. A single object of implausible scale
        /// does more for a sense of place than any amount of mid-distance detail, and it
        /// costs one mesh.</summary>
        private void BuildSkyRing()
        {
            Transform ring = BuildRing("SkyRing", 190f, 7f, 128);
            ring.SetParent(transform, false);
            ring.localPosition = new Vector3(0f, 78f, 210f);
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
            // Very slow. These are large objects filling the periphery, and peripheral
            // rotation is the classic vection trigger — a few degrees per second at most.
            _phase += Time.deltaTime;
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
