# CARD-1610: Hold a live task at its role ceiling

Date: 2026-10-30. Round 2 Plan, replacing the round-1 design in place.
Source base: `93a21a1fac2ab3965cce5a48503d54fb71132c07`.
Status: early design skeleton; detailed verification and source census follow in
this same task. No product changes are authorized by this Plan dispatch.

## Decisions

- **D-1 — Adopt a non-failing ceiling hold for all roles, including Deploy.**
  A Working session at its role ceiling remains alive; its task becomes Blocked
  with a typed ceiling question. Cancel, continue/extend via Reply, and ordinary
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
  Evaluate bounded declared ceiling extensions and bounded drain polling without
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
  CARD-1573 owns sharded Unit speedups, CARD-1151 boot detection, CARD-1615 the
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
| Blocked automatically keeps phase/report processing | Deadline/reply selectors currently accept Dispatched/Working | Explicitly admit typed ceiling holds; do not widen all Blocked tasks |
| Deploy fits its ceiling | Default drain wait 480 exceeds ceiling 240 | All-role hold plus declared budget and bounded command waits |

## Slices

1. Deadline hold, typed question, continue/extend, settlement eligibility and durable
   caller receipt. Files: TaskDeadlinePolicy, AgentTaskDispatcher, AgentTaskService,
   AgentTaskReplyService and their existing runtime-action consumers; tests to follow.
2. Failed+live notice and visibility, janitor detection-only handling. Files:
   failure notification/queue, AttentionService, pool janitor; tests to follow.
3. Deploy budget/drain integration and operator documentation. Files:
   delegate.ps1, deploy-server2.ps1, DTO/model/configuration, relevant owner docs.
4. Exact checkpoint manifest and delivery/custody regressions; guards and counts.

## Performance budget

Apply docs/testing-and-build.md#performance-standards and
`docs/test-performance-standards.md`. No product tests run in this Plan stage;
checkpoint import is the requested tooling validation. Detailed provisional costs
and exact filters follow. Reuse one producer build per unchanged committed slice;
share database/container/migrations once per run and isolate tests with schemas.
Record admission, build, startup, migration, body, teardown and external-call time
separately. Missing samples are not measured p95; TestDesign must collect bounded
samples and set timeouts near 2x measured p95. Any run expected over 30 minutes
requires Debug before repetition. Import defaults are 45/15/90 minutes for
slot/row/total, not the round-1 plan's claimed limits.

## Sessions waiting for input

A ceiling hold keeps the Working session and its seat. No automatic release
exists while parking is disabled (CARD-1083). Explicit Cancel stops an open task;
Retry stops the old attempt; a normal exit or qualified existing release can
release it. Parking must never treat a Working ceiling hold as an idle question.

## Handoff

This round returns to independent design Review; TestDesign remains separate
before Code. The next revisions in this task replace this skeleton with the
source-backed contract, per-slice tests and imported checkpoint table.

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
ceiling breach commits Blocked, a typed Ceiling block reason, and one question:
"This task has run N minutes against its C-minute ceiling. Its session is still
working. Continue under phase supervision, cancel, or extend the ceiling by M?"
No CompletedAt, failure event, report, stage outcome, workspace-consumer release,
pool-idle mark or completion settlement is written. Session input is unnecessary
for this control-plane transition. Parent notification says `next=decide` and
explicitly says the delegate continues. Do not emit a terminal Failed header.

Persist nullable attempt-scoped hold fields on AgentTask (episode ID, held-at,
accepted session StartedAt, acknowledged-at), plus the resolved ceiling and
source. Add BlockedKind.Ceiling (append enum values) for classification/UI; do not
encode eligibility by searching FailureReason prose. Defaults/nulls preserve old
rows. Hold identity includes task, attempt, session, generation and dispatch;
existing optimistic concurrency and session/task fence reject stale decisions.
Generate EF migrations with the CLI during Code. No separate wait-ledger entity.

A shared expression/predicate, **Dispatched or Working or current typed ceiling
hold**, must be used at each affected consumer: overdue sweep and its post-pull
check; AgentTaskReplyService.OnTurnEndLockedAsync/OnTranscriptActionAsync;
AgentSessionRuntime catch-up and AdmitActionsAsync; SessionRuntimeActionCoordinator
RequestAsync; and the runtime-action save fence. Audit AppDbContext's task fence
and the dead-session/unsafe-refusal paths too. Existing task/session/generation
fences remain mandatory. Ordinary question/quota/routing Blocked tasks are not
made settle-eligible. A complete report already admitted while Working remains
attributed after the hold and uses normal settlement (including commit/push and
review evidence requirements). A done/blocked/failed report resolves the hold
atomically with its ordinary outcome; the same report cannot settle twice.

Reply gets an explicit typed ceiling action, exposed by delegate.ps1 and the
existing reply endpoint: Continue or Extend with positive integral minutes.
Cancel uses the existing Cancel operation. Continue marks this ceiling episode
acknowledged and returns Working, suppressing further *role-ceiling* holds for
this attempt; phase and loop supervision continue without an end-time waiver.
Extend sets the absolute ceiling to `max(elapsed minutes, current ceiling)+M`,
limited by D-4's cap, clears the active hold and rearms at that new ceiling.
Do not parse arbitrary prose as a number. Reject a stale episode/attempt/round
or unsupported action with 409/validation before changes. Invalid answers leave
the question open. Reply on other Blocked kinds keeps its existing behavior.

For this special reply, **do not update RepliedAt/RepliedAtSequence, queue a new
prompt, clear pending report attribution or create a new task attempt**. The
original turn is still running; existing Reply's watermark change would discard
its eventual report. Record the control decision as an event and publish normal
task invalidation. Cancel/Retry retire the hold with the old attempt; their stop
semantics remain unchanged. Persisted decisions survive restart without repeated
hold notes. Exclude Ceiling from standing-authority auto-continue, question
parking, follow-up reuse and generic question auto-resume. It continues to own
its seat, workspace reservation and concurrency occupancy.

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

Implementation census must include `FailAsync`, `FailAndNotifyAsync`, direct
Failed writes in AgentTaskDispatcher, AgentTaskReplyService settlement,
ApiErrorRecoveryService, SpecialistTaskRunner and task-related landing recovery.
Do not classify reads as writers. Any writer that cannot prove exit or no
session owes the notice; a test guards each actual writer reached by this change.
No existing automatic kill gains authority from this contract; CARD-0079 stays
its narrowly qualified exception.

Wire text states task ID, attempt, why Failed, and: "Your task is Failed. Stop
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
