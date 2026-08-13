# Unity authoring runtime audit

Audit in sola lettura del repository e della scena `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`. Stato osservato: 2026-08-10. **IMPLEMENTED** indica codice presente; **NOT WIRED** indica codice non serializzato nella scena; **NOT VERIFIED** indica comportamento non eseguito in Unity Editor.

## 1. Current study runtime architecture

Il GameObject di scena `DreamCodeVR2_RuntimeServices` contiene `SceneRegistry`, `InteractionContextProvider`, `InteractionContextTransmitter` (99), `SceneContextCompiler` e `SceneContextTransmitter` (100). È l’unico runtime sperimentale effettivamente serializzato nella Escape Room.

`ExperimentConditionManager`, `StudyConfiguration`, `AuthoringProtocolClient`, `AuthoringActionExecutor`, `AuthoringUndoManager`, `AuthoringProposalPresenter`, `ExperimentTelemetry`, `ExperimentalPlaythroughReset`, `PredefinedVoiceCommandExecutor`, `QuestEventBus`, `QuestEventDrivenValidator`, `RuntimeTaskValidator` e `DynamicStoryTaskController` esistono in `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/` e `.../Quest/`, ma non compaiono nella scena. Sono quindi **IMPLEMENTED / NOT WIRED** e richiedono un GameObject e riferimenti Inspector. `DreamCodeVRAuthoringUIBootstrap` crea UI/quest runtime solo nella Escape Room, ma non crea tali componenti sperimentali.

## 2. Condition implementation

| Condizione | Codice abilitato | Codice disabilitato | UI/voce/progressione | Stato |
| --- | --- | --- | --- | --- |
| C1 `VoiceCommandBaseline` | `MicrophoneCapture.sendToServer=true`; `PredefinedVoiceCommandExecutor` accetta messaggi solo in C1 | `AuthoringProtocolClient.ReceiveProposal`/`Execute` rifiutano authoring tramite `IsAuthoringAvailable` | UI authoring resta visibile; PTT/STT restano attivi; nessun percorso fixed C1 specifico | **PARTIAL / NOT WIRED** |
| C2 `PlayerAuthoring` | proposal, execute, undo dell’authoring strutturato | proposte `proactive` rifiutate | UI proposal esiste; quest usa `QuestRuntimeState`/mock plan | **PARTIAL / NOT WIRED** |
| C3 `DynamicStorytelling` | C2 + `DynamicStoryTaskController` dopo `TaskCompleted` | proposte proattive rifiutate | mostra “Preparing the next objective...”; attende `next_task` | **PARTIAL / NOT WIRED** |

L’unico residuo mixed-initiative è il campo `AuthoringProposal.proactive` e il testo condizionale in `AuthoringProposalPresenter`; il client ora rifiuta una proposta proattiva, quindi non è un comportamento attivo.

## 3. AuthoringActionExecutor

`Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringActionExecutor.cs` gestisce:

| ID | Handler | Parametri | Check | Undo / SceneContext | Stato |
| --- | --- | --- | --- | --- | --- |
| `SET_PROPERTY:color` | `SetProperty` | target, hex `value` | editable, `SET_PROPERTY`, property | colore materiale; sì / sì | **IMPLEMENTED** |
| `visible`, `active` | `SetProperty` | bool `value` | `canHide`/`canDeactivate`, questCritical | activeSelf; sì / sì | **IMPLEMENTED** |
| `kinematic`, `gravity_enabled` | `SetProperty` | bool | Rigidbody + property | Rigidbody; sì / sì | **IMPLEMENTED** |
| `scale` | `SetProperty` | `numericValue` | min/max capability | transform scale; sì / sì | **IMPLEMENTED** |
| `SET_AFFORDANCE` | `SetAffordance` | operation/value | operation + forbidden affordances | vedi §4; sì / sì | **PARTIAL** |
| `ADD_BEHAVIOR` | `AddBehavior` | operation/behaviorId | allowlist behavior | AddComponent; destroy undo / sì | **IMPLEMENTED** |
| `CREATE_OBJECT` | `CreateObject` | type, anchorId | anchor/type/occupancy | primitive + metadata; destroy / sì | **IMPLEMENTED** |
| `RELOCATE_OBJECT` | `Relocate` | target, anchorId | canMove, anchor | parent/pose; sì / sì | **IMPLEMENTED** |
| `TOGGLE_STATE` | `ToggleState` | value | operation allowlist | `AuthoringSemanticState`; sì / sì | **IMPLEMENTED** |
| `LINK_OBJECTS` | `Link` | target/secondary/operation | canLink/object exists | `AuthoringObjectLink`; sì / sì | **PARTIAL** |

