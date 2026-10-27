# Design Review fe4577c5: CARD-1593 capacity admission plan (subject a9ca2252)

Reviewed: `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md` at
`545ce41223b72821574e03d237153792b6d1033e`. Base is rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.
The tip contains only the plan; I ran no tests because the branch has no product or test code yet.
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef f84228f2 -HeadRef 545ce412` found
commits=1, entries=0, violations=0 (exit 0).

**Verdict:** the overall direction holds: task first, provider as a leaf, consume and receipt
in the claim commit, and one rollback helper. Four plan defects need fixing before Code.
All line numbers are at the base.

## Findings (design defects)

### F1: the S1 claim writes Agents rows after it takes the provider lock. This breaks Rule P, and L1 is wrong.
- **Where:** `AgentTaskDispatcher.cs`.
  - Cold launch: `agent.PoolProjectId/LaunchEnvJson/...` at `:6003-6011` and
    `agent.PersistentSessionId/Status/UpdatedAt` at `:6102-6104`, flushed by `SaveChangesAsync` at `:6150`.
  - Warm reuse: `agent.Status/PoolIdleSince/PoolReservedForRootTaskId` at `:8231-8234`, flushed at
    commit. Neither row is locked earlier in the claim; only the specialist *owner* row is
    locked, at `:5539`. The plan puts the consume "immediately before `Status = Dispatched`"
    (`:6100`, `:8247`), so EF writes the Agents row *after* rank 6. The real sequence for L1 is
    `1->2->3->(3)->5->6->Agents write`, but the table records `1->2->3->(3)->5`.
- **Failure:** L16 stays deferred to S3. The supervisor locks a standing `Agents` row X with
  `FOR UPDATE` on connection A (`HerdrSupervisionStateService.cs:26`). It then waits for provider K on
  connection B (`AgentSupervisorService.cs:507` `RedeemAsync`). Meanwhile a cold-launch claim
  onto that pinned standing agent holds provider K and waits for row X at `:6150`.
  PostgreSQL cannot see this cycle. It clears only when a command times out, so the claim or
  the restart fails for that cycle. S1 creates this wait cycle, because the base claim takes no
  provider lock. The plan's S2 `LockOrderLedger` would also throw on every warm reuse and every
  cold relaunch.
- **Same class elsewhere:** L7, the released-seat continuation, is marked as conforming. After
  `SupersedeTaskWaitsOnAsync` at `AgentTaskService.cs:3845`, `RequeueCoreAsync` calls
  `RemoveEphemeralAgentAsync` (around `:3880`). That call deletes the pool Agents row or marks it
  Idle inside the same transaction, under rank 6, and the row was never locked earlier. No
  current cross-connection cycle exists, because the supervisor does not supervise pool
  delegates. The row and the "continuation" V-8 guard still misstate it.
- **Fix (amendment):** make the provider lock the claim's last lock. Run `SaveChangesAsync`
  to flush every entity write (task, agent, session, events, parks and verification
  reservation) **before** `RedeemOnAsync(consume:true)`. After the consume, only capacity rows
  (rank 7) may be written before commit. In S1, assert in the claim that the ChangeTracker
  has no Modified/Added/Deleted `AgentTask`/`Agent`/`AgentSession` entries at consume time.
  Add a V-8 argument `claim-agent-row`: a test connection holds the pool or pinned agent row,
  the claim blocks on it, and `pg_locks` shows no capacity-kind advisory held. Its positive
  control is the unamended consume-before-flush order.
  - Correct L1 and L7 in the table. For L7, either lock the pool agent before the supersede or
    move `RemoveEphemeralAgentAsync` ahead of it.

### F2: the acquisition table misses one site and records another in the wrong order
- **Missing:** `AgentTaskReplyService.cs:1991`, the quota-blocked settlement in
  `HandleApiErrorTurnAsync`. It mutates the task (`Status=Blocked`, `CapacityWaitId=null`, new
  `ConcurrencyToken`; `:1970-1977`) and the `ApiErrorRecoveries` row, with no ambient
  transaction. It then calls `SupersedeTaskWaitsOnAsync`. That call opens its own transaction,
  takes the provider lock and runs `SaveChangesAsync` (`CapacityRecoveryService.cs:347/:372`),
  which flushes the dirty task row under the provider lock. This is provider->task, the same
  shape as L3. No slice covers it.
  - It does not deadlock with the S1 claim. The claim's `Status==Queued` filter rolls back
    before the provider lock, because that task is Working.
  - It does break Rule P. The S2 ledger would throw on this production path.
  - It can deadlock (40P01) with L7 on the same task/session.
- **Wrong order:** `AppDbContext.cs:162-164`, the deferred-task fence, is listed in L20 as
  `3 (+4)`. It locks `AgentSessions` **before** `AgentTasks` (4->3), which inverts D-1 and
  reverses L7/L11 (task->session). If a fenced scope's save runs inside a provider-holding
  supersede, the order becomes provider->session->task explicitly.
- **Fix:** add the quota path as row L23 and assign it to S2. Either supersede before mutating
  the task or lock the task first, and add a V-8 argument. Correct the fence row, and either
  give sessions-before-tasks for the fence its own rank or justify an exemption.

### F3: R-1 says "unchanged behaviour", but two of its tests pin the receipt block that S1 deletes
- **Where:**
  - `WallRerouteDispatchTests.Receipt_throw_after_launch_leaves_the_task_dispatched` (`:271`)
    asserts `Dispatched==1`, the task `Dispatched` and the wait still `Admitted` with
    `LaunchSessionId` null.
  - `Failed_receipt_cannot_resurrect_a_concurrently_superseded_wait` (`:300`) also depends on
    `ThrowOnReceiptSave` firing after a committed launch.
- **Failure:** under S1 the receipt is part of the claim commit. The interceptor then fails
  the claim itself, so the task does not dispatch and these assertions invert. CP-3 (the whole
  `WallRerouteDispatchTests` class) goes red, or Code edits the tests outside the plan.
- **Fix:**
  - List both tests as rewritten, and state the new contract: a failure of the consume or
    receipt save goes to `AbandonClaimAsync`, and the task stays `Queued` with its reservation.
    This also means a consume exception (including a 40P01 victim) must not reach the tick's
    generic dispatch catch, which fails the task.
  - Update the CP-3 expectation accordingly.

### F4: some witnesses are missing or cannot go red
- **Missing consume sites:** consume runs at three sites, but V-2..V-7 exercise only cold or
  pinned-Worktree launches. No witness covers warm reuse (`:8247`) or standing placement (`:8502`).
  If either consume or its receipt write were deleted, every test would stay green.
  - Add `C1593_warm_reuse_consumes_one_admission` and `C1593_standing_placement_consumes_one_admission`.
    Each asserts `AdmissionCount==1`, `StartAccepted` and `LaunchSessionId==` the reused session.
    Both are red at base: the pre-claim redeem plus the post-commit receipt gives `AdmissionCount`
    1, but the cut fails. Better, assert the rollback variant (a parallel hold after selection),
    which is red at base.
- **Crash cut c5 has no witness:** D-3 relies on c5 (the grant expires during remote
  preparation, the launch consume refuses, the claim returns `NotClaimed`, the reservation is
  re-granted, and one admission results), but no V row covers it.
  - Add a V-4 argument `grant-expired-during-prep`. Advance `FakeTimeProvider` past
    `StalledAdmissionTimeout`, run `GrantReadyAsync`, then the launch tick. Assert
    `AdmissionCount==1` after the regrant and that the ActionKey moved.
- **V-10 cannot go red here:** it has no red at base and no positive control in this plan; its
  reds exist only on parked branches. Name its PC: restore the base `TryRewalkQueuedChainAsync`
  with the S1 claim and expect `40P01`. Do the same for the continuation case.
- **V-8 `routing-resume` red needs a specific setup:** it is red at base only with at least two
  routing-blocked tasks in one pass. The *second* task must own a capacity wait, so that
  supersede has kinds to process and flushes the first task's edits. The test connection must
  hold the *first* task's row. Otherwise the argument passes at base. State this setup in the
  plan.

## Advisories (no Review rerun needed)
- **A1, rollback helper (Q4):** reloading in place adopts concurrent committed changes, so none
  are lost. It discards only the tick's own unsaved edits to other tasks, and there should be
  none. Two changes:
  - Build `AbandonClaimAsync` from `FinishParallelHoldAsync` (`:8004-8020`). That method already
    reloads tracked `AgentTask`, `Agent` and `AgentSession` instances, while D-4 lists tasks only.
    Route the parallel hold through the same helper.
  - The F1 ChangeTracker assertion also gives S1 a cheap Rule P check.
- **A2, grant held through slow work:** the reservation is now held through the claim's
  `git worktree add` and through remote preparation. The default grant expiry is 2×60 s, and
  server2 mirror preparation often takes longer. Expect expiry and re-arm churn and one launch
  per provider per preparation. Record the p95 in V-4.
- **A3, receipts on a reused session:** a warm-reuse or standing receipt binds `SessionId` to a
  live session that already has a transcript. `ObserveTranscriptAsync`
  (`CapacityRecoveryService.cs:718`) confirms or progresses on any later UserPrompt or TurnEnd,
  with no baseline sequence. This is pre-existing. The admission count stays exactly-once, but
  confirmation is not tied to the brief's UserPrompt. Consider recording a baseline sequence
  in S3.
- **A4, exactly-once (Q3):** I1-I4 and cuts c1-c4 hold for cold launch. An enqueue refusal after
  commit (`:6198-6211`) keeps `StartAccepted`/S. Interim-launch recovery
  (`SessionReconciliationService.cs:115/:342`, `AgentSessionLaunchQueue.cs:67`) relaunches S without
  re-admission. A lost commit acknowledgement also counts once. Warm reuse and standing
  placement are argued but not witnessed (F4).
- **A5, residual risk of shipping S1 alone (Q2), once F1 is amended:** no provider->task path
  remains that can deadlock with the claim.
  - L10 cancel is safe at base: no task lock, and nothing is dirty before `:2949`.
  - L16, L17 and L6 remain consume-before-commit leaks, not deadlocks. They are the same C508
    class for standing restarts, queue flush and retained return. They recover through
    `RearmStalledAdmissionsAsync` and spend one attempt each.
  - Without F1, S1 adds the timeout-bounded supervisor cycle.
  - S1 (with D-5) is the smallest fix. A save-before-supersede reroute would lose atomicity
    between the kind change and the supersede.
- **A6, checkpoint tool bypass:** the 67,108,864-byte reader cap is real
  (`scripts/lib/checkpoint-file-reader.ps1:196`). Because the table runs outside the
  checkpoint tool, the Final Review of the code cannot report `reviewedSourceClean: true` from
  sealed receipts. Consider filing a card to raise the cap.
- **Checked and correct:**
  - The line references in L1-L5, L9-L10 and L15-L18.
  - The CP filter grammar (pipes escaped; namespace `Antiphon.Tests.Application` for all CP-3
    classes).
  - The CP-3 count: 15 + 15 + 6 = 36 ≥ 34.
  - The four `C508_GuardRefMismatchWarnedOnce` arguments.
  - The V-1/V-2/V-3 base reds, which are consistent with the pre-claim redeem.
