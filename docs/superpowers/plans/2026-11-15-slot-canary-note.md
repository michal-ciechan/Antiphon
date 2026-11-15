# CARD-0824 slot canary
Both server/runner support flags and the server role gate must admit a slot lease; otherwise eligible work uses legacy mirrors.
The runner replays an existing task/preparation lease or chooses the lowest-numbered Free slot, creating within pool/disk limits when needed.
It fetches and verifies the exact task branch/base SHA, increments lease generation and records ownership in `/work/workspace-slots/ledger.json`.
Release requires a terminal task, stopped runner session, confirmed sync with no Pending/Held debt, and no live follow-up; Blocked keeps its lease.
The runner checks custody, retains evidence, resets/detaches to the published SHA and frees the slot; unsafe contents stay quarantined, never manually deleted.
Soft rollback sets server `Delegation:WorkspaceSlotRoles=none`; confirm the next decision has `reason=roles_none`, and leave both support flags enabled while existing leases drain.
Disable runner support only after drain and recreate through its owning deployment phase; release polling is not a deadline, and release/reuse await caller observation after this task stops.
