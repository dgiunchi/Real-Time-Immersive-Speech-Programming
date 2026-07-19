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
    public float autoHideAfterSeconds = 12f;
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

    public void ShowListening()
    {
        RevealPanel();
        if (statusLine) statusLine.text = "Listening...";
        if (statusIcon) statusIcon.color = neutralColor;
    }

    public void ShowProcessing()
    {
        RevealPanel();
        if (statusLine) statusLine.text = "Processing...";
        if (statusIcon) statusIcon.color = neutralColor;
    }

    public void Clear()
    {
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
