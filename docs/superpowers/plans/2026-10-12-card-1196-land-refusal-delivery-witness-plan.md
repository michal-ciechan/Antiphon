# CARD-1196: land-refusal delivery witness

Date: 2026-10-12. Stage: Plan; next stage: TestDesign. Inspected source:
`f3d67cc88beb516aeca13e67766df6753a968068`.

Prove that a later **live LandRefused Outcome** travels from its real producer,
through the real session message queue to a complete recipient UserPrompt, is
confirmed by the real notification reconciler, and resolves an older historical
LandRefused in the public attention projection. Include busy recipients and the
durable handoffs across enqueue failure and service restart.

This is a test coverage change. No production change is expected. This dispatch
writes design only; it runs no builds, tests or mutations. The verification below
is proposed input to the separate TestDesign stage, not an instruction to skip it.

## Ground truth

Line references below are relative to the inspected source commit, not generated
card files. CARD-1196, CARD-1203 and CARD-1190 were read from the Antiphon board.

| Card assumption / design question | What the code does | Consequence |
|---|---|---|
| CARD-1085 tests stop at seeded confirmation. | `tests/Antiphon.Tests/Application/AttentionFeedNoiseTests.cs:81` explicitly discloses this gap; its test calls `ConfirmLegacyReceiptAsync` at line 105. | Retain the projection tests; add a causal witness instead of renaming their evidence. |
| A historical refusal cannot be resolved by live delivery because legacy notes do not enqueue. | `server/Application/Services/AgentTaskLandNotificationService.cs:53` prevents a legacy note with no queue link from submitting. `server/Application/Services/AttentionService.cs:1676` independently accepts a later confirmed Outcome for the same task. | Seed the old refusal, produce a distinct live refusal. Do not try to replay the historical note. |
| Any later land result is a suitable positive fixture. | `AttentionService.cs:1680` resolves on a later Landed/AlreadyPresent publication before checking the later receipt at line 1686. | A successful landing would hide a broken receipt path. Produce a refusal with no publication and assert this exclusion. |
| There is no existing land/queue composition. | `tests/Antiphon.Tests/TestHelpers/LandOutcomeDeliveryHarness.cs:39` combines `LandingProtocolHarness` and `BridgeQueueHarness`; `AgentTaskLandEnqueueTests.cs:14` exercises production-generated outcomes through the queue. | Reuse those components and recipes. A large replacement framework is unnecessary. |
| StandingBootQueueWitnessTests has no real queue. | The current `StandingBootQueueWitnessTests.cs:280` attaches a real Bridge queue and records adapter submissions as transcript entries; lines 298-304 model a silent recipient. | The card's characterization is stale for CARD-1165. Its missing parts are the land producer, durable note and refusal projection, not the queue. |
| BridgeQueueHarness supplies a complete land scenario. | `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:56` exposes schema, attachment, service and DbContext hooks; line 169 registers the real queue. It does not seed land tasks or register the land notifier. | Compose a small card-specific fixture around its existing hooks. |
| A real producer requires running Git or a native process. | `LandingProtocolHarness.cs:18` uses isolated PostgreSQL and controlled Git; `RequestAsync` at line 149 and `FailAsync` at line 125 invoke the production land service. | Use Request followed by the drain-side failure entry point. No real Git, verifier or agent launch is needed in these tests. |
| The terminal producer and queue insert are one transaction. | `server/Application/Services/AgentTaskLandService.cs:1348` saves terminal event, request and Outcome before committing at line 1356. The reconciler separately calls queue enqueue at `AgentTaskLandNotificationService.cs:143` with `deliverIfIdle:false`. | Observe and fault both durable handoffs independently. |
| A queue insert or Sent flag resolves the refusal. | `AgentTaskLandNotificationService.cs:240` requires attempts; lines 278-293 use `LandNoteReceipt`; lines 324-329 write confirmation and the matching sequence. | Check unconfirmed state before reconciliation even after a real recipient submission. Never assign ConfirmedAt in a positive witness. |
| Only UserPrompt can ever confirm a live Outcome. | `server/Application/Services/LandNoteReceipt.cs:21` also accepts complete QueuedUserPrompt for non-legacy Held/Aged/Conflict/Outcome. `AgentTaskLandReceiptTests.cs:209` pins this behavior. | Require UserPrompt for the new witness, but preserve existing QueuedUserPrompt compatibility. A queued prompt is not a universal negative control. |
| An arbitrary matching prompt can confirm. | `LandNoteReceipt.cs:38` scopes destination, lines 43-48 enforce the sequence/time floor, and line 74 requires both identity and completeness. | Negative cases must keep other receipt conditions valid so the intended guard is reachable. |
| Recovery needs another note or input attempt. | `AgentTaskLandNotificationService.cs:78` reattaches the row by SourceLandNotificationId; `SessionMessageQueueService.cs:865` invokes the post-insert seam after queue save; the receipt-before-save seam is at notifier line 326. | Preserve task/request/event/note/queue identity; rebuild services, then recover from stored evidence without duplicate delivery. |
| The new tests need the existing native PTY suite. | `SessionMessageQueuePtyIntegrationTests.cs:29` explicitly requires Windows ConPTY and staged fakeclaude.exe; lines 127-137 join file and destination UserPrompt evidence. | It is a distinct native transport proof. Do not silently put Windows-only cases in an Any checkpoint or claim native coverage from Bridge. |

