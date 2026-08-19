# DreamCodeVR2-only cleanup manifest

## Deleted legacy assets

- `Unity/Assets/RoslynCSharp/` runtime compiler, examples, documentation and metadata.
- `Unity/Assets/Demos/` legacy demo scenes and assets.
- `Unity/Assets/Scenes/Scripts/TestRoslyn.cs` and `Unity/Assets/Scenes/SampleScene.unity`.
- `Unity/Assets/CodeGenerationManager.cs` and `Unity/Assets/ConversationalAgentManager.cs`.
- Legacy quest scenario/planner/applier/catalog classes and mock quest JSONs.
- Historical reports, duplicate audits and scene snapshots listed in the working-tree changes.

## Refactored

- The study scene removes NetworkId 94 components and is renamed to `DreamCodeVR2_RuntimeServices`.
- `SelectObjectRay` and ContextBridge now use direct selection only.
- Proposal DTO/UI removes proactive authoring.
- The UI bootstrap no longer creates the obsolete quest planner runtime.
- Quest plan data retains only the fixed/dynamic runtime fields.

The removed material was DreamCodeVR v1-only or superseded by the final DreamCodeVR2 runtime.
