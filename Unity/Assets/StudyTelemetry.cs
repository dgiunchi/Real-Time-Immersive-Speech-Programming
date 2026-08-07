using System.Globalization;
using UnityEngine;
using Ubiq.Messaging;

/// <summary>
/// Streams head pose and gaze to the study server so the event log can say where
/// the participant was looking, not only what they said.
///
/// This exists mainly for two measures.
///
/// Task 4 spawns its object behind the participant. Without pose there is no
/// record of whether they ever turned around, so "did not notice it" and
/// "turned, saw it, and said nothing" are the same row. With yaw the turn is a
/// measurable event with a latency attached to it, and no probe is needed to
/// get it.
///
/// In conditions B and C it turns the manipulation check from a self-report item
/// into dwell time. "Did you notice the feedback" answered yes is weak evidence;
/// four seconds of gaze on the panel before speaking again is strong evidence,
/// and it is available on every trial without asking anyone anything.
///
/// SAMPLING IS CONTINUOUS, and that is the point.
///
/// This used to be movement-gated — a sample was only sent once the head had
/// moved 2cm or turned 2 degrees — on the reasoning that a still head is not
/// worth a row. That reasoning is exactly backwards for dwell. A participant
/// reading the feedback panel holds their head as still as they ever will, so
/// the gate suppressed every sample of the behaviour the measure exists to
/// capture, and produced a log in which attentive reading and taking the headset
/// off look identical: nothing.
///
/// The cost is roughly 10 rows per second for the length of a session. Gating is
/// still available via <see cref="continuous"/> if a file ever needs to be cut
/// down, but it should not be the default, and turning it on silently disables
/// dwell.
///
/// Attach to the StudyManager object. StudyUIBootstrapper adds it automatically
/// if it is missing.
/// </summary>
[DefaultExecutionOrder(-90)]
public class StudyTelemetry : MonoBehaviour
{
    [Tooltip("Samples per second. 10 resolves a head turn to 100ms, which is well " +
             "under the scale anything here is measured on. Higher mostly buys file size.")]
    public float sampleRate = 10f;

    [Tooltip("Sample on a fixed clock regardless of movement. Leave ON: gating on " +
             "movement removes exactly the still-headed samples that dwell time is made of.")]
    public bool continuous = true;

    [Tooltip("Only used when 'continuous' is off. Minimum metres of movement before a sample is sent.")]
    public float positionThreshold = 0.02f;

    [Tooltip("Only used when 'continuous' is off. Minimum degrees of rotation before a sample is sent.")]
    public float rotationThreshold = 2.0f;

    [Header("Gaze")]
    [Tooltip("Half-angle in degrees of the cone counted as looking at something.")]
    public float gazeConeDegrees = 20f;

    [Tooltip("Seconds between re-scans for the panel and the agent. They are created " +
             "and destroyed during a session, so a one-time lookup goes stale.")]
    public float targetRefreshSeconds = 2f;

    // Must match TELEMETRY_NETWORK_ID in app.js.
    private const int TELEMETRY_NETWORK_ID = 97;

    private NetworkContext context;
    private Transform head;
    private float nextSampleAt;
    private Vector3 lastPos;
    private Quaternion lastRot;
    private bool hasSent;

    private float nextTargetScanAt;
    private FeedbackPanelController panel;
    private EmbodiedAgentDialogue agent;

