# CARD-1149 / CARD-1150: retain unattempted dispatch and recover a Running brief gap

## Outcome and baseline

Choose a visible **Blocked hold, with no automatic relaunch**, for CARD-1149.
For CARD-1150, extend interrupted-launch discovery to a narrowly selected Running
delegate with an outstanding brief obligation, and recover through an idempotent
queue operation before the delivery watchdog can fail it. Recovery must not stop,
release, restart, or replace a live or Working session.

This is a Plan deliverable, not an implementation or a green verification report.
**Next: test-design.** The brief did not explicitly fold that stage into Plan.
The verification design below supplies its behavior matrix and forward checkpoint
manifest. TestDesign must finalize the recreated fixture, discovery counts and the
inherited safety conflict described under D-7 before commissioning Code.

- Fetched `origin/master` on 2026-10-07 and inspected full SHA
  `16ed20f3af157e0e922a95ab9b4cb90fb2c7e3a2`.
- Fast-forwarded assigned branch `feat/card-task-c9a61e19` from
  `b5e78700ae9a76430c13d75cc03399055dd82e59`; no rebase, reset, or rewritten commit.
- Read full CARD-1149 (`62c236b7-fc55-4f0a-9e4b-0a6a1fe36a3d`) and CARD-1150
  (`a32f54ac-4d1d-4e0e-a91f-860646180cae`) through `scripts/card.ps1`, board Antiphon.
  Both are confirmed residuals, not duplicates of the fixes in `6e04c87f38`.
- Evidence: [residual investigation](../../investigations/2026-10-07-card-1074-residual-scenarios.md)
  and [CARD-1074 plan](2026-10-07-card-1074-interrupted-dispatch-brief-plan.md).
  Their SQL totals are inherited measurements, not new measurements in this task.
- Read the project, session-runtime, orchestration, lifecycle, HTTP, and testing
  owners. Only this new plan is edited by this dispatch. No build, test, deployment,
  settings change, card move, or process stop was performed.

## Ground truth

Pins are repository-relative `file:line` references verified at the master SHA
above. A range described in prose starts at its cited line.

| Card assumption / required behavior | What the code does at the pinned SHA | Design consequence |
|---|---|---|
| The claim can exist before the brief. | `server/Application/Services/AgentTaskDispatcher.cs:5186` sets Dispatched; `:5236` saves and commits before launch enqueue at `:5270` and brief enqueue at `:5306`. | Retain the same task, attempt, session, dispatch clock and input obligation across this boundary. |
| A refused enqueue immediately fails the task. | False for the measured refusal: `AgentTaskDispatcher.cs:5277` catches non-cancellation refusal, records a Warning, and continues to enqueue the brief. | Keep that landed behavior; do not reimplement CARD-1074. |
| An absent process is automatically resumed. | False: `server/Application/Services/SessionReconciliationService.cs:295` selects Starting only; `:306` requires a runner-listed Running session; `:312` checks generation; `:314` checks ownership; `:316` applies the compaction gate. | No automatic relaunch is hidden in CARD-0340. Never fake an attachable process in the absent fixture. |
| After grace, the refused task stays retryable. | `SessionReconciliationService.cs:205` allows Starting grace; `:242` writes Failed with the exact runner-unknown reason. `server/Application/Settings/SessionReconciliationSettings.cs:27` defaults to 90 seconds. `AgentTaskDispatcher.cs:2559` waits a further dead-session grace; `:2624` fails and notifies. `server/Application/Settings/DelegationSettings.cs:571` defaults that grace to three minutes. | Insert the narrow hold decision before terminal task failure; retain both graces. Session Failed and task Blocked are separate axes. |
| Dead-session recovery destroys the process. | It does not: `AgentTaskDispatcher.cs:2441` documents no kill. Its runner gate at `:2535` currently recognizes only entries whose status is Running. | Preserve no-kill behavior and make the new hold require complete owning-runner absence, not merely absence from the Running subset. |
| Backfill survives a second crash. | `server/Application/Services/AgentSessionService.cs:871` saves Running; `:879` publishes SessionStarted; `:886` only then looks for a task and backfills at `:909`. `:826` returns for Running. | Reordering helps new launches but does not repair already-stranded Running rows. Both discovery and execution need a Running debt arm. |
| Reusing resume is automatically non-destructive. | False: non-resumable handling calls a runner kill at `AgentSessionService.cs:850`; the exception tail at `:945` kills/disposes and fails. | A Running repair must never fall through those tails. Delegate recovery errors preserve custody and input. |
| The watchdog always gives backfill time. | `AgentTaskDispatcher.cs:2065` defers Starting only; `:2127` reads the brief, `:2136` recognizes Pending, and `:2203` produces the no-brief failure. `:2282` checks Working before `:2301` calls the stopper. Timeout is ten minutes (`DelegationSettings.cs:393`). | Run recovery/hold arbitration before destructive judgment for this debt. A recovery refusal or queued handoff is not a real delivery failure. |
| Queue serialization makes brief backfill exactly once. | `server/Application/Services/SessionMessageQueueService.cs:633` locks the session, but existing dedupe at `:676` concerns completion SourceTaskId/digest. ExecutionTaskId is assigned at `:753`, without an equivalent brief idempotency check. `AgentSessionService.cs:895` checks presence outside insertion. | Serialize presence check and brief insert together, including the normal producer racing recovery. Launch ownership alone cannot close that race. |
| One flush sends brief and follow-up. | `SessionMessageQueueService.cs:1663` locks, then `:1674` delivers the head only. The investigation observed the earlier Ui follow-up sent first when the missing brief was appended later. | Preserve FIFO and drive a second genuine idle/turn-end flush. Never assert both Sent after one flush or reorder the Ui row to manufacture green. |
| The brief is an immutable full snapshot even if no row exists. | Existing rows carry Body and optional RemoteSpillBody (`server/Domain/Entities/SessionQueuedMessage.cs:43`). `AgentTaskDispatcher.cs:5853` renders from the task; `:5895` overwrites a local full-brief spill. Remote fitting stages in memory before binding to the queue (`SessionMessageQueueService.cs:136`, `:152`, `:761`). | Reuse retained bytes before rendering. Goal preserves accepted task input, but legacy zero-row state has no universal immutable snapshot of a historical wrapper/settings version. Do not claim that stronger property. |
| Generic Retry is harmless for automatic recovery. | `server/Application/Services/AgentTaskService.cs:3485` reaches StopDelegateAsync through RequeueCoreAsync; retry increments Attempt and clears session/dispatch identity at `:3503` and `:3526`. | Do not call RetryAsync from either recovery path. An explicit later retry remains the caller's separate action. |
| Blocked requires a new schema. | `AgentTaskDispatcher.cs:6520` already stages Blocked, reason, token and event. `AgentTaskService.cs:4546` stages a parent note in its context. | Use existing columns/events and the existing note shape. No migration, new task status, or retry counter. Preserve AgentSessionId instead of copying BlockRunnerKindAsync's clear. |
| CARD-0079 is already the only automatic stop of Working on master. | There is a source-level conflict: `server/Application/Services/TaskDeadlinePolicy.cs:163` gates phase evaluation on IsWorkingAsync, then selects BootModelWait at `:180`. `AgentTaskDispatcher.cs:2803` calls TryFailBootStallAsync, whose `:2922` tail kills or invokes RetryAsync at `:2939`. `DelegationSettings.cs:452` defaults boot wait to eight minutes. `docs/orchestration-loop.md:1089` also describes that kill/retry. | The measured Working witness did not cover an aged prompt-only boot stall. Keep this conflict explicit; D-7 makes its characterization a TestDesign prerequisite, not a silently passing invariant. |
| The measured boundary class can simply be rerun. | The investigation says DelegationDispatchRecoveryBoundaryTests was throwaway and not committed. It is absent at this SHA. DelegationBriefRecoveryTests exists, with three ordinary witnesses. | Recreate the boundary class from the described faults; do not cite its historical six results as a current regression run. |

