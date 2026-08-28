# Quest 3 researcher workflow

1. Put PC and Quest on the same LAN; start the server-side STT (`130.136.2.161:50101`), Ubiq (`130.136.2.161:50000`) and Researcher Control (`http://130.136.2.161:50001`) services.
2. Set `StudyConfiguration.researcherControlBaseUrl` to `http://130.136.2.161:50001`.
3. The existing `NetworkScene` uses the Ubiq endpoint `130.136.2.161:50000`. `RoomJoiner` only joins its room GUID after that connection exists.
4. Ensure Android permits cleartext HTTP for the Researcher API and the firewall permits ports 50000, 50001 and the server-side STT port 50101.
5. Launch on Quest, wait for a Ubiq peer UUID, then hold **LEFT Y** for approximately one second to open the panel. Release Y before using it again.
6. Point with a controller ray and use its trigger to click the panel. Select the condition and press START; continue only after `SESSION READY`. Participant PTT is deliberately blocked until then.
7. To change condition during an active session, select the new condition then press START; this deliberately performs server restart. Use RESET or END as appropriate.
8. Close the panel before participant speech testing. PTT is suppressed while the panel is visible. If the server does not produce a relevant NID101 response, `PROCESSING` leaves automatically after the configured 10-second timeout and displays `NO SERVER RESPONSE`.

Controls: LEFT Y hold = panel; ray = point; trigger = UI click while panel is open; left trigger with panel closed plus SESSION READY = PTT; grip = gameplay grab.

Required physical verification: LAN room join, HTTP reachability, left-Y toggle, hover/click logs, PTT session guard/isolation, all C1/C2/C3 server flows and a C3 generated-task payload.
