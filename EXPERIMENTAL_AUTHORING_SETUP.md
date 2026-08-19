# Experimental authoring setup

Open `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity` and enter Play mode. `VerticalSliceRuntimeBootstrap` installs the DreamCodeVR2 runtime automatically.

For researcher testing, enable `StudyConfiguration.researcherMode` or run a development build, then press F5 to show `ExperimentalResearcherPanel`. The panel changes C1/C2/C3 through the real condition manager and resets the playthrough between conditions.

C1 accepts predefined drawer commands. C2 and C3 accept the deterministic SceneAPI/BehaviorAPI calls. C3 waits for the server `next_task` protocol message after a task completes.
