// DreamCodeVR+ — the hint that gets out of the way.
//
// Onboarding and status are different jobs and this project had been doing both with one
// panel, which is why the panel could never leave: it was showing the pipeline stages, so
// removing it would have removed the security story too. Splitting them (§33) lets the
// tutorial behave like a tutorial.
//
//     DcvrTutorial      TutorialUI   — says what to try first, then RETIRES for good
//     DcvrHud           SystemStatusUI — LISTENING / GENERATING / VALIDATING / BLOCKED
//
// It retires on the first real creation rather than on a timer, because that is the moment
// it stops being useful and starts being a slab floating in front of the thing the user
// just made (§32). Retirement is permanent: a hint that comes back is not a hint.
//
// World-space and world-anchored like everything else — nothing here is parented to the
// camera, so the user can simply walk past it while it is still up.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrTutorial : MonoBehaviour
    {
        private const float PanelWidth = 0.92f;    // modest: ~20° at 2.6 m, not a wall
        private const float FadeSeconds = 0.9f;

        private readonly System.Collections.Generic.List<Material> _mats =
            new System.Collections.Generic.List<Material>();
        private GameObject _starterCube;
        private bool _retired;

        /// <summary>Build the hint next to the starter object.
        ///
        /// Offset sideways and slightly down from the creation zone on purpose: directly
        /// ahead is where the user's first creation will appear, and a tutorial that has
        /// to be dismissed before the demo can be seen is worse than none.</summary>
        public static DcvrTutorial Build(Vector3 anchor, GameObject starterCube)
        {
            var go = new GameObject("TutorialUI");
            go.transform.SetParent(null, true);
            go.transform.position = anchor + new Vector3(-0.85f, -0.10f, -0.15f);
            go.transform.rotation = Quaternion.Euler(0f, -18f, 0f);

            var t = go.AddComponent<DcvrTutorial>();
            t._starterCube = starterCube;
            t.Construct();
            return t;
        }

        private void Construct()
        {
            var plate = DcvrPrim.Create(PrimitiveType.Quad);
            plate.name = "TutorialUI_Plate";
            plate.transform.SetParent(transform, false);
            plate.transform.localScale = new Vector3(PanelWidth, 0.40f, 1f);
            Material plateMat = MakeTransparent(new Color(0.02f, 0.035f, 0.055f, 0.72f));
            plate.GetComponent<Renderer>().sharedMaterial = plateMat;
            _mats.Add(plateMat);

            DcvrText.Make(transform, "TRY SAYING", new Vector3(0f, 0.12f, -0.01f), 0.030f, DcvrWorld.Dim);
            DcvrText.Make(transform, "\"make this cube red\"", new Vector3(0f, 0.01f, -0.01f), 0.040f, Color.white);
            DcvrText.Make(transform, "then ask for anything you like",
                          new Vector3(0f, -0.11f, -0.01f), 0.026f, DcvrWorld.Dim);
        }

        private static Material MakeTransparent(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "TutorialUI_Mat" };
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
            m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// <summary>The user has built something real. The hint has done its job.
        ///
        /// Idempotent, because every creative path calls it and none of them should have
        /// to know whether another already did.</summary>
        public void RetireOnFirstCreation()
        {
            if (_retired) { return; }
            _retired = true;
            StartCoroutine(FadeAndGo());
        }

        private IEnumerator FadeAndGo()
        {
            var texts = GetComponentsInChildren<Renderer>(true);
            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.deltaTime;
                float k = 1f - t / FadeSeconds;
                foreach (Renderer r in texts)
                {
                    if (r == null || r.material == null) { continue; }
                    Color c = r.material.color;
                    c.a = k;
                    r.material.color = c;
                }
                // The starter cube shrinks away with the hint. It exists to be the first
                // thing you change; once you are building for real it is clutter sitting
                // in the middle of the creation area.
                if (_starterCube != null)
                {
                    _starterCube.transform.localScale = Vector3.one * (0.30f * Mathf.Max(k, 0.0001f));
                }
                yield return null;
            }

            if (_starterCube != null) { _starterCube.SetActive(false); }
            Destroy(gameObject);
        }
    }
}
