// DreamCodeVR+ — generation preview that shows PROGRESS without showing CODE.
//
// A demonstration naturally wants to display the model's output scrolling past. That is
// precisely the wrong thing to put on a shared screen. In this project the generated
// text is derived from what the wearer SAID, and the headset is frequently being cast to
// a room or a projector during a demo, so rendering it verbatim leaks the utterance to
// every bystander — the shoulder-surfing and bystander-privacy surface the privacy
// chapter is about. Displaying it would contradict the thesis on the thesis's own stage.
//
// So this renders the SHAPE of generation and never its content: rows of glyph-blocks
// whose widths are drawn from a fixed pseudo-random sequence with a per-request seed, so
// it reads convincingly as code being written while carrying no information about the
// actual code. Nothing here is derived from the real payload — deliberately. It is an
// honest abstraction, and it is labelled as one on the panel.
//
// The bar alongside it IS real: it tracks the pipeline stage the backend reports.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrCodePreview : MonoBehaviour
    {
        private const int Rows = 7;
        private const int MaxBlocksPerRow = 9;
        private const float RowHeight = 0.052f;
        private const float PanelWidth = 1.05f;

        private readonly List<List<Transform>> _rows = new List<List<Transform>>();
        private readonly List<Material> _mats = new List<Material>();
        private Transform _barFill;
        private Material _barMat;
        private object _percentLabel;
        private Coroutine _run;
        private float _progress;

        public static DcvrCodePreview Build(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("DCVR_CodePreview");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var p = go.AddComponent<DcvrCodePreview>();
            p.Construct();
            return p;
        }

        private void Construct()
        {
            DcvrText.Make(transform, "GENERATING  ·  CONTENT  HIDDEN",
                          new Vector3(0f, RowHeight * Rows * 0.5f + 0.075f, 0f),
                          0.040f, DcvrWorld.Dim);

            var rng = new System.Random(7717);
            for (int r = 0; r < Rows; r++)
            {
                var row = new List<Transform>();
                float x = -PanelWidth * 0.5f;
                // Indentation, so the block pattern reads as structured code rather than
                // as a bar chart.
                float indent = (r == 0 || r == Rows - 1) ? 0f
                             : (r % 3 == 0 ? 0.10f : 0.05f);
                x += indent;

                int blocks = 3 + rng.Next(MaxBlocksPerRow - 3);
                for (int b = 0; b < blocks; b++)
                {
                    float w = 0.045f + (float)rng.NextDouble() * 0.16f;
                    if (x + w > PanelWidth * 0.5f) { break; }

                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = $"DCVR_Glyph{r}_{b}";
                    q.transform.SetParent(transform, false);
                    q.transform.localPosition = new Vector3(
                        x + w * 0.5f,
                        (Rows * 0.5f - r) * RowHeight - RowHeight * 0.5f,
                        0f);
                    q.transform.localScale = new Vector3(w, RowHeight * 0.42f, 1f);
                    Destroy(q.GetComponent<Collider>());

                    Material m = Holo(DcvrWorld.Cyan, 0f);
                    if (m != null)
                    {
                        q.GetComponent<Renderer>().sharedMaterial = m;
                        _mats.Add(m);
                    }
                    var rr = q.GetComponent<Renderer>();
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rr.receiveShadows = false;
                    row.Add(q.transform);
                    x += w + 0.028f;
                }
                _rows.Add(row);
            }

            BuildBar();
            gameObject.SetActive(false);
        }

        private void BuildBar()
        {
            float y = -(Rows * 0.5f) * RowHeight - 0.075f;

            var track = GameObject.CreatePrimitive(PrimitiveType.Quad);
            track.name = "DCVR_BarTrack";
            track.transform.SetParent(transform, false);
            track.transform.localPosition = new Vector3(0f, y, 0.002f);
            track.transform.localScale = new Vector3(PanelWidth, 0.012f, 1f);
            Destroy(track.GetComponent<Collider>());
            Material tm = Holo(DcvrWorld.Dim, 0.09f);
            if (tm != null) { track.GetComponent<Renderer>().sharedMaterial = tm; }

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "DCVR_BarFill";
            fill.transform.SetParent(transform, false);
            // Anchored left: scale grows rightward from a fixed origin, which is why the
            // pivot is offset rather than the quad simply being scaled about its centre.
            fill.transform.localPosition = new Vector3(-PanelWidth * 0.5f, y, 0f);
            fill.transform.localScale = new Vector3(0f, 0.012f, 1f);
            Destroy(fill.GetComponent<Collider>());
            _barMat = Holo(DcvrWorld.Cyan, 0.75f);
            if (_barMat != null) { fill.GetComponent<Renderer>().sharedMaterial = _barMat; }
            _barFill = fill.transform;

            _percentLabel = DcvrText.Make(transform, "0%", new Vector3(0f, y - 0.055f, 0f),
                                          0.038f, DcvrWorld.Dim);
        }

        /// <summary>Begin the visualisation. <paramref name="seed"/> is the request id, so
        /// the pattern differs between requests without being derived from the payload.</summary>
        public void Begin(string seed)
        {
            gameObject.SetActive(true);
            _progress = 0f;
            foreach (Material m in _mats) { m.SetFloat("_Alpha", 0f); }
            if (_run != null) { StopCoroutine(_run); }
            _run = StartCoroutine(Stream(seed));
        }

        /// <summary>Drive the bar from the real pipeline stage. The bar is honest even
        /// though the glyphs are decorative.</summary>
        public void SetStageProgress(DcvrStage stage)
        {
            _progress = stage switch
            {
                DcvrStage.Intent => 0.25f,
                DcvrStage.Generate => 0.55f,
                DcvrStage.Validate => 0.85f,
                DcvrStage.Execute => 1.00f,
                _ => 0f,
            };
        }

        public void Finish() { StartCoroutine(FadeOut()); }

        private IEnumerator Stream(string seed)
        {
            int h = seed != null ? seed.GetHashCode() : 0;
            var rng = new System.Random(h);
            // Reveal row by row with a small jitter so it reads as typing rather than a
            // mechanical wipe.
            for (int r = 0; r < _rows.Count; r++)
            {
                foreach (Transform g in _rows[r])
                {
                    var mat = g.GetComponent<Renderer>().sharedMaterial;
                    mat.SetFloat("_Alpha", 0.55f);
                    yield return new WaitForSeconds(0.012f + (float)rng.NextDouble() * 0.02f);
                }
                yield return new WaitForSeconds(0.03f);
            }
        }

        private IEnumerator FadeOut()
        {
            const float dur = 0.45f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                foreach (Material m in _mats) { m.SetFloat("_Alpha", 0.55f * k); }
                yield return null;
            }
            gameObject.SetActive(false);
            _run = null;
        }

        private void Update()
        {
            if (_barFill == null) { return; }
            Vector3 s = _barFill.localScale;
            float w = Mathf.MoveTowards(s.x, PanelWidth * _progress, Time.deltaTime * 1.4f);
            _barFill.localScale = new Vector3(w, s.y, 1f);
            // Left-anchored growth.
            _barFill.localPosition = new Vector3(-PanelWidth * 0.5f + w * 0.5f,
                                                 _barFill.localPosition.y,
                                                 _barFill.localPosition.z);
            int pct = Mathf.RoundToInt(Mathf.Clamp01(w / PanelWidth) * 100f);
            DcvrText.SetText(_percentLabel, pct + "%");
        }

        private static Material Holo(Color c, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return null; }
            var m = new Material(s) { name = "DCVR_PreviewMat" };
            m.SetColor("_Color", c);
            m.SetFloat("_Alpha", alpha);
            m.SetFloat("_ScanSpeed", 0f);
            return m;
        }
    }
}
