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
        // noop | spawn | move | recolor | orbit | setup | clear | condition | mic
        public string action = "spawn";
        public string shape = "sphere";   // sphere | cube | cylinder | capsule
        public string pos = "hand";       // hand | origin | offset | high | floor
        public float scaleX = 0.15f, scaleY = 0.15f, scaleZ = 0.15f;
        public string color = "";         // "#RRGGBB", empty = default
        public bool physics = false;
        public bool useCollider = true;
        public int count = 1;

        // Named scene objects. The study scene holds a sphere, a cube and a
        // campfire; errors act on them by name so the server can say "move the
        // cube instead of the sphere" without knowing anything about the scene.
        public string target = "";        // sphere | cube | lantern | ball …
        public string moveTo = "";        // campfire | sphere | cube — destination
        public bool away = false;         // move the wrong way (direction ambiguous)
        public bool far = false;          // arrive, but nowhere near (imprecise "next to")
        public bool spin = false;         // arrive correctly, then start spinning
        public float scaleMultiplier = 1f;// arrive correctly, but change size

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
            // "floor" is the ambiguous-placement error: it appears, but at the
            // participant's feet rather than in their hand.
            case "floor":  return new Vector3(OriginPos.x, 0.05f, OriginPos.z) + OriginFwd * 0.5f;
            // Task 4 (system behaviour): the object is created correctly but
            // lands outside the field of view. Placed well behind and slightly
            // up so it is unmistakable once they turn, but invisible until then.
            case "behind": return OriginPos - OriginFwd * 2.5f + Vector3.up * 1.2f;
            // Its success counterpart: clearly in front and above, where they
            // would actually look for something "above the campfire".
            case "front":  return OriginPos + OriginFwd * 2.0f + Vector3.up * 1.4f;
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
        if (spec.action == "setup")     { EnsureSceneObjects(); return; }

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
            // "noop" is the missing-detail error: the system appears to have
            // understood but produced nothing, so only the feedback fires.
            case "noop":    break;
            case "spawn":   DoSpawn(spec);   break;
            case "move":    DoMove(spec);    break;
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

    // ── Named scene objects ───────────────────────────────────────────────────
    // The study scene is "a sphere, a cube and a campfire". The sphere and cube
    // are rebuilt at every trial start so each trial begins from an identical
    // arrangement; the campfire belongs to the DreamCodeVR scene and is found by
    // name. Errors address these by name, which is what lets the server express
    // "move the cube instead of the sphere" without knowing the scene layout.

    private const string SphereName = "StudySphere";
    private const string CubeName   = "StudyCube";

    /// Creates the sphere and cube if they are missing. Idempotent.
    private void EnsureSceneObjects()
    {
        Vector3 basePos = OriginPos + OriginFwd * 1.6f;
        Vector3 right = Vector3.Cross(Vector3.up, OriginFwd).normalized;

        if (!FindNamed(SphereName))
        {
            var go = Primitive(PrimitiveType.Sphere, basePos - right * 0.5f + Vector3.up * 0.1f,
                Vector3.one * 0.22f, null);
            go.name = SphereName;
            go.tag = "Interactable";
        }
        if (!FindNamed(CubeName))
        {
            var go = Primitive(PrimitiveType.Cube, basePos + right * 0.5f + Vector3.up * 0.1f,
                Vector3.one * 0.22f, null);
            go.name = CubeName;
            go.tag = "Interactable";
        }
    }

    private GameObject FindNamed(string n)
    {
        for (int i = 0; i < CreatedRoot.childCount; i++)
        {
            var c = CreatedRoot.GetChild(i);
            if (c && c.name == n) return c.gameObject;
        }
        return null;
    }

    /// Resolves a server-side object word ("sphere", "cube", "campfire") to a
    /// transform. Falls back to the most recently spawned object so a variant
    /// naming something bespoke (a lantern, a ball) still resolves.
    private Transform Resolve(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        string w = word.Trim().ToLowerInvariant();

        if (w.Contains("fire") || w.Contains("camp")) return FindCampfire();

        if (w.Contains("sphere") || w.Contains("ball") || w.Contains("cube"))
        {
            EnsureSceneObjects();
            var go = FindNamed(w.Contains("cube") ? CubeName : SphereName);
            if (go) return go.transform;
        }

        var r = FindInteractable();
        return r ? r.transform : null;
    }

    /// The campfire is part of the authored scene, so it is located by name
    /// rather than created. If the scene has none, a point ahead of the
    /// participant stands in so the task still reads correctly.
    private Transform FindCampfire()
    {
        foreach (var t in FindObjectsOfType<Transform>())
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("campfire") || n.Contains("bonfire") || n.Contains("fireplace"))
                return t;
        }
        var anchor = FindNamed("StudyCampfireAnchor");
        if (!anchor)
        {
            anchor = new GameObject("StudyCampfireAnchor");
            anchor.transform.SetParent(CreatedRoot, false);
            anchor.transform.position = OriginPos + OriginFwd * 3.2f;
        }
        return anchor.transform;
    }

    // ── Move ──────────────────────────────────────────────────────────────────
    private void DoMove(OutcomeSpec spec)
    {
        var mover = Resolve(spec.target);
        if (!mover) { Debug.LogWarning("[StudyOutcomes] move: no object named " + spec.target); return; }

        var dest = Resolve(spec.moveTo);
        Vector3 to = DestinationFor(mover, dest, spec);

        foreach (var old in mover.GetComponents<StudyGlide>()) Destroy(old);
        var glide = mover.gameObject.AddComponent<StudyGlide>();
        glide.destination = to;
        // Growing mid-flight is the "plus extra" error; 1 leaves the size alone.
        glide.scaleMultiplier = spec.scaleMultiplier;
        glide.spinAfterArrival = spec.spin;
    }

    /// Where a move should end up, including the two error geometries:
    /// "away" sends it in the opposite direction, "far" overshoots well past.
    private Vector3 DestinationFor(Transform mover, Transform dest, OutcomeSpec spec)
    {
        if (!dest) return mover.position + OriginFwd * (spec.away ? -1f : 1f);

        Vector3 from = mover.position;
        Vector3 toward = dest.position - from;
        float keep = 0.45f;   // rest *next to* the target, not inside it

        if (spec.away) return from - toward.normalized * Mathf.Max(1.2f, toward.magnitude * 0.8f);
        if (spec.far)  return dest.position + toward.normalized * 2.5f + Vector3.right * 1.5f;

        return dest.position - toward.normalized * keep;
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

        // Task 3 recolours a *named* object and usually moves it in the same
        // breath, so the colour change and the move are one outcome rather than
        // two injects the participant would see as two separate events.
        Transform named = Resolve(spec.target);
        Renderer target = named ? named.GetComponentInChildren<Renderer>() : FindInteractable();
        if (!target) { Debug.LogWarning("[StudyOutcomes] recolor: nothing to recolor"); return; }

        Remember(target);
        target.material.color = c;
        if (spec.revert) StartCoroutine(RevertColour(target));

        if (!string.IsNullOrWhiteSpace(spec.moveTo)) DoMove(spec);
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

        // A correct outcome is silent in every condition: "no agent intervention
        // in C, no panel in B, nothing in A". Only errors are explained, so the
        // feedback itself never signals that the attempt succeeded.
        if (string.IsNullOrWhiteSpace(spec.errorText)) return;

        if (cond && cond.IsConditionC())
        {
            if (!string.IsNullOrWhiteSpace(spec.agentPost))
            {
                var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
                if (agent) agent.SpeakCustom(spec.agentPost);
            }
        }
        else
        {
            var panel = FindObjectOfType<FeedbackPanelController>(true);
            if (panel) panel.ShowError(spec.label, spec.errorText);
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

        // The sphere and cube are the scene, not trial debris — put them back so
        // every trial opens on the same arrangement the briefing describes.
        EnsureSceneObjects();
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
/// <summary>
/// Glides an object to a destination and optionally misbehaves on arrival.
///
/// The movement is deliberately smooth and unhurried rather than a teleport:
/// the participant has to watch it happen to notice *how* it went wrong (the
/// wrong object moving, the wrong direction, growing on the way), which is what
/// the "happened differently" and "plus extra" errors depend on.
/// </summary>
public class StudyGlide : MonoBehaviour
{
    public Vector3 destination;
    public float speed = 1.1f;
    public float scaleMultiplier = 1f;
    public bool spinAfterArrival;

    private Vector3 startScale;
    private float startDistance;
    private bool arrived;

    void Start()
    {
        startScale = transform.localScale;
        startDistance = Mathf.Max(0.01f, Vector3.Distance(transform.position, destination));

        // Physics would fight the glide and drop the object mid-flight.
        var rb = GetComponent<Rigidbody>();
        if (rb) { rb.useGravity = false; rb.isKinematic = true; }
    }

    void Update()
    {
        if (!arrived)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, destination, speed * Time.deltaTime);

            if (!Mathf.Approximately(scaleMultiplier, 1f))
            {
                float travelled = 1f - Mathf.Clamp01(
                    Vector3.Distance(transform.position, destination) / startDistance);
                transform.localScale = Vector3.Lerp(
                    startScale, startScale * scaleMultiplier, travelled);
            }

            if (Vector3.Distance(transform.position, destination) < 0.01f) arrived = true;
            return;
        }

        if (spinAfterArrival) transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
    }
}

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
