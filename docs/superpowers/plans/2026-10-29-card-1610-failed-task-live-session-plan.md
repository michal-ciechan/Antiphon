# CARD-1610: Hold a live task at its role ceiling

Date: 2026-10-30. Round 3 Plan, revising the round-2 design in place.
Round-3 source/census base: `8e311b52225ea4d16df3b098241ebae23601fe14`.
Prior round source: `93a21a1fac2ab3965cce5a48503d54fb71132c07`.
Status: final round-3 design; verification design is folded into this final Plan round.
Round 4 (S1 test adequacy) raises V-1 to 22 with mixed-cap and zero-row-terminal.
Implementation handoff will be Code on origin/master after the stated ordering gates.
No product changes are authorized by this Plan dispatch. The initial skeleton
and each defect response were committed and pushed during this round.

## Round-3 resolution checklist

All five review defects are resolved in the sections and executable roster below.

- R3-1 resolved: Working + typed fields; exclusive Reply action, separate question projection, four counting/Check witnesses (D-1).
- R3-2 resolved: bounded Continue, explicit 720-minute final hold and authenticated bounded operator Extend; no-phase-tail witnesses (D-4).
- R3-3 resolved: keep step-5 authority; token presence defines delegation; 5-minute default/8-minute maximum drain slices with 9-minute foreground bound (D-4).
- R3-4 resolved: bounded independent Failed-report scan, distinct durable parent triage obligation and eligible/busy/crash/identity witnesses (D-5/D-9).
- R3-5 resolved: full affected classes, 39 Unit shards plus method-only Unit and new policy cases; counts/costs recalculated. Final importer validation follows.

## Decisions

- **D-1 — Adopt a non-failing ceiling hold for all roles, including Deploy.**
  A Working session at its role ceiling remains alive; its task stays Working
  with an attempt-bound typed ceiling hold and an answerable attention question. Cancel, continue/extend via Reply, and ordinary
  report settlement remain available. A hold must remain in phase supervision:
  model-wait and tool deadlines still fail real stalls. CARD-1151 is a precedent
  for detection-only handling, not an existing Blocked transition (its code
  leaves task status unchanged). Separate phase and ceiling decisions so a larger
  ceiling fraction cannot mask an overdue phase.
- **D-2 — Keep failure history and tell remaining live failed sessions.**
  Persist one attempt-bound note obligation for non-destructive failures; deliver
  through the existing queue at a safe turn boundary. No timer grants stop authority.
- **D-3 — Correct the control-verb premise.** Retry already calls StopDelegateAsync
  unless release is confirmed; Cancel stops an open task. Delete the new operator
  token rule and task-oriented release protocol from round 1.
- **D-4 — Cover Deploy explicitly.** Its default ceiling is 240 minutes while the
  rollout defaults to a 480-minute drain wait. All-role hold semantics apply.
  Adopt bounded declared ceiling extensions and bounded drain polling without
  weakening the 90-minute tool deadline or the drain safety gates.
- **D-5 — Delete automatic late Failed-to-Succeeded correction.** A held task
  settles normally. Historical/remaining Failed reports require explicit triage,
  fenced by card and role, with next=decide; never start a second chain.
- **D-6 — Drop the broker wait ledger.** No broker migrations, interval accounting,
  runner protocol or lock-order convergence are needed to prevent ceiling failure.
  Existing wait metrics remain diagnostic. Target master plus separately reviewed
  rc ports after CARD-1604; coordinate Deploy integration with CARD-1615.
- **D-7 — Related work matters.** Preserve CARD-1604 token-count semantics and
  CARD-1606's exclusion of monitor self-progress; retain CARD-0153 loop detection.
  CARD-1573 includes the sharded Unit requirement, CARD-1151 boot detection, CARD-1615 the
  Deploy/landing deadlock. Incidents: Review 7ee3b07a, the Astra plans, Deploy
  2e4f7375. Source and live evidence will be distinguished below.
- **D-8 — Verification classes need a declared lane, Slow status and limiter.**
  Add classification/limiter guard checkpoint coverage and exact roster floors.
- **D-9 — Delivery proof is a complete UserPrompt.** Hold notes to the parent and
  failure notes to the delegate require eligible, busy and post-commit crash arms.
- **D-10 — A warm Working session past TTL is detected, never killed.** Remove
  the janitor's time-based kill and add durable attention for the taskless case.

Rejected: terminal ceiling failure plus compensating release/ledger/recovery
subsystems; resetting ceilings on chatter; an automatic hard-stop timer;
changing global phase deadlines to hide long test or drain commands.

## Ground-truth table

| Assumption | Source truth | Consequence |
|---|---|---|
| A role ceiling proves a hang | TryFailOverdueAsync fails even with a recent ToolCall | Hold Working sessions, preserve phase failure |
| Boot detection already blocks tasks | AgentTaskDispatcher.BootStall.cs explicitly preserves status | Reuse detection principle, implement hold transitions explicitly |
| Retry cannot stop Failed+live | RequeueCoreAsync calls StopDelegateAsync | Reuse existing authority and controls |
| Blocked keeps occupancy | DispatchConcurrencyPolicy, dispatcher local cap, pipeline in-flight and CARD-0079 select Dispatched/Working | Keep Status=Working; do not widen these selectors |
| Reply only accepts Blocked | AnswerAsync also handles a Working open-question tool; it validates nonblank message before routing | Add an exclusive typed ceiling path before ordinary answer logic |
| Continue always has phase supervision | ClassifyPhase returns no phase clock for compaction/title/queue tails | Re-arm through 720; expose absence of phase deadline; final operator decision |
| Failed late report reaches caller automatically | Open-task report selectors ignore Failed; completion recovery reads stored Result | Independent transcript scan forwards one parent triage note; outcome remains Failed |
| Delegated drain expiry revokes stop policy | Docker-stack step 5 explicitly authorizes exact remaining old-runner seats at four hours | Preserve that authority; short slices grant none |
| A C547 filename is a test class | AgentTaskReplyC547HoldTests.cs declares AgentTaskReplyIntegrationTests | Run the full real partial class |
| Deploy fits its ceiling | Default drain wait 480 exceeds ceiling 240 | All-role hold plus declared budget and bounded command waits |

## Slices

S1 delivers visibility and janitor safety. S2 implements hold/control semantics;
S3 delivers durable notices and successor fencing; S4 covers declared budgets,
Deploy scripts and documentation. The concrete files/tests and shared build
groups are listed in "Revised slices and concrete file ownership" below.

## Performance budget

Apply docs/testing-and-build.md#performance-standards and
`docs/test-performance-standards.md`. No product tests run in this Plan stage;
checkpoint import is the requested tooling validation. Detailed provisional costs
and exact filters are below. Reuse one producer build per unchanged committed group;
share database/container/migrations once per run and isolate tests with schemas.
Record admission, build, startup, migration, body, teardown and external-call time
separately. Missing samples are not measured p95; the required Debug timing assessment and
Code's necessary runs collect bounded samples and set timeouts near 2x measured p95. Any run expected over 30 minutes
requires Debug before repetition. Import defaults are 45/15/90 minutes for
slot/row/total, not the round-1 plan's claimed limits.

## Sessions waiting for input

A ceiling hold keeps the Working session and its seat. No automatic release
exists while parking is disabled (CARD-1083). Explicit Cancel stops an open task;
Retry stops the old attempt; a normal exit or qualified existing release can
release it. Parking must never treat a Working ceiling hold as an idle question.

## Handoff

Verification design is included in this final Plan round. Next is Code based on
origin/master, after the D-6 ordering gates and performance assessment. This
dispatch edits the plan only; the caller commissions implementation separately.

## D-1 detail: typed ceiling hold and continuing supervision

`TaskDeadlinePolicy.EvaluateAsync` currently chooses the largest fraction, then
`TryFailOverdueAsync` fails that verdict. This must become a shared evaluation
with separate ceiling and phase facts and an explicit disposition. After the
existing catch-up and identity recheck, a breached **real phase** wins over a
ceiling hold, even when ceiling fraction is larger. Preserve the existing boot,
API-recovery, commit-recovery and provider-dialog guards. CARD-1604 heartbeats
adjust ModelWait only. Defaults remain ModelWait=20, LocalExecution=90 minutes;
provider input waits retain their own existing classification and policy.

For a positively Working, current-generation session with no breached phase,
ceiling breach commits **Status=Working**, a typed active ceiling episode and
one durable parent-question obligation. No CompletedAt, Blocked event, failure
reason/code, report, stage outcome, workspace release, pool-idle mark or terminal
settlement is written. Session input is unnecessary. The parent note says
next=decide and explicitly says the delegate continues and still consumes capacity.

Persist nullable attempt-scoped hold fields on AgentTask: episode ID, held-at,
accepted session StartedAt, acknowledged-at, resolved initial ceiling/source and
next ceiling at absolute elapsed minutes. DTO ceilingHold is separate from
blocked; do not add BlockedKind.Ceiling or overload FailureReason/Result.
Identity includes task, attempt, session, generation and DispatchedAt. Nulls
preserve old rows. Generate EF migrations with the CLI during Code. No wait ledger.

Keeping Working preserves the existing overdue, progress/loop, reply settlement,
AgentSessionRuntime catch-up/admission, SessionRuntimeActionCoordinator and
AppDbContext save-fence populations. Inspect these sites, but do not widen any
Blocked selector. An already admitted report retains its original attribution;
done/blocked/failed resolves the active hold atomically with normal settlement.
Cancel/Retry retire the episode with the old attempt and keep their stop semantics.
A normal report can settle without an answer. No hold prompt goes to the delegate.

The minimal Reply change is a nullable exclusive ceilingAction/episodeId/
extendMinutes shape in ReplyToAgentTaskRequest and POST /{id}/reply in
server/Api/Endpoints/AgentTaskEndpoints.cs. Branch to a new
AgentTaskReplyService.AnswerCeilingAsync BEFORE AnswerAsync's nonblank-message,
workspace-answer, open-question and Blocked routing. The typed method accepts
only Working + current active episode + matching attempt/generation, under the
existing recipient gate and task save fence. Mixed ordinary-message/typed shapes,
stale episode/attempt and invalid actions refuse without mutation. Ordinary
AnswerAsync and ContinueWithAuthorityAsync remain unchanged, including the
existing Working open-question exception and Blocked-only standing authority.

For the typed decision do not change RepliedAt/RepliedAtSequence, queue input,
clear pending report attribution, reserve a new workspace or create an attempt.
Record the decision, publish task/attention invalidation and clear the active
question only after the new bound is persisted. Continue/Extend re-arm according
to R3-2 below; neither grants an indefinite waiver. Restart uses persisted episode
and obligation keys, so a repeated tick cannot send a second question. A separate
ceilingHold DTO and CeilingHoldCard in TaskDetailBody expose typed Continue,
Extend and existing Cancel; the normal BlockedQuestionCard remains for Blocked.
The attention producer selects Working + active episode, never only Blocked.

| Counting/selection site (source at round-3 base) | Disposition and witness |
|---|---|
| DispatchConcurrencyPolicy.cs:364-368 IsOpen/IsParallel, stage and absolute limits | No selector edit. C1610_Hold_keeps_occupancy(stage-absolute): four Code tasks including one held still count four; fifth create refuses 409 on the expected constraints |
| AgentTaskDispatcher.cs:510-516 local active and MaxConcurrentTasks | No count edit. C1610_Hold_keeps_occupancy(local): held local task consumes its slot and queued fifth never launches |
| AgentTaskPipelineStatusService.cs:182-188 fleet/scoped/local in-flight | No selector edit. C1610_Hold_keeps_occupancy(pipeline): scope/role/host counts and remaining stay unchanged across hold; repeat after restart |
| CheckCompactionContinuationService.cs:619/653 occupant/owning Check selection | No selector edit. C1610_Hold_keeps_occupancy(check): qualified CARD-0079 still sees the held owning Check and performs only its existing conditional stop path; unqualified seats remain untouched |

All four witnesses drive the actual policy/service selectors, not a fixture-side
count. S2 lists these four inspected files and the full existing regression
classes. A typed hold remains excluded from generic question parking, follow-up
reuse and auto-continue by retaining Working and using a separate projection.
It retains seat, workspace reservation and concurrency occupancy for its whole
unanswered lifetime. The rejected alternative (Blocked plus broadened selectors)
would require a fleet-wide census and risks missed counting/stop predicates.

The pre-ceiling warning remains a projection. At breach, positively idle
non-boot tasks use the existing overdue failure and D-2 notice; a stale/unknown
runner/activity observation cannot establish eligibility for a new hold or a
safe stop and gets attention plus a bounded re-observation. Never reinterpret
unknown as idle. Boot remains CARD-1151 detection-only with unchanged status.

Required tests (planned): ceiling Working holds across Review/Code/Deploy/Plan;
boot status unchanged; phase wins even at a larger ceiling fraction; held task
later crosses each real phase limit; normal report before any answer; continue
and extend without a prompt/watermark change; stale answers; report-versus-hold,
report-versus-answer, cancel and retry races; restart dedup; ordinary Blocked
exclusion; unchanged reservations/seat/parking and zero stopper calls.

## D-2 detail: every remaining Failed-with-live path tells its session

Choose **note, not automatic stop** for non-destructive failures. Persist an
immutable bounded system notice and identity in the same transaction as the
failure event; use the existing SessionQueuedMessage queue and completion-work
pump, not a new scheduler. Add a typed source-event/purpose key with a uniqueness
constraint if existing SourceTaskId/content digest cannot distinguish hold,
terminal caller note and delegate notice. A task-wide completion stamp must not
let a hold notice discharge the later final report's caller obligation.

| Remaining path | Session disposition |
|---|---|
| ModelWait, LocalExecution, or existing provider-input deadline failure | Persist Failed and one delegate notice; retain live seat and custody |
| Positively idle role-ceiling failure | Same notice; idle permits ordinary queue delivery, not a forced kill |
| Unsafe-cleanup watchdog refusal | The existing No denies the unsafe command; additionally queue Failed notice; No is not proof that the session knows the task failed |
| Boot-wedge/credential/rules/delivery/dispatch failure with an accepted live generation | Central failure writer records the same notice, whether or not a caller exists |
| Dead-session/launch failure with authoritative exit or no accepted process | No live recipient; persist NotApplicable with the identity/exit evidence, not Sent |
| Delegate's own marked Failed report or other terminal writer leaving a live generation | Same central post-settlement notice obligation, even if the delegate authored the report; no silent exemption |
| Historical Failed row with a still-confirmed same-generation session | Bounded scan creates one legacy notice keyed to original failure/attempt; unknown identity stays visible and unresolved |

The round-3 writer census distinguishes actual saves from status reads:

