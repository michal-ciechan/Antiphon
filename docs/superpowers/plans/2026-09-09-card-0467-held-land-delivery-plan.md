# CARD-0467: observable held lands and durable caller delivery

Plan stage, 2026-09-09. Source baseline: `4e0ae76a`. Task: `29c997c6`.
Status: implementation design complete; separate TestDesign required before Code.

## Outcome and scope

A land request must remain observable from acceptance through repository publication and caller
receipt. Show an explicit hold and its owner, preserve the notification obligation across every
database/queue boundary, and prove that the outcome becomes a complete `UserPrompt` in the caller's
transcript. Delegate success, remote publication, cleanup and notification are separate facts.

Preserve CARD-0227/CARD-0448 writer exclusion, repository leases, source identity checks, remote
containment proof, recovery checkpoints and guarded cleanup. No new kill, owner retirement,
automatic unblock, force push, card move, provider launch or SendNow action belongs to this change.

The corrected incident is a hold, not an observed post-publication notification loss. CARD-0466
task `81da7ca2` had `LandAttempt=0`; Shared task `c420add0`, Blocked since September 4, excluded
landing. Debug established that commit `569ce66f` was not an ancestor of master. A match in
`git log --all` was mistaken for containment. The publication-to-notification loss window below
is a separately confirmed code defect, not a claim about what happened in that incident.

## Ground truth

| Assumption / need | What the baseline actually does | Consequence |
|---|---|---|
| Succeeded means landed | `AgentTask.Status` records delegate settlement; `AgentTaskLanding` separately records publication and cleanup | Keep success and land status visibly separate |
| A Blocked owner has stopped writing | `AgentTaskLandService.FindWriterAsync` includes Dispatched, Working **and Blocked**, checks source aliases and Shared common-repository identity, and fails closed on inaccessible identity | Preserve the real guard; the older `IsHeldBehindSharedWriter` helper is narrower and is not adequate test coverage |
| Holds notify the caller | `RunAsync` emits at most one generic Held event since `LandRequestedAt`; it neither publishes a task-change event nor enqueues a note in either hold branch | Persist structured current hold, publish it, and owe a durable hold note |
| Holds consume retries / eventually fail | Admission holds return before `LandStartedAt` and `LandAttempt` advance; the boot/5-second sweep retries pending rows outside the process active set | A valid hold can last indefinitely; detection must include attempt-zero and process-active requests |
| `-Status` exposes pending land | `scripts/delegate.ps1` prints summary status and report/failure only, despite DTO pending timestamps and structured landing evidence | Add explicit land and notification lines |
| Existing Attention catches land silence | `BuildCallerNoteUndeliveredItemsAsync` needs Pending rows correlated by SourceTaskId (or Check key); Land supplies neither a source task nor a parseable Check key | It misses even an existing Land queue row, as well as holds, absent rows and Sent-but-unconfirmed rows |
| Terminal event ensures delivery | `SettleLandedAsync` and `PersistRefusalAsync` save terminal evidence and clear pending before `DeliverAsync`; enqueue errors are caught and logged | A crash or enqueue error leaves no durable notification work to recover |
| Adding source/digest fixes every loss | `CompletionNoteWorkHostedService` recovers already-persisted Pending rows with SourceTaskId/ContentDigest and zero attempts | Metadata fixes deferred flush discovery, but cannot reconstruct a never-inserted row |
| Sent / Delivered proves receipt | Queue stamps Sent before typing; Delivered may be screen-confirmed; interrupted and late-confirm paths exist | Require matching complete UserPrompt evidence, with the queue's anti-replay floor |
| Landing safety tests prove successful notification | `LandingSafetyHarness` seeds ReplyTo=None and constructs a fresh land queue for direct runs; C448_V23 supplies a missing destination and proves publication survives delivery failure | Add a composed request/background/queue/recipient harness, retaining existing safety tests |
| Existing sequencing E2E covers this contract | `DelegationSequencingE2ETests` takes a note and submits it into the caller itself; PTY queue tests begin at Enqueue | Neither proves Land's producer-to-recipient route |
| Standing TestDesign covers asynchronous delivery | `stage-test-design.md` requires V/R/PC lists but no delivery-path inventory; no separate test-design-review bundle exists | Add the requirement to TestDesign and the audit to the existing Review bundle |

