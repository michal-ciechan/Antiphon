# CARD-0432: distiller availability, ingestion and deadlines

Plan, 2026-09-07. Based on investigation commit `6a3d5865` and the code at that commit. Next stage: TestDesign. This deliverable changes documentation only.

Integration note: `cda42403` landed the independent distiller contract-v2 character-budget change while this plan was being finalized. This plan is rebased on it. That change touches the bundle, contract version and provisioner tests, not the timeout/ingestion paths below. Preserve it and partition subsequent measurements by actual `BundleStamp`; the investigation's contract-v1 rejection rate is historical, not a prediction for v2.

## Outcome and scope

Keep `OutputDistillerMode=Shadow` and `OutputDistillerWaitSeconds=45`. Fast-degrade a known model hold, stop slow runtime side effects from blocking transcript ingestion, and carry one absolute deadline from completion-note admission through application. Preserve the raw report and standing-seat ownership on every degradation. Make late task outcome and cost visible without applying a late answer.

The investigation is [2026-09-07-card-0432-distiller-timeouts.md](../../investigations/2026-09-07-card-0432-distiller-timeouts.md). Its 140-row sample has 38 timeouts: 19 never dispatched under a model hold, 17 eventually succeeded, and two failed delivery/correlation. Of the 17, twelve produced their marked report within 45 seconds of task creation; five incurred substantial queue delay before generation. Across 90 eventual successes, prompt-to-report was median 16.752 seconds, maximum 30.634 seconds. Report-to-`CompletedAt` reached 80.084 seconds. These are producer timestamps compared with settlement-entry timestamps, not measurements of database commit latency.

Compression-gate tuning is CARD-0430. Do not edit the distiller bundle, gates, provider choice, quotas, seat count, or enable Apply. Apply behavior must nevertheless be corrected and tested using isolated fixtures. No production restarts, model turns, broker sends, or live configuration changes are part of Code verification.

## Ground truth

| Assumption or desired behavior | Code/evidence at the planning baseline | Consequence |
|---|---|---|
| A timeout means slow Haiku generation. | `SpecialistTaskRunner.WaitForRunAsync` polls durable task state. Availability, dispatch, ingestion and settlement all precede success. | Keep separate reasons and phase clocks; a timeout-only increase is rejected. |
| Unavailable models are detected before specialist work is created. | `AgentTaskDispatcher` checks `ModelAvailability`; `SpecialistTaskRunner` does not. `DiagnoseService` already uses `IModelAvailability` for preflight. | Use the existing hold authority before creating Distill work and recheck while queued. |
| There is a single 45-second clock. | `AgentTaskReplyService.DeliverToParentAsync` sets Apply `HoldUntil` before queue enqueue; the specialist deadline starts after provisioning and task creation. | The request must carry its original absolute deadline; no downstream clock reset. |
| The current backlog setting bounds pending requests. | `OutputDistillationQueue` is unbounded and serial. `OutputDistillerMaxBacklog=3` counts unfinished Distill task rows, not channel items. | Bound request admission separately from unfinished model work. |
| `WaitMs` includes the whole request. | It excludes channel residence and includes specialist provisioning and timeout incident/alert handling. | Preserve the legacy field and add explicit phase timestamps/durations. |
| An expired but undelivered note cannot be replaced. | `TryApplyAsync` checks Pending and zero attempts, but no deadline; its read/save is outside the queue's session lock. | Deadline and delivery-claim checks must occur together under the queue's existing lock. |
| The fleet event pump only ingests events. | `SessionRunnerEventPump` awaits `ObserveTranscriptAsync`, which awaits channel/review/task routing, compaction and queue flushing. Startup/reconnect `SyncTranscriptAsync` also performs side effects serially across sessions. | Slow work must leave both live ingestion and reconnect catch-up paths. |
| Only the final queue flush can block ingestion. | `FlushQueueOnIdleAsync` calls task settlement first; `DeliverToParentAsync` awaits `SessionMessageQueueService.EnqueueAsync`, whose default idle path calls delivery verification inline. `ObserveTranscriptAsync` also awaits SignalR before persistence. | Instrument each awaited child, including nested parent-note delivery and publication; do not fix only the obvious final flush. |
| Persisted transcript order makes arbitrary background execution safe. | Persistence rebases sequences and deduplicates `(uuid, kind)`; current side effects mostly select the latest turn. Catch-up, streamed replay, split TurnEnd/text and answered-Blocked watermarks have specific guards. | Separate ingestion from effects with explicit sequence identity, ownership and replay recovery, not untracked `Task.Run`. |
| Timed-out work costs zero. | `SpecialistTaskRunner.Finish` returns zero/null on timeout, but dispatched tasks can later settle with cost. Ledger stats sum the captured zero. | Preserve the original timeout verdict while resolving eventual status/cost from the linked task. |

