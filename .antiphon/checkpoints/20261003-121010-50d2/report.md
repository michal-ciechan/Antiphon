--- checkpoint report ---
run: 20261003-121010-50d2   manifest: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/manifest.resolved.yaml
commit: 1462102cb4e88dea9c42223a554f8b9dd67ed0cf  branch: feat/card-task-4c1697dc  worktree: /work/worktrees/task-4c1697dc  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 1462102cb4e88dea9c42223a554f8b9dd67ed0cf state=clean buildSource=verified
CHECKPOINT CP-2 commit=1462102cb4e88dea9c42223a554f8b9dd67ed0cf build=ok filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=29 passed=28 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=1462102cb4e88dea9c42223a554f8b9dd67ed0cf sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=159.2015854s startup=37.4123395s testsWall=21.8093483s teardown=1.3309314s hostWall=60.552644s
FAILED Antiphon.Tests.Application.AgentTaskInputFallbackTests.Input_body_is_absent_from_task_summary_events_and_logs (CP-2) NotSupportedException: Serialization and deserialization of 'System.Reflection.RuntimeMethodInfo' instances is not supported. The unsupported member type is located on type 'System.Object'. Path: $.Metadata. -> rows/CP-2/failures.md
CHECKPOINT CP-3 commit=1462102cb4e88dea9c42223a554f8b9dd67ed0cf build=ok filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=1462102cb4e88dea9c42223a554f8b9dd67ed0cf sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=101.5130819s startup=40.5387697s testsWall=13.2634412s teardown=1.3944662s hostWall=55.1966772s
unlisted: none (the tool ran no other build or test command)
wall: 6m18s  sequential-equivalent: 1m56s  builds: 5  max-concurrent-builds: 1  rows: 1 green 1 red 0 skipped
outputs: kept bin-c965-input/, bin-c965-fallback/, bin-c965-attention/, bin-c965-unit/, bin-c965-compat/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-121010-50d2
evidence: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/report.md
verdict: RED exit=1
