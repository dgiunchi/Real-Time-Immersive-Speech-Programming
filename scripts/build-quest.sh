#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DreamCodeVR+ — build (and optionally install/launch) the Quest 3 client.
#
#   scripts/build-quest.sh                 release APK
#   scripts/build-quest.sh --dev           development build (profiler + full logs)
#   scripts/build-quest.sh --install       build, then adb install -r
#   scripts/build-quest.sh --run           build, install, launch, and tail logcat
#
# The APK is produced by Assets/Editor/DcvrBuild.cs so every player setting lives in
# reviewable source, not in serialized YAML. Nothing here needs the Unity GUI.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

UNITY="${DCVR_UNITY:-/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="$ROOT/unity-quest"
APK="$PROJECT/Builds/DreamCodeVRPlus.apk"
PKG="com.bham.dreamcodevrplus"
# Unity rejects a -logFile path under a dot-directory ("not a valid directory name"),
# which silently loses the build log exactly when a failure needs diagnosing.
LOGDIR="$ROOT/build-logs"; mkdir -p "$LOGDIR"
BUILD_LOG="$LOGDIR/unity-build.log"

# adb ships outside PATH on this machine; resolve it rather than assuming.
if ! command -v adb >/dev/null 2>&1; then
  for c in "$HOME/Downloads/platform-tools" "$HOME/Library/Android/sdk/platform-tools"; do
    [ -x "$c/adb" ] && export PATH="$PATH:$c" && break
  done
fi

DEV=""; DO_INSTALL=0; DO_RUN=0
for a in "$@"; do
  case "$a" in
    --dev)     DEV="-dcvrDevelopment" ;;
    --install) DO_INSTALL=1 ;;
    --run)     DO_INSTALL=1; DO_RUN=1 ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "unknown flag: $a" >&2; exit 2 ;;
  esac
done

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY (override with DCVR_UNITY)"; exit 1; }

echo "[build-quest] building${DEV:+ (development)} — this takes several minutes for IL2CPP…"
START=$(date +%s)
"$UNITY" -batchmode -quit -projectPath "$PROJECT" \
         -buildTarget Android -executeMethod DcvrBuild.BuildQuest $DEV \
         -logFile "$BUILD_LOG"
RC=$?
ELAPSED=$(( $(date +%s) - START ))

if [ $RC -ne 0 ] || [ ! -f "$APK" ]; then
  echo "[build-quest] BUILD FAILED (rc=$RC, ${ELAPSED}s). Errors:"
  grep -E "error CS[0-9]+|BuildFailedException|Error building|DcvrBuild\] (FAILED|EXCEPTION)" \
       "$BUILD_LOG" | head -30
  echo "  full log: $BUILD_LOG"
  exit 1
fi

echo "[build-quest] OK  $APK  ($(du -h "$APK" | cut -f1), ${ELAPSED}s)"

if [ $DO_INSTALL -eq 1 ]; then
  command -v adb >/dev/null 2>&1 || { echo "adb not found — cannot install"; exit 1; }
  adb get-state >/dev/null 2>&1 || { echo "no Quest attached — cannot install"; exit 1; }
  echo "[build-quest] installing to $(adb devices | sed -n 2p | cut -f1)…"
  adb install -r "$APK" || exit 1
fi

if [ $DO_RUN -eq 1 ]; then
  adb shell am force-stop "$PKG" 2>/dev/null
  adb logcat -c 2>/dev/null
  adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
  echo "[build-quest] launched; tailing logcat (Ctrl+C to stop)…"
  sleep 3
  adb logcat -s Unity:V DEBUG:E AndroidRuntime:E ModeC-Net:V
fi
