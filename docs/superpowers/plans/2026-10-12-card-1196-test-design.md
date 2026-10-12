# CARD-1196: verification appendix for the land-refusal delivery witness

Date: 2026-10-12. TestDesign source: `019fa6fb1111cf08a9d7e0de435d8404a8a97395`.
Card: Antiphon CARD-1196, `d5ed0eae-27dc-489b-beb4-5e64347f3bae`.
The card was read through the task-token-bound API. No token is stored here.

This is the verification appendix to the [landed fix design](2026-10-12-card-1196-land-refusal-delivery-witness-plan.md).
Its D-1 through D-8 and S1/S2 file ownership remain the implementation design.
This separate, commissioned artifact freezes the tests, mutation reachability and
checkpoint roster; use this table instead of the plan's proposed table. It adds
verification cases within the same two new test/helper files, without production
changes or dependencies on CARD-1190's unlanded ready-gate fixture.

Design only: no build, test, listing driver or mutation was run. Method names for
new files below are implementation obligations, not discovered tests. Code must
bind them to implemented bodies and argument-expanded results. A missing method,
unbound payload/case/oracle, or unreachable mutation remains **binding required**
after Code; an enqueue or a passing count cannot close it. Review judges that
binding before land. Post-land Mutation is paused by the commissioning brief:
PC-1 through PC-30 are specified and pending, not executed or waived.

## Verification design

### Inspection

Paths below are relative to the repository. These are source bodies read, rather
than search hits or assumptions from other task reports.

| Bodies read | Boundaries -> verification IDs or exclusion |
|---|---|
| `tests/Antiphon.Tests/Application/AttentionFeedNoiseTests.cs`: ten test bodies and helpers; `AttentionServiceTests.cs`: BuildService, Scenario's session/task/history/confirmation helpers | Existing projection-only evidence -> R-2. New tests use BuildService with an explicit isolated DbContext; do not use its default-store Scenario or ConfirmLegacyReceiptAsync for live evidence. |
| `TestHelpers/LandOutcomeDeliveryHarness.cs`: initialization, LandAsync, notifier composition, PumpFlushAsync, receipt assertions, disposal; `Application/AgentTaskLandEnqueueTests.cs`: five methods/six executions | Nearest fixture for both new files -> V-1 through V-6, R-3. Its successful LandAsync(false), default TurnEnd callback, MakeDueAsync timestamp rewrite and fixed Caller instance are not the new refusal/restart recipe. |
| `TestHelpers/LandingProtocolHarness.cs`: Initialize/Seed/CreateContext/CreateLand/Request/Fail/RestartServices, SaveFault and TransactionFault | Real terminal producer with controlled Git -> V-1; producer survives service replacement. Harness CreateLand does not forward a LandDeliveryBoundary; use the completed FailAsync return as the H0 cut, not an imaginary injected terminal hook. |
| `TestHelpers/BridgeQueueHarness.cs`: CreateAsync service graph, attached/new session branches, transcript insertion, DisposeAsync; `Agents/FakeAgentProtocolAdapter.cs`: OnSubmitted, SendInputAsync, composer/submission lists, disposal | Queue/runtime/DB plus callback recipient -> V-1 through V-11. PreserveDatabaseOnDispose also preserves TempRoot: fixture owns cleanup of each incarnation. |
| `Application/StandingBootQueueWitnessTests.cs`: OpenQueueAsync, FakeAdapterView and queued/recipient assertions | Landed silent-recipient recipe -> V-1/V-2/V-5. This file does use the real queue; no ready-gate branch dependency. |
| `Application/AgentTaskLandReceiptTests.cs`: C467_V11_RejectFalseReceipts, SeedAsync, catch-up/save recovery and retention bodies | Fifteen-case compatibility roster -> R-4. SeedAsync is a seeded-notification substitute and is excluded from the new positive producer chain. |
| `Application/AgentTaskLandNotificationPersistenceTests.cs`: approval transaction/rollback and concurrent settlement bodies; `Application/SessionMessageQueuePtyIntegrationTests.cs`: Queued_message_submits_through_the_real_runtime_runner_pty_path | Existing transaction rollback and native transport ownership; excluded from this ordinary roster. The latter needs native ConPTY and a file transcript; the callback fixture cannot claim that evidence. |
| `server/Application/Services/AgentTaskLandService.cs`: RequestAsync entry, FailRequestAsync/PersistFailureAsync, CompleteTerminalLockedAsync, AddNotification; `LandNotificationPayload.cs`: Create | H0 producer identity/no publication -> V-1, G-2/G-25. Git publication and rollback internals remain excluded. |
| `AgentTaskLandNotificationService.cs`: ReconcileAsync, Actionable, RetrySeconds, receipt-scan context/stamp; `LandDeliveryBoundary.cs`; `CompletionNoteWork.cs`: CompletionNoteFlushQueue | H1/H2/H3/H5 -> V-3/V-4/V-5/V-6/V-9/V-11. New-row and recovered-row wakeups are independent guards. Hosted worker cadence and overflow scheduling are excluded. |
| `SessionMessageQueueService.cs`: keyed enqueue/insert, OnTurnEndAsync, FlushIfIdleAsync, FlushStrandedQueuesAsync, attempt claim and delivery/verdict branch, RevertRunAsync, interrupted recovery, IsWorkingAsync | H3/H4 -> V-1/V-2/V-5/V-6/V-10. Before-typing exceptions are caught and reverted; before-verdict exceptions escape. Do not label both cuts as a hard crash. |
| `LandNoteReceipt.cs`; `ReceiptScanTarget.cs`; `src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs`; `AttentionService.cs`: historical grouping/eligibility and LandRefusalResolvedAsync | Prompt kind/session/sequence/time/completeness and receipt-to-history query -> V-7/V-8/R-4. Keep the keyed queue destination unchanged in wrong-session receipt tests: the existing whitelist can deliberately follow a changed keyed destination. |
| `TestHelpers/ControlledTimeProvider.cs`; `docs/testing-and-build.md`: checkpoint schema/runner, source qualification, build slots, async delivery and restoration; `docs/session-runtime-invariants.md`: land receipt contract; `docs/project-context.md`; `docs/orchestration-loop.md`: stage and post-land verification contract; `docs/ops-http.md`: bound API reads | Clock, isolated schema, execution and evidence constraints below. No new production session/release deadline. |

All abbreviated test paths in this appendix are under `tests/Antiphon.Tests/`;
abbreviated service paths are under `server/Application/Services/`.

