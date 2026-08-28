# Unity researcher panel XR fix audit

## ROOT CAUSE FOUND

The installed Ubiq `XRUIRaycaster` does not use a Unity input module for controller clicks: it sends pointer events directly. Its prior press handling dispatched pointer-down every frame while trigger was held, making button interaction unreliable and opaque to diagnostics.

## EVENTSYSTEM CONFIGURATION

Exactly one `EventSystem` is retained. The runtime-created one has no `StandaloneInputModule`: Ubiq requires `EventSystem.current` for `PointerEventData`, but sends pointer events itself.

## XRUIRAYCASTER CONFIGURATION

`XRUIRaycaster` remains on a child ray GameObject of each `HandController`, as required by its `GetComponentInParent<HandController>()` implementation. The researcher Canvas has both `GraphicRaycaster` and `XRUICanvas`.

## CLICK INPUT PIPELINE

HandController `TriggerState` drives one pointer-down edge and one pointer-up/click edge through Ubiq `XRUIRaycaster`. Panel buttons use standard `Button` components and listeners created after each button is instantiated.

## OVERLAPPING UI HANDLING

Opening the researcher panel temporarily sets the participant authoring UI CanvasGroup to non-interactable and non-raycast-blocking. Closing restores its prior values; gameplay UI stays visible.

## BUTTON LISTENERS

START, END, RESET, C1 VOICE, C2 AUTHOR, C3 STORY, MARK TEST and Advanced controls are generated with valid `Button.onClick` listeners. Every button logs `RESEARCHER_BUTTON_CLICK` before invoking its action.

## SIMPLIFIED PANEL

The default view shows only session controls, condition controls, concise READY/connection/API/logging status, MARK TEST, and ADVANCED. Buttons use larger 44–52 pixel world-space rows.

## ADVANCED VIEW

Advanced is collapsed by default and contains IDs, task/selection/debug state, context refresh, object selection, local test injection and recent notes.

## LOGGING

Raycaster state transitions inside the panel log `XR_UI_HOVER_ENTER`, `XR_UI_HOVER_EXIT`, `XR_UI_POINTER_DOWN`, `XR_UI_POINTER_UP`, and `XR_UI_CLICK`. Missing raycasters log `RESEARCHER_UI_INPUT_ERROR`.

## DEPLOYMENT DOC FIX

Active researcher documentation now uses Ubiq `130.136.2.161:50000`, Researcher API `http://130.136.2.161:50001`, and server-side STT `130.136.2.161:50101`.

## STATIC MUST FIX

0 known static blockers. Runtime interaction remains a manual Quest verification item.

## MANUAL QUEST VERIFICATION

TEST 1: hold LEFT Y ~1 s and verify one panel open. TEST 2: point at C1 and verify hover/`XR_UI_HOVER_ENTER`. TEST 3: trigger C1 and verify selection plus `RESEARCHER_BUTTON_CLICK`. TEST 4: trigger START and wait for SESSION READY. TEST 5: close panel, use PTT on drawer and verify proposal/rejection. TEST 6: reopen panel, click controls and verify no NID98 STT start/control packets.
