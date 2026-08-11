using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays the live speech-to-text transcript in VR so the participant can
/// see what the system understood from their spoken instruction.
/// Attach to any GameObject; assign the TranscriptText field in the Inspector.
/// </summary>
public class TranscriptDisplay : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI transcriptText;
    public TextMeshProUGUI statusText;
    public GameObject displayPanel;

    [Header("Settings")]
    [Tooltip("How long the transcript stays visible after it is set (0 = indefinitely).")]
    public float autoHideAfterSeconds = 8f;
    [Tooltip("Prefix shown while the user is still speaking.")]
    public string recordingLabel = "[Listening...]";

    private Coroutine hideCoroutine;
    private bool isRecording;

    private void Start()
    {
        SetPanelVisible(false);
        SetTranscriptText("");
    }

    // Condition A means NO feedback of any kind — including the transcript.
    // Without this, showing a transcript re-activates the panel that the
    // StudyConditionManager hid, making conditions A and B look identical.
    private bool SuppressedByCondition()
    {
        var cond = FindObjectOfType<StudyConditionManager>(true);
        return cond && cond.IsConditionA();
    }

    /// <summary>Called by TranscriptionCollector when the user starts speaking.</summary>
    public void OnRecordingStart()
    {
        if (SuppressedByCondition()) return;
        isRecording = true;
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        SetPanelVisible(true);
        if (statusText) statusText.text = recordingLabel;
    }

    /// <summary>Called by TranscriptionCollector when the user stops speaking.</summary>
    public void OnRecordingStop()
    {
        isRecording = false;
        if (statusText) statusText.text = "";
    }

    /// <summary>
    /// Called by TranscriptionCollector when the server returns a transcript.
    /// Shows the text the participant said so they can verify the system understood them.
    /// </summary>
    public void ShowTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;
        if (SuppressedByCondition()) return;

        SetPanelVisible(true);
        SetTranscriptText(transcript);

        if (autoHideAfterSeconds > 0f)
        {
            if (hideCoroutine != null) StopCoroutine(hideCoroutine);
            hideCoroutine = StartCoroutine(HideAfterDelay(autoHideAfterSeconds));
        }
    }

    public void ClearTranscript()
    {
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        SetTranscriptText("");
        SetPanelVisible(false);
    }

    private void SetTranscriptText(string text)
    {
        if (transcriptText) transcriptText.text = text;
    }

    private void SetPanelVisible(bool visible)
    {
        if (displayPanel) displayPanel.SetActive(visible);
    }

    private IEnumerator HideAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ClearTranscript();
    }
}
