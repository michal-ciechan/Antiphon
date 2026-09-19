# CARD-0537: follow-up (`-OnAgent`) dispatch silently never delivered

Status: Investigation. Root cause confirmed from stored rows, the 2026-09-15 server log and the code at `08c34ff1`. Not reproduced in a test; no test covers the branch (see Uncertainties). Investigate task: `96612a31`. Card: CARD-0537 (Antiphon board).

## Verdict

The `TranscriptBoundByDiscovery` incident is unrelated. The follow-ups were pinned to a pool delegate whose previous task had settled **Blocked**, and a Blocked settlement does not release the delegate to the pool. The dispatcher's warm-reuse path then answered `WaitForAgent` on every 5-second tick, which is a silent `return false`: no event, no log line, no timeout, no attention row. The task sits Queued until the pinned agent goes Idle, which for a Blocked prior task never happens on its own.

| Ask | Answer |
|---|---|
| 1. Root cause; discovery binding? | `AgentTaskDispatcher.TryReuseWarmAgentAsync` returns `WaitForAgent` when the pinned pool delegate is not `Idle` with a `PoolIdleSince` stamp (`AgentTaskDispatcher.cs:4643-4644`). The prior task on that agent, `c40cf169`, settled Blocked at 22:09:25Z; settlement releases a pool delegate only on Succeeded (`AgentTaskReplyService.cs:822-823`), so the agent stayed `Running` from its reuse at 21:55:38Z. Discovery binding is never read on this path, and the same incident preceded the follow-up that worked. |
| 2. Timeout or escalation for a Queued task with no dispatch event? | None. `WaitForAgent` bypasses `TraceHeldAsync`; no `Held` event, no `HeldAged`, no attention kind, no deadline. The CARD-0535 plan (holds visibility) leaves this branch silent by design (`NotClaimed` stays silent). The janitor never retires an agent with a Blocked task, so nothing ever changes. A follow-up counts as an open occupant for the project cap (`DelegationOpenGate.cs:20-25`) while its own creation skipped that gate (`AgentTaskService.cs:1055-1058`). It can genuinely sit forever. |
| 3. Was stop + cancel the right recovery? | It recovered, at the cost of the context the follow-up wanted and of the agent row and its incident history. The supported path that keeps context is to answer the Blocked task (`delegate.ps1 -Reply c40cf169 ...`): the reply moves it to Working, its next done report settles Succeeded, the release marks the agent Idle, and the queued follow-ups dispatch on the next tick by reuse. Stop alone would have relaunched the same row with a fresh session (no context). The cancel deleted the pool agent row (`AgentTaskService.cs:1837`, `:2380-2388`), which cascaded its incidents; that is why neither row exists today. `c40cf169` is still Blocked with a dangling `AgentId`. |

## Timeline (UTC), from `AgentTasks`, `AgentTaskEvents`, `AgentSessions`, `SessionQueuedMessages` and `server/logs/antiphon-20260915.log`

