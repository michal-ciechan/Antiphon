# Blocked complete Code tasks hold server2 seats

Investigate task 12ea19bf, 2026-11-09. Read-only: nothing was canceled, replied to, released or changed.
Base master `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`. Evidence came from `GET /api/agent-tasks/{id}`,
`GET /api/agent-tasks?projectId=…`, `GET /api/agent-tasks/pipeline`, `GET /api/attention`, `git ls-remote`
and the source at the base.

## Verdict

**Confirmed, by design, plus a policy contradiction.** Each task's last settlement was a complete report.
Two things went wrong at that settlement:

1. The desktop canonical repository's mutation lease was busy for the whole 120 s runner-sync budget.
   The lease was busy *during the observation fetch*, and the reported tip was not yet in the desktop's
   object store.
2. The settlement code treats exactly that case as "unfetched Code": it settles the task **Blocked/Decide**,
   writes **no sync-debt row** (so no sweep ever retries it), and asks for a reply. A Blocked task keeps
   its session while parking is off, so the seat is held until a human replies or cancels.

The standing rule 6 (CARD-1469, 2026-10-22) says never to Reply here, because "the settlement sync sweep
clears the block". That premise is false for this arm: there is no debt row for the sweep to retry. So
every task in this class now waits forever.

This is answer (d) to the brief's question. (a) is only the trigger: the lease is not stuck, it was just
busy for 120 s once. (b) is false: nothing waits for Review or a land. (c) is a consequence, not the cause.

## Mechanism (file:line at d2ff7e8d4)

1. `server/Infrastructure/Git/TaskProgressGit.cs:127-145`. `ObserveExactRefAsync` reads the tip with
   `ls-remote`. When `cat-file -e <tip>` misses locally it needs the repository lease to fetch. A busy
   lease returns `Unavailable("repository_lease_busy")` and carries the advertised tip.
2. `server/Application/Services/RemoteWorkspaceService.cs:711-716`. The observation is retried inside
   `WhileLeaseBusyAsync` until `SyncBudget` is spent (`Delegation:RunnerSyncBudgetSeconds`, default 120,
   `DelegationSettings.cs:727`). At `:804-805` a still-busy observation becomes
   `Unavailable(runner_sync_lease_busy)` **with `SourceDescends = null`**. Compare `:853-856`: a busy lease
   at the *later* acquire step, after the fetch succeeded, gets `SourceDescends = true`.
3. `server/Application/Services/SettlementSyncDebtPolicy.cs:100-109`. `Classify` marks it `Pending`
   anyway: the reason is lease-busy, the SHA is full and the ref is owned.
4. `server/Application/Services/TaskCompletionProgressService.cs:223-268`. The Pending arm needs
   `SourceDescends == true` and the tip present locally. Neither holds, so it returns
   `Unknown(runner_sync_lease_busy)` at **:268** (assessment Indeterminate).
5. `SettlementSyncDebtPolicy.cs:47-54`. `BlockReason` returns `LeaseBusy` for a Pending Code task with
   Indeterminate evidence (CARD-1113, `319236fef`).
6. `server/Application/Services/AgentTaskReplyService.cs:1248-1250`. A Succeeded status becomes
   **Blocked**. At `:1291-1298` it sets `NextStage=Decide` and the handoff "Runner sync blocked
   (runner_sync_lease_busy): … then reply to this task for a fresh completion report."
7. `AgentTaskReplyService.cs:1566-1594`. The debt row is written only when `remoteBlock is null`, so it is
   **never written** here. `SettlementSyncRecoveryService.SweepAsync` (`:38-45`) reads only
   `AgentTaskSyncDebts`, so nothing ever retries the task.
8. Seat. `AgentTaskReplyService.cs:1558-1561` routes a Blocked runner task to
   `TerminalRunnerSeatReleaseService.TryHandleTaskAsync`. That returns `true` without acting when
   `BlockedTaskParking:Enabled` is false (`TerminalRunnerSeatReleaseService.cs:76-78`). The pool idle
   retirement also skips these seats (`AgentTaskDispatcher.cs:9419-9430`). The session stays
   `Running, idle`.

