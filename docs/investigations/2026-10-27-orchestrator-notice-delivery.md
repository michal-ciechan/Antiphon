# Orchestrator completion and check notices stay queued and untyped

Date: 2026-10-27. Task 4fb187d4 (Investigate). Measured read-only at 2026-10-27T14:45:34Z against `https://antiphon.desktop.codeperf.net`. Live server `bedea1c03cd788d83b2ff54c40bfb603927bd69c` (`land-v2`, `operator-shutdown-v1`). Cited files are identical between that SHA and this checkout `718bd29f4a1504c64c491d607d31403fcb87dae2` (`git diff` of the cited paths is empty). No queue row was updated and nothing was sent to the session.

## Verdict

Confirmed. Task, check, and land notices for standing session `b2f1a869-9bc5-4974-b8ae-53686448fd20` are durable `SessionQueuedMessages` on that same session id, almost all `MessageSendMode.WhenIdle`, and they are not being typed. At measurement the session was idle (`working=false`, last transcript row is `TurnEnd` sequence 3315 at 2026-10-27T14:32:02.999Z, `stop=end_turn`) and `GET /api/sessions/b2f1a869-9bc5-4974-b8ae-53686448fd20/messages` returned `modalBlocked=true`, `modalBlockedReason="Claude dialog observation unavailable"`, `lastDelivery=null`, and 340 `Pending` rows. 338 of those have `deliveryAttempts=0`. A land note stays `AwaitingReceipt` with `lastErrorCode=null` until a delivery attempt records a floor the receipt scan can use. Zero attempts means no receipt is possible.

The CARD-0680 dialog guard is the gate that stops the typing. The exact observation field that makes the guard say "unavailable" is not on any public route.

## What each candidate shows

| Candidate | Finding |
|---|---|
| Never idle, because of tool calls and Monitors | The idle detector reads this session idle, and it has ended turns all day. 491 `TurnEnd` rows exist. The hold survives idleness. Tool chaining can delay a `WhenIdle` note until the next turn end; it does not explain a backlog that stays at zero attempts across those turn ends. |
| CARD-0680 input guard withholds terminal input | This is the live gate. `ModalBlockReasonAsync` maps every dialog refusal other than a recognized blocking dialog to "Claude dialog observation unavailable". `DeliverNextLockedAsync` and `DeliverAsync` return before an attempt is charged. |
| Destination bound to a stale session id or generation after the 2026-10-26 15:21 and 2026-10-27 ~03:00 AppHost restarts | The destination and the queue are the live session. `AgentSession.StartedAt` (the generation) is `2026-10-26T06:56:36.243078Z` and did not move at those later restarts. The same id still owns the agent (`persistentSessionId` and `liveSession.id`). A runner-side `AcceptedStartedAt` mismatch would also produce this refusal and is not exposed by HTTP. |
| Claude Code `ReadNotifications` is not fed by Antiphon | No `ReadNotifications` symbol exists in this repo. Antiphon delivers by pasting into the terminal. Claude's own composer queue (`QueueEnqueue` / `QueueDequeue` / `<task-notification>` `QueuedUserPrompt`) and Monitor events are a separate channel and still arrive. |

## 1. How a notice is routed

Destination is `AgentTask.ParentSessionId` / `AgentTaskLandNotification.ParentSessionId`, stored on `SessionQueuedMessage.AgentSessionId`. Resume of the same row advances `AgentSession.StartedAt` via `SessionGeneration.Next` (`AgentControlService.cs:656`) and relaunches that id (`:688-690`). Pending notices stay on the id. They are not rewritten onto a new generation.

`MessageSendMode` (`SessionQueueDtos.cs:4-10`): `Now=0` types immediately and creates no queue row; `WhenIdle=1` waits for an idle turn. These producers all enqueue `WhenIdle` with `deliverIfIdle:false`, so the row is persisted and delivery is left to the turn-end flush or the stranded-queue sweep (`SessionMessageQueueService.cs:445-463`, `StrandedAgeSeconds` default 60 at `SupervisionSettings.cs:344`):