Ogni `Execute` blocca duplicate actionId e azione concorrente, controlla oggetto/editabilità e richiama lo snapshot SceneContext anche sul fallimento. L’ack viene inviato da `AuthoringProtocolClient` come `execution_result`; non è presente un rollback transazionale per azioni multi-step perché gli handler attuali sono singolo-step.

## 4. Affordance implementation

| Affordance | Modifica reale | Undo | SceneContext | Stato |
| --- | --- | --- | --- | --- |
| `grabbable` | solo bool in `AuthoringAffordanceState` | sì | `player_authored_affordances` | **metadata only** |
| `movable` | solo bool metadata | sì | sì | **metadata only** |
| `interactable` | solo bool metadata | sì | sì | **metadata only** |
| `collision_enabled` | `Collider.enabled` figli | sì | non come affordance; compare in `components` | **IMPLEMENTED** |
| `gravity_enabled` | `Rigidbody.useGravity` | sì | component list, non valore | **PARTIAL** |
| `kinematic` | `Rigidbody.isKinematic` | sì | component list, non valore | **PARTIAL** |

Critico: **grabbable non collega `AuthoringAffordanceState` a `Ubiq.XR.IGraspable` o a un grasper reale**. Non rende l’oggetto afferrabile dall’utente.

## 5. Predefined voice command implementation

`VoiceCommandCapabilities` dichiara `OPEN`, `CLOSE`, `ACTIVATE`, `DEACTIVATE`, `MOVE_TO_PRESET`, `USE_WITH`; `PredefinedVoiceCommandExecutor` chiama un riferimento Inspector `PredefinedVoiceCommandTarget`.

L’adapter implementa `Open/Close` con GameObject open/closed, `SetActiveState`, `MoveToPreset` (Transform up/down) e `UseWith` (set semantic state `used`). Sono adapter generici **IMPLEMENTED**, non `DrawerInteraction`, `LampController` o `PlatformController`. Nessun `VoiceCommandCapabilities`/target è serializzato nella scena: OPEN/CLOSE, ACTIVATE, preset e use-with sono tutti **NOT WIRED / NOT VERIFIED** per l’Escape Room.

## 6. Behavior runtime

| Classe | Parametri / lifecycle | Undo | SceneContext | Stato |
| --- | --- | --- | --- | --- |
| `AuthoringRotateBehavior` | `degreesPerSecond`, Update | destroy | nome active behavior | **IMPLEMENTED** |
| `AuthoringMoveBetweenAnchorsBehavior` | Transform first/second, speed; non configurati dall’action | destroy | sì | **PARTIAL** |
| `AuthoringBlinkBehavior` | renderers cache, interval | destroy | sì | **IMPLEMENTED** |
| `AuthoringFollowTargetBehavior` | target/speed non configurati | destroy | sì | **PARTIAL** |
| `AuthoringContactTrigger` | targetObjectId; attiva sé stesso | destroy | sì | **PARTIAL** |
| `AuthoringProximityTrigger` | target/radius non configurati | destroy | sì | **PARTIAL** |
| `AuthoringTaskCompletionTrigger` | bool/manual `Trigger` | destroy | sì | **PARTIAL** |
| `AuthoringObjectLink` | source/target/operation; `Activate` solo `activate` | destroy | non nel DTO come link | **PARTIAL** |

