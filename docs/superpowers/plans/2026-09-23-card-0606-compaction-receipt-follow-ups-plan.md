# CARD-0606: compaction receipt follow-ups

Plan and folded verification design, 2026-09-23. Ready for Code under the supplied
brief; no operator decision is outstanding. Ground truth is fetched `origin/master`
and worktree HEAD `bb89e77b4dcc30feb809e1549d62653890a0311a` (identical for these files).
Line numbers below refer to that revision. This is a repair of CARD-0079, not a new
restart policy. No migration, setting, runner protocol or deployment change is needed.

## Ground truth

| Card assumption | Current code | Consequence |
|---|---|---|
| Non-legacy receipt compares the wrong task ID. | `CheckCompactionContinuationService.cs:531-538` compares queue `SourceTaskId` with the **interpretation** ID. `SessionMessageQueueService.CheckPublication.cs:28-46` writes the request's **CheckedTaskId** into that field, with an exact `CallerMessageId` on `SpecialistRequest`. `AgentTaskCheckService.cs:237-240` uses the checked task for ordinary legacy enqueue too. | Changing one equality needs a durable interpretation-to-checked-task join; there is no queue `CheckedTaskId` property. |
| Legacy handling masks the other path. | `LegacyReceiptAsync`, lines 696-710, returns `Handled` if *any* publication exists for this physical seat/session, even if none belongs to the candidate interpretation. | Retained historical publications can permanently veto a later modern receipt. Applicable but invalid legacy evidence must still withhold recovery. |
| Fixing that early return makes modern receipts reachable. | Lines 520-526 resolve the caller from the interpretation or an episode-bound legacy publication. `SpecialistTaskRunner.cs:327` deliberately leaves `ParentSessionId` null; modern `SpecialistRequestService` also creates standalone interpretation tasks. | Modern correlation/caller resolution must happen before the legacy-only parent prerequisite. |
| The stop proof check is around line 365. | `FinishStopAsync:368-369` checks `ConfirmsExit` and accepted generation, but not `AttemptId`. | A previous attempt's success at the same generation authorizes the current stop/resume transition. |
| Existing successful-stop fixtures will remain green unchanged. | Four success factories use `Guid.NewGuid()` for the response attempt: automatic-restart tests, continuation tests, restart handoff tests and the crash worker. | Make those fakes echo the actual request; do not weaken the new guard to accommodate them. |
| `CompactionTranscriptFact.StopReason` has a useful consumer. | It is declared in `CompactionContinuationPolicy.cs:323-333` and projected at service lines 980/1016, but never read. The policy recognizes effective endings by transcript kind/text at lines 129-133. | Remove this local projection/member. Retain `TranscriptEntry.StopReason` and other consumers. Two positional test constructors also pass the unused final null. |

## Decisions

- **D-1: select the receipt through its producer's durable identity.** Keep the
  existing post-stop, succeeded, nonempty Check task plus marked interpreter prompt
  and later TurnEnd requirements. For a modern Check, join `SpecialistAttempt.TaskId`
  to that interpretation and `SpecialistRequest.WinnerAttemptId` to the attempt.
  Require `Purpose=Check` and the attempt's accepted session generation to equal
  `episode.ResumeAcceptedStartedAt`; the interpretation is already selected in
  `ResumeSessionId`. Resolve the subject via `request.CheckedTaskId`, the caller
  via that subject's `ParentSessionId`/session reply target, and the queue row via
  `request.CallerMessageId`. Require that row to belong to the caller, have
  `Origin=Check`, `SourceTaskId=subject.Id`, `Sent`, a nonempty body and a delivery
  baseline. Preserve the complete UserPrompt and strictly-after-baseline proof.
  Keep the existing recovered fields/incident/budget transition; a modern message
  normally has no land-notification ID, so do not invent one.
  Rejected: latest message for the subject (can borrow another Check), parsing
  titles/goals, or manufacturing a parent on interpretation tasks. An unassociated
  old-style note without either durable producer link cannot prove recovery.
- **D-2: legacy ownership is per interpretation, not per seat history.** Pass the
  candidate Check identity to `LegacyReceiptAsync`. A publication for that
  interpretation owns its validation, even when captured, suppressed, stale or
  malformed: it must not fall through to a weaker modern/queue check. Validate its
  existing episode, interpreter session/generation, checked attempt/dispatched
  time/Check number, useful run and original-body receipt fences. Use the matched
  publication's own parent, not another run's parent. Publications belonging to
  other interpretations are `NotApplicable` and do not block a valid modern
  request. Rejected: return `NotApplicable` whenever no *valid Produced* row is
  found (bypasses invalid legacy evidence), or keep the broad historical veto.
  Retain the current newest-useful-Check selection; changing scheduling/selection
  across multiple unfinished Checks is outside this repair.
