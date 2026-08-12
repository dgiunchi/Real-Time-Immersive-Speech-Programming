# DreamCodeVR+ — agent handoff

**`CLAUDE.md` in this directory is the single source of truth. Read it first, in full.**

This file used to be a full copy of the handoff. That copy drifted out of date and described a
branch two sessions stale, which is exactly the failure it was meant to prevent — so it is now a
pointer instead of a duplicate. Do not re-expand it into a second handoff.

`CLAUDE.md` contains:

- **Exact state** — current branch/HEAD, what was verified when, and what is only *recorded*
  from an earlier session rather than re-verified.
- **⛰ Quest Demo Progress Ladder** — the permanent `FUNCTIONAL → BASIC → ADVANCED → WOW`
  staging rule, the per-stage checklists, the live progress block, and the idea backlogs.
  **Update it whenever you verify something; never tick a box you have not observed working.**
- **Working rules** — do not touch the thesis, do not push without explicit approval, run the
  gate before finishing a change, and any guardrail change needs a red-team test.
- **Session log** — append a dated line at the end of every session.

Quick orientation, if you read nothing else:

- MSc dissertation project (Sandeep Rai, University of Birmingham): a Rust safety layer for a
  speak-to-code VR system. The guardrail (`crates/csharp-policy`) is the security core.
- `./run.sh console | local | embedded | quest | stop` is the launcher.
- `bash scripts/verify-all.sh` verifies the whole system against a recorded baseline.
- Honest framing matters more than impressive numbers. Separate *implemented* / *tested* /
  *live-verified* / *designed*, and never round the residual away.
