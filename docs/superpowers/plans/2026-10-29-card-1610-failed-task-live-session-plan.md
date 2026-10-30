# CARD-1610: Hold a live task at its role ceiling

Date: 2026-10-30. Round 3 Plan, revising the round-2 design in place.
Source base: `93a21a1fac2ab3965cce5a48503d54fb71132c07`.
Status: round-3 revision in progress; verification design is folded into this final Plan round.
Implementation handoff will be Code on origin/master after the stated ordering gates.
No product changes are authorized by this Plan dispatch. The initial skeleton
and each defect response were committed and pushed during this round.

## Round-3 resolution checklist

This is the pushed skeleton; each item is resolved in place before handoff.

- R3-1: retain Working with typed hold fields; inspect Reply and all four occupancy/Check selectors.
- R3-2: Continue re-arms through 720 minutes; final hold requires explicit operator Extend.
- R3-3: preserve docker-stack step-5 stop authority; use ANTIPHON_TASK_TOKEN and foreground-safe resumable slices.
- R3-4: forward Failed late reports as durable parent triage notes without settlement or handoff.
- R3-5: include full affected classes and sharded Unit lane; recount and re-import.

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
| Blocked automatically keeps phase/report processing | Deadline/reply selectors currently accept Dispatched/Working | Explicitly admit typed ceiling holds; do not widen all Blocked tasks |
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
before Code. The completed artifact includes the source-backed contract,
per-slice tests and checkpoint table. This round does not authorize implementation.

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
not because Failed was already written. Use the existing attention rendering
contract and add optional DTO facts; the CLI text has offline script tests.
The existing BlockedQuestionCard needs a small explicit Ceiling branch: its
fallback otherwise sends an ordinary answer or offers Retry. Add Continue,
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
A retry retains an explicitly requested budget but resolves a default afresh.
Extend replies use the same 720-minute absolute cap. At cap, Continue is still an
explicit decision to proceed under unchanged phase/loop supervision; it is not
a hidden failure timer.

The brief names Deploy **2e4f7375** as a live ceiling incident involving sanctioned
rollout waiting; the fresh status/report observation is distinguished in D-7.
`DelegationSettings.RolePolicy[Deploy]`
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
explicit WaitIdleMinutes above 60 or Phase=all before mutating anything and
explain how to split it. Standalone human invocations retain their explicit
long-wait range and all-phase command.
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

Replace round-1's client visibility tests with three focused cases in
client/src/features/delegations/BlockedCeilingHold.test.tsx: Continue sends a
typed decision without ordinary reply; Extend validates/sends minutes and stale
episode refusal; Cancel uses the existing route. Each also asserts no generic
Retry/auto-continue affordance. CP-20/21 require a fresh Vitest JSON receipt with
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
old instruction reaches the replacement/finished turn. All nine cases assert
zero automatic stopper/release calls and preserve foreign sessions. Own and
join harness children; do not use any production runner or provider profile.

There is no correction producer after D-5, so a correction-specific witness is
removed rather than pretended covered by C1161. Retain the existing C1161 Failed
**parent** receipt method (three cases) and C1259 accepted-attempt replacement
fence (two cases) as independent regression checkpoints.

## D-10 detail: pooled Working detection without reclamation

`RetireIdleWarmAgentsAsync` currently protects Working sessions only when their
last transcript is newer than the TTL cutoff, then logs "reads mid-turn but has
been silent" and calls KillPooledSessionAsync. Both TTL and cap eviction must
exclude **all Working and Unknown** sessions, regardless of transcript age.
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
incident for the same episode. No fresh runner sample means Unknown, not cleared.
Use the existing bounded pool sweep and seat sampler; no per-request runner fanout.
A task-bound Failed session uses FailedTaskSessionHeld, suppressing the duplicate
pool warning. A genuinely idle warm session retains existing TTL/cap retirement.
Resolution requires proved idle/end or a new open owner; retain historical audit.

Replace, do not delete, the contradictory existing test
`AgentTaskPoolTests.the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl`
with `C1610_Janitor_retains_working_after_full_ttl`, which fails on the old kill.
The new visibility tests cover TTL and cap pressure, taskless incident creation,
restart dedup, Unknown retention, failed-session dedup, idle recovery and normal
idle retirement. Assert no stopper call, no row/worktree deletion and retained
capacity for Working/Unknown. A stall is reported for a decision, never killed.

