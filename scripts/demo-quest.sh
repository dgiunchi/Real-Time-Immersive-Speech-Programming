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
#   scripts/demo-quest.sh --creative   Mode A: arbitrary validated C# on the headset
#
# The two modes are the dissertation's two answers to the same question, and the
# difference is worth showing rather than hiding:
#
#   default    Mode C — the model's request becomes a BOUNDED ACTION PLAN. Whole
#              classes of attack are unrepresentable because there is no way to
#              express them in the plan vocabulary. Safe by construction, limited
#              by construction.
#   --creative Mode A — the model writes arbitrary C#, the guardrail validates it,
#              the analyzer compiles it here, and the headset INTERPRETS the IL
#              (IL2CPP cannot compile). Unlimited expressiveness; safety now rests
#              entirely on the guardrail catching what it is shown.
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
CREATIVE=0
for a in "$@"; do
  case "$a" in
    --no-launch) LAUNCH_APP=0 ;;
    --creative)  CREATIVE=1 ;;
  esac
done

BACKEND_PID=""
ANALYZER_PID=""
REVERSE_SET=0
ANALYZER_PORT=5099

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
  [ -n "$ANALYZER_PID" ] && kill "$ANALYZER_PID" 2>/dev/null
  [ "$REVERSE_SET" = "1" ] && adb reverse --remove tcp:$ROOM_PORT >/dev/null 2>&1
  wait "$BACKEND_PID" 2>/dev/null
  wait "$ANALYZER_PID" 2>/dev/null
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

# ---- optional: the Roslyn analyzer, which Mode A needs to COMPILE ------------
# Mode A used to be undemonstrable on a Quest 3 because IL2CPP is ahead-of-time and
# ships no C# compiler. The compile now happens here instead, and the headset
# interprets the IL — so Mode A needs this service running, and fails closed
# (sends nothing) without it rather than sending source the device cannot run.
ANALYZER_UP=0
MODE_A_ENV=()
if [ "$CREATIVE" = "1" ]; then
  echo "[demo] starting the Roslyn analyzer (Mode A compiles here)…"
  if command -v dotnet >/dev/null 2>&1; then
    dotnet run --project services/roslyn-analyzer/RoslynAnalyzer.csproj \
      > "$LOGDIR/analyzer.log" 2>&1 &
    ANALYZER_PID=$!
    for _ in $(seq 1 60); do
      curl -sf -m 1 "http://127.0.0.1:$ANALYZER_PORT/analyze" -X POST \
        -H 'content-type: application/json' -d '{"csharp":""}' >/dev/null 2>&1 \
        && { ANALYZER_UP=1; break; }
      kill -0 "$ANALYZER_PID" 2>/dev/null || break
      sleep 1
    done
  fi
  if [ "$ANALYZER_UP" = "1" ]; then
    MODE_A_ENV=(DCVR_MODE_A=true DCVR_CSHARP_RESEARCH=true
                "DCVR_ROSLYN_URL=http://127.0.0.1:$ANALYZER_PORT/analyze")
  else
    echo "[demo] analyzer did NOT start — staying in Mode C (see $LOGDIR/analyzer.log)"
    CREATIVE=0
  fi
fi

# ---- start the backend (one binary: RoomServer + pipeline + admin) -----------
# Mode C (bounded action plans) is the default: safe by construction, and the
# architecture the dissertation argues for. --creative switches on the Mode A arm.
env DCVR_EMBED_ROOMSERVER=true \
    DCVR_ADMIN_PORT=$ADMIN_PORT DCVR_ADMIN_BIND=127.0.0.1 \
    "${MODE_A_ENV[@]}" \
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
if [ "$CREATIVE" = "1" ]; then
  ok   "Mode" "A  (arbitrary validated C#, compiled here, interpreted on device)"
  ok   "Compiler" "Roslyn analyzer :$ANALYZER_PORT  ->  IL over NID-94"
else
  ok   "Mode" "C  (bounded action plan, no runtime compilation)"
fi
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
