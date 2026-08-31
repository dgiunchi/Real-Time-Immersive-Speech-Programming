# Unity A1 Legacy Mapping Cleanup

## REMOVED A1 SPECIAL CASES

Removed the A1-only drawer rewrite from `QuestCanonicalIds` / `QuestInstanceResolver`:

- `set_a_instance_1 + lock_002` no longer forces `table_drawer_002`;
- `table_drawer_001 -> table_drawer_002` task-object rewriting was removed;
- generated task required objects, task target, and success-condition objects no longer contain an A1-specific remap;
- `FixedQuestWireConverter` now uses the normalized runtime required-object target rather than the raw wire value.

Canonical A1 server data therefore passes through without a QuestInstance-specific rewrite.

## LEGACY INPUT COMPATIBILITY

Generic protocol-boundary lock alias compatibility remains in `QuestCanonicalIds.Normalize`:

```text
lock_drawer_001 / lock_drawer_002 -> lock_002
lock_drawer_003                  -> lock_003
```

It converts only the lock ID. It does not infer, replace, or otherwise modify `task_targets.drawer`. Current canonical server payloads do not need this conversion.

## CANONICAL A1

The canonical A1 path now resolves naturally as:

```text
key_001 -> lock_002 -> table_drawer_002
unlock condition: LOCK_UNLOCKED:lock_002
open condition:   OBJECT_OPEN:table_drawer_002
reveal drawer:    table_drawer_002
```

`QuestInstanceResolver` now reports `QUEST_CANONICAL_INSTANCE_RESOLVED` once per resolution. For canonical A1 it reports `legacy_conversion_used=false` with drawer `table_drawer_002`, lock `lock_002`, and key `key_001`.

## FALLBACK

`FixedQuestActivationFallback` is canonical for A1 and A2. It was also updated to the supplied physical matrix for the previously stale cross-set entries:

```text
A1 key_001 -> lock_002 -> table_drawer_002
A2 key_001 -> lock_002 -> table_drawer_002
B1 key_001 -> lock_003 -> cabinet_drawer_002
C1 key_002 -> lock_003 -> cabinet_drawer_002
Exit          lock_001 -> door_001
```

## CROSS-SET MATRIX

The runtime has no remaining A1-specific drawer association. Server bindings are applied as declared after generic lock-ID normalization. The fallback matrix above matches the authoritative physical map.

## RUNTIME BINDING

`QuestInstanceController.ApplyLockBinding` is unchanged. With current A1 data it configures:

```text
lock_002.requiredKeyId = key_001
lock_002.associatedTargetObjectId = table_drawer_002
```

The existing `QUEST_LOCK_TARGET_BOUND` diagnostic consequently emits this exact canonical pair.

## TESTS

Updated editor coverage verifies:

- canonical A1 binding and A1 open task pass through unchanged;
- direct A1 resolved instance is `key_001 -> lock_002 -> table_drawer_002` without an instance-specific remap;
- generic legacy alias normalization changes only the lock ID and preserves the declared drawer;
- canonical A1 fallback and A2 remain unchanged;
- B1 and C1 fallback now use `lock_003 -> cabinet_drawer_002` with their respective keys.

The local machine has no .NET SDK, so `dotnet build Assembly-CSharp.csproj --no-restore` could not run. Run Unity EditMode tests / compilation in the Unity Editor before device deployment.

## DEVICE VALIDATION

1. Start A1 and inspect `QUEST_CANONICAL_INSTANCE_RESOLVED`: it must show `legacy_conversion_used=false`, `lock_002`, and `table_drawer_002`.
2. Unlock and open the second desk drawer; confirm the key insertion, open task, and reveal all refer to the same physical drawer.
3. Smoke-test A2, B1, and C1 against the matrix above, including `QUEST_LOCK_TARGET_BOUND`.
4. Confirm old alias input, if intentionally tested, changes only the lock ID and does not rewrite the submitted drawer target.
