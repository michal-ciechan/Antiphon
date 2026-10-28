# False model-wait failure — Codex Plan 30f56bd5

**Status:** Root cause confirmed. The Failed row is the model-wait clock in `TaskDeadlinePolicy`, applied by `AgentTaskDispatcher.TryFailOverdueAsync`, against a transcript that had stopped moving while the Codex rollout had not. No product code was changed.

**Date:** 2026-10-28. Code read on `feat/card-task-a91c27d8` at `aa6dfd24a51db7c02e8722a05a21817bf4df407a`. The desktop process that wrote the failure reports `GET /api/version` `version` / `informationalVersion` `f84228f285dd3e4ad7b5dc8260cbc9f819256f50` (capabilities `land-v2`, `operator-shutdown-v1`). That SHA is an ancestor of this checkout (`git merge-base` is `f84228f`). On that ancestor the model-wait label is still `TaskDeadlinePolicy.cs:224` and the CommandExecution skip is still `CodexTranscriptNormalizer.cs:267`. Line numbers below are the checkout.

## Verdict

Task `30f56bd5-7b67-46c1-9454-a3934b12e3ce` (CARD-1593, Plan, Worker, Codex, Frontier / gpt-6-astra, runner server2) was Failed at `2026-10-28T16:05:49.442245Z` because it was mid-turn, its last **normalized** transcript row was `AssistantText` at `15:45:43.145Z`, and that age crossed the global `Delegation:ModelWaitDeadlineMinutes` of 20. The sweep does not kill the session. Session `0cd4407b-529a-4dba-9a46-f4ae6d837b4f` stayed Running, kept its seat, and at `16:36:35.887Z` wrote `TurnEnd` / `end_turn` plus a real plan on `feat/card-task-30f56bd5` (`63036f8e5ae4e6c22aa04307c9bfd7c0955aa379`). Settlement only loads `Dispatched` or `Working`, so that report did not settle the Failed row. `result` and `nextStage` are still null.

The rollout under `CODEX_HOME` kept a `token_count` about every five minutes through `16:19:12.918Z`, including after the failure. The normalizer absorbs those rows for usage and emits no `TranscriptPart`. Empty Reasoning summaries and unmapped `CommandExecution` rows made the same window look like silence. A 300 ms poll cannot account for a 45-minute transcript gap.

## 1. The clock that wrote Failed

`FailOverdueTasksAsync` loads every `Dispatched` or `Working` task that has a dispatch time and a session (`AgentTaskDispatcher.cs:3793-3798`) and calls `TryFailOverdueAsync` (`:3853`). The policy is `TaskDeadlinePolicy.EvaluateAsync`. Two clocks exist; the tighter fraction wins (`TaskDeadlinePolicy.cs:18-24`).

| Clock | Setting | Default | What it measures |
|---|---|---|---|
| Role ceiling | `RolePolicyEntry.TimeoutMinutes`, else `DefaultTimeoutMinutes` | 240 (`DelegationSettings.cs:445`, `:1142`) | Wall clock from `DispatchedAt` |
| Model wait | `ModelWaitDeadlineMinutes` | 20 (`:470`) | While `IsWorkingAsync` is true, and the last entry is `UserPrompt`, `ToolResult`, `Thinking`, or `AssistantText` (`TaskDeadlinePolicy.cs:262-271`) |
| Local execution | `LocalExecutionDeadlineMinutes` | 90 (`:541`) | Same, last entry `ToolCall` (`:275`) |
| Boot notice | `BootModelWaitDeadlineMinutes` | 8 (`:519`) | Detection only once a boot episode is unresolved (`:63-68`, dispatcher `:3866-3920`) |

`ModelWaitDeadlineMinutes` is one global integer. It is not per role, per kind, per level, or per `expectedDurationMinutes`. The comment at `DelegationSettings.cs:453-457` records the 2026-08-20 corpus: prompt-gap p99 163 s, max 217 s; tool-result max 1 478 s. Twenty minutes is about three times that tool-result max. `<= 0` disables the clock.