## Revised slices and concrete file ownership

| Slice | Product and documentation files | Test files / ordinary checkpoints |
|---|---|---|
| S1 visibility and janitor safety | AgentTaskDispatcher.cs pool retirement; AttentionService.Seats.cs/Leaks.cs, shared FailedTaskSessionFacts.cs; AgentTaskDtos.cs and client/src/api/agentTasks.ts type mirror; AttentionKind/AgentIncidentKind and client/src/api/attention.ts kind mirror; docs/session-runtime-invariants.md | Application/TaskSupervisionVisibilityTests.cs (8); changed AgentTaskPoolTests method (1); S1 red/green plus existing failed-pool regression and guards. CLI status waits for S4's script tests |
| S2 ceiling policy and controls | TaskDeadlinePolicy.cs; new AgentTaskDispatcher.CeilingHold.cs; AgentTaskDispatcher.cs overdue/stall/dead-session/refusal selectors; AgentTaskService.cs; AgentTaskReplyService.cs; BlockedContextBuilder.cs; AgentTask.cs; AgentTaskDtos.cs; AppDbContext.cs mapping/fence and generated migration; AgentSessionRuntime.cs; SessionRuntimeActionCoordinator.cs; TaskOwnerGuard is inspected, unchanged (already accepts Blocked) | Application/TaskCeilingPolicyTests.cs (12), TaskCeilingHoldTests.cs (20), TaskCeilingControlTests.cs (12); policy heartbeat and runtime-action regressions |
| S3 receipts and successor fencing | Failure writers listed in D-2; SessionQueuedMessage identity/mapping, SessionMessageQueueService.cs, CompletionNoteWork.cs and hosted recovery; DelegationReportFormatter.cs, TaskCompletionNotification.cs; PipelineHandoffActionService.cs and AgentTaskService child admission; docs/agent-card-lifecycle.md, docs/session-runtime-invariants.md | Application/TaskSupervisionReceiptTests.cs (9); C1161 (3); held successor-race tests in Control class |
| S4 declared Deploy budget and docs | scripts/delegate.ps1 parameter/create/Reply/Status branches; scripts/deploy-server2.ps1; DTO/create/reply binding; client/src/api/agentTasks.ts and features/delegations/BlockedQuestionCard.tsx; docs/docker-stack.md, docs/ops-http.md | Scripts/TaskCeilingScriptTests.cs (8); client/src/features/delegations/BlockedCeilingHold.test.tsx (3); classification/limiter guards; slow-tests-allowlist.txt and strict roster |

Paths without a directory in this table are under server/Application/Services
unless their explicit layer is named above. scripts/delegate.ps1 owns these
parameter and switch branches; no duplicate parameter parser. Add types at their owning
layer. Domain remains infrastructure-free. No build-slot broker, runner release
protocol or raw killing API is added. Existing generic attention
presentation consumes the new facts; preserve nullable DTO compatibility.

Commit failing behavioral witnesses before fixes. S1 is a small independently
reviewable safety slice. S2-S4 are one **test/build group**, committed as meaningful
source slices before the green run; their mutually dependent hold/report behavior
must not be activated piecemeal. Share one isolated build for all rows at each
unchanged committed group, never across red versus green source. No source edits
while a run is in flight. TestDesign owns the detailed guard/positive-control
inventory before Code; this Plan's ordinary roster below is not executed evidence.

## Ordinary verification roster (for subsequent TestDesign)

New names below are design commitments, not existing tests or passing results.
Each Arguments value becomes one executed result. All tests call real production
policy/services/scripts through existing seams; no test that compares its own
fixture constants counts. TestDesign must identify the intended failing assertion
and method-scoped positive control for each behavior before Code.

