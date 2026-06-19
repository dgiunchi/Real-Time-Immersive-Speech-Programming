# Escape Room Scene Snapshot After Fixes

Scene: `Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`
Selection convention: `SelectObjectRay` and `InteractionContextProvider` now resolve `GetComponentInParent<AIEditableObject>()` and require `game` on either the hit collider object or the resolved semantic root.
Current selection rule requires `game`: yes.

## AIEditableObject Inventory

### `basket_001`
- Unity name: `basket_001`
- Display name: `Basket`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `basket_001` (BoxCollider, tag=game)
- Labels: basket, container, receptacle, placement_target, ball_target, puzzle_mechanism, interactive
- Description: Basket where the created soccer ball must be placed.
- Selectable under current rules: yes

### `cabinet_001`
- Unity name: `cabinet_001`
- Display name: `Cabinet`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `cabinet_drawer_003` (BoxCollider, tag=game), `cabinet_drawer_001` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game), `lock_003` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game), `clue_note_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: cabinet, dresser, furniture, container_parent, interactive
- Description: Cabinet with drawers that may contain hidden puzzle objects such as the silver key or notes.
- Selectable under current rules: yes

### `cabinet_drawer_001`
- Unity name: `cabinet_drawer_001`
- Display name: `Cabinet Drawer 1`
- Root tag: `game`
- Parent: `cabinet_001`
- Colliders: `cabinet_drawer_001` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: drawer, cabinet_drawer, container, openable, unlocked, contains_silver_key, interactive
- Description: First cabinet drawer. It contains or hides the silver key.
- Selectable under current rules: yes

### `cabinet_drawer_002`
- Unity name: `cabinet_drawer_002`
- Display name: `Locked Cabinet Drawer`
- Root tag: `game`
- Parent: `cabinet_001`
- Colliders: `lock_003` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game), `clue_note_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: drawer, cabinet_drawer, container, locked, lockable, golden_key_target, interactive
- Description: Locked drawer of the cabinet. It should be unlocked using the golden key.
- Selectable under current rules: yes

### `cabinet_drawer_003`
- Unity name: `cabinet_drawer_003`
- Display name: `Cabinet Drawer 3`
- Root tag: `game`
- Parent: `cabinet_001`
- Colliders: `cabinet_drawer_003` (BoxCollider, tag=game)
- Labels: drawer, cabinet_drawer, container, openable, unlocked, interactive
- Description: Third cabinet drawer. It can be made openable and searched.
- Selectable under current rules: yes

### `clue_note_001`
- Unity name: `clue_note_001`
- Display name: `First Clue Note`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `clue_note_001` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: clue, note, readable, puzzle_instruction, first_clue, interactive
- Description: First note explaining that the golden key opens locked drawers.
- Selectable under current rules: yes

### `clue_note_002`
- Unity name: `clue_note_002`
- Display name: `Second Clue Note`
- Root tag: `game`
- Parent: `cabinet_drawer_002`
- Colliders: `clue_note_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: clue, note, readable, puzzle_instruction, ball_task_clue, interactive
- Description: Second note instructing the user to create a soccer ball and place it in the basket.
- Selectable under current rules: yes

### `door_001`
- Unity name: `door_001`
- Display name: `Exit Door`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `Door` (BoxCollider, tag=game), `lock_001` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game), `Frame` (MeshCollider, tag=Untagged)
- Labels: door, exit, openable, lockable, interactive, final_goal
- Description: Main exit door of the escape room. It can be unlocked with the correct key and opened.
- Selectable under current rules: yes

### `key_001`
- Unity name: `key_001`
- Display name: `Golden Key`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: key, golden_key, drawer_key, puzzle_item, unlock_item, visible, interactive
- Description: Visible key used to unlock locked drawers and discover further instructions.
- Selectable under current rules: yes

### `key_002`
- Unity name: `key_002`
- Display name: `Silver Key`
- Root tag: `game`
- Parent: `cabinet_drawer_001`
- Colliders: `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: key, silver_key, exit_key, puzzle_item, unlock_item, hidden, interactive
- Description: Hidden key used to unlock and open the exit door.
- Selectable under current rules: yes