- Task done / failed / blocked. `AgentTaskReplyService.DeliverToParentAsync` (`:2946-2954`) calls `EnqueueParentNoteAsync` (`:3015-3037`): origin `Delegation`, conversation key `task:{rootTaskId:N}`, optional distill hold. The visible header comes from `DelegationReportFormatter.BuildCompletionNote` (`:2987`). Profiled settlements also insert an `AgentTaskLandNotification` of kind `TaskCompletion` (`AddCompletionObligationAsync`, `:3078-3102`), which the reconciler enqueues the same way.
- Checks. `AgentTaskCheckService` enqueues origin `Check`, key `check:{taskId:N}` (`:237-240`). The body opens with `HeaderPrefix` `"[check "` (`:586`, `:738`). A legacy publisher can instead insert kind `LegacyCheckNote`; the reconciler then uses the same check key (`AgentTaskLandNotificationService.cs:143-152`).
- Land Held / Aged / Conflict / Outcome. `LandNotificationPayload.Create` (`:48-57`) sets `ParentSessionId` from the request and initial state `Queued` (or `NotRequired` / `DestinationUnavailable` when there is no session reply). `AgentTaskLandNotificationService.ReconcileAsync` (`:44`) links or creates the queue row (`WhenIdle`, `deliverIfIdle:false`, `sourceLandNotificationId`, key `land:{noteId:N}`) and sets `State=AwaitingReceipt` (`:149-160`). `AgentTaskLandNotificationHostedService` rescans non-terminal notes every 5 seconds (`:92-121`).

`scripts/delegate.ps1:737` prints `kind`, `state`, `destinationSessionId`, `queueMessageId`, `confirmedAt`, and `lastErrorCode`. `Held` and `Aged` are kinds (`LandingEnums.cs:21`), not states. States are `Queued`, `RetryPending`, `AwaitingReceipt`, `Confirmed`, `NotRequired`, `DestinationUnavailable`, `Canceled`, `LegacyUnverified` (`:22`).

Aging. A pending land request with no progress for `LandWarningSeconds` (default 300) or `LandErrorSeconds` (default 900) inserts another note of kind `Aged` (`AgentTaskLandMonitorService.cs:40-43`, `AddAgedAsync` `:90`). An Outcome that stays unconfirmed that long does the same (`:66-69`, `AddReceiptAged` `:79`). The Aged note is itself a `WhenIdle` queue row to the same parent, so it cannot reach a parent whose queue is not being typed. `enqueueAttempts` on the land row counts reconcile inserts, not terminal typing.

Delivery worker. On turn end, `OnTurnEndCoreAsync` (`SessionMessageQueueService.cs:1630`) calls `DeliverNextLockedAsync` (`:2276`) only when `CanWriteResumeInputAsync` is true and the session is `Running`. `FlushStrandedQueuesAsync` (`:1694`) is the idle redelivery path for always-on sessions. `DeliverNextLockedAsync:2339` returns `TemporaryBlock` when `IsModalBlockedLockedAsync` is true, before a row is selected and before `DeliveryAttempts` increments. `DeliverAsync:3875` returns `DeliveryVerdict.ModalBlocked` at the same gate, so a later send-now of an existing row hits it too.

Receipt. `LandNoteReceipt.Prompts` (`:30-51`) returns null when the queue row has neither `LastDeliveryBaselineSequence` nor `LastDeliveryStartedAt`. The reconciler runs the scan only when `DeliveryAttempts > 0` (`AgentTaskLandNotificationService.cs:254`). A hit sets `ConfirmedAt`, `ConfirmingPromptSequence`, and `State=Confirmed` (`:346-358`). Non-legacy Held, Aged, Conflict, and Outcome accept `UserPrompt` or `QueuedUserPrompt` above the floor with the complete expected text (`LandNoteReceipt.cs:21-23`, `:75-77`). Task completion and checks accept only `UserPrompt`. Task completion also refuses when the session generation no longer equals `LastDeliveryGeneration` (`completion_delivery_generation_changed`, `:266-272`). With zero attempts the note stays `AwaitingReceipt`, `lastErrorCode` stays null, and the status line shows an empty receipt and an empty error. That is the line in the brief.

