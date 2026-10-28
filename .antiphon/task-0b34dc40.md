# Review 0b34dc40: CARD-1593 S3 atomic-consume plan (design review)

Subject task 62fb542c-8dec-4e3b-a494-338b955f156d, branch `feat/card-task-62fb542c`, plan tip
`df429e37451aca20f8af65baefcc2952a85bad75`, plan
`docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md`. Base `rc/2026-10-27`
`aa6dfd24a`. All file:line references are at that base.

Verdict: **the design direction is right, but there are 5 blocking defects.** These are: retained-task
stranding, Enter-only delivery without a spend, S-4 lost-ack handling, an incomplete supervisor exit
table, and coverage that is too narrow for the regression risk. Next stage: **plan amendment**.

What ran: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef aa6dfd24a… -HeadRef df429e374…`
reported commits=2, entries=0, violations=0, exit 0. Nothing was built or tested; this was a design
review. I counted the existing results at base and they match the plan: Supervision 6, Task 6,
Queue 8, Lifecycle 26, LockOrder 16, WallReroute 15, ReceiptFailure 21. Ground-truth rows 1-7 and 9
check out against the code.

## Blocking defects

### D-1 S3c: `ReturnRetainedAsync` strands retained tasks
- **Where:** plan D-S3-3 steps 2 and 4. Base code: `AgentTaskDispatcher.cs:631-635` and `:1756-1757`.
- **Failure:** at base a retained task with **no unfinished wait** returns to execution without a
  consume (`wait is null || …`). This covers a wait that is `Exhausted` after MaxAttempts, one that
  was `Canceled` (orphan sweep or S1 class-4 release) or one that was `Superseded`. A retained task
  also returns with **capacity disabled** (`_capacityRecovery.IsEnabled && …`). The plan's method
  always consumes `waitId` and rechecks `CapacityWaitId == waitId`. Each of those cases refuses
  (`missing-grant`, `not-action-pending` or `feature-disabled`), rolls back and returns false on
  every tick. The task stays `Working` with `CapacityWaitRetained = true` forever: it is never
  counted and never resumed.
- **Why:** this is a new strand with no reconciler backstop. Retained tasks are not orphan-swept.
- **Fix:**
  - `ReturnRetainedAsync(taskId, (Guid Id, string Key, AgentKind Kind)? wait, int? localLimit)`.
  - When `wait` is null or the feature is disabled, run steps 1-3 and commit with no consume.
  - Recheck only `Working && CapacityWaitRetained`. Consume the wait that
    `FindUnfinishedCapacityWaitAsync` finds.
  - Keep the tick's `WaitingForHold` skip and the AsNoTracking grant-match pre-read
    (`:1762-1769`) before the call. Otherwise every ungranted retained task takes the counted lock,
    a task row lock and the provider lock on every tick.
  - Add V-S3-6b `[Arguments("no-unfinished-wait")]` and `("feature-disabled")`. Both are guards at
    base. The PC deletes the null branch.

### D-2 S3d: Enter-only and pointer-recovery exits deliver without spending
- **Where:** Ground truth row 8 and D-S3-4. The code paths:
  - `SessionMessageQueueService.cs:2687-2701` -> `EnterOnlyConfirmLockedAsync` (`:3380`).
  - `RecoverPointerAsync` (`SessionMessageQueueService.CodexPointers.cs:128-143`) -> Enter-only (`:139-140`).
- **Failure:** the plan lists "enter-only confirm" as a pre-`Sent` exit that "returns without
  delivery". It does not: it presses Enter on a body that was typed earlier, which submits a
  provider turn. Consider a capacity-stamped head with `DeliveryAttempts > 0`. That happens when an
  earlier attempt hit the wall and `ApplyCapacityHoldAsync` (`:5561-5573`) then stamped the same
  Pending row, or on a revalidation redelivery. Once the consume moves to the `Sent` stamp, that head
  is submitted with no consume, so:
  - the grant stays held,
  - `NextAdmissionAt` is not advanced, so the next waiter is admitted with no interval,
  - `AdmissionCount` does not move, so MaxAttempts never applies.
- **Why:** this is exactly the "launched without spend" class. Base spends at `:2586` before these
  exits.
- **Fix:**
  - Consume in `Admitted` mode in a short transaction immediately before the Enter keystroke in
    `EnterOnlyConfirmLockedAsync`, when the head carries `CapacityWaitId` and its key.
  - On refusal, return `Nothing` without pressing Enter.
  - Add a V-S3-8 argument `("enter-only")`. It is a guard at base. The PC deletes that consume,
    which turns it red with `AdmissionCount == 0` and a null clock.

### D-3 S3b: S-4 lost commit-ack is unsafe, and "success" never launches
- **Where:** D-S3-5 row S-4. Code: `AgentControlService.cs:687-688` and `:797-798`.
- **Failure 1:** a fresh-scope reread can see "not committed" while the in-doubt COMMIT is still
  pending. S1 D-9 class 3 already established this. S-4 then takes the S-3 path:
  - `RecordStartFailureInFreshScopeAsync` records a backoff for a start that does commit.
  - The charge does not double-spend: it blocks on the advisory, then refuses on `grant-mismatch`.
- **Failure 2:** "committed -> treat as success" never calls `EnqueueInteractiveSession`, because
  the enqueue sits after `CommitAsync`. The result is a `Starting` standing session with nothing
  launching it, and the plan names no recovery.
- **Fix:**
  - Do the S-4 reread in a fresh transaction under `SELECT … FOR UPDATE` on the agent row (rank 5,
    leaf). That lock waits out the in-doubt transaction.
  - If committed: enqueue with the accepted generation, or name and test the interrupted-launch
    recovery for standing sessions.
  - If not committed: take S-3.
  - Add a witness that throws from `TransactionCommittedAsync`: `AdmissionCount == 1`, exactly one
    launch, no backoff.

### D-4 S3b: the supervisor exit table (D-S3-5) is incomplete
- **Where:** D-S3-5. Code: `AgentSupervisorService.cs:230-245` and `:531-571`.
- **Failures:**
  - (a) `HerdrSupervisionStateService.HeldCode` and `StandingContinuityState.HeldCode` thrown from
    inside the start are caught at `:232-238`, which returns false with no backoff. No S-class covers
    this exit, and the plan specifies neither a charge nor a release for it.
  - (b) S-1 says "the supervisor catches `capacity_consume_refused`". The non-reserve
    `_control.StartAsync` at `:531` is outside `StartSupervisedRestartAsync`'s try, and that try
    filters `standing_resume_*` only. A refusal therefore reaches the generic catch at `:236`, which
    records a backoff and an S-3 charge. That is the opposite of S-1. The catch must sit beside `:232`.
  - (c) Transient `40P01`/`40001`/`55P03` errors fall to S-3 and are charged. The reservation now
    holds ranks 4 and 5 while it waits on rank 7. S1 class 2 says a transient is not charged.
  - (d) `OperationCanceledException` at shutdown is not listed.
  - (e) After an S-1 or S-4 rollback, `AcceptAsync` has already saved the agent pointer, intent and
    session edits into the shared scoped `_db`. The tick reuses that context across agents. S1 D-4
    requires a reload or `ChangeTracker.Clear()`; base does the equivalent for refusals through a
    fresh scope (`:545-559`).
- **Fix:**
  - Add rows for (a) (S-0 or S-2: choose and state it), (c) (no charge, no backoff) and (d).
  - Pin the S-1 catch site.
  - Require a tracker clear after a rollback.

### D-5 Coverage too narrow for "every standing start"
- **Where:** `### Checkpoints` (CP-3) and the verification design.
- **Failure:**
  - S3b rewrites the fresh-reservation pre-check (`AgentControlService.cs:599-611`) and `AcceptAsync`
    (`:806-816`). CP-3 runs 3 of the 51 `StandingRestartEscalationTests` results, which span 9
    partials: Reservation, Refusal, Boundaries, Guards, Transient and others.
  - No row runs `AgentSupervisionTests` (10 results).
  - These paths have no witness at all:
    - the card arm (L16c, consume after `CardService.SpawnAsync`),
    - S-4,
    - S-2 for `capacity_recovery_*`,
    - the remote retained return (`localLimit: null`).
