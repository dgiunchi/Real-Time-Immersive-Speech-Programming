# Unity researcher panel interaction audit

## CURRENT PANEL HIERARCHY

The runtime panel is created by `DreamCodeVR2.ExperimentalAuthoring.ExperimentalResearcherPanel.Build` in `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/ExperimentalResearcherPanel.cs`.

- Canvas GameObject: `ExperimentalResearcherPanel`.
- Components added at runtime: `Canvas`, `CanvasScaler`, `GraphicRaycaster`, `Ubiq.XR.XRUICanvas`, and `ResearcherPanelXrDiagnostics`.
- Render mode: `WorldSpace`; `worldCamera = Camera.main`; `sortingOrder = 100`; local scale `0.0015`.
- Position on open: HMD position plus forward `1.15 m`, left `0.42 m`, down `0.04 m`; it is not parented to the HMD.
- Layer: Unity default layer `0` (new runtime objects do not change layer).
- Panel child: `Panel`, an `Image` with `raycastTarget = false`; it has a `VerticalLayoutGroup`, but no `CanvasGroup`.
- Canvas and `GraphicRaycaster` are enabled by construction. There is no panel-local CanvasGroup to make the buttons non-interactable.

## BUTTON WIRING

All buttons are created dynamically by `ExperimentalResearcherPanel.AddButtons`, after `Button` is added to `ResearcherButton_<id>`.

| Button | Runtime GameObject | Button/Image/Text | Runtime listener callback |
|---|---|---|---|
| START | `ResearcherButton_START` | Button yes; Image raycast target default **true**; TMP label false; interactable default **true** | log `RESEARCHER_BUTTON_CLICK`, then `StartServerSession` |
| END | `ResearcherButton_END` | same | log, then `EndServerSession` |
| RESET | `ResearcherButton_RESET` | same | log, then `ResetCurrentRun` |
| C1 | `ResearcherButton_C1 VOICE` | same | log, then `Switch(VoiceCommandBaseline)` |
| C2 | `ResearcherButton_C2 AUTHOR` | same | log, then `Switch(PlayerAuthoring)` |
| C3 | `ResearcherButton_C3 STORY` | same | log, then `Switch(DynamicStorytelling)` |
| MARK TEST | `ResearcherButton_MARK TEST` | same | log, then `DreamCodeVR2ClientLogger.MarkTest` |

Each listener is a runtime `Button.onClick.AddListener` lambda. The panel background cannot win the button raycast because it explicitly has `raycastTarget = false`; text labels are also explicitly non-raycast targets. No static scene button is involved.

## EVENTSYSTEM

The active test scene has no serialized EventSystem found by static search. `VerticalSliceRuntimeBootstrap.EnsureEventSystem` creates exactly one `DreamCodeVR2_EventSystem` only if `FindFirstObjectByType<EventSystem>()` finds none. The current bootstrap adds **EventSystem only**, without `StandaloneInputModule`.

`StandaloneInputModule` is not active from this bootstrap. Static search found no `InputSystemUIInputModule` and no Ubiq-specific input module. This matches the installed Ubiq implementation: its `XRUIRaycaster` explicitly operates outside Unity input modules and uses `EventSystem.current` only when constructing `PointerEventData`.

## XRUIRAYCASTER

The installed `Hand Controller.prefab` has a child named `UI Ray`. That child has `Ubiq.XR.XRUIRaycaster` and `XRUIRaycasterCursor`; it is a child of the `Hand Controller` object, not a component on the controller itself. This is the package’s intended hierarchy: `XRUIRaycaster.Awake` calls `GetComponentInParent<HandController>()`.

`VerticalSliceRuntimeBootstrap.EnsureXrUiRaycasters` only creates a child `DreamCodeVR2_XRUIRaycaster` for a hand that does not already have an `XRUIRaycaster` below it. With the installed player/hand prefab, the existing `UI Ray` satisfies that test, so the intended prefab raycaster is retained for each hand.

Current added runtime fields in the raycaster are `ignorePhysicsOcclusion`, pointer transition events, and `CurrentTarget`. On researcher-panel open, `SetResearcherRayPriority(true)` sets `ignorePhysicsOcclusion` on every discovered Ubiq UI raycaster; it is reset on close/destroy.