Owners consulted: `docs/project-context.md`, `docs/orchestration-loop.md`,
`docs/agent-card-lifecycle.md`, `docs/ops-http.md`, `docs/testing-and-build.md`, and
`docs/session-runtime-invariants.md`. This plan changes no running-stack state.

## Decisions

### D-1: request identity precedes landing-operation identity

Add `AgentTaskLandRequest` and an `AgentTask.CurrentLandRequestId` reference. A request exists
before repository admission, so a held request can be observed without inventing a landing
operation. `AgentTaskLanding.Id` remains the identity of the publication/cleanup history; several
explicit requests can refer to the same operation for cleanup retries.

The request stores:

- Id, TaskId, RequestedAt, VerifyFilter; the ReplyTo and ParentSessionId destination snapshot.
- State: Queued, Held, Running, NeedsResolution, Completed, Superseded or Canceled; nullable
  LandingOperationId and TerminalEventId. These are land-request states, not new task roles,
  task statuses or card columns.
- StartedAt, Attempt, LastAttemptAt, LastEvaluatedAt and LastProgressAt.
- HoldReasonCode, HoldDetail, HoldingTaskId, HoldingTaskStatus, HeldSince and HoldEpisode.
- A concurrency token; timestamps needed for first aged warning and error transitions.

Persist the request and LandRequested event atomically, then enqueue its `(TaskId, RequestId)`.
Keep existing `LandRequestedAt`, `LandStartedAt`, `LandAttempt` and `LandVerifyFilter` synchronized
on AgentTask during the compatibility period. They remain the existing pending-work front door;
the request retains history after pending clears. Reconciliation reports a disagreement rather
than manufacturing publication from either representation.

Serialize request acceptance on the task row. A repeat POST while the same pending request is
not process-active requeues the same request, preserving its original age and attempt count;
it must not erase evidence by looking newly requested. It may update the pending verify filter
before execution under the same lock. An active request still returns 409. After a terminal
outcome an explicit POST creates a new request with attempts reset, preserving the existing
ability to retry refusal or cleanup. Return `requestId` in the additive 202 payload.

Before acting, the drain reloads the task/request and checks current identity and pending state.
A stale channel item is a no-op. Keep the existing repository lease and writer guard before
any protocol work. Database serialization is not a substitute for the repository lease.

Rejected: using operation ID as the only key (no operation exists during a hold); deriving the
current hold by parsing the first historical Held string (it goes stale when the blocker changes);
resetting request age on a requeue (it conceals a no-progress condition).

### D-2: holds are durable state with caller notes, not permission to override owners

Both hold branches use one persistence path. Record reason `repository_mutation_lease_busy` or
`repository_or_source_writer`, the actual returned writer ID/status and bounded detail. An
identity failure can carry a specific detail while retaining the same fail-closed predicate.
For an occupied lease whose owner is not exposed by the lease API, show owner unknown; never
guess a holding task. Do not narrow `FindWriterAsync` or replace it with the pure legacy helper.

On entry or a changed reason/holding task, increment HoldEpisode, save current state and a Held
timeline event, and create its notification obligation in the same transaction. Publish
`AgentTaskChanged` after commit. Unchanged sweeps update evaluation evidence without appending
repeated notes/events. Hold age is the current episode's age; request/no-progress age is also
shown and does not reset when the blocker changes.

On admission clear current hold fields, record HeldReleased, and advance LastProgressAt as
actual admission/phase evidence advances. The normal terminal outcome is the caller's release
follow-up; do not enqueue an extra per-sweep progress message. Holds consume zero execution
attempts, including Blocked owners. A host restart recreates state from committed rows.