### Delivery inventory

The durable chain is task T -> request Q -> live source event E -> notification N
-> keyed queue row M -> recipient session S and UserPrompt sequence P. The old
historical event H is distinct from E and has no LandRequestId. The immutable
notification body B and digest D travel with N/M. Capture these values from fresh
database reads, not a helper's expected identifiers. H's attention key is
`legacy-land:<H:N>`. An unrelated/newer refusal has its own key.

| Handoff | Producer -> destination; persistence boundary | Recovery, recipient evidence and named controls |
|---|---|---|
| H0 | Real RequestAsync then FailAsync -> snapshotted caller S. Terminal E, Q.TerminalEventId and non-legacy Outcome N commit in CompleteTerminalLockedAsync. | V-1 immediate/restart-after-terminal: cut after FailAsync returns, replace services before first reconcile, then complete the same N's delivery. No second Fail/Request to manufacture recovery. PC-2 protects the obligation; PC-25 protects the no-publication exclusion. |
| H1 | ReconcileAsync -> real EnqueueAsync, WhenIdle and deliverIfIdle:false. N is durable before a queue row exists. | V-3 before-enqueue IOException and real queue SaveChanges failure: no M/input; RetryPending and a future due time; retry reconstructs services and completes one UserPrompt. PC-5/PC-29 cover retry and due gating; PC-23 covers deferred delivery. |
| H2 | Queue SaveChanges commits M with SourceLandNotificationId=N before the note backlink saves. | V-4 queue-inserted tagged cancellation: one M, N.QueueMessageId null. Reconstruct, reattach M, consume the recovered-row wakeup, obtain P and confirm. PC-6/PC-17. V-11 mismatch/unknown identity withholds linkage; PC-27/PC-28. |
| H3 | Notifier saves N.QueueMessageId=M, then signals the in-memory CompletionNoteFlushQueue -> real queue. | V-1 consumes the actual signal (PC-3). V-2 busy recipient waits for recorded TurnEnd (PC-4). V-5 loses signal at queue-committed-before-wakeup; fresh empty signal queue, real stranded sweep, idle and busy variants (PC-7/PC-18). |
| H4a | Queue commits attempt count/baseline/start before adapter input. Callback independently observes that stored claim. | V-1 captures claim at input (PC-24). V-10 queue-before-typing IOException is caught transport failure: Pending M with attempt retained and no input, then service reconstruction and real stranded retry reaches one UserPrompt (PC-30). This is exception/restart recovery, not abrupt worker death. |
| H4b | Runtime -> declared provider substitute; actual submitted bytes become one complete UserPrompt on S. Queue verdict save follows that receipt. | V-6 queue-before-verdict tagged cancellation leaves Sent/null-verdict M, positive attempt and P. Reconstruct notifier/queue/runtime and confirm from P without retyping (PC-8/PC-26). The queue status/verdict is not the receipt. |
| H5 | ReconcileAsync catches up and scans S, matches B above attempt floor, then saves N.ConfirmedAt and ConfirmingPromptSequence=P. | V-6 receipt-before-save tagged cancellation leaves P durable but N unconfirmed. Reconstruct and reconcile twice; same P, one submission total (PC-8/PC-26). V-7/R-4 reject the named false evidence (PC-9 through PC-13, PC-21/PC-22). |
| H6 | Real AttentionService.GetAsync reads T/H/E/N after stored confirmation. No delivery write occurs in the reader. | V-1 resolves the exact H key only after reconciliation (PC-1/PC-19); V-8 protects other-task, newer/equal-time and non-Outcome conditions (PC-14/PC-15/PC-20). V-9 protects historical no-replay (PC-16). |

**Substitutes and limits.** ControlledLandingGit replaces Git/network/verifier
behavior; it proves no real publication. FakeAgentProtocolAdapter replaces the
provider and terminal. Its awaited OnSubmitted callback records the bytes the
real queue submitted; it cannot prove native terminal framing, a running model,
remote transport or transcript-file ingestion. Explicit calls to worker entry
points replace hosted scheduling and cannot prove cadence. Service reconstruction
replaces in-process services against retained storage; it is not OS-process death
or database crash-durability qualification. The new tests stop at recipient
transcript evidence plus real confirmation and attention, not at transport ack,
request, event, queue insertion, Sent/Delivered flag or submission counter.

**Recipe F (positive producer/recipient).** Compose LandRefusalDeliveryHarness
from the landed LandingProtocolHarness and replaceable BridgeQueueHarness. Use
one isolated schema for all contexts, AlwaysOn=true, PreserveDatabaseOnDispose=true,
and attach the same S/agent on reconstruction. Seed one harmless completed turn
to establish a non-null baseline and H at test-now minus three days. Set the
task's ReplyTo=Session and ParentSessionId=S before RequestAsync snapshots them.
FailAsync receives a bounded fixture IOException before any land RunAsync.
Fresh reads assert one live terminal LandRefused with request Q and one live
Outcome N, E!=H and E.At>H.At, no notification sourced from H, and ReplyTo=Session.
Assert no Landed/AlreadyPresent publication event for T. Do not seed N or assign
positive confirmation/attempt fields.

Replace Bridge's default OnSubmitted callback: collect submitted bytes and an
independent fresh-context snapshot of M's durable attempt/floor, then insert those
bytes as UserPrompt when the production working query says idle, QueuedUserPrompt
when busy. Add no assistant response or TurnEnd. Retain the observations across
fixture incarnations. Assertions on captured observations run outside callbacks
so production exception handling cannot hide a failing test assertion. Expected
receipt uses both IsConfirmedBy and IsCompleteIn; the actual persisted text must
also equal the callback's submitted text. Expected outcome tables are hand-written,
not computed using ReconcileAsync or LandRefusalResolvedAsync.

Evidence order for F:

1. H's exact key is present; producer persists E/Q/N with no publication and null
   confirmation. Save the identity/body/digest tuple.
2. Reconcile in a fresh scope. Observe one M keyed to N, correct S/T/B/D and the
   backlink, zero attempts and zero submitted bodies **before** consuming wakeup.
3. Await the actual signal with a five-second timer watchdog, call FlushIfIdleAsync,
   and call Flushes.Complete in finally. Return a boolean only on that watchdog
   expiring and assert `C1196:wakeup-received`; tagged fault cancellations must
   propagate. Unlike the nearest helper's broad cancellation catch, distinguish
   the watchdog token from a cut. Do not rescue a missing signal with a direct flush.
