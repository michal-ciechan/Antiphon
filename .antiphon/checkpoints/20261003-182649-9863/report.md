--- checkpoint report ---
run: 20261003-182649-9863   manifest: /work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-182649-9863/manifest.resolved.yaml
commit: 3947a6d339df9c2b1c0cf28c981da970edb90bbf  branch: feat/card-task-535715ec  worktree: /work/worktrees/task-535715ec  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3947a6d339df9c2b1c0cf28c981da970edb90bbf state=clean buildSource=verified
CHECKPOINT CP-2 commit=3947a6d339df9c2b1c0cf28c981da970edb90bbf build=ok filter=/*[Category=Unit]/*/*/(!windows_quick_row_finishes_beside_a_slow_row*)&(!windows_row_arguments_round_trip_intact*)&(!windows_chatty_row_drains_interleaved_stdout_and_stderr*)&(!windows_row_timeout_kills_the_start_b_grandchild*)&(!C665_LockedFileMidDeleteResumesOnLaterPass*)&(!C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded*) executed=3973 passed=3973 failed=0 skipped=27 trx=/work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-182649-9863/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3947a6d339df9c2b1c0cf28c981da970edb90bbf sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=217.5385352s startup=145.6988595s testsWall=235.5929196s teardown=2.4228556s hostWall=383.7146321s
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 110s tests=84 (CP-2)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 66s tests=25 (CP-2)
unlisted: none (the tool ran no other build or test command)
wall: 10m03s  sequential-equivalent: 6m24s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1005-final/, bin-c1005-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-182649-9863
evidence: /work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-182649-9863/report.md
verdict: RED exit=3
