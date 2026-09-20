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
