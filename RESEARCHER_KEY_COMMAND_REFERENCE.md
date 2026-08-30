# C1 key/lock command reference

For researcher diagnostics and training only; do not present this sheet to participants unless the study protocol explicitly permits it.

Supported natural forms for a configured key and lock:

- “use the Golden Key with the desk drawer lock”
- “use the Golden Key on the desk drawer lock”
- “apply the Golden Key to the desk drawer lock”
- “insert the Golden Key into the desk drawer lock”
- “put the Golden Key in the desk drawer lock”

The server maps these forms to canonical `USE_WITH`. Unity verifies the authoritative `QuestLockController` binding: the correct key unlocks the configured lock, refreshes scene context and emits `LOCK_UNLOCKED`; an incorrect key retains the lock and shows “That key does not fit this lock.” A later `OPEN` command opens the associated drawer normally; unlocking does not automatically open it.
