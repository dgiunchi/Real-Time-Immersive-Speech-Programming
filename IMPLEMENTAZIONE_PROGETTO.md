# Implementazione del progetto — DreamCodeVR / DreamCodeVR2

_Documento tecnico basato sullo stato del repository al 31 luglio 2026._

## 1. Scopo e risultato ottenuto

Il progetto e' un'applicazione Unity per VR collaborativa, costruita su **Ubiq** e Ubiq-Genie. Il suo caso d'uso principale, **DreamCodeVR**, permette di selezionare un oggetto nella scena, impartire un comando vocale e ricevere dal server un componente C# generato dall'AI: il client lo compila a runtime e lo applica all'oggetto selezionato.

Al nucleo originale sono state aggiunte estensioni raccolte sotto `Unity/Assets/DreamCodeVR2/`:

- un ponte di contesto d'interazione (oggetto selezionato/indicato);
- un inventario serializzato della scena;
- un testbed di escape room semanticamente annotato;
- un sistema di piani di quest, con anteprima, validazione, applicazione e stato di avanzamento;
- una UI di authoring/stato vocale costruita a runtime.

Il repository contiene anche le demo Ubiq-Genie di trascrizione, generazione texture, storytelling e agente conversazionale; esse sono esempi separati dal flusso DreamCodeVR principale.

## 2. Struttura del repository

| Percorso | Responsabilita' |
| --- | --- |
| `Unity/` | Client Unity 2021.3.16f1, scene, script C#, asset XR e runtime compiler. |
| `Server/` | Server Node.js/Ubiq-Genie e servizi Python della pipeline AI. |
| `Server local/`, `Server linux/` | Copie/configurazioni di deploy locali e Linux del server. |
| `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity` | Scena DreamCodeVR principale e unica scena abilitata nelle build. |
| `Unity/Assets/DreamCodeVR2/` | Funzionalita' di contesto, quest, UI ed escape room aggiunte. |
| `Unity/Assets/RoslynCSharp/` | Libreria impiegata per compilare C# a runtime. |
| `Unity/Assets/LegacyUbiqDependencies/` | Controller desktop/XR, ray, teletrasporto e UI raycaster ereditati. |

Dipendenze Unity rilevanti: Ubiq, WebRTC Ubiq fork, Oculus XR, Newtonsoft JSON, TextMeshPro/UI, AI Navigation e RoslynCSharp. Il server usa Node.js, Ubiq/Ubiq-Genie, Python e i servizi di STT/generazione.

## 3. Architettura end-to-end

```text
Utente VR/desktop
  -> ray di selezione + push-to-talk
  -> MicrophoneCapture (PCM mono 16-bit, NetworkId 98)
  -> server Node: CodeGeneration app
  -> servizio faster-whisper HTTP: trascrizione
  -> servizio OpenAI: C# MonoBehaviour
  -> Ubiq NetworkId 94
  -> CodeGenerationManager / TestRoslyn
  -> compilazione e applicazione al GameObject selezionato
```

La rete Ubiq usa canali numerici fissi. Quelli principali sono:

| NetworkId | Uso |
| --- | --- |
| 93 | Selezione materiale/oggetto (gli usi non sono completamente uniformi). |
| 94 | Risposta con codice generato e risposta dell'agente conversazionale. |
| 95 | Storytelling. |
| 96 | Generazione testo. |
| 97 | Generazione texture. |
| 98 | Audio PCM e comandi di inizio/fine registrazione; transcript collector. |
| 99 | ContextBridge: snapshot del contesto di interazione. |
| 100 | SceneContext: snapshot dell'inventario della scena. |

I pacchetti inviati dal client sui canali 98, 99 e 100 iniziano con i **36 byte UTF-8 dell'UUID del peer**, seguiti dal payload. Questo consente al server di associare audio e contesto al singolo utente nella stanza collaborativa.

## 4. Input vocale e diagnosi microfono

`Unity/Assets/MicrophoneCapture.cs` implementa il push-to-talk e la trasmissione:

1. All'avvio registra il componente su `NetworkId(98)` e inizializza il microfono Unity a 16 kHz.
2. In VR controlla continuamente il grilletto sinistro (`triggerButton` o valore analogico >= `0.75`); applica un debounce sul rilascio di default di 0,15 s.
3. In desktop/editor `DesktopServerMicAudioController.cs` usa la barra spaziatrice per avviare/interrompere lo stesso metodo `SetRecording`.
4. Alla partenza invia `__STT_CONTROL__:start`; alla fine scarica gli ultimi campioni e invia `__STT_CONTROL__:stop`.
5. Durante la registrazione legge il ring buffer del microfono, effettua downmix mono, applica il gain, converte i float in PCM signed 16-bit little-endian e invia i blocchi.
6. Produce diagnostica: durata, campioni, byte PCM, RMS, picco, silenzio/quasi-silenzio e buffer vuoto.
7. Se rileva segnale nullo o quasi nullo puo' riavviare automaticamente il microfono, con cooldown e un numero massimo di tentativi configurabili.

