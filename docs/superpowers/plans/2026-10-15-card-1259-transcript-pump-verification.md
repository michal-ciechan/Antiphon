# CARD-1259 verification manifest: transcript pump decoupling

TestDesign task 68e8c7ab, 2026-10-15. Source inspected:
e7c05e00f68617be9eba98cdde7644459c620930. Card read live:
bbd94575-e940-4616-9cda-a1090e1bfc39, Antiphon board
8988ca03-7414-47ad-b0b6-51556c701703, revision 15.

This supplements the [landed fix design](2026-10-15-card-1259-transcript-pump-decoupling-plan.md).
D-1 through D-7 and S1 through S4 remain in force. This file is the **executable
manifest**, replacing that document's proposed checkpoint roster and cost.
Pass this file to the checkpoint importer; do not run both tables.
**Code can start:** Plan correction `4cdba1a7`, based on
`26147352a09a61a0cb210fdcebdc504c5fc0d539`, resolves B-1 with D-8 and binds R-6/
PC-113 here. This brief folds that narrow verification correction into Plan;
no further TestDesign round is needed. V-1..V-12 and CP-1..CP-12 are unchanged;
CP-13 expands only the payload guard. There are still 13 checkpoints.

This is a design, not execution evidence. No product tests, builds or PCs ran in
TestDesign. Code runs ordinary V/R; independent Review judges before land;
SourceLanding Mutation runs every PC after land.

## Verification design

### Inspection

Paths below are relative to tests/Antiphon.Tests unless prefixed server/.
"Read" means the relevant method and fixture bodies, not just their names.

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| Application/SessionRunnerEventPumpTests.cs: both tests, GatedEventBus, PhaseLogger; TestHelpers/ScriptedSessionRunnerClient.cs | Four S0 blocking oracles flip; typed stale exit retained -> V-1, V-2, R-4. Scripted runner needs ready-marker/original-copy production and per-phase snapshot counters. |
| TestHelpers/BridgeQueueHarness.cs: construction, options, attach, transcript helpers; Agents/FakeAgentProtocolAdapter.cs: SendInputAsync, SendPromptAsync, submission and screen accounting | Real queue/runtime DI with fake I/O -> all V. Default OnSubmitted inserts directly into DB: replace it in this card's cases. Static CreateContext uses the shared database: new cases must instead use their clone's connection. |
| Application/AgentSessionRuntimeTests.Persist.cs: C698 and C561 persistence methods, PersistFixture and fault interceptors; Application/SessionStateCommitTests.cs and TestHelpers/SessionStateTestFixture.cs | Actual write lease, stored sequences, stub/fallback, before/after-save ambiguity -> V-3, V-4, R-1, R-5. Existing optional-store harnesses need the real store. |
| Application/SessionMessageQueueDeliveryVerificationTests.cs: the five R-2 methods, ObservableHarness/LongQueuedBody/ClipSubmitToPrefix; TestHelpers/RemoteControlRecoveryHarness.cs: DI/adapter setup | Narrow compaction, complete body, LF and separate Enter -> V-7, V-12, R-2. Existing LF test does not itself assert paste framing; V-2 explicitly does. |
| Application/ChannelReplyLateConfirmDispatchTests.cs: the three R-3 methods and held/cancellable producer bodies | Exact source IDs, publication claim and live/grace race -> V-5, V-9, R-3. SentReplies is fake destination acceptance, not real Telegram receipt. |
| Application/ReviewReplyDispatcherTests.cs: all seven methods and Harness | Error withholding, owning/next prompt, late text -> V-5, V-11, R-5. TrackThread currently grants map authority; replace fixture seeding with authorized queue dispatch while retaining each behaviour assertion. |
| Application/AgentTaskMidTurnRefineTests.cs: C1170 and both C1188 bodies, PrepareSentRefinement/Scope fixture; Application/AgentTaskReplyIntegrationTests.cs: Phone_parent_preserves_worker_table_and_stage_handoff | Deferred refinement verdict still requires queue lock; task report must reach parent's complete UserPrompt -> V-5, V-7, R-5. C1188's blocking Observe oracle must change. |
| TestHelpers/QueuedReceiptAssertions.cs: submission binding and receipt methods | Its adapter callback also inserts directly into DB. Reuse complete-body comparison only, not that producer binding, for new pump-delivery evidence. |
| Application/SessionRunnerHeldEventDeliveryTests.cs: selected first three tests, DeliveryHarness, tracked scopes; Application/HerdrStatusDisagreementTests.cs: all bodies | Raw held-event initial offer/cancellation and status has no delivery/control authority -> R-5. Raw-output abandonment is not the new durable action worker shutdown policy. |
| Application/CompletionNoteWorkQueueTests.cs; TestHelpers/TestDbFixture.cs; Application/TranscriptHotPathMigrationTests.cs: clean/retry migration recipe; TestHelpers/TranscriptHotPathFixture.cs | Nearest bounded-worker and migration fixtures for new files -> V-8 through V-11. Do not reuse completion-note recovery as arbitrary runtime-action debt. Migration tests use an owned cloned database, never the shared template. |
| server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs: ExecuteAsync, catch-up, subscription marker, held dispatch; server/Application/Services/AgentSessionRuntime.cs: observe, status, effect chain, sync, persist and synthetic append | All live/snapshot ingress and lock ordering -> V-1 through V-4, V-7, V-9. Raw output, exit/adoption/bind/fault I/O stays outside the fix. |
| server/Application/Services/SessionMessageQueueService.cs: confirmation loop, TryFindConfirmingRecordAsync and grace; ReviewThreadService.DispatchAsync; ReviewReplyDispatcher; TranscriptTurnWindow; ChannelReplyDispatcher bound/unbound entry points; AgentTaskReplyService turn/attempt judgement; DataRetentionService transcript/session/queue pruning | Real confirmation barrier, routing ownership, authorized review correlation and retention -> V-2, V-5 through V-12. Verifier algorithm, 30s/7s settings and native transport are preserved. |

Owners consulted: docs/testing-and-build.md (manifest/importer, isolated outputs,
slots, PostgreSQL, source receipts), docs/session-runtime-invariants.md,
docs/herdr-sessions.md (delivery/status), docs/project-context.md and
docs/orchestration-loop.md (stage order and D-2).

**Requested source-reader audit.** At the inspected base, searches over tests for
SessionRunnerEventPump, AgentSessionRuntime and SessionMessageQueueService, then
ReadAllText/ReadSource/SourceText/SourceFile and literal source filenames, found
runtime/reflective consumers but **no tests that read those three source files**.
The literal paths in ScopeDriftTests and DelegationUnitTests are inert scope
examples; they never read production source. Do not invent source-reading classes
or run the whole Unit lane to compensate. CP-11/CP-12 bind existing affected guard
consumers below. Code repeats the search on its actual base; if landing introduces
a real source-reading class, read it and append its exact filter/count/cost to this
manifest before running it. That is a newly discovered scope requirement, not an
unlisted run.

#### Fixture and binding contract

Use one TestDbFixture.CreateIsolatedSchemaAsync clone per new parameterized case,
with every production, interceptor and observer context explicitly bound to that
connection. Register SessionStateStore, its real loader/settings/metrics, the
coordinator and owned hosted workers. No EF InMemory provider. The migration arm
owns a fresh clone restored to the migration immediately preceding this card,
seeds old rows there, applies the CLI-generated migration, and restarts DI.

Use FakeTimeProvider with fixed UTC and explicit timers/barriers. Do not freeze a
wall-clock poll by accident: register the same clock for runtime, queue and worker.
Use real time only as a bounded failure deadline. All gates are released and all
started pump/worker/transport tasks joined in finally, including expected-red cases.
No unowned Task.Run, live provider, shared runner, broker or external chat.

For new action APIs, Code binds concrete phase/result names, SQL interception
predicates, fault hooks, migration ID and mutation snippets to implemented members
in an append-only binding record here before handoff. New fields/methods below
are planned contracts, not assertions that they already exist. An unbound mutation
payload remains **binding required after Code**; it cannot be marked run, green,
equivalent or covered by a nearby test. Existing EF interceptors and owned I/O barriers can verify the scheduling and
delivery boundaries; new phase boundaries may expose internal, test-only fault
hooks that cannot provide delivery evidence. D-8 and the R-6 recipe below bind
the remaining payload contract; Code records concrete implementation line sites
alongside the other planned members, without reopening the contract choice.

**B-1: original UI bytes versus durable stored bytes.** In
AgentSessionRuntimeTests.Persist.cs,
C561_a_NUL_in_tool_result_text_persists_as_U_FFFD asserts BOTH persisted
Text contains U+FFFD AND SessionTranscript payload text still contains NUL.
ObserveTranscriptAsync currently publishes ToTranscriptPayload(entry) before
SanitizeEntries stores the changed text. D-1 moves publication behind durable
admission; D-2 forbids a duplicate transcript body and specifies only separate
producer sequence storage. A restarted worker reading that row cannot recover
where original NUL bytes were replaced (a literal U+FFFD may have existed too).
An in-memory callback is not restart evidence. The safe-stub path has the same
original-versus-stored payload issue.

**Resolved by D-8 (Plan correction 4cdba1a7):** preserve the original UI DTO,
including NUL, literal U+FFFD and safe-stub source fields, before and after restart.
The action retains producer sequence plus a versioned binary projection delta:
original NUL positions, lost timestamp precision/offset, and original values of
stub-clipped/cleared fields. Ordinary transcript bodies are not duplicated; a
stub's discarded body exists only in the delta. Store it as bytea atomically with
the final row/action, rebuild it for fallback, and retain it through acknowledgement.
Both first publication and restart use the same stored-row reconstruction seam.
Stored sanitized/stub rows remain the state/delivery authority. R-6 keeps C561's
NUL assertion and adds the two restart cases below; PC-113 bypasses reconstruction
at the worker, rather than mutating the now-obsolete inline publication site.

