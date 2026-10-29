# CARD-1610: Keep failed tasks' live sessions reachable

Date: 2026-10-29. Stage: Plan; verification remains a separate TestDesign stage.
Inspected task base: `f8bddb6efda7b0e8f50f3b45744087e52188bc66`.
Only this plan is changed. No builds, tests, release calls, settings changes or
session stops were performed. Draft findings were committed and pushed early.

## Problem and evidence

A watchdog failure ends the task's ordinary control/report paths while its
session can remain productive and occupy a seat. Preserve the failure history,
make the remaining session visible and explicitly releasable, avoid charging
build admission waits as execution, and accept a subsequently completed report
only when it still belongs to that failed attempt.

CARD-1610 was read through `scripts/card.ps1 get CARD-1610 -Board Antiphon`.
Its first example is Review `7ee3b07a-7fad-4121-84d3-e8c65cd09afe`, dispatched
02:10:38Z and failed 06:10:40Z at the 240-minute ceiling, with a ToolCall six
minutes old. Reads during this Plan still showed Failed, attempt 1, Worktree,
and session `8aa54343-3835-4e5f-8f3b-4e77391f3f6f` Running/working. Its slot
reported `occupiesCapacity=true`, **`orphan=false`, `openTaskId=null`**. The
agent detail reported Idle and the same persistent session. Attention contained
RecentFailure, with no SlotOrphan for this session at that observation.

The second example, `038c27ab`, failed after unsafe cleanup approval was refused.
Its session `fb1bc7a9-a675-4318-b8f5-d4c89855cb0b` was already ended at
2026-10-29 00:11:30 when inspected. This Plan does not claim a continuing second
leak or infer how that session ended.

The running server reported source
`f84228f285dd3e4ad7b5dc8260cbc9f819256f50`; that is distinct from the task base.
`SeatDesktopJoin.cs` and `RunnerSlotService.cs` are identical between those two
trees. The larger service/dispatcher files differ elsewhere. Live observations
and source explanations are therefore recorded separately, not presented as a
same-build execution test. Private runtime configuration was not inspected:
the automatic-release setting's *effective live value* remains unverified.

## Ground-truth table

| Card assumption or question | What the code or read actually establishes | Design consequence |
|---|---|---|
| No supported verb can stop a Failed task's live session | `AgentTaskService.CancelAsync` rejects Succeeded/Failed/Canceled. The existing single-seat `POST /api/session-runners/{runnerId}/slots/{sessionId}/release` accepts operator authentication and a reason independently of task status | Keep cancel semantics; improve task-oriented access to the existing release mechanism |
| Failed has no live owner task, therefore its slot must be an orphan | `RunnerSlotService.IsOrphan` excludes warm pool state. `SeatDesktopJoin.LoadAsync` treats Idle plus non-null PoolIdleSince as warm without proving session idleness. `FailAndNotifyAsync` calls `RemoveEphemeralAgentAsync`, which calls `MarkIdleForJanitor` when the process survives. The incident's slot is concretely non-orphan | The warm-pool exemption explains the absent SlotOrphan; do not equate agent Idle with session idle |
| A scheduled orphan sweep should have reclaimed it | Both `DiscoverScheduledAsync` and `ReleaseOrphansAsync` return without discovery when AutomaticEnabled is false (shipping default). When enabled, fresh runner evidence must qualify Idle and remain stable; Working/Unknown defer. The job runs independently of whether slots display orphan | Disabled discovery is a possible additional reason, not a proven incident setting. Working alone prevents conditional release even if enabled |
| CARD-0691 release sweep catches every live terminal session | `ReleaseUnownedPoolDelegatesAsync` initially selects PoolIdleSince null; the failure path already set it. The warm janitor defers recently active Working sessions | Preserve custody, surface it explicitly; do not shorten pool TTL to fix visibility |
| All existing janitor paths obey the no-Working-stop rule | `RetireIdleWarmAgentsAsync` can retire a Working-reading session after transcript silence exceeds TTL; `AgentTaskPoolTests.the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl` explicitly expects a kill | Correct this existing policy conflict in S1; silence cannot turn Working into idle or grant stop consent |
| CARD-1604 progress fixes the role ceiling | `TaskDeadlinePolicy` now considers CodexTokenCountAt for ModelWait only. Ceiling still uses now minus DispatchedAt; local ToolCall has its separate clock. ExpectedMinutes schedules checks only | Preserve CARD-1604. Add a separately named execution ceiling; do not overload ExpectedMinutes or reset ceiling on token_count |
| Existing metrics can subtract a task's queue wait | BuildSlotRequest carries optional task/session IDs. BuildSlotWaiter exposes pid/label/time/position, not attempt identity; metrics aggregate waits across callers and retain a bounded history | Add attempt-bound accounting at the real broker. Transcript text and aggregate p95 are not accounting authority |
| A later report automatically settles Failed | Reply `OnTurnEndLockedAsync`, `OnTranscriptActionAsync`, runtime `AdmitActionsAsync`, catch-up eligibility and coordinator `RequestAsync` select Dispatched/Working only | Widen through one narrow recovery eligibility predicate across producer and consumer, not just the final classifier |
| Dead-session reconciliation will recover a later report | It evaluates open tasks and revisits a narrow Failed population only to repair a lost dead-session failure notice | Add explicit late-report recovery; preserve dead-session detection and failure-notice recovery |
| Existing lock order can simply be reused everywhere | Runtime action saves lock session then task. Terminal release `LockIdentityAsync` and released-answer paths lock task/release/session (and sometimes agent) | Converge the intersecting orders before joining release and late settlement |

## Decisions

### D-1: Keep task failure and process release as separate outcomes

Do not allow ordinary cancel to overwrite Failed with Canceled. Add
`delegate.ps1 -ReleaseSession <task-id> -Reason <text> -ConfirmStop`, which reads
the current task/session binding and submits an **identity-bound** request to the
existing single-seat release endpoint. The result names the release/action ID,
task attempt, requested session generation and Confirmed/Pending/Refused outcome.
Failed, its failure event/reason and original completion time survive this action.

Rejected: making cancel on every terminal task unconditional, treating a 409 as
permission to call a raw kill, and inventing a second scheduler or process-stop
service. A terminal task is not evidence that its process exited.

### D-2: Destructive release remains an explicit human-authorized operation

