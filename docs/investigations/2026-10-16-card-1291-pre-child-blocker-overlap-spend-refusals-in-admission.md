# CARD-1291: pre-child blocker/overlap/spend refusals in admission

Date: 2026-10-16. Investigation: `00679518-b90a-488a-8395-ba3202d5fe64`.
Board: Antiphon (`8988ca03-7414-47ad-b0b6-51556c701703`).
Card: `a8df892b-c0e7-4e10-a45b-d7afb8c328e6`, observed revision 4.
Branch: `feat/card-task-00679518`. Target: master only.

## Outcome

**Confirmed: CARD-1291 is covered by landed S6a.** Its missing-implementation premise
is stale. Source reconstruction, stored landing/Review records and fresh execution
of all six checkpoints establish the requested pre-child dependency, overlap and
spend refusals. **11 passed, 0 failed, 0 skipped; both source validators passed.**
No remaining acceptance defect was reproduced, so no red-first repair or new
Backlog card is warranted. The caller can reconcile this card as already covered.
This does not complete parent CARD-1281, D-3 operating acceptance, or pending
Mutation obligations. Next stage: **none** (already implemented on master).

## Source and evidence identity

- Actual verification source: `f90b7167e804ef743bf8b3747ccd101e483809bb`, the assigned
  committed plan on production/test parent `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`.
- Fetched master: `bc3168594e598cb4206b5e11f6d3aabc04561fd9`.
- `git diff --name-only <tested-source> origin/master -- <paths>` returned no
  differences for AgentTaskService, PipelineHandoffActionService, CardBlockers,
  PipelineHandoffActionDtos, PipelineHandoffActionEndpoints, Program,
  PipelineSpeedIntegrationTests (`.cs`, `.Wiring.cs`, `.Http.cs`) and
  AgentTaskScopeOverlapCreateTests. This establishes equality of those inspected
  files; it does not transfer receipt credit to a different full source SHA.
- All three landed implementation commits below are ancestors of fetched master.
  No production code, test code, runtime setting, service or card was changed here.
- Raw checkpoint evidence: `.antiphon/checkpoints/20261016-092716-9d79/`.
  Retained evidence: `/work/repos/antiphon/.git/antiphon/investigation-evidence/00679518/`.
  The final inventory binds raw evidence by SHA-256. Generated payloads are not committed.

## Confirmed mechanism and chronology

The card was created on 2026-10-15 at 13:40:41.329306Z from the B-2 deferral. Its
current description still says S0 only validates identity/linkage and overlap is
advisory. Subsequent S6a work changed the keyed handoff admission path while
retaining ordinary creation's advisory overlap behavior. Thus an accurate earlier
component deferral became an inaccurate statement of current master.

| Stored evidence | Observation |
|---|---|
| CARD-1291 row above; `card-1291.json` and `card-1291-history.json` | Description still requests missing guards; revision 1 records the master-only label, later revisions reflect task dispatch/settlement. |
| Commit `1839210555a5e6cf84e11295e5e226b0749e7960` | Adds recipient and handoff admission refusals. |
| Commit `0633527c62be63a8acedc35cb7bb6a967d06afdf` | Adds guarded child admission, explicit spend, and refusal persistence. |
| Commit `635e286627ade38635d1dff858a5216e02d07b1b` | Registers guarded correction and caller handoff routes. |
| Code task `5cdc89c1-b000-4180-a468-4a00924b271d`, landing operation `7ceae9a6-5e94-4cca-8e81-fa8f915ad2ff` | Publication `Landed`, destination `refs/heads/master`, source `a835989e3abe7caec300c4b1ca4db1acd0b77752`, verified/remote `b6fdedc13fc46baddc22206c5c34003a93d3cd5b`, remote confirmed 2026-10-15 17:57:28.090211Z. |

The task, Review and landing rows were read through the documented HTTP API and
retained as `task-5cdc89c1.json`, `task-a14f814f.json`, `task-2274b6da.json` and
`s6a-review-rows.json`. These are stored task/result/landing records, not an
inference from a healthy running server. No activation claim is made.