4. Fresh transcript query yields one complete UserPrompt P on S after M's stored
   baseline; M has positive attempts and start time. Check the captured claim was
   already durable at submission. N is still unconfirmed and H still present.
5. Only a fresh ReconcileAsync writes Confirmed state/time/sequence=P. Real
   AttentionServiceTests.BuildService(runner, db: isolatedDb, timeProvider: clock)
   and GetAsync(includeProgressProbe:false) omit H's exact key. A second reconcile
   and read retain the same receipt and one submission. Assert attention reads
   leave ChangeTracker.HasChanges false. Do not assert an empty entire feed.

**Recipe B (busy).** After the baseline TurnEnd, persist AssistantText and prove
production IsWorkingAsync=true. Consume wakeup; check Pending/attempts=0, no
submitted body, null confirmation and H present. Persist a TurnEnd/end_turn,
prove IsWorkingAsync=false, then call OnTurnEndAsync and complete F steps 4-5.
A turn-end handler call alone cannot manufacture eligibility.

**Recipe X (restart/cut).** Arm a one-shot fault with recorded boundary/task/key
and firing count. Await the owned operation to finish/throw; inspect the durable
state in a fresh context before teardown. Dispose notifier provider/scopes and
Bridge/runtime; restart Land.Services as applicable; reattach S/agent with a new
Bridge, notifier provider and CompletionNoteFlushQueue. Reconnect Land.Messages.
Do not reset note/queue state, IDs, floors or confirmation. Keep all owned
TempRoots until providers/callbacks are done, then remove those directories and
dispose the schema. This ownership also applies to negative fixtures.

Use a separate ControlledTimeProvider, initialized near the queue's System UTC,
for notifier due timestamps and attention. Advance it forward to persisted
NextAttemptAt; do not rewrite due timestamps or sleep for backoff. Queue timers
remain TimeProvider.System with Bridge's compressed defaults. Five-second waits
use timer cancellation/monotonic elapsed time, not DateTime deadline loops. Read
attention at or after relevant event timestamps; avoid relying on the live
note's age-gated warning row. None of these fixture bounds releases a production
input-wait session or changes the parking-off policy.

### Proves it works now

All new methods are on `Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests`,
Category Integration, PostgreSQL with controlled provider, with the nearest
land-enqueue fixture's 180-second per-execution watchdog. Classification is
explicit below: **chain** means F's real producer-to-recipient-to-attention proof;
**guard** means a deliberate one-condition negative fixture; **compatibility**
means seeded existing coverage. Exact case strings become Arguments attributes.

| ID / method / executions | Recipe, variant and case | Oracle and mutation reachability/classification |
|---|---|---|
| V-1: `C1196_Eligible_recipient_receipt_resolves_older_refusal` / 2 | F; immediate, restart-after-terminal. The second cuts only between successful FailAsync and first reconcile using X. | `C1196:produced-outcome` count=1; `C1196:no-publication` count=0; `C1196:before-wakeup-submissions`=0; `C1196:claim-durable-at-submit` attempts=1/floor present; `C1196:history-before-confirm` contains H; `C1196:history-resolved` excludes H. Same T/Q/E/N/M/S/B/D survives X. PCs 1/2/3/19/23/24/25; chain. |
| V-2: `C1196_Busy_recipient_waits_for_turn_end_before_resolution` / 1 | B then F completion; unchanged message identity across recorded TurnEnd. | `C1196:busy-submissions`=0 before TurnEnd; attempts=0, Pending, H present. After TurnEnd one UserPrompt and confirmation clear H. PC-4 bypasses only FlushIfIdleAsync's working gate; callback would record queued input while busy, so assert zero input before checking receipt kind; chain. |
| V-3: `C1196_Enqueue_failure_recovers_the_same_obligation` / 2 | F/X; before-enqueue, queue-save. Before-enqueue uses one-shot IOException; queue-save uses ConfigureDbContext SaveChangesInterceptor restricted to Added SessionQueuedMessage with SourceLandNotificationId=N. | Fault fired once; no keyed row/input; RetryPending/null backlink/future due/H present. Disarm, reconcile before due: `C1196:before-due-rows`=0. Reconstruct, advance clock to due, retry: `C1196:retry-linked` backlink present; complete F. PCs 5/29; chain with injected enqueue failures. |
| V-4: `C1196_Crash_after_queue_insert_recovers_the_existing_key` / 1 | F/X; tagged OperationCanceledException at queue-inserted, escaping notifier catch. | `C1196:cut-unlinked` null backlink but one committed M; no attempt/input. After X require `C1196:recovered-link`=original M **before** waiting; then `C1196:recovered-wakeup` true and F completion. PCs 6/17 isolate link and wakeup; chain/service-restart cut. |
| V-5: `C1196_Lost_wakeup_is_recovered_by_the_stranded_queue` / 2 | F/X; eligible, busy. Tagged cancellation at queue-committed-before-wakeup; do not pump a wakeup after restart. | Cut has linked Pending M/attempts=0/no input. Only FlushStrandedQueuesAsync recovers eligible case: `C1196:stranded-userprompt` count=1. Busy variant calls stranded sweep while IsWorking=true and asserts `C1196:stranded-busy-submissions`=0, then persists TurnEnd and invokes stranded sweep again after idle. No OnTurnEnd/direct flush rescue in this method. Complete F. PCs 7/18; chain. |
| V-6: `C1196_Receipt_save_crash_recovers_without_retyping` / 2 | F/X; queue-before-verdict, receipt-before-save. Register the former on Bridge ConfigureServices and the latter on notifier. Tagged cancellation propagates at these locations. | Both cuts retain complete UserPrompt P and null persisted N confirmation/H present. Queue-before-verdict also has Sent/null verdict and positive attempt. After X reconcile twice: `C1196:receipt-persisted` Confirmed/time/sequence=P; `C1196:submission-count`=1 across incarnations; H absent. No queue flush to obtain a second receipt. PCs 8/26; chain/service-restart cuts. |
| V-7: `C1196_False_receipt_evidence_keeps_the_refusal_open` / 5 | F's real producer/queue, one flip from valid receipt; sent-without-prompt, head-only, wrong-session, at-baseline, no-attempt. Detailed data below. | `C1196:false-evidence-unconfirmed` null ConfirmedAt and sequence; H remains. Assert candidate prerequisites before notifier scan, so PCs 9/10/11/12/13 reach the intended condition; guard, not successful delivery proof. |
| V-8: `C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` / 4 | Delivered F; other-task, newer-refusal, equal-time-refusal, non-outcome. Detailed data below. | `C1196:protected-history` contains the protected key after genuine confirmation. In other-task the original H still clears. PCs 14/15/20; guard around real receipt, with declared projection perturbations. |
| V-9: `C1196_Unlinked_legacy_note_never_submits` / 1 | Isolated historical negative fixture: note IsLegacy=true, AwaitingReceipt, null QueueMessageId, due now, valid S/B/D and source H. Do not also create/confirm a live Outcome for T. | Reconcile twice: `C1196:legacy-keyed-rows`=0, no input or confirmation, H remains. PC-16 reaches the legacy check because State is not LegacyUnverified/Confirmed/NotRequired; guard. Method name is inherited from the proposed roster, not an absolute coverage claim. |
| V-10: `C1196_Pretyping_transport_failure_recovers_the_same_row` / 1 | F/X; one-shot IOException at queue-before-typing. This queue hook is inside the caught transport block. | Cut fired after durable claim; `C1196:pretyping-reverted` Pending, attempts=1, baseline/start retained, no input/P, N unconfirmed/H present. Reattach idle S and run stranded sweep; same M, attempts=2, one UserPrompt, real confirmation/H absent. PC-30; chain/transport-exception recovery. |
| V-11: `C1196_Recovery_link_requires_matching_destination_and_digest` / 3 | Real producer and V-4 cut; wrong-destination, wrong-digest, missing-digest. Flip just M.AgentSessionId to a second owned existing session, D to a distinct digest, or D to null. All other tuple members unchanged. | Reconcile: `C1196:identity-refused-link` null backlink, reconciliation error names ConflictException, one existing M, no input/confirmation, H present. Restore only the fixture's flipped field, reconstruct, advance notifier clock to its newly persisted due time and complete F through the recovered wakeup. PCs 27/28; guard followed by valid chain on an unattempted row. |

