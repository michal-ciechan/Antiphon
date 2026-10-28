# CARD-1593 leak 5: quota settle and transcript fence lock order (plan)

Task e95ecc01, Plan stage, server2/Linux. Base `origin/rc/2026-10-27` =
`aa6dfd24a51db7c02e8722a05a21817bf4df407a`. All line numbers below are at that base. The five
files this plan touches or tests (`server/Infrastructure/Data/AppDbContext.cs`,
`server/Application/Services/CapacityRecoveryService.cs`,
`server/Application/Services/AgentTaskReplyService.cs`,
`tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`,
`tests/Antiphon.Tests/TestHelpers/CapacityLockProbe.cs`) are byte-identical on
`origin/master` `fe73cfd2e` (`git diff --stat origin/master HEAD -- <file>` is empty for each). The
line numbers therefore hold on both lines.

Inputs read:
- Investigation bcb5bc45, item 5 (`docs/investigations/2026-10-28-card-1593-capacity-leaks.md` on
  `origin/feat/card-task-bcb5bc45`).
- The S1 plan `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md`
  (this tree's copy, which is newer than the one on `feat/card-task-d94003bc`), in particular D-1,
  Rule P, Rule X, rows L9, L10, L15, L23, L24 and L25, and S2.

No product code is changed by this task.

## Problem

The S1 rank order (D-1) is: card 0, counted lane 1, owner 2, task 3, release/session 4, physical
agent 5, parallel 6, provider 7, capacity rows 8. Rule P says that while rank 7 is held, a
transaction writes only rank-8 rows and rows it locked earlier.

Two sites break this order.

1. **L23, quota settle.** `HandleApiErrorTurnAsync` dirties the task
   (`AgentTaskReplyService.cs:1972-1979`) and the `ApiErrorRecoveries` row (`:1980-1988`), with no
   transaction open. It then calls `SupersedeTaskWaitsOnAsync` (`:1991`). That method opens its own
   transaction (`CapacityRecoveryService.cs:502-503`) and takes the provider advisory (`:506`). Its
   `SaveChangesAsync` (`:533`) then flushes the dirty task. The resulting order depends on scope:
   - Unfenced scope (`OnTurnEndAsync`, the delivery watchdog, the API-error sweep): provider 7,
     then task 3.
   - Fenced scope (`OnTranscriptActionAsync`, `AgentTaskReplyService.cs:121`): the same save first
     runs the fence, which locks the session (`AppDbContext.cs:163`) and then the task (`:164`).
     The order is provider 7, session 4, task 3.
2. **L24, the fence itself.** Every fenced save takes session 4 and then task 3. That is the reverse
   of the codebase convention of task before session. Examples:
   - `BlockedTaskParkingService.cs:90`: "Same task-before-session lock order as CARD-0667".
   - `TaskParkPublicationService.cs:392-393`.
   - `TerminalRunnerSeatReleaseService.cs:872-878`.
   - `AgentTaskService.cs:3634-3642` (`LockReleasedAnswerAsync`).

This plan found a third site, **L23b, wall reroute (new).** `RequeueOnWallAsync` sets
`task.AgentKind`, `ModelLevel` and `FailureReason` (`AgentTaskService.cs:3406-3408`). It also adds an
`AgentIncident` (`:3391-3402`). It then calls `RequeueAsync` (`:3409`), which reaches
`RequeueCoreAsync` and its supersede at `:3852`. Between the edits and the supersede no save runs:
`AdmitWorkspaceAsync` and `StopDelegateAsync` (`:4415-4430`, a runner kill) do not save. The
supersede's owned transaction therefore flushes the dirty task under the provider, which is the L3
base shape. In the fenced scope it also runs the fence under the provider. The settle reaches this
path for a list-governed task on a non-quota wall (`AgentTaskReplyService.cs:2002-2016`), through
the scope's own `AgentTaskService`, which shares the fenced `AppDbContext`. The S1 table's L9 row
("conforms while nothing is dirty at `:3845`") does not cover this dirty caller.

## Ground truth (card/investigation assumption vs code at `aa6dfd24a`)

| Assumption | What the code does |
|---|---|
| Quota settle takes the provider, then the session, then the task (investigation item 5) | Confirmed for the fenced scope (`CapacityRecoveryService.cs:506` -> `:533` -> `AppDbContext.cs:163-164`). In the unfenced scope it is provider then task (no fence). Both are Rule P breaks. |
| Swapping the fence order fixes the quota deadlock | **No.** After the swap the fenced order is provider 7, task 3, session 4, which is still a provider-first wait. The unfenced path does not touch the fence at all. The swap fixes only L24. |
| The supersede always takes a provider lock | **No.** `kinds` (`:496`) is read from every wait of the task, finished or not. With no wait row at all, the method takes no lock and saves nothing. The quota settle then saves in the final fenced save (`AgentTaskReplyService.cs:1993`) with no provider held. The provider lock is reached only when the task has any `CapacityRecoveryWaits` row. |
| The settle holds DB locks when it calls `ApiErrorRecoveryService.EnsureAdoptedAsync` (`:1924`) | No. That service opens its own scope and transaction (`ApiErrorRecoveryService.cs:88-89`, `:102`) while the settle context holds no transaction. Rule X holds. Its own supersede (`:543`) runs with no ranked lock held before it (`ApplyWallAsync` `:458-541` does reads, then the hold upsert with `saveChanges: false`). |
| The fence is used by several callers | **One producer**: `AgentTaskReplyService.cs:121` is the only writer of `RuntimeActionTaskFence` in `server/` and `tests/` (grep). Its reach is wide, though: every `SaveChangesAsync` on that scope's `AppDbContext` during `JudgeOpenTaskAsync` (`:124`) is fenced. That includes scoped services resolved from `scope.ServiceProvider`, for example `AgentTaskService` (`Program.cs:427`, scoped), so the L23b requeue is fenced too. |
| Postgres cannot see this deadlock | **No.** Both sides are single connections waiting on lock-manager locks: row locks and `pg_advisory_xact_lock`. The deadlock detector covers both, so a cycle is a `40P01` after `deadlock_timeout` (default 1 s), not a hang. |
| The deadlock happens in production | **Not observed.** Read-only query of the desktop Postgres (`antiphon.desktop.codeperf.net:17280`, 2026-10-28 15:44Z): 18 quota-blocked settles (events `Provider quota exceeded for%`, 2026-10-19 to 2026-10-22, 18 tasks), and **0** of those tasks had any `CapacityRecoveryWaits` row, so none took the provider lock. Wall reroutes (`%hit a usage wall (%rerouted to%`): 7 events on 5 tasks, 3 of those tasks with a wait row, the last on 2026-09-15. The server2 stack's own database was not queried. |
| The partner the S1 plan names (L7 continuation on the same task) can meet L23 | Only in theory. L7 locks a task waiting on a released-seat answer. L23 settles an open (`Dispatched`/`Working`) task on its live session (`AgentTaskReplyService.cs:117-120`). Any conforming transaction that holds the task row and then wants the same provider is a partner. Examples are the S1 reroute L3 (`Queued` only), the S1 routing resume L4 (`Queued` only), and the S1 claim (`Queued` only, plan D-5). None of those targets an open task. **No live partner was found.** The severity is a latent Rule P break, not an observed deadlock. |

**Severity.** Low. The path fires only for a quota or wall settle of a task that already has a
capacity wait row. Over 2026-09-01 to 2026-10-28 that was 0 quota settles and 3 reroute tasks. No
partner that takes task then provider on an open task exists today. A cycle would be a detectable
`40P01`:
- If the settle is the victim, its transaction rolls back. `OnTurnEndLockedAsync` logs the error
  (`:147-151`), and the transcript action fails to its durable owner (`:123-124`), which retries.
- The wait is not spent: supersede only releases.

The fix keeps Rule P true so that the S2 `LockOrderLedger` can be strict, and so that a future
open-task path that takes task then provider cannot deadlock.

## Decisions

**D-1. Swap the fence to task, then session (L24).** At `AppDbContext.cs:163-164`, lock
`AgentTasks` before `AgentSessions`. Move the two statements into one
`internal async Task LockRuntimeActionFenceAsync(CancellationToken)` on `AppDbContext`. It does
nothing when `RuntimeActionTaskFence` is null, and it throws `InvalidOperationException` when no
transaction is open. `SaveChangesAsync` calls it in place of the two lines, inside the transaction
it already opens (`:160-161`). The identity checks at `:165-171` are unchanged.

*Why this is safe for every fenced save (the compatibility proof).* Take any transaction T that
runs a fenced save. Let P be the locks T took before the save, and Q the locks it takes after.
- Before the swap, T acquires P, session, task, Q. After the swap, T acquires P, task, session, Q.
- The relative order of each pair (p, task), (p, session), (task, q) and (session, q) is unchanged.
- The only pair that changes is (session, task). It goes from inverted to the D-1 order.
- Re-locking a row T already holds is a no-op in Postgres and does not wait.

So the swap removes one inversion and adds none, whatever P and Q are. This needs no enumeration of
the fenced call tree.

*The swap's only new exposure* is a different transaction that locks a session and then the same
task (rank 4, then 3). Today that would be a conforming partner of the old fence. After the swap it
is a detectable cycle partner. The plan grepped every `FROM "AgentSessions" ... FOR UPDATE` in
`server/`:
- `BlockedTaskParkingService.cs:92`, `TerminalRunnerSeatReleaseService.cs:878`,
  `TaskParkPublicationService.cs:393` and `AgentTaskService.cs:3642` lock the task first.
- `AgentSessionRuntime.cs:213` (exit observer, then the agent), `SessionReconciliationService.cs:208`
  and `:649`, `AgentSessionService.cs:413`, `AgentService.cs:943` and `:1012`, and
  `AgentControlService.cs:1286` do not lock or write `AgentTasks` after the session lock in the
  same transaction. `AgentService.cs:976` is an unlocked `AnyAsync` read.
- `SessionMessageQueueService.LockAgentSessionRowAsync` (`:5658-5662`; callers
  `AgentSessionRuntime.cs:1434` and `:1604`, `SessionMessageQueueService.cs:1039`) locks the session
  inside the caller's transaction, and no `AgentTasks` lock or write follows in those windows.

Code repeats this grep on its final tree (C-4). Any new hit is a plan amendment, not a silent edit.

**D-2. Lock the task, then the fence, inside the supersede, before the provider (L23, L23b).** In
`SupersedeTaskWaitsOnAsync` (`CapacityRecoveryService.cs:504-506`), after the owned or ambient
transaction is in place and before `TakeProviderLockAsync`, add:
1. `SELECT 1 FROM "AgentTasks" WHERE "Id" = {taskId} FOR UPDATE` (rank 3).
2. `await db.LockRuntimeActionFenceAsync(ct)`: the fence task (rank 3, a no-op when it is the same
   task), then the fence session (rank 4). It does nothing unfenced.

Then the provider (rank 7), as today. Every row the following save writes is now one of these:
- a row locked earlier in the same transaction (the task, and the session when fenced), or
- a rank-8 row, or
- an unranked row whose other writers also take it only after the provider (`ApiErrorRecoveries`:
  L15 writes it under the same provider), or an insert.

The order is 3, 3, 4, 7, 8, which satisfies Rule P. One edit covers:
- L23 fenced and unfenced.
- L23b fenced and unfenced.
- Any future caller that leaves the task dirty before a supersede.

A task with no wait row still takes no lock (the `kinds` loop is empty).

*Rejected: alternative A, the S2 sentence ("one owned transaction in `HandleApiErrorTurnAsync`":
task `FOR UPDATE`, session if fenced, mutate, ambient supersede, save, commit).* It fixes only L23.
L23b, and any other dirty caller of the supersede, would still flush under the provider. It also
moves the settle's commit point. That point is unchanged under D-2, because the supersede's
transaction already commits the task edits today.

*Rejected: alternative B, "save the task edits first, then supersede with a clean tracker".* That
would split the quota block from the wait supersede into two commits, which changes crash
atomicity. It also does not help the fenced scope: the fence runs on every save, including the
supersede's own save with a clean tracker. Under the provider that save would still lock session
and task.

*Rejected: put the fence pre-lock only in `TakeProviderLockAsync`.* That method does not know the
supersede's task id, so the unfenced L23 and L23b would still flush an unlocked task under the
provider. D-2 places the fence pre-lock in the supersede next to the task lock. `TakeProviderLockAsync`
stays unchanged. In the fenced scope the only provider-takers that receive a `db` argument are
these:
- `RedeemOnAsync`, `ReleaseClaimReservationOnAsync` and `ReceiptDispatchOnAsync` are used by the
  dispatcher, which has its own unfenced context.
- `BumpWaveOnAsync` is used by `ApiErrorRecoveryService` on its own unfenced scope.
- `SupersedeTaskWaitsOnAsync` is the only one reachable from the settle.

**D-3. `HandleApiErrorTurnAsync` and `RequeueOnWallAsync` are not changed.** The fix lives in the
two lock sites. The settle's event and parent-delivery order (`:1992-1995`) stays as it is.

**D-4. What the new task lock in the supersede does to its other callers.** This is the risk table
for the supersede's callers:

| Caller | Transaction | Before (base) | After D-2 | Risk |
|---|---|---|---|---|
| L3 queued reroute `AgentTaskDispatcher.cs:1680` | owned, task already locked (S1) | 3 -> 7 | 3 -> 3 (no-op) -> 7 | none |
| L4 routing resume `:1839` | one transaction per task, task locked (S1) | 3 -> 7 | same plus a no-op | none |
| L7/L8 continuation `AgentTaskService.cs:3852` via `ContinueReleasedSeatAnswerAsync` | ambient: task, release, session, agent | 3,4,4,5 -> 7 | 3,4,4,5 -> 3 (no-op) -> 7 | none |
| L9 retry / L23b requeue `:3852` via `RequeueAsync` | owned per kind | 7 (then flushes a dirty task for L23b) | 3 -> [fence 3,4] -> 7 | now holds the task row for the supersede's short transaction. It still holds only the in-process session gate (`:3807-3808`) across connections, so Rule X holds |
| L10 cancel `:2949` | owned per kind | 7 | 3 -> 7 | as L9 |
| L15 API-error wall `ApiErrorRecoveryService.cs:543` | the service's own transaction (`:102`, or the owned one for the repair calls at `:644` and `:742`) | 7 -> (queue rows `ExecuteUpdate`, ensure, bump) | 3 -> 7 -> ... | the adoption transaction now holds the open task's row. Its callers (`AgentTaskReplyService.cs:1924`, the sweep `ApiErrorRecoveryService.cs:187`, `FireOneAsync` `:294`, `ChannelReplyDispatcher.cs:783`) hold no transaction on another connection when they call it. Two concurrent adoptions serialize on the task instead of on the provider. No new cycle |
| L23 quota `AgentTaskReplyService.cs:1991` | owned per kind | 7 -> [4 -> 3 fenced] -> 3 | 3 -> [3, 4] -> 7 | fixed |

*Pre-existing and out of scope, noted for the S2 ledger.* L15 runs
`SessionQueuedMessages ... ExecuteUpdateAsync` (`ApiErrorRecoveryService.cs:551-560`) while it
holds the provider. That is a rank-4 write under rank 7, which the S1 L15 row ("7 only") missed. No
partner was found that holds a `SessionQueuedMessages` row and then waits on the provider (L17
holds no row lock while it redeems). File it as S2 ledger input, not in this slice.

**D-5. Ship master first, then cherry-pick to `rc/2026-10-27` (the investigation's order).**
- The severity is Low, there is no production occurrence, and the deadlock would be detectable.
- A native rc qualification (28bbb58e) is live, and the rc should not move under it for a latent
  defect.
- The touched files are identical on both lines, so the product and test commits pick cleanly.
  Drop the plan doc from the rc pick, because rc carries no `docs/superpowers/plans`.
- The rc port is optional for `rc/2026-10-27` and is the operator's call after the qualification
  settles. The only effect of not porting is that the S2 ledger, if it lands on rc, would throw on
  L23 and L23b.

**D-6. No new column, setting or migration.**

## Rank table for the touched transactions (file:line at `aa6dfd24a`)

| Row | Path | Base sequence | After this plan |
|---|---|---|---|
| L23u | Quota settle, unfenced (`OnTurnEndAsync` `AgentTaskReplyService.cs:88` -> `:137`; watchdog `:177`; sweep `ApiErrorRecoveryService.cs:220`) -> `:1948-1991` | 7 (`CapacityRecoveryService.cs:506`) -> 3 (flush `:533`) **inverted** | 3 (new, `:504-506`) -> 7 -> flush of held rows |
| L23f | Quota settle, fenced (`OnTranscriptActionAsync` `:102-127`, fence `:121`) | 7 -> 4 (`AppDbContext.cs:163`) -> 3 (`:164`) **inverted** | 3 -> 3 -> 4 (`LockRuntimeActionFenceAsync`) -> 7 -> save (fence re-lock, no-op) |
| L23b | Wall reroute (`AgentTaskReplyService.cs:2002-2016` -> `AgentTaskService.cs:3163` -> `:3365-3409` -> `:3852`) | 7 -> 3 (flush) [-> 4 -> 3 fenced] **inverted** | 3 -> [3, 4] -> 7 -> flush of held rows plus an `AgentIncident` insert (FK `KEY SHARE` on the session; when fenced it is already held) |
| L24 | Any fenced save (`AppDbContext.cs:155-176`) | 4 -> 3 **inverted** | 3 -> 4 |
| L3, L4, L7, L9, L10, L15 | Other supersede callers | see D-4 | see D-4 |

## Change list (Code)

- **C-1** `server/Infrastructure/Data/AppDbContext.cs`:
  - Add `LockRuntimeActionFenceAsync` (D-1): task, then session, `FOR UPDATE`, using the same SQL
    text as `:163-164`.
  - Replace `:163-164` with a call to it.
  - Add no other behaviour change.
- **C-2** `server/Application/Services/CapacityRecoveryService.cs`, `SupersedeTaskWaitsOnAsync`
  `:504-506`: add the task row lock and then `db.LockRuntimeActionFenceAsync(ct)` before
  `TakeProviderLockAsync`, inside the existing `try` (D-2). Update the summary at `:490` to say
  "locks the task (and the fenced session) before the provider".
- **C-3** Tests (`tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`; helpers in
  `CapacityLockProbe` as needed):
  - The witnesses below.
  - The quota and reroute arms reuse `WallRerouteHarness`, which already registers
    `CapacityRecoveryService` (`tests/Antiphon.Tests/TestHelpers/WallRerouteFixture.cs:293`), and
    the `ComplexityWallRerouteTests` seed helpers (`SeedWorkingChainTaskAsync`,
    `SeedApiErrorStubTurnAsync`, `StampSessionModelAsync`). Make those `internal static`. That is a
    visibility change only.
  - The fenced arm calls `AgentTaskReplyService.OnTranscriptActionAsync` (`internal`) with a
    `SessionRuntimeAction` carrying `AcceptedTaskId`, `AcceptedTaskAttempt`,
    `AcceptedGeneration = session.StartedAt`, `TriggerKind = "catchup"` and the stub turn's
    `TurnEnd` sequence.
  - Each arm seeds one capacity wait for the task (the existing `EnsureWaitAsync` helper,
    `CapacityLockOrderTests.cs:622`), with the `ExecutionKind` of the session's kind, so the
    supersede takes that kind's key.
- **C-4** Re-run the D-1 session-locker grep on the final tree and list the hits in the Code report:
  `grep -rn '"AgentSessions"[^;]*FOR \(UPDATE\|NO KEY UPDATE\|SHARE\)' server --include=*.cs`,
  plus the callers of `LockAgentSessionRowAsync`.
- No other product file changes. `AgentTaskReplyService.cs` and `AgentTaskService.cs` are unchanged
  (D-3).

## Verification design

The test-design stage is not folded into this dispatch. The witnesses are named here so that Review
can judge the plan, and test-design may refine them. Every wait polls `pg_stat_activity` and
`pg_locks` through `CapacityLockProbe` with its 30 s `Deadline`. There are no sleeps. Each red is a
lock state, or a forced cycle that Postgres must break. None depends on scheduling luck. The red
commit comes before C-1 and C-2 (git-common).

| ID | Test | Asserts | Red at base |
|---|---|---|---|
| W-1 | `CapacityLockOrderTests/C1593_path_blocked_on_task_holds_no_provider_lock`, new arguments `quota-blocked` (unfenced, `OnTurnEndAsync`), `quota-blocked-fenced` (`OnTranscriptActionAsync`) and `wall-reroute` (unfenced; a list-governed Working task on a session-limit wall with an alternative, as in `ComplexityWallRerouteTests.Session_limit_with_an_alternative_switches_immediately`) | The existing `AssertBlockedWithoutAdvisoryAsync` (`:452-486`) is used. A holder locks the task `FOR UPDATE`, and the path's backend waits on it (`PollBlockedByAsync`). That pid holds no `antiphon.capacity.kind.<Kind>` key. After release the task ends `Blocked` (`SubscriptionQuotaExceeded`) for the quota arguments, or `Queued` at attempt + 1 with the new kind for `wall-reroute`. The wait ends `Superseded` | **Red**, all three: the supersede holds the key from `CapacityRecoveryService.cs:506` while its save waits on the task. For `quota-blocked-fenced`, the base waiter's lock is the fence's `:164` task lock, taken after `:163`. |
| W-2 | `CapacityLockOrderTests/C1593_quota_settle_and_task_holder_complete_without_deadlock` `[Arguments("unfenced")]`, `("fenced")`. This is the two-transaction deadlock witness. | **Steps:**<br>1. Holder H begins a transaction and locks the task `FOR UPDATE` (the conforming partner: rank 3, then 7).<br>2. Start the quota settle and poll until its pid is blocked by H.<br>3. H runs `SET LOCAL lock_timeout = '15s'`, then `pg_advisory_xact_lock(hashtext(kindKey))`, and then commits.<br>**Asserts:** H gets the key with no `40P01` and no `55P03`. The settle completes, the task is `Blocked`, and the wait is `Superseded`. No `40P01` warning appears in the settle's captured log (`OnTurnEndLockedAsync` swallows it, `:147-151`). For `fenced`, the transcript action does not throw. | **Red**, deterministically: at base the settle already holds the key that H requests, so the two connections form a cycle. Postgres must abort one of them with `40P01` after `deadlock_timeout` (1 s). Whichever side is aborted, an assertion fails: H's acquire throws, or the settle's task is still `Working`. The 15 s `lock_timeout` only bounds a regression that cannot be detected, and it is never reached. |
| W-3 | `CapacityLockOrderTests/C1593_fenced_save_blocked_on_task_holds_no_session_lock` | The fenced quota settle runs on a task with **no** wait row, so the supersede is a no-op and the only locks are the fence's at `AgentTaskReplyService.cs:1993`. H locks the task, and the settle's pid is blocked by H. A third connection then runs `CapacityLockProbe.TryNowaitAsync(connection, "AgentSessions", sessionId)`, which must return true (`:73-74` pattern). After release the task is `Blocked`. | **Red**: the base fence holds the session (`AppDbContext.cs:163`) while it waits on the task (`:164`), so the `NOWAIT` probe gets `55P03` (false). |
| G-1 | Existing `CapacityLockOrderTests` (16 results), unchanged | V-8, V-9, V-10 and V-15 of S1 still pass | guard |

**Positive controls (method-scoped, run once each, then restored).**
- **PC-W1:** delete only the new task lock line in C-2 and keep the fence pre-lock. `quota-blocked`
  and `wall-reroute` go red on the advisory assertion. `quota-blocked-fenced` stays green, because
  the fence pre-lock takes the task. That shows the two lines each do their own job.
- **PC-W3:** restore `:163-164` order inside `LockRuntimeActionFenceAsync`. W-3 goes red, and so
  does W-1 `quota-blocked-fenced`: under D-2 the pre-lock then takes session before task, and the
  holder blocks it at the task with no key held. That case stays green on the key assertion, which
  is expected. W-3 is its witness.
- **Note for Mutation:** the S1 plan's V-8 `cancel` and `retry` PCs ("wrap the supersede and the
  following task save in one transaction") no longer go red after D-2, because the supersede itself
  now takes the task first. Mutation redefines them as "C-2 task lock removed **and** the S1 wrap
  applied". The plan states this so the change is not reported as a weakened guard.

## Performance budget

Per [test performance standards](../../test-performance-standards.md) and
`docs/testing-and-build.md#performance-standards`:
- **Measured:**
  - S1 rounds on server2: single capacity lock-order methods took 1.2-11.4 s, and the first test in
    a host was about 10-11 s (shared Postgres plus clone).
  - A leased `tests/Antiphon.Tests` build took 217-399 s compile and 220-520 s in the slot.
  - This task ran no build and no test. Its 45-minute budget went into the read and the production
    query.
  - CP-3 and CP-4 classes have no recorded server2 p95. Code records one per row from the TRX.
- **Shared setup:** one shared Postgres per test host, and one isolated schema per test
  (`TestDbFixture.CreateIsolatedSchemaAsync`). There is no per-test server boot.
- **Phases recorded per row:** slot wait, build, host start, and per-test clone, act and assert.
- **Budgets:**
  - 6 new results (W-1 three arguments, W-2 two, W-3 one). The p95 estimate is 12 s per method, plus
    at most 1 s of `deadlock_timeout` in W-2 at the red commit only.
  - The method timeout stays at the class's `[Timeout(120_000)]`. The in-test lock-wait deadline
    stays at 30 s (`CapacityLockProbe.Deadline`). Both are about 2x or more of the provisional p95,
    and Code replaces them with the measured p95 if that p95 exceeds 15 s.
  - Rows: CP-1 about 6 min including host start (22 results), CP-2 about 8 min, CP-3 about 8 min,
    CP-4 about 10 min. Total test time is about 32 min plus one build.
  - A row over 2x its estimate is a Debug item, not a timeout raise.

### Checkpoints

**Lane:** Linux, server2. `GET /api/runner-defaults` returned `globalRunnerId=server2` (revision 2),
and `GET /api/session-runners` listed desktop (windows), server2 (linux) and server2-temp (linux).
There is no `-Runner` or `-Platform` pin.
- **Build:** one slot build with `UseAppHost=false`, then `dotnet exec --no-build` per row, run
  through `scripts/build-slot.ps1`.
- **Not run:** the whole Unit lane, and any native or Windows test, because a desktop
  qualification is live.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | C-1+C-2 | `tests/Antiphon.Tests -> bin-c1593q/` | lock-order | `/*/Antiphon.Tests.Application/CapacityLockOrderTests/*` | W-1, W-2, W-3, G-1 | 22 results (16 existing + 6 new), 0 failed/skipped | 22 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | C-1+C-2 | CP-1 | c508-guard | `/*/*/AgentTaskDispatchBaseGuardTests/(Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*)\|(C508_GuardRefMismatchWarnedOnce*)\|(C540_ClaimRollbackHasNoCollapsedIntent*)\|(C540_ClaimRollbackKeepsLaterQueuedTaskCustody*)` | S1 R-1, unchanged claim | 11 results (as S1 CP-2), 0 failed/skipped | 11 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | C-1+C-2 | CP-1 | fence-callers | `/*/Antiphon.Tests.Application/(AgentTaskCatchUpSettlementTests*)\|(SessionRuntimeActionRecoveryTests*)/*` | D-1 fenced saves: catch-up settles run `OnTranscriptActionAsync` (fenced success path); `C1259_admitted_task_attempt_cannot_settle_its_replacement` is the fence's identity scope | all listed, 0 failed/skipped (6 + 12 test methods; argument expansion makes it at least 18 results) | 18 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | C-1+C-2 | CP-1 | supersede-callers | `/*/Antiphon.Tests.Application/(ProviderQuotaRefusalAcceptanceTests*)\|(ComplexityWallRerouteTests*)\|(WallRerouteDispatchTests*)/*` | D-2 and D-4: the quota settle (L23), the wall reroute (L23b), the queued reroute (L3) and L15 adoption races (`Racing_sweep_and_settlement_do_not_enqueue_retry`) | all listed, 0 failed/skipped (23 + 11 + 13 methods, WallReroute 15 results as in S1 CP-3; at least 49) | 49 | 10 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

**Red run (before C-1 and C-2, not in the table).** Run one build of the red test commit, then a
`--no-build` run with this filter:
`/*/*/CapacityLockOrderTests/(C1593_path_blocked_on_task_holds_no_provider_lock*)|(C1593_quota_settle_and_task_holder_complete_without_deadlock*)|(C1593_fenced_save_blocked_on_task_holds_no_session_lock*)`.
- Expected red: the three new W-1 arguments, both W-2 arguments and W-3. Each must fail on its
  named assertion.
- Expected green: the 8 existing V-8 arguments.
- Zero tests, or a fixture error, is not red.

**Suffix check at base.** Each of these patterns matches exactly one class:
`CapacityLockOrderTests*`, `AgentTaskCatchUpSettlementTests*`, `SessionRuntimeActionRecoveryTests*`,
`ProviderQuotaRefusalAcceptanceTests*`, `ComplexityWallRerouteTests*` and `WallRerouteDispatchTests*`
(grep of `class <Name>[A-Za-z0-9_]*` across `tests/`). The new method names
`C1593_quota_settle_and_task_holder_complete_without_deadlock` and
`C1593_fenced_save_blocked_on_task_holds_no_session_lock` are not prefixes of any existing method,
and no existing method is a prefix of them. Code re-checks this after writing the class.

**Session-wait checklist.** Not applicable: the plan adds or changes no session that waits for
input.

### Landing

- Cut the Code branch from `origin/master`. The touched files are identical on both lines.
- Before Review, run `git fetch` and `git merge-tree --write-tree` against `origin/master` and
  `origin/rc/2026-10-27`.
- Run `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef <tip>`.
- rc port (D-5): cherry-pick the red-test and fix commits onto `rc/2026-10-27` after the live
  qualification settles, dropping this plan doc. Then re-run CP-1 and CP-4 on the rc build.

## Residual risk

- The swap (D-1) can create new detectable `40P01` partners only among session-then-task lockers.
  The grep found none, and C-4 re-checks.
- D-2 holds the task row slightly longer in the L9, L10 and L15 short transactions. No cycle was
  found: none of these callers waits cross-connection while it holds the row.
- The L15 queue-row write under the provider is pre-existing and goes to the S2 ledger (D-4 note).
- The server2 stack's own database was not queried for frequency. The desktop numbers are the
  evidence behind "Low".
