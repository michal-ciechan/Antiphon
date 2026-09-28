# CARD-0519 — refused or interrupted outbound publication must remain recoverable

TestDesign task `60616b1c`, 2026-09-28. Source inspected at
`cd133de97700dbba517bce0cf967a193c8736a44`. Ready for Code. This document
specifies tests and the behavior they require; no implementation, test execution,
or mutation result is claimed. No earlier CARD-0519 plan exists in this checkout.
The defaults below make this a concrete Code handoff, not a claim that the proposed
publication service or its tests already exist.

## Finding and acceptance boundary

`ChannelReplyDispatcher.DispatchAsync` commits `ChannelReplySettledAt`, installs
`_dispatched`, and prepares attachments before its producer try/catch. An ordinary
producer exception reopens the rows, but cancellation, preparation failure, failure
of the reopening save, or death after the claim can leave them permanently settled.
The periodic sweep queries only unsettled Channel rows. Reopening also depends on
a later turn trigger to retry and extraction then considers the newest turn.

`DispatchFollowUpAsync` advances an in-memory watermark before preparation/send and
does not roll it back. `DispatchMachineTurnFollowUpAsync` settles before preparing
the body; its exception handler excludes cancellation and cannot survive a process
death. Machine rows are outside the Channel-only TTL sweep. Its bundle-stamp save
shares the producer catch, so a successful publish followed by a database failure
can also cause a duplicate. Multi-target publication currently reopens every source
when any target fails. These are separate paths, not one refusal test.

Publication in this card means acceptance by `IAntiphonMessagingProducer.SendAsync`
(the production implementation awaits Kafka `ProduceAsync`). It does **not** mean
the external person saw a reply. `GatewayOutboundService` currently auto-commits
offsets and logs adapter refusals; it provides no durable application receipt.
V-20 therefore observes an actual consumer and adapter, while explicitly stopping
short of claiming native Telegram/Slack delivery durability. A gateway outbox and
end-to-end native acknowledgement protocol are outside this server-publication fix.

### Behavior ratified for Code

- **D-1 — durable obligation before side effects.** Introduce a durable publication
  record and source membership, with a stable ID, session/owning-prompt identity,
  text sequence interval, producer path (main/trailing/machine), frozen destination,
  source queue IDs, original response, prepared envelope when available, attempt
  count/owner/expiry, next attempt time, last failure, accepted timestamp and durable
  incident stamp. Suggested concrete names: `ChannelOutboundPublication` and
  `ChannelOutboundPublicationService`. Claiming is not settlement. A source's
  `ChannelReplySettledAt` stays null until its publication is confirmed or its
  deliberate silence/terminal incident is committed. A trailing failure does not
  clear the already-published initial reply's settlement.
- **D-2 — immutable retry.** Once prepared, persist the complete `ChannelReply`
  including attachment bytes, kind, exact text, provider, conversation and handle.
  Recovery never reconstructs a different answer from the latest turn, current
  catalog, changed settings or modified files. A preparation failure retains the
  original response/paths and source evidence until preparation succeeds or the
  obligation is held with an incident. Existing missing/oversize-file annotations
  remain intentional successful preparation; an unexpected file-read exception
  is a failed attempt, not successful empty publication.
- **D-3 — target and interval ownership.** Use database uniqueness and a conditional
  attempt claim, not a singleton dictionary, to serialize each destination's text
  intervals. Persist all batch members. Confirmed target A is not republished when
  B fails. Different targets can have different outcomes in the same dispatch
  result. A late fragment retains the same target and its own interval; a newer
  prompt caps the old turn instead of erasing its pending fragment.
- **D-4 — bounded recovery without a new turn.** Add a production recovery worker
  with a startup pass and periodic passes. It discovers unmaterialized eligible
  completed turns as well as pending publications, including machine turns and
  late fragments. Reuse the existing correlation/turn-window policy; do not simply
  call the current newest-turn-only `OnTurnEndAsync` for every session. Use typed
  settings with proposed defaults: scan 30 seconds, retry delay 30 seconds, send
  timeout 30 seconds, attempt lease 90 seconds, maximum 3 automatic attempts.
  Enforce positive values, timeout shorter than lease, and a finite per-pass page
  size (100). Claim failure before possible I/O spends no send attempt. Repeated
  preparation failures are still bounded by the existing 30-minute
  `PendingReplyTtlMinutes`, measured from the first obligation, never reset by retry.
  Process death/lease expiry spends the persisted attempt. At cap or age limit,
  retain the payload in a durable Held state and raise a Critical incident. No
  automatic reset of the cap and no new manual-retry UI/API is required by this card.
- **D-5 — uncertainty is explicit.** A persisted attempt whose outcome was lost,
  including cancellation during send or acceptance followed by a failed outcome
  commit, is `PublicationUnknown`, not Published and not proof of non-delivery.
  Record a Critical `ChannelReplyLost` incident with truthful uncertainty wording
  and permit the remaining bounded attempts using the same publication identity.
  This is at-least-once publication: one duplicate is possible after an ambiguous
  accepted send. Do not assert exactly-once delivery in that window. Once Published
  is committed, ordinary triggers/restarts never resend it.
  An exception is not itself proof the broker refused the record: unclassified
  producer errors/timeouts are Unknown unless the transport supplies definite
  non-acceptance evidence. A test producer's known accepted count is an oracle for
  that test, not information production may infer from an exception type alone.
- **D-6 — durable observability.** Definite refusals persist a sanitized failure,
  attempt/time and retry disposition immediately. At exhaustion/age expiry,
  commit Held plus one Critical `ChannelReplyLost` incident and its alert atomically;
  include publication ID, source IDs, session, target, stage and attempt. Incident
  dedupe survives restart and incident-history pruning. Failed incident persistence
  leaves the obligation unresolved/retryable for *incident recording*, not another
  send beyond the cap. A best-effort originating-chat notice happens only after
  incident commit; notice refusal/disabled channel cannot undo the incident or
  recursively create another reply-loss episode. Unknown wording says publication
  may have happened; it never says the agent did not answer.
- **D-7 — settlement and metadata.** Confirmed publication and its correlation
  settlement commit together, fenced by the attempt owner. `LastReplyAt`, preview,
  and implied-bundle stamps follow confirmed publication; a later metadata failure
  is repairable without sending again. Bundle stamps remain limited to files
  actually included. Runtime late-confirm diagnostics must not mistake a pending
  publication/claim for a successfully published reply.
