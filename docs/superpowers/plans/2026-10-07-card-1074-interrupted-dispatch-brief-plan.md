# CARD-1074: interrupted dispatch and brief recovery, current-master plan

## Outcome and baseline

The card's two descriptions of the code are superseded. Commit
`6e04c87f38e9becc1cd5d978c05b4446da04e19a` already added both the missing-brief
backfill and the catch around launch enqueue, through CARD-0519 S12d. The three
generic witnesses pass on current fetched master. Do not commission those same
production edits again.

The broader requirement in this dispatch is not yet established: **every committed
but unattempted task must retain its brief and remain retryable or visibly Blocked
with its reason**. Automatic recovery without a runner process, and interruption
of recovery itself, have gaps in the existing proof. This plan routes **next:
investigate**, rather than treating the old premise as a Code brief. It specifies
bounded measurements and a regression manifest; it is not approval of a speculative
new launch state machine.

- Inspected/tested master: `5a402d6d96da3fff88013faaaf01beb99ce9a98d`, fetched on
  2026-10-07. The assigned branch was fast-forwarded from
  `d36f79f93512ef84589314b32a4fefd24e1bc4ae`; no rebase or rewritten commits.
- Card: CARD-1074, board Antiphon, `b2fbc09c-42a0-4390-8701-7306a7d09375`.
  Read its full text and CARD-0340, CARD-1097 and CARD-1082. Searches used
  `card.ps1 search 'interrupted dispatch'`, `'brief'`, and `'launch enqueue refusal'`,
  each with `-Board Antiphon -All` (1, 130, and 1 results respectively).
- The historical design is
  [CARD-0519 S12d](2026-10-04-card-0519-unified-outbound-recovery-plan.md),
  D-S12-4/D-S12-5 and CP-64/65/66/73/78. Its old ground-truth paragraph is a
  historical baseline, not a description of this SHA.
- Only this plan is changed by this dispatch. No production, fixture, settings,
  card status, deployment, or owner-document mutation was made.

Recommendation: record the original two fixes as already implemented by the commit
above. Close the original narrowly worded defect with the fresh evidence below only
if the caller tracks the remaining measurements separately; otherwise keep it open
with a corrected brief. Do not close the stronger end-to-end acceptance claim on
the strength of manually invoked resume fixtures.

## Ground truth

All file:line pins below were read at the full master SHA above. Paths are relative
to the repository; future line drift does not change the pinned revision.

