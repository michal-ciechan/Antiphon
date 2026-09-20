# CARD-0079 amendment: durable legacy Check-note publication

Plan task `0c6a0491`, 2026-09-20. Base inspected and confirmed by `git log -1`:
`01bde59cc739c527c802a8730f8f1a5df2d1a4af`, TestDesign task `cf897be0`.
The requested fetch succeeded; checkout of `feat/card-task-cf897be0` was refused
because `C:/Antiphon/worktrees/card-task-cf897be0` owns it. This amendment uses
`feat/card-task-0c6a0491` starting at that exact SHA, without changing the other
worktree or its branch.

This closes the **design** gap H-5/G-161/PC-161 in the
[automatic compaction recovery plan](2026-09-20-card-0079-stuck-working-compaction-plan.md#verification-design).
It supplies the producer identity, immutable payload, transaction, retry worker,
queue key and receipt join missing there. TestDesign must review this seam and
update the executable controls and cost before Code. No runtime implementation,
test execution, live recovery or deployment is claimed.

## Scope and authority

The parent plan's D-1 through D-10, episode E, accepted generations G/G2,
ten-minute explicit auto-CompactBoundary predicate, one attempt per episode,
24-hour allowance, strict same-conversation resume, and all generation/intent
vetoes remain authoritative. The new publication record never authorizes a stop,
resume, Fresh start, Check schedule tick or model substitution.

Apply the new producer path only to **legacy validation Checks associated with
an existing AwaitingCheck episode on its eligible AlwaysOn Claude Check seat**.
Bind E and its accepted G2 when admitting that newly requested Check; do not
infer them later from an agent's latest session or a matching slug. Both the
logical owner and physical seat must satisfy the existing scope. Routed H-4
keeps its request graph. Ordinary legacy Checks outside this association keep
their existing behavior. H-6 still owns retirement/failure notifications.

An already committed caller obligation remains recoverable if the interpreter
generation changes, the subject settles, discovery is disabled or an operator
supersedes E. That is delivery of existing work, not authorization for new work.
Such a note cannot validate a different episode or replacement generation.

## Ground truth

Paths are relative to the repository at the inspected base; line numbers identify
the relevant existing bodies, not future implementation locations.

| Card assumption / proposed reuse | What code actually does | Required consequence |
|---|---|---|
| A legacy Check already has a publication identity. | `AgentTaskCheckService.RunCheckAsync` (`server/Application/Services/AgentTaskCheckService.cs:133`) calls unkeyed `EnqueueAsync` at :202 and only then saves a new Check event at :226. `check:{TaskId}` groups observations; it does not identify one Check number. | Introduce a persisted publication identity before any queue effect. An event or a later Check cannot substitute for the lost note. |
| Reading the current CheckCount after restart identifies the original observation. | `AgentTaskDispatcher.ClaimCheckAsync` (:3226) advances CheckCount/NextCheckAt before the in-memory queue; `ArmFirstCheck` resets CheckCount for a dispatch. `DelegateCheckProbe.GatherAsync` snapshots the current count. | Freeze the subject execution identity and claimed number at capture. Never reconstruct them from the latest task row during recovery. |
| The interpreter identity is available to the note producer. | `SpecialistRun.RunTaskId` identifies the real Check task, but private `Interpretation` (:398) retains only routed RequestId; `InterpretAsync` (:455) discards the legacy run ID. | Preserve and durably bind the exact run task and its accepted seat generation, rather than parsing the display text. |
| The existing outbox can deliver a Check unchanged. | `AgentTaskLandNotificationService.ReconcileAsync` owns immutable Body/digest, keyed adoption, retry and whole-prompt receipt, but presently chooses Delegation origin and land/task conversation keys (:122-126). | Append an explicit LegacyCheckNote kind and a narrow origin/key branch. It is not a task completion and must not stamp CompletionNoteQueuedAt. |
| Queue insertion can safely be retried. | `SessionMessageQueueService.EnqueueAsync` (:351-461) adopts by SourceLandNotificationId and handles a concurrent unique-index winner. `AppDbContext` (:1636) enforces that key in PostgreSQL. | Use the same notification ID for immediate/recovered enqueue, including lost insert acknowledgments. Preserve attempted/parked rows. |
| A Check Body remains immutable after enqueue. | The Check supersession loop (:1667-1694) can cancel a note or prepend a freshly composed banner; delivery can spill an oversized body to a pointer (:1806 onward). | Decide suppression/banner before publication. Exempt only the new keyed kind from automatic supersession rewriting/cancellation. Retain the existing immutable-outbox receipt rule for spills. |
| Re-running RunCheckAsync is recovery of a produced note. | It can gather different facts and start another interpretation. The existing `A_note_whose_queue_row_cannot_be_persisted_fails_cleanly_and_the_retry_delivers` test does that. | A recovered produced note reads only its committed payload and uses no probe, interpreter or next Check number. |
| A new hosted delivery loop is needed. | `AgentTaskLandNotificationHostedService` scans unresolved notifications on boot and every five seconds, independent of CheckEnabled. `CompletionNoteFlushQueue` supplies the producer wakeup. | Reuse this scanner/flush lane. Add bounded capture-finalization recovery ahead of its notification scan; no second delivery engine. |
| Retention already protects all new evidence. | DataRetentionService protects notification subject/recipient and unresolved queue/transcript evidence, but knows nothing of a publication's interpreter task or E/G2 joins. | Extend retention for those additional links and keep a publication identity tombstone so pruning cannot recreate an old note. |

## Decisions

These are implementation decisions within the supplied authorization, not
unanswered operator choices or a change to the restart defaults.

- **D-11 — A separate publication record, existing transport outbox.** Add
  `LegacyCheckNotePublication` for the captured observation and interpretation
  linkage; use `AgentTaskLandNotification` for delivery. Rejected: force legacy
  calls into SpecialistRequest routing, use the Check event's truncated detail
  as a replay body, or add an independent queue/receipt service.
- **D-12 — Capture once, produce by durable commit.** Persist the observation
  before starting its interpretation; persist the final body, Check event and
  notification in one transaction before enqueue. A renderer returning a string
  inside that transaction is not an accepted publication. Before commit, retry
  the same captured operation; after commit, the body is authoritative forever.
  Rejected: queue-first persistence, recomposition from live facts after death,
  and a new interpretation or refreshed wait budget as a publication retry.
- **D-13 — Database uniqueness owns identity.** Allocate IDs once in the capture
  transaction and use unique constraints plus row locks for concurrent workers.
  In-process locks alone do not cover restart or another server worker. A retry
  with different binding/body under an existing identity refuses; it does not
  overwrite the winner or mint another identity.
- **D-14 — Suppression is decided before production.** Preserve the existing
  producer supersession/completion-note grace behavior, freezing its banner and
  suppress decision. Once a nonsuppressed note is Produced, later subject
  settlement cannot discard or rewrite it. This narrow exception is necessary
  to deliver the original observation; the captured timestamp already identifies
  its age. Explicit user cancellation remains possible and never counts as
  receipt. Rejected: calling a canceled/suppressed note recovered, or changing
  ordinary/routed Check supersession behavior.
- **D-15 — Delivery and seat recovery have separate verdicts.** The outbox can
  confirm an old obligation after an interpreter restart, but only the exact
  E/G2/new Check proof can release E's receipt gate. Suppressed, degraded,
  mismatched or pointer-only notes cannot prove useful recovery. Rejected:
  accepting Sent, a notification flag alone, any caller's prompt, or an unrelated
  later Check as the receipt of this publication.

## Durable identity and payload

Add `server/Domain/Entities/LegacyCheckNotePublication.cs` with these persisted
fields (names below are the implementation contract):

| Group | Fields / constraints |
|---|---|
| Identity | `Id` (publication P), `CheckedTaskId`, `CheckedTaskAttempt`, `CheckedTaskDispatchedAt`, `CheckNumber`. Unique `(CheckedTaskId, CheckedTaskAttempt, CheckedTaskDispatchedAt, CheckNumber)`. DispatchedAt is the captured database value, normalized with the existing generation precision rules; all key members are nonnull. |
| Recovery binding | `RecoveryId`, `PhysicalAgentId`, `InterpreterSessionId`, `InterpreterAcceptedStartedAt` (= E's reserved/accepted G2), `ParentSessionId`. Frozen at capture. A replay cannot attach the same logical Check to a newer E. |
| Observation | `CapturedAt`, versioned `FactsSnapshotJson`, and `RenderContextJson` containing the subject/header/reply-style inputs used by BuildNote. The captured facts' number must equal CheckNumber. Store bounded existing probe data, not new transcript dumps. |
| Interpretation | nullable unique `InterpretationTaskId`, original `InterpretationDeadlineAt`, and nullable `InterpretationSnapshotJson` (selected outcome, exact reading/degraded reason, cost/event line and useful-result proof references). Record the deadline at capture, immediately before the legacy interpreter call: current time plus the existing CheckInterpreterWaitSeconds budget. There is no new timeout setting or renewed wait after restart. |
| Publication | `State` = Captured / Produced / Suppressed, preallocated unique `SourceEventId` and `NotificationId`, nullable `ProducedAt`, immutable canonical `Body`, `ContentDigest`, `EventDetail`, suppression reason/time. Body is LF-normalized and trimmed once; digest is SHA-256 of its UTF-8 bytes. |
| Recovery bookkeeping | `NextAttemptAt`, retry/error metadata and `ConcurrencyToken`. Mutable bookkeeping must never modify identity, snapshots or Produced payload. |

The extra execution fields prevent a re-dispatch that resets CheckCount from
colliding with an earlier observation. Capture uses a coherent task snapshot
under its row lock and rejects a changed execution/count before acceptance;
an ambiguous/missing dispatch identity cannot be a recovery-validation Check.
Lookup an existing P **before** gathering again or starting an interpreter.
After acceptance, recovery is by P, never by whatever CheckCount is now.

Use Restrict links for the source task/event, interpretation task and E; destination
remains an immutable session snapshot as in the existing outbox. Add mappings,
indexes and state constraints in AppDbContext and a CLI-generated migration.
Produced requires a complete body/digest/event/notification; Suppressed has no
delivery obligation and cannot satisfy E. Persist the publication identity for
at least the lifetime of its subject execution and recovery audit. Ordinary
pruning must never remove a still-retryable identity and permit a second mint.

Append `LandNotificationKind.LegacyCheckNote` in `LandingEnums.cs`; do not reorder
values. Its notification has `IsLegacy=false` (that flag means historical
unverified evidence), `TaskId=CheckedTaskId`, `SourceEventId=P.SourceEventId`,
`ReplyTo=Session`, the frozen parent, `Body=P.Body`, `ContentDigest=P.ContentDigest`,
and `State=Queued`. RequestId, LandingOperationId and Completion snapshot/rendering
fields remain null. SourceEventId's existing unique index prevents a second outbox
row for the Check event. P.NotificationId joins it without encoding identity in
user-facing prose.

## Production and recovery protocol

Add concrete scoped `LegacyCheckNotePublicationService` in Application/Services.
The named methods below are proposed production seams, not existing APIs.

1. **CaptureAsync:** after legacy routing/seat selection but before its interpreter
   task is runnable, freeze the subject execution, claimed number, facts, render
   context, E/G2 and parent in P. A duplicate capture adopts the existing row after
   verifying identity; it neither probes again nor overwrites snapshots. This
   operation is reachable only through the scope above, not through a new API.
2. **Bind the one interpretation:** extend SpecialistTaskRunner's internal legacy
   Check entry with optional publication P. Under a P row lock, create the Check
   task and Created event and write P.InterpretationTaskId in the **same
   database transaction**. A duplicate adopts that run; no linked task exists
   without P and no committed linked P has an unrecorded runnable task. Keep
   Diagnose/Distill and ordinary legacy callers unchanged. Final standing dispatch
   still checks E/G2. Never replay/rebind an old generation's Check into G2.
3. **Complete the captured observation:** preserve RunTaskId in Interpretation.
   A recovering Captured P may finish waiting on its linked run using the original
   deadline, read its durable result, or create the originally reserved run once
   if none was committed and the same E/G2 admission still holds. It does not call
   RunCheckAsync, advance CheckCount, re-probe, extend the deadline or start a
   replacement interpretation. P's captured deadline caps admission even if no
   run was committed: an expired capture finalizes the timeout digest and creates
   no task. If the generation/intent is no longer eligible,
   freeze the existing degraded outcome with no new task. A selected timeout or
   degradation is frozen; a late successful run cannot replace it.
4. **ProduceAsync(P):** retain the chosen interpretation snapshot, evaluate the
   existing producer suppression/grace policy, and render from P's captured
   context. Under the P lock, commit Produced + complete immutable Body/digest +
   Check event + LegacyCheckNote notification atomically. For suppression, commit
   Suppressed + the Check event/reason and no notification. A retry of Produced
   returns its stored notification/body before consulting mutable task state or
   running the renderer. Persist any selected interpretation outcome before a
   retryable rendering/publication operation so recovery cannot upgrade it.
   Select the interpretation snapshot once under the same P lock: competing
   finalizers adopt that snapshot before rendering. Perform any grace wait
   outside the transaction, then freeze the suppression decision at production.
5. **After commit only:** call existing notification ReconcileAsync(P.NotificationId).
   For LegacyCheckNote it enqueues `Origin=Check`, `WhenIdle`,
   `ConversationKey=check:{CheckedTaskId:N}`, `SourceTaskId=CheckedTaskId`, immutable
   digest/body, and `SourceLandNotificationId=P.NotificationId`. Use the current
   queue insertion/adoption and durable outbox bookkeeping, then the existing
   CompletionNoteFlushQueue wakeup. Ensure recovered adoption of an already
   inserted Pending row also schedules that wakeup when QueueAttention says it
   is actionable; the current new-insert-only wakeup is insufficient after a
   lost acknowledgment. Do not wake parked rows merely because the scanner saw
   them. Do not label the advisory note TaskCompletion
   or update the completion-note stamp. The return outcome describes queued or
   pending transport, never a proven receipt.
6. **Boot/periodic recovery:** extend AgentTaskLandNotificationHostedService with
   a paged Captured-P pass (`RecoverCapturedAsync`) before its existing notification
   scan, with fresh scopes and per-row error isolation. Captured recovery finalizes
   only the original operation; Produced rows are delivered solely by the existing
   notification scan, not by another Check schedule tick or the episode coordinator.
   Both scans run with optional Check/discovery disabled. Disabling may prevent
   creating an uncommitted interpreter run, but cannot discard captured work or
   an already Produced caller obligation. The coordinator only consumes receipt
   evidence; it must not supply a second notification delivery path.

No database transaction spans interpreter waiting, queue locking, process I/O or
flush. The atomic run-link and publication transactions commit before these
effects. Preserve the current EF cleanup of abandoned run/event entities after
failed insertion. Cross-process unique-key races reload committed winners in a
fresh scope; an uncommitted transaction is not a visible reservation.

For this kind only, the queue supersession loop must skip automatic cancellation
and banner insertion. Check origin already excludes batching; preserve that.
Keep its sourced/conversation identity so the parked-message sweeper cannot
discard it as anonymous machine input. Existing manual cancellation, attempt caps,
composer-generation fences and late-confirm-before-retry rules still apply.
Oversize or changed wire/pointer rows remain unconfirmed against the immutable
outbox Body; do not weaken the parent's whole-body acceptance rule or silently
replace a previously attempted/parked row. Normal acceptance uses a body within
the supported inline envelope. A missing authoritative queue row or unavailable
parent retains a visible unresolved obligation rather than minting a new row or
redirecting it to the parent's current replacement session.

## Crash cuts and receipt join

| Cut | Durable recovery and required result |
|---|---|
| Before capture commits | No accepted P, linked interpretation or note. Retry that scheduled observation only if still admissible. This is not a produced-note delivery success. |
| Capture committed; before run-link commit / acknowledgment | Recover P and atomically create or adopt its one run within the original deadline. A rolled-back run/event is not left runnable. Captured deadline and E/G2 remain fixed. |
| Result available; before Produce commit | Recover the same captured facts and linked result/selected outcome; complete the one production transaction. No partially published Check event/outbox. Do not call a later Check a retry. |
| Produced commit; before first enqueue (PC-161) | P, Check event and notification survive. Boot/periodic notification scan enqueues the original Body using its existing notification ID. Zero new probe/interpreter/Check-count activity. |
| Queue insert fails | Notification stays retryable. Later reconciliation uses the same P/notification/body. Busy parent remains untyped; eligible parent receives through the normal wakeup. |
| Queue committed; acknowledgment or outbox-link save lost | Find the unique SourceLandNotificationId row, adopt its ID/attempt history, then flush through the existing lane. Never add another queue row. |
| Attempt committed / prompt accepted; receipt save lost | Existing queue late-confirm/recovery observes the complete original parent UserPrompt above its attempt floor before considering another submit. Reconcile the same notification and record that sequence. |
| Interpreter automatically restarts or E is superseded between production and enqueue | Deliver the already Produced original note to its frozen parent. Do not regenerate it from the restarted seat, replay a Check brief or transfer recovery credit to E2/G3. D-9 still prohibits a second automatic restart while the first lacks receipt. |
| Subject settles or settings are disabled after production | Original obligation remains discoverable and immutable. Suppression chosen before production remains Suppressed; it never becomes a receipt. No new restart authority is created. |

The recovery proof is the persisted chain
`E -> accepted G2 -> new InterpretationTaskId -> complete interpreter brief
UserPrompt -> useful correlated report/TurnEnd -> P -> NotificationId ->
SourceLandNotificationId queue row -> complete parent UserPrompt after attempt floor`.
Store the queue ID, parent session and confirming sequence/native identity with
E's proof. Validate P's frozen E/seat/G2/subject/check identity, useful interpretation
snapshot and actual transcript evidence; a Confirmed flag alone is insufficient.
Unconfirmed Sent, old identical text below the floor, a partial/header-only prompt,
wrong parent, suppression, degradation and receipt from another P cannot release
E's D-9 gate. Unresolved P and its interpreter/parent evidence survive retention;
confirmation may allow ordinary payload retention only after the durable recovery
proof and identity tombstone remain sufficient to prevent replay or gate reset.

## Implementation slices

These extend parent S4/S6; they do not change its stop/resume slices. Paths under
Application/Services and tests/Antiphon.Tests/Application are explicit below.

| Slice | Files | Tests / acceptance to finalize in TestDesign |
|---|---|---|
| H5-1 capture and atomic production | New `server/Domain/Entities/LegacyCheckNotePublication.cs`; `server/Domain/Enums/LandingEnums.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*` and snapshot; new `server/Application/Services/LegacyCheckNotePublicationService.cs`; `server/Application/Services/AgentTaskCheckService.cs`; `server/Application/Services/SpecialistTaskRunner.cs`; `server/Program.cs` DI. | New `tests/Antiphon.Tests/Application/LegacyCheckNotePublicationTests.cs`: unique capture/run binding, re-dispatch/count reset, snapshot immutability, atomic production rollback, duplicate producer conflict, suppression, unchanged ordinary/routed paths. |
| H5-2 recovery and queue semantics | `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`; `server/Application/Services/AgentTaskLandNotificationService.cs`; `server/Application/Services/SessionMessageQueueService.cs`; `server/Application/Services/DataRetentionService.cs`; the parent plan's new `CheckCompactionContinuationService.cs`; `docs/session-runtime-invariants.md` narrow publication/supersession contract. | Extend `AgentTaskLandNotificationRecoveryTests.cs`, `AgentTaskLandNotificationPersistenceTests.cs`, `SessionMessageQueueServiceTests.cs`, `DataRetentionServiceTests.cs`; named new publication tests below. Prove no completion stamp, fixed Check body after subject settlement, no new row after lost ack, disabled discovery still recovers, preserved interpreter evidence and original receipt joins. |
| H5-3 original-note acceptance | `tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.cs`; new parent-plan `tests/Antiphon.Tests/Application/CheckCompactionCrashTests.cs`, `tests/Antiphon.Tests/TestHelpers/CheckCompactionFixture.cs`, `CheckCompactionCrashWorker.cs`; parent-plan `CheckCompactionBoundary` instrumentation at the real transactions. | Busy/eligible callers, actual automatic G-to-G2 flow, real producer/result settlement, exact worker-death cuts, whole original note and exact IDs. Keep `ReceiptFailureDeliveryTests.cs` and existing handoff cases as regression coverage; they do not replace H5 original-note recovery. |

## TestDesign handoff: H-5 / G-161 / PC-161

This is input to the separate TestDesign stage. The parent's Verification design
and 169-row inventory remain the prior review record; no new runtime counts,
executable-control total, timing measurement or Code-entry approval is asserted.

**H-5:** legacy AgentTaskCheckService -> captured P/run -> ProduceAsync transaction
-> AgentTaskLandNotificationService -> real Check-origin parent queue -> original
parent transcript. **G-161:** every accepted Produced legacy recovery note has a
durable immutable publication and an independently discoverable pre-enqueue outbox;
worker death cannot lose it or cause a second interpretation/publication.

**Concrete PC-161 seam:** retain
`CheckNoteDeliveryHandoffTests.Legacy_produced_note_survives_pre_enqueue_worker_death`.
Use the parent plan's owned child-worker fixture, real database, real notification
hosted service and isolated runner. Pause at `legacy-note-produced`, **after the
ProduceAsync transaction commits and before any enqueue/wakeup**, using the
preallocated P/notification IDs in the rendezvous. Arm the existing planned
`before-note-enqueue` boundary for this notification on **every** reconciliation
entry (producer and scanner) before producing it. It holds concurrent discovery
before queue insertion too; a post-commit producer pause alone would race the
scanner. The replacement worker does not inherit this test rendezvous hold.
Verify the committed body/event/
outbox and absent queue row from an independent context. Kill and await that worker,
drain its output, then boot a new worker without the old in-memory producer.
No direct ReconcileAsync/FlushSessionAsync call from the test is a rescue path.

The proposed compiling defect is to exclude `LandNotificationKind.LegacyCheckNote`
from the unresolved-notification query in
`AgentTaskLandNotificationHostedService.ExecuteAsync`. Captured-P recovery must
exclude Produced rows, so another loop cannot mask this defect. Everything needed
for the post-death enqueue is then committed but undiscovered: the decisive
assertion is `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1)` and must
fail with zero, not with a build/fixture/precondition error. This replaces the
prior hypothetical "omit its intent commit" mutation with a named production
query and a reachable persistence cut. TestDesign may split independent guards
into additional numbered controls; it must not mark this control executed.

Run the method with busy and already-eligible parent variants. The busy variant
asserts zero input while Working, then uses the actual parent turn-end; the
eligible variant depends on the recovered producer's flush wakeup. Change live
probe facts/clock/subject display state after the kill; both still receive the
exact original Body once, with the original P/notification/queue join, and no
increase in CheckCount, probe calls or interpretation-task count. Leave the
subject open for PC-161 itself so supersession cannot hide a discovery defect.

Additional independent methods for TestDesign to map and cost:

- `Legacy_capture_and_interpretation_link_commit_together`: run-link save failure
  and acknowledgment loss produce zero orphan tasks and at most one original run.
- `Legacy_note_body_event_and_obligation_commit_together`: fault event/outbox
  insertion independently; no partial Produced state, then same-P retry succeeds.
- `Legacy_production_retries_keep_the_first_body`: change live facts and renderer
  inputs after production, recreate services, assert exact stored bytes/digest.
- `Legacy_note_duplicate_workers_share_one_queue_row`: concurrent enqueue and
  lost acknowledgment; one keyed row, unchanged attempt history, one receipt.
- `Legacy_produced_note_survives_subject_settlement`: settle after production;
  original body remains deliverable without a new banner or automatic cancel.
- `Legacy_suppressed_note_never_recovers_the_seat`: suppression before production
  saves its event, zero outbox/queue, E remains AwaitingCheck.
- `Legacy_note_recovery_runs_with_checks_disabled`: the boot/periodic scan ships
  Produced work with no interpreter launch or restart despite disabled discovery.
- `Legacy_note_receipt_cannot_validate_another_generation`: deliver an old P after
  E supersession/G3; its own delivery can confirm but E2's receipt gate stays shut.
- `Legacy_note_retention_preserves_original_receipt_evidence`: retain P, linked
  interpreter and unresolved caller evidence alongside an unrelated prune control.

Use the parent V-5/V-6 real producer/recipient and method-scoped PC rules. Reuse
its original-message attempt/receipt cut matrix and negative receipt cases.
TestDesign must add the new class to ordinary filters, ensure the child fixture
registers the actual scanner and flush worker, and revise counts/cost. The
existing 415.44-minute estimate is historical, not an estimate for this amendment.

## Validation and next stage

Plan-only validation: source/owner inspection, local link and named-file checks,
and `git diff --check`. Runtime tests/builds: **0**. No daemon, runner or live
session was changed. Commit and push this artifact and its parent-plan references.

Next: **test-design**. Review this concrete original-note seam, amend the parent
Verification design's H-5/G-161/PC-161 mapping and additional controls/cost, then
decide whether Code's executable-verification gate is satisfied. No further
operator restart authorization is requested.


## Verification design

TestDesign task `efe21527`, 2026-09-20, inspected exact HEAD
`0d09e445f73dd372bf4a57470ca62cd2019dd3d3` (confirmed with `git log -1`).
Fetch succeeded; Git refused the requested branch checkout because
`C:/Antiphon/worktrees/card-task-0c6a0491` owns that branch. This amendment
uses the clean task branch `feat/card-task-efe21527` at that exact SHA.

**Design verdict: ready for Code.** PC-161 now has an executable construction
through committed publication, worker death, independent scanner discovery and
the original recipient transcript. This section supersedes only the parent's
historical H-5/G-161/PC-161 review, affected filters and cost. D-1 through D-15,
AlwaysOn Check scope, explicit auto-CompactBoundary trigger and generation fences
are unchanged. Tests and production seams named as new below are implementation
requirements, not claims of existing or executed methods. Runtime builds/tests
performed by this TestDesign: **0**.

### Inspection

Bodies actually read in this dispatch are recorded below. Paths abbreviated to
a filename in the first six rows are under `tests/Antiphon.Tests/Application/`.

| Test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `CheckNoteDeliveryHandoffTests.cs`, all seven tests, Handoff.CreateAsync/EnsureInterpreterAsync/DisposeAsync, seeding, FailFirstInsert, StartCheckAndDeliverTheBriefAsync and both whole-body assertions | Producer/brief/note, busy/eligible and failed insert -> V-8/V-10, R-8. Existing AlwaysOn=false, direct result settlement, direct rescue flush and pointer-file acceptance cannot prove this amendment. |
| `AgentTaskLandNotificationRecoveryTests.cs`, complete file including C467_V09, C467_V10, destination/digest mismatch and FailedInsert | Cross-process uniqueness, boot scan paging, due retry and adopted identity -> V-9, R-9. C467_V10 ends at queue insertion and starts no flush worker. |
| `AgentTaskLandNotificationPersistenceTests.cs`, complete file including C467_V17 migration and atomicity tests | Real PostgreSQL migration, independent observer, insertion/commit cuts -> V-8, R-8. Land-only outcomes remain regression coverage, not legacy publication acceptance. |
| `ReceiptFailureDeliveryTests.cs`, complete file including both verification helpers, CreateProvider, OffsetClock and both fault interceptors | Queue/attempt/prompt/receipt cuts and disabled reminders -> V-10, R-9/R-10. Service recreation is a substitute for ordinary cut coverage, not worker death. |
| `SessionMessageQueueServiceTests.cs`, first seven methods (send shape, idle gate, turn-end), CreateHarnessAsync, transcript helpers and disposal | LF/bracketed paste/separate Enter and idle eligibility -> V-9/V-10, R-9. Shared-store helper must not be copied into isolated child workers. |
| `DataRetentionServiceTests.cs`, C508_IntentSessionRetention, C508_IntentTranscriptRetention, C508_NotificationTaskTreeRetention, C544_CompletionObligationRetention, SeedCompletionObligation, CreateService, SeedSessionAsync, SeedTranscriptAsync, SeedQueuedAsync | Interpreter/parent/task-tree/queue evidence and prune controls -> V-11, R-11. Confirmed completion-specific wire rules do not apply to LegacyCheckNote. |
| `AgentTaskCheckSweepTests.cs`, interpretation-window suppression, pending cancellation, attempted-row protection, banner fallback, final-flush cancellation, still-working cases; Harness construction, context, specialist/result, note/completion seeding and disposal | Independently reachable dispatcher cancellation and queue cancellation/banner -> V-9, R-9, PC-198/199/215. |
| `ParkedMessageSweepServiceTests.cs`, human/cap and Completion_and_conversation_keyed_rows_stay_parked; complete World | Sourced/keyed rows survive ordinary anonymous-input sweeper -> V-9, R-9. |
| `TestHelpers/BridgeQueueHarness.cs`, CreateAsync, insert/receipt callback, explicit connection registration and disposal entry; complete `DelegationTestServices.cs`, `TestDbFixture.cs` and `LandQueueRaceWorker.cs` | Nearest fixtures for new LegacyCheckNotePublicationTests and CheckCompactionFixture/CrashWorker: isolated migrated clone, real queue graph, child entry/kill/await/drain -> all H-5 integration tests. |
| `tests/Antiphon.SessionRunner.Tests/LocalHttpRunner.cs`, complete; `TestHelpers/ProductionRunnerGuard.cs` assembly guard | Random-port owned runner and production-port refusal -> V-10. |
| Parent plan's Verification design, setup, delivery inventory, affected guard/PC rows and complete cost/command inventory | Existing 169 controls retained; H-5 deltas below add independently bypassable guards. Unchanged parent inspection remains its prior record, not a claim of rereading every sibling method here. |

Production bodies read: AgentTaskCheckService.RunCheckAsync/InterpretAsync/RoutedAsync;
SpecialistTaskRunner.RunAsync/CreateRunTaskAsync/WaitForRunAsync and failed-save
detachment; AgentTaskDispatcher.ReconcileSupersededChecksAsync; entire
AgentTaskLandNotificationService and AgentTaskLandNotificationHostedService;
CompletionNoteWorkHostedService and CompletionNoteWork; queue keyed insert/adoption
and final supersession loop; ParkedMessageSweepService candidate/action loop.
Owner instructions read: AGENTS, testing-and-build ordinary/PC/delivery sections,
session-runtime-invariants delivery/continuity sections, orchestration-loop stage
and SourceLanding rules, project-context layer/migration conventions.

**Required setup, absent at this HEAD, with concrete construction:**

1. Implement the parent's CheckCompactionFixture/CrashWorker and synthetic native
   Check scenario. Reuse the inspected child-dispatch pattern, not a second test
   database in each child. Parent owns the migrated clone, random-port runner and
   native home; application workers inherit only their explicit connection,
   runner URL, owned root and operation IDs. Verify matching assembly MVID, runner
   URL port other than 17204, and child TestDbFixture state `never-requested`.
   Register actual AgentControlService, launch queue, dispatcher, specialist run/
   settlement, LegacyCheckNotePublicationService, notification service, singleton
   CompletionNoteFlushQueue/SpecialistFailureQueue, **both**
   AgentTaskLandNotificationHostedService and CompletionNoteWorkHostedService,
   and the real session event pump. Use AddDelegationWorktreeGraph. New process
   classes, including the extended Handoff class, take ProcessSpawnLimit; stop,
   await and drain every owned child before disposing its store.
2. Native acceptance must receive the actual new G2 brief, emit its complete
   UserPrompt and correlated useful report/TurnEnd, and let real settlement
   persist the result. Do not call SettleInterpretationAsync or seed successful
   transcript receipts. Seed only the qualifying initial episode/native history.
   Busy caller input ends through the scripted native child's own TurnEnd and
   event pump; eligible caller has an ended turn and no future TurnEnd to rescue it.
   Final assertions query persisted transcript and independently read the native
   transcript. Keep successful note/brief bodies within the inline envelope.
3. Extend the parent's concrete CheckCompactionBoundary with
   `legacy-capture-committed`, `legacy-run-link-committed`,
   `legacy-outcome-selected`, `legacy-before-produce-commit`,
   `legacy-note-produced`, and `legacy-captured-scan`.
   Include P, preallocated event/notification, run, E/G2 and parent in each
   rendezvous. `legacy-note-produced` is after actual transaction commit.
   Use a DbTransactionInterceptor for commit/ack cuts; SavedChanges inside an
   explicit transaction is not a commit. Scope command/save faults to the exact
   P/event/notification or interpretation task, never any INSERT globally.
4. Before admitting P, arm `before-note-enqueue` for P.NotificationId on
   **all** producer/scanner reconciliation entries. The worker may publish its
   post-commit rendezvous only when this hold is installed. Independently read
   P=Produced, complete body/digest, one Check event, one outbox and zero keyed
   queue rows. Kill that exact application worker, await exit/drain, then start
   a replacement without inherited holds or the producer invocation. Scanner
   receipts identify first/second completed passes even when the target is absent.
   Bounded polling must end in the named Shouldly assertion; cancellation/timeout
   or a missing fixture rendezvous is an infrastructure/precondition failure.
5. For immediate/adoption wakeup controls only, pause the target session at the
   existing LandDeliveryBoundary `completion-scan` before it enqueues a
   fallback wakeup. Allow CompletionNoteWorkHostedService.FlushAsync to run.
   Hold any generic stranded-queue scan for that target until after assertion.
   This isolates the producer/adoption wakeup from the one-second completion
   scan without replacing the production flush consumer. PC-161 needs no such
   masking fix because the crash leaves no queue row for that scan to discover.
6. Schema tests must migrate an empty clone and upgrade from the immediately
   preceding migration, then prove: Captured saves with preallocated IDs and no
   event/outbox; Produced has the committed links; independent inserts enforce
   unique keys and deletion restrictions. An immediate nonnull FK from the
   preallocated SourceEventId to a not-yet-created event would make Capture
   impossible. Keep the preallocated identity distinct from an established
   relationship until production; verification asserts this lifecycle, not a
   fictitious event at capture. Code must report migration constraint failures,
   not work around them by dropping integrity checks.
7. Instrument real probe entry, renderer entry, Check count and created-run IDs.
   Counters observe calls; they never provide results. After production, edit
   live facts/title/reply style, advance time and recreate services; stored bytes
   remain authoritative. After selected timeout, allow the original run to finish
   successfully and prove the selection is not upgraded.
8. Use a deterministic clock for deadline math (-1 microsecond, exact deadline,
   +1 microsecond); use real-time-offset timers for native/hosted tests. The
   existing notification scanner uses DateTime.UtcNow and a five-second delay:
   make retry rows due relative to real UTC, or wait its actual cadence.
   Do not advance only a fake clock and infer a scan happened. Each test names
   its expected nonzero case count and records each completed scan/cut.

### Delivery inventory

H-1/H-2/H-4/H-6/H-7 remain in the parent unchanged. This table refines H-3's new
publication association and replaces H-5. Durable join:
`(CheckedTaskId, Attempt, DispatchedAt, CheckNumber) -> P -> E/G2 + InterpretationTaskId -> SourceEventId -> NotificationId -> SourceLandNotificationId/QueueMessageId -> frozen ParentSessionId + complete UserPrompt sequence/UUID`.
No slug, latest CheckCount, latest session or text marker substitutes for that join.

| Path | Producer -> destination | Persistence boundary and recovery | Observable receipt |
|---|---|---|---|
| H-5a capture/run | AgentTaskCheckService -> CaptureAsync -> SpecialistTaskRunner -> real dispatcher/G2 queue | Captured P before runnable work; one linked run + Created event + InterpretationTaskId commit together. RecoverCapturedAsync adopts the same run; original deadline and eligibility cap a missing-run admission. | Complete G2 UserPrompt matching that run's entire brief, then useful correlated report/TurnEnd and actual settlement. V-8/V-10; parent PC-112–117 plus PC-170–180. |
| H-5b production | Original selected result -> ProduceAsync -> Check event + LegacyCheckNote outbox | Selected outcome frozen first; P/body/digest/event/outbox commit atomically. Suppressed commits event/reason without obligation. Restart renders captured inputs only. | Persisted chain is a production precondition, never caller receipt. Same-P recovery continues through H-5c/d. V-8; PC-181–185/200/214. |
| H-5c enqueue/wakeup | Immediate producer or notification scanner -> keyed Check-origin queue -> CompletionNoteFlushQueue/real flush worker | Produce commits before enqueue. Failed insert leaves same outbox due. Lost insert/link/wakeup ack adopts same row/history; actionable adopted and already-linked rows wake, parked rows do not. Captured pass must never become a second Produced delivery engine. | Busy parent: zero input/attempts until native TurnEnd. Eligible parent: exact original body in complete UserPrompt via wakeup, without direct Reconcile/Flush calls. V-9/V-10; PC-161/186–197/208. |
| H-5d attempt/receipt | SessionMessageQueueService -> frozen caller's actual composer -> native transcript -> outbox and E receipt readers | Attempt baseline/generation/body persist before I/O. After accepted-prompt death/lost verdict, late-confirm precedes retry. Missing/unavailable destination or authoritative queue row remains unresolved; no replacement destination/row. | Exactly one matching complete immutable P.Body prompt above attempt floor, matching parent/native identity, then same notification confirmation and E proof. Pointer/header/Sent/ack/other-P receipt cannot validate E. V-10/V-11; parent receipt PCs plus PC-201–212. |

Crash/enqueue matrix: run **both busy and already-eligible caller variants** for
each of (1) capture rollback, (2) capture committed before run creation,
(3) run-link transaction rollback, (4) run-link committed/ack lost,
(5) selected result committed before production, (6) event/outbox insertion
failure, (7) Produce committed before enqueue, (8) queue insertion failure,
(9) queue committed before acknowledgment, (10) outbox link committed before
wakeup, (11) attempt committed before input, (12) prompt accepted before verdict,
and (13) verdict committed before notification/E receipt save. Ordinary
service-recreation cases cover all 26 combinations; V-10 repeats the same cuts
with actual application-worker death at the reachable before/after commit
rendezvous. Fault-before-commit variants must first prove rollback, retry the
same admissible operation, and end at receipt. On an expired/disabled/mismatched
missing-run capture, the endpoint is a complete degraded note and E still
AwaitingCheck, not a successful interpretation.

The pre-enqueue PC-161 case specifically leaves the subject open and useful
result selected, changes live facts only after death, and checks zero extra
probe/render-after-Produced/run/count activity. A second replacement worker
must preserve the original queue ID, receipt sequence and one submitted body.
Run boot and periodic discovery: for periodic, hold a real pass before P commits,
then release it and observe the following pass recover the obligation.

Additional boundary combinations: duplicate capture before/after run and
production; Check number advance versus re-dispatch with reset count; independently
changed Attempt/DispatchedAt with same number; same P with one changed binding
member; two workers at absent-key and selected-outcome races; no run versus linked
Queued/Working/terminal run at the fixed deadline; each disabled setting before
capture, after capture and after production; settled subject before/after production
with/without a completion note; queue never attempted/attempted/parked/canceled;
parent running/stopped/failed/missing/replaced; exact/old/wrong-parent/partial/
pointer/no UserPrompt evidence. Preserve the parent's physical/logical AlwaysOn
2x2 and role/provider/trigger/generation negative matrices at admission. Cross
every delivery crash cut with caller busy/eligible; unrelated full Cartesian
products of all settings, corruptions and crash cuts are excluded because the
single-fault controls and listed transition races exercise the independent guards.

Substitutes and limits: direct DB seeds/interceptors prove identity/rollback and
negative evidence rejection, not delivery. Fake adapter submission callbacks
prove queue bytes, not native tailing. Service recreation cannot prove release
of a dead process's locks. Native scripted Claude proves actual runner/queue/
transcript plumbing and settlement, not model quality or the provider stall cause.
Queue insertion, terminal Check events, return value Delivered, notification
Confirmed without its prompt, Sent and transport ack are never delivery acceptance.
Review must reject a handoff ending before the matching complete caller UserPrompt.

### Proves it works now

These are Code's ordinary acceptance requirements; no runtime pass is claimed here.

- V-8: captured identity, migration lifecycle, one run and atomic production |
  PostgreSQL + real services | LegacyCheckNotePublicationTests and
  AgentTaskLandNotificationPersistenceTests | coherent capture, fixed deadline,
  immutable first selection/body, no partial event/obligation and no orphan run.
- V-9: scanner, keyed adoption, wakeup and narrow supersession exception |
  hosted services + real queue | AgentTaskLandNotificationRecoveryTests,
  SessionMessageQueueServiceTests, AgentTaskCheckSweepTests and
  ParkedMessageSweepServiceTests | one original row, unchanged attempts/payload,
  no completion stamp, ordinary/routed behavior preserved.
- V-10: all H-5 cuts and producer-to-recipient chain | owned application workers,
  PostgreSQL + isolated native runner | CheckNoteDeliveryHandoffTests,
  CheckCompactionCrashTests and ReceiptFailureDeliveryTests | complete original
  brief/result/caller prompt with exact joins, busy/eligible and no second Check.
- V-11: retained evidence and receipt isolation | retention + real receipt reader |
  DataRetentionServiceTests and Handoff receipt methods | referenced evidence
  remains, unrelated eligible data prunes, no gate credit for another E/G/run/P.

### Guards the regression

- R-8: accepted capture/run/production can be lost or changed on retry | every
  L/I method below | independent fresh-context IDs/bytes/counts and complete
  same-P recovery receipt; migration must accept Captured without invented event.
- R-9: scanner/adoption loses wakeup, resets queue history or treats a Check as
  completion | O/Q methods below and unchanged queue/sweep suites | original row
  identity/history and complete prompt; target's fallback scans held for wakeup PCs.
- R-10: settlement/disable/restart/pointer/old evidence releases recovery early |
  H methods and parent receipt methods with legacy variants | AwaitingCheck until
  that exact useful result and complete immutable caller prompt, one submit only.
- R-11: retention removes the interpreter's indirect evidence or publication
  identity | U methods below plus parent PC-158/162–165 | retained IDs and exact
  proof, unrelated prune control, later capture adopts tombstone without new note.

### Guard inventory

The parent's G-1 through G-169 remain mapped 1:1. G-161 is now specifically
independent discovery of a committed Produced legacy outbox after worker death;
the formerly bundled capture, atomicity, immutability and wakeup guards are split
below. No safety-critical guard is declared none. Every new guard has a distinct
PC with the same number; no PC is used for two guard rows.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-170 | D-12 CaptureAsync: Lookup an existing capture before a new probe. | PC-170 |
| G-171 | D-13 CaptureAsync: Capture verifies the locked execution/count snapshot. | PC-171 |
| G-172 | D-13 migration: Database uniqueness, not a process lock, owns the logical Check key. | PC-172 |
| G-173 | Protocol 2: Interpretation task, Created event and P link commit together. | PC-173 |
| G-174 | Protocol 2: Repeated binding adopts the committed original run. | PC-174 |
| G-175 | D-13 migration: One interpreter run cannot be linked to two publications. | PC-175 |
| G-176 | Protocol 2/3: Missing-run admission uses captured E/G2, never the latest seat. | PC-176 |
| G-177 | Protocol 3: Restart cannot renew the captured interpretation deadline. | PC-177 |
| G-178 | Protocol 3: An expired capture with no linked run starts no interpretation. | PC-178 |
| G-179 | Protocol 3/4: Selected degradation survives late successful settlement. | PC-179 |
| G-180 | Protocol 4: Concurrent finalizers adopt the first selected outcome. | PC-180 |
| G-181 | D-12 ProduceAsync: Check event cannot commit outside the publication transaction. | PC-181 |
| G-182 | D-12 ProduceAsync: Outbox cannot commit without the corresponding Produced publication. | PC-182 |
| G-183 | D-12/14 ProduceAsync: Produced returns stored payload before mutable facts/rendering. | PC-183 |
| G-184 | D-13 replay: Conflicting replay bindings or payload are refused without overwriting the winner. | PC-184 |
| G-185 | Payload canonicalization: Digest identifies the once-normalized full UTF-8 Body. | PC-185 |
| G-186 | Protocol 6: Captured work is discoverable after producer death. | PC-186 |
| G-187 | Protocol 6: One failing captured row cannot abort later recoveries. | PC-187 |
| G-188 | Recovery bookkeeping: Not-yet-due captured retries are not executed. | PC-188 |
| G-189 | Protocol 6: Captured recovery does not deliver Produced work through a second path. | PC-189 |
| G-190 | Scope/Protocol 6: Disabled discovery cannot discard an already Produced caller obligation. | PC-190 |
| G-191 | Protocol 3/6: Disabled optional work cannot create an uncommitted interpretation. | PC-191 |
| G-192 | Protocol 5: The new outbox kind enqueues with Check origin. | PC-192 |
| G-193 | Protocol 5: The conversation key names the checked task, not the notification or root. | PC-193 |
| G-194 | Protocol 5: LegacyCheckNote never stamps completion. | PC-194 |
| G-195 | Protocol 5/D-13: Immediate/recovered enqueue carries the same notification key. | PC-195 |
| G-196 | Protocol 5: Actionable adopted/already-linked rows receive a flush wakeup. | PC-196 |
| G-197 | Protocol 5 QueueAttention: Parked rows do not get woken just because the notification was scanned. | PC-197 |
| G-198 | D-14 queue last look: Final delivery cannot automatically cancel a Produced Check. | PC-198 |
| G-199 | D-14 queue last look: Final delivery cannot add a new banner to a Produced body. | PC-199 |
| G-200 | D-14 producer suppression: Suppression before production creates no delivery obligation. | PC-200 |
| G-201 | D-15 suppression receipt: Suppressed publication cannot validate the recovered seat. | PC-201 |
| G-202 | D-15 E identity: A delivered old publication cannot validate another episode. | PC-202 |
| G-203 | D-15 G2 identity: The accepted interpreter generation must match P's frozen binding. | PC-203 |
| G-204 | D-15 run identity: Useful result and complete brief must belong to P.InterpretationTaskId. | PC-204 |
| G-205 | D-15 subject identity: Recovery credit requires the captured checked execution and number. | PC-205 |
| G-206 | D-15 immutable receipt: A pointer/wire rewrite cannot replace the original whole-body receipt. | PC-206 |
| G-207 | Scope frozen destination: An unavailable original parent never redirects to a replacement. | PC-207 |
| G-208 | Protocol 5 authoritative row: Missing authoritative queue row never causes a new mint. | PC-208 |
| G-209 | Retention task link: Unresolved P protects its interpreter task tree independently of the checked task. | PC-209 |
| G-210 | Retention session link: Unresolved P protects the original interpreter session. | PC-210 |
| G-211 | Retention transcript link: Unresolved P protects original interpreter transcript evidence. | PC-211 |
| G-212 | D-13 retention identity: Pruning cannot remove publication identity and permit replay. | PC-212 |
| G-213 | Scope producer entry: Only the admitted legacy recovery-validation Check enters publication capture. | PC-213 |
| G-214 | Payload state lifecycle: Migration accepts Captured but rejects incomplete Produced state. | PC-214 |
| G-215 | D-14 dispatcher sweep: Dispatcher supersession cannot cancel the Produced obligation before flush. | PC-215 |
| G-216 | Protocol 6 paging: Captured recovery reaches rows past the first page. | PC-216 |
| G-217 | Protocol 5 source identity: SourceTaskId names the checked task even when its root differs. | PC-217 |

### Positive controls

PC-161: break G-161 by adding `n.Kind != LandNotificationKind.LegacyCheckNote`
to AgentTaskLandNotificationHostedService.ExecuteAsync's unresolved-notification
query. Expect `CheckNoteDeliveryHandoffTests.Legacy_produced_note_survives_pre_enqueue_worker_death`
red at `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1)` with actual zero,
after confirmed pre-enqueue commit, worker exit/drain and two completed recovery
passes. Both busy and eligible variants run; a queue row, direct rescue call,
replacement Check or missing rendezvous cannot satisfy this control.

Each row below names one compiling production defect, exact method and decisive
assertion. L/O/I/U tests use isolated PostgreSQL and real services; H tests use
the native worker fixture and end at recipient evidence unless deliberately
rejecting evidence. Pure persistence negatives use fresh independent contexts.
Methods with several arguments run every declared variant under the exact method
filter. In particular PC-172/175/214 modify the CLI-generated migration's actual
Up operation, not only EF metadata over an already migrated database.

| Alias | Exact class / file under tests/Antiphon.Tests/Application |
|---|---|
| L | `LegacyCheckNotePublicationTests` / `LegacyCheckNotePublicationTests.cs` |
| H | `CheckNoteDeliveryHandoffTests` / `CheckNoteDeliveryHandoffTests.cs` |
| O | `AgentTaskLandNotificationRecoveryTests` / `AgentTaskLandNotificationRecoveryTests.cs` |
| I | `AgentTaskLandNotificationPersistenceTests` / `AgentTaskLandNotificationPersistenceTests.cs` |
| Q | `SessionMessageQueueServiceTests` / `SessionMessageQueueServiceTests.cs` |
| U | `DataRetentionServiceTests` / `DataRetentionServiceTests.cs` |

| PC | Exact method | Break the mapped guard by this compiling defect | Expected red assertion |
|---|---|---|---|
| PC-170 | `L.Legacy_capture_retry_does_not_probe_again` | Move the existing-P lookup after GatherAsync. | `probeCalls.ShouldBe(1) after duplicate capture and service recreation` |
| PC-171 | `L.Legacy_capture_rejects_a_changed_execution` | Remove the locked task-snapshot equality check. | `acceptedPublications.Count.ShouldBe(0) after the competing execution/count update` |
| PC-172 | `I.Legacy_capture_key_is_unique_in_postgres` | Change the generated publication-key CreateIndex call to unique:false. | `Should.ThrowAsync<DbUpdateException>(duplicateInsert) fails because the identical four-member key inserts` |
| PC-173 | `L.Legacy_capture_and_interpretation_link_commit_together` | Commit the new run/Created event before beginning the P-link transaction. | `orphanRunIds.ShouldBeEmpty() after the P-link write fault` |
| PC-174 | `L.Legacy_run_link_ack_loss_adopts_the_original_run` | On a linked P, create a fresh run instead of returning InterpretationTaskId. | `runIdsForPublication.Count.ShouldBe(1) after lost acknowledgment and a second worker` |
| PC-175 | `I.Legacy_interpretation_link_is_unique_in_postgres` | Change the generated InterpretationTaskId index to unique:false. | `Should.ThrowAsync<DbUpdateException>(secondPublicationWithSameRun) fails` |
| PC-176 | `L.Legacy_missing_run_cannot_bind_a_replacement_generation` | Read the seat's current accepted generation instead of P's frozen generation when admitting the linked run. | `newRunIds.ShouldBeEmpty() when the same session ID now has G3` |
| PC-177 | `L.Legacy_linked_run_recovery_keeps_the_original_deadline` | Set InterpretationDeadlineAt to clock-now plus CheckInterpreterWaitSeconds in recovery. | `storedDeadline.ShouldBe(originalDeadline) at restart with the original run still Working` |
| PC-178 | `L.Legacy_expired_capture_never_creates_an_interpreter` | Remove the missing-run deadline admission check. | `newRunIds.ShouldBeEmpty() at deadline and deadline+1 microsecond` |
| PC-179 | `L.Legacy_late_result_cannot_upgrade_a_selected_timeout` | Overwrite an existing InterpretationSnapshotJson with the later linked-run result. | `storedSelection.ShouldBe(originalTimeoutSnapshot) after a render failure and late success` |
| PC-180 | `L.Legacy_concurrent_finalizers_keep_the_first_selection` | Replace the locked selection's existing-value return with an unconditional update. | `selectionAfterSecondFinalizer.ShouldBe(firstCommittedSelection) after a controlled competing finalizer` |
| PC-181 | `L.Legacy_note_body_event_and_obligation_commit_together` | Persist the Check event in a separate committed context before the publication transaction. | `checkEvents.Count.ShouldBe(0) after the before-Produced-update fault rolls back publication` |
| PC-182 | `L.Legacy_outbox_cannot_commit_without_production` | Commit the Check event and notification in a separate context before the publication transaction. | `notifications.Count.ShouldBe(0) after the before-Produced-update fault` |
| PC-183 | `L.Legacy_production_retries_keep_the_first_body` | Call the renderer before the Produced-state short circuit. | `rendererCallsAfterProduction.ShouldBe(0); the recording renderer returns normally with changed inputs` |
| PC-184 | `L.Legacy_conflicting_replay_preserves_the_winner` | Replace the capture/production immutable-snapshot comparison with true. | `replayConflict.ShouldNotBeNull() for a single changed binding or canonical body; winnerSnapshot.ShouldBe(original)` |
| PC-185 | `L.Legacy_body_digest_covers_the_canonical_body` | Compute ContentDigest from the first body line only. | `storedDigest.ShouldBe(SHA256OfCanonicalFullBody) for CRLF, Unicode and a distinct final line` |
| PC-186 | `H.Legacy_captured_note_survives_worker_death` | Remove the hosted RecoverCapturedAsync pass, leaving notification scanning intact. | `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1) with zero after death before production` |
| PC-187 | `O.Legacy_captured_scan_isolates_a_poison_row` | Move the per-row exception handler outside the captured foreach. | `laterPublication.State.ShouldBe(Produced) after a deterministic first-row fault` |
| PC-188 | `O.Legacy_captured_retry_obeys_its_due_time` | Remove NextAttemptAt from the captured scan's due predicate. | `recoverCallsForFuturePublication.ShouldBe(0) before due; exact due completes it` |
| PC-189 | `O.Legacy_captured_pass_excludes_produced_rows` | Remove the State==Captured restriction in the captured pass. | `capturedPassPublicationIds.ShouldNotContain(producedId) with a simultaneous genuine Captured control` |
| PC-190 | `H.Legacy_note_recovery_runs_with_checks_disabled` | Return before notification scanning when CheckEnabled is false. | `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1) under disabled recovery; zero extra launches` |
| PC-191 | `L.Legacy_disabled_capture_finalizes_without_a_new_run` | Ignore the disabled-setting admission veto for a captured P with null InterpretationTaskId. | `newRunIds.ShouldBeEmpty() while the degraded original note remains owed` |
| PC-192 | `O.Legacy_note_uses_check_origin` | Use QueuedMessageOrigin.Delegation for LegacyCheckNote. | `row.Origin.ShouldBe(QueuedMessageOrigin.Check)` |
| PC-193 | `O.Legacy_note_keeps_the_original_check_conversation` | Use land:{note.Id:N} for LegacyCheckNote. | `row.ConversationKey.ShouldBe(checkKeyForCheckedTask) with checked task distinct from its root` |
| PC-194 | `O.Legacy_note_never_stamps_task_completion` | Include LegacyCheckNote in IsCompletionNoteKind. | `task.CompletionNoteQueuedAt.ShouldBeNull() and unchanged digest after insert and recovered adoption` |
| PC-195 | `H.Legacy_note_duplicate_workers_share_one_queue_row` | Pass sourceLandNotificationId:null only for LegacyCheckNote. | `queueRowsForPublication.Count.ShouldBe(1) after two workers, lost acknowledgment and original receipt` |
| PC-196 | `H.Legacy_adopted_note_wakes_an_already_eligible_parent` | Omit the actionable-existing-row flushes.TryEnqueue branch in ReconcileAsync. | `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1) while completion/stranded fallback scans remain held` |
| PC-197 | `O.Legacy_parked_note_is_not_woken_by_adoption` | Replace the actionable QueueAttention predicate on recovered rows with true. | `targetWakeupCount.ShouldBe(0) for parked Pending at cap; interrupted Sent at cap is the positive opposite` |
| PC-198 | `H.Legacy_produced_note_survives_subject_settlement` | Remove the LegacyCheckNote exemption only from the queue completion-present cancellation arm. | `matchingCompleteOriginalParentPrompts.Count.ShouldBe(1) after subject settlement with completion present` |
| PC-199 | `H.Legacy_produced_note_is_not_rebannered` | Remove the LegacyCheckNote exemption only from the queue banner arm. | `receivedCanonicalBody.ShouldBe(originalProducedBody) after settlement without completion` |
| PC-200 | `L.Legacy_suppression_commits_an_event_without_an_obligation` | Route the suppress=true branch through normal production/outbox creation. | `notifications.Count.ShouldBe(0) while the sole Check event/reason persists` |
| PC-201 | `H.Legacy_suppressed_note_never_recovers_the_seat` | Treat a Suppressed P and its Check event as satisfying legacy receipt. | `episode.State.ShouldBe(AwaitingCheck) after a suppressed useful interpretation` |
| PC-202 | `H.Legacy_note_receipt_cannot_validate_another_episode` | Remove P.RecoveryId equality from the legacy receipt join. | `replacementEpisode.State.ShouldBe(AwaitingCheck) despite old P's real confirmed receipt` |
| PC-203 | `H.Legacy_note_receipt_cannot_validate_another_generation` | Remove the accepted-generation tuple comparison from the legacy receipt join. | `episode.State.ShouldBe(AwaitingCheck) when only P's generation binding differs from E's accepted G2` |
| PC-204 | `H.Legacy_note_requires_its_own_interpretation_proof` | Resolve the newest Check result instead of the stored InterpretationTaskId. | `episode.State.ShouldBe(AwaitingCheck) when only another run has useful complete proof` |
| PC-205 | `H.Legacy_note_requires_the_captured_check_identity` | Remove the captured subject-execution/check tuple comparison from the receipt join. | `episode.State.ShouldBe(AwaitingCheck) for one mismatched tuple member at a time` |
| PC-206 | `H.Legacy_pointer_only_prompt_is_not_original_note_receipt` | Use row.Body instead of note.Body as expected receipt for LegacyCheckNote. | `episode.State.ShouldBe(AwaitingCheck) and note.ConfirmedAt.ShouldBeNull() after actual pointer-only submission` |
| PC-207 | `O.Legacy_unavailable_parent_is_not_redirected` | On missing/stopped original destination, use CheckedTask.ParentSessionId's new value. | `replacementParentPrompts.ShouldBeEmpty() and note.ParentSessionId.ShouldBe(originalParent)` |
| PC-208 | `O.Legacy_missing_authoritative_queue_row_remains_unresolved` | Clear QueueMessageId when its row is missing so the next reconciliation re-enqueues. | `replacementRows.Count.ShouldBe(0) after two reconciliation passes` |
| PC-209 | `U.Legacy_note_retention_preserves_interpreter_tasks` | Remove the publication interpreter-task referencer from PruneTasksAsync. | `observedRetention.ShouldBe(expectedRetention)`; the observation includes caught prune error type, retained original run IDs and absent unrelated task IDs |
| PC-210 | `U.Legacy_note_retention_preserves_interpreter_sessions` | Remove the publication interpreter-session referencer from PruneSessionsAsync. | `observedRetention.ShouldBe(expectedRetention)`; the observation includes caught prune error type, retained original session IDs and absent unrelated session IDs |
| PC-211 | `U.Legacy_note_retention_preserves_original_receipt_evidence` | Remove the publication interpreter referencer from PruneTranscriptsAsync. | `observedRetention.ShouldBe(expectedRetention)`; the observation includes caught prune error type, retained original transcript IDs and absent unrelated transcript IDs |
| PC-212 | `U.Legacy_publication_tombstone_prevents_reminting` | Delete confirmed publication identity/tombstone in ordinary retention. | `publicationIdentity.ShouldBe(originalIdentity) after prune and duplicate capture; newNotes.Count.ShouldBe(0)` |
| PC-213 | `L.Legacy_publication_scope_is_narrow` | Infer the current AwaitingCheck E/G2 from the seat for any legacy Check instead of requiring the explicit requested association. | `publicationsOutsideAssociatedLegacyRecovery.Count.ShouldBe(0)` for an ordinary legacy Check on that otherwise eligible seat; routed/role/AlwaysOn negatives also remain unchanged |
| PC-214 | `I.Legacy_publication_state_constraints_preserve_capture_lifecycle` | Change the generated Produced completeness CHECK constraint predicate to TRUE. | `Should.ThrowAsync<DbUpdateException>(incompleteProducedInsert) fails; the valid Captured control still saves` |
| PC-215 | `H.Legacy_produced_note_survives_the_dispatcher_supersession_sweep` | Remove the keyed-LegacyCheckNote exclusion in AgentTaskDispatcher.ReconcileSupersededChecksAsync. | `row.Status.ShouldBe(Pending) after real dispatcher sweep while caller stays busy, then exact original prompt arrives` |
| PC-216 | `O.Legacy_captured_scan_reaches_later_pages` | Break unconditionally after the first captured page. | `lastPagePublicationIds.ShouldBe(expectedLastPageIds) after a pass over 263 rows` |
| PC-217 | `O.Legacy_note_keeps_the_checked_task_source` | Use RootTaskId rather than CheckedTaskId for LegacyCheckNote enqueue. | `row.SourceTaskId.ShouldBe(checkedChildTaskId)` |

Atomicity methods inject event INSERT, notification INSERT and transaction
commit failures independently, and also stop after successful INSERTs but before
the transaction commit. Each must observe no partial state through another
connection. Their PCs deliberately publish an event/obligation early; the expected
red is the leaked row assertion, not failure to reach a fault hook. The same
admissible P then completes once after restoring the fault. Run-link rollback
also saves an unrelated event on the failed scoped context and ticks the real
dispatcher: an abandoned Added run must not be republished by that later save.

PC-171 varies each execution/count member individually. PC-172 uses identical
keys, adjacent Check numbers, changed Attempt and changed DispatchedAt with a
reset number; database and service observations must agree. PC-175 includes
null versus linked runs. PC-177/178 cross no run, Queued/Working run and exact
deadline; PC-179/180 distinguish stored selected outcome from a later live result.
PC-184 changes one immutable member at a time; no input has two mismatches that
could mask a missing check. PC-214's decisive mutation case uses an empty Body
with valid digest/event/notification links so another FK or NOT NULL guard cannot
mask the missing completeness check. Its ordinary matrix independently omits
body/digest/established event/notification links from Produced, while Captured
preallocated IDs and Suppressed/no-obligation remain valid. Record unaffected
ordinary positive/negative controls as such, not as additional mutation variants.
Code preserves constraint exceptions as evidence, not as fixture failures.

PC-195 runs concurrent absent-key insertion and lost acknowledgment. PC-196
runs lost queue acknowledgment, failed outbox-link save, and committed link/lost
wakeup; each adopted row is fresh Pending and below stranded age. In PC-197
observe actual target wakeups through the production boundary/queue, drain no
fake channel; run Pending at cap, below cap, interrupted Sent at cap and explicit
Canceled, preserving all attempt/generation/baseline fields. PC-198/215 hold
caller busy during subject settlement with a completion note; PC-199 omits that
completion note so only the banner branch is eligible. The dispatcher exclusion
is required by D-14 even though H5-2's original file list omitted
`server/Application/Services/AgentTaskDispatcher.cs`; include that file
and AgentTaskCheckSweepTests in the implementation/checkpoint footprint.

PC-202 and PC-203 separate E identity from accepted-generation identity. In
PC-202's isolated corruption case change only P.RecoveryId to a distinct episode
with the same seat/generation/subject bindings, leaving every receipt check
otherwise satisfied. In PC-203 change only P's generation tuple; retain E's
AwaitingCheck state, matching run and actual G2/native caller evidence so another
veto does not hide the omitted comparison. Also run the ordinary post-production
G3/superseded-E case: the old note may deliver, but it earns no credit for G3.
PC-204 supplies another valid run so a missing join is observable. PC-205
changes one subject tuple member with all receipt evidence otherwise valid.
PC-206 uses a real oversized note whose actual submitted pointer lands; no
test helper copies P.Body into a fake receipt. PC-207 freezes the original
parent before editing the subject's current parent. PC-208 removes only the
authoritative queue row in test data; attempts remain recorded in the outbox
and no new producer runs. Retention controls use aged, stopped, unowned
interpreter sessions/tasks so standing pointers or unrelated obligations cannot
mask the publication referencer; each sweep has an unrelated eligible prune
control. RetentionObservation records (prune error type, retained protected IDs,
remaining unrelated IDs); expectedRetention is (null, original IDs, empty).
Use its equality assertion so a Restrict refusal and an illicit deletion both
produce the specified red, without an unhandled fixture error being counted.

Carry the legacy path as an additional argument in the parent's exact methods
for PC-30, PC-112–117, PC-121–123 and PC-159 (11 controls).
Their guard/mutation/assertion definitions do not change: Sent is insufficient,
brief/result/parent/attempt-floor/end joins still apply, busy means no input,
eligible means producer wakeup, accepted-prompt recovery cannot retype and a
truncated body is insufficient. PC-122 holds fallback scanners as above so
its omitted producer wakeup cannot pass accidentally. The parent's failure-note
PC-120 and routed suppression PC-160 remain unchanged; their independently
implemented legacy guards are PC-195 and PC-201. Parent PC-162–165 retain their
existing evidence scope; P's new interpreter referencers are PC-209–211.

For all 217 controls: Code implements ordinary tests and runs V/R; ordinary
Review judges the code and pending control designs before land. After confirmed
land, SourceLanding Mutation records break, exact-method intended red, restore,
fresh build and exact-method green, each argument's nonzero counts, tested SHA,
MVID and assertion. Build/fixture errors, zero selection and generic timeout
are not red. A surviving or impossible control is a finding, never a waived PC.
No mutation is executed by this documentation stage.

### Out of scope

- Changes to AlwaysOn Check admission, CompactBoundary predicates, generation
  fences, stop/resume policy, timeout settings or model selection: unchanged
  parent scope; negative controls remain required.
- Rewriting ordinary legacy/routed Check behavior or accepting pointer-only
  evidence under TaskCompletion rules: expressly excluded by D-14/D-15.
- Live production recovery/deployment/provider reasoning quality: separate
  rollout work; scripted native evidence proves transport and settlement only.
- Full assembly/browser suites and unrelated Cartesian products: bounded affected
  filters and listed transitions cover this amendment; no broad-run exemption
  removes a required recipient/crash case.
- Pre-land mutation and claims of runtime success from this stage: Code owns
  ordinary V/R, Mutation owns all post-land red/restore/green cycles.

### Checkpoints

This is the complete ordinary Code command manifest for the parent plan plus
this H-5 amendment, replacing the parent's historical unnumbered inventory.
Each .NET row means **one foreground isolated build and one test invocation**
using the exact selector below. Reuse this task's owned `bin-c79/` output
between rows; rebuild at every checkpoint so changes made between checkpoints
cannot use stale assemblies. Run application/runner/Pty projects sequentially.
The final client row uses Vitest's own source transform through the required
wrapper; no server or client bundle build is needed for that existing mapping
test. This is an explicit source-test build exception, not a skipped .NET build.

| CP | Project under tests / build | Exact test selector | Coverage | V/R minutes, estimated |
|---|---|---|---|---:|
| CP-1 | `Antiphon.Tests`, dotnet build | `/*/*/*/*[Category=Unit]` | V-1/R-1 | 2.00 |
| CP-2 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionRecoveryFlowTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(AgentTaskStandingAgentDispatchTests*)\|(SessionMessageQueueWedgedHeadTests*)/*` | V-2/R-2 | 12.00 |
| CP-3 | `Antiphon.Tests`, dotnet build | `/*/*/(CompactionContinuationWireTests*)\|(AgentSessionRuntimeTests*)/*` | V-3/R-3 | 6.00 |
| CP-4 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionContinuationTests*)\|(CheckCompactionAutomaticRestartTests*)\|(CheckCompactionRecoveryPersistenceTests*)/*` | V-4/R-4 | 9.00 |
| CP-5 | `Antiphon.Tests`, dotnet build | `/*/*/(AgentSupervisionTests*)\|(SpecialistStartIntentTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionRecoveryHttpTests*)\|(AgentControlServiceIntegrationTests*)/*` | V-4/R-4 | 11.00 |
| CP-6 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckNoteDeliveryHandoffTests*)\|(ReceiptFailureDeliveryTests*)/*` | V-5/V-10; R-5/R-8/R-10 | 37.90 |
| CP-7 | `Antiphon.Tests`, dotnet build | `/*/*/CheckCompactionCrashTests/*` | V-6/V-10; R-6/R-10 | 33.00 |
| CP-8 | `Antiphon.Tests`, dotnet build | `/*/*/(CheckCompactionAttentionTests*)\|(SpecialistHealthAttentionTests*)\|(DataRetentionServiceTests*)/*` | V-7/V-11; R-7/R-11 | 9.50 |
| CP-9 | `Antiphon.SessionRunner.Tests`, dotnet build | `/*/*/(CompactionContinuationStopTests*)\|(TranscriptTailerObservationTests*)\|(RunnerSessionGenerationTests*)\|(TranscriptTailerCompactionTests*)\|(RemoteControlConditionalInputTests*)/*` | V-3/R-3/R-6 | 7.00 |
| CP-10 | `Antiphon.Agents.Pty.Tests`, dotnet build | `/*/*/FakeClaudeContractTests/*` | V-3/R-3 | 2.00 |
| CP-11 | `Antiphon.Tests`, dotnet build | `/*/*/(LegacyCheckNotePublicationTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(SessionMessageQueueServiceTests*)\|(AgentTaskCheckSweepTests*)\|(ParkedMessageSweepServiceTests*)/*` | V-8/V-9; R-8/R-9 | 30.00 |
| CP-12 | client, Vitest transform | `pwsh -File scripts/test-client.ps1 attentionVisuals.test` | V-7/R-7 | 0.50 |


For every .NET row, set `$cpProject` to `tests/<Project>` from that row,
`$cpFilter` to its literal selector (Markdown backslashes escaping pipe
characters are not part of the filter), and `$cpId` to its CP ID. Execute:

```powershell
dotnet build $cpProject --property:OutputPath=bin-c79/ --nologo
if ($LASTEXITCODE -ne 0) { throw "Checkpoint build failed: $cpId" }
$cpResults = Join-Path '.antiphon' ($cpId + '-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project $cpProject --no-build --property:OutputPath=bin-c79/ -- --treenode-filter $cpFilter --report-trx --report-trx-filename run.trx --results-directory $cpResults
if ($LASTEXITCODE -ne 0) { throw "Checkpoint tests failed: $cpId" }
```

The table supplies all arguments; this is an execution template, not an unknown
filter. Record each CP's verified commit, exact filter, duration and expanded
pass/fail/skip counts. Check fresh TRX contains every intended class and new
method; no missing/skipped acceptance variants. `--list-tests` is not
execution evidence. Use the duration tripwire, investigate new slow methods,
and rerun only changed/failing checkpoints after a fix. Additional tests/builds
need a stated defect or changed-source reason. A base failure is inherited only
after that exact method is reproduced at the base; no relaxed waits/retries.

### Cost

All numbers are **estimated clean execution floors**, not measured at this HEAD.
Authoring, diagnosis, ordinary Review, commissioning and live qualification are
additional. The larger total makes the new persistence, crash and independent
guard work explicit; the historical 415.44 minutes is superseded.

| Ordinary Code component | Minutes |
|---|---:|
| Parent cold builds/isolated output and PostgreSQL setup | 10.00 |
| New publication migration upgrade/clone preparation | 3.00 |
| Eight additional warm checkpoint builds (11 .NET rows versus original 3), 0.55 each | 4.40 |
| **Code setup/build** | **17.40** |
| Inherited ordinary V/R floor | 87.00 |
| CP-11 publication, scanner and six affected integration classes | 30.00 |
| CP-6 additional native legacy methods: 13 x 1.50 + PC-161 correction 0.90 + 11 inherited method variants x 0.50 | 25.90 |
| CP-7 full H-5 worker-cut matrix beyond existing parent cuts | 13.00 |
| CP-8 additional retention evidence/tombstone cases | 4.00 |
| **Ordinary V/R only (sum of CP rows)** | **159.90** |
| **Code floor: setup/build + ordinary V/R** | **177.30** |

One PC cycle includes all arguments of its exact named method, both red and
restored green. The parent has 169 cycles, including the former estimate for
PC-161. This amendment keeps that cycle, makes it executable and adds 48 cycles
(PC-170 through PC-217), total **217**. Native H methods are budgeted for both
caller states; no variant is assigned zero cost.

| Mutation method time | Arithmetic | Minutes |
|---|---|---:|
| Parent 169 controls' red/green method floor | retained historical estimate | 102.64 |
| Replace PC-161's 0.60-minute run with 1.50-minute busy/eligible crash method | 2 x (1.50 - 0.60) | 1.80 |
| New L/O/I/U controls (all non-H rows) | 35 x 2 x 0.60 | 42.00 |
| New H producer/native/worker controls | 13 x 2 x 1.50 | 39.00 |
| Extra legacy arguments in the 11 explicitly listed inherited methods | 11 x 2 x 0.50 | 11.00 |
| **Every PC red + green method run** | | **196.44** |

| Mutation component | Minutes |
|---|---:|
| Exact-L preflight, isolated build/template setup | 8.00 |
| Discovery/count validation | 5.00 |
| Apply/restore/restoration checks, 217 x 0.10 | 21.70 |
| Fresh red and restored-green builds, 434 x 0.55 | 238.70 |
| Every method red/green and added argument variant | 196.44 |
| **Mutation floor** | **469.84** |
| **Total verification floor: Code 177.30 + Mutation 469.84** | **647.14 (10 h 47 m)** |

Increase over the historical floor: **231.70 minutes**, comprising 80.30 Code
and 151.40 Mutation. Checkpoint budget sums include client CP-12; it is not
charged again in setup. All 434 incremental mutation builds are charged,
including restored builds; no test or build is hidden behind a claimed reuse.

Quantified savings already reflected: the parent's 2.10-minute grouping and
0.72-minute Unit deduplication; six new affected classes grouped in CP-11 save
five independent host/template startups x 0.15 = **0.75 minutes**; adding legacy
arguments to 11 existing methods avoids eleven separate host starts x 0.15 =
**1.65 minutes**. Total credited ordinary savings **5.22 minutes**. PC sharding
and mutation batching savings **0.00**: most mutations share producer/queue/
scanner files, and SourceLanding has one managed snapshot. Method-scope savings
against whole-suite mutation runs are not assigned an unsupported wall time.

Exact PC-161 execution selector (same filter for red and restored green):
```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c79-pc/ -- --treenode-filter '/*/*/CheckNoteDeliveryHandoffTests/Legacy_produced_note_survives_pre_enqueue_worker_death'
```
For each other new PC use `/*/*/<alias-expanded class>/<exact method>`
from the table, never a whole class or a wildcard method. SourceLanding stores
fresh per-PC red/green TRX, logs, mutation diffs, counts and restoration records
under its caller-assigned external evidence root, with no snapshot commit/push.
Refresh restored timestamps and verify MVID. Await each foreground run before
editing; retain an exact owned output inventory and delete only verified
task-owned alternate-output paths after all children exit.

**Handoff audit:** bodies and nearest fixtures read; inherited guards=169,
new guards=48, **guards=217, mapped=217, missing=0, duplicate PC mappings=0**.
All 217 PC designs are executable constructions; implemented/executed by this
TestDesign=0. PC-161 has no remaining unspecified delivery seam. Ordinary
Code floor=177.30, Mutation floor=469.84, total=647.14 estimated minutes.
Next: **code**, then ordinary Review before land and separately commissioned
post-land Mutation. No human choice or expanded restart authorization is needed.