Plan's role entry sets `Level = Frontier` and `RecommendedInFlight = 1` and does not set `TimeoutMinutes` (`DelegationSettings.cs:352`), so the Plan ceiling is the 240-minute default. `EscalateAfterMinutes` is unset on the role entries (`:356-360`, property `:1122`), so this sweep does not escalate (dispatcher comment `:3762-3766`).

`expectedDurationMinutes` only arms the first check: `NextCheckAt = DispatchedAt + ExpectedDurationMinutes` (`AgentTaskDispatcher.cs:6742-6752`). The Fibonacci ramp (`5, 10, 15, 25, 40, 60`) ignores it (`CheckSchedule.cs:23-25`). Checks are notes. They do not settle.

Phase elapsed is `min(last-entry age, wall clock since dispatch)` (`TaskDeadlinePolicy.cs:218-220`). `Duration` prints whole minutes (`:352-357`), so 20 minutes and 6 seconds renders as `20m`. `LoadLastEntryAsync` takes the highest sequence, and a strictly later timestamp can only replace it (`:324-342`), which can only withhold a failure. `PreviewFraction` is 0.8 (`:50`), so attention and the check digest say "closing on" at 16 minutes. The sweep fails only at `Breached` (`:98`, dispatcher `:3860`).

The phase arm runs only while `SessionMessageQueueService.IsWorkingAsync` is true (`TaskDeadlinePolicy.cs:193-197`). Working is transcript-derived: activity after the last `TurnEnd` (`TranscriptWorkingStateQuery.cs:16-19`). An open Codex turn whose last stored row is `AssistantText` is Working. The sentence is built at `TaskDeadlinePolicy.cs:221-230`:

> Mid-turn and waiting on the model for {Duration}, against a {minutes}-minute deadline. Last transcript entry: {Kind}, {age} ago.

`TryFailOverdueAsync` appends the fixed trailer at `AgentTaskDispatcher.cs:3951-3955` ("Failed, not escalated and not retried" and "The session was NOT killed"). That is the stored `failureReason`, byte for byte.

### This row

| Fact | Value |
|---|---|
| Created | `2026-10-28T15:34:25.302012Z`, explicit Codex / Frontier override (Plan/Medium row not consulted) |
| Dispatched | `15:37:23.269669Z` to agent `task-30f56bd5` (gpt-6-astra) |
| Failed | `16:05:49.442245Z` |
| `expectedDurationMinutes` / `checkCount` / `nextCheckAt` | 10 / 3 / `16:17:28.414022Z` (still stored after Failed) |
| `failureCode`, `recoveredAt`, `repliedAt`, `result`, `nextStage` | null |
| Last normalized row the clock could see | seq 5 `AssistantText` `15:45:43.145Z` |
| Age at the failure | 20m 6s, rendered `20m` |
| Wall clock since dispatch | about 28m, under the 240 ceiling |

Checks, all while status was still `Dispatched` and the session line was `Running, WORKING`:

- `#1` `15:47:31Z` — elapsed 10m/10m, `DEADLINE: none near` (first check is dispatch + 10).
- `#2` `15:52:31Z` — elapsed 15m/10m, none near (ramp +5).
- `#3` `16:02:30Z` — elapsed 25m/10m, `DEADLINE: closing on ModelWait 20m` at 16m (ramp +10, preview fraction 0.8).

The next ramp step (+15) is the stored `nextCheckAt`. `FailAsync` clears `NextCheckAt` only when `DispatchedAt` is null (`AgentTaskDispatcher.cs:7943-7944`), so a Failed row can keep the old check time.

Gates that did not save it: no unresolved boot episode (model rows already existed, so the 8-minute boot notice does not apply); the transcript pull (`:3896-3911`) can only withhold a failure, and the missing liveness never becomes a `TranscriptEntry`; bind-refusal recovery runs only when the transcript is empty (`:3928-3946`, CARD-0551). This session had rows, so the sweep logged and failed it.

## 2. What "last transcript entry" means for Codex