## Decisions

- **D-1 — Seed history, exercise the live producer.** Seed one historical event
  with LandRequestId null, Type LandRefused, and At three days before the test read.
  Use a Succeeded Worktree task with ReplyTo Session and the fixture caller as
  ParentSessionId. Call the real RequestAsync, then FailAsync with a bounded test
  IOException before any publication. Read the produced event and Outcome from a
  fresh context. Reject directly inserting the live notification or using
  AgentTaskLandReceiptTests.SeedAsync for the positive chain: those bypass the
  producer obligation. Reject LandOutcomeDeliveryHarness.LandAsync(false): a
  published outcome satisfies the alternative attention resolution path.
- **D-2 — Compose a narrow fixture without changing shared defaults.** Add
  LandRefusalDeliveryHarness in TestHelpers, composing LandingProtocolHarness and
  BridgeQueueHarness and using the existing LandDeliveryBoundary hooks. Follow
  LandOutcomeDeliveryHarness for notifier/wakeup composition, but own replaceable
  Bridge instances and fresh CompletionNoteFlushQueue instances for restart tests.
  No changes to shared Bridge, fake adapter, landing harness, or production classes
  are required by this plan. Reject copying their service graphs.
- **D-3 — Use the real queue with a declared provider substitute.** Positive
  prompt records come only from FakeAgentProtocolAdapter.OnSubmitted, using the
  actual submitted bytes. The queue, runtime, database, notifier, matcher and
  attention reader remain real. Model a silent recipient; emit UserPrompt when
  idle and QueuedUserPrompt when busy. Reject hard-coded expected-body insertion
  as recipient evidence. This proves application delivery composition, not native
  terminal framing, a running provider, or transcript-file ingestion.
- **D-4 — Keep the new ordinary lane portable.** All proposed checkpoints are
  **Any / PostgreSQL integration / controlled provider**. No new PTY behavior is
  being changed. Keep the existing native PTY suite as a separately owned transport
  guard; no new native checkpoint is proposed. GET /api/runner-defaults (revision 2)
  and GET /api/session-runners were read with the task-token header on 2026-10-12;
  the default placement was available and eligible, and both Linux and Windows
  capacity were advertised. Re-read before dispatch. Omit -Runner and -Platform;
  use -Platform Any only to remove an inherited pin. No fleet address is part of
  the plan.
- **D-5 — Real persistence boundaries, explicit reconstruction.** Each fault must
  report that it fired and expose the database state at the cut. A service restart
  means disposing the old provider/DbContext and recreating queue, runtime,
  notifier and wakeup queue against the same schema and session. It is not an
  OS-process crash claim. Reject merely clearing ChangeTracker as restart proof.
- **D-6 — No production semantic changes.** In particular, preserve legacy
  no-replay, complete QueuedUserPrompt acceptance for live Outcomes, receipt
  ordering, delivery attempt limits, and the publication alternative. A discovered
  production defect goes back into an explicit Code scope/plan amendment with a
  reproducer; do not weaken assertions or broaden receipt acceptance.
