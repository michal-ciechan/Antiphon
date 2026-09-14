# CARD-0492 — a task settles Failed on a transient API error while its own resume keeps the session working

Date: 2026-09-14. Stage: Investigate (task 85ea60c8). Verdict: **root cause confirmed** from stored
rows and server logs; two independent occurrences tonight reconstruct identically. No fix designed.

## One-paragraph summary

The "self-recovery" the card describes is Antiphon's own CARD-0072 resume ladder, not the provider's
harness: the `Your previous turn was killed by a transient API error…` prompt at transcript seq 156 is
`ApiErrorRecoverySettings.TransientPrompt` (`server/Application/Settings/SupervisionSettings.cs:238`),
typed by `ApiErrorRecoveryService.FireOneAsync`. The ladder then marks its own success as
`ResolvedReason = Superseded` on its next rung, because its own resume prompt is a `UserPrompt` newer than
the stub (`server/Application/Services/ApiErrorRecoveryService.cs:255-261`). Meanwhile the resumed turn
has not ended, so the newest `TurnEnd` in the session is still the API-error stub. The first assistant
text of the resumed turn re-triggers task settlement (`AgentSessionRuntime.cs:384` →
`DispatchChannelRepliesAsync` → `AgentTaskReplyService.OnTurnEndAsync`); `ExtractMarkedTurnAsync` picks
that stale stub as the turn's end (`AgentTaskReplyService.cs:1910-1913`, `:1980`), and
`HandleApiErrorTurnAsync` finds the recovery row already resolved, so it falls through its only two
non-failing branches (unresolved → defer; wall → hold) into `Failed` with the text
`Recovery ended (Superseded)` (`AgentTaskReplyService.cs:1074-1121`). The release then pools the live,
working session as an idle warm delegate (`:1619-1622`), and the pool janitor kills it 60 minutes later
without checking whether it is working (`AgentTaskDispatcher.cs:4756`, `:4848`, `:4939`). Nothing listens
for the session's later report once the task row is terminal (`AgentTaskDispatcher.cs:2313-2315`,
`AgentTaskReplyService.cs:106-110`).

## Timeline of task 6bb32617 (all UTC; server log lines are stamped +01:00)

Sources: `AgentTasks`, `AgentTaskEvents`, `TranscriptEntries`, `ApiErrorRecoveries`,
`SessionQueuedMessages`, `AgentIncidents`, `AgentSessions`, `Agents` rows for task
`6bb32617-1651-487f-a147-3c96ae8e1325` / session `8e05f112-9728-4319-9afa-67d995a62c2a` / agent
`a0b87cb1-5a4e-41e7-8f09-56b9ca998361`; `C:\src\Antiphon\server\logs\antiphon-20260914.log`.

