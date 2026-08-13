// DreamCodeVR+ — the starter cube, and knowing when to leave.
//
// A single small cube sits in the creation zone at startup for one reason: a deictic
// command ("make this red") issued before anything has been created still needs a referent,
// and refusing the very first thing a new user tries is a poor introduction.
//
// The moment a real creation lands it has done its job. Leaving it in view — along with the
// instruction panel that used to accompany it — made the scene look like a tutorial rather
// than a workspace, which is exactly wrong when the whole point is that the user can build
// anything. So it fades out and stops being a target.
//
// This replaces the onboarding panel entirely. The voice overlay already reports what the
// system is doing, so there is nothing left for a permanent instruction to say.

using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public sealed class DcvrStarterTarget : MonoBehaviour
    {
        private const float FadeSeconds = 0.7f;

        private GameObject _cube;
        private bool _retired;

        public static DcvrStarterTarget Attach(GameObject starterCube, ModeCNetworkedDemo client)
        {
            if (starterCube == null) { return null; }
            var go = new GameObject("DCVR_StarterTarget");
            go.transform.SetParent(null, true);
            var t = go.AddComponent<DcvrStarterTarget>();
            t._cube = starterCube;
            // Modest from the outset: it is a hint, not the subject of the scene.
            starterCube.transform.localScale = Vector3.one * 0.22f;
            client?.AttachStarterTarget(t);
            return t;
        }

        /// <summary>Called on the first real creation. Idempotent — every creative path
        /// calls it and none should have to know whether another already did.</summary>
        public void RetireOnFirstCreation()
        {
            if (_retired || _cube == null) { return; }
            _retired = true;
            StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            Vector3 from = _cube.transform.localScale;
            float t = 0f;
            while (t < FadeSeconds && _cube != null)
            {
                t += Time.deltaTime;
                _cube.transform.localScale = from * Mathf.Max(1f - t / FadeSeconds, 0.0001f);
                yield return null;
            }
            if (_cube != null) { _cube.SetActive(false); }
        }
    }
}
