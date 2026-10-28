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

**Round 3 (task d94003bc).** Amended for design review f703a93e
(`.antiphon/task-f703a93e.md` at `68721c8e`). That review confirmed the supervisor refutation
(L16 has no hidden cycle). What changed:
- Sessions and releases now rank before the physical agent, matching CARD-1190. The pre-lock
  orders for warm reuse and continuation change with it, and L20, L21 and L25 are corrected.
- Grant expiry is named as reconciler-only. c5, D-9, D-10 and V-4 are rewritten to match.
- D-9 now separates refusals and transient errors (the task stays `Queued`) from deterministic
  failures (the task fails and the reservation is released).
- The V-8 and V-10 positive controls are fixed, and new witnesses are added.
- `Cards` gets rank 0.

See [Review f703a93e response](#review-f703a93e-response).

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
| The physical agent row ranks before sessions (round 2 rank 4) | **No (review f703a93e F1).** The code deliberately locks the session first (CARD-1190):<br>- `AgentControlService.cs:546-549`, whose comment says "Sessions before the agent ... the agent lock first deadlocks it (40P01, CARD-1190)".<br>- Stop at `AgentControlService.cs:1276-1283`, and the same shape at `AgentService.cs:942-950` and `:1011-1017`.<br>- `AgentSessionService.cs:410-417`: "Session before agent, matching CloseSessionOnExitAsync".<br>- The exit observer: session at `AgentSessionRuntime.cs:213`, agent write at `:276`, flushed at `:331`.<br>- `TerminalRunnerSeatReleaseService.cs:872-878` (task, release, session), then the agent `ExecuteUpdate` at `:956`. |
| An expired grant is refused at redemption (round 2 c5/D-9) | **No (review f703a93e F2).** `RedeemAsync` (`CapacityRecoveryService.cs:237-301`) checks only the grant id and key (`:259`), `NextAdmissionAt` (`:261`) and the refusal reason. Only the reconciler enforces expiry: `ReconcileAsync` `:47-55` -> `GrantReadyAsync` `:148-158` -> `StillHoldsGrant` `:996-1000` (`GrantExpired` `:1015-1017`) -> `RecordRevokedGrantAsync` `:1019-1027` -> `RearmRevokedGrantOnAsync` `:1042-1062`. Grant order then uses `DueAt` for `grant-expired` waits (`:177-179`), so other waiters can be granted first. |
| The existing claim catch covers a consume failure | **No.** The catch at `AgentTaskDispatcher.cs:6153` is filtered to `Workspace == Worktree && SourceLandingOperationId is null`. Warm reuse and standing placement commit at `:5705-5706` with no catch. Anything else reaches the tick's generic catch (`:1120-1130`, `FailAndNotifyAsync`). |
| The claim flush touches only ranked rows | **No (review f703a93e advisory).** The new `AgentSessions` row's FKs (`AppDbContextModelSnapshot.cs:10386-10400`: `CardId` `:10389`, `WorktreeId` `:10399`) take `KEY SHARE` on `Cards` and `Worktrees` rows after rank 6. `Cards` rows are locked `FOR UPDATE` at `PipelineHandoffActionService.cs:33/:159`, `CardCompletionAcceptanceService.cs:94`, `CardWorktreeCleanupExecutor.cs:80` and `CardWorktreeMirrorCleanupService.cs:100`. Each of those then locks only non-`Queued` tasks, or none. `AgentTaskService.cs:1579` takes `FOR NO KEY UPDATE`, which does not conflict with `KEY SHARE`. |
| A completed remote preparation survives a regrant | **Yes.** The preparer records `RemoteWorktreePath` on the task, and `PrepareRemoteWorkspaceAsync` returns that path without asking for a new mirror (`AgentTaskDispatcher.cs:7311-7312`). Only `RemoteWorktreePath is null` reaches the `HeldForRemotePrep` commit (`:5978-5986`). Nothing there reads the grant. |
| Checkpoint tool can seal the test DLL | No at base: `scripts/lib/checkpoint-file-reader.ps1:196` caps at 67,108,864 bytes and `Antiphon.Tests.dll` is about 69 MB. As the operator directed, Code runs the table through `scripts/build-slot.ps1` (one build, then `--no-build`). |

## Decisions

**D-1 Canonical global lock order (task before provider).** Every transaction acquires locks
in this rank order and never acquires a lower rank after a higher one:

| Rank | Resource | Key |
|---|---|---|
| 0 | `Cards` row (`FOR UPDATE`; `FOR NO KEY UPDATE` at task create). Card-owned rows (`PipelineHandoffActions`, `CardWorkflowRuns`, `CardWorktreeCleanupEndpoints`) are taken only under the card lock, before or after a non-`Queued` task (`PipelineHandoffActionService.cs:33-34` -> `:55`; `AgentTaskService.cs:1579` -> `:1585`). No claim and no rank-6/7 holder takes them, so they carry no rank of their own | card id |
| 1 | counted-task lane advisory | `hashtext('antiphon.capacity.counted-tasks')` |
| 2 | standing-specialist owner `Agents` row (`FOR UPDATE` / `FOR NO KEY UPDATE`) | owner id |
| 3 | `AgentTasks` row (`FOR UPDATE`, or any EF/SQL write of the row); in a claim the claimed task first, then (warm reuse) the previous token owner, a non-`Queued` task no other claim takes as its own | task id |
| 4 | `RunnerSeatReleases`, then `AgentSessions`, then `SessionQueuedMessages` rows, including FK `KEY SHARE` on a referenced existing session | row id |
| 5 | physical (launch-target / pool) `Agents` row the task runs or ran on | agent id |
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

Round 2 added the physical agent row as rank 4. Round 3 (review f703a93e F1) places it
**after** sessions and releases, as rank 5. The code chose session-before-agent on purpose in
CARD-1190, and every stop, exit-observer, launch-failure and seat-release path follows it (see
Ground truth). Rank 0 (`Cards`) is new. Rows L1-L25 below use the round-3 numbers.

**Exception E-1 (pre-existing, detectable).** For a standing specialist, the stop and delete
paths lock the session and then the owner row (`AgentControlService.cs:1276-1283`,
`AgentService.cs:942-950`/`:1011-1017`, `ownerId = StandingSpecialistOwnerId ?? agent.Id`).
That is 4->2. For a non-specialist the owner is the agent itself, so the order is 4->5 and
conforms. E-1 pairs with the specialist claim L1s (owner 2, then the live session `KEY SHARE`
4) in a cycle on one database graph, so PostgreSQL detects it as `40P01`, and the cycle is
present at base. S1 moves the claim's side into the pre-locks, where D-9 abandons the claim
instead of failing it. The S2 ledger allowlists E-1 by call site.

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
  - *FK `KEY SHARE` exemption (round 3):* the new session row's FK checks take `KEY SHARE`
    on its `Cards` (rank 0) and `Worktrees` rows after rank 6. These can wait briefly on a
    `Cards FOR UPDATE` holder. Every such holder (`PipelineHandoffActionService.cs:33/:159`,
    `CardCompletionAcceptanceService.cs:94`, `CardWorktreeCleanupExecutor.cs:80`,
    `CardWorktreeMirrorCleanupService.cs:100`) then locks only non-`Queued` tasks or none, and
    no holder takes rank 6 or 7, so no cycle can form. No code locks `Worktrees` rows at base.
    The exemption is FK `KEY SHARE` on rank-0 and `Worktrees` rows from an insert. The S2
    ledger allows exactly that and nothing else.

*Rejected:* provider-first, Round 2's order. It needs the wait kinds before the task lock,
then a speculative pre-lock and a rollback when the kind changes. It would also have to change
every `AgentTaskService` path that already locks the task first: continuation, requeue, retry,
cancel. Round 2 F1 shows how easy one of those is to miss. Task-first keeps the provider a leaf
and changes only the paths listed in S1.

**D-2 Admission lifecycle: reserve, consume, release.**
- *Reserve:* `GrantReadyAsync` sets `ProviderState.GrantedWaitId = W` (wait `ActionPending`). It is
  durable and reusable: any number of ticks, preparation commits and restarts observe the same
  grant, and `StillHoldsGrant`/`GrantExpired` is its only bound, and only the reconciler enforces it (D-12).
- *Consume (amended for review F1):* `RedeemOnAsync(db, ..., consume: true)` runs only inside
  a claim transaction, as the claim's **last lock**, after the flush. The three launch shapes set
  `Status = Dispatched` at cold launch `:6100`, warm reuse `:8247` and standing placement `:8502`.
  They reach the consume at two physical points:
  - Cold launch: between the `SaveChangesAsync` at `:6150` and `CommitAsync` at `:6151`.
  - Warm reuse and standing placement: both return `ReuseOutcome.Reused` to `:5705`, so the
    consume runs between the `SaveChangesAsync` at `:5706` and `CommitAsync` at `:5707`.

  Before `RefuseParallelAsync`, each shape takes its row locks in rank order (Rule F; round 3
  ranks):
  - Cold launch onto an existing pinned or pool row (not `freshAgent`): agent
    `SELECT 1 ... FOR UPDATE` (rank 5) just before `RefuseParallelAsync` at `:6098`. The lock
    is raw SQL, so the in-memory edits from `:6003-6011` and `:7457` survive. Cold launch locks
    and writes no existing session (it inserts a new one, `:6102`), so only the rank of its
    agent lock changes.
  - Warm reuse (amended for review f703a93e F1), in this order:
    1. The previous-token-owner task (rank 3, `:8244`), moved ahead of the agent edits.
    2. The reused session `S = X.PersistentSessionId` (read unlocked from the pick at
       `:8097`/`:8203`) `FOR KEY SHARE` (rank 4).
    3. The agent X `FOR UPDATE` (rank 5), then a reload and a recheck of `Status == Idle &&
       PoolIdleSince != null && PersistentSessionId == S`.

    Only after these does the code mutate `:8231-8234`. If the recheck fails, the code returns
    the outcome base gives for that state: `WaitForAgent` for a pin, `SpawnFresh` for a pool
    pick. A concurrent stop of X (session S `FOR UPDATE`, then X; `AgentControlService.cs:1276-1283`)
    now meets the claim in the same order. If the stop holds S, the claim waits on S holding
    neither X nor rank 6. If the claim holds S, the stop waits on S. Either way there is no cycle.
  - Standing placement: the live session `FOR KEY SHARE` (rank 4) before `RefuseParallelAsync`
    at `:8482`. It writes no agent row. For a specialist this pairs with E-1, which is
    detectable.

  The full claim order becomes 1 -> 2 -> 3 -> (3) -> 4 -> 5 -> 6 -> flush -> 7 -> 8. A
  `40P01`/`55P03` raised by any of these pre-lock statements is handled by D-9 (abandon,
  `NotClaimed`), never by the generic catch.
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
nothing re-admits. (c4) Enqueue refusal (`:6198-6211`) is c3 without the crash.

(c5) Grant expiry during preparation (rewritten for review f703a93e F2). The default
`StalledAdmissionTimeout` is 2 x 60 s (`CapacityRecoveryPolicy.cs:87-90`). Only the reconciler
enforces expiry. `RedeemAsync`/`RedeemOnAsync` never check it (D-12). While the preparer is in
flight, the tick holds the task before the gate (`AgentTaskDispatcher.cs:1261`). There are two
orders, and both serialize on the provider lock:
- **(c5a) Reconciler first.** `ReconcileAsync` (`CapacityRecoveryService.cs:47-55`) ->
  `GrantReadyAsync` (`:148-158`) finds `StillHoldsGrant` false (`GrantExpired`,
  `:996-1000`/`:1015-1017`) -> `RecordRevokedGrantAsync` (`:1019-1027`) ->
  `RearmRevokedGrantOnAsync` (`:1042-1062`).
  - That re-arm moves `ActionOrdinal`/`ActionKey` through `PrepareNextAttemptOnAsync`, sets
    `DueAt = now + StalledAdmissionTimeout`, clears the grant, and leaves `AdmissionCount`
    unchanged. (`AttemptWouldExhaust` reads `AdmissionCount` only.)
  - The same `GrantReadyAsync` pass, or a later one, may now grant the provider to **another**
    waiter. Grant order uses `DueAt` for a `grant-expired` wait (`:177-179`).
  - W's post-prep tick gets `grant-mismatch` at the gate and the task stays held. Once W is
    granted again (after `DueAt` and after any earlier-ordered waiters), the next tick finds
    `RemoteWorktreePath` recorded (`AgentTaskDispatcher.cs:7311-7312`), so it requests no new
    preparation, and the launch consumes once.
- **(c5b) Tick first.** The gate passes (the grant id and key still match). The claim consumes
  the expired grant under the provider lock, which counts as one admission. If the reconciler
  takes the provider lock between the gate and the consume, the consume sees `grant-mismatch`
  and refuses. D-9 then abandons the claim (`NotClaimed`, task `Queued`), and c5a follows.

Either way c5 ends with `AdmissionCount == 1` and the receipt bound to the launched session.
No livelock: a completed preparation is durable on the task and does not depend on the grant,
so each regrant after it launches. (c6) Lost commit acknowledgement: see D-9; the durable
reread makes it c3.

**D-12 Expiry stays reconciler-only (review f703a93e F2).** `RedeemOnAsync(consume:true)` does
**not** refuse a grant that has expired but is not yet revoked.
- *Why it is safe:* revocation (`GrantReadyAsync`) and the consume both run under the same
  provider lock (rank 7). A grant whose id and key still match under that lock has been handed
  to no other waiter, so spending it cannot double-admit.
- *Rejected: an expiry check in the consume.* The smallest form is one
  `if (GrantExpired(state, now)) return Refused("grant-expired")` after the key check at
  `:259`. It would refuse a grant still reserved for W and so add one full re-arm-and-regrant
  cycle, for no safety gain. It would also give D-9 a fourth refusal class whose only effect
  is delay.
- So "grant expired" is not a consume refusal. V-4 c5 drives the reconciler explicitly.

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
conform (review F1). S1 adds its rank-5 lock. `LockReleasedAnswerAsync`
(`AgentTaskService.cs:3632-3643`) gains the physical agent row `FOR UPDATE` **after** the
session (`:3640`) whenever `task.AgentId` is set (round 3, review f703a93e F1). The sequence
becomes task A (`:3634`) -> release R (`:3637`) -> session S (`:3640`) -> agent X. That is the
`TerminalRunnerSeatReleaseService.cs:872-878` -> `:956` order and the stop path's
session-then-agent order, so a concurrent stop of X no longer forms a cycle with the
continuation. A changed `AgentId` after the reload at `:3767` is the existing
`answer_revision_changed` conflict. Then the later
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

**D-9 Consume and receipt failure contract (review fe4577c5 F3; rewritten for review f703a93e
F1, F3 and the catch advisory).**

*Scope.* A new **unfiltered** `try` in each launch shape covers three steps: the S1 pre-locks
(D-2: agent, session `KEY SHARE`, previous-owner task), `RedeemOnAsync(consume:true)` with its
capacity-only save, and `CommitAsync`. The existing catch at `:6153` is filtered to
`Worktree && SourceLandingOperationId is null`, and warm reuse/standing placement at
`:5705-5706` have no catch, so neither is relied on.

The flush between the pre-locks and the consume keeps base behaviour. A `SaveChangesAsync`
failure at `:6150`/`:5706` is a data error on rows the claim wrote, and the task fails as today
through `:6153` or the generic catch (`:1120-1130`). Nothing has been consumed. Code confirms
that `OrphanedWaits` (`CapacityRecoveryService.cs:445-457`) cancels a terminal task's
reservation, and adds the clause if it does not.

*Classification.* Every failure in the scope is one of four classes:
1. **Refusal from the consume** (no exception). The causes are `grant-mismatch`,
   `missing-grant`, a wait that is no longer `ActionPending`, `exhausted`, `clock-not-due` and
   `feature-disabled`. In the claim, `RedeemOnAsync(consume:true)` writes nothing for any of
   them, not even the `Exhausted`/`Deferred` rows `RedeemAsync` writes at `:270-280` and `:258`/`:262`.
   The next tick's `consume:false` gate records the durable refusal on its own scope.
   - *Outcome:* `AbandonClaimAsync`, task stays `Queued`, `NotClaimed`. "Grant expired" is not
     a refusal (D-12).
2. **Transient database error.** Any exception whose chain holds a `PostgresException` with
   SQLSTATE `40P01` (deadlock victim, including in a pre-lock), `40001` (serialization) or
   `55P03` (lock not available).
   - *Outcome:* `AbandonClaimAsync`. The task stays `Queued` with `AgentSessionId`/`AgentId`
     unchanged in the database. No session row exists, and the reservation is intact.
     `NotClaimed`.
   - *Why it is bounded:* these need a concurrent partner and cannot repeat by themselves.
3. **Exception from `CommitAsync` itself**, where the acknowledgement may be lost.
   - After the helper reloads, reread the task. If it is `Dispatched` with
     `AgentSessionId == S`, the claim committed. Treat it as c4 (enqueue refused): record the
     Warning, leave the `Starting` session to interrupted-launch recovery
     (`SessionReconciliationService.cs:115/:342`, `AgentSessionLaunchQueue.cs:67`), and count
     the task as dispatched.
   - If the reread sees `Queued`, the server may still apply the `COMMIT` later (review
     f703a93e priority 4). That is safe: the task then ends `Dispatched`/`Starting` with no
     enqueue, interrupted-launch recovery launches it, and the next claim's `Queued` filter
     (`:5543-5545`) refuses it. So a `Queued` reread is handled as class 2.
   - `AbandonClaimAsync` tolerates a throwing rollback after a failed commit (it catches and
     logs, then disposes).
4. **Any other exception** before the commit is deterministic and would repeat on every
   regrant. Examples are the Rule P `InvalidOperationException`, a non-transient receipt-save
   `DbUpdateException`, or a bug in a pre-lock. Because expiry never increments
   `AdmissionCount`, keeping the task `Queued` would retry the whole claim forever (review
   f703a93e F3). The outcome:
   1. `AbandonClaimAsync`.
   2. Then, in its own short transaction (a 7->8 leaf that holds no task lock),
      `ReleaseClaimReservationOnAsync(db, taskId, kind, "claim-consume-failed")`, which cancels
      the wait and clears the grant if it is still held.
   3. Then rethrow, so the generic catch (`:1120-1130`) fails the task through
      `FailAndNotifyAsync`, as base does for the same bug.

   If the release itself throws, the task still fails, and `CancelOrphanedWaitsAsync` is the
   backstop. *Rejected: a per-wait consecutive-failure cap.* It needs a new column and a
   migration in an rc slice, and it still burns several full claims before failing.

Classes 1-3 save one deduped Warning event, `capacity consume refused: <reason or SQLSTATE>`,
after the rollback in its own save. `OperationCanceledException` while `ct` is cancelled:
abandon, then rethrow.

This removes the base reason for the swallow at `:1058-1068`. That block is deleted together
with the receipt step.

**D-10 Grant held through slow claim work (review advisory A2; corrected for review f703a93e
F2).**
- *What changes:* the reservation now survives `git worktree add` inside the claim and the
  whole remote preparation (`HeldForRemotePrep`). Mirror preparation on server2 often outlasts
  the 2-minute expiry.
- *What a long preparation costs:* when it does, the reconciler revokes and re-arms W (c5a)
  and may **grant the provider to other waiters while W is still preparing**. The round-2
  claim of "one launch per preparation window" was wrong. The provider keeps admitting other
  waiters at its normal admission interval.
- *What W loses:* W's slot. It waits for `DueAt` (`now + StalledAdmissionTimeout`) and for any
  waiters ordered ahead of it, so one or more extra grant cycles (about 2-4 minutes on
  defaults). It spends no attempt, and the finished preparation is reused (`:7311-7312`).
- *Scope:* this applies only to tasks that have a capacity wait; tasks without one are ungated.
- *Accepted for rc,* because base already loses the admission *and* an attempt in the same
  window (C508).
- *Follow-up:* V-4 records the c5 sequence. Code reports the server2 preparation p95 from task
  history (Performance budget). A later card may extend the grant on a `HeldForRemotePrep`
  commit.

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

Round 3 (task d94003bc) hands off to Review again. The brief says to return `next: code` only
for "wording/rank detail the reviewer already specified". Most of round 3 is exactly that:
the rank swap, L7 A->R->S->X, warm reuse S-then-X, the V-4 c5 sequence, the V-10 boundary,
and `Cards` as rank 0. Three parts go beyond it:
- D-9 is now a four-class failure contract, using the reviewer's fail-and-release option.
- D-12 records an explicit expiry decision.
- The guard arguments of V-8 gain positive controls that earlier rounds lacked.

A Review of only the round-3 delta is enough.

## Global lock-acquisition table (every site; rank sequence per transaction)

"Leaf" means the transaction holds ranks 7-8 only. "n/c" means no capacity lock is ever held,
so only the rank 0-5 order matters and any inversion there is a same-connection cycle that
PostgreSQL detects (`40P01`). The rank numbers are round 3's (D-1: sessions 4 before the physical agent 5). "Base sequence" is what the
code at `f84228f28` does; "S1 sequence" is given where S1 changes it.

| # | Path | Site(s) | Base sequence | S1 sequence / conformance | Slice |
|---|---|---|---|---|---|
| L1 | Dispatch claim, cold launch | `AgentTaskDispatcher.cs:5530` tx; `:5535` counted (1); `:5539` owner agent (2); `:5543` task (3); `ResolveAgentAsync` `:7447` pinned agent read, unlocked; `:6098` parallel (6); flush `:6150` writes agent (5, unlocked until now), inserts session (new; FK `KEY SHARE` on `Cards`/`Worktrees`, Rule F exemption) and events/parks (child) | 1->2->3->6->[5 at flush] | 1->2->3->**5** (explicit, existing agent only)->6->flush->**7** consume->**8** receipt; tracker clean at 7; D-9 `try` from the pre-lock to the commit | S1 |
| L1w | Dispatch claim, warm reuse | as L1 to `:5543`; `:8097`/`:8203` agent pick, unlocked; edits `:8231-8234`; `:8244` -> `:7978` previous token owner task (3); `:8245` parallel (6); flush `:5706` writes agent (5) and FK `KEY SHARE` on the reused session (4) | 1->2->3->(3)->6->[5,4 at flush] | 1->2->3->(3)->**4** (S `FOR KEY SHARE`)->**5** (X `FOR UPDATE`, reload, recheck `Idle`/`PoolIdleSince`/`PersistentSessionId == S`)->6->flush->**7**->**8**; meets a stop of X (4->5) in the same order | S1 |
| L1s | Dispatch claim, standing placement | as L1 to `:5543`; `PlaceOnStandingAgentAsync` `:8400` (owner row reloaded `:8403`); `:8482` parallel (6); flush `:5706` (task, event; FK `KEY SHARE` on the live session, 4) | 1->2->3->6->[4 at flush] | 1->2->3->**4** (`FOR KEY SHARE`)->6->flush->**7**->**8**; specialist owner (2) vs stop's session-then-owner is E-1 (detectable, at base; a claim victim is D-9 class 2) | S1 |
| L2 | Pre-claim redeem (gate) | `:959` -> `:1764` -> `CapacityRecoveryService.cs:248` own scope | 7->8 leaf, but **consumes** | 7->8 leaf, `consume:false` (refusals and expiry re-arm only) | S1 |
| L3 | Queued reroute | `AgentTaskDispatcher.cs:1682` kind edit; `:1685` -> `CapacityRecoveryService.cs:347`, flush `:372` | 7->3 (implicit) **no** | owned tx: 3 (`FOR UPDATE`, recheck)->7->8, then save of the held task (Rule P) | S1 |
| L4 | Routing-blocked resume | `AgentTaskDispatcher.cs:1826` -> `CapacityRecoveryService.cs:347`; edits `:1842-1846` saved at loop end | 7->3(other) **no** | one tx per task: 3->7->8 | S1 |
| L5 | Post-dispatch receipt | `AgentTaskDispatcher.cs:1054` -> `CapacityRecoveryService.cs:405` | 7->8 (+ flush of dirty tick rows) crash window | removed (receipt in the claim commit) | S1 |
| L6 | Retained return | `AgentTaskDispatcher.cs:633` -> `:1757` -> `RedeemAsync`; `:636` counted (no tx) | 7 leaf, consume before the counted slot and separate save | unchanged (`consume:true` kept) | S3 |
| L7 | Released-seat continuation | `AgentTaskService.cs:3764` tx; `:3634/:3637/:3640` task, release, session (3,4,4); `:3845` provider (7); `:3881` -> `:3997`/`:4004` pool agent write (5) | 3->4->4->7->[5] **no** (Rule P; at base a writer of X under the provider) | 3->4->4->**5** (agent `FOR UPDATE` after the session in `LockReleasedAnswerAsync`; A->R->S->X as TRSR `:872-878`->`:956`)->7->8; later writes are of held rows | S1 |
| L8 | Released-seat answer accept | `AgentTaskService.cs:3576` -> `:3632` | 3->4->4 | 3->4->4->5 (shares the S1 helper) | S1 (by sharing) |
| L9 | Retry/escalate requeue | `RequeueAsync` `AgentTaskService.cs:3792` (in-process gate) -> `RequeueCoreAsync` `:3845` (owned per-kind tx) | 7 then task and agent saved outside | conforms while nothing is dirty at `:3845` (V-8 `retry` guard) | guard |
| L10 | Cancel | `AgentTaskService.cs:2949` | 7 (owned), task and agent saved after | conforms (V-8 `cancel` guard) | S2 hardening |
| L11 | Released-seat recovery tick | `AgentTaskDispatcher.cs:5358` grok-rules advisory (own tx); `:5395/:5398` task, session (3,5); `:5337` -> `RetryAsync` (= L9) | 3->5 | n/c | - |
| L12 | Readiness/held trace | `AgentTaskDispatcher.cs:1366` task (3) | 3 | n/c | - |
| L13 | Brief recovery hold / absent launch | `:2839`, `:3650` task (3) | 3 | n/c | - |
| L14 | Task create | `AgentTaskService.cs:1577-1579` card (0, `FOR NO KEY UPDATE`), `:1585` handoff action (card-owned), `:1683` interim latch -> `InterimVerificationPolicy.cs:161-168` task (3); `:1897` host-budget advisory; `:4818` `EnsureWaitOnAsync` (no provider lock) | 0->(card-owned)->3 | n/c | - |
| L15 | API-error wall | `ApiErrorRecoveryService.cs:102` tx -> `:543` supersede (7), `:571` ensure, `:588` bump wave `CapacityRecoveryService.cs:647` (7, same kind) | 7 only (holds/recovery rows) | conforms | - |
| L16 | Supervisor (standing-agent restart) | `AgentSupervisorService.cs:127` tx; `HerdrSupervisionStateService.cs:26` agent (5; 2 for a specialist owner), session read only (`:41`); commit and dispose `:226-227`; then `:230` -> `:507` `RedeemAsync` (7, own scope); `:522` session/state save; `:525` start | 5, commit; then 7->8 leaf; consume before start | Rule X holds (confirmed by review f703a93e); lifecycle leak only | S3 |
| L17 | Queue flush redemption | `SessionMessageQueueService.cs:2578` (no tx) | 7 leaf, consume before flush | lifecycle | S3 |
| L18 | Capacity reconcile (hosted) | `CapacityRecoveryService.cs:138` grant, `:457` orphan cancel, `:1181` re-arm (+ `SessionQueuedMessages` rows), `ConsumePendingReleasesAsync` (no lock); `:691` counted slot (statement-scoped) | 7->8 leaf | conforms | - |
| L19 | Land paths | `AgentTaskLandService.cs:84/:353/:393/:1603`, `AgentTaskLandingProtocol.cs:954`, `AgentTaskLandMonitorService.cs:23/:56-57`, `AgentTaskLandRequestWriter.cs:233`, `AbandonedLandingReconciler.cs:81` | 3 (+5) | n/c | - |
| L20 | Other task-row lockers | `BlockedTaskParkingService.cs:92/:240`, `AgentTaskDecisionQuestionService.cs:59` (NOWAIT), `VerificationCleanupService.cs:25`, `BootStallWarningWriter.cs:77/:144`, `RemoteWorkspaceService.cs:261`, `RemoteWorkspaceReadiness.cs:29`, `PipelineHandoffActionService.cs:33-34/:55` (card 0 -> succeeded predecessor task 3 -> action) and `:159-160` (card, action), `SessionMessageQueueService.DispatchBrief.cs:61`, `SessionMessageQueueService.cs:1966-1969`, `ProviderWaitWarningWriter.cs:34-38`, `TerminalRunnerSeatReleaseService.cs:872/:875/:878` (task, release, session) then `:956` agent `ExecuteUpdate` (3->4->4->5, corrected round 3), `SettlementSyncRecoveryService.cs:300`, `BlockedTaskSyncRecoveryService.cs:139`, `VerificationExecutionService.cs:73`, `TaskParkPublicationService.cs:392-393` (task, session), `AgentTaskReplyService.cs:5276`, `ReviewEvidenceRecoveryService.cs:140`, `CardWorktreeCleanupExecutor.cs:79/:80/:85` (journal advisory -> card 0 -> terminal task 3), `CardWorktreeMirrorCleanupService.cs:100/:103/:112` (card 0 -> endpoint -> task 3), `TaskWorktreeRetirementService.cs:88`, `WorktreeRemovalEvidence.cs:234` | 0->3, 3 (+4, +5) | n/c; all conform to round-3 ranks | - |
| L21 | Agent-row lockers (corrected round 3) | **Session first (4->5, conforms; 4->2 for a specialist owner is E-1):** `AgentControlService.cs:547` -> `:549` (`LockAgentAsync` `:1485`), stop `:1277` -> `:1283`, `AgentService.cs:943` -> `:950`, `:1012` -> `:1016`, `AgentSessionService.cs:413` -> `:417` (`FOR NO KEY UPDATE`). **Agent only, no session lock or write in the same tx:** `AgentService.cs:1245` (queue update `:1085/:1128/:1182`, cards and queue rows), `AgentControlService.cs:177/:1013/:1255` via `LockAgentAsync` (autocommit, statement-scoped), `AgentPinnedInstructionService.cs:489/:500` (session read `:420` unlocked), `HerdrPlacementLock.cs:18/:21` (owner 2 then agent 5; users `AgentService.cs:659`, `HerdrLabelFollowService.cs:63` read sessions only), `HerdrSupervisionStateService.cs:26`, `StandingSpecialistHealthService.cs:28` (owner), `StandingSpecialistSeatService.cs:54` (owner), `StandingSpecialistRoutingService.cs:172`, `SpecialistRequestService.cs:482` (owner, after counted `:182`), `SpecialistRequestService.Qualification.cs:43` (counted) | 4->5, 4->2 (E-1), 1->2, 2->5, 5 | n/c (no row here holds 7; L16 commits first) | - |
| L22 | Other advisories and disjoint rows (never with rank 6/7) | host budget `HostBudgetService.cs:46`, `AgentTaskService.cs:1897`; `DelegationOpenGate.cs:140`; `SourceLandingAdmission.cs:73`; `DispatchConcurrencySettingsService.cs:728` (parallel key, settings writes, no rows of ranks 2-5: Rule F); `GrokRulesRefreshService.cs:187`; channel outbound (`ChannelOutboundService.cs:77/:209/:370/:547`, `ChannelOutboundDeliveryPump.cs:283/:328/:609`, `ChannelOutboundFailureRecorder.cs:27/:83/:89`, `ChannelOutboundMetadataRepair.cs:38`, `ChannelBridgeService.cs:454/:502`); `PipelineDefinitionService.cs:68`; `CardService.cs:1402`; `CardCompletionAcceptanceService.cs:94/:103` (card 0, workflow run); `ProjectCascade.cs:86`; `ExpectationLedger.cs:411/:535/:558/:579/:626`; `SessionMessageQueueService.CheckPublication.cs:23` (specialist request); `DispatchBaseWarningIntentService.cs:87/:181` (intent, post-commit); workspace-reservation admission advisory `WorkspaceReservationJournal.cs:253` (corrected round 3: own-scope tx at `:48-49`/`:85-86` takes the key then journal rows; reached from inside a claim via `AgentTaskDispatcher.cs:5630` -> `WorkspaceUseAdmission.cs:20` on another connection while the claim holds rank 3 (also from settlement sync, `RemoteWorkspaceService.cs:504`); its only ranked holder, `CardWorktreeCleanupExecutor.cs:79-85`, takes key -> card 0 -> a terminal task 3, never a `Queued` row, so no cycle with a claim); `DatabaseSeeder.cs:63`; Hangfire `KeepSixReminderJob.cs:41` | own key or row only, except the journal key above | n/c | - |
| L23 | Quota-blocked settlement (new, review F2) | `AgentTaskReplyService.cs:1970-1977` task edits and `:1980-1988` `ApiErrorRecoveries` edits (no tx); `:1991` -> `CapacityRecoveryService.cs:342-347` owned tx, provider (7); flush `:372` writes the task (3), and in the fence scope (`:121`) the fence adds 5->3 | 7->[3] or 7->5->3 **no** (Rule P) | S2: own tx; task `FOR UPDATE` (3), session if fenced (5), then mutate, supersede ambient (7->8), save, commit | S2 |
| L24 | Deferred-task fence (corrected, review F2) | `AppDbContext.cs:163` session (5) then `:164` task (3), on every save in the `AgentTaskReplyService.cs:121` scope | 5->3 **no** | S2: swap to task (3) then session (5); the identity checks at `:167-171` are order-independent | S2 |
| L25 | Session-row lockers (corrected round 3) | `SessionReconciliationService.cs:208` (session only) and `:649` -> agent write `:671` (4->5), `AgentSessionRuntime.cs:213` -> agent write `:276`, flushed `:331` (exit observer, 4->5; comment `:1454`), `AgentControlService.cs:547` (-> agent, L21), `SessionMessageQueueService.cs:5653`, `GrokRulesRefreshService.cs:240` (agent read only), `StandingBootWarningWriter.cs:57` (`SKIP LOCKED`) | 4, 4->5 | n/c; conform to round-3 ranks; none takes 7 in the same tx. A later task write in the same tx would be 4->3 like L24; the S2 ledger reports it | - |

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
  - Rank-4/5 pre-locks before `RefuseParallelAsync` (D-2, round-3 order): cold launch agent
    (5) before `:6098`; warm reuse previous-owner task (3), session `KEY SHARE` (4), agent
    `FOR UPDATE` (5) with reload and recheck, before `:8245`; standing placement session
    `KEY SHARE` (4) before `:8482`.
  - Consume after the flush and before the commit: `:6150`/`:6151` and `:5706`/`:5707` (D-2).
    Each shape gets its own unfiltered D-9 `try`, from the pre-locks to the commit. It
    classifies a failure (refusal, transient `40P01`/`40001`/`55P03`, commit-ack loss, or
    other) and never relies on the filtered catch at `:6153`.
  - Committed non-launch exits release the reservation.
  - Add `AbandonClaimAsync`, built from `FinishParallelHoldAsync` `:8004-8020` (D-4). Route the
    rollback exits that `Clear()` (`:5973`, `:5993`, `:6058`, `:6082`), the catch at `:6153`, and
    `FinishParallelHoldAsync` itself through it.
  - Delete the dispatch receipt block `:1050-1070`.
  - Reroute (D-5) adds `RewalkOutcome.ClaimLost`. `ResumeRoutingBlockedAsync` uses one
    transaction per task.
- `server/Application/Services/AgentTaskService.cs`: `LockReleasedAnswerAsync`
  (`:3632-3643`) locks the physical agent row after the session (`:3640`), giving
  A->R->S->X (D-5, L7/L8).
- Tests:
  - New: `tests/Antiphon.Tests/Application/CapacityAdmissionLifecycleTests.cs` and
    `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`.
  - Port `Stale_claim_preserves_capacity_grant_for_the_fresh_inspection` into
    `AgentTaskDispatchBaseGuardTests.cs`.
  - Rewrite the two `WallRerouteDispatchTests` receipt tests (V-14). `ThrowOnReceiptSave` (`:411-428`) gains a SQLSTATE argument: it throws `DbUpdateException` wrapping `new PostgresException("injected", "ERROR", "ERROR", sqlState)`, the existing pattern at `SessionStateCommitTests.cs:198`. V-14 passes `40P01`; V-13 `deterministic-consume-failure` passes `XX000`.
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

**No undetectable deadlock involves the claim, and no detectable one fails a task.**
- The claim's provider lock is its last lock, and the tracker is asserted clean there, so the
  claim never waits on a row while it holds a provider (Rule P).
- Every row it waits on is taken before the global parallel key (Rule F). The exception is
  the FK `KEY SHARE` on `Cards`/`Worktrees`, whose holders never wait on rank 6/7.
- L3, L4 and L7, the provider->row writers that could meet it, are fixed in S1.
- Round 3 fixes the pre-locks to follow the CARD-1190 session-before-agent order, so a stop,
  exit observer or seat release of the same agent meets the claim and the continuation in the
  same order (review f703a93e F1).
- The one remaining detectable cycle is E-1: a specialist stop (session -> owner) against
  standing placement (owner -> session). It exists at base, where the claim's victim is failed
  by the generic catch. S1 makes the claim's victim a D-9 class-2 abandon that leaves the task
  `Queued`.
- L23 and L24 cannot meet it (S2 reasoning above).
- The supervisor's cross-connection cycle does not exist (Ground truth, L16; confirmed by
  review f703a93e).

