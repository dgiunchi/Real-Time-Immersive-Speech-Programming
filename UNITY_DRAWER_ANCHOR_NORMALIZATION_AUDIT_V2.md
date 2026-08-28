# Drawer Anchor Normalization Audit V2

## DISCOVERY METHOD

Inspected the active scene `DreamCodeVR2_EscapeRoom_Testbed.unity`, every literal motion-anchor occurrence, prefab-instance `m_Modifications`, `m_AddedGameObjects`, `m_AddedComponents`, stripped-object records, source-prefab GUID `3f46a5fe3e59db445b9f5c6557b040f3`, and `Assets/Scrivanie e cassettiere/Prefabs/Cabinet .prefab`. Also inspected serialized `ExperimentalDrawerController` fields across scene and prefabs and the runtime bootstrap fallback lookup.

The cabinet prefab contains only three drawer source transforms: `C Drawer 1` (source ID `2006100300571712507`), `C Drawer 2` (`6442575328978680615`) and drawer `3` (`466115070560271274`), under source parent `Cabinet `. The active scene adds semantic components, colliders, `drawer_inside_anchor` children, locks and content children to this prefab instance. It contains no serialized `ExperimentalDrawerController`; the scene does, however, now contain a persistent shared cabinet motion-profile pair beneath the cabinet instance transform.

## CABINET CONTROLLER REFERENCES

| Drawer | Controller source | Closed ref | Open ref | Hierarchy / serialization source |
|---|---|---|---|---|
| cabinet_drawer_001 | Runtime `VerticalSliceRuntimeBootstrap.ConfigureDrawer` | Runtime `DrawerClosedAnchor_cabinet_drawer_001` | Runtime `DrawerOpenAnchor_cabinet_drawer_001` | Individual siblings under cabinet parent, initialized from the persistent profile delta. |
| cabinet_drawer_002 | Runtime bootstrap | Runtime `DrawerClosedAnchor_cabinet_drawer_002` | Runtime `DrawerOpenAnchor_cabinet_drawer_002` | Same. |
| cabinet_drawer_003 | Runtime bootstrap | Runtime `DrawerClosedAnchor_cabinet_drawer_003` | Runtime `DrawerOpenAnchor_cabinet_drawer_003` | Same. |

The per-drawer pairs are runtime-created, but the persistent profile pair is `CabinetDrawerClosedAnchor` / `CabinetDrawerOpenAnchor`, parented to the cabinet instance transform (fileID `729322093`). It is used only to obtain a displacement; no moving drawer references these shared physical transforms.

## CABINET CURRENT GEOMETRY

The persistent profile uses identity-equivalent 180° Y rotations under the 180°-rotated cabinet parent. Local closed is `(-0.224, 0.78225875, -0.021999955)`; local open is `(-0.454, 0.78225875, -0.021999955)`; local delta is `(-0.230, 0, 0)`, magnitude `0.230 m`. Its world delta is `(+0.230, 0, 0)`, magnitude `0.230 m`. The profile anchors do not overlap.

## CABINET REFERENCE

**cabinet_drawer_001** is the cabinet reference drawer by priority/order. Its persistent cabinet profile pair is valid, non-overlapping and has a shared-parent orientation compatible with the other cabinet drawers. No desk delta is reused; the coincident world magnitude is independently established by the cabinet profile.

## CABINET NORMALIZATION

| Drawer | Original delta | Final delta | Magnitude | Changed |
|---|---|---|---:|---|
| cabinet_drawer_001 | profile (+0.230, 0, 0) world | (+0.230, 0, 0) world | 0.230 m | Yes: distinct pair initialized |
| cabinet_drawer_002 | profile (+0.230, 0, 0) world | (+0.230, 0, 0) world | 0.230 m | Yes: distinct pair initialized |
| cabinet_drawer_003 | profile (+0.230, 0, 0) world | (+0.230, 0, 0) world | 0.230 m | Yes: distinct pair initialized |

## DESK RECHECK

The root-level manual reference pair remains:

`table_drawer_001`: closed `(-4.336, 0.78225875, -1.358)`, open `(-4.106, 0.78225875, -1.358)`, delta `(+0.230, 0, 0)`, magnitude `0.230 m`, identity anchor rotations.

`table_drawer_002` and `table_drawer_003` receive distinct runtime anchors and the reference **world-space delta**, never the reference open position. This is valid because they share the desk parent/orientation.

## LOOKUP ROBUSTNESS

The former `GameObject.Find("DrawerClosedAnchor")` / `GameObject.Find("DrawerOpenAnchor")` lookup was replaced. The reference resolver now searches only root transforms, rejects duplicate root names and leaves explicit controller references untouched where present. Cabinet drawers never use this root reference path.

## VALIDATION

- Controller motion remains world-anchor based; no local absolute position write was introduced.
- Desk reference is non-overlapping and 0.230 m long.
- Cabinet profile anchors are persistent and non-overlapping; each drawer receives its own non-shared pair with the same world displacement.
- The cabinet parent orientation proves the local -X / world +X conversion. Physical collision clearance remains a Scene View/device check.

## MANUAL CHECKS STILL REQUIRED

1. Confirm on device that each generated per-drawer pair clears the cabinet frame and adjacent drawers; static YAML cannot prove mesh collision clearance.
2. If per-drawer travel needs to differ from the shared 0.230 m profile, persist separate authored pair overrides before changing the profile.