- **D-8 — policy and lifetime.** Preserve NO_REPLY, API-error withholding, machine
  origin settings, full prompt correlation and transport markers. Preserve old
  settled rows with no new journal: there is no evidence to distinguish their
  successful sends from historical claim crashes, so do not replay them on upgrade.
  Recover old *unsettled* rows. Retain unresolved publication payloads and the
  source/session/transcript needed for pending preparation, discovery or incident
  attribution; retention must not silently cascade them away. Published historical
  rows remain eligible for existing retention. Update the owner docs' old
  claim-before-produce guarantee when the implementation lands.

### Committed slice group

| Slice | Implementation and associated tests |
|---|---|
| S1 | Publication entity/membership and generated EF migration; unique interval/target identity; conditional claims and outcome/incident transactions; immutable payload; retention exclusions. Add the proposed publication fixture and O tests below. |
| S2 | Route the three dispatcher paths through S1; recover original turns and intervals on startup/timer; preserve partial-target outcomes, runtime late-confirm, catalog and bundle behavior. Add delivery, policy and recovery cases. |
| S3 | Add owned crash worker and transport fixture; complete R amendments; update `docs/telegram.md`, relevant `docs/session-runtime-invariants.md` paragraphs and entity comments. Run the closed checkpoint list after S1-S3 are committed. |

Keep production logic in concrete services. External file/producer I/O may use
interfaces; test boundary observation can follow `CheckCompactionBoundary`'s concrete
no-op/virtual pattern. Do not add an interface solely to mock the publication service.
Use the EF CLI under the build-slot wrapper for the migration, never hand-author it.

## Verification design

### Inspection

| Bodies read | Boundaries covered or excluded |
|---|---|
| `ChannelReplyDispatcher`: `OnTurnEndAsync`, main/late/machine dispatch, `SettleAsync`, preparation, API-error handling, TTL classification, `ReportLostAsync`, notice sends, correlation and text-window queries | All three producer paths, preparation outside catches, cancellation, per-target progress, lost watermarks, incident-save gap: V-1..V-14, V-17..V-19. |
| `AgentSessionRuntime.FlushQueueOnIdleAsync` and late-confirm warning; `AgentSupervisorHostedService` channel sweep; `ChatChannelService.SendAsync`/`StampLastReplyAsync`; `ChannelBridgeSettings` | Event/timer entry, late-confirm, disabled notice refusal, catalog metadata: V-3, V-7, V-10, V-15, V-21; R-1/R-3. |
| `ChannelReplyDurabilityTests` restart, refusal, late-confirm, TTL and unroutable bodies; `ChannelFollowUpAttachmentTests` main incident, refusal, restart, trailing, implied-bundle and helper bodies; `ChannelMachineTurnTextTests` policy, repeat, metadata and follow-up bodies; `ChannelBatchingTests` real queue/batch bodies | Exact existing expectations and fixture construction: R-1..R-4. Source roster counts are below; these are not measured executions. |
| `ChannelPromptCorrelationTests` harness, native replay, `C584_Red_JoinedGrokReply`, `C584_Red_CommonHeadDifferentTail`, `C584_Red_PreAttemptPrompt`; session runtime correlation owner | Reuse full receipt/attempt matching during discovery: V-12 and R-5. |
| `BridgeQueueHarness` DI, attach/preserve options, seeding, transcript insertion and disposal; `TestDbFixture`; `CheckCompactionCrashTests`, `CheckCompactionCrashWorker`, `CheckCompactionFixture`, `TestWorkerModes` | Real queue with fake adapter, isolated migrated database, process restart and actual death: V-15/V-17/V-18. |
| `DataRetentionService.PruneSessionsAsync`, `PruneTranscriptsAsync`, `PruneQueuedMessagesAsync` | Session cascade and independent source pruning: V-19. |
| `KafkaAntiphonMessagingProducer.SendAsync`, `ChannelReply`, `GatewayOutboundService` consumption/dispatch; `GatewayTests` adapter/producer helpers; `KafkaInboundCommitTests` owned Redpanda setup | Production broker transport and independent adapter receipt: V-20. Inbound commit policy and native provider retries are unchanged. |
| `server/Bundles/stage-test-design.md`, `docs/testing-and-build.md` checkpoint/mutation/delivery sections; recent CARD-0593/CARD-0758 TestDesign sections; checkpoint importer/validator | V/R matrix, one guard per PC, source-expanded counts, filter syntax and closed run list. |

### Delivery inventory

| Producer -> destination | Durable identity and persistence boundary | Recovery | Observable receipt and limit |
|---|---|---|---|
| Channel bridge/real `SessionMessageQueueService` -> agent | Queue ID, body/marker, session and original attempt floor, committed before submit | Existing queue; idle and busy recipients in V-15 | Complete matching `UserPrompt` beyond the attempt floor. Fake adapter receipt does not qualify a native TUI. No changes to paste/Enter. |
| Completed main turn -> broker -> adapter | Publication ID + owning prompt + text interval + target, all source queue members; committed before producer entry | Startup and timer scans, original turn, independent target state | Independent Kafka consumer/adapter captures exact text, kind, target and file bytes in V-20. Producer return is only broker acceptance. |
| Trailing text -> same destination | Durable parent turn/target and non-overlapping text interval; first reply's settlement is not the fragment's receipt | Recreated worker with no `_dispatched`, even after a newer prompt | Adapter capture of fragment exactly once in definite-refusal cases, initial answer never resent; unknown-acceptance counts explicitly allow duplication. |
| Machine task/check/scheduled/attachment reply -> captured conversation | Injection queue IDs, owning turn and same publication contract; implied task IDs/files recorded | Same startup/periodic recovery even with no open Channel row | Exact follow-up content and attached bytes; source settlement/bundle stamps alone are insufficient. |
| Exhausted/unknown publication -> incident/alert and optional chat notice | Publication ID + failure reason incident stamp, transaction with durable state | Failed incident insert retried; stamp survives history pruning | Query incident and alert through independent context. Failed chat notice leaves these visible. No session notification is introduced. |

Every test joins these legs using the fixture's IDs and original body/byte hashes.
Do not use database-global counts. A producer-call log, queue insert, claim, source
settlement, `LastReplyAt`, or successful `SendAsync` is not an adapter receipt.

### Fixture and fault seams Code must implement

**O** = new `tests/Antiphon.Tests/Application/ChannelOutboundPublicationTests.cs`.
**C** = new `tests/Antiphon.Tests/Application/ChannelOutboundCrashTests.cs`.
**T** = new `tests/Antiphon.Tests/Application/ChannelOutboundTransportTests.cs`.
All methods below have the `C519_` prefix and return `Task`; arguments expand into
separate TUnit results. Do not count internal assertions or child processes as tests.

