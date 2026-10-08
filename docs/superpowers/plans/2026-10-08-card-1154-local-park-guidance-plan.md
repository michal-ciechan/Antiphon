# CARD-1154: local confirmed-park follow-up guidance

Plan date: 2026-10-08. Plan task: `dd35b815-3d21-4e52-9fb2-8fac218cd88e`.
Inspected source: `2a0f8407486f37ffb3588c221c42c77d7d1fbb70`, the assigned branch base.
Read CARD-1154, landed CARD-1144 and CARD-1146, and their
[combined plan](2026-10-08-card-1144-1146-1147-confirmed-park-reply-plan.md).
The findings below are source inspection, not executed runtime evidence.

The local follow-up refusal can recommend an answer that the existing Reply path
correctly refuses. Reuse the already shared admission query in this refusal;
do not change release or answer admission. Verification design is included because
the brief requests the tests, production controls, statement budget and executable
checkpoint manifest. Next stage: **Code**, then ordinary Review and Land.
Production mutation remains a separately commissioned post-land obligation; this
plan does not lift the operator's pause on Mutation reported by the predecessor cards.

## Ground truth

Line references below refer to the inspected source, not the older combined plan.

| Card assumption or question | What the code does | Plan consequence |
|---|---|---|
| Local follow-up recommends an inadmissible Reply. | `server/Application/Services/AgentTaskService.cs:525` calls `HasConfirmedPublishedParkAsync`; `:528` recommends `Reply to continue`. That detector at `:3339` requires current task/attempt, publication, linked release, Parked/ResumePending, Confirmed state and ConfirmedAt, but no release identity, ActionId or outcome whitelist. | Premise confirmed. Replace this use, not the detector's meaning. |
| Admission needs a new identity predicate. | `server/Application/Services/RunnerSeatReleaseQueries.cs:20` already matches task, attempt, session, agent, runner, settlement revision/time and a correlated server session's runner/store/StartedAt. `:48` adds the full confirmed receipt. `TerminalRunnerSeatReleaseService.cs:534` and `:539` consume those queries. | Reuse the implementation already landed for CARD-1146. No copied comparisons. |
| Remote guidance is a suitable shared classification. | `AgentTaskService.cs:3311` composes `Confirmed(ForAttempt(...))`, links it to a current-attempt published Parked/ResumePending park, and returns Admitted, ReleaseMismatch or None in one `ToListAsync`. Admitted wins over mismatched candidates. | Use this same classifier for the local branch. Preserve its query and precedence among candidates. |
| A stale release still queues to the old session. | `AgentTaskService.cs:3183` now throws `park_release_identity_mismatch` before returning to the old-session fallback, after the existing accepted-answer-pending refusal. Continued answers use the shared confirmed query at `:3367` and again under locks. | CARD-1144 is landed. Preserve its weak detector as a veto; strengthening that detector would remove the protection this card relies on. |
| Local evidence order cannot matter. | Create reads `sessionLive` at `:504`, refuses remote-pool follow-up at `:516`, then returns generic live Reply advice at `:518` before reading park evidence. A stale Running session projection can therefore hide an inadmissible confirmed park. | Keep the remote 422 first. Evaluate local confirmed-park classification before selecting ordinary live-session advice. No session state is changed. |
| Existing local positive tests establish exact identity. | `BlockedTaskParkDeliveryTests.cs:360` calls `RetireLocalSessionAsync` (`:700`), which clears task runner and session IDs. `ConfirmCurrentAttemptParkAsync` creates an incomplete release. `C1065_PinnedFollowupCannotStealParkedIdentity` (`:314`) also expects Reply from `ParkedPredecessor` (`:911`), whose local task has no runner and whose receipt lacks admission facts. | Those local assertions encode the defect. Keep their refusal/no-insert and other coverage, replace their unsafe local Reply expectations, and supply genuine positive coverage through the real publication fixture. |
| “Local branch” means the task has no runner identity. | The 422 requires both `followAgent.IsPoolDelegate` and a canonical nonlocal retained runner. A non-pool agent with an exact bound runner reaches the local 409 branch. `ForAttempt` intentionally admits no task with null/empty runner or null session. | A positive local-branch fixture sets only `agent.IsPoolDelegate=false` after publication, retaining exact task/session/release bindings. Missing identity is a negative case, never repaired in production. |
| All uses of the weak detector should be replaced. | `AgentTaskService.GetAsync:2483` also supplies that detector to `BlockedContextBuilder`, and answer acceptance uses it as the stale-park veto. | This card changes follow-up Create guidance only. Task-detail `CanAnswer`/context projection is outside scope and remains a separate limitation; do not claim all presentation surfaces are repaired. |
| Parking has a time-based seat-release promise. | `BlockedTaskParkingOptions.cs:6` and `:7` default Enabled/ReclaimExisting off; the runtime owner explicitly says there is no automatic release deadline while disabled. | No enablement, automatic continuation, stop, failure or scheduler changes. |

