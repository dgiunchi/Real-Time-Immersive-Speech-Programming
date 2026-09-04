# Unity Canonical A/B/C End-to-End Protocol Audit

Audit date: 2026-09-02

Scope: current DreamCodeVR2 Unity client only. This is a code audit of the actual client/runtime behavior under fixed canonical streaming for C1/C2. No code was modified.

Evidence base:
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocol.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocolClient.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestConsequenceDispatcher.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestEventBus.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestEventDrivenValidator.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestObjectVisibilityController.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestRuntimeState.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestWorldControllers.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestWorldStateReporter.cs`
- `Unity/Assets/DreamCodeVR2/SceneContext/SceneContextCompiler.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/Tests/Editor/ExperimentalRuntimeEditModeTests.cs`
- repo audit docs already present in root for recent protocol changes

Important boundary:
- The client knows the fixed task wire format and the local success-condition vocabulary.
- The full six-task payloads for every A/B/C run are not hardcoded in the Unity repo; later task labels come from NID101 server messages.
- Where a task name comes from the user canonical list rather than a checked-in full wire sample, this report says so explicitly and audits the actual Unity execution/predicate/event path that would satisfy it.

## INITIAL STATE MATRIX

### Global canonical apply behavior

Order on reset:
1. `QuestConsequenceDispatcher.ReceiveReset`
2. `ExperimentConditionManager` session/set fields updated
3. `ExperimentalPlaythroughReset.ResetExperimentalPlaythrough()`
4. `QuestInstanceController.Apply(instance)`
5. `SceneContextTransmitter.SendSceneContextSnapshot("canonical reset applied")`
6. `QuestWorldStateReporter.ResetCompleted(...)`

So on canonical reset the initial `SceneContext` snapshot is sent before `RESET_COMPLETED`.

### Physical/setup categories

| Category | Actual Unity behavior at canonical apply |
|---|---|
| placements | `QuestInstanceController.ApplyPlacements` re-parents normal objects to authored anchors. Keys are normalized visually. Clue notes are explicitly excluded from relocation. |
| active/inactive objects | `ApplyInitialStates` supports `active` / `inactive`. Ordinary objects are `SetActive(bool)`. Locks may remain visible while semantically inactive via `QuestSemanticInactivityMarker`. |
| clue positions | `anchor_assignments` and `placements` are converted into `QuestPlacementBinding`, but `clue_note_*` is not physically moved. |
| clue visibility | `QuestNoteController.Configure(text, visible)` controls rendered text and `SetActive`. Reset path calls `ResetToDefault(false)`. |
| locks | `QuestLockController.Configure` or `SetLocked` sets semantic lock state and emits `LOCK_STATE_CHANGED`. |
| sphere profile | Runtime `sphere_001` can be created/reused, then `C1QuestSphereController.TrySetProfile("football"/"neutral")` sets semantic profile and emits `SPHERE_PROFILE_CHANGED`. |
| drawers | `ExperimentalDrawerController` controls motion/open state. Drawer open can reveal configured contents through `QuestDrawerContentsReveal`. |
| door | `QuestDoorController.TryOpen/TryClose` updates semantic open/closed state and local quest event only; no authoritative world event is emitted. |
| painting | `QuestPaintingController.ResetCrooked` resets physical pose and hides clue; later align emits authoritative `PAINTING_STATE_CHANGED`. |
| lamps | `SetLampState(active)` changes active/inactive semantics locally only. `TrySetColorProfile` emits authoritative `LIGHT_PROFILE_CHANGED`. |

### `clue_note_001` deep initial-state inspection

| Question | Actual current behavior |
|---|---|
| instantiated/available at reset? | Yes. It is a scene-authored object, not runtime-created. |
| positioned at `painting_001.clue_display_anchor`? | Not guaranteed by canonical apply code. Unity deliberately preserves the authored scene transform for clue notes and does not reposition them from quest payload anchor assignments. |
| active or inactive at reset? | Inactive. `QuestInstanceController.ResetControlledState` calls `QuestNoteController.ResetToDefault(false)`. |
| visible or hidden at reset? | Hidden via `SetActive(false)`. |
| what consequence currently makes it visible? | For the painting path, visibility is caused directly by `QuestPaintingController.CompleteAlignment` calling `clueToReveal.SetActive(true)`. Not by `QuestConsequenceDispatcher`. |
| does `REVEAL_OBJECT_IN_CONTAINER` make sense for this anchor? | Not really for the painting clue lifecycle. That consequence reparents the object to a container/anchor, while the clue-note path is intentionally authored-in-place and only hidden/revealed. |

## QUEST WORLD EVENT INVENTORY

Authoritative `QuestWorldStateEvent` types Unity can emit today:

| event_type | Source controller | semantic_state shape | When emitted |
|---|---|---|---|
| `RESET_COMPLETED` | `QuestWorldStateReporter.ResetCompleted` | object map of all known `AIEditableObject.objectId -> semantic string` | after canonical reset apply |
| `OBJECT_AVAILABILITY_GENERATION` | `QuestWorldStateReporter.MarkAvailable` | string `"available"` | every reveal/generation bump |
| `OBJECT_REVEALED` | `QuestWorldStateReporter.Revealed` | string `"revealed"` | reveal by drawer reward or consequence reveal |
| `LOCK_STATE_CHANGED` | `QuestWorldStateReporter.LockChanged` | string `"locked"` or `"unlocked"` | lock configure/unlock/reset/set |
| `SPHERE_PROFILE_CHANGED` | `QuestWorldStateReporter.SphereProfile` | string profile, currently `"football"` or `"neutral"` | sphere profile set/reset |
| `LIGHT_PROFILE_CHANGED` | `QuestWorldStateReporter.LightProfile` | string profile, currently `"green"` or `"default"` | lamp color-profile set |
| `PAINTING_STATE_CHANGED` | `QuestWorldStateReporter.PaintingStateChanged` | object map `{ "painting_001": "aligned"|"crooked" }` | painting semantic transition only |

Local task predicates with no matching authoritative world event today:
- `OBJECT_OPEN`
- `OBJECT_CLOSED`
- `DOOR_OPEN`
- `OBJECT_ACTIVE`
- `OBJECT_INACTIVE`
- `OBJECT_AT_ANCHOR`

Also missing as authoritative reveal evidence in one important path:
- `clue_note_001` reveal caused by painting alignment changes local state and `SceneContext`, but does not emit `OBJECT_REVEALED` / availability generation.

## SET A TASK MATRIX

Canonical list from user prompt:
- T1 painting alignment
- T2 sphere discovery
- T3 football + basket
- T4 Golden Key discovery
- T5 `lock_001` unlock
- T6 door open

| Task | Physical action that satisfies it | Owning controller | Local semantic state / predicate | SceneContext evidence | QuestWorldStateEvent | event_type | semantic_state payload | Local validator timing | Authoritative evidence guaranteed before `task_completed`? |
|---|---|---|---|---|---|---|---|---|---|
| A-T1 painting alignment | align `painting_001` to aligned pose or C1 `MOVE_TO_PRESET aligned` | `QuestPaintingController` | `PAINTING_ALIGNED` on `painting_001` | `semantic_state=aligned`, `is_aligned=true` | yes | `PAINTING_STATE_CHANGED` | `{ "painting_001":"aligned" }` | validator completes on same synchronous quest event publish | No strict wire guarantee; local event is after `SceneContext` and after event preparation, but `task_completed` can still be sent before the server physically receives the world event on NID102 |
| A-T2 sphere discovery | discover/reveal `sphere_001` in world | no dedicated discovery controller; usually visibility/runtime-object presence | likely `OBJECT_REVEALED` or availability-style task if server uses it | object appears in snapshot, `active=true`, `sphere_profile` set | partial only | only if reveal is performed through `Revealed(...)` | `"revealed"` plus availability generation | if task uses `OBJECT_REVEALED`, local completion depends on active hierarchy | Not guaranteed, and if discovery is satisfied only by presence/anchor without `Revealed(...)`, there may be no authoritative event at all |
| A-T3 football + basket | set sphere profile to football and place in `basket_001.basket_inside_anchor` | `C1QuestSphereController` plus `QuestPlacementMonitor` | usually `OBJECT_AT_ANCHOR` on `sphere_001`; profile is separate local semantic context | `sphere_profile=football`, `parent_anchor=basket_001.basket_inside_anchor` | profile yes, placement no | `SPHERE_PROFILE_CHANGED` only | `"football"` | if task predicate is `OBJECT_AT_ANCHOR`, completion happens on local `ObjectPlacedInZone` | No, because placement has no authoritative world event |
| A-T4 Golden Key discovery | reveal/find `key_001` or `key_002` depending server task wording; repo state indicates Set A drawer reward flow for hidden reward objects | `QuestDrawerContentsReveal` when drawer opens | usually `OBJECT_REVEALED` / availability generation | key active and parented in revealed container | yes for drawer-reveal path | `OBJECT_AVAILABILITY_GENERATION` + `OBJECT_REVEALED` | `"available"` then `"revealed"` | reveal happens before any local task completion keyed on reveal | Mostly yes for drawer reward path |
| A-T5 `lock_001` unlock | use correct key with exit lock | `QuestLockController` | `LOCK_UNLOCKED(lock_001)` | `is_locked=false`, `required_key_id`, `associated_target_object_id` | yes | `LOCK_STATE_CHANGED` | `"unlocked"` | validator completes from local `LockOpened` event before/around publish | Not guaranteed before receipt, but authoritative event is emitted in same method after scene snapshot |
| A-T6 door open | open `door_001` after unlock | `QuestDoorController` | `DOOR_OPEN` / `OBJECT_OPEN` | `is_open=true`, `semantic_state=open` | no | none | none | validator completes from local `ObjectStateChanged(detail=open)` | No |

## SET B TASK MATRIX

Full Set B six-task names are not hardcoded in the repo, but the client clearly models this family around:
- cabinet drawer search/unlock flow,
- `lock_002` / `lock_003` state transitions,
- drawer open/reclose,
- reveal/availability generation.

The following table audits the actual Unity protocol paths those canonical tasks rely on.

| Canonical B task family | Physical action | Controller | Local predicate | SceneContext field/state | QuestWorldStateEvent | event_type | semantic_state payload | `task_completed` timing | Authoritative-before-completed? |
|---|---|---|---|---|---|---|---|---|---|
| B-T1 find required key and unlock cabinet drawer | use key on `lock_003` | `QuestLockController` | `LOCK_UNLOCKED(lock_003)` | `is_locked=false` | yes | `LOCK_STATE_CHANGED` | `"unlocked"` | local completion on `LockOpened` | Not wire-guaranteed, but event is emitted |
| B drawer open transition task | open target drawer after unlock | `ExperimentalDrawerController` | `OBJECT_OPEN(drawer)` | `is_open=true`, `semantic_state=open` | no authoritative drawer-open event from the drawer controller path | none in current open path | none | completion on local `ObjectStateChanged(open)` | No |
| B consequence reveal task (user called out B-T3) | reveal object in container | `QuestConsequenceDispatcher` | usually object becomes active/revealed | active object + new parent anchor visible in `SceneContext` | yes | `OBJECT_AVAILABILITY_GENERATION` + `OBJECT_REVEALED` | `"available"` and `"revealed"` | ACK is sent after scene snapshot in dispatcher | yes for consequence-driven reveal |
| B lock state task involving `lock_002` | set or validate `lock_002` | `QuestLockController` / consequence `SET_LOCK_STATE` | `LOCK_UNLOCKED` if task uses it | `is_locked` state | yes | `LOCK_STATE_CHANGED` | `"locked"` or `"unlocked"` | depends on whether changed by use-with or consequence | emitted, but not guaranteed to arrive before completion if local validator triggers first |
| B reclose task | close drawer | `ExperimentalDrawerController` or consequence `CLOSE_DRAWER` | `OBJECT_CLOSED(drawer)` if used | `is_open=false` | no authoritative close event | none | none | local completion on quest event only | No |
| B availability-generation task | reveal hidden reward/content | `QuestWorldStateReporter.MarkAvailable/Revealed` | usually `OBJECT_REVEALED` | active object + parent anchor | yes | `OBJECT_AVAILABILITY_GENERATION` / `OBJECT_REVEALED` | string states | local completion can be based on active object | yes when reveal uses reporter |

## SET C TASK MATRIX

Canonical list from user prompt:
- lamp profile events
- note discovery
- sphere reveal
- sphere profile
- basket relation
- `lock_001` unlock
- door open

| Task family | Physical action | Controller | Local semantic state / predicate | SceneContext evidence | QuestWorldStateEvent | event_type | semantic_state payload | `task_completed` timing | Authoritative-before-completed? |
|---|---|---|---|---|---|---|---|---|---|
| C painting/clue opener | align `painting_001` | `QuestPaintingController` | `PAINTING_ALIGNED` | `semantic_state=aligned`, clue may be visible | yes for painting only | `PAINTING_STATE_CHANGED` | `{ "painting_001":"aligned" }` | local completion on quest event publish | Not guaranteed before receipt |
| C lamp profile task | set lamp profile to green/default | `QuestLampController.TrySetColorProfile` or consequence `SET_LIGHT_PROFILE` | if task checks profile, usually by semantic state or object state design | `light_profile`, `semantic_state` | yes | `LIGHT_PROFILE_CHANGED` | `"green"` / `"default"` | consequence ACK after scene snapshot | Yes for the profile event itself |
| C note discovery | reveal/read clue note | usually `QuestConsequenceDispatcher.REVEAL_OBJECT_IN_CONTAINER` or direct note active state | `OBJECT_REVEALED(clue)` | note active in snapshot | only if reveal uses reporter | `OBJECT_REVEALED` | `"revealed"` | if explicit discovery task, completion requires active object | Not guaranteed for painting-driven clue reveal because that path emits no reveal world event |
| C sphere reveal | reveal `sphere_001` | consequence or runtime-object presence | likely `OBJECT_REVEALED` / availability | active + present in snapshot | only if reveal routed through reporter | `OBJECT_REVEALED` | `"revealed"` | local completion from active state | Depends on reveal path |
| C sphere profile | set sphere profile | `C1QuestSphereController` or consequence `SET_SPHERE_PROFILE` | profile in semantic state | `sphere_profile` | yes | `SPHERE_PROFILE_CHANGED` | `"football"` / `"neutral"` | local completion only if task is actually profile-based, not anchor-based | Yes for profile only |
| C basket relation | place sphere in basket anchor | `QuestPlacementMonitor` | `OBJECT_AT_ANCHOR(sphere_001,basket_001.basket_inside_anchor)` | `parent_anchor=basket_001.basket_inside_anchor` | no | none | none | completion on local `ObjectPlacedInZone` | No |
| C `lock_001` unlock | use correct key with exit lock | `QuestLockController` | `LOCK_UNLOCKED(lock_001)` | `is_locked=false` | yes | `LOCK_STATE_CHANGED` | `"unlocked"` | completion on local `LockOpened` | Not guaranteed before receipt |
| C door open | open exit door | `QuestDoorController` | `DOOR_OPEN(door_001)` | `is_open=true`, `semantic_state=open` | no | none | none | completion on local `ObjectStateChanged(open)` | No |

## EVENT ORDERING

### Actual controller-level orderings

Painting align:
1. physical pose set/validated
2. semantic state set locally
3. `SceneContext` snapshot
4. `QuestWorldStateEvent(PAINTING_STATE_CHANGED)` prepared/sent
5. local `QuestEventBus.ObjectStateChanged`
6. `QuestEventDrivenValidator` may mark complete
7. `AuthoringProtocolClient.SendTaskCompleted`

Lock unlock:
1. physical lock boolean changes
2. key insertion pose applied
3. local `QuestEventBus.LockOpened`
4. `SceneContext` snapshot via `Publish("unlocked")`
5. `QuestWorldStateEvent(LOCK_STATE_CHANGED)`
6. validator may complete from step 3 and send `task_completed`

Door open:
1. physical door leaf rotates
2. semantic open state set
3. local `QuestEventBus.ObjectStateChanged(open)`
4. `SceneContext` snapshot
5. validator may complete
6. `task_completed`
7. no authoritative world event

Lamp active/inactive:
1. local active flag changes
2. local semantic state set
3. local `QuestEventBus.ObjectStateChanged(active/inactive)`
4. `SceneContext` snapshot
5. validator may complete
6. `task_completed`
7. no authoritative world event

Lamp profile:
1. physical light color changes
2. semantic profile set
3. `QuestWorldStateEvent(LIGHT_PROFILE_CHANGED)`
4. `SceneContext` snapshot
5. no fixed canonical validator path in this code unless server task uses a compatible condition

Sphere profile:
1. physical materials change
2. semantic profile set
3. `QuestWorldStateEvent(SPHERE_PROFILE_CHANGED)`
4. `SceneContext` snapshot
5. no placement proof yet

Placement to basket:
1. object parent set to anchor
2. local `QuestEventBus.ObjectPlacedInZone`
3. `SceneContext` snapshot
4. validator may complete
5. no authoritative world event

### Ordering conclusion

The authoritative world event is not guaranteed to be received by the server before `ExperimentStateEvent/task_completed` for any task whose validator completes from a local quest event before or without a matching authoritative world-state emission. The client only guarantees same-method local ordering, not network arrival ordering.

## CONSEQUENCE APPLICATION

Supported `QuestConsequenceInstruction` types today:

| instruction type | Controller/path | Physical effect | ACK timing |
|---|---|---|---|
| `SET_LOCK_STATE` | `QuestLockController.SetLocked` | lock locked/unlocked | after apply, after dispatcher scene snapshot |
| `SET_LIGHT_PROFILE` | `QuestLampController.TrySetColorProfile` | lamp color/profile change | after apply, after dispatcher scene snapshot |
| `SET_OBJECT_VISIBILITY` | direct `GameObject.SetActive` | visible/hidden | after apply, after dispatcher scene snapshot |
| `SET_CLUE_TEXT` | `QuestNoteController.Configure` | note text set, visibility preserved from activeSelf arg | after apply, after dispatcher scene snapshot |
| `REVEAL_OBJECT_IN_CONTAINER` | direct reparent + activate + optional key normalization + `reporter.Revealed` | object moved into container anchor and shown | after apply, after dispatcher scene snapshot |
| `CLOSE_DRAWER` | `ExperimentalDrawerController.TryClose` | drawer closes physically | after close completes and dispatcher scene snapshot |
| `SET_SPHERE_PROFILE` | `C1QuestSphereController.TrySetProfile` | sphere material/profile change | after apply, after dispatcher scene snapshot |

Consequence application problems:
- `REVEAL_OBJECT_IN_CONTAINER` is a bad fit for `clue_note_001` if the intended lifecycle is "authored at painting anchor, hidden until T1". The codebase explicitly preserves clue-note transforms and does not treat clue-note anchor assignments as relocation instructions.
- `SET_OBJECT_VISIBILITY` and `SET_CLUE_TEXT` do not emit authoritative world-state events of their own. They rely on later snapshots, not event evidence.
- `CLOSE_DRAWER` has no authoritative close/open world event; the ACK can be success even though the server receives no dedicated drawer-state transition event.

## CLUE_NOTE_001 LIFECYCLE

Clean intended lifecycle from current code comments and controller behavior:
- authored in scene near/below painting
- hidden at canonical reset
- text configured from quest payload if present
- revealed when painting alignment succeeds

Actual current implementation:
1. `clue_note_001` exists as an authored scene object.
2. Canonical reset calls `QuestNoteController.ResetToDefault(false)`, which restores its original parent/transform/text and hides it.
3. If the incoming quest instance contains clue text, `QuestInstanceController.Apply` calls `QuestNoteController.Configure(text,false)`, which sets quest text and keeps it hidden.
4. `ApplyPlacements` refuses to move `clue_note_001` even if the payload contains an anchor assignment.
5. When `painting_001` aligns, `QuestPaintingController.CompleteAlignment` directly calls `clueToReveal.SetActive(true)`.
6. That reveal does not go through `QuestWorldStateReporter.Revealed`, so no authoritative reveal/availability event is emitted for `clue_note_001`.

Discrepancy versus clean server-facing lifecycle:
- physically, the note is already in the scene and only hidden/revealed
- semantically, the payload may describe an anchor assignment, but Unity treats that anchor as descriptive context, not a relocation command
- the reveal is local-only plus `SceneContext`, not an authoritative reveal event

## LOCAL VS SERVER EVIDENCE GAPS

Distinct client/server contract gaps identified: 6

1. `OBJECT_OPEN` / drawer-open tasks complete locally without any authoritative `QuestWorldStateEvent`.
2. `DOOR_OPEN` / door-open tasks complete locally without any authoritative `QuestWorldStateEvent`.
3. `OBJECT_AT_ANCHOR` / basket-placement tasks complete locally without any authoritative `QuestWorldStateEvent`.
4. `OBJECT_ACTIVE` / `OBJECT_INACTIVE` lamp-state tasks complete locally without any authoritative `QuestWorldStateEvent`.
5. `clue_note_001` reveal caused by painting alignment has no authoritative reveal/availability event.
6. Consequence-driven visibility/text changes (`SET_OBJECT_VISIBILITY`, `SET_CLUE_TEXT`, `CLOSE_DRAWER`) ACK successfully without dedicated authoritative transition evidence for the changed state.

## TASK STREAMING

Verified client behavior for fixed next-task-only delivery:

1. `AuthoringProtocolClient.ProcessMessage` receives `NextTaskGenerated`.
2. In C1/C2, `HandleNextTaskGenerated` converts the task and either stores a pending full quest instance or a pending task-only payload.
3. `NextTaskActivationRequest(task_id)` triggers `HandleNextTaskActivation`.
4. If a full instance is pending, `QuestInstanceController.Apply(...)` runs and `runtimeState.SetAwaitingServerTask(true)` preserves streamed progression behavior.
5. If only a task is pending, `QuestInstanceController.ActivateServerTask(...)` appends the task to the current runtime plan without resetting world state.
6. `QuestRuntimeState.AdvanceToNextTask` then waits for the server-approved successor; no local synthesis of the next fixed task occurs.

Fallback:
- If `NextTaskActivationRequest` arrives without a matching `NextTaskGenerated`, `FixedQuestActivationFallback.TryCreate` can synthesize only the first canonical task for known instances.
- This fallback is intentionally narrow and does not create later tasks.

Conclusion:
- next-task-only delivery is implemented
- later fixed tasks preserve world state if sent task-only
- no local progression beyond the currently activated task

## MISMATCHES

Top mismatches:
- local validator supports more success predicates than the world-event protocol proves
- clue-note anchor assignments are accepted by the wire converter but intentionally ignored physically for clue-note transforms
- painting T1 reveals `clue_note_001` visually without authoritative reveal evidence
- drawer open/close are visible in `SceneContext` but absent from authoritative event inventory
- basket placement is visible in `SceneContext` but absent from authoritative event inventory
- lamp active/inactive is locally validatable but absent from authoritative event inventory

## RECOMMENDED PATCH ORDER

1. Add authoritative world events for `door open/closed`, `drawer open/closed`, and `object at anchor`.
2. Add authoritative world events for lamp `active/inactive`.
3. Route painting-driven `clue_note_001` reveal through a canonical reveal reporter path so visibility and availability evidence are emitted.
4. Align consequence ACK paths with authoritative transition emission for `SET_OBJECT_VISIBILITY`, `SET_CLUE_TEXT`, and `CLOSE_DRAWER` where needed.
5. Recheck task-completion ordering after the missing world events exist, so server evidence and local completion no longer diverge.

