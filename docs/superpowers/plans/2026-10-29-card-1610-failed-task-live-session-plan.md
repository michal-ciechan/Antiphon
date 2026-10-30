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
not because Failed was already written. No new client test rows are required:
use the existing attention rendering contract and add optional DTO facts; the
CLI text is covered by its offline script tests. A bespoke client control is
out of scope for this reduced design.

## D-4 detail: Deploy and declared long work

Use `-CeilingMinutes` (nullable positive integer, maximum **720**) and
`-CeilingReason` at dispatch for **every role**, not just Code/Review. Null uses
the configured role ceiling; ExpectedMinutes/-ExpectAbout remains an estimate,
not authority to change supervision. Require a nonblank bounded reason for an
override, snapshot resolved minutes/provenance at dispatch, include them in task
DTOs/brief/status, and reset elapsed/hold acknowledgements on a genuine retry.
Reject zero, negative, overflow and over-cap values before admission; existing
configured zero/off semantics are not silently redefined for old settings.
A retry retains an explicitly requested budget but resolves a default afresh.
Extend replies use the same 720-minute absolute cap. At cap, Continue is still an
explicit decision to proceed under unchanged phase/loop supervision; it is not
a hidden failure timer.

Concrete incident: Deploy **2e4f7375** hit the 240-minute role ceiling while a
sanctioned drain was still running. `DelegationSettings.RolePolicy[Deploy]`
inherits TimeoutMinutes=240; `deploy-server2.ps1` defaults WaitIdleMinutes=480.
D-1 removes its false terminal failure. A declared eight-hour rollout can request
CeilingMinutes=600 with a reason allocating 480 minutes drain plus 120 minutes
for other phases. A total 240-minute drain can request 360 on the same basis.
These are explicit estimates, not global role-default increases or guarantees.

A ceiling override does **not** exempt a 240/480-minute single ToolCall from the
90-minute local-tool deadline. Avoid that second defect by changing the script's
**default WaitIdleMinutes to 30**, retaining its existing named safe timeout
and drain state when time expires. Delegate rollout instructions require named
phases with each command bounded to at most 60 minutes (30 by default), returning
to the agent between calls. While running in a delegated context, reject an
explicit WaitIdleMinutes above 60 before mutating anything and explain how to
split it. Standalone human invocations retain their explicit long-wait range.
No wait expiry authorizes stopping a seat, undeploying a container, clearing a
drain, skipping census, recycling a volume or advancing to the next phase.

The delegate brief records an **absolute overall drain deadline** (e.g. 240 or
480 minutes from the first drain request) and finite 30-minute command slices.
After each named still-busy outcome, inspect status/census and continue only the
same idempotent drain phase within that original deadline; never restart the
overall budget on each call. At the overall deadline report Blocked with actual
state. This is a documented rollout procedure, not a broker wait ledger or a
background process; the delegate owns and awaits every command. The sanctioned
240-minute drain remains available as eight bounded waits. If a transport or
other failure occurs, do not treat it as the named safe still-busy result.

Files: `scripts/delegate.ps1`, `server/Application/Dtos/AgentTaskDtos.cs`,
`server/Domain/Entities/AgentTask.cs`, dispatch/clone/retry/formatter bindings,
API reply/create binding, EF mapping/migration, `scripts/deploy-server2.ps1`,
`docs/docker-stack.md` and `docs/ops-http.md`. Offline tests exercise actual
script entry validation and existing HTTP stubs: default 30, delegated >60
refusal before mutation, explicit standalone 240, still-busy preservation, and
finite overall-deadline guidance. No live Docker rollout is a Plan checkpoint.
CARD-1615 owns the source-writer/landing circular wait; no amount of extra time
fixes it. Do not bypass that hold in this card.

## D-5 detail: no late correction can start a second chain