Create `TestHelpers/ChannelOutboundFixture.cs` around `BridgeQueueHarness`, one
`TestDbFixture.CreateIsolatedSchemaAsync()` store per argument. Supply its connection
string to **every** context, including restarted providers and fault interceptors.
`BridgeQueueHarness.CreateContext()` uses the shared store and is unsuitable here.
Use `PreserveDatabaseOnDispose=true` and `AttachSessionId`/`AttachAgentId` on restart;
the parent owns final cleanup. Never boot real `Program` or contact a live runner.

The fixture creates the same production service/worker graph used in Program.
Wire production registration through a reusable registration method so V-3 tests
the shipped hosted worker, not a hand-written test loop. Use a controllable clock
for outbound scheduling; do not freeze the queue's confirmation/submit clock.
In V-15 run the queue on system/scaled time and advance only outbound time.
Record a `TaskCompletionSource` boundary for each completed scan; release and await
it rather than sleeping for an assumed duration. Settings are 3 attempts, 30-second
retry, 30-second timeout, 90-second lease, 30-second scan and page size 2 in tests.

Use three paths: `main`, `trailing`, `machine`. Main seeds one delivered Channel
row and its complete turn. Trailing first publishes `Initial answer.` successfully,
then appends `Late answer.` after that TurnEnd. Machine first settles that Channel
turn, seeds a Sent Delegation row as in the attachment tests, then completes its
distinct turn with text plus a small PDF. Capture baseline initial sends separately.
Additional machine-origin policies are exercised in V-14/R-3, not counted as a
fourth path. Every answer has unique middle and tail sentinels.

`ScriptedOutboundProducer` implements the actual I/O seam and records **entered**,
**accepted** and **returned** separately. Modes are synchronous throw before returning
a Task, asynchronous fault, serialization-shaped `JsonException`, cancellation
before acceptance, acceptance followed by lost result, and a held successful call.
It must not append accepted messages on a rejected call. Its serialization-shaped
fault proves caller failure handling only; V-20 supplies an actual Kafka refusal.
Use `ConfigureDbContext` interceptors to fail the specific commit below production
code, scoped by publication ID; never fail every SaveChanges or compare a mock
result to itself. Fresh contexts verify rollback and externally visible state.

Required concrete boundary names: `before-publication-commit`,
`publication-committed`, `attempt-committed`, `producer-entered`,
`producer-accepted`, `outcome-committed`, `before-incident-commit`.
An attachment-read I/O seam throws after file existence/size checks; filesystem
permissions or timing a delete are not portable fault injection. The boundary
observer may pause/throw; it may not decide or manufacture a production outcome.

For C, add `ChannelOutboundCrashWorker` to `TestWorkerModes.All`, using the existing
same-assembly `dotnet <test dll>` worker mode. A parent-owned isolated database,
temporary root with owner marker, PID and build MVID are required. Parent reads the
committed database and receiver evidence before killing its exact child, awaits
exit and both pipes, then launches a **new process** on the same store. No graceful
cancel/unclaim runs in the killed worker. Use `[ParallelLimiter<ProcessSpawnLimit>]`
and `try/finally` cleanup; do not create another project or container in each child.

T adds the existing messaging-test Redpanda package version to `Antiphon.Tests`
and a test-only project reference to `Antiphon.Messaging.Gateway`. Run the production
Kafka producer and hosted gateway against a test-owned Redpanda container, random
topic/group and loopback ports. A `RecordingChannelAdapter` is the recipient and
returns `SendResult.Sent` after capturing complete envelopes. Start its consumer
either immediately or after the recovered publication. This is a real broker
queue; the adapter is an explicit substitute for the provider API. Dispose all
hosts, consumers and the owned container. No shared/live messaging broker.

### Assertion vocabulary

- **Owed:** independently reloaded source/obligation remains discoverable; no
  Published timestamp for the failed target/interval; original payload/identity
  survives; no premature catalog or bundle stamp. Main/machine source settlement
  is null; trailing source stays at its original settlement. Failure stage/reason,
  attempt count and retry/hold disposition are queryable, not just logged.
- **Published:** exact accepted envelope, durable accepted/outcome state and source
  membership; subsequent triggers plus a fresh worker add zero sends. For definite
  refusal then recovery, accepted count delta is exactly 1; submitted call count may
  exceed 1. Metadata repair never produces another accepted envelope.
- **Loud:** exactly one Critical `ChannelReplyLost` incident and corresponding alert
  scoped to publication/agent, naming the original session, sources, target, stage,
  attempt and disposition. For Unknown assert “may”/uncertainty semantics rather than
  false “never sent”/“no turn completed” claims. Clear or resolve the outstanding
  failure state after confirmed recovery without deleting its incident history.
- **Receipt:** for inbound, complete matching UserPrompt, correct session/marker and
  attempt floor; for outbound, independent adapter capture with exact expected
  text, kind, target, attachment names/MIME/bytes. A count alone is insufficient.

### Proves it works now

All O rows use the real EF/service/dispatcher graph. C is actual process-death
acceptance. T is owned-broker integration. “New” means expected to expose an
uncovered production defect or missing durable behavior on the inspected base;
“guard” means a required safety comparison, not evidence of a current failing test.

