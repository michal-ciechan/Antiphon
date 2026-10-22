# CARD-1430 S3 (task 5d1d84e4): release R before the baseline capture

Code stage, 2026-10-22, runner mirror `/work/worktrees/task-5d1d84e4`, branch `feat/card-task-5d1d84e4`,
base `6c1b0704b8b078d26adb359f1d7e9fa28def2c19`. Plan: `docs/superpowers/plans/2026-10-22-card-1462-option-a-desktop-land-cost-plan.md`
(on master as `bb43ec352`; not carried on this branch to avoid an add/add conflict). Landing owner: this task (5d1d84e4).

## Outcome

Done. `AgentTaskDispatcher.DispatchOneAsync` now disposes the dispatch lease R right after the SourceLanding
`ValidateVerificationAsync` and before `CaptureProgressBaselineAsync`, for Worktree claims. Red witness committed and
run red (CP-9), fix committed and CP-10 green at the tested SHA. Both commits merge cleanly onto `origin/master`
and cherry-pick cleanly onto `origin/rc/2026-10-20` by themselves.

| Commit | What |
|---|---|
| `b9230ddf88057503dfa6bf9d0e287b5481b99d75` | test(CARD-1430): `DispatcherLeaseSpanTests` (3 methods) + `TestHelpers/LeaseSpanRecorder.cs`; red at this commit |
| `bceeeb21ea302cb6bfcff234d746a0de76c1cc04` | fix(CARD-1430): the release in `server/Application/Services/AgentTaskDispatcher.cs` (+ acquire-site comment); tested SHA for CP-10 |

Files: `server/Application/Services/AgentTaskDispatcher.cs`, `tests/Antiphon.Tests/Application/DispatcherLeaseSpanTests.cs`,
`tests/Antiphon.Tests/TestHelpers/LeaseSpanRecorder.cs` (recording decorators over the production
`RepositoryMutationLease` and `TaskProgressGit`; no fake git).

## Checkpoints (closed list CP-9, CP-10; tool runs; lane server2 = this mirror; no -Runner/-Platform)

Placement read before running: `GET /api/runner-defaults` globalRunnerId=server2; `GET /api/session-runners`
desktop (windows), server2 (linux), server2-temp.

`import --plan` validation before the first row (tool built to `bin-c1430-tool/`): the full 29-row table of the
master plan imports (`imported 29 rows`) with one advisory `WARNING CP-4: names more than four land classes`;
the CP-9/CP-10 extract imports (`imported 2 rows`). No table defect, so no plan amendment was needed.

Run `20261022-081423-a4e8` (CP-9 at the test commit), unedited lines:

```
CHECKPOINT CP-9 commit=b9230ddf88057503dfa6bf9d0e287b5481b99d75 build=ok filter=/*/Antiphon.Tests.Application/DispatcherLeaseSpanTests/C1430_lease_is_released_before_the_baseline_capture executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5d1d84e4/.antiphon/checkpoints/20261022-081423-a4e8/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=b9230ddf88057503dfa6bf9d0e287b5481b99d75 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=360s build=219.5490509s startup=65.7156237s testsWall=9.6415226s teardown=1.2816212s hostWall=76.6387762s
FAILED Antiphon.Tests.Application.DispatcherLeaseSpanTests.C1430_lease_is_released_before_the_baseline_capture (CP-9) ShouldAssertException: released     should be less than 5     but was 7  Additional Info:     baseline pin recorded before the lease dispose: lease:acquired:dispatch > owns:dispatch:true > owns:dispatch:true > progress:rev-parse:HEAD > progress:observe:refs/heads/feat/card-task-baf1fd7b > progress:pin:primary-local > lease:refused:untagged > lease:released:dispatch > lease:disposed-again:dispatch -> rows/CP-9/failures.md
verdict: RED exit=1
```

Expected by the plan: 1 executed, 1 failed, "baseline pin recorded before the lease dispose". Matches. The red run
retained `bin-c1430-red/` (`refused: still-present`); deleted by hand afterwards. `validate` on the red run says
`cleanup_refused`, as a red witness always does; the CHECKPOINT line above is the evidence.

Run `20261022-082753-3799` (CP-10 at the fix commit), unedited lines:

```
CHECKPOINT CP-10 commit=bceeeb21ea302cb6bfcff234d746a0de76c1cc04 build=ok filter=/*/Antiphon.Tests.Application/(DispatcherLeaseSpanTests*)|(AgentTaskWorktreeBaseCreateTests*)|(WorktreeStartRefDispatchTests*)|(DispatcherSiblingInspectionSchedulingTests*)|(TaskProgressGitTests*)|(LeaseTurnScenarioTests*)/* executed=115 passed=115 failed=0 skipped=0 trx=/work/worktrees/task-5d1d84e4/.antiphon/checkpoints/20261022-082753-3799/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=bceeeb21ea302cb6bfcff234d746a0de76c1cc04 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=286.178884s startup=60.7849468s testsWall=86.22552s teardown=3.3112447s hostWall=150.3217357s
verdict: GREEN exit=0
```