Delete round-1 S4's failure-episode ledger, Failed report admission, recovery
sweep and Failed-to-Succeeded correction. Failed remains terminal; preserved
transcripts and pushed commits are evidence, not an automatic new outcome.
The failure notice explicitly tells the caller and delegate this. Historical
7ee3b07a and 30f56bd5 require an explicit caller decision about their artifacts;
do not rewrite old failures, infer a land, or replay their `next=review/code`.
Any manual correction/triage note defaults to **next=decide**. It identifies all
newer same-board/card/**role** tasks created after the failure, plus retry or
replacement lineage and matching source branch. An unresolved overlap means
superseded/unresolved, never permission to launch a second chain. For uncarded
work use task-root/branch lineage; unknown lineage requires a decision. This is
a review procedure using existing task reads, not a new automated correction API.

Also guard the narrower real concurrency risk introduced by holds: a previously
held task can finish after its caller separately commissioned a replacement.
Normal settlement may retain its valid report/outcome, but before constructing
the parent completion/handoff check for a newer task on the same board/card/role
since HeldAt (any status, including already completed). Under the ordinary
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
preserving token-count behavior. At this Plan read, GET /api/version reports
`8edda999623a473be2d1970727c172bfb5088d59`, consistent with the brief's rc
activation; CARD-1604 is still Review on the board. Source here already contains
CodexTokenCountAt in TaskDeadlinePolicy. These three facts are distinct.

Land S1 (visibility/janitor) first after ordinary Review, then request its own
reviewed rc port for prompt safety relief. S2/S3 (hold, controls and delivery)
ship together to master after their shared checkpoint group; port together only
after independent design Review, TestDesign, ordinary Code/Review and migration
qualification. S4 (Deploy CLI/docs) follows the CARD-1615 admission resolution
and integration review; do not concurrently edit its rollout/admission area or
use this plan as authority to bypass source-writer holds. Until the coordinated
rc port is active, use existing bounded named-phase waits and explicit operator
handling of ceiling risk. Speedups CARD-1573/1313/1404/1514/1531 remain separate;
this plan does not broaden their code surface or depend on unlanded performance
claims. The caller owns canonical rc activation and source-SHA confirmation.
This task does no tags, publication, deployment, rc rebase or desktop-file reads.

Placement was read from GET /api/runner-defaults and GET /api/session-runners on
2026-10-30 around 03:08Z. The catalogue had an eligible Linux and Windows lane,
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

| Evidence / related card | Verified fact and implication |
|---|---|
| Review 7ee3b07a, CARD-1610 description read this round | 240-minute failure with ToolCall only six minutes old; motivates the Working ceiling hold |
| Astra Plan 30f56bd5, CARD-1604 description read this round | Failed at 20-minute model wait despite continuing token_count; later pushed plan 63036f8e with result/nextStage still absent. Preserve 1604's heartbeat fix; historical report remains explicit triage |
| Astra round-1 Plan c90aa6c9 | Compensation design rejected by review 25f7ce83; this round removes its release/ledger/late-correction machinery |
| Deploy 2e4f7375, fresh delegate -Status read | Still Failed, session now ended at 2026-10-30 02:03:19; retained report describes completed redeploy-old and a requested later drain-temp -WaitIdleMinutes 240. The brief reports its earlier live failure; do not claim it remains live now or that this report proves the earlier failure occurred inside that drain |
| CARD-1604 (Review) | Source heartbeat changes exist here; live API reports rc 8edda999; master/rc sequencing is D-6 |
| CARD-1606 (Backlog) | Supervisor CPU counts its own monitor and masks hangs. Neither monitor ticks, queue polling nor this new notice are useful agent progress |
| CARD-1615 (InProgress; Plan task 7734ac39 in caller brief) | A Shared Deploy task holds a bound land, which blocks redeploy-old. Resolve admission separately; do not relax recycle gates |
| CARD-1573 (Review) | Actual title is output-inventory >64 MiB, with later card text explicitly adding a sharded Unit strategy after long Full runs. It is not solely a Unit speedup card; no unleased workaround receives checkpoint credit |
| CARD-1151 (Review) | Source BootStallDetectAsync records detection without changing task outcome or sending input. Preserve this exact safety precedent |
| CARD-0153 (Done) | TaskProgressPolicy fingerprint/file novelty detects chatty loops; include typed ceiling-held tasks in its active-task selector, keep detection-only behavior |
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
| TaskCeilingHoldTests | Integration | No; no allowlist entry | None: isolated schema and injected runner | 20 |
| TaskCeilingControlTests | Integration | No; no allowlist entry | None: service/transaction barriers, no children | 12 |
| TaskSupervisionReceiptTests | Integration | Slow; add fully qualified name and CARD-1610 reason to slow-tests-allowlist.txt | ParallelLimiter<ProcessSpawnLimit>; add to strict roster | 9 |
| TaskCeilingScriptTests | Integration | Slow; add fully qualified name and CARD-1610 reason to slow-tests-allowlist.txt | ParallelLimiter<ProcessSpawnLimit>; add to strict roster | 8 |

Total new cases **69**, not 69 assertions. Category guards are explicit checkpoint
rows at each green source group: TestLaneCategoryGuardTests (1), shared
TestClassificationGuardTests (1), ProcessSpawnLimitTests (21 expanded cases:
seven singleton methods, six CPU arguments, eight override arguments) = **23**.
No runner source or new runner test class remains; its unchanged two-method safety
selection has a separate project row, not another new-class census obligation.
Changing the classification during TestDesign requires revising this table,
allowlist and count floor together. Any fixture that begins spawning must gain
the assembly-local limiter and strict-roster membership before running.

Client command rows from round 1 are removed with the bespoke client feature.
There is therefore no uncounted Vitest obligation. If TestDesign establishes a
necessary client behavior change, add a named command checkpoint with exact
Vitest JSON `numTotalTests`/passed/failed floors and source binding before Code;
a success string alone is not a test-count floor.