| Card assumption / required guarantee | What master actually does | Consequence |
|---|---|---|
| Claim and brief are separate commits. | `server/Application/Services/AgentTaskDispatcher.cs:5186` sets Dispatched; `:5236-5237` saves/commits; `:5270-5308` hands off launch then queues the brief. `server/Application/Services/SessionMessageQueueService.cs:637-638` obtains its own scoped context and `:790` saves the queue row. | Still true; no atomic claim-plus-brief persistence exists. |
| CARD-0340 never notices a missing brief. | `server/Application/Services/AgentSessionService.cs:886-919` now finds the Dispatched task, tests existing queue/prompt evidence, uses `FitBriefForTyping`, queues with `deliverIfIdle:false`, preserves deadline/task identity and records a Warning before `:922` flushes. | False since `6e04c87f38`; V-1 passes. |
| Repeated recovery always creates another brief. | `AgentSessionService.cs:895-900` recognizes ExecutionTaskId, the rules SourceTaskId plus marker, and a UserPrompt marker later than DispatchedAt. `:826-827` returns for non-Starting sessions. | V-3 proves these three existing-evidence shapes, not every malformed or canceled shape. |
| Launch enqueue exceptions escape directly to generic task failure. | `server/Application/Services/AgentTaskDispatcher.cs:5270-5290` catches non-cancellation enqueue exceptions, saves a Warning and continues to brief persistence. | False for that exact boundary; V-2 passes through real TickAsync with a refusing launch sink. |
| Every error after claim is covered. | Bundle/spec composition remains outside the catch at `AgentTaskDispatcher.cs:5265-5269`. The Warning save at `:5289` can throw. Non-requested OperationCanceledException can reach TickAsync's generic catch at `:980-988`. The brief catch at `:5310-5318` assumes persistence even when fitting or SaveChanges failed. | Do not generalize the narrow fix; measure M-3. |
| A retained Starting row is automatically retryable after a pre-launch refusal. | `server/Application/Services/SessionReconciliationService.cs:306-317` requires a matching Running runner session, no Pending UI, no current launch owner, and the existing compaction admission gate before scheduling resume. `:205-244` closes a runner-unknown session after its grace. `AgentTaskDispatcher.cs:2474-2479`, `:2520-2561`, `:2624-2625` can later fail its task after independent dead-session evidence/grace. | A no-process refusal is not proven recoverable. Defaults are 90 s Starting grace (`server/Application/Settings/SessionReconciliationSettings.cs:27`) and 3 min dead-session grace (`server/Application/Settings/DelegationSettings.cs:571`), not a retry promise. Measure M-1. |
| A second server interruption during resume cannot lose recovery. | `AgentSessionService.cs:871-884` commits Running and publishes events before `:909` persists backfill. Resume returns for Running at `:826`. | A second interruption in this interval can bypass this backfill entry. Measure M-2; this is source-order evidence, not a completed crash reproduction. |
| Receipt presence proves the full accepted brief. | `AgentSessionService.cs:895-900` tests queue identity or marker containment, not complete-body equality/status/attempt digest. `AgentTaskDispatcher.cs:5853-5855` rebuilds the body; `:5895` writes the local spill. Normal post-dispatch refinements do **not** rewrite Goal (`server/Application/Services/AgentTaskReplyService.cs:632-648`). | Goal is retained, but immutable fitted bytes, damaged spill handling, marker-only evidence and cross-attempt/canceled rows need distinct proof. Do not invent a normal Goal-mutation bug. Measure M-3. |
| Existing acceptance exercises the automatic recovery front door. | `tests/Antiphon.Tests/Application/DelegationBriefRecoveryTests.cs:208-213` calls ResumeInterruptedLaunchAsync directly. Its refusal test uses real TickAsync (`:45-82`) but a fake attachable adapter. `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:496-506` calls launchQueue.ResumeInterrupted directly after child death/refusal. | These are valuable generic/transport witnesses; neither proves reconciliation can discover an absent process. |
| New polling is necessary. | The landed backfill is confined to ResumeInterruptedLaunchAsync. It adds one queue-presence read and, only without a queue row, one transcript-presence read; Warning writes occur on backfill/refusal. | This documentation change adds **0 statements per tick**. Do not add an independent polling query for the repair. M-4 measures total commands before selecting an implementation. |

## Decisions

### D-1. Preserve the landed fix; investigate the remaining boundaries

The exact card repros now have passing ordinary witnesses. Rejected: re-applying
the historical patch, changing the converter fixture to seed a missing brief, or
loosening its Dispatched/complete-prompt assertions. None establishes new recovery.
The next stage is Investigate because the implementation premise is stale, not
because a user decision blocks this Plan.

### D-2. Retain input and outcome identity at every recovery boundary

The acceptance oracle is one complete original logical brief, its correct typed
pointer/spill where applicable, the same task/attempt/session-generation binding,
and at most one submitted UserPrompt. A marker fragment, canceled row, screen echo
or enqueue acknowledgement alone must not discharge this obligation. Uncertain
input must remain retained and visibly held/Blocked with a stable reason; it must
not be silently replaced or reported as attempted work. Rejected: unconditional
regeneration, unconditional resend, or a terminal Failed with merely better prose.
M-3 determines whether durable input capture or a narrower ordering/idempotency
change is needed; no schema choice is assumed before that measurement.

### D-3. Exercise discovery, not just the resume method

M-1 must use real dispatcher, reconciliation and launch ownership services together.
A fake runner is permitted, but its inventory must truthfully report no process
after a sink refusal. Never make Attach succeed for a nonexistent session to get
green. Include the matching live-process companion, unavailable inventory, wrong
generation and already-owned controls. Advance test clocks, not timeouts.

For definitely unattempted work, retryable or visibly Blocked is required. Choosing
between a durable relaunch intent and an explicit Blocked transition belongs to
the corrected implementation plan after M-1; automatic requeue without accounting
for the committed agent/session/reservations is rejected.

