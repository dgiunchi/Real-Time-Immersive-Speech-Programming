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

## First recorded run

Run on macOS with editor `6000.3.19f1` and `--allow-version-mismatch`, because the
pinned `6000.3.9f1` was not installable at the time. The result is therefore
recorded as `gateValid: false` and is not evidence for the pinned method version.

| Check | Result |
|---|---|
| build 1 exit status | 0 |
| build 1 rewrote the scene | yes |
| build 1 compile errors | 0 |
| build 2 exit status | 0 |
| build 2 rewrote the scene | yes |
| two builds byte identical | **yes** |
| rebuild matches the committed scene | **no** |

Two things follow.

The builder is deterministic within a run: two consecutive builds produced
identical bytes. That part of the gate holds.

The rebuild does not reproduce the committed scene. The committed scene was
generated on Windows with `6000.3.9f1`; this rebuild used macOS with
`6000.3.19f1`. Editor version, host platform, and the possibility that the
committed scene predates the current builder source are all plausible causes, and
they cannot be separated without the pinned editor. Re-run the gate with
`6000.3.9f1` installed before drawing any conclusion. If it still differs there,
the committed scene is stale relative to the builder and should be regenerated.

### Two ways this check can lie, both now handled

The builder registers the scene in `EditorBuildSettings` by its **imported asset
GUID**. Deleting the scene before a build to prove the build really ran destroys
that GUID, and the builder throws:

```
InvalidOperationException: The study scene was not registered first, enabled,
and with its imported GUID.
```

So the script does not delete the scene. It records the modification time and
requires the file to have been rewritten, which detects a build that never ran
without breaking the build.

A build that throws still leaves a partial scene on disk. Hashing that and
comparing it to another partial scene compares two failures and can report a
determinism problem that is really a build problem. The script now treats a
non-zero exit as fatal and stops before any comparison.
