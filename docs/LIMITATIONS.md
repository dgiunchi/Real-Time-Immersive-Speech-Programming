# Limitations & Honest Scope

DreamCodeVR+ is a **research / dissertation prototype**. This document states,
plainly, what it is and is not — so nothing in the README or code is read as an
overclaim.

## What is verified

- Rust workspace **builds, lints (`clippy -D warnings`), and tests (164 passing,
  0 ignored) fully offline** with mock STT/LLM/Roslyn; `cargo deny check` is
  clean; the .NET analyzer builds.
- The safe **Mode-C** action-plan path and the validator-gated **Mode-A** path
  have been demonstrated in the Unity 6 editor and on a Quest 1/2 Mono sideload.
- The **Mode-D** sandbox has been exercised under Docker and gVisor.

## What is NOT production-ready

- **Not a production security boundary.** Do not expose on an untrusted network
  or run against untrusted peers without hardening.
- **Peer authentication is not enforced** in this release (an HMAC module exists
  but is unwired). The Ubiq channel is **plaintext** (no TLS).
- **Admin panel** mutating routes are **unauthenticated unless a token is set**;
  it binds to loopback by default. Token comparison is not constant-time.
- **Mode B semantic layer is mock-by-default** (approves) unless the .NET Roslyn
  service is deliberately wired; the lexical layer is then the effective gate,
  and it is a denylist (thorough, adversarially tested, but not provably
  complete).
- **Mode A** (runtime C# compile) widens the trust surface and is **off by
  default**; keep it for trusted-network research/demos.
- **Mode D** uses `runc` by default (a soft boundary per NIST SP 800-190);
  gVisor is opt-in.

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
