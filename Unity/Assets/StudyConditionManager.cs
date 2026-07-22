using UnityEngine;
using TMPro;

/// <summary>
/// Manages the three study conditions:
///   A – No feedback: participant sees only the scene result.
///   B – Text panel:  a 2D panel shows transcript, code summary, and error message.
///   C – Embodied agent: the virtual agent speaks and the text panel is also available.
///
/// Set the condition in the Inspector before a session, or call SetCondition() at runtime.
/// All condition-specific GameObjects are toggled here so the session is reproducible.
/// </summary>
public class StudyConditionManager : MonoBehaviour
{
    public enum Condition { A_NoFeedback, B_TextPanel, C_EmbodiedAgent }

    [Header("Active Condition")]
    public Condition activeCondition = Condition.A_NoFeedback;

    [Header("Condition B – Text Panel")]
    public GameObject feedbackPanelRoot;

    [Header("Condition C – Embodied Agent")]
    public GameObject embodiedAgentRoot;
    public EmbodiedAgentBody embodiedAgentBody;

    [Header("Transcript display (visible in B and C)")]
    public GameObject transcriptPanelRoot;

    [Header("Researcher Info")]
    public TextMeshProUGUI conditionLabel;

    private void Start()
    {
        ApplyCondition(activeCondition);
    }

    public void SetConditionA() => ApplyCondition(Condition.A_NoFeedback);
    public void SetConditionB() => ApplyCondition(Condition.B_TextPanel);
    public void SetConditionC() => ApplyCondition(Condition.C_EmbodiedAgent);

    public void ApplyCondition(Condition condition)
    {
        activeCondition = condition;

        bool showPanel  = condition == Condition.B_TextPanel || condition == Condition.C_EmbodiedAgent;
        bool showAgent  = condition == Condition.C_EmbodiedAgent;
        bool showTranscript = condition != Condition.A_NoFeedback;

        if (feedbackPanelRoot)  feedbackPanelRoot.SetActive(showPanel);
        if (embodiedAgentRoot)  embodiedAgentRoot.SetActive(showAgent);
        if (embodiedAgentBody)  embodiedAgentBody.SetVisible(showAgent);
        if (transcriptPanelRoot) transcriptPanelRoot.SetActive(showTranscript);

        var label = condition switch
        {
            Condition.A_NoFeedback    => "Condition A – No Feedback",
            Condition.B_TextPanel     => "Condition B – Text Panel",
            Condition.C_EmbodiedAgent => "Condition C – Embodied Agent",
            _                         => ""
        };

        if (conditionLabel) conditionLabel.text = label;
        Debug.Log($"[StudyCondition] Active: {label}");
    }

    public bool IsConditionA() => activeCondition == Condition.A_NoFeedback;
    public bool IsConditionB() => activeCondition == Condition.B_TextPanel;
    public bool IsConditionC() => activeCondition == Condition.C_EmbodiedAgent;
}