## Decisions

### D-1. Reuse the existing classifier at the one blocked-follow-up decision point

In `AgentTaskService.CreateAsync`, replace the remote-pool-only conditional assignment
to `parkReply` with one unconditional awaited `RemotePoolParkReplyAsync(blockedOnAgent, ct)`
inside `if (blockedOnAgent is not null)`. Retain the existing
`RefuseRemotePoolFollowUp(followAgent, retainedRunnerId, priorId, parkReply, blockedOnAgent.Id)`
call. That call continues to throw the existing 422 before any local 409 is chosen.

Then choose local text in this order:

1. **Admitted:** existing published-seat 409, including this blocked task's `-Reply`.
2. **ReleaseMismatch:** the same 409 code, with the new neutral mismatch text in D-2.
3. **None plus sessionLive:** existing live Blocked Reply-or-cancel 409.
4. **None plus no live session:** existing dead-session cancel-and-re-send 409.

The current liveness SELECT may stay before the classification SELECT; the order
above is the order in which evidence controls the message. An Admitted or
ReleaseMismatch result outranks a stale Starting/Running projection. Ordinary live
Blocked tasks with no confirmed park retain today's behavior. A current park with
unknown/missing receipt evidence never earns the published-seat Reply message.
None retains today's generic liveness-based advice; a read failure propagates and
must not be converted into an Admitted result or a successful empty read.

Keep `RemotePoolParkReplyAsync` and its enum name for this small change; update its
XML comment to say it serves both follow-up branches. No API rename or wrapper is
needed. Its existing confirmed classification is intentionally weaker than Admitted:
it permits a conservative mismatch message, never continuation authority.

Rejected: broad helper rename (unnecessary churn); duplicate identity comparisons
(future drift); preload a release then run the classifier (extra read); call the
classifier per park (unbounded reads); replace `HasConfirmedPublishedParkAsync`
globally (breaks the CARD-1144 veto and expands scope); keep the live return ahead
of confirmed evidence (still recommends an inadmissible Reply).

### D-2. Change only the misleading guidance and its evidence precedence

Keep HTTP 409 and code `follow_up_agent_blocked`, the prior/blocked task identifiers,
and the existing positive published-seat wording. For ReleaseMismatch use:

> Task {priorShort} ran on agent '{followAgent.Name}', which is parked on Blocked task {blockedShort}. Its published park's seat release does not match answer admission, so that parked answer cannot be continued.

This branch names neither `-Reply` nor `/cancel`, and gives no automatic repair or
fresh-task command. We know that its release fails admission; we have not established
that cancellation is the intended recovery. A stale live projection gets the same
mismatch text. Remote-pool 422 wording remains unchanged. The ordinary no-park live
and dead messages remain unchanged. No exception code, endpoint, DTO or client edit.

“Admitted” here means the same release-identity and confirmed-receipt predicate as
Reply at this read, not a promise that future authentication, quota, workspace,
concurrent revision or dispatch checks will succeed. Actual Answer retains all
those checks and locked rechecks. No source repair, receipt retagging or input is
performed while producing guidance.

### D-3. Preserve one guidance read, explicitly account for the live branch

The classification is exactly **one database SELECT**, including all correlated
park/release/session evidence, with zero writes and no runner call. It is one call
per blocked-follow-up Create, independent of candidate count and classification.
No separate `HasConfirmedPublishedParkAsync` call remains in this Create block.
The existing liveness query stays separate (zero if the task has no session ID,
otherwise one SELECT). This is not a claim that the entire Create request is one read.

Relative to the inspected source: remote-pool and nonlive local paths retain one
guidance read; local live paths add one bounded read, necessary to prevent a stale
live projection bypassing confirmed-release evidence. Other Create reads do not
change. Do not put this query in a retry pipeline or change database configuration.
V-4 measures both the helper boundary and its actual public caller; measuring only
the helper would miss an extra weak query or duplicate call in Create.

