# Gate 7: physical executability

The first six review gates checked whether the method, declarations, logs, and generated scene agreed.
They did not prove that a participant could inhabit the scene and perform each declared action. Gate 7
blocks technical preflight unless the generated scene uses the canonical Ubiq XR player prefab, retains
its tracked HMD and two-hand interaction graph, provides participant-controlled movement, uses the
study-safe compiler rather than the demo `TestRoslyn` component, and wires the required task affordances.

## Automated evidence

- Unity's builder inspects the instantiated component graph, non-null head camera, distinct tracked hands,
  teleport rays, graspers, use routes, Teleport-tagged floor, task affordances, safe compiler, and D6 rules.
- Server readiness resolves Unity asset GUIDs and fails if the player/hand prefab chain, compiler,
  graspables, buttons, or teleport floor is absent.
- The regression suite mutates the XR player GUID in memory and observes readiness reject the scene.
- Two consecutive batch-mode builds must remain byte-identical.

## Headset evidence still required

Automation cannot prove comfort, tracking quality, controller reach, ray ambiguity, locomotion safety,
or that the door state is visually legible at the authored distance. Complete and sign
`docs/vr-study-rehearsal-checklist.md` before piloting or participant use.

## Running the determinism check

The two build requirement above is now executable:

```bash
cd Server
npm run verify:scene-determinism
```

It emits a JSON result and exits non zero unless every check passes. It fails
closed on the three ways a hand run produces a false PASS:

- Unity's exit status is lost when its output is piped, so a build that never
  launched looks successful. The script reads the real status and never pipes.
- If the build did not run, the scene file is unchanged, so hashing it before and
  after appears to match. The script deletes the scene first, so a build that
  does not run cannot masquerade as a stable one, and restores it from git if a
  build fails.
- Another `6000.3.x` editor will open the project and emit a scene, but that is
  not evidence for the pinned method version. The script refuses a mismatched
  editor and reports what is installed. `--allow-version-mismatch` overrides the
  refusal and marks the result `gateValid: false`.

It also compares the rebuild against the committed scene, so a rebuild that is
self consistent but differs from what is in the repository is reported rather
than silently accepted.
