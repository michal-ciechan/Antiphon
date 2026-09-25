# CARD-0701 — committed session state in memory and change-driven consumers

Date: 2026-09-25. Stage: Plan. Next stage: Code, **Round 1 first**.

## Decision and scope

Keep PostgreSQL as the record. Maintain a DI-owned, in-process projection of each active session's **committed** transcript, updated by one serialized ingestion boundary. Read working state from that projection. Publish changes after publication of the new snapshot, first to in-process waiters and then through `IEventBus` to SignalR. Cache immutable runner bindings separately from connection liveness. Preserve every CARD-0679 R6 live/unknown/gone distinction and every transcript-confirmed delivery rule.

Ship three rounds. Round 1 removes the working-state query from the queue and agent UI/API hot paths without changing scheduling or deduplication. Round 2 migrates the remaining state readers and adds binding/recent-identity caches. Round 3 replaces transcript polling waits with change notifications, including UI invalidation. Existing timers that detect silence, expiry, external state or lost events remain. Finishing Round 1 is not full-card acceptance.

The operator's standing authority already covers in-memory caching and event-driven session state; no further design approval is required. This plan changes no runtime code, database setting, production process or test fixture.

## Evidence and limits

Read CARD-0701 and sibling CARD-0699/CARD-0700 through `scripts/card.ps1` on this runner. Source inspected at `316a4975a72c87f2d0fd0221f776a2e3955e81cb`. A live `/api/version` read returned `b0a5829763c13c3a0ad5f35e29ed16d263863563`, with `land-v2`; do not mistake this worktree for the running build. Rates below are **source defaults**, not a dump of deployed configuration. Event/request-dependent rates are identified instead of assigning invented frequencies.

The caller supplied the newer 31-second read-only `pg_stat_statements` delta from `C:\Antiphon\evidence\server-cpu-trace-2026-09-25\investigation.md`. That desktop file was not reread from the Linux runner. It supersedes the older CARD-0698 sparse `pg_stat_activity` observations for current load estimates:

| Shape | Calls / 31 s | Calls/s | Interpretation |
|---|---:|---:|---|
| `DISCARD ALL` | 5,789 | 186.74 | Pool resets; not necessarily extra network round trips |
| Boards sibling lookup | 829 | 26.74 | CARD-0700 |
| Runner binding projection | 432 | 13.94 | `PhoneHomeRunnerDirectory.GetBindingAsync` |
| Completion stamp UPDATE | 216 | 6.97 | CARD-0699 |
| Working-state statement | 180 | 5.81 | 457 ms execution time in the whole window |
| UUID membership probes | 691 | 22.29 | Against 57 insert calls (1.84/s); calls are not row counts |

The brief reports about 270 EF queries/s and 0.7 server CPU cores while idle. Neither the historical 36 scans/s nor the card's older 140 Boards scans/s is a current SQL-call measurement. `pg_stat_statements.total_exec_time` is elapsed execution time, not CPU. CARD-0698's indexes and one-statement working query, and CARD-0696's five-second pending-inventory cache, are prerequisites already in the inspected source. Keep them.

## Caller census and disposition

Census scope: production server reads of `TranscriptEntries`, all `IsWorkingAsync`/`IsWorkingBatchAsync` callers, their timer/event/API owners, all runner transcript RPC callers, client state derivations, and the runner's own transcript readers. Tests, migration definitions and XML/comment references are not callers. `N` means eligible sessions/tasks in that invocation; `U` means open clients. A 5-second sweep is at most 0.2 sweeps/s, often **N times** that many per-session reads. Delays after work make actual rates lower. Classes below are in `server/Application/Services/` unless a path is given.

Disposition: **M** = read small committed metadata from memory; **E** = wake on change; **T** = keep a timer; **D** = retain targeted durable content/history reads. A content reader may be E+D: an event says when to query; it never constitutes a delivery receipt. Round numbers identify when behavior changes.

### Server state readers, transcript consumers and their owners

