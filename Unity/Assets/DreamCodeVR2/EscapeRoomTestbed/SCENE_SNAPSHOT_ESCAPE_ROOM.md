# Escape Room Scene Snapshot

## 1. Scene and Runtime Summary
- scene name: `DreamCodeVR2_EscapeRoom_Testbed`
- scene path: `Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`
- total GameObjects inspected: `599`
- AIEditableObject count: `29`
- collider component count: `17`
- GameObjects with colliders: `17`
- likely selectable/raycastable GameObjects: `17`
- duplicate objectIds: `0`
- Ubiq runtime status: inherited DynamicCompiler runtime objects are present in scene serialization.
- ContextBridge status: `SceneRegistry=1`, `InteractionContextProvider=1`, `InteractionContextTransmitter=1`
- SceneContext status: `SceneContextCompiler=1`, `SceneContextTransmitter=1`
- detectable missing inspector references in runtime objects: `5`

## 2. High-Level Hierarchy
- clue_note_001 [AIEditableObject] [Collider]
  - Text (TMP)
- clue_note_002 [AIEditableObject] [Collider]
  - Text (TMP)
- DreamCodeVR2_RuntimeServices
- GameObject_1125636089 [AIEditableObject]
- GameObject_1198313274 [AIEditableObject]
- GameObject_1213554618 [AIEditableObject] [Collider]
- GameObject_1222747714 [AIEditableObject]
- GameObject_1311336857 [AIEditableObject]
- GameObject_1315326853 [AIEditableObject]
- GameObject_1738825464 [AIEditableObject] [Collider]
- GameObject_1891920693 [AIEditableObject]
- GameObject_190967584 [AIEditableObject]
- GameObject_1916706071 [AIEditableObject] [Collider]
- GameObject_1967296781 [AIEditableObject]
- GameObject_2084505965 [AIEditableObject]
- GameObject_436017451 [AIEditableObject]
- GameObject_45509927 [AIEditableObject]
- GameObject_480108160 [AIEditableObject]
- GameObject_713582546 [AIEditableObject]
- GameObject_861069271 [AIEditableObject] [Collider]
- lock_001 [AIEditableObject] [Collider]
- lock_002 [AIEditableObject] [Collider]
- lock_003 [AIEditableObject] [Collider]
- Menu [Collider]
  - Canvas
    - Main Panel
      - Join Room Panel
        - Keyboard
          - Keyboard
            - 0
              - Text
            - 1
              - Text
            - 2
              - Text
            - 3
              - Text
            - 4
              - Text
            - 5
              - Text
            - 6
              - Text
            - 7
              - Text
            - 8
              - Text
            - 9
              - Text
            - A
              - Text
            - B
              - Text
            - backspace
              - Text
            - C
              - Text
            - D
              - Text
            - E
              - Text
            - F
              - Text
            - G
              - Text
            - H
              - Text
            - I
              - Text
            - J
              - Text
            - K
              - Text
            - L
              - Text
            - M
              - Text
            - N
              - Text
            - O
              - Text
            - P
              - Text
            - Q
              - Text
            - R
              - Text
            - S
              - Text
            - shift
              - Text
            - spacebar
              - Text
            - T
              - Text
            - U
              - Text
            - V
              - Text
            - W
              - Text
            - X
              - Text
            - Y
              - Text
            - Z
              - Text
      - New Room Panel
        - Keyboard
          - Keyboard
            - 0
              - Text
            - 1
              - Text
            - 2
              - Text
            - 3
              - Text
            - 4
              - Text
            - 5
              - Text
            - 6
              - Text
            - 7
              - Text
            - 8
              - Text
            - 9
              - Text
            - A
              - Text
            - B
              - Text
            - backspace
              - Text
            - C
              - Text
            - D
              - Text
            - E
              - Text
            - F
              - Text
            - G
              - Text
            - H
              - Text
            - I
              - Text
            - J
              - Text
            - K
              - Text
            - L
              - Text
            - M
              - Text
            - N
              - Text
            - O
              - Text
            - P
              - Text
            - Q
              - Text
            - R
              - Text
            - S
              - Text
            - shift
              - Text
            - spacebar
              - Text
            - T
              - Text
            - U
              - Text
            - V
              - Text
            - W
              - Text
            - X
              - Text
            - Y
              - Text
            - Z
              - Text
      - Set Name Panel
        - Keyboard
          - Keyboard
            - 0
              - Text
            - 1
              - Text
            - 2
              - Text
            - 3
              - Text
            - 4
              - Text
            - 5
              - Text
            - 6
              - Text
            - 7
              - Text
            - 8
              - Text
            - 9
              - Text
            - A
              - Text
            - B
              - Text
            - backspace
              - Text
            - C
              - Text
            - D
              - Text
            - E
              - Text
            - F
              - Text
            - G
              - Text
            - H
              - Text
            - I
              - Text
            - J
              - Text
            - K
              - Text
            - L
              - Text
            - M
              - Text
            - N
              - Text
            - O
              - Text
            - P
              - Text
            - Q
              - Text
            - R
              - Text
            - S
              - Text
            - shift
              - Text
            - spacebar
              - Text
            - T
              - Text
            - U
              - Text
            - V
              - Text
            - W
              - Text
            - X
              - Text
            - Y
              - Text
            - Z
              - Text
