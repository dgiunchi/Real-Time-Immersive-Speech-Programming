# DreamCodeVR+ Build and Run Guide

The canonical, end-to-end setup guide. A new developer can clone the source, install
the prerequisites, follow these commands, and reproduce the verified development
system — **without reading any internal audit reports**.

DreamCodeVR+ is a **research / dissertation prototype**, not a production security
boundary (see [SECURITY_MODEL.md](SECURITY_MODEL.md) and [LIMITATIONS.md](LIMITATIONS.md)).

## 1. What this guide covers

- The **offline mock demonstration** (Rust only — no credentials, no Unity, no network).
- Building and testing the **Rust workspace**.
- The **optional** .NET Roslyn analyzer (Mode B) and Docker/gVisor sandbox (Mode D).
- **Ubiq / Unity** integration for the live VR loop.
- Configuring **real STT / LLM** providers.
- **Network configuration** for LAN / Quest / restricted Wi-Fi.

The offline Rust path needs **only Rust**; everything else is optional.

## 2. Supported and tested environments

| Platform / component | Tested | Expected to work | Not verified |
|---|---|---|---|
| Ubuntu Linux (kernel 7.0), x86-64 | ✅ full Rust gate + .NET builds | other modern Linux | — |
| Rust 1.96.0 (rustfmt 1.9.0, clippy 0.1.96) | ✅ | ≥ 1.96 | older Rust |
| .NET SDK 10.0.109 (Roslyn analyzer + sandbox harness *build*) | ✅ builds | — | running the analyzer end-to-end against a live backend |
| Python 3.14 (red-team `py_compile`) | ✅ | Python ≥ 3.8 | — |
| Docker 29.6 / gVisor `runsc` | present, **not run** here | — | Mode-D live containment run |
| Ubiq RoomServer (Node) | — | Node ≥ 18 | not run in this validation |
| Unity 6000.5.x editor / Quest 1/2 (Mono) | statically reviewed | — | **not compiled/run in this environment** |
| Quest 3 / Store (IL2CPP/ARM64) | — | — | **future work** (see LIMITATIONS.md) |
| Windows / macOS | — | Rust path via native toolchain; `doctor.sh`/scripts via WSL or Git Bash | not verified |

"Tested" = actually executed here. Everything marked "not verified" must not be
claimed as working. The fresh-build result and exact tool versions are in
[REPRODUCIBILITY.md](REPRODUCIBILITY.md).

## 3. Repository structure

```
apps/            4 binaries (dreamcodevr-server, fake-quest-client, ubiq-probe, sandbox-runner)
crates/          15 Rust libraries (protocol, transport, router, validators, clients, …)
tests/           workspace integration tests (dcvr-integration-tests)
services/        .NET Roslyn analyzer (Mode B) + sandbox worker harness (Mode D)
scripts/         run / build / doctor / network helpers
redteam/         reproducible adversarial corpus generator + runner (Python)
unity/           authored Unity C# drop-ins (Runtime + Editor)
unity-examples/  example Unity project scripts (Mode C / networked)
docs/            architecture, security model, protocol, this guide, reproducibility, limitations
```

## 4. Software prerequisites

| Tool | Purpose | Version (tested) | Required? | Check |
|---|---|---|---|---|
| Rust + Cargo | build + test the backend | 1.96.0 | **Required** | `rustc --version` |
| rustfmt, clippy | format + lint gates | bundled with 1.96 | **Required** | `cargo fmt --version`, `cargo clippy --version` |
| Bash | helper scripts | 5.x | **Required** (scripts) | `bash --version` |
| cargo-deny | supply-chain gate | 0.19 | Optional | `cargo deny --version` |
| .NET SDK | Mode-B analyzer, Mode-D harness | 10.0 | Optional | `dotnet --version` |
| Python 3 | red-team tooling | 3.14 (≥3.8) | Optional | `python3 --version` |
| Docker (+ gVisor `runsc`) | Mode-D sandbox | 29.x | Optional | `docker --version` |
| Node.js | run the fetched Ubiq RoomServer | ≥ 18 | Optional (live VR) | `node --version` |
| Unity | the VR client | 6000.5.x | Optional (live VR) | — |
| `curl`, `jq`, `openssl`, `ip`, `ss` | some helper scripts | — | Optional | `scripts/doctor.sh` |

