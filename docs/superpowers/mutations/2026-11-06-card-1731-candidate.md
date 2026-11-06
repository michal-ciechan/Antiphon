# CARD-1731 unbound Mutation candidate

Binding required after Code and publication at the actual O/L. No deliberate
mutant was executed by Code. Every row remains pending for SourceLanding Mutation,
including compiling red, restore/fresh-build/green and missing-control discovery.
Original Code/landing owner: 0915902c; restart: none.

Project: tests/Antiphon.Tests. Source: scripts/test-release-gate.ps1.
Exact class: C599SnapshotTests (Integration, Slow, ProcessSpawnLimit).

| PC | Variant in actual helper | Exact method | Expected assertion |
|---|---|---|---|
| PC-1 | Reinsert `Restore-C599ASnapshot -Snap $Snap` before the variant try | Restore_runs_once_and_only_writes_changed_bytes | one mandatory restore fails with restores=2 |
| PC-2 | Remove unchanged-byte skip and always write saved bytes | Restore_runs_once_and_only_writes_changed_bytes | fixed old timestamp no longer survives |
| PC-3 | Remove finally restoration | Finally_restores_after_a_throwing_mutation | every byte/absence and subsequent gate validity fail |
| PC-4 | Replace byte comparison by a length/stat comparison | Restore_runs_once_and_only_writes_changed_bytes | same-length/same-timestamp changed bytes remain wrong |

Use `/*/*/C599SnapshotTests/ExactMethod` per cycle, never the class selector.
Resolve literal preimages, exact substitutions and payload digests at L; this
prose is not a mechanically eligible pack. Retain per-PC expected assertion
failures; compilation, zero tests, fixture failures and timeouts are not red.