- room_floor [AIEditableObject] [Collider]
- wall_back [AIEditableObject] [Collider]
- wall_back (1) [AIEditableObject] [Collider]
- wall_back (2) [AIEditableObject] [Collider]
- wall_front [AIEditableObject] [Collider]
- wall_left [AIEditableObject] [Collider]
- wall_right [AIEditableObject] [Collider]

## 3. AIEditableObject Inventory

| path | objectId | displayName | labels | description | editable | collider status | notes |
|---|---|---|---|---|---:|---|---|
| `clue_note_001` | `clue_note_001` | Clue Note | `clue, note, puzzle_item` | Note containing a clue for the escape room | true | BoxCollider |  |
| `clue_note_002` | `clue_note_002` | Clue Note | `clue, note, puzzle_item` | Note containing a clue for the escape room | true | BoxCollider |  |
| `GameObject_1125636089` | `desk_drawer_001` | Desk Drawer 1 | `drawer, openable, interactive` | First drawer of the desk | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1198313274` | `table_001` | Wooden Table | `table, surface, furniture, openable, interactive` | Table used as a placement surface with drawers, drawers can contain | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1213554618` | `lamp_001` | Puzzle Lamp | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic | true | BoxCollider | no renderer on self/children |
| `GameObject_1222747714` | `key_002` | Silver Key | `key, pickup, puzzle_item` | A puzzle key that can be used in future behavior logic | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1311336857` | `desk_drawer_002` | Desk Drawer 2 | `drawer, openable, interactive` | Second drawer of the desk | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1315326853` | `cabinet_001` | Wooden Cabinet | `cabinet, container, openable, interactive` | Cabinet with drawers that can contain clues or keys, drawers can contain | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1738825464` | `lamp_004` | Puzzle Lamp | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic | true | BoxCollider | no renderer on self/children |
| `GameObject_1891920693` | `painting_001` | Wall Painting | `painting, decoration, clue_holder, interactive` | Painting that can hide or reveal a clue | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_190967584` | `basket_001` | Basket | `basket, target, puzzle_mechanism` | Basket that should contain a ball | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_1916706071` | `lamp_002` | Puzzle Lamp | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic | true | BoxCollider | no renderer on self/children |
| `GameObject_1967296781` | `cab_drawer_002` | Cabinet Drawer 2 | `drawer, openable, interactive` | Second drawer of the cabinet | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_2084505965` | `key_001` | Golden Key | `key, pickup, puzzle_item` | A puzzle key that can be used in future behavior logic | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_436017451` | `cab_drawer_003` | Cabinet Drawer 3 | `drawer, openable, interactive` | Third drawer of the cabinet | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_45509927` | `door_001` | Door | `door, exit, interactive, openable` | Main exit door of the escape room | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_480108160` | `cab_drawer_001` | Cabinet Drawer 1 | `drawer, openable, interactive` | First drawer of the cabinet | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_713582546` | `desk_drawer_003` | Desk Drawer 3 | `drawer, openable, interactive` | Third drawer of the desk | true | none | no collider on self/children; no renderer on self/children |
| `GameObject_861069271` | `lamp_003` | Puzzle Lamp | `lamp, light, feedback_object, interactive` | Lamp that can change state or color in future puzzle logic | true | BoxCollider | no renderer on self/children |
| `lock_001` | `lock_001` | Door Lock | `lock, door_lock, puzzle_mechanism, interactive` | Lock attached to the exit door | true | BoxCollider |  |
| `lock_002` | `lock_002` | Table Drawer Lock | `lock, door_lock, puzzle_mechanism, interactive` | Lock attached to the second table drawer | true | BoxCollider |  |
| `lock_003` | `lock_003` | Cabinet Drawer Lock | `lock, door_lock, puzzle_mechanism, interactive` | Lock attached to the second cabinet drawer | true | BoxCollider |  |
| `room_floor` | `room_floor` | Room Floor | `floor, room_structure, static` | Floor of the escape room test environment | false | BoxCollider |  |
| `wall_back` | `wall_back` | Back Wall | `wall, room_structure, static` | Back wall of the test room | false | BoxCollider |  |
| `wall_back (1)` | `wall_back_1` | Back Wall | `wall, room_structure, static` | Back wall of the test room | false | BoxCollider |  |
| `wall_back (2)` | `wall_back_2` | Back Wall | `wall, room_structure, static` | Back wall of the test room | false | BoxCollider |  |
| `wall_front` | `wall_front` | Front Wall | `wall, room_structure, static` | Front wall of the test room | false | BoxCollider |  |
| `wall_left` | `wall_left` | Left Wall | `wall, room_structure, static` | Left wall of the test room | false | BoxCollider |  |
| `wall_right` | `wall_right` | Right Wall | `wall, room_structure, static` | Right wall of the test room | false | BoxCollider |  |