### D-4. Keep safety and operational scope unchanged

Migration: **no**. Settings/protocol/client changes: **no**. AppHost restart:
**yes after reviewed land to activate the server change; no during Plan or Code**.
No runner restart. The caller follows `docs/apphost-runbook.md` from the canonical
checkout, updates that checkout before restart, and verifies `/api/version` against
its HEAD. Never restart from this worktree or use `-AllowWorktree`.

Parking remains default-off and inert for new parks. Existing receipts and accepted
answers retain their established recovery. Nothing here stops or fails a Working
session; CARD-0079 remains the only exceptional automatic stop policy for Working.

Input-wait checklist: this change adds no waiting session. With shipping parking
settings, **nothing automatically releases a session waiting for input, and there
is no automatic release deadline** (CARD-1083). With the existing gates explicitly
enabled, publication, custody, source, idle and conditional-release evidence control
release. The 120-second proof/interval and 600-second backoff are not deadlines.
Reply on an ordinary live Blocked task resumes work while retaining the seat.

### D-5. Use lanes, not a host pin

Read `GET /api/runner-defaults` and `GET /api/session-runners` at approximately
2026-10-08 15:42 UTC. Defaults revision was 2 with no per-kind overrides. The
catalogue showed an accepting Linux lane at its reported capacity, another Linux
entry draining, and an accepting Windows lane. These are observations, not a future
reservation. The manifest uses **Linux / isolated PostgreSQL** for integration and
**Linux / portable unit** for the documentation pin. No native Windows work is needed.

Re-read defaults/catalogue before dispatch. Omit `-Runner`; omit `-Platform` because
the code and tests require no OS pin. `-Platform Any` explicitly unpins. Queue for
effective capacity rather than choosing a fleet location in this plan. Keep fixture
schemas isolated, `NotInParallel("MessageQueue")`, and the assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. Do not co-schedule Pty/FakeClaude assemblies;
no test uses the production runner.

## Slices and collision boundaries

Three sequential authoring slices, each 30–60 minutes. Commit and push each slice
with verification pending in its message; after S3, freeze source and run the
single S1-S3 checkpoint group below. One isolated build is reused by all rows.

| Slice | Authoring | Files | Deliverable and tests |
|---|---:|---|---|
| S1 | 45 min | `server/Application/Services/AgentTaskService.cs`; `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs` | D-1/D-2 production change, test-local non-pool fixture setup and V-1 nine-field matrix. Shared query implementation and real Answer stay untouched. |
| S2 | 60 min | `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs`; `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs` | V-2 session matrix, V-3 evidence order, V-4 SQL budget; correct the two existing local regressions described below without removing their other witnesses. |
| S3 | 30 min | `docs/session-runtime-invariants.md`; `docs/orchestration-loop.md`; `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs` | Update owner sentences and existing pins; run CP-1 through CP-17 after all three commits. No additional production change expected. |

Read-only dependencies: `RunnerSeatReleaseQueries.cs`,
`TerminalRunnerSeatReleaseService.cs`, `BlockedContextBuilder.cs`,
`RunnerSeatReleaseFixture.cs`, `FullCommandCounter.cs`. Keep private fixture helpers
in the existing admission test file; no new production test seam is required.

| In-flight work named by this dispatch | Collision / sequencing |
|---|---|
| CARD-1149/1150 S2 | Owns changes in `SessionReconciliationService.cs`; no planned overlap. Do not edit that file to accommodate a regression. |
| CARD-1121 S2 | Also touches `SessionReconciliationService.cs`; outside this scope. Any necessary change there waits for its owner and a separately commissioned task. |
| CARD-1151 | Touches `AgentTaskDispatcher*`; no planned overlap. Do not change dispatch, reconciliation, scheduling or repair behavior here. |

Refresh active ownership before Code. Any newly active writer of AgentTaskService,
the three named test files, or the owner documentation must be sequenced before the
overlapping slice. The supplied collisions are not evidence of current availability.
Do not replay any CARD-1144/1146/1147 slice or edit generated `docs/cards` files.

## Owner documentation changes

Replace `LocalGuidanceSentence` in both runtime/orchestration owners and its existing
pin with this exact sentence:

> Local 409 follow_up_agent_blocked guidance uses the same confirmed release-identity query as Reply; it recommends Reply for a current-attempt published park only when that query admits its linked release (CARD-1154).

Add and pin these two sentences in both owners:

> Confirmed park evidence is evaluated before local live-session advice; a confirmed park with mismatched release identity names neither Reply nor cancellation (CARD-1154).

> Confirmed-park guidance performs one database read per blocked follow-up, independent of the number of candidate parks; the existing session-liveness read is separate (CARD-1154).

Qualify the surrounding generic live/dead descriptions: they apply when no confirmed
park classification takes precedence; published-seat Reply advice requires Admitted.
Retain the CARD-1144 veto, CARD-1146 shared-query and CARD-1103 remote-precedence
sentences and pins. Replace the obsolete c1154 source anchor about the weak detector
with anchors for the Create consumer and its evidence order. Keep the separate weak
detector assertion, relabeled as the CARD-1144 safety-veto contract, if retained;
its weakness is no longer evidence of a local Create defect. Runtime behavior is
proven by V-1..V-4, not by these source-text pins.

Replace the one runtime known-limits sentence and its exact pin with:

> Known limits stay on CARD-1097 item 2 (resume reads the desktop checkout, not the runner mirror) and CARD-1104 for a remote parent with RunnerCwd.

Add CARD-1154 to that pin's closed-item exclusion list. Preserve all other default-off,
Working, occupancy, recovery and known-limit assertions. Do not broaden claims to
task-detail answerability; this plan's corrected surface is follow-up refusal.

## Verification design

### Fixture and assertion discipline

Use `RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true)`,
then real `BlockedTaskParkDeliveryTests.PublishAsync` and `StampAsync`. To enter the
local (non-remote-pool) branch with admissible identity, set only the fixture agent's
`IsPoolDelegate=false` after publication. Preserve the bound runner, session/store,
generation, settlement token/time and published source. Never use
`RetireLocalSessionAsync` as a positive control: it deliberately destroys identity.
No existing shared fixture behavior needs to change.

Public actions are `BlockedTaskParkDeliveryTests.FollowUpAsync` (real Create) and
fixture `AnswerAsync`/`TryAnswerAsync` (real Reply). For every refused follow-up assert
status/code, the blocked task ID, message classification, unchanged task count,
task status/attempt/revision, and no newly accepted answer, Replied event, old-session
queue input, launch or stop. Snapshot these after fixture setup, not before publication
(which legitimately releases the fixture seat). Read durable outcomes in fresh contexts.
Tests do not run a dispatcher to manufacture success.

For a mismatched current confirmed park with no answer pending, actual Reply must
return 409 `park_release_identity_mismatch`, retain Blocked and the attempt/revision,
and produce no answer ID, event, queue write, launch or stop. For an admitted park,
actual Reply must produce exactly one new Queued attempt and one durable full answer,
with one Replied event, no old-session input and no launch before dispatch. Incomplete
or unconfirmed receipts must not be asserted to veto all answers: existing
Reserved/Unresolved durable buffering is a different contract and stays unchanged.

### New behavior coverage

All four new methods live in
`tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs`.

| ID | Method | Worlds and required witnesses | TUnit results |
|---|---|---|---:|
| V-1 | `C1154_LocalReleaseIdentityParity` | Nine `[Arguments]` values listed below. Healthy shared-query result and public local 409 name this task's Reply. Flip one ledger field: direct exact and confirmed queries exclude it, local 409 names neither Reply nor cancel and explains mismatch, actual Answer vetoes without effects. Restore only that field, prove positive local guidance again, then actual Answer queues one new attempt. | 9 |
| V-2 | `C1154_LocalSessionIdentityParity` | Six `[Arguments]`: missing server session, wrong session RunnerId, wrong session RunnerStoreId, null task RunnerId, empty task RunnerId, null task AgentSessionId. Begin with a healthy local control; change only the named side. Shared exact/confirmed queries exclude the release; public local refusal and real Reply show the same mismatch/no-effects behavior as V-1. | 6 |
| V-3 | `C1154_LocalGuidanceEvidenceOrder` | Eleven independently reported cases in the next table. Pin positive, mismatch, None, live and remote precedence through public Create. Use real Answer for the admitted-park and ordinary live-no-park positives, and for confirmed-identity mismatch negatives. Other receipt/park-scope cases assert guidance only. | 11 |
| V-4 | `C1154_LocalGuidanceUsesOneStatement` | One test, five internal worlds: admitted, mismatched revision, missing session, no park, multiple current-attempt parks (admitted plus mismatched candidate). Use `FullCommandCounter` on the real service DbContext. Measure helper and public Create separately as below. No fixture queries inside either measured window. | 1 |