- **D-7 — Shared harness assessment for CARD-1203 / CARD-1190 D4.** Reusing the
  already-landed Bridge attachment, adapter callback and persistence-fault hooks
  is sensible now. Land-specific history and attention assertions are not useful
  to the ready-gate producer. Do not extract a new generic cross-card framework or
  modify AgentSessionLaunchFailureTests in CARD-1196. CARD-1203 can reuse the small
  recipient/restart recipe after it lands; its own producer and any native PTY
  scope remain independent. CARD-1190 was InProgress and CARD-1203 Backlog at this
  inspection; neither branch is an implementation dependency.
- **D-8 — Separate verification stage.** TestDesign audits the proposed guards,
  mutation reachability and checkpoint roster before Code. No operator decision
  is needed to write this plan under D-1 through D-7. Code will run ordinary V/R;
  a separate Review follows, then Land and commissioned SourceLanding Mutation.

## Implementation slices and ownership

| Slice | Owned files | Work and completion boundary | Tests / checkpoint |
|---|---|---|---|
| S1 | New `tests/Antiphon.Tests/TestHelpers/LandRefusalDeliveryHarness.cs`; new `tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs` | Compose isolated history, real producer, replaceable recipient, notifier, wakeup drain and real attention read. Add eligible and busy witnesses. Commit and push before verification. | V-1, V-2; CP-1. |
| S2 | The same two files; comment only at `tests/Antiphon.Tests/Application/AttentionFeedNoiseTests.cs:81` | Add enqueue/restart/receipt-save faults and the negative cases below. Replace the coverage-gap comment with a precise cross-reference once the witness passes, retaining the projection tests. Commit and push before verification. | V-3 through V-9; CP-2 through CP-4. |

No generated docs/cards files, production code, migrations, shared harness defaults,
native PTY tests, or ready-gate source are owned by these slices. If an implementation
needs one, amend scope and checkpoints explicitly before editing it. Keep the plan
and any later TestDesign amendment at this single artifact path.

### Fixture construction and observable assertions

1. Initialize LandingProtocolHarness and put every DbContext on its isolated schema.
   Create BridgeQueueHarness on that schema with AlwaysOn true and
   PreserveDatabaseOnDispose true. Connect Land.Messages to the real queue. Seed
   task routing and the old historical event before RequestAsync captures routing.
2. Seed a harmless completed transcript turn to establish a non-null baseline.
   The land body itself is never seeded as a positive transcript. Store the old
   refusal ID and assert the exact `legacy-land:<event-id:N>` row exists via
   AttentionServiceTests.BuildService with the fixture DbContext and runner,
   followed by real GetAsync. Use a fresh read context each time and assert reads
   stage no writes. Do not infer resolution from an empty whole attention feed.
3. Request and fail through the real land service. Assert exactly one live
   terminal LandRefused, non-null LandRequestId, one non-legacy Outcome, same task
   and request, immutable destination/body/digest, and no ConfirmedAt. Assert no
   event for this task has Landed/AlreadyPresent publication, no notification is
   sourced from the historical refusal, and ReplyTo remains Session. The source
   event must be newer than the old refusal and different from it.
4. Reconcile using a new scope. Assert one queue row keyed by
   SourceLandNotificationId, correct SourceTaskId/destination/digest/body and the
   note's QueueMessageId backlink. Drain the actual CompletionNoteFlushQueue
   signal into Queue.FlushIfIdleAsync, calling Complete in finally. An idle test
   must fail when no wakeup is received; do not substitute a direct flush to
   rescue a missing signal. A bounded five-second signal wait is a test watchdog.
5. For the busy case, persist activity after the baseline end; assert production
   IsWorkingAsync true. Consume the wakeup and assert Pending, zero attempts, zero
   land submissions and the historical row still present. Persist a real TurnEnd
   transcript record, assert no longer working, then call real OnTurnEndAsync.
   Merely invoking the turn-end handler while leaving the transcript busy would
   manufacture eligibility and is not the desired witness.
6. After the queue submits, assert exactly one complete matching UserPrompt on the
   destination, using both PromptSubmissionMatch identity and completeness; assert
   positive DeliveryAttempts, a valid delivery floor, unchanged durable identities
   and exactly one adapter submission across all fixture incarnations. Before
   calling the notifier again, the notification and old attention condition must
   still be unconfirmed/present. Only ReconcileAsync may write ConfirmedAt and
   ConfirmingPromptSequence. Then read attention again and require the exact old
   condition to disappear. Read and reconcile a second time to prove no replay.