- **D-3: compare the response attempt before accepting exit proof.** In
  `FinishStopAsync`, require `result.AttemptId == attempt` alongside the existing
  proof/generation checks. A crossed attempt transitions to `NeedsDecision` with
  reason `stop-attempt-mismatch`, preserving the already-spent allowance; no
  stopped-session write, `StopOutcomeAt`, retirement or resume follows. Do not
  automatically retry. Keep failed/lost proof behavior and generation checks.
  Rejected: trusting `ConfirmsExit`, or adding the comparison only to the HTTP
  client (the coordinator owns the state transition and must validate its input).
- **D-4: remove the unused fact member.** Delete the SQL projection, constructor
  argument and `CompactionTranscriptFact.StopReason` member, plus obsolete test
  arguments. A synthetic diagnostic would add output without improving a receipt;
  using the field to change what counts as an ended turn would alter restart policy.

## Implementation slices

Commit and push each slice with its actual verification status; finish all edits
before the shared build. No source edits while a checkpoint is running.

| Slice | Files | Changes and tests |
|---|---|---|
| S1: producer-correlated receipts | `server/Application/Services/CheckCompactionContinuationService.cs`; new `tests/Antiphon.Tests/Application/CheckCompactionReceiptTests.cs` | Implement D-1/D-2 in the existing service, using small private helpers if useful. Add V-1/V-2 and the negative receipt tests below. Reuse `BridgeQueueHarness` with an isolated schema; no new service interface or schema field. |
| S2: attempt-bound stop proof | Same service; `tests/Antiphon.Tests/Application/CheckCompactionAutomaticRestartTests.cs`; `tests/Antiphon.Tests/TestHelpers/FakeSessionRunnerClient.cs`; `tests/Antiphon.Tests/Application/CheckCompactionContinuationTests.cs`; `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.Restart.cs`; `tests/Antiphon.Tests/TestHelpers/CheckCompactionCrashWorker.cs` | Add D-3 and V-3. Add an optional request-aware stop-result callback to the fake, preserving explicitly supplied results unchanged for crossed-response tests. Switch the four success factories to echo `request.AttemptId`; leave the wire fixture's deliberate payload unchanged. |
| S3: remove dead projection | Same service; `server/Application/Services/CompactionContinuationPolicy.cs`; `tests/Antiphon.Tests/Application/CompactionContinuationPolicyTests.cs` | Implement D-4; remove the two trailing null arguments in `Latest_arrival_controls_the_ten_minute_boundary`. R-3 verifies existing behavior; no reflection/source-text test for a deleted field. |

## Verification design

### Inspection and setup

Read the relevant bodies of `CheckCompactionAutomaticRestartTests` (success,
refusal, holds and seed), `CheckCompactionContinuationTests`,
`CheckCompactionCrashTests` (stop/resume crash cuts), `CompactionContinuationPolicyTests`,
`SpecialistPublicationTests`, and `CheckNoteDeliveryHandoffTests` including its
`H5` receipt helper/cases and `Restart` flow. Read the stop-result fake,
`CheckCompactionFixture`, crash-worker success factory, and `BridgeQueueHarness`
queue/adapter/transcript setup. Producer inspection includes `AgentTaskCheckService`,
`SpecialistRequestService`, `SessionMessageQueueService.CheckPublication` and
`LegacyCheckNotePublicationService`.

Missing setup to add in S1: a completed winning specialist request/attempt bound
to a useful Check in the resumed generation, with the interpretation's
`ParentSessionId=null`, and a different checked-task ID. Seed this producer input,
then invoke real `PublishCheckRequestAsync` and the real queue. Do not manually
seed the successful caller queue row or its receipt. Fake adapter submission
inserts the exact submitted UserPrompt, as the existing harness does. Negative
correlation cases may seed/alter durable evidence independently to isolate a guard.
Use fresh contexts when recreating the coordinator and asserting committed state.

### Delivery inventory

| Path | Producer, destination, durable identity and persistence | Recovery and observable receipt |
|---|---|---|
| Modern Check | Completed winning request -> `PublishCheckRequestAsync` -> checked task's caller. Interpretation -> winning attempt -> request -> checked task and exact `CallerMessageId`. Publication timestamp, queue row and Check event commit together. | Queue flush handles busy/eligible callers and interrupted delivery. Recreated coordinator observes the original row and full caller UserPrompt after its baseline, then commits `Recovered` and budget eligibility together. Test busy and idle, failed queue insertion, committed queue before flush, swallowed submit/retry, and receipt before coordinator recreation. |
| Legacy Check | Captured publication -> existing notification reconciler -> caller; publication retains interpretation, episode, checked attempt/number, notification and original body. | Existing `CheckNoteDeliveryHandoffTests.H5.cs` cases cover worker-death recovery and identity/body exclusions. Keep their original-body semantics; S1 adds historical-publication noninterference and applicable-invalid-publication exclusion. |