## Decisions

### D-1. CARD-1149 uses an explicit hold, not a new automatic launch state machine

After the existing dead-session grace, recognize only the current Dispatched
attempt whose session has the exact reconciliation runner-unknown failure, whose
owning runner positively reports absence, and whose current brief is demonstrably
unattempted. Use a shared constant for that existing reason without changing its
public wording; do not use a broad `Contains("launch")` predicate.

Repair 2 tightens this into a whitelist: a task is never attempted only when
there is exactly one original Pending brief, bound to this session and dispatch,
with zero attempts and every delivery, confirmation, failure, control and reply
marker pristine under every supported identity; there is no other related message
of any kind; no Working history or transcript entry of any kind exists; complete
native/sidecar evidence is positively empty for this generation; the owning runner
positively reports absence; and the session failure is exactly the runner-unknown
reason. Missing rows, unknown columns, null/unreadable evidence, and unexpected
shapes do not qualify. Current Working and listed/unknown runner safety gates
withhold before either hold or failure. Bind/report recovery still runs first.

The pure `AbsentLaunchPolicy.IsNeverAttempted` is the one decision. Its message
column census is compared to the EF model without a database statement, so an
unclassified mapped column fails closed. The original payload, optional specialist
input policy and execution deadline are retained metadata, not delivery markers.
All related rows are read without an origin/status filter, including ExecutionTaskId,
SourceTaskId, task-input ConversationKey, task marker in Body/RemoteSpillBody, and
RulesCoveredByMessageId custody links. At most two rows are materialized: two is
already outside the one-brief whitelist. Any database transcript kind/time excludes
the hold, including queued input, old activity, and an ended Working turn. Replied
fields and non-launch task events also exclude historical Working/reply activity.

Native evidence limitation: today's runner `GetTranscript` uses `GetSession`, so an
absent session normally answers 404. That answer is unknown, not an empty history.
Under this brief's strict fail-closed rule it takes the previous Failed path.
A positive hold now requires a complete, empty snapshot with the matching session
and accepted generation; the positive integration fixtures supply that explicit
certificate. They do not prove that today's absent-session API can produce it.
Supporting positive holds for that production shape requires a separately designed
runner-side retained-history/absence proof; this repair adds no runner protocol or
filesystem guesses. No 404 fallback is silently treated as proof.

Persist `Status=Blocked`, `CompletedAt=null`, a stable reason beginning
`dispatch_launch_absent`, one Blocked event and a caller note when ReplyTo requires
one. Suggested reason: "Launch never reached the runner; no brief attempt is
recorded. Original input is retained. Automatic relaunch is disabled; inspect the
session and queued input, then explicitly retry or cancel this task."

Keep Task.Id, Attempt, DispatchedAt, AgentId, AgentSessionId, Goal, queue IDs,
sequences, bodies, remote spill bytes, local spill bytes and worktree intact.
Do not invoke FailAndNotifyAsync, RetryAsync, RemoveEphemeralAgentAsync, pool
release, parking, or a stopper. Stage state/event/note in one database transaction;
publish invalidation after commit. Repeated scans see Blocked and do not produce
more events or notifications. A failed transaction leaves the original debt for
the next pass, not an error branch that terminally fails the task.

Take the existing per-session queue gate before the final evidence read, then a
task row lock / concurrency recheck in a fresh transaction. Recheck status,
attempt, session, dispatch time and generation; no change is authorized if any
changed. Recheck the owning runner before committing the hold. Missing inventory
is Unknown, not an empty list. A listed session of **any** status or generation
prevents this new absent-process action. Do not infer a remote absence from the
desktop runner's inventory.

Automatic relaunch bound is deliberately **zero**. This selects the brief's
explicitly permitted Blocked alternative. The cap witness is repeated eligible
passes yielding one visible hold and zero starts, rather than inventing an
incremented retry ledger. Explicit Retry retains its existing admission and input
history semantics; this plan does not promise to migrate an old Ui follow-up to
a new attempt. The hold note therefore requires inspecting that input first.

Rejected: resetting Dispatched to Queued with its old reservations; calling the
existing destructive Retry path; terminal Failed with nicer wording; a new retry
setting/table; and continuously restamping a Held warning.

### D-2. CARD-1150 has a Running brief-recovery arm in the existing discovery pass

Extend the **existing** pass-1c candidate query rather than adding a query/sweep.
It still includes Starting sessions. Its additional arm selects Running sessions
bound to a current Dispatched task whose brief is absent or is Pending and has
never been attempted. Express task/queue predicates as EXISTS in that one query.
Do not select every Running session or every historical task. Full receipt and
attempted/canceled rows exclude automatic reconstruction, and rules bootstrap
retains its separate pending/failed/acknowledgement owner.

Keep owning-runner partition, listed Running/no Pending UI, exact generation,
launch ownership and CheckCompactionAdmission gates. Use the same
ILaunchOwnership.ResumeInterrupted handoff so reconciliation and a watchdog
handoff cannot own two repairs. Do not add another job. A task that becomes
Working, Blocked, terminal, rebound, or superseded loses admission before typing.