Retry. `MaxDeliveryAttempts` defaults to 3 (`SupervisionSettings.cs:434`). A `Pending` row at that cap is `Parked` (`SessionQueueDtos.cs:35`, `BuildQueueDtoAsync` `:5699`). Parked rows are excluded from automatic retry. A modal block returns before the attempt counter moves, so the backlog does not park itself by failing. `GET /api/sessions/{id}/messages` (`SessionEndpoints.cs:87-92`, `BuildQueueDtoAsync` `:5684-5715`) returns only `Pending` rows, plus `working` and the modal flag. `Sent` rows are absent there.

Public list shape. There is no list-by-destination for land notes. `GET /api/agent-tasks/{id}` carries `landRequest.notifications[]`, and `destinationSessionId` is `ParentSessionId`. `GET /api/sessions/{id}` does not exist; session identity is on `GET /api/agents` (`liveSession.id`, `liveSession.startedAt`, `transcriptBinding`, `working`).

## 2. Why `b2f1a869` gets no receipt

The guard. For Claude Code, `ModalBlockReasonAsync` (`:5728-5745`) returns "Remote Control menu blocks input" when an open `RemoteControlModalEpisode` matches the generation, "Claude dialog blocks input" when `ProviderDialogObservationService.Refusal` returns `ModalBlocked`, and "Claude dialog observation unavailable" for every other refusal. `Refusal` (`ProviderDialogObservationService.cs:23-33`) returns `ObservationUnknown` when the snapshot is null, the session id mismatches, `RuntimeEpoch` is empty, `InputHistoryKnown` is false, `AcceptedStartedAt` differs from `AgentSession.StartedAt`, or the state is anything other than `Clear`. Only `ProviderDialogState.Clear` allows automated input. A null snapshot includes the 2-second read timeout (`ReadBoundedAsync`, `:13-20`).

The runner builds that snapshot in `SessionRunnerRuntime.ObserveDialog` (`:2513-2536`). `InputHistoryKnown` is true only when the session is not adopted, `_terminalInputUncertain` is false, and the backend is `PtyHost` (`:2521`). `AdoptCoreAsync` sets `_adopted=true` (`:3338`) and leaves it set, so a session reattached after a runner restart stays unknown for the life of that process. `WriteAsync` sets `_terminalInputUncertain=true` on a write exception (`:4031-4038`) and never clears it in-process. The screen classifier (`ClaudeDialogObservation.Observe`, `ClaudeDialogObservation.cs:11-39`) returns `Unknown` for a blank screen, a screen over 65536 characters or 512 rows, or any screen that is neither a readable composer, a working spinner, nor a recognized blocking prompt with a footer. Herdr snapshots force `ProviderDialogState.Unknown` (`SessionRunnerRuntime.cs:3981`); this session is `PtyHost`, so that branch is not the one serving it. `AcceptedStartedAt` is on the runner snapshot and is not on `GET /api/agents` or the queue DTO.

`SendInteractiveInputAsync` (`AgentSessionRuntime.cs:2170-2190`) does not call the guard. `SendInputAsync` for Claude Code does (`:2156-2159`). Human sentences in the transcript after the stall are PTY conversation, not deliveries of this queue.

Standing-resume fence, separate and not sufficient here. A Claude standing resume enqueues a `WhenIdle` system note with `runtimeActionKey = StandingResumeNote.Key(launch.StartedAt)` and `deliverIfIdle:false` (`AgentSessionService.DeliverLaunchNoteAsync`, `:3444-3457`; body `ChannelPreamble.RestartResumeBody`, `:96-99`). `CanWriteResumeInputAsync` (`SessionMessageQueueService.ResumeReceipt.cs:58-73`) refuses every automatic write while that current-generation note exists, has not been confirmed, and `ObserveReceiptReadinessAsync` is not `Ready`. Readiness is `Unknown` on ownership or generation mismatch, an incomplete snapshot, or the 5-second budget (`AgentSessionRuntime.ReceiptReadiness.cs:24-61`). Old-generation resume notes are filtered out of `DeliverNext` (`SessionMessageQueueService.cs:2382-2383`) and are never typed. The live queue still holds the current-generation resume note at zero attempts, and four `[task … failed]` prompts were submitted after that note was created. The fence allows other writes once readiness is `Ready`, and delivery order is sequence order, so those four are consistent with a short window when the observation was acceptable. The fence does not account for the hours of zero-attempt backlog after that window.

