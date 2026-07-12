#!/usr/bin/env bash
# Stop the whole DreamCodeVR+ stack (backend + RoomServer).
pkill -9 -f 'target/debug/dreamcodevr-server' 2>/dev/null || true
pkill -9 -f 'node app.js'                     2>/dev/null || true
echo "[stop-all] DreamCodeVR+ stack stopped."