In AgentSessionService, handle this Running population **before** the current
Starting-only return, in a separate non-destructive branch. Re-read task/session
identity and runner evidence, catch up transcript, then check Working. A task
status of Working or a transcript-derived Working session is an immediate defer:
no attach, queue change, synthetic restart boundary, event republish, or cleanup.
On an eligible idle session, attach to that same generation if necessary, verify
ready, ensure the brief once, and request the ordinary WhenIdle flush. Do not
invoke StartAsync, restamp StartedAt/DispatchedAt/LaunchResumedAt, emit another
SessionStarted, or replay Grok bootstrap. In particular, repeatedly discovering
debt must not perpetually reset the ten-minute watchdog clock.

For Starting delegates, share the ensure-brief operation and place durable brief
acceptance before publishing Running/SessionStarted where the rules owner permits
it. This reduces future gaps; the Running arm is still mandatory for existing
rows and for a crash after insert/before flush. Recovery-tail exceptions must not
enter the old kill/dispose path. Keep genuine nondelegate launch handling outside
this new Running branch; do not generalize it into a new relaunch policy.

Readiness refusal, unavailable/generation-unknown evidence, compaction ownership,
or a persistence error must preserve the obligation and issue no process-control
call. While ownership/evidence is transiently unavailable, defer. At the existing
delivery deadline an unresolved recoverable obligation becomes visibly Blocked
with `dispatch_brief_recovery_held` and retained input, rather than Failed or a
kill. A live Blocked session retains its seat (D-6). Recoverable cancellation
leaves the database state discoverable after service reconstruction.

Rejected: only moving the backfill above Running (does not repair historical
rows); widening the status guard while retaining the destructive catch; assuming
Attach success proves the runner still owns this generation; and treating queue
insertion as proof of a delivered UserPrompt.

### D-3. Brief idempotency belongs inside the queue gate

Add a narrow concrete queue entry for ensuring a Dispatched task's brief. Factor
the existing WhenIdle insertion code into a private under-lock core as needed;
do not recursively acquire GetLock by calling public EnqueueAsync while holding
it. Normal dispatch and interrupted recovery must both use this entry, so a late
normal producer and recovery cannot insert twice. Other origins, completion
dedupe and rules refresh keep their current semantics.

Under the queue gate and a task row lock, re-read the expected task attempt,
AgentSessionId, DispatchedAt and session generation. Check current queue evidence
and current-attempt received prompt before constructing any body. Keep the check
and insertion in the **same scoped database transaction**. All participating
brief producers must take that row lock, including a second service provider;
the process-local launch ownership registry is not a database uniqueness proof.
Commit before flush or any nested-scope read. Use the existing queue fields, not
a new unique key that would conflate same-task retries or rules messages.

Evidence outcomes are explicit: existing unattempted row (reuse unchanged), valid
received brief (no enqueue), absent (one insert), or ambiguous/attempted (no new
row; leave normal attempt recovery in charge or hold with reason). ExecutionTaskId
alone is insufficient across a same-task retry: include session and dispatch
window. Legacy rules SourceTaskId+marker remains a recognized exclusion. A
marker-only prompt or canceled/current attempted row is **not** permission to
replace or resend input. Report it as uncertain when it cannot prove delivery.
A task-marked legacy row without enough attempt identity is also ambiguous, not
an absent row. Do not add a second brief beside it.

Do not use a full historical prompt marker as proof of complete body equality.
Use the existing prompt matching/generation rules for delivery; retained payload
equality is tested independently. Exactly once here means one durable current
brief row plus queue-owned submit recovery, not a promise that an arbitrary
provider accepts exactly one network delivery.

Rejected: independent AnyAsync then EnqueueAsync; launch-owner-only dedupe;
unconditionally adding ContentDigest completion semantics to all briefs; clearing
delivery attempt fields to make a Pending row retryable; changing FIFO ordering.

### D-4. Preserve bytes; reconstruct only when there is no accepted row

An existing queue row is authoritative: retain Id, Body, RemoteSpillBody,
RemoteSpillRelativePath and every delivery field. A valid local full-brief spill
is read and reused without rewriting it. When there is no row/spill, render from
the retained task through the same brief formatter and fitting rules. Preserve
Goal and its Unicode/newlines; normal wire normalization remains the existing
queue contract. Fit or validate only after the locked absence decision.

Split fitting from payload construction enough to accept a retained full body,
instead of calling the current overwrite-on-fit path against a retained spill.
Validate task marker, current binding, expected goal and complete content. A
conflicting/damaged spill, unavailable required input, or a pointer whose payload
cannot be staged results in `dispatch_brief_input_unavailable` Blocked; it does
not silently overwrite the artifact or submit a marker-only pointer. Remote rows
retain their queue-owned spill; remote zero-row reconstruction stages the full
retained task input through the existing courier before one insert.

The byte oracle compares captured **pre-fault** brief bytes to recovered retained
payload bytes, and pre-gap Ui bytes to post-recovery Ui bytes. It does not compare
two calls to the same renderer and declare success. For a remote new row, the
queue-generated pointer UUID is new by construction; assert the full spill bytes
and pointer-to-that-row binding, not equality to an uncommitted old UUID.

No claim is made that legacy zero-row state can recover an old, never-persisted
wrapper after an arbitrary formatter/settings change. Where retained bytes and
current rendering conflict, hold for inspection. Guaranteeing that broader
historical snapshot contract would need a separately justified durable capture.

### D-5. Recovery arbitration precedes watchdog failure; cleanup remains conditional

Reuse the watchdog's already loaded suspects/session/brief information. On a
suspect with D-1 absent-launch debt, perform the same guarded hold even if the
watchdog happens to run before the dead-session sweep. On D-2 Running debt, give
the existing launch owner the same resume handoff and return/defer that judgment;
the entry must be usable without a prior reconciliation tick. An overdue
recoverable debt whose gates refuse recovery becomes a visible hold, not a
terminal "Boot prompt was never delivered" judgment.

Treat an existing Pending **unattempted** brief after the insert/flush crash the
same way. If the earlier follow-up is Working, leave the brief Pending until a
real idle/turn-end flush. Do not pump the queue in a loop. Attempted rows keep the
existing durable MaxDeliveryAttempts bound; at that bound recovery never resets
attempts or enqueues a replacement. The recovery obligation is held visibly.

Do not suppress genuine unrelated failure: provider/authentication failure,
actual attempted delivery failure, and the freshly adjudicated uncorrelated-report
arm retain their normal failure verdict. Before any stopper call require a
successful failure commit for the still-current attempt, a fresh non-Working
verdict, and affirmative owning-runner evidence permitting cleanup. A runner
that still lists the session (any status/generation), or unavailable evidence,
withholds stop/release. Recheck under the existing synchronization boundaries;
a losing status/concurrency write grants no cleanup authority. Recovery branches
must return before generic ephemeral-agent removal or pool release.

