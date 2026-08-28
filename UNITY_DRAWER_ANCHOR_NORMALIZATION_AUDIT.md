# Drawer Anchor Normalization Audit

## CONTROLLER COORDINATE SPACE

`ExperimentalDrawerController` consumes `closedAnchor.position` and `openAnchor.position` in **world space**. Its motion coroutine interpolates the drawer transform from its current world position to the selected anchor world position using `Mathf.SmoothStep`. It never treats a displacement as an absolute `localPosition`. Anchor rotation is ignored unless `applyAnchorRotation` is enabled.

## DESK DRAWER REFERENCE

The serialized study-scene reference pair is the root-level manual pair used by `table_drawer_001` at runtime:

| Drawer | Closed world position | Open world position | Delta | Magnitude | Local delta relative to root | Direction |
|---|---|---|---|---:|---|---|
| table_drawer_001 | (-4.336, 0.78225875, -1.358) | (-4.106, 0.78225875, -1.358) | (+0.230, 0, 0) | 0.230 m | (+0.230, 0, 0) | world +X |

Both anchors are root transforms, so their serialized local positions equal their world positions. Rotations are identity. This pair remains the reference and is not repositioned by code.

## DESK DRAWER NORMALIZATION

| Drawer | Original serialized delta | Final runtime delta | Magnitude | Changed |
|---|---|---|---:|---|
| table_drawer_001 | (+0.230, 0, 0) | (+0.230, 0, 0) | 0.230 m | No |
| table_drawer_002 | NOT FOUND (no serialized pair) | reference delta applied to its own ClosedAnchor | 0.230 m | Runtime anchor creation/configuration only |
| table_drawer_003 | NOT FOUND (no serialized pair) | reference delta applied to its own ClosedAnchor | 0.230 m | Runtime anchor creation/configuration only |

The desk sibling drawers share the desk prefab parent/orientation, so copying the reference **delta** is geometrically compatible. No absolute open position is copied.

## CABINET DRAWER REFERENCE

No cabinet `DrawerClosedAnchor_*` / `DrawerOpenAnchor_*` pair is serialized in the current scene or prefab data. Therefore no valid cabinet reference or `cabinetOpeningDelta` can be proven. The implementation deliberately does **not** reuse the desk delta for cabinet drawers.

## CABINET DRAWER NORMALIZATION

| Drawer | Original serialized delta | Final delta | Changed |
|---|---|---|---|
| cabinet_drawer_001 | NOT FOUND | NOT NORMALIZED | No |
| cabinet_drawer_002 | NOT FOUND | NOT NORMALIZED | No |
| cabinet_drawer_003 | NOT FOUND | NOT NORMALIZED | No |

Runtime creates stable named anchors at each cabinet drawer's closed pose only when they are absent. The resulting overlap is intentionally rejected by `ExperimentalDrawerController` until the manually authored OpenAnchor pose is available; it is not treated as a valid travel distance.

## VALIDATION

- Desk reference pair exists and does not overlap; distance is above the controller 0.001 m minimum.
- Desk group validates distances with a 0.03 m tolerance and direction dot product of at least 0.95. It warns but does not fail minor float differences.
- `table_drawer_002` and `table_drawer_003` receive distinct anchor transforms; they never share `table_drawer_001`'s physical open transform.
- Cabinet pairs are missing from serialized data and therefore remain a required manual setup. No front/back direction can be proven statically, so none is guessed.
- No rotation normalization is performed.

## MANUAL CHECKS STILL REQUIRED

1. In Scene View, create or persist the manually authored pair for each cabinet drawer and move only its OpenAnchor along the real physical travel direction.
2. Verify the runtime-named desk anchors for `table_drawer_002` and `table_drawer_003` are persisted if distinct manually tuned travel is desired.
3. Test each drawer on device after anchor placement; confirm its open pose does not intersect a sibling drawer or travel into the furniture body.
4. Confirm the root reference anchors remain uniquely intended for `table_drawer_001`; `GameObject.Find` uses those exact names as the verified reference pair.