| When | Event | Source |
|---|---|---|
| 09-14 21:34:48 | `9474e409` dispatched to new pool delegate `task-9474e409` (agent `9eae0875`, session `a5ffc152`, Codex gpt-6-astra) in `C:\src\markdown-package`. | events type 1; `SessionQueuedMessages` seq 1 |
| 09-14 21:35:03 | `TranscriptBoundByDiscovery` (Info) on session `a5ffc152`. | card; row later cascade-deleted (see 07:26:28) |
| 09-14 21:51:37 | `9474e409` settled Succeeded. Release path pooled the agent warm (Idle, `PoolIdleSince`, reserved for its run). | events type 8; `AgentTaskReplyService.cs:1757-1773` |
| 09-14 21:55:34 | `c40cf169` created with `followUpOnTask=9474e409`. `FollowUpOfTaskId` = `9474e409`. | `AgentTasks` |
| 09-14 21:55:38 | `c40cf169` dispatched by warm reuse, 4 s after create: `Reused warm delegate 'task-9474e409' ... focused /compact first`. Agent set `Running`, `PoolIdleSince = null`. | events type 1; `AgentTaskDispatcher.cs:4706-4709`; queue seq 2 |
| 09-14 22:09:25 | `c40cf169` settled **Blocked** (`verdict: blocked`, 3,516 chars). `Status=3`, `CompletedAt` set. Its report says publication "remains blocked by the Code-stage requirement for Review and caller landing", a hand-back rather than a question. `shouldRelease` is false, so the agent stays `Running`. | events type 3; `AgentTaskReplyService.cs:2742`, `:822-823` |
| 09-14 22:21:07 | `a4207657` created with `followUpOnTask=9474e409`. Server resolved the prior task's agent (`9eae0875`, still present), took the live-follow-up arm, pinned `AgentId=9eae0875`, returned `FollowUpMessage: follow-up on the live agent`. Only a `Created` event. | `AgentTaskService.cs:325-386`; events |
| 09-14 22:21 to 09-15 05:26 | Every tick: `queued` list loaded, no cap skip (1 non-specialist in flight), no lease hold (nothing writing in that checkout), `DispatchOneAsync` → claim → `TryReuseWarmAgentAsync` → pinned row exists, is a pool delegate, session live, kind and project match, `Status == Running` → `WaitForAgent` → rollback, `return false`. No row, no log. | reconstruction from code; 09-14 log rolled off |
| 09-15 05:25:31 | `08e67be4` created, same `-OnAgent 9474e409`. Same fate. | log line 7550; events |
| 09-15 05:26:09 | Fresh non-follow-up dispatch refused 409 `concurrency_limit`: "2 Code tasks already in flight ... a4207657 Code Queued, 08e67be4 Code Queued" (project role cap 1). | log line 7597 |
| 09-15 05:26:57 | `c7a1ff79` created with `ignoreConcurrencyLimit=true` (event: "2/3 open (limit 3), 2/1 Code (limit 1)"). | events type 12 |
| 09-15 05:26:58 | Same tick, ordered by `CreatedAt`: `a4207657` and `08e67be4` processed first and skipped silently; `c7a1ff79` launched on a new agent `task-c7a1ff79` (`80398495`, session `10deadf6`) 1 s after create. Proves the cap and the lease were clear and the only silent branch is `WaitForAgent`. | log 7620-7625; events |
| 09-15 05:27:04 | Next tick: both follow-ups get their first and only `Held` event, behind `c7a1ff79` (Shared-to-Shared lease). This is the proof the dispatcher saw them every tick. | events type 18; log 7638-7639 |
| 09-15 05:27:40 | `TranscriptBoundByDiscovery` on the **working** agent `80398495` / session `10deadf6`. | `AgentIncidents` `950ea77e` |
| 09-15 05:37:40 | `c7a1ff79` settled Succeeded. Lease clears; the follow-ups return to silent `WaitForAgent`. | events type 8 |
| 09-15 07:24:42 | `POST /api/agents/9eae0875/stop`: session `a5ffc152` killed (`Status=4`, `TerminationSource=1 OperatorRequest`, `EndedAt 07:24:42.571`), agent `Stopped`. | log 9871-9875; `AgentSessions`; `AgentControlService.cs:976-1024` |
| 09-15 07:26:28 / :29 | `POST /api/agent-tasks/{a4207657,08e67be4}/cancel`: `Canceled` events; `RemoveEphemeralAgentAsync` deleted pool row `9eae0875` (any pool delegate, `Ephemeral` flag not consulted), cascading its `AgentIncidents`. | events type 10; `AgentTaskService.cs:1824-1848`, `:2380-2388` |
| today | `Agents` has no `9eae0875`; `AgentIncidents` has no row for it or session `a5ffc152`; `c40cf169` is the only Blocked task in the database whose `AgentId` no longer exists. | queries below |

Event counts today: `a4207657` and `08e67be4` each have exactly Created, Held, Canceled. `c40cf169` has Created, Dispatched, Check, Blocked. No `Replied` event on `c40cf169`: the question was never answered.

## Mechanism

### Create: a follow-up pins to the prior task's agent without checking what that agent is doing

`AgentTaskService.CreateAsync`, `server/Application/Services/AgentTaskService.cs:325-386`. When `FollowUpOnTask` is set it resolves the prior task, loads `prior.AgentId`, and if the agent row exists takes the live arm: `liveFollowUp = true`, `Workspace = Shared`, `AgentId = followAgent.Id`, message "follow-up on the live agent". It checks neither `prior.Status` (Blocked here) nor `followAgent.Status` nor `PoolIdleSince`. A live follow-up also skips the create-time open gate (`:1055-1058`, "no new process"), so the caller receives 200 Queued with no warning.

