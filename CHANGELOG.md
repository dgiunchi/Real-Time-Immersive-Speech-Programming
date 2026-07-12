# Changelog

All notable changes to DreamCodeVR+ are recorded here. DreamCodeVR+ is a
dissertation prototype; versions are informal.

## [0.1.0] — initial public-preparation snapshot

This is the first cleaned snapshot prepared for repository publication. It
captures the DreamCodeVR+ system as verified on the development machine.

### Added (relative to the original DreamCodeVR)

- A Rust workspace (`crates/`, `apps/`) that replaces the original Node.js
  Ubiq-Genie backend and joins the same Ubiq room as a service peer.
- A fail-closed action-plan validator and a bounded six-action IR (Mode C).
- Static/lexical + optional .NET Roslyn validation of generated C# (Mode B),
  including hardening against namespace-alias, Unicode-escape and `dynamic`
  evasion in the lexical scanner.
- A two-layer intent screen (keyword + optional LLM classifier) that neutralises
  malicious requests before generation.
- A Docker/gVisor sandbox harness for untrusted C# (Mode D).
- Observability (privacy-safe JSONL telemetry), an axum admin/debug panel,
  personalization/RAG, and a reproducible red-team harness (`redteam/`).
- Authored Unity C# integration scripts (`unity/`, `unity-examples/`) and the
  .NET Roslyn analyzer + sandbox worker services (`services/`).

### Notes

- The original runtime-C# path is retained as Mode A, now validator-gated and
  **off by default**.
- Mock STT/LLM/Roslyn clients are the default, so the pipeline runs fully
  offline with no credentials.
- Validation at this snapshot: `cargo fmt --check`, `cargo check`,
  `cargo clippy -D warnings`, `cargo test` (164 passing, 0 ignored),
  `cargo deny check`, .NET analyzer build — all pass (see docs/DEVELOPMENT.md).