| Caller(s), entry points | Present trigger/rate | Planned disposition |
|---|---|---|
| `SessionMessageQueueService`: enqueue idle gate, `FlushSessionAsync`, `FlushIfIdleAsync`, failure gate, `BuildQueueDtoAsync`, overlay and local-command idle gates | Per enqueue/flush/recovery/request; queue DTO also feeds each 3 s working badge | R1 M; existing queue serialization and live/unknown gates stay |
| Same service: `FlushStrandedQueuesAsync` | `SessionHealthHostedService`, normally 60 s; min of enabled RC probe settings, floor 1 s | R1 M; R3 E on idle/queue/runner recovery, **T retained** for stranded age and lost wakes |
| Same service: `CaptureTranscriptBaselineAsync`, `HasTerminalCapacityHoldAsync` | Per delivery or capacity gate | R2 M for max sequence/latest prompt; capacity/recovery ledger stays D |
| Same service: `TryFindConfirmingRecordAsync`, `TryFindUnobservableConfirmingRecordAsync`, late-confirm loops | Delivery window: 500 ms default (2/s/attempt), 30 s confirmation; unobservable and post-failure pull cadence max(1 s, poll interval), 20 s late-confirm grace | R3 E+D on committed revision; retain screen/Enter clocks, bounded runner pulls and final pull before absence decisions |
| `AgentService.GetAllAsync`, detail/`IsSessionWorkingAsync`, context fullness | Request; `api/agents.ts` list/detail each 5 s (0.2/s/U) | R1 M working; R2 revision-keyed fullness input projection; R3 E UI |
| `AgentTaskDispatcher.AutoEscalateStalledAsync`, `FailNeverStartedAsync`, `FailDeadSessionTasksAsync`, `TryFailOverdueAsync`, `SettleDeferredReportsAsync`, warm reuse, pool release and idle retirement | Delegation tick 5 s; up to N reads in several sweeps; deferred report rehand throttled 60 s | R2 M metadata; R3 E for report/session changes, T for grace/deadline/stall/retirement. Keep dispatch tick until all non-transcript triggers have owners |
| `TaskDeadlinePolicy.EvaluateAsync`, `LoadLastEntryAsync`, boot/launch clock | Dispatcher 5 s, Attention/API, scheduled Check | R2 M for working/latest-by-sequence/latest-by-time; D for task-specific history; T deadline evaluation |
| `TaskProgressPolicy.EvaluateAsync` | Dispatcher 5 s after stall floor (30 min), Attention, expectation samples; currently reads all session progress rows | R2 M working; R3 cache **input facts by transcript revision**, not time-dependent verdict; T for silence/window aging and file/git progress |
| `AgentTaskReplyService`: answer floor, open-question tool, bind-refusal count, release/liveness, marked-turn extraction, subagent wait and unmarked-waiting check | Turn-end/catch-up and explicit actions; dispatcher deferred-report sweep 5 s, rehand 60 s | R2 M metadata; R3 E+D marked content/questions/tool pairs; keep grace expiry T and durable settlement ownership |
| `DelegateBindRefusalRecovery.ReadHeldDoneReportAsync` | Failed-bind recovery only; no independent timer | D; recovery cannot use a cached working bit as a report |
| `AgentTaskService.LoadListCandidatesAsync` | Task list API, typical task UI 15 s | R2 M working/last transcript; task selection remains durable |
| `AgentTaskPipelineStatusService.LoadLastActivityAsync` | Pipeline API, UI 15 s | R2 M last-activity batch; R3 E invalidation plus age display clock |
| `DelegateCheckProbe.GatherSessionAsync`, transcript tail, deadline/progress helpers | Each Check capture; Check scheduling 5–60 min, plus on-demand checks | R2 M count/working/last-time; D bounded tail; no independent transcript polling to remove |
| `AgentTaskCheckService` + `SpecialistTaskRunner` | Check/diagnose/distill task wait polls task result every 2 s; completion-note grace polls existence every 100 ms for up to 5 s | These are **task/note**, not transcript polls. Keep durable ownership; R3 may consume task/note change wakes, paired with CARD-0699, never a session-only signal |
| `SpecialistRequestService.AdvanceAsync`, `ObserveAttemptAsync`, `.Qualification` idle gates | Hosted sweep 2 s; synchronous request wait up to 1 s; qualification also explicit | R2 M; R3 E+D for new interpreter content/idle qualification, T deadline and durable request recovery |
| `StandingSpecialistSeatService.ReconcileAsync` | Supervisor 10 s, pulls before idle gate | R2 M; keep pre-action pull and T for seat/recovery policy, optionally E on idle |
| `AttentionService`: open tasks, newest ends/report markers, no-prompt details, boot reply, land busy, outlived task; `.Leaks` pool gate | API; detail/summary UI 15 s, summary cache 10 s; also other callers | R2 M metadata; R3 memoize transcript-derived input facts by revision, D report/receipt content; keep age refresh T and ledger reads |
| `ExpectationSnapshotReader.ReadInFlightAsync`/`IsReceivedAsync`, `ExpectationNudgeDeliveryService.FindReceiptAsync` | Requested expectation sampling and nudge action, not an independent TranscriptEntries timer | R2 M metadata via policies; R3 revision wake/cached inputs; D full matching receipts. Sampling clock remains its owner's |
| `BootReplyWatch` helper and `BootReplyWatchdogService` | Supervisor 10 s, dispatcher/Attention/check policy callers | R2 M presence/working where applicable; R3 E to invalidate boot facts, T to detect missing response; fresh pull before action |
| `QueuedInputWatchdogService` | Supervisor 60 s | R2 M working; R3 version-keyed enqueue/prompt facts; T silence, D evidence before incident |
| `ContextCompactionService` | Supervisor 60 s, plus explicit action; timeout checks in sweep | R2 M working; R3 E on usage/idle and D boundary confirmation; T timeout, live-only probes |
| `SessionContextUsage.LoadFullnessAsync` | Agent list/detail and compaction reads; effectively 5 s/UI plus 60 s compaction | R2 bounded memoized projection keyed by session revision, provider/accounting and settings version. Preserve provider-specific timestamp/sequence and clear/compact invalidation |
| `PolicyRefreshService.EvaluateGatesAsync`, `HasBeenIdleLongEnoughAsync` | Supervisor 60 s, explicit refresh | R2 M working/activity stamps; T external file drift, idle floor and cooldown; pull still required before relaunch |
| `HerdrStatusCorroborationService` | Supervisor 60 s; may query before and after pull | R2 M; T for sustained disagreement; Herdr never overrides transcript state |
| `RemoteControlRecoveryService`, `Infrastructure/Supervision/SessionHealthActions` | Health normally 60 s, boot/maintenance and explicit actions | R2 M gate; T screen/menu probes and live-only safety; no cached menu/armed authority |
| `SubscriptionUsageMonitorService` | 30 min default; minimum per-session poll interval 25 min | R2 M gate, T external quota command; skip unknown remote sessions |
| `ScheduleService.FireCoreAsync` | Schedule fire; sweep 5 s by default, no independent transcript timer | R2 M idle gate; T due dates. Live-or-unknown behavior unchanged |
| `CheckCompactionContinuationService`: scope, facts, modern/legacy receipts | Dispatcher recovery/check paths at 5 s; discovery on Check policy | R2 M working; R3 E+D on facts/receipt change, T explicit 10 min continuation silence. Preserve sole CARD-0079 automatic-stop exception exactly |
| `OrchestratorInvestigationSweepService` | Supervisor 60 s when enabled | R3 version-keyed facts, T sustained window; detection only |
| `ApiErrorRecoveryService` and `ApiErrorStubText` | Supervisor 60 s plus transcript/settlement paths; retry due clocks | R3 E+D discovery/sibling content; T retry/reset due time; fresh pull before retry, live-only |
| `CapacityEvidence` | Capacity transitions and supervisor reconcile (10 s) | R3 revision-keyed content lookup; T admission/expiry; durable capacity ledger remains authoritative |
| `ChannelReplyDispatcher` | Assistant/turn-end and catch-up events; stale correlation sweep 60 s | Already largely E; D turn windows/API-error siblings. R2 M ends/floors where equivalent; T TTL stays |
| `ReviewReplyDispatcher` | Turn-end and pending review reply events | Already E+D; M boundary metadata where equivalent |
| `TranscriptTurnWindow`, `TranscriptPromptSpan`, `RemoteSpillCourier` | Helpers invoked by channel/review/queue/receipt consumers; no own clock | D exact owning prompt/window/body evidence; R3 avoid repeating unchanged queries, never shorten whole-body receipts |
| `AgentSessionService.CaptureBootConfirmBaselineAsync`, `TryLateConfirmBootPromptAsync`, `GetTranscriptAsync`, restart boundary | Launch/late-confirm/API/resume; transcript API pulls runner and selects durable rows per request | R1 route synthetic boundary through ingestion; R2 M floor; D content API. Keep explicit best-effort sync contract and mandatory pulls |
| `AgentSessionRuntime.PersistTranscriptAsync`, per-row fallback, `IsUnseenTurnBoundaryAsync` | Every live transcript event/catch-up batch; duplicate replay can have zero inserts but multiple queries | R1 serialized commit publication; R2 M positive identities/max sequence; D bounded misses. New state events describe actual persisted rows |
| `AgentFilesService.GetEditedPathsAsync`, `GetAgentActivityAsync` | Files/review API, on-demand; no autonomous DB poll | D file/tool history, optionally revision-keyed bounded results; not replaced by working bool |
| `DiagnosticsBundleService.BuildAsync` | Explicit diagnostics download | R2 M working; D exported history |
| `DelegationUsageRollup` / `DelegationCostBackfillService` | Settlement and one-time startup backfill | D accounting history; no steady-state transcript-state polling |
| `DataRetentionService.PruneTranscriptsAsync` | Retention sweep every 6 h | Keep maintenance T+D; invalidate/reseed affected summaries **after deletion commits**, including count/maxima/recent UUIDs |
| `AgentTaskLandNotificationService.ReconcileAsync` | Note work plus 5 s recovery scanner; pulls and reads matching prompts per unconfirmed note | R3 E+D on recipient transcript/queue change; T delivery retry and recovery, target outstanding notes only |
| `AgentTaskLandMonitorService` | `LandSweepSeconds=5`; reads land requests/notes and their age | **No transcript read.** Keep T for age; CARD-0701 must not replace it with transcript events |
| `CompletionNoteWorkHostedService` / `CompletionNoteStamp` / `HasCompletionNoteAsync` | 1 s scanner plus per-check 100 ms grace | **No working-state derivation.** CARD-0699 removes historical work; events may wake outstanding work but cannot replace durable stamps |
| `CardTaskFileService` / `CardTaskFilePolicy` | Card-file sweep 60 s plus status/API | **No session state.** CARD-0700 owns sibling-board amplification and invalidation |

### Runner, transport and client census

