# CARD-1773 Review round 1 (Final): land park follow-ups

Verdict: NOT CLEAN. One blocker (B-1, the index migration cannot be retried after an interruption). Two advisories (A-1, A-2). No test regressions: 4 rows, 219/219 green at the reviewed SHA. next: code (repair on the same Code owner).

Subject: origin/feat/card-task-d53f3fba tip 4f20588c8c7d43cb8852d398098f2aea7ba29892 (Grok Code author, three commits on base f54741ef3bc08baa0493f9e0bebd2425acab6fb5). Red commit 5dbad1cdc6f018f68e01e2b8ef05984adb773ed5, fix 26d5d4c0d46cc9837269992002fa16e6364c8d1a, test fix 4f20588c8c. The branch has no plan artifact and no committed Code report (card-direct Code).

Publication checks: `scripts/check-evidence-diff.ps1 -BaseRef f54741ef3b -HeadRef 4f20588c8c` printed commits=3 entries=0 violations=0. `git merge-tree --write-tree origin/master HEAD` (master b35fb137acde6e0c52e296dd6d7b4e345fe3d238) exited 0 with tree 0737e4c94e29a9597144699e07d5357ce77f55e3. Nothing was merged.

## Checkpoints

There is no `### Checkpoints` table. Review ran the brief-named scope through the checkpoint tool from the ignored extract `.antiphon/c1773-review-plan.md`. Run `20261108-134200-b28d` used one cold build `bin-c1773rv/` and finished GREEN exit 0 in 17m53s wall. `validate --evidence report.json --expected-source-sha 4f20588c…` printed `CHECKPOINT SOURCE VALID source=4f20588c8c7d43cb8852d398098f2aea7ba29892 rows=4`. No unlisted run: the only other build was the checkpoint tool itself (`tools/Antiphon.Checkpoints`, build-slot lease, 8 s).

| Row | Filter | Result |
|---|---|---|
| CP-1 | `(AgentTaskLandNotification*)\|(AgentTaskLandHeldNotificationTests*)` | 84/84. Classes: ParkFollowUp 6, Rescan 1, Persistence 12, ReconcileLoad 4, Recovery 49, Held 12 |
| CP-2 | `C0824RefOnlySnapshotMigration\|C1514HostLoadMigration\|ExpectationDebt\|C0824RefOnlyIdentity\|C0824RefOnlyDebt` | 28/28 (4+8+6+7+3) |
| CP-3 | `DispatchBaseNotificationTests` (sibling of the hosted service) | 57/57 |
| CP-4 | guard batch: TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitnessTests | 50/50 |

The author did not run C0824RefOnlyIdentityTests or AgentTaskLandHeldNotificationTests. Both are green here (7 and 12 tests).

## Blocker

### B-1 The concurrent index migration cannot be retried after an interruption

- **Where:** `server/Migrations/20261108121426_Card1773TranscriptPromptStampIndex.cs` Up. It is a plain `CreateIndex(...).Annotation("Npgsql:CreatedConcurrently", true)` on `TranscriptEntries`, which is the largest table.
- **Failure:** `Program.cs:1125` runs `Database.Migrate()` at startup with the default 30 s command timeout; nothing overrides it. A concurrent build can be interrupted by that timeout, by an AppHost restart or kill, or by a server2 rolling-phase stop. It also waits for older transactions, for example those held by the old API still serving during a rollout. When it is interrupted, PostgreSQL leaves an INVALID index named `IX_TranscriptEntries_AgentSessionId_Kind_Sequence`, and the migration-history row is never written. Every later startup re-runs `CREATE INDEX CONCURRENTLY "IX_…"` and fails with 42P07 (relation already exists), so the server cannot start until someone drops the index by hand.
- **Why it blocks:** the repo already fixed this exact defect for TranscriptEntries concurrent indexes. CARD-0698 commit 50275689d8 ("recover interrupted concurrent index migrations") added `20260925081051_AddTranscriptHotPathIndexes.CreateRetryableIndex`, its plan requires the retry path (plan lines 97-103), and `TranscriptHotPathMigrationTests.Retry_*` pins it. This migration regresses that contract. Down (`DROP INDEX CONCURRENTLY IF EXISTS`, transaction suppressed) is safe.
- **Fix:** reuse the CARD-0698 pattern for this name. Rename the index if it is INVALID, drop the `_invalid` copy with a separate `DROP INDEX CONCURRENTLY IF EXISTS`, then `CREATE INDEX CONCURRENTLY IF NOT EXISTS`, each with `suppressTransaction: true`. Keep the model snapshot and Designer. Add an interrupted-retry witness modelled on `TranscriptHotPathMigrationTests.Retry_replaces_all_three_interrupted_invalid_indexes`. Then rerun CP-1 and CP-2.