#### Source scope per slice

| Slice | Allowed source footprint and test responsibility |
|---|---|
| S1 | Application/SessionRunnerEventPumpTests.cs; TestHelpers/ScriptedSessionRunnerClient.cs; TestHelpers/BridgeQueueHarness.cs for clone-aware observers, barriers and lifecycle only. Register the existing state store now. Flipped S0 plus V-2 compile against old public APIs. Commit/push tests before CP-1; its intended assertion red is retained. |
| S2 | server/Domain/Entities/SessionRuntimeAction.cs; server/Application/Services/SessionRuntimeActionCoordinator.cs; server/Infrastructure/Agents/SessionRunner/SessionRuntimeActionHostedService.cs; server/Infrastructure/Data/AppDbContext.cs; server/Program.cs; CLI-generated server/Migrations/*; server/Application/Services/DataRetentionService.cs; new Application/SessionRuntimeActionTests.cs and local helper partials if needed. |
| S3 | server/Application/Services/{AgentSessionRuntime,ChannelReplyDispatcher,ReviewReplyDispatcher,ReviewThreadService,AgentTaskReplyService,TranscriptTurnWindow,SessionMessageQueueService}.cs; server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs; server/Domain/Entities/SessionQueuedMessage.cs; EF mapping/migration; new Application/SessionRuntimeActionRoutingTests.cs. Queue edits expose intent/outcome only; no confirmation matcher or retry-policy change. |
| S4 | Complete S1-S3 tests; adapt AgentSessionRuntimeTests/PersistFixture, SessionStateTestFixture, ReviewReplyDispatcherTests, the three AgentTaskMidTurnRefineTests and selected queue tests to await effect evidence. Extend shared registration only where required; preserve direct persistence callers. docs/session-runtime-invariants.md, docs/herdr-sessions.md, this binding record. Production lifecycle corrections stay within S2/S3 paths. |

Paths without a server/ or docs/ prefix in this table are under tests/Antiphon.Tests.
For D-8 only, S3 also touches SessionRuntimeActionCoordinator.cs (listed in S2)
and adds the R-6 tests to Application/AgentSessionRuntimeTests.Persist.cs in its
test-first commit; S4 runs their final evidence through CP-13.
Commit each test addition before its implementation; existing working guards need
not fail on the old source. For genuinely new semantics the compilation must
succeed and the named behaviour assertion must fail before its fix. Code records
that outcome through the appropriate CP row, then reruns only affected rows after
the fix; planned final groups run once on the completed source. CP-1 is the initial
seven-case reproduction; later intentional assertion reds are reruns of CP-2/CP-3,
not hidden test commands. Include those actual extra runs in cost.

### Delivery inventory

Durable action identity throughout is (session, stored sequence, trigger kind),
joined to accepted generation, owning prompt/boundary and the exact downstream
queue/source/task-attempt identity. "Delivered" below always requires the matching
complete recipient UserPrompt past its attempt floor, read in a fresh context.
Action ack, queue insertion, event publication, Sent and request success are not
recipient evidence.

| Path: producer -> destination | Persistence, recovery and receipt | Cases |
|---|---|---|
| Live/initial sync/subscription sync/held transcript -> deferred effects | Transcript plus action commit together; failed/closed hint leaves debt. Startup/overflow/1s scan discovers it without another event. Read exact ordered transcript and then actual recipient result. | V-1..V-4,V-8,V-9 |
| Stored transcript -> session UI | Same action identity, ordered SessionTranscript publication, phase ack after fake receiver accepts payload. Publication error retries; DB commit is independent. Receipt is fake bus receiver's ordered payload list with original producer DTO sequence and stored action identity separately observed. D-8's delta restores original fields after restart, including safe-stub fallback. This proves server dispatch, not browser rendering. | V-1,V-3,V-8,V-10,R-6 |
| Boundary/refinement/compaction/status/catch-up -> same-session queue | Producer action retains phase; compaction recovery has its own stored watermark before enqueue; queue owns stable message ID, attempt floor and generation. Crash before queue commit retries owed action; after commit/before ack reuses queue identity. In-progress Sent checks transcript before retry. Busy and already eligible recipients each finish at complete UserPrompt through pump. | V-2,V-6,V-7,V-9,V-12 |
| Bound channel turn/late-confirm source IDs -> channel destination | Existing channel queue correlation/publication claim and durable outbound journal remain owners. Fake destination records full body, conversation and source ID. Test pre-accept failure, after journal commit/before wake, and after acceptance/before action ack; recover with original identity. Direct transport's uncertain crash window remains PublishUncertain, never asserted exactly-once. | V-5,V-9,V-10,R-3 |
| Bound task report -> task settlement -> parent's queue -> parent session | Join task ID + Attempt + completion publication/notification ID to parent queue ID, original source turn and complete parent UserPrompt. Settlement without the parent transcript is insufficient. Test real queue with parent busy and already idle; cross-session routing cannot stall ingress. | V-5 parent-handoffs,V-9 |
| ReviewThreadService -> review prompt queue -> agent session -> review comment | Persist typed ReviewThreadId with Channel origin, exact review:thread key, body and recipient. Agent's complete prompt binds the owning turn; comment/thread update/ReviewReplyConsumedAt commit together. Restart map-free and observe exact thread comment via independent DB query. Legacy rows remain unknown. | V-11,V-5 review-bound |

Handoff cuts used by V-5 parent-handoffs, V-7 manual and V-11 enqueue-failure
are individually named and run for both busy and already eligible recipients:
H0 producer commit before enqueue; H1 enqueue throws before commit; H2 queue commit
before wake; H3 submit has produced complete streamed receipt before producer/action
ack; H4 effect acknowledged before worker restart. Discard/recreate DI, retain the
clone and the same durable IDs, invoke the real recovery entry point, and drain
through the real queue/pump to recipient evidence. H1 must assert fault hit count
one; H2/H3 must assert pre-restart evidence, not reseed a replacement obligation.
For compaction and parent notes register their existing recovery workers explicitly;
a direct call to a test helper that manufactures a queue row does not count.
For a crash before the producer's own durable commit no obligation was accepted:
assert rollback and zero delivery, then explicitly retry the original producer.

Substitutes: scripted event stream replaces runner HTTP/native JSONL; recording
adapter replaces terminal; fake channel receiver replaces remote chat; fake clock
replaces time. None proves real CLI parsing, OS process-death atomicity, broker
receipt, browser rendering or loaded desktop latency. In-process abrupt scope
replacement models cuts in durable state, not power loss. PostgreSQL transaction,
constraint and migration behaviour is real. Raw test DB inserts may seed history
and competing state only; they never mint a new delivery receipt.

### Proves it works now

P = SessionRunnerEventPumpTests, A = SessionRuntimeActionTests,
T = SessionRuntimeActionRoutingTests, in Antiphon.Tests.Application.
Each row binds one exact TUnit method; listed variants are separate Arguments
results, not method-name wildcards. Internal subcases below do not increase Min.
All new methods are Integration, with isolated PostgreSQL and the existing
ProcessSpawnLimit discipline; no native process launch is needed.

| ID / exact method | Recipe, variants / cases | Oracle, reachability and classification |
|---|---|---|
| V-1 / P.S0_blocked_hosted_pump_probe | Keep the four existing tuples: signalr-publish/false/false; queue-confirm/false/false; queue-confirm/true/false; signalr-publish/false/true. Start real hosted pump/workers and hold A's effect. | Before release, B's entire three-row ordered transcript is committed and its marked task has Result when another worker is free. Received contains B in stream cases. Preserve phase entry and throwing-diagnostics observations. Functional progress regression; old false/ShouldNotContain oracles flip. |
| V-2 / P.C1259_confirming_prompt_and_other_session_progress_before_confirmation | Three variants: live, startup, subscription-ready. Use the real confirm-read barrier described below. | A complete UserPrompt and B's ordered rows committed while confirmation read is held; then Delivered/Transcript, one attempt, one body and one Enter, zero post-submit recovery pulls. Integration acceptance; missing barrier entry is fixture failure, never red evidence. |
| V-3 / A.C1259_commit_and_effect_admission_are_atomic | Seven variants: batch-fail, fallback-fail, safe-stub, replay-after-ack, commit-wake-gap, cancel-before-commit, cancel-after-commit. Interceptors hit real saves/transactions; suspend execution when inspecting debt. Replay-after-ack also repeats an active key at a later phase. | Exact committed-row/action roster in independent contexts; zero actions for failed rows; safe stub's stored classification preserved; duplicate key cannot insert or rewind phase; postcommit cancellation publishes/reloads state; restart recovers omitted hint once. Atomicity/recovery; prove fault and commit cuts reached. |
| V-4 / A.C1259_stream_pull_and_replay_preserve_order | Five variants: mixed-stream-pulls, same-api-siblings, synthetic-race, missing-store, queue-lock-catchup. Gate actual storage writes, overlap stream/two snapshots/restart boundary, use low runner sequences and same UUID with differing kinds. | Exact UUID/kind/stored-sequence roster, one eligible effect per ApiCallId including same-batch siblings; peak same-session writer one; missing store refuses before write; CatchUp returns with queue lock held and debt later routes. Ordering/lock regression, not timing benchmark. |
| V-5 / T.C1259_buffered_turns_route_their_own_recipients_before_flush | Eight variants: channel-bound, review-bound, task-bound, prompt-window, early-text, trailing-text, frontier-race, parent-handoffs. Hold first routing effect; commit two turns with distinct bodies/destinations/task IDs. Prompt-window includes in-turn QueuedUserPrompt and a next-turn opener; task-bound includes answered-Blocked watermark and housekeeping/error reports. Parent-handoffs runs both recipient states at H0-H4. | Recipient bodies and IDs exactly match each owning turn in trigger order; no newer-turn substitution, no early consume, no repeated trailing text. First next-input byte occurs only after all owed routing through the newly checked frontier. Parent sees complete original report/marker/handoff in one UserPrompt. Identity/order/delivery integration. |
| V-6 / T.C1259_new_working_or_blocked_state_defers_old_idle_action | Six variants: working, herdr-blocked, rules-held, generation-changed, task-attempt-changed, action-during-flush. Change state after action admission, before execution; hold a real queue operation for the last case. | Working/blocked/rules cases have zero input and unchanged attempts until eligible, then one complete streamed receipt. Stale generation action never rebinds/types; stale attempt never settles replacement. New action during flush remains pending and is eventually serviced. Eligibility/fencing regression; assert authoritative gate entered. |
| V-7 / T.C1259_compaction_refinement_and_status_cannot_block_ingestion | Five variants: sent-refinement, manual-compaction, automatic-compaction, blocked-exit, empty-sync. Hold queue lock/effect, ingest A and B. Manual includes busy/eligible at H0-H4; empty-sync starts with all rows already stored and an owed task. | Ingress returns before held effect; Sent refinement remains unconfirmed under queue lock then confirms from its full prompt. Manual recovery precedes narrow flush and produces recipient receipt, zero SessionFinished/task settlement. Auto creates no manual flush. Status grants no delivery/kill/working override. Empty sync reconciles owed work without historical blanket publication. |
| V-8 / A.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | Five variants: production-capacity, duplicate-owner, overflow, backstop, poison-page. Use real limits 4 workers/128 hints/128 page at least once: block four different session deliveries; add 129 distinct pending sessions, plus >128 actions for one session. Deliberately race hint and scan. Separate overflow and timer stimulus by holding the other scan triggers. | Confirming prompts for all four and B persist before workers release; max owners 4 and per-session 1; hints/pages <=128; no unbounded overflow collection. Overflow, startup or exactly 1s backstop drains all durable IDs without events. A failing early page does not hide later eligible sessions; page-boundary yield lets B route. Capacity/liveness/recovery. |
| V-9 / A.C1259_shutdown_joins_and_restart_reconciles_submission | Six variants: before-wake, during-confirm, after-input, after-effect-before-ack, uncooperative-effect, reconnect-cohort. Own cancellation and fresh DI over the same DB; after-effect covers queue, review and channel journal outcomes separately. | Pending phase survives, cancellation never becomes success, all active scopes joined before clean stop, no live task after it. Uncooperative fake keeps Stop incomplete and owner held until release. Restart finds prior full UserPrompt before any input; no extra body/Enter. Reconnect creates no extra worker cohort. Lifecycle/recovery integration. |
| V-10 / A.C1259_retry_and_retention_preserve_pending_turn | Five variants: retry-clock, phase-failure, prune-candidate, prune-race, correlation-retention. Retry-clock covers 1/2/4/8/16/30/30s; phase-failure injects each effect/save separately. Prune both transcript and session; correlation-retention invokes queue retention. | Due-time barrier prevents early retry; failed/deferred phase not acked/deleted, previous committed phases not replayed, later session progresses. Whole required turn and queue correlation survive discovery and mutation-lease races/expiry; normal eligible pruning resumes after completion. Recovery/retention safety. |
| V-11 / T.C1259_review_recovery_uses_authorized_queue_identity_once | Ten variants: map-free-restart, forged-target, wrong-session, wrong-origin, incomplete-body, atomic-comment, legacy-upgrade, ttl, api-error, enqueue-failure. Actual ReviewThreadService producer plus queue/pump, not Track. Atomic-comment cuts before commit and after commit/before action ack; legacy upgrades old rows using real migration. Enqueue-failure includes busy/eligible H0-H4. | Exactly one correct comment and consume stamp in one transaction, unchanged ChannelReplySettledAt ownership. Forged tag cannot redirect; wrong session/origin/body cannot consume. Legacy target/stamp remain null with bounded identity-only diagnostic, no historical replay; explicit fresh re-dispatch survives restart. TTL=60m remains withheld after expiry. Error turn keeps owed correlation; genuine resumed answer routes. Authorization/upgrade/recovery. |
| V-12 / T.C1259_missing_or_truncated_prompt_never_confirms | Six variants: absent, wrong-body, old-sequence, old-generation, old-time, truncated. Seed ordinary observable attempt; drive screen/output/status/UI/action ack without valid prompt. Old-time uses existing wall-time floor path, keeping its established degraded fallback distinct. | No transcript-confirmed Delivered/LateConfirmed from negative evidence; same existing negative verdict/attempt behaviour; truncated identity stays Pending/parked and never retypes. A valid complete current UserPrompt is the positive continuation where permitted (no automatic unpark of truncated row). Receipt regression, no timeout-policy change. |

#### V-2 confirmation-read barrier (required recipe)

1. Seed A's prior prompt/end via persistence, warm the real state store, and queue
   a non-specialist ordinary WhenIdle body with deliverIfIdle:false. Use a
   distinctive two-line body. Keep TranscriptConfirmTimeoutSeconds=30,
   ReEnterIntervalSeconds=7 and normal SubmitAttempts/PollIntervalMs.
   Check observable baseline >0 and fullInline=false in captured query/preconditions.
2. Install a DbCommandInterceptor through HarnessOptions.ConfigureDbContext.
   Arm it only when A's adapter actually submits its nonempty body. That callback
   enqueues A's complete UserPrompt in ScriptedSessionRunnerClient and immediately
   returns; it performs no DB write and waits on no barrier.
3. Hold exactly the first post-submit ReaderExecutingAsync for the real
   TryFindConfirmingRecordAsync text query: TranscriptEntries SELECT Text, A's
   session parameter, Sequence > the captured baseline, ordinary prompt-kind
   predicate. Record CommandId, parameters, arm count and hit count=1. Distinguish
   the RequiresCompleteSpecialistPrompt/RequiresSubmittedChannelPrompt prequeries.
   The gate is before execution, holds no transaction/state lease, excludes
   observer contexts and permits both A and B writes. Never suppress its result.
4. In live mode stream A's boundary and then B. In startup mode snapshots contain
   A's boundary then B's three rows, and OnSubmitted feeds A's confirming prompt
   into the future live stream. In subscription-ready mode initial list is empty;
   the next marker causes snapshots with A's boundary and B, followed by a held
   payload and its duplicate original. Scripted client must implement that exact
   marker/copy contract, count both pulls separately and record one stored payload.
   Do not manually run a second pump.
5. Wait for interceptor Entered; while Release is incomplete use an independent
   clone connection to observe A's full prompt and B's three ordered rows and
   SessionStateStore's committed frontier. Assert effect/delivery not completed,
   one body write and one submitting Enter already recorded. Old inline startup/
   live/subscription paths cannot satisfy these pre-release assertions.
6. Release read, advance one normal poll if needed (total logical time below 7s),
   and await the same delivery/action. Assert exactly one normalized complete
   UserPrompt after baseline, original queue ID, DeliveryVerdict.Delivered and
   transcript-confirmed evidence, DeliveryAttempts=1, no LateConfirmed/timeout/
   grace/extra body or Enter. Assert LF body inside bracketed paste and separate
   lone carriage-return write. SnapshotReads after submission may contain only
   the explicitly expected startup/subscription pulls, zero verifier recovery
   pulls. Record order by barrier epochs, not DateTime proximity.

A barrier on OnSubmitted is S0's transport hold, not V-2. A query gate that also
blocks writes recreates an artificial deadlock and invalidates the test. Missing
read hit, fake DB-inserted receipt, eventual success only after release, timeout
recovery, or empty test selection is not acceptance.

### Guards the regression

| ID | Existing exact test and decisive assertion |
|---|---|
| R-1 | AgentSessionRuntimeTests.C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations: exact kinds and 1..4 sequences; C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence: complete pair set, 512/512/1 probes and replay count; Error_turn_end_is_acted_on_once_replay_dedups: one row. The last currently proves persistence only; V-3/V-4 add action-count proof. |
| R-2 | SessionMessageQueueDeliveryVerificationTests.C514_Multiline_release_keeps_LF_paste_and_separate_Enter; Manual_boundary_flushes_a_message_stranded_before_the_compaction; Manual_boundary_with_an_empty_queue_publishes_no_finished_event; A_clipped_prefix_parks_as_truncated_not_sent; Late_confirm_does_not_promote_a_truncated_body_to_sent. Preserve wire bytes, narrow path and parked/truncated/no-retype assertions. Await worker evidence for manual cases, then inspect full receipt as well as Sent. |
| R-3 | ChannelReplyLateConfirmDispatchTests.Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes (4); Grace_and_runtime_redispatch_share_one_atomic_publication_owner (2); Caller_cancellation_does_not_release_a_still_publishing_direct_reply (2). Exact earlier/later recipient bodies and one accepted publication, retained claim during live transport. If FlushQueueOnIdleAsync is renamed, bind its real deferred equivalent; do not replace race with a mock callback. |
| R-4 | SessionRunnerEventPumpTests.C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition: stale generation cannot close Running row. Existing name promises more than its body asserts; add zero exit/status notifications and an event-consumed barrier so arbitrary Task.Delay is not the acceptance oracle. |
| R-5 | CP-11 runs all 8 SessionStateCommitTests, all 7 ReviewReplyDispatcherTests and all 14 HerdrStatusDisagreementTests executions. CP-12 runs the three named held-event methods (4 executions), C1170 (1), and both C1188 methods (2). Preserve commit publication, stub classification, review withholding/window and status no-authority assertions. C1188_SentRefinementStillWaitsOnTheQueueLockBeforeConfirming now requires Observe to finish while verdict remains null under lock, then confirms after release; keep method name because the confirmation still waits. C1170 awaits persisted verdict, not Observe return. |
| R-6 | AgentSessionRuntimeTests.C561_a_NUL_in_tool_result_text_persists_as_U_FFFD (1): preserve stored U+FFFD/no-NUL and published original-NUL assertions, awaiting worker publication. Add AgentSessionRuntimeTests.C1259_deferred_UI_payload_preserves_original_fields_after_restart (2 Arguments: sanitized, safe-stub). Exact original DTO after cold DI restart, independently observed sanitized/stub row and retained delta before publication, no historical replay after ack. CP-13 executes 3 results total. |

#### R-6 restart payload binding (D-8)

Implement the new method in Application/AgentSessionRuntimeTests.Persist.cs with
the clone-aware PersistFixture, real state store/coordinator/worker and recording
event bus. Its two Arguments results each run this recipe:

1. Seed older history so producer Sequence=1 rebases to a different stored
   sequence. Ingest a ToolResult whose original Text contains both NUL and a
   literal U+FFFD (also put a surrogate pair before a NUL to pin UTF-16 indexing).
   Populate the other DTO fields, NUL-bearing metadata and a timestamp with a
   nonzero offset and sub-microsecond ticks. Retain expected field values in the
   fixture, not by invoking the production reconstruction helper.
2. Keep hosted execution stopped during admission. In sanitized, use ordinary
   field lengths. In safe-stub, use a 201-character ToolName to hit real 22001
   batch/per-row fallback; include distinctive nonempty Text and ToolInput.
   Assert the actual stub marker, clipped name and null ToolInput in a fresh
   context. In both cases assert row/action/delta committed together, producer
   sequence retained, stored text has no NUL, and zero UI events before restart.
3. Dispose/join the first DI graph, discard its pending hints and construct a
   new graph over the same clone. Start the real worker; no new event, cache,
   runner snapshot or manufactured action may provide the lost fields. Require
   a startup-discovery/publication barrier before asserting exactly one received
   SessionTranscript for that action. Compare every DTO field with the original
   event; explicitly retain text.ShouldContain NUL, exact literal U+FFFD positions,
   original producer sequence/timestamp and the unclipped/cleared stub fields.
   The independently read transcript row must still be sanitized or stubbed.
4. Await durable final ack/action removal, restart workers again, then replay
   the source through ingestion. Assert no new action/publication. This is
   acknowledged replay proof, not an exactly-once claim for a crash before ack.

Preserve all original C561 assertions, including its subsequent persistence
result. The two new cases inherit Integration/Slow from the class and need real
PostgreSQL, no native process. Their sub-assertions/restarts do not add executions.
Commit the guard additions before the projection implementation. This is a
preserved observable contract, not an assertion that old C561 was red: ordinary
CP-13 runs in S2-S4, and PC-113 supplies its independent post-land compiling-defect
red. Any failure-driven CP-13 rerun is reported with its actual extra cost.

### Guard inventory

Every row is a separate control obligation, including inherited delivery guards
whose preservation is asserted here. Equivalent or noncompiling mutations do not
satisfy it. The listed guard is the smallest intended boundary; Code must split
further if its implementation introduces independently bypassable checks. There
are no "none" or intentionally untested safety-critical guards in this scope.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-1: Live transcript ingress never waits for confirmation | PC-1 |
| G-2 | D-1,D-3: Startup catch-up never waits for effects | PC-2 |
| G-3 | D-1,D-3: Subscription catch-up/held transcript remains ingestion-only | PC-3 |
| G-4 | D-1: SessionTranscript publication cannot block ingress | PC-4 |
| G-5 | D-5: Sent refinement confirmation stays off ingress | PC-5 |
| G-6 | D-5: Compaction recovery/enqueue stays off ingress | PC-6 |
| G-7 | D-1,D-5: Blocked-exit flush stays off the event reader | PC-7 |
| G-8 | D-2: Batch transcript/action admission is atomic | PC-8 |
| G-9 | D-2: Per-row fallback transcript/action admission is atomic | PC-9 |
| G-10 | D-2: Uncommitted/failed rows grant no effect authority | PC-10 |
| G-11 | D-2: Safe-stub effect eligibility uses preserved source intent and actual stored row | PC-11 |
| G-12 | D-2: Completed transcript replay cannot recreate effects | PC-12 |
| G-13 | D-2: Unique action key enforces one active durable identity | PC-13 |
| G-14 | D-2: Wake of existing action cannot rewind acknowledged phase | PC-14 |
| G-15 | D-2: Same-ApiCallId sibling boundaries share one eligible effect | PC-15 |
| G-16 | D-3: UUID/kind dedup includes same-batch and replay membership | PC-16 |
| G-17 | D-3: Different kinds sharing UUID remain distinct | PC-17 |
| G-18 | D-3: Restarted runner sequences rebase monotonically | PC-18 |
| G-19 | D-3: Every writer, including synthetic boundary, shares state lease | PC-19 |
| G-20 | D-3: Production cannot silently use an unleased fallback | PC-20 |
| G-21 | D-3: Committed-state publication follows transaction commit | PC-21 |
| G-22 | D-3: Storage lease acquisition observes cancellation before commit | PC-22 |
| G-23 | D-3: Postcommit cancellation reconciles committed state | PC-23 |
| G-24 | D-3: CatchUp under queue lock never invokes queue effects | PC-24 |
| G-25 | D-3: No transcript lease survives into external effects | PC-25 |
| G-26 | D-3,D-5: No DB transaction is held across external effect | PC-26 |
| G-27 | D-3: Worker scopes never share a DbContext | PC-27 |
| G-28 | D-2,D-4: Wake is offered only after durable admission | PC-28 |
| G-29 | D-4: Full or closed hints cannot block ingress | PC-29 |
| G-30 | D-4: Hint storage remains bounded at 128 | PC-30 |
| G-31 | D-4: At most four owned workers execute effects | PC-31 |
| G-32 | D-4: Hint/sweep race cannot create two owners for one session | PC-32 |
| G-33 | D-4: Discovery/action pages are bounded to 128 | PC-33 |
| G-34 | D-4: Failing early session cannot starve later eligible sessions | PC-34 |
| G-35 | D-4: Busy session yields at page boundaries | PC-35 |
| G-36 | D-4: Overflow requests durable discovery | PC-36 |
| G-37 | D-4: One-second backstop discovers unhinted durable actions | PC-37 |
| G-38 | D-4,D-7: Startup discovers durable debt after lost wake | PC-38 |
| G-39 | D-4: Failed retries use the capped durable due-time schedule | PC-39 |
| G-40 | D-5: Failed/deferred phase never acknowledges success | PC-40 |
| G-41 | D-5: Successful earlier phase is not repeated after later failure | PC-41 |
| G-42 | D-5: Channel/review/task/queue phases preserve order | PC-42 |
| G-43 | D-5: Channel deferred routing binds the original turn | PC-43 |
| G-44 | D-5: Review deferred routing binds the original turn | PC-44 |
| G-45 | D-5: Task deferred routing binds original marked turn | PC-45 |
| G-46 | D-5: Owning UserPrompt precedes in-turn QueuedUserPrompt | PC-46 |
| G-47 | D-5: Next true opener caps extraction, in-turn queued prompt does not | PC-47 |
| G-48 | D-5: Extraction never crosses a later opening prompt | PC-48 |
| G-49 | D-5: Early boundary without text leaves correlation owed | PC-49 |
| G-50 | D-5: Trailing text watermark is durable and exact | PC-50 |
| G-51 | D-5: Channel API-error turn cannot publish a partial answer | PC-51 |
| G-52 | D-5,D-6: Review API-error turn cannot consume or publish | PC-52 |
| G-53 | D-5: Task housekeeping/cancel/error text cannot become success report | PC-53 |
| G-54 | D-5: Answered-Blocked reply watermark fences stale settlement | PC-54 |
| G-55 | D-5: Queue rechecks newly advanced committed routing frontier | PC-55 |
| G-56 | D-5: Current Working state defers old idle request | PC-56 |
| G-57 | D-5: Current Herdr blocked state defers input | PC-57 |
| G-58 | D-5: Current rules hold defers input | PC-58 |
| G-59 | D-5: Stale input generation is superseded, never rebound | PC-59 |
| G-60 | D-5: Stale task attempt cannot settle replacement attempt | PC-60 |
| G-61 | D-5: Narrow flush cannot emit finished/report settlement | PC-61 |
| G-62 | D-5: Manual compaction recovery precedes narrow delivery | PC-62 |
| G-63 | D-5: Automatic compaction cannot request manual idle flush | PC-63 |
| G-64 | D-5: Status never grants a delivery receipt | PC-64 |
| G-65 | D-5: Actions arriving during flush survive coalescing | PC-65 |
| G-66 | D-5: Late-confirm dispatch receives only exact returned source IDs | PC-66 |
| G-67 | D-5: Live/grace dispatch share atomic publication claim | PC-67 |
| G-68 | D-5,D-7: Cancellation does not release a still-running publication owner | PC-68 |
| G-69 | D-6: Review target authority comes from typed queue target, not tags | PC-69 |
| G-70 | D-6: Review correlation requires Channel origin | PC-70 |
| G-71 | D-6: Review correlation requires exact review key | PC-71 |
| G-72 | D-6: Review authority must match recipient session | PC-72 |
| G-73 | D-6: Review prompt must match complete stored body | PC-73 |
| G-74 | D-6: Review enqueue and typed authority commit atomically | PC-74 |
| G-75 | D-6: Review comment/thread update and consume stamp are atomic | PC-75 |
| G-76 | D-6: New review authority survives empty-map restart | PC-76 |
| G-77 | D-6: Legacy upgrade never invents typed review target | PC-77 |
| G-78 | D-6: Expired review authority retains 60-minute TTL withholding | PC-78 |
| G-79 | D-2,D-5: Empty catch-up reconciles owed work without historical replay | PC-79 |
| G-80 | D-2: Transcript retention excludes pending action sessions | PC-80 |
| G-81 | D-2,D-3: Retention rechecks pending actions under mutation lease | PC-81 |
| G-82 | D-2,D-6: Queue retention preserves review/downstream correlation while owed | PC-82 |
| G-83 | D-7: Cancellation leaves the interrupted phase pending | PC-83 |
| G-84 | D-7: Shutdown joins workers and scopes through actual completion | PC-84 |
| G-85 | D-7: Restart reconciles complete prior prompt before retrying input | PC-85 |
| G-86 | D-7: Within-process reconnect does not start second worker cohort | PC-86 |
| G-87 | D-7: Truncated identity cannot confirm delivery | PC-87 |
| G-88 | D-7: Attempt sequence floor excludes prior matching prompts | PC-88 |
| G-89 | D-7: Accepted generation fences receipt recovery | PC-89 |
| G-90 | D-7: Wall-time floor excludes stale/null-timestamp receipt | PC-90 |
| G-91 | D-7: Screen/output/status/action ack cannot replace complete receipt | PC-91 |
| G-92 | D-7,R-2: Multi-line body preserves LF | PC-92 |
| G-93 | D-7,R-2: Multi-line input uses bracketed paste | PC-93 |
| G-94 | D-7,R-2: Submission Enter is a separate write | PC-94 |
| G-95 | D-7,R-4: Typed stale exit cannot close current generation or publish | PC-95 |
| G-96 | D-4: Diagnostic failure cannot reject committed ingestion | PC-96 |
| G-97 | D-4,D-7: Worker timeout never stops an input-waiting session | PC-97 |
| G-98 | D-5: Parent completion enqueue failure retains durable report obligation | PC-98 |
| G-99 | D-5: Compaction enqueue failure retains durable recovery obligation | PC-99 |
| G-100 | D-5: Per-session UI action publication preserves stored order | PC-100 |
| G-101 | D-3: Database I/O observes precommit cancellation | PC-101 |
| G-102 | D-4: Retrying effects release worker capacity between attempts | PC-102 |
| G-103 | D-5: Status cannot overwrite committed Working state | PC-103 |
| G-104 | D-6: Legacy migration never fabricates review consumption | PC-104 |
| G-105 | D-2,D-6: Upgrade does not backfill historical transcript actions | PC-105 |
| G-106 | D-2: Session retention cannot cascade-delete pending action evidence | PC-106 |
| G-107 | D-7: Queue cancellation cannot promote a delivery verdict | PC-107 |
| G-108 | D-7,R-2: Late truncated receipt cannot authorize retyping | PC-108 |
| G-109 | D-7: Successful first confirmation sends no extra Enter | PC-109 |
| G-110 | D-7: Worker timeout never releases or parks a waiting session seat | PC-110 |
| G-111 | D-5: Final delivery gate preserves attempt cap | PC-111 |
| G-112 | D-2,D-4: No expiration deletes unfinished action debt | PC-112 |
| G-113 | D-8,R-6: Durable UI reconstruction preserves original NUL/literal U+FFFD and source fields across restart and safe-stub fallback; stored rows remain sanitized/stubbed | PC-113 |

### Positive controls

Each row means: break the named G-n with the **compiling production defect**
described, run the exact method at right, and expect its named assertion to turn
red in the listed variant. All other admission predicates remain valid so that
the defective guard is reached. Case setup must record the real gate/phase/fault
entry, action ID and recipient identity before evaluating its oracle. A timeout
is only a hang bound; unrelated fixture/build/cleanup failure is not red.

These are functional mutation controls, not test-only stubs or parameter flips
that change expected output. Before any mutation, run the target method green;
then break/red/restore/fresh-build/green on landed source L. Use only
--treenode-filter "/*/*/ClassName/ExactTestMethod" after replacing ClassName and
ExactTestMethod with the fully bound target below. A parameterized method runs
all of its listed arguments; record which named variant hit the decisive red.
Never run a whole class/suite during a PC cycle. No disabled guard remains in the
tree. Keep each PC's source diff, compile outcome, executed count, failed
assertion, restoration digest and final green outside a SourceLanding snapshot.

