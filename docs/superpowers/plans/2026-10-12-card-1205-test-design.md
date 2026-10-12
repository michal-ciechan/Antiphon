# CARD-1205 frozen test design

Date: 2026-10-12. Inspection base: `ba9504c172627549ad884be6406a3a04b546baed`.
This is the verification appendix to [the fix plan](2026-10-12-card-1205-turnend-gap-plan.md);
it leaves that document and its fix design unchanged. **Use this document's checkpoint
table for Code**, superseding only the plan's proposed verification roster/counts.
The task token is present; API reads were task-bound. No production/test files changed,
and no build, discovery executable, test or mutation ran in TestDesign.

Current remote master observed read-only was `d8188da496b2369e9ee688f8977c4888510fbb9f`.
The plan commit changes documentation only. This is source evidence for the proposed
baseline, **not an executed baseline-red result**. S1 must record the current immutable
master SHA and compare relevant production files before calling its result baseline-red.

The card was read through `scripts/card.ps1 get CARD-1205 -Board Antiphon`.
Retain the plan's latent-race attribution, server-only subscription proof, 120-second
default evidence interval, local-only scope and logs-first visibility. Neither an
elapsed deadline nor a successful HTTP connection certifies idle.

## Verification design

### Inspection

Bodies inspected before reserving new cases:

| Bodies read | Boundaries -> verification or exclusion |
|---|---|
| `SessionRunnerEventPumpTests.cs` complete, including GatedEventBus and PhaseLogger; `ScriptedSessionRunnerClient.cs` complete | S0 = four argument executions, typed exit = one; R-6. Its preexisting channel retains pre-subscription events, so exclude that fake from H1. |
| `SessionRunnerHttpClientHostStatsTests.cs`; `SessionRunnerGenerationWireTests.cs` client/handler, generation and null-body cases | HTTP handler and clock recipes -> H2, V-3..7, V-32. Existing tests are read-only recipes, outside ordinary selection. |
| `SessionStateCommitTests.cs` complete (8); `TranscriptWorkingStateQueryTests.cs` complete (12); `SessionStateTestFixture.cs` complete; `TranscriptHotPathFixture.cs` DB setup/capture/oracle | SaveGate, SaveFault, RejectRow, RacingRow and per-test DB ownership -> H3, V-8..12/V-25..26, R-7. Do not copy their sleep-based absence checks. |
| `ControlledTimeProvider.cs` complete; `LandReceiptScanCacheTests.cs` manual-clock recipe; `QueuedInputWatchdogTests.cs` first eight cases and Scenario setup; `CompletionNoteWorkQueueTests.cs` complete | Timer-registration barriers, one-flip rows, detection-only spies, queue overflow -> H4, V-13..19/V-27..35. |
| `C544World.cs` complete; `C544DeliveryRig.cs` complete, including C544Boundary/C544DeliveryFault; `BridgeQueueHarness.cs` options, graph, caller registration and insert helpers; `FakeAgentProtocolAdapter.cs` input/composer/submitted-body and kill/dispose paths | Actual settlement and keyed outbox/queue recipe, restart and EF transaction cuts -> H5, V-20..24/V-36..40. Replace direct SQL caller receipt insertion in the card-specific helper. |
| `VerificationRoundDeliveryTests.cs`: CompletionReceipt, CompletionRecovery, CompletionReceiptWholeWire, StampIsNotReceipt, AssertReceivedOnceAsync | Full-wire/destination/floor/digest oracles and before/after enqueue cuts -> H5, R-8. Internal loops count as one TUnit execution per method. |
| `AntiphonWebAppFactory.cs` configuration/override/disposal; `TestDbFixture.cs` isolated-store API; `Antiphon.Tests.csproj` references | H4 host boot uses actual Program registration, isolated DB, production-runner refusal guard. Real SessionRunnerEventHub is already project-referenced; no project-file change needed. |
| `SessionRunnerEventPump.cs` complete; `SessionRunnerHttpClient.StreamEventsAsync/ParseEvent`; runner `Program.cs` /events body; `SessionRunnerEventHub.Subscribe/Publish` | `: connected` is flushed BEFORE Subscribe; exact keepalive/parsed supported event follows Subscribe -> V-1..7, R-1/R-6. |
| `AgentSessionRuntime` PullAndPersistAsync, CatchUpForReceiptAsync, ObserveReceiptStateAsync, SyncTranscriptAsync, PersistTranscriptAsync/Core, PublishCommittedAsync; `SessionStateStore` read/write/lease bodies and `SessionStateSnapshot` classifier | Shared gate, committed revision, NeedsReload/retained failure and source identities -> V-8..12/V-25..26. No replacement ingestion pipeline. |
| `SessionRunnerDtos.cs`; runner GetTranscript; `PhoneHomeRunnerDirectory.GetBindingAsync`; routing Route/StreamEvents | DTO generation exists; local binding is the existing three-field ownership rule, not runner-name equality -> H3/H4. Phone-home pump changes excluded. |
| `CompletionNoteWorkHostedService.cs`, `CompletionNoteWork.cs` complete; queue FlushIfIdle/stranded discovery; notification Reconcile receipt and keyed-row recovery; Program state registrations | Durable recovery, volatile wakeup capacity, final idle gate and receipt-save cut -> H5, V-22..24/V-33/V-36..40. |
| `docs/testing-and-build.md` checkpoint schema/tool, source qualification, bound Mutation, filter syntax, clock/DB/process safety; project-context, resilience, orchestration and session-runtime owner sections | Execution contract below; no whole Unit or namespace lane. |

**Binding status.** The five new classes, two helpers, checked operation and watchdog
do not exist at the inspection SHA; source search found no C1205 methods. Their paths
and method names below are reservations for Code, not a compiled-name claim. Existing
R-6/R-7/R-8 bodies and counts are source-bound. Code must bind each new method, argument
roster and assertion label to the compiled result before returning Review. A proposed
payload targeting new code remains **binding required after Code**, even with a green
ordinary run. Read-only listing cannot establish filtered execution on pinned TUnit.

#### Fixture recipes and reachability

- **H1 — gap integration**, new `tests/Antiphon.Tests/TestHelpers/TurnEndGapHarness.cs`.
  Compose SessionStateTestFixture's isolated DB/gate registrations with actual
  SessionRunnerHttpClient and EventPump. A controlled HttpMessageHandler owns list,
  transcript snapshots and a frame-gated stream; real SessionRunnerEventHub owns
  subscription/publication. Snapshot/tailer is a declared in-memory substitute.
  Seed prior TurnEnd then qualifying ToolCall and assert SQL/cache Working. Complete
  the caller's initial snapshot before publishing ToolResult/AssistantText/TurnEnd
  into a hub with no subscriber; append those same identities to snapshot custody
  first. Hold a second session's first pull or the events handler to fix that order.
  Record C1-complete < custody append < publish < connected < Subscribe < proof.
- **H2 — HTTP framing**, same helper. Return at most one complete SSE frame per
  underlying ReadAsync. After its bytes are consumed, the **next read request**
  completes an observable barrier and blocks. StreamReader cannot prefetch a frame
  that has not been released. This barrier works on baseline and fixed code: on
  fixed code the marker consumer must finish C2 before requesting further bytes.
  Use iterator-only consumers for marker counts and actual pump for ordering/persist.
  The fake writes connected before registration, matching the inspected endpoint;
  an open response, response headers or connected callback is not a subscription
  witness. Register the real hub, then release keepalive or a real supported event.
- **H3 — checked runtime**, H1's DB graph, real runtime/store/SQL readers, EF
  interceptors from SessionStateCommitTests. Call proposed
  `ProbeWorkingTranscriptAsync` (same signature in all tests once Code lands).
  Capture durable frontier under `SessionStateStore.BeginWriteAsync`; hold the
  HTTP snapshot **after releasing that lease**. Concurrent runtime ingestion uses
  the existing gate. After pull, reacquire that same lease, freshly read DB
  status/ownership/generation/frontier and compare the captured stamp **before**
  `PersistTranscriptCoreAsync`, then `PublishCommittedAsync` under the lease.
  Do not call PersistTranscriptAsync while already holding its nonreentrant lease.
  Cache-disabled cases keep the registered store/gate and assert the fresh SQL
  verdict; absent store is a separate Unknown case.
- **H4 — bounded scheduler**, new watchdog tests use H1/H3 plus ControlledTimeProvider
  timers and a card-local UTC-offset wrapper. Advance monotonic time/timers separately
  from wall time. Expose read-only scheduler observations (admitted IDs, active owned
  operations, directory/pending counts, pass completion); do not substitute a fake
  scheduler or result classifier. Drive concrete passes to a barrier/quiescent
  continuation boundary before asserting absence. A deterministic continuation
  drain must reach a stable blocked-I/O point; a real-time wait expiration is not
  an absence oracle. For the serial-await mutant, record that the pass remains
  pending after its admitted probe blocks and the controlled continuation queue
  drains; fail at the pass-completed assertion, then release/join in finally.
  A test-hang cancellation budget is teardown protection only.
- **H5 — delivery**, new `tests/Antiphon.Tests/TestHelpers/TurnEndGapDeliveryHarness.cs`.
  Use C544World's configurable graph and RestartAsync, real AgentTaskReplyService,
  AgentTaskLandNotificationService and hosted scanner, SessionMessageQueueService,
  CompletionNoteWorkHostedService, pump, state store and watchdog. Use
  AddDelegationWorktreeGraph via C544World, not copied manual git registrations.
  Disable distillation; five independent task roots prevent batching from hiding
  missing members. Actual `FakeAgentProtocolAdapter.OnSubmitted(body)` appends that
  body's LF-normalized full text, UUID, producer time and ordinary TurnEnd to runner
  custody, then publishes through the real hub. It must return without awaiting a
  callback on the same serial pump: the queue may occupy that pump while waiting
  for receipt. The real queue's HTTP catch-up may ingest the prompt while the pump
  is occupied; record whether ingestion was live or that real pull. Both must use
  actual HTTP mapping/runtime persistence, not direct SQL receipt insertion.
  Advance queue timers only after their registration barriers. C544Clock is a
  wall offset and cannot prove new monotonic deadlines; replace TimeProvider in
  the card-specific graph, including timers, and drive it until owned queue work
  completes. Do not freeze GetUtcNow while leaving real timers ticking.

H1/H3/H4 integration cases are Category=Integration (Slow only if measured/required
by the repository registry); H2 iterator-only cases may use Unit. H5 is
Integration/Slow and uses the existing assembly-local ProcessSpawnLimit because
C544World creates Git children. Program-host tests retain ProductionRunnerGuard;
remove unrelated hosted consumers only by explicit fixture registrations, retaining
the recovery host under test. Each recipe owns one isolated PostgreSQL store and
scopes reads to its IDs. No test contacts the production runner or messaging broker.
Dispose streams/subscriptions; release test gates and cancel/await workers, HTTP
requests, hosts and provider lifetimes in finally, including the deliberate red.

Slow classification requires a matching fully qualified entry and adjacent reason
in `tests/Antiphon.Tests/slow-tests-allowlist.txt`; its header and existing
VerificationRoundDeliveryTests entry were inspected, as was
`tests/Shared/TestClassificationGuardTests.cs`. S5 owns its new delivery-class entry;
S3/S4 may add only their own measured/required Slow entries. R-9 runs the existing
metadata guard in CP-6. These are test metadata edits, not a production-scope change.

**Frontier test binding.** T13 new-prompt and annotation flips use real runtime
persistence; identity replacement uses the store's mutation/reseed discipline and
an actual new durable row at the same sequence/count, not a stale cache assignment.
Generation/owner/status flips commit through a fresh DB context before response
release. T15 uses existing `NextGateWait` plus EF SaveGate to prove a concurrent
writer reached the serialization boundary; no 30ms sleep. A store revision is not
a replacement for the fresh DB ownership read. If Code cannot keep final validation
and persistence under the existing gate without a new external seam, return
`next: plan` for that seam; do not move validation after append.

**Completeness limit.** Current transcript DTO carries AcceptedStartedAt and
LastSequence but no cryptographic complete-history certificate. Require session,
generation, present known frontier and consistency with the returned tail. T11's
truncated-tail is an observable inconsistency (LastSequence ahead of last entry)
with frontier otherwise present. A peer that fabricates a self-consistent truncated
snapshot is outside this card's proof; do not claim its detection. Missing/invalid
evidence for the admitted checks is Unknown. Do not require TerminalComplete=true
for a Running session's ordinary full snapshot.

