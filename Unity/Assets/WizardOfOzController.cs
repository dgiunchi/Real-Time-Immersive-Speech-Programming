using System;
using System.Collections.Generic;
using Ubiq.Messaging;
using Ubiq.Networking;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Wizard-of-Oz controller for the study.
///
/// Instead of sending participant audio to a live LLM, the researcher uses this
/// hidden panel to manually trigger pre-scripted 'good code' or 'error' responses
/// for each task. This gives full control over what the participant experiences
/// so results are reproducible across all conditions (A/B/C).
///
/// The researcher sees:
///   - The latest speech transcript from the participant
///   - Buttons to inject a Success or one of 4 Error responses for the active task
///
/// Usage: hide this panel from the participant (put it on a separate layer or
/// disable the canvas renderer on the participant's camera rig).
/// </summary>
public class WizardOfOzController : MonoBehaviour
{
    // ── Network ──────────────────────────────────────────────────────────────
    // Sends injected code/errors on the same network ID as CodeGenerationManager
    // so it goes straight into the code execution pipeline.
    public NetworkId codeNetworkId = new NetworkId(94);
    private NetworkContext context;

    // ── Task data ─────────────────────────────────────────────────────────────
    [Serializable]
    public struct TaskScript
    {
        [Tooltip("Short label shown on the WoZ panel.")]
        public string taskName;

        [TextArea(3, 10)]
        [Tooltip("C# code sent when the researcher triggers 'Success'.")]
        public string successCode;

        [TextArea(3, 10)]
        [Tooltip("Error 1 – missing/ambiguous detail (e.g. no position specified).")]
        public string error1Code;
        public string error1Description;

        [TextArea(3, 10)]
        [Tooltip("Error 2 – wrong interpretation of the instruction.")]
        public string error2Code;
        public string error2Description;

        [TextArea(3, 10)]
        [Tooltip("Error 3 – physics/collider issue (looks fine, breaks on interaction).")]
        public string error3Code;
        public string error3Description;

        [TextArea(3, 10)]
        [Tooltip("Error 4 – scale/count issue inherited from scene state.")]
        public string error4Code;
        public string error4Description;
    }

    [Header("Pre-scripted Tasks")]
    public TaskScript[] tasks;

    // ── Researcher UI ─────────────────────────────────────────────────────────
    [Header("Researcher Panel (hidden from participant)")]
    public GameObject wizardPanel;
    public TextMeshProUGUI lastTranscriptLabel;
    public TextMeshProUGUI activeTaskLabel;
    public TextMeshProUGUI statusLabel;
    public Button[] taskSelectButtons;   // One per task; highlights the active one

    // ── Internal state ────────────────────────────────────────────────────────
    private int activeTaskIndex = 0;
    private string lastTranscript = "";

    [Serializable]
    private struct CodeMessage
    {
        public string type;
        public string peer;
        public string data;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        context = NetworkScene.Register(this, codeNetworkId);
        PopulateDefaultTasks();
        RefreshUI();