7. For recovery, retain schema, session, agent, identities and submitted-body
   observations across provider disposal. Reattach the same session to a new
   Bridge instance; reset no queue state or confirmation fields. Record every
   Bridge TempRoot: PreserveDatabaseOnDispose also preserves its directory.
   Dispose all contexts/providers before removing only owned scratch directories
   and disposing the schema. Do not leave callback work running at teardown.

Queue timers use TimeProvider.System and existing compressed harness settings.
An independent fake clock may drive notifier backoff and attention reads; advance
it to the stored NextAttemptAt instead of sleeping or rewriting due state. An
attention read must be at or after relevant event times. If live receipt-attention
rows are also asserted, read past LandWarningSeconds; they are age-gated, unlike
the historical refusal. Do not pass an unadvanced fake clock to queue delays.

No production input-wait session or release policy is added. These are in-process
fixture sessions owned until deterministic teardown. Busy work resumes delivery
on its recorded TurnEnd; this is not a session-release deadline. With parking
disabled there is no automatic release deadline for a production input wait
(CARD-1083).

## Verification design

Proposed, for the separate TestDesign audit. Tests belong to
`LandRefusalDeliveryWitnessTests`, Category Integration, with no native child
processes. Counts below are TUnit argument-expanded executions. Avoid automatic
hosted pumps racing deliberate cuts; exercise the production worker entry points
and the real wakeup channel explicitly. This does not qualify hosted cadence.

### Delivery inventory

| Handoff | Producer / destination | Persistence and durable join | Recovery / final observable |
|---|---|---|---|
| H0: terminal outcome | AgentTaskLandService.FailAsync -> caller notification obligation | TaskId -> RequestId -> terminal SourceEventId -> notification Id, committed with the terminal event | Reconstruct notifier after the terminal commit; same note eventually yields recipient UserPrompt and clears the old attention key. |
| H1: obligation -> queue | AgentTaskLandNotificationService.ReconcileAsync -> snapshotted ParentSessionId | Real EnqueueAsync, key SourceLandNotificationId; queue body/digest and SourceTaskId | Before-enqueue failure and actual queue-save failure leave no queue row or input; due retry creates one. |
| H2: queue commit -> note link | Real queue -> reconciler | Queue row commits before callback and before notification QueueMessageId save | Fault queue-inserted, reopen services, recover the existing row by its key. Never create a replacement identity. |
| H3: saved link -> wakeup | Reconciler -> CompletionNoteFlushQueue -> real queue | Linked note and Pending row survive; wakeup is memory only | Fault queue-committed-before-wakeup, discard the old signal queue, invoke real FlushStrandedQueuesAsync on reconstructed idle AlwaysOn recipient. |
| H4: submitted bytes -> receipt save | Real queue/runtime -> simulated recipient -> notifier | UserPrompt after attempt floor; ConfirmedAt and ConfirmingPromptSequence saved separately | Fault receipt-before-save, then reconstruct and confirm from the retained prompt, without retyping. |
| H5: receipt -> attention | AttentionService.GetAsync reads the same stored task/event/note | Older historical SourceEventId is distinct; later same-task Outcome has real confirmation | Exact old legacy-land key disappears while an unrelated/newer refusal stays. |

Substitutes: controlled Git cannot prove Git/network/publication behavior; fake
adapter cannot prove PTY or provider ingestion; explicit pumps cannot prove hosted
scheduling cadence. No request, queue row, event, Sent status, transport ack, or
in-memory counter substitutes for the positive destination UserPrompt.

### Proves it works now and guards the regression

All methods below are proposed exact names. Every positive scenario includes the
common producer/receipt/attention assertions above. Negative fixture perturbations
are explicitly distinguished from evidence of successful delivery.

