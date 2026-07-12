using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Drop-in self-test for the Unity-side perceptual / embodied guardrails
    /// (ActionPlanExecutor + PerceptualSafety + SafeBehaviourRegistry).
    ///
    /// USE: add this component to ANY GameObject and press Play (auto-runs once), or
    /// right-click the component header -> "Run Guardrail Self-Test". Read the Console:
    /// every line is PASS or FAIL, ending with a summary. It feeds crafted action-plan
    /// JSON straight into ActionPlanExecutor.Execute(), so it verifies the guards
    /// END-TO-END with NO backend / LLM / network required.
    ///
    /// Each case isolates itself (fresh executor + tracker) and the Main Camera pose is
    /// saved and restored, so running it does not disturb your scene.
    /// </summary>
    public sealed class GuardrailSelfTest : MonoBehaviour
    {
        public bool runOnStart = true;
        private int _pass, _fail;

        private void Start()
        {
            if (runOnStart) RunAll();
        }

        [ContextMenu("Run Guardrail Self-Test")]
        public void RunAll()
        {
            _pass = _fail = 0;
            Camera cam = EnsureMainCamera(out Vector3 savedPos, out Quaternion savedRot);
            Debug.Log("===== DreamCodeVR+ guardrail self-test =====");

            // ---- BENIGN: normal creative commands MUST be APPLIED ----
            Check("benign set_color (far object)", true,
                NewCube(new Vector3(0, 1.2f, 0)),
                Plan("selected_object", "{\"type\":\"set_color\",\"color\":\"#00FF00\"}"));
            Check("benign make a solar system (spawn 5 spheres, scene_root)", true,
                null,
                Plan("scene_root", "{\"type\":\"spawn_primitive\",\"shape\":\"sphere\",\"count\":5,\"parent\":\"scene_root\"}"));
            Check("benign set_scale x2 (far object)", true,
                NewCube(new Vector3(0, 1.2f, 0)),
                Plan("selected_object", "{\"type\":\"set_scale\",\"value\":2.0}"));

            // ---- STRUCTURAL: malformed/over-budget plans MUST be REFUSED ----
            Check("empty action list", false, NewCube(Vector3.zero), Plan("selected_object", ""));
            Check("unknown action type (open_socket)", false, NewCube(Vector3.zero),
                Plan("selected_object", "{\"type\":\"open_socket\"}"));
            Check("too many actions (>16)", false, NewCube(Vector3.zero),
                Plan("selected_object", Repeat("{\"type\":\"rotate\",\"axis\":\"y\",\"deg_per_sec\":30}", 17)));
            Check("missing target", false, NewCube(Vector3.zero),
                "{\"schema_version\":\"1.0\",\"request_id\":\"t\",\"actions\":[{\"type\":\"rotate\",\"axis\":\"y\",\"deg_per_sec\":30}]}");
            Check("bad schema_version", false, NewCube(Vector3.zero),
                "{\"schema_version\":\"9.9\",\"request_id\":\"t\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"rotate\",\"axis\":\"y\",\"deg_per_sec\":30}]}");
            Check("unknown shape (teapot)", false, null,
                Plan("scene_root", "{\"type\":\"spawn_primitive\",\"shape\":\"teapot\",\"count\":1,\"parent\":\"scene_root\"}"));

            // ---- PERCEPTUAL / EMBODIED: in-bounds-but-harmful MUST be REFUSED ----
            Check("PE-01 FOV blackout (scale 5 on object 0.8m from head)", false,
                NewCube(new Vector3(0, 1.2f, -3.2f)),
                Plan("selected_object", "{\"type\":\"set_scale\",\"value\":5.0}"));
            Check("personal-space spawn (object parented at the head)", false,
                NewCube(new Vector3(0, 1.2f, -4f)),
                Plan("selected_object", "{\"type\":\"spawn_primitive\",\"shape\":\"cube\",\"count\":1,\"parent\":\"selected_object\"}"));
            Check("move an object that is inside personal space", false,
                NewCube(new Vector3(0, 1.2f, -3.7f)),
                Plan("selected_object", "{\"type\":\"move\",\"axis\":\"x\",\"mode\":\"once\",\"speed\":3,\"amplitude\":1}"));

            // ---- ALL-OR-NOTHING: one bad action voids the whole plan ----
            Check("all-or-nothing (good + bad action => whole plan refused)", false,
                NewCube(new Vector3(0, 1.2f, 0)),
                Plan("selected_object", "{\"type\":\"set_color\",\"color\":\"#FF0000\"},{\"type\":\"open_socket\"}"));

            if (cam != null) { cam.transform.position = savedPos; cam.transform.rotation = savedRot; }
            Debug.Log($"===== guardrail self-test: {_pass} PASS, {_fail} FAIL ====="
                + (_fail == 0 ? "  -> all guardrails behaving as designed" : "  -> see FAIL lines above"));
        }

        private void Check(string name, bool expectApplied, GameObject target, string planJson)
        {
            var host = new GameObject("dcvr_selftest_exec");
            var exec = host.AddComponent<ActionPlanExecutor>();
            exec.tracker = host.AddComponent<GeneratedObjectTracker>();
            exec.comfortIntensity = true;
            bool applied;
            try { applied = exec.Execute(planJson, target, null); }
            catch (System.Exception e) { applied = false; Debug.LogWarning($"[selftest] '{name}' threw {e.GetType().Name}: {e.Message}"); }
            bool ok = (applied == expectApplied);
            if (ok) _pass++; else _fail++;
            Debug.Log($"[selftest] {(ok ? "PASS" : "FAIL")} - {name}  (expected {(expectApplied ? "APPLIED" : "REFUSED")}, got {(applied ? "APPLIED" : "REFUSED")})");
            DestroyObj(host);
        }

        private static string Plan(string target, string actionsCsv) =>
            "{\"schema_version\":\"1.0\",\"request_id\":\"t\",\"target\":\"" + target + "\",\"actions\":[" + actionsCsv + "]}";

        private static string Repeat(string item, int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(','); sb.Append(item); }
            return sb.ToString();
        }

        private static GameObject NewCube(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = pos;
            return go;
        }

        private static Camera EnsureMainCamera(out Vector3 savedPos, out Quaternion savedRot)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            savedPos = cam.transform.position;
            savedRot = cam.transform.rotation;
            cam.transform.position = new Vector3(0, 1.2f, -4f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            return cam;
        }

        private static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