Run `bash scripts/doctor.sh` to check all of these at once (§8).

## 5. Install Rust

Install `rustup` from the official site (`https://rustup.rs`) and let it install the
stable toolchain, then let this repo select the pinned version:

- **Linux / macOS:** `curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh`
- **Windows:** install `rustup-init.exe` from the official site (then use WSL or Git Bash for the shell scripts).

This repo ships a `rust-toolchain.toml` pinning **1.96.0** with `rustfmt` + `clippy`,
so `rustup` selects the correct toolchain automatically inside the repo.

Verify:

```bash
rustc --version        # rustc 1.96.0
cargo --version        # cargo 1.96.0
cargo fmt --version
cargo clippy --version
```

## 6. Install optional prerequisites

Only install what your chosen path needs. **None of these are needed for the offline
Rust test path (§10–§12).**

- **.NET SDK 10** — for the Mode-B Roslyn analyzer (§15) and Mode-D harness (§16).
- **Python 3** — for the red-team tooling (§17). No third-party packages; standard library only.
- **Docker** (+ optional **gVisor `runsc`**) — for the Mode-D sandbox (§16).
- **Node.js** — to run the separately-fetched **Ubiq RoomServer** (§18).
- **Unity 6000.5.x** + **Android/Quest** toolchain — for the VR client (§18). Not verified here.
- **OpenAI API key** — only for the real STT/LLM path (§20). Optional; mocks are the default.
- **cargo-deny** — `cargo install cargo-deny` for the supply-chain gate (§11).

## 7. Download or clone

Once a Git remote exists you will `git clone <url>` and `cd` into the repo. Until
then, obtain the source archive, unpack it, and enter the directory (the published
GitLab slug is `dreamcodevr-plus`):

```bash
cd dreamcodevr-plus
```

## 8. Run the prerequisite checker

```bash
bash scripts/doctor.sh
```

It prints each detected tool + version, marks tools **required** vs **optional**, and
exits non-zero **only** if a required tool (Rust/Cargo/rustfmt/clippy/Bash) is missing.
Optional-tool warnings are informational — the offline build path still works without them.

## 9. Environment configuration

```bash
cp .env.example .env      # .env is gitignored — NEVER commit it
```

- With **no** `OPENAI_API_KEY` and **no** STT URL, the backend uses **offline mocks** — no key needed.
- Real-provider mode requires **your own** values (§20).
- All network addresses must be adapted to **your** LAN (§19). Nothing is baked in.

Every user-facing variable (grouped by section in `.env.example`):

| Variable | Required | Default | Secret | Purpose |
|---|---|---|---|---|
| `DCVR_MODE` | no | `action_plan_fast` | no | execution mode (Mode C) |
| `DCVR_LISTEN_ADDR` | no | `127.0.0.1:9098` | no | standalone TCP listener |
| `DCVR_UBIQ_ADDR` | no | unset | no | RoomServer `host:port` (service-peer mode) |
| `DCVR_ROOM_GUID` | no | code default | no | Ubiq room to join |
| `DCVR_ADMIN_PORT` | no | unset (off) | no | admin panel port (loopback) |
| `DCVR_ADMIN_TOKEN` | if non-loopback | unset | **treat as secret** | `X-Admin-Token` for mutating routes |
| `DCVR_STT_HTTP_URL` | no | unset (mock) | no | faster-whisper STT endpoint |
| `DCVR_STT_OPENAI` | no | `false` | no | use OpenAI Whisper STT |
| `OPENAI_STT_MODEL` | no | `whisper-1` | no | OpenAI STT model |
| `DCVR_STT_TIMEOUT_MS` | no | `10000` | no | STT timeout |
| `OPENAI_API_KEY` | for real LLM | unset (mock) | **YES** | OpenAI key |
| `OPENAI_MODEL` | no | `gpt-4o-mini` | no | LLM model |
| `OPENAI_BASE_URL` | no | OpenAI | no | OpenAI-compatible endpoint |
| `DCVR_LLM_TIMEOUT_MS` | no | `60000` | no | LLM timeout |
| `DCVR_CSHARP_RESEARCH` | no | `false` | no | Mode-B validated-C# path |
| `DCVR_ROSLYN_URL` | no | unset (mock) | no | Roslyn analyzer URL |
| `DCVR_SANDBOX_DOCKER_RUNTIME` | no | unset (`runc`) | no | `runsc` for gVisor (Mode D) |
| `DCVR_MODE_A` | no | `false` | no | runtime-C# path (opt-in) |
| `DCVR_REQUIRE_PEER_AUTH` | no | `false` | no | require peer HMAC token (see LIMITATIONS) |
| `DCVR_PEER_AUTH_SECRET` | if peer-auth | unset | **YES** | shared HMAC secret |
| `DCVR_PERCEPTUAL_HARDENING` | no | `false` | no | ban perceptual-attack C# surface |
| `DCVR_PERSONALIZATION_DIR` | no | `.dcvr-data/personalization` | no | local RAG store (gitignored) |
| `DCVR_EMBED_OPENAI` | no | `false` | no | OpenAI embeddings for RAG |