Migration facts: the change is additive (one index). Its id sorts after master's latest, `20261107104114_HostLoadRetentionCatchup`, and master has no newer migration. The snapshot hunk is additive, and `HasPendingModelChanges` is false (C0824RefOnlySnapshotMigrationTests and C1514HostLoadMigrationTests are green). `MigrationTestDatabase.DetachHostLoad` also drops the new index and history row, which the Identity and Held callers exercise.

## Brief items

1. **Park-map pruning:** correct. `Retain(seen)` runs only after a pass that reached the end without cancellation. A page-read exception propagates before it. Per-row exceptions are caught, and their ids are already in `seen`. Every parked entry carries a queue id, so its note stays on the due page (`QueueMessageId != null`) while it is AwaitingReceipt. That means a live parked note is never dropped. The `C1773_ExternalConfirmDropsTheParkEntry` control proves this.
2. **Injected clock:** done. `AgentTaskLandNotificationRescanTests` and the new class freeze a `FakeTimeProvider` on `HarnessOptions.TimeProvider`, which the hosted pass reads from scope. `C1773_BackoffDoublesAndCapsAtFiveMinutes` checks quiet and wake at 59/61 s, 119/121 s, 239/241 s and 299/301 s. Without the cap, the last window (8 min) would stay quiet at 301 s and the test would fail, so it witnesses both the doubling and the cap.
3. **Canceled terminal:** the watchdog predicate is correct. `ExpectationSnapshotReader` excludes Canceled in both outbox predicates and nothing else. Blocked and AwaitingReceipt are unchanged (ExpectationDebtTests and C0824RefOnlyDebtTests are green). A Canceled note is never on the page and is never parked. See A-1 for one narrow behaviour change.
4. **Pull dedupe:** correct. The session is added to the set before the await, so a failed pull still bounds the pass. Later notes read the store, or `ObserveReceiptStateAsync` for the scan-cache path, and the certificate still requires `before == current`. Direct reconcilers pass null and pull as before. A prompt that lands on the runner after the first pull waits for the next pull or the park backoff, which is the documented runner-only bound.
5. **Park after 3 misses:** correct. Misses 1 and 2 leave the entry due (`ParkedUntil = MinValue`). The third miss parks for `InitialBackoff`. While the row is still missing the entry stays parked, and the row coming back wakes it. In steady state that is one reconcile per minute, with no doubling. `C1773_MissingQueueParksAfterThreeMisses` covers both directions.
6. **Kind index and the runtime-model failure:** the failure came from the new test itself, not from product code. `IsCreatedConcurrently()` is null on the runtime model, and 4f20588c8c moved the assertion to `IDesignTimeModel`. It is green here in the full class run, not only when the method runs alone.
7. **Product behaviour otherwise unchanged:** yes, apart from A-1.

## Advisories (for cards; they do not hold the land)

- **A-1 Canceled no longer confirms in the same pass.** On master, the reconcile that first sees a Canceled queue row marked the note Canceled, then still ran the receipt scan. If a matching UserPrompt was already in the transcript, it confirmed the note. The branch returns before that scan (service lines 220-226). The path is narrow: a human Drop (`CancelAsync`, any Pending row) of a land-note row with `DeliveryAttempts > 0` whose prompt actually landed. The note then stays `queue_canceled_unconfirmed`, `AgentTaskLandMonitorService` ages it to Warning or Error, and a TaskCompletion handoff receipt is not bound (`PipelineHandoffActionService:240`). `session-runtime-invariants.md:1655` ("Canceled … rows remain unconfirmed") supports terminal Canceled, so this is not a blocker. Suggested change: short-circuit only when `DeliveryAttempts == 0`, otherwise scan once. `C1773_CanceledNoteDoesNotLateConfirm` pins the current behaviour.
- **A-2 The index covers every transcript row.** The stamp read only needs the UserPrompt and QueuedUserPrompt kinds. A partial `(AgentSessionId, Sequence) WHERE "Kind" IN ('UserPrompt','QueuedUserPrompt')` would cost far less to build and maintain on every transcript insert. A-6 also asked for a measurement on real transcripts first, and none was taken.

## Housekeeping

The green run deleted `bin-c1773rv/`. Evidence: `/work/worktrees/task-3e56dbb0/.antiphon/checkpoints/20261108-134200-b28d/report.md`. The plan extract and the manifest `.antiphon/c1773-rv-manifest.yaml` are ignored and uncommitted. The tool build output `tools/Antiphon.Checkpoints/bin/` is the native default output. The only commit on this branch is this document.
