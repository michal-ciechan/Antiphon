# CARD-0481 — Grok usage-wall reroute leaves the task permanently Queued

Investigate stage, 2026-09-15. Task 08203aae, worktree `card-task-08203aae`, base `9b914298`.

## Verdict

**Confirmed.** The reroute itself works: `RerouteOnWallAsync` requeues the task onto the next
chain candidate (Grok → Codex `gpt-5.6-sol`) and the dispatcher tick sees it as Queued. What stops
it is the CARD-0412 capacity-recovery gate in the dispatch loop: the *walled session's*
`LiveSession` capacity wait was registered with `TaskId = <this task>` and `ExecutionKind = Grok`
one second before the reroute, the reroute never supersedes that wait, and the tick's gate
`HasUnfinishedCapacityWaitAsync && !TryRedeemCapacityWaitAsync → continue` matches the task by
`TaskId` and skips it on every tick with no event, no log line and no Blocked transition. The task
is therefore waiting for **Grok** capacity to be granted, even though it has already been rerouted
to Codex. Grok's hold had no reset time (6 h default → 01:23 next day), so the task sat Queued for
25 minutes until the operator cancelled it.

The same mechanism reproduced on a surviving log on 2026-09-13 (task `0c8a18f7`, fable → opus): the
task sat Queued for 68 minutes after the reroute and dispatched at the exact second the *fable*
session's wait was granted, because Claude's session limit had a stated reset time. Two wall
reroutes on 2026-09-06, before CARD-0412 shipped, dispatched within 4–5 seconds.

CARD-0535 is **a distinct root cause** (sibling-base hold, then repository-mutation lease), and its
task actually dispatched 70 s after the lease cleared; the operator cancelled it 43 s after
dispatch. The two cards share only the symptom class: a silent per-tick skip in the dispatch loop
with no escalation and, after the first Held event, no further trace.

## What is supposed to happen

