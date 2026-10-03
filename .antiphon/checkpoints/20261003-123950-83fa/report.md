--- checkpoint report ---
run: 20261003-123950-83fa   manifest: /work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/manifest.resolved.yaml
commit: 6c50fb9c4b5c63eab445a9c702d970b8049b58c4  branch: feat/card-task-22b182d6  worktree: /work/worktrees/task-22b182d6  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 6c50fb9c4b5c63eab445a9c702d970b8049b58c4 state=clean buildSource=verified
CHECKPOINT CP-1 commit=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 build=ok filter=/*/*/(AgentTaskInputSpillTests*)|(AgentTaskRefineTests*)|(AgentTaskReplyOverlayTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=15s build=398.9300384s startup=39.2099645s testsWall=16.1526613s teardown=2.6816611s hostWall=58.0443022s
CHECKPOINT CP-2 commit=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 build=reused filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=45.5702919s testsWall=30.5822608s teardown=1.9758056s hostWall=78.1283614s
CHECKPOINT CP-3 commit=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 build=reused filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=43.2505672s testsWall=9.7868676s teardown=1.1726081s hostWall=54.2100396s
CHECKPOINT CP-7 commit=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 build=reused filter=/*/*/(ParkedMessageSweepServiceTests*)|(CapacityRecoveryCompatibilityTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=29.6959841s testsWall=21.6892538s teardown=0.9268004s hostWall=52.3120386s
CHECKPOINT CP-6 commit=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 build=reused filter=/*/*/*/*[Category=Unit] executed=3910 passed=3910 failed=0 skipped=52 trx=/work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=6c50fb9c4b5c63eab445a9c702d970b8049b58c4 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=90.7395984s testsWall=164.958729s teardown=3.5549101s hostWall=259.2532377s
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupPresentationTests 155s tests=33 (CP-6)
SLOW CLASS Antiphon.Tests.Checkpoints.PlanCoverageCommandTests 78s tests=10 (CP-6)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 71s tests=25 (CP-6)
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 70s tests=65 (CP-6)
SLOW CLASS Antiphon.Tests.Application.ChannelOutboundStorageTests 67s tests=28 (CP-6)
SLOW CLASS Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests 67s tests=4 (CP-6)
SLOW CLASS Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests 63s tests=65 (CP-6)
SLOW CLASS Antiphon.Tests.Application.SessionDeliveryProfileTests 63s tests=5 (CP-6)
unlisted: none (the tool ran no other build or test command)
wall: 15m45s  sequential-equivalent: 9m04s  builds: 1  max-concurrent-builds: 1  rows: 5 green 0 red 0 skipped
outputs: deleted bin-c965-qualified/
evidence: /work/worktrees/task-22b182d6/.antiphon/checkpoints/20261003-123950-83fa/report.md
verdict: GREEN exit=0