V-1's one-flip matrix changes the release ledger only. Its historical identity
fields are not foreign keys. Preserve all other identity and confirmation fields.

| Release field | Single corruption |
|---|---|
| TaskId | Another GUID |
| Attempt | Original + 1 |
| SessionId | Another GUID |
| AgentId | Another GUID |
| RunnerId | A distinct fixture runner string |
| RunnerStoreId | Another GUID |
| AcceptedStartedAt | Original + 1 second |
| SettlementRevision | Another GUID |
| SettledAt | Original non-null time + 1 second |

Assert direct query exclusion before Answer so a later transactional guard cannot
mask a missing comparison. Keep case-specific failure labels. V-2's store case is
a different non-null store, not a lone null: the latter violates
`CK_AgentSessions_RunnerBinding_AllOrNone` and would test fixture failure.

| V-3 case | Setup | Expected public follow-up |
|---|---|---|
| admitted-stopped | Exact real park; non-pool; Stopped projection | 409, existing published-seat text and task-specific Reply; actual Answer queues once. |
| admitted-running | Same, session status alone changed to Running | Same admitted message; receipt outranks stale liveness. Actual Answer queues once. |
| mismatch-stopped | Confirmed real park; release SettlementRevision flipped; non-pool | 409 mismatch text, neither Reply nor cancel; actual Answer veto. |
| mismatch-running | Previous case with session status alone Running | Same mismatch and veto; never generic live advice. |
| no-park-running | Fresh ordinary live Blocked fixture, parking disabled, non-pool | Existing 409 Reply-or-cancel text. Actual Answer stays on the same session/attempt, becomes Working, and queues the answer without a stop. |
| no-park-stopped | Fresh no-park fixture, Stopped projection, non-pool | Existing 409 cancel/re-send text; no published-seat claim or Reply command. |
| previous-attempt-stopped | Real park; change park.Attempt only to a prior attempt | Existing no-current-park dead-session 409, no Reply command. |
| unknown-outcome-stopped | Identity exact; Confirmed/ConfirmedAt retained, OutcomeCode unknown | ReleaseMismatch 409, no Reply or cancel; no claim about new-attempt admission. |
| missing-confirmed-at-stopped | Identity exact; ConfirmedAt null | None and existing dead-session 409, no published-seat Reply claim. |
| remote-admitted-running | Exact real park, pool flag retained, bound runner; Running projection | Existing 422 `follow_up_remote_pool_unsupported`, admitted Reply suffix, no local 409. Actual Answer queues once. |
| remote-mismatch-running | Same remote routing; release revision flipped | Existing 422 mismatch/fresh-task wording, no Reply command; actual Answer veto. |

Only change session status to model stale projections; retain StartedAt and store.
Use ordinary live fixture/transcript setup for no-park-running, not a physically
released park with its evidence deleted. No `Task.Delay`, real-provider launch or
retry is part of these witnesses.

V-4 uses the existing all-six-command-path `FullCommandCounter`; retain the complete
SQL list/count outside tracked evidence and print a bounded roster. At the helper
boundary require Total=1, a SELECT and zero writes. In a separate public Create
window count **all** commands, require exactly one command referencing park/release
evidence, no standalone release preload or duplicate park SELECT, zero writes,
and the correct 409. The normal liveness SELECT is allowed separately. Verify one
guidance command for the live case too (use the no-park world with a live session).
For one versus multiple parks, the entire public command count and normalized roster
must be equal; the mixed candidates must still return Admitted. Seed the additional
candidate with a distinct mismatched ledger identity, not a duplicate exact release
that would violate `SingleOrDefault`/uniqueness. Direct helper assertions alone do
not satisfy this row. Failures/cancellation must not be swallowed as authority.

### Existing tests to preserve or correct

R-1: `BlockedTaskParkReplyAdmissionTests.C1103_RemotePoolReplyNamesOnlyAdmittedRelease`
keeps admitted, stale and prior-attempt remote 422 behavior and actual positive Reply.
R-2/R-3: `C1146_ConfirmationParity` (10 cases) and `C1146_ParkScopeParity` (7) continue
to pin every confirmation and park-scope fence in the unchanged shared classifier.

