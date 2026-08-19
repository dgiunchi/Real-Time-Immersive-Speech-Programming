# Researcher panel

The panel is available only in researcher mode or a debug build. Toggle it with F5 on desktop, or hold menu-or-primary on both Ubiq controllers for one second on Quest. It is a world-space `XRUICanvas`; bootstrap adds an EventSystem and a missing `XRUIRaycaster` for each hand.

Set `StudyConfiguration.researcherControlBaseUrl` to `http://PC_LAN_IP:3004` for Quest. Select C1/C2/C3, then press START. If a session is already active, START uses server restart for the selected condition. Continue only after `SESSION READY`; END and RESET change Unity state only after the corresponding server response succeeds.

While the panel is visible the participant PTT path is suppressed, so controller trigger can be used for UI. Close the panel before testing voice input. Local drawer/color/behavior/next-task buttons are clearly local test injections, not server E2E.

Manual check: on device verify the panel opens, both rays can hover/click, PTT stays off while open, and no duplicate EventSystem/raycaster warning appears.
