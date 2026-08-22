# Claude study-audit entrypoint

This branch must be audited against the attached AgenticXR paper and the proposed
complete Method before participant-study implementation is treated as approved.

Use the complete audit prompt at:

`C:\Users\Maisy\Documents\Codex\2026-08-21\ok-x20\work\claude_task_design_audit_prompt.txt`

That prompt also directs the reviewer to:

- the paper PDF;
- the complete proposed Method;
- the independent task-design audit;
- the live repository and Unity project;
- every commit on `codex/paper-study-implementation` after `8b2978b`.

## Commit-review rule

Run `git log --graph --decorate --oneline --all`, inspect the actual diff of every
post-`8b2978b` local commit, and audit any uncommitted changes separately. Commit
subjects are claims, not evidence. The final response must record the exact reviewed
HEAD, whether the working tree was clean, and a KEEP / AMEND / REVERT disposition for
every local commit.

## Authority order

1. Approved/preregistered study Method, once frozen by the investigators.
2. The paper for H1--H4, L1--L5, condition pairings, measures, and safety.
3. Actual executable behaviour and test evidence.
4. Repository documentation and commit messages.

Until investigator approval, the proposed Method is an auditable draft rather than a
source of authority. No commit may silently change the scientific design.