**Reachability audit.** Interval is 30..3600 and poll is 1..30. Therefore poll >
interval cannot be the sole invalid scalar under those independent bounds. The
plan's proposed independent poll>interval PC is unreachable; the 12 invalid rows
exercise 10 independently bypassable bound predicates instead. Keep a redundant
relationship validator if implemented, but do not claim an additional independent
red. Pending unique work cannot exceed admitted tokens if membership is maintained:
T23 checks that subset under generation churn, separately from directory capacity.
If Code implements independent pending storage, bind its additional capacity guard
and PC rather than leaving an unreachable 1025th-pending case behind an earlier cap.

T11 isolates persistence vetoes: the NeedsReload row uses the existing 23505
collision/reload path with no retained failure; the retained-failure row uses
RejectRow to commit a ToolResult stub plus a good final TurnEnd, yielding a fresh
idle SQL verdict and `NeedsReload=false` while the failure remains retained.
The general persist-fault row protects the combined fail-closed outcome; it is not
claimed as a second independent red for removing just NeedsReload. Empty snapshot
is also missing its Working frontier: PC-15 models an erroneous early empty=idle
return, rather than removing a redundant nonempty check and expecting other guards
to stop protecting it. Cancellation in T11 is the checked result Unknown.

T28 starts from valid defaults (interval120/poll15/timeout10/concurrency4/page128)
and flips one scalar. In particular poll31 leaves interval120, so the relationship
validator cannot mask the poll upper-bound mutant. Resolve and invoke the real
registered startup options validator before starting timers: an incidental timer
argument exception must not substitute for the expected options rejection.

With defaults, the nominal single-session check allowance is 120+15+10 = 145
seconds, excluding measured commit delay. Stable N-session discovery adds up to
ceil(N/128)*15 seconds and admitted backlog up to ceil(N/4)*10 seconds; held or
non-cooperative I/O changes the outcome to visible debt. Log due/result metadata
(session, generation, server epoch, reason, elapsed age, pre/post stored sequence,
added count, duration) and separate unknown, discovery, overflow, capacity and
overdue warnings. T18/T21/T22/T24/T25/T31 assert these at controlled poll boundaries.

#### Deterministic baseline-red stop gate

T01 performs H1 through the next-read barrier following a subscribed keepalive.
Capture the missing-end row count and SQL/cache verdict then, with **no grace pull
yet**. In finally/control work, call the real CatchUpTranscriptAsync, prove that
it retrieves and stores the same UUID/kind exactly once and makes the caller idle,
and stop/join the pump. Assert the captured pre-grace count equals one and captured
Working is false **after** preserving the grace witnesses. On current production
the first assertion should be `preGraceEndCount.ShouldBe(1)` with actual 0.
This makes the failing reason missing durable coverage, not fixture timeout.
T02 holds subscription throughout, proves missing custody is recovered by the explicit
pull and stays green before/after S2. Healthy keepalives are individually consumed;
no simulated-hours assertion substitutes for missing-row evidence.

C1 and post-proof C2 must be distinguishable HTTP requests. Prove no C2 for connected,
unknown comments, unsupported frames or iterator construction. The fixed quiet case
uses exact keepalive; the active case retains the first supported event and awaits
C2 before dispatch. The marker remains internal. An unsuccessful C2 attempt may
leave Working/debt; the new watchdog is not allowed to infer success from Sync's
void Task. S1 contains test/helper edits only, is committed/pushed before CP-1, and
is not landed alone. If CP-1 is green or fails elsewhere, stop the fix sequence and
return investigation evidence; do not invert the desired assertion or add sleeps.

### Delivery inventory

Join each evidence line by SessionId + AcceptedGeneration and, for completion,
TaskId -> terminal SourceEventId -> notification Id -> QueueMessageId /
SourceLandNotificationId -> destination -> wire SHA-256 -> confirming prompt sequence.
Retain raw exact submitted body in ignored test evidence, never production logs.

| Path / producer -> destination | Persistence boundary | Recovery / observable recipient evidence | Cases |
|---|---|---|---|
| Tail append -> real hub -> HTTP iterator/pump | Runner snapshot holds UUID+kind before hub Publish; subscription has no replay | C2 after genuine subscription proof, or independent checked probe; fresh SQL row plus committed state | T01..T16 |
| Watchdog discovery -> owned probe | DB Running/local ownership and committed Working; directory is epoch-local | Boot discovery reconstructs unknown age; bounded pages/retries expose debt; result only after checked persistence | T18..T35 |
| Checked idle -> flush queue -> normal worker | Transcript committed before volatile wakeup; queue/outbox rows already durable | Full channel requests durable sweep; restart's actual CompletionNoteWorkHostedService scan schedules same destination; actual receipt required | T33, T38..T39, T43 |
| Actual reply settlement -> TaskCompletion outbox | Task result/event/notification commit atomically | Boot notification scanner recovers same obligation; failed settlement rolls back and replays the already durable report through real OnTurnEnd | T37, T42..T43 |
| Notification reconciler -> real WhenIdle queue | Keyed insert and immutable payload/destination | Pre-enqueue failure retries; lost ack rejoins committed key; busy caller has zero attempts; eligible caller reaches actual submitted body | T37, T41..T44 |
| Queue claim/render -> provider input | Frozen wire digest/member IDs and attempt baseline committed before submit | Interrupted claim recovers through existing stranded queue path; uncertain/Working stays held | T43, T46 |
| Actual submitted bytes -> runner UserPrompt -> SQL -> notifier | Complete prompt ingested through real HTTP/runtime before confirmation save | Receipt-before-save failure; fresh providers scan the same prompt without retyping, then commit ConfirmedAt/sequence | T37..T40, T43, T45 |

Evidence order is asserted, not reconstructed from timestamps: settlement commit <
queue admission < idle eligibility < attempt claim < actual body submit < runner prompt
custody < committed full UserPrompt < notifier confirmation. No request, insert,
event, Sent flag, screen redraw, Enter ack or fake adapter callback alone passes.
Receipt must be one complete matching prompt on the destination above its attempt
floor, carrying the frozen wire, with a matching recorded digest; a sibling stamp
or prefix is insufficient. Post-restart reconcile twice and assert durable keys and
submission count unchanged. ConfirmingPromptSequence is set by real notifier code.

H5's eight cut rows each run with both a busy and an already eligible caller.
Record the cut's counter=1 before restart. Suppress/hold competing old workers so a
scanner or sweep cannot heal a cut before it is observed. For obligation-insert
there is no committed notification key yet: assert transaction rollback, restart,
and replay settlement once, then follow the newly committed key. After key commit,
replacement identity is a failure. For wakeup-full, fill the real 128-entry
CompletionNoteFlushQueue before destination admission, observe TryEnqueue=false and
the actual Recovery sweep, drain owned dummy work, then await receipt. For
transcript-commit/lost-wakeup, a recording queue override blocks **before** its base
TryEnqueue; cancel/join old services, rebuild a fresh queue and observe
`completion-scan` as the retry producer. T39 must not call fixture FlushIfIdle.
For receipt-before-save, committed full prompt exists while notification remains
unconfirmed; restart without editing either row. H5 controls rebuild provider,
runtime/cache, pump, queue, notifier, watchdog and volatile queues, keeping only DB,
simulated runner custody, composer/submission ledger and immutable identities.

End each witness at a finite schedule of observed producer/worker passes and
registered timers, then read recipient rows even when their count is zero. Waiting
only for an expected receipt would turn a missing-delivery mutant into a timeout.
After the initial boot pass, keep unrelated periodic scanner/sweep passes held
until the path under test has its observation, so another recovery mechanism cannot
mask its mutant. T43's wakeup-full row includes a separate same-epoch recovery
control before its restart arm: drain the initial boot sweep, saturate the queue,
reject the destination wakeup, release capacity and require a receipt from the
requested recovery sweep without advancing the 15-minute fallback. Then use a
fresh obligation for the crash/full-channel arm. PC-94 targets that first control;
a boot sweep after a restart would otherwise mask removal of RequestSweep.

For T43's interrupted Sent claim, invoke the real `FlushStrandedQueuesAsync`
recovery entry point once after the recorded attempt-age boundary, then require
recipient evidence. `SessionHealthHostedService.cs` was also read: its existing
PeriodicTimer is real time and is gated by RC watch settings. Direct invocation of
its concrete recovery primitive is a declared substitute for that legacy host's
cadence; it proves claim recovery and recipient delivery, not automatic scheduling
by that host. T39 separately uses the actual CompletionNoteWorkHostedService boot
sweep for the newly changed lost-wakeup handoff, without a fixture direct flush.

Substitutes: controlled HTTP framing does not prove Kestrel scheduling; real hub plus
the inspected endpoint ordering proves the modeled subscription boundary. Simulated
tailer/provider does not prove JSONL capture, native paste/Enter, model behavior or
AppHost process restart. C544World's seeded report/dispatch does not prove delegate
launch transport; its real reply settlement is the completion producer boundary.
Fresh service graphs prove service restart survival. Existing R-8 direct-SQL receipt
fixtures prove receipt matching only; they cannot replace H5's ingestion witness.

### Proves it works now

This title names the required Code evidence, not an execution claim by TestDesign.
New methods are in `tests/Antiphon.Tests/Application/<Class>.cs`, namespace
`Antiphon.Tests.Application`; helpers are the two paths above. Each row is a frozen
method reservation. Literal variant lists expand via explicit Arguments rows; do
not silently replace them with an internal loop and lower the execution floor.
H1..H5 supply layer/classification, setup and reachability; the table refines them.
All assertions have case labels. Use hand-written expected outcomes, not the
production predicate as the expected-value factory.

