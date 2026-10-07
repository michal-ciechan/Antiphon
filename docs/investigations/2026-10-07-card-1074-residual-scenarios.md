# CARD-1074 residual scenarios

Date: 2026-10-07. Investigate only. No production code was edited. The Unit lane was not run and is out of scope.

Tree: worktree `feat/card-task-4c391558` at `d7d5a060d81d548ea3597b901830b4167f3c403d` (the plan commit). `git fetch` during this investigation put `origin/master` at `b5e78700ae9a76430c13d75cc03399055dd82e59`. The three commits in between are CARD-1105 compose-secret docs and fixes. `git diff d7d5a060..b5e78700` is empty for `AgentTaskDispatcher.cs`, `AgentSessionService.cs`, `SessionReconciliationService.cs`, `SessionMessageQueueService.cs`, `AgentSessionLaunchQueue.cs`, `CheckCompactionAdmission.cs`, `SessionReconciliationSettings.cs`, and `DelegationSettings.cs`. The measurements below are the current production behavior of those files.

The two original CARD-1074 defects stay fixed by `6e04c87f38e9becc1cd5d978c05b4446da04e19a`. This note does not re-run V-1, V-2, or V-3. It measures the two residuals the plan left open, plus follow-up preservation, the CARD-0079 / stall interaction, and statement counts.

## Reproduction

Throwaway class `DelegationDispatchRecoveryBoundaryTests` (not committed). Real `AgentTaskDispatcher.TickAsync` produced the claim. Real `SessionReconciliationService.ScanAsync` plus `AgentSessionLaunchQueue.ResumeInterrupted` produced discovery. A missing brief was an injected `IOException` on the first `SessionQueuedMessages` INSERT during that dispatch, swallowed by the brief catch. A crash after `Running` was an `OperationCanceledException` from `IEventBus` on `SessionStarted`, which the resume catch does not handle. Isolated schema per world, `UseAppHost=false`, `TUNIT_MAX_PARALLEL_TESTS=1`, filter `/*/*/DelegationDispatchRecoveryBoundaryTests/*`.

Measuring run: total 6, failed 2, succeeded 4, skipped 0, about 68s. The two failures are throwaway assertions that expected both queue rows to be `Sent` inside the one resume flush. The `C1074-OBS` line is written before those asserts and is the measurement. The four passes are the absent-process path, the three discovery gates, the Working-session tick, and the statement counts.

## What already holds

A refused launch enqueue no longer fails the task at dispatch time. `AgentTaskDispatcher.cs:5277-5290` keeps the committed claim, writes the warning `launch enqueue refused after the committed claim ... awaits interrupted-launch recovery`, and still persists the brief. Observed immediately after that tick, with no runner process: task `Dispatched`, session `Starting`, one `Pending` delegation brief, that warning, zero kills.

When a runner process is listed `Running`, generation matches, `Pending` is empty, and this process does not own the launch, discovery does resume. `SessionReconciliationService.ResumeInterruptedLaunchesAsync` (`:287-328`) calls `ResumeInterrupted`. For a brief that dispatch already persisted, the session becomes `Running`, the task stays `Dispatched`, the brief becomes `Sent` once, one UserPrompt is recorded, and the warning is `launch resumed after a server restart...`, not `brief re-queued`. SQL for that scan-plus-resume was 63. Zero delegate-stopper kills, runner kills, and compaction stops.

A follow-up enqueued as `QueuedMessageOrigin.Ui` with `deliverIfIdle: false` during the gap stayed a single row. It was not duplicated. `FlushSessionAsync` (`SessionMessageQueueService.cs:1661-1674`) delivers the head message only, so the other row was still `Pending` when resume returned. On the already-persisted-brief path the brief was the head and was `Sent`; the follow-up stayed `Pending`. On the missing-brief path the follow-up was queued first, so it was `Sent` and the re-queued brief stayed `Pending`, with the warning `brief re-queued: the interrupted dispatch died before its brief row was persisted`. The throwaway stopped on the status assert before it compared the follow-up body bytes. Nothing else writes a Ui row, and delivery does not rewrite `Body`.

Gates, all observed, all left the brief `Pending`, the task `Dispatched`, the session `Starting`, and issued no kill and no submit:

- Runner `ListAsync` throws, including five minutes past grace. Unavailable inventory is not a close and not a resume (`SessionReconciliationService.cs:214-216` and the list-failure skip above the close). Acceptable.
- Runner lists `Running` with a different `AcceptedStartedAt`. `GenerationMatches` (`:313`) refuses resume, and the row is not closed because the runner does know the process. Acceptable safety limit. The brief is retained. The task can sit `Dispatched`. The delivery watchdog's Starting deferral (`AgentTaskDispatcher.cs:2062-2093`) does not check generation, so a listed `Running` Starting session is also deferred there.
- This process already owns the launch (`TryRegister` before the scan). `Owns` (`:314`) skips resume. Acceptable.

A Working task whose session is `Running` and whose runner lists that process: scan, direct `ResumeInterruptedLaunchAsync` (returns at `AgentSessionService.cs:826` because status is not `Starting`), and one dispatcher tick. The task stayed `Working`, the session stayed `Running`, `IsWorkingAsync` stayed true, and stopper kills, runner kills, and compaction stops stayed zero. That tick's statement count was 18, the same as a held `Dispatched` tick. The stall sweep is on that tick and did not kill. CARD-0079 was not invoked (`CheckCompactionAdmission.BlocksGenericLaunchResumeAsync`, `:60-73`, is the generic-resume block for an unresolved compaction episode; this run seeded none). An empty-runner Working session was not reproduced. The unknown-session close (`SessionReconciliationService.cs:242-247`) is a database status write, not `KillAsync`.

## Defect A — no runner process. Filed as CARD-1149

Same refused dispatch as above. Reconciliation does not resume, because pass 1c requires the runner to list the session (`:306-307`). Inside `StartingGraceMs` (90_000, `SessionReconciliationSettings.cs:27`) the scan leaves `Starting`. Statement count 8. After 91s the next scan marks the session `Failed` with `Session runner does not know this session (the launch failed, the runner restarted, or the server restarted before the launch reached the runner).` (`SessionReconciliationService.cs:242-247`). The task is still `Dispatched`, the brief is still `Pending`, and there is no kill. That close scan was 6 statements.

`FailDeadSessionTasksAsync` (`AgentTaskDispatcher.cs:2467`) is armed only when both the runner client and `DeadSessionFirstSeenState` exist (`:2471-2472`). It never kills (`:2441`). The first tick after the close records first-seen and leaves the task `Dispatched` (18 statements, same as a held tick). After `DeadSessionFailGraceMinutes` (3, `DelegationSettings.cs:571`) the next tick fails the task: `Session died before the task settled: its session is Failed (Session runner does not know this session...). No report is coming; read session <id> before re-running this task.` That tick was 24 statements. The brief row was still `Pending`, nothing was submitted, and the goal spill still contained the goal. Stopper kills, runner kills, and compaction stops stayed zero.

Verdict: DEFECT. The work was never attempted. The brief bytes survived. The task is terminal `Failed`, not re-queued and not `Blocked`. The reason is visible, so it is not silent. That is the outcome the plan rejected for definitely unattempted work.

## Defect B — crash after Running, before the brief is durable. Filed as CARD-1150

The dispatch brief catch (`AgentTaskDispatcher.cs:5310-5319`) swallows any non-cancellation exception from `EnqueueAsync` and writes a log line only. It does not write an `AgentTaskEvent`, and it assumes the row was persisted. Injecting `IOException` on the INSERT (Npgsql runs `INSERT ... RETURNING` as a reader) produced: task `Dispatched`, session `Starting`, zero brief rows, zero warnings.

Discovery then saw a matching `Running` process and entered `ResumeInterruptedLaunchAsync`. That method saves `Status = Running` (`AgentSessionService.cs:871-873`) and publishes `SessionStarted` (`:879-884`) before the backfill query (`:886-919`). `OperationCanceledException` on that publish is excluded from the catch at `:945`, so the catch does not kill and does not call `FailInterruptedLaunchAsync`. Observed: session `Running`, task `Dispatched`, zero briefs, zero submits, zero kills. SQL for that scan-plus-resume was 20.

A second discovery scan does not select a `Running` row (`ResumeInterruptedLaunchesAsync` filters `Starting`, `:295`). A direct `ResumeInterruptedLaunchAsync` returns at `:826-827`. Observed again: still `Running`, still `Dispatched`, still zero briefs.

The delivery watchdog `FailNeverStartedAsync` (`AgentTaskDispatcher.cs:2021`) uses `DeliveryFailTimeoutMinutes` (10, `DelegationSettings.cs:393`). Its Starting deferral (`:2062-2093`) does not apply once the session is `Running`. After 11 minutes on the harness clock the tick failed the task: `Boot prompt was never delivered: 10 minutes after dispatch the session wrote no turn prompt of either kind for this task (no brief was queued for this task after dispatch).` `IDelegateSessionStopper.KillAsync` ran once (`:2301`). The session's failure reason became `Killed by the delivery watchdog: ...`. Runner `KillAsync` and compaction stops stayed zero. The session was not Working, so the withhold at `:2282-2286` did not apply. SQL for that tick was 28. This kill is the delivery watchdog, not CARD-0079 and not the stall rule.

