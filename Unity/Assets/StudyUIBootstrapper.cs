using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-drop study setup.
///
/// Add this single component to an empty GameObject in the scene, press Play,
/// and it will build all the study UI (transcript panel, feedback panel,
/// agent subtitle panel, code panel) as a world-space canvas and wire every
/// reference into the existing managers automatically. This removes almost all
/// manual Inspector wiring.
///
/// It also ensures the study manager components exist (adds them if missing):
///   StudyConditionManager, TranscriptDisplay, FeedbackPanelController,
///   EmbodiedAgentDialogue.
///
/// Set the condition (A/B/C) here or on the StudyConditionManager it creates.
/// </summary>
[DefaultExecutionOrder(-100)]
public class StudyUIBootstrapper : MonoBehaviour
{
    [Header("Condition (A = none, B = panel, C = agent)")]
    public StudyConditionManager.Condition condition = StudyConditionManager.Condition.B_TextPanel;

    [Header("Placement")]
    [Tooltip("If set, panels are parented here. Otherwise they float in front of the main camera.")]
    public Transform anchor;
    public Vector3 panelWorldPosition = new Vector3(0f, 1.6f, 2.0f);
    public float panelScale = 0.0025f;
    [Tooltip("If true, the canvas follows the camera each frame (billboard).")]
    public bool faceCamera = true;

    [Header("Build options")]
    public bool buildOnStart = true;
    public bool addMissingManagers = true;

    [Header("Study isolation")]
    [Tooltip("Disable the original DreamCodeVR in-VR task menu (ChooseTask/CheckResult) so it can't hijack the session and bounce the participant back to its menu. Recommended ON for the study.")]
    public bool disableLegacyMenu = true;

    // Built references
    private Canvas canvas;
    private StudyConditionManager conditionManager;
    private TranscriptDisplay transcriptDisplay;
    private FeedbackPanelController feedbackPanel;
    private EmbodiedAgentDialogue agentDialogue;
    private EmbodiedAgentBody agentBody;

    private GameObject transcriptPanel, feedbackPanelRoot, agentPanel, codePanel;
    // Deliberately NOT handed to StudyConditionManager: this panel is identical
    // in A, B and C, so it must never be one of the things a condition toggles.
    private GameObject questionPanelRoot;
    private QuestionPanelController questionPanel;
    private TextMeshProUGUI pendingProbeLine, pendingConfidenceLine, pendingScaleLine;

    private void Start()
    {
        if (buildOnStart) Build();
    }

