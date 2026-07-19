#!/usr/bin/env bash
# Hot-swap the RUNNING guardrail during a presentation — the "command change"
# that makes the server behave dynamically. No restart needed.
#
#   apps/xr-security-eval/guardrail.sh off        # bypass    → attacks reach the headset
#   apps/xr-security-eval/guardrail.sh on         # protected → attacks blocked before the headset
#   apps/xr-security-eval/guardrail.sh security    # security-only (misses XR manipulation)
set -euo pipefail
PORT="${XR_DEMO_PORT:-7979}"

case "${1:-}" in
  off|bypass)   MODE=bypass ;;
  on|protected) MODE=protected ;;
  security)     MODE=security ;;
  *) echo "usage: $0 on|off|security"; exit 2 ;;
esac

curl -s -X POST "http://127.0.0.1:${PORT}/api/mode" \
     -H 'content-type: application/json' -d "{\"mode\":\"${MODE}\"}" \
  | python3 -c "import sys,json;d=json.load(sys.stdin);print('  guardrail →',d['mode'].upper(),'\n ',d['label'])" 2>/dev/null \
  || echo "  could not reach the server on :${PORT} — is it running?  (start it with present.sh)"