Boolean flags accept `true`/`TRUE`/`1`/`yes` (any case, whitespace-tolerant); anything else is false.

## 10. Fresh Rust build

Reproducible build from the committed lockfile:

```bash
cargo build --workspace --locked        # debug; artifacts under target/debug/
cargo build --workspace --release --locked   # optimized; target/release/
```

`--locked` refuses to update `Cargo.lock`, so the dependency set is exactly what was verified.

## 11. Run validation

The full gate (identical to CI, plus `--locked`):

```bash
cargo fmt --all -- --check
cargo check --workspace --locked
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace --locked
cargo build --workspace --release --locked
cargo deny check            # optional; needs cargo-deny
```

Expected: **164 tests pass, 0 failed, 0 ignored, 0 Clippy warnings.** (Was 160 before
this release added 4 config boolean-parser tests.) A one-shot equivalent:
`bash scripts/verify-fresh-build.sh`.

## 12. Run the offline / mock demonstration

No API key, no Unity, no network. Two terminals, from the repo root:

**Terminal 1 — backend (offline mocks):**
```bash
cargo run -p dreamcodevr-server
```
It binds the standalone TCP listener on `127.0.0.1:9098` and logs that it is using
mock STT/LLM clients. Leave it running.

**Terminal 2 — drive it with the fake Quest client:**
```bash
cargo run -p fake-quest-client            # optional explicit addr: -- 127.0.0.1:9098
```
The client's first argument is the backend address (default `127.0.0.1:9098`). It runs a
**built-in demo scenario** — selects an object and sends a sample "spoken" command over the
mock path — and prints the backend's validated action-plan decision. No audio, no Unity,
no credentials involved. (For a Ubiq-room run instead: `-- --ubiq <host>:8009 <room> "<command>"`.)

**Stop:** press `Ctrl-C` in each terminal (or `scripts/stop-all.sh` if you used the
launchers). **Port conflict?** If `9098` is busy, set `DCVR_LISTEN_ADDR=127.0.0.1:<free-port>`
for the backend and pass the same `host:port` to the client (`-- <addr>`); diagnose with
`ss -ltnp | grep 9098`.

## 13. Run the Rust backend

- **Standalone / mock mode (default):** `cargo run -p dreamcodevr-server` — TCP listener on
  `DCVR_LISTEN_ADDR` (default `127.0.0.1:9098`). Good for the fake client and local tests.
- **Ubiq service-peer mode:** set `DCVR_UBIQ_ADDR=<roomserver-host>:8009` (and optionally
  `DCVR_ROOM_GUID`) — the backend joins that RoomServer room as a service peer instead of
  listening standalone. Requires a running RoomServer (§18).

Verify it is up: watch the startup log, or `ss -ltnp | grep 9098` (standalone), or check
the admin panel (§14).

## 14. Run the admin panel

Enabled by an environment variable; **binds to loopback**:

```bash
DCVR_ADMIN_PORT=7878 cargo run -p dreamcodevr-server
# then open http://127.0.0.1:7878
```

- Mutating routes honour an optional `DCVR_ADMIN_TOKEN` (`X-Admin-Token` header).
- **If no token is set, mutating routes are unauthenticated** — so **never** bind the
  panel to `0.0.0.0` / a public interface without setting a token. Keep it on loopback.
- The `/api/*` routes (config, stats, safety log, validate, red-team, command, sandbox,
  profiles) are listed authoritatively in `crates/admin/src/lib.rs`.

## 15. Run the Roslyn analyzer (Mode B, optional)