        // Toggle the wizard panel with F12 so the researcher can show/hide it
        // without the participant noticing.
        if (wizardPanel) wizardPanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            if (wizardPanel) wizardPanel.SetActive(!wizardPanel.activeSelf);
        }
    }

    // ── Called by TranscriptionCollector via UnityEvent ──────────────────────

    public void OnTranscriptReceived(string transcript)
    {
        lastTranscript = transcript;
        if (lastTranscriptLabel) lastTranscriptLabel.text = "Heard: " + transcript;
    }

    // ── Task selection ────────────────────────────────────────────────────────

    public void SelectTask(int index)
    {
        if (index < 0 || index >= tasks.Length) return;
        activeTaskIndex = index;
        RefreshUI();
    }

    // ── Response injection ────────────────────────────────────────────────────

    public void InjectSuccess()        => InjectCode(tasks[activeTaskIndex].successCode, "SUCCESS");
    public void InjectError1()         => InjectCode(tasks[activeTaskIndex].error1Code, "ERROR-1: " + tasks[activeTaskIndex].error1Description);
    public void InjectError2()         => InjectCode(tasks[activeTaskIndex].error2Code, "ERROR-2: " + tasks[activeTaskIndex].error2Description);
    public void InjectError3()         => InjectCode(tasks[activeTaskIndex].error3Code, "ERROR-3: " + tasks[activeTaskIndex].error3Description);
    public void InjectError4()         => InjectCode(tasks[activeTaskIndex].error4Code, "ERROR-4: " + tasks[activeTaskIndex].error4Description);

    private void InjectCode(string code, string label)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            Debug.LogWarning($"[WoZ] No code set for: {label}");
            SetStatus("⚠ No code set for: " + label);
            return;
        }

        Debug.Log($"[WoZ] Injecting {label}");
        SetStatus("Injected: " + label);

        context.Send(new CodeMessage
        {
            type = "CodeGenerated",
            peer = "WizardOfOz",
            data = code
        });
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (activeTaskLabel && tasks.Length > activeTaskIndex)
            activeTaskLabel.text = "Task: " + tasks[activeTaskIndex].taskName;

        for (int i = 0; i < taskSelectButtons.Length; i++)
        {
            if (taskSelectButtons[i] == null) continue;
            var colors = taskSelectButtons[i].colors;
            colors.normalColor = i == activeTaskIndex ? Color.yellow : Color.white;
            taskSelectButtons[i].colors = colors;
        }
    }

    private void SetStatus(string msg)
    {
        if (statusLabel) statusLabel.text = msg;
    }

    // ── Default pre-scripted tasks for the study ──────────────────────────────
    // These match the 4 error types discussed with the supervisor:
    //   1. Missing detail / ambiguous instruction
    //   2. Wrong interpretation by the system
    //   3. Physics / collider issue (gradual reveal)
    //   4. Scale / count issue inherited from parent

    private void PopulateDefaultTasks()
    {
        if (tasks != null && tasks.Length > 0) return; // already set in Inspector

        tasks = new TaskScript[]
        {
            new TaskScript
            {
                taskName = "Task 1 – Create a ball at hand position",
                successCode = @"using UnityEngine;
public class Task1_CreateBall : MonoBehaviour {
    void Start() {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = transform.position + transform.forward * 0.3f;
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        go.tag = ""Interactable"";
    }
}",
                error1Code = @"using UnityEngine;
public class Task1_Error1_NoPosition : MonoBehaviour {
    void Start() {
        // Error: created at world origin, not at hand – participant asked for
        // 'a ball here' but no position was anchored to the hand.
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = Vector3.zero;
        go.AddComponent<Rigidbody>();
    }
}",
                error1Description = "Ball created at world origin, not at hand (missing position anchor)",

                error2Code = @"using UnityEngine;
public class Task1_Error2_WrongShape : MonoBehaviour {
    void Start() {
        // Error: system interpreted 'ball' as 'cube' due to ambiguous phrasing.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = transform.position + transform.forward * 0.3f;
        go.AddComponent<Rigidbody>();
    }
}",
                error2Description = "Cube created instead of sphere (wrong interpretation of 'ball')",

                error3Code = @"using UnityEngine;
public class Task1_Error3_NoCollider : MonoBehaviour {
    void Start() {
        // Error: sphere appears correct but its collider is disabled,
        // so the ball falls through the floor. Looks fine at first glance.
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = transform.position + transform.forward * 0.3f + Vector3.up * 0.5f;
        var col = go.GetComponent<SphereCollider>();
        if (col) col.enabled = false;
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
    }
}",
                error3Description = "Ball falls through the floor (collider disabled – gradual reveal error)",

                error4Code = @"using UnityEngine;
public class Task1_Error4_WrongScale : MonoBehaviour {
    void Start() {
        // Error: parent object has non-uniform scale inherited, resulting in
        // an ellipsoid instead of a sphere.
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = new Vector3(0.05f, 0.25f, 0.05f);
        go.transform.position = transform.position + transform.forward * 0.3f;
        go.AddComponent<Rigidbody>();
    }
}",
                error4Description = "Squashed ellipsoid instead of sphere (scale inherited from parent)"
            },

            new TaskScript
            {
                taskName = "Task 2 – Change the ball colour to green",
                successCode = @"using UnityEngine;
public class Task2_ChangeColour : MonoBehaviour {
    void Start() {
        var renderers = FindObjectsOfType<Renderer>();
        foreach (var r in renderers) {
            if (r.gameObject.CompareTag(""Interactable"")) {
                r.material.color = Color.green;
                return;
            }
        }
    }
}",
                error1Code = @"using UnityEngine;
public class Task2_Error1_NoTarget : MonoBehaviour {
    void Start() {
        // Error: 'it' was ambiguous – which object? Changes everything instead of selected.
        var renderers = FindObjectsOfType<Renderer>();
        foreach (var r in renderers)
            r.material.color = Color.green;
    }
}",
                error1Description = "All objects changed green (ambiguous 'it' – no selected object)",

                error2Code = @"using UnityEngine;
public class Task2_Error2_WrongColour : MonoBehaviour {
    void Start() {
        // Error: system interpreted 'green' as a shade of blue-green (teal).
        var renderers = FindObjectsOfType<Renderer>();
        foreach (var r in renderers) {
            if (r.gameObject.CompareTag(""Interactable"")) {
                r.material.color = new Color(0f, 0.7f, 0.7f);
                return;
            }
        }
    }
}",
                error2Description = "Ball changed to teal instead of green (wrong colour interpretation)",

                error3Code = @"using UnityEngine;
public class Task2_Error3_DelayedChange : MonoBehaviour {
    void Start() => StartCoroutine(DelayChange());
    System.Collections.IEnumerator DelayChange() {
        // Error: colour applies but then reverts after 2 s due to a missed
        // material instance issue. Looks correct at first.
        var renderers = FindObjectsOfType<Renderer>();
        Renderer target = null;
        foreach (var r in renderers) if (r.gameObject.CompareTag(""Interactable"")) { target = r; break; }
        if (target) target.material.color = Color.green;
        yield return new WaitForSeconds(2f);
        if (target) target.material.color = Color.white; // reverts
    }
}",
                error3Description = "Colour reverts after 2 seconds (material instance not instantiated)",

                error4Code = @"using UnityEngine;
public class Task2_Error4_NewObject : MonoBehaviour {
    void Start() {
        // Error: instead of changing colour, a new green sphere is created.
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.localScale = Vector3.one * 0.15f;
        go.transform.position = Vector3.up * 0.5f;
        go.GetComponent<Renderer>().material.color = Color.green;
    }
}",
                error4Description = "New green sphere created instead of recolouring existing ball"
            },

            new TaskScript
            {
                taskName = "Task 3 – Make the ball orbit the cube",
                successCode = @"using UnityEngine;
public class Task3_Orbit : MonoBehaviour {
    private Transform centre;
    private float speed = 60f;
    void Start() {
        var cubes = GameObject.FindGameObjectsWithTag(""Untagged"");
        foreach (var c in cubes) if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
    }
    void Update() {
        if (centre) transform.RotateAround(centre.position, Vector3.up, speed * Time.deltaTime);
    }
}",
                error1Code = @"using UnityEngine;
public class Task3_Error1_NoCentre : MonoBehaviour {
    void Update() {
        // Error: no centre object found; ball orbits world origin instead.
        transform.RotateAround(Vector3.zero, Vector3.up, 60f * Time.deltaTime);
    }
}",
                error1Description = "Ball orbits world origin, not the cube (centre unspecified)",

                error2Code = @"using UnityEngine;
public class Task3_Error2_WrongAxis : MonoBehaviour {
    private Transform centre;
    void Start() {
        var cubes = GameObject.FindGameObjectsWithTag(""Untagged"");
        foreach (var c in cubes) if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
    }
    void Update() {
        // Error: orbits on wrong axis (forward instead of up).
        if (centre) transform.RotateAround(centre.position, Vector3.forward, 60f * Time.deltaTime);
    }
}",
                error2Description = "Ball orbits on wrong axis (tilted plane instead of horizontal)",

                error3Code = @"using UnityEngine;
public class Task3_Error3_CollidesOnce : MonoBehaviour {
    private Transform centre;
    private float speed = 60f;
    void Start() {
        var cubes = GameObject.FindGameObjectsWithTag(""Untagged"");
        foreach (var c in cubes) if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
    }
    void Update() {
        if (!centre) return;
        transform.RotateAround(centre.position, Vector3.up, speed * Time.deltaTime);
    }
    void OnCollisionEnter(Collision col) {
        // Error: orbit radius is too small – ball collides with the cube and stops.
        speed = 0;
    }
}",
                error3Description = "Orbit radius too small; ball crashes into cube and stops",

                error4Code = @"using UnityEngine;
public class Task3_Error4_TooFast : MonoBehaviour {
    private Transform centre;
    void Start() {
        var cubes = GameObject.FindGameObjectsWithTag(""Untagged"");
        foreach (var c in cubes) if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
    }
    void Update() {
        // Error: 1000°/s orbit – too fast to see, looks like the ball has disappeared.
        if (centre) transform.RotateAround(centre.position, Vector3.up, 1000f * Time.deltaTime);
    }
}",
                error4Description = "Orbit speed too high (1000°/s) – ball looks like it has vanished"
            },

            new TaskScript
            {
                taskName = "Task 4 – Create a solar system (sphere + orbiting sphere)",
                successCode = @"using UnityEngine;
public class Task4_SolarSystem : MonoBehaviour {
    private GameObject planet;
    private float orbitSpeed = 45f;
    void Start() {
        var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        star.name = ""Star"";
        star.transform.position = transform.position;
        star.transform.localScale = Vector3.one * 0.4f;
        star.GetComponent<Renderer>().material.color = new Color(1f, 0.8f, 0f);

        planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = ""Planet"";
        planet.transform.position = star.transform.position + Vector3.right * 0.8f;
        planet.transform.localScale = Vector3.one * 0.15f;
        planet.GetComponent<Renderer>().material.color = new Color(0.2f, 0.4f, 1f);
    }
    void Update() {
        if (planet) planet.transform.RotateAround(transform.position, Vector3.up, orbitSpeed * Time.deltaTime);
    }
}",
                error1Code = @"using UnityEngine;
public class Task4_Error1_NoPlanet : MonoBehaviour {
    void Start() {
        // Error: only the star was created – participant said 'sphere' but did
        // not clearly specify that a second, orbiting sphere was needed.
        var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        star.transform.position = transform.position;
        star.transform.localScale = Vector3.one * 0.4f;
        star.GetComponent<Renderer>().material.color = new Color(1f, 0.8f, 0f);
    }
}",
                error1Description = "Only one sphere created – 'solar system' instruction was ambiguous",

                error2Code = @"using UnityEngine;
public class Task4_Error2_WrongScale : MonoBehaviour {
    private GameObject planet;
    void Start() {
        var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        star.transform.position = transform.position;
        // Error: planet inherits parent scale – appears as an ellipsoid.
        star.transform.localScale = new Vector3(0.4f, 0.2f, 0.4f);
        planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.transform.SetParent(star.transform);
        planet.transform.localPosition = Vector3.right * 2f;
    }
    void Update() {
        if (planet) planet.transform.RotateAround(transform.position, Vector3.up, 45f * Time.deltaTime);
    }
}",
                error2Description = "Planet appears squashed (non-uniform scale inherited from star parent)",

                error3Code = @"using UnityEngine;
public class Task4_Error3_Drift : MonoBehaviour {
    private GameObject planet;
    private Rigidbody planetRb;
    void Start() {
        var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        star.transform.position = transform.position;
        star.transform.localScale = Vector3.one * 0.4f;
        planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.transform.position = star.transform.position + Vector3.right * 0.8f;
        planet.transform.localScale = Vector3.one * 0.15f;
        // Error: Rigidbody was added – gravity pulls the planet off-orbit.
        planetRb = planet.AddComponent<Rigidbody>();
        planetRb.AddForce(Vector3.forward * 2f, ForceMode.VelocityChange);
    }
}",
                error3Description = "Planet drifts away instead of orbiting (gravity not disabled on Rigidbody)",

                error4Code = @"using UnityEngine;
public class Task4_Error4_TooMany : MonoBehaviour {
    void Start() {
        // Error: participant said 'a few planets'; system created 50.
        var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        star.transform.position = transform.position;
        star.transform.localScale = Vector3.one * 0.4f;
        for (int i = 0; i < 50; i++) {
            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.transform.position = star.transform.position + Quaternion.Euler(0, i * 7.2f, 0) * Vector3.right * 0.8f;
            p.transform.localScale = Vector3.one * 0.07f;
        }
    }
}",
                error4Description = "50 planets created (participant said 'a few'; over-generated)"
            }
        };
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage data) { }
}
