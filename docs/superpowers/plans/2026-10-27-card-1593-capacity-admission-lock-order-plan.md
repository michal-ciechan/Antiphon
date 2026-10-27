# CARD-1593 capacity admission: lock order and lifecycle (redo design)

Task a9ca2252, Plan stage. Base `origin/rc/2026-10-27` = `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.
The change ships to rc/2026-10-27 first, then master. All line numbers below are at that base.

Parked attempts read: `feat/card-task-1794d6c7` (`053aa52b`, round 1) and
`feat/card-task-b095c160` (`730c76a2`, round 2). Their sign-in lock fix (`d2d7acc82`) is
extracted elsewhere and is out of scope here.

## Problem

Three defects share one root: capacity admission is spent at a different moment, and under
a different lock order, from the launch it pays for.

1. **C508 (native).** The dispatch tick redeems the grant at
   `server/Application/Services/AgentTaskDispatcher.cs:959-961` (`TryRedeemCapacityWaitAsync` ->
   `CapacityRecoveryService.RedeemAsync` at `CapacityRecoveryService.cs:237`, its own scope and
   commit). Only then does it claim at `:977` (`DispatchOneAsync`). Every claim exit that rolls
   back, or returns without launching, leaves the wait `Admitted` with `AdmissionCount+1` and the
   provider grant cleared. Those exits are `SiblingObservationStale` `:5588`, `NotClaimed`
   `:5549/:5599/:5975/:5994/:6059/:6095`, `HeldOnLease` `:5558`, `HeldForAgent` `:5700/:5728`,
   `HeldOnParallel` `:8024`, and `HeldForRemotePrep` `:5987`. The task stays `Queued` with no
   grant, so the next tick's redeem returns `grant-mismatch`. Only `RearmStalledAdmissionsAsync`
   (`CapacityRecoveryService.cs:1159`, after 2 admission intervals) recovers it, and each loss
   also spends one of the bounded attempts.
2. **Lock-order inversions (Round 1 F1, Round 2 F1).** Round 1 moved redemption inside the claim
   (task->provider) but reroute is provider->task. Round 2 flipped the claim to provider->task, but
   the released-seat continuation is task->provider. Neither attempt fixed one order for every
   path.
3. **Receipt and rollback (Round 2 F2, F4).** The dispatch receipt (`Admitted -> StartAccepted`,
   `LaunchSessionId`) is a separate post-commit step (`AgentTaskDispatcher.cs:1050-1070` ->
   `CapacityRecoveryService.cs:396`). A crash between the launch commit and that step leaves an
   unbound `Admitted` wait for a running task. Round 2's late-consume rollback called
   `ChangeTracker.Clear()`. That detached the tick's other queued tasks, so the next task
   dispatched with one admission and no receipt, because `task.AgentSessionId` on the orphaned
   instance was stale at `:1052`.

## Ground truth (card assumption vs code at base)

| Card/review assumption | What the code at `f84228f28` does |
|---|---|
| "Admission consumed before stale-claim rollback" | Confirmed: redeem `:959` commits on its own scope before the claim transaction `:5532`; stale rollback `:5587` cannot undo it. |
| "Admission consumed during remote preparation" | Confirmed: the same pre-claim redeem; `HeldForRemotePrep` commit `:5983` keeps the task `Queued`. |
| Reroute takes provider->task | Confirmed, implicitly. `TryRewalkQueuedChainAsync` sets `task.AgentKind` at `:1682` and then calls `SupersedeTaskWaitsOnAsync` `:1685`. With no ambient tx, that method opens its own (`CapacityRecoveryService.cs:344`), takes the provider lock `:347`, and its `SaveChangesAsync` `:372` flushes the tracked task row. The task row is written under the provider lock. |
| Claim takes task->provider | Not at base: the base claim takes no provider lock. Round 1 introduced it. Under this design it becomes task->provider deliberately (D-1). |
| Continuation takes task->provider | Confirmed: `ContinueReleasedSeatAnswerAsync` `AgentTaskService.cs:3764-3766` locks task, release and session, then `RequeueCoreAsync` -> `SupersedeTaskWaitsOnAsync` `:3845` inside that transaction (ambient) -> provider. |
| `ResumeRoutingBlockedAsync` is safe | No. Each iteration's `SupersedeTaskWaitsOnAsync` `:1826` `SaveChangesAsync` flushes the previous iterations' tracked task edits (`Status=Queued` etc., `:1842-1846`, saved only at the end) under a provider lock: provider->task(other). |
| Supervisor redemption is ordered | Rank-consistent (agent->provider), but across connections. `SuperviseAsync` opens a tx `AgentSupervisorService.cs:127`, and Herdr `ObserveAsync` row-locks the agent (`HerdrSupervisionStateService.cs:26`). `TryHandleCapacityWaitAsync` `:230` then calls `RedeemAsync` `:507` on another scope and commits the admission before the outer tx commits `:312`. PostgreSQL cannot detect a cycle that runs through the app. Same consume-before-commit class. |
| Queue redemption | `SessionMessageQueueService.cs:2578` runs with no DB transaction open (in-process session gate only); it is a provider-only leaf. Consume happens before the flush; deferred (S3). |
| Counted-slot advisory is a lock | `TryClaimCountedSlotAsync` (`CapacityRecoveryService.cs:690`, called at `AgentTaskDispatcher.cs:636`) runs with no explicit transaction, so the xact lock is released at statement end. Recorded; deferred (S3). |
| Hangfire jobs touch capacity | No. Recurring jobs (`Program.cs:1141-1156`: census, worktree residue, expectation watchdog, runner slot reconcile, runner retire, keep-six) do not call `CapacityRecoveryService`. Capacity reconciliation runs from `AgentSupervisorHostedService.cs:146` (`ReconcileAsync`), whose steps are provider-only leaves. |
| `EnsureWaitOnAsync` takes the provider lock | No. `EnsureWaitCoreAsync` (`CapacityRecoveryService.cs:521`) relies on the unique consumer index, not the provider lock. |
| Checkpoint tool can seal the test DLL | No at base: `scripts/lib/checkpoint-file-reader.ps1:196` caps at 67,108,864 bytes and `Antiphon.Tests.dll` is about 69 MB. As the operator directed, Code runs the table through `scripts/build-slot.ps1` (one build, then `--no-build`). |

## Decisions

**D-1 Canonical global lock order (task before provider).** Every transaction acquires locks
in this rank order and never acquires a lower rank after a higher one:

| Rank | Resource | Key |
|---|---|---|
| 1 | counted-task lane advisory | `hashtext('antiphon.capacity.counted-tasks')` |
| 2 | standing-specialist owner `Agents` row (`FOR UPDATE` / `FOR NO KEY UPDATE`) | owner id |
| 3 | `AgentTasks` row (`FOR UPDATE`, or any EF/SQL write of the row) | task id |
| 4 | `RunnerSeatReleases`, `AgentSessions`, `SessionQueuedMessages` rows | row id |
| 5 | parallel-tasks advisory | `hashtext('antiphon.delegation.parallel-tasks')` |
| 6 | capacity provider advisory, ascending `AgentKind` when several | `hashtext('antiphon.capacity.kind.<Kind>')` |
| 7 | capacity rows (`CapacityRecoveryWaits`, `CapacityRecoveryProviderStates`, `CapacityRecoveryWaitHolds`) | row id |

Rule P (provider is a leaf): while a rank-6 lock is held, the transaction may not lock or
write any `AgentTasks`/`Agents` row that it did not already lock earlier in the same
transaction. That covers EF `SaveChangesAsync` flushes of tracked rows, the trap behind the
reroute inversion. Rule X (no cross-connection waits): a path that holds a rank 1-5 lock in its
own transaction may not wait for a rank-6 lock on a different connection.

*Rejected:* provider-first, Round 2's order. It needs the wait kinds before the task lock,
then a speculative pre-lock and a rollback when the kind changes. It would also have to change
every `AgentTaskService` path that already locks the task first: continuation, requeue, retry,
cancel. Round 2 F1 shows how easy one of those is to miss. Task-first keeps the provider a leaf
and changes only the paths listed in S1.

**D-2 Admission lifecycle: reserve, consume, release.**
- *Reserve:* `GrantReadyAsync` sets `ProviderState.GrantedWaitId = W` (wait `ActionPending`). It is
  durable and reusable: any number of ticks, preparation commits and restarts observe the same
  grant, and `StillHoldsGrant`/`GrantExpired` is its only bound.
- *Consume:* `RedeemOnAsync(db, ..., consume: true)` runs only inside a claim transaction,
  immediately before a `Status = Dispatched` assignment: cold launch `:6100`, warm reuse `:8247`,
  standing placement `:8502`. It runs after `RefuseParallelAsync` returned false, so ranks
  3 -> 5 -> 6. Under the provider lock it checks the grant, increments `AdmissionCount`, clears
  the grant, and **writes the receipt in the same commit**: `State = StartAccepted`,
  `SessionId = LaunchSessionId = <launched session>`, `DispatchAttemptId`. The task's
  `Dispatched`, its session row and the receipt commit atomically or not at all.
- *Release:* a claim exit that commits without launching releases the reservation in that same
  commit. The exits are FailAsync, optional-work expiry, interim/reuse-gate block, Grok/Claude
  probe failure, repair-source fail, worktree-base block and progress-capture fail (Round 2's
  `CommitClaimWithoutLaunchAsync` list). Release cancels the wait with `claim-not-launched` and
  clears the grant if it is held; `AdmissionCount` is unchanged. A claim exit that rolls back
  touches nothing, so the reservation survives.
- *Pre-claim gate* (`:957-971`): read-only for consumption (`consume: false`). It still commits
  durable refusals (exhaustion, clock-not-due defer, disabled) on its own scope, before any task
  lock (a rank-6/7 leaf).

*Rejected:* redeem-then-compensate (Round 1: refund on rollback). Every exit needs its own
compensation, and a crash between the redeem and the compensation still leaks. *Rejected:* a
post-commit receipt. It leaves a crash window, and after a `Clear()` it reads a stale task
instance (F4).

**D-3 Exactly-once proof.** I1: `AdmissionCount` changes only inside `RedeemOnAsync(consume:true)`.
I2: that call runs only in a claim transaction whose commit also sets `Status = Dispatched`,
`AgentSessionId = S`, and the wait's `LaunchSessionId = S`. A commit therefore means exactly one
increment bound to `S`; a rollback means none. I3: `StartAccepted` is never a grant candidate
(`CapacityRecoveryPolicy.cs:28-31`). `RearmStalledAdmissionsAsync` only touches `Admitted`, so no
restart path admits the same attempt again. I4: the `HeldForRemotePrep` commit writes no capacity
row. I5: crash cuts. (c1) Before the claim commit: nothing committed. (c2) After the prep
commit: the grant is still reserved; a restart re-observes it and the launch consumes once.
(c3) After the launch commit, before enqueue: the task is `Dispatched`, the session `Starting`,
the wait `StartAccepted`/S. `AgentSessionLaunchQueue.ResumeInterrupted` (`:67`) relaunches S and
nothing re-admits. (c4) Enqueue refusal (`:6198-6211`) is c3 without the crash. (c5) Grant
expiry during preparation: the launch's consume refuses, the launch rolls back (`NotClaimed`),
and the reservation is re-granted later; only launches count.

**D-4 Rollback model: restore task identities.** Every claim-transaction rollback and every
refused late consume goes through one helper, `AbandonClaimAsync(transaction, claimed)`.
It generalizes `:6161-6168`:
1. Snapshot the tracked non-`Added` `AgentTask` CLR instances.
2. Dispose the transaction and `ChangeTracker.Clear()`.
3. Reload each snapshotted instance in place.

The tick's `queued` list stays tracked with database values, so a later task's
`AgentSessionId` and token are current. Because of D-2 the dispatch path has no post-commit
receipt step left to skip (the `:1050-1070` block is removed). *Rejected:* a bare `Clear()`
(Round 2 F4 reproduced it). *Rejected:* rereading the durable dispatch result after each
outcome. It would be redundant once the receipt is in the launch commit.

**D-5 Reroute and routing resume lock the task first.** `TryRewalkQueuedChainAsync` owns its
transaction and refuses an ambient one. Inside it: task `FOR UPDATE`, recheck `Queued` and the
token (lost -> `RewalkOutcome.ClaimLost`, and the tick `continue`s), mutate the kind, run
`SupersedeTaskWaitsOnAsync` (ambient; provider kinds ascending), save, commit.
`ResumeRoutingBlockedAsync` uses one transaction per task in the same shape and saves per task,
so no cross-iteration flush happens under a provider lock. The released-seat continuation,
requeue, retry and cancel already conform to D-1 once Rule P is checked (S1 asserts it in the
lock-order tests). Cancel's `SupersedeTaskWaitsOnAsync` (`AgentTaskService.cs:2949`) gets a task
lock first in S2.

**D-6 Legacy leaked admissions.** Rows already leaked (`Admitted`, no receipt, task `Queued`)
recover through the existing `RearmStalledAdmissionsAsync`. No data migration ships in rc.

**D-7 Native C508 E2E expectations** (`DispatchBaseWarningDeliveryE2ETests.C508_*`) are obsolete
fixtures; the card decision leaves them for their own repair. No native Windows row in this plan.

**D-8 Next-stage conflict.** The operator direction says "next: review" and the task text
says "next: code after a design review". This plan carries its own `## Verification design`
and `### Checkpoints`, so it hands off to a design Review. Code follows the Review.