Relevant owners: [project-context](../../project-context.md), [orchestration-loop](../../orchestration-loop.md), [session-runtime-invariants](../../session-runtime-invariants.md), and [testing-and-build](../../testing-and-build.md). Code must reread the owners for any additional area it changes.

## Decisions

### D-1. Keep the current operating mode and budget

Shadow and 45 seconds remain the defaults and live settings throughout this work. The sample supports fixing wasted waiting before buying more of it. A larger serial wait can worsen burst latency; all 73 promptly completed attempts were rejected by compression gates anyway. A 75-90 second Shadow trial is a possible later operator decision only after phase measurements, not a change in this card.

### D-2. A model hold is an immediate, distinct fallback

Add a held result to the specialist runner and append `DegradedHeld` to `DistillationOutcome` without renumbering existing values. Record a structured reason, canonical kind/alias and observation time. A known hold creates no Distill task, invokes no model and leaves/releases the raw note immediately; it must not raise a fresh generic unavailable alert for every report. Existing model-hold attention remains authoritative.

Resolve the alias with the same rule as `AgentTaskDispatcher.ResolveDispatchAliasAsync`: normalized pinned `Agent.ModelId`, otherwise the task kind's Low-tier alias. Extract/reuse that rule rather than hard-code `haiku` or match log strings. For a missing seat use the spec's intended kind/tier for a read-only preflight before `EnsureAsync`; check again after Ensure in case the seat identity changed. An unknown/error availability read is `DegradedUnavailable` with an availability-check reason, not proof of a hold or permission to bypass one. Existing length/role/disabled eligibility semantics remain first.

Use an explicit opt-in execution policy for Distill when extending the shared runner. Check and Diagnose retain their existing caller contracts unless a separately justified change is necessary. The dispatcher still checks holds at dispatch. While a Distill task remains Queued, the caller rechecks availability at the existing two-second polling cadence (each read is bounded by remaining time). A newly held queued task is conditionally canceled and returns `DegradedHeld`. A task that won the dispatch race is left standing and awaited only to the original deadline; do not kill/requeue it or relabel an active run as never-dispatched.

### D-3. One deadline, beginning before note enqueue

Capture `RequestedAt` and `DeadlineAt = RequestedAt + max(1, OutputDistillerWaitSeconds)` once immediately before the completion-note enqueue. Extend `DistillRequest` to carry those values and the mode snapshot with source/queued-note IDs. Apply `HoldUntil` is exactly that deadline. Shadow carries the same observation deadline with no hold. A mode refresh must not turn an already admitted Shadow request into Apply.

Carry that deadline through the worker, provisioning, task creation, queued dispatch and application. Persist a nullable deadline on the Distill `AgentTask` before dispatch can see it, so a server restart or caller cleanup failure cannot release expired queued work into the model. Existing non-Distill tasks and historical null-deadline rows retain current semantics. Add a structured expiry phase (`request-queue`, `provision`, `dispatch-queue`, `await-settlement`, `apply`) and append `DegradedExpired` for work rejected before execution; an in-flight run whose wait expires remains `DegradedTimeout`.

