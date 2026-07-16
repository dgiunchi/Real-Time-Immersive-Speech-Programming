# Changelog

All notable changes to DreamCodeVR+ are recorded here. DreamCodeVR+ is a
dissertation prototype; versions are informal.

## [Unreleased] — security hardening (opt-in `hardened` profile)

A `hardening/mode-a-security` branch adds an authentication/authorisation layer
behind an explicit `DCVR_SECURITY_PROFILE`. **The default `legacy` profile is
byte-identical to 0.1.0 and is unaffected**; the current Quest demo keeps working
unchanged. Everything below is host-side verified; **on-device (Quest) validation
is pending hardware (2026-07-23)**.

### Added
- **Security profiles** (`legacy` default / `hardened` / `test`) with fail-closed
  invariants — hardened refuses to start without its keys (no silent downgrade).
- **Message authentication** — versioned canonical `AuthEnvelope`; client→backend
  **HMAC-SHA256** admission and backend→Unity **Ed25519** signatures (audited
  `ring 0.17`); domain separation on `NetworkId.b`; SHA-256 payload binding;
  **strict-monotonic replay guard**. Outgoing NID-94 signed on the live path;
  Unity `BackendVerifier.cs` mirror + integration notes.
- **Fail-closed Mode A/B** in hardened: a real Roslyn analyzer is required (no
  approve-all mock) and each request is timeout-bounded.
- **Privacy at rest:** profile **erasure** (+ authed `POST /api/profile/delete`),
  owner-only `0600` perms, **TTL purge**, and optional **ChaCha20-Poly1305**
  encryption gated on `DCVR_PROFILE_ENC_KEY` (no key ⇒ unchanged plaintext).
- **Admin hardening (all profiles):** constant-time token compare; refuse to bind
  off-loopback without a token.
- **`keygen`** utility + `docs/HARDENING.md` / `docs/DEPLOYMENT.md`.

### Verification
- Cross-crate **adversarial campaign**: 0% bypass, 0% false-positive across
  forge/tamper/expire/wrong-domain/downgrade/truncate/garbage/replay.
- Deterministic **fuzz corpora** for both wire parsers (`AuthEnvelope::from_bytes`
  and `decode_frame`): never-panic, canonical, mutation-closed over ~95k inputs.
- Full workspace **226 tests passing**, `clippy -D warnings` + `fmt` + `deny` clean.

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
