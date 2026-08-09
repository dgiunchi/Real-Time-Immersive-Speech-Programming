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

        StandOnFloor(copy.transform);

        // The bob and the facing turn need a head. Ubiq's floating avatar names
        // it "Head"; anything else falls back to the tallest renderer, which is
        // the head on every humanoid rig worth the name.
        head = FindDeepChild(copy.transform, "Head") ?? TallestRenderer(copy.transform) ?? copy.transform;

        // No eye spheres on a real avatar, so the speaking tell has to live
        // somewhere else. MakeEye still runs, parented to the head, but small
        // and tucked in front — it reads as an indicator light rather than a
        // face, and it keeps SetEyeColor working unchanged.
        leftEye  = MakeEye(new Vector3(-0.10f, 0.10f, 0.30f), out leftEyeR);
        rightEye = MakeEye(new Vector3( 0.10f, 0.10f, 0.30f), out rightEyeR);
        SetEyeColor(eyeColorIdle);

        Debug.Log("[EmbodiedAgentBody] agent is using the participant avatar prefab " +
                  $"'{prefab.name}' (stripped of behaviour).");
        return true;
    }

    /// <summary>
    /// Drops the avatar so its lowest visible point rests on the floor.
    ///
    /// The agent used to be placed at a hard-coded y of about 1.5m, which put a
    /// small body at roughly eye height — floating in mid-air with nothing under
    /// it. That is fine for an abstract helper blob and wrong for something
    /// wearing a participant's avatar, which reads as a person and therefore
    /// reads as a person hovering.
    ///
    /// Measured rather than assumed, because the prefab's own origin is not
    /// dependable: Ubiq's floating avatar is normally driven by tracked head and
    /// hand positions, so with no tracking data its parts sit wherever the
    /// prefab left them. Taking the rendered bounds and shifting until the
    /// bottom is at floor level works whatever the authored origin, and works
    /// for a different avatar prefab later.
    /// </summary>
    private void StandOnFloor(Transform avatar)
    {
        var renderers = avatar.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        // FloorY is the ground the study objects sit on, not the agent's own
        // origin, so the agent shares a floor with the scene rather than with
        // whatever height the panel happens to be at.
        float drop = bounds.min.y - FloorY;
        root.position -= new Vector3(0f, drop, 0f);
    }

    /// The scene's ground plane. The sphere, cube and campfire are all built
    /// against y = 0 in StudyOutcomes, so the agent uses the same reference.
    private const float FloorY = 0f;

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
