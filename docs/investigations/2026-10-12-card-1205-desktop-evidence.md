# CARD-1205: desktop ingestion evidence

Desktop evidence confirms that the runner captured the missing 11:19 turn tail before the 12:14 compact event, and that a 30-second delivery-verification failure caused the recovery pull at 17:07:38. The server's catch-up-before-SSE-subscription gap is the strongly supported upstream attribution. The retained logs do not timestamp the original tailer's publication or the SSE subscription, so incident-specific loss in that gap remains an inference. Next: investigate to close that last attribution gap.

## Provenance and limits

Task d5091e82; source/base `ce470787aa054ee2d5c0fbdfd7a63a49645d4a96`; branch `feat/card-task-d5091e82`. Times are UTC on 2026-10-12 unless a full date is shown. Desktop Serilog lines explicitly have `+01:00`: subtract one hour. Primary server log is `C:\src\Antiphon\server\logs\antiphon-20261012.log` (line numbers below). Runner Serilog is `C:\Users\lndco\AppData\Local\Temp\antiphon-logs\session-runner-20261012.log`. Supervisor log is `C:\src\Antiphon\logs\session-runner.log`.

Read the log and HTTP owners first. Queries used the documented desktop `docker exec antiphon-postgres psql -X -U antiphon -d antiphon` entry point, with `BEGIN READ ONLY` and `ROLLBACK`. HTTP reads used the runner's documented `/capabilities` and `/sessions/{id}/transcript` routes. No process restart, DB write, provider input, source repair, or settings change was performed. Generated metadata receipts are gitignored under `.antiphon/card-1205-desktop/`; essential evidence is retained here. No credentials/provider auth files were read or emitted.

The pushed source-side investigation was read from `origin/feat/card-task-ef0bb4ed:docs/investigations/2026-10-12-card-1205-task-completion-notes-not-delivered-to-the-orchestrator-after-an-apphost-restart-five-tasks-silent-for-about-an-hour.md`. Its Working-state reconstruction is corroborated by the fresh DB rows here; this report adds desktop evidence and distinguishes facts from attribution.

## Restart and coverage window

| Evidence | UTC | Meaning |
|---|---|---|
| Watchdog log lines 64843, 64845 | restart lock stamped 11:16:47.9044471 | Restart owned by PID 27908; watchdog skipped while it was in flight. |
| Server log 36790 | 11:16:50.815 | Old server announced shutdown. |
| Current server process metadata | 11:18:40.8075211 | PID 37924 started, executable `C:\src\Antiphon\server\bin\Debug\net9.0\Antiphon.Server.exe`. |
| Server log 36837 | 11:19:06.811 | New server began listening on its Aspire-assigned port 57809. |
| Server log 36844 | 11:19:06.828 | Application started. |
| DB catch-up batch, sequences 16933–16936 | 11:19:16.499347 | Caller snapshot persisted through its ToolCall at 11:19:11.614. |
| Missing ToolResult producer timestamp | 11:19:19.914 | After the caller snapshot batch. |
| Missing AssistantText and TurnEnd producer timestamp | 11:19:24.074 | After the caller snapshot batch, before fleet catch-up completed. |
| Server log 37088 | 11:19:49.825 | `Transcript catch-up completed for 73 runner session(s)`. |

`SessionRunnerEventPump.cs:44` awaits the entire catch-up before consuming `StreamEventsAsync` at line 46; the per-session sync is awaited at line 123. HTTP stream subscription begins only when the async iterator sends its request (`SessionRunnerHttpClient.cs:878`), and the runner `/events` handler subscribes at `src/Antiphon.SessionRunner/Program.cs:401`. Thus this pump's SSE subscription was **no earlier than 11:19:49.825**, at least 33.326 seconds after the caller's stored catch-up batch. The missing tail's timestamps fall inside that interval. There is no separate retained subscription timestamp.

`SessionRunnerEventHub.Subscribe` adds only a fresh reader (`SessionRunnerRuntime.cs:4283`, `:4304`). `Publish` sends only to its current subscriber snapshot (`:4319`); it does not replay previously published events. Catch-up runs on connection iterations, not on a timer while the stream is healthy. Keepalives every 15 seconds (`Program.cs:398`, `:408`) reset the HTTP idle watchdog (`SessionRunnerHttpClient.cs:868`, `:890`, `:894`), so a healthy subscription need not reconnect to recover a missed pre-subscription event.