This is deliberately stricter than "the loaded task once said Dispatched".
Preserve arm-2's conditional failure transaction and fresh reply adjudication.
Keep all Land, review-evidence, CARD-1065 parking/publication/answer transitions,
and settlement-sync behavior unchanged.

### D-6. No migration, no steady-state statement increase, no new release timer

Use existing task state/reason, events, session clocks and queue evidence. No new
entity, persisted attempt counter, schema/index migration, recurring job or
separate per-tick read is approved. Candidate enrichment belongs in the existing
query; deeper reads happen only for admitted recovery debt or a due failure.

All slow-path counts must be measured by a DbCommandInterceptor across reader,
scalar and nonquery methods, sync and async, in **every participating context**.
Setup, fixture assertions and teardown are excluded from counted action scopes.
Use equal fixture options and clocks for baseline/candidate. Do not hide a new
SELECT by merging assertions into the measured action or disabling another sweep.

| Action in one-task fixture | Inherited absolute statements | Candidate requirement / explicit design ceiling |
|---|---:|---|
| Held Dispatched/Starting tick | 18 | **18 exactly**, delta 0 |
| Working/live tick below unrelated deadline | 18 | **18 exactly**, delta 0 |
| Runner-absent scan inside Starting grace | 4 measured (investigation said 8) | **4 exactly** on the one-task BuildService fixture. Do not treat 8 as this fixture's acceptance. Delta 0. |
| First dead-session observation tick | 18 | **18 exactly**, new evidence reads wait until due |
| Initial refused dispatch | 45 | Record new total; <= 55, including queue identity checks |
| Runner-unknown close scan | 6 | **6 exactly**; no task recovery query in this close arm |
| Due absent task hold tick, no parent / with parent | Old failure tick 24 | Measure separately; <= 40 / <= 48 |
| Repeated tick after the absent task is already Blocked | Not measured | Record absolute total; <= 18, no recovery-specific reads or writes |
| Running recovery, existing unattempted brief | Starting analogue 63 | Record scan plus awaited repair; <= 85 |
| Running recovery, missing brief | Starting analogue 73 | Record scan plus awaited repair; <= 95 |
| Repeated discovery after confirmed delivery | Not measured | Record absolute total and query roster; no repair invocation, no writes |
| Running recovery input/readiness hold | Not measured | <= 65; one transition/event, no repeated writes |
| Watchdog-first recovery / guarded hold | Old fail+kill tick 28 | Record separately, <= 100 including awaited repair; never hide background SQL |
| Empty tick / unrelated healthy scan | Not measured | Establish baseline and assert delta 0 |

The new-path numbers are conservative **design ceilings, not measured results**.
TestDesign pinned three rows in `C1149_C1150_Statement_budgets`: held
Dispatched/Starting tick 18, young Working tick 18, and the one-task inside-grace
absent scan 4 (live AgentSessions select, FOR UPDATE, reload, Agents). The
investigation's scan total of 8 was not reproduced on that BuildService graph.
Never waive the measured 18/4 totals or trade them for elapsed-time claims. The
unknown-close 6 and the other D-6 rows stay plan figures until Code adds those
arguments and records their rosters. Also inspect the enriched candidate query's
shape: constant round-trip count is not permission for an unbounded materialized
per-session join.

**Waiting-session checklist:** D-1 has positively established no runner process;
its Blocked task therefore does not create a physical waiting seat. D-2/D-5 can
hold an existing live session. With BlockedTaskParking:Enabled=false, **nothing
automatically releases a session waiting for input; there is no finite automatic
release deadline** (CARD-1083). The caller must inspect and explicitly continue,
retry or cancel through existing controls. An answer is not a seat-release timer.
Separately enabled existing parking retains all CARD-1065 guards; these changes
must not enable it, register a new parking caller or change its release bound.

### D-7. Treat the boot-stall policy conflict as an inherited prerequisite

The source pins in the ground-truth table contradict the requested global
CARD-0079-only Working-stop rule. The prior investigation proved only a Working
session that did not traverse the aged boot-stall branch. TestDesign must add a
baseline probe: real TickAsync, prompt-only Working turn older than eight minutes,
quiet workspace, a runner-listed matching generation, and no compaction episode.
Record every stopper/runner/adapter/release call and the status/attempt change.
Use a companion with actual assistant/tool activity; do not disable boot timeout
to make the safety witness green.

If the source-indicated automatic stop is reproduced, report it as inherited and
return a separate prerequisite repair/investigation request to the caller. Do
not silently widen these two cards into a rewrite of boot deadlines, and do not
claim the global invariant is proved by a recovery-only test. The two residual
fixes above remain valid designs; unconditional full-tick safety approval waits
for that prerequisite disposition. There is no license here for any new Working
stop, including a readiness or backfill error path.

Rejected: accepting the old prose as authority over the brief; deleting the
negative control; widening a timeout; or labeling this an intermittent test.

### D-8. Effective runner lane and ordered file ownership

Read GET `/api/runner-defaults` and GET `/api/session-runners` on 2026-10-07 at
approximately 15:27 UTC. Defaults revision 2 selects the general Linux lane; the
catalogue includes an accepting Linux runner and a draining Linux runner. This
is an observation, not a permanent host selection. Re-read both routes when
dispatching. Omit `-Runner` and `-Platform`; `-Platform Any` clears an inherited
OS pin. All checkpoints below use lane **portable .NET/Postgres, effective general
runner**. CP-36 additionally needs an isolated test broker and owned child.

S1 -> S2 -> S3 -> S4 -> S5 is mandatory. Shared dispatcher edits are serialized.
Each authoring slice is 30-60 minutes excluding builds, row execution and slot
waits. Commit and push each meaningful slice before its checkpoint group.
S2-S4 form one checkpoint group: the queue identity, discovery and watchdog
behaviors depend on one another. Push each slice, then build and run CP-6..19
once after all three commits; do not test an unfinished deadline/hold path early.
CP-20 is After S-boot and is not part of that group.

## Slices, files, activation and overlap

Abbreviations here only: `Services/` means `server/Application/Services/`;
`Tests/` means `tests/Antiphon.Tests/Application/`.