## Current admission path

Line references below bind to the actual verification source.

| Boundary | Source evidence and behavior |
|---|---|
| Real endpoint | `server/Api/Endpoints/PipelineHandoffActionEndpoints.cs:34` maps the keyed child POST and line 49 calls RequestChildAsync. `server/Program.cs:490` registers the service; line 1152 maps the endpoints. |
| Explicit spend | `server/Application/Dtos/PipelineHandoffActionDtos.cs:28` defaults AcceptSpend to false. `PipelineHandoffActionService.cs:127` rejects a non-caller trigger and line 128 rejects false acceptance before resolving the create service. `AgentTaskService.cs:247` also defaults the internal overload to false; line 260 rejects it as the first operation of CreateCoreAsync, with `handoff_spend_held`. |
| Dependency | `AgentTaskService.cs:1635` queries the card's BlockedBy relationships and throws `handoff_dependency_held`. `CardBlockers.cs:18` defines resolved as archived, Done or Canceled; line 34 builds the open-blocker expression. |
| Overlap | `AgentTaskService.cs:1638` calls FindScopeOverlapsAsync with refuseOnError true and throws `handoff_overlap_held` on a nonempty result. Lines 5042, 5051, 5065 and 5093 respectively exclude ReadOnly/specialists, select Dispatched/Working writers, match repository, and exclude Allow policy. Ordinary creation still returns advisory ScopeOverlaps at line 1784. |
| Before child effects | `AgentTaskService.cs:1562` starts the transaction, lines 1577 and 1583 lock card then action. Checks above precede worktree-base preparation at 1674, workspace admission at 1680, AgentTasks.Add at 1681, and Created event at 1682. Child save/link/commit occur at 1740, 1744 and 1749. No earlier child save/admission exists in this method. |
| Durable refusal | `PipelineHandoffActionService.cs:141` owns/disposes the create scope; lines 146-150 record a 409/422 only after rollback. Lines 157-188 use a separate card/action transaction, skip relabeling an existing child, map dependency/overlap/spend to dependency/overlap/decision, retain the exact code in Detail, and set NextObservationAt to now + five minutes. This timestamp grants no retry/spend authority. |
| Idempotence | `AgentTaskService.cs:1592` rejects a changed digest and line 1594 returns the already linked child. `PipelineHandoffActionService.cs:130` provides the same committed-child recovery before new creation. Child/link share one transaction. |

## Fresh narrow verification

No local checkpoint report existed for this source. Historical evidence has a
different SHA, so none was counted as new current-source execution. The inherited
plan's CP-1..CP-6 were selected exactly once, serially, from its committed S1 table.
One isolated test build is shared by all six rows. The checkpoint-tool bootstrap
was the only extra build, through build-slot.ps1: granted, waited 0s, 9s held,
0 errors and four nullable warnings. No whole Unit lane, assembly, deliberate
mutation or production launch ran. No timeout or assertion was changed.

Run `20261016-092716-9d79` ended **GREEN, exit 0**. Reported wall time was
737.5103144 seconds (12m17s), including one test build of 250.2608283 seconds.
The build and all rows had granted slots; CP-1 waited 45 seconds and the other
rows waited zero. Transient owner-read HTTP 502/timeouts were logged and recovered;
no ownership or slot guard was bypassed. Final receipt cleanupRefusals is empty.

| Checkpoint | Executed | Passed | Failed | Skipped | Row wall seconds |
|---|---:|---:|---:|---:|---:|
| CP-1 | 1 | 1 | 0 | 0 | 115.932 |
| CP-2 | 1 | 1 | 0 | 0 | 66.833 |
| CP-3 | 1 | 1 | 0 | 0 | 60.866 |
| CP-4 | 1 | 1 | 0 | 0 | 61.463 |
| CP-5 | 6 | 6 | 0 | 0 | 63.027 |
| CP-6 | 1 | 1 | 0 | 0 | 99.558 |
| Total | 11 | 11 | 0 | 0 | |