Server logs searched over 11:16–17:09Z contain one shutdown/start pair and one fleet catch-up completion, with **no** `Session runner event stream disconnected`, `Failed to persist transcript entries`, or transcript-row failure match in that interval. The AppHost log says `session-runner: port 17204 already listening — adopting`. No runner restart occurred: current listener and process identity still match the October 8 startup below. These absences narrow the mechanism; they do not prove that a Debug-only failure never happened.

## Runner capture versus server persistence

Caller: `c83e3c62-c0ec-4a12-b40d-8f083dbd8885`. Its DB AgentSessions row has StartedAt `07:00:43.702936`, Status 2 (Running), and null RunnerId/RunnerStoreId (local desktop routing).

Fresh runner-local transcript metadata preserves the original sequence order:

| Runner sequence | Kind | Producer Timestamp | UUID | Server sequence / CreatedAt |
|---|---|---|---|---|
| 15932 | ToolResult | 11:19:08.789 | 2e66bd6f-16a3-49dd-9ab2-d760644d4eaa | 16935 / 11:19:16.499347 |
| 15933 | ToolCall | 11:19:11.614 | 27e4580f-cbfc-463c-a549-523a8fde1637 | 16936 / 11:19:16.499347 |
| 15934 | ToolResult | 11:19:19.914 | 2179db4a-de02-421d-ad09-98237526534c | 16982 / 17:07:38.350141 |
| 15935 | AssistantText | 11:19:24.074 | 216336dd-1779-4b48-a100-673231b2b33a | 16983 / 17:07:38.350141 |
| 15936 | TurnEnd | 11:19:24.074 | 216336dd-1779-4b48-a100-673231b2b33a | 16984 / 17:07:38.350141 |
| 15937 | CompactBoundary | 12:14:18.208 | cdff2140-14f8-4e41-86ef-9ffe7e82dc8d | 16937 / 12:14:20.339209 |
| 15938 | UserPrompt | 12:14:18.201 | e31fcb8b-bebc-4d03-b0f0-28401afd89e6 | 16938 / 12:14:20.368672 |

The production tailer increments its sequence and appends to the runner snapshot **before** publishing each event (`TranscriptTailer.cs:522`, `:527`, `:529`), synchronously inside `EmitLine`. The same runner process stayed alive. Therefore runner capture of 15934–15936 occurred before the compact event 15937, whose live event the server had persisted by 12:14:20.339209. A tailer that first exposed the old tail at 17:07 is ruled out. Native producer timestamps alone do not prove that the tailer emitted 15936 before 11:19:49.825: the retained runner log records binding, not per-record emission. That distinction is the remaining incident attribution uncertainty.

Runner Serilog line 76733 at 07:00:44.846Z records tailing the exact known file `C:\Users\lndco\.claude\projects\C--src-Antiphon\c83e3c62-c0ec-4a12-b40d-8f083dbd8885.jsonl`. No later log line names this caller. The documented sidecar path `C:\logs\antiphon\session-runner\transcripts\c83e3c62-c0ec-4a12-b40d-8f083dbd8885.json` is absent; no substitute was created. The successful runner transcript read and exact binding log are the custody evidence used here.

## Why recovery happened at 17:07:38

This part is confirmed by incident logs, DB rows and source, rather than timing alone:

1. A new caller TurnEnd with producer Timestamp `17:07:05.773` was stored as server sequence 16979 at `17:07:06.067434`. It reopened the ordinary idle queue gate. The missing old TurnEnd was not needed to trigger this first reopening.
2. Server lines 43507–43511 at `17:07:06.145–.157` shrink all five polled completion notes. An older settings message was at the queue head. Its prompt was submitted at `17:07:06.863` (server sequence 16987, backfilled later).
3. Server line 43515 at `17:07:14.173` records a second Enter, and 43517 at `17:07:21.543` a third. Line 43522 at `17:07:36.991` records `Delivery verification failed ... body (688 chars) ... past sequence 16979 within 30s after 3 Enter(s)`.
4. The failure handler calls the post-failure grace receipt check (`SessionMessageQueueService.cs:4699`). That check pulls via `CatchUpTranscriptAsync` before checking receipt (`:4223`, `:4240`). `AgentSessionRuntime.cs:719–725` fetches the runner snapshot and persists it without queue side effects.
5. The old ToolResult/AssistantText/TurnEnd were saved at `17:07:38.350141`. Server 43523 at `17:07:38.427` late-confirms settings queue row `6ed11d6f-3de0-4210-9702-3cef0063d67b`, baseline 16979. Line 43524 at `.430` confirms all one message reached transcript in the grace window. First completion input attempt follows at `17:07:38.489845`.