| Caller/path | Present rate | Decision |
|---|---|---|
| `PhoneHomeRunnerDirectory.GetBindingAsync` through `GetOwnerAsync` and `RoutingSessionRunnerClient.Route` | Every routed per-session RPC; measured 13.94 DB reads/s | R2 single-flight binding cache; keep current connection resolution per RPC |
| `PhoneHomeRecoveryPump.OwnerMatchesAsync` | Positive cache per connection; negative 5 s; event-dependent | Reuse R2 binding loader, retain epoch/store owner check; no new liveness authority |
| `PendingRunnerSessionInventory` | Five-second single-flight bootstrap TTL, including empty/failure throttle, before first recovered List | Preserve CARD-0696 implementation in R1/R2. Do not fold this membership cache into the immutable binding cache |
| `PhoneHomeRecoveryPump` | 50 ms in-memory orchestration cycle; catch-up List retries 5 s; inventory refresh 30 s; transcript snapshot once per owned session on catch-up | Keep inventory/lease timers; R3 session change wake after durable ingest, not before. No transcript RPC every 50 ms |
| `SessionRunnerEventPump` | Local SSE; reconnect delay setting (default 1 s); List and transcript catch-up per connection | Keep E, publish through same ingest boundary as phone-home; retain catch-up |
| `AgentSessionRuntime.CatchUpTranscriptAsync` / `SyncTranscriptAsync` | Called by census consumers and exit/recovery; no autonomous poll | Keep fetch-and-persist separate from queue/settlement effects. Coalesce concurrent pulls per session but a pre-action pull must start after that action's evidence floor |
| `RunnerCodexAdapter`, `RunnerGrokAdapter` | Baseline per submit; turn transcript polling 250 ms (4/s/active turn); Codex submission also polls at 250 ms, with a 20 s confirmation budget and 4 s Enter interval by default | Keep in this card: runner-memory RPC/native completion contract, not PostgreSQL state. R2 removes binding DB read beneath it |
| `RunnerClaudeAdapter`/Grok verified submit, `RunnerRawAdapter`/OpenCode, `RunnerTerminalSession` | Screen/output waits: configured 500 ms evidence polling; several 20/25/50/250 ms buffer/settle waits | Keep: screen/output reads, not TranscriptEntries polling; no submit/Enter contract changes |
| `SpecialistTaskRunner`, `ChannelBridgeService`, runtime manual-turn waits | Task result every 2 s; channel session availability every 2 s; manual output first/quiet waits every 100 ms | Task/liveness/output clocks, not transcript SQL loops. E only when the matching task/lifecycle/output event exists; do not conflate with Working |
| `GrokRulesRefreshService.InitializeAsync` / `ReconcileAsync`, `GrokRulesRecoveryHostedService` | Initialize: 250 ms pull/DB check; recovery: 5 s | R2 M floor; R3 E+D rules receipt, retain deadline/final pull and recovery T |
| `AgentSessionService` reattach/RC loops, `RemoteControlRecoveryService` menu loops | Reattach 500 ms; RC output 250 ms; menu/screen 50/100 ms | Keep external-readiness/screen clocks; not transcript SQL loops |
| `SessionReconciliationHostedService`, `WatchdogHostedService`, `RunAttemptStallHostedService` | 15 s, 10 s, 10 s respectively | Keep liveness/silence timers. No inference that cache miss/idle is process death; retain existing policy distinctions |
| `TranscriptTailer`, `CodexTranscriptTailer`, `GrokTranscriptTailer` in runner | File-tail 300 ms; Claude/Codex locate 250 ms | Keep file discovery/tailing; these produce events and do not query PostgreSQL |
| `HerdrStatusPushService` in runner | Transcript/output driven, 500 ms debounce; 300 s heartbeat | Keep its file-ordered classification and empty=Unknown behavior; server arrival-ordered semantics intentionally differ |
| `SessionCpuWatchdogService` in runner | 5 s (floor 1 s) reads runner-memory transcript | Keep native CPU policy and `IsProvenIdle`; server cache never authorizes runner kills |
| Runner HTTP `/sessions/{id}/transcript`, `PhoneHomeCommandDispatcher`, `PhoneHomeRuntimeAdapter`, `SessionRunnerRuntime.GetTranscript`, HTTP/phone-home/scoped/routing clients | Request-driven wrappers; no own timer | Retain transport contract; no extra polling introduced |
| `SessionWorkingBadge` | Queue API 3 s per mounted badge | R3 initial snapshot + `SessionStateChanged` invalidation; connected polling removed, disconnected fallback 30 s |
| `api/agents.ts` list/detail | 5 s per active query | R3 state/AgentChanged invalidation; connected safety refresh 60 s for other agent fields, disconnected 30 s |
| `SessionMessageQueue` | Initial HTTP + `SessionQueueChanged`; no timer | Add state invalidation/shared TanStack query while preserving queue events |
| `SessionTranscriptPanel` + `transcriptModel.isWorking` | Initial/reconnect HTTP then `SessionTranscript` events; local derivation on entries change | Already E. Keep history merge; authoritative server state for badge, local projection for loaded-history rendering; no new per-entry REST load |
| `api/attention.ts`, `api/agentTasks.ts` (list/detail/pipeline) | 15 s per active query | R3 coalesced state/task invalidation; retain 15 s aging refresh initially, served from M/version-keyed facts |
| Attention inspection transcript, delegation land-queue evidence | On opening/enabling query; no transcript interval | D initial snapshot; invalidate only relevant session/evidence keys |
| `api/orchestrator.ts`, home/card-thread/review displays | Orchestrator 5 s, home/thread 15 s, review 10/15/60 s by query | Durable workflow/history consumers; timer retention is intentional, not another working-state implementation |

For Code's census check, rerun `rg -n 'TranscriptEntries|IsWorkingAsync|IsWorkingBatchAsync|GetTranscriptAsync|CatchUpTranscriptAsync|SyncTranscriptAsync' server/Application server/Infrastructure server/Api` and the corresponding client/runner search. Account for each **executable** occurrence under one row above. Changes since the inspected SHA must be incorporated, not silently ignored. Source defaults for key clocks live in `DelegationSettings`, `SupervisionSettings`, `PhoneHomeRunnerSettings`, `SessionReconciliationSettings`, `SubscriptionUsageMonitoringSettings`, and the named hosted services.

## Projection and commit protocol

### Data model and ownership

Use a concrete singleton `SessionStateStore` (Application) with immutable `SessionStateSnapshot` values and DI-owned per-session gates. Put the pure fold/classification in Domain or a dependency-free Application helper, SQL warm-up in Infrastructure behind an external-I/O interface such as `ISessionStateLoader`, and registration/host startup in Program. No static mutable cache, retained DbContext, repository wrapper, direct hub call, or service locator through `AppDbContext`.

Proposed snapshot fields:

- `SessionId`, server-process epoch, monotonically increasing local `Revision`, readiness (`Cold`, `Loading`, `Ready`, `Faulted`), and retention/reset epoch.
- Actual persisted `LastSequence`, row count/has-entries, latest entry by sequence (kind, timestamp and CreatedAt), and independently newest effective timestamp. Keep producer timestamp distinct from local commit/activity time; backfill is not fresh model progress.
- Latest actual `TurnEnd`, latest `TurnTitle`, latest `UserPrompt` sequence, and sufficient working-state accumulators below. Store metadata/IDs, not the full transcript or arbitrary prompt bodies.
- Accepted session generation is an identity fence for lifecycle/runner events; the transcript summary itself spans all persisted generations of that conversation. A restart boundary changes its state; resetting a tailer's sequence never clears history.

Bound settings through `IOptions<SessionStateSettings>`: start with 4,096 cached sessions, 2,048 positive `(Uuid, Kind)` identities per session, a global 100,000-identity cap, 512-ID warm-up batches, and 15-minute terminal-entry idle eviction. Active sessions and outstanding waiters are pinned; if capacity is exceeded, emit pressure metrics and use the correct SQL path for unadmitted entries rather than silently losing active evidence. A gate cannot be evicted while held or awaited. Dropping a dedup identity only causes a DB probe. Do not put liveness tombstones in this evictable store.