`docs/orchestration-loop.md:1866` documents this as intended: "Dirty, diverged, and unfetched Code stay
Blocked with no debt row … Pending evidence without a debt row means the task stayed Blocked and a reply
is required". Rule 6 at `docs/orchestration-loop.md:651` says the opposite.

## The four tasks (stored rows)

| Task | Card | Kind | Settled | Last verdict | Why Blocked | origin tip = report tip | Mirror |
|---|---|---|---|---|---|---|---|
| 2b953d03 | CARD-1514 | Grok | 2026-11-07 19:43:35Z | **blocked** (asks to authorize `audit-land-verification-graph.ps1 -Update`) | Its own question (`blocked.reason=marked-blocked`). The earlier done report at 17:18:46Z was lease-busy-blocked, then got a round-2 reply at 18:31 with new work | `feat/card-task-2b953d03` 036bbcba0 (reviewed); `-master` 1705a3ee8 = report tip | clean at 1705a3ee8 |
| 83c6ade8 | CARD-1743 | Grok | 2026-11-07 19:28:44Z | done | lease-busy arm | 9f6466120 = report | clean |
| 317b5f73 | CARD-1731 | Grok | 2026-11-08 00:24:03Z | done | lease-busy arm | cf0aba324 = report | clean |
| 0ed344bc | CARD-1790 | ClaudeCode | 2026-11-08 18:23:32Z | done | lease-busy arm (**fourth member, not in the brief**) | 071665186 = report | clean |

Each lease-busy row carries `progressEvidence.remoteSync = {state: Pending, reason: runner_sync_lease_busy,
observedSha: <tip>, confirmedSha: null, mirrorPushed: false}`, `assessment: Indeterminate`, `syncDebt: null`,
and the event "Runner sync unavailable: runner_sync_lease_busy … reply to this task for a fresh completion
report". Every commit not on master, by patch-id (`git cherry origin/master <tip>`), is a report or docs
commit: 2b953d03-master c74dc8c8c and 1705a3ee8, 83c6ade8 9f6466120, 0ed344bc 071665186; 317b5f73 has none.
The implementation landed through Merge batches (for example `6d590a513` CARD-1743 and `e79c0e03b` CARD-1790).

**Lease holder: unidentified.** The lease is the *desktop* repository mutation lease, not a server2 lease,
and it is busy only at the moment of settlement. It is not stuck. In the same window 37 tasks settled
Succeeded with a Pending debt that reached `settlement_sync_ready`, for example 47538c88 at 11-07 20:25Z
and c3f172ad at 11-08 19:35Z. 0ed344bc blocked 21 s after Merge batch 132 (bee64303) settled at 18:23:11Z.
The other three have no task-level land within ±4 min. Naming the holder needs the desktop server log
lines for the repository lease at those four timestamps (see docs/logs.md). That log is not reachable from
this runner.

## Fleet scope (Antiphon project, read 2026-11-09 ~01:07Z)

Blocked: 5 tasks, all Code on server2.

- **This class, 4 tasks:** 83c6ade8 (29.6 h), 317b5f73 (24.7 h), 0ed344bc (6.7 h), and 2b953d03
  (29.4 h; it entered this class first and is now a stale question). Each has a complete marked report and
  a live idle session.
- **Different class, 1 task:** 48473acc (CARD-1783). "Repeat of task 18dd27d8 (AuthenticationRequired) is
  blocked; no Grok process or worktree was started." It has no session and holds no seat.

Census of all 805 Code tasks on server2/server2-temp since 2026-10-15, one `GET /api/agent-tasks/{id}` each:

- 59 tasks got "Runner sync unavailable: runner_sync_lease_busy" (the Blocked arm).
- 39 tasks got "Runner sync pending: runner_sync_lease_busy" and settled **Succeeded (37) / Failed (2)**
  with a debt row. That is the fetched-then-lease-busy arm (`SourceDescends=true`); their debts went
  Ready (34) or Superseded (3). This is the contrast the brief asked for.

## Regression analysis

Code: **not a regression.** The Blocked arm was built and pinned on purpose:

- `3d4bf7c3` and `cff20cb13` (CARD-1082, 2026-10-06): observation lease-busy carries the tip, and Pending
  progress without local objects is `Unknown(LeaseBusy)`.