The task-oriented request still requires `X-Antiphon-Operator-Token`, a nonempty
reason and affirmative `confirmStop=true`. Task/session/capability tokens alone,
including the parent orchestrator's token, do not authorize it. The operator token
continues to be read through `scripts/lib/runner-operator-token.ps1`, never echoed
or copied into the task/report. The script must not obtain credentials from an
unrelated home or remote machine.

A human may issue this command. An orchestrator may execute it only when the
human has expressly authorized stopping the named session/attempt; record that
authorization reference in the reason/audit. General dispatch/restart authority,
this Plan dispatch, Failed status and `orphan=true` are insufficient. An
orchestrator without that authority can inspect and request a decision; the
supported operator verb is already available. This plan does not silently grant
the broader orchestrator kill authority the standing rules withhold.

Reject missing authority before runner reads or mutation. The explicit endpoint
is independent of AutomaticEnabled. Automatic discovery retains every Working,
unknown, custody, delivery and publication guard; it never selects the operator
branch. CARD-0079's separately qualified automatic stop is unchanged.

### D-3: Bind the convenience operation to a task attempt and runner generation

Extend `RunnerSlotReleaseRequest` with an optional task-bound expectation object:
task ID, attempt, concurrency token, session ID, runner ID/store ID, accepted
StartedAt, action ID and confirmStop. All expectation fields are mandatory as a
group; partial objects are validation failures. The new CLI always sends them.
Legacy human single-seat requests retain their existing contract; they are not
used as a fallback for a refused bound operation.

Use `RunnerSeatRelease` for the task-bound action, adding an explicit authority
discriminator (legacy/default = AutomaticIdle) and bounded operator reason/actor
metadata. Do not overload ObservationToken to imply consent. Reuse the existing
states, task/attempt/settlement identity, revision, action ID and attention debt.
Before reserving, require the exact current Failed attempt, matching session and
store/generation, no newer owner/reservation, no standing/AlwaysOn/board/specialist
owner, and no SourceLanding or verification custody. Only the explicit operator
path may bypass the *idle/stability* requirement; it may not bypass identity or
custody. A stale expectation returns a named 409, not a refreshed automatic retry.

The runner's existing ReleaseSlot transport gains a capability-advertised bound
request variant, checked under the same launch/session gate that owns
`ReleaseSlotUnderGateAsync`. Validate store and accepted generation before any
signal; persist the action identity and its result for idempotent reconciliation.
If already absent, an exact retained action receipt may confirm it; a bare 404
or empty List cannot prove which generation was stopped. Mismatched receipts
stay Pending/Refused. Unsupported peers refuse without sending the legacy call.

Reuse verified process exit, manifest removal and checkout-retention code. Never
delete a worktree, report, Git ref or unpublished source. Only the expected
generation's runner manifest is removed after confirmed exit. A kill timeout or
live child retains custody and capacity. The API records intent before transport;
lost answers reconcile that same action, never send an unqualified second kill.
Teach the existing reconciliation job to query operator-action receipts even
while automatic discovery is disabled; a recovery read gains no send authority.

### D-4: Detect a failed occupied session independently of the warm-pool label

Add a single `FailedTaskSessionHeld` attention kind, keyed by task/attempt/session/
generation, Warning immediately when an authoritative observation shows that
the Failed attempt's bound session occupies capacity and has no replacement
owner. This condition does not depend on orphan or PoolIdleSince. Include task,
card, session and runner navigation, failure source, Working/idle/unknown,
observed-at, release disposition and the supported inspection/release guidance.
No notification sink, automatic input or stop action is added.

Share a `FailedTaskSessionFacts` projection between attention and task detail.
Consume the existing bounded sampler for remote seats and a bounded local runtime
observation for local sessions. Surface unknown/stale evidence explicitly; a stale
desktop Running row is not proof of current occupancy. Do not add a runner List
for every task on each attention GET. When inventory becomes unavailable retain
the held-session concern with Unknown evidence, not a cleared/released claim.
Suppress the generic SlotOrphan/SeatIdle duplicate for the same confirmed held
session while keeping their general behavior for other seats. A warm successful
Shared delegate and a live Blocked owner retain their existing semantics.

`delegate.ps1 -Status` prints `Failed, session still Running: seat held` only
when those facts confirm it, plus the activity and supported release guidance.
Starting/Stopping use their actual states. Unavailable observations print
`Failed, session state unknown: seat release unconfirmed`. Confirmed release or
new attempt clears the held condition; a task status alone does not. Publish the
existing task/session invalidations after commits. Old DTOs without the new facts
produce explicit unverified guidance rather than a fabricated seat assertion.

Do not change `IsOrphan` to make every retained agent killable. Separate process
custody from healthy warm readiness in the display. S1 also removes TTL-based
permission to retire Working sessions: janitor/release sweep and their final
automatic runner boundary must defer Working or Unknown regardless of age.
Use conditional runner evidence for automatic release, without a force fallback;
an unsupported peer remains held. Keep the pool agent row and worktree while held.

### D-5: A finite execution ceiling, with an explicit dispatch override

Add nullable `RoleCeilingMinutes` and `DeadlineReason` to create/summary/detail,
the task model, clone/retry paths, formatter and `delegate.ps1`. This is separate
from ExpectedMinutes and monetary CostCeiling. For Code/Review only, permit an
explicit positive ceiling up to 720 minutes; require a reason when overriding
the role default. Null preserves the configured role ceiling. Reject zero,
negative, overflow, unsupported roles and values over the cap; never interpret
an explicit zero as disabling supervision. Snapshot the resolved ceiling and
its provenance at dispatch for that attempt. Retry records a new budget snapshot
and never carries elapsed wait credit into a new attempt.

A Full-scope review's dispatcher names the checkpoint/cost basis and can request,
for example, 480 minutes explicitly. Full is not an implicit entitlement to 480
minutes; estimates and queue cost must be reviewed. Do not change global role
defaults or the model/local/boot deadlines to solve one dispatch.

Reject resetting the role ceiling on every ToolCall, AssistantText or token_count:
chatter could keep a broken loop alive indefinitely. Useful transcript/tool
progress still controls the existing phase clocks and is displayed in the
deadline verdict. This selects the brief's explicit-larger-ceiling alternative,
with actual queue wait excluded as required by D-6.

### D-6: Exclude observed build admission waits, not tool execution

Add attempt context to both slot clients (`scripts/lib/build-slot.ps1` and
`tools/Antiphon.Checkpoints/Slots/BuildSlotClient.cs`): task, attempt, session,
accepted generation, dispatch time and a per-acquire ID, alongside existing
pid/start identity. Dispatch injects these bounded nonsecret fields. The server
checks context against its attempt before accepting accounting; caller labels,
transcript `BUILD SLOT` strings and wrapper-declared elapsed values are not proof.