## Global lock-acquisition table (every site; rank sequence per transaction)

"Leaf" means the transaction holds ranks 6-7 only. "n/c" means no capacity lock is ever held,
so only the rank 1-4 order matters.

| # | Path | Site(s) | Base sequence | Conforms? | Slice |
|---|---|---|---|---|---|
| L1 | Dispatch claim (cold/warm/standing) | `AgentTaskDispatcher.cs:5532` tx; `:5535` counted (1); `:5539` owner agent (2); `:5543` task (3); `:7978` previous token owner task (3, warm reuse); `:7934` parallel (5) | 1->2->3->(3)->5 | yes; S1 adds 6 at consume | S1 |
| L2 | Pre-claim redeem (gate) | `:959` -> `CapacityRecoveryService.cs:248` own scope | 6->7 leaf, but **consumes** | lifecycle defect | S1 |
| L3 | Queued reroute | `AgentTaskDispatcher.cs:1685` -> `CapacityRecoveryService.cs:347`, flush `:372` | 6->3 (implicit) | **no** | S1 |
| L4 | Routing-blocked resume | `AgentTaskDispatcher.cs:1826` -> `CapacityRecoveryService.cs:347` | 6->3(other) | **no** | S1 |
| L5 | Post-dispatch receipt | `AgentTaskDispatcher.cs:1054` -> `CapacityRecoveryService.cs:405` | 6->7 (+ flush of dirty tick rows) | crash window | S1 (removed) |
| L6 | Retained return | `AgentTaskDispatcher.cs:633` -> `RedeemAsync`; `:636` counted (no tx) | 6 leaf then separate save | non-atomic | S3 |
| L7 | Released-seat continuation | `AgentTaskService.cs:3764` tx; `:3635/:3638/:3641` task, release, session (3,4); `:3845` provider (6) | 3->4->6 | yes | guard (S1 test) |
| L8 | Released-seat answer accept | `AgentTaskService.cs:3576` -> `:3632` | 3->4 | n/c | - |
| L9 | Retry/escalate requeue | `RequeueAsync` `AgentTaskService.cs:3792` (in-process gate) -> `RequeueCoreAsync` `:3845` (owned tx) | 6 then task save outside | yes if no dirty task at `:3845` (Rule P) | guard (S1 test) |
| L10 | Cancel | `AgentTaskService.cs:2949` | 6 (owned), task saved after | yes (Rule P holds; no task lock) | S2 hardening |
| L11 | Released-seat recovery tick | `AgentTaskDispatcher.cs:5358` grok-rules advisory (own tx); `:5395/:5398` task, session (3,4); `:5337` -> `RetryAsync` (= L9) | 3->4 | n/c | - |
| L12 | Readiness/held trace | `AgentTaskDispatcher.cs:1366` task (3) | 3 | n/c | - |
| L13 | Brief recovery hold / absent launch | `:2839`, `:3650` task (3) | 3 | n/c | - |
| L14 | Task create | `AgentTaskService.cs:1579` card, `:1585` handoff action, `:1683` interim latch -> `InterimVerificationPolicy.cs:168` task (3); `:1897` host-budget advisory; `:4818` `EnsureWaitOnAsync` (no provider lock) | card->action->3 | n/c | - |
| L15 | API-error wall | `ApiErrorRecoveryService.cs:102` tx -> `:543` supersede (6), `:571` ensure, `:588` bump wave `CapacityRecoveryService.cs:647` (6, same kind) | 6 only (holds/recovery rows) | yes | - |
| L16 | Supervisor (standing-agent restart) | `AgentSupervisorService.cs:127` tx; `HerdrSupervisionStateService.cs:26` agent (2); `:230` -> `:507` `RedeemAsync` on another connection (6) | 2 -> (6 cross-conn), consume before commit | **Rule X** | S3 |
| L17 | Queue flush redemption | `SessionMessageQueueService.cs:2578` (no tx) | 6 leaf, consume before flush | lifecycle | S3 |
| L18 | Capacity reconcile (hosted) | `CapacityRecoveryService.cs:138` grant, `:457` orphan cancel, `:1181` re-arm (+ `SessionQueuedMessages` rows, 7), `ConsumePendingReleasesAsync` (no lock) | 6->7 leaf | yes | - |
| L19 | Land paths | `AgentTaskLandService.cs:84/:353/:393/:1603`, `AgentTaskLandingProtocol.cs:954`, `AgentTaskLandMonitorService.cs:23/:56`, `AgentTaskLandRequestWriter.cs:233`, `AbandonedLandingReconciler.cs:81` | 3 (+4) | n/c | - |
| L20 | Other task-row lockers | `BlockedTaskParkingService.cs:240`, `AgentTaskDecisionQuestionService.cs:59` (NOWAIT), `VerificationCleanupService.cs:25`, `BootStallWarningWriter.cs:77`, `RemoteWorkspaceService.cs:261`, `RemoteWorkspaceReadiness.cs:29`, `PipelineHandoffActionService.cs:34`, `SessionMessageQueueService.DispatchBrief.cs:61`, `SessionMessageQueueService.cs:1966`, `ProviderWaitWarningWriter.cs:34`, `TerminalRunnerSeatReleaseService.cs:872`, `SettlementSyncRecoveryService.cs:300`, `BlockedTaskSyncRecoveryService.cs:139`, `VerificationExecutionService.cs:73`, `TaskParkPublicationService.cs:392`, `AgentTaskReplyService.cs:5276`, `ReviewEvidenceRecoveryService.cs:140`, `CardWorktreeCleanupExecutor.cs:85`, `CardWorktreeMirrorCleanupService.cs:112`, `TaskWorktreeRetirementService.cs:88`, `WorktreeRemovalEvidence.cs:234`, `AppDbContext.cs:164` | 3 (+4) | n/c | - |
| L21 | Agent-row lockers | `AgentService.cs:950/:1016/:1245`, `AgentControlService.cs:1283/:1485`, `AgentPinnedInstructionService.cs:489`, `HerdrPlacementLock.cs:18/:21`, `HerdrSupervisionStateService.cs:26`, `StandingSpecialistHealthService.cs:28`, `StandingSpecialistSeatService.cs:54`, `StandingSpecialistRoutingService.cs:172`, `SpecialistRequestService.cs:482` (after counted `:182`), `SpecialistRequestService.Qualification.cs:43` (counted), `AgentSessionService.cs:417` (`FOR NO KEY UPDATE`) | 1->2 | n/c (only L16 adds 6) | - |
| L22 | Other advisories (disjoint key spaces, never with ranks 3/6) | host budget `HostBudgetService.cs:46`, `AgentTaskService.cs:1897`; `DelegationOpenGate.cs:140`; `SourceLandingAdmission.cs:73`; `DispatchConcurrencySettingsService.cs:728` (parallel key, settings writes); `GrokRulesRefreshService.cs:187`; channel outbound (`ChannelOutboundService.cs:77/:209/:370`, `ChannelOutboundDeliveryPump.cs:283/:328`, `ChannelOutboundFailureRecorder.cs:83`, `ChannelBridgeService.cs:502`); `PipelineDefinitionService.cs:68`; `CardService.cs:1402`; `WorkspaceReservationJournal.cs:253`; `DatabaseSeeder.cs:63`; Hangfire `KeepSixReminderJob.cs:41` | own key only | n/c | - |