The live pump awaits transcript handling and turn-boundary queue handling (`AgentSessionRuntime.cs:382`, `:402`, `:648`), explaining why submitted prompt evidence can sit behind its own delivery confirmation. The 32-second recovery here is positively identified as the settings-delivery timeout/grace pull. No AppHost or runner restart at 17:07 caused it. A second ordinary system message repeats the same shape at 17:08:43.726 (server 43604), followed by late confirmation at 17:08:45.166 (43605).

## Five completion outbox and queue rows

All five incident rows: notification Kind 6 (TaskCompletion), State 3 (Confirmed), EnqueueAttempts 1, LastErrorCode null; linked queue Status 1 (Sent), DeliveryAttempts 1, DeliveryVerdict 0 (Delivered), HoldUntil null. First attempts were hours after queue creation. This was withheld input, rather than repeated failed typing or a missing completion obligation.

| Task prefix | Notification CreatedAt | Queue CreatedAt | SentAt | ConfirmedAt | ConfirmingPromptSequence |
|---|---|---|---|---|---|
| fb9c2e7a | 11:28:57.334250 | 11:29:07.069410 | 17:07:38.489845 | 17:07:43.599494 | 16990 |
| bf5659fd | 11:43:41.247744 | 11:44:07.918300 | 17:07:47.997351 | 17:07:51.655416 | 16999 |
| 12bf506d | 11:44:14.154528 | 11:44:38.177671 | 17:08:05.734839 | 17:08:10.409843 | 17002 |
| feafa656 | 12:12:39.735250 | 12:13:03.772333 | 17:08:08.657219 | 17:08:11.965293 | 17007 |
| b48a7bff | 12:49:19.523394 | 12:49:43.492620 | 17:08:45.205945 | 17:08:48.503012 | 17022 |

| Full TaskId | NotificationId | QueueId | LastDeliveryBaselineSequence |
|---|---|---|---|
| fb9c2e7a-2094-4879-a9b6-f94647900710 | 583191a1-cfad-4760-9679-7d7734a72b2a | 22413b9a-e95c-4f5b-93d7-5a6283863ae9 | 16989 |
| bf5659fd-e2d4-4a8e-9a58-f6de75997428 | 9894a85c-3db3-4397-80de-52953233ff3a | d4780c8d-d0dd-46c3-baab-6b37fef6b150 | 16996 |
| 12bf506d-843b-4bba-97fc-39ea42923d94 | b19959f4-5636-4c50-86a4-1f9bf79a0122 | 5710bce7-20dc-4162-89b6-45e892333c9f | 17001 |
| feafa656-4ccb-4b0c-9011-6969f9599869 | edc998aa-acd9-49a2-b3b2-90773732ba90 | 365dce00-9d61-44b7-84cf-dad6e53a02c6 | 17004 |
| b48a7bff-241c-4699-a1c8-31e64bd3b126 | f3964563-f3a4-47a7-b2ae-fd56f6fe1bfb | 89f75b15-2bc5-4456-a79f-556e07e6d58e | 17021 |

Server lines 37124, 37209, 37286, 37629, 37731, 37751, 37973, 38156, 38160, 38165, 38290 and 39075 mention the caller and checks on these tasks between 11:20:01.727 and 11:49:51.058Z. These Check-service `delivered` messages concern delegate checks, not caller completion receipt. The completion-specific caller log lines begin at 17:07:06, then recur at 17:07:38 (43525–43529), 17:07:47 (43540–43543), 17:08:05 (43547–43549), 17:08:08 (43551–43552), 17:08:12 (43553), and 17:08:45 (43607). The notification/queue rows establish exactly what remained owed during the intervening hours.

The source report measured completion-to-submitted-prompt delays of 4h19m26.363s–5h38m41.833s. Fresh DB values agree. Later landing/outcome rows for some of these tasks were created after 17:09 and are outside this incident; they are not additional delayed completions.

## Runner version, regression and exposure

