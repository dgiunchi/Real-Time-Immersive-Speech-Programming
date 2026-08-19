# Unity pre-build integration audit — current source

Status terms are literal: **implemented** means source wiring exists; **statically verified** means inspected without running Unity; **manual verification required** means it still needs a device/editor test. Unity was deliberately not launched for this audit.

## Runtime ownership

The only enabled study scene is `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`. Its retained services remain on `DreamCodeVR2_RuntimeServices`; `VerticalSliceRuntimeBootstrap` creates/fetches the experimental services on `ExperimentalAuthoringRuntime`. There are only C1 VoiceCommandBaseline, C2 PlayerAuthoring and C3 DynamicStorytelling. Network IDs are 98 STT, 99 InteractionContext, 100 SceneContext, 101 inbound experimental traffic and 102 outbound results/events. Network ID 94 and Roslyn/code-generation paths are statically absent.

## Researcher session lifecycle

`StudyConfiguration.researcherControlBaseUrl` supplies the control API URL; the fallback is `http://127.0.0.1:3004`. Quest must receive `http://PC_LAN_IP:3004`, never localhost. The client calls GET health/status and POST start/end/restart/reset beneath `/api/authoring/dev`, always with the peer UUID.

Selecting C1/C2/C3 changes only `selectedCondition`. Pressing START during an active session uses server restart with the selected value, then performs the local reset and starts the new condition only after a valid server session ID. END and RESET reset Unity only after the matching server success response. This active-session switch/restart flow is now implemented; server E2E remains manual verification.

## Canonical Ubiq 101/102 protocol

101 accepts `AuthoringProposal`, `AuthoringExecutionRequest`, `AuthoringUndoRequest`, `PredefinedCommandProposal`, `PredefinedCommandExecutionRequest`, `NextTaskGenerated`, `NextTaskActivationRequest`, rejections and status. The 36-byte peer UUID prefix is retained on 102; JSON DTOs sent by Unity are flat: `AuthoringAck`, `PredefinedCommandAck`, `ExperimentStateEvent` and `NextTaskAck`.

Authoring proposals do not emit an execution ACK when merely displayed, confirmed, rejected or modified. `AuthoringAck` is emitted only after Unity executes/undoes an authoring action. Experiment events are filtered to the canonical values `task_started`, `task_completed`, `incorrect_attempt`, `hint_requested` and `session_completed`. C3 sends `task_completed` before awaiting a generated/activated next task. `NextTaskGenerated` is stored separately from `NextTaskActivationRequest`; activation validates the task before showing it to the player.

Static limitation: the server contract represents `success_conditions` as strings while the current Unity runtime validator uses structured `RuntimeSuccessCondition` objects. A real C3 server message must be inspected/tested before treating this mapping as server E2E complete.

## XR researcher interaction

The bootstrap now ensures exactly one EventSystem (with StandaloneInputModule) and adds an `XRUIRaycaster` child to each discovered Ubiq `HandController` that has none. The panel uses a world-space Canvas with `GraphicRaycaster` and `XRUICanvas`. It is researcher/debug gated; F5 is desktop fallback, while Quest uses a one-second simultaneous left/right menu-or-primary hold.

While the panel is visible, `ResearcherUiInteractionState` reserves the trigger for researcher UI and forces PTT gain to zero in `SelectObjectRay`; it does not alter the grip/select mechanism. The panel now caches left/right `HandController` and `DynamicStoryTaskController` references rather than searching in update/refresh hot paths.

Manual verification required: EventSystem uniqueness at runtime, panel ray hover/click, gesture detection, PTT suppression/release, and interaction with the installed Ubiq XR package.

## LAN and Android findings

`RoomJoiner.cs` only obtains a `RoomClient`, waits for an existing `NetworkScene` connection, then joins its configured room GUID. Static search found no DreamCodeVR2-owned RoomClient/NetworkScene endpoint, host or port configuration, and no custom Android manifest/network-security template. Therefore no host/port value was invented in source.

Manual Ubiq procedure: in Unity select the scene object that owns `NetworkScene`/its connection component (under `DreamCodeVR2_RuntimeServices`), set the existing connection endpoint to the PC LAN address and the vendor/server port, and save the scene/prefab. Do not change the RoomJoiner GUID unless the target room changes. If the endpoint field is unavailable, it is vendor/package-owned and must be configured in that package's normal connection asset or launcher.

For HTTP `http://PC_LAN_IP:3004` on Android, confirm Player Settings or the generated Android manifest permits cleartext traffic; no project-owned manifest was found to verify it statically.

## Required manual checks before release

1. In Play Mode, check Console for compile/runtime errors, one EventSystem, one raycaster per hand and panel F5/Quest gesture behavior.
2. On Quest, confirm both controller rays can press panel buttons and trigger does not open PTT while panel is visible.
3. Test C1, C2 and C3 start/restart/end/reset against the live `/api/authoring/dev` server.
4. Capture a 101/102 exchange for each canonical message, especially C3 generated/activation payloads.
5. Configure the vendor Ubiq endpoint to the PC LAN address, then verify peer UUID and room join on Quest.
6. Confirm Android cleartext/LAN networking and firewall rules for port 3004 and the Ubiq server port.

No Unity build, batch mode or Editor test was run in this pass by request.