A rebase conflict keeps the existing Blocked/merge-helper behavior. Record request
NeedsResolution and a correlated conflict notification. It is not successful publication and
must not run through the Succeeded-only sweep until the existing merge-resolution path permits
resumption. Cancellation/invalidation closes only that request's pending work with recorded
reason; it never erases an already-committed publication or an owed terminal notification.

### D-3: terminal evidence and notification obligation share one commit

Add a Land-specific durable outbox entity `AgentTaskLandNotification`. Reuse the production
SessionMessageQueueService for delivery; do not create another terminal transport.

Each notification contains Id, RequestId, TaskId, optional LandingOperationId, SourceEventId,
Kind (Held, Aged, Conflict, Outcome), destination snapshot, immutable Body/ContentDigest,
CreatedAt, NextAttemptAt, EnqueueAttempts, LastErrorCode/At, optional QueueMessageId,
EnqueuedAt, ConfirmedAt and ConfirmingPromptSequence. Track explicit NotRequired for ReplyTo=None,
and DestinationUnavailable for ReplyTo=Session with no valid destination. Missing destination
must not silently become NotRequired. Queued, RetryPending, AwaitingReceipt and confirmed states
must be distinguishable. Use a unique SourceEventId; hold/age events have stable per-request
episode/threshold identity. Do not cascade-delete an unresolved obligation with a session.

For Landed, AlreadyPresent, LandedWithResidue, LandingCleanup and LandRefused, one transaction
commits the existing terminal protocol evidence, stage outcomes, terminal event, request
completion, pending-field clearing **and** notification obligation. Refusal before an operation
exists is supported. Conflict and hold events also atomically acquire their obligations.
Network/terminal work starts only after commit and after releasing the repository lease.

Settlement is idempotent for a request: a unique terminal request identity and conditional
completion prevent two terminal events/outbox rows after concurrent settlement or an ambiguous
commit acknowledgement. `FailAsync` first reloads this evidence. A saved terminal event means
notification recovery only; it must not fabricate a second LandingCleanup or LandRefused event.
An explicit later cleanup request still produces a legitimate new LandingCleanup event tied to
the same publication. If commit failed before persistence, the request remains pending and
CARD-0448 protocol recovery retains its existing job. If commit succeeded but its acknowledgement
was lost, reload and recover the existing notification, without repeating publication/cleanup.

Rejected: enqueue before saving publication (can announce rolled-back success); merely move the
current enqueue into the caller's transaction (Enqueue owns another scope and may deliver inline);
rerun git to recover a missing note; rely on a log or SignalR event as a durable obligation.

### D-4: idempotent outbox-to-queue handoff; recovery runs without another caller turn

Add scoped `AgentTaskLandNotificationService` and a hosted worker with an immediate boot scan
and a five-second periodic backstop. Use bounded wakeups only as an optimization. Scan all
unresolved requests' notifications, including those on Succeeded tasks whose pending land was
cleared. Paginate with a stable cursor and isolate row failures so one destination cannot starve
the rest. The land drain never waits for terminal verification or a caller idle window.

Add nullable `SessionQueuedMessage.SourceLandNotificationId` with a filtered unique database
index. Extend Enqueue with this key and an idempotent return/reload of the existing row ID;
do not repurpose CapacityRecoveryActionKey. Persist the key in the same queue insert as Body,
destination, Origin=Delegation, SourceTaskId, ContentDigest and NoteHeader. Use
`conversationKey=land:<notificationId>` and a compact first line with unique notification/request
identity, task ID and outcome. The digest is over the stable Land payload, not `task.Result`.
Never deduplicate by task or operation alone: repeated legitimate refusals and cleanup changes
must each be deliverable. Include destination in validation of an existing idempotency key.
Run the new keyed branch before the existing SourceTaskId/ContentDigest duplicate check and
restrict that old report check to rows without SourceLandNotificationId. A Land digest includes
the immutable notification identity and destination as well as payload, so identical prose in
different outcome events cannot collapse into one receipt.

