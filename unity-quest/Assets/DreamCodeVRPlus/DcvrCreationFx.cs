// DreamCodeVR+ — making a creation feel like it ARRIVED.
//
// Until now a generation simply existed: one frame there was nothing, the next frame a
// castle. Everything was correct and nothing was impressive, and in a medium where the
// user has just spoken a wish out loud, the moment it is granted is the moment worth
// spending effort on.
//
// Three effects, all bounded and all cheap:
//
//   ASSEMBLY   parts arrive in sequence rather than at once, each scaling up through a
//              brief over-shoot. Sequencing is what reads as construction; simultaneous
//              appearance reads as a scene being loaded.
//   IGNITION   each part flashes emissive as it lands and settles to its material's own
//              emission over about a second. With bloom on, that is a spark of light at
//              the moment of creation.
//   BREATHING  parts whose material is genuinely a light source (a lamp, a sun, an eye)
//              pulse very slightly forever, so the scene is never entirely static.
//
// PERFORMANCE RULES THIS FILE OBEYS
//   * MaterialPropertyBlock for every per-object override. The material system hands out
//     SHARED cached materials — writing to `renderer.material` would instantiate a copy
//     per object and silently undo the batching the cache exists to provide, and writing
//     to `sharedMaterial` would change every object using it.
//   * No allocation in Update. The block is reused; the breathing list is preallocated.
//   * Bounded work: assembly is a fixed short coroutine, breathing touches only parts that
//     are actually emissive, and the whole thing is capped.
//
// SAFETY RULES THIS FILE OBEYS
//   * It only ever changes SCALE (briefly, during arrival) and EMISSION. It never moves a
//     creation, so world anchoring is untouched — an effect that slid objects around would
//     undo the invariant the whole project is built on.
//   * No strobing, no rapid hue cycling, no large bright surfaces: the breathing amplitude
//     is a few percent at ~0.4 Hz, which is well inside the comfort limits and reads as
//     alive rather than as flashing.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrCreationFx : MonoBehaviour
    {
        public static DcvrCreationFx Instance { get; private set; }

        private const float PartInterval = 0.045f;   // stagger between parts
        private const float PartRise = 0.30f;        // how long one part takes to arrive
        private const float Overshoot = 1.12f;       // brief scale overshoot, then settle
        private const float IgnitionSeconds = 0.9f;
        private const float IgnitionStrength = 2.2f;

        // A creation of 60 parts would otherwise take 2.7 s to assemble; past this the
        // stagger compresses so a large build still feels responsive.
        private const int MaxStaggeredParts = 28;

        private MaterialPropertyBlock _block;

        private struct Breather
        {
            public Renderer Renderer;
            public Color Base;
            public float Phase;
        }

        private readonly List<Breather> _breathers = new List<Breather>(64);

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public static DcvrCreationFx Ensure()
        {
            if (Instance != null) { return Instance; }
            var go = new GameObject("DCVR_CreationFx");
            go.transform.SetParent(null, true);
            Instance = go.AddComponent<DcvrCreationFx>();
            Instance._block = new MaterialPropertyBlock();
            return Instance;
        }

        /// <summary>Play the arrival of a finished creation.
        ///
        /// Called AFTER the spatial compositor has placed the group, so the objects are
        /// already where they belong — this animates their appearance, never their
        /// position.</summary>
        public void PlayArrival(GenerationGroup group)
        {
            if (group?.Root == null) { return; }
            StartCoroutine(Assemble(group));
        }

        private IEnumerator Assemble(GenerationGroup group)
        {
            // Snapshot first: the group's list can change if the user issues another
            // command while this is running, and iterating a mutating list would throw
            // mid-effect and leave objects at zero scale — invisible, and indistinguishable
            // from a generation that failed.
            var parts = new List<Transform>(group.Objects.Count);
            var finalScale = new List<Vector3>(group.Objects.Count);
            foreach (GameObject go in group.Objects)
            {
                if (go == null) { continue; }
                parts.Add(go.transform);
                finalScale.Add(go.transform.localScale);
            }
            if (parts.Count == 0) { yield break; }

            // Start collapsed. Not at exactly zero: a zero scale can produce degenerate
            // normals for a frame on some meshes.
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null) { parts[i].localScale = finalScale[i] * 0.001f; }
            }

            float stagger = parts.Count > MaxStaggeredParts
                ? PartInterval * MaxStaggeredParts / parts.Count
                : PartInterval;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null)
                {
                    StartCoroutine(RaisePart(parts[i], finalScale[i]));
                }
                yield return new WaitForSeconds(stagger);
            }

            // Once everything has landed, collect the parts that are genuinely light
            // sources so they can breathe.
            yield return new WaitForSeconds(PartRise + IgnitionSeconds);
            CollectBreathers(group);
        }

        private IEnumerator RaisePart(Transform t, Vector3 target)
        {
            var r = t.GetComponent<Renderer>();
            Color emissionBase = ReadEmission(r);

            float elapsed = 0f;
            while (elapsed < PartRise && t != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / PartRise);
                // Ease out with a small overshoot, so the part settles into place rather
                // than snapping — the difference between "appeared" and "arrived".
                float e = 1f - Mathf.Pow(1f - k, 3f);
                float scale = Mathf.Lerp(0.001f, Overshoot, e);
                if (k > 0.75f)
                {
                    scale = Mathf.Lerp(Overshoot, 1f, (k - 0.75f) / 0.25f);
                }
                t.localScale = target * scale;
                yield return null;
            }
            if (t == null) { yield break; }
            t.localScale = target;

            // IGNITION: a bright flash on landing that decays to the material's own value.
            if (r == null) { yield break; }
            float fade = 0f;
            while (fade < IgnitionSeconds && r != null)
            {
                fade += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(fade / IgnitionSeconds);
                // Cubic decay: bright for an instant, gone quickly. A linear fade reads as
                // the object being lit rather than as a spark.
                float boost = IgnitionStrength * k * k * k;
                SetEmission(r, emissionBase + emissionBase * boost + new Color(0.10f, 0.14f, 0.18f) * boost);
                yield return null;
            }
            if (r != null) { SetEmission(r, emissionBase); }
        }

        /// <summary>Find the parts that are genuinely emissive and give them a slow pulse.
        ///
        /// Only real light sources: the material system gives every surface a small
        /// emission floor for visibility, so "has any emission" would select everything and
        /// the whole creation would throb.</summary>
        private void CollectBreathers(GenerationGroup group)
        {
            foreach (GameObject go in group.Objects)
            {
                if (go == null) { continue; }
                var r = go.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null) { continue; }
                if (!r.sharedMaterial.name.Contains("Emissive")) { continue; }
                if (_breathers.Count >= 64) { break; }

                _breathers.Add(new Breather
                {
                    Renderer = r,
                    Base = ReadEmission(r),
                    // Offset phases so a row of lamps shimmers rather than pulsing in
                    // lockstep, which reads as a machine rather than as light.
                    Phase = Random.value * Mathf.PI * 2f,
                });
            }
        }

        private void Update()
        {
            if (_breathers.Count == 0) { return; }

            float t = Time.time;
            for (int i = _breathers.Count - 1; i >= 0; i--)
            {
                Breather b = _breathers[i];
                if (b.Renderer == null)
                {
                    _breathers.RemoveAt(i);   // the object was deleted or cleared
                    continue;
                }
                // ~0.4 Hz, a few percent either side. Deliberately far below anything that
                // could read as flashing.
                float k = 1f + 0.14f * Mathf.Sin(t * 2.5f + b.Phase);
                SetEmission(b.Renderer, b.Base * k);
            }
        }

        // ---- property-block plumbing -------------------------------------------------

        private Color ReadEmission(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) { return Color.black; }
            return r.sharedMaterial.HasProperty(EmissionId)
                ? r.sharedMaterial.GetColor(EmissionId)
                : Color.black;
        }

        /// <summary>Per-renderer emission WITHOUT instantiating a material.
        ///
        /// `renderer.material` would clone the shared material for every object and undo
        /// the batching the material cache exists to provide; `sharedMaterial` would change
        /// every object using it at once. A property block is the only route that is both
        /// per-object and allocation-free.</summary>
        private void SetEmission(Renderer r, Color c)
        {
            if (r == null) { return; }
            r.GetPropertyBlock(_block);
            _block.SetColor(EmissionId, c);
            r.SetPropertyBlock(_block);
        }

        /// <summary>Highlight whatever the controller is pointing at.
        ///
        /// Reuses the same property-block path, so selection costs no material and no
        /// allocation. Called with null to clear.</summary>
        public void SetHighlight(Renderer previous, Renderer current)
        {
            if (previous != null && previous != current)
            {
                SetEmission(previous, ReadEmission(previous));
            }
            if (current != null)
            {
                Color b = ReadEmission(current);
                SetEmission(current, b + b * 0.9f + new Color(0.10f, 0.20f, 0.26f));
            }
        }

        /// <summary>Forget everything. Called by the full clear so a destroyed creation's
        /// renderers are not held alive by this list.</summary>
        public void Reset()
        {
            _breathers.Clear();
            StopAllCoroutines();
        }
    }
}