Substitutes: seeded successful interpretation/request replaces model execution
and qualification; the fake adapter replaces terminal transport. They prove the
real database/queue/receipt consumer, not native Claude behavior or actual process
exit. The existing automatic restart handoff remains the full coordinator-to-note
regression. No new handoff or delivery producer is introduced.

### Proves it works now

- **V-1** (`CheckCompactionReceiptTests.Modern_receipt_recovers_without_interpretation_parent`,
  arguments `idle`, `busy`): assert distinct subject/run IDs, null run parent,
  exact request message, queue `SourceTaskId=subject.Id`; no recovery before receipt.
  After real submission/flush, assert one complete matching caller prompt after
  the floor, `Recovered`, `UsefulCheckTaskId=run.Id`, exact confirming sequence,
  cleared active recovery, and eligible restart budget. Repeat sweep: no duplicate
  incident or publication. Use a unique short body that cannot spill to a file.
- **V-2** (`Modern_receipt_survives_publication_and_delivery_recreation`, arguments
  `insert-failure`, `queued-before-flush`, `submit-retry`, `receipt-before-sweep`):
  use the `SpecialistPublicationTests.FailPublication` interceptor pattern, busy
  caller and fresh queue/coordinator instances to exercise the four cuts. Rollback
  leaves no message/event/publication timestamp; retry retains the same message
  identity. Every arm finishes with V-1's whole-prompt/recovery assertions. These
  are exception/service-recreation tests, not claims of OS worker-death coverage.
- **V-3** (`CheckCompactionAutomaticRestartTests.Crossed_stop_attempt_cannot_confirm_exit`):
  valid current generation and `ConfirmsExit=true`, but response carries a prior
  attempt ID. Persist current `StopRequested` attempt first and sweep with a fresh
  service. Assert one RPC, `NeedsDecision`, mismatch reason, null `StopOutcomeAt`,
  unchanged running session/termination fields, spent allowance, zero resume and
  zero generic kill. Existing `One_stop_is_reconciled_without_a_second_launch`
  is the matched-attempt positive case. Add
  `Crossed_stop_generation_cannot_confirm_exit` with matching attempt but changed
  generation to ensure the new fixture seam does not conceal the old fence.

### Guards the regression

- **R-1:** receipt tests below, plus all `CheckNoteDeliveryHandoffTests` and
  `SpecialistPublicationTests`. In every negative new case, keep all unrelated
  evidence valid and assert `AwaitingCheck`, budget ineligible and no confirming
  sequence; restoring the single invalid field/evidence must allow recovery.
- **R-2:** all `CheckCompactionAutomaticRestartTests`,
  `CheckCompactionContinuationTests`, `CompactionContinuationWireTests` and
  `CheckCompactionCrashTests`; covers request-aware fake compatibility, positive
  restart, hold vetoes and durable stop/resume cuts.
- **R-3:** Unit lane includes `CompactionContinuationPolicyTests`, especially
  `Turn_end_after_boundary_disqualifies_it` and arrival/threshold cases; the
  isolated build checks all positional fact constructors. Code review/search
  confirms only the local fact projection was removed.

### Guard inventory and positive controls

The following are this repair's changed/re-exposed receipt guards and stop-proof
fences. Existing legacy validation and discovery/hold/budget guards remain owned
by CARD-0079 and its named regression classes; this card does not recommission that
entire PC matrix. `C` below means `CheckCompactionReceiptTests`, `A` means
`CheckCompactionAutomaticRestartTests`. Each row maps one guard to one PC.