### `lamp_001`
- Unity name: `lamp_001`
- Display name: `Puzzle Lamp`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `lamp_001` (BoxCollider, tag=game)
- Labels: lamp, light, feedback_object, interactive
- Description: Lamp that can provide visual feedback for puzzle events.
- Selectable under current rules: yes

### `lamp_002`
- Unity name: `lamp_002`
- Display name: `Puzzle Lamp`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `lamp_002` (BoxCollider, tag=game)
- Labels: lamp, light, feedback_object, interactive
- Description: Lamp that can change state or color in future puzzle logic
- Selectable under current rules: yes

### `lamp_003`
- Unity name: `lamp_003`
- Display name: `Puzzle Lamp`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `lamp_003` (BoxCollider, tag=game)
- Labels: lamp, light, feedback_object, interactive
- Description: Lamp that can change state or color in future puzzle logic
- Selectable under current rules: yes

### `lamp_004`
- Unity name: `lamp_004`
- Display name: `Puzzle Lamp`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `lamp_004` (BoxCollider, tag=game)
- Labels: lamp, light, feedback_object, interactive
- Description: Lamp that can change state or color in future puzzle logic
- Selectable under current rules: yes

### `lock_001`
- Unity name: `lock_001`
- Display name: `Door Lock`
- Root tag: `game`
- Parent: `Door`
- Colliders: `lock_001` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: lock, door_lock, exit_lock, puzzle_mechanism, interactive
- Description: Lock attached to the exit door. It should be unlocked with the silver key.
- Selectable under current rules: yes

### `lock_002`
- Unity name: `lock_002`
- Display name: `Desk Drawer Lock`
- Root tag: `game`
- Parent: `table_drawer_002`
- Colliders: `lock_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: lock, drawer_lock, desk_drawer_lock, table_drawer_lock, golden_key_target, puzzle_mechanism, interactive
- Description: Lock attached to the locked desk drawer. It should be unlocked with the golden key.
- Selectable under current rules: yes

### `lock_003`
- Unity name: `lock_003`
- Display name: `Cabinet Drawer Lock`
- Root tag: `game`
- Parent: `cabinet_drawer_002`
- Colliders: `lock_003` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: lock, drawer_lock, cabinet_drawer_lock, golden_key_target, puzzle_mechanism, interactive
- Description: Lock attached to the locked cabinet drawer. It should be unlocked with the golden key.
- Selectable under current rules: yes

### `painting_001`
- Unity name: `painting_001`
- Display name: `Crooked Painting`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `painting_001` (BoxCollider, tag=game)
- Labels: painting, wall_object, decoration, movable, rotatable, clue_context, interactive
- Description: A crooked wall painting that can be straightened and moved to reveal or contextualize clues.
- Selectable under current rules: yes

### `room_floor`
- Unity name: `room_floor`
- Display name: `Room Floor`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `room_floor` (BoxCollider, tag=Untagged)
- Labels: floor, room_structure, static
- Description: Floor of the escape room test environment
- Selectable under current rules: no

### `table_001`
- Unity name: `table_001`
- Display name: `Desk`
- Root tag: `game`
- Parent: `<scene-root>`
- Colliders: `table_drawer_001` (BoxCollider, tag=game), `lock_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game), `table_drawer_003` (BoxCollider, tag=game)
- Labels: desk, table, furniture, surface, container_parent, interactive
- Description: Desk with drawers. It can hold keys, notes, and other puzzle objects.
- Selectable under current rules: yes

### `table_drawer_001`
- Unity name: `table_drawer_001`
- Display name: `Desk Drawer 1`
- Root tag: `game`
- Parent: `table_001`
- Colliders: `table_drawer_001` (BoxCollider, tag=game)
- Labels: drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive
- Description: First drawer of the desk. It can be made openable and may contain puzzle objects.
- Selectable under current rules: yes