| Actual save boundary | Obligation seam and witness |
|---|---|
| AgentTaskDispatcher.FailAsync / FailAndNotifyAsync | Ordinary phase/dead-session/dispatch failure: add notice with Failed event before SaveChanges |
| AgentTaskDispatcher.TryFailArm2ConditionalAsync (:7837) | Conditional ExecuteUpdate plus failure event/outbox transaction: insert obligation only when the compare succeeds, before transaction commit |
| AgentTaskReplyService.FailUnreportedTurnAsync (:1864) | Insert before PersistDeliverThenReleaseAsync's failure save; no-report does not excuse a live recipient |
| AgentTaskReplyService API-error terminal save (:2071) | Same persisted notice before release; preserve API-error outcome and existing exit checks |
| AgentTaskReplyService normal marked-failed settlement | Add through the common persisted settlement boundary, including a delegate-authored Failed report |
| CheckCompactionContinuationService.FailOccupantAsync (:664) | Same obligation or proved-exit NotApplicable; preserve CARD-0079 selector and stop authority |

C1610_Failure_writer_records_notice has these six Arguments. Drive each real
save with a fault after commit/before enqueue; assert exactly one durable identity
or proved-exit NotApplicable and recovery, never a false Told receipt. The
three delegate UserPrompt arms remain the end-to-end delivery oracle.
ApiErrorRecoveryService and SpecialistTaskRunner inspect Failed in this source;
they are not direct task-Failed writers. Inspect their call paths but do not edit
those reads or mislabel them as writes. Task-related landing recovery routes to
existing settlement; only its common save gains the obligation, not new release
authority. Recheck new direct writers at Code's actual base. Any writer that cannot
prove exit or no session owes the notice.
No existing automatic kill gains authority from this contract; CARD-0079 stays
its narrowly qualified exception.

Wire text includes the canonical task marker, task ID, attempt, why Failed, and: "Your task is Failed. Stop
starting further work, preserve/push your authorized work, and return a complete
report. That report is evidence for triage; it will not automatically change this
Failed outcome or launch a next stage." Delivery is WhenIdle after a confirmed
TurnEnd, respecting LF/bracketed paste/separate Enter and existing modal guards.
Never send mid-turn, interrupt a tool, stop an arbitrary process, or call Retry
from the sweep. Before enqueue and send, recheck generation, task attempt and
replacement ownership; superseded messages cannot reach a newly reused session.
A busy session owes the note until a safe boundary. The contract distinguishes
Pending from **Told**: only a complete correlated UserPrompt beyond its floor
establishes Told. No safe boundary means unresolved notice debt and attention,
not a claimed delivery or a new timer-based kill. This limitation is deliberate.

A single bounded recovery pass (at most 32 obligations per sweep, persisted
identity and existing pump/backoff) resumes post-commit enqueue failures. It
reuses the same message/source event and dedup key across restart. The hold
parent note uses the same transactional queue pattern, `next=decide`, its own
identity, and the D-9 receipt tests. If the task settles before that note sends,
retain the audit but suppress stale hold guidance under the recipient gate;
normal completion is then the only actionable note.

## D-3 detail: existing authority and visible custody

Corrected ground truth: CancelAsync rejects already terminal tasks but calls
StopDelegateAsync **before** setting an open task Canceled. RetryAsync reaches
RequeueCoreAsync, which stops the old delegate unless its exact release is
confirmed, then increments Attempt and queues a replacement. Retry is therefore
not a harmless status repair; it spends a new attempt and stops old work.
Existing quota, model availability, commit-recovery and workspace admission
refusals still apply before requeue. Neither verb proves OS exit merely by
changing task status.

Delete round-1 `-ReleaseSession`, operator-bound RunnerSeatRelease mode, runner
capability/receipt transport and the proposed global lock-order convergence.
No new operator-token requirement is added to Cancel/Retry. Raw single-seat
force-release remains the existing human-authorized operation; its authority
is not widened. For held tasks use Cancel if stopping is desired. For terminal
Failed tasks show existing Retry with its consequences, session inspection,
and the existing operator release guidance; never auto-retry to reclaim a seat.
The revised scope intentionally does not make Cancel on Failed a new API verb.

Retain only the useful round-1 visibility: shared FailedTaskSessionFacts in task
detail, attention and delegate.ps1 -Status. A fresh bounded inventory confirms
"Failed, session still Running: seat held" and Working/idle/unknown activity;
stale/unavailable data says "session state unknown: release unconfirmed". Include
notice Pending/Told/Superseded, task/card/session links and failure source. Detect
independently of orphan and PoolIdleSince; `SeatDesktopJoin`'s warm-pool exception
explains why a failed live session need not appear orphaned. Do not call runner
List per task on every attention GET; reuse bounded cached seat observations.
Deduplicate generic SeatIdle/SlotOrphan warnings for this same concern, retaining
other concerns. Clear only after authoritative exit/release or new ownership,
not because Failed was already written. Use the existing attention rendering
contract and add optional DTO facts; the CLI text has offline script tests.
Add a separate CeilingHoldCard for detail.ceilingHold in TaskDetailBody; leave
BlockedQuestionCard and its ordinary answer behavior unchanged. Add Continue,
bounded Extend and existing Cancel controls that send the typed action/episode,
and report "Decision recorded; session continues" instead of "Queued for the
delegate". No auto-continue, Refine or Retry affordance appears for this kind.
Update client/src/api/agentTasks.ts and its existing reply mutation typing.

## D-4 detail: Deploy and declared long work

Use `-CeilingMinutes` (nullable positive integer, maximum **720**) and
`-CeilingReason` at dispatch for **every role**, not just Code/Review. Null uses
the configured role ceiling; ExpectedMinutes/-ExpectAbout remains an estimate,
not authority to change supervision. Require a nonblank bounded reason for an
override, snapshot resolved minutes/provenance at dispatch, include them in task
DTOs/brief/status, and reset elapsed/hold acknowledgements on a genuine retry.
Reject zero, negative, overflow and over-cap values before admission; existing
configured zero/off semantics are not silently redefined for old settings.
A retry retains an explicitly requested initial budget but resolves a default afresh;
it clears active episodes, next-bound acknowledgements and operator exceptions.
Ordinary Extend replies use the same **720-minute total elapsed cap**. Continue
sets next ceiling to min(720, max(current elapsed minutes, current next ceiling)
+ initial resolved ceiling minutes); the initial interval is snapshotted and
never grows through prior Continues. Ordinary Extend sets min-cap-validated
max(elapsed, next ceiling)+M; a requested value over 720 refuses, never clips
silently. Clear the current question only when persisting the new finite bound.
At/after elapsed 720, Continue and ordinary Extend refuse with ceiling_cap_reached;
the same final hold stays Working, visible and counted. It never releases or kills.

**Explicit operator Extend is the only way to re-arm beyond 720.** Use the same
typed Reply with operatorExtend=true, positive M at most 240 and a bounded reason.
The API verifies OperatorCredential.Require (existing operator credential, never
AnswerOrigin or a caller-supplied boolean as authority), then passes authenticated
authority to the service; a task token alone refuses. Set the next absolute hold
to elapsed+M, retain the original 720 cap/provenance, and record actor/reason/old
and new bound. The next breach is again a final hold requiring operator Extend;
Continue cannot piggyback on an operator exception. No unlimited waiver. An
operator can instead use existing Cancel. CLI explicit operator mode uses the
existing owner-only token helper; ordinary Reply, Cancel and Retry gain no token
requirement. Client displays the cap reason and operator action guidance without
reading a credential. Fail closed when operator authority is unavailable.

The question and ceilingHold DTO always include last transcript kind, age,
phase kind/limit/deadline (nullable) and next absolute ceiling. For CompactBoundary,
TurnTitle and Queue* tails, ClassifyPhase returns no phase clock: display plainly
**"No phase deadline covers this tail; the ceiling is its only deadline."**
Working is an activity classification, not proof of useful progress. A breached
real phase always wins even during a hold or after a Continue/Extend.

R3-2 witnesses use injected time: Code compacts at minute 200, holds at 240,
Continue without input/watermark change re-arms 480, then holds again with no
phase deadline. Repeat for title and queued-message tails. At 720 Continue and
ordinary over-cap Extend refuse and the question stays open. An unauthenticated
operatorExtend refuses; authenticated Extend(60) at 725 re-arms 785, whose breach
holds again. Replay/stale episode cannot extend twice. Loop detection remains
active but is never described as a replacement for an absent phase clock.

The brief names Deploy **2e4f7375** as a live ceiling incident involving sanctioned
rollout waiting; the fresh status/report observation is distinguished in D-7.
`DelegationSettings.RolePolicy[Deploy]`
inherits TimeoutMinutes=240; `deploy-server2.ps1` defaults WaitIdleMinutes=480.
D-1 removes its false terminal failure. A declared eight-hour rollout can request
CeilingMinutes=600 with a reason allocating 480 minutes drain plus 120 minutes
for other phases. A total 240-minute drain can request 360 on the same basis.
These are explicit estimates, not global role-default increases or guarantees.

A ceiling override does not exempt a 240/480-minute ToolCall from the 90-minute
local-tool deadline. The exact delegated-context predicate is
!IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN), already used at deploy-server2.ps1:35.
An operator shell launched inside a delegate inherits it and remains delegated;
no bypass based on an operator token, caller name or shell parent.

For delegated drain commands default WaitIdleMinutes to **5**, reject explicit
values above **8** and Phase=all before any token-file read, HTTP, SSH, evidence
creation or mutation. Preserve standalone operator default 480 and explicit
long-wait range. Use PSBoundParameters to distinguish default from an explicit
argument. Each foreground delegated drain call has a total 9-minute wall budget
including HTTP/SSH and bounded output drain; cancellation must leave drain state
and all live containers intact. Pass the remaining cancellation budget to waits
and external calls. This leaves room within the 10-minute foreground tool limit.
No background job, poll orphan or return-with-child-running is allowed.

Resume by invoking the SAME named phase, SHA and target after inspecting its
status/census. drain-old already validates/reuses draining+redirect state
(:1231-1252); drain-temp already tolerates retiredAt and rechecks old accepting
(:1294-1313). Continue only for the phase's named still-busy/exit-wait result;
transport, census, identity and version failures stop for triage. Never clear a
drain or restart a container to resume a slice. Verify these idempotence claims
with the actual offline script harness. If a foreground budget expires during
an external operation, retain its exact observed state and require a fresh
census before resuming; timeout alone is not success.

The delegate records the first drain request time and an **absolute 240-minute
drain deadline** per the existing documented steps 5/8, in its durable rollout
report; a resume takes remaining time from that timestamp (at most 5 minutes
per next default slice), never a new 240-minute allowance. A longer operator
commission retains its explicitly specified absolute deadline. No broker ledger.

Update the docs' delegated examples in steps 4/8 to named five-minute slices;
keep the standalone operator 240-minute examples and original absolute deadline.
**Keep docs/docker-stack.md step 5's existing stop authority verbatim.** At the
four-hour OldRunnerStillBusy cap the operator has explicitly authorized stopping
remaining server2 sessions: inspect each exact seat, prefer resumable owner stop,
use the existing runner-slots.ps1 release with its recorded reason as documented,
record IDs/effects, and recheck all three zero counters before redeploy-old.
This is existing CARD-0934 authority, not authority from a ceiling hold or a
5-minute slice expiry. Do not extend it to temp or another runner: step 8 keeps
its own stop-and-report/retirement/exit rules and four-hour wait. Intermediate
slice expiry authorizes no release, redeploy, drain-clear, volume recycling or
phase advance. If the existing cap procedure refuses, report Blocked with state.

Files: scripts/deploy-server2.ps1, scripts/delegate.ps1, API/DTO budget bindings,
EF mapping/migration, docs/docker-stack.md and docs/ops-http.md. Offline tests:
delegated default 5; explicit 8 allowed; >8 and all refused before side effects;
inherited token still delegated; token-absent standalone 240/default 480 retained;
consecutive drain-old/drain-temp calls retain identity/drain and original deadline;
cap instructions preserve the exact step-5 authority and step-8 limitations.
No live Docker rollout is a checkpoint. Other named phases remain governed by
their existing bounded foreground/recycle procedures and CARD-1615 integration.
CARD-1615 owns the source-writer/landing circular wait; no amount of extra time
fixes it. Do not bypass that hold in this card.

## D-5 detail: no late correction can start a second chain

Delete round-1 Failed-to-Succeeded correction and settlement admission for Failed.
**Forwarding is a separate triage obligation, not settlement.** A Deploy failed
by ModelWait at 20 minutes can still emit its rollout report later; it must reach
the original parent even though Reply on Failed remains refused. Preserve Failed,
FailureCode/Reason, CompletedAt, Result, stage outcomes and existing original
failure notification. Never run commit/land, settle again, reopen, or dispatch next.

At each failure with an accepted generation persist the attempt, dispatch time,
session/generation, original parent, original report floor and failure transcript
sequence in the typed notice obligation. Extend the existing bounded completion
recovery pump with LateFailedReportForwarder.cs; scan at most 32 identities per
pass with a persisted keyset/frontier and normal backoff. Read stored transcript
windows (including an ended generation's final window) and reuse report-marker
parsing/correlation, never JudgeOpenTaskAsync or SettleAsync. Ingestion continues
for Failed sessions; this independent scan does not need to widen runtime-action
settlement selectors or AppDbContext's open-task fence. On restart the persisted
frontier retries uncommitted parsing/queue work. Discovery must not filter only
open tasks or stop watching merely because the delegate notice was Told.

Accept one complete [antiphon-report:<task> done|blocked|failed] report whose
completion marker is beyond the failure frontier and whose source turn belongs
to the accepted attempt/generation (original task brief or that failure notice).
The body can start before failure; a pre-failure complete report alone does not
qualify. Require current task still Failed on that attempt, unchanged generation
and parent at obligation creation; retry/session reuse refuses attribution. Do
not infer identity for legacy rows lacking those coordinates: retain attention
and explicit manual triage. No parent is a durable NotApplicable reason, never
Told. Do not discard final transcript just because the runner exited meanwhile.

In one save persist immutable report excerpt/link, full report digest and source
range plus a unique purpose key (task, attempt, failure event, LateFailedReport).
Then enqueue one **parent triage note** through the same recovery/queue path.
It states "Task remains Failed; late report for triage only", carries next=decide,
and includes report text (bounded, or an exact source link plus digest), board,
card, role, attempt/generation, branch and superseder facts. Enumerate newer
same-board/card/role tasks since this attempt's DispatchedAt, retry/replacement
lineage and matching branch; unknown/no-card lineage is unresolved and decide.
Put the delegate's claimed next stage only inside quoted evidence; do not pass
its header to PipelineHandoffAction, stage completion or landing logic. The note
purpose is non-actionable even if the original report says next=code/land.

A distinct purpose key and receipt keep this separate from the original Failed
caller note and the delegate Failed notice: neither existing completion stamp
can discharge it. Eligible/busy/crash delivery follows D-9 and requires one full
correlated UserPrompt after the parent's recorded floor, zero in the decoy.
Record Pending/Told/Superseded independently; failed enqueue after obligation
commit is recovered, repeated transcript replay does not duplicate it. If the
parent session is gone/reused, keep unresolved attention with the report link,
never redirect to a foreign generation. The source remains Failed in every arm.

Historical 7ee3b07a/30f56bd5 and the 8edda999 Deploy incident are not retroactively
corrected. A coordinate-complete historical row may receive this triage forward;
otherwise the caller explicitly inspects its retained transcript/artifact. No
normal Reply to Failed and no new automatic result chain is introduced.

Also guard the narrower real concurrency risk introduced by holds: a previously
held task can finish after its caller separately commissioned a replacement.
Normal settlement may retain its valid report/outcome, but before constructing
the parent completion/handoff check for a newer task on the same board/card/role
since the held attempt's **DispatchedAt**, not HeldAt (any status, including
already completed, including a successor admitted between the 80% warning and hold). Under the ordinary
settlement/admission serialization, record the decision and force next=decide
with superseding IDs when one exists. A different role alone is not a conflict;
retry/attempt/session fences still apply independently. Recheck when reserving
an automatic handoff so a replacement admitted after settlement cannot race
through; bind the existing PipelineHandoffAction identity to the checked episode.
The caller must inspect these facts before manual dispatch too. No automatic
second review/land is inferred from a recovered result. Hold notes always use
next=decide. Tests cover newer same-role, different-role and no-card lineage,
including replacement admission between settlement and handoff reservation.

