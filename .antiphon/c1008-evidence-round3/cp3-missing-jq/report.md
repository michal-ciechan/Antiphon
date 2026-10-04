--- checkpoint report ---
run: 20261003-175056-b40e   manifest: /work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-175056-b40e/manifest.resolved.yaml
commit: 5bd903e34d6857e0cad31a99d1c54d14c7c9b0fe  branch: feat/card-task-3aebd909  worktree: /work/worktrees/task-3aebd909  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 5bd903e34d6857e0cad31a99d1c54d14c7c9b0fe state=clean buildSource=verified
CHECKPOINT CP-3 commit=5bd903e34d6857e0cad31a99d1c54d14c7c9b0fe build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*)|(C944_*)|(C951_*)|(C976_*)|(C946_*)|(C957_*) executed=35 passed=34 failed=1 skipped=19 trx=/work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-175056-b40e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=5bd903e34d6857e0cad31a99d1c54d14c7c9b0fe sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=99.3797504s startup=4.8465434s testsWall=54.118774s teardown=0.6457129s hostWall=59.6110486s
FAILED Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance (CP-3) ShouldAssertException: run.Output     should contain (case insensitive comparison) "RunnerBusy"     but was actually "DIAGNOSIS=RecycleToolsMissing " -> rows/CP-3/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 2m40s  sequential-equivalent: 1m00s  builds: 5  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-175056-b40e
evidence: /work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-175056-b40e/report.md
verdict: RED exit=1