| ID | Exact class.method and arguments (executed results) | Ratified body / decisive assertions | Kind |
|---|---|---|---|
| V-1 | O.`C519_Refusal_preserves_the_original_obligation(string path, string stage)`; each of 3 paths x `prepare`, `publication-commit`, `attempt-commit`, `serialize`, `produce-sync`, `produce-async`, `failure-save` (**21**) | Arm the named fault. Await real dispatch; verify cut fired, no accepted answer, Owed, no success metadata. In failure-save the producer first refuses, then the error/unclaim save also fails: the previously committed attempt/source is the recovery evidence. Disarm, advance due time (lease expiry for failure-save) and run production recovery without another turn; Published with exact original bytes. Before materialization assert retained source/transcript, then exactly one journal identity on recovery. | New; sync/async main catch alone is guard. |
| V-2 | O.`C519_Cancellation_never_becomes_publication(string path, string cut)`; 3 paths x `before-attempt`, `during-send`, `accepted-before-result` (**9**) | Use an actual canceled token/OperationCanceledException. Cancellation propagates or stops the worker normally; fresh process state is Owed. Before-attempt spends 0 sends; during/after send keeps the spent count and eventually Loud Unknown. Restart with a new token, no extra TurnEnd, and recover. Accepted-before-result may have 2 identical accepted copies after retry, never claim exactly one. | New. |
| V-3 | O.`C519_Recovery_needs_no_new_turn(string path, string trigger)`; 3 paths x `startup`, `periodic` (**6**) | Refuse once; persist a different newer complete turn; recreate all singletons. Startup row disables timer advance; periodic row waits for empty startup before inserting the owed work and disables the fast signal. Only the named trigger runs. Original reply is Published, newer answer never substituted. Use 3 owed obligations total with page size 2: first pass processes at most 2; another startup in a fresh process or the next timer pass (same tested trigger) drains the third. | New. |
| V-4 | O.`C519_Retry_budget_and_age_survive_restart()` (**1**) | Refuse with cap 3. Tick just before/exactly at due time; no early call, exactly one at due time. Restart after each refusal; attempts 1/2/3 never reset. At cap, Loud Held and 0 further sends across two scans. Separate fresh case ages a preparation-failed obligation to exactly TTL; retry does not extend its original age. | New. |
| V-5 | O.`C519_Fanout_keeps_independent_target_outcomes(string caseName)`; `second-refuses`, `unroutable-sibling` (**2**) | Seed a legitimate complete multi-member transcript/queue batch with targets A/B (explicit latent fanout fixture; ordinary queue never batches across chats). A succeeds; B fails or has malformed key. A's correlation outcome is Published, B's failed; retry only B. Same-target members share one envelope and all membership rows. No A duplicate when B errors or its incident commit is retried. | New. |
| V-6 | O.`C519_Concurrent_dispatchers_share_one_fenced_attempt()` (**1**) | Two independent providers stop after selecting the same source. Release together; exactly one committed publication and one producer entry while its call is held. The losing worker may not alter its owner. Complete winner, rerun loser, still one accepted send. Separately expire owner A, claim B, then release A's late completion: A cannot overwrite B's attempt/state. | New; database-level concurrency. |
| V-7 | O.`C519_Post_acceptance_failure_does_not_lie_or_resend(string cut)`; `outcome-commit`, `catalog-stamp`, `bundle-stamp` (**3**) | Producer really accepts. Outcome-commit failure leaves Unknown, Loud and original identity, not Published; retry may duplicate. Catalog/bundle cases first commit Published then fail only metadata; restart repairs metadata with accepted delta still 1. Bundle case includes included and over-cap files; only included files permit the stamp. | New. |
| V-8 | O.`C519_Trailing_interval_survives_restart_and_a_newer_prompt()` (**1**) | Publish initial reply; append a late fragment and the next UserPrompt in the same transcript batch. Refuse fragment; restart; recover exactly that fragment, not initial text or next turn text. Race a later fragment against first-fragment capture: non-overlapping durable intervals, original pending interval immutable, each confirmed fragment once. | New. |
| V-9 | O.`C519_Retry_uses_frozen_payload_and_destination()` (**1**) | Refuse a prepared machine reply containing UTF-8 text and PDF. Change catalog handle/binding, append newer turn, change truncation settings and overwrite/delete source files. Recovery retains original provider/chat/handle/kind/text/names/MIME/bytes and original implied task identity. New content may form another obligation but cannot replace this one. | New. |
| V-10 | O.`C519_Failure_incident_survives_its_own_refusal(string fault)`; `incident-save`, `notice-produce`, `notice-disabled`, `missing-owner` (**4**) | Exhaust publishing. Incident-save aborts its transaction: no terminal success/settlement or dedupe stamp without incident. Recover and assert Loud once. Notice failures/disabled channel preserve committed incident/alert and no recursion. Missing-owner case removes current session pointer/catalog binding after obligation creation: persisted owner still receives Loud. Prune only incident history, scan twice: persistent episode stamp prevents a new page. | New. |
| V-11 | O.`C519_Upgrade_preserves_old_settled_rows_and_recovers_open_rows()` (**1**) | Seed pre-journal settled and open Channel/machine rows. Migration/backfill and startup leave settled history alone, do not pretend it has broker receipts. Eligible open rows recover via their own completed turn. Exact NO_REPLY legacy policy is preserved. Verify through a migrated database, never EnsureCreated. | Guard + new open-row recovery. |
| V-12 | O.`C519_Recovery_cannot_borrow_another_turn(string mismatch)`; `session`, `marker`, `tail`, `attempt-floor`, `next-turn` (**5**) | Only mismatch changes from an otherwise eligible complete turn. Scan must create/send no publication for the wrong turn. Wrong-tail shares >200 initial chars; next-turn has valid answer text only after a different prompt. Keep source owed for normal classification. Then supply correct evidence and verify original source recovery. | Guard on the new scan. |
| V-13 | O.`C519_Expired_attempt_is_unknown_and_spends_its_budget()` (**1**) | Hold producer return after a durable attempt. Before expiry another worker sends nothing. On expiry it records Loud Unknown and retries the same identity within cap. Hang final attempt through send timeout: worker returns within clock-driven budget and eventually Held with no further send. Assert no live producer Task remains after fixture disposal. | New. |
| V-14 | O.`C519_Recovery_preserves_intentional_withholding(string policy)`; `main-no-reply`, `machine-no-reply`, `system-text`, `main-api-error`, `trailing-api-error` (**5**) | Run both startup and periodic scans on each completed turn. No new accepted reply or spurious outbound-failure incident. Main NO_REPLY deliberately settles; machine NO_REPLY/System text do not claim; transient API failure keeps source owed for its existing provider/TTL policy. Positive sibling with prose around NO_REPLY or explicit attachment does publish. | Guard. |
| V-15 | O.`C519_Real_queue_receipt_then_reply_survives_refusal(string recipient)`; `idle`, `busy` (**2**) | Real EnqueueAsync WhenIdle -> queue -> fake adapter -> complete UserPrompt. Busy initially has one Pending row and zero submitted bodies; append TurnEnd and flush. Append answer via runtime transcript synchronization, refuse publication, restart recovery only. Exactly one input submission/receipt and one recovered answer; no retyping the inbound message. | New integrated acceptance. |
| V-16 | O.`C519_Database_rejects_duplicate_publication_ownership()` (**1**) | Direct duplicate insert bypassing services violates the target/turn/interval unique constraint; direct second membership for the same source/interval fails. Distinct target and later non-overlapping interval are valid. Assert named PostgreSQL constraint errors, not an arbitrary exception. | New schema guard. |
| V-17 | C.`C519_Killed_publish_recovers_from_committed_evidence(string path, string cut)`; 3 paths x `attempt-committed`, `producer-entered`, `producer-accepted`, `outcome-committed` (**12**) | Parent waits for exact cut and independently verifies store/receiver, kills child and awaits pipes. Fresh child starts recovery only. First 2 cuts have 0 prior accepted; recover 1 and record Unknown for interrupted attempt. Accepted cut has 1 prior copy and may finish with 2 identical copies plus Loud Unknown. Outcome-committed stays at 1 with no unknown incident or resend. Source/payload and target fixed for all. | Actual crash acceptance. |
| V-18 | C.`C519_Killed_materialization_recovers_without_an_event(string path, string cut)`; 3 paths x `before-publication-commit`, `publication-committed` (**6**) | Kill before commit (no visible journal) or after commit before wake signal (one visible journal). Original persisted transcript/source survives. Add newer transcript before restarting; boot-only recovery discovers original work, produces one receipt, and no extra source queue delivery. For trailing, baseline initial publication is durable. | Actual discovery/enqueue crash acceptance. |
| V-19 | O.`C519_Retention_cannot_erase_an_unresolved_reply(string pass)`; `session`, `transcript`, `queue` (**3**) | Use DataRetentionService's real corresponding prune method. Make terminal source older than retention and remove unrelated protections (persistent pointer, tasks, inbound journal). Pending preparation, Publishing and Held subcases survive with source evidence/payload. After committed publication and normal retention age, unrelated eligible rows still prune. Check target IDs, not returned global totals. | New lifetime guard. |
| V-20 | T.`C519_Reply_crosses_the_real_broker_and_reaches_the_adapter(string condition)`; `ready`, `consumer-late`, `broker-refuses` (**3**) | Full production dispatcher -> Kafka producer -> gateway -> recording adapter. Ready/late include a prior scripted refusal and worker recreation; late starts gateway only after publication. Broker-refuses sets test topic max.message.bytes below actual serialized attachment envelope and verifies real produce failure and no consumed reply; raise that same topic limit, retry frozen envelope. Observe complete Receipt, original source ID locally, exact 1 accepted adapter envelope for definite refusal, durable Published. Commit/signal alone is never receipt. | Transport acceptance; adapter substitutes native API. |
| V-21 | O.`C519_Late_confirm_cannot_hide_a_failed_publication()` (**1**) | Reuse LateConfirmBatch shape through Runtime.SyncTranscriptAsync, with cancellation/preparation/publish refusal subcases. Actual inbound LateConfirmed and complete receipt; publication not Published; outcome/warning names exact source and failure (no bypass merely because claim is persisted). Recover without another runtime transcript batch and assert one outbound reply. | New; extends existing CARD-0313 refusal guard. |
| V-22 | O.`C519_Publication_settings_reject_unbounded_or_inconsistent_values()` (**1**) | Each setting independently invalid: 0/negative scan, retry, timeout, lease, cap, page; timeout >= lease; invalid reply TTL. Validator fails naming property. Production defaults and one valid compressed test configuration succeed. These internal cases count as one result. | New configuration guard. |