## D-6 detail: scope, target lines and activation order

Deleted outright: round-1 S2 bound release protocol and operator-token policy;
S3 broker interval ledger, epochs, proxy, durable wait-credit migration and both
slot-client changes; S4 automatic late correction and lock-order convergence.
Keep: no-Working-stop janitor guard, held-session visibility, explicit per-task
ceiling, normal settlement for the new hold population, and small queue-backed
notice obligations. BuildSlotRequest task/session IDs and aggregate metrics may
be displayed as **diagnostic/unattributed wait**, never subtracted as proven
attempt time. Queue waits can trigger a harmless ceiling decision; they no
longer terminally fail a productive session. The brief's preference for hold
supersedes the original card's demand for exact queue-time subtraction. Do not
claim that a >90-minute single queued tool is exempt from its phase deadline.

Implementation target: **master, then separately reviewed rc ports**. No plan
commit activates a product host. Sequence the deadline work after CARD-1604's
master landing (or verify its exact equivalent already exists at Code's base),
preserving token-count behavior. At the prior round-2 Plan read, GET /api/version reported
`8edda999623a473be2d1970727c172bfb5088d59`, consistent with the brief's rc
activation; CARD-1604 is still Review on the board. Source here already contains
CodexTokenCountAt in TaskDeadlinePolicy. These three facts are distinct.

Land S1 (visibility/janitor) first after ordinary Review, then request its own
reviewed rc port for prompt safety relief. S2/S3 (hold, controls and delivery)
ship together to master after their shared checkpoint group; port together only
after this folded verification design, ordinary Code/Review and migration
qualification. S4 (Deploy CLI/docs) follows the CARD-1615 admission resolution and CARD-1609 retirement gate
and integration review; do not concurrently edit its rollout/admission area or
use this plan as authority to bypass source-writer holds. Until the coordinated
rc port is active, use existing bounded named-phase waits and explicit operator
handling of ceiling risk. Speedups CARD-1573/1313/1404/1514/1531 remain separate;
this plan does not broaden their code surface or depend on unlanded performance
claims. The caller owns canonical rc activation and source-SHA confirmation.
This task does no tags, publication, deployment, rc rebase or desktop-file reads.

Placement was read again from GET /api/runner-defaults and GET /api/session-runners
on 2026-10-30 at 03:57Z (runner-default revision 2). The catalogue had an eligible Linux and Windows lane,
plus an unavailable drained Linux entry. Use live defaults at dispatch; omit
-Runner unless intentionally pinning one host, omit -Platform for portable work
(-Platform Any unpins). Every checkpoint below names its logical lane; no fleet
location is baked into a command. The Plan ran in its assigned Linux worktree.

## D-7 detail: evidence and related owners

The full independent Opus review is `.antiphon/task-25f7ce83.md` at the assigned
base. It reviewed Astra's round-1 plan task c90aa6c9, tip
`7960ee532981c13d9d5f2d039acadeacfdcf84b8`, and rejected ten defects. This revised
plan supersedes that design; its 27 imported rows were valid syntax, not executed
product-test evidence. The original historical source observations remain in Git.

| Prior round-2 evidence / related card | Verified fact and implication |
|---|---|
| Review 7ee3b07a, CARD-1610 description read in round 2 | 240-minute failure with ToolCall only six minutes old; motivates the Working ceiling hold |
| Astra Plan 30f56bd5, CARD-1604 description read this round | Failed at 20-minute model wait despite continuing token_count; later pushed plan 63036f8e with result/nextStage still absent. Preserve 1604's heartbeat fix; historical report remains explicit triage |
| Astra round-1 Plan c90aa6c9 | Compensation design rejected by review 25f7ce83; this round removes its release/ledger/late-correction machinery |
| Deploy 2e4f7375, round-2 delegate -Status read | Still Failed, session now ended at 2026-10-30 02:03:19; retained report describes completed redeploy-old and a requested later drain-temp -WaitIdleMinutes 240. The brief reports its earlier live failure; do not claim it remains live now or that this report proves the earlier failure occurred inside that drain |
| CARD-1604 (Review) | Source heartbeat changes exist here; live API reports rc 8edda999; master/rc sequencing is D-6 |
| CARD-1606 (Backlog) | Supervisor CPU counts its own monitor and masks hangs. Neither monitor ticks, queue polling nor this new notice are useful agent progress |
| CARD-1615 (InProgress; Plan task 7734ac39 in caller brief) | A Shared Deploy task holds a bound land, which blocks redeploy-old. Resolve admission separately; do not relax recycle gates |
| CARD-1573 (Review) | Actual title is output-inventory >64 MiB, with later card text explicitly adding a sharded Unit strategy after long Full runs. It is not solely a Unit speedup card; no unleased workaround receives checkpoint credit |
| CARD-1151 (Review) | Source BootStallDetectAsync records detection without changing task outcome or sending input. Preserve this exact safety precedent |
| CARD-0153 (Done) | TaskProgressPolicy fingerprint/file novelty detects chatty loops; Working holds remain in its existing active-task selector, keep detection-only behavior |
| CARD-1083 / CARD-0079 | Parking off supplies no release deadline; only the narrowly qualified Check compaction continuation has the existing automatic Working-stop exception |

No current runner setting was inferred from a historical report. No desktop
qualification files were read, no completed qualification was resumed, and no
session was stopped. Runtime reads establish observations only, not a same-build
execution test of the proposed code.

## D-8 detail: test lanes and class inventory

All six new classes live in `tests/Antiphon.Tests`. Application tests use namespace
Antiphon.Tests.Application; the script class uses Antiphon.Tests.Scripts. One
file per named class. These replace all seven round-1 proposed classes.

| New class | Category | Slow / allowlist | Spawn limiter | Planned executed cases |
|---|---|---|---|---:|
| TaskSupervisionVisibilityTests | Integration | No; no allowlist entry | None: fake runtime/stopper, no child processes | 8 |
| TaskCeilingPolicyTests | Unit | No; no allowlist entry | None: pure policy/control validation | 12 |
| TaskCeilingHoldTests | Integration | No; no allowlist entry | None: isolated schema and injected runner | 25 |
| TaskCeilingControlTests | Integration | No; no allowlist entry | None: service/transaction barriers, no children | 19 |
| TaskSupervisionReceiptTests | Integration | Slow; add fully qualified name and CARD-1610 reason to slow-tests-allowlist.txt | ParallelLimiter<ProcessSpawnLimit>; add to strict roster | 21 |
| TaskCeilingScriptTests | Integration | Slow; add fully qualified name and CARD-1610 reason to slow-tests-allowlist.txt | ParallelLimiter<ProcessSpawnLimit>; add to strict roster | 11 |

Total new cases **96**; these are expanded executions, not assertion counts. Category guards are explicit checkpoint
rows at each green source group: TestLaneCategoryGuardTests (1), shared
TestClassificationGuardTests (1), ProcessSpawnLimitTests (21 expanded cases:
seven singleton methods, six CPU arguments, eight override arguments) = **23**.
No runner source or new runner test class remains; its unchanged two-method safety
selection has a separate project row, not another new-class census obligation.
Changing the classification during Code requires revising this table,
allowlist and count floor together. Any fixture that begins spawning must gain
the assembly-local limiter and strict-roster membership before running.

Replace round-1's client visibility tests with three focused cases in
client/src/features/delegations/CeilingHold.test.tsx: Continue sends a
typed decision without ordinary reply; Extend validates/sends minutes and stale
episode refusal; Cancel uses the existing route. Each also asserts no generic
Retry/auto-continue affordance. The client red/green rows require a fresh Vitest JSON receipt with
numTotalTests=3 and three exact case names, zero pending/todo, and for green
numPassedTests=3/numFailedTests=0 plus exit 0. A success string is insufficient.
These are client/Vitest, not C# Unit/Integration or ProcessSpawnLimit classes.

## D-9 detail: producer-to-recipient delivery witnesses

`TaskSupervisionReceiptTests.C1610_Hold_parent_has_complete_UserPrompt(mode)`
has exactly three Arguments: eligible, busy, crash. Drive the **real** hold
producer through dispatcher/persistence, actual queue admission/flush, runtime
normalizer and transcript ingestion into a BridgeQueueHarness parent plus decoy.
Assert one complete correlated UserPrompt contains the full hold question,
identity and next=decide after the captured parent sequence floor. Assert zero
matching prompts in the decoy. Enqueued/Sent flags, ack-only counters, fragments,
terminal redraws and a formatter self-comparison cannot pass this test.

Eligible delivers once while idle. Busy remains Pending with zero prompts until
that parent's TurnEnd, then delivers once. Crash faults the first queue insert
**after the durable hold obligation commits**, reconstructs the service scope,
and drives recovery twice: one obligation, one logical message, one complete
prompt. A separate negative in the stale-notice method settles the task before
flush and proves the parent receives the ordinary final report without obsolete
hold action text. The hold message has a distinct purpose identity, so it never
discharges or suppresses that final caller receipt.

`C1610_Failed_delegate_has_complete_UserPrompt(mode)` repeats the three arms for
the failed task's own session. Busy has no injected input before TurnEnd; crash
retains Failed and its notice obligation across restart; accepted delivery says
that later output is triage only. Capture actual full UserPrompt text in the
recipient transcript, not the queue row. `C1610_Stale_notice_is_not_delivered`
has three arguments: retry, generation-replacement, settled-hold. It proves no
old instruction reaches the replacement/finished turn. All twenty-one cases assert
zero automatic stopper/release calls and preserve foreign sessions. Own and
join harness children; do not use any production runner or provider profile.

Add C1610_Failed_late_report_parent_has_complete_UserPrompt with eligible/busy/
crash Arguments. Drive a real Deploy model-wait failure, store its later complete
report, run the actual forwarder/pump and BridgeQueueHarness normalizer/ingestion.
Assert full parent UserPrompt, source link/body, Failed unchanged, next=decide,
superseders, no new stage outcome/child/landing and no decoy receipt. Crash after
the triage obligation save but before queue insert, restart and recover twice.
Add C1610_Late_report_identity_is_fenced with retry, generation-reuse and
pre-failure-report Arguments; no triage forward may be attributed to those.
The first three arms also replay the transcript and prove one forward, independent
of the already delivered original Failed caller note. The delivery/identity methods have 15 cases; six writer-save cases bring the class to 21.
There is no correction producer; these witnesses prove forwarding only. Retain the existing C1161 Failed
**parent** receipt method (three cases) and C1259 accepted-attempt replacement
fence (two cases) within their full affected-class regression checkpoints.

## D-10 detail: pooled Working detection without reclamation

`RetireIdleWarmAgentsAsync` currently protects Working sessions only when their
last transcript is newer than the TTL cutoff, then logs "reads mid-turn but has
been silent" and calls KillPooledSessionAsync. Both TTL and cap eviction must
exclude every Working session regardless of transcript age. IsWorkingBatchAsync
returns bool, not an Unknown enum: uncertainty means absent transcript evidence,
an unfinished session (Created/Stopping) outside liveSessionIds, an unavailable
fresh runner sample or mismatched generation. These cannot prove idle and are
also retained. A transcript with no rows is absent evidence and is retained on
the TTL pass, the cap pass, and the pre-kill recheck. Over the last 14 days,
1,862 completed delegate sessions had no Succeeded task with a zero-row session;
all 34 zero-row sessions belonged to Failed or Canceled tasks, so no agent kind
routinely produces a zero-row session after a completed turn. The pre-kill
recheck missing-boundary clause is unwitnessed defence in depth: the first pass
already drops those sessions from the retire set, so on a stable transcript it
never decides a kill, and zero-row-recheck witnesses the helper
SessionsWithoutTurnBoundaryAsync rather than that call site. The boundary read is two
server-side queries that return only session ids: sessions that have any
transcript row, and sessions that have a boundary row (TurnEnd,
SessionRestartBoundary, a CompactBoundary whose text contains "(manual)", or a
UserPrompt whose text starts with "[Request interrupted"). Row text is not
selected. A pooled session that never writes a row keeps its seat until a turn
boundary or an operator acts. The Failed hold is only the latest task on that
unfinished session and the latest task on its agent, and only when StartedAt is
not default. There is no DispatchedAt >= StartedAt comparison: a Failed attempt
from before a session restart still holds (A-2). A later task on the session or
the agent releases the hold, including a Canceled task that was never dispatched
and has no session (A-1). An authoritative terminal session can take the existing stale arm.
Recheck under the existing release gate immediately before any idle retirement;
remote automatic release continues to require the runner's conditional idle
proof. No force fallback and no new timer exception. Preserve pool agent,
worktree and occupancy until authoritative exit or authorized release.

Add `PooledSessionWorkingPastTtl` attention backed by a durable AgentIncident
for a pooled, same-generation Working session with **no open task owner** whose
current turn age exceeds the configured pool TTL. If the turn start is unknown,
start a first-observed-Working episode and age that observation; never backdate
from a stale pool label. Record first/last observation, last useful activity,
identity, age and custody guidance. Repeated ticks/restarts do not create another
incident for the same episode. No fresh runner sample means uncertain observation, not a cleared incident.
Use the existing bounded pool sweep and seat sampler; no per-request runner fanout.
A task-bound Failed session uses FailedTaskSessionHeld, suppressing the duplicate
pool warning. A positively idle warm session retains existing TTL/cap retirement. If every
member is Working or uncertain, PoolMaxIdlePerDirectory cannot be reached: keep
the excess, report it, and do not kill to satisfy the cap. It remains a soft
retirement cap, not a new admission limit; existing host/stage capacity still
bounds new dispatch. This card adds no speculative prewarming or over-cap fill.
Resolution requires proved idle/end or a new open owner; retain historical audit.

Replace, do not delete, the contradictory existing test
`AgentTaskPoolTests.the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl`
with `C1610_Janitor_retains_working_after_full_ttl`, which fails on the old kill.
The new visibility tests cover TTL and cap pressure, taskless incident creation,
restart dedup, uncertain-observation retention, failed-session dedup, idle recovery and normal
idle retirement. Assert no stopper call, no row/worktree deletion and retained
capacity for Working or unproved-idle sessions. A stall is reported for a decision, never killed.

## Revised slices and concrete file ownership

All services below are under server/Application/Services unless a full path is
shown. DTOs live in server/Application/Dtos; entities/enums in server/Domain;
EF mapping is server/Infrastructure/Data/AppDbContext.cs and migrations are CLI
generated. Tests below are under tests/Antiphon.Tests/Application except Scripts
and the named client file. Work is based on origin/master after the CARD-1604 deadline prerequisite;
coordinate the CARD-1615 source-writer/rollout fix before S4. Recheck these cards'
landed SHAs at commissioning; a plan observation is not current landing evidence.

| Slice | Files and concrete behavior | Tests |
|---|---|---|
| S1 visibility/janitor | AgentTaskDispatcher.cs retirement; AttentionService.Seats.cs/Leaks.cs; new FailedTaskSessionFacts.cs; FailedTaskSessionOwnership.cs; AgentTaskDtos.cs; AttentionKind/AgentIncidentKind; client/src/api/agentTasks.ts and attention.ts mirrors; docs/session-runtime-invariants.md | TaskSupervisionVisibilityTests (22), changed AgentTaskPoolTests janitor cases, GrokDelegateDispatchTests cap case, full Pool/Overdue/Attention/SeatOccupancy classes, guards, two runner safety methods |
| S2 hold/control | TaskDeadlinePolicy.cs; new AgentTaskDispatcher.CeilingHold.cs; AgentTaskDispatcher.cs overdue; AgentTaskReplyService.cs typed AnswerCeilingAsync; AgentTaskService.cs retry; new CeilingHoldFacts.cs; AttentionService hold projection; AgentTask.cs; DTOs; server/Api/Endpoints/AgentTaskEndpoints.cs; EF mapping/migration | TaskCeilingPolicyTests (12), TaskCeilingHoldTests (25), TaskCeilingControlTests (19); full policy/overdue/boot/reply/runtime classes and occupancy witnesses |
| S2 inspected unchanged | DispatchConcurrencyPolicy.cs; AgentTaskPipelineStatusService.cs; CheckCompactionContinuationService.cs; TaskProgressPolicy.cs; AgentSessionRuntime.cs; SessionRuntimeActionCoordinator.cs; AppDbContext runtime fence; tools/Antiphon.Checkpoints/Execution/TaskOwnerGuard.cs | No broadening of Blocked; Working naturally remains selected. Full relevant regression classes below; CARD-0079 witness is narrowly qualified |
| S3 notices/triage/fencing | Actual failure saves in D-2 (AgentTaskDispatcher.cs, AgentTaskReplyService.cs and CheckCompactionContinuationService.cs; recovery/landing callers inspected); new TaskSupervisionNotice domain entity/Purpose/State with EF mapping; SessionQueuedMessage purpose identity; SessionMessageQueueService.cs; CompletionNoteWork.cs; Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs; new LateFailedReportForwarder.cs; DelegationReportFormatter.cs; TaskCompletionNotification.cs; PipelineHandoffActionService.cs; AgentTaskService child admission; lifecycle/runtime docs | TaskSupervisionReceiptTests (21); full DelegationDispatchRecoveryBoundaryTests including C1161, full reply/runtime and completion-recovery classes, successor/control witnesses |
| S4 budgets/Deploy/client | scripts/delegate.ps1 typed Reply/create/Status and explicit operator-Extend option using existing token helper; scripts/deploy-server2.ps1; dispatch/clone/formatter/DTO budget bindings; client/src/api/agentTasks.ts; new features/delegations/CeilingHoldCard.tsx, TaskDetailBody.tsx; docs/docker-stack.md, docs/ops-http.md | Scripts/TaskCeilingScriptTests (11); client/src/features/delegations/CeilingHold.test.tsx (3); existing script contracts in Full Unit shards; allowlist/ProcessSpawnLimit roster guards |

TaskSupervisionNotice has a typed purpose (CeilingParent, FailureDelegate,
LateFailedParent), immutable task/attempt/generation/recipient/event identity and
bounded payload, plus Pending/Told/Superseded/NotApplicable receipt state. The
failure purpose retains its report-scan frontier even after its own notice is
Told. A unique purpose key prevents task-wide completion stamps from hiding
other obligations. This is delivery accounting, not a wait-credit or outcome-
correction ledger. No broker, new runner protocol or automatic stop is added.

Commit behavioral tests before fixes. S1 is independently reviewable and can be
ported first. S2/S3 ship together; S4 waits for CARD-1615. S2-S4 share a final
source/build group after those ordering gates, not piecemeal activation. Never
edit source during a running checkpoint. Compatible type/scaffold changes may
precede red witnesses but retain old behavior; compile errors do not count red.

## Verification design

This section folds TestDesign into this last Plan round; **next: code**. It is
an executable ordinary roster, not a claim that tests exist or ran. At Code's
actual base reconcile the committed roster with source discovery and fresh TRX;
unknown/missing/extra cases refuse Full credit until explained and the manifest
is amended. Do not silently omit a class or run a broader suite. The following
new class counts are exact design commitments (96 TUnit + 3 client cases).
Round 4 raises the V-1 commitment from 20 to 22.

| ID | New class/method | Expanded cases and essential oracle |
|---|---|---|
| V-1 | TaskSupervisionVisibilityTests.C1610_Visibility_and_pool_safety | 22 Arguments: failed-fresh, failed-unknown, taskless-ttl, taskless-cap, real-idle, restart-resolution, failed-dedup, source-custody, reuse, retain-created, retain-stopping, retain-no-generation, retain-remote-pending, absent-transcript, zero-row-ttl, zero-row-cap, zero-row-recheck, mixed-cap, zero-row-terminal, reuse-other-agent, later-agent-task, id-only-query. Actual attention plus custody/no-stop assertions. zero-row-recheck is a helper witness. mixed-cap retains the oldest zero-row agent and kills the 2-minute TurnEnd agent. zero-row-terminal sweeps a Stopped zero-row row without a process kill |
| V-2 | TaskCeilingPolicyTests.C1610_Declared_budget_validation; C1610_Phase_dispatch_guards | 8 budget inputs (null,1,720,0,-1,721,overflow,reasonless), 4 guards (phase-wins, boot, heartbeat, uncertain); 12 |
| V-3 | TaskCeilingHoldTests.C1610_Working_ceiling_holds | Review/Code/Deploy/Plan/Check; 5; Working status and active episode, no terminal save/release |
| V-4 | TaskCeilingHoldTests.C1610_Phase_beats_ceiling; C1610_Held_task_keeps_phase_supervision | Each ModelWait/LocalExecution; 4; real phase failure wins despite larger ceiling fraction |
| V-5 | TaskCeilingHoldTests.C1610_Normal_report_settles_hold | done/blocked/failed; 3; original report floor, exactly one normal outcome |
| V-6 | TaskCeilingHoldTests.C1610_Hold_race_preserves_identity; C1610_Hold_preserves_custody_and_restart_identity; C1610_Non_ceiling_blocks_remain_excluded | report/retry/cancel/generation (4), custody/restart (2), question/quota/routing (3); 9 |
| V-7 | TaskCeilingHoldTests.C1610_Hold_keeps_occupancy | stage-absolute/local/pipeline/check; 4; D-1 actual selectors and 409/remaining/launch/CARD-0079 oracles; Hold total 25 |
| V-8 | TaskCeilingControlTests.C1610_Continue_keeps_report_floor; C1610_Extend_validates_and_rearms; C1610_Stale_answer_is_refused; C1610_Answer_races_report_without_reprompt | 1 + valid/cap/invalid (3) + episode/attempt (2) + 1; 7 |
| V-9 | TaskCeilingControlTests.C1610_Successor_fence_uses_card_and_role; C1610_Replacement_racing_handoff_is_held; C1610_Retry_resets_hold_but_preserves_requested_ceiling | same-role/different-role/no-card/pre-hold-successor (4) + 1 + 1; 6 |
| V-10 | TaskCeilingControlTests.C1610_No_phase_tail_rearms; C1610_Final_cap_requires_operator_extend | compaction/title/queue (3), ordinary-refusal/unauthorized-operator/authenticated-rearm (3); Control total 19 |
| V-11 | TaskSupervisionReceiptTests.C1610_Hold_parent_has_complete_UserPrompt; C1610_Failed_delegate_has_complete_UserPrompt; C1610_Failed_late_report_parent_has_complete_UserPrompt | eligible/busy/crash each; 9; D-9 real producer to full recipient UserPrompt plus decoy, durable retry, zero automatic stops |
| V-12 | TaskSupervisionReceiptTests.C1610_Stale_notice_is_not_delivered; C1610_Late_report_identity_is_fenced; C1610_Failure_writer_records_notice | stale notice (3), late identity (3), six actual writer-save boundaries from D-2 (6); Receipt total 21 |
| V-13 | TaskCeilingScriptTests.C1610_Status; C1610_Declared_budget; C1610_Ceiling_reply; C1610_Drain_wait | fresh/unknown (2), valid/invalid input sets (2), typed-action shape and explicit operator-mode auth (1), drain default/eight/oversize-or-all/inherited-token/standalone/resume (6); 11 |
| V-14 | CeilingHold.test.tsx | Continue, Extend, Cancel; 3. Render a Working held task through TaskDetailBody, assert typed payload, no ordinary reply/Retry/auto-continue, cap guidance and stale answer feedback |

S1-red expected outcomes are explicit: failed-fresh, failed-unknown, taskless-ttl,
taskless-cap, restart-resolution and failed-dedup fail their missing feature
assertion (6 red); real-idle and source-custody are existing safety guards (2
green). Round 2 adds reuse, retain-created, retain-stopping, retain-no-generation,
retain-remote-pending and absent-transcript, so the round-2 roster was 14. reuse and
absent-transcript are red on the pre-fix tip. The four retain arms pass until
Created, Stopping, a missing generation, or a pending remote inventory is treated
as idle. Round 3 follows D-10 and makes V-1 20 by adding zero-row-ttl, zero-row-cap,
zero-row-recheck, reuse-other-agent, later-agent-task and id-only-query. On the
pre-fix tip the three zero-row arms fail because a transcript with no rows is still
eligible to kill. Round 4 makes V-1 22 by adding mixed-cap and zero-row-terminal.
Both pass on the committed tip. Removing the first-pass missing-boundary clause
leaves the earlier arms green and fails these two: the cap retires nothing, and
the Stopped zero-row row stays in the retire set so the stale sweep never takes it.
reuse-other-agent and later-agent-task pass on that tip and go red
only under their own mutations (any Failed on the session holds; the agent-latest
clause dropped). A later Canceled never-dispatched task on the agent releases the
hold (A-1). Generation is proved only by StartedAt != default; there is no
DispatchedAt >= StartedAt comparison, so a pre-restart Failed attempt still holds
(A-2). The changed janitor test is one more red (old kill). Never count a guard
that already passes as a new red. New negative cases in later red groups may
already pass; retain per-case expected outcomes in the red receipt rather than
requiring every argument to fail. New behavior must have a genuine assertion red.

Before a fix, record each witness's actual failure and the tested SHA. After the
fix, all ordinary rows must pass with no unexpected skip. Positive controls are
method-scoped on the committed bound pack in the separately commissioned
post-land Mutation stage: force Blocked in C1610_Working_ceiling_holds; drop each
Working selector in C1610_Hold_keeps_occupancy; suppress re-arm in
C1610_No_phase_tail_rearms; admit unsigned cap override in
C1610_Final_cap_requires_operator_extend; move Reply watermark in
C1610_Continue_keeps_report_floor; use HeldAt in
C1610_Successor_fence_uses_card_and_role; restore old TTL kill in
C1610_Visibility_and_pool_safety; skip obligation replay/pretend queue ACK is
receipt in each of the three producer receipt methods; admit wrong attempt in
C1610_Late_report_identity_is_fenced; and remove delegated validation in
C1610_Drain_wait. Each precise method filter must fail its named oracle, then
pass after restore; no whole-class mutation run and no fixture/build failure as red.

### Full existing affected classes

Counts below are Roslyn syntax census at the round-3 base, with Arguments
expanded and partial declarations merged by actual class/namespace. They are
expected execution counts, not executed results. AgentTaskOverdueDeadlineTests
has 36 cases; its file also declares AgentTaskDispatcherWiringTests separately.
AgentTaskReplyC547HoldTests.cs is a partial of **AgentTaskReplyIntegrationTests**,
not a class named after the file. Full-class selections below include every
method; do not replace them with C1161/C1259 or the single changed janitor case.
Changing an existing assertion is allowed only for the designed behavior change:
keep real-idle, phase, boot, commit-recovery, receipt and identity guards intact.

| Existing full class | Expanded cases | Source/lane | Group |
|---|---:|---|---|
| AgentTaskPoolTests | 48 | 1 source file(s), Integration | S1 and final |
| AgentTaskOverdueDeadlineTests | 36 | 1 source file(s), Integration | S1 and final |
| AttentionServiceTests | 167 | 3 source file(s), Integration/Slow | S1 and final |
| SeatOccupancyAttentionTests | 10 | 1 source file(s), Integration | S1 and final |
| TaskDeadlinePolicyTests | 39 | 1 source file(s), Integration | final |
| BootStallDetectionTests | 59 | 1 source file(s), Integration | final |
| AgentTaskReplyIntegrationTests | 341 | 18 source file(s), Integration | final |
| DelegationDispatchRecoveryBoundaryTests | 382 | 24 source file(s), Integration | final |
| SessionRuntimeActionRecoveryTests | 17 | 1 source file(s), Integration/Slow | final |
| SessionRuntimeActionTests | 28 | 1 source file(s), Integration/Slow | final |
| SessionRuntimeActionRoutingTests | 35 | 2 source file(s), Integration/Slow | final |
| SessionRuntimeActionDeferralTests | 12 | 1 source file(s), Integration/Slow | final |
| SessionRuntimeActionTemporaryBlockTests | 4 | 1 source file(s), Integration/Slow | final |
| DispatchConcurrencyAdmissionTests | 8 | 1 source file(s), Integration | final |
| AgentTaskConcurrencyLimitTests | 25 | 1 source file(s), Integration | final |
| AgentTaskPipelineStatusTests | 69 | 6 source file(s), Integration | final |
| CheckCompactionRecoveryFlowTests | 1 | 1 source file(s), Integration | final |
| TaskProgressStallSweepTests | 13 | 1 source file(s), Integration | final |
| CompletionNoteDurableSweepTests | 1 | 1 source file(s), Integration | final |
| ApiErrorRecoveryServiceTests | 40 | 1 source file(s), Integration/Slow | final |
| SpecialistTaskRunnerDeadlineTests | 7 | 1 source file(s), Integration | final |
| PipelineHandoffPilotTests | 12 | 1 source file(s), Integration | final |
| AgentTaskDetailBlockedContextTests | 10 | 1 source file(s), Integration | final |
| AgentTaskDispatcherWiringTests | 1 | 1 source file(s), Integration | final |
| CheckCompactionContinuationTests | 2 | 1 source file(s), Integration | final |
| CheckCompactionCrashTests | 9 | 1 source file(s), Integration/Slow | final |
| CheckCompactionAttentionTests | 1 | 1 source file(s), Integration | final |
| CheckCompactionWorkerLifetimeTests | 2 | 1 source file(s), Integration/Slow | final |
| CheckCompactionReceiptTests | 27 | 1 source file(s), Integration | final |
| CheckCompactionAutomaticRestartTests | 11 | 1 source file(s), Integration | final |
| CheckCompactionRecoveryPersistenceTests | 2 | 1 source file(s), Integration | final |

S1 full Pool includes all three cap/TTL regressions called out by Review; rename
only the contradictory full-TTL Working-kill method. Full Overdue covers commit
recovery/abandonment saves. Full reply includes every C547 hold/recovery/audit
partial. Full runtime classes protect saved action identity even though Working
holds need no widened runtime selector. CompletionNoteWorkQueueTests, concurrency
policy, report formatter, Docker/deploy script contracts and classification guards
are included in the sharded Unit lane below. No product test executes in Plan.

### Final Unit coverage and filter census

The source-derived Unit roster contains **471 class-tagged classes / 6,252
expanded cases**, including the shared linked Antiphon.TestSupport guard. The
new TaskCeilingPolicyTests contributes 12 via its own green row. A separate exact
method row covers MatrixDefaultComplexityCreateTests' method-only Unit tag (8 Arguments).
Thus final Unit membership is **6,272 cases** at this design base. The Unit
shards partition existing classes; CP policy plus the method-only row completes
the lane. Do not claim Full from class shards alone (CARD-1573). Compare the
union of TRX test identities/categories against source discovery at the actual
Code SHA, including conditional/platform data. A missing case leaves Full
incomplete; an added case requires a roster/count update before execution.

Nine MethodDataSource methods were expanded from source providers, not counted
as singleton methods: ClaudeEffort InvalidCases=6; DispatchHold HoldSentences=18;
RolePolicy validation InvalidCases=8/ValidCases=12; RolePolicyRead Unsupported=15;
RemoteScriptContract hidden-location fixture=132; EntityScalarSnapshot consumer
files=4, distinct groups=9, unsupported runtime values=14. Other expanded counts
come from explicit Arguments or singleton tests. This is census, not discovery
or passing-test evidence. Preserve fresh TRX and receipt membership.

Suffix census found two real collisions: RunnerGrokAdapterReadyTests also prefixes
RunnerGrokAdapterReadyTestsPty; OutboundConversionManifestTests also prefixes
OutboundConversionManifestTestsPump. Their Unit classes get **single exact class**
rows; no wildcard can pull in the Integration companion. Every other combined
class token below has parentheses and a trailing *, pipes escaped in Markdown.
New C1610 class names have no source declaration/collision at this base. Recheck
if source changes; partials count once and helper declarations with no Test do
not add executions. All listed new C1610 methods are distinct within their class.

### Checkpoints

Each Group names its logical lane: server-unit, server-integration, script-
integration, runner-integration or client-Vitest. Portable work uses live defaults,
no host pin or OS flag. Native platform evidence, if a selected existing test
requires it, must use its required OS and cannot be credited by a skipped test.
Run Antiphon.Tests and SessionRunner.Tests sequentially; no Pty assembly here.
One build per distinct output per committed After group; no reuse across SHAs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1610-visibility-red/` | server-integration-visibility-red | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 | 22 executed; pre-fix red: zero-row-ttl, zero-row-cap, zero-row-recheck; mixed-cap and zero-row-terminal red only with the first-pass clause removed; no skipped | 22 | 6 |
| CP-2 | S1-red | CP-1 | server-integration-janitor-red | `/*/*/AgentTaskPoolTests/C1610_Janitor_retains_working_after_full_ttl` | S1 | 1 intended assertion red | 1 | 1 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1610-visibility/` | server-integration-visibility | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 | 22 passed; no failed/skipped | 22 | 6 |
| CP-4 | S1 | CP-3 | server-integration-AgentTaskPoolTests | `/*/*/AgentTaskPoolTests/*` | S1 full class | 48 passed; no failed/skipped | 48 | 4 |
| CP-5 | S1 | CP-3 | server-integration-AgentTaskOverdueDeadlineTests | `/*/*/AgentTaskOverdueDeadlineTests/*` | S1 full class | 36 passed; no failed/skipped | 36 | 4 |
| CP-6 | S1 | CP-3 | server-integration-AttentionServiceTests | `/*/*/AttentionServiceTests/*` | S1 full class | 167 passed; no failed/skipped | 167 | 12 |
| CP-7 | S1 | CP-3 | server-integration-SeatOccupancyAttentionTests | `/*/*/SeatOccupancyAttentionTests/*` | S1 full class | 10 passed; no failed/skipped | 10 | 2 |
| CP-8 | S1 | CP-3 | server-unit-guards | `/*/*/(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(ProcessSpawnLimitTests*)/*` | guards | 23 passed; no failed/skipped | 23 | 1 |
| CP-9 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-runner-safety/` | runner-integration-safety | `/*/*/TerminalSeatReleaseTests/(Working_remains_protected_after_arbitrary_silence*)\|(Explicit_operator_release_keeps_its_contract*)` | runner safety | 2 passed; no failed/skipped | 2 | 3 |
| CP-9a | S1 | CP-3 | server-integration-grok-kind-cap | `/*/*/GrokDelegateDispatchTests/the_cap_still_retires_a_surplus_within_one_kind` | proved-idle cap | 1 passed; no failed/skipped | 1 | 3 |
| CP-10 | S2-S4-red | `tests/Antiphon.Tests -> bin-c1610-hold-red/` | server-unit-policy-red | `/*/*/TaskCeilingPolicyTests/C1610_*` | V-2 | 12 executed; intended behavioral reds plus guards | 12 | 5 |
| CP-11 | S2-S4-red | CP-10 | server-integration-hold-control-red | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/C1610_*` | V-3..V-10 | 44 executed; intended behavioral reds plus guards | 44 | 3 |
| CP-12 | S2-S4-red | CP-10 | server-integration-receipts-red | `/*/*/TaskSupervisionReceiptTests/C1610_*` | V-11..V-12 | 21 executed; intended receipt/writer assertions red | 21 | 5 |
| CP-13 | S2-S4-red | CP-10 | script-integration-budget-red | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13 | 11 executed; intended script assertions red | 11 | 3 |
| CP-14 | S2-S4-red | n/a | client-Vitest-red | `pwsh -NoProfile -File scripts/test-client.ps1 CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-client-red.json` | V-14 | JSON total=3; all names; intended behavioral red, no setup error | n/a | 2 |
| CP-15 | S2-S4 | `tests/Antiphon.Tests -> bin-c1610-hold/` | server-unit-policy | `/*/*/TaskCeilingPolicyTests/C1610_*` | V-2 | 12 passed; no failed/skipped | 12 | 5 |
| CP-16 | S2-S4 | CP-15 | server-integration-hold-control | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/C1610_*` | V-3..V-10 | 44 passed; no failed/skipped | 44 | 3 |
| CP-17 | S2-S4 | CP-15 | server-integration-receipts | `/*/*/TaskSupervisionReceiptTests/C1610_*` | V-11..V-12 | 21 passed; no failed/skipped | 21 | 5 |
| CP-18 | S2-S4 | CP-15 | script-integration-budget | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13 | 11 passed; no failed/skipped | 11 | 3 |
| CP-19 | S2-S4 | CP-15 | server-integration-visibility-final | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 final source | 22 passed; no failed/skipped | 22 | 2 |
| CP-20 | S2-S4 | n/a | client-Vitest | `pwsh -NoProfile -File scripts/test-client.ps1 CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-client-green.json` | V-14 | fresh JSON total=3 passed=3 failed/pending/todo=0; all names; exit 0 | n/a | 2 |
| CP-21 | S2-S4 | CP-15 | server-integration-AgentTaskPoolTests | `/*/*/AgentTaskPoolTests/*` | full affected class | 48 passed; no failed/skipped | 48 | 4 |
| CP-22 | S2-S4 | CP-15 | server-integration-AgentTaskOverdueDeadlineTests | `/*/*/AgentTaskOverdueDeadlineTests/*` | full affected class | 36 passed; no failed/skipped | 36 | 4 |
| CP-23 | S2-S4 | CP-15 | server-integration-AttentionServiceTests | `/*/*/AttentionServiceTests/*` | full affected class | 167 passed; no failed/skipped | 167 | 12 |
| CP-24 | S2-S4 | CP-15 | server-integration-SeatOccupancyAttentionTests | `/*/*/SeatOccupancyAttentionTests/*` | full affected class | 10 passed; no failed/skipped | 10 | 2 |
| CP-25 | S2-S4 | CP-15 | server-integration-TaskDeadlinePolicyTests | `/*/*/TaskDeadlinePolicyTests/*` | full affected class | 39 passed; no failed/skipped | 39 | 3 |
| CP-26 | S2-S4 | CP-15 | server-integration-BootStallDetectionTests | `/*/*/BootStallDetectionTests/*` | full affected class | 59 passed; no failed/skipped | 59 | 4 |
| CP-27 | S2-S4 | CP-15 | server-integration-AgentTaskReplyIntegrationTests | `/*/*/AgentTaskReplyIntegrationTests/*` | full affected class | 341 passed; no failed/skipped | 341 | 25 |
| CP-28 | S2-S4 | CP-15 | server-integration-DelegationDispatchRecoveryBoundaryTests | `/*/*/DelegationDispatchRecoveryBoundaryTests/*` | full affected class | 382 passed; no failed/skipped | 382 | 25 |
| CP-29 | S2-S4 | CP-15 | server-integration-SessionRuntimeActionRecoveryTests | `/*/*/SessionRuntimeActionRecoveryTests/*` | full affected class | 17 passed; no failed/skipped | 17 | 3 |
| CP-30 | S2-S4 | CP-15 | server-integration-SessionRuntimeActionTests | `/*/*/SessionRuntimeActionTests/*` | full affected class | 28 passed; no failed/skipped | 28 | 3 |
| CP-31 | S2-S4 | CP-15 | server-integration-SessionRuntimeActionRoutingTests | `/*/*/SessionRuntimeActionRoutingTests/*` | full affected class | 35 passed; no failed/skipped | 35 | 3 |
| CP-32 | S2-S4 | CP-15 | server-integration-SessionRuntimeActionDeferralTests | `/*/*/SessionRuntimeActionDeferralTests/*` | full affected class | 12 passed; no failed/skipped | 12 | 2 |
| CP-33 | S2-S4 | CP-15 | server-integration-SessionRuntimeActionTemporaryBlockTests | `/*/*/SessionRuntimeActionTemporaryBlockTests/*` | full affected class | 4 passed; no failed/skipped | 4 | 1 |
| CP-34 | S2-S4 | CP-15 | server-integration-DispatchConcurrencyAdmissionTests | `/*/*/DispatchConcurrencyAdmissionTests/*` | full affected class | 8 passed; no failed/skipped | 8 | 2 |
| CP-35 | S2-S4 | CP-15 | server-integration-AgentTaskConcurrencyLimitTests | `/*/*/AgentTaskConcurrencyLimitTests/*` | full affected class | 25 passed; no failed/skipped | 25 | 3 |
| CP-36 | S2-S4 | CP-15 | server-integration-AgentTaskPipelineStatusTests | `/*/*/AgentTaskPipelineStatusTests/*` | full affected class | 69 passed; no failed/skipped | 69 | 4 |
| CP-37 | S2-S4 | CP-15 | server-integration-CheckCompactionRecoveryFlowTests | `/*/*/CheckCompactionRecoveryFlowTests/*` | full affected class | 1 passed; no failed/skipped | 1 | 1 |
| CP-38 | S2-S4 | CP-15 | server-integration-TaskProgressStallSweepTests | `/*/*/TaskProgressStallSweepTests/*` | full affected class | 13 passed; no failed/skipped | 13 | 2 |
| CP-39 | S2-S4 | CP-15 | server-integration-CompletionNoteDurableSweepTests | `/*/*/CompletionNoteDurableSweepTests/*` | full affected class | 1 passed; no failed/skipped | 1 | 2 |
| CP-40 | S2-S4 | CP-15 | server-integration-ApiErrorRecoveryServiceTests | `/*/*/ApiErrorRecoveryServiceTests/*` | full affected class | 40 passed; no failed/skipped | 40 | 5 |
| CP-41 | S2-S4 | CP-15 | server-integration-SpecialistTaskRunnerDeadlineTests | `/*/*/SpecialistTaskRunnerDeadlineTests/*` | full affected class | 7 passed; no failed/skipped | 7 | 2 |
| CP-42 | S2-S4 | CP-15 | server-integration-PipelineHandoffPilotTests | `/*/*/PipelineHandoffPilotTests/*` | full affected class | 12 passed; no failed/skipped | 12 | 3 |
| CP-43 | S2-S4 | CP-15 | server-integration-AgentTaskDetailBlockedContextTests | `/*/*/AgentTaskDetailBlockedContextTests/*` | full affected class | 10 passed; no failed/skipped | 10 | 2 |
| CP-44 | S2-S4 | CP-15 | server-integration-AgentTaskDispatcherWiringTests | `/*/*/AgentTaskDispatcherWiringTests/*` | full affected class | 1 passed; no failed/skipped | 1 | 1 |
| CP-45 | S2-S4 | CP-15 | server-unit-shard-01 | `/*/Antiphon.Tests/(CachingChatClientTests*)\|(LandDeliveryWaitRoutingTests*)\|(ProcessSpawnLimitTests*)\|(ProgressAwareWaitTests*)/*` | Final Unit partition | 44 passed; no failed/skipped | 44 | 2 |
| CP-46 | S2-S4 | CP-15 | server-unit-shard-02 | `/*/Antiphon.Tests.Agents/(AgentLaunchSpecTests*)\|(AgentProtocolAdapterFactoryTests*)\|(AgentRegistrySettingsTests*)\|(AgentRegistryTests*)\|(AgentSessionSettingsTests*)\|(CodexAdapterRawResponseTests*)\|(CodexResponseAnalyzerTests*)\|(CodexWorkingIndicatorTests*)\|(FakeLlmApiSelfTests*)\|(GrokAdapterTests*)\|(GrokCredentialStoreTests*)\|(GrokFreshWorktreeQualificationTests*)\|(GrokLinuxBlockingPromptTests*)\|(GrokLinuxStartupReadinessTests*)\|(GrokModelListParserTests*)\|(GrokSignInPromptDetectorTests*)\|(GrokStartupCaptureStoreTests*)\|(GrokStartupReadinessTests*)\|(GrokStartupSizeTests*)\|(GrokTrustPromptDetectorTests*)\|(HerdrEnvelopeBodyTests*)\|(OpenCodeAdapterTests*)\|(RunnerClaudeAdapterEffortPromptTests*)\|(RunnerClaudeAdapterTrustPromptTests*)\|(RunnerClaudeAdapterVerifiedPromptTests*)/*` | Final Unit partition | 215 passed, 1 Windows-only skips on Linux; 216 passed on Windows; no unexpected skips/failed | 215 | 3 |
| CP-47 | S2-S4 | CP-15 | server-unit-shard-03 | `/*/Antiphon.Tests.Agents/(RunnerCodexAdapterReadyTests*)\|(RunnerCodexAdapterSubmitConfirmTests*)\|(RunnerCodexAdapterTurnCompleteTests*)/*` | Final Unit partition | 29 passed; no failed/skipped | 29 | 2 |
| CP-48 | S2-S4 | CP-15 | server-unit-shard-04 | `/*/Antiphon.Tests.Agents/RunnerGrokAdapterReadyTests/*` | Final Unit partition | 22 passed; no failed/skipped | 22 | 2 |
| CP-49 | S2-S4 | CP-15 | server-unit-shard-05 | `/*/Antiphon.Tests.Agents/(RunnerGrokAdapterSignInPromptTests*)\|(RunnerGrokAdapterTrustPromptTests*)\|(RunnerGrokAdapterTurnCompleteTests*)\|(RunnerTerminalSessionGenerationTests*)\|(RunnerTerminalSessionInputEncodingTests*)\|(TestOwnedPtyHostIdentityTests*)\|(TestOwnedPtyHostPolicyTests*)\|(TranscriptNormalizerTests*)\|(TranscriptTurnBaselineTests*)/*` | Final Unit partition | 107 passed; no failed/skipped | 107 | 2 |
| CP-50 | S2-S4 | CP-15 | server-unit-shard-06 | `/*/Antiphon.Tests.AgentTui/(AgentTuiOperationCoordinatorTests*)\|(AgentTuiSecretProtectorTests*)\|(AgentTuiSmokeScriptTests*)\|(DollarEnvArgTests*)\|(RunnerProbePowerShellFixtureTests*)\|(RunnerProcessProbeRedactionTests*)/*` | Final Unit partition | 158 passed; no failed/skipped | 158 | 2 |
| CP-51 | S2-S4 | CP-15 | server-unit-shard-07 | `/*/Antiphon.Tests.ApiKeys/(AgentLaunchEnvTests*)\|(ApiKeyPlaceholderTests*)\|(ApiKeyProtectorTests*)\|(ProviderEndpointProjectionTests*)/*` | Final Unit partition | 89 passed; no failed/skipped | 89 | 2 |
| CP-52 | S2-S4 | CP-15 | server-unit-shard-08 | `/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)\|(AgentContextContractTests*)\|(AgentExecutableResolverTests*)\|(AgentPinPathTests*)\|(AgentPresetsTests*)\|(AgentSessionRuntimeBoundedReadTests*)\|(AgentTaskDecisionQuestionTests*)\|(AgentTaskDispatcherPredicateTests*)\|(AgentTaskLandingStateTests*)\|(AgentTaskLandQueueVisibilityTests*)\|(AgentTaskLandVerificationEvidenceTests*)\|(AgentTaskLivenessTests*)\|(AlarmSettingsValidatorTests*)/*` | Final Unit partition | 247 passed, 2 Windows-only skips on Linux; 249 passed on Windows; no unexpected skips/failed | 247 | 4 |
| CP-53 | S2-S4 | CP-15 | server-unit-shard-09 | `/*/Antiphon.Tests.Application/(ApiErrorClassifierTests*)\|(ApiErrorRetryScheduleTests*)\|(AreaMapContractTests*)\|(AreaMapLoaderTests*)\|(AttentionKindWireTests*)\|(AttentionSettingsValidatorTests*)\|(AwayDigestFormatterTests*)\|(BlockedQuestionTests*)\|(BlockedTaskParkProjectionTests*)\|(BoardServiceNeedsHumanReviewTests*)\|(BootStallDocumentationTests*)\|(BootStallPolicyTests*)\|(BootWedgeContractTests*)\|(CapacityRecoveryPolicyTests*)\|(CardAliasNormalizationTests*)\|(CardFilePolicyTests*)\|(CardFilePrivacyDocumentationTests*)\|(CardIdentifierAllocatorTests*)\|(CardRankingOrderTests*)\|(CardRankingTests*)\|(CardTaskFileRendererTests*)\|(CardWorkflowRunFactoryTests*)\|(ChannelContractsTests*)\|(ChannelInboundDebouncerTests*)\|(ChannelMachineTurnMatchTests*)/*` | Final Unit partition | 173 passed; no failed/skipped | 173 | 3 |
| CP-54 | S2-S4 | CP-15 | server-unit-shard-10 | `/*/Antiphon.Tests.Application/(ChannelOutboundContractTests*)\|(ChannelOutboundEvidenceAccountingTests*)\|(ChannelOutboundStorageTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelReplyAttachmentReaderTests*)\|(CheckCompactionContinuationSettingsTests*)\|(CheckInterpretationValidatorTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckScheduleBackoffTests*)\|(CheckScheduleElapsedTimesTests*)\|(CheckScheduleRoundingTests*)\|(CheckSpecialistLaunchPolicyTests*)\|(ClaudeEffortByRoleSettingsTests*)\|(ClaudeLaunchArgsTests*)\|(ClaudeRemoteControlLaunchArgsTests*)\|(CodexLaunchArgsTests*)\|(CodexPhoneHomeProjectionTests*)\|(ColumnTextTests*)\|(CommitOnSettleDocumentationTests*)\|(CommitOnSettlePolicyResolverTests*)\|(CommitRecoveryObligationsTests*)\|(CompactionContinuationPolicyTests*)\|(CompletionHeaderOverlapTests*)/*` | Final Unit partition | 249 passed, 1 Windows-only skips on Linux; 250 passed on Windows; no unexpected skips/failed | 249 | 4 |
| CP-55 | S2-S4 | CP-15 | server-unit-shard-11 | `/*/Antiphon.Tests.Application/(CompletionNoteWorkQueueTests*)\|(ComplexityEffectiveComposeTests*)\|(ComplexityRoutingComposeTests*)\|(ContextCompactionSettingsTests*)\|(D2AdvisoryDocumentationTests*)\|(DelegateBindRefusalRecoveryTests*)\|(DelegateBranchNameTests*)\|(DelegationAllowedRootsFileTests*)\|(DelegationCapabilityContractTests*)\|(DelegationCostTests*)\|(DelegationDenyHookPolicyTests*)\|(DelegationKindPricingTests*)\|(DelegationLeaseSettingsTests*)\|(DelegationPoolDefaultsTests*)\|(DelegationQuestionDetectionTests*)\|(DelegationReportFormatterTests*)\|(DelegationScopeLeaseTests*)\|(DelegationWorkspaceBoundaryTests*)\|(DeliveryBackendCeilingsTests*)/*` | Final Unit partition | 244 passed, 1 Windows-only skips on Linux; 245 passed on Windows; no unexpected skips/failed | 244 | 4 |
| CP-56 | S2-S4 | CP-15 | server-unit-shard-12 | `/*/Antiphon.Tests.Application/(DesktopCapabilitiesSnapshotTests*)\|(DiagnosisTests*)\|(DiagnosticsRedactorTests*)\|(DialogDeliveryAdmissionTests*)\|(DirectoryBrowseServiceTests*)\|(DirectoryMatcherTests*)\|(DispatchBaseWarningRosterTests*)\|(DispatchConcurrencyPolicyTests*)\|(DispatchHoldLedgerTests*)\|(ExpectationDirectiveTests*)\|(ExpectationPipelineTests*)\|(GitIndexLockTests*)\|(GrokComposerScreenTests*)\|(GrokDeliveryShapeTests*)\|(GrokLaunchArgsTests*)\|(GrokNativeSessionResumeTests*)\|(GrokQuestionPopupTests*)\|(GrokRulesLiveEvidenceTests*)\|(GrokRulesTransportCompatibilityTests*)\|(HerdrAgentKindMapTests*)\|(HerdrLaunchContextTitleTests*)\|(HerdrStatusDisagreementTests*)/*` | Final Unit partition | 224 passed, 17 Windows-only skips on Linux; 241 passed on Windows; no unexpected skips/failed | 224 | 4 |
| CP-57 | S2-S4 | CP-15 | server-unit-shard-13 | `/*/Antiphon.Tests.Application/(HerdrSupervisionFailureEvidenceTests*)\|(HostCleanupWorktreeInventoryTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsCacheTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(InstructionFragmentTests*)\|(InternalDecisionPolicyTests*)\|(IssueTrackerConfigParserTests*)\|(KeepSixDocumentationTests*)\|(KeepSixProjectionTests*)\|(LandFailureDiagnosticTests*)\|(LandMergeWriterTests*)/*` | Final Unit partition | 214 passed; no failed/skipped | 214 | 3 |
| CP-58 | S2-S4 | CP-15 | server-unit-shard-14 | `/*/Antiphon.Tests.Application/(LandReceiptScanCacheTests*)\|(LandVerificationBuildSlotTests*)\|(LaunchModelArgumentAppenderTests*)\|(LeaseTurnScenarioTests*)\|(MatrixDefaultComplexitySettingsTests*)\|(MechanicalPcContractTests*)\|(MidTurnInterruptDocumentationTests*)\|(MidTurnInterruptPolicyTests*)/*` | Final Unit partition | 184 passed; no failed/skipped | 184 | 3 |
| CP-59 | S2-S4 | CP-15 | server-unit-shard-15 | `/*/Antiphon.Tests.Application/(ModelAliasTests*)\|(ModelLevelAliasDisplayTests*)\|(MutationRoleContractTests*)\|(OperatorShutdownCoordinatorTests*)\|(OrchestratorBundleOwnershipTests*)\|(OrchestratorInstructionsDeltaTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(OrchestratorInstructionsPathTests*)\|(OrchestratorInstructionsRecipientTests*)\|(OrchestratorInstructionsRendererTests*)\|(OrchestratorInvestigationDetectorTests*)\|(OrchestratorSettingsTests*)\|(OrchestratorWorkspaceLayoutTests*)/*` | Final Unit partition | 184 passed; no failed/skipped | 184 | 3 |
| CP-60 | S2-S4 | CP-15 | server-unit-shard-16 | `/*/Antiphon.Tests.Application/OutboundConversionManifestTests/*` | Final Unit partition | 35 passed; no failed/skipped | 35 | 2 |
| CP-61 | S2-S4 | CP-15 | server-unit-shard-17 | `/*/Antiphon.Tests.Application/(OutputDistillationGateTests*)\|(OutputDistillationPolicyTests*)\|(OutputDistillationQueueTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)\|(PhoneHomeRunnerSettingsValidatorTests*)\|(PhoneHomeSpillTests*)\|(PhoneHomeTaskRoutingTests*)\|(PipelineDefinitionDocumentationTests*)\|(PipelineHandoffParseTests*)\|(PipelineHandoffRouteDocumentationTests*)\|(PipelineStagesJsonTests*)\|(PlanCatalogServiceTests*)/*` | Final Unit partition | 247 passed; no failed/skipped | 247 | 4 |
| CP-62 | S2-S4 | CP-15 | server-unit-shard-18 | `/*/Antiphon.Tests.Application/(PolicyRefreshDeltaTests*)\|(PostLandMutationContractTests*)\|(PostLandMutationPublicationTests*)\|(PostLandMutationReceiptPolicyTests*)\|(PostLandRetrospectiveContractTests*)\|(ProviderCapacityNoticeTests*)\|(ProviderContractCatalogTests*)\|(ProviderEndpointDocumentationTests*)\|(ProviderQuotaRefusalTests*)\|(PtyDeliveryCeilingsTests*)\|(PtyInlineCeilingTests*)\|(PublishedRoutingDocumentationTests*)\|(ReceiptScanDestinationTests*)\|(ReleaseBranchModelDocumentationTests*)\|(RemoteControlMenuScreenTests*)\|(RemoteControlPolicyTests*)\|(RemoteVerificationWorkspaceTests*)/*` | Final Unit partition | 238 passed, 4 Windows-only skips on Linux; 242 passed on Windows; no unexpected skips/failed | 238 | 4 |
| CP-63 | S2-S4 | CP-15 | server-unit-shard-19 | `/*/Antiphon.Tests.Application/(RemoteWorktreeMirrorTests*)\|(RepairSourceDocumentationTests*)\|(RepositoryLeaseWaitersTests*)\|(RestartFailureClassificationTests*)\|(ReviewEvidenceParserTests*)\|(RolePolicyConfigurationValidationTests*)\|(RolePolicyReadServiceTests*)\|(RolePolicyToolBundleTests*)\|(RoutingCandidateParseTests*)\|(RunnerAbsenceEvidenceValidatorTests*)\|(RunnerBranchContractDocumentationTests*)\|(RunnerCapabilitiesFailureTests*)\|(RunnerContractMapperTranscriptEventTests*)\|(RunnerDefaultGuidanceTests*)\|(RunnerReadSourcePinTests*)\|(RunnerSlotRulesTests*)\|(RunnerWorkspaceParkPathTests*)\|(ScheduleRecurrenceTests*)\|(ScopeDriftTests*)\|(ScopedVerificationInstructionTests*)\|(ScopeOverlapPolicyTests*)\|(ScopeResolverAreaTests*)\|(SeatOccupancyProjectionTests*)/*` | Final Unit partition | 241 passed; no failed/skipped | 241 | 4 |
| CP-64 | S2-S4 | CP-15 | server-unit-shard-20 | `/*/Antiphon.Tests.Application/(SessionContextUsageTests*)\|(SessionDeliveryProfileTests*)\|(SessionReAdoptionStateTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerSettingsTests*)\|(SettlementSyncDebtAttentionTests*)\|(SettlementSyncDebtPolicyTests*)\|(SharedWriterLeaseProjectionTests*)\|(SiblingInspectionResultsTests*)\|(SiblingWarningReducerTests*)\|(SlashCommandCatalogServiceTests*)\|(SlashCommandParserTests*)\|(SourceLandingAdmissionTests*)\|(SpecialistAttemptEvidenceTests*)\|(SpecialistFailurePolicyTests*)\|(SpecialistQualificationContractTests*)\|(SpecialistRoleContractTests*)\|(StandingBootDocumentationTests*)\|(StandingBootWatchPolicyTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(StandingSpecialistHealthPolicyTests*)\|(SubagentNotificationTests*)\|(SubscriptionQuotaGateTests*)\|(SubscriptionUsageParserTests*)/*` | Final Unit partition | 239 passed, 2 Windows-only skips on Linux; 241 passed on Windows; no unexpected skips/failed | 239 | 4 |
| CP-65 | S2-S4 | CP-15 | server-unit-shard-21 | `/*/Antiphon.Tests.Application/(TaskCompletionContinuationTests*)\|(TaskCompletionProgressPolicyTests*)\|(TaskPlatformGuidanceTests*)\|(TaskReportHousekeepingTests*)\|(TierReasoningEffortAgreementTests*)\|(TrackerSyncSummaryFormatterTests*)\|(TranscriptIncidentReplayGuardTests*)\|(TranscriptLocalCommandEchoTests*)\|(TypedBodySpillTests*)\|(UnmarkedWaitingContractTests*)\|(UsageLimitWallParserTests*)\|(VerificationReceiptPolicyTests*)\|(VerificationRoundBriefTests*)\|(VerificationRoundInstructionTests*)\|(WorkerWorkspaceDefaultMigrationShapeTests*)\|(WorkerWorkspaceDefaultStorageTests*)\|(WorkflowEngineParsingTests*)\|(WorkspaceReservationKeyTests*)\|(WorkspaceReservationLivenessTests*)/*` | Final Unit partition | 232 passed; no failed/skipped | 232 | 3 |
| CP-66 | S2-S4 | CP-15 | server-unit-shard-22 | `/*/Antiphon.Tests.Application/(WorktreeCleanupPresentationTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(WorktreeIgnoredContentClassifierTests*)\|(WorktreeListParsingTests*)/*` | Final Unit partition | 89 passed; no failed/skipped | 89 | 2 |
| CP-67 | S2-S4 | CP-15 | server-unit-shard-23 | `/*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)\|(BuildSlotClientTests*)\|(C1307CliCancellationTests*)\|(CheckpointAdmissionBudgetTests*)\|(CheckpointAdmissionContractTests*)\|(CheckpointAppTests*)\|(CheckpointBoundedReaderTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointConfinementLedgerTests*)\|(CheckpointEofStallTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointFixtureProcessTests*)\|(CheckpointFreshOutputSealTests*)\|(CheckpointImportTests*)\|(CheckpointLargeStampScriptTests*)\|(CheckpointLargeStampTests*)\|(CheckpointLaunchCleanupTests*)\|(CheckpointLineTests*)\|(CheckpointManifestTests*)\|(CheckpointNamespaceCensusUsageTests*)\|(CheckpointOrFilterTests*)\|(CheckpointOutputConfinementTests*)\|(CheckpointOwnedOutputTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointReceiptSafetyTests*)/*` | Final Unit partition | 216 passed; no failed/skipped | 216 | 3 |
| CP-67a | S2-S4 | CP-15 | server-unit-shard-23-diagnostic-1 | `/*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)\|(BuildSlotClientTests*)\|(C1307CliCancellationTests*)\|(CheckpointAdmissionBudgetTests*)\|(CheckpointAdmissionContractTests*)/*` | CP-67 disjoint diagnostic partition | 38 passed; no failed/skipped | 38 | 3 |
| CP-67b | S2-S4 | CP-15 | server-unit-shard-23-diagnostic-2 | `/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)\|(CheckpointBoundedReaderTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointConfinementLedgerTests*)\|(CheckpointEofStallTests*)/*` | CP-67 disjoint diagnostic partition | 48 passed; no failed/skipped | 48 | 3 |
| CP-67c | S2-S4 | CP-15 | server-unit-shard-23-diagnostic-3 | `/*/Antiphon.Tests.Checkpoints/(CheckpointExecutorLogTests*)\|(CheckpointFixtureProcessTests*)\|(CheckpointFreshOutputSealTests*)\|(CheckpointImportTests*)\|(CheckpointLargeStampScriptTests*)/*` | CP-67 disjoint diagnostic partition | 40 passed; no failed/skipped | 40 | 3 |
| CP-67d | S2-S4 | CP-15 | server-unit-shard-23-diagnostic-4 | `/*/Antiphon.Tests.Checkpoints/(CheckpointLargeStampTests*)\|(CheckpointLaunchCleanupTests*)\|(CheckpointLineTests*)\|(CheckpointManifestTests*)\|(CheckpointNamespaceCensusUsageTests*)/*` | CP-67 disjoint diagnostic partition | 28 passed; no failed/skipped | 28 | 3 |
| CP-67e | S2-S4 | CP-15 | server-unit-shard-23-diagnostic-5 | `/*/Antiphon.Tests.Checkpoints/(CheckpointOrFilterTests*)\|(CheckpointOutputConfinementTests*)\|(CheckpointOwnedOutputTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointReceiptSafetyTests*)/*` | CP-67 disjoint diagnostic partition | 62 passed; no failed/skipped | 62 | 3 |
| CP-68 | S2-S4 | CP-15 | server-unit-shard-24 | `/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)\|(CheckpointRepeatConfigurationTests*)\|(CheckpointRepeatDocumentationTests*)\|(CheckpointRepeatHostTests*)\|(CheckpointRunOwnershipTests*)\|(CheckpointScriptReaderDeadlineTests*)\|(CheckpointShapeConflictTests*)\|(CheckpointSlotContractTests*)\|(CheckpointStampSafetyTests*)\|(CheckpointTaskOwnershipTests*)\|(CheckpointTempRootSweepTests*)\|(CheckpointTempScopeTests*)\|(CheckpointTimingHarnessTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointToolSourceTests*)\|(CheckpointTrackedBaselineTests*)\|(CheckpointWindowsReadModeTests*)\|(DetachedLauncherTests*)\|(EvidenceFolderTests*)\|(ExitCodeTests*)/*` | Final Unit partition | 215 passed; no failed/skipped | 215 | 3 |
| CP-69 | S2-S4 | CP-15 | server-unit-shard-25 | `/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)\|(PlanCoverageAssertionTests*)\|(PlanCoverageBindingTests*)\|(PlanCoverageCensusTests*)\|(PlanCoverageCommandTests*)\|(PlanCoverageGoldenTests*)\|(PlanCoverageHandleTests*)\|(PlanCoverageParserTests*)\|(PlanCoveragePcTests*)\|(ProcessDriverTests*)\|(ReapedProcStatIdentityTests*)\|(ReportMergerTests*)\|(ReportWriterTests*)\|(RerunFilterHostTests*)\|(RerunPolicyTests*)\|(RowRunnerTests*)\|(RunSchedulerTests*)\|(RunStateStoreTests*)\|(SafeDeleteProcessBoundaryTests*)\|(ShadowCopyTests*)\|(TestPhaseCustodyTests*)\|(TestSetTimingTests*)\|(TimeoutTests*)\|(TimingJournalTests*)\|(TrxReportTests*)/*` | Final Unit partition | 196 passed; no failed/skipped | 196 | 3 |
| CP-70 | S2-S4 | CP-15 | server-unit-shard-26 | `/*/Antiphon.Tests.Checkpoints/WaitCommandTests/*` | Final Unit partition | 12 passed; no failed/skipped | 12 | 2 |
| CP-71 | S2-S4 | CP-15 | server-unit-shard-27 | `/*/Antiphon.Tests.Domain.StateMachine/(CardStateMachineTests*)\|(RunAttemptStateMachineTests*)\|(WorkflowStateMachineTests*)/*` | Final Unit partition | 61 passed; no failed/skipped | 61 | 2 |
| CP-72 | S2-S4 | CP-15 | server-unit-shard-28 | `/*/Antiphon.Tests.Infrastructure/(AppDbContextModelTests*)\|(AppHostBrokerSourceGuardTests*)\|(C1307CancellationTests*)\|(C1307ToolRemovalTests*)\|(CheckpointToolProjectContractTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(CustodyHelperContractTests*)\|(DindRunnerContractTests*)\|(DirectoryBuildRspTests*)\|(DockerDeliveryBarrierTests*)\|(DockerDeliveryObservationTests*)\|(DockerStackContractTests*)/*` | Final Unit partition | 246 passed; no failed/skipped | 246 | 4 |
| CP-73 | S2-S4 | CP-15 | server-unit-shard-29 | `/*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)\|(DotnetToolManifestContractTests*)\|(E2ECopyOwnedPtyHostAppHostContractTests*)\|(EvidencePolicyWorkflowTests*)\|(FileSystemEvidenceRetentionTests*)\|(FileUseWindowsTests*)\|(GithubCredentialHelperTests*)\|(GitMinimumVersionGuardTests*)\|(GrokRunnerImageContractTests*)\|(GrokSha256PinTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(HostCleanupCheckpointCompatibilityTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointExtractionTests*)\|(HostCleanupCheckpointPathTests*)\|(HostCleanupCheckpointReadDeadlineTests*)\|(HostCleanupCheckpointReceiptTests*)\|(HostCleanupCheckpointSweepRaceTests*)\|(HostCleanupExecutionTests*)\|(HostCleanupOwnershipTests*)\|(HostCleanupPolicyTests*)\|(JqRunnerImageContractTests*)/*` | Final Unit partition | 247 passed; no failed/skipped | 247 | 4 |
| CP-74 | S2-S4 | CP-15 | server-unit-shard-30 | `/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)\|(LandingIdentityControlTests*)\|(LandingRemovalPolicyControlTests*)\|(LogRetentionTests*)\|(OperatorDashboardSessionsTests*)\|(OperatorTokenFileTests*)\|(OrchestratorInstructionsMigrationShapeTests*)\|(PackageVersionPinContractTests*)\|(PostgresPoolConfigurationTests*)\|(RunnerPushCredentialDocsTests*)\|(SessionRunnerGrokLineEndingContractTests*)\|(ShellFixtureLineEndingContractTests*)\|(WorktreeDeleteAccessProbeTests*)\|(WorktreeLockDiagnosticsTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerTests*)/*` | Final Unit partition | 250 passed; no failed/skipped | 250 | 4 |
| CP-75 | S2-S4 | CP-15 | server-unit-shard-31 | `/*/Antiphon.Tests.Infrastructure/WorktreeRemovalDefaultTests/*` | Final Unit partition | 5 passed; no failed/skipped | 5 | 2 |
| CP-76 | S2-S4 | CP-15 | server-unit-shard-32 | `/*/Antiphon.Tests.Infrastructure.Resilience/(HttpReadResilienceTests*)\|(HttpResilienceRegistrationTests*)\|(ResilienceBudgetTests*)\|(ResilienceCircuitTests*)\|(ResilienceOptionsAndTelemetryTests*)\|(ResiliencePredicateTests*)/*` | Final Unit partition | 46 passed; no failed/skipped | 46 | 2 |
| CP-77 | S2-S4 | CP-15 | server-unit-shard-33 | `/*/Antiphon.Tests.Migrations/(CommitOnSettleMigrationTests*)\|(PipelineDefinitionMigrationShapeTests*)/*` | Final Unit partition | 4 passed; no failed/skipped | 4 | 2 |
| CP-78 | S2-S4 | CP-15 | server-unit-shard-34 | `/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)\|(C1364BoundLandWaitProofTests*)\|(DelegateScriptInterruptSwitchTests*)\|(DelegateScriptRunnerSwitchTests*)\|(HostJqPrerequisiteScriptTests*)\|(ProcessSpawnTimeoutPolicyTests*)\|(QualificationCheckoutSourceTests*)\|(RefreshGithubTokenRemoteCommandTests*)\|(RefreshGithubTokenScriptTests*)/*` | Final Unit partition | 67 passed; no failed/skipped | 67 | 2 |
| CP-79 | S2-S4 | CP-15 | server-unit-shard-35 | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/*` | Final Unit partition | 499 passed; no failed/skipped | 499 | 12 |
| CP-80 | S2-S4 | CP-15 | server-unit-shard-36 | `/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)\|(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)\|(ScriptHarnessProcessContractTests*)\|(SeedSmokeTargetingPackContractTests*)\|(SelectorLineEndingAttributeTests*)\|(TestTimingReportTests*)\|(VerifyDevStackSmokeTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | Final Unit partition | 168 passed; no failed/skipped | 168 | 4 |
| CP-81 | S2-S4 | CP-15 | server-unit-shard-37 | `/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)\|(CheckCompactionFixtureTests*)\|(ControlledLandingGitTests*)\|(DelegationHarnessCensusTests*)\|(DelegationTestServicesTests*)\|(DirectoryLinkFixtureTests*)\|(EntityScalarSnapshotConsumerGuardTests*)\|(EntityScalarSnapshotTests*)\|(LiveFixtureTextTests*)\|(LoadTimeoutBoundWitnessTests*)\|(LoadTimeoutWaitTests*)\|(PairedOverheadStatisticsTests*)\|(ScaledTimeProviderTests*)\|(SlowTestTripwireTests*)\|(TestAppHostPathTests*)\|(TestClassificationPolicyTests*)\|(TestDbFixtureLifecycleTests*)\|(TestDbFixtureSchemaCacheLifecycleTests*)\|(TestLaneCategoryGuardTests*)\|(TestPhaseSummaryTests*)/*` | Final Unit partition | 250 passed; no failed/skipped | 250 | 4 |
| CP-82 | S2-S4 | CP-15 | server-unit-shard-38 | `/*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)\|(TestSchemaCacheTests*)\|(TestTimingLifecycleTests*)\|(TestTimingRecorderTests*)\|(WindowsDeliveryFixtureWitnessTests*)/*` | Final Unit partition | 37 passed; no failed/skipped | 37 | 2 |
| CP-83 | S2-S4 | CP-15 | server-unit-shard-39 | `/*/Antiphon.TestSupport/TestClassificationGuardTests/*` | Final Unit partition | 1 passed; no failed/skipped | 1 | 2 |
| CP-84 | S2-S4 | CP-15 | server-unit-method-tag | `/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` | method-only Unit | 8 passed; Category=Unit; no failed/skipped | 8 | 1 |
| CP-85 | S2-S4 | CP-15 | server-integration-CheckCompactionContinuationTests | `/*/*/CheckCompactionContinuationTests/*` | CARD-0079 full regression | 2 passed; no failed/skipped | 2 | 2 |
| CP-86 | S2-S4 | CP-15 | server-integration-CheckCompactionCrashTests | `/*/*/CheckCompactionCrashTests/*` | CARD-0079 full regression | 9 passed; no failed/skipped | 9 | 3 |
| CP-87 | S2-S4 | CP-15 | server-integration-CheckCompactionAttentionTests | `/*/*/CheckCompactionAttentionTests/*` | CARD-0079 full regression | 1 passed; no failed/skipped | 1 | 1 |
| CP-88 | S2-S4 | CP-15 | server-integration-CheckCompactionWorkerLifetimeTests | `/*/*/CheckCompactionWorkerLifetimeTests/*` | CARD-0079 full regression | 2 passed; no failed/skipped | 2 | 1 |
| CP-89 | S2-S4 | CP-15 | server-integration-CheckCompactionReceiptTests | `/*/*/CheckCompactionReceiptTests/*` | CARD-0079 full regression | 27 passed; no failed/skipped | 27 | 5 |
| CP-90 | S2-S4 | CP-15 | server-integration-CheckCompactionAutomaticRestartTests | `/*/*/CheckCompactionAutomaticRestartTests/*` | CARD-0079 full regression | 11 passed; no failed/skipped | 11 | 3 |
| CP-91 | S2-S4 | CP-15 | server-integration-CheckCompactionRecoveryPersistenceTests | `/*/*/CheckCompactionRecoveryPersistenceTests/*` | CARD-0079 full regression | 2 passed; no failed/skipped | 2 | 1 |
| CP-92 | S2-S4 | CP-15 | server-integration-supervision-retention | `/*/*/DataRetentionServiceTests/C1610_Unresolved_supervision_retains_evidence_until_told` | S3 retention safeguards | 6 passed; no failed/skipped | 6 | 2 |

Debug task 40e35f61 adds CP-92 to close the preceding S3 report's explicit
retention coverage gap. Six isolated-schema cases exercise source/recipient
sessions and transcripts, the purpose-linked queue row and the whole task tree.
Each proves unrelated eligible data is pruned while unresolved debt survives,
then proves the same evidence becomes eligible after Told. The existing plan
counts/costs above remain historical; this amendment adds six executions and
two provisional minutes. No existing selection, floor or assertion is removed.

Run each committed After group once with the checkpoint tool and wait until its
exit is not 75; use foreground windows at most 45 seconds and own every child.
Bootstrap the tool through scripts/build-slot.ps1; the executor takes driver
slots itself. Never nest the executor's long run inside a held outer slot. Exit
4 means not run; no unleased or NoSlot fallback receives credit. Preserve red
and green receipts separately. Client rows need fresh, previously absent JSON
paths with the three exact test names; exit 0 or a success string alone is not
a receipt. Keep previous receipts, choose new paths on retry, validate native
exit and total/passed/failed/pending/todo counts.

### Cost and performance execution contract

**91 rows, 5 producer builds, 335 provisional build/execution minutes**, excluding
admission. TUnit floors sum to **8,166 executions** (97 red-phase, 8,069 green/
regression); client adds 3 red and 3 green = **8,172 total**. New cases are 96
TUnit plus 3 client; the renamed janitor case is existing. Final Unit covers
6,272 identities, while guards and S1 classes intentionally repeat on different
source groups. No full assembly run beyond the specified Unit lane is added.

| Committed group | Rows | Estimated execution/build minutes | TUnit floor | Provisional execution cap | Independent hard wall |
|---|---:|---:|---:|---:|---:|
| S1-red | 2 | 7 | 9 | 14 minutes | 44 minutes |
| S1 | 7 | 32 | 294 | 64 minutes | 94 minutes |
| S2-S4-red | 5 | 18 | 88 | 36 minutes | 66 minutes |
| S2-S4 | 77 | 278 | 7775 | 556 minutes | 586 minutes |

These are missing-sample scheduling bounds, not measured p95. The hard wall
allows at most 30 total extra wall minutes for admission/other excluded time;
each acquisition has a separate 15-minute cap. It does not promise all 77 rows
can wait 15 minutes each. Exceeding either wall or execution stops the run with
partial evidence and an incomplete gate, never an automatic budget increase.

R1 requires a **caller-commissioned Debug timing assessment before Code executes
this roster**, because S1, final group and the aggregate exceed 30 minutes. It
uses these exact filters and existing comparable receipts, including the two
largest full integration classes (341/382 cases), then bounds necessary first
runs. This is scheduling/measurement work, not another Plan/TestDesign round.
No new delegation is launched by this Plan delegate. Debug may recommend shared
setup repairs or disjoint method partitions covering the same full classes;
record the change and counts in this plan before running them. No dropped tests.
The 335-minute estimate is materially higher than round 2's 51 because Final
coverage was missing. Authoring provision is 180 minutes, making 515 minutes
before queue waits; the caller must commission within available supervision and
ordering limits, not assume this feature is already active in the host.

### Performance budget details

Apply docs/testing-and-build.md#performance-standards and R1-R9 in
docs/test-performance-standards.md. At this Plan base there are **zero measured
samples** for the new tests or this expanded selection; no measured p95 is
claimed. CARD-1610 owns first-run collection, CARD-1404 cost/timeout refinement,
CARD-1514/1531 phase/admission instrumentation. Consult comparable retained
receipts first; collect per-test p95/sample count/host/platform/load/run/SHA from
the necessary red/green runs, not repeated full sweeps solely for statistics.
Use approximately 2x comparable measured p95; small samples stay provisional.

| Phase/component | Provisional target | Provisional finite bound |
|---|---|---|
| Pure policy body | below 1 second | 2 seconds outside shared discovery |
| DB/service/barrier body | 1-10 seconds | 20 seconds, injected clock for 20/90/240/720-minute thresholds |
| Receipt/script body | 5-30 seconds | 60 seconds including process cleanup |
| Server build | 3-5 minutes | 10-minute target; actual scheduler coupling below |
| Runner build | 1-2 minutes | 4-minute target; actual scheduler coupling below |
| Container/readiness/migrations | 10-40 seconds once per assembly run | 80 seconds; existing tighter external deadlines stay |
| Per-case schema/data | 1-3 seconds | 6 seconds |
| Shared queue/web host | 1-5 seconds | 10 seconds; crash tests recreate scope, not infrastructure |
| New visibility/hold/control row body | 30-180 seconds | 6 minutes |
| New receipt row body | 2-4 minutes | 8 minutes |
| Existing integration and Unit shard row | estimate in CP table | initially 2x that estimate, pending comparable measured p95 |
| Client typecheck/lint/test row | 1-2 minutes | 4 minutes; cold dependency install separately 5 minutes |
| Teardown/output drain | 1-10 seconds | 20 seconds; retain residue on timeout |
| Admission | unmeasured for this campaign | 15 minutes/acquisition plus independent group hard wall |

The importer defaults (45-minute admission, 15-minute row minimum, 90-minute
legacy total and max(15,3x estimate) row bounds) are **not** the execution policy.
For Code, retain the imported YAML as provenance and materialize a derived
ignored manifest with exactly these rows: build.slotWaitMinutes=15, row
timeoutMinutes=max(2,2x EstimatedMinutes), timeouts.rowMinutes=2, and explicit
per-group --total-timeout/--hard-wall-timeout values from the table. The tool
accepts a manifest as a positional argument to run (Program.cs LoadSelection),
so use that derived manifest rather than re-importing away the override.
Record its digest and all overrides in the stored report.

Actual scheduler limitation: a shared build's bound is at least 15 minutes and
the maximum dependent row timeout; there is no independent 10-minute build
knob in this flow. Final's large full-class rows provisionally imply a 50-minute
build ceiling although build target is 5 minutes. This is a disclosed temporary
CARD-1404 exception, not a claimed 10-minute enforced limit. Debug must examine
that coupling and comparable build p95 before execution; split selections with
unchanged coverage or use the existing supported row override only with recorded
justification. Never pretend legacy totalMinutes is consumed by the scheduler.
A row over 30 minutes triggers Debug before repeating; do not fix red by widening
a timeout, loosening assertions or adding retries.

Share PostgreSQL/container/template/migrations once per assembly run; use isolated
schemas and barriers, never global cleanup per test. Share BridgeQueueHarness
startup where configuration is identical, with isolated recipient/decoy sessions.
Crash cases inject the post-save queue failure and reconstruct a scope; a real
process restart is justified and timed only when it is the subject. Tests that
spawn keep the assembly-local ParallelLimiter<ProcessSpawnLimit> and strict
roster membership; new receipt/script classes also join the Slow allowlist.
Any inherited global fixture cannot safely overlap: mark the affected manifest
rows serial during derived-manifest preparation and retain the reason/cost.

Capture admission, build, discovery, container/readiness, migrations, schema
clone/data, host startup, body, teardown and output drain separately. External
DB, filesystem, HTTP, CLI and readiness calls need bounded cancellation and
duration spans. Keep unknown phases null and remainder explicit; wall time is
not the sum of overlapping spans. Preserve exact TRX identities/categories,
counts, original CHECKPOINT lines, native exit and source/build receipts under
ignored .antiphon/c1610-checkpoints/<run>/; validate SHA/source-clean/build binding
before Review claims green. Confirm unexpected inherited red at the base using
only the failing methods. No production runner, provider profile or live Docker
rollout supplies test evidence.

App categories: server supervision/database/queue; offline CLI scripts and
client controls; unchanged session-runner safety. Capture the actual direct
ProjectReference graph and transitive dependents at Code's SHA. Full Unit is
a required ordinary profile lane, not rc qualification. No broker/pty change,
new project extraction, tag, publish or rc qualification credit is claimed.
Delete only owned alternative outputs after joining all children. This Plan
uses only its scratch output root for tooling, obeying the dispatch's deletion
restriction; it deletes nothing elsewhere.

## Round-3 validation and handoff record

Prior round tooling import (21 rows, 5 builds, 51 minutes, 196 TUnit floor) and
review f5c62876 are historical; they do not validate this expanded manifest.
Round-3 read-only source census used Roslyn syntax nodes, including partials and
linked shared guard, then inspected all nine Unit data providers. It found the
C547 filename/class mismatch, a second class in the Overdue file, and two Unit
class-prefix collisions; the exact selections above account for them.

The original runtime observations in D-7 remain dated round-2 evidence, not
fresh claims. Round-3 runner defaults/catalogue were read at 03:57Z. CARD-1609
was read at 04:13Z: InProgress, content-derived retirement gate and attested
manual path. Therefore S4 follows **both CARD-1615 and CARD-1609** on master and
uses separately reviewed rc ports afterward. S1 is independent of those script
changes; preserve CARD-1604 heartbeat semantics before deadline work. No native
Windows rc run is resumed and no deployment is performed by this task.

For final source safety: keep card/handoff/task ordering at successor admission.
Do not acquire a new card lock inside runtime session/task save fencing. The
completion header is a snapshot; later replacement admission refuses automatic
handoff with a decision reason. Uncarded ambiguous lineage is always decide.
Failed report forwarding never calls handoff reservation. Hold/question, 720 cap
or a checkpoint timeout never authorizes a stop. With parking off there is no
automatic release deadline; existing open Cancel/Retry, process exit or separately
authorized release frees custody. CARD-0079 and docker-stack step 5 retain only
their existing narrow authority.

Validation at plan commit 4f8071b6de6dab22abfbd3039ec0c1bdab3dee47:
the gated importer accepted **91 rows**, exit 0. Independent YAML totals: five
producer builds, 335 estimated minutes, 8,166 TUnit floors (plus six client
executions). The Unit shard union is 471 existing classes; the exact method-only
Unit selection has **eight Arguments**, giving 6,272 final Unit identities with
the new policy class. Roslyn source-to-filter audit found zero existing-server
roster mismatches; canonical OR audit found zero unsafe shapes. The two runner
methods retain the independently verified round-2 count of two.

Essential tooling provenance (not product-test receipts):

```text
BUILD SLOT granted lease=82fb74da-3e5d-4ec7-bd52-582399e14894 waited=0s maxcpucount=6
WARNING CP-27: derived row timeout (75 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
WARNING CP-28: derived row timeout (75 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
imported 91 rows -> .antiphon/c1610-r3-counted.yaml
BUILD SLOT released lease=82fb74da-3e5d-4ec7-bd52-582399e14894 held=2s
```

The tool was built once under a granted slot (9 seconds, five warnings in
unchanged tooling, zero build errors), with output entirely in this task's
scratch root. Later imports reused that binary under fresh slots. Product test
execution count in Plan is **zero**; this validates syntax/roster, not behavior.
CP-27/28's default 75-minute advisory is addressed by the explicit performance
assessment/derived-manifest contract above, not silently credited as compliant.
Generated YAML, census and tooling outputs remain ignored.

A fetched origin/master at 98fc3ceb6440a17b68cafe3ecc21c76d190b18ca had a clean
merge-tree check against the plan branch; no merge/rebase was performed. Final
push/source/evidence checks and full published SHA are reported to the caller.
Next is Code on origin/master with the required timing assessment; S1 is
independent, S2/S3 together after the heartbeat prerequisite, S4 after CARD-1615
and CARD-1609. Ordinary Final Review and separately reviewed rc ports remain
required. No product edits, tags, rollout or Windows qualification ran here.


## Debug continuation 698bf6b9: platform accounting and CP-67 diagnosis

The seven mixed rows now state Linux executed floors and exact expected Windows-only
skip counts, following CARD-1614 CP-47. Their filters and total discovered identities
are unchanged: CP-46 215+1=216; CP-52 247+2=249; CP-54 249+1=250;
CP-55 244+1=245; CP-56 224+17=241; CP-62 238+4=242; CP-64 239+2=241.
All 28 cases are intentionally guarded Windows proofs (classification in the Debug
record). None is a Linux behavioral gap. Every original assertion remains intact.
Native Windows execution is outside this commission and remains pending; a Linux
skip is never native execution credit. RowRunner accepts the portable floor and
preserves skipped identities, but ReportValidator currently rejects any skipped
case regardless of ExpectText. Do not label that validator GREEN or waive it.

Before repeating timed-out CP-67, five additional diagnostic rows partition its
25 class operands into five disjoint groups, floors 38+48+40+28+62=216.
No class, method or argument is removed; CP-67's original exact aggregate remains.
The added rows are an explicit unlisted-run amendment for timeout diagnosis, not
extra product coverage. Each retains the inherited six-minute deadline while
collecting opt-in per-case journals. Preserve one isolated producer for the same
committed source and serial scheduling. Review fixture and process/IO phase spans,
unfinished cases and setup cost before choosing any corrected aggregate deadline.
No test timeout or product phase deadline is changed. Missing row-history p95 stays
provisional (CARD-1404/1514/1531); a mixed-case p95 is not a row-history p95.
