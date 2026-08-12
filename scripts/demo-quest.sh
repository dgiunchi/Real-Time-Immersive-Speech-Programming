#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DreamCodeVR+ — the instructor demonstration launcher.
#
# One command brings up the whole system for a live Quest 3 demo and reports what
# is ACTUALLY reachable. Nothing prints READY unless it was probed: a status board
# that lies is worse than no status board, because it fails in front of an audience.
#
#   scripts/demo-quest.sh              start everything, show status, hold
#   scripts/demo-quest.sh --no-launch  do not launch the app on the headset
#
# Ctrl+C tears down only what this script started.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PKG="com.bham.dreamcodevrplus"
ROOM_PORT=8009
ADMIN_PORT=7878
LOGDIR="$ROOT/.run-logs"; mkdir -p "$LOGDIR"
BACKEND_LOG="$LOGDIR/backend.log"
LAUNCH_APP=1
[ "${1:-}" = "--no-launch" ] && LAUNCH_APP=0

BACKEND_PID=""
REVERSE_SET=0

# ---- colours (plain if not a tty) -------------------------------------------
if [ -t 1 ]; then
  B=$'\033[1m'; DIM=$'\033[2m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; N=$'\033[0m'
else
  B=""; DIM=""; G=""; Y=""; R=""; N=""
fi
ok()   { printf "  %-16s ${G}%s${N}\n" "$1" "$2"; }
warn() { printf "  %-16s ${Y}%s${N}\n" "$1" "$2"; }
bad()  { printf "  %-16s ${R}%s${N}\n" "$1" "$2"; }

cleanup() {
  echo
  echo "[demo] stopping…"
  [ -n "$BACKEND_PID" ] && kill "$BACKEND_PID" 2>/dev/null
  [ "$REVERSE_SET" = "1" ] && adb reverse --remove tcp:$ROOM_PORT >/dev/null 2>&1
  wait "$BACKEND_PID" 2>/dev/null
  echo "[demo] done."
}
trap cleanup EXIT INT TERM

# ---- prerequisites -----------------------------------------------------------
command -v cargo >/dev/null 2>&1 || { echo "cargo not found — install Rust"; exit 1; }

# adb is not on PATH on this machine; resolve it rather than assuming.
if ! command -v adb >/dev/null 2>&1; then
  for c in "$HOME/Downloads/platform-tools" "$HOME/Library/Android/sdk/platform-tools"; do
    [ -x "$c/adb" ] && export PATH="$PATH:$c" && break
  done
fi

# A stale backend holding :8009 makes every later probe lie. Clear it first.
if lsof -nP -iTCP:$ROOM_PORT -sTCP:LISTEN >/dev/null 2>&1; then
  echo "[demo] port $ROOM_PORT busy — stopping the previous backend"
  pkill -f "target/debug/dreamcodevr-server" 2>/dev/null
  sleep 2
fi

[ -f .env ] && { set -a; . ./.env; set +a; }

echo "[demo] building backend…"
cargo build -q -p dreamcodevr-server || { echo "backend build failed"; exit 1; }

# ---- start the backend (one binary: RoomServer + pipeline + admin) -----------
# No DCVR_MODE_A / DCVR_CSHARP_RESEARCH: those put the backend in the Mode A/B
# research path, which sends C# the Quest cannot compile under IL2CPP. Mode C
# (bounded action plans) is the deployable architecture and the default.
DCVR_EMBED_ROOMSERVER=true \
DCVR_ADMIN_PORT=$ADMIN_PORT DCVR_ADMIN_BIND=127.0.0.1 \
  ./target/debug/dreamcodevr-server > "$BACKEND_LOG" 2>&1 &
BACKEND_PID=$!

# ---- wait for it to actually listen -----------------------------------------
BACKEND_UP=0
for _ in $(seq 1 60); do
  if lsof -nP -iTCP:$ROOM_PORT -sTCP:LISTEN >/dev/null 2>&1; then BACKEND_UP=1; break; fi
  kill -0 "$BACKEND_PID" 2>/dev/null || break
  sleep 0.5
done

ADMIN_UP=0
if [ "$BACKEND_UP" = "1" ]; then
  for _ in $(seq 1 40); do
    if curl -sf "http://127.0.0.1:$ADMIN_PORT/api/health" >/dev/null 2>&1; then ADMIN_UP=1; break; fi
    sleep 0.25
  done
fi

# ---- device + tunnel ---------------------------------------------------------
QUEST_ID=""
if command -v adb >/dev/null 2>&1; then
  QUEST_ID="$(adb devices 2>/dev/null | awk '$2=="device"{print $1; exit}')"
  if [ -n "$QUEST_ID" ]; then
    # USB tunnel: the client's built-in default host is 127.0.0.1:8009, so with
    # adb reverse the demo works with no discovery and no configuration at all.
    # This is the fallback that does not depend on hotspot broadcast behaviour.
    adb reverse tcp:$ROOM_PORT tcp:$ROOM_PORT >/dev/null 2>&1 && REVERSE_SET=1
  fi
fi

LAN_IP="$(ipconfig getifaddr en0 2>/dev/null)"
[ -n "$LAN_IP" ] || LAN_IP="$(ifconfig 2>/dev/null | awk '/inet /&&$2!="127.0.0.1"{print $2; exit}')"

APK_STATE="not built"
[ -f "$ROOT/unity-quest/Builds/DreamCodeVRPlus.apk" ] && APK_STATE="built"
if [ -n "$QUEST_ID" ] && adb shell pm list packages 2>/dev/null | grep -q "$PKG"; then
  APK_STATE="installed"
fi

DISCOVERY="off"
grep -q "discovery. beacon up" "$BACKEND_LOG" 2>/dev/null && DISCOVERY="on"

# ---- status board ------------------------------------------------------------
echo
echo "${B}  DREAMCODEVR+  QUEST DEMO${N}"
echo "  ────────────────────────────────────────────────"
[ "$BACKEND_UP" = "1" ] && ok "Backend" "READY  pid $BACKEND_PID" || bad "Backend" "DOWN  (see $BACKEND_LOG)"
[ "$BACKEND_UP" = "1" ] && ok "RoomServer" "READY  :$ROOM_PORT (embedded, Rust)" || bad "RoomServer" "DOWN"
[ "$ADMIN_UP" = "1" ]   && ok "Admin" "READY  http://127.0.0.1:$ADMIN_PORT" || bad "Admin" "DOWN"
[ "$DISCOVERY" = "on" ] && ok "LAN discovery" "READY  UDP 8987/8988" || warn "LAN discovery" "not advertised"
if [ -n "$QUEST_ID" ]; then ok "Quest 3" "CONNECTED  $QUEST_ID"; else bad "Quest 3" "NOT CONNECTED"; fi
[ "$REVERSE_SET" = "1" ] && ok "USB tunnel" "READY  adb reverse :$ROOM_PORT" || warn "USB tunnel" "not set"
[ -n "$LAN_IP" ] && ok "LAN address" "$LAN_IP:$ROOM_PORT" || warn "LAN address" "unknown"
case "$APK_STATE" in
  installed) ok   "APK" "INSTALLED  $PKG" ;;
  built)     warn "APK" "built, not installed (scripts/build-quest.sh --install)" ;;
  *)         bad  "APK" "not built (scripts/build-quest.sh)" ;;