Use WhenIdle and `deliverIfIdle:false`, then wake CompletionNoteFlushQueue. Source/digest
metadata makes the existing CompletionNoteWorkHostedService scan find a persisted row after a
lost wakeup, including on an already-idle non-AlwaysOn caller. Enqueue persistence success is
distinct from the later receipt. A crash after queue insertion but before recording QueueMessageId
is repaired by looking up SourceLandNotificationId; never insert a second row. Database uniqueness
must protect two workers/service providers racing beyond the in-process session semaphore.

Retry transient enqueue failures at 5, 10, 20 ... seconds capped at five minutes; persist the next
attempt time and error, and keep the obligation rather than exhausting it into silence. Missing
destination is immediately observable and retried conservatively without spawning or rerouting
a caller. Stopped/Failed callers retain their owed message. Existing queue attempt caps, parked
and truncated behavior remain authoritative once a row exists: the outbox never creates another
row to evade them. Intentional cancellation is shown as Canceled/unconfirmed, not Delivered;
any explicit operator resolution must retain that distinction.

Adding SourceTaskId makes this a participant in existing completion consumers. Audit
AgentTaskService read/poll handling, AgentTaskCheckService completion detection, failure reminders,
output distillation and queue polled-note shrinking. Ordinary delegate-completion predicates must
exclude SourceLandNotificationId where they mean a report; a Land note must not satisfy a missing
delegate report or be rewritten by task-result distillation/shrinking. Preserve source identity
for the machine-turn follow-up routing that legitimately uses it. A pending hold note may be
canceled as superseded after its terminal outcome is committed **only before any typing attempt**;
keep its event and supersession reason. Never cancel an attempted row to escape late confirmation.

### D-5: persist an observable receipt, including recovery after confirmation crashes

Receipt reconciliation examines each unresolved keyed queue row using production transcript
catch-up and matching rules. Match the actual immutable queued transport body (including a spill
pointer if the queue produced one) against a complete `TranscriptKinds.UserPrompt` in the
destination session. Reuse PromptSubmissionMatch identity/completeness and the saved
LastDeliveryBaselineSequence, or the existing attempt-time floor when no sequence baseline exists.
Include notification identity at the beginning of the body so two identical-looking outcomes
cannot acknowledge each other. Do not accept QueuedUserPrompt, QueueEnqueue, a screen redraw,
HTTP 202, terminal event, status poll, Sent alone or screen-confirmed Delivered.

Persist ConfirmedAt and the matching prompt sequence on the outbox. Reconciliation is idempotent
and can recover a receipt if the prompt was persisted just before a worker crash. A genuine
LateConfirmed queue row still needs its correlated prompt evidence. A truncated identity match
remains unconfirmed and parked. A screen-confirmed delivery with no transcript is visible as
unconfirmed; absence of proof never authorizes automatic retyping of that Sent row.

The notifier may catch up and inspect, but all typing/retry/Enter-only recovery remains in the
existing queue machinery. Keep LF, bracketed paste and the separate submitting Enter intact.
Retain keyed queue rows and relevant prompt evidence while their notification is unresolved;
after durable receipt the outbox retains the sequence/time proof through ordinary queue pruning.
Do not allow retention cleanup to delete the sole notification identity before receipt recovery.

### D-6: visibility is based on durable state and does not depend on task openness

Add a land-request/status projection alongside existing `landing` evidence in AgentTask detail:
current request ID/state, requested/started/evaluated/progress times, age, attempt, hold reason,
holder identity/status, publication/cleanup evidence, and each unresolved notification's status,
destination, queue row, error and receipt. Do not overwrite the delegate Result/Status.

`delegate.ps1 -Status` prints, before the report, for example:

```text
Delegate: Succeeded
Land: Held; requested 18m ago; attempt 0; no progress for 18m
Reason: repository_or_source_writer; holder c420add0 (Blocked)
Publication: Unconfirmed; cleanup: NotStarted
Notification: Held note queued for caller <id>; receipt unconfirmed
```

For a completed land print the precise outcome, operation ID, verified/remote commit,
confirmation time, cleanup and notification receipt separately. With no request say
`Land: Not requested`; with legacy evidence say what is known/unknown. A prior published operation
plus a held cleanup retry must display both facts rather than regress publication to Unconfirmed.