| Time (UTC) | Evidence | What happened |
|---|---|---|
| 12:34:54 | `AgentTasks.CreatedAt`; event type 0 | Task created: Worker/Custom, High (opus), **Shared** workspace, `C:\src\Antiphon`, bound to CARD-0508. |
| 12:34:59 | event type 1; log `Dispatched task 6bb32617 … to session 8e05f112` | Dispatched to pool delegate `task-6bb32617`. |
| 12:35:10 | transcript seq 1 `UserPrompt` `[antiphon-task:6bb32617] …` | Brief delivered. One long working turn follows: seq 2–153 are tool calls/results with **no TurnEnd** (the query for `UserPrompt`/`QueuedUserPrompt`/`TurnEnd`/`IsApiError` rows returns only seq 1, 154, 155, 156). |
| 13:07:22.25 | seq 154 `AssistantText` IsApiError=t, ApiErrorClass `server_error`, ApiCallId `a5065de6…`, text `API Error: Can't reach the API server — check your internet or DNS (ENOTFOUND)`; seq 155 `TurnEnd` IsApiError=t, StopReason `stop_sequence`, same ApiCallId | The API killed the turn. Both rows persisted at 13:07:22.51 / .53. **No settlement ran**: no `Adopted`, `deferred`, or `failed` log line for this session between 14:07:22 and 14:08:04 (+01:00). |
| 13:08:04.95 | `ApiErrorRecoveries` row `24f9f2c1`: StubSequence 155, Classification 2 (Transient), DetectedAt 13:08:04.95, EvidenceAt 13:07:22.25; log `Adopted API-error stub … seq 155 as "Transient" (nextAttempt=2026-09-14 13:09:04Z, reason=null)` | The one-minute supervisor sweep adopted the stub (rung 1 = +1 min). |
| 13:09:14.42 | `SessionQueuedMessages` `659b5c92` Origin 5 (Supervision), CreatedAt 13:09:14.42, **SentAt 13:09:14.44**, body = `TransientPrompt` | Rung 1 fired; the queue found the session idle and typed the resume within 15 ms. |
| 13:09:14 | `AgentIncidents` kind 23 (13:20:09): `Idle auto-compact … submitted at 13:09:14Z but no (manual) CompactBoundary appeared within 10 minutes` | Side observation: the compaction service also saw the session idle at the same second and typed `/compact`; no boundary ever appeared. Not investigated further. |
| 13:09:15.63 | seq 156 `UserPrompt` `Your previous turn was killed by a transient API error. Review where you got to and continue the work you were doing…` | The resume prompt landed in the transcript. |
| 13:09:16.16 | recovery row AttemptCount 1, LastEnqueuedAt 13:09:16.16; log `Enqueued API-error resume … (attempt 1, next=2026-09-14 13:12:16Z)` | Rung 2 scheduled at +3 min. |
| 13:09:18 → 13:12:19 | seq 157–172: 8 tool calls / 8 results (git log, git diff, `dotnet run … 1 succeeded`) | The session resumed immediately and worked. Task still `Working`. |
| 13:12:34.38 | recovery row ResolvedAt 13:12:34.38, ResolvedReason **Superseded**, NextAttemptAt null | Rung 2 fired (sweep at 13:12:34 for a 13:12:16 due time). `FireOneAsync` found a `UserPrompt` with Sequence > 155 — its own resume at seq 156 — and resolved the row Superseded (`ApiErrorRecoveryService.cs:255-262`). No log line; the resolve is silent. |
| 13:12:58.33 / persisted 13:13:00.34 | seq 173 `AssistantText` ApiCallId `msg_011Cf3PYut…`: `I found a real ordering gap in the attention read. Let me fix it and write the LM tests.` | First assistant text of the resumed turn. Its arrival is the trigger for the next row. |
| **13:13:00.36** | `AgentTasks.Status` 5 (Failed), `CompletedAt` 13:13:00.36, `FailureReason` = `The delegate's turn was killed by an API error (Transient: server_error) — API Error: Can't reach the API server … Recovery ended (Superseded). The error text is not a report and no report exists. The work may well be real — read session 8e05f112… before re-running this task.`; event type 9 (Failed); log 14:13:00.497 `Task 6bb32617 failed: session 8e05f112's turn was killed by an API error ("Transient": server_error/null)` | **Settled Failed 16 ms after seq 173 was persisted.** No `ApiErrorDeferred` (type 15) event was ever written for this task. |
| 13:13:00.49 | `AgentIncidents` kind 22 severity 1: `Task 6bb32617 was killed by an API error, not finished: Transient (server_error). The task is Failed …` | Incident raised from the fail arm. |
| 13:13:00.50 | `SessionQueuedMessages` on caller session `39c6eb3a…`, SentAt 13:13:00.55: `[task 6bb32617 failed] CARD-0508 remaining V/R · opus · 38m01 The delegate's turn was killed … Recovery ended (Superseded) … read session 8e05f112… before re-running this task.` | The caller received the failure note. It names the session but says nothing about it being alive. |
| 13:13:00.53 | log `Delegate 'task-6bb32617' pooled warm in C:\src\Antiphon (reserved for run 6bb32617 first)` | Release: agent Status → Idle, `PoolIdleSince` = 13:13:00. The session was mid-turn. |
| 13:13:00 → 14:10:18 | seq 174–330: **86 ToolCall, 85 ToolResult, 3 AssistantText** after seq 156; last activity 14:10:18.52. Texts at 13:28:20 (`Now I'll write the four DR retention tests.`) and 13:33:20 (`Now the DBN component test file — the largest remaining batch.`) | 57 more minutes of real work on the same worktree, invisible to the task, which stayed Failed. No `TurnEnd` after seq 155 — the resumed turn never ended before the kill. |
| 13:13:17.63 | `AgentTasks` `dd6975f3` CreatedAt; events type 18 `Held: repository mutation lease is occupied or unavailable` at 13:13:18/23/28 | The caller re-dispatched 17 s after the failure note, onto the same repo/branch. The lease it waited on belonged to something else; 6bb32617's lease was gone because 6bb32617 was terminal. |
| 14:04:39 | `SessionQueuedMessages` `089369eb` Origin 0, Status 0 (Pending, never sent): `Status check: a second worker was accidentally dispatched onto this same worktree/branch (task dd6975f3) and c…`; log `POST /api/sessions/8e05f112…/messages` 200 | The orchestrator, having found the session alive by hand, queued a WhenIdle message. It never delivered: the session never went idle again. |
| 14:04:45 | `dd6975f3` Status 4 (Succeeded), Result begins `Stood down — no further commands run against C:\Antiphon\worktrees\card-task-be0020e5 or feat/card-task-be0020e5 …` | The duplicate discovered the collision itself and stood down. |
| **14:13:04.76** | `AgentSessions.EndedAt` 14:13:04.76, Status 4 (Stopped), ExitCode 1, FailureReason `Process exited (KilledByRequest, code 1).`, TerminationSource 2 (SystemRequest); `Agents.UpdatedAt` 14:13:04.59, Status 4; log 15:13:04.783 `Retired warm delegate 'task-6bb32617' from C:\src\Antiphon (idle since 2026-09-14T13:13:00.3608270Z)` | **The pool janitor killed the live session exactly `PoolIdleRetireMinutes` (60) after the false Failed.** Its last transcript row was 2m46s earlier; the last tool result at 14:09:50 shows a chunked `dotnet run` test sweep (`=== c508b-ns-… ===`) in progress. |

