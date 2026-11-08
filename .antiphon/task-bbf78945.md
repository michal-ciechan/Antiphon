# CARD-1769 Review round 2 (Final, diff-only) — task bbf78945

Verdict: CLEAN. B-1 and B-2 are closed; no blocker, no regression. One new advisory (A-7). Advisories A-1..A-6 from round 1 stay advisories. next: land.

Subject: origin/feat/card-task-810f46fe tip 95987c52042b5092e06b7b2aff4ebe461905339b (code ce17e53dec7dd8f03b30a8074f053e07b62b5185; 95987c52 is the report-only commit). Reviewed range 956736c959ca969ccee04c56c4fbe6c4c168e6cf..95987c52042b5092e06b7b2aff4ebe461905339b: two files, +298/-45 code, plus `.antiphon/task-810f46fe.md`. Red commit 12f433bd93ce8f6a694ab612144ed128fe5b1bca; its product hunk only adds the `RescanReconciledBoundary` constant and the per-row hook (the old end-of-pass stamp read stayed), so the red was an assertion red, not a compile red. Original Code landing owner 9edde7f9-70f3-4d5a-9964-d1c7505c57a4 (repair task 810f46fe).

Publication checks: `scripts/check-evidence-diff.ps1 -BaseRef 956736c95 -HeadRef 95987c52` exit 0 (commits=3 entries=1 violations=0; the one entry is the Code report). `git merge-tree --write-tree origin/master HEAD` exit 0, tree a63d4c80ed8f79266b611d5fe16cb821ca9b5115, origin/master 6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa. Not merged.

## Checkpoints

No plan artifact exists for this card (card-direct Code). Review reran the brief-named classes and the guard batch through the checkpoint tool from the ignored plan extract `.antiphon/c1769-r2-review-plan.md`, run `20261108-040819-20d6`, one cold build `bin-c1769r2/` (381 s, host load 38 at start), `--row-timeout 45m`. Verdict GREEN exit 0; `validate --evidence report.json --expected-source-sha 95987c52…` printed `CHECKPOINT SOURCE VALID source=95987c52042b5092e06b7b2aff4ebe461905339b rows=3`. Unlisted: none.

```
source: 95987c52042b5092e06b7b2aff4ebe461905339b state=clean buildSource=verified
CHECKPOINT CP-1 commit=95987c52042b5092e06b7b2aff4ebe461905339b build=ok filter=/*/*/(AgentTaskLandNotificationRescanTests*)|(AgentTaskLandNotificationReconcileLoadTests*)|(AgentTaskLandNotificationRecoveryTests*)/* executed=54 passed=54 failed=0 skipped=0 trx=/work/worktrees/task-bbf78945/.antiphon/checkpoints/20261108-040819-20d6/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=95987c52042b5092e06b7b2aff4ebe461905339b sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=95987c52042b5092e06b7b2aff4ebe461905339b build=reused filter=/*/*/AgentTaskLandNotificationPersistenceTests/(C467_V0*)|(C488_*)|(C498_*)|(C508_*)|(Legacy_*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-bbf78945/.antiphon/checkpoints/20261108-040819-20d6/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=95987c52042b5092e06b7b2aff4ebe461905339b sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=95987c52042b5092e06b7b2aff4ebe461905339b build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=/work/worktrees/task-bbf78945/.antiphon/checkpoints/20261108-040819-20d6/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=95987c52042b5092e06b7b2aff4ebe461905339b sourceState=clean buildSource=verified
BUDGET wall=732.483s execution=702.499s admissionOnly=29.984s
verdict: GREEN exit=0
```

| Row | Code's run | executed | passed | failed | skipped | tests wall |
|---|---|---:|---:|---:|---:|---:|
| CP-1 touched-scan (Rescan, ReconcileLoad, Recovery) | touched classes, 65 passed | 54 | 54 | 0 | 0 | 184 s |
| CP-2 touched-persistence (class minus C467_V17) | touched classes | 11 | 11 | 0 | 0 | 26 s |
| CP-4 guards | 75 passed | 75 | 75 | 0 | 0 | 86 s |

CP-1 + CP-2 = 65 = the Code report's passed count for the touched classes; CP-4 = 75 = its guard count. The rescan test ran 8.2 s and printed `CARD-1769 measure beforeCommands=6093 beforeUpdates=1508 afterCommands=15 afterUpdates=0`, reproducing the Code measurement exactly (quiet pass: one extra statement over 956736c95's 14, zero updates).

`C467_V17_UpgradeAndLegacyEvidence` was excluded from CP-2 by filter and not executed this round: it is the brief-named inherited red (round 1 reproduced it at 956736c95 as CP-3 with `HostLoadRetentionCatchup.Down`), the Persistence test and the migration are byte-identical between 956736c95 and this tip, and the fix is the test-side master commit e6132c64d0 (`test: migrate legacy land evidence forward`), after this branch's base 74a8004f4. Excluding it is what let the run produce a SHA-validated clean receipt.

## Brief items

### (1) B-1 closed: stamp read order