Preserve `[task <id> done]` and next-stage parsing, but add a lossless header bit to Worktree
delegate completion notes: `delegate=succeeded; publication=unconfirmed; land=not-requested`
(or the actual known state). A generic stage report's "done" must not imply publication.
Land outcome notes use a separate event identity and explicitly say publication and cleanup;
they do not claim the notification itself was received. Change `-Land`'s acceptance text to
`Queued land request <id>. Publication pending; status/hold and outcome notifications are tracked.`
Show `notification=not-required` when the request has no caller destination instead of promising
delivery unconditionally. No continuous manual polling requirement is introduced.

Add durable-state Attention projections (append enum values; preserve existing numeric values):

- LandHeld: immediate Warning on each current hold, with request/task/card, reason, holder and
  both ages; persists while held even if the task Succeeded and all incidents aged out.
- LandNoProgress: Warning after five minutes without meaningful admission/phase progress,
  Error after fifteen. Includes Queued, Held, Running and NeedsResolution, including an ID still
  in the process active set. For a held request this enriches/escalates its single LandHeld row.
- LandOutcomeUnconfirmed: Warning five minutes after an outcome is committed without a complete
  caller receipt, Error after fifteen; missing destination, enqueue errors or a parked/truncated
  row can immediately show the more specific error. Include outcome/publication, destination,
  missing/queued/attempted/unconfirmed status and last error. Busy-caller detail must say it is
  waiting for WhenIdle, not assert the caller is broken. Aged hold-note delivery uses the same
  delivery view but never claims a terminal business outcome.

Use typed validated DelegationSettings with the above defaults and TimeProvider for clocks.
LastEvaluatedAt, retry count, repeated starts at the same checkpoint, changed blockers and generic
`AgentTaskLanding.UpdatedAt` writes are not forward progress. Maintain a durable highest-progress
checkpoint for this request; advance only for first admission or newly acknowledged protocol
evidence, not cleanup retries revisiting the same phase. Total request age remains visible.

The monitor runs independently of the land drain at boot and on the sweep cadence; Attention can
recompute from durable rows if a wakeup is lost. Save first threshold crossings and owe one compact
aged caller note per severity, not one per tick. All unresolved states have stable Attention keys
outside recency filters. Resolve current hold/no-progress rows when the corresponding condition
clears; resolve receipt warnings on persisted complete receipt (NotRequired is an explicit separate
outcome). Deduplicate the generic CallerNoteUndelivered/ParkedMessage views against the keyed
Land item while retaining the queue's failure detail. Actions open the task, holder, caller or
queue; never kill, bypass exclusion, automatically resume an owner or route decisions to alerts.

### D-7: upgrade and historical evidence are explicit

Create EF migrations through the CLI. Add filtered uniqueness for one current pending request per
task, one terminal event per request, one notification per source event and one queue row per
notification; indexes support pending-age/due-notification scans. Handle legitimate nonterminal
Held/aged events separately from the terminal uniqueness constraint.

Backfill current pending tasks into requests using existing LandRequestedAt/StartedAt/Attempt/filter
and destination; evaluate current blockers afresh on the first sweep. Do not parse an old Held
string into current ownership. Existing operation evidence retains its semantics.

Historical terminal events without a notification obligation become explicitly LegacyUnverified
in detail/Attention, grouped by task's latest relevant outcome so history does not flood the feed.
Do not automatically replay old outcomes using a guessed historical destination. If an existing
legacy `land:<taskId>` queue row can be uniquely tied to an event by destination, time and exact
body, attach it idempotently and inspect receipt; ambiguous/missing evidence stays unverified.
This is a migration provenance limit, not a successful receipt. For all outcomes created after the
migration, including completion of pre-existing pending requests, the atomic obligation is required.
Known ReplyTo=None history is NotRequired; unknown historical routing is labeled unknown and
must not be asserted to be an owed session message. LegacyUnverified is informational evidence,
separate from the Warning/Error overdue predicate for recorded notification obligations.
No production data repair is executed by this Plan stage.

