# CARD-1291: establish the remaining pre-child admission gap

Date: 2026-10-16. Plan task: `cd89ada2-cf39-453d-9706-1572e14454d3`.
Board: Antiphon. Card: `a8df892b-c0e7-4e10-a45b-d7afb8c328e6`, revision 1.
Assigned branch: `feat/card-task-cd89ada2`.
Inspected source: `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`.
Fetched master: `bc3168594e598cb4206b5e11f6d3aabc04561fd9`.

Reconciliation completed on 2026-10-16 by task `00679518`: the six scoped
checkpoints passed (11/11 executions), both source validators passed, and the
stored S6a landing confirms the guards are already on master. No remaining
admission defect was reproduced. See the [investigation report](../../investigations/2026-10-16-card-1291-pre-child-blocker-overlap-spend-refusals-in-admission.md).
The original plan and conditional slices below are preserved as history; S2/S3
were not activated. Current handoff: `next: none`, reconcile CARD-1291 as covered.

## Outcome and scope

The card's missing-implementation premise is stale. CARD-1281 S6a already added
the three requested refusals to the handoff path, registered its child endpoint,
and added service/HTTP tests. Those changes are ancestors of both the assigned
source and fetched master. The inspected admission, handoff service/DTO/endpoint,
Program and three core integration-test files have no difference between those
two source tips.

This plan binds a small evidence investigation before any further implementation.
The next stage is **investigate**, because writing the proposed guards again would
duplicate landed behavior. The commissioning brief requested `next: code`; the
Plan-stage contract instead requires investigation when the card's premise is
wrong. The investigation must establish a concrete remaining defect or reconcile
the card with the landed implementation. It must not infer that the whole D-3
pilot, B-3 completion wiring, or CARD-1281 is complete.

Scope is the explicit, keyed caller handoff:
`POST /api/agent-tasks/{predecessorId}/handoff-actions/{actionId}/child` ->
`PipelineHandoffActionService.RequestChildAsync` ->
`AgentTaskService.CreateAsync(..., handoffActionId, ..., acceptSpend)`.
"Before a child exists" means before child persistence, its Created event, its
handoff link, or workspace admission. An in-memory proposed task is not a
persisted child. This work does not add a dispatcher, change ordinary explicit
task admission, activate tracker/scheduled spending, or publish a release branch.
All eventual changes target master through normal task-branch Review/landing.