| Test / V or R | Exact method / executions | Recipe; literal variants | Decisive oracle; reachability | Case -> mutation -> red |
|---|---|---|---|---|
| T01 / V-1/R-1 | `TurnEndGapReproductionTests.C1205_Gap_end_is_stored_before_any_grace_pull` / 1 | H1; quiet stream; cache on | Captured pre-grace end count = 1 and Working = false; grace control count = 1. Reach: Next-read barrier after keepalive; assert custody/publication order before observation. | PC-1; exact compiling defect and red below. |
| T02 / V-2 | `TurnEndGapReproductionTests.C1205_Grace_pull_recovers_the_unsubscribed_tail` / 1 | H1; subscription remains held | Before explicit pull: count 0, Working true; after: count 1, Working false. Reach: First catch-up completed, hub publication has no subscriber; actual CatchUpTranscriptAsync called. | PC-2; exact compiling defect and red below. |
| T03 / V-3 | `SessionRunnerStreamReadinessTests.C1205_Connected_comment_is_not_readiness` / 1 | H2; headers then exact connected comment, subscription held | Markers = 0, snapshot requests still 1. Reach: Next-read barrier after connected frame; HTTP headers independently recorded. | PC-3; exact compiling defect and red below. |
| T04 / V-3 | `SessionRunnerStreamReadinessTests.C1205_Unknown_comment_is_not_readiness` / 5 | H2; `: Keepalive`, `: keepalive extra`, `: unknown`, empty frame, unsupported event | Markers = 0 for each literal frame. Reach: Each supplied frame consumed before next-read barrier. | PC-4; exact compiling defect and red below. |
| T05 / V-4 | `SessionRunnerStreamReadinessTests.C1205_Keepalive_emits_readiness_once` / 1 | H2; three exact keepalive comments | One readiness marker; zero transcript rows/events carrying the marker. Reach: Register hub subscriber before first comment; drain all three frames. | PC-5, PC-10; exact compiling defect and red below. |
| T06 / V-5 | `SessionRunnerStreamReadinessTests.C1205_First_event_follows_readiness_without_loss` / 8 | H2; transcript, output, exit, fault, bound, adopted, status, started | Sequence is [marker, identical supported event]; event count 1. Reach: Actual ParseEvent accepts each real wire DTO; compare payload fields and identity. | PC-6; exact compiling defect and red below. |
| T07 / V-6 | `SessionRunnerStreamReadinessTests.C1205_Reconnect_rearms_readiness` / 1 | H1/H2; two distinct enumerator connections | One marker and second catch-up on each connection; second gap end saved. Reach: Cancel/dispose first stream; hold second subscription and publish a second UUID. | PC-7; exact compiling defect and red below. |
| T08 / V-7 | `SessionRunnerStreamReadinessTests.C1205_Stream_failure_and_cancellation_release_owned_resources` / 2 | H2; read-fault, caller cancellation | Response/content/stream disposed; subscriber closed; owned tasks joined. Reach: Fault/cancel at pending ReadAsync; count disposal before releasing teardown fallback. | PC-8; exact compiling defect and red below. |
| T09 / V-5/R-1 | `SessionRunnerStreamReadinessTests.C1205_Catchup_finishes_before_live_dispatch` / 1 | H1; first live output held behind second snapshot | Trace order C1 < subscribe < proof < C2 commit < live callback. Reach: EF save barrier holds C2; callback spy must remain empty at barrier. | PC-9; exact compiling defect and red below. |
| T47 / V-5 | `SessionRunnerStreamReadinessTests.C1205_Held_event_survives_failed_second_catchup` / 4 | H2; success, list timeout, throwing list, host cancellation | A responsive consumer accepts the original output once. Failed catch-up gives a bounded preservation offer; a pending consumer at cancellation/deadline is explicitly warned and abandoned. Timeout reconnects; throw and cancellation do not. Reach: Gate the second GET /sessions; success returns, timeout waits on ListAsync's own deadline, throw faults the handler, cancellation cancels the pump token. | PC-111, PC-112, PC-113, PC-114; exact compiling defect and red below. |
| T48 / V-5,V-7 | `SessionRunnerHeldEventDeliveryTests.C1205_Blocked_held_callback_stops_on_host_cancellation` / 2 | Cancellation during second list, or after list deadline and callback entry; non-cooperative consumer has an unreleased gate | ExecuteTask finishes and stream is disposed before release; one abandonment warning, one offer, cancellable callback token. | PC-115 restores unbounded None dispatch; PC-116 omits host linkage: stopped.ShouldBeTrue fails. |
| T49 / V-5,V-7 | `SessionRunnerHeldEventDeliveryTests.C1205_Blocked_held_callback_stops_at_monotonic_deadline` / 1 | Second-list deadline followed by blocked consumer; advance FakeTimeProvider exactly 3 seconds without cancelling host | Reconnect reached and stream disposed before release; callback token cancelled; one warning and one offer. | PC-117 removes deadline: disconnected.ShouldBeTrue fails. |
| T50 / V-5 | `SessionRunnerHeldEventDeliveryTests.C1205_Last_transcript_pull_cancellation_preserves_one_output` / 1 | Real HTTP/runtime final pull swallows host cancellation; consumer rejects cancelled tokens | Exactly one accepted output with original text, exactly one attempt and disposed iterator. | PC-118 removes post-sync cancellation check; PC-119 pre-cancels preservation token: Accepted.ShouldBe(1) fails. |
| T10 / V-8/R-2 | `WorkingTranscriptRecoveryTests.C1205_Complete_idle_snapshot_recovers_once` / 2 | H3; cache enabled, disabled | RecoveredIdle; one committed end; SQL idle; enabled cache agrees; replay adds 0. Reach: New checked entry point, valid generation and current runner frontier; invoke twice. | PC-11, PC-38; exact compiling defect and red below. |
| T11 / V-9 | `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` / 15 | H3; pull-fault, persist-fault, state-read-fault, empty, wrong-generation, missing-generation, wrong-session, mixed-entry-session, missing-frontier, null-uuid-frontier-mismatch, retained-failure, needs-reload, absent-store, canceled-pull, truncated-tail | Unknown; no idle certification; no new end for precommit refusals; actual committed rows disclosed for partial failure. Reach: One flip from T10; record boundary hit/fault count = 1; successful pull/persist cannot be assumed from completed Task. | PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25; exact compiling defect and red below. |
| T12 / V-10 | `WorkingTranscriptRecoveryTests.C1205_Newer_runner_activity_keeps_working` / 2 | H3; cache enabled, disabled | CheckedWorking; missing old end still absent; new turn Working. Reach: Runner has old missing end then current prompt already durable; otherwise valid complete snapshot. | PC-26; exact compiling defect and red below. |
| T13 / V-11 | `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` / 12 | H3; six flips x cache on/off: new prompt, queue annotation, generation, remote ownership, terminal status, delete/recreate frontier | Unknown; no stale end inserted; fresh durable frontier unchanged by probe. Reach: Hold snapshot response after initial lease released; commit one flip, release response, observe final gate wait. | PC-27, PC-28, PC-29, PC-30, PC-31; exact compiling defect and red below. |
| T14 / V-12 | `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` / 1 | H3; snapshot/live overlap; shared text/end UUID; low resumed sequence; null UUID replay | One row per identity, both kinds, increasing stored sequence, source timestamp preserved. Reach: Seed high stored sequence, then overlap via real PersistTranscriptAsync and checked probe. | PC-32, PC-33, PC-34, PC-35, PC-36; exact compiling defect and red below. |
| T15 / V-25 | `WorkingTranscriptRecoveryTests.C1205_Probe_holds_the_commit_gate_through_publication` / 2 | H3; cache enabled, disabled | Competing ingest enters gate wait but cannot commit until probe publication; resulting SQL verdict protects new prompt. Reach: Use Store.NextGateWait plus SaveGate at probe save; no delay-based IsCompleted check. | PC-37; exact compiling defect and red below. |
| T16 / V-26 | `WorkingTranscriptRecoveryTests.C1205_Runner_tail_classification_is_conservative` / 8 | H3; end -> Idle; new prompt -> Working; future kind -> Working; auto compact -> Working; manual compact -> Idle; null-time activity -> Working; equal-time activity -> Working; queue annotation -> Idle | Hand-written result per row; Working rows append no old end. Reach: Actual source-order rows, otherwise T10 valid proof; annotations omitted from runner coverage without veto. | PC-39, PC-40; exact compiling defect and red below. |
| T18 / V-13/R-3 | `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` / 2 | H4; IntervalSeconds 120, 180 | After success: no probe at interval-1 tick; exactly 1 at interval; due and result logs contain required metadata. Reach: Observe timer registration and completed scheduler pass; successful Working observation resets only successful-check stamp. | PC-41, PC-101, PC-102, PC-110; exact compiling defect and red below. |
| T19 / V-14 | `StaleWorkingTranscriptWatchdogTests.C1205_Wall_clock_jumps_do_not_change_due_time` / 2 | H4; UTC +6h, UTC -6h | UTC jump alone yields 0 probes; monotonic interval yields 1. Reach: Clock wrapper changes GetUtcNow offset independently of GetTimestamp/timers. | PC-42; exact compiling defect and red below. |
| T20 / V-15 | `StaleWorkingTranscriptWatchdogTests.C1205_Healthy_keepalives_do_not_reset_probe_debt` / 4 | H4; keepalive, cached state read, queue attempt, unrelated session event | Due session probes at original monotonic boundary. Reach: Real HTTP comments/real cache or queue operations; record successful-check stamp before/after. | PC-43; exact compiling defect and red below. |
| T21 / V-16 | `StaleWorkingTranscriptWatchdogTests.C1205_Fair_pages_and_single_flight_bound_work` / 1 | H4; 5 ordered cold local Working sessions; PageSize=2; first blocked | At most 2 discoveries per pass; cursor wraps; later pages and later due IDs progress while first blocked. Reach: DB command capture and ordered admitted IDs across explicit ticks; no queued notes. | PC-44, PC-45, PC-105; exact compiling defect and red below. |
| T22 / V-27 | `StaleWorkingTranscriptWatchdogTests.C1205_Concurrency_and_generation_are_bounded` / 1 | H4; 6 due sessions; MaxConcurrentProbes=2; repeated ticks | At most 2 active; at most 1 active per generation; same-session replacement waits for old owned work to join. Reach: Hold admitted HTTP requests, record active IDs, advance ticks, change generation. | PC-46, PC-47, PC-48, PC-104; exact compiling defect and red below. |
| T23 / V-28 | `StaleWorkingTranscriptWatchdogTests.C1205_Capacity_overflow_remains_visible` / 2 | H4; 1025 stable IDs at directory cap; generation churn while queued work is held | Tracked count <=1024; pending owners subset of admitted tokens and count <=1024; overflow visible; later eligible IDs progress after release. Reach: Saturate directory with held workers; in second row replace/remove queued generations before dequeue and inspect pending ownership. | PC-49, PC-50, PC-51; exact compiling defect and red below. |
| T24 / V-17 | `StaleWorkingTranscriptWatchdogTests.C1205_Failed_probe_retains_debt_and_retries` / 1 | H4; Unknown then valid idle snapshot | No success stamp/wakeup on Unknown; no retry before poll; retry at poll; recovered idle produces one wakeup. Reach: Completion barrier separates result from next tick; record due age and attempt timestamp. | PC-52, PC-53, PC-103; exact compiling defect and red below. |
| T25 / V-17 | `StaleWorkingTranscriptWatchdogTests.C1205_Overdue_operation_is_logged_and_joined` / 1 | H4/H3; non-cooperative DB save holds past probe budget | Overdue warning by next poll; discovery still advances; no replacement write; StopAsync pending until release, then joined. Reach: Save interceptor deliberately ignores cancellation; observe next-pass barrier before releasing it. | PC-54, PC-55, PC-56; exact compiling defect and red below. |
| T26 / V-18/R-4 | `StaleWorkingTranscriptWatchdogTests.C1205_Hosted_restart_discovers_working_sessions_immediately` / 1 | H4; new real host/provider twice; same DB, no warmed cache | Hosted registration resolved; due on first pass in new epoch without advancing interval. Reach: Guarded AntiphonWebAppFactory subclass, real Program registration, controlled runner; stop/join first host before rebuilding. | PC-57, PC-58; exact compiling defect and red below. |
| T27 / V-19 | `StaleWorkingTranscriptWatchdogTests.C1205_Disabled_setting_logs_the_missing_bound` / 2 | H4; feature disabled, SessionRunner disabled | 0 probes and explicit disabled-bound startup warning. Reach: Start real hosted driver with one setting flipped; await its startup-completed observation. | PC-59, PC-60, PC-61; exact compiling defect and red below. |
| T28 / V-19 | `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` / 12 | H4; interval -1, 0, 29, 3601; poll 0, 31; timeout 0, 31; concurrency 0, 5; page 0, 129 | Startup rejects exact invalid setting; zero transcript reads. Reach: Production startup options validator; one invalid scalar per row, otherwise valid defaults. | PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71; exact compiling defect and red below. |
| T29 / V-29 | `StaleWorkingTranscriptWatchdogTests.C1205_Valid_settings_limits_are_accepted` / 2 | H4; interval30/poll1/timeout1/concurrency1/page1; interval3600/poll30/timeout30/concurrency4/page128 | Startup accepted; configured values used. Reach: Actual options binding and first discovery, not isolated validator invocation. | PC-72; exact compiling defect and red below. |
| T30 / V-30 | `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` / 7 | H4; local Working no note, remote, missing session, Stopped, idle, cold-cache local Working, changed generation | Probe only positive local Running Working rows; cold/no-note included; generation loses freshness. Reach: Real DB ownership rules and SQL Working query; one flip per row; unknown discovery classified separately. | PC-73, PC-74, PC-106, PC-107, PC-108; exact compiling defect and red below. |
| T31 / V-31 | `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_failure_retains_visible_debt` / 1 | H4; discovery query throws once | Warning and retained directory/debt; bounded next poll retries, successful later discovery. Reach: DbCommandInterceptor keyed to discovery query; counter proves fault fired. | PC-75, PC-76; exact compiling defect and red below. |
| T32 / V-32 | `StaleWorkingTranscriptWatchdogTests.C1205_Probe_budget_covers_headers_body_and_retries` / 3 | H4/H2; headers hang, body hang, admitted-read backoff | Caller token cancels at configured 10s; one owned probe; no inner 100/120s extension. Reach: Real named read pipeline, deterministic jitter, controlled handler/body and timer-registration barriers. | PC-77; exact compiling defect and red below. |
| T33 / V-33 | `StaleWorkingTranscriptWatchdogTests.C1205_Recovered_idle_schedules_only_coalesced_work` / 1 | H4/H3; repeated recovery while real flush worker is blocked | One coalesced pending wake; gate can be acquired during callback; scheduler discovers another session. Reach: Recording subclass calls base CompletionNoteFlushQueue; actual worker and adapter input barrier. | PC-78, PC-79; exact compiling defect and red below. |
| T34 / V-34 | `StaleWorkingTranscriptWatchdogTests.C1205_Ownership_cleanup_is_bounded_and_conservative` / 2 | H4; confirmed terminal, ownership read uncertain | Bounded reconciliation page; remove only freshly proven ineligible entries; uncertain entry/debt retained. Reach: Seed >PageSize tracked entries and DB status changes; query/fault barriers. | PC-80, PC-81; exact compiling defect and red below. |
| T35 / V-35 | `StaleWorkingTranscriptWatchdogTests.C1205_Recovery_does_not_manufacture_idle_or_release` / 3 | H4; genuinely Working, Unknown, RecoveredIdle with parking disabled | No synthetic end, direct input, kill, interrupt, or seat release; only valid recovery may wake normal queue. Reach: Runner operation spies and DB transcript/status comparison after exact due ticks; no pending message for idle arm. | PC-82, PC-83, PC-109; exact compiling defect and red below. |
| T37 / V-20/R-5 | `TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once` / 2 | H5; cache enabled, disabled | Five complete matching caller UserPrompts and notifier confirmations; stable task/event/note/queue/digest identities; no typing while busy. Reach: Real settlement -> notification -> queue; new provider; H1 gap; watchdog held before due to isolate C2. | PC-84; exact compiling defect and red below. |
| T38 / V-21 | `TurnEndGapRestartDeliveryTests.C1205_Watchdog_delivers_while_event_stream_remains_healthy` / 1 | H5; drop later TurnEnd publication after readiness; keep snapshot and keepalives | Five complete receipts after due probe, before manual catch-up/stranded sweep. Reach: Initial readiness/C2 already drained; keepalive traffic acknowledged; real watchdog and flush workers. | PC-85; exact compiling defect and red below. |
| T39 / V-22 | `TurnEndGapRestartDeliveryTests.C1205_Restart_after_transcript_commit_recovers_lost_wakeup` / 1 | H5; commit idle end; crash before TryEnqueue | Same queued obligations reach receipts through boot recovery. Reach: Probe task held at recording flush-queue override before base; stop old services, fresh flush queue; observe completion-scan. | PC-86; exact compiling defect and red below. |
| T40 / V-23 | `TurnEndGapRestartDeliveryTests.C1205_Receipt_restart_confirms_without_retyping` / 1 | H5; UserPrompt committed; receipt-before-save fault | Same confirming sequence after fresh notifier scans twice; submission count unchanged. Reach: LandDeliveryBoundary receipt-before-save and actual matching prompt already ingested. | PC-87, PC-88; exact compiling defect and red below. |
| T41 / V-24 | `TurnEndGapRestartDeliveryTests.C1205_Busy_tail_keeps_completion_obligations_pending` / 1 | H5; newer authoritative prompt; restart and repeated due ticks | 5 Pending, 0 attempts/submissions/confirmations; no fabricated receipt. Reach: Real queue flush attempts finish while SQL/cache Working; complete snapshot available. | PC-89; exact compiling defect and red below. |
| T42 / V-36 | `TurnEndGapRestartDeliveryTests.C1205_Already_eligible_caller_receives_completion` / 1 | H5; caller durably idle at real settlement | One complete receipt; normal worker delivery with stable keys. Reach: Actual producer and enqueue path, no fixture FlushIfIdle shortcut. | PC-90; exact compiling defect and red below. |
| T43 / V-37 | `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` / 16 | H5; 8 cuts x busy/eligible: obligation-insert, settled-committed, note-insert, note-committed, render-committed, attempt-committed, wakeup-full, receipt-before-save | Cut hit once; durable identity preserved when committed; rollback leaves no obligation without task result; eventual complete receipt once; no retype of complete receipt. Reach: C544DeliveryFault/Boundary recipe and real bounded flush queue saturation; restart before any alternative worker masks cut. | PC-91, PC-92, PC-93, PC-94, PC-95; exact compiling defect and red below. |
| T44 / V-38 | `TurnEndGapRestartDeliveryTests.C1205_Unknown_probe_keeps_obligations_held` / 2 | H5; wrong-generation, pull failure | No attempt or receipt; same obligations survive restart; valid subsequent probe eventually produces complete receipts. Reach: Only watchdog enabled after initial baseline, ordinary scanner cannot pull until attempt >0. | PC-96; exact compiling defect and red below. |
| T45 / V-39 | `TurnEndGapRestartDeliveryTests.C1205_Only_complete_destination_receipts_confirm` / 4 | H5; header-only, truncated, wrong destination, below attempt floor | No confirmation or ConfirmingPromptSequence from invalid evidence. Reach: Transform actual adapter submission into runner records; prior-floor record is prepended before attempt; finish scan without direct DB receipt insert. | PC-97, PC-98, PC-99; exact compiling defect and red below. |
| T46 / V-40 | `TurnEndGapRestartDeliveryTests.C1205_Flush_rechecks_a_new_turn_after_recovery` / 1 | H5; new UserPrompt commits after recovery before worker dequeue | Pending notes with zero attempts/submissions despite recovered-idle result. Reach: Hold flush reader, ingest new prompt via runtime, release worker; query after worker-complete barrier. | PC-100; exact compiling defect and red below. |

