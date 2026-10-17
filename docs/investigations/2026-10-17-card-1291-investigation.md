# CARD-1291: pre-child admission refusals are already on the keyed handoff path

Read on 2026-10-17 at `HEAD` `f01104c85c2c862ff9ca6e5388c58c415fb62590`, which is `origin/master`. No production file was edited. No test was executed in this task. The mechanism is the current source. The 2026-10-16 receipts remain the last execution, and they are not this SHA.

**Confirmed: the gap named on the card was closed by CARD-1281 S6a.** Blocker, overlap, and spend refusals run on the keyed handoff child path before a child row exists. Ordinary `POST /api/agent-tasks` still does not refuse an open blocker, still returns overlap as advice after commit, and has no spend flag. That split is what the plan binds. There is no remaining admission defect to implement, and a new red-first witness of these throws cannot go red.

## What the card still says

CARD-1291 (`a8df892b-c0e7-4e10-a45b-d7afb8c328e6`) is In Progress, master-only. Its body still says B-2 persisted caller delay and consumption, and that child admission lacks pre-child blocker, overlap, and spend refusals; that S0 checks only identity and links; that overlap is advisory; and that V-9, V-11, and the S6 wiring were deferred.

History: created from the B-2 deferral; Plan `cd89ada2` succeeded 2026-10-16 09:24; Investigate `00679518` succeeded 2026-10-16 09:44; this Investigate `9adeb707` was dispatched 2026-10-17 15:32. The description was not rewritten after S6a. The earlier outputs are `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md` (binding follow-up, task `024ec8fb`), `docs/superpowers/plans/2026-10-16-card-1291-pre-child-refusals-plan.md`, and `docs/investigations/2026-10-16-card-1291-pre-child-blocker-overlap-spend-refusals-in-admission.md` (landed as `56a1550eb`, on this master, not on `origin/rc/2026-10-15`). This note does not repeat their measurements. It re-reads admission after CARD-1303 (`6aaee1f83`) and CARD-1288.

B-2 is Code task `27d33f88-e1c4-4846-9d15-e6c7c67468c1`. Its evidence (`docs/superpowers/evidence/2026-10-15-card-1281-b2-caller-actions.md`) says the caller narrowed that task to writer and records, forbade editing `AgentTaskService.cs`, and deferred refusal-before-child. At that base, `FindScopeOverlapsAsync` was advisory and ran after commit, `CardBlockers` was not called from create, and `RequestChildAsync` was absent. Those sentences describe `27d33f88`, not current master.

## What admission does today

Two `CreateAsync` overloads share `CreateCoreAsync` (`server/Application/Services/AgentTaskService.cs`).

| Entry | Call | Handoff checks |
|---|---|---|
| Ordinary create | `AgentTaskEndpoints.cs:55` calls the 3-argument `CreateAsync` (`AgentTaskService.cs:251-254`), which passes `handoffActionId: null` | Not run |
| Keyed child | `PipelineHandoffActionEndpoints.cs:34-49` `POST /api/agent-tasks/{predecessorId}/handoff-actions/{actionId}/child` calls `RequestChildAsync`, which calls the 5-argument `CreateAsync` (`AgentTaskService.cs:247-249`) | Run |

`server/Program.cs:492` registers `PipelineHandoffActionService`. `server/Program.cs:1158` maps the endpoints. `server/Bundles/orchestrator.md` does not name `handoff-actions`. Live orchestrator creates still use the ordinary POST.

### Keyed path, in order

`CreateCoreAsync` (`AgentTaskService.cs:256`):

