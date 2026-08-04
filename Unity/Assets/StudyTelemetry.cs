using System.Globalization;
using UnityEngine;
using Ubiq.Messaging;

/// <summary>
/// Streams head pose to the study server so the event log can say where the
/// participant was looking, not only what they said.
///
/// This exists mainly for task 4, where the object spawns behind them. Without
/// pose there is no record of whether they ever turned around, so "did not
/// notice it" and "turned, saw it, and said nothing" are the same row. With yaw
/// the turn is a measurable event with a latency attached to it.
///
/// Sent at a fixed low rate rather than every frame. At 72fps a session would
/// produce roughly a quarter of a million rows per participant, which buys
/// nothing: head movement relevant to this study happens over hundreds of
/// milliseconds, not milliseconds. 10Hz keeps the file openable in a spreadsheet
/// and still resolves a turn precisely.
///
/// Movement-gated as well as rate-limited. A participant standing still
/// generates no rows at all, so the log records changes rather than a heartbeat,
/// which is what makes it readable by eye.
///
/// Attach to the StudyManager object. StudyUIBootstrapper adds it automatically
/// if it is missing.
/// </summary>
[DefaultExecutionOrder(-90)]
public class StudyTelemetry : MonoBehaviour
{
    [Tooltip("Samples per second. 10 is ample for head-turn latency; higher just makes the file bigger.")]
    public float sampleRate = 10f;

    [Tooltip("Minimum metres of movement before a sample is sent.")]
    public float positionThreshold = 0.02f;

    [Tooltip("Minimum degrees of rotation before a sample is sent.")]
    public float rotationThreshold = 2.0f;

    // Must match TELEMETRY_NETWORK_ID in app.js.
    private const int TELEMETRY_NETWORK_ID = 97;

    private NetworkContext context;
    private Transform head;
    private float nextSampleAt;
    private Vector3 lastPos;
    private Quaternion lastRot;
    private bool hasSent;

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

        if (hasSent &&
            Vector3.Distance(pos, lastPos) < positionThreshold &&
            Quaternion.Angle(rot, lastRot) < rotationThreshold)
        {
            return;     // nothing worth a row
        }

        lastPos = pos;
        lastRot = rot;
        hasSent = true;

        // Invariant culture: a machine with a comma decimal separator would
        // otherwise write "1,23" into a comma-separated file and silently shift
        // every column after it.
        var c = CultureInfo.InvariantCulture;
        context.SendJson(new PoseMessage
        {
            type = "HeadPose",
            x    = pos.x.ToString("F3", c),
            y    = pos.y.ToString("F3", c),
            z    = pos.z.ToString("F3", c),
            yaw  = rot.eulerAngles.y.ToString("F1", c)
        });
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
    }
}