L'evento `RecordingStateChanged` e l'evento `DiagnosticsUpdated` sono usati rispettivamente dal ContextBridge e dalla UI di stato vocale. La cattura richiede il permesso microfono su Android; il codice lo richiede quando necessario.

## 5. Selezione e generazione dinamica di comportamento

`SelectObjectRay.cs` esegue ogni frame una `Physics.Linecast` nella direzione del controller. Accetta oggetti taggati `game`, visualizza il ray rosso quando e' presente un bersaglio e assegna il GameObject a `CodeGenerationManager.targetObject`.

Sul server, `Server/samples/apps/code_runtime_generator/app.js`:

1. legge `NetworkId 98`;
2. separa UUID, controlli STT e audio;
3. accumula l'audio per peer nel servizio STT;
4. quando arriva una trascrizione sufficientemente lunga, la salva anche in `data/input.txt`;
5. accetta i comandi preceduti da `>` e li passa al servizio di code generation;
6. inoltra la risposta C# sul canale 94 con `type: "CodeGenerated"` e l'UUID del peer.

Il servizio `code_generation/service_chatgpt.js` avvia `openai_chatgpt_api.py`, che riceve prompt e configurazione (chiave/model/limite token da ambiente). Il servizio STT configurato e' il wrapper Python `transcribe_whisper.py`, collegato a un endpoint faster-whisper HTTP; Azure STT non e' la via attiva della demo.

Nel client `CodeGenerationManager.cs` ascolta il canale 94. Quando riceve codice e c'e' un `targetObject`, passa il testo a `TestRoslyn.RunCode(targetObject)`: il runtime Roslyn compila il `MonoBehaviour` e lo associa all'oggetto. L'esecuzione dipende quindi sia da una selezione valida sia dal fatto che il codice generato compili.

## 6. Contesto d'interazione (ContextBridge)

La cartella `DreamCodeVR2/ContextBridge/` aggiunge un contratto strutturato senza modificare il canale audio.

- `AIEditableObject`: componente da applicare agli oggetti che l'AI puo' conoscere/modificare. Espone id stabile, nome leggibile, descrizione, etichette, flag `editable` e bounds del renderer.
- `SceneRegistry`: registra gli `AIEditableObject`, individua duplicati di `objectId`, restituisce riepiloghi e puo' produrre un fallback basato sul GameObject per oggetti non annotati.
- `InteractionContextProvider`: raccoglie selezione attiva e hit/raycast corrente, anche facendo un raycast non invasivo dai transform configurati.
- `InteractionContextTransmitter`: serializza `InteractionContextSnapshot` come JSON e lo invia su `NetworkId(99)` a inizio/fine registrazione; puo' inviare manualmente o periodicamente mentre si parla.

Lo snapshot contiene `schema_version`, timestamp, peer, `scene_version`, `active_selection`, `pointed_object`, posizione del puntamento e campi predisposti ma non ancora popolati per `last_action` e `pending_confirmation`.

## 7. Contesto completo della scena (SceneContext)

`DreamCodeVR2/SceneContext/` invia un inventario della scena su `NetworkId(100)`.

`SceneContextCompiler` cerca gli `AIEditableObject` attivi e inattivi, li ordina per id e produce per ciascuno: id, nomi, etichette/tipi, descrizione, posizione, rotazione, scala, stato attivo/modificabile, parent annotato, materiali (slot/shader/colore), componenti e operazioni disponibili (`edit` se editabile).

`SceneContextTransmitter` invia uno snapshot dopo 1,5 s dall'avvio e poi ogni 15 s (configurabile). Non invia delta e non opera ogni frame. L'UUID deve essere esattamente lungo 36 byte; in caso contrario l'invio viene bloccato con un warning.

## 8. Escape-room testbed

La scena `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity` e' una scena di prova semantica, non un puzzle completo. Riutilizza la configurazione Ubiq/DynamicCompiler e contiene il runtime `DreamCodeVR2_RuntimeServices` con registry, ContextBridge e SceneContext.

Gli oggetti sono annotati con id stabili quali `door_001`, `lock_001`, `drawer_001`, `key_001`, `button_001`, `painting_001`, `clue_note_001`, `socket_001`, `sphere_001` e `lamp_001`. Serve per verificare la consegna del contesto, la selezione e l'interpretazione di richieste semanticamente riferite alla scena (ad esempio: "make the key red"). Non implementa la logica reale di porta, serratura, cassetto o indizi.

## 9. Quest: piani, applicazione e runtime

Il sistema Quest e' una pipeline separata per descrivere scenari mediante JSON.

