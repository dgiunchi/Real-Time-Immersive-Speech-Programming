# DreamCodeVR2 - Recap integrazione quest, C1 e diagnostica

Data: 28 agosto 2026

## Obiettivo

Correggere l'avvio delle quest fisse C1/C2, visualizzare il task attivo nei
pannelli Unity, rendere distinguibili gli oggetti C1 e diagnosticare perché i
comandi vocali funzionavano solo sul primo drawer del tavolo.

## Evidenze raccolte sul Quest

È stato letto il file di log dell'ultima build direttamente dal Quest:

`/sdcard/Android/data/com.VARLab.DreamCodeVR2/files/DreamCodeVR2/logs/client_20260828T164034Z_run.jsonl`

Le evidenze principali sono:

1. Il server inviava `NextTaskActivationRequest`, ma non inviava il
   `NextTaskGenerated` documentato prima dell'attivazione.

   Esempio ricevuto:

   ```json
   {
     "peer": "f5e93a14-1860-48b8-b17e-6e1178d41215",
     "type": "NextTaskActivationRequest",
     "task_id": "set_a_instance_1:T1"
   }
   ```

2. Prima della correzione, questo produceva localmente:

   ```text
   FIXED_QUEST_ACTIVATION_FAILED
   No matching fixed quest task is pending.
   ```

3. Dopo il fallback client, il task è stato effettivamente attivato:

   ```text
   FIXED_QUEST_ACTIVATED_FALLBACK
   TASK_ACTIVATED target=sphere_001
   ```

4. La sfera C1 non veniva creata perché l'anchor iniziale non veniva trovato:

   ```text
   C1_QUEST_SPHERE_CREATE_FAILED
   Quest sphere start anchor is unavailable.
   anchor_id=table_001.desk_surface_anchor
   ```

5. Il contesto di interazione mandato al server aveva sempre
   `current_task_id: null`, anche con un task locale attivo. Il server riceveva
   correttamente gli oggetti puntati, ad esempio `painting_001`,
   `table_drawer_003` e `cabinet_drawer_001`, ma non l'ID della quest/task
   corrente.

6. Il server ha rifiutato i comandi non relativi al primo drawer con:

   ```text
   PredefinedCommandRejected
   That type of modification is not available.
   ```

7. L'unica proposta C1 effettivamente prodotta nel test è stata:

   ```json
   {
     "intent": "OPEN",
     "target_object_id": "table_drawer_001",
     "task_id": "set_a_instance_1:T1",
     "quest_instance_id": "set_a_instance_1"
   }
   ```

## Modifiche effettuate nel client Unity

### 1. Gestione robusta dell'attivazione delle quest fisse

File principali:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocol.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocolClient.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/ExperimentalResearcherPanel.cs`

Il client supporta ora due modalità.

#### Flusso contrattuale completo

Quando il server invia `NextTaskGenerated` con `task` e `quest_instance`, il
client converte il payload server nel formato runtime Unity:

- task ID;
- istruzione del partecipante;
- condizioni di successo;
- target dell'istanza;
- binding chiave-lock;
- lampada selezionata;
- note/indizi;
- setup C1 della sfera.

Il task viene applicato quando arriva il successivo
`NextTaskActivationRequest` con lo stesso `task_id`.

#### Fallback per il comportamento osservato sul server attuale

Poiché il server deployed invia soltanto `NextTaskActivationRequest`, è stato
aggiunto `FixedQuestActivationFallback` per i primi task canonici:

| Istanza | Drawer | Lock drawer | Chiave drawer | Lampada |
|---|---|---|---|---|
| `set_a_instance_1` | `table_drawer_001` | `lock_001` | `key_001` | `lamp_001` |
| `set_a_instance_2` | `table_drawer_002` | `lock_002` | `key_001` | `lamp_001` |
| `set_b_instance_1` | `cabinet_drawer_001` | `lock_003` | `key_001` | `lamp_001` |
| `set_c_instance_1` | `cabinet_drawer_002` | `lock_002` | `key_002` | `lamp_003` |

Per C1 Set A il fallback configura anche:

- `sphere_001`;
- anchor iniziale `table_001.desk_surface_anchor` per A1;
- anchor iniziale `table_drawer_003.drawer_inside_anchor` per A2;
- anchor di destinazione `basket_001.basket_inside_anchor`.

Il fallback è una protezione client. Non sostituisce il payload canonico che
deve essere inviato dal server.

### 2. Correzione della gara tra Ubiq e HTTP al riavvio sessione

In precedenza `ResetPlaythrough()` veniva chiamato nella callback della
risposta HTTP. Il server poteva però mandare Ubiq/NID101 prima della callback:
il task si attivava e veniva subito cancellato dal reset successivo.

Ora il reset viene eseguito prima della chiamata HTTP di start/restart. In
questo modo un'activation request arrivata rapidamente non viene più persa.

### 3. UI partecipante e ricercatore

File principali:

- `Unity/Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIController.cs`
- `Unity/Assets/DreamCodeVR2/Quest/QuestRuntimeState.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/ExperimentalResearcherPanel.cs`

Partecipante:

- mostra `CURRENT TASK`;
- mostra l'istruzione del task attivo nello stesso campo sempre visibile;
- mostra `Completed: N`;
- non mostra task futuri né il numero progressivo sul pannello partecipante.

Ricercatore, sezione Advanced:

- C1/C2: `Current Step N/Total`, task ID e task type;
- C3: task ID dinamico e numero di task completati;
- mantiene session ID, peer UUID, selected/pointed object, log e contatori di
  warning/error.

### 4. Correzione del task ID nel contesto inviato al server

File modificato:

- `Unity/Assets/DreamCodeVR2/ContextBridge/InteractionContextProvider.cs`

Problema precedente:

```json
"current_task_id": null
```

o, se configurato, sarebbe stato inviato il numero locale dello step (`"1"`)
anziché il task ID previsto dal contratto.

Correzione:

- il provider risolve automaticamente `QuestRuntimeState` se non è stato
  assegnato dall'Inspector;
- il bootstrap assegna esplicitamente il riferimento al provider;
- viene inviato `QuestTaskSpec.taskId`, per esempio
  `set_a_instance_2:T1`;
- il numero di step viene usato solo come fallback per task legacy senza ID.

Questo è importante perché il server usa il task ID per associare comando,
sessione, quest instance e autorizzazioni C1.

### 5. Correzione registrazione placement anchor e sfera C1

File modificato:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/VerticalSliceRuntimeBootstrap.cs`

