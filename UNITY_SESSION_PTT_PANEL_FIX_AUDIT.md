# Unity session / PTT / panel fix audit

## SESSION READY GUARD

`ExperimentConditionManager.IsResearcherSessionReady` requires `sessionStarted`, no completed session, non-empty session ID, and the status-verification transition that emits `SESSION_READY`.

## PTT BLOCK BEHAVIOR

Left trigger cannot start recording, send STT control-start, or emit audio before READY. It displays `NO ACTIVE SESSION`, logs `PTT_BLOCKED_NO_SESSION`, and remains blocked until trigger release.

## PROCESSING TIMEOUT

`StudyConfiguration.processingResponseTimeoutSeconds` defaults to 10 seconds. The participant UI changes to `NO SERVER RESPONSE — CHECK SESSION / CONNECTION`, then returns to normal error/ready handling; it sends no protocol message.

## TIMEOUT RESOLUTION EVENTS

Only `PredefinedCommandProposal`, `PredefinedCommandRejected`, `AuthoringProposal`, `AuthoringRejected`, and `AuthoringStatus` resolve a current processing state. End, reset, restart, status mismatch, disable, and pause cancel it.

## RESEARCHER PANEL INPUT

Hold LEFT Y for approximately one second. Runtime input uses `UnityEngine.XR.InputDevices` with left controller characteristics and `CommonUsages.secondaryButton`. A hold toggles once; release re-arms it. F5 remains desktop fallback.

## Y-BUTTON CONFLICT SEARCH

Active DreamCodeVR2 runtime source had no prior `secondaryButton`/Y binding. The old panel-only simultaneous left/right menu-or-primary logic was removed. Existing `PrimaryButtonState` uses outside this panel were left untouched.

## LOGGING EVENTS

`PTT_BLOCKED_NO_SESSION`, `PROCESSING_STARTED`, `PROCESSING_RESOLVED`, `PROCESSING_TIMEOUT`, `RESEARCHER_PANEL_TOGGLE_HOLD_START`, `RESEARCHER_PANEL_OPENED`, and `RESEARCHER_PANEL_CLOSED` are recorded by the existing client logger.

## REGRESSION CHECK

The Ubiq, researcher routes, NetworkIds 98–102, STT payload/control format, condition semantics, and C3 path were not changed.

## STATIC MUST FIX

0 known static blockers. No Unity batch build was run.

## MANUAL QUEST VERIFICATION

TEST A — no session: launch, do not START, press left trigger; confirm no STT start/audio, visible `NO ACTIVE SESSION`, and `PTT_BLOCKED_NO_SESSION`.

TEST B — panel: hold LEFT Y ~1 s to open once; keep holding with no repeat; release and hold to close.

TEST C — C1: select C1, START, wait for READY, close panel, point at drawer, say `open this drawer`; processing starts and a proposal/rejection resolves it.

TEST D — timeout: safely make the server response unavailable after recording; confirm processing exits after the configured timeout and no fake ACK is sent.

TEST E — panel/PTT: with panel open, trigger clicks UI and emits no STT start; LEFT Y toggles independently.