### Detailed AIEditableObject Notes
#### `clue_note_001`
- GameObject name: `clue_note_001`
- objectId: `clue_note_001`
- displayName: `Clue Note`
- description: Note containing a clue for the escape room
- labels / semantic_types: `clue, note, puzzle_item`
- editable flag: `true`
- active flag: `true`
- position: `(-4.86, 1.058, 0.743)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.01, 0.28, 0.2)`
- parent object: ``
- children: `Text (TMP)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.01,0.28,0.20)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `LiberationSans SDF.asset, Paper.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `none`

#### `clue_note_002`
- GameObject name: `clue_note_002`
- objectId: `clue_note_002`
- displayName: `Clue Note`
- description: Note containing a clue for the escape room
- labels / semantic_types: `clue, note, puzzle_item`
- editable flag: `true`
- active flag: `true`
- position: `(-0.035, 0.074, 0.01)`
- rotation: `(0.0, 0.0, 90.0)`
- scale: `(0.01, 0.28, 0.2)`
- parent object: ``
- children: `Text (TMP)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.01,0.28,0.20)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `LiberationSans SDF.asset, Paper.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `none`

#### `GameObject_1125636089`
- GameObject name: `GameObject_1125636089`
- objectId: `desk_drawer_001`
- displayName: `Desk Drawer 1`
- description: First drawer of the desk
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1198313274`
- GameObject name: `GameObject_1198313274`
- objectId: `table_001`
- displayName: `Wooden Table`
- description: Table used as a placement surface with drawers, drawers can contain
- labels / semantic_types: `table, surface, furniture, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1213554618`
- GameObject name: `GameObject_1213554618`
- objectId: `lamp_001`
- displayName: `Puzzle Lamp`
- description: Lamp that can change state or color in future puzzle logic
- labels / semantic_types: `lamp, light, feedback_object, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.23,1.29,0.26)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no renderer on self/children`

#### `GameObject_1222747714`
- GameObject name: `GameObject_1222747714`
- objectId: `key_002`
- displayName: `Silver Key`
- description: A puzzle key that can be used in future behavior logic
- labels / semantic_types: `key, pickup, puzzle_item`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1311336857`
- GameObject name: `GameObject_1311336857`
- objectId: `desk_drawer_002`
- displayName: `Desk Drawer 2`
- description: Second drawer of the desk
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1315326853`
- GameObject name: `GameObject_1315326853`
- objectId: `cabinet_001`
- displayName: `Wooden Cabinet`
- description: Cabinet with drawers that can contain clues or keys, drawers can contain
- labels / semantic_types: `cabinet, container, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1738825464`
- GameObject name: `GameObject_1738825464`
- objectId: `lamp_004`
- displayName: `Puzzle Lamp`
- description: Lamp that can change state or color in future puzzle logic
- labels / semantic_types: `lamp, light, feedback_object, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.23,1.29,0.26)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no renderer on self/children`

#### `GameObject_1891920693`
- GameObject name: `GameObject_1891920693`
- objectId: `painting_001`
- displayName: `Wall Painting`
- description: Painting that can hide or reveal a clue
- labels / semantic_types: `painting, decoration, clue_holder, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_190967584`
- GameObject name: `GameObject_190967584`
- objectId: `basket_001`
- displayName: `Basket`
- description: Basket that should contain a ball
- labels / semantic_types: `basket, target, puzzle_mechanism`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_1916706071`
- GameObject name: `GameObject_1916706071`
- objectId: `lamp_002`
- displayName: `Puzzle Lamp`
- description: Lamp that can change state or color in future puzzle logic
- labels / semantic_types: `lamp, light, feedback_object, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.23,1.29,0.26)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no renderer on self/children`