### Exact working rule

`TranscriptWorkingStateQuery` remains the database oracle/fallback and retains CARD-0698's indexes. Its rule is not “the last record is not TurnEnd”:

1. End rows: `TurnEnd`, `SessionRestartBoundary`, manual `CompactBoundary`, and `UserPrompt` starting exactly `[Request interrupted`. Preserve current ordinal/case/prefix/null behavior.
2. Activity excludes all end rows, TurnTitle, queued/queue-operation records, all CompactBoundary rows, local-command wrappers and synthetic compaction continuation prompts. Unknown kinds and raw slash text retain current behavior.
3. `Eseq` is maximum end sequence; `Etime` is maximum non-null timestamp of **any** end, potentially a different row.
4. With no end, working means any activity. Otherwise, working means **one activity row** with `Sequence > Eseq` and (`Timestamp` null, `Etime` null, or `Timestamp >= Etime`). Empty known transcripts are idle. A failed load is not empty/idle.

A bounded exact accumulator is possible because committed stored sequences increase: after each new end reset the post-end activity accumulator; retain global `Etime=max(old, new timestamp)`. For activity after the current Eseq, keep `HasActivity`, `HasNullTimestampActivity`, and maximum non-null activity timestamp. Test timestamp and sequence against the same post-end population. A new end with an older timestamp must not lower Etime. In-batch fold order is **stored sequence**, not source sequence or timestamp. Detect any non-append mutation and invalidate/reseed instead of applying this append-only fold.

The runner's file-ordered `TranscriptWorkingState` deliberately lacks the timestamp override and treats no evidence as Unknown. Do not replace it with the server fold. CARD-0698's 12 server oracle scenarios, plus client classification tests, pin the server semantics.

### One append boundary, including the existing exception

Today `AgentSessionRuntime.PersistTranscriptAsync` handles local live events, HTTP catch-up and phone-home catch-up. **`AgentSessionService.WriteRestartBoundaryIfInterruptedAsync` writes TranscriptEntries directly.** Route that synthetic append through the same commit boundary in Round 1; otherwise the first resume already invalidates the design.

For each session:

1. Acquire its state/ingest gate; ensure its durable seed exists; assign monotonic stored sequences under this gate. Do not hold the directory's `_gate` over database I/O.
2. Sanitize and perform today's dedup/insert/fallback protocol. Preserve `(session, uuid, kind)` identity, null-UUID sequence fallback, 512-UUID probe chunks, replay/API-call turn-boundary suppression, and C561 persist-stub/skip behavior.
3. Commit. Only committed rows, with their **actual stored** sequences, kind/text classification and timestamps, may enter the fold. On batch failure, each successfully committed fallback row contributes once; a persist stub contributes its stored representation, not the attempted original. A 23505 skip is not evidence that the proposed row/sequence was inserted: recover the actual durable row or invalidate/reseed before publishing. Unknown commit outcome also invalidates/reloads. Do not mark uncertain UUIDs committed.
4. Publish the immutable snapshot/revision while still serialized. State reads coordinate with the same gate: a read racing an in-flight append either linearizes before it or waits and sees the committed snapshot. After ingest returns, every read and event observer sees the new revision. Failed writes cannot advance the snapshot. If publication fails after commit, mark unavailable and rebuild; never serve a knowingly stale Ready entry.
5. Record a coalesced dirty-session wake and release the gate. Only then invoke existing queue/channel/task actions or asynchronous subscribers. Never invoke a subscriber under the state gate. A queue holder may call `CatchUpTranscriptAsync`; that method must not reenter queue delivery. Lock order: existing queue lock may enter state gate, state gate never enters queue lock.

Outer transactions require publication **after outer commit**, never merely `SaveChanges`. Prefer making this append boundary own its persistence scope/transaction, as runtime ingest already does. If a caller requires a shared transaction, use an explicit post-commit callback and hold/invalidate its projection appropriately; no fire-and-forget “eventually coherent” shortcut. Keep current source event formatting/settlement semantics separate from the new committed-state event; do not rename every old live event as durable evidence.

`PersistResult` must distinguish newly committed stored rows from duplicate/source events sufficiently to implement these rules. Do not make a subscriber reconstruct state by querying TranscriptEntries again.

### Warm-up, restart, invalidation and eviction

Start a hosted warm-up before state-dependent hosted loops. Select active (`Starting/Running/Stopping`) session IDs and terminal sessions still referenced by open tasks, outstanding queues/notes or registered waiters. Use one bounded **statement per batch** to return their summary columns with indexed lateral probes/aggregates; do not materialize all historical rows and do not issue one query per consumer/session. A cold historical API request loads that ID on demand. Empty input emits no SQL; a known empty transcript seeds Ready/idle, a missing session remains missing, and load failure propagates/throttles for five seconds rather than caching idle.

Within a batch, acquire gates in a stable ID order, or use captured revisions plus a final compare/retry under each gate. Startup is a barrier for consumers, not an excuse to discard arriving events: ingestion also loads through the same single-flight gate. A delayed seed can never overwrite a newer append. A caller canceling its wait must not poison a shared load; host shutdown cancels it. Load completion, not start time, begins failure/negative throttle windows.

The seed returns last sequence, independent end maxima, post-end activity aggregates and latest metadata from **one statement snapshot**. It may scan an indexed post-end tail for a long unfinished turn; “bounded” means bounded requested IDs/output and indexed session scope, not a false claim of constant work for an arbitrarily long turn. Measure this cold cost separately. No full-history startup scan of every terminal session. No persisted summary table in these rounds: add one only if measured cold-start cost justifies its transactional/migration complexity.

Retention deletes can change counts, maxima, working state and UUID membership. `DataRetentionService` must take the affected session gates around deletion/commit/invalidation, then reseed before returning their state. Session deletion removes both state and binding entries and wakes waiters with a deleted result. Bulk/raw operations bypass EF tracked callbacks; enumerate and wire them explicitly. Direct out-of-process SQL edits require an explicit invalidation/restart operational procedure; this single-server design does not claim multi-writer coherence. Future multiple **server processes** need a durable invalidation/version mechanism before sharing this cache design. Multiple runners already feed the same server boundary.

## Runner binding cache and CARD-0679 R6

Create a separate concrete `SessionRunnerBindingCache` behind the existing directory. Key by session ID; value is exactly `Missing`, `Local`, or immutable `Remote(RunnerId, RunnerStoreId, RunnerCwd)`, plus local version and load status. Positives have no freshness TTL because accepted session binding is persisted before launch and does not change. Missing results have a maximum five-second TTL; a transient DB failure is not Local/Missing. Single-flight concurrent misses; bound memory and use the injected clock.

Creation hooks in `AgentControlService` and both dispatcher session-creation/resume paths publish/invalidate after commit, before launch/routing. Deletion/retention hooks invalidate after commit. A create racing a slow missing lookup wins via version/CAS; a stale load never replaces its binding. Do not cache tracked entities or a `PhoneHomeRunnerClient`/connection. Cache only identity; resolve current socket, lease, recovery eligibility and epoch on **every** routed operation. The existing per-connection OwnerMatches positive cache can delegate to this cache but must still compare runner/store provenance.

Keep CARD-0696 `PendingRunnerSessionInventory` separately in these rounds: its active membership can change even when binding cannot. Do not turn its cached negative into proof of death. If a later round replaces it, the five-second single-flight/failure contracts must be preserved independently.