Check remaining time before every expensive stage and after awaited results, including a successful final poll: success observed after the deadline does not become an in-budget result. Clamp poll delays and all request-path I/O to the remaining budget. A producer report timestamp inside the window is diagnostic evidence; it does not authorize application after the note deadline. Do not clamp an already-expired request back up to another one-second wait.

The dispatcher must reject expired deadline-bearing Queued tasks during candidate selection and revalidate under its dispatch claim immediately before transition/delivery. The queued brief must also carry expiry into the terminal queue: a Dispatched row can still have an untyped Pending brief. Before its first attempt, cancel an expired, never-typed brief through the established queue lock and reconcile the task without typing. If typing has begun or a prompt is confirmed, retain the running task/standing owner and existing confirmation recovery. This closes the gap between a task's `DispatchedAt` and actual input.

Timeout is a decision at the deadline, not a promise that an unavailable database commits cleanup at that instant. Hold expiry independently makes the raw note eligible. Give best-effort cancellation/hold release/ledger finalization a separate **two-second total cleanup budget**, linked to host shutdown but not to the expired execution token. Log unfinished cleanup for the existing/added recovery sweep. Incident/alert emission must run outside the serial specialist wait and must not extend this cleanup allowance. Never reuse a scoped DbContext concurrently or abandon a still-running I/O operation against a disposed scope.

### D-4. Bounded, nonblocking request admission

Add `OutputDistillerQueueCapacity`, default **3 waiting requests**, distinct from the existing maximum of 3 unfinished Distill task rows. Keep one distiller reader. Use bounded-channel `TryWrite` with rejection when full; do not use a drop mode that reports success while discarding another request. Reserve no unbounded overflow list.

Record `DegradedBusy` with `queue-full` when admission fails, clear a previously established hold, and keep the raw note eligible. Ensure the producer handles a missing/closed worker as well as a full queue. Its fallback bookkeeping may await bounded database work, never model execution or external alert delivery. A request expired on dequeue records `DegradedExpired` without provisioning/creating work. Existing short/long skips should remain identifiable rather than be mislabeled model failures.

For completion notes, call the existing queue enqueue with inline idle delivery disabled, persist the note, and request an owned asynchronous flush. Admit distillation before a slow parent delivery can consume its budget. Both Shadow and ordinary raw completions must still get an idle flush without waiting for a future TurnEnd. Duplicate-note suppression that returns no new note ID must not enqueue duplicate distillation work. A process restart may abandon this optional in-memory optimization; the persisted raw note and its finite hold remain the fallback, and an expired task cannot launch afterward.

### D-5. Persist transcripts independently of ordered side effects

The default design is a durable, per-session sequence of runtime-action intents plus a bounded hosted worker. The live stream awaits transcript persistence and intent recording, then continues. Channel/review/task routing, compaction recovery, queue delivery, git/deliverable work and external publication must not be awaited in that fleet receive loop. Reconnect catches up transcript storage before resuming live ingestion without waiting for those effects. Instrumentation in S0 must confirm the exact blocked child and validate this boundary, not claim the historical 80-second event has been fully reconstructed.

Persist meaningful action identity using session ID, stored sequence, trigger kind and action phase in the same transaction as new transcript rows. A proposed `SessionRuntimeAction` entity has a unique identity on those keys, pending/completed state and retry metadata; names may follow nearby conventions. This is durable intent, not a new store of report bodies. A channel of IDs only wakes the worker; a startup/periodic database scan recovers pending actions after full channels, crashes or missed wakeups. Do not backfill all historical boundaries as new sends: initial migration/recovery targets currently owed task/queue/rules work, using existing durable consume guards.

Serialize transcript persistence per session across stream and pull paths, including the unseen-boundary decision and sequence allocation. That lock covers storage only. It is never held while executing effects, sending input or waiting for confirmation. `CatchUpTranscriptAsync` remains a fetch/persist-only operation safe under the queue lock; it must never await the side-effect worker. Cancellation must reach persistence, which currently has no token argument.