O has **19 methods / 69 expanded results**. C has **2 methods / 18 results**.
T has **1 method / 3 results**. Total new ordinary roster: **22 methods / 90 results**.
V-1's cross-product and all enumerated arguments are mandatory; use explicit
Arguments/MethodDataSource results, not one loop claimed as 21 executions.

### Ratified body shapes

These illustrate **new fixture APIs to implement**, not code that currently compiles.
The matrix above specifies the complete bodies, including exceptional assertions.
`ReadAsync` always opens a fresh context. `RecoverDueAsync` starts/drives the actual
production worker and awaits its observed scan completion; it never calls a test
copy of publication logic. The fixture retains source evidence even when initial
materialization fails and obtains the eventual ID through durable membership.

```csharp
[Test]
[MethodDataSource(nameof(RefusalCases))] // exact 3 x 7 matrix in V-1
public async Task C519_Refusal_preserves_the_original_obligation(
    string path, string stage)
{
    await using var f = await ChannelOutboundFixture.CreateAsync(path);
    var source = await f.CompleteSourceTurnAsync();
    f.Faults.Arm(stage);
    await f.DispatchAsync(source);
    f.Faults.WasReached(stage).ShouldBeTrue();
    f.AcceptedFor(source).ShouldBeEmpty();
    ((await f.ReadAsync(source)).Publication?.PublishedAt).ShouldBeNull();
    await f.AssertOwedAsync(source); // includes source-kind-specific settlement
    if (stage is "publication-commit" or "attempt-commit")
        f.ProducerEntriesFor(source).Count.ShouldBe(0);
    f.Faults.Disarm();
    await f.RestartServicesAsync();
    await f.RecoverAtNextEligibleTimeAsync(source); // due time or expired lease
    var sent = f.AcceptedFor(source).ShouldHaveSingleItem();
    sent.ShouldBeEquivalentTo(source.ExpectedEnvelope);
    await f.AssertPublishedAsync(source);
    await f.RestartServicesAsync();
    await f.RecoverDueAsync();
    f.AcceptedFor(source).Count.ShouldBe(1);
}
```

V-1 owns this body's 21 argument results; the data source is the exact matrix above.
`failure-save` arms both a rejected producer call and the subsequent failed error
save; other arguments arm one fault. A null Publication is allowed only before
materialization; `AssertOwedAsync` then requires the committed source and transcript.
For `publication-commit` and `attempt-commit`, additionally assert zero producer
entries while the interceptor rejects the commit. For prepare, assert original
response/path evidence instead of a prepared envelope that does not yet exist.

V-17's body must perform, in order: `StartOwnedWorker(cut)` -> await held cut ->
independent store/receiver snapshot -> `Kill(entireProcessTree: true)` on that exact
owned process -> await exit/stdout/stderr -> launch fresh boot-recovery worker ->
await receipt -> assert the cut's counts/state/incident -> finally drain all owned
workers. A thrown exception, canceled Task, or `new ChannelReplyDispatcher(...)`
alone is **not** this test. V-18 uses the same process body at its two earlier cuts.

V-15 must query `TranscriptEntries` for the source's complete UserPrompt and original
attempt floor before claiming the inbound leg passed. Calling `InsertTurnAsync`
with a fabricated duplicate UserPrompt after queue submission would hide missing
queue delivery; append only the assistant answer and TurnEnd to that actual receipt.

### Guards the regression

