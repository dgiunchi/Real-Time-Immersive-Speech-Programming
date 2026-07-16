# DreamCodeVR+ Mode-A Security Hardening

Status of the hardening programme on branch `hardening/mode-a-security`. Mode A
(speech → LLM → open-ended C# → validate → authenticated decision → Unity runtime
compilation) remains the primary research path; hardening adds authentication,
screening, monitoring and external recovery **without** replacing it. The honest
target is *research-only hardened Mode A*, not "arbitrary in-process C# is safe".

## Security profiles (spec §6)

Selected with `DCVR_SECURITY_PROFILE`:

| Profile | Posture |
|---|---|
| `legacy` (default) | Original-compatible. New controls off; **byte-identical** to the pre-hardening build, so the current Quest/Ubiq experience is unchanged. Logged as research-only/insecure. |
| `hardened` | Multi-user deployment. Authentication + replay protection mandatory; **fails closed** — a missing required control (admission secret, backend signing seed) is a startup error, never a silent downgrade to `legacy`. |
| `test` | Deterministic CI: loopback, mock STT/LLM, deterministic local keys. |

## Phase 1 — Identity, Authentication, Replay (IMPLEMENTED, Rust)

Two trust directions use deliberately different primitives so a compromise on one
leg cannot forge the other:

- **Client → backend:** per-session **HMAC-SHA256** MAC over an authenticated
  envelope (`EnvelopeMac`). Symmetric room-admission trust.
- **Backend → Unity:** asymmetric **Ed25519** signature (`BackendSigner` /
  `BackendVerifier`). The private key lives only in the backend; Unity holds the
  public key — so a leaked client secret cannot forge an approved NID-94 code
  decision.

The **authenticated envelope** (`crates/protocol/envelope.rs`, a pure crypto-free
codec) binds, into the signed region: protocol version, security profile (downgrade
detection), the message domain (`network_id_b`, so a tag for one NID can't be
replayed on another), a per-session monotonic sequence (`SessionSequence` replay
guard), an expiry, session/peer/request ids, a target peer, and a **SHA-256
payload hash** (for NID-94, the exact code hash). Verification is constant-time via
the audited `ring` crate.

The server seam (`apps/dreamcodevr-server/auth_gate.rs`) signs outgoing NID-94 and
verifies incoming envelopes, gated by the profile; `legacy` is an inert passthrough.

### Enabling hardened mode

```bash
export DCVR_SECURITY_PROFILE=hardened
export DCVR_PEER_AUTH_SECRET=<shared room admission secret>
export DCVR_BACKEND_SIGNING_SEED=<64 hex chars = 32-byte Ed25519 seed>
# Provision ServerAuth::backend_public_key() to the Unity BackendVerifier.
```

### Wire format (NID-94 signed message)

```
payload       = [u32 LE envelope_len][envelope][body]
envelope      = signing_input || [u16 tag_len][tag]
```

### Status & residual

- **Implemented + tested (Rust):** profiles, envelope codec, HMAC + Ed25519,
  replay guard, server signing seam. Part of the full **242-test** suite, clippy
  `-D warnings` clean.
- **Source complete (Unity, on-device pending):** `BackendVerifier.cs`
  (byte-matched to the Rust signer; Ed25519 primitive is a pluggable seam).
- **Deferred:** live-Quest end-to-end reproduction (no hardware until 2026-07-23);
  activating incoming envelope verification in the recv loop (needs the Unity
  client to emit envelopes); TLS is a transport-confidentiality **deployment**
  step (message-level auth already provides end-to-end integrity through an
  untrusted relay).
- **Irreducible (out of Phase-1 scope):** in-process C# limits (loop/memory/guard
  integrity) — addressed by later containment/perceptual phases, and honestly
  documented as contained-not-prevented.

## Later phases — status

- **Phase 2 Admin & privacy — DONE.** Constant-time admin token + fail-closed
  off-loopback bind (all profiles); profile erasure, `0600`, TTL purge, and optional
  ChaCha20-Poly1305 encryption-at-rest (`DCVR_PROFILE_ENC_KEY`).
- **Phase 3 Fail-closed C# validation — DONE (Rust).** Hardened Mode A/B requires a
  real Roslyn analyzer (no approve-all mock) + per-request timeout; the DeployHardened
  perceptual denylist covers the unambiguous device/rig/haptics/webcam + XR
  device-enumeration surface (dual-use MR/interaction APIs are deliberately left to
  runtime enforcement). Unity `RoslynCSharpSettings.asset` parity is on-device pending.
- **Phase 4 Per-peer concurrency — SAFE SUBSET DONE.** Every external `.await` held
  under the shared router lock is bounded (`screen_intent` + RAG embeds fail-open;
  OpenAI clients get transport timeouts) and an opt-in fail-closed
  `DCVR_UTTERANCE_TIMEOUT_MS` umbrella exists — closing the one-hung-connection-stalls-
  every-peer DoS. The full lock refactor (drop the lock across the network awaits) is
  **still open**: it rewrites live cancellation semantics and needs on-device
  validation, so it is held pending a Quest + risk sign-off; the timeouts already bound
  the stall regardless.
- **Phase 5 Runtime provenance/cleanup — Rust part DONE** (Mode-D `nofile`/`nproc`
  ulimits). OS watchdog + Unity registry are on-device pending.
- **Phase 6 Aggregate perceptual safety — Unity AUTHORED, on-device pending**
  (`PerceptualDisclosureHud` + `DisclosureFeed`, `DisclosureBackendForwarder` on NID 97;
  default-off, EditMode-tested, no runtime claim).
- **Phase 7 Voice confirmation — Rust + Unity AUTHORED.** Hardened STT `AudioBounds`
  input validation (Rust, done) + `VoiceCompileConfirmationGate` (Unity, authored,
  default-off, on-device pending).
- **Phase 8 Adversarial evaluation — DONE.** Cross-crate campaign (0% bypass / 0% FPR),
  wire-parser fuzz corpora, and a 12-skeptic adversarial-verify pass over the new
  controls (which caught + fixed a denylist over-block and a non-exercising test).

See `CURRENT_HANDOFF.md` / `PHASE_STATUS.json` in the external control directory for
live task status.
