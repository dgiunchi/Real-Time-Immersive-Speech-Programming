using TMPro;
using UnityEngine;

/// <summary>
/// Shows the participant the two questions they are asked after every failure.
///
/// WHY THE PARTICIPANT SEES THESE AT ALL
/// Both questions are spoken by the researcher, word for word, in every
/// condition. Putting them on screen as well is not new information — it is the
/// same sentence, readable. It exists because a spoken question in a second
/// language, delivered once, from outside a headset, over the participant's own
/// concentration, is a memory test attached to the study's primary measure. A
/// participant who half-heard "why do you think that happened" and answers a
/// question they half-invented is noise in H1, and nothing downstream can tell
/// that apart from a genuine answer.
///
/// WHY THIS IS NOT THE FEEDBACK PANEL
/// The feedback panel is condition B's manipulation and is switched off in A and
/// C. These questions are asked in ALL conditions, so they need a surface that
/// is not part of what is being manipulated. It is deliberately identical in
/// every condition: same panel, same wording, same position. A constant across
/// cells cannot confound the comparison between them; putting the questions on
/// the feedback panel would have given condition A a panel and quietly turned
/// the control group into a third feedback condition.
///
/// WHAT IS DELIBERATELY NOT SHOWN
/// The three attribution codes — blamed themselves / blamed the system /
/// genuinely unsure — are the WIZARD's coding scheme, and they stay on the
/// wizard's screen. Showing them would convert an open question into a
/// three-way multiple choice, and "genuinely unsure" would stop being something
/// a participant arrives at and start being an option offered to them. The
/// probe's whole value is that the answer is in their own words, so the panel
/// shows the question and nothing else.
///
/// The confidence scale IS shown, because 0-10 is the response format the
/// participant is being asked to use, and a numeric scale that is only ever
/// spoken aloud is genuinely hard to answer.
/// </summary>
public class QuestionPanelController : MonoBehaviour
{
    [Header("Wired by StudyUIBootstrapper")]
    public GameObject panelRoot;
    public TextMeshProUGUI probeLine;
    public TextMeshProUGUI confidenceLine;
    public TextMeshProUGUI scaleLine;
    public TextMeshProUGUI anchorLine;

    // Kept in one place so the headset and the wizard panel cannot drift apart.
    // If either sentence changes, it changes in both or the participant is
    // reading one question while being asked another.
    public const string ProbeText =
        "In your own words, why do you think that happened?";
    public const string ConfidenceText =
        "How confident are you that there is something you could say differently " +
        "that would make that work?";
    // Every point on the scale, not just its ends.
    //
    // This read "0 = not at all · 10 = completely", which describes the anchors
    // accurately and shows the scale not at all: two labelled values with
    // nothing between them look like two options, and a participant answering
    // that picks one of the two. Showing all eleven numbers is what makes it a
    // scale rather than a choice, and it is the difference between a mediator
    // with usable variance and a bimodal one.
    private const string ScaleText =
        "0    1    2    3    4    5    6    7    8    9    10";
    private const string AnchorText =
        "not at all confident                    completely confident";

    private void Awake()
    {
        // Never up at the start of a trial. It appears with a failure and goes
        // when the trial resolves.
        Hide();
    }

    public void Show()
    {
        if (probeLine)      probeLine.text      = ProbeText;
        if (confidenceLine) confidenceLine.text = ConfidenceText;
        if (scaleLine)      scaleLine.text      = ScaleText;
        if (anchorLine)     anchorLine.text     = AnchorText;
        if (panelRoot)      panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot) panelRoot.SetActive(false);
    }
}