Prima il bootstrap cercava gli anchor solo come figli diretti dell'oggetto:

```csharp
owner.transform.Find(anchorName)
```

`desk_surface_anchor` è presente nella scena, ma non era un figlio diretto del
root `table_001`; di conseguenza non veniva aggiunto come `AuthoringAnchor` e
la sfera C1 non poteva essere creata.

La ricerca ora procede in quest'ordine:

1. figlio diretto dell'oggetto;
2. qualunque discendente dell'oggetto;
3. anchor unico con quel nome nell'intera scena.

Il bootstrap registra inoltre nel log:

- `PLACEMENT_ANCHOR_REGISTERED` con anchor ID e posizione;
- `PLACEMENT_ANCHOR_MISSING` se non viene trovato;
- `PLACEMENT_ANCHOR_AMBIGUOUS` se il nome non è univoco.

### 6. Alias e capability per tutti i drawer

File modificato:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/VerticalSliceRuntimeBootstrap.cs`

Tutti i sei drawer pubblicano `open` e `close`, con controller
`ExperimentalDrawerController` dedicato.

Alias aggiunti:

| Object ID | Alias principali |
|---|---|
| `table_drawer_001` | drawer, table drawer, desk drawer, first table drawer, table drawer 1 |
| `table_drawer_002` | second table drawer, second desk drawer, table drawer 2 |
| `table_drawer_003` | third table drawer, third desk drawer, table drawer 3 |
| `cabinet_drawer_001` | cabinet drawer, first cabinet drawer, cabinet drawer 1 |
| `cabinet_drawer_002` | second cabinet drawer, cabinet drawer 2 |
| `cabinet_drawer_003` | third cabinet drawer, cabinet drawer 3 |

Il bootstrap produce anche `DRAWER_CAPABILITIES_PUBLISHED` con object ID,
alias e comandi realmente configurati.

### 7. Log diagnostici C1 e risposte complete del server

File principali:

- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/AuthoringProtocolClient.cs`
- `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/PredefinedVoiceCommandExecutor.cs`

Sono stati aggiunti i seguenti eventi nel file `.jsonl` del Quest:

| Evento | Contenuto |
|---|---|
| `NID101_SERVER_PAYLOAD` | JSON completo ricevuto dal server su NID101, troncato a 12.000 caratteri |
| `PREDEFINED_COMMAND_PROPOSAL` | command ID, target, intent, preset, secondary object e interpretazione |
| `PREDEFINED_COMMAND_EXECUTION_REQUEST` | comando C1 che il server chiede di eseguire |
| `PREDEFINED_COMMAND_REJECTED_BY_SERVER` | reason restituita dal server |
| `PREDEFINED_COMMAND_EXECUTE_LOCAL` | comando effettivamente ricevuto dal client per l'esecuzione |
| `PREDEFINED_COMMAND_FAILED` | target, intent, preset e codice di errore locale |
| `DRAWER_CAPABILITIES_PUBLISHED` | alias/capability dei drawer configurati a runtime |

## Comandi C1 supportati dal client

Il client non espone un'operazione generica `create_ball` in C1.

Per la sfera C1 il server deve proporre:

```json
{
  "intent": "MOVE_TO_PRESET",
  "target_object_id": "sphere_001",
  "preset_id": "soccer_ball"
}
```

Per posizionare la sfera nel basket:

