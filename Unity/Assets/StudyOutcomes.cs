using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;
using UnityEngine.Networking;

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

        // Used to look up pre-recorded voice clips from Resources/AgentVoice/:
        //   {taskKey}_{variantKey}_pre.wav   — played during the thinking delay
        //   {taskKey}_{variantKey}_post.wav  — played with the explanation
        //
        // Keyed on variant as well as task because the agent's lines name the
        // object ("I made the lantern, but you didn't say what colour…") and
        // each variant uses a different one. A clip keyed on task alone would
        // speak the wrong noun for two thirds of participants — audible, and
        // exactly the kind of thing that makes a participant suspect a wizard.
        //
        // Clips are optional; the agent falls back to subtitles when absent.
        public string taskKey = "";
        public string variantKey = "";

        public string value = "";      // payload for condition/mic actions
    }

    [Serializable]
    private struct Message { public string type; public string peer; public string data; }

    void Start()
    {
        context = NetworkScene.Register(this, networkId);
        SnapshotAuthoredScene();
    }

    // ── Authored scene protection ─────────────────────────────────────────────
    //
    // Everything present before the study spawns anything belongs to the scene,
    // not to a trial, and must survive every reset. Recorded once at startup by
    // instance id, because names are not unique and tags are shared with the
    // objects trials create.

    private static readonly HashSet<int> authored = new HashSet<int>();
    private static bool authoredTaken;

    private static void SnapshotAuthoredScene()
    {
        if (authoredTaken) return;
        authoredTaken = true;
        foreach (var go in FindObjectsOfType<GameObject>(true))
            if (go) authored.Add(go.GetInstanceID());
    }

    private static bool IsAuthored(GameObject go)
    {
        return go && authored.Contains(go.GetInstanceID());
    }

    // Sends the actual world-space position of a spawned/moved object back to
    // the server control panel, so the CSV has ground-truth coordinates rather
    // than the approximate labels ("floor", "hand") that the server knew in advance.
    private IEnumerator ReportSceneEvent(string type, string name, string shape, Vector3 pos)
    {
        var body = $"{{\"type\":\"{type}\",\"name\":\"{name}\",\"shape\":\"{shape}\"," +
                   $"\"x\":{pos.x:F3},\"y\":{pos.y:F3},\"z\":{pos.z:F3}}}";
        // The study machine's real address when discovery has found it, and
        // 127.0.0.1 otherwise — which reaches the Mac only over the USB tunnel.
        // On an untethered headset localhost is the headset, so without this the
        // POST would quietly go nowhere and the confirmed-coordinate rows would
        // just never appear.
        var host = ServerAutoDiscovery.ResolvedHost ?? "127.0.0.1";
        using var req = new UnityWebRequest($"http://{host}:8181/scene-event", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        // Fire-and-forget: a failed POST is not a study error, just a missing log row.
    }

    /// <summary>
    /// Reports something only the headset can time.
    ///
    /// The server knows when it SENT a scripted outcome. It cannot know when the
    /// panel actually appeared, when the agent stopped talking, or when either
    /// went away — and in condition C those differ from the send time by however
    /// long the agent's pre-roll line took. "How long was the explanation
    /// available to them" is the denominator of every dwell measure, so it has
    /// to come from the side that displayed it.
    ///
    /// Static and fire-and-forget so any script can call it without a reference
    /// and without a failure here ever costing a participant anything.
    /// </summary>
    public static void ReportHeadsetEvent(MonoBehaviour host, string type,
                                          string detail = "", string value = "")
    {
        if (!host) return;
        host.StartCoroutine(PostHeadsetEvent(type, detail, value));
    }

    private static IEnumerator PostHeadsetEvent(string type, string detail, string value)
    {
        var body = $"{{\"type\":{JsonString(type)},\"detail\":{JsonString(detail)}," +
                   $"\"value\":{JsonString(value)},\"category\":\"feedback\"}}";
        var host = ServerAutoDiscovery.ResolvedHost ?? "127.0.0.1";
        using var req = new UnityWebRequest($"http://{host}:8181/headset-event", "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        // Fire-and-forget: a failed POST is a missing log row, not a study error.
    }

    /// Feedback text is authored prose and reaches this as JSON. An unescaped
    /// quote or newline in it would produce a malformed body that the server
    /// rejects, losing the row for exactly the wordiest explanations.
    private static string JsonString(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// FindGameObjectsWithTag throws outright when the tag is not declared in
    /// the project's TagManager, and an uncaught throw here takes the rest of
    /// the reset with it. Returning nothing keeps a missing tag survivable.
    /// </summary>
    private static GameObject[] TaggedObjects(string tag)
    {
        try { return GameObject.FindGameObjectsWithTag(tag); }
        catch (UnityException)
        {
            Debug.LogWarning($"[StudyOutcomes] tag '{tag}' is not defined in " +
                             "ProjectSettings/TagManager — add it or objects will leak.");
            return Array.Empty<GameObject>();
        }
    }

    /// <summary>
    /// Assigning an undeclared tag throws, and these assignments sit in the
    /// middle of outcome execution: one throw skipped the rest of the spawn AND
    /// the feedback that explains it, so the participant saw a failure with no
    /// explanation and the condition looked broken. Tagging is a convenience for
    /// finding objects later — never a reason to lose an outcome.
    /// </summary>
    private static void SafeTag(GameObject go, string tag)
    {
        if (!go) return;
        try { go.tag = tag; }
        catch (UnityException)
        {
            Debug.LogWarning($"[StudyOutcomes] tag '{tag}' is not defined in " +
                             "ProjectSettings/TagManager — continuing untagged.");
        }
    }

    private static bool HasTag(GameObject go, string tag)
    {
        if (!go) return false;
        try { return go.CompareTag(tag); }
        catch (UnityException) { return false; }
    }

    // ── Origin helpers ────────────────────────────────────────────────────────
    //
    // THE SCENE IS ANCHORED. IT DOES NOT FOLLOW THE PARTICIPANT.
    //
    // These used to fall back to Camera.main — the live headset — whenever
    // spawnOrigin was unset, which is always, because spawnOrigin is only
    // assigned from the legacy CodeGenerationManager and the study disables that
    // system at startup. So every position was computed from wherever the
    // participant happened to be standing at that instant, and the scene was
    // rebuilt around them each time they moved. Objects appeared in a different
    // place every trial, which is the "created at random places where I go"
    // report, and it quietly broke task 4 as well: "behind" was measured from a
    // forward vector that had already turned with them.
    //
    // The anchor is captured once, when the scene is first built or explicitly
    // re-anchored, and everything is placed relative to that. A participant can
    // then walk around the scene rather than dragging it with them.
    private Transform studyAnchor;

    private Vector3 OriginPos =>
        spawnOrigin ? spawnOrigin.position :
        studyAnchor ? studyAnchor.position :
        (Camera.main ? Flatten(Camera.main.transform.position) : Vector3.zero);

    private Vector3 OriginFwd =>
        spawnOrigin ? FlattenDir(spawnOrigin.forward) :
        studyAnchor ? studyAnchor.forward :
        (Camera.main ? FlattenDir(Camera.main.transform.forward) : Vector3.forward);

    /// Horizontal only. A head that is tilted down should not tip the whole
    /// scene into the floor, and "2.5 m behind" should mean behind, not behind
    /// and below.
    private static Vector3 FlattenDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    private static Vector3 Flatten(Vector3 p) => new Vector3(p.x, 0f, p.z);

    /// <summary>
    /// Pins the scene to where the participant is standing and facing now.
    ///
    /// Called when the scene is built and again from ResetScene, so each trial
    /// opens with the arrangement in front of the participant wherever they have
    /// ended up — and then stays put for the whole trial. Re-anchoring between
    /// trials rather than never is deliberate: a participant who has drifted
    /// should not have to walk back to find the campfire the briefing describes,
    /// but nothing should move underneath them mid-trial.
    /// </summary>
    private void AnchorSceneToParticipant()
    {
        if (!studyAnchor)
        {
            var go = new GameObject("StudyAnchor");
            go.transform.SetParent(CreatedRoot, false);
            studyAnchor = go.transform;
        }
        var cam = Camera.main;
        if (!cam) return;
        studyAnchor.position = Flatten(cam.transform.position);
        studyAnchor.rotation = Quaternion.LookRotation(FlattenDir(cam.transform.forward));
    }

    /// Where the participant is RIGHT NOW, flattened. Used only by "behind",
    /// which is about the current field of view rather than the scene layout.
    private Vector3 LivePos => Camera.main ? Flatten(Camera.main.transform.position) : OriginPos;
    private Vector3 LiveFwd => Camera.main ? FlattenDir(Camera.main.transform.forward) : OriginFwd;

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
            //
            // The ONLY position measured from the participant's live pose rather
            // than the anchor, and deliberately so: the manipulation is "outside
            // your field of view right now". Anchored, it would appear behind
            // where the scene was built — which, if they had turned, could be
            // directly in front of them, and the task would measure nothing.
            case "behind": return LivePos - LiveFwd * 2.5f + Vector3.up * 1.2f;
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
                var clip = LoadAgentClip(spec.taskKey, spec.variantKey, "pre");
                agent.SpeakCustom(spec.agentPre, clip);
                float dur = clip ? clip.length : EstimateSpeech(spec.agentPre);
                wait = Mathf.Max(wait, dur + 0.3f);
            }
        }

        yield return new WaitForSeconds(wait);

        Apply(spec);
        NotifyFeedback(spec);
    }

    private static float EstimateSpeech(string text) =>
        string.IsNullOrEmpty(text) ? 0f : Mathf.Max(2f, text.Length * 0.055f);

    /// <summary>
    /// Looks for Resources/AgentVoice/{taskKey}_{variantKey}_{stage}.wav, or
    /// null when absent. Naming: task1_v1_pre, task1_v1_post, task1_v2_pre …
    ///
    /// Falls back to the variant-less name so a single clip per task still
    /// works for any task whose wording does not vary — and so an older clip
    /// set keeps playing rather than silently going quiet.
    ///
    /// Dropping new WAV files into Assets/Resources/AgentVoice/ and rebuilding
    /// is all that is needed to add or replace voice — no code or Inspector
    /// changes. The agent falls back to subtitles when the clip is missing, so
    /// adding voice is incremental and never breaks condition C.
    /// </summary>
    private static AudioClip LoadAgentClip(string taskKey, string variantKey, string stage)
    {
        if (string.IsNullOrEmpty(taskKey)) return null;
        if (!string.IsNullOrEmpty(variantKey))
        {
            var byVariant = Resources.Load<AudioClip>($"AgentVoice/{taskKey}_{variantKey}_{stage}");
            if (byVariant) return byVariant;
        }
        return Resources.Load<AudioClip>($"AgentVoice/{taskKey}_{stage}");
    }

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
            SafeTag(go, "Interactable");

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
            MostRecentSpawn = go;
            StartCoroutine(ReportSceneEvent("object-spawned", go.name, spec.shape, p));
        }
    }

    /// <summary>
    /// The last object a trial created, for the gaze measure in StudyTelemetry.
    ///
    /// On task 4 this is the object that spawns behind the participant, so
    /// "looking at it" is the moment they found it — a behavioural measure of
    /// comprehension that needs no probe and no reconstruction from yaw. On a
    /// cluster spawn it is the last of the ring, which is close enough: they are
    /// within a metre of each other and the measure is a twenty-degree cone.
    /// </summary>
    public static GameObject MostRecentSpawn { get; private set; }

    // ── Named scene objects ───────────────────────────────────────────────────
    // The study scene is "a sphere, a cube and a campfire". The sphere and cube
    // are rebuilt at every trial start so each trial begins from an identical
    // arrangement; the campfire belongs to the DreamCodeVR scene and is found by
    // name. Errors address these by name, which is what lets the server express
    // "move the cube instead of the sphere" without knowing the scene layout.

    private const string SphereName   = "StudySphere";
    private const string CubeName     = "StudyCube";
    private const string CampfireName = "StudyCampfire";

    /// Creates the sphere, cube and campfire if they are missing. Idempotent.
    private void EnsureSceneObjects()
    {
        // Anchor before anything is placed. Without this the sphere, cube and
        // campfire are rebuilt at the participant's current position every time
        // the scene resets, so they arrive somewhere new each trial.
        if (!studyAnchor) AnchorSceneToParticipant();

        EnsureGround();

        Vector3 basePos = OriginPos + OriginFwd * 1.6f;
        Vector3 right = Vector3.Cross(Vector3.up, OriginFwd).normalized;

        // Created if missing, MOVED if they already exist.
        //
        // These persist between trials, so a create-only check meant the objects
        // stayed wherever they were first built and re-anchoring did nothing.
        // A participant who had drifted got a briefing describing a scene behind
        // them. Placing them every time is idempotent and costs nothing.
        PlaceSceneObject(SphereName, PrimitiveType.Sphere,
                         basePos - right * 0.5f + Vector3.up * 0.1f);
        PlaceSceneObject(CubeName, PrimitiveType.Cube,
                         basePos + right * 0.5f + Vector3.up * 0.1f);
        EnsureCampfire();
    }

    /// Creates the named scene object if it is missing, or moves it back into
    /// place if it is not. Physics is cleared on the way, since a participant
    /// may have knocked it and a rolling sphere should not carry momentum from
    /// the previous trial into the next briefing.
    private void PlaceSceneObject(string name, PrimitiveType shape, Vector3 pos)
    {
        var existing = FindNamed(name);
        if (existing)
        {
            var rb = existing.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            existing.transform.position = pos;
            existing.transform.rotation = Quaternion.identity;
            return;
        }

        var go = Primitive(shape, pos, Vector3.one * 0.22f, null);
        go.name = name;
        SafeTag(go, "Interactable");
    }

    private const string GroundName = "StudyGround";

    /// <summary>
    /// A floor large enough that the participant cannot walk off it.
    ///
    /// The authored scene's green ground is a fixed, fairly small plane centred
    /// on the scene's own origin. Because the study anchors itself to wherever
    /// the participant is standing, they were routinely placed near its edge or
    /// past it — walking "outside the ground" into a void, which breaks presence
    /// far more than a plain floor ever would.
    ///
    /// 40 m square, centred on the anchor and sunk fractionally below y=0 so it
    /// never z-fights with the authored ground where the two overlap. Only built
    /// once, and only if the scene has no large ground of its own.
    /// </summary>
    private void EnsureGround()
    {
        if (FindNamed(GroundName)) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = GroundName;
        go.transform.SetParent(CreatedRoot, false);
        // Unity's plane primitive is 10 units across at scale 1, so 4 gives 40 m.
        go.transform.position = new Vector3(OriginPos.x, -0.02f, OriginPos.z);
        go.transform.localScale = new Vector3(4f, 1f, 4f);

        var r = go.GetComponent<Renderer>();
        if (r)
        {
            // Muted green, matching the authored ground closely enough that the
            // join is not a visible seam, and dark enough that a grey object on
            // it still reads as grey — task 5 depends on that contrast.
            r.material.color = new Color(0.22f, 0.34f, 0.20f);
        }
    }

    /// <summary>
    /// The briefing says "above the campfire", so a campfire has to be visible.
    /// The build scene contains none, and the previous fallback was an empty
    /// GameObject — a position with nothing to look at. Every task that refers
    /// to the campfire then pointed at thin air, and an object placed correctly
    /// "above" it looked like it had been dropped in an empty field.
    ///
    /// Built rather than authored so the scene needs no new assets: a dark cone
    /// of logs, an emissive flame and a point light read as a campfire from any
    /// angle. Only created when the scene genuinely has none, so a real
    /// authored campfire always wins.
    /// </summary>
    private void EnsureCampfire()
    {
        // Cheap check first: scanning every Transform in the scene is not
        // something to do on each outcome just to learn what we already built.
        if (FindNamed(CampfireName)) return;
        if (FindAuthoredCampfire()) return;

        Vector3 at = OriginPos + OriginFwd * 3.2f;
        at.y = 0.05f;

        var root = new GameObject(CampfireName);
        root.transform.SetParent(CreatedRoot, false);
        root.transform.position = at;

        // A ring of stones. Squashed spheres at slightly different sizes and
        // heights — a ring of identical ones reads as a manufactured object,
        // and the irregularity is most of what makes it look like stone.
        for (int i = 0; i < 9; i++)
        {
            float t = (i / 9f) * Mathf.PI * 2f;
            float wobble = 0.85f + 0.3f * Mathf.Abs(Mathf.Sin(i * 2.4f));
            var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stone.name = "Stone" + i;
            stone.transform.SetParent(root.transform, false);
            stone.transform.localPosition =
                new Vector3(Mathf.Sin(t) * 0.52f, 0.045f, Mathf.Cos(t) * 0.52f);
            stone.transform.localScale =
                new Vector3(0.19f, 0.13f, 0.17f) * wobble;
            stone.transform.localRotation = Quaternion.Euler(0f, i * 37f, i * 11f);
            var sr = stone.GetComponent<Renderer>();
            if (sr) sr.material.color =
                Color.Lerp(new Color(0.34f, 0.33f, 0.31f),
                           new Color(0.19f, 0.18f, 0.17f), (i % 3) / 2f);
        }

        // Ash bed, so the logs sit in something rather than on the floor.
        var ash = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ash.name = "AshBed";
        ash.transform.SetParent(root.transform, false);
        ash.transform.localScale = new Vector3(0.48f, 0.02f, 0.48f);
        var ashCol = ash.GetComponent<Collider>();
        if (ashCol) ashCol.enabled = false;
        var ashR = ash.GetComponent<Renderer>();
        if (ashR) ashR.material.color = new Color(0.13f, 0.11f, 0.10f);

        // Logs leaning inward as a teepee. This is the shape people actually
        // recognise as a campfire; a flat disc with a capsule above it, which is
        // what used to be here, reads as a lamp.
        for (int i = 0; i < 6; i++)
        {
            float t = (i / 6f) * Mathf.PI * 2f;
            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log" + i;
            log.transform.SetParent(root.transform, false);
            log.transform.localPosition =
                new Vector3(Mathf.Sin(t) * 0.19f, 0.20f, Mathf.Cos(t) * 0.19f);
            log.transform.localScale = new Vector3(0.055f, 0.27f, 0.055f);
            // Tilt each log outward at its base so the tops converge.
            log.transform.localRotation =
                Quaternion.Euler(Mathf.Cos(t) * 26f, 0f, -Mathf.Sin(t) * 26f);
            var lr = log.GetComponent<Renderer>();
            if (lr) lr.material.color =
                Color.Lerp(new Color(0.26f, 0.17f, 0.10f),
                           new Color(0.15f, 0.09f, 0.05f), (i % 2));
        }

        // Embers under the flame: dull red, always lit, so the fire still reads
        // as a fire in the frames where the flame is at its smallest.
        var embers = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        embers.name = "Embers";
        embers.transform.SetParent(root.transform, false);
        embers.transform.localPosition = new Vector3(0f, 0.07f, 0f);
        embers.transform.localScale = new Vector3(0.34f, 0.10f, 0.34f);
        StripCollider(embers);
        Emissive(embers, new Color(1f, 0.32f, 0.06f), new Color(1f, 0.22f, 0.03f), 2.4f);

        // Three nested flame cones, smallest and brightest innermost. One solid
        // capsule cannot look like flame at any colour; the layering is what
        // gives it a hot core and a soft edge.
        var flame = new GameObject("Flame");
        flame.transform.SetParent(root.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0.30f, 0f);

        AddFlameLayer(flame.transform, 0.30f, 0.46f, 0.00f,
                      new Color(1f, 0.34f, 0.05f), 1.9f);   // outer, deep orange
        AddFlameLayer(flame.transform, 0.20f, 0.34f, 0.03f,
                      new Color(1f, 0.62f, 0.10f), 2.8f);   // mid, orange-yellow
        AddFlameLayer(flame.transform, 0.11f, 0.22f, 0.06f,
                      new Color(1f, 0.88f, 0.45f), 3.6f);   // core, near-white

        var light = new GameObject("Firelight");
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        var point = light.AddComponent<Light>();
        point.type = LightType.Point;
        point.color = new Color(1f, 0.6f, 0.25f);
        point.range = 6f;
        point.intensity = 2.2f;

        // Movement is what separates fire from an orange ornament, and it costs
        // one component driving a scale and an intensity. It also gives the
        // scene its only motion between trials, which quietly signals the app is
        // alive rather than frozen — worth having in a study where a frozen
        // headset and a waiting one otherwise look the same.
        var flicker = root.AddComponent<CampfireFlicker>();
        flicker.flame = flame.transform;
        flicker.firelight = point;
        flicker.baseIntensity = point.intensity;
    }

    /// One cone of the flame. Cones, not capsules: a flame tapers upward.
    private static void AddFlameLayer(Transform parent, float width, float height,
                                      float lift, Color colour, float glow)
    {
        // Unity has no cone primitive, so a cylinder scaled to a point would be
        // needed — a capsule scaled tall and narrow reads closer to a flame
        // tongue and needs no mesh building.
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "FlameLayer";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, lift, 0f);
        go.transform.localScale = new Vector3(width, height, width);
        StripCollider(go);
        Emissive(go, colour, colour, glow);
    }

    /// Flame and embers must never be selectable or collide with anything the
    /// participant creates — an object spawned "above the campfire" that bounces
    /// off the fire is a failure the study did not script.
    private static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c) c.enabled = false;
    }

    private static void Emissive(GameObject go, Color albedo, Color emission, float strength)
    {
        var r = go.GetComponent<Renderer>();
        if (!r) return;
        var m = r.material;
        m.color = albedo;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission * strength);
    }

    /// A campfire that belongs to the authored scene, if there is one.
    private Transform FindAuthoredCampfire()
    {
        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (!t || IsStudyCreated(t)) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("campfire") || n.Contains("bonfire") || n.Contains("fireplace"))
                return t;
        }
        return null;
    }

    private static bool IsStudyCreated(Transform t)
    {
        return createdRoot && t.IsChildOf(createdRoot);
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
        var authored = FindAuthoredCampfire();
        if (authored) return authored;

        // Otherwise the study builds one, so the word always has something
        // visible behind it rather than an invisible anchor point.
        EnsureCampfire();
        var built = FindNamed(CampfireName);
        return built ? built.transform : null;
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
        StartCoroutine(ReportSceneEvent("object-moved", mover.name, spec.target, to));
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
        SafeTag(go, "Interactable");

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
        // The two questions, before the condition check.
        //
        // They are asked in every condition, so they are shown in every
        // condition — including A, which returns immediately below. This is
        // deliberately not part of the manipulation: identical panel, identical
        // wording, identical position in all three cells. What differs between
        // conditions is the EXPLANATION, and there is none here.
        var questions = FindObjectOfType<QuestionPanelController>(true);
        if (questions)
        {
            if (string.IsNullOrWhiteSpace(spec.errorText)) questions.Hide();
            else questions.Show();
        }

        var cond = FindObjectOfType<StudyConditionManager>(true);
        if (cond && cond.IsConditionA()) return;   // A shows no explanation, by design

        // A correct outcome is silent in every condition: "no agent intervention
        // in C, no panel in B, nothing in A". Only errors are explained, so the
        // feedback itself never signals that the attempt succeeded.
        //
        // Silent means SILENT, not "leave the last failure on screen". Returning
        // here without clearing left the previous error's explanation up through
        // the success, next to a "Processing..." line from the attempt that
        // fixed it — so the panel was still explaining a failure at the exact
        // moment the participant got what they asked for. Clearing keeps the
        // outcome unannounced, which is the design, while removing an
        // explanation that has stopped being true.
        if (string.IsNullOrWhiteSpace(spec.errorText))
        {
            var stale = FindObjectOfType<FeedbackPanelController>(true);
            if (stale) stale.Clear();
            return;
        }

        if (cond && cond.IsConditionC())
        {
            if (!string.IsNullOrWhiteSpace(spec.agentPost))
            {
                var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
                if (agent)
                {
                    var clip = LoadAgentClip(spec.taskKey, spec.variantKey, "post");
                    agent.SpeakCustom(spec.agentPost, clip);
                    // Onset is now; offset is when the line finishes. Reported
                    // as a duration rather than a second POST because the agent
                    // has no completion callback, and an estimate that is
                    // explicitly an estimate is better than a timestamp that
                    // silently is one.
                    float dur = clip ? clip.length : EstimateSpeech(spec.agentPost);
                    ReportHeadsetEvent(this, "feedback-onset", "agent",
                                       Mathf.RoundToInt(dur * 1000f).ToString());
                }
            }
        }
        else
        {
            var panel = FindObjectOfType<FeedbackPanelController>(true);
            if (panel)
            {
                // B and C must carry the SAME sentence, differing only in how it
                // is delivered. This used to show errorText — a third-person
                // system report ("The sign was created, but no colour was
                // given...") — while C spoke agentPost, a first-person agent line
                // ("I made the sign, but you didn't say what colour..."). The two
                // cells therefore differed in person, phrasing and delivery at
                // once, and no B-versus-C difference could be attributed to any
                // of them. Showing the agent's own words leaves delivery as the
                // only difference, which is what the comparison is for.
                //
                // errorText still decides WHETHER this is a failure (above), so a
                // spec that omits agentPost degrades to the old text rather than
                // showing a participant nothing at all.
                var text = string.IsNullOrWhiteSpace(spec.agentPost)
                    ? spec.errorText
                    : spec.agentPost;
                panel.ShowError(spec.label, text);
                ReportHeadsetEvent(this, "feedback-onset", "panel",
                    Mathf.RoundToInt(Mathf.Max(0f, panel.autoHideAfterSeconds) * 1000f).ToString());
            }
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

        // Destroy() only takes effect at the end of the frame, but the rebuild
        // below runs immediately and looks the objects up by name. Detaching
        // first is what makes the container actually empty *now*; without it
        // EnsureSceneObjects finds the doomed sphere and cube, concludes they
        // already exist, and creates nothing — leaving an empty scene after
        // every reset, which is to say at the start of every trial.
        for (int i = CreatedRoot.childCount - 1; i >= 0; i--)
        {
            var child = CreatedRoot.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }

        // Only what this study spawned. The sweep used to take everything
        // carrying these tags, which in this scene means SphereTraining, Cube
        // and CubeTraining — authored objects that nothing ever puts back. One
        // reset permanently stripped the scene the briefing describes.
        foreach (var go in TaggedObjects("Interactable")) if (!IsAuthored(go)) Destroy(go);
        foreach (var go in TaggedObjects("game"))         if (!IsAuthored(go)) Destroy(go);

        var panel = FindObjectOfType<FeedbackPanelController>(true);
        if (panel) panel.Clear();

        // Agent must return to idle, not be left mid-sentence from the last trial.
        var agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
        if (agent) agent.StopSpeaking();
        var body = FindObjectOfType<EmbodiedAgentBody>(true);
        if (body) body.OnFinishedSpeaking();

        var transcript = FindObjectOfType<TranscriptDisplay>(true);
        if (transcript) transcript.ClearTranscript();

        // The questions belong to the failure that prompted them, so they must
        // not still be up when the next trial's briefing is read.
        var questions = FindObjectOfType<QuestionPanelController>(true);
        if (questions) questions.Hide();

        // Re-anchor between trials, not during one.
        //
        // Scene setup is the wizard pressing "Set up the scene" before reading
        // the briefing, so this is the moment to put the arrangement back in
        // front of wherever the participant has ended up. Within a trial the
        // anchor is fixed, so nothing shifts under them while they are working.
        AnchorSceneToParticipant();

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
            if (HasTag(r.gameObject, "Interactable")) return r;
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

/// <summary>
/// Makes the built campfire move. Added at runtime by StudyOutcomes, never in
/// the Inspector, so it does not need its own file to be assignable.
///
/// Two incommensurable sine waves rather than one, and rather than random: a
/// single sine is visibly periodic within a couple of seconds and starts to look
/// like a pulsing light, while per-frame randomness strobes. Two waves whose
/// periods do not divide each other never visibly repeat.
/// </summary>
public class CampfireFlicker : MonoBehaviour
{
    public Transform flame;
    public Light firelight;
    public float baseIntensity = 2.2f;

    private float seed;

    private void Start()
    {
        // Offset per instance so two fires in one scene never pulse together.
        seed = UnityEngine.Random.value * 10f;
    }

    private void Update()
    {
        float t = Time.time + seed;
        float wave = Mathf.Sin(t * 6.1f) * 0.5f + Mathf.Sin(t * 9.7f) * 0.5f;

        if (flame)
        {
            // Taller and thinner together, so it licks upward rather than
            // inflating like a balloon.
            float stretch = 1f + wave * 0.11f;
            flame.localScale = new Vector3(1f - wave * 0.05f, stretch, 1f - wave * 0.05f);
            flame.localRotation = Quaternion.Euler(wave * 3.5f, t * 22f, wave * 2.5f);
        }

        if (firelight)
        {
            firelight.intensity = baseIntensity * (1f + wave * 0.16f);
        }
    }
}