Extend the **actual shared broker** with attempt-scoped wait observations and a
bounded durable interval ledger. Start a wait only on a broker Busy/MemoryFloor
refusal, close on grant/expiry/timeout/cancel, and clip abandoned waiters at their
last observed poll (never manufacture the following silent minute as wait).
Expose broker epoch, stable wait ID, process identity, attempt identity, sequence,
closed intervals and an active interval through a read operation. Keep aggregate
metrics unchanged. Multiple simultaneous waits count their union, not the sum.
Granted build/test time, disabled/unleased admission, arbitrary shell sleeps and
another task's waits contribute zero credit.

The runner proxies that read to the same configured broker used by slot clients;
do not accidentally consult an empty runner-local broker. Advertise support.
The server stores validated, deduplicated intervals per attempt, so server restart
does not reset credit. The broker retains unacknowledged interval records across
its restart; acknowledged records may be compacted to per-attempt interval unions.
Use monotonic durations within each broker epoch. Anchor interval placement with
dispatch binding and bounded clock observations; implausible/future/reversed
times or an unjoinable epoch are Unknown, never extra credit. Preserve gaps and
overflow explicitly. Proposed bounded retention: 24 hours and 4096 intervals per
attempt; exhaustion sets incomplete accounting rather than silently truncating it.

The shared deadline policy reports wall time, verified wait union, charged time,
ceiling source and accounting completeness. Charged ceiling age is
`max(0, wall age - union(wait intervals clipped to this attempt))`. Subtract only
wait intersections with the current ToolCall interval from LocalExecution age;
otherwise a single real queue wait could still trip the 90-minute tool deadline.
Leave model/boot clocks and CARD-1604 unchanged. Pull queue evidence only for
tasks approaching a deadline and cache bounded snapshots, not every five-second
tick for every task. Recheck task/attempt/generation after asynchronous reads.

If an upgraded task has incomplete/unreachable accounting at a suspected breach,
hold the failure and surface a deadline-accounting concern; uncertainty is not
zero wait. A 15-minute uncertainty threshold escalates attention for a human
decision without killing or silently failing the session. It is not an automatic
release deadline. Legacy tasks lacking the new accounting capability retain their
documented wall clock and an explicit unadjusted label; callers must commission
a supported new attempt or consciously choose a larger ceiling. Do not claim
retroactive exclusion for the incident's historical waits.

### D-7: Recover a late report only for an identified failed attempt

Record an append-only failure episode when a non-destructive watchdog fails a
task: task/attempt, session/store/accepted generation, failure event/revision,
failure source and phase, FailedAt, dispatch/prompt/reply floors. Failure codes
currently being null is not sufficient eligibility. For new records, use typed
sources (Deadline, UnsafeCleanupRefusal, DeadSession, other), not substring parsing
of FailureReason. Preserve the original Failed event and its delivery receipt.

Automatic report correction is allowed only for a Deadline episode that still
owns the same attempt/session/generation, has not been retried/replaced/canceled,
has no operator release in Reserved/Unresolved/Confirmed, and has no prior
accepted final report or later human adjudication. A complete correlated marked
report must have the original accepted task prompt, correct task marker and
turn/response attribution beyond the stored attempt floor. Commit/push alone,
unmarked commentary, a token-only close, a new prompt, interrupted/API-error turns
or report-like text in a tool result cannot satisfy it.

Extend one shared eligibility selector through runtime action admission,
catch-up request, reply entry points and a bounded recovery sweep (32 due episodes
per tick, persisted cursor/backoff). Capture accepted task ID/attempt/generation
on the durable action just as for open tasks. Recheck the episode and task
revision under SQL locks on save. Recovery of an action admitted while Working
may use its same attempt, but cannot switch to whichever task is newest now.
Service restart or repeated transcript arrival must find the same episode and
completion obligation, not invent a second correction.

Run the normal report classifier, progress/source checks, remote publication and
sync-debt handling. A valid `done` may correct to Succeeded; `blocked` may correct
to Blocked with its question; `failed` records the actual final failed report.
If source validation leaves the report Blocked, preserve it and its evidence
exactly as ordinary settlement does. Never mark Review Clean/Full or manufacture
verified receipts merely because a report arrived late. Parent notification is
an explicit correction of the earlier failure, deduplicated by episode and
report digest, and retains the report's valid `next=` stage. It does not launch
a replacement, land, activate, or reopen a closed card.

UnsafeCleanupRefusal, manual cancellation, genuine report failures, unknown legacy
failure causes and verification-custody failures are not auto-overturned. Retain
their correlated late report and expose it as awaiting a decision. Human-reviewed
recovery continues through existing recovery/repair commissioning; a safety veto
cannot be erased by the same session asserting success. For historical deadline
failures, an operator can enroll one exact failure episode after checking its
event, current binding and generation; do not sweep/backfill every Failed row by
parsing English failure text. The present incident has FailureCode null and needs
this reviewed enrollment if still relevant at rollout.

### D-8: Preserve source custody during late settlement

Watchdog failure already releases task workspace consumers. Before any late
report source probe, publish or synchronization, reacquire the existing
`WorkspaceUseAdmission` protection for that exact checkout/ref and attempt.
Refuse/hold if retirement is claimed, cleanup is in progress, a different owner
has the workspace, or a newer attempt/ref no longer matches. No resurrection of
deleted worktrees and no forced fetch/reset/merge. A changed or unavailable source
retains the report as evidence with an explicit hold, never a success claim.
Release that new consumer only after settlement or a safely recorded hold.

### D-9: Resolve the intersecting lock orders; no external calls under SQL locks

Use this order for the changed recovery/release domain:

1. Session queue/maintenance gate, when the operation needs to exclude new input
   or reuse; acquire the settlement gate only for report processing, and never
   wait for the queue gate while holding a settle gate.
2. Short SQL transaction: AgentSessions row, AgentTasks row, then the release or
   failure-episode/action rows, then an Agent row only if needed. Multiple rows
   of one kind are locked in stable ID order.
3. Commit durable reservation/intent and release SQL locks before runner HTTP,
   Git/workspace, event publication or parent delivery.
4. At the runner, the session launch gate validates store/generation/action
   immediately before the existing release primitive. Final audit uses a new
   short transaction and checks the original revision/identity again.