Mutation payloads targeting new code are recipe-bound here; exact compiling
diffs remain binding required after Code as stated above. Mutation does not
repair product/tests. A missed/invalid control returns a classified finding;
Review can approve ordinary implementation before PCs run but cannot call it
PC-clean. **All PC-1 through PC-113 must run**, including every named variant,
before the post-land verification companion may be closed. Current execution
status: 0/113 run; 113 required, not waived.

| PC / guard | Variant and compiling defect | Exact method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 / G-1 | live: await the boundary queue effect from ObserveTranscriptAsync again | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | before-release A/B committed |
| PC-2 / G-2 | startup: await the admitted action completion in startup SyncTranscriptAsync | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | before-release A/B committed |
| PC-3 / G-3 | subscription-ready: await the action during subscription SyncTranscriptAsync | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | before-release A/B committed |
| PC-4 / G-4 | signalr-publish: move awaited publication back ahead of transcript persistence | SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe | B committed and settled before release |
| PC-5 / G-5 | sent-refinement: await ConfirmSentRefinementsAsync from the ingest caller | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | Observe returned while lock held |
| PC-6 / G-6 | manual-compaction: await compaction recovery inline after persistence | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | A/B committed while recovery held |
| PC-7 / G-7 | blocked-exit: await FlushIfIdleAsync in ObserveAgentStatusAsync | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | B committed before flush release |
| PC-8 / G-8 | batch-fail: commit transcript batch before saving its actions | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | rows/actions have equal committed identity roster |
| PC-9 / G-9 | fallback-fail: save fallback row and action in separate commits | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | every landed fallback row owns its action |
| PC-10 / G-10 | fallback-fail: admit action for the rejected row despite failed save | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | failed UUID has zero actions/effects |
| PC-11 / G-11 | safe-stub: discard manual-boundary intent when building its stub action | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | stored stub plus manual narrow-recovery action, no report settlement |
| PC-12 / G-12 | replay-after-ack: admit fresh action for an already stored UUID/kind | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | replay action count zero and recipient count unchanged |
| PC-13 / G-13 | replay-after-ack: make the (session,sequence,trigger kind) index nonunique in the generated migration and mapping; apply that migration to the case's fresh owned store | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | second direct duplicate-key insert is refused |
| PC-14 / G-14 | replay-after-ack: reset phase to first on key conflict | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | saved phase remains advanced; prior receiver count unchanged |
| PC-15 / G-15 | same-api-siblings: remove sibling ApiCallId eligibility check | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | eligible boundary effect count one |
| PC-16 / G-16 | mixed-stream-pulls: skip same-batch membership update while retaining replay query | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | exact roster contains each pair once |
| PC-17 / G-17 | mixed-stream-pulls: key dedup by UUID alone | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | exact roster contains both kinds |
| PC-18 / G-18 | mixed-stream-pulls: use incoming sequence instead of next stored sequence | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | stored sequence roster matches increasing expected values |
| PC-19 / G-19 | synthetic-race: omit lease acquisition on synthetic append | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | peak same-session writers one and exact roster |
| PC-20 / G-20 | missing-store: allow persistence to continue with missing SessionStateStore | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | missing-store fails before transcript/action write |
| PC-21 / G-21 | batch-fail: publish proposed rows before commit acknowledgement | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | state revision/count unchanged for rolled-back rows |
| PC-22 / G-22 | cancel-before-commit: replace BeginWriteAsync cancellation token with CancellationToken.None | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | cancelled lease/save operation completes without new row/action |
| PC-23 / G-23 | cancel-after-commit: skip publish/reload when token becomes cancelled after commit | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | store agrees with committed DB frontier |
| PC-24 / G-24 | queue-lock-catchup: await queue.OnTurnEndAsync from CatchUpTranscriptAsync | SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | catch-up returns while caller still owns queue lock |
| PC-25 / G-25 | live: hold the write lease while awaiting action completion | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | A prompt committed before confirm-read release |
| PC-26 / G-26 | phase-failure: retain action transaction until held receiver returns | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | receiver observes no current transaction on action scope |
| PC-27 / G-27 | production-capacity: reuse one scoped AppDbContext across worker executions | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | four worker context IDs are distinct before effects release |
| PC-28 / G-28 | commit-wake-gap: TryWrite the hint before transcript/action commit | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | hint observer can read committed action at offer |
| PC-29 / G-29 | overflow: replace TryWrite by awaited bounded WriteAsync | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | overflow transcript commits before worker release |
| PC-30 / G-30 | production-capacity: use unbounded hint channel | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | observed queued hints never exceed 128 |
| PC-31 / G-31 | production-capacity: start a fifth execution worker | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | peak active effect workers equals four |
| PC-32 / G-32 | duplicate-owner: bypass per-session execution ownership check | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | max same-session active owners one |
| PC-33 / G-33 | poison-page: remove Take(pageSize) from discovery query | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | captured materialized page count <=128 |
| PC-34 / G-34 | poison-page: restart keyset scan from first failing key instead of advancing | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | later-session recipient observed with first failure still held |
| PC-35 / G-35 | poison-page: keep same-session ownership and consume its next page before yielding | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | later-session routing starts before page-two release |
| PC-36 / G-36 | overflow: drop overflow sweep request | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | overflow-only action received before backstop is allowed |
| PC-37 / G-37 | backstop: disable timer-driven pending discovery | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | unhinted action received after 1s tick without startup/overflow signal |
| PC-38 / G-38 | before-wake: skip startup pending scan | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | same action delivered before backstop is allowed |
| PC-39 / G-39 | retry-clock: set retry DueAt to now instead of the configured backoff | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | no phase attempt before each 1/2/4/8/16/30/30s due; B progresses |
| PC-40 / G-40 | phase-failure: map caught failure/deferred result to Completed | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | failed phase remains pending, recipient absent then recovered |
| PC-41 / G-41 | phase-failure: restart retry at phase zero | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | earlier receiver count remains one |
| PC-42 / G-42 | frontier-race: run turn-end queue phase before task routing phase | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | first next-input byte follows all owed route receipts |
| PC-43 / G-43 | channel-bound: call unbound latest-turn channel dispatcher | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | channel A receives answer A, channel B receives answer B |
| PC-44 / G-44 | review-bound: select max TurnEnd instead of action boundary | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | thread A comment is answer A exactly |
| PC-45 / G-45 | task-bound: extract newest report instead of action's marked window | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | task/attempt result equals its own report |
| PC-46 / G-46 | prompt-window: choose last prompt of either kind without UserPrompt priority | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | original recipient receives complete own answer |
| PC-47 / G-47 | prompt-window: cap at any QueuedUserPrompt | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | answer retains post-queued in-turn text |
| PC-48 / G-48 | prompt-window: remove next-opener upper bound | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | answer excludes next turn's sentinel text |
| PC-49 / G-49 | early-text: consume correlation when response text is empty | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | pending correlation survives boundary then later text delivers |
| PC-50 / G-50 | trailing-text: leave accepted text watermark unchanged | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | each trailing segment accepted once across restart/replay |
| PC-51 / G-51 | channel-bound: ignore containsApiErrorStub in bound channel path | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | error subcase has zero receiver bodies and source remains owed |
| PC-52 / G-52 | api-error: ignore error withholding before comment/consume | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | zero new comment/stamp for mixed error turn |
| PC-53 / G-53 | task-bound: admit housekeeping final text to marked-report success path | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | original task stays open/non-success; later genuine report settles |
| PC-54 / G-54 | task-bound: ignore RepliedAtSequence lower bound | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | pre-answer report cannot settle resumed task |
| PC-55 / G-55 | frontier-race: flush using admission frontier without re-reading it | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | newly committed owed turn routes before next input |
| PC-56 / G-56 | working: bypass final current-state Working check | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | zero input/attempts until genuine idle |
| PC-57 / G-57 | herdr-blocked: bypass final blocked check | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | zero input/attempts while blocked |
| PC-58 / G-58 | rules-held: bypass final rules eligibility check | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | zero input/attempts while rules held |
| PC-59 / G-59 | generation-changed: replace action generation with current generation before input | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | zero body/Enter from stale action |
| PC-60 / G-60 | task-attempt-changed: omit task-attempt match in settlement admission | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | replacement attempt unchanged |
| PC-61 / G-61 | manual-compaction: call OnTurnEndAsync instead of FlushIfIdleAsync | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | SessionFinished count zero and stale task result unchanged |
| PC-62 / G-62 | manual-compaction: flush before recovery enqueue | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | recovery note is first intended queued delivery and has full receipt |
| PC-63 / G-63 | automatic-compaction: remove manual-kind predicate | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | no narrow-flush entry for auto boundary |
| PC-64 / G-64 | blocked-exit: set Delivered from done status before transcript match | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | verdict remains unconfirmed until exact prompt; no state override |
| PC-65 / G-65 | action-during-flush: delete all session actions when current flush completes | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | new durable action remains then receives its own receipt |
| PC-66 / G-66 | all four arguments: replace returned IDs with all open session correlations | ChannelReplyLateConfirmDispatchTests.Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes | first receiver body is earlier answer; later source not consumed |
| PC-67 / G-67 | both arguments: skip publication claim acquisition in runtime bound path | ChannelReplyLateConfirmDispatchTests.Grace_and_runtime_redispatch_share_one_atomic_publication_owner | one accepted receiver publication |
| PC-68 / G-68 | both arguments: release claim as soon as caller wait cancels | ChannelReplyLateConfirmDispatchTests.Caller_cancellation_does_not_release_a_still_publishing_direct_reply | producer Attempts=1 before held transport release |
| PC-69 / G-69 | forged-target: choose target thread by parsing model-written tag | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | authorized thread alone receives comment; forged thread unchanged |
| PC-70 / G-70 | wrong-origin: remove Origin=Channel predicate | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | no comment/consume for origin-mismatched row |
| PC-71 / G-71 | forged-target: ignore ConversationKey/ReviewThreadId agreement | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | mismatched-key subcase has no comment/consume |
| PC-72 / G-72 | wrong-session: remove recipient-session predicate | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | other session cannot consume authorized row |
| PC-73 / G-73 | incomplete-body: accept tag/prefix identity without completeness | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | head-only prompt produces no comment/consume |
| PC-74 / G-74 | enqueue-failure: save queue row before writing ReviewThreadId | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | post-commit recovered queue row has authorized target and delivers |
| PC-75 / G-75 | atomic-comment: commit comment before consume stamp in a second transaction | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | restart yields exactly one comment and one consumed correlation |
| PC-76 / G-76 | map-free-restart: require in-memory pending map before durable lookup | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | fresh worker writes correct comment from stored queue row |
| PC-77 / G-77 | legacy-upgrade: populate new target by parsing old review key in migration | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | historical ReviewThreadId remains null |
| PC-78 / G-78 | ttl: remove TTL cutoff from durable candidate query | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | expired row unconsumed with no new comment |
| PC-79 / G-79 | empty-sync: return before admitting catch-up when no row added | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | owed task/parent receipt appears; historical UI/replies unchanged |
| PC-80 / G-80 | prune-candidate: omit pending-action exclusion from transcript candidate discovery | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | pending session absent from candidates; all turn rows survive |
| PC-81 / G-81 | prune-race: omit under-lease pending-action recheck | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | newly admitted pending turn survives candidate-to-delete race |
| PC-82 / G-82 | correlation-retention: treat ChannelReplySettledAt as enough to prune an unconsumed review row | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | authorized queue row/body survives until review consumption |
| PC-83 / G-83 | during-confirm: ack interrupted action phase in cancellation catch | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | pending phase/verdict unconfirmed after stop |
| PC-84 / G-84 | uncooperative-effect: return from Stop when timeout wins while effect still runs | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | Stop remains incomplete and scope undisposed before release |
| PC-85 / G-85 | after-input: retry body without interrupted-Sent receipt reconciliation | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | one total body/Enter and one matching prompt across restart |
| PC-86 / G-86 | reconnect-cohort: start new coordinator workers on each pump subscription | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | one cohort and <=4 active workers after reconnect |
| PC-87 / G-87 | truncated: treat Identity as Complete in delivery confirmation | SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | Pending/Truncated, never Delivered |
| PC-88 / G-88 | old-sequence: remove Sequence > baseline predicate | SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | old receipt never confirms current attempt |
| PC-89 / G-89 | old-generation: ignore LastDeliveryGeneration when reconciling attempt | SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | old-generation receipt cannot grant current success |
| PC-90 / G-90 | old-time: remove timestamp floor from unobservable confirmation | SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | no Transcript-confirmed success from old/null timestamp |
| PC-91 / G-91 | absent: return Transcript-confirmed Delivered on screen advance | SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | no transcript-confirmed verdict without UserPrompt |
| PC-92 / G-92 | live: replace LF with CR in queued body transport | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | body contains LF and no CR |
| PC-93 / G-93 | live: omit bracketed-paste framing on multiline body write | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | body write begins/ends exact paste markers |
| PC-94 / G-94 | live: append carriage return to body write instead of separate submit | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | wire roster has one framed body then one lone Enter |
| PC-95 / G-95 | single: forward exit without its accepted generation fence | SessionRunnerEventPumpTests.C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition | Running row unchanged and zero terminal notifications |
| PC-96 / G-96 | signalr-publish/false/true: let phase logger exception escape ingestion | SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe | B commit/settlement succeeds with throwing logger |
| PC-97 / G-97 | uncooperative-effect: call existing session stop on worker deadline | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | kill/stop/seat-release calls zero while action remains pending |
| PC-98 / G-98 | parent-handoffs H1: ack settlement delivery phase when parent enqueue throws | SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | same task-attempt eventually has complete parent UserPrompt |
| PC-99 / G-99 | manual-compaction H1: ack compaction recovery phase when queue enqueue throws | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | same recovery identity eventually has full recipient UserPrompt |
| PC-100 / G-100 | duplicate-owner: load action page in descending stored sequence | SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | fake receiver ordered UUID roster equals committed order |
| PC-101 / G-101 | cancel-before-commit: replace action/transcript SaveChangesAsync token with CancellationToken.None | SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | cancelled save rolls back with zero newly committed row/action |
| PC-102 / G-102 | retry-clock: await retry delay inside each worker instead of releasing its slot | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | eligible B completes while four failed sessions are waiting for due time |
| PC-103 / G-103 | blocked-exit: publish an idle state snapshot on done status | SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | committed Working state unchanged without transcript boundary |
| PC-104 / G-104 | legacy-upgrade: populate ReviewReplyConsumedAt for historical rows | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | all historical consume stamps remain null |
| PC-105 / G-105 | legacy-upgrade: insert actions for historical transcript rows in migration | SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | zero historical action/UI/comment replay after migration/startup |
| PC-106 / G-106 | prune-candidate: omit pending-action predicate from session deletion candidates | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | pending owning session and its whole turn survive session prune |
| PC-107 / G-107 | during-confirm: return Delivered when confirm wait is cancelled | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | queue verdict remains unconfirmed absent full prompt |
| PC-108 / G-108 | single: return no match instead of Truncated on late incomplete identity | SessionMessageQueueDeliveryVerificationTests.Late_confirm_does_not_promote_a_truncated_body_to_sent | Adapter.Inputs empty and queue row remains Pending/parked |
| PC-109 / G-109 | live: send a second Enter immediately after initial submit | SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | wire Enter count exactly one at receipt |
| PC-110 / G-110 | uncooperative-effect: invoke existing seat-release/parking path at worker deadline | SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | seat-release/parking calls zero and same session retains seat |
| PC-111 / G-111 | working: ignore MaxDeliveryAttempts at final queue selection | SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | capped idle-row subcase has zero input and unchanged attempts |
| PC-112 / G-112 | retry-clock: delete failed actions at bounded retry/age threshold | SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | action persists after long clock advance and later recovers |
| PC-113 / G-113 | Both sanitized and safe-stub: in the planned SessionRuntimeActionCoordinator.RestoreTranscriptPayload bypass application of UiProjectionDelta and return the stored-row projection with ProducerSequence still applied. Leave transaction/admission, startup discovery and publication/ack intact; this is a compiling payload-only defect. | AgentSessionRuntimeTests.C1259_deferred_UI_payload_preserves_original_fields_after_restart | Exact filter /*/*/AgentSessionRuntimeTests/C1259_deferred_UI_payload_preserves_original_fields_after_restart; 2 executed per pass: baseline 2 green, mutation 2 assertion failures, restored 2 green, 0 skipped. Each red must be original DTO/text-NUL fidelity after the restart publication barrier; stored U+FFFD/stub assertions still pass. |

