# Development

## Prerequisites

- **Rust 1.96** (edition 2021) — the only thing needed for the backend + tests.
- Optional: **.NET SDK 10** (Mode-B Roslyn analyzer, Mode-D harness), **Docker**
  (+ `runsc` for gVisor), **Node.js** (Ubiq RoomServer, fetched separately),
  **Unity 6000.5.x** (VR client), **Python 3** (red-team tooling).

A pinned toolchain is recommended; add a `rust-toolchain.toml` with
`channel = "1.96"` if you want deterministic builds.

## Everyday commands (run from the repository root)

```bash
cargo check --workspace
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace          # 164 tests, fully offline
cargo fmt --all                 # or: cargo fmt --all -- --check
cargo build --workspace --release
cargo deny check                # supply-chain policy
```

`scripts/check.sh` runs the full gate (fmt-check + clippy + test + release +
deny). The workspace is `unsafe`-free and panic-averse by lint policy
(`unsafe_code = forbid`; `unwrap`/`expect`/`panic` denied in library crates).

## Optional services

```bash
dotnet build services/roslyn-analyzer/RoslynAnalyzer.csproj   # Mode-B analyzer
python3 -m py_compile redteam/*.py                            # red-team tooling
bash -n scripts/*.sh                                          # script syntax
```

## Validation status at this snapshot

All of the following pass on the reference machine (Rust 1.96, .NET 10):
`cargo fmt --check`, `cargo check`, `cargo clippy -D warnings` (0 warnings),
`cargo test` (**164 passed, 0 failed, 0 ignored**), `cargo deny check`
(advisories/bans/licenses/sources ok), and the .NET analyzer build (0 warnings,
0 errors). `cargo build --release` and `cargo audit` were not run in the
preparation environment (the latter needs network for its advisory DB;
`cargo deny`'s advisory check is used instead).

## Workspace layout

`crates/` (15 libs) + `apps/` (4 bins) + `tests/` (integration) form one Cargo
workspace rooted at the repository root (`Cargo.toml`). `services/` are .NET
projects; `scripts/`, `redteam/`, `unity/`, `unity-examples/` are tooling and
client code. See [ARCHITECTURE.md](ARCHITECTURE.md).

## Configuration

All configuration is via environment variables; copy `.env.example` to `.env`
for local use (never commit `.env`). Key flags: `DCVR_MODE_A`,
`DCVR_CSHARP_RESEARCH`, `DCVR_ROSLYN_URL`, `DCVR_ADMIN_PORT`/`DCVR_ADMIN_TOKEN`,
`DCVR_STT_OPENAI`, `OPENAI_API_KEY`, `DCVR_SANDBOX_DOCKER_RUNTIME`. Unset
credentials ⇒ offline mocks.