#### `GameObject_1967296781`
- GameObject name: `GameObject_1967296781`
- objectId: `cab_drawer_002`
- displayName: `Cabinet Drawer 2`
- description: Second drawer of the cabinet
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_2084505965`
- GameObject name: `GameObject_2084505965`
- objectId: `key_001`
- displayName: `Golden Key`
- description: A puzzle key that can be used in future behavior logic
- labels / semantic_types: `key, pickup, puzzle_item`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_436017451`
- GameObject name: `GameObject_436017451`
- objectId: `cab_drawer_003`
- displayName: `Cabinet Drawer 3`
- description: Third drawer of the cabinet
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_45509927`
- GameObject name: `GameObject_45509927`
- objectId: `door_001`
- displayName: `Door`
- description: Main exit door of the escape room
- labels / semantic_types: `door, exit, interactive, openable`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_480108160`
- GameObject name: `GameObject_480108160`
- objectId: `cab_drawer_001`
- displayName: `Cabinet Drawer 1`
- description: First drawer of the cabinet
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_713582546`
- GameObject name: `GameObject_713582546`
- objectId: `desk_drawer_003`
- displayName: `Desk Drawer 3`
- description: Third drawer of the desk
- labels / semantic_types: `drawer, openable, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `false`
- collider type and approximate bounds: `(none)`
- collider location: `none`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `false`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no collider on self/children; no renderer on self/children`

#### `GameObject_861069271`
- GameObject name: `GameObject_861069271`
- objectId: `lamp_003`
- displayName: `Puzzle Lamp`
- description: Lamp that can change state or color in future puzzle logic
- labels / semantic_types: `lamp, light, feedback_object, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(0.0, 0.0, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.0, 1.0, 1.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.23,1.29,0.26)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `false`
- material names: `(none)`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `no renderer on self/children`

#### `lock_001`
- GameObject name: `lock_001`
- objectId: `lock_001`
- displayName: `Door Lock`
- description: Lock attached to the exit door
- labels / semantic_types: `lock, door_lock, puzzle_mechanism, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(-0.891, 0.96, 4.901)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.02, 0.05, 0.02)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.02,0.05,0.02)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `DarkLock.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `none`

