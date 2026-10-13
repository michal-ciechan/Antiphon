# CARD-1205 S3 — unbound Mutation candidate

Status: pending; SourceLanding Mutation has not run. Original Code and landing
owner: 3e736c72-542c-472a-b6c9-1dcd367c7a36.

This candidate adds the S3 bindings to the inherited card inventory. PC-11..40,
including each cache mode and uncertainty/concurrency/classification variant,
remain pending. No ordinary green result discharges a PC. S4/S5 remain outside
this Code slice. Caller commissions Review, lands the original Code owner, then
commissions SourceLanding Mutation against that landed snapshot.

Payloads and expected reds are in the frozen
[test design](../plans/2026-10-12-card-1205-test-design.md#positive-controls).
Mutation owns compiling payload adaptation and missing-control discovery.

| Pending PCs | Method-scoped selection | Variants |
|---|---|---|
| PC-11, PC-38 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Complete_idle_snapshot_recovers_once` | cache enabled/disabled; missing persist and disabled-cache veto |
| PC-12..25 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Uncertain_probe_does_not_certify_idle` | all 15 frozen T11 variants; NeedsReload and retained-failure are independent vetoes |
| PC-26 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Newer_runner_activity_keeps_working` | cache enabled/disabled |
| PC-27..31 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Concurrent_commit_invalidates_snapshot` | new prompt, queue annotation, generation, remote owner, terminal, replacement x both cache modes |
| PC-32..36 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | UUID/kind, low source sequence, replay, stored rebase, producer time, null UUID replay |
| PC-37 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Probe_holds_the_commit_gate_through_publication` | both cache modes; warmed gate acquisition and published state |
| PC-39, PC-40 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Runner_tail_classification_is_conservative` | all 8 literal expectation rows; future activity and server-only annotations |

PC-29/30 payloads must remove the relevant final LocalRunning predicate and
its matching routing/status comparison together, leaving generation/frontier
checks intact. PC-31 substitutes max-sequence/count for the complete stamp
comparison (revision/reset plus row identities); removing only row equality is
masked by the mutation/reseed revision. These are compiling-payload bindings,
not additional production exceptions.

Additional missing-control discovery remains owned by Mutation, including retained
persistence failure when the authoritative tail is Working.

No timeout or assertion relaxation. Method-scoped red/restore/green cycles only.
Restart: none.

## F1 repair additions — all pending

Repair/landing owner: 080e0111-220a-4633-bc3e-efa40d4016ff. Original S3 author
remains 3e736c72-542c-472a-b6c9-1dcd367c7a36. Add these to inherited PC-1..190
and all PC-11..40 variants; ordinary test-first evidence is not a PC cycle.

| Pending PC | Method-scoped filter | Payload / required red |
|---|---|---|
| PC-191 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changed_after_validation_must_not_append_old_end` | Bypass checked SQL fence; generation wins at identity barrier -> stale end count 1 instead of 0. |
| PC-192 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Late_precondition_change_rejects_without_publication` | Independent variants: remove fenced generation, owner/local-routing, terminal-status, full-row-frontier predicate; corresponding late change -> zero stale end fails. Also bypass rejection publication/log-once separately -> unchanged snapshot/one Warning fails. |
| PC-193 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Generation_changes_before_validation_are_rejected` | Remove both final and SQL generation predicates (keep snapshot equality) -> end/result assertions fail. |
| PC-194 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Unchanged_generation_publishes_only_committed_rows_once` | Both cache modes; publish proposed rows/release state gate before SQL save -> warmed reader exclusion fails; omit save or dedupe -> end/exactly-once witness fails. |
| PC-195 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Concurrent_recoveries_commit_one_end_and_converge` | Both cache modes; bypass final revision/full-row comparison and SQL row comparison -> second stale recovery falsely succeeds; recovered/Unknown counts fail. |
| PC-196 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Recovery_commit_fences_lifecycle_writer_then_allows_new_generation` | Both cache modes; release SQL row fence before held save -> writer NOWAIT lock unexpectedly succeeds (expected 55P03); remove later generation mismatch -> old snapshot Unknown assertion fails. |

Mutation owns compiling payload adaptation, red/restore/green and missing-control
discovery, including fallback fence and postcommit-order coverage. No PC executed.
Restart: none.