Converge `TerminalRunnerSeatReleaseService.LockIdentityAsync`,
`AgentTaskService.LockReleasedAnswerAsync` and the intersecting task/release
claims with `AppDbContext`'s session-before-task fence. Audit callers for outer
task locks; merely reversing the helper is insufficient. A path that arrives
already owning the inverse order must release/re-enter through a fresh scope and
CAS or defer, never nest. Do not touch host capacity/repository locks unless a
specific intersection is demonstrated; CARD-1593's separate ordering remains.

Release and late correction both claim the same task/session revision. The
winner commits its durable action; the loser returns a named conflict/hold.
An outstanding release also fences Requeue, released-answer continuation, pool
claim and new input on that identity until resolved. The failure history is not
that fence. Calls with unknown transport outcome retain it; an observation timeout
does not free it. Automatic release may not treat a newly Blocked question as an
already completed failed report.

### D-10: Defaults and scope boundaries

Keep parking and automatic terminal discovery disabled by default. No fleet host
pin, provider-specific launch change, global timeout increase, new scheduler,
general retry, process sweep, UI kill button or worktree cleanup is authorized.
The proposed 720-minute explicit-ceiling cap and accounting limits are stated
design defaults for review, not measured timeout recommendations or live changes.
S1 can deliver visibility and the no-Working-stop guard independently. Complete
card acceptance additionally requires S2-S4 and their compatibility witnesses.

## Defects and risks (fail-open cases first)

| Risk | Failure mode | Required disposition / witness |
|---|---|---|
| Terminal label grants destructive authority | Automatic sweep or ordinary task token kills a productive session | D-2 operator boundary; V-3,V-5,V-8; no force fallback |
| Silent Working session becomes disposable | Existing warm janitor uses TTL to permit a kill | S1 replaces that expectation; V-3 and existing mid-turn witnesses, including stale transcript and unavailable observation |
| Stop races a replacement or late report | Raw session ID kills a new generation or loses a completed report | D-3/D-9 action binding, durable reservation and runner gate; V-5,V-6,V-8 |
| False exit or manifest removal | Timeout/lost reply/kill failure reported as released; restart loses a live process | Preserve Pending and custody; query original receipt; V-7,V-9 |
| Arbitrary chatter/forged waits evade deadlines | Token_count or a label extends execution forever; concurrent waits double-count | Broker observations, bound attempt and interval union; V-10..V-15 |
| Safety refusal becomes success | Late `done` overwrites unsafe cleanup, cancellation, another attempt or an earlier review result | Typed episode, protected-cause exclusion, revision fencing; V-17,V-18 |
| Report reopens a retired checkout | Failure had released its consumers before the report arrived | Reacquire existing workspace admission; V-19 |
| SQL/gate inversion | Release takes task then session while late settlement takes session then task | D-9 convergence and real DB barrier tests; V-6,V-20 |
| Duplicate parent or pipeline action | A replayed report sends a second completion or starts another stage | Episode/digest completion obligation, retained prior failure; V-16,V-20 |
| Warm-pool exemption hides terminal occupancy | Failed process is represented only by an agent Idle flag | Independent held-session projection, V-1,V-2 |
| Accounting unavailable holds work forever | Broker gap prevents a trustworthy ceiling decision | Visible Unknown, 15-minute attention escalation, explicit operator decision; no automatic kill or claimed wait credit |
| Legacy peer ignores new conditional fields | Task CLI silently falls back to unconditional release | Capability refusal, V-8; preserve existing human legacy route separately |

The warm-janitor stale-Working behavior is an existing structural conflict found
by source inspection, not an executed reproduction. It is included in S1 because
otherwise CARD-1610 cannot promise the stated safety invariant. The caller should
track it explicitly with this card or a linked repair if S1 is commissioned alone;
this read-only Plan dispatch did not file or modify board cards.

## Slices and concrete files

Each slice has a compiling red-test commit pushed before its implementation.
Tests in a red commit use current endpoints/services or neutral contract scaffolding;
missing symbols/build failure are never the witness. No long run starts on dirty
source. Commit and push each implementation slice before its checkpoint group.

| Slice | Production and documentation files | Tests and observable close |
|---|---|---|
| S1: visibility and no automatic Working stop | `server/Application/Services/SeatDesktopJoin.cs`, `SeatOccupancySampler.cs`, `SeatOccupancySnapshot.cs`, `AttentionService.Seats.cs`, `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `PoolDelegateRelease.cs`; new `FailedTaskSessionFacts.cs`; `server/Application/Dtos/AgentTaskDtos.cs`, `server/Domain/Enums/AttentionKind.cs`; `scripts/delegate.ps1`; attention client enum/display mapping only where required; `docs/ops-http.md`, `docs/session-runtime-invariants.md`, `docs/agent-card-lifecycle.md` | New `tests/Antiphon.Tests/Application/FailedTaskSeatRecoveryTests.cs` (S1 methods) and `tests/Antiphon.Tests/Scripts/FailedTaskSeatScriptTests.cs`; change the contradictory existing `AgentTaskPoolTests` stale-Working expectation. Confirm held visibility despite warm flag, unknown semantics, no kill at any silence age |
| S2: explicit bound release | `server/Api/Endpoints/SessionRunnerEndpoints.cs`; `RunnerSlotService.cs`, `TerminalRunnerSeatReleaseService.cs`, `RunnerSeatReleaseQueries.cs`, `AgentTaskService.cs` and reuse/input admission callers; `server/Domain/Entities/RunnerSeatRelease.cs`, `server/Application/Dtos/RunnerSlotDtos.cs`; runner contracts `PhoneHomeContracts.cs`, `TerminalSeatRelease.cs`; `SessionRunnerRuntime.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRuntimeAdapter.cs`, `Program.cs`; server runner interfaces/HTTP/routing/phone-home clients and capability projection; `scripts/delegate.ps1`, `scripts/runner-slots.ps1`; EF-generated migration/model snapshot; `docs/ops-http.md`, `docs/antiphon-api.md` | S2 methods in `FailedTaskSeatRecoveryTests.cs`; new `tests/Antiphon.SessionRunner.Tests/OperatorBoundSeatReleaseTests.cs`; S2 script method. Actual runner factory plus tiny owned process, refusal/mismatched generation/exit-failure and response-loss cuts. Existing legacy explicit release still passes |
| S3: budget and wait accounting | `TaskDeadlinePolicy.cs`, `AgentTaskDispatcher.cs`, `AgentTaskService.cs`, `DelegationReportFormatter.cs`, `server/Application/Settings/DelegationSettings.cs`, task DTO/entity; new `TaskBuildSlotWaitAccounting.cs` and durable attempt/interval entities; broker contracts, `BuildSlotBroker.cs`, `BuildSlotRoutes.cs`, runner phone-home proxy and capability; `scripts/lib/build-slot.ps1`, checkpoint `Slots/BuildSlotClient.cs`, dispatch environment composition; EF-generated migration/model snapshot; `docs/testing-and-build.md`, `docs/ops-http.md` | New `tests/Antiphon.SessionRunner.Tests/BuildSlotAttemptWaitTests.cs`, `tests/Antiphon.Tests/Application/TaskDeadlineBudgetTests.cs`, `TaskBuildSlotWaitTests.cs`; one identity-propagation method in existing script/checkpoint client tests. Virtual time; real broker refusal/grant; verify union and restart accounting, dispatch validation and phase behavior |
| S4: late report | New `FailedTaskReportRecovery.cs` and typed failure episode entity; `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentSessionRuntime.cs`, `SessionRuntimeActionCoordinator.cs`, `AgentTaskService.cs`; `server/Infrastructure/Data/AppDbContext.cs`; existing `WorkspaceUseAdmission` integration and completion obligation path; EF-generated migration/model snapshot; `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md`, `docs/agent-card-lifecycle.md` | New `tests/Antiphon.Tests/Application/FailedTaskLateReportTests.cs`; normal entry and durable action/catch-up/restart paths, protected failures, source custody and races; existing dead-session missing-note and CARD-1604 tests remain required focused regressions |

Migration commands use the build-slot gate and are separately recorded as
necessary generation operations; migrations must be CLI-generated, never edited
into existence. The Plan does not run them. All new test classes have a server,
shared runner or git/workspace app mapping; no broad Unit lane is selected.

## Witness map and checkpoint proposal

This is the Plan's ordinary witness roster, not a claim of executed coverage.
TestDesign remains separate: perform the requested independent design review
with another company's model, then add the full `## Verification design`, exact
guard/PC inventory and refined cost manifest before Code. It must inspect the
real production entry points, not approve these method names on trust. A review
of this document alone is not a Code Review or permission to land product changes.