`GET http://localhost:17204/capabilities` gives runner commit `8e1fe916cc421a98dbef46d73c1ee9bf97ef6f3e`, assembly write time `2026-10-08T22:36:20.6227522Z`, process start `2026-10-08T22:36:21.4696355Z`. `Get-NetTCPConnection -LocalPort 17204 -State Listen` identifies PID 24664; process metadata independently matches that start and canonical runner executable. Supervisor log lines 48–50 name the same runner PID/start. This **is** the older desktop process that tails the caller. It was not upgraded by the October 12 AppHost restart.

That older build is not evidence that CARD-0262 caused transcript loss. Comparing it to activated server/source `ce470787a` yields no difference in `TranscriptTailer.cs` or runner `Program.cs`; `SessionRunnerRuntime.cs` differs only in three KillGenerationAsync return projections adding ExitReason metadata. Catch-up/event hub behavior is unchanged. Comparing pre-restart server `a91ba68a72185d57d39e87b218e459c5df2451de` to `ce470787a` similarly finds no event-pump, tailer, or queue delivery change; AgentSessionRuntime only changes a test adapter's kill result projection. A new activation-range regression is not demonstrated. The source report traces catch-up-before-stream ordering to `3c2c81056b6a0996e82614a1ac08960f5598606b` (August 8); that is historical provenance, **not** an established first bad commit. No incident-equivalent bisect was run.

Source exposure is all local sessions whose records publish after their own catch-up snapshot and before the fleet finishes and subscribes; a missing boundary can strand any idle-gated queue. This is not specific to task-completion notes or this caller's pins. The 73-session catch-up makes a material window. This report does not establish the same loss for phone-home remote sessions, which have a different pump.

A read-only DB scan for ordinary prompt/tool/text/end rows with both clocks inside 11:16–17:09Z and ingestion lag over ten minutes returned **one session**, this caller, **three rows**, maximum lag `05:48:18.436141`. That bounds observed long-delay rows, not all exposed callers: never-ingested records and delays outside the window cannot appear in this query.

## Remaining uncertainty and next evidence

Confirmed: old tail captured before 12:14; server ingestion delayed; missing boundary withheld the five prompts; new 17:07 boundary reopened delivery; the settings delivery grace pull recovered the old tail. Strongest attribution: the post-restart catch-up/SSE coverage gap. Still unobserved: exact original publication times, exact subscription time, and any Debug-only ingest/catch-up exception. `RuntimePhase.cs:32` emits the useful phase evidence at Debug; no such lines were retained in the examined Info-level server log. Absence of Warning persistence failures weighs against a DB-write failure, but is not an exhaustive exclusion of a pre-storage exception.

To resolve upstream attribution, reproduce the production pump/hub ordering in an isolated test with a controlled tail published after one session snapshot and before subscription, record both custody and storage, then compare the failure signature to this incident. If available, an already-retained metadata-only runner publish trace or HTTP `/events` request timestamp from this restart would close the live timing gap directly. Do not restart the live stack to manufacture that evidence. Keep first-bad-commit unassigned until there is an executable discriminator.

## Not done, noted

Potential follow-on: close the snapshot-to-subscription coverage gap and require a restart-survival test that submits a TurnEnd inside it; no fix was designed or implemented here.

## Re-query

```sql
BEGIN READ ONLY;
SELECT "Sequence", "Kind", "Timestamp", "CreatedAt", "Uuid"
FROM "TranscriptEntries"
WHERE "AgentSessionId" = 'c83e3c62-c0ec-4a12-b40d-8f083dbd8885'
  AND "Sequence" BETWEEN 16927 AND 16984 ORDER BY "Sequence";
SELECT n."TaskId", n."Id", n."Kind", n."CreatedAt", n."EnqueuedAt",
       n."State", n."EnqueueAttempts", n."ConfirmedAt", n."ConfirmingPromptSequence",
       n."LastErrorCode", q."Id", q."CreatedAt", q."Status", q."SentAt",
       q."DeliveryAttempts", q."DeliveryVerdict", q."LastDeliveryBaselineSequence", q."HoldUntil"
FROM "AgentTaskLandNotifications" n
JOIN "SessionQueuedMessages" q ON q."Id" = n."QueueMessageId"
WHERE left(n."TaskId"::text,8) IN ('feafa656','12bf506d','bf5659fd','b48a7bff','fb9c2e7a')
  AND n."CreatedAt" <= '2026-10-12T17:09Z' ORDER BY n."CreatedAt";
SELECT "AgentSessionId", count(*), max("CreatedAt" - "Timestamp")
FROM "TranscriptEntries"
WHERE "CreatedAt" BETWEEN '2026-10-12T11:16Z' AND '2026-10-12T17:09Z'
  AND "Timestamp" BETWEEN '2026-10-12T11:16Z' AND '2026-10-12T17:09Z'
  AND "Kind" IN ('UserPrompt','ToolCall','ToolResult','AssistantText','TurnEnd')
  AND "CreatedAt" - "Timestamp" > interval '10 minutes'
GROUP BY "AgentSessionId" ORDER BY max("CreatedAt" - "Timestamp") DESC;
ROLLBACK;
```