Idle detector. `TranscriptWorkingStateQuery` (`:16-28`) treats the session as working when transcript activity exists after the latest `TurnEnd`, `SessionRestartBoundary`, manual `CompactBoundary`, or interrupted `UserPrompt`. `QueuedUserPrompt`, `QueueEnqueue`, `QueueDequeue`, and `QueueRemove` are not activity. An empty transcript is idle. Claude's own `<task-notification>` rows therefore do not keep this session "working".

## 3. Server data at 2026-10-27T14:45:34Z

Agent `Antiphon-Orchestrator` `a392cbc4-0fc0-4603-b4d0-5198d1929718`, slug `antiphon-orchestrator`, kind `ClaudeCode`, `alwaysOn=true`, status `Running`, `sessionBackend=PtyHost`, `remoteControlEnabled=true`. `persistentSessionId` and `liveSession.id` are `b2f1a869-9bc5-4974-b8ae-53686448fd20`. Session `createdAt` `2026-10-24T22:21:44.614114Z`, generation `startedAt` `2026-10-26T06:56:36.243078Z`, `lastSeenAt` `2026-10-27T14:32:02.895471Z`, `endedAt` null, `transcriptBinding=bound`, `failureReason` null. `GET /api/agents` reports `working=false`.

Queue (`GET /api/sessions/b2f1a869-9bc5-4974-b8ae-53686448fd20/messages`):

