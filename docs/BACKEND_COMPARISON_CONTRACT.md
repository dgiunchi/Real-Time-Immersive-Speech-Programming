# Backend comparison contract

## Why this exists

`Server/study/model_pin.v1.json` freezes one model so that a single study run is
reproducible. It cannot express a comparison. Its own `changeRule` says that any
model change requires a new `methodVersion`, so swapping Claude for GPT or Gemini
does not produce a second arm of one study, it produces a different study.

That leaves a gap. Running the same protocol against several backends and
reporting the numbers together is only a controlled comparison if something
guarantees that each backend received the same study and was scored the same way.
Nothing in the repository asserted that, so the guarantee lived only in whoever
happened to remember it.

`Server/study/backend_pin.v1.json` states the guarantee and
`Server/study/backend_equivalence.js` enforces it.

## What is held constant

Proven by `heldConstantDigest`, a hash over the canonical hashes of:

| Artifact | Why it may not vary |
| --- | --- |
| `protocol.v1.json` | Same trial structure in every arm |
| `task_cards.v1.json` | Same task wording and success criteria |
| `task_manifest.v1.json` | Same scene objects and stable identifiers |
| `rubrics.v1.json` | Same scoring instrument |
| `questionnaires.v1.json` | Same subjective measures |
| `interaction_contract.v1.json` | Same L1 to L5 routing and consent semantics |
| `analysis_plan.v1.json` | Same locked analysis |

Proven by `toolSurfaceDigest`: the agent tool surface, currently 24 tools, read
from the orchestrator source. A backend offered a different set of tools is
solving a different problem.

Proven by `invariants.candidateCountDefault`: the H4 best of N manipulation must
mean the same thing in every arm.

`model_pin.v1.json` is deliberately **not** held constant. It is the thing that
varies.

## What may vary

`providerId`, `modelId`, `modelVersionString`, `systemPromptHash`,
`toolInvocationMechanism`, `tokenizer`, `samplingParameterAvailability`.

A differing `systemPromptHash` is expected, because prompt formats differ between
providers. It is still recorded per trial, so a prompt change *within* one backend
remains detectable.

## How it fails

`validateBackendPin()` fails if a held constant artifact is edited, if the tool
surface changes, or if the analysis plan lock is broken. `trialBackendPin()`
throws rather than emitting a trial record, so a contaminated run cannot silently
reach an export.

A backend whose `status` is not `registered` is refused. The `openai` and `google`
entries are placeholders with null fields: whoever runs those backends fills in
every required field and flips the status, and until then their trials cannot be
exported or analysed.

## Usage

```bash
cd Server
npm run test:backends
```

```js
const eq = require("./study/backend_equivalence");

eq.comparabilityReport();                 // contract state and every backend
eq.validateBackend("claude-sonnet-4");    // is one backend admissible
eq.assertComparable("a", "b");            // were two backends run under one study
eq.trialBackendPin("claude-sonnet-4");    // record to embed in a trial export
```

## Registering a backend

1. Run the protocol unchanged. Do not edit any held constant artifact.
2. Add the real `modelId`, `modelVersionString`, `systemPromptHash`,
   `candidateCountDefault`, `toolInvocationMechanism`,
   `samplingParameterAvailability` and `toolsetVersion`.
3. Set `methodVersion` to the contract's `methodVersion`.
4. Set `status` to `registered`.
5. Run `npm run test:backends`. Registration is valid only if it passes.

Adding or removing a held constant artifact, changing the tool surface, or
changing the candidate count default requires a new comparison version and
re-locking both digests. Registering a backend does not.