Workers have one owner per session, independent DI scopes and bounded global concurrency (initial default **4**); no fire-and-forget tasks or lock held across sessions. Bound external waits at their I/O seam and record queue age/saturation. A blocked delivery must not consume the transcript-ingestion lane, including that same session's confirming UserPrompt. Do not cancel a partially submitted delivery just to meet a distiller timer; its existing verification/parking/late-confirm protocol owns its outcome. If all workers are occupied, retain durable pending work and expose age, rather than lose triggers or spawn unbounded workers.

Preserve the current logical effect order: channel/review routing, task settlement, then next-message delivery; manual compaction is recovery followed by narrow idle flush, never report settlement. Preserve the second channel dispatch after late-confirm promotion. Carry the triggering stored sequence/turn boundary into extraction so processing an older job cannot select a newer turn merely because ingestion ran ahead. AssistantText after an early TurnEnd must trigger a later routing/settlement pass; `(uuid, kind)`/ApiCallId replay dedup, canceled/error turns, answered-Blocked watermarks and Grok rules barriers remain authoritative.

Before a delivery phase, re-read current idle/blocked/rules state and defer while earlier owed routing phases remain. A backlog of several already-ingested turns must be routed before another prompt is injected; do not use a historical idle snapshot to type now. Reuse existing durable task settlement and channel-send claims to avoid duplicates on retry. Record completion per phase so retrying a failed flush does not repeat successful routing or finished notifications. Explicitly test commit-before-wakeup, send/settle-before-action-ack, and worker restart; an in-memory dedup set is insufficient.

This is the largest-risk slice and requires separate TestDesign and Review. If S0 reveals that a narrower owned delivery scheduler removes all measured inline waits while preserving these contracts, Code may propose that smaller design in the plan with evidence before implementing it. It may not replace D-5 with arbitrary concurrent calls to today's latest-turn methods.

### D-6. Application shares the delivery lock

Move body replacement into a narrow method on `SessionMessageQueueService` using its existing per-session lock. Lock acquisition consumes remaining request time. Within a fresh scoped read, require matching note/source/digest, Pending, zero attempts, request mode Apply, unexpired original deadline and no full-report poll suppression. Re-read suppression under the same protection used by polled-note shrinking. Keep `NoteHeader`, raw `ContentDigest`, source identity, header handoff and full-report pointer unchanged.

Replace body and release the hold in one guarded save. A final time check precedes the update; a database statement deadline must prevent a delayed write from being treated as timely. The concurrency protection must be shared with claim-for-delivery, not a distiller-only semaphore. Already Sent/Canceled/attempted or explicitly SendNow-delivered notes are immutable to the distiller. A valid result that loses this race records `AppliedLate` with a reason and makes no replacement or second note. Expired results likewise never replace a Pending raw note. In Shadow, successful in-budget work can still stamp `DistilledResult` and gate outcomes, but no note is held or replaced.

### D-7. Account for late work without revising the note decision

Keep each ledger decision (`Outcome`, mode, captured wait/cost) as recorded. Extend list/stats projections with eventual linked task status, completion time, eventual cost and a cost-known flag, joined by `DistillTaskId`. This is read-side reconciliation against the authoritative task row: it handles settlement before or after the ledger write and survives process restart without callbacks or a second model request.

Add an effective total that counts a distinct linked run once, using its settled cost when available and the captured cost otherwise; show unresolved runs separately instead of presenting unknown cost as verified zero. Retain the old captured-cost total explicitly for compatibility and comparison. Apply the requested ledger time window before resolving later task outcomes, so a run settling tomorrow still belongs to yesterday's request cohort. Avoid changing Feedback/FullReadAt or original timeout classification. No late result updates the note or schedules another completion. Historical missing tasks remain unknown, not reconstructed from guessed prices. Existing task costs may be estimates; these changes do not improve the pricing model itself.

### D-8. Phase evidence is part of the fix