- `QuestPlan`, `QuestTaskSpec`, `QuestClueSpec`, `QuestInitialSetupAction`: modello dati del contratto quest.
- `QuestScenarioController`: coordinatore con modalita' Fixed, LLM-generated e Manual Debug. Carica mock in `Resources/MockQuestPlans`, mostra l'anteprima, applica il piano e gestisce scorciatoie F2-F12.
- `QuestPlannerClient`: esegue una POST JSON a `/api/quest/generate` (default `http://130.136.2.161:50001`), invia UUID peer, modalita' e, in debug, template; gestisce timeout ed errori HTTP.
- `QuestPlanApplier`: deserializza, valida riferimenti ad oggetti/anchor/materiali e applica azioni iniziali, testi degli indizi, posizionamenti e configurazioni ammesse. Mantiene cache di oggetti annotati e anchor.
- `QuestRuntimeState`: conserva piano attivo, indice, completamenti e risultato dell'ultimo task.
- `QuestTaskValidator`: per ora non collega eventi di gioco: tutti i task richiedono completamento manuale (`F3`).
- `RuntimeCreatableObjectCatalog`: crea solo `soccer_ball_001` e `colored_cube_001`, come Sphere/Cube taggati `game`, con `AIEditableObject`; puo' assegnare materiali esistenti o fallback colorati.

Le scorciatoie includono: F2 stato, F3 completa task manualmente, F4 reset, F6 cambia modalita', F7 anteprima, F8 applica, F9 applica il mock di contratto server, F10 richiede anteprima dal server, F11 applica l'ultimo piano ricevuto e F12 richiede e applica.

## 10. UI DreamCodeVR2

`DreamCodeVRAuthoringUIBootstrap` costruisce a runtime un Canvas/UI con TextMeshPro e i controller necessari (anche quelli Quest se assenti). `DreamCodeVRAuthoringUIController` mantiene pannelli di ispezione/selezione, transcript, piano e feedback, aggiornandone visibilita' e posizione rispetto al target.

`DreamCodeVRSpeechStatusBridge` ascolta `MicrophoneCapture` e `TranscriptionCollector`. Traduce lo stato tecnico in stati UI: inizializzazione, pronto, ascolto, elaborazione, frase ricevuta, nessun parlato, buffer vuoto, transcript vuoto ed errore. Espone inoltre durata, RMS, picco e dispositivo, cosi' che i problemi del microfono siano visibili invece di apparire come semplici errori AI.

`TranscriptionCollector` conserva l'ultimo testo e pubblica l'evento statico `TranscriptReceived`, usato dalla UI. Sono mantenuti anche controller per texture generation, storytelling e agente conversazionale delle demo Ubiq-Genie.

## 11. Avvio e configurazione

1. Installare dipendenze Node in `Server`: `npm install`.
2. Creare l'ambiente Python in `Server/samples/venv` e installare `requirements.txt`.
3. Impostare almeno `OPENAI_API_KEY`; configurabili anche `OPENAI_MODEL`, `OPENAI_MAX_COMPLETION_TOKENS`, URL/parametri STT.
4. Avviare `node app.js` in `Server/samples/apps/code_runtime_generator`.
5. Aprire `Unity/` con Unity 2021.3.16f1 e la scena `DynamicCompiler.unity`.
6. Configurare `Room Client` con l'IP della macchina server e TCP 8009.
7. In VR: puntare un oggetto `game`, tenere premuto il trigger sinistro, pronunciare il comando e rilasciarlo. In editor si puo' usare lo spazio per la registrazione.

Il server usa TCP 8009 e WSS 8010 per la stanza Ubiq. Il servizio STT deve essere raggiungibile (configurazione predefinita `http://130.136.2.161:50101/stt/transcribe`). Le chiavi non devono essere salvate in `config.json` o committate.

## 12. Stato effettivo, confini e lavoro rimanente

Implementato e collegato: cattura PCM, push-to-talk VR/desktop, controlli STT, trascrizione esterna, generazione OpenAI, ritorno rete, compilazione runtime, selezione per ray, diagnostica vocale, contesto di interazione, snapshot scena, UI di stato e quest JSON con validazione/applicazione.

Non ancora implementato o intenzionalmente limitato: logica effettiva dell'escape room, completamento automatico dei task, SceneAPI/BehaviorAPI completi, risoluzione semantica avanzata, undo/versioning, gestione conflitti collaborativi, sicurezza/sandbox del C# AI, persistenza/versionamento della scena, delta scene context e operazioni per-oggetto oltre a `edit` nel contratto corrente.

In particolare, compilare ed eseguire C# prodotto da un modello e' una superficie di rischio: il repository include il compilatore runtime ma non una sandbox applicativa completa. Questa parte va rafforzata prima di un uso non controllato su device/ambienti condivisi.