The standalone script and checkpoint tool validators both exited 0 for exactly
CP-1..CP-6, printing the line retained below. All rows have dirty=0,
sourceState=clean and buildSource=verified. Independent TRX inspection joined
Results.testId to TestDefinitions/TestMethod and matched each exact class/method
roster, including all six CP-5 methods; `trx-roster.txt` retains names and times.
The checkpoint executor was awaited to terminal exit/death, removed its test
output and shadow tool copy, and the two task-owned bootstrap output directories
were removed after validation. No run remains in flight.

The test methods read fresh EF contexts against migrated test PostgreSQL, not
production data. C544World creates a cloned database (`C544World.cs:157` and
`TestDbFixture.cs:65`); ordinary overlap cases use the test-session shared store
(`AgentTaskScopeOverlapCreateTests.cs:209`). Database row counts below are the
executed test assertions; this investigation did not retain or claim live product
child rows.

| Row | Exact witness and database/HTTP assertions |
|---|---|
| CP-1 | `PipelineSpeedIntegrationTests.Wiring.cs:67`: four separately seeded dependency/overlap/decision/stage scenarios. Named ConflictException, ChildTaskId null, zero tasks on the card except the predecessor (lines 94-98). |
| CP-2 | `PipelineSpeedIntegrationTests.Http.cs:29`: twelve hold scenarios; eleven 409 responses and one placement 422, exact problem codes, restart, null child link, exact retained refusal Detail, non-null NextObservationAt and zero card children (lines 86-96). |
| CP-3 | `.Http.cs:101`: foreign caller 403; false-spend and tracker-trigger 409; null child link and durable observation across restart. Authorized creation reaches a post-commit event fault (500); concurrent retries after restart return the committed child; changed digest refuses; final fresh count is exactly one (line 150). |
| CP-4 | `PipelineSpeedIntegrationTests.cs:29`: concurrent first requests and restart return the same child, seven stale identity variants refuse, initial fresh child count one; link/commit fault cuts leave it one with no new link, post-commit cut leaves two and recovers the second child (lines 61-86). |
| CP-5 | `AgentTaskScopeOverlapCreateTests.cs:30`: six ordinary-create methods cover Shared serialise advice, Worktree warn advice, disjoint, ReadOnly, undeclared Shared and foreign repository. Each creates a persisted task; overlap remains advisory. |
| CP-6 | `.Http.cs:161`: real Program wiring with eligible/busy caller variants. `.Http.cs:276` requests a child, verifies its link/readback and five-minute request accounting; line 283 asserts no runner launch attempts. |

The no-Created-event and no-workspace-admission conclusion is reconstructed from
the inspected control-flow ordering. The selected negative tests directly measure
child/link absence; they do not separately count Created events or instrument
workspace calls. Scenario counts inside loops are not extra TUnit executions.

## Historical verification remains historical