Use `TimeProvider` for deadlines/testable timestamps and monotonic elapsed measurements for individual operations. Preserve producer timestamps and transcript `CreatedAt`; the latter is assigned before save and must not be called a commit timestamp. Add structured logs/spans for fleet event receipt, persistence start/end, action enqueue/start/end, matched-report observation, settlement entry and successful settlement save. Include session/source/run/note IDs and stored sequence, not prompt text or secrets. Time each dispatcher sweep and candidate wait separately from its nominal five-second poll.

Add nullable phase fields to the distillation ledger/DTO: requested/deadline/dequeued/run-created/decision times, queue wait, specialist wait, cleanup duration, reason/expiry phase and availability alias. Keep legacy `WaitMs` semantics documented; absent historical data stays null. Diagnostics must have no external blocking sink on ingestion and must not change delivery or settlement if logging fails.

## Implementation slices

### S0. Instrument and reproduce the actual blocking call

Files: `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs`, `server/Application/Services/AgentSessionRuntime.cs`, `AgentTaskReplyService.cs`, `SessionMessageQueueService.cs`, `AgentTaskDispatcher.cs`, and `SpecialistTaskRunner.cs`.

Add the D-8 phase observations before moving awaits. Use the existing fake runner and test database: hold session A's delivery-confirmation await open, stream B's exact task UserPrompt, marked AssistantText and TurnEnd, and observe receipt/persistence/settlement. Exercise A via both direct turn-end flushing and settlement's parent-note enqueue. Also block SignalR publication and a routing dependency independently to identify their contribution. Repeat with reconnect backfill. No live model or live broker.

Exit evidence: an operation-named trace proving where B stops while A is gated, plus an inventory of every slow awaited child in live/catch-up/exit/status event handling. Write the evidence and chosen boundary into this plan. Trace a representative synthetic outlier from producer through durable settlement; distinguish ingress delay from post-entry settlement work. If the assumed block does not reproduce, complete availability/deadline slices but report ingestion as unfinished and return for investigation; instrumentation alone is not the ingestion fix.

### S1. Contract, deadline and phase schema

Files: `OutputDistillationQueue.cs`, `OutputDistillationService.cs`, `SpecialistTaskRunner.cs`, `server/Application/Settings/DelegationSettings.cs`, `server/Domain/Entities/AgentTask.cs`, `SessionQueuedMessage.cs` if expiry metadata is required, `OutputDistillationRecord.cs`, `server/Domain/Enums/OutputDistillationEnums.cs`, `server/Application/Dtos/OutputDistillationDtos.cs`, EF model configuration and CLI-generated migration/snapshot, `server/Program.cs`.

Add the carried request contract, nullable persisted task deadline, append-only enum values/reasons, opt-in runner policy and nullable timing fields. Do not use the mutable hold as the only source of the original deadline: releasing it must not erase expiry evidence. Preserve old callers through an explicit compatibility path. Test existing null fields/old enums and mode snapshots before activating behavior.

### S2. Held-model and queue/deadline fallback

