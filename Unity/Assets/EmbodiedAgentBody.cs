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

    // Where the body actually ended up, which is not necessarily worldPosition:
    // a full-size avatar is placed by its head height, not by its root.
    private Vector3 placedPosition;
    // The head's own authored transform, so the speaking pulse and nod are
    // applied RELATIVE to it rather than overwriting it with blob-sized values.
    private Vector3 headBaseScale = Vector3.one;
    private Quaternion headBaseRotation = Quaternion.identity;
    // Only set on the avatar path; null for the primitive blob, which shows
    // speech through its eyes instead.
    private Transform speechIndicator;
    private Vector3 speechIndicatorBaseScale = Vector3.one;

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

        // Preferred: the agent wears the same avatar the participants wear.
        //
        // A purple blob with two eyes is a different KIND of thing from the
        // avatar a participant sees on themselves, and condition C is supposed
        // to vary how the explanation is delivered — not introduce a novel
        // creature whose unfamiliarity is doing work the design cannot separate
        // from embodiment. Reusing the room's own avatar removes that.
        if (BuildFromParticipantAvatar()) return;

        // Fallback: the primitive body, unchanged. Reached when no AvatarManager
        // has been created yet (the agent can be built before the room is
        // joined) or when the prefab cannot be instantiated. An agent that looks
        // wrong is recoverable; an agent that fails to appear is a lost trial.

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

        // The blob is placed exactly where it was told to be, and its head keeps
        // the size it was built at. Recorded in the same fields the avatar path
        // uses so Update needs no branch.
        placedPosition = worldPosition;
        headBaseScale = head.localScale;
        headBaseRotation = head.localRotation;
    }

    /// <summary>
    /// Instantiates the same avatar prefab the AvatarManager gives participants,
    /// as scenery. Returns false if there is nothing to instantiate, in which
    /// case the caller builds the primitive body instead.
    ///
    /// Every MonoBehaviour is removed from the copy. That is the whole trick and
    /// it is not optional: the prefab is a networked object, and left intact it
    /// would register with the NetworkScene, claim an id, and be transmitted to
    /// every peer as though a person had joined — inside a study whose entire
    /// premise is that one participant is alone with a system. Stripped to
    /// meshes and transforms it is a mannequin, which is all the agent needs,
    /// because EmbodiedAgentBody already drives the bob, the turn and the
    /// speaking tell itself.
    /// </summary>
    private bool BuildFromParticipantAvatar()
    {
        GameObject prefab = null;
        var manager = FindObjectOfType<Ubiq.Avatars.AvatarManager>(true);
        if (manager) prefab = manager.avatarPrefab;
        if (!prefab) return false;

        // Instantiated under a parent that is switched OFF, and switched on only
        // after the behaviours are gone.
        //
        // Stripping after a plain Instantiate is too late: Awake runs during
        // Instantiate, so the networked components would already have run,
        // looked for a NetworkScene, and either registered or thrown — before
        // the first line of stripping code executes. An exception there happens
        // while the study UI is still being built, which is not a visible error
        // in a headset, it is an app that never finishes loading.
        //
        // A child of an inactive parent is inactive in the hierarchy, and Unity
        // defers Awake until it becomes active. So nothing on the copy runs at
        // all until it has nothing left to run.
        var holder = new GameObject("AgentAvatarHolder");
        holder.transform.SetParent(root, false);
        holder.SetActive(false);

        GameObject copy;
        try
        {
            copy = Instantiate(prefab, holder.transform);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[EmbodiedAgentBody] could not instantiate the participant " +
                             $"avatar, falling back to the primitive body: {e.Message}");
            Destroy(holder);
            return false;
        }

        copy.name = "AgentAvatar";

        // DestroyImmediate rather than Destroy: Destroy is deferred to the end
        // of the frame, which would be after the holder is switched on below,
        // so the behaviours would get exactly the Awake this is meant to avoid.
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb) DestroyImmediate(mb);
        }
        foreach (var col in copy.GetComponentsInChildren<Collider>(true))
        {
            if (col) DestroyImmediate(col);
        }
        // Audio would be a second voice in a study measuring one.
        foreach (var src in copy.GetComponentsInChildren<AudioSource>(true))
        {
            if (src) DestroyImmediate(src);
        }

        // Full participant scale, not a shrunken desk companion.
        //
        // The avatar prefab is authored at human size, so it is used at human
        // size: an agent scaled to a fifth of a person reads as a toy floating
        // beside a panel, and condition C is meant to be an embodied
        // interlocutor. Matching the participants' own avatar is the point of
        // using their avatar at all.
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localScale = Vector3.one;

        // Safe to switch on now: there is nothing left that could Awake into a
        // network registration. This has to happen before the bounds below are
        // read, because Renderer.bounds on an inactive object is meaningless.
        holder.SetActive(true);

        // The bob and the facing turn need a head. Ubiq's floating avatar names
        // it "Head"; anything else falls back to the tallest renderer, which is
        // the head on every humanoid rig worth the name.
        head = FindDeepChild(copy.transform, "Head") ?? TallestRenderer(copy.transform) ?? copy.transform;

        // Anchor by the head, then remember the result. Update bobs around
        // placedPosition, so if this is not recorded the placement is undone on
        // the very next frame.
        PlaceAtStandingHeight(copy.transform, head);
        placedPosition = root.position;

        // Keep the head's authored transform so the speaking nod and pulse are
        // applied on top of it rather than replacing it.
        headBaseScale = head.localScale;
        headBaseRotation = head.localRotation;

        // A single speech indicator, not a pair of eyes.
        //
        // The blob's tell was two eye spheres stuck on the front of its head.
        // On a humanoid avatar that does not work: the offsets are in head-local
        // units, so on a real head they land inside the mesh or behind it, and
        // the avatar has its own face already. The result is an agent that
        // speaks with no visible sign of speaking, which in condition C is the
        // manipulation failing quietly.
        //
        // One emissive sphere floating just above the head reads as "this one is
        // talking" from any angle, does not fight the avatar's own face, and
        // needs no rigging. It is driven through the same eye-renderer fields,
        // so SetEyeColor and the speaking pulse work unchanged.
        var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicator.name = "SpeechIndicator";
        StripCollider(indicator);
        indicator.transform.SetParent(head, false);
        // Above the head in WORLD-ish terms: head scale varies per avatar, so
        // the offset is expressed against the head's own size rather than as a
        // fixed number that would sit in the wrong place on a different rig.
        indicator.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        indicator.transform.localScale = Vector3.one * 0.42f;
        leftEye = rightEye = indicator.transform;
        leftEyeR = rightEyeR = indicator.GetComponent<Renderer>();
        speechIndicator = indicator.transform;
        speechIndicatorBaseScale = indicator.transform.localScale;
        SetEyeColor(eyeColorIdle);

        Debug.Log("[EmbodiedAgentBody] agent is using the participant avatar prefab " +
                  $"'{prefab.name}' (stripped of behaviour).");
        return true;
    }

    /// <summary>
    /// Places the avatar by its HEAD, at standing eye height.
    ///
    /// The obvious approach — take the rendered bounds and drop the body until
    /// its lowest point touches the floor — is wrong here, and wrong in a way
    /// that produced exactly the reported symptom: a head sitting at ground
    /// level with the rest apparently underground.
    ///
    /// Ubiq's floating avatar has no legs. It is a head, a torso and two hands,
    /// authored at the heights a standing person's would be, with nothing below
    /// about waist level. Its lowest rendered part is therefore roughly a metre
    /// up, and forcing that metre-high part down to y=0 sinks the whole figure
    /// by a metre, leaving only the top of the head above the floor.
    ///
    /// So the head is the anchor instead: put it where a standing person's head
    /// would be and everything else follows, whether or not the avatar has legs.
    /// </summary>
    private void PlaceAtStandingHeight(Transform avatar, Transform headTransform)
    {
        if (!headTransform)
        {
            // No head to anchor to — fall back to the bounds, which is at least
            // better than leaving it wherever the prefab's origin fell.
            var rs = avatar.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            root.position -= new Vector3(0f, b.min.y - FloorY, 0f);
            return;
        }

        float lift = EyeHeight - (headTransform.position.y - FloorY);
        root.position += new Vector3(0f, lift, 0f);
    }

    /// The scene's ground plane. The sphere, cube and campfire are all built
    /// against y = 0 in StudyOutcomes, so the agent shares their floor.
    private const float FloorY = 0f;

    /// Standing eye height. Matches the default camera height a seated-or-standing
    /// Quest user sits at closely enough that the agent reads as a person facing
    /// them rather than as a child or a giant.
    private const float EyeHeight = 1.6f;

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }

    private static Transform TallestRenderer(Transform parent)
    {
        Transform best = null;
        float bestY = float.NegativeInfinity;
        foreach (var r in parent.GetComponentsInChildren<Renderer>(true))
        {
            if (r.bounds.center.y > bestY) { bestY = r.bounds.center.y; best = r.transform; }
        }
        return best;
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
        // On the avatar path both fields point at the same indicator, so this
        // assigns twice to one renderer — harmless, and cheaper than branching.
        if (leftEyeR) Tint(leftEyeR, c);
        if (rightEyeR && rightEyeR != leftEyeR) Tint(rightEyeR, c);
    }

    /// Emissive as well as coloured. An unlit sphere changing hue is easy to
    /// miss in a dim scene lit mainly by a campfire, which is the only light
    /// this scene reliably has; a glowing one is not.
    private static void Tint(Renderer r, Color c)
    {
        var m = r.material;
        m.color = c;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 1.6f);
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    private void Update()
    {
        if (root == null) return;

        // Gentle idle bob, around wherever the body was actually placed.
        //
        // This used to bob around `worldPosition`, which meant it reassigned the
        // position from scratch every frame and silently threw away any vertical
        // placement done at build time. The avatar was being put at a sensible
        // height and then yanked back to the raw configured value one frame
        // later — which is why it ended up buried with only its head showing.
        bobPhase += Time.deltaTime;
        float bob = Mathf.Sin(bobPhase * 1.6f) * 0.02f;
        root.position = placedPosition + Vector3.up * bob;

        // Smooth the speaking level.
        speakLevel = Mathf.MoveTowards(speakLevel, speaking ? 1f : 0f, Time.deltaTime * 4f);

        if (head)
        {
            // Nod plus a small yaw, so it reads as talking rather than as a
            // metronome. A pure pitch nod at a single frequency looks
            // mechanical; two axes at different rates look like someone
            // speaking, and cost the same.
            float nod  = speaking ? Mathf.Sin(Time.time * 10f) * 9f * speakLevel : 0f;
            float sway = speaking ? Mathf.Sin(Time.time * 6.3f) * 4f * speakLevel : 0f;
            head.localRotation = headBaseRotation * Quaternion.Euler(nod, sway, 0f);

            float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.05f * speakLevel;
            // Scaled from whatever the head's own scale is, not from `size`.
            // `size` describes the primitive blob; forcing it onto a real
            // avatar's head shrank it to a fraction of itself every frame.
            head.localScale = headBaseScale * pulse;
        }

        // The indicator does the visible work on the avatar, where the head
        // pulse is subtle and the avatar has its own face. Swells while
        // speaking, so "is it talking?" is answerable at a glance and from
        // behind, which matters because participants turn away mid-trial.
        if (speechIndicator)
        {
            float swell = 1f + Mathf.Sin(Time.time * 11f) * 0.22f * speakLevel + 0.25f * speakLevel;
            speechIndicator.localScale = speechIndicatorBaseScale * swell;
        }

        SetEyeColor(Color.Lerp(eyeColorIdle, eyeColorSpeaking, speakLevel));

        if (faceCamera && Camera.main)
        {
            var to = Camera.main.transform.position - root.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.0001f)
            {
                // TOWARDS the participant, not away.
                //
                // This was LookRotation(-to), which points the body's forward
                // axis directly away from whoever it is addressing. On the old
                // blob that was survivable — it was a sphere with two dots, so
                // "backwards" mostly read as "featureless". On a humanoid avatar
                // it is unmistakable: the agent stands with its back to the
                // participant and talks over its shoulder, which is what the
                // "talking with his behind" report is.
                root.rotation = Quaternion.Slerp(root.rotation,
                    Quaternion.LookRotation(to), Time.deltaTime * 5f);
            }
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