`CodexTranscriptTailer` polls `CODEX_HOME/sessions/YYYY/MM/DD/rollout-*.jsonl` every 300 ms (locate 250 ms) (`CodexTranscriptTailer.cs:63-64`). The file for this session is `rollout-2026-10-28T15-37-26-01a18328-b7f9-7fc0-8693-8b120915cda5.jsonl` (6 059 510 bytes at the turn end). Polling interval does not explain a 45-minute gap.

`CodexTranscriptNormalizer` emits `AssistantText` for `item_completed` / `AgentMessage` (`:247-257`). It emits `Thinking` for `Reasoning` only when `summary_text` is non-empty (`:259-264`). The class remark says TUI Reasoning items carry empty `summary_text` / `raw_content` and that the text lives in `response_item`, which v1 does not read (`:41-43`). This file has 45 `Reasoning` items; all 45 have empty `summary_text` and empty `raw_content`.

`CommandExecution`, `FileChange`, `McpToolCall`, and Extension are deliberately unmapped (`:267-272`: "Nothing downstream needs a mid-turn activity signal from Codex"). `token_count` is `AbsorbTokenCount`, which updates usage counters and returns `[]` (`:183`, `:298-319`). That usage is stamped later on the `TurnEnd` from `task_complete` (`:341-356`), not as a mid-turn row. `task_started` is skipped. This turn's end is `task_complete` at `16:36:35.887Z` → `TurnEnd` / `end_turn`. There is no `turn_aborted`. This incident is the heartbeat gap, not the CARD-0242 abort drop (that mapping is already present at `:185`).

Linux Codex transcripts therefore contain prompts, assistant text, and turn ends, and no tool-call rows (`docs/agent-kinds.md:79-82`, CARD-0779).

Normalized transcript of session `0cd4407b` (`lastSequence` 10, zero `ToolCall`, zero `Thinking`):

| Seq | Kind | Timestamp | |
|---|---|---|---|
| 1 | UserPrompt | `15:37:31.54Z` | |
| 2–4 | AssistantText | `15:37:36.68Z`, `15:38:20.199Z`, `15:40:01.363Z` | |
| 5 | AssistantText | `15:45:43.145Z` | last row the deadline saw |
| 6 | AssistantText | `16:30:35.141Z` | 44m 52s after seq 5 |
| 7 | AssistantText | `16:32:16.144Z` | |
| 8 | AssistantText | `16:34:50.261Z` | |
| 9 | AssistantText | `16:36:35.867Z` | final answer, 1493 chars |
| 10 | TurnEnd | `16:36:35.887Z` | `stopReason` `end_turn` |

After seq 5 and before seq 6 the rollout contains, among other unmapped rows:

- 5 `item_completed` / `CommandExecution` completions, last at `15:48:52.321Z`.
- 6 `Reasoning` item completions through `15:48:52.251Z`, then the next at `16:30:35.119Z` (the resume).
- `token_count` at `15:45:44.870Z`, `15:46:34.556Z`, `15:47:20.325Z`, `15:48:52.332Z`, `15:53:53.561Z`, `15:58:55.662Z`, `16:03:58.832Z`, then after the failure `16:09:01.372Z`, `16:14:07.581Z`, `16:19:12.918Z`, then `16:30:37.194Z` as work resumed.

The failure at `16:05:49` sits between `token_count` `16:03:58` and `16:09:01`. Mapping `CommandExecution` to `ToolCall` would not have saved this task: the last command at `15:48:52` would start a model-wait or tool clock that still breaches around `16:08`, before seq 6. If each `token_count` reset a 20-minute clock, the `16:19:12` heartbeat would push the deadline to about `16:39`, and the turn ended at `16:36:35`.

Claude's normalizer emits `text`, `thinking`, and `tool_use` as the JSONL blocks arrive (`TranscriptNormalizer.cs:118-140`). A `tool_use` selects the 90-minute local-execution clock. Grok emits `tool_call` immediately and coalesces `agent_message_chunk` / `agent_thought_chunk` until `turn_completed` (`GrokTranscriptNormalizer.cs:19-27`), so a pure Grok reasoning stretch also writes no row until the turn ends. The 14-day scan below did not find a Grok model-wait failure in this project.

