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

### Boundaries and delivery inventory

| Producer -> destination | Durable identity / persistence | Recovery and receipt oracle | Limit |
|---|---|---|---|
| Bridge -> real session queue -> fake agent adapter | `ChannelInbound` plus `SessionQueuedMessage.Id`, session and conversation; complete body/attempt metadata | Existing bridge fixtures manually insert the reply's prompt/turn. The fake producer's full `ChannelReply` is observed after dispatcher completion. | This task does not turn typed input or Sent into proof of recipient acceptance. CARD-0639 owns submit/enqueue/recovery evidence. |
| Transcript turn -> dispatcher -> real outbound facade -> fake producer | Owning prompt sequence, original queue correlation, conversation/handle; direct-send correlation settlement | Exact payload/kind/attachment bytes, catalog stamp, pending count and follow-up counts in V-1..14; R-1 includes full bridge positive/negative companions. | Switch-off direct publication only. Fake producer acceptance is not Kafka, gateway or human receipt. |
| API-error stub -> recovery service -> persisted hold/retry | Same-session sibling UUID and TurnEnd sequence, native timestamp, `ApiErrorRecovery`, linked `ModelAvailabilityHold` | V-15..19: no outgoing answer, unsettled correlation, exact rich reason, native evidence time, exact reset/padding and scheduled retry | Does not run the retry later or qualify quota behavior for delegated tasks. Existing full durability class supplies surrounding regression checks. |
| Unified recovery after integration | CARD-0519 captured delivery/member/root identity and publication attempt | CARD-0519 V-11..13 and its R-2 reruns remain required | No process-crash or real-transport claim from these fixture repairs. |

Ordinary requirements: **R-1** is the full `ChannelBridgeTests` class (40 results);
**R-2** is the full `ChannelReplyDurabilityTests` class (25 results). These are
CARD-1064-local IDs; together they repair CARD-0519 R-2's inherited red debt.
The positive-control table also inventories all 15 inherited failing methods.

### Positive controls: one production behavior per mutation

Each row names one exact test method with the required trailing wildcard. V-n
and PC-n share the numeric suffix. Ordinary Code executes the assertions through
CP-1/2; a separately commissioned SourceLanding Mutation owns baseline, deliberate
red, exact restoration and green for each PC. No PC is discharged by this Plan.

Use the fixed production implementation as baseline. Build/fixture errors or zero
results do not count as red. In particular, deleting a DI registration is not a
behavioral PC: the mutation must reach and fail the outcome assertion below.
All mutations are temporary, method-scoped at execution, and restored exactly.

