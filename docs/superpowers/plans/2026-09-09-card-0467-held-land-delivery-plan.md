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

## Verification design

TestDesign, 2026-09-09, task `311fb3cb`; reviewed plan commit `808ce854` from worktree
baseline `10cfbc63`. This section specifies tests to implement and execute during Code; it
does not claim that the proposed classes, seams or tests already exist. D-1 through D-8 remain
the implementation contract. In particular, a **Blocked writer still excludes landing**.

### Delivery inventory and executable fixture contract

Apply D-8 to this design now. The identity chain is
`TaskId -> RequestId -> SourceEventId -> NotificationId -> SourceLandNotificationId/QueueMessageId
-> destination session + ConfirmingPromptSequence`. Operation ID is optional before admission
and is not a notification deduplication key. Every failure artifact records the entire available
chain, with publication and receipt as separate verdicts.

| Producer | Destination | Persistence boundary and durable identity | Recovery exercised | Observable receipt and covering tests |
|---|---|---|---|---|
| Real `delegate.ps1 -Land` / HTTP request | Hosted land drain | Request plus LandRequested event; `(TaskId, RequestId)` wakeup | Dropped wakeup, new host, concurrent POST, stale channel item | Committed matching admission/hold, not delivery acceptance; V-1, V-2, V-27 |
| Land admission and independent age monitor | Caller session and Attention | Current hold/checkpoint, episode/threshold event and immutable notification in one commit | Restart, owner change, lost wakeup, repeated evaluation, attempt-zero and active requests | Stable Attention plus complete keyed hold/aged UserPrompt; V-3, V-4, V-15, V-16, V-24 |
| Land settlement, refusal, conflict and cleanup | Snapshotted caller session | Business evidence, terminal request/event where applicable, pending clear and outbox in one commit | Before-commit failure, lost acknowledgement, actual server death before enqueue | Independent remote containment and complete keyed outcome UserPrompt; V-5 through V-7, V-25, V-29, V-31 |
| Notification worker | Real session queue | Unique notification key, destination, immutable transport payload and source metadata in the queue insert | Enqueue errors, insert/link ambiguity, two providers, pagination, missing caller | Same queue row enters normal delivery; insert alone is insufficient; V-8 through V-10, V-26, V-28, V-31 |
| Production queue, runner, native tailer and runtime event pump | Caller transcript, then durable outbox receipt | Attempt/baseline -> actual UserPrompt -> ConfirmedAt/sequence | Lost flush, delayed catch-up, typed-before-verdict/receipt failure, late confirmation and pruning | Complete matching UserPrompt in the right session after the attempt floor; V-11 through V-14, V-22 through V-24, V-28, V-30, V-32 |

Use these two complementary fixtures:

- **Integration:** extend `LandingSafetyHarness` only for its safety/real-git use cases; keep its
  default ReplyTo=None. New delivery-focused integration helpers compose the real request,
  protocol, notification, queue and monitor services with `TestDbFixture` isolated schemas,
  `LandingGitFixture`, `DelegationTestServices`, independent service providers for races and
  fresh `AsNoTracking` database observers. Direct service ticks and seeded transcript rows are
  allowed here to isolate an assertion. They prove transactions, predicates and matching,
  not production scheduling or terminal receipt.
- **E2E:** add `AgentTaskLandDeliveryE2ETests` and `Fixtures/LandDeliveryFixture`. Adapt the
  real Kestrel `Program` setup in `AntiphonAppFixture`, `IsolatedSessionRunner` and the owned
  bare-remote/source fixture. Add FakeGrok output staging to `Antiphon.E2E.csproj`, following
  the existing Pty.Tests copy target so alternate outputs contain `fakegrok/fakegrok.exe`.
  Use modern ConPTY, a private `GROK_HOME`, a non-AlwaysOn caller outside the source worktree,
  and `Supervision:DeliveryVerification:Enabled=true` plus
  `Supervision:DeliveryVerification:TranscriptConfirmEnabled=true`. Assert these
  resolved settings and loaded server/runner/fake binary identities before exercising Land.
  Disable check interpreter, diagnose, distiller, channel bridge and live messaging; retain the
  Land drain/sweep/monitor/notifier, completion worker and runtime event pump. Refuse external
  messaging and assert the runner is fixture-owned and is not port 17204.

The E2E fixture creates a test caller/token and a succeeded Worktree task with no pending land,
using production token construction. Invoke the checked-out `scripts/delegate.ps1` with
`ProcessStartInfo.ArgumentList`, `ANTIPHON_API` set to the owned host and the test token only in
the child environment. Adapt `DelegateScriptRunner`'s invocation pattern; do not reference the
whole Antiphon.Tests assembly just for that helper. Record the HTTP 202 body via passive HTTP
observation and correlate its request ID with the script's output. A barrier after durable
request acceptance, before execution, proves the script returns while publication is pending.
Tests may release barriers, observe HTTP/DB/runner data, and explicitly settle a **test-owned**
writer. They may not invoke RunAsync, DeliverAsync, FlushSessionAsync or FlushIfIdleAsync to
supply a missing production trigger, copy a Land note into the terminal, or insert a confirming
transcript row in any E2E arm.

Add a default-off FakeGrok file gate for one marked caller turn: write the native
`user_message_chunk`, acknowledge that the turn is held, and withhold `turn_completed` until
the fixture releases the gate. Continue reading input while held so an erroneous early submit
is recorded and detected rather than silently buffered. Release through the control file, not
another user prompt; then emit the normal native completion/idle evidence. Add
`FakeGrokContractTests.C467_BusyGatePreservesNativePromptAndTurnEnd` for this opt-in behavior
and run existing FakeGrok contract tests unchanged. Use a long, multi-line ASCII outcome body
with a distinctive beginning, middle and tail. FakeGrok drops LF; use the production
`PromptSubmissionMatch.IsConfirmedBy` **and** `IsCompleteIn` whitespace rules, not byte equality
and not only the 200-character identity window. A spill case compares the actual queued
transport pointer and verifies that the fixture-owned spill file contains the full payload;
it does not claim the model read that file.

