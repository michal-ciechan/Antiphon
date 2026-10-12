# CARD-1205: completion delivery delay after AppHost restart

The five notes were created promptly, retained in PostgreSQL, and delivered once each after 17:07Z. The confirmed withholding mechanism was the caller's persisted Working verdict: its 11:19:24 TurnEnd was missing until 17:07:38. The upstream reason that boundary missed ingestion is **not fully confirmed**; desktop server/runner logs are still required. Next stage: Investigate.

## Scope and provenance

Investigator task ef0bb4ed-e865-4591-9bcf-2937dc2f7a87; branch feat/card-task-ef0bb4ed; examined source ce470787aa054ee2d5c0fbdfd7a63a49645d4a96. Card read through scripts/card.ps1: CARD-1205, id 29eaf667-5a31-42c1-9248-c4d1cf758795, board 8988ca03-7414-47ad-b0b6-51556c701703. Title is preserved in this artifact's filename. All times below are UTC, 2026-10-12.

Evidence was obtained from the task-authenticated server API at ANTIPHON_API, known caller transcript route, individual task detail routes, /api/agents and /api/diagnostics/session-state. Read-only Npgsql queries reached the desktop antiphon database on its documented development PostgreSQL port, using BEGIN READ ONLY followed by ROLLBACK. No data, runtime settings, processes or production source were changed. API/DB captures are under the gitignored .antiphon/card-1205-evidence/ in the assigned mirror; essential row values and query are preserved here because mirror retirement can destroy those captures.

At 17:12:19.3419113Z, diagnostics reported server process 37924, processStartedAt=11:18:40.8075211Z, cache enabled=true, loadFaults=0, ingestCalls=8011, committedRows=1412. The API version matched ce470787a. Caller c83e3c62-c0ec-4a12-b40d-8f083dbd8885 was still Running, ClaudeCode, cwd C:\src\Antiphon, startedAt=07:00:43.702936Z; this AppHost restart did not restart the caller process.

## What happened to the five notes

AgentTasks.ParentSessionId and all five outbox/queue destinations match c83e3c62-c0ec-4a12-b40d-8f083dbd8885. AgentTasks.ReplyTo=1 (Session), Status=4 (Succeeded). Each outbox row is Kind=6 (TaskCompletion), carries a completion snapshot, has EnqueueAttempts=1 and now State=3 (Confirmed), LastErrorCode=null. Each linked queue row has DeliveryAttempts=1, Status=1 (Sent), DeliveryVerdict=0 (Delivered), HoldUntil=null, CanceledAt=null. LastDeliveryStartedAt equals SentAt; LastDeliveryGeneration=07:00:43.702936Z for every note. The five retained attempt stamps begin after 17:07Z; there is no stored evidence of earlier charged attempts. Complete UserPrompt receipts, rather than attempt bookkeeping alone, establish delivery.

