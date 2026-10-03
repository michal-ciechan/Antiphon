--- checkpoint report ---
run: 20261003-115340-1393   manifest: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/manifest.resolved.yaml
commit: 93b76ea56a640074b5df342fce6574b087691907  branch: feat/card-task-4c1697dc  worktree: /work/worktrees/task-4c1697dc  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 93b76ea56a640074b5df342fce6574b087691907 state=clean buildSource=verified
CHECKPOINT CP-6 commit=93b76ea56a640074b5df342fce6574b087691907 build=ok filter=/*/*/*/*[Category=Unit] executed=3910 passed=3910 failed=0 skipped=52 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=93b76ea56a640074b5df342fce6574b087691907 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=100.2197687s startup=88.1838164s testsWall=139.4583704s teardown=2.3820288s hostWall=230.0242337s
SLOW CLASS Antiphon.Tests.Application.WorktreeCleanupPresentationTests 165s tests=33 (CP-6)
SLOW CLASS Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests 80s tests=65 (CP-6)
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 64s tests=25 (CP-6)
CHECKPOINT CP-7 commit=93b76ea56a640074b5df342fce6574b087691907 build=ok filter=/*/*/(ParkedMessageSweepServiceTests*)|(CapacityRecoveryCompatibilityTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=93b76ea56a640074b5df342fce6574b087691907 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=124.8569721s startup=49.3993144s testsWall=26.6304913s teardown=1.2559064s hostWall=77.285712s
unlisted: none (the tool ran no other build or test command)
wall: 8m54s  sequential-equivalent: 5m08s  builds: 5  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: deleted bin-c965-input/, bin-c965-fallback/, bin-c965-attention/, bin-c965-unit/, bin-c965-compat/
evidence: /work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/report.md
verdict: GREEN exit=0
