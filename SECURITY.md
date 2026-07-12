# Security Policy

## Status: research / dissertation prototype — NOT a production security boundary

DreamCodeVR+ is an academic prototype that turns spoken VR commands into safe
scene actions or validated runtime C#. It demonstrates a **defence-in-depth**
approach to LLM-driven code generation, but it is **not hardened for production
or hostile-network deployment**. Do not expose it on an untrusted network or
run it against untrusted peers without the additional hardening below.

## What the safety model does (and does not) cover

The safety design is documented in `docs/SECURITY_MODEL.md`. In summary:

* **Mode C (default)** — a bounded action-plan IR with a fail-closed validator;
  unsafe operations are unrepresentable. This is the safe path.
* **Mode B** — lexical (tree-sitter) validation of generated C#, optionally
  backed by a .NET Roslyn semantic check. The Roslyn service is **optional**;
  when it is not wired, a mock analyzer approves and the Rust lexical layer is
  the effective gate.
* **Mode A** — the original runtime-C# compile path, retained but **OFF by
  default** (`DCVR_MODE_A=false`). It compiles validator-approved C# on the
  client; enabling it widens the trust surface.
* **Mode D** — a hardened Docker/gVisor sandbox for genuinely untrusted C#;
  gVisor is **opt-in** (`DCVR_SANDBOX_DOCKER_RUNTIME=runsc`).

## Known limitations (do not deploy without addressing)

These are documented honestly rather than hidden:

1. **Peer authentication is not enforced by default.** An HMAC admission-token
   module exists (`crates/unity-transport`) but is not wired into the live
   transport in this release. Ubiq peers self-assert their identity.
2. **The admin/debug panel is unauthenticated unless a token is set** and binds
   to loopback by default. Do **not** bind it to `0.0.0.0` without setting
   `DCVR_ADMIN_TOKEN`.
3. **No transport confidentiality.** The Ubiq channel is plaintext TCP; use a
   trusted network or add TLS/WSS for real deployments.
4. **Mode A trusts NID-94 code messages.** With Mode A enabled and no peer auth,
   a malicious room member could get C# compiled on a client.
5. The lexical C# guard is a **denylist** — thorough and adversarially tested,
   but completeness is not guaranteed; it is one layer of defence-in-depth.

See `docs/LIMITATIONS.md` and `docs/SECURITY_MODEL.md` for detail.

## Reporting a vulnerability

This is a student research project. If you find a security issue, please open a
confidential issue or contact the author (see `CITATION.cff`). There is no SLA.

## Secrets

No credentials are committed. Configuration (including the optional OpenAI key)
is read from the environment; copy `.env.example` to `.env` for local use. Never
commit `.env`. If a key is ever exposed, rotate it.