### Dispatch: the pinned agent is not Idle, so reuse waits, silently

`AgentTaskDispatcher.DispatchOneAsync`, `server/Application/Services/AgentTaskDispatcher.cs:3224-3242`:

```
switch (await TryReuseWarmAgentAsync(claimed, now, ct))
  case Reused: ... return true;
  case Expired: ... return false;
  case WaitForAgent:
      // The pinned agent is mid-task. Delivering the follow-up now would land it
      // BETWEEN the running task's turns ... wait for the settle → pool handshake
      await transaction.RollbackAsync(ct);
      return false;
```

`TryReuseWarmAgentAsync`, `:4570-4644`, for a pinned pool delegate: returns `SpawnFresh` when the row is gone (`:4588`), the session is not Starting/Running (`:4596`, via `LiveSessionIdOfAsync` `:5337-5346`), the directory is a worktree, the kind differs, the pool project differs, or the inherited env differs; otherwise

```
if (pinned.Status != AgentStatus.Idle || pinned.PoolIdleSince is null)
    return ReuseOutcome.WaitForAgent;                       // :4643-4644
```

Nothing on this path reads the transcript binding. The only session fact consulted is `AgentSessions.Status`.

The comment assumes "mid-task" means a task that will settle and hand the agent back. `AgentStatus.Running` is a lifecycle latch, "has a live session", not "mid-turn" (`server/Domain/Enums/AgentStatus.cs:7-13`). The card's observation `status: Running, working: false, queue: []` is exactly this state: alive, idle at the prompt, latched Running.

### Settle: Blocked keeps the agent, and nothing else ever frees it

`AgentTaskReplyService.cs:822-823`:

```
var shouldRelease = task.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress
    || task.Status == AgentTaskStatus.Succeeded;
```

`ReleaseDelegateAsync` (`:1732-1773`) is the only code that sets a pool delegate `Idle` with `PoolIdleSince` after work (the other writers are the reuse and spawn paths setting `Running`, `:4706-4709`, `:3408`). It runs only for Succeeded (and the no-progress arm). A Blocked task keeps its session so the answer can be typed into it (`AnswerAsync`, `:300-330`: Blocked → Working, enqueue the answer on `task.AgentSessionId`). That is deliberate, and the consequence is that the agent's `Status` stays `Running` until that task is answered and later settles Succeeded, or the agent is stopped.

The pool janitor cannot intervene: `RetireIdleWarmAgentsAsync` only considers rows with `Status == Idle` and `PoolIdleSince != null` for retirement (`:5064-5067`) and treats agents with a Queued, Dispatched, Working **or Blocked** task as busy (`:5110-5118`).

So the state machine is: Blocked prior task → agent `Running` forever → every follow-up pinned to it `WaitForAgent` forever → follow-ups count as open occupants for the project cap (`DelegationOpenGate.cs:20-25` counts Queued) → every further Code dispatch in the project 409s until someone intervenes.

### Why the fresh dispatch worked

`c7a1ff79` had no `AgentId`, so `TryReuseWarmAgentAsync` went to the unpinned pool shop (`:4648-4682`), which selects only `Idle` rows with a `PoolIdleSince` stamp; `9eae0875` was `Running`, so it was not a candidate, and a fresh agent was spawned. It dispatched 1 s after creation in the same tick that silently skipped both follow-ups.

## Discovery binding is not involved

- `TranscriptBoundByDiscovery` is an Info timeline row with no alert, written when the tailer bound a transcript by cwd discovery, a fork or the restart shim instead of the exact filename (`TranscriptBindingIncidentService.cs:278-310`). Every adoption rule still passed.
- On the affected session the incident (21:35:03Z) preceded the follow-up that **worked**: `c40cf169` was delivered by warm reuse at 21:55:38Z and completed a full turn (`SessionQueuedMessages` seq 2, `DeliveryVerdict 0`, `DeliveryVerdictAt 21:55:41Z`).
- The working fresh agent `80398495` logged the same incident at 05:27:40Z (`AgentIncidents` `950ea77e`) and settled normally. The `AgentIncidents` table holds 15 such rows for 2026-09-15 alone; it is routine for Codex sessions.
- The dispatch and reuse paths never read binding state. The delivery mechanism for a reused agent is the session message queue (`DeliverReuseMessagesAsync`, `:3229`), which was never reached.