## Mechanism, step by step (file:line)

1. **The stub's own arrival never reaches task settlement.** `AgentSessionRuntime.ObserveTranscriptAsync`
   (`server/Application/Services/AgentSessionRuntime.cs:349`) flushes the idle path only for an
   `IsTurnBoundary` entry (`:376`, `:460-473`): `end_turn`, Grok `cancelled`/`error`, or the interrupt
   marker. A Claude stub `TurnEnd` carries `stop_sequence`, so it is not a boundary — deliberately
   (CARD-0071 plan §1.4). The `AssistantText` arm (`:384` → `DispatchChannelRepliesAsync` `:561` →
   `taskReplies.OnTurnEndAsync` `:579-581`) does run for the stub text at seq 154, but at that instant the
   session has **no** `TurnEnd` after the brief (seq 155 is persisted 18 ms later), so
   `ExtractMarkedTurnAsync` returns `Nothing` (`AgentTaskReplyService.cs:1910-1915`). Evidence: no
   `Adopted`/`deferred` log line and no `ApiErrorDeferred` event at 13:07; the adoption is the sweep's,
   42 s later.
2. **The supervisor sweep adopts and resumes.** `ApiErrorRecoveryService.AdoptAsync` (`:143`) →
   `EnsureAdoptedAsync` (`:78`) → `BuildNewRowAsync` classifies `server_error` as Transient
   (`ApiErrorClassifier.cs:31-33`) and sets `NextAttemptAt = now + 1 min`
   (`ApiErrorRetrySchedule.TransientRungsMinutes = [1,3,5,10,30,60]`). `FireOneAsync` (`:237`) enqueues
   `TransientPrompt` WhenIdle (`:289-291`); the session is idle so it is typed at once (queue row
   `659b5c92`, Sent 15 ms after Created).
