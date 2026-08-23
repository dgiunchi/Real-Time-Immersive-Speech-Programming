#!/usr/bin/env bash
# Launch the backend fully detached (survives the parent shell / harness task reaping).
set -euo pipefail
cd "$(dirname "$0")/.."

# Load secrets (OPENAI_API_KEY) without echoing them.
set -a
# shellcheck disable=SC1091
. ./.env 2>/dev/null || true
set +a

mkdir -p .run-logs
pkill -9 -f '[t]arget/debug/dreamcodevr-server' 2>/dev/null || true
sleep 1

setsid env \
  DCVR_MODE=baseline \
   \
  DCVR_STT_OPENAI=true \
  DCVR_ADMIN_PORT=7878 \
  DCVR_ADMIN_BIND=0.0.0.0 \
  DCVR_LLM_TIMEOUT_MS=180000 \
  DCVR_UBIQ_ADDR=127.0.0.1:8009 \
  ./target/debug/dreamcodevr-server > .run-logs/backend.log 2>&1 < /dev/null &

pid=$!
echo "launched detached pid $pid"
exit 0