## Ask 2: what exists today for a stuck-Queued task

| Surface | Covers this case? | Evidence |
|---|---|---|
| `Held` events via `TraceHeldAsync` (`AgentTaskDispatcher.cs:713-726`) | No. Only the loop's `continue` branches call it (scope, dated pin, model hold, repair-source landing, sibling landing, capacity wait). `WaitForAgent` is a `return false` inside `DispatchOneAsync`. | `:3237-3241` |
| Concurrency-cap skip (`:427-432`) | Not this case, and itself silent today. | `:430` |
| CARD-0535 plan `HeldAged` / `DispatchHeld` (`docs/superpowers/plans/2026-09-19-card-0535-dispatch-hold-visibility-plan.md`) | No. D-1 makes `DispatchOneAsync` three-valued (`Dispatched`, `HeldOnLease`, `NotClaimed`) and states "`NotClaimed` ... stays silent as today"; `WaitForAgent` would land in `NotClaimed`. D-3 escalates only hold kinds recorded by the loop's `continue` branches. | plan lines 43, D-3 |
| `ExecutionDeadlineAt` | Only `OptionalWork` roles are auto-canceled at the deadline (`:325-330`); these tasks have `ExecutionDeadlineAt = null`. | `AgentTasks` rows |
| Check-ins (`NextCheckAt`, `CheckCount`) | Armed at dispatch (`ArmFirstCheck`), so `NextCheckAt = null` on a never-dispatched task. | rows: `NextCheckAt` null, `CheckCount 0` |
| `TaskDeadlinePolicy` | Measures from `DispatchedAt`; not applicable. | `TaskDeadlinePolicy.cs:52-56` |
| Attention kinds (`AttentionDtos.cs:13-285`) | Nothing keyed on Queued age. `AgentOutlivedTask` excludes pool delegates and agents with open tasks. `BlockedQuestion` exists for `c40cf169` itself, which is the one signal the caller could have acted on. | enum listing |
| Pool janitor | Protects, never retires, an agent with a Blocked task. | `:5110-5118` |
| `delegate.ps1 -Status` | Prints `dispatchedAt` only; nothing says why. | card |

Conclusion: a follow-up pinned to a Blocked agent has no clock and no signal at all. It can sit forever and it holds a role slot in the project cap the whole time.

## Ask 3: recovery paths, as implemented

| Action | Effect on the pinned agent | Effect on the queued follow-ups | Context kept? |
|---|---|---|---|
| `POST /api/agent-tasks/c40cf169/reply` (`delegate.ps1 -Reply`) | Task → Working; answer typed into the live session (`AgentTaskReplyService.cs:300-330`). On its next `done` report → Succeeded → `ReleaseDelegateAsync` → Idle + `PoolIdleSince`. | Next tick: `TryReuseWarmAgentAsync` → `Reused`; brief delivered into the same session. | Yes |
| `POST /api/agent-tasks/c40cf169/cancel` | `StopDelegateAsync` kills the session; `RemoveEphemeralAgentAsync` deletes the pool row (`:1837`, `:2380-2388`). | Pinned row gone → `SpawnFresh` (`:4588-4589`) → new agent, new session. | No |
| `POST /api/agents/9eae0875/stop` (what was done first) | Session killed, agent `Stopped` (`AgentControlService.cs:976-1024`); row kept. | Session not live → `SpawnFresh` (`:4596`) → `ResolveAgentAsync` reuses the row (`:4254-4272`) with a new session. Would have relaunched without the follow-up being canceled. | No |
| cancel the follow-ups (what was done second) | First cancel deleted the pool row and its incidents. | Canceled; project slot freed. | n/a |
| `POST /api/agent-tasks/{id}/refine` on a Blocked task | Refused: "waiting for an ANSWER, reply to its question instead" (`:524-527`). | n/a | n/a |
| `POST /api/agent-tasks/{id}/retry` on a Queued task | Refused: "has not run yet, it is already queued" (`AgentTaskService.cs:1859-1863`). | n/a | n/a |