    private void Start()
    {
        context = NetworkScene.Register(this, new NetworkId(TELEMETRY_NETWORK_ID));

        var cam = Camera.main;
        if (cam) head = cam.transform;
        if (!head)
        {
            Debug.LogWarning("[StudyTelemetry] No main camera; head pose will not be sent.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (head == null || Time.time < nextSampleAt) return;
        nextSampleAt = Time.time + (1f / Mathf.Max(1f, sampleRate));

        var pos = head.position;
        var rot = head.rotation;

        if (!continuous && hasSent &&
            Vector3.Distance(pos, lastPos) < positionThreshold &&
            Quaternion.Angle(rot, lastRot) < rotationThreshold)
        {
            return;     // gated mode only — see the class comment before enabling
        }

        lastPos = pos;
        lastRot = rot;
        hasSent = true;

        // Invariant culture: a machine with a comma decimal separator would
        // otherwise write "1,23" into a comma-separated file and silently shift
        // every column after it.
        var c = CultureInfo.InvariantCulture;

        // Pitch as a signed angle rather than a raw Euler value. eulerAngles
        // reports looking down as 350-ish rather than -10, and a column that
        // jumps from 5 to 355 across the horizon cannot be averaged, differenced
        // or plotted without someone first noticing the wrap.
        float pitch = Mathf.DeltaAngle(0f, rot.eulerAngles.x);

        context.SendJson(new PoseMessage
        {
            type  = "HeadPose",
            x     = pos.x.ToString("F3", c),
            y     = pos.y.ToString("F3", c),
            z     = pos.z.ToString("F3", c),
            yaw   = rot.eulerAngles.y.ToString("F1", c),
            pitch = pitch.ToString("F1", c),
            gaze  = CurrentGazeTarget(pos, head.forward)
        });
    }

    /// <summary>
    /// What the participant is looking at, or "" for nothing in particular.
    ///
    /// An angular cone rather than a physics raycast, because the feedback panel
    /// is a UI canvas with no collider and the agent's collider (if it has one)
    /// is not where its face is. Angle-to-centre needs neither, cannot be
    /// blocked by an object that happens to drift between them, and is what the
    /// measure actually means: the panel is within the region of the visual
    /// field they could be reading.
    ///
    /// Nearest-angle wins when two candidates are both inside the cone, so a
    /// participant facing the panel with the agent off to one side is recorded
    /// as looking at the panel.
    /// </summary>
    private string CurrentGazeTarget(Vector3 eye, Vector3 forward)
    {
        if (Time.time >= nextTargetScanAt)
        {
            nextTargetScanAt = Time.time + Mathf.Max(0.25f, targetRefreshSeconds);
            panel = FindObjectOfType<FeedbackPanelController>(true);
            agent = FindObjectOfType<EmbodiedAgentDialogue>(true);
        }

        string best = "";
        float bestAngle = gazeConeDegrees;

        // Only counts while it is actually on screen. A panel that has
        // auto-hidden is not something anyone can be looking at, and recording
        // dwell on it would inflate the manipulation check with the seconds
        // after the explanation disappeared.
        if (panel && panel.panelRoot && panel.panelRoot.activeInHierarchy)
        {
            Consider(eye, forward, panel.panelRoot.transform.position, "panel", ref best, ref bestAngle);
        }
        if (agent && agent.gameObject.activeInHierarchy)
        {
            Consider(eye, forward, agent.transform.position, "agent", ref best, ref bestAngle);
        }

        // Whatever the study most recently put in the world. On task 4 this is
        // the object that spawned behind them, so the moment they turn far
        // enough to see it is a row that says so, with no probe and no
        // reconstruction from yaw.
        var spawned = StudyOutcomes.MostRecentSpawn;
        if (spawned)
        {
            Consider(eye, forward, spawned.transform.position, "object", ref best, ref bestAngle);
        }

        return best;
    }

    private static void Consider(Vector3 eye, Vector3 forward, Vector3 target,
                                 string name, ref string best, ref float bestAngle)
    {
        var to = target - eye;
        if (to.sqrMagnitude < 0.0001f) return;
        float angle = Vector3.Angle(forward, to);
        if (angle < bestAngle)
        {
            bestAngle = angle;
            best = name;
        }
    }

    /// <summary>
    /// Required by Ubiq for any registered component, including send-only ones.
    /// Without it NetworkScene.Register throws at Start() and no pose is ever
    /// sent — the component looks attached and does nothing. Telemetry is
    /// one-way, so there is genuinely nothing to handle here.
    /// </summary>
    public void ProcessMessage(ReferenceCountedSceneGraphMessage data) { }

    [System.Serializable]
    private struct PoseMessage
    {
        public string type;
        public string x, y, z, yaw;
        // Added after the first sessions were run. The server reads these by
        // name and leaves the column blank when they are absent, so a headset
        // running an older build stays compatible rather than breaking.
        public string pitch;
        public string gaze;
    }
}