### D-8: standing producer-to-recipient verification rule

Code applies this exact requirement to `server/Bundles/stage-test-design.md`, inside its required
Verification design structure, and the matching audit paragraph to `stage-review.md`:

> For every new or changed asynchronous outcome-delivery path, enumerate producer, destination,
> persistence boundary, recovery, and observable receipt. Name the durable identity that connects
> them. Include a producer-to-recipient test through the real queue/delivery path, covering a busy
> recipient and one already eligible to receive, plus crash/enqueue-failure recovery at each
> handoff. An accepted request, queue insert, terminal business event, Sent flag or transport
> acknowledgement alone must not satisfy delivery acceptance. For session input require the
> matching complete UserPrompt transcript evidence. Declare substitutes and what each cannot
> prove. Test-design review must reject a design that stops before recipient evidence. Every
> safety-critical delivery/recovery guard needs a named positive control.

The review paragraph requires checking that inventory and the evidence behind its V/R/PC items,
and reporting a missing producer-to-recipient test as a defect. It creates no new pipeline role
or mandatory separate review stage. Update the orchestration/testing owners to point to this
standing requirement. Extend InstructionBundleTests' existing stage-invariant tests to pin the
five inventory fields, recipient acceptance and review obligation. A text invariant test protects
the process instruction; it is not evidence that any delivery path works.

No defaults above need a human decision. TestDesign should return next: plan if a concrete seam
cannot satisfy the durable receipt or fault-injection requirements, rather than weakening them.

## Delivery acceptance and TestDesign handoff

This is the required coverage inventory, not the separate executable Verification design. TestDesign
must append its normal V-n/R-n/PC-n, commands, expected assertions, exclusions and measured cost.

| Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|
| POST land / request acceptance | Background land worker | request + requested event before channel wakeup | boot sweep, lost/full channel, stale request identity | committed admission/hold tied to accepted request; intermediate evidence only |
| Hold and age monitor | caller session + Attention | structured hold/event/outbox in one commit | startup/periodic scans, repeated unchanged holds | durable Attention plus keyed complete UserPrompt for the hold/aged note |
| Land settlement/refusal/cleanup | caller session | terminal evidence + pending clear + notification obligation | commit-ack ambiguity, restart, enqueue failure | full keyed outcome UserPrompt, remote proof and notification receipt queried separately |
| Notification worker | real SessionMessageQueueService | unique notification key inserted with queue body | crash before/after insert or link, duplicate workers, dropped flush wakeup | same queue row reaches normal delivery; insertion alone is intermediate evidence |
| Real queue/transport | caller transcript | attempt/baseline -> received prompt -> durable receipt | queue late-confirm and interrupted-attempt recovery; receipt rescan | complete matched UserPrompt after the correct floor, one logical notification |

Required capstone harness: `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` with a dedicated
`Fixtures/LandDeliveryFixture.cs` adapting AntiphonAppFixture's real Kestrel Program, isolated
Postgres and isolated random-port session runner. Use a test-owned bare git remote, source
worktree and caller directory outside that source. Register a deterministic FakeGrok caller with
its private native home and modern ConPTY; production runtime/tailer/event-pump persistence must
produce the receipt. Fake the model only, not the queue, land service, drain, protocol or git
publication. Enable transcript confirmation. If FakeGrok needs a bounded busy/release mode,
extend that fixture behavior explicitly so it writes real native prompt/turn-end events.

Seed a succeeded Worktree task with ReplyTo=Session, clear LandRequestedAt, and bind its caller.
Invoke the **real** delegate.ps1 -Land via HTTP using a test caller token. Assert HTTP acceptance
precedes a controlled execution barrier, then let the actual hosted land drain run. Do not call
RunAsync/DeliverAsync/FlushSessionAsync from the test to supply a missing production trigger.
Assert remote containment directly in the test-owned remote; capture request, operation, terminal
event, notification, queue row and caller prompt identifiers in failure artifacts.

