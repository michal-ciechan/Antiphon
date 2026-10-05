# CARD-1064: repair inherited channel reply verification

Date: 2026-10-05. Plan task: `4bbcbf05-4561-43dd-9331-8acc6ab0db93`.
Inspected checkout: `17fe69add817134c516a9b21275bac7b67574674`.
Inherited-red base: `82f39bca49efac3633d68839eae9af1272fac2df`.
Status: plan complete; **next: test-design**. The brief requests PC and checkpoint
design but does not fold the separate TestDesign stage into this dispatch. The
verification design below is its concrete starting artifact, not executed evidence.

## Outcome and scope

The 14 CP-51 bridge failures share a stale, hand-built dependency-injection
fixture: `ChannelBridgeTests.HarnessAsync` never registers `ChannelOutboundService`
or its outbound-specific constructor dependencies. The dispatcher now requires
that service, even with unified recovery disabled. Its outer exception handler
logs and returns an empty dispatch result; the fixture uses `NullLogger`, so the
observable failure is no reply. Production and `BridgeQueueHarness` register the
service. The missing registration is present at both inspected commits.

The CP-52 failure is a stale exact string expectation. The session-limit fixture
already freezes time and supplies native transcript timestamps. Production now
formats a reason containing the UTC reset, dated local reset, timezone and raw
provider sentence. The test still expects the former short sentence.

Classification: **two stale-fixture defects; no production recovery defect is
established by these reds**. They do not establish that users currently lose
replies or receive the wrong reset. The bridge tests cannot reach their intended
publication, metadata, turn-window and attachment assertions; the durability test
stops before checking its hold deadline and scheduled retry. That loss of useful
regression evidence matters to CARD-0519's unified-recovery switch.

Repair only the two test files. Preserve exact outcomes and timing budgets. A
fresh green run is still required: static identification of the common obstruction
does not assert that removing it uncovers no other failures.

Plan validation: a read-only source census confirmed 40/25 declared results,
all 19 PC filters resolving to the 15 intended existing methods, two correctly
shaped checkpoint rows, and byte-identical inherited receipt text. No application
build, test or mutation was run by this Plan task.

## Evidence and ground truth

Read the live CARD-1064, CARD-0523 and CARD-0639 descriptions with
`scripts/card.ps1 get <card> -Board Antiphon`. Inspected the previous Code report,
the base TRX's 15 failed result messages, and its `source.json` receipt.

| Card assumption / question | Code or recorded execution | Consequence |
|---|---|---|
| These are S8-introduced regressions. | S8 `7ccbb390ff0d7a28e5140b0a9e593f23adb9875a`: CP-51 40 executed, 26 passed, 14 failed; CP-52 25 executed, 24 passed, 1 failed. Exact 15-method base replay: 15 executed, 0 passed, 15 failed, 0 skipped; clean source, verified reused build, granted slot. | Inherited failures remain red. Do not repeat a whole suite to re-establish inheritance. |
| The old bridge fixture's fake producer is sufficient. | `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs:959` builds its own graph; it registers the producer and `ChatChannelService` but no outbound service/options/file store. `server/Application/Services/ChannelReplyDispatcher.cs:460` resolves `ChannelOutboundService` before preparing/publishing a matched reply. `OnTurnEndAsync` at line 194 catches non-cancellation exceptions and returns the current result. | Register the real facade plus its two missing dependencies in this fixture. The swallowed DI exception explains the shared no-reply symptom. |
| The missing service may also be a production composition defect. | `server/Program.cs:744` onwards unconditionally registers outbound settings, `ChannelOutboundService` and `IChannelOutboundFileStore`. `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:171` onwards does likewise. `ChannelReplyDurabilityTests` uses that shared harness. | Do not change production registration or add a dispatcher fallback to accommodate the incomplete fixture. |
| Unified recovery must be enabled, or the pump run, to restore old replies. | `ChannelOutboundSettings.UnifiedRecoveryEnabled` defaults false. `ChannelOutboundService.SendAsync` directly publishes a catalogued reply when the switch is off, no profile qualifies and no older delivery exists. `DispatchDurableFollowUpsAsync` resolves the facade, then returns when the switch is off. | Keep the fixture explicitly default-off, with no profiles. No pump or conversion graph is needed for these legacy-path assertions. |
| PostgreSQL, shared-store contention or timeout drift explains the reds. | Retained TRX reports outcome assertion failures, not connection, SQL, build or fixture exceptions. The missing service is deterministic in the fixture graph; the quota failure includes the exact rich reset reason. | No database configuration, timeout, retry, assertion-count or scheduling relaxation. Report any newly exposed infrastructure failure separately. |
| CARD-0523's fixed-date bug is still this test's root cause. | At both base and current source, `Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset` constructs `FakeTimeProvider` at `2026-09-05T15:16:00Z` and stamps both stub records with that native time. Failure is at `hold.Reason`, before `DisabledUntil`. | Keep the frozen-date deadline assertion. CARD-0523's original wall-clock premise has already been addressed in code; this card corrects the remaining reason expectation. Caller should reconcile the older card after verification, not claim it already closed. |
| The short reason is the current contract. | `UsageLimitWallParser.FormatReason` at line 139 emits UTC ISO reset + dated local reset/zone + raw text. `ApiErrorRecoveryService.ApplyWallAsync` parses from `EvidenceAt`, stores `ResetAtUtc`, and applies `ModelAvailability.SessionLimitResumePadding`. | Exact expected reason must match this richer diagnostic contract. Preserve exact reset + two-minute padding and retry assertions. |
| Green bridge tests would prove complete recipient acceptance. | The local fake adapter generally has no `OnSubmitted` transcript writer. The queue can return degraded screen-only delivery; these tests insert reply transcripts themselves. CARD-0639 explicitly owns that gap plus enqueue/recovery failures. | These repairs prove dispatcher-to-fake-producer outcomes, not source-agent receipt or real chat delivery. Do not silently migrate the entire fixture or close CARD-0639. |
| Green CP-51/52 would qualify the unified switch. | CARD-0519 assigns these classes to R-2; enabled capture/discovery/pump/crash/transport evidence belongs to its other V/R rows, especially V-11..13. | Use repaired classes as baseline regressions. Rerun them on the integrated CARD-0519 candidate; neither this plan nor its greens authorize activation. |

Evidence locations, read-only from this task:

- Previous stored Markdown report: `/work/worktrees/task-23e91309/.antiphon/task-23e91309.md`.
- Ordinary report: `/work/worktrees/task-23e91309/.antiphon/checkpoints/20261005-090659-7319/report.json`.
- Base TRX and source receipt: `/work/worktrees/task-23e91309/.antiphon/s8-baseline-checkpoints/CP-S8-BASE-FILTER-20261005-092620-c3f0/{run.trx,source.json}`.

Essential base receipt, copied unedited from `source.json`; the earlier zero-test
OR invocation is excluded from evidence:

