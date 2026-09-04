# Drawer-discovery success-condition fix

## 1. Exact previous unsupported path

The newest real Quest run `client_20260902T185453Z_run.jsonl` received and parsed:

```text
NextTaskGenerated(set_a:T2)
drawer_discovery:cabinet_drawer_003:sphere_001
```

`NextTaskWireConverter.TryConvertCondition` had no `drawer_discovery` case. It returned false, producing:

```text
FIXED_QUEST_WIRE_CONVERSION_FAILED
Unsupported success condition: drawer_discovery:cabinet_drawer_003:sphere_001
```

T2 was therefore never pending when the subsequent activation request arrived.

## 2. Converter patched

`NextTaskWireConverter` in `Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocol.cs` now recognizes the canonical `drawer_discovery` vocabulary. `FixedQuestWireConverter` continues to use that same converter, so no server wire format, task ID, or progression path changed.

## 3. Runtime condition representation

The converted `RuntimeSuccessCondition` is:

```text
type         = DRAWER_DISCOVERY
container_id = <canonical drawer ID>
object_id    = <canonical discovered-object ID>
```

Both object references undergo the existing canonical normalization only; no Set-A-specific mapping is used.

## 4. Supported syntax

```text
drawer_discovery:<container_id>:<object_id>
```

Example:

```text
drawer_discovery:cabinet_drawer_003:sphere_001
```

Malformed inputs are rejected with a specific conversion error. The parser does not infer an omitted container or object.

## 5. Canonical tasks covered

The generic conversion supports all supplied canonical tasks:

| Task | Container | Object |
|---|---|---|
| A-T2 | `cabinet_drawer_003` | `sphere_001` |
| A-T4 | `table_drawer_001` | `key_001` |
| B-T2 | `cabinet_drawer_001` | `key_002` |
| B-T4 | `cabinet_drawer_003` | `key_001` |
| C-T3 | `table_drawer_001` | `clue_note_002` |
| C-T4 | `cabinet_drawer_001` | `sphere_001` |

## 6. Discovery strictness preserved

`DRAWER_DISCOVERY` is not satisfied by active state, visibility, or an already-open drawer.

The existing `ExperimentalDrawerController` now reports a drawer opening to the world-state reporter only for an actual state change from closed to open. `QuestWorldStateReporter` is the sole discovery-evidence source: it records an object only when that qualified opening is associated with the expected drawer. `RuntimeTaskValidator` requires a matching drawer ID, object ID, and `CLOSED_TO_OPEN` transition.

## 7. Generation handling

Discovery evidence includes the object’s current availability generation. Evaluation compares the recorded generation with the reporter’s current generation. A later availability update invalidates older evidence, so a stale generation cannot complete a discovery task. Reset clears generation, open-transition, and discovery evidence.

## 8. Malformed input behavior

The following now fail safely and precisely:

```text
drawer_discovery
drawer_discovery:drawer_only
drawer_discovery::sphere_001
drawer_discovery:cabinet_drawer_003:
```

## 9. T2 registration result

For `drawer_discovery:cabinet_drawer_003:sphere_001`, conversion now yields a valid `DRAWER_DISCOVERY` condition. Therefore `NextTaskGenerated(set_a:T2)` can populate the pending fixed task instead of logging `FIXED_QUEST_WIRE_CONVERSION_FAILED`.

## 10. T2 activation result

With the pending task present, the unchanged `NextTaskActivationRequest(set_a:T2)` path can call `ActivateServerTask`, set `set_a:T2` as the current task, and refresh the participant UI to “Find the sphere.” It still does not create a successor task locally; post-T2 progression remains server-driven.

## 11. Tests

Added editor tests cover:

- all six canonical drawer-discovery strings;
- preservation of `container_id` and `object_id`;
- malformed input rejection;
- wrong drawer rejection;
- object existence/visibility without qualified opening rejection;
- matching qualified opening acceptance;
- stale generation rejection;
- conversion followed by server-task activation into `set_a:T2`.

The workspace has no checked-in `.sln` or `.csproj`, so these NUnit editor tests must be run by Unity Test Runner after Unity recompiles the scripts.

## 12. Remaining known mismatch

No remaining wire-conversion mismatch is known for T2. A Quest build/retest is required to verify the emitted device diagnostics:

```text
FIXED_QUEST_DISCOVERY_CONDITION_PARSED
DRAWER_DISCOVERY_EVALUATION
```

The expected device flow is `NextTaskGenerated(set_a:T2)` → conversion success → pending registration → `NextTaskActivationRequest(set_a:T2)` → activation success → “Find the sphere.”