#### `lock_002`
- GameObject name: `lock_002`
- objectId: `lock_002`
- displayName: `Table Drawer Lock`
- description: Lock attached to the second table drawer
- labels / semantic_types: `lock, door_lock, puzzle_mechanism, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(-0.044, 0.175, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.02, 0.05, 0.02)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.02,0.05,0.02)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `DarkLock.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `none`

#### `lock_003`
- GameObject name: `lock_003`
- objectId: `lock_003`
- displayName: `Cabinet Drawer Lock`
- description: Lock attached to the second cabinet drawer
- labels / semantic_types: `lock, door_lock, puzzle_mechanism, interactive`
- editable flag: `true`
- active flag: `true`
- position: `(-0.235, 0.129, 0.006)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.02, 0.05, 0.02)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.02,0.05,0.02)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `DarkLock.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `true`
- notes about possible issues: `none`

#### `room_floor`
- GameObject name: `room_floor`
- objectId: `room_floor`
- displayName: `Room Floor`
- description: Floor of the escape room test environment
- labels / semantic_types: `floor, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(-0.02, -0.05, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(10.0, 0.1, 10.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(10.00,0.10,10.00)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Wood.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_back`
- GameObject name: `wall_back`
- objectId: `wall_back`
- displayName: `Back Wall`
- description: Back wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(-3.026, 1.5, 4.95)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(4.04, 3.0, 0.2)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(4.04,3.00,0.20)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_back (1)`
- GameObject name: `wall_back (1)`
- objectId: `wall_back_1`
- displayName: `Back Wall`
- description: Back wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(2.512, 1.5, 4.95)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(4.832, 3.0, 0.2)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(4.83,3.00,0.20)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_back (2)`
- GameObject name: `wall_back (2)`
- objectId: `wall_back_2`
- displayName: `Back Wall`
- description: Back wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(-0.42, 2.574, 4.95)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(1.2, 0.85, 0.2)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(1.20,0.85,0.20)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_front`
- GameObject name: `wall_front`
- objectId: `wall_front`
- displayName: `Front Wall`
- description: Front wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(0.0, 1.5, -4.95)`
- rotation: `(0.0, 90.0, 0.0)`
- scale: `(0.2, 3.0, 10.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.20,3.00,10.00)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_left`
- GameObject name: `wall_left`
- objectId: `wall_left`
- displayName: `Left Wall`
- description: Left wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(-4.97, 1.5, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.2, 3.0, 10.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.20,3.00,10.00)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

#### `wall_right`
- GameObject name: `wall_right`
- objectId: `wall_right`
- displayName: `Right Wall`
- description: Right wall of the test room
- labels / semantic_types: `wall, room_structure, static`
- editable flag: `false`
- active flag: `true`
- position: `(4.93, 1.5, 0.0)`
- rotation: `(0.0, 0.0, 0.0)`
- scale: `(0.2, 3.0, 10.0)`
- parent object: ``
- children: `(none)`
- has Collider: `true`
- collider type and approximate bounds: `Box ~(0.20,3.00,10.00)`
- collider location: `self`
- has Rigidbody: `false`
- has Renderer: `true`
- material names: `Socket.mat`
- likely selectable by raycast: `true`
- likely semantic target for speech commands: `false`
- notes about possible issues: `none`

## 4. Drawer and Container Analysis
- No explicit `drawer*` GameObjects were found by name; drawers may be embedded in imported furniture meshes or named differently.

Container-like AIEditableObjects detected:
- `GameObject_190967584` labels=`basket, target, puzzle_mechanism` collider=`none`
- `GameObject_436017451` labels=`drawer, openable, interactive` collider=`none`
- `GameObject_480108160` labels=`drawer, openable, interactive` collider=`none`
- `GameObject_713582546` labels=`drawer, openable, interactive` collider=`none`
- `GameObject_1125636089` labels=`drawer, openable, interactive` collider=`none`
- `GameObject_1311336857` labels=`drawer, openable, interactive` collider=`none`
- `GameObject_1315326853` labels=`cabinet, container, openable, interactive` collider=`none`
- `GameObject_1967296781` labels=`drawer, openable, interactive` collider=`none`

