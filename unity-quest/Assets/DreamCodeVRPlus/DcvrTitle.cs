// DreamCodeVR+ — the standing title piece.
//
// Deliberately minimal. The brief was "wow", and in a headset restraint reads as
// expensive while density reads as clutter: thin letter-spaced type, a single accent
// rule, generous space, one idea on screen at a time. Everything moves slowly enough
// that it never competes with the work happening on the platform.
//
// The rotating line crossfades rather than cutting, because a hard cut in peripheral
// vision registers as a flicker.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrTitle : MonoBehaviour
    {
        private static readonly string[] Lines =
        {
            "SPEAK  ·  AND  THE  WORLD  RESPONDS",
            "EVERY  COMMAND  IS  VALIDATED  BEFORE  IT  RUNS",
            "UNSAFE  OPERATIONS  ARE  UNREPRESENTABLE",
            "SAFE  BY  CONSTRUCTION  ·  NOT  BY  FILTER",
            "MSc  CYBER  SECURITY  ·  UNIVERSITY  OF  BIRMINGHAM",
        };

        private const float HoldSeconds = 4.2f;
        private const float FadeSeconds = 0.9f;

        private object _rotating;
        private Transform _float;
        private float _phase;

        public static DcvrTitle Build(Vector3 position, float yaw)
        {
            var go = new GameObject("DCVR_Title");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var t = go.AddComponent<DcvrTitle>();
            t.Construct();
            return t;
        }

        private void Construct()
        {
            _float = transform;

            DcvrText.Make(transform, "DREAMCODEVR+", new Vector3(0f, 0.68f, 0f),
                          0.20f, DcvrWorld.Cyan);

            // The subheading names what the system IS, which the product name alone does
            // not. Set small and dim: it should be legible when read deliberately and
            // recede the rest of the time.
            DcvrText.Make(transform, "SAFE  IMMERSIVE  SPEECH  PROGRAMMING",
                          new Vector3(0f, 0.52f, 0f), 0.052f, DcvrWorld.Dim);

            // A single hairline rule. One accent, doing the job that a box and a
            // background gradient would otherwise be asked to do badly.
            var rule = DcvrPrim.Create(PrimitiveType.Quad);
            rule.name = "DCVR_TitleRule";
            rule.transform.SetParent(transform, false);
            rule.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            rule.transform.localScale = new Vector3(2.1f, 0.005f, 1f);
            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            if (holo != null)
            {
                var m = new Material(holo) { name = "DCVR_TitleRuleMat" };
                m.SetColor("_Color", DcvrWorld.Cyan);
                m.SetFloat("_Alpha", 0.55f);
                rule.GetComponent<Renderer>().sharedMaterial = m;
            }

            DcvrText.Make(transform, "SANDEEP  RAI", new Vector3(0f, 0.26f, 0f),
                          0.085f, Color.white);

            _rotating = DcvrText.Make(transform, Lines[0], new Vector3(0f, 0.06f, 0f),
                                      0.045f, DcvrWorld.Dim);

            StartCoroutine(Rotate());
        }

        private IEnumerator Rotate()
        {
            int i = 0;
            while (true)
            {
                yield return new WaitForSeconds(HoldSeconds);
                yield return Fade(1f, 0f);
                i = (i + 1) % Lines.Length;
                DcvrText.SetText(_rotating, Lines[i]);
                yield return Fade(0f, 1f);
            }
        }

        private IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(from, to, t / FadeSeconds);
                DcvrText.SetColor(_rotating, new Color(DcvrWorld.Dim.r, DcvrWorld.Dim.g,
                                                       DcvrWorld.Dim.b, k) * k);
                yield return null;
            }
        }

        private void Update()
        {
            // Barely-there drift, so the piece is never dead but never distracting.
            _phase += Time.deltaTime * 0.5f;
            Vector3 p = _float.localPosition;
            _float.localPosition = new Vector3(p.x, p.y, p.z);
            _float.localRotation = Quaternion.Euler(0f, _float.localEulerAngles.y,
                                                    Mathf.Sin(_phase) * 0.25f);
        }
    }
}
