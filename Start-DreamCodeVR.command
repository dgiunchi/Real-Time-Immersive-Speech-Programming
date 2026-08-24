#!/usr/bin/env bash
cd "$(dirname "$0")"

cat << 'BANNER'

  ╔══════════════════════════════════════════════════════════════════╗
  ║                    DreamCodeVR+  —  starting                     ║
  ╠══════════════════════════════════════════════════════════════════╣
  ║  Preferred Quest link: USB + adb reverse, 127.0.0.1:8009         ║
  ║                                                                  ║
  ║  Admin dashboard on this laptop:  http://127.0.0.1:7878          ║
  ║  To stop: close this window or press Ctrl+C.                     ║
  ╚══════════════════════════════════════════════════════════════════╝

BANNER

./run.sh demo