Files: `OutputDistillationService.cs`, `OutputDistillationQueue.cs`, `OutputDistillationHostedService.cs`, `SpecialistTaskRunner.cs`, `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, shared alias resolver adjacent to `ModelAvailability`, and `SessionMessageQueueService.cs`.

Implement D-2/D-3/D-4, including pre-Ensure checks, queued rechecks, bounded admission, atomic dispatch-expiry guard and final pre-type expiry guard. Give request paths a remaining-budget token and cleanup its separate bound. Route alert work outside the serial wait using owned, bounded processing. Do not hide shutdown cancellation in a generic Timeout result. Make raw fallback eligibility independent of successfully writing the diagnostic ledger.

### S3. Remove slow effects from the fleet receive/catch-up loops

Files: `SessionRunnerEventPump.cs`, `AgentSessionRuntime.cs`, new concrete `SessionRuntimeAction` domain entity and application coordinator, new hosted worker under `server/Infrastructure`, EF configuration/CLI migration, `Program.cs`; sequence-bound extraction changes in `ChannelReplyDispatcher.cs`, `ReviewReplyDispatcher.cs`, `AgentTaskReplyService.cs` as required; queue/compaction/rules call sites identified by S0.

Implement D-5 using durable intent and bounded owned workers. Keep external I/O implementation in Infrastructure and domain records dependency-free. Do not use a shared DbContext across workers. Re-run the S0 blocked cases: B's transcript must persist while A remains blocked, and B's task must settle within its remaining deadline when worker capacity is available. Validate same-session confirmation progress, several buffered turns, replay and reconnect, not just two independent direct method calls. Audit all fleet event branches so exit, status, fault or publication handling cannot retain an unbounded inline wait.

### S4. Atomic application and eventual-cost projections

Files: `OutputDistillationService.cs`, `SessionMessageQueueService.cs`, `OutputDistillationDtos.cs`, `server/Api/Endpoints/DistillationEndpoints.cs` only if projection wiring needs adjustment, and current distillation API client/types/stat display/script consumers discovered by `rg`.

Implement D-6/D-7. Keep current endpoint routes, outcome names and captured values backward compatible; add fields for eventual/effective accounting. No dashboard redesign. Ensure projection queries avoid N+1 reads and count distinct runs. Update `docs/orchestration-loop.md` section 10 with actual deployed semantics and `docs/session-runtime-invariants.md` with the new storage/effect ordering contract after implementation; do not present this plan as already shipped.

### S5. Verify, review and measure in Shadow

TestDesign supplies executable V/R/PC cases before Code. Code runs them with isolated output and reports counts, failures and phase evidence. This cross-session delivery/settlement change goes to Review before landing. Deploy/measurement is a later operator-owned stage following the canonical runbook, not an implicit restart during tests.

## Verification requirements for TestDesign

The separate TestDesign stage must turn these acceptance contracts into fixtures, named tests, regression filters and a small set of meaningful red/revert/green positive controls. The following are requirements, not claims of executed tests.

| Area | Required proof | Existing anchors / proposed new class |
|---|---|---|
| Availability | Held before provisioning creates no Distill row; normalized alias and kind isolation; hold appearing while queued cancels only Queued; dispatch wins safely; hold removal permits a later request; read failure differs from hold. | `ModelAvailabilityDispatcherTests`, `ModelAvailabilityTests`, `OutputDistillationTests`; new `SpecialistTaskRunnerDeadlineTests`. |
| Queue | Capacity+1 rejects the extra request explicitly; producer never waits for a model; raw hold is released; duplicate completion admits no second request; expired requests never Ensure or dispatch. | `OutputDistillationTests`; new `OutputDistillationQueueTests` and `OutputDistillationHostedServiceTests`. |
| Deadline | Provisioning/channel/dispatch/brief-queue time all consume one budget; exact-boundary and just-late reads/writes; restart retains expiry; no minimum-budget reset; typed/confirmed work survives caller expiry. | New `OutputDistillationDeadlineTests`; dispatcher and message-queue fixtures. |
| Cleanup | Blocked alert cannot extend waiting; bounded cleanup with DB failure; shutdown does not fake success/timeout; raw hold expires even if release fails; dispatched standing seat remains owned. | `OutputDistillationTests`, new runner deadline tests, `AgentTaskCheckInterpreterTests`, `SpecialistRoleContractTests`, existing Diagnose behavior tests selected by actual caller changes. |
| Ingestion | S0 reproductions red under original await shape, green after separation while the blocking gate is still held; actual hosted stream/catch-up path; persistence timing distinguished from CompletedAt and save. | New `SessionRunnerEventPumpTests`; `AgentSessionRuntimeTests`, `FakeSessionRunnerClient`. |
| Ordering/recovery | Own UserPrompt ingests during verification; split end/text, multiple buffered turns, duplicate/restarted tailer sequence, interrupted/error/manual/auto-compaction semantics; durable pending work after crash/full wakeup channel; saturated workers remain bounded. | `AgentTaskSettlementRaceTests`, `SessionMessageQueueDeliveryVerificationTests`, `ChannelReplyDurabilityTests`, `CompactionRecoveryTests`, `GrokRulesCompactionRecoveryTests`; new runtime-action tests. |
| Application | Before deadline replaces exactly once; pending after expiry never replaces; concurrent delivery/SendNow/full-report poll wins safely; attempted/canceled note immutable; Shadow never holds/rewrites; raw report, digest, header/handoff and full pointer survive fallback. | `OutputDistillationTests`, `PolledCompletionNoteShrinkTests`, `OutputDistillationGateTests`; new deadline tests. |
| Accounting | Late success/failure/cancellation updates eventual projection without changing original verdict or note; settlement on either side of ledger insert; restart; repeated reads and duplicate links count once; unknown is visible; cohort window stable. | `DistillationEndpointTests`; new `OutputDistillationAccountingTests`. |

Use `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c432/ -- --treenode-filter "/*/*/<NamedClass>/*"` with each actual class substituted. TestDesign must select methods for large regression classes rather than force the whole Application namespace. Require nonzero executed counts and intended assertion failures for positive controls. Use the established production-runner guard and row-scoped shared-Postgres assertions. Tests driving global sweeps require ungrouped `[NotInParallel]`. A process-spawning fixture needs the assembly's `ParallelLimiter<ProcessSpawnLimit>`; do not run `Antiphon.Tests` concurrently with `Antiphon.Agents.Pty.Tests`.

Drive deadline and polling tests using controllable timers/barriers, not 45-second sleeps or a frozen timestamp with real timers. An offset clock or properly driven FakeTimeProvider must satisfy the queue's clock contract. The stream test must feed a real fake stream through the hosted pump, not call the newly split ingest method and assume the wiring uses it. External sends stay captured by fakes. If API consumer types change, use scoped `scripts/test-client.ps1` tests plus the client type/build check; no browser E2E is required for this backend change.

## Post-deploy measurement and completion bar

After a separately authorized deployment, inspect the running mode and budget directly and collect the next naturally occurring Shadow cohort (target at least 100 eligible attempts across an available period and a hold period; report if a hold never occurs). No synthetic specialist jobs are needed. Preserve request counts including admission/length skips and distinguish all-ledger rate from attempted-run rate.

Report held-degrade latency and created-run count, request-queue age/rejections, available-run dispatch and prompt-to-report time, fleet receipt-to-persist, report-observed-to-settlement-save, cleanup time, phase-specific expiry rates, worker saturation and unresolved/effective cost. Compare available-model requests separately so relabeling holds cannot masquerade as latency improvement, and separate bundle versions so the independent v2 prompt change is not attributed to CARD-0432. Use the investigation's baseline only for comparable intervals; no promise of a particular timeout percentage or gate acceptance follows from this design.

Shipping requires the bounded-ingestion reproductions to pass, no duplicate/raw-report loss, correct expiry before launch/application, and unchanged Shadow mode. Production trend measurement can remain pending until enough natural traffic arrives and must be reported as pending, not inferred from a healthy server. A continued large ingestion tail after the selected waits are removed is new evidence for the named phase, not grounds for an automatic timeout bump.

## Decisions and handoff status

No user decision blocks TestDesign. D-1 through D-8 are implementation defaults. The unresolved historical blocker is explicitly scoped to S0, with named reproduction gates and a required evidence artifact before the concurrency slice. The separate verification design must cover the durable-action complexity rather than fold this into a small timeout-setting test.

--- next stage ---
next: test-design
handoff: Design executable V/R/PC coverage for CARD-0432 S0-S5: held-model fast fallback, one 45-second deadline through admission/dispatch/application, instrumented fleet-pump blocking reproduction, durable ordered side effects and late-cost projection. Keep Shadow, use isolated runner/broker fakes, and require Review for the concurrency changes.
artifact: docs/superpowers/plans/2026-09-07-card-0432-distiller-timeouts-plan.md