- `319236fef` (CARD-1113, 2026-10-07): "fail closed without progress evidence", so the Pending Code arm blocks.
- `80db50749` (CARD-1082, 2026-10-07): the documented sentence "Pending evidence without a debt row …
  a reply is required."

Policy: **regression, on 2026-10-22 10:15Z.** `e22361575` (CARD-1469) added rule 6, "never Reply to a
complete report Blocked only by runner sync … the settlement sync sweep clears the block". The census split
at that commit:

| Lease-busy-Blocked events | Outcome |
|---|---|
| Before rule 6 (24) | 24 replied (21 Succeeded, 3 Failed). Example: e10fbce9 replied 52 s after its block and ended Succeeded. |
| After rule 6 (35) | 25 Canceled by hand, hours to 36 h later (for example f9e4bfe6 canceled at 22:09 on 10-22; 109f308b and 5b5af094 at 10-26 13:26). 6 replied and Succeeded; the last was b1ceb641 on 11-07 (replied 12:50Z). **4 still Blocked**, the first being 2b953d03's done report at 11-07 17:18Z. |

Since 2026-10-22 no task in this class has settled on its own.

**What should have caught it**

- `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries` (`tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs:586`)
  pins Blocked, no debt and a "then reply" warning, and then *replies*. Recovery by reply is assumed by
  construction.
- `SettlementSyncDebtPolicyTests.C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason` and
  `C1119_PendingCodeIndeterminateCarriesEvaluatedReason` pin the block reason, not its exit.
- `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` (`:647`) does the same.
- `RunnerBranchContractDocumentationTests` (`:91`, `:272-273`, `:318`) pins that the "reply is required"
  sentences *exist*. No test checks them against rule 6.
- No test pins "a complete report on a runner never holds a seat without an owner that will clear it",
  and no Review of CARD-1469 checked rule 6 against the CARD-1082/1113 contract.

## Impact

- **Seats.** server2 shows `inFlight 10 / effectiveLimit 10, remaining 0` (`GET /api/agent-tasks/pipeline`
  hosts). That is 4 idle Blocked sessions, 1 Working, and 5 just-Dispatched. Two tasks are Queued on
  `hostBudget` (afa927c5 CARD-1798, 121cbdd3 CARD-1800 plan), so about 40 % of server2 is held by finished work.
- **Accounting.** The project concurrency scope does *not* count them: Code shows `parallel 4`, and the
  stage lists the 5 Blocked tasks separately. The host budget *does* count them, so per-stage remaining
  slots overstate what can dispatch.
- **Alerts did fire and nobody acted.**
  - `SeatIdle` Error ("Blocked for 1778.5 min", CARD-1079, 180-minute threshold) for all four.
  - `BlockedQuestion` Critical "waiting on a human answer" for all four.
  - `LandOutcomeUnconfirmed` "Task completion notification: AwaitingReceipt; queued; caller idle".
  - `CallerNoteUndelivered` "Check note still Pending on caller session b2f1a869 after 38h57m".

  The caller orchestrator session b2f1a869 never received the completion notes. The missing piece is
  escalation and ownership, not detection.

## Sanctioned cleanup (not executed)

Cancel each task with `POST /api/agent-tasks/{full-id}/cancel`. The route takes a GUID only and no body.
`delegate.ps1` has no `-Cancel` switch. Run it with the caller's credentials:

```
curl -fsS -X POST -H "X-Antiphon-Task-Token: <caller token>" "$ANTIPHON_API/api/agent-tasks/83c6ade8-c5ed-47f6-9563-c4df58cf0663/cancel"
curl -fsS -X POST -H "X-Antiphon-Task-Token: <caller token>" "$ANTIPHON_API/api/agent-tasks/317b5f73-0ba2-48e5-bfdd-ad545cdc8944/cancel"
curl -fsS -X POST -H "X-Antiphon-Task-Token: <caller token>" "$ANTIPHON_API/api/agent-tasks/0ed344bc-54fc-4dd7-b0c7-e30338128e16/cancel"
curl -fsS -X POST -H "X-Antiphon-Task-Token: <caller token>" "$ANTIPHON_API/api/agent-tasks/2b953d03-b2da-477e-aa70-d978254c8d14/cancel"
```