The following internal subcases are mandatory in their owning V method (one
Arguments result can contain several isolated setup/run/cleanup subcases):
V-3 cancel-before-commit gates lease acquisition and actual SaveChanges separately;
V-3 replay-after-ack's unique-index subcase applies the real migration to a fresh
owned store rather than inheriting a cached template index (otherwise PC-13 would
not reach the mutated DDL);
V-5 channel-bound includes a mixed API-error turn; task-bound covers housekeeping,
cancelled/error and answered-Blocked old watermark; V-6 working includes a capped
idle row after its ordinary working-to-idle case; V-8 poison-page checks discovery
and execution pages plus page-two yield separately; V-9 uncooperative-effect
records scope disposal, stop/kill, park/seat-release and owner counts; V-10
retry-clock fills all four slots with failures, advances beyond retention/retry
age and then recovers; prune-candidate invokes transcript and session pruning
separately; V-11 forged-target has both malicious model tag and mismatched queue
key subcases; legacy-upgrade independently inspects target, consume and historical
action backlog. Every internal subcase logs its case label and assertion result,
but none is counted as another TUnit execution.

### Out of scope

Native tailer debounce/file offsets, actual provider input/Enter behaviour,
ConPTY/Herdr/Grok canaries, real chat/broker/browser receipt and desktop load
qualification are unchanged and cannot be certified by these substitutes.
No live messages, fleet setting, parking enablement, session lifecycle timeout,
distributed executor, all-Unit lane or whole assembly. Direct external send's
uncertain crash window remains uncertain under CARD-1247; do not make it
exactly-once by changing the test's receiver definition.