| IDs | Class and exact planned methods | Cases / required observations |
|---|---|---|
| V-1 | TaskSupervisionVisibilityTests.C1610_Visibility_and_pool_safety | 8 Arguments: failed-fresh, failed-unknown, taskless-ttl, taskless-cap, real-idle, restart-resolution, failed-dedup, source-custody; D-3/D-10 invariants. S3 adds notice-debt fields later, so S1 does not depend on that producer |
| V-2 | TaskCeilingPolicyTests.C1610_Declared_budget_validation; C1610_Phase_dispatch_guards | 8 budget inputs (null,1,720,0,-1,721,overflow,reasonless) + 4 guard inputs (phase wins, boot, heartbeat, unknown); 12 |
| V-3 | TaskCeilingHoldTests.C1610_Working_ceiling_holds | Review, Code, Deploy, Plan; 4 |
| V-4 | TaskCeilingHoldTests.C1610_Phase_beats_ceiling; C1610_Held_task_keeps_phase_supervision | ModelWait and LocalExecution each; 4 |
| V-5 | TaskCeilingHoldTests.C1610_Normal_report_settles_hold | done, blocked, failed; 3 |
| V-6 | TaskCeilingHoldTests.C1610_Hold_race_preserves_identity; C1610_Hold_preserves_custody_and_restart_identity; C1610_Non_ceiling_blocks_remain_excluded | report/retry/cancel/generation (4), custody/restart (2), question/quota/routing (3); 9; Hold class total 20 |
| V-7 | TaskCeilingControlTests.C1610_Continue_keeps_report_floor; C1610_Extend_validates_and_rearms; C1610_Stale_answer_is_refused; C1610_Answer_races_report_without_reprompt | 1 + 3 (valid/cap/invalid) + 2 (episode/attempt) + 1 = 7 |
| V-8 | TaskCeilingControlTests.C1610_Successor_fence_uses_card_and_role; C1610_Replacement_racing_handoff_is_held; C1610_Retry_resets_hold_but_preserves_requested_ceiling | 3 (same-role/different-role/no-card) + 1 + 1 = 5; Control total 12 |
| V-9 | TaskSupervisionReceiptTests.C1610_Hold_parent_has_complete_UserPrompt; C1610_Failed_delegate_has_complete_UserPrompt; C1610_Stale_notice_is_not_delivered | D-9's three named arguments per method; 9 |
| V-10 | TaskCeilingScriptTests.C1610_Status; C1610_Declared_budget; C1610_Ceiling_reply; C1610_Drain_wait | 2 (fresh/unknown), 2 (valid/invalid input sets), 1 (both typed actions), 3 (default/delegated refusal/explicit standalone); 8. Default drain arm proves still-busy retains drain state; delegated arm rejects Phase=all and waits >60 before side effects |
| R-1 | AgentTaskPoolTests.C1610_Janitor_retains_working_after_full_ttl | 1 changed existing witness; old line kills and must go red |
| R-2 | AgentTaskOverdueDeadlineTests.an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives | 1; existing idle TurnEnd ceiling-failure fixture remains valid under D-1; do not relabel it a phase test |
| R-3 | TaskDeadlinePolicyTests.a_token_count_does_not_reset_the_role_ceiling; a_token_count_does_not_reset_the_local_tool_clock | 2; unchanged heartbeat clock ownership |
| R-4 | DelegationDispatchRecoveryBoundaryTests.C1161_Failed_caller_note_has_one_complete_user_prompt | 3; eligible/busy/crash, original parent failure note |
| R-5 | SessionRuntimeActionRecoveryTests.C1259_admitted_task_attempt_cannot_settle_its_replacement | 2; catchup false/true |
| R-6 | TerminalSeatReleaseTests.Working_remains_protected_after_arbitrary_silence; Explicit_operator_release_keeps_its_contract | 2; unchanged runner safety boundary |
| R-7 | TestLaneCategoryGuardTests; TestClassificationGuardTests; ProcessSpawnLimitTests | 23 executed results per green group; include new process classes in strict roster |
| V-11 | BlockedCeilingHold.test.tsx | 3 Vitest cases: Continue, Extend, Cancel; D-8 defines exact receipt floors |

No new runner capability or live provider test is needed. Existing
TaskOwnerGuard accepts Blocked with a live bound session, so a hold does not end
an in-flight checkpoint run; TestDesign retains that source check and the existing
ownership harness when choosing its detailed controls. Suffix-collision census
must precede execution: each wildcard class/method token must name only its
listed roster, partial declarations are one class, nested helpers with no Test
attribute add no cases. Inspect fresh TRX results; --list-tests is not evidence.