| V / PC | Behavior and exact filter | Temporary production mutation | Expected failing oracle |
|---|---|---|---|
| V-1 / PC-1 | Main answer publication: `/*/*/ChannelBridgeTests/Turn_end_sends_the_agents_answer_down_the_channel*` | In `ChannelOutboundService.PublishDirectAsync`, skip the producer call while reporting Published. | `SentReplies.ShouldHaveSingleItem()` fails with zero replies. |
| V-2 / PC-2 | Outbound-only catalog stamp: `/*/*/ChannelBridgeTests/Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns*` | In `ChannelReplyDispatcher.DispatchAsync`, omit the published-path `StampLastReplyAsync` call. | `after.LastReplyAt.ShouldNotBeNull()` fails; inbound column checks remain. |
| V-3 / PC-3 | Queued-only opener: `/*/*/ChannelBridgeTests/A_queued_channel_prompt_still_routes_the_reply*` | In `TranscriptTurnWindow.FindOwningPromptAsync`, remove the QueuedUserPrompt fallback. | Exact reply count/text fails. |
| V-4 / PC-4 | In-turn queued note cannot steal identity: `/*/*/ChannelBridgeTests/A_mid_turn_launch_note_does_not_steal_the_channel_reply*` | In `TranscriptTurnWindow.FindOwningPromptAsync`, choose the latest prompt of either kind instead of preferring UserPrompt. | Final Hi Phil reply/settlement assertion fails after the owning TurnEnd. |
| V-5 / PC-5 | Text after an early stop remains owed and sends: `/*/*/ChannelBridgeTests/A_turn_whose_stop_marker_precedes_the_text_still_replies*` | In `ChannelReplyDispatcher.ExtractTurnResponseAsync`, exclude assistant entries after the first TurnEnd following the prompt. | Second dispatch has no expected reply; first dispatch's pending assertion remains intact. |
| V-6 / PC-6 | A later real answer follows an interim answer: `/*/*/ChannelBridgeTests/Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat*` | Return from `DispatchFollowUpAsync` before publishing nonempty trailing text. | Expected two replies and exact second answer fails. |
| V-7 / PC-7 | Next UserPrompt caps old follow-up: `/*/*/ChannelBridgeTests/Follow_up_stops_once_the_next_turn_starts*` | In `TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync`, ignore UserPrompt as a cap. | Next turn's All green text leaks; final count exceeds one. |
| V-8 / PC-8 | Same-batch tail survives a newer prompt: `/*/*/ChannelBridgeTests/Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch*` | In `DispatchFollowUpAsync`, return immediately whenever `nextPromptSeq` is non-null, before draining the window. | In-window guest-list second reply is missing. |
| V-9 / PC-9 | Answer versus question classification: `/*/*/ChannelBridgeTests/A_response_ending_in_a_question_is_typed_as_question*` | Make `ChannelReplyDispatcher.ClassifyKind` always return Answer. | Published `Kind.ShouldBe(Question)` fails. |
| V-10 / PC-10 | Next QueuedUserPrompt caps old follow-up: `/*/*/ChannelBridgeTests/Follow_up_stops_once_the_next_queued_prompt_starts*` | In `FindNextTurnOpeningPromptSeqAsync`, ignore QueuedUserPrompt even after TurnEnd. | Final count exceeds one due to leaked next-turn text. |
| V-11 / PC-11 | Inline file plus stripped marker: `/*/*/ChannelBridgeTests/An_attach_marker_sends_the_file_inline_and_strips_the_marker_line*` | In `PrepareReplyBody`, retain the raw response as returned text while retaining attachment processing. | Exact text comparison catches the marker leak; file bytes/MIME checks remain. |
| V-12 / PC-12 | Attachment-only answer has null text: `/*/*/ChannelBridgeTests/A_marker_only_reply_sends_the_document_with_no_text*` | In main `DispatchAsync`, assign empty `targetText` instead of converting it to null. | `reply.Text.ShouldBeNull()` fails while the image still exists. |
| V-13 / PC-13 | Missing attachment has visible note: `/*/*/ChannelBridgeTests/A_missing_attachment_file_becomes_a_visible_note_not_a_lost_reply*` | In `PrepareReplyBody`, omit the missing-file note but retain `continue`. | Published text lacks `attachment not found`; no file I/O exception substitutes for red. |
| V-14 / PC-14 | Oversized attachment respects budget: `/*/*/ChannelBridgeTests/An_oversized_attachment_is_skipped_with_a_note*` | In `PrepareReplyBody`, bypass only the size-budget branch. | Attachments are unexpectedly nonempty; the 64-byte fixture is safe to read. |
| V-15 / PC-15 | Rich quota diagnostic: `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | In `UsageLimitWallParser.FormatReason`, return the old short session-limit reason. | Independent exact `hold.Reason` comparison fails. |
| V-16 / PC-16 | Native evidence beats ingestion time: `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | In `CapacityEvidence.Resolve`, prefer usable TurnEnd CreatedAt over its native timestamp. | Added `recovery.EvidenceAt` or exact reset/reason assertion fails; preserve all fixture timestamps. |
| V-17 / PC-17 | API wall leaves original answer owed: `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | In `HandleApiErrorWithholdAsync`, call `SettleAsync` on the open rows before returning from the unresolved retryable branch. | `ChannelReplySettledAt.ShouldBeNull()` fails. |
| V-18 / PC-18 | Hold includes two-minute padding: `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | In `ApiErrorRecoveryService.ApplyWallAsync`, remove the padding from the parsed-reset deadline expression. | Exact `DisabledUntil` 16:22 UTC check fails, independently of `ResetAtUtc` 16:20. |
| V-19 / PC-19 | Standing-session retry uses the hold deadline: `/*/*/ChannelReplyDurabilityTests/Claude_production_session_limit_stub_withholds_and_adopts_the_AssistantText_reset*` | In `ApplyWallAsync`, replace the taskless SessionLimit `NextAttemptAt` assignment with null, preserving adoption/hold. | `recovery.NextAttemptAt.ShouldBe(hold.DisabledUntil)` fails. |

These mutations mostly touch shared methods; run them sequentially. Do not batch
several mutants under the same quota test, or claim the first failing assertion
proved later behaviors. Each phase executes one result (`MinExecuted=1`) using
its exact table filter. Retain the assertion failure, actual tested SHA and
restored green separately. Mutation estimates: allow 2–4 minutes per baseline,
red or restored phase including an isolated build, approximately 114–228 minutes
for 19 independent cycles; TestDesign should refine from runner measurements and
coordinate already-owned CARD-0519 controls without silently dropping an oracle.

### Execution, evidence and cost

Ordinary scope costs an estimated 13 minutes (7 + 6), including both isolated
builds. Do not add a 15-method preliminary replay and then repeat the same green
methods: the retained base replay already establishes the red starting point.
CP-1/2 must inspect fresh TRX method identities, not only aggregate counts.

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

Lane for **every row**: portable PostgreSQL integration with fake messaging; live
default runner selection, OS-neutral. `Group` names encode that lane. Full named
classes are deliberate: the shared fixture affects all bridge tests, and the card
explicitly requires both complete affected classes. PCs remain single-method.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1064-cp1/` | portable-pg-bridge | `/*/*/ChannelBridgeTests/*` | V-1..14, R-1 | all 40 results including the 14 named bridge methods, 0 failed/skipped | 40 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1064-cp2/` | portable-pg-durability | `/*/*/ChannelReplyDurabilityTests/*` | V-15..19, R-2 | all 25 results including the named session-limit method, 0 failed/skipped | 25 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
