# AgenticXR physical-VR rehearsal checklist

Method version: `method-draft-2026-08-22`

Build identifier: ____________________  Date: __________  Operator: ____________________

Record `PASS`, `FAIL`, or `NOT RUN` for every item. A failure blocks participant use until its
disposition is recorded. These checks require real XR hardware or external services and are not CI coverage.
For each row, describe the observed behaviour; do not copy the expected behaviour into the evidence cell.

| Physical rehearsal | Participant must see | Operator response | Scientific state must | Result / evidence |
|---|---|---|---|---|
| Headset removed and replaced mid-trial | A calm paused/interrupted status, then the same trial on return | Check comfort and fit; resume only after the participant confirms readiness | Preserve participant, trial, condition, task, timer exclusion, and interaction chain | ______ |
| Tracking lost and reacquired | Tracking-loss status without a false success or object move | Restore tracking; verify pose and active variant before resuming | Preserve the trial and record interruption/resumption; detector remains idempotent | ______ |
| Guardian trigger / recentre | Guardian/recentre UI, never forced participant locomotion | Re-establish a safe origin and verify reachable objects | Preserve assignments and measurements; record the interruption | ______ |
| Controller disconnect; battery warning | Visible device warning and paused input | Replace/reconnect controller; confirm mappings before resume | Preserve trial and decision state; no duplicated approval or detector event | ______ |
| Hand-tracking to controller switching and back | Input mode/status change without an accidental action | Verify the intended input mode and repeat only an unregistered gesture | Preserve consent locks and trial identity; no phantom response | ______ |
| OpenXR session interruption | Paused/interrupted state and the same trial after recovery | Restore OpenXR; if recovery fails, mark the trial partial rather than restarting invisibly | Preserve the journalled assignment and resume fingerprint | ______ |
| Participant physically leaves the required pose | Instruction to return, without automatic locomotion | Pause; guide verbally; confirm comfort and safe pose | Preserve the trial; detector must not fire solely from the interruption | ______ |
| Microphone unavailable or silent | Actionable microphone/silence message and a retry route | Check device selection and levels; retry the utterance once ready | Record the failed utterance as transcription_error; preserve correlation and trial | ______ |
| STT service unreachable mid-utterance | Transcription-failed status with no fabricated intent | Restore service or mark the trial partial under the protocol | Record transcription_error and retain metadata only, never audio/transcript | ______ |
| Link cable disconnect / Air Link drop | Connection-loss status, not a successful completion | Restore the tether; if state cannot be proven, mark partial and stop | Preserve or fail closed on scientific state; never silently start a replacement trial | ______ |
| D6 door safety walkthrough | A non-blocking training door off all real exits; two scripted proxy avatars; no participant locomotion | Inspect door collider list and egress placement in-headset; traverse every exit path | Keep the door trial-local; reset it between trials; no persistence or locomotion event | ______ |
| L4 door legibility at approximately 5.3 m | The practice-door state change is unambiguous from the participant position | Observe the full closed-to-open change from the marked participant origin | Preserve the authored door pose and record any visibility failure before participant use | ______ |
| Task-object raycast pointing at 2.6-4.0 m | Every task object can be selected unambiguously without hitting a neighbouring object | Point to every authored task object from the participant position and record occlusion or ambiguity | Preserve stable object identity and prevent a raycast from resolving to the wrong target | ______ |
| A/B room distinctness | Each A/B pair feels like a different room rather than the same room recoloured | Rehearse both variants of every task and record whether layout and role changes are perceptible | Preserve equal geometric difficulty while confirming the counterbalanced manipulation is meaningful | ______ |
| Tracked study rig | Head and both controller/hand poses follow the Quest/OpenXR hardware without a desktop-camera fallback | Enter Play Mode, move each tracked device independently, and verify handedness | Keep exactly one active Ubiq XR player and record any tracking loss as an interruption | ______ |
| Participant-controlled region reach | L2 and L4 regions can be entered safely using the authored joystick/teleport routes | Reach each region without experimenter input or agent-forced locomotion | Emit region entry once and retain the participant's voluntary movement path | ______ |
| Grasp/use task loop | L1 tools, L2 parts, and the L3 marker can be grasped and released; L3 done responds once to trigger use | Exercise every A/B object and repeat after trial reset | Preserve stable IDs and detector idempotence; proximity alone must not press done | ______ |

Per-phase wall-clock (researcher session 1, balancingBlock 0): consent/demographics ____; training ____;
trials 1-4 ____; break 1 ____; trials 5-7 ____; break 2 ____; trials 8-10 ____; final battery/debrief ____;
total ____ (target approximately 120 minutes).

Per-phase wall-clock (researcher session 2, balancingBlock 1): consent/demographics ____; training ____;
trials 1-4 ____; break 1 ____; trials 5-7 ____; break 2 ____; trials 8-10 ____; final battery/debrief ____;
total ____ (target approximately 120 minutes).

Overall result: `PASS / FAIL / NOT RUN`  Investigator sign-off: ____________________