Review must additionally read the full diff and call graph, rather than infer
these properties from green tests:

- Every live/sync/status producer stops at commit/admission and every nested
  queue/channel/review/task operation runs after the write lease/transaction.
  CatchUp remains persistence-only under an existing queue lock. Check raw
  held-output abandonment stays distinct from joined durable action workers.
- Verify production DI registration and singleton/scoped lifetimes, mandatory
  state store, one worker cohort, absence of unowned per-event tasks/unbounded
  side dictionaries, and cancellation ownership through scope disposal.
- Verify action schema/indexes, CLI migration/snapshot consistency, unique keys,
  phase outcome propagation and row-by-row fallback transactions; inspect both
  retention discovery and leased recheck, including session cascade and review
  queue evidence. Test failure injection does not prove an uninspected branch.
- Verify all sequence-bound dispatcher callers, source versus stored sequence,
  owning-prompt/ApiCallId windows and current generation/task-attempt fences.
  An existing sweep/API entry point must retain its previous contract.
- Verify ReviewThreadId is written only by authorized dispatch, never derived
  from model tags or upgrade SQL; review consume is independent of channel
  publication ownership. Migration must leave legacy rows unknown and logs must
  name identities without leaking transcript bodies.
- Inspect the queue diff for unchanged 30s/7s/Enter-only confirmation policy and
  full body/floor checks. Review the completed case/PC bindings, expected-red
  record and every qualified ordinary receipt at exact clean source SHA.
  Legacy S0's old blocking passes and failed receipt validation remain historical
  diagnostics, never substituted for this acceptance proof.