### Checkpoints

Lanes: all `server-*` rows are portable **server Unit/Integration** as identified
by Group; `scripts-*` are portable **script Integration** (offline pwsh stubs);
`runner-*` is portable **session-runner Integration**; `client-*` is portable
**client/Vitest**. No host pin and no OS flag
is needed. Unit and Integration cases are not mixed in one filter invocation.
Same After group shares a build; each distinct source group owns fresh output.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1610-visibility-red/` | server-visibility-red | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 | 8 executed; intended behavior assertion reds retained | 8 | 6 |
| CP-2 | S1-red | CP-1 | server-janitor-red | `/*/*/AgentTaskPoolTests/C1610_Janitor_retains_working_after_full_ttl` | R-1 | 1 executed; old kill causes intended assertion red | 1 | 1 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1610-visibility/` | server-visibility | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 | 8 passed, 0 failed/skipped | 8 | 6 |
| CP-4 | S1 | CP-3 | server-janitor | `/*/*/AgentTaskPoolTests/C1610_Janitor_retains_working_after_full_ttl` | R-1 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-5 | S1 | CP-3 | server-failed-pool | `/*/*/AgentTaskOverdueDeadlineTests/an_overdue_pool_delegate_keeps_its_row_idle_while_the_session_lives` | R-2 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-6 | S1 | CP-3 | server-unit-guards-s1 | `/*/*/(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-7 | 23 passed, 0 failed/skipped | 23 | 1 |
| CP-7 | S2-S4-red | `tests/Antiphon.Tests -> bin-c1610-hold-red/` | server-unit-policy-red | `/*/*/TaskCeilingPolicyTests/C1610_*` | V-2 | 12 executed; intended budget/disposition reds retained | 12 | 5 |
| CP-8 | S2-S4-red | CP-7 | server-hold-control-red | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/C1610_*` | V-3..V-8 | 32 executed; intended hold/control assertion reds retained | 32 | 2 |
| CP-9 | S2-S4-red | CP-7 | server-receipts-red | `/*/*/TaskSupervisionReceiptTests/C1610_*` | V-9 | 9 executed; intended missing-receipt reds retained | 9 | 3 |
| CP-10 | S2-S4-red | CP-7 | scripts-hold-deploy-red | `/*/*/TaskCeilingScriptTests/C1610_*` | V-10 | 8 executed; intended wire/wait assertion reds retained | 8 | 2 |
| CP-11 | S2-S4 | `tests/Antiphon.Tests -> bin-c1610-hold/` | server-unit-policy | `/*/*/TaskCeilingPolicyTests/C1610_*` | V-2 | 12 passed, 0 failed/skipped | 12 | 5 |
| CP-12 | S2-S4 | CP-11 | server-hold-control | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/C1610_*` | V-3..V-8 | 32 passed, 0 failed/skipped | 32 | 2 |
| CP-13 | S2-S4 | CP-11 | server-receipts | `/*/*/TaskSupervisionReceiptTests/C1610_*` | V-9 | 9 passed, 0 failed/skipped | 9 | 3 |
| CP-14 | S2-S4 | CP-11 | scripts-hold-deploy | `/*/*/TaskCeilingScriptTests/C1610_*` | V-10 | 8 passed, 0 failed/skipped | 8 | 2 |
| CP-15 | S2-S4 | CP-11 | server-heartbeat-regression | `/*/*/TaskDeadlinePolicyTests/(a_token_count_does_not_reset_the_role_ceiling*)\|(a_token_count_does_not_reset_the_local_tool_clock*)` | R-3 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-16 | S2-S4 | CP-11 | server-parent-receipt-regression | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Failed_caller_note_has_one_complete_user_prompt` | R-4 | 3 passed, 0 failed/skipped | 3 | 1 |
| CP-17 | S2-S4 | CP-11 | server-attempt-regression | `/*/*/SessionRuntimeActionRecoveryTests/C1259_admitted_task_attempt_cannot_settle_its_replacement` | R-5 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-18 | S2-S4 | CP-11 | server-unit-guards-hold | `/*/*/(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-7 | 23 passed, 0 failed/skipped | 23 | 1 |
| CP-19 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1610-runner-safety/` | runner-safety-regression | `/*/*/TerminalSeatReleaseTests/(Working_remains_protected_after_arbitrary_silence*)\|(Explicit_operator_release_keeps_its_contract*)` | R-6 | 2 passed, 0 failed/skipped | 2 | 3 |
| CP-20 | S2-S4-red | n/a | client-ceiling-red | `pwsh -NoProfile -File scripts/test-client.ps1 BlockedCeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-client-red.json` | V-11 | JSON total=3; all 3 names; intended control assertion red, not typecheck/setup failure | n/a | 2 |
| CP-21 | S2-S4 | n/a | client-ceiling | `pwsh -NoProfile -File scripts/test-client.ps1 BlockedCeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-client-green.json` | V-11 | JSON total=3, passed=3, failed/pending/todo=0; exact names; exit 0 | n/a | 2 |