### D-4. No new steady-state desktop statement cost

No new job, per-task query in TickAsync, or second sweep query is allowed. Instrument
all reader/scalar/nonquery commands, including separate queue scopes. Record
absolute statement counts for empty tick, held queued task, successful dispatch,
and interrupted resume; compare against the same fixture at the pinned baseline.
Acceptance for any later repair is a **zero additional statement delta on empty
and ordinary non-recovery ticks**. Recovery-only work must have explicit bounded
counts. Source-read presence checks above are not claimed as measured total SQL.
Use the interceptor pattern in
`tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs:542`, read-only;
do not edit that in-flight feature's test to add these measurements.

### D-5. Preserve the existing safety boundaries

CARD-0079 admission and the rule that a stall is detection, never automatic kill,
remain unchanged. Do not modify Land, review evidence binding, settlement verdicts,
park release/reply/source checks, or deadlines to make the tests pass. Distinguish
a positively absent unlaunched process from a live stalled or unknown process.

This plan creates no session waiting for input. If a subsequent repair chooses
Blocked, state the release consequence explicitly: with parking disabled, **nothing
automatically releases a session waiting for input; there is no finite automatic
release deadline** (CARD-1083). An explicit authorized stop/cancel or a separately
enabled existing parking path owns release. A reply continues work; it is not a
seat-release timer. Never introduce a timeout kill to compensate for Blocked.

### D-6. Use the effective runner lane without a host pin

GET `/api/runner-defaults` and GET `/api/session-runners` were read on 2026-10-07
at about 14:13 UTC. Defaults revision 2 resolves to the general Linux runner lane;
the catalogue showed an accepting Linux runner and a draining Linux runner. Those
observations are not permanent placement configuration. Re-read both routes at
dispatch, omit `-Runner`, and omit `-Platform` because these tests require no OS.
`-Platform Any` unpins an inherited OS selection. All checkpoint rows name the
portable .NET/Postgres lane below; no fleet address is embedded in this plan.

### D-7. Keep evidence and closure claims separate

The three fresh runs are ordinary green evidence at master, not newly executed
red-first or mutation evidence. Historical baseline claims in CARD-0519 and commit
messages are provenance only. Pending controls remain pending. A later repair
requires a revised implementation/verification plan, Code, ordinary Review, Land,
and the existing post-land SourceLanding mutation workflow. Do not change that
workflow to close this card.

## Slices, files and overlaps

Each slice is 30-60 minutes of authoring/investigation, excluding checkpoint runtime
and slot waits. S2/S3 are test-shaped investigation only; no production repair is
authorized by this stale-premise plan. Commit/push test probes before long runs and
retain genuine policy assertion failures as findings rather than weakening them.

| Slice | Minutes | Files and result | Tests / checkpoint closure | AppHost restart |
|---|---:|---|---|---|
| S1: revalidate current master (completed here) | 45 | Read the dispatcher/session/queue/reconciler and historical plan; write this file. No source changes. | V-1/V-2/V-3: three exact methods in DelegationBriefRecoveryTests, evidence below. | No |
| S2: discover no-process and repeated-interruption outcomes | 60 | New `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs`; reuse BridgeQueueHarness, FakeAgentProtocolAdapter, real AgentTaskDispatcher, SessionReconciliationService and AgentSessionLaunchQueue. Add only test-side interceptors/faults. Update this plan with observed task/session/queue transitions. | M-1/M-2 methods below; CP-4/5. Each has real no-fault/live-process controls. | No |
| S3: input identity, persistence failure and SQL accounting | 60 | Extend the same boundary test file and, only as needed, its local fixture. Read `SessionMessageQueueService.cs`, `DelegationReportFormatter.cs`, `AgentTaskReplyService.cs`; do not alter their behavior. | M-3/M-4 methods below; CP-6/7. Record each subcase's observed outcome and SQL counts. | No |
| S4: narrow disposition and compatibility | 45 | This plan plus accurate, pinned sentences in `docs/session-runtime-invariants.md` and `docs/orchestration-loop.md` only after S2/S3 establish the limits. A confirmed defect yields a corrected implementation plan or linked remediation, not an unplanned production patch. | Converter and named claim/launch/resume/queue regressions CP-8..14. Preserve pending PCs. | No |