1. Spend, before any transaction or insert (`260-261`). `handoffActionId != null && !acceptSpend` throws `ConflictException` code `handoff_spend_held`. `ConflictException` is HTTP 409 (`server/Application/Exceptions/ConflictException.cs:8-13`).
2. Policy overrides on that same path (`262-264`) throw `handoff_override_held` when the request sets `IgnoreConcurrencyLimit`, `IgnoreSubscriptionQuota`, `IgnoreModelDisabled`, `IgnoreRoutingPin`, or `AllowUnauthenticatedProvider`. A handoff child cannot carry `-IgnoreConcurrencyLimit`.
3. The admission transaction starts at `1565-1567` when a handoff id is present. Card lock is `FOR NO KEY UPDATE` at `1578-1580` (CARD-1303). Handoff row lock is `FOR UPDATE` at `1584-1588`.
4. Identity and links, still before the three holds: scope `1589-1592` (`handoff_scope_changed`), digest `1593-1594` (`handoff_request_changed`), existing child `1595-1607` (returns that child; no second insert), predecessor `1609-1616` (`handoff_predecessor_changed`), workflow fence `1617-1629` (`handoff_workflow_changed`), stage `1630-1633` (`handoff_stage_held`).
5. Decision `1634-1635` (`handoff_decision_held`) when the locked card is `NeedsDecision`.
6. Blocker `1636-1638` (`handoff_dependency_held`).
7. Overlap `1639-1640` (`handoff_overlap_held`).
8. Recorded eligibility `1641-1642` (`handoff_not_eligible`), host budget `1643` (`RequireHandoffHostCapacityAsync` at `1892`), open-gate fallback with `ignoreConcurrencyLimit: false` at `1644-1646`, quota `1647-1649`.
9. Closed card `1651-1654`, for every implementation role, handoff or not: Done, Canceled, or archived throws `card_implementation_closed`. This is after the handoff holds and still before insert. A handoff onto a Done card that also has an open blocker returns `handoff_dependency_held`, not `card_implementation_closed`.
10. Concurrency gate inside the transaction `1674-1676`. Then `AdmitWorkspaceAsync` `1685`, `AgentTasks.Add` `1686`, Created event `1687`, `SaveChangesAsync` `1745`, child link `1746-1751`, commit `1753-1754`.

`git blame -L 1636,1640` is `1839210555a5e6cf84e11295e5e226b0749e7960` (`fix(card-1281): enforce recipient and handoff admission refusals`). That commit is an ancestor of `origin/master` and of `origin/rc/2026-10-15` (`5e5d5cc728949420647d15a932766e70755a6f01`). So is `0633527c62be63a8acedc35cb7bb6a967d06afdf` (guarded child admission) and `635e286627ade38635d1dff858a5216e02d07b1b` (route registration). CARD-1303 changed the lock and the benign fence around these lines; it did not move the throws to after `Add`.

### Ordinary path

With `handoffActionId` null, steps 1-8 do not run. Overlap is computed after commit at `1789` (`ScopeOverlaps: await FindScopeOverlapsAsync(task, ct)` with `refuseOnError` left false) and returned on the 201 body. The helper's own comment (`5046-5055`) says an overlap listing must never refuse a launch. `docs/agent-card-lifecycle.md:76` states the matching rule: explicit creation on a held card is not refused (OD-3); the hold is a pull rule. CARD-0100 OD-3 (`docs/superpowers/plans/2026-10-14-card-0100-card-relationships-plan.md:304-306`) left `409 card_blocked` out of that card.

`CreateMergeTaskAsync` and `CreateCommitTaskAsync` are outside this handoff overload. CARD-1288 left them without `card_implementation_closed`. They also do not take a handoff action id, so they do not run blocker, overlap, or spend holds.

## The three refusals

Plan binding is the B-2 paragraph in `docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md` (task `024ec8fb`): pass the action id into `CreateAsync`; the admission transaction revalidates blocker, cap, quota, placement, and spend; insert the child and the link together; a refusal persists an observation and does not acknowledge a child. V-9 and V-11 in that plan are the acceptance rows. They are scoped to the caller pilot, not to every `CreateAsync`.

### Blocker

Rule: a keyed child is refused when the locked card has a `BlockedBy` row whose blocker is open. Open is `CardBlockers.IsOpenBlocker` (`CardBlockers.cs:26`, built at `34-46`). Resolved is archived, Done, or Canceled (`CardBlockers.cs:18-19`). Any other status stays open. Data: the card id already on the task, the `BlockedBy` navigation, the blocker card's status and `ArchivedAt`. Place: inside the handoff transaction, after the card lock and the identity checks, before workspace admission and `Add` (`AgentTaskService.cs:1636-1638`). Code: 409 `handoff_dependency_held`. `RequestChildAsync` maps that code to observation reason `dependency` and stores the original code in `Detail` (`PipelineHandoffActionService.cs:165-184`), with `NextObservationAt` five minutes later. That timestamp does not retry or spend.

