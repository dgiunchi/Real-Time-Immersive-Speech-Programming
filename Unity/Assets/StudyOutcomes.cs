using System;
using System.Collections;
using System.Collections.Generic;
using Ubiq.Messaging;
using UnityEngine;

/// <summary>
/// Executes study outcomes described by the server.
///
/// WHY THIS EXISTS
/// The original DreamCodeVR pipeline compiled injected C# at runtime with
/// RoslynCSharp (Assembly.Load of freshly built IL). That works in the Editor
/// (Mono/JIT) but CANNOT work in a standalone Quest build, which uses IL2CPP
/// (ahead-of-time, no JIT) — so on the headset every injection silently failed.
///
/// A Wizard-of-Oz study only plays pre-planned outcomes, so no runtime
/// compilation is needed. The server sends a small JSON *spec* describing what
/// should happen and this component performs it with ordinary compiled code.
///
/// Because the spec (including feedback wording and agent dialogue) lives on the
/// server, task variants can be authored/edited in Server/.../app.js WITHOUT
/// rebuilding and reinstalling the APK — important, since each rebuild costs
/// minutes and a headset deploy.
///
/// Channel: NetworkId 99
///     { type:"StudyOutcome", peer:"WizardOfOz", data:"<json OutcomeSpec>" }
/// </summary>
public class StudyOutcomes : MonoBehaviour
{
    public NetworkId networkId = new NetworkId(99);

    [Tooltip("Reference point for spawning. If unset, uses the main camera (in front of the participant).")]
    public Transform spawnOrigin;

    [Tooltip("Fake 'AI thinking' delay (seconds) before an outcome appears, so it feels like live processing.")]
    public float thinkingDelayMin = 1.2f;
    public float thinkingDelayMax = 2.5f;

    private NetworkContext context;

    /// <summary>What the server asks us to do. All content is server-authored.</summary>
    [Serializable]
    public class OutcomeSpec
    {
        public string action = "spawn";   // spawn | recolor | orbit | clear | condition | mic
        public string shape = "sphere";   // sphere | cube | cylinder | capsule
        public string pos = "hand";       // hand | origin | offset | high
        public float scaleX = 0.15f, scaleY = 0.15f, scaleZ = 0.15f;
        public string color = "";         // "#RRGGBB", empty = default
        public bool physics = false;
        public bool useCollider = true;
        public int count = 1;
        public bool applyToAll = false;   // recolor: hit every renderer (ambiguous-target error)
        public bool revert = false;       // recolor: revert after a moment (flaky-material error)
        public bool drift = false;        // orbit: fly away instead of orbiting
        public string orbitTarget = "cube"; // cube | origin
        public string orbitAxis = "up";     // up | forward | right
        public float orbitSpeed = 60f;
        public bool stopOnCollision = false;

        // Feedback content (conditions B and C) — authored server-side.
        public string label = "";      // what the system claims it did
        public string errorText = "";  // plain-language explanation; empty = treated as success
        public string agentPre = "";   // condition C: spoken before the result
        public string agentPost = "";  // condition C: spoken after the result

        public string value = "";      // payload for condition/mic actions
    }

    [Serializable]
    private struct Message { public string type; public string peer; public string data; }

    void Start()
    {
        context = NetworkScene.Register(this, networkId);
    }

    // ── Origin helpers ────────────────────────────────────────────────────────
    private Vector3 OriginPos => spawnOrigin ? spawnOrigin.position
        : (Camera.main ? Camera.main.transform.position : Vector3.zero);
    private Vector3 OriginFwd => spawnOrigin ? spawnOrigin.forward
        : (Camera.main ? Camera.main.transform.forward : Vector3.forward);

    private Vector3 ResolvePosition(string pos)
    {
        switch (pos)
        {
            case "origin": return Vector3.zero;                       // "wrong place" error
            case "offset": return OriginPos + OriginFwd * 0.9f;
            case "high":   return OriginPos + OriginFwd * 0.3f + Vector3.up * 0.6f;
            default:       return OriginPos + OriginFwd * 0.3f;       // "hand"
        }
    }