### Guards the regression

- **R-1:** T01 remains the unchanged desired-behavior assertion from baseline red
  through CP-2 green; T09 asserts C2 commit precedes the first live callback.
- **R-2:** T10/T12/T13/T15 protect idle recovery, new-turn preservation and the same
  lease in both cache modes. New unchecked/Unknown outcomes cannot advance freshness.
- **R-3:** T18..T25/T31..T35 cover configured monotonic due time, visible debt,
  bounded discovery/probes, no re-pull storm and owned non-cooperative operations.
- **R-4:** T26 resolves the real production host registration and reconstructs
  due sessions after provider restart; an independently instantiated service alone
  is not registration evidence.
- **R-5:** T37..T46 require producer-to-recipient restart receipts and final
  Working recheck; T41/T44 are negative busy/unknown controls.
- **R-6:** Existing `SessionRunnerEventPumpTests`, 5 executions.
  `S0_blocked_hosted_pump_probe` has four literal Arguments rows
  (signalr-publish/false/false, queue-confirm/false/false,
  queue-confirm/true/false, signalr-publish/false/true).
  Held callback -> dispatch subsequent event on another task -> red at
  `persistedWhileBlocked.ShouldBeFalse`. Preserve this characterization of
  CARD-0432, not a desired parallel-pump rewrite.
  `C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition`
  has one execution: stale generation exit -> remove runtime generation veto ->
  red at durable session Status=Running. New T06 supplies first-event preservation;
  this existing test's 500ms delay is not used as H1's subscription proof.
- **R-7:** Existing commit and SQL-classifier compatibility: 20 executions below.
  No changes to these bodies are planned. Their diagnostic mutation/red statements
  explain the compatibility oracle; the commissioned PCs for the changed recovery
  entry point are the numbered inventory below.
- **R-8:** Existing `VerificationRoundDeliveryTests`: three named executions.
  `C544_CompletionReceipt`: real settlement/queue/full receipt -> omit notification
  reconciliation -> red at State=Confirmed (its busy/spill/distill loops are 1 test).
  `C544_CompletionReceiptWholeWire`: prefix -> accept header prefix -> red at
  State!=Confirmed; `C544_StampIsNotReceipt`: failed sibling enqueue -> use sibling
  CompletionNoteQueuedAt as receipt -> red at later task's complete prompt count=1.
  Their direct-SQL fake receipts are compatibility evidence, not H5 evidence.
- **R-9:** Existing `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`,
  one execution from `tests/Shared/TestClassificationGuardTests.cs`: compiled Slow
  metadata -> remove the new fully qualified registry entry -> red at
  `lines.ShouldBeEmpty(errors)`. This is a test-metadata compatibility check,
  not another recovery-safety guard or commissioned PC.

R-7 current exact names, each one execution:

| Existing class/method | Case -> diagnostic mutation -> decisive red |
|---|---|
| SessionStateCommitTests.Readers_wait_for_commit_and_then_observe_the_published_revision | Held save -> publish before commit -> reader completes before commit (G-37). |
| SessionStateCommitTests.Failed_save_does_not_advance_snapshot_or_revision | Reject save -> publish proposed rows -> before/after snapshot equality fails (G-13). |
| SessionStateCommitTests.Partial_fallback_folds_only_successful_rows_once | Poison end rejected -> fold proposed end -> EndSequence null assertion fails (G-13/G-22). |
| SessionStateCommitTests.Persist_stub_uses_stored_classification_and_preserves_source_callback_flags | Compact stub -> classify original manual text -> Working true fails. |
| SessionStateCommitTests.Unique_collision_recovers_actual_durable_row_and_sequence | Competing row sequence17 -> report proposed sequence1 -> LastStoredSeq=17 fails (G-35). |
| SessionStateCommitTests.Ambiguous_post_commit_exception_reloads_durable_state | Postcommit exception -> retain cached count0 -> durable count1 fails (G-13/G-22). |
| SessionStateCommitTests.Synthetic_restart_boundary_uses_the_commit_gate_and_is_idempotent | Replay restart boundary -> append twice -> count2 equality fails (G-34/G-37). |
| SessionStateCommitTests.Concurrent_ingests_rebase_sequences_and_warmed_queue_observes_each_commit | 12 concurrent low-sequence rows -> omit rebase -> count12/lastSequence12 fails (G-33/G-35). |
| TranscriptWorkingStateQueryTests.Empty_input_emits_no_sql | Empty IDs -> query anyway -> captured reads empty fails. |
| TranscriptWorkingStateQueryTests.Missing_and_empty_sessions_are_idle | Empty session -> default Working -> expected false fails. |
| TranscriptWorkingStateQueryTests.No_end_activity_is_working | No boundary + future activity -> ignore unknown kinds -> expected true fails (G-39). |
| TranscriptWorkingStateQueryTests.No_end_housekeeping_is_idle | Housekeeping only -> count queue annotations as activity -> expected false fails (G-40). |
| TranscriptWorkingStateQueryTests.Turn_end_and_restart_boundary_end_work | End then new activity -> ignore post-end activity -> expected true fails (G-26). |
| TranscriptWorkingStateQueryTests.Interrupt_prefix_ends_work_but_mentions_do_not | Embedded interrupt mention -> Contains instead of prefix -> expected true fails. |
| TranscriptWorkingStateQueryTests.Manual_compact_and_continuation_are_idle | Manual compact + continuation -> count continuation as activity -> expected false fails. |
| TranscriptWorkingStateQueryTests.Auto_and_unknown_compact_do_not_end_work | Auto compact -> treat compact of any kind as end -> expected true fails. |
| TranscriptWorkingStateQueryTests.Local_and_queue_records_are_inert_but_raw_slash_and_unknown_kinds_are_activity | Raw slash prompt -> suppress slash prompts -> expected true fails. |
| TranscriptWorkingStateQueryTests.Stale_replay_requires_one_qualifying_row_not_independent_activity_maxima | Stale replay -> independent timestamp/sequence maxima -> expected false fails (G-36). |
| TranscriptWorkingStateQueryTests.Null_and_equal_timestamps_preserve_conservative_working | Null/equal activity time -> use strict timestamp comparison -> expected true fails. |
| TranscriptWorkingStateQueryTests.End_sequence_and_timestamp_maxima_can_come_from_different_rows | Mixed end sequence/time extrema -> pair both from one row -> literal expected false fails. |

Those unchanged SQL semantics, native release mechanisms and generalized pump callback
isolation are compatibility boundaries, not new implementations to mutate under this
card. New T16 carries explicit representative classification assertions; if Code
changes shared classification instead of reusing it, this frozen scope is insufficient
and must return to Plan/TestDesign for the expanded guards.

### Guard inventory

The inventory covers the plan's new/reused recovery and delivery safety assertions.
A guard with multiple independently bypassable predicates has separate rows.
Configuration's dominated poll/interval relationship and pending's derived capacity
are justified in the reachability audit, not counted as untested independent guards.
Existing unchanged internals beyond these boundaries remain the stated exclusions.