`IsWorking` staying true is why the phase clock runs. A rule of "never fail a session the runner reports working" would remove hang detection: the 20-minute arm is already gated on that working verdict (`TaskDeadlinePolicy.cs:193-197`). The screen prefix `Working (Ns • esc to interrupt)` is turn-complete detection in `CodexDetectors`, not this policy. Provider-input wait (CARD-1518) rewrites the local-tool sentence for a current Claude dialog only (`AgentTaskDispatcher.cs:3956-3959`).

The ANSI log was not read. Process CPU was not sampled. The rollout's `token_count` and completed `CommandExecution` rows are the liveness evidence.

## 3. Why Failed, and what survived

Failed is a terminal task status for settlement. `FailAsync` sets `Status = Failed`, `FailureReason`, `CompletedAt`, and a Failed event (`AgentTaskDispatcher.cs:7907-7925`). It does not stop the session. `FailAndNotifyAsync` then calls `RemoveEphemeralAgentAsync` (`:4580-4581`). When the session is still live and this task is the sole owner, that marks the pool row Idle for the janitor (`AgentTaskService.cs:3995-4008`, `PoolDelegateRelease.MarkIdleForJanitor` at `PoolDelegateRelease.cs:49-54`) instead of deleting it. `ReleaseTaskConsumersAsync` (`:7954-7958`) releases workspace-use journal consumers. It does not release the runner seat.

`OnTurnEndAsync` loads only `Dispatched` or `Working` (`AgentTaskReplyService.cs:136-140`). The same filter gates the released-seat window (`:654`). A later marked report on a Failed task does not settle. A human retry is a separate task.

Seat, re-read after the turn had ended (`GET /api/session-runners/server2/slots`, `ageSeconds` 4184): `status` Running, `desktopStatus` Running, `occupiesCapacity` true, `orphan` false, `openTaskId` null, `pid` 23495, `exitReason` Unknown, `endedAt` null. The task's session object agrees: `working` false, `lastSeenAt` `16:36:35.223193Z`, `lastTranscriptAt` `16:36:36.127295Z`, `endedAt` null.

`openTaskId` is the latest Queued, Dispatched, Working, or Blocked task (`SeatDesktopJoin.cs:69-72`). Failed is not an owner, so the id is null. `IsOrphan` is true for a live session with no open task unless the session is a warm pool delegate (`RunnerSlotService.cs:31-32`). `orphan` false on this live, unowned seat means `PooledWarm` was true. That flag is Idle **and** `PoolIdleSince != null` (`SeatDesktopJoin.cs:101-105`). The public agent DTO has no `poolIdleSince` field (the agent `c2216707-e88d-4d94-9086-cdb2f09d7d1e` reads `status` Idle, `persistentSessionId` this session, details "Pool delegate for Worker/Plan task 30f56bd5"). The slot flag is the evidence the column was set; the timestamp was not read.

`ReleaseUnownedPoolDelegatesAsync` only selects `PoolIdleSince == null` (`AgentTaskDispatcher.cs:9066-9069`), so this row is no longer an unowned candidate. `PoolReleaseGraceSeconds` (default 120, `:1063`) is that sweep's grace, not this row's clock. The warm janitor retires an Idle row after `PoolIdleRetireMinutes` (default 60, `:1049`) unless the session is still mid-turn and its last transcript is newer than the cutoff, in which case it resets `PoolIdleSince` to now (`:9218`, `:9299-9313`). The turn is over (`working` false), so that deferral does not apply on a later tick. Automatic terminal-seat release is off (`TerminalRunnerSeatReleaseService.cs:17`, `AutomaticEnabled` default false). `PoolEnabled`'s comment says a worktree delegate is not reused for the next task (`DelegationSettings.cs:1036-1037`). The failure path still left this row Idle for the janitor because the process was alive.

