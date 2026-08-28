# Ubiq UI Ray Fix Audit

## ACTUAL UI RAY

The project uses the installed Ubiq chain `HandController -> UI Ray -> XRUIRaycaster -> XRUIRaycasterCursor/XRUIRaycasterLine -> XRUICanvas -> GraphicRaycaster -> Button`. The existing `UI Ray` is retained; no second UI input or click system has been added. Opening the researcher panel now discovers installed Ubiq raycasters (including inactive scene objects), enables the existing ray GameObject and raycaster, enables available cursor/line components, and records every changed state for restoration on close.

The installed hand prefab contained a cursor but no `XRUIRaycasterLine`, making the UI ray invisible until it had already hit a canvas. Researcher mode now temporarily adds the installed Ubiq line component to an existing UI Ray when necessary. It is cyan, remains visible for two metres on a miss, reaches the UI target on a hit, and is removed on close. This does not add a second raycaster or a separate rendering/input system.

## GAMEPLAY RAY SUPPRESSION

`SelectObjectRay` is now reversibly suppressed while the panel is open. Its line renderer is hidden and its gameplay selection update stops; it is not destroyed or permanently disabled. The previous suppression state is restored when the panel closes or is destroyed. This leaves the Ubiq UI ray as the only researcher-facing pointer.

## XRUICANVAS REGISTRATION

The dynamically-built researcher canvas already has `Canvas`, `GraphicRaycaster`, and `XRUICanvas`. Its root `RectTransform` is explicitly 800×1400 rather than Unity's 100×100 default: the installed `XRUIRaycaster` first intersects this root rectangle before `GraphicRaycaster` resolves a child button. Its `GraphicRaycaster.ignoreReversedGraphics` is disabled only for this readable researcher canvas, so an installed Ubiq controller ray is not rejected before it can reach a button. At panel open the code checks that the canvas is present in `XRUICanvas.Canvases`, which is the installed Ubiq registration list populated through normal component lifecycle. A failed check logs `RESEARCHER_UI_INPUT_ERROR`; no vendor-private collection is modified.

## EVENTSYSTEM

The existing bootstrap still creates one `EventSystem` only when none exists. No `StandaloneInputModule` or `InputSystemUIInputModule` was added. Opening the panel verifies exactly one scene EventSystem and a non-null `EventSystem.current`, logging `RESEARCHER_UI_INPUT_ERROR` otherwise.

## CAMERA

On open, the panel checks `Camera.main`, assigns it to `Canvas.worldCamera`, and repositions the canvas using the project's existing readable world-space UI convention. The panel logs either `RESEARCHER_UI_CAMERA_OK` or `RESEARCHER_UI_CAMERA_ERROR`.

## TRIGGER STATE

The installed Ubiq `HandController` obtains `TriggerState` from `UnityEngine.XR.CommonUsages.triggerButton`. The diagnostics now log only state transitions while the researcher panel is open: `XR_UI_TRIGGER_DOWN` and `XR_UI_TRIGGER_UP`, with hand and current target. PTT/session behavior and the LEFT-Y hold toggle were not changed.

## POINTER EVENTS

The installed `XRUIRaycaster` remains the sole dispatcher for Unity pointer events. It retains the current target for 0.12 seconds during a momentary Quest controller-tracking miss, preventing an `XR_UI_HOVER_EXIT` between trigger-down and trigger-up from canceling a valid button click. Diagnostics attach to each installed raycaster, including ones activated at panel open, and log `XR_UI_HOVER_ENTER`, `XR_UI_HOVER_EXIT`, `XR_UI_POINTER_DOWN`, `XR_UI_POINTER_UP`, and `XR_UI_CLICK` for researcher-panel descendants. Entries include hand, target, and trigger state.

## BUTTON DISPATCH

Existing button listeners remain in place. Each listener retains `RESEARCHER_BUTTON_CLICK`; it additionally marks the dispatch for diagnostics. If `XR_UI_CLICK` reaches a `ResearcherButton_*` but its `Button.onClick` has not run, the panel logs `RESEARCHER_UI_BUTTON_DISPATCH_ERROR` with the exact target hierarchy.

## LEGACY UI

Participant authoring UI raycasts remain disabled during researcher mode and restored afterward. If an active legacy Ubiq `Canvas` is detected while the researcher panel is open, its `GraphicRaycaster` is temporarily disabled and `LEGACY_UI_RAYCAST_DISABLED` is logged. Its original state is restored on close. No legacy menu is deleted.

## LOGGING

The Advanced section now has a small, researcher-only diagnostic refreshed at 0.15-second intervals:

`UI TARGET: <ResearcherButton name / NONE>`

`TRIGGER: UP / DOWN`

It reads the actual `XRUIRaycaster.CurrentTarget` and the owning `HandController.TriggerState`.

## STATIC MUST FIX

No remaining source-level blocker was found after this change. One environmental limitation remains: the local machine has no .NET SDK, so `dotnet build Unity/Assembly-CSharp.csproj --no-restore` could not run. Unity must compile the scripts in the editor before deployment; Unity batch mode was not run.

## QUEST TEST

### TEST 1

Open the panel with LEFT Y.

Expected: gameplay `SelectObjectRay` disappears; only the Ubiq UI ray/cursor remains visible.

### TEST 2

Point the UI ray at C1.

Expected: `UI TARGET: ResearcherButton_C1 VOICE`; `XR_UI_HOVER_ENTER` is logged.

### TEST 3

Press the trigger while pointing at C1.

Expected: `TRIGGER: DOWN`; `XR_UI_TRIGGER_DOWN`; `XR_UI_POINTER_DOWN`; on release `XR_UI_TRIGGER_UP`, `XR_UI_POINTER_UP`, `XR_UI_CLICK`, `RESEARCHER_BUTTON_CLICK`; C1 becomes selected.

### TEST 4

Point at START and click.

Expected: `RESEARCHER_BUTTON_CLICK START`; researcher HTTP request; `SESSION READY`.

### TEST 5

Close the panel.

Expected: Ubiq researcher UI ray state restores; gameplay ray restores; participant PTT is available only if the session is ready.