Current delivery touches only the new plan: no source overlap with either named
in-flight effort. A later production repair would touch
`server/Application/Services/AgentTaskDispatcher.cs`, which **does overlap
CARD-1097** (`RefuseParkedResumeAsync`, currently `:6377`), even though the methods
are different. CARD-1097 also owns `RemoteWorkspaceService.RefuseParkResumeAsync`
(`:689`); keep that file out of this work. Serialize any shared dispatcher edit.

CARD-1082 itself now reports Done; its F3 Held re-check continuation owns
`SettlementSyncRecoveryService.cs` and `SettlementSyncRecoveryTests.cs` per
`2026-10-07-card-1082-followups-plan.md`. Neither is a proposed edit here. Its
dispatcher sweep hook already exists; do not modify it. S4's two owner docs are
shared with both efforts, so coordinate those short edits at the latest target.
Test/build execution shares host slots and the Postgres resource even when files
do not overlap.

If measurements require a later server-code repair, its activation needs an
AppHost restart after Review/Land from the canonical checkout and verification of
GET `/api/version` against the intended SHA. No runner-binary change or runner
restart is proposed. No restart/deployment is part of these four slices.

## Documentation sentences and pins

S4 may add the following factual sentences, retaining their limits:

- `docs/session-runtime-invariants.md`, beside the CARD-0340 launch ownership rule:
  "Interrupted delegate launch resume checks for a missing Delegation brief before
  flushing and backfills through the dispatcher's brief fitting path; matching
  existing queue identity or received prompt prevents another brief."
  Pins: `DelegationBriefRecoveryTests.Interrupted_dispatch_backfills_the_missing_brief_on_resume`
  and `Resume_never_duplicates_an_existing_brief`.
- `docs/orchestration-loop.md`, delegation launch/recovery guidance:
  "A non-cancellation launch enqueue refusal after claim commit records a Warning,
  retains Dispatched/Starting and continues brief persistence; this is not proof
  that a runner process exists or that automatic recovery has completed."
  Pin: `DelegationBriefRecoveryTests.Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched`.
- Keep the runner-present condition explicit: automatic interrupted-launch resume
  requires matching Running runner evidence and no launch owner. Pins:
  `SessionReconciliationServiceTests.Starting_runner_Running_unowned_resumes_the_launch`,
  `Starting_runner_Running_owned_is_not_resumed`, and
  `Runner_Running_snapshot_for_a_superseded_generation_does_not_resume_an_interrupted_launch`.

Do not replace these with "all interrupted dispatches recover". Add a stronger
sentence only when its automatic-front-door witness passes.

## Verification design

### Witnesses and remaining measurements

