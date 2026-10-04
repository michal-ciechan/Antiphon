# CARD-0701 investigation: session working-state cache

Date: 2026-10-04. Task: `646d8caf-44f9-4301-8289-6db119fb1530`. Stage: Investigate.

**Confirmed:** repeated transcript-state derivation and replay dedup reads remain in the running server. Round 1 already supplies a committed in-memory projection to queue and agent working-state reads; the remaining amplification belongs to unfinished Rounds 2/3. This investigation made no implementation or design changes. Next: **plan**, to reconcile the existing plan with the current source and evidence.

## Identity and stored history

Inspected source and live `GET /api/version` both reported `bb18064ba647e0ddb03cae4da437ab60ed447d98`. The response advertised `land-v2` and `operator-shutdown-v1`. No production process, settings, database statistics or transcript was modified.

`pwsh -NoProfile -File scripts/card.ps1 get CARD-0701 -Board Antiphon` returned card `3f44191a-60c5-40c7-8089-323d95930932` on board `8988ca03-7414-47ad-b0b6-51556c701703`. Its description reports historical 36 scans/s and 100% desktop CPU, then asks for ingest-updated memory state, caller inventory, notifications, restart evidence and desktop Postgres before/after acceptance. Those historical numbers were not reproduced here. It initially read Review; revision 13, `01a106bd-f80b-7db0-895c-db67880eaee6`, records this investigation's automatic move to InProgress at `2026-10-04T11:47:55.531292Z`.

Stored evidence was retrieved through:

- `GET /api/cards/3f44191a-60c5-40c7-8089-323d95930932/thread?boardId=8988ca03-7414-47ad-b0b6-51556c701703`.
- The same card's `/revisions?boardId=8988ca03-7414-47ad-b0b6-51556c701703` endpoint.
- `GET /api/diagnostics/session-state`, which reads metadata without pulling a transcript (`server/Api/Endpoints/DiagnosticsEndpoints.cs:16`).

The thread's stored task/result rows establish:

| Task row | Stored outcome relevant to this investigation |
|---|---|
| `6123de4a-de8d-453f-972a-113eca222622` | Plan succeeded; initial commission was R1 S0-S3/CP-1-CP-4; full acceptance required later rounds and desktop measurements. |
| `84de0093-e7b8-47f5-af8b-e1b59309ed96` | R1 Code succeeded; two intended query-budget reds, CP-2 192 passes, CP-3 15 passes, Unit 3,112 passes/29 skips; R2/R3 and desktop acceptance pending. |
| `0c382461-3148-4f60-8974-db84d1a67ac7` | Review found cascade deletion bypassing cache invalidation, despite passing R1 checkpoints. |
| `3678a831-82c8-4409-8019-28947f9f8949` | Repair recorded four assertion failures before repair and 198/198 CP-2 afterward. |
| `d59497d9-9a3f-4a83-97c4-efec238e54fc` | Final R1 repair review reported publication at `11df271537b559818229dae02e6490bc91cab3bf`; post-land CP-2 198/198 and CP-3 15/15. Explicitly left all positive controls, CP-5-CP-13 and desktop acceptance pending. |
| `0c287711-61b2-4609-bbc4-c59cbccd72e3` | Earlier live CPU investigation reported 0.64-0.73 server cores and about 270 EF queries/s; warned that trace stack percentages included waits and did not assign CPU share to EF. |

These are stored reports, not fresh execution or SHA-validated TRX evidence. The older desktop evidence path `C:\Antiphon\evidence\server-cpu-trace-2026-09-25\investigation.md` is inaccessible from this assigned Linux mirror. The older plan's 31-second SQL delta is likewise supplied historical evidence, not a new measurement (`docs/superpowers/plans/2026-09-25-card-0701-session-state-cache-plan.md:17`).

## Confirmed mechanism