Run each committed After group once through the checkpoint tool. CP-19 belongs
to S1 even though printed last; run server and runner projects sequentially.
Bootstrap the tool through scripts/build-slot.ps1; the checkpoint executor takes
its own driver slots. Await run/wait until exit is not 75, using foreground
windows no longer than 45 seconds. Exit 4 is not run, never permission to bypass
the gate. Preserve red and green receipts independently; compile/fixture errors
and zero tests do not demonstrate an intended red. Compatible contract/refactor
scaffolding may precede the tests-first commit, but must retain old behavior so
red witnesses target actual missing behavior, not missing types or methods.

### Cost

**21 rows, 5 producer builds, 51 estimated execution/build minutes**, excluding
admission. Planned TUnit floors sum to **196 executions**: 70 red-witness
executions and 126 green/regression executions. Add 3 red and 3 green Vitest
executions = **202 total**. Guards repeat at two different source groups.
Unique new cases: 69 TUnit + 3 Vitest; changed janitor case: 1.
No product tests have run in Plan. These estimates require TestDesign refinement.
Code authoring allowance is provisionally 180 minutes plus 51 ordinary minutes;
post-land mutation cost is not claimed zero and remains for TestDesign's exact
method/variant inventory. Independent plan Review returns to TestDesign before
Code. No broad Unit/full-suite rerun is authorized by this targeted plan.

## Performance budget details

