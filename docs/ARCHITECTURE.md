# Architecture

DreamCodeVR+ is a single async (`tokio`) Rust process that joins a Ubiq room as a
**service peer**, plus optional side-services (an admin panel, a LAN discovery
beacon) and optional external services (.NET Roslyn analyzer, Docker sandbox).

## Runtime flow

1. The Unity/Quest client joins a Ubiq room via the RoomServer; the backend joins
   the same room as another peer (`crates/unity-transport`).
2. The user selects a target object (NID 93) and holds push-to-talk to stream
   audio (NID 98).
3. The backend accumulates the audio, then runs, per utterance, in its own task
   (so one peer never stalls another — `apps/dreamcodevr-server/src/server.rs`):
   **STT → LLM → validation → dispatch**, orchestrated by
   `crates/command-router`.
4. **STT** (`crates/stt-client`): mock (default), OpenAI Whisper, or an HTTP
   faster-whisper endpoint. A "smart" wrapper treats short printable payloads as
   already-typed text.
5. **Intent screen (Layer 1)**: a keyword classifier plus an optional LLM
   classifier neutralise malicious requests *before* generation.
6. **LLM** (`crates/llm-client`): mock (default) or OpenAI. Produces an action
   plan and, on the dual path, a C# candidate.
7. **Validation**: `crates/code-policy` (action plan, fail-closed) and/or
   `crates/csharp-policy` (lexical C#) + `crates/roslyn-client` (optional .NET
   semantic check).
8. **Mode selection** (env-configured): C (action plan, default), B (validated
   C#), A (runtime-C#, off by default), D (sandbox).
9. **Dispatch** back into the Ubiq room (NID 94) to the Unity client, which
   either applies the action plan or compiles validated C#.
10. **Telemetry** (`crates/observability`): privacy-safe JSONL (reason codes,
    never transcripts) + a live event bus (`crates/control`) feeding the admin
    panel (`crates/admin`).

## Design invariants (enforced in code)

- **Fail-closed:** any STT/LLM/analyzer error or timeout yields `RejectUnsafe`
  with no plan.
- **Per-peer isolation:** no global lock; each peer has its own session and
  processing is spawned.
- **Privacy by construction:** external error *detail* never reaches the wire or
  logs — only fixed reason codes; the API key lives in `SecretString`.
- **Offline by default:** with no key/endpoint, mock clients are selected, so the
  whole pipeline runs locally and deterministically (this is what the test suite
  exercises).

## Crates (15) and apps (4)

| Crate | Role |
|---|---|
| `protocol` | Ubiq wire codec (no I/O, `unsafe`-free) |
| `unity-transport` | Ubiq service-peer membership + frame routing; HMAC token module (not wired by default) |
| `command-router` | the pipeline brain: sessions, orchestration, intent screen, validation, rate/comfort limits |
| `behaviour-dsl` | the 6-action allow-list IR + numeric bounds |
| `code-policy` | fail-closed action-plan validator |
| `csharp-policy` | lexical/tree-sitter C# validator (Mode B) |
| `roslyn-client` | client to the optional .NET semantic analyzer |
| `sandbox` | Docker/gVisor sandbox runner (Mode D) |
| `stt-client`, `llm-client` | pluggable STT/LLM seams (mock + real) |
| `observability` | privacy-safe JSONL telemetry |
| `config` | env/default settings; secret handling |
| `control`, `admin` | runtime control plane + web admin/debug panel |
| `personalization` | preference profile + embedding retrieval (RAG) |

| App | Role |
|---|---|
| `dreamcodevr-server` | the backend binary (wires everything) |
| `fake-quest-client` | test client (simulates selection + push-to-talk) |
| `ubiq-probe` | Ubiq join/connectivity probe |
| `sandbox-runner` | Mode-D containment demo |

See [PROTOCOL.md](PROTOCOL.md) for the wire format and [SECURITY_MODEL.md](SECURITY_MODEL.md)
for the safety design.
