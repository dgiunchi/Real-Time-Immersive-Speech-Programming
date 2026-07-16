# Limitations & Honest Scope

DreamCodeVR+ is a **research / dissertation prototype**. This document states,
plainly, what it is and is not — so nothing in the README or code is read as an
overclaim.

## What is verified

- Rust workspace **builds, lints (`clippy -D warnings`), and tests (254 passing,
  0 ignored) fully offline** with mock STT/LLM/Roslyn; `cargo deny check` is
  clean; the .NET analyzer builds. (254 includes the hardened-profile auth stack,
  a cross-crate adversarial campaign, and deterministic fuzz corpora for both wire
  parsers — see below.)
- The safe **Mode-C** action-plan path and the validator-gated **Mode-A** path
  have been demonstrated in the Unity 6 editor and on a Quest 1/2 Mono sideload.
- The **Mode-D** sandbox has been exercised under Docker and gVisor.

## What is NOT production-ready

- **Not a production security boundary.** Do not expose on an untrusted network
  or run against untrusted peers without the hardened profile (below), and even
  then treat it as research-grade until the on-device pass is done.
- **Peer authentication is off in the default (`legacy`) profile** — peers
  self-assert identity and the Ubiq channel is **plaintext** (no TLS). An opt-in
  **`hardened`** profile enforces cryptographic peer auth (see below); TLS/WSS
  remains a separate transport step in either profile.
- **Admin panel** mutating routes are **unauthenticated unless a token is set**;
  it binds to loopback by default. It now **refuses to bind off-loopback without a
  token** and uses **constant-time** token comparison (both unconditional).
- **Mode B semantic layer is mock-by-default** (approves) unless the .NET Roslyn
  service is deliberately wired; the lexical layer is then the effective gate,
  and it is a denylist (thorough, adversarially tested, but not provably
  complete).
- **Mode A** (runtime C# compile) widens the trust surface and is **off by
  default**; keep it for trusted-network research/demos.
- **Mode D** uses `runc` by default (a soft boundary per NIST SP 800-190);
  gVisor is opt-in.

## The hardened profile (opt-in) — what it does and does not yet cover

`DCVR_SECURITY_PROFILE=hardened` adds an authentication/authorisation layer on
top of the default build. **The default `legacy` profile is byte-identical to the
original and is unaffected by any of this.** What hardened adds, verified in Rust:

- Message authentication — `AuthEnvelope` + client→backend HMAC-SHA256 and
  backend→Unity Ed25519 (audited `ring`), domain separation, SHA-256 payload
  binding, and a strict-monotonic replay guard.
- Fail-closed startup (missing keys ⇒ refuse to start, no silent downgrade),
  fail-closed Mode A/B analyzer (no approve-all mock), analyzer timeout.
- Privacy-at-rest: optional AEAD profile encryption, `0600`, TTL purge, erasure.
- A cross-crate **adversarial campaign** (forge/tamper/expire/wrong-domain/
  downgrade/truncate/garbage/replay) passing at **0% bypass, 0% false-positive**,
  plus deterministic **fuzz corpora** proving both wire parsers never panic and
  are canonical over ~95k malformed inputs.

**Not yet covered / honest caveats:**

- **No on-device (Quest) end-to-end run yet** — hardware arrives 2026-07-23. All
  evidence to date is host-side automated tests, not a live headset session.
- **Incoming envelope verification is not yet active on the wire** — the Rust
  verifier and its tests exist, but the Unity client must be built to *emit*
  envelopes before the recv-loop enforces them; only outgoing NID-94 signing is on
  the live path today.
- **No TLS/WSS** — message auth gives integrity through an untrusted relay, but
  not transport confidentiality; TLS is still a deployment step.
- **In-process C# limits** (loop/memory bounds inside Mode A/B) remain
  *contained, not prevented*; OS-level containment is future work (Mode D / later
  phases).

See [`HARDENING.md`](HARDENING.md) and [`DEPLOYMENT.md`](DEPLOYMENT.md).

## Platform limitations

- **Quest 3 / Meta Store** builds (IL2CPP/ARM64) are **future work**. IL2CPP
  forbids runtime C# compilation, so Mode A is limited to sideloaded Quest 1/2
  Mono builds; Mode C is the deployable-by-construction path.
- Headless Unity EditMode tests need a Unity licence sign-in; there is no Unity
  CI here.
- Live speech and Quest paths need hardware, an OpenAI key with quota, and the
  separately-fetched Ubiq RoomServer.

## Ownership & publication — REQUIRES HUMAN CONFIRMATION

DreamCodeVR+ is a **derivative of UCL's Apache-2.0 DreamCodeVR / Ubiq-Genie**
(see [`../LICENSE`](../LICENSE), [`../NOTICE`](../NOTICE)). It is also **MSc
dissertation work (University of Birmingham)**. Before any **public** release,
confirm:

1. **UCL / Apache-2.0** redistribution terms are satisfied (retain licence +
   NOTICE + statement of changes — done here; verify sufficiency).
2. **University of Birmingham** permits public release of the dissertation code
   (supervisor consent; no embargo; department IP policy).
3. No **employer / client / contract** IP is involved.
4. The proprietary **RoslynCSharp** asset is excluded (it is — not in this repo).
5. The author copyright line in `NOTICE`/`CITATION.cff` is completed.

The audit that accompanies this project (kept as private evidence, outside this
repository) records these as open items; they are **not resolved by the code**.

## Known engineering debt

- Some documentation in the original project contradicted itself (e.g. gVisor
  "verified" vs. the thesis's hedge); this compact doc set is the reconciled,
  code-accurate version.
- `cargo build --release` and `cargo audit` were not run during preparation
  (see [DEVELOPMENT.md](DEVELOPMENT.md)); `cargo check`/`clippy`/`deny` cover the
  same ground except release codegen and the online advisory yank-check.