| ID | Existing class / exact bodies to retain | Required expectations and amendments |
|---|---|---|
| R-1 | `ChannelReplyDurabilityTests` — **24 results**. In particular `A_correlation_survives_a_process_restart_and_the_reply_is_published`, `A_restarted_process_does_not_answer_an_already_answered_turn_twice`, `A_failed_late_confirm_recovery_warns_with_the_correlation_and_leaves_it_owed`, TTL/unroutable, API-withheld and provider-error methods. | Keep exact content, target, single-send, Critical/alert and withheld semantics. Pass the new service through Restarted helper if construction changes. Refusal row may have a new durable publication in addition to null ChannelReplySettledAt; never invert the latter to pass. Existing TTL classification tests that invoke only the abandonment sweep retain their classifications; recovery scanning is a distinct operation. |
| R-2 | `ChannelFollowUpAttachmentTests` — **20 methods / 22 results**. `A_produce_failure_un_claims_so_the_next_trigger_sends_once`, `Re_running_the_same_turn_end_and_a_restart_do_not_double_send`, implied-bundle idempotency, late attachment, file budgets/order/MIME and NO_REPLY bodies. | Keep source null after definite failure and settled after success, exact file bytes and no duplicate. If retry due time now gates a repeated trigger, advance the injected outbound clock before the second trigger; do not add a sleep or weaken send count. Add post-failure assertion that bundle stamp remains null; V-7 separately covers accepted publish/failed stamp. |
| R-3 | `ChannelMachineTurnTextTests` — **18 methods / 19 results**. `A_produce_failure_un_claims_so_the_next_trigger_sends_once`, `Trailing_text_follows_via_the_dispatched_watermark`, `Follow_up_send_stamps_LastReplyAt_without_touching_inbound_columns`, exact NO_REPLY, System exclusion, settings and Check/Scheduled arguments. | Keep user-visible behavior. Watermark test's historical name may stay; durable state replaces its implementation. LastReplyAt/preview follow acceptance; LastMessageAt/author/native inbound ID remain unchanged. Same clock amendment as R-2. |
| R-4 | `ChannelBatchingTests` — **10 results**, especially `Batched_reply_fans_out_once_to_the_conversation` and NO_REPLY cases. | Real queue still batches only one conversation, all members settle with one reply; UI/mixed origins and queue failure contracts unchanged. |
| R-5 | `ChannelPromptCorrelationTests` — **24 results**, including the three C584 bodies in Inspection. | Full marker/body/session/attempt evidence, flattened marked receipt and TTL ownership stay intact. Recovery must call the same policy; no broad text-head fallback. |

Total retained ordinary roster: **96 methods / 99 expanded results**
(24 + 20 + 18 + 10 + 24 methods). Use **99** as the checkpoint floor.
Counts came from source attributes, not discovery or execution; Code reports fresh
TRX counts. Any added dynamic source must update its floor explicitly.

### Guard inventory and positive controls

Each row is one independently bypassable guard and one distinct pending PC. **O**,
**C**, **T** expand to the exact classes above. Mutation changes production code
only. If Code implements a guard separately in the three paths, its PC must disable
that invariant in all three or add separately budgeted controls; do not credit a
red in one path as proof the other implementations are protected.

