<img width="556" height="488" alt="스크린샷 2026-08-21 230044" src="https://github.com/user-attachments/assets/759b5e60-3b76-4b5a-a567-c3496f4eba66" />
<img width="537" height="421" alt="스크린샷 2026-08-21 230022" src="https://github.com/user-attachments/assets/97cacdc5-dc89-46e9-a659-400ae3762358" />
# DreamCodeVR
DreamCodeVR is a Unity project developed at UCL based on Ubiq Social VR Platform and Ubiq Genie (plugin for backend services). It is designed to assist users, irrespective of their coding skills, in crafting basic object behavior in VR environments by translating spoken language into
code within an active application. 

![Illustrations of DreamcodeVR concept](DCVR.png)

Dreamcode VR is based on Ubiq and Ubiq Genie. Ubiq is a framework developed by UCL for social VR experiments, and Ubiq-Genie is a framework that enables you to build server-assisted collaborative mixed reality applications with Unity using the [Ubiq](https://ubiq.online) framework.

## Current Setup

Use this section for the current DreamCodeVR fork.

### Required tools

- Unity `6000.3.9f1`
- Node.js with npm
- Python `3.12` recommended for `Server/samples/venv`
- OpenAI API key for code generation
- Access to a Faster Whisper-compatible STT HTTP endpoint

### Install server dependencies

```powershell
cd Server
npm install
```

```powershell
cd samples
py -3.12 -m venv .\venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

If PowerShell blocks activation:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

### Configure API keys

Set secrets in the same terminal before starting the server. Do not commit API keys.

```powershell
$env:OPENAI_API_KEY="sk-proj-your-real-key"
$env:OPENAI_MODEL="gpt-5.5"
$env:OPENAI_MAX_COMPLETION_TOKENS="1000"
```

For the AgenticXR path, Claude replaces the legacy OpenAI code generator. Set the
Anthropic key only in the terminal that starts the server:

```powershell
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
$env:STT_HTTP_URL="http://your-faster-whisper-host:50101/stt/transcribe"
cd Server
npm run doctor
npm run start:agenticxr
```

`npm run start:agenticxr` enables `AGENTICXR_MODE=claude` automatically. It keeps
the Ubiq room server, speech capture and STT in this process, then starts one Claude
Agent SDK orchestration turn for each completed headset utterance. API keys must not
be written into `.mcp.json` or any committed JSON file.

Current STT uses Faster Whisper HTTP, not Azure Speech STT. The URL is required;
there is no built-in lab-server fallback:

```powershell
$env:STT_HTTP_URL="http://your-stt-host:50101/stt/transcribe"
$env:STT_SAMPLE_RATE="16000"
$env:STT_CHANNELS="1"
$env:STT_BITS_PER_SAMPLE="16"
$env:STT_REQUIRE_RECORDING="true"
```

Health check:

```powershell
curl.exe http://your-stt-host:50101/health
```

### Run DreamCodeVR

```powershell
cd Server\samples\apps\code_runtime_generator
node app.js
```

Then open the Unity project from the `Unity` folder with Unity `6000.3.9f1`, open `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity`, verify the `Room Client` points to the server IP and TCP port `8009`, then press Play or build to device. The AgenticXR runtime installs itself when this scene loads; no manual component placement is required.

In VR, hold the left controller trigger to record speech. Release it to send the utterance to STT. Point at an object with the ray to select it; red ray means selected target.

In AgenticXR mode, keep the ray on the target when recording starts so its stable
object ID is sent with the audio session. Claude queries that object, validates the
generated behaviour on an inactive staging clone, and either applies a low-risk
automatic proposal or displays the world-space Approve/Reject/Undo panel.

### Git hygiene

Generated folders such as `Server/samples/venv`, `node_modules`, Unity `Library`, `Temp`, `obj`, runtime logs, and sample input/output files are ignored. If any of those are already tracked, remove them from Git with `git rm --cached` so they stay on disk but stop being pushed.

### AgenticXR user study

#### Current implementation snapshot

The study implementation is on branch `agenticxr/study`. Snapshot commit
`830d429` is suitable for collaborator code review and continued development,
but the project is **not participant-ready**.

The study is a within-participant design with 24 participants and ten trials per
participant: five interaction modes, each experienced in two conditions. The
working session target is approximately 120 minutes, but that estimate must be
replaced with timings from two complete headset rehearsals.

| Mode | Participant task | Experimental comparison |
| --- | --- | --- |
| L1 | A loose tool finishes inside an empty workbench tray | Full AgenticXR vs. no Verification-Space dry-run |
| L2 | A loose part finishes inside its matching station socket | Full AgenticXR vs. no Verification-Space dry-run |
| L3 | An underspecified spoken request moves a marker to one of three pads; the participant then presses Done | Full AgenticXR clarification vs. DreamCodeVR-style baseline |
| L4 | A spoken request opens a trial-local practice door from its marked approach region | Full AgenticXR consent route vs. DreamCodeVR-style baseline |
| L5 | A three-step sequence survives two standardised spoken revisions | Full AgenticXR multi-turn route vs. DreamCodeVR-style baseline |

The confirmatory outcomes are H1 task success for L3-L5, H2 grounding-error
count for L1-L2, and H4 first-proposal acceptance for best-of-N=3 versus N=1.
H3 appropriateness is estimation-only, with 90% confidence intervals and TOST as
a sensitivity analysis; absence of statistical significance is not evidence of
stability.

#### What has been built

- A deterministic P001-P024 assignment with balanced task order, condition
  position, A/B room variants, and H4 candidate-count order.
- Versioned task, questionnaire, rubric, interaction-contract, model-pin, and
  hash-locked analysis-plan definitions under `Server/study/`.
- A fail-closed session state machine, append-only journal, replay verifier,
  privacy audit, blinded rater packets, trial exports, and a synthetic 24-person
  pilot harness.
- A generated Unity study scene containing 83 GameObjects, ten A/B variant
  roots, and all 68 manifest identifiers.
- The canonical Ubiq/OpenXR player prefab with tracked HMD and controllers,
  joystick movement, two teleport rays, a Teleport-tagged floor, grasp/use
  routes, reachable L2/L4 regions, graspable L1-L3 task objects, and working L3
  Done buttons. The system never moves the participant automatically.
- A study-safe `AgenticRuntimeCompiler`; the legacy `TestRoslyn` component is now
  only a demo adapter and is absent from the study scene.
- L4 safety controls: a trial-local door off the egress route, no door or proxy
  colliders, two scripted proxy avatars, no persistence, and no forced
  locomotion.
- H2 exposure fields derived from the append-only journal, including candidate,
  dry-run, visible-proposal, application, error-opportunity, error, and exposure-
  duration counts.
- Gate 7 physical-executability checks, which reject a generated scene missing
  its XR player, interaction paths, locomotion, task affordances, safe compiler,
  or D6 safety controls.

The authoritative definitions are in `Server/study/`; operator commands are in
`Server/evaluation/study_operator.js`; Unity study code is in
`Unity/Assets/Study/`; the deterministic builder is
`Unity/Assets/Editor/AgenticXRStudySceneBuilder.cs`; and its generated output is
`Unity/Assets/Scenes/AgenticXRStudy.unity`. Change the builder or definitions and
rebuild—never hand-edit the generated scene.

#### Latest verification

| Evidence | Current result |
| --- | --- |
| Node deterministic suite | PASS: 1,241 assertions |
| Mock integration | PASS, including 117-column trial exports |
| Synthetic pilot | PASS: 24 participants, 240 trials and 240 exports |
| Static task readiness | PASS: 104/104 checks and 68/68 manifest identifiers |
| Unity 6000.3.9f1 compilation | PASS in the recorded build run |
| Runtime compile/attach/dispose smoke test | PASS |
| Generated-scene determinism | PASS: two normalized rebuilds were byte-identical |
| Human Unity Editor inspection | NOT RUN |
| Quest-over-Link rehearsal | NOT RUN |
| Real model/STT session | NOT RUN |
| Human/institutional preflight | BLOCKED as intended |

Automated checks do not prove visual quality, comfort, reachability in a real
headset, tracking quality, ray ambiguity, or session-day safety.

#### What the researcher/investigators need to do next

Complete these in order. Do not recruit or run participants until every item is
finished.

1. **Resolve the remaining pre-data scientific decisions.** Confirm whether H2's
   primary estimand is errors per trial or errors per attempted application. The
   current locked plan calls the plain count-model coefficient an
   “incidence-rate ratio” without an exposure offset; exports now contain the
   denominators needed for either choice. If the plan changes, document the
   pre-data decision, update the method version where required, and deliberately
   re-lock the plan. Also decide whether to preregister and whether the paper's
   section 7.1 wording should be amended to match the implemented L1 route.
2. **Record the study's limitations.** L4 uses a safe trial-local door and two
   scripted proxies, so its “shared consequence” has deliberately reduced real
   stakes. L5 uses two fixed revisions, making it standardised multi-turn
   instruction-following rather than unconstrained co-authoring. L1/L2 have no
   additional participant primary activity beyond their paper-aligned task; any
   new activity would be a study-design amendment, not a code cleanup.
3. **Obtain the protected wording and approvals.** Add the exact licensed
   NASA-TLX and Jian et al. (2000) text through the approved process—never an AI
   paraphrase. Investigator-approve the twelve study-specific items, task cards,
   and rubrics. Complete ethics/institutional approval, information sheet,
   consent and debrief wording, safety, privacy/data-management, recruitment,
   recording/transcription, and adverse-event procedures. Copy
   `Server/study/approvals.example.json` to the gitignored
   `Server/study/approvals.local.json` and record only real approvals.
4. **Configure the private live environment.** Provide the pinned model version,
   model credentials, STT URL and credentials, the required STT-confidence
   channel, and a participant-specific artifact-log path. Keep secrets, logs,
   recordings, transcripts, and participant data out of Git. Disable transcript
   debugging before any human session.
5. **Inspect the generated scene in Unity.** Use Unity `6000.3.9f1`, run
   **AgenticXR > Build Study Scene** twice, verify byte-identical output, then open
   `AgenticXRStudy.unity`. Inspect the Console, hierarchy, all stable IDs, the XR
   player and both hands, teleport floor, every A/B task route, colliders, task
   cards, questionnaires, consent UI, lighting, resets, and build settings.
6. **Perform two complete researcher headset sessions.** Use reserved IDs such as
   P900 and P901 so both balancing blocks are rehearsed. Test Quest over Link,
   tracking, controllers, locomotion, grasp/use, push-to-talk, all L1-L5 routes,
   interruptions, recovery, abort, breaks, resume, export, and withdrawal.
   Record real phase timings and sign
   `docs/vr-study-rehearsal-checklist.md` only from observed evidence.
7. **Pass both final gates.** Technical preflight must pass with live services and
   log routing. Human preflight must then pass with licensed instruments,
   investigator sign-off, institutional approvals, and the signed rehearsal.
   Only then is participant recruitment permitted.

#### Commands

Run all Node commands from `Server/`, **not the repository root**. Create a plan
before participant-specific preflight. Use P900-P999 only for researcher dry-runs:

```powershell
cd Server
npm test
npm run test:integration
node study/pilot_harness.js
node -e "const r=require('./study/task_readiness').validateTaskReadiness(); console.log(JSON.stringify(r,null,2)); process.exit(r.ok?0:1)"

# Researcher dry-run
node evaluation/study_operator.js plan --participant=P900
node evaluation/study_operator.js preflight --mode=technical --participant=P900

# Human session, only after every approval and rehearsal gate is complete
node evaluation/study_operator.js plan --participant=P001
node evaluation/study_operator.js preflight --mode=human --participant=P001
```

Technical preflight checks the executable design, scene, state graph, analysis
lock, model pin, privacy defaults, live services, and participant log routing.
Human preflight adds questionnaire, rubric, investigator, institutional, and
physical-rehearsal gates. Its current failure is intentional; do not weaken or
bypass it to obtain a green result.

#### Do not

- Do not invent, reconstruct, or paraphrase copyrighted validated instruments.
- Do not widen the H3 equivalence margin or silently retune a hypothesis.
- Do not change the model, prompt, toolset, or candidate-count default without a
  new method version.
- Do not hand-edit `AgenticXRStudy.unity`; rebuild it deterministically.
- Do not run tests from the repository root.
- Do not claim participant readiness from Node tests, mock integration, Unity
  YAML, or compilation alone.
- Do not commit local approvals, credentials, logs, recordings, transcripts,
  participant data, `_audit_context/`, or Unity Performance Testing artifacts.

For operation, read `docs/EXPERIMENTER_RUNBOOK.md`. For the missing physical
evidence, complete `docs/vr-study-rehearsal-checklist.md`. Gate 7's scope and
limits are recorded in `docs/PHYSICAL_EXECUTABILITY_GATE.md`.

## Features

Ubiq's goal is to enable your networked project. It includes message passing, room management, rendezvous and matchmaking, object spawning, shared binary blobs, multiple synchronisation models, lighweight XR interaction examples, customisable avatars and voice chat across Windows, Linux, Android, MacOS, and Javascript running in the browser.

## Quick Start

These instruction will get you a copy of the project up and running on your local machine to run the samples and to start building your own applications. Alternatively, [GitHub Codespaces](https://docs.github.com/en/codespaces) can be used to quickly set up a server and run the server-side components of the samples (in this case, you may skip installing Node.js, step 2, and step 3).

0. Install [Unity](https://unity3d.com/get-unity/download) and [Node.js](https://nodejs.org/en/download/).

1. Clone this repository somewhere on your local PC.

```
git clone git@github.com:UCL-VR/ubiq-genie.git Ubiq-Genie
```

2. Open a terminal in the `Genie` folder and run `npm install` to install the dependencies. This includes the Node.js server of Ubiq.

3. Create a virtual environment and install the Python dependencies:

    ```
    python -m venv venv
    source venv/bin/activate
    pip install -r requirements.txt
    ```

    Note: on Windows, run `venv\Scripts\activate.bat` instead of `source venv/bin/activate`.

4. In Unity, open the `Unity` folder. To add Ubiq to the `Unity Hub`, open the `Unity Hub`, click `Add`, then navigate to `/Ubiq/Unity` and click `Select Folder`.

5. Read the README file in the corresponding folder in the `Server/samples/apps` folder for further setup instructions. For a list of available samples, see the [Samples](#samples) section below.

## Code Runtime Generator

The `Server/samples` folder contains a number of samples that demonstrate how to use Ubiq-Genie and so DreamcodeVR. For more information on how to use these samples, please refer to the README files in the corresponding folders. Currently, the following collaborative samples are available:

- [**DreameCodeVR**](Server/samples/apps/code_runtime_generator/README.md): generates a code that will be used to build procedural behaviours inside the 3D environment.

## Other Samples from Ubiq Genie

The `Genie/samples` folder contains a number of samples that demonstrate how to use Ubiq-Genie. For more information on how to use these samples, please refer to the README files in the corresponding folders. Currently, the following collaborative samples are available:

- [**Texture Generation**](Server/samples/apps/texture_generation/README.md): generates a texture based on voice-based input and an optional ray to select target objects
- [**Multi-user Conversational Agent**](Server/samples/apps/virtual_assistant/README.md): a conversational agent that can be interacted with by multiple users
- [**Transcription**](Server/samples/apps/transcription/README.md): transcribes audio streams of users in a room

For a demo video of the samples, please refer to the [Ubiq-Genie demo video](https://youtu.be/cGz0z9BIgQk).


## to do list

- add components (done)

- add objects (done)

- track all the objects in the scene

- track all components in the scene

- replace object (versioning)

- replace component (versioning)

- a custom component defined in framework needs to be known by compiler

- positioning in the hierarchy and the 3D space

- change values of public variables of the components (include linking objects)

- make an interaction between two (or more) objects (need a visible ID for the object known by the model)

- ​improve prompt programming (probably needs to be dynamic; prompt needs to change during the session)

- add interaction for checking only when use activate the recording session

- upgrade to ubiq 0.5.0

 

General Aspects to be tackled

- clashing between valid instructions, conflict handler 

- how to deal with code that does not work or not doing the required action (but can build, which visual feedback? how to edit?)

- security (we can inject what we want in a Quest)

- for the collaborative aspect, make all networked objects
    -- collaborative dynamic programming
       - the object has to be networked for the parameters need to be shared (gpt this for creating a prompt https://ucl-vr.github.io/ubiq/creatinganetworkedobject/)
       - no clashing


- store the created scene  (do not know if it is possible - versioning) (and so restore)


<img width="556" height="488" alt="스크린샷 2026-08-21 230044" src="https://github.com/user-attachments/assets/bc18394f-c7b4-4a22-b157-546e3c1fcf6d" />
<img width="537" height="421" alt="스크린샷 2026-08-21 230022" src="https://github.com/user-attachments/assets/12d7afb2-ca3f-4b28-998d-d129bb5d3507" />


## Verifying the project

One command runs everything that does not need an API key, a headset, or a
network beyond localhost. It works the same on macOS and Windows.

```bash
cd Server
npm install
npm run verify:all
```

| Flag | What it runs | Roughly |
|---|---|---|
| (none) | Node suites, mock integration, synthetic pilot, task readiness, Unity smoke test | 1 to 2 minutes |
| `-- --node` | everything except Unity | 50 seconds |
| `-- --unity-only` | the Unity smoke test alone | 20 seconds warm |

It prints one line per check and a single verdict, and exits non-zero if anything
failed or if nothing ran. A skipped check is never reported as a pass.

The Unity check compiles generated C# and attaches it to a live GameObject, then
confirms the capability allowlist blocks `System.IO`, `System.Net`,
`System.Reflection` and `System.Diagnostics`. Roslyn needs a JIT, so it runs in
the Editor where Mono provides one. It is not evidence about a standalone IL2CPP
build, where an assembly cannot be loaded at runtime at all.

If the pinned editor is not installed, the Unity check is skipped rather than
failed. To run it against a different installed editor, knowing the result is not
evidence for the pinned version:

```bash
AGENTICXR_UNITY_VERSION_OVERRIDE=6000.3.19f1 npm run verify:all
```

The command also checks that `ProjectVersion.txt` still matches what is
committed. Opening the project with a newer editor rewrites it, which silently
changes what "the pinned version" means and would invalidate any determinism
evidence taken afterwards.

### Platform note

Everything above runs identically on macOS and Windows. The physical VR
rehearsal in `docs/vr-study-rehearsal-checklist.md` does not: the study targets
Windows x64 with the Quest over Link, and Quest Link has no macOS client. See
`docs/LIVE_SYSTEM_REQUIREMENTS.md` section 6c.