| ID | Proposed method / executions | Case -> decisive oracle | Mutation -> required red |
|---|---|---|---|
| V-1 / R-1 | `C1196_Eligible_recipient_receipt_resolves_older_refusal` / 1 | Idle caller: production refusal -> wakeup -> one complete UserPrompt -> real reconciliation -> historical key absent. Before reconciliation, key remains. | PC-1 removes later-Outcome resolution: the final historical-key-absent assertion fails despite a confirmed note. PC-2 omits producer Outcome: exactly-one-produced-note fails. PC-3 drops new-row wakeup: bounded signal assertion fails. |
| V-2 | `C1196_Busy_recipient_waits_for_turn_end_before_resolution` / 1 | Busy caller remains Pending/unattempted/unconfirmed, then recorded TurnEnd permits the same body and identity to complete the whole chain. | PC-4 bypasses the working gate in FlushIfIdleAsync: zero-submissions-before-turn-end fails. |
| V-3 | `C1196_Enqueue_failure_recovers_the_same_obligation` / 2 Arguments: before-enqueue, queue-save | Arm a one-shot before-enqueue IOException or a SaveChangesInterceptor scoped to adding the keyed SessionQueuedMessage. Assert fault fired, no queue row or input, same note RetryPending and historical key present. Advance notifier clock to persisted due, reconstruct and complete delivery once. | PC-5 makes RetryPending notes return without recovery: after-retry queue/receipt assertion fails in both variants. |
| V-4 | `C1196_Crash_after_queue_insert_recovers_the_existing_key` / 1 | Throw a tagged OperationCanceledException at queue-inserted to escape the reconciler catch. Persisted note has null link, one committed keyed row, no input. Reconstruct; same queue ID links, delivers once, confirms and clears history. | PC-6 returns when the recovery lookup finds the existing row, before saving the link: same-queue-ID backlink assertion fails. |
| V-5 | `C1196_Lost_wakeup_is_recovered_by_the_stranded_queue` / 1 | Throw at queue-committed-before-wakeup. Link exists, attempts zero, no input. Reconstruct with an empty wakeup queue; call real stranded sweep. Same row produces one receipt, confirmation and attention resolution. | PC-7 disables stranded delivery: one-complete-recipient-prompt assertion fails; direct FlushSession/SendNow fallback is prohibited in this case. |
| V-6 | `C1196_Receipt_save_crash_recovers_without_retyping` / 1 | Real delivery first; tagged cancellation at receipt-before-save leaves complete prompt but no persisted confirmation and historical key present. Reconstruct, reconcile twice, confirm the same prompt sequence, clear history with still one submission. | PC-8 prevents persistence of a matched receipt: confirmed-sequence/state assertion after recovery fails. |
| V-7 | `C1196_False_receipt_evidence_keeps_the_refusal_open` / 5 Arguments: sent-without-prompt, head-only, wrong-session, at-baseline, no-attempt | Exercise real producer and queue. For sent-without-prompt, suppress only the provider transcript callback and explicitly present Sent/Delivered metadata if needed; for head-only record submitted head only with identity intact; for wrong-session route submitted bytes to a second owned session. For at-baseline/no-attempt, first deliver real bytes, then deliberately perturb only baseline to the real prompt sequence / attempts to zero before notifier read. All require null confirmation and historical key present. These edits are negative guard fixtures, never positive delivery proof. | PC-9 treats Sent as receipt; PC-10 drops completeness; PC-11 drops destination restriction; PC-12 changes strict sequence > to >=; PC-13 bypasses attempts > 0. Each must fail the null-confirmation assertion in its corresponding argument. |
| V-8 | `C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` / 2 Arguments: other-task, newer-refusal | Confirm a genuinely delivered Outcome. In other-task, also seed a second task's older historical refusal. In newer-refusal, seed a historical refusal for the same task strictly later than the produced event before reconciliation. Assert the matching protected historical key survives. The positive target in other-task still clears. | PC-14 removes same-task matching; PC-15 removes strict event-time ordering from the later-Outcome query. Each wrongly removes the protected key in its corresponding argument. |
| V-9 | `C1196_Unlinked_legacy_note_never_submits` / 1 | Seed a separate historical IsLegacy note, AwaitingReceipt, null QueueMessageId, source old refusal; no terminal State early return may mask the legacy guard. Reconcile twice; zero keyed rows/inputs/confirmation, historical key remains. This is a historical negative fixture, not the live producer witness. | PC-16 removes the IsLegacy/no-link early return: zero-keyed-rows assertion fails. |