```bash
dotnet restore services/roslyn-analyzer/RoslynAnalyzer.csproj
dotnet build   services/roslyn-analyzer/RoslynAnalyzer.csproj
dotnet run --project services/roslyn-analyzer/RoslynAnalyzer.csproj   # listens on :5099
```

Point the backend at it: `DCVR_CSHARP_RESEARCH=true DCVR_ROSLYN_URL=http://127.0.0.1:5099/analyze`.
It POSTs `/analyze` and returns `{approved, diagnostics}` (fail-closed deny-list).
**Without it wired, Mode B uses a mock analyzer that approves**, so the Rust lexical
denylist is the effective gate (see [SECURITY_MODEL.md](SECURITY_MODEL.md)). Stop with `Ctrl-C`.

## 16. Run the sandbox worker (Mode D, optional — research tooling)

Mode D runs **untrusted** C# in a hardened container. **Never run generated C# directly
on the host.** Requires Docker (gVisor `runsc` is optional, stronger isolation).

```bash
docker build -t dcvr-sandbox-harness:local services/sandbox-worker    # build the harness image
cargo run -p sandbox-runner                                           # demo the containment
# optional gVisor: DCVR_SANDBOX_DOCKER_RUNTIME=runsc cargo run -p sandbox-runner
```

Mode D is a **research arm**; it is **not** wired into the normal live speech path (the
deployable path is Mode C). Containment flags are in `scripts/sandbox-run-docker.sh` /
`scripts/sandbox-run-gvisor.sh`.

## 17. Run red-team tooling (Python)

No third-party packages; deterministic; offline for the C# layer:

```bash
python3 redteam/corpus_gen.py redteam/corpus.json          # generate ~1,057 vectors (offline)
python3 redteam/run_campaign.py --layer csharp             # fire at the validator (offline, no API)
python3 redteam/run_campaign.py --layer nl                 # NL layer — needs the backend running
```

Outputs land in `redteam/results_*.json` and `redteam/corpus.json` (both gitignored).
The `--layer csharp` run is API-free; `--layer nl` needs the backend up (mocks by default,
so no paid API).

## 18. Configure Ubiq and Unity

- The **Ubiq RoomServer is not included** (third-party UCL/Ubiq code, not redistributed).
  Obtain a compatible RoomServer from the Ubiq project and run it on TCP `:8009`
  (`scripts/run-roomserver.sh` expects it under `vendor/ubiq-roomserver/`; see
  [REPRODUCIBILITY.md](REPRODUCIBILITY.md)). The Rust backend joins it via `DCVR_UBIQ_ADDR`.
- **NetworkIds** (see [PROTOCOL.md](PROTOCOL.md)): 93 select · 98 audio · 94 backend decision ·
  95 like/dislike · 96 compile result.
- **Authored Unity scripts** live in `unity/Runtime` (+ `unity/Editor`); import them into a
  Unity 6 project and register `ActionPlanNetworkBridge` on `NetworkId(94)` (remove any
  original `CodeGenerationManager` from NID 94). Full wiring: [UNITY_INTEGRATION.md](UNITY_INTEGRATION.md).
- The **proprietary RoslynCSharp** Unity asset used by the original Mode-A path is **not
  included** and may not be redistributed here; obtain it separately under its own licence
  if you need Mode A on-device. Mode C needs no runtime compilation.

## 19. Local-network and Wi-Fi configuration

Development originally used **fixed university/lab addresses**; those values were specific
to that environment and are **no longer hardcoded** in the published source. You must
supply **your own** host addresses (via `DCVR_UBIQ_ADDR`, and the helper scripts read
`DCVR_ROOMSERVER_HOST` / `DCVR_ALIAS_IP`). The computer and the Quest/Unity client must be
able to reach the RoomServer.

**Scenarios:**

- **Everything on one computer** — use loopback (`127.0.0.1:9098` / `127.0.0.1:8009`). Simplest.
- **Backend + RoomServer on one computer, Unity Editor on another** — point the Unity Ubiq
  config at the **host computer's LAN IP** (e.g. `192.0.2.10:8009`), not loopback.
  `scripts/show-ip.sh` prints the LAN IP.
- **Quest headset on Wi-Fi** — the headset **cannot** reach the computer's `127.0.0.1`; use
  the **computer's LAN IP**. Open TCP `8009` (and UDP `8987/8988` for discovery) in the
  firewall — `scripts/open-firewall.sh` helps.