| ID | Exact test / proposed test | Required observation |
|---|---|---|
| V-1 | `DelegationBriefRecoveryTests.Interrupted_dispatch_backfills_the_missing_brief_on_resume` (existing, 1) | No seeded brief; stale prior prompt does not suppress backfill; one correct spill/pointer, complete submitted UserPrompt, one backfill warning, repeated resume does not duplicate. |
| V-2 | `DelegationBriefRecoveryTests.Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched` (existing, 1) | Actual TickAsync hits refusing sink once; persisted task remains Dispatched, session Starting, brief Pending, reason null, Warning present, then direct resume delivers once. |
| V-3 | `DelegationBriefRecoveryTests.Resume_never_duplicates_an_existing_brief` (existing, 1 result, three internal cases) | ExecutionTaskId, rules SourceTaskId+marker, and transcript-only full prompt each suppress duplicate delivery. Three cases are not three TUnit results. |
| M-1 | `DelegationDispatchRecoveryBoundaryTests.C1074_Automatic_recovery_after_enqueue_refusal` (new, 5 explicit arguments: absent, live, unavailable, wrong-generation, owned) | Call real dispatcher then real scan/ownership, never resume directly. Observe before/after 90 s Starting and 3 min dead grace using test clocks, same task/attempt, all queue rows, warnings, input/start/kill calls and caller evidence. Absent unattempted work must be retryable/Blocked; uncertain/live-owned cases must not start/kill or consume a second brief. |
| M-2 | `DelegationDispatchRecoveryBoundaryTests.C1074_Recovery_survives_a_second_interruption` (new, 3: before-Running, after-Running-before-brief, after-brief-before-flush) | Interrupt through test-side save/event boundaries; rebuild services with committed database state; resume via discovery. Exactly one complete original brief is eventually receipted or a retained, reasoned Blocked obligation is visible. Include a no-fault companion inside each case. Never seed a missing brief. |
| M-3 | `DelegationDispatchRecoveryBoundaryTests.C1074_Uncertain_brief_evidence_is_not_replaced` (new, 7: marker-only prompt, canceled queue row, stale attempt row, damaged spill, queue-save fault, warning-save fault, non-requested cancellation) | Start from actual dispatched identity; exercise recovery with fresh scopes. Preserve original logical body and accepted input evidence. Capture whether a false presence check suppresses recovery or a fault creates Failed. Ambiguous evidence permits no silent replacement, partial receipt, or duplicate UserPrompt. For persistence/cancellation faults require retryable/Blocked with its reason. Full valid receipt is the companion control. |
| M-4 | `DelegationDispatchRecoveryBoundaryTests.C1074_Recovery_statement_budget` (new, 4: empty-tick, held, dispatch, resume) | Count every SQL command in all participating contexts after setup; store absolute counts and per-path counts. Retain baselines for the eventual zero-delta non-recovery comparison. Do not derive commands from elapsed time or EF SaveChanges calls. |
| R-1 | `ChannelOutboundUnifiedTransportTests.C519_Converter_handoff` (8) | All four cuts x idle/busy, unchanged complete worker brief oracle, final converted fake-recipient receipt. This requires isolated test broker/owned child process, never the live messaging broker. |
| R-2 | `AgentSessionInterruptedLaunchResumeTests` (full class) | Ready/blocked/nondelegate/already-Running and rules compatibility. |
| R-3 | `AgentTaskDispatchFailureTests` (full class) | Genuine pre-session failures still notify; caller reminder behavior retained. |
| R-4 | `AgentSessionLaunchQueueOwnershipTests` (full class) | Single owner, exact generation, retained launch responsibility and release on completion/fault. |
| R-5 | `AgentTaskConcurrencyLimitTests`, `AgentTaskDispatcherPredicateTests` (full named classes) | Claim/capacity and repository-lease admission predicates retained. |
| R-6 | `SessionMessageQueueInterruptedAttemptTests` (full class) | Interrupted Sent receipt handling, no blind retype, working/unknown-screen gates. |
| R-7 | Selected exact `SessionReconciliationServiceTests` methods in CP-14 | Matching live runner resumes; owned/wrong generation does not; absent runner inside grace remains Starting. |

M-1..M-3 are **policy witnesses to author during investigation**, not existing green
tests. Expected policy assertions may fail on master. That is a diagnostic result
to record by subcase, never a green checkpoint or a reason to fix production in
this stage. Build/fixture failure or zero executions is not a reproduced defect.
Use isolated TestDbFixture stores (the current CreateIsolatedSchemaAsync clones a
database, despite its name), serial Postgres execution, and the process limiter on
any new class that actually spawns a child. No production adapter/session runner.

### Negative controls for fail-closed requirements

Each row states a compiling mutation and an independent detecting assertion. These
are pending method-scoped post-land controls for any later repaired candidate,
not mutations performed by this Plan. Use the exact method filters below, one
control at a time; confirm intended red then restore/fresh-build/green. A missing
detector must be added before that repair is considered verified.