| Guard | Plan reference and safety-critical guard/invariant | Positive control |
|---|---|---|
| G-1 | D-1/D-2: Post-subscription catch-up closes quiet gap | PC-1 |
| G-2 | D-1: Grace control uses actual fetch/persist | PC-2 |
| G-3 | D-2: Headers/connected do not certify subscription | PC-3 |
| G-4 | D-2: Readiness requires exact keepalive or supported parsed event | PC-4 |
| G-5 | D-2: Readiness emitted once per connection | PC-5 |
| G-6 | D-2: First parsed event retained | PC-6 |
| G-7 | D-2: Reconnect resets readiness | PC-7 |
| G-8 | D-2/D-10: Cancellation/fault releases HTTP resource ownership | PC-8 |
| G-9 | D-2: C2 is awaited before first live dispatch | PC-9 |
| G-10 | D-2: Internal marker is not transcript/publication | PC-10 |
| G-111 | D-2: Held first event survives a second-list timeout | PC-111 |
| G-112 | D-2: Held first event survives host cancellation during the second list | PC-112 |
| G-113 | D-2: A throwing second list does not reconnect before the held event | PC-113 |
| G-114 | D-2: Readiness marker keeps the parsed event for failed catch-up delivery | PC-114 |
| G-11 | D-7 step 5: RecoveredIdle follows committed persistence | PC-11 |
| G-12 | D-7 step 1: Pull failure is Unknown | PC-12 |
| G-13 | D-7 step 5: Failed save cannot certify recovery | PC-13 |
| G-14 | D-7 step 5: State read failure is Unknown | PC-14 |
| G-15 | D-7 step 2: Empty snapshot is insufficient evidence | PC-15 |
| G-16 | D-7 step 2: Generation equals accepted generation | PC-16 |
| G-17 | D-7 step 2: Missing generation is not positive evidence | PC-17 |
| G-18 | D-7 step 2: Snapshot destination matches session | PC-18 |
| G-19 | D-7 step 2: Contained entries belong to destination | PC-19 |
| G-20 | D-7 step 2: Snapshot covers durable activity identity | PC-20 |
| G-21 | D-7 step 2: Legacy null-UUID frontier needs record identity | PC-21 |
| G-22 | D-7 step 5: Retained persistence failure vetoes certification | PC-22 |
| G-23 | D-7 step 4: Absent shared store cannot recover outside its gate | PC-23 |
| G-24 | D-7 step 5: Canceled attempt cannot stamp success | PC-24 |
| G-25 | D-7 step 2: Known snapshot truncation is not complete coverage | PC-25 |
| G-26 | D-7 step 3: Busy authoritative tail defers old-end backfill | PC-26 |
| G-27 | D-7 step 4: Concurrent committed transcript revision invalidates snapshot before append | PC-27 |
| G-28 | D-7 step 4: Generation revalidated after pull | PC-28 |
| G-29 | D-7 step 4: Local ownership revalidated after pull | PC-29 |
| G-30 | D-7 step 4: Running status revalidated after pull | PC-30 |
| G-31 | D-7 step 4: Frontier removal/replacement cannot reuse a stamp | PC-31 |
| G-32 | D-3: UUID identity includes kind | PC-32 |
| G-33 | D-3: Low resumed sequence does not discard UUID rows | PC-33 |
| G-34 | D-3: Overlapping identical rows deduplicate | PC-34 |
| G-35 | D-3: Stored sequences rebase monotonically | PC-35 |
| G-36 | D-3: Producer timestamps retained | PC-36 |
| G-37 | D-7 step 4: Same session lease covers final check, save and publication in both cache modes | PC-37 |
| G-38 | D-7 step 4: Disabled cache uses fresh SQL verdict | PC-38 |
| G-39 | D-7 step 3: Tail classifier recognizes qualifying future activity conservatively | PC-39 |
| G-40 | D-7 step 2: Server-only annotations do not veto runner coverage | PC-40 |
| G-41 | D-4: Configured interval controls due threshold | PC-41 |
| G-42 | D-3/D-4: Due elapsed time is monotonic | PC-42 |
| G-43 | D-4: Unrelated liveness cannot reset authoritative check age | PC-43 |
| G-44 | D-5: One keyset discovery page per tick | PC-44 |
| G-45 | D-5: Discovery cursor progresses fairly | PC-45 |
| G-46 | D-5: Active probes obey configured concurrency | PC-46 |
| G-47 | D-5: Same generation is single flight | PC-47 |
| G-48 | D-5: Replacement generation does not abandon old owned work | PC-48 |
| G-49 | D-5: Tracked recovery directory has capacity 1024 | PC-49 |
| G-50 | D-5: Pending work remains owned by admitted generation tokens (derived <=1024 cap) | PC-50 |
| G-51 | D-5/D-6: Overflow is reported and does not silently erase debt | PC-51 |
| G-52 | D-4/D-7: Unknown preserves success-check debt | PC-52 |
| G-53 | D-5: Unknown retry has poll backoff | PC-53 |
| G-54 | D-5/D-6: Hung operation cannot block independent discovery/log loop | PC-54 |
| G-55 | D-6: Overdue owned operation reports debt | PC-55 |
| G-56 | D-10: Shutdown joins owned probe even when cancellation is ignored | PC-56 |
| G-57 | D-4/D-9: Real production hosted registration and boot discovery | PC-57 |
| G-58 | D-4/D-9: New server epoch has immediate unknown freshness | PC-58 |
| G-59 | D-4: Feature disable blocks probes | PC-59 |
| G-60 | D-5: Transport disable blocks probes | PC-60 |
| G-61 | D-6: Disabled bound is visible | PC-61 |
| G-62 | D-4 settings: IntervalSeconds lower bound 30 | PC-62 |
| G-63 | D-4 settings: IntervalSeconds upper bound 3600 | PC-63 |
| G-64 | D-4 settings: PollSeconds lower bound 1 | PC-64 |
| G-65 | D-4 settings: PollSeconds upper bound 30 | PC-65 |
| G-66 | D-4 settings: ProbeTimeoutSeconds lower bound 1 | PC-66 |
| G-67 | D-4 settings: ProbeTimeoutSeconds upper bound 30 | PC-67 |
| G-68 | D-4 settings: MaxConcurrentProbes lower bound 1 | PC-68 |
| G-69 | D-4 settings: MaxConcurrentProbes upper bound 4 | PC-69 |
| G-70 | D-4 settings: PageSize lower bound 1 | PC-70 |
| G-71 | D-4 settings: PageSize upper bound 128 | PC-71 |
| G-72 | D-4 settings: Valid extremes are admitted | PC-72 |
| G-73 | D-5: Discovery includes cold local Working without queued obligations | PC-73 |
| G-74 | D-5: Discovery excludes remote/terminal/missing/idle using positive eligibility | PC-74 |
| G-75 | D-6: Discovery failure logs and retains directory/debt | PC-75 |
| G-76 | D-6: Discovery failure is observable | PC-76 |
| G-77 | D-4 timeout: Probe caller budget includes HTTP retries and body reads | PC-77 |
| G-78 | D-7 step 6: Recovered idle schedules ordinary coalesced wakeup | PC-78 |
| G-79 | D-7 step 6: Queue callback runs after commit lease released | PC-79 |
| G-80 | D-5: Cleanup work bounded per pass | PC-80 |
| G-81 | D-5: Only fresh proof removes stale ownership entries | PC-81 |
| G-82 | D-7/D-10: Elapsed age cannot synthesize idle | PC-82 |
| G-83 | D-10: Recovery cannot stop/interrupt/release sessions | PC-83 |
| G-84 | D-9: Gap recovery reaches actual complete destination receipt | PC-84 |
| G-85 | D-4/D-7: Independent watchdog delivers despite healthy SSE | PC-85 |
| G-86 | D-9: Boot sweep replaces lost flush wakeup | PC-86 |
| G-87 | D-9: Stored complete receipt reconciles after confirmation-save loss | PC-87 |
| G-88 | D-9: Recovery does not retype already complete receipt | PC-88 |
| G-89 | D-7/D-10: Ordinary Working gate holds pending completion | PC-89 |
| G-90 | D-9: Eligible producer uses real queue wakeup | PC-90 |
| G-91 | D-9: Task settlement and obligation creation are atomic | PC-91 |
| G-92 | D-9: Failed keyed enqueue retries from same obligation | PC-92 |
| G-93 | D-9: Committed keyed queue is reused after lost acknowledgement | PC-93 |
| G-94 | D-9: Full in-memory flush channel requests durable recovery | PC-94 |
| G-95 | D-9: Claim/render lost before submission is recovered | PC-95 |
| G-96 | D-7: Unknown result cannot trigger wakeup/delivery | PC-96 |
| G-97 | D-9: Receipt requires complete wire text | PC-97 |
| G-98 | D-9: Receipt bound to destination session | PC-98 |
| G-99 | D-9: Receipt is above attempt floor | PC-99 |
| G-100 | D-7 step 6: Flush rechecks current Working after recovery | PC-100 |
| G-101 | D-6: Due and result firings leave metadata-only log evidence | PC-101 |
| G-102 | D-6: Checked outcome is logged with committed sequence/count | PC-102 |
| G-103 | D-6: Unknown/timeout result warning preserves visible recovery debt | PC-103 |
| G-104 | D-6: Due debt waiting behind busy workers is reported by independent loop | PC-104 |
| G-105 | D-5: Fair due queue gives eligible peers a turn after failed oldest probe | PC-105 |
| G-106 | D-5: Discovery status requires Running | PC-106 |
| G-107 | D-5: Discovery requires durable Working verdict | PC-107 |
| G-108 | D-4: Generation change revokes previous successful freshness | PC-108 |
| G-109 | D-7 step 6: Recovery cannot submit input directly | PC-109 |
| G-110 | D-4: Nondefault configured interval is honored | PC-110 |

### Positive controls

**Status: specified, execution pending/paused.** Code runs V/R; ordinary Review judges
this design and compiled bindings before land. On unpause after confirmed land,
Mutation must run the **whole** inventory, method-scoped, as baseline green -> break ->
expected assertion red -> restore exact source -> restored green. No PC is credited
here as run. The later completion gate is 114/114 cycles executed with provenance.
PC-111..PC-114 are the held-event repair controls. This TestDesign handoff is not a
claim of verification completion.

Each row below binds a concrete compiling-defect recipe to one named test method.
Method filters are `/*/*/<Class>/<ExactMethod>*` (trailing wildcard includes the
parameterized cases). The T row supplies the exact method execution floor; all
argument rows run in each phase, and the named failing variant must be present in
fresh TRX. `ShouldBeStrictlyIncreasing` prose, where used, means explicit adjacent
pair comparisons in Shouldly, not a dependency on an unavailable assertion API.
Mutation of a non-existing branch remains **binding required after Code**:
record landed file/symbol, exact patch, assertion message and source/build hashes.
A test compile error, failed fixture, hang guard, zero executions, a different
assertion or a swallowed fault is not the designed red. Fault counters and barrier
traces establish that the intended branch was reached.

Each PC is independent even when several target the same method; restore before
the next mutant. No class/suite mutation filter, no source edits during a run, and
no widening timeout or weakening expected values. Keep results ignored/external
under SourceLanding custody. All builds/tests use the host slot and isolated
forward-slash OutputPath. Refresh restored timestamps or cleanly rebuild to avoid
a stale mutant DLL.

