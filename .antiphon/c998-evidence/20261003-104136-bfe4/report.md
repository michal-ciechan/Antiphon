--- checkpoint report ---
run: 20261003-104136-bfe4   manifest: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/manifest.resolved.yaml
commit: af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9  branch: feat/card-task-3eceba1b  worktree: /work/worktrees/task-3eceba1b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 state=clean buildSource=verified
CHECKPOINT CP-1 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/CheckpointTempUsageTests*/*census* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=120.2752333s startup=4.950978s testsWall=14.6796226s teardown=0.8345173s hostWall=20.4651292s
CHECKPOINT CP-2 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/CheckpointTempUsageTests*/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=182.4755146s startup=10.3391369s testsWall=27.7898034s teardown=1.1579373s hostWall=39.2868778s
CHECKPOINT CP-3 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/*/*[Category=Unit] executed=3911 passed=3910 failed=1 skipped=52 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=133.8065128s startup=115.6882997s testsWall=140.6444102s teardown=3.677204s hostWall=260.0099142s
FAILED Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration (CP-3) ShouldAssertException: methodBoth     should be empty but had 1     item and was ["tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs::CheckpointTempUsageTests (Integration class has a Unit method)"]  Additional Info:     methods that inherit one lane and declare the other: tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs::CheckpointTempUsageTests (Integration class has a Unit method) -> rows/CP-3/failures.md
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupPresentationTests 129s tests=33 (CP-3)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 75s tests=25 (CP-3)
SLOW CLASS Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests 62s tests=65 (CP-3)
unlisted: none (the tool ran no other build or test command)
wall: 12m42s  sequential-equivalent: 5m23s  builds: 3  max-concurrent-builds: 1  rows: 2 green 1 red 0 skipped
outputs: kept bin-c998-tests-red/, bin-c998-final/, bin-c998-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-104136-bfe4
evidence: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/report.md
verdict: RED exit=1