esac
ok "Mode" "C  (bounded action plan, no runtime compilation)"
ok "Guardrail" "DeployHardened"
if [ -n "${OPENAI_API_KEY:-}" ]; then
  ok "LLM" "OpenAI (live generation)"
else
  # Never dress the offline mock up as a live model result.
  warn "LLM" "MOCK — no OPENAI_API_KEY; say so during the demo"
fi
echo "  ────────────────────────────────────────────────"

if [ "$BACKEND_UP" != "1" ]; then
  echo "${R}  backend failed to start — not continuing${N}"
  exit 1
fi

if [ "$LAUNCH_APP" = "1" ] && [ -n "$QUEST_ID" ] && [ "$APK_STATE" = "installed" ]; then
  echo "  launching on the headset…"
  adb shell am force-stop "$PKG" >/dev/null 2>&1
  adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
  echo "  ${DIM}put the headset on — it must be worn (or awake) or Horizon OS pauses the app${N}"
fi

echo
echo "  Try:  curl -s -X POST http://127.0.0.1:$ADMIN_PORT/api/command \\"
echo "          -H 'content-type: application/json' \\"
echo "          -d '{\"command\":\"make it bright green and spin it\"}'"
echo
echo "  ${DIM}Ctrl+C to stop everything.${N}"
echo

wait "$BACKEND_PID"
