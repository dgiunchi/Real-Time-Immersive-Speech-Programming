// DreamCodeVR+ — the power-on sequence.
//
// Four seconds, once, at launch. Its job is to make the first moment in the headset feel
// authored rather than loaded: the space is dark, the platform lights, the rings spin up,
// the title resolves, the HUD fades in and the system reports READY.
//
// Kept short and strictly non-blocking. A wearer who has seen it twice should not have to
// sit through it, and nothing in the pipeline waits on it — a command arriving mid-sequence
// still executes, it simply arrives to a partly-lit stage.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrStartup : MonoBehaviour
    {
        private const float RimTime = 1.1f;
        private const float RingTime = 1.0f;
        private const float TitleTime = 0.9f;
        private const float HudTime = 0.8f;

        public static DcvrStartup Run(DcvrNearLayer near, DcvrHud hud, Transform title)
        {
            var go = new GameObject("DCVR_Startup");
            var s = go.AddComponent<DcvrStartup>();
            s.StartCoroutine(s.Sequence(near, hud, title));
            return s;
        }

        private IEnumerator Sequence(DcvrNearLayer near, DcvrHud hud, Transform title)
        {
            // Collect what we are going to fade. Materials are captured up front so the
            // sequence never allocates mid-animation.
            List<Material> rim = near != null ? near.EmissiveMaterials : new List<Material>();
            var targets = new List<float>(rim.Count);
            foreach (Material m in rim) { targets.Add(m.GetFloat("_Alpha")); }
            foreach (Material m in rim) { m.SetFloat("_Alpha", 0f); }


            if (hud != null) { hud.SetPresentation(0f); }

            DcvrAudio.Instance?.PowerOn();
            yield return new WaitForSeconds(0.35f);

            // 1. The rim lights, segment by segment, from the front around both sides.
            float t = 0f;
            while (t < RimTime)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / RimTime);
                for (int i = 0; i < rim.Count; i++)
                {
                    // Stagger by index so it sweeps rather than fading as one block.
                    float local = Mathf.Clamp01(k * 1.6f - (i / (float)Mathf.Max(rim.Count, 1)) * 0.6f);
                    rim[i].SetFloat("_Alpha", targets[i] * local);
                }
                yield return null;
            }

            // 2. Rings spin up (their own Update drives rotation; this just reveals them).
            yield return new WaitForSeconds(RingTime * 0.35f);

            // 3. The name piece runs its own perpetual loop, so the sequence only waits
            //    for it rather than driving it — two systems animating one object would
            //    fight each other.
            _ = title;
            yield return new WaitForSeconds(TitleTime * 0.5f);

            // 4. HUD fades in and reports ready.
            if (hud != null)
            {
                t = 0f;
                while (t < HudTime)
                {
                    t += Time.deltaTime;
                    hud.SetPresentation(Mathf.SmoothStep(0f, 1f, t / HudTime));
                    yield return null;
                }
                hud.SetPresentation(1f);
                hud.SetListening(false);
            }
        }
    }
}