The crash fixture owns database, repository, runner and caller independently of the server
host. Implement a test-assembly child entry, analogous to
`LandingSafetyHarness.RunCrashWorkerAsync`, which starts the same real Kestrel factory and
awaits indefinitely. Its parent starts it using pwsh/reflection or a dedicated test executable,
with all overrides installed by the factory; no public fault endpoint or production fault
setting is required. Refactor fixture ownership so restarting the child neither reseeds nor
disposes the existing database/runner. Use a fresh host/provider and verify its new PID/MVID,
same database and caller, and fresh transcript catch-up on recovery.

Provide awaitable, instance-scoped test barriers at the cuts below. EF save/transaction
interceptors must distinguish `SavedChanges` from **transaction committed**; acknowledge a
durable cut only after an independent connection sees it. A test callback or pass-through I/O
decorator may delay or throw at a boundary, but may not implement the business/queue path.
For terminal-before-enqueue, block the notifier before its first enqueue as well as the producer
after commit: an independently scanning worker must not race past the intended crash cut.
Barrier receipts contain cut, nonce, PID/start time and correlation IDs. The parent confirms
ownership, kills only that server child (`entireProcessTree:false`), awaits its exit, and keeps
the runner/database alive. Graceful disposal and killing the E2E test process do not count.
Trigger the terminal barrier from the committed terminal event, without requiring an outbox row
to exist: PC-7 must fail the missing-obligation assertion, not time out in fixture setup.
For insert/link recovery, use the queue's keyed Enqueue branch to validate/reload the existing
row and return its ID; record that this branch ran before linking. This makes V-26/PC-13 exercise
production idempotent handoff, rather than a test-only shortcut around that guard.

| Cut | Arrange and required fresh-observer assertion | Test |
|---|---|---|
| Request committed, wakeup absent | Drop only the request-channel wakeup; restart with the original ID/age still pending | V-27 |
| Terminal transaction before commit | Throw before commit, including after SaveChanges inside the transaction; no terminal event/outbox/pending clear visible | V-6 |
| Terminal commit acknowledged ambiguously | Throw after independently observed commit, then call the normal error/recovery path; one terminal identity and no additional git mutation | V-6, V-7 |
| Terminal committed, enqueue not begun | Hard-kill at the named barrier with pending fields cleared and no queue row; boot notification scan alone must recover | V-25 |
| Enqueue fails before insert | Fail first two notification queue inserts without failing the terminal commit; original obligation remains due with persisted error | V-8, V-31 |
| Queue committed, link not persisted | Hard-kill after observing the keyed queue insert, while QueueMessageId is absent on the outbox; recover the same row | V-26 |
| Queue persisted, flush wakeup absent | Drop only its completion-flush wakeup; already-idle caller stays untouched by the test; periodic completion scan delivers | V-28 |
| Body attempted / prompt persisted, verdict or receipt not saved | Arm separate failures at each save; restart observer and catch up from real native evidence without another body write | V-12, V-30 |
| Destination unavailable or queue parked/truncated | Keep the unresolved obligation and actionable error through restart and age thresholds; no new row/caller or false receipt | V-8, V-13, V-16, V-32 |

Use real time for queue polling/deadlines. For fast age/retry tests, use an offset over real time
or an auto-advancing FakeTimeProvider; never freeze the whole server clock. Exact 5/15-minute
boundary tests use an isolated monitor clock, outside queue loops. In E2E, inject the shared
offset clock only into Land request/monitor/notification services so their event/due timestamps
agree; keep queue/runtime deadlines on TimeProvider.System. Let E2E scans run at their
production cadence; gates remove ordering guesses. Use cancellation-bounded waits (60 seconds
for delivery after eligibility, 120 seconds for host startup/crash recovery), fail with the
last observed state, and measure actual latency. A negative assertion spans at least two
observed worker passes while its gate remains closed; a short arbitrary sleep is not evidence.
Crash/recovery and busy tests also count native keyed prompt records/body writes through the
observation window after receipt: one logical notification must not become two submissions.

### Proves it works now

`I` below means integration in `tests/Antiphon.Tests/Application/<Class>.cs`, unless a row names
another path. `E` means E2E in `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs`. Names are the
required new methods, not claims of existing discovery. Each matrix row becomes an explicit
TUnit Arguments/DataSource case with its case name visible in results; no hidden loop that
reports only its first failure. All positive-path E2E methods use real Land production and the
inventory's full receipt oracle. No skipped capstone is acceptance evidence.