    // ── Network entry point ───────────────────────────────────────────────────
    public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
    {
        Message m = data.FromJson<Message>();
        if (string.IsNullOrWhiteSpace(m.data)) return;

        OutcomeSpec spec;
        try { spec = JsonUtility.FromJson<OutcomeSpec>(m.data); }
        catch (Exception e) { Debug.LogWarning("[StudyOutcomes] Bad spec: " + e.Message); return; }
        if (spec == null) return;

        // Control actions are immediate; visible outcomes get the thinking delay.
        if (spec.action == "condition") { SetCondition(spec.value); return; }
        if (spec.action == "mic")       { SetRemoteRecording(spec.value == "start"); return; }
        if (spec.action == "clear")     { ResetScene(); return; }

        StartCoroutine(RunAfterThinking(spec));
    }

    private IEnumerator RunAfterThinking(OutcomeSpec spec)
    {
        var cond = FindObjectOfType<StudyConditionManager>(true);
        bool feedback = !cond || !cond.IsConditionA();
        bool embodied = cond && cond.IsConditionC();

        // The "thinking" state belongs to whichever channel will deliver the
        // explanation, so it must not appear on the panel in condition C.
        if (feedback && !embodied)
        {
            var panel = FindObjectOfType<FeedbackPanelController>(true);
            if (panel) panel.ShowProcessing();
        }

        float wait = UnityEngine.Random.Range(thinkingDelayMin, thinkingDelayMax);

        // Condition C: acknowledge the request before the result appears.
        if (embodied && !string.IsNullOrWhiteSpace(spec.agentPre))
        {
            var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
            if (agent)
            {
                agent.SpeakCustom(spec.agentPre);
                wait = Mathf.Max(wait, EstimateSpeech(spec.agentPre) + 0.3f);
            }
        }

        yield return new WaitForSeconds(wait);

        Apply(spec);
        NotifyFeedback(spec);
    }

    private static float EstimateSpeech(string text) =>
        string.IsNullOrEmpty(text) ? 0f : Mathf.Max(2f, text.Length * 0.055f);

    // ── Outcome execution ─────────────────────────────────────────────────────
    public void Apply(OutcomeSpec spec)
    {
        Debug.Log($"[StudyOutcomes] {spec.action} shape={spec.shape} pos={spec.pos} err={!string.IsNullOrEmpty(spec.errorText)}");
        switch (spec.action)
        {
            case "spawn":   DoSpawn(spec);   break;
            case "recolor": DoRecolor(spec); break;
            case "orbit":   DoOrbit(spec);   break;
            default: Debug.LogWarning("[StudyOutcomes] Unknown action: " + spec.action); break;
        }
    }

