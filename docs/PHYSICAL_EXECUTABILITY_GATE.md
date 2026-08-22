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