## UBIQ XR UI PIPELINE

Installed package source:

`HandController` updates `TriggerState` from Unity XR `CommonUsages.triggerButton`. `XRUIRaycaster`:

1. casts a ray from its own child `UI Ray` transform;
2. iterates `XRUICanvas.Canvases`;
3. intersects each world-space Canvas rect;
4. invokes that Canvas’s `GraphicRaycaster.Raycast` using `PointerEventData(EventSystem.current)`;
5. directly dispatches `pointerEnter`, `pointerExit`, `pointerDown`, `pointerUp`, and `pointerClick` through `ExecuteEvents.ExecuteHierarchy`.

`XRUIRaycasterLine` / `XRUIRaycasterCursor` are the package visual UI-ray components. They depend on the UI raycaster’s hit/miss events.

## CONTROLLER CLICK INPUT

The installed Ubiq UI click source is **`HandController.TriggerState`**, not a standalone Unity input module, InputAction, or the participant microphone component. `HandController.Update` reads `CommonUsages.triggerButton` from left/right `InputDevice`s filtered by Controller, HeldInHand, and hand side.

The current panel does not replace this input path. It reserves participant PTT while open via `ResearcherUiInteractionState`, but relies on the same Ubiq `TriggerState` for UI click.

## POINTER EVENTS

Pointer events are produced by current source. `XRUIRaycaster.PerformRaycast` dispatches enter/exit; `CheckInput` dispatches down on a trigger press edge and up/click on release over the same target. The current code therefore has an actual Unity UI pointer-event producer.

Static limitation: source cannot prove that the Quest runtime’s `UI Ray` obtains a valid panel target or that `HandController.TriggerState` changes when the physical trigger is pressed.

## RAYCAST BLOCKERS

Inside the runtime panel:

- full-panel `Panel` Image: explicitly not raycastable;
- labels: explicitly not raycastable;
- button Images: raycastable and intended targets;
- no transparent full-panel overlay, debug overlay, or panel CanvasGroup exists.

At a button position, the only panel-local expected Graphic target is the button Image. Static source does not identify a panel-local Graphic that would supersede it.

## OVERLAPPING CANVASES

The participant `DreamCodeVRAuthoringUIBootstrap` creates a separate world-space Canvas, but it does **not** add `XRUICanvas`; Ubiq’s `XRUIRaycaster` therefore does not iterate it. When the researcher panel opens, `ExperimentalResearcherPanel.SetParticipantUiRaycasts(false)` adds/uses a CanvasGroup on the participant UI root and sets both `interactable` and `blocksRaycasts` false; it restores saved values on close.

The test scene does include an old Ubiq `Canvas` with `XRUICanvas`, `GraphicRaycaster`, scale `0.003`, sorting order `0`, and a `Menu` ancestor. `DreamCodeVRAuthoringUIBootstrap.HideLegacyMenuUi` reparents/turns off `Menu`, `Menu Panel`, `Join Room Panel`, `Join Room`, and related old UI at startup. An inactive Canvas is skipped by `XRUICanvas.Canvases`.

Therefore the participant authoring Canvas is not a confirmed Ubiq UI blocker. The hidden legacy Ubiq Canvas is only a runtime uncertainty if a later script re-enables its `Menu` ancestor.

## LAYERS / CAMERA

The runtime researcher Canvas, panel, and buttons default to layer `0`. The `GraphicRaycaster` is dynamically added with Unity defaults; no custom layer mask is assigned. Canvas `worldCamera` is set to `Camera.main`; the installed player prefab provides a `Main Camera` tagged `MainCamera`.

The `GraphicRaycaster` is configured by Unity defaults, including reversed-graphic filtering. The Canvas faces using `Quaternion.LookRotation(canvasPosition - cameraPosition)`, matching the existing project’s world-space UI convention. Static source cannot verify that the active Quest camera is the same object returned by `Camera.main` at panel construction time.

## VISIBLE RAY TYPE

The visible ray reported by the user is **not confirmed UI-capable**. The project separately creates `SelectObjectRay` (and legacy `SelectRay`) with a `LineRenderer`. `SelectObjectRay.Update` always renders a straight 8 m physics ray and uses `Physics.RaycastAll` only against gameplay objects; it does not create `PointerEventData` or Unity UI events.