Sources read: the live card; [parent design and binding decisions](2026-10-15-card-1281-pipeline-speed-plan.md);
[S6a checkpoint manifest](2026-10-15-card-1281-s6a-checkpoints.md);
[S6a evidence](../evidence/2026-10-15-card-1281-s6a-wiring.md);
[B-2 evidence](../evidence/2026-10-15-card-1281-b2-caller-actions.md);
[project conventions](../../project-context.md), [orchestration/D-2](../../orchestration-loop.md#d-2-advisory-policy-card-1281),
[blocker semantics](../../agent-card-lifecycle.md#blocked-by-another-card-card-0100),
[HTTP operations](../../ops-http.md), and [checkpoint rules](../../testing-and-build.md).

## Ground truth

Line references are for the inspected source; method names are the durable anchors.

| Card assumption | What the code and record actually do | Consequence |
|---|---|---|
| S0 checks identity/linkage only; pre-child dependency refusal is absent | `server/Application/Services/AgentTaskService.cs:1635` queries `CardBlockers.IsOpenBlocker` inside the card/action transaction and throws `handoff_dependency_held`, before `AdmitWorkspaceAsync` and `AgentTasks.Add` at 1680. `CardBlockers.cs` treats Done, Canceled and archived blockers as resolved. | Verify this existing boundary; reuse the canonical blocker predicate. |
| Overlap remains advisory in AgentTaskService | Ordinary creation still returns advisory `ScopeOverlaps`. Handoff admission at 1638 calls `FindScopeOverlapsAsync(..., refuseOnError: true)` and throws `handoff_overlap_held` for a nonempty result. The helper at 5039 selects Dispatched/Working writers in the same repository, applies the area policy, and excludes ReadOnly/specialists/Allow intersections. | Preserve the handoff/ordinary distinction. Do not turn all task creation into a new hard gate. |
| Explicit spend is not checked before child insertion | `CreateCoreAsync` at 260 refuses a handoff when `acceptSpend` is false. `PipelineHandoffChildRequest.AcceptSpend` defaults false. `RequestChildAsync` independently checks spend and permits only `Trigger == "caller"`. | Existing service and HTTP checks are both relevant; HTTP coverage alone cannot prove the inner check's sensitivity. |
| B-2 only records consumption/delay | `RequestChildAsync` calls the real creation service in an owned scope, then persists 409/422 refusals in a separate transaction. It maps dependency/overlap/spend to dependency/overlap/decision, stores the original refusal code in Detail and schedules the next observation five minutes later. | A refusal must leave zero child rows and retain its observation after restart. A timer is an observation target, not authority to retry or spend. |
| V-11 and S6 caller wiring are absent | Creation locks card then action, validates predecessor/workflow/request identity, returns an existing linked child, and commits insertion/link together. `server/Program.cs:490` registers the action service; line 1152 maps its endpoints. | Retain duplicate/restart/lost-response recovery; no second child or new launch on replay. |
| No ordinary proof exists for V-9/V-11 | S6a source `fe6e54ee4fc8980ed579167cdb06586c89dd338f` records CP-26 2/2, CP-19 3/3, CP-21 1/1 and CP-28 24/24. The same report records 113 total executions, 111 passes and two base-confirmed census failures. | Historical evidence supports implemented behavior, but is neither a new run at this tip nor a clean Full gate. Do not erase the failures or transfer receipt credit between SHAs. |
| The original B-2 deferral is the latest state | B-2's component report deferred admission. Later commits `1839210555a5e6cf84e11295e5e226b0749e7960` and `0633527c62be63a8acedc35cb7bb6a967d06afdf` implement guarded admission; `635e286627ade38635d1dff858a5216e02d07b1b` registers the routes. | Reconcile the later S6a work before commissioning replacement Code. |

## Decisions

These decisions follow the existing caller-pilot scope; they introduce no new
operator policy and require no approval to write this plan.

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Investigate current evidence and remaining behavior first. The requested implementation is already on master. | Duplicate production guards or invent a regression to satisfy a Code handoff. |
| D-2 | Keep refusals scoped to keyed handoff admission. Ordinary explicit creates retain OD-3 blocker handling and advisory overlap. | Broaden every `CreateAsync` call, changing manual work and existing compatibility tests. |
| D-3 | Keep existing named 409 codes, explicit spend default false, caller-only trigger, and no policy override. | Infer spend acceptance from a receipt, eligible timestamp, prior attempt, or tracker activity. |
| D-4 | Preserve transactional child/link insertion and separate durable refusal recording. Keep lock order card -> action -> host budget -> concurrency gate. | Persist a child and cancel/delete it after discovering a hold, or acknowledge a held action without its reason. |
| D-5 | Use the bounded Integration checkpoint selection below. No whole Unit lane or full assembly, per CARD-1287. Existing tests that pass need no invented red. | Re-run the entire historical S6a manifest, unrelated correction/close suites, or the full delivery matrix for an unchanged admission path. |
| D-6 | Keep historical behavior, verification completeness and advisory findings separate under D-2. Pending Mutation and operating pilot obligations remain pending. | Relabel the historical incomplete gate Clean/Full, or call the whole D-3 handoff complete from these refusals alone. |

Placement: `GET /api/runner-defaults` and `GET /api/session-runners` both succeeded
on 2026-10-16. Defaults revision 2; one Linux lane was eligible and the other two
catalogue entries were unavailable. This is an observation, not a host pin or
Windows qualification. Re-read both at commissioning and read project-scoped
pipeline limits. Omit `-Runner` and `-Platform`: these tests are portable; request
an OS only for a demonstrated native requirement. Embed no fleet location.

## Slices and exact ownership

| Slice | Work and files | Tests / completion condition |
|---|---|---|
| S0: this Plan | Add only `docs/superpowers/plans/2026-10-16-card-1291-pre-child-refusals-plan.md`; commit/push the assigned task branch. | Documentation/static checks only. No test execution or runtime changes claimed. |
| S1: investigate current admission | Read `server/Application/Services/AgentTaskService.cs`, `PipelineHandoffActionService.cs`, `CardBlockers.cs`; `server/Application/Dtos/PipelineHandoffActionDtos.cs`; `server/Api/Endpoints/PipelineHandoffActionEndpoints.cs`; `server/Program.cs`. Read the two historical evidence reports above and the full stored S6a/Review outcome if available. Findings are in `docs/investigations/2026-10-16-card-1291-pre-child-blocker-overlap-spend-refusals-in-admission.md` on the investigation task's assigned branch. | Qualify available same-source receipts first; absent current-source proof, run CP-1..CP-6 once. Report exact source, row counts, codes, absence of persisted children and historical versus newly established results. |
| S2: conditional red-first repair | Only if S1 reproduces a remaining acceptance failure, add its isolated method to new `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.PreChildRefusals.cs`, reusing the existing partial class helpers. Commit/push the failing test before the fix. Amend this plan's executable table to the exact method(s) and committed slice before any new run. | One concrete failing assertion against the current baseline; missing symbols, bad fixtures, build failures and zero selections are not red. Proposed method names and witnesses are below. A green existing behavior needs no duplicate implementation. |
| S3: conditional production repair | Change only the implicated admission predicate in `AgentTaskService.cs`, or the request/refusal boundary in `PipelineHandoffActionService.cs`. Touch DTO/endpoint only if a reproduced input-binding/authorization defect requires it. `Program.cs` is relevant only if current composition is actually absent. Update `docs/antiphon-api.md`/`docs/orchestration-loop.md` only for a changed public contract; no schema/migration/bundle work is planned. | Same exact red method green, plus only its affected rows below. Preserve atomicity and ordinary-create compatibility. Commit/push each meaningful slice; ordinary Review then normal master landing. |

S1 should measure the following, rather than ask Code to assume a gap:

1. Current-source method/receipt coverage for each dependency, overlap and spend
   refusal, including exact HTTP problem code and fresh database child count.
2. Persistence order: no child, Created event, child link or workspace admission
   before a refusal; durable classified observation after rollback/restart.
3. Recovery: identical request returns one committed child across concurrent
   calls, restart and lost response; changed digest and stale identity refuse.
4. The uncovered distinctions: HTTP tests check the outer spend guard, while the
   direct-create helper always supplies `acceptSpend: true`; spend HTTP tests
   check a null link but do not count task rows immediately inside each held arm.
   Determine whether additional proof of the inner guard is needed. These are
   coverage limits, not proof that production admission is absent.
5. Overlap semantics are an active-writer snapshot. Queued writers and races on
   different cards are not proved by the existing snapshot tests. Do not silently
   turn CARD-1291 into a repository reservation redesign; report a reproducible
   contract violation separately if one is found.

If S1 finds no remaining gap, report this card as covered by the listed S6a
commits and return the evidence to the caller for card reconciliation. Card edits
belong to the board, never generated `docs/cards/` files. If a gap is reproduced,
the next Code brief must name that gap and its red-first method, replacing the
obsolete blanket claim that admission is missing.

## Verification design

This brief asks for executable checkpoints and a Code handoff, so verification
design is included. Its first use is the bounded S1 investigation; it does not
authorize conditional S2/S3 before a remaining defect is established.

All rows use the **portable Integration lane**, isolated migrated PostgreSQL and
the existing `C544World` / `PilotHost` fixtures. Preserve their process limiter and
serialization. `WiringFactory` uses the isolated test runner and removes real
background activity. No production child, session, tracker action or spend is
needed. The existing method loops remain one TUnit execution each; scenario
counts are not `Min` counts.

| ID | Exact existing witness | Required observation and evidence limit |
|---|---|---|
| V-1 | `PipelineSpeedIntegrationTests.C1281_AdmissionHolds` in `.Wiring.cs` | Dependency/overlap/decision/stage each returns its named conflict and fresh DB has no child/link. Four internal scenarios, one execution. |
| V-2 | `PipelineSpeedIntegrationTests.C1281_B2HttpAdmissionHolds` in `.Http.cs` | Twelve independently seeded hold variants use the actual endpoint; HTTP codes, zero child/link and retained refusal/next-observation survive HTTP restart. One execution. |
| V-3 | `PipelineSpeedIntegrationTests.C1281_B2HttpChildRecovery` in `.Http.cs` | Spend and tracker trigger refusals; authorized control; committed-response loss; concurrent replay returns same child; changed digest refuses. One execution. Do not claim it independently detects removal of the inner spend check. |
| V-4 | `PipelineSpeedIntegrationTests.C1281_B2AtomicChild` in `.cs` | Concurrent first create, restart, stale identity and faults at link/commit/post-commit yield one child or rollback with no link. One execution. |
| R-1 | `AgentTaskScopeOverlapCreateTests` in same-named file | All six existing cases preserve ordinary Shared/Worktree advice, disjoint/read-only/foreign-repository behavior and undeclared shared writer handling. |
| R-2 | `PipelineSpeedIntegrationTests.C1281_WiredRoundtrip` in `.Http.cs` | Real Program registration, complete caller receipt and persisted child roundtrip, with zero real runner launches. One execution with busy/eligible internal variants. Its existing correction arm is an unavoidable small co-selected fixture; no additional B-1 suite is selected. |

Conditional red-first tests, only for a reproduced missing behavior:

| Proposed method in `.PreChildRefusals.cs` | Arrange and assertion that must fail before its fix |
|---|---|
| `C1291_DependencyRefusesBeforeChild` | A valid eligible action with an open blocker; call actual admission. Require `handoff_dependency_held`, no inserted child/Created event/link, and no workspace admission. A separately arranged resolved blocker is the allowed control. |
| `C1291_OverlapRefusesBeforeChild` | A valid action and same-repository active intersecting writer, with other gates eligible. Require `handoff_overlap_held` and the same absence assertions. Preserve separate warn/serialise cases and disjoint/Allow controls if the reproduced defect concerns those policies. |
| `C1291_SpendRefusesBeforeChild` | Call the handoff `AgentTaskService` overload directly with omitted/false acceptance, bypassing the outer service guard; separately exercise HTTP omitted/false input with fresh task/event counts. Require `handoff_spend_held` and no child effects; true is the allowed control. If it already passes, record coverage only, not regression red. |

The historical S6a first red explicitly observed dependency admission creating a
child; its loop stopped there. That observation does not prove individual overlap
or spend sensitivity. Parent PC-14/PC-15/PC-20 and S6a missing-control discovery
remain with their bound SourceLanding Mutation owner. Bind each to one exact
method and compile a defect in the corresponding production guard; require its
expected assertion red and the identical restored method green. Removing only the
inner spend guard requires the direct-overload witness above, because the outer
HTTP guard masks it. Do not run deliberate mutants in Plan/Code or report pending
PCs as passed.

No changed session waits for input. A held handoff is persisted data, not a new
Blocked worker. The existing five-minute `NextObservationAt` does not release a
seat or automatically create work. Parking remains disabled by default; an
existing input-waiting session has no automatic release deadline (CARD-1083).

Run the checkpoint tool on the committed S1 source, through the host build slot:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1291-admission-evidence -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1291-tool/ -- run --plan docs/superpowers/plans/2026-10-16-card-1291-pre-child-refusals-plan.md --after S1 --expected-source-sha <full-committed-sha> --serial
```

Await the run and any `wait <run-id> --max-wait 45s` continuations to terminal exit,
including repeated exit 75. Keep source frozen and record the one necessary gated
tool bootstrap separately. Each checkpoint takes its own slot; do not wrap the
row driver in another slot wrapper. Preserve unedited CHECKPOINT lines, exact TRX
rosters and source/build receipts. Zero tests, missing provenance or slot timeout
is incomplete, never green. Generated payloads stay ignored. Remove only owned
alternate outputs, including the tool bootstrap. Code/Review runs the full
base..HEAD evidence-diff guard.

Estimated ordinary cost is **19 minutes**, including one isolated test build and
six row startups; slot waits and a necessary tool bootstrap are additional.
There are **11 expected TUnit executions**, not the many internal scenario count.
No execution occurred in this Plan task. Historical same-source evidence may
replace a fresh run only through deterministic receipt validation and an
append-only audit under D-2; similarity to an older source is insufficient.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1291-admission/` | integration-service-refusals | `/*/*/PipelineSpeedIntegrationTests/C1281_AdmissionHolds` | V-1 | exact named method, 0 failed/skipped | 1 | 6 |
| CP-2 | S1 | CP-1 | integration-http-refusals | `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpAdmissionHolds` | V-2 | exact named method, 0 failed/skipped | 1 | 3 |
| CP-3 | S1 | CP-1 | integration-spend-recovery | `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpChildRecovery` | V-3 | exact named method, 0 failed/skipped | 1 | 2 |
| CP-4 | S1 | CP-1 | integration-atomic-child | `/*/*/PipelineSpeedIntegrationTests/C1281_B2AtomicChild` | V-4 | exact named method, 0 failed/skipped | 1 | 2 |
| CP-5 | S1 | CP-1 | integration-ordinary-overlap | `/*/*/AgentTaskScopeOverlapCreateTests/*` | R-1 | all six methods, 0 failed/skipped | 6 | 2 |
| CP-6 | S1 | CP-1 | integration-program-wiring | `/*/*/PipelineSpeedIntegrationTests/C1281_WiredRoundtrip` | R-2 | exact named method, 0 failed/skipped | 1 | 4 |

## Risks and handoff

- Existing S6a proof includes inherited census failures and pending PCs. Its
  later Review/repair disposition must be read before claiming a clean gate.
  This plan neither reruns those unrelated guards nor waives their historical
  outcome. The latest evidence report is at commit
  `b6fdedc13fc46baddc22206c5c34003a93d3cd5b` in this checkout.
- The name `C1281_AdmissionHolds*` also selects the host-budget test. CP-1 uses
  the exact method intentionally; count one. The HTTP method already includes
  host, stage-cap, quota and placement regression arms.
- Source inspection proves placement of guards, not behavior under every race or
  deployment activation. Record a running server SHA only if operational
  activation is investigated; no restart or live spending is authorized here.
- B-3 close registration, D-3 operational timing cohorts, full delivery recovery
  and parent Mutation obligations are separate. Do not close the parent based
  on this card's admission evidence.

Immediate handoff: investigate the stale CARD-1291 premise using the exact source
anchors and CP-1..CP-6, first reconciling the stored S6a evidence. Return either a
reproduced remaining acceptance gap with its narrow red-first test, or evidence
that the caller can use to mark this card covered. Conditional Code receives only
that reproduced gap and the amended exact checkpoint manifest. Land this plan
through the normal caller-owned flow to master; never push master directly.
