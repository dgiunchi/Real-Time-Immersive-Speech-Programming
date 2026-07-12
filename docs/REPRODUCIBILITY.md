# Reproducibility

Instructions live in **[BUILD_AND_RUN.md](BUILD_AND_RUN.md)**. This document is the
**evidence**: the exact environment, commands, and results of the verified build.

## Environment of record

| Component | Version (verified) |
|---|---|
| OS | Ubuntu Linux, kernel 7.0, x86-64 |
| Rust / Cargo | 1.96.0 (pinned by `rust-toolchain.toml`) |
| rustfmt / clippy | 1.9.0 / 0.1.96 |
| cargo-deny | 0.19 |
| .NET SDK | 10.0.109 |
| Python | 3.14 (standard library only) |
| Bash | 5.x |

## Verified commands and results (offline, hermetic)

Run from the repo root; all offline and API-free:

```bash
cargo fmt --all -- --check
cargo check --workspace --locked
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace --locked
cargo build --workspace --release --locked
cargo deny check
dotnet build services/roslyn-analyzer/RoslynAnalyzer.csproj
dotnet build services/sandbox-worker/harness/Harness.csproj
python3 -m py_compile redteam/corpus_gen.py redteam/run_campaign.py
bash -n scripts/*.sh
```

Results (this snapshot):

- **`cargo test --workspace`: 164 passed, 0 failed, 0 ignored** — the offline test path
  needs **only Rust** and uses mock STT/LLM/Roslyn clients (no key, no network). (Was 160;
  4 boolean-env-parser tests were added in this release.)
- **Clippy: 0 warnings** (`-D warnings`); `cargo fmt --check`: clean.
- **`cargo deny check`: ok** (advisories / bans / licenses / sources).
- **.NET analyzer + sandbox harness: build with 0 warnings / 0 errors.**
- **Python compile + `bash -n`: pass.**

## Fresh-build verification

Building from a **clean selective copy** (no `.git`, no `target/`, no `.env`,
no build outputs) reproduces the same result: the full gate above passes and the
**offline mock demonstration runs** — the backend binds `127.0.0.1:9098` with mock
clients, the `fake-quest-client` default scenario connects and receives a validated
action-plan decision, and the backend stops cleanly. **No API key, no paid API, no
external/university host, and no generated C# on the host** are involved.
`bash scripts/verify-fresh-build.sh` runs this gate in one shot.

## Offline smoke test

```bash
cargo test --workspace --locked              # 164 tests, offline
cargo run -p dreamcodevr-server &            # backend with mock STT/LLM (127.0.0.1:9098)
cargo run -p fake-quest-client               # built-in demo scenario -> validated decision
```

## The Ubiq RoomServer (fetched separately, not vendored)

The live VR loop needs the Ubiq RoomServer — **not** included (third-party UCL/Ubiq
code). Obtain it from the Ubiq project and run it on TCP `:8009`;
`scripts/run-roomserver.sh` expects it under `vendor/ubiq-roomserver/`. The backend
joins it via `DCVR_UBIQ_ADDR`. See [BUILD_AND_RUN.md §18](BUILD_AND_RUN.md).

## Not runtime-tested here (require external resources / hardware)

- **Live VR loop** (RoomServer + Unity + Quest): the authored Unity C# was statically
  reviewed but **not compiled/run** in this environment (no Unity toolchain). Quest
  3 / IL2CPP is future work (see [LIMITATIONS.md](LIMITATIONS.md)).
- **Mode-D Docker/gVisor** live containment run: not executed (the .NET harness builds).
- **Real OpenAI / Whisper** paths: not executed (would need a key + incur cost). The
  test suite never calls a paid API.

## Regenerating the red-team corpus (not committed)

```bash
python3 redteam/corpus_gen.py redteam/corpus.json      # ~1,057-vector corpus (offline)
python3 redteam/run_campaign.py --layer csharp         # fire at the validator (offline)
python3 redteam/run_campaign.py --layer nl             # needs the backend running
```

Results land in `redteam/results_*.json` (gitignored). `--layer csharp` is offline;
`--layer nl` needs the backend up (mocks by default, no paid API).

## Network configuration

The old lab-specific addresses are **no longer hardcoded**; supply your own via
`DCVR_UBIQ_ADDR` (app) and `DCVR_ROOMSERVER_HOST` / `DCVR_ALIAS_IP` (helper scripts).
`scripts/show-ip.sh` and `scripts/open-firewall.sh` assist LAN/Quest setup. Full
scenarios and Wi-Fi caveats: [BUILD_AND_RUN.md §19](BUILD_AND_RUN.md).
