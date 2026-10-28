# CARD-1593 capacity admission: lock order and lifecycle (redo design)

Task a9ca2252, Plan stage. Base `origin/rc/2026-10-27` = `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.
The change ships to rc/2026-10-27 first, then master. All line numbers below are at that base.

Parked attempts read: `feat/card-task-1794d6c7` (`053aa52b`, round 1) and
`feat/card-task-b095c160` (`730c76a2`, round 2). Their sign-in lock fix (`d2d7acc82`) is
extracted elsewhere and is out of scope here.

**Round 2 (task 444ea5d9).** Amended for design review fe4577c5
(`.antiphon/task-fe4577c5.md` at `6fed4e7c`). The endorsed direction is unchanged. What
changed: the rank table, the claim's consume point (flush first, then consume), the L1, L7,
L16 and L20 rows, the new L23, the consume-failure contract (D-9), and the witnesses. See
[Review fe4577c5 response](#review-fe4577c5-response) for each finding with its evidence.

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
| Supervisor redemption holds the agent row across connections (round 1 of this plan; review fe4577c5 F1) | **No (corrected in round 2).** `SuperviseAsync` opens a tx at `AgentSupervisorService.cs:127`, and Herdr `ObserveAsync` row-locks the agent (`HerdrSupervisionStateService.cs:26`). On the capacity branch, the code first runs `await CompleteAsync(false); await transaction.DisposeAsync();` at `:226-227` (`CompleteAsync` `:349-356` saves and commits). Only then does it call `TryHandleCapacityWaitAsync` (`:230`), which reaches `RedeemAsync` at `:507`. The comment at `:206-208` states the intent: "Release the consumer lock before entering that lane". The agent row is therefore not held while the supervisor waits for the provider, so Rule X holds for L16. What remains is a lifecycle defect: consume at `:507` comes before `StartSupervisedRestartAsync` `:525`, so a failed start leaks one admission. That stays in S3. |
| The claim writes no Agents row under its new provider lock (round 1 L1) | **No (review F1).** Cold launch edits the agent at `AgentTaskDispatcher.cs:6003-6011` (pool fields) and `:6102-6104` (`PersistentSessionId`/`Status`/`UpdatedAt`), and `ResolveAgentAsync` sets `existing.Kind` at `:7457`. All of it is flushed by `SaveChangesAsync` `:6150`. Warm reuse edits the agent at `:8231-8234`, and the caller flushes at `:5706`. Neither row is locked earlier: only the specialist owner row is, at `:5539`. A consume placed "before `Status = Dispatched`" would therefore write Agents rows under the provider lock. At base the flush also runs after the global parallel key (`:6098` -> `:6150`), so a claim that waits on an agent row holds that key. |
| Released-seat continuation conforms (round 1 L7) | **No (review F1).** After `SupersedeTaskWaitsOnAsync` at `AgentTaskService.cs:3845` (ambient tx, provider held), `RequeueCoreAsync` calls `RemoveEphemeralAgentAsync` (`:3881` -> `:3951`). That call deletes the pool Agents row (`:4004`) or marks it Idle (`:3997`). The row was never locked earlier: `LockReleasedAnswerAsync` (`:3632-3643`) locks only the task, release and session. |
| Deferred-task fence orders task then session (round 1 L20) | **No (review F2).** `AppDbContext.cs:163` locks the `AgentSessions` row and `:164` then locks the `AgentTasks` row, so the order is session->task. It is active only in the scope `AgentTaskReplyService.cs:121` binds (`OnTranscriptActionAsync`). That scope reaches the quota path below through `JudgeOpenTaskAsync` `:124` -> `HandleApiErrorTurnAsync` `:229`. |
| Every provider->task writer is in the table (round 1) | **No (review F2).** The quota-blocked settlement `AgentTaskReplyService.cs:1970-1977` mutates the task (and `ApiErrorRecoveries` `:1980-1988`) with no ambient tx, then calls `SupersedeTaskWaitsOnAsync` at `:1991`. That call opens its own tx and takes the provider lock (`CapacityRecoveryService.cs:342-347`), and its `SaveChangesAsync` at `:372` flushes the dirty task row under that lock. Inside the fence scope, the same save runs the fence: provider->session->task. Added as L23. |
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
| 3 | `AgentTasks` row (`FOR UPDATE`, or any EF/SQL write of the row); in a claim the claimed task first, then (warm reuse) the previous token owner, a non-`Queued` task no other claim takes as its own | task id |
| 4 | physical (launch-target / pool) `Agents` row the task runs or ran on | agent id |
| 5 | `RunnerSeatReleases`, `AgentSessions`, `SessionQueuedMessages` rows, including FK `KEY SHARE` on a referenced existing session | row id |
| 6 | parallel-tasks advisory | `hashtext('antiphon.delegation.parallel-tasks')` |
| 7 | capacity provider advisory, ascending `AgentKind` when several | `hashtext('antiphon.capacity.kind.<Kind>')` |
| 8 | capacity rows (`CapacityRecoveryWaits`, `CapacityRecoveryProviderStates`, `CapacityRecoveryWaitHolds`) | row id |

Task-owned child rows (`AgentTaskEvents`, `AgentTaskParks`, dispatch-warning intents, the
verification reservation) carry no rank of their own:
- Writers of parks and verification rows hold the owning task row first. Park publication
  runs task->session at `TaskParkPublicationService.cs:392-393`. Verification locks the task at
  `VerificationExecutionService.cs:73`.
- The claim creates dispatch-warning intents. They are materialized after the commit under
  their own row lock (`DispatchBaseWarningIntentService.cs:87/:181`, own tx, no task or capacity
  lock), so that path is n/c.

Holding the task row therefore covers these rows. The S2 ledger checks this.

Round 2 renumbers the ranks. It adds rank 4, the physical agent row (review F1), so rank 5
onward moves down one place. Rows L1-L25 below use the new numbers.

- **Rule P (provider is a leaf).** While a rank-7 lock is held, the transaction may write only
  rank-8 rows, plus rows it already locked earlier in the same transaction. That covers EF
  `SaveChangesAsync` flushes of tracked rows, the trap behind the reroute inversion. The claim
  is stricter: after the consume it writes rank-8 rows only. S1 asserts that the claim's
  ChangeTracker holds no `Added`/`Modified`/`Deleted` `AgentTask`, `Agent` or `AgentSession`
  entry when `RedeemOnAsync(consume:true)` starts (D-2).
- **Rule X (no cross-connection waits).** A path that holds a rank 1-6 lock in its own
  transaction may not wait for a rank-7 lock on a different connection. At base every
  cross-connection provider waiter first commits its row locks: the supervisor at
  `AgentSupervisorService.cs:226-227` (see Ground truth), and the queue flush and retained return
  hold none.
- **Rule F (the global parallel key never waits on a row).** Rank 6 is one global key that
  serializes every non-specialist claim. Its only other holder, the settings write
  (`DispatchConcurrencySettingsService.cs:726-728`), takes no rank 2-5 row. In S1 the claim
  explicitly locks every existing row its flush will touch (ranks 4-5) **before**
  `RefuseParallelAsync`. The flush after rank 6 then writes only rows the claim already holds,
  new rows, and task-owned child rows. At base the flush at `:6150` can block on an agent row
  while holding the global key.

*Rejected:* provider-first, Round 2's order. It needs the wait kinds before the task lock,
then a speculative pre-lock and a rollback when the kind changes. It would also have to change
every `AgentTaskService` path that already locks the task first: continuation, requeue, retry,
cancel. Round 2 F1 shows how easy one of those is to miss. Task-first keeps the provider a leaf
and changes only the paths listed in S1.

**D-2 Admission lifecycle: reserve, consume, release.**
- *Reserve:* `GrantReadyAsync` sets `ProviderState.GrantedWaitId = W` (wait `ActionPending`). It is
  durable and reusable: any number of ticks, preparation commits and restarts observe the same
  grant, and `StillHoldsGrant`/`GrantExpired` is its only bound.
- *Consume (amended for review F1):* `RedeemOnAsync(db, ..., consume: true)` runs only inside
  a claim transaction, as the claim's **last lock**, after the flush. The three launch shapes set
  `Status = Dispatched` at cold launch `:6100`, warm reuse `:8247` and standing placement `:8502`.
  They reach the consume at two physical points:
  - Cold launch: between the `SaveChangesAsync` at `:6150` and `CommitAsync` at `:6151`.
  - Warm reuse and standing placement: both return `ReuseOutcome.Reused` to `:5705`, so the
    consume runs between the `SaveChangesAsync` at `:5706` and `CommitAsync` at `:5707`.

  Before `RefuseParallelAsync`, each shape takes its row locks in rank order (Rule F):
  - Cold launch onto an existing pinned or pool row (not `freshAgent`): agent
    `SELECT 1 ... FOR UPDATE` (rank 4) just before `RefuseParallelAsync` at `:6098`. The lock is raw SQL, so the
    in-memory edits from `:6003-6011` and `:7457` survive.
  - Warm reuse: previous-token-owner task (rank 3, `:8244`, moved ahead of the agent edits),
    then the agent `FOR UPDATE` (rank 4) with a reload and a recheck of `Status == Idle &&
    PoolIdleSince != null`, then the reused session `FOR KEY SHARE` (rank 5). Only after these
    does the code mutate `:8231-8234`. If the recheck fails, the code returns the outcome base
    gives for that state (`WaitForAgent` for a pin, `SpawnFresh` for a pool pick).
  - Standing placement: the live session `FOR KEY SHARE` (rank 5) before `RefuseParallelAsync` at `:8482`. It writes no
    agent row.

  The full claim order becomes 1 -> 2 -> 3 -> (3) -> 4 -> 5 -> 6 -> flush -> 7 -> 8.
  `RedeemOnAsync` first asserts that the tracker is clean (Rule P). Then, under the provider
  lock, it checks the grant, increments `AdmissionCount`, clears the grant, and **writes the
  receipt in the same commit**: `State = StartAccepted`,
  `SessionId = LaunchSessionId = <launched or reused session>`, `DispatchAttemptId`. Its own
  `SaveChangesAsync` writes capacity rows only. The task's `Dispatched`, its session row and
  the receipt commit atomically or not at all.
- *Callers that keep `RedeemAsync` unchanged.* The own-scope wrapper keeps writing `Admitted`
  with no receipt for its remaining callers: supervisor `AgentSupervisorService.cs:507`, queue
  flush `SessionMessageQueueService.cs:2578`, retained return (via the shared helper
  `AgentTaskDispatcher.cs:1757-1762`, called at `:633`) and the service-level tests in
  `CapacityRecoveryAcceptanceTests`. The shared helper therefore gains a `consume` parameter:
  `:633` passes `true` (unchanged until S3) and the dispatch gate `:961` passes `false`.
  Switching the helper globally would stop retained return from ever consuming.
- *Release:* a claim exit that commits without launching releases the reservation in that same
  commit. The exits are FailAsync, optional-work expiry, interim/reuse-gate block, Grok/Claude
  probe failure, repair-source fail, worktree-base block and progress-capture fail (Round 2's
  `CommitClaimWithoutLaunchAsync` list). Release cancels the wait with `claim-not-launched` and
  clears the grant if it is held; `AdmissionCount` is unchanged. A claim exit that rolls back
  touches nothing, so the reservation survives.
- *Pre-claim gate* (`:957-971`): read-only for consumption (`consume: false`). It still commits
  durable refusals (exhaustion, clock-not-due defer, disabled) on its own scope, before any task
  lock (a rank-7/8 leaf).

*Rejected:* redeem-then-compensate (Round 1: refund on rollback). Every exit needs its own
compensation, and a crash between the redeem and the compensation still leaks. *Rejected:* a
post-commit receipt. It leaves a crash window, and after a `Clear()` it reads a stale task
instance (F4).

**D-3 Exactly-once proof (dispatch consumers).** I1: for a queued-task wait, `AdmissionCount`
changes only inside `RedeemOnAsync(consume:true)`. The wrapper callers L6, L16 and L17 are
other consumers (S3).
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
expiry during preparation (`StalledAdmissionTimeout`, `CapacityRecoveryPolicy.cs:87-90`, 2 x 60 s by
default). While the preparer is in flight the tick holds the task before the gate
(`AgentTaskDispatcher.cs:1261`). On the first tick after preparation, the `consume:false` gate
sees `GrantExpired` (`CapacityRecoveryService.cs:1015-1017`) and revokes the grant through
`RearmRevokedGrantOnAsync` (`:1042-1062`). That call moves `ActionOrdinal`/`ActionKey` by
`PrepareNextAttemptOnAsync`, sets `DueAt = now + StalledAdmissionTimeout`, and leaves
`AdmissionCount` unchanged (`AttemptWouldExhaust` reads `AdmissionCount` only, so churn from
expiry never exhausts a wait). If expiry lands between the gate and the consume, the consume
refuses and the claim abandons (D-9, `NotClaimed`). Either way the next grant is re-issued and
the launch consumes once: c5 ends with `AdmissionCount == 1`. (c6) Lost commit acknowledgement:
see D-9; the durable reread makes it c3.

**D-4 Rollback model: restore task identities.** Every claim-transaction rollback and every
refused late consume goes through one helper, `AbandonClaimAsync(transaction, claimed)`.
It is built from `FinishParallelHoldAsync` (`AgentTaskDispatcher.cs:8004-8020`, review advisory
A1). That method already snapshots and reloads the tracked non-`Added` `AgentTask`, `Agent` and
`AgentSession` instances. D-4 round 1 listed tasks only. The same rollback also lives at
`:6161-6168`.
1. Snapshot the tracked non-`Added` `AgentTask`, `Agent` and `AgentSession` CLR instances.
2. Roll back and dispose the transaction, then `ChangeTracker.Clear()`.
3. Reload each snapshotted instance in place. Reloading adopts concurrently committed values,
   so nothing committed is lost. It discards only the tick's own unsaved edits to other rows,
   and by Rule P and the flush-first claim there are none.

`FinishParallelHoldAsync` keeps its Held-event write, but makes its rollback through the
helper. The rollback exits that today call a bare `Clear()` (`:5973`, `:5993`, `:6058`,
`:6082`) and the catch at `:6153` also route through the helper.

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
so no cross-iteration flush happens under a provider lock. The released-seat continuation does **not**
conform (review F1). S1 adds its rank-4 lock: `LockReleasedAnswerAsync`
(`AgentTaskService.cs:3632-3643`) gains the physical agent row `FOR UPDATE` between the task
(`:3634`) and the release (`:3637`) whenever `task.AgentId` is set. A changed `AgentId` after the
reload at `:3767` is the existing `answer_revision_changed` conflict. Then the later
`RemoveEphemeralAgentAsync` (`:3881`) writes a row the transaction already holds, which Rule P
allows. Moving `RemoveEphemeralAgentAsync` ahead of the supersede was rejected: its sole-owner
and terminal-session reads (`:3970-3995`) would then run before `StopDelegateAsync` (`:3843`)
and change its decision. Requeue/retry (L9) and cancel (L10) use the supersede's own
per-kind transaction, so the provider is released before their task and agent saves. They
conform as long as nothing is dirty at `:3845`/`:2949`, and the V-8 `retry`/`cancel` guards
check that. Cancel's supersede gets a task lock first in S2.

**D-6 Legacy leaked admissions.** Rows already leaked (`Admitted`, no receipt, task `Queued`)
recover through the existing `RearmStalledAdmissionsAsync`. No data migration ships in rc.

**D-7 Native C508 E2E expectations** (`DispatchBaseWarningDeliveryE2ETests.C508_*`) are obsolete
fixtures; the card decision leaves them for their own repair. No native Windows row in this plan.

**D-9 Consume and receipt failure contract (review F3).** The flush before the consume keeps
base behaviour. A `SaveChangesAsync` failure at `:6150`/`:5706` is a data error on rows the
claim wrote: the task fails through the existing catch (`:6153`) and the tick's generic catch
(`:1120-1130`), as today. Nothing has been consumed, and `CancelOrphanedWaitsAsync`
(`CapacityRecoveryService.cs:445`) cancels the failed task's reservation. Code confirms that
`OrphanedWaits` covers a terminal task's consumer and adds the clause if it does not. The
consume step is `RedeemOnAsync(consume:true)`, its capacity-only save, and the commit. Any
exception there, or a refusal (grant mismatch, grant expired, wait no longer `ActionPending`),
goes to `AbandonClaimAsync` and never reaches the generic catch:
- **Refusal, or an exception before the commit was sent** (including a `40P01` or `40001`
  victim, a `55P03`, or the test interceptor's injected receipt failure): the task stays
  `Queued` with its `AgentSessionId`/`AgentId` unchanged in the database. No session row
  exists, and the reservation is intact. The outcome is `NotClaimed`. One deduped Warning event
  `capacity consume refused: <reason or SQLSTATE>` is saved after the rollback, in its own save.
  A deterministic failure cannot loop forever: the grant expires (c5), and each re-arm then
  waits `StalledAdmissionTimeout`.
- **Exception from `CommitAsync` itself** (a lost acknowledgement is possible): after the
  helper reloads, reread the task. If it is `Dispatched` with `AgentSessionId == S`, the claim
  committed; treat it as c4 (enqueue refused). Record the Warning, leave the `Starting`
  session to interrupted-launch recovery (`SessionReconciliationService.cs:115/:342`,
  `AgentSessionLaunchQueue.cs:67`), and count the task as dispatched. Otherwise, handle it as
  the first case.
- `OperationCanceledException` while `ct` is cancelled: abandon, then rethrow.

This removes the base reason for the swallow at `:1058-1068`. That block is deleted together
with the receipt step.

**D-10 Grant held through slow claim work (review advisory A2).** The reservation now survives
`git worktree add` inside the claim and the whole remote preparation (`HeldForRemotePrep`).
Mirror preparation on server2 often outlasts the 2-minute expiry. Expect a c5 expiry and re-arm
on most runner-bound cold launches. Each costs at most about `StalledAdmissionTimeout` plus one
grant cycle of delay, and no attempt. While a grant is reserved, no other waiter of that
provider is granted, so the provider admits about one launch per preparation window. This
holds only for tasks that have a capacity wait; tasks without one are ungated. Accepted for
rc. V-4 records the preparation p95 from its TRX and states the expiry count it observed. A
later card may extend the grant on a `HeldForRemotePrep` commit.

**D-11 How Final Review verifies without sealed receipts (review advisory A6).** The checkpoint
reader caps each file at 67,108,864 bytes (`scripts/lib/checkpoint-file-reader.ps1:196`), and
`Antiphon.Tests.dll` is about 69 MB, so CP rows run outside the checkpoint tool. Code and Final
Review each:
1. Build once at the reviewed tip with `scripts/build-slot.ps1` (`--property:OutputPath=bin-c1593/`).
2. Run every CP filter `--no-build`, saving the TRX under `.antiphon/c1593-<cp>/` (gitignored).
3. Report one CHECKPOINT-shaped line per row: tip SHA, filter, executed/passed/failed/skipped,
   TRX path, and TRX SHA-256.

Final Review re-runs CP-1..CP-3 itself rather than trusting the Code report. It reports
`reviewedSourceClean: false` with the reason "reader cap: sealed receipts unavailable", never
`true`. A separate card should raise the reader cap. It is not in this card's scope.

**D-8 Next-stage conflict.** The operator direction says "next: review" and the task text
says "next: code after a design review". This plan carries its own `## Verification design`
and `### Checkpoints`, so it hands off to a design Review. Code follows the Review. Round 2
changes the rank table, the lock-acquisition table and the consume point of the lifecycle, so it
hands off to a design Review of the amendment again (task 444ea5d9 brief: "next: review if
you changed the lock table or lifecycle").

## Global lock-acquisition table (every site; rank sequence per transaction)

"Leaf" means the transaction holds ranks 7-8 only. "n/c" means no capacity lock is ever held,
so only the rank 1-5 order matters and any inversion there is a same-connection cycle that
PostgreSQL detects (`40P01`). The rank numbers are round 2's (D-1). "Base sequence" is what the
code at `f84228f28` does; "S1 sequence" is given where S1 changes it.

| # | Path | Site(s) | Base sequence | S1 sequence / conformance | Slice |
|---|---|---|---|---|---|
| L1 | Dispatch claim, cold launch | `AgentTaskDispatcher.cs:5532` tx; `:5535` counted (1); `:5539` owner agent (2); `:5543` task (3); `ResolveAgentAsync` `:7447` pinned agent read, unlocked; `:6098` parallel (6); flush `:6150` writes agent (4, unlocked until now), inserts session (new) and events/parks (child) | 1->2->3->6->[4 at flush] | 1->2->3->**4** (explicit, existing agent only)->6->flush->**7** consume->**8** receipt; tracker clean at 7 | S1 |
| L1w | Dispatch claim, warm reuse | as L1 to `:5543`; `:8097`/`:8203` agent pick, unlocked; edits `:8231-8234`; `:8244` -> `:7978` previous token owner task (3); `:8245` parallel (6); flush `:5706` writes agent (4) and FK `KEY SHARE` on the reused session (5) | 1->2->3->(3)->6->[4,5 at flush] | 1->2->3->(3)->**4** (lock, reload, recheck Idle)->**5** (`FOR KEY SHARE`)->6->flush->**7**->**8** | S1 |
| L1s | Dispatch claim, standing placement | as L1 to `:5543`; `PlaceOnStandingAgentAsync` `:8400` (owner row reloaded `:8403`); `:8482` parallel (6); flush `:5706` (task, event; FK `KEY SHARE` on the live session, 5) | 1->2->3->6->[5 at flush] | 1->2->3->**5** (`FOR KEY SHARE`)->6->flush->**7**->**8** | S1 |
| L2 | Pre-claim redeem (gate) | `:959` -> `:1764` -> `CapacityRecoveryService.cs:248` own scope | 7->8 leaf, but **consumes** | 7->8 leaf, `consume:false` (refusals and expiry re-arm only) | S1 |
| L3 | Queued reroute | `AgentTaskDispatcher.cs:1682` kind edit; `:1685` -> `CapacityRecoveryService.cs:347`, flush `:372` | 7->3 (implicit) **no** | owned tx: 3 (`FOR UPDATE`, recheck)->7->8, then save of the held task (Rule P) | S1 |
| L4 | Routing-blocked resume | `AgentTaskDispatcher.cs:1826` -> `CapacityRecoveryService.cs:347`; edits `:1842-1846` saved at loop end | 7->3(other) **no** | one tx per task: 3->7->8 | S1 |
| L5 | Post-dispatch receipt | `AgentTaskDispatcher.cs:1054` -> `CapacityRecoveryService.cs:405` | 7->8 (+ flush of dirty tick rows) crash window | removed (receipt in the claim commit) | S1 |
| L6 | Retained return | `AgentTaskDispatcher.cs:633` -> `:1757` -> `RedeemAsync`; `:636` counted (no tx) | 7 leaf, consume before the counted slot and separate save | unchanged (`consume:true` kept) | S3 |
| L7 | Released-seat continuation | `AgentTaskService.cs:3764` tx; `:3634/:3637/:3640` task, release, session (3,5,5); `:3845` provider (7); `:3881` -> `:3997`/`:4004` pool agent write (4) | 3->5->7->[4] **no** (Rule P; `40P01` against L1 once S1 holds 4 then waits 7) | 3->**4** (agent `FOR UPDATE` in `LockReleasedAnswerAsync`)->5->7->8, later writes are of held rows | S1 |
| L8 | Released-seat answer accept | `AgentTaskService.cs:3576` -> `:3632` | 3->5 | 3->4->5 (shares the S1 helper) | S1 (by sharing) |
| L9 | Retry/escalate requeue | `RequeueAsync` `AgentTaskService.cs:3792` (in-process gate) -> `RequeueCoreAsync` `:3845` (owned per-kind tx) | 7 then task and agent saved outside | conforms while nothing is dirty at `:3845` (V-8 `retry` guard) | guard |
| L10 | Cancel | `AgentTaskService.cs:2949` | 7 (owned), task and agent saved after | conforms (V-8 `cancel` guard) | S2 hardening |
| L11 | Released-seat recovery tick | `AgentTaskDispatcher.cs:5358` grok-rules advisory (own tx); `:5395/:5398` task, session (3,5); `:5337` -> `RetryAsync` (= L9) | 3->5 | n/c | - |
| L12 | Readiness/held trace | `AgentTaskDispatcher.cs:1366` task (3) | 3 | n/c | - |
| L13 | Brief recovery hold / absent launch | `:2839`, `:3650` task (3) | 3 | n/c | - |
| L14 | Task create | `AgentTaskService.cs:1577-1579` card, `:1585` handoff action, `:1683` interim latch -> `InterimVerificationPolicy.cs:161-168` task (3); `:1897` host-budget advisory; `:4818` `EnsureWaitOnAsync` (no provider lock) | card->action->3 | n/c | - |
| L15 | API-error wall | `ApiErrorRecoveryService.cs:102` tx -> `:543` supersede (7), `:571` ensure, `:588` bump wave `CapacityRecoveryService.cs:647` (7, same kind) | 7 only (holds/recovery rows) | conforms | - |
| L16 | Supervisor (standing-agent restart) | `AgentSupervisorService.cs:127` tx; `HerdrSupervisionStateService.cs:26` agent (2/4); commit and dispose `:226-227`; then `:230` -> `:507` `RedeemAsync` (7, own scope); `:522` session/state save; `:525` start | 4, commit; then 7->8 leaf; consume before start | Rule X holds (corrected, see Ground truth); lifecycle leak only | S3 |
| L17 | Queue flush redemption | `SessionMessageQueueService.cs:2578` (no tx) | 7 leaf, consume before flush | lifecycle | S3 |
| L18 | Capacity reconcile (hosted) | `CapacityRecoveryService.cs:138` grant, `:457` orphan cancel, `:1181` re-arm (+ `SessionQueuedMessages` rows), `ConsumePendingReleasesAsync` (no lock); `:691` counted slot (statement-scoped) | 7->8 leaf | conforms | - |
| L19 | Land paths | `AgentTaskLandService.cs:84/:353/:393/:1603`, `AgentTaskLandingProtocol.cs:954`, `AgentTaskLandMonitorService.cs:23/:56-57`, `AgentTaskLandRequestWriter.cs:233`, `AbandonedLandingReconciler.cs:81` | 3 (+5) | n/c | - |
| L20 | Other task-row lockers | `BlockedTaskParkingService.cs:92/:240`, `AgentTaskDecisionQuestionService.cs:59` (NOWAIT), `VerificationCleanupService.cs:25`, `BootStallWarningWriter.cs:77/:144`, `RemoteWorkspaceService.cs:261`, `RemoteWorkspaceReadiness.cs:29`, `PipelineHandoffActionService.cs:33-34/:55/:159-160` (cards, actions), `SessionMessageQueueService.DispatchBrief.cs:61`, `SessionMessageQueueService.cs:1966-1969`, `ProviderWaitWarningWriter.cs:34-38`, `TerminalRunnerSeatReleaseService.cs:872/:875/:878` (task, release, session), `SettlementSyncRecoveryService.cs:300`, `BlockedTaskSyncRecoveryService.cs:139`, `VerificationExecutionService.cs:73`, `TaskParkPublicationService.cs:392-393` (task, session), `AgentTaskReplyService.cs:5276`, `ReviewEvidenceRecoveryService.cs:140`, `CardWorktreeCleanupExecutor.cs:80/:85`, `CardWorktreeMirrorCleanupService.cs:100-103/:112`, `TaskWorktreeRetirementService.cs:88`, `WorktreeRemovalEvidence.cs:234` | 3 (+5) | n/c | - |
| L21 | Agent-row lockers | `AgentService.cs:943/:950/:1012/:1016/:1245`, `AgentControlService.cs:1277/:1283/:1485`, `AgentPinnedInstructionService.cs:489/:500`, `HerdrPlacementLock.cs:18/:21`, `HerdrSupervisionStateService.cs:26`, `StandingSpecialistHealthService.cs:28`, `StandingSpecialistSeatService.cs:54`, `StandingSpecialistRoutingService.cs:172`, `SpecialistRequestService.cs:482` (after counted `:182`), `SpecialistRequestService.Qualification.cs:43` (counted), `AgentSessionService.cs:413-417` (`FOR NO KEY UPDATE`) | 1->2/4 | n/c (no row here holds 7; L16 commits first) | - |
| L22 | Other advisories and disjoint rows (never with ranks 3/7) | host budget `HostBudgetService.cs:46`, `AgentTaskService.cs:1897`; `DelegationOpenGate.cs:140`; `SourceLandingAdmission.cs:73`; `DispatchConcurrencySettingsService.cs:728` (parallel key, settings writes, no rows of ranks 2-5: Rule F); `GrokRulesRefreshService.cs:187`; channel outbound (`ChannelOutboundService.cs:77/:209/:370/:547`, `ChannelOutboundDeliveryPump.cs:283/:328/:609`, `ChannelOutboundFailureRecorder.cs:27/:83/:89`, `ChannelOutboundMetadataRepair.cs:38`, `ChannelBridgeService.cs:454/:502`); `PipelineDefinitionService.cs:68`; `CardService.cs:1402`; `CardCompletionAcceptanceService.cs:94/:103` (card, workflow run); `ProjectCascade.cs:86`; `ExpectationLedger.cs:411/:535/:558/:579/:626`; `SessionMessageQueueService.CheckPublication.cs:23` (specialist request); `DispatchBaseWarningIntentService.cs:87/:181` (intent, post-commit); `WorkspaceReservationJournal.cs:253`; `DatabaseSeeder.cs:63`; Hangfire `KeepSixReminderJob.cs:41` | own key or row only | n/c | - |
| L23 | Quota-blocked settlement (new, review F2) | `AgentTaskReplyService.cs:1970-1977` task edits and `:1980-1988` `ApiErrorRecoveries` edits (no tx); `:1991` -> `CapacityRecoveryService.cs:342-347` owned tx, provider (7); flush `:372` writes the task (3), and in the fence scope (`:121`) the fence adds 5->3 | 7->[3] or 7->5->3 **no** (Rule P) | S2: own tx; task `FOR UPDATE` (3), session if fenced (5), then mutate, supersede ambient (7->8), save, commit | S2 |
| L24 | Deferred-task fence (corrected, review F2) | `AppDbContext.cs:163` session (5) then `:164` task (3), on every save in the `AgentTaskReplyService.cs:121` scope | 5->3 **no** | S2: swap to task (3) then session (5); the identity checks at `:167-171` are order-independent | S2 |
| L25 | Session-row lockers (re-grep, round 2) | `SessionReconciliationService.cs:208/:649`, `AgentSessionRuntime.cs:213` (comment `:1454`), `AgentControlService.cs:547`, `SessionMessageQueueService.cs:5653`, `GrokRulesRefreshService.cs:240`, `StandingBootWarningWriter.cs:57` (`SKIP LOCKED`) | 5 | n/c; none takes 7 in the same tx. A later task write in the same tx would be 5->3 like L24; the S2 ledger reports it | - |

`AgentTaskDispatcher.cs:4324`, `:5552`, `AgentSessionRuntime.cs:1454`, `StandingBootWarningWriter.cs:24` and
`SessionMessageQueueService.cs:5646` matched the re-grep only in comments.

## Slices

**S1 (rc slice, the smallest fix for C508 that does not introduce an inversion).**
S1 covers rows L1, L1w, L1s, L2, L3, L4, L5, L7 and L8. Without L3, L4 and L7, the S1 claim's
new order (3->4->5->6->7) recreates Round 1 F1 against reroute and Round 2 F1 against the
continuation.
- `server/Application/Services/CapacityRecoveryService.cs`:
  - Add `RedeemOnAsync(db, waitId, actionKey, kind, path, ct, consume, launchSessionId)`. It
    requires the ambient transaction, takes the provider lock, and writes the receipt on
    consume.
  - When `consume` is true, it first throws `InvalidOperationException("capacity consume with
    dirty task/agent/session rows")` if the tracker holds any `Added`/`Modified`/`Deleted`
    `AgentTask`, `Agent` or `AgentSession` entry (Rule P).
  - Keep `RedeemAsync` as its own-scope wrapper, with unchanged `Admitted` semantics (D-2).
  - Add `ReleaseClaimReservationOnAsync(db, taskId, kind, reason)`.
- `server/Application/Services/AgentTaskDispatcher.cs`:
  - The helper at `:1757-1762` gains `consume`. The gate at `:957-971` passes `false`; retained
    return at `:633` passes `true`.
  - Rank-4/5 locks before `RefuseParallelAsync` (D-2): cold launch `:6098`, warm reuse
    `:8244-8245` (with the agent reload and recheck), standing placement `:8482`.
  - Consume after the flush and before the commit: `:6150`/`:6151` and `:5706`/`:5707` (D-2).
    A refusal or exception follows D-9.
  - Committed non-launch exits release the reservation.
  - Add `AbandonClaimAsync`, built from `FinishParallelHoldAsync` `:8004-8020` (D-4). Route the
    rollback exits that `Clear()` (`:5973`, `:5993`, `:6058`, `:6082`), the catch at `:6153`, and
    `FinishParallelHoldAsync` itself through it.
  - Delete the dispatch receipt block `:1050-1070`.
  - Reroute (D-5) adds `RewalkOutcome.ClaimLost`. `ResumeRoutingBlockedAsync` uses one
    transaction per task.
- `server/Application/Services/AgentTaskService.cs`: `LockReleasedAnswerAsync`
  (`:3632-3643`) locks the physical agent row between the task and the release (D-5, L7/L8).
- Tests:
  - New: `tests/Antiphon.Tests/Application/CapacityAdmissionLifecycleTests.cs` and
    `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`.
  - Port `Stale_claim_preserves_capacity_grant_for_the_fresh_inspection` into
    `AgentTaskDispatchBaseGuardTests.cs`.
  - Rewrite the two `WallRerouteDispatchTests` receipt tests (V-14).
- Reusable from parked branches:
  - `CapacityRecoveryAcceptanceTests` rollback case and `CapacityRecoveryTaskTests` `configure`
    hook: copy.
  - `CapacityAdmissionTimingTests` four-cut body from `11251ebdd`: port into the lifecycle class
    as V-4.
  - Competing claim/reroute body from `bd0083f9c`: port as V-10.
  - `RedeemOnAsync(consume:)` and `CommitClaimWithoutLaunchAsync` call-site list from
    `8bb811e47`: reuse **without** its provider-first claim prelude.

**S2 (master, then rc port; deferred).**
- Cancel (L10) locks the task before the supersede.
- Quota-blocked settlement (L23) runs in one owned transaction: task `FOR UPDATE`, then the
  mutation, then an ambient supersede, then save and commit.
  - *Why S2, not S1:* it cannot deadlock with the S1 claim. Its task is `Working`; the tick
    claims only `Queued` rows, and the claim's `Status == Queued` filter (`:5543-5545`) rolls
    back before any provider lock. The claim also never waits on a row while it holds the
    provider. Its only cycle partner is L7 on the same task and session, and that cycle is
    same-connection (PostgreSQL resolves it as `40P01`) and present at base. It breaks Rule P,
    so the S2 ledger would throw on it; S2 fixes it before the ledger lands.
  - Add a V-8 argument `quota-blocked` with S2.
- Fence (L24): swap to task then session.
  - *Why S2:* the S1 claim's rows never meet the fence's rows. The fence's task is
    `Dispatched`/`Working`; the claimed task is `Queued`. The swap fixes only the pre-existing,
    detectable L24-vs-L7 pairing.
- Add a debug-build `LockOrderLedger`. It throws when a rank is inverted, or when a tracked
  `AgentTask`/`Agent`/`AgentSession` row is flushed under rank 7 without its earlier lock
  (Rule P).

**S3 (deferred, separate card).**
- Supervisor (L16): consume in the start commit with a receipt, instead of `:507` before
  `:525`. Rule X already holds.
- Queue flush (L17) and retained return (L6): atomic consume plus receipt.
- Counted slot (L6, `:636`): hold the lock in a transaction.
- A baseline transcript sequence on reuse receipts (advisory A3).

### Residual risk if S1 ships alone (review advisory A5)

**No deadlock remains that involves the claim.**
- The claim's provider lock is its last lock, and the tracker is asserted clean there, so the
  claim never waits on a row while it holds a provider (Rule P).
- Every row it waits on is taken before the global parallel key (Rule F).
- L3, L4 and L7, the provider->row writers that could meet it, are fixed in S1.
- L23 and L24 cannot meet it (S2 reasoning above).
- The supervisor's cross-connection cycle does not exist (Ground truth, L16).

**What remains are pre-existing consume-before-commit leaks** of the same C508 class:
- **L16 supervisor:** a failed `StartSupervisedRestartAsync` after `:507`.
- **L17 queue flush:** a failed flush after `:2578`.
- **L6 retained return:** `TryClaimCountedSlotAsync` refusing at `:636` after the redeem at
  `:633`, then `continue`.

Each leaves the wait `Admitted` without a receipt. `RearmStalledAdmissionsAsync`
(`CapacityRecoveryService.cs:1159-1207`) re-arms it after `StalledAdmissionTimeout` (2 minutes
by default), and the next attempt is prepared. The cost is that delay plus one spent admission
per leak: `AdmissionCount` stays incremented, so a wait leaks at most `MaxAttempts` times before
it reaches `Exhausted`. Recovery is automatic, and nothing deadlocks or loses work.

**Acceptable for rc: yes.** The leaks exist at the rc base and in the qualified candidate.
They concern standing restarts, queue flushes and retained returns, not the C508 dispatch path.
S1 strictly reduces leaks. S3 must still be filed as its own card before CARD-1593 closes.

## Verification design

All red witnesses are committed before the fix (git-common). "Red at base" means the test fails
on the S1 test commit, before the product commit, with the stated assertion. A build or fixture
error does not count. A "guard" passes at base, so it carries a named positive control (PC):
a method-scoped mutation of the S1 product code that must turn it red, after which the mutation
is restored.

| ID | Test (class/method) | Asserts | Red witness |
|---|---|---|---|
| V-1 | `AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection` | after the stale rollback: `AdmissionCount==0` and the grant is still `W`; the next fresh inspection dispatches with exactly 1 admission and `StartAccepted` bound to the session | red at base: `AdmissionCount` 1 (seen at `4dda25d52`) |
| V-2 | `CapacityAdmissionLifecycleTests/C1593_rollback_exit_keeps_reservation` `[Arguments("stale-sibling")]`, `("token-lost")`, `("lease-preflight")`, `("parallel-hold")` | grant `W` kept, `AdmissionCount==0`, wait `ActionPending` | red at base, each argument |
| V-3 | `CapacityAdmissionLifecycleTests/C1593_committed_non_launch_exit_releases_reservation` | pinned-Worktree `FailAsync` exit: wait `Canceled`/`claim-not-launched`, grant cleared, `AdmissionCount==0`, next wait grantable | red at base (`AdmissionCount` 1, state `Admitted`) |
| V-4 | `CapacityAdmissionLifecycleTests/C1593_preparation_restart_and_dispatch_consume_one_admission` `[Arguments("after-receipt", false)]`, `("after-receipt", true)`, `("queue-inserted", false)`, `("lost-wakeup", false)`, `("grant-expired-during-prep", false)` | the first four: preparation keeps the grant unspent; a restart in backoff keeps it; the launch spends once; the wait is `StartAccepted` with `LaunchSessionId`; one complete `UserPrompt` via `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync`. **c5 (review F4):** after the `HeldForRemotePrep` commit, advance `FakeTimeProvider` past `StalledAdmissionTimeout`, run the gate tick (expiry re-arm), advance past `DueAt`, run `GrantReadyAsync`, then the launch tick. Assert that `ActionOrdinal` moved by 1 and `ActionKey` changed, that the launch consumed once (`AdmissionCount==1`), and that the receipt is bound to the session. The test also records the preparation duration (D-10) | red at base: preparation consumes; c5 ends with `AdmissionCount` 2 (prep redeem + stalled re-arm + launch redeem) |
| V-5 | `CapacityAdmissionLifecycleTests/C1593_crash_after_launch_commit_consumes_one_admission` | the `LandDeliveryBoundary` at `dispatch-warning-claim-committed` throws (crash cut c3); a new provider recovers through `ResumeInterrupted`; `AdmissionCount==1`, wait `StartAccepted` with `LaunchSessionId==S`, one complete `UserPrompt` on S, no second session | red at base (receipt never written: `Admitted`, `LaunchSessionId` null) |
| V-6 | `CapacityAdmissionLifecycleTests/C1593_enqueue_failure_consumes_one_admission` | the launch sink throws; the claim and the `Starting` session are kept; `AdmissionCount==1`, receipt bound to S; after recovery, one `UserPrompt` | guard; PC: drop the receipt write from the consume -> red |
| V-7 | `CapacityAdmissionLifecycleTests/C1593_late_consume_refusal_keeps_tick_identities` | two queued tasks A and B in one tick. A test-only `LandDeliveryBoundary` point `capacity-consume-before` revokes A's grant on another connection, so A is `NotClaimed` and its tracked entry is still tracked with database values. B dispatches with its own single admission and `LaunchSessionId==B.AgentSessionId` | red at the round-2 tip `730c76a2` pattern; PC: replace `AbandonClaimAsync` with a bare `Clear()` -> red |
| V-8 | `CapacityLockOrderTests/C1593_path_blocked_on_task_holds_no_provider_lock` `[Arguments("claim")]`, `("claim-agent-row")`, `("queued-reroute")`, `("routing-resume")`, `("continuation")`, `("cancel")`, `("retry")` | a test connection holds a row `FOR UPDATE` (the task row; for `claim-agent-row` the pinned pool agent row of a cold relaunch). The path runs until its backend waits on that row (`pg_stat_activity.wait_event_type='Lock'` for its pid). At that moment `pg_locks` shows no granted advisory with an `antiphon.capacity.kind.*` key for that pid; for `claim-agent-row` it also shows no `antiphon.delegation.parallel-tasks` key (Rule F). The test then releases the row and the path completes. **`routing-resume` setup (review F4):** at least two routing-blocked tasks A and B in one pass, where B (the second) owns a capacity wait, so B's supersede has a kind to process and flushes A's edits; the test connection holds **A's** row. **`claim-agent-row` setup:** dispatch concurrency is configured (`_dispatchConcurrency` non-null), so `RefuseParallelAsync` runs | red at base: `queued-reroute`, `routing-resume` (only with the A/B setup above; one task alone passes at base), and `claim-agent-row` (the base flush `:6150` waits on the agent row while holding the parallel key). Guards: `claim`, `continuation`, `cancel`, `retry`. PCs: `claim-agent-row` -> consume before the flush (round 1 order) is red on the capacity-key assertion; `continuation` -> drop the S1 agent lock from `LockReleasedAnswerAsync` with the test holding the pool agent row is red |
| V-9 | `CapacityLockOrderTests/C1593_path_blocked_on_provider_already_holds_task` `[Arguments("claim-launch")]`, `("queued-reroute")`, `("continuation")` | a test connection holds the provider advisory; the path blocks on it; `SELECT ... FOR UPDATE NOWAIT` on the task from the test connection raises `55P03`; for `claim-launch` the change tracker of the claim context has no `Modified`/`Added`/`Deleted` `AgentTask`/`Agent`/`AgentSession` entry at that moment (Rule P assertion, review F1) | red at base: `claim-launch` (no provider lock at launch) and `queued-reroute` (no task lock first) |
| V-10 | `CapacityLockOrderTests/C1593_competing_claim_and_reroute_complete_without_deadlock` and `C1593_competing_claim_and_continuation_complete_without_deadlock` | two contexts are barrier-released at the lock step (boundary `capacity-consume-before` for the claim). Reroute case: the reroute targets the claimed task. Continuation case: claim task B is pinned to pool agent X (cold relaunch), and the continuation is for task A, which last ran on X, `Ephemeral`, with no park continuation, so `RemoveEphemeralAgentAsync` writes X. Both complete; no `40P01`; exactly one dispatch | guard at base (the base claim takes no provider lock, so neither case can cycle). PCs (review F4): reroute -> restore base `TryRewalkQueuedChainAsync` (`:1682-1686`, no owned tx) under the S1 claim and expect `40P01`; continuation -> drop the S1 agent lock in `LockReleasedAnswerAsync` and expect `40P01` |
| V-11 | `CapacityAdmissionLifecycleTests/C1593_warm_reuse_consumes_one_admission` | rollback variant (review F4). Tick 1: a warm pool agent matches and the parallel limit is full, so `HeldOnParallel` happens after the reuse selection. Free the slot; tick 2 reuses the warm session. Assert `AdmissionCount==1`, wait `StartAccepted`, `LaunchSessionId ==` the reused session id, and the agent `Running` | red at base: the tick-1 pre-claim redeem consumed and cleared the grant, so tick 2 holds on "not yet granted" (`Dispatched==0`) |
| V-12 | `CapacityAdmissionLifecycleTests/C1593_standing_placement_consumes_one_admission` | same rollback variant on a non-specialist standing agent with a live session (`PlaceOnStandingAgentAsync`). Assert `AdmissionCount==1`, `StartAccepted`, `LaunchSessionId ==` the standing session | red at base, as V-11 |
| V-13 | `CapacityAdmissionLifecycleTests/C1593_consume_failure_leaves_the_task_queued` `[Arguments("deadlock-victim")]`, `("lost-commit-ack")` | D-9. `deadlock-victim`: an interceptor throws `PostgresException` with SQLSTATE `40P01` on the consume save. Expect the task `Queued`, `FailureReason` null, no committed session row, the grant still `W`, `AdmissionCount==0`, one `capacity consume refused` Warning, and `Failures==0`; the next tick dispatches once. `lost-commit-ack`: a `DbTransactionInterceptor` throws from `TransactionCommittedAsync` (after the real commit). Expect the task `Dispatched`, `AdmissionCount==1`, wait `StartAccepted`/S, no `Failed`, a Warning, and interrupted-launch recovery relaunching S once | red at base: `deadlock-victim` (base has no consume in the claim; the injected failure hits the post-commit receipt and the task is `Dispatched`, not `Queued`); `lost-commit-ack` (the base catch `:6153` reloads and rethrows, so the generic catch `:1120` fails the task) |
| V-14 | `WallRerouteDispatchTests` rewritten pair (review F3): `Receipt_throw_after_launch_leaves_the_task_dispatched` (`:271`) becomes `Receipt_throw_in_the_claim_commit_leaves_the_task_queued`, and `Failed_receipt_cannot_resurrect_a_concurrently_superseded_wait` (`:300`) becomes `Failed_consume_cannot_resurrect_a_concurrently_superseded_wait` | `ThrowOnReceiptSave` (`:411`) is unchanged; it now fires on the consume save inside the claim. First test: tick 1 gives `fault.Throws==1`, `Dispatched==0`, `Failures==0`; the task is `Queued` with `AgentSessionId` null and `FailureReason` null; the wait is `ActionPending`, `AdmissionCount==0`, and the grant still `W`. Tick 2 (fault spent) gives `Dispatched==1`, `AdmissionCount==1`, `StartAccepted`, and `LaunchSessionId ==` the task's session. Second test: after tick 1 the claim context has no open transaction; a concurrent supersede ends the wait `Superseded`; a later unrelated event save on the same dispatcher context leaves `State`/`Outcome`/`OutcomeReason`/`Version` unchanged and `SessionId`/`LaunchSessionId`/`DispatchAttemptId` null; the task stays `Queued`; `fault.Throws==1` | red at base: both assert `Dispatched==0` after tick 1, and base dispatches (`Dispatched==1`) |
| R-1 | `WallRerouteDispatchTests` (other 11 methods), `CapacityRecoveryTaskTests`, `CapacityRecoveryAcceptanceTests` (whole classes) and `AgentTaskDispatchBaseGuardTests/C508_GuardRefMismatchWarnedOnce` (4 args) | unchanged behaviour. `CapacityRecoveryAcceptanceTests` calls `RedeemAsync` directly, and the wrapper keeps `Admitted` semantics (D-2). `Redeemed_dispatch_receipts_the_wait_and_the_transcript_progresses_it` keeps its expectations (`StartAccepted`, `AdmissionCount==1`), because the receipt now comes from the claim. If any other R-1 assertion encodes the pre-claim consume, Code stops and reports it as a plan amendment rather than editing the test | - |

Deterministic means no sleeps: waits poll `pg_stat_activity`/`pg_locks` for the path's backend
pid, with a 30 s deadline. The advisory key is matched by `classid`/`objid`, computed from
`hashtext(key)` on the same server. Lifecycle tests advance a `FakeTimeProvider`, not wall time.

### Red witnesses (run before the product commit, not in the table)

One build-slot build of the S1 test commit, then these `--no-build` filters. Each must show the
named assertion failure; zero tests or a fixture error is not red.

- `/*/*/AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection`
- `/*/*/CapacityAdmissionLifecycleTests/(C1593_rollback_exit_keeps_reservation*)|(C1593_committed_non_launch_exit_releases_reservation*)|(C1593_preparation_restart_and_dispatch_consume_one_admission*)|(C1593_crash_after_launch_commit_consumes_one_admission*)|(C1593_warm_reuse_consumes_one_admission*)|(C1593_standing_placement_consumes_one_admission*)|(C1593_consume_failure_leaves_the_task_queued*)`
- `/*/*/CapacityLockOrderTests/(C1593_path_blocked_on_task_holds_no_provider_lock*)|(C1593_path_blocked_on_provider_already_holds_task*)`. Expected: the red arguments named in V-8/V-9 fail and the guard arguments pass.
- `/*/*/WallRerouteDispatchTests/(Receipt_throw_in_the_claim_commit_leaves_the_task_queued*)|(Failed_consume_cannot_resurrect_a_concurrently_superseded_wait*)`

The PCs for V-6, V-7, V-8 (`claim-agent-row`, `continuation`) and V-10 (both cases) are each
method-scoped, run once, and restored. The V-10 PCs pass only when the run shows `40P01` in
the TRX output.

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
- **Budgets:** 29 new results (17 lifecycle + 12 lock-order), plus the 2 rewritten
  `WallRerouteDispatchTests` methods inside CP-3.
  - p95 per method is estimated at 12 s, so the method timeout is 30 s, and so is the lock-wait
    deadline inside a test. Both are provisional until Code records p95 from its TRX.
  - V-4 `grant-expired-during-prep` runs on `FakeTimeProvider`, so the 2-minute expiry costs
    no wall time. The real server2 preparation p95 that D-10 asks for comes from server2 task
    history, not from this test: the gap between the `HeldForRemotePrep` Held event
    (`DispatchHoldDetails.RemoteMirrorRequested`, `AgentTaskDispatcher.cs:1014-1015`) and the
    `Dispatched` event. Code reports both.
  - Row budgets: CP-1 about 15 min including build, CP-2 about 3 min, CP-3 about 10 min. The
    whole round stays under 30 min of test time. A row over 2x its
  estimate is a Debug item, not a timeout raise.

### Checkpoints

Lane: Linux, server2 (runner default `globalRunnerId=server2`). No `-Runner` or `-Platform` pin.
Build once, then use `--no-build` (operator direction).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1593/` | c1593-new | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityLockOrderTests*)/*` | V-2..V-13 | all listed, 0 failed/skipped (17 lifecycle + 12 lock-order results) | 29 | 15 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S1 | CP-1 | c508-guard | `/*/*/AgentTaskDispatchBaseGuardTests/(Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*)\|(C508_GuardRefMismatchWarnedOnce*)` | V-1, R-1 | all listed, 0 failed/skipped (1 + 4 args) | 5 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S1 | CP-1 | capacity-regress | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(CapacityRecoveryTaskTests*)\|(CapacityRecoveryAcceptanceTests*)/*` | V-14, R-1 | >= 36 executed (WallReroute 15 incl. the 2 rewritten, Acceptance 15, Task 6), 0 failed/skipped | 36 | 10 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Suffix check at base: `CapacityAdmissionLifecycleTests*`, `CapacityLockOrderTests*`,
`WallRerouteDispatchTests*`, `CapacityRecoveryTaskTests*` and `CapacityRecoveryAcceptanceTests*`
each match exactly one class (`WallRerouteFixture`/`WallRerouteHarness` and the other
`CapacityRecovery*` classes do not match). Code verifies each intended class in the fresh TRX.
`Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*` and
`C508_GuardRefMismatchWarnedOnce*` have no sibling method prefixes in that class. The new
method names in the red-witness filters (`C1593_*`, `Receipt_throw_in_the_claim_commit_leaves_the_task_queued`,
`Failed_consume_cannot_resurrect_a_concurrently_superseded_wait`) are not prefixes of any other
method in their class. Code re-checks this after it writes the classes.

Not run: the whole Unit lane (25 min timeout, operator direction), native Windows C508 E2E
(D-7), and the checkpoint tool's sealed receipt (64 MiB reader cap, see Ground truth). Code
and Final Review verify as D-11 says: one build-slot build, `--no-build` CP rows, and one
CHECKPOINT-shaped line per row with TRX path, SHA-256 and counts. Final Review re-runs the rows
itself and reports `reviewedSourceClean: false` for the reader cap.

### Landing

Cut the branch from `origin/rc/2026-10-27`. Before Review, run `git fetch` and
`git merge-tree --write-tree` against both `origin/rc/2026-10-27` and `origin/master`. A master
merge carries this plan doc; per the rc cherry-pick notes, drop plan docs from the rc pick if
rc lacks `docs/superpowers/plans`.
Run `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f84228f285dd3e4ad7b5dc8260cbc9f819256f50 -HeadRef <tip>`.

## Review fe4577c5 response

Every reference is at rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.

**F1 (Rule P broken by the S1 claim). Accepted, with one part refuted.**
- *Fix:*
  - The claim writes Agents rows after the planned consume point. Confirmed at
    `AgentTaskDispatcher.cs:6003-6011`, `:6102-6104` and `:7457`, all flushed at `:6150`, and at
    warm reuse `:8231-8234`, flushed at `:5706`.
  - D-2 now places the consume after the flush (`:6150`/`:6151`, `:5706`/`:5707`) and asserts
    a clean tracker.
  - The claim locks its existing agent and reused session rows (ranks 4-5) before the parallel
    key (Rule F).
  - L1 is split into L1/L1w/L1s with base and S1 sequences.
  - L7 is corrected: `RemoveEphemeralAgentAsync` `:3881` -> `:3997/:4004` runs under the
    provider lock. S1 moves the agent lock into `LockReleasedAnswerAsync`.
  - New witnesses: V-8 `claim-agent-row` (red at base on the parallel-key assertion; PC is the
    round-1 consume-before-flush order) and the V-9 `claim-launch` tracker assertion.
- *Refuted:* the supervisor cross-connection cycle. `AgentSupervisorService.cs:226-227` commits
  (`CompleteAsync` `:349-356`) and disposes the Herdr transaction before
  `TryHandleCapacityWaitAsync` `:230` reaches `RedeemAsync` `:507`, as the comment at `:206-208`
  intends. No agent row is held while the supervisor waits for the provider. L16 therefore
  stays in S3 as a lifecycle leak. Round 1 of this plan made the same mistake in its Ground
  truth row, which is now corrected.

**F2 (missing site and wrong fence order). Accepted.**
- L23 is the quota-blocked settlement `AgentTaskReplyService.cs:1970-1991`. It is assigned to
  S2, with the reasoning in Slices: it cannot meet the S1 claim, and its only partner (L7) is a
  detectable, pre-existing cycle.
- L24 records the fence as session->task (`AppDbContext.cs:163-164`), with an S2 swap; it is
  removed from L20.
- The re-grep (`FOR UPDATE|FOR NO KEY UPDATE|FOR SHARE|FOR KEY SHARE|pg_advisory*` over
  `server/**/*.cs`, 138 hits) added L25 (session-row lockers) and the missing L20-L22 sites.
  It also recorded five comment-only hits.
- The new rank 4 (physical agent) also fixes the round-1 table, which had no rank for it.

**F3 (R-1 "unchanged" was false). Accepted.**
- The two receipt tests (`WallRerouteDispatchTests.cs:271/:300`) are rewritten as V-14, with
  the new expectations: tick 1 leaves the task `Queued` with its reservation, and tick 2
  dispatches once.
- D-9 defines the consume and receipt failure contract. Abandon and stay `Queued`; never reach
  the generic catch (`:1120-1130`) that fails the task. A lost commit acknowledgement is
  reread durably and treated as c4.
- CP-3's count is updated to 36.

**F4 (missing or non-red witnesses). Accepted.**
- V-11 (warm reuse) and V-12 (standing placement) are the rollback variants, red at base.
- V-4 gains `grant-expired-during-prep` (c5), red at base with `AdmissionCount` 2.
- V-10 is a guard with named PCs that expect `40P01`.
- V-8 `routing-resume` states its two-task setup; it is red at base only with that setup.

**Advisories:**
- A1: D-4 is built from `FinishParallelHoldAsync` and reloads tasks, agents and sessions.
- A2: D-10.
- A3: deferred to S3.
- A4: the c3/c4 reasoning is unchanged; c6 (lost acknowledgement) is added, and warm reuse and
  standing placement are now witnessed.
- A5: Residual risk section.
- A6: D-11.