- **Fix:**
  - CP-3 runs the whole `/*/*/StandingRestartEscalationTests/*` class (51 results) plus
    `AgentSupervisionTests` (10).
  - Add a card-arm guard. The PC deletes the post-spawn consume, which leaves `AdmissionCount == 0`.
  - Add the S-4, S-2 `capacity_recovery_suspended` and remote-retained arguments.

## Advisories
- **A-1 Lock order, priority 1:** walked against the S1 rank table. It conforms.
  - **L16':** the consume runs on the reservation's own connection after `AcceptAsync`'s save, with
    nothing written after it. There is no runner or network await between the consume and the
    commit: `ListAsync` (`:527`) and composition both come before `:543`, and the enqueue comes after
    the commit.
  - **In-process queue locks:** the reservation holds them (`:538-541`) while it waits on rank 7.
    `CapacityRecoveryService` never takes a queue lock, so that wait cannot form a hidden cycle.
  - **The Rule P assertion is vacuous:** after a save, `RejectDirtyLaunchRows` always passes. The
    real guarantee is that nothing is written after the consume, and V-S3-11 should assert that.
  - **L17':** `RearmStalledAdmissionsAsync` re-keys `SessionQueuedMessages` under rank 7
    (`:1342` -> `:1402-1408`). It only does so for receipt-less waits, and a stamped head has
    `SelectedMessageId`, so no cycle forms. Record this in the table.
  - **L6':** the counted lock now spans the transaction and serializes with L1, as intended.
