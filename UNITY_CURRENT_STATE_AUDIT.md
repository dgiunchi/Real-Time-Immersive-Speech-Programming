# DreamCodeVR2 current state

The DreamCodeVR2 scene owns the retained Ubiq/STT services; `VerticalSliceRuntimeBootstrap` adds the experimental runtime, researcher panel, protocol client, condition manager, quest validation and dynamic-story controller.

The client now statically implements canonical 101 receive names and flat 102 result/event messages with the peer UUID prefix. Authoring execution is server-driven: proposal display/confirmation does not execute locally, while execution and undo return `AuthoringAck` only after their result. C1 predefined commands, C2 authoring and C3 dynamic task activation are condition-gated.

The researcher panel can select a pending condition and START will restart an already active server session with it. It dynamically provisions one EventSystem and missing Ubiq XR UI raycasters. Panel visibility suppresses PTT gain, and hot-path controller/dynamic-task searches are cached.

Still requiring manual confirmation: server payload compatibility for C3 success conditions, Ubiq vendor LAN endpoint configuration, Android cleartext HTTP settings, physical Quest panel interaction and complete server E2E.