```json
{
  "intent": "PLACE_IN",
  "target_object_id": "sphere_001",
  "secondary_object_id": "basket_001"
}
```

Per allineare il quadro:

```json
{
  "intent": "MOVE_TO_PRESET",
  "target_object_id": "painting_001",
  "preset_id": "aligned"
}
```

Per aprire/chiudere un drawer:

```json
{
  "intent": "OPEN",
  "target_object_id": "cabinet_drawer_001"
}
```

oppure un altro object ID presente nella SceneContext.

## Modifiche necessarie lato server

### Priorità 1 - rispettare il flusso task su NetworkId 101

Per C1/C2 il server deve inviare, nell'ordine:

1. `NextTaskGenerated` con `task`;
2. per il primo task, anche `quest_instance`;
3. `NextTaskActivationRequest` con lo stesso `task_id`.

Schema minimo:

```json
{
  "type": "NextTaskGenerated",
  "task": {
    "task_id": "set_a_instance_2:T1",
    "player_instruction": "...",
    "task_type": "...",
    "required_objects": ["..."],
    "success_conditions": ["..."]
  },
  "quest_instance": {
    "quest_instance_id": "set_a_instance_2",
    "quest_set_id": "set_a_ball_and_drawer",
    "key_lock_bindings": [],
    "task_targets": {},
    "clue_texts": {}
  }
}
```

Poi:

```json
{
  "type": "NextTaskActivationRequest",
  "task_id": "set_a_instance_2:T1"
}
```

Il fallback Unity va mantenuto come protezione, ma non può sostituire le
istruzioni, condizioni e configurazioni complete del server.

### Priorità 2 - usare il task e il contesto corrente per risolvere C1

Il server riceve ora:

- peer UUID;
- sessione;
- `current_task_id` canonico;
- selected object;
- pointed object;
- SceneContext con object ID, alias, capability e preset.

Il resolver C1 deve usare questi dati e non un hard-code limitato a
`table_drawer_001`.

### Priorità 3 - estendere l'allowlist del parser C1

Il test ha dimostrato che il server risponde:

```text
That type of modification is not available.
```

per comandi relativi a palla, quadro, altri table drawer e cabinet drawer.

Il server deve consentire e generare richieste `PredefinedCommandProposal` per:

- `OPEN` e `CLOSE` su tutti gli oggetti che dichiarano tali capability;
- `MOVE_TO_PRESET` + `soccer_ball` su `sphere_001`;
- `PLACE_IN` da `sphere_001` a `basket_001`;
- `MOVE_TO_PRESET` + `aligned` su `painting_001`;
- `USE_WITH` fra le chiavi e i lock previsti dalla quest instance;
- `ACTIVATE`, `DEACTIVATE` e `TOGGLE` sulle lampade dichiarate.

Il server deve scegliere il target nell'ordine seguente:

1. object ID esplicitamente nominato nella frase;
2. object ID puntato/selezionato nel contesto;
3. alias univoco della SceneContext;
4. target ammesso dal task/quest instance attivo.

### Priorità 4 - includere informazioni nei rifiuti

Quando il server rifiuta un comando, oltre a `reason` dovrebbe restituire:

- `command_id`, se disponibile;
- intent riconosciuto, se disponibile;
- target candidato;
- task ID;
- codice di rifiuto stabile, ad esempio `unsupported_intent`,
  `target_not_in_task_scope`, `missing_capability` o `ambiguous_target`.

Questo rende l'analisi dei test molto più rapida.

## Verifica dopo la prossima build

1. Avvia C1 e seleziona A1 oppure A2.
2. Verifica nel log `FIXED_QUEST_ACTIVATED_FALLBACK` oppure il flusso canonico
   `FIXED_QUEST_WIRE_RECEIVED` + `FIXED_QUEST_ACTIVATED`.
3. Verifica `PLACEMENT_ANCHOR_REGISTERED` per
   `table_001.desk_surface_anchor` o per l'anchor A2 previsto.
4. Verifica `C1_QUEST_SPHERE_CREATED` e che `sphere_001` sia visibile.
5. Pronuncia un comando e verifica che `INTERACTION_CONTEXT_SENT` riporti:

   ```json
   "current_task_id": "set_a_instance_1:T1"
   ```

   o l'ID dell'istanza selezionata.

6. Per ogni comando, confronta `NID101_SERVER_PAYLOAD`,
   `PREDEFINED_COMMAND_PROPOSAL` e gli eventuali rifiuti server/client.
7. Testa almeno:

   - sfera soccer-ball;
   - posizionamento nel basket;
   - allineamento quadro;
   - secondo/terzo drawer del tavolo;
   - primo/secondo drawer del cabinet.

## Test aggiunti

È stato aggiunto/coperto un test EditMode per:

- conversione di un `NextTaskGenerated` con quest instance;
- fallback A2 quando arriva solo `NextTaskActivationRequest`.

I test e la build Unity devono essere eseguiti dall'editor Unity prima della
nuova installazione sul Quest.