```text
CHECKPOINT CP-S8-BASE-FILTER commit=82f39bca49efac3633d68839eae9af1272fac2df build=reused filter=/*/*/(ChannelBridgeTests*)|(ChannelReplyDurabilityTests*)/(A_marker_only_reply_sends_the_document_with_no_text*)|(A_mid_turn_launch_note_does_not_steal_the_channel_reply*)|(A_missing_attachment_file_becomes_a_visible_note_not_a_lost_reply*)|(A_queued_channel_prompt_still_routes_the_reply*)|(A_response_ending_in_a_question_is_typed_as_question*)|(A_turn_whose_stop_marker_precedes_the_text_still_replies*)|(An_attach_marker_sends_the_file_inline_and_strips_the_marker_line*)|(An_oversized_attachment_is_skipped_with_a_note*)|(Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*)|(Follow_up_stops_once_the_next_queued_prompt_starts*)|(Follow_up_stops_once_the_next_turn_starts*)|(Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*)|(Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat*)|(Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch*)|(Turn_end_sends_the_agents_answer_down_the_channel*) executed=15 passed=0 failed=15 skipped=0 trx=/work/worktrees/task-23e91309/.antiphon/s8-baseline-checkpoints/CP-S8-BASE-FILTER-20261005-092620-c3f0/run.trx slot=granted waited=0s dirty=0 source=82f39bca49efac3633d68839eae9af1272fac2df sourceState=clean buildSource=verified
```

## Decisions

- **D-1 — Local composition repair.** Add scoped `ChannelOutboundService`, singleton
  `IOptions<ChannelOutboundSettings>` with `UnifiedRecoveryEnabled = false` and
  empty profiles, and a real `IChannelOutboundFileStore` backed by
  `ChannelOutboundFileStore` at a unique fixture-owned temporary root. Existing
  scoped `AppDbContext`, singleton producer and clock satisfy the other constructor
  dependencies. Reject replacing the facade with a fake: that would bypass the
  direct-publication/settlement behavior the tests are supposed to observe.
- **D-2 — Preserve the existing transcript arrangements.** Keep `HarnessAsync` and
  the test-specific single-row/batch insertion helpers. Do not migrate all 40
  results onto `BridgeQueueHarness`: its automatic submit transcript and trailing
  TurnEnd would change the queued-only, stop-before-text and same-batch scenarios.
  CARD-0639 owns that broader receipt repair. Do not shorten its existing compressed
  budgets as part of this change.
- **D-3 — Keep strict quota oracles.** Replace the obsolete exact reason with the
  independent literal below. Do not call `UsageLimitWallParser.FormatReason` to
  generate the expected value, and do not replace equality with `ShouldContain`.
  Preserve `RawText`, no-publish, owed-correlation, `DisabledUntil`, unresolved
  recovery and `NextAttemptAt` checks. Add independent `EvidenceAt` and `ResetAtUtc`
  assertions so native evidence selection and padding are visible separately.
- **D-4 — No production behavior change is justified.** Reject optional service
  resolution, catch suppression, production fallback sends, parser rollback,
  switching unified recovery on, or running the pump to make these tests pass.
  If the repaired fixture exposes a different failure, identify its exact production
  branch and user-visible consequence before extending scope; the current evidence
  does not justify a routing or recovery redesign.
- **D-5 — Portable PostgreSQL lane.** `GET /api/runner-defaults` and
  `GET /api/session-runners` were read on 2026-10-05. Live defaults provided an
  available, dispatch-eligible Linux lane; no Windows dependency is present here.
  Resolve placement again when dispatching. Omit `-Runner` and `-Platform`; do not
  encode a fleet host in the plan. Both checkpoint groups below use the portable
  PostgreSQL lane with the in-memory messaging producer.
- **D-6 — Closed ordinary scope and separate mutation.** Exactly the two named
  classes are ordinary verification: 40 + 25 existing argument-expanded results.
  No whole-Unit or whole-assembly run. Method-scoped positive controls are designed
  here for TestDesign review and later SourceLanding Mutation, not run during Code.
  Existing base-red evidence is provenance, not a deliberate production mutation.

These are code-supported implementation decisions, not unresolved product-policy
defaults. No human decision is required to advance to TestDesign.

Exact expected diagnostic:

```text
session-limit resets 2026-09-05T16:20:00Z (2026-09-05 17:20 Europe/London; You've hit your session limit · resets 5:20pm (Europe/London))
```

## Implementation slices

### S1 — Restore the bridge fixture's outbound graph (45–60 minutes)

Files: `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs` only.

1. Import `Antiphon.Server.Infrastructure.Files`; add the three D-1 registrations
   beside `ChatChannelService` before building the provider. Allocate a unique
   root under the OS temporary directory and carry ownership in `Harness` so
   disposal removes only that root if it was created. Constructor resolution is
   lazy; the store's string-path constructor does not require production hosting.
2. Resolve `ChannelOutboundService` in a temporary async scope before returning
   the harness. This is fixture fail-fast validation, not a new test or publication
   oracle. Retain all real dispatcher calls and fake producer assertions.
3. Keep outbound settings explicit, so future switch defaults do not silently
   turn an immediate-send regression into a deferred-pump test. No conversion,
   hosted-service, broker or live-runner dependency is added.
4. Commit and push; run CP-1. Require all 40 existing results, including every
   bridge method in the PC table, with zero failures/skips. A negative/no-reply
   test passing while the facade is missing is not useful evidence; the same
   correctly composed fixture must support its positive companions.

A minimal fixture repair is sufficient to unblock the 14 outcomes. If CP-1 reveals
a second obstruction, retain the failing method/log and classify it; do not change
its expected reply, increase a deadline or add a retry.

### S2 — Align the session-limit contract and retain date safety (30–45 minutes)

Files: `tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs` only,
method `Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset`.

1. Replace the short expected `hold.Reason` with the literal above. Keep the
   existing frozen provider at `2026-09-05T15:16:00Z` and native stub timestamps.
   Keep the 16:22 UTC expected hold deadline: it is 17:20 London / 16:20 UTC plus
   two minutes, on the same frozen evidence date.
2. After independently loading the recovery, assert `EvidenceAt` equals the
   frozen native instant and `ResetAtUtc` equals `2026-09-05T16:20:00Z`. Keep
   `NextAttemptAt == hold.DisabledUntil` and the unresolved/owed/no-send checks.
   Use independent constants or arithmetic from the test's frozen input, never
   values returned by the parser to construct expected results.
3. Preserve test-scoped cleanup, including the hold on assertion failures via
   the owning harness. Do not delete shared-table rows outside this session.
4. Commit and push; run CP-2. Require all 25 results green. Report CARD-0523's
   already-present clock fix and this remaining diagnostic repair to the caller;
   caller owns updating/closing that related card.

Each slice is a separate meaningful commit on its assigned branch, pushed before
verification. Do not rebase/reset/amend a pushed task commit. Integration onto the
original CARD-0519 Code owner remains caller-owned; this Plan task does not adopt,
land, deploy or activate another task's branch.

## Verification design

TestDesign finalized on 2026-10-05 at source `f79f73a808e4db41a95a8cd2c6825b839d7755d7`.
This section completes the Plan-stage verification draft; the fix design, D-1..6,
S1/S2 and retained inherited-red receipt above are unchanged. **Next: code.**
This is executable design based on body inspection, not fresh green or mutation
evidence. No build, application test or PC was run by TestDesign.

