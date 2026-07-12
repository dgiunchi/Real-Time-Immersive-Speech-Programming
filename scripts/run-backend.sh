#!/usr/bin/env bash
# Start the DreamCodeVR+ backend as a Ubiq service peer.
# Loads OPENAI_API_KEY / OPENAI_MODEL from .env (if present) so the real LLM is used.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [ -f .env ]; then set -a; . ./.env; set +a; fi
export DCVR_UBIQ_ADDR="${DCVR_UBIQ_ADDR:-127.0.0.1:8009}"
export DCVR_LLM_TIMEOUT_MS="${DCVR_LLM_TIMEOUT_MS:-180000}"  # 3 min: big builds (solar system) at reasoning=high can exceed 60s; fail-closed timeout would else reject them
cargo build -q -p dreamcodevr-server
echo "[run-backend] llm = $([ -n "${OPENAI_API_KEY:-}" ] && echo "OpenAI (${OPENAI_MODEL:-gpt-4o-mini})" || echo mock)  |  ubiq = $DCVR_UBIQ_ADDR"
exec ./target/debug/dreamcodevr-server