## 7. Scene modification runtime

Le capability sono `AuthoringCapabilities` (string array e flag); gli ack e telemetry sono emessi da `AuthoringProtocolClient`/`ExperimentTelemetry`. Set property, create, relocate, state e link sono concreti come §3. Affordance è in gran parte metadata. CREATE produce primitivi Cube/Sphere anche per `bridge_segment`/`platform`; non crea geometria o comportamento dedicato. `LINK_OBJECTS` non viene attivato automaticamente.

## 8. SceneContext output

`SceneObjectSummary` emette: `id`, nomi, semantic_types/labels, description, position/rotation/scale, active/editable, parent_id, materials, components, available_operations, allowed_editable_properties, allowed_behaviors, quest_critical, semantic_state, runtime_created, active_authoring_behaviors, parent_anchor, currently_held, player_authored_affordances, created_by_action_id, created_during_task_id.

Mancano: predefined voice commands, editable affordances allowlist, physics property values, protected current task, held-state reale, link list/stato, authoring operation scope per task. `currently_held` è sempre `false`; `parent_anchor` usa `GetComponentInParent` e può includere il proprio parent. C’è un invio periodico ogni 15 s e metodi di invio immediato chiamati dall’executor.

## 9. Quest integrity protection

`questCritical`, protected properties e forbidden affordances esistono sia in `AuthoringCapabilities` sia in `QuestTaskSpec`. `AuthoringActionExecutor.ViolatesActiveTaskProtection` controlla l’attuale `QuestRuntimeState.GetCurrentTask()` solo per oggetti elencati in `protectedDuringTask`, disattivazione/nascondimento, `TOGGLE_STATE`, proprietà e affordance proibite. `successState` è solo dato, non usato.

| Caso | Esito attuale |
| --- | --- |
| rendere final door grabbable | **non bloccato** salvo config task esplicita forbidden |
| disabilitare lock | bloccato solo se lock in `protectedDuringTask` e property protetta / questCritical path active/visible |
| unlock lock direttamente | `TOGGLE_STATE` bloccato solo se lock protetto; semantic state non equivale a lock reale |
| teleport key a successo | `RELOCATE_OBJECT` non blocca in base a success state; dipende solo da anchor | 
| deactivate required object | bloccato per `caps.questCritical` e `active=false`, o protected task |

Quindi la protezione è **PARTIAL**, non un QuestIntegrityValidator completo.

## 10. Quest events

Event enum: `ObjectPickedUp`, `ObjectDropped`, `ObjectPlacedInZone`, `ObjectStateChanged`, `ButtonPressed`, `LockOpened`, `ObjectCreated`, `BehaviorAdded`, `LinkActivated`, `HintRequested`, `IncorrectAttempt`, `TaskStarted`, `TaskCompleted`.

Produttori reali: `QuestRuntimeState` pubblica TaskStarted/Completed, ObjectCreated, ObjectPlacedInZone, LockOpened, HintRequested, IncorrectAttempt. `QuestEventDrivenValidator` è il solo consumer e completa quattro tipi task; non produce eventi da collider/grab/door/drawer. Nessun oggetto scena è cablato ai producer. `ObjectGrabbed`, `ObjectReleased`, `ObjectPlacedAtAnchor`, `DrawerOpened`, `AffordanceChanged` non esistono esattamente con quei nomi o non hanno producer. F3 manuale resta in `QuestScenarioController` senza guardia debug: quindi è ancora disponibile nel percorso scena.

## 11. Fixed task progression

I mock sono `Unity/Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/*.json`; `QuestScenarioController` carica `MockQuestA_Ball`, `MockQuestB_Cube`, `MockQuestDebug`, `MockQuest_ServerContract`. `QuestRuntimeState.AdvanceToNextTask` attiva il primo task `NotStarted`; `QuestEventDrivenValidator` lo chiama quando riconosce i suoi quattro casi. Non esiste `QuestSequence` C1/C2 dedicato né wiring condizionale: C1/C2 *potrebbero* condividere lo stesso `QuestPlan`, ma non è configurato come requisito runtime.

