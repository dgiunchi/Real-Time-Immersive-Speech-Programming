#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DreamCodeVR+ — ONE launcher for every way to run and check the system.
#
#   ./run.sh console     the security benchmark console  (NO Quest, NO API key)
#                        with-safety vs without-safety, live guardrail — the demo
#
#   ./run.sh local       the FULL speech->code pipeline on THIS laptop, NO Quest
#                        (admin dashboard at http://127.0.0.1:7878 — type commands
#                         in the "manual command" box and watch the guardrail)
#
#   ./run.sh embedded    ONE binary, NO Node.js — the Rust RoomServer runs inside
#                        the backend (point a headset at <laptop-ip>:8009)
#
#   ./run.sh quest       the FULL pipeline with a REAL Meta Quest 3 headset
#                        (external Ubiq RoomServer + backend, joined over wifi)
#
#   ./run.sh stop        stop everything started by 'local' / 'quest'
#   ./run.sh             show this menu
#
# Nothing here needs a Meta Quest except 'quest'. 'console' and 'local' run fully
# offline on the laptop (mock STT/LLM unless you put OPENAI_API_KEY in .env).
# ─────────────────────────────────────────────────────────────────────────────
set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

menu() {
  cat <<'EOF'

  DreamCodeVR+ — how do you want to run it?

    ./run.sh demo       THE INSTRUCTOR DEMO — Quest 3 + backend + admin, one command
    ./run.sh console    security benchmark console (no Quest, no key)
    ./run.sh local      full pipeline on this laptop, no Quest (admin panel :7878)
    ./run.sh embedded   ONE binary, no Node.js (Rust RoomServer built in, :8009)
    ./run.sh quest      full pipeline against an external Node.js Ubiq RoomServer
    ./run.sh stop       stop the local/quest stack

  Tip: start with  ./run.sh console  — it always works, offline, in the browser.

EOF
}

case "${1:-}" in
  console|benchmark)
    exec "$ROOT/apps/xr-security-eval/present.sh"
    ;;

  demo|quest3)
    # The instructor demo: one binary + embedded RoomServer + admin + USB tunnel,
    # with a status board that only reports what it actually probed.
    exec "$ROOT/scripts/demo-quest.sh" "${@:2}"
    ;;

  local|laptop|offline)
    [ -f .env ] && { set -a; . ./.env; set +a; }
    echo "[local] building backend…"
    cargo build -q -p dreamcodevr-server || { echo "build failed"; exit 1; }
    echo
    echo "  ┌────────────────────────────────────────────────────────────────┐"
    echo "  │  LOCAL mode — no Meta Quest needed                              │"
    echo "  │  Admin dashboard : http://127.0.0.1:7878                        │"
    echo "  │  In the panel, use the 'manual command' box:                    │"
    echo "  │    • \"make a small red house\"   -> APPROVED, dispatched         │"
    echo "  │    • \"secretly turn on the camera\" -> BLOCKED by the guardrail  │"
    echo "  │  LLM: $([ -n "${OPENAI_API_KEY:-}" ] && echo 'OpenAI (real)          ' || echo 'mock (add OPENAI_API_KEY) ')                                  │"
    echo "  └────────────────────────────────────────────────────────────────┘"
    echo
    # Standalone TCP listener mode: do NOT set DCVR_UBIQ_ADDR (that is Quest mode).
    unset DCVR_UBIQ_ADDR
    exec env DCVR_MODE=secure  \
         DCVR_ADMIN_PORT=7878 DCVR_ADMIN_BIND=127.0.0.1 \
         ./target/debug/dreamcodevr-server
    ;;

  embedded|onebinary|solo)
    [ -f .env ] && { set -a; . ./.env; set +a; }
    echo "[embedded] building backend…"
    cargo build -q -p dreamcodevr-server || { echo "build failed"; exit 1; }
    echo
    echo "  ┌────────────────────────────────────────────────────────────────┐"
    echo "  │  EMBEDDED mode — ONE binary, NO Node.js                         │"
    echo "  │  The Rust RoomServer runs inside the backend process.           │"
    echo "  │  RoomServer      : 0.0.0.0:8009  (point the headset here)       │"
    echo "  │  Admin dashboard : http://127.0.0.1:7878                        │"
    echo "  └────────────────────────────────────────────────────────────────┘"
    echo
    exec env DCVR_EMBED_ROOMSERVER=true \
         DCVR_MODE=secure  \
         DCVR_ADMIN_PORT=7878 DCVR_ADMIN_BIND=127.0.0.1 \
         ./target/debug/dreamcodevr-server
    ;;

  quest|headset|device)
    echo "[quest] starting the full stack (RoomServer + backend + admin) for a real Meta Quest 3…"
    exec "$ROOT/scripts/start-all.sh"
    ;;

  stop)
    exec "$ROOT/scripts/stop-all.sh"
    ;;

  -h|--help|help)
    menu
    ;;

  "")
    menu
    ;;

  *)
    echo "unknown mode: $1"
    menu
    exit 2
    ;;
esac