### Checkpoints

This is the closed ordinary scope. All paths are repository-relative. CP-1 is
intentionally assertion-red at S1; execute all seven cases and retain failure
evidence. The importer/runner still returns failure for that row: do not rewrite
its receipt to green or select the next group implicitly. S2-contracts and S3-contracts are test-first commits inside the existing S2/S3
slices: schema/DI/API prerequisites must compile and permit real fixtures before
CP-2/CP-3, but the new guarded behaviour has not yet been implemented. Do not
count missing table/type, NotImplementedException or failure to enter a required
fault seam as behavioural red. Each new behaviour needs its named assertion red;
if an earlier slice already supplied it, retain that earlier red and its distinct
post-land control rather than breaking correct code during Code. Preserved V-12
negative guards may already pass; their independent compiling-defect reds are
PC-87..PC-91 and PC-108. These are not an asserted historical defect.
The completed source
must run CP-4..CP-13 at one clean committed SHA after S2-S4. Reuse CP-4's one
isolated build only within that unchanged After group. No whole Unit lane
(CARD-1287); the explicitly named Unit class in CP-11 is portable and small.
Lane assignment: CP-1..CP-10 and CP-12..CP-13 are Integration, Linux-capable;
CP-11 retains its exact mixed Integration/portable Unit class selection.
The Plan correction re-read runner defaults/catalogue at 17:20 UTC on 2026-10-15:
revision 2, no kind overrides, eligible Linux and Windows lanes. Re-read at dispatch;
omit -Runner and -Platform, with no fleet location or OS requirement added.

