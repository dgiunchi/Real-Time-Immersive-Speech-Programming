#!/usr/bin/env bash
# Fresh-build verification: the offline, hermetic gate a new checkout must pass.
# No paid APIs, no live services, no credentials, no Docker, no generated-C#
# execution. Fails fast on the first failing step. Complements scripts/check.sh by
# using --locked (reproducible from Cargo.lock) and by also compiling the Python
# and shell tooling.
#
#   bash scripts/verify-fresh-build.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

step() { printf '\n==> %s\n' "$1"; }

step "cargo fmt --all -- --check";                             cargo fmt --all -- --check
step "cargo check --workspace --locked";                      cargo check --workspace --locked
step "cargo clippy --workspace --all-targets -- -D warnings";  cargo clippy --workspace --all-targets -- -D warnings
step "cargo test --workspace --locked";                       cargo test --workspace --locked
step "cargo build --workspace --release --locked";            cargo build --workspace --release --locked

if command -v cargo-deny >/dev/null 2>&1; then
  step "cargo deny check";                                    cargo deny check
else
  printf '\n(skip) cargo-deny not installed — supply-chain gate skipped (optional).\n'
fi

if command -v python3 >/dev/null 2>&1; then
  step "python3 -m py_compile redteam/*.py";                  python3 -m py_compile redteam/corpus_gen.py redteam/run_campaign.py
fi

step "bash -n scripts/*.sh"
for f in scripts/*.sh; do bash -n "$f"; done

printf '\nOK: fresh-build verification passed.\n'
