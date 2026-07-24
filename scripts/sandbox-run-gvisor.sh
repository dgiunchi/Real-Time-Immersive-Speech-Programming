#!/usr/bin/env bash
# Mode-D sandbox wrapper WITH gVisor (runsc) — the real Axis-B kernel isolation.
#
# STATUS: NOT YET VERIFIED on this machine. Docker containment WAS live-verified
# gVisor adds a userspace kernel that also contains
# kernel-level escapes, but `runsc` is NOT installed here, so this path is
# scripted/documented only. Install runsc (gvisor.dev/docs/user_guide/install/) and test
# before claiming gVisor isolation in the dissertation.
#
# Identical to scripts/sandbox-run-docker.sh except for `--runtime=runsc`.
set -euo pipefail
JOB_DIR="${1:?usage: sandbox-run-gvisor.sh <job_dir containing Job.cs>}"
cat "${JOB_DIR}/Job.cs" | docker run --rm -i \
  --runtime=runsc \
  --network=none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --memory=256m --memory-swap=256m --cpus=0.5 --pids-limit=64 \
  --cap-drop=ALL --security-opt=no-new-privileges --user 65534:65534 \
  dcvr-sandbox-harness:local