3. **The ladder records its own success as `Superseded`.** On the next due rung `FireOneAsync` checks for
   any `UserPrompt` with `Sequence > StubSequence` (`:255-262`) and resolves the row `Superseded`. The
   resume prompt it typed is exactly such a row. This is the intended CARD-0072 semantics — plan step 3
   in `docs/superpowers/plans/2026-08-19-card-0072-api-error-timed-retry-plan.md:180`, and the test
   comment at `tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs:460` ("A resume that
   landed as a UserPrompt is Superseded, not a failed attempt"). For the ladder, Superseded = done.
4. **The resumed turn's first text re-triggers settlement against the stale stub.** Seq 173 arrives →
   `AgentSessionRuntime.cs:384` → `OnTurnEndAsync` → `OnTurnEndLockedAsync` finds the open task
   (`AgentTaskReplyService.cs:106-110`) → `ExtractMarkedTurnAsync` takes the **newest `TurnEnd`**
   (`:1910-1913`), which is still stub seq 155 because the resumed turn has not ended; the prompt walk-back
   lands on seq 1 (the marker) since seq 156 is a prompt *after* 155 and the walk-back takes the last
   prompt *before* the end (`:1929`). `IsApiErrorStub(end)` (`:1980`) yields `ApiErrorStubFacts` for
   seq 155 → `HandleApiErrorTurnAsync` (`:123-129`, `:1021`).
5. **`HandleApiErrorTurnAsync` has no branch for "the session has moved on".** It re-reads the recovery
   row (`:1033-1037`), then: the `laterPrompt` guard at `:1042-1058` exists **only** inside the
   `IsListGoverned && Wall` reroute branch (added by CARD-0090, commit `ce69a77e`; its comment names this
   exact hazard: "AssistantText re-triggers OnTurnEndAsync while the newest TurnEnd is still this stub");
   `recovery is { ResolvedAt: null }` → defer (`:1074-1078`) is false because rung 2 already resolved it;
   the wall/capacity branch (`:1080-1101`) does not apply to Transient; so it reaches
   `terminalReason = recovery?.ResolvedReason` (`:1103`) and writes `Failed` with `Recovery ended
   (Superseded)` (`:1104-1121`). The method's own doc comment (`:1010-1019`) lists NeedsHuman,
   Unknown-exhausted and wall-parked as the intended Fail cases; Superseded is not among them. Introduced
   with the fall-through in commit `05b9f083` (CARD-0072).
6. **Release pools a working session as idle.** `PersistDeliverThenReleaseAsync` → the release arm at
   `AgentTaskReplyService.cs:1619-1630`: `Shared && sessionAlive` (alive = DB status Starting/Running,
   `:1600-1603`; no working check) → `Status = Idle`, `PoolIdleSince = now`, reserved for the root task.
   The session is mid-turn at that moment (seq 174 ToolCall persisted 180 ms later).
7. **The janitor kills it on the idle clock alone.** `RetireIdleWarmAgentsAsync`
   (`AgentTaskDispatcher.cs:4747`) selects `Status == Idle && PoolIdleSince <= now - 60 min` (`:4756-4760`)
   and calls `KillPooledSessionAsync` (`:4848` → `:4939` → `_sessions.KillAsync`). No
   `SessionMessageQueueService.IsWorkingAsync` (`SessionMessageQueueService.cs:3995`) consultation. That
   is the 14:13:04 kill, 60m04s after `PoolIdleSince`.
8. **Nothing can carry a late report back.** `SettleDeferredReportsAsync` scans only
   `Dispatched`/`Working` tasks (`AgentTaskDispatcher.cs:2313-2315`); `OnTurnEndLockedAsync` returns when no
   open task exists (`AgentTaskReplyService.cs:106-110`); `RecoverFromBindRefusalAsync` applies only to
   zero-transcript sessions. Once the row is Failed the session's eventual `[antiphon-report:…]` has no
   consumer. Task f14fb9d9 below shows it happening.

### Why the caller could not see it

- The failure note and `FailureReason` say "read session … before re-running" but carry no liveness
  fact. `AgentTaskDetailDto` exposes `AgentSessionId`, `AgentId`, `Status`, `FailureReason`,
  `RecoveredAt` (`server/Application/Dtos/AgentTaskDtos.cs:33-55` summary, `:195` detail) and no session
  status or working flag.
- `GET /api/agents/{id}` does expose `Working` and `LiveSession` (`AgentDtos.cs:98-105`) and
  `GET /api/sessions/{id}` exposes `Status`/`LastSeenAt`/`EndedAt` (`BoardDtos.cs:116-126`) — which is
  the three-endpoint cross-check the orchestrator had to do by hand.
- The follow-up `dd6975f3` was created 17 s after the note. The CARD-0063 repository lease that would
  have held it behind a running 6bb32617 no longer existed, because 6bb32617 was terminal.

## Second occurrence tonight: task f14fb9d9 (session 7f60ccc5, `C:\src\slides`, opus, Shared)

Same mechanism, with two stubs, which also shows the orphaned report:

| Seq | Kind | Timestamp | Note |
|---|---|---|---|
| 22 | TurnEnd end_turn | 13:01:55 | previous turn boundary (persisted 13:03:06.39) |
| 23/24 | AssistantText+TurnEnd, IsApiError, `Connection lost mid-response` | 13:03:06 | stub 1; recovery row StubSequence 24 adopted 13:03:44, fired 13:04:56 |
| 25 | UserPrompt = TransientPrompt | 13:04:55 | resume 1 |
| 26/27 | AssistantText+TurnEnd, IsApiError, `Can't reach the API server … ENOTFOUND` | 13:08:32.9 | stub 2, persisted 13:08:33.33/.57 |
| — | `AgentTasks` Failed **13:08:33.386** | | Stub 2's *text* (seq 26) triggered `OnTurnEndAsync` 40 ms before its own TurnEnd (seq 27) was persisted; the newest TurnEnd was stub 1 (seq 24), whose row had been resolved **Superseded at 13:08:05** by rung 2 seeing seq 25. `FailureReason` quotes stub 1's text (`Connection lost mid-response`) and `Recovery ended (Superseded)`. |
| 28 | UserPrompt = TransientPrompt | 13:10:15 | resume 2 (row StubSequence 27 adopted 13:09:14, fired 13:10:15, later Superseded 13:13:44) |
| 41, 48 | AssistantText | 13:10:45, 13:11:17 | `Only notes.md is modified. Committing:` … `Entry 8 captured in C:\src\slides\…\notes.md, committed as 9e6fb64 and pushed …` |
| 49 | TurnEnd end_turn | 13:11:17 | **A real completed report, 2m44s after the task was marked Failed. Nothing consumed it.** |
| — | session EndedAt **14:08:34.66**, `KilledByRequest` | | pool retirement, 60 min after the false Failed |

## The original Codex incident (d6fa8844, 2026-09-12) is the same settlement defect with a different tail

`AgentTasks` d6fa8844: AgentKind 2 (Codex), Workspace 1 (Worktree), `ApiErrorDeferred` event at
09:51:03 (the text arrived before rung 2 here, so it *did* defer once), Failed 09:56:22 with
`(Unknown: cyber_policy) … Recovery ended (Superseded)`. `cyber_policy` is an unknown class with no
status → `ApiErrorClassification.Unknown` (`ApiErrorClassifier.cs:27-50`), same 1/3/5-minute ladder.
Because the workspace is Worktree the release path kills rather than pools: `AgentSessions.EndedAt`
09:56:22.58 (200 ms after `CompletedAt`), ExitCode 1, TerminationSource 2. Yet `LastSeenAt` is
10:21:15 — 25 minutes after the recorded end — consistent with the card's "genuinely CPU-active dotnet
test process 20+ minutes later". Whether the Codex process itself survived the kill or only an
orphaned `dotnet test` child did is **not established** here (no transcript rows after the kill: the
tailer stopped with the session).

## How often, and the shape of the window

- Every task ever failed with `killed by an API error` by resolved reason: WallModelPaused 9,
  **Superseded 3** (6bb32617, f14fb9d9, d6fa8844), NeedsHuman 1, WallParked 1. All three Superseded
  failures are this defect. The two Transient ones are both from tonight; both sessions had substantial
  activity after their Failed row (157 and 23 rows) and both were killed by pool retirement 60 min later.
- 21 tasks carry an `ApiErrorDeferred` event (the defer arm ran at least once): 7 Succeeded, 13 Canceled,
  1 Failed (d6fa8844). Tonight's two never reached the defer arm at all.
- The defer arm only wins if the resumed turn's first `AssistantText` lands **before** rung 2 resolves
  the row (rung 1 at +1 min plus up to 60 s sweep slack, rung 2 three minutes after that). After that
  point, every `AssistantText` arrival while the resumed turn is still open fails the task. A resumed
  turn that needs more than roughly four minutes and says anything is failed deterministically, not
  racily. Reconstructed, not reproduced in a test: the stub row's `AssistantText` sibling is persisted
  before its `TurnEnd` sibling (seq 154 < 155; f14fb9d9 seq 48 text at 13:11:18.188 before seq 49 end at
  13:11:18.204), so even the resumed turn's *final report line* can trip the fail path first when no
  thinking record precedes it.

## Remaining uncertainties

1. Whether the 6bb32617 session had a `dotnet run` test sweep in flight at 14:13:04 that the kill
   orphaned (last tool result 14:09:50 shows chunked test runs; last row 14:10:18). Not checked against
   process records.
2. The Codex kill-survival question above (d6fa8844).
3. Why the idle auto-compact at 13:09:14 produced no boundary (kind 23 incident). Likely the resume
   prompt typed in the same second displaced `/compact`; not investigated — separate defect if real.
4. The 13 Canceled tasks among the 21 deferred ones were not examined; they may be callers giving up
   on a deferred task, which would be the same user-facing cost by another route.

## Not done, noted

- Fix idea: treat a later `UserPrompt` (any origin) after the stub as "session moved on" in
  `HandleApiErrorTurnAsync` for every classification — return without settling, as the CARD-0090 Wall
  branch already does at `:1042-1058` — and never pool or retire an agent whose session
  `IsWorkingAsync` says is mid-turn.

--- next stage ---
next: plan
handoff: HandleApiErrorTurnAsync fails a task as "Recovery ended (Superseded)" when the ladder's own TransientPrompt resume (a later UserPrompt) resolves the row while the resumed turn is still open; the pool then kills the live session 60 min later. Plan: skip settlement on a later prompt for all classes, gate warm-pool/retire on IsWorkingAsync, and surface session liveness on the task.
artifact: docs/investigations/2026-09-14-card-0492-task-fails-on-transient-api-error-while-session-recovers.md
