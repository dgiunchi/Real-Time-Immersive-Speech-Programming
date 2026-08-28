# Researcher panel

The panel is available only in researcher mode or a debug build. Toggle it with F5 on desktop, or hold the **left controller Y button** for one second on Quest. This uses Unity XR `CommonUsages.secondaryButton`; continuing to hold it does not re-toggle, and release re-arms it. It is a world-space `XRUICanvas`; the installed Ubiq `XRUIRaycaster` points with its ray and uses the controller trigger for pointer down/up/click.

Deployment endpoints are Ubiq `130.136.2.161:50000`, Researcher API `http://130.136.2.161:50001`, and server-side STT `130.136.2.161:50101`. Select C1/C2/C3, then press START. If a session is already active, START uses server restart for the selected condition. Continue only after `SESSION READY`; END and RESET change Unity state only after the corresponding server response succeeds.

While the panel is visible the participant PTT path is suppressed, so controller trigger can be used for UI. With the panel closed, PTT is enabled only after `SESSION READY`; otherwise the participant sees `NO ACTIVE SESSION` and no STT start/audio packets are sent. `PROCESSING` automatically leaves after its 10-second configured timeout if no relevant NID101 response arrives. Local drawer/color/behavior/next-task buttons are clearly local test injections, not server E2E.

The default panel exposes only session controls, condition, concise status, MARK TEST and ADVANCED. Advanced contains context refresh, object selection and local test controls. While open, the overlapping participant UI remains visible but does not receive raycasts.

Manual check: hold LEFT Y, point a ray at C1 and press trigger, then verify the `XR_UI_*`, `RESEARCHER_BUTTON_CLICK`, and session logs. PTT stays off while open and before session readiness.