Reply was the only path that would have delivered the follow-ups with the context `-OnAgent` was chosen for. The fresh dispatch the orchestrator fell back to is the equivalent of the stop path, minus the wait.

Residue today: `c40cf169` is Blocked with `AgentId = 9eae0875` (deleted) and `AgentSessionId = a5ffc152` (Stopped). A reply to it now would enqueue onto a dead session. It should be canceled.

## Uncertainties

1. **The 2026-09-14 server log has rolled off** (`server/logs/` holds 09-15 onward). The 22:21Z to 05:25Z stretch is reconstructed from the event rows plus code; the 05:26:58Z tick (both follow-ups skipped silently while a third task launched between them and the next tick's `Held`) is direct evidence of the same mechanism and is in the retained log.
2. **Why nothing relaunched in the 106 s between the stop (07:24:42Z) and the cancels (07:26:28Z).** By code, the stop should have turned `WaitForAgent` into `SpawnFresh` and relaunched the row within a tick. The log shows ticks running in that window (delivery-watchdog lines every 5 s, with gaps to 42 s around two 15 s git timeouts at 07:25:15Z) and no dispatcher line for either task, and no new `Held` event was written even though `98eb8a0a` (Code, Shared, same checkout) was running from 07:23:24Z and should have produced one. Both facts point at the loop not reaching the lease check, which is the shape of the silent cap skip (`:427-432`). Reconstructed in-flight count at 07:24Z and 07:26Z is 3 non-specialist against the default `MaxConcurrentTasks` 6, but that reconstruction is a floor (a later retry overwrites `DispatchedAt`), and the tick result is logged only at Debug. Unresolved; it does not change the root cause. CARD-0535 D-2 will trace the cap skip.
3. **Not reproduced in a test.** `grep WaitForAgent tests/` finds nothing; the branch is untested. A reproduction is: seed a pool delegate `Running` with a live session, a Blocked task on it, a Queued task pinned to it, tick, assert the task is still Queued with no event.
4. Whether `c40cf169`'s `blocked` verdict was the delegate's honest state or a misuse of the token (its body reads as "done, publication needs Review and landing") is a separate question about report vocabulary; either way the platform behaviour above is the same for any Blocked prior task.

## Not done, noted

- Fix idea (create): refuse or warn a live follow-up whose prior task is Blocked or whose pinned agent is not Idle, naming the Blocked task and the reply verb; alternatively let a follow-up on a Blocked prior task act as the reply.
- Fix idea (dispatch): route `WaitForAgent` through `TraceHeldAsync` with a stable detail naming the pinned agent and the task it is parked on, so CARD-0535's `HeldAged` and `DispatchHeld` escalation covers it.
- Fix idea (cancel): `RemoveEphemeralAgentAsync` deletes any pool delegate regardless of `task.Ephemeral` and cascades its incidents; a follow-up's cancel should not erase the shared agent's history.
- Housekeeping: cancel `c40cf169`.

## Queries used

```
select ... from "AgentTasks" where "Id"::text like 'a4207657%' or ... '08e67be4%' or ... 'c7a1ff79%' or ... '9474e409%' or ... 'c40cf169%';
select left("AgentTaskId"::text,8),"Type","At",left("Detail",400) from "AgentTaskEvents" where ... order by "At";
select * from "AgentSessions" where "Id"='a5ffc152-c3e0-403a-a324-5d8ac5e1775d';
select * from "Agents" where "Id"::text like '9eae0875%' or "Name" like 'task-9474e409%';           -- 0 rows
select * from "AgentIncidents" where "AgentId"::text like '9eae0875%' or "SessionId"='a5ffc152-...';   -- 0 rows
select ... from "AgentIncidents" where "Kind"=16 order by "CreatedAt" desc limit 15;                   -- 15 rows on 09-15
select ... from "SessionQueuedMessages" where "AgentSessionId"='a5ffc152-...' order by "CreatedAt";     -- 2 rows, seq 1 and 2
select count(*) from "AgentTasks" where "Status"=3 and "AgentId" is not null and not exists (select 1 from "Agents" a where a."Id"="AgentTasks"."AgentId");  -- 1
grep -n "a4207657\|08e67be4\|9eae0875\|task-9474e409\|a5ffc152" C:\src\Antiphon\server\logs\antiphon-20260915.log
```