| Guard / PC | Exact test method | Compiling mutation and required red assertion |
|---|---|---|
| G-1 / PC-1: checked-task correlation | `C.Modern_receipt_recovers_without_interpretation_parent` | Compare queue source with interpretation ID again; `Recovered` assertion fails. Both idle/busy expansions must execute. |
| G-2 / PC-2: legacy applicability | `C.Historical_legacy_publication_does_not_block_modern_receipt` | Restore seat/session-wide `Handled`; valid modern receipt remains stuck instead of `Recovered`. Seed an older publication for another episode/run in the same seat/session. |
| G-3 / PC-3: invalid applicable legacy evidence cannot fall through | `C.Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt` | Treat an invalid associated publication as `NotApplicable`; modern proof for that same run improperly recovers. Exercise suppressed and wrong-episode publication variants. |
| G-4 / PC-4: winning attempt | `C.Nonwinning_interpretation_cannot_claim_request_receipt` | Join any attempt for a request instead of its winner; `AwaitingCheck` fails for a newer succeeded nonwinning run with its own prompt/end. |
| G-5 / PC-5: resumed generation | `C.Prior_generation_request_cannot_recover_current_episode` | Remove attempt/resumed generation equality; `AwaitingCheck` fails despite identical session ID and otherwise valid receipt. |
| G-6 / PC-6: exact caller message | `C.Another_message_for_the_checked_task_is_not_the_receipt` | Replace `CallerMessageId` selection with latest same-subject Check row; delivered decoy recovers while real request message has no receipt. Assert `AwaitingCheck`. |
| G-7 / PC-7: complete caller prompt | `C.Partial_caller_prompt_does_not_recover` | Replace whole-body matching with a nonempty/prefix match; partial receipt wrongly sets `Recovered`. |
| G-8 / PC-8: receipt follows attempt floor | `C.Caller_prompt_at_or_before_baseline_does_not_recover` | Remove the sequence floor; at-floor and older-prompt variants wrongly set `Recovered`. |
| G-9 / PC-9: stop attempt | `A.Crossed_stop_attempt_cannot_confirm_exit` | Remove only attempt equality; `NeedsDecision`/zero-resume assertions fail. This supplies CARD-0079's formerly methodless PC-70 attempt case. |
| G-10 / PC-10: stop generation | `A.Crossed_stop_generation_cannot_confirm_exit` | Remove only accepted-generation equality; `NeedsDecision`/zero-resume assertions fail. |
| G-11 / PC-11: Check request purpose | `C.Modern_receipt_requires_complete_evidence` | Remove the purpose predicate; the `wrong-purpose` variant wrongly recovers. |
| G-12 / PC-12: Check queue origin | `C.Modern_receipt_requires_complete_evidence` | Remove the origin predicate; the `wrong-origin` variant wrongly recovers. |
| G-13 / PC-13: caller ownership | `C.Modern_receipt_requires_complete_evidence` | Remove the queue/caller session equality; the `wrong-caller` variant (identical full prompt also present in the intended caller) wrongly recovers. |

Guards=13, mapped=13, missing=0, duplicate mappings=0. New receipt tests also
exercise absent publication/message/baseline, wrong purpose/caller/source/origin,
empty result, missing interpreter prompt/end and Sent-without-UserPrompt as a
table-driven `Modern_receipt_requires_complete_evidence` test; each independently
leaves the episode open. No new guard is inferred from deleting `StopReason`.

Code implements tests and runs ordinary V/R. Separate Review precedes land;
Mutation executes PC-1 through PC-13 after confirmed land in a commissioned
SourceLanding task. Each red/restore/green uses only
`/*/*/<Class>/<ExactMethod>` (all named argument expansions), never a class/suite.
These mutations touch the same service and run serially. Require the named
assertion failure; zero tests or fixture/build errors are not red.

### Out of scope

No real-provider canary, UI/E2E, runner/Pty suite, production-stack restart or new
process-stop authorization. No change to the original-body rule for legacy
spilled notes. No database schema or caller-notification protocol change.

### Cost

Estimates, not measurements: ordinary Code floor is **15 minutes** (CP sum), plus
about **35 minutes** authoring. Post-land Mutation floor is **45 minutes**: about
3 minutes initial build/setup plus 13 serial method-scoped red/restore/green
cycles at roughly 3.2 minutes each (rounded). Reusing CP-1 output avoids two redundant
builds (estimated 4 minutes). No full assembly run: the affected classes are
bounded; Unit plus named integration/native-worker coverage is the ordinary scope.

### Checkpoints

Run once in order after S1-S3 commits, using `scripts/run-checkpoint.ps1`, fresh
`.antiphon/c606-checkpoints` result directories and a producer-owned output
inventory. CP-2/3 use `-NoBuild`; nothing changes between them. Pass all listed
class names to `-Expect` and verify actual expanded counts. Report each CP line,
SHA, failures and reruns. Run duration-tripwire on fresh TRX; remove only inventoried
`bin-c606/` outputs after resolving/checking paths within this worktree. Any
additional build/test run needs a stated reason; inherited red is checked at base
using only the failing methods, never hidden by retries or weakened assertions.

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c606/` | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed; includes `CompactionContinuationPolicyTests` | 5 |
| CP-2 | S1-S3 | CP-1 | receipt-and-stop | `/*/*/(CheckCompactionReceiptTests*)\|(CheckCompactionAutomaticRestartTests*)\|(CheckCompactionContinuationTests*)\|(CheckNoteDeliveryHandoffTests*)\|(SpecialistPublicationTests*)\|(CompactionContinuationWireTests*)/*` | V-1, V-2, V-3, R-1, R-2 | all listed classes and new methods/variants, 0 failed | 7 |
| CP-3 | S1-S3 | CP-1 | stop-crash-regression | `/*/*/CheckCompactionCrashTests/*` | R-2 | all class methods, 0 failed | 3 |
