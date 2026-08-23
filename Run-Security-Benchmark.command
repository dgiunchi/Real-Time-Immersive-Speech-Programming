#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DreamCodeVR+ — double-click launcher for the Security Benchmark (macOS).
# ─────────────────────────────────────────────────────────────────────────────
set -u
cd "$(dirname "$0")"
export PATH="$HOME/.cargo/bin:/opt/homebrew/bin:/usr/local/bin:$PATH"

clear
echo "  ╔══════════════════════════════════════════════════════════════════╗"
echo "  ║          DreamCodeVR+  —  Security Evaluation Benchmark          ║"
echo "  ╚══════════════════════════════════════════════════════════════════╝"
echo "  Running the 40-attack deterministic XR security benchmark..."
echo

cargo run --release -p xr-security-eval

echo
echo "Press any key to close this window..."
read -n 1 -s