What a cancel does (`AgentTaskService.CancelAsync`, `server/Application/Services/AgentTaskService.cs:3083-3156`):

- It kills the session first (`StopDelegateAsync`, `:4593`) and removes the ephemeral agent.
- It sets `Canceled` and keeps `Result`, the stored report.
- It retires residue (lease waiters, nudges), runs `TerminalRunnerSeatReleaseService.TryHandleTaskAsync`
  (seat back) and releases workspace consumers.
- It does not touch origin branches. The desktop worktree and the runner mirror go through the normal
  retirement of a Canceled task.

Refusals:

- 409 "has already finished" if the task is already settled.
- 409 "The task attempt changed before cancellation" if the attempt or session moved.

**No work is lost.** Every origin branch equals its report tip, every server2 mirror is clean at that tip,
and everything not on master is a report or docs commit. Do not use Reply: rule 6 forbids it, and it
would spend a new model turn for work already landed. Do not use `-RecoverReviewedSource`: it is for a
Review subject, not this. The 25 Canceled tasks from 10-22 to 10-26 are the precedent.

## Card A: a complete runner report never waits on a reply for lease contention

A runner Code task whose complete report settles while the desktop repository lease is busy *during the
observation fetch* is Blocked/Decide with no `AgentTaskSyncDebts` row:

- `RemoteWorkspaceService.cs:804` sets `SourceDescends=null`.
- `TaskCompletionProgressService.cs:268` returns `Unknown(LeaseBusy)`.
- `SettlementSyncDebtPolicy.cs:53-54` blocks.
- `AgentTaskReplyService.cs:1567` skips the debt row.

Nothing retries it. The seat stays held while parking is off, and rule 6 forbids the Reply the warning
asks for. Four tasks have held 4 of 10 server2 seats for 6-30 h. Since 2026-10-22, 25 more were canceled
by hand.

Acceptance:

- (1) A lease-busy observation with a full owned-ref tip leaves the task open for a re-attempt, or mints a
  retryable record, instead of minting a terminal Blocked. One option: treat a spent observation like
  `LeaseWaiting` and keep re-attempting within a bounded total.
- (2) A complete report never holds a seat with no owner that clears it. It ends Succeeded/Failed with a
  debt row, or the seat is released.
- (3) Rule 6 in docs/orchestration-loop.md and the "Pending evidence without a debt row … reply is required"
  sentence (:1866, delegate.ps1:910, ops-http.md:179) agree. A documentation test pins the agreement.
- (4) A witness modelled on `Sync_uncertainty_blocks_and_reply_retries` reaches a settled state **without**
  a reply.

Scope: the settlement area (SettlementSyncDebtPolicy, TaskCompletionProgressService, RemoteWorkspaceService,
AgentTaskReplyService) and docs/orchestration-loop.md.

## Card B: a Blocked seat past N hours escalates to an owner

`SeatIdle` (CARD-1079) and `BlockedQuestion` already fire for a Blocked seat, and `CallerNoteUndelivered`
shows the caller (b2f1a869) is idle. They are attention rows only: four seats stayed held for up to 30 h
with Error/Critical rows and no action.

Add escalation:

- When a seat-holding Blocked task passes `Attention:SeatIdleErrorMinutes` × k (or a new N-hour setting)
  *and* its caller note is undelivered, the condition reaches the operator channel, and the keep-six/keep-seven
  reminder names "N seats held by Blocked tasks".
- Show the host-budget gap: pipeline `hosts.inFlight` minus Dispatched/Working is already
  `OccupancyDivergence`. Surface it in the shortfall the orchestrator reads.

Detection only. It must not kill, park or release anything.

## Not done, noted

- Fix idea (one line): in `RemoteWorkspaceService` fold a spent *observation* lease into the existing
  `LeaseWaiting` re-hand path, or mint a debt row that does its own fetch, so `remoteBlock` is never
  LeaseBusy for a full owned tip.
- Open question: who held the desktop lease at 17:18:46Z, 19:28:44Z, 00:24:03Z and 18:23:32Z. Desktop
  server log lines would resolve it.