R-4: in `BlockedTaskParkDeliveryTests.C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply`,
retain the explicitly unbound local setup and assert the new mismatch 409/no Reply
for both current-park observations. Preserve the prior-attempt fallback, later
aligned remote 422 positive, no-insert assertions, and all existing task-detail
assertions (that separate surface is not changed). R-5: in
`C1065_PinnedFollowupCannotStealParkedIdentity`, make its malformed local
ParkedPredecessor assert mismatch/no Reply/no cancel; keep the remote refusal and
real publication/Answer continuation worlds. These are intentional corrections of
defect-dependent expectations, not removal of coverage; V-1 supplies exact local positives.

R-6/R-7: retain `C1144_StaleParkNeverFallsBackToOldSession` and
`C1144_MarkReadPreservesConfirmedParkReply`. R-8: retain
`TerminalRunnerSeatReleaseTests.Reply_on_live_local_or_warm_session_is_unchanged`.
R-9/R-10/R-11: retain the three selected AgentTaskServiceIntegrationTests for an
ordinary blocked agent, a dead blocked session and a Working follow-up that queues.
R-12: retain `Working_session_keeps_ownership_and_visible_debt`.
R-13: update `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers`
with the owner sentences above, preserving its other safety assertions.

### Production mutation controls

All controls are **pending**, not run in this Plan and not authorization to resume
the paused Mutation stage. Separately commissioned SourceLanding Mutation records
fresh method green -> compiling production change -> intended assertion red -> full
restore -> fresh method green. Parameterized methods execute their bounded cases;
never run a class/suite per control. Zero tests, build/translation/constraint/setup
errors and timeouts are not red. Never mutate the test to obtain a production red.

| PC | Production change; intended detector | Exact method filter | Variants |
|---|---|---|---:|
| PC-1 | In local message selection accept `parkReply != None` as Admitted (the old weak-confirmation behavior). V-1's mismatch no-Reply assertion fails. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalReleaseIdentityParity` | 1 |
| PC-2a..i | Omit one ForAttempt identity comparison at a time, for the nine V-1 fields. That field's direct exclusion assertion must fail before Answer can mask it. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalReleaseIdentityParity` | 9 |
| PC-3a..e | Separately bypass the correlated session Any, its runner equality, or add a task-ID-only release fallback for exactly one of null runner, empty runner and null session. Each missing-value mutant must retain the other two refusals, making these three distinct reachable mutants. V-2's corresponding exclusion fails. Do not merely delete a redundant null guard and call it detected. The store comparison is already PC-2's store variant. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalSessionIdentityParity` | 5 |
| PC-4 | Move the generic local `if (sessionLive)` refusal above the Admitted/Mismatch arms. V-3's mismatch-running case must fail on Reply advice. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceEvidenceOrder` | 1 |
| PC-5 | Move remote-pool refusal below the local classification arms. V-3's remote cases fail their 422/type/code checks. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceEvidenceOrder` | 1 |
| PC-6 | Suppress the local ReleaseMismatch arm so it falls through. V-3's mismatch-running gets live Reply and mismatch-stopped gets cancel; both must fail. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceEvidenceOrder` | 1 |
| PC-7a..b | Add a redundant awaited release SELECT inside the classifier; separately add a second awaited classifier call at the Create consumer. V-4 catches helper Total=2 and public guidance count=2, respectively. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceUsesOneStatement` | 2 |

Total: **20 production variants**. These do not discharge the predecessor plan's
31 pending variants. An additional documentation control temporarily corrupts one
new owner sentence and uses only
`/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers`;
record it separately from production controls. Preserve labels, C/O/L provenance,
failed assertions/counts and restoration evidence in the assigned external evidence
root; SourceLanding makes no snapshot commits or pushes.

### Execution, evidence and cost

One isolated build, then each exact method filter below, after S1-S3 are committed
and pushed. The trailing method wildcard is intentional for pinned TUnit discovery;
inspect actual names and argument-expanded counts. No whole-Unit, namespace,
assembly, browser or live-provider run is authorized by this manifest.

Use the checkpoint tool once for this group:

```powershell
dotnet <isolated-tool-output>/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-08-card-1154-local-park-guidance-plan.md --after S1-S3 --expected-source-sha <committed-code-sha>
```

The standard source form is `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <plan.md>`.
If bootstrapping is necessary, build the tool once through
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1154-checkpoint-tool -- <build-command>`
to an isolated forward-slash output and invoke that DLL. Do not nest an outer slot
around checkpoint row drivers; they acquire their own slots. Every other build/test
driver uses the host gate. Await each run in the foreground; if wait returns 75,
keep waiting (calls no longer than 60 seconds for progress reporting). Exit 4 means
not-run/blocked, never retry unleased or use `-NoSlot`. Do not end with a live run.