Witness: `PipelineSpeedIntegrationTests.C1281_AdmissionHolds` dependency arm (`PipelineSpeedIntegrationTests.Wiring.cs:67-98`) and the `dependency` variant of `C1281_B2HttpAdmissionHolds` (`PipelineSpeedIntegrationTests.Http.cs:29-97`). Filter: `/*/*/PipelineSpeedIntegrationTests/C1281_AdmissionHolds`.

Ordinary create is unchanged and must stay unchanged. Widening this throw to the 3-argument overload would contradict `docs/agent-card-lifecycle.md:76`.

### Overlap

Rule: a keyed child is refused when `FindScopeOverlapsAsync(task, ct, refuseOnError: true)` returns a non-empty list (`1639-1640`). The helper (`5057-5131`) returns null for `ReadOnly` or a specialist (`5060-5061`); selects other tasks that are Dispatched or Working, not ReadOnly, and not specialists (`5069-5073`); keeps the same repository key (`5083-5088`); skips `ScopeOverlapPolicy.Allow` (`5111-5112`); includes intersecting warn/serialise pairs and, when `SerialiseSharedWriters` is on, two Shared writers with no area intersection (`5090-5105`). With `refuseOnError: true`, a query or resolver exception propagates (`5126-5127`). With the default false, the same failure is logged and treated as no overlap.

Data: the prospective task's workspace, role, scope, and repo key, plus those fields on other open writers. The new task is not saved yet; the query excludes `t.Id != task.Id` (`5070`). Place: same transaction, immediately after the blocker check, before `Add`. Code: 409 `handoff_overlap_held`, observation reason `overlap`.

Witness: the `overlap` arm of `C1281_AdmissionHolds` and of `C1281_B2HttpAdmissionHolds`. Ordinary create still persists and returns advice. The six methods in `AgentTaskScopeOverlapCreateTests` (`a_shared_task_is_told_it_will_wait_behind_the_running_one`, `a_worktree_task_is_told_a_rebase_is_coming_and_against_which_branch`, `a_disjoint_scope_is_told_nothing`, `a_read_only_task_is_told_nothing_because_it_can_collide_with_nothing`, `an_undeclared_shared_writer_is_told_it_will_wait`, `a_running_task_in_another_repo_is_told_nothing`) expect a created task. Filter if that contract is touched: `/*/*/AgentTaskScopeOverlapCreateTests/*`.

### Spend

Rule: a keyed child requires explicit acceptance. `PipelineHandoffChildRequest.AcceptSpend` defaults false (`PipelineHandoffActionDtos.cs:28-29`). `RequestChildAsync` rejects `Trigger != "caller"` as `handoff_trigger_held` and `!AcceptSpend` as `handoff_spend_held` before it resolves `AgentTaskService` (`PipelineHandoffActionService.cs:127-128`). The inner overload defaults `acceptSpend` false and throws again as the first statement of `CreateCoreAsync` (`260-261`). Both are 409. Observation reason for spend, trigger, decision, and override is `decision` (`171`).

This is not the schedule gate. `ScheduleService` (`786-792`) refuses a Card schedule whose `Start` is Release or Spawn when `AcceptSpend` is false, by throwing `SpendUnacknowledgedException`: HTTP 422 `spend_unacknowledged` (`SpendUnacknowledgedException.cs:6-18`), before the schedule row is persisted. That path does not create a task and does not use `handoff_spend_held`. Copying the handoff default onto ordinary create would refuse every current `POST /api/agent-tasks`, because that body has no `acceptSpend` and the inner default is false.