Mandatory scenarios:

1. **Already idle:** establish an observable idle caller before POST; no later human input or
   unrelated turn-end may wake it. The worker and recovery scan deliver the Land outcome and
   persist a complete matching prompt and receipt.
2. **Busy caller:** hold a real caller turn open when Land finishes. Observe committed outcome
   and Pending notification without terminal typing; release that turn and require the normal
   runtime turn-end path to deliver the unique note. A busy caller must not block another land.
3. **Held owner then release:** a same-repository Shared **Blocked** writer causes attempt zero,
   no operation/git mutation, current hold detail, durable Attention and caller hold receipt.
   Advance the monitor clock for aging/restart survival; release ownership through a test
   fixture's explicit state transition, then require automatic sweep -> publication -> outcome
   receipt without a second POST. Also test source writer, occupied lease and changed owner.
4. **Outcomes:** Landed, AlreadyPresent, LandedWithResidue, genuine subsequent LandingCleanup,
   and LandRefused, including a pre-operation refusal, all owe distinct correlated notes.
   Conflict remains NeedsResolution and visible; it must not be mislabeled landed.
5. **False positives:** unrelated or old identical prompt, wrong destination/request, header-only
   truncated prompt, QueuedUserPrompt/QueueEnqueue, screen-only Delivered and Sent/null verdict
   cannot confirm. Polling Status cannot suppress the Land note or discharge its receipt.

Fault matrix (fresh DbContext observers; restart uses a new host/provider, not a reused tracker):

| Cut | Required result |
|---|---|
| request committed, channel wakeup absent | boot sweep executes the original request |
| terminal transaction before commit | no terminal event/outbox/pending clear becomes visible; existing protocol recovery remains safe |
| terminal commit succeeds, acknowledgement throws or process dies | one outcome and obligation survive; no extra push/cleanup/event; notification scan recovers |
| obligation saved, enqueue not begun / fails | pending notification survives with error and due retry; committed publication stays true |
| queue insert committed, link/ack lost | recover same keyed queue row; exactly one insert despite two concurrent workers |
| queue persisted, flush wakeup lost, caller already idle | real periodic completion scan flushes without external input |
| typed / UserPrompt persisted, queue verdict or outbox receipt not saved | catch up and confirm without typing a second body; preserve attempt baseline |
| permanent missing caller or parked/truncated queue row | unresolved durable Attention; no reroute, new row, retry-cap bypass or false receipt |

Include at least one actual owned server-process termination between terminal commit and enqueue,
and one after queue commit before linkage, resuming against the same isolated database/runner.
Save-failure interceptors cover before/after acknowledgement combinations cheaply; they do not
replace the process-crash controls. Kill only the fixture-owned server PID after a named barrier;
the fixture retains the runner and database until recovery evidence is captured.
AntiphonAppFixture currently hosts Program in-process: the crash arm therefore needs a separate
fixture-owned server child mode, with owned port/database/runner configuration and a barrier
receipt. Do not terminate the E2E test runner or call graceful host disposal a hard-crash test.

Positive controls must make the capstones fail when: the producer omits an obligation; enqueue
metadata/wakeup recovery is removed; the keyed queue uniqueness/idempotency guard is removed;
the real idle flush is disabled; Sent/screen-only is accepted as receipt; writer exclusion drops
Blocked; or polling/age recomputation falsely clears an unresolved state. TestDesign chooses exact
test methods and one-line mutations with expected assertion failures. Preserve independent remote
publication assertions so a passing receipt cannot hide a publication failure.

## Implementation slices and files