`ScanLandNotificationsOnceAsync` (hosted service lines 125-189): `wake` (session stamps and queue stamps for every parked entry) is read once at pass start and decides `Unchanged` for already-parked notes. For each page, the ids that are not `Unchanged` become `pending`; `LoadPreReconcileStampsAsync(pending)` reads the note identities (ParentSessionId, QueueMessageId), the session stamps and the queue stamps for those notes before any reconcile on that page. `RememberPreReconcile` parks with that pre-read stamp only. Nothing is read after a reconcile for the park; the only post-reconcile reads are `tracked` (state, parent, queue id from the row context) and `parkedAt` (the backoff clock).

Reconcile changing the row: if the reconcile changes the note's parent or queue id (null queue id enqueued during the pass, or a repaired link), the identity check at lines 233-236 fails and the note is left unparked, so the next pass reconciles it once more and then parks it on a fresh pre-read. If the reconcile changes a stamped field (CatchUp writing UserPrompt rows raises the prompt sequence), the entry keeps the pre-write value, the next pass's `wake` differs, the note reconciles once more, then parks. Both are one extra reconcile in the safe direction, bounded. A change that lands between the pre-read and the reconcile is seen by the reconcile and also re-triggers once next pass. A change that lands after the pre-read and after the reconcile (the B-1 window) differs from the remembered stamp and is reconciled on the next pass.

Witness: the mid-pass boundary (`MidPassBoundary.ReachedAsync` on `land-rescan-reconciled` for the mid-pass note) stops the session after that note's reconcile inside the pass; the follow-up pass must change its token and must not touch the samples. Red at 12f433bd93 by construction (stamp still read after the pass), reported red by Code with the exact assertion text; green here.

### (2) B-2 closed: queue-row wake

`QueueRescanStamp` carries Status, DeliveryAttempts, DeliveryVerdict, CanceledAt; `Unchanged` compares all four (CanceledAt through `SessionGeneration.Equal`, null-aware). Delivery writers (`SessionMessageQueueService` lines 1151-1153 and 1586-1588 set Sent and bump attempts; the cancel paths set Canceled + CanceledAt; verdict writers set DeliveryVerdict) all touch a stamped field. `LoadQueueStampsAsync` returns an empty dictionary on an empty id list (no statement); `messageIds.Contains(message.Id)` is one Npgsql `= ANY(@ids)` statement with one array parameter, no parameter-count limit; the measure confirms one statement for ~1,508 parked ids (14 to 15 commands). `LoadParkedStampsAsync` skips the database entirely when nothing is parked.

QueueMessageId null: `RememberPreReconcile` parks on the session stamp alone and `Unchanged` returns true on a session match (line 403-404). In practice an AwaitingReceipt note always has a queue id (reconcile throws at line 178-179 otherwise); a note that gains one during reconcile fails the identity check and is parked on the next pass.

Witnesses: delivery (Pending to Sent, attempts 0 to 1, baseline 0) wakes only the delivery note and leaves it AwaitingReceipt; cancel (Canceled + CanceledAt) wakes only the cancel note and the reconcile marks it Canceled. Both red at 12f433bd93 (no queue field in the stamp), matching the Code report's three failure strings; green here.

### (3) Starvation and leaks

No new unbounded structure: `pre` is per page and transient; `wake` is two session reads plus one queue read per pass over the parked set (the A-3 entry leak is unchanged and stays an advisory); the per-pass `boundaryScope` is disposed with the pass and resolves only the singleton no-op `LandDeliveryBoundary` in production (Program.cs:509). The remaining wake gap (a receipt that exists only on the runner, with no stamped field changed) is acknowledged in the Code report and in the `RescanPark` XML doc as bounded by the 1-5 minute backoff.

### (4) Witnesses behavioural and red-first

Yes: every new assertion observes ConcurrencyToken movement, note State, and the sample notes' tokens through the real hosted pass over the real schema. The A-2 real-clock exposure grew by five passes (delivery, cancel, two mid-pass passes plus token reads inside the samples' first 60 s park); the whole test took 8.2 s here, well inside the window; A-2 stays an advisory.

### (5) No regression

CP-1, CP-2, CP-4 green; `Backoff`, `Expire`, `Forget`, the page query (Canceled excluded), and the compiled lookups are unchanged by this diff; merge-tree against master clean.

## New advisory (card, not a land hold)

- A-7 A note whose queue row is missing is never parked. `RememberPreReconcile` forgets a note whose queue id has no pre-read stamp (lines 245-249). Reconcile handles a missing row by writing `keyed_queue_row_missing` and leaving the note AwaitingReceipt (service lines 186-193), so such a note is reconciled every 5 s pass with one UPDATE each, where 956736c95 parked it on the session stamp alone. Retention never deletes a queue row under an unconfirmed note (DataRetentionService lines 436-437); the reachable path is the phone-home input-unavailable removal (SessionMessageQueueService line 1193). Cost is bounded to the pre-card per-note cost and the state is already an error state. Fix: park on the session stamp with a missing-queue marker (compare "still missing" as unchanged).

## Housekeeping

The green run deleted `bin-c1769r2/` and its tool copy. Evidence: `/work/worktrees/task-bbf78945/.antiphon/checkpoints/20261108-040819-20d6/report.md`. The plan extract `.antiphon/c1769-r2-review-plan.md` is ignored and uncommitted. The only commit on this branch is this document.