| Observation | Required R6 result and effect |
|---|---|
| No successful catch-up List in this process | Active rows bound to the accepted runner are Unknown, including list-membership callers; row-aware `RemoteInventoryPending` remains |
| Reconnecting, closed socket, expired lease, recovering, stale inventory | Unknown, for as long as outage lasts; TTL expiry never means Gone |
| Healthy recovered connection with fresh inventory | Live only for confirmed entries; resolve current connection, not a cached adapter |
| Exit/kill or authoritative connected List omits the generation | Gone with existing generation tombstone ordering |
| List started before a later launch ack/exit | Cannot overwrite the later evidence; tombstones survive the connection, no time-window expiry |
| New session generation | New generation can become live; old exit/tombstone cannot close it |
| Terminal DB row with empty bootstrap result | Stays terminal; cache never resurrects it |

Destructive/reconciliation/claim/resume guards continue to use live-or-unknown; delivery retains Pending and retryable 503 while unreachable. Usage, compaction, watchdog answers, API-error retries, local-command probes, expectation nudges and policy relaunch stay **live-only**. A cached Working/Idle value neither grants a live lease nor proves gone. Read-only state endpoints may return last known transcript state together with availability; never silently display “stopped” from cache eviction.

## Change events, waits and UI

Add `SessionStateChanges`, a concrete in-process broadcaster with per-session revision waiting and bounded dirty-session coalescing. `IEventBus` currently only sends to SignalR; do not pretend it is an in-process pub/sub bus. Publish typed `SessionStateChangedEventDto` through `IEventBus`, carrying `sessionId`, optional agent/task/workflow correlation, server epoch, revision, last stored sequence, working state and change flags. No transcript bodies, runner workspace paths or credentials in the event.

Two notification levels avoid moving SQL load to SignalR:

- Every successful append advances an internal revision and wakes registered transcript/receipt waiters, including appends that leave Working unchanged. Consumers needing every row use the durable sequence range since their watermark; a coalesced event is a wake, not a list of every transition.
- Publish UI state on working/lifecycle/availability changes and coalesce progress/usage updates to at most once per second per session. Do not invalidate all agents/attention on every token. A busy subscriber consumes the newest revision and loops if dirtied while running. Queue and settlement handlers remain idempotent and recheck durable ownership; one logical consumer owns each action to avoid old-callback/new-subscriber double dispatch.

Use subscribe/read/wait with a revision recheck: capture current snapshot, register `WaitForChange(sessionId, epoch, revision)`, immediately compare revision, then await change **or the next required deadline/catch-up clock**. No lost wake between read and registration; cancellation unregisters; a restart/eviction/reset epoch forces a fresh snapshot. Events are at-least-once hints, not durable delivery claims. Subscriber failure is isolated, leaves that session dirty, and logs a metric. Bounded queue overflow sets a rescan-needed flag; startup and existing recovery sweeps discover outstanding durable work. Avoid adding a full session/transcript rescan every second as recovery.

Receipt and report readers keep their exact durable match/window queries. While waiting on unchanged transcript revision, do not run them repeatedly. Screen observations, scheduled Enter retry, mandatory runner catch-up and final absence check still have their own clocks. Coalescing a pull is valid only when its request started after the caller's required freshness point. A cached “no receipt” from before typing can never suppress a pull after typing. Preserve whole-body matching, generation/floor/timestamp fences, `CatchUpTranscriptAsync`'s lack of queue effects, and all CARD-0696 immediate-refusal restoration semantics.

For state-derived content facts (context usage, task deadlines/progress, boot/question/report presence), use a bounded revision-keyed **input** cache or an incremental equivalent with oracle tests. Include task dispatch/generation and policy/provider settings in keys. Do not cache `now`-dependent verdicts: silence, TTL, rolling-window expiry, capacity due times and external file/git progress must still change without a transcript event. Large-history input caches need an explicit size cap and measured fallback; never retain unbounded prompt/tool text to avoid a query.

Add a small `GET /api/sessions/{id}/state` initial/reconnect snapshot endpoint, or reuse an existing DTO only if it avoids querying the entire queue just for the badge. REST and events share epoch/revision. The client follows TanStack Query plus `useSignalRInvalidation`; keep any connectivity state in the existing store. `SessionStateChanged` targets `['session', id, 'state']`, the existing `working-badge` key during migration, relevant agent list/detail and already-used task/attention keys with coalescing. Ensure the connection actually joins the session group, or use the authorized metadata broadcast lane used by AgentChanged; a group-only event with no joined subscriber is not coverage.

Subscribe before the initial snapshot, rejoin and fetch on reconnect, ignore older same-epoch events, accept a new server epoch by refetching. A disconnect starts the documented fallback clock. Keep `SessionQueueChanged` for queue content. The transcript panel is already event-driven; do not add polling to it or send raw transcript text in broad invalidation events. UI age labels may tick locally without a REST request.

## Npgsql reset decision and sibling composition

**Leave `No Reset On Close=false` in CARD-0701.** Disabling reset can be safe for a deliberately stateless runtime pool, but the current evidence is a source audit, not a pooled-connection contamination test or measurement of the deployed pool. It does not establish an unconditional production flip.

The inspected runtime SQL uses transaction-scoped `pg_advisory_xact_lock` in capacity admission, delegation gates, source-landing admission, specialist qualification and Grok rules. Queue interruption recovery uses `set_config('statement_timeout', ..., true)` inside its transaction: `true` makes it transaction-local. No runtime session-scoped advisory lock, `SET ROLE`/`search_path`, SQL LISTEN/UNLISTEN, explicit Npgsql prepare call, session temp table or configured auto-prepare was found in the searched source. Migration `20260910014917_AttachUnambiguousLegacyLandEvidence` creates temporary tables `ON COMMIT DROP`. The server references `Npgsql.EntityFrameworkCore.PostgreSQL` `9.*`; qualification must record its **resolved** provider/driver versions and sanitized effective flags, including startup/migration code, connection initializers, test hooks and extensions. Never print the connection string.

