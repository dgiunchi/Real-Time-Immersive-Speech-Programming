#!/usr/bin/env bash
# DreamCodeVR+ prerequisite checker ("doctor").
#
# Read-only: detects local tools and prints versions. It installs nothing, starts
# no services, makes no network/API calls, and changes no firewall settings.
# Exit status is non-zero ONLY when a REQUIRED prerequisite is missing.
#
#   bash scripts/doctor.sh
set -u
cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

REQUIRED_MISSING=0
ok()   { printf '  [ ok ]   %-9s %s\n' "$1" "$2"; }
opt()  { printf '  [ opt ]  %-9s %s\n' "$1" "$2"; }
miss() { printf '  [MISS]   %-9s %s\n' "$1" "$2"; }

# ver <cmd> <args...> — first non-empty line of the version output, or empty.
ver() { command -v "$1" >/dev/null 2>&1 && "$@" 2>&1 | head -1 || true; }

echo "DreamCodeVR+ prerequisite check"
echo "==============================="
echo
echo "Required (the offline Rust build + tests need only these):"

# rustc / cargo (required)
if command -v rustc >/dev/null 2>&1; then ok "rustc" "$(ver rustc --version)"; else miss "rustc" "install via rustup (https not fetched here) — REQUIRED"; REQUIRED_MISSING=1; fi
if command -v cargo >/dev/null 2>&1; then ok "cargo" "$(ver cargo --version)"; else miss "cargo" "install via rustup — REQUIRED"; REQUIRED_MISSING=1; fi

# rustfmt / clippy (required components)
if command -v cargo >/dev/null 2>&1 && cargo fmt --version >/dev/null 2>&1; then ok "rustfmt" "$(cargo fmt --version 2>&1 | head -1)"; else miss "rustfmt" "rustup component add rustfmt — REQUIRED for the format gate"; REQUIRED_MISSING=1; fi
if command -v cargo >/dev/null 2>&1 && cargo clippy --version >/dev/null 2>&1; then ok "clippy" "$(cargo clippy --version 2>&1 | head -1)"; else miss "clippy" "rustup component add clippy — REQUIRED for the lint gate"; REQUIRED_MISSING=1; fi

# bash (required for the helper scripts)
ok "bash" "${BASH_VERSION:-unknown}"

echo
echo "Optional (only for the paths you use):"

# cargo-deny (optional local gate)
if command -v cargo-deny >/dev/null 2>&1; then ok "cargo-deny" "$(ver cargo-deny --version)"; else opt "cargo-deny" "not found — 'cargo deny check' supply-chain gate unavailable (cargo install cargo-deny)"; fi
# .NET (optional: Mode-B analyzer + Mode-D harness)
if command -v dotnet >/dev/null 2>&1; then ok "dotnet" "$(ver dotnet --version) (Mode-B Roslyn analyzer / Mode-D harness)"; else opt "dotnet" "not found — optional (.NET SDK 10 for the Roslyn analyzer / sandbox harness)"; fi
# python3 (optional: red-team tooling)
if command -v python3 >/dev/null 2>&1; then ok "python3" "$(ver python3 --version) (red-team tooling)"; else opt "python3" "not found — optional (Python 3 for redteam/)"; fi
# docker (optional: Mode D)
if command -v docker >/dev/null 2>&1; then ok "docker" "$(ver docker --version) (optional Mode-D sandbox)"; else opt "docker" "not found — optional (Mode-D sandbox only)"; fi
# runsc/gVisor (optional: Mode D hardening)
if command -v runsc >/dev/null 2>&1; then ok "runsc" "$(ver runsc --version) (optional gVisor for Mode D)"; else opt "runsc" "not found — optional (gVisor hardening for Mode D)"; fi
# node (optional: Ubiq RoomServer, fetched separately)
if command -v node >/dev/null 2>&1; then ok "node" "$(ver node --version) (optional: run the separately-fetched Ubiq RoomServer)"; else opt "node" "not found — optional (Node.js runs the separately-fetched Ubiq RoomServer)"; fi
# misc network/util tools used by helper scripts (each has its own version flag)
for t in curl jq openssl ip ss lsof; do
  if command -v "$t" >/dev/null 2>&1; then
    case "$t" in
      ip)      v="$(ip -V 2>&1 | head -1)";;
      lsof)    v="$(lsof -v 2>&1 | grep -i 'revision' | head -1)";;
      openssl) v="$(openssl version 2>&1 | head -1)";;
      ss)      v="$(ss --version 2>&1 | head -1)";;
      *)       v="$(ver "$t" --version | head -1)";;
    esac
    ok "$t" "${v:-present}"
  else opt "$t" "not found — used by some helper scripts (optional)"; fi
done

echo
if [ "$REQUIRED_MISSING" -ne 0 ]; then
  echo "RESULT: missing a REQUIRED prerequisite. Install Rust 1.96 (rustup) with the"
  echo "        rustfmt + clippy components, then re-run. See PROJECT.md."
  exit 1
fi
echo "RESULT: all required prerequisites present. Offline build/test path is ready:"
echo "        cargo build --workspace --locked && cargo test --workspace --locked"
echo "        Optional tools above enable Mode-B/Mode-D/red-team/live-VR paths."
exit 0