### `table_drawer_002`
- Unity name: `table_drawer_002`
- Display name: `Locked Desk Drawer`
- Root tag: `game`
- Parent: `table_001`
- Colliders: `lock_002` (BoxCollider, tag=game), `selection_proxy_collider` (BoxCollider, tag=game)
- Labels: drawer, desk_drawer, table_drawer, container, locked, lockable, golden_key_target, interactive
- Description: Locked drawer of the desk. It should be unlocked using the golden key.
- Selectable under current rules: yes

### `table_drawer_003`
- Unity name: `table_drawer_003`
- Display name: `Desk Drawer 3`
- Root tag: `game`
- Parent: `table_001`
- Colliders: `table_drawer_003` (BoxCollider, tag=game)
- Labels: drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive
- Description: Third drawer of the desk. It can be made openable and searched.
- Selectable under current rules: yes

### `wall_back`
- Unity name: `wall_back`
- Display name: `Back Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_back` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Back wall of the test room
- Selectable under current rules: no

### `wall_back_1`
- Unity name: `wall_back (1)`
- Display name: `Back Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_back (1)` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Back wall of the test room
- Selectable under current rules: no

### `wall_back_2`
- Unity name: `wall_back (2)`
- Display name: `Back Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_back (2)` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Back wall of the test room
- Selectable under current rules: no

### `wall_front`
- Unity name: `wall_front`
- Display name: `Front Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_front` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Front wall of the test room
- Selectable under current rules: no

### `wall_left`
- Unity name: `wall_left`
- Display name: `Left Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_left` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Left wall of the test room
- Selectable under current rules: no

### `wall_right`
- Unity name: `wall_right`
- Display name: `Right Wall`
- Root tag: `Untagged`
- Parent: `<scene-root>`
- Colliders: `wall_right` (BoxCollider, tag=Untagged)
- Labels: wall, room_structure, static
- Description: Right wall of the test room
- Selectable under current rules: no

## Intended Hierarchy

- `door_001` -> parent `<scene-root>`
- `lock_001` -> parent `Door`
- `table_001` -> parent `<scene-root>`
- `table_drawer_001` -> parent `table_001`
- `table_drawer_002` -> parent `table_001`
- `lock_002` -> parent `table_drawer_002`
- `table_drawer_003` -> parent `table_001`
- `cabinet_001` -> parent `<scene-root>`
- `cabinet_drawer_001` -> parent `cabinet_001`
- `key_002` -> parent `cabinet_drawer_001`
- `cabinet_drawer_002` -> parent `cabinet_001`
- `lock_003` -> parent `cabinet_drawer_002`
- `cabinet_drawer_003` -> parent `cabinet_001`
- `key_001` -> parent `<scene-root>`
- `clue_note_001` -> parent `<scene-root>`
- `clue_note_002` -> parent `cabinet_drawer_002`
- `basket_001` -> parent `<scene-root>`
- `painting_001` -> parent `<scene-root>`

## Selection Readiness Issues Remaining

- room_floor: collider `room_floor` is not tagged game and root is not tagged game
- wall_back: collider `wall_back` is not tagged game and root is not tagged game
- wall_back_1: collider `wall_back (1)` is not tagged game and root is not tagged game
- wall_back_2: collider `wall_back (2)` is not tagged game and root is not tagged game
- wall_front: collider `wall_front` is not tagged game and root is not tagged game
- wall_left: collider `wall_left` is not tagged game and root is not tagged game
- wall_right: collider `wall_right` is not tagged game and root is not tagged game

## Runtime Setup Status

- `SelectObjectRay`: resolves semantic parents with `GetComponentInParent<AIEditableObject>()`, sorts `RaycastAll` hits by distance, and accepts the hit if either the collider or the resolved semantic root is tagged `game`.
- `InteractionContextProvider`: mirrors the same semantic resolution rule for pointer snapshots.
- `SceneRegistry`: resolves collider hits to semantic parents with `GetComponentInParent<AIEditableObject>()`.
