using System;
using System.Collections;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;

/// <summary>
/// Runs the study's pre-scripted outcomes as ordinary compiled C#.
///
/// WHY THIS EXISTS
/// The original DreamCodeVR pipeline compiles the injected C# at runtime with
/// RoslynCSharp (Assembly.Load of freshly built IL). That works in the Unity
/// Editor (Mono/JIT) but CANNOT work in a standalone Quest build, which uses
/// IL2CPP (ahead-of-time, no JIT) — Assembly.Load of new IL throws, so nothing
/// appears on the headset.
///
/// A Wizard-of-Oz study only ever plays a FIXED set of outcomes (4 tasks ×
/// 5 responses). So instead of compiling code on the device, the server sends
/// a tiny "task/response" id and this component runs the matching pre-built
/// behaviour. Pure compiled C# → works perfectly on the Quest.
///
/// The server sends, on NetworkId 95:
///     { type:"StudyOutcome", peer:"WizardOfOz", data:"task1/success" }
/// </summary>
public class StudyOutcomes : MonoBehaviour
{
    public NetworkId networkId = new NetworkId(99);

    [Tooltip("Where new objects spawn / the reference point. If unset, uses the main camera (in front of the participant).")]
    public Transform spawnOrigin;

    private NetworkContext context;

    [Serializable]
    private struct Message { public string type; public string peer; public string data; }

    void Start()
    {
        context = NetworkScene.Register(this, networkId);
    }

    // ── Origin helpers ────────────────────────────────────────────────────────
    private Vector3 OriginPos =>
        spawnOrigin ? spawnOrigin.position :
        (Camera.main ? Camera.main.transform.position : Vector3.zero);

    private Vector3 OriginFwd =>
        spawnOrigin ? spawnOrigin.forward :
        (Camera.main ? Camera.main.transform.forward : Vector3.forward);

    private Vector3 InFront => OriginPos + OriginFwd * 0.3f;

    // ── Network entry point ───────────────────────────────────────────────────
    public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
    {
        Message m = data.FromJson<Message>();
        if (string.IsNullOrWhiteSpace(m.data)) return;

        var parts = m.data.Split('/');
        if (parts.Length != 2) { Debug.LogWarning("[StudyOutcomes] Bad outcome id: " + m.data); return; }
        Run(parts[0], parts[1]);
    }

    /// <summary>Runs an outcome. task = "task1".."task4", response = "success"/"error1".."error4".</summary>
    public void Run(string task, string response)
    {
        Debug.Log($"[StudyOutcomes] Running {task}/{response}");
        switch (task)
        {
            case "reset": ResetScene(); break;
            case "condition": SetCondition(response); break;
            case "task1": Task1(response); break;
            case "task2": Task2(response); break;
            case "task3": Task3(response); break;
            case "task4": Task4(response); break;
            default: Debug.LogWarning("[StudyOutcomes] Unknown task: " + task); break;
        }
    }

    /// <summary>Destroys everything the study created, for a clean start.</summary>
    private void ResetScene()
    {
        foreach (var go in GameObject.FindGameObjectsWithTag("Interactable")) Destroy(go);
        foreach (var go in GameObject.FindGameObjectsWithTag("game")) Destroy(go);
    }

    /// <summary>Switches the visible feedback condition live (A/B/C) from the panel.</summary>
    private void SetCondition(string ab)
    {
        var mgr = FindObjectOfType<StudyConditionManager>(true);
        if (!mgr) { Debug.LogWarning("[StudyOutcomes] No StudyConditionManager to switch."); return; }
        switch (ab.ToUpperInvariant())
        {
            case "A": mgr.SetConditionA(); break;
            case "B": mgr.SetConditionB(); break;
            case "C": mgr.SetConditionC(); break;
            default: Debug.LogWarning("[StudyOutcomes] Unknown condition: " + ab); break;
        }
    }

    // ── Small helpers ─────────────────────────────────────────────────────────
    private GameObject Ball(Vector3 pos, float scale, Color? color = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        if (color.HasValue) go.GetComponent<Renderer>().material.color = color.Value;
        return go;
    }

    private Renderer FindInteractable()
    {
        foreach (var r in FindObjectsOfType<Renderer>())
            if (r.gameObject.CompareTag("Interactable")) return r;
        return null;
    }

    private Transform FindCube()
    {
        // The scene's cube: an untagged object carrying a BoxCollider.
        foreach (var c in GameObject.FindGameObjectsWithTag("Untagged"))
            if (c.GetComponent<BoxCollider>()) return c.transform;
        return null;
    }

    // ── TASK 1 — create a ball at hand ────────────────────────────────────────
    private void Task1(string r)
    {
        switch (r)
        {
            case "success": {
                var go = Ball(InFront, 0.15f);
                go.AddComponent<Rigidbody>().useGravity = true;
                go.tag = "Interactable";
                break;
            }
            case "error1": { // wrong position: world origin
                var go = Ball(Vector3.zero, 0.15f);
                go.AddComponent<Rigidbody>();
                go.tag = "Interactable";
                break;
            }
            case "error2": { // wrong shape: cube not sphere
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.position = InFront;
                go.transform.localScale = Vector3.one * 0.15f;
                go.AddComponent<Rigidbody>();
                go.tag = "Interactable";
                break;
            }
            case "error3": { // collider disabled -> falls through floor
                var go = Ball(InFront + Vector3.up * 0.5f, 0.15f);
                var col = go.GetComponent<SphereCollider>();
                if (col) col.enabled = false;
                go.AddComponent<Rigidbody>().useGravity = true;
                go.tag = "Interactable";
                break;
            }
            case "error4": { // squashed ellipsoid
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = InFront;
                go.transform.localScale = new Vector3(0.05f, 0.25f, 0.05f);
                go.AddComponent<Rigidbody>();
                go.tag = "Interactable";
                break;
            }
        }
    }