V-7 must assert its actual body is long enough for a head-only sample which passes
identity and fails completeness; do not let a wrong identity mask PC-10. For
at-baseline retain valid timestamps, and for no-attempt retain a valid floor and
the complete prompt. Guard fixtures must observe the genuine queue submission
before perturbing persisted metadata. Do not retype uncertain deliveries just to
obtain a positive tail in a negative test.

R-2: retain the ten existing `AttentionFeedNoiseTests` methods, including age-out,
mixed-history grouping and publication resolution. R-3: retain all six executions
in `AgentTaskLandEnqueueTests` for the existing shared-component behavior. R-4:
run `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` (15 arguments), which
includes both positive complete QueuedUserPrompt and false-receipt compatibility.
These are existing tests, not new producer-to-attention evidence.

### Guard inventory and proposed positive controls

One guard per PC; TestDesign must review the concrete compiling edits and bound
method filters. Target locations use the source commit above.

| Guard / PC | Production target and proposed defect | Detecting method filter |
|---|---|---|
| G-1 / PC-1: later confirmed Outcome resolves history | `AttentionService.cs:1686`, replace only the final later-Outcome query with false, preserving own-receipt and publication alternatives | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` |
| G-2 / PC-2: terminal producer owes an Outcome | `AgentTaskLandService.cs:1351`, omit AddNotification in terminal completion | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` |
| G-3 / PC-3: eligible delivery gets a wakeup | `AgentTaskLandNotificationService.cs:161`, omit TryEnqueue after new linked row | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` |
| G-4 / PC-4: busy caller waits | `SessionMessageQueueService.cs:1815`, admit FlushIfIdleAsync while working | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Busy_recipient_waits_for_turn_end_before_resolution` |
| G-5 / PC-5: failed enqueue is recoverable | `AgentTaskLandNotificationService.cs:48`, add RetryPending to terminal early-return states | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Enqueue_failure_recovers_the_same_obligation` |
| G-6 / PC-6: committed keyed row reattaches | `AgentTaskLandNotificationService.cs:82`, return upon existing-row lookup instead of linking it | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Crash_after_queue_insert_recovers_the_existing_key` |
| G-7 / PC-7: lost wakeup has durable recovery | `SessionMessageQueueService.cs:1599`, replace stranded-sweep body with return 0 | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Lost_wakeup_is_recovered_by_the_stranded_queue` |
| G-8 / PC-8: matched receipt persists after restart | `AgentTaskLandNotificationService.cs:327`, return immediately after the receipt-before-save boundary and before assigning confirmation; keep the boundary call so the first crash remains reachable | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_save_crash_recovers_without_retyping` |
| G-9 / PC-9: status is not receipt | `AgentTaskLandNotificationService.cs:240`, add a Sent shortcut saving Confirmed state/time without scanning a prompt | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` |
| G-10 / PC-10: complete body required | `LandNoteReceipt.cs:74`, remove IsCompleteIn conjunct, retain IsConfirmedBy | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` |
| G-11 / PC-11: destination required | `LandNoteReceipt.cs:38`, remove AgentSessionId predicate only | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` |
| G-12 / PC-12: strict attempt floor | `LandNoteReceipt.cs:43`, replace Sequence > floor with >= floor | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` |
| G-13 / PC-13: attempt required | `AgentTaskLandNotificationService.cs:240`, admit zero-attempt row into receipt scan | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` |
| G-14 / PC-14: resolution stays task-scoped | `AttentionService.cs:1687`, remove TaskId equality only from later-Outcome query | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` |
| G-15 / PC-15: resolution requires later source | `AttentionService.cs:1691`, remove e.At > source.At only | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` |
| G-16 / PC-16: historical note cannot submit | `AgentTaskLandNotificationService.cs:53`, remove legacy/no-link early return only | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Unlinked_legacy_note_never_submits` |

Proposed census: guards 16, mapped 16, missing 0, duplicate guard-to-PC maps 0.
Distinct PCs may detect through the same method; run them separately when they
touch the same file/method. Each PC needs baseline green, a compiling mutation,
the stated assertion red, restored source and restored green. A build/fixture
failure, a test timeout or zero executions is not red evidence. Parameterized
method filters run that method's arguments; record which intended argument fails.
No PCs have been executed in this Plan dispatch. SourceLanding Mutation comes
after implementation land and must retain its evidence externally.

