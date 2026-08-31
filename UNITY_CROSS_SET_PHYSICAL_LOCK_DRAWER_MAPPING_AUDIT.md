# Unity Cross-Set Physical Lock/Drawer Mapping Audit

## Scope and evidence standard

This is a read-only mapping audit, except for the editor regression test strengthened at the end of this document. No quest mapping, collider, raycast, scene hierarchy, or server code was changed.

The physical conclusions below are based on the serialized active scene `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity` and the source-prefab transform parents, not on the numeric suffixes of IDs or on display labels.

## Physical lock map

| Physical lock GameObject / canonical ID | Serialized parent path | Physical target proved by parent transform | Runtime key-insertion setup |
| --- | --- | --- | --- |
| `lock_001` | `door_001 / Door / lock_001` | `door_001` (the lock is child of the `Door` transform in `Door_1_Yellow.prefab`) | `VerticalSliceRuntimeBootstrap.EnsureKeyInsertAnchor` and `EnsureKeyInsertionZone` create children of this lock at runtime. |
| `lock_002` | `table_001 / S.T Drawer 2 / lock_002` | `table_drawer_002` (the semantic object is the same `S.T Drawer 2` prefab source transform) | Same per-lock runtime anchor and zone. |
| `lock_003` | `cabinet_001 / C Drawer 2 / lock_003` | `cabinet_drawer_002` (the semantic object is the same `C Drawer 2` prefab source transform) | Same per-lock runtime anchor and zone. |

There are exactly three active physical lock objects in the scene. Thus the authoritative physical associations are:

```text
lock_001 -> door_001
lock_002 -> table_drawer_002
lock_003 -> cabinet_drawer_002
```

### All drawer identities checked

| Semantic ID | Prefab child actually represented |
| --- | --- |
| `table_drawer_001` | `table_001 / S.T Drawer 1` |
| `table_drawer_002` | `table_001 / S.T Drawer 2` |
| `table_drawer_003` | `table_001 / S.T Drawer 3` |
| `cabinet_drawer_001` | `cabinet_001 / C Drawer 1` |
| `cabinet_drawer_002` | `cabinet_001 / C Drawer 2` |
| `cabinet_drawer_003` | `cabinet_001 / C Drawer 3` |

`cabinet_drawer_001` has no physical lock child. It is a different physical drawer from the one that owns `lock_003`.

## Binding pipeline

The active flow is:

```text
server QuestInstance / local T1 fallback
  -> FixedQuestWireConverter (server DTO conversion)
  -> QuestCanonicalIds.Normalize / ResolveDrawerForLock
  -> QuestInstanceResolver.Resolve
  -> QuestInstanceController.ApplyLockBinding
  -> QuestLockController.Configure(requiredKeyId, targetObjectId)
  -> QuestLockController.FindForTarget / CanOpenTarget
```

`QuestLockController` has no physical-parent verification. It accepts the `targetObjectId` supplied by the resolved quest and later treats it as authoritative. Therefore a logical binding can make a key inserted in one physical lock unlock a different drawer.

`VerticalSliceRuntimeBootstrap` creates the insertion anchor and insertion zone under the *physical lock object*, but its bootstrap defaults are cleared by `QuestInstanceController.ResetControlledState` before per-quest bindings are applied. The resulting key/target association is consequently dictated by the resolved QuestInstance.

## Canonicalization and stale compatibility

`QuestCanonicalIds.Normalize` currently converts:

```text
lock_drawer_001 -> lock_002
lock_drawer_002 -> lock_002
lock_drawer_003 -> lock_003
```

This is a legacy logical-alias translation, not a physical lookup. It is ambiguous for any producer that uses the first two aliases to mean different physical locks.

`QuestCanonicalIds.ResolveDrawerForLock` also contains an A1-only compatibility override: every `set_a_instance_1` binding normalized to `lock_002` is forced to `table_drawer_002`; task object `table_drawer_001` is similarly remapped for A1. This matches the physical A1 table lock, but is stale transitional client-side behavior and must not become the general cross-set mapping mechanism.

## Resolved instance matrix

“Current resolved” means the active local resolver/fallback configuration. B1 additionally has direct real-device evidence in `client_20260831T140046Z_run.jsonl`; activation-only server messages for the other sets use the local first-task fallback when the documented preceding payload is absent.

