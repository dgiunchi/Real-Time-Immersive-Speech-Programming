# DreamCodeVR2 study conditions

| Condition | Participant capability | Quest progression |
| --- | --- | --- |
| C1 — Voice Command Baseline | STT with predefined scene commands only | fixed `vertical_slice_fixed` quest |
| C2 — Player Authoring | Player-initiated validated SceneAPI and BehaviorAPI changes | same fixed quest as C1 |
| C3 — Dynamic Storytelling | exactly the C2 authoring capabilities | server supplies the next task only after the current task completes |

C1 to C2 changes predefined commands into participant-requested world modifications. C2 to C3 changes fixed task progression into post-completion next-task generation. No condition contains mixed-initiative or proactive authoring.
