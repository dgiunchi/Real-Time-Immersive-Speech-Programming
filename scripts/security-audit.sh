#!/usr/bin/env bash
# Supply-chain + secret audit. Non-fatal if optional tools are missing.
set -uo pipefail
cd "$(dirname "$0")/.."
echo "==> cargo audit (advisory DB)";        command -v cargo-audit >/dev/null && cargo audit || echo "  (cargo-audit not installed)"
echo "==> cargo deny (licenses/bans/sources)"; command -v cargo-deny  >/dev/null && cargo deny check || echo "  (cargo-deny not installed)"
echo "==> gitleaks (committed secrets)";       command -v gitleaks    >/dev/null && gitleaks detect --source . --redact --no-banner || echo "  (gitleaks not installed)"
echo "==> trufflehog (secrets)";               command -v trufflehog  >/dev/null && trufflehog filesystem . || echo "  (trufflehog not installed)"
echo "done"
