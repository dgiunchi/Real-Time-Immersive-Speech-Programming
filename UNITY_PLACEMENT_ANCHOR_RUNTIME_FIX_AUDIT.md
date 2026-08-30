# Unity placement-anchor runtime fix audit

## SOURCE PATCH STATUS

The recursive patch was already present in `VerticalSliceRuntimeBootstrap` before this audit. The old implementation was `FindPlacementAnchor(Transform owner, string anchorName)`: it searched the owner's descendants recursively, then fell back to a scene-wide leaf-name search.

That fallback was unsafe for `drawer_inside_anchor`: the scene has six such leaf names. It also emitted an ambiguity event inside the resolver, after which the outer caller emitted a missing event, so one required anchor could generate two diagnostics.

The patch now uses `ResolvePlacementAnchor(AIEditableObject owner, string anchorName, out Transform point)`. It searches only the owner hierarchy, including inactive descendants, and returns `Found`, `Missing`, or `Ambiguous`. `RegisterPlacementAnchors()` is the only method that logs the resulting diagnostic.

## ACTUAL SCENE HIERARCHY

Active scene: `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`.

| Canonical owner | Transform hierarchy | Parent | Exact object name | Active | Duplicates |
| --- | --- | --- | --- | --- | --- |
| `table_001` | `Study Table / desk_surface_anchor` | instantiated `Study Table` root (Transform `1747401695`) | `desk_surface_anchor` | yes | none (one occurrence) |
| `table_drawer_003` | `Study Table / S.T Drawer 3 / drawer_inside_anchor` | instantiated `S.T Drawer 3` (Transform `1747401696`) | `drawer_inside_anchor` | yes | six scene-wide leaf-name occurrences; exactly one below this owner |
| `basket_001` | `Basket_03 / basket_inside_anchor` | instantiated `Basket_03` root (Transform `323660379`) | `basket_inside_anchor` | yes | none (one occurrence) |

The `AIEditableObject` IDs are attached to the corresponding instantiated prefab transforms: `table_001` to the Study Table root, `table_drawer_003` to `S.T Drawer 3`, and `basket_001` to the Basket root. No scene object was reparented.

## BOOTSTRAP CALL PATH

`VerticalSliceRuntimeBootstrap.Install()` is marked with `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`. It has one intentional gate: it returns when the active scene name is not `DreamCodeVR2_EscapeRoom_Testbed`.

For the active Escape Room scene, after logger and runtime dependencies are configured, it calls in this order:

1. `ConfigureVerticalSliceObjects(eventBus, context)`
2. `RegisterPlacementAnchors()`
3. `ValidateC1Capabilities()`
4. `StartFixedQuest(state)`

There is no early return between the scene-name gate and `RegisterPlacementAnchors()`. Owner discovery and descendant discovery use `FindObjectsInactive.Include` / `GetComponentsInChildren<Transform>(true)`, so inactive descendants do not suppress registration. The anchors are serialized in the scene, not created asynchronously after bootstrap.

The prior device log has no `PLACEMENT_ANCHOR_*` event. Given this call path, a newly built APK running this scene must now emit one result for each required placement anchor. Its absence after the retest would mean the device is not executing this bootstrap/scene path, rather than a failure to find the serialized desk anchor.

## CANONICAL IDS

Registration no longer derives an ID from any Transform hierarchy. It uses the resolved `AIEditableObject.objectId` plus the anchor leaf name:

| Scene owner / leaf | Registered `AuthoringAnchor.anchorId` |
| --- | --- |
| `table_001` / `desk_surface_anchor` | `table_001.desk_surface_anchor` |
| `table_drawer_003` / `drawer_inside_anchor` | `table_drawer_003.drawer_inside_anchor` |
| `basket_001` / `basket_inside_anchor` | `basket_001.basket_inside_anchor` |

Static hierarchy check: all three anchors are direct descendants of their respective semantic owners, so the owner-relative resolver resolves each one uniquely.

## FIX

Changed `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/VerticalSliceRuntimeBootstrap.cs` only.

- Kept the existing recursive owner-descendant search.
- Removed the scene-wide leaf-name fallback that could select or ambiguously reject another drawer's anchor.
- Added a typed result so one required anchor produces exactly one terminal diagnostic.
- Built `AuthoringAnchor.anchorId` from `owner.objectId + "." + anchorLeafName`.
- Kept inactive descendants eligible for registration.
- Did not change scene hierarchy, prefab structure, quest payload parsing, or server code.

## EXPECTED LOG EVENTS

At bootstrap, exactly one of the following events is emitted for every configured owner/leaf pair:

- `PLACEMENT_ANCHOR_REGISTERED` when exactly one matching descendant is found;
- `PLACEMENT_ANCHOR_MISSING` when its owner is absent or has no matching descendant;
- `PLACEMENT_ANCHOR_AMBIGUOUS` when more than one matching descendant exists below that owner.

For the current serialized hierarchy, the expected events include:

```text
PLACEMENT_ANCHOR_REGISTERED anchor_id=table_001.desk_surface_anchor
PLACEMENT_ANCHOR_REGISTERED anchor_id=table_drawer_003.drawer_inside_anchor
PLACEMENT_ANCHOR_REGISTERED anchor_id=basket_001.basket_inside_anchor
```

The remaining configured drawer owners should likewise emit one `PLACEMENT_ANCHOR_REGISTERED` event each.

## DEVICE RETEST

Build and install a fresh Quest APK, launch `DreamCodeVR2_EscapeRoom_Testbed`, then inspect the client JSONL log before starting C1/A1 or C1/A2.

Confirm the three registered events above appear before any `C1_QUEST_SPHERE_CREATE_FAILED`. If a failure remains, collect the same log from app launch through condition activation; the presence or absence of these bootstrap events will distinguish a deployed-scene/bootstrap issue from a quest activation issue.