### Inspection

All paths below are repository-relative. Entire test bodies and their setup/cleanup
were read before naming cases; no new test file is proposed.

| Bodies read | Boundaries -> coverage or explicit exclusion |
|---|---|
| `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs`, all 34 test methods plus HarnessAsync, BindChannelAsync, MarkSessionWorkingAsync, InsertEntryAsync, InsertTurnAsync, InsertTranscriptEntriesInOneBatchAsync and DisposeAsync | 40 argument-expanded results -> R-1; all 14 formerly red methods -> V-1..14 and their supplemental guards. Per-row versus same-commit inserts, queued-only versus in-turn queued versus next-turn queued, early stop versus interim answer, attachments with/without text, missing/oversized file are preserved. |
| `tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs`, all 25 methods, CreateHarnessAsync, Restarted, RowAsync, LateConfirmBatch, FailingProducer, AssertTransportNoticeAsync and TransportFixtureBatch | R-2; frozen production SessionLimit case -> V-15..19, V-20, V-24..26. Existing restart, late confirmation, producer exception, ordinary/mixed/follow-up API walls, TTL loss, transport versus capacity companions remain in CP-2. Restarted recreates the dispatcher, not an OS process. |
| `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`, CreateAsync, both OnSubmitted callbacks, all insertion/seed helpers, EmptyRunnerClient and DisposeAsync | Native submitted prompt callback exists here; SeedChannelCorrelationAsync bypasses queue delivery. Own session/agent IDs and hold cleanup survive failed assertions. No migration of the bridge's separate harness (D-2). |
| `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`, SendInputAsync and OnSubmitted contract; `src/Antiphon.Messaging.Client.Testing/FakeAntiphonMessagingClient.cs`, SendAsync/SentReplies and inbound fake | Typed/composer/submitted bytes are distinguishable from complete recipient transcript. SentReplies observes only producer acceptance. Receipt limits below. |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`, assembly lifecycle and options; `TestDbFixtureLifecycle.cs`, CreateAsync/StartAsync/MigrateAsync/BootstrapAsync | Docker-backed PostgreSQL 16, migrations, assembly-shared store -> serial CP-1/2. No new per-test isolation promised. |
| `tests/Antiphon.SessionRunner.Tests/Fixtures/grok-transport-error.jsonl` and its content link in Antiphon.Tests.csproj | CP-2's transport fixture is copied to Fixtures at build. Its header says card-quoted, not a measured live capture; no live-transport proof is inferred from the test method name. |
| `server/Application/Services/ChannelOutboundService.cs`, constructor, SendAsync and PublishDirectAsync; Program.cs outbound registrations; ChannelOutboundSettings defaults; ChannelOutboundFileStore string constructor | D-1's three registrations are sufficient for this switch-off/no-profile path. No new production delivery branch. Temp-root constructor does not create a directory; dispose only the unique owned root if present. |
| `ChannelReplyDispatcher.cs`, OnTurnEndAsync, DispatchAsync, HandleApiErrorWithholdAsync, NotifyCapacityAsync, FindApiErrorStubAsync, PrepareReplyBody, DispatchFollowUpAsync, ExtractTurnResponseAsync, QueryTurnWindowAsync and ClassifyKind; `TranscriptTurnWindow.cs`, both selectors; `ChatChannelService.cs`, StampLastReplyAsync | Concrete mutation sites for V-1..14, V-17 and supplementary publication/window/addressing/metadata guards; exceptions caught by OnTurnEnd cannot be mistaken for a successful behavioral PC. |
| `ApiErrorRecoveryService.cs`, BuildNewRowAsync/ApplyWallAsync; `CapacityEvidence.cs`, Resolve and sibling/TurnEnd loaders; `UsageLimitWallParser.cs`, Parse/FormatReason; `ModelAvailability.cs`, UpsertAutoDetectedAsync | Independent diagnostic, native evidence, stored reset, padded hold and retry assertions -> V-15..19, V-24..26. Same-session/same-UUID sibling text, null TurnEnd.Text, native September evidence and later ingestion are retained. |
| `docs/testing-and-build.md` checkpoint schema, slots, mutation/restoration and delivery rules; `docs/session-runtime-invariants.md` channel receipt and turn-window contracts; `docs/orchestration-loop.md` stage/land/Mutation rules; `docs/project-context.md` | Closed ordinary scope, full transcript receipt, pending-PC review and post-land execution rules below. |

Missing setup is concrete and owned by Code: S1 adds the real outbound facade,
explicit default-off/empty-profile options and a real file store at a unique
fixture-owned temp root, plus fail-fast scoped resolution. S2 replaces only the
obsolete reason literal and adds the two independent persisted evidence assertions.
No new process, broker, conversion graph or timer is needed.

For decisive independent quota PCs, load the recovery and assert EvidenceAt and
ResetAtUtc **before** the existing hold.Reason and DisabledUntil assertions.
Keep all existing exact assertions (including RawText, no-send, owed, unresolved,
deadline and NextAttemptAt). This ordering makes PC-16 red at EvidenceAt even
though wrong evidence would also change the diagnostic date. Retain the exact
frozen clock/native stamps and original CreatedAt ingestion values; record that
ingestion differs from 2026-09-05 15:16 UTC when commissioning PC-16.

Boundary combinations: text before/after TurnEnd, tail before/after the next
UserPrompt, tail before/after a post-TurnEnd QueuedUserPrompt, an in-turn queued
note and same-batch tail/new prompt are covered separately. Text plus file, file
only, missing file and safely oversized file are separate cases. Exact-at-limit
and cumulative multi-file limits, DST rollover/parser grammar, cross-session UUID
collisions, converted/enabled outbound paths and simultaneous dispatch races are
unchanged and excluded from this fixture repair's new claims. The ordinary class
companions still run; this battery does not newly qualify all their production
guards or claim a full transport safety audit.

### Delivery inventory

**New/changed production async delivery paths: zero.** Only fixture composition
and expectations change. The inventory below records the paths the repaired
tests exercise and exactly where their evidence ends.

| Producer -> destination | Durable identity and persistence boundary | Recovery and observable receipt | Coverage / substitute limit |
|---|---|---|---|
| ChannelBridgeService -> real SessionMessageQueueService -> fake registered session adapter | Provider/conversation/native message -> ChannelInbound.Id -> QueueMessageId / SessionQueuedMessage.Id, SourceChannelInboundId, session, body and attempt floor; inbound envelope and queue owner persist | Existing eligible-recipient Redelivered_message_is_not_routed_twice installs OnSubmitted and observes a complete matching UserPrompt via AgentSessionRuntime. Its final query checks one UserPrompt whose Text equals owner.Body, with the channel marker. | R-1 runs this real-queue receipt case. Most other local HarnessAsync tests supply their reply turn manually; Inputs/SubmittedBodies, Sent, and fabricated turn rows do not prove automatic recipient receipt. |
| The same inbound path while recipient is busy | Same persisted inbound/queue identity; Bridge_enqueues_with_channel_origin_and_conversation_key first marks the session Working | Existing test stops at queue metadata; it does not make the recipient eligible and observe a complete UserPrompt | R-1 protects queue metadata only. Busy-to-receipt completion and each enqueue/crash handoff are existing CARD-0639 debt, not completed by S1. |
| Owning transcript turn -> ChannelReplyDispatcher -> real ChannelOutboundService -> in-memory producer | SessionQueuedMessage.Id plus prompt sequence and conversation; switch-off direct path persists ChannelReplySettledAt before produce and rolls it back on exception | V-1..14 and supplementary rows observe full ChannelReply text/kind/route/attachment bytes in SentReplies, then independent catalog/correlation effects. R-2 recreates dispatcher against persisted correlations and tests a throwing producer. | Producer acceptance is a test substitute, not Kafka, gateway or human receipt. Dispatcher recreation does not test a crash between settlement and publication; unified crash recovery remains CARD-0519. |
| API-error transcript -> recovery adoption -> ModelAvailability hold and scheduled retry | AgentSessionId + StubSequence + same-session sibling UUID -> ApiErrorRecovery.Id -> AppliedHoldId; EvidenceAt, ResetAtUtc, DisabledUntil and NextAttemptAt persist | V-15..20 and V-24..26 observe no outgoing reply, the still-owed correlation and exact persisted adoption facts | This is scheduling evidence only. It never proves that a later retry was enqueued or reached a complete recipient UserPrompt. |

Receipt-required matrix retained for CARD-0639 / integrated CARD-0519
qualification: exercise a busy recipient through later eligibility and an already
eligible recipient through the **real queue**; for each, interrupt/fail after
inbound persistence before queue insertion, after queue commit before typing,
and after typing before confirmation. Recover from the persisted identity, pull
the transcript, and require one matching **complete UserPrompt** beyond the
original attempt floor, with no duplicated body. For outbound delivery also cut
before/after publication and correlate the downstream recipient payload, not
only an accepted produce. These are missing receipt tests here, not substitutes
silently counted as V results. CARD-0639 retains ownership of inbound setup,
enqueue-failure and receipt repair; CARD-0519 retains enabled capture/pump/crash
and transport acceptance. No new method names are invented for those unimplemented
cases, and neither card is closed by CP-1/2.

A request, queue insert, event, Sent flag, producer acceptance or acknowledgement
never discharges recipient delivery. Reject any handoff that describes these
fixture greens as end-to-end delivery or activation qualification. The absent
receipt work does not block these two test-only repairs because they introduce
no new or changed production handoff.

### Proves it works now

All V rows are PostgreSQL integration assertions using real production services
and the named fixture substitutes above. Code executes them through the two
full-class CP rows, not as an extra method replay. Exact method filters below
are the executable selection for later PC baseline/red/green phases; each
selects **one** nonparameterized result. V-1..19 retain the Plan numbering.
V-20..35 make previously bundled, independently bypassable checks explicit.

| ID | Behavior | Exact test filter | Expected fixed outcome |
|---|---|---|---|
| V-1 | Main answer publication | `/*/*/ChannelBridgeTests/Turn_end_sends_the_agents_answer_down_the_channel*` | One exact Pasta answer to telegram / h.ChatId; pending count 0. |
| V-2 | Outbound-only catalog stamp | `/*/*/ChannelBridgeTests/Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*` | LastReplyAt set and exact preview; all three inbound fields unchanged. |
| V-3 | Queued-only opener | `/*/*/ChannelBridgeTests/A_queued_channel_prompt_still_routes_the_reply*` | One exact Pasta answer and pending count 0 from a queued-only opener. |
| V-4 | In-turn queued note cannot steal identity | `/*/*/ChannelBridgeTests/A_mid_turn_launch_note_does_not_steal_the_channel_reply*` | No send at the previous boundary; one exact hiPhil answer at its own boundary; persisted settlement. |
| V-5 | Text after an early stop remains owed and sends | `/*/*/ChannelBridgeTests/A_turn_whose_stop_marker_precedes_the_text_still_replies*` | First dispatch empty and pending 1; late text produces one exact answer and pending 0. |
| V-6 | A later real answer follows an interim answer | `/*/*/ChannelBridgeTests/Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat*` | Exactly two replies, exact trailing answer, original conversation; later trigger sends no duplicate. |
| V-7 | Next UserPrompt caps old follow-up | `/*/*/ChannelBridgeTests/Follow_up_stops_once_the_next_turn_starts*` | Count remains 1 after the next UserPrompt and All green. |
| V-8 | Same-batch tail survives a newer prompt | `/*/*/ChannelBridgeTests/Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch*` | Count 2 with exact guest list before the same-batch prompt; remains 2 after next-turn text. |
| V-9 | Answer versus question classification | `/*/*/ChannelBridgeTests/A_response_ending_in_a_question_is_typed_as_question*` | Single reply.Kind is Question. |
| V-10 | Next QueuedUserPrompt caps old follow-up | `/*/*/ChannelBridgeTests/Follow_up_stops_once_the_next_queued_prompt_starts*` | Count remains 1 after the post-TurnEnd queued prompt and All green. |
| V-11 | Inline file plus stripped marker | `/*/*/ChannelBridgeTests/An_attach_marker_sends_the_file_inline_and_strips_the_marker_line*` | Exact marker-free text and one PDF with expected name, MIME and bytes. |
| V-12 | Attachment-only answer has null text | `/*/*/ChannelBridgeTests/A_marker_only_reply_sends_the_document_with_no_text*` | Null reply.Text and one image attachment. |
| V-13 | Missing attachment has visible note | `/*/*/ChannelBridgeTests/A_missing_attachment_file_becomes_a_visible_note_not_a_lost_reply*` | No attachment and visible attachment not found note. |
| V-14 | Oversized attachment respects budget | `/*/*/ChannelBridgeTests/An_oversized_attachment_is_skipped_with_a_note*` | No attachment and visible over the budget note. |
| V-15 | Rich quota diagnostic | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | hold.Reason equals the independent literal in D-3; RawText preserved. |
| V-16 | Native evidence beats ingestion time | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | recovery.EvidenceAt equals 2026-09-05 15:16 UTC, independently of ingestion. |
| V-17 | API wall leaves original answer owed | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | Correlation ChannelReplySettledAt remains null. |
| V-18 | Hold includes two-minute padding | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | hold.DisabledUntil is exactly 2026-09-05 16:22 UTC. |
| V-19 | Standing-session retry uses the hold deadline | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | recovery.NextAttemptAt equals the independently checked hold deadline. |
| V-20 | Withheld API stubs produce no outward message independently of settlement (D-3/S2). | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | No outward reply/notice for the retryable session limit. |
| V-21 | A textless early stop leaves the correlation owed (D-2/S1). | `/*/*/ChannelBridgeTests/A_turn_whose_stop_marker_precedes_the_text_still_replies*` | Pending count 1 after the first textless TurnEnd. |
| V-22 | An in-turn queued note does not cap extraction (D-2/S1). | `/*/*/ChannelBridgeTests/A_mid_turn_launch_note_does_not_steal_the_channel_reply*` | Full hiPhil text remains in-window despite an in-turn queued note. |
| V-23 | Advance the trailing-text watermark so later triggers do not duplicate it (D-2/S1). | `/*/*/ChannelBridgeTests/Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat*` | Final count remains 2 after repeated trailing triggers. |
| V-24 | Persist the raw provider sentence independently of the rendered reason (D-3/S2). | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | hold.RawText equals the complete input provider sentence. |
| V-25 | Persist ResetAtUtc independently of EvidenceAt and the padded hold (D-3/S2). | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | recovery.ResetAtUtc is exactly 2026-09-05 16:20 UTC. |
| V-26 | Keep a first standing-session SessionLimit recovery unresolved (D-3/S2). | `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | recovery.ResolvedAt is null and no terminal notice is published. |
| V-27 | Explain an oversized omission independently of refusing its bytes (S1). | `/*/*/ChannelBridgeTests/An_oversized_attachment_is_skipped_with_a_note*` | Visible over the note when the 64-byte file exceeds the 16-byte budget. |
| V-28 | Preserve attachment content bytes independently of stripping its marker (S1). | `/*/*/ChannelBridgeTests/An_attach_marker_sends_the_file_inline_and_strips_the_marker_line*` | attachment.Content exactly equals the PDF fixture bytes. |
| V-29 | An outbound stamp preserves inbound LastMessageAt (S1). | `/*/*/ChannelBridgeTests/Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*` | after.LastMessageAt equals before.LastMessageAt. |
| V-30 | An outbound stamp preserves inbound LastAuthor (S1). | `/*/*/ChannelBridgeTests/Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*` | after.LastAuthor equals before.LastAuthor (Mike). |
| V-31 | An outbound stamp preserves the inbound native message ID (S1). | `/*/*/ChannelBridgeTests/Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*` | after.LastChannelMessageId equals the original inbound ID. |
| V-32 | A main reply retains the matched provider (S1). | `/*/*/ChannelBridgeTests/Turn_end_sends_the_agents_answer_down_the_channel*` | Main reply.Channel equals telegram. |
| V-33 | A main reply retains the matched conversation (S1). | `/*/*/ChannelBridgeTests/Turn_end_sends_the_agents_answer_down_the_channel*` | Main reply.ConversationId equals h.ChatId. |
| V-34 | A trailing reply retains the original conversation (D-2/S1). | `/*/*/ChannelBridgeTests/Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat*` | Second reply.ConversationId equals h.ChatId. |
| V-35 | Successful direct publication durably consumes its owed correlation (D-1/S1). | `/*/*/ChannelBridgeTests/Turn_end_sends_the_agents_answer_down_the_channel*` | Final pending count is 0 after the main answer. |