By contrast, the Ubiq `UI Ray` line/cursor is driven only by Ubiq UI-ray hit/miss events. A continuously visible gameplay selection ray crossing the panel is therefore insufficient evidence that the `UI Ray` has reached the panel.

## RECENT RUNTIME MODIFICATIONS

- `VerticalSliceRuntimeBootstrap.EnsureEventSystem`: creates EventSystem if missing.
- `VerticalSliceRuntimeBootstrap.EnsureXrUiRaycasters`: ensures a child Ubiq UI raycaster per hand only when absent.
- `ExperimentalResearcherPanel.Build`: creates Canvas, GraphicRaycaster, XRUICanvas, panel graphics, dynamic Buttons, and diagnostics.
- `ExperimentalResearcherPanel.SetParticipantUiRaycasts`: changes participant CanvasGroup interaction while panel is open.
- `ExperimentalResearcherPanel.SetResearcherRayPriority`: changes `XRUIRaycaster.ignorePhysicsOcclusion` while panel is open.
- `XRUIRaycaster`: was modified to expose pointer events, dispatch trigger edges, and optionally ignore physics occlusion.

## LOGGING COVERAGE

`ResearcherPanelXrDiagnostics.Start` subscribes every discovered `XRUIRaycaster` to `PointerHoverEnter`, `PointerHoverExit`, `PointerDown`, `PointerUp`, and `PointerClick`, filtering targets to panel descendants. These runtime subscriptions emit `XR_UI_HOVER_ENTER`, `XR_UI_HOVER_EXIT`, `XR_UI_POINTER_DOWN`, `XR_UI_POINTER_UP`, and `XR_UI_CLICK` when the Ubiq UI raycaster reaches a panel target.

`RESEARCHER_BUTTON_CLICK` is emitted in every generated button listener. `RESEARCHER_UI_INPUT_ERROR` is emitted only if no `XRUIRaycaster` exists when diagnostics starts. The calls are wired, but no retained Quest log was available for this audit, so no event occurrence is proven.

## ROOT-CAUSE RANKING

1. **HIGH — the observed visible ray is likely `SelectObjectRay`, a physics/gameplay ray rather than the separate Ubiq `UI Ray`; therefore visible-ray observation does not establish hover, target acquisition, or trigger pointer events for the researcher Canvas.**
2. **MEDIUM — the Ubiq UI raycaster can produce no target if its ray transform, parent HandController input state, Canvas-facing/camera state, or XRUICanvas registration is invalid at runtime. Static source cannot distinguish these cases.**
3. **MEDIUM — the legacy Ubiq XRUICanvas can interfere only if the disabled legacy Menu becomes active later. The normal participant authoring UI is not registered as XRUICanvas and is additionally CanvasGroup-disabled on panel open.**
4. **LOW — missing Button/listener wiring. Current runtime construction creates Button, target Image, and one listener for every required control.**
5. **LOW — wrong Unity input module. The installed Ubiq raycaster intentionally bypasses input modules; a Standalone/InputSystem module is not its click mechanism.**

## MINIMAL FIX PATH

Do not add a parallel click system or an XR Interaction Toolkit module. First instrument and visually distinguish the existing Ubiq `UI Ray` (not `SelectObjectRay`) and verify its target transitions while the panel is open. The smallest correct fix path is then based on evidence:

- if no `XR_UI_HOVER_ENTER` occurs, correct/enable the existing prefab `UI Ray` transform/cursor and validate the runtime XRUICanvas/camera registration;
- if hover occurs but no down/up, inspect live `HandController.TriggerState` on the same hand/ray object;
- if click occurs but no `RESEARCHER_BUTTON_CLICK`, inspect the Button target returned by `GraphicRaycaster` and the direct ExecuteEvents hierarchy.

This preserves the installed Ubiq mechanism and avoids another independent controller-click implementation.

## STATIC UNCERTAINTIES

Static inspection cannot observe the actual Quest hierarchy after bootstrap, whether the user is looking at gameplay versus UI ray, current `Camera.main`, dynamic active state of the legacy menu Canvas, `HandController.TriggerState`, or the events written on device. These require a focused Quest test with the existing XR UI event logs visible/retrievable.
