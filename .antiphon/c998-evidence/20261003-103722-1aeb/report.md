--- checkpoint report ---
run: 20261003-103722-1aeb   manifest: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-103722-1aeb/manifest.resolved.yaml
commit: 76d7cf19780bdfccae858d718290d440a18b69d2  branch: feat/card-task-3eceba1b  worktree: /work/worktrees/task-3eceba1b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 76d7cf19780bdfccae858d718290d440a18b69d2 state=clean buildSource=verified
CHECKPOINT CP-1 commit=76d7cf19780bdfccae858d718290d440a18b69d2 build=ok filter=/*/*/CheckpointTempUsageTests*/*census* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-103722-1aeb/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=76d7cf19780bdfccae858d718290d440a18b69d2 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=175.0261421s startup=3.5708528s testsWall=13.3202915s teardown=1.0713485s hostWall=17.962513s
FAILED Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census (CP-1) ShouldAssertException: UsageLibrary.Errors(result.RootElement, "native")     should be empty but had 1     item and was ["full roster checkpoint cases=349 expected=290"]  Additional Info:     the current compiled checkpoint roster clears the Full census and floor -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster (CP-1) ShouldAssertException: UsageLibrary.Errors(result.RootElement, "native")     should be empty but had 1     item and was ["namespace census selected=349 expected=290 executed=331 skipped=18"] -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_matches_compiled_checkpoint_cases (CP-1) ShouldAssertException: result.RootElement.GetProperty("selected").GetInt32()     should be 349     but was 290  Additional Info:     scripts/lib/checkpoint-usage.ps1 Get-NamespaceCensus is stale against the compiled checkpoint cases -> rows/CP-1/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 3m15s  sequential-equivalent: 0m19s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c998-tests-red/, bin-c998-final/, bin-c998-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-103722-1aeb
evidence: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-103722-1aeb/report.md
verdict: RED exit=1
