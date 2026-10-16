# CARD-0987 S1b D-1 unbound Mutation candidate

Original Code and landing owner: 6e11ca43-8795-4cd6-85df-a75dd5116be8.
Ordinary repair selection: ../plans/2026-10-16-card-0987-s1b-d1-repair.md.

All PC-1..413 and their frozen-design variants remain pending, including the
S1b PC-201/202 bindings in 2026-10-16-card-0987-s1b-candidate.md. Ordinary
red-first regression evidence does not discharge a Mutation control.

Proposed PC-414 is pending in all four (pendingTemp, missingDirectory) variants:
(false, false), (false, true), (true, false), (true, true).
Bind after landing, using the exact method filter
`/*/*/HostCleanupCheckpointSweepRaceTests/vanished_index_entry_does_not_abort_remaining_candidate`.
The candidate defect removes the per-entry not-found handler from
CheckpointTempRootSweep.EnumerateIndexFiles. Expected red is an escaping
FileNotFoundException or DirectoryNotFoundException from each listing before
the surviving candidate completes. Restore the handler and require all four
cases green, including successful owned-root removal, intact unowned sentinels
and no deletion outside the owned root and its index record.

Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
This is unbound planning evidence, not a completed control or execution pack.
