--- checkpoint report ---
run: 20261003-105757-9594   manifest: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/manifest.resolved.yaml
commit: 39ae60c11965d9a1e9f66a502f4671a161dc7bfa  branch: feat/card-task-3eceba1b  worktree: /work/worktrees/task-3eceba1b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 39ae60c11965d9a1e9f66a502f4671a161dc7bfa state=clean buildSource=verified
CHECKPOINT CP-4 commit=39ae60c11965d9a1e9f66a502f4671a161dc7bfa build=ok filter=/*/*/Checkpoint*UsageTests*/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=39ae60c11965d9a1e9f66a502f4671a161dc7bfa sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=96.1256785s startup=4.9996719s testsWall=18.0080992s teardown=0.5258944s hostWall=23.5336904s
CHECKPOINT CP-5 commit=39ae60c11965d9a1e9f66a502f4671a161dc7bfa build=ok filter=/*/*/*/*[Category=Unit] executed=3911 passed=3911 failed=0 skipped=52 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=39ae60c11965d9a1e9f66a502f4671a161dc7bfa sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=151.6287156s startup=98.7087955s testsWall=147.2423582s teardown=3.1906116s hostWall=249.1417655s
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupPresentationTests 157s tests=33 (CP-5)
SLOW CLASS Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests 88s tests=65 (CP-5)
SLOW CLASS Antiphon.Tests.Application.ChannelOutboundStorageTests 69s tests=28 (CP-5)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 68s tests=25 (CP-5)
SLOW CLASS Antiphon.Tests.Application.SessionDeliveryProfileTests 62s tests=5 (CP-5)
unlisted: none (the tool ran no other build or test command)
wall: 8m42s  sequential-equivalent: 4m33s  builds: 5  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: kept bin-c998-tests-red/, bin-c998-final/, bin-c998-unit/, bin-c998-repair/, bin-c998-unit-repair/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-105757-9594
evidence: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/report.md
verdict: GREEN exit=0