| ID | Layer and exact class.method / client test | Arrange, action and required assertion |
|---|---|---|
| V-1 | I `AgentTaskLandRequestTests.C467_V01_AcceptRequeueSerializeRequest` | Initial POST commits request/event and returns its ID; inactive requeue preserves ID/age/attempt and permits filter update; active request is 409 without mutation. Race two contexts at acceptance: at most one current request; each response is the same accepted ID or documented 409, never a unique-constraint/500 failure. A terminal retry gets a new ID/reset attempts and preserves history/destination snapshots. |
| V-2 | I `AgentTaskLandRequestTests.C467_V02_RejectStaleWorkAndExposeMirrorDrift` | Drain old ID after a newer request, and canceled/superseded IDs: no git or newer-row mutation. NeedsResolution is not swept as Succeeded. Compatibility-field disagreement is visible and never creates publication/receipt. Cancel a completed request's task and retain its owed terminal note. |
| V-3 | I `AgentTaskLandHoldVisibilityTests.C467_V03_RealWriterLeaseAndEpisodeMatrix` | Exercise real FindWriterAsync for Shared Dispatched/Working/Blocked in common-repo aliases, source writers/path aliases, inaccessible identity, and an occupied lease. Unrelated repository is the negative control. Each exclusion gives zero attempts, no protocol entry/operation/mutating git calls, correct reason/actual holder (unknown for unexposed lease owner). Capture a faulty run's exception and assert admission/protocol-entry counters before rethrowing, so lease-bypass PCs fail on the safety assertion rather than a downstream null-lease error. Unchanged sweeps do not duplicate; changed reason/holder creates one episode; admission clears current hold and records HeldReleased. |
| V-4 | I `AgentTaskLandHoldVisibilityTests.C467_V04_HoldAndAgeAreAtomicBeforePublish` | Fault hold and aged-event transactions before/after commit; current state/event/outbox appear together or not at all. IEventBus observer opens a fresh connection: AgentTaskChanged cannot precede persistence. Restart reconstructs same episode/threshold obligation. |
| V-5 | I `AgentTaskLandNotificationPersistenceTests.C467_V05_OutcomeObligationMatrix` | Real git fixtures produce Landed, AlreadyPresent, LandedWithResidue, later LandingCleanup, pre-operation LandRefused, operation-backed LandRefused and Conflict. Assert correct publication/cleanup, event linkage, immutable body/digest/destination and one obligation per event; terminal pending clear is atomic. Conflict stays NeedsResolution/Blocked with existing helper behavior and no claimed publication. Enqueue starts only after commit and repository lease release. |
| V-6 | I `AgentTaskLandPersistenceFailureTests.C467_V06_AtomicSettlementFaultMatrix` | For success/refusal/cleanup: before-save, after-save-before-commit, commit failure, and committed-but-acknowledgement-lost. First three leave no partial terminal transaction; last preserves one complete transaction. Recovery uses committed protocol checkpoints; FailAsync cannot emit a second terminal event or repeat publication/cleanup after committed settlement. |
| V-7 | I `AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` | Race terminal settlement/FailAsync across contexts; unique terminal request/event/obligation, including a commit-ack retry. Separate explicit cleanup request after residue legitimately shares the operation but creates a different event, notification, digest and queue row. Raw duplicate inserts verify pending-request, terminal-event and SourceEventId uniqueness; multiple nonterminal hold/age events remain legal. |
| V-8 | I `AgentTaskLandNotificationRecoveryTests.C467_V08_RetryAndDestinationMatrix` | Enqueue errors persist 5,10,20,40,80,160,300,300-second due offsets, attempt/error and original obligation; no early retry or exhaustion into silence. None -> NotRequired. Missing Session destination -> DestinationUnavailable, never NotRequired; deleted/stopped/failed caller retains obligation without spawn/reroute. Destination snapshot survives task edits. On recovery one queue row carries all source/key/header/digest metadata and WhenIdle with deliverIfIdle=false. |
| V-9 | I `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` | Two independently constructed providers rendezvous after absent-key reads and enqueue concurrently into real Postgres. One row/returned ID, no unhandled unique violation. Independently try a raw duplicate key to pin the database constraint. Same task/body but different event/destination identities remains distinct; replay of same key with wrong destination is refused. Keyed branch precedes report dedup; ordinary report dedup still works. |
| V-10 | I `AgentTaskLandNotificationRecoveryTests.C467_V10_BootScanFairnessAndClearedPending` | More than two configured pages, with failed/not-yet-due/confirmed rows interspersed and Succeeded tasks whose pending fields are null. Fresh hosted worker boot plus periodic scans eventually process every eligible row; a failing first row cannot starve the tail. Drop bounded notifier wakeups and keep the same result without manual ticks. |
| V-11 | I `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | Cases: only QueueEnqueue/QueuedUserPrompt, Sent/null verdict, screen-only Delivered, wrong session, wrong notification/request, old identical prompt at/below sequence baseline, old prompt before attempt-time floor with no sequence, head-only clip, head+tail splice, unrelated later prompt. Assert ConfirmedAt/sequence remain null; LateConfirmed flag alone also fails. Matching complete newer UserPrompt is the paired positive case. |
| V-12 | I `AgentTaskLandReceiptTests.C467_V12_CatchUpAndRecoverReceiptWithoutRetyping` | Withhold event-pump persistence but expose matching native evidence through CatchUpTranscriptAsync; production catch-up must persist it. Fail queue-verdict and outbox-receipt saves separately, use new scopes/providers, reconcile twice: one prompt, saved correct sequence/time, zero new body/Enter calls. Include long/spilled transport body, newline flattening and genuine late-confirm. |
| V-13 | I `AgentTaskLandReceiptTests.C467_V13_RetentionCancellationAndSupersession` | Attempt-zero hold note may be superseded only after terminal commit, preserving reason/event. Attempted/parked/truncated rows are retained and not canceled/replaced to bypass retry caps. Explicit queue cancellation remains Canceled/unconfirmed. Retention preserves unresolved key and prompt evidence; after receipt ordinary pruning may remove queue history while outbox proof survives. Session deletion cannot cascade away the obligation. |
| V-14 | I `AgentTaskLandNotificationRecoveryTests.C467_V14_LandNotesDoNotCountAsReports` | Exercise the real AgentTaskService read/poll, AgentTaskCheckService completion check, failure-reminder selection, output-distillation and polled-note shrinking with a Land row plus a separate ordinary report control. Polling/read stamps may acknowledge the report only; Land payload/digest/receipt remain unchanged. A Land note cannot satisfy a missing report or suppress its reminder. Retain SourceTaskId for machine-turn follow-up routing. |
| V-15 | I `AgentTaskLandMonitoringTests.C467_V15_ThresholdsUseMeaningfulProgress` | Queued/Held/Running/NeedsResolution at 4:59.999, 5:00, 14:59.999 and 15:00 without progress; include attempt zero and process-active ID. Warning then Error, one durable threshold event/note per severity across restart/races. Repeated evaluations, starts at same checkpoint, blocker changes, retry count and UpdatedAt do not reset age; first admission/new checkpoint does. Validate configured positive ordered thresholds and defaults; request age remains intact. |
| V-16 | I `AgentTaskLandMonitoringTests.C467_V16_AttentionSurvivesRecencyAndDeduplicates` | Query real Attention after aging incidents out and restarting on Succeeded tasks. One enriched LandHeld item per current hold, independent no-progress and outcome receipt conditions, stable keys and specific missing/queued/busy/parked errors. 5/15-minute outcome thresholds start at outcome commit. Deduplicate generic CallerNoteUndelivered/ParkedMessage only for matching keyed Land; unrelated queue warnings remain. Complete receipt resolves receipt warning, release resolves hold, NotRequired is explicit; no task/card moves, owner stop/resume, SendNow or alerts. |
| V-17 | I `AgentTaskLandNotificationPersistenceTests.C467_V17_UpgradeAndLegacyEvidence` | Migrate a schema at the immediately preceding migration with aged pending rows, terminal history, ReplyTo=None, unknown routing, a uniquely matchable legacy queue row and ambiguous rows. Backfill preserves clocks/attempt/filter and evaluates blocker afresh. Latest relevant legacy outcome is informational LegacyUnverified without replay; known None is NotRequired. Unique legacy attachment is idempotent and needs real receipt evidence. New completion of a backfilled pending request always creates atomic outbox. Verify index catalog and migration rerun safety. |
| V-18 | I `DelegateScriptLandStatusTests.C467_V18_StatusAndAcceptance` | Run real pwsh script against test HTTP responses for no request, held Blocked owner/attempt zero, running, published-with-residue plus held cleanup retry, each terminal outcome, queued/unconfirmed/confirmed, legacy unknown and None. Assert exact meaningful fields before the report, additive request ID acceptance text, no publication/receipt inferred from Succeeded or HTTP 202. |
| V-19 | I `DelegationReportFormatterTests.C467_V19_CompletionHeadersSeparatePublication` | Worktree report header preserves `[task ... done]`, next-stage parse and lossless delegate/publication/land bits; actual known land state is used. Event-specific Land notes distinguish publication/cleanup and never announce their own receipt. Non-Worktree/report parsing controls remain green. |
| V-20 | Client `TaskDrawer.test.tsx` and `attentionVisuals.test.ts` | Add named cases `shows a blocked land separately from delegate success`, `keeps published evidence during a held cleanup retry`, `shows unconfirmed receipt and destination errors`, `invalidates land detail after AgentTaskChanged`, and `opens land task holder caller and queue without mutation`. Pin appended Attention enum values against prior numeric values and task/attention DTO serialization in V-16. Component tests use DTO fixtures and prove rendering/actions only. |
| V-21 | I/unit `InstructionBundleTests.C467_V21_DeliveryInventoryAndReviewAreMandatory` | Pin D-8's five inventory fields, durable identity, busy/already-eligible recipient, every-handoff fault recovery, complete UserPrompt acceptance, substitute declaration and named positive controls in StageTestDesign. Pin matching StageReview inventory/evidence audit and explicit rejection of designs stopping before recipient proof. Run existing stage invariants, ASCII/2,500-char caps and composition tests; preserve required structure and read-only Review. |
| V-22 | E `C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput` | Establish real native TurnEnd/idle before POST, release execution gate and supply no later input/turn-end. Require remote ancestry from test remote, terminal obligation, full unique UserPrompt and persisted receipt via production workers. Observe two more scans with no duplicate prompt. |
| V-23 | E `C467_V23_BusyCallerDoesNotBlockAnotherLand` | Gate a real caller turn, finish first land and observe pending outcome with zero keyed prompt/body attempts. Land a second task in another owned repository to an idle caller and require its complete receipt while the first caller is still busy. Release the first turn through the fake gate; normal runtime TurnEnd delivers exactly once. |
| V-24 | E `C467_V24_BlockedWriterThenReleaseDeliversBothNotes` | Seed same-common-repo Shared Blocked writer. POST once: attempt zero, no operation/git mutation, correct current holder and Attention, complete hold-note receipt. Advance monitor age past both thresholds, require aged-note receipts and single warning/error obligations; restart retains ages/identities. Settle only fixture writer explicitly, then automatic sweep publishes and delivers terminal note without re-POST. Require HeldReleased and resolved current hold/receipt warnings. |
| V-25 | E `C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt` | Kill server child at terminal-commit/before-enqueue barrier. Fresh connection proves remote-confirmed terminal event/outbox and no queue row. New child against same DB/runner automatically delivers and confirms; no extra push, cleanup, terminal event or obligation. |
| V-26 | E `C467_V26_HardCrashAfterQueueInsertReusesRow` | Kill server child at queue-commit/before-link barrier, holding delivery until observation is captured. Restart recovers same SourceLandNotificationId/queue ID and full receipt, with exactly one queue insert/logical prompt. Busy caller control may delay typing across crash; release through native gate only. V-9 supplies the independent-provider insert race. |
| V-27 | E `C467_V27_LostRequestWakeupRecoversAtBoot` | Drop request wakeup after acceptance and stop child before periodic sweep can consume it. Restart with fresh channel/provider: same request ID/age is admitted, remotely published and fully received. No second POST or direct queue/service call. |
| V-28 | E `C467_V28_LostFlushWakeupRecoversOnIdleCaller` | Already-idle non-AlwaysOn caller; let Land persist keyed queue row, drop its immediate flush wakeup, and do not produce another TurnEnd. CompletionNoteWorkHostedService periodic scan discovers source/digest, normal idle flush delivers, and receipt is persisted. Record scan provenance so an unrelated wakeup cannot satisfy this test. |
| V-29 | E `C467_V29_RealOutcomeProducerMatrix` | Real-git cases: Landed, independently AlreadyPresent, residue from a controlled cleanup failure, legitimate later cleanup after removing that obstacle, pre-operation and operation-backed refusal, and real rebase conflict. Each produces its own full keyed caller UserPrompt and correct state; successful outcomes prove remote ancestry directly. Refusals never infer publication from local/all-ref presence. Conflict retains NeedsResolution and helper behavior without auto-resume. Repeated identical refusal prose across explicit requests produces distinct notes. |
| V-30 | E `C467_V30_ReceiptSaveFailureNeverRetypes` | Fault after native prompt is persisted, before queue verdict and before outbox receipt in separate cases. Restart host, catch up and save receipt with original attempt floor; retain one native keyed prompt/body attempt. No test-seeded transcript or manually submitted note. |
| V-31 | E `C467_V31_EnqueueFailureRecoversAutomatically` | Fail first two queue inserts after real terminal commit; show RetryPending/LastError/NextAttemptAt from a fresh observer, then recover at persisted due times without new POST/input. Remote publication stays true throughout; final UserPrompt and receipt match the original notification. |
| V-32 | E `C467_V32_StatusPollingCannotDischargeUnreceivedOutcome` | Two cases: busy caller across outcome commit; already-idle caller paused at the real queue's Sent/attempt-save-before-typing I/O barrier. Repeatedly run real -Status while moving Land monitor clock beyond 5/15 minutes. Outcome remains unconfirmed with no matching native prompt, Attention persists with accurate busy/attempted detail and one aged note per severity. Release gate; only actual complete receipt resolves its warning. Re-read state after restart to rule out in-memory clearing. |

For V-29 use genuine repository arrangements, not fabricated successful protocol results:
AlreadyPresent pre-publishes the source into the owned bare remote; residue uses a one-shot
cleanup I/O failure after confirmed publication; refusal uses absent source identity or a
rejected owned-remote push; conflict uses opposing source/target edits. Assert source/recovery
refs and user content remain protected on failure. The integration fault decorator observes
real git commands and may fail the selected I/O; it cannot substitute remote containment.

### Guards the regression

Every R item names the change it would catch. Run the associated V methods plus the listed
existing class filters; a copied helper assertion alone is not retained coverage.

| ID | Regression | Caught by / decisive assertion |
|---|---|---|
| R-1 | Requeue hides an old request, or stale queue work mutates a new request | V-1/V-2/V-27: unchanged identity/age and no stale git; existing `AgentTaskLandRequestTests`, `AgentTaskLandSweepTests` |
| R-2 | Blocked/aliased writer or occupied lease is treated as permission to land | V-3/V-24: real guard, zero attempts/mutations; `AgentTaskLandAdmissionTests`, `AgentTaskLandConcurrencyTests`, `AgentTaskLandIdentityMatrixTests` |
| R-3 | Holds become silent or emit a note per tick | V-3/V-4/V-15/V-24: structured episode, atomic obligation, one event per transition, complete hold/aged receipt |
| R-4 | Publication and pending clear survive without an owed note | V-5/V-6/V-25/V-31: fresh-observer atomicity and terminal-commit hard crash; `AgentTaskLandPersistenceFailureTests`, `AgentTaskLandRecoveryTests` |
| R-5 | Error recovery repeats publication/cleanup or collapses later cleanup into an old note | V-7/V-9/V-26/V-29: stable request terminal identity and distinct legitimate event identities; `AgentTaskLandPublicationTests`, `AgentTaskLandCleanupSafetyTests`, `AgentTaskLandStageOutcomeTests` |
| R-6 | In-memory wakeups or open-task queries strand owed work | V-8/V-10/V-22/V-27/V-28/V-31: boot/due scans recover cleared-pending outcomes and already-idle callers |
| R-7 | Same notification duplicates across processes, or report dedup collapses distinct Land events | V-9/V-26/V-29: database uniqueness, same returned queue ID, distinct event notes and one actual prompt |
| R-8 | Busy delivery types early or stalls the land drain | V-23: no first-caller attempt while second land and receipt finish; existing `SessionMessageQueuePtyIntegrationTests`, `SessionMessageQueueDeliveryVerificationTests` |
| R-9 | Sent, screen, old/wrong/truncated text passes as receipt | V-11/V-12/V-30/V-32: complete correct-session UserPrompt after floor and no recovery retype; existing `SessionMessageQueueInterruptedAttemptTests` |
| R-10 | New SourceTaskId metadata alters ordinary report consumers or cancels attempted rows | V-13/V-14/V-32: report controls still work, Land body/obligation survives; `OutputDistillationQueueTests`, `PolledCompletionNoteShrinkTests` |
| R-11 | Polling, changed blockers, progress timestamps or recency hide silence | V-15/V-16/V-24/V-32: durable ages/keys, thresholds once, stable unresolved evidence; `AttentionServiceTests` |
| R-12 | Upgrade fabricates receipt/destination or automatically replays history | V-17: explicit LegacyUnverified/NotRequired, no guessed replay, new atomic obligation for upgraded pending work; `AgentTaskLandingPersistenceTests` |
| R-13 | UI/script/report conflates delegate completion, publication and receipt | V-18/V-19/V-20: separate facts and preserved completion parser; `DelegationReportFormatterTests`, touched client files |
| R-14 | Future TestDesign stops at the queue or Review accepts that gap | V-21: standing inventory and rejection rule retained; existing `InstructionBundleTests` stage/cap/composition coverage |

### Positive controls

Code executes **every** PC red -> restore -> green against the exact method named below,
including all named matrix cases. These are temporary one-line source mutations at the stated
semantic anchor in the implemented slice; record the actual file/line and diff. Where a guard
is new, the expression below specifies which behavior to replace, not a requirement to expose
a production mutation switch. A compiler error, fixture failure, unavailable binary, skip or
zero tests does not satisfy red. Red must contain the named invariant's assertion failure.
If a lower assertion trips first, improve the test to reach the targeted observable failure.

| ID | One-line mutation / owning anchor | Exact method to run | Expected red assertion |
|---|---|---|---|
| PC-1 | Land request requeue: replace preserved RequestedAt with `now` | `AgentTaskLandRequestTests.C467_V01_AcceptRequeueSerializeRequest` | Original age changed on inactive requeue |
| PC-2 | Drain identity guard: replace current-request-ID equality with `true` | `AgentTaskLandRequestTests.C467_V02_RejectStaleWorkAndExposeMirrorDrift` | Stale ID performs work or changes newer request |
| PC-3 | FindWriterAsync status predicate: remove `Blocked` | `AgentTaskLandDeliveryE2ETests.C467_V24_BlockedWriterThenReleaseDeliversBothNotes` | Attempt-zero/no-mutation hold and holder/hold receipt missing |
| PC-4 | Land admission: replace `if (lease is null)` with `if (false)` | `AgentTaskLandHoldVisibilityTests.C467_V03_RealWriterLeaseAndEpisodeMatrix` | Busy lease no longer holds; attempt/protocol-entry counter advances without lease |
| PC-5 | FindWriterAsync inaccessible identity branch: return no writer | `AgentTaskLandHoldVisibilityTests.C467_V03_RealWriterLeaseAndEpisodeMatrix` | Fail-closed identity case is admitted/mutates |
| PC-6 | Shared hold persistence path: remove notification-obligation add | `AgentTaskLandHoldVisibilityTests.C467_V04_HoldAndAgeAreAtomicBeforePublish` | Committed hold/aged event has no atomic outbox |
| PC-7 | Terminal settlement: remove the outcome-notification add before commit | `AgentTaskLandDeliveryE2ETests.C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt` | Terminal commit has no recoverable obligation/complete receipt |
| PC-8 | Terminal transaction: move pending-field clearing save to immediately before BeginTransactionAsync | `AgentTaskLandPersistenceFailureTests.C467_V06_AtomicSettlementFaultMatrix` | Before-commit fault exposes cleared pending fields without complete transaction |
| PC-9 | FailAsync: remove the already-committed terminal-request early return | `AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` | Extra terminal settlement/side effect or failed idempotent recovery |
| PC-10 | Notifier failure handler: set ConfirmedAt to `now` instead of scheduling retry | `AgentTaskLandDeliveryE2ETests.C467_V31_EnqueueFailureRecoversAutomatically` | Owed notification falsely discharged without UserPrompt |
| PC-11 | Notification scan: add `task.LandRequestedAt != null` filter | `AgentTaskLandDeliveryE2ETests.C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt` | Cleared-pending terminal obligation never delivered after boot |
| PC-12 | SourceLandNotificationId index migration: change unique to non-unique (fresh migrated schema) | `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` | Raw duplicate accepted / concurrent insert count exceeds one |
| PC-13 | Keyed enqueue existing-row branch: replace return-existing-ID with a conflict throw | `AgentTaskLandDeliveryE2ETests.C467_V26_HardCrashAfterQueueInsertReusesRow` | Insert/link recovery cannot reuse and confirm existing queue row |
| PC-14 | Existing-key destination validation: replace destination equality guard with `true` | `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` | Wrong-destination key reuse is accepted |
| PC-15 | Land enqueue: pass `sourceTaskId:null` | `AgentTaskLandDeliveryE2ETests.C467_V28_LostFlushWakeupRecoversOnIdleCaller` | Metadata assertion/recovery scan fails; no complete idle receipt |
| PC-16 | CompletionNoteWorkHostedService flush consumer: skip its FlushIfIdleAsync call | `AgentTaskLandDeliveryE2ETests.C467_V28_LostFlushWakeupRecoversOnIdleCaller` | Persisted pending row never reaches caller despite eligible scan |
| PC-17 | Land enqueue: change `MessageSendMode.WhenIdle` to `MessageSendMode.Now` | `AgentTaskLandDeliveryE2ETests.C467_V23_BusyCallerDoesNotBlockAnotherLand` | Keyed body/prompt attempted while caller's gate is closed |
| PC-18 | Receipt predicate: replace `IsCompleteIn` with `IsConfirmedBy` | `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | Head-only/head+tail clipped prompt falsely confirms |
| PC-19 | Receipt sequence cutoff: replace saved baseline with `0` | `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | Old identical prompt confirms |
| PC-20 | Receipt fallback-time cutoff: replace saved attempt time with `DateTime.MinValue` | `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | Old prompt confirms when sequence baseline is absent |
| PC-21 | Receipt transcript query: remove destination-session predicate | `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | Matching text in a different session confirms |
| PC-22 | Receipt transcript query: remove UserPrompt-kind predicate | `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` | QueueEnqueue/QueuedUserPrompt falsely confirms |
| PC-23 | Receipt reconciler: treat Sent or screen Delivered as confirmed without transcript match | `AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome` | Sent-before-typing case is confirmed/warning cleared despite no UserPrompt; V-11 also retains the isolated screen-only negative |
| PC-24 | Receipt reconciliation: skip production CatchUpTranscriptAsync | `AgentTaskLandReceiptTests.C467_V12_CatchUpAndRecoverReceiptWithoutRetyping` | Native-only receipt remains unpersisted/unconfirmed |
| PC-25 | Unresolved-key retention predicate: remove unresolved-Land exclusion | `AgentTaskLandReceiptTests.C467_V13_RetentionCancellationAndSupersession` | Sole key/prompt evidence pruned before receipt recovery |
| PC-26 | Hold-note supersession: remove `DeliveryAttempts == 0` predicate | `AgentTaskLandReceiptTests.C467_V13_RetentionCancellationAndSupersession` | Attempted row canceled despite possible late receipt |
| PC-27 | Notifier linked-row handling: clear QueueMessageId/key association when row is parked | `AgentTaskLandReceiptTests.C467_V13_RetentionCancellationAndSupersession` | Existing authoritative row association lost or replacement attempted to evade cap |
| PC-28 | Delegate-report consumer predicates: remove `SourceLandNotificationId == null` in **each** consumer, one separate cycle per site: read/poll, check completion, failure reminder, output distillation, polled shrinking, legacy report dedup | `AgentTaskLandNotificationRecoveryTests.C467_V14_LandNotesDoNotCountAsReports` (first five sites); `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` (dedup site) | Land row acknowledged/rewritten, missing report satisfied/reminder suppressed, or distinct Land events collapsed. Report PC-28a through PC-28f separately |
| PC-29 | Monitor: assign LastProgressAt on every LastEvaluatedAt update | `AgentTaskLandMonitoringTests.C467_V15_ThresholdsUseMeaningfulProgress` | Re-evaluation hides 5/15-minute warning/error |
| PC-30 | Monitor scan: skip IDs in process active set | `AgentTaskLandMonitoringTests.C467_V15_ThresholdsUseMeaningfulProgress` | Active Running request escapes no-progress detection |
| PC-31 | Threshold crossing guard: force already-emitted check to `false` | `AgentTaskLandMonitoringTests.C467_V15_ThresholdsUseMeaningfulProgress` | Duplicate event/obligation or unique conflict instead of idempotent sweep |
| PC-32 | Land Attention query: restrict to Working/Dispatched tasks | `AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome` | Succeeded task's unresolved outcome warning disappears |
| PC-33 | Land Attention predicate: accept task read/poll timestamp as receipt | `AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome` | -Status clears warning before complete caller prompt |
| PC-34 | Generic queue Attention: remove keyed-Land dedup exclusion | `AgentTaskLandMonitoringTests.C467_V16_AttentionSurvivesRecencyAndDeduplicates` | More than one item for same unresolved keyed Land condition |
| PC-35 | Historical projection: map ambiguous LegacyUnverified to Confirmed | `AgentTaskLandNotificationPersistenceTests.C467_V17_UpgradeAndLegacyEvidence` | Guessed legacy receipt/destination shown as fact |
| PC-36 | Cancellation/invalidation: delete unresolved terminal obligations for the canceled task | `AgentTaskLandRequestTests.C467_V02_RejectStaleWorkAndExposeMirrorDrift` | Owed published outcome erased by later cancellation |
| PC-37 | Hold transition: move PublishAsync before transaction commit | `AgentTaskLandHoldVisibilityTests.C467_V04_HoldAndAgeAreAtomicBeforePublish` | Event observer cannot read advertised committed hold/outbox |
| PC-38 | Boot land sweep: omit enqueue of recovered pending requests | `AgentTaskLandDeliveryE2ETests.C467_V27_LostRequestWakeupRecoversAtBoot` | Original accepted request never reaches publication/receipt |
| PC-39 | StageTestDesign bundle: delete D-8's complete UserPrompt acceptance sentence | `InstructionBundleTests.C467_V21_DeliveryInventoryAndReviewAreMandatory` | Standing session receipt requirement absent |
| PC-40 | StageReview bundle: delete the reject-missing-producer-to-recipient-test sentence | `InstructionBundleTests.C467_V21_DeliveryInventoryAndReviewAreMandatory` | Review obligation absent |
| PC-41 | Request pending-uniqueness migration: change unique index to non-unique | `AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` | Raw duplicate current request accepted |
| PC-42 | Terminal-request uniqueness migration: change unique index to non-unique | `AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` | Raw duplicate terminal event accepted |
| PC-43 | Notification SourceEventId uniqueness migration: change unique index to non-unique | `AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` | Raw duplicate source-event obligation accepted |
| PC-44 | Request acceptance: omit the task-row serialization lock statement | `AgentTaskLandRequestTests.C467_V01_AcceptRequeueSerializeRequest` | Concurrent acceptance produces an unhandled uniqueness/500 failure or different accepted current IDs |

PC-12/41/42/43 each use a fresh pre-change database and the modified CLI-generated migration;
changing only the EF model against an already-migrated schema would leave the guard enabled.
For PC-44, rendezvous immediately before lock acquisition, pause the first accepted reader
before its write, start the second request, then release the first. The fixed second request
must wait for the row lock and reload; a missing lock exposes the stale read. Do not place a
two-reader barrier inside the correctly locked region, which would deadlock the green run.
For PC-28f, seed an ordinary-report row whose task/digest matches the candidate Land note as an
adversarial fixture: different legitimate Land identities normally have distinct digests, which
would otherwise mask removal of the report-only dedup predicate. The keyed Land row must still
be inserted and returned independently. No production digest algorithm is weakened for the test.

All inherited CARD-0448 publication/identity/cleanup guards remain covered by the R-2/R-4/R-5
class floor and their existing controls. If Code changes one of those guard implementations,
also run its named control from the CARD-0448 verification design, method-scoped; do not rewrite
that protocol under this card. New source aliases/identity cases in V-3 supplement the legacy
`IsHeldBehindSharedWriter` helper tests and cannot be replaced by them.

### Commands, evidence and test-design review

Use one task-owned output directory per assembly/run family; do not build against locked live
outputs. The following PowerShell helper is an executable command template for every C# row.
Its class and optional method parameters map exactly to the tables. Run from the repository
root; each invocation produces a uniquely named fresh TRX in the requested results directory.

```powershell
function Invoke-C467Test {
    param(
        [ValidateSet('Antiphon.Tests','Antiphon.E2E','Antiphon.Agents.Pty.Tests')]
        [string]$Project = 'Antiphon.Tests',
        [Parameter(Mandatory)][string]$Class,
        [string]$Method = '*',
        [string]$Label = 'verify'
    )
    $runId = '{0}-{1}-{2}' -f $Label, $Class, [Guid]::NewGuid().ToString('N')
    $resultDir = Join-Path (Get-Location) '.antiphon/acceptance/card-0467'
    New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
    $outputArg = '--property:OutputPath=bin-c467-{0}/' -f $Project.ToLowerInvariant()
    dotnet run --project "tests/$Project" $outputArg -- --treenode-filter "/*/*/$Class/$Method" --report-trx --report-trx-filename "$runId.trx" --results-directory $resultDir
    $runExit = $LASTEXITCODE
    $trxPath = Join-Path $resultDir "$runId.trx"
    if (-not (Test-Path -LiteralPath $trxPath)) { throw "Missing TRX: $trxPath (exit $runExit)" }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $rows = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($rows.Count -eq 0) { throw "Zero executed results: $trxPath" }
    $rows | Select-Object testName, outcome, duration
    [pscustomobject]@{ ExitCode=$runExit; Results=$rows.Count; Trx=$trxPath }
}