### Out of scope

Real provider/PTY qualification, remote runner replay, hosted worker timing, receipt
cache performance, canceled/parked/destination-loss matrices, notification spill,
publication/cleanup safety, source refusal classification, and every ready-gate
producer change remain with their existing suites/cards. This plan neither changes
those contracts nor claims its small fixture proves them. The existing receipt
matrix supplies the other compatibility controls; TestDesign should not expand
this into an assembly or whole-Unit run.

### Cost

Estimates, not measured evidence: authoring and fixture work 60-90 minutes; ordinary
Code V/R floor **27 minutes** (8 + 9 + 5 + 5 below), so Code budget 87-117 minutes
before slot waits. Separate ordinary Review uses the final-source rows CP-2 through
CP-4, estimated 19 minutes plus inspection; earlier S1-only evidence does not
qualify the final source. SourceLanding Mutation estimate: setup/baseline 10 minutes
plus 16 independent red/restored-green cycles at 6 minutes = **106 minutes**, plus
15 minutes reporting/restoration = **121 minutes**. Combined Code verification
and Mutation floor is **148 minutes**, excluding authoring and Review. TestDesign
must update these estimates if a native lane or additional controls are added.

Reusing the landed controlled harnesses avoids new native-process setup. No
measured speedup is claimed. Each checkpoint builds once; no broad discovery run
or repeated unchanged full-suite run is part of the ordinary scope.

### Checkpoints

Proposed closed list for TestDesign to finalize. Every row names the **Any /
PostgreSQL integration / controlled-provider lane** in Group. The wildcards are
intentional TUnit discovery syntax. CP-2 expects only the nine proposed methods
(15 argument-expanded results), CP-3 ten AttentionFeedNoise and six land-enqueue
results, and CP-4 the existing 15 receipt-matrix results. All require zero skipped.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1196-s1/` | any-pg-eligible-busy | `/*/Antiphon.Tests.Application/LandRefusalDeliveryWitnessTests/(C1196_Eligible_recipient_receipt_resolves_older_refusal*)\|(C1196_Busy_recipient_waits_for_turn_end_before_resolution*)` | V-1, V-2, R-1 | all 2 listed results, 0 failed/skipped | 2 | 8 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-witness/` | any-pg-refusal-chain | `/*/Antiphon.Tests.Application/LandRefusalDeliveryWitnessTests*/*` | V-1 through V-9, R-1 | all 15 listed results, 0 failed/skipped | 15 | 9 | true |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-regression/` | any-pg-attention-enqueue | `/*/Antiphon.Tests.Application/(AttentionFeedNoiseTests*)\|(AgentTaskLandEnqueueTests*)/*` | R-2, R-3 | all 16 existing results, 0 failed/skipped | 16 | 5 | true |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-receipts/` | any-pg-receipt-compatibility | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptTests/C467_V11_RejectFalseReceipts*` | R-4 | all 15 existing argument results, 0 failed/skipped | 15 | 5 | true |

## Execution and handoff

Before Code, TestDesign should finalize the concrete mutation edits, verify each
negative case isolates its guard, and confirm the 9-method/15-result proposed
roster and existing 16/15 regression counts against then-current source. Preserve
the no-publication control and the declared distinction between provider substitute
and native recipient evidence. No design question requires operator approval.

Code commits/pushes each slice before running its checkpoint group through
`tools/Antiphon.Checkpoints` (`run --plan <this-file> --rows CP-1` for S1,
`--rows CP-2,CP-3,CP-4` for S2), binding `--expected-source-sha` to the exact
committed source. Use the documented build-slot gate for any driver/bootstrap,
and await checkpoint completion, continuing wait after exit 75. Record every
unedited CHECKPOINT line, executed roster/count, source receipt and rerun reason.
Do not edit source under an active run or retry a slot timeout unleased. Remove
all owned alternate bin directories through the documented cleanup path.

Code/Review run `scripts/check-evidence-diff.ps1` over the full assigned task range.
Generated receipts/TRX/logs remain ignored. Confirm inherited failures only by
method-scoped baseline reproduction; do not loosen timeouts/assertions. Ordinary
Review, land, linked post-land verification commissioning and SourceLanding
Mutation remain separate stages. **Post-land Mutation was NOT run for this plan.**