Witness for the outer gate: the `spend` variant of `C1281_B2HttpChildRecovery` (`Http.cs:115-125`), which posts `acceptSpend: false` and expects `handoff_spend_held`, a null child link, and a durable observation after restart. Filter: `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpChildRecovery`. The HTTP test does not call the 5-argument overload with the outer guard skipped. The inner `if` is present in source; its own method-scoped witness was never added. That is a coverage limit, not an absent check. The 2026-10-16 plan already recorded it.

## V-9 and V-11 as the plan defines them

V-9 (`pipeline-speed-plan.md` acceptance row): one invalid dimension, every other dimension eligible; no child; a durable reason; the allowed control creates through the existing API. Dimensions named there: unapproved stage, open blocker, overlap, quota, stage cap, host cap, placement failure, tracker not activated, spend not accepted. No provider reroute and no cap override.

Current witnesses, all on `PipelineSpeedIntegrationTests`, not on the plan's original `PipelineHandoffPilotTests.C1281_AdmissionHolds` name:

| Dimension | Code | Where asserted |
|---|---|---|
| Stage | `handoff_stage_held` | `C1281_AdmissionHolds` and HTTP `stage` |
| Blocker | `handoff_dependency_held` | both `dependency` |
| Overlap | `handoff_overlap_held` | both `overlap` |
| Decision | `handoff_decision_held` | both `decision` |
| Host cap | `handoff_capacity_held` | `C1281_AdmissionHoldsHostBudget`; HTTP `host` |
| Quota | `subscription_quota_low` | HTTP `quota` |
| Stage cap | `concurrency_limit` | HTTP `stage-cap` |
| Placement | `validation_failed` (422) | HTTP `placement` |
| Spend | `handoff_spend_held` | `C1281_B2HttpChildRecovery` |
| Tracker trigger | `handoff_trigger_held` | same method, `tracker` variant |
| Override, including `IgnoreConcurrencyLimit` | `handoff_override_held` | HTTP `override` |

`concurrency_limit` is `ConcurrencyLimitException.ErrorCode` (`ConcurrencyLimitException.cs:12`), HTTP 409, thrown from `DelegationOpenGate.EnsureCanCreateAsync` (`DelegationOpenGate.cs:69-76`) inside the insert transaction. On the handoff path the ignore flag is already rejected at `262-264`, and the fallback gate passes `false` at `1646`. Ordinary create still honors `-IgnoreConcurrencyLimit` for a LegacyOpen absolute cap (`orchestration-loop.md` and `AgentTaskDtos.cs` on `IgnoreConcurrencyLimit`). A handoff 409 `concurrency_limit` is recorded as observation reason `capacity` (`PipelineHandoffActionService.cs:169`). Do not answer that 409 with `-IgnoreConcurrencyLimit` on the child POST; the pilot rejects the flag.

V-11 (same plan row): one child for the same predecessor, attempt, settlement, stage, and workflow key; a duplicate, a restart, or a lost create response returns that child; a stale workflow revision creates none. The planned method `PipelineHandoffPilotTests.C1281_CurrentHandoffOnce` does not exist. The behavior is `C1281_B2HttpChildRecovery` (`Http.cs:101-150`): foreign caller 403 `handoff_recipient_denied` with no action write from that caller; spend and tracker refusals leave `ChildTaskId` null across restart; a post-commit event fault returns 500 while the child is committed; two concurrent replays return that same id; a changed goal returns `handoff_request_changed`; the card's task count stays 1. The in-process early return of an already linked child is `AgentTaskService.cs:1595-1607`. Stale workflow is `handoff_workflow_changed` (`1617-1629` and HTTP `workflow`). Missing or unmarked handoff text is the completion parser's return-to-owner rule, not this admission method.

The plan's later S6 is release-train composition (`pipeline-speed-plan.md` around the S6 row and the "Do not commission S1-S10 as one Code task" / "No train code" close). It is not this card. The wiring the card calls "S6" is S6a: the endpoint, the service registration, and the admission transaction. Those are on both tips. `C1281_WiredRoundtrip` exercises the registered routes. Parent positive controls PC-14, PC-15, and PC-20 remain Mutation work on the umbrella, not a missing production branch.

## CARD-1288 and CARD-1321