### Guards the regression

- **R-1:** the entire `ChannelBridgeTests` class, CP-1, exactly 40 argument-expanded
  results (34 methods; ClassifyKind and ExtractAttachments each expand to four).
  Decisive repaired oracles are the 14 named reply methods above, plus existing
  negative/unbound/dedup/attachment/proactive companions. A negative-only pass
  with an unresolvable facade is rejected by the positive cases and fail-fast DI.
- **R-2:** the entire `ChannelReplyDurabilityTests` class, CP-2, exactly 25 results.
  The repaired production SessionLimit method must reach all persisted reason,
  EvidenceAt, ResetAtUtc, DisabledUntil, unresolved and NextAttemptAt checks,
  while the existing restart, withheld/owed, loss, idempotency and transport
  companion assertions remain green. Keep scoped hold cleanup on failure.
- All expectations stay independent of the implementation under test: use the
  D-3 literal and UTC constants, never FormatReason/Parse to manufacture the
  expected value. No timeout widening, retry addition, count reduction or switch
  activation is an acceptable repair. Existing inherited-red provenance above
  is retained verbatim; new failures need exact-method base comparison.

### Guard inventory

The original 19 controls are retained. Sixteen additional controls split guards
that the original red would not exercise independently: API no-publish versus
owed settlement; textless retention versus late extraction; owning prompt versus
window cap; trailing publication versus watermark; raw/evidence/reset/deadline/
retry/terminal state; attachment stripping versus bytes or omission note;
outbound metadata versus inbound preservation; publication versus route and
settlement. These add no Code test methods or ordinary executions.