- `AgentTaskService.RerouteOnWallAsync` (server/Application/Services/AgentTaskService.cs:1876) is
  called from `AgentTaskReplyService.HandleApiErrorTurnAsync` when a list-governed task's session
  dies on a `Wall` (AgentTaskReplyService.cs:1064). It re-walks the chain and, if a different
  candidate survives, calls `RequeueOnWallAsync` (AgentTaskService.cs:2027) → `RequeueAsync`
  (AgentTaskService.cs:2179): kills the session, drops the ephemeral agent, sets
  `AgentKind/ModelLevel` to the new candidate, `Status = Queued`, `Attempt++`, writes the `Rerouted`
  event, publishes `AgentTaskChanged`. The design intent (docs/orchestration-loop.md §"Complexity
  chains") is that the requeued row is picked up by the next dispatcher tick and launched on the
  fallback kind; there is no separate "redispatch" step — being Queued *is* the redispatch request.
- The dispatcher (`AgentTaskDispatcherHostedService`, one scope per 5 s tick) loads every Queued
  row in `TickAsync` (AgentTaskDispatcher.cs:327) and walks the hold ladder per task: concurrency
  cap → shared-writer scope → routing-pin `NotBefore` → model held (with chain re-walk) → root
  budget → repair-owner landing → sibling base → **capacity wait** → `DispatchOneAsync`
  (AgentTaskDispatcher.cs:2970, repository lease + claim + launch).

So the answer to "requeue against a new candidate, or wait for Grok quota to clear?" is: the
reroute *intends* the former, but the capacity gate *implements* the latter.

## The stuck path, line by line

1. **Wall detected, wait registered against the task (19:23:49.29Z).**
   `ApiErrorRecoveryService` writes the auto-detected hold and then registers a capacity wait
   (ApiErrorRecoveryService.cs:454-467):
   `ConsumerKey = session:<sid>`, `ConsumerKind = LiveSession`, `ExecutionKind = kind` (the
   *session's* kind, Grok), `TaskId = openTaskId` (line 426-430 finds the Dispatched/Working task
   on that session → `3bfe742a`). DB row `CapacityRecoveryWaits` `e86f8a7b`:
   `ConsumerKind=0, ConsumerKey=session:00b462b8…, TaskId=3bfe742a, RequestedKind=4,
   ExecutionKind=4, RequestedAlias=grok-4.6, BlockedAt=2026-09-10 19:23:49.29Z`.
   `ApiErrorRecoveries` `0414d526` (session 00b462b8, seq 476, 402 `payment_required`) is resolved
   at the same instant as `WallModelPaused` with `CapacityWaitId=e86f8a7b`, `AppliedHoldId=dac63807`.

2. **Reroute recorded (19:23:50.06Z).** `RerouteOnWallAsync` → `RequeueOnWallAsync` → `RequeueAsync`.
   Task row afterwards: `Status=Queued, AgentKind=2 (Codex), ModelLevel=1 (High), Attempt=2,
   MaxAttempts=2, DispatchedAt=null, AgentId=null, AgentSessionId=null, RoutingPinId=4683c049`,
   `FailureReason` = the reroute sentence. `AgentIncidents` row 19:23:49.90Z "hit a usage wall;
   rerouted to gpt-5.6-sol as task attempt 2". Nothing in `RequeueOnWallAsync`, `RequeueAsync` or
   `ResolveRecoveryAfterChainRerouteAsync` (AgentTaskReplyService.cs:1168) touches
   `CapacityRecoveryWaits`; the latter is also a no-op here because the recovery row was already
   resolved (`WallModelPaused`) 0.8 s earlier, so the `recovery is not { ResolvedAt: null }` guard
   returns immediately.

3. **Every tick from 19:23:55Z to 19:48:16Z: silent skip.** With the task now `Codex/High`:
   - concurrency cap (line 424): the only rows Dispatched/Working anywhere in 19:23–19:48Z were
     three short Shared Codex tasks (`aa422b34` Review 19:32–19:35, `3b2de0e0` Docs 19:35–19:36,
     `9f8fe836` Docs 19:36–19:38), never more than one at a time against
     `MaxConcurrentTasks = 6` — not the cause;
   - scope lease: Worktree task, only Shared-to-Shared waits — not the cause;
   - model held (line 517-): alias resolves to `gpt-5.6-sol`; `ModelAvailabilityHolds` had no Codex
     hold until 2026-09-11 01:41Z — block skipped, so neither `TryRewalkQueuedChainAsync` nor
     `BlockQueuedChainIfExhaustedAsync` ran (which is why there is no second "… at dispatch"
     `Rerouted` event and no `Blocked`);
   - repair owner / sibling base: `RepairSourceTaskId` null, `WorktreePath` already set
     (`C:\Antiphon\worktrees\card-task-3bfe742a`) → `EvaluateCardSiblingBaseAsync` not entered;
   - **capacity gate (AgentTaskDispatcher.cs:646-649)**:
     ```csharp
     if (_capacityRecovery is { IsEnabled: true }
         && await HasUnfinishedCapacityWaitAsync(task, ct)
         && !await TryRedeemCapacityWaitAsync(task, CapacityRedemptionPath.Dispatch, ct))
         continue;
     ```
     `HasUnfinishedCapacityWaitAsync` (line 795-806) matches `w.TaskId == task.Id ||
     w.ConsumerKey == "task:<id>"` in any state other than Progressed/Canceled/Superseded/Exhausted
     → the LiveSession wait `e86f8a7b` (Grok) matches by `TaskId`.
     `TryRedeemCapacityWaitAsync` (line 809-837) returns false when the wait is `WaitingForHold`,
     or when `CapacityRecoveryProviderStates[ExecutionKind=Grok].GrantedWaitId != wait.Id`, or when
     `RedeemAsync` refuses (`grant-mismatch`, `clock-not-due`). The redemption is keyed to
     **`wait.ExecutionKind` = Grok**, never to the task's new kind.
     The `continue` writes no `AgentTaskEvent`, no log line, and does not count in `TickResult`.
   - Independently of the gate, any later hold on this task would also have been invisible: the
     tick loads `everHeld` once from *all* prior `Held` events (line 374-380) and only writes a new
     `Held` event on `everHeld.Add(task.Id)`; `3bfe742a` already had 29 `Held: repository mutation
     lease…` events from 17:50–17:53Z, so every subsequent hold path was mute.

4. **Why Grok never granted in the window.** The Grok provider state's `NextAdmissionAt` was
   `2026-09-10 19:25:42Z` (= second recovery row `4ed58213` at 19:24:42Z + 60 s admission interval)
   and the wait's `LatestClearObservedAt` is `2026-09-11 01:24:31Z` — the instant hold `dac63807`
   (`grok-4.6 provider capacity (no reset stated)`, `DisabledUntil 01:23:48Z`) expired
   (`CapacityRecoveryWaitHolds`: `ReleaseAcknowledgedAt 01:24:31Z`). An operator kind-wide hold
   `Grok *` (`88d28198`, 18:38Z → 05:00Z next day) was also in force. So the earliest the gate
   could have opened was ~01:24Z on 2026-09-11, six hours after the reroute. Operator cancelled at
   19:48:16Z.

5. **Task-level evidence matches.** `AgentTaskEvents` for `3bfe742a`: `Rerouted` 19:23:50.06Z →
   nothing → `Canceled` 19:48:16.60Z. API detail: `blocked: null`, `capacityWaitId: null` (the task
   row's own `CapacityWaitId` is unset because the wait belongs to the *session*; the task DTO
   cannot show it).

## Reproduction on a surviving log: `0c8a18f7`, 2026-09-13

Server log retention is 5 days, so 2026-09-10 is gone, but the 09-13 log
(`C:\src\Antiphon\server\logs\antiphon-20260913.log`, local time +01:00) shows the same shape:

| local time | line |
|---|---|
| 01:44:47.102 | `Task 0c8a18f7 requeued as attempt 2 at opus: fable hit a usage wall (… resets 2:50am (Europe/London)) …` |
| 01:44:47.102 | `Task 0c8a18f7 rerouted on wall: fable → opus as attempt 2` |
| 01:44:47.467 | `Task 0c8a18f7 rerouted at dispatch: fable → opus` |
| 01:44:47 → 02:52:31 | no line for `0c8a18f7` at all (68 minutes) |
| 02:52:31.863 | `Task 0c8a18f7 (Worker/TestDesign): launching …` |
| 02:52:31.953 | `Dispatched task 0c8a18f7 (… at opus) to session ffa476c3…` |

DB: wait `006dbac3` (`ConsumerKind=0, ConsumerKey=session:bb611dde…, TaskId=0c8a18f7,
ExecutionKind=1 (ClaudeCode), RequestedAlias=fable, BlockedAt 2026-09-13 00:14:47Z`,
`LatestClearObservedAt 01:52:26Z`); `CapacityRecoveryProviderStates[Kind=1]`:
`LastActionKey=006dbac3…:0, LastActionAt=2026-09-13 01:52:31.65Z`. The dispatch at 01:52:31.83Z
(02:52:31 local) is the redemption of the **fable** session's wait, 1.5 minutes after Claude's
session-limit reset (2:50am London). The rerouted-to alias (opus) was free the whole time.

## Historical sweep — every usage-wall reroute on record

```sql
select … from "AgentTaskEvents" where "Type"=23 and "Detail" like '%usage wall%'
```

| task | rerouted at (UTC) | next event | gap | outcome | CARD-0412 gate live? |
|---|---|---|---|---|---|
| 430da29a | 2026-09-06 07:44:42 | Rerouted "opus held → grok-4.6 … at dispatch" 07:44:43; Dispatched 07:44:47 | 5 s | Failed (grok rules argv) | no |
| c2ab9503 | 2026-09-06 12:20:58 | Rerouted "fable held → gpt-6-astra … at dispatch" 12:20:59; Dispatched 12:21:02 | 4 s | Succeeded | no |
| **3bfe742a** | **2026-09-10 19:23:50** | **Canceled 19:48:16** | **24.5 min, then cancelled** | Canceled | yes |
| 0c8a18f7 | 2026-09-13 00:44:47 | Rerouted "fable held → opus … at dispatch" 00:44:47; Dispatched **01:52:31** | **68 min** | Succeeded | yes |

The gate (`HasUnfinishedCapacityWaitAsync` in the tick) landed in `82f61227` on 2026-09-08
("test(CARD-0412): execute remaining V-n matrix and wire redemption seams"); the
`CapacityRecoveryProviderStates` rows were seeded 2026-09-09 07:38Z. Both post-gate wall reroutes
stalled; both pre-gate ones dispatched within one tick. The wall reroute itself (`567769a7`,
2026-09-05, CARD-0090 S5) has not changed.

## Is this specific to a mid-flight reroute?

Yes. A fresh create never has a `LiveSession` wait carrying its `TaskId`; that row only exists
when a session that *this task owns* hits a wall. The normal create → tick → dispatch path runs the
same gate but finds no wait. The reroute path is the one place where a task changes kind while a
wait for the *old* kind still names it.

## Secondary finding: the session waits leak

`RequeueAsync`, task cancel and task settle never terminalize the `LiveSession` wait. Wait
`e86f8a7b` for the (cancelled) `3bfe742a` is still `State=2 (ActionPending)`, `Version=1479`,
`Outcome=Ready / grant-expired`, and the 09-13 log shows it being granted every ~10 minutes
(`Capacity recovery grant e86f8a7b…:336 kind "Grok" … blockedAt 2026-09-10 19:23:49Z`), three
days after the task was cancelled. Today there are 5 non-terminal `LiveSession` waits, 2 of which
name tasks that are already Succeeded/Failed/Canceled. The reconcile pass in
`CapacityRecoveryService` (line ~726-731) skips terminal tasks only for `task:` consumer keys, not
for `session:` keys. This is not what stuck `3bfe742a`, but it is the same object and any fix that
supersedes the wait on reroute should also stop the churn.

## CARD-0535 comparison (task `9fb36757`, 2026-09-14)

| UTC | evidence |
|---|---|
| 21:03:05 | created (Plan, Worktree, CARD-0492) |
| 21:03:06 | `Held` event + log line: "CARD-0492's kept branch feat/card-task-85ea60c8 (task 85ea60c8) is landing and is not yet in HEAD" — the `EvaluateCardSiblingBaseAsync` hold (AgentTaskDispatcher.cs:619). Silent thereafter (`everHeld`). |
| 21:51:58 | be0020e5 land requested; land queued (409 "land is running" to a re-POST at 21:57:27) |
| 21:56:18 → 22:05:46 | 95 × `Held: repository mutation lease is occupied or unavailable.` events (one per tick, written in `DispatchOneAsync` line 2977-2986, **no log line**) — the sibling hold had lifted, the lease was held by be0020e5's land |
| 22:06:29 | be0020e5 `landed operation=0a2a8cf1…` |
| 22:07:39 | `Dispatched task 9fb36757 (… at fable)`; "released: its scope 'null' no longer intersects a running task" |
| 22:08:22 | `POST /api/agent-tasks/9fb36757…/cancel` (operator) |

So CARD-0535's task was held by two legitimate, sequential conditions and dispatched 70 s after
the second cleared; the card's "still retrying over an hour after creation … never actually
dispatched" was observed while the lease was genuinely occupied, and the cancel landed after the
dispatch. Its root cause (if any beyond visibility) lives in the sibling-base guard, the lease
hold and the absence of a "held too long" escalation — none of which is on the wall-reroute path.
**Not the same root cause as CARD-0481.** Shared factors worth one fix, not two: (a) the dispatch
loop has several `continue` paths that leave no trace once `everHeld` contains the task, and one
(the capacity gate) that leaves no trace ever; (b) nothing turns a long Queued hold into a
Blocked/attention item.

## Remaining uncertainties

- In all three Claude-wall cases the tick 0.4 s after the reroute logged "`<walled alias> held →
  <chosen>` at dispatch", i.e. `ResolveDispatchAliasAsync` resolved the *pre-reroute* alias from a
  row the reply path had just rewritten. The hosted service creates a fresh scope per tick, so this
  is not a stale tracked entity; it is unexplained here and did not change the outcome (the chain
  re-walk chose the same candidate the reroute had). Not on the Grok path (`3bfe742a` has no such
  event because `gpt-5.6-sol` was not held).
- The exact state (`WaitingForHold` vs `Ready`) of wait `e86f8a7b` during 19:23–19:48Z cannot be
  read back (only the current row survives); both states make `TryRedeemCapacityWaitAsync` return
  false, so the verdict does not depend on it.
- Server logs for 2026-09-10 are past the 5-day retention; the 3bfe742a reconstruction is from
  DB rows plus the 09-13 log reproduction.

## Affected code paths

- `server/Application/Services/AgentTaskDispatcher.cs:646-649` — the silent capacity gate;
  `:795-806` `HasUnfinishedCapacityWaitAsync` (matches by `TaskId`, any kind);
  `:809-837` `TryRedeemCapacityWaitAsync` (redeems against `wait.ExecutionKind`, not the task's kind).
- `server/Application/Services/AgentTaskService.cs:2027-2076` `RequeueOnWallAsync`, `:2179-2237`
  `RequeueAsync` — change kind/level without superseding the old-kind wait.
- `server/Application/Services/AgentTaskReplyService.cs:1040-1071` wall branch,
  `:1168-1183` `ResolveRecoveryAfterChainRerouteAsync` — resolves the recovery row only, not the wait.
- `server/Application/Services/ApiErrorRecoveryService.cs:426-467` — registers the `LiveSession`
  wait with `TaskId = openTaskId`.
- `server/Application/Services/CapacityRecoveryService.cs:330-372` `EnsureWaitCoreAsync`,
  `:726-731` reconcile (terminal-task skip only for `task:` keys).
- `server/Application/Services/AgentTaskDispatcher.cs:374-380, 450, 483, 555, 596, 622` —
  `everHeld` single-trace rule (visibility, shared with CARD-0535).

## Not done, noted

- Fix idea (one line, not designed): on `RequeueOnWallAsync`, supersede/cancel any non-terminal
  wait whose `TaskId` is this task and whose `ExecutionKind` differs from the new kind (or scope
  the dispatcher gate to waits whose `ExecutionKind == task.AgentKind`), and make the gate's
  `continue` write a once-per-transition `Held` event / count in `TickResult`.
- The wait leak for terminal tasks and the "held too long → attention" escalation are adjacent
  and could ride the same card or CARD-0535; that is a planning decision.
