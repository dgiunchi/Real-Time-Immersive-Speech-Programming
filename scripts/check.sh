#!/usr/bin/env bash
# Full local quality gate. Run before every commit.
set -euo pipefail
cd "$(dirname "$0")/.."
echo "==> rustfmt"
cargo fmt --all -- --check
echo "==> clippy (-D warnings)"
cargo clippy --workspace --all-targets -- -D warnings
echo "==> tests"
cargo test --workspace
echo "==> build"
cargo build --workspace
echo "OK: all checks passed"
