# Escape Room Scene Snapshot After Fixes

## Selection Convention
- `SelectObjectRay` previously required `selectedObject.tag == "game"` on the direct hit object.
- The selection patch now resolves `GetComponentInParent<AIEditableObject>()` and accepts hits when either the collider or resolved AI root is tagged `game`.
- `ContextBridge` already resolves parent `AIEditableObject` targets through `SceneRegistry.TryGetSummary(Collider)`.

## AIEditableObject Inventory

| gameObject | objectId | displayName | tag | collider | selectable | labels | description |
|---|---|---|---|---|---:|---|---|
| `basket_001` | `basket_001` | `Basket` | `game` | `BoxCollider` | yes | `basket, container, receptacle, placement_target, ball_target, puzzle_mechanism, interactive` | Basket where the created soccer ball must be placed. |
| `cabinet_drawer_001` | `cab_drawer_001` | `Cabinet Drawer 1` | `game` | `BoxCollider` | yes | `drawer, cabinet_drawer, container, openable, unlocked, contains_silver_key, interactive` | First cabinet drawer. It contains or hides the silver key. |
| `cabinet_drawer_002` | `cab_drawer_002` | `Locked Cabinet Drawer` | `game` | `BoxCollider` | yes | `drawer, cabinet_drawer, container, locked, lockable, golden_key_target, interactive` | Locked drawer of the cabinet. It should be unlocked using the golden key. |
| `cabinet_drawer_003` | `cab_drawer_003` | `Cabinet Drawer 3` | `game` | `BoxCollider` | yes | `drawer, cabinet_drawer, container, openable, unlocked, interactive` | Third cabinet drawer. It can be made openable and searched. |
| `cabinet_001` | `cabinet_001` | `Cabinet` | `game` | `BoxCollider` | yes | `cabinet, dresser, furniture, container_parent, interactive` | Cabinet with drawers that may contain hidden puzzle objects such as the silver key or notes. |
| `clue_note_001` | `clue_note_001` | `First Clue Note` | `game` | `BoxCollider` | yes | `clue, note, readable, puzzle_instruction, first_clue, interactive` | First note explaining that the golden key opens locked drawers. |
| `clue_note_002` | `clue_note_002` | `Second Clue Note` | `game` | `BoxCollider` | yes | `clue, note, readable, puzzle_instruction, ball_task_clue, interactive` | Second note instructing the user to create a soccer ball and place it in the basket. |
| `desk_drawer_001` | `desk_drawer_001` | `Desk Drawer 1` | `game` | `BoxCollider` | yes | `drawer, desk_drawer, container, openable, unlocked, interactive` | First drawer of the desk. It can be made openable and may contain puzzle objects. |
| `desk_drawer_002` | `desk_drawer_002` | `Locked Desk Drawer` | `game` | `BoxCollider` | yes | `drawer, desk_drawer, container, locked, lockable, golden_key_target, interactive` | Locked drawer of the desk. It should be unlocked using the golden key. |
| `desk_drawer_003` | `desk_drawer_003` | `Desk Drawer 3` | `game` | `BoxCollider` | yes | `drawer, desk_drawer, container, openable, unlocked, interactive` | Third drawer of the desk. It can be made openable and searched. |
| `door_001` | `door_001` | `Exit Door` | `game` | `BoxCollider` | yes | `door, exit, openable, lockable, interactive, final_goal` | Main exit door of the escape room. It can be unlocked with the correct key and opened. |
| `golden_key_001` | `key_001` | `Golden Key` | `game` | `BoxCollider` | yes | `key, golden_key, drawer_key, puzzle_item, unlock_item, visible, interactive` | Visible key used to unlock locked drawers and discover further instructions. |
| `silver_key_001` | `key_002` | `Silver Key` | `game` | `BoxCollider` | yes | `key, silver_key, exit_key, puzzle_item, unlock_item, hidden, interactive` | Hidden key used to unlock and open the exit door. |
| `lamp_001` | `lamp_001` | `Puzzle Lamp` | `game` | `BoxCollider` | yes | `lamp, light, feedback_object, interactive` | Lamp that can provide visual feedback for puzzle events. |
| `lamp_002` | `lamp_002` | `Puzzle Lamp` | `game` | `BoxCollider` | no | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic |
| `lamp_003` | `lamp_003` | `Puzzle Lamp` | `game` | `BoxCollider` | no | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic |
| `lamp_004` | `lamp_004` | `Puzzle Lamp` | `game` | `BoxCollider` | no | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic |
| `lock_001` | `lock_001` | `Door Lock` | `game` | `BoxCollider` | yes | `lock, door_lock, exit_lock, puzzle_mechanism, interactive` | Lock attached to the exit door. It should be unlocked with the silver key. |
| `lock_002` | `lock_002` | `Desk Drawer Lock` | `game` | `BoxCollider` | yes | `lock, drawer_lock, desk_drawer_lock, golden_key_target, puzzle_mechanism, interactive` | Lock attached to the locked desk drawer. It should be unlocked with the golden key. |
| `lock_003` | `lock_003` | `Cabinet Drawer Lock` | `game` | `BoxCollider` | yes | `lock, drawer_lock, cabinet_drawer_lock, golden_key_target, puzzle_mechanism, interactive` | Lock attached to the locked cabinet drawer. It should be unlocked with the golden key. |
| `painting_001` | `painting_001` | `Crooked Painting` | `game` | `BoxCollider` | yes | `painting, wall_object, decoration, movable, rotatable, clue_context, interactive` | A crooked wall painting that can be straightened and moved to reveal or contextualize clues. |
| `room_floor` | `room_floor` | `Room Floor` | `Untagged` | `BoxCollider` | no | `floor, room_structure, static` | Floor of the escape room test environment |
| `table_001` | `table_001` | `Desk` | `game` | `BoxCollider` | yes | `desk, table, furniture, surface, container_parent, interactive` | Desk with drawers. It can hold keys, notes, and other puzzle objects. |
| `wall_back` | `wall_back` | `Back Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Back wall of the test room |
| `wall_back (1)` | `wall_back_1` | `Back Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Back wall of the test room |
| `wall_back (2)` | `wall_back_2` | `Back Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Back wall of the test room |
| `wall_front` | `wall_front` | `Front Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Front wall of the test room |
| `wall_left` | `wall_left` | `Left Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Left wall of the test room |
| `wall_right` | `wall_right` | `Right Wall` | `Untagged` | `BoxCollider` | no | `wall, room_structure, static` | Right wall of the test room |

## Hierarchy Checks
- `lock_001` -> `door_001`
- `lock_002` -> `desk_drawer_002`
- `lock_003` -> `cabinet_drawer_002`
- `silver_key_001` -> `cabinet_drawer_001`

## Collider Status
- Added root BoxColliders for: basket_001 (BoxCollider), cabinet_001 (BoxCollider), cabinet_drawer_001 (BoxCollider), cabinet_drawer_002 (BoxCollider), cabinet_drawer_003 (BoxCollider), desk_drawer_001 (BoxCollider), desk_drawer_002 (BoxCollider), desk_drawer_003 (BoxCollider), door_001 (BoxCollider), golden_key_001 (BoxCollider), painting_001 (BoxCollider), silver_key_001 (BoxCollider), table_001 (BoxCollider)
- Resized existing BoxColliders for: clue_note_001, clue_note_002, lock_001, lock_002, lock_003
- Collider hit targets / proxy collider tag blockers: none introduced; colliders live on semantic roots.

## Selection Readiness
- Intended interactables missing tag `game`: none
- Intended interactables still not selectable: none
- Room walls and floor remain untagged and are not promoted to semantic targets.

## Runtime Setup Status
- `DreamCodeVR2_RuntimeServices` remains present.
- No NetworkId 98 / 99 / 100 / 94 changes were authored.
- No SceneAPI / BehaviorAPI / Planner / Undo / Memory systems were added.

## Change Summary
- Renamed semantic GameObject roots: cab_drawer_001: cab_drawer_001 -> cabinet_drawer_001, cab_drawer_002: cab_drawer_002 -> cabinet_drawer_002, cab_drawer_003: cab_drawer_003 -> cabinet_drawer_003, key_001: key_001 -> golden_key_001, key_002: key_002 -> silver_key_001
- Tag changes: none
- Verified hierarchy relationships: lock_001 remains parented to the door prefab transform.; lock_002 remains parented to desk_drawer_002.; lock_003 remains parented to cabinet_drawer_002.; silver_key_001 remains parented under cabinet_drawer_001.