    private void DoSpawn(OutcomeSpec spec)
    {
        Vector3 basePos = ResolvePosition(spec.pos);
        int n = Mathf.Max(1, spec.count);

        for (int i = 0; i < n; i++)
        {
            // Multiple objects (the "over-interpreted" error) fan out in a ring.
            Vector3 p = n == 1 ? basePos
                : basePos + Quaternion.Euler(0, i * (360f / n), 0) * Vector3.right * 0.8f;

            var go = Primitive(ParseShape(spec.shape), p,
                new Vector3(spec.scaleX, spec.scaleY, spec.scaleZ), ParseColor(spec.color));
            go.tag = "Interactable";

            if (!spec.useCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col) col.enabled = false;   // falls through the floor
            }
            if (spec.physics) go.AddComponent<Rigidbody>().useGravity = true;
            if (spec.drift)
            {
                var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.AddForce(Vector3.forward * 2f, ForceMode.VelocityChange);
            }
        }
    }

    private void DoRecolor(OutcomeSpec spec)
    {
        Color c = ParseColor(spec.color) ?? Color.green;

        if (spec.applyToAll)
        {
            // The "wrong target" error repaints the whole environment, including
            // scenery the study did not create. Those renderers are not destroyed
            // by a reset, so their colours have to be remembered and put back —
            // otherwise the next condition starts in a green room.
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                Remember(r);
                r.material.color = c;
            }
            return;
        }

        var target = FindInteractable();
        if (!target) { Debug.LogWarning("[StudyOutcomes] recolor: nothing to recolor"); return; }

        Remember(target);
        target.material.color = c;
        if (spec.revert) StartCoroutine(RevertColour(target));
    }

    /// Restores the object's real previous colour, not an assumed white — the
    /// participant must see it return to how it actually was.
    private IEnumerator RevertColour(Renderer target)
    {
        yield return new WaitForSeconds(2f);
        if (target && originalColours.TryGetValue(target, out var original))
            target.material.color = original;
    }

    // ── Original-colour bookkeeping ───────────────────────────────────────────
    private readonly Dictionary<Renderer, Color> originalColours = new Dictionary<Renderer, Color>();

    /// Records a renderer's colour the first time the study changes it.
    private void Remember(Renderer r)
    {
        if (r && !originalColours.ContainsKey(r)) originalColours[r] = r.material.color;
    }

    private void RestoreColours()
    {
        foreach (var kv in originalColours)
            if (kv.Key) kv.Key.material.color = kv.Value;
        originalColours.Clear();
    }

    private void DoOrbit(OutcomeSpec spec)
    {
        var ball = FindInteractable();
        GameObject go = ball ? ball.gameObject
            : Primitive(PrimitiveType.Sphere, ResolvePosition("hand"), Vector3.one * 0.15f, null);
        go.tag = "Interactable";

        foreach (var old in go.GetComponents<StudyOrbit>()) Destroy(old);   // don't stack orbits

        // "drift" is the mis-heard-verb error: the object is sent away instead of
        // being set circling, so it must not get an orbit component at all.
        if (spec.drift)
        {
            var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.AddForce(OriginFwd * 1.5f + Vector3.up * 0.4f, ForceMode.VelocityChange);
            return;
        }

        var orbit = go.AddComponent<StudyOrbit>();
        orbit.speed = spec.orbitSpeed;
        orbit.stopOnCollision = spec.stopOnCollision;
        orbit.axis = spec.orbitAxis == "forward" ? Vector3.forward
                   : spec.orbitAxis == "right"   ? Vector3.right
                   : Vector3.up;

        if (spec.orbitTarget == "origin") orbit.useWorldOrigin = true;   // orbits the wrong thing
        else orbit.centre = FindOrCreateCube();
    }

    // ── Feedback (conditions B and C) ─────────────────────────────────────────
    // Exactly one channel carries the explanation: the panel in B, the agent in
    // C. See StudyConditionManager for why these are exclusive rather than
    // cumulative.
    private void NotifyFeedback(OutcomeSpec spec)
    {
        var cond = FindObjectOfType<StudyConditionManager>(true);
        if (cond && cond.IsConditionA()) return;   // A shows nothing, by design

        bool embodied = cond && cond.IsConditionC();

        if (!embodied)
        {
            var panel = FindObjectOfType<FeedbackPanelController>(true);
            if (panel)
            {
                if (string.IsNullOrWhiteSpace(spec.errorText)) panel.ShowSuccess(spec.label);
                else panel.ShowError(spec.label, spec.errorText);
            }
        }
        else if (!string.IsNullOrWhiteSpace(spec.agentPost))
        {
            var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
            if (agent) agent.SpeakCustom(spec.agentPost);
        }
    }

    // ── Scene management ──────────────────────────────────────────────────────
    /// Returns the scene to its pre-trial state: generated objects removed,
    /// environment colours restored, feedback and agent cleared. Every trial
    /// starts from here so no participant inherits the previous condition.
    private void ResetScene()
    {
        StopAllCoroutines();          // cancel a pending colour revert or thinking delay

        RestoreColours();

        for (int i = CreatedRoot.childCount - 1; i >= 0; i--)
            Destroy(CreatedRoot.GetChild(i).gameObject);

        foreach (var go in GameObject.FindGameObjectsWithTag("Interactable")) Destroy(go);
        foreach (var go in GameObject.FindGameObjectsWithTag("game")) Destroy(go);

        var panel = FindObjectOfType<FeedbackPanelController>(true);
        if (panel) panel.Clear();

        // Agent must return to idle, not be left mid-sentence from the last trial.
        var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
        if (agent) agent.StopSpeaking();
        var body = FindObjectOfType<EmbodiedAgentBody>(true);
        if (body) body.OnFinishedSpeaking();

        var transcript = FindObjectOfType<TranscriptDisplay>(true);
        if (transcript) transcript.ClearTranscript();
    }

    private void SetRemoteRecording(bool on)
    {
        var mic = FindObjectOfType<MicrophoneCapture>(true);
        if (mic) mic.SetRemoteRecordOverride(on);
        else Debug.LogWarning("[StudyOutcomes] No MicrophoneCapture found.");
    }

    private void SetCondition(string ab)
    {
        var mgr = FindObjectOfType<StudyConditionManager>(true);
        if (!mgr) { Debug.LogWarning("[StudyOutcomes] No StudyConditionManager."); return; }
        switch ((ab ?? "").ToUpperInvariant())
        {
            case "A": mgr.SetConditionA(); break;
            case "B": mgr.SetConditionB(); break;
            case "C": mgr.SetConditionC(); break;
            default: Debug.LogWarning("[StudyOutcomes] Unknown condition: " + ab); break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Everything the study creates lives under one container so a reset removes
    // all of it — tags alone used to miss untagged objects.
    private static Transform createdRoot;
    private static Transform CreatedRoot
    {
        get
        {
            if (!createdRoot)
            {
                var go = GameObject.Find("StudyCreatedObjects") ?? new GameObject("StudyCreatedObjects");
                createdRoot = go.transform;
            }
            return createdRoot;
        }
    }

    private GameObject Primitive(PrimitiveType type, Vector3 pos, Vector3 scale, Color? color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(CreatedRoot, true);
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (color.HasValue) go.GetComponent<Renderer>().material.color = color.Value;
        return go;
    }

    private static PrimitiveType ParseShape(string s)
    {
        switch ((s ?? "").ToLowerInvariant())
        {
            case "cube":     return PrimitiveType.Cube;
            case "cylinder": return PrimitiveType.Cylinder;
            case "capsule":  return PrimitiveType.Capsule;
            default:         return PrimitiveType.Sphere;
        }
    }

    private static Color? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : (Color?)null;
    }

    private Renderer FindInteractable()
    {
        foreach (var r in FindObjectsOfType<Renderer>())
            if (r.gameObject.CompareTag("Interactable")) return r;
        return null;
    }

    private Transform FindOrCreateCube()
    {
        // Reuse the study's own cube. (Scanning the scene for any BoxCollider
        // used to pick up random environment objects like fences.)
        var existing = CreatedRoot.Find("OrbitCube");
        if (existing) return existing;

        var cube = Primitive(PrimitiveType.Cube, ResolvePosition("offset"),
            Vector3.one * 0.25f, new Color(0.85f, 0.4f, 0.15f));
        cube.name = "OrbitCube";
        return cube.transform;
    }
}

/// <summary>Pre-compiled orbit motion (replaces the runtime-compiled orbit scripts).</summary>
public class StudyOrbit : MonoBehaviour
{
    public Transform centre;
    public Vector3 centrePoint;
    public bool useFixedPoint;
    public bool useWorldOrigin;
    public Vector3 axis = Vector3.up;
    public float speed = 60f;
    public bool stopOnCollision;

    void Update()
    {
        Vector3 c;
        if (useWorldOrigin)     c = Vector3.zero;
        else if (useFixedPoint) c = centrePoint;
        else if (centre)        c = centre.position;
        else                    return;
        transform.RotateAround(c, axis, speed * Time.deltaTime);
    }

    void OnCollisionEnter(Collision _)
    {
        if (stopOnCollision) speed = 0f;
    }
}
