# Paper design extensions: implementation contract

This document records the implementation of the five capabilities specified by
`agenticxr_paper/rag/prompts/system-design-extensions-2026-07-22.md`. At implementation
time, the paper-strengthening prompt had **not** been executed: `main.tex` still had
H1--H3, described one-candidate verification, and stated that person memory lacked
cross-session persistence/revocation. Consequently the detailed engineering prompt,
not post-strengthening paper prose, was the available contract.

## Sequencing decision

Multi-candidate generation/selection and symmetric lifecycle operations were built
first because they can produce a defensible single-session measure: first-proposal
acceptance without revision for selected-best-of-three versus one candidate. This
does not replace study-readiness work. Live Anthropic, Unity Play Mode, Quest, and
participant measurements remain higher-risk blockers than further architectural
scope. The runtime event exporter now reports candidate counts, eligible counts,
selected scores, validated outcomes, and committed-without-revision counts.

## 1. Persistent user learning

`PersonPolicyStore` now supports a stable pseudonymous `personId` distinct from
`sessionId`. The raw identifier is never stored; a truncated SHA-256 key indexes a
JSON profile. Persistence is opt-in only through `set_person_profile_consent` or the
`AGENTICXR_PROFILE_CONSENT=true` plus `AGENTICXR_PERSON_ID` startup configuration.
Default behavior remains session-only. Profiles expire after a configurable 1--365
day retention period (90 days by default), expired records are removed from disk, and
`reset_person_profile` revokes bindings and deletes learned data.

The profile aggregates accept/reject, undo/repair, interaction/authoring mode,
response-latency, region, gaze/focus, and accepted-risk signals already emitted by
Shared XR Memory. It informs candidate ranking and may make automatic execution more
restrictive after repeated rejection. It cannot loosen `mode_policy`, Proposal Gate,
or Unity consent checks. Role/ownership remains the existing single-owner stub; this
feature is preference learning, not multi-user arbitration.

## 2. Multi-candidate lifecycle

The Artifact/Code Generator is instructed to produce exactly three distinct
candidates for one create/edit/remove intent. Every candidate must independently pass
Validator/Critic review and `simulate_artifact`. `rank_artifact_candidates` rejects
unvalidated or unsimulated candidates and deterministically ranks eligible candidates
by risk, pseudonymous preference fit, and experience-context fit. Non-selected and
ineligible candidates are retained in the existing temporal artifact log. Only the
best candidate is surfaced by default; L5 users may explicitly request alternatives.

Create, edit, and remove share `ArtifactProposal`, candidate metadata, freshness,
Verification Space, mode policy, Proposal Gate, and Unity confirmation. Edit/remove
require an active `existingArtifactId` and are forbidden in automatic mode. Unity
compiles edit code on a staging clone before replacing the current component. Remove
verifies the active reference, disables it only after confirmation, logs a distinct
`removed` result, and creates an undoable tombstone instead of pretending removal is
a failed-validation rollback.

## 3. Evolution history

The existing append-only `ArtifactLog` now indexes artifact IDs and exposes ordered
`evolution()` records containing operation, version, superseded/rollback pointer,
candidate set, rejected candidate, selection reason, intent, outcome, and correlation.
The MCP `get_evolution_history` tool and Version/Memory agent consume this same log;
there is no parallel version database.

## 4. Experience-context continuity

`ExperienceContextStore` maintains one inspectable coarse mode: productivity,
training, entertainment, exploration, or unspecified. Intent keywords provide a
lightweight inference; `set_experience_context` creates an explicit override that
subsequent inference cannot silently replace. Context persists across backend restart
and conditions candidate generation/ranking.

## 5. Checkpoint/resume

The backend atomically checkpoints active artifact references, consented profiles,
and experience contexts every 30 seconds and on clean process exit. The underlying
artifact log, profile store, and context store load on restart. Resume classification
requires a current stable-object inventory; references absent from the current scene
are returned and logged as `checkpoint_orphaned`, never silently resurrected.

Unity independently writes active generated code and its artifact/version/rollback
metadata to `Application.persistentDataPath/agenticxr-runtime-checkpoint.json` after
commit, edit, removal, rollback, and clean shutdown. On the next matching scene it
recompiles and attaches code only when the stable object still exists. Missing targets
and compile failures emit explicit checkpoint result events. Generated code is stored
locally in plain JSON; deployments that treat generated procedures as sensitive must
protect or disable device backups for that application data.

## Evidence boundary

Deterministic Node tests cover ranking eligibility, lifecycle invariants, stricter
learned-autonomy behavior, opt-in persistence/reset, context override/restart,
evolution lineage, and checkpoint orphan classification. The mock Ubiq/MCP flow
executes three dry-runs, deterministic ranking, create, edit, and remove. Unity batch
compilation checks the C# implementation. These do not establish live-model quality,
Unity Play Mode execution, physical headset behavior, longitudinal learning benefit,
or participant outcomes.