## Slices

**S1 (rc slice, the smallest fix for C508 that does not introduce an inversion).**
Every row S1 touches. Without L3/L4, the S1 claim's new task->provider order recreates
Round 1 F1.
- `server/Application/Services/CapacityRecoveryService.cs`: add
  `RedeemOnAsync(db, waitId, actionKey, kind, path, ct, consume, launchSessionId)` (ambient
  transaction, provider lock, receipt written on consume) and keep `RedeemAsync` as its
  own-scope wrapper. Add `ReleaseClaimReservationOnAsync(db, taskId, kind, reason)`.
- `server/Application/Services/AgentTaskDispatcher.cs`:
  - Gate `:957-971` becomes `consume:false`.
  - Consume at `:6100`, `:8247` and `:8502` after `RefuseParallelAsync`; a refusal goes to
    `AbandonClaimAsync` and returns `NotClaimed`.
  - Committed non-launch exits release the reservation.
  - Add `AbandonClaimAsync` and route rollback exits that `Clear()` (`:5973`, `:5993`, `:6058`,
    `:6082`, catch `:6153`) through it.
  - Delete the dispatch receipt block `:1050-1070`.
  - Reroute (D-5) adds `RewalkOutcome.ClaimLost`; `ResumeRoutingBlockedAsync` uses one
    transaction per task.