V-7's hand-written cases (one eligible receipt condition invalid per row):

| Case | Candidate construction and reachability | Expected |
|---|---|---|
| sent-without-prompt | Suppress only callback transcript insertion. Real queue still submits. After the owned flush completes, deliberately set Sent/Delivered if needed without changing its positive attempt/floor; assert no matching transcript on S. This metadata is negative test setup, not receipt evidence. | Unconfirmed, H present; PC-9. |
| head-only | Callback records only enough submitted text to retain the full normalized 200-character identity window, omitting a non-whitespace tail. Assert B is long enough, IsConfirmedBy=true and IsCompleteIn=false. A raw 200-character slice may normalize shorter; select the slice using those independent preconditions. | Unconfirmed, H present; PC-10. |
| wrong-session | Callback records actual submitted bytes on a second owned session S2. Keep M.AgentSessionId=S, so ReceiptScanTarget cannot legitimately follow S2. Pad S2 with harmless entries before callback insertion so its matching sequence is **strictly above M's real baseline**; verify timestamp/floor and both text predicates pass. No matching prompt on S. | Unconfirmed, H present; PC-11. |
| at-baseline | Complete UserPrompt P is callback-created on S. Before the first post-delivery notifier scan, flip only M.LastDeliveryBaselineSequence to P.Sequence. Keep timestamps and positive attempts valid. | Unconfirmed, H present; PC-12. |
| no-attempt | Complete UserPrompt P is callback-created on S above a valid baseline/time. Flip only M.DeliveryAttempts to zero before notifier scan. Do not mutate N confirmation. | Unconfirmed, H present; PC-13. |

Do not run the success helper's post-delivery reconcile before V-7 perturbations.
For its first three variants the queue may report failed/truncated/uncertain
delivery; await completion, preserve the actual result and do not retry to make
the negative fixture green. Candidate checks concern receipt inputs, not queue
success. No assistant ack or screen event is promoted to UserPrompt.

V-8's other-task variant seeds a second valid task and H2 older than E; E/N remain
owned by T. Newer-refusal uses a distinct same-task historical H2 at E.At plus one
second; equal-time-refusal uses exactly the database-read E.At. Seed H2 before
the confirmation/read under test and assert H2's key (history grouping may replace
H's representative). Advance attention clock to H2.At. For non-outcome, after
real F confirmation change only N.Kind from Outcome to Held before the tested
attention read; keep H/E/time/task and persisted receipt valid. This is a
projection guard fixture, not a claim that Held resolves history. These cases
separately exercise TaskId, strict event ordering, and Outcome kind.

### Guards the regression

- R-1: the CARD-1085 causal gap. V-1's exact H key survives real submission while
  N remains unconfirmed, then disappears after real reconciliation. PC-1 must
  make its final history assertion red while the produced note is confirmed.
- R-2: retain `AttentionFeedNoiseTests`' ten existing executions listed below.
  These seeded projection tests do not establish producer delivery. Only replace
  the coverage-gap comment after CP-2 passes with a precise link to V-1/V-2/V-6
  and the controlled-provider limitation; do not rewrite their assertions.
- R-3: retain `AgentTaskLandEnqueueTests`' five methods/six executions listed
  below. Real queue/producer coverage protects composition with shared harnesses;
  it does not supply the historical-refusal oracle.
- R-4: retain `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts`, exactly 15
  argument executions. Complete, flattened, clock-tolerance and queued-prompt
  expect confirmation/sequence 11; queue-enqueue, sent, screen-delivered,
  late-confirmed, wrong-session, wrong-identity, old-sequence, old-time, head-only,
  head-tail-splice and unrelated expect null confirmation/sequence. Adapter inputs
  remain empty. PCs 9-13/21/22 supply receipt mutants; QueuedUserPrompt is a
  supported positive Outcome receipt and must not be made a universal negative.

R-4's recipe is its inspected SeedAsync plus real enqueue, then explicitly seeded
attempt metadata and candidate evidence; classification **compatibility**, with
no producer or successful typing claim. Case-to-mutation sensitivity is:

| Existing argument(s) | Mutation -> exact existing red oracle |
|---|---|
| complete, flattened, clock-tolerance, queued-prompt | PC-8's confirmation-assignment defect -> ConfirmedAt.HasValue is false instead of the hand-written true, and sequence is null instead of 11. |
| queue-enqueue | PC-21 -> ConfirmedAt.HasValue true instead of false. |
| sent, screen-delivered, late-confirmed | PC-9's Sent shortcut -> ConfirmedAt.HasValue true instead of false; no prompt was seeded. |
| wrong-session | PC-11 -> ConfirmedAt.HasValue true instead of false; foreign sequence 11 clears floor 10. |
| wrong-identity, unrelated | Replace LandNoteReceipt.IsReceipt's expression with true -> ConfirmedAt.HasValue true instead of false. This wholesale matcher defect is an existing-test sensitivity recipe, not a separately commissioned identity-only PC; the independent-predicate issue is explained below. |
| old-sequence | PC-12 -> ConfirmedAt.HasValue true instead of false at exact floor 10. |
| old-time | PC-22 -> ConfirmedAt.HasValue true instead of false with null sequence baseline. |
| head-only, head-tail-splice | PC-10 -> ConfirmedAt.HasValue true instead of false while head identity still matches. |

R-2 exact roster and sensitivity (these existing presentation/compatibility
assertions are ordinary regressions, not additional commissioned PC cycles):

| Exact method on AttentionFeedNoiseTests | Decisive assertion; compiling regression that would make it red |
|---|---|
| `C1085_Land_outcome_receipts_collapse_to_the_newest_per_task` | One newest row, severity retains older error; return uncollapsed input in CollapseLandOutcomeReceipts -> count red. |
| `C1085_Legacy_rows_age_out_after_the_task_is_done_and_outcomes_do_not` | Old canceled receipt absent, unresolved live outcome retained; remove receipt age-out continue -> canceled row red. |
| `C1085_Unresolved_land_refusal_survives_the_legacy_age_out` | Old refused/canceled keys present; add a 24-hour skip to EligibleHistoricalRefusalsAsync -> missing refusal red. |
| `C1085_Recent_incidents_stay_one_row_and_name_the_hidden_tail` | One incident row with three displayed entries and '1 more'; return only newest evidence -> tail assertion red. |
| `C1085_Min_severity_hides_rows_and_keeps_the_kind_census` | Critical view hides Error while KindCounts retains it; calculate census after severity filtering -> census red. |
| `C1085_Mixed_history_prefers_the_unresolved_refusal_and_caps_the_folded_receipts` | Representative is refusal and exact folded evidence; choose receipt representative in mixed branch -> ConditionKey red. |
| `C1085_Aged_out_receipt_plus_unresolved_refusal_is_only_the_refusal` | Evidence receipts=1 and excludes aged marker; retain aged receipt group -> evidence red. |
| `C1085_Unconfirmed_receipt_stays_when_a_later_publication_resolves_the_refusal` | Representative is receipt instead of refusal; remove publication resolution arm -> ConditionKey red. |
| `C1085_Aged_receipt_and_resolved_refusal_emit_no_legacy_row` | No legacy row; remove publication resolution arm -> nonempty legacy rows red. |
| `C1085_Two_tasks_with_mixed_histories_emit_two_legacy_rows` | Two task-specific refusal representatives; collapse historical groups to one task -> count red. |

R-3 exact roster:

| Exact method on AgentTaskLandEnqueueTests | Executions / decisive assertion / sensitivity |
|---|---|
| `C641_Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy` | 1; keyed immutable row, receipt sequence and zero lease probes. PC-3 new-row wakeup omission -> delivery assertion red. |
| `C641_Conflict_enqueues_and_reaches_a_busy_caller_after_turn_end` | 1; no submission before turn-end, complete receipt after. PC-4 busy gate bypass -> premature submission red. |
| `C641_Recovered_commit_and_lost_insert_ack_use_one_notification_and_queue_row` | 2: before-enqueue, queue-inserted; original inserted ID retained. PC-6 reattachment omission -> backlink red. |
| `C641_Unavailable_destination_records_a_due_retry` | 1; DestinationUnavailable, null link, due time and no input. Remove stopped-destination early-return block -> state/link red. |
| `C641_Concurrent_reconcilers_keep_one_key_and_complete_receipt` | 1; one keyed row and receipt. PC-3 wakeup omission on creation -> delivery assertion red. Concurrency scheduling is exercised, not an exhaustive race proof. |

### Guard inventory

Scope is the new delivery witness/recovery claims and its receipt compatibility
controls. Each listed guard has a distinct PC; a shared detecting method is not
a shared PC. The added guards split the proposed plan's wakeup, busy, confirmation
and recovery assertions where different production branches can be broken
independently. None is omitted for cost. Execution status is pending for this
entire inventory under the post-land pause.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1, H6: later confirmed Outcome can resolve H without publication | PC-1 |
| G-2 | D-1, H0: terminal refusal produces its durable Outcome obligation | PC-2 |
| G-3 | D-2, H3: a newly linked eligible row receives a wakeup | PC-3 |
| G-4 | D-3, B: FlushIfIdleAsync withholds typing while recipient is working | PC-4 |
| G-5 | D-5, H1: RetryPending remains recoverable after its due time | PC-5 |
| G-6 | D-5, H2: an existing keyed row restores the original backlink | PC-6 |
| G-7 | D-5, H3: stranded pending work can reach an idle recipient after signal loss | PC-7 |
| G-8 | D-5, H4b/H5: matched persisted receipt can complete notification confirmation after restart | PC-8 |
| G-9 | D-3/D-6, H5: Sent/Delivered metadata without transcript does not confirm | PC-9 |
| G-10 | D-3/D-6, H5: an identifying head without the whole B does not confirm | PC-10 |
| G-11 | D-3/D-6, H5: candidate prompt belongs to the selected destination | PC-11 |
| G-12 | D-6, H5: a sequence-baseline receipt is strictly above its floor | PC-12 |
| G-13 | D-6, H5: confirmation requires a positive delivery-attempt count | PC-13 |
| G-14 | D-1, H6: later-Outcome resolution remains scoped to T | PC-14 |
| G-15 | D-1, H6: resolving Outcome's source is strictly later than protected history | PC-15 |
| G-16 | D-6, H1: unlinked historical legacy evidence cannot create new queue input | PC-16 |
| G-17 | D-5, H2: reattachment has its own wakeup, independent of new-row wakeup | PC-17 |
| G-18 | D-3/D-5, H3: stranded recovery independently withholds typing while working | PC-18 |
| G-19 | D-1, H6: an unconfirmed live Outcome does not resolve H | PC-19 |
| G-20 | D-6, H6: later confirmed Held is not the later-Outcome resolution arm | PC-20 |
| G-21 | D-6, H5: queue events do not substitute for admitted submitted prompt kinds | PC-21 |
| G-22 | D-6, H5: absent a sequence baseline, the timestamp floor excludes old prompts | PC-22 |
| G-23 | D-2/D-5, H1/H3: notifier enqueue defers typing until delivery work runs | PC-23 |
| G-24 | Fixture step 6, H4a: attempt and floor are durable before the callback sees input | PC-24 |
| G-25 | D-1, H0/H6: refusal witness cannot pass through the publication alternative | PC-25 |
| G-26 | D-5, H5: reconciliation of a stored receipt does not submit B again | PC-26 |
| G-27 | D-5, H2: existing-key recovery refuses a different destination | PC-27 |
| G-28 | D-5, H2: existing-key recovery refuses a different or missing digest | PC-28 |
| G-29 | D-5, H1: a future due time with null backlink withholds another enqueue | PC-29 |
| G-30 | D-5, H4a: caught pre-input transport failure returns the row to Pending, preserving attempt/floor | PC-30 |