## 12. C3 runtime task support

`NextTaskSpec` contiene id/titolo/istruzione/tipo/oggetti/conditions/dependencies/protectedObjects/scope/narrative. `RuntimeTaskValidator` allowlista condition types; esegue realmente OBJECT_AT_ANCHOR, OBJECT_HAS_STATE, OBJECT_HAS_AFFORDANCE, LINK_ACTIVE, BEHAVIOR_ACTIVE, ALL/ANY. OBJECT_GRABBED è sempre false; OBJECT_USED_WITH e SEQUENCE_COMPLETED sono allowlist ma non valutati.

`DynamicStoryTaskController` ascolta `QuestRuntimeState.TaskCompleted`, solo in C3 invia SceneContext + `task_completed`, imposta `WaitingForNextTask`, e accetta/acknowledge `next_task`. Non registra `NextTaskSpec` nella `QuestRuntimeState`, non costruisce subscriptions/validator lifecycle, né completa il task dinamico. End-to-end C3 è **BLOCKED**.

## 13. Actual Escape Room object audit

`drawer_001` non è presente; sono presenti `table_drawer_001` e `cabinet_drawer_001`.

| id | GameObject/path verificabile | AIEditable / collider / Rigidbody | interaction | voice/capabilities/anchor | C1 / C2-C3 |
| --- | --- | --- | --- | --- | --- |
| door_001 | prefab instance, root `door_001` | AIEditable sì; collider/Rigidbody prefab **not verified** | nessuno aggiunto | nessuno | no / no |
| lock_001 | `door_001/lock_001` | AIEditable sì, BoxCollider sì, Rigidbody no | nessuno | nessuno | no / no |
| key_001 | prefab instance `key_001` | AIEditable sì; collider/Rigidbody prefab **not verified** | nessuno aggiunto | nessuno | no / no |
| lamp_001 | prefab instance `lamp_001` | AIEditable sì, BoxCollider sì, Rigidbody no | nessuno | nessuno | no / no |
| table_drawer_001 | serialized AIEditable at scene lines ~23100 | AIEditable sì; collider/Rigidbody **not verified** | nessuno | nessuno | no / no |

Nessuno degli oggetti ispezionati ha `AuthoringCapabilities`, `VoiceCommandCapabilities`, `AuthoringAnchor` o `questCritical` serializzato; gli attributi di AIEditable (`editable: 1`) non sono capability runtime.

## 14. Actual grabbing implementation

Il progetto contiene il sistema legacy `Ubiq.XR`: `IGraspable`, `DefaultGraspable` (Require Rigidbody, Grasp/Release vuoti) e `GraspableObjectGrasper` (`GripPress`, trigger enter/exit, chiama `IGraspable.Grasp/Release`). Non è stato trovato nessun `DefaultGraspable` o altra implementazione `IGraspable` nella Escape Room serializzata. Non esistono callback QuestEventBus. Un adapter dovrebbe implementare `IGraspable` su oggetti con Rigidbody e pubblicare ObjectPickedUp/ObjectDropped; oggi è **MISSING**.

## 15. Reset reliability

`ExperimentalPlaythroughReset` salva in Start: pose/scale/active, Rigidbody kinematic/gravity, collider enabled e colori. Reset distrugge oggetti con label `runtime_created`, behavior/link Authoring, poi resetta quest, undo, action IDs e pending proposal.

Non ripristina `AuthoringAffordanceState`, `AuthoringSemanticState`, occupancy anchor, componenti create per affordance, stato held, telemetry session/file, stati `VoiceCommandTarget`, NextTaskSpec/waiting state, materiali diversi dal colore, parent per oggetti non relocati, messaggi rete già in transito. È **PARTIAL / NOT WIRED**.

## 16. Scene API mapping