## 5. Lock and Key Analysis
### Locks
- `lock_001` associated object=`not inferable` childOfControlled=`false` hasAI=`true` hasCollider=`true` followWhenOpened=`unknown`
- `lock_002` associated object=`not inferable` childOfControlled=`false` hasAI=`true` hasCollider=`true` followWhenOpened=`unknown`
- `lock_003` associated object=`not inferable` childOfControlled=`false` hasAI=`true` hasCollider=`true` followWhenOpened=`unknown`

### Keys
- `Menu/Canvas/Main Panel/Join Room Panel/Keyboard` color=`not inferable` parent=`Join Room Panel` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`
- `Menu/Canvas/Main Panel/Join Room Panel/Keyboard/Keyboard` color=`not inferable` parent=`Keyboard` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`
- `Menu/Canvas/Main Panel/New Room Panel/Keyboard` color=`not inferable` parent=`New Room Panel` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`
- `Menu/Canvas/Main Panel/New Room Panel/Keyboard/Keyboard` color=`not inferable` parent=`Keyboard` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`
- `Menu/Canvas/Main Panel/Set Name Panel/Keyboard` color=`not inferable` parent=`Set Name Panel` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`
- `Menu/Canvas/Main Panel/Set Name Panel/Keyboard/Keyboard` color=`not inferable` parent=`Keyboard` location=`(0.062, 1.277, 0.663)` hasAI=`false` hasCollider=`false` visible=`false` note=`should likely be independent if pickup target`

## 6. Painting and Note Analysis
- `GameObject_1891920693` visible=`false` selectable=`false` labels=`painting, decoration, clue_holder, interactive` parent=`` notes=`no collider on self/children; no renderer on self/children`
- `clue_note_002` visible=`true` selectable=`true` labels=`clue, note, puzzle_item` parent=`` notes=`none`
- `clue_note_001` visible=`true` selectable=`true` labels=`clue, note, puzzle_item` parent=`` notes=`none`

## 7. Selection and Raycast Readiness
- objects with AIEditableObject but no collider on self or children:
  - `GameObject_45509927`
  - `GameObject_190967584`
  - `GameObject_436017451`
  - `GameObject_480108160`
  - `GameObject_713582546`
  - `GameObject_1125636089`
  - `GameObject_1198313274`
  - `GameObject_1222747714`
  - `GameObject_1311336857`
  - `GameObject_1315326853`
  - `GameObject_1891920693`
  - `GameObject_1967296781`
  - `GameObject_2084505965`
- objects with collider but no AIEditableObject parent:
  - `Menu`
- objects where raycast may hit a child mesh without finding AIEditableObject unless GetComponentInParent is used:
  - none
- objects that are too small to select reliably:
  - `clue_note_002` bounds=`Box ~(0.01,0.28,0.20)`
  - `wall_left` bounds=`Box ~(0.20,3.00,10.00)`
  - `wall_right` bounds=`Box ~(0.20,3.00,10.00)`
  - `lock_001` bounds=`Box ~(0.02,0.05,0.02)`
  - `clue_note_001` bounds=`Box ~(0.01,0.28,0.20)`
  - `lock_003` bounds=`Box ~(0.02,0.05,0.02)`
  - `GameObject_861069271` bounds=`Box ~(0.23,1.29,0.26)`
  - `GameObject_1213554618` bounds=`Box ~(0.23,1.29,0.26)`
  - `wall_front` bounds=`Box ~(0.20,3.00,10.00)`
  - `GameObject_1738825464` bounds=`Box ~(0.23,1.29,0.26)`
  - `lock_002` bounds=`Box ~(0.02,0.05,0.02)`
  - `GameObject_1916706071` bounds=`Box ~(0.23,1.29,0.26)`
- objects whose collider is likely occluded:
  - `lock_001`
  - `lock_003`
  - `lock_002`
- objects that may need a larger proxy collider:
  - none obvious

## 8. Metadata / Label Issues
- `GameObject_45509927` -> `overly generic labels`
- `GameObject_436017451` -> `overly generic labels`
- `GameObject_480108160` -> `overly generic labels`
- `lock_001` -> `overly generic labels`
- `GameObject_713582546` -> `overly generic labels`
- `lock_003` -> `overly generic labels`
- `GameObject_861069271` -> `overly generic labels`
- `GameObject_1125636089` -> `overly generic labels`
- `GameObject_1198313274` -> `overly generic labels`
- `GameObject_1213554618` -> `overly generic labels`
- `GameObject_1311336857` -> `overly generic labels`
- `GameObject_1315326853` -> `overly generic labels`
- `GameObject_1738825464` -> `overly generic labels`
- `lock_002` -> `overly generic labels`
- `GameObject_1891920693` -> `overly generic labels`
- `GameObject_1916706071` -> `overly generic labels`
- `GameObject_1967296781` -> `overly generic labels`

## 9. Recommended Fixes
- hierarchy fixes: separate inherited DynamicCompiler/demo content from intended escape-room content more explicitly, ideally by root grouping rather than mixed top-level items.
- collider fixes: add or enlarge proxy colliders for very small semantic targets such as keys, notes, and lock elements; review child-only collider setups for precision and ease of selection.
- AIEditableObject fixes: review legacy/demo AIEditableObject entries that are unrelated to the escape-room experiment and ensure each intended semantic target has exactly one stable authoring anchor.
- label/description fixes: fill missing descriptions and labels, remove overly generic labels, and add lock ownership/container semantics where missing.
- selection algorithm fixes: continue relying on `GetComponentInParent<AIEditableObject>()`; consider filtering non-semantic collider-only runtime objects out of semantic raycasts.
- SceneContext fixes: validate whether inherited non-room AIEditableObjects should be excluded from exported scene snapshots before external runtime use.

## 10. Open Questions
- Which remaining AIEditableObjects are intentionally part of the escape-room authoring testbed versus inherited DynamicCompiler/demo content?
- Are there desk/cabinet drawers present under non-obvious names inside imported meshes?
- Should lock-to-door / lock-to-drawer ownership be encoded primarily by hierarchy or by semantic labels?
- Should hidden keys/notes stay parented under their containers until reveal time, or become free semantic pickup targets immediately?
- Should SceneContext include every AIEditableObject currently present, or only a curated escape-room subset?

## Runtime Findings
- `SceneRegistry` found: 1
- `InteractionContextProvider` found: 1
- `InteractionContextTransmitter` found: 1
- `SceneContextCompiler` found: 1
- `SceneContextTransmitter` found: 1

Potential missing runtime references detected:
- `DreamCodeVR2_RuntimeServices` `DreamCodeVR2.ContextBridge.InteractionContextTransmitter` missing/ref-zero fields: `m_CorrespondingSourceObject, m_PrefabInstance, m_PrefabAsset`
- `DreamCodeVR2_RuntimeServices` `DreamCodeVR2.ContextBridge.InteractionContextProvider` missing/ref-zero fields: `m_CorrespondingSourceObject, m_PrefabInstance, m_PrefabAsset, codeGenerationManager`
- `DreamCodeVR2_RuntimeServices` `DreamCodeVR2.ContextBridge.SceneRegistry` missing/ref-zero fields: `m_CorrespondingSourceObject, m_PrefabInstance, m_PrefabAsset`
- `DreamCodeVR2_RuntimeServices` `DreamCodeVR2.SceneContext.SceneContextCompiler` missing/ref-zero fields: `m_CorrespondingSourceObject, m_PrefabInstance, m_PrefabAsset`
- `DreamCodeVR2_RuntimeServices` `DreamCodeVR2.SceneContext.SceneContextTransmitter` missing/ref-zero fields: `m_CorrespondingSourceObject, m_PrefabInstance, m_PrefabAsset`