PostgreSQL: CP-1 through CP-13 all need a real test database because each row
contains database-consuming tests. The held-output methods in CP-12 and
HerdrStatusDisagreementTests within CP-11 do not themselves use PostgreSQL.
V-11 legacy-upgrade needs an owned clone and real migrations; merely applying
the new EF model to an already migrated fixture is not upgrade proof.
Minimums count Arguments results: 70 baseline selections, 126 final green = 196 planned
ordinary executions. Zero failures/skips required for every final green row.
The method unions and class unions below use escaped literal pipes exactly
as required by docs/testing-and-build.md; method-name prefixes exist solely for
TUnit's argument suffix matching, and do not select unrelated methods.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1259-red/ | ingress-red | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)\|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*) | V-1,V-2 baseline | all 7 executed; intended pre-release progress assertions red, no fixture/build/cleanup failure | 7 | 5 |
| CP-2 | S2-contracts | tests/Antiphon.Tests -> bin-c1259-action-red/ | action-baseline | /*/Antiphon.Tests.Application/SessionRuntimeActionTests/(C1259_commit_and_effect_admission_are_atomic*)\|(C1259_stream_pull_and_replay_preserve_order*)\|(C1259_saturation_keeps_ingestion_live_and_recovers_overflow*)\|(C1259_shutdown_joins_and_restart_reconciles_submission*)\|(C1259_retry_and_retention_preserve_pending_turn*) | V-3,V-4,V-8,V-9,V-10 | all 28 executed; named new-behaviour assertions red; preserved guards may pass | 28 | 8 |
| CP-3 | S3-contracts | tests/Antiphon.Tests -> bin-c1259-routing-red/ | routing-baseline | /*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/(C1259_buffered_turns_route_their_own_recipients_before_flush*)\|(C1259_new_working_or_blocked_state_defers_old_idle_action*)\|(C1259_compaction_refinement_and_status_cannot_block_ingestion*)\|(C1259_review_recovery_uses_authorized_queue_identity_once*)\|(C1259_missing_or_truncated_prompt_never_confirms*) | V-5,V-6,V-7,V-11,V-12 | all 35 executed; named new-behaviour assertions red; preserved guards may pass | 35 | 10 |
| CP-4 | S2-S4 | tests/Antiphon.Tests -> bin-c1259-green/ | ingress-green | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)\|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*) | V-1,V-2 | all 7 listed, 0 failed/skipped | 7 | 5 |
| CP-5 | S2-S4 | CP-4 | action-lifecycle | /*/Antiphon.Tests.Application/SessionRuntimeActionTests/(C1259_commit_and_effect_admission_are_atomic*)\|(C1259_stream_pull_and_replay_preserve_order*)\|(C1259_saturation_keeps_ingestion_live_and_recovers_overflow*)\|(C1259_shutdown_joins_and_restart_reconciles_submission*)\|(C1259_retry_and_retention_preserve_pending_turn*) | V-3,V-4,V-8,V-9,V-10 | all 28 listed, 0 failed/skipped | 28 | 6 |
| CP-6 | S2-S4 | CP-4 | action-routing | /*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/(C1259_buffered_turns_route_their_own_recipients_before_flush*)\|(C1259_new_working_or_blocked_state_defers_old_idle_action*)\|(C1259_compaction_refinement_and_status_cannot_block_ingestion*)\|(C1259_review_recovery_uses_authorized_queue_identity_once*)\|(C1259_missing_or_truncated_prompt_never_confirms*) | V-5,V-6,V-7,V-11,V-12 | all 35 listed, 0 failed/skipped | 35 | 8 |
| CP-7 | S2-S4 | CP-4 | transcript-regression | /*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations*)\|(C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence*)\|(Error_turn_end_is_acted_on_once_replay_dedups*) | R-1 | all 3 listed, 0 failed/skipped | 3 | 2 |
| CP-8 | S2-S4 | CP-4 | delivery-regression | /*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/(C514_Multiline_release_keeps_LF_paste_and_separate_Enter*)\|(Manual_boundary_flushes_a_message_stranded_before_the_compaction*)\|(Manual_boundary_with_an_empty_queue_publishes_no_finished_event*)\|(A_clipped_prefix_parks_as_truncated_not_sent*)\|(Late_confirm_does_not_promote_a_truncated_body_to_sent*) | R-2 | all 5 listed, 0 failed/skipped | 5 | 2 |
| CP-9 | S2-S4 | CP-4 | late-confirm-regression | /*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/(Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes*)\|(Grace_and_runtime_redispatch_share_one_atomic_publication_owner*)\|(Caller_cancellation_does_not_release_a_still_publishing_direct_reply*) | R-3 | all 8 listed, 0 failed/skipped | 8 | 3 |
| CP-10 | S2-S4 | CP-4 | stale-exit-regression | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition | R-4 | all 1 listed, 0 failed/skipped | 1 | 1 |
| CP-11 | S2-S4 | CP-4 | existing-guard-classes | /*/Antiphon.Tests.Application/(SessionStateCommitTests*)\|(ReviewReplyDispatcherTests*)\|(HerdrStatusDisagreementTests*)/* | R-5 | all 29 listed, 0 failed/skipped | 29 | 3 |
| CP-12 | S2-S4 | CP-4 | held-and-refinement-guards | /*/Antiphon.Tests.Application/(SessionRunnerHeldEventDeliveryTests*)\|(AgentTaskMidTurnRefineTests*)/(C1205_Blocked_held_callback_stops_on_host_cancellation*)\|(C1205_Blocked_held_callback_stops_at_monotonic_deadline*)\|(C1205_Last_transcript_pull_cancellation_preserves_one_output*)\|(C1170_LiveUserPromptConfirmsSentRefinementWithoutASyncPump*)\|(C1188_UserPromptDoesNotWaitOnTheQueueLockWithoutASentRefinement*)\|(C1188_SentRefinementStillWaitsOnTheQueueLockBeforeConfirming*) | R-5 | all 7 listed, 0 failed/skipped | 7 | 2 |
| CP-13 | S2-S4 | CP-4 | original-ui-payload-guard | /*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C561_a_NUL_in_tool_result_text_persists_as_U_FFFD*)\|(C1259_deferred_UI_payload_preserves_original_fields_after_restart*) | R-6,D-8 | all 3 listed (C561=1, sanitized=1, safe-stub=1), 0 failed/skipped | 3 | 3 |

**Code starts with CP-1**, after committing/pushing S1's tests; retain its seven
intended assertion-red results. Next run CP-2 at S2-contracts, then CP-3 at
S3-contracts. CP-4..CP-13 are the final S2-S4 group, not the initial baseline.
Run the checkpoint tool with run --plan <this-file> --after S1 then --after S2-contracts, then --after S3-contracts, and finally
--after S2-S4 --expected-source-sha <committed-implementation-sha>. Supply the
corresponding committed S1 SHA for CP-1 too. Await every executor to terminal
exit; 75 means still running. Rows acquire host build slots. Any prerequisite
tool bootstrap build must use scripts/build-slot.ps1 and be reported separately.
Never wrap the whole checkpoint executor in a second slot lease.

Code retains unedited CHECKPOINT/provenance lines and qualified receipts, runs
scripts/check-evidence-diff.ps1 over recorded base..pushed HEAD and removes only
its owned alternate outputs. Missing cases, lost assertions, fixture errors,
unexpected failures and unbound required payloads remain incomplete gates.
Review is Final/Full for this closed scope, not whole-assembly verification.

### Cost

All amounts below are **estimated wall-clock minutes**, not measured receipts.
Slot wait, authoring and unexpected failure repair are additional; no cost is
silently converted from test counts.

- Ordinary V/R floor (Code): **58 minutes**, sum of CP EstimatedMinutes:
  CP-1=5, CP-2=8, CP-3=10, CP-4=5, CP-5=6, CP-6=8, CP-7=2,
  CP-8=2, CP-9=3, CP-10=1, CP-11=3, CP-12=2, CP-13=3. Baseline selection floor
  23 minutes; final green floor 35 minutes. This contains four isolated builds,
  estimated 1.5 minutes each (6 build + 52 test/fixture minutes). Optional cold
  checkpoint-tool bootstrap is separately budgeted at 2 minutes and gated.
- PC floor (Mutation): **733.25 minutes** serial. Pristine landed-pack setup/build
  3 minutes, then 113 independent cycles. Each cycle budgets two isolated
  builds at 1.5 minutes each, 0.25 minutes for break/restore/source checks,
  and three executions of the exact target method (baseline green, red,
  restored green). No whole-class fallback. The table below enumerates all
  targets and per-pass test minutes, so the total can be recalculated.
- Total execution provision including optional tool bootstrap:
  **2 + 58 + 733.25 = 793.25 minutes**. Without bootstrap, 791.25 minutes.
  This is not a prediction of elapsed calendar time with parallel independent
  mutation packs, and no parallel savings are assumed.
- Savings: four builds rather than thirteen independent ordinary-row builds
  save nine builds, estimated **13.5 minutes**. Final rows run only named
  classes/methods once. No saving is claimed for skipping an ordinary case or
  any PC. Most controls touch the same runtime/coordinator/routing files;
  speculative batching could mask defects, so the PC estimate credits zero
  concurrency savings. Baseline red runs are now explicit costs rather than
  hidden extra authoring runs.

For every target below, expand Class.Method to
/*/*/Class/Method; this is the **exact PC filter**, never its whole class.
The PC count is the number of distinct mutation cycles against that method, not
a TUnit execution count. Each method run still expands the roster above.