| Quest instance | Current resolved drawer-lock binding | Physical comparison | Key comparison | Status |
| --- | --- | --- | --- | --- |
| A1 `set_a_instance_1` | `key_001 -> lock_002 -> table_drawer_002`; exit `key_002 -> lock_001 -> door_001` | Both physical pairs match. | Consistent. | PASS |
| A2 `set_a_instance_2` | `key_001 -> lock_002 -> table_drawer_002`; exit `key_002 -> lock_001 -> door_001` | Both physical pairs match. | Consistent. | PASS |
| B1 `set_b_instance_1` | `key_001 -> lock_003 -> cabinet_drawer_001`; exit `key_002 -> lock_001 -> door_001` | `lock_003` is physically on `cabinet_drawer_002`, not `cabinet_drawer_001`. The same mismatch was emitted by the actual B1 device binding log. | The key itself is a valid configurable quest rule, but it is attached to a wrong drawer target. | MISMATCH |
| C1 `set_c_instance_1` | `key_002 -> lock_002 -> cabinet_drawer_002`; exit `key_001 -> lock_001 -> door_001` | `lock_002` is physically on `table_drawer_002`; the target is wrong. The physical cabinet target has `lock_003`. | `key_002` may be the intended alternate key, but it is bound to the wrong physical lock for the declared cabinet target. | MISMATCH |

## Task-target consistency

For A1 the special normalization keeps generated `object_open:table_drawer_001` tasks aligned with `table_drawer_002`; this is currently physically correct but relies on legacy compensation.

For A2, the resolved target already agrees with the physical table lock.

For B1, any unlock/open task referring to `cabinet_drawer_001` is logically routed through `lock_003` even though the key is visibly inserted on `cabinet_drawer_002`. This is the proven visual/physical mismatch.

For C1, a task intended to unlock/open `cabinet_drawer_002` must resolve through `lock_003`, not `lock_002`. The current fallback instead assigns `lock_002`, making the visible key insertion occur on the desk drawer while the logical gate affects the cabinet drawer.

## Key consistency

The scene establishes ownership of locks, not fixed key colours/IDs. `requiredKeyId` is intentionally quest-configurable. The key inconsistency is therefore not “which key ID is globally correct”; it is that B1 and C1 bind otherwise valid per-set key rules to a lock whose physical owner is a different drawer than the requested target.

## Proven mismatches

1. **B1:** `lock_003 -> cabinet_drawer_001` is false physically. The correct physical target of `lock_003` is `cabinet_drawer_002`.
2. **C1:** `lock_002 -> cabinet_drawer_002` is false physically. The correct physical lock for `cabinet_drawer_002` is `lock_003`; `lock_002` belongs to `table_drawer_002`.

No mismatch was proven for A1 or A2.

## Authoritative side to correct

The authoritative correction belongs to the server/vendor QuestInstance data: it must emit canonical physical lock IDs and matching `task_targets.drawer` / binding target IDs. The client receives and trusts those values.

After the server migration, the client should remove or narrow the legacy alias normalization and A1 forced remap only when the deployed server no longer emits legacy A1 values. The local `FixedQuestActivationFallback` must then be changed in the same release to match the corrected server matrix. This audit intentionally does **not** make those mapping changes.

## Tests

The existing editor test `FixedFallbackInstancesRetainTheirDeclaredCanonicalDrawerBinding` was strengthened to assert the current target as well as lock and key for A1, A2, B1, and C1. It makes the current B1/C1 discrepancy explicit in test evidence rather than silently testing only IDs.

This is a resolver regression test, not a replacement for a scene-physical integration test. There is presently no centralized serialized physical map for `QuestLockController` to validate automatically. A later mapping-fix change should update this test to the corrected B1/C1 data and add a scene integration assertion that compares each configured target with the lock transform's physical owner.

## Device validation after the eventual mapping correction

1. Run B1; insert `key_001` into the visible lock on cabinet drawer 2 and confirm that the same drawer opens.
2. Run C1; insert the configured alternate key into the visible lock on cabinet drawer 2 and confirm that the same drawer opens.
3. Re-run A1 and A2; confirm the golden-key lock remains the second desk drawer and the exit key remains the door lock.
4. Verify `QUEST_LOCK_TARGET_BOUND` and `DRAWER_OPEN_GATE` use the same physical target as the visible lock in each run.

## No mapping patch applied

No production mapping was changed in this audit. No collider, raycast, hierarchy, quest design, or server code was altered.