Each method below is one `[Test]` result unless TestDesign explicitly changes the
roster and count. Internal scenario matrices are named in the test output and do
not inflate Min. Proposed classes/method prefixes were searched at the base:
none already exists, so the C1610 families have no known suffix collision.

| V | Proposed exact class.method | Discriminating oracle and red-first approach |
|---|---|---|
| V-1 | `FailedTaskSeatRecoveryTests.C1610_S1_Failed_warm_seat_is_visible` | Real failure plus retained Idle/PoolIdleSince agent and occupying runner yields one held-session item even when orphan=false. Base has no such item |
| V-2 | `FailedTaskSeatRecoveryTests.C1610_S1_Unknown_and_reowned_seats_are_not_claimed_released` | Unavailable inventory produces Unknown; new owner clears old held binding; successful warm/Blocked cases unchanged. Baseline changed-display assertion is red |
| V-3 | `FailedTaskSeatRecoveryTests.C1610_S1_Janitor_never_stops_working_or_unknown` | Advance fake time beyond TTL with recent/stale/null transcript, then change generation at final gate: zero stop calls and manifest retained. Base stale-Working branch is red |
| V-4 | `FailedTaskSeatScriptTests.C1610_S1_Status_names_held_unknown_and_confirmed` | Execute actual script against fixture HTTP; assert exact held line, Unknown text and absence after receipt. Base lacks line |
| V-5 | `FailedTaskSeatRecoveryTests.C1610_S2_Release_requires_authority_and_exact_binding` | Missing/wrong token, absent consent, mismatched task/attempt/token/store/generation, newer owner and protected custody produce no runner mutation; valid operator request leaves Failed history intact. Base ignores new expectation payload |
| V-6 | `FailedTaskSeatRecoveryTests.C1610_S2_Release_races_have_one_winner` | Real SQL barriers race correction/requeue/pool claim/input with reserve; one winner, loser held/conflict, bounded completion. No new generation stopped. Base lacks reservation fence |
| V-7 | `FailedTaskSeatRecoveryTests.C1610_S2_Lost_response_preserves_pending_and_audits_once` | Save intent then drop response/restart services; same action queried, no force resend, task remains Failed, receipt produces one confirmed audit. Base lacks bound receipt |
| V-8 | `OperatorBoundSeatReleaseTests.C1610_S2_Bound_release_checks_generation_and_capability` | Production runtime and HTTP/phone-home round-trip parity; swapped generation/store, unsupported old peer and automatic-origin request refuse before signal. Authorized Working release succeeds on exact identity. Base bound contract assertion red with compiling wire scaffolding |
| V-9 | `OperatorBoundSeatReleaseTests.C1610_S2_Exit_failure_and_replay_preserve_custody` | Owned process remains alive on failed exit: manifest/seat retained; completed action replay cannot affect replacement. Base cannot bind replay |
| V-10 | `BuildSlotAttemptWaitTests.C1610_S3_Broker_records_only_refused_admission_intervals` | Fake clock across busy, memory-floor, grant, cancellation, stale polling, unlimited/unleased; only broker-acknowledged wait counted. Base lacks per-attempt ledger |
| V-11 | `BuildSlotAttemptWaitTests.C1610_S3_Parallel_waits_restarts_and_gaps_keep_union` | Overlap counted once; broker restart restores interval identity; truncation/clock discontinuity is incomplete, not zero or double credit. Base lacks durable union |
| V-12 | `TaskBuildSlotWaitTests.C1610_S3_Accounting_is_attempt_bound_and_recoverable` | Real server persistence and transport fixture: wrong identity rejected, repeated pages idempotent, server restart retains credit, disconnect/incomplete epoch holds failure. Base deadline fails at wall clock |
| V-13 | `TaskDeadlineBudgetTests.C1610_S3_Explicit_ceiling_is_validated_and_snapshotted` | Send JSON to existing create route, read stored/result budget and retry; invalid values/roles refused, explicit 480 retained. Base ignores field; semantic assertion red |
| V-14 | `TaskDeadlineBudgetTests.C1610_S3_Queue_wait_is_excluded_from_ceiling_and_tool_age` | 250 wall minutes minus 30 actual wait is 220 charged; 240 ceiling not breached; advance to genuine charged breach -> Failed, zero stops. Tool deadline subtracts only intersecting wait. Base breaches early |
| V-15 | `TaskDeadlineBudgetTests.C1610_S3_Progress_and_unknown_accounting_keep_distinct_clocks` | Useful progress does not reset ceiling; null legacy preserves old policy, upgraded unknown accounting is held/visible, Codex heartbeat keeps its model-only effect. Base unknown-accounting assertion red |
| V-16 | `FailedTaskLateReportTests.C1610_S4_Deadline_report_corrects_once_through_normal_settlement` | Full correlated pushed report through actual ingestion/action/settlement corrects Failed, retains failure event and sends one correction with next stage. Include done/blocked/failed internal cases. Base ignores report |
| V-17 | `FailedTaskLateReportTests.C1610_S4_Safety_manual_and_unmarked_reports_do_not_overturn_failure` | Unsafe cleanup, manual/report failure, unknown legacy, token-only/unmarked/tool text and wrong prompt are retained/refused, never auto-success. The late-report retention assertion is new/red; exclusions are green controls |
| V-18 | `FailedTaskLateReportTests.C1610_S4_Changed_attempt_generation_or_release_vetoes_correction` | Old deferred action, retry, new prompt, replacement generation and pending/confirmed release cannot settle new work. Pair with eligible control so an implementation that ignores all reports fails |
| V-19 | `FailedTaskLateReportTests.C1610_S4_Workspace_admission_fences_late_source_work` | Real admission occupied/retired/changed-ref blocks source effects; accepted lease enables the same report. No write/reset/recreation on refusal. Base never acquires recovery consumer |
| V-20 | `FailedTaskLateReportTests.C1610_S4_Restart_replay_and_release_race_preserve_history` | Commit/save/parent-ack barriers with fresh scopes, repeated TurnEnd/catch-up and competing release; one episode/settlement/delivery identity, no deadlock, immutable history. Base has no correction |
| V-21 | `FailedTaskSeatScriptTests.C1610_S2_Release_sends_binding_and_never_falls_back` | Actual script emits required fields/reason/consent via secret-safe helper; on unsupported/409 it does not call cancel or raw release. Base lacks command |
| V-22 | `BuildSlotScriptTests.C1610_S3_Wrapper_sends_attempt_context` and `BuildSlotClientTests.C1610_S3_Checkpoint_sends_attempt_context` | Fixture broker sees exact dispatched context through both real clients. Base sends task/session only; fields assertion red |