The worktree and branch survived. Desktop path `C:\Antiphon\worktrees\card-task-30f56bd5`, branch `feat/card-task-30f56bd5`. `origin` is `63036f8e5ae4e6c22aa04307c9bfd7c0955aa379`. The runner mirror `/work/worktrees/task-30f56bd5` is that commit. `FailAsync` does not delete the worktree or merge the branch.

The orchestrator already retried as `62fb542c-8dec-4e3b-a494-338b955f156d`, title "CARD-1593 S3 atomic consume plan (retry)", ClaudeCode, High, Plan, created `16:06:31.219012Z` (42 s after the failure), dispatched `16:07:20.897066Z`, Succeeded `16:16:43.915381Z`. That success landed while this Codex session was still between `token_count` heartbeats. The original session then finished its own plan anyway.

## 4. The same sentence in the last 14 days

Scoped Failed list for project Antiphon (`d4ea7ae9-e769-474b-95b9-aa25fbc1303f`), detail GET of each row, no task token: 94 Failed tasks. Kinds: ClaudeCode 15, Codex 72, Grok 7. Failure text containing "waiting on the model": 7, every one Codex, every one the identical sentence "Mid-turn and waiting on the model for 20m… Last transcript entry: AssistantText, 20m ago." Every one has `lastTranscriptAt` after `completedAt`.

| Task | Role / level | Failed | Session | Session now | `lastTranscriptAt` |
|---|---|---|---|---|---|
| `05cc7d09` CARD-1539 direct code and land | Code / High | `2026-10-23T21:35:11Z` | `1f7a5cec` | Stopped | `22:22:05Z` |
| `6c903205` CARD-1540 direct code and land | Code / High | `21:35:07Z` | `83068840` | Stopped | `22:22:05Z` |
| `fb7c4a41` CARD-1572 Unit lane census continuation | Debug / High | `21:35:21Z` | `970b008e` | Stopped | `22:29:43Z` |
| `2dbadf60` CARD-1545 direct code and land | Code / High | `21:35:50Z` | `12d1481e` | Stopped | `22:22:05Z` |
| `87928ecc` Unmatched projects in qualification policy | Debug / High | `21:35:52Z` | `bfd2e4d3` | Stopped | `22:22:05Z` |
| `4963339c` CARD-0931 method-OR filter wildcard | Code / High | `21:36:02Z` | `4a4c14be` | Stopped | `22:22:05Z` |
| `30f56bd5` CARD-1593 S3 atomic consume plan | Plan / Frontier | `2026-10-28T16:05:49Z` | `0cd4407b` | Running | `16:36:36Z` |

The six October 23 rows are distinct sessions and failed inside one minute. One sample transcript (`05cc7d09` / `1f7a5cec`, 42 rows) now ends `TurnEnd` `21:26:35.234Z`, which is **before** the `21:35:11` failure, while `lastTranscriptAt` is `22:22:05`. Seq 33 `AssistantText` `21:14:57.218Z` is about 20m 14s before that failure, which is the age the sentence describes. Rows 34–42 carry event times that also precede the failure. Those six are the same verdict plus sessions that gained transcript after Failed. Their rollouts were not read, so they are not established as copies of this `token_count` stretch.

Two other deadline failures are the local-tool sentence, outside this question: `b985ca59` ClaudeCode Review "waiting on a local tool for 1h30m", and `be6a673b` Grok Deploy the same. The other 85 Failed rows are other reasons.

## 5. Smallest fix that still detects a hang

Keep `Delegation:ModelWaitDeadlineMinutes` at 20. Do not raise it for Codex or for Frontier Plan. The 20 minutes is the Claude/Grok streaming measurement from 2026-08-20 (`DelegationSettings.cs:453-457`). This AssistantText gap was 44m 52s, so a Codex-only number would have to clear that (60 is only about 1.3×) and the next longer reasoning turn would still false-fail. The role ceiling stays 240 either way.