| Slice | Authoring | Files and concrete result | Tests / checkpoint group | AppHost restart |
|---|---:|---|---|---|
| S1: absent-launch hold | 60 min | `Services/AgentTaskDispatcher.cs` dead sweep + hold transaction; `Services/SessionReconciliationService.cs` shared exact reason only; new `Tests/DelegationDispatchRecoveryBoundaryTests.cs` and optional same-class `.Fixture.cs`. Preserve the original refusal catch. | V-1..V-5; CP-1..5 | Yes for activation after Review/Land; none during authoring |
| S2: queue-owned brief identity and bytes | 60 min | `Services/SessionMessageQueueService.cs` narrow locked ensure entry; `Services/AgentTaskDispatcher.cs` normal producer and non-overwriting fit; boundary fixture/tests. No generic Enqueue behavior change for other origins. | V-6..V-9; CP-6..9 | Yes |
| S3: Running recovery discovery and execution | 60 min | `Services/SessionReconciliationService.cs` existing candidate query; `Services/AgentSessionService.cs` safe Running arm and shared brief tail; boundary tests; `Tests/AgentSessionInterruptedLaunchResumeTests.cs`, `Tests/SessionReconciliationServiceTests.cs` changed expectation companions. `AgentSessionLaunchQueue.cs` is read-only unless passing an explicit expected generation is required; any such edit gets CP-27. | V-10..V-14; CP-10..14 | Yes |
| S4: watchdog arbitration and resource budgets | 60 min | `Services/AgentTaskDispatcher.cs` FailNeverStartedAsync arbitration/cleanup gates; boundary tests; `Tests/AgentTaskDeliveryWatchdogTests.cs` and its C714 partial only if fixture injection requires it. No settlement policy changes. | V-15..V-20; CP-15..20 | Yes |
| S5: regression closure and pinned owner sentences | 45 min | `docs/session-runtime-invariants.md`, `docs/orchestration-loop.md` at the launch/recovery paragraph only; this plan's actual-count annotations. Rerun boundary and exact named regression classes. | R-1..R-16; CP-21..36 | Docs alone no; activate S1-S4 together |

