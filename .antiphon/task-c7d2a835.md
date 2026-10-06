# CARD-1076 S1 Code report (task c7d2a835)

Outcome: S1 (D-1 per-call `LandingGitRunOptions`, D-3 live `remote-prep-push` journal that does not fence) is implemented and the closed checkpoint is green on Linux. Tested source `c4eee157b38f9a5ee07ae621b860e1613bae07ab`. Branch `feat/card-task-c7d2a835`. Worktree `/work/worktrees/task-c7d2a835`. Landing owner / original Code task `c7d2a835`. Base `1d3508b9cfb31240b11ac101e788c6893804dd5e`. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`. restart: none (library change, not activated).

## Commits

- `d0320f5a4df40ca582c0bff4ee6ed7e36c427ddb` CARD-1076 S1: per-call git budget and a live remote-prep push that does not fence
- `c4eee157b38f9a5ee07ae621b860e1613bae07ab` CARD-1076 S1: count the two pre-existing Windows lease skips in CP-1

S2–S5 are not started. `Delegation:RemotePrepPushBudgetMinutes`, the per-repository push gate, lease-free re-arm, and pipeline reasons stay in those slices.

## Checkpoint (run 20261006-065448-cf9c)

Outer lease `55603a54-dcaf-4c01-9915-4674488459ba` waited=0s held=229s maxcpucount=6. The tool's implicit bootstrap build is the explained unlisted build (dotnet run is not given -maxcpucount). Tool line: `unlisted: none (the tool ran no other build or test command)`. Verdict GREEN exit=0. Wall 3m41s. Host Debian GNU/Linux 12. sourceState=clean buildSource=verified.

```
CHECKPOINT CP-1 commit=c4eee157b38f9a5ee07ae621b860e1613bae07ab build=ok filter=/*/*/(RepositoryMutationLeaseTests*)|(RepositoryMutationLeaseDescribeTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryFenceObserverTests*)/* executed=29 passed=29 failed=0 skipped=2 trx=/work/worktrees/task-c7d2a835/.antiphon/checkpoints/20261006-065448-cf9c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c4eee157b38f9a5ee07ae621b860e1613bae07ab sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=134.1332289s startup=42.4885723s testsWall=16.9337598s teardown=1.134269s hostWall=60.5566178s
CHECKPOINT CP-2 commit=c4eee157b38f9a5ee07ae621b860e1613bae07ab build=reused filter=/*/*/LandingGitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-c7d2a835/.antiphon/checkpoints/20261006-065448-cf9c/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=c4eee157b38f9a5ee07ae621b860e1613bae07ab sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=3.9566081s testsWall=20.6431911s teardown=0.6337464s hostWall=25.2335456s
```

TRX counters: CP-1 total=31 executed=29 passed=29 failed=0 notExecuted=2. CP-2 total=57 executed=57 passed=57 failed=0 notExecuted=0. The two NotExecuted rows are the pre-existing Windows skips `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` and `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`.

CP-1 Min in the plan is `29 linux / 31 windows` because those two `SkipTestException` tests are NotExecuted on Linux. The Windows floor stays 31. Assertions were not loosened. That plan edit is commit `c4eee157b` and is the source the checkpoint verified.

## Ordinary V/R for S1

| ID | Outcome |
|---|---|
| V-1 `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` | Passed (CP-2, 2.48s) |
| V-2 `C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive` | Passed (CP-2, 8.11s) |
| V-3 `C1076_LiveTaggedPrepPushChildDoesNotFenceButDeadReusedOrUnknownStillDoes` alive, exited, reused, unknown | Passed (CP-1, four arms) |
| V-4 `C1076_a_tagged_record_reports_its_purpose_and_task` | Passed (CP-1) |
| R-2 lease class existing rows | Passed inside CP-1 (the two skips are the Windows-only rows above) |
| R-3 `LandingGitTests` existing 55 | Passed inside CP-2 (57 = 55 + V-1 + V-2) |
| R-8 inspector 6 + V-4, describe 1, fence 1 | Passed inside CP-1 |

V-5..V-15 and R-1, R-4..R-7, R-9..R-12 belong to S2–S5 and were not run. They are not marked passed.

The first recorded execution of the new tests was green after `d0320f5a4`. A separate red TRX was not captured. The tests assert `git_timeout`, lease admission, and purpose/task fields. PC-1..PC-3 are the Mutation red witnesses.

## Registry guard (unlisted)

Brief requires `/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*`. Not a checkpoint row. Lease `658df9c7-689d-4d59-9603-f45f48a792b8` waited=0s held=137s. TRX `.antiphon/c1076-guard/guard.trx`. total 3, passed 3, failed 0, skipped 0, duration 3s 229ms: `Registry_matches_compiled_metadata`, `unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, `allowlist_file_exists_and_has_spawn_lane_entries`. `slow-tests-allowlist.txt` was not edited.

Earlier unlisted pre-runs against `d0320f5a4`, superseded by the checkpoint: LandingGitTests 57/57; lease class 20 passed + 2 skipped; inspector class 7/7. One earlier build failed before tests because `UseAppHost=false` was omitted (fakeclaude apphost collision). The checkpoint tool adds that property itself.

## Evidence

`scripts/check-evidence-diff.ps1 -BaseRef 1d3508b9cfb31240b11ac101e788c6893804dd5e -HeadRef c4eee157b38f9a5ee07ae621b860e1613bae07ab` exit 0: commits=2 entries=0 violations=0. Re-run after this report commit is in the caller summary.

## Positive controls pending for Mutation

PC-1 `HasUnfinishedAsync` ignore Purpose (V-3 alive admitted → refused). PC-2 skip any tagged record regardless of liveness (V-3 exited and reused refused → admitted). PC-3 `ExecuteProcessAsync` always DefaultBudget (V-1, no `git_timeout` inside the bound). PC-4..PC-12 stay pending for later slices. Mutation was not run.

## Behaviour shipped

`LandingGitRunOptions` and `RepositoryChildTag` on `ILandingGit`; default interface method forwards to the three-argument `RunAsync`. `LandingGit.DefaultBudget` is 5 minutes. A caller budget replaces it only when set. The read-only byte batch stays at 5 minutes. A live well-formed record whose purpose is `remote-prep-push` does not fence. Dead, reused, unknown, completed, torn, unreadable, and live untagged records still fence. A tagged fence sentence names purpose and task (`N` guid). Untagged fences keep the previous sentence. Recovery script appends ` purpose=<p> task=<id>` on the retained line when the record has them. Schema stays 1.
