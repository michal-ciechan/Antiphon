# CARD-1731 unbound Mutation candidate

Binding required after Code and publication at the actual O/L. Code's local
baseline reds, one per witness and restored before green, are not SourceLanding
mutants. No SourceLanding mutant was executed by Code. Every row remains pending
for SourceLanding Mutation, including compiling red, restore/fresh-build/green
and missing-control discovery.
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

## Evidence-path residual (task 317b5f73)

Source: scripts/lib/release-authority.ps1 `Resolve-ReleaseAuthorityEvidencePath`.
No acceptance cache. Maximum staleness is none: every call reads attributes again.
Ordinary Code ran one local red per witness and restored the file before the
green class run. That baseline red is the same fact as the opening sentence:
a restored local red, not a SourceLanding mutant. PC-5 and PC-6 stay pending.

| PC | Variant | Exact method | Expected assertion |
|---|---|---|---|
| PC-5 | Skip the ReparsePoint refusal | Evidence_path_refusals_match_the_seeded_set | outside, inside, middle-component, file and broken links, the seeded set, and the post-replacement row fail |
| PC-6 | Restore the FileInfo / Test-Path / Get-Item component walk | Evidence_path_walk_finishes_within_fifteen_seconds | 611 ordinary resolutions exceed 15 seconds |

Filter form: `/*/*/C599SnapshotTests/ExactMethod*`. Do not raise the 300 second harness budget.

## Unreadable evidence and unwitnessed Windows rows (task 39248db6)

CARD-1770. A non-not-found attribute error returns `''` from
`Resolve-ReleaseAuthorityEvidencePath`. The publication gate then emits
`REFUSED: publication gate (chunk-evidence-escape:...)` and publishes nothing.
Blank, absolute, parent-escape, and reparse refusals are unchanged. No
acceptance cache. Maximum staleness is none. The 300 second harness budget
is unchanged.

The 15 second walk ceiling still has one Linux sample (0.153 s). A Windows
sample of `C1731_EvidenceWalk` is still required. This note records that gap.
It does not change the ceiling.

Windows-only evidence shapes (drive-relative, UNC, alternate data streams,
device names, trailing dot or space, and case-folding) and native Windows
execution of the seeded rows are not witnessed by this slice. The middle
component row `evidence/mid-link/x.trx` was witnessed here as a Linux symlink.
On Windows the fixture falls back to a junction when symlink creation is
refused. That fallback was not executed here.

The unreadable-directory witness was red at `23e38ffeff` (`IOException`
Permission denied, no REFUSED line). Ordinary Code runs one restored local
mutant per witness before the green class run. Those baseline reds are not
SourceLanding credit. PC-7 stays pending.

| PC | Variant | Exact method | Expected assertion |
|---|---|---|---|
| PC-7 | Rethrow a non-not-found attribute error | C599A_ExecutionLedger | the unreadable-directory row throws, or the REFUSED line and `chunk-evidence-escape:antiphon-001` are absent, and nothing is published |

Filter form: `/*/*/ReleaseGatePublicationAuthorityTests/C599A_ExecutionLedger`. Do not raise the 300 second harness budget.
