# Project structure — where everything lives

A map of the repository so the layout is understandable at a glance. **Nothing here
is renamed** — in a Rust workspace the crate/file names are wired into `Cargo.toml`
and `mod`/`use`, and in Unity a script's filename must match its class + `.meta`
GUID, so the names are part of the source contract, not cosmetic. This is the
conventional Cargo-workspace + Unity layout.

```
DreamCodeVRPlus-hardening/
│
├── Cargo.toml              # Rust workspace manifest (lists all crates + apps)
├── rust-toolchain.toml     # pinned Rust 1.96
├── deny.toml               # cargo-deny supply-chain policy
├── README.md               # front door
├── CHANGELOG.md            # release notes (Unreleased = the hardening work)
├── SECURITY.md             # security policy + honest limitations
│
├── BACKEND (Rust) ─────────────────────────────────────────────
│
├── crates/                 # 15 LIBRARIES (the reusable backend pieces)
│   ├── protocol/           #   Ubiq wire codec + AuthEnvelope (auth message format)
│   ├── unity-transport/    #   Ubiq service peer + HMAC/Ed25519 crypto primitives
│   ├── command-router/     #   per-peer sessions + STT→LLM→validate orchestration
│   ├── config/             #   settings + SecurityProfile (legacy/hardened/test)
│   ├── admin/              #   web admin/debug panel (axum)
│   ├── personalization/    #   RAG + user profiles + encryption-at-rest
│   ├── csharp-policy/      #   generated-C# validator (lexical + perceptual denylist)
│   ├── code-policy/        #   action-plan validator (the safe Mode-C gate)
│   ├── behaviour-dsl/      #   the 6-action plan DSL + numeric bounds
│   ├── roslyn-client/      #   client for the .NET Roslyn analyzer service
│   ├── stt-client/         #   speech-to-text clients + hardened audio bounds
│   ├── llm-client/         #   LLM clients (OpenAI / mock)
│   ├── control/            #   live runtime config + event bus (admin panel)
│   ├── observability/      #   privacy-safe JSONL telemetry
│   └── sandbox/            #   Mode-D Docker/gVisor sandbox for untrusted C#
│
├── apps/                   # 4 BINARIES (things you run)
│   ├── dreamcodevr-server/ #   THE BACKEND. src/{server,app,auth_gate,watchdog}.rs
│   │   └── src/bin/        #     keygen (make hardened keys) · watchdog (supervisor)
│   ├── fake-quest-client/  #   test client (drives the pipeline without a headset)
│   ├── sandbox-runner/     #   Mode-D sandbox runner
│   └── ubiq-probe/         #   Ubiq wire-format probe
│
├── tests/                  # cross-crate integration tests (auth_redteam.rs = attacks)
├── services/               # the .NET Roslyn analyzer + sandbox worker (C#)
├── redteam/                # reproducible adversarial corpus generator (Python)
├── scripts/                # run / build / network helpers
│
├── FRONTEND (Unity C#) ────────────────────────────────────────
│
├── unity/                  # authored drop-in scripts (copy into a Unity project)
│   ├── Runtime/            #   PerceptualDisclosureHud, VoiceCompileConfirmationGate,
│   │   │                   #   DisclosureBackendForwarder, PerceptualSafety, ...
│   │   └── Security/       #   BackendVerifier, ClientEnvelopeSigner, Ed25519Verifiers
│   └── Editor/             #   EditMode tests (Phase67, BackendVerifier, ClientEnvelope)
│
├── unity-examples/         # runnable EXAMPLE Unity projects
│   ├── Networked/          #   the live networked demo (ModeCNetworkedDemo.cs)
│   └── ModeC/              #   the offline Mode-C example
│
└── docs/                   # DOCUMENTATION
    ├── HARDENING.md            # the hardened security profile (design)
    ├── DEPLOYMENT.md           # how to run hardened
    ├── SECURITY_MODEL.md       # trust boundaries + the four modes
    ├── ROSLYNCSHARP_HARDENING.md  # on-device compile-time denylist config
    ├── TLS_DEPLOYMENT.md       # transport confidentiality (proxy)
    ├── ARCHITECTURE.md · PROTOCOL.md · LIMITATIONS.md · BUILD_AND_RUN.md · ...
    └── (unity/Runtime/README-PHASE6-7-SECURITY.md — the client security drop-ins)
```

## The 30-second mental model

- **`crates/` = the parts, `apps/dreamcodevr-server/` = the backend that wires them.**
  A voice command flows: `unity-transport` (receive) → `stt-client` → `llm-client` →
  `csharp-policy`/`code-policy` (validate) → `auth_gate` (sign) → back to Unity.
- **`unity/` = authored client code; `unity-examples/` = runnable demos.** The security
  pieces are under `unity/Runtime/Security/`.
- **`docs/` = read here first** (`HARDENING.md` for the security design).

## Why it isn't reorganized

Renaming any crate folder, `.rs` file, or Unity `.cs` file would require editing the
manifests / `mod` declarations / class names / `.meta` files — i.e. changing source
and risking a broken build for a cosmetic gain. The industry norm for a working tree
is to keep the conventional names and provide a map like this one.