# Illustrative verification invocations; execute all table classes, not just these examples.
Invoke-C467Test -Class AgentTaskLandHoldVisibilityTests
Invoke-C467Test -Project Antiphon.E2E -Class AgentTaskLandDeliveryE2ETests

# PC-3: run after applying its single mutation, then restore and run again.
Invoke-C467Test -Project Antiphon.E2E -Class AgentTaskLandDeliveryE2ETests -Method C467_V24_BlockedWriterThenReleaseDeliversBothNotes -Label PC03-red
Invoke-C467Test -Project Antiphon.E2E -Class AgentTaskLandDeliveryE2ETests -Method C467_V24_BlockedWriterThenReleaseDeliversBothNotes -Label PC03-green
```

Do not run those red/green lines consecutively without restoring source between them. Inspect
the reported exit code, test names, case names and assertions: green requires all expected
cases passed with no skipped acceptance cases; red requires the expected assertion failures.
The helper deliberately preserves failing TRX evidence rather than treating any failure as
successful mutation evidence. Confirm the runner accepts the results-directory flag and file
location on the first invocation; an option/discovery failure is not a test result.

Execute the **union**, once after restoration, of every C# class in V-1 through V-19/V-21,
`AgentTaskLandDeliveryE2ETests`, and the existing classes explicitly named in R-1 through R-14.
Use separate class-filtered invocations or the testing owner's supported parenthesized class
OR syntax; never a namespace-wide fallback. Include existing `SessionMessageQueuePtyIntegrationTests`
and `SessionMessageQueueInterruptedAttemptTests` even if only the Land caller changes.
After Antiphon.Tests completes, run:

```powershell
Invoke-C467Test -Project Antiphon.Agents.Pty.Tests -Class FakeGrokContractTests
pwsh -NoProfile -File scripts/test-client.ps1 TaskDrawer.test
pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test
Push-Location client
try { npm run build; if ($LASTEXITCODE -ne 0) { throw 'client build failed' } }
finally { Pop-Location }
Invoke-C467Test -Project Antiphon.E2E -Class AgentTaskLandDeliveryE2ETests
```

Run E2E once after the client build; the earlier E2E invocation only illustrates the helper.
Process-spawning classes, including new script/real-git fixtures, carry their assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. No Antiphon.Tests/Pty.Tests co-scheduling. A global sweep
on a shared schema requires unkeyed NotInParallel; prefer isolated schemas and scoped counts.
This fake-provider E2E is Windows/modern-ConPTY integration acceptance, not a headed real-model
canary; missing Docker/Postgres/git/pwsh/modern PTY/fake binaries is a prerequisite failure to
report and fix, never a passing skip. No live-stack restart, deploy or live-caller probe is part
of these commands. Ensure fixture teardown accounts for every owned process and retains crash
evidence before removing only its own temporary paths.

For each run retain commit SHA, mutation diff (if any), binary MVID/hash, fresh TRX, case counts,
duration and failing assertions. For capstones also retain redacted request/operation/event/
notification/queue/receipt snapshots, native keyed prompts and submitting input counters,
attempt baseline, host/runner ownership, barrier receipt, monitor ages, git command trace and
`git --git-dir <owned-bare-remote> merge-base --is-ancestor <verified-sha> refs/heads/master`
exit code. Do not log caller tokens or native credentials. Store paths beneath
`.antiphon/acceptance/card-0467/<run-id>/` and E2E's normal `TestOutput/Logs/<method>/`; the Code
report gives absolute retained artifact paths and a V/R/PC result table. Update restored source
last-write time/rebuild so green cannot reuse a mutated DLL. Commit before long runs and do not
edit a running worktree. PC sharding uses only additional owned worktrees at the same committed
task tip, with separate outputs/results and no agent sub-delegation; controls touching the same
file/method remain separate, as required by the testing owner.

Test-design review applies in the existing Code/Review process; this creates no new role.
Review must trace each inventory row to its V/R/PC evidence, inspect the capstone for a direct
test-side submit/flush or seeded receipt, and check both actual server-death cuts. Reject missing
busy/already-idle/held-release recipient proof, a guard without its red/green control, skipped
capstones, status-only delivery acceptance, or a mutation whose expected assertion never ran.
Review also verifies that S6 installs D-8 in StageTestDesign and StageReview and updates the
orchestration/testing owners. V-21 protects instruction text only. If the concrete fixture cannot
reach these cuts or ingest native UserPrompt evidence, return next: plan with that seam defect;
do not replace the acceptance oracle with mocks or ask Code to invent a weaker test.

### Out of scope

- Live incident-owner release, live caller messages, production data repair/migration execution,
  deploying/restarting the shared stack, card moves, owner termination and exclusion bypass.
- Model understanding or taking the next stage correctly after receipt; native FakeGrok proves
  session transport/persistence, not reasoning, provider authentication or every provider's TUI.
- Real GitHub/network publication reliability; a real local bare remote proves git protocol and
  containment with deterministic rejection/crash control, not WAN availability.
- Revalidating all generic terminal policy or introducing a new delivery engine. Retained queue
  and FakeGrok contract suites cover the touched transport dependencies; inherited landing safety
  suites cover unchanged protocol guards. Changed inherited guards bring their own PCs into scope.
- Automatic legacy outcome replay or semantic reconstruction of an unknowable destination.
  V-17 requires explicit uncertainty, not manufactured historical delivery proof.

### Cost

- Suites forced: the named Antiphon.Tests class union, FakeGrokContractTests sequentially,
  TaskDrawer/attentionVisuals via the client wrapper, a current client build, and the eleven
  Land delivery E2E methods V-22 through V-32, with parameterized outcome/save-failure/receipt
  cases. Count executed cases from TRX rather than equating V rows with test counts.
- Estimated verification floor after tests exist: 35-65 minutes for build plus restored-green
  named regression/capstone runs, and 100-180 minutes for 49 method-scoped PC cycles (PC-28 has
  six sites). Serial total approximately 135-245 minutes, excluding implementation and failure
  diagnosis. Independent worktree shards can reduce elapsed PC time, not required evidence.
- **Measured evidence available now:** the testing owner records approximately 25.5 minutes for
  the full Antiphon.Tests assembly and 15-35 seconds for a filtered client file. Those are context,
  not measurements of this new selection. TestDesign ran no application suites or capstones;
  their costs are unmeasured estimates. Code must report cold/warm build time, per-class/TRX
  durations, capstone/recovery latency and per-PC red/green cost, then replace estimates with
  those measurements. A long/slow or missing test does not relax acceptance.
