#!/usr/bin/env bash
# Mode-D sandbox wrapper (one-shot CLI form).
#
# VERIFICATION NOTE: the LIVE-VERIFIED Mode-D path is the Rust DockerSandboxRunner
# (apps/sandbox-runner), which streams the job to the container stdin and enforces
# a wall-clock timeout + `docker kill`. This script is the equivalent shell form
# and is intended for manual/deployment use. Plain Docker is a SOFT boundary
# (NIST SP 800-190, runc "Leaky Vessels" CVE-2024-21626); use
# scripts/sandbox-run-gvisor.sh for the real Axis-B isolation (gVisor/runsc).
set -euo pipefail
JOB_DIR="${1:?usage: sandbox-run-docker.sh <job_dir containing Job.cs>}"
# The harness image reads the untrusted C# from STDIN (see services/sandbox-worker).
cat "${JOB_DIR}/Job.cs" | docker run --rm -i \
  --network=none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --memory=256m --memory-swap=256m --cpus=0.5 --pids-limit=64 \
  --cap-drop=ALL --security-opt=no-new-privileges --user 65534:65534 \
  dcvr-sandbox-harness:local