| PC | Break guard by compiling defect | Exact red method (T roster gives filter floor) | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: Remove pump readiness branch await of CatchUpTranscriptsAsync. | T01: `TurnEndGapReproductionTests.C1205_Gap_end_is_stored_before_any_grace_pull` | preGraceEndCount.ShouldBe(1). |
| PC-2 | G-2: Return false from CatchUpTranscriptAsync without PullAndPersistAsync. | T02: `TurnEndGapReproductionTests.C1205_Grace_pull_recovers_the_unsubscribed_tail` | postGraceEndCount.ShouldBe(1). |
| PC-3 | G-3: Emit readiness on exact connected comment before keepalive. | T03: `SessionRunnerStreamReadinessTests.C1205_Connected_comment_is_not_readiness` | markerCount.ShouldBe(0). |
| PC-4 | G-4: Change exact keepalive check to StartsWith(":"). | T04: `SessionRunnerStreamReadinessTests.C1205_Unknown_comment_is_not_readiness` | markerCount.ShouldBe(0) in unknown comment row. |
| PC-5 | G-5: Remove readiness-emitted condition from keepalive branch. | T05: `SessionRunnerStreamReadinessTests.C1205_Keepalive_emits_readiness_once` | markerCount.ShouldBe(1). |
| PC-6 | G-6: Delete yield of held parsed event after yielding marker. | T06: `SessionRunnerStreamReadinessTests.C1205_First_event_follows_readiness_without_loss` | events.Count.ShouldBe(2); second is original event. |
| PC-7 | G-7: Move readiness latch to client field and preserve it across iterator calls. | T07: `SessionRunnerStreamReadinessTests.C1205_Reconnect_rearms_readiness` | secondConnectionMarkers.ShouldBe(1). |
| PC-8 | G-8: Remove using response and both stream/reader disposal scopes, preserving declarations. | T08: `SessionRunnerStreamReadinessTests.C1205_Stream_failure_and_cancellation_release_owned_resources` | streamDisposed.ShouldBeTrue() in both rows. |
| PC-9 | G-9: Replace await of C2 with discarded Task. | T09: `SessionRunnerStreamReadinessTests.C1205_Catchup_finishes_before_live_dispatch` | liveCallbacksAtSaveBarrier.ShouldBeEmpty(). |
| PC-10 | G-10: Dispatch marker through ObserveOutputAsync with marker text instead of consume-only handling. | T05: `SessionRunnerStreamReadinessTests.C1205_Keepalive_emits_readiness_once` | publicationsWithMarker.ShouldBeEmpty(). |
| PC-11 | G-11: Return RecoveredIdle without invoking PersistTranscriptCoreAsync. | T10: `WorkingTranscriptRecoveryTests.C1205_Complete_idle_snapshot_recovers_once` | storedEndCount.ShouldBe(1). |
| PC-12 | G-12: In checked probe catch(HttpRequestException), return RecoveredIdle. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for pull-fault. |
| PC-13 | G-13: Ignore PersistResult.NeedsReload on the 23505 reload path with no retained failure. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for needs-reload. |
| PC-14 | G-14: Convert fresh verdict-read exception to idle success. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for state-read-fault. |
| PC-15 | G-15: Return RecoveredIdle early for an empty snapshot, bypassing frontier validation. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for empty. |
| PC-16 | G-16: Remove non-null generation equality veto. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for wrong-generation. |
| PC-17 | G-17: Coalesce snapshot.AcceptedStartedAt to captured generation. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for missing-generation. |
| PC-18 | G-18: Remove DTO SessionId equality check. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for wrong-session. |
| PC-19 | G-19: Remove entry SessionId validation. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for mixed-entry-session. |
| PC-20 | G-20: Replace UUID/kind frontier membership check with LastSequence >= stored maximum. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for missing-frontier with high sequence. |
| PC-21 | G-21: Accept legacy frontier on sequence alone despite changed kind/text/time. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for null-uuid-frontier-mismatch. |
| PC-22 | G-22: Delete TryGetTranscriptPersistFailure veto. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for retained-failure. |
| PC-23 | G-23: On null _states delegate to unguarded pull/persist and return success. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for absent-store. |
| PC-24 | G-24: Catch cancellation and return RecoveredIdle. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for canceled-pull. |
| PC-25 | G-25: Ignore snapshot LastSequence beyond final source entry while frontier is present. | T11: `WorkingTranscriptRecoveryTests.C1205_Uncertain_probe_does_not_certify_idle` | classification.ShouldBe(Unknown) for truncated-tail. |
| PC-26 | G-26: Persist snapshot before testing runner-tail Working. | T12: `WorkingTranscriptRecoveryTests.C1205_Newer_runner_activity_keeps_working` | newOldEndCount.ShouldBe(0). |
| PC-27 | G-27: Remove captured revision/frontier comparison, retaining identity checks. | T13: `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` | newOldEndCount.ShouldBe(0) for new-prompt and queue-annotation. |
| PC-28 | G-28: Remove final accepted-generation comparison. | T13: `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` | classification.ShouldBe(Unknown) for generation. |
| PC-29 | G-29: Remove final routing-binding comparison. | T13: `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` | classification.ShouldBe(Unknown) for remote ownership. |
| PC-30 | G-30: Remove final Running check. | T13: `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` | classification.ShouldBe(Unknown) for terminal status. |
| PC-31 | G-31: Replace full captured-stamp equality with only max-sequence/count equality. | T13: `WorkingTranscriptRecoveryTests.C1205_Concurrent_commit_invalidates_snapshot` | classification.ShouldBe(Unknown) for delete/recreate frontier. |
| PC-32 | G-32: Project seen identity to UUID only in persistence dedupe. | T14: `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | storedKindsForSharedUuid.ShouldBe([AssistantText,TurnEnd]). |
| PC-33 | G-33: Skip UUID-bearing source rows with e.Sequence <= maxSeq. | T14: `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | newUuidPresent.ShouldBeTrue(). |
| PC-34 | G-34: Skip seenUuids membership check for incoming UUID rows. | T14: `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | rowsForIdentity.Count.ShouldBe(1). |
| PC-35 | G-35: Assign storedSeq=e.Sequence without rebase. | T14: `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | newUuidPresent.ShouldBeTrue() and stored sequence above the prior maximum. |
| PC-36 | G-36: Assign Timestamp=now instead of normalized producer Timestamp. | T14: `WorkingTranscriptRecoveryTests.C1205_Overlap_preserves_uuid_kind_and_rebased_sequence` | storedTimestamp.ShouldBe(sourceTimestamp). |
| PC-37 | G-37: Dispose probe lease immediately before persistence core. | T15: `WorkingTranscriptRecoveryTests.C1205_Probe_holds_the_commit_gate_through_publication` | competingCommitAtProbeSaveBarrier.ShouldBeFalse(). |
| PC-38 | G-38: When _states.Enabled is false return Unknown instead of reading SQL. | T10: `WorkingTranscriptRecoveryTests.C1205_Complete_idle_snapshot_recovers_once` | classification.ShouldBe(RecoveredIdle) in disabled row. |
| PC-39 | G-39: Treat unrecognized kind after old end as housekeeping. | T16: `WorkingTranscriptRecoveryTests.C1205_Runner_tail_classification_is_conservative` | classification.ShouldBe(CheckedWorking) for future-kind. |
| PC-40 | G-40: Require QueueEnqueue identity in runner snapshot frontier. | T16: `WorkingTranscriptRecoveryTests.C1205_Runner_tail_classification_is_conservative` | classification.ShouldBe(RecoveredIdle) for annotation. |
| PC-41 | G-41: Change due comparison from >= configured interval to > configured interval. | T18: `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` | probeAtExactInterval.ShouldBe(1). |
| PC-42 | G-42: Use GetUtcNow minus last-success UTC as age. | T19: `StaleWorkingTranscriptWatchdogTests.C1205_Wall_clock_jumps_do_not_change_due_time` | probeBeforeMonotonicDue.ShouldBe(0) forward; probeAtDue.ShouldBe(1) backward. |
| PC-43 | G-43: Refresh success stamp on observed stream keepalive (then bind separate refresh branches if implemented). | T20: `StaleWorkingTranscriptWatchdogTests.C1205_Healthy_keepalives_do_not_reset_probe_debt` | probeAtOriginalDue.ShouldBe(1) for keepalive. |
| PC-44 | G-44: Loop discovery pages to exhaustion inside one tick. | T21: `StaleWorkingTranscriptWatchdogTests.C1205_Fair_pages_and_single_flight_bound_work` | discoveryRowsThisTick.ShouldBeLessThanOrEqualTo(2). |
| PC-45 | G-45: Reset keyset cursor to null at each tick. | T21: `StaleWorkingTranscriptWatchdogTests.C1205_Fair_pages_and_single_flight_bound_work` | admittedIds.ShouldContain(lastPageId). |
| PC-46 | G-46: Use configured cap + 1 for worker admission. | T22: `StaleWorkingTranscriptWatchdogTests.C1205_Concurrency_and_generation_are_bounded` | peakActive.ShouldBeLessThanOrEqualTo(2). |
| PC-47 | G-47: Remove active-generation membership veto. | T22: `StaleWorkingTranscriptWatchdogTests.C1205_Concurrency_and_generation_are_bounded` | peakActiveForGeneration.ShouldBe(1). |
| PC-48 | G-48: Remove per-session old-generation ownership veto on replacement. | T22: `StaleWorkingTranscriptWatchdogTests.C1205_Concurrency_and_generation_are_bounded` | replacementStartedBeforeOldJoin.ShouldBeFalse(). |
| PC-49 | G-49: Raise only tracked admission cap to 1025. | T23: `StaleWorkingTranscriptWatchdogTests.C1205_Capacity_overflow_remains_visible` | trackedCount.ShouldBeLessThanOrEqualTo(1024). |
| PC-50 | G-50: Retain a pending old-generation token after its tracked generation is removed. | T23: `StaleWorkingTranscriptWatchdogTests.C1205_Capacity_overflow_remains_visible` | pendingOwners.Except(trackedOwners).ShouldBeEmpty() during generation churn. |
| PC-51 | G-51: Suppress overflow warning while retaining capacity limit. | T23: `StaleWorkingTranscriptWatchdogTests.C1205_Capacity_overflow_remains_visible` | overflowWarnings.ShouldContain(deferredId). |
| PC-52 | G-52: Stamp last-success on Unknown result. | T24: `StaleWorkingTranscriptWatchdogTests.C1205_Failed_probe_retains_debt_and_retries` | dueAgeAfterUnknown.ShouldBe(originalDebtAge). |
| PC-53 | G-53: Clear last-attempt timestamp on failure. | T24: `StaleWorkingTranscriptWatchdogTests.C1205_Failed_probe_retains_debt_and_retries` | attemptCountBeforePoll.ShouldBe(1). |
| PC-54 | G-54: Await entire probe batch inside scheduler tick. | T25: `StaleWorkingTranscriptWatchdogTests.C1205_Overdue_operation_is_logged_and_joined` | nextDiscoveryPassCompleted.ShouldBeTrue() at independent loop barrier. |
| PC-55 | G-55: Delete overdue warning emission. | T25: `StaleWorkingTranscriptWatchdogTests.C1205_Overdue_operation_is_logged_and_joined` | overdueWarnings.ShouldContain(sessionId). |
| PC-56 | G-56: Remove await of active probes from Stop/Dispose path. | T25: `StaleWorkingTranscriptWatchdogTests.C1205_Overdue_operation_is_logged_and_joined` | stopCompletedBeforeRelease.ShouldBeFalse(). |
| PC-57 | G-57: Remove hosted registration from Program. | T26: `StaleWorkingTranscriptWatchdogTests.C1205_Hosted_restart_discovers_working_sessions_immediately` | registeredRecoveryHosts.Count.ShouldBe(1). |
| PC-58 | G-58: Initialize last-success to current timestamp on discovery. | T26: `StaleWorkingTranscriptWatchdogTests.C1205_Hosted_restart_discovers_working_sessions_immediately` | firstPassProbeCount.ShouldBe(1). |
| PC-59 | G-59: Ignore StaleWorkingTranscript.Enabled. | T27: `StaleWorkingTranscriptWatchdogTests.C1205_Disabled_setting_logs_the_missing_bound` | probeCount.ShouldBe(0) for feature-disabled. |
| PC-60 | G-60: Ignore SessionRunner.Enabled. | T27: `StaleWorkingTranscriptWatchdogTests.C1205_Disabled_setting_logs_the_missing_bound` | probeCount.ShouldBe(0) for transport-disabled. |
| PC-61 | G-61: Remove startup disabled warning. | T27: `StaleWorkingTranscriptWatchdogTests.C1205_Disabled_setting_logs_the_missing_bound` | disabledWarnings.Count.ShouldBe(1) in each row. |
| PC-62 | G-62: Delete only IntervalSeconds lower-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for IntervalSeconds below 30. |
| PC-63 | G-63: Delete only IntervalSeconds upper-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for IntervalSeconds above 3600. |
| PC-64 | G-64: Delete only PollSeconds lower-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for PollSeconds below 1. |
| PC-65 | G-65: Delete only PollSeconds upper-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for PollSeconds above 30. |
| PC-66 | G-66: Delete only ProbeTimeoutSeconds lower-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for ProbeTimeoutSeconds below 1. |
| PC-67 | G-67: Delete only ProbeTimeoutSeconds upper-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for ProbeTimeoutSeconds above 30. |
| PC-68 | G-68: Delete only MaxConcurrentProbes lower-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for MaxConcurrentProbes below 1. |
| PC-69 | G-69: Delete only MaxConcurrentProbes upper-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for MaxConcurrentProbes above 4. |
| PC-70 | G-70: Delete only PageSize lower-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for PageSize below 1. |
| PC-71 | G-71: Delete only PageSize upper-bound validation. | T28: `StaleWorkingTranscriptWatchdogTests.C1205_Invalid_settings_are_rejected` | startupRejected.ShouldBeTrue() for PageSize above 128. |
| PC-72 | G-72: Change interval upper bound from 3600 to 3599. | T29: `StaleWorkingTranscriptWatchdogTests.C1205_Valid_settings_limits_are_accepted` | startupAccepted.ShouldBeTrue() for valid maximum row. |
| PC-73 | G-73: Join candidates to pending notes (or enumerate only warm cache). | T30: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` | probedIds.ShouldContain(coldNoNoteSession). |
| PC-74 | G-74: Remove explicit local-binding condition only. | T30: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` | probedIds.ShouldNotContain(remoteSession). |
| PC-75 | G-75: Clear tracked entries in discovery catch block. | T31: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_failure_retains_visible_debt` | retainedDebtIds.ShouldContain(previouslyDueSession). |
| PC-76 | G-76: Suppress discovery-failure warning. | T31: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_failure_retains_visible_debt` | warnings.ShouldContain(discoveryFailureCode). |
| PC-77 | G-77: Use CancellationToken.None for checked GetTranscriptAsync. | T32: `StaleWorkingTranscriptWatchdogTests.C1205_Probe_budget_covers_headers_body_and_retries` | httpCancellationObservedAt10Seconds.ShouldBeTrue() in headers/body/backoff rows. |
| PC-78 | G-78: Omit CompletionNoteFlushQueue.TryEnqueue on RecoveredIdle. | T33: `StaleWorkingTranscriptWatchdogTests.C1205_Recovered_idle_schedules_only_coalesced_work` | recordedWakeups.ShouldContain(sessionId). |
| PC-79 | G-79: Move flush callback inside lease scope and await it. | T33: `StaleWorkingTranscriptWatchdogTests.C1205_Recovered_idle_schedules_only_coalesced_work` | gateAcquiredDuringFlush.ShouldBeTrue(). |
| PC-80 | G-80: Remove Take(PageSize) from reconciliation query/loop. | T34: `StaleWorkingTranscriptWatchdogTests.C1205_Ownership_cleanup_is_bounded_and_conservative` | reconciledThisTick.ShouldBeLessThanOrEqualTo(pageSize). |
| PC-81 | G-81: Treat ownership read exception as terminal removal. | T34: `StaleWorkingTranscriptWatchdogTests.C1205_Ownership_cleanup_is_bounded_and_conservative` | trackedIds.ShouldContain(uncertainSession). |
| PC-82 | G-82: Append SessionRestartBoundary when due without evidence. | T35: `StaleWorkingTranscriptWatchdogTests.C1205_Recovery_does_not_manufacture_idle_or_release` | syntheticBoundaries.ShouldBeEmpty(). |
| PC-83 | G-83: Call existing runner KillAsync in due Working arm. | T35: `StaleWorkingTranscriptWatchdogTests.C1205_Recovery_does_not_manufacture_idle_or_release` | killCalls.ShouldBeEmpty(). |
| PC-84 | G-84: Suppress queue wakeup from Sync's recovered boundary. | T37: `TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once` | completeReceiptCount.ShouldBe(5). |
| PC-85 | G-85: Return from the watchdog due pass before admitting probes; keep the SSE pump unchanged. | T38: `TurnEndGapRestartDeliveryTests.C1205_Watchdog_delivers_while_event_stream_remains_healthy` | confirmedReceiptCount.ShouldBe(5). |
| PC-86 | G-86: Skip CompletionNoteWorkHostedService sweep's ScheduleFlush for target destination. | T39: `TurnEndGapRestartDeliveryTests.C1205_Restart_after_transcript_commit_recovers_lost_wakeup` | completeReceiptCount.ShouldBe(5). |
| PC-87 | G-87: Set found receipt evidence to null in notification reconciler. | T40: `TurnEndGapRestartDeliveryTests.C1205_Receipt_restart_confirms_without_retyping` | confirmedSequence.ShouldBe(originalReceiptSequence). |
| PC-88 | G-88: Skip late receipt check and requeue completed attempt. | T40: `TurnEndGapRestartDeliveryTests.C1205_Receipt_restart_confirms_without_retyping` | submissionCountAfterRestart.ShouldBe(submissionCountBeforeRestart). |
| PC-89 | G-89: Replace !ReadWorkingAsync condition in FlushIfIdleAsync with true. | T41: `TurnEndGapRestartDeliveryTests.C1205_Busy_tail_keeps_completion_obligations_pending` | submittedBodies.ShouldBeEmpty(). |
| PC-90 | G-90: Drop notification producer's wakeup after successful keyed queue admission. | T42: `TurnEndGapRestartDeliveryTests.C1205_Already_eligible_caller_receives_completion` | completeReceiptCount.ShouldBe(1). |
| PC-91 | G-91: Omit TaskCompletion obligation insert during settlement. | T43: `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` | notificationCount.ShouldBe(1) after restart of settled-committed row. |
| PC-92 | G-92: Exclude RetryPending from notification scan. | T43: `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` | completeReceiptCount.ShouldBe(1) for note-insert row. |
| PC-93 | G-93: In ReconcileAsync's existing-key branch, return before assigning and saving the recovered QueueMessageId. | T43: `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` | notification.QueueMessageId.ShouldBe(originalQueueId) for note-committed row. |
| PC-94 | G-94: Remove Recovery.RequestSweep in TryEnqueue full-channel path. | T43: `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` | sameEpochReceiptCount.ShouldBe(1) for wakeup-full control before the restart arm. |
| PC-95 | G-95: Exclude interrupted Sent row from stranded-retry eligibility. | T43: `TurnEndGapRestartDeliveryTests.C1205_Restart_recovers_each_delivery_handoff` | completeReceiptCount.ShouldBe(1) for render-committed row. |
| PC-96 | G-96: Treat Unknown as RecoveredIdle in scheduler result dispatch. | T44: `TurnEndGapRestartDeliveryTests.C1205_Unknown_probe_keeps_obligations_held` | wakeupCount.ShouldBe(0) before subsequent good probe. |
| PC-97 | G-97: Accept header prefix instead of whole-wire PromptSubmissionMatch. | T45: `TurnEndGapRestartDeliveryTests.C1205_Only_complete_destination_receipts_confirm` | confirmed.ShouldBeFalse() for header-only/truncated. |
| PC-98 | G-98: Remove scanSession filter from LandNoteReceipt.Prompts. | T45: `TurnEndGapRestartDeliveryTests.C1205_Only_complete_destination_receipts_confirm` | confirmed.ShouldBeFalse() for wrong-destination. |
| PC-99 | G-99: Drop baseline sequence predicate in LandNoteReceipt.Prompts. | T45: `TurnEndGapRestartDeliveryTests.C1205_Only_complete_destination_receipts_confirm` | confirmed.ShouldBeFalse() for below-floor. |
| PC-100 | G-100: Use stale RecoveredIdle verdict to bypass queue's fresh Working read. | T46: `TurnEndGapRestartDeliveryTests.C1205_Flush_rechecks_a_new_turn_after_recovery` | submittedBodies.ShouldBeEmpty() after new prompt. |
| PC-101 | G-101: Suppress due log (separate result-log variant is PC below). | T18: `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` | dueLogs.Count.ShouldBe(1) at interval. |
| PC-102 | G-102: Suppress result log. | T18: `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` | resultLogs.ShouldContain(sessionGenerationAndCounts). |
| PC-103 | G-103: Suppress warning on Unknown result. | T24: `StaleWorkingTranscriptWatchdogTests.C1205_Failed_probe_retains_debt_and_retries` | unknownWarnings.ShouldContain(sessionId). |
| PC-104 | G-104: Suppress capacity-deferred warning. | T22: `StaleWorkingTranscriptWatchdogTests.C1205_Concurrency_and_generation_are_bounded` | deferredWarnings.ShouldContain(waitingSessionId). |
| PC-105 | G-105: Put failed session at front instead of tail on retry. | T21: `StaleWorkingTranscriptWatchdogTests.C1205_Fair_pages_and_single_flight_bound_work` | probeOrder.ShouldContain(laterEligibleId) within enumerated rounds. |
| PC-106 | G-106: Remove status=Running predicate while retaining local/Working filters. | T30: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` | probedIds.ShouldNotContain(stoppedWorkingSession). |
| PC-107 | G-107: Remove SQL Working admission check. | T30: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` | probedIds.ShouldNotContain(idleSession). |
| PC-108 | G-108: Reuse last-success stamp across accepted-generation change. | T30: `StaleWorkingTranscriptWatchdogTests.C1205_Discovery_requires_positive_local_working_evidence` | newGenerationProbeOnFirstPass.ShouldBeTrue(). |
| PC-109 | G-109: Add SendInputAsync call on RecoveredIdle before ordinary wakeup. | T35: `StaleWorkingTranscriptWatchdogTests.C1205_Recovery_does_not_manufacture_idle_or_release` | directInputCalls.ShouldBeEmpty(). |
| PC-110 | G-110: Hardcode IntervalSeconds=120 in due-age comparison. | T18: `StaleWorkingTranscriptWatchdogTests.C1205_Interval_boundary_controls_probe_and_log` | probeCountAt120Seconds.ShouldBe(0) in the 180-second row. |
| PC-111 | G-111: Remove the pump's failure-path dispatch and rethrow immediately. | T47: `SessionRunnerStreamReadinessTests.C1205_Held_event_survives_failed_second_catchup` | deltas.Count.ShouldBe(1) on the timeout row. |
| PC-112 | G-112: Skip that dispatch when the host stopping token is cancelled. | T47: `SessionRunnerStreamReadinessTests.C1205_Held_event_survives_failed_second_catchup` | deltas.Count.ShouldBe(1) on the cancellation row. |
| PC-113 | G-113: Rethrow a non-cancellation second-list fault from CatchUpTranscriptsAsync. | T47: `SessionRunnerStreamReadinessTests.C1205_Held_event_survives_failed_second_catchup` | disconnected.ShouldBeFalse() on the throw row. |
| PC-114 | G-114: Yield a payload-free readiness marker before the held parsed event. | T47: `SessionRunnerStreamReadinessTests.C1205_Held_event_survives_failed_second_catchup` | deltas.Count.ShouldBe(1) on the timeout row. |
| PC-115 | Restore an unbounded await with CancellationToken.None on held-event delivery. | T48: `SessionRunnerHeldEventDeliveryTests.C1205_Blocked_held_callback_stops_on_host_cancellation` | stopped.ShouldBeTrue() fails before fixture release. |
| PC-116 | Remove the host cancellation registration from bounded delivery. | T48: `SessionRunnerHeldEventDeliveryTests.C1205_Blocked_held_callback_stops_on_host_cancellation` | stopped.ShouldBeTrue() fails without fake deadline advancement. |
| PC-117 | Remove the monotonic deadline token/timer from bounded delivery. | T49: `SessionRunnerHeldEventDeliveryTests.C1205_Blocked_held_callback_stops_at_monotonic_deadline` | disconnected.ShouldBeTrue() fails at three fake seconds. |
| PC-118 | Remove the cancellation check after the final SyncTranscriptAsync. | T50: `SessionRunnerHeldEventDeliveryTests.C1205_Last_transcript_pull_cancellation_preserves_one_output` | Bus.Accepted.ShouldBe(1) observes zero. |
| PC-119 | Propagate an already-cancelled host token before offering the held event. | T50: `SessionRunnerHeldEventDeliveryTests.C1205_Last_transcript_pull_cancellation_preserves_one_output` | Bus.Accepted.ShouldBe(1) observes zero. |

