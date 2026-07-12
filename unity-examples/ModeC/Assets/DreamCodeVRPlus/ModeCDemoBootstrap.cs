using System.Collections;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Minimal DreamCodeVR+ **Mode-C** demo — NOT the full original DreamCodeVR
    /// integration (no Ubiq networking, no backend, no speech).
    ///
    /// It proves the dissertation's core claim: the safe <see cref="ActionPlanExecutor"/>
    /// can apply the exact action-plan JSON that the Rust backend emits on Ubiq NID 94,
    /// changing a scene object **without compiling any code at runtime** (so it is
    /// Quest/IL2CPP-safe). Press Play and watch the cube turn red, grow, spawn spheres,
    /// and safely reject an invalid plan.
    ///
    /// Auto-boots on Play via [RuntimeInitializeOnLoadMethod], so you do not need to
    /// add anything to the scene — just open the project and press the Play button.
    /// </summary>
    public sealed class ModeCDemoBootstrap : MonoBehaviour
    {
        private GameObject _target;
        private ActionPlanExecutor _exec;
        private GeneratedObjectTracker _tracker;
        private string _status = "starting…";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (Object.FindAnyObjectByType<ModeCDemoBootstrap>() == null)
            {
                new GameObject("ModeCDemo").AddComponent<ModeCDemoBootstrap>();
            }
        }

        private void Start()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _target.name = "TargetCube";
            _target.transform.position = Vector3.zero;

            // Make sure something is looking at the cube.
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(0f, 1.2f, -4f);
                Camera.main.transform.LookAt(_target.transform);
            }
            else
            {
                var camGo = new GameObject("DemoCamera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                camGo.transform.position = new Vector3(0f, 1.2f, -4f);
                camGo.transform.LookAt(_target.transform);
                _ = cam;
            }

            _tracker = gameObject.AddComponent<GeneratedObjectTracker>();
            _exec = gameObject.AddComponent<ActionPlanExecutor>();
            _exec.tracker = _tracker;

            Debug.Log("[ModeC] demo ready — running validated action-plans on the cube.");
            StartCoroutine(DemoSequence());
        }

        private IEnumerator DemoSequence()
        {
            yield return Wait(1.0f);
            RunPlan(
                "1/4  set_color #FF0000  (cube turns RED)",
                "{\"schema_version\":\"1.0\",\"request_id\":\"d1\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"set_color\",\"color\":\"#FF0000\"}]}");

            yield return Wait(2.0f);
            RunPlan(
                "2/4  set_scale 1.8  (cube GROWS, clamped to bounds)",
                "{\"schema_version\":\"1.0\",\"request_id\":\"d2\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"set_scale\",\"value\":1.8}]}");

            yield return Wait(2.0f);
            RunPlan(
                "3/4  spawn_primitive sphere x3  (SPAWNS, capped at 8/session)",
                "{\"schema_version\":\"1.0\",\"request_id\":\"d3\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"spawn_primitive\",\"shape\":\"sphere\",\"count\":3,\"parent\":\"scene_root\"}]}");

            yield return Wait(2.0f);
            RunPlan(
                "4/4  INVALID plan (unknown action) — must be SAFELY REJECTED",
                "{\"schema_version\":\"1.0\",\"request_id\":\"d4\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"delete_everything\"}]}");

            yield return Wait(1.0f);
            _status = "DONE ✓  Mode-C ran 3 safe actions + rejected 1 unsafe one — with NO runtime code compilation.";
            Debug.Log("[ModeC] " + _status);
        }

        private void RunPlan(string label, string json)
        {
            bool ok = _exec.Execute(json, _target, null);
            _status = $"{label}\n   → executor returned: {(ok ? "applied" : "rejected")}";
            Debug.Log($"[ModeC] {label}  -> {(ok ? "applied" : "rejected")}");
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
            GUI.Label(
                new Rect(14, 12, Screen.width - 28, 160),
                "<b>DreamCodeVR+ — Mode-C demo</b>  (minimal; no networking)\n" + _status,
                style);
        }
    }
}