| Guard / PC | Mutation | Detecting test and intended red |
|---|---|---|
| G-1 / PC-1: missing input remains recoverable | Skip the backfill. | V-1: no sole correctly bound brief / complete submitted UserPrompt. |
| G-2 / PC-2: existing accepted input is not duplicated | Remove both existing-row and received-prompt suppression. | V-3: duplicate queue/submitted bodies or extra backfill Warning; verify all three internal cases. |
| G-3 / PC-3: enqueue refusal does not fail unattempted work | Rethrow the caught non-cancellation enqueue exception. | V-2: task Failed instead of Dispatched, missing Pending brief. |
| G-4 / PC-4: automatic discovery retains no-process debt without unsafe restart | Treat absent inventory as successfully recovered; separately bypass generation/ownership/unknown-inventory gates. | M-1: missing retryable/Blocked reason/obligation, or start/input/kill calls in the prohibited companion. These are separate variants. |
| G-5 / PC-5: recovery survives its own commit cuts | Publish Running without preserving a recoverable input obligation. | M-2: after-Running cut yields neither the one complete receipt nor retained Blocked input. |
| G-6 / PC-6: uncertain input is not silently substituted | Trust marker-only/canceled/stale evidence; separately overwrite the accepted spill or swallow pre-persistence faults as delivered. | M-3: missing original content/digest, partial receipt, duplicate, or Failed unattempted task; report each variant. |
| G-7 / PC-7: no added steady-state polling | Add an extra SELECT to the empty/held dispatcher path. | M-4, after the corrected repair plan pins baseline totals: non-recovery command delta is +1 instead of 0. |

No assertions weaken CARD-0079, Land, review evidence or parking. Those mechanisms
are excluded from production changes, so this plan does not claim new verification
of their full suites. Stop and revise scope/checkpoints if a later repair touches
them.

### Execution and cost

Lane for every row: **portable .NET/Postgres on the effective general runner**;
CP-8 additionally needs an isolated container broker and owned probe child. All
rows are serial, `TUNIT_MAX_PARALLEL_TESTS=1`, `UseAppHost=false`. No whole-Unit,
namespace, assembly, or argument-name-only filters. Trailing method wildcards
select parameterized results; inspect fresh TRX expanded names and counts.

For future slices run the checkpoint tool once per committed After group, using
`run --plan docs/superpowers/plans/2026-10-07-card-1074-interrupted-dispatch-brief-plan.md
--after S2 --serial` (substitute the group). Any dotnet build/run used to bootstrap
the tool goes through `scripts/build-slot.ps1`; the checkpoint executor leases its
own row drivers. Await completion; exit 75 means wait again, exit 4 means not run,
never bypass the slot. Reuse builds only within the same After group. Remove only
the run's inventoried alternate outputs when finished. No source edits during a run.

The table budgets 62 ordinary minutes, separate from 210 authoring minutes. Slot
waits are additional. Mutation is a separately commissioned cost: at least 21
method-scoped variant cycles (PC-1..3: 3; PC-4: 4; PC-5: 3; PC-6: 7; PC-7: 4),
provisionally 63 minutes at 3 minutes per cycle, plus discovery/reporting. These are
estimates, not measured PC results; the corrected implementation plan must pin the
actual mutation inventory and cost before Code. Full regression minima of 1 below
are discovery floors; every result in each named class is required, with counts
reported from TRX rather than guessed from method declarations.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1074-s1/` | missing-brief | `/*/*/DelegationBriefRecoveryTests/Interrupted_dispatch_backfills_the_missing_brief_on_resume*` | V-1 | 1 executed, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `CP-1` | enqueue-refusal | `/*/*/DelegationBriefRecoveryTests/Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched*` | V-2 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `CP-1` | no-duplicate | `/*/*/DelegationBriefRecoveryTests/Resume_never_duplicates_an_existing_brief*` | V-3 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1074-s2/` | automatic-recovery | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1074_Automatic_recovery_after_enqueue_refusal*` | M-1 | 5 executed; record each policy red/green; no infrastructure failures | 5 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S2 | `CP-4` | second-interruption | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1074_Recovery_survives_a_second_interruption*` | M-2 | 3 executed; record each policy red/green; no infrastructure failures | 3 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1074-s3/` | uncertain-input | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1074_Uncertain_brief_evidence_is_not_replaced*` | M-3 | 7 executed; record each policy red/green; no infrastructure failures | 7 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S3 | `CP-6` | statement-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1074_Recovery_statement_budget*` | M-4 | 4 executed, 0 failed/skipped; absolute SQL counts retained | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1074-s4/` | converter-handoff | `/*/*/ChannelOutboundUnifiedTransportTests/C519_Converter_handoff*` | R-1 | all 8 expanded results, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S4 | `CP-8` | resume-regression | `/*/*/AgentSessionInterruptedLaunchResumeTests/*` | R-2 | full named class, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S4 | `CP-8` | failure-regression | `/*/*/AgentTaskDispatchFailureTests/*` | R-3 | full named class, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S4 | `CP-8` | launch-ownership | `/*/*/AgentSessionLaunchQueueOwnershipTests/*` | R-4 | full named class, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S4 | `CP-8` | claim-admission | `/*/*/(AgentTaskConcurrencyLimitTests*)\|(AgentTaskDispatcherPredicateTests*)/*` | R-5 | both full named classes, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S4 | `CP-8` | interrupted-queue | `/*/*/SessionMessageQueueInterruptedAttemptTests/*` | R-6 | full named class, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S4 | `CP-8` | discovery-gates | `/*/*/SessionReconciliationServiceTests/(Starting_runner_Running_unowned_resumes_the_launch*)\|(Starting_runner_Running_owned_is_not_resumed*)\|(Runner_Running_snapshot_for_a_superseded_generation_does_not_resume_an_interrupted_launch*)\|(Starting_session_within_grace_is_left_alone*)` | R-7 | all 4 named methods, 0 failed/skipped | 4 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Evidence from this Plan dispatch

