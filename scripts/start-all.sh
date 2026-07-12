#!/usr/bin/env bash
# Start the WHOLE DreamCodeVR+ stack and keep it running after you close the
# terminal: the Ubiq RoomServer (:8009/:8010, fetched separately) + the Rust backend with the
# admin panel (:7878), in Mode A/B (validated runtime C#), joined to the Ubiq room.
#
# Run it from anywhere:
#   <repository-root>/scripts/start-all.sh
# or:
#   cd dreamcodevr-plus && scripts/start-all.sh
#
# Everything is detached (setsid+nohup), so it survives the terminal closing.
# Logs:  .run-logs/{roomserver,backend}.log
# Stop:  scripts/stop-all.sh
set -u
cd "$(dirname "${BASH_SOURCE[0]}")/.."
mkdir -p .run-logs

echo "[start-all] stopping any existing instances…"
pkill -9 -f 'target/debug/dreamcodevr-server' 2>/dev/null || true
pkill -9 -f 'node app.js'                     2>/dev/null || true
sleep 1

echo "[start-all] starting Ubiq RoomServer (:8009 / :8010)…"
setsid nohup scripts/run-roomserver.sh > .run-logs/roomserver.log 2>&1 < /dev/null &
for i in $(seq 1 60); do ss -ltn 2>/dev/null | grep -q ':8009' && break; sleep 0.5; done

echo "[start-all] starting backend + admin panel (:7878), joining Ubiq…"
# DCVR_STT_OPENAI=true -> REAL speech-to-text (OpenAI Whisper, key from .env). The VR
# client is mic-only (left-trigger push-to-talk), so mock STT would make it deaf.
setsid nohup env DCVR_MODE_A=true DCVR_CSHARP_RESEARCH=true DCVR_STT_OPENAI=true \
  DCVR_ADMIN_PORT=7878 DCVR_ADMIN_BIND=0.0.0.0 \
  scripts/run-backend.sh > .run-logs/backend.log 2>&1 < /dev/null &
for i in $(seq 1 180); do ss -ltn 2>/dev/null | grep -q ':7878' && break; sleep 0.5; done

echo
echo "================ DreamCodeVR+ stack ================"
ss -ltn 2>/dev/null | grep -q ':8009' && echo "  RoomServer      : UP  (127.0.0.1:8009)" || echo "  RoomServer      : DOWN — see .run-logs/roomserver.log"
ss -ltn 2>/dev/null | grep -q ':7878' && echo "  Backend + Admin : UP  (http://127.0.0.1:7878)" || echo "  Backend + Admin : DOWN — see .run-logs/backend.log"
grep -q 'llm = OpenAI' .run-logs/backend.log 2>/dev/null && echo "  LLM             : OpenAI (real)" || echo "  LLM             : mock  (put OPENAI_API_KEY in .env for real builds)"
echo "  Admin panel     : http://127.0.0.1:7878"
echo "  Logs            : .run-logs/"
echo "  Stop everything : scripts/stop-all.sh"
echo "==================================================="
echo
# Show the LAN IP to type into the Quest app ("Laptop server IP"). On an iPhone
# hotspot this changes each session, so it's printed on every start.
scripts/show-ip.sh 2>/dev/null || true
echo
echo "On a Quest headset: enter the IP above in the app, press \"Apply & Reconnect\", speak."
echo "In the Unity Editor instead: open DreamCodeVRPlus-Unity6-Networked, press Play (127.0.0.1)."