- Tests: `tests/Antiphon.Tests/Application/CapacityAdmissionLifecycleTests.cs` (new),
  `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (new), and port
  `Stale_claim_preserves_capacity_grant_for_the_fresh_inspection` into
  `AgentTaskDispatchBaseGuardTests.cs`.
- Reusable from parked branches:
  - `CapacityRecoveryAcceptanceTests` rollback case and `CapacityRecoveryTaskTests` `configure`
    hook: copy.
  - `CapacityAdmissionTimingTests` four-cut body from `11251ebdd`: port into the lifecycle class
    as V-4.
  - Competing claim/reroute body from `bd0083f9c`: port as V-9.
  - `RedeemOnAsync(consume:)` and `CommitClaimWithoutLaunchAsync` call-site list from
    `8bb811e47`: reuse **without** its provider-first claim prelude.

**S2 (master, then rc port; deferred).** Cancel (L10) locks the task before supersede. Add a
debug-build `LockOrderLedger` that throws when a rank is inverted or when a tracked
`AgentTask`/`Agent` row is flushed under rank 6 without its rank-3/2 lock (Rule P).

**S3 (deferred, separate card).** Supervisor (L16): move the redemption onto its own
transaction (Rule X) and consume at the start commit. Queue flush (L17) and retained return
(L6): atomic consume plus receipt. Counted slot (L6, `:636`): hold the lock in a transaction.

## Verification design

All red witnesses are committed before the fix (git-common). "Red at base" means the test fails
on the S1 test commit, before the product commit, with the stated assertion. A build or fixture
error does not count.

| ID | Test (class/method) | Asserts | Red witness |
|---|---|---|---|
| V-1 | `AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection` | after stale rollback: `AdmissionCount==0`, grant still `W`; the next fresh inspection dispatches with exactly 1 admission and `StartAccepted` bound to the session | red at base: `AdmissionCount` 1 (seen at `4dda25d52`) |
| V-2 | `CapacityAdmissionLifecycleTests/C1593_rollback_exit_keeps_reservation` `[Arguments("stale-sibling")]`, `("token-lost")`, `("lease-preflight")`, `("parallel-hold")` | grant `W` kept, `AdmissionCount==0`, wait `ActionPending` | red at base, each argument |
| V-3 | `CapacityAdmissionLifecycleTests/C1593_committed_non_launch_exit_releases_reservation` | pinned-Worktree `FailAsync` exit: wait `Canceled`/`claim-not-launched`, grant cleared, `AdmissionCount==0`, next wait grantable | red at base (`AdmissionCount` 1, state `Admitted`) |
| V-4 | `CapacityAdmissionLifecycleTests/C1593_preparation_restart_and_dispatch_consume_one_admission` `[Arguments("after-receipt", false)]`, `("after-receipt", true)`, `("queue-inserted", false)`, `("lost-wakeup", false)` | prep keeps the grant unspent; restart in backoff keeps it; launch spends once; `StartAccepted`/`LaunchSessionId`; one complete `UserPrompt` via `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync` | red at base (prep consumes) |
| V-5 | `CapacityAdmissionLifecycleTests/C1593_crash_after_launch_commit_consumes_one_admission` | the `LandDeliveryBoundary` at `dispatch-warning-claim-committed` throws (crash cut c3); a new provider recovers through `ResumeInterrupted`; `AdmissionCount==1`, wait `StartAccepted` with `LaunchSessionId==S`, one complete `UserPrompt` on S, no second session | red at base (receipt never written: `Admitted`, `LaunchSessionId` null) |
| V-6 | `CapacityAdmissionLifecycleTests/C1593_enqueue_failure_consumes_one_admission` | the launch sink throws; claim and `Starting` session kept; `AdmissionCount==1`, receipt bound to S; after recovery, one `UserPrompt` | guard; positive control: drop the receipt write from the consume -> red |
| V-7 | `CapacityAdmissionLifecycleTests/C1593_late_consume_refusal_keeps_tick_identities` | two queued tasks A, B in one tick. A test-only `LandDeliveryBoundary` point `capacity-consume-before` revokes A's grant on another connection, so A is `NotClaimed` and its tracked entry is still tracked with database values. B dispatches with its own single admission and `LaunchSessionId==B.AgentSessionId` | red at round-2 tip `730c76a2` pattern; positive control: replace `AbandonClaimAsync` with bare `Clear()` -> red |
| V-8 | `CapacityLockOrderTests/C1593_path_blocked_on_task_holds_no_provider_lock` `[Arguments("claim")]`, `("queued-reroute")`, `("routing-resume")`, `("continuation")`, `("cancel")`, `("retry")` | a test connection holds the task row `FOR UPDATE`; the path runs until its backend waits on that row (`pg_stat_activity.wait_event_type='Lock'` for its pid). At that moment, `pg_locks` shows no granted advisory with the `antiphon.capacity.kind.*` key for that pid. The test then releases and the path completes | red at base: `queued-reroute`, `routing-resume`; the others are guards |
| V-9 | `CapacityLockOrderTests/C1593_path_blocked_on_provider_already_holds_task` `[Arguments("claim-launch")]`, `("queued-reroute")`, `("continuation")` | a test connection holds the provider advisory; the path blocks on it; `SELECT ... FOR UPDATE NOWAIT` on the task from the test connection raises `55P03` | red at base: `claim-launch` (no provider lock at launch) and `queued-reroute` (no task lock first) |
| V-10 | `CapacityLockOrderTests/C1593_competing_claim_and_reroute_complete_without_deadlock` and `C1593_competing_claim_and_continuation_complete_without_deadlock` | two contexts barrier-released at the lock step; both complete; no `40P01`; exactly one dispatch | reroute: red with `40P01` on any task->provider claim without D-5 (seen at `bd0083f9c`); continuation: red on the provider-first claim (`730c76a2`) |
| R-1 | `WallRerouteDispatchTests`, `CapacityRecoveryTaskTests`, `CapacityRecoveryAcceptanceTests` (whole classes) and `AgentTaskDispatchBaseGuardTests/C508_GuardRefMismatchWarnedOnce` (4 args) | unchanged behaviour | - |

Deterministic means no sleeps: waits poll `pg_stat_activity`/`pg_locks` for the path's backend
pid, with a 30 s deadline. The advisory key is matched by `classid`/`objid` computed from
`hashtext(key)` on the same server. Lifecycle tests advance a `FakeTimeProvider`, not wall time.

### Red witnesses (run before the product commit, not in the table)

One build-slot build of the S1 test commit, then these `--no-build` filters. Each must show the
named assertion failure; zero tests or a fixture error is not red.

- `/*/*/AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection`
- `/*/*/CapacityAdmissionLifecycleTests/(C1593_rollback_exit_keeps_reservation*)|(C1593_committed_non_launch_exit_releases_reservation*)|(C1593_preparation_restart_and_dispatch_consume_one_admission*)|(C1593_crash_after_launch_commit_consumes_one_admission*)`
- `/*/*/CapacityLockOrderTests/(C1593_path_blocked_on_task_holds_no_provider_lock*)|(C1593_path_blocked_on_provider_already_holds_task*)`. Expected: the red arguments named in V-8/V-9 fail and the guard arguments pass.

V-6 and V-7 positive controls are method-scoped, run once each, and restored.

## Performance budget

Per [test performance standards](../../test-performance-standards.md):
- **Measured:**
  - Parked rounds on server2: single capacity methods took 1.2-11.4 s, and the first test in a
    host (shared Postgres plus clone setup) was about 10-11 s.
  - Leased builds of `tests/Antiphon.Tests` took 217-399 s compile and 220-520 s slot.
  - A whole Unit run took over 25 min (timed out), so it is excluded.
  - A five-class integration row hit the overall deadline, so regression classes are split.
- **Shared setup:** one shared Postgres per test host; each test clones an isolated database
  (existing `CapacityRecoveryTestSupport`). No per-test server boot. Lifecycle tests reuse one
  `CreateDispatcherProvider` per test.
- **Phases recorded per row:** slot wait, build, host start, per-test (clone, act, assert).
- **Budgets:** about 23 new results. p95 per method is estimated at 12 s, so the method
  timeout is 30 s and the lock-wait deadline inside a test is 30 s; both are provisional until
  Code records p95 from its TRX. Row budgets: CP-1 about 12 min including build; CP-2 about
  3 min; CP-3 about 10 min. The whole round stays under 30 min of test time. A row over 2x its
  estimate is a Debug item, not a timeout raise.

### Checkpoints

Lane: Linux, server2 (runner default `globalRunnerId=server2`). No `-Runner` or `-Platform` pin.
Build once, then use `--no-build` (operator direction).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1593/` | c1593-new | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityLockOrderTests*)/*` | V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10 | all listed, 0 failed/skipped (12 lifecycle + 11 lock-order results) | 23 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S1 | CP-1 | c508-guard | `/*/*/AgentTaskDispatchBaseGuardTests/(Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*)\|(C508_GuardRefMismatchWarnedOnce*)` | V-1, R-1 | all listed, 0 failed/skipped (1 + 4 args) | 5 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S1 | CP-1 | capacity-regress | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(CapacityRecoveryTaskTests*)\|(CapacityRecoveryAcceptanceTests*)/*` | R-1 | >= 34 executed, 0 failed/skipped | 34 | 10 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Suffix check at base: `CapacityAdmissionLifecycleTests*`, `CapacityLockOrderTests*`,
`WallRerouteDispatchTests*`, `CapacityRecoveryTaskTests*` and `CapacityRecoveryAcceptanceTests*`
each match exactly one class (`WallRerouteFixture`/`WallRerouteHarness` and the other
`CapacityRecovery*` classes do not match). Code verifies each intended class in the fresh TRX.
`Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*` and
`C508_GuardRefMismatchWarnedOnce*` have no sibling method prefixes in that class.

Not run: the whole Unit lane (25 min timeout, operator direction), native Windows C508 E2E
(D-7), and the checkpoint tool's sealed receipt (64 MiB reader cap, see Ground truth). Code
reports the CHECKPOINT-shaped line per row from its build-slot runs, with the TRX path and
counts.

### Landing

Cut the branch from `origin/rc/2026-10-27`. Before Review, run `git fetch` and
`git merge-tree --write-tree` against both `origin/rc/2026-10-27` and `origin/master`. A master
merge carries this plan doc; per the rc cherry-pick notes, drop plan docs from the rc pick if
rc lacks `docs/superpowers/plans`.
Run `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f84228f285dd3e4ad7b5dc8260cbc9f819256f50 -HeadRef <tip>`.