Apply [performance standards](../../testing-and-build.md#performance-standards)
and [R1-R9](../../test-performance-standards.md). No measured p95 exists for the
proposed tests: sample size **0**, source base named above, Linux Plan, no product
run or load sample. Do not label estimates as measured. CARD-1610 owns provisional
collection, with CARD-1404 for cost packing and CARD-1514/1531 for phase/admission
instrumentation. TestDesign first consults comparable retained receipts; Code's
first necessary red and green runs collect per-test timings without adding broad
repetitions solely to manufacture a percentile. Replace provisional limits with
about **2x comparable measured p95**, identifying SHA, host, platform, load,
category, sample count and run IDs. Small samples remain provisional.

| Component | Provisional expected duration | Finite provisional execution bound |
|---|---|---|
| Pure test body | <1 second | 2 seconds, excluding shared discovery/setup |
| DB/service/barrier body | 1-10 seconds | 20 seconds, no wall-clock minute sleeps |
| Script/receipt body | 5-30 seconds | 60 seconds; process cleanup included |
| One server build | 3-5 minutes | 10 minutes; restore/compile separated |
| One runner build | 1-2 minutes | 4 minutes |
| Shared container/readiness/migrations | 10-40 seconds once per assembly/run | 80 seconds provisional; retain existing tighter call limits |
| Schema clone/data prep | 1-3 seconds per scenario | 6 seconds |
| Shared queue/web host setup | 1-5 seconds | 10 seconds; restart tests recreate scopes only unless process restart is the subject |
| Unit/guard row body | <30 seconds | 1 minute |
| Visibility/hold/control row body | 30-120 seconds | 4 minutes |
| Receipt row body | 1-3 minutes | 6 minutes |
| Script row body | 30-90 seconds | 3 minutes |
| Runner safety row body | 10-60 seconds | 2 minutes |
| Teardown/output drain | 1-10 seconds | 20 seconds; timeout retains residue/custody evidence |
| Admission | Unknown; card reports historical 10-25 minute waits | 15 minutes per acquisition as a provisional scheduling cap, not measured p95 |

Expected complete group costs from the table: S1-red 7 minutes; S1 green including
runner safety 12; S2-S4-red 14; S2-S4-green 18. Total **51**, versus round 1's 80.
Five builds instead of twelve; four are Antiphon.Tests, one SessionRunner.Tests.
No source-stamp reuse across different commits, and no repeat setup per case.
Because the cumulative campaign exceeds 30 minutes, **caller commissions a
scoped Debug timing assessment before Code** under R1, using this exact roster
and available build/fixture receipts. This delegate cannot sub-delegate. The
Debug task must account for aggregate builds and queue waits, not pretend that
splitting groups removes the obligation. A single group observed above 30
minutes also requires its own timing investigation before repetition.

Importer facts, not performance policy: generated defaults are
`build.slotWaitMinutes=45`, `timeouts.rowMinutes=15`, `timeouts.totalMinutes=90`;
individual `timeoutMinutes` use max(15, 3x EstimatedMinutes). The import validation
below checks syntax/counts only and **does not claim** the old 15/29 limits are
active. Before Code, TestDesign must commit a derived manifest with explicit
per-row execution bounds from this table/measurements, a 10-minute server build
bound, 15-minute slot cap, separate charged group execution caps (14/24/28/36
minutes respectively, provisional ~2x group estimates), and a finite hard wall
that includes each possible admission. Tool global/row/build knobs must be
checked against their actual consumer; if one knob couples build and row time,
record the measured justified exception instead of claiming independent limits.
Keep the imported default manifest as historical provenance; the derived manifest
is a new artifact with exact selection and all deviations visible to Review.
A hard wall over 30 minutes is an allowance, not measured execution; record
admission separately and carry the aggregate Debug decision. No unlisted run or
silent timeout widening is permitted to resolve a refused/slow row.

Share PostgreSQL container and template/migrations once per assembly/run. New DB
tests use isolated schemas and transaction/barrier state, not whole-database
cleanup or parallel global sweeps. Share BridgeQueueHarness startup where
configuration is identical; distinct recipient/decoy sessions are isolated, not
a new container per test. A receipt crash is a service/queue failure injection;
only a test explicitly proving process death may recreate the process and must
measure that cost. Clock advance simulates 20/90/240-minute thresholds. External
DB, filesystem, HTTP, CLI, readiness and output-drain calls each carry bounded
cancellation and duration spans. No production runner/AppHost/browser is used.

Evidence lives in ignored `.antiphon/c1610-checkpoints/` per run/source group:
TRX, native exit, exact case/category membership, source/build binding, host/load,
admission/build/discovery/setup/body/teardown timings and external-call spans.
Unknown phases remain unknown, remainder is explicit, wall time is separate
from summed parallel spans. Preserve failed receipts and unexpected-red baseline
checks at their exact methods. Record actual versus expected counts per CP.

App ownership: server supervision/queue/database -> server application batch;
script CLI -> server API contract plus offline script integration; runner safety
-> shared session-runner batch. Retain the actual ProjectReference graph and
transitive dependents at the Code SHA. No broker/pty-backend behavior changes,
no new project extraction, and no Full rc qualification credit is claimed; scoped
release qualification remains CARD-1545. Cleanup removes only this run's owned
alternate outputs after all children finish, never foreign build/evidence roots.

## Final source audit and validation record

Further direct source inspection found that BlockedQuestionCard's fallback would
send an ordinary answer (or offer Retry) for an unknown Ceiling kind. D-3/D-8
therefore include a small typed-decision branch and three receipt-counted client
cases; removing all client coverage would have been unsound. Client rows use
`test-client.ps1`'s actual -JsonResultPath option; before running, require the
receipt path absent, capture native exit, then validate JSON counts/names. For
reruns retain the old receipt in its run's evidence directory and supply a fresh
path in the refined manifest. A missing/truncated/stale JSON or a typecheck
failure cannot count as three tests. Typecheck/lint and npm install are included
in this driver's phases; dependencies install once per checkout, not per case.
Provisional client body target is 15-30 seconds / 60-second limit; the 2-minute
row estimate includes typecheck/lint. Record cold install separately (5-minute
provisional cap) and refine from actual p95 in TestDesign.

Additional source-backed boundaries: `TaskOwnerGuard.cs:186` already admits
Blocked tasks with a live session; no checkpoint-tool change is needed.
`PipelineHandoffActionService` and `AgentTaskService` child admission already
start with the card lock. Recheck D-5 superseders there in **card, handoff, task**
order. Do not add a card lock inside the runtime's **session, task** save fence.
The completion header is a snapshot; a replacement arriving later makes child
admission refuse with a decision reason. It does not rewrite immutable report
text or justify a new global lock-order project. Uncarded ambiguous lineage
always produces next=decide rather than inventing a distributed admission lock.

Fresh Deploy event read: task 2e4f7375-076d-44e4-9ae2-263d08e8a59d was dispatched
2026-10-29 20:01:26Z and failed 2026-10-30 00:01:37Z. Its Failed event names the
240-minute Deploy ceiling and a ToolCall **7 minutes old**, explicitly saying
the session was not killed. This directly confirms the healthy-phase ceiling
split. It does not identify that ToolCall as drain-temp; the retained report
listed that phase as forthcoming. The session ended later at 02:03:19Z.

Plan validation history: at `2db13eac06c53073c6810ba7ad745657a83ce6fc`, the gated
checkpoint importer successfully imported the then-19-row table (five producer
builds, 47 estimated minutes, 196 TUnit floor). Slot granted immediately; held
8 seconds. Five compiler warnings in unchanged tooling, zero build/import errors.
This was tooling validation, not product-test execution. The client correction
adds two rows/four estimated minutes; final re-import evidence follows below.
The six new C# class names have no pre-existing declarations/suffix collisions;
the three guard names each map to one test class in Antiphon.Tests plus its
shared linked guard source. The existing two token-count methods and two runner
methods are distinct; this checkout has one TerminalSeatReleaseTests declaration
in the runner test project. TestDesign must recheck against its actual base and
Code must verify fresh TRX rosters, not infer discovery from this source census.

For typed ceiling Reply, `ceilingAction`, `episodeId` and optional `extendMinutes`
form an exclusive request shape; ordinary message text is not required for that
shape and is not passed through the existing message.Trim/queue path. Existing
ordinary Reply requests remain compatible. Unsupported mixed shapes are rejected
before mutation. The CLI and client share this contract; neither sends prose
that the server must guess is a ceiling instruction.

A ceiling hold is not a participant waiting at an idle prompt: it keeps running
while the parent decides. A subsequent genuine blocked report may wait for input.
With parking disabled neither has an automatic release deadline. Explicit open
Cancel/Retry, normal process exit, or an independently authorized existing release
is what can free the session; a note, 15-minute admission cap or 720-minute
ceiling cap never becomes stop authority. With parking enabled, a Working hold
is still excluded; only a separately qualified actual idle question may park.

Final importer validation at `ab23f3967bdcf2564df625be7299bee14eec1627`:
**21 rows imported, exit 0**, using the already-built tool under a granted host
slot (0-second wait, 2-second lease). Generated ignored manifest:
`.antiphon/c1610-plan-round2-final.yaml`. Independent totals read from that YAML:
5 builds, EstimatedMinutes sum 51, MinExecuted sum 196; six additional Vitest
executions have their explicit JSON floors. Actual imported defaults are still
45/15/90, with CP-1/3 individual timeouts 18 minutes and remaining TUnit rows 15;
these are validation output, not the refined execution budget promised above.
Both imports were foreground and joined. No product tests ran; product behavior
and all ordinary/PC acceptance remain unverified until subsequent stages.

The evidence-history guard over base `93a21a1fac2ab3965cce5a48503d54fb71132c07`
to `ab23f3967bdcf2564df625be7299bee14eec1627` found 13 commits, zero evidence
entries and zero violations. `git fetch origin master` followed by
`git merge-tree --write-tree origin/master HEAD` was conflict-free against
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` (tree
`82d5b79541273d8f86755e60aa12dc8666dadb7a`). Later commits only clarify plan
prose/validation; final branch publication and merge check are reported to the
caller. No plan test count is a claim of executed tests.
