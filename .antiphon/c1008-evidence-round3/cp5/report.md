--- checkpoint report ---
run: 20261003-180253-e063   manifest: /work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-180253-e063/manifest.resolved.yaml
commit: cc6a8914e385c80b62c9f457dd716354006cddfb  branch: feat/card-task-3aebd909  worktree: /work/worktrees/task-3aebd909  host: Debian GNU/Linux 12 (bookworm) cores=24
source: cc6a8914e385c80b62c9f457dd716354006cddfb state=clean buildSource=verified
CHECKPOINT CP-5 commit=cc6a8914e385c80b62c9f457dd716354006cddfb build=ok filter=/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-180253-e063/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=cc6a8914e385c80b62c9f457dd716354006cddfb sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=160.5101026s startup=2.4743266s testsWall=531.3179048s teardown=0.7496554s hostWall=534.5418971s
SLOW CLASS Antiphon.Tests.Scripts.RollingVolumeRecycleDockerTests 531s tests=1 (CP-5)
unlisted: none (the tool ran no other build or test command)
wall: 11m37s  sequential-equivalent: 8m55s  builds: 5  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-180253-e063
evidence: /work/worktrees/task-3aebd909/.antiphon/checkpoints/20261003-180253-e063/report.md
verdict: GREEN exit=0