| Guard | Plan reference + independently bypassable guard/invariant | Unique control |
|---|---|---|
| G-1 | Publish a matched main answer through the producer (D-1/S1). | PC-1 |
| G-2 | Stamp LastReplyAt after successful publication (S1). | PC-2 |
| G-3 | Allow a queued-only prompt to own its reply window (D-2/S1). | PC-3 |
| G-4 | Prefer the in-turn UserPrompt over a later queued launch note (D-2/S1). | PC-4 |
| G-5 | Extract assistant text after an early TurnEnd (D-2/S1). | PC-5 |
| G-6 | Publish nonempty trailing text after an interim answer (D-2/S1). | PC-6 |
| G-7 | Cap a follow-up at the next UserPrompt (D-2/S1). | PC-7 |
| G-8 | Drain in-window text before retiring a window capped in the same batch (D-2/S1). | PC-8 |
| G-9 | Classify a final question as Question (S1). | PC-9 |
| G-10 | Cap a follow-up at a QueuedUserPrompt that follows TurnEnd (D-2/S1). | PC-10 |
| G-11 | Remove attachment marker lines from outgoing text (S1). | PC-11 |
| G-12 | Represent an attachment-only reply with null text (S1). | PC-12 |
| G-13 | Report a missing attachment visibly without losing the text reply (S1). | PC-13 |
| G-14 | Exclude an attachment exceeding the remaining byte budget (S1). | PC-14 |
| G-15 | Retain the exact rich reset diagnostic (D-3/S2). | PC-15 |
| G-16 | Choose native evidence time ahead of ingestion time (D-3/S2). | PC-16 |
| G-17 | Leave the original correlation unsettled during retryable API withholding (D-3/S2). | PC-17 |
| G-18 | Apply two-minute padding to the parsed reset deadline (D-3/S2). | PC-18 |
| G-19 | Schedule the standing-session retry at the hold deadline (D-3/S2). | PC-19 |
| G-20 | Withheld API stubs produce no outward message independently of settlement (D-3/S2). | PC-20 |
| G-21 | A textless early stop leaves the correlation owed (D-2/S1). | PC-21 |
| G-22 | An in-turn queued note does not cap extraction (D-2/S1). | PC-22 |
| G-23 | Advance the trailing-text watermark so later triggers do not duplicate it (D-2/S1). | PC-23 |
| G-24 | Persist the raw provider sentence independently of the rendered reason (D-3/S2). | PC-24 |
| G-25 | Persist ResetAtUtc independently of EvidenceAt and the padded hold (D-3/S2). | PC-25 |
| G-26 | Keep a first standing-session SessionLimit recovery unresolved (D-3/S2). | PC-26 |
| G-27 | Explain an oversized omission independently of refusing its bytes (S1). | PC-27 |
| G-28 | Preserve attachment content bytes independently of stripping its marker (S1). | PC-28 |
| G-29 | An outbound stamp preserves inbound LastMessageAt (S1). | PC-29 |
| G-30 | An outbound stamp preserves inbound LastAuthor (S1). | PC-30 |
| G-31 | An outbound stamp preserves the inbound native message ID (S1). | PC-31 |
| G-32 | A main reply retains the matched provider (S1). | PC-32 |
| G-33 | A main reply retains the matched conversation (S1). | PC-33 |
| G-34 | A trailing reply retains the original conversation (D-2/S1). | PC-34 |
| G-35 | Successful direct publication durably consumes its owed correlation (D-1/S1). | PC-35 |

