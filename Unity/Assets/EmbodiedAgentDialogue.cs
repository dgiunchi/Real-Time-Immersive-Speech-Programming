using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Condition-C embodied agent pre-scripted dialogue system.
///
/// The virtual agent (peer avatar) speaks to the participant at two moments:
///   1. Pre-execution: after hearing the instruction, the agent acknowledges
///      what it understood and sets expectations ("I'll try to create a ball…").
///   2. Post-execution: after seeing the result, the agent comments on it
///      ("Hmm, the sphere looks squashed – do you want me to fix the scale?").
///
/// In WoZ mode the researcher manually triggers lines via the WizardOfOzController.
/// Dialogue lines are pre-scripted here and matched to each task + error type.
/// Audio playback is optional (assign AudioClips in the Inspector, or the
/// agent shows speech as subtitles only if no clips are assigned).
/// </summary>
public class EmbodiedAgentDialogue : MonoBehaviour
{
    // ── Dialogue data ─────────────────────────────────────────────────────────

    [Serializable]
    public struct DialogueLine
    {
        [TextArea(2, 4)]
        public string text;
        public AudioClip clip;          // optional TTS pre-recorded audio
        public float fallbackDuration;  // seconds to display if no clip
    }

    [Serializable]
    public struct TaskDialogue
    {
        public string taskName;
        public DialogueLine preSuccess;
        public DialogueLine postSuccess;
        public DialogueLine preError1;
        public DialogueLine postError1;
        public DialogueLine preError2;
        public DialogueLine postError2;
        public DialogueLine preError3;
        public DialogueLine postError3;
        public DialogueLine preError4;
        public DialogueLine postError4;
    }

    [Header("Dialogue scripts")]
    public TaskDialogue[] taskDialogues;

    // ── UI and Audio ──────────────────────────────────────────────────────────

    [Header("Agent speech subtitle")]
    public TextMeshProUGUI subtitleText;
    public GameObject subtitlePanel;

    [Header("Agent audio source")]
    public AudioSource agentAudioSource;

    [Header("Events")]
    public UnityEvent onAgentStartedSpeaking;
    public UnityEvent onAgentFinishedSpeaking;

    // ── Internal ──────────────────────────────────────────────────────────────