Plan PC crosswalk (this appendix's numbers are authoritative): old 1->1;
2->2; 3->3,4; 4->5,10; 5->6,9; 6->7; 7->8; 8->11,38;
9->12..25; 10->26; 11->27..31,37; 12->32..36; 13->41,101,102,110;
14->42; 15->43; 16->44..51,73,74,80,81,104..108;
17->52..56,77..79,96,103; 18->57,58; 19->86,94,95;
20->87,88,97..99; 21->89,100. Additional settings/no-release/producer guards
are PC-59..72,82..85,90..93,109. Thus the original proposed variants have no
unpriced implicit “and also” control.

The test filters below are the bound method selections for PCs; this is not an
additional ordinary checkpoint table:

| Test | Exact method filter | Expected executions per PC phase |
|---|---|---:|
| T01 | `/*/*/TurnEndGapReproductionTests/C1205_Gap_end_is_stored_before_any_grace_pull*` | 1 |
| T02 | `/*/*/TurnEndGapReproductionTests/C1205_Grace_pull_recovers_the_unsubscribed_tail*` | 1 |
| T03 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Connected_comment_is_not_readiness*` | 1 |
| T04 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Unknown_comment_is_not_readiness*` | 5 |
| T05 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Keepalive_emits_readiness_once*` | 1 |
| T06 | `/*/*/SessionRunnerStreamReadinessTests/C1205_First_event_follows_readiness_without_loss*` | 8 |
| T07 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Reconnect_rearms_readiness*` | 1 |
| T08 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Stream_failure_and_cancellation_release_owned_resources*` | 2 |
| T09 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Catchup_finishes_before_live_dispatch*` | 1 |
| T47 | `/*/*/SessionRunnerStreamReadinessTests/C1205_Held_event_survives_failed_second_catchup*` | 4 |
| T48 | `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Blocked_held_callback_stops_on_host_cancellation*` | 2 |
| T49 | `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Blocked_held_callback_stops_at_monotonic_deadline*` | 1 |
| T50 | `/*/*/SessionRunnerHeldEventDeliveryTests/C1205_Last_transcript_pull_cancellation_preserves_one_output*` | 1 |
| T10 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Complete_idle_snapshot_recovers_once*` | 2 |
| T11 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Uncertain_probe_does_not_certify_idle*` | 15 |
| T12 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Newer_runner_activity_keeps_working*` | 2 |
| T13 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Concurrent_commit_invalidates_snapshot*` | 12 |
| T14 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Overlap_preserves_uuid_kind_and_rebased_sequence*` | 1 |
| T15 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Probe_holds_the_commit_gate_through_publication*` | 2 |
| T16 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Runner_tail_classification_is_conservative*` | 8 |
| T18 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Interval_boundary_controls_probe_and_log*` | 2 |
| T19 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Wall_clock_jumps_do_not_change_due_time*` | 2 |
| T20 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Healthy_keepalives_do_not_reset_probe_debt*` | 4 |
| T21 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Fair_pages_and_single_flight_bound_work*` | 1 |
| T22 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Concurrency_and_generation_are_bounded*` | 1 |
| T23 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Capacity_overflow_remains_visible*` | 2 |
| T24 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Failed_probe_retains_debt_and_retries*` | 1 |
| T25 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Overdue_operation_is_logged_and_joined*` | 1 |
| T26 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Hosted_restart_discovers_working_sessions_immediately*` | 1 |
| T27 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Disabled_setting_logs_the_missing_bound*` | 2 |
| T28 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Invalid_settings_are_rejected*` | 12 |
| T29 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Valid_settings_limits_are_accepted*` | 2 |
| T30 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Discovery_requires_positive_local_working_evidence*` | 7 |
| T31 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Discovery_failure_retains_visible_debt*` | 1 |
| T32 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Probe_budget_covers_headers_body_and_retries*` | 3 |
| T33 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Recovered_idle_schedules_only_coalesced_work*` | 1 |
| T34 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Ownership_cleanup_is_bounded_and_conservative*` | 2 |
| T35 | `/*/*/StaleWorkingTranscriptWatchdogTests/C1205_Recovery_does_not_manufacture_idle_or_release*` | 3 |
| T37 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once*` | 2 |
| T38 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Watchdog_delivers_while_event_stream_remains_healthy*` | 1 |
| T39 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_after_transcript_commit_recovers_lost_wakeup*` | 1 |
| T40 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Receipt_restart_confirms_without_retyping*` | 1 |
| T41 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Busy_tail_keeps_completion_obligations_pending*` | 1 |
| T42 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Already_eligible_caller_receives_completion*` | 1 |
| T43 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_recovers_each_delivery_handoff*` | 16 |
| T44 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Unknown_probe_keeps_obligations_held*` | 2 |
| T45 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Only_complete_destination_receipts_confirm*` | 4 |
| T46 | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Flush_rechecks_a_new_turn_after_recovery*` | 1 |

### Out of scope

- Phone-home streaming/recovery, runner protocol changes and Kestrel/native-PTY
  qualification: direct HTTP framing and real-hub gap only.
- CARD-0432 serial callback obstruction: R-6 preserves its current characterizations.
  A blocked queue callback can still delay that pump; the watchdog loop must remain
  independently owned and observable.
- Generic SQL Working semantics, notification retention, distillation/spill-content
  recovery and release/parking implementation changes. Reuse their existing behavior;
  R-7/R-8 protect named compatibility. H5 uses inline non-distilled notes.
- Real model launches, AppHost deployment/restart, production fault injection,
  live caller messages, operational setting changes and incident-wide census.
- Exact attribution to an historical first-bad change; the deterministic baseline
  proves the current gap signature, not the original incident's unpublished timing.
- Self-consistent lying/truncated provider snapshots, cross-process writers that
  bypass the supported session mutation discipline, and an arbitrary-population
  fixed 120-second recovery SLA. Their absence cannot be inferred from this pack.

### Checkpoints

This is the **closed ordinary manifest** replacing only the plan's proposed table.
One isolated build and one exact filter per row; `Min` is TUnit executions.
The union is 170 distinct ordinary cases; CP-1 executes two again on baseline,
so the sequential Code total is 172 executions, including the one intended red.
Current existing counts are source-bound, future counts are frozen target rosters
to bind in Code. Any extra/missing/skip result needs a manifest amendment and
explanation, not silently treating a floor as the census. No broad Unit lane.
S1 recorded CP-1 as the expected-red reproduction. After S2, T01 is green and stays
green. Repair round 2 runs CP-1, CP-2, and ADJ-1 on one S2 output. All rows require
0 failed/0 skipped.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1205-s2/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | 2 executed, both green; T01 preGraceEndCount.ShouldBe(1) unchanged; 0 skipped | 2 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1205-s2/` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)/*` | V-1..7,R-1,R-6,T47..T50 | Exactly 34 executed, all named cases green | 34 | 10 |
| ADJ-1 | S2 | `tests/Antiphon.Tests -> bin-c1205-s2/` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | wire and working-state compatibility | Exactly 33 executed, all green | 33 | 3 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1205-s3/` | checked-recovery | `/*/Antiphon.Tests.Application/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8..12,V-25,V-26,R-2,R-7 | Exactly 62 executed, all named cases green | 62 | 11 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1205-s4/` | watchdog | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/*` | V-13..19,V-27..35,R-3,R-4 | Exactly 48 executed, all named cases green | 48 | 12 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1205-s5/` | restart-receipts | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/*` | V-20..24,V-36..40,R-5 | Exactly 30 executed, all named cases green | 30 | 16 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1205-receipts/` | receipt-compatibility | `/*/*/(VerificationRoundDeliveryTests*)\|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)\|(C544_StampIsNotReceipt*)\|(Registry_matches_compiled_metadata*)` | R-8,R-9 | Exactly the 4 named methods green | 4 | 7 |

Dispatch checkpoint reference:
`checkpoints: docs/superpowers/plans/2026-10-12-card-1205-test-design.md@<this pushed commit> section "### Checkpoints"`.

Commit/push the slice, freeze source, then use the checkpoint tool with this plan.
Repair round 2 selects `--after S2` because CP-1, CP-2, and ADJ-1 share that
output. Later slices use `--after S3`, `S4`, and `S5`. Pass `--serial` and
`--expected-source-sha <slice-commit>`. Await owned `wait` calls until exit !=75.
Use the existing verified tool binary, or the plan's disclosed build-slot bootstrap
with `OutputPath=bin-c1205-tool/`; an implicit unleased dotnet build is not allowed.
Rows acquire their own slots; do not hold an outer lease while waiting on row slots.
Do not name a build id `tests`. Tool bootstrap is the only setup build, separately
priced below. Code records exact observed filtered method/argument roster, CP counts,
SHA, source/build provenance, slot disposition and unedited CHECKPOINT lines.
Slot timeout is not-run, not a reason to bypass the gate. Unexpected red is checked
method-scoped at the base with a stated extra-run reason; do not rerun assemblies.
No new retry/flaky exemptions. Full-range `scripts/check-evidence-diff.ps1` is
required for Code/Review. Generated payloads stay ignored, and tool-owned alternate
outputs are removed through the documented custody path after evidence retention.

### Cost

**Estimated, not measured.** Ordinary V/R floor (Code) = CP-1 6 + CP-2 9 +
CP-3 11 + CP-4 12 + CP-5 16 + CP-6 7 = **61 minutes**, including six isolated
builds. Optional verified-tool bootstrap/setup = **4 minutes**, Code verification
allocation **65 minutes**. The fixed-source ordinary selection CP-2..CP-6 is
55 minutes; do not repeat the historical CP-1 expected-red run after the fix.
The plan's original 46-minute proposed floor is replaced by 61, a **15-minute**
increase for expanded cases, not an executed slowdown.

Mutation is separately paused. Price **114** method-scoped baseline/red/restored-green
cycles. The arithmetic below still prices the original 110 (17 H5 controls plus 93);
PC-111..PC-114 are four additional cycles outside that sum. PC-84..100 (17 H5 controls) each estimate
1 minute patch/restore + 3 phases x 3 minutes build-and-method = **10 minutes**
each, **170 minutes**. The other 93 controls each estimate 1 minute patch/restore +
3 phases x 2 minutes build-and-method = **7 minutes**, **651 minutes**.
Those filters are explicitly listed in the T-method selection table above;
H5 includes the 16-row cut method where selected. Baseline/red/green each has
its own isolated build: approximate build share 1.25 minutes per phase; runtime/
fixture share 0.75 minutes for ordinary methods or 1.75 for H5. These are scheduling
floors to measure during commissioning, not promises on a loaded host.

PC cycle floor = **821 minutes**; Mutation binding/setup/reporting = **10 minutes**;
Mutation allocation = **831 minutes**. Total verification allocation =
4 setup + 61 ordinary + 10 Mutation setup + 821 PC cycles = **896 minutes**.
Retaining the plan's provisional 219-minute Code authoring estimate gives
**1,115 minutes** authoring plus verification, subject to re-estimation after S1;
do not promise that S3/S4's expanded fixtures fit the old 60/50-minute authoring
allocations. Slice order remains unchanged; split a dispatch at a committed boundary
and amend its After/checkpoint ownership before running if needed.

Claimed measured savings = **0 minutes**: nothing executed here. Narrow method
filters avoid paying class/suite cost for each mutant, but no invented measured
speedup offsets the additional independent guards. Post-land pausing defers 831
minutes of estimated work; it does not remove that obligation.

#### Slice order and collision gate

Safe order remains **S1 -> S2 -> S3 -> S4 -> S5**, on an evolving committed branch.
Do not land S1's red tests alone. All slices collide logically with the
session/transcript delivery path; path scopes and current task ownership must be
re-read before dispatch. No existing shared helper modification is preauthorized.

| Slice | Source/test areas touched (fix design unchanged) | Current collision/dependency assessment |
|---|---|---|
| S1 | New TurnEndGapHarness.cs and TurnEndGapReproductionTests.cs only | Shares TestDbFixture consumers with CARD-1192; avoid editing shared DB infrastructure. Check checkpoint-driver readiness from CARD-1199. |
| S2 | SessionRunnerDtos.cs; SessionRunnerHttpClient.cs; SessionRunnerEventPump.cs; new readiness tests; card-specific helper | Session/transcript delivery overlap with CARD-1032/CARD-1033 ownership; recheck Review/Code scopes before edit/land. |
| S3 | AgentSessionRuntime.cs; new TranscriptRecoveryResult.cs and recovery tests; card helper | Highest shared runtime/commit-gate collision risk. No SessionStateStore or SessionMessageQueueService source edit authorized; return Plan if required. |
| S4 | New recovery service/settings/host; Program.cs, appsettings.json, session-runtime owner doc; watchdog tests | DI/session supervision and delivery path; order after S3. Shared Program/runtime writers require serialization. |
| S5 | New TurnEndGapDeliveryHarness.cs and restart tests; card-specific helper; delivery Slow-registry entry | C544/Bridge and delivery-fixture consumers; avoid changing shared fixtures or checkpoint infrastructure underneath others. CP-5 then CP-6. |

Task-bound scoped API snapshot, 2026-10-12 approximately 18:02 UTC:
Code CARD-1199 repair10 `71060864`, CARD-1192 S4 `1a656bd4`,
CARD-0955 redacted-frames `da4cdf51`; Review CARD-1032 S2 r2
`d8992498`; Plan CARD-0987 amendment `04839ba2`. Blocked CARD-1199 repair7
and CARD-1042 review were also listed. The task briefs show CARD-1199 owns checkpoint
confinement, CARD-1192 owns fixture-volume work, and CARD-0955 owns land-service log
redaction; these are tooling/shared-infrastructure dependencies, not evidence of
a same-file runtime edit. The listing supplied no useful path scope, so it cannot
certify no collision. CARD-0987 explicitly defers its delivery pieces behind
CARD-1032/1033; caller should retain that ownership gate when dispatching this work.

The project pipeline snapshot reported open/parallel 6, remaining absolute capacity
0, Code occupancy 3; these values are observations, not a reservation or authority
to override. Re-read effective project settings and the caller's operating instructions
after this TestDesign settles. No dispatch, scope change or board update was made here.

#### Handoff census

Design census: **guards=110, mapped=110, missing=0, duplicate PC maps=0**.
44 new method reservations / 141 argument-expanded cases, plus 29 existing cases =
170 fixed-source cases. CP-1 adds 2 baseline executions = 172 across S1..S5.
PC executions in this task: **0**; all 110 remain pending under the explicit paused
post-land policy. Executable payload binding remains required after Code; Review
must reject a renamed, unreachable or non-red-capable payload without an amendment.
The eventual Mutation completion gate is all 110 break/red/restore/green cycles,
not “PCs specified.” No green runtime claim is made by this document.

--- next stage ---
next: code
handoff: Start S1 only: implement the two barrier-driven reproduction methods and card-specific HTTP/real-hub fixture; commit/push before CP-1. Require the exact missing-end baseline red plus green grace control, then sequential S2-S5 with this frozen checkpoint roster. Recheck collision scopes; post-land PCs stay paused.
artifact: docs/superpowers/plans/2026-10-12-card-1205-test-design.md