Verdict: DEFECT. The brief is not recoverable. CARD-0340 cannot re-enter after `Running` is committed. The task is not re-launched and not `Blocked`. It becomes terminal `Failed`, and a non-working session is killed.

### Cut after the brief insert, before flush

The same missing-brief dispatch, then a follow-up, then an `OperationCanceledException` on the next database command after the backfill INSERT. The INSERT commits inside `EnqueueAsync` before `ReadWorkingAsync` (`SessionMessageQueueService.cs:790` then `:848`). The exception escapes the resume catch the same way. Observed: session `Running`, task `Dispatched`, one `Pending` delegation brief, one `Pending` follow-up, nothing submitted, no UserPrompt, zero kills. The goal spill contained the goal. No second discovery flush. The +11 minute watchdog tick was not run for this cut. The same arm treats a still-`Pending` brief as never typed (`AgentTaskDispatcher.cs:2136` and `:2155-2207`) and then kills unless the session is Working (`:2282-2301`) or a bind-refusal recovery withholds it. `TryRecoverBindRefusalAsync` returns none when reply services are absent (`AgentTaskDispatcher.cs:6569-6573`; this fixture did not register them). That later kill is cited, not reproduced, for this cut.

### No-fault companion

Same missing-brief dispatch and the same follow-up, with resume allowed to finish. Discovery backfilled one brief (`brief re-queued...`) and the one flush sent the earlier follow-up (`Sent`, one submit, one UserPrompt). The brief row was still `Pending`. Session `Running`, task `Dispatched`, zero kills. SQL 73. This is the FIFO head-of-queue flush, not a lost or duplicated brief. It also shows the backfill works when `ResumeInterruptedLaunchAsync` is allowed to reach it, which is what the `Running` gate prevents in the crash cut.

## Statement counts

Counted with a `DbCommandInterceptor` on reader, non-query, and scalar, sync and async. These are one-shot recovery or tick totals, not a new steady-state query.

| Phase | Statements |
|---|---|
| First tick, which dispatches the queued task (not an empty tick) | 45 |
| Later tick while the task is already `Dispatched` / `Starting` | 18 |
| Reconciliation scan inside the 90s grace, runner absent | 8 |
| Reconciliation scan that closes the unknown session | 6 |
| First dead-session tick (record only) | 18 |
| Dead-session tick that fails the task | 24 |
| Discovery plus resume that delivers an existing brief | 63 |
| Discovery plus resume that crashes before the backfill | 20 |
| Discovery plus resume that backfills and flushes | 73 |
| Delivery-watchdog tick that fails and kills | 28 |
| Tick of a Working session the runner still lists | 18 |

The held tick stays 18 with a pending brief and with a Working session. The inside-grace scan stays 8 and does not resume. The extra statements appear only inside the resume invocation. Backfill reads live in `ResumeInterruptedLaunchAsync` (`:886-900`), not in `TickAsync`. A later repair's acceptance is that the held tick and the inside-grace scan do not grow. A tick with no queued and no open task was not measured.

## Verdict

Do not close CARD-1074 as fully done. `6e04c87f38` fixed the two original defects: a refused enqueue keeps the claim and the brief, and a resume that is allowed to run backfills a missing brief once. Two residuals are defects:

- CARD-1149. No runner process. The brief survives and the task becomes terminal `Failed` through reconciliation plus the dead-session sweep. Not `Blocked`, not re-queued, not silent, not killed.
- CARD-1150. After `Running` is committed, CARD-0340 cannot re-enter. A crash before the brief row leaves zero rows; the delivery watchdog then fails the task and kills the non-working session. A crash after the row commits leaves the bytes `Pending` and untyped.

The live-runner path, the unavailable-inventory hold, the generation mismatch hold, the owned-launch hold, and a Working session the runner still lists are not defects.

Not done, noted: keep the absent-process obligation retryable or visibly Blocked with the original brief, and make the post-Running pre-brief window recoverable (durable brief before `Running` is published, or one backfill while the task is still `Dispatched` and no brief evidence exists), with no added per-tick query and no kill of a Working session; one code slice covering CARD-1149 and CARD-1150.
