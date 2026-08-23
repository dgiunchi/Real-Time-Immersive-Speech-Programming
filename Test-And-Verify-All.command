#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DreamCodeVR+ — double-click launcher for the Verification Suite (macOS).
# ─────────────────────────────────────────────────────────────────────────────
set -u
cd "$(dirname "$0")"
export PATH="$HOME/.cargo/bin:/opt/homebrew/bin:/usr/local/bin:$PATH"

clear
echo "  ╔══════════════════════════════════════════════════════════════════╗"
echo "  ║           DreamCodeVR+  —  Verify All Tests & Pipeline           ║"
echo "  ╚══════════════════════════════════════════════════════════════════╝"
echo "  This will run the one-click test pipeline. Give it a minute."
echo

bash scripts/verify-all.sh

echo
echo "Press any key to close this window..."
read -n 1 -s
