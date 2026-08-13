// DreamCodeVR+ — the demonstration environment, built in code.
//
// Why procedural rather than an authored scene: the whole environment is reviewable as
// source, regenerates identically from a clean checkout, and needs no binary .unity
// asset to be kept in sync by hand. It also means the look can be changed without the
// Unity GUI, which is how it was actually developed.
//
// Art direction: a dark holographic creation space. Everything visible is either
// emissive or unlit, so there is exactly one realtime light and no shadow casters —
// the cheapest way to look deliberate on a standalone headset.
//
// STAGE 1 of the visual ladder (see CLAUDE.md): sky, floor, platform, rings, world-space
// UI, accept/block feedback, materialization. Stage 2 effects layer on top of the same
// objects rather than replacing them.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>Builds and owns the DreamCodeVR+ environment. One instance, created by
    /// <see cref="ModeCNetworkedDemo"/> (or standalone for look-dev).</summary>
    public sealed class DcvrWorld : MonoBehaviour
    {
        // ---- palette -------------------------------------------------------------
        // One place to change the identity. Cyan = safe/system, amber = in-flight,
        // green = accepted, red = blocked. Chosen to stay distinguishable for the most
        // common colour-vision deficiencies: the states differ in brightness and in
        // motion, never in hue alone.
        public static readonly Color Cyan = new Color(0.15f, 0.85f, 1.00f);
        public static readonly Color Amber = new Color(1.00f, 0.72f, 0.20f);
        public static readonly Color Green = new Color(0.25f, 1.00f, 0.55f);
        public static readonly Color Red = new Color(1.00f, 0.26f, 0.30f);
        public static readonly Color Dim = new Color(0.30f, 0.42f, 0.52f);

        public const float PlatformRadius = 4.0f;   // 8 m across — room to walk on

        /// <summary>Centre of the creation area, in front of the wearer. The rig origin is
        /// where the person physically stands, so the platform must be offset forward or
        /// they end up standing inside it.</summary>
        public static readonly Vector3 PlatformCenter = Vector3.zero;

        /// <summary>Where generated content appears: ahead of the wearer, on the platform,
        /// at comfortable focal distance. Separate from the platform centre because the
        /// wearer now stands ON the platform rather than looking at it from outside.</summary>
        public static readonly Vector3 CreationZone = new Vector3(0f, 1.15f, 2.6f);
        private const float RingHeight = 0.035f;

        private Transform _platform;
        private readonly List<Transform> _rings = new List<Transform>();
        private readonly List<Material> _ringMats = new List<Material>();
        private Material _gridMat;
        private Transform _spawnAnchor;
        private GameObject _target;
        private Light _key;

        private float _gridPulse = -1f;   // <0 = idle
        private Color _ringColor = Cyan;
        private float _ringPulse;
        private Transform _shardRoot;
        private ParticleSystem _motes;

        /// <summary>Where generated content is parented and what the action plan's
        /// "selected_object" resolves to.</summary>
        public GameObject Target => _target;

        public Transform SpawnAnchor => _spawnAnchor;

        /// <summary>Re-bind the runtime references after the SAVED scene loads.
        ///
        /// The world is generated at edit time and serialised as GameObjects, but these
        /// fields are plain private references — Unity does not serialise them, so on load
        /// they come back null while the objects themselves are present. Everything then
        /// looks correct in the hierarchy and fails at the first dereference. Re-resolve by
        /// name instead of marking them [SerializeField], because the objects are the
        /// authority and a stale serialised link would be worse than none.</summary>
        private void Awake()
        {
            // Recursive: Find() only searches DIRECT children, and the target sits under
            // the spawn anchor rather than at the top level.
            if (_target == null)
            {
                Transform t = FindDeep("DCVR_Target");
                if (t != null) { _target = t.gameObject; }
            }
            if (_spawnAnchor == null) { _spawnAnchor = FindDeep("DCVR_SpawnAnchor"); }
            if (_platform == null) { _platform = FindDeep("DCVR_Platform"); }

            if (_ringMats.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    Transform r = FindDeep($"DCVR_Ring{i}");
                    if (r == null) { continue; }
                    _rings.Add(r);
                    var rend = r.GetComponentInChildren<Renderer>();
                    if (rend != null) { _ringMats.Add(rend.sharedMaterial); }
                }
            }
            if (_gridMat == null)
            {
                Transform g = FindDeep("DCVR_Ground");
                var gr = g != null ? g.GetComponent<Renderer>() : null;
                if (gr != null) { _gridMat = gr.sharedMaterial; }
            }
            Debug.Log($"[DcvrWorld] awake: target={(_target != null ? _target.name : "NULL")} " +
                      $"rings={_ringMats.Count} grid={(_gridMat != null)}");
        }

        private Transform FindDeep(string name)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == name) { return t; }
            }
            return null;
        }

        public static DcvrWorld Build()
        {
            var go = new GameObject("DCVR_World");
            var w = go.AddComponent<DcvrWorld>();
            w.Construct();
            return w;
        }

        private void Construct()
        {
            BuildLighting();
            BuildGround();
            BuildPlatform();
            BuildRings();
            BuildDistantStructures();
            BuildSkyShards();
            BuildMotes();
            BuildTarget();
        }

        // ---- lighting ------------------------------------------------------------
        private void BuildLighting()
        {
            var lightGo = new GameObject("DCVR_KeyLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(48f, 34f, 0f);
            _key = lightGo.AddComponent<Light>();
            _key.type = LightType.Directional;
            _key.color = new Color(0.62f, 0.80f, 1.0f);
            _key.intensity = 0.85f;
            // No realtime shadows: nothing in this scene casts a shadow worth the
            // per-frame cost on a mobile GPU, and the look is emissive anyway.
            _key.shadows = LightShadows.None;

            // Assign the skybox at runtime rather than relying on it being serialized into
            // the scene's lighting settings — the scene is generated in batchmode and a
            // lost skybox reference is invisible until it renders as flat grey on device.
            Material sky = MakeMaterial("DreamCodeVRPlus/SkyGradient", "DCVR_SkyboxMat");
            if (sky != null)
            {
                sky.SetColor("_GroundColor", new Color(0.015f, 0.022f, 0.035f));
                sky.SetColor("_HorizonColor", new Color(0.045f, 0.150f, 0.250f));
                sky.SetColor("_SkyColor", new Color(0.008f, 0.015f, 0.040f));
                sky.SetColor("_GlowColor", new Color(0.070f, 0.480f, 0.660f));
                sky.SetFloat("_GlowPower", 16f);
                RenderSettings.skybox = sky;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.06f, 0.12f, 0.18f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.07f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.02f, 0.03f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.05f, 0.09f);
            RenderSettings.fogDensity = 0.0062f;
        }

        // ---- ground --------------------------------------------------------------
        private void BuildGround()
        {
            var ground = DcvrPrim.Create(PrimitiveType.Plane);
            ground.name = "DCVR_Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);   // 600 m — well past the far layer

            _gridMat = MakeMaterial("DreamCodeVRPlus/Grid", "DCVR_GridMat");
            Debug.Log("[DcvrWorld] grid shader " + (_gridMat != null ? "OK" : "MISSING"));
            if (_gridMat != null)
            {
                _gridMat.SetColor("_BaseColor", new Color(0.012f, 0.017f, 0.028f));
                _gridMat.SetColor("_LineColor", new Color(0.08f, 0.42f, 0.58f));
                _gridMat.SetFloat("_Spacing", 1.0f);
                _gridMat.SetFloat("_FadeStart", 8f);
                _gridMat.SetFloat("_FadeEnd", 85f);
                ground.GetComponent<Renderer>().sharedMaterial = _gridMat;
            }
        }

        // ---- platform ------------------------------------------------------------
        private void BuildPlatform()
        {
            var disc = DcvrPrim.Create(PrimitiveType.Cylinder);
            disc.name = "DCVR_Platform";
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = PlatformCenter + new Vector3(0f, 0.06f, 0f);
            disc.transform.localScale = new Vector3(PlatformRadius * 2f, 0.06f, PlatformRadius * 2f);
            _platform = disc.transform;

            Material m = MakeUnlit("DCVR_PlatformMat", new Color(0.085f, 0.105f, 0.145f));
            disc.GetComponent<Renderer>().sharedMaterial = m;
        }

        private void BuildRings()
        {
            // Three concentric rings at different radii and speeds. Counter-rotation
            // reads as "alive" without any element moving fast enough to cause vection.
            float[] radii = { PlatformRadius * 0.72f, PlatformRadius * 0.92f, PlatformRadius * 1.06f };
            float[] widths = { 0.05f, 0.03f, 0.018f };
            for (int i = 0; i < radii.Length; i++)
            {
                GameObject ring = BuildRingMesh($"DCVR_Ring{i}", radii[i], widths[i], 96);
                ring.transform.SetParent(transform, false);
                ring.transform.localPosition =
                    PlatformCenter + new Vector3(0f, 0.135f + i * 0.014f, 0f);

                Material mat = MakeMaterial("DreamCodeVRPlus/Holo", $"DCVR_RingMat{i}");
                if (mat != null)
                {
                    mat.SetColor("_Color", Cyan);
                    mat.SetFloat("_Alpha", 0.30f - i * 0.06f);
                    mat.SetFloat("_ScanSpeed", (i % 2 == 0) ? 0.5f : -0.4f);
                    ring.GetComponent<Renderer>().sharedMaterial = mat;
                    _ringMats.Add(mat);
                }
                _rings.Add(ring.transform);
            }
        }

        /// <summary>Flat annulus in the XZ plane — a triangle strip between an inner and
        /// outer circle. Cheaper and sharper than a scaled torus, and it never shows
        /// silhouette artefacts when viewed edge-on.</summary>
        private static GameObject BuildRingMesh(string name, float radius, float width, int segments)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            var verts = new Vector3[segments * 2];
            var norms = new Vector3[segments * 2];
            var tris = new int[segments * 6];

            float inner = radius - width * 0.5f;
            float outer = radius + width * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * inner, 0f, s * inner);
                verts[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
                norms[i * 2] = Vector3.up;
                norms[i * 2 + 1] = Vector3.up;

                int n = (i + 1) % segments;
                int t = i * 6;
                tris[t] = i * 2;
                tris[t + 1] = n * 2;
                tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2 + 1;
                tris[t + 4] = n * 2;
                tris[t + 5] = n * 2 + 1;
            }

            var mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // ---- surroundings --------------------------------------------------------
        private void BuildDistantStructures()
        {
            // A ring of monoliths at mid-distance gives parallax and a sense of scale —
            // the two things that make a VR space feel like somewhere rather than a
            // backdrop. Deterministic placement (fixed seed) so the scene is identical
            // every run and screenshots are comparable.
            var rng = new System.Random(20260812);
            var root = new GameObject("DCVR_Structures");
            root.transform.SetParent(transform, false);

            Material body = MakeMaterial("DreamCodeVRPlus/Building", "DCVR_MonolithMat")
                           ?? MakeUnlit("DCVR_MonolithMatFallback", new Color(0.045f, 0.065f, 0.095f));
            if (body != null && body.HasProperty("_TopColor"))
            {
                body.SetColor("_BaseColor", new Color(0.028f, 0.042f, 0.062f));
                body.SetColor("_TopColor", new Color(0.090f, 0.160f, 0.220f));
                body.SetColor("_WindowColor", Cyan);
                body.SetFloat("_LitFraction", 0.46f);
                body.SetFloat("_Emission", 2.7f);
                body.SetFloat("_WindowWidth", 0.6f);
                body.SetFloat("_WindowHeight", 0.9f);
            }
            Material edge = MakeMaterial("DreamCodeVRPlus/Holo", "DCVR_MonolithEdgeMat");
            if (edge != null)
            {
                edge.SetColor("_Color", Cyan);
                edge.SetFloat("_Alpha", 0.22f);
                edge.SetFloat("_ScanSpeed", 0.18f);
                edge.SetFloat("_ScanDensity", 3.5f);
            }

            // 28 monoliths, evenly distributed over the FULL 360 degrees. A headset wearer
            // turns around; anything staged only in front of them collapses the moment
            // they look over their shoulder and the world reads as a stage flat.
            const int count = 7;
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.16f;
                float dist = 46f + (float)rng.NextDouble() * 26f;
                float h = 8f + (float)rng.NextDouble() * 20f;
                float w = 1.1f + (float)rng.NextDouble() * 2.2f;

                var slab = DcvrPrim.Create(PrimitiveType.Cube);
                slab.name = $"DCVR_Monolith{i}";
                slab.transform.SetParent(root.transform, false);
                slab.transform.localPosition = new Vector3(Mathf.Cos(a) * dist, h * 0.5f, Mathf.Sin(a) * dist);
                slab.transform.localScale = new Vector3(w, h, w);
                slab.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 60f, 0f);
                var r = slab.GetComponent<Renderer>();
                r.sharedMaterial = body;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                // A thin lit seam up one face; sells "structure" far more cheaply than
                // any amount of geometry would.
                if (edge != null)
                {
                    var seam = DcvrPrim.Create(PrimitiveType.Cube);
                    seam.name = $"DCVR_MonolithSeam{i}";
                    seam.transform.SetParent(slab.transform, false);
                    seam.transform.localScale = new Vector3(0.06f, 0.86f, 0.06f);
                    seam.transform.localPosition = new Vector3(0.52f, 0f, 0.52f);
                    var sr = seam.GetComponent<Renderer>();
                    sr.sharedMaterial = edge;
                    sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    sr.receiveShadows = false;
                }
            }
        }


        /// <summary>Slowly drifting shards overhead. Their only job is to make the space
        /// above the wearer worth looking at — without them, tilting the head up shows an
        /// empty gradient and the world stops feeling like a place.</summary>
        private void BuildSkyShards()
        {
            var rng = new System.Random(41277);
            _shardRoot = new GameObject("DCVR_SkyShards").transform;
            _shardRoot.SetParent(transform, false);

            Material shard = MakeMaterial("DreamCodeVRPlus/Holo", "DCVR_ShardMat");
            if (shard == null) { return; }
            shard.SetColor("_Color", Cyan);
            shard.SetFloat("_Alpha", 0.14f);
            shard.SetFloat("_ScanSpeed", 0.25f);
            shard.SetFloat("_ScanDensity", 6f);

            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                float dist = 12f + (float)rng.NextDouble() * 16f;
                float y = 9f + (float)rng.NextDouble() * 11f;

                var s = DcvrPrim.Create(PrimitiveType.Quad);
                s.name = $"DCVR_Shard{i}";
                s.transform.SetParent(_shardRoot, false);
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * dist, y, Mathf.Sin(a) * dist);
                s.transform.localRotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f);
                float sc = 1.2f + (float)rng.NextDouble() * 3.4f;
                s.transform.localScale = new Vector3(sc, sc * 0.35f, 1f);
                var r = s.GetComponent<Renderer>();
                r.sharedMaterial = shard;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>Ambient motes drifting through the middle distance. This is the cheapest
        /// possible cure for a static-feeling scene: a few hundred slow-moving points give
        /// the air texture and, in stereo, read strongly as depth.</summary>
        private void BuildMotes()
        {
            var go = new GameObject("DCVR_Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 3.0f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 22f;
            main.startSpeed = 0.12f;
            main.startSize = 0.045f;
            main.startColor = new Color(0.35f, 0.85f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.004f;      // drift gently upward
            main.maxParticles = 180;             // stays well inside the mobile budget

            var emission = ps.emission;
            emission.rateOverTime = 8f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(38f, 9f, 38f);

            // No collision, no sub-emitters, no lights: all of those are what make
            // particles expensive on a standalone headset.
            var col = ps.collision; col.enabled = false;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 0.12f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Material m = MakeMaterial("DreamCodeVRPlus/Holo", "DCVR_MoteMat");
            if (m != null)
            {
                m.SetColor("_Color", Cyan);
                m.SetFloat("_Alpha", 0.5f);
                renderer.sharedMaterial = m;
            }
            _motes = ps;
        }

        // ---- the object commands act on -----------------------------------------
        private void BuildTarget()
        {
            var anchor = new GameObject("DCVR_SpawnAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = CreationZone;
            _spawnAnchor = anchor.transform;

            // A real, visible, renderable object. The previous build handed the executor
            // an EMPTY GameObject as scene root and null as the selected object, so every
            // plan targeting "selected_object" was refused before it could do anything —
            // correct fail-closed behaviour reacting to a wiring bug, not a policy one.
            _target = DcvrPrim.Create(PrimitiveType.Cube);
            _target.name = "DCVR_Target";
            _target.transform.SetParent(anchor.transform, false);
            _target.transform.localScale = Vector3.one * 0.55f;

            Material m = MakeUnlit("DCVR_TargetMat", new Color(0.62f, 0.72f, 0.85f));
            _target.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ---- state feedback ------------------------------------------------------
        /// <summary>Colour + pulse the whole platform for a pipeline state. This is the
        /// visual half of the security story: accepted work glows cyan-green, a blocked
        /// request flushes red, and the difference is legible from across the room.</summary>
        public void SetState(Color color, bool pulse)
        {
            _ringColor = color;
            if (pulse)
            {
                _ringPulse = 1.0f;
                _gridPulse = 0f;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < _rings.Count; i++)
            {
                float dir = (i % 2 == 0) ? 1f : -1f;
                // Deliberately slow. Fast rotation in the periphery is a known comfort
                // hazard in VR, and this sits in the floor of the user's view.
                _rings[i].Rotate(Vector3.up, dir * (3.5f + i * 1.5f) * dt, Space.Self);
            }

            // Overhead shards drift very slowly. Rotation in the periphery is the classic
            // vection trigger, so this is deliberately near the threshold of perception:
            // enough that the world is never frozen, slow enough to be comfortable.
            if (_shardRoot != null)
            {
                _shardRoot.Rotate(Vector3.up, 0.6f * dt, Space.Self);
            }

            _ringPulse = Mathf.Max(0f, _ringPulse - dt * 1.6f);
            for (int i = 0; i < _ringMats.Count; i++)
            {
                _ringMats[i].SetColor("_Color", Color.Lerp(_ringMats[i].GetColor("_Color"), _ringColor, dt * 6f));
                _ringMats[i].SetFloat("_Pulse", _ringPulse * (1.1f - i * 0.25f));
            }

            if (_gridPulse >= 0f && _gridMat != null)
            {
                _gridPulse += dt * 1.5f;
                _gridMat.SetColor("_PulseColor", _ringColor);
                _gridMat.SetFloat("_Pulse", _gridPulse);
                if (_gridPulse > 3f) { _gridPulse = -1f; _gridMat.SetFloat("_Pulse", 0f); }
            }
        }


        /// <summary>Destroy that also works outside play mode. The scene builder is run
        /// by the offscreen look-dev renderer in the Editor, where Destroy() is deferred
        /// to the end of a frame that never comes and the object survives.</summary>
        private static void SafeDestroy(Object o)
        {
            if (o == null) { return; }
            if (Application.isPlaying) { Destroy(o); } else { DestroyImmediate(o); }
        }

        // ---- helpers -------------------------------------------------------------
        private static Material MakeMaterial(string shaderName, string name)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null)
            {
                // Shaders must be referenced by a scene object or listed in Always
                // Included Shaders or they are stripped from the player build. Failing
                // loudly here beats a silently magenta demo on the headset.
                Debug.LogError($"[DcvrWorld] shader not found in build: {shaderName}");
                return null;
            }
            return new Material(s) { name = name };
        }

        private static Material MakeUnlit(string name, Color color)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(s) { name = name };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", color); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", color); }
            return m;
        }
    }
}
