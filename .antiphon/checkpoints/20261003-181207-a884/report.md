--- checkpoint report ---
run: 20261003-181207-a884   manifest: /work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-181207-a884/manifest.resolved.yaml
commit: 740c9b570f4361ff5baad2c863140a23a9712973  branch: feat/card-task-535715ec  worktree: /work/worktrees/task-535715ec  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 740c9b570f4361ff5baad2c863140a23a9712973 state=clean buildSource=verified
CHECKPOINT CP-2 commit=740c9b570f4361ff5baad2c863140a23a9712973 build=ok filter=/*[Category=Unit]/*/*/(!windows_quick_row_finishes_beside_a_slow_row*)&(!windows_row_arguments_round_trip_intact*)&(!windows_chatty_row_drains_interleaved_stdout_and_stderr*)&(!windows_row_timeout_kills_the_start_b_grandchild*)&(!C665_LockedFileMidDeleteResumesOnLaterPass*)&(!C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded*) executed=3954 passed=3954 failed=0 skipped=46 trx=/work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-181207-a884/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=740c9b570f4361ff5baad2c863140a23a9712973 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=129.4762276s startup=172.4246167s testsWall=191.5815567s teardown=4.3347049s hostWall=368.3411638s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 81s tests=25 (CP-2)
unlisted: none (the tool ran no other build or test command)
wall: 8m21s  sequential-equivalent: 6m09s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1005-final/, bin-c1005-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-181207-a884
evidence: /work/worktrees/task-535715ec/.antiphon/checkpoints/20261003-181207-a884/report.md
verdict: RED exit=3
