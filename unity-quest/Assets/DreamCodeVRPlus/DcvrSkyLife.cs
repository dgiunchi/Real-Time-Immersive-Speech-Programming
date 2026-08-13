// DreamCodeVR+ — a world that is going about its business.
//
// A static skyline reads as a backdrop; a skyline with traffic in it reads as a place. The
// difference costs almost nothing, because none of it needs to be near the wearer or
// detailed — motion at distance is what sells scale, and at 40 m a craft is a few pixels
// with a light on it.
//
// THE RULE THAT SHAPES ALL OF THIS: NOTHING NEAR THE USER.
// Everything here is confined to a shell far outside the platform and above eye level. The
// area a person builds in must stay clear — an effect drifting through the space where a
// castle is about to appear is worse than no effect, and the whole point of the spatial
// layer is that creations own that volume. Nothing in this file is ever a target, is ever
// registered, or is ever selectable; it is scenery.
//
// COMFORT. Everything moves slowly, on smooth paths, in the periphery. Fast motion across
// the visual field is the classic way to induce vection in a headset, so speeds are chosen
// to read as "distant traffic" rather than as anything the eye wants to track. Nothing
// strobes and nothing crosses the wearer's forward view close enough to demand attention.
//
// COST. Three sets of small objects on analytic paths — no physics, no particles, no
// lights, one shared material per family, positions written once per frame from a formula.
// Everything is instanced and unlit, which on a mobile tiler is close to free.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrSkyLife : MonoBehaviour
    {
        // The clear zone. Creations are placed within roughly 10 m; nothing here comes
        // closer than this, so the two never compete for the same space.
        private const float MinRadius = 26f;
        private const float MaxRadius = 62f;

        private struct Flyer
        {
            public Transform T;
            public float Radius;
            public float Height;
            public float Speed;
            public float Phase;
            public float Bob;
            public Transform Glow;
        }

        private struct Drifter
        {
            public Transform T;
            public Vector3 Axis;
            public float Speed;
            public float Spin;
        }

        private readonly List<Flyer> _flyers = new List<Flyer>(18);
        private readonly List<Drifter> _drifters = new List<Drifter>(14);

        public static DcvrSkyLife Build(Transform worldRoot)
        {
            var go = new GameObject("DCVR_SkyLife");
            go.transform.SetParent(worldRoot, false);
            var s = go.AddComponent<DcvrSkyLife>();
            s.Construct();
            return s;
        }

        private void Construct()
        {
            // Deterministic: the same world every run, so a demo can be rehearsed and a
            // recording retaken without the scenery rearranging itself.
            Random.InitState(20260813);

            BuildTraffic();
            BuildDrift();
            BuildBeacons();

            Debug.Log($"[DcvrSkyLife] {_flyers.Count} craft, {_drifters.Count} drifting forms "
                      + $"between {MinRadius} m and {MaxRadius} m — clear of the build area");
        }

        /// <summary>Craft on wide circular lanes at several altitudes.
        ///
        /// Circular rather than point-to-point because a lane that never ends needs no
        /// spawning, no despawning and no lifetime bookkeeping — the position is a function
        /// of time, so the whole system is stateless and cannot leak.</summary>
        private void BuildTraffic()
        {
            var body = DcvrMaterials.Make(new Color(0.20f, 0.24f, 0.30f));
            Material glow = DcvrMaterials.Make(new Color(1.00f, 0.62f, 0.28f));
            SetEmissive(glow, new Color(1.00f, 0.55f, 0.20f) * 1.4f);
            Material glowCool = DcvrMaterials.Make(new Color(0.35f, 0.85f, 1.00f));
            SetEmissive(glowCool, new Color(0.30f, 0.80f, 1.00f) * 1.4f);

            const int lanes = 3;
            for (int lane = 0; lane < lanes; lane++)
            {
                int perLane = 5 + lane;
                float radius = Mathf.Lerp(MinRadius + 6f, MaxRadius - 8f, lane / (float)(lanes - 1));
                float height = 9f + lane * 7f;

                for (int i = 0; i < perLane; i++)
                {
                    var craft = new GameObject($"Craft_{lane}_{i}").transform;
                    craft.SetParent(transform, false);

                    // A stretched box reads as a vehicle at distance; anything more detailed
                    // is invisible at 40 m and costs the same to draw.
                    GameObject hull = DcvrPrim.Create(PrimitiveType.Cube, "Hull");
                    hull.transform.SetParent(craft, false);
                    hull.transform.localScale = new Vector3(2.6f, 0.5f, 0.8f);
                    hull.GetComponent<Renderer>().sharedMaterial = body;

                    GameObject lamp = DcvrPrim.Create(PrimitiveType.Cube, "Lamp");
                    lamp.transform.SetParent(craft, false);
                    lamp.transform.localPosition = new Vector3(-1.5f, 0f, 0f);
                    lamp.transform.localScale = new Vector3(0.45f, 0.3f, 0.5f);
                    lamp.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? glow : glowCool;

                    _flyers.Add(new Flyer
                    {
                        T = craft,
                        Radius = radius + Random.Range(-3f, 3f),
                        Height = height + Random.Range(-1.5f, 1.5f),
                        // Slow, and alternating direction by lane so the sky has depth
                        // rather than one uniform sweep.
                        Speed = (lane % 2 == 0 ? 1f : -1f) * Random.Range(0.020f, 0.036f),
                        Phase = i / (float)perLane * Mathf.PI * 2f,
                        Bob = Random.Range(0.3f, 0.8f),
                        Glow = lamp.transform,
                    });
                }
            }
        }

        /// <summary>Large slow forms much further out — the silhouettes that make a horizon
        /// feel occupied. Deliberately vague: at this distance a shape reading as "something
        /// alive out there" is more effective than a recognisable model.</summary>
        private void BuildDrift()
        {
            Material shell = DcvrMaterials.Make(new Color(0.16f, 0.20f, 0.28f));
            Material core = DcvrMaterials.Make(new Color(0.55f, 0.35f, 0.85f));
            SetEmissive(core, new Color(0.50f, 0.28f, 0.90f) * 1.2f);

            for (int i = 0; i < 12; i++)
            {
                var d = new GameObject($"Drifter_{i}").transform;
                d.SetParent(transform, false);

                float a = i / 12f * Mathf.PI * 2f;
                float r = Random.Range(MaxRadius * 0.75f, MaxRadius);
                d.localPosition = new Vector3(Mathf.Cos(a) * r, Random.Range(16f, 34f), Mathf.Sin(a) * r);

                GameObject b = DcvrPrim.Create(PrimitiveType.Capsule, "Body");
                b.transform.SetParent(d, false);
                b.transform.localScale = new Vector3(2.2f, 1.1f, 2.2f);
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                b.GetComponent<Renderer>().sharedMaterial = shell;

                GameObject c = DcvrPrim.Create(PrimitiveType.Sphere, "Core");
                c.transform.SetParent(d, false);
                c.transform.localScale = Vector3.one * 0.9f;
                c.GetComponent<Renderer>().sharedMaterial = core;

                _drifters.Add(new Drifter
                {
                    T = d,
                    Axis = new Vector3(Random.Range(-0.2f, 0.2f), 1f, Random.Range(-0.2f, 0.2f)).normalized,
                    Speed = Random.Range(0.6f, 1.6f) * (Random.value > 0.5f ? 1f : -1f),
                    Spin = Random.Range(3f, 9f),
                });
            }
        }

        /// <summary>Still emissive markers on the far towers. No motion at all — they exist
        /// so the skyline has points of light for the bloom to catch, which is most of what
        /// makes a distant city read as inhabited.</summary>
        private void BuildBeacons()
        {
            Material beacon = DcvrMaterials.Make(new Color(1.00f, 0.30f, 0.28f));
            SetEmissive(beacon, new Color(1.00f, 0.22f, 0.20f) * 1.6f);

            for (int i = 0; i < 22; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(MinRadius + 10f, MaxRadius);
                GameObject b = DcvrPrim.Create(PrimitiveType.Cube, $"Beacon_{i}");
                b.transform.SetParent(transform, false);
                b.transform.localPosition =
                    new Vector3(Mathf.Cos(a) * r, Random.Range(10f, 40f), Mathf.Sin(a) * r);
                b.transform.localScale = Vector3.one * Random.Range(0.35f, 0.7f);
                b.GetComponent<Renderer>().sharedMaterial = beacon;
            }
        }

        private static void SetEmissive(Material m, Color c)
        {
            if (m == null) { return; }
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", c); }
        }

        private void Update()
        {
            float t = Time.time;

            // Positions are a pure function of time — no integration, so nothing drifts out
            // of place over a long session and a paused app resumes exactly where it should.
            for (int i = 0; i < _flyers.Count; i++)
            {
                Flyer f = _flyers[i];
                if (f.T == null) { continue; }
                float a = f.Phase + t * f.Speed;
                float x = Mathf.Cos(a) * f.Radius;
                float z = Mathf.Sin(a) * f.Radius;
                float y = f.Height + Mathf.Sin(t * 0.6f + f.Phase) * f.Bob;
                f.T.localPosition = new Vector3(x, y, z);
                // Face along the lane, with a slight bank into the turn.
                f.T.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + (f.Speed > 0 ? 90f : -90f),
                                                     f.Speed > 0 ? 6f : -6f);
            }

            for (int i = 0; i < _drifters.Count; i++)
            {
                Drifter d = _drifters[i];
                if (d.T == null) { continue; }
                d.T.RotateAround(Vector3.zero, Vector3.up, d.Speed * Time.deltaTime);
                d.T.Rotate(d.Axis, d.Spin * Time.deltaTime, Space.Self);
            }
        }
    }
}