Npgsql normally buffers reset commands until the next use, so 5,789 DISCARD calls are not proof of 5,789 extra round trips. Prepared statements managed by Npgsql already survive pooling: reset uses equivalent commands when necessary, rather than discarding those statements. Turning on auto-prepare merely to hide DISCARD counts is not this fix. See [Npgsql performance](https://www.npgsql.org/doc/performance.html) and [connection parameters](https://www.npgsql.org/doc/connection-string-parameters). PostgreSQL DISCARD also clears session configuration, listeners and session advisory locks; removing it changes those isolation guarantees ([PostgreSQL 16 DISCARD](https://www.postgresql.org/docs/16/sql-discard.html), [advisory-lock functions](https://www.postgresql.org/docs/16/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS)).

If separately commissioned, qualify a **separate runtime-only pool** with max size one under success, cancellation, exception, commit and rollback: transaction-local timeout must revert, xact locks release to another backend, no role/search_path/listener/temp leakage, prepared command reuse remains valid, and deliberately injected session state demonstrates the negative control. Startup migrations/administrative stateful commands must stay on a reset-enabled pool. Benchmark both settings with equal useful work and record server CPU, useful SQL rate and reset count; reset count alone is not benefit. Only then opt in using a typed setting, default false, with explicit rollback. This optional pool experiment is not needed for session-cache acceptance and is not commissioned by this plan's Code checkpoints.

Composition is additive:

- **CARD-0698:** keep indexes, bounded 512-UUID misses, database oracle and query-plan protections. Cache misses/restart/backfill still need efficient SQL.
- **CARD-0696:** keep pending inventory and durable mention/refusal contracts. Cached state does not change queue acceptance or delivery evidence.
- **CARD-0699:** its scanner must select bounded **outstanding** work and retire stamped completions; do not cache an all-history scan to hide it. Existing completion stamp/notification uniqueness remains durable. A note event may wake its work queue; session events only trigger receipt re-evaluation for outstanding notes. Do not edit `CompletionNoteStamp` in this card.
- **CARD-0700:** build/reuse one sibling-board snapshot per operation/sweep and invalidate on relevant board/project/privacy writes in that card. Preserve opt-out cleanup visibility. The session store must not become a general Boards cache. Do not edit card-file policy in this card.

Record each sibling's deployed SHA during acceptance. Attribute working/binding/UUID changes to 0701; stamp UPDATE and Boards improvements to their own cards. With resets enabled, fewer logical database operations should also reduce resets; do not promise zero DISCARD or total 270/s elimination from this card alone.

## Implementation rounds

### Round 1 — smallest usable projection (S0–S3)

S0: Add two baseline-red query-count tests against current production entry points, with existing signatures and a real isolated PostgreSQL fixture: repeat warmed queue DTO reads; repeat warmed agent working-state reads. Count only the production working-state SQL/TranscriptEntries derivation, not unrelated queue/agent reads. They must compile before the cache exists and fail on the repeated reads, not setup. Add behavioral/race tests alongside implementation.

S1: Introduce immutable state/fold, bounded single-flight loader, per-session ingest serialization and read barrier. Fold actual committed rows and recover failed/partial writes. Route restart boundaries through ingestion. Wire retention/deletion invalidation for the summary before enabling it. No UUID fast path yet; today's dedup SQL remains.

S2: Warm active sessions and use the store for queue working-state gates/DTO and AgentService working-state list/detail. Keep the old static SQL helpers for explicitly unmigrated callers and as an oracle; never resolve the singleton from an EF context. Existing event/queue actions continue after the updated snapshot. R1 does not replace process liveness or user prompt matching.

S3: Add metrics/query tags, controlled restart evidence and the R1 measurement collector. Update the runtime owner doc for the new commit boundary. Execute CP-1–CP-4. Review/land/activate this slice before expanding if the caller commissions rounds separately. Its report names residual working-state queries from unmigrated callers.

### Round 2 — all metadata readers, bindings and replay cost (S4–S6)

S4: Move every remaining census `IsWorking*` caller to the concrete store/reader, including static policy helpers by explicit parameter. Keep legacy DB helper only in oracle/fallback tests. Batch state loads; move eligible max/count/last/prompt metadata reads. Memoize context-usage inputs by revision with provider/settings invalidation.

S5: Add the runner-binding single-flight cache with post-commit creation/deletion hooks, stale-load rejection and five-second negatives. Preserve directory connection/tombstone/pending-inventory code. Test two routed calls with one lookup, restart, negative-create race and reconnect to a new epoch.

S6: Add bounded **positive-only** recent UUID/kind and recent TurnEnd API-call identity sets. A hit skips its membership probe; a miss probes PostgreSQL in the existing bounded chunks, never assumes novel. Store max sequence from the state snapshot under the ingest gate. Failed/rolled-back/skipped/ambiguous writes do not populate identities. Null-UUID sequence fallback remains exactly as today, with no new identity reinterpretation. Add successful baseline-red and green evidence CP-5–CP-8; run isolated query-budget checks before R3.

### Round 3 — event waits and incremental consumers (S7–S9)

S7: Implement revision waiters/broadcaster and coalesced worker scheduling. Convert receipt waits, Grok initialization, land-note receipt recovery and deferred-report discovery to change wakes plus their necessary deadline/pull timers. Migrate transcript-derived policy input caches to revision-aware reads. Preserve existing lost-event recovery and idempotent settlement; no full dispatch tick on every transcript token.

S8: Add typed SignalR state event/snapshot endpoint and client invalidation/subscription/reconnect handling. Remove working-badge 3 s connected poll; adjust agent polling as specified. Keep attention/task age clocks. Extend existing client tests and add event ordering/reconnect tests.

S9: Execute CP-9–CP-12 and final before/after desktop acceptance. Update operational documentation with metrics, supported single-server scope, invalidation and rollback. Full CARD-0701 acceptance requires all three rounds, ordinary Review, publication and matching activated server SHA.

## Rejected alternatives

- Five-second TTL working-state cache: stale idle can type into a working turn; stale working strands WhenIdle. TTL remains appropriate only for the documented bootstrap/negative inventory knowledge.
- “Last entry kind” or max(sequence)/max(timestamp) across unrelated activities: changes replay/manual-compaction/null-timestamp semantics.
- Update memory before commit, or asynchronously after returning ingest: creates state that the database cannot prove or a stale-reader race.
- A boolean combining working and alive: loses R6 Unknown and accepted-generation safety.
- Cache all transcript text/history or all UUIDs forever: unbounded memory and duplicate accounting; use small metadata, capped positive identity sets and bounded durable misses.
- Put a persistent summary row on every append now: extra writes/migration/crash protocol before warm-up cost has been measured.
- Remove every timer: silence, lease expiry, due work, external screen/file state and recovery still need clocks.
- LISTEN/NOTIFY/Redis for the current single server: unnecessary distributed failure modes. Revisit durable cross-process invalidation before adding another server writer.
- Globally disable Npgsql resets, turn on auto-prepare, or grow the pool as a proxy for fixing repeated reads: mixes an isolation/performance experiment into cache correctness and hides the source of load.

## Verification design

No tests/builds run during Plan. Code uses isolated PostgreSQL and fake runner clients; a host booting Program must use the established isolated-runner guard, never production 17204. No need to run provider TUIs or Pty assemblies for this server-only design; if Code changes native delivery/transport scope it must amend this plan and state the added verification reason. Process-spawning tests take the assembly's `ParallelLimiter<ProcessSpawnLimit>`.

### Red tests and coverage roster

New classes listed here are **to be authored**, not claims that they exist. Use real production paths and deterministic barriers/TimeProvider; no sleeps, self-comparisons, test-owned cache substituting for the production store, or mocks returning the expected verdict.

| ID | Executed class / cases | Red condition and evidence |
|---|---|---|
| V-1 | `SessionStateCacheReadTests` (2 baseline methods, retained green) | `Queue_reads_after_warmup_do_not_query_working_state`, `Agent_reads_after_warmup_do_not_query_working_state`: baseline produces repeated working SQL; new code zero after seed |
| V-2 | `SessionStateProjectionTests` (at least 12 methods) | Table-driven parity with independent CARD-0698 oracle after every append and batch/restart seed; independent end maxima, stale replay, equality/nulls, manual/auto compact, local commands, housekeeping, unknown kinds, empty/missing/duplicate requested IDs |
| V-3 | `SessionStateCommitTests` (at least 8) | Commit barrier, no stale observer/read after ingest, failed save no advance, fallback partial success, stored stub classification, actual duplicate row recovery, ambiguous outcome reload, synthetic restart boundary. Removing post-commit order or routing must fail |
| V-4 | `SessionStateWarmupTests` (at least 6) | New DI container on same DB, concurrent cold readers one query, ingest-vs-delayed seed, cancellation/failure retry, retention/delete reseed, eviction/active waiter safety. Fresh process container cannot inherit test static state |
| V-5 | `SessionStateConsumerTests` (at least 6) | Queue skip/flush parity, manual compact does not settle task, max-sequence/last-time mapping, context usage parity, no repeated metadata SQL across remaining census consumers, settings/generation invalidation. A cached idle must never make an unknown session deliver |
| V-6 | `SessionRunnerBindingCacheTests` (at least 6) | `Repeated_binding_reads_use_one_projection` baseline-red; concurrent single-flight, missing-then-create race, delete/rollback/failure, current-connection reroute, immutable owner/store provenance |
| V-7 | `SessionTranscriptIdentityCacheTests` (at least 6) | `Duplicate_replay_does_not_repeat_uuid_probes` baseline-red; same UUID different kinds, eviction DB fallback, partial-failure retry, null UUID parity, live/catch-up race with monotonic stored sequence |
| V-8 | `SessionStateChangeTests` (at least 8) | Publish-after-commit, unchanged Working still wakes receipt, registration race, duplicate/coalesced events, overflow recovery, throwing subscriber isolation, canceled waiter eviction, epoch/reset and backlog gap recovery |
| V-9 | `SessionStateWaitTests` (at least 6) | `Unchanged_confirmation_wait_does_not_repeat_transcript_reads` baseline-red; confirming whole UserPrompt wakes once, prefix/QueuedUserPrompt cannot confirm, final pull after missed stream, pull/queue lock does not deadlock, timer-only silence still fires |
| V-10 | `SessionStateDerivedFactsTests` (at least 4) | Revision unchanged skips transcript SQL while advancing time still changes deadline/stall/TTL; file progress, task/settings changes and retention invalidate the right input facts; full content evidence preserved |
| V-11 | Client `useSignalRInvalidation.test.ts`, new `SessionWorkingBadge.test.tsx`, existing `SessionTranscriptPanel.test.tsx` | Connected idle→working→idle without 3 s fetch; reconnect/new epoch gets snapshot; stale revision ignored; disconnected fallback; coalescing doesn't issue one REST call/token; queue events still update |
| V-12 | Live measurement protocol below | Repeatable read-only query deltas + process CPU + active/session workload counts, no fabricated after sample |
| R-1 | `TranscriptWorkingStateQueryTests` (existing 12) | Oracle remains exact and usable on misses; retain 0698 indexes |
| R-2 | `AgentSessionRuntimeTests` C561/C698 and sequence restart (existing 15 selected executions) | Sanitization/stub/skip recovery, replay and sequence restart preserved; exclude Windows-only cmd restart test from Linux filter |
| R-3 | `SessionMessageQueueServiceTests`, `SessionMessageQueueDeliveryVerificationTests` | Existing working/delivery evidence and compaction behavior, whole receipt contract |
| R-4 | `PhoneHomeDirectoryTests`, `PhoneHomePendingInventoryTests`, `PhoneHomeStrandedQueueTests`, `PhoneHomeImmediateSendTests`, `PhoneHomeSessionRoutingTests`, `PhoneHomeEventPumpTests` | R6 Unknown/Gone matrix, inventory races, pending retention, 503 immediate-refusal restoration and current-epoch routing |
| R-5 | Unit lane | Cross-cutting DI/DTO/classification and existing policy contracts; no affected contract skipped |

Baseline-red classes must use current entry points and compile against baseline production. CP-1 expects exactly two assertion failures; CP-5 exactly two; CP-9 exactly one. A fixture/compile error or missing test is not red evidence. Other new race/behavior tests require a named production defect/positive-control removal that makes them fail; record those mappings. Later Mutation executes them; Code need not rerun broad mutation loops. Floors below count TUnit executions, not assertions or scenario loop iterations.

### Read-only before/after acceptance measurement

Code adds `scripts/measure-session-state-cache.ps1`, ASCII-only, with modes `Capture`, `Compare`, and `Acceptance`. It must write local evidence and run SELECT-only statistics reads against the identified database; no reset, extension installation, EXPLAIN ANALYZE, VACUUM, settings change, session stop or synthetic production prompt. Use the desktop Docker/psql path in `docs/logs.md`, local approved authentication, and never print credentials or query parameters/transcript text. Fetching a version/health endpoint is observational, not activation authority.

1. Record UTC times, `/api/version`, canonical checkout SHA (all git commands timeout-bounded), resolved driver versions/sanitized flags, sibling deployment SHAs, Postgres/server process identity, number of live/unknown sessions (require at least 10 live for final acceptance), open clients, pending queues/tasks, and ingest **calls and rows** separately. Capture the same idle and natural-activity workload before/after. No automatic test traffic to the production runner.
2. Warm for 60 s, then capture two 180-second windows at baseline and after each activated round. Collector waits must expose progress at least every 30 s. Never reset shared stats. Read `pg_stat_statements` deltas keyed `(dbid, userid, queryid, toplevel)` plus `pg_stat_statements_info.stats_reset`/dealloc and elapsed time. A reset/entry eviction/negative delta/process restart within a window invalidates it. Preserve raw snapshots, sanitized normalized shape/hash map and comparison summary.
3. Tag production loader/fallback/binding/identity commands and record EF counters for those paths. Retain normalized SQL fingerprint attribution for older baseline commands. Measure all TranscriptEntries SELECTs, actual inserts/rows, working-state SQL, binding SELECT, UUID probes, Boards sibling SELECT, completion stamp UPDATE and reset statements separately. Do not sum table seq-scan counters as SQL calls.
4. Record server CPU from process CPU-time delta / wall seconds (cores), and PostgreSQL container CPU on the same windows, plus memory/GC, cache hits/misses/load faults/evictions, duplicate replay count, internal event backlog and handler latency. No “total_exec_time == CPU” substitution.
5. **R1 gate:** after warm-up, controlled queue/agent hot-loop tests produce zero working-state SQL; live attributed reads for those paths are zero except declared first activation/failure/retention loads. Overall working queries decrease; quantify the residual callers. No behavioral failure, stale read, duplicate queue send or warm-up failure.
6. **Full R3 gate:** ready active sessions perform zero timer/API-driven working-state derivations and zero repeated binding projections; repeated positive-identity replay hits perform zero UUID membership probes. In the controlled 10-session, 180-second no-ingest case, state/receipt polling contributes **zero** TranscriptEntries SELECTs after warm-up. In production, working/binding read rates fall at least 95% from the matched current baseline (floors are for genuinely rare cold loads, not a TTL refresh); enumerate each residual reason. Receipt/content work is triggered by revisions, explicit history requests or mandatory recovery pulls, not N consumers × timer frequency.
7. In the activity window, publish `TranscriptEntries SELECT calls / accepted ingest batches`, cold/recovery/content counts and duplicate probes/hit rate; compare 10-session and 20-session controlled replay with the **same ingest count** to show reads do not grow with idle consumers. UUID misses and explicit content exports may still query; no claim that every novel insert needs zero reads. Set a controlled budget of <= one summary load per new session plus one probe per 512 uncached UUIDs (and the documented null-sequence/boundary misses), zero extra reads from warmed metadata consumers. Assert budgets in integration tests, not fragile production timing.
8. Report measured before/after CPU for both processes. Acceptance requires lower median server CPU across the matched pair of windows, no PostgreSQL CPU regression beyond sample variation, and the query gates above. If activity differs or noise makes the CPU result inconclusive, collect a third matched window and report it; do not claim CPU reduction from fewer calls alone. No fixed 0.7→0.1-core promise.

Production capture/activation requires the desktop lane. A Linux-only Code round records its controlled query budgets and leaves V-12 explicitly pending for the caller's activation/acceptance task; it cannot mark this card accepted. Before a meaningful after capture, the caller activates from the canonical checkout under `docs/apphost-runbook.md` and confirms `/api/version` equals the expected server SHA. A push/land or healthy endpoint alone is insufficient.

### Cost

Ordinary R1 checkpoint floor: **36 min** (8+14+5+9); authoring estimate 90–150 min. R2 floor: **40 min** (8+18+5+9), authoring 90–150 min. R3 floor: **36 min** (8+16+9+3), authoring 120–180 min. Full ordinary checkpoint floor **112 min**, plus **20 min** desktop acceptance excluding activation waits. Dispatch `-ExpectAbout` per commissioned round adds its authoring estimate; these are estimates, not timeouts or permission gates. First round deliberately excludes binding, dedup and scheduling changes so its correctness and query savings can be reviewed independently.


### Checkpoints

This is the closed build/test list. **Initial Code dispatch commissions R1 only: CP-1–CP-4.** R2 commissions CP-5–CP-8; R3 commissions CP-9–CP-12. CP-13 is mandatory full desktop acceptance after activation, a separate deployment/acceptance obligation when Code runs on server2. Do not mark uncommissioned rounds completed or run them incidentally. A later dispatch must name the selected round and this table at the committed plan SHA. Every commissioned row is required; any unlisted run needs a stated reason.

Use `scripts/run-checkpoint.ps1` with the exact filter, `-MinExecuted`, fresh results directory under `.antiphon/card0701-checkpoints`, and comma-separated `-Expect` class names. It supplies `UseAppHost=false` on Linux. `Build=CP-n` uses `-NoBuild` and the same output; no intervening source change. Table `\|` escapes are bare `|` in the argument. Report CP-n commit/build/filter/executed/passed/failed/skipped/TRX and reruns. New/affected tests must not skip. Unit platform skips must be named. No `dotnet test`, namespace-wide integration sweep or parallel Antiphon/Pty assemblies.

The new measurement script's Acceptance mode consumes immutable saved baseline windows and captures/compares the current after windows; it must fail if the baseline is missing/incompatible, not silently call the changed build “before”. Round 1's controlled query budgets live in V-1/V-4; live R1 captures are useful deployment evidence but not an unlisted build/test. CP-13 evidence records the capture commands, environment and all comparison gates.

| CP | After | Build | Group | Filter / exact non-TUnit command | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0, baseline production | `tests/Antiphon.Tests -> bin-c701-r1-red/` | r1-red | `/*/*/SessionStateCacheReadTests/*` | V-1 red | exactly 2 executed, 2 designed assertion failures, 0 skipped | 2 | 8 |
| CP-2 | S1–S3 | `tests/Antiphon.Tests -> bin-c701-r1/` | r1-state | `/*/*/(SessionStateCacheReadTests*)\|(SessionStateProjectionTests*)\|(SessionStateCommitTests*)\|(SessionStateWarmupTests*)\|(TranscriptWorkingStateQueryTests*)\|(SessionMessageQueueServiceTests*)\|(SessionMessageQueueDeliveryVerificationTests*)/*` | V-1–V-4, R-1/R-3 | every listed class; >= 2+12+8+6 new and existing oracle/delivery tests; 0 failed, 0 affected skips | 40 | 14 |
| CP-3 | S1–S3 | `CP-2` | r1-persist | `/*/*/AgentSessionRuntimeTests/(C561_*)\|(C698_*)\|(Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart)` | R-2 | existing 15 selected executions; 0 failed/skipped | 15 | 5 |
| CP-4 | S1–S3 | `CP-2` | r1-unit | `/*/*/*/*[Category=Unit]` | R-5 | all Unit selected, 0 failed; unrelated platform skips named | 1 | 9 |
| CP-5 | S4 test scaffolds on R1 production | `tests/Antiphon.Tests -> bin-c701-r2-red/` | r2-red | `/*/*/(SessionRunnerBindingCacheTests)\|(SessionTranscriptIdentityCacheTests)/(Repeated_binding_reads_use_one_projection)\|(Duplicate_replay_does_not_repeat_uuid_probes)` | V-6/V-7 red | exactly 2 executed, 2 designed assertion failures, 0 skipped | 2 | 8 |
| CP-6 | S4–S6 | `tests/Antiphon.Tests -> bin-c701-r2/` | r2-readers | `/*/*/(SessionStateConsumerTests*)\|(SessionRunnerBindingCacheTests*)\|(SessionTranscriptIdentityCacheTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeStrandedQueueTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeEventPumpTests*)\|(SessionStateCacheReadTests*)\|(SessionStateProjectionTests*)\|(SessionStateCommitTests*)\|(SessionStateWarmupTests*)/*` | V-1–V-7, R-4 | every listed class, >=18 new R2 methods, all prior projection tests; 0 failed/skipped | 18 | 18 |
| CP-7 | S4–S6 | `CP-6` | r2-persist | `/*/*/AgentSessionRuntimeTests/(C561_*)\|(C698_*)\|(Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart)` | R-2, V-7 | existing 15 selected executions; update only query-count expectations that deliberately change, preserve behavioral assertions; 0 failed/skipped | 15 | 5 |
| CP-8 | S4–S6 | `CP-6` | r2-unit | `/*/*/*/*[Category=Unit]` | R-5 | all Unit selected, 0 failed; unrelated platform skips named | 1 | 9 |
| CP-9 | S7 test scaffolds on R2 production | `tests/Antiphon.Tests -> bin-c701-r3-red/` | r3-red | `/*/*/SessionStateWaitTests/Unchanged_confirmation_wait_does_not_repeat_transcript_reads` | V-9 red | exactly 1 executed, 1 designed assertion failure, 0 skipped | 1 | 8 |
| CP-10 | S7–S9 | `tests/Antiphon.Tests -> bin-c701-r3/` | r3-events | `/*/*/(SessionStateChangeTests*)\|(SessionStateWaitTests*)\|(SessionStateDerivedFactsTests*)\|(SessionStateConsumerTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeStrandedQueueTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeEventPumpTests*)/*` | V-5/V-8–V-10, R-3/R-4 | every listed class, >=18 new R3 methods; 0 failed/skipped | 18 | 16 |
| CP-11 | S7–S9 | `CP-10` | r3-unit | `/*/*/*/*[Category=Unit]` | R-5 | all Unit selected, 0 failed; unrelated platform skips named | 1 | 9 |
| CP-12 | S7–S9 | n/a | r3-client | `pwsh -NoProfile -File scripts/test-client.ps1 useSignalRInvalidation.test SessionWorkingBadge.test SessionTranscriptPanel.test` | V-11 | all 3 files executed, nonzero tests in each, 0 failed/skipped; wrapper exit 0 | n/a | 3 |
| CP-13 | Activated R3, matched baseline captured before activation | n/a | desktop-acceptance | `pwsh -NoProfile -File scripts/measure-session-state-cache.ps1 -Mode Acceptance -PlanCard CARD-0701 -EvidenceRoot C:\Antiphon\evidence\card-0701-session-state` | V-12 | valid matched before/after pg_stat_statements windows, >=10 live sessions, query gates and CPU comparison satisfied; otherwise explicitly pending/failed, never inferred | n/a | 20 |




## Rollback and completion evidence

A typed `SessionState:Enabled` rollout switch selects cache-backed reads versus the existing indexed SQL reader. Keep ingestion serialization/restart-boundary fixes and database writes in both modes. Event-driven consumers must have a documented fallback timer mode when state events are disabled. Switches are read at process startup: rollback activates the previous build or disables the feature and restarts through the canonical runbook, then verifies the served SHA and captures a short diagnostic window. Never clear durable transcript, queue, completion stamp or runner tombstone data to roll back a cache.

Final implementation report gives per-round commit, CP counts/failures, remaining commissioned work, activated server SHA, evidence paths, query/CPU deltas, sibling deployment identities and any unresolved cold-load/memory limits. A correct low-query test is necessary; full card completion additionally requires actual deployment measurement. Plan deliverable is ready for Round 1 Code without another approval request.
