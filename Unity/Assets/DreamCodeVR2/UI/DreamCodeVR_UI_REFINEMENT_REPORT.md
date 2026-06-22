# DreamCodeVR UI Refinement Report

## Files Modified
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIBootstrap.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIController.cs`

## Layout Changes
- Replaced the large stacked dashboard with a compact always-visible card plus progressive-disclosure cards.
- Switched to smaller card widths, tighter typography, and content-sized vertical layouts to avoid text overlap.
- Added separate cards for compact status, inspect, speech, plan preview, and feedback.

## UI Modes Implemented
- `Compact Mode`: always visible with title, pointed object, selected object, and compact speech summary.
- `Inspect Mode`: shown only when an object is pointed or selected.
- `Speech Mode`: shown only when a transcript is received, then auto-hides.
- `Plan Preview Mode`: hidden by default and shown only when `SetPlanPreview(...)` has steps.
- `Feedback Mode`: hidden by default and shown only for status/undo feedback.
- `debugAlwaysShowAllPanels`: forces all cards visible for UI debugging.

## Positioning Strategy
- The UI now lives slightly to the right of the player view instead of covering the center.
- The root follows the player head/camera with configurable:
- `distanceFromCamera`
- `horizontalOffset`
- `verticalOffset`
- `uiScale`
- `followSmoothing`
- Default placement targets a comfortable reading distance around chest-to-eye height.

## Auto-Hide Behavior
- Inspect card remains visible while an object is pointed or selected.
- Speech card appears when transcript text changes and hides after `speechCardHideDelay`.
- Plan card remains hidden unless plan steps exist.
- Feedback card appears on status/undo updates and hides after `feedbackHideDelay` unless undo is available.

## Remaining Limitations
- Rounded corners are approximated with cleaner compact cards and borders; no custom rounded sprite asset is used.
- Compact speech state still reflects local transcript availability rather than a richer microphone lifecycle.
- The UI is still runtime-generated rather than authored as a prefab in-scene.
- This refinement was not runtime-tested inside the Unity Editor from here.

## Manual Test Checklist
- scene still starts
- Ubiq room still joins
- server still logs peer joined
- SceneContext still sends objects
- ContextBridge still sends pointed_object
- compact panel stays visible without blocking the center view
- inspect card appears for painting, drawer, lock, and basket
- long labels/descriptions no longer overlap
- speech card appears when transcript arrives and hides afterward
- plan card stays hidden until `SetPlanPreview(...)` is used
- feedback card stays hidden until status or undo data is set