Two apparent extra predicates are not independently bypassable for these inputs.
For the long generated B, IsCompleteIn implies IsConfirmedBy: deleting only the
identity conjunct cannot make a wrong-identity body pass completeness. G-10 tests
the independently bypassable completeness condition; R-4 retains wrong-identity
coverage. Similarly, deleting `n.SourceEventId != source.Id` from the final
attention query cannot change an own-event confirmed receipt already accepted by
LegacyReceiptConfirmedAsync. Do not commission either equivalent mutation as a
positive control. The no-own-receipt and no-publication fixture checks make G-1's
later-Outcome arm observable. The other queue admission, spill, cache and native
guards remain outside the claimed scope as declared below.

### Positive controls

These are compiling source-edit recipes assessed by inspection, **not compiled
or executed evidence**. All 30 must receive method-scoped break/red/restore/green
cycles when Mutation is commissioned after land. Code runs V/R only; ordinary
Review judges implementation, evidence, and PC reachability before land. Do not
auto-resume the paused Mutation stage.

Each filter below is literal and method-scoped. Parameterized methods run their
whole argument roster; evidence must name the intended failing argument and
assertion. A watchdog boolean assertion is an oracle, but a TUnit timeout, zero
executions, build/fixture failure, or an unrelated exception is not an accepted
red. Establish baseline green before mutation. Restore exact tracked/index bytes
and rebuild for restored green. A missing/unreachable control stays binding
required, including after ordinary Code passes.