    private void LateUpdate()
    {
        if (faceCamera && canvas && Camera.main)
        {
            canvas.transform.rotation = Quaternion.LookRotation(
                canvas.transform.position - Camera.main.transform.position);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Build()
    {
        DisableLegacyMenuSystem();
        BuildCanvas();
        BuildTranscriptPanel();
        BuildFeedbackPanel();
        BuildQuestionPanel();
        BuildAgentPanel();
        BuildCodePanel();
        EnsureManagers();
        WireEverything();
        ApplyCondition();
        Debug.Log("[StudyUIBootstrapper] Study UI built and wired. Condition = " + condition);
    }

    // Turn off the original DreamCodeVR task menu so it can't run its own flow
    // underneath the study (its "Start" buttons and target-area completion send
    // the participant back to that menu, which reads as "quits and won't start").
    private void DisableLegacyMenuSystem()
    {
        if (!disableLegacyMenu) return;
        int n = 0;
        foreach (var ct in FindObjectsOfType<ChooseTask>(true))
        {
            if (ct.menu) ct.menu.SetActive(false);
            ct.enabled = false;
            n++;
        }
        foreach (var cr in FindObjectsOfType<CheckResult>(true))
        {
            cr.enabled = false;
            n++;
        }
        Debug.Log($"[StudyUIBootstrapper] Legacy DreamCodeVR menu/task system disabled ({n} components). Study web panel is now the only driver.");
    }

    // ── Canvas ────────────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        var go = new GameObject("StudyCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(900, 1000);

        if (anchor)
        {
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.zero;
        }
        else
        {
            go.transform.position = panelWorldPosition;
        }
        go.transform.localScale = Vector3.one * panelScale;
    }

    // ── Panels ────────────────────────────────────────────────────────────────

    private void BuildTranscriptPanel()
    {
        transcriptPanel = MakePanel("TranscriptPanel", new Vector2(0, 420), new Vector2(860, 150),
            new Color(0.05f, 0.06f, 0.09f, 0.92f));

        MakeText(transcriptPanel, "Status", new Vector2(0, 48), new Vector2(820, 40),
            "", 26, new Color(0.6f, 0.75f, 1f), out var statusTxt);
        MakeText(transcriptPanel, "Transcript", new Vector2(0, -18), new Vector2(820, 90),
            "Waiting for speech…", 34, Color.white, out var transcriptTxt);

        pendingTranscriptStatus = statusTxt;
        pendingTranscriptText = transcriptTxt;
    }

    /// The two questions the participant is asked after every failure.
    ///
    /// Built low on the canvas, below the feedback panel, so that in condition B
    /// it never overlaps the explanation and in every condition it occupies the
    /// same place. Amber rather than the panels' blue-grey, because it is the
    /// researcher speaking rather than the system reporting, and a participant
    /// should not have to work out which of those they are reading.
    private void BuildQuestionPanel()
    {
        questionPanelRoot = MakePanel("QuestionPanel", new Vector2(0, -330), new Vector2(900, 300),
            new Color(0.10f, 0.08f, 0.04f, 0.94f));

        MakeText(questionPanelRoot, "ProbeLine", new Vector2(0, 95), new Vector2(860, 80),
            "", 30, new Color(1f, 0.86f, 0.55f), out pendingProbeLine);
        MakeText(questionPanelRoot, "ConfidenceLine", new Vector2(0, -15), new Vector2(860, 110),
            "", 28, new Color(1f, 0.86f, 0.55f), out pendingConfidenceLine);
        MakeText(questionPanelRoot, "ScaleLine", new Vector2(0, -110), new Vector2(860, 46),
            "", 24, new Color(0.75f, 0.78f, 0.85f), out pendingScaleLine);
    }

    private void BuildFeedbackPanel()
    {
        feedbackPanelRoot = MakePanel("FeedbackPanel", new Vector2(0, 90), new Vector2(860, 380),
            new Color(0.07f, 0.09f, 0.13f, 0.95f));

        // status icon strip at top
        var iconGo = new GameObject("StatusIcon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(feedbackPanelRoot.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchoredPosition = new Vector2(0, 150);
        iconRt.sizeDelta = new Vector2(820, 10);
        pendingStatusIcon = iconGo.GetComponent<Image>();
        pendingStatusIcon.color = new Color(0.8f, 0.8f, 0.8f);

        MakeText(feedbackPanelRoot, "TranscriptLine", new Vector2(0, 100), new Vector2(820, 60),
            "", 28, new Color(0.75f, 0.82f, 1f), out pendingFbTranscript);
        MakeText(feedbackPanelRoot, "ActionLine", new Vector2(0, 30), new Vector2(820, 60),
            "", 26, Color.white, out pendingFbAction);
        MakeText(feedbackPanelRoot, "StatusLine", new Vector2(0, -40), new Vector2(820, 50),
            "", 30, Color.white, out pendingFbStatus);
        MakeText(feedbackPanelRoot, "ErrorLine", new Vector2(0, -120), new Vector2(820, 90),
            "", 24, new Color(1f, 0.7f, 0.6f), out pendingFbError);
    }

    private void BuildAgentPanel()
    {
        agentPanel = MakePanel("AgentSubtitlePanel", new Vector2(0, -240), new Vector2(860, 140),
            new Color(0.10f, 0.06f, 0.14f, 0.95f));
        MakeText(agentPanel, "AgentLabel", new Vector2(0, 44), new Vector2(820, 34),
            "Assistant", 22, new Color(0.8f, 0.65f, 1f), out _);
        MakeText(agentPanel, "Subtitle", new Vector2(0, -14), new Vector2(820, 90),
            "", 28, Color.white, out pendingAgentSubtitle);
    }

    private void BuildCodePanel()
    {
        codePanel = MakePanel("CodeResultPanel", new Vector2(0, -430), new Vector2(860, 160),
            new Color(0.03f, 0.03f, 0.03f, 0.92f));
        MakeText(codePanel, "CodeLabel", new Vector2(0, 60), new Vector2(820, 30),
            "Generated code", 20, new Color(0.5f, 0.9f, 0.6f), out _);
        MakeText(codePanel, "CodeText", new Vector2(0, -14), new Vector2(820, 110),
            "", 16, new Color(0.85f, 0.9f, 0.85f), out pendingCodeText);
        pendingCodeText.alignment = TextAlignmentOptions.TopLeft;
        pendingCodeText.enableWordWrapping = true;
        codePanel.SetActive(false);
    }

    // temp holders set during Build*, consumed in WireEverything
    private TextMeshProUGUI pendingTranscriptStatus, pendingTranscriptText;
    private TextMeshProUGUI pendingFbTranscript, pendingFbAction, pendingFbStatus, pendingFbError;
    private TextMeshProUGUI pendingAgentSubtitle, pendingCodeText;
    private Image pendingStatusIcon;

    // ── Managers ──────────────────────────────────────────────────────────────

    private void EnsureManagers()
    {
        conditionManager  = FindOrAdd<StudyConditionManager>();
        transcriptDisplay = FindOrAdd<TranscriptDisplay>();
        feedbackPanel     = FindOrAdd<FeedbackPanelController>();
        agentDialogue     = FindOrAdd<EmbodiedAgentDialogue>();

        // Zero-config LAN discovery: finds the Mac's server automatically on any
        // shared Wi-Fi so the headset never needs a hardcoded IP or a rebuild.
        FindOrAdd<ServerAutoDiscovery>();
        FindOrAdd<StudyTelemetry>();

        // Runs the study outcomes as pre-compiled C# (works on the Quest, unlike
        // the runtime Roslyn path which needs a JIT the IL2CPP build doesn't have).
        var outcomes = FindOrAdd<StudyOutcomes>();
        if (outcomes && !outcomes.spawnOrigin)
        {
            var codeGen = FindObjectOfType<CodeGenerationManager>(true);
            if (codeGen && codeGen.targetObject) outcomes.spawnOrigin = codeGen.targetObject.transform;
        }
    }

    private T FindOrAdd<T>() where T : Component
    {
        var found = FindObjectOfType<T>(true);
        if (found) return found;
        if (!addMissingManagers) return null;
        return gameObject.AddComponent<T>();
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private void WireEverything()
    {
        // QuestionPanelController — every condition, wired before anything that
        // could switch panels off, and never registered with the condition
        // manager.
        questionPanel = FindOrAdd<QuestionPanelController>();
        if (questionPanel)
        {
            questionPanel.panelRoot      = questionPanelRoot;
            questionPanel.probeLine      = pendingProbeLine;
            questionPanel.confidenceLine = pendingConfidenceLine;
            questionPanel.scaleLine      = pendingScaleLine;
            questionPanel.Hide();
        }

        // TranscriptDisplay
        if (transcriptDisplay)
        {
            transcriptDisplay.transcriptText = pendingTranscriptText;
            transcriptDisplay.statusText = pendingTranscriptStatus;
            transcriptDisplay.displayPanel = transcriptPanel;
        }

        // FeedbackPanelController
        if (feedbackPanel)
        {
            feedbackPanel.transcriptLine = pendingFbTranscript;
            feedbackPanel.actionLine = pendingFbAction;
            feedbackPanel.statusLine = pendingFbStatus;
            feedbackPanel.errorDescriptionLine = pendingFbError;
            feedbackPanel.statusIcon = pendingStatusIcon;
            feedbackPanel.panelRoot = feedbackPanelRoot;
        }

        // EmbodiedAgentDialogue
        if (agentDialogue)
        {
            agentDialogue.subtitleText = pendingAgentSubtitle;
            agentDialogue.subtitlePanel = agentPanel;
            if (!agentDialogue.agentAudioSource)
                agentDialogue.agentAudioSource = agentDialogue.GetComponent<AudioSource>()
                    ?? agentDialogue.gameObject.AddComponent<AudioSource>();

            // Give condition C a visible body beside the panel, and drive its
            // animation from the agent's speaking events.
            agentBody = FindOrAdd<EmbodiedAgentBody>();
            if (agentBody)
            {
                // Beside the panel horizontally, but at floor level rather than
                // panel level. A full-size avatar hung off the panel's own
                // height would stand with its feet in mid-air; EmbodiedAgentBody
                // measures its bounds and drops it onto the floor from here, so
                // the y given here only needs to be the ground.
                //
                // Pushed further out than the old -1.15 because the agent is now
                // a person rather than a small blob: at human scale that
                // distance put it close enough to feel like it was standing over
                // the panel.
                agentBody.worldPosition = new Vector3(
                    panelWorldPosition.x - 1.6f, 0f, panelWorldPosition.z);
                agentDialogue.onAgentStartedSpeaking.AddListener(agentBody.OnStartedSpeaking);
                agentDialogue.onAgentFinishedSpeaking.AddListener(agentBody.OnFinishedSpeaking);
            }
        }

        // StudyConditionManager toggles
        if (conditionManager)
        {
            conditionManager.feedbackPanelRoot = feedbackPanelRoot;
            conditionManager.embodiedAgentRoot = agentPanel;
            conditionManager.transcriptPanelRoot = transcriptPanel;
            conditionManager.embodiedAgentBody = agentBody;
        }

        // Hook TranscriptionCollector → transcript display
        // (the DynamicCompiler scene doesn't include one, so add it if missing)
        //
        // ONE PANEL SHOWS WHAT THE PARTICIPANT SAID, and it is the transcript
        // panel. The feedback panel used to be sent the transcript as well, which
        // was wrong twice over.
        //
        // In condition B it put the participant's own words on screen twice, a
        // few centimetres apart, so the panel carrying the explanation opened
        // with a line that was already visible above it. The explanation is the
        // manipulation; anything sharing its panel competes with it for the one
        // thing this study measures people reading.
        //
        // In condition C it was worse than redundant. The feedback panel is
        // condition B's channel and ApplyCondition deliberately switches it off
        // for C — but ShowTranscript called RevealPanel, which switched it back
        // on. So condition C ran with three panels up: the transcript, the
        // agent's subtitles, and a text panel that was supposed to be the thing
        // condition C does not have. That is the difference between the two
        // conditions, quietly undone at runtime.
        var collector = FindOrAdd<TranscriptionCollector>();
        if (collector)
        {
            collector.transcriptDisplay = transcriptDisplay;
        }

        // Hook CodeGenerationManager → feedback panel + code display
        var codeGen = FindObjectOfType<CodeGenerationManager>(true);
        if (codeGen)
        {
            codeGen.feedbackPanel = feedbackPanel;
            codeGen.codeResultText = pendingCodeText;
            codeGen.codeResultPanel = codePanel;
        }
    }

    private void ApplyCondition()
    {
        if (conditionManager)
        {
            conditionManager.ApplyCondition(condition);
        }
    }

    // ── UI construction helpers ──────────────────────────────────────────────

    private GameObject MakePanel(string name, Vector2 anchoredPos, Vector2 size, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = bg;
        return go;
    }

    private void MakeText(GameObject parent, string name, Vector2 pos, Vector2 size,
        string content, float fontSize, Color color, out TextMeshProUGUI text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
    }
}
