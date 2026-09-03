# CARD-0331 — a land request is a row, not a channel item

**Plan pass, 2026-09-03. Sources verified: master `b9c5f439`; `AgentTaskLandQueue`, `AgentTaskLandService`, `AgentTaskLandHostedService`, `DelegationWorktreeService` (land arms), `AgentTaskDispatcher` (CARD-0215 sibling hold), `AgentTaskReplyService.ResolveConflictedParentAsync`, `ScheduleSweepHostedService` / `AgentTaskCheckHostedService` (the repo's existing claim-and-hand-off shape), `scripts/restart-apphost.ps1`, and the live `antiphon` database. No production code is changed by this plan.**

## 1. Verified current behaviour

**The queue is the only record that a land is wanted.** `AgentTaskLandService.RequestAsync` writes a `LandRequested` event, saves, then `_queue.TryEnqueue(taskId, filter)` into an unbounded in-process `Channel<LandRequest>`. `AgentTaskLandHostedService.ExecuteAsync` is a single reader that runs `RunAsync` per item. Nothing reads the database to discover pending lands — not at boot, not on a timer. The `LandRequested` event is history, not state.

**Restarts are hard kills.** `scripts/restart-apphost.ps1:136` runs `taskkill /T /F` on the AppHost tree. `stoppingToken` never fires, there is no drain, and a land can die at any instruction — sitting in the channel, mid-`git rebase`, mid-`dotnet build`, between `merge --ff-only` and `git push`. The card's "enqueued but not yet dequeued, or mid-RunAsync" is exactly right, and "mid-RunAsync" includes the git steps.

**Three readers derive "a land is in flight" from the latest event type, and all three strand the same way:**

| Reader | Where | What it does on a stranded `LandRequested` |
|---|---|---|
| `RequestAsync` 409 | `AgentTaskLandService.cs:59-66` — latest of `{LandRequested, Landed, LandRefused, LandedWithResidue}` | Refuses every retry forever: "already has a land operation queued". |
| CARD-0215 sibling hold | `AgentTaskDispatcher.cs:2380-2442` `IsSiblingLandInFlight` — latest of `{LandRequested, Landed, LandRefused}` | Holds every same-card Execute dispatch forever ("is landing"). The card did not mention this one; it is the same bug with a worse blast radius. |
| Nothing else | — | `AgentTaskPipelineStatusService` and the client only label events. |

**Two more ways the same row strands, found while reading:**

- `AgentTaskLandHostedService.cs:32`: an exception out of `RunAsync` is logged and dropped. No `LandRefused`, no cleared state — the task sits at `LandRequested` and the 409 applies. Any `DbUpdateConcurrencyException`, IO fault, or git-launch failure produces the card's symptom without a restart.
- `Conflicted` (6) is in neither reader's set. A land whose rebase conflicts writes `Conflicted`, flips the task `Blocked`, and spawns a Merge delegate that pushes the target itself and removes the worktree (`CreateMergeTaskAsync` goal, steps 3–4). After `ResolveConflictedParentAsync` flips the parent back to `Succeeded`, the latest land event is still `LandRequested`: a follow-up `-Land` (the documented cleanup-retry verb) 409s.

**The `Held` loop blocks the consumer.** `AgentTaskLandHostedService.cs:26-30` sleeps 5 s inside the single reader before re-enqueueing a Shared-writer-held land; every other queued land waits behind that sleep for as long as the Shared task runs.

**Live evidence (query run 2026-09-03 against `antiphon-postgres`; latest of `{Conflicted, LandRequested, Landed, LandRefused, LandedWithResidue}` per task):**

| Task | Card | Latest land event | Requested at | Branch today |
|---|---|---|---|---|
| `db33399c` | CARD-0323 Plan | `LandRequested` | 2026-09-03 01:03:21Z | hand-landed as `e56fc2d1`; branch and worktree removed by hand |
| `bf31273c` | CARD-0057 Execute S1–S3 | `LandRequested` | 2026-09-02 12:22:18Z | `feat/card-task-bf31273c` still exists in `C:\src\Antiphon` at `45b8f285`, 1 commit above master, **not** an ancestor; worktree `C:\Antiphon\worktrees\card-task-bf31273c` still registered. The same work reached master as `5b0a5a03` at 16:25 the same day by another route. No `Held`, no outcome event — a silently dropped land the card did not know about. |

Totals: 42 `Landed`, 5 `LandedWithResidue`, 11 `LandRefused`, 3 `Conflicted` (all three parents since `Succeeded` via their merge tasks), 2 stranded `LandRequested`. Two of ~63 lands lost to this in three days of `-Land` existing.

## 2. Design

### 2.1 Ask (3), answered: yes, durable — the database is the queue, the channel is the hand-off

This is the shape the repo already uses twice. Checks (CARD-0047): the dispatcher tick claims due `AgentTasks` rows by advancing `NextCheckAt`, then drops the id into `AgentTaskCheckQueue`; a restart loses the channel but not the rows, and the next tick re-claims. Schedules (CARD-0057 D4): `ScheduleSweepHostedService` claims rows by advancing `NextFireAt`, `ScheduleFireQueue` hands them to `ScheduleFireHostedService`. In both, the channel exists only so the sweep never waits on the work, and the durable fact lives on the row. The land queue is the one place where the channel *is* the record. The fix is to make it look like the other two.

What it is not:

- **Not a Hangfire job.** `HangfireConfiguration` uses `UseInMemoryStorage` — Hangfire here is no more durable across `taskkill` than the channel, and its worker is off in every test boot. CARD-0328's proposed S3 residue sweep is a periodic *janitor* over filesystem residue, report-only by default; a land is a *command* that must run exactly once per request. Different shape.
- **Not a separate outbox table.** A task has at most one land wanted at a time, and the per-attempt history already exists as events. Columns on `AgentTasks` (the `NextCheckAt`/`CheckCount` precedent) are one migration, one index, zero joins, and readable from the row every existing reader already has.
- **Not a heartbeat or staleness timeout.** Land duration is unbounded when `-Verify` names a test filter (`Antiphon.Tests` is ~28 minutes), so any "older than N minutes means dead" rule is either wrong or useless. Liveness has an exact answer instead (§2.3).

### 2.2 Persisted land state on `AgentTask`

| Column | Type | Meaning | Written by |
|---|---|---|---|
| `LandRequestedAt` | `timestamp NULL` | **A land is wanted and has not reached an outcome.** Non-null is the durable queue. | Set by `RequestAsync`. Cleared in the same `SaveChanges` as every terminal outcome: `SettleLandedAsync` (`Landed`, `LandedWithResidue`), `RefuseAsync` (`LandRefused`), the new failure arm, the not-landable early return (unless `Blocked`), and `ResolveConflictedParentAsync` (the Merge delegate finished the land). **Kept** through `Conflicted` — see D3. |
| `LandVerifyFilter` | `text NULL` (≤ 400) | The `-Verify` filter to use when this request runs. | `RequestAsync`; cleared with `LandRequestedAt`. |
| `LandStartedAt` | `timestamp NULL` | The moment the current attempt passed the Shared-writer gate and started git work. Null while queued or held. | Set in `RunAsync` just before `PrepareLandAsync`, with its own `SaveChanges`; nulled by the sweep when it records an interrupted attempt; cleared with `LandRequestedAt`. |
| `LandAttempt` | `int NOT NULL DEFAULT 0` | How many times this request started git work. | Reset to 0 by `RequestAsync`; incremented with `LandStartedAt`. Left in place after the outcome (informational). |

Index: `IX_AgentTasks_LandRequestedAt` on `LandRequestedAt` with `HasFilter("\"LandRequestedAt\" IS NOT NULL")` — the sweep's query touches only pending rows. Migration `20260903120000_AddAgentTaskLandRequest` hand-written with the snapshot updated, per the `AddSessionLaunchBlock` convention (running daemons lock `bin/`). **No data backfill in the migration** — D5.

`RunAsync` begins with a fresh read and returns `Complete` without touching git when `LandRequestedAt` is null: a request that was superseded, already settled, or never made is a no-op, which is what makes a duplicate enqueue (sweep and `RequestAsync` racing, or a sweep read that straddled a completion) harmless. Existing tests that call `RunAsync` directly seed the column in `SeedSucceededWorktreeAsync`.

### 2.3 Liveness is exact: the in-process active set

`AgentTaskLandQueue` gains a `ConcurrentDictionary<Guid, byte>` of task ids that are queued or running **in this process**:

- `TryEnqueue(taskId, filter)` — `TryAdd` first; returns `false` when the id is already active (a second enqueue while queued or running is refused, never duplicated).
- `IsActive(taskId)` — queued or running here, now.
- `Release(taskId)` — the drain service calls it in a `finally` after every `RunAsync`, whatever the outcome.
- `PendingCount`, `TryDequeue` for tests, as `AgentTaskCheckQueue` has.

That set is the honest answer to "is a land genuinely in flight". A single server process runs the land, so a pending row whose id is not active in this process is by definition not running anywhere: the process that held it is gone (restart), or it never got enqueued (`TryEnqueue` returned false to a lost race), or the consumer dropped it. No timeout can be more accurate than this, and none is needed.

`RequestAsync` becomes:

1. Existing guards unchanged (Worktree only; `Succeeded` only — so a `Blocked`-by-conflict task is still refused with the status message).
2. `if (_queue.IsActive(taskId))` → `ConflictException($"Task {short} land is running in this server: requested {LandRequestedAt:u}" + (LandStartedAt is null ? ", queued" : $", started {LandStartedAt:u}, attempt {LandAttempt}") + ". Wait for its outcome event.")`. That is the only 409 arm.
3. Otherwise set `LandRequestedAt = now`, `LandVerifyFilter`, `LandStartedAt = null`, `LandAttempt = 0`, new `ConcurrencyToken`; add the existing `LandRequested` event; if the column was already non-null (pending but not active — a request the previous process lost), also add a `Warning` event `"Previous land request at {old:u} was not running (server restarted); replaced by this request."` and return status `"requeued"` instead of `"queued"`.
4. `SaveChanges`, then `_queue.TryEnqueue`. A `false` here means a concurrent request won the race a moment ago; the row is set and the winner will run it, so return `"queued"` — not an error.

The card's "-Force / re-request after a timeout" is subsumed: a stale request is never active, so plain `-Land` requeues it and says so. A land that is genuinely hung (git waiting on a credential prompt, a wedged build) stays active and the 409 says since when; `restart-apphost.ps1` is the documented recovery, and after this card that restart *re-runs* the land instead of losing it.

### 2.4 The sweep: boot reconciliation and the periodic backstop are one loop

New `AgentTaskLandSweepHostedService` (`server/Infrastructure/Orchestration/`), the shape of `ScheduleSweepHostedService`: gated on `DelegationSettings.Enabled` like the check worker, runs one pass **immediately** on start and then every `DelegationSettings.LandSweepSeconds` (default 5 — today's `Held` retry cadence) via `PeriodicTimer`. Each pass opens a scope and calls `AgentTaskLandService.SweepAsync(ct)`:

```
pending = AgentTasks where LandRequestedAt != null && Status == Succeeded && Workspace == Worktree
for each row:
    if _queue.IsActive(row.Id): continue                      // queued or running here — leave it alone
    if row.LandAttempt >= LandMaxAttempts:                    // started N times, never finished
        RefuseAsync(row, $"land interrupted {n} times without finishing (last started {LandStartedAt:u}); not retried automatically — run -Land again")
        continue
    if row.LandStartedAt != null:                             // an attempt was cut off mid-git
        add Warning "Land attempt {n} started {at:u} did not finish (server restarted); re-running."
        row.LandStartedAt = null; SaveChanges                 // once per interruption, not once per tick
    _queue.TryEnqueue(row.Id, row.LandVerifyFilter)
```

What each case covers:

- **Killed while queued** (the card's incident): `LandStartedAt` null → silently re-enqueued on the first pass after boot. Nothing was lost, nothing to say; the outcome event is the record.
- **Killed mid-git**: `LandStartedAt` set → one `Warning` on the task, then re-run. §2.5 is why the re-run is safe.
- **Held behind a Shared writer**: `RunAsync` returns `Held` without starting git (`LandStartedAt` stays null, attempt not counted); the drain releases the id; the next sweep pass re-enqueues within ≤ 5 s. The `Held` event is still written once per request by the existing `alreadyHeld` check, so the loop is silent. The 5 s `Task.Delay` in the drain service is deleted — the reader never sleeps.
- **`RunAsync` throws**: the drain service's catch now calls `AgentTaskLandService.FailAsync(taskId, ex)` → `LandRefused` "land failed: {ex.Message}" + the existing kept-branch `Warning`, clears the pending state, delivers the note. If that write itself throws (database unreachable), it is logged and the row stays pending: the sweep retries it once the database is back, and `LandAttempt` bounds the retries.
- **`Blocked` by conflict**: excluded by the `Status` filter; the Merge delegate owns the rest (D3).
- **`LandMaxAttempts`** (default 3): three started-and-interrupted attempts on one request is a repeating crash, not bad luck; refuse with the reason and let the orchestrator decide. `Held` passes do not count.

The drain service (`AgentTaskLandHostedService`) shrinks to: read → scope → `RunAsync` → on exception `FailAsync` → `finally Release`. It no longer decides anything about retry.

### 2.5 Re-running after a hard kill is safe at every step

Because every re-run goes through `RunAsync` from the top, the question is only whether each kill point leaves a state that `RunAsync` handles. Traced against `PrepareLandAsync` / `FinalizeLandAsync` / `IsAlreadyLandedAsync` / `CleanupAlreadyLandedAsync`:

| Killed during | State left | Re-run does | Change needed |
|---|---|---|---|
| in channel / before any git | nothing | runs normally | none |
| `git fetch`, `rev-list`, `merge-base` | nothing | runs normally | none |
| `git rebase <target>` | `rebase-merge` (or `rebase-apply`) directory in the worktree's git dir | today: `git rebase` fails "already a rebase-merge directory", `diff --diff-filter=U` finds nothing, `rebase --abort` runs, result is a **spurious `LandRefused`**; the *next* `-Land` works | **S3:** `PrepareLandAsync` checks `git rev-parse --git-path rebase-merge` / `rebase-apply` before rebasing and runs `git rebase --abort` first, noting "aborted an interrupted rebase" in the detail. One refusal saved per interruption. |
| `dotnet build` / `dotnet test` (`VerifyAsync`) | `bin-land/` left in the worktree (the `finally` cannot run); MSBuild nodes killed with the tree | next `VerifyAsync` overwrites `bin-land/` and its `finally` deletes it | none — note it in the code comment |
| `AdvanceTargetAsync` (`merge --ff-only` in the main checkout) before `git push` | local target ahead of `origin/target` by the task's commits; branch is an ancestor of local target | `IsAlreadyLandedAsync` → true → `CleanupAlreadyLandedAsync` removes worktree/branch, `Landed` reports `origin/target = <old sha>` — **the push never happens** until the next unrelated land pushes it | **S3 (D6):** in the already-landed arm, when `rev-list --count origin/<target>..<target>` > 0 and the branch is an ancestor of local `<target>`, `git push origin <target>` first and report the pushed sha. Same push `FinalizeLandAsync` would have made; the "origin ahead" refusal already guards the other direction. |
| after `git push`, before/during worktree removal | pushed; residue | `IsAlreadyLandedAsync` → cleanup only → `Landed` or `LandedWithResidue` | none — this is CARD-0328's existing re-land arm |
| after the outcome `SaveChanges`, before `DeliverAsync` | outcome event written, column cleared | not re-run (nothing pending) — the caller's WhenIdle note is lost | accepted: the event is the record and `GET /api/agent-tasks/{id}` shows it; delivery was best-effort before this card too |

Nothing in the table needs a heartbeat, a lease, or a lock: git's own state plus the outcome events are enough to make a fresh `RunAsync` idempotent.

### 2.6 The dispatcher hold reads the column

`AgentTaskDispatcher` sibling guard (`:2380-2442`): the sibling projection adds `t.LandRequestedAt`; the hold arm becomes `sibling.LandRequestedAt is not null`; the `landEvents` query and `IsSiblingLandInFlight` are deleted. Semantics preserved exactly — `Held` (a Shared-writer wait) leaves it set, a refusal clears it into the warn arm, a conflict keeps it until the Merge delegate resolves (D3) — with one deliberate difference: a stranded request no longer holds a card's Execute forever, because a stranded request no longer exists (the sweep runs it) and the two pre-migration rows have a null column (D5). `AgentTaskDispatchBaseGuardTests.a_sibling_land_in_flight_holds_until_the_base_contains_it` sets and clears the column instead of adding `LandRequested`/`Landed` events.

### 2.7 Surface

- `POST /api/agent-tasks/{id}/land` — unchanged route and body; `202 { status: "queued" | "requeued" }`; the 409 body now says *running since when* and never fires for a request no process holds.
- `AgentTaskDto` (`AgentTaskDtos.cs:190`, beside `NextCheckAt`/`CheckCount`) and `client/src/api/agentTasks.ts:141` gain `landRequestedAt`, `landStartedAt`, `landAttempt`, so `GET /api/agent-tasks/{id}` shows a pending land without reading events.
- `delegate.ps1 -Land` prints the returned status word ("Queued land" / "Requeued land") — one line in the `'Land'` arm.
- Settings: `DelegationSettings.LandSweepSeconds` (5, validator 1–60) and `LandMaxAttempts` (3, validator 1–10).
- Docs: `docs/orchestration-loop.md` §1 "Also delegated" and §5 land paragraph (a request survives restarts; a 409 means running now, wait for the outcome; a `Warning` "did not finish (server restarted); re-running" is informational); `docs/ops-http.md:48` and `docs/antiphon-api.md:231` (status words, the 409 meaning, the three DTO fields); `docs/session-runtime-invariants.md` new bullet (CARD-0331: *a land request is a row; the channel is a hand-off; the sweep re-runs anything pending that no process holds; three interrupted attempts refuse*), next to the CARD-0215 bullet at `:139`.

## 3. Decisions

| # | Question | Decision |
|---|---|---|
| D1 | Columns on `AgentTasks` vs an `AgentTaskLandRequests` outbox table | **Columns.** One pending land per task; history is the event log; it is the `NextCheckAt` precedent and every reader already has the row. |
| D2 | Sweep home: Hangfire job, dispatcher tick, or its own hosted service | **Own hosted service** (`AgentTaskLandSweepHostedService`, `ScheduleSweepHostedService` shape). Hangfire storage is in-memory here; the dispatcher tick is serial and already carries the check sweep, and a land sweep must run at boot before the first tick's sibling guard reads the column. |
| D3 | Does `Conflicted` clear `LandRequestedAt`? | **No.** The Merge delegate is finishing that land (it pushes the target and removes the worktree); `ResolveConflictedParentAsync` clears the column when it flips the parent back to `Succeeded`. That preserves the CARD-0215 hold through the merge, which today's event-based hold also does. A Merge delegate that fails leaves the parent `Blocked` with the column set, exactly as today's latest-event rule leaves it; cancelling the parent lifts the hold (the guard only considers `Succeeded`/`Blocked`). Not widened here. |
| D4 | Liveness by boot-id column, heartbeat, or in-process set | **In-process set.** One server process runs lands; "is this id queued or running in this process" is exact and needs no schema, no clock, and no tuning. A boot-id column would encode the same fact one step removed. |
| D5 | Backfill pre-migration stranded rows in the migration? | **No.** Only two exist (§1) and one of them (`bf31273c`) would rebase a duplicate of work already on master — possibly clean, possibly a Merge-delegate spend. The orchestrator handles both by hand (§5); the column starts null everywhere, which also lifts the two cards' phantom holds on the first tick. |
| D6 | Push in the already-landed arm when local target is ahead of origin | **Yes**, guarded by "branch is an ancestor of local target". It is the push `FinalizeLandAsync` would have made a second later; without it a kill in that one-second window reports `pushed (origin/master=<old sha>)`. Reviewer may drop it if the guard feels too clever; the cost is one wrong sha in one event until the next land pushes. |
| D7 | Warn per requeue? | **Only for an interrupted attempt** (`LandStartedAt` set). A request that merely sat in the channel across a restart is re-enqueued silently; a `Held` re-pick every 5 s must not write anything. |

## 4. Slices

| Slice | Files | Tests | Estimate |
|---|---|---|---|
| **S1** durable request + honest 409 | `server/Domain/Entities/AgentTask.cs` (4 columns), `server/Infrastructure/Data/AppDbContext.cs` (map + filtered index), `server/Migrations/20260903120000_AddAgentTaskLandRequest.cs` + `AppDbContextModelSnapshot.cs` (hand-written, `AddSessionLaunchBlock` convention), `server/Application/Services/AgentTaskLandQueue.cs` (active set: `TryEnqueue` dedup, `IsActive`, `Release`, `PendingCount`, `TryDequeue`), `server/Application/Services/AgentTaskLandService.cs` (`RequestAsync` per §2.3; `RunAsync` top-of-method no-op guard, attempt claim write before `PrepareLandAsync`, `ClearPending` in `SettleLandedAsync` / `RefuseAsync` / not-landable return; new `FailAsync`), `server/Application/Services/AgentTaskReplyService.cs` (`ResolveConflictedParentAsync` clears), `server/Application/Dtos/AgentTaskDtos.cs` + `AgentTaskService` mapping + `client/src/api/agentTasks.ts`, `scripts/delegate.ps1` `'Land'` arm status word | New `AgentTaskLandRequestTests` (isolated schema, no git): request sets columns + `LandRequested` event and enqueues; second request while active → 409 whose message contains "running" and the requested time; request when the column is set but not active → `requeued`, one `Warning`, column refreshed; request after `Landed` → `queued` with attempt reset; `TryEnqueue` twice → second false; `RunAsync` with a null column is a no-op (no events, no git). `AgentTaskLandStageOutcomeTests`: seed helper sets `LandRequestedAt`; happy path / refused / residue / already-landed each end with the column null; conflict path leaves it set and the task `Blocked`; `RunAsync` sets `LandStartedAt` and `LandAttempt = 1` before git (assert via the `Held` arm: a held run leaves both untouched). `AgentTaskReplyService` resolve-conflict test (extend the existing one if present, else one case): column cleared on resolution. | 3–4 h Code (Grok or opus; a Worktree task). Hand-written migration + snapshot is the fiddly part; build to `--property:OutputPath=bin-<name>/` while daemons hold `bin/`. |
| **S2** sweep + drain rewrite + hold on the column | `server/Application/Services/AgentTaskLandService.cs` (`SweepAsync` per §2.4), `server/Infrastructure/Orchestration/AgentTaskLandSweepHostedService.cs` (new), `AgentTaskLandHostedService.cs` (drain: `FailAsync` on exception, `Release` in `finally`, no `Held` sleep), `server/Application/Settings/DelegationSettings.cs` + validator (`LandSweepSeconds`, `LandMaxAttempts`), `server/Program.cs` (register the sweep beside the drain), `server/Application/Services/AgentTaskDispatcher.cs` (§2.6) | New `AgentTaskLandSweepTests` (isolated schema, fake `TimeProvider`, real queue): pending + not active → enqueued, no event; pending + `LandStartedAt` set → one `Warning` naming the attempt and `LandStartedAt` nulled, enqueued once across two passes; pending + active → skipped; `Blocked` pending → skipped; `LandAttempt >= LandMaxAttempts` → `LandRefused` naming the count, column cleared, not enqueued; `Canceled` with the column set → cleared, not enqueued. Drain: an `RunAsync` that throws writes `LandRefused` "land failed:" and clears (test through `FailAsync` directly plus one hosted-service run with a throwing scoped service, `AgentTaskCheckHostedService` has no such test so keep it small). `AgentTaskDispatchBaseGuardTests`: the hold test moves from events to the column; add `a_stranded_request_row_with_a_null_column_only_warns` (kept branch, no column → warn arm, dispatch proceeds). | 2–3 h Code (same worktree as S1 — it edits the same service; land S1+S2 as one pass or two commits). |
| **S3** crash hardening + docs | `server/Application/Services/DelegationWorktreeService.cs` (`PrepareLandAsync` rebase-state heal; already-landed push per D6 in `CleanupAlreadyLandedAsync` or a new `PushIfAheadAsync` the land service calls before cleanup), `AgentTaskLandService.CleanupAlreadyLandedAsync` (report the pushed sha), `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/session-runtime-invariants.md` | `DelegationWorktreeTests`: `prepare_land_aborts_an_interrupted_rebase_first` — seed a conflicting branch, run `git rebase master` by hand so the worktree is left mid-rebase, call `PrepareLandAsync` → `Conflicted` with the file list (not a `Rebase onto master failed: … rebase-merge` refusal) and the detail mentions the abort; `already_landed_arm_pushes_a_target_that_is_ahead_of_origin` — ff-merge the branch into local master without pushing, `RunAsync` → `Landed` whose sha equals `origin/master` after the run. | 2–3 h Code (Codex terra or Grok; a Worktree task after S1+S2 land). |

Order: S1 → S2 in one worktree, `-Land` it, then S3. S1 is shippable alone (the 409 is honest and a restart no longer 409s the retry — the orchestrator can `-Land` again by hand); S2 is what makes the retry automatic; S3 removes the one spurious refusal and the one wrong sha a re-run could still produce. Verification floor per slice: the named test classes with `--treenode-filter "/*/Antiphon.Tests.Application/*/*"`, not the assembly.

## 5. Recovery of the two live rows (orchestrator, after S1 deploys)

Both rows have `LandRequestedAt = NULL` after the migration (D5), so `-Land` treats each as a fresh request and the CARD-0215 guard stops holding their cards on the first tick.

- **`db33399c`** (CARD-0323 Plan): `delegate.ps1 -Land db33399c`. Branch and worktree are already gone, so `IsAlreadyLandedAsync` is true and the outcome is `Landed … nothing left to clean`. That closes its event history.
- **`bf31273c`** (CARD-0057 Execute S1–S3): the work is on master as `5b0a5a03`; the branch tip `45b8f285` is a *different* commit of the same content. First `git -C C:\src\Antiphon diff master...feat/card-task-bf31273c --stat`. If it is empty or trivially a duplicate, `-Land bf31273c` will rebase (git drops already-applied patches), fast-forward nothing, and clean up. If the diff shows real drift, do not `-Land` (a conflict spawns a Merge delegate for work that is already landed); remove by hand: `git -C C:\src\Antiphon worktree remove --force C:\Antiphon\worktrees\card-task-bf31273c` then `git -C C:\src\Antiphon branch -D feat/card-task-bf31273c`.

## 6. Not in this card

- Graceful drain on restart (`HostOptions.ShutdownTimeout`, a SIGTERM path in `restart-apphost.ps1`). With a durable request there is nothing to drain; a kill mid-git is handled by §2.5.
- A `LandRequested`-without-outcome attention item or incident. After S2 that state lasts ≤ 5 s plus the land itself; if it recurs, the `LandRefused` "interrupted N times" line is the signal.
- The Merge-delegate-failed dead end (D3), and `CancelAsync` clearing land columns — unchanged behaviour, filed if it bites.
- CARD-0328 S3's residue sweep. Orthogonal: it tidies filesystem residue after outcomes; this card guarantees the outcome happens.