| PC / break the mapped guard with this source defect | Exact detecting filter | Expected red assertion and reachability |
|---|---|---|
| PC-1: in AttentionService.LandRefusalResolvedAsync replace only the final later-Outcome AnyAsync return with `return false;`, retaining preceding awaited own-receipt/publication checks | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:history-resolved`: H remains despite N being Confirmed. Both variants; no publication/own receipt can rescue. |
| PC-2: omit the AddNotification(...Outcome...) call in CompleteTerminalLockedAsync, leaving E/Q save and commit | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:produced-outcome`: queried list count is 0 instead of 1. Assert a list count before accessing its item, avoiding SingleAsync setup failure. |
| PC-3: omit only `flushes.TryEnqueue(session)` in the newly-created-row branch after queue-committed-before-wakeup (keep a legal empty guarded block) | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:wakeup-received` false after bounded signal read. Link/row preconditions already passed; no hosted worker or sweep repairs it. |
| PC-4: change the working condition inside FlushIfIdleAsync to `if (true)` | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Busy_recipient_waits_for_turn_end_before_resolution` | `C1196:busy-submissions` is 1 instead of 0 before TurnEnd. Running/live/clear composer conditions remain eligible. |
| PC-5: add RetryPending to ReconcileAsync's initial terminal-state return pattern | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Enqueue_failure_recovers_the_same_obligation` | `C1196:retry-linked` remains null after clock reaches persisted due in both cases. The fault itself is disarmed. |
| PC-6: replace the existing-row branch's link/save/wakeup work with `return;` when the lookup found M | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Crash_after_queue_insert_recovers_the_existing_key` | `C1196:recovered-link` remains null instead of original M. This assertion precedes signal waiting. |
| PC-7: replace FlushStrandedQueuesAsync's body with `return await Task.FromResult(0);` | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Lost_wakeup_is_recovered_by_the_stranded_queue` | Eligible case `C1196:stranded-userprompt` count=0 instead of 1. No direct flush fallback. |
| PC-8: keep receipt-before-save hook but replace matched-hit assignments with ConfirmedAt=null, ConfirmingPromptSequence=null and State=AwaitingReceipt | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_save_crash_recovers_without_retyping` | After reconstruction `C1196:receipt-persisted` still lacks confirmation/sequence. Receipt-save cut remains reachable because its hook runs before these assignments. |
| PC-9: immediately before the attempt-count receipt block add a Sent shortcut assigning note.ConfirmedAt=now, State=Confirmed, saving and returning without scanning | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` | sent-without-prompt: `C1196:false-evidence-unconfirmed` finds non-null ConfirmedAt. Baseline/delivery attempts are valid. |
| PC-10: remove only `&& PromptSubmissionMatch.IsCompleteIn(expected, promptText)` from LandNoteReceipt.IsReceipt | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` | head-only: `C1196:false-evidence-unconfirmed` becomes non-null. Independent precondition proves identity still matches. |
| PC-11: remove only AgentSessionId equality from LandNoteReceipt.Prompts' first Where, preserving Text!=null | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` | wrong-session: `C1196:false-evidence-unconfirmed` becomes non-null. Foreign prompt is deliberately above the real floor; M still targets S. |
| PC-12: change sequence predicate `p.Sequence > floor` to `p.Sequence >= floor` | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` | at-baseline: `C1196:false-evidence-unconfirmed` becomes non-null for exact P.Sequence, with valid attempts/text/session. |
| PC-13: change ReconcileAsync's receipt-scan gate `row.DeliveryAttempts > 0` to `row.DeliveryAttempts >= 0` (the ordinary receipt gate, not completion-rendering diagnostics) | `/*/*/LandRefusalDeliveryWitnessTests/C1196_False_receipt_evidence_keeps_the_refusal_open` | no-attempt: `C1196:false-evidence-unconfirmed` becomes non-null. Retained non-null baseline/time prevents floor masking. |
| PC-14: remove only `n.TaskId == source.AgentTaskId` from the final later-Outcome query | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` | other-task: `C1196:protected-history` loses H2 belonging to the second task. E is later than both histories. |
| PC-15: remove only `e.At > source.At` from that query's nested source-event Any | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` | newer-refusal and equal-time-refusal: `C1196:protected-history` loses H2. Equal-time also catches a future >= mutation; only this deletion is commissioned in PC-15. |
| PC-16: delete the `note.IsLegacy && note.QueueMessageId is null` early return | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Unlinked_legacy_note_never_submits` | `C1196:legacy-keyed-rows` becomes 1. Note is AwaitingReceipt, due, with valid destination, so initial terminal/due gates do not mask. |
| PC-17: omit only the recovered-existing-row branch's `flushes.TryEnqueue(session)` after queue-existing-key | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Crash_after_queue_insert_recovers_the_existing_key` | `C1196:recovered-wakeup` false, after same-M backlink assertion passed. Different edit from PC-3. |
| PC-18: change only FlushStrandedQueuesAsync's per-candidate `!await ReadWorkingAsync(...)` condition to true | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Lost_wakeup_is_recovered_by_the_stranded_queue` | busy: `C1196:stranded-busy-submissions` becomes 1 on the first sweep. No OnTurnEnd event was yet recorded. |
| PC-19: remove only `n.ConfirmedAt != null` from the final later-Outcome query | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:history-before-confirm` loses H after E/N are produced but before notification confirmation. No publication or own-event receipt exists. |
| PC-20: remove only `n.Kind == LandNotificationKind.Outcome` from that query | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_does_not_resolve_another_task_or_newer_refusal` | non-outcome: `C1196:protected-history` loses H although the confirmed note's only perturbed field is Held kind. |
| PC-21: in LandNoteReceipt.Prompts add QueueEnqueue to the accepted kinds in the non-legacy Outcome arm | `/*/*/AgentTaskLandReceiptTests/C467_V11_RejectFalseReceipts` | queue-enqueue argument: existing `note.ConfirmedAt.HasValue.ShouldBe(confirmed)` fails (true vs false). Its sequence 11 is above 10 and body is complete. queued-prompt remains positive. |
| PC-22: replace only the timestamp-floor return in LandNoteReceipt.Prompts with `return prompts;` | `/*/*/AgentTaskLandReceiptTests/C467_V11_RejectFalseReceipts` | old-time argument: the same existing confirmation assertion fails (true vs false). Baseline is null and attempt start is present, reaching the fallback branch. |
| PC-23: change only notifier's EnqueueAsync argument from deliverIfIdle:false to true | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:before-wakeup-submissions` becomes 1. Assert this immediately after the first reconcile, before its possibly premature confirmation checks. |
| PC-24: in DeliverNextLockedAsync omit the ordinary `else await db.SaveChangesAsync(ct)` immediately after the attempt/floor claim and before DeliveryOutcome/typing; retain the completion-rendering transaction arm | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:claim-durable-at-submit` reads captured attempts=0/floor null. The callback records the observed row without throwing; later verdict SaveChanges cannot rewrite this observation. Outcome is not profiled TaskCompletion. |
| PC-25: after SetLandingEvidence(terminal, op), assign `terminal.LandingPublication = LandPublicationOutcome.Landed;` for type LandRefused before its save | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Eligible_recipient_receipt_resolves_older_refusal` | `C1196:no-publication` count becomes 1 before reading attention. The deliberately false publication could otherwise bypass G-1. |
| PC-26: in the matched-hit block after receipt-before-save, add `await messages.EnqueueAsync(session, note.Body, MessageSendMode.Now, ct);` before confirmation assignments | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Receipt_save_crash_recovers_without_retyping` | `C1196:submission-count` becomes 2 across incarnations. Place this assertion immediately after recovery reconcile; the first receipt-save cancellation still precedes the inserted resubmission. Mode.Now bypasses the WhenIdle busy gate. |
| PC-27: in existing-row reattachment's mismatch condition remove the AgentSessionId comparison, leaving the digest comparison | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Recovery_link_requires_matching_destination_and_digest` | wrong-destination: `C1196:identity-refused-link` becomes non-null. Digest/body/key are valid; do not run a delivery pump during the refusal observation. |
| PC-28: in that same condition remove only the ContentDigest comparison, leaving destination comparison | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Recovery_link_requires_matching_destination_and_digest` | wrong-digest and missing-digest: `C1196:identity-refused-link` becomes non-null. Unknown digest remains withheld in baseline; no permissive default. |
| PC-29: omit ReconcileAsync's null-link/future-NextAttemptAt early return | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Enqueue_failure_recovers_the_same_obligation` | `C1196:before-due-rows` becomes 1 after disarming the one-shot fault, before advancing notifier time. |
| PC-30: omit `await RevertRunAsync(db, run)` only from DeliverNextLockedAsync's generic transport catch, leaving logging/incident/Failed return | `/*/*/LandRefusalDeliveryWitnessTests/C1196_Pretyping_transport_failure_recovers_the_same_row` | `C1196:pretyping-reverted` observes Sent instead of Pending immediately after the caught cut. Assert before reconstruction/recovery could conceal the wrong state. |

The queue's independent keyed uniqueness constraint does not mask PC-6/27/28:
these controls change recovery of an already committed row, not duplicate insert.
The runtime receipt cache is absent in the small notifier graph (matching the
landed composition fixture); a Running recipient also falls outside its terminal
session whitelist. Cache behavior is not evidence claimed here. PC-11 intentionally
changes the query itself, not ReceiptScanTarget's valid alternate destination.

### Out of scope

- Native provider/PTY and transcript-file ingestion; owned by the inspected native
  queue integration suite. The new callback receipt is the declared application
  composition substitute, not a native delivery receipt.
- Hard process kill, database transaction rollback/commit-loss durability, hosted
  scanner boot enumeration/cadence, and queue overflow scheduling. H0 covers a
  completed terminal commit then service reconstruction; rollback coverage remains
  with the existing landing persistence suites. There is no new production seam.
- Publication/cleanup authorization, refusal classification, destination-loss,
  cancellation/park/attempt-cap matrices, alternate receipt destinations, null-floor
  policy, spill rendering, retention and receipt cache performance/certificates.
  Existing behavior is preserved. R-4 covers its named compatibility cases, not
  these entire contracts. No missing-data assumption authorizes new delivery.
- Broad attention presentation semantics beyond R-2's retained projection roster,
  and CARD-1190/CARD-1203 ready-gate producers. No shared harness defaults, native
  tests or production files are owned by this Code task.

### Checkpoints