`C1288_ClosedCardBindingForms` (`PipelineSpeedIntegrationTests.Admission.cs:168`) pins the lifecycle sentence and refuses ordinary Code create on a Done card by explicit id, by title token, and by the caller parent's card. Each arm expects 409 `card_implementation_closed` and zero tasks (`217-225`). It calls the 3-argument `CreateAsync`, so it does not enter the handoff holds.

Production `1653` also refuses Canceled and archived. This tree's test does not execute a Canceled arm or a follow-up arm. CARD-1321's commit `44c9d1eee8c7d90f53348853e46f5a5acb3f449c` is not an ancestor of `f01104c85`. Those arms are not on this master. They do not change the finding: the closed-card check is already before `Add`, and it is a different 409 from the three handoff holds.

## Slices

No new slice. Each one below is already on `origin/master`, and the production commits are also on `origin/rc/2026-10-15`. A new test that only restates these throws cannot go red. The card label is master-only; that does not create a master delta. Do not revert the rc copies as part of this card.

Ordered by the damage of re-implementing them on the ordinary create path, which is the mistake that would break live dispatch:

| Slice | If commissioned again | Narrow filter that already passes | Why it must not be widened |
|---|---|---|---|
| S-b overlap | Hard-refuse every intersecting writer | `/*/*/PipelineSpeedIntegrationTests/C1281_AdmissionHolds` | Ordinary creates today return 201 plus `ScopeOverlaps`. The six `AgentTaskScopeOverlapCreateTests` methods persist the task. A 409 on `POST /api/agent-tasks` stops orchestrator dispatch that is only being warned. |
| S-a blocker | `409 card_blocked` or `handoff_dependency_held` on every create | same method, dependency arm | OD-3 and `agent-card-lifecycle.md:76` keep the hold as a pull rule. Pipeline backlog already omits held cards. Explicit create on a held card succeeds. |
| S-c spend | Require `acceptSpend: true` on the 3-argument overload | `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpChildRecovery` | Default false. Ordinary bodies omit the flag. Schedule Release/Spawn is already 422 `spend_unacknowledged` and must keep that code. |
| S-d S6 wiring | Register the routes or add a second dispatcher | `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpAdmissionHolds` | `Program.cs:492` and `:1158` already map one child POST. A second writer can insert a second child for the same key. Train S6 stays uncommissioned. |

Safest rollout is no rollout. The hard gate is already limited to the keyed child POST. Ordinary create is the warn-first behavior: advice on the response, no throw, no new flag. A regression that deleted the handoff throws shows up as `C1281_AdmissionHolds` or `C1281_B2HttpAdmissionHolds` creating a child (historical S6a first red did that on the dependency arm). A regression that moved the throws onto the ordinary POST shows up as `AgentTaskScopeOverlapCreateTests` failing and as orchestrator `delegate.ps1` creates returning 409 during normal overlap.

## Not done, noted

No fix. Do not widen these three holds onto ordinary `CreateAsync`.

## Uncertainties

- This task did not re-run the filters. The last recorded execution is the 2026-10-16 checkpoint set at `f90b7167e804ef743bf8b3747ccd101e483809bb` (CP-1 `C1281_AdmissionHolds` 1/1, and the sibling HTTP rows in that investigation). That SHA is not an ancestor of `f01104c85`. The throw lines themselves still blame to `183921055`, which is an ancestor of this HEAD, and the statements still sit before `AgentTasks.Add`.
- The inner spend `if` has no test that bypasses `RequestChildAsync`. Removing only line 260-261 would leave the HTTP spend arm green.
- `C1281_CurrentHandoffOnce` was never added under that name. V-11 is the recovery method above, not a missing feature.
- CARD-1321's extra closed-card arms are not on this HEAD. Canceled is refused in production and not separately executed here.
- Resolved-blocker, every overlap-policy permutation, and a forced overlap query failure were not re-executed. `refuseOnError: true` is the fail-closed branch in source.
- Parent PC-14, PC-15, and PC-20 were not run. They stay with SourceLanding Mutation on CARD-1281.