    // ── TASK 2 — colour the ball green ────────────────────────────────────────
    private void Task2(string r)
    {
        switch (r)
        {
            case "success": {
                var t = FindInteractable();
                if (t) t.material.color = Color.green;
                break;
            }
            case "error1": { // everything turns green
                foreach (var rend in FindObjectsOfType<Renderer>()) rend.material.color = Color.green;
                break;
            }
            case "error2": { // wrong shade (teal)
                var t = FindInteractable();
                if (t) t.material.color = new Color(0f, 0.7f, 0.7f);
                break;
            }
            case "error3": { // reverts after 2s
                StartCoroutine(RevertColour());
                break;
            }
            case "error4": { // new green ball instead of recolouring
                Ball(OriginPos + Vector3.up * 0.5f, 0.15f, Color.green);
                break;
            }
        }
    }

    private IEnumerator RevertColour()
    {
        var t = FindInteractable();
        if (t) t.material.color = Color.green;
        yield return new WaitForSeconds(2f);
        if (t) t.material.color = Color.white;
    }

    // ── TASK 3 — orbit the cube ───────────────────────────────────────────────
    private void Task3(string r)
    {
        var ball = FindInteractable();
        GameObject go = ball ? ball.gameObject : Ball(InFront, 0.15f);
        if (!ball) go.tag = "Interactable";

        // Clear any previous orbit so repeated injects don't stack.
        foreach (var old in go.GetComponents<StudyOrbit>()) Destroy(old);
        var orbit = go.AddComponent<StudyOrbit>();
        orbit.centre = FindCube();

        switch (r)
        {
            case "success": orbit.axis = Vector3.up;      orbit.speed = 60f; break;
            case "error1":  orbit.centre = null;          orbit.useWorldOrigin = true; orbit.axis = Vector3.up; orbit.speed = 60f; break; // wrong centre
            case "error2":  orbit.axis = Vector3.forward; orbit.speed = 60f; break;   // wrong axis
            case "error3":  orbit.axis = Vector3.up;      orbit.speed = 60f; orbit.stopOnCollision = true; break; // crashes into cube
            case "error4":  orbit.axis = Vector3.up;      orbit.speed = 1000f; break; // too fast
        }
    }

    // ── TASK 4 — solar system ─────────────────────────────────────────────────
    private void Task4(string r)
    {
        Vector3 centre = InFront;
        switch (r)
        {
            case "success": {
                var star = Ball(centre, 0.4f, new Color(1f, 0.8f, 0f));
                var planet = Ball(centre + Vector3.right * 0.8f, 0.15f, new Color(0.2f, 0.4f, 1f));
                var orbit = planet.AddComponent<StudyOrbit>();
                orbit.centrePoint = centre; orbit.useFixedPoint = true; orbit.axis = Vector3.up; orbit.speed = 45f;
                break;
            }
            case "error1": { // star only, no planet
                Ball(centre, 0.4f, new Color(1f, 0.8f, 0f));
                break;
            }
            case "error2": { // squashed star, planet parented (bad scale inherited)
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = centre;
                star.transform.localScale = new Vector3(0.4f, 0.2f, 0.4f);
                star.GetComponent<Renderer>().material.color = new Color(1f, 0.8f, 0f);
                var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planet.transform.SetParent(star.transform);
                planet.transform.localPosition = Vector3.right * 2f;
                var orbit = planet.AddComponent<StudyOrbit>();
                orbit.centrePoint = centre; orbit.useFixedPoint = true; orbit.axis = Vector3.up; orbit.speed = 45f;
                break;
            }
            case "error3": { // planet drifts off
                Ball(centre, 0.4f, new Color(1f, 0.8f, 0f));
                var planet = Ball(centre + Vector3.right * 0.8f, 0.15f);
                planet.AddComponent<Rigidbody>().AddForce(Vector3.forward * 2f, ForceMode.VelocityChange);
                break;
            }
            case "error4": { // 50 planets
                var star = Ball(centre, 0.4f, new Color(1f, 0.8f, 0f));
                for (int i = 0; i < 50; i++)
                {
                    var p = Ball(centre + Quaternion.Euler(0, i * 7.2f, 0) * Vector3.right * 0.8f, 0.07f);
                }
                break;
            }
        }
    }
}

/// <summary>Pre-compiled orbit motion (replaces the runtime-compiled orbit scripts).</summary>
public class StudyOrbit : MonoBehaviour
{
    public Transform centre;          // orbit around this transform, if set
    public Vector3 centrePoint;       // …or this fixed world point when useFixedPoint
    public bool useFixedPoint;
    public bool useWorldOrigin;       // …or Vector3.zero
    public Vector3 axis = Vector3.up;
    public float speed = 60f;
    public bool stopOnCollision;

    void Update()
    {
        Vector3 c;
        if (useWorldOrigin)     c = Vector3.zero;
        else if (useFixedPoint) c = centrePoint;
        else if (centre)        c = centre.position;
        else                    return; // no valid centre yet
        transform.RotateAround(c, axis, speed * Time.deltaTime);
    }

    void OnCollisionEnter(Collision _)
    {
        if (stopOnCollision) speed = 0f;
    }
}
