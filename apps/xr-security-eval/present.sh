#!/usr/bin/env bash
# Launch the DreamCodeVR+ live code-admission console for a presentation.
#
#   apps/xr-security-eval/present.sh              # starts protected (guardrail ON)
#   DCVR_GUARDRAIL=off apps/xr-security-eval/present.sh   # start already bypassed
#
# Then, in a SECOND terminal, flip the guardrail live while presenting:
#   apps/xr-security-eval/guardrail.sh off        # bypass  → attacks reach the headset
#   apps/xr-security-eval/guardrail.sh on         # protected → attacks blocked
set -euo pipefail
cd "$(dirname "$0")/../.."   # repo root (this script lives in apps/xr-security-eval)

PORT="${XR_DEMO_PORT:-7979}"
GUARD="${DCVR_GUARDRAIL:-on}"   # on = protected (default), off = bypassed

echo "building the console (release)…"
cargo build --release -q -p xr-security-eval --bin xr-security-demo

echo
echo "  ┌──────────────────────────────────────────────────────────────┐"
echo "  │  DreamCodeVR+ live code-admission console                     │"
echo "  │  Console : http://127.0.0.1:${PORT}"
echo "  │  Guardrail starts: ${GUARD}"
echo "  │                                                              │"
echo "  │  Flip it live (second terminal):                             │"
echo "  │    apps/xr-security-eval/guardrail.sh off   # bypass         │"
echo "  │    apps/xr-security-eval/guardrail.sh on    # protected      │"
echo "  └──────────────────────────────────────────────────────────────┘"
echo

XR_DEMO_PORT="$PORT" DCVR_GUARDRAIL="$GUARD" exec ./target/release/xr-security-demo
