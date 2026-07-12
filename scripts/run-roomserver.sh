#!/usr/bin/env bash
# Start the Ubiq RoomServer (TCP :8009 — used by DreamCodeVR+ — / WSS :8010).
#
# The Ubiq RoomServer is third-party and is NOT redistributed in this repository.
# Fetch it once into vendor/ubiq-roomserver/ as described in docs/REPRODUCIBILITY.md.
# The first run does `npm install --ignore-scripts` (network needed once), after
# which node_modules is reused offline.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRV="$HERE/vendor/ubiq-roomserver"
if [ ! -d "$SRV" ]; then
  echo "[run-roomserver] ERROR: $SRV not found." >&2
  echo "[run-roomserver] The Ubiq RoomServer is not bundled; fetch it into" >&2
  echo "[run-roomserver]   vendor/ubiq-roomserver/  (see docs/REPRODUCIBILITY.md)." >&2
  exit 1
fi
cd "$SRV"

# Node: the nvm install has no system-wide symlink, so add it to PATH if needed.
if ! command -v node >/dev/null 2>&1; then
  if [ -x "$HOME/.nvm/versions/node/v24.18.0/bin/node" ]; then
    export PATH="$HOME/.nvm/versions/node/v24.18.0/bin:$PATH"
  else
    echo "[run-roomserver] ERROR: node not found on PATH or at the expected nvm location." >&2
    exit 1
  fi
fi

# Dependencies are vendored; restore only if missing (e.g. cleaned checkout).
if [ ! -d node_modules ]; then
  echo "[run-roomserver] node_modules missing -> npm install --ignore-scripts"
  npm install --ignore-scripts
fi

# Dev self-signed cert/key for the WSS :8010 listener (NOT committed to the repo).
if [ ! -f cert.pem ] || [ ! -f key.pem ]; then
  echo "[run-roomserver] generating dev self-signed cert/key (localhost) ..."
  openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem \
    -days 365 -subj "/CN=localhost" >/dev/null 2>&1
fi

echo "[run-roomserver] starting vendored Ubiq RoomServer (TCP 8009 / WSS 8010) ..."
exec node app.js