Do not key the failure off `IsWorking` (section 2). Do not treat `token_count` as a new `TranscriptKind`. `ClassifyPhase` sends an unrecognised kind to a zero phase limit (`TaskDeadlinePolicy.cs:277`), so a heartbeat kind as the last row would skip the 20-minute arm and leave only the 240-minute ceiling after the heartbeats stopped. Mapping `CommandExecution` to `ToolCall` is a separate improvement and would not have saved this task (section 2). Replacing Failed with another Check would avoid the terminal row, because settlement ignores Failed, and would still misread silence; the ceiling could fail the task later.

The model-wait elapsed should be measured from the later of the last normalized transcript timestamp and the last Codex rollout `token_count`. The tailer already sees those rows. `AbsorbTokenCount` discards the time. Keep the last entry's Kind (here `AssistantText` / ModelWait) and only move the activity timestamp the phase clock ages. A model that stops writing both transcript rows and `token_count` still fails at 20 minutes. The session stays unkilled, as today.

Files: `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` (surface the `token_count` time without a report part), `src/Antiphon.SessionRunner/CodexTranscriptTailer.cs` (persist that heartbeat), `server/Application/Services/TaskDeadlinePolicy.cs` (`LoadLastEntryAsync` / `EvaluateAsync` take the later instant). `AgentTaskDispatcher.TryFailOverdueAsync` only if the gate reads the rollout live instead of a stored heartbeat. `DelegationSettings` only if a per-kind override is chosen instead of the heartbeat.

Red witness, in `TaskDeadlinePolicy` or the overdue-dispatcher tests: last `TranscriptEntry` is `AssistantText` 21 minutes old, a Codex `token_count` heartbeat is 5 minutes old, verdict is not breached, status stays `Dispatched`. Control: no heartbeat and no transcript row for 21 minutes → ModelWait breached, the same failure sentence, session not killed. `AgentTaskOverdueDeadlineTests` already asserts `FailureReason` contains "waiting on the model" and "NOT killed" for the current behavior (`tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs:154`, `:326-327`).

For a Frontier Plan on Codex the deadline stays 20 minutes from the later of those two signals, under the 240-minute Plan ceiling.

## What the session did after 16:30:35Z

Read only. The session was not killed, cancelled, or messaged.

- Seq 6 `16:30:35.141Z`: the design has four slices (supervisor start, retained return, queue attempt, release).
- Seq 7 `16:32:16.144Z`: the draft is written, including failure handling and checkpoint rows.
- Seq 8 `16:34:50.261Z`: the plan is committed and pushed; the checkpoint table is 15 rows and 252 expected results.
- Seq 9 `16:36:35.867Z`: final answer. Plan path `docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md` in the task worktree. Progress marker `commit=63036f8e5ae4e6c22aa04307c9bfd7c0955aa379`. Closing block `next: review` and `[antiphon-report:30f56bd5 done]`.
- Seq 10 `16:36:35.887Z`: `TurnEnd`, `end_turn`.

`origin/feat/card-task-30f56bd5` is that commit. The task row is still Failed, `result` null, `nextStage` null. The Claude retry `62fb542c` had already Succeeded at `16:16:43Z`.

## Uncertainties

- `PoolIdleSince`'s timestamp was not read. The slot's `orphan=false` with a null `openTaskId` is evidence the column was non-null at the read. Whether the janitor retires the seat one hour after that stamp was not waited out.
- The six 2026-10-23 sessions were not rollout-read. Same verdict text and later transcript, not a proven `token_count` stretch.
- CPU, the pty screen, and the ANSI log were not measured.
- The failure sentence names a 20-minute deadline, which is `ModelWaitDeadlineMinutes` at sweep time. Appsettings was not opened; an explicit 20 would read the same as the default.
- The desktop process is `f84228f`, an ancestor of `aa6dfd24`. The grepped deadline label and CommandExecution skip match. The rest of the binary was not diffed line by line.
- The effective live runner build field was not a separate property on `GET /api/version` (keys: `version`, `informationalVersion`, `capabilities`).

## Not done, noted

The `token_count` heartbeat was not implemented.