[S6a's committed report](../superpowers/evidence/2026-10-15-card-1281-s6a-wiring.md)
records the dependency assertion failing before the guard (lines 114-125): run
`20261015-150306-35a6`, source `b30cc6d3b1c366c8db81a510d7bbd60fa9191fbb`,
CP-26 1 executed / 1 failed because a child was created. The loop stopped at that
first case; it does not establish independent overlap or spend sensitivity.

At `fe6e54ee4fc8980ed579167cdb06586c89dd338f`, Code later executed 113 tests:
111 passed and two inherited census failures. The full changed class passed 24/24.
The two exact failures also failed at the unchanged base; receipt validation
returned cleanup_refused. Those facts and costs are not rewritten here.

Review `a14f814f-5087-4f73-950d-88065a3d2f14` repeated 113/111/2, found no S6a
regression/fail-open, and confirmed the same failures on master. Its stored row
`f2253ca6-c8db-4a42-a619-dab8d5bdda4f` says Clean, Interim scope,
reviewedSourceClean false. Later Review `2274b6da-b9b2-44e5-9559-f1425429a9d8`
ran the eight previously green rows at `a835989e3abe7caec300c4b1ca4db1acd0b77752`
(run `20261015-172205-d0ad`, 45/45, wall 25m58s) and reported both validators valid.
It excluded CP-27's two inherited failures. Its stored approval row
`c32f653c-49e2-4be9-aed4-e8ee266ad3ba` says Clean, Final/Full,
reviewedSourceClean true, while its report explicitly caveats combining that pass
with the earlier CP-27 execution. The landing used that row. Recording this later
approval does not turn the original red gate green or freshly validate its files.

The inherited failures were
`DelegationHarnessCensusTests.RuleC_reply_harnesses_register_git_via_helper_or_BridgeQueueHarness`
and `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`.
They are outside this card's six-row selection and were not rerun here.

## Remaining uncertainty and scope limits

- The inner spend check and omitted AcceptSpend default are confirmed by source;
  CP-3 exercises explicit false at the outer guard. The direct helper always passes
  true (`PipelineSpeedIntegrationTests.cs:154`). CP-3 does not count task rows
  immediately within each held arm, although its final child count is one. These
  are test-sensitivity limits, not a reproduced admission defect. Bound parent
  SourceLanding Mutation still owns independent spend/overlap/dependency controls.
- Overlap is a snapshot of Dispatched/Working writers. Queued writers and races
  between different cards are not proved by these tests. No repository-reservation
  redesign or broader overlap contract is inferred from CARD-1291.
- Resolved-blocker, every overlap-policy permutation, and fail-closed query-error
  behavior are source-inspected rather than newly isolated by this selection.
- No production activation, timing cohort, B-3 closure, full parent delivery matrix,
  Windows/native qualification, full-suite result or PC-clean status is claimed.

## Not done, noted

No fix proposed or implemented; reconcile CARD-1291 as covered by landed S6a and keep parent verification/Mutation obligations with their existing owners.

## Reproduction

From the committed plan/source, bootstrap the checkpoint tool through
`scripts/build-slot.ps1`, then invoke its already built DLL (each checkpoint owns
its own build slot):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1291-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1291-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1291-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-16-card-1291-pre-child-refusals-plan.md --after S1 --expected-source-sha f90b7167e804ef743bf8b3747ccd101e483809bb --serial --max-wait 45s
dotnet tools/Antiphon.Checkpoints/bin-c1291-tool/Antiphon.Checkpoints.dll wait <run-id> --max-wait 45s
```

Await every exit-75 continuation to a terminal result; preserve its actual SHA.
The report-only reconciliation commit is not the tested SHA. Future runs need
fresh producer-owned outputs and their own exact committed source SHA.

## Unedited checkpoint and validation lines

```text
CHECKPOINT CP-1 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=ok filter=/*/*/PipelineSpeedIntegrationTests/C1281_AdmissionHolds executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=reused filter=/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpAdmissionHolds executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=reused filter=/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpChildRecovery executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=reused filter=/*/*/PipelineSpeedIntegrationTests/C1281_B2AtomicChild executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=reused filter=/*/*/AgentTaskScopeOverlapCreateTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=f90b7167e804ef743bf8b3747ccd101e483809bb build=reused filter=/*/*/PipelineSpeedIntegrationTests/C1281_WiredRoundtrip executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-00679518/.antiphon/checkpoints/20261016-092716-9d79/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=f90b7167e804ef743bf8b3747ccd101e483809bb sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=f90b7167e804ef743bf8b3747ccd101e483809bb rows=6
```

--- next stage ---
next: none
handoff: Reconcile CARD-1291 as covered by landed S6a; preserve the original inherited census failures and parent Mutation/operating obligations. This report and the inherited plan contain the master-only evidence.
artifact: docs/investigations/2026-10-16-card-1291-pre-child-blocker-overlap-spend-refusals-in-admission.md