All 35 guards are unexecuted by this TestDesign stage and have executable controls.
There are no unmapped in-scope guards. Surrounding untouched behavior in R-1/R-2
is ordinary regression coverage, not a claim that this task exhausts mutation
qualification of every service reached by the full classes.

### Positive controls

Each row breaks only its named guard with a compiling production defect. Its V
row supplies the **exact existing method and filter**; reuse that filter for every
phase with `-MinExecuted 1` and the exact Class.Method in `-Expect`. Trailing
method wildcards are intentional for the pinned TUnit discovery implementation.
Inspect the executed roster: one result from that method, no extra method.

| PC | Break guard by this compiling defect | Exact method/filter | Expected first failing assertion |
|---|---|---|---|
| PC-1 | G-1: In `ChannelOutboundService.PublishDirectAsync`, replace only `await _producer.SendAsync(reply, ct);` with `await Task.CompletedTask;`, keeping its Published result and settlement. | V-1 | `h.Messaging.SentReplies.ShouldHaveSingleItem()` fails with 0. |
| PC-2 | G-2: In `ChannelReplyDispatcher.DispatchAsync`, omit the published-path `StampLastReplyAsync` call. | V-2 | `after.LastReplyAt.ShouldNotBeNull()` fails; inbound column checks remain. |
| PC-3 | G-3: In `TranscriptTurnWindow.FindOwningPromptAsync`, delete only the null-coalesced queued fallback, leaving the UserPrompt query. | V-3 | `h.Messaging.SentReplies.ShouldHaveSingleItem()` fails with 0. |
| PC-4 | G-4: In `FindOwningPromptAsync`, replace the entire owningId expression with `window.OrderByDescending(t => t.Sequence).Select(t => (Guid?)t.Id).FirstOrDefault()`. | V-4 | The final `h.Messaging.SentReplies.ShouldHaveSingleItem()` fails with 0 after the owning TurnEnd; the earlier empty assertion passes. |
| PC-5 | G-5: In `ExtractTurnResponseAsync`, after QueryTurnWindowAsync insert `var firstEnd = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > promptSeq).MinAsync(t => (long?)t.Sequence, ct); if (firstEnd.HasValue) entries = entries.Where(t => t.Sequence < firstEnd.Value).ToList();` before computing maxSeq. | V-5 | The SECOND dispatch's `h.Messaging.SentReplies.ShouldHaveSingleItem()` fails with 0; the first dispatch's owed-count assertion passes. |
| PC-6 | G-6: In `DispatchFollowUpAsync`, after its texts.Count == 0 block, insert `if (texts.Count > 0) return;` before the watermark claim. | V-6 | `h.Messaging.SentReplies.Count.ShouldBe(2, ...)` fails with 1. |
| PC-7 | G-7: In `FindNextTurnOpeningPromptSeqAsync`, remove only `row.Kind == TranscriptKinds.UserPrompt \|\|` from the returning condition; retain the queued-after-TurnEnd arm. | V-7 | The FINAL `h.Messaging.SentReplies.Count.ShouldBe(1, ...)` fails with 2 (All green leaks). |
| PC-8 | G-8: In `DispatchFollowUpAsync`, insert `if (nextPromptSeq is not null) return;` immediately after QueryTurnWindowAsync. | V-8 | The FIRST `h.Messaging.SentReplies.Count.ShouldBe(2, ...)` fails with 1, before the later out-of-window-text check. |
| PC-9 | G-9: Replace the body of `ChannelReplyDispatcher.ClassifyKind` with `return ChannelReplyKind.Answer;`. | V-9 | `h.Messaging.SentReplies.ShouldHaveSingleItem().Kind.ShouldBe(ChannelReplyKind.Question)` fails with Answer. |
| PC-10 | G-10: In `FindNextTurnOpeningPromptSeqAsync`, remove only the QueuedUserPrompt arm of the returning condition, retaining UserPrompt; remove the now-unused sawTurnEnd declaration and assignment, keeping the TurnEnd continue. | V-10 | The FINAL `h.Messaging.SentReplies.Count.ShouldBe(1, ...)` fails with 2 (All green leaks). |
| PC-11 | G-11: In `PrepareReplyBody`, set `var text = responseText;` instead of descriptor.Text after RemoveMarkers; retain attachment preparation. | V-11 | `reply.Text.ShouldBe("Here's the invoice 🩵")` fails because the attach marker remains. |
| PC-12 | G-12: In main `DispatchAsync`'s ChannelReply initializer, set `Text = targetText` instead of the length-to-null ternary. | V-12 | `reply.Text.ShouldBeNull(...)` fails with an empty string. |
| PC-13 | G-13: In `PrepareReplyBody`, remove only the missing-file `notes.Add`, retaining the exists check, log and continue. | V-13 | `reply.Text.ShouldContain("attachment not found")` fails; the text remains Here you go: and attachments remain empty. |
| PC-14 | G-14: In `PrepareReplyBody`, change `if (file.Length > budget)` to `if (file.Length > long.MaxValue)`, leaving file reads and notes code well-typed. | V-14 | `reply.Attachments.ShouldBeEmpty()` fails with the safe 64-byte fixture attachment. |
| PC-15 | G-15: In `UsageLimitWallParser.FormatReason`'s SessionLimit branch replace its return with `return "session-limit resets 17:20 Europe/London";`. | V-15 | The independent exact `hold.Reason.ShouldBe(...)` fails with the old short sentence. |
| PC-16 | G-16: In `CapacityEvidence.Resolve`, insert the existing usable turnEndCreatedAt return block BEFORE the native timestamp branches (and remove its old copy). | V-16 | The added `recovery.EvidenceAt.ShouldBe(now.UtcDateTime)` fails at ingestion time. As specified in Inspection, load/assert recovery evidence before hold.Reason so this is the decisive first red. |
| PC-17 | G-17: In `HandleApiErrorWithholdAsync`, insert `await SettleAsync(db, open, ct);` immediately before the unresolved non-transport branch's return; retain withholding and adoption. | V-17 | `(await RowAsync(messageId)).ChannelReplySettledAt.ShouldBeNull()` fails with a timestamp. |
| PC-18 | G-18: In `ApiErrorRecoveryService.ApplyWallAsync` change only `reset + ModelAvailability.SessionLimitResumePadding` to `reset`. | V-18 | `hold.DisabledUntil.ShouldBe(new DateTime(2026, 9, 5, 16, 22, 0, DateTimeKind.Utc))` fails with 16:20 UTC. ResetAtUtc remains 16:20. |
| PC-19 | G-19: In `ApplyWallAsync`'s SessionLimit branch change only `row.NextAttemptAt = disabledUntil;` to `row.NextAttemptAt = null;`. | V-19 | `recovery.NextAttemptAt.ShouldBe(hold.DisabledUntil)` fails with null; adoption and hold assertions pass. |
| PC-20 | G-20: In `ChannelReplyDispatcher.HandleApiErrorWithholdAsync`, inside the unresolved non-transport branch, insert `await NotifyCapacityAsync(db, open, stubText ?? "PC-20", ct);` immediately before its existing return; leave adoption and settlement untouched. | V-20 | `h.Messaging.SentReplies.ShouldBeEmpty(...)` fails with one notice. |
| PC-21 | G-21: In `DispatchAsync`'s `string.IsNullOrWhiteSpace(responseText)` branch insert `await SettleAsync(db, open, ct);` before returning. | V-21 | The FIRST `PendingCountAsync(...).ShouldBe(1, ...)` fails with 0; no late-text assertion substitutes. |
| PC-22 | G-22: In `TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync`, remove only `&& sawTurnEnd` from the QueuedUserPrompt arm, retaining owning-prompt selection; remove the now-unused sawTurnEnd declaration and assignment, keeping the TurnEnd continue. | V-22 | The final `h.Messaging.SentReplies.ShouldHaveSingleItem()` fails with 0 after the owning TurnEnd; the earlier empty assertion still passes. |
| PC-23 | G-23: In `DispatchFollowUpAsync` replace `var claimed = turn with { MaxTextSeq = late[^1].Sequence };` with `var claimed = turn;`, retaining TryUpdate and publication. | V-23 | `h.Messaging.SentReplies.Count.ShouldBe(2, ...)` fails with 3 after the final TurnEnd. |
| PC-24 | G-24: In `ApiErrorRecoveryService.ApplyWallAsync` pass `null` instead of `wall.RawText` as the rawText argument of `UpsertAutoDetectedAsync`; retain FormatReason and parsing. | V-24 | `hold.RawText.ShouldBe(text)` fails with null. |
| PC-25 | G-25: In `ApplyWallAsync` replace `row.ResetAtUtc = wall?.ResetAt;` with `row.ResetAtUtc = null;`; leave the local parsed wall and deadline untouched. | V-25 | The added `recovery.ResetAtUtc.ShouldBe(new DateTime(2026, 9, 5, 16, 20, 0, DateTimeKind.Utc))` fails with null. |
| PC-26 | G-26: In `ApplyWallAsync`'s SessionLimit branch set `row.ResolvedAt = now;` instead of null, retaining NextAttemptAt. | V-26 | `h.Messaging.SentReplies.ShouldBeEmpty(...)` fails: HandleApiErrorWithholdAsync treats the erroneously resolved recovery as terminal and emits a notice. This is distinct from PC-20, which leaks a notice while recovery stays unresolved; do not claim the later ResolvedAt assertion ran. |
| PC-27 | G-27: In `PrepareReplyBody` remove only the `notes.Add` in the `file.Length > budget` branch; keep its log and continue. | V-27 | `reply.Text.ShouldContain("over the")` fails; `reply.Attachments.ShouldBeEmpty()` still passes. |
| PC-28 | G-28: In `PrepareReplyBody`'s OutboundAttachment initializer replace `Content = File.ReadAllBytes(file.FullName)` with `Content = Array.Empty<byte>()`; retain Name, Mime, Kind and text. | V-28 | `attachment.Content.ShouldBe(bytes)` fails with empty content after the earlier text/metadata checks pass. |
| PC-29 | G-29: In `ChatChannelService.StampLastReplyAsync` add `.SetProperty(c => c.LastMessageAt, (DateTime?)DateTime.UnixEpoch)` to its ExecuteUpdate chain. | V-29 | `after.LastMessageAt.ShouldBe(before.LastMessageAt)` fails. |
| PC-30 | G-30: In `ChatChannelService.StampLastReplyAsync` add `.SetProperty(c => c.LastAuthor, "PC-30")` to its ExecuteUpdate chain. | V-30 | `after.LastAuthor.ShouldBe(before.LastAuthor)` fails (PC-30 versus Mike). |
| PC-31 | G-31: In `ChatChannelService.StampLastReplyAsync` add `.SetProperty(c => c.LastChannelMessageId, "PC-31")` to its ExecuteUpdate chain. | V-31 | `after.LastChannelMessageId.ShouldBe(before.LastChannelMessageId)` fails. |
| PC-32 | G-32: In `DispatchAsync`'s ChannelReply initializer set `Channel = "pc32"` instead of target.Provider. The catalog-less direct path still publishes through the fake. | V-32 | `reply.Channel.ShouldBe("telegram")` fails with pc32, after the one-reply check passes. |
| PC-33 | G-33: In `DispatchAsync`'s ChannelReply initializer set `ConversationId = "pc33"` instead of target.ConversationId. Keep source.CorrelationIds and ReplyHandle unchanged. | V-33 | `reply.ConversationId.ShouldBe(h.ChatId)` fails with pc33 after one reply and provider checks pass. |
| PC-34 | G-34: In `DispatchFollowUpAsync`'s ChannelReply initializer set `ConversationId = "pc34"` instead of target.ConversationId, retaining main-path routing. | V-34 | `h.Messaging.SentReplies[1].ConversationId.ShouldBe(h.ChatId)` fails with pc34 after count and exact text pass. |
| PC-35 | G-35: In `ChannelOutboundService.PublishDirectAsync` set `row.ChannelReplySettledAt = null;` instead of now in the pre-publication loop, leaving the producer call intact. | V-35 | The final `PendingCountAsync(...).ShouldBe(0)` fails with 1 after the exact reply assertions pass. |

