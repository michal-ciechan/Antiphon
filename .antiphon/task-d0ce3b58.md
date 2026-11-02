# Review d0ce3b58: CARD-1488/1497 terminal task residue (round 1)

Subject: feat/card-task-d4b80a8c, range 6dbd129b791e0ca13b2cff1a0eb5930cd5af53e2..169ed60c103bddddd2f9afcfdf017e5da382b6e1 (Code owner d4b80a8c).
Verdict: CLEAN. No defects (D-n). Four advisories below.

## Priorities

1. **No over-clearing.** `TerminalTaskResidue.RetireAsync` (server/Application/Services/TerminalTaskResidue.cs:27-66)
   - drops waiters with `RepositoryLeaseWaiters.Clear(task.Id)`, which removes only that task id from every repository (RepositoryLeaseWaiters.cs:129-137);
   - cancels the nudge only by its exact recorded `ReportNudgeMessageId` in the task's own `AgentSessionId`;
   - selects wait notes with an exact `ConversationKey == "task-wait:<id:N>"`, plus `SourceTaskId == task.Id`, `Pending` and `DeliveryAttempts == 0`. There is no prefix or LIKE match;
   - writes every cancel through `CancelPendingIfUntypedAsync`, which re-checks `Pending && DeliveryAttempts == 0` under the session lock (SessionMessageQueueService.cs:1327, 1447-1480);
   - matches the CARD-1474 private copy (AgentTaskReplyService.cs:4457) line for line.

   The witness `Cancel_retires_..._only_for_that_task` keeps a sibling's wait note and a human Ui row Pending. Mutant M5 (the filter widened to `StartsWith("task-wait:")`) turned it red on `row.Status should be Pending`.
2. **No under-clearing of the real bug.**
   - Every operator and card cancel reaches `AfterCancelCommitAsync`, because they all go through `AgentTaskService.CancelAsync`: the HTTP endpoint (AgentTaskEndpoints.cs:207) and CardTaskSettlement.cs:100/191, which covers card move/close and the closed-card sweep. That one path covers Queued, Working and Blocked tasks.
   - The other Canceled writers are not entries for a settlement waiter. These are:
     - the dispatcher's Queued-deadline `ExecuteUpdate` (AgentTaskDispatcher.cs:521, 6242). It only handles Queued tasks, and their dispatch waiters are dropped by `ReconcileDispatch` every tick;
     - `ExpireClaimedOptionalWorkAsync` (5403) and the queue service's brief-expiry `ExecuteUpdate` (SessionMessageQueueService.cs:2266). Both handle only Dispatched optional work;
     - the specialist runners and seats, and the channel outbound pump (no worktree);
     - the land supersede of merge helpers (AgentTaskLandService.cs:285). It refuses while the helper's session is live.

     A `WorktreeSettlement` waiter is registered only by a report-time sync of a Working task (RemoteWorkspaceService.cs:808 via TaskCompletionProgressService.PrepareAsync), so none of these misses the 90 s land-yield bug.
   - The dead-session (process-exit) failure goes through `FailAsync` outside a transaction. The witness is green, and mutant M3 (the call removed) turns it red.
3. **Concurrency, ordering and DI.**
   - The helper runs after the Canceled commit and after `gate.Release()`, before the seat release.
   - It is idempotent: `Clear` on an absent id is a no-op, and a non-Pending row returns false. So cancel followed by `FailAsync` is safe.
   - Production wiring is live: AgentTaskService has a single public constructor (AgentTaskService.cs:88), registered with `AddScoped<AgentTaskService>()` (Program.cs:427) beside an unconditional `AddSingleton<RepositoryLeaseWaiters>()` (Program.cs:486). MS DI injects a registered optional parameter, so the fix is not inert. No `new AgentTaskService(` exists in server/ or src/. The dispatcher is resolved the same way (Program.cs:504).
