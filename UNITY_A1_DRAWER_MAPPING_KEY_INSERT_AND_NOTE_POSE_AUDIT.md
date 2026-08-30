# A1 drawer mapping, key insertion, and note-pose audit

## PROVEN PREVIOUS BUG

The device trace proved `lock_002` remained unlocked, but A1 bound its target
to `table_drawer_001`. In the scene `lock_002` is physically attached to the
locked desk drawer `table_drawer_002`.

## A1 DRAWER MAPPING

`FixedQuestWireConverter` now normalizes the A1 legacy drawer-lock role as one
physical pair: `lock_drawer_001 -> lock_002 -> table_drawer_002`. The A1
fallback uses the same target. `key_002` and `clue_note_002` are configured on
that selected drawer, so reveal and open-gate lookup use one canonical ID.

## OTHER INSTANCE MAPPING AUDIT

| Instance | Current selected drawer | Canonical lock | Key | Status |
| --- | --- | --- | --- | --- |
| A1 | table_drawer_002 | lock_002 | key_001 | corrected physical pair |
| A2 | table_drawer_002 | lock_002 | key_001 | existing declaration retained |
| B1 | cabinet_drawer_001 | lock_003 | key_001 | existing fallback retained; physical scene retest required |
| C1 | cabinet_drawer_002 | lock_002 | key_002 | existing fallback retained; physical scene retest required |

## BOXCOLLIDER STATUS

The saved scene includes the manually added `BoxCollider` on the
`table_drawer_002` prefab instance. Bootstrap does not add or remove drawer
colliders. The drawer keeps its kinematic Rigidbody, so its collider moves with
the animated transform.

## DRAWER MOTION SAFETY

`ExperimentalDrawerController` moves the drawer transform directly when its
Rigidbody is kinematic. The retained BoxCollider is co-located on that moving
transform and does not change motion-anchor calculation. Device retest must
still check `DRAWER_MOTION_START`/`DRAWER_MOTION_COMPLETE` for drawer 002.

## GOLDEN KEY INSERTION

The previous insertion used `SetParent(slot, false)` below a lock whose imported
world scale is approximately `.02/.05/.02`. That made the key inherit the tiny
lock scale and visually disappear. The fixed path preserves world scale while
parenting, then places the key at the named insertion anchor.

## INSERTION ANCHORS

Bootstrap creates/reuses a deliberate `key_insert_anchor` for `lock_002` and
`lock_001`. Its local pose is explicit and `KEY_INSERT_POSE_APPLIED` records
the final world pose and whether the named anchor was already present.

## DRAWER CONTENT ANCHORS

The A1 selected drawer receives two distinct child anchors under its registered
inside anchor: `drawer_key_anchor` and `drawer_note_anchor`. Final poses are
applied while both objects are hidden, before the reveal subscription can
activate them.

## CLUE NOTE AXES

The previous generic placement copied the drawer anchor rotation, leaving the
note vertical/back-facing. The typed note placement reads the actual TMP
transform forward vector and rotates the note so its readable normal aligns to
drawer-local up (`QUEST_NOTE_FACE_ALIGNED`).

## CLUE NOTE FINAL POSE

The note uses `drawer_note_anchor`, offset from the key and raised slightly from
the drawer bottom. It is flat to the drawer-local plane rather than using a
world-space Euler correction.

## SILVER KEY PLACEMENT

`key_002` uses the separate `drawer_key_anchor`; it is offset opposite the
note, avoiding overlap and allowing its existing key interaction component to
remain usable after reveal.

## RESET

`QuestInsertedKeyState` is unchanged: it restores original parent, pose,
Rigidbody and grasp state before the next instance placement is applied.

## TESTS

Updated EditMode coverage verifies the A1 legacy lock alias resolves to
`lock_002` and `table_drawer_002`, and that the A1 fallback declares that same
drawer. Existing tests retain A2/B1/C1 declared lock/key mapping and key reset.

## NEXT DEVICE TEST

Rebuild after saving the scene, start C1/A1, point at `table_drawer_002`, use
`key_001` with `lock_002`, then open that drawer. Confirm the new target-bound,
motion, insertion-pose, typed-content, and reveal events. Verify the note text
reads “The Silver Key opens the exit door.” and visually tune the authored
anchor poses in the scene if required.