PC-20 deliberately bypasses the retryable no-notice decision through the existing
real notification helper; it does not replace the fake producer or alter the
test. PC-26 independently makes a retryable recovery terminal and expects that
observable terminal-notice defect. The shared first failing no-send assertion
does not merge those guards or their separate mutations. All other controls
likewise run independently; a red at one assertion cannot discharge another PC.
Test/fixture errors, missing DI, timeouts, absent TRX, zero tests, or compilation
failure are not a positive-control red.

Run **baseline / break / red / exact restore / green** after ordinary Code,
separate ordinary Review and confirmed land in a commissioned SourceLanding
Mutation snapshot. Review judges this design before land; Code runs V/R only.
Use the local inherited `scripts/run-checkpoint.ps1` driver and its documented
external-evidence copy/layout, `bin-c1064-pcN-baseline/`,
`bin-c1064-pcN-red/`, `bin-c1064-pcN-green/` (N is the numeric row ID), separate
external evidence directories and the same exact one-method filter for every
phase. The driver takes the host slot; never wrap it in a second slot gate.
Baseline/green need exit 0 and 1 passed/0 failed/0 skipped; red needs exit 1 and
the exact assertion above. A build/fixture failure or selection error is not red.

All cycles run serially: many mutations touch shared methods and these fixtures
use the shared store. Do not combine mutants merely because they share a test.
Keep source frozen while each driver runs, await all children, refresh restored
timestamps and rebuild before green. Record baseline SHA, patch, failing assertion
and restored tracked-byte equality per PC. Snapshot edits and evidence never
commit/push; caller-commissioned repair returns through Code/Review/land.

