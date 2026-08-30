# Unity key-lock binding runtime audit

## ROOT CAUSE

The device payload for `set_a_instance_1` specifies `key_001 + lock_drawer_001`. Unity previously normalized `lock_drawer_001` to `lock_001`, but `lock_001` is the exit-door lock. The canonical desk-drawer lock is `lock_002`. Consequently the intended `lock_002` controller had no active required key and rejected every key. The same wrong normalization left the `LOCK_UNLOCKED` success condition on the server alias.

## QUESTINSTANCE BINDING PATH

`NextTaskGenerated.quest_instance.key_lock_bindings` deserializes to `ServerQuestBindingDto`, converts to `QuestLockBinding`, canonicalizes legacy drawer-lock aliases, then reaches `QuestInstanceController.Apply`. `Apply` clears prior bindings, resolves the canonical `AIEditableObject`, configures its `QuestLockController`, applies state-only initial states, and emits final binding summary. `TryUseKey` compares only canonical `key_###` strings.

## CURRENT INSTANCE BINDINGS

The captured real A1 payload contains `lock_drawer_001 -> key_001` and `lock_001 -> key_002`. Its runtime conversion is `lock_002 -> key_001` and `lock_001 -> key_002`.

| QuestInstance | Lock | Expected required key | Runtime required key | Status |
|---|---|---|---|---|
| A1 / set_a_instance_1 | lock_002 | key_001 | key_001 after fix | corrected from wrong lock_001 mapping |
| A1 / set_a_instance_1 | lock_001 | key_002 | key_002 | direct canonical binding |
| A2 / fallback | lock_002 | key_001 | key_001 | canonical fallback |
| B1 / fallback | lock_003 | key_001 | key_001 | canonical fallback |
| C1 / fallback | lock_002 | key_002 | key_002 | awaiting server-payload device confirmation |

The A2/B1/C1 entries are local fallback declarations, not observed server payloads; runtime `QUEST_LOCK_BINDING_SUMMARY` is the authoritative device confirmation.

## RUNTIME LOCK CONFIGURATION

`lock_001`, `lock_002` and `lock_003` are canonical scene objects with `QuestLockController` registration created during bootstrap. Instance application now logs `QUEST_LOCK_BINDING_APPLIED` for each requested binding and `QUEST_LOCK_BINDING_SUMMARY` after reset/initial state processing, including actual controller values.

## IDENTIFIER COMPARISON

`QuestLockController.TryUseKey` compares `incoming_key_id` and `required_key_id` via ordinal equality only. It does not compare GameObject names, display labels, hierarchy paths or instance IDs.

## INITIALIZATION ORDER

Bootstrap installs controllers/default debug state first. Quest application then clears all prior binding values, applies the active instance bindings, and applies initial state only for locked/open state. Initial states do not overwrite `requiredKeyId`; the active QuestInstance wins.

## RESET / SWITCH

`ResetControlledState` now calls `ClearQuestBinding` on every registered lock before applying the next instance. A stale key or target cannot survive restart/instance switch.

## LOCK USE TRACE

`LOCK_USE_ATTEMPT` now includes `lock_id`, `incoming_key_id`, `required_key_id`, `is_locked`, and `binding_match`. A match emits `LOCK_USE_SUCCESS` and existing `LOCK_UNLOCKED`; mismatch emits `LOCK_WRONG_KEY` without mutation.

## KEY-LOCK MATRIX

For A1 after the fix:

| Lock | key_001 | key_002 |
|---|---|---|
| lock_001 | fail | pass |
| lock_002 | pass | fail |
| lock_003 | fail unless explicitly bound | fail unless explicitly bound |

Only bindings present in the active QuestInstance should pass.

## TESTS

Added EditMode coverage for A1 legacy alias conversion to `lock_002`, canonical key comparison, wrong-key rejection, and binding clearing/reapplication. Unity tests have not been run here because the workspace lacks a Unity/.NET test runner.

## NEXT DEVICE TEST

Run A1 and inspect `QUEST_LOCK_BINDING_APPLIED`/`SUMMARY`. Confirm `lock_002 -> key_001`; then test `key_001 + lock_002` (success), `key_002 + lock_002` (failure), and `OPEN` on the associated drawer after success. Capture server payloads for A2, B1 and C1 to replace fallback-only rows with observed runtime values.