Focused pre-existing regression roster (source names, not executed here):

- R-1: `AgentTaskOverdueDeadlineTests.an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives`.
- R-2: `TerminalSeatReleaseTests.Working_remains_protected_after_arbitrary_silence`
  and `Explicit_operator_release_keeps_its_contract`.
- R-3: `TaskDeadlinePolicyTests.a_token_count_does_not_reset_the_role_ceiling`
  and `a_token_count_does_not_reset_the_local_tool_clock`.
- R-4: TestDesign chooses the existing exact dead-session missing-failure-note
  recovery method and deferred-action changed-attempt method after inspecting
  their assertions; add them to the manifest before Code, never run whole classes.

Positive-control design seeds for separate post-land Mutation: remove operator
auth/consent; omit task-attempt/store/generation comparisons individually; turn
Unknown into idle; skip final runner gate; delete manifest before exit; resend
after unknown answer; count overlapping wait twice; retain credit across retry;
accept token_count as ceiling reset; admit any Failed cause; remove prompt floor;
skip source admission; remove the release/correction CAS; drop completion dedup.
TestDesign maps each independent guard to one exact method and compiling mutation.
No controls have run and none is discharged by ordinary red-first tests.

### Checkpoints

Lanes: **server** = portable server Integration without provider processes;
**runner** = portable shared-runner Integration with only owned fixture processes;
**scripts** = portable PowerShell/client-contract lane. Read
`GET /api/runner-defaults` and `GET /api/session-runners` at dispatch. Both succeeded
during Plan (defaults revision 2; eligible Linux and Windows advertised); these
are placement observations, not a host reservation. Omit `-Runner` and `-Platform`.
No OS-specific production branch is planned. A changed native process/identity
helper requires a separately scoped native witness before qualification.

