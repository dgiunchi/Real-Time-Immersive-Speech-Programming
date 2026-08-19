# Quest 3 researcher workflow

1. Put PC and Quest on the same LAN; start the speech, Ubiq/vendor and researcher-control services.
2. Set `StudyConfiguration.researcherControlBaseUrl` to `http://PC_LAN_IP:3004`.
3. In Unity configure the existing vendor `NetworkScene` connection endpoint to the PC LAN host and its vendor port. `RoomJoiner` only joins its room GUID after that connection exists.
4. Ensure Android permits cleartext HTTP for the LAN control URL and the PC firewall permits port 3004 plus the Ubiq service port.
5. Launch on Quest, wait for a Ubiq peer UUID, then hold both menu-or-primary buttons for one second.
6. Point with a controller ray and use trigger to click the panel. Select the condition and press START; continue only after `SESSION READY`.
7. To change condition during an active session, select the new condition then press START; this deliberately performs server restart. Use RESET or END as appropriate.
8. Close the panel before participant speech testing. PTT is suppressed while the panel is visible.

Required physical verification: LAN room join, HTTP reachability, panel ray click, gesture toggle, PTT isolation, all C1/C2/C3 server flows and a C3 generated-task payload.