Budget execution in 30–60 minute blocks of five controls: PC-1..5, PC-6..10,
PC-11..15, PC-16..20, PC-21..25, PC-26..30 and PC-31..35. These are reporting
checkpoints within the commissioned battery, not permission to leave a mutant
or orphan a running child at settlement.

### Out of scope

- Production code/configuration, deployment, unified-recovery activation, parser
  rollback, and changes to CARD-0519's ownership or qualification criteria.
- CARD-0639's local-fixture automatic receipt/enqueue/crash matrix. The limitations
  above remain visible; S1 does not migrate HarnessAsync to BridgeQueueHarness.
- Actual delayed retry execution, live provider/Kafka/gateway receipt, OS-process
  crash recovery and capacity behavior for delegated tasks. These tests cannot
  establish those results.
- New boundary matrices for exactly-at-budget/multi-file attachments, DST or
  parser grammar, concurrent producers and cross-session sibling ownership:
  no changed production branch or new assertion here depends on them.
- A whole Unit/namespace/assembly run, extra preliminary 15-method replay, new
  test cases or extra helper tests for reversible temp-root/DI plumbing. Inspect
  ownership/disposal and fail-fast resolution in Review; do not treat a setup
  exception as a behavioral PC.

### Execution and evidence

Use the checkpoint tool once per committed slice, awaiting completion even when
`wait` returns 75. The tool's rows acquire build slots themselves. If a bootstrap
build is needed, it is a declared tool-only build through `build-slot.ps1`, not
another application verification row:

```powershell
$plan = 'docs/superpowers/plans/2026-10-05-card-1064-inherited-channel-reply-reds-plan.md'
$source = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1064-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1064-tool/ --nologo
# S1: CP-1. S2: CP-2. Final ordinary Review: CP-1,CP-2 on its reviewed source.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1064-tool/ -- run --plan $plan --rows CP-1 --serial --expected-source-sha $source --max-wait 55s
# When exit 75 names a live run, assign its printed id to $runId and keep waiting.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1064-tool/ -- wait --run $runId --max-wait 55s
```

No host pin or fleet URL is part of these commands. PostgreSQL comes through the
existing `TestDbFixture` contract; the producer remains offline. Rows run serially
with `TUNIT_MAX_PARALLEL_TESTS=1` because these fixtures use shared-store state.
Do not overlap them with another assembly or add a new isolation mechanism here.

Report CP-n counts and unedited CHECKPOINT lines with source/build provenance,
slot outcome, fresh TRX and every failed method. Preserve the distinction between
the previous owner's inherited results and new runs. Slot timeout is not run,
not a reason to bypass the gate. Any newly exposed failure is targeted at the
relevant base before being called inherited; do not re-run an entire assembly.

Generated TRX/JSON/logs stay ignored. Code/Review run
`scripts/check-evidence-diff.ps1` over the full task base..pushed-tip range. Remove
all task-owned `bin-c1064-*` outputs, including bootstrap output across referenced
projects, after all runs terminate; use checkpoint cleanup for its owned outputs.
SourceLanding Mutation instead follows its external evidence/restoration contract
and commits nothing from its snapshot. No production activation is required.


### Checkpoints

The union is the entire ordinary Code scope: 65 existing argument-expanded
results and V-1..35/R-1..2. Each row owns one isolated build and one exact class
filter. `Min` is the TUnit execution floor, never elapsed time; the fixed-source
roster must still equal 40 and 25. Both rows are portable PostgreSQL integration
with an offline producer. Use current dispatch defaults, with no fleet host pin.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1064-cp1/` | portable-pg-bridge | `/*/*/ChannelBridgeTests/*` | V-1..14, V-21..23, V-27..35, R-1 | all 40 results including the 14 named bridge methods, 0 failed/skipped | 40 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1064-cp2/` | portable-pg-durability | `/*/*/ChannelReplyDurabilityTests/*` | V-15..20, V-24..26, R-2 | all 25 results including the named session-limit method, 0 failed/skipped | 25 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

All numbers are **estimates**, not measurements from this TestDesign task. Slot
queue time is additional wall time and must be reported separately.

- Ordinary V/R floor (**Code**) = **7 + 6 = 13 minutes**, the sum of CP
  EstimatedMinutes. CP-1 filter `/*/*/ChannelBridgeTests/*`: estimate 3 minutes
  isolated build + 4 minutes execution/setup. CP-2 filter
  `/*/*/ChannelReplyDurabilityTests/*`: 3 + 3 minutes. These include each
  test host's PostgreSQL startup/migration. Tool-only bootstrap/setup adds
  **2 minutes**, for **15 minutes** Code verification overhead. Retain S1
  45–60 minutes and S2 30–45 minutes as total slice estimates, inclusive of
  their CP-1 and CP-2 verification respectively (bootstrap belongs to S1).
- PC floor (**Mutation**) = **35 x 10 = 350 minutes**. **Each named PC-1..35
  uses exactly its V-row method filter:** baseline 3 minutes (2 build + 1 test),
  mutated red 3 (2 + 1), exact restoration/evidence check 1, restored green
  3 (2 + 1). Each five-control block above is **50 minutes**. The original 19
  cost 190 minutes; the 16 independent controls add 160. Do not charge a
  class/suite replay or shared-test mutant batch as a substitute.
- Total setup/build + ordinary V/R + all PC baseline/red/restore/green =
  **2 + 13 + 350 = 365 minutes** (6 hours 5 minutes), excluding code authoring,
  queue waits, analysis and failure repair. Application builds are already
  included: Code 6 minutes plus Mutation 210; never add those again.
  A separately commissioned ordinary Review with the same 13-minute rows and
  2-minute bootstrap adds **15 minutes**, making **380 minutes** for
  Code + ordinary Review + post-land Mutation verification.
- Savings: omitting a duplicate preliminary inherited-red replay saves an
  estimated **5 minutes** (2 build + 3 execution); full-class Code coverage
  remains all 65 results. No PC batching savings are claimed (**0 minutes**):
  shared methods/state require independent phases. The increased PC cost is
  explicit rather than hidden under the 13-minute ordinary floor.

Handoff audit: **bodies read; guards=35, mapped=35, missing=0, duplicate PC maps=0;
all PCs executable as specified; numeric Cost present**. Read-only source census
confirmed the 35 filters resolve uniquely to the 15 intended existing methods,
each with one execution; the two full-class rosters remain 34 methods / 40
results and 25 methods / 25 results. Static table validation confirmed all 35 V
IDs occur in the CP union, the 13-minute sum, the required headings and the
one-to-one guard map. The fix-design prefix and inherited receipt are byte-for-byte
unchanged. Execution evidence is pending Code/Review/Mutation, not represented
by this audit.