The table is a proposed closed ordinary list for TestDesign to finalize. Red and
green have separate receipts. Rows sharing a build have identical After groups.
Every OR operand has parentheses and its own trailing star; table pipes are
escaped. Inspect executed TRX names, not list-tests, for suffix over-selection.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1610-s1-red/` | server-s1-red | `/*/*/FailedTaskSeatRecoveryTests/C1610_S1_*` | V-1..V-3 | 3 executed; changed behavior assertion reds recorded | 3 | 6 |
| CP-2 | S1-red | CP-1 | scripts-s1-red | `/*/*/FailedTaskSeatScriptTests/C1610_S1_Status_names_held_unknown_and_confirmed` | V-4 | 1 executed; exact missing line assertion red | 1 | 1 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1610-s1/` | server-s1 | `/*/*/FailedTaskSeatRecoveryTests/C1610_S1_*` | V-1..V-3 | 3 passed, 0 failed/skipped | 3 | 6 |
| CP-4 | S1 | CP-3 | scripts-s1 | `/*/*/FailedTaskSeatScriptTests/C1610_S1_Status_names_held_unknown_and_confirmed` | V-4 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-5 | S1 | CP-3 | server-s1-regression | `/*/*/AgentTaskOverdueDeadlineTests/an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives` | R-1 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-6 | S2-runner-red | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-s2-runner-red/` | runner-s2-red | `/*/*/OperatorBoundSeatReleaseTests/C1610_S2_*` | V-8,V-9 | 2 executed; bound behavior assertion reds | 2 | 3 |
| CP-7 | S2-runner | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-s2-runner/` | runner-s2 | `/*/*/OperatorBoundSeatReleaseTests/C1610_S2_*` | V-8,V-9 | 2 passed, 0 failed/skipped | 2 | 3 |
| CP-8 | S2-runner | CP-7 | runner-s2-regression | `/*/*/TerminalSeatReleaseTests/(Working_remains_protected_after_arbitrary_silence*)\|(Explicit_operator_release_keeps_its_contract*)` | R-2 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-9 | S2-server-red | `tests/Antiphon.Tests -> bin-c1610-s2-server-red/` | server-s2-red | `/*/*/FailedTaskSeatRecoveryTests/C1610_S2_*` | V-5..V-7 | 3 executed; new binding/action assertion reds | 3 | 6 |
| CP-10 | S2-server-red | CP-9 | scripts-s2-red | `/*/*/FailedTaskSeatScriptTests/C1610_S2_Release_sends_binding_and_never_falls_back` | V-21 | 1 executed; new command contract assertion red | 1 | 1 |
| CP-11 | S2-server | `tests/Antiphon.Tests -> bin-c1610-s2-server/` | server-s2 | `/*/*/FailedTaskSeatRecoveryTests/C1610_S2_*` | V-5..V-7 | 3 passed, 0 failed/skipped | 3 | 6 |
| CP-12 | S2-server | CP-11 | scripts-s2 | `/*/*/FailedTaskSeatScriptTests/C1610_S2_Release_sends_binding_and_never_falls_back` | V-21 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-13 | S3-runner-red | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-s3-runner-red/` | runner-s3-red | `/*/*/BuildSlotAttemptWaitTests/C1610_S3_*` | V-10,V-11 | 2 executed; new ledger assertion reds | 2 | 3 |
| CP-14 | S3-runner | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-s3-runner/` | runner-s3 | `/*/*/BuildSlotAttemptWaitTests/C1610_S3_*` | V-10,V-11 | 2 passed, 0 failed/skipped | 2 | 3 |
| CP-15 | S3-server-red | `tests/Antiphon.Tests -> bin-c1610-s3-server-red/` | server-s3-red | `/*/*/(TaskDeadlineBudgetTests*)\|(TaskBuildSlotWaitTests*)/C1610_S3_*` | V-12..V-15 | 4 executed; new accounting/budget assertion reds | 4 | 6 |
| CP-16 | S3-server-red | CP-15 | scripts-s3-red | `/*/*/(BuildSlotScriptTests*)\|(BuildSlotClientTests*)/C1610_S3_*` | V-22 | 2 executed; missing context assertions red | 2 | 1 |
| CP-17 | S3-server | `tests/Antiphon.Tests -> bin-c1610-s3-server/` | server-s3 | `/*/*/(TaskDeadlineBudgetTests*)\|(TaskBuildSlotWaitTests*)/C1610_S3_*` | V-12..V-15 | 4 passed, 0 failed/skipped | 4 | 6 |
| CP-18 | S3-server | CP-17 | scripts-s3 | `/*/*/(BuildSlotScriptTests*)\|(BuildSlotClientTests*)/C1610_S3_*` | V-22 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-19 | S3-server | CP-17 | server-s3-regression | `/*/*/TaskDeadlinePolicyTests/(a_token_count_does_not_reset_the_role_ceiling*)\|(a_token_count_does_not_reset_the_local_tool_clock*)` | R-3 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-20 | S4-red | `tests/Antiphon.Tests -> bin-c1610-s4-red/` | server-s4-red | `/*/*/FailedTaskLateReportTests/C1610_S4_*` | V-16..V-20 | 5 executed; eligible correction/retention assertion reds, negative controls preserved | 5 | 7 |
| CP-21 | S4 | `tests/Antiphon.Tests -> bin-c1610-s4/` | server-s4 | `/*/*/FailedTaskLateReportTests/C1610_S4_*` | V-16..V-20 | 5 passed, 0 failed/skipped | 5 | 7 |

R-4 is explicitly pending TestDesign selection, so this table does not yet grant
Code a complete verification profile. TestDesign also adds exact post-change
execution of the renamed contradictory janitor test. Do not silently count
either obligation as covered by the proposed new tests.

Use the checkpoint tool's `run --plan <this-path> --after <group>` bound to the
full committed source SHA, then `wait` with at most 45-second foreground windows
until exit is not 75. One invocation per committed group; no concurrent source
edits and no full suite. Gate the checkpoint launcher bootstrap through
`scripts/build-slot.ps1` if it needs compilation; checkpoint rows acquire their
own slots. TestDesign must set explicit manifest execution/admission limits from
the budget below rather than accepting legacy generous importer defaults. An
exit 4 is not run; never retry unleased. Preserve failed receipts and compare any
unexpected red with the same exact test at the base, not the whole assembly.

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards)
and [the owner rules](../../test-performance-standards.md). **No measured p95 for
these proposed methods exists in this Plan.** Counts and timings below are design
estimates, not measurements or green evidence. No test/build was necessary to
establish the source and live read facts above, so none was run on the busy fleet.

Provisional per-method body targets: pure interval/ceiling logic under 1 second;
DB/HTTP scenario methods 5-20 seconds; script/process/recovery methods 10-30
seconds. Provisional per-method execution limits are 2, 40 and 60 seconds
respectively. These are small-sample exceptions tracked on CARD-1610, to be
replaced by approximately 2x comparable measured p95, not raised to hide a red.
The first Code red and green runs collect samples; TestDesign first searches
retained comparable fixture receipts and binds source/host/category provenance.
One or two observations remain provisional. Do not run repeated broad suites
merely to manufacture a percentile.

| Phase / group | Expected duration (unmeasured) | Provisional finite bound / measurement requirement |
|---|---|---|
| Slot admission | Incident describes 10-25 minutes; not a measured distribution | 15 minutes per acquisition for this narrow campaign; exit 4 is not run, adjust scheduling rather than bypass gate |
| Isolated server build | 3-5 minutes | 10 minutes build limit pending comparable p95; separate bootstrap/restore/compile timing |
| Isolated runner build | 1-2 minutes | 4 minutes pending p95 |
| Shared DB/container/readiness/migrations | 10-40 seconds once per test process, existing shared fixture | Measure separately, use existing bounded readiness; do not replicate per class |
| Schema clone/seed per scenario | 1-3 seconds | 6 seconds provisional; record DB spans |
| Test host setup | 1-5 seconds shared per run/fixture | 10 seconds provisional, record HTTP readiness separately |
| Bodies + teardown, server group | 30-120 seconds | Row execution 4 minutes; barriers and cancellation replace minute/hour sleeps |
| Bodies + teardown, runner group | 10-60 seconds | Row execution 2 minutes; owned process termination/join is part of proof |
| Complete one-project invocation | 3-8 minutes execution plus admission | Explicit total charged-execution cap 14 minutes server / 6 minutes runner; admission remains separate |

The table's ordinary estimate is **71 minutes cumulatively**, before the small
R-4/renamed-test additions and admission waits. It spans separate red/green
invocations, not one 71-minute test command. Under R1 the aggregate campaign also
deserves a timing/Debug assessment before Code, particularly given the incident's
10-25-minute slot waits. The caller must commission that scoped assessment rather
than repeatedly paying a long batch; no Debug delegate is spawned from this Plan.
Any individual selection/batch/run expected or observed over 30 minutes requires
Debug with source SHA, run identity, exact membership, host/load and phase evidence
before repetition. Splitting rows is not permission to hide total costs.

