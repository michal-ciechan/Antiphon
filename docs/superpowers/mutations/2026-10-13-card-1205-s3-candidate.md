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