Fresh TRX per class: DispatcherLeaseSpanTests 3 (all three `C1430_*` Passed), AgentTaskWorktreeBaseCreateTests 23,
WorktreeStartRefDispatchTests 30, DispatcherSiblingInspectionSchedulingTests 47, TaskProgressGitTests 11,
LeaseTurnScenarioTests 1 = 115 (plan Min 33). Receipts: tool `validate --rows CP-10` and
`scripts/validate-checkpoint-receipt.ps1 -Rows CP-10` both print `CHECKPOINT SOURCE VALID source=bceeeb21e... rows=1`.
Evidence: `.antiphon/checkpoints/20261022-081423-a4e8/` and `.antiphon/checkpoints/20261022-082753-3799/` (ignored).
`scripts/check-evidence-diff.ps1 -BaseRef 6c1b0704b -HeadRef HEAD`: `commits=2 entries=0 violations=0`.

Unlisted builds/runs, each stated:

| What | Why | Slot |
|---|---|---|
| `dotnet build tools/Antiphon.Checkpoints -> bin-c1430-tool/` | checkpoint tool bootstrap for `import`/`start`/`wait`/`validate` | waited=195s held=24s |
| `dotnet build tests/Antiphon.Tests -> bin-c1430-shake/` at `b9230ddf8` | compile shakedown of ~250 new test lines before the closed list | waited=0s held=311s |
| `dotnet run --no-build` filter `DispatcherLeaseSpanTests/*` at `b9230ddf8` | fixture shakedown of all three new tests: total 3, failed 2 (the two ordering tests), succeeded 1 (SourceLanding), as designed | waited=240s held=76s |

All `bin-c1430-*` directories deleted (the tool deleted `bin-c1430/` itself). Repetitions of the proof selection: CP-9 once, CP-10 once.

## Invariants

- V-5: `C1430_lease_is_released_before_the_baseline_capture` (red CP-9, green CP-10), `C1430_source_landing_validation_runs_under_the_lease`
  (green), `C1430_baseline_failure_after_release_still_fails_a_code_claim` (red at the test commit in the shakedown by ordering, green CP-10).
- V-13 regression classes named for S3: green in CP-10 (counts above).
- No ordinary R-n is assigned to S3 by the plan.

## Notes for Review

- Test 2 runs on `PostLandMutationWorld.CreateAsync(provision: false)` plus `AgentTaskDispatchBaseGuardTests.CreateProvider`
  (with `runners:`, the world's runner client and `VerificationExecutionService`), not on `SiblingInspectionFixture` as the plan
  table says: that fixture has no confirmed publication, which a SourceLanding claim needs. Tests 1 and 3 are on the fixture.
- The plan's "6 existing" for `DispatcherSiblingInspectionSchedulingTests` is 8 methods / 47 rows on this base; the Min floor holds.
- The release is guarded to `claimed.Workspace == WorkspaceMode.Worktree`. A Shared claim has no cut and no capture, and the
  land's `AgentTaskLandService.FindWriterAsync` reads Dispatched Shared writers under R, so it keeps the old span through commit.
  D-4 speaks of "the cut", so this is the plan's intent made explicit.
- Residual window (D-4 accepted it; stating it): a Worktree claim now commits with R free, so a land that acquires R during the
  capture (seconds) does not yet see this claim as Dispatched. `FindWriterAsync` only matches a Worktree candidate whose worktree
  path equals the landing task's own worktree, i.e. a follow-up adopting a kept worktree while its owner is being landed.
- Observable side effect: the baseline pin's own nested acquire used to be refused under the dispatcher's R
  (`lease:refused:untagged` in the red trace); it now takes and releases a short lease per pin. That is the documented
  `PinBaselineAsync` path working as written.

## PCs pending for method-scoped SourceLanding Mutation

| PC | Mutant | Red test |
|---|---|---|
| PC-1 | delete the new `DisposeAsync` statement (old order) | `C1430_lease_is_released_before_the_baseline_capture` (demonstrated red by CP-9) |
| PC-2 | move the release above `ValidateVerificationAsync` at the dispatcher | `C1430_source_landing_validation_runs_under_the_lease` (validation refuses `repository_lease_required`) |
| PC-3 | route a Code capture failure through the non-Code swallow branch | `C1430_baseline_failure_after_release_still_fails_a_code_claim` (Dispatched instead of Failed) |
| PC-4 | drop the `Workspace == Worktree` guard | no test goes red: uncovered control, candidate for `docs/superpowers/mutations/` |

## Landability and rc

- `git merge-tree --write-tree origin/master HEAD` at `origin/master` = `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`: clean, 0 conflicts.
  CARD-1446 milestone 2 not waited on (does not touch these files).
- rc: chain simulation of this task's two commits alone onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`):
  `CLEAN b9230ddf8`, `CLEAN bceeeb21e`. The hunk anchors on `ValidateVerificationAsync(claimed, repositoryLease!, ct)`, present
  on rc (`AgentTaskDispatcher.cs:5768`); the test helpers' hooks (`SiblingInspectionFixture(configure:)`,
  `CreateProvider(runners:, configure:)`, `PostLandMutationWorld(provision:)`) exist on rc. So THIS change does not depend on
  the CARD-1427/1428/1432 chain. Not built or run on rc.

Budget: the brief allowed 45 minutes; the task took about 75 minutes wall, of which slot waits were 195 + 240 + 360 s and two
Antiphon.Tests builds about 10 minutes.