| Exact target Class.Method | PC cycles | Minutes per method pass | Cycle subtotal (count x (3.25 + 3 x pass)) |
|---|---:|---:|---:|
| SessionRunnerEventPumpTests.C1259_confirming_prompt_and_other_session_progress_before_confirmation | 8 | 1 | 50 |
| SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe | 2 | 0.5 | 9.5 |
| SessionRuntimeActionRoutingTests.C1259_compaction_refinement_and_status_cannot_block_ingestion | 10 | 1 | 62.5 |
| SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic | 12 | 0.75 | 66 |
| SessionRuntimeActionTests.C1259_stream_pull_and_replay_preserve_order | 7 | 0.75 | 38.5 |
| SessionRuntimeActionTests.C1259_retry_and_retention_preserve_pending_turn | 10 | 1 | 62.5 |
| SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow | 11 | 1 | 68.75 |
| SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission | 8 | 1 | 50 |
| SessionRuntimeActionRoutingTests.C1259_buffered_turns_route_their_own_recipients_before_flush | 14 | 1.5 | 108.5 |
| SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once | 13 | 2 | 120.25 |
| SessionRuntimeActionRoutingTests.C1259_new_working_or_blocked_state_defers_old_idle_action | 7 | 0.5 | 33.25 |
| ChannelReplyLateConfirmDispatchTests.Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes | 1 | 0.75 | 5.5 |
| ChannelReplyLateConfirmDispatchTests.Grace_and_runtime_redispatch_share_one_atomic_publication_owner | 1 | 0.5 | 4.75 |
| ChannelReplyLateConfirmDispatchTests.Caller_cancellation_does_not_release_a_still_publishing_direct_reply | 1 | 0.5 | 4.75 |
| SessionRuntimeActionRoutingTests.C1259_missing_or_truncated_prompt_never_confirms | 5 | 1 | 31.25 |
| SessionRunnerEventPumpTests.C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition | 1 | 0.25 | 4 |
| SessionMessageQueueDeliveryVerificationTests.Late_confirm_does_not_promote_a_truncated_body_to_sent | 1 | 0.25 | 4 |
| AgentSessionRuntimeTests.C1259_deferred_UI_payload_preserves_original_fields_after_restart | 1 | 1 | 6.25 |

Cycle subtotal **730.25** + pristine setup/build **3** = **733.25** minutes.
PC-113 runs its exact two-case method three times (2 green / 2 intended red /
2 restored green = 6 results), with the same two-build cycle allowance as the
other controls. Its pass estimate rises from 0.5 to 1 minute for cold DI restart
and stub recovery. These remain prospective budgets, not execution evidence.

Design handoff inventory: **guards=113, mapped=113, missing=0, duplicate PC maps=0**.
Execution remains **all 113 PCs required after land; 0 run in TestDesign**.
Code must preserve this inventory and bind the concrete payloads/variants; ordinary
Review verifies that none disappeared. Any new unverifiable seam returns to Plan;
an actual product-policy choice returns to Decide. Current next stage: **Code**;
D-8 and R-6/PC-113 resolve and bind B-1 without another TestDesign dispatch.

Historical TestDesign validation (before this correction): a foreground PowerShell structural check passed the
13-row/9-column schema, build-reuse groups, 194 planned execution total,
56-minute ordinary sum, 113 one-to-one guard/PC mappings and append-only original
plan check. This was not the actual compiled importer or a product test/build.

Plan correction validation: the foreground PowerShell structural check passed
13 rows/9 columns, four build groups, 70 baseline + 126 final = 196 planned
executions, 58 ordinary minutes and 113 one-to-one guard/PC maps. Recalculation
gives 733.25 PC minutes and 793.25 total including optional bootstrap. Comparison
to the assigned base confirms D-1..D-7, V-1..V-12, R-1..R-5, G/PC-1..112 and
CP-1..CP-12 unchanged; only the two assigned Markdown files changed and
`git diff --check` passed. No compiled importer, product build/test or PC ran.