| | |
|---|---|
| working / modal / reason / lastDelivery | false / true / Claude dialog observation unavailable / null |
| Pending | 340. Oldest `2026-10-25T05:13:31.966368Z`. Newest `2026-10-27T14:42:52.493866Z` (this task's own `[check 4fb187d4 #1]`, attempts 0) |
| By day | 2026-10-25: 58. 2026-10-26: 167. 2026-10-27: 115 |
| By origin | Check 3 (attempts 0). Delegation 324 (attempts 0) plus 2 parked (attempts 3). Scheduled 1 (attempts 0). System 10 (attempts 0) |
| By first line | `[task` 137, `[land` 189, `[check` 3, other 11. Delegation count equals task plus land |

Checks still pending, attempts 0: `[check 64f85365 #1]` created `2026-10-26T19:23:05.977213Z` (queue `f737c9a3-96e7-45b4-9995-da340fc584b0`); `[check 2c094db2 #1]` `2026-10-27T13:02:30.961499Z` (`b5a3004f-7d77-41a1-8da5-f6259934e108`); `[check 4fb187d4 #1]` `2026-10-27T14:42:52.493866Z` (`db75ef27-c80e-4f9a-9db7-d5f000f1bb00`).

Parked, and therefore excluded from automatic retry even if the guard later clears: queue `a16768f9-233a-457e-924c-c339c55a7056`, `[task 6f30d2a2 done]`, created `2026-10-25T05:13:31.966368Z`; queue `195b52d6-f382-43d0-93f5-70256620f299`, `[task 08c8e223 done]`, created `2026-10-25T05:15:27.584511Z`.

System rows at attempts 0 include resume notes created `2026-10-26T02:18:07` (two), `02:20:22`, `06:40:53`, and `06:56:56` (queue `f6859e07-9839-4793-8f52-21a73dd5196c`, body starts with the restart-resume ritual, created 20 seconds after the current generation). Also a compaction note `2026-10-26T09:51:49`, runner availability notes on 2026-10-27, and an orchestrator-instructions note `2026-10-27T12:08:16`. The public DTO does not include `RuntimeActionKey`, so the old resume notes cannot be proved filtered, only observed untyped.

Land `12e8a22a-d65f-4f53-b4e9-e30c16921501` ("rc top-up 7"), role Merge, status Succeeded, completed `2026-10-27T03:19:21.606263Z`, publication Landed. Request `d2990749-5031-43a4-b594-8b2b399fd563` Completed, requested `2026-10-27T03:39:47.349019Z`, `ageSeconds` about 39949. Four notes, all destination `b2f1a869`, state `AwaitingReceipt`, `enqueueAttempts=1`, `lastErrorCode` null, `confirmedAt` null:

| Kind | Note | Queue | Created |
|---|---|---|---|
| Aged | `aff0d501-71b0-413b-a515-aa94e388df29` | `61156bc2-5dc0-4886-8a39-7eb06710b903` | 2026-10-27T03:45:09Z |
| Outcome | `87cb8597-bfd0-4dfe-8e00-d224b37eaef0` | `acd0114c-233b-434d-8131-18ac8ee786f7` | 2026-10-27T03:52:29Z |
| Aged | `544316e4-1337-432c-a20f-314ed043afc1` | `5e6ff526-4620-4e9f-8b34-bd4e9f423852` | 2026-10-27T03:57:31Z |
| Aged | `1d738621-d9be-48bb-ae5d-fea4c7681874` | `4096f409-a5ba-49e7-bdce-d3e0bd9c65be` | 2026-10-27T04:07:31Z |

All four queue ids are still `Pending`, origin Delegation, attempts 0, parked false.

Land `3b5896b8-cd27-41a3-a9e9-b496d534083f` ("rc top-up 6"), Succeeded, completed `2026-10-26T23:49:22.460171Z`. Request `ceccd165-17dd-4000-b72a-aaec5968f40a` Completed, requested `2026-10-26T23:53:42.827851Z`, `ageSeconds` about 53514. Counts: Held 52, Aged 5, Outcome 1. Every note is destination `b2f1a869`, `AwaitingReceipt`, `enqueueAttempts=1`, error null, confirmed null. First Held `3eef17ce-20d9-451e-a775-f88292849d5a` / queue `8c808810-7d5b-4392-90a9-db7be926182d` at `2026-10-26T23:53:43.211318Z`. Outcome `eb37a9ed-0ef6-493b-9170-5743771c7cdf` / queue `2b2d02c0-8bd5-466c-abbc-8655cea26089` at `2026-10-27T03:00:17.015865Z`.

Transcript `GET /api/sessions/b2f1a869-…/transcript?since=0`: `lastSequence` 3315, 3315 entries. Counts: AssistantText 439, CompactBoundary 1, QueueDequeue 186, QueueEnqueue 246, QueueRemove 60, QueuedUserPrompt 60, SessionRestartBoundary 1, Thinking 3, ToolCall 698, ToolResult 698, TurnEnd 491, UserPrompt 432. `SessionRestartBoundary` sequence 1811 at `2026-10-26T02:18:07.398176Z`. Manual `CompactBoundary` sequence 2090 at `2026-10-26T09:51:48.745Z`. A second fetch `since=3315` at 14:45:34Z returned no newer entries. `QueueEnqueue` / `QueueDequeue` are Claude composer-queue operations (`TranscriptNormalizer.cs:272-277`), not Antiphon queue deliveries. 48 `QueuedUserPrompt` rows are Claude `<task-notification>` bodies. On 2026-10-27 the transcript has 42 enqueues and 34 dequeues of that composer queue, and the newest ordinary `UserPrompt` is sequence 3294 at `2026-10-27T14:30:03.552Z` ("How is desktop running?"). Turn ends continued through sequence 3315.

Antiphon notice prompts after the current generation: exactly four `UserPrompt` rows, all `[task … failed]`, then none.

| Sequence | At | Header |
|---|---|---|
| 1818 | 2026-10-26T06:57:03Z | `[task 68c7c0f9 failed]` |
| 1831 | 2026-10-26T06:58:52Z | `[task e7421374 failed]` |
| 1838 | 2026-10-26T07:01:19Z | `[task 72c7cbd5 failed]` |
| 1934 | 2026-10-26T07:08:04Z | `[task c0eea360 failed]` |

No later `UserPrompt` or `QueuedUserPrompt` starts with `[task `, `[check `, or `[land `. The queue kept accepting notes (115 created on 2026-10-27, including one at 14:42:52Z) and none of those were typed.

Same observation on other live Claude Code `PtyHost` sessions, all `working=false`, same minute:

| Session | Remote control | Started (generation) | Modal | Pending | Max attempts |
|---|---|---|---|---|---|
| antiphon-check-interpreter `cea73d57` | false | 2026-10-26T02:17:11Z | unavailable | 0 | 0 |
| antiphon-diagnose `df3b5125` | false | 2026-10-26T02:17:10Z | unavailable | 8 | 0 |
| Antiphon-Orchestrator `b2f1a869` | true | 2026-10-26T06:56:36Z | unavailable | 340 | 3 |
| antiphon-output-distiller `067f9d0e` | false | 2026-10-26T18:59:06Z | clear | 0 | 0 |
| AZ Care `b8aad212` | true | 2026-10-26T06:40:02Z | unavailable | 0 | 0 |
| ClaudeBot-Antiphon `e1626153` | true | 2026-10-26T12:24:27Z | clear | 0 | 0 |
| Family `fb38e751` | true | 2026-10-26T06:40:58Z | unavailable | 1 | 0 |
| Gym Stat Orchestrator `4f71db42` | true | 2026-10-26T02:16:57Z | unavailable | 1 | 0 |
| markdown-package Orchestrator `384d59ee` | true | 2026-10-26T07:13:41Z | clear | 4 | 3 |
| school-revision `d1880154` | true | 2026-10-26T06:55:09Z | unavailable | 4 | 0 |
| slides Orchestrator `b788e4ec` | true | 2026-10-26T06:57:27Z | unavailable | 2 | 0 |
| Torquay Leander `8c1d3da6` | true | 2026-10-26T07:01:36Z | unavailable | 0 | 0 |

The refusal is per session. It is not a fleet-wide empty runtime epoch, and remote control alone does not cause it. markdown-package's four pending rows reached the attempt cap, which is what delivery looks like when the observation is acceptable. diagnose (8) and school-revision (4) are the same zero-attempt hold on other sessions.

Logs. `scripts/logs.ps1 -Source desktop-server` throws `No log files found at /work/repos/antiphon/server/logs/antiphon-*.log` (`scripts/logs.ps1:42`). This runner mirror does not have the desktop Serilog files (`docs/logs.md` places them at `C:\src\Antiphon\server\logs`). Server2 runner logs under `/state/runner-logs` for 2026-10-26 and 2026-10-27 contain no `b2f1a869` lines. The orchestrator is a desktop `PtyHost` session, so those files are the wrong host. Hangfire is in-memory and does not retain delivery attempts across the AppHost restarts. The public buffer route is the ANSI log (`AgentSessionRuntime.GetBufferSnapshotAsync`), not the rendered screen `ClaudeDialogObservation.Observe` reads.

## Uncertainties

Which `Refusal` input is failing is unresolved: null snapshot (including the 2-second timeout), empty `RuntimeEpoch`, `InputHistoryKnown=false` because `_adopted` or sticky `_terminalInputUncertain`, `AcceptedStartedAt` different from `2026-10-26T06:56:36.243078Z`, or a screen the classifier calls `Unknown`. The collapsed reason string does not distinguish them. Desktop server and pty-host logs, plus the runner dialog snapshot (`Adopted`, `InputHistoryKnown`, `AcceptedStartedAt`, rendered screen), would isolate it. The delivery stop after `2026-10-26T07:08:04Z`, with four successful typings in the twelve minutes after the generation change, fits a sticky uncertain-input flag or an adoption after a later runner restart better than a generation change of the server row, because the server generation did not move again.

## Not done, noted

Not done, noted: a fix was not implemented — stop treating `ProviderDialogObservationService.Refusal`'s `ObservationUnknown` (`ProviderDialogObservationService.cs:23-33`, collapsed at `SessionMessageQueueService.cs:5740-5744`) as a permanent pre-attempt withhold of `WhenIdle` notes on an idle, transcript-bound Claude session (`DeliverNextLockedAsync:2339`, `DeliverAsync:3875`), and put the distinct cause on `SessionQueueDto`; witness is this session at 2026-10-27T14:45:34Z, idle, reason "Claude dialog observation unavailable", 338 of 340 pending rows at `deliveryAttempts=0`, land notes `AwaitingReceipt` with null `lastErrorCode` because `LandNoteReceipt.Prompts` (`:50-51`) has no delivery floor.

The rows are still readable at `GET /api/sessions/b2f1a869-9bc5-4974-b8ae-53686448fd20/messages`. The two parked rows will not auto-retry. Send-now uses `DeliverAsync` and stops at the same guard. The orchestrator can keep learning task outcomes from `GET /api/agent-tasks`, which is the path it is already using.