| Guard -> PC | Production defect to introduce after land | Exact method / intended red assertion |
|---|---|---|
| G-1 -> PC-1: committed obligation before I/O (D-1) | Move producer entry before the publication transaction commits. | O.`C519_Refusal_preserves_the_original_obligation`: publication-commit refusal producer entries must be 0; independent reader must see committed obligation at entry. |
| G-2 -> PC-2: preparation failure retains source (D-2) | Mark source settled/fragment consumed in preparation catch without a recoverable owner. | Same V-1 method: prepare case Owed and eventual original envelope. |
| G-3 -> PC-3: producer faults retain retry (D-4) | In send exception branch mark Published instead of retry due. | Same V-1 method: sync/async/serialization PublishedAt must remain null. |
| G-4 -> PC-4: cancellation is not success (D-5) | Set Published and clear attempt in cancellation branch. | O.`C519_Cancellation_never_becomes_publication`: Owed/Unknown, accepted-before-result uncertainty. |
| G-5 -> PC-5: await actual producer completion (D-7) | Fire SendAsync without awaiting it, commit Published while held. | O.`C519_Expired_attempt_is_unknown_and_spends_its_budget`: before completion PublishedAt must be null. |
| G-6 -> PC-6: outcome-commit refusal remains uncertain (D-5) | On outcome SaveChanges failure clear pending ownership as successful. | O.`C519_Post_acceptance_failure_does_not_lie_or_resend`: outcome-commit Owed/Loud Unknown after restart. |
| G-7 -> PC-7: metadata cannot reopen publication (D-7) | Route catalog/bundle metadata failure through publish-retry logic. | Same V-7 method: metadata cases accepted count must remain 1. |
| G-8 -> PC-8: frozen content (D-2) | Rebuild pending payload from current transcript/files/settings. | O.`C519_Retry_uses_frozen_payload_and_destination`: exact original text/attachment bytes. |
| G-9 -> PC-9: captured destination (D-2) | Resolve target from current catalog on retry. | Same V-9 method: original provider/chat/handle/binding. |
| G-10 -> PC-10: independent target progress (D-3) | Requeue all targets after one target refuses. | O.`C519_Fanout_keeps_independent_target_outcomes`: A accepted once and A outcome Published. |
| G-11 -> PC-11: complete source membership (D-3) | Record/settle only the batch head. | Same V-5 method: all same-target member IDs owned/settled, one envelope. |
| G-12 -> PC-12: DB uniqueness (D-3) | Remove interval identity unique index from EF model and generated migration. | O.`C519_Database_rejects_duplicate_publication_ownership`: duplicate insert must fail at named constraint. |
| G-13 -> PC-13: DB membership uniqueness (D-3) | Remove source-membership unique index from model and generated migration. | Same V-16 method: second owner/member insert must fail. |
| G-14 -> PC-14: one live claim (D-3) | Omit active-lease predicate in claim. | O.`C519_Concurrent_dispatchers_share_one_fenced_attempt`: held producer entry count must be 1. |
| G-15 -> PC-15: stale completion fence (D-7) | Omit attempt owner/version predicate from outcome update. | Same V-6 method: late owner A cannot overwrite B. |
| G-16 -> PC-16: due-time admission (D-4) | Ignore NextAttemptAt during scanning/fast trigger. | O.`C519_Retry_budget_and_age_survive_restart`: no pre-due send. |
| G-17 -> PC-17: persistent retry cap (D-4) | Reset attempt count on process reconstruction or omit cap predicate. | Same V-4 method: count stays 3, no fourth call, Held. |
| G-18 -> PC-18: original age bound (D-4) | Refresh first-owed time on each preparation retry. | Same V-4 method: preparation failure becomes Loud Held at original TTL. |
| G-19 -> PC-19: send timeout (D-4) | Remove timeout linkage while retaining external worker cancellation. | O.`C519_Expired_attempt_is_unknown_and_spends_its_budget`: advancing timeout completes attempt while host token remains live. Assert completed flag, not merely test timeout. |
| G-20 -> PC-20: expired attempt is Unknown (D-5) | Treat stale Publishing as already Published. | C.`C519_Killed_publish_recovers_from_committed_evidence`: pre-acceptance cuts lack receipt; accepted cut lacks Loud Unknown. |
| G-21 -> PC-21: startup recovery (D-4) | Skip the startup scan. | O.`C519_Recovery_needs_no_new_turn`: startup arguments lack original receipt with timer held. |
| G-22 -> PC-22: periodic recovery (D-4) | Skip periodic pending/discovery scans. | Same V-3 method: periodic arguments have 0 recovered replies after observed timer pass. |
| G-23 -> PC-23: recover pre-materialization gaps (D-4) | Scan only existing publication rows, not eligible source turns. | C.`C519_Killed_materialization_recovers_without_an_event`: before-commit arguments have 0 original receipts. |
| G-24 -> PC-24: pending durable row needs no wake signal (D-4) | Recovery drains only the in-memory wake queue. | Same V-18 method: committed-before-signal arguments have 0 receipts. |
| G-25 -> PC-25: bounded scan (D-4) | Remove claim/discovery page limit. | O.`C519_Recovery_needs_no_new_turn`: first observed pass processes >2 of three eligible obligations. |
| G-26 -> PC-26: durable trailing intervals (D-3) | Use only `_dispatched` to enumerate late work after restart. | O.`C519_Trailing_interval_survives_restart_and_a_newer_prompt`: missing Late answer receipt. |
| G-27 -> PC-27: next prompt caps, not discards (D-3) | Discard pending fragment when a newer prompt exists. | Same V-8 method: same-batch late fragment must publish. |
| G-28 -> PC-28: machine recovery eligibility (D-4) | Filter recovery sources to Origin.Channel only. | O.`C519_Recovery_needs_no_new_turn`: machine arguments lack recovered reply. |
| G-29 -> PC-29: atomic incident/dedupe (D-6) | Commit Held/incident stamp before incident insertion transaction. | O.`C519_Failure_incident_survives_its_own_refusal`: incident-save restart must end with exactly one incident. |
| G-30 -> PC-30: Critical/alert routing (D-6) | Record publication failures below Critical so channel-bound escalation is lost. | Same V-10 method: Critical incident and corresponding alert required. |
| G-31 -> PC-31: durable incident dedupe (D-6) | Deduplicate only against current AgentIncidents, dropping publication stamp. | Same V-10 method: prune/restart must not raise another page. |
| G-32 -> PC-32: notice cannot gate incident (D-6) | Send chat notice before committing incident, returning on notice failure. | Same V-10 method: notice-produce/disabled must retain incident/alert. |
| G-33 -> PC-33: truthful uncertainty (D-5) | Format Unknown with definite “never sent/no turn completed” wording. | O.`C519_Cancellation_never_becomes_publication`: accepted-before-result incident must state possible publication. |
| G-34 -> PC-34: NO_REPLY policy (D-8) | Recovery treats exact NO_REPLY as an ordinary answer. | O.`C519_Recovery_preserves_intentional_withholding`: main/machine NO_REPLY accepted delta 0. |
| G-35 -> PC-35: API-error policy (D-8) | Recovery publishes a turn whose answer window contains an API-error stub. | Same V-14 method: main/trailing API accepted delta 0. |
| G-36 -> PC-36: machine-origin policy (D-8) | Admit System plain text despite default origin settings. | Same V-14 method: system-text accepted delta 0. |
| G-37 -> PC-37: full correlation during discovery (D-8) | In recovery accept any complete turn on the source session without invoking its persisted prompt matcher. | O.`C519_Recovery_cannot_borrow_another_turn`: marker/tail/attempt-floor cases must not publish. Session and next-turn branches remain independent R-5 coverage; add PCs below. |
| G-38 -> PC-38: session identity (D-8) | Remove source session admission from discovery's candidate query and the matcher's session comparison; retaining either would mask this same invariant. | Same V-12 method: wrong-session-only answer must not publish. Keep otherwise valid marker/body/floors. |
| G-39 -> PC-39: turn answer boundary (D-8) | Remove next-prompt cap when recovery reads source answer. | Same V-12 method: next-turn answer cannot satisfy source. |
| G-40 -> PC-40: committed success is final (D-5) | Include Published rows in recovery send candidates. | C.`C519_Killed_publish_recovers_from_committed_evidence`: outcome-committed accepted count must stay 1. |
| G-41 -> PC-41: conservative legacy settlement (D-8) | Backfill old settled sources into Pending publication rows. | O.`C519_Upgrade_preserves_old_settled_rows_and_recovers_open_rows`: old settled rows produce 0 new replies. |
| G-42 -> PC-42: source session retention (D-8) | Remove unresolved-outbound exclusion from session pruning. | O.`C519_Retention_cannot_erase_an_unresolved_reply`: session argument source/session/payload survives. |
| G-43 -> PC-43: source transcript retention (D-8) | Remove unresolved-outbound exclusion from transcript pruning, including locked recheck. | Same V-19 method: transcript argument retains original owning prompt/answer. |
| G-44 -> PC-44: source queue retention (D-8) | Remove unresolved-outbound exclusion from queue pruning. | Same V-19 method: queue argument retains exact source membership. |
| G-45 -> PC-45: late-confirm outcome truth (D-7) | Treat a durable claim as Published in runtime outcome/warning handling. | O.`C519_Late_confirm_cannot_hide_a_failed_publication`: failed source outcome/diagnostic remains observable. |
| G-46 -> PC-46: validated bounds (D-4) | Return validation success unconditionally for new outbound settings. | O.`C519_Publication_settings_reject_unbounded_or_inconsistent_values`: each invalid property must be refused. |
| G-47 -> PC-47: non-overlapping interval reservation (D-3) | Reserve trailing ranges from the published watermark while ignoring a previously reserved pending range; keep exact-range uniqueness, and append more text between captures so the overlapping ranges differ. | O.`C519_Trailing_interval_survives_restart_and_a_newer_prompt`: intervals must not overlap and no fragment may appear twice. |
| G-48 -> PC-48: committed attempt before I/O (D-1) | Leave publication commit intact but enter producer before the attempt transaction commits. | O.`C519_Refusal_preserves_the_original_obligation`: attempt-commit refusal producer entries must be 0. |
| G-49 -> PC-49: captured incident owner (D-6) | Resolve incident owner only from the current session pointer/catalog, ignoring the persisted obligation owner. | O.`C519_Failure_incident_survives_its_own_refusal`: missing-owner argument still has its original owner's incident and alert. |
| G-50 -> PC-50: discovery cursor advances past withheld rows (D-4) | Restart source discovery at the first page on every pass instead of resuming after the last examined row. | O.`C519_Discovery_pages_past_withheld_rows_without_replaying_pre_chat_attachments`: the owed channel reply behind the withheld backlog must publish on the next pass. |
| G-51 -> PC-51: discovery has a per-pass budget (D-4) | Continue source discovery beyond its ten-page budget in one pass. | O.`C519_Discovery_pages_past_withheld_rows_without_replaying_pre_chat_attachments`: the later owed reply must remain unpublished after the first budgeted pass. |
| G-52 -> PC-52: trailing discovery ranks recent transcripts first (D-4) | Rank open trailing windows by publication time and omit transcript recency. | O.`C519_Trailing_discovery_prioritizes_recent_transcript_over_idle_publications`: with more idle windows than one page and older transcript rows in each, the trailing fragment must publish on startup. |

