using UnityEngine;

/// <summary>
/// A simple visible body for the Condition-C embodied agent, built entirely from
/// primitives at runtime (no art assets required). It gives the participant
/// something to attribute the "assistant" to, which is the whole point of the
/// embodiment condition.
///
/// Behaviour:
///   • Idle: floats near the feedback panel and bobs gently.
///   • Speaking: the "head" nods / pulses and the eyes brighten, driven by the
///     EmbodiedAgentDialogue speaking events.
///
/// Added and wired automatically by StudyUIBootstrapper in condition C.
/// </summary>
public class EmbodiedAgentBody : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("World position for the agent. Usually set beside the feedback panel by the bootstrapper.")]
    public Vector3 worldPosition = new Vector3(-0.9f, 1.5f, 2.0f);
    public float size = 0.18f;
    [Tooltip("If true, the agent turns to face the main camera each frame.")]
    public bool faceCamera = true;

    [Header("Colours")]
    public Color bodyColor = new Color(0.55f, 0.4f, 0.85f);
    public Color eyeColorIdle = new Color(0.5f, 0.8f, 1f);
    public Color eyeColorSpeaking = new Color(1f, 0.95f, 0.6f);

    private Transform root, head, leftEye, rightEye;
    private Renderer leftEyeR, rightEyeR;
    private float bobPhase;
    private float speakLevel;        // 0..1 smoothed "is speaking" amount
    private bool speaking;
    private bool pendingVisible = true;   // desired visibility if SetVisible ran before Build

    private void Start()
    {
        Build();
        root.gameObject.SetActive(pendingVisible);
    }

    // ── Construction ──────────────────────────────────────────────────────────
    private void Build()
    {
        root = new GameObject("EmbodiedAgent").transform;
        root.SetParent(transform, false);
        root.position = worldPosition;

        // Body (rounded — a slightly squashed sphere)
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        StripCollider(body);
        body.transform.SetParent(root, false);
        body.transform.localScale = new Vector3(size, size * 0.85f, size);
        Paint(body, bodyColor);

        // Head
        var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        StripCollider(headGo);
        headGo.transform.SetParent(root, false);
        headGo.transform.localPosition = new Vector3(0, size * 0.85f, 0);
        headGo.transform.localScale = Vector3.one * size * 0.75f;
        Paint(headGo, bodyColor * 1.1f);
        head = headGo.transform;

        // Eyes
        leftEye = MakeEye(new Vector3(-0.22f, 0.05f, 0.34f), out leftEyeR);
        rightEye = MakeEye(new Vector3(0.22f, 0.05f, 0.34f), out rightEyeR);

        SetEyeColor(eyeColorIdle);
    }

    private Transform MakeEye(Vector3 localPosFractionOfHead, out Renderer rend)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        StripCollider(eye);
        eye.transform.SetParent(head, false);
        eye.transform.localPosition = localPosFractionOfHead; // in head-local space
        eye.transform.localScale = Vector3.one * 0.28f;
        rend = eye.GetComponent<Renderer>();
        return eye.transform;
    }

    private static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c) Destroy(c);
    }

    private static void Paint(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        if (r) r.material.color = c;
    }

    private void SetEyeColor(Color c)
    {
        if (leftEyeR) leftEyeR.material.color = c;
        if (rightEyeR) rightEyeR.material.color = c;
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    private void Update()
    {
        if (root == null) return;

        // Gentle idle bob.
        bobPhase += Time.deltaTime;
        float bob = Mathf.Sin(bobPhase * 1.6f) * 0.02f;
        root.position = worldPosition + Vector3.up * bob;

        // Smooth the speaking level.
        speakLevel = Mathf.MoveTowards(speakLevel, speaking ? 1f : 0f, Time.deltaTime * 4f);

        if (head)
        {
            // Nod + pulse while speaking.
            float nod = speaking ? Mathf.Sin(Time.time * 10f) * 6f * speakLevel : 0f;
            head.localRotation = Quaternion.Euler(nod, 0, 0);
            float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.05f * speakLevel;
            head.localScale = Vector3.one * size * 0.75f * pulse;
        }

        SetEyeColor(Color.Lerp(eyeColorIdle, eyeColorSpeaking, speakLevel));

        if (faceCamera && Camera.main)
        {
            var to = Camera.main.transform.position - root.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.0001f)
                root.rotation = Quaternion.Slerp(root.rotation,
                    Quaternion.LookRotation(-to), Time.deltaTime * 5f);
        }
    }

    // ── Hooks (called by EmbodiedAgentDialogue events) ────────────────────────
    public void OnStartedSpeaking() { speaking = true; }
    public void OnFinishedSpeaking() { speaking = false; }

    public void SetVisible(bool visible)
    {
        pendingVisible = visible;
        if (root) root.gameObject.SetActive(visible);
    }
}