**Two lifecycle costs that S1 adds or keeps:**
- **D-10:** a long remote preparation can lose its grant to another waiter and wait one or
  more extra grant cycles (about 2-4 minutes). It spends no attempt.
- **D-9 class 4:** a deterministic consume failure fails the task, as base does for the same
  bug, and now also releases the reservation.

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
| V-4 | `CapacityAdmissionLifecycleTests/C1593_preparation_restart_and_dispatch_consume_one_admission` `[Arguments("after-receipt", false)]`, `("after-receipt", true)`, `("queue-inserted", false)`, `("lost-wakeup", false)`, `("grant-expired-during-prep", false)`, `("grant-expired-tick-first", false)` | The first four: preparation keeps the grant unspent; a restart in backoff keeps it; the launch spends once; the wait is `StartAccepted` with `LaunchSessionId`; one complete `UserPrompt` via `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync`.<br>**c5a `grant-expired-during-prep` (rewritten for review f703a93e F2):** after the `HeldForRemotePrep` commit:<br>1. Advance `FakeTimeProvider` past `StalledAdmissionTimeout` and run `GrantReadyAsync`, which revokes and re-arms. Assert the grant is cleared, `ActionOrdinal` moved by 1, `ActionKey` changed, `AdmissionCount == 0` and `OutcomeReason == "grant-expired"`.<br>2. Run a dispatch tick and assert the task is held, not claimed.<br>3. Advance past `DueAt` and run `GrantReadyAsync`, which regrants W.<br>4. Run the launch tick. Assert `AdmissionCount == 1`, the receipt is bound to the session, and the remote-prep fake's begin count is still 1: the finished preparation is reused (`:7311-7312`), which is the progress guarantee.<br>**c5b `grant-expired-tick-first`:** advance past expiry with no reconciler run, then run the launch tick. Assert it consumes the expired but unrevoked grant once (`AdmissionCount == 1`, `ActionOrdinal` unchanged, D-12). The test also records the preparation duration (D-10) | Red at base: the preparation tick consumed (`AdmissionCount` 1 before launch, grant cleared), so neither c5 launch tick is granted (`Dispatched == 0`), and the first four fail on the unspent-grant assertion |
| V-5 | `CapacityAdmissionLifecycleTests/C1593_crash_after_launch_commit_consumes_one_admission` | the `LandDeliveryBoundary` at `dispatch-warning-claim-committed` throws (crash cut c3); a new provider recovers through `ResumeInterrupted`; `AdmissionCount==1`, wait `StartAccepted` with `LaunchSessionId==S`, one complete `UserPrompt` on S, no second session | red at base (receipt never written: `Admitted`, `LaunchSessionId` null) |
| V-6 | `CapacityAdmissionLifecycleTests/C1593_enqueue_failure_consumes_one_admission` | the launch sink throws; the claim and the `Starting` session are kept; `AdmissionCount==1`, receipt bound to S; after recovery, one `UserPrompt` | guard; PC: drop the receipt write from the consume -> red |
| V-7 | `CapacityAdmissionLifecycleTests/C1593_late_consume_refusal_keeps_tick_identities` | two queued tasks A and B in one tick. A test-only `LandDeliveryBoundary` point `capacity-consume-before` revokes A's grant on another connection, so A is `NotClaimed` and its tracked entry is still tracked with database values. B dispatches with its own single admission and `LaunchSessionId==B.AgentSessionId` | red at the round-2 tip `730c76a2` pattern; PC: replace `AbandonClaimAsync` with a bare `Clear()` -> red |
| V-8 | `CapacityLockOrderTests/C1593_path_blocked_on_task_holds_no_provider_lock` `[Arguments("claim")]`, `("claim-agent-row")`, `("queued-reroute")`, `("routing-resume")`, `("continuation")`, `("continuation-agent-row")`, `("cancel")`, `("retry")` | A test connection holds a row `FOR UPDATE`. This is the task row except in two arguments: `claim-agent-row` holds the pinned pool agent row of a cold relaunch, and `continuation-agent-row` holds pool agent X, the agent the released task A ran on. The path runs until its backend waits on that row (`pg_stat_activity.wait_event_type='Lock'` for its pid). At that moment `pg_locks` shows no granted advisory with an `antiphon.capacity.kind.*` key for that pid. For `claim-agent-row` it also shows no `antiphon.delegation.parallel-tasks` key (Rule F). The test then releases the row and the path completes.<br>**`routing-resume` setup:** at least two routing-blocked tasks A and B in one pass, where B (the second) owns a capacity wait, so B's supersede has a kind to process and flushes A's edits. The test connection holds **A's** row.<br>**`claim-agent-row` setup:** dispatch concurrency is configured (`_dispatchConcurrency` non-null), so `RefuseParallelAsync` runs.<br>**`continuation-agent-row` setup:** A is `Ephemeral` with no park continuation, so `RemoveEphemeralAgentAsync` writes X (`AgentTaskService.cs:3997/:4004`). | **Red at base:**<br>- `queued-reroute`.<br>- `routing-resume`, only with the A/B setup above (one task alone passes at base).<br>- `claim-agent-row`: the base flush `:6150` waits on the agent row while holding the parallel key.<br>- `continuation-agent-row`: the base continuation waits on X at `:3997/:4004` while holding the provider from `:3845`.<br>**Guards, each with a method-scoped PC (review f703a93e F4):**<br>- `claim`: PC takes the provider lock before the task lock (round 2's provider-first prelude).<br>- `continuation`: PC calls the supersede before `LockReleasedAnswerAsync` (round 2 order).<br>- `cancel` / `retry`: PC wraps the supersede at `:2949` / `:3845` and the following task save in one transaction (the L3 base shape).<br>**PCs on the red arguments:**<br>- `claim-agent-row` PC (a) deletes the explicit agent pre-lock, which is red on the parallel-key assertion.<br>- `claim-agent-row` PC (b) deletes the pre-lock, moves the consume before the flush, and bypasses the Rule P assertion. That is red on the capacity-key assertion.<br>- `continuation-agent-row` PC drops the S1 agent lock from `LockReleasedAnswerAsync`, which is red. |
| V-9 | `CapacityLockOrderTests/C1593_path_blocked_on_provider_already_holds_task` `[Arguments("claim-launch")]`, `("queued-reroute")`, `("continuation")` | a test connection holds the provider advisory; the path blocks on it; `SELECT ... FOR UPDATE NOWAIT` on the task from the test connection raises `55P03`; for `claim-launch` the change tracker of the claim context has no `Modified`/`Added`/`Deleted` `AgentTask`/`Agent`/`AgentSession` entry at that moment (Rule P assertion, review F1) | red at base: `claim-launch` (no provider lock at launch) and `queued-reroute` (no task lock first) |
| V-10 | `CapacityLockOrderTests/C1593_competing_claim_and_reroute_complete_without_deadlock` and `C1593_competing_claim_and_continuation_complete_without_deadlock` | **Two test-only `LandDeliveryBoundary` points (review f703a93e F4):** `capacity-consume-before` in the claim, and `capacity-supersede-after-lock`, right after `TakeProviderLockAsync` inside the supersede.<br>**Steps:**<br>1. Run the claim to its boundary. It now holds its pre-locks and flush.<br>2. Start the non-claim path and poll until one of two states holds. Either its pid holds the kind key at its boundary (`pg_locks`), or its backend waits on a row lock (the S1 shape: blocked on the claim's task row or X).<br>3. Release the claim. If the other path is at its boundary, poll until the claim's pid waits on the advisory.<br>4. Release the other path.<br>**Cases:**<br>- *Reroute:* targets the claimed task.<br>- *Continuation:* claim task B is pinned to pool agent X (cold relaunch). The continuation is for task A, which last ran on X, is `Ephemeral` and has no park continuation.<br>**Assertions:** both paths complete, no `40P01` on either connection, exactly one dispatch, and the reroute ends `ClaimLost` | Guard at base: the base claim takes no provider lock, so neither case can cycle. Each PC yields `40P01` deterministically through the boundaries:<br>- *Reroute PC:* restore base `TryRewalkQueuedChainAsync` (`:1682-1686`, no owned tx) under the S1 claim.<br>- *Continuation PC:* drop the S1 agent lock in `LockReleasedAnswerAsync`. |
| V-11 | `CapacityAdmissionLifecycleTests/C1593_warm_reuse_consumes_one_admission` | rollback variant (review F4). Tick 1: a warm pool agent matches and the parallel limit is full, so `HeldOnParallel` happens after the reuse selection. Free the slot; tick 2 reuses the warm session. Assert `AdmissionCount==1`, wait `StartAccepted`, `LaunchSessionId ==` the reused session id, and the agent `Running` | red at base: the tick-1 pre-claim redeem consumed and cleared the grant, so tick 2 holds on "not yet granted" (`Dispatched==0`) |
| V-12 | `CapacityAdmissionLifecycleTests/C1593_standing_placement_consumes_one_admission` | same rollback variant on a non-specialist standing agent with a live session (`PlaceOnStandingAgentAsync`). Assert `AdmissionCount==1`, `StartAccepted`, `LaunchSessionId ==` the standing session | red at base, as V-11 |
| V-13 | `CapacityAdmissionLifecycleTests/C1593_consume_failure_leaves_the_task_queued` `[Arguments("deadlock-victim")]`, `("pre-lock-deadlock-victim")`, `("lost-commit-ack")`, `("deterministic-consume-failure")` | D-9 classes.<br>**`deadlock-victim`:** the SQLSTATE interceptor throws `40P01` on the consume save. Expect:<br>- task `Queued`, `FailureReason` null<br>- no committed session row<br>- grant still `W`, `AdmissionCount == 0`<br>- one `capacity consume refused` Warning, `Failures == 0`<br>- the next tick dispatches once<br>**`pre-lock-deadlock-victim`** (warm reuse): a `DbCommandInterceptor` throws `40P01` on the first command after the claim's task lock that locks or writes X's `Agents` row. Expect the same outcome as `deadlock-victim`.<br>**`lost-commit-ack`:** a `DbTransactionInterceptor` throws from `TransactionCommittedAsync`, after the real commit. Expect:<br>- task `Dispatched`, `AdmissionCount == 1`<br>- wait `StartAccepted`/S, no `Failed`<br>- a Warning<br>- interrupted-launch recovery relaunches S once<br>**`deterministic-consume-failure`** (review f703a93e F3): the interceptor throws `XX000` on the consume save. Expect:<br>- task `Failed` through `FailAndNotifyAsync`, `Failures == 1`<br>- wait `Canceled`/`claim-consume-failed`, grant cleared, `AdmissionCount == 0`<br>- a second tick neither claims nor consumes again | Red at base:<br>- `deadlock-victim`: base has no consume in the claim. The fault hits the post-commit receipt, so the task is `Dispatched`, not `Queued`.<br>- `pre-lock-deadlock-victim`: base writes X in the flush at `:5706`, where there is no catch, so the generic catch fails the task.<br>- `lost-commit-ack`: the base catch `:6153` reloads and rethrows, and the generic catch `:1120` fails the task.<br>- `deterministic-consume-failure`: base swallows the receipt fault at `:1058-1068`, so the task is `Dispatched`. |
| V-14 | `WallRerouteDispatchTests` rewritten pair (review F3): `Receipt_throw_after_launch_leaves_the_task_dispatched` (`:271`) becomes `Receipt_throw_in_the_claim_commit_leaves_the_task_queued`, and `Failed_receipt_cannot_resurrect_a_concurrently_superseded_wait` (`:300`) becomes `Failed_consume_cannot_resurrect_a_concurrently_superseded_wait` | `ThrowOnReceiptSave` (`:411-428`) now throws the transient class (`40P01`, D-9 class 2; it threw `InvalidOperationException`, which would now be class 4) and fires on the consume save inside the claim.<br>**First test:**<br>- Tick 1: `fault.Throws == 1`, `Dispatched == 0`, `Failures == 0`. The task is `Queued` with `AgentSessionId` and `FailureReason` null. The wait is `ActionPending`, `AdmissionCount == 0`, and the grant is still `W`.<br>- Tick 2 (fault spent): `Dispatched == 1`, `AdmissionCount == 1`, `StartAccepted`, and `LaunchSessionId` equals the task's session.<br>**Second test:**<br>- After tick 1 the claim context has no open transaction.<br>- A concurrent supersede ends the wait `Superseded`.<br>- A later unrelated event save on the same dispatcher context leaves `State`/`Outcome`/`OutcomeReason`/`Version` unchanged and `SessionId`/`LaunchSessionId`/`DispatchAttemptId` null.<br>- The task stays `Queued`, and `fault.Throws == 1`. | Red at base: both assert `Dispatched == 0` after tick 1, and base dispatches (`Dispatched == 1`) |
| V-15 | `CapacityLockOrderTests/C1593_stop_and_warm_reuse_complete_without_deadlock` (review f703a93e F1) | **Setup:** pool agent X is `Idle` with live session S. Queued task T matches warm reuse, and dispatch concurrency is configured.<br>**Steps:**<br>1. A test connection follows the stop path's order (`AgentControlService.cs:1276-1283`) and locks S `FOR UPDATE`.<br>2. Run the tick until the claim's backend waits on a lock. At that moment `pg_locks` shows no parallel-tasks key and no kind key for the claim's pid (Rule F).<br>3. The test connection locks X `FOR UPDATE`, which must succeed within the deadline. It marks X and S `Stopped`, then commits.<br>**Assertions:** the claim's recheck sees the change and falls back to `SpawnFresh`. T is `Dispatched` on a new session, with no `Failed`, no `40P01` on either connection, and `AdmissionCount == 1`. | **Red at base:** the base claim reaches S only in the flush after `:8245`, so it waits while holding the parallel key (Rule F assertion). **PC:** lock X before S in the warm-reuse pre-locks (round-2 order). The test's X lock then forms a cycle and one side gets `40P01`, so the test is red. |
| R-1 | `WallRerouteDispatchTests` (other 11 methods), `CapacityRecoveryTaskTests`, `CapacityRecoveryAcceptanceTests` (whole classes) and `AgentTaskDispatchBaseGuardTests/C508_GuardRefMismatchWarnedOnce` (4 args) | unchanged behaviour. `CapacityRecoveryAcceptanceTests` calls `RedeemAsync` directly, and the wrapper keeps `Admitted` semantics (D-2). `Redeemed_dispatch_receipts_the_wait_and_the_transcript_progresses_it` keeps its expectations (`StartAccepted`, `AdmissionCount==1`), because the receipt now comes from the claim. If any other R-1 assertion encodes the pre-claim consume, Code stops and reports it as a plan amendment rather than editing the test | - |

Deterministic means no sleeps: waits poll `pg_stat_activity`/`pg_locks` for the path's backend
pid, with a 30 s deadline. The advisory key is matched by `classid`/`objid`, computed from
`hashtext(key)` on the same server. Lifecycle tests advance a `FakeTimeProvider`, not wall time.

### Red witnesses (run before the product commit, not in the table)

One build-slot build of the S1 test commit, then these `--no-build` filters. Each must show the
named assertion failure; zero tests or a fixture error is not red.

- `/*/*/AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection`
- `/*/*/CapacityAdmissionLifecycleTests/(C1593_rollback_exit_keeps_reservation*)|(C1593_committed_non_launch_exit_releases_reservation*)|(C1593_preparation_restart_and_dispatch_consume_one_admission*)|(C1593_crash_after_launch_commit_consumes_one_admission*)|(C1593_warm_reuse_consumes_one_admission*)|(C1593_standing_placement_consumes_one_admission*)|(C1593_consume_failure_leaves_the_task_queued*)`
- `/*/*/CapacityLockOrderTests/(C1593_path_blocked_on_task_holds_no_provider_lock*)|(C1593_path_blocked_on_provider_already_holds_task*)|(C1593_stop_and_warm_reuse_complete_without_deadlock*)`. Expected: the red arguments named in V-8/V-9 and V-15 fail; the guard arguments pass.
- `/*/*/WallRerouteDispatchTests/(Receipt_throw_in_the_claim_commit_leaves_the_task_queued*)|(Failed_consume_cannot_resurrect_a_concurrently_superseded_wait*)`

The PCs for V-6, V-7, V-8 (all eight: `claim`, `claim-agent-row` (a) and (b), `continuation`,
`continuation-agent-row`, `cancel`, `retry`; `queued-reroute` and `routing-resume` are red at base
and need none), V-10 (both cases) and V-15 are each method-scoped, run once, and restored. A
PC passes only when the named assertion fails, not on a build or fixture error. The V-10 and
V-15 PCs pass only when the run shows `40P01` in the TRX output.

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
- **Budgets:** 34 new results (20 lifecycle + 14 lock-order; round 3 adds V-4 c5b, V-8 `continuation-agent-row`, V-13 `pre-lock-deadlock-victim` and `deterministic-consume-failure`, and V-15), plus the 2 rewritten
  `WallRerouteDispatchTests` methods inside CP-3.
  - p95 per method is estimated at 12 s, so the method timeout is 30 s, and so is the lock-wait
    deadline inside a test. Both are provisional until Code records p95 from its TRX.
  - V-4 `grant-expired-during-prep` runs on `FakeTimeProvider`, so the 2-minute expiry costs
    no wall time. The real server2 preparation p95 that D-10 asks for comes from server2 task
    history, not from this test: the gap between the `HeldForRemotePrep` Held event
    (`DispatchHoldDetails.RemoteMirrorRequested`, `AgentTaskDispatcher.cs:1014-1015`) and the
    `Dispatched` event. Code reports both.
  - Row budgets: CP-1 about 15 min including build (34 methods at a 12 s p95 is about 7 min, plus host start), CP-2 about 3 min, CP-3 about 10 min. The
    whole round stays under 30 min of test time. A row over 2x its
  estimate is a Debug item, not a timeout raise.

### Checkpoints

Lane: Linux, server2 (runner default `globalRunnerId=server2`). No `-Runner` or `-Platform` pin.
Build once, then use `--no-build` (operator direction).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1593/` | c1593-new | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityLockOrderTests*)/*` | V-2..V-13, V-15 | all listed, 0 failed/skipped (20 lifecycle + 14 lock-order results) | 34 | 15 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S1 | CP-1 | c508-guard | `/*/*/AgentTaskDispatchBaseGuardTests/(Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*)\|(C508_GuardRefMismatchWarnedOnce*)` | V-1, R-1 | all listed, 0 failed/skipped (1 + 4 args) | 5 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S1 | CP-1 | capacity-regress | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(CapacityRecoveryTaskTests*)\|(CapacityRecoveryAcceptanceTests*)/*` | V-14, R-1 | >= 36 executed (WallReroute 15 incl. the 2 rewritten, Acceptance 15, Task 6), 0 failed/skipped | 36 | 10 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Suffix check at base: `CapacityAdmissionLifecycleTests*`, `CapacityLockOrderTests*`,
`WallRerouteDispatchTests*`, `CapacityRecoveryTaskTests*` and `CapacityRecoveryAcceptanceTests*`
each match exactly one class (`WallRerouteFixture`/`WallRerouteHarness` and the other
`CapacityRecovery*` classes do not match). Code verifies each intended class in the fresh TRX.
`Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*` and
`C508_GuardRefMismatchWarnedOnce*` have no sibling method prefixes in that class. The new
method names in the red-witness filters (`C1593_*`, including round 3's `C1593_stop_and_warm_reuse_complete_without_deadlock`, `Receipt_throw_in_the_claim_commit_leaves_the_task_queued`,
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
- The new rank 4 (physical agent) also fixes the round-1 table, which had no rank for it. (Round 3 moves it to rank 5, after sessions; see the f703a93e response.)

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

## Review f703a93e response

Every reference is at rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`. The review
confirmed priority 1, the supervisor refutation, so L16 is unchanged apart from rank numbers.

**F1 (rank 4 before 5 inverted CARD-1190). Accepted as specified.**
- *Evidence re-read:* `AgentControlService.cs:546-549` (and its CARD-1190 comment), `:1276-1283`;
  `AgentService.cs:942-950`, `:1011-1017`; `AgentSessionService.cs:410-417`;
  `AgentSessionRuntime.cs:213` -> `:276` (flushed `:331`); `TerminalRunnerSeatReleaseService.cs:872-878`
  -> `:956`; `SessionReconciliationService.cs:649` -> `:671`.
- *Rank table (D-1):* the order is now 0 card -> 1 counted -> 2 owner -> 3 task ->
  4 release/session -> 5 physical agent -> 6 parallel -> 7 provider -> 8 capacity rows.
- *Pre-locks:*
  - Warm reuse takes S `KEY SHARE`, then X `FOR UPDATE`, and rechecks `PersistentSessionId == S`.
  - L7 is A->R->S->X.
  - Cold launch only moves its agent lock's rank.
- *Table:* L20 (TRSR), L21 (split into session-first and agent-only sites) and L25 (exit
  observer, reconciliation) are corrected.
- *E-1* records the pre-existing, detectable specialist owner-vs-session pairing.
- *D-9 class 2* covers `40P01`/`55P03` from any pre-lock, so a victim abandons and is never
  failed.
- *New witnesses:* V-15 `stop_and_warm_reuse` (red at base on the Rule F assertion; its PC is
  the round-2 X-before-S order, which must produce `40P01`) and the V-13
  `pre-lock-deadlock-victim` argument.
- *Lock-table re-verification for sites the rank change touches.* I re-read every site that
  locks or writes both a session and an agent row. No transaction at base locks a physical agent
  and then locks or writes a session:
  - `HerdrSupervisionStateService.cs:41`, `AgentPinnedInstructionService.cs:420`, the
    `HerdrPlacementLock` users (`AgentService.cs:659`, `HerdrLabelFollowService.cs:63`) and
    `GrokRulesRefreshService.cs:240` only read sessions.
  - `AgentControlService.cs:177/:1013/:1255` lock the agent in autocommit, so the lock is
    statement-scoped.
  - The supervisor's `CompleteAsync` (`AgentSupervisorService.cs:349-356`) saves supervision
    state, not sessions.

**F2 (expiry is reconciler-only). Accepted; decision D-12: keep the reconciler contract.**
- `RedeemAsync` checks the grant id and key (`CapacityRecoveryService.cs:259`) and nothing
  about age. Revocation (`GrantReadyAsync` `:148-158`) and the consume serialize on the same
  provider lock, so a matching grant is safe to spend.
- The smallest alternative (a `GrantExpired` refusal after `:259`) is rejected in D-12: it adds
  a regrant cycle for no safety gain.
- *What changed:*
  - c5 is rewritten as c5a (reconciler first) and c5b (tick first).
  - "Grant expired" is gone from D-9.
  - D-10 now says the provider is regranted to other waiters while W prepares (`:177-179`).
  - V-4 c5a follows the reviewer's sequence, with a begin-count assertion for the progress
    guarantee (`AgentTaskDispatcher.cs:7311-7312`). V-4 adds c5b.

**F3 (endless retry). Accepted: fail and release.**
- D-9 has four classes. Classes 1-3 keep the task `Queued`: refusals, transient
  `40P01`/`40001`/`55P03`, and a commit-ack loss whose reread shows `Dispatched`.
- Every other exception is class 4. The claim abandons, releases the reservation in its own
  short transaction (`claim-consume-failed`), and rethrows to the generic catch, which fails
  the task as base does.
- A per-wait failure cap was rejected: it needs a migration in an rc slice.
- `ThrowOnReceiptSave` gains a SQLSTATE: V-14 uses `40P01`, and V-13
  `deterministic-consume-failure` uses `XX000`.

**F4 (controls that cannot go red). Accepted.**
- V-8 `claim-agent-row` gets PC (a), deleting the pre-lock, which is red on the parallel key.
  PC (b) deletes the pre-lock, consumes before the flush and bypasses Rule P, which is red on
  the capacity key.
- The new `continuation-agent-row` argument holds X and is red at base. The `continuation`
  guard gets the round-2-order PC, and the `claim`/`cancel`/`retry` guards get named PCs.
- V-10 gains the `capacity-supersede-after-lock` boundary and `pg_locks` gating, so each PC's
  `40P01` is deterministic.

**Advisories.**
- *The catch at `:6153` is filtered, and `:5706` has none:* D-9 has its own unfiltered `try`
  per shape (D-9 scope, S1 text).
- *Rule F vs `Cards`/`Worktrees` FK `KEY SHARE`:* `Cards` is rank 0, and Rule F carries the FK
  exemption with its holder list.
- *L22 journal description:* corrected. The journal key is taken cross-connection from inside
  a claim via `AgentTaskDispatcher.cs:5630` -> `WorkspaceUseAdmission.cs:20`, and its one ranked holder,
  `CardWorktreeCleanupExecutor.cs:79-85`, takes key -> card -> a terminal task.
- *A commit landing after the reread:* stated as safe in D-9 class 3. `AbandonClaimAsync`
  tolerates a throwing rollback.
- *Counts:* CP-1 is now 34 (20 + 14). CP-2 and CP-3 are unchanged.
