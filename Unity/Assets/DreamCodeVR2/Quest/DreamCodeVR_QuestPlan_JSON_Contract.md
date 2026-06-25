# DreamCodeVR QuestPlan JSON Contract

## Canonical Field Names
- `initial_setup[].object`
- `clues[].object`
- `tasks[].target`
- `tasks[].key`
- `tasks[].lock`
- `tasks[].object_to_create`
- `tasks[].target_anchor`

## Unity Parsing Notes
- Unity uses `JsonUtility` for deserialization.
- `QuestInitialSetupAction` and `QuestClueSpec` expose an internal normalized accessor named `ObjectReference`.
- The canonical JSON field remains `object`.
- Legacy `object_id` is still accepted temporarily as fallback and produces a warning.
- `clues[]` is the canonical source for clue note text.
- `initial_setup` is reserved for physical/setup actions such as placement, visibility, parenting, reset, and material changes.
- Legacy `SetClueText` inside `initial_setup` is still supported only as fallback when the same clue object is not present in `clues[]`.
- Initial setup anchors may be provided as either:
  - simple anchor names such as `drawer_inside_anchor`
  - fully qualified placement keys such as `cabinet_drawer_001.drawer_inside_anchor`

## Valid QuestPlan Example
```json
{
  "quest_id": "mock_server_contract_001",
  "mode": "llm_generated_placeholder",
  "title": "Server Contract Alignment Quest",
  "summary": "A server-style quest plan using canonical object fields and unique variable placements.",
  "final_key": "key_002",
  "drawer_key": "key_001",
  "tasks": [
    {
      "step": 1,
      "type": "StraightenAndMovePainting",
      "target": "painting_001",
      "description": "Straighten or move the painting to reveal the next lead."
    },
    {
      "step": 2,
      "type": "ReadClue",
      "target": "clue_note_002",
      "key": "key_001",
      "description": "Use the search key to reach the clue and read it carefully."
    },
    {
      "step": 3,
      "type": "CreateTextureAndPlaceObject",
      "object_to_create": "soccer_ball_001",
      "primitive": "sphere",
      "material": "soccer_ball_material",
      "target_anchor": "basket_001.basket_inside_anchor",
      "requires_planning": true,
      "description": "Create a soccer ball, give it the right look, and place it in the basket."
    },
    {
      "step": 4,
      "type": "UnlockDoorWithKey",
      "target": "door_001",
      "key": "key_002",
      "lock": "lock_001",
      "has_error_risk": true,
      "description": "Use the correct final key on the exit door."
    }
  ],
  "initial_setup": [
    {
      "action": "PlaceObject",
      "object": "key_001",
      "anchor": "table_001.desk_surface_anchor",
      "parent": "table_001"
    },
    {
      "action": "PlaceObject",
      "object": "key_002",
      "anchor": "cabinet_drawer_001.drawer_inside_anchor",
      "parent": "cabinet_drawer_001"
    },
    {
      "action": "PlaceObject",
      "object": "clue_note_002",
      "anchor": "table_drawer_002.drawer_inside_anchor",
      "parent": "table_drawer_002"
    }
  ],
  "clues": [
    {
      "object": "clue_note_001",
      "text_target": "Text (TMP)",
      "style": "vague_but_actionable",
      "text": "Two keys, two purposes. Some locks help you search, one lock leads outside. Try carefully."
    }
  ]
}
```

## Unique Placement Rule
- `key_001`, `key_002`, and `clue_note_002` cannot share the same initial placement anchor/container.
- The resolved placement key is:
  - `parent.anchor` when parent is supplied
  - `anchor` when no parent is supplied
- Hidden objects without `PlaceObject` do not count as occupying an anchor.

## Legacy Notes
- New mocks and new server-generated plans should not use `object_id`.
- New mocks and new server-generated plans should not place `SetClueText` inside `initial_setup`.