| Unity capability | Candidate Scene API | Readiness |
| --- | --- | --- |
| color/visibility/active/physics/scale | `setProperty` | Partial |
| metadata affordance / collider | `setAffordance` | Partial |
| primitive at named anchor | `createObject` | Partial |
| move to named anchor | `relocateObject` | Partial |
| semantic string | `setSemanticState` | Partial |

## 17. Behavior API mapping

| Unity capability | Candidate Behavior API | Readiness |
| --- | --- | --- |
| allowlisted AddComponent | `addBehavior` | Partial |
| undo destroy created behavior | `removeBehavior` | Partial |
| configure targets/anchors | `configureBehavior` | Missing |
| one activate link | `createLink` | Partial |
| inspect/remove existing link | `get/removeLink` | Missing |

## 18. Vertical slice readiness

| Slice | Readiness | Exact blocker |
| --- | --- | --- |
| C1 “Open the drawer” | **BLOCKED** | no `drawer_001`; no voice capability/target wired; server command flow exists only in code |
| C2 “Make drawer grabbable” | **BLOCKED** | no capabilities/protocol runtime scene wiring; grabbable changes only metadata, not Ubiq grab |
| C3 C2 + next task | **BLOCKED** | all C2 blockers + NextTaskSpec not integrated into QuestRuntimeState/completion |

## 19. Tests

Presenti: `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/Tests/Editor/AuthoringActionExecutorEditModeTests.cs` (duplicate action, scale bounds, missing anchor, quest critical deactivation) e `ContextBridgeEditModeTests.cs`. Non esistono test per predefined commands, real affordances/grab, undo categories, automatic completion, dynamic activation, reset o condition switching. Test Unity **NOT RUN**.

## 20. Concrete recommendation

**A. Scene API già supportabile:** property, collider/physics state, named-anchor creation/relocation, semantic state e snapshot, purché con capability Inspector.

**B. Behavior API già supportabile:** installazione/rimozione di una allowlist limitata, con parametri solo parzialmente configurabili.

**C. Necessario per vertical slice:** serializzare un runtime C1/C2/C3 nella scena; collegare `drawer_001` o rinominare il target; implementare adapter reale `IGraspable`/grab events; cablare voice target; creare fixed QuestPlan condiviso; integrare NextTaskSpec con task runtime; completare task-integrity e reset.

**D. Solo Inspector wiring:** voice target, capability, anchor, executor/protocol/telemetry/runtime references; nessuno è oggi presente nella Escape Room.

**E. Da non implementare ancora:** arbitrary C#, reflection di componenti, espressioni task arbitrarie, auto-proposte mixed-initiative e tre scene sperimentali separate.

## Post-Implementation API Runtime Status

Since the audit, the repository contains `ExperimentalGrabbableAdapter`, `ExperimentalDrawerController`, `VerticalSliceRuntimeBootstrap`, `SceneApiExecutor` and `BehaviorApiExecutor`. The bootstrap runs only for the existing Escape Room scene and creates/wires an `ExperimentalAuthoringRuntime` GameObject at scene load; it does not create a second experimental scene.

`table_drawer_001` is configured at runtime as the selected drawer, with C1 OPEN/CLOSE via `ExperimentalDrawerController`; its C2/C3 grabbable affordance now calls the explicit `IGraspable` adapter rather than changing metadata alone. `key_001` gets a Rigidbody and real adapter, whose `Grasp` parents it to the Ubiq hand, publishes `ObjectPickedUp`, and triggers SceneContext. `lock_001`/`door_001` become quest-critical at runtime. The shared fixed plan is `vertical_slice_fixed` (retrieve key, then use with lock).

SceneAPI maps only property, affordance, create, relocate and semantic-state calls to `AuthoringActionExecutor`. BehaviorAPI maps only add `rotate_continuously`/`blink` and activation links. C3 now activates a validated `NextTaskSpec` in `QuestRuntimeState` and evaluates supported conditions after quest events. These changes are source-level **IMPLEMENTED** but PlayMode/VR/server execution remains **NOT VERIFIED**.