Only S1's three existing methods ran. They were invoked sequentially through
`scripts/run-checkpoint.ps1`, one isolated build then two `-NoBuild` calls, with
`-MsBuildProperty UseAppHost=false`, exact method filters, parallel limit 1, and
`-ExpectedSourceSha 5a402d6d96da3fff88013faaaf01beb99ce9a98d`. These diagnostic
master runs predate this plan and use `BASE-C1074-*` names / `bin-c1074-plan/`
instead of the forward manifest's CP names/output. No other build/test ran.

Result: **3 executed / 3 passed / 0 failed / 0 skipped**. Build: 0 errors, 637
warnings. Each source.json passed `scripts/validate-checkpoint-receipt.ps1` against
the tested SHA. The checkpoint importer accepted all 14 forward-manifest rows.
Build and row leases were granted. The future boundary probes,
converter run, full regression classes and mutation controls remain **not run**.
The plan commit is documentation after testing; it is not relabeled as the tested
source SHA. Raw artifacts remain ignored; all 28 producer-recorded alternate output
directories were removed after the three runs.

Essential unedited receipt lines:

```text
CHECKPOINT BASE-C1074-MISSING commit=5a402d6d96da3fff88013faaaf01beb99ce9a98d build=ok filter=/*/*/DelegationBriefRecoveryTests/Interrupted_dispatch_backfills_the_missing_brief_on_resume* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-af619c5d/.antiphon/checkpoints/BASE-C1074-MISSING-20261007-141438-5a99/run.trx slot=granted waited=0s dirty=0 source=5a402d6d96da3fff88013faaaf01beb99ce9a98d sourceState=clean buildSource=verified
CHECKPOINT BASE-C1074-REFUSAL commit=5a402d6d96da3fff88013faaaf01beb99ce9a98d build=reused filter=/*/*/DelegationBriefRecoveryTests/Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-af619c5d/.antiphon/checkpoints/BASE-C1074-REFUSAL-20261007-141854-6751/run.trx slot=granted waited=0s dirty=0 source=5a402d6d96da3fff88013faaaf01beb99ce9a98d sourceState=clean buildSource=verified
CHECKPOINT BASE-C1074-NODUP commit=5a402d6d96da3fff88013faaaf01beb99ce9a98d build=reused filter=/*/*/DelegationBriefRecoveryTests/Resume_never_duplicates_an_existing_brief* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-af619c5d/.antiphon/checkpoints/BASE-C1074-NODUP-20261007-142042-eb3c/run.trx slot=granted waited=0s dirty=0 source=5a402d6d96da3fff88013faaaf01beb99ce9a98d sourceState=clean buildSource=verified
```

## Handoff

Next: **investigate**. Execute S2/S3 at a recorded current target SHA, preserving
the landed CARD-1074 witnesses. Measure automatic discovery for a refusal before
any runner process exists and interruption after Running commits but before brief
persistence. Record exact policy failures and statement totals, then narrow the
remaining defect and return a corrected plan/test-design handoff. Do not duplicate
`6e04c87f38`, seed missing briefs, weaken Failed assertions, or touch parking,
CARD-0079, Land or review-evidence behavior.
