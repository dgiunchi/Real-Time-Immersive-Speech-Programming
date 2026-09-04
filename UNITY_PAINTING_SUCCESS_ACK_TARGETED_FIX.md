# Unity Painting Success ACK Targeted Fix

## Root Cause

The local painting execution path succeeded physically, but `AuthoringProtocolClient` did not treat that success as terminal for the `command_id`.

That meant a later stale `PredefinedCommandRejected` or failed `PredefinedCommandAck` with the same `command_id` could still overwrite participant feedback with an unsupported-operation style message, even though `QuestPaintingController.TryAlign(...)` had already succeeded.

## Physical Execution

`QuestPaintingController.TryAlign(...)` succeeds physically by moving `painting_001` to `alignedAnchor` and returning `true`.

## Why Failure Feedback Was Emitted

The execution/ACK path had no command-level guard for:

- duplicate `PredefinedCommandExecutionRequest` replays after local success;
- stale server-side failure messages arriving after local success for the same `command_id`.

As a result, participant feedback could still show a late failure even though the local execution result was already successful.

## Exact Patch

Patched:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocolClient.cs`

Added:

- `terminalSuccessfulPredefinedCommandIds`

Behavior:

- mark a predefined `command_id` terminal when local execution succeeds;
- ignore duplicate execution requests for the same already-successful `command_id`;
- suppress late `PredefinedCommandRejected` and failed `PredefinedCommandAck` feedback for the same already-successful `command_id`.

No parser, resolver, target capability rules, or `MOVE_TO_PRESET` execution semantics were weakened.

## Resulting ACK

For a successful `MOVE_TO_PRESET` on `painting_001`, the client now keeps the single successful local result and sends only the success ACK for that `command_id`.

## Duplicate-Response Status

Duplicate execution requests for an already-successful predefined command are now ignored.

Late failure feedback for the same already-successful `command_id` is now suppressed instead of replacing the success state.

## Tests

Added targeted edit-mode tests in:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/Tests/Editor/ExperimentalRuntimeEditModeTests.cs`

Coverage:

- successful painting execution marks the `command_id` terminal;
- duplicate execution with the same `command_id` is ignored;
- late failure suppression applies only to the same already-successful `command_id`.