- **A-2 Post-Sent exits:** the list misses `CancelJustClaimedExpiredBriefsAsync` (`:2753`) and
  `OptionalBriefExpiredException` (`:2758-2761`). Both cancel the selected message after the spend.
  The wait is then `Admitted` with a receipt, so it re-arms as revalidation and is regranted on every
  expiry with no consumer and no MaxAttempts bound. This is pre-existing at base. Record it as a
  residual, or finish the wait when its selected message is canceled.
- **A-3 Repeated failed delivery (priority 3):** `AdmissionCount` is spent once in total, because
  revalidation never increments. Each redelivery still advances the provider clock and holds the
  grant. The bound is only the queue's `DeliveryAttempts`/withhold. There is no double spend:
  revalidation consumes run under the same provider lock and clear the grant.
- **A-4 S3f, priority 4:**
  - The cap is enforced by the dispatcher: `RenewGrantAsync` uses the in-memory `since`. The
    reconciler enforces only 120 s after the last renewal.
  - Renewal stops whenever an earlier `continue` skips the task (`:849-943`: root budget, sibling
    guard, platform gate). D-12 then revokes, which is safe.
  - Holding a provider for up to 600 s blocks every other waiter of that kind.
  - Ship with the default **0 (off)** on master until the p95 is measured, and do not port it to rc.
- **A-5:** D-S3-1 also opens the dispatcher's pre-claim gate (`:1774`) to `Admitted`
  revalidation task waits. A retained task that is later requeued is one example, so Risk 5's "only
  legacy rows" is too narrow. State it.
- **A-6 S-0 residual:** the bound text "live agent progresses the wait" is false for a human Fresh
  start.
  - Progress keys on `wait.SessionId` (`CapacityRecoveryService.cs:883-889`).
  - `OrphanedWaits` (`:665-680`) never cancels standing waits.
  - So the grant cycles every expiry until the agent dies. This is pre-existing; record it as a
    residual.
- **A-7:** V-S3-12, the no-production-caller guard, can only go green once S3b, S3c and S3d have all
  landed. It must ride the last of them on each branch.

## Slicing (priority 6)
- **Minimum viable slice:** S3a + S3b, plus the L16' argument of V-S3-11. This fixes the High
  supervisor leak.
- **Order:** rc is mid-qualification (Windows run on `4563e525`), so land S3a and S3b on master
  first. Port them to rc only after a clean master Review that includes the widened CP-3. Whether the
  current rc needs them at all is the orchestrator's call.
- **S3c** (stale snapshot only) and **S3d** (most complex, and D-2) go to master only until soaked.
- **S3f** goes to master only, with default 0.
