using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Condition-B text panel that shows the participant:
///   1. What the system heard (transcript)
///   2. What the system did (code summary / action taken)
///   3. Whether there was a problem – and a plain-language description of it
///
/// The panel is driven by CodeGenerationManager and WizardOfOzController.
/// All text is set programmatically; the researcher triggers responses via WoZ.
/// </summary>
public class FeedbackPanelController : MonoBehaviour
{
    [Header("Panel Sections")]
    public TextMeshProUGUI transcriptLine;
    public TextMeshProUGUI actionLine;
    public TextMeshProUGUI statusLine;
    public TextMeshProUGUI errorDescriptionLine;

    [Header("Visual state")]
    public Image statusIcon;
    public Color successColor = new Color(0.2f, 0.8f, 0.2f);
    public Color errorColor   = new Color(0.9f, 0.3f, 0.3f);
    public Color neutralColor = new Color(0.8f, 0.8f, 0.8f);

    [Header("Auto-hide")]
    // Zero means never. The explanation stays until something ends the failure
    // it describes — a successful outcome, or the next trial's scene reset.
    //
    // It was twelve seconds. That is fine for a fluent reader glancing at it
    // once and wrong for everyone else: a participant thinking about what to say
    // next, or reading in a second language, looks back to find the explanation
    // gone. In a study whose entire subject is whether people can work out WHY
    // something failed, taking the explanation away mid-thought is removing the
    // evidence and then measuring the diagnosis.
    //
    // It also made condition B and condition C differ in how long the same
    // sentence remained readable, which is not the difference the design is
    // testing.
    public float autoHideAfterSeconds = 0f;
    private Coroutine hideCoroutine;

    public GameObject panelRoot;

    // ── Public API called by other scripts ────────────────────────────────────

    public void ShowTranscript(string transcript)
    {
        RevealPanel();
        if (transcriptLine) transcriptLine.text = "You said: \"" + transcript + "\"";
    }

    public void ShowSuccess(string actionSummary)
    {
        RevealPanel();
        if (actionLine)           actionLine.text = "Action: " + actionSummary;
        if (statusLine)           statusLine.text = "Done";
        if (errorDescriptionLine) errorDescriptionLine.text = "";
        if (statusIcon)           statusIcon.color = successColor;
        ScheduleHide();
    }

    public void ShowError(string actionSummary, string errorDescription)
    {
        RevealPanel();
        if (actionLine)           actionLine.text = "Action: " + actionSummary;
        if (statusLine)           statusLine.text = "There was a problem";
        if (errorDescriptionLine) errorDescriptionLine.text = errorDescription;
        if (statusIcon)           statusIcon.color = errorColor;
        ScheduleHide();
    }

    public void ShowVolumeTooLow()
    {
        RevealPanel();
        if (statusLine)           statusLine.text = "Could not hear you clearly";
        if (errorDescriptionLine) errorDescriptionLine.text = "Please speak louder or closer to the microphone and try again.";
        if (statusIcon)           statusIcon.color = neutralColor;
        ScheduleHide();
    }

    // Listening and Processing both mean "a NEW attempt is under way", so the
    // previous attempt's explanation stops being true at that moment and has to
    // go. Leaving it up produced the worst state the panel can be in: a stale
    // error sitting under a live "Processing..." line, which reads as the system
    // reporting that failure again about the sentence just spoken.
    public void ShowListening()
    {
        RevealPanel();
        if (statusLine)           statusLine.text = "Listening...";
        if (errorDescriptionLine) errorDescriptionLine.text = "";
        if (statusIcon)           statusIcon.color = neutralColor;
    }

    public void ShowProcessing()
    {
        RevealPanel();
        if (statusLine)           statusLine.text = "Processing...";
        if (errorDescriptionLine) errorDescriptionLine.text = "";
        if (statusIcon)           statusIcon.color = neutralColor;
    }

    public void Clear()
    {
        // The moment the explanation stopped being available to read.
        //
        // Reported before the panel is hidden, and only when it was actually up,
        // so the log distinguishes an explanation that timed out from one that
        // was never shown. Dwell on the panel is only interpretable against this
        // — four seconds of gaze means something different when the panel was up
        // for five than when it was up for twelve.
        if (panelRoot && panelRoot.activeInHierarchy)
        {
            StudyOutcomes.ReportHeadsetEvent(this, "feedback-offset", "panel");
        }

        if (transcriptLine)       transcriptLine.text = "";
        if (actionLine)           actionLine.text = "";
        if (statusLine)           statusLine.text = "";
        if (errorDescriptionLine) errorDescriptionLine.text = "";
        if (statusIcon)           statusIcon.color = neutralColor;
        if (panelRoot)            panelRoot.SetActive(false);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void RevealPanel()
    {
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        if (panelRoot) panelRoot.SetActive(true);
    }

    private void ScheduleHide()
    {
        if (autoHideAfterSeconds > 0f)
        {
            if (hideCoroutine != null) StopCoroutine(hideCoroutine);
            hideCoroutine = StartCoroutine(HideAfterDelay(autoHideAfterSeconds));
        }
    }

    private IEnumerator HideAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Clear();
    }
}