| Task | CompletedAt | Queue CreatedAt | First attempt / SentAt | Submitted UserPrompt | Completion to UserPrompt | Queue to first attempt |
|---|---|---|---|---|---|---|
| fb9c2e7a | 11:28:57.33425 | 11:29:07.06941 | 17:07:38.489845 | 17:07:39.167 (#16990) | 20321.833 s | 20311.420 s |
| bf5659fd | 11:43:41.247744 | 11:44:07.9183 | 17:07:47.997351 | 17:07:48.681 (#16999) | 19447.434 s | 19420.079 s |
| 12bf506d | 11:44:14.154528 | 11:44:38.177671 | 17:08:05.734839 | 17:08:06.783 (#17002) | 19432.629 s | 19407.557 s |
| feafa656 | 12:12:39.73525 | 12:13:03.772333 | 17:08:08.657219 | 17:08:09.386 (#17007) | 17729.651 s | 17704.885 s |
| b48a7bff | 12:49:19.523394 | 12:49:43.49262 | 17:08:45.205945 | 17:08:45.886 (#17022) | 15566.363 s | 15541.713 s |

The measured wait is 4h19m26.363s to 5h38m41.833s from settlement to the submitted prompt, substantially longer than the card's initial “about an hour.” Completion-to-queue creation is 9.735–26.671 seconds. AgentTasks.CompletionNoteQueuedAt matches the outbox admission times, within milliseconds of queue creation.

Full identities for independent retrieval:

| TaskId | AgentTaskLandNotifications.Id | SessionQueuedMessages.Id | Outbox ConfirmedAt |
|---|---|---|---|
| fb9c2e7a-2094-4879-a9b6-f94647900710 | 583191a1-cfad-4760-9679-7d7734a72b2a | 22413b9a-e95c-4f5b-93d7-5a6283863ae9 | 2026-10-12T17:07:43.599494Z |
| bf5659fd-e2d4-4a8e-9a58-f6de75997428 | 9894a85c-3db3-4397-80de-52953233ff3a | d4780c8d-d0dd-46c3-baab-6b37fef6b150 | 2026-10-12T17:07:51.655416Z |
| 12bf506d-843b-4bba-97fc-39ea42923d94 | b19959f4-5636-4c50-86a4-1f9bf79a0122 | 5710bce7-20dc-4162-89b6-45e892333c9f | 2026-10-12T17:08:10.409843Z |
| feafa656-4ccb-4b0c-9011-6969f9599869 | edc998aa-acd9-49a2-b3b2-90773732ba90 | 365dce00-9d61-44b7-84cf-dad6e53a02c6 | 2026-10-12T17:08:11.965293Z |
| b48a7bff-241c-4699-a1c8-31e64bd3b126 | f3964563-f3a4-47a7-b2ae-fd56f6fe1bfb | 89f75b15-2bc5-4456-a79f-556e07e6d58e | 2026-10-12T17:08:48.503012Z |

All five completion headers occur in actual UserPrompt transcript entries, not only ToolResults or redraws: sequences 16990, 16999, 17002, 17007 and 17022. Their receipt sequences equal the respective outbox ConfirmingPromptSequence. The immutable completion obligation and queue handoff are implemented at server/Application/Services/TaskCompletionNotification.cs:107 and server/Application/Services/AgentTaskLandNotificationService.cs:143. All five stored delivery renderings are “polled”: the reports were withheld because the caller had read them at 17:05Z. Their NoteShrunk task events begin at 17:07:06Z. The poll did not create these old queue/outbox rows.

ReadNotifications at caller transcript sequence 16944 (17:05:06.867Z) returned “No queued notifications” at sequence 16945. That tool result is not evidence of an empty Antiphon delivery queue: these five SessionQueuedMessages rows had already existed for hours, with their first attempts still in the future. The actual GET /api/sessions/{caller}/messages captured after all deliveries had messages=[] and working=false, as expected then.

## Confirmed withholding mechanism

The restart command is preserved at caller sequence 16932; result sequence 16935 records restart exit=0 and server ce470787a. The restart/version/status reply was produced at 11:19:24.074Z, but its AssistantText and TurnEnd were not persisted until 17:07:38.350141Z, a lag of **5h48m14.276s**. The preceding ToolResult lag was **5h48m18.436s**. CreatedAt is the ingestion clock assigned by server/Application/Services/AgentSessionRuntime.cs:1062; Timestamp retains producer time at line 1043. These are separate clocks, not a timezone interpretation.

| Stored sequence | Kind | Producer Timestamp | DB CreatedAt |
|---|---|---|---|
| 16935 | ToolResult | 2026-10-12T11:19:08.789Z | 2026-10-12T11:19:16.499347Z |
| 16936 | ToolCall | 2026-10-12T11:19:11.614Z | 2026-10-12T11:19:16.499347Z |
| 16937 | CompactBoundary | 2026-10-12T12:14:18.208Z | 2026-10-12T12:14:20.339209Z |
| 16938 | UserPrompt | 2026-10-12T12:14:18.201Z | 2026-10-12T12:14:20.368672Z |
| 16939 | UserPrompt | 2026-10-12T17:04:52.8Z | 2026-10-12T17:04:53.142289Z |
| 16979 | TurnEnd | 2026-10-12T17:07:05.773Z | 2026-10-12T17:07:06.067434Z |
| 16982 | ToolResult | 2026-10-12T11:19:19.914Z | 2026-10-12T17:07:38.350141Z |
| 16983 | AssistantText | 2026-10-12T11:19:24.074Z | 2026-10-12T17:07:38.350141Z |
| 16984 | TurnEnd | 2026-10-12T11:19:24.074Z | 2026-10-12T17:07:38.350141Z |
| 16986 | TurnEnd | 2026-10-12T17:07:05.782Z | 2026-10-12T17:07:38.350141Z |
| 16989 | TurnEnd | 2026-10-12T17:07:08.292Z | 2026-10-12T17:07:38.350141Z |

A post-restart ingestion batch persisted the restart result and next ToolCall at 11:19:16.499347Z. It did not persist the 11:19:19 ToolResult or 11:19:24 reply/end then. The DB timestamp alone does not identify which pull or callback owned that batch. Live ingestion continued: the automatic compact boundary and synthetic summary arrived at 12:14:20Z, and the operator's “How’s it going?” UserPrompt arrived at 17:04:53Z. This is a missing tail of a turn, not a six-hour total runner outage.

Using the production SQL classification predicates from server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:16 and :21 against rows whose CreatedAt was at or before each observation time reproduced these historical decisions:

| Observation time | Latest stored end sequence | Latest end Timestamp | Newest qualifying activity sequence | Reconstructed Working |
|---|---|---|---|---|
| 2026-10-12T11:19:25Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T11:29:08Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T11:44:39Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T12:13:04Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T12:14:21Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T12:49:44Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T17:04:52Z | 16924 | 2026-10-12T11:16:04.811Z | 16936 | true |
| 2026-10-12T17:07:06.1Z | 16979 | 2026-10-12T17:07:05.773Z | none | false |
| 2026-10-12T17:07:38.4Z | 16989 | 2026-10-12T17:07:08.292Z | none | false |

At every completion admission, end sequence 16924 was still the last end; activity sequence 16936 was a ToolCall timestamped 11:19:11.614Z, newer than the last end at 11:16:04.811Z. Both the SQL oracle and enabled cache therefore read Working. The cache rules at server/Application/Services/SessionStateSnapshot.cs:31 and :63 agree. The automatic compact boundary and synthetic continuation summary are deliberately excluded from end/activity classifications; neither supplies the missing TurnEnd.

SessionMessageQueueService.FlushIfIdleAsync refuses delivery while ReadWorkingAsync is true (server/Application/Services/SessionMessageQueueService.cs:1815); it uses the enabled state store at :5254. The first newly ingested 17:07:05.773 TurnEnd changed the reconstructed verdict to idle at 17:07:06.067434Z. The older settings note appears as UserPrompt sequence 16987 timestamped 17:07:06.863Z, then the 11:19 tail appears in the 17:07:38 batch; the first completion attempt stamp followed that batch by 139 milliseconds. The queue ordering explains the later notes proceeding through individual NO_REPLY turns. The precise callback that owned this catch-up and the extra 32 seconds needs desktop phase logs; it is not assumed from timing alone. The settings note's saved attempt stamp below is later than its producer UserPrompt timestamp, another reason to distinguish bookkeeping from receipt time.

An older settings queue row 6ed11d6f-3de0-4210-9702-3cef0063d67b (sequence 3193) was created at 11:19:57.168936Z and has retained attempt stamp 17:07:38.427706Z. The compaction note 67e858ef-934a-48bc-af5c-5d71023d8e94 (sequence 3211) was created at 12:14:20.354215Z and has retained attempt stamp 17:08:45.166312Z. Their actual submitted prompt timestamps precede these stamps. The same caller-wide idle gate held ordinary system notes as well as completions, but the system-note stamps alone cannot establish their first input times.

Source includes a boot/15-minute completion backstop that selects Pending zero-attempt sourced rows and schedules flushes (server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:36, :69 and :149). Therefore “nothing re-offers the note” is not established. A repeated flush can still refuse the same stale Working verdict. Receipt catch-up in AgentTaskLandNotificationService is inside the DeliveryAttempts > 0 arm (:240), so those zero-attempt completions do not themselves pull the missing caller tail through the receipt scanner.

## Upstream cause and regression status

**Leading hypothesis, source-confirmed coverage gap:** SessionRunnerEventPump completes catch-up for all desktop sessions before it opens the event stream (server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:44, :46 and :123). RoutingSessionRunnerClient.ListAsync and StreamEventsAsync both select the local runner (:45, :120), independently of the phone-home pump delivering the remote tasks. The runner's /events endpoint subscribes only after connection begins (src/Antiphon.SessionRunner/Program.cs:401). SessionRunnerEventHub publishes to currently subscribed readers and does not replay earlier publications (src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4304 and :4319). A TurnEnd published after that caller's snapshot but before the SSE subscription can therefore miss both coverage paths. The incident's ingestion batch at 11:19:16 and missing producer tail at 11:19:19–24 fit this mechanism, subject to proving the underlying snapshot and subscription times. **The live subscription time is not available here**, so this is a candidate upstream explanation, not a confirmed attribution.

Other live explanations are a runner tailer that exposed this tail only later, or a server ingestion failure for those events followed by recovery. Desktop event/phase logs and the caller's known runner/provider transcript can distinguish them. The fact that later compact/operator events streamed excludes a sustained total disconnection, but does not distinguish these three causes.

Known-good evidence is from the same caller before the restart: task 3c07676a-7395-41d4-83bb-56b741c4a9fc had queue CreatedAt=11:15:40.933806Z, SentAt=11:15:41.053016Z and outbox ConfirmedAt=11:15:42.918891Z; sequence 16918 contains the done UserPrompt at 11:15:42.315Z. Server version was a91ba68a72185d57d39e87b218e459c5df2451de at transcript sequence 16166, 07:09:30.22Z. The earlier restart record at sequence 16067 identifies the desktop runner as build 8e1fe916cc421a98dbef46d73c1ee9bf97ef6f3e, running since 2026-10-08 22:36:21. Later restart text says the runner was preserved.

Git log/diff from a91ba68a to ce470787a finds no change to the event pump, idle SQL/cache classifier, completion queue or outbox delivery in that activation range. The only change in AgentSessionRuntime is b1dfabcd0's one-line test-adapter KillGenerationAsync return projection; it does not change this production ingestion path. ce470787a itself changes only GuardedWorktreeRemoval.cs. Catch-up-before-stream ordering dates to 3c2c81056b6a0996e82614a1ac08960f5598606b, 2026-08-08, and is present at the known-good SHA. Thus **a new delivery-code regression from the activated range is not demonstrated**; a pre-existing restart-window defect is plausible. Do not call that August commit the first bad change without reproducing the live loss mechanism. No bisect was run because an incident-equivalent failure discriminator has not yet been established.

## Completed source reconstruction and desktop handoff

Follow-up scope: the caller commissioned desktop task **d5091e82** to gather the inaccessible desktop evidence and push **docs/investigations/2026-10-12-card-1205-desktop-evidence.md**. That artifact has not been inspected in this source reconstruction; its contents and publication are not assumed. This report supplies the source-side discriminators for the next Investigate pass, without waiting for that independent task.

The desktop ingestion chain is concrete:

1. `TranscriptTailer.ProcessPending` consumes newline-terminated records; `EmitLine` normalizes each record, appends its parts to the in-memory runner snapshot, then publishes each part to the event hub (src/Antiphon.SessionRunner/TranscriptTailer.cs:483, :505 and :529). Runner snapshot custody and live subscriber delivery are separate operations. A DB row missing until 17:07 does not, by itself, establish that the runner lacked the record before then.
2. `SessionRunnerEventPump` first calls List and synchronously awaits every session's `SyncTranscriptAsync`; that sync includes persistence and turn-boundary callbacks before the pump proceeds to the next session (:44 and :123). The HTTP stream request is made only when the later async iterator is consumed (server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:865 and :878). The server's initial caller pull can therefore finish while the stream has no subscription yet.
3. `SessionRunnerEventHub.Publish` snapshots current subscribers and offers the event only to them (src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4319). `Subscribe` adds a reader without historical replay (:4304). An event emitted after the caller's catch-up snapshot and before subscription is absent from both that snapshot and the new live stream. This is source reconstruction of a possible loss window, not a claim that the incident's subscription time is known.
4. Once connected, the runner emits keepalive comments, normally every 15 seconds (src/Antiphon.SessionRunner/Program.cs:398 and :408). Every received line, including a keepalive, resets the client's idle watchdog (server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:888–894); the default watchdog is 90 seconds (server/Application/Settings/SessionRunnerSettings.cs:21). Healthy keepalives can prevent reconnection indefinitely while an earlier event remains missing. The loop performs catch-up at connection iterations, not periodically while a healthy stream remains open.
5. The normal live path awaits SignalR publication **before** storage and awaits turn-boundary queue effects afterward (server/Application/Services/AgentSessionRuntime.cs:371, :382 and :402). An inline callback can obstruct consumption of subsequent events; source therefore does not rule out a blocking interval around the lost tail. Stored CARD-0432 evidence already demonstrates serial pump/catch-up obstruction with an isolated database: docs/investigations/2026-09-07-card-0432-s0-s2-evidence.md:10. The retained characterization test `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` explicitly expects another session's transcript not to persist while SignalR or queue confirmation is held (tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs:30). That is prior evidence, not a test rerun or incident attribution here.
6. A caller with a missing end remains Working. A sourced zero-attempt completion backstop can re-offer a flush without pulling the caller transcript, and the idle gate continues refusing it. By contrast, a later actual caller turn end can reopen delivery. During delivery verification, a complete runner pull can recover the old missing tail; that pull preserves the producer timestamps and rebases new stored sequences above the current maximum (server/Application/Services/AgentSessionRuntime.cs:719 and :1032).

The 32-second recovery batch has a specific **candidate** callback explanation. Ordinary delivery confirmation waits up to the configured timeout, 30 seconds by default (server/Application/Settings/SupervisionSettings.cs:355). Its regular loop pulls the transcript only for full-inline specialist evidence or an unobservable baseline (server/Application/Services/SessionMessageQueueService.cs:4081). An ordinary observable-baseline prompt whose confirming event is behind the pump's own awaited queue callback can therefore run to timeout. The post-failure grace path then calls `CatchUpTranscriptAsync` before checking receipts (:4223 and :4240), through the failure handler at :4699. That pull has no queue side effects, so it can run while the delivery lock is held; `SyncTranscriptAsync` would re-enter boundary handling instead (server/Application/Services/AgentSessionRuntime.cs:702). This source path fits a submitted settings prompt at 17:07:06.863Z, its reply at 17:07:08.292Z, and the recovery batch at 17:07:38.350141Z. **Effective timeout settings, verdict logs and the identity of the actual pull remain unverified**, so timing alone does not prove this was the callback.

The stalled desktop runner's known build also narrows the version hypothesis. Comparing 8e1fe916cc421a98dbef46d73c1ee9bf97ef6f3e to ce470787a shows no changes in `TranscriptTailer.cs`, `TranscriptNormalizer.cs` or the runner `Program.cs`. `SessionRunnerRuntime.cs` differs only in three `KillGenerationAsync` return expressions adding exit-reason metadata; its hub/snapshot path is unchanged. These observations weaken attribution to the pending CARD-0262 runner upgrade but do not certify the live runner's file binding, socket state or provider output.

Desktop evidence should discriminate as follows:

| Live explanation | Decisive incident evidence |
|---|---|
| Snapshot-to-subscription gap | The runner had parsed/published the missing tail after the caller's snapshot, while desktop SSE subscription began after that publication; subsequent live events arrived normally. |
| In-stream callback obstruction | SSE was already subscribed and the event was retained, with an outstanding pump/callback phase preventing storage; its completion or a separate pull explains recovery. |
| Server persistence failure | The missing event reached `pump.event`/persistence, but its save failed; exact UUID/kind and warning/failure evidence bind the failed row. |
| Runner tailer delay or binding issue | The bound runner snapshot/native file evidence shows the tail became available only later, or the tailer failed to normalize/read the original records. |

The missing AssistantText and TurnEnd share native UUID **216336dd-1779-4b48-a100-673231b2b33a**; their kinds distinguish the two normalized rows. TurnEnd row id is **d74f4587-7e52-48c5-85c6-069a0b755293**, stored sequence 16984. `RuntimePhase` emits at Debug level (server/Application/Services/RuntimePhase.cs:32), and phase scope exit is not proof of a successful commit; `transcript.save completed` is the stronger marker. If Debug logging was disabled, absence of phase lines cannot establish absence of the event. Preserve actual log availability and use retained HTTP/runner/native evidence rather than infer an event loss solely from missing Debug lines.

## Impact and remaining evidence

A complete uncapped SQL census of profile-v1 TaskCompletion outbox rows created since 2026-10-12 00:00Z returned 41 rows at capture time. All 41 had this same caller; 35 preceded the restart, these five followed it, and the next task 4b08e5d5 followed recovery with a queue-to-SentAt wait of 0.069038 seconds. This dataset cannot establish other callers are affected or unaffected. The snapshot/subscription mechanism is structurally shared by desktop sessions; fleet-wide impact is an inference to investigate, not an observed fact.

Desktop server retrieval failed: scripts/logs.ps1 -Source desktop-server -Tail 5 exited 1, “No log files found at /work/repos/antiphon/server/logs/antiphon-*.log.” The logs owner locates the real files at C:\src\Antiphon\server\logs\antiphon-*.log (docs/logs.md:11). Those files and the desktop runner's logs are not in this Linux mirror. The local server2 runner log contains planned server-stopping disconnect/reconnect lines at 11:16:50Z, but it is not the caller's desktop transport and cannot resolve the missing desktop TurnEnd.

To confirm the upstream root, obtain desktop logs for 11:18:40–11:20Z containing pump.catch-up for c83e3c62, “Transcript catch-up completed,” the /events connection/subscription, and pump.event/transcript.persist/save for the 11:19:19.914 and 11:19:24.074 UUIDs. Missing ToolResult UUID=2179db4a-de02-421d-ad09-98237526534c; AssistantText UUID=216336dd-1779-4b48-a100-673231b2b33a. Retrieve the TurnEnd UUID from the SQL below. Also obtain the 17:07:06–38 phase lines naming who pulled the recovery batch. Compare the known caller runner transcript/sidecar with the provider's known session file only; do not enumerate unrelated provider stores. If subscription opened after 11:19:24 and the runner already held/emitted that boundary, the coverage gap is confirmed. If subscription preceded it, inspect the exact event/tailer/persistence failures instead.

## Reproduction query and evidence retention

This is a read-only reconstruction from retained DB rows, not a fabricated transcript fixture. It yielded 9 decisions: 7 Working and then 2 idle. CreatedAt filtering reconstructs when each row became available; exact transaction commit instants may differ by milliseconds, immaterial to the hours-long gap. No restart, runtime fault injection, production repair, build or repository test was run. An optional isolated observation using the existing /app runner assembly failed before exercising the hub because PowerShell's System.Text.Json assembly version did not match; it provides no red/green behavioral evidence.

Run against the desktop antiphon DB in a read-only transaction. The current raw rows can be retrieved with:

```sql
BEGIN READ ONLY;
SELECT "Id", "Sequence", "Uuid", "Kind", "Timestamp", "CreatedAt", "StopReason"
FROM "TranscriptEntries"
WHERE "AgentSessionId" = 'c83e3c62-c0ec-4a12-b40d-8f083dbd8885'
  AND "Sequence" BETWEEN 16927 AND 16989
ORDER BY "Sequence";
SELECT "Id", "TaskId", "ParentSessionId", "CreatedAt", "EnqueuedAt",
       "QueueMessageId", "ConfirmedAt", "ConfirmingPromptSequence", "State"
FROM "AgentTaskLandNotifications"
WHERE left("TaskId"::text,8) IN ('fb9c2e7a','bf5659fd','12bf506d','feafa656','b48a7bff');
ROLLBACK;
```

Exact historical verdict reconstruction executed:

```sql
WITH times AS (SELECT unnest(ARRAY['2026-10-12 11:19:25Z','2026-10-12 11:29:08Z','2026-10-12 11:44:39Z','2026-10-12 12:13:04Z','2026-10-12 12:14:21Z','2026-10-12 12:49:44Z','2026-10-12 17:04:52Z','2026-10-12 17:07:06.1Z','2026-10-12 17:07:38.4Z']::timestamptz[]) AS at), h AS (SELECT * FROM "TranscriptEntries" WHERE "AgentSessionId"='c83e3c62-c0ec-4a12-b40d-8f083dbd8885') SELECT times.at, e.seq AS "EndSequence",e.ts AS "EndTimestamp",a."Sequence" AS "ActivitySequence",a."Kind" AS "ActivityKind",a."Timestamp" AS "ActivityTimestamp",a."CreatedAt" AS "ActivityIngestedAt",a."Sequence" IS NOT NULL AS "Working" FROM times LEFT JOIN LATERAL (SELECT max("Sequence") AS seq,max("Timestamp") AS ts FROM h WHERE "CreatedAt"<=times.at AND ("Kind" IN ('TurnEnd','SessionRestartBoundary') OR ("Kind"='CompactBoundary' AND strpos("Text",'(manual)')>0) OR ("Kind"='UserPrompt' AND "Text" LIKE '[Request interrupted%'))) e ON true LEFT JOIN LATERAL (SELECT * FROM h WHERE "CreatedAt"<=times.at AND (e.seq IS NULL OR ("Sequence">e.seq AND ("Timestamp" IS NULL OR e.ts IS NULL OR "Timestamp">=e.ts))) AND "Kind" NOT IN ('TurnEnd','TurnTitle','SessionRestartBoundary','QueuedUserPrompt','QueueEnqueue','QueueDequeue','QueueRemove','CompactBoundary') AND ("Kind"<>'UserPrompt' OR "Text" IS NULL OR ("Text" NOT LIKE '<command-name>%' AND "Text" NOT LIKE '<local-command-stdout>%' AND "Text" NOT LIKE 'This session is being continued from a previous conversation%' AND "Text" NOT LIKE '[Request interrupted%')) ORDER BY "Sequence" DESC LIMIT 1) a ON true ORDER BY times.at;
```

SHA-256 of captured evidence bytes:

```text
6c929954762fd6a0e8c49863183de399b9736bd1572f7494dec02c4ff43e9507  caller-transcript.json
e6c55ca9f45819ffc4b6eda31c5a1d7d1ff2c94cfeceeeedaa305745a9636626  db-notifications.json
c8be5e6a7c2748f7b0a607bfb77b6099ae1007e951831c8fd18c8ad60917d231  db-queue.json
6f1a4608f9f63c609b1bc23841dfeea224102a077f8ae5a0e9fdd41b4855655f  db-transcript-window.json
a8a7faffb2c1d0521cbebd603852eb47d1b625f0258cf519e40bd7b3a06053e7  db-working-reconstruction.json
```

## Not done, noted

Candidate fix only, not designed: close or recover the snapshot-to-live-event coverage gap if desktop evidence confirms it; commission the restart-survival case → mutation → red design in a separate stage after attribution.

--- next stage ---
next: investigate
handoff: Reconcile this source/DB reconstruction with d5091e82's docs/investigations/2026-10-12-card-1205-desktop-evidence.md. Attribute the missing 11:19 caller TurnEnd using SSE subscription, callback and persist evidence; confirm the 17:07:38 recovery pull. Keep upstream cause unconfirmed until the live evidence distinguishes the hypotheses.
artifact: docs/investigations/2026-10-12-card-1205-task-completion-notes-not-delivered-to-the-orchestrator-after-an-apphost-restart-five-tasks-silent-for-about-an-hour.md
