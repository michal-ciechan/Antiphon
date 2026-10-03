--- checkpoint report ---
run: 20261003-121954-2168   manifest: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/manifest.resolved.yaml
commit: 0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26  branch: feat/card-task-4c1697dc  worktree: /work/worktrees/task-4c1697dc  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 state=clean buildSource=verified
CHECKPOINT CP-1 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=ok filter=/*/*/(AgentTaskInputSpillTests*)|(AgentTaskRefineTests*)|(AgentTaskReplyOverlayTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=100.0675877s startup=40.0752565s testsWall=18.4330189s teardown=4.8177282s hostWall=63.3260078s
CHECKPOINT CP-2 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=29 passed=28 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=40.3945844s testsWall=21.1281048s teardown=2.3203481s hostWall=63.8430372s
FAILED Antiphon.Tests.Application.AgentTaskInputFallbackTests.Input_body_is_absent_from_task_summary_events_and_logs (CP-2) NotSupportedException: Serialization and deserialization of 'Microsoft.AspNetCore.Http.RequestDelegate' instances is not supported. Path: $.RequestDelegate. -> rows/CP-2/failures.md
CHECKPOINT CP-3 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=38.2108318s testsWall=11.5049229s teardown=1.4778587s hostWall=51.1936135s
CHECKPOINT CP-7 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/(ParkedMessageSweepServiceTests*)|(CapacityRecoveryCompatibilityTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=41.0516644s testsWall=26.3782937s teardown=1.6003814s hostWall=69.0303394s
unlisted: none (the tool ran no other build or test command)
wall: 5m50s  sequential-equivalent: 4m09s  builds: 1  max-concurrent-builds: 1  rows: 3 green 1 red 0 skipped
outputs: kept bin-c965-qualified/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-121954-2168
evidence: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/report.md
verdict: RED exit=1