1. **Unmigrated consumers still bypass the singleton.** The static `SessionMessageQueueService.IsWorkingAsync` calls `IsWorkingBatchAsync`, which always invokes `TranscriptWorkingStateQuery.ReadAsync` (`server/Application/Services/SessionMessageQueueService.cs:4996`). The SQL reconstructs end sequence, end timestamp and subsequent activity from `TranscriptEntries` on each invocation (`server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:31`), tagged `session-state.fallback` at line 70. It is indexed SQL, so current calls are not evidence of full-table scans.
2. **Timers multiply that derivation even when transcript state has not changed.** The dispatcher runs from a periodic timer (`server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs:35`; default five seconds at `server/Application/Settings/DelegationSettings.cs:18`). Pool release calls the SQL helper at `AgentTaskDispatcher.cs:7135`; idle retirement calls it at line 7291 and separately aggregates latest transcript time at line 7292. The stall policy calls the helper at `TaskProgressPolicy.cs:79` and then selects all of that session's progress rows at line 87 before filtering the time window in memory. These are eligible-work-dependent reads, not a fixed query count per tick.
3. **The ingest cache does not yet eliminate replay identity reads.** `AgentSessionRuntime.PersistTranscriptCoreAsync` queries `(Uuid, Kind)` in chunks of 512 (`server/Application/Services/AgentSessionRuntime.cs:910`), handles null-UUID sequences at line 924, then queries maximum sequence at line 937. Only afterward does line 991 return for a fully duplicate batch. Thus even an ingest with zero new rows can issue many reads; a full replay with U distinct UUIDs performs `ceil(U/512)` identity probes plus the sequence maximum and session lookup. Runtime catch-up and sync both use this path (lines 706 and 727).
4. **Runner binding is still read per routing lookup.** `PhoneHomeRunnerDirectory.GetBindingAsync` creates a scope and queries `AgentSessions` each time (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:200`). The diagnostics binding counter is zero because this path is not tagged; zero does not mean zero binding SQL.

These paths explain why enabling the R1 cache cannot by itself meet full-card acceptance. No competing mechanism is needed to explain the measured tagged reads and replay count. Their exact share of Postgres or server CPU remains uncertain.

## What already works in source and stored evidence

Queue working-state reads choose the singleton when enabled (`SessionMessageQueueService.cs:4993`, queue DTO at line 5028); agent list/detail do the same (`AgentService.cs:111`, line 168). `SessionStateStore` shares gates across reads and writes (`SessionStateStore.cs:65`, line 83). Ingest holds the lease through durable persistence and publication (`AgentSessionRuntime.cs:834`); publication folds committed rows or reloads an ambiguous result (line 852). Snapshot metadata includes sequence/kind/time, end/title/prompt maxima and working state (`SessionStateSnapshot.cs:8`), but no recent UUID set or subscriber API.

Startup warms pinned sessions before the hosted service starts (`SessionStateWarmupService.cs:8`); minute maintenance checks pins without reloading ready snapshots (`SessionStateStore.cs:186`, line 232). The loader batches requested IDs, while its count/effective-time aggregate still examines each requested session's retained rows (`SessionStateLoader.cs:39`). Bounded session batches do not imply a constant amount of transcript work per session.

The existing test `Concurrent_ingests_rebase_sequences_and_warmed_queue_observes_each_commit` checks working then idle after ingestion, stable state after replay, and **14 identity queries for 14 ingests** (`tests/Antiphon.Tests/Application/SessionStateCommitTests.cs:126`). The warmed queue/agent tests assert zero repeated working queries (`SessionStateCacheReadTests.cs:20`, line 32). These were inspected, not run during this investigation.

## Current consumer census

A source search for `SessionMessageQueueService.IsWorking(?:Batch)?Async(` found **38 call sites in 23 files**. Two are AgentService's disabled-cache alternatives; one is AgentSessionRuntime's no-store restart alternative. The remaining **35 sites in 21 files** still invoke SQL directly. This is a census of explicit working-helper callers, not every transcript/content read. The wider transcript census already exists in `docs/superpowers/plans/2026-09-25-card-0701-session-state-cache-plan.md:33`.

| Current consumer group | Direct SQL working sites | Trigger/rate evidence |
|---|---:|---|
| AgentTaskDispatcher | 6 | Five-second default tick; conditions and candidate count determine executions. Sites at 2123, 2264, 3807, 6655, 7135, 7291. |
| AttentionService and AttentionService.Leaks | 6 | Requests; UI list/summary every 15 s (`client/src/api/attention.ts:293`, 303), plus internal callers. Sites at 1005, 1088, 1146, 1427, 2689; Leaks:52. |
| AgentTaskReplyService, AgentTaskService, TaskDeadlinePolicy, TaskProgressPolicy, CheckCompactionContinuationService | 7 | Settlement/reply actions, task lists, dispatcher/check/deadline evaluation. References: ReplyService:2358/3621/3791; TaskService:2490; DeadlinePolicy:168; ProgressPolicy:79; ContinuationService:1018. |
| SpecialistRequestService and its Qualification partial | 3 | Two-second hosted reconciliation (`SpecialistRequestHostedService.cs:14`), plus explicit qualification. Sites 158; Qualification:40/133. |
| StandingSpecialistSeatService | 1 | Supervisor default 10 s (`SupervisionSettings.cs:13`; hosted service:106/115), site 43. |
| ContextCompactionService, PolicyRefreshService, QueuedInputWatchdogService, HerdrStatusCorroborationService | 5 | Minute supervisor sweeps (`AgentSupervisorHostedService.cs:32`, 39, 46); corroboration may read before and after a pull. Sites 260, 500, 127, 123/129. |
| RemoteControlRecoveryService, SessionHealthActions | 3 | Health/recovery/screen gates. Health cadence uses enabled probe settings, **floor 10 s** (`SessionHealthHostedService.cs:44`); default probes 60 s (`SupervisionSettings.cs:269`, 290). Sites Recovery:348/472; Actions:86. |
| DelegateCheckProbe, DiagnosticsBundleService, ScheduleService, SubscriptionUsageMonitorService | 4 | Check capture, explicit diagnostics, schedule fire, periodic quota action respectively. Sites 337, 175, 334, 189; rates depend on their policies/actions. |

Queue and agent working bits are cached, but the UI still polls: agent list/detail at five seconds (`client/src/api/agents.ts:554`, 569), working badge at three seconds (`client/src/features/agents/SessionWorkingBadge.tsx:16`). Queue delivery confirmation still polls with default 500 ms (`SupervisionSettings.cs:317`; `SessionMessageQueueService.cs:3864`) and retains durable receipt queries. `AgentTaskLandMonitorService` has no transcript derivation; notification receipt reads are in `AgentTaskLandNotificationService.cs:233`. Boards and completion-note amplification belong to CARD-0700/CARD-0699 as the existing plan states, not to an absent session cache.

## Natural-workload measurement

Two read-only diagnostic samples spanned **265.4837662 seconds**, from `2026-10-04T11:49:00.2479564+00:00` to `2026-10-04T11:53:25.7317226+00:00`. Both had process ID **18824**, start time `2026-10-04T05:28:42.6833643Z`, cache epoch `23ff8e55-5d74-4eb8-b694-951284670033`, and the same version SHA above. Both reported **16 live / 0 unknown sessions**, cache enabled, 250 cached sessions. No synthetic traffic was generated; unrelated real activity continued.

| Counter | Start | End | Delta | Per second |
|---|---:|---:|---:|---:|
| Cache hits | 159,788 | 160,857 | 1,069 | 4.027 |
| Cache loads / tagged seed attempts | 23 | 23 | 0 | 0 |
| Load faults | 0 | 0 | 0 | 0 |
| Evictions | 19 | 19 | 0 | 0 |
| Capacity fallbacks | 0 | 0 | 0 | 0 |
| Ingest calls | 63,795 | 63,861 | 66 | 0.249 |
| Committed rows | 5,555 | 5,610 | 55 | 0.207 |
| Duplicate batches | 58,276 | 58,287 | 11 | 0.041 |
| Tagged working-state fallback attempts | 14,693 | 14,878 | 185 | 0.697 |
| Tagged UUID identity attempts | 217,079 | 218,556 | 1,477 | 5.563 |
| Tagged pin attempts | 381 | 385 | 4 | 0.015 |
| Tagged binding attempts | 0 | 0 | 0 | 0 (untagged path; see above) |

The UUID attempts averaged **22.38 per ingest call** across the window, including replay/chunk amplification. The 185 working-state attempts occurred with no seed, fault or capacity fallback, so cache misses cannot explain them. At both endpoints the workload satisfied the card's minimum live-session count, but these counters alone do not establish its full query-rate acceptance.

Server process CPU advanced from **14,567.921875** to **14,729.953125 seconds**: 162.03125 CPU seconds / 265.4837662 wall seconds = **0.6103 logical cores**. This is total server-process CPU, not Postgres CPU or CPU attributable to these queries. Working set was 590,073,856 then 685,330,432 bytes. Sanitized pool flags at both endpoints: pooling=true, noResetOnClose=true, maxAutoPrepare=0, multiplexing=false; driver 9.0.3.0, provider 9.0.4.0.

`SessionStateCommandMetrics.cs:18` counts tagged EF read attempts before execution and includes retries. `SessionStateStore.cs:305`/315 records ingests/committed-row deltas/duplicate batches; these are not interchangeable with SQL INSERT calls. No read-completion latency or individual caller tag was available.

## Uncertainties and required evidence

- No fresh Postgres statement delta, query plan or container CPU sample was captured. Tagged EF attempts include retries and exclude untagged queries; they are not completed SQL calls, full scans or database CPU. Desktop before/after attribution requires matched captures from the desktop owner using `scripts/measure-session-state-cache.ps1` and its existing acceptance criteria; this mirror cannot read the desktop files.
- No process restart or fault was induced. Startup and stale-read guarantees have stored R1 reports and source tests; no fresh restart, independent positive control or whole-card acceptance is claimed.
- Live configuration differs from the R1 owner note: the diagnostics report `noResetOnClose=true`, whereas `docs/session-runtime-invariants.md:111` says resets remain enabled. That setting's activation/provenance was not investigated. Any comparison must record actual flags rather than assume the old note describes deployment.
- The previously identified cascade membership race is still visible at `SessionStateDeletion.cs:57` through line 61, then delete at line 31. `card.ps1 get CARD-0720 -Board Antiphon` confirmed existing follow-up `b1a3f44c-741a-424c-a704-f6e622a39233`, Backlog. This investigation did not reproduce that separate race or create a duplicate.
- Current per-caller contributions, continuous live-session count between samples, open clients and equal-workload baseline are unknown. The current observation establishes residual work, not a passing CP-13 result.

## Stored checkpoint provenance

Essential lines below are copied unedited from task `3678a831-82c8-4409-8019-28947f9f8949`'s stored result. Generated TRX files were not retrieved or revalidated. The later review's post-land claims are separate evidence in the table above.

```text
CHECKPOINT CP-1 commit=ffed154faa39ed9da69623e7dfed54645b96bd2d build=ok filter=/*/*/SessionStateCacheReadTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-3678a831/.antiphon/card0701-checkpoints/CP-1-20260925-163837-c084/run.trx reruns=0
CHECKPOINT CP-2 commit=ffed154faa39ed9da69623e7dfed54645b96bd2d build=ok filter=/*/*/(SessionStateCacheReadTests*)|(SessionStateProjectionTests*)|(SessionStateCommitTests*)|(SessionStateWarmupTests*)|(TranscriptWorkingStateQueryTests*)|(SessionMessageQueueServiceTests*)|(SessionMessageQueueDeliveryVerificationTests*)/* executed=198 passed=198 failed=0 skipped=0 trx=/work/worktrees/task-3678a831/.antiphon/card0701-checkpoints/CP-2-20260925-164111-ec13/run.trx reruns=0
CHECKPOINT CP-3 commit=ffed154faa39ed9da69623e7dfed54645b96bd2d build=reused filter=/*/*/AgentSessionRuntimeTests/(C561_*)|(C698_*)|(Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart) executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-3678a831/.antiphon/card0701-checkpoints/CP-3-20260925-165011-8b69/run.trx reruns=0
CHECKPOINT CP-4 commit=ffed154faa39ed9da69623e7dfed54645b96bd2d build=reused filter=/*/*/*/*[Category=Unit] executed=3112 passed=3112 failed=0 skipped=29 trx=/work/worktrees/task-3678a831/.antiphon/card0701-checkpoints/CP-4-20260925-165127-4d04/run.trx reruns=0
```

## Not done, noted

Possible fix direction, deferred to Plan: reconcile and resume the existing R2/R3 cache/notification plan; retain R1 and CARD-0720's separate ownership rather than commission R1 again.

Verification this turn: zero builds/tests; read-only API observations, stored-row reconstruction and source audit. Only this Markdown report is a tracked deliverable. Generated JSON and observation scripts remain gitignored under `.antiphon/card0701-investigation/`; essential evidence is recorded above.

--- next stage ---
next: plan
handoff: Reconcile CARD-0701's existing R2/R3 plan with active R1 at bb18064b, 35 direct SQL working-state sites and replay UUID reads. Preserve the landed projection and separate CARD-0720 ownership; full desktop acceptance and independent controls remain unverified.
artifact: docs/investigations/2026-10-04-card-0701-investigation.md
