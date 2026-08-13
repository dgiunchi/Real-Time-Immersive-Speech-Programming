// DreamCodeVR+ — sound, generated rather than shipped.
//
// Every clip here is synthesised in C# at startup. That is not a novelty: it means no
// audio assets to license, attribute or redistribute in a dissertation artefact, nothing
// to lose in a repo transfer, and no APK size cost. It also lets the palette be described
// in the same terms as the visuals — the accept tone is literally a fifth above the
// listening tone, and the block is a detuned pair, so the relationship between them is a
// property of the code rather than of a file someone chose.
//
// Spatialised where it belongs in the world (the platform, the creation zone) and 2D where
// it belongs to the interface. The ambient bed is deliberately very quiet: a drone that is
// audible as a drone becomes fatiguing within minutes of wearing a headset.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrAudio : MonoBehaviour
    {
        private const int Rate = 44100;

        public static DcvrAudio Instance { get; private set; }

        private AudioSource _ui;        // 2D: interface feedback
        private AudioSource _world;     // 3D: events at the creation zone
        private AudioSource _ambient;   // 2D bed

        private AudioClip _powerOn, _accept, _block, _materialize, _listen, _bed;

        public static DcvrAudio Build(Vector3 worldAnchor)
        {
            var go = new GameObject("DCVR_Audio");
            var a = go.AddComponent<DcvrAudio>();
            a.Construct(worldAnchor);
            Instance = a;
            return a;
        }

        private void Construct(Vector3 worldAnchor)
        {
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;
            _ui.volume = 0.5f;

            var wgo = new GameObject("WorldSource");
            wgo.transform.SetParent(transform, false);
            wgo.transform.position = worldAnchor;
            _world = wgo.AddComponent<AudioSource>();
            _world.playOnAwake = false;
            _world.spatialBlend = 1f;          // fully positional
            _world.rolloffMode = AudioRolloffMode.Linear;
            _world.minDistance = 1.5f;
            _world.maxDistance = 18f;
            _world.volume = 0.7f;

            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.playOnAwake = false;
            _ambient.loop = true;
            _ambient.spatialBlend = 0f;
            _ambient.volume = 0.055f;          // felt, not heard

            _powerOn = Sweep("dcvr_power", 2.0f, 60f, 420f, 0.30f);
            _listen = Tone("dcvr_listen", 0.16f, 660f, 0.22f);
            _accept = Chord("dcvr_accept", 0.42f, 660f, 990f, 0.26f);   // a fifth up
            _block = Detuned("dcvr_block", 0.40f, 150f, 158f, 0.30f);   // beating, uneasy
            _materialize = Shimmer("dcvr_mat", 0.55f, 0.20f);
            _bed = Bed("dcvr_bed", 6f);

            _ambient.clip = _bed;
            _ambient.Play();
        }

        // ---- public triggers -------------------------------------------------------
        public void PowerOn() => Play(_ui, _powerOn);
        public void Listening() => Play(_ui, _listen);
        public void Accepted() { Play(_ui, _accept); Play(_world, _materialize); }
        public void Blocked() => Play(_ui, _block);
        public void Materialized() => Play(_world, _materialize);

        private static void Play(AudioSource src, AudioClip clip)
        {
            if (src != null && clip != null) { src.PlayOneShot(clip); }
        }

        // ---- synthesis -------------------------------------------------------------
        /// <summary>Rising sweep for power-on. Exponential in frequency so it reads as
        /// acceleration rather than a linear ramp.</summary>
        private static AudioClip Sweep(string name, float secs, float f0, float f1, float amp)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t * t);
                phase += 2f * Mathf.PI * f / Rate;
                // Fade in and out so there is no click at either end.
                float env = Mathf.Sin(t * Mathf.PI);
                data[i] = Mathf.Sin(phase) * env * amp;
            }
            return Make(name, data);
        }

        private static AudioClip Tone(string name, float secs, float freq, float amp)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Sin(t * Mathf.PI);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / Rate) * env * amp;
            }
            return Make(name, data);
        }

        /// <summary>Two tones a fifth apart, the upper entering slightly late. Consonant
        /// and rising: this is the sound of a request being allowed.</summary>
        private static AudioClip Chord(string name, float secs, float f1, float f2, float amp)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-3.2f * t);
                float a = Mathf.Sin(2f * Mathf.PI * f1 * i / Rate);
                float delay = Mathf.Clamp01((t - 0.10f) * 8f);
                float b = Mathf.Sin(2f * Mathf.PI * f2 * i / Rate) * delay;
                data[i] = (a * 0.6f + b * 0.4f) * env * amp;
            }
            return Make(name, data);
        }

        /// <summary>Two low tones a few hertz apart. The beating between them is what makes
        /// this read as wrong without being harsh — a refusal should be unambiguous, not
        /// punishing to hear repeatedly during a demo.</summary>
        private static AudioClip Detuned(string name, float secs, float f1, float f2, float amp)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-4.5f * t) * Mathf.Min(1f, t * 40f);
                float a = Mathf.Sin(2f * Mathf.PI * f1 * i / Rate);
                float b = Mathf.Sin(2f * Mathf.PI * f2 * i / Rate);
                data[i] = (a + b) * 0.5f * env * amp;
            }
            return Make(name, data);
        }

        /// <summary>Filtered noise sweeping upward: matter assembling.</summary>
        private static AudioClip Shimmer(string name, float secs, float amp)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            var rng = new System.Random(4242);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                // One-pole low-pass whose cutoff opens over time.
                float k = Mathf.Lerp(0.02f, 0.35f, t);
                lp += (white - lp) * k;
                float env = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.35f);
                data[i] = lp * env * amp;
            }
            return Make(name, data);
        }

        /// <summary>A very quiet, seamlessly looping bed: two detuned low sines plus a
        /// slow swell. Loop length is a whole number of cycles for both partials so the
        /// wrap is inaudible.</summary>
        private static AudioClip Bed(string name, float secs)
        {
            int n = (int)(Rate * secs);
            var data = new float[n];
            const float baseF = 55f;    // both are exact divisors of the loop length
            const float fifth = 82.5f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float swell = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / secs);
                float a = Mathf.Sin(2f * Mathf.PI * baseF * t);
                float b = Mathf.Sin(2f * Mathf.PI * fifth * t) * 0.5f;
                data[i] = (a + b) * 0.33f * swell;
            }
            return Make(name, data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