Inventory: **52 guards, 52 distinct PCs, missing=0, duplicate PC maps=0**.
PC-37 guards reuse of the existing complete matcher, not a claim of requalifying
each untouched matcher conjunct; R-5 retains that owner's cases. Bound settings
validation is one rejection entry point with independently invalid inputs. If Code
adds further independent policies or alternate success/cleanup entry points, extend
the inventory and cost before Review, not with an untested generic “retry” claim.

Every PC uses `/*/*/<exact class>/<exact method>*` (expand O/C/T). The trailing
wildcard selects all argument results; never add literal parameter suffixes. Run
baseline green -> compiling defect/intended assertion red -> exact restoration,
fresh isolated build and green. Use `scripts/run-checkpoint.ps1`, method-specific
`-Expect` and the method's full expanded result floor above. Do not mutate fixtures
or assertions. Schema PCs regenerate migrations with the CLI and recreate the
isolated migrated store for each phase. Keep output/results per phase in the
SourceLanding external evidence root and follow restoration custody rules.

Code runs ordinary V/R. Separate Review audits ordinary evidence and these pending
controls; after confirmed land the SourceLanding Mutation companion executes PCs.
Most controls share the publication service/dispatcher: run them serially, not as
simultaneous source edits. A compile failure, fixture failure, timeout or zero-test
selection is not a killed control.

### Out of scope and evidence limits

- Native Telegram/Slack retries, gateway offset-loss recovery and human read
  acknowledgement need their own end-to-end contract. V-20 proves broker-to-adapter
  transport only. Do not label Kafka acceptance “delivered to chat”.
- No paid provider, live broker, production runner, AppHost restart or UI work.
  Synthetic complete UserPrompt proves the server queue contract, not native TUI
  behavior. No broad Unit/namespace suite is needed for these scoped services.
- Historical settled rows cannot be repaired automatically without independent
  publication evidence. V-11 explicitly prevents speculative historical replays.
- There is no exactly-once guarantee across broker acceptance and a lost database
  outcome. D-5 makes bounded duplicate risk observable. Ordinary confirmed sends
  and definite refusals still have exact no-duplicate assertions.
- An unavailable database cannot store an incident during the outage. Preserve
  original committed obligations, emit an error and recover when it returns; tests
  must demonstrate recovery after restore, not demand impossible outage writes.

### Checkpoints

All rows are serial to keep the database/process/broker resource budget explicit.
Same committed S1-S3 group, one isolated test build reused by the remaining rows.
`Environment` caps in-process test concurrency; it is not a live server setting.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c519/` | publication | `/*/*/ChannelOutboundPublicationTests/*` | V-1..V-16, V-19, V-21, V-22 | all 71 argument results, 0 failed/skipped | 71 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S3 | CP-1 | crash | `/*/*/ChannelOutboundCrashTests/*` | V-17, V-18 | all 18 argument results, 0 failed/skipped | 18 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | CP-1 | broker-receipt | `/*/*/ChannelOutboundTransportTests/*` | V-20 | all 3 argument results, 0 failed/skipped | 3 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | CP-1 | existing-replies | `/*/*/(ChannelReplyDurabilityTests*)\|(ChannelFollowUpAttachmentTests*)\|(ChannelMachineTurnTextTests*)\|(ChannelBatchingTests*)\|(ChannelPromptCorrelationTests*)/*` | R-1..R-5 | all 5 classes and >= 99 executed, 0 failed/skipped | 99 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

All times are estimates, not measured. Ordinary execution floor **47 minutes**:
CP-1 15 (including an estimated 6-minute build), CP-2 12, CP-3 8, CP-4 12.
Min execution floor **191** = 71 + 18 + 3 + 99; counts are not minutes.
Three reused rows save an estimated **18 build minutes** compared with four builds.
No omitted test class or native qualification is charged as a saving.

Code authoring/schema/fixture allowance **300 minutes**, plus ordinary floor 47:
**347 minutes** before separate Review. Estimated Review **25 minutes**.
Mutation floor: 48 non-crash PCs x 6 minutes + 4 crash PCs (20/23/24/40) x 12
minutes = **336 minutes**, including each method's baseline/red/restored-green
build and run. **156 phase invocations** total. Add **25 minutes** discovery,
schema regeneration and evidence/restoration reporting: commission Mutation for
at least **361 minutes**. Implementation through Mutation total **733 minutes**
(347 + 25 + 361), plus build-slot wait/cold pulls and repair. The new schema and
actual crash cuts are why a single existing “producer throws” test is insufficient.

### Code run and reporting contract

After committing S1-S3, execute the table once through the checkpoint tool:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-0519-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-28-card-0519-stranded-reply-test-design.md --after S1-S3
```

If exit 75, call `dotnet run --no-build --project tools/Antiphon.Checkpoints -- wait
--run <returned-run-directory> --max-wait 50s` until terminal. Await every owned run.
Exit 4 is a slot timeout to report, never a reason to bypass the slot. EF migration
generation/tool bootstrap are setup drivers; lease them and report them explicitly
as non-checkpoint setup. Extra tests/builds require a stated reason.

Report each emitted CHECKPOINT CP-n line with commit, build/reuse outcome, expanded
executed/passed/failed/skipped counts, TRX path and reruns. Preserve crash-cut PID,
MVID, independent before/after store snapshots and receiver evidence. No plan count
or fake-producer acceptance is a substitute for actual execution/recipient evidence.
Next after Code is separate Review; PCs remain pending for post-land Mutation.

TestDesign validation is limited to source/manifest/roster inspection. This host
has neither `dotnet` nor `pwsh` on PATH, so no importer, build, test or PC ran here.
This does not block the documentation deliverable; Code needs the normal .NET,
PowerShell and owned-Docker test lane.
The static document audit passed: one checkpoint table, four valid-width rows with
the same reuse group, 191 minimum results, 47 ordinary minutes, 52 unique guard/PC
mappings, and no named PC target absent from the V matrix. This is a document
consistency check, not execution evidence or official ManifestLoader validation.

--- next stage ---
next: code
handoff: Implement S1-S3 and the ratified V/R roster for durable main, trailing and machine reply publication. Cover all refusal/cancellation/crash cuts, autonomous recovery and loud uncertainty; run CP-1..CP-4 after committed slices and report actual counts. Leave the 52 method-scoped PCs pending for post-land Mutation.
artifact: docs/superpowers/plans/2026-09-28-card-0519-stranded-reply-test-design.md