Record each unedited CHECKPOINT line, tested SHA, full executed/passed/failed/skipped
counts, sourceState/dirty/buildSource, receipt path and every rerun. Freeze source
throughout the run. Any extra build or test needs a stated reason. Reproduce an
unexpected existing failure with that exact method at the recorded Code base before
attribution; do not loosen assertions/timeouts or add retries. For intentionally
corrected R-4/R-5, report the changed requirement rather than calling old assertions
flaky. Clean only producer-owned isolated output directories after all runs end.
Generated receipts/logs/TRX remain ignored. Code and Review run
`scripts/check-evidence-diff.ps1 -BaseRef <task-base> -HeadRef <pushed-sha>` over their
full assigned range. Neither this plan nor its static review claims tests are green.

Authoring estimate: **135 minutes**. Ordinary checkpoint floor: **55 minutes** from
the table, plus slot wait and any one-time tool bootstrap. Code allocation floor:
**190 minutes**. Mutation planning floor: 20 variants x 5 minutes + 5 for the doc
control + 15 for discovery/restoration = **120 minutes**, separately commissioned.
These are estimates, not measured execution times. Review/landing/activation are
separate work. The Plan dispatch's 45-minute budget is not the implementation budget.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1154/` | linux-pg-local-release-identity | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalReleaseIdentityParity*` | V-1 | all 9 argument cases, 0 failed/skipped | 9 | 10 | true |
| CP-2 | S1-S3 | CP-1 | linux-pg-local-session-identity | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalSessionIdentityParity*` | V-2 | all 6 argument cases, 0 failed/skipped | 6 | 5 | true |
| CP-3 | S1-S3 | CP-1 | linux-pg-evidence-order | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceEvidenceOrder*` | V-3 | all 11 argument cases, 0 failed/skipped | 11 | 7 | true |
| CP-4 | S1-S3 | CP-1 | linux-pg-public-guidance-sql | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1154_LocalGuidanceUsesOneStatement*` | V-4 | one method, all 5 worlds, 0 failed/skipped | 1 | 4 | true |
| CP-5 | S1-S3 | CP-1 | linux-pg-remote-precedence | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1103_RemotePoolReplyNamesOnlyAdmittedRelease*` | R-1 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-6 | S1-S3 | CP-1 | linux-pg-confirmed-receipt | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ConfirmationParity*` | R-2 | all 10 argument cases, 0 failed/skipped | 10 | 4 | true |
| CP-7 | S1-S3 | CP-1 | linux-pg-park-scope | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ParkScopeParity*` | R-3 | all 7 argument cases, 0 failed/skipped | 7 | 4 | true |
| CP-8 | S1-S3 | CP-1 | linux-pg-attempt-guidance | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply*` | R-4 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-9 | S1-S3 | CP-1 | linux-pg-pinned-followup | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/C1065_PinnedFollowupCannotStealParkedIdentity*` | R-5 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-10 | S1-S3 | CP-1 | linux-pg-stale-answer-veto | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_StaleParkNeverFallsBackToOldSession*` | R-6 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-11 | S1-S3 | CP-1 | linux-pg-mark-read-reply | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_MarkReadPreservesConfirmedParkReply*` | R-7 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-12 | S1-S3 | CP-1 | linux-pg-live-warm-reply | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Reply_on_live_local_or_warm_session_is_unchanged*` | R-8 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-13 | S1-S3 | CP-1 | linux-pg-live-blocked-followup | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/a_follow_up_onto_an_agent_parked_on_a_blocked_task_is_refused*` | R-9 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-14 | S1-S3 | CP-1 | linux-pg-dead-blocked-followup | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/names_cancel_when_the_blocked_tasks_session_is_dead*` | R-10 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-15 | S1-S3 | CP-1 | linux-pg-working-followup | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/a_follow_up_behind_a_working_task_still_queues*` | R-11 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-16 | S1-S3 | CP-1 | linux-pg-working-custody | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Working_session_keeps_ownership_and_visible_debt*` | R-12 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-17 | S1-S3 | CP-1 | linux-unit-owner-contracts | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers*` | R-13 | named method, 0 failed/skipped | 1 | 1 | true |