| Slice | Files / work | Named tests to extend or add |
|---|---|---|
| S1 Request/notification persistence | new Domain/Entities/AgentTaskLandRequest.cs and AgentTaskLandNotification.cs; Domain/Enums/LandingEnums.cs; AgentTask.cs; SessionQueuedMessage.cs; Infrastructure/Data/AppDbContext.cs; CLI-generated migrations; DTOs | new AgentTaskLandNotificationPersistenceTests; AgentTaskLandingPersistenceTests; AgentTaskLandRequestTests; legacy migration fixtures |
| S2 Admission visibility and monitor | AgentTaskLandService.cs, AgentTaskLandQueue.cs, AgentTaskLandingProtocol.cs progress checkpoints, DelegationSettings.cs; new AgentTaskLandMonitorService.cs; LandHostedService/LandSweepHostedService identity/cadence | new AgentTaskLandHoldVisibilityTests and AgentTaskLandMonitoringTests; AgentTaskLandSweepTests, AgentTaskLandAdmissionTests, AgentTaskLandConcurrencyTests |
| S3 Atomic outcomes and notifier | LandService settlement/refusal/FailAsync; new AgentTaskLandNotificationService.cs and Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs; Program DI; SessionMessageQueueService keyed enqueue; CompletionNoteWorkHostedService integration and report-only consumer predicates | new AgentTaskLandNotificationRecoveryTests; AgentTaskLandPersistenceFailureTests, AgentTaskLandRecoveryTests, OutputDistillationQueueTests, PolledCompletionNoteShrinkTests |
| S4 Receipt, status and Attention | notifier transcript catch-up/matcher; retention policies for keyed rows; AgentTaskService.cs, AgentTaskDtos.cs, LandingEvidenceDto.cs; AttentionService.cs/AttentionDtos.cs; scripts/delegate.ps1; DelegationReportFormatter.cs; client API and task/attention components | new AgentTaskLandReceiptTests, DelegateScriptLandStatusTests; AttentionServiceTests, DelegationReportFormatterTests, SessionMessageQueueInterruptedAttemptTests; TaskDrawer/attentionVisuals tests |
| S5 Composed delivery and failure controls | tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs and Fixtures/LandDeliveryFixture.cs; AntiphonAppFixture isolated test configuration and restart hooks; test-owned git fixture; native fake busy gate only if needed | busy/idle/hold-release capstones and process crash controls above; retain LandingSafetyHarness's ReplyTo=None default for safety-only tests |
| S6 Standing process/docs | server/Bundles/stage-test-design.md, stage-review.md; docs/orchestration-loop.md, docs/ops-http.md, docs/testing-and-build.md, docs/session-runtime-invariants.md | InstructionBundleTests stage invariant/composition tests; no claim that bundle text tests replace behavioral coverage |

Implementation order: S1 -> S2/S3 -> S4 -> S5 -> S6; introduce the test harness seams with their
owning slices. Apply S6's process requirement to this card's TestDesign immediately through this
plan/handoff even before the bundle change ships. Land the Plan commit before dispatching the
next Worktree stage so the artifact is present on its base branch.

## Validation and delivery boundaries

Plan-stage verification: source/owner review and document consistency only; no application tests,
builds, live land requests or live notifications were run. This document does not claim a fixed
runtime. The only deliverable in this stage is this committed/pushed plan.

TestDesign must give executable class/method filters, not a namespace-wide default. TUnit runs
via `dotnet run --project tests/<project>`; use a task-specific alternate output directory with
a forward slash, count executed tests in fresh TRX, and run Antiphon.Tests and
Antiphon.Agents.Pty.Tests sequentially. PC red/green runs are exact-method-scoped. Process-spawning
classes carry the assembly-local ProcessSpawnLimit; declare Windows/modern-ConPTY dependencies.
Skipped capstones or missing fake binaries are missing acceptance evidence, not passing coverage.
E2E uses its owned runner, never production 17204. Rebuild client/dist before E2E according to the
testing owner. Use `pwsh -File scripts/test-client.ps1` for the touched client tests. Do not add a
local scheduled task or exercise a live caller to manufacture proof.

Out of scope: rewriting generic terminal submission, provider-specific policy refresh, deploying
the stack, resolving the incident's Shared owner, auto-closing the card, guaranteed semantic
understanding of a note by a model, and automatic replay of unprovable legacy notifications.
A complete UserPrompt proves receipt by the session, not that its next model response took the
right action. Real-model behavior is not needed for this transport acceptance.
