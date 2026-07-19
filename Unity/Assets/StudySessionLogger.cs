using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Client-side study logger. Sends events that only Unity knows about back to
/// the Wizard-of-Oz server (which appends them to the participant's CSV), and
/// keeps local counters (e.g. number of speech attempts, time per task).
///
/// Attach anywhere in the scene. It auto-hooks the TranscriptionCollector to
/// count speech attempts. Call MarkTaskStart / MarkTaskComplete from your task
/// flow, or the WizardOfOzController, to record task timing.
///
/// Metrics captured here support the study measures discussed with the
/// supervisor: number of times the participant speaks, and time-on-task.
/// </summary>
public class StudySessionLogger : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("Base URL of the Wizard-of-Oz control server.")]
    public string serverUrl = "http://localhost:8181";

    [Header("State (read-only)")]
    public int speechAttempts;
    public int currentTask;

    private float taskStartTime;

    private void Start()
    {
        var collector = FindObjectOfType<TranscriptionCollector>(true);
        if (collector != null)
        {
            collector.onTranscriptReceived.AddListener(OnTranscript);
        }
    }

    private void OnTranscript(string text)
    {
        speechAttempts++;
        LogEvent("speech-attempt", $"count={speechAttempts}; text={text}");
    }

    // ── Task timing ──────────────────────────────────────────────────────────

    public void MarkTaskStart(int taskNumber)
    {
        currentTask = taskNumber;
        taskStartTime = Time.time;
        speechAttempts = 0;
        LogEvent("task-start", $"task={taskNumber}");
    }

    public void MarkTaskComplete(int taskNumber)
    {
        float elapsed = Time.time - taskStartTime;
        LogEvent("task-complete", $"task={taskNumber}; seconds={elapsed:F1}; speechAttempts={speechAttempts}");
    }

    // ── Generic event ────────────────────────────────────────────────────────

    public void LogEvent(string type, string detail)
    {
        Debug.Log($"[StudyLog] {type}: {detail}");
        StartCoroutine(PostEvent(type, detail));
    }

    private IEnumerator PostEvent(string type, string detail)
    {
        var json = "{\"type\":" + Quote(type) + ",\"detail\":" + Quote(detail) + "}";
        using var req = new UnityWebRequest(serverUrl + "/event", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        // Silent on failure – logging must never disrupt the session.
    }

    private static string Quote(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.Append("\"").ToString();
    }
}