## Caller transcript rows 16927–16984

The complete metadata table follows; no transcript bodies or tool arguments are included.
| Server sequence | Kind | Timestamp UTC | CreatedAt UTC |
|---|---|---|---|
| 16927 | UserPrompt | 2026-10-12T11:16:06.421Z | 2026-10-12T11:16:08.879359Z |
| 16928 | ToolCall | 2026-10-12T11:16:13.122Z | 2026-10-12T11:16:13.486058Z |
| 16929 | ToolResult | 2026-10-12T11:16:38.002Z | 2026-10-12T11:16:38.307441Z |
| 16930 | ToolCall | 2026-10-12T11:16:41.859Z | 2026-10-12T11:16:42.014902Z |
| 16931 | ToolResult | 2026-10-12T11:16:42.683Z | 2026-10-12T11:16:42.940745Z |
| 16932 | ToolCall | 2026-10-12T11:16:45.490Z | 2026-10-12T11:16:46.038469Z |
| 16933 | QueueEnqueue | 2026-10-09T23:23:07.651Z | 2026-10-12T11:19:16.499347Z |
| 16934 | QueueDequeue | 2026-10-09T23:23:07.682Z | 2026-10-12T11:19:16.499347Z |
| 16935 | ToolResult | 2026-10-12T11:19:08.789Z | 2026-10-12T11:19:16.499347Z |
| 16936 | ToolCall | 2026-10-12T11:19:11.614Z | 2026-10-12T11:19:16.499347Z |
| 16937 | CompactBoundary | 2026-10-12T12:14:18.208Z | 2026-10-12T12:14:20.339209Z |
| 16938 | UserPrompt | 2026-10-12T12:14:18.201Z | 2026-10-12T12:14:20.368672Z |
| 16939 | UserPrompt | 2026-10-12T17:04:52.800Z | 2026-10-12T17:04:53.142289Z |
| 16940 | ToolCall | 2026-10-12T17:04:56.422Z | 2026-10-12T17:04:56.851014Z |
| 16941 | ToolResult | 2026-10-12T17:04:57.825Z | 2026-10-12T17:04:58.100129Z |
| 16942 | ToolCall | 2026-10-12T17:04:59.870Z | 2026-10-12T17:05:00.263853Z |
| 16943 | ToolResult | 2026-10-12T17:05:05.316Z | 2026-10-12T17:05:05.523814Z |
| 16944 | ToolCall | 2026-10-12T17:05:06.867Z | 2026-10-12T17:05:07.397841Z |
| 16945 | ToolResult | 2026-10-12T17:05:06.888Z | 2026-10-12T17:05:07.403831Z |
| 16946 | ToolCall | 2026-10-12T17:05:08.643Z | 2026-10-12T17:05:09.254355Z |
| 16947 | ToolResult | 2026-10-12T17:05:09.879Z | 2026-10-12T17:05:10.180996Z |
| 16948 | ToolCall | 2026-10-12T17:05:11.531Z | 2026-10-12T17:05:12.027288Z |
| 16949 | ToolResult | 2026-10-12T17:05:12.836Z | 2026-10-12T17:05:13.262493Z |
| 16950 | AssistantText | 2026-10-12T17:05:15.129Z | 2026-10-12T17:05:16.035052Z |
| 16951 | ToolCall | 2026-10-12T17:05:15.691Z | 2026-10-12T17:05:16.057111Z |
| 16952 | ToolResult | 2026-10-12T17:05:19.184Z | 2026-10-12T17:05:19.413635Z |
| 16953 | ToolCall | 2026-10-12T17:05:25.103Z | 2026-10-12T17:05:25.552318Z |
| 16954 | ToolResult | 2026-10-12T17:05:25.475Z | 2026-10-12T17:05:25.857975Z |
| 16955 | ToolCall | 2026-10-12T17:05:27.816Z | 2026-10-12T17:05:28.319171Z |
| 16956 | ToolResult | 2026-10-12T17:05:28.256Z | 2026-10-12T17:05:28.636659Z |
| 16957 | ToolCall | 2026-10-12T17:05:34.196Z | 2026-10-12T17:06:12.040088Z |
| 16958 | ToolResult | 2026-10-12T17:05:34.282Z | 2026-10-12T17:06:12.050054Z |
| 16959 | ToolCall | 2026-10-12T17:05:43.350Z | 2026-10-12T17:06:12.118697Z |
| 16960 | ToolResult | 2026-10-12T17:05:43.404Z | 2026-10-12T17:06:12.126257Z |
| 16961 | ToolCall | 2026-10-12T17:05:52.896Z | 2026-10-12T17:06:12.147642Z |
| 16962 | ToolResult | 2026-10-12T17:05:52.941Z | 2026-10-12T17:06:12.153045Z |
| 16963 | ToolCall | 2026-10-12T17:06:02.395Z | 2026-10-12T17:06:12.171514Z |
| 16964 | ToolResult | 2026-10-12T17:06:02.437Z | 2026-10-12T17:06:12.176935Z |
| 16965 | ToolCall | 2026-10-12T17:06:11.731Z | 2026-10-12T17:06:12.195033Z |
| 16966 | ToolResult | 2026-10-12T17:06:11.787Z | 2026-10-12T17:06:12.200116Z |
| 16967 | QueuedUserPrompt | 2026-10-12T17:05:37.821Z | 2026-10-12T17:06:12.217311Z |
| 16968 | ToolCall | 2026-10-12T17:06:19.262Z | 2026-10-12T17:06:19.721801Z |
| 16969 | ToolResult | 2026-10-12T17:06:19.688Z | 2026-10-12T17:06:20.742285Z |
| 16970 | ToolCall | 2026-10-12T17:06:26.458Z | 2026-10-12T17:06:26.897881Z |
| 16971 | ToolResult | 2026-10-12T17:06:28.832Z | 2026-10-12T17:06:29.057144Z |
| 16972 | AssistantText | 2026-10-12T17:06:30.453Z | 2026-10-12T17:06:37.375915Z |
| 16973 | ToolCall | 2026-10-12T17:06:37.204Z | 2026-10-12T17:06:37.393631Z |
| 16974 | ToolResult | 2026-10-12T17:06:37.248Z | 2026-10-12T17:06:37.407983Z |
| 16975 | ToolCall | 2026-10-12T17:06:39.846Z | 2026-10-12T17:06:40.453770Z |
| 16976 | ToolResult | 2026-10-12T17:06:42.204Z | 2026-10-12T17:06:42.605090Z |
| 16977 | ToolCall | 2026-10-12T17:06:52.383Z | 2026-10-12T17:06:52.751359Z |
| 16978 | ToolResult | 2026-10-12T17:07:01.560Z | 2026-10-12T17:07:01.742074Z |
| 16979 | TurnEnd | 2026-10-12T17:07:05.773Z | 2026-10-12T17:07:06.067434Z |
| 16980 | QueueEnqueue | 2026-10-09T23:23:07.651Z | 2026-10-12T17:07:38.350141Z |
| 16981 | QueueDequeue | 2026-10-09T23:23:07.682Z | 2026-10-12T17:07:38.350141Z |
| 16982 | ToolResult | 2026-10-12T11:19:19.914Z | 2026-10-12T17:07:38.350141Z |
| 16983 | AssistantText | 2026-10-12T11:19:24.074Z | 2026-10-12T17:07:38.350141Z |
| 16984 | TurnEnd | 2026-10-12T11:19:24.074Z | 2026-10-12T17:07:38.350141Z |

No builds or automated tests were run; verification consisted of desktop log correlation, independent process/runner metadata and read-only DB reconstruction.

--- next stage ---
next: investigate
handoff: Reproduce the desktop pump's snapshot-before-subscription gap with a controlled TurnEnd; original publish/subscription times are absent at Info logging. Desktop rows confirm the tail existed before 12:14 and a settings-delivery grace pull recovered it at 17:07:38. Establish incident attribution and a first-bad discriminator before planning.
artifact: docs/investigations/2026-10-12-card-1205-desktop-evidence.md
