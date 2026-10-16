# CARD-1313 S4-S5 Mutation candidate

Original Code/landing owner: `25c628b6`.
Branch: `feat/card-task-25c628b6`. Target: master. Restart: none.

This is an **unbound candidate**, not an executable SourceLanding pack or a qualification receipt.
Mutation is paused by the Code brief. Every control and variant below is **pending**, with zero
mutants executed (`executed now = 0`). The exact semantic defects and detecting filters remain
owned by the [landed verification design](../plans/2026-10-16-card-1313-change-aware-selection-plan.md#positive-controls).
Mutation must bind minimal compiling patches, restored fixture digests, expanded counts and
current landed source before execution; it owns missing-control discovery. Do not infer control
sensitivity from ordinary green or from the S4 producer-receipt red.

PC-1..PC-81 stay pending. This increment's production sites are the S4 measurement/journal and
the S5 report/estimate attachment. PC-1..PC-29 stay on the S1-S3 candidate. PC-49..PC-73,
PC-75, PC-76, PC-79 and PC-80 are S6-S7 and are not implemented here. Ordinary V-5/V-6/V-7
green does not discharge a control.

S4-S5 implementation sites available for binding:

| Controls | Production/data site |
|---|---|
| PC-30, PC-31 | `tools/Antiphon.Checkpoints/Timing/TimingMeasurements.cs`: host wall, inclusive case window, duration sum; unavailable walls stay null |
| PC-32, PC-74 | `TimingMeasurements` category join: expanded UID plus producing assembly hash, never display name |
| PC-33, PC-34, PC-78, PC-81 | `tools/Antiphon.Checkpoints/Timing/TimingRowRecorder.cs` and `Execution/RowRunner.cs`: one producer, completion after owned-child join, build-failure completion, start flushed before the first driver |
| PC-35, PC-36, PC-37 | `tools/Antiphon.Checkpoints/Timing/TimingJournal.cs`: `RequireComponent`, hosted-root refusal, `DisposableOutputs` |
| PC-38, PC-42, PC-43, PC-44 | `TimingJournal` flush/spool plus `RowRunner` timing-fault swallow: ack after flush, spool retained on append failure, unfinished start, failed TRX stays failed |
| PC-39, PC-40, PC-41, PC-45, PC-77 | `TimingJournal` replay, event-id conflict, sequence resume, torn-tail versus corrupt middle record, in-process shard semaphore |
| PC-46, PC-47 | `scripts/lib/test-timings.ps1`: `Get-TestTimingReport` comparability key and incurred attempt cost |
| PC-48 | `Add-TestTimingEstimates`, `scripts/select-test-rosters.ps1` `-TimingRoots`, `Get-TestSelectionDigest`: estimates never recompute the digest or drop a selected set |

Ordinary test targets use the plan's exact method names. Each S4-S5 target has one outer
execution. CP-12/CP-13/CP-14 (R-2) and CP-20 (Windows replay) were not run by this Code task.
SourceLanding must use its external evidence root and restore without committing its snapshot.

| PC | Variant | Detecting method | Assertion | Status |
|---|---|---|---|---|
| PC-30 | G-30: assign caseDurationSumSeconds to caseWindowSeconds | `TestSetTimingTests.C1313_WallEnvelopes` | `wall-not-sum` | Pending |
| PC-31 | G-31: coalesce an unavailable wall measurement to zero | `TestSetTimingTests.C1313_UnavailableMeasurements` | `unknown-not-zero` | Pending |
| PC-32 | G-32: accept the fixture foreign assembly digest in the metadata join | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-build-bound` | Pending |
| PC-33 | G-33: enable both tool and wrapper row completion writers | `TestSetTimingTests.C1313_ProducerReceipts` | `one-row-producer` | Pending |
| PC-34 | G-34: append terminal completion before releasing/joining the held child | `TestSetTimingTests.C1313_ProducerReceipts` | `completion-after-join` | Pending |
| PC-35 | G-35: allow ../escape as the run component | `TimingJournalTests.C1313_RootCustody` | `path-component-refused` | Pending |
| PC-36 | G-36: accept the output-contained hosted root | `TimingJournalTests.C1313_RootCustody` | `root-outside-custody` | Pending |
| PC-37 | G-37: include the external timing shard in the producer cleanup list | `TimingJournalTests.C1313_RootCustody` | `history-survives-output` | Pending |
| PC-38 | G-38: return append success before the injected flush barrier completes | `TimingJournalTests.C1313_RecoveryHandoffs` | `flush-before-ack` | Pending |
| PC-39 | G-39: append an exact duplicate event instead of recognizing it | `TimingJournalTests.C1313_ReplayIdentity` | `replay-once` | Pending |
| PC-40 | G-40: accept the changed payload with the same event id | `TimingJournalTests.C1313_ReplayIdentity` | `event-conflict` | Pending |
| PC-41 | G-41: restart sequence at zero for an existing shard | `TimingJournalTests.C1313_ReplayIdentity` | `sequence-resumed` | Pending |
| PC-42 | G-42: remove spool after a failed append | `TimingJournalTests.C1313_RecoveryHandoffs` | `recoverable-handoff` | Pending |
| PC-43 | G-43: synthesize success for a start-only attempt | `TimingJournalTests.C1313_RecoveryHandoffs` | `unfinished-not-success` | Pending |
| PC-44 | G-44: overwrite failed test outcome with pass when the timing writer fails | `TimingJournalTests.C1313_RecoveryHandoffs` | `persistence-not-test-outcome` | Pending |
| PC-45 | G-45: ignore a malformed complete middle record | `TimingJournalTests.C1313_TornTail` | `corrupt-not-truncated` | Pending |
| PC-46 | G-46: omit membership digest from the comparability key | `TestTimingReportTests.C1313_ComparableHistory` | `comparable-series` | Pending |
| PC-47 | G-47: filter failed attempts out of total incurred cost | `TestTimingReportTests.C1313_AttemptCost` | `attempt-cost` | Pending |
| PC-48 | G-48: drop an expensive set when its p90 exceeds the budget | `TestTimingReportTests.C1313_EstimatesNeverSelect` | `budget-never-omits` | Pending |
| PC-74 | G-74: join the two colliding display names instead of expanded UID | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-uid-bound` | Pending |
| PC-77 | G-77: remove the shard writer lock while holding two append calls at the sequence barrier | `TimingJournalTests.C1313_ReplayIdentity` | `serialized-shard` | Pending |
| PC-78 | G-78: skip completion emission for the build-failed path | `TestSetTimingTests.C1313_ProducerReceipts` | `failed-attempt-recorded` | Pending |
| PC-81 | G-81: launch the child before releasing the start-event flush barrier | `TestSetTimingTests.C1313_ProducerReceipts` | `start-before-launch` | Pending |

PC-1..PC-29, PC-49..PC-73, PC-75, PC-76, PC-79 and PC-80: **Pending**. Executed now = 0.