Only the new plan overlaps this Plan dispatch. Later S1-S4 **do** share
`AgentTaskDispatcher.cs` with CARD-1097 S4b. At this SHA its parked-resume method is
`:6377`, with `RemoteWarnAsync` at `:6403` (the brief's approximate `:6409` region).
Leave that region untouched and serialize shared-file landings. CARD-1097 is
currently InProgress; do not describe a different method as no file overlap.

CARD-1082 is Done, but its commissioned F3d settlement-sync follow-up remains an
in-flight constraint. Its `SettlementSyncRecoveryService.cs` and
`SettlementSyncRecoveryTests.cs` are read-only here. Do not copy/edit its counting
interceptor in place or alter dispatcher sync-debt sweep hooks. Its owner-doc
sentences share the same two S5 files, so merge those edits against the latest
target. The larger follow-up plan also names dispatcher changes; recheck actual
active scopes before dispatching S1.

CARD-1083 is InProgress and owns docs/bundles. S5 overlaps its two owner docs but
does not edit AGENTS.md, stage bundles, delegate-basics, orchestration skills or
agent-card-lifecycle.md. Preserve its parking-disabled wording exactly. No source
overlap with CARD-1065 parking internals, Land or review binding is intended.

One canonical AppHost restart after the code is reviewed and landed activates
S1-S4; verify GET `/api/version` against the intended canonical HEAD. No runner
binary update, runner restart, deployment, or worktree restart is part of this
Plan task. Tests use isolated services/runners. If new work requires changing a
runner protocol, migration, parking path or settlement binding, stop and revise
scope rather than treating it as an implementation detail.

## Documentation sentences and pins

S5 adds the following only after the named witnesses are green:

- At `docs/session-runtime-invariants.md:819`, adjacent to launch ownership:
  "A Dispatched task whose launch is positively absent and whose input was never
  attempted is held Blocked with its original input; this recovery never starts,
  stops or releases a process." Pins: V-1, V-2, V-3 and V-4.
- At the same paragraph: "Interrupted brief recovery also discovers an eligible
  Running delegate, ensures one current-attempt brief under the queue gate, and
  uses normal FIFO WhenIdle delivery; an existing follow-up remains one unchanged
  row." Pins: V-6, V-10, V-11 and V-12.
- In `docs/orchestration-loop.md` section 4 beside watchdog clocks: "Missing-brief
  recovery is considered before delivery failure; a retained recovery hold is not
  a failure verdict or stop authority. A live Blocked session retains its seat
  while parking is disabled, with no automatic release deadline." Pins: V-15,
  V-16, V-17 and V-18.
- Preserve CARD-0079 as the only authorized Working stop in this design. Do not
  silently rewrite the existing boot-stall paragraph at `:1089` as if its code
  already conformed; D-7's prerequisite must establish the correct owner text.

## Verification design

### Fixture and baseline discipline

Recreate `DelegationDispatchRecoveryBoundaryTests` with real dispatcher,
reconciliation, AgentSessionLaunchQueue, AgentSessionService and queue service.
Use an isolated TestDbFixture store per world, fake runner inventories and the
existing fake adapter/BridgeQueueHarness. Recreate services between crash cuts.
Drain/await launch-queue work before counting SQL, asserting or disposing a world.
No production session runner, provider or messaging broker. A process-spawning
variant must use the assembly's ParallelLimiter<ProcessSpawnLimit>.

The launch sink refusal must leave the fake runner genuinely empty. Inject the
brief INSERT failure in an interceptor's reader arm (Npgsql INSERT RETURNING),
and the second interruption on SessionStarted publication after Running commits.
Also retain the committed-insert/pre-flush cut. Do not seed the missing brief
as test setup. Capture bytes before the fault; compare from an independent fresh
scope afterwards. Test clocks advance 89/91 seconds, the three-minute dead grace,
and +11 minutes explicitly, without sleeping or widening production timeouts.

Before Code, TestDesign runs D-7's method on the pinned base and reports the actual
inherited result. It also finalizes the fixture's SQL action scopes and expanded
test roster. This stage may use test-only probes; any such source is committed
before a build. Code then proves each new assertion red against the pre-fix
production boundary, with compiling source and actual test executions. Existing
passing CARD-1074 witnesses remain intact. Infrastructure/build failure and zero
executions are not red proof. Do not rerun the whole assembly to classify red.

### Behavior witnesses

Every V method below is new in `DelegationDispatchRecoveryBoundaryTests`. Each is
one TUnit result unless an explicit argument count is given. Internal matrix
cases are not counted as separate executions. Use assertion labels matching the
V IDs so later coverage/mutation selection stays unambiguous.

| ID | Exact method | Required observation / negative companion |
|---|---|---|
| V-1 | `C1149_Absent_launch_is_blocked_with_original_input` | Real refused claim -> 91s close -> due dead sweep: task Blocked, CompletedAt null, stable reason/event, one preserved Pending brief, no start/stop/release, unchanged attempt/session/goal/spill. Capture and compare full UTF-8 bytes. |
| V-2 | `C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero` | Repeat due ticks and reconstruct services: one hold event/note, no new launch, zero relaunches, same bytes and queue ID. Atomic-save fault gives neither partial note nor Blocked state; next pass commits once. |
| V-3 | `C1149_Listed_or_unknown_runner_is_never_absence` (5 arguments) | Listed Running, listed Starting/Pending, listed Exited, wrong generation, unavailable owning inventory: no new hold/fail/start/kill/release by the absent arm. Include a remote owner with a misleading empty local list. |
| V-4 | `C1149_Changed_or_working_attempt_is_untouched` (3 arguments) | Working task, Dispatched task with Working transcript, and concurrent rebind/settlement before conditional hold: no mutation from the recovery arm, no input/stop/release. |
| V-5 | `C1149_Different_reason_or_attempted_brief_still_uses_failure_policy` (3 arguments) | Different session failure, Pending with prior delivery evidence, and genuine dead attempted turn: fail with original reason through existing policy; never relabel unattempted Blocked. No dead-sweep kill. |
| V-6 | `C1150_Concurrent_producers_ensure_one_brief` | Barrier releases normal post-claim enqueue plus two recovery calls through separate service scopes/providers. One row/one payload, same attempt; repeat after new service construction. If a delivery occurs, one attributable complete UserPrompt. |
| V-7 | `C1150_Existing_brief_and_spill_are_byte_identical` (3 arguments) | Inline, local spill, remote queue-owned spill. Store pre-gap byte arrays; no body, ID, path or delivery-field rewrite; pointer resolves to exact original bytes. Include Unicode and LF. |
| V-8 | `C1150_Uncertain_evidence_cannot_create_a_replacement` (4 arguments) | Canceled row, attempted row, marker-only prompt, damaged/conflicting spill: no new row or submit; explicit retained hold/owner outcome, never "delivered". Existing complete receipt companion remains no-op. |
| V-9 | `C1150_Old_attempt_evidence_does_not_suppress_current_brief` | Old session/dispatch-window row and stale prompt do not discharge current obligation. Changed current attempt during lock acquisition grants no insert or spill overwrite. |
| V-10 | `C1150_Running_cut_is_discovered_and_delivered_once` | Actual dispatch INSERT fault, actual resume publication cut after Running save, then automatic discovery in fresh services. One correct brief, full UserPrompt, no start/kill; second scan no backfill and LaunchResumedAt unchanged. |
| V-11 | `C1150_Insert_before_flush_cut_reuses_pending_row` | Crash after INSERT commit before flush. Running rediscovery delivers that same row once; no replacement or body rewrite. |
| V-12 | `C1150_Followup_survives_both_queue_orders` (2 arguments) | Existing brief before Ui, and Ui before missing backfill. Ui Id/Body/Sequence unchanged and one row; first flush sends only original head. Real turn-end/idle advancement eventually confirms both full bodies once. |
| V-13 | `C1150_Discovery_gates_withhold_recovery` (6 arguments) | Working, unavailable runner, wrong generation, owned launch, Pending UI, unresolved compaction episode. No attach/insert/submit/start/kill/release; no clock restamp. Fresh eligibility allows the positive companion to recover. |
| V-14 | `C1150_Readiness_or_persistence_fault_preserves_custody` (3 arguments) | Attach/readiness refusal, brief save fault, cancellation after durable insert. Retain same input/task/session; do not enter kill/dispose failure tail. Fresh pass recovers transient cuts; deadline turns unresolved debt into visible hold. |
| V-15 | `C1150_Watchdog_first_recovers_before_failure` | Advance +11 minutes without a second reconciliation scan; dispatcher arbitrates Running missing-brief recovery first, await handoff, then obtain one complete prompt. No terminal failure or stopper. |
| V-16 | `C1149_Watchdog_first_holds_absent_launch` | Due watchdog runs before due dead sweep on the exact refused/absent shape: same atomic hold, byte preservation and zero process calls as V-1. |
| V-17 | `C1150_Recovery_exhaustion_is_visible_without_replacement` | At queue MaxDeliveryAttempts, keep row/attempt evidence, one Blocked reason, no reset/retype/replacement/start/stop. Repeat after restart; no warning storm. |
| V-18 | `C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence` (5 arguments) | Working before decision; Working after catch-up; listed session; unavailable inventory; failure-write loses to settlement. All stopper and release counts zero. Independent real non-Working unrelated failure with authoritative safe evidence retains its failure and conditional stopper behavior. |
| V-19 | `C1149_C1150_Statement_budgets` (14 arguments) | One argument for each D-6 table action (split paired variants internally and emit their individual totals). Pinned fixtures: held tick 18, inside-grace scan 4, young Working tick 18. Unknown-close 6 stays the plan figure until that argument exists. Delta-zero healthy paths, bounded new paths; count separately scoped queue SQL and await all repair work. |
| V-20 | `C1149_C1150_Working_full_tick_safety` (2 arguments) | Active Working companion, aged prompt-only boot stall. Real scan/resume/tick, same runner generation. No recovery mutation, submit, stop or release; no CARD-0079 episode. D-7 is an inherited-red prerequisite: do not disable boot wait or claim this passes without evidence. |

V-20 cannot be marked green merely because the recovery branch returned early:
the outer tick must also be observed. If baseline reproduces the separate boot
stop, TestDesign names that blocking prerequisite and hands its evidence to the
caller before issuing a Code-ready verification contract.

### Regression roster and intentional expectation changes

| ID | Regression class / behavior | Scope |
|---|---|---|
| R-1 | DelegationDispatchRecoveryBoundaryTests | All final boundary methods once at the combined candidate; SQL and safety assertions included. |
| R-2 | DelegationBriefRecoveryTests | All three landed CARD-1074 witnesses; retain no duplicate and complete prompt assertions. |
| R-3 | AgentTaskDeadSessionReconciliationTests | All failure/grace/bind/report controls; only the new positively unattempted runner-unknown case gains Blocked. |
| R-4 | AgentTaskDeliveryWatchdogTests | Full partial class, including C714 tests. Preserve fresh-attribution/conditional-failure behavior. Existing tests with no runner evidence must not be mistaken for the positively eligible Running recovery case. |
| R-5 | AgentSessionInterruptedLaunchResumeTests | Full class. Replace the unconditional `Already_Running_row_is_a_noop` assumption with an ineligible Running no-op plus the real recoverable companion. Keep nondelegate and rules tests. Any changed delegate cleanup expectation must explicitly name the live/unknown custody gate. |
| R-6 | SessionReconciliationServiceTests | Full class: local/remote absence, generation, Starting grace and ownership; enrich the existing query without changing close semantics. |
| R-7 | AgentSessionLaunchQueueOwnershipTests | Full class, including registration/release after fault; release here means in-process launch ownership, not a physical seat. |
| R-8 | AgentTaskDispatchFailureTests | Full class: genuine pre-claim/launch errors still fail and notify. |
| R-9 | AgentTaskConcurrencyLimitTests | Full class, claim/admission/capacity compatibility. |
| R-10 | AgentTaskDispatcherPredicateTests | Full class, prepared worktree/runner claim predicates unchanged. |
| R-11 | SessionMessageQueueInterruptedAttemptTests | Full class, attempted Sent recovery, receipt before resend, Working/unknown-screen gates. |
| R-12 | SessionMessageQueueDeliveryVerificationTests | Full class: ordinary queue verification remains authoritative after shared insertion refactor. |
| R-13 | SessionMessageQueueServiceTests | Full class: FIFO, origins, WhenIdle and normal queue behavior. |
| R-14 | SessionTerminationSourcePersistenceTests | Full class: real failure and reconciliation source attribution remains correct. |
| R-15 | AgentTaskLivenessTests | Full class: unrelated death classifier semantics unchanged. |
| R-16 | ChannelOutboundUnifiedTransportTests.C519_Converter_handoff | Eight cut x idle/busy cases; no seeded replacement brief or weakened final worker-body oracle. Isolated test broker only. |

Do not change an existing assertion simply because it fails. First classify its
fixture against the new explicit evidence predicates. Add a positive and a
negative companion for each intentional changed expectation; inherited unrelated
red is rerun method-scoped at the base SHA and reported separately.

### Fail-closed negative controls and later positive-control mutations

These are required detecting assertions, with concrete compiling mutations for
the separately commissioned post-land Mutation stage. They are not runs performed
by this Plan. Each variant uses only the exact method filter from its CP row;
never mutate and run a whole class. Confirm the intended assertion is red, restore
all source, commit any authorized repair separately, and run that method green.

| PC | Guard to falsify / compiling mutation | Detector and intended red |
|---|---|---|
| PC-1 | Route positively absent unattempted debt to FailAndNotifyAsync instead of hold. | V-1: Failed instead of Blocked, or CompletedAt set. |
| PC-2 | Permit one automatic RetryAsync / duplicate hold publication on a repeated due pass. | V-2: start/stop/attempt count exceeds zero, or more than one event/note. Two separate variants. |
| PC-3 | Treat unavailable inventory as empty; separately recognize only Running as listed. | V-3: protected runner case is mutated or stop/release called. Two variants. |
| PC-4 | Remove task/transcript Working guard; separately remove current-attempt concurrency recheck. | V-4: changed state/queue/stop in each protected companion. Two variants. |
| PC-5 | Drop exact reason or no-attempt requirement. | V-5: unrelated failure wrongly becomes Blocked. Two variants. |
| PC-6 | Put queue existence read back outside insertion lock/transaction. | V-6: barrier produces two brief rows or prompts. |
| PC-7 | Regenerate/overwrite retained brief or spill bytes. | V-7: independent captured-byte comparison fails. |
| PC-8 | Treat canceled/attempted/marker-only evidence as authority to enqueue a replacement; overwrite corrupt spill. | V-8: prohibited new row/input or missing visible hold. Four variants. |
| PC-9 | Remove session/dispatch-window filter from brief evidence. | V-9: old evidence suppresses current brief. |
| PC-10 | Restore Starting-only discovery; separately restore Running early return. | V-10: no sole full brief receipt after the second automatic scan. Two variants. |
| PC-11 | Ignore an already persisted pending brief and reconstruct it. | V-11: changed ID or duplicate row. |
| PC-12 | Flush twice immediately or replace/reorder the Ui row. | V-12: first-flush FIFO or Ui byte/ID oracle fails. Two variants. |
| PC-13 | Bypass generation, ownership, Pending UI or compaction gate, one at a time. | V-13: attach/input/mutation count is nonzero for the prohibited case. Four variants. |
| PC-14 | Send recovery exception through existing kill/dispose tail. | V-14: stopper/adapter kill/release count nonzero. |
| PC-15 | Run watchdog failure before recovery arbitration. | V-15 and separately V-16: terminal failure or stop instead of recover/hold. Two cycles. |
| PC-16 | Reset DeliveryAttempts or ignore its cap. | V-17: extra submit/replacement/attempt reset, or absent Blocked reason. |
| PC-17 | Remove Working, listed/unknown-runner, or successful-current-failure requirement at cleanup. | V-18: corresponding forbidden stopper/release call. Three variants. |
| PC-18 | Add one SELECT to held tick or inside-grace scan. | V-19: 19 != 18, or 5 != 4 on the pinned inside-grace fixture. Two variants. |
| PC-19 | Bypass a new Working guard in recovery; after prerequisite repair, bypass its boot guard separately. | V-20: stop/release or recovery mutation under real full tick. Second cycle belongs to the prerequisite repair's owner, not an unassigned mutation here. |

### Execution, cost and provenance

Every row uses the portable .NET/Postgres lane named in D-8; all rows are serial
and set `TUNIT_MAX_PARALLEL_TESTS=1`. Use the checkpoint tool once per committed
After group, `run --plan <this-path> --after S1 --serial --expected-source-sha
<committed-sha>` (substitute group). Await `wait` until exit is not 75. Any dotnet
bootstrap/build/test driver uses `scripts/build-slot.ps1`; the executor gates its
own row drivers. Slot timeout is not run, never permission for an unleased run.
Use forward-slash alternate outputs and default Linux UseAppHost=false. Keep
source frozen during each run; reuse a build only in its own After group.

TestDesign's baseline probe is `BootStallWorkingTickCharacterizationTests`,
not the unwritten V-20 method. It ran against the pinned base and classified
the inherited conflict. It does not certify the candidate.
Any probe authoring gets its own test-only commit and named source provenance.
No whole-Unit, namespace or assembly run is commissioned here.

Authoring estimate: 285 minutes. Ordinary forward checkpoints for commissioned
Code (CP-1..CP-19 and CP-21..CP-38): **119 minutes**, plus build-slot waits.
CP-20 adds 3 minutes and is After S-boot, not in that budget. A Code dispatch
budgets both, and re-estimates the two
larger queue classes from fresh roster timings before starting if necessary.
Mutation has at least 35 assigned method-scoped variant cycles (plus the separate
boot prerequisite cycle), estimated 105 minutes plus setup/restoration. These are
budgets, not observed runtimes or executed counts. No repeat-proof run is required.

TestDesign on 2026-10-07 replaced the full-class Min=1 discovery floors with
verified execution counts. CP-22 stays 3 and CP-36 stays 8. CP-21 is 59 once
V-1..V-20 exist as specified; the boot-stall characterization class is not part
of that 59. The three budget arguments committed at TestDesign are the start of
V-19's 14, not executions added on top. CP-19 Min stays 14 and fails until Code
adds the other eleven arguments. CP-20 is After S-boot with its own build.
CP-37 and CP-38 pin the characterization and must stay green. Code reports
actual TRX counts, failures and skips for every CP. Zero tests, skipped
mandatory cases, and missing methods cannot be green. Preserve
unedited CHECKPOINT lines and source receipts in the stored report; generated
TRX/JSON/logs remain ignored. Code/Review run the evidence-diff guard over the full
task range and validate receipts against exact tested SHAs. Remove only inventoried
alternate outputs after runs. No broader repeat testing after green without a
new failure/change reason.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1149-s1/` | absent-hold | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Absent_launch_is_blocked_with_original_input*` | V-1 | 1 executed, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `CP-1` | hold-bound | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero*` | V-2 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `CP-1` | absence-gates | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Listed_or_unknown_runner_is_never_absence*` | V-3 | 5 executed, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `CP-1` | hold-race | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Changed_or_working_attempt_is_untouched*` | V-4 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | `CP-1` | real-failure | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Different_reason_or_attempted_brief_still_uses_failure_policy*` | V-5 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S2-S4 | `tests/Antiphon.Tests -> bin-c1150-recovery/` | brief-identity | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Concurrent_producers_ensure_one_brief*` | V-6 | 1 executed, 0 failed/skipped | 1 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S2-S4 | `CP-6` | brief-bytes | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Existing_brief_and_spill_are_byte_identical*` | V-7 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S2-S4 | `CP-6` | ambiguous-input | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Uncertain_evidence_cannot_create_a_replacement*` | V-8 | 4 executed, 0 failed/skipped | 4 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S2-S4 | `CP-6` | attempt-identity | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Old_attempt_evidence_does_not_suppress_current_brief*` | V-9 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S2-S4 | `CP-6` | running-gap | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Running_cut_is_discovered_and_delivered_once*` | V-10 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S2-S4 | `CP-6` | insert-cut | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Insert_before_flush_cut_reuses_pending_row*` | V-11 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S2-S4 | `CP-6` | followup-fifo | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Followup_survives_both_queue_orders*` | V-12 | 2 executed, 0 failed/skipped | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S2-S4 | `CP-6` | discovery-gates | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Discovery_gates_withhold_recovery*` | V-13 | 6 executed, 0 failed/skipped | 6 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S2-S4 | `CP-6` | recovery-faults | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Readiness_or_persistence_fault_preserves_custody*` | V-14 | 3 executed, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S2-S4 | `CP-6` | watchdog-recover | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Watchdog_first_recovers_before_failure*` | V-15 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S2-S4 | `CP-6` | watchdog-absent | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_Watchdog_first_holds_absent_launch*` | V-16 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S2-S4 | `CP-6` | attempt-bound | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Recovery_exhaustion_is_visible_without_replacement*` | V-17 | 1 executed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S2-S4 | `CP-6` | cleanup-gates | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence*` | V-18 | 5 executed, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S2-S4 | `CP-6` | statement-cost | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | V-19 | 14 executed, 0 failed/skipped; actual totals reported per action | 14 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S-boot | `tests/Antiphon.Tests -> bin-c1149-sboot/` | working-safety | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Working_full_tick_safety*` | V-20 | 2 executed, 0 failed/skipped; prerequisite card has removed the boot kill | 2 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S5 | `tests/Antiphon.Tests -> bin-c1149-s5/` | boundary-regression | `/*/*/DelegationDispatchRecoveryBoundaryTests/*` | R-1 | 59 executed, 0 failed/skipped | 59 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S5 | `CP-21` | landed-brief | `/*/*/DelegationBriefRecoveryTests/*` | R-2 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S5 | `CP-21` | dead-session | `/*/*/AgentTaskDeadSessionReconciliationTests/*` | R-3 | 25 executed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S5 | `CP-21` | watchdog-regression | `/*/*/AgentTaskDeliveryWatchdogTests/*` | R-4 | 85 executed, 0 failed/skipped | 85 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S5 | `CP-21` | resume-regression | `/*/*/AgentSessionInterruptedLaunchResumeTests/*` | R-5 | 8 executed, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S5 | `CP-21` | reconcile-regression | `/*/*/SessionReconciliationServiceTests/*` | R-6 | 67 executed, 0 failed/skipped | 67 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S5 | `CP-21` | ownership-regression | `/*/*/AgentSessionLaunchQueueOwnershipTests/*` | R-7 | 7 executed, 0 failed/skipped | 7 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S5 | `CP-21` | dispatch-failure | `/*/*/AgentTaskDispatchFailureTests/*` | R-8 | 15 executed, 0 failed/skipped | 15 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S5 | `CP-21` | claim-capacity | `/*/*/AgentTaskConcurrencyLimitTests/*` | R-9 | 25 executed, 0 failed/skipped | 25 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S5 | `CP-21` | claim-predicates | `/*/*/AgentTaskDispatcherPredicateTests/*` | R-10 | 17 executed, 0 failed/skipped | 17 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | S5 | `CP-21` | interrupted-attempt | `/*/*/SessionMessageQueueInterruptedAttemptTests/*` | R-11 | 15 executed, 0 failed/skipped | 15 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S5 | `CP-21` | queue-verification | `/*/*/SessionMessageQueueDeliveryVerificationTests/*` | R-12 | 123 executed, 0 failed/skipped | 123 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S5 | `CP-21` | queue-regression | `/*/*/SessionMessageQueueServiceTests/*` | R-13 | 25 executed, 0 failed/skipped | 25 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-34 | S5 | `CP-21` | termination-source | `/*/*/SessionTerminationSourcePersistenceTests/*` | R-14 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-35 | S5 | `CP-21` | liveness-classifier | `/*/*/AgentTaskLivenessTests/*` | R-15 | 6 executed, 0 failed/skipped | 6 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-36 | S5 | `CP-21` | converter-handoff | `/*/*/ChannelOutboundUnifiedTransportTests/C519_Converter_handoff*` | R-16 | 8 executed, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-37 | S1 | `CP-1` | boot-stall-now | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-boot | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-38 | S5 | `CP-21` | boot-stall-still | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-boot | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
