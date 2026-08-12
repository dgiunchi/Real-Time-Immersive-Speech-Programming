// DreamCodeVR+ — the unmistakable 3D diagnostic world.
//
// Ugly on purpose. Its only job is to make "real 3D" and "a picture stuck to my face"
// impossible to confuse, which the beautiful version could not do: a dark, foggy,
// low-contrast scene of similar-looking monoliths is exactly the kind of world where a
// broken rig is hard to spot.
//
// The layout is built around PARALLAX. Objects sit at deliberately different depths —
// 1 m, 2 m, 4 m, 10 m, 25 m — in saturated, individually identifiable colours. Leaning
// sideways must slide the near cubes across the far tower by an obvious amount. If
// everything shifts together, the rig is still wrong.
//
// Everything here is parented to a single world root at the scene origin. Nothing in it
// is ever parented to the camera or the XR Origin.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrTestWorld
    {
        public sealed class Built
        {
            public Transform Root;
            public Transform[] Anchors;
            public string[] AnchorNames;
        }

        public static Built Build()
        {
            var root = new GameObject("DreamCodeVR_World").transform;
            root.position = Vector3.zero;

            var env = new GameObject("Environment").transform;
            env.SetParent(root, false);

            BuildLighting(root);
            BuildFloor(env);

            var anchors = new List<Transform>();
            var names = new List<string>();

            // ---- near layer: big parallax when leaning -------------------------------
            anchors.Add(Box(env, "NearCube_Yellow", new Vector3(-0.9f, 1.0f, 1.6f),
                            Vector3.one * 0.30f, new Color(1f, 0.85f, 0.10f)));
            names.Add("nearYellow");

            anchors.Add(Box(env, "NearCube_Purple", new Vector3(0.9f, 1.0f, 1.6f),
                            Vector3.one * 0.30f, new Color(0.70f, 0.30f, 1f)));
            names.Add("nearPurple");

            // ---- mid layer ------------------------------------------------------------
            anchors.Add(Box(env, "FloatingCube_White", new Vector3(0f, 1.5f, 4f),
                            Vector3.one * 0.6f, Color.white));
            names.Add("floatWhite");

            Box(env, "Pillar_Green", new Vector3(-4f, 2.0f, 8f),
                new Vector3(0.8f, 4.0f, 0.8f), new Color(0.15f, 0.95f, 0.35f));
            Box(env, "Pillar_Red", new Vector3(4f, 2.0f, 8f),
                new Vector3(0.8f, 4.0f, 0.8f), new Color(1f, 0.25f, 0.25f));

            // An archway: something to walk THROUGH, which reads as depth instantly.
            Box(env, "Arch_LeftLeg", new Vector3(-2.2f, 1.5f, 12f), new Vector3(0.4f, 3f, 0.4f),
                new Color(0.25f, 0.65f, 1f));
            Box(env, "Arch_RightLeg", new Vector3(2.2f, 1.5f, 12f), new Vector3(0.4f, 3f, 0.4f),
                new Color(0.25f, 0.65f, 1f));
            Box(env, "Arch_Top", new Vector3(0f, 3.1f, 12f), new Vector3(4.8f, 0.4f, 0.4f),
                new Color(0.25f, 0.65f, 1f));

            // Deliberately occluded: only visible if you move to see PAST the red pillar.
            Box(env, "Hidden_Orange", new Vector3(4f, 1.0f, 11f), Vector3.one * 0.5f,
                new Color(1f, 0.55f, 0.05f));

            // ---- far layer: near-zero parallax --------------------------------------
            anchors.Add(Box(env, "Tower_Blue", new Vector3(0f, 6f, 25f),
                            new Vector3(3f, 12f, 3f), new Color(0.15f, 0.45f, 1f)));
            names.Add("farTower");

            Box(env, "Tower_Teal_L", new Vector3(-14f, 5f, 30f), new Vector3(3f, 10f, 3f),
                new Color(0.10f, 0.75f, 0.75f));
            Box(env, "Tower_Teal_R", new Vector3(14f, 5f, 30f), new Vector3(3f, 10f, 3f),
                new Color(0.10f, 0.75f, 0.75f));

            // Behind the player too — turning round must not reveal an empty void.
            Box(env, "Tower_Behind", new Vector3(0f, 5f, -22f), new Vector3(3f, 10f, 3f),
                new Color(0.55f, 0.20f, 0.75f));

            var platform = BuildPlatform(env);
            anchors.Add(platform);
            names.Add("platform");

            return new Built
            {
                Root = root,
                Anchors = anchors.ToArray(),
                AnchorNames = names.ToArray(),
            };
        }

        private static void BuildLighting(Transform root)
        {
            var lightGo = new GameObject("Lighting");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.color = Color.white;
            l.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.25f, 0.30f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.18f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.fog = false;   // fog hides the depth cues this scene exists to prove
        }

        /// <summary>A large chequered floor. Passing markings underfoot is the clearest
        /// possible confirmation that walking actually translates the viewpoint.</summary>
        private static void BuildFloor(Transform parent)
        {
            var floor = new GameObject("Floor").transform;
            floor.SetParent(parent, false);

            const int tiles = 24;      // 24 x 24 tiles of 2 m = 48 m square
            const float size = 2f;
            float half = tiles * size * 0.5f;

            Material a = Lit(new Color(0.16f, 0.18f, 0.22f));
            Material b = Lit(new Color(0.09f, 0.10f, 0.13f));

            for (int x = 0; x < tiles; x++)
            {
                for (int z = 0; z < tiles; z++)
                {
                    var q = DcvrPrim.Create(PrimitiveType.Quad, $"Tile_{x}_{z}");
                    q.transform.SetParent(floor, false);
                    q.transform.localPosition = new Vector3(
                        -half + size * (x + 0.5f), 0f, -half + size * (z + 0.5f));
                    q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    q.transform.localScale = new Vector3(size, size, 1f);
                    q.GetComponent<Renderer>().sharedMaterial = ((x + z) % 2 == 0) ? a : b;
                    q.isStatic = true;
                }
            }
        }

        private static Transform BuildPlatform(Transform parent)
        {
            var disc = DcvrPrim.Create(PrimitiveType.Cylinder, "Platform");
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            disc.transform.localScale = new Vector3(8f, 0.05f, 8f);   // 8 m across
            disc.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.20f, 0.22f, 0.27f));
            return disc.transform;
        }

        private static Transform Box(Transform parent, string name, Vector3 pos,
                                     Vector3 scale, Color color)
        {
            var go = DcvrPrim.Create(PrimitiveType.Cube, name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Lit(color);
            return go.transform;
        }

        private static Shader _litShader;

        private static Material Lit(Color c)
        {
            if (_litShader == null)
            {
                // UNLIT on purpose. This world exists to make depth unambiguous, and a flat
                // saturated colour per object is the clearest possible read — no shading
                // gradients to confuse with distance. It is also the cheapest thing a mobile
                // GPU can draw, and it renders identically in the offscreen look-dev pass,
                // where URP's lit path has no lighting set up and returned a uniform green.
                _litShader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Standard");
                Debug.Log("[DcvrTestWorld] material shader = " +
                          (_litShader != null ? _litShader.name : "NULL"));
            }
            var m = new Material(_litShader) { name = "DCVR_Test_" + ColorUtility.ToHtmlStringRGB(c) };
            // URP Lit uses _BaseColor; Standard uses _Color. Set whichever exists, and
            // report if neither does — a silently uncoloured material is how every object
            // ended up the same shade.
            bool set = false;
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); set = true; }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); set = true; }
            if (!set) { Debug.LogWarning("[DcvrTestWorld] no colour property on " + _litShader.name); }
            m.color = c;
            return m;
        }
    }
}