4. **Witnesses.** Run in scratch clones with exact assertion attribution. All probes are diagnostic; every PC stays pending for SourceLanding Mutation.
   - Pre-fix (server/ at 0a4d8643e, tests at the tip): 4/4 red with assertion failures.
   - Batch A (M2: `Clear` by repository; M3: `FailAsync` call removed; M5: wait-note prefix over-match): 3/4 red. `keeps_another` was red (M2), `process_exit` was red (M3) and `only_for_that_task` was red (M5). `Cancel_clears` stayed green, as expected for M2.
   - Batch B (M1: `Clear` removed; M4: nudge cleanup removed): 4/4 red. `Cancel_clears` and `keeps_another` were red on the waiter snapshot (M1); `only_for_that_task` and `process_exit` were red on the nudge status (M4).
5. **Source checks.**
   - Land graph `-Check`: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (matches; not updated).
   - `check-evidence-diff`: commits=4, entries=0, violations=0.
   - `merge-tree` onto origin/master 30cb5279bd4e (moved past 6dbd129b7): clean, tree 512115a0c.
   - `merge-tree` onto origin/rc/2026-11-01 10a826b9d: clean, tree c8c548a00. rc has `RepositoryLeaseWaiters.Clear(Guid)`, `CancelPendingIfUntypedAsync` and `AfterCancelCommitAsync`. The rc compile was not run.

## Checkpoint evidence (tip, receipt validated)

Run 20261102-174359-a9a4 used the plan .antiphon/review-d0ce3b58-plan.md (review-only, ignored). Source: 169ed60c103bddddd2f9afcfdf017e5da382b6e1, state=clean, buildSource=verified. `validate`: CHECKPOINT SOURCE VALID rows=2.

- CP-1: TerminalTaskResidue + DeadSessionReconciliation + CancelReason + ClosingReportNudgeLifecycle + RunnerSyncLeaseFairness + RepositoryLeaseWaiters. executed=53, passed=53, failed=0, skipped=0.
- CP-2: TerminalRunnerSeatReleaseTests + CapacityLockOrderTests (the Cancel post-commit siblings). executed=58, passed=58, failed=0, skipped=0.
- Wall time was 15m59s with 1 build. Slowest class: TerminalRunnerSeatReleaseTests at 264 s.

Unlisted runs, with reasons: three scratch-clone `Antiphon.Tests` builds and their runs, all through build-slot (pre-fix red, mutant batch A, mutant batch B), plus one build that failed on a mutant compile error and was fixed. Also the LandGraphAudit evaluator build that `-Check` needs. All scratch clones and `bin-land-audit` directories were deleted.

Scope: the commissioned named-class selection ran in full. The whole Unit lane was not run, because the brief commissioned the named classes only and there is no CARD-1488 plan Unit lane. So `ordinaryScopeCompleted` is Interim.

## Advisories

- **A-1 (follow-up, known).** A sync that registers its waiter after the cancel's `Clear` (it read the task as Working just before the cancel) and then ends with `LeaseWaiting` still leaves a waiter until restart. The window is narrow, and the cost is bounded at 90 s per land. The dispatcher-tick reconcile of waiters whose task is terminal or deleted closes it.
- **A-2 (changed input wait).** When the parent has a pending wait note, `RetireAsync` waits, with `CancellationToken.None`, on the parent's queue lock. A delivery holds that lock through its confirm poll (`TranscriptConfirmTimeoutSeconds`, default 30 s). The wait is released when that delivery's confirm poll ends, within about 30 s. Until then the cancel HTTP response, or the dispatcher sweep on `FailAsync`, is delayed. Parking plays no part. This is the same bounded lock-wait already noted on CARD-1497.
- **A-3.** Failed paths outside `FailAsync` do not call the helper. These are `TryFailArm2ConditionalAsync` (AgentTaskDispatcher.cs:7959, Dispatched only, so no nudge or waiter), AgentTaskReplyService `FailUnreportedTurnAsync` and line 2086 (owned by CARD-1503), and CheckCompactionContinuationService.cs:670 (Check seat). A parent wait note can outlive the AgentTaskReplyService Failed paths. Fold them into `TerminalTaskResidue` once CARD-1503 lands.
- **A-4.** There is no Program-level DI witness. The wiring was confirmed by reading the code, and test harnesses register the waiters explicitly.