    private Coroutine currentLine;
    private int activeTask = 0;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        PopulateDefaultDialogue();
        HideSubtitle();
    }

    // ── Public API (called by WizardOfOzController) ───────────────────────────

    public void SetActiveTask(int taskIndex)
    {
        activeTask = Mathf.Clamp(taskIndex, 0, taskDialogues.Length - 1);
    }

    public void SpeakPreSuccess()   => Speak(taskDialogues[activeTask].preSuccess);
    public void SpeakPostSuccess()  => Speak(taskDialogues[activeTask].postSuccess);
    public void SpeakPreError1()    => Speak(taskDialogues[activeTask].preError1);
    public void SpeakPostError1()   => Speak(taskDialogues[activeTask].postError1);
    public void SpeakPreError2()    => Speak(taskDialogues[activeTask].preError2);
    public void SpeakPostError2()   => Speak(taskDialogues[activeTask].postError2);
    public void SpeakPreError3()    => Speak(taskDialogues[activeTask].preError3);
    public void SpeakPostError3()   => Speak(taskDialogues[activeTask].postError3);
    public void SpeakPreError4()    => Speak(taskDialogues[activeTask].preError4);
    public void SpeakPostError4()   => Speak(taskDialogues[activeTask].postError4);

    public void StopSpeaking()
    {
        if (currentLine != null) StopCoroutine(currentLine);
        if (agentAudioSource && agentAudioSource.isPlaying) agentAudioSource.Stop();
        HideSubtitle();
    }

    // ── Core playback ─────────────────────────────────────────────────────────

    private void Speak(DialogueLine line)
    {
        if (string.IsNullOrWhiteSpace(line.text) && line.clip == null) return;
        if (currentLine != null) StopCoroutine(currentLine);
        currentLine = StartCoroutine(PlayLine(line));
    }

    private IEnumerator PlayLine(DialogueLine line)
    {
        onAgentStartedSpeaking?.Invoke();
        ShowSubtitle(line.text);

        if (agentAudioSource && line.clip)
        {
            agentAudioSource.clip = line.clip;
            agentAudioSource.Play();
            yield return new WaitWhile(() => agentAudioSource.isPlaying);
        }
        else
        {
            float duration = line.fallbackDuration > 0f ? line.fallbackDuration
                : Mathf.Max(2f, line.text.Length * 0.055f); // ~55 ms per character
            yield return new WaitForSeconds(duration);
        }

        HideSubtitle();
        onAgentFinishedSpeaking?.Invoke();
    }

    private void ShowSubtitle(string text)
    {
        if (subtitleText)  subtitleText.text = text;
        if (subtitlePanel) subtitlePanel.SetActive(true);
    }

    private void HideSubtitle()
    {
        if (subtitleText)  subtitleText.text = "";
        if (subtitlePanel) subtitlePanel.SetActive(false);
    }

    // ── Default dialogue scripts for the 4 tasks ─────────────────────────────

    private void PopulateDefaultDialogue()
    {
        if (taskDialogues != null && taskDialogues.Length > 0) return;

        taskDialogues = new TaskDialogue[]
        {
            new TaskDialogue
            {
                taskName = "Task 1 – Create a ball",
                preSuccess  = L("Okay, I heard you'd like a ball near your hand. Let me create that for you."),
                postSuccess = L("There you go – a ball right in front of you. You can interact with it now."),
                preError1   = L("You'd like a ball. I'm not sure exactly where – I'll place it at the centre of the room."),
                postError1  = L("I placed a ball, but it ended up at the centre of the room rather than by your hand. Could you tell me where you'd like it?"),
                preError2   = L("Let me create a ball for you."),
                postError2  = L("Hmm – that came out as a cube. I may have misunderstood. Did you mean a sphere?"),
                preError3   = L("Creating a ball near your hand."),
                postError3  = L("The ball appeared, but it seems to be falling through the floor. There might be a collider issue. Would you like me to fix it?"),
                preError4   = L("Alright, I'll place a sphere at your hand."),
                postError4  = L("The sphere looks a bit squashed – that sometimes happens when the scale is inherited from the scene. Want me to reset it?")
            },
            new TaskDialogue
            {
                taskName = "Task 2 – Change colour to green",
                preSuccess  = L("I'll change the colour of the ball to green."),
                postSuccess = L("Done – the ball is now green."),
                preError1   = L("You'd like something green. I'll apply it to all visible objects since I'm not sure which one you mean."),
                postError1  = L("I changed all the objects to green. Was that too many? You can point to a specific object next time."),
                preError2   = L("Changing the colour to green."),
                postError2  = L("That came out as more of a teal. I may have picked the wrong shade. Would you like a brighter green?"),
                preError3   = L("Applying the colour green to the ball."),
                postError3  = L("It turned green for a moment, then reverted. There was a material-instance problem. Let me try again."),
                preError4   = L("I'll make the ball green."),
                postError4  = L("A new green ball appeared instead of colouring the existing one. Did you want me to update the original?")
            },
            new TaskDialogue
            {
                taskName = "Task 3 – Make the ball orbit the cube",
                preSuccess  = L("I'll set the ball to orbit the cube."),
                postSuccess = L("The ball is now orbiting the cube. You can change the speed if you'd like."),
                preError1   = L("Orbiting around the cube – let me try."),
                postError1  = L("The ball is orbiting, but around the centre of the room rather than the cube. I couldn't identify a clear target. Should I fix that?"),
                preError2   = L("Setting up the orbit now."),
                postError2  = L("The orbit is on the wrong axis – it looks tilted. I might have misread the direction. Want a horizontal orbit instead?"),
                preError3   = L("Making the ball orbit the cube."),
                postError3  = L("The orbit radius was too tight – the ball collided with the cube and stopped. Let me adjust the distance."),
                preError4   = L("Starting the orbit."),
                postError4  = L("The ball is moving very fast – almost invisible. I set the speed too high. Should I slow it down?")
            },
            new TaskDialogue
            {
                taskName = "Task 4 – Solar system",
                preSuccess  = L("I'll create a small solar system with a star and an orbiting planet."),
                postSuccess = L("There's your solar system – a star in the middle and a planet orbiting around it."),
                preError1   = L("Creating a solar system. I'll start with a central star."),
                postError1  = L("I only created the star. I wasn't sure whether you wanted planets as well. Would you like me to add an orbiting planet?"),
                preError2   = L("Building the solar system now."),
                postError2  = L("The planet looks squashed – it inherited a non-uniform scale from the star. Let me reset it to a proper sphere."),
                preError3   = L("Assembling the solar system."),
                postError3  = L("The planet is drifting away instead of orbiting – gravity is pulling it off course. I need to disable gravity on it."),
                preError4   = L("Creating your solar system."),
                postError4  = L("That created fifty planets instead of a few. I over-interpreted your instruction. Want me to remove the extras?")
            }
        };
    }

    private static DialogueLine L(string text, float duration = 0f) =>
        new DialogueLine { text = text, fallbackDuration = duration };
}