- **University / guest / enterprise Wi-Fi** — such networks often enforce **client
  isolation**, block **multicast/UDP** and inbound ports, so auto-discovery and direct
  peer-to-peer may fail. Prefer a **shared trusted LAN**, configure the host manually when
  discovery fails, check firewall rules, and use approved infrastructure. **Do not attempt
  to bypass network security controls.**

**Ports** (all configurable; confirm against source):

| Port | Component | Config |
|---|---|---|
| 9098/tcp | standalone backend listener | `DCVR_LISTEN_ADDR` |
| 8009/tcp | Ubiq RoomServer | `DCVR_UBIQ_ADDR` |
| 8010/tcp | RoomServer WSS | (RoomServer) |
| 7878/tcp | admin panel (loopback) | `DCVR_ADMIN_PORT` |
| 5099/tcp | Roslyn analyzer | `DCVR_ROSLYN_URL` |
| 8987/udp, 8988/udp | LAN discovery beacon | (fixed) |

**Never expose the admin panel or the backend directly to the public internet.**

## 20. Configure real STT and LLM providers

Offline mocks are the default. For real providers, set in `.env`:

- **LLM:** `OPENAI_API_KEY=sk-...` (secret), optionally `OPENAI_MODEL`, `OPENAI_BASE_URL`, `DCVR_LLM_TIMEOUT_MS`.
- **STT:** `DCVR_STT_OPENAI=true` (OpenAI Whisper, reuses the key) **or** `DCVR_STT_HTTP_URL=http://<host>:50101/stt/transcribe` (faster-whisper).

`.env` is private (never commit). **Real providers may incur cost.** The test suite
**never** calls a paid API (it uses mocks). Consider data/privacy implications before
sending real audio/prompts to a provider.

## 21. Common failures and troubleshooting

| Symptom | Likely cause | Diagnose | Fix |
|---|---|---|---|
| `error: toolchain '1.96.0' is not installed` / MSRV error | wrong Rust | `rustc --version` | install rustup; the `rust-toolchain.toml` selects 1.96 |
| `cargo fmt`/`clippy` not found | missing components | `cargo fmt --version` | `rustup component add rustfmt clippy` |
| `Address already in use` | port taken | `ss -ltnp \| grep <port>` | change `DCVR_LISTEN_ADDR`/`DCVR_ADMIN_PORT` |
| backend can't join room | RoomServer down / wrong addr | `cargo run -p ubiq-probe -- <host>:8009` | start RoomServer; fix `DCVR_UBIQ_ADDR` |
| Quest can't connect | using `127.0.0.1` | check the app's server IP | use the computer's LAN IP |
| headset unreachable on Wi-Fi | firewall / client isolation | `scripts/open-firewall.sh`; try a trusted LAN | open 8009/tcp + 8987-8988/udp; avoid guest Wi-Fi |
| LLM stays mock | no key | grep the startup log for `llm = mock` | set `OPENAI_API_KEY` in `.env` |
| Mode B approves everything | Roslyn not wired | is `:5099` up? | run the analyzer (§15) + set `DCVR_ROSLYN_URL` |
| Mode D fails | Docker/gVisor absent | `docker --version`, `runsc --version` | install Docker; `runsc` optional |
| `.env` ignored | not loaded | confirm you `cp .env.example .env` | run from the repo root; some scripts source `.env` |
| `DCVR_*=TRUE` ignored | (fixed) case/whitespace | — | booleans now accept any case + whitespace |
| admin panel open without auth | non-loopback + no token | check bind + token | set `DCVR_ADMIN_TOKEN`, keep loopback |
| Unity Mode A won't compile | RoslynCSharp excluded | — | obtain the asset separately, or use Mode C |

## 22. Clean rebuild

```bash
cargo clean                              # removes target/ (build output only)
cargo build --workspace --locked
cargo test --workspace --locked
```

## 23. Security and limitations

See [SECURITY_MODEL.md](SECURITY_MODEL.md) and [LIMITATIONS.md](LIMITATIONS.md).
**DreamCodeVR+ is a research / dissertation prototype and is not a production security
boundary.** Peer authentication exists but is not fully wired; the Ubiq channel is
plaintext; Mode C is the deployable, no-compilation path.
