// DreamCodeVR+ — the name piece.
//
// One line of text — SANDEEP RAI — that appears, holds, and leaves, forever, using a
// DIFFERENT effect on every cycle. Six of them, in sequence, so the piece never repeats
// itself within a demo and never settles into wallpaper.
//
// The effects are built from four primitives only: alpha, scale, position and a substring
// reveal. That is deliberate. Anything richer would need a per-character mesh or a custom
// text shader, and the cost of both is paid every frame on a mobile GPU for a decorative
// element — whereas these are a handful of transform writes on one object.
//
// Timing is chosen for the periphery: nothing snaps, nothing strobes, and the hold is long
// enough to read the name without it feeling like a slideshow. Peripheral flicker is both
// uncomfortable and, at speed, a genuine photosensitivity concern.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrNameShow : MonoBehaviour
    {
        private const string Name = "SANDEEP  RAI";
        private const float Hold = 3.4f;
        private const float Gap = 0.9f;

        private object _text;
        private Transform _textT;
        private Transform _rule;
        private Material _ruleMat;
        private int _effect;

        public static DcvrNameShow Build(Vector3 position, float yaw)
        {
            var go = new GameObject("DCVR_NameShow");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var n = go.AddComponent<DcvrNameShow>();
            n.Construct();
            return n;
        }

        private void Construct()
        {
            _text = DcvrText.Make(transform, Name, Vector3.zero, 0.17f, Color.white);
            _textT = ((Component)_text).transform;

            // A hairline rule under the name, animated with it. One accent is enough.
            var rule = DcvrPrim.Create(PrimitiveType.Quad, "Rule");
            rule.transform.SetParent(transform, false);
            rule.transform.localPosition = new Vector3(0f, -0.16f, 0f);
            rule.transform.localScale = new Vector3(0f, 0.006f, 1f);
            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            if (holo != null)
            {
                _ruleMat = new Material(holo) { name = "DCVR_NameRuleMat" };
                _ruleMat.SetColor("_Color", DcvrWorld.Cyan);
                _ruleMat.SetFloat("_Alpha", 0.6f);
                rule.GetComponent<Renderer>().sharedMaterial = _ruleMat;
            }
            _rule = rule.transform;

            StartCoroutine(Loop());
        }

        private IEnumerator Loop()
        {
            while (true)
            {
                yield return Enter(_effect);
                yield return new WaitForSeconds(Hold);
                yield return Exit(_effect);
                yield return new WaitForSeconds(Gap);
                _effect = (_effect + 1) % 6;
            }
        }

        // ---- entrances -----------------------------------------------------------
        private IEnumerator Enter(int which)
        {
            switch (which)
            {
                case 0: yield return TypeOn(); break;
                case 1: yield return FadeScale(from: 0.75f, to: 1f, dur: 0.9f, fadeIn: true); break;
                case 2: yield return RiseIn(); break;
                case 3: yield return SweepIn(); break;
                case 4: yield return SettleIn(); break;
                default: yield return ShimmerIn(); break;
            }
            yield return RuleTo(1.5f, 0.5f);
        }

        private IEnumerator Exit(int which)
        {
            yield return RuleTo(0f, 0.4f);
            switch (which)
            {
                case 0: yield return TypeOff(); break;
                case 1: yield return FadeScale(from: 1f, to: 1.25f, dur: 0.8f, fadeIn: false); break;
                case 2: yield return SinkOut(); break;
                case 3: yield return SweepOut(); break;
                case 4: yield return FadeScale(from: 1f, to: 0.8f, dur: 0.7f, fadeIn: false); break;
                default: yield return ShimmerOut(); break;
            }
        }

        // 0 — typed on, character by character.
        private IEnumerator TypeOn()
        {
            Alpha(1f);
            Scale(1f);
            for (int i = 0; i <= Name.Length; i++)
            {
                DcvrText.SetText(_text, Name.Substring(0, i));
                yield return new WaitForSeconds(0.045f);
            }
        }

        private IEnumerator TypeOff()
        {
            for (int i = Name.Length; i >= 0; i--)
            {
                DcvrText.SetText(_text, Name.Substring(0, i));
                yield return new WaitForSeconds(0.028f);
            }
        }

        // 1 — fade with a scale change, in or out.
        private IEnumerator FadeScale(float from, float to, float dur, bool fadeIn)
        {
            DcvrText.SetText(_text, Name);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                Scale(Mathf.Lerp(from, to, k));
                Alpha(fadeIn ? k : 1f - k);
                yield return null;
            }
            Alpha(fadeIn ? 1f : 0f);
        }

        // 2 — rises into place from below.
        private IEnumerator RiseIn()
        {
            DcvrText.SetText(_text, Name);
            Scale(1f);
            const float dur = 0.85f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                _textT.localPosition = new Vector3(0f, Mathf.Lerp(-0.22f, 0f, k), 0f);
                Alpha(k);
                yield return null;
            }
            _textT.localPosition = Vector3.zero;
        }

        private IEnumerator SinkOut()
        {
            const float dur = 0.7f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                _textT.localPosition = new Vector3(0f, Mathf.Lerp(0f, 0.22f, k), 0f);
                Alpha(1f - k);
                yield return null;
            }
            _textT.localPosition = Vector3.zero;
        }

        // 3 — a horizontal sweep: squashed wide, then resolving.
        private IEnumerator SweepIn()
        {
            DcvrText.SetText(_text, Name);
            const float dur = 0.75f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                _textT.localScale = new Vector3(Mathf.Lerp(2.2f, 1f, k), Mathf.Lerp(0.35f, 1f, k), 1f);
                Alpha(k);
                yield return null;
            }
            _textT.localScale = Vector3.one;
        }

        private IEnumerator SweepOut()
        {
            const float dur = 0.6f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                _textT.localScale = new Vector3(Mathf.Lerp(1f, 0.2f, k), Mathf.Lerp(1f, 1.8f, k), 1f);
                Alpha(1f - k);
                yield return null;
            }
            _textT.localScale = Vector3.one;
        }

        // 4 — overshoots slightly and settles, like a mechanism locking.
        private IEnumerator SettleIn()
        {
            DcvrText.SetText(_text, Name);
            const float dur = 0.8f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float s = 1f + Mathf.Sin(k * Mathf.PI * 2.2f) * 0.12f * (1f - k);
                Scale(s);
                Alpha(Mathf.SmoothStep(0f, 1f, k * 1.8f));
                yield return null;
            }
            Scale(1f);
        }

        // 5 — resolves through a few brightness beats, cyan settling to white.
        private IEnumerator ShimmerIn()
        {
            DcvrText.SetText(_text, Name);
            Scale(1f);
            const float dur = 0.95f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                // Three slow beats, never fast enough to read as a flicker.
                float beat = 0.55f + 0.45f * Mathf.Sin(k * Mathf.PI * 3f);
                Color c = Color.Lerp(DcvrWorld.Cyan, Color.white, k);
                DcvrText.SetColor(_text, new Color(c.r, c.g, c.b, 1f) * (beat * k));
                yield return null;
            }
            Alpha(1f);
        }

        private IEnumerator ShimmerOut()
        {
            const float dur = 0.7f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                float beat = 0.5f + 0.5f * Mathf.Cos(k * Mathf.PI * 3f);
                Color c = Color.Lerp(Color.white, DcvrWorld.Cyan, k);
                DcvrText.SetColor(_text, new Color(c.r, c.g, c.b, 1f) * (beat * (1f - k)));
                yield return null;
            }
            Alpha(0f);
        }

        // ---- helpers ---------------------------------------------------------------
        private IEnumerator RuleTo(float width, float dur)
        {
            if (_rule == null) { yield break; }
            float from = _rule.localScale.x;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float w = Mathf.Lerp(from, width, Mathf.SmoothStep(0f, 1f, t / dur));
                _rule.localScale = new Vector3(w, 0.006f, 1f);
                yield return null;
            }
            _rule.localScale = new Vector3(width, 0.006f, 1f);
        }

        private void Alpha(float a) =>
            DcvrText.SetColor(_text, new Color(1f, 1f, 1f, 1f) * Mathf.Clamp01(a));

        private void Scale(float s) => _textT.localScale = Vector3.one * s;
    }
}