Use the existing assembly-shared PostgreSQL container/template/migrations once
per run; isolated schemas/transactions provide scenario isolation. Share one
web host where configuration is invariant. Recreate only service scopes or broker
state at explicit restart cuts; the restart is the subject, and its cost must be
recorded. No live provider, production runner, AppHost or browser. Process-spawning
methods use that assembly's `ParallelLimiter<ProcessSpawnLimit>` and own/join all
children. Runner and server process suites run sequentially.

Retain per-test duration and scenario membership, source SHA, category, platform,
host/load, build/slot/discovery/execution/teardown phases and individually timed
DB, HTTP, filesystem and CLI calls under ignored `.antiphon/c1610-checkpoints/`.
Record unavailable phases as unknown and unattributed time as remainder; wall
time is separate from nested/parallel totals. Reuse RuntimePhase/structured
logging for live accounting reads, reservation, runner release and late settlement;
never log tokens, prompts or report bodies. Add no payload-bearing telemetry.

The dependency manifest covers shared runner contracts -> broker/runtime and
server runner clients -> server supervision/settlement; slot PowerShell and
checkpoint client -> broker; settlement -> workspace/Git admission and parent
delivery. No slow project extraction is justified here: the new small classes
isolate the components without a dependency rewrite. R2's larger extraction work
remains CARD-1546; measured cost packing/instrumentation remain CARD-1404/CARD-1514.
This scoped ordinary plan does not claim rc/full qualification under CARD-1545.
Review must inspect shared setup, near-2x-p95 budgets, missing attribution,
process ownership and over-30-minute Debug dispositions. Code runs the evidence
diff guard over its full base..HEAD and removes only its owned alternate output
directories after joined runs; this Plan generated no alternate outputs.

## Failure-mode and recovery analysis

| Cut | Persisted fact and next action |
|---|---|
| Refusal before reserve | No release intent, no signal, original Failed/session unchanged |
| Death after reserve, before wire | Reserved action fences reuse; recovery queries receipt. Do not assume it sent; a new send requires explicit renewed operator action against current identity |
| Death/lost response after send | Unresolved action remains visible; query exact runner receipt/owned absence evidence. No automatic unbound resend |
| Process exits, server audit fails | Runner keeps exact completion receipt; reconcile once under matching generation, without another kill |
| Process refuses to exit | Manifest, agent and capacity retained; Refused/Pending with actionable reason, no fake freed seat |
| Report arrives during release | Compare task/session reservation under the common order. If release won, preserve report as late evidence; do not undo stop. If correction won, stale release returns 409 |
| Report accepted, parent acknowledgement lost | Durable completion obligation retries/reconciles its own identity; old failure remains historical, no second logical correction |
| Runner/broker restart or accounting gap | Keep binding and validated wait credit; unknown evidence raises attention and defers irreversible decisions |
| Requeue or source retirement races recovery | Exact attempt/ref/consumer fence refuses; never settle a new attempt or reopen a deleted checkout |

## Rollout and compatibility

1. Land/activate S1 visibility and no-Working-stop protection first. Verify the
   incident-shaped Failed+Idle-agent+Working-session view without stopping real
   work. The new status line/attention is the operational acceptance surface.
2. Deploy additive migrations, then runner/broker capability and receipt support,
   then server readers and CLI. Bound release is refused on unsupported runners;
   accounting completeness is explicit for old broker/client combinations.
   Existing ordinary cancel and human legacy single-seat release are unchanged.
3. Exercise the bound operator command only against a disposable commissioned
   session with explicit consent, and check actual exit, manifest/capacity and
   original Failed history. Run no production kill canary from this Plan.
4. Enable late Deadline recovery for new typed episodes after S2/S3 acceptance.
   Enroll historical failures individually with reviewed binding evidence.
   Never replay an old report into another attempt. Complete reports that only
   owe runner sync keep the existing sync-debt/no-Reply contract.
5. Rollback disables new recovery/accounting discovery and new bound release
   admission, preserving ledgers, reports, pending receipt queries and attention.
   Do not roll back by enabling auto force release or deleting evidence. Old
   server binaries must not own new operator-mode records; drain/disable their
   discovery during a mixed-version rollback.

A published plan or master land is not activation. The caller owns reviewed rc
qualification/activation and compares `/api/version` and runner/broker capability
versions before claiming this incident repaired in production. This document
contains no fleet address or fixed execution host.

## Sessions waiting for input

A recovered `blocked` report can leave its session waiting for an answer.
With `BlockedTaskParking:Enabled=false` (default), **there is no automatic release
deadline**. The session holds its seat until a human-authorized explicit release,
normal process exit, or a separately enabled qualified parking/release path.
An answer alone resumes work; it is not proof of seat release. The D-6 15-minute
accounting escalation is detection only. D-3's explicit operator action can stop
the named session; the current task-oriented convenience is scoped to Failed, so
an already Blocked result uses its existing supported operator seat surface.

## Open questions for the operator and next stage

These defaults permit a complete design without pausing this Plan. They do not
authorize any live destructive operation.

- Is any broader orchestrator stop authority desired? D-2 defaults to existing
  human-only authority. Expanding it needs an explicit policy decision, scoped
  credentials and its own authorization witnesses; Code must not infer it.
- Is 720 minutes an acceptable maximum explicit Code/Review execution ceiling?
  D-5 proposes it without changing 240-minute defaults. TestDesign should challenge
  the cap using the Full review checkpoint budget and current cost measurements.
- Should unsafe-cleanup failures ever be auto-corrected? D-7 says no: preserve
  the late report and ask for a decision. A different choice is substantive.
- Does the current incident still need operator release or late-report enrollment
  at activation? Re-read task, transcript, slot and generation immediately then;
  the observations here are not an approval or a current release target.
- Runtime AutomaticEnabled was not exposed by these reads. If incident-specific
  sweep attribution is required, inspect effective configuration and retained
  discovery/runner qualification outcomes; do not infer enabled/disabled from an
  absent release. The measured orphan=false already has a source-backed explanation.
- The brief requested design review by a different company's model. Perform that
  review before Code, with particular scrutiny of D-3/D-6/D-7/D-9, and finish the
  separate TestDesign stage (R-4, guard/PC inventory, costs and exact rosters).
  Stage-contract next is **test-design**; its handoff carries the independent
  design review requirement. This Plan is not an executable verification approval.