Closed ordinary Code list. Each row has one isolated build and one exact TUnit
filter, and runs serially with no other build in this worktree. No whole-Unit or
assembly run is authorized by this roster. The new class is exactly the eleven
methods V-1 through V-11: 24 argument-expanded executions. CP-1 selects three of
those executions on S1; CP-2 reruns them on the final S1-S2 source. R-2 is ten
methods/results; R-3 is five methods/six results; R-4 is one method/fifteen results.
Counts are static source/Arguments-derived expectations, not measured discovery.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1196-s1/` | pg-refusal-eligible-busy | `/*/Antiphon.Tests.Application/LandRefusalDeliveryWitnessTests/(C1196_Eligible_recipient_receipt_resolves_older_refusal*)\|(C1196_Busy_recipient_waits_for_turn_end_before_resolution*)` | V-1, V-2, R-1 | all 3 listed results, 0 failed/skipped | 3 | 8 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-witness/` | pg-refusal-recovery-guards | `/*/Antiphon.Tests.Application/LandRefusalDeliveryWitnessTests/*` | V-1 through V-11, R-1 | all 24 listed results, 0 failed/skipped | 24 | 12 | true |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-regression/` | pg-attention-enqueue | `/*/Antiphon.Tests.Application/(AttentionFeedNoiseTests)\|(AgentTaskLandEnqueueTests)/*` | R-2, R-3 | all 16 listed results, 0 failed/skipped | 16 | 5 | true |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1196-receipts/` | pg-receipt-compatibility | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptTests/C467_V11_RejectFalseReceipts*` | R-4 | all 15 listed results, 0 failed/skipped | 15 | 5 | true |

Execution binding: commit/push S1, then checkpoint tool
`run --plan docs/superpowers/plans/2026-10-12-card-1196-test-design.md --rows CP-1 --expected-source-sha <S1-SHA>`.
Commit/push S2, then the same command with `--rows CP-2,CP-3,CP-4` and the final
S1-S2 SHA. Keep waiting while exit=75; do not settle a running driver. Use the
documented host-slot wrapper for tool bootstrap/build drivers and the checkpoint
tool's own row leases without double-wrapping run-checkpoint.ps1. Exit 4 is not-run,
not permission for an unleased retry. Preserve unedited CHECKPOINT lines, actual
rosters, counts, source receipts and rerun reasons in the stored report.

Code must report **58 ordinary executions** over the four rows (55 on final
S1-S2 source). A count alone does not bind the intended variants: match method and
argument rosters too. Validate clean SHA/build provenance. Required assertions and
fixtures must exist before the run; no silent roster/count reduction. A production
defect or an unavailable seam returns for a plan/scope amendment with the precise
reproducer. Do not weaken the oracle, broaden acceptance or replace real queue
delivery with seeded confirmation.

Code/Review run `scripts/check-evidence-diff.ps1` over their complete task ranges.
Generated TRX/JSON/logs remain ignored. Checkpoint cleanup removes owned alternate
bin outputs; after a red run use the documented clean command and await it.
Ordinary Review qualifies CP-2 through CP-4 at final source (22 estimated minutes)
plus inspection; S1 evidence does not certify S2. Post-land SourceLanding Mutation
uses the assigned external evidence root and makes no snapshot commits/pushes.

### Cost

Estimates, not measurements; slot waits and failure-driven reruns are additional
reported time. PostgreSQL setup is included in the row estimates. Reserve **2
minutes** for checkpoint-tool setup/bootstrap outside the ordinary V/R floor.
If a tool bootstrap build is needed, report it as a tool-only prerequisite with
that stated reason and take the host build slot; it is not an extra test-project
build or an additional test selection.

- Ordinary V/R floor (Code): **30 minutes = CP-1 8 + CP-2 12 + CP-3 5 + CP-4 5**.
  Estimated row decomposition is four isolated builds at 3 minutes = 12 build
  minutes, plus 18 minutes test/setup work. Code authoring is separately estimated
  90-120 minutes for the eleven methods and fixture; dispatch budget 122-152
  minutes including the 2-minute tool setup, before slot waits.
  Union: V-1..V-11 and R-1..R-4.
- PC floor (Mutation, paused): **210 minutes = 30 cycles x 7 minutes**. Per cycle
  estimate: apply/bind defect 0.5, red isolated build 2, red exact-method run 1,
  restore 0.5, green isolated build 2, green same-method run 1. Thus 120 build
  minutes + 60 execution minutes + 30 mutation/restoration minutes. The literal
  filter and argument count for each cycle are the PC table, never a class run.
- Method-filter cost allocation: V-1 filter, PCs 1/2/3/19/23/24/25, 49 minutes;
  V-2, PC-4, 7; V-3, PCs 5/29, 14; V-4, PCs 6/17, 14; V-5, PCs 7/18, 14;
  V-6, PCs 8/26, 14; V-7, PCs 9..13, 35; V-8, PCs 14/15/20, 21; V-9, PC-16, 7;
  V-10, PC-30, 7; V-11, PCs 27/28, 14; R-4, PCs 21/22, 14. Sum **210**.
- Mutation setup/binding and method-scoped baseline qualification: **20 minutes**;
  external report/restoration audit: **10 minutes**. Total commissioned Mutation
  estimate **240 minutes = 20 + 210 + 10**. Current executed Mutation time: **0**.
- Combined verification estimate **272 minutes = 2 tool setup + 30 ordinary + 240 Mutation**,
  excluding Code authoring, separate ordinary Review and slot waits. Including
  Review's 22-minute run floor gives **294 minutes**, before review inspection.

No measured runtime savings are claimed. Relative to the plan's proposed 27-minute
ordinary floor, this roster budgets **3 additional minutes** for H4 cuts and
independently reachable guards. One build per row avoids unbudgeted discovery and
repeat builds; no unchanged-suite repetition or native startup is purchased here.
Mutation batching savings are **0**: the controls mostly share production files
and detecting methods, so this estimate commissions independent cycles. Do not
hide the larger PC floor in the ordinary Code budget or treat the pause as savings.

Design handoff census: **guards=30, mapped=30, missing=0, duplicate PC maps=0**.
Execution census: **PCs run=0/30; all 30 required, pending under the explicit
post-land pause**. Ordinary builds/tests run in this TestDesign task: **0**.
Next: Code S1 (fixture, eligible/restart-after-terminal and busy witnesses), then
S2 and the remaining checkpoint rows; no human choice or production seam blocks
that handoff.